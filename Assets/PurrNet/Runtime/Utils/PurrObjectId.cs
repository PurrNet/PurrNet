#if UNITY_6000_3_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace PurrNet.Utils
{
    /// <summary>
    /// Version safe replacement for Object.GetInstanceID, which is obsolete since Unity 6000.3.
    /// </summary>
    public static class PurrObjectId
    {
        public static ObjectId Of(UnityEngine.Object obj)
        {
#if UNITY_6000_3_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }
    }
}
