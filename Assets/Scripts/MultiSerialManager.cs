using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Production-grade multi-port serial manager for Project Nimbus VR.
/// Safely manages 3 simultaneous Arduino threads at 9600 baud with zero CPU starvation.
/// </summary>
public class MultiSerialManager : MonoBehaviour
{
    public static MultiSerialManager Instance { get; private set; }

    [System.Serializable]
    public class ArduinoConnectionConfig
    {
        public string deviceName = "Arduino";
        public string portName = "COM6";
        public int baudRate = 115200;
        public int readTimeoutMs = 25;
        public int writeTimeoutMs = 50;
        public bool enabled = true;
    }

    [Header("Hardware Configuration (3 Arduinos)")]
    public ArduinoConnectionConfig broomSensors = new ArduinoConnectionConfig { deviceName = "Broom Rig", portName = "COM6", baudRate = 9600 };
    public ArduinoConnectionConfig hapticJacket = new ArduinoConnectionConfig { deviceName = "Haptics", portName = "COM7", baudRate = 9600 };
    public ArduinoConnectionConfig fanWindRig = new ArduinoConnectionConfig { deviceName = "Wind Fans", portName = "COM12", baudRate = 9600 };

    [Header("Thread Tuning")]
    [Tooltip("Sleep duration in milliseconds inside the reading loop to prevent CPU exhaustion.")]
    [Range(1, 10)]
    public int threadSleepMs = 1;

    [Header("Debug Telemetry")]
    public bool enableDebugLogs = true;

    // Worker references
    private SerialPortWorker worker1;
    private SerialPortWorker worker2;
    private SerialPortWorker worker3;

    // Events for other scripts to subscribe to
    public event Action<string> OnBroomDataReceived;
    public event Action<string> OnHapticDataReceived;
    public event Action<string> OnFanDataReceived;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        // Initialize all 3 serial workers
        if (broomSensors.enabled)
            worker1 = new SerialPortWorker(broomSensors, threadSleepMs, enableDebugLogs);

        if (hapticJacket.enabled)
            worker2 = new SerialPortWorker(hapticJacket, threadSleepMs, enableDebugLogs);

        if (fanWindRig.enabled)
            worker3 = new SerialPortWorker(fanWindRig, threadSleepMs, enableDebugLogs);

        // Start threads
        worker1?.Start();
        worker2?.Start();
        worker3?.Start();
    }

    void Update()
    {
        // Dispatch received data to the Unity Main Thread safely
        worker1?.ProcessIncomingQueue(line => OnBroomDataReceived?.Invoke(line));
        worker2?.ProcessIncomingQueue(line => OnHapticDataReceived?.Invoke(line));
        worker3?.ProcessIncomingQueue(line => OnFanDataReceived?.Invoke(line));
    }

    // Public methods to send data out to any specific Arduino
    public void SendToBroom(string message) => worker1?.Send(message);
    public void SendToHaptics(string message) => worker2?.Send(message);
    public void SendToFans(string message) => worker3?.Send(message);

    void OnDisable() => ShutdownAll();
    void OnDestroy() => ShutdownAll();
    void OnApplicationQuit() => ShutdownAll();

    private bool isShuttingDown = false;
    private void ShutdownAll()
    {
        if (isShuttingDown) return;
        isShuttingDown = true;

        if (enableDebugLogs) Debug.Log("[MultiSerialManager] Safely closing all 3 serial workers...");

        worker1?.StopAndClose();
        worker2?.StopAndClose();
        worker3?.StopAndClose();

        worker1 = null;
        worker2 = null;
        worker3 = null;
    }
}

/// <summary>
/// Encapsulates a single background thread and SerialPort instance with safe error handling.
/// </summary>
public class SerialPortWorker
{
    private readonly MultiSerialManager.ArduinoConnectionConfig config;
    private readonly int sleepMs;
    private readonly bool debug;

    private SerialPort serialPort;
    private Thread workerThread;
    private volatile bool isRunning = false;

    private readonly ConcurrentQueue<string> incomingQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> outgoingQueue = new ConcurrentQueue<string>();

    public bool IsConnected => serialPort != null && serialPort.IsOpen && isRunning;

    public SerialPortWorker(MultiSerialManager.ArduinoConnectionConfig config, int sleepMs, bool debug)
    {
        this.config = config;
        this.sleepMs = sleepMs;
        this.debug = debug;
    }

    public void Start()
    {
        try
        {
            serialPort = new SerialPort(config.portName, config.baudRate)
            {
                ReadTimeout = config.readTimeoutMs,
                WriteTimeout = config.writeTimeoutMs
            };

            serialPort.Open();

            try
            {
                serialPort.DtrEnable = true;
                serialPort.RtsEnable = true;
            }
            catch { }

            isRunning = true;

            workerThread = new Thread(ThreadLoop)
            {
                IsBackground = true,
                Name = $"SerialWorker_{config.deviceName}_{config.portName}"
            };
            workerThread.Start();

            if (debug) Debug.Log($"[SerialPortWorker] Connected to {config.deviceName} on {config.portName} ({config.baudRate} baud).");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SerialPortWorker] Failed to open {config.deviceName} on {config.portName}: {ex.Message}");
            StopAndClose();
        }
    }

    private void ThreadLoop()
    {
        while (isRunning)
        {
            bool hadActivity = false;

            if (serialPort != null && serialPort.IsOpen)
            {
                // 1. Process Outgoing Commands (Unity -> Arduino)
                while (outgoingQueue.TryDequeue(out string outCmd))
                {
                    try
                    {
                        serialPort.WriteLine(outCmd);
                        hadActivity = true;
                    }
                    catch (TimeoutException) { }
                    catch (Exception ex)
                    {
                        if (debug) Debug.LogWarning($"[{config.deviceName}] Write error: {ex.Message}");
                        break;
                    }
                }

                // 2. Process Incoming Telemetry (Arduino -> Unity)
                try
                {
                    string line = serialPort.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (incomingQueue.Count < 50)
                        {
                            incomingQueue.Enqueue(line);
                        }
                        hadActivity = true;
                    }
                }
                catch (TimeoutException)
                {
                    // Normal occurrence when waiting for next sensor reading
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    if (isRunning && debug)
                    {
                        Debug.LogWarning($"[{config.deviceName}] Unexpected read error: {ex.Message}");
                    }
                }
            }
            else
            {
                Thread.Sleep(50);
            }

            // 3. Relinquish CPU Timeslice to prevent thread starvation
            if (!hadActivity)
            {
                Thread.Sleep(sleepMs);
            }
            else
            {
                Thread.Yield();
            }
        }
    }

    public void Send(string message)
    {
        if (IsConnected)
        {
            outgoingQueue.Enqueue(message);
        }
    }

    public void ProcessIncomingQueue(Action<string> onLineReceived)
    {
        while (incomingQueue.TryDequeue(out string line))
        {
            onLineReceived?.Invoke(line);
        }
    }

    public void StopAndClose()
    {
        isRunning = false;

        // 1. Safely join the thread FIRST before closing the port handle
        if (workerThread != null && workerThread.IsAlive)
        {
            try
            {
                workerThread.Join(500);
            }
            catch (Exception) { }
            finally
            {
                workerThread = null;
            }
        }

        // 2. Now safe to close and dispose the port
        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            }
            catch (Exception) { }
            try
            {
                serialPort.Dispose();
            }
            catch (Exception) { }
            finally
            {
                serialPort = null;
            }
        }
    }
}
