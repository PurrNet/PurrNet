using System;
using System.Collections.Generic;
using System.Threading.Tasks;
#if UNITY_EDITOR && PURR_LEAKS_CHECK
using PurrNet.Pooling;
#endif
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(32000)]
    public class UnityLatestUpdate : MonoBehaviour
    {
        static UnityLatestUpdate _instance;

        public static event Action onUpdate;

        public static event Action onFixedUpdate;

        public static event Action onLatestUpdate;

        private static readonly List<PriorityAction> _executeASAP = new();

        struct PriorityAction
        {
            public int priority;
            public int subPriority;
            public Action action;
        }

        private void Awake()
        {
            TriggerPendingAsaps();
        }

        /// <summary>
        /// Execute body as soon as possible, be it Update/LateUpdate/Start/Awake whatever
        /// Higher priority value means it will be executed later
        /// </summary>
        /// <param name="action"></param>
        /// <param name="priority"></param>
        /// <param name="subPriority"></param>
        public static void ExecuteAsap(Action action, int priority = 0, int subPriority = 0)
        {
            var item = new PriorityAction
            {
                priority = priority,
                subPriority = subPriority,
                action = action,
            };

            int insertIdx = _executeASAP.Count;

            for (int i = 0; i < _executeASAP.Count; i++)
            {
                var cur = _executeASAP[i];
                if (cur.priority > priority ||
                    (cur.priority == priority && cur.subPriority > subPriority))
                {
                    insertIdx = i;
                    break;
                }
            }

            _executeASAP.Insert(insertIdx, item);
        }

        public static void TriggerPendingAsaps()
        {
            for (var i = 0; i < _executeASAP.Count; i++)
            {
                var action = _executeASAP[i];

                try
                {
                    action.action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public static Task Yield()
        {
            var promise = new TaskCompletionSource<bool>();

            onUpdate += OnUpdate;

            return promise.Task;

            void OnUpdate()
            {
                if (promise.TrySetResult(true))
                    onUpdate -= OnUpdate;
            }
        }

        public static Task WaitSeconds(float seconds)
        {
            var promise = new TaskCompletionSource<bool>();
            float timer = 0f;

            onUpdate += OnUpdate;

            return promise.Task;

            void OnUpdate()
            {
                timer += Time.deltaTime;
                if (timer >= seconds && promise.TrySetResult(true))
                    onUpdate -= OnUpdate;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            onUpdate = null;
            onFixedUpdate = null;
            onLatestUpdate = null;
            _executeASAP.Clear();

            if (_instance)
                return;

            var go = new GameObject("PurrNet_UnityLatestUpdate")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<UnityLatestUpdate>();
        }

#if UNITY_EDITOR && PURR_LEAKS_CHECK
        private float _sweep;
#endif

        private void OnEnable()
        {
            TriggerPendingAsaps();
        }

        private void OnDisable()
        {
            TriggerPendingAsaps();
        }

        private void OnDestroy()
        {
            TriggerPendingAsaps();
        }

        private void Update()
        {
            TriggerPendingAsaps();
            onUpdate?.Invoke();
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            _sweep += Time.deltaTime;

            if (_sweep >= 1f)
            {
                _sweep = 0f;
                AllocationTracker.CheckForLeaks();
            }
#endif
        }

        private void FixedUpdate()
        {
            TriggerPendingAsaps();
            onFixedUpdate?.Invoke();
        }

        private void LateUpdate()
        {
            TriggerPendingAsaps();
            onLatestUpdate?.Invoke();
        }
    }
}
