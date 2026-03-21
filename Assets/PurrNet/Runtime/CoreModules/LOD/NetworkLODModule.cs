using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.LOD
{

    /// <summary>
    /// Base LOD module for any type of LOD measurement.
    /// Maintains send intervals to each observer and calls
    /// <see cref="OnSendToObserver"/> when it's time to update them.
    /// </summary>
    [System.Serializable]
    public abstract class NetworkLODModule : NetworkModule, ITick
    {
        /// <summary>
        /// (observer, last sent Time.time)
        /// </summary>
        private readonly Dictionary<PlayerID, float> _lastSendTime = new();

        /// <summary>
        /// Returns the amount of time between sends.
        /// 0 = every tick
        /// </summary>
        /// <param name="observer"></param>
        /// <returns></returns>
        protected abstract float GetSendInterval(PlayerID observer);

        /// <summary>
        /// Call your target RPC here to the observer.
        /// </summary>
        /// <param name="observer"></param>
        /// <param name="deltaTime">The amount of time since the last send. Useful for interpolation.</param>
        protected abstract void OnSendToObserver(PlayerID observer, float deltaTime);

        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            _lastSendTime[player] = 0f;
        }

        public override void OnObserverRemoved(PlayerID player)
        {
            _lastSendTime.Remove(player);
        }

        public override void OnDespawned()
        {
            _lastSendTime.Clear();
        }

        public void OnTick(float delta)
        {
            if (!isServer) return;

            float time = Time.time;
            var observers = parent.observers;

            for (int i = 0; i < observers.Count; i++)
            {
                var observer = observers[i];
                float interval = GetSendInterval(observer);
                float lastSendTime = _lastSendTime.GetValueOrDefault(observer, 0f);
                
                float elapsed = time - lastSendTime;
                if (elapsed < interval) continue;

                _lastSendTime[observer] = time;

                OnSendToObserver(observer, elapsed);
            }
        }
        
    }

}