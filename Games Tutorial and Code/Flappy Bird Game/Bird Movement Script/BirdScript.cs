using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using UnityEngine;

public class BirdScript : MonoBehaviour // Renamed for clarity
{
    public LogicScript logic;
    private bool birdIsAlive = true;
    [Header("Serial Port Settings")]
    public string portName = "COM10"; // Make port configurable in Inspector
    public int baudRate = 115200;     // Make baud rate configurable

    [Header("Gyro Input Mapping")]
    // --- IMPORTANT: CALIBRATE THESE VALUES! ---
    // Observe the 'Current Gyro Input (qy)' value in the Inspector
    // while tilting the sensor to its desired minimum and maximum positions.
    public float inputMinQy = -0.5f; // Gyro value corresponding to the bird's minimum height
    public float inputMaxQy = 0.5f;  // Gyro value corresponding to the bird's maximum height
    // You might need to swap min/max or use qx/qz if the movement is inverted or wrong axis

    [Header("Bird Movement")]
    public float birdMinY = -3f;      // Minimum Y position for the bird in game units
    public float birdMaxY = 5f;       // Maximum Y position for the bird in game units
    public float smoothTime = 0.1f;   // How quickly the bird follows the target position (lower = faster)

    [Header("Debug Info")]
    [SerializeField] // Show private field in Inspector for debugging
    private float currentGyroInput_qy = 0f; // Store the relevant gyro value
    [SerializeField]
    private float targetYPosition = 0f; // Calculated target Y position

    private SerialPort stream;
    private string strReceived;
    private string[] strData = new string[4]; // Temporary array for splitting

    private float currentYVelocity = 0.0f; // Used by SmoothDamp
    private Vector3 currentPosition;       // Store current position efficiently

    void Start()
    {
        logic = GameObject.FindGameObjectWithTag("Logic").GetComponent<LogicScript>();
        currentPosition = transform.position; // Initialize with starting position
        targetYPosition = currentPosition.y;  // Start at the initial Y position

        // --- Ensure No Falling ---
        // If your bird has a Rigidbody2D, disable gravity or set it to Kinematic
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // Option 1: Disable gravity (if you might use physics for other things later)
            rb.gravityScale = 0;

            // Option 2: Set to Kinematic (better if you ONLY control position via script)
            // rb.bodyType = RigidbodyType2D.Kinematic;

            // Make sure velocity is initially zeroed if not kinematic
            if (rb.bodyType != RigidbodyType2D.Kinematic)
            {
                rb.linearVelocity = Vector2.zero;
            }
            Debug.Log("Rigidbody2D found. Gravity Scale set to 0.");
        }
        else
        {
            Debug.LogWarning("No Rigidbody2D found on the bird. Position will be controlled directly, but ensure no other physics components are affecting it.");
        }
        // --- End Ensure No Falling ---


        try
        {
            stream = new SerialPort(portName, baudRate);
            stream.ReadTimeout = 50; // Prevent blocking indefinitely if no data arrives
            stream.Open();
            Debug.Log("Serial port opened successfully: " + portName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error opening serial port {portName}: {e.Message}");
            stream = null; // Ensure stream is null if opening failed
        }
    }

    void Update()
    {
        if (birdIsAlive == false) {
            Destroy(gameObject);
            return;
        }
            // Only proceed if the stream is open
            if (stream == null || !stream.IsOpen)
        {
            return;
        }

        try
        {
            strReceived = stream.ReadLine(); // Read data from Arduino
            strData = strReceived.Split(','); // Split the comma-separated values

            // Check if we received enough data points
            if (strData.Length >= 4 && strData[0] != "" && strData[1] != "" && strData[2] != "" && strData[3] != "")
            {
                // Try to parse the qy value (index 2 based on your original code)
                if (float.TryParse(strData[2], out currentGyroInput_qy)) // <<< CHANGE strData[INDEX] if needed (1=qx, 2=qy, 3=qz)
                {
                    // --- Core Control Logic ---

                    // 1. Clamp the input value to the calibrated range
                    float clampedInput = Mathf.Clamp(currentGyroInput_qy, inputMinQy, inputMaxQy);

                    // 2. Normalize the clamped input value to a 0-1 range
                    float normalizedInput = Mathf.InverseLerp(inputMinQy, inputMaxQy, clampedInput);

                    // 3. Map the normalized (0-1) value to the desired bird Y-position range
                    targetYPosition = Mathf.Lerp(birdMinY, birdMaxY, normalizedInput);

                    // 4. Smoothly move the bird towards the target Y position
                    currentPosition = transform.position;
                    float newY = Mathf.SmoothDamp(currentPosition.y, targetYPosition, ref currentYVelocity, smoothTime);

                    // 5. Apply the new position, ONLY changing the Y axis
                    // This directly sets the position, overriding any physics like gravity
                    transform.position = new Vector3(currentPosition.x, newY, currentPosition.z);
                }
                else
                {
                    Debug.LogWarning("Failed to parse gyro value (qy) from serial data: " + strData[2]);
                }
            }
        }
        catch (System.TimeoutException) { /* Ignore timeout - expected */ }
        catch (System.Exception e)
        {
            Debug.LogError($"Error reading/processing serial data: {e.Message}\nData: {strReceived ?? "NULL"}");
        }
    }

    void OnDestroy() { ClosePort(); }
    void OnApplicationQuit() { ClosePort(); }

    void ClosePort()
    {
        if (stream != null && stream.IsOpen)
        {
            stream.Close();
            Debug.Log("Serial port closed.");
        }
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        logic.gameOver();
        birdIsAlive = false;
    }
}