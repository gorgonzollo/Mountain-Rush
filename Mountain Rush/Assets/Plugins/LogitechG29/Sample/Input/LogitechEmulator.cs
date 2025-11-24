using UnityEngine;
using LogitechG29.Sample.Input;

public class LogitechEmulator : MonoBehaviour
{

    public InputControllerReader targetReader;

    [Range(-1f, 1f)] public float TestSteer;
    [Range(0f, 1f)] public float TestGas;
    [Range(0f, 1f)] public float TestBrake;
    public bool ClickGearUp;
    public bool ClickGearDown;

    void Update()
    {
        if (targetReader == null) return;

        targetReader.TEST_SimulateInput(TestSteer, TestGas, TestBrake);

        if (ClickGearUp)
        {
            targetReader.TEST_SimulateGearUp();
            ClickGearUp = false;
        }

        if (ClickGearDown)
        {
            targetReader.TEST_SimulateGearDown();
            ClickGearDown = false;
        }
    }
}