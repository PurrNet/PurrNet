using System;
using UnityEngine.Events;

namespace PurrNet.HostMigration
{
    [Serializable]
    public sealed class HostMigrationPlanEvent : UnityEvent<HostMigrationPlan>
    {
    }

    [Serializable]
    public sealed class HostMigrationFailureEvent : UnityEvent<string>
    {
    }
}
