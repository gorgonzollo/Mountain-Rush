using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Bhaptics.SDK2;

public class BhapticsStatusHUD : MonoBehaviour
{
    public bool visible = true;
    public int fontSize = 16;

    void Start()
    {
        Debug.Log("[bHaptics HUD] Ready");
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.f1Key.wasPressedThisFrame) visible = !visible;
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
        }
    }

    void OnGUI()
    {
        if (!visible) return;

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = fontSize
        };
        style.normal.textColor = Color.cyan;

        Rect r = new Rect(10, 10, 620, 360);
        GUI.Box(r, BuildText(), style);
    }

    string BuildText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"bHaptics Init: {BhapticsSDK2.IsInitialized}");
        var devs = BhapticsLibrary.GetDevices();
        sb.AppendLine($"Devices: {devs.Count}");
        for (int i = 0; i < devs.Count; i++)
        {
            var d = devs[i];
            sb.AppendLine($"[{i}] {d.DeviceName} | Pos={d.Position} | Connected={d.IsConnected}");
        }
        return sb.ToString();
    }
}