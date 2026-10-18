using System;
using System.Collections;
using System.IO.Ports;
using UnityEngine;

/// <summary>
/// Controls the Dual Haptic Vest/Jacket Arduino (haptic_jacket.ino).
/// Dynamically scales dual-motor vibration intensity based on VR flight / rig forward speed.
/// - Speed / Thrust increases -> Haptic vibration increases proportionally.
/// - Speed / Thrust decreases -> Haptic vibration winds down.
/// - Resting / Stop          -> Vibration completely turns off (0 PWM).
/// Includes deadzone thresholds to guarantee ZERO vibration when stationary or tracking jitters.
/// </summary>
public class HapticJacketController : MonoBehaviour
{
    public static HapticJacketController Instance { get; private set; }

    [Header("Serial Port Configuration (Haptic Jacket)")]
    [Tooltip("Target COM port where the Haptic Jacket Arduino is connected (e.g. COM7).")]
    public string portName = "COM7";
    [Tooltip("Baud rate matching haptic_jacket.ino (default: 9600).")]
    public int baudRate = 9600;
    [Tooltip("If enabled, automatically scans other COM ports if the specified port cannot be opened.")]
    public bool autoDetectPort = false;
    [Tooltip("Enable or disable physical serial communications.")]
    public bool enableSerial = true;

    [Header("Speed Reference & Sources")]
    [Tooltip("Direct reference to BroomFlightController. If null, automatically located in scene.")]
    public BroomFlightController flightController;
    [Tooltip("Target transform to track (e.g. XR Origin / VR Rig). If unassigned, automatically located.")]
    public Transform rigTarget;
    [Tooltip("VR Camera transform. If unassigned, uses Camera.main.")]
    public Transform vrCamera;

    [Header("Speed-to-Vibration Calibration & Resting Threshold")]
    [Tooltip("Minimum forward speed in m/s before vibration starts. Speeds below this are 100% idle (0 PWM), filtering out VR headset tracking drift.")]
    [Range(0.0f, 3.0f)]
    public float minSpeedThreshold = 0.2f;
    [Tooltip("Forward speed in m/s that corresponds to maximum vibration power (255 PWM).")]
    public float maxSpeedForFullVibration = 25.0f;
    [Tooltip("Minimum broom thrust percentage (FSR 1) required to activate vibration while stationary.")]
    [Range(0, 50)]
    public int thrustActivationThreshold = 10;
    [Tooltip("Minimum PWM sent when moving (helps motors overcome initial static friction). Matches MIN_HAPTIC_PWM in Arduino sketch.")]
    [Range(0, 100)]
    public int minVibrationPWM = 45;
    [Tooltip("Maximum allowed vibration PWM cap (0-255).")]
    [Range(50, 255)]
    public int maxVibrationPWM = 255;
    [Tooltip("Smoothing factor for speed & vibration transitions (prevents abrupt jerky motor clicks).")]
    public float vibrationSmoothingSpeed = 10.0f;
    [Tooltip("Response curve exponent (1.0 = linear, 1.5 - 2.0 = gentle vibration at cruising speed, intense at top speed).")]
    [Range(0.5f, 3.0f)]
    public float responseCurve = 1.0f;

    [Header("External Effects & Boost Pulses")]
    [Tooltip("Allow temporary haptic pulses (e.g., ring collection, crash impacts, boost triggers) to override base speed vibration.")]
    public bool allowExternalPulses = true;

    [Header("Transmission Optimization")]
    [Tooltip("Interval (seconds) between serial packets (e.g. 0.05s = 20Hz refresh).")]
    public float updateInterval = 0.05f;
    [Tooltip("Forced heartbeat interval to keep hardware alive even when speed is constant.")]
    public float heartbeatInterval = 0.5f;

    [Header("Testing & Debug Fallback")]
    [Tooltip("Allow keyboard testing keys: H (Max vibration), J (50% vibration), K (Stop), W (Simulated flight forward).")]
    public bool enableKeyboardDebug = true;
    [Tooltip("Manual override slider for testing in the Inspector (-1 = disabled, uses dynamic speed).")]
    [Range(-1, 255)]
    public int manualPWMOverride = -1;
    public bool showDebugLogs = false;
    [Tooltip("Show live on-screen telemetry HUD in Game view.")]
    public bool showOnScreenHUD = true;

