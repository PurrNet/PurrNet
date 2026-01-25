using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class Test : NetworkIdentity
{
    private SyncTimer _syncTimer = new();

    private SyncEvent _syncEvent = new();

    [SerializeField] private Transform _parentTarget;
    [SerializeField] private SyncDictionary<int, TestClass> _myTestDictionary = new();

    protected override void OnSpawned()
    {
        base.OnSpawned();
        if (isServer)
        {
            _myTestDictionary.Add(1, new TestClass() { myStats = 20 });
            _myTestDictionary.Add(3, new TestClass() { myStats = 30 });
            _myTestDictionary.Add(69, new TestClass() { myStats = 420 });
        }
    }

    private void OnEnable()
    {
        _syncTimer.onTimerStart += () => Debug.Log($"Timer started");
        _syncTimer.onTimerEnd += () => Debug.Log($"Timer ended");
        _syncTimer.onTimerSecondTick += () => Debug.Log($"Timer: {_syncTimer.remainingInt}");
        _syncEvent += SyncEventTest;
    }

    private void OnDisable()
    {
        _syncEvent -= SyncEventTest;
    }

    private void SyncEventTest()
    {
        Debug.Log($"SyncEvent: Invoked");
    }

    [ContextMenu("Start Timer")]
    private void StartTimer()
    {
        _syncTimer.StartTimer(20);
    }

    [ContextMenu("End Timer")]
    private void EndTimer()
    {
        _syncTimer.StopTimer();
    }

    [ContextMenu("Invoke")]
    private void InvokeTest()
    {
        _syncEvent?.Invoke();
    }

    [ContextMenu("Parent test")]
    private void ParentTest()
    {
        transform.SetParent(_parentTarget);
        transform.localPosition = Vector3.zero;
    }

    [Serializable]
    public class TestClass
    {
        public int myStats;
        public string moreInfo = "This is just some default";
        public bool isBobsiTheGreatest = true;
    }
}
