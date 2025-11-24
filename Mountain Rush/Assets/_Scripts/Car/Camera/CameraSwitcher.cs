using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraSwitcher : MonoBehaviour
{
    [Header("Камеры для переключения (GameObject-ы с компонентом Camera)")]
    [SerializeField] private GameObject[] cameraObjects;

    [Header("Управление")]
    public Key switchKey = Key.C;

    private int activeIndex = 0;

    void Reset()
    {
        AutoFillFromChildren();
    }

    void Awake()
    {
        if (cameraObjects == null || cameraObjects.Length == 0)
            AutoFillFromChildren();

        int firstActive = GetFirstActiveIndex();
        activeIndex = firstActive >= 0 ? firstActive : 0;

        ApplyActiveIndex();
    }

    void Update()
    {
        if (WasSwitchPressedThisFrame())
            NextCamera();
    }

    public void NextCamera()
    {
        if (cameraObjects == null || cameraObjects.Length == 0) return;

        int tries = 0;
        do
        {
            activeIndex = (activeIndex + 1) % cameraObjects.Length;
            tries++;
        } while ((cameraObjects[activeIndex] == null) && tries < cameraObjects.Length);

        ApplyActiveIndex();
    }

    private void ApplyActiveIndex()
    {
        if (cameraObjects == null || cameraObjects.Length == 0) return;

        for (int i = 0; i < cameraObjects.Length; i++)
        {
            var go = cameraObjects[i];
            if (!go) continue;

            bool active = (i == activeIndex);

            if (go.activeSelf != active)
                go.SetActive(active);

            var listeners = go.GetComponentsInChildren<AudioListener>(true);
            foreach (var al in listeners)
                al.enabled = active;
        }
    }

    private bool WasSwitchPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            return Keyboard.current[switchKey].wasPressedThisFrame;
#endif
        return false;
    }

    private void AutoFillFromChildren()
    {
        var cams = GetComponentsInChildren<Camera>(true);
        cameraObjects = new GameObject[cams.Length];
        for (int i = 0; i < cams.Length; i++)
            cameraObjects[i] = cams[i].gameObject;
    }

    private int GetFirstActiveIndex()
    {
        for (int i = 0; i < cameraObjects.Length; i++)
        {
            var go = cameraObjects[i];
            if (go != null && go.activeInHierarchy)
                return i;
        }
        return -1;
    }
}