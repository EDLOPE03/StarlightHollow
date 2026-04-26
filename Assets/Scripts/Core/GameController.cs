using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Settings")]
    public Transform playerBody;
    public float     mouseSensitivity = 2f;
    public float     verticalClamp    = 60f;

    private float _xRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void Update()
    {
        // Skip camera rotation when paused
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        // Get mouse input
        var mouse = Mouse.current;
        if (mouse == null) return;

        float mouseX = mouse.delta.x.ReadValue() * mouseSensitivity;
        float mouseY = mouse.delta.y.ReadValue() * mouseSensitivity;

        // Vertical look — clamp so camera doesn't flip
        _xRotation -= mouseY;
        _xRotation  = Mathf.Clamp(_xRotation, -verticalClamp, verticalClamp);

        // Apply rotation
        transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);

        // Rotate player body horizontally
        if (playerBody != null)
            playerBody.Rotate(Vector3.up * mouseX);
    }
}
