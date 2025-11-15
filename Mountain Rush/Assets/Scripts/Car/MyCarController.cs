using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody))]
public class MyCarController : MonoBehaviour
{
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

    public KeyCode flipKey = KeyCode.R;
    public float flipCooldown = 2f;
    public float flipRaise = 1.5f;
    public bool requireLowSpeedToFlip = true;
    public float maxFlipSpeedKmh = 10f;

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
    public KeyCode gearUpKey = KeyCode.E;
    public KeyCode gearDownKey = KeyCode.F;

    public Text gearText;

    public AudioSource engineSource;
    public AudioClip engineClip;
    public float engineSpatialBlend = 0.75f;
    public float engineMinDistance = 5f;
    public float engineMaxDistance = 50f;
    public float idleRPM = 900f;
    public float redlineRPM = 6000f;
    public float throttleRPMBoost = 800f;
    public float engineResponse = 10f;
    public float enginePitchAtIdle = 0.8f;
    public float enginePitchAtRedline = 2.0f;
    public float engineVolumeIdle = 0.2f;
    public float engineVolumeFull = 0.85f;

    const float Ms2Kmh = 3.6f;
    const bool kBlockUnsafeDownshift = true;
    const float kDownshiftSafetyFactor = 1.05f;
    const bool kBlockDirectionalShiftSpeed = true;
    const float kDirectionalShiftMaxKmh = 5f;
    const bool kBlockNeutralHighSpeed = true;
    const float kNeutralMaxKmh = 20f;
    const float kShiftSnapStrength = 0.85f;
    const float kUpshiftDipRPM = 400f;
    const float kDownshiftBlipRPM = 600f;
    const float kShiftFXTime = 0.12f;

    Rigidbody rb;
    float steerAngle;
    float currentSpeedKmh;
    int currentGear = 1;

    Quaternion steeringWheelInitialLocalRot;
    float steeringWheelCurrentAngle;
    Quaternion speedometerInitialLocalRot;
    float speedometerCurrentAngle;

    float engineRPM;
    float lastGasInput;
    float shiftFxTimer;
    float shiftFxAddRPM;

    bool queuedGearUp, queuedGearDown, queuedFlip;
    int queuedDirectGear;

    public float CurrentSpeedKmh => currentSpeedKmh;
    public int CurrentGear => currentGear;

    void Reset()
    {
        forwardGearMaxSpeedsKmh = new float[] { 20f, 70f, 100f, 120f };
        forwardGearTorqueMul = new float[] { 2.2f, 1.6f, 1.2f, 1.0f };
        maxSpeedKmh = 120f;
        startGear = 1;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = Mathf.Max(rb.mass, 1200f);
        rb.centerOfMass += centerOfMassOffset;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = 0.02f;
        rb.angularDamping = 0.5f;
        rb.solverIterations = 12;
        rb.solverVelocityIterations = 12;

        if (steeringWheel) steeringWheelInitialLocalRot = steeringWheel.localRotation;
        if (speedometerNeedle) speedometerInitialLocalRot = speedometerNeedle.localRotation;

        EnsureGearArrays();

        currentGear = Mathf.Clamp(startGear, -1, forwardGearMaxSpeedsKmh.Length);
        engineRPM = Mathf.Max(idleRPM, 900f);

        SetupEngineAudio();
        UpdateGearUI();
    }

    void OnEnable()
    {
        if (engineSource && engineClip && !engineSource.isPlaying) engineSource.Play();
    }

    void OnDisable()
    {
        if (engineSource) engineSource.Stop();
    }

    void Update()
    {
        PollButtonEventsInUpdate();
        UpdateSteeringWheelVisual();
        UpdateSpeedometerNeedle();
        UpdateEngineAudio();
        UpdateGearUI();
    }

    void FixedUpdate()
    {
        bool gearUpPressed = Consume(ref queuedGearUp);
        bool gearDownPressed = Consume(ref queuedGearDown);
        bool flipPressed = Consume(ref queuedFlip);
        int directGear = queuedDirectGear; queuedDirectGear = 0;

        ReadContinuousInput(out float steerInput, out float gasInput, out float brakeInput, out bool handbrake);
        lastGasInput = gasInput;
        currentSpeedKmh = rb.linearVelocity.magnitude * Ms2Kmh;

        int oldGear = currentGear;
        bool gearChanged = false;

        if (directGear > 0) gearChanged = TrySelectForwardGear(directGear);
        if (!gearChanged && TryHandleManualGearShift(gearUpPressed, gearDownPressed)) gearChanged = true;
        if (gearChanged) { OnGearChanged(oldGear, currentGear); UpdateGearUI(); }

        TryFlipIfRequested(flipPressed);

        float targetSteer = maxSteerAngle * steerInput;
        steerAngle = Mathf.Lerp(steerAngle, targetSteer, steerSmooth * Time.fixedDeltaTime);
        if (wheels.Length > 0 && wheels[0]) wheels[0].steerAngle = steerAngle;
        if (wheels.Length > 1 && wheels[1]) wheels[1].steerAngle = steerAngle;

        float motor = 0f;
        float brake = Mathf.Clamp01(brakeInput) * maxBrakeTorque;

        float forwardVel = Vector3.Dot(rb.linearVelocity, transform.forward);

        GetGearProperties(out float gearSign, out float gearTopKmh, out float torqueMul, out float launchFloor);

        float capKmh = gearTopKmh;
        if (useSpeedLimiter && maxSpeedKmh > 1f) capKmh = Mathf.Min(capKmh, maxSpeedKmh);

        float torqueLimiter = 1f;
        if (float.IsFinite(capKmh))
        {
            float speed01cap = Mathf.Clamp01(currentSpeedKmh / Mathf.Max(1f, capKmh));
            torqueLimiter = 1f - speed01cap;
        }

        bool movingOpposite = (currentGear > 0 && forwardVel < -0.5f) || (currentGear < 0 && forwardVel > 0.5f);

        if (gasInput > 0.01f && Mathf.Abs(gearSign) > 0.0001f)
        {
            if (movingOpposite) brake = Mathf.Max(brake, maxBrakeTorque);
            else
            {
                float speed01Gear = 0f;
                if (float.IsFinite(gearTopKmh) && gearTopKmh > 0.1f) speed01Gear = Mathf.Clamp01(currentSpeedKmh / gearTopKmh);
                float rpmFactor = Mathf.SmoothStep(0f, 1f, speed01Gear);
                float engineFactor = Mathf.Max(launchFloor, rpmFactor);
                float baseTorque = maxMotorTorque * gasInput * torqueMul;
                motor = gearSign * baseTorque * Mathf.Clamp01(engineFactor) * Mathf.Clamp01(torqueLimiter);
            }
        }

        if (handbrake) brake = Mathf.Max(brake, handbrakeTorque);

        ApplyDrive(motor, brake);

        rb.AddForce(-transform.up * (downforceCoeff * rb.linearVelocity.magnitude));

        if (wheels.Length >= 4)
        {
            ApplyAntiRoll(wheels[0], wheels[1]);
            ApplyAntiRoll(wheels[2], wheels[3]);
        }

        UpdateWheelVisuals();
    }

    void PollButtonEventsInUpdate()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame) queuedGearUp = true;
            if (Keyboard.current.fKey.wasPressedThisFrame) queuedGearDown = true;
            if (Keyboard.current.rKey.wasPressedThisFrame) queuedFlip = true;

            if (Keyboard.current.digit1Key?.wasPressedThisFrame == true || Keyboard.current.numpad1Key?.wasPressedThisFrame == true) queuedDirectGear = 1;
            if (Keyboard.current.digit2Key?.wasPressedThisFrame == true || Keyboard.current.numpad2Key?.wasPressedThisFrame == true) queuedDirectGear = 2;
            if (Keyboard.current.digit3Key?.wasPressedThisFrame == true || Keyboard.current.numpad3Key?.wasPressedThisFrame == true) queuedDirectGear = 3;
            if (Keyboard.current.digit4Key?.wasPressedThisFrame == true || Keyboard.current.numpad4Key?.wasPressedThisFrame == true) queuedDirectGear = 4;
            return;
        }
#endif
        if (Input.GetKeyDown(gearUpKey)) queuedGearUp = true;
        if (Input.GetKeyDown(gearDownKey)) queuedGearDown = true;
        if (Input.GetKeyDown(flipKey)) queuedFlip = true;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) queuedDirectGear = 1;
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) queuedDirectGear = 2;
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) queuedDirectGear = 3;
        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) queuedDirectGear = 4;
    }

    void ReadContinuousInput(out float steer, out float gas, out float brake, out bool handbrake)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            steer = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steer -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steer += 1f;
            steer = Mathf.Clamp(steer, -1f, 1f);

            gas = (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) ? 1f : 0f;
            brake = (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) ? 1f : 0f;
            handbrake = Keyboard.current.spaceKey.isPressed;
            return;
        }
#endif
        steer = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        gas = Mathf.Clamp01(v);
        brake = Mathf.Clamp01(-v);
        if (Input.GetKey(KeyCode.W)) gas = 1f;
        if (Input.GetKey(KeyCode.S)) brake = 1f;
        handbrake = Input.GetKey(KeyCode.Space);
    }

    static bool Consume(ref bool flag) { bool f = flag; flag = false; return f; }

    bool TrySelectForwardGear(int desired)
    {
        desired = Mathf.Clamp(desired, 1, forwardGearMaxSpeedsKmh.Length);
        if (currentGear < 0 && kBlockDirectionalShiftSpeed && currentSpeedKmh > kDirectionalShiftMaxKmh) return false;
        if (kBlockUnsafeDownshift && !IsSafeForTargetForwardGear(desired)) return false;
        if (currentGear != desired) { currentGear = desired; return true; }
        return false;
    }

    bool TryHandleManualGearShift(bool up, bool down)
    {
        int target = currentGear;

        if (up)
        {
            target = Mathf.Min(currentGear + 1, forwardGearMaxSpeedsKmh.Length);
            if (target > 0 && kBlockUnsafeDownshift && !IsSafeForTargetForwardGear(target)) return false;
        }

        if (down)
        {
            target = Mathf.Max(currentGear - 1, -1);
            if (kBlockDirectionalShiftSpeed)
            {
                bool dirChange = (currentGear >= 0 && target < 0) || (currentGear < 0 && target >= 0);
                if (dirChange && currentSpeedKmh > kDirectionalShiftMaxKmh) return false;
            }
            if (target == 0 && kBlockNeutralHighSpeed && currentSpeedKmh > kNeutralMaxKmh) return false;
            if (target > 0 && kBlockUnsafeDownshift && !IsSafeForTargetForwardGear(target)) return false;
        }

        if (target == currentGear) return false;
        currentGear = target;
        return true;
    }

    void GetGearProperties(out float sign, out float topKmh, out float torqueMul, out float launchFloor)
    {
        sign = 0f; topKmh = float.PositiveInfinity; torqueMul = 0f; launchFloor = 0.85f;

        float maxTop = GetMaxForwardTop();

        if (currentGear > 0)
        {
            int idx = Mathf.Clamp(currentGear - 1, 0, forwardGearMaxSpeedsKmh.Length - 1);
            topKmh = forwardGearMaxSpeedsKmh[idx];
            torqueMul = (forwardGearTorqueMul != null && forwardGearTorqueMul.Length > idx) ? forwardGearTorqueMul[idx] : 1f;
            sign = 1f;

            float ratioNorm = (maxTop > 0.01f) ? (maxTop / Mathf.Max(0.01f, topKmh)) : 1f;
            float ratioMax = GetMaxRatioNorm();
            float tRatio = (ratioNorm - 1f) / Mathf.Max(0.0001f, (ratioMax - 1f));
            launchFloor = Mathf.Lerp(0.15f, 0.85f, Mathf.Clamp01(tRatio));
        }
        else if (currentGear < 0)
        {
            sign = -1f;
            topKmh = reverseMaxSpeedKmh;
            torqueMul = (forwardGearTorqueMul != null && forwardGearTorqueMul.Length > 0) ? forwardGearTorqueMul[0] : 1f;
            float ratioNorm = (maxTop > 0.01f) ? (maxTop / Mathf.Max(0.01f, reverseMaxSpeedKmh)) : 1f;
            launchFloor = Mathf.Lerp(0.15f, 0.85f, 0.75f);
        }
        else
        {
            sign = 0f; topKmh = float.PositiveInfinity; torqueMul = 0f; launchFloor = 0.15f;
        }
    }

    float GetMaxForwardTop()
    {
        if (forwardGearMaxSpeedsKmh == null || forwardGearMaxSpeedsKmh.Length == 0) return Mathf.Max(1f, maxSpeedKmh);
        float m = 0f;
        for (int i = 0; i < forwardGearMaxSpeedsKmh.Length; i++) if (forwardGearMaxSpeedsKmh[i] > m) m = forwardGearMaxSpeedsKmh[i];
        return m;
    }

    float GetMaxRatioNorm()
    {
        float maxTop = GetMaxForwardTop();
        float minTop = maxTop;
        if (forwardGearMaxSpeedsKmh != null && forwardGearMaxSpeedsKmh.Length > 0)
            for (int i = 0; i < forwardGearMaxSpeedsKmh.Length; i++) if (forwardGearMaxSpeedsKmh[i] < minTop) minTop = forwardGearMaxSpeedsKmh[i];
        return (maxTop > 0.01f) ? (maxTop / Mathf.Max(0.01f, minTop)) : 1f;
    }

    bool IsSafeForTargetForwardGear(int gear)
    {
        if (gear < 1 || gear > forwardGearMaxSpeedsKmh.Length) return false;
        float top = forwardGearMaxSpeedsKmh[gear - 1] * Mathf.Max(1f, kDownshiftSafetyFactor);
        return currentSpeedKmh <= top + 0.001f;
    }

    void OnGearChanged(int oldGear, int newGear)
    {
        if (!engineSource || !engineClip) return;

        float newBaseRPM = EstimateRPMForSpeedAndGear(newGear);
        engineRPM = Mathf.Lerp(engineRPM, newBaseRPM, Mathf.Clamp01(kShiftSnapStrength));

        if (kShiftFXTime <= 0f) return;

        if (newGear > oldGear && oldGear >= 1) { shiftFxAddRPM = -Mathf.Abs(kUpshiftDipRPM); shiftFxTimer = kShiftFXTime; }
        else if (newGear >= 1 && oldGear >= 1 && newGear < oldGear) { shiftFxAddRPM = Mathf.Abs(kDownshiftBlipRPM); shiftFxTimer = kShiftFXTime; }
        else if (newGear == 0) { shiftFxAddRPM = 0f; shiftFxTimer = 0f; engineRPM = Mathf.Lerp(engineRPM, idleRPM, 0.7f); }
        else { shiftFxAddRPM = 0f; shiftFxTimer = 0f; }
    }

    void UpdateEngineAudio()
    {
        if (!engineSource || !engineClip) return;
        if (engineSource.clip != engineClip) engineSource.clip = engineClip;
        if (!engineSource.isPlaying && isActiveAndEnabled) engineSource.Play();

        float targetRPM = EstimateRPMForSpeedAndGear(currentGear);

        if (shiftFxTimer > 0f)
        {
            float k = Mathf.Clamp01(shiftFxTimer / Mathf.Max(0.0001f, kShiftFXTime));
            targetRPM += shiftFxAddRPM * k;
            shiftFxTimer -= Time.deltaTime;
        }

        targetRPM = Mathf.Clamp(targetRPM, idleRPM, redlineRPM);
        engineRPM = Mathf.Lerp(engineRPM, targetRPM, engineResponse * Time.deltaTime);

        float rpm01 = Mathf.InverseLerp(idleRPM, redlineRPM, engineRPM);
        float pitch = Mathf.Lerp(enginePitchAtIdle, enginePitchAtRedline, rpm01);
        pitch = Mathf.Clamp(pitch, 0.1f, 3f);

        float vol = Mathf.Lerp(engineVolumeIdle, engineVolumeFull, Mathf.Clamp01(0.7f * Mathf.Clamp01(lastGasInput) + 0.3f * rpm01));
        engineSource.pitch = pitch;
        engineSource.volume = vol;
    }

    float EstimateRPMForSpeedAndGear(int gear)
    {
        if (gear == 0)
        {
            float tGas = Mathf.Clamp01(lastGasInput);
            float rpm01 = Mathf.Pow(tGas, 1.4f);
            return Mathf.Lerp(idleRPM, redlineRPM, rpm01);
        }

        float gearTop = Mathf.Max(1f, (gear > 0) ? forwardGearMaxSpeedsKmh[Mathf.Clamp(gear - 1, 0, forwardGearMaxSpeedsKmh.Length - 1)] : reverseMaxSpeedKmh);
        float speed01 = Mathf.Clamp01(currentSpeedKmh / gearTop);
        float rpmFromSpeed01 = Mathf.SmoothStep(0f, 1f, speed01);
        float baseRPM = Mathf.Lerp(idleRPM, redlineRPM, rpmFromSpeed01);
        float boost = throttleRPMBoost * Mathf.Clamp01(lastGasInput) * (1f - rpmFromSpeed01);
        return baseRPM + boost;
    }

    void SetupEngineAudio()
    {
        if (!engineSource) engineSource = GetComponent<AudioSource>();
        if (!engineSource) engineSource = gameObject.AddComponent<AudioSource>();

        engineSource.loop = true;
        engineSource.playOnAwake = false;
        engineSource.clip = engineClip;
        engineSource.spatialBlend = engineSpatialBlend;
        engineSource.minDistance = engineMinDistance;
        engineSource.maxDistance = engineMaxDistance;
        engineSource.dopplerLevel = 1f;
        engineSource.pitch = enginePitchAtIdle;
        engineSource.volume = engineVolumeIdle;

        if (engineClip && isActiveAndEnabled) engineSource.Play();
    }

    void ApplyDrive(float motor, float brake)
    {
        for (int i = 0; i < wheels.Length; i++) { if (!wheels[i]) continue; wheels[i].motorTorque = 0f; wheels[i].brakeTorque = 0f; }

        void DriveWheel(int i) { if (i < 0 || i >= wheels.Length) return; var wc = wheels[i]; if (!wc) return; wc.motorTorque = motor; }

        switch (driveType)
        {
            case DriveType.FWD: DriveWheel(0); DriveWheel(1); break;
            case DriveType.RWD: DriveWheel(2); DriveWheel(3); break;
            case DriveType.AWD: DriveWheel(0); DriveWheel(1); DriveWheel(2); DriveWheel(3); break;
        }

        for (int i = 0; i < wheels.Length; i++) { if (!wheels[i]) continue; wheels[i].brakeTorque = Mathf.Max(wheels[i].brakeTorque, brake); }
    }

    void ApplyAntiRoll(WheelCollider left, WheelCollider right)
    {
        if (!left || !right) return;

        bool groundedL = left.GetGroundHit(out WheelHit hitL);
        bool groundedR = right.GetGroundHit(out WheelHit hitR);

        float travelL = 1f, travelR = 1f;

        if (groundedL) travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / Mathf.Max(0.0001f, left.suspensionDistance);
        if (groundedR) travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / Mathf.Max(0.0001f, right.suspensionDistance);

        float antiRollForce = (travelL - travelR) * antiRollStiffness;

        if (groundedL) rb.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
        if (groundedR) rb.AddForceAtPosition(right.transform.up * antiRollForce, right.transform.position);
    }

    void UpdateWheelVisuals()
    {
        int count = Mathf.Min(wheels.Length, wheelVisuals.Length);
        for (int i = 0; i < count; i++)
        {
            var wc = wheels[i]; var vis = wheelVisuals[i];
            if (!wc || !vis) continue;
            wc.GetWorldPose(out Vector3 pos, out Quaternion rot);
            vis.position = pos;
            vis.rotation = rot;
        }
    }

    void UpdateSteeringWheelVisual()
    {
        if (!steeringWheel || maxSteerAngle <= 0.0001f) return;
        float t = Mathf.Clamp(steerAngle / maxSteerAngle, -1f, 1f);
        float targetAngle = t * steeringWheelMaxAngle * (steeringWheelInvert ? -1f : 1f);
        steeringWheelCurrentAngle = Mathf.Lerp(steeringWheelCurrentAngle, targetAngle, steeringWheelSmooth * Time.deltaTime);
        Quaternion add = Quaternion.AngleAxis(steeringWheelCurrentAngle, Vector3.forward);
        steeringWheel.localRotation = steeringWheelInitialLocalRot * add;
    }

    void UpdateSpeedometerNeedle()
    {
        if (!speedometerNeedle) return;
        float gaugeMax = Mathf.Max(1f, speedometerGaugeMaxKmh);
        float t = Mathf.InverseLerp(0f, gaugeMax, Mathf.Max(0f, currentSpeedKmh));
        float targetAngle = Mathf.Lerp(speedometerMinAngle, speedometerMaxAngle, t);
        if (speedometerInvert) targetAngle = -targetAngle;
        speedometerCurrentAngle = Mathf.Lerp(speedometerCurrentAngle, targetAngle, speedometerSmooth * Time.deltaTime);
        Quaternion add = Quaternion.AngleAxis(speedometerCurrentAngle, Vector3.forward);
        speedometerNeedle.localRotation = speedometerInitialLocalRot * add;
    }

    void UpdateGearUI()
    {
        if (!gearText) return;
        gearText.text = (currentGear < 0) ? "R" : (currentGear == 0 ? "N" : currentGear.ToString());
    }

    float lastFlipTime = -999f;

    void TryFlipIfRequested(bool requested)
    {
        if (!requested) return;
        if (Time.time - lastFlipTime < flipCooldown) return;
        if (requireLowSpeedToFlip && currentSpeedKmh > maxFlipSpeedKmh) return;
        DoFlip();
        lastFlipTime = Time.time;
    }

    void DoFlip()
    {
        float yaw = transform.eulerAngles.y;
        Vector3 origin = transform.position + Vector3.up * 2f;
        Vector3 newPos;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
            newPos = hit.point + Vector3.up * Mathf.Max(0.5f, flipRaise);
        else
            newPos = transform.position + Vector3.up * Mathf.Max(0.5f, flipRaise);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.MovePosition(newPos);
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
    }

    void EnsureGearArrays()
    {
        if (forwardGearMaxSpeedsKmh == null || forwardGearMaxSpeedsKmh.Length == 0) forwardGearMaxSpeedsKmh = new float[] { 20f, 70f, 100f, 120f };
        if (forwardGearTorqueMul == null || forwardGearTorqueMul.Length == 0) forwardGearTorqueMul = new float[] { 2.2f, 1.6f, 1.2f, 1.0f };
        if (forwardGearTorqueMul.Length != forwardGearMaxSpeedsKmh.Length)
        {
            System.Array.Resize(ref forwardGearTorqueMul, forwardGearMaxSpeedsKmh.Length);
            for (int i = 0; i < forwardGearTorqueMul.Length; i++) if (forwardGearTorqueMul[i] <= 0f) forwardGearTorqueMul[i] = 1f;
        }
    }
}