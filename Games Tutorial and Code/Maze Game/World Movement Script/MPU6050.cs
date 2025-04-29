using UnityEngine;
using System.IO.Ports;
using System;

public class MPU6050 : MonoBehaviour // Attach this script to the MAZE PLATFORM GameObject
{
    public FinshScripte check;
    [Header("Serial Port Settings")]
    [Tooltip("Enter the COM port name (e.g., COM3, COM8)")]
    public string portName = "COM3"; // <<< DOUBLE CHECK THIS VALUE!
    [Tooltip("Must match the Arduino's Serial.begin() speed")]
    public int baudRate = 115200;
    [Tooltip("Milliseconds before a serial read times out.")]
    public int readTimeout = 50;

    [Header("Rotation Control")]
    [Tooltip("Controls how quickly the MAZE PLATFORM rotates to match the sensor. Lower values = smoother.")]
    public float rotationSmoothSpeed = 10.0f; // <<< TUNE THIS VALUE! Start lower for worlds.

    // --- Serial Port Variables ---
    private SerialPort stream;
    private bool isPortOpen = false;
    private string rawReceivedString;
    private string[] splitData;

    // --- Rotation Data ---
    private float qw, qx, qy, qz;
    private Quaternion latestSensorQuaternion = Quaternion.identity;
    private bool newRotationDataAvailable = false;

    // --- Target Rotation ---
    // We will rotate the transform this script is attached to.

    void Start()
    {
        // Optional: Add a check to ensure this object has colliders for the ball?
        // Collider[] colliders = GetComponentsInChildren<Collider>();
        // if (colliders.Length == 0) {
        //     Debug.LogWarning("Maze Platform has no colliders. The ball might fall through.", this);
        // }

        OpenSerialPort();
    }

    void Update() // Read serial data in Update
    {
        if (!isPortOpen || stream == null || !stream.IsOpen ) return;
        if (check.PlayerisFinish) { return; }
        try
        {
            if (stream.BytesToRead > 0)
            {
                rawReceivedString = stream.ReadLine();
                if (!string.IsNullOrEmpty(rawReceivedString))
                {
                    splitData = rawReceivedString.Split(',');
                    if (splitData.Length == 4)
                    {
                        try
                        {
                            qw = float.Parse(splitData[0]);
                            qx = float.Parse(splitData[1]);
                            qy = float.Parse(splitData[2]);
                            qz = float.Parse(splitData[3]);

                            // Store the raw sensor rotation
                            // (Using your original axis mapping: -qy -> X, -qz -> Y, qx -> Z, qw -> W)
                            // IMPORTANT: This mapping determines how sensor tilt maps to Unity axes.
                            // Test and adjust this Quaternion constructor if the tilt directions
                            // (forward/backward, left/right) feel wrong or swapped.
                            // Common alternatives might involve swapping qx, qy, qz or changing signs.
                            latestSensorQuaternion = new Quaternion(-qy, -qz, qx, qw);

                            newRotationDataAvailable = true;
                        }
                        catch (FormatException fe) { Debug.LogWarning($"Parse Error: {fe.Message}"); }
                        catch (Exception e) { Debug.LogError($"Processing Error: {e.Message}"); }
                    }
                    else { Debug.LogWarning($"Incomplete data: '{rawReceivedString}'"); }
                }
            }
        }
        catch (TimeoutException) { /* Normal if no data is sent frequently */ }
        catch (Exception e)
        {
            Debug.LogError($"Serial Read Error: {e.Message}");
            CloseSerialPort();
        }
    }

    void FixedUpdate() // Apply rotation in FixedUpdate for smoother physics interaction
    {
        if (newRotationDataAvailable)
        {
            // --- Rotation Logic (Constrained to Tilt X/Z of this GameObject) ---

            // 1. Convert the latest sensor reading to Euler angles (Pitch, Yaw, Roll)
            //    These angles represent the sensor's orientation in 3D space.
            Vector3 sensorEuler = latestSensorQuaternion.eulerAngles;

            // 2. Get this GameObject's *current* world Y rotation (Yaw).
            //    We want to preserve the current facing direction and only apply tilt.
            //float currentWorldYRotation = transform.eulerAngles.y;

            // 3. Create the target Euler angles for the GameObject:
            //    - Use the sensor's X rotation (Pitch) for the GameObject's X rotation.
            //    - Use the GameObject's *current* Y rotation (Yaw) for the GameObject's Y rotation.
            //    - Use the sensor's Z rotation (Roll) for the GameObject's Z rotation.
            //    *** IMPORTANT AXIS CHECK ***:
            //    Depending on your sensor mounting and the Quaternion mapping in Update(),
            //    you might need to swap sensorEuler.x and sensorEuler.z here, or negate them,
            //    to get the intuitive forward/back (X-axis tilt) and left/right (Z-axis tilt).
            //    Example: If tilting sensor forward makes platform roll left, swap sensorEuler.x and sensorEuler.z.
            Vector3 targetEulerAngles = new Vector3(sensorEuler.x, 0, sensorEuler.z);

            // 4. Convert the target Euler angles back into a target Quaternion.
            Quaternion targetRotation = Quaternion.Euler(targetEulerAngles);

            // 5. Calculate the smoothed rotation using Slerp (Spherical Linear Interpolation).
            //    This smoothly transitions from the current rotation to the target rotation
            //    based on the smoothing speed and fixed delta time.
            Quaternion smoothedRotation = Quaternion.Slerp(
                                            transform.rotation, // Current world rotation
                                            targetRotation,     // Target world rotation (with constrained Y)
                                            Time.fixedDeltaTime * rotationSmoothSpeed // Smoothing factor
                                          );

            // 6. Apply the smoothed, constrained rotation to this GameObject's transform.
            transform.rotation = new Quaternion(smoothedRotation.x, 0,smoothedRotation.z, smoothedRotation.w);

            // --- End Rotation Logic ---

            newRotationDataAvailable = false; // Reset the flag
        }
    }

    // --- Serial Port Management (Keep these methods as before) ---
    void OpenSerialPort()
    {
        try
        {
            if (stream != null && stream.IsOpen) stream.Close();
            stream = new SerialPort(portName, baudRate);
            stream.ReadTimeout = readTimeout;
            // Set DTR and RTS true for stability with some Arduino boards
            stream.DtrEnable = true;
            stream.RtsEnable = true;
            stream.Open();
            stream.DiscardInBuffer(); // Clear any old data
            isPortOpen = true;
            Debug.Log($"Serial port {portName} opened successfully for Maze Platform.");
        }
        catch (System.IO.IOException ioex)
        {
            Debug.LogError($"IO Error opening port {portName} for Maze: {ioex.Message}. Is the port correct and not in use?");
            isPortOpen = false; stream = null;
        }
        catch (UnauthorizedAccessException uaex)
        {
            Debug.LogError($"Access Denied opening port {portName} for Maze: {uaex.Message}. Check permissions or if another program is using it.");
            isPortOpen = false; stream = null;
        }
        catch (Exception e)
        {
            Debug.LogError($"General Error opening port {portName} for Maze: {e.Message}");
            isPortOpen = false; stream = null;
        }
    }

    void CloseSerialPort()
    {
        if (stream != null && stream.IsOpen)
        {
            try
            {
                stream.Close();
                Debug.Log($"Serial port {portName} closed for Maze Platform.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing serial port: {e.Message}");
            }
        }
        isPortOpen = false;
        stream = null; // Ensure stream is nullified after closing
    }

    // Ensure the port is closed when the game stops or the object is destroyed
    void OnDestroy() { CloseSerialPort(); }
    void OnApplicationQuit() { CloseSerialPort(); }
    // --- End Serial Port Management ---
}