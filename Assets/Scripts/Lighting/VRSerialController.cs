using System;
using System.IO.Ports;
using UnityEngine;

public class VRSerialController : MonoBehaviour
{
    [Header("Serial Configuration")]
    [Tooltip("Enter your Arduino COM port (e.g., COM8)")]
    public string portName = "COM8";
    public int baudRate = 115200;

    [Header("Optimization")]
    [Tooltip("Minimum time (in seconds) between speed updates to prevent flooding the Arduino")]
    public float minTimeBetweenSpeedUpdates = 0.08f; // ~12 updates per second

    [Header("VR Rig Speed & Boost Tracking")]
    [Tooltip("Target transform to track (e.g. XROrigin / VR Rig root). If unassigned, automatically located.")]
    public Transform rigTarget;
    [Tooltip("VR Camera transform. If unassigned, uses Camera.main.")]
    public Transform vrCamera;
    [Tooltip("Direct reference to BroomFlightController if flying a broom.")]
    public BroomFlightController flightController;

    [Tooltip("Speed in m/s that corresponds to 100% lighting speed.")]
    public float maxSpeed = 25.0f;
    [Tooltip("Minimum speed threshold (m/s) before boost lights appear.")]
    public float minSpeedThreshold = 0.15f;
    [Tooltip("Minimum boost light percentage (0-100) when movement starts so LEDs are immediately visible.")]
    [Range(10, 50)]
    public int minActiveSpeedPercent = 25;
    [Tooltip("If enabled, automatically reads XR Rig / Broom movement and updates lighting speed.")]
    public bool autoTrackRigSpeed = true;

    [Header("Diagnostics & HUD")]
    public bool showDebugLogs = true;
    public bool showOnScreenDebug = true;

    private SerialPort serialPort;
    private int lastTransmittedSpeed = -1;
    private float lastSpeedUpdateTime = 0f;
    private float ringPriorityUntil = 0f;

    // Position tracking buffers
    private Vector3 lastRigPosition;
    private bool hasRigPosition = false;
    private Vector3 lastCameraPosition;
    private bool hasCameraPosition = false;

    private float smoothSpeed = 0f;
    private float logTimer = 0f;
    private CharacterController characterController;
    private Rigidbody rigRigidbody;

    void Start()
    {
        ResolveRigReferences();
        InitializeSerial();
    }

    private void ResolveRigReferences()
    {
        // 1. Locate BroomFlightController if present
        if (flightController == null)
        {
            flightController = FindObjectOfType<BroomFlightController>();
        }

        // 2. Locate VR Camera
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
        }

        // 3. Locate Rig Target (XR Origin Root)
        if (rigTarget == null)
        {
            if (flightController != null && flightController.rigTarget != null)
            {
                rigTarget = flightController.rigTarget;
            }
            else if (vrCamera != null && vrCamera.root != null && vrCamera.root != vrCamera)
            {
                // In XR Origin: Camera -> Camera Offset -> XR Origin (root)
                rigTarget = vrCamera.root;
            }
            else
            {
                GameObject xrObj = GameObject.Find("XR Origin (VR)") ?? GameObject.Find("XR Origin") ?? GameObject.Find("XROrigin");
                if (xrObj != null) rigTarget = xrObj.transform;
            }
        }

        // 4. Cache physics components on Rig Target
        if (rigTarget != null)
        {
            lastRigPosition = rigTarget.position;
            hasRigPosition = true;
            characterController = rigTarget.GetComponentInChildren<CharacterController>();
            rigRigidbody = rigTarget.GetComponentInChildren<Rigidbody>();
        }

        if (vrCamera != null)
        {
            lastCameraPosition = vrCamera.position;
            hasCameraPosition = true;
        }
    }

    private void InitializeSerial()
    {
        try
        {
            CloseSerial();

            serialPort = new SerialPort(portName, baudRate)
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

            Debug.Log($"[VRSerialController] Successfully connected to Arduino on {portName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRSerialController] Failed to open {portName}. Is the Arduino plugged in? Is the Serial Monitor closed? Error: {e.Message}");
            CloseSerial();
        }
    }

    private void SendCommand(string command)
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Write(command + "\n");
                if (showDebugLogs)
                {
                    Debug.Log($"[VRSerialController] Sent: {command}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRSerialController] Failed to send command. Connection lost? Error: {e.Message}");
            }
        }
        else
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[VRSerialController] Cannot send command '{command}'. Serial port is not open.");
            }
        }
    }

    // ==========================================
    // --- PUBLIC API (FOR GAMEPLAY SCRIPTS) ---
    // ==========================================

    public void SetSpeed(int speed)
    {
        // 1. Clamp speed between 0 and 100
        speed = Mathf.Clamp(speed, 0, 100);

        // 2. If ring animation is currently playing, prioritize ring lighting
        if (Time.time < ringPriorityUntil) return;

        // 3. Only send if speed actually changed and interval has elapsed
        if (speed != lastTransmittedSpeed && Time.time - lastSpeedUpdateTime >= minTimeBetweenSpeedUpdates)
        {
            SendCommand($"C:SPD:{speed}");
            lastTransmittedSpeed = speed;
            lastSpeedUpdateTime = Time.time;
        }
    }

    public void TriggerRing()
    {
        ringPriorityUntil = Time.time + 1.4f; // Lock priority for 1.4s (RNG_DURATION is 1.2s)
        SendCommand("E:RNG");
    }

    public void TriggerCollision()
    {
        SendCommand("E:COL");
    }

    public void TriggerBoost()
    {
        if (Time.time < ringPriorityUntil) return;
        SendCommand("E:BST");
    }

    public void TriggerDanger()
    {
        SendCommand("E:DNG");
    }

    public void TriggerVictory()
    {
        SendCommand("E:WIN");
    }

    // ==========================================
    // --- UPDATE & INPUT HANDLING ---
    // ==========================================
    void Update()
    {
        // 1. Read incoming feedback from Arduino
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                while (serialPort.BytesToRead > 0)
                {
                    string incoming = serialPort.ReadLine();
                    if (!string.IsNullOrEmpty(incoming))
                    {
                        Debug.Log($"[Arduino Lighting] {incoming}");
                    }
                }
            }
            catch (TimeoutException) { }
            catch (Exception) { }
        }

        // 2. Ensure references are resolved if initially absent
        if (rigTarget == null || vrCamera == null)
        {
            ResolveRigReferences();
        }

        // 3. Automatically track XR Origin / Broom movement and update lighting speed
        if (autoTrackRigSpeed)
        {
            TrackAndSendRigSpeed();
        }

        // 4. Testing Hotkeys
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(25);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(50);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetSpeed(75);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SetSpeed(100);

        if (Input.GetKeyDown(KeyCode.R)) TriggerRing();
        if (Input.GetKeyDown(KeyCode.C)) TriggerCollision();
        if (Input.GetKeyDown(KeyCode.B)) TriggerBoost();
        if (Input.GetKeyDown(KeyCode.G)) TriggerDanger();
        if (Input.GetKeyDown(KeyCode.V)) TriggerVictory();
    }

    private void TrackAndSendRigSpeed()
    {
        float currentSpeed = 0f;

        // Source 1: Direct Broom flight speed
        float broomSpeed = 0f;
        if (flightController != null && flightController.enabled)
        {
            broomSpeed = flightController.CurrentSpeed;
            if (broomSpeed > currentSpeed) currentSpeed = broomSpeed;
        }

        // Source 2: Camera World Movement (Foolproof across all VR setups: walking, joystick, head tracking, teleport)
        float cameraSpeed = 0f;
        if (vrCamera != null && Time.deltaTime > 0.0001f)
        {
            if (hasCameraPosition)
            {
                cameraSpeed = (vrCamera.position - lastCameraPosition).magnitude / Time.deltaTime;
                if (cameraSpeed > currentSpeed) currentSpeed = cameraSpeed;
            }
            lastCameraPosition = vrCamera.position;
            hasCameraPosition = true;
        }

        // Source 3: Rig Target Root Displacement
        float rigSpeed = 0f;
        if (rigTarget != null && Time.deltaTime > 0.0001f)
        {
            if (hasRigPosition)
            {
                rigSpeed = (rigTarget.position - lastRigPosition).magnitude / Time.deltaTime;
                if (rigSpeed > currentSpeed) currentSpeed = rigSpeed;
            }
            lastRigPosition = rigTarget.position;
            hasRigPosition = true;
        }

        // Source 4: Physical CharacterController / Rigidbody
        if (characterController != null && characterController.enabled)
        {
            float ccSpeed = characterController.velocity.magnitude;
            if (ccSpeed > currentSpeed) currentSpeed = ccSpeed;
        }
        else if (rigRigidbody != null && !rigRigidbody.isKinematic)
        {
            float rbSpeed = rigRigidbody.linearVelocity.magnitude;
            if (rbSpeed > currentSpeed) currentSpeed = rbSpeed;
        }

        // Source 5: Keyboard Test Fallback (W / Up Arrow)
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            if (currentSpeed < 12f) currentSpeed = 15f; // Simulated flight speed
        }

        // Smooth speed transition to prevent frame-to-frame flicker
        smoothSpeed = Mathf.Lerp(smoothSpeed, currentSpeed, 8f * Time.deltaTime);

        // Map speed to 0-100 percentage
        int speedPercent = 0;
        if (smoothSpeed >= minSpeedThreshold)
        {
            float speedRatio = Mathf.Clamp01((smoothSpeed - minSpeedThreshold) / Mathf.Max(0.01f, maxSpeed - minSpeedThreshold));
            speedPercent = Mathf.RoundToInt(Mathf.Lerp(minActiveSpeedPercent, 100f, speedRatio));
        }

        SetSpeed(speedPercent);

        // Diagnostic Console Logging (Every 1s)
        logTimer += Time.deltaTime;
        if (showDebugLogs && logTimer >= 1.0f)
        {
            logTimer = 0f;
            if (speedPercent > 0)
            {
                Debug.Log($"[VRSerialController] Rig Moving! Speed: {smoothSpeed:F1} m/s -> Lighting: {speedPercent}% | Sources: [Broom: {broomSpeed:F1}, Cam: {cameraSpeed:F1}, Rig: {rigSpeed:F1}]");
            }
        }
    }

    void OnGUI()
    {
        if (!showOnScreenDebug) return;

        bool isOpen = serialPort != null && serialPort.IsOpen;
        GUI.color = isOpen ? Color.green : Color.red;

        string rigName = rigTarget != null ? rigTarget.name : (vrCamera != null ? vrCamera.name : "None");
        string info = $"[Lighting {portName}] {(isOpen ? "CONNECTED" : "DISCONNECTED")} | Speed: {smoothSpeed:F1} m/s ({lastTransmittedSpeed}%) | Rig: '{rigName}'";
        
        GUI.Box(new Rect(10, 10, 520, 26), "");
        GUI.Label(new Rect(15, 12, 510, 24), info);
    }

    // ==========================================
    // --- CONNECTION MANAGEMENT ---
    // ==========================================
    void OnDisable()
    {
        CloseSerial();
    }

    void OnDestroy()
    {
        CloseSerial();
    }

    void OnApplicationQuit()
    {
        CloseSerial();
    }

    private void CloseSerial()
    {
        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                    Debug.Log($"[VRSerialController] Closed connection to {portName}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRSerialController] Error closing serial port: {e.Message}");
            }
            try
            {
                serialPort.Dispose();
            }
            catch { }
            finally
            {
                serialPort = null;
            }
        }
    }
}
