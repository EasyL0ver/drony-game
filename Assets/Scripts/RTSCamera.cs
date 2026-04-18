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
    [SerializeField] float zoomSpeed = 3f;
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
    bool    initialized;
    float   initCooldown;   // ignore input briefly after Init

    // Input devices
    Keyboard kb;
    Mouse    mouse;

    /// <summary>
    /// Set camera to look at a world XZ point from a given height and pitch.
    /// Must be called before Start() to prevent Start from overriding.
    /// </summary>
    public void Init(Vector3 lookAt, float zoom, float pitch)
    {
        targetZoom  = zoom;
        targetPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        targetYaw   = 0f;
        currentVelocity = Vector3.zero;
        initCooldown = 0.5f; // ignore input for half a second to prevent edge-scroll drift

        // Position camera directly above lookAt, offset back by pitch
        float zOff = -zoom / Mathf.Tan(targetPitch * Mathf.Deg2Rad);
        targetPosition = new Vector3(lookAt.x, zoom, lookAt.z + zOff);

        transform.position = targetPosition;
        transform.rotation = Quaternion.Euler(targetPitch, targetYaw, 0f);
        initialized = true;
    }

    void Start()
    {
        UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();

        if (!initialized)
        {
            targetPosition = transform.position;
            targetZoom     = transform.position.y;
            targetYaw      = transform.eulerAngles.y;
            targetPitch    = transform.eulerAngles.x;
        }

        if (targetPitch < minPitch) targetPitch = (minPitch + maxPitch) * 0.5f;
    }

    // Touch state
    bool isTouchPanning;
    Vector2 lastTouchPos;

    void Update()
    {
        // Skip input briefly after Init to prevent edge-scroll drift
        if (initCooldown > 0f)
        {
            initCooldown -= Time.unscaledDeltaTime;
            return;
        }

        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            HandleTouchPan();
        }
        else
        {
            isTouchPanning = false;

            kb    = Keyboard.current;
            mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            HandlePan();
            HandleZoom();
            HandleRotation();
        }

        ApplyTransform();
    }

    // ═══════════════════════════════════════
    //  TOUCH PAN (single finger)
    // ═══════════════════════════════════════

    void HandleTouchPan()
    {
        var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        if (touches.Count != 1) { isTouchPanning = false; return; }

        // Ignore if touch is over UI (drone cards, etc.)
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touches[0].touchId))
        {
            isTouchPanning = false;
            return;
        }

        var touch = touches[0];

        if (!isTouchPanning)
        {
            isTouchPanning = true;
            lastTouchPos = touch.screenPosition;
            return;
        }

        Vector2 delta = touch.screenPosition - lastTouchPos;
        lastTouchPos = touch.screenPosition;

        // Convert screen-space delta to world-space pan
        float heightFactor = Mathf.Lerp(0.5f, 2f,
            Mathf.InverseLerp(minHeight, maxHeight, targetZoom));
        float speed = panSpeed * heightFactor * 0.005f;

        Vector3 fwd   = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        // Invert so dragging moves the world under your finger
        targetPosition -= (right * delta.x + fwd * delta.y) * speed;

        if (boundsX > 0f)
            targetPosition.x = Mathf.Clamp(targetPosition.x, -boundsX, boundsX);
        if (boundsZ > 0f)
            targetPosition.z = Mathf.Clamp(targetPosition.z, -boundsZ, boundsZ);
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

        // Smooth XZ position (SmoothDamp for pan momentum)
        Vector3 pos = transform.position;
        Vector3 goalXZ = new Vector3(targetPosition.x, pos.y, targetPosition.z);
        Vector3 smoothedXZ = Vector3.SmoothDamp(pos, goalXZ, ref currentVelocity,
            1f / moveDamping, Mathf.Infinity, dt);

        // Smooth Y zoom (Lerp only — no momentum buildup)
        float smoothedY = Mathf.Lerp(pos.y, targetZoom, dt * zoomSmoothing);

        transform.position = new Vector3(smoothedXZ.x, smoothedY, smoothedXZ.z);

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
