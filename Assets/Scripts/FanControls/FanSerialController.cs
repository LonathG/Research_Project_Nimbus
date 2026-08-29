using System;
using System.IO.Ports;
using UnityEngine;

/// <summary>
/// Controls 3 physical wind simulation fans via Arduino on COM12.
/// Dynamically scales fan RPM based on VR Rig forward movement speed and head turning/tilt.
/// </summary>
public class FanSerialController : MonoBehaviour
{
    public static FanSerialController Instance { get; private set; }

    [Header("Serial Port Settings (Arduino Fans)")]
    [Tooltip("COM Port where Arduino Fan controller is connected.")]
    public string portName = "COM12";
    public int baudRate = 115200;
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
    public float minSpeedThreshold = 0.2f;
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
    [Tooltip("Interval (seconds) between serial packets to prevent buffer overflow (e.g. 0.05s = 20Hz).")]
    public float updateInterval = 0.05f;

    [Header("Debug & Telemetry")]
    public bool showDebugLogs = false;
    public bool showOnScreenHUD = true;

    // Internal State
    private SerialPort serialPort;
    private float currentForwardSpeed = 0f;
    private float smoothedForwardSpeed = 0f;
    private float lastSendTime = 0f;

    private int currentLeftPWM = 0;
    private int currentMiddlePWM = 0;
    private int currentRightPWM = 0;
    private int lastSentLeft = -1;
    private int lastSentMiddle = -1;
    private int lastSentRight = -1;

    private Vector3 lastRigPosition;

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

        if (rigTarget == null)
        {
            if (flightController != null && flightController.rigTarget != null)
            {
                rigTarget = flightController.rigTarget;
            }
            else
            {
                rigTarget = transform;
            }
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

        if (rigTarget != null)
        {
            lastRigPosition = rigTarget.position;
        }

        // 2. Initialize Serial Connection
        if (enableSerial)
        {
            OpenSerialConnection();
        }
    }

    private void OpenSerialConnection()
    {
        try
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }

            serialPort = new SerialPort(portName, baudRate);
            serialPort.WriteTimeout = 50;
            serialPort.ReadTimeout = 10;
            serialPort.Open();

            Debug.Log($"[FanSerialController] Successfully connected to Fans on {portName} at {baudRate} baud!");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FanSerialController] Could not open {portName}: {e.Message}. Make sure Arduino is connected to {portName}.");
        }
    }

    void Update()
    {
        // 1. Calculate VR Rig Speed
        CalculateRigSpeed();

        // 2. Compute 3-Fan PWM Levels based on Speed & Head Tilt
        ComputeFanPWMs();

        // 3. Transmit to Arduino over Serial
        if (Time.time - lastSendTime >= updateInterval)
        {
            SendFanPWMData();
            lastSendTime = Time.time;
        }
    }

    private void CalculateRigSpeed()
    {
        float targetSpeed = 0f;

        // Primary source: BroomFlightController
        if (flightController != null)
        {
            targetSpeed = flightController.CurrentSpeed;
        }
        // Fallback: Delta distance calculation from rig transform
        else if (rigTarget != null && Time.deltaTime > 0.0001f)
        {
            Vector3 worldDelta = rigTarget.position - lastRigPosition;
            lastRigPosition = rigTarget.position;

            // Forward speed component along VR camera gaze
            if (vrCamera != null)
            {
                Vector3 forwardFlat = Vector3.ProjectOnPlane(vrCamera.forward, Vector3.up).normalized;
                targetSpeed = Mathf.Max(0f, Vector3.Dot(worldDelta, forwardFlat) / Time.deltaTime);
            }
            else
            {
                targetSpeed = worldDelta.magnitude / Time.deltaTime;
            }
        }

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
        float speedRatio = Mathf.Clamp01((smoothedForwardSpeed - minSpeedThreshold) / (maxSpeedForFullWind - minSpeedThreshold));

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
        // Only transmit if values have changed or periodically
        if (currentLeftPWM == lastSentLeft &&
            currentMiddlePWM == lastSentMiddle &&
            currentRightPWM == lastSentRight)
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

                if (showDebugLogs)
                {
                    Debug.Log($"[FanSerialController] Sent -> {packet.Trim()} (Speed: {smoothedForwardSpeed:F1} m/s)");
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

        GUILayout.BeginArea(new Rect(Screen.width - 320, 15, 305, 180), boxStyle);
        GUILayout.Label("<b>== VR FAN CONTROLLER (COM12) ==</b>");
        GUILayout.Label($"Port: <b>{portName}</b> | Status: {(serialPort != null && serialPort.IsOpen ? "<color=#00FF00>OPEN</color>" : "<color=#FF5555>CLOSED</color>")}");
        GUILayout.Label($"Rig Forward Speed: <b>{smoothedForwardSpeed:F1} m/s</b>");
        GUILayout.Label($"Middle Fan (Pin 9):  <b>{currentMiddlePWM}</b> / 255");
        GUILayout.Label($"Left Fan   (Pin 10): <b>{currentLeftPWM}</b> / 255");
        GUILayout.Label($"Right Fan  (Pin 11): <b>{currentRightPWM}</b> / 255");
        GUILayout.EndArea();
    }

    void OnDisable()
    {
        StopAllFans();
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
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                // Send zero packet before closing
                serialPort.Write("0,0,0\n");
                serialPort.Close();
                Debug.Log($"[FanSerialController] Serial connection to {portName} safely closed.");
            }
            catch (Exception) { }
        }
    }
}
