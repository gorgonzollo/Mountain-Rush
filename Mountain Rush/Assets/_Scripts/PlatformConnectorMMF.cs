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
    public bool invertPitch = false;
    public bool invertRoll = true;

    private SendingData _sendingData;
    private ObjectTelemetryData _telemetryData;
    private Rigidbody _carRb;

    private float _currentPitch;
    private float _currentRoll;
    private float _targetPitch;
    private float _targetRoll;

    private Coroutine _telemetryRoutine;

    void Awake()
    {
        _sendingData = new SendingData();
        _telemetryData = _sendingData.ObjectTelemetryData;
    }

    void Start()
    {
        if (!carController) carController = GetComponent<MyCarController>();
        if (carController) _carRb = carController.GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        _sendingData.SendingStart();
        _telemetryRoutine = StartCoroutine(TelemetryHandler());
    }

    void OnDisable()
    {
        if (_telemetryRoutine != null) StopCoroutine(_telemetryRoutine);
        _telemetryRoutine = null;
        _sendingData.SendingStop();
    }

    void OnApplicationQuit()
    {
        if (_telemetryRoutine != null) StopCoroutine(_telemetryRoutine);
        _telemetryRoutine = null;
        _sendingData.SendingStop();
    }

    void FixedUpdate()
    {
        if (!carController) return;

        Vector3 g = carController.LocalGForce;

        float pSign = invertPitch ? -1f : 1f;
        float rSign = invertRoll ? -1f : 1f;

        _targetPitch = Mathf.Clamp(g.z * pitchGain * pSign, -maxAngle, maxAngle);
        _targetRoll = Mathf.Clamp(g.x * rollGain * rSign, -maxAngle, maxAngle);

        float rpmT = Mathf.InverseLerp(carController.idleRPM, carController.redlineRPM, carController.CurrentRPM);
        float noise = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * (idleVibration + rpmT * rpmVibrationFactor);

        _targetPitch += noise;
        _targetRoll += noise;

        float dt = Time.fixedDeltaTime;
        _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, dt * smoothing);
        _currentRoll = Mathf.Lerp(_currentRoll, _targetRoll, dt * smoothing);
    }

    IEnumerator TelemetryHandler()
    {
        float waitTime = SendingData.WAIT_TIME / 1000f;
        var wait = new WaitForSeconds(waitTime);
        while (true)
        {
            if (_telemetryData != null)
            {
                _telemetryData.Angles = new Vector3(_currentPitch, 0f, _currentRoll);
                _telemetryData.Velocity = _carRb ? _carRb.linearVelocity : Vector3.zero;
            }
            yield return wait;
        }
    }
}