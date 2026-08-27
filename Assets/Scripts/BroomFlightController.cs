using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class BroomFlightController : MonoBehaviour
{
    [Header("Serial Port Settings")]
    public string portName = "COM6";
    public int baudRate = 9600;
    public bool enableSerial = true;

    [Header("Movement Target")]
    [Tooltip("Target GameObject to move (e.g. XROrigin / VR Rig root). If unassigned, automatically uses parent or this transform.")]
    public Transform rigTarget;
    public Transform vrCamera;

    [Header("Flight Physics")]
    public float speedMultiplier = 25.0f;
    public float accelerationSmooth = 0.05f;
    public float decelerationSmooth = 0.02f;

    [Header("Steering Settings")]
    public float turnSensitivity = 1.5f;
    public float tiltThreshold = 8f;

    [Header("Testing & Debug Fallback")]
    [Tooltip("Allow pressing W or Space key to fly for testing without hardware.")]
    public bool enableKeyboardFallback = true;
    public bool debugLogs = true;

    private SerialPort sp;
    private Thread serialThread;
    private string lastData = "";
    private bool isRunning = true;

    private float currentSpeed = 0f;
    private int targetThrust = 0;
    private float logTimer = 0f;

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

        // 2. Resolve Rig Target (CRITICAL: Do not move Camera directly because XR tracking overrides it)
        if (rigTarget == null)
        {
            if (transform == vrCamera && transform.parent != null)
            {
                rigTarget = transform.parent;
                Debug.Log($"[BroomFlight] Script attached to Camera. Redirected movement target to parent VR Rig: {rigTarget.name}");
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
        }

        // 4. Connect Serial Hardware
        if (enableSerial)
        {
            InitSerial();
        }
    }

    void InitSerial()
    {
        try
        {
            string[] availablePorts = SerialPort.GetPortNames();
            if (debugLogs)
            {
                Debug.Log($"[BroomFlight] Available Serial Ports: {string.Join(", ", availablePorts)}");
            }

            sp = new SerialPort(portName, baudRate);
            sp.ReadTimeout = 10;
            sp.Open();

            serialThread = new Thread(ReadSerialData);
            serialThread.IsBackground = true;
            serialThread.Start();
            Debug.Log($"[BroomFlight] Broom Hardware Connected on {portName}!");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BroomFlight] Serial Error on {portName}: {e.Message}. Make sure Arduino is connected to {portName} or test using W/Space keys!");
        }
    }

    void ReadSerialData()
    {
        while (isRunning && sp != null && sp.IsOpen)
        {
            try
            {
                string line = sp.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    lastData = line;
                }
            }
            catch (System.Exception) { }
        }
    }

    void Update()
    {
        // Parse incoming Arduino serial thrust
        ParseThrust();

        // Keyboard / Editor Testing Fallback
        if (enableKeyboardFallback)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow))
            {
                targetThrust = 100;
            }
            else if (sp == null || !sp.IsOpen)
            {
                // If serial port is disconnected and no key pressed
                targetThrust = 0;
            }
        }

        // Calculate Target & Current Speed
        float targetSpeed = targetThrust * speedMultiplier;
        float lerpFactor = (targetSpeed > currentSpeed) ? accelerationSmooth : decelerationSmooth;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, lerpFactor);

        // Debug Periodic Console Summary
        logTimer += Time.deltaTime;
        if (logTimer > 1.0f)
        {
            if (debugLogs)
            {
                Debug.Log($"[BroomFlight Status] TargetThrust: {targetThrust} | CurrentSpeed: {currentSpeed:F2} | Moving Target: {(rigTarget != null ? rigTarget.name : "NULL")}");
            }
            logTimer = 0;
        }

        if (rigTarget == null || vrCamera == null) return;

        // STEERING (Head Tilt Rotation)
        float tiltZ = vrCamera.localEulerAngles.z;
        if (tiltZ > 180) tiltZ -= 360;

        if (Mathf.Abs(tiltZ) > tiltThreshold)
        {
            float turnAmount = -tiltZ * turnSensitivity * Time.deltaTime;
            rigTarget.Rotate(0, turnAmount, 0);
        }

        // MOVEMENT (Forward Flight)
        if (currentSpeed > 0.001f)
        {
            Vector3 moveDir = vrCamera.forward;
            Vector3 moveDelta = moveDir * currentSpeed * Time.deltaTime;

            if (characterController != null && characterController.enabled)
            {
                characterController.Move(moveDelta);
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

    void ParseThrust()
    {
        if (string.IsNullOrEmpty(lastData)) return;

        // Consume line once
        string cleanData = lastData.Trim();
        lastData = null;

        if (string.IsNullOrEmpty(cleanData)) return;

        // Ignore Arduino header/banner info
        if (cleanData.StartsWith("-") || cleanData.StartsWith("=") || cleanData.StartsWith("*") || cleanData.StartsWith("#") || !HasDigits(cleanData))
        {
            if (debugLogs) Debug.Log("[BroomFlight Arduino Info] " + cleanData);
            return;
        }

        try
        {
            string[] items = cleanData.Split(',');
            bool parsed = false;

            // 1. Prioritized search: "Broom_Speed", "Speed", "Thrust"
            foreach (string item in items)
            {
                string[] parts = item.Split(':');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().ToLower();
                    if (key.Contains("speed") || key.Contains("thrust"))
                    {
                        if (float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                        {
                            targetThrust = Mathf.RoundToInt(val);
                            parsed = true;
                            break;
                        }
                    }
                }
            }

            // 2. Secondary search: "Raw_Value", "Raw"
            if (!parsed)
            {
                foreach (string item in items)
                {
                    string[] parts = item.Split(':');
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim().ToLower();
                        if (key.Contains("raw") || key.Contains("val"))
                        {
                            if (float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                            {
                                targetThrust = Mathf.RoundToInt(val);
                                parsed = true;
                                break;
                            }
                        }
                    }
                }
            }

            // 3. Fallback: Direct numeric parsing
            if (!parsed)
            {
                foreach (string item in items)
                {
                    string rawNum = item;
                    if (item.Contains(":"))
                    {
                        string[] parts = item.Split(':');
                        rawNum = parts[parts.Length - 1];
                    }

                    if (float.TryParse(rawNum.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        targetThrust = Mathf.RoundToInt(val);
                        parsed = true;
                        break;
                    }
                }
            }

            if (!parsed && debugLogs)
            {
                Debug.LogWarning("[BroomFlight] Could not parse thrust value from: '" + cleanData + "'");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BroomFlight] Parse Error: '" + cleanData + "' | " + e.Message);
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

    void OnApplicationQuit()
    {
        isRunning = false;
        if (serialThread != null && serialThread.IsAlive)
        {
            serialThread.Abort();
        }
        if (sp != null && sp.IsOpen)
        {
            sp.Close();
        }
    }
}