using Newtonsoft.Json;

namespace PurrNet.Editor
{
    public struct Result<T>
    {
        public bool Success { get; }
        public T Value { get; }
        public string Error { get; }

        private Result(bool success, T value, string error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static Result<T> Ok(T value) => new Result<T>(true, value, null);
        public static Result<T> Fail(string error) => new Result<T>(false, default, error);
    }

    public class PackagesResponse
    {
        [JsonProperty("packages")]
        public PackageInfo[] Packages { get; private set; }
    }

    public class PackageInfo
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("slug")]
        public string Slug { get; private set; }

        [JsonProperty("github_owner")]
        public string GithubOwner { get; private set; }

        [JsonProperty("github_repo")]
        public string GithubRepo { get; private set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; private set; }

        [JsonProperty("description")]
        public string Description { get; private set; }

        [JsonProperty("upm_package_name")]
        public string UpmPackageName { get; private set; }

        [JsonProperty("required_tier")]
        public string RequiredTier { get; private set; }

        [JsonProperty("is_hidden")]
        public bool IsHidden { get; private set; }

        [JsonProperty("is_early_access")]
        public bool IsEarlyAccess { get; private set; }

        [JsonProperty("is_user_editable")]
        public bool IsUserEditable { get; private set; }

        [JsonProperty("dependency_ids")]
        public string[] DependencyIds { get; private set; }

        [JsonProperty("entitled_version")]
        public string EntitledVersion { get; private set; }

        [JsonProperty("has_access")]
        public bool HasAccess { get; private set; }

        [JsonProperty("frozen")]
        public bool Frozen { get; private set; }

        [JsonProperty("latest_version")]
        public string LatestVersion { get; private set; }

        [JsonProperty("category")]
        public string Category { get; private set; }

        [JsonProperty("display_order")]
        public int DisplayOrder { get; private set; }

        [JsonProperty("versions")]
        public VersionInfo[] Versions { get; private set; }

        [JsonProperty("is_external")]
        public bool IsExternal { get; private set; }

        [JsonProperty("git_install_url_release")]
        public string GitInstallUrlRelease { get; private set; }

        [JsonProperty("git_install_url_dev")]
        public string GitInstallUrlDev { get; private set; }

        [JsonProperty("latest_commit_release")]
        public string LatestCommitRelease { get; private set; }

        [JsonProperty("latest_commit_dev")]
        public string LatestCommitDev { get; private set; }

        public string GetUpmPackageName()
        {
            // The website reads this directly from the repository's package.json.
            // Never guess: a fabricated manifest key can install the right files under
            // the wrong package identity and leave Unity's lock file inconsistent.
            return UpmPackageName;
        }
    }

    public class VersionInfo
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("version")]
        public string Version { get; private set; }

        [JsonProperty("channel")]
        public string Channel { get; private set; }

        [JsonProperty("tag_name")]
        public string TagName { get; private set; }

        [JsonProperty("release_notes")]
        public string ReleaseNotes { get; private set; }

        [JsonProperty("published_at")]
        public string PublishedAt { get; private set; }
    }

    public class DownloadResponse
    {
        [JsonProperty("url")]
        public string Url { get; private set; }

        [JsonProperty("filename")]
        public string Filename { get; private set; }
    }

    public class EntitlementsResponse
    {
        [JsonProperty("tier")]
        public string Tier { get; private set; }

        [JsonProperty("total_donated_cents")]
        public int TotalDonatedCents { get; private set; }

        [JsonProperty("features")]
        public FeaturesInfo Features { get; private set; }
    }

    public class FeaturesInfo
    {
        [JsonProperty("basic-tools")]
        public bool BasicTools { get; private set; }

        [JsonProperty("pro-tools")]
        public bool ProTools { get; private set; }

        [JsonProperty("premium-tools")]
        public bool PremiumTools { get; private set; }

        [JsonProperty("studio-tools")]
        public bool StudioTools { get; private set; }

        [JsonProperty("supporter")]
        public bool Supporter { get; private set; }
    }

    public class UserInfo
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("username")]
        public string Username { get; private set; }

        [JsonProperty("avatar_url")]
        public string AvatarUrl { get; private set; }

        [JsonProperty("is_admin")]
        public bool IsAdmin { get; private set; }
    }

    public class PackageRegistrationRequest
    {
        [JsonProperty("github_owner")]
        public string GithubOwner { get; }

        [JsonProperty("github_repo")]
        public string GithubRepo { get; }

        [JsonProperty("display_name")]
        public string DisplayName { get; }

        [JsonProperty("required_tier")]
        public string RequiredTier { get; }

        [JsonProperty("is_early_access")]
        public bool IsEarlyAccess { get; }

        public PackageRegistrationRequest(string githubOwner, string githubRepo, string displayName)
        {
            GithubOwner = githubOwner;
            GithubRepo = githubRepo;
            DisplayName = displayName;
            RequiredTier = "admin";
            IsEarlyAccess = true;
        }
    }

    public class PackageRegistrationResponse
    {
        [JsonProperty("success")]
        public bool Success { get; private set; }

        [JsonProperty("package")]
        public PackageInfo Package { get; private set; }
    }

    public class ApiError
    {
        [JsonProperty("error")]
        public string Error { get; private set; }

        [JsonProperty("message")]
        public string Message { get; private set; }
    }
}
