using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class Test : NetworkIdentity
{
    private SyncTimer _syncTimer = new();

    private SyncEvent _syncEvent = new();
    
    [SerializeField] private Transform _parentTarget;
    
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
}
