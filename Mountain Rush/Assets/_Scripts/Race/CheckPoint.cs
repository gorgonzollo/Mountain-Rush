using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    public int index;
    public AudioClip collectSfx;
    public float collectSfxVolume = 1f;
    public AudioSource sfxSource;

    public event Action<Checkpoint, MyCarController> Passed;

    Collider _col;
    Renderer[] _renderers;
    bool _collected;
    bool _active = true;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_active || _collected) return;
        var car = other.GetComponentInParent<MyCarController>();
        if (car != null) Passed?.Invoke(this, car);
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (_col) _col.enabled = active;
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i]) _renderers[i].enabled = active;
    }

    public void Collect()
    {
        if (_collected) return;
        _collected = true;

        if (collectSfx)
        {
            float vol = Mathf.Clamp01(collectSfxVolume);
            if (sfxSource) sfxSource.PlayOneShot(collectSfx, vol);
            else AudioSource.PlayClipAtPoint(collectSfx, transform.position, vol);
        }

        SetActive(false);
    }
}