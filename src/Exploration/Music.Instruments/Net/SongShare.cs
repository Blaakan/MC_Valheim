using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = server songs. Server (or host; single player = own game) with ShareSongs on list the MIDI files of its
// ServerSongsFolder; every player see them in song window ("Server songs") and get a file's bytes when they pick it.
// Plain ZRpc, ZPackage payloads only (bad raw parameter read log the sender out), compatible peers only:
//   client -> server  "<guid>.SongListRequest"  (byte layout)                     song window open (ListEvery s at most)
//   server -> client  "<guid>.SongList"         (byte layout, bool shared, int count, per song: int hash, int size,
//                                                 string name)
//   client -> server  "<guid>.SongRequest"      (byte layout, int hash, int request)   player PICK a server song (click,
//                                                 Enter, Play...; never while just moving through the list)
//   server -> client  "<guid>.SongChunk"        (byte layout, int hash, int request, byte status, int size, int offset,
//                                                 byte[] data)  status: 0 data, 1 gone (no longer shared / changed),
//                                                 2 busy (too many transfers or requests)
// Request = client's counter, echoed in every piece: a piece of an older pick (still on its way) is ignored, never
// taken for a broken download. Server send one transfer per player, ChunkBytes at a time, only while that connection's
// send queue is short enough that ZDOMan still has room after the piece (QueueLimit: world sync keep priority). Client
// check size and hash, keep a session cache (by hash, MaxCacheBytes, cleared at world exit), then SongLibrary.LoadBytes
// parse it like a file. Host / single player: own window list the folder files directly (no transfer). Song id = FNV-1a
// of the bytes: same on every game, new bytes = new song.
// Never trust the wire: requests from unready or incompatible peers dropped before reading, list and song requests on
// own per-minute budgets (over: songs get "busy", far over: dropped); server list clamped (count, size, name: no rich
// text); pieces must come in order, fit the size and match the hash.
internal static class SongShare
{
    internal const byte Layout = 1;
    internal const int MaxSongs = 200;
    internal const int ChunkBytes = 4 * 1024;
    // ZDOMan.SendZDOs send only while it has 2048 of its 10240 free (queue <= 8192): after one piece (+ headers) the
    // queue stay <= 6144, so world data always still go out.
    internal const int SendRoom = 6144;
    internal const int QueueLimit = SendRoom - ChunkBytes - 64;
    internal const byte StatusData = 0;
    internal const byte StatusGone = 1;
    internal const byte StatusBusy = 2;
    private const int MaxNameChars = 80;
    private const long MaxSharedBytes = 64L * 1024 * 1024; // server: files listed at most (bytes in all)
    private const int MaxCacheBytes = 16 * 1024 * 1024;     // client: downloaded songs kept
    private const float ListEvery = 3f;                    // client: list asked again at most this often
    private const float ScanEvery = 2f;                    // server: folder read again at most this often
    private const float StallSeconds = 20f;                // client: no piece this long = download failed
    private const int MaxTransfers = 16;                   // server: transfers at once, all players
    private const int ChunksPerFrame = 4;                  // server: pieces sent per frame, all players
    private const int ListsPerMinute = 30;                 // server: list requests per player
    private const int SongsPerMinute = 30;                 // server: song requests per player (over: "busy")

    internal const string ListRequestRpc = ModInfo.Guid + ".SongListRequest";
    internal const string ListRpc = ModInfo.Guid + ".SongList";
    internal const string RequestRpc = ModInfo.Guid + ".SongRequest";
    internal const string ChunkRpc = ModInfo.Guid + ".SongChunk";

    // Default server folder when the setting is empty (set by Plugin).
    internal static string DefaultFolder = "";

    private static bool _active;

    // ---------- server state ----------

    private sealed class Shared
    {
        internal string Path;
        internal string Name;
        internal int Hash;
        internal int Size;
        internal DateTime Time;
    }

    private sealed class Transfer
    {
        internal ZNetPeer Peer;
        internal int Hash;
        internal int Request;
        internal byte[] Data;
        internal int Offset;
    }

    private static readonly List<Shared> ServerSongs = new List<Shared>();
    private static readonly Dictionary<string, Shared> ScanCache = new Dictionary<string, Shared>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Transfer> Transfers = new List<Transfer>();
    private static readonly Dictionary<long, KeyValuePair<float, int>> ListCounts = new Dictionary<long, KeyValuePair<float, int>>();
    private static readonly Dictionary<long, KeyValuePair<float, int>> SongCounts = new Dictionary<long, KeyValuePair<float, int>>();
    private static float _scannedAt = -999f;
    private static bool _scanDirty = true;
    private static string _scanError;
    private static int _next;

    // ---------- client state ----------

    private sealed class Download
    {
        internal SongEntry Entry;
        internal int Request;
        internal byte[] Buffer;
        internal int Received;
        internal float LastPieceAt;
    }

    private static readonly List<SongEntry> ClientSongs = new List<SongEntry>();
    private static readonly Dictionary<int, SongEntry> ClientByHash = new Dictionary<int, SongEntry>();
    private static readonly List<SongEntry> HostSongs = new List<SongEntry>();
    private static readonly Dictionary<string, SongEntry> HostByPath = new Dictionary<string, SongEntry>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, byte[]> Cache = new Dictionary<int, byte[]>();
    private static readonly List<int> CacheOrder = new List<int>();
    private static int _cacheBytes;
    private static Download _download;
    private static bool _listKnown;
    private static bool _listShared;
    private static float _listAskedAt = -999f;
    private static int _requestId;

    // Window polls these: list changed (rebuild rows), a download moved (refresh details).
    internal static int ListVersion { get; private set; }
    internal static int ProgressVersion { get; private set; }

#if DEBUG
    // Self test: share on and folder without touching the config.
    internal static bool? TestShare;
    internal static string TestFolder;
#endif

    internal static bool Sharing
    {
        get
        {
#if DEBUG
            if (TestShare.HasValue)
            {
                return TestShare.Value;
            }
#endif
            return Plugin.ShareSongs != null && Plugin.ShareSongs.Value;
        }
    }

    internal static string Folder
    {
        get
        {
#if DEBUG
            if (!string.IsNullOrEmpty(TestFolder))
            {
                return TestFolder;
            }
#endif
            var configured = Plugin.ServerSongsFolder != null ? (Plugin.ServerSongsFolder.Value ?? "").Trim() : "";
            return configured.Length > 0 ? configured : DefaultFolder;
        }
    }

    // ZNet.Update postfix: pieces to send (server), a download to watch (client).
    internal static bool HasWork => Transfers.Count > 0 || _download != null;

    // ---------------------------------------------------------------- life

    internal static void Start()
    {
        _active = true;
        _scanDirty = true;
        EnsureFolder();
        var net = ZNet.instance;
        if (net == null)
        {
            return;
        }
        // Turned on mid-session: listen on the connections that exist already.
        var server = net.IsServer();
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.m_rpc != null)
            {
                Register(peer.m_rpc, server);
            }
        }
    }

    internal static void Stop()
    {
        _active = false;
        ServerReset();
        ServerSongs.Clear();
        ScanCache.Clear();
        Reset();
    }

    // World exit (host, single player): transfers and budgets of that session gone.
    internal static void ServerReset()
    {
        Transfers.Clear();
        ListCounts.Clear();
        SongCounts.Clear();
        _next = 0;
        _scanDirty = true;
    }

    // New connection or world exit: the old server's list, download and the session cache are gone.
    internal static void Reset()
    {
        if (_download != null)
        {
            _download.Entry.Pending = null;
            _download = null;
        }
        ClientSongs.Clear();
        ClientByHash.Clear();
        HostSongs.Clear();
        HostByPath.Clear();
        _listKnown = false;
        _listShared = false;
        _listAskedAt = -999f;
        Cache.Clear();
        CacheOrder.Clear();
        _cacheBytes = 0;
        ListVersion++;
    }

    // ShareSongs or ServerSongsFolder changed: read the folder again next time (made now when sharing); sharing off =
    // transfers stop and their players are told at once.
    internal static void ServerSettingsChanged()
    {
        _scanDirty = true;
        HostSongs.Clear();
        HostByPath.Clear();
        EnsureFolder();
        if (!Sharing)
        {
            foreach (var t in Transfers)
            {
                try
                {
                    if (t.Peer != null && t.Peer.m_rpc != null && t.Peer.m_socket != null && t.Peer.m_socket.IsConnected())
                    {
                        t.Peer.m_rpc.Invoke(ChunkRpc, StatusPackage(t.Hash, t.Request, StatusGone));
                    }
                }
                catch (Exception e)
                {
                    PatchGuard.Report("SongShare.ServerSettingsChanged", e);
                }
            }
            Transfers.Clear();
        }
        ListVersion++;
    }

    // Sharing on: the folder exists for the admin to fill (a dedicated server has no window to show where).
    private static void EnsureFolder()
    {
        if (!Sharing)
        {
            return;
        }
        var folder = Folder;
        try
        {
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Log.Info("Made the server songs folder " + folder + ": put the MIDI files to share there.");
            }
        }
        catch (Exception e)
        {
            if (Warned.Add("folder:" + folder))
            {
                Log.Warning("Could not make the server songs folder " + folder + ": " + e.Message);
            }
        }
    }

    // Register replace: safe twice. Server listen for requests; client for list and pieces.
    internal static void Register(ZRpc rpc, bool server)
    {
        if (rpc == null)
        {
            return;
        }
        if (server)
        {
            rpc.Register<ZPackage>(ListRequestRpc, OnListRequest);
            rpc.Register<ZPackage>(RequestRpc, OnSongRequest);
        }
        else
        {
            rpc.Register<ZPackage>(ListRpc, OnList);
            rpc.Register<ZPackage>(ChunkRpc, OnChunk);
        }
    }

    internal static void Update()
    {
        if (Transfers.Count > 0)
        {
            SendPieces();
        }
        var d = _download;
        if (d != null && Time.unscaledTime - d.LastPieceAt > StallSeconds)
        {
            _download = null;
            d.Entry.Pending = "The server stopped sending the song: pick it again.";
            ProgressVersion++;
        }
    }

    // ---------------------------------------------------------------- window side (every game)

    // Songs for the window's "Server songs" section. show false = no section (not shared, list not here yet, no
    // world). hint = one line under the songs (empty folder, folder error), or null.
    internal static List<SongEntry> WindowSongs(out bool show, out string hint)
    {
        show = false;
        hint = null;
        var net = ZNet.instance;
        if (!_active || net == null)
        {
            return ClientSongs;
        }
        if (net.IsServer())
        {
            // Own game is the server (host, single player): the folder's files, read like own MIDI files.
            if (!Sharing)
            {
                return HostSongs;
            }
            show = true;
            ScanIfDue();
            BuildHostSongs();
            if (_scanError != null)
            {
                hint = _scanError;
            }
            else if (HostSongs.Count == 0)
            {
                hint = "Put .mid files in " + Folder + " to share them with every player.";
            }
            return HostSongs;
        }
        show = _listKnown && _listShared;
        if (show && ClientSongs.Count == 0)
        {
            hint = "The server shares no songs yet.";
        }
        return ClientSongs;
    }

    // Song window opened on a client: ask the server for its list (answer come later: ListVersion moves).
    internal static void AskList()
    {
        var net = ZNet.instance;
        if (!_active || net == null || net.IsServer())
        {
            return;
        }
        var now = Time.unscaledTime;
        if (now - _listAskedAt < ListEvery)
        {
            return;
        }
        var server = net.GetServerPeer();
        if (server == null || server.m_rpc == null || !server.IsReady())
        {
            return;
        }
        _listAskedAt = now;
        var pkg = new ZPackage();
        pkg.Write(Layout);
        server.m_rpc.Invoke(ListRequestRpc, pkg);
    }

    // Server song already downloaded this session: read from the cache (no request). True = it is ready.
    internal static bool FromCache(SongEntry e)
    {
        if (e == null || e.Source != SongSource.Server || e.Path != null)
        {
            return false;
        }
        if (e.Score != null)
        {
            return true;
        }
        if (e.Error != null || !Cache.TryGetValue(e.ServerHash, out var cached))
        {
            return false;
        }
        SongLibrary.LoadBytes(e, cached);
        ProgressVersion++;
        return e.Score != null;
    }

    internal static bool IsDownloading(SongEntry e) => e != null && _download != null && _download.Entry == e;

    // A downloaded server song PICKED: from the cache, else ask the server (one download at a time; a new pick
    // replace the old one, on both sides). Host songs (Path set) are files: SongLibrary.Load read them.
    internal static void Fetch(SongEntry e)
    {
        if (!_active || e == null || e.Source != SongSource.Server || e.Path != null || e.Score != null || e.Error != null)
        {
            return;
        }
        if (_download != null && _download.Entry == e)
        {
            return; // already coming
        }
        if (FromCache(e))
        {
            return;
        }
        var net = ZNet.instance;
        var server = net != null ? net.GetServerPeer() : null;
        if (server == null || server.m_rpc == null || !server.IsReady())
        {
            e.Pending = "Not connected to the server.";
            ProgressVersion++;
            return;
        }
        Begin(e);
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(e.ServerHash);
        pkg.Write(_download.Request);
        server.m_rpc.Invoke(RequestRpc, pkg);
    }

    private static void Begin(SongEntry e)
    {
        if (_download != null && _download.Entry != e)
        {
            _download.Entry.Pending = null; // picked again later = asked again
        }
        e.Pending = "Downloading from the server...";
        _download = new Download
        {
            Entry = e,
            Request = ++_requestId,
            Buffer = new byte[e.ServerSize],
            LastPieceAt = Time.unscaledTime,
        };
        ProgressVersion++;
    }

    // ---------------------------------------------------------------- server

    private static void OnListRequest(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            var peer = Sender(rpc);
            if (peer == null || Budget(ListCounts, peer, ListsPerMinute) != 0 || pkg == null || pkg.ReadByte() != Layout)
            {
                return;
            }
            ScanIfDue();
            rpc.Invoke(ListRpc, ListPackage());
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongShare.OnListRequest", e);
        }
    }

    private static void OnSongRequest(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            var peer = Sender(rpc);
            if (peer == null)
            {
                return;
            }
            var budget = Budget(SongCounts, peer, SongsPerMinute);
            if (budget == 2 || pkg == null || pkg.ReadByte() != Layout)
            {
                return; // far over budget: not even a reply
            }
            var hash = pkg.ReadInt();
            var request = pkg.ReadInt();
            if (budget == 1)
            {
                rpc.Invoke(ChunkRpc, StatusPackage(hash, request, StatusBusy)); // no file read
                return;
            }
            // One transfer per player: a new pick replace the old one.
            for (var i = Transfers.Count - 1; i >= 0; i--)
            {
                if (Transfers[i].Peer == peer)
                {
                    Transfers.RemoveAt(i);
                }
            }
            ScanIfDue();
            var song = Sharing ? FindShared(hash) : null;
            if (song == null)
            {
                rpc.Invoke(ChunkRpc, StatusPackage(hash, request, StatusGone));
                return;
            }
            if (Transfers.Count >= MaxTransfers)
            {
                rpc.Invoke(ChunkRpc, StatusPackage(hash, request, StatusBusy));
                return;
            }
            byte[] data;
            try
            {
                data = File.ReadAllBytes(song.Path);
            }
            catch (Exception)
            {
                data = null;
            }
            if (data == null || data.Length != song.Size || MusicMath.Fnv1a(data) != song.Hash)
            {
                // File changed or gone since the list: list again next time, player pick it again.
                _scanDirty = true;
                rpc.Invoke(ChunkRpc, StatusPackage(hash, request, StatusGone));
                return;
            }
            Transfers.Add(new Transfer { Peer = peer, Hash = hash, Request = request, Data = data });
#if DEBUG
            SendsStarted++;
#endif
            Log.Debug($"Sending server song \"{song.Name}\" ({song.Size} bytes) to {PeerName(peer)}.");
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongShare.OnSongRequest", e);
        }
    }

    // Requesting peer when it may ask: server, active, ready, compatible. Else null (nothing read yet).
    private static ZNetPeer Sender(ZRpc rpc)
    {
        var net = ZNet.instance;
        if (!_active || net == null || rpc == null || !net.IsServer())
        {
            return null;
        }
        var peer = net.GetPeer(rpc);
        if (peer == null || !peer.IsReady() || !NetworkGate.PeerCompatible(peer))
        {
            return null;
        }
        return peer;
    }

    // Per player and minute: 0 = within budget, 1 = over (a song request get "busy"), 2 = far over (dropped).
    private static int Budget(Dictionary<long, KeyValuePair<float, int>> counts, ZNetPeer peer, int perMinute)
    {
        var now = Time.unscaledTime;
        if (!counts.TryGetValue(peer.m_uid, out var c) || now - c.Key > 60f)
        {
            if (counts.Count > 256)
            {
                counts.Clear();
            }
            counts[peer.m_uid] = new KeyValuePair<float, int>(now, 1);
            return 0;
        }
        var n = c.Value + 1;
        counts[peer.m_uid] = new KeyValuePair<float, int>(c.Key, n);
        return n <= perMinute ? 0 : n <= 2 * perMinute ? 1 : 2;
    }

    // Send queue short enough for one more piece (Steam: bytes waiting; crossplay: a quarter of the bytes in flight).
    internal static bool CanSend(int sendQueueBytes) => sendQueueBytes <= QueueLimit;

    private static void SendPieces()
    {
        var net = ZNet.instance;
        if (!_active || net == null || !net.IsServer())
        {
            Transfers.Clear();
            return;
        }
        var peers = net.GetPeers();
        var sent = 0;
        var tries = Transfers.Count;
        while (tries-- > 0 && sent < ChunksPerFrame && Transfers.Count > 0)
        {
            if (_next >= Transfers.Count)
            {
                _next = 0;
            }
            var t = Transfers[_next];
            if (t.Peer == null || t.Peer.m_rpc == null || t.Peer.m_socket == null || !peers.Contains(t.Peer)
                || !t.Peer.m_socket.IsConnected())
            {
                Transfers.RemoveAt(_next);
                continue;
            }
            var queue = t.Peer.m_socket.GetSendQueueSize();
#if DEBUG
            MaxQueueSeen = Math.Max(MaxQueueSeen, queue);
#endif
            if (!CanSend(queue))
            {
                _next++;
                continue;
            }
#if DEBUG
            MaxQueueAtSend = Math.Max(MaxQueueAtSend, queue);
            PiecesSent++;
#endif
            t.Peer.m_rpc.Invoke(ChunkRpc, ChunkPackage(t.Hash, t.Request, t.Data, t.Offset));
            t.Offset += Math.Min(ChunkBytes, t.Data.Length - t.Offset);
            sent++;
            if (t.Offset >= t.Data.Length)
            {
                Transfers.RemoveAt(_next);
            }
            else
            {
                _next++;
            }
        }
    }

    // Folder read again when asked for and due (cheap: known files by time and size keep their hash).
    private static void ScanIfDue()
    {
        var now = Time.unscaledTime;
        if (!_scanDirty && now - _scannedAt < ScanEvery)
        {
            return;
        }
        _scanDirty = false;
        _scannedAt = now;
        ServerSongs.Clear();
        _scanError = null;
        if (!Sharing)
        {
            return;
        }
        var folder = Folder;
        try
        {
            var files = SongLibrary.MidiFiles(folder, MaxSongs, out var cut);
            var keep = new Dictionary<string, Shared>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<int>();
            long total = 0;
            foreach (var file in files)
            {
                Shared s;
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists || info.Length <= 0)
                    {
                        continue;
                    }
                    if (info.Length > MidiReader.MaxFileBytes)
                    {
                        if (Warned.Add(file))
                        {
                            Log.Warning($"Server song \"{info.Name}\" is larger than 2 MB: not shared.");
                        }
                        continue;
                    }
                    if (total + info.Length > MaxSharedBytes)
                    {
                        _scanError = "Only the first " + ServerSongs.Count + " songs are shared (64 MB in all).";
                        break;
                    }
                    if (!ScanCache.TryGetValue(file, out s) || s.Time != info.LastWriteTimeUtc || s.Size != info.Length)
                    {
                        var data = File.ReadAllBytes(file);
                        s = new Shared
                        {
                            Path = file,
                            Name = CleanName(System.IO.Path.GetFileNameWithoutExtension(file)),
                            Hash = MusicMath.Fnv1a(data),
                            Size = data.Length,
                            Time = info.LastWriteTimeUtc,
                        };
                    }
                }
                catch (Exception e)
                {
                    // One file (locked while copied, no permission, gone): skipped, read again next time.
                    if (Warned.Add("read:" + file))
                    {
                        Log.Warning($"Server song \"{System.IO.Path.GetFileName(file)}\" could not be read: {e.Message}");
                    }
                    continue;
                }
                keep[file] = s;
                if (!seen.Add(s.Hash))
                {
                    continue; // same bytes twice: listed once
                }
                total += s.Size;
                ServerSongs.Add(s);
            }
            ScanCache.Clear();
            foreach (var pair in keep)
            {
                ScanCache[pair.Key] = pair.Value;
            }
            if (cut && _scanError == null)
            {
                _scanError = "Only the first " + MaxSongs + " songs of the folder are shared.";
            }
        }
        catch (Exception e)
        {
            // Folder itself (missing drive, no permission): nothing listed.
            ServerSongs.Clear();
            _scanError = "Could not read the server songs folder: " + e.Message;
            if (Warned.Add("folder:" + folder))
            {
                Log.Warning(_scanError);
            }
        }
    }

    private static Shared FindShared(int hash)
    {
        foreach (var s in ServerSongs)
        {
            if (s.Hash == hash)
            {
                return s;
            }
        }
        return null;
    }

    // Host / single player: server songs are its folder files (entries kept while path and hash stay).
    private static void BuildHostSongs()
    {
        HostSongs.Clear();
        foreach (var s in ServerSongs)
        {
            if (!HostByPath.TryGetValue(s.Path, out var e) || e.ServerHash != s.Hash)
            {
                e = new SongEntry
                {
                    Id = IdOf(s.Hash),
                    Title = s.Name,
                    Source = SongSource.Server,
                    Path = s.Path,
                    Info = "Server MIDI file, " + Kb(s.Size),
                    ServerHash = s.Hash,
                    ServerSize = s.Size,
                };
                HostByPath[s.Path] = e;
            }
            HostSongs.Add(e);
        }
    }

    internal static ZPackage ListPackage()
    {
        var pkg = new ZPackage();
        pkg.Write(Layout);
        var shared = Sharing;
        pkg.Write(shared);
        var count = shared ? Math.Min(ServerSongs.Count, MaxSongs) : 0;
        pkg.Write(count);
        for (var i = 0; i < count; i++)
        {
            var s = ServerSongs[i];
            pkg.Write(s.Hash);
            pkg.Write(s.Size);
            pkg.Write(s.Name);
        }
        return pkg;
    }

    internal static ZPackage ChunkPackage(int hash, int request, byte[] data, int offset)
    {
        var length = Math.Max(0, Math.Min(ChunkBytes, data.Length - offset));
        var piece = new byte[length];
        Buffer.BlockCopy(data, offset, piece, 0, length);
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(hash);
        pkg.Write(request);
        pkg.Write(StatusData);
        pkg.Write(data.Length);
        pkg.Write(offset);
        pkg.Write(piece);
        return pkg;
    }

    private static ZPackage StatusPackage(int hash, int request, byte status)
    {
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(hash);
        pkg.Write(request);
        pkg.Write(status);
        pkg.Write(0);
        pkg.Write(0);
        pkg.Write(Array.Empty<byte>());
        return pkg;
    }

    // ---------------------------------------------------------------- client

    private static void OnList(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            var net = ZNet.instance;
            if (!_active || net == null || net.IsServer())
            {
                return;
            }
            ReceiveList(pkg);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongShare.OnList", e);
        }
    }

    // Server's list: entries kept by hash (a download or a loaded song survive a new list). Bad list = ignored.
    internal static bool ReceiveList(ZPackage pkg)
    {
        try
        {
            if (pkg == null || pkg.ReadByte() != Layout)
            {
                return false;
            }
            var shared = pkg.ReadBool();
            var count = pkg.ReadInt();
            if (count < 0 || count > MaxSongs)
            {
                return false;
            }
            var fresh = new List<SongEntry>(count);
            for (var i = 0; i < count; i++)
            {
                var hash = pkg.ReadInt();
                var size = pkg.ReadInt();
                var name = CleanName(pkg.ReadString());
                if (size <= 0 || size > MidiReader.MaxFileBytes)
                {
                    continue;
                }
                if (!ClientByHash.TryGetValue(hash, out var e) || e.ServerSize != size)
                {
                    e = new SongEntry
                    {
                        Id = IdOf(hash),
                        Source = SongSource.Server,
                        Info = "Server MIDI, " + Kb(size),
                        ServerHash = hash,
                        ServerSize = size,
                    };
                    ClientByHash[hash] = e;
                }
                e.Title = name;
                if (!fresh.Contains(e))
                {
                    fresh.Add(e);
                }
            }
            ClientSongs.Clear();
            ClientSongs.AddRange(fresh);
            _listShared = shared;
            _listKnown = true;
            ListVersion++;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void OnChunk(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            var net = ZNet.instance;
            if (!_active || net == null || net.IsServer())
            {
                return;
            }
            ReceiveChunk(pkg);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SongShare.OnChunk", e);
        }
    }

    // One piece (or a status) of the song being downloaded. Out of order, too big or for another song = dropped; the
    // last piece checked against the hash, then parsed and cached. False = piece not taken.
    internal static bool ReceiveChunk(ZPackage pkg)
    {
        var d = _download;
        if (pkg == null || d == null)
        {
            return false;
        }
        int hash, request, size, offset;
        byte status;
        byte[] data;
        try
        {
            if (pkg.ReadByte() != Layout)
            {
                return false;
            }
            hash = pkg.ReadInt();
            request = pkg.ReadInt();
            status = pkg.ReadByte();
            size = pkg.ReadInt();
            offset = pkg.ReadInt();
            data = pkg.ReadByteArray();
        }
        catch (Exception)
        {
            return false;
        }
        if (hash != d.Entry.ServerHash || request != d.Request)
        {
            return false; // an older pick's piece (or status), still on its way: not this download's
        }
        if (status != StatusData)
        {
            Fail(d, status == StatusBusy
                ? "The server is busy sending songs: pick it again in a moment."
                : "The server no longer shares this song: open the window again.");
            if (status == StatusGone)
            {
                _listAskedAt = -999f;
            }
            return true;
        }
        if (size != d.Buffer.Length || offset != d.Received || data == null || data.Length == 0 || data.Length > ChunkBytes
            || offset + data.Length > size)
        {
            Fail(d, "The download broke off: pick the song again.");
            return false;
        }
        Buffer.BlockCopy(data, 0, d.Buffer, offset, data.Length);
        d.Received += data.Length;
        d.LastPieceAt = Time.unscaledTime;
        if (d.Received < size)
        {
            d.Entry.Pending = "Downloading from the server ("
                              + (d.Received * 100L / size).ToString(CultureInfo.InvariantCulture) + " %).";
            ProgressVersion++;
            return true;
        }
        _download = null;
        if (MusicMath.Fnv1a(d.Buffer) != hash)
        {
            d.Entry.Pending = "The song came damaged: pick it again.";
            ProgressVersion++;
            return true;
        }
        AddToCache(hash, d.Buffer);
        SongLibrary.LoadBytes(d.Entry, d.Buffer);
        ProgressVersion++;
        return true;
    }

    private static void Fail(Download d, string why)
    {
        if (_download == d)
        {
            _download = null;
        }
        d.Entry.Pending = why;
        ProgressVersion++;
    }

    private static void AddToCache(int hash, byte[] data)
    {
        if (data.Length > MaxCacheBytes || Cache.ContainsKey(hash))
        {
            return;
        }
        while (_cacheBytes + data.Length > MaxCacheBytes && CacheOrder.Count > 0)
        {
            var old = CacheOrder[0];
            CacheOrder.RemoveAt(0);
            if (Cache.TryGetValue(old, out var bytes))
            {
                _cacheBytes -= bytes.Length;
                Cache.Remove(old);
            }
        }
        Cache[hash] = data;
        CacheOrder.Add(hash);
        _cacheBytes += data.Length;
    }

    // ---------------------------------------------------------------- helpers

    internal static string IdOf(int hash) => "server:" + hash.ToString("x8", CultureInfo.InvariantCulture);

    private static string Kb(int bytes) =>
        bytes < 1024 ? bytes + " bytes" : (bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";

    // File name as shown: no control characters, no rich text (TMP tags), not too long, never empty.
    internal static string CleanName(string name)
    {
        var sb = new StringBuilder(Math.Min(name?.Length ?? 0, MaxNameChars));
        if (name != null)
        {
            foreach (var c in name)
            {
                if (sb.Length >= MaxNameChars)
                {
                    break;
                }
                if (char.IsControl(c) || c == '<' || c == '>')
                {
                    continue;
                }
                sb.Append(c);
            }
        }
        var clean = sb.ToString().Trim();
        return clean.Length > 0 ? clean : "Server song";
    }

    private static string PeerName(ZNetPeer peer) =>
        peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : "a player";

#if DEBUG
    internal static int SharedCount => ServerSongs.Count;
    internal static int TransferCount => Transfers.Count;
    internal static int CachedCount => Cache.Count;

    // Self test, client side: downloads asked for since the game started (each pick that needs the server: +1).
    internal static int RequestsMade => _requestId;

    // Self test, client side: the server's list is here, and it says it shares.
    internal static bool ListKnown => _listKnown;
    internal static bool ListShared => _listShared;

    // Self test, server side: transfers started since the game started; hash and size of each shared song by name.
    internal static int SendsStarted;

    // Self test, server side: pieces sent, the longest send queue a piece was sent into (bytes waiting just before),
    // and the longest queue seen while a transfer waited or ran.
    internal static int PiecesSent;
    internal static int MaxQueueAtSend;
    internal static int MaxQueueSeen;

    internal static string DescribeShared()
    {
        var sb = new StringBuilder();
        foreach (var s in ServerSongs)
        {
            sb.Append(s.Name).Append('=').Append(s.Hash.ToString("x8", CultureInfo.InvariantCulture)).Append(':').Append(s.Size).Append(';');
        }
        return sb.ToString();
    }
    internal static IReadOnlyList<SongEntry> ClientList => ClientSongs;

    // Self test: read the folder now.
    internal static void TestScan()
    {
        _scanDirty = true;
        ScanIfDue();
    }

    // Self test: every package the server would send for a song and request (no network), in order.
    internal static List<ZPackage> TestPieces(int hash, int request)
    {
        var list = new List<ZPackage>();
        var song = FindShared(hash);
        if (song == null)
        {
            list.Add(StatusPackage(hash, request, StatusGone));
            return list;
        }
        var data = File.ReadAllBytes(song.Path);
        for (var offset = 0; offset < data.Length; offset += ChunkBytes)
        {
            list.Add(ChunkPackage(hash, request, data, offset));
        }
        return list;
    }

    // Request id of the download in progress (0 = none): test packages for it.
    internal static int TestRequest => _download != null ? _download.Request : 0;

    // Self test: start a download as if the request went out (no network).
    internal static void TestBegin(SongEntry e)
    {
        e.Pending = null;
        Begin(e);
    }

    internal static ZPackage TestStatus(int hash, int request, byte status) => StatusPackage(hash, request, status);

    internal static void TestForget(int hash)
    {
        if (Cache.TryGetValue(hash, out var bytes))
        {
            _cacheBytes -= bytes.Length;
            Cache.Remove(hash);
            CacheOrder.Remove(hash);
        }
    }
#endif
}
