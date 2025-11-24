using System.Collections;
using UnityEngine;
using _2DOF;

public class CarTelemetryHandler : MonoBehaviour
{
    private const float WAIT_TIME = SendingData.WAIT_TIME / 1000f;

    [SerializeField] private Transform vehicleTransform;
    [SerializeField] private Rigidbody rb;

    private ObjectTelemetryData _telemetry;
    private SendingData _sender;

    void Awake()
    {
        _sender = new SendingData();
        _telemetry = _sender.ObjectTelemetryData;
    }

    void OnEnable()
    {
        StartCoroutine(nameof(TelemetryLoop));
        _sender.SendingStart();
    }

    void OnDisable()
    {
        StopCoroutine(nameof(TelemetryLoop));
        _sender.SendingStop();
    }

    IEnumerator TelemetryLoop()
    {
        var wait = new WaitForSeconds(WAIT_TIME);
        while (true)
        {
            if (_telemetry == null) { yield return wait; continue; }
            UpdateAngles();
            UpdateVelocity();
            yield return wait;
        }
    }

    void UpdateVelocity()
    {
        if (rb != null) _telemetry.Velocity = rb.linearVelocity;
    }

    void UpdateAngles()
    {
        if (vehicleTransform == null) return;
        Vector3 e = vehicleTransform.rotation.eulerAngles;
        e.x = e.x > 180f ? e.x - 360f : e.x;
        e.y = e.y > 180f ? e.y - 360f : e.y;
        e.z = e.z > 180f ? e.z - 360f : e.z;
        _telemetry.Angles = e;
    }
}