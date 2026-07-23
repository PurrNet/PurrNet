using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    /// <summary>
    /// Draws authoring controls and a compact distance-band preview for a network LOD profile.
    /// </summary>
    public static class NetworkLODProfileEditorGUI
    {
        private const float SummaryHeight = 78f;
        private static GUIStyle _bandLabel;
        private static GUIStyle _boundaryLabel;

        private static GUIStyle bandLabel
        {
            get
            {
                if (_bandLabel != null)
                    return _bandLabel;

                _bandLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip
                };
                _bandLabel.normal.textColor = Color.white;
                return _bandLabel;
            }
        }

        private static GUIStyle boundaryLabel
        {
            get
            {
                if (_boundaryLabel != null)
                    return _boundaryLabel;

                _boundaryLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip
                };
                return _boundaryLabel;
            }
        }

        /// <summary>
        /// Draws a read-only visualization of the configured distance bands, cadence, and culling tail.
        /// </summary>
        public static void DrawSummary(NetworkLODProfile profile)
        {
            if (!profile)
            {
                EditorGUILayout.HelpBox("Assign a Network LOD Profile to preview its distance bands.",
                    MessageType.Info);
                return;
            }

            int count = profile.tierCount;
            if (count == 0)
            {
                EditorGUILayout.HelpBox("This profile has no tiers. Runtime resolution stays at tier 0.",
                    MessageType.Info);
                return;
            }

            Rect outer = EditorGUILayout.GetControlRect(false, SummaryHeight);
            outer = EditorGUI.IndentedRect(outer);
            GUI.Box(outer, GUIContent.none, EditorStyles.helpBox);

            Rect content = new Rect(outer.x + 7f, outer.y + 5f, outer.width - 14f, outer.height - 10f);
            var lastTier = profile.GetTier(count - 1);
            string tail = profile.cullBeyondLastTier
                ? "Tail: send-culled after the hold edge"
                : $"Tail: T{count - 1} continues";

            Rect titleRect = new Rect(content.x, content.y, content.width * 0.42f,
                EditorGUIUtility.singleLineHeight);
            Rect tailRect = new Rect(titleRect.xMax, content.y, content.width - titleRect.width,
                EditorGUIUtility.singleLineHeight);
            GUI.Label(titleRect, $"{count} distance tier{(count == 1 ? string.Empty : "s")}",
                EditorStyles.miniBoldLabel);
            GUI.Label(tailRect, tail, EditorStyles.miniLabel);

            float maxDistance = 0f;
            for (int i = 0; i < count; i++)
                maxDistance = Mathf.Max(maxDistance, profile.GetTier(i).maxDistance);
            maxDistance = Mathf.Max(maxDistance, 0.0001f);

            Rect bandRect = new Rect(content.x, titleRect.yMax + 3f, content.width, 27f);
            float previousDistance = 0f;

            for (int i = 0; i < count; i++)
            {
                var tier = profile.GetTier(i);
                float distance = Mathf.Max(0f, tier.maxDistance);
                float start = Mathf.Clamp01(previousDistance / maxDistance);
                float end = Mathf.Clamp01(distance / maxDistance);
                if (end < start)
                    end = start;

                Rect segment = new Rect(
                    bandRect.x + bandRect.width * start,
                    bandRect.y,
                    Mathf.Max(1f, bandRect.width * (end - start)),
                    bandRect.height);
                Color color = GetTierColor(i);
                EditorGUI.DrawRect(segment, color);

                string rate = tier.sendIntervalTicks <= 1
                    ? "every tick"
                    : $"every {tier.sendIntervalTicks} ticks";
                bool hasDemotionBoundary = i < count - 1 || profile.cullBeyondLastTier;
                string transition = hasDemotionBoundary
                    ? $"demotion edge {FormatDistance(distance + Mathf.Max(0f, tier.hysteresis))}"
                    : "last tier continues beyond its enter edge";
                string tooltip =
                    $"Tier {i}: {FormatDistance(previousDistance)}–{FormatDistance(distance)}; " +
                    $"send opportunity {rate}; {transition}.";
                string label = segment.width >= 70f
                    ? $"T{i} · {(tier.sendIntervalTicks <= 1 ? "1 tick" : $"{tier.sendIntervalTicks} ticks")}"
                    : $"T{i}";
                GUI.Label(segment, new GUIContent(label, tooltip), bandLabel);
                previousDistance = Mathf.Max(previousDistance, distance);
            }

            EditorGUI.DrawRect(new Rect(bandRect.x, bandRect.y, bandRect.width, 1f),
                new Color(0f, 0f, 0f, 0.45f));
            EditorGUI.DrawRect(new Rect(bandRect.x, bandRect.yMax - 1f, bandRect.width, 1f),
                new Color(0f, 0f, 0f, 0.45f));

            Rect boundaryRow = new Rect(content.x, bandRect.yMax + 1f, content.width,
                EditorGUIUtility.singleLineHeight);
            previousDistance = 0f;

            for (int i = 0; i < count; i++)
            {
                var tier = profile.GetTier(i);
                float distance = Mathf.Max(previousDistance, Mathf.Max(0f, tier.maxDistance));
                float start = Mathf.Clamp01(previousDistance / maxDistance);
                float end = Mathf.Clamp01(distance / maxDistance);
                Rect labelRect = new Rect(
                    boundaryRow.x + boundaryRow.width * start,
                    boundaryRow.y,
                    Mathf.Max(1f, boundaryRow.width * (end - start)),
                    boundaryRow.height);
                float demotionDistance = distance + Mathf.Max(0f, tier.hysteresis);
                bool hasDemotionBoundary = i < count - 1 || profile.cullBeyondLastTier;
                string boundary = tier.hysteresis > 0f && hasDemotionBoundary
                    ? $"{FormatDistance(distance)} / {FormatDistance(demotionDistance)}"
                    : FormatDistance(distance);
                string tooltip = hasDemotionBoundary
                    ? "Enter edge / outward demotion edge"
                    : "Enter edge; the last tier continues beyond it";
                GUI.Label(labelRect, new GUIContent(boundary, tooltip),
                    boundaryLabel);
                previousDistance = distance;
            }

            DrawValidation(profile);
        }

        /// <summary>
        /// Draws editable serialized fields for a referenced network LOD profile.
        /// </summary>
        public static void DrawInlineEditor(NetworkLODProfile profile)
        {
            if (!profile)
                return;

            using var profileObject = new SerializedObject(profile);
            profileObject.UpdateIfRequiredOrScript();
            DrawProperties(profileObject);
            if (profileObject.ApplyModifiedProperties())
                SceneView.RepaintAll();
        }

        internal static Color GetTierColor(int tier)
        {
            switch (tier)
            {
                case 0:
                    return new Color(0.18f, 0.64f, 0.38f, 0.9f);
                case 1:
                    return new Color(0.76f, 0.67f, 0.16f, 0.9f);
                case 2:
                    return new Color(0.86f, 0.43f, 0.12f, 0.9f);
                case 3:
                    return new Color(0.78f, 0.22f, 0.22f, 0.9f);
                default:
                    Color color = Color.HSVToRGB(Mathf.Repeat(0.92f - tier * 0.09f, 1f), 0.62f, 0.82f);
                    color.a = 0.9f;
                    return color;
            }
        }

        internal static string FormatDistance(float distance)
        {
            return $"{distance:0.##}m";
        }

        internal static string FormatSendOpportunity(int interval)
        {
            return interval <= 1
                ? "send opportunity every tick"
                : $"send opportunity every {interval} ticks";
        }

        internal static void DrawProperties(SerializedObject profileObject)
        {
            EditorGUILayout.PropertyField(profileObject.FindProperty("_tiers"), true);
            EditorGUILayout.PropertyField(profileObject.FindProperty("_cullBeyondLastTier"));
        }

        private static void DrawValidation(NetworkLODProfile profile)
        {
            int count = profile.tierCount;

            if (count > NetworkLODProfile.CulledTier)
            {
                EditorGUILayout.HelpBox(
                    $"{count} visible tiers exceed the byte tier range; tier 255 is reserved for send-culling.",
                    MessageType.Error);
            }

            float previous = -1f;
            for (int i = 0; i < count; i++)
            {
                var tier = profile.GetTier(i);
                if (tier.maxDistance <= previous)
                {
                    EditorGUILayout.HelpBox(
                        $"Tier {i} ends at {FormatDistance(tier.maxDistance)}. Distances must be strictly ascending.",
                        MessageType.Warning);
                    break;
                }

                previous = tier.maxDistance;
            }

            for (int i = 0; i < count; i++)
            {
                var tier = profile.GetTier(i);
                if (tier.maxDistance < 0f || tier.hysteresis < 0f || tier.sendIntervalTicks < 1)
                {
                    EditorGUILayout.HelpBox(
                        $"Tier {i} contains a negative distance/hysteresis or a send interval below one.",
                        MessageType.Warning);
                    break;
                }
            }
        }
    }

    [CustomEditor(typeof(NetworkLODProfile))]
    [CanEditMultipleObjects]
    internal sealed class NetworkLODProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (targets.Length != 1)
            {
                DrawDefaultInspector();
                return;
            }

            NetworkLODProfileEditorGUI.DrawSummary((NetworkLODProfile)target);
            EditorGUILayout.Space(4f);

            serializedObject.UpdateIfRequiredOrScript();
            NetworkLODProfileEditorGUI.DrawProperties(serializedObject);
            if (serializedObject.ApplyModifiedProperties())
                SceneView.RepaintAll();
        }
    }
}
