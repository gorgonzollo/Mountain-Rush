using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class CarCameraController : MonoBehaviour
{
    [Header("Ввод — мышь")]
    public float touchSmoothness = 5f;
    public bool blockWhenOverUI = true;
    public float mouseSensitivity = 0.5f;
    public int mouseButton = 0;

    [Header("UI — безопасная зона")]
    public float uiSafeMargin = 24f;
    [Range(4, 64)] public int uiProximitySamples = 20;

    [Header("Ограничения углов")]
    public float maxVerticalAngle = 60f;
    public float maxHorizontalAngle = 90f;

    [Header("Сила реакции на ускорения (амплитуда по осям)")]
    public float forwardEffect = 0.10f;
    public float lateralEffect = 0.15f;
    public float verticalEffect = 0.10f;

    [Header("Эффекты вращения")]
    public float pitchEffect = 5f;
    public float rollEffect = 3f;
    public float yawEffect = 2f;

    [Header("Сглаживание углов (0..1)")]
    [Range(0f, 1f)] public float effectSmoothness = 0.2f;

    [Header("Позиция: плавность и лимиты")]
    public float moveSmoothTime = 0.22f;
    public Vector3 maxOffsetLocal = new Vector3(0.12f, 0.10f, 0.0f);

    [Header("Z — асимметричное ограничение")]
    public float maxZForward = 0.12f;
    public float maxZBackward = 0.12f;

    private float currentX = 0f, currentY = 0f, targetX = 0f, targetY = 0f;
    private float pitchAngle = 0f, rollAngle = 0f, yawAngle = 0f;

    private Vector3 initialPosition;
    private Vector3 targetPosition;
    private Vector3 previousVelocity;
    private Vector3 smoothedVelocityDelta;
    private Rigidbody carRigidbody;
    private Transform velocityReference;

    private Vector3 moveVelLocal;

    private bool isMouseDragging = false;
    private Vector2 lastMousePos;

    private static readonly List<RaycastResult> s_RaycastResults = new List<RaycastResult>(16);

    void Start()
    {
        initialPosition = transform.localPosition;
        targetPosition = initialPosition;

        Vector3 angles = transform.localEulerAngles;
        currentX = targetX = NormalizeAngle(angles.x);
        currentY = targetY = NormalizeAngle(angles.y);

        carRigidbody = GetComponentInParent<Rigidbody>();
        if (carRigidbody != null)
        {
            velocityReference = carRigidbody.transform;
        }
    }

    private void OnEnable()
    {
        if (carRigidbody != null)
        {
            var v = carRigidbody.linearVelocity;
            previousVelocity = velocityReference
                ? velocityReference.InverseTransformDirection(v)
                : v;
        }
        else previousVelocity = Vector3.zero;

        moveVelLocal = Vector3.zero;
    }

    void Update()
    {
        HandleMouseInput();

        currentX = Mathf.Lerp(currentX, targetX, Time.deltaTime * touchSmoothness);
        currentY = Mathf.Lerp(currentY, targetY, Time.deltaTime * touchSmoothness);
        transform.localRotation = Quaternion.Euler(currentX + pitchAngle, currentY + yawAngle, rollAngle);

        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            targetPosition,
            ref moveVelLocal,
            Mathf.Max(0.0001f, moveSmoothTime),
            Mathf.Infinity,
            Time.deltaTime
        );

        transform.localPosition = ClampPerAxisLocal(initialPosition, transform.localPosition, maxOffsetLocal, maxZForward, maxZBackward);
    }

    void FixedUpdate()
    {
        if (carRigidbody == null) return;
        if (velocityReference == null) velocityReference = carRigidbody.transform;

        Vector3 localVelocity = velocityReference.InverseTransformDirection(carRigidbody.linearVelocity);
        Vector3 localAngularVelocity = velocityReference.InverseTransformDirection(carRigidbody.angularVelocity);

        Vector3 dv = localVelocity - previousVelocity;
        previousVelocity = localVelocity;

        smoothedVelocityDelta = Vector3.Lerp(smoothedVelocityDelta, dv, effectSmoothness);

        Vector3 a = new Vector3(
            Mathf.Clamp(smoothedVelocityDelta.x, -1f, 1f),
            Mathf.Clamp(smoothedVelocityDelta.y, -1f, 1f),
            Mathf.Clamp(smoothedVelocityDelta.z, -1f, 1f)
        );

        Vector3 positionOffset = new Vector3(
            -a.x * lateralEffect,
            -a.y * verticalEffect,
            -a.z * forwardEffect
        );

        Vector3 desiredLocal = initialPosition + positionOffset;
        targetPosition = ClampPerAxisLocal(desiredLocal: desiredLocal);

        pitchAngle = Mathf.Lerp(pitchAngle, -a.z * pitchEffect, effectSmoothness);
        rollAngle = Mathf.Lerp(rollAngle, -a.x * rollEffect, effectSmoothness);
        yawAngle = Mathf.Lerp(yawAngle, -localAngularVelocity.y * yawEffect, effectSmoothness);

        pitchAngle = Mathf.Clamp(pitchAngle, -15f, 15f);
        rollAngle = Mathf.Clamp(rollAngle, -10f, 10f);
        yawAngle = Mathf.Clamp(yawAngle, -15f, 15f);
    }

    private Vector3 ClampPerAxisLocal(Vector3 desiredLocal)
    {
        return ClampPerAxisLocal(initialPosition, desiredLocal, maxOffsetLocal, maxZForward, maxZBackward);
    }

    static Vector3 ClampPerAxisLocal(Vector3 centerLocal, Vector3 pointLocal, Vector3 maxOffsetXY, float maxZPos, float maxZNeg)
    {
        Vector3 off = pointLocal - centerLocal;
        off.x = Mathf.Clamp(off.x, -Mathf.Abs(maxOffsetXY.x), Mathf.Abs(maxOffsetXY.x));
        off.y = Mathf.Clamp(off.y, -Mathf.Abs(maxOffsetXY.y), Mathf.Abs(maxOffsetXY.y));
        float pos = Mathf.Max(0f, maxZPos);
        float neg = Mathf.Max(0f, maxZNeg);
        off.z = Mathf.Clamp(off.z, -neg, pos);
        return centerLocal + off;
    }

    void HandleMouseInput()
    {
        if (Mouse.current == null) return;

        ButtonControl targetButton;
        switch (mouseButton)
        {
            case 1: targetButton = Mouse.current.rightButton; break;
            case 2: targetButton = Mouse.current.middleButton; break;
            default: targetButton = Mouse.current.leftButton; break;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();

        if (targetButton.wasPressedThisFrame)
        {
            if (!IsMouseOverOrNearUI(mousePos))
            {
                isMouseDragging = true;
                lastMousePos = mousePos;
            }
        }

        if (isMouseDragging && targetButton.isPressed)
        {
            if (blockWhenOverUI && IsMouseOverOrNearUI(mousePos))
            {
                lastMousePos = mousePos;
                return;
            }

            Vector2 delta = mousePos - lastMousePos;
            lastMousePos = mousePos;

            float dx = delta.x * mouseSensitivity * 0.1f;
            float dy = delta.y * mouseSensitivity * 0.1f;

            targetY += dx;
            targetX -= dy;

            targetX = Mathf.Clamp(targetX, -maxVerticalAngle, maxVerticalAngle);
            targetY = Mathf.Clamp(targetY, -maxHorizontalAngle, maxHorizontalAngle);
        }

        if (!targetButton.isPressed)
        {
            isMouseDragging = false;
        }
    }

    bool IsMouseOverOrNearUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;
        
        if (EventSystem.current.IsPointerOverGameObject()) return true;

        return IsScreenPosOverOrNearUI(screenPos);
    }

    bool IsScreenPosOverOrNearUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;

        var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
        s_RaycastResults.Clear();
        EventSystem.current.RaycastAll(eventData, s_RaycastResults);

        if (s_RaycastResults.Count > 0) return true;

        if (uiSafeMargin <= 0f) return false;

        int samples = Mathf.Max(4, uiProximitySamples);
        float radius = uiSafeMargin;

        for (int i = 0; i < samples; i++)
        {
            float angle = (Mathf.PI * 2f) * i / samples;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Vector2 pos = screenPos + offset;

            eventData.position = pos;
            s_RaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, s_RaycastResults);
            if (s_RaycastResults.Count > 0) return true;
        }

        return false;
    }

    static float NormalizeAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
        uiSafeMargin = Mathf.Max(0f, uiSafeMargin);
        uiProximitySamples = Mathf.Clamp(uiProximitySamples, 4, 64);
        maxVerticalAngle = Mathf.Clamp(maxVerticalAngle, 0f, 89.9f);
        maxHorizontalAngle = Mathf.Clamp(maxHorizontalAngle, 0f, 180f);
        moveSmoothTime = Mathf.Max(0.0001f, moveSmoothTime);
        maxOffsetLocal.x = Mathf.Max(0f, maxOffsetLocal.x);
        maxOffsetLocal.y = Mathf.Max(0f, maxOffsetLocal.y);
        maxZForward = Mathf.Max(0f, maxZForward);
        maxZBackward = Mathf.Max(0f, maxZBackward);
        forwardEffect = Mathf.Max(0f, forwardEffect);
        lateralEffect = Mathf.Max(0f, lateralEffect);
        verticalEffect = Mathf.Max(0f, verticalEffect);
    }
#endif
}