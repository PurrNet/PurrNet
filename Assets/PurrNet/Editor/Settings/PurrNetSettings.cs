using System;
using System.IO;
using Newtonsoft.Json;

namespace PurrNet.Editor
{
    public enum ToolbarMode
    {
        Full,
        Compact,
        None
    }

    [Serializable]
    public class PurrNetSettings
    {
        private const string _path = "ProjectSettings/PurrNetSettings.asset";

        static object _lock = new object();

        public StripCodeMode stripCodeMode = StripCodeMode.DoNotStrip;
        public GuardFailureAction guardFailureAction = GuardFailureAction.ReturnDefault;

        public string reflectionCachePath = "Assets/PurrNet/ReflectionRPCTargets.txt";

        public ToolbarMode toolbarMode = ToolbarMode.Full;
        public bool toolbarTransportDropDown;

        public bool stripServerCode => stripCodeMode != StripCodeMode.DoNotStrip;

        public static event Action<PurrNetSettings> onSettingsChanged;

        public static void SaveSettings(PurrNetSettings settings)
        {
            lock (_lock)
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_path, json);
                onSettingsChanged?.Invoke(settings);
            }
        }

        public static PurrNetSettings GetOrCreateSettings()
        {
            lock (_lock)
            {
                if (File.Exists(_path))
                {
                    string json = File.ReadAllText(_path);
                    return JsonConvert.DeserializeObject<PurrNetSettings>(json);
                }

                var settings = new PurrNetSettings();
                SaveSettings(settings);
                return settings;
            }
        }
    }
}
