using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// EDITOR-ONLY TEST TOOL — compiled out of real builds automatically.
///
/// BroomFlightController steers by reading vrCamera.localEulerAngles.z (head roll/tilt),
/// which only a real headset produces. This script fakes that with the keyboard so you
/// can test steering from the laptop: hold Left/Right Arrow to lean, release to level out.
///
/// Setup: drop this on any GameObject in the scene (e.g. the Main Camera itself), then
/// either leave "Camera To Tilt" empty (it will grab Camera.main automatically) or drag
/// your VR camera in manually.
/// </summary>
public class KeyboardTiltSimulator : MonoBehaviour
{
    [Tooltip("Camera whose local Z rotation represents head tilt. Defaults to Camera.main if left empty.")]
    public Transform cameraToTilt;

    [Tooltip("Max simulated lean angle in degrees. BroomFlightController's tiltThreshold is 8 by default, so keep this comfortably above that.")]
    public float maxTiltAngle = 25f;

    [Tooltip("Degrees/second the simulated lean ramps toward the held direction.")]
    public float tiltSpeed = 90f;

    [Tooltip("Degrees/second it springs back to level when no key is held.")]
    public float returnSpeed = 60f;

    [Tooltip("Flip this if turning feels backwards in Play Mode.")]
    public bool invert = false;

    private float currentRoll = 0f;

    void Start()
    {
        if (cameraToTilt == null && Camera.main != null)
        {
            cameraToTilt = Camera.main.transform;
        }

        if (cameraToTilt == null)
        {
            Debug.LogWarning("[KeyboardTiltSimulator] No camera assigned and no Camera.main found. Assign 'Camera To Tilt' manually in the Inspector.");
        }
    }

    void LateUpdate()
    {
        if (cameraToTilt == null) return;

        float input = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) input += 1f;
        if (Input.GetKey(KeyCode.RightArrow)) input -= 1f;
        if (invert) input = -input;

        float target = input * maxTiltAngle;
        float speed = (Mathf.Abs(input) > 0.01f) ? tiltSpeed : returnSpeed;
        currentRoll = Mathf.MoveTowards(currentRoll, target, speed * Time.deltaTime);

        // Preserve whatever pitch (x) / yaw (y) another system set this frame
        // (mouse-look, tracked pose driver, etc.) and only override roll (z).
        Vector3 e = cameraToTilt.localEulerAngles;
        cameraToTilt.localRotation = Quaternion.Euler(e.x, e.y, currentRoll);
    }
}
#endif