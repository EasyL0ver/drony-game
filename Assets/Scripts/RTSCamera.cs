using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Full-featured RTS camera using the new Input System:
/// WASD/arrow pan, mouse edge scroll, scroll-wheel zoom,
/// middle-mouse orbit, Q/E rotate, smooth damping.
/// Attach to the Main Camera.
/// </summary>
public class RTSCamera : MonoBehaviour
{
    [Header("Pan")]
    [SerializeField] float panSpeed = 25f;
    [SerializeField] [Range(0f, 50f)] float edgeScrollMargin = 12f;
    [SerializeField] bool enableEdgeScroll = true;

    [Header("Zoom")]
    [SerializeField] float zoomSpeed = 12f;
    [SerializeField] float minHeight = 5f;
    [SerializeField] float maxHeight = 80f;
    [SerializeField] float zoomSmoothing = 8f;

    [Header("Rotation")]
    [SerializeField] float rotateSpeed = 120f;
    [SerializeField] bool invertRotation = false;

    [Header("Pitch (vertical angle)")]
    [SerializeField] float minPitch = 30f;
    [SerializeField] float maxPitch = 85f;
    [SerializeField] float pitchSpeed = 60f;

    [Header("Damping")]
    [SerializeField] float moveDamping = 10f;

    [Header("Bounds (0 = unlimited)")]
    [SerializeField] float boundsX = 0f;
    [SerializeField] float boundsZ = 0f;

    // State
    Vector3 targetPosition;
    float   targetZoom;
    float   targetYaw;
    float   targetPitch;
    Vector3 currentVelocity;

    // Input devices
    Keyboard kb;
    Mouse    mouse;

    void Start()
    {
        targetPosition = transform.position;
        targetZoom     = transform.position.y;
        targetYaw      = transform.eulerAngles.y;
        targetPitch    = transform.eulerAngles.x;

        if (targetPitch < minPitch) targetPitch = (minPitch + maxPitch) * 0.5f;
    }

    void Update()
    {
        kb    = Keyboard.current;
        mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        HandlePan();
        HandleZoom();
        HandleRotation();
        ApplyTransform();
    }

    // ═══════════════════════════════════════
    //  PAN
    // ═══════════════════════════════════════

    void HandlePan()
    {
        Vector2 input = Vector2.zero;

        // Keyboard
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  input.x -= 1f;

        // Edge scroll
        if (enableEdgeScroll && Application.isFocused)
        {
            Vector2 mp = mouse.position.ReadValue();
            if (mp.x <= edgeScrollMargin)                input.x -= 1f;
            if (mp.x >= Screen.width - edgeScrollMargin) input.x += 1f;
            if (mp.y <= edgeScrollMargin)                input.y -= 1f;
            if (mp.y >= Screen.height - edgeScrollMargin) input.y += 1f;
        }

        if (input.sqrMagnitude > 1f) input.Normalize();

        // Speed scales with zoom height so far-out panning feels natural
        float heightFactor = Mathf.Lerp(0.5f, 2f,
            Mathf.InverseLerp(minHeight, maxHeight, targetZoom));
        float speed = panSpeed * heightFactor;

        // Camera-relative directions (projected onto XZ plane)
        Vector3 fwd   = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        Vector3 move = (right * input.x + fwd * input.y) * speed;
        targetPosition += move * Time.unscaledDeltaTime;

        // Clamp to bounds
        if (boundsX > 0f)
            targetPosition.x = Mathf.Clamp(targetPosition.x, -boundsX, boundsX);
        if (boundsZ > 0f)
            targetPosition.z = Mathf.Clamp(targetPosition.z, -boundsZ, boundsZ);
    }

    // ═══════════════════════════════════════
    //  ZOOM
    // ═══════════════════════════════════════

    void HandleZoom()
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // scroll comes in larger increments with Input System, normalize
            targetZoom -= Mathf.Sign(scroll) * zoomSpeed;
            targetZoom  = Mathf.Clamp(targetZoom, minHeight, maxHeight);
        }
    }

    // ═══════════════════════════════════════
    //  ROTATION / PITCH
    // ═══════════════════════════════════════

    void HandleRotation()
    {
        if (mouse.middleButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            float dir = invertRotation ? -1f : 1f;
            targetYaw   += delta.x * rotateSpeed * dir * Time.unscaledDeltaTime;
            targetPitch -= delta.y * pitchSpeed  * dir * Time.unscaledDeltaTime;
            targetPitch  = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }

        // Q / E for keyboard rotation
        if (kb.qKey.isPressed) targetYaw -= rotateSpeed * Time.unscaledDeltaTime;
        if (kb.eKey.isPressed) targetYaw += rotateSpeed * Time.unscaledDeltaTime;
    }

    // ═══════════════════════════════════════
    //  APPLY
    // ═══════════════════════════════════════

    void ApplyTransform()
    {
        float dt = Time.unscaledDeltaTime;

        // Smooth position
        Vector3 pos = transform.position;
        float smoothedY = Mathf.Lerp(pos.y, targetZoom, dt * zoomSmoothing);
        Vector3 goal = new Vector3(targetPosition.x, smoothedY, targetPosition.z);
        transform.position = Vector3.SmoothDamp(pos, goal, ref currentVelocity,
            1f / moveDamping, Mathf.Infinity, dt);

        // Smooth rotation
        float yaw   = Mathf.LerpAngle(transform.eulerAngles.y, targetYaw,   dt * moveDamping);
        float pitch = Mathf.LerpAngle(transform.eulerAngles.x, targetPitch, dt * moveDamping);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    // ═══════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════

    /// <summary>Instantly focus on a world position.</summary>
    public void FocusOn(Vector3 worldPos)
    {
        targetPosition = new Vector3(worldPos.x, targetPosition.y, worldPos.z);
    }

    /// <summary>Set zoom height directly.</summary>
    public void SetZoom(float height)
    {
        targetZoom = Mathf.Clamp(height, minHeight, maxHeight);
    }
}
