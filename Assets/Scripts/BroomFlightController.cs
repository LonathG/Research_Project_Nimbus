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

        // 1. Log the exact raw string coming from the Arduino
        Debug.Log("RAW SERIAL DATA: " + lastData);

        try
        {
            string[] values = lastData.Split(',');

            if (values.Length > 0)
            {
                // NOTE: Based on the "Thrust,Pitch,Roll" spec, Thrust is the first value (index 0).
                // If you actually are using colons (e.g., "Speed:150"), you will need to adjust this!
                Debug.Log("ATTEMPTING TO PARSE THRUST FROM: " + values[0]);

                targetThrust = int.Parse(values[0]);

                // 2. Confirming successful parse
                Debug.Log("SUCCESS! TARGET THRUST: " + targetThrust);
            }
        }
        catch (System.Exception e)
        {
            // 3. STOP SWALLOWING ERRORS - This will print exactly why it is failing!
            Debug.LogError("PARSE ERROR! Raw string was: '" + lastData + "' | Error: " + e.Message);
        }
    }
    void OnApplicationQuit()
    {
        isRunning = false;
        if (serialThread != null) serialThread.Abort();
        if (sp.IsOpen) sp.Close();
    }
}