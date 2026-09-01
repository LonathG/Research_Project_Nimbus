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

    [Header("Steering Settings")]
    public float turnSensitivity = 1.5f;
    public float tiltThreshold = 8f;

    [Header("Sensor Mapping Settings")]
    [Tooltip("Minimum expected raw sensor value from hardware (deadzone).")]
    public float rawInputMin = 15f;
    [Tooltip("Maximum expected raw sensor value for Thrust FSR 1 (e.g. 350).")]
    public float rawThrustMax = 350f;
    [Tooltip("Maximum expected raw sensor value for Altitude FSR 2 (e.g. 700).")]
    public float rawAltitudeMax = 700f;
    [Tooltip("Automatically scale hardware values to 0-100% flight thrust & altitude.")]
    public bool autoScaleSensorInput = true;

    [Header("Testing & Debug Fallback")]
    [Tooltip("Allow pressing W/Up to thrust, Space/E to climb, Q/Ctrl to descend for testing without hardware.")]
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
    private int serialThrust = 0;
    private int serialAltitude = 0;
    private int targetThrust = 0;
    private int targetAltitude = 0;
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
        }

        // 4. Keyboard Fallback Input (Testing without hardware)
        int keyboardThrust = 0;
        int keyboardAltitude = 0;
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
        }

        // Combine Serial Hardware and Keyboard Inputs (Max of both)
        targetThrust = Mathf.Max(serialThrust, keyboardThrust);
        targetAltitude = Mathf.Max(serialAltitude, keyboardAltitude);

        // Smooth inputs to eliminate frame-rate jitter
        smoothThrustInput = Mathf.Lerp(smoothThrustInput, targetThrust, 15f * Time.deltaTime);
        smoothAltitudeInput = Mathf.Lerp(smoothAltitudeInput, targetAltitude, 15f * Time.deltaTime);

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
                Debug.Log($"[BroomFlight Status] {status} | Thrust: {targetThrust}% | Alt: {targetAltitude}% | HSpeed: {currentSpeed:F1}m/s | VSpeed: {currentVerticalSpeed:F1}m/s | Ground: {isGrounded}");
            }
            logTimer = 0;
        }

        if (rigTarget == null || vrCamera == null) return;

        // 8. STEERING (Head Tilt Roll)
        float tiltZ = vrCamera.localEulerAngles.z;
        if (tiltZ > 180) tiltZ -= 360;

        if (Mathf.Abs(tiltZ) > tiltThreshold)
        {
            float turnAmount = -tiltZ * turnSensitivity * Time.deltaTime;
            rigTarget.Rotate(0, turnAmount, 0);
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

        GUILayout.BeginArea(new Rect(15, 15, 420, 280), cachedBoxStyle);
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
        GUILayout.Label($"Thrust: <b>{targetThrust}%</b> (Serial: {serialThrust}%) | Speed: <b>{currentSpeed:F1} m/s</b>");
        GUILayout.Label($"Altitude: <b>{targetAltitude}%</b> (Serial: {serialAltitude}%) | VertSpeed: <b>{currentVerticalSpeed:F1} m/s</b>");
        GUILayout.Label($"Rig Position Y: <b>{(rigTarget != null ? rigTarget.position.y.ToString("F2") : "NULL")} m</b>");
        GUILayout.Label($"Grounded: <b>{(lastGroundedState ? "<color=#00FF66>TRUE (Grounded)</color>" : "<color=#00FFFF>AIRBORNE (Flying)</color>")}</b>");
        GUILayout.Label($"Movement Target: <b>{(rigTarget != null ? rigTarget.name : "NULL")}</b> | Collider: {(characterController != null ? $"CharacterController ({lastCollisionFlags})" : (rb != null ? "Rigidbody" : "Transform"))}");
        GUILayout.Label($"FPS: <b>{(1f / Time.unscaledDeltaTime):F0}</b> | Thread Active: <b>{isThreadActive}</b>");
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

            // 1. Processed percentage keys ("Broom_Speed", "Broom_Altitude", "Speed", "Altitude")
            foreach (string item in items)
            {
                string[] parts = item.Split(':');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().ToLower();
                    if (float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        if (key == "broom_speed" || key == "speed")
                        {
                            serialThrust = Mathf.RoundToInt(Mathf.Clamp(val, 0f, 100f));
                            parsedThrust = true;
                        }
                        else if (key == "broom_altitude" || key == "altitude" || key == "climb" || key == "lift")
                        {
                            serialAltitude = Mathf.RoundToInt(Mathf.Clamp(val, 0f, 100f));
                            parsedAltitude = true;
                        }
                    }
                }
            }

            // 2. Secondary fallback for raw ADC sensor keys ("Raw_Thrust", "Raw_Altitude")
            if (!parsedThrust || !parsedAltitude)
            {
                foreach (string item in items)
                {
                    string[] parts = item.Split(':');
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim().ToLower();
                        if (float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                        {
                            if (!parsedThrust && (key == "raw_thrust" || key.Contains("thrust")))
                            {
                                serialThrust = ScaleSensorValue(val, rawThrustMax);
                                parsedThrust = true;
                            }
                            else if (!parsedAltitude && (key == "raw_altitude" || key.Contains("alt")))
                            {
                                serialAltitude = ScaleSensorValue(val, rawAltitudeMax);
                                parsedAltitude = true;
                            }
                        }
                    }
                }
            }

            // 3. Fallback for unlabeled comma-separated numbers (e.g. "300, 700")
            if (!parsedThrust && !parsedAltitude && items.Length >= 1)
            {
                if (float.TryParse(items[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val1))
                {
                    serialThrust = ScaleSensorValue(val1, rawThrustMax);
                    parsedThrust = true;
                }
                if (items.Length >= 2 && float.TryParse(items[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val2))
                {
                    serialAltitude = ScaleSensorValue(val2, rawAltitudeMax);
                    parsedAltitude = true;
                }
            }

            if (parsedThrust || parsedAltitude)
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