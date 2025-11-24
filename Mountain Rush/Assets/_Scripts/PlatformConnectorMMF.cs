using UnityEngine;
using _2DOF;

public class PlatformConnectorMMF : MonoBehaviour
{
    [Header("References")]
    public MyCarController carController;

    [Header("2DOF Motion Logic")]
    [Tooltip("Чувствительность наклона вперед/назад при разгоне/торможении")]
    public float pitchGain = 2.0f; 
    
    [Tooltip("Чувствительность наклона вбок при повороте")]
    public float rollGain = 2.5f;
    
    [Tooltip("Максимальный угол наклона (градусы)")]
    public float maxAngle = 15.0f;
    
    [Tooltip("Плавность движения (больше = плавнее)")]
    public float smoothing = 5.0f;

    [Header("Vibration")]
    public float idleVibration = 0.1f;
    public float rpmVibrationFactor = 0.0002f;

    [Header("Debug")]
    [Tooltip("Если включить, будет крутить объект. НЕ ВКЛЮЧАТЬ, ЕСЛИ СКРИПТ ВИСИТ НА МАШИНЕ!")]
    public bool visualizeMotion = false; 

    private SendingData _sender;
    
    private float _currentPitch;
    private float _currentRoll;
    private float _targetPitch;
    private float _targetRoll;

    void Start()
    {
        if (carController == null) 
            carController = GetComponent<MyCarController>();

        _sender = new SendingData();
        _sender.SendingStart();
        Debug.Log("2dof активно");
    }

    void OnApplicationQuit()
    {
        if (_sender != null) _sender.SendingStop();
    }

    void FixedUpdate()
    {
        if (carController == null) return;

        CalculateMotionLogic();
        UpdateTelemetry();
    }

    void CalculateMotionLogic()
    {
        Vector3 gForce = carController.LocalGForce;
        float rpm = carController.CurrentRPM;

        _targetPitch = -gForce.z * pitchGain;
        _targetRoll = -gForce.x * rollGain;

        float noise = (Mathf.PerlinNoise(Time.time * 20f, 0f) - 0.5f); 
        float intensity = idleVibration + (rpm * rpmVibrationFactor);
        
        _targetPitch += noise * intensity;
        _targetRoll += noise * intensity;

        _targetPitch = Mathf.Clamp(_targetPitch, -maxAngle, maxAngle);
        _targetRoll = Mathf.Clamp(_targetRoll, -maxAngle, maxAngle);

        float dt = Time.fixedDeltaTime;
        _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, dt * smoothing);
        _currentRoll = Mathf.Lerp(_currentRoll, _targetRoll, dt * smoothing);
    }

    void UpdateTelemetry()
    {
        _sender.ObjectTelemetryData.Angles = new Vector3(_currentPitch, 0, _currentRoll);
        
        if(carController.GetComponent<Rigidbody>() != null)
            _sender.ObjectTelemetryData.Velocity = carController.GetComponent<Rigidbody>().linearVelocity;
        
        if (visualizeMotion)
        {
            transform.localRotation = Quaternion.Euler(_currentPitch, 0, -_currentRoll);
        }
    }
}