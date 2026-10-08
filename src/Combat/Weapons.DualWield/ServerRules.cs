using System;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = server combat rules for everyone (copy of MC Forge Idol Upgrades' ServerRules). Server (dedicated or host)
// send its DualRules to each player with the mod; a client use them instead of its own config while connected.
// Client still waiting for them = built-in defaults (DualRules.Defaults, design 4.3 / D25), never own config.
// Single player, host, server: own config.
//   client -> server  "<guid>.SettingsRequest"  (int layout)            after handshake (RPC_PeerInfo), and when the
//                                                                        client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage DualRules)    answer; again to all players with the mod when
//                                                                        server settings change or server turn on
// Vanilla peer ignore unknown rpc. Rules belong to the ZNet session they came on: new session = wait again.
// Only peers with same network version get rules (other version = other layout, its copy never active here).
// Handlers stay on the rpc after feature off (me choose: client keep server rules for when it turn back on, and log
// about them only while Active; server answer only while Active).
internal static class ServerRules
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";

    private static bool _active;
    private static bool _pushPending;
    private static DualRules _own;
    private static DualRules _server;
    private static ZNet _session;
    private static string _lastLogged;
    private static bool _badLogged;

    internal static bool PushPending => _pushPending;

#if DEBUG
    // Self test force rules (never the config file). Null = normal.
    internal static DualRules TestRules;

    // Self test read me: rules text of the last "Using the server's dual wielding rules" line (null = none logged on
    // this session, or since the feature went off).
    internal static string LastLogged => _lastLogged;
#endif

    // Rules in force. Hot path (every equip, every swing, Player.Update compare): cached objects only, so same rules =
    // same reference.
    internal static DualRules Current
    {
        get
        {
#if DEBUG
            if (TestRules != null)
            {
                return TestRules;
            }
#endif
            var net = ZNet.instance;
            var client = net != null && !net.IsServer();
            return Pick(client, client && ReferenceEquals(net, _session) ? _server : null, _own ??= DualRules.Own());
        }
    }

    // Pure choice behind Current (self test hammer it). Client of a server: server rules, else built-in defaults while
    // they no come yet. Anything else (single player, host, dedicated server): own config.
    internal static DualRules Pick(bool clientOfServer, DualRules serverRules, DualRules own)
    {
        if (!clientOfServer)
        {
            return own;
        }
        return serverRules ?? DualRules.Defaults;
    }

    internal static bool UsingServer
    {
        get
        {
            var net = ZNet.instance;
            return _server != null && net != null && !net.IsServer() && ReferenceEquals(net, _session);
        }
    }

    // Own config changed (any rule setting): new snapshot at next use; server send it again.
    internal static void OwnChanged()
    {
        _own = null;
        MarkChanged();
    }

    // OnActivated. Server: listen on peers that joined while off, send rules to players with the mod. Client already
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

    internal static void Forget()
    {
        _server = null;
        _session = null;
        _lastLogged = null;
        _badLogged = false;
    }

    internal static void MarkChanged()
    {
        if (_active)
        {
            _pushPending = true;
        }
    }

    // ZNet.Update postfix, only when PushPending: send to every ready player with the mod, once for many changes.
    // Delivery only (SameVersion): a copy turned off keep server rules for when it turn back on. Verdict = PlayerCheck.
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
            if (peer != null && peer.m_rpc != null && peer.IsReady() && SameVersion(peer))
            {
                peer.m_rpc.Invoke(SettingsRpc, Package());
                sent++;
            }
        }
        if (sent > 0)
        {
            Log.Debug($"Sent the dual wielding rules to {sent} player(s): {Current.Describe()}.");
        }
    }

    // Peer said hello with my network version (hello come before its PeerInfo, so before any request). Other version
    // = copy that cannot read my layout and never run here: nothing to send it.
    private static bool SameVersion(ZNetPeer peer) =>
        NetworkGate.PeerHasMod(peer) && NetworkGate.PeerNetworkVersion(peer) == ModInfo.NetworkVersion;

    private static ZPackage Package()
    {
        var pkg = new ZPackage();
        (_own ??= DualRules.Own()).Write(pkg);
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
        rpc.Invoke(RequestRpc, DualRules.Layout);
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
            if (peer == null || !peer.IsReady() || !SameVersion(peer))
            {
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

    // Client: rules from the server. Server / single player / host never take rules from a peer. Copy not Active
    // (turned off while connected; handler stay registered) = store silently: log line only when they are used.
    internal static bool Receive(ZPackage pkg)
    {
        var net = ZNet.instance;
        if (net == null || net.IsServer())
        {
            return false;
        }
        if (!DualRules.TryRead(pkg, out var rules, out var clamped))
        {
            if (_active && !_badLogged)
            {
                _badLogged = true;
                Log.Warning("The server sent dual wielding rules this version cannot read; "
                            + (_server != null && ReferenceEquals(net, _session)
                                ? "the last rules it sent stay in use."
                                : "the built-in default rules are used until it sends readable ones."));
            }
            return false;
        }
        // Same values again (server resend after its own change elsewhere): keep old object, caches stay.
        if (_server == null || !ReferenceEquals(net, _session) || !_server.SameAs(rules))
        {
            _server = rules;
        }
        _session = net;
        if (!_active)
        {
            return true;
        }
        var text = rules.Describe();
        if (text != _lastLogged)
        {
            _lastLogged = text;
            if (clamped)
            {
                Log.Warning($"The server sent dual wielding rules outside the allowed ranges; they were brought back into range: {text}.");
            }
            Log.Info($"Using the server's dual wielding rules: {text}. Your own settings apply again in single player and when you host.");
        }
        return true;
    }
}
