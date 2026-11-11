using UnityEngine;
using UnityEngine.Rendering;

namespace PurrNet.Recording
{
    /// <summary>
    /// Writes the camera this machine renders with to the "camera" stream. Recorders add
    /// one next to themselves unless its camera recording is turned off; add your own to record a specific camera.
    /// The pose is taken right before the camera renders, after every script and camera rig has moved it, so it is
    /// exactly what the player saw. Without rendering (batchmode) it falls back to the end of LateUpdate. One sample
    /// is taken per tick.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class RecordLocalCamera : MonoBehaviour
    {
        [Tooltip("Camera to record. Empty uses Camera.main.")]
        [SerializeField] private Camera _camera;

        ulong _sampledTick = ulong.MaxValue;
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
            var recorder = RecordingSession.active;
            if (recorder == null)
                return;

            ulong tick = recorder.localTick;
            if (tick == _sampledTick)
                return;
            _sampledTick = tick;

            var t = cam.transform;
            recorder.WriteSample(RecordingSession.CameraStream, new CameraSample
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
