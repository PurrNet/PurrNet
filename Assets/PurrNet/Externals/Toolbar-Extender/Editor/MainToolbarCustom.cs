#if UNITY_6000_3_OR_NEWER
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace UnityEditor.Toolbars
{
    public static class ToolbarGUI
    {
        public static MainToolbarElement Create(Func<VisualElement> createElement)
        {
            var type = typeof(MainToolbarButton).Assembly.GetType("UnityEditor.Toolbars.MainToolbarCustom");

            if (type == null)
                return null;

            var instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { createElement },
                culture: null
            );

            return (MainToolbarElement)instance;
        }
    }
}
#endif
