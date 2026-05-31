using System;
using System.Collections.Generic;
using System.Reflection;
using PurrNet.StateMachine;
using UnityEngine;

internal static class StateMachineTestPrefabBuilder
{
    private static readonly FieldInfo StatesField = typeof(StateMachine).GetField(
        "_states",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo OwnerAuthField = typeof(StateMachine).GetField(
        "_ownerAuth",
        BindingFlags.Instance | BindingFlags.NonPublic);

    internal static StateMachineTestRig Create(string name, bool ownerAuth)
    {
        if (StatesField == null || OwnerAuthField == null)
            throw new MissingFieldException(nameof(StateMachine), "_states/_ownerAuth");

        var root = new GameObject(name);
        root.SetActive(false);

        var rig = root.AddComponent<StateMachineTestRig>();
        var machineRoot = new GameObject("Machine");
        machineRoot.transform.SetParent(root.transform);
        var machine = machineRoot.AddComponent<StateMachine>();

        var initialStates = new StateMachineTestNode[10];
        var states = new List<StateNode>(initialStates.Length);
        for (var i = 0; i < initialStates.Length; i++)
        {
            initialStates[i] = CreateNode(machineRoot.transform, i);
            states.Add(initialStates[i]);
        }

        var insertedState = CreateNode(machineRoot.transform, 100);
        var addedState = CreateNode(machineRoot.transform, 101);
        var removeByReferenceState = CreateNode(machineRoot.transform, 102);
        var payloadState = CreatePayloadNode(machineRoot.transform, 200);
        var blockedState = CreateNode(machineRoot.transform, 201, canEnter: false);

        OwnerAuthField.SetValue(machine, ownerAuth);
        StatesField.SetValue(machine, states);
        rig.Configure(
            name,
            machine,
            initialStates,
            insertedState,
            addedState,
            removeByReferenceState,
            payloadState,
            blockedState);

        return rig;
    }

    private static StateMachineTestNode CreateNode(Transform parent, int key, bool canEnter = true)
    {
        var go = new GameObject($"State {key}");
        go.transform.SetParent(parent);
        var node = go.AddComponent<StateMachineTestNode>();
        node.Configure(key, canEnter);
        return node;
    }

    private static StateMachinePayloadTestNode CreatePayloadNode(Transform parent, int key)
    {
        var go = new GameObject($"State {key}");
        go.transform.SetParent(parent);
        var node = go.AddComponent<StateMachinePayloadTestNode>();
        node.Configure(key);
        return node;
    }
}
