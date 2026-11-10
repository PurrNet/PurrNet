using UnityEngine;
using UnityEngine.Rendering;

namespace PurrNet.Recording
{
    /// <summary>
    /// Writes the camera this machine renders with to the "camera" stream. Recorders add
    /// one next to themselves unless its camera recording is turned off; add your own to record a specific camera.
    /// The pose is taken right before the camera renders, after every script and camera rig has moved it, so it is
    /// exactly what the player saw. Without rendering (batchmode) it falls back to the end of LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class RecordLocalCamera : MonoBehaviour
    {
        [Tooltip("Camera to record. Empty uses Camera.main.")]
        [SerializeField] private Camera _camera;
        [SerializeField] private string _stream = "camera";
        [Tooltip("Most samples per second. Frames beyond this are skipped.")]
        [SerializeField] private float _maxSamplesPerSecond = 60f;

        float _nextSample;
        int _sampledFrame = -1;
        int _lastRenderFrame = -1000;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Camera.onPreCull += OnPreCullCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Camera.onPreCull -= OnPreCullCamera;
        }

        Camera target => _camera ? _camera : Camera.main;

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam) => OnRender(cam);

        void OnPreCullCamera(Camera cam) => OnRender(cam);

        void OnRender(Camera cam)
        {
            if (cam != target)
                return;
            _lastRenderFrame = Time.frameCount;
            Sample(cam);
        }

        void LateUpdate()
        {
            // Only when nothing has rendered recently (batchmode, minimized).
            if (Time.frameCount - _lastRenderFrame > 2 && target)
                Sample(target);
        }

        void Sample(Camera cam)
        {
            if (_sampledFrame == Time.frameCount)
                return;

            var recorder = RecordingSession.active;
            if (recorder == null)
                return;

            float now = Time.unscaledTime;
            if (now < _nextSample)
                return;
            _nextSample = Mathf.Max(now - 0.5f / _maxSamplesPerSecond, _nextSample) + 1f / _maxSamplesPerSecond;
            _sampledFrame = Time.frameCount;

            var t = cam.transform;
            recorder.WriteSample(_stream, new CameraSample
            {
                position = t.position,
                rotation = t.rotation,
                fieldOfView = cam.fieldOfView,
                aspect = cam.aspect,
                orthographic = cam.orthographic,
                orthographicSize = cam.orthographicSize
            });
        }
    }
}
