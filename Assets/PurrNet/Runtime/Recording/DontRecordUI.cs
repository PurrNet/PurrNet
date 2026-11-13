using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>
    /// Keeps the UI on and below this object out of recordings: uGUI graphics under it, and the whole document when
    /// it sits on a UIDocument. For part of a UI Toolkit document, give the element the USS class
    /// <see cref="UssClass"/> instead.
    /// </summary>
    [AddComponentMenu("PurrNet/Recording/Dont Record UI")]
    [DisallowMultipleComponent]
    public sealed class DontRecordUI : MonoBehaviour
    {
        /// <summary>USS class that keeps a UI Toolkit element and everything inside it out of recordings.</summary>
        public const string UssClass = "dont-record";

        internal static readonly List<DontRecordUI> all = new List<DontRecordUI>();
        /// <summary>Changes whenever a marker is enabled or disabled.</summary>
        internal static int version;

        void OnEnable()
        {
            all.Add(this);
            version++;
        }

        void OnDisable()
        {
            all.Remove(this);
            version++;
        }
    }
}
