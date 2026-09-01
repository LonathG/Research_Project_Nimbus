using System;
using System.Collections;
using System.IO.Ports;
using UnityEngine;

/// <summary>
/// Controls 3 physical wind simulation fans via Arduino on COM15 (with auto-detect support).
/// Dynamically scales fan RPM as long as the VR Rig moves forward.
/// </summary>
public class FanSerialController : MonoBehaviour
{
    public static FanSerialController Instance { get; private set; }

    [Header("Serial Port Settings (Arduino Fans)")]
    [Tooltip("Target COM port where the Arduino Fan controller is connected (e.g. COM12).")]
    public string portName = "COM12";
    [Tooltip("If enabled, automatically scans other COM ports ONLY if the specified port fails (excluding Broom port).")]
    public bool autoDetectPort = false;
    public int baudRate = 9600;
    public bool enableSerial = true;

    [Header("Speed Reference & Sources")]
    [Tooltip("Optional direct reference to BroomFlightController. If null, automatically located in scene.")]
    public BroomFlightController flightController;
    [Tooltip("VR Rig root transform to track physical world velocity if BroomFlightController is absent.")]
    public Transform rigTarget;
    [Tooltip("VR Camera transform used for directional wind bias based on head roll / turning.")]
    public Transform vrCamera;

    [Header("Wind Calibration & Physics")]
    [Tooltip("Speed in m/s that corresponds to maximum fan speed (255 PWM).")]
    public float maxSpeedForFullWind = 25.0f;
    [Tooltip("Minimum speed threshold (m/s) before fans start blowing.")]
    public float minSpeedThreshold = 0.15f;
    [Tooltip("Minimum PWM sent to fan when moving (helps fans overcome static motor friction).")]
    [Range(0, 100)]
    public int minFanStartPWM = 40;
    [Tooltip("Maximum PWM cap (0-255).")]
    [Range(50, 255)]
    public int maxFanPWM = 255;
    [Tooltip("Response smoothing speed for accelerating/decelerating fan RPM.")]
    public float windSmoothingFactor = 10.0f;

    [Header("Directional Wind (Spatial Simulation)")]
    [Tooltip("Bias wind towards the left or right fan when user rolls/tilts head or turns.")]
    public bool enableDirectionalWind = true;
    [Tooltip("Tilt angle (degrees) for maximum side fan bias.")]
    public float maxTiltAngle = 20.0f;
    [Tooltip("Intensity of directional wind side difference (0 = pure frontal, 1 = intense side bias).")]
    [Range(0f, 1f)]
    public float directionalIntensity = 0.6f;

    [Header("Transmission Optimization")]
    [Tooltip("Interval (seconds) between serial packets (e.g. 0.05s = 20Hz).")]
    public float updateInterval = 0.05f;
    [Tooltip("Maximum time without sending a packet before a heartbeat packet is forced (keeps Arduino watchdog alive).")]
    public float heartbeatInterval = 0.4f;

    [Header("Debug & Telemetry")]
    public bool showDebugLogs = false;
    public bool showOnScreenHUD = true;

    // Internal State
    private SerialPort serialPort;
    private string activePort = "";
    private bool isConnecting = false;
    private float smoothedForwardSpeed = 0f;
    private float lastSendTime = 0f;
    private float lastSuccessfulTransmitTime = 0f;

    private int currentLeftPWM = 0;
    private int currentMiddlePWM = 0;
    private int currentRightPWM = 0;
    private int lastSentLeft = -1;
    private int lastSentMiddle = -1;
    private int lastSentRight = -1;

    private Vector3 lastRigPosition;
    private CharacterController characterController;
    private Rigidbody rigRigidbody;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);
    }

    void Start()
    {
        // 1. Resolve references
        if (flightController == null)
        {
            flightController = FindObjectOfType<BroomFlightController>();
        }

        if (vrCamera == null)
        {
            if (flightController != null && flightController.vrCamera != null)
            {
                vrCamera = flightController.vrCamera;
            }
            else if (Camera.main != null)
            {
                vrCamera = Camera.main.transform;
            }
            else
            {
                vrCamera = transform;
            }
        }

        if (rigTarget == null)
        {
            if (flightController != null && flightController.rigTarget != null)
            {
                rigTarget = flightController.rigTarget;
            }
            else if (vrCamera != null && vrCamera.parent != null)
            {
                rigTarget = vrCamera.parent;
            }
            else
            {
                rigTarget = transform;
            }
        }

        if (rigTarget != null)
        {
            lastRigPosition = rigTarget.position;
            characterController = rigTarget.GetComponent<CharacterController>();
            rigRigidbody = rigTarget.GetComponent<Rigidbody>();
        }

        // 2. Initialize Serial Connection (Tries COM15 first, scans if needed)
        if (enableSerial)
        {
            StartCoroutine(AutoConnectLoop());
        }
    }

    /// <summary>
    /// Background routine that ensures the serial connection is established and stays alive.
    /// </summary>
    private IEnumerator AutoConnectLoop()
    {
        while (enableSerial)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                yield return StartCoroutine(DetectAndOpenSerialPort());
            }

            // Check connection health every 3 seconds
            yield return new WaitForSeconds(3.0f);
        }
    }

    private IEnumerator DetectAndOpenSerialPort()
    {
        if (isConnecting) yield break;
        isConnecting = true;

        // 1. First priority: Open user-specified port (e.g. COM12)
        if (!string.IsNullOrEmpty(portName) && !portName.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            if (TryOpenPort(portName))
            {
                activePort = portName;
                isConnecting = false;
                yield break;
            }
        }

        if (!autoDetectPort)
        {
            isConnecting = false;
            yield break;
        }

        // 2. Auto-detect across candidate ports (ignoring the broom port if known)
        string broomPort = (flightController != null && !string.IsNullOrEmpty(flightController.portName)) ? flightController.portName : "COM6";
        string[] availablePorts = SerialPort.GetPortNames();

        if (availablePorts == null || availablePorts.Length == 0)
        {
            if (showDebugLogs) Debug.LogWarning("[FanSerialController] No COM ports found. Will retry...");
            isConnecting = false;
            yield break;
        }

        // Multiple ports: Probe each candidate port with handshake (skipping the broom's port)
        foreach (string candidatePort in availablePorts)
        {
            if (candidatePort.Equals(broomPort, StringComparison.OrdinalIgnoreCase))
            {
                continue; // Do not touch the broom controller's port!
            }

            if (TryProbePortForFans(candidatePort))
            {
                activePort = candidatePort;
                portName = candidatePort;
                Debug.Log($"[FanSerialController] Auto-detected Fan Arduino on {candidatePort}!");
                isConnecting = false;
                yield break;
            }
            yield return new WaitForSeconds(0.05f);
        }

        isConnecting = false;
    }

    private bool TryProbePortForFans(string port)
    {
        SerialPort testPort = null;
        try
        {
            testPort = new SerialPort(port, baudRate);
            testPort.ReadTimeout = 150;
            testPort.WriteTimeout = 100;
            testPort.DtrEnable = true;
            testPort.RtsEnable = true;
            testPort.Open();

            // Send ping
            testPort.Write("PING\n");

            // Read response
            float startTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startTime < 0.25f)
            {
                if (testPort.BytesToRead > 0)
                {
                    string reply = testPort.ReadLine();
                    if (!string.IsNullOrEmpty(reply) && (reply.Contains("VR_FAN") || reply.Contains("READY") || reply.Contains("PWM")))
                    {
                        serialPort = testPort;
                        return true;
                    }
                }
            }

            testPort.Close();
            testPort.Dispose();
        }
        catch (Exception)
        {
            if (testPort != null)
            {
                try 
                { 
                    if (testPort.IsOpen) testPort.Close(); 
                    testPort.Dispose(); 
                } 
                catch { }
            }
        }
        return false;
    }

    private bool TryOpenPort(string port)
    {
        try
        {
            CloseSerialConnection();

            serialPort = new SerialPort(port, baudRate)
            {
                WriteTimeout = 50,
                ReadTimeout = 50
            };
            serialPort.Open();

            try
            {
                serialPort.DtrEnable = true;
                serialPort.RtsEnable = true;
            }
            catch { }

            Debug.Log($"[FanSerialController] Successfully opened Fan serial connection on {port} ({baudRate} baud).");
            return true;
        }
        catch (Exception e)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[FanSerialController] Could not open {port}: {e.Message}");
            }
            CloseSerialConnection();
            return false;
        }
    }

    void Update()
    {
        // 1. Calculate VR Rig Forward Movement Speed
        CalculateRigForwardSpeed();

        // 2. Compute 3-Fan PWM Levels based on Forward Speed & Head Tilt
        ComputeFanPWMs();

        // 3. Transmit to Arduino over Serial (periodic / heartbeat)
        if (Time.time - lastSendTime >= updateInterval)
        {
            SendFanPWMData();
            lastSendTime = Time.time;
        }
    }

    /// <summary>
    /// Accurately detects forward movement of the VR Rig from all available sources.
    /// </summary>
    private void CalculateRigForwardSpeed()
    {
        float targetSpeed = 0f;

        // Source 1: BroomFlightController
        if (flightController != null && flightController.enabled)
        {
            targetSpeed = Mathf.Max(targetSpeed, flightController.CurrentSpeed);
        }

        // Source 2: Physical CharacterController / Rigidbody forward velocity
        Vector3 forwardFlat = Vector3.forward;
        if (vrCamera != null)
        {
            forwardFlat = Vector3.ProjectOnPlane(vrCamera.forward, Vector3.up).normalized;
        }
        else if (rigTarget != null)
        {
            forwardFlat = Vector3.ProjectOnPlane(rigTarget.forward, Vector3.up).normalized;
        }

        if (characterController != null && characterController.enabled)
        {
            float ccForward = Vector3.Dot(characterController.velocity, forwardFlat);
            if (ccForward > targetSpeed) targetSpeed = ccForward;
        }
        else if (rigRigidbody != null && !rigRigidbody.isKinematic)
        {
            float rbForward = Vector3.Dot(rigRigidbody.linearVelocity, forwardFlat);
            if (rbForward > targetSpeed) targetSpeed = rbForward;
        }

        // Source 3: Transform Position Delta (pure forward displacement per frame)
        if (rigTarget != null && Time.deltaTime > 0.0001f)
        {
            Vector3 worldDelta = rigTarget.position - lastRigPosition;
            lastRigPosition = rigTarget.position;

            float deltaForwardSpeed = Vector3.Dot(worldDelta, forwardFlat) / Time.deltaTime;
            if (deltaForwardSpeed > targetSpeed)
            {
                targetSpeed = deltaForwardSpeed;
            }
        }

        // Source 4: Testing Fallback - Keyboard W / Up Arrow
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            if (targetSpeed < 10f) targetSpeed = 15f; // Simulated test flight forward speed
        }

        // Clamp to positive forward speed (moving backwards or standing still produces 0 wind)
        targetSpeed = Mathf.Max(0f, targetSpeed);

        // Smooth speed transition
        smoothedForwardSpeed = Mathf.Lerp(smoothedForwardSpeed, targetSpeed, windSmoothingFactor * Time.deltaTime);
    }

    private void ComputeFanPWMs()
    {
        if (smoothedForwardSpeed < minSpeedThreshold)
        {
            currentLeftPWM = 0;
            currentMiddlePWM = 0;
            currentRightPWM = 0;
            return;
        }

        // Normalize speed to 0.0 - 1.0 ratio
        float speedRatio = Mathf.Clamp01((smoothedForwardSpeed - minSpeedThreshold) / Mathf.Max(0.01f, maxSpeedForFullWind - minSpeedThreshold));

        // Base PWM scaling from minFanStartPWM to maxFanPWM
        int basePWM = Mathf.RoundToInt(Mathf.Lerp(minFanStartPWM, maxFanPWM, speedRatio));

        float leftFactor = 1.0f;
        float rightFactor = 1.0f;

        // Directional Wind calculation based on Head Tilt (Z-Roll)
        if (enableDirectionalWind && vrCamera != null)
        {
            float tiltZ = vrCamera.localEulerAngles.z;
            if (tiltZ > 180f) tiltZ -= 360f;

            // Tilt to left (positive) -> more wind from left side
            float normalizedTilt = Mathf.Clamp(tiltZ / maxTiltAngle, -1.0f, 1.0f);

            if (normalizedTilt > 0.05f) // Tilting Left
            {
                leftFactor = 1.0f + (normalizedTilt * directionalIntensity);
                rightFactor = 1.0f - (normalizedTilt * directionalIntensity * 0.5f);
            }
            else if (normalizedTilt < -0.05f) // Tilting Right
            {
                rightFactor = 1.0f + (Mathf.Abs(normalizedTilt) * directionalIntensity);
                leftFactor = 1.0f - (Mathf.Abs(normalizedTilt) * directionalIntensity * 0.5f);
            }
        }

        currentMiddlePWM = basePWM;
        currentLeftPWM = Mathf.Clamp(Mathf.RoundToInt(basePWM * leftFactor), 0, maxFanPWM);
        currentRightPWM = Mathf.Clamp(Mathf.RoundToInt(basePWM * rightFactor), 0, maxFanPWM);
    }

    private void SendFanPWMData()
    {
        bool valueChanged = (currentLeftPWM != lastSentLeft ||
                             currentMiddlePWM != lastSentMiddle ||
                             currentRightPWM != lastSentRight);

        bool needsHeartbeat = (Time.time - lastSuccessfulTransmitTime >= heartbeatInterval && (currentLeftPWM > 0 || currentMiddlePWM > 0 || currentRightPWM > 0));

        // Only transmit if values changed or if heartbeat interval elapsed
        if (!valueChanged && !needsHeartbeat)
        {
            return;
        }

        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                // Packet format: "L,M,R\n" (e.g. "120,255,120\n")
                string packet = $"{currentLeftPWM},{currentMiddlePWM},{currentRightPWM}\n";
                serialPort.Write(packet);

                lastSentLeft = currentLeftPWM;
                lastSentMiddle = currentMiddlePWM;
                lastSentRight = currentRightPWM;
                lastSuccessfulTransmitTime = Time.time;

                if (showDebugLogs)
                {
                    Debug.Log($"[FanSerialController] Sent -> {packet.Trim()} (Forward Speed: {smoothedForwardSpeed:F1} m/s)");
                }
            }
            catch (Exception e)
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"[FanSerialController] Serial Send Error: {e.Message}");
                }
            }
        }
    }

    // Public API for external trigger or direct override
    public void SetFanSpeedDirect(int pwm)
    {
        currentLeftPWM = Mathf.Clamp(pwm, 0, maxFanPWM);
        currentMiddlePWM = currentLeftPWM;
        currentRightPWM = currentLeftPWM;
        SendFanPWMData();
    }

    public void StopAllFans()
    {
        currentLeftPWM = 0;
        currentMiddlePWM = 0;
        currentRightPWM = 0;
        SendFanPWMData();
    }

    void OnGUI()
    {
        if (!showOnScreenHUD) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.fontSize = 12;
        boxStyle.normal.textColor = Color.white;

        string displayPort = string.IsNullOrEmpty(activePort) ? (string.IsNullOrEmpty(portName) ? "AUTO" : portName) : activePort;
        bool isConnected = serialPort != null && serialPort.IsOpen;

        GUILayout.BeginArea(new Rect(Screen.width - 320, 15, 305, 185), boxStyle);
        GUILayout.Label("<b>== VR FAN CONTROLLER ==</b>");
        GUILayout.Label($"Port: <b>{displayPort}</b> | Status: {(isConnected ? "<color=#00FF00>CONNECTED</color>" : "<color=#FF5555>SCANNING / CLOSED</color>")}");
        GUILayout.Label($"Rig Forward Speed: <b>{smoothedForwardSpeed:F1} m/s</b>");
        GUILayout.Label($"Middle Fan (Pin 9):  <b>{currentMiddlePWM}</b> / 255");
        GUILayout.Label($"Left Fan   (Pin 10): <b>{currentLeftPWM}</b> / 255");
        GUILayout.Label($"Right Fan  (Pin 11): <b>{currentRightPWM}</b> / 255");
        GUILayout.EndArea();
    }

    void OnDisable()
    {
        StopAllFans();
        CloseSerialConnection();
    }

    void OnDestroy()
    {
        CloseSerialConnection();
    }

    void OnApplicationQuit()
    {
        CloseSerialConnection();
    }

    private void CloseSerialConnection()
    {
        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    // Send zero packet before closing
                    try { serialPort.Write("0,0,0\n"); } catch { }
                    serialPort.Close();
                    Debug.Log($"[FanSerialController] Serial connection safely closed.");
                }
            }
            catch (Exception) { }
            try
            {
                serialPort.Dispose();
            }
            catch (Exception) { }
            finally
            {
                serialPort = null;
            }
        }
    }
}
