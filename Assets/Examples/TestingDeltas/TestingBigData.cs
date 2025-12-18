using PurrNet;
using UnityEngine;
using UnityEngine.UI;

public class TestingBigData : NetworkIdentity
{
    [SerializeField] private SyncTextureFile _avatar;
    [SerializeField] private RawImage _avatarImage;
    [SerializeField] private string _path;

    private string _avatarPath;

    private void OnValidate()
    {
        if (!isSpawned || !isController)
            return;

        if (_path != _avatarPath)
        {
            _avatarPath = _path;
            _avatar.filePath = _path;
        }
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        if (oldOwner != newOwner)
        {
            _avatarPath = null;
            OnValidate();
        }
    }

    protected override void OnSpawned()
    {
        OnValidate();
    }

    private void OnEnable()
    {
        _avatar.onDataChanged += OnAvatarChanged;
    }

    private void OnDisable()
    {
        _avatar.onDataChanged -= OnAvatarChanged;
    }

    private void OnAvatarChanged(Texture2D texture)
    {
        _avatarImage.texture = texture;
    }

    [PurrButton]
    public void TakeOwnershipButton()
    {
        TakeOwnership();
    }
}
