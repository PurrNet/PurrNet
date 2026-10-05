using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Scripting;

namespace PurrNet
{
    [AttributeUsage(AttributeTargets.Method), UsedImplicitly]
    public class PurrContextButtonAttribute
#if PURR_CONTEXT_BUTTONS
        : PurrButtonAttribute { }
#else
        : Attribute {}
#endif

    [AttributeUsage(AttributeTargets.Method), UsedImplicitly]
    public class PurrButtonAttribute : PreserveAttribute
    {
        public string ButtonName { get; private set; }

        public PurrButtonAttribute(string buttonName = "")
        {
            ButtonName = buttonName;
        }
    }

}
