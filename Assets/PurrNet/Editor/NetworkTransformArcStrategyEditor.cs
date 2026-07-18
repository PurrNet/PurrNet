using UnityEditor;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(NetworkTransformArcStrategy), true)]
    public class NetworkTransformArcStrategyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Reconstructs skipped motion along locally fitted circular arcs. " +
                "This covers any motion with roughly constant curvature over one send interval: " +
                "orbits, turns, arches, projectile arcs and spline-like paths. " +
                "Straight motion is handled as well, falling back to linear interpolation.",
                MessageType.Info);

            DrawDefaultInspector();
        }
    }
}
