using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>Records the callback itself, including byte and pixel checks at callback time.</summary>
public class SyncTextureAssetTestIdentity : NetworkIdentity
{
    [SerializeField] private bool _ownerAuthority;
    [SerializeField] private SyncTextureAsset _serverTexture = new(ownerAuth: false, maxKBPerSec: 4000, ownerOnly: false);
    [SerializeField] private SyncTextureAsset _ownerTexture = new(ownerAuth: true, maxKBPerSec: 4000, ownerOnly: false);

    private static SyncTextureAssetTestIdentity _localServerAuthority;
    private static SyncTextureAssetTestIdentity _localOwnerAuthority;

    public static SyncTextureAssetTestIdentity GetLocalInstance(bool ownerAuthority)
        => ownerAuthority ? _localOwnerAuthority : _localServerAuthority;

    public static void ResetLocalInstance(bool ownerAuthority)
    {
        if (ownerAuthority)
            _localOwnerAuthority = null;
        else
            _localServerAuthority = null;
    }

    private readonly int[] _callbackCounts = new int[SyncTextureAssetTestData.PhaseCount];
    private readonly string[] _callbackFailures = new string[SyncTextureAssetTestData.PhaseCount];
    private readonly List<Texture2D> _sourceTextures = new();
    private int _phase = -1;
    private SyncTextureAssetTestData _expected;
    private string _unexpectedCallback;
    private bool _subscribed;

    private SyncTextureAsset texture => _ownerAuthority ? _ownerTexture : _serverTexture;

    public void Configure(bool ownerAuthority) => _ownerAuthority = ownerAuthority;

    public void Prepare(int phase)
    {
        _phase = phase;
        _expected = new SyncTextureAssetTestData(phase);
        _sourceTextures.Add(_expected.source);
    }

    public void Send()
    {
        // Exercise the public asset setter used in the original report, including the local
        // OnDataReady path. Every phase supplies a new Texture2D reference.
        texture.assetToSync = _expected.source;
    }

    public int CallbackCount(int phase) => _callbackCounts[phase];

    public string Describe(int phase)
    {
        return $"phase={phase + 1}, callbacks={_callbackCounts[phase]}, " +
               $"JPEG={texture.data.Count}/{_expected.jpeg.Length}, compressed={texture.compressedData.Count}, " +
               $"progress={texture.progress:0.###}, ready={texture.isDataReady}";
    }

    public string Validate(int phase)
    {
        if (_unexpectedCallback != null)
            return _unexpectedCallback;
        if (_callbackCounts[phase] != 1)
            return $"onDataChanged count={_callbackCounts[phase]}, expected exactly 1";
        if (_callbackFailures[phase] != null)
            return _callbackFailures[phase];
        return ValidateCurrent(texture.asset);
    }

    private string ValidateCurrent(Texture2D content)
    {
        if (content != texture.asset || content != texture.assetToSync)
            return "onDataChanged argument differs from asset / assetToSync accessor";
        if (!texture.isDataReady || !Mathf.Approximately(texture.progress, 1f))
            return "onDataChanged fired before local data was ready";

        var bytesFailure = _expected.CompareBytes(texture.data);
        if (bytesFailure != null)
            return bytesFailure;

        int compressedLength = texture.compressedData.Count;
        bool expectMultipleParts = _phase == 1;
        if (compressedLength <= 0 || (compressedLength > SyncTextureAssetTestData.TransferPartSize) != expectMultipleParts)
            return $"compressed length={compressedLength} did not exercise the expected " +
                   (expectMultipleParts ? "multiple-part transfer" : "single-part transfer");

        // SyncTextureAsset uses lossy JPEG. Compare with an independent decode of the expected
        // JPEG, not with the original uncompressed source pixels.
        return _expected.CompareTexture(content);
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
        if (_subscribed)
            return;
        _subscribed = true;
        _serverTexture.onDataChanged += OnServerTextureChanged;
        _ownerTexture.onDataChanged += OnOwnerTextureChanged;
    }

    protected override void OnSpawned(bool asServer)
    {
        if (_ownerAuthority)
            _localOwnerAuthority = this;
        else
            _localServerAuthority = this;
    }

    private void OnServerTextureChanged(Texture2D content) => OnTextureChanged(false, content);
    private void OnOwnerTextureChanged(Texture2D content) => OnTextureChanged(true, content);

    private void OnTextureChanged(bool ownerAuthority, Texture2D content)
    {
        if (ownerAuthority != _ownerAuthority || _phase < 0)
        {
            _unexpectedCallback = $"unexpected onDataChanged: ownerAuthority={ownerAuthority}, phase={_phase}";
            return;
        }

        _callbackCounts[_phase]++;
        try
        {
            _callbackFailures[_phase] ??= ValidateCurrent(content);
        }
        catch (Exception exception)
        {
            _callbackFailures[_phase] = $"onDataChanged content check threw {exception.GetType().Name}: {exception.Message}";
        }
    }

    protected override void OnDespawned()
    {
        if (_subscribed)
        {
            _serverTexture.onDataChanged -= OnServerTextureChanged;
            _ownerTexture.onDataChanged -= OnOwnerTextureChanged;
            _subscribed = false;
        }

        if (_serverTexture.asset && !_sourceTextures.Contains(_serverTexture.asset))
            Destroy(_serverTexture.asset);
        if (_ownerTexture.asset && !_sourceTextures.Contains(_ownerTexture.asset))
            Destroy(_ownerTexture.asset);
        foreach (var source in _sourceTextures)
        {
            if (source)
                Destroy(source);
        }
        _sourceTextures.Clear();
        if (GetLocalInstance(_ownerAuthority) == this)
            ResetLocalInstance(_ownerAuthority);
    }
}
