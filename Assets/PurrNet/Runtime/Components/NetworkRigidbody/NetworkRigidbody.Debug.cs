using UnityEngine;

namespace PurrNet
{
    public partial class NetworkRigidbody
    {
        [Header("Debug")]
        [SerializeField] private bool _debugGizmos;
        [SerializeField] private float _debugTextOffset = 2f;

        private void OnDrawGizmos()
        {
            if (!_debugGizmos || _rigidbody == null)
                return;

            bool isController = isSpawned && IsController(_ownerAuth);

            Gizmos.color = isController ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(_rigidbody.position, 0.2f);

            if (!isController && isSpawned)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_targetPosition, 0.15f);
                
                Gizmos.color = Color.red;
                Gizmos.DrawLine(_rigidbody.position, _targetPosition);

                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.matrix = Matrix4x4.TRS(_targetPosition, _targetRotation, Vector3.one * 0.3f);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                Gizmos.matrix = Matrix4x4.identity;
            }

#if UNITY_6000_0_OR_NEWER
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_rigidbody.position, _rigidbody.linearVelocity * 0.5f);
#else
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_rigidbody.position, _rigidbody.velocity * 0.5f);
#endif
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
            float error = Vector3.Distance(_rigidbody.position, _targetPosition);
            float springScale = GetDynamicSpringScale();
            float effectiveSpring = _springConstant * springScale;
            float effectiveDamping = _dampingConstant * springScale;

#if UNITY_6000_0_OR_NEWER
            float velocityMagnitude = _rigidbody.linearVelocity.magnitude;
#else
            float velocityMagnitude = _rigidbody.velocity.magnitude;
#endif

            string info = $"<b>NetworkRigidbody</b>\n" +
                          $"Controller: {isController}\n" +
                          $"OwnerAuth: {_ownerAuth}\n" +
                          $"Owner: {(owner.HasValue ? owner.Value.ToString() : "none")}\n" +
                          $"isServer: {isServer}\n" +
                          $"isClient: {isClient}\n" +
                          $"---\n" +
                          $"Position: {_rigidbody.position:F2}\n" +
                          $"Target: {_targetPosition:F2}\n" +
                          $"Error: {error:F3}m\n" +
                          $"Extrapolation: {_lastExtrapolation}\n" +
                          $"---\n" +
                          $"Velocity: {velocityMagnitude:F2}\n" +
                          $"Correcting: {_isCorreting}\n" +
                          $"CorrectionTimer: {_correctionTimer:F2}s\n" +
                          $"---\n" +
                          $"<b>Dynamic Scaling</b>\n" +
                          $"Accel: {_recentAccelerationMagnitude:F2}\n" +
                          $"Scale: {springScale:P1}\n" +
                          $"Spring: {_springConstant:F1} → {effectiveSpring:F2}\n" +
                          $"Damping: {_dampingConstant:F1} → {effectiveDamping:F2}";

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