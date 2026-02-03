using System;
using System.Collections.Generic;

namespace PurrNet.DeltaPackerAnalysis.Editor
{
    /// <summary>
    /// Serializable snapshot of a DeltaPacker analysis run.
    /// Saved under DeltaSnapshots as JSON for later comparison.
    /// </summary>
    [Serializable]
    public class DeltaPackerSnapshotData
    {
        public string name;
        public string timestamp;
        public TypeResultData[] results;

        public List<TypeResultData> ResultsList
        {
            get => results != null ? new List<TypeResultData>(results) : new List<TypeResultData>();
            set => results = value?.ToArray() ?? Array.Empty<TypeResultData>();
        }
    }

    [Serializable]
    public class TypeResultData
    {
        public string TypeName;
        public int BitsFull;
        public int BitsDeltaUnchanged;
        public int BitsDeltaChanged;
        public double WriteTimeMicroseconds;
        public double ReadTimeMicroseconds;
        public string Error;
    }
}
