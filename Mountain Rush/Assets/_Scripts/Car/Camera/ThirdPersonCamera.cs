using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class AAAOrbitCarCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public float pivotHeight = 1.5f;

    [Header("Orbit")]
    public float distance = 8f;
    public float minDistance = 4f;
    public float maxDistance = 12f;

    [Header("Zoom")]
    public float wheelZoomSpeed = 1.0f;

    [Header("Angles")]
    public float yaw = 0f;
    public float pitch = 20f;
    public float minPitch = 5f;
    public float maxPitch = 60f;

    [Header("Smooth")]
    public float yawSmooth = 10f;
    public float pitchSmooth = 10f;

    [Header("Collision")]
    public LayerMask obstacleMask;
    public float collisionRadius = 0.4f;
    public float collisionBuffer = 0.2f;
    public float collisionSmooth = 0.08f;

    [Header("Input Settings")]
    public float mouseSensitivity = 0.15f;    // НОВАЯ нормальная чувствительность
    public float rotationLerp = 12f;          // сглаживание дельты

    [Header("Auto Follow")]
    public bool autoYawFollow = true;
    public float autoYawDelay = 0.6f;
    public float autoYawSmooth = 4f;
    public bool followOnlyWhenMoving = true;
    public float followMinSpeed = 1.0f;
    public float followYawOffset = 0f;

    [Header("Composition")]
    public bool enablePitchDolly = true;
    [Range(0f, 0.5f)] public float pitchDollyPercent = 0.12f;

    private float currentYaw, currentPitch;
    private float currentDistance;
    private float collisionVel;
    private Vector3 pivotPos;

    private Vector2 smoothedDelta;
    private bool dragging;
    private float lastInputTime;
    private Rigidbody targetRb;

    void Start()
    {
        if (target) target.TryGetComponent(out targetRb);

        pivotPos = target.position + Vector3.up * pivotHeight;
        currentYaw = yaw;
        currentPitch = pitch;
        currentDistance = distance;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (!target) return;

        HandleInput();
        AutoAlignYaw(Time.deltaTime);

        pivotPos = target.position + Vector3.up * pivotHeight;

        currentYaw = Mathf.LerpAngle(currentYaw, yaw, 1 - Mathf.Exp(-yawSmooth * Time.deltaTime));
        currentPitch = Mathf.Lerp(currentPitch, pitch, 1 - Mathf.Exp(-pitchSmooth * Time.deltaTime));

        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 backDir = (rot * Vector3.back).normalized;

        float desiredDistance = Mathf.Clamp(distance, minDistance, maxDistance);

        if (enablePitchDolly && pitchDollyPercent > 0f)
        {
            float tDown = Mathf.InverseLerp(maxPitch, minPitch, currentPitch);
            tDown = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tDown));
            float dolly = desiredDistance * pitchDollyPercent * tDown;
            desiredDistance = Mathf.Clamp(desiredDistance - dolly, minDistance, maxDistance);
        }

        float safeDistance = desiredDistance;

        if (Physics.SphereCast(
                pivotPos, collisionRadius, backDir,
                out RaycastHit hitPre,
                desiredDistance + 1f,
                obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            safeDistance = Mathf.Max(minDistance, hitPre.distance - collisionBuffer);
        }

        currentDistance = Mathf.SmoothDamp(currentDistance, safeDistance, ref collisionVel, collisionSmooth);

        float appliedDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        Vector3 finalPos = pivotPos + backDir * appliedDistance;

        if (Physics.SphereCast(
                pivotPos, collisionRadius, backDir,
                out RaycastHit hitHard,
                appliedDistance,
                obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            float d = Mathf.Max(minDistance, hitHard.distance - collisionBuffer);
            finalPos = pivotPos + backDir * d;
            currentDistance = d;
            collisionVel = 0f;
        }

        transform.position = finalPos;
        transform.rotation = Quaternion.LookRotation(pivotPos - transform.position, Vector3.up);
    }

    //---------------------------------------------------------
    //                FIXED INPUT — SMOOTH, STABLE
    //---------------------------------------------------------

    void HandleInput()
    {
        if (Mouse.current == null) return;

        // raw delta
        Vector2 raw = Mouse.current.delta.ReadValue();

        // нормализуем под FPS
        raw *= Time.deltaTime * 60f;

        // сглаживаем дельту
        smoothedDelta = Vector2.Lerp(smoothedDelta, raw, rotationLerp * Time.deltaTime);

        if (smoothedDelta.sqrMagnitude > 0.000001f)
        {
            dragging = true;
            yaw += smoothedDelta.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - smoothedDelta.y * mouseSensitivity, minPitch, maxPitch);
            lastInputTime = Time.time;
        }
        else dragging = false;

        // ==== zoom (устойчивый) ====
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance = Mathf.Clamp(
                distance - scroll * 0.02f * wheelZoomSpeed,
                minDistance, maxDistance
            );
        }
    }

    void AutoAlignYaw(float dt)
    {
        if (!autoYawFollow) return;
        if (dragging) return;
        if (Time.time - lastInputTime < autoYawDelay) return;

        float speed = targetRb ? targetRb.linearVelocity.magnitude : 999f;
        if (followOnlyWhenMoving && speed < followMinSpeed) return;

        float targetYaw = target.eulerAngles.y + followYawOffset;
        float w = 1f - Mathf.Exp(-autoYawSmooth * dt);
        yaw = Mathf.LerpAngle(yaw, targetYaw, w);
    }
}
