using System;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod;

// Me = server rules for everyone (copy of Forge Idol Upgrades' ServerRules, design 4.4). Server (dedicated or host)
// send its MoraleRules to each player with the mod; a client use them instead of its own config while connected to
// that server. Single player, host, server: own config.
//   client -> server  "<guid>.SettingsRequest"  (int layout)             after handshake (RPC_PeerInfo), and when the
//                                                                         client turn on while connected
//   server -> client  "<guid>.Settings"         (ZPackage MoraleRules)   answer; again to all players with the mod
//                                                                         when server settings change or server turn on
// Vanilla peer ignore unknown rpc. Rules belong to the ZNet session they came on: new session = own config again.
// Handlers stay on the rpc after feature off (me not go unregister on every peer): server answer only while Active;
// client ignore rules while off (Start ask again when on). Server push only to peers NetworkGate call compatible.
// Pending (Combat mods' shared rule, design decision 24): client of a server that has not sent readable rules yet
// have NO rules: Decide say Hostile, standing not published. Client never play by own config on a server with me.
// Own edit in a world wait EditSettle s of quiet before it apply (ConfigurationManager set a text setting at every
// key and a slider at every step: else one rebuild, push, publish and log line each).
internal static class ServerRules
{
    internal const string SettingsRpc = ModInfo.Guid + ".Settings";
    internal const string RequestRpc = ModInfo.Guid + ".SettingsRequest";
    internal const float EditSettle = 0.75f;

    private static bool _active;
    private static bool _pushPending;
    private static bool _editPending;
    private static float _editApplyAt;
    private static MoraleRules _own;
    private static MoraleRules _server;
    private static byte[] _serverBytes;
    private static ZNet _session;
    private static string _lastProblems;
    private static bool _badLogged;
    private static int _version;

    // ZNet.Update postfix work: rules push (server) or an own edit waiting to apply.
    internal static bool HasWork => _pushPending || _editPending;

    // Go up at every change of the rules in force (own config, server rules, test rules). Caches keyed on rules
    // (name check, listed tokens) compare it.
    internal static int Version => _version;

#if DEBUG
    private static MoraleRules _testRules;

    // Self test force rules (never the config file, never clamped). Null = normal. Set = rules changed.
    internal static MoraleRules TestRules
    {
        get => _testRules;
        set
        {
            _testRules = value;
            Changed();
        }
    }
#endif

    // Rules in force. Hot path (AI checks): cached objects, one reference compare. While Pending this is own config,
    // but game code must ask Pending first and do vanilla then.
    internal static MoraleRules Current
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
            if (_server != null && net != null && !net.IsServer() && ReferenceEquals(net, _session))
            {
                return _server;
            }
            return Own;
        }
    }

    // Client of a server (any ZNet that is not server) with no readable server rules for this session yet.
    internal static bool Pending
    {
        get
        {
#if DEBUG
            if (_testRules != null)
            {
                return false;
            }
#endif
            var net = ZNet.instance;
            return net != null && !net.IsServer() && !(_server != null && ReferenceEquals(net, _session));
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

    private static MoraleRules Own
    {
        get
        {
            if (_own == null)
            {
                _own = MoraleRules.Own();
                LogProblems(_own, "your settings");
            }
            return _own;
        }
    }

    // Rule setting edited (Plugin.OnSettingChanged). No world, or me off: nothing reads rules, apply now (snapshot
    // rebuilt at first use, silent). In a world while on: apply once edits stop for EditSettle s (Update).
    internal static void OwnEdited()
    {
        if (ZNet.instance == null || !_active)
        {
            _editPending = false;
            OwnChanged();
            return;
        }
        _editPending = true;
        _editApplyAt = UnityEngine.Time.realtimeSinceStartup + EditSettle;
    }

    // Edit still waiting: apply now (world end, turned off), so no stale snapshot stay.
    internal static void FlushEdit()
    {
        if (_editPending)
        {
            _editPending = false;
            OwnChanged();
        }
    }

    // Own config changed (any rule setting): new snapshot at next use; server send it again.
    internal static void OwnChanged()
    {
        _own = null;
        MarkChanged();
        Changed();
    }

    // Rules in force (may have) changed: caches keyed on Version rebuild; own standing published again (listed kill
    // bonuses depend on the rules). Standing.PublishNow do nothing while off or pending.
    private static void Changed()
    {
        _version++;
        if (_active)
        {
            Standing.PublishNow();
        }
    }

    // OnActivated. Server: listen on peers that joined while off, send rules to players with the mod. Client already
    // connected: listen and ask. Client still connecting: RPC_PeerInfo postfix listen and ask.
    internal static void Start()
    {
        _active = true;
        _pushPending = false;
        _version++;
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
        FlushEdit();
        _pushPending = false;
        Forget();
    }

    internal static void Forget()
    {
        _server = null;
        _serverBytes = null;
        _session = null;
        _badLogged = false;
        _version++;
    }

    internal static void MarkChanged()
    {
        if (_active)
        {
            _pushPending = true;
        }
    }

    // ZNet.Update postfix, only when HasWork. Own edit settled -> apply (it mark a push). Push: send to every ready
    // player whose copy can use them, once for many changes (waits while an edit settle: it would send old rules).
    internal static void Update()
    {
        if (_editPending)
        {
            if (UnityEngine.Time.realtimeSinceStartup < _editApplyAt)
            {
                return;
            }
            _editPending = false;
            OwnChanged();
        }
        if (!_pushPending)
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
            Log.Debug($"Sent the creature rules to {sent} player(s): {Own.Describe()}.");
        }
        PrefabTokens.Ensure();
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
        rpc.Invoke(RequestRpc, MoraleRules.Layout);
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
            // Dedicated server has no local player and no creatures: name check of its rules happen here.
            PrefabTokens.Ensure();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerRules.OnRequest", e);
        }
    }

    // Off here = ignore: nothing use them, and Start ask the server again when turned on.
    private static void OnSettings(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            if (_active)
            {
                Receive(pkg);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ServerRules.OnSettings", e);
        }
    }

    // Client: rules from the server. Server / single player / host never take rules from a peer. Unreadable = keep
    // the last good ones; none yet = stay pending (vanilla creatures), never own config.
    internal static bool Receive(ZPackage pkg)
    {
        var net = ZNet.instance;
        if (net == null || net.IsServer())
        {
            return false;
        }
        // Same bytes again (push after a change + answer to our request, server copy turned on again...): keep the
        // object me have, nothing rebuilt, nothing published again.
        var bytes = pkg != null ? pkg.GetArray() : null;
        if (_server != null && ReferenceEquals(net, _session) && SameBytes(bytes, _serverBytes))
        {
            return true;
        }
        if (!MoraleRules.TryRead(pkg, out var rules, out var clamped))
        {
            if (!_badLogged)
            {
                _badLogged = true;
                Log.Warning("The server sent creature rules this version cannot read; "
                            + (_server != null && ReferenceEquals(net, _session)
                                ? "the last rules it sent stay in use."
                                : "creatures behave as in the normal game until it sends readable ones."));
            }
            return false;
        }
        _server = rules;
        _serverBytes = bytes;
        _session = net;
        // New rules (same bytes returned above): logged every time, also when the summary reads the same (a rank
        // list or pack edit that keeps the counts).
        var text = rules.Describe();
        if (clamped)
        {
            Log.Warning($"The server sent creature rules outside the allowed ranges; they were brought back into range: {text}.");
        }
        Log.Info($"Using the server's creature rules: {text}. Your own settings apply again in single player and when you host.");
        LogProblems(rules, "the server's settings");
        Changed();
        return true;
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    // Kill steps or packs me could not read: one Warning per new text.
    private static void LogProblems(MoraleRules rules, string whose)
    {
        var problems = rules.ParseProblems;
        if (problems == null || problems == _lastProblems)
        {
            return;
        }
        _lastProblems = problems;
        Log.Warning($"Some of {whose} could not be read: {problems}.");
    }
}
