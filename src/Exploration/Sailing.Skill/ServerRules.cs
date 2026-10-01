using System;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Me = server rules for everyone (copy of Sneak Ambush's ServerRules, with Weapon Moveset's push debounce and _active
// guard, and Creature Morale's push filter). Server (dedicated or host) send its SailingRules to each player with me;
// a client use them instead of its own config while connected to that server. Single player, host, server, main
// menu: own config.
//   client -> server  "<guid>.SettingsRequest"  (int layout)              after handshake (RPC_PeerInfo), and when the
//                                                                          client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage SailingRules)   answer; again to all compatible players when
//                                                                          server settings change (PushDelay s after
//                                                                          last change) or server turn on
// Client with no server rules yet = SailingRules.Pending (vanilla ships, no XP), never own config: own numbers would
// steer ships other players ride (the owner may be anyone aboard). Unreadable answer = last good server rules stay, or
// still pending when none came. Vanilla peer ignore unknown rpc. Rules belong to the ZNet session they came on: new
// session = pending again. Handlers stay on the rpc after feature off (ZRpc has no unregister): server answer only
// while Active; client store only while Active (copy off = rules forgotten, push ignored; turned on again = ask
// again, pending until answer).
internal static class ServerRules
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";

    // Server push own rules only when settings stayed still this long (Time.unscaledTime): slider drag in
    // ConfigurationManager change value every frame, me send once after, not once per frame to every player.
    internal const float PushDelay = 0.5f;

    private static bool _active;
    private static bool _pushPending;
    private static float _changedAt = float.NegativeInfinity;
    private static SailingRules _own;
    private static SailingRules _server;
    private static ZNet _session;
    private static string _lastLogged;
    private static bool _badLogged;

    internal static bool PushPending => _pushPending;

#if DEBUG
    // Self test force rules (never the config file). Null = normal.
    internal static SailingRules TestRules { get; set; }

    // Self test force pending (client waiting for server). Win over TestRules.
    internal static bool TestPending { get; set; }
#endif

    // Rules in force. Hot path (ship physics every fixed step): cached objects, few compares.
    internal static SailingRules Current
    {
        get
        {
#if DEBUG
            if (TestPending)
            {
                return SailingRules.Pending;
            }
            if (TestRules != null)
            {
                return TestRules;
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

    private static SailingRules OwnRules => _own ??= SailingRules.Own();

    // Pure (self test hammer it). Not client (single player, host, dedicated server, menu) = own. Client = server's
    // rules of this session, else pending.
    internal static SailingRules Select(bool isClient, SailingRules serverRules, SailingRules own)
    {
        if (!isClient)
        {
            return own;
        }
        return serverRules ?? SailingRules.Pending;
    }

    // Pure (self test): settings still long enough since last change to push them?
    internal static bool Settled(float now, float changedAt) => now - changedAt >= PushDelay;

    // Own config changed (any rule setting): new snapshot at next use; server send it again once settled.
    internal static void OwnChanged()
    {
        _own = null;
        MarkChanged();
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
            _changedAt = float.NegativeInfinity;
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
            _changedAt = Time.unscaledTime;
        }
    }

    // ZNet.Update postfix, only when PushPending: once settings stayed still PushDelay s, send to every ready
    // compatible player, once for many changes. Waiting = one float compare per frame.
    internal static void Update()
    {
        var net = ZNet.instance;
        if (!_active || net == null || !net.IsServer())
        {
            _pushPending = false;
            return;
        }
        if (!Settled(Time.unscaledTime, _changedAt))
        {
            return;
        }
        _pushPending = false;
        var sent = 0;
        foreach (var peer in net.GetPeers())
        {
            // Compatible = has me, same network version, copy not off: others would log rules they cannot read, or
            // take rules while off. They ask again when they turn on (Start).
            if (peer != null && peer.m_rpc != null && peer.IsReady() && NetworkGate.PeerCompatible(peer))
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
        rpc.Invoke(RequestRpc, SailingRules.Layout);
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
            if (layout != SailingRules.Layout)
            {
                // Other layout = other network version: join check handle that player, our package would not read.
                Log.Debug($"{peer.m_playerName} asked for {ModInfo.Name} rules in layout {layout}; this server sends "
                          + $"layout {SailingRules.Layout}, so it sends nothing.");
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
            // Own copy off (turned off, other network version): no store, no "Using the server's" line.
            if (!_active)
            {
                return;
            }
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
        if (!SailingRules.TryRead(pkg, out var rules, out var clamped))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                Log.Warning("The server sent " + ModInfo.Name + " rules this version cannot read; "
                            + (UsingServer
                                ? "the last rules it sent stay in use."
                                : "ships sail like in the normal game and give no Sailing XP until it sends readable ones."));
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
        return true;
    }
}
