using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Settings")]
    public Transform playerBody;
    [SerializeField] private Transform cameraTarget;
    public float     mouseSensitivity = 2f;
    public float     verticalClamp    = 60f;
    [SerializeField] private float lookSmoothing = 18f;
    [SerializeField] private float lookDeadzone = 0.01f;

    private float _xRotation = 0f;
    private Vector2 _rawLook;
    private Vector2 _smoothedLook;

    void Start()
    {
        // Auto-wire a child named CameraTarget if one was not assigned in the inspector.
        if (cameraTarget == null && playerBody != null)
        {
            var t = playerBody.Find("CameraTarget");
            if (t != null) cameraTarget = t;
        }

        // Keep current pitch so we don't snap on scene load.
        if (cameraTarget != null)
            _xRotation = NormalizeAngle(cameraTarget.localEulerAngles.x);
        else
            _xRotation = NormalizeAngle(transform.localEulerAngles.x);

        _xRotation = Mathf.Clamp(_xRotation, -verticalClamp, verticalClamp);
    }

    void Update()
    {
        // Skip camera rotation when paused
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
        {
            _rawLook = Vector2.zero;
            return;
        }

        // Only process look input while gameplay cursor is locked.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            _rawLook = Vector2.zero;
            return;
        }

        // Get mouse input
        var mouse = Mouse.current;
        if (mouse == null)
        {
            _rawLook = Vector2.zero;
            return;
        }

        _rawLook = mouse.delta.ReadValue() * mouseSensitivity;
    }

    void LateUpdate()
    {
        // Apply camera after movement updates to reduce jitter.
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
        {
            _smoothedLook = Vector2.zero;
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            _smoothedLook = Vector2.zero;
            return;
        }

        float lerpT = 1f - Mathf.Exp(-Mathf.Max(0.01f, lookSmoothing) * Time.unscaledDeltaTime);
        _smoothedLook = Vector2.Lerp(_smoothedLook, _rawLook, lerpT);

        if (_smoothedLook.sqrMagnitude < lookDeadzone * lookDeadzone)
            _smoothedLook = Vector2.zero;

        float mouseX = _smoothedLook.x;
        float mouseY = _smoothedLook.y;

        // Vertical look — clamp so camera doesn't flip
        _xRotation -= mouseY;
        _xRotation  = Mathf.Clamp(_xRotation, -verticalClamp, verticalClamp);

        // If using Cinemachine, rotate the tracking target for pitch and the player body for yaw.
        if (cameraTarget != null)
            cameraTarget.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        else
            transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);

        // Rotate player body horizontally
        if (playerBody != null)
            playerBody.Rotate(Vector3.up * mouseX);
    }

    private static float NormalizeAngle(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
