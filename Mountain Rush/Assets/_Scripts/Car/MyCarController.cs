using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.InputSystem;
using LogitechG29.Sample.Input;

[RequireComponent(typeof(Rigidbody))]
public class MyCarController : MonoBehaviour
{
    [Header("Logitech Plugin Setup")]
    [SerializeField] private InputControllerReader _inputReader;

    private Rigidbody rb;

    public float CurrentSpeedKmh { get; private set; }
    public float CurrentRPM { get; private set; }
    public Vector3 LocalGForce { get; private set; }
    public float idleRPM = 900f;
    public float redlineRPM = 6000f;

    public event Action OnGearShiftUp;
    public event Action OnGearShiftDown;
    public event Action<float> OnCollisionForce;
    public event Action OnRevLimiterHit;
    public event Action<bool> OnTractionLoss;

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

    [Header("Car Physics")]
    public float maxMotorTorque = 3000f;
    public float maxSteerAngle = 30f;
    public float maxBrakeTorque = 4000f;
    public float[] gearSpeeds = { 40, 80, 120, 160, 200, 240 };

    private int currentGear = 1; 
    private Vector3 _lastVelocity;
    
    [Header("DEBUG INPUT")]
    [SerializeField] private float _wheelSteer = 0f;
    [SerializeField] private float _wheelGas = 0f;
    [SerializeField] private float _wheelBrake = 0f;

    private float _finalSteer;
    private float _finalGas;
    private float _finalBrake;
    
    private bool _queuedUp, _queuedDown;
    private Quaternion _initialNeedleRot;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0);
        CurrentRPM = idleRPM;

        if (speedometerNeedle) 
            _initialNeedleRot = speedometerNeedle.localRotation;
    }

    void OnEnable() 
    { 
        if (_inputReader != null)
        {
            _inputReader.SteeringCallback += (v) => _wheelSteer = v;
            _inputReader.ThrottleCallback += (v) => _wheelGas = v;
            _inputReader.BrakeCallback += (v) => _wheelBrake = v;

            _inputReader.OnRightShiftCallback += HandlePaddleUp;
            _inputReader.OnLeftShiftCallback += HandlePaddleDown;
            
            _inputReader.Shifter1Callback += (v) => HandleHShifter(1, v);
            _inputReader.Shifter2Callback += (v) => HandleHShifter(2, v);
            _inputReader.Shifter3Callback += (v) => HandleHShifter(3, v);
            _inputReader.Shifter4Callback += (v) => HandleHShifter(4, v);
            _inputReader.Shifter5Callback += (v) => HandleHShifter(5, v);
            _inputReader.Shifter6Callback += (v) => HandleHShifter(6, v);
            _inputReader.Shifter7Callback += (v) => HandleHShifter(-1, v);

            _inputReader.OnNorthButtonCallback += HandleResetCarInput;
        }
    }

    void OnDisable() 
    { 
        if (_inputReader != null)
        {
             _inputReader.OnRightShiftCallback -= HandlePaddleUp;
             _inputReader.OnLeftShiftCallback -= HandlePaddleDown;
             _inputReader.OnNorthButtonCallback -= HandleResetCarInput;
        }
    }

    private void HandlePaddleUp(bool pressed) { if (pressed) _queuedUp = true; }
    private void HandlePaddleDown(bool pressed) { if (pressed) _queuedDown = true; }
    private void HandleResetCarInput(bool pressed) { if (pressed) ResetCar(); }

    private void HandleHShifter(int gear, bool active)
    {
        if (active) {
            currentGear = gear;
            if (gear > 0) OnGearShiftUp?.Invoke();
            if (gear < 0) OnGearShiftDown?.Invoke();
        }
        else if (currentGear == gear) {
            currentGear = 0; 
        }
    }

    void Start()
    {
        if (engineSource != null)
        {
            if (engineClip != null) engineSource.clip = engineClip;
            engineSource.loop = true;
            engineSource.spatialBlend = spatialBlend; 
            engineSource.volume = 0.5f; 
            if(!engineSource.isPlaying) engineSource.Play();
        }
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

        if (Keyboard.current != null)
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
        CalculatePhysicsTelemetry();
        CheckTraction();
    }

    void ResetCar()
    {
        transform.position += Vector3.up * 2.0f;
        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    void CalculatePhysicsTelemetry()
    {
        Vector3 acceleration = (rb.linearVelocity - _lastVelocity) / Time.fixedDeltaTime;
        _lastVelocity = rb.linearVelocity;
        Vector3 localAcc = transform.InverseTransformDirection(acceleration);
        Vector3 rawG = localAcc / 9.81f;
        LocalGForce = Vector3.Lerp(LocalGForce, rawG, Time.fixedDeltaTime * 5f);
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
            if (currentGear > 1 && CurrentSpeedKmh > gearSpeeds[currentGear - 2] * 1.2f) { canShift = false; OnRevLimiterHit?.Invoke(); }

            if (canShift && currentGear > -1) { currentGear--; OnGearShiftDown?.Invoke(); }
            _queuedDown = false;
        }
    }

    void HandleEngine()
    {
        float torque = 0;
        float currentMaxSpeed = 0;

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
            torque = 0;
        }
        else
        {
            if (CurrentSpeedKmh < currentMaxSpeed)
            {
                float direction = (currentGear > 0) ? 1f : -1f;
                torque = maxMotorTorque * _finalGas * direction;
            }
            else
            {
                OnRevLimiterHit?.Invoke();
            }
        }

        foreach (var w in wheels)
        {
            w.motorTorque = torque;
            w.brakeTorque = _finalBrake * maxBrakeTorque;
        }
    }

    void HandleSteering()
    {
        float angle = _finalSteer * maxSteerAngle;
        if(wheels.Length > 1) 
        {
            wheels[0].steerAngle = angle;
            wheels[1].steerAngle = angle;
        }
    }

    void CheckTraction()
    {
        bool slip = false;
        foreach(var w in wheels) {
            if(w.GetGroundHit(out WheelHit hit)) 
                if(Mathf.Abs(hit.sidewaysSlip) > 0.5f) slip = true;
        }
        OnTractionLoss?.Invoke(slip);
    }
    
    void OnCollisionEnter(Collision c)
    {
        if (c.relativeVelocity.magnitude > 2f)
            OnCollisionForce?.Invoke(c.relativeVelocity.magnitude);
    }

    void UpdateVisuals()
    {
        if (gearText) 
        {
            if (currentGear == -1) gearText.text = "R";
            else if (currentGear == 0) gearText.text = "N";
            else gearText.text = currentGear.ToString();
        }

        if (steeringWheel) 
            steeringWheel.localRotation = Quaternion.Euler(0, 0, -_finalSteer * 450f);
        
        for(int i=0; i<wheels.Length; i++) {
            if(i < wheelVisuals.Length && wheelVisuals[i] != null) {
                wheels[i].GetWorldPose(out Vector3 p, out Quaternion r);
                wheelVisuals[i].position = p; 
                wheelVisuals[i].rotation = r;
            }
        }
    }

    void UpdateSpeedometer()
    {
        if (!speedometerNeedle) return;
        float speedFactor = Mathf.Clamp01(CurrentSpeedKmh / speedometerMaxKmh);
        float currentAngle = Mathf.Lerp(speedometerMinAngle, speedometerMaxAngle, speedFactor);
        Vector3 axis = invertSpeedometer ? -Vector3.forward : Vector3.forward;
        speedometerNeedle.localRotation = _initialNeedleRot * Quaternion.AngleAxis(currentAngle, axis);
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
            float gearMaxSpeed = (currentGear == -1) ? 35f : gearSpeeds[Mathf.Clamp(currentGear - 1, 0, gearSpeeds.Length - 1)];
            float speedRatio = Mathf.Clamp01(CurrentSpeedKmh / gearMaxSpeed);
            targetRPM = Mathf.Lerp(idleRPM, redlineRPM, speedRatio);
            if (_finalGas > 0) targetRPM += 500f * _finalGas; 
        }

        CurrentRPM = Mathf.Lerp(CurrentRPM, targetRPM, Time.deltaTime * 5f);
        engineSource.pitch = Mathf.Lerp(minPitch, maxPitch, Mathf.InverseLerp(idleRPM, redlineRPM, CurrentRPM));
        engineSource.volume = Mathf.Lerp(0.5f, 1.0f, Mathf.InverseLerp(idleRPM, redlineRPM, CurrentRPM) * 0.7f + _finalGas * 0.3f);
    }
}