using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.InputSystem;
using LogitechG29.Sample.Input;

[RequireComponent(typeof(Rigidbody))]
public class MyCarController : MonoBehaviour
{
    [Header("Logitech Setup")]
    [SerializeField] private InputControllerReader _inputReader;

    [Header("Debug Input")]
    [SerializeField] private float _wheelSteer;
    [SerializeField] private float _wheelGas;
    [SerializeField] private float _wheelBrake;

    private Rigidbody rb;

    public float CurrentSpeedKmh { get; private set; }
    public float CurrentRPM { get; private set; }
    public Vector3 LocalGForce { get; private set; }

    public event Action OnGearShiftUp;
    public event Action OnGearShiftDown;
    public event Action<float> OnCollisionForce;
    public event Action OnRevLimiterHit;
    public event Action<bool> OnTractionLoss;

    [Header("Wheels & Visuals")]
    public WheelCollider[] wheels;
    public Transform[] wheelVisuals;
    public Transform steeringWheel;
    public Text gearText;

    [Header("Speedometer")]
    public Transform speedometerNeedle;
    public float speedometerMinAngle = -120f;
    public float speedometerMaxAngle = 120f;
    public float speedometerMaxKmh = 240f;
    public bool invertSpeedometer = false;

    [Header("Audio Settings")]
    public AudioSource engineSource;
    public AudioClip engineClip;
    public float minPitch = 0.7f;
    public float maxPitch = 2.4f;
    [Range(0, 1)] public float spatialBlend = 0.2f;

    [Header("Engine Tuning")]
    public float rpmResponse = 7f;
    public float rpmBoostOnThrottle = 600f;
    [Range(0.3f, 0.9f)] public float rpmCoastFactor = 0.6f;
    public float engineBrakingTorque = 300f;

    [Header("Car Physics")]
    public float maxMotorTorque = 3000f;
    public float maxSteerAngle = 30f;
    public float maxBrakeTorque = 4000f;
    public float[] gearSpeeds = { 40, 80, 120, 160, 200, 240 };
    public float idleRPM = 900f;
    public float redlineRPM = 6000f;

    private int currentGear = 1;
    private Vector3 _lastVelocity;
    private bool _queuedUp, _queuedDown;
    private float _finalSteer, _finalGas, _finalBrake;
    private Quaternion _initialNeedleRot;

    private Action<bool> _cbRightShift, _cbLeftShift, _cbNorth;
    private Action<bool> _cbSh1, _cbSh2, _cbSh3, _cbSh4, _cbSh5, _cbSh6, _cbSh7;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0);
        CurrentRPM = idleRPM;
        if (speedometerNeedle) _initialNeedleRot = speedometerNeedle.localRotation;
    }

    void OnEnable()
    {
        if (_inputReader == null) return;

        _inputReader.SteeringCallback += HandleSteer;
        _inputReader.ThrottleCallback += HandleGas;
        _inputReader.BrakeCallback += HandleBrake;

        _cbRightShift = p => { if (p) _queuedUp = true; };
        _cbLeftShift = p => { if (p) _queuedDown = true; };
        _cbNorth = p => { if (p) ResetCar(); };

        _inputReader.OnRightShiftCallback += _cbRightShift;
        _inputReader.OnLeftShiftCallback += _cbLeftShift;
        _inputReader.OnNorthButtonCallback += _cbNorth;

        _cbSh1 = v => HandleHShifter(1, v);
        _cbSh2 = v => HandleHShifter(2, v);
        _cbSh3 = v => HandleHShifter(3, v);
        _cbSh4 = v => HandleHShifter(4, v);
        _cbSh5 = v => HandleHShifter(5, v);
        _cbSh6 = v => HandleHShifter(6, v);
        _cbSh7 = v => HandleHShifter(-1, v);

        _inputReader.Shifter1Callback += _cbSh1;
        _inputReader.Shifter2Callback += _cbSh2;
        _inputReader.Shifter3Callback += _cbSh3;
        _inputReader.Shifter4Callback += _cbSh4;
        _inputReader.Shifter5Callback += _cbSh5;
        _inputReader.Shifter6Callback += _cbSh6;
        _inputReader.Shifter7Callback += _cbSh7;
    }

    void OnDisable()
    {
        if (_inputReader == null) return;

        _inputReader.SteeringCallback -= HandleSteer;
        _inputReader.ThrottleCallback -= HandleGas;
        _inputReader.BrakeCallback -= HandleBrake;

        if (_cbRightShift != null) _inputReader.OnRightShiftCallback -= _cbRightShift;
        if (_cbLeftShift != null) _inputReader.OnLeftShiftCallback -= _cbLeftShift;
        if (_cbNorth != null) _inputReader.OnNorthButtonCallback -= _cbNorth;

        if (_cbSh1 != null) _inputReader.Shifter1Callback -= _cbSh1;
        if (_cbSh2 != null) _inputReader.Shifter2Callback -= _cbSh2;
        if (_cbSh3 != null) _inputReader.Shifter3Callback -= _cbSh3;
        if (_cbSh4 != null) _inputReader.Shifter4Callback -= _cbSh4;
        if (_cbSh5 != null) _inputReader.Shifter5Callback -= _cbSh5;
        if (_cbSh6 != null) _inputReader.Shifter6Callback -= _cbSh6;
        if (_cbSh7 != null) _inputReader.Shifter7Callback -= _cbSh7;
    }

    void Start()
    {
        if (!engineSource) return;
        if (engineClip) engineSource.clip = engineClip;
        engineSource.loop = true;
        engineSource.spatialBlend = spatialBlend;
        if (!engineSource.isPlaying) engineSource.Play();
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame) _queuedUp = true;
            if (Keyboard.current.fKey.wasPressedThisFrame) _queuedDown = true;
            if (Keyboard.current.rKey.wasPressedThisFrame) ResetCar();
        }

        UpdateVisuals();
        UpdateSpeedometer();
        UpdateAudio();
    }

    void FixedUpdate()
    {
        _finalSteer = _wheelSteer;
        _finalGas = _wheelGas;
        _finalBrake = _wheelBrake;

        if (Mathf.Abs(_finalSteer) < 0.01f && _finalGas < 0.01f && _finalBrake < 0.01f && Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) _finalGas = 1f;
            if (Keyboard.current.sKey.isPressed) _finalBrake = 1f;
            if (Keyboard.current.aKey.isPressed) _finalSteer = -1f;
            else if (Keyboard.current.dKey.isPressed) _finalSteer = 1f;
        }

        _finalGas = Mathf.Clamp01(_finalGas);
        _finalBrake = Mathf.Clamp01(_finalBrake);
        _finalSteer = Mathf.Clamp(_finalSteer, -1f, 1f);

        CurrentSpeedKmh = rb.linearVelocity.magnitude * 3.6f;

        HandleSequentialGears();
        HandleEngine();
        HandleSteering();
        CalculateTelemetry();
        CheckTraction();
    }

    void HandleSteer(float v) { _wheelSteer = v; }
    void HandleGas(float v) { _wheelGas = v; }
    void HandleBrake(float v) { _wheelBrake = v; }

    void HandleHShifter(int gear, bool active)
    {
        if (active)
        {
            currentGear = gear;
            if (gear > 0) OnGearShiftUp?.Invoke();
            if (gear < 0) OnGearShiftDown?.Invoke();
        }
        else
        {
            if (currentGear == gear) currentGear = 0;
        }
    }

    void HandleSequentialGears()
    {
        if (_queuedUp)
        {
            if (currentGear < gearSpeeds.Length) { currentGear++; OnGearShiftUp?.Invoke(); }
            _queuedUp = false;
        }

        if (_queuedDown)
        {
            bool canShift = true;
            if (currentGear == 0 && CurrentSpeedKmh > 5f) canShift = false;
            if (currentGear > 1)
            {
                float maxSpeedForTarget = gearSpeeds[currentGear - 2];
                if (CurrentSpeedKmh > maxSpeedForTarget * 1.2f) { canShift = false; OnRevLimiterHit?.Invoke(); }
            }
            if (canShift && currentGear > -1) { currentGear--; OnGearShiftDown?.Invoke(); }
            _queuedDown = false;
        }
    }

    void HandleEngine()
    {
        float torque = 0f;
        float currentMaxSpeed = 0f;

        if (currentGear > 0)
        {
            int index = Mathf.Clamp(currentGear - 1, 0, gearSpeeds.Length - 1);
            currentMaxSpeed = gearSpeeds[index];
        }
        else if (currentGear == -1)
        {
            currentMaxSpeed = 35f;
        }

        if (currentGear == 0)
        {
            torque = 0f;
        }
        else
        {
            if (CurrentSpeedKmh < currentMaxSpeed)
            {
                float direction = currentGear > 0 ? 1f : -1f;
                torque = maxMotorTorque * _finalGas * direction;
            }
            else
            {
                OnRevLimiterHit?.Invoke();
            }
        }

        float brakeTorque = _finalBrake * maxBrakeTorque;

        if (currentGear != 0 && _finalGas < 0.05f)
        {
            float ratio = currentMaxSpeed > 0.01f ? Mathf.Clamp01(CurrentSpeedKmh / currentMaxSpeed) : 0f;
            float engineBrake = engineBrakingTorque * Mathf.Lerp(1f, 0.3f, ratio);
            brakeTorque += engineBrake;
        }

        foreach (var w in wheels)
        {
            w.motorTorque = torque;
            w.brakeTorque = brakeTorque;
        }
    }

    void HandleSteering()
    {
        float angle = _finalSteer * maxSteerAngle;
        if (wheels.Length > 1)
        {
            wheels[0].steerAngle = angle;
            wheels[1].steerAngle = angle;
        }
    }

    void ResetCar()
    {
        transform.position += Vector3.up * 2f;
        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    void CalculateTelemetry()
    {
        Vector3 acc = (rb.linearVelocity - _lastVelocity) / Time.fixedDeltaTime;
        _lastVelocity = rb.linearVelocity;
        LocalGForce = Vector3.Lerp(LocalGForce, transform.InverseTransformDirection(acc) / 9.81f, Time.fixedDeltaTime * 5f);
    }

    void CheckTraction()
    {
        bool slip = false;
        foreach (var w in wheels)
        {
            if (w.GetGroundHit(out WheelHit h) && Mathf.Abs(h.sidewaysSlip) > 0.5f) slip = true;
        }
        OnTractionLoss?.Invoke(slip);
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.relativeVelocity.magnitude > 2f) OnCollisionForce?.Invoke(c.relativeVelocity.magnitude);
    }

    void UpdateVisuals()
    {
        if (gearText)
        {
            if (currentGear == -1) gearText.text = "R";
            else if (currentGear == 0) gearText.text = "N";
            else gearText.text = currentGear.ToString();
        }

        if (steeringWheel) steeringWheel.localRotation = Quaternion.Euler(0, 0, -_finalSteer * 450f);

        for (int i = 0; i < wheels.Length; i++)
        {
            if (i < wheelVisuals.Length && wheelVisuals[i])
            {
                wheels[i].GetWorldPose(out Vector3 p, out Quaternion r);
                wheelVisuals[i].position = p;
                wheelVisuals[i].rotation = r;
            }
        }
    }

    void UpdateSpeedometer()
    {
        if (!speedometerNeedle) return;
        float f = Mathf.Clamp01(CurrentSpeedKmh / speedometerMaxKmh);
        float angle = Mathf.Lerp(speedometerMinAngle, speedometerMaxAngle, f);
        Vector3 axis = invertSpeedometer ? -Vector3.forward : Vector3.forward;
        speedometerNeedle.localRotation = _initialNeedleRot * Quaternion.AngleAxis(angle, axis);
    }

    void UpdateAudio()
    {
        if (!engineSource) return;
        if (!engineSource.isPlaying && isActiveAndEnabled) engineSource.Play();
        engineSource.spatialBlend = spatialBlend;

        float targetRPM = idleRPM;

        if (currentGear == 0)
        {
            targetRPM = Mathf.Lerp(idleRPM, redlineRPM, _finalGas);
        }
        else
        {
            float gearMaxSpeed = currentGear == -1 ? 35f : gearSpeeds[Mathf.Clamp(currentGear - 1, 0, gearSpeeds.Length - 1)];
            float speedRatio = Mathf.Clamp01(CurrentSpeedKmh / gearMaxSpeed);
            float baseRPM = Mathf.Lerp(idleRPM, redlineRPM, speedRatio);
            float coastAdjusted = Mathf.Lerp(baseRPM * rpmCoastFactor, baseRPM, _finalGas);
            float boost = rpmBoostOnThrottle * _finalGas * (1f - speedRatio);
            targetRPM = Mathf.Clamp(coastAdjusted + boost, idleRPM, redlineRPM);
        }

        CurrentRPM = Mathf.Lerp(CurrentRPM, targetRPM, Time.deltaTime * rpmResponse);

        float rpm01 = Mathf.InverseLerp(idleRPM, redlineRPM, CurrentRPM);
        engineSource.pitch = Mathf.Lerp(minPitch, maxPitch, rpm01);
        engineSource.volume = Mathf.Lerp(0.5f, 1.0f, rpm01 * 0.6f + _finalGas * 0.4f);
    }
}