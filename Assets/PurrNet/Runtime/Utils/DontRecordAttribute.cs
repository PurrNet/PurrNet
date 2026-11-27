using System;

namespace PurrNet.Recording
{
    /// <summary>
    /// Keeps a network identity class, one sync field or RPC of it, a broadcast type, or every RPC of an assembly out
    /// of recordings: plumbing that is not game state
    /// (PurrDiction's PredictionManager, whose data is recorded as predicted state and input instead).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Struct |
                    AttributeTargets.Assembly)]
    public sealed class DontRecordAttribute : Attribute { }
}
