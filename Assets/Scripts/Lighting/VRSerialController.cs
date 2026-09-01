using System;
using System.IO.Ports;
using UnityEngine;

public class VRSerialController : MonoBehaviour
{
    [Header("Serial Configuration")]
    [Tooltip("Enter your Arduino COM port (e.g., COM3)")]
    public string portName = "COM3";
    public int baudRate = 115200;

    [Header("Optimization")]
    [Tooltip("Minimum time (in seconds) between speed updates to prevent flooding the Arduino")]
    public float minTimeBetweenSpeedUpdates = 0.1f; // Max 10 updates per second

    private SerialPort serialPort;
    private int lastTransmittedSpeed = -1;
    private float lastSpeedUpdateTime = 0f;

    void Start()
    {
        InitializeSerial();
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
                // The Arduino parser expects a newline at the end of every command
                serialPort.Write(command + "\n");
                Debug.Log($"[VRSerialController] Sent: {command}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRSerialController] Failed to send command. Connection lost? Error: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"[VRSerialController] Cannot send command '{command}'. Serial port is not open.");
        }
    }

    // ==========================================
    // --- PUBLIC API (FOR GAMEPLAY SCRIPTS) ---
    // ==========================================

    public void SetSpeed(int speed)
    {
        // 1. Clamp speed to ensure it always stays between 0 and 100
        speed = Mathf.Clamp(speed, 0, 100);

        // 2. Optimization: Only send if the speed actually changed 
        // AND enough time has passed to prevent flooding the serial buffer.
        if (speed != lastTransmittedSpeed && Time.time - lastSpeedUpdateTime >= minTimeBetweenSpeedUpdates)
        {
            SendCommand($"C:SPD:{speed}");
            lastTransmittedSpeed = speed;
            lastSpeedUpdateTime = Time.time;
        }
    }
    public void TriggerRing()
{
    SendCommand("E:RNG");
}
    public void TriggerCollision()
    {
        SendCommand("E:COL");
    }

    public void TriggerBoost()
    {
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
    // --- TESTING MODE (KEYBOARD CONTROLS) ---
    // ==========================================
    void Update()
    {
        // Allow manual testing of the bridge before real VR integration
        
        // Continuous States
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(25);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(50);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetSpeed(75);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SetSpeed(100);

        // Discrete Events
        if (Input.GetKeyDown(KeyCode.C)) TriggerCollision();
        if (Input.GetKeyDown(KeyCode.B)) TriggerBoost();
        if (Input.GetKeyDown(KeyCode.D)) TriggerDanger();
        if (Input.GetKeyDown(KeyCode.V)) TriggerVictory();
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
