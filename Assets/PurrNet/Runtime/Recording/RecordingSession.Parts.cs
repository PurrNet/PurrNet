using System;
using System.Collections.Generic;
using System.IO;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>
    /// Server to one client: a recording started (or stopped) and the client may write its own part of it. The token
    /// is the player's own; the server's file has its hash, so a part can be checked against the match.
    /// </summary>
    public struct RecordingPartPacket : IPackedAuto
    {
        public bool recording;
        public PlayerID player;
        public ulong idA, idB;
        public ulong tokenA, tokenB;
        public ulong firstTick;
        /// <summary>The server's tick when it sent this: nothing the client records for it happened before.</summary>
        public ulong tick;
    }

    // Client parts: data that only exists on a client (raw input, UI) is written by that client into a small
    // recording of its own and uploaded by it, so it never travels over the game's connection. The server only
    // announces when a recording starts and stops.
    public sealed partial class RecordingSession
    {
        /// <summary>Folder client parts are written to. Relative paths are under Application.persistentDataPath.</summary>
        public string partDirectory = "Recordings";

        /// <summary>The id of the recording being written (a part carries the id of the server's file); null when none is.</summary>
        public string recordingId { get; private set; }

        /// <summary>Whether the open file is a client part.</summary>
        public bool isPart => _file != null && _file == _partFile;

        /// <summary>
        /// Whether samples written now are kept: a recording is open here, or the server announced one and this
        /// client's part opens with the first sample.
        /// </summary>
        public bool wantsSamples => _file != null || _partOffered;

        // Server: the main file's id, and the token of every player a part was offered to.
        ulong _idA, _idB;
        ulong _mainFirstTick;
        readonly Dictionary<ulong, (ulong a, ulong b)> _partTokens = new Dictionary<ulong, (ulong a, ulong b)>();
        readonly byte[] _random = new byte[16];

        // Client: the server's announcement, and the part opened for it.
        bool _partOffered;
        RecordingPartPacket _offer;
        RecordingFile _partFile;
        ulong _partTick;

        static string Hex(ulong a, ulong b) => a.ToString("x16") + b.ToString("x16");

        void Random128(out ulong a, out ulong b)
        {
            try
            {
                using var generator = System.Security.Cryptography.RandomNumberGenerator.Create();
                generator.GetBytes(_random);
            }
            catch (Exception)
            {
                Guid.NewGuid().ToByteArray().CopyTo(_random, 0);
            }
            a = BitConverter.ToUInt64(_random, 0);
            b = BitConverter.ToUInt64(_random, 8);
        }

        /// <summary>
        /// What the server's file keeps of a player's token: SHA-256 of its 32 hex digits, as 64 hex digits. The token
        /// itself is only ever in that player's hands, so reading the recording does not let anyone make a part for it.
        /// </summary>
        static string TokenHash(string token)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes(token));
            var text = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                text.Append(b.ToString("x2"));
            return text.ToString();
        }

        void SubscribeParts()
        {
            _networkManager.Subscribe<RecordingPartPacket>(OnPartPacket, false);
            _networkManager.onPlayerJoined += OnPlayerJoinedParts;
            if (isRecording && !isPart)
                OfferParts();
        }

        void UnsubscribeParts()
        {
            _networkManager.Unsubscribe<RecordingPartPacket>(OnPartPacket, false);
            _networkManager.onPlayerJoined -= OnPlayerJoinedParts;
        }

        // ---- Server ---------------------------------------------------------------------------------

        /// <summary>A main file was opened: gives it an id and tells the connected clients.</summary>
        void BeginParts(ulong firstTick)
        {
            Random128(out _idA, out _idB);
            recordingId = Hex(_idA, _idB);
            _mainFirstTick = firstTick;
            _partTokens.Clear();
            _file.SetLink(LinkJson(recordingId, false, 0, null));
            OfferParts();
        }

        /// <summary>The open file is closing: a main file tells the clients, a part is simply over.</summary>
        void EndParts()
        {
            if (isPart)
            {
                _partOffered = false;
                recordingId = null;
                return;
            }

            if (_networkManager && _networkManager.isServer)
            {
                var players = _networkManager.players;
                for (int i = 0; i < players.Count; i++)
                    if (_partTokens.ContainsKey(players[i].id.value))
                        SendPart(players[i], new RecordingPartPacket { player = players[i] });
            }
            _partTokens.Clear();
            recordingId = null;
        }

        void OfferParts()
        {
            if (!_networkManager || !_networkManager.isServer)
                return;
            var players = _networkManager.players;
            for (int i = 0; i < players.Count; i++)
                OfferPart(players[i]);
        }

        void OnPlayerJoinedParts(PlayerID player, bool isReconnect, bool asServer)
        {
            if (asServer && isRecording && !isPart)
                OfferPart(player);
        }

        void OfferPart(PlayerID player)
        {
            // Bots have no client; a host's own samples go straight into the main file.
            if (player.isBot || (_networkManager.isClient && _networkManager.localPlayer == player))
                return;

            ulong key = player.id.value;
            ulong now = Math.Max(_context.localTick, _mainFirstTick);
            if (!_partTokens.TryGetValue(key, out var token))
            {
                Random128(out token.a, out token.b);
                _partTokens[key] = token;
                WriteMeta(RecordKind.Part, _json.Clear().BeginObject()
                    .Prop("player", key).Prop("tick", now)
                    .Prop("tokenHash", TokenHash(Hex(token.a, token.b))).EndObject());
            }

            SendPart(player, new RecordingPartPacket
            {
                recording = true, player = player, idA = _idA, idB = _idB,
                tokenA = token.a, tokenB = token.b, firstTick = _mainFirstTick, tick = now
            });
        }

        void SendPart(PlayerID player, RecordingPartPacket packet)
        {
            // The server may already be shutting down when a recording stops.
            if (_networkManager.TryGetModule<PurrNet.PlayersBroadcaster>(true, out var broadcaster))
                broadcaster.Send(player, packet);
        }

        // ---- Client ---------------------------------------------------------------------------------

        void OnPartPacket(PlayerID sender, RecordingPartPacket packet, bool asServer)
        {
            // Only a server's word counts, and only to a machine that is not the server itself.
            if (asServer || !_networkManager || _networkManager.isServer)
                return;

            ClosePart();
            _partTick = 0;
            _offer = packet;
            _partOffered = packet.recording;
            // Samples are wanted from now on (see sampleEpoch); the part itself opens with the first one.
            if (_partOffered)
                _sampleEpoch++;
        }

        void ClosePart()
        {
            if (isPart)
                Stop(null);
            _partOffered = false;
        }

        /// <summary>
        /// Once per frame: a part ends with its connection, and its chunk ends here, between frames, by time or when
        /// the log is half full. No chunk ends while a writer is in the middle of its rows, so a writer that saw
        /// <see cref="sampleEpoch"/> change has its full state at the start of the new chunk.
        /// </summary>
        void UpdatePart(bool isServer)
        {
            if (!_partOffered && !isPart)
                return;
            if (isServer || !_networkManager || !_networkManager.isClient)
            {
                ClosePart();
                return;
            }
            if (isPart && _slots.Count > 0 &&
                (PartTick(_context.viewTick) - _chunkFirstTick >= (ulong)_chunkTicks || _job.log.bytes.length * 2 > _logCapacity))
                FlushChunk();
        }

        /// <summary>
        /// The server tick a part's row goes under: the one this client is showing, but never before the tick the
        /// server announced the recording on (the client's clock may not have caught up with the server's yet) and
        /// never going back (the clock is corrected in steps).
        /// </summary>
        ulong PartTick(double view)
        {
            ulong tick = view >= 0 && view < ulong.MaxValue ? (ulong)view : 0;
            return Math.Max(tick, Math.Max(_partTick, Math.Max(_offer.tick, _offer.firstTick)));
        }

        static string LinkJson(string recordingId, bool part, ulong player, string token) =>
            part
                ? $"{{\"recordingId\":\"{recordingId}\",\"kind\":\"part\",\"player\":{player},\"token\":\"{token}\"}}"
                : $"{{\"recordingId\":\"{recordingId}\",\"kind\":\"main\"}}";

        /// <summary>Opens this client's part of the recording the server announced.</summary>
        void OpenPart(ulong firstTick)
        {
            string id = Hex(_offer.idA, _offer.idB);
            string token = Hex(_offer.tokenA, _offer.tokenB);
            ulong player = _offer.player.id.value;
            string fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Application.productName}-{id.Substring(0, 8)}-p{player}.purrrec";
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            int tickRate = Mathf.Max(1, _context.tickRate);
            _types = new RecordingTypeRegistry();
            _baseColumns.Clear();
            _file = _partFile = new RecordingFile(Path.Combine(ResolveDirectory(partDirectory), fileName),
                (long)maxLocalMegabytes * 1024 * 1024);
            _file.SetLink(LinkJson(id, true, player, token));
            RecordingUploader.SetActiveRecording(_file.path);
            recordingId = id;
            _chunkTicks = Mathf.Max(1, Mathf.RoundToInt(chunkSeconds * tickRate));
            _chunkFirstTick = firstTick;
            _lastTick = 0;
            _ticksRecorded = 0;
            _chunks = 0;
            _peakLogBytes = 0;
            _maxTickBytes = 0;
            // A part holds a few small rows per tick.
            _logCapacity = 1 << 16;
            ReserveChunkMemory();
            _stats.Reset();
            _stats.file = _file;
            _slots.Clear();
            chunkStart = true;

            var json = _json.Clear().BeginObject()
                .Prop("format", "purrrec").Prop("version", 1)
                .Prop("part", true).Prop("recordingId", id).Prop("player", player).Prop("token", token)
                .Prop("game", Application.productName).Prop("gameVersion", Application.version)
                .Prop("unityVersion", Application.unityVersion)
                .Prop("tickRate", tickRate)
                .Prop("firstTick", firstTick)
                .Prop("serverFirstTick", _offer.firstTick)
                .Prop("offeredTick", _offer.tick)
                .Prop("startedUtc", DateTime.UtcNow.ToString("o"))
                .Prop("recorder", "client")
                .Key("labels").BeginObject();
            foreach (var pair in labels)
                json.Prop(pair.Key, pair.Value);
            json.EndObject().EndObject();
            EnqueueJson(RecordKind.Header, json, false);
        }

        /// <summary>
        /// A sample written on a client: a row in its part, under the server tick the client was presenting (never
        /// before the recording's first tick, never going back).
        /// </summary>
        void WritePartSample<T>(string stream, ref ClientSample<T> envelope) where T : struct
        {
            if (!_partOffered)
                return;

            ulong tick = PartTick(envelope.viewTick);
            if (_file == null)
                OpenPart(tick);
            else if (!isPart)
                return;

            if (_slots.Count == 0)
                _chunkFirstTick = tick;
            if (tick != _lastTick || _ticksRecorded == 0)
                _ticksRecorded++;
            _lastTick = _partTick = tick;

            AppendRow(GetCustomStream(stream, typeof(ClientSample<T>), _offer.player), tick, ref envelope);
        }
    }
}
