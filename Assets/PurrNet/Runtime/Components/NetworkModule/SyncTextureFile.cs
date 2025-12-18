using System;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public class SyncTextureFile : SyncFile<Texture2D>
    {
        public override void FromBytes(ArraySegment<byte> bytes, ref Texture2D content)
        {
            if (!content)
                content = new Texture2D(1, 1);

            content.LoadImage(bytes.Array);
        }
    }
}