    // Public Getters for external integration
    public float CurrentSpeed => smoothedForwardSpeed;
    public int CurrentPWM => currentPWM;
    public float CurrentIntensityPercent => Mathf.Clamp01((float)currentPWM / maxVibrationPWM) * 100f;
    public bool IsConnected => serialPort != null && serialPort.IsOpen;
    public string ActivePort => activePort;

    // Internal State
    private SerialPort serialPort;
    private string activePort = "";
    private bool isConnecting = false;
    private float smoothedForwardSpeed = 0f;
    private int currentPWM = 0;
    private int lastSentPWM = -1;
    private float lastSendTime = 0f;
    private float lastSuccessfulTransmitTime = 0f;

    // External pulse state
    private int pulsePWM = 0;
    private float pulseEndTime = 0f;

    // Rig tracking
    private Vector3 lastRigPosition;
    private bool hasLastPosition = false;
    private CharacterController characterController;
    private Rigidbody rigRigidbody;

    // Cached GUI Style
    private GUIStyle cachedBoxStyle;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);

        currentPWM = 0;
        lastSentPWM = -1;
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
                vrCamera = flightController.vrCamera;
            else if (Camera.main != null)
                vrCamera = Camera.main.transform;
        }

        if (rigTarget == null)
        {
            if (flightController != null && flightController.rigTarget != null)
                rigTarget = flightController.rigTarget;
            else if (vrCamera != null && vrCamera.parent != null)
                rigTarget = vrCamera.parent;
            else
                rigTarget = transform;
        }

        if (rigTarget != null)
        {
            characterController = rigTarget.GetComponent<CharacterController>();
            rigRigidbody = rigTarget.GetComponent<Rigidbody>();
            lastRigPosition = rigTarget.position;
            hasLastPosition = true;
        }

        // 2. Initialize Serial Connection
        if (enableSerial)
        {
            StartCoroutine(InitializeSerialConnection());
        }
    }

    public void ConnectToPort(string targetPort)
    {
        portName = targetPort;
        StartCoroutine(InitializeSerialConnection());
    }

    private IEnumerator InitializeSerialConnection()
    {
        isConnecting = true;

        // Try user specified port first
        if (!string.IsNullOrEmpty(portName))
        {
            if (TryOpenPort(portName))
            {
                isConnecting = false;
                yield break;
            }
        }

        // Optional Auto-detect across all COM ports
        if (autoDetectPort)
        {
            string[] availablePorts = SerialPort.GetPortNames();
            foreach (string p in availablePorts)
            {
                // Skip broom port if known
                if (flightController != null && p.Equals(flightController.portName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryOpenPort(p))
                {
                    isConnecting = false;
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        isConnecting = false;
        if (showDebugLogs)
        {
            Debug.LogWarning("[HapticJacketController] Could not establish connection to Haptic Jacket on " + portName);
        }
    }

    private bool TryOpenPort(string port)
    {
        try
        {
            CloseSerialConnection();

            serialPort = new SerialPort(port, baudRate)
            {
                WriteTimeout = 60,
                ReadTimeout = 60
            };
            serialPort.Open();

            try
            {
                serialPort.DtrEnable = true;
                serialPort.RtsEnable = true;
            }
            catch { }

            activePort = port;
            lastSuccessfulTransmitTime = Time.time;
            lastSentPWM = -1;

            // Send initial handshake
            try
            {
                serialPort.Write("HAPTIC:0\n");
            }
            catch { }

            Debug.Log($"[HapticJacketController] Successfully opened Haptic Jacket serial connection on {port} ({baudRate} baud).");
            return true;
        }
        catch (Exception e)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[HapticJacketController] Failed to open {port}: {e.Message}");
            }
            CloseSerialConnection();
            return false;
        }
    }

    void Update()
    {
        // 1. Keyboard Debug Inputs
        HandleKeyboardTesting();

        // 2. Calculate Rig Forward Speed
        CalculateForwardSpeed();

        // 3. Compute Haptic Vibration PWM (from both handle squeeze and flight speed)
        ComputeVibration();

        // 4. Transmit to Arduino over Serial
        if (Time.time - lastSendTime >= updateInterval)
        {
            SendHapticData();
            lastSendTime = Time.time;
        }
    }

    /// <summary>
    /// Accurately detects forward movement speed of the VR Rig / Broom,
    /// filtering out resting FSR baselines and VR headset micro-jitter.
    /// </summary>
    private void CalculateForwardSpeed()
    {
        float targetSpeed = 0f;

        // Source 1: Direct from BroomFlightController
        if (flightController != null && flightController.enabled)
        {
            targetSpeed = Mathf.Max(targetSpeed, flightController.CurrentSpeed);
        }

        // Directional forward vector
        Vector3 forwardFlat = Vector3.forward;
        if (vrCamera != null)
        {
            forwardFlat = Vector3.ProjectOnPlane(vrCamera.forward, Vector3.up).normalized;
        }
        else if (rigTarget != null)
        {
            forwardFlat = Vector3.ProjectOnPlane(rigTarget.forward, Vector3.up).normalized;
        }

        // Source 2: CharacterController forward velocity
        if (characterController != null && characterController.enabled)
        {
            float ccForward = Vector3.Dot(characterController.velocity, forwardFlat);
            if (ccForward > targetSpeed) targetSpeed = ccForward;
        }

        // Source 3: Rigidbody forward velocity
        if (rigRigidbody != null && !rigRigidbody.isKinematic)
        {
#if UNITY_6000_0_OR_NEWER
            float rbForward = Vector3.Dot(rigRigidbody.linearVelocity, forwardFlat);
#else
            float rbForward = Vector3.Dot(rigRigidbody.velocity, forwardFlat);
#endif
            if (rbForward > targetSpeed) targetSpeed = rbForward;
        }

        // Source 4: Pure transform delta displacement per frame (filtered against headset micro-jitter)
        if (rigTarget != null && Time.deltaTime > 0.0001f)
        {
            if (hasLastPosition)
            {
                Vector3 worldDelta = rigTarget.position - lastRigPosition;
                float deltaSpeed = Vector3.Dot(worldDelta, forwardFlat) / Time.deltaTime;

                // Only count position delta if it exceeds the resting jitter deadzone
                if (deltaSpeed > minSpeedThreshold)
                {
                    targetSpeed = Mathf.Max(targetSpeed, deltaSpeed);
                }
            }
            lastRigPosition = rigTarget.position;
            hasLastPosition = true;
        }

        // Source 5: Keyboard forward cruise testing (W / Up Arrow)
        if (enableKeyboardDebug && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)))
        {
            if (targetSpeed < 10f) targetSpeed = 15f; // Simulated forward speed
        }

        // Clamp to positive speed
        targetSpeed = Mathf.Max(0f, targetSpeed);

        // Snap immediately to zero if below threshold (prevents infinite lerp crawl around 0.1 m/s)
        if (targetSpeed < minSpeedThreshold)
        {
            targetSpeed = 0f;
            smoothedForwardSpeed = Mathf.MoveTowards(smoothedForwardSpeed, 0f, vibrationSmoothingSpeed * 2.5f * Time.deltaTime);
            if (smoothedForwardSpeed < 0.02f) smoothedForwardSpeed = 0f;
        }
        else
        {
            smoothedForwardSpeed = Mathf.Lerp(smoothedForwardSpeed, targetSpeed, vibrationSmoothingSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Computes target motor PWM based on BOTH handle squeeze thrust and forward flight speed:
    /// - Resting (no squeeze, speed below deadzone) -> 0 PWM (Off)
    /// - Squeezing handle -> Proportional vibration
    /// - Flying through air -> Proportional vibration
    /// </summary>
    private void ComputeVibration()
    {
        // 1. Manual Inspector override
        if (manualPWMOverride >= 0)
        {
            currentPWM = Mathf.Clamp(manualPWMOverride, 0, 255);
            return;
        }

        // 2. External temporary pulse (ring pickup, damage impact, turbo boost)
        if (allowExternalPulses && Time.time < pulseEndTime)
        {
            currentPWM = pulsePWM;
            return;
        }

        // 3. Thrust Squeeze Contribution (from Broom handle FSR 1)
        float thrustPWM = 0f;
        if (flightController != null && flightController.TargetThrust > thrustActivationThreshold)
        {
            float thrustRatio = Mathf.Clamp01(((float)flightController.TargetThrust - thrustActivationThreshold) / (100f - thrustActivationThreshold));
            thrustPWM = Mathf.Lerp((float)minVibrationPWM, (float)maxVibrationPWM, thrustRatio);
        }

        // 4. Forward Flight Speed Contribution
        float speedPWM = 0f;
        if (smoothedForwardSpeed >= minSpeedThreshold)
        {
            float speedRange = Mathf.Max(0.01f, maxSpeedForFullVibration - minSpeedThreshold);
            float normalized = Mathf.Clamp01((smoothedForwardSpeed - minSpeedThreshold) / speedRange);

            if (Mathf.Abs(responseCurve - 1.0f) > 0.01f && responseCurve > 0.01f)
            {
                normalized = Mathf.Pow(normalized, responseCurve);
            }

            speedPWM = Mathf.Lerp((float)minVibrationPWM, (float)maxVibrationPWM, normalized);
        }

        // Combined intensity is the maximum of handle squeeze and flight speed
        float combinedTargetPWM = Mathf.Max(thrustPWM, speedPWM);

        // Resting threshold check: if below minimum starting vibration, turn completely off
        if (combinedTargetPWM < (float)minVibrationPWM)
        {
            currentPWM = 0;
        }
        else
        {
            currentPWM = Mathf.RoundToInt(Mathf.Clamp(combinedTargetPWM, 0f, (float)maxVibrationPWM));
        }
    }

    /// <summary>
    /// Transmits the PWM command to haptic_jacket.ino via Serial.
    /// Protocol: "HAPTIC:<0-255>\n" (directly controls digital pins 3 & 5).
    /// </summary>
    private void SendHapticData()
    {
        if (serialPort == null || !serialPort.IsOpen) return;

        // Optimization: Send when PWM changes or heartbeat timer triggers
        bool valueChanged = (currentPWM != lastSentPWM);
        bool heartbeatDue = (Time.time - lastSuccessfulTransmitTime >= heartbeatInterval);

        if (!valueChanged && !heartbeatDue) return;

        try
        {
            // Format matching haptic_jacket.ino parser
            string packet = $"HAPTIC:{currentPWM}\n";
            serialPort.Write(packet);

            lastSentPWM = currentPWM;
            lastSuccessfulTransmitTime = Time.time;

            if (showDebugLogs)
            {
                Debug.Log($"[HapticJacketController] Sent '{packet.Trim()}' (PWM: {currentPWM}, Speed: {smoothedForwardSpeed:F1} m/s)");
            }
        }
        catch (Exception e)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[HapticJacketController] Write error: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Triggers an immediate haptic vibration pulse (e.g. collecting a ring or taking impact damage)
    /// that temporarily overrides the normal speed-based vibration.
    /// </summary>
    /// <param name="pwm">Vibration intensity (0-255).</param>
    /// <param name="durationSeconds">Duration of the pulse in seconds.</param>
    public void TriggerHapticPulse(int pwm, float durationSeconds)
    {
        pulsePWM = Mathf.Clamp(pwm, 0, 255);
        pulseEndTime = Time.time + durationSeconds;
        SendImmediatePWM(pulsePWM);
    }

    /// <summary>
    /// Immediately stops all vibration.
    /// </summary>
    public void StopAllVibrations()
    {
        manualPWMOverride = -1;
        pulsePWM = 0;
        pulseEndTime = 0f;
        currentPWM = 0;
        SendImmediatePWM(0);
    }

    private void SendImmediatePWM(int pwm)
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Write($"HAPTIC:{pwm}\n");
                lastSentPWM = pwm;
            }
            catch { }
        }
    }

    private void HandleKeyboardTesting()
    {
        if (!enableKeyboardDebug) return;

        // Key H: Full vibration test pulse
        if (Input.GetKeyDown(KeyCode.H))
        {
            TriggerHapticPulse(255, 2.0f);
            if (showDebugLogs) Debug.Log("[HapticJacketController] Test Key H: 100% Vibration Pulse (255 PWM)");
        }
        // Key J: Medium vibration test pulse
        else if (Input.GetKeyDown(KeyCode.J))
        {
            TriggerHapticPulse(150, 2.0f);
            if (showDebugLogs) Debug.Log("[HapticJacketController] Test Key J: Medium Vibration Pulse (150 PWM)");
        }
        // Key K: Stop vibration
        else if (Input.GetKeyDown(KeyCode.K))
        {
            StopAllVibrations();
            if (showDebugLogs) Debug.Log("[HapticJacketController] Test Key K: Vibration STOPPED");
        }
    }

    void OnGUI()
    {
        if (!showOnScreenHUD) return;

        if (cachedBoxStyle == null)
        {
            cachedBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12
            };
            cachedBoxStyle.normal.textColor = Color.white;
        }

        // Display panel on top-right to avoid overlapping BroomFlightController
        float panelWidth = 430;
        float panelHeight = 225;
        float posX = Screen.width - panelWidth - 15;
        float posY = 15;

        GUILayout.BeginArea(new Rect(posX, posY, panelWidth, panelHeight), cachedBoxStyle);
        GUILayout.Label("<b><size=13>== HAPTIC JACKET (VEST) CONTROLLER ==</size></b>");

        if (IsConnected)
        {
            GUILayout.Label($"Serial: <color=#00FF66><b>CONNECTED ({activePort} @ {baudRate})</b></color>");
        }
        else
        {
            string status = isConnecting ? "Connecting..." : "DISCONNECTED";
            GUILayout.Label($"Serial: <color=#FFAA00><b>{status} ({portName})</b></color>");
        }

        int broomThrust = (flightController != null) ? flightController.TargetThrust : 0;
        GUILayout.Label($"Broom Thrust: <b>{broomThrust}%</b> [Threshold: {thrustActivationThreshold}%] | Speed: <b>{smoothedForwardSpeed:F1} m/s</b>");

        string stateLabel = (currentPWM == 0)
            ? "<color=#888888>IDLE / RESTING (0 PWM)</color>"
            : $"<color=#00FF66>VIBRATING (<b>{currentPWM} / 255 PWM</b> - {CurrentIntensityPercent:F0}%)</color>";
        
        string activePulse = (Time.time < pulseEndTime) ? " <color=#FFAA00>[PULSE ACTIVE]</color>" : "";
        GUILayout.Label($"Output: {stateLabel}{activePulse}");

        // Visual ASCII power bar
        int barLength = 20;
        int filled = Mathf.RoundToInt((currentPWM / 255f) * barLength);
        string bar = new string('|', filled) + new string('.', barLength - filled);
        string barColor = currentPWM > 0 ? (currentPWM > 180 ? "#FF4444" : "#00FF66") : "#888888";
        GUILayout.Label($"Power: <color={barColor}>[{bar}]</color>");

        // Interactive GUI Buttons for instant testing & Port Switching
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("TEST VIBRATE (2s)"))
        {
            TriggerHapticPulse(255, 2.0f);
        }
        if (GUILayout.Button("STOP"))
        {
            StopAllVibrations();
        }
        if (!IsConnected && GUILayout.Button("RECONNECT"))
        {
            ConnectToPort(portName);
        }
        GUILayout.EndHorizontal();

        // Quick Port Switcher row
        GUILayout.BeginHorizontal();
        GUILayout.Label("Switch Port:", GUILayout.Width(75));
        string[] commonPorts = new string[] { "COM3", "COM4", "COM6", "COM7", "COM8" };
        foreach (string p in commonPorts)
        {
            if (GUILayout.Button(p, GUILayout.Width(50)))
            {
                ConnectToPort(p);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label($"<size=10><color=#CCCCCC>Keys: [H] Max Pulse | [J] Med Pulse | [K] Stop | [W] Cruise</color></size>");
        GUILayout.EndArea();
    }

    void OnDisable()
    {
        StopAllVibrations();
        CloseSerialConnection();
    }

    void OnDestroy()
    {
        StopAllVibrations();
        CloseSerialConnection();
    }

    void OnApplicationQuit()
    {
        StopAllVibrations();
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
                    serialPort.Write("HAPTIC:0\n");
                    serialPort.Write("s\n");
                    serialPort.BaseStream.Flush();
                    serialPort.Close();
                }
                serialPort.Dispose();
            }
            catch { }
            finally
            {
                serialPort = null;
                activePort = "";
            }
        }
    }
}
