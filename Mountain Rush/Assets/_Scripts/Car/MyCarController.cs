using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class MyCarController : MonoBehaviour
{
    private InputController _controls;

    public int CurrentGear => currentGear;
    public float CurrentSpeedKmh => currentSpeedKmh;
    public float CurrentRPM => engineRPM;
    public float ThrottleInput => lastGasInput;
    public Vector3 LocalGForce { get; private set; }

    public event Action OnGearShiftUp;
    public event Action OnGearShiftDown;
    public event Action<float> OnCollisionForce;
    public event Action OnRevLimiterHit;
    public event Action<bool> OnTractionLoss;

    private Vector3 _lastVelocity;

    public enum DriveType { FWD, RWD, AWD }
    public WheelCollider[] wheels = new WheelCollider[4];
    public Transform[] wheelVisuals = new Transform[4];
    public DriveType driveType = DriveType.RWD;
    public float maxMotorTorque = 3000f;
    public float maxSteerAngle = 28f;
    public float steerSmooth = 8f;
    public float maxBrakeTorque = 3500f;
    public float handbrakeTorque = 4500f;
    public bool useSpeedLimiter = true;
    public float maxSpeedKmh = 120f;
    public float antiRollStiffness = 6000f;
    public float downforceCoeff = 30f;
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.35f, 0f);

    public Transform steeringWheel;
    public float steeringWheelMaxAngle = 450f;
    public float steeringWheelSmooth = 12f;
    public bool steeringWheelInvert = false;

    public Transform speedometerNeedle;
    public float speedometerMinAngle = -120f;
    public float speedometerMaxAngle = 120f;
    public float speedometerGaugeMaxKmh = 240f;
    public float speedometerSmooth = 10f;
    public bool speedometerInvert = false;

    public float[] forwardGearMaxSpeedsKmh;
    public float[] forwardGearTorqueMul;
    public float reverseMaxSpeedKmh = 25f;
    public int startGear = 1;
    public Text gearText;

    public AudioSource engineSource;
    public AudioClip engineClip;
    public float idleRPM = 900f;
    public float redlineRPM = 6000f;
    public float throttleRPMBoost = 800f;
    public float engineResponse = 10f;
    public float enginePitchAtIdle = 0.8f;
    public float enginePitchAtRedline = 2.0f;
    public float engineVolumeIdle = 0.2f;
    public float engineVolumeFull = 0.85f;

    const float Ms2Kmh = 3.6f;
    const float kDownshiftSafetyFactor = 1.05f;
    const float kShiftSnapStrength = 0.85f;
    const float kUpshiftDipRPM = 400f;
    const float kDownshiftBlipRPM = 600f;
    const float kShiftFXTime = 0.12f;

    Rigidbody rb;
    float steerAngle;
    float currentSpeedKmh;
    
    int currentGear = 1;
    float engineRPM;
    float lastGasInput;
    float lastBrakeInput;
    float shiftFxTimer;
    float shiftFxAddRPM;

    bool queuedGearUp, queuedGearDown;
    int queuedDirectGear;

    Quaternion steeringWheelInitialLocalRot;
    float steeringWheelCurrentAngle;
    Quaternion speedometerInitialLocalRot;
    float speedometerCurrentAngle;

    float _debugRawSteer, _debugRawGas, _debugRawBrake;

    void Awake()
    {
        _controls = new InputController();
        rb = GetComponent<Rigidbody>();
        rb.mass = Mathf.Max(rb.mass, 1200f);
        rb.centerOfMass += centerOfMassOffset;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 0.02f; 
        rb.angularDamping = 0.5f;

        if (steeringWheel) steeringWheelInitialLocalRot = steeringWheel.localRotation;
        if (speedometerNeedle) speedometerInitialLocalRot = speedometerNeedle.localRotation;

        EnsureGearArrays();
        currentGear = Mathf.Clamp(startGear, -1, forwardGearMaxSpeedsKmh.Length);
        engineRPM = Mathf.Max(idleRPM, 900f);
        SetupEngineAudio();
    }

    void OnEnable()
    {
        _controls.Enable();
        if (engineSource && engineClip && !engineSource.isPlaying) engineSource.Play();
        if(rb) _lastVelocity = rb.linearVelocity;
    }

    void OnDisable()
    {
        _controls.Disable();
        if (engineSource) engineSource.Stop();
    }

    void Update()
    {
        PollInputControlsButtons();
        UpdateSteeringWheelVisual();
        UpdateSpeedometerNeedle();
        UpdateEngineAudio();
        UpdateGearUI();
    }

    void FixedUpdate()
    {
        ReadInputControlsAxes(out float steerInput, out float gasInput, out float brakeInput);

        bool gearUpPressed = Consume(ref queuedGearUp);
        bool gearDownPressed = Consume(ref queuedGearDown);
        int directGear = queuedDirectGear; queuedDirectGear = 0;

        lastGasInput = gasInput;
        lastBrakeInput = brakeInput;
        currentSpeedKmh = rb.linearVelocity.magnitude * Ms2Kmh;

        int oldGear = currentGear;
        bool gearChanged = false;

        if (directGear > 0) gearChanged = TrySelectForwardGear(directGear);
        if (!gearChanged && TryHandleManualGearShift(gearUpPressed, gearDownPressed)) gearChanged = true;
        
        if (gearChanged) { OnGearChanged(oldGear, currentGear); UpdateGearUI(); }

        float targetSteer = maxSteerAngle * steerInput;
        steerAngle = Mathf.Lerp(steerAngle, targetSteer, steerSmooth * Time.fixedDeltaTime);
        if (wheels.Length > 0) wheels[0].steerAngle = steerAngle;
        if (wheels.Length > 1) wheels[1].steerAngle = steerAngle;

        float motor = 0f;
        float brake = Mathf.Clamp01(brakeInput) * maxBrakeTorque;

        GetGearProperties(out float gearSign, out float gearTopKmh, out float torqueMul, out float launchFloor);
        float capKmh = (useSpeedLimiter && maxSpeedKmh > 1f) ? Mathf.Min(gearTopKmh, maxSpeedKmh) : gearTopKmh;
        float torqueLimiter = 1f;
        if (float.IsFinite(capKmh))
            torqueLimiter = 1f - Mathf.Clamp01(currentSpeedKmh / Mathf.Max(1f, capKmh));

        float forwardVel = Vector3.Dot(rb.linearVelocity, transform.forward);
        bool movingOpposite = (currentGear > 0 && forwardVel < -0.5f) || (currentGear < 0 && forwardVel > 0.5f);

        if (gasInput > 0.01f && Mathf.Abs(gearSign) > 0.0001f)
        {
            if (movingOpposite) brake = Mathf.Max(brake, maxBrakeTorque);
            else
            {
                float speed01Gear = (float.IsFinite(gearTopKmh) && gearTopKmh > 0.1f) ? Mathf.Clamp01(currentSpeedKmh / gearTopKmh) : 0f;
                float rpmFactor = Mathf.SmoothStep(0f, 1f, speed01Gear);
                float engineFactor = Mathf.Max(launchFloor, rpmFactor);
                motor = gearSign * maxMotorTorque * gasInput * torqueMul * engineFactor * torqueLimiter;
            }
        }

        ApplyDrive(motor, brake);

        rb.AddForce(-transform.up * (downforceCoeff * rb.linearVelocity.magnitude));
        
        if (wheels.Length >= 4) {
            ApplyAntiRoll(wheels[0], wheels[1]);
            ApplyAntiRoll(wheels[2], wheels[3]);
            CheckTractionLoss();
        }

        UpdatePlatformData();
        UpdateWheelVisuals();
    }

    void ReadInputControlsAxes(out float steer, out float gas, out float brake)
    {
        steer = _controls.Steeringwheel.Steering_Steering.ReadValue<float>();
        float rawGas = _controls.Pedals.Throttle.ReadValue<float>();
        gas = Mathf.Clamp01(rawGas);
        float rawBrake = _controls.Pedals.Brake.ReadValue<float>();
        brake = Mathf.Clamp01(rawBrake);

        _debugRawSteer = steer;
        _debugRawGas = gas;
        _debugRawBrake = brake;

        if (Mathf.Abs(steer) < 0.05f && gas < 0.05f && brake < 0.05f && Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steer = -1;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steer = 1;
            
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) gas = 1;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) brake = 1;
        }
    }

    void PollInputControlsButtons()
    {
        if (_controls.Buttons.RightBumper.triggered) queuedGearUp = true;
        if (_controls.Buttons.LeftBumper.triggered) queuedGearDown = true;

        if (_controls.Transmission.Shifter1.triggered) queuedDirectGear = 1;
        if (_controls.Transmission.Shifter2.triggered) queuedDirectGear = 2;
        if (_controls.Transmission.Shifter3.triggered) queuedDirectGear = 3;
        if (_controls.Transmission.Shifter4.triggered) queuedDirectGear = 4;
        if (_controls.Transmission.Shifter5.triggered) queuedDirectGear = 5;
        if (_controls.Transmission.Shifter6.triggered) queuedDirectGear = 6;
        if (_controls.Transmission.Shifter7.triggered) queuedDirectGear = -1;

        if (Keyboard.current != null) {
            if (Keyboard.current.eKey.wasPressedThisFrame) queuedGearUp = true;
            if (Keyboard.current.fKey.wasPressedThisFrame) queuedGearDown = true;
        }
    }

    void UpdatePlatformData()
    {
        Vector3 currentVel = rb.linearVelocity;
        Vector3 acceleration = (currentVel - _lastVelocity) / Time.fixedDeltaTime;
        Vector3 rawG = transform.InverseTransformDirection(acceleration) / 9.81f;
        LocalGForce = Vector3.Lerp(LocalGForce, rawG, Time.fixedDeltaTime * 5f);
        _lastVelocity = currentVel;
    }

    void CheckTractionLoss()
    {
        bool drifting = false;
        foreach(var w in wheels) {
            if(w.GetGroundHit(out WheelHit h) && Mathf.Abs(h.sidewaysSlip) > 0.6f) { drifting = true; break; }
        }
        OnTractionLoss?.Invoke(drifting);
    }
    
    void OnCollisionEnter(Collision c) {
        if(c.relativeVelocity.magnitude > 2f) OnCollisionForce?.Invoke(Mathf.Clamp01(c.relativeVelocity.magnitude/20f));
    }

    static bool Consume(ref bool flag) { bool f = flag; flag = false; return f; }

    bool TrySelectForwardGear(int desired)
    {
        desired = Mathf.Clamp(desired, 1, forwardGearMaxSpeedsKmh.Length);
        if (currentGear != desired) { currentGear = desired; return true; }
        return false;
    }

    bool TryHandleManualGearShift(bool up, bool down)
    {
        int target = currentGear;
        if (up) target = Mathf.Min(currentGear + 1, forwardGearMaxSpeedsKmh.Length);
        if (down) target = Mathf.Max(currentGear - 1, -1);
        if (target == currentGear) return false;
        currentGear = target;
        return true;
    }

    void OnGearChanged(int oldGear, int newGear)
    {
        if(newGear > oldGear) OnGearShiftUp?.Invoke();
        else if(newGear < oldGear) OnGearShiftDown?.Invoke();

        if (engineSource && engineClip) {
            float newBaseRPM = EstimateRPMForSpeedAndGear(newGear);
            engineRPM = Mathf.Lerp(engineRPM, newBaseRPM, Mathf.Clamp01(kShiftSnapStrength));
            if (newGear > oldGear && oldGear >= 1) { shiftFxAddRPM = -Mathf.Abs(kUpshiftDipRPM); shiftFxTimer = kShiftFXTime; }
            else if (newGear < oldGear && newGear >= 1) { shiftFxAddRPM = Mathf.Abs(kDownshiftBlipRPM); shiftFxTimer = kShiftFXTime; }
        }
    }

    void GetGearProperties(out float sign, out float topKmh, out float torqueMul, out float launchFloor)
    {
        sign = 0f; topKmh = float.PositiveInfinity; torqueMul = 0f; launchFloor = 0.85f;
        if (currentGear > 0) {
            int idx = Mathf.Clamp(currentGear - 1, 0, forwardGearMaxSpeedsKmh.Length - 1);
            topKmh = forwardGearMaxSpeedsKmh[idx];
            torqueMul = (forwardGearTorqueMul != null && forwardGearTorqueMul.Length > idx) ? forwardGearTorqueMul[idx] : 1f;
            sign = 1f;
        } else if (currentGear < 0) {
            sign = -1f; topKmh = reverseMaxSpeedKmh; torqueMul = 1f;
        }
    }

    void ApplyDrive(float motor, float brake)
    {
        for (int i = 0; i < wheels.Length; i++) { if (wheels[i]) { wheels[i].motorTorque = 0f; wheels[i].brakeTorque = 0f; } }
        void DW(int i) { if (i >= 0 && i < wheels.Length && wheels[i]) wheels[i].motorTorque = motor; }
        switch (driveType) { case DriveType.FWD: DW(0); DW(1); break; case DriveType.RWD: DW(2); DW(3); break; case DriveType.AWD: DW(0); DW(1); DW(2); DW(3); break; }
        for (int i = 0; i < wheels.Length; i++) if (wheels[i]) wheels[i].brakeTorque = Mathf.Max(wheels[i].brakeTorque, brake);
    }

    void ApplyAntiRoll(WheelCollider l, WheelCollider r)
    {
        if (!l || !r) return;
        float tL = 1f, tR = 1f;
        if (l.GetGroundHit(out WheelHit hL)) tL = (-l.transform.InverseTransformPoint(hL.point).y - l.radius) / l.suspensionDistance;
        if (r.GetGroundHit(out WheelHit hR)) tR = (-r.transform.InverseTransformPoint(hR.point).y - r.radius) / r.suspensionDistance;
        float f = (tL - tR) * antiRollStiffness;
        if (hL.collider) rb.AddForceAtPosition(l.transform.up * -f, l.transform.position);
        if (hR.collider) rb.AddForceAtPosition(r.transform.up * f, r.transform.position);
    }

    void UpdateEngineAudio()
    {
        if (!engineSource || !engineClip) return;
        if (engineSource.clip != engineClip) engineSource.clip = engineClip;
        if (!engineSource.isPlaying && isActiveAndEnabled) engineSource.Play();

        float targetRPM = EstimateRPMForSpeedAndGear(currentGear);
        if (shiftFxTimer > 0f) { targetRPM += shiftFxAddRPM * (shiftFxTimer / kShiftFXTime); shiftFxTimer -= Time.deltaTime; }
        
        targetRPM = Mathf.Clamp(targetRPM, idleRPM, redlineRPM);
        engineRPM = Mathf.Lerp(engineRPM, targetRPM, engineResponse * Time.deltaTime);
        if (engineRPM >= redlineRPM * 0.95f) OnRevLimiterHit?.Invoke();

        float rpm01 = Mathf.InverseLerp(idleRPM, redlineRPM, engineRPM);
        engineSource.pitch = Mathf.Clamp(Mathf.Lerp(enginePitchAtIdle, enginePitchAtRedline, rpm01), 0.1f, 3f);
        engineSource.volume = Mathf.Lerp(engineVolumeIdle, engineVolumeFull, Mathf.Clamp01(0.7f * Mathf.Clamp01(lastGasInput) + 0.3f * rpm01));
    }

    float EstimateRPMForSpeedAndGear(int gear)
    {
        if (gear == 0) return Mathf.Lerp(idleRPM, redlineRPM, Mathf.Pow(Mathf.Clamp01(lastGasInput), 1.4f));
        float gearTop = Mathf.Max(1f, (gear > 0) ? forwardGearMaxSpeedsKmh[Mathf.Clamp(gear - 1, 0, forwardGearMaxSpeedsKmh.Length - 1)] : reverseMaxSpeedKmh);
        float speed01 = Mathf.Clamp01(currentSpeedKmh / gearTop);
        return Mathf.Lerp(idleRPM, redlineRPM, Mathf.SmoothStep(0f, 1f, speed01)) + (throttleRPMBoost * Mathf.Clamp01(lastGasInput) * (1f - speed01));
    }

    void SetupEngineAudio()
    {
        if (!engineSource) engineSource = GetComponent<AudioSource>();
        if (!engineSource) engineSource = gameObject.AddComponent<AudioSource>();
        engineSource.loop = true;
        engineSource.spatialBlend = 0.75f;
    }

    void UpdateWheelVisuals()
    {
        for (int i = 0; i < Mathf.Min(wheels.Length, wheelVisuals.Length); i++)
        {
            if (wheels[i] && wheelVisuals[i]) {
                wheels[i].GetWorldPose(out Vector3 p, out Quaternion r);
                wheelVisuals[i].position = p; wheelVisuals[i].rotation = r;
            }
        }
    }

    void UpdateSteeringWheelVisual()
    {
        if (!steeringWheel) return;
        float target = (steerAngle / maxSteerAngle) * steeringWheelMaxAngle * (steeringWheelInvert ? -1f : 1f);
        steeringWheelCurrentAngle = Mathf.Lerp(steeringWheelCurrentAngle, target, steeringWheelSmooth * Time.deltaTime);
        steeringWheel.localRotation = steeringWheelInitialLocalRot * Quaternion.AngleAxis(steeringWheelCurrentAngle, Vector3.forward);
    }

    void UpdateSpeedometerNeedle()
    {
        if (!speedometerNeedle) return;
        float t = Mathf.InverseLerp(0f, speedometerGaugeMaxKmh, currentSpeedKmh);
        float target = Mathf.Lerp(speedometerMinAngle, speedometerMaxAngle, t) * (speedometerInvert ? -1f : 1f);
        speedometerCurrentAngle = Mathf.Lerp(speedometerCurrentAngle, target, speedometerSmooth * Time.deltaTime);
        speedometerNeedle.localRotation = speedometerInitialLocalRot * Quaternion.AngleAxis(speedometerCurrentAngle, Vector3.forward);
    }

    void UpdateGearUI() { if (gearText) gearText.text = (currentGear < 0) ? "R" : (currentGear == 0 ? "N" : currentGear.ToString()); }

    void EnsureGearArrays() { if (forwardGearMaxSpeedsKmh == null || forwardGearMaxSpeedsKmh.Length == 0) { forwardGearMaxSpeedsKmh = new float[] { 20f, 70f, 100f, 120f }; forwardGearTorqueMul = new float[] { 2.2f, 1.6f, 1.2f, 1.0f }; } }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 20;
        GUI.color = Color.cyan;
        
        GUILayout.BeginArea(new Rect(10, 10, 600, 600));
        GUILayout.Label("<b>[2DOF SIMULATOR DEBUG]</b>");
        
        GUILayout.Space(10);
        GUILayout.Label($"<b>Steer (Raw/Final):</b> {_debugRawSteer:F2} / {(steerAngle/maxSteerAngle):F2}");
        GUILayout.Label($"<b>Gas (Raw/Final):</b> {_debugRawGas:F2} / {lastGasInput:F2}");
        GUILayout.Label($"<b>Brake (Raw/Final):</b> {_debugRawBrake:F2} / {lastBrakeInput:F2}");
        
        GUILayout.Space(10);
        GUILayout.Label($"<b>Speed:</b> {currentSpeedKmh:F0} km/h");
        GUILayout.Label($"<b>Gear:</b> {currentGear}");
        GUILayout.Label($"<b>RPM:</b> {engineRPM:F0}");
        
        GUILayout.Space(10);
        GUILayout.Label($"<b>Local G-Force:</b> {LocalGForce}");
        
        GUILayout.Space(10);
        if (_debugRawSteer == 0 && _debugRawGas == 0)
            GUILayout.Label("<color=yellow>USING KEYBOARD FALLBACK (WASD)</color>");
        else
            GUILayout.Label("<color=green>WHEEL DETECTED</color>");

        GUILayout.EndArea();
    }
}