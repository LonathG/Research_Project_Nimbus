using UnityEngine;
using System.Collections;
using System.IO.Ports;
using System.Threading;
using System.Text;

public class BroomFlightController : MonoBehaviour
{
    [Header("Serial Port Settings")]
    [Tooltip("COM port for Arduino / ESP hardware.")]
    public string portName = "COM6";
    public int baudRate = 115200;
    public bool enableSerial = true;

    [Header("Movement Target")]
    [Tooltip("Target GameObject to move (e.g. XROrigin / VR Rig root). If unassigned, automatically uses parent or this transform.")]
    public Transform rigTarget;
    public Transform vrCamera;

    [Header("Flight Physics - Forward")]
    public float speedMultiplier = 25.0f;
    public float accelerationSmooth = 0.05f;
    public float decelerationSmooth = 0.02f;

    [Header("Flight Physics - Altitude (FSR 2)")]
    [Tooltip("Ascent speed when pressure is applied to FSR 2 or Spacebar is held.")]
    public float maxClimbSpeed = 10.0f;
    [Tooltip("Gradual descent speed when no pressure is applied to FSR 2 (smoothly brings broom down to ground).")]
    public float gravityFallSpeed = 4.0f;
    [Tooltip("Fast descent speed when Q / Left Control is pressed.")]
    public float maxDescendSpeed = 7.0f;
    [Tooltip("Smooth time for vertical velocity transitions.")]
    public float altitudeSmoothTime = 0.22f;
    [Tooltip("Minimum Y height floor level to prevent falling into void.")]
    public float minGroundY = 0.0f;
    public bool useGroundLimit = true;

    [Header("Steering Settings - General")]
    [Tooltip("Maximum yaw turning speed in degrees per second when squeezing Left/Right Turn FSRs or pressing A/D.")]
    public float turnSpeed = 60.0f;
    [Tooltip("Smoothing response speed for FSR turning transitions.")]
    public float turnSmoothSpeed = 15.0f;
    [Tooltip("Enable steering via FSR 3 (Left Turn) and FSR 4 (Right Turn).")]
    public bool enableFsrSteering = true;
    [Tooltip("If true, scales raw sensor readings with the manual FSR 3 and FSR 4 threshold settings below instead of relying on Arduino presets.")]
    public bool preferRawSteeringSensors = true;
    [Tooltip("Enable steering via VR head tilt roll.")]
    public bool enableHeadTiltSteering = true;
    [Tooltip("Sensitivity multiplier for head tilt roll steering.")]
    public float turnSensitivity = 1.5f;
    [Tooltip("Minimum head tilt roll angle before tilt steering kicks in.")]
    public float tiltThreshold = 8f;

    [Header("FSR 3 Settings (Left Turn / Pin A2)")]
    [Tooltip("Minimum raw ADC deadzone for FSR 3. Sensor values below this are ignored (resting baseline).")]
    public float fsr3MinThreshold = 15f;
    [Tooltip("Maximum raw ADC value for FSR 3 that corresponds to 100% full left turn.")]
    public float rawLeftTurnMax = 350f;
    [Tooltip("Sensitivity multiplier for Left Turn FSR 3 (lower this, e.g. 0.7, if Left is too sensitive; raise it if too weak).")]
    [Range(0.1f, 3.0f)]
    public float fsr3Sensitivity = 1.0f;
    [Tooltip("Response curve exponent for FSR 3 (1.0 = linear, 1.5 - 2.0 = softer gentle squeeze, ramps up when pressed harder).")]
    [Range(0.5f, 3.0f)]
    public float fsr3ResponseCurve = 1.0f;

    [Header("FSR 4 Settings (Right Turn / Pin A3)")]
    [Tooltip("Minimum raw ADC deadzone for FSR 4. Sensor values below this are ignored (resting baseline).")]
    public float fsr4MinThreshold = 15f;
    [Tooltip("Maximum raw ADC value for FSR 4 that corresponds to 100% full right turn.")]
    public float rawRightTurnMax = 350f;
    [Tooltip("Sensitivity multiplier for Right Turn FSR 4 (lower this, e.g. 0.7, if Right is too sensitive; raise it if too weak).")]
    [Range(0.1f, 3.0f)]
    public float fsr4Sensitivity = 1.0f;
    [Tooltip("Response curve exponent for FSR 4 (1.0 = linear, 1.5 - 2.0 = softer gentle squeeze, ramps up when pressed harder).")]
    [Range(0.5f, 3.0f)]
    public float fsr4ResponseCurve = 1.0f;

    [Header("FSR 1 Settings (Forward Thrust / Pin A0)")]
    [Tooltip("Minimum raw ADC deadzone for FSR 1. Sensor values below this are ignored (resting baseline).")]
    public float fsr1MinThreshold = 15f;
    [Tooltip("Maximum raw ADC value for FSR 1 that corresponds to 100% full thrust.")]
    public float rawThrustMax = 350f;
    [Tooltip("Sensitivity multiplier for Thrust FSR 1.")]
    [Range(0.1f, 3.0f)]
    public float fsr1Sensitivity = 1.0f;
    [Tooltip("Response curve exponent for FSR 1 (1.0 = linear, 1.5 - 2.0 = softer gentle squeeze, ramps up when pressed harder).")]
    [Range(0.5f, 3.0f)]
    public float fsr1ResponseCurve = 1.0f;

    [Header("FSR 2 Settings (Altitude / Climb / Pin A1)")]
    [Tooltip("Minimum raw ADC deadzone for FSR 2. Sensor values below this are ignored (resting baseline).")]
    public float fsr2MinThreshold = 270f;
    [Tooltip("Maximum raw ADC value for FSR 2 that corresponds to 100% full climb.")]
    public float rawAltitudeMax = 700f;
    [Tooltip("Sensitivity multiplier for Altitude FSR 2.")]
    [Range(0.1f, 3.0f)]
    public float fsr2Sensitivity = 1.0f;
    [Tooltip("Response curve exponent for FSR 2 (1.0 = linear, 1.5 - 2.0 = softer gentle squeeze, ramps up when pressed harder).")]
    [Range(0.5f, 3.0f)]
    public float fsr2ResponseCurve = 1.0f;

    [Header("Thrust & Altitude Calibration Mode")]
    [Tooltip("If true, scales raw sensor readings with the manual FSR 1 and FSR 2 threshold settings above instead of relying on Arduino presets.")]
    public bool preferRawThrustAltitudeSensors = true;
    [Tooltip("Legacy fallback minimum expected raw sensor value (used if legacy autoScale is called).")]
    public float rawInputMin = 15f;
    [Tooltip("Automatically scale hardware values to 0-100% flight thrust & altitude.")]
    public bool autoScaleSensorInput = true;

    // Convenience property aliases for backwards compatibility
    public float rawThrustMin { get => fsr1MinThreshold; set => fsr1MinThreshold = value; }
    public float rawAltitudeMin { get => fsr2MinThreshold; set => fsr2MinThreshold = value; }

    [Header("Testing & Debug Fallback")]
    [Tooltip("Allow pressing W/Up (thrust), Space/E (climb), Q/Ctrl (descend), A/Left (turn left), D/Right (turn right) without hardware.")]
    public bool enableKeyboardFallback = true;
    public bool debugLogs = true;
    [Tooltip("Display an on-screen real-time telemetry HUD in the Game view / VR.")]
    public bool showOnScreenDebug = true;

    // Serial & Threading State (Crash-proof architecture)
    private SerialPort sp;
    private Thread serialThread;
    private readonly object dataLock = new object();
    private string lastData = "";
    private volatile bool isRunning = false;
    private volatile bool isThreadActive = false;

    // Diagnostics & Metrics
    private int totalPacketsReceived = 0;
    private int packetsInLastSecond = 0;
    private int measuredPacketsPerSec = 0;
    private float ppsTimer = 0f;
    private float lastPacketReceivedTime = 0f;
    private string latestTelemetryRaw = "Waiting for hardware...";
    private string lastConnectionError = "";
    private bool portNamesLogged = false;
    private int consecutiveErrors = 0;

    // Flight Dynamics State
    private float currentSpeed = 0f;
    private float currentVerticalSpeed = 0f;
    private float verticalSpeedVelocity = 0f;
    private float smoothThrustInput = 0f;
    private float smoothAltitudeInput = 0f;
    private float smoothTurnInput = 0f;
    private int serialThrust = 0;
    private int serialAltitude = 0;
    private int serialLeftTurn = 0;
    private int serialRightTurn = 0;
    private int targetThrust = 0;
    private int targetAltitude = 0;
    private int targetLeftTurn = 0;
    private int targetRightTurn = 0;

    // Live raw sensor readings for real-time calibration & HUD display
    private float rawThrust = 0f;
    private float rawAltitude = 0f;
    private float rawLeftTurn = 0f;
    private float rawRightTurn = 0f;

    private float logTimer = 0f;
    private bool lastGroundedState = false;
    private CollisionFlags lastCollisionFlags = CollisionFlags.None;

    // Cached GUI Styles to prevent native IMGUI memory leaks
    private GUIStyle cachedBoxStyle;
    private GUIStyle cachedHeaderStyle;
    private GUIStyle cachedDataStyle;

    // Public Telemetry Accessors for external controllers (Fans, Lighting, Audio)
    public float CurrentSpeed => currentSpeed;
    public float CurrentVerticalSpeed => currentVerticalSpeed;
    public int TargetThrust => targetThrust;
    public int TargetAltitude => targetAltitude;
    public int TargetLeftTurn => targetLeftTurn;
    public int TargetRightTurn => targetRightTurn;
    public float RawLeftTurn => rawLeftTurn;
    public float RawRightTurn => rawRightTurn;
    public float RawThrust => rawThrust;
    public float RawAltitude => rawAltitude;
    public float SmoothTurnInput => smoothTurnInput;
    public int PacketsPerSecond => measuredPacketsPerSec;
    public bool IsSerialConnected => sp != null && sp.IsOpen && isRunning;

    private CharacterController characterController;
    private Rigidbody rb;

    void Start()
    {
        // 1. Resolve VR Camera
        if (vrCamera == null)
        {
            if (Camera.main != null) vrCamera = Camera.main.transform;
            else vrCamera = transform;
        }

        // 2. Resolve Rig Target (Do not move Camera directly because XR tracking overrides it)
        if (rigTarget == null)
        {
            if (transform == vrCamera && transform.parent != null)
            {
                rigTarget = transform.parent;
                Debug.Log($"[BroomFlight] Script attached to Camera. Movement target set to parent: {rigTarget.name}");
            }
            else
            {
                rigTarget = transform;
            }
        }

        // 3. Check for Physics Components on Rig Target
        if (rigTarget != null)
        {
            characterController = rigTarget.GetComponent<CharacterController>();
            rb = rigTarget.GetComponent<Rigidbody>();

            Debug.Log($"[BroomFlight Setup] Rig Target: '{rigTarget.name}' | Position: {rigTarget.position}");
            Debug.Log($"[BroomFlight Setup] CharacterController found: {(characterController != null ? $"YES (Enabled: {characterController.enabled}, Height: {characterController.height}, Center: {characterController.center})" : "NO")}");
            Debug.Log($"[BroomFlight Setup] Rigidbody found: {(rb != null ? $"YES (isKinematic: {rb.isKinematic}, useGravity: {rb.useGravity})" : "NO")}");

            // Scan and report any other locomotion/movement scripts
            MonoBehaviour[] allScripts = rigTarget.GetComponents<MonoBehaviour>();
            foreach (var s in allScripts)
            {
                if (s != null && s != this)
                {
                    string scriptName = s.GetType().Name;
                    if (scriptName.Contains("Move") || scriptName.Contains("Locomotion") || scriptName.Contains("Gravity") || scriptName.Contains("Driver"))
                    {
                        Debug.LogWarning($"[BroomFlight Conflict Warning] Detected locomotion script '{scriptName}' on RigTarget. Ensure gravity/movement scripts do not fight BroomFlight.");
                    }
                }
            }
        }

        // 4. Connect Serial Hardware with Auto-Reconnect Coroutine
        if (enableSerial)
        {
            StartCoroutine(AutoConnectLoop());
        }
    }

    private IEnumerator AutoConnectLoop()
    {
        while (enableSerial)
        {
            if (sp == null || !sp.IsOpen || !isThreadActive)
            {
                InitSerial();
            }

            // Non-aggressive 4-second check to avoid driver lockups
            yield return new WaitForSeconds(4.0f);
        }
    }

    void InitSerial()
    {
        try
        {
            CloseSerialConnection();

            if (!portNamesLogged && debugLogs)
            {
                portNamesLogged = true;
                try
                {
                    string[] availablePorts = SerialPort.GetPortNames();
                    Debug.Log($"[BroomFlight] Available Serial Ports on PC: [{string.Join(", ", availablePorts)}]");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[BroomFlight] Could not query COM ports: {ex.Message}");
                }
            }

            Debug.Log($"[BroomFlight] Attempting connection on {portName} @ {baudRate} baud...");

            sp = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 50,    // Short timeout so thread loop responds to shutdown quickly
                WriteTimeout = 50,
                DtrEnable = true,
                RtsEnable = true
            };

            sp.Open();

            isRunning = true;
            lastConnectionError = "";
            consecutiveErrors = 0;

            serialThread = new Thread(ReadSerialData)
            {
                IsBackground = true,
                Name = "BroomFlight_SerialReader"
            };
            serialThread.Start();

            Debug.Log($"<color=#00FF66><b>[BroomFlight] Successfully Connected to Hardware on {portName} ({baudRate} baud)!</b></color>");
        }
        catch (System.Exception e)
        {
            lastConnectionError = e.Message;
            if (debugLogs)
            {
                Debug.LogWarning($"[BroomFlight] Serial Connection Pending on {portName}: {e.Message}");
            }
            CloseSerialConnection();
        }
    }

    /// <summary>
    /// Thread-safe, non-blocking serial packet collector.
    /// Accumulates raw incoming bytes without blocking ReadLine hangs or mid-line buffer clearing.
    /// </summary>
    void ReadSerialData()
    {
        isThreadActive = true;
        StringBuilder lineBuilder = new StringBuilder(128);

        while (isRunning)
        {
            try
            {
                if (sp != null && sp.IsOpen)
                {
                    // Check available bytes to prevent synchronous kernel hang
                    int bytesAvailable = sp.BytesToRead;
                    if (bytesAvailable > 0)
                    {
                        // Prevent extreme buffer pileup if computer hitched
                        if (bytesAvailable > 512)
                        {
                            sp.DiscardInBuffer();
                            lineBuilder.Clear();
                            continue;
                        }

                        for (int i = 0; i < bytesAvailable; i++)
                        {
                            int byteRead = sp.ReadByte();
                            if (byteRead == -1) break;

                            char c = (char)byteRead;
                            if (c == '\n')
                            {
                                string completeLine = lineBuilder.ToString();
                                lineBuilder.Clear();

                                if (!string.IsNullOrEmpty(completeLine))
                                {
                                    lock (dataLock)
                                    {
                                        lastData = completeLine;
                                    }
                                }
                            }
                            else if (c != '\r')
                            {
                                if (lineBuilder.Length < 256)
                                {
                                    lineBuilder.Append(c);
                                }
                                else
                                {
                                    lineBuilder.Clear(); // Discard oversized garbage
                                }
                            }
                        }

                        consecutiveErrors = 0;
                    }
                    else
                    {
                        // No bytes currently in buffer; sleep briefly to yield CPU
                        Thread.Sleep(5);
                    }
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
            catch (System.TimeoutException)
            {
                // Expected when waiting for next sensor burst
            }
            catch (System.IO.IOException ioEx)
            {
                consecutiveErrors++;
                if (consecutiveErrors < 3 && debugLogs)
                {
                    Debug.LogWarning($"[BroomFlight Serial IO] {ioEx.Message}");
                }
                Thread.Sleep(20);
            }
            catch (System.InvalidOperationException)
            {
                Thread.Sleep(50);
            }
            catch (System.Exception ex)
            {
                consecutiveErrors++;
                if (consecutiveErrors < 3 && debugLogs)
                {
                    Debug.LogWarning($"[BroomFlight Serial Error] {ex.Message}");
                }
                Thread.Sleep(20);
            }
        }

        isThreadActive = false;
    }

    void Update()
    {
        // 1. Process incoming packet telemetry from background thread
        ParseSerialData();

        // 2. Measure Packets Per Second (PPS)
        ppsTimer += Time.deltaTime;
        if (ppsTimer >= 1.0f)
        {
            measuredPacketsPerSec = packetsInLastSecond;
            packetsInLastSecond = 0;
            ppsTimer = 0f;
        }

        // 3. Stale Data Watchdog: If no fresh hardware packet received within 350ms, smoothly zero inputs
        if (Time.time - lastPacketReceivedTime > 0.35f)
        {
            serialThrust = Mathf.RoundToInt(Mathf.MoveTowards(serialThrust, 0, 150f * Time.deltaTime));
            serialAltitude = Mathf.RoundToInt(Mathf.MoveTowards(serialAltitude, 0, 150f * Time.deltaTime));
            serialLeftTurn = Mathf.RoundToInt(Mathf.MoveTowards(serialLeftTurn, 0, 150f * Time.deltaTime));
            serialRightTurn = Mathf.RoundToInt(Mathf.MoveTowards(serialRightTurn, 0, 150f * Time.deltaTime));
        }

        // 4. Keyboard Fallback Input (Testing without hardware)
        int keyboardThrust = 0;
        int keyboardAltitude = 0;
        int keyboardLeftTurn = 0;
        int keyboardRightTurn = 0;
        bool isKeyboardDescending = false;

        if (enableKeyboardFallback)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                keyboardThrust = 100;
            }

            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E))
            {
                keyboardAltitude = 100;
            }

            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C))
            {
                isKeyboardDescending = true;
            }

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                keyboardLeftTurn = 100;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                keyboardRightTurn = 100;
            }
        }

        // Combine Serial Hardware and Keyboard Inputs (Max of both)
        targetThrust = Mathf.Max(serialThrust, keyboardThrust);
        targetAltitude = Mathf.Max(serialAltitude, keyboardAltitude);
        targetLeftTurn = Mathf.Max(serialLeftTurn, keyboardLeftTurn);
        targetRightTurn = Mathf.Max(serialRightTurn, keyboardRightTurn);

        // Smooth inputs to eliminate frame-rate jitter
        smoothThrustInput = Mathf.Lerp(smoothThrustInput, targetThrust, 15f * Time.deltaTime);
        smoothAltitudeInput = Mathf.Lerp(smoothAltitudeInput, targetAltitude, 15f * Time.deltaTime);

        // Calculate net steering input (-1.0 for full Left, +1.0 for full Right)
        float rawTurnInput = ((float)targetRightTurn - (float)targetLeftTurn) / 100f;
        smoothTurnInput = Mathf.Lerp(smoothTurnInput, rawTurnInput, turnSmoothSpeed * Time.deltaTime);

        // 5. Check Grounded State
        bool isGrounded = false;
        if (characterController != null && characterController.enabled)
        {
            isGrounded = characterController.isGrounded;
        }
        else if (useGroundLimit && rigTarget != null && rigTarget.position.y <= minGroundY + 0.05f)
        {
            isGrounded = true;
        }

        // 6. Calculate Forward Flight Speed
        float maxSpeed = (speedMultiplier <= 2.0f) ? (speedMultiplier * 100f) : speedMultiplier;
        float targetSpeed = (smoothThrustInput / 100f) * maxSpeed;
        float lerpFactor = (targetSpeed > currentSpeed) ? accelerationSmooth : decelerationSmooth;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, lerpFactor);

        // 7. Calculate Vertical Ascent vs Smooth Glide/Descent
        float targetVertSpeed = 0f;
        if (smoothAltitudeInput > 2.0f)
        {
            // Squeezing FSR 2 (or holding Space) -> Fly UP proportional to squeeze pressure
            float climbRatio = Mathf.Clamp01(smoothAltitudeInput / 100f);
            targetVertSpeed = climbRatio * maxClimbSpeed;
        }
        else if (isKeyboardDescending)
        {
            // Fast descent override (Q / Left Ctrl)
            targetVertSpeed = -maxDescendSpeed;
        }
        else
        {
            // No squeeze -> Smooth glide down to ground
            if (isGrounded)
            {
                targetVertSpeed = -0.5f;
            }
            else
            {
                targetVertSpeed = -gravityFallSpeed;
            }
        }

        // Smooth vertical velocity damping
        currentVerticalSpeed = Mathf.SmoothDamp(currentVerticalSpeed, targetVertSpeed, ref verticalSpeedVelocity, altitudeSmoothTime);

        // Anti-Bounce on touchdown
        if (isGrounded && currentVerticalSpeed < -0.8f && smoothAltitudeInput <= 2.0f)
        {
            currentVerticalSpeed = -0.5f;
        }

        // Periodic Diagnostic Console Log (Every 1.5s)
        logTimer += Time.deltaTime;
        if (logTimer > 1.5f)
        {
            if (debugLogs)
            {
                string status = (sp != null && sp.IsOpen) ? $"CONNECTED ({measuredPacketsPerSec} PPS)" : "DISCONNECTED";
                Debug.Log($"[BroomFlight Status] {status} | Thrust(FSR1): Raw {rawThrust:F0} -> {targetThrust}% | Alt(FSR2): Raw {rawAltitude:F0} -> {targetAltitude}% | FSR3(L): Raw {rawLeftTurn:F0} -> {targetLeftTurn}% | FSR4(R): Raw {rawRightTurn:F0} -> {targetRightTurn}% | Speed: {currentSpeed:F1}m/s");
            }
            logTimer = 0;
        }

        if (rigTarget == null || vrCamera == null) return;

        // 8. STEERING (FSR Handle Sensors + Head Tilt Roll)
        float totalTurnDelta = 0f;

        // FSR Steering (Left / Right Pressure Sensors)
        if (enableFsrSteering && Mathf.Abs(smoothTurnInput) > 0.005f)
        {
            totalTurnDelta += smoothTurnInput * turnSpeed * Time.deltaTime;
        }

        // Head Tilt Roll Steering (VR Immersion)
        if (enableHeadTiltSteering && vrCamera != null)
        {
            float tiltZ = vrCamera.localEulerAngles.z;
            if (tiltZ > 180f) tiltZ -= 360f;

            if (Mathf.Abs(tiltZ) > tiltThreshold)
            {
                totalTurnDelta += -tiltZ * turnSensitivity * Time.deltaTime;
            }
        }

        if (Mathf.Abs(totalTurnDelta) > 0.0001f)
        {
            rigTarget.Rotate(0f, totalTurnDelta, 0f);
        }

        // 9. MOVEMENT (Horizontal Velocity + Vertical Ascent)
        Vector3 moveDelta = Vector3.zero;

        if (currentSpeed > 0.001f)
        {
            Vector3 forwardDir = Vector3.ProjectOnPlane(vrCamera.forward, Vector3.up).normalized;
            if (forwardDir.sqrMagnitude > 0.001f)
            {
                moveDelta += forwardDir * currentSpeed * Time.deltaTime;
            }
        }

        moveDelta.y += currentVerticalSpeed * Time.deltaTime;

        // Clamp height if ground limit is active and no CharacterController
        if (useGroundLimit && (characterController == null || !characterController.enabled) && (rigTarget.position.y + moveDelta.y) < minGroundY)
        {
            float clampDeltaY = minGroundY - rigTarget.position.y;
            if (moveDelta.y < clampDeltaY)
            {
                moveDelta.y = clampDeltaY;
                currentVerticalSpeed = 0f;
            }
        }

        // Log grounded state transitions
        if (debugLogs && isGrounded != lastGroundedState)
        {
            Debug.Log($"[BroomFlight Grounded Toggle] isGrounded: {isGrounded} | RigY: {rigTarget.position.y:F2} | VertSpeed: {currentVerticalSpeed:F2}");
            lastGroundedState = isGrounded;
        }

        // Apply movement vector to Rig Target
        if (moveDelta.sqrMagnitude > 0.000001f)
        {
            if (characterController != null && characterController.enabled)
            {
                lastCollisionFlags = characterController.Move(moveDelta);
                if (debugLogs && targetAltitude > 0 && (lastCollisionFlags & CollisionFlags.CollidedAbove) != 0)
                {
                    Debug.LogWarning("[BroomFlight Collision] Collided with CEILING above!");
                }
            }
            else if (rb != null && !rb.isKinematic)
            {
                rb.MovePosition(rigTarget.position + moveDelta);
            }
            else
            {
                rigTarget.position += moveDelta;
            }
        }
    }

    void OnGUI()
    {
        if (!showOnScreenDebug) return;

        // Lazy initialize cached GUIStyles to prevent native IMGUI memory leaks
        if (cachedBoxStyle == null)
        {
            cachedBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12
            };
            cachedBoxStyle.normal.textColor = Color.white;
        }

        GUILayout.BeginArea(new Rect(15, 15, 470, 345), cachedBoxStyle);
        GUILayout.Label("<b><size=14>== PROJECT NIMBUS: BROOM FLIGHT ==</size></b>");

        if (sp != null && sp.IsOpen)
        {
            GUILayout.Label($"Serial: <color=#00FF66><b>CONNECTED ({portName} @ {baudRate})</b></color> [{measuredPacketsPerSec} PPS | Total: {totalPacketsReceived}]");
        }
        else
        {
            string err = string.IsNullOrEmpty(lastConnectionError) ? "Searching hardware..." : lastConnectionError;
            if (err.Length > 32) err = err.Substring(0, 29) + "...";
            GUILayout.Label($"Serial: <color=#FFAA00><b>DISCONNECTED ({portName})</b></color> <size=10><i>[{err}]</i></size>");
        }

        GUILayout.Label($"Raw String: <size=11><color=#FFFF88>{latestTelemetryRaw}</color></size>");
        GUILayout.Label($"Thrust (FSR 1): Raw <b>{rawThrust:F0}</b> [{fsr1MinThreshold:F0}..{rawThrustMax:F0}] -> <b>{targetThrust}%</b> | Speed: <b>{currentSpeed:F1} m/s</b>");
        GUILayout.Label($"Altitude (FSR 2): Raw <b>{rawAltitude:F0}</b> [{fsr2MinThreshold:F0}..{rawAltitudeMax:F0}] -> <b>{targetAltitude}%</b> | VertSpeed: <b>{currentVerticalSpeed:F1} m/s</b>");
        GUILayout.Label($"FSR 3 (Left Turn): Raw <b>{rawLeftTurn:F0}</b> [{fsr3MinThreshold:F0}..{rawLeftTurnMax:F0}] -> <b>{targetLeftTurn}%</b> (Sens: {fsr3Sensitivity:F1})");
        GUILayout.Label($"FSR 4 (Right Turn): Raw <b>{rawRightTurn:F0}</b> [{fsr4MinThreshold:F0}..{rawRightTurnMax:F0}] -> <b>{targetRightTurn}%</b> (Sens: {fsr4Sensitivity:F1})");
        GUILayout.Label($"Net Steering: <b>{smoothTurnInput:F2}</b> {(smoothTurnInput < -0.05f ? "<color=#00FFFF><-- TURNING LEFT</color>" : (smoothTurnInput > 0.05f ? "<color=#00FF66>TURNING RIGHT --></color>" : "<color=#888888>CENTERED</color>"))}");
        GUILayout.Label($"Rig Y: <b>{(rigTarget != null ? rigTarget.position.y.ToString("F2") : "NULL")} m</b> | Grounded: <b>{(lastGroundedState ? "<color=#00FF66>TRUE</color>" : "<color=#00FFFF>AIRBORNE</color>")}</b>");
        GUILayout.Label($"Target: <b>{(rigTarget != null ? rigTarget.name : "NULL")}</b> | FPS: <b>{(1f / Time.unscaledDeltaTime):F0}</b>");
        GUILayout.EndArea();
    }

    void ParseSerialData()
    {
        string rawLine = null;
        lock (dataLock)
        {
            if (!string.IsNullOrEmpty(lastData))
            {
                rawLine = lastData;
                lastData = null;
            }
        }

        if (string.IsNullOrEmpty(rawLine)) return;

        string cleanData = rawLine.Trim();
        if (string.IsNullOrEmpty(cleanData)) return;

        latestTelemetryRaw = cleanData;
        totalPacketsReceived++;
        packetsInLastSecond++;

        // Ignore Arduino banner / header comments
        if (cleanData.StartsWith("-") || cleanData.StartsWith("=") || cleanData.StartsWith("*") || cleanData.StartsWith("#") || !HasDigits(cleanData))
        {
            if (debugLogs) Debug.Log("[BroomFlight Arduino Banner] " + cleanData);
            return;
        }

        try
        {
            string[] items = cleanData.Split(',');
            bool parsedThrust = false;
            bool parsedAltitude = false;
            bool parsedLeftTurn = false;
            bool parsedRightTurn = false;

            bool hasRawThrust = false;
            bool hasRawAltitude = false;
            bool hasRawLeftTurn = false;
            bool hasRawRightTurn = false;

            bool hasBroomThrust = false;
            bool hasBroomAltitude = false;
            bool hasBroomLeftTurn = false;
            bool hasBroomRightTurn = false;

            float readRawThrust = 0f;
            float readRawAltitude = 0f;
            float readRawLeftTurn = 0f;
            float readRawRightTurn = 0f;

            float readBroomThrust = 0f;
            float readBroomAltitude = 0f;
            float readBroomLeftTurn = 0f;
            float readBroomRightTurn = 0f;

            // 1. Scan key-value pairs
            foreach (string item in items)
            {
                string[] parts = item.Split(':');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().ToLower();
                    if (float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        // Raw sensor readings
                        if (key == "raw_thrust" || key == "rawthrust" || key == "fsr1" || key == "raw_fsr1")
                        {
                            readRawThrust = val;
                            hasRawThrust = true;
                        }
                        else if (key == "raw_altitude" || key == "rawaltitude" || key == "raw_alt" || key == "fsr2" || key == "raw_fsr2")
                        {
                            readRawAltitude = val;
                            hasRawAltitude = true;
                        }
                        else if (key == "raw_leftturn" || key == "rawleftturn" || key == "raw_left" || key == "fsr3" || key == "raw_fsr3")
                        {
                            readRawLeftTurn = val;
                            hasRawLeftTurn = true;
                        }
                        else if (key == "raw_rightturn" || key == "rawrightturn" || key == "raw_right" || key == "fsr4" || key == "raw_fsr4")
                        {
                            readRawRightTurn = val;
                            hasRawRightTurn = true;
                        }
                        // Arduino-processed percentage readings
                        else if (key == "broom_speed" || key == "speed" || key == "thrust")
                        {
                            readBroomThrust = val;
                            hasBroomThrust = true;
                        }
                        else if (key == "broom_altitude" || key == "altitude" || key == "climb" || key == "lift")
                        {
                            readBroomAltitude = val;
                            hasBroomAltitude = true;
                        }
                        else if (key == "broom_leftturn" || key == "broom_left" || key == "leftturn" || key == "left")
                        {
                            readBroomLeftTurn = val;
                            hasBroomLeftTurn = true;
                        }
                        else if (key == "broom_rightturn" || key == "broom_right" || key == "rightturn" || key == "right")
                        {
                            readBroomRightTurn = val;
                            hasBroomRightTurn = true;
                        }
                    }
                }
            }

            // 2. Update live raw ADC tracking variables (so HUD & logs always reflect real hardware)
            if (hasRawThrust) rawThrust = readRawThrust;
            if (hasRawAltitude) rawAltitude = readRawAltitude;
            if (hasRawLeftTurn) rawLeftTurn = readRawLeftTurn;
            if (hasRawRightTurn) rawRightTurn = readRawRightTurn;

            // 3. Calibrate and assign Steering (FSR 3 & FSR 4):
            // When preferRawSteeringSensors is enabled (default), we calculate steering percentages
            // in Unity using our dedicated independent thresholds, sensitivities, and response curves.
            if (preferRawSteeringSensors && hasRawLeftTurn)
            {
                serialLeftTurn = ScaleFSR(rawLeftTurn, fsr3MinThreshold, rawLeftTurnMax, fsr3Sensitivity, fsr3ResponseCurve);
                parsedLeftTurn = true;
            }
            else if (hasBroomLeftTurn)
            {
                serialLeftTurn = Mathf.RoundToInt(Mathf.Clamp(readBroomLeftTurn, 0f, 100f));
                parsedLeftTurn = true;
            }
            else if (hasRawLeftTurn)
            {
                serialLeftTurn = ScaleFSR(rawLeftTurn, fsr3MinThreshold, rawLeftTurnMax, fsr3Sensitivity, fsr3ResponseCurve);
                parsedLeftTurn = true;
            }

            if (preferRawSteeringSensors && hasRawRightTurn)
            {
                serialRightTurn = ScaleFSR(rawRightTurn, fsr4MinThreshold, rawRightTurnMax, fsr4Sensitivity, fsr4ResponseCurve);
                parsedRightTurn = true;
            }
            else if (hasBroomRightTurn)
            {
                serialRightTurn = Mathf.RoundToInt(Mathf.Clamp(readBroomRightTurn, 0f, 100f));
                parsedRightTurn = true;
            }
            else if (hasRawRightTurn)
            {
                serialRightTurn = ScaleFSR(rawRightTurn, fsr4MinThreshold, rawRightTurnMax, fsr4Sensitivity, fsr4ResponseCurve);
                parsedRightTurn = true;
            }

            // 4. Calibrate and assign Thrust & Altitude (FSR 1 & FSR 2):
            // When preferRawThrustAltitudeSensors is enabled, we calibrate raw ADC readings
            // with independent FSR 1 and FSR 2 min/max thresholds, sensitivities, and response curves.
            if (preferRawThrustAltitudeSensors && hasRawThrust)
            {
                serialThrust = ScaleFSR(rawThrust, fsr1MinThreshold, rawThrustMax, fsr1Sensitivity, fsr1ResponseCurve);
                parsedThrust = true;
            }
            else if (hasBroomThrust)
            {
                serialThrust = Mathf.RoundToInt(Mathf.Clamp(readBroomThrust, 0f, 100f));
                parsedThrust = true;
            }
            else if (hasRawThrust)
            {
                serialThrust = ScaleFSR(rawThrust, fsr1MinThreshold, rawThrustMax, fsr1Sensitivity, fsr1ResponseCurve);
                parsedThrust = true;
            }

            if (preferRawThrustAltitudeSensors && hasRawAltitude)
            {
                serialAltitude = ScaleFSR(rawAltitude, fsr2MinThreshold, rawAltitudeMax, fsr2Sensitivity, fsr2ResponseCurve);
                parsedAltitude = true;
            }
            else if (hasBroomAltitude)
            {
                serialAltitude = Mathf.RoundToInt(Mathf.Clamp(readBroomAltitude, 0f, 100f));
                parsedAltitude = true;
            }
            else if (hasRawAltitude)
            {
                serialAltitude = ScaleFSR(rawAltitude, fsr2MinThreshold, rawAltitudeMax, fsr2Sensitivity, fsr2ResponseCurve);
                parsedAltitude = true;
            }

            // 5. Fallback for unlabeled comma-separated numbers (e.g. "300, 700, 150, 0")
            if (!parsedThrust && !parsedAltitude && !parsedLeftTurn && !parsedRightTurn && items.Length >= 1)
            {
                if (float.TryParse(items[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val1))
                {
                    rawThrust = val1;
                    serialThrust = ScaleFSR(val1, fsr1MinThreshold, rawThrustMax, fsr1Sensitivity, fsr1ResponseCurve);
                    parsedThrust = true;
                }
                if (items.Length >= 2 && float.TryParse(items[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val2))
                {
                    rawAltitude = val2;
                    serialAltitude = ScaleFSR(val2, fsr2MinThreshold, rawAltitudeMax, fsr2Sensitivity, fsr2ResponseCurve);
                    parsedAltitude = true;
                }
                if (items.Length >= 3 && float.TryParse(items[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val3))
                {
                    rawLeftTurn = val3;
                    serialLeftTurn = ScaleFSR(val3, fsr3MinThreshold, rawLeftTurnMax, fsr3Sensitivity, fsr3ResponseCurve);
                    parsedLeftTurn = true;
                }
                if (items.Length >= 4 && float.TryParse(items[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val4))
                {
                    rawRightTurn = val4;
                    serialRightTurn = ScaleFSR(val4, fsr4MinThreshold, rawRightTurnMax, fsr4Sensitivity, fsr4ResponseCurve);
                    parsedRightTurn = true;
                }
            }

            if (parsedThrust || parsedAltitude || parsedLeftTurn || parsedRightTurn)
            {
                lastPacketReceivedTime = Time.time;
            }
        }
        catch (System.Exception e)
        {
            if (debugLogs)
            {
                Debug.LogError($"[BroomFlight] Parse Error: '{cleanData}' | {e.Message}");
            }
        }
    }

    private int ScaleSensorValue(float val, float maxVal)
    {
        if (autoScaleSensorInput && maxVal > rawInputMin)
        {
            float pct = Mathf.InverseLerp(rawInputMin, maxVal, val) * 100f;
            return Mathf.RoundToInt(Mathf.Clamp(pct, 0f, 100f));
        }
        else
        {
            return Mathf.RoundToInt(Mathf.Clamp(val, 0f, 100f));
        }
    }

    /// <summary>
    /// Scales an individual FSR ADC reading (0-1023) to a calibrated 0-100% control percentage,
    /// applying custom deadzone, max ceiling, sensitivity multiplier, and non-linear response curves.
    /// </summary>
    private int ScaleFSR(float rawVal, float minVal, float maxVal, float sensitivity, float responseCurve)
    {
        if (maxVal <= minVal) maxVal = minVal + 1f;

        // Below deadzone = 0%
        if (rawVal <= minVal)
        {
            return 0;
        }

        // Normalized 0.0 to 1.0 between deadzone and max
        float normalized = Mathf.Clamp01((rawVal - minVal) / (maxVal - minVal));

        // Apply non-linear response curve (curve > 1 gives finer gentle control, curve < 1 gives immediate kick)
        if (Mathf.Abs(responseCurve - 1.0f) > 0.01f && responseCurve > 0.01f)
        {
            normalized = Mathf.Pow(normalized, responseCurve);
        }

        // Apply sensitivity multiplier
        float scaled = normalized * sensitivity * 100f;

        return Mathf.RoundToInt(Mathf.Clamp(scaled, 0f, 100f));
    }

    private bool HasDigits(string input)
    {
        foreach (char c in input)
        {
            if (char.IsDigit(c)) return true;
        }
        return false;
    }

    void OnDisable()
    {
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

    /// <summary>
    /// Safely terminates the background thread BEFORE closing the underlying serial port handle.
    /// This prevents native Win32 kernel crashes and mono thread abort exceptions.
    /// </summary>
    private void CloseSerialConnection()
    {
        isRunning = false;

        // 1. Give the reader thread up to 500ms to exit cooperatively
        if (serialThread != null && serialThread.IsAlive)
        {
            try
            {
                serialThread.Join(500);
            }
            catch (System.Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"[BroomFlight] Thread Join Warning: {ex.Message}");
            }
            finally
            {
                serialThread = null;
            }
        }

        // 2. Safely close and dispose the SerialPort stream
        if (sp != null)
        {
            try
            {
                if (sp.IsOpen)
                {
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    sp.Close();
                }
            }
            catch (System.Exception) { }

            try
            {
                sp.Dispose();
            }
            catch (System.Exception) { }
            finally
            {
                sp = null;
            }
        }

        isThreadActive = false;
    }
}