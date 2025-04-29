using UnityEngine;
using System.IO.Ports;
using System; // Required for TryParse and Math

public class PaddleMovements : MonoBehaviour // Renamed for clarity
{
    [Header("Serial Port Settings")]
    public string portName = "COM10";
    public int baudRate = 115200;

    [Header("Paddle Control Settings")]
    public float minX = -8.0f; // Minimum X position for the paddle (adjust based on your screen)
    public float maxX = 8.0f;  // Maximum X position for the paddle (adjust based on your screen)

    [Tooltip("Which Euler angle axis represents the left/right tilt? (X=Pitch, Y=Yaw, Z=Roll) - Experiment needed!")]
    public Axis axisToUse = Axis.Z; // Defaulting to Roll (Z). Change based on testing.

    [Tooltip("Maximum tilt angle (degrees) in each direction for full paddle movement.")]
    public float maxTiltAngle = 30.0f; // E.g., -30 to +30 degrees maps to minX to maxX

    [Tooltip("Invert the direction of movement if needed.")]
    public bool invertAxis = false;

    [Tooltip("Smoothing factor for paddle movement (0=no smoothing, closer to 1=more smoothing)")]
    [Range(0f, 0.99f)]
    public float smoothing = 0.5f;

    [Header("Debug Info (Read Only)")]
    [SerializeField] // Show private fields in inspector for debugging
    private string receivedString;
    [SerializeField]
    private float rawTiltAngle = 0f;
    [SerializeField]
    private float targetXPosition = 0f;

    private SerialPort stream;
    private float currentSmoothedX; // For storing the smoothed position

    public enum Axis
    {
        X, // Pitch
        Y, // Yaw
        Z  // Roll
    }

    void Start()
    {
        InitializeSerialPort();
        // Initialize smoothed position to the paddle's starting position
        currentSmoothedX = transform.position.x;
        targetXPosition = transform.position.x; // Initialize target position
    }

    void InitializeSerialPort()
    {
        try
        {
            stream = new SerialPort(portName, baudRate);
            stream.ReadTimeout = 50; // Prevent blocking indefinitely if no data arrives
            stream.Open();
            Debug.Log($"Serial port {portName} opened successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error opening serial port {portName}: {ex.Message}");
            stream = null; // Ensure stream is null if opening failed
        }
    }

    void Update()
    {
        if (stream == null || !stream.IsOpen)
        {
            // Optional: Try to reconnect periodically?
            // For now, just do nothing if the port isn't open.
            return;
        }

        try
        {
            receivedString = stream.ReadLine(); // Read the information
            ProcessSerialData(receivedString);
        }
        catch (TimeoutException)
        {
            // Expected if no data is sent from Arduino for 50ms, do nothing.
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error reading from serial port: {ex.Message}");
            // Consider closing and reopening the port on certain errors.
        }

        // --- Position Calculation ---
        // Clamp the raw angle to the defined max tilt range
        float clampedAngle = Mathf.Clamp(rawTiltAngle, -maxTiltAngle, maxTiltAngle);

        // Normalize the angle: map [-maxTiltAngle, +maxTiltAngle] to [0, 1]
        float normalizedAngle = Mathf.InverseLerp(-maxTiltAngle, maxTiltAngle, clampedAngle);

        // Map the normalized angle to the paddle's X position range: [minX, maxX]
        targetXPosition = Mathf.Lerp(minX, maxX, normalizedAngle);

        // --- Apply Smoothing ---
        // Lerp between the current smoothed position and the target position
        currentSmoothedX = Mathf.Lerp(currentSmoothedX, targetXPosition, 1f - smoothing);

        // --- Update Paddle Position ---
        // Get the current position
        Vector3 currentPos = transform.position;
        // Set the new position, only changing the X value
        transform.position = new Vector3(currentSmoothedX, currentPos.y, currentPos.z);
    }

    void ProcessSerialData(string data)
    {
        string[] strData = data.Split(',');

        // Expecting 4 values for the quaternion (w, x, y, z) or (x, y, z, w)
        if (strData.Length >= 4)
        {
            // Try parsing the float values
            // IMPORTANT: Your original code used qw, qx, qy, qz and mapped them oddly.
            // Standard Unity Quaternion is (x, y, z, w).
            // Assuming Arduino sends W, X, Y, Z based on your variable names:
            if (float.TryParse(strData[0], out float qw) &&
                float.TryParse(strData[1], out float qx) &&
                float.TryParse(strData[2], out float qy) &&
                float.TryParse(strData[3], out float qz))
            {
                // Construct the Quaternion.
                // *Critical:* The order (x,y,z,w) and potential negation depends HEAVILY
                // on your Arduino code output and MPU6050 library.
                // Using your original mapping: Quaternion(-qy, -qz, qx, qw)
                // Let's try a more standard Unity mapping assuming Arduino sends W, X, Y, Z: Quaternion(qx, qy, qz, qw)
                // You MUST verify this part based on your Arduino output!
                // Let's stick to your original for now, but add a warning.
                // Quaternion rotation = new Quaternion(qx, qy, qz, qw); // Standard Unity order (if Arduino sends W,X,Y,Z)
                Quaternion rotation = new Quaternion(-qy, -qz, qx, qw); // Your original mapping - verify this!
                // Debug.LogWarning("Using custom Quaternion mapping (-qy, -qz, qx, qw). Verify this matches Arduino output and desired axes.");


                // Convert quaternion to Euler angles (in degrees)
                Vector3 eulerAngles = rotation.eulerAngles;

                // Select the desired angle based on axisToUse
                switch (axisToUse)
                {
                    case Axis.X: // Pitch
                        rawTiltAngle = ConvertAngle(eulerAngles.x);
                        break;
                    case Axis.Y: // Yaw
                        rawTiltAngle = ConvertAngle(eulerAngles.y);
                        break;
                    case Axis.Z: // Roll
                        rawTiltAngle = ConvertAngle(eulerAngles.z);
                        break;
                }

                // Invert axis if needed
                if (invertAxis)
                {
                    rawTiltAngle *= -1f;
                }
            }
            else
            {
                Debug.LogWarning($"Could not parse float values from: {data}");
            }
        }
        else
        {
            Debug.LogWarning($"Received incomplete data: {data}");
        }
    }

    // Helper function to convert angles from 0-360 range to -180 to +180 range
    float ConvertAngle(float angle)
    {
        if (angle > 180f)
        {
            return angle - 360f;
        }
        return angle;
    }

    // Ensure the serial port is closed when the application quits or the object is destroyed
    void OnDestroy()
    {
        CloseSerialPort();
    }

    void OnApplicationQuit()
    {
        CloseSerialPort();
    }

    void CloseSerialPort()
    {
        if (stream != null && stream.IsOpen)
        {
            try
            {
                stream.Close();
                Debug.Log($"Serial port {portName} closed.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error closing serial port {portName}: {ex.Message}");
            }
            stream = null;
        }
    }
}