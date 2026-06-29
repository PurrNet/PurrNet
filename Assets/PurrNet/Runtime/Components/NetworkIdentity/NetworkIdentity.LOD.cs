using System;
using UnityEngine;

namespace PurrNet
{
    public partial class NetworkIdentity
    {
        public delegate void LODTierChangedDelegate(PlayerID player, byte previousTier, byte newTier);

        /// <summary>
        /// Called when the LOD tier of this object changes for a player.
        /// Server only. Network LOD does not send automatically; use this event to react with user-authored
        /// catch-up state, quality changes, or other LOD-aware behavior.
        /// </summary>
        public event LODTierChangedDelegate onLODTierChanged;

        private NetworkLOD _lod;

        /// <summary>
        /// The <see cref="NetworkLOD"/> component governing this identity, if any.
        /// </summary>
        public NetworkLOD networkLOD => _lod;

        internal void SetLODComponent(NetworkLOD lod)
        {
            _lod = lod;
        }

        /// <summary>
        /// Current LOD tier of this object for the given player.
        /// 0 (full detail) when no LOD component is present. Server only.
        /// </summary>
        public byte GetLODTier(PlayerID player)
        {
            return _lod ? _lod.GetTier(player) : (byte)0;
        }

        /// <summary>
        /// Whether state for this object should be sent to the given player on the current tick,
        /// based on the player's LOD tier. Always true when no LOD component is present.
        /// This is a query only; RPC dispatch and visibility do not consult it automatically.
        /// </summary>
        public bool ShouldSendLODToPlayer(PlayerID player)
        {
            return !_lod || _lod.ShouldSendToPlayer(player);
        }

        /// <summary>
        /// Called when the LOD tier of this object changes for a player.
        /// Server only. Network LOD does not send automatically.
        /// </summary>
        /// <param name="player">The player whose tier changed</param>
        /// <param name="previousTier">The previous tier</param>
        /// <param name="newTier">The new tier</param>
        protected virtual void OnLODTierChanged(PlayerID player, byte previousTier, byte newTier)
        {
        }

        internal void TriggerOnLODTierChanged(PlayerID player, byte previousTier, byte newTier)
        {
            try
            {
                OnLODTierChanged(player, previousTier, newTier);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                onLODTierChanged?.Invoke(player, previousTier, newTier);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

    }
}
