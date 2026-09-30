using System;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = server rules for everyone (copy of MC Forge Idol Upgrades' ServerRules, design 4). Server (dedicated or host)
// send its TowerRules to each player with the mod; a client use them instead of its own config while connected to
// that server. Single player, host, server, main menu: own config.
//   client -> server  "<guid>.SettingsRequest"  (int layout)            after handshake (RPC_PeerInfo), and when the
//                                                                        client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage TowerRules)   answer; again to all players with the mod
//                                                                        PushDelay s after server's last settings
//                                                                        change, and when server turn on
// Pending policy (Combat mods' shared rule): client of a server with me use NO tower rules (InForce null = vanilla
// towers) until server's rules come, so its own Towers list never unequip anything at join.
// Tower data live in items, so rules in force must be pushed into them: me raise Generation on every change of the
// rules in force (server rules came and differ, own config changed while not a client, session forgotten, test
// override set or cleared); TowerSync apply it PushDelay s after the last change.
// Vanilla peer ignore unknown rpc. Rules belong to the ZNet session they came on: new session = forget them.
// Handlers stay on the rpc after feature off (ZRpc has no unregister): server answer only while Active; client store.
internal static class ServerRules
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";
    internal const float PushDelay = 0.5f;

    private static bool _active;
    private static bool _pushPending;
    private static float _pushDue;
    private static TowerRules _own;
    private static TowerRules _server;
    private static ZNet _session;
    private static string _lastLogged;
    private static bool _badLogged;
    private static int _generation;
    private static float _changedAt = float.MinValue;
#if DEBUG
    private static TowerRules _testRules;
#endif

    internal static bool PushPending => _pushPending;

    // Raised on every change of the rules in force. TowerSync compare it with the one it applied (one int compare).
    internal static int Generation => _generation;

    // Time.unscaledTime of the last Generation raise (TowerSync wait PushDelay after it).
    internal static float ChangedAt => _changedAt;

#if DEBUG
    // Self test force rules (never the config file). Null = normal. Set or clear = new generation.
    internal static TowerRules TestRules
    {
        get => _testRules;
        set
        {
            if (!ReferenceEquals(_testRules, value))
            {
                _testRules = value;
                RaiseGeneration();
            }
        }
    }
#endif

    // Rules in force (design 2.0). Null = client of a server still waiting for the server's rules: vanilla towers.
    // Hot-ish path: static reads and one reference compare, no allocation once own snapshot exist.
    internal static TowerRules InForce
    {
        get
        {
#if DEBUG
            if (_testRules != null)
            {
                return _testRules;
            }
#endif
            var net = ZNet.instance;
            if (net == null || net.IsServer())
            {
                return Own;
            }
            return _server != null && ReferenceEquals(net, _session) ? _server : null;
        }
    }

    // This game's own config snapshot (what a server send, what single player use).
    internal static TowerRules Own => _own ??= TowerRules.Own();

    internal static bool UsingServer
    {
        get
        {
            var net = ZNet.instance;
            return _server != null && net != null && !net.IsServer() && ReferenceEquals(net, _session);
        }
    }

    // Client of a server that no send readable rules yet this session (pending policy).
    internal static bool Waiting => IsClient && !UsingServer;

    private static bool IsClient
    {
        get
        {
            var net = ZNet.instance;
            return net != null && !net.IsServer();
        }
    }

    // Own config changed (any rule setting): new snapshot at next use; server send it again; own rules in force
    // (not a client) = new generation.
    internal static void OwnChanged()
    {
        _own = null;
        MarkChanged();
        if (!IsClient)
        {
            RaiseGeneration();
        }
    }

    // OnActivated. Server: listen on peers that joined while off, send rules to players with the mod. Client already
    // connected: listen and ask (vanilla towers until answer). Client still connecting: RPC_PeerInfo postfix listen
    // and ask.
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
            _pushDue = Time.unscaledTime;
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

    // Server's rules gone (new connection, feature off). Client go back to waiting (vanilla) until they come again.
    internal static void Forget()
    {
        var had = _server != null;
        _server = null;
        _session = null;
        _lastLogged = null;
        _badLogged = false;
        if (had)
        {
            RaiseGeneration();
        }
    }

    // Server side: send own rules to players again, PushDelay s after the last change (one send for many key presses).
    internal static void MarkChanged()
    {
        if (_active)
        {
            _pushPending = true;
            _pushDue = Time.unscaledTime + PushDelay;
        }
    }

    internal static void RaiseGeneration()
    {
        unchecked
        {
            _generation++;
        }
        _changedAt = Time.unscaledTime;
    }

    // ZNet.Update postfix, only when PushPending: at due time send to every ready player with the mod.
    internal static void Update()
    {
        if (Time.unscaledTime < _pushDue)
        {
            return;
        }
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
            Log.Debug($"Sent the tower shield rules to {sent} player(s): {Own.Describe()}.");
        }
    }

    private static ZPackage Package()
    {
        var pkg = new ZPackage();
        Own.Write(pkg);
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
        rpc.Invoke(RequestRpc, TowerRules.Layout);
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
        if (!TowerRules.TryRead(pkg, out var rules, out var clamped))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                Log.Warning("The server sent tower shield rules this version cannot read; "
                            + (_server != null
                                ? "the last rules it sent stay in use."
                                : "tower shields stay normal until it sends readable ones."));
            }
            return false;
        }
        Accept(rules, clamped, net);
        return true;
    }

    // Store rules for this session. Same key as the ones in use = keep the object me have, no new generation
    // (nothing to apply, nothing rebuilt). Receive call me; self test too (it cannot be a client).
    internal static void Accept(TowerRules rules, bool clamped, ZNet session)
    {
        if (_server != null && ReferenceEquals(_session, session) && _server.Key == rules.Key)
        {
            rules = _server;
        }
        else
        {
            _server = rules;
            _session = session;
            RaiseGeneration();
        }
        var text = rules.Describe();
        if (text != _lastLogged)
        {
            _lastLogged = text;
            if (clamped)
            {
                Log.Warning($"The server sent tower shield rules outside the allowed ranges; they were brought back into range: {text}.");
            }
            Log.Info($"Using the server's tower shield rules: {text}. Your own settings apply again in single player and when you host.");
        }
    }
}
