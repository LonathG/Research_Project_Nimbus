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

    [Header("Flight Physics - Forward")]
    public float speedMultiplier = 25.0f;
    public float accelerationSmooth = 0.05f;
    public float decelerationSmooth = 0.02f;

    [Header("Flight Physics - Altitude (FSR 2)")]
    [Tooltip("Ascent speed when pressure is applied to FSR 2.")]
    public float maxClimbSpeed = 10.0f;
    [Tooltip("Descent / gravity fall speed when no pressure is applied to FSR 2.")]
    public float gravityFallSpeed = 5.0f;
    [Tooltip("Smooth transition factor for vertical velocity.")]
    public float altitudeSmooth = 0.08f;
    [Tooltip("Minimum Y height floor level to prevent falling into void.")]
    public float minGroundY = 0.0f;
    public bool useGroundLimit = true;

    [Header("Steering Settings")]
    public float turnSensitivity = 1.5f;
    public float tiltThreshold = 8f;

    [Header("Sensor Mapping Settings")]
    [Tooltip("Minimum expected raw sensor value from hardware (e.g. 0).")]
    public float rawInputMin = 0f;
    [Tooltip("Maximum expected raw sensor value for Thrust FSR 1 (e.g. 7).")]
    public float rawThrustMax = 7f;
    [Tooltip("Maximum expected raw sensor value for Altitude FSR 2 (e.g. 1 for 0-1 switch, 7 for 0-7 handle).")]
    public float rawAltitudeMax = 1f;
    [Tooltip("Automatically scale hardware values to 0-100% flight thrust & altitude.")]
    public bool autoScaleSensorInput = true;

    [Header("Testing & Debug Fallback")]
    [Tooltip("Allow pressing W/Up to thrust, Space/E to fly up, Q to fall for testing without hardware.")]
    public bool enableKeyboardFallback = true;
    public bool debugLogs = true;

    private SerialPort sp;
    private Thread serialThread;
    private string lastData = "";
    private bool isRunning = true;

    private float currentSpeed = 0f;
    private float currentVerticalSpeed = 0f;
    private int targetThrust = 0;
    private int targetAltitude = 0;
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
        // Parse incoming Arduino serial inputs (Forward Thrust & Altitude FSRs)
        ParseSerialData();

        // Keyboard / Editor Testing Fallback
        if (enableKeyboardFallback)
        {
            // Thrust Keyboard Controls
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                targetThrust = 100;
            }
            else if (sp == null || !sp.IsOpen)
            {
                targetThrust = 0;
            }

            // Altitude Keyboard Controls (Space / E = Fly UP)
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E))
            {
                targetAltitude = 100;
            }
            else if (sp == null || !sp.IsOpen)
            {
                targetAltitude = 0;
            }
        }

        // 0. Check Grounded State
        bool isGrounded = false;
        if (characterController != null && characterController.enabled)
        {
            isGrounded = characterController.isGrounded;
        }
        else if (useGroundLimit && rigTarget != null && rigTarget.position.y <= minGroundY + 0.05f)
        {
            isGrounded = true;
        }

        // 1. Calculate Target & Current Horizontal Speed
        float targetSpeed = targetThrust * speedMultiplier;
        float lerpFactor = (targetSpeed > currentSpeed) ? accelerationSmooth : decelerationSmooth;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, lerpFactor);

        // 2. Calculate Vertical Climb vs Fall Velocity
        float targetVertSpeed = 0f;
        if (targetAltitude > 0)
        {
            // Pressure registered on FSR 2 -> Fly UP proportional to force
            float climbRatio = targetAltitude / 100f;
            targetVertSpeed = climbRatio * maxClimbSpeed;
        }
        else
        {
            if (isGrounded)
            {
                // When grounded with no climb pressure, use a gentle grounding force to prevent physics bouncing ("jumping jacks")
                targetVertSpeed = -0.5f;
            }
            else
            {
                // Falling back to ground under gravity
                targetVertSpeed = -gravityFallSpeed;
            }
        }

        currentVerticalSpeed = Mathf.Lerp(currentVerticalSpeed, targetVertSpeed, altitudeSmooth);

        // Debug Periodic Console Summary
        logTimer += Time.deltaTime;
        if (logTimer > 1.0f)
        {
            if (debugLogs)
            {
                Debug.Log($"[BroomFlight Status] Thrust: {targetThrust}% | Altitude: {targetAltitude}% | HSpeed: {currentSpeed:F1} | VSpeed: {currentVerticalSpeed:F1} | Grounded: {isGrounded}");
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

        // MOVEMENT (Forward Flight + Vertical Ascent/Descent)
        Vector3 moveDelta = Vector3.zero;

        // Forward flight vector along camera orientation
        if (currentSpeed > 0.001f)
        {
            Vector3 forwardDir = vrCamera.forward;
            if (isGrounded && forwardDir.y < 0)
            {
                // Flatten forward pitch when grounded so looking down doesn't push into floor
                forwardDir.y = 0;
                if (forwardDir.sqrMagnitude > 0.001f) forwardDir.Normalize();
            }
            moveDelta += forwardDir * currentSpeed * Time.deltaTime;
        }

        // Add Vertical velocity delta (Climb / Gravity)
        moveDelta.y += currentVerticalSpeed * Time.deltaTime;

        // Clamp height if ground limit is enabled and no CharacterController physics collision is active
        if (useGroundLimit && (characterController == null || !characterController.enabled) && (rigTarget.position.y + moveDelta.y) < minGroundY)
        {
            float clampDeltaY = minGroundY - rigTarget.position.y;
            if (moveDelta.y < clampDeltaY)
            {
                moveDelta.y = clampDeltaY;
                currentVerticalSpeed = 0f;
            }
        }

        // Apply movement vector to Rig Target
        if (moveDelta.sqrMagnitude > 0.000001f)
        {
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

    void ParseSerialData()
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
            bool parsedThrust = false;
            bool parsedAltitude = false;

            // 1. Process key-value pairs (e.g. Broom_Speed:100, Broom_Altitude:100 or Raw_Thrust:7, Raw_Altitude:7)
            foreach (string item in items)
            {
                string[] parts = item.Split(':');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().ToLower();
                    if (float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        if (key == "broom_speed" || key.Contains("speed") || key == "thrust" || key == "raw_thrust")
                        {
                            targetThrust = ScaleSensorValue(val, rawThrustMax);
                            parsedThrust = true;
                        }
                        else if (key == "broom_altitude" || key.Contains("altitude") || key == "climb" || key == "lift" || key == "raw_altitude")
                        {
                            targetAltitude = ScaleSensorValue(val, rawAltitudeMax);
                            parsedAltitude = true;
                        }
                    }
                }
            }

            // 2. Fallback for unlabeled comma-separated numbers (e.g. "7, 1" -> thrust, altitude)
            if (!parsedThrust && !parsedAltitude && items.Length >= 1)
            {
                if (float.TryParse(items[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val1))
                {
                    targetThrust = ScaleSensorValue(val1, rawThrustMax);
                }
                if (items.Length >= 2 && float.TryParse(items[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val2))
                {
                    targetAltitude = ScaleSensorValue(val2, rawAltitudeMax);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BroomFlight] Parse Error: '" + cleanData + "' | " + e.Message);
        }
    }

    private int ScaleSensorValue(float val, float maxVal)
    {
        if (autoScaleSensorInput && maxVal > rawInputMin)
        {
            // If input value is within maxVal range (e.g. 0-1 for FSR 2 or 0-7 for FSR 1), map to 0-100 percentage.
            if (val <= maxVal)
            {
                float pct = Mathf.InverseLerp(rawInputMin, maxVal, val) * 100f;
                return Mathf.RoundToInt(Mathf.Clamp(pct, 0f, 100f));
            }
            else
            {
                return Mathf.RoundToInt(Mathf.Clamp(val, 0f, 100f));
            }
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