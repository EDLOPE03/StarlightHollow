using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Settings")]
    public Transform playerBody;
    [SerializeField] private Transform cameraTarget;
    public float     mouseSensitivity = 1f;
    public float     verticalClamp    = 60f;
    [SerializeField] private float lookSmoothing = 20f;
    [SerializeField] private float lookDeadzone = 0.03f;

    private float _xRotation = 0f;
    private float _yRotation = 0f;
    private Vector2 _rawLook;

    void Start()
    {
        // Auto-wire a child named CameraTarget if one was not assigned in the inspector.
        if (cameraTarget == null && playerBody != null)
        {
            var t = playerBody.Find("CameraTarget");
            if (t != null) cameraTarget = t;
        }

        // Keep current rotation so we don't snap on scene load.
        if (cameraTarget != null)
        {
            _xRotation = NormalizeAngle(cameraTarget.localEulerAngles.x);
            _yRotation = NormalizeAngle(cameraTarget.localEulerAngles.y);
        }
        else
        {
            _xRotation = NormalizeAngle(transform.localEulerAngles.x);
            _yRotation = NormalizeAngle(transform.localEulerAngles.y);
        }

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
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 look = _rawLook;
        if (look.sqrMagnitude < lookDeadzone * lookDeadzone)
            look = Vector2.zero;

        float mouseX = look.x;
        float mouseY = look.y;

        float targetXRotation = Mathf.Clamp(_xRotation - mouseY, -verticalClamp, verticalClamp);
        float targetYRotation = _yRotation + mouseX;

        if (lookSmoothing > 0f)
        {
            _xRotation = Mathf.LerpAngle(_xRotation, targetXRotation, Time.deltaTime * lookSmoothing);
            _yRotation = Mathf.LerpAngle(_yRotation, targetYRotation, Time.deltaTime * lookSmoothing);
        }
        else
        {
            _xRotation = targetXRotation;
            _yRotation = targetYRotation;
        }

        // Rotate the camera target only; movement stays on the character controller.
        if (cameraTarget != null)
        {
            cameraTarget.localRotation = Quaternion.Euler(_xRotation, _yRotation, 0f);
        }
        else
        {
            transform.localRotation = Quaternion.Euler(_xRotation, _yRotation, 0f);
        }
    }

    private static float NormalizeAngle(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
