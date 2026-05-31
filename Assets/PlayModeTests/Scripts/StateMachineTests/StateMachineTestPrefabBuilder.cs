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

    internal static StateMachineTestIdentity Create(string name, bool ownerAuth)
    {
        if (StatesField == null || OwnerAuthField == null)
            throw new MissingFieldException(nameof(StateMachine), "_states/_ownerAuth");

        var root = new GameObject(name);
        var identity = root.AddComponent<StateMachineTestIdentity>();

        var machineObject = new GameObject("StateMachine");
        machineObject.transform.SetParent(root.transform);
        var machine = machineObject.AddComponent<StateMachine>();

        var initialStates = new StateMachineTestNode[10];
        var states = new List<StateNode>(initialStates.Length);
        for (var i = 0; i < initialStates.Length; i++)
        {
            initialStates[i] = CreateNode(machineObject.transform, i);
            states.Add(initialStates[i]);
        }

        var insertedState = CreateNode(machineObject.transform, 100);
        var addedState = CreateNode(machineObject.transform, 101);

        OwnerAuthField.SetValue(machine, ownerAuth);
        StatesField.SetValue(machine, states);
        identity.Configure(machine, initialStates, insertedState, addedState);

        root.SetActive(false);
        return identity;
    }

    private static StateMachineTestNode CreateNode(Transform parent, int key)
    {
        var go = new GameObject($"State {key}");
        go.transform.SetParent(parent);
        var node = go.AddComponent<StateMachineTestNode>();
        node.Configure(key);
        return node;
    }
}
