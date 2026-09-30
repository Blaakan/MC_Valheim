using System;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// Where the rules in force come from (Debug lines say it).
internal enum RulesSource : byte
{
    Own,       // single player, host, dedicated server: own config
    Server,    // client: the server's rules
    Pending,   // client: server rules not here (yet), or unreadable and none before: moves off
    SelfTest,  // Debug self test override
}

// Me = server rules for everyone (copy of MC Forge Idol Upgrades' flow). Server (dedicated or host) send its MoveRules
// to each player with the mod; a client use ONLY them while connected to that server.
//   client -> server  "<guid>.SettingsRequest"  (int layout)          after handshake (RPC_PeerInfo), and when the
//                                                                      client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage MoveRules)  answer; again to all players with the mod when
//                                                                      server settings change (PushDelay s after last
//                                                                      change) or server turn on
// Change against Forge (design Decision 20, combat mods' pending rule): client of a server never use own config.
// Until server rules come (or when they cannot be read and none came before), both moves off = vanilla swings.
// Single player, host, server: own config. Rules belong to the ZNet session they came on: new session = pending
// again. Vanilla peer ignore unknown rpc. Handlers stay on the rpc after feature off (ZRpc has no unregister): server
// answer only while Active; client store only while Active (copy off = rules forgotten, push ignored; turned on again
// = ask again, moves off until answer).
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
    private static MoveRules _own;
    private static MoveRules _server;
    private static ZNet _session;
    private static string _lastLogged;
    private static bool _badLogged;

    internal static bool PushPending => _pushPending;

#if DEBUG
    // Self test force rules (never the config file). Null = normal.
    internal static MoveRules TestRules { get; set; }
#endif

    // Rules in force. Read at attack start while a move token live: cached objects, few compares.
    internal static MoveRules Current
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
            return Pick(client, _server, ReferenceEquals(net, _session), client ? null : OwnRules());
        }
    }

    // Pure (self test hammer it). Client of a server: only server rules from this ZNet session, else moves off
    // (Decision 20). Single player, host, server: own.
    internal static MoveRules Pick(bool client, MoveRules server, bool sameSession, MoveRules own)
    {
        if (client)
        {
            return server != null && sameSession ? server : MoveRules.Off;
        }
        return own;
    }

    // These rules the server's (warnings then say: ask the server admin)? Reference compare.
    internal static bool FromServer(MoveRules rules) => rules != null && ReferenceEquals(rules, _server);

    internal static RulesSource Source
    {
        get
        {
#if DEBUG
            if (TestRules != null)
            {
                return RulesSource.SelfTest;
            }
#endif
            var net = ZNet.instance;
            if (net != null && !net.IsServer())
            {
                return _server != null && ReferenceEquals(net, _session) ? RulesSource.Server : RulesSource.Pending;
            }
            return RulesSource.Own;
        }
    }

    // Own config changed (any rule setting): new snapshot at next use; server send it again.
    internal static void OwnChanged()
    {
        _own = null;
        MarkChanged();
    }

    // OnActivated. Server: listen on peers that joined while off, send rules to players with the mod. Client already
    // connected: listen and ask (moves off until answer). Client still connecting: RPC_PeerInfo postfix listen and ask.
    internal static void Start()
    {
        _active = true;
        _pushPending = false;
        _changedAt = float.NegativeInfinity; // turned on: push at once, no wait
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

    // OnDeactivated. Client forget server rules: turned on again = ask again, moves off until they come.
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
            _changedAt = Time.unscaledTime;
        }
    }

    // Pure (self test): settings still long enough since last change to push them?
    internal static bool Settled(float now, float changedAt) => now - changedAt >= PushDelay;

    // ZNet.Update postfix, only when PushPending: once settings stayed still PushDelay s, send to every ready player
    // with the mod, once for many changes. Waiting = one float compare per frame.
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
            if (peer != null && peer.m_rpc != null && peer.IsReady() && NetworkGate.PeerHasMod(peer))
            {
                peer.m_rpc.Invoke(SettingsRpc, Package());
                sent++;
            }
        }
        if (sent > 0)
        {
            Log.Debug($"Sent the move settings to {sent} player(s): {OwnRules().Describe()}.");
        }
    }

    private static MoveRules OwnRules() => _own ??= MoveRules.Own();

    private static ZPackage Package()
    {
        var pkg = new ZPackage();
        OwnRules().Write(pkg);
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
        rpc.Invoke(RequestRpc, MoveRules.Layout);
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
        if (!MoveRules.TryRead(pkg, out var rules, out var clamped))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                var keep = _server != null && ReferenceEquals(net, _session);
                Log.Warning("The server sent move settings this version cannot read; "
                            + (keep
                                ? "the last settings it sent stay in use."
                                : "jump and roll attacks stay off until it sends settings this version can read."));
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
                Log.Warning($"The server sent move settings outside the allowed ranges; they were brought back into range: {text}.");
            }
            Log.Info($"Using the server's move settings: {text}. Your own settings apply again in single player and when you host.");
        }
        return true;
    }
}
