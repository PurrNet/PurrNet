using System;

namespace PurrNet
{
    public readonly struct GlobalNetworkID : IEquatable<GlobalNetworkID>
    {
        public readonly SceneID scene;
        public readonly NetworkID id;

        public GlobalNetworkID(SceneID scene, NetworkID id)
        {
            this.scene = scene;
            this.id = id;
        }

        public GlobalNetworkID(NetworkIdentity identity)
        {
            if (identity)
            {
                this.scene = identity.sceneId;
                this.id = identity.id.GetValueOrDefault();
            }
            else
            {
                this.scene = default;
                this.id = default;
            }
        }

        public bool Equals(GlobalNetworkID other)
        {
            return scene.Equals(other.scene) && id.Equals(other.id);
        }

        public override bool Equals(object obj)
        {
            return obj is GlobalNetworkID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(scene, id);
        }
    }
}
