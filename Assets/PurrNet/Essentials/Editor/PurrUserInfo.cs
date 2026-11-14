using Newtonsoft.Json;

namespace PurrNet.Essentials
{
    public class PurrUserInfo
    {
        [JsonProperty("id")]
        public string id { get; private set; }

        [JsonProperty("username")]
        public string username { get; private set; }

        [JsonProperty("avatar_url")]
        public string avatarUrl { get; private set; }

        [JsonProperty("is_admin")]
        public bool isAdmin { get; private set; }
    }
}
