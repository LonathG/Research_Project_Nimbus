using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class BroomFlightController : MonoBehaviour
{
    private SerialPort sp = new SerialPort("COM6", 9600);
    private Thread serialThread;
    private string lastData = "";
    private bool isRunning = true;

    [Header("Flight Physics")]
    // INCREASED SPEED: Changed from 5.0f to 25.0f for a much faster flight
    public float speedMultiplier = 25.0f;
    public float accelerationSmooth = 0.05f;
    public float decelerationSmooth = 0.02f;

    [Header("Steering Settings")]
    public Transform vrCamera;
    public float turnSensitivity = 1.5f;
    public float tiltThreshold = 8f;

    private float currentSpeed = 0f;
    private int targetThrust = 0;
    private float logTimer = 0f; // Timer for printing

    void Start()
    {
        if (vrCamera == null) vrCamera = Camera.main.transform;

        try
        {
            sp.Open();
            sp.ReadTimeout = 10;
            serialThread = new Thread(ReadSerialData);
            serialThread.Start();
            Debug.Log("Broom Hardware Connected!");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Serial Error: " + e.Message);
        }
    }

    void ReadSerialData()
    {
        while (isRunning && sp.IsOpen)
        {
            try { lastData = sp.ReadLine(); }
            catch (System.Exception) { }
        }
    }

    void Update()
    {
        ParseThrust();

        float targetSpeed = targetThrust * speedMultiplier;
        float lerpFactor = (targetSpeed > currentSpeed) ? accelerationSmooth : decelerationSmooth;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, lerpFactor);

        // --- PRINT SPEED TO CONSOLE (Every 0.5 seconds) ---
        logTimer += Time.deltaTime;
        if (logTimer > 0.5f)
        {
            Debug.Log("ACTUAL SPEED: " + currentSpeed.ToString("F2"));
            logTimer = 0;
        }

        // STEERING
        float tiltZ = vrCamera.localEulerAngles.z;
        if (tiltZ > 180) tiltZ -= 360;

        if (Mathf.Abs(tiltZ) > tiltThreshold)
        {
            float turnAmount = -tiltZ * turnSensitivity * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }

        // MOVEMENT
        Vector3 moveDir = vrCamera.forward;
        transform.position += moveDir * currentSpeed * Time.deltaTime;
    }

    void ParseThrust()
    {
        if (string.IsNullOrEmpty(lastData)) return;

        // Consume lastData so each received string is processed only once
        string cleanData = lastData.Trim();
        lastData = null;

        if (string.IsNullOrEmpty(cleanData)) return;

        // Handle Arduino status/header messages (e.g. "--- FSR ONLY MODE: TESTING THRUST ---")
        if (cleanData.StartsWith("-") || cleanData.StartsWith("=") || cleanData.StartsWith("*") || cleanData.StartsWith("#") || !HasDigits(cleanData))
        {
            Debug.Log("ARDUINO INFO: " + cleanData);
            return;
        }

        try
        {
            string[] items = cleanData.Split(',');
            bool parsed = false;

            // 1. Prioritized search: Look for explicit "Broom_Speed", "Speed", or "Thrust" keys
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

            // 2. Secondary search: Check for "Raw_Value" or "Raw" key if speed/thrust was not found
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

            // 3. Fallback: Parse pure numeric values (e.g. "150,10,5" or single number "150")
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

            if (!parsed)
            {
                Debug.LogWarning("Could not parse numeric thrust value from raw string: '" + cleanData + "'");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("PARSE ERROR! Raw string was: '" + cleanData + "' | Error: " + e.Message);
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
        if (serialThread != null) serialThread.Abort();
        if (sp.IsOpen) sp.Close();
    }
}