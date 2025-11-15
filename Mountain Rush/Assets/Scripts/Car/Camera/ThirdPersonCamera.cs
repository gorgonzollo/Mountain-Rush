using UnityEngine;
using UnityEngine.EventSystems;

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
    public float wheelZoomSpeed = 0.5f;

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

    [Header("Input")]
    [SerializeField] private float degreesPerScreen = 120f;
    [SerializeField] private float baseSensitivity = 0.06f;

    [Header("Auto Follow")]
    public bool autoYawFollow = true;
    public float autoYawDelay = 0.6f;
    public float autoYawSmooth = 4f;
    public bool followOnlyWhenMoving = true;
    public float followMinSpeed = 1.0f;
    public float followYawOffset = 0f;

    [Header("Composition")]
    [SerializeField] private bool enablePitchDolly = true;
    [SerializeField][Range(0f, 0.5f)] private float pitchDollyPercent = 0.12f;

    private float currentYaw, currentPitch;
    private float currentDistance;
    private float collisionVel;
    private Vector3 pivotPos;

    private Vector2 prevMousePos;
    private bool mouseDragging;
    private float lastInputTime = -999f;
    private Rigidbody targetRb;

    void Start()
    {
        if (target) pivotPos = target.position + Vector3.up * pivotHeight;
        currentYaw = yaw;
        currentPitch = pitch;
        currentDistance = distance;
        if (target) target.TryGetComponent(out targetRb);
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
            float dolly = desiredDistance * Mathf.Clamp01(pitchDollyPercent) * tDown;
            desiredDistance = Mathf.Clamp(desiredDistance - dolly, minDistance, maxDistance);
        }

        float safeDistance = desiredDistance;
        float castDistance = desiredDistance + 1f;
        if (Physics.SphereCast(
                pivotPos,
                collisionRadius,
                backDir,
                out RaycastHit hitPre,
                castDistance,
                obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            safeDistance = Mathf.Max(minDistance, hitPre.distance - collisionBuffer);
        }

        currentDistance = Mathf.SmoothDamp(currentDistance, safeDistance, ref collisionVel, collisionSmooth);

        float appliedDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        Vector3 finalPos = pivotPos + backDir * appliedDistance;

        if (Physics.SphereCast(
                pivotPos,
                collisionRadius,
                backDir,
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
        transform.rotation = Quaternion.LookRotation((pivotPos - transform.position).normalized, Vector3.up);
    }

    void HandleInput()
    {
        HandleMouseRotation();

        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.0001f)
        {
            distance = Mathf.Clamp(
                distance - Input.mouseScrollDelta.y * wheelZoomSpeed,
                minDistance, maxDistance
            );
        }
    }

    void ApplyRotationFromPixels(Vector2 pixelDelta)
    {
        float shortSide = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
        float effSens = baseSensitivity;
        float k = (degreesPerScreen * effSens) / shortSide;

        yaw += pixelDelta.x * k;
        pitch = Mathf.Clamp(pitch - pixelDelta.y * k, minPitch, maxPitch);

        lastInputTime = Time.time;
    }

    void HandleMouseRotation()
    {
        bool overUI = IsPointerOverUI();

        if (Input.GetMouseButtonDown(0) && !overUI)
        {
            mouseDragging = true;
            prevMousePos = Input.mousePosition;
            lastInputTime = Time.time;
        }
        if (Input.GetMouseButtonUp(0))
        {
            mouseDragging = false;
        }
        if (mouseDragging && !overUI)
        {
            Vector2 curr = (Vector2)Input.mousePosition;
            Vector2 deltaPixels = curr - prevMousePos;
            prevMousePos = curr;

            if (deltaPixels.sqrMagnitude > 0.001f)
            {
                ApplyRotationFromPixels(deltaPixels);
            }
        }
    }

    void AutoAlignYaw(float dt)
    {
        if (!autoYawFollow) return;
        if (mouseDragging) return;
        if (Time.time - lastInputTime < autoYawDelay) return;

        float speed = targetRb ? targetRb.linearVelocity.magnitude : 999f;
        if (followOnlyWhenMoving && speed < followMinSpeed) return;

        float targetYaw = target.eulerAngles.y + followYawOffset;
        float w = 1f - Mathf.Exp(-Mathf.Max(0f, autoYawSmooth) * dt);
        yaw = Mathf.LerpAngle(yaw, targetYaw, w);
    }

    bool IsPointerOverUI()
    {
        if (!EventSystem.current) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
}