using System;
using UnityEngine;

namespace PurrNet.HostMigration
{
    [Serializable]
    public sealed class HostMigrationPlan
    {
        [SerializeField] private HostMigrationDecision _decision;
        [SerializeField] private PlayerID _localPlayer;
        [SerializeField] private PlayerID _promotedPlayer;
        [SerializeField] private HostMigrationEndpoint _endpoint;
        [SerializeField] private string _reason;

        public HostMigrationPlan(HostMigrationDecision decision, PlayerID localPlayer,
            PlayerID promotedPlayer, HostMigrationEndpoint endpoint, string reason)
        {
            _decision = decision;
            _localPlayer = localPlayer;
            _promotedPlayer = promotedPlayer;
            _endpoint = endpoint;
            _reason = reason ?? string.Empty;
        }

        public HostMigrationDecision decision => _decision;

        public PlayerID localPlayer => _localPlayer;

        public PlayerID promotedPlayer => _promotedPlayer;

        public HostMigrationEndpoint endpoint => _endpoint;

        public string reason => _reason;

        public bool shouldPromote => _decision == HostMigrationDecision.PromoteToServer;

        public bool shouldTransfer => _decision == HostMigrationDecision.TransferToServer;
    }
}
