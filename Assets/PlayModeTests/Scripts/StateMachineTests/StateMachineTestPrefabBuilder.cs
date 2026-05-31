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

    internal static StateMachine Create(string name, bool ownerAuth)
    {
        if (StatesField == null || OwnerAuthField == null)
            throw new MissingFieldException(nameof(StateMachine), "_states/_ownerAuth");

        var root = new GameObject(name);
        var machine = root.AddComponent<StateMachine>();
        var rig = root.AddComponent<StateMachineTestRig>();

        var initialStates = new StateMachineTestNode[10];
        var states = new List<StateNode>(initialStates.Length);
        for (var i = 0; i < initialStates.Length; i++)
        {
            initialStates[i] = CreateNode(root.transform, i);
            states.Add(initialStates[i]);
        }

        var insertedState = CreateNode(root.transform, 100);
        var addedState = CreateNode(root.transform, 101);

        OwnerAuthField.SetValue(machine, ownerAuth);
        StatesField.SetValue(machine, states);
        rig.Configure(machine, initialStates, insertedState, addedState);

        root.SetActive(false);
        return machine;
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
