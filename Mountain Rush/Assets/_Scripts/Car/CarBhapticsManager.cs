using UnityEngine;
using Bhaptics.SDK2;

public class CarBhapticsManager : MonoBehaviour
{
    public MyCarController car;

    [Header("Event Ids")]
    public string accelId = "accel";
    public string brakeId = "brake";
    public string turnLeftId = "turn_left";
    public string turnRightId = "turn_right";
    public string driftId = "drift";
    public string shiftUpId = "shift_up";
    public string shiftDownId = "shift_down";

    [Header("Thresholds")]
    public float accelG = 0.35f;
    public float brakeG = 0.35f;
    public float turnG = 0.35f;

    [Header("Timings")]
    public int pulseMs = 180;
    public int minIntervalMs = 220;

    float _nextAccel, _nextBrake, _nextTurn, _nextDrift;
    bool _driftActive;

    void OnEnable()
    {
        if (!car) car = FindObjectOfType<MyCarController>();
        if (!car) return;
        car.OnGearShiftUp += OnShiftUp;
        car.OnGearShiftDown += OnShiftDown;
        car.OnTractionLoss += OnTraction;
    }

    void OnDisable()
    {
        if (!car) return;
        car.OnGearShiftUp -= OnShiftUp;
        car.OnGearShiftDown -= OnShiftDown;
        car.OnTractionLoss -= OnTraction;
    }

    void Update()
    {
        if (!BhapticsSDK2.IsInitialized || !car) return;

        float now = Time.time;
        float gx = car.LocalGForce.x;
        float gz = car.LocalGForce.z;

        if (gz > accelG && now >= _nextAccel)
        {
            float k = Mathf.InverseLerp(accelG, accelG + 0.8f, gz);
            Play(accelId, Mathf.Clamp01(k), pulseMs, 0f, 0f);
            _nextAccel = now + minIntervalMs * 0.001f;
        }

        if (gz < -brakeG && now >= _nextBrake)
        {
            float k = Mathf.InverseLerp(brakeG, brakeG + 0.8f, -gz);
            Play(brakeId, Mathf.Clamp01(k), pulseMs, 0f, 0f);
            _nextBrake = now + minIntervalMs * 0.001f;
        }

        if (Mathf.Abs(gx) > turnG && now >= _nextTurn)
        {
            float k = Mathf.InverseLerp(turnG, turnG + 0.8f, Mathf.Abs(gx));
            float angle = Mathf.Sign(gx) < 0 ? -45f : 45f;
            Play(gx < 0 ? turnLeftId : turnRightId, Mathf.Clamp01(k), pulseMs, angle, 0f);
            _nextTurn = now + minIntervalMs * 0.001f;
        }

        if (_driftActive && now >= _nextDrift)
        {
            float k = Mathf.Clamp01(Mathf.Abs(car.LocalGForce.x));
            Play(driftId, Mathf.Lerp(0.5f, 1f, k), pulseMs, 0f, 0f);
            _nextDrift = now + minIntervalMs * 0.001f;
        }
    }

    void OnShiftUp() { if (BhapticsSDK2.IsInitialized) Play(shiftUpId, 1f, pulseMs, 0f, 0f); }
    void OnShiftDown() { if (BhapticsSDK2.IsInitialized) Play(shiftDownId, 1f, pulseMs, 0f, 0f); }
    void OnTraction(bool drift) { _driftActive = drift; if (drift) _nextDrift = 0f; }

    void Play(string id, float intensity01, int durMs, float angleX, float offsetY)
    {
        BhapticsLibrary.Play(id, 0, Mathf.Clamp01(intensity01), Mathf.Max(0.01f, durMs * 0.001f), angleX, offsetY);
    }
}