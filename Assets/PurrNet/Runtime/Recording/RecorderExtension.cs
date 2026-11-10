using System;

namespace PurrNet.Recording
{
    /// <summary>
    /// Keeps a network identity class, or one sync field of it, out of recordings: plumbing that is not game state
    /// (PurrDiction's PredictionManager, whose data is recorded as predicted state and input instead).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public sealed class DontRecordAttribute : Attribute { }

    /// <summary>
    /// Adds another package's data to what <see cref="NetworkRecorder"/> records (PurrDiction's predicted state and
    /// input). Registered once with <see cref="NetworkRecorder.RegisterExtension{T}"/>; every recorder creates its own.
    /// </summary>
    public abstract class RecorderExtension
    {
        public NetworkRecorder recorder { get; internal set; }

        public RecordingSession session => recorder.session;

        /// <summary>The source whose <see cref="RecordingSource.RecordTick"/> writes this extension's rows.</summary>
        public abstract RecordingSource source { get; }

        /// <summary>
        /// The clock recordings run on while this extension has one (PurrDiction's prediction ticks), or null for
        /// network ticks. An extension with a clock calls <see cref="NetworkRecorder.RecordTick"/> itself, once per tick.
        /// A recording has one clock: when it changes, the file ends and the next one starts.
        /// </summary>
        public virtual IRecordingContext clock => null;

        /// <summary>False keeps recordings from starting on their own (the extension's world is still loading).</summary>
        public virtual bool isReady => true;

        /// <summary>Whether this extension writes the PLAYER records itself.</summary>
        public virtual bool recordsPlayers => false;

        public virtual void OnCreated() { }
        public virtual void OnUpdate() { }
        public virtual void OnRecordingStarted() { }
        /// <summary>Called before the file is closed.</summary>
        public virtual void OnRecordingStopping() { }
        /// <summary>Adds properties to the FOOTER record.</summary>
        public virtual void WriteFooter(JsonBuilder json) { }
        public virtual void OnDestroyed() { }
    }
}
