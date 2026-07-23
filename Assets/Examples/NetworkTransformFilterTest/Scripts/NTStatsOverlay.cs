using PurrNet;
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
        private NetworkTransform[] _transforms = System.Array.Empty<NetworkTransform>();
        private float _minBehindData = float.MaxValue;
        private float _lastMinBehindData;

        private void Update()
        {
            foreach (var nt in _transforms)
            {
                if (nt && !nt.isController && nt.adaptiveDebugTicksBehindData < _minBehindData)
                    _minBehindData = nt.adaptiveDebugTicksBehindData;
            }

            float elapsed = Time.unscaledTime - _lastSampleTime;
            if (elapsed < 1f)
                return;

            _writtenPerSec = (NetworkTransformModule.entriesWrittenCount - _lastWritten) / elapsed;
            _holdsPerSec = (NetworkTransformModule.adaptiveHoldCount - _lastHolds) / elapsed;

            _lastWritten = NetworkTransformModule.entriesWrittenCount;
            _lastHolds = NetworkTransformModule.adaptiveHoldCount;
            _lastSampleTime = Time.unscaledTime;
            _transforms = FindObjectsByType<NetworkTransform>(FindObjectsSortMode.None);
            _lastMinBehindData = _minBehindData;
            _minBehindData = float.MaxValue;
        }

        private void OnGUI()
        {
            float total = _writtenPerSec + _holdsPerSec;
            float suppression = total > 0f ? _holdsPerSec / total * 100f : 0f;

            GUILayout.BeginArea(new Rect(10, 10, 460, 220), GUI.skin.box);
            GUILayout.Label($"NT entries written/s: {_writtenPerSec:F0}");
            GUILayout.Label($"NT holds/s: {_holdsPerSec:F0}");
            GUILayout.Label($"Suppression: {suppression:F1}%");

            foreach (var nt in _transforms)
            {
                if (!nt || nt.isController)
                    continue;

                GUILayout.Label(
                    $"{nt.name}: rel {nt.adaptiveDebugRenderRel:F1} target {nt.adaptiveDebugTargetRel:F1} " +
                    $"behindData {nt.adaptiveDebugTicksBehindData:F1} (min1s {_lastMinBehindData:F1}) " +
                    $"vouch {(nt.adaptiveDebugHasVouch ? "Y" : "N")} " +
                    $"extrap {(nt.adaptiveDebugExtrapolating ? "Y" : "N")} corr {nt.adaptiveDebugCorrWeight:F2}");
            }

            GUILayout.EndArea();
        }
    }
}
