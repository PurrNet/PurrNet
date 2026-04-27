using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomPropertyDrawer(typeof(NetworkSimulation))]
    public class NetworkSimulationDrawer : PropertyDrawer
    {
        private bool _foldout;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!_foldout)
                return EditorGUIUtility.singleLineHeight;

            var includeInBuild = property.FindPropertyRelative("includeInBuild");
            var simulateLatency = property.FindPropertyRelative("simulateLatency");
            var simulatePacketLoss = property.FindPropertyRelative("simulatePacketLoss");

            // foldout + help box + includeInBuild + latency toggle
            float lines = 4;

            if (simulateLatency.boolValue)
                lines += 2; // min + max

            lines += 1; // packet loss toggle

            if (simulatePacketLoss.boolValue)
                lines += 1; // chance slider

            if (includeInBuild.boolValue)
                lines += 2.5f; // warning box

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            // help box is ~2.5 lines tall
            return (lines + 1.5f) * (EditorGUIUtility.singleLineHeight + spacing) + spacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var rect = new Rect(position.x, position.y, position.width, lineHeight);

            _foldout = EditorGUI.Foldout(rect, _foldout, label, true, EditorStyles.foldoutHeader);

            if (_foldout)
            {
                EditorGUI.indentLevel++;

                rect.y += lineHeight + spacing;

                // info help box
                var helpRect = new Rect(rect.x, rect.y, rect.width, lineHeight * 2.5f);
                EditorGUI.HelpBox(helpRect,
                    "For more accurate results, consider using Clumsy (Windows) or connecting to a relay in a distant region.",
                    MessageType.Info);
                rect.y += lineHeight * 2.5f + spacing;

                var includeInBuild = property.FindPropertyRelative("includeInBuild");
                var simulateLatency = property.FindPropertyRelative("simulateLatency");
                var minLatency = property.FindPropertyRelative("minLatency");
                var maxLatency = property.FindPropertyRelative("maxLatency");
                var simulatePacketLoss = property.FindPropertyRelative("simulatePacketLoss");
                var packetLossChance = property.FindPropertyRelative("packetLossChance");

                // include in build
                rect.height = lineHeight;
                EditorGUI.PropertyField(rect, includeInBuild, new GUIContent("Include In Build"));
                rect.y += lineHeight + spacing;

                if (includeInBuild.boolValue)
                {
                    var warnRect = new Rect(rect.x, rect.y, rect.width, lineHeight * 2.5f);
                    EditorGUI.HelpBox(warnRect,
                        "Simulation will be active in builds! Make sure to disable this before shipping.",
                        MessageType.Warning);
                    rect.y += lineHeight * 2.5f + spacing;
                }

                // latency
                EditorGUI.PropertyField(rect, simulateLatency, new GUIContent("Simulate Latency"));
                rect.y += lineHeight + spacing;

                if (simulateLatency.boolValue)
                {
                    EditorGUI.PropertyField(rect, minLatency, new GUIContent("Min Latency (ms)"));
                    rect.y += lineHeight + spacing;

                    EditorGUI.PropertyField(rect, maxLatency, new GUIContent("Max Latency (ms)"));
                    rect.y += lineHeight + spacing;
                }

                // packet loss
                EditorGUI.PropertyField(rect, simulatePacketLoss, new GUIContent("Simulate Packet Loss"));
                rect.y += lineHeight + spacing;

                if (simulatePacketLoss.boolValue)
                {
                    EditorGUI.IntSlider(rect, packetLossChance, 1, 100, new GUIContent("Packet Loss (%)"));
                    rect.y += lineHeight + spacing;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }
}
