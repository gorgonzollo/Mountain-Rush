using System.Collections;
using UnityEngine;
using _2DOF;

public class PlatformConnectorMMF : MonoBehaviour
{
    public MyCarController carController;
    
    public float pitchGain = 2.0f; 
    public float rollGain = 2.5f;
    public float maxAngle = 15.0f;
    public float smoothing = 5.0f;
    public float idleVibration = 0.15f;
    public float rpmVibrationFactor = 0.05f;

    private SendingData _sendingData;
    private ObjectTelemetryData _telemetryData;
    private Rigidbody _carRb;
    
    private float _currentPitch;
    private float _currentRoll;
    private float _targetPitch;
    private float _targetRoll;

    void Awake()
    {
        _sendingData = new SendingData();
        _telemetryData = _sendingData.ObjectTelemetryData;
    }

    void Start()
    {
        if (carController == null) 
            carController = GetComponent<MyCarController>();
        
        if (carController != null)
            _carRb = carController.GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        _sendingData.SendingStart();
        StartCoroutine(TelemetryHandler());
    }

    void OnDisable()
    {
        StopCoroutine(TelemetryHandler());
        _sendingData.SendingStop();
    }

    void FixedUpdate()
    {
        CalculateCustomMotionLogic();
    }

    private IEnumerator TelemetryHandler()
    {
        float waitTime = SendingData.WAIT_TIME / 1000f; 

        while (true)
        {
            if (_telemetryData == null)
            {
                yield return new WaitForSeconds(waitTime * 10f);
                continue;
            }

            _telemetryData.Angles = new Vector3(_currentPitch, 0, _currentRoll);

            if (_carRb != null)
            {
                _telemetryData.Velocity = _carRb.linearVelocity;
            }

            yield return new WaitForSeconds(waitTime);
        }
    }

    void CalculateCustomMotionLogic()
    {
        if (carController == null) return;

        Vector3 gForce = carController.LocalGForce;
        
        _targetPitch = gForce.z * pitchGain; 
        _targetRoll = -gForce.x * rollGain;

        float rpmPercent = Mathf.InverseLerp(carController.idleRPM, carController.redlineRPM, carController.CurrentRPM);
        float noise = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f);
        float totalVibro = idleVibration + (rpmPercent * rpmVibrationFactor);
        
        _targetPitch += noise * totalVibro;
        _targetRoll += noise * totalVibro;

        _targetPitch = Mathf.Clamp(_targetPitch, -maxAngle, maxAngle);
        _targetRoll = Mathf.Clamp(_targetRoll, -maxAngle, maxAngle);

        float dt = Time.fixedDeltaTime;
        _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, dt * smoothing);
        _currentRoll = Mathf.Lerp(_currentRoll, _targetRoll, dt * smoothing);
    }
}