using System;

namespace PurrNet.Editor.Tests
{
    [Serializable]
    public class UitkDrawerProbeValue
    {
        public int value;
    }

    public class UitkDrawerProbe : NetworkBehaviour
    {
        public UitkDrawerProbeValue probe;
    }
}
