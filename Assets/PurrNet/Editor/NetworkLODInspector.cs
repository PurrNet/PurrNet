using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(NetworkLOD))]
    [CanEditMultipleObjects]
    internal sealed class NetworkLODInspector : NetworkIdentityInspector
    {
        private bool _showInlineProfile;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (targets.Length != 1 || target is not NetworkLOD networkLOD || !networkLOD.profile)
                return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("LOD Effect", EditorStyles.boldLabel);
            NetworkLODProfileEditorGUI.DrawSummary(networkLOD.profile);

            _showInlineProfile = EditorGUILayout.Foldout(
                _showInlineProfile,
                "Edit Profile Inline",
                true);

            if (!_showInlineProfile)
                return;

            EditorGUI.indentLevel++;
            NetworkLODProfileEditorGUI.DrawInlineEditor(networkLOD.profile);
            EditorGUI.indentLevel--;
        }

        [DrawGizmo(GizmoType.Selected)]
        private static void DrawDistanceBands(NetworkLOD networkLOD, GizmoType gizmoType)
        {
            if (!networkLOD || !networkLOD.profile)
                return;

            NetworkLODProfile profile = networkLOD.profile;
            int count = profile.tierCount;
            Vector3 center = networkLOD.transform.position;

            for (int i = 0; i < count; i++)
            {
                NetworkLODTier tier = profile.GetTier(i);
                float enterRadius = Mathf.Max(0f, tier.maxDistance);
                Color color = NetworkLODProfileEditorGUI.GetTierColor(i);
                Gizmos.color = color;
                Gizmos.DrawWireSphere(center, enterRadius);

                bool hasDemotionBoundary =
                    tier.hysteresis > 0f &&
                    (i < count - 1 || profile.cullBeyondLastTier);
                float demotionRadius = enterRadius + Mathf.Max(0f, tier.hysteresis);

                if (hasDemotionBoundary)
                {
                    Color demotionColor = color;
                    demotionColor.a = 0.28f;
                    Gizmos.color = demotionColor;
                    Gizmos.DrawWireSphere(center, demotionRadius);
                }

                string transition;
                if (i < count - 1)
                    transition = $"demote after {NetworkLODProfileEditorGUI.FormatDistance(demotionRadius)}";
                else if (profile.cullBeyondLastTier)
                    transition = $"send-cull after {NetworkLODProfileEditorGUI.FormatDistance(demotionRadius)}";
                else
                    transition = "continues beyond this edge";

                string label =
                    $"T{i}: enter ≤ {NetworkLODProfileEditorGUI.FormatDistance(enterRadius)} · " +
                    $"{NetworkLODProfileEditorGUI.FormatSendOpportunity(tier.sendIntervalTicks)} · {transition}";
                Handles.Label(center + Vector3.right * enterRadius, label);
            }

            if (count > 0)
                Handles.Label(center, "Closest player anchor decides distance · ownership stays T0");
        }
    }
}
