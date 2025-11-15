using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RaceManager : MonoBehaviour
{
    public MyCarController playerCar;
    public Transform checkpointsRoot;
    public Text countdownText;
    public Text timerText;
    public bool autoStart = true;

    Rigidbody _rb;
    readonly List<Checkpoint> _checkpoints = new List<Checkpoint>();
    int _expectedIndex;
    bool _running;
    bool _finished;
    float _startTime;
    float _elapsed;

    void Awake()
    {
        if (!playerCar) { enabled = false; return; }
        _rb = playerCar.GetComponent<Rigidbody>();
        if (!_rb) { enabled = false; return; }

        BuildCheckpointList();
        _expectedIndex = 0;
        DeactivateAll();
        if (_checkpoints.Count > 0) ActivateOnly(_expectedIndex);

        FreezeCar(true);
        UpdateTimerUI(0f);
        SetCountdown(string.Empty);

        if (autoStart) StartCoroutine(StartSequence());
    }

    void OnDisable()
    {
        for (int i = 0; i < _checkpoints.Count; i++)
            if (_checkpoints[i] != null) _checkpoints[i].Passed -= OnCheckpointPassed;
    }

    void Update()
    {
        if (!_running) return;
        _elapsed = Time.time - _startTime;
        UpdateTimerUI(_elapsed);
    }

    void BuildCheckpointList()
    {
        _checkpoints.Clear();
        if (!checkpointsRoot) return;

        for (int i = 0; i < checkpointsRoot.childCount; i++)
        {
            var t = checkpointsRoot.GetChild(i);
            var col = t.GetComponent<Collider>();
            if (!col) continue;
            col.isTrigger = true;

            var cp = t.GetComponent<Checkpoint>();
            if (!cp) cp = t.gameObject.AddComponent<Checkpoint>();
            cp.index = i;
            cp.Passed -= OnCheckpointPassed;
            cp.Passed += OnCheckpointPassed;
            cp.SetActive(false);
            _checkpoints.Add(cp);
        }
    }

    void DeactivateAll()
    {
        for (int i = 0; i < _checkpoints.Count; i++)
            if (_checkpoints[i]) _checkpoints[i].SetActive(false);
    }

    void ActivateOnly(int index)
    {
        for (int i = 0; i < _checkpoints.Count; i++)
            if (_checkpoints[i]) _checkpoints[i].SetActive(i == index);
    }

    IEnumerator StartSequence()
    {
        SetCountdown("3"); yield return new WaitForSeconds(1f);
        SetCountdown("2"); yield return new WaitForSeconds(1f);
        SetCountdown("1"); yield return new WaitForSeconds(1f);
        SetCountdown("GO!");

        _startTime = Time.time;
        _running = true;
        _finished = false;
        FreezeCar(false);

        yield return new WaitForSeconds(0.6f);
        SetCountdown(string.Empty);
    }

    void SetCountdown(string s)
    {
        if (countdownText) countdownText.text = s;
    }

    void UpdateTimerUI(float timeSec)
    {
        if (!timerText) return;
        if (timeSec < 0f) timeSec = 0f;
        int m = (int)(timeSec / 60f);
        int s = (int)(timeSec % 60f);
        int ms = (int)((timeSec - Mathf.Floor(timeSec)) * 1000f);
        timerText.text = $"{m:00}:{s:00}.{ms:000}";
    }

    void FreezeCar(bool freeze)
    {
        playerCar.enabled = !freeze;
        if (freeze)
        {
            if (_rb.isKinematic == false)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
        }
        else
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    void OnCheckpointPassed(Checkpoint cp, MyCarController car)
    {
        if (!_running || _finished) return;
        if (car != playerCar || cp == null) return;
        if (cp.index != _expectedIndex) return;

        cp.Collect();

        bool isLast = (_expectedIndex >= _checkpoints.Count - 1);
        if (isLast) { FinishRace(); return; }

        _expectedIndex++;
        ActivateOnly(_expectedIndex);
    }

    void FinishRace()
    {
        _running = false;
        _finished = true;
        SetCountdown("Finish!");
        UpdateTimerUI(_elapsed);
    }

    public void ManualStart()
    {
        if (_running) return;
        StopAllCoroutines();
        StartCoroutine(StartSequence());
    }
}