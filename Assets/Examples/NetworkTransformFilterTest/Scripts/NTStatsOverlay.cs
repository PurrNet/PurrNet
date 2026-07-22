using PurrNet.Modules;
using UnityEngine;

namespace NetworkTransformFilterTest
{
    public class NTStatsOverlay : MonoBehaviour
    {
        private long _lastWritten;
        private long _lastHolds;
        private float _lastSampleTime;
        private float _writtenPerSec;
        private float _holdsPerSec;

        private void Update()
        {
            float elapsed = Time.unscaledTime - _lastSampleTime;
            if (elapsed < 1f)
                return;

            _writtenPerSec = (NetworkTransformModule.entriesWrittenCount - _lastWritten) / elapsed;
            _holdsPerSec = (NetworkTransformModule.adaptiveHoldCount - _lastHolds) / elapsed;

            _lastWritten = NetworkTransformModule.entriesWrittenCount;
            _lastHolds = NetworkTransformModule.adaptiveHoldCount;
            _lastSampleTime = Time.unscaledTime;
        }

        private void OnGUI()
        {
            float total = _writtenPerSec + _holdsPerSec;
            float suppression = total > 0f ? _holdsPerSec / total * 100f : 0f;

            GUILayout.BeginArea(new Rect(10, 10, 320, 90), GUI.skin.box);
            GUILayout.Label($"NT entries written/s: {_writtenPerSec:F0}");
            GUILayout.Label($"NT holds/s: {_holdsPerSec:F0}");
            GUILayout.Label($"Suppression: {suppression:F1}%");
            GUILayout.EndArea();
        }
    }
}
