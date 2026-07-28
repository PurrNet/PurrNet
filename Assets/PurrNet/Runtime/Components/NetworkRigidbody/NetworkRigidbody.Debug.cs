using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public partial class NetworkRigidbody
    {
        [Header("Debug")]
        [SerializeField] private bool _debugGizmos;
        [SerializeField] private float _debugTextOffset = 2f;

        private double3 _prePredictionTarget;

        private void OnDrawGizmos()
        {
            if (!_debugGizmos || _rigidbody == null)
                return;

            bool amIController = isSpawned && IsController(_ownerAuth);

            Gizmos.color = amIController ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(_rigidbody.position, 0.2f);

            if (!amIController && isSpawned)
            {
                Vector3 worldTargetPos = ToWorldPosition(_targetPosition, _targetParent);
                Quaternion worldTargetRot = ToWorldRotation(_targetRotation, _targetParent);

                Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
                Gizmos.DrawWireSphere(ToWorldPosition(_latestRawSnapshotPos, _latestRawSnapshotParent), 0.1f);

                Gizmos.color = Color.red;
                Gizmos.DrawLine(_rigidbody.position, worldTargetPos);

                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.matrix = Matrix4x4.TRS(worldTargetPos, NormalizeQuaternion(worldTargetRot), Vector3.one * 0.3f);
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

            Vector3 worldPrePred = ToWorldPosition(_prePredictionTarget, _targetParent);
            Vector3 worldTargetPos = ToWorldPosition(_targetPosition, _targetParent);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(worldPrePred, 0.15f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(worldTargetPos, 0.15f);

            if (_predictionFactor > 0f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(worldPrePred, worldTargetPos);
            }
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            PurrOnGUI.Subscribe(DrawDebugGUI);
        }

        private void OnDisable()
        {
            PurrOnGUI.Unsubscribe(DrawDebugGUI);
        }

        private void DrawDebugGUI()
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

            bool amIController = IsController(_ownerAuth);
            Vector3 worldTargetPos = ToWorldPosition(_targetPosition, _targetParent);
            float posError = GetPositionError(worldTargetPos);
            float rotError = Quaternion.Angle(_rigidbody.rotation, NormalizeQuaternion(ToWorldRotation(_targetRotation, _targetParent)));
            float velocityMagnitude = GetLinearVelocity().magnitude;

            double bufferSpan = 0;
            if (_bufferCount >= 2)
            {
                var oldest = GetSnapshot(0);
                var newest = GetSnapshot(_bufferCount - 1);
                bufferSpan = newest.time - oldest.time;
            }

            float ratio = 0f;
            if (!amIController)
            {
                float range = Mathf.Max(_correctionRange, 0.01f);
                ratio = Mathf.Clamp01(posError / range);
            }

            var syncParentInstance = GetSyncParentIdentity();
            string frame = _softParent && _softParent.isSpawned
                ? $"Soft->{_softParent.name}"
                : syncParentInstance ? $"Parent->{syncParentInstance.name}"
                : _space == RigidbodyTransformSpace.Local ? "Local" : "World";

            string info = $"<b>NetworkRigidbody</b>\n" +
                          $"Server: {isServer} | Controller: {amIController}\n" +
                          $"OwnerAuth: {_ownerAuth}\n" +
                          $"Owner: {(owner.HasValue ? owner.Value.ToString() : "none")}\n" +
                          $"Frame: {frame}\n" +
                          $"---\n" +
                          $"Pos Error: {posError:F3}m\n" +
                          $"Rot Error: {rotError:F1}deg\n" +
                          $"Ratio: {ratio:F3}\n" +
                          $"Velocity: {velocityMagnitude:F2}\n" +
                          $"Correcting: {(amIController ? "-" : _lastCorrectionReason)}\n" +
                          $"---\n" +
                          $"Buffer: {_bufferCount}/{BUFFER_SIZE}\n" +
                          $"Sample: {_bufferSampleMode}\n" +
                          $"Span: {bufferSpan:F3}s\n" +
                          $"Delay: {interpolationDelay:F3}s\n" +
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
#endif
    }
}
