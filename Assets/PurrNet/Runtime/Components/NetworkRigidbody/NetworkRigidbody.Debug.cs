using UnityEngine;

namespace PurrNet
{
    public partial class NetworkRigidbody
    {
        [Header("Debug")]
        [SerializeField] private bool _debugGizmos;
        [SerializeField] private float _debugTextOffset = 2f;

        private Vector3 _prePredictionTarget;

        private void OnDrawGizmos()
        {
            if (!_debugGizmos || _rigidbody == null)
                return;

            bool isController = isSpawned && IsController(_ownerAuth);

            Gizmos.color = isController ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(_rigidbody.position, 0.2f);

            if (!isController && isSpawned)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
                Gizmos.DrawWireSphere(_latestRawSnapshotPos, 0.1f);

                Gizmos.color = Color.red;
                Gizmos.DrawLine(_rigidbody.position, _targetPosition);

                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.matrix = Matrix4x4.TRS(_targetPosition, NormalizeQuaternion(_targetRotation), Vector3.one * 0.3f);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                Gizmos.matrix = Matrix4x4.identity;
            }

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_rigidbody.position, GetLinearVelocity() * 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            if (_rigidbody == null || !isSpawned || IsController(_ownerAuth))
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_prePredictionTarget, 0.15f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_targetPosition, 0.15f);

            if (_predictionFactor > 0f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(_prePredictionTarget, _targetPosition);
            }
        }

        private void OnGUI()
        {
            if (!_debugGizmos || !isSpawned || _rigidbody == null)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector3 worldPos = _rigidbody.position + Vector3.up * _debugTextOffset;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0)
                return;

            screenPos.y = Screen.height - screenPos.y;

            bool isController = IsController(_ownerAuth);
            float posError = Vector3.Distance(_rigidbody.position, _targetPosition);
            float rotError = Quaternion.Angle(_rigidbody.rotation, NormalizeQuaternion(_targetRotation));
            float velocityMagnitude = GetLinearVelocity().magnitude;
            float angVelMagnitude = _rigidbody.angularVelocity.magnitude;

            double bufferSpan = 0;
            if (_bufferCount >= 2)
            {
                var oldest = GetSnapshot(0);
                var newest = GetSnapshot(_bufferCount - 1);
                bufferSpan = newest.time - oldest.time;
            }

            float ratio = 0f;
            if (!isController)
            {
                float range = Mathf.Max(_correctionRange, 0.01f);
                ratio = Mathf.Clamp01(posError / range);
            }

            string info = $"<b>NetworkRigidbody</b>\n" +
                          $"Server: {isServer} | Controller: {isController}\n" +
                          $"OwnerAuth: {_ownerAuth}\n" +
                          $"Owner: {(owner.HasValue ? owner.Value.ToString() : "none")}\n" +
                          $"---\n" +
                          $"Pos Error: {posError:F3}m\n" +
                          $"Rot Error: {rotError:F1}deg\n" +
                          $"Ratio: {ratio:F3}\n" +
                          $"Velocity: {velocityMagnitude:F2}\n" +
                          $"Correcting: {(isController ? "-" : _lastCorrectionReason)}\n" +
                          $"---\n" +
                          $"Buffer: {_bufferCount}/{BUFFER_SIZE}\n" +
                          $"Sample: {_bufferSampleMode}\n" +
                          $"Span: {bufferSpan:F3}s\n" +
                          $"Delay: {_interpolationDelay:F3}s\n" +
                          $"Predict: {_predictionFactor:F2}\n" +
                          $"PredOffset: {_predictionOffset:F3}m";

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white },
                richText = true
            };

            Vector2 size = style.CalcSize(new GUIContent(info));
            Rect bgRect = new Rect(screenPos.x - size.x / 2 - 5, screenPos.y, size.x + 10, size.y + 10);

            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(screenPos.x - size.x / 2, screenPos.y + 5, size.x, size.y), info, style);
        }
    }
}
