using UnityEngine;
using UnityEngine.InputSystem;
using Bhaptics.SDK2;

public class BhapticsManualTrigger : MonoBehaviour
{
    public int pulseMs = 180;
    public float intensity = 1f;
    public float angleX = 0f;
    public float offsetY = 0f;

    public string accel = "accel";
    public string brake = "brake";
    public string turnLeft = "turn_left";
    public string turnRight = "turn_right";
    public string drift = "drift";
    public string shiftUp = "shift_up";
    public string shiftDown = "shift_down";

    void Update()
    {
        if (!BhapticsSDK2.IsInitialized) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Play(accel);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) Play(brake);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) Play(turnLeft);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) Play(turnRight);
            if (Keyboard.current.digit5Key.wasPressedThisFrame) Play(drift);
            if (Keyboard.current.digit6Key.wasPressedThisFrame) Play(shiftUp);
            if (Keyboard.current.digit7Key.wasPressedThisFrame) Play(shiftDown);
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) Play(accel);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Play(brake);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Play(turnLeft);
            if (Input.GetKeyDown(KeyCode.Alpha4)) Play(turnRight);
            if (Input.GetKeyDown(KeyCode.Alpha5)) Play(drift);
            if (Input.GetKeyDown(KeyCode.Alpha6)) Play(shiftUp);
            if (Input.GetKeyDown(KeyCode.Alpha7)) Play(shiftDown);
        }
    }

    void Play(string id)
    {
        BhapticsLibrary.Play(id, 0, Mathf.Clamp01(intensity), Mathf.Max(0.01f, pulseMs * 0.001f), angleX, offsetY);
        Debug.Log($"[bHaptics] Play {id}");
    }
}