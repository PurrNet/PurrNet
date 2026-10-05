using PurrNet.Utils;

namespace PurrNet.Transports
{
    /// <summary>
    /// Read-only view of the project PurrServices linked to this Unity project. PurrServices
    /// (dev.purrnet.services) stores the link in <see cref="ApplicationConstants"/>; the keys here
    /// mirror its <c>PurrServicesSettings</c> so PurrNet itself can honour the project-wide
    /// setting without depending on that package. Writing the link stays in PurrServices.
    /// </summary>
    public static class PurrServicesProjectLink
    {
        public const string KeyBuildApiKey = "PurrServices.build.apiKey";
        public const string KeyBuildProjectId = "PurrServices.build.projectId";
        public const string KeyBuildProjectName = "PurrServices.build.projectName";
        public const string KeyEditorOverride = "PurrServices.editor.override";
        public const string KeyEditorApiKey = "PurrServices.editor.apiKey";
        public const string KeyEditorProjectId = "PurrServices.editor.projectId";
        public const string KeyEditorProjectName = "PurrServices.editor.projectName";

        /// <summary>The Unity Editor override profile is enabled (only ever true inside the editor).</summary>
        public static bool isEditorProfile
        {
            get
            {
#if UNITY_EDITOR
                return ApplicationConstants.TryGet(KeyEditorOverride, out var value) &&
                       bool.TryParse(value, out var enabled) && enabled;
#else
                return false;
#endif
            }
        }

        /// <summary>The linked project's public key for the active profile, or null.</summary>
        public static string projectKey => Get(isEditorProfile ? KeyEditorApiKey : KeyBuildApiKey);

        public static string projectId => Get(isEditorProfile ? KeyEditorProjectId : KeyBuildProjectId);

        public static string projectName => Get(isEditorProfile ? KeyEditorProjectName : KeyBuildProjectName);

        public static bool isLinked => !string.IsNullOrWhiteSpace(projectKey);

        /// <summary>The Player Builds link, regardless of which profile is active.</summary>
        public static string buildProjectKey => Get(KeyBuildApiKey);
        public static string buildProjectId => Get(KeyBuildProjectId);
        public static string buildProjectName => Get(KeyBuildProjectName);
        public static bool isBuildLinked => !string.IsNullOrWhiteSpace(buildProjectKey);

        static string Get(string key) =>
            ApplicationConstants.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
    }
}
