using System;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod;

// Me = server rules for everyone (copy of Forge Idol Upgrades' ServerRules, with a pending state). Server (dedicated
// or host) send its AmbushRules to each player with me; a client use them instead of its own config while connected
// to that server. Single player, host, server, main menu: own config.
//   client -> server  "<guid>.SettingsRequest"  (int layout)             after handshake (RPC_PeerInfo), and when the
//                                                                         client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage AmbushRules)   answer; again to all players with me (same
//                                                                         network version) when server settings change
//                                                                         or server turn on
// Different from Forge: client with no server rules yet = AmbushRules.Pending (vanilla), never own config: own numbers
// would reach creatures other games own (stealth factor in ZDO). Unreadable answer = last good server rules stay, or
// still pending when none came.
// Vanilla peer ignore unknown rpc. Rules belong to the ZNet session they came on: new session = pending again.
// Handlers stay on the rpc after feature off: server answer only while Active; client store.
internal static class ServerRules
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";

    private static bool _active;
    private static bool _pushPending;
    private static AmbushRules _own;
    private static AmbushRules _server;
    private static ZNet _session;
    private static string _lastLogged;
    private static bool _badLogged;
    private static int _revision;

    internal static bool PushPending => _pushPending;

    // Go up each time Current may have changed (rules arrived, own config changed, forgot server, test override).
    // Caches (cue texts) compare it; one int read.
    internal static int Revision => _revision;

    // Current may have changed (same moments as Revision). Recipe rebuild listen here. Main thread only.
    internal static event Action Changed;

#if DEBUG
    private static AmbushRules _testRules;
    private static bool _testPending;

    // Self test force rules (never the config file). Null = normal.
    internal static AmbushRules TestRules
    {
        get => _testRules;
        set
        {
            _testRules = value;
            NotifyChanged();
        }
    }

    // Self test force pending (client waiting for server). Win over TestRules.
    internal static bool TestPending
    {
        get => _testPending;
        set
        {
            if (_testPending != value)
            {
                _testPending = value;
                NotifyChanged();
            }
        }
    }
#endif

    // Rules in force. Hot path (AI sense postfix, HUD every frame): cached objects, few compares.
    internal static AmbushRules Current
    {
        get
        {
#if DEBUG
            if (_testPending)
            {
                return AmbushRules.Pending;
            }
            if (_testRules != null)
            {
                return _testRules;
            }
#endif
            var net = ZNet.instance;
            var isClient = net != null && !net.IsServer();
            return Select(isClient, isClient && ReferenceEquals(net, _session) ? _server : null, OwnRules);
        }
    }

    // Client of a server with me, and its rules not here yet.
    internal static bool IsPending => Current.IsPending;

    // Client and server rules of this session in use (logs, self test).
    internal static bool UsingServer
    {
        get
        {
            var net = ZNet.instance;
            return _server != null && net != null && !net.IsServer() && ReferenceEquals(net, _session);
        }
    }

    private static AmbushRules OwnRules => _own ??= AmbushRules.Own();

    // Pure (self test hammer it). Not client (single player, host, dedicated server, menu) = own. Client = server's
    // rules of this session, else pending.
    internal static AmbushRules Select(bool isClient, AmbushRules serverRules, AmbushRules own)
    {
        if (!isClient)
        {
            return own;
        }
        return serverRules ?? AmbushRules.Pending;
    }

    // Own config changed (any rule setting): new snapshot at next use; server send it again.
    internal static void OwnChanged()
    {
        _own = null;
        MarkChanged();
        NotifyChanged();
    }

    // OnActivated. Server: listen on peers that joined while off, send rules to players with me. Client already
    // connected: listen and ask. Client still connecting: RPC_PeerInfo postfix listen and ask.
    internal static void Start()
    {
        _active = true;
        _pushPending = false;
        var net = ZNet.instance;
        if (net == null)
        {
            return;
        }
        if (net.IsServer())
        {
            foreach (var peer in net.GetPeers())
            {
                if (peer != null && peer.m_rpc != null)
                {
                    RegisterServer(peer.m_rpc);
                }
            }
            _pushPending = true;
            return;
        }
        var server = net.GetServerPeer();
        if (server != null && server.m_rpc != null)
        {
            RegisterClient(server.m_rpc, forget: false);
            Request(server.m_rpc);
        }
    }

    internal static void Stop()
    {
        _active = false;
        _pushPending = false;
        Forget();
    }

    // Client back to pending (new connection, feature off). Server rules of old session never come back.
    internal static void Forget()
    {
        var had = _server != null;
        _server = null;
        _session = null;
        _lastLogged = null;
        _badLogged = false;
        if (had)
        {
            NotifyChanged();
        }
    }

    internal static void MarkChanged()
    {
        if (_active)
        {
            _pushPending = true;
        }
    }

    // ZNet.Update postfix, only when PushPending: send to every ready player with me (same network version), once for
    // many changes. Other network version cannot read our layout: they get nothing (their copy is inactive anyway).
    internal static void Update()
    {
        _pushPending = false;
        var net = ZNet.instance;
        if (!_active || net == null || !net.IsServer())
        {
            return;
        }
        var sent = 0;
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.m_rpc != null && peer.IsReady() && NetworkGate.PeerHasMod(peer)
                && NetworkGate.PeerNetworkVersion(peer) == ModInfo.NetworkVersion)
            {
                peer.m_rpc.Invoke(SettingsRpc, Package());
                sent++;
            }
        }
        if (sent > 0)
        {
            Log.Debug($"Sent the {ModInfo.Name} rules to {sent} player(s): {OwnRules.Describe()}.");
        }
    }

    private static ZPackage Package()
    {
        var pkg = new ZPackage();
        OwnRules.Write(pkg);
        return pkg;
    }

    internal static void RegisterServer(ZRpc rpc)
    {
        rpc.Register<int>(RequestRpc, OnRequest);
    }

    internal static void RegisterClient(ZRpc rpc, bool forget)
    {
        if (forget)
        {
            Forget();
        }
        rpc.Register<ZPackage>(SettingsRpc, OnSettings);
    }

    internal static void Request(ZRpc rpc)
    {
        rpc.Invoke(RequestRpc, AmbushRules.Layout);
    }

    private static void OnRequest(ZRpc rpc, int layout)
    {
        try
        {
            var net = ZNet.instance;
            if (!_active || net == null || !net.IsServer() || rpc == null)
            {
                return;
            }
            var peer = net.GetPeer(rpc);
            if (peer == null || !peer.IsReady())
            {
                return;
            }
            if (layout != AmbushRules.Layout)
            {
                // Other layout = other network version: join check handle that player, our package would not read.
                Log.Debug($"{peer.m_playerName} asked for {ModInfo.Name} rules in layout {layout}; this server sends "
                          + $"layout {AmbushRules.Layout}, so it sends nothing.");
                return;
            }
            rpc.Invoke(SettingsRpc, Package());
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerRules.OnRequest", e);
        }
    }

    private static void OnSettings(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            Receive(pkg);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerRules.OnSettings", e);
        }
    }

    // Client: rules from the server. Server / single player / host never take rules from a peer.
    internal static bool Receive(ZPackage pkg)
    {
        var net = ZNet.instance;
        if (net == null || net.IsServer())
        {
            return false;
        }
        if (!AmbushRules.TryRead(pkg, out var rules, out var clamped))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                Log.Warning("The server sent " + ModInfo.Name + " rules this version cannot read; "
                            + (UsingServer
                                ? "the last rules it sent stay in use."
                                : "stealth, smoke and sneak-attack XP work like the normal game until it sends readable ones."));
            }
            return false;
        }
        _server = rules;
        _session = net;
        var text = rules.Describe();
        if (text != _lastLogged)
        {
            _lastLogged = text;
            if (clamped)
            {
                Log.Warning($"The server sent {ModInfo.Name} rules outside the allowed ranges; they were brought back into range: {text}.");
            }
            Log.Info($"Using the server's rules: {text}. Your own settings apply again in single player and when you host.");
        }
        NotifyChanged();
        return true;
    }

    // Raise Changed; a broken listener never reach rpc or config code.
    private static void NotifyChanged()
    {
        _revision++;
        try
        {
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerRules.Changed", e);
        }
    }
}
