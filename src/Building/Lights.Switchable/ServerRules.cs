using System;
using MC.Shared;

namespace MC.Building.LightsSwitchableMod;

// Me = server rules for everyone (same flow as MC Breeding's ServerSettings). Server (dedicated or host) send its
// LightsRules to each player with the mod; a client use them instead of its own config while connected to that server.
// Single player, host, server: own config.
//   client -> server  "<guid>.SettingsRequest"  (int layout)            after handshake (RPC_PeerInfo), and when the
//                                                                        client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage LightsRules)   answer; again to all players with the mod when
//                                                                        server settings change or server turn on
// Vanilla peer ignore unknown rpc. Rules belong to the ZNet session they came on: new session = own config again.
// Handlers stay on the rpc after feature off (ZRpc has no unregister): server answer only while Active; client store.
internal static class ServerRules
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";

    private static bool _active;
    private static bool _pushPending;
    private static LightsRules _own;
    private static LightsRules _server;
    private static ZNet _session;
    private static string _lastLogged;
    private static bool _badLogged;

    internal static bool PushPending => _pushPending;

#if DEBUG
    // Self test force rules (never the config file). Null = normal.
    internal static LightsRules TestRules;
#endif

    // Rules in force. Hot path (tooltips, UI every frame): cached objects, one reference compare.
    internal static LightsRules Current
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
            if (_server != null && net != null && !net.IsServer() && ReferenceEquals(net, _session))
            {
                return _server;
            }
            return _own ??= LightsRules.Own();
        }
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
        Changed();
    }

    // Rules in force changed: lights already in the world follow at once (only while the feature is on).
    private static void Changed()
    {
        if (_active)
        {
            LiveLights.ApplyAll();
        }
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
            if (peer != null && peer.m_rpc != null && peer.IsReady() && NetworkGate.PeerHasMod(peer))
            {
                peer.m_rpc.Invoke(SettingsRpc, Package());
                sent++;
            }
        }
        if (sent > 0)
        {
            Log.Debug($"Sent the light rules to {sent} player(s): {Current.Describe()}.");
        }
    }

    private static ZPackage Package()
    {
        var pkg = new ZPackage();
        (_own ??= LightsRules.Own()).Write(pkg);
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
        rpc.Invoke(RequestRpc, LightsRules.Layout);
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
        if (!LightsRules.TryRead(pkg, out var rules))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                Log.Warning("The server sent light rules this version cannot read; "
                            + (_server != null ? "the last rules it sent stay in use." : "your own settings are used until it sends readable ones."));
            }
            return false;
        }
        _server = rules;
        _session = net;
        var text = rules.Describe();
        if (text != _lastLogged)
        {
            _lastLogged = text;
            Log.Info($"Using the server's {text}. Your own setting applies again in single player and when you host.");
        }
        Changed();
        return true;
    }
}
