using UnityEngine;

namespace PurrNet.Essentials
{
    public interface IBuildManifestExtractor
    {
        string netcode { get; }

        string netcodeVersion { get; }

        bool IsNetworkObject(GameObject gameObject);

        void Extract(BuildManifest manifest);
    }
}
