using System;
using System.Collections.Generic;
using HarmonyLib;

namespace MC.Shared;

// Me decide if feature may run in THIS session, based on where it run:
//   Client mod        -> always ok (no patches here at all).
//   Server mod        -> only on host / dedicated server.
//   Both mod          -> on host/server, or on client whose server answered "on" with same network version.
//   SinglePlayer mod  -> only when no other player fully connected.
//
// Hello protocol v1 (per mod, so each mod dll own its rpc names):
//   client -> server  "<guid>.Hello"      "1|<netVersion>|<version>|<on|off>" (right after connect; last field = client
//                                                                            side ready: on, deps ok, no error. Old
//                                                                            copies send no last field = unknown)
//   server -> client  "<guid>.HelloAck"   "1|on|<netVersion>|<version>"      (or "1|off|..." when server copy not Active)
//   client -> server  "<guid>.HelloState" "on" | "off"                       (client side ready changed while connected)
// Server mod code ask PeerCompatible (has mod, same network version, side not off) to refuse players.
// Server also re-send HelloAck to all peers when its copy turn on/off, so clients follow live.
// ZRpc keep order on one connection, and server send its PeerInfo only after ours, so first ack arrive before
// server PeerInfo. No ack by then = server no have mod. Vanilla peer ignore unknown rpc, so vanilla never break.
// While answer pending, client stay ACTIVE (patched): load-time hooks (ZNetScene.Awake, ObjectDB.Awake...) must
// run on clients same as host. Player spawn only after PeerInfo, so no gameplay happen before verdict.
// Static fields ok: one ModPlugin per mod dll, and each dll has own copy of this class.
internal static class NetworkGate
{
    private const string Protocol = "1";

    private enum ServerCheck
    {
        None,
        Pending,
        Present,
        Off,
        Missing,
    }

    private static ModDescriptor _info;
    private static Func<bool> _isActive = () => true;
    private static Func<bool> _isLocalReady = () => true;
    private static ServerCheck _server = ServerCheck.None;
    private static string _serverVersion = "";
    private static int _serverNetVersion;
    private static ZRpc _serverRpc;          // client: connection to server (hello sent on it)
    private static bool? _sentReady;         // client: side-ready value server last heard, this connection
    private static readonly Dictionary<ZRpc, string> ClientHellos = new Dictionary<ZRpc, string>();
    private static readonly Dictionary<ZRpc, bool> ClientReady = new Dictionary<ZRpc, bool>();

    // Server: a connected player's side-ready state changed (HelloState). Mod code re-check that player.
    public static event Action<ZNetPeer> PeerStateChanged;

    private static string HelloRpc => _info.Guid + ".Hello";
    private static string AckRpc => _info.Guid + ".HelloAck";
    private static string StateRpc => _info.Guid + ".HelloState";

    public static void Install(ModDescriptor info, Func<bool> isActive, Func<bool> isLocalReady = null)
    {
        _info = info;
        _isActive = isActive ?? (() => true);
        _isLocalReady = isLocalReady ?? (() => true);
        var needsWorldEvents = info.ServerOnly || info.NeedsServer || info.SinglePlayerOnly;
        if (!needsWorldEvents)
        {
            return;
        }

        // Own harmony id: framework patches stay on even when feature toggled off.
        // All or nothing: half-installed gate = wrong verdicts. Caller turn exception into Error status.
        var h = new Harmony(info.Guid + ".framework");
        try
        {
            var self = typeof(NetworkGate);
            h.Patch(Target(nameof(ZNet.Awake)), postfix: new HarmonyMethod(self, nameof(WorldChanged)));
            h.Patch(Target(nameof(ZNet.OnDestroy)), postfix: new HarmonyMethod(self, nameof(WorldEnded)));
            h.Patch(Target(nameof(ZNet.OnNewConnection)), postfix: new HarmonyMethod(self, nameof(OnNewConnection)));
            h.Patch(Target(nameof(ZNet.RPC_PeerInfo)), postfix: new HarmonyMethod(self, nameof(OnPeerInfo)));
            h.Patch(Target(nameof(ZNet.Disconnect)), postfix: new HarmonyMethod(self, nameof(OnDisconnect)));
        }
        catch
        {
            h.UnpatchSelf();
            throw;
        }
    }

    private static System.Reflection.MethodInfo Target(string name) =>
        AccessTools.Method(typeof(ZNet), name) ?? throw new MissingMethodException(nameof(ZNet), name);

    // Null = network say nothing, feature may run. Else = why not.
    public static (ModState state, string text)? Check(ModDescriptor info)
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return null; // main menu: nothing to gate yet
        }

        var isServer = net.IsServer();
        if (info.ServerOnly && !isServer)
        {
            return (ModState.ServerOnly, "Runs on the server/host only; nothing to do on your side while you are a client.");
        }

        if (info.SinglePlayerOnly && (!isServer || net.GetPeerConnections() > 0))
        {
            return (ModState.SinglePlayerOnly, "Inactive: single-player only (not supported while other players are connected).");
        }

        if (info.NeedsServer && !isServer)
        {
            switch (_server)
            {
                case ServerCheck.Missing:
                    return (ModState.ServerMissing, "Inactive: the server does not have this mod. It must be installed on the server too.");
                case ServerCheck.Off:
                    return (ModState.ServerMissing, "Inactive: the server has this mod turned off (or it is not working there).");
                case ServerCheck.Present when _serverNetVersion != info.NetworkVersion:
                    return (ModState.ServerMismatch,
                        $"Inactive: the server has version {_serverVersion}, you have {info.Version}, and they cannot talk to each other. Use matching versions.");
                default:
                    return null; // present, or still waiting (stay patched so load hooks run)
            }
        }

        return null;
    }

    // ModPlugin call me when this mod's own Active changed. Server tell connected clients.
    public static void OnActiveChanged()
    {
        try
        {
            var net = ZNet.instance;
            if (_info == null || !_info.NeedsServer || net == null || !net.IsServer())
            {
                return;
            }
            var payload = AckPayload();
            foreach (var peer in net.GetPeers())
            {
                peer?.m_rpc?.Invoke(AckRpc, payload);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.OnActiveChanged", e);
        }
    }

    // ModPlugin call me after every refresh. Client connected to server: side ready changed = tell server. Same = one
    // compare. Server, single player, menu: nothing.
    public static void OnLocalReadyChanged()
    {
        try
        {
            if (_info == null || !_info.NeedsServer || _serverRpc == null || _sentReady == null)
            {
                return;
            }
            var ready = _isLocalReady();
            if (ready == _sentReady.Value || !_serverRpc.IsConnected())
            {
                return;
            }
            _sentReady = ready;
            _serverRpc.Invoke(StateRpc, ready ? "on" : "off");
            Log.Info($"Told the server that {_info.Name} is now {(ready ? "on" : "off")} on this game.");
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.OnLocalReadyChanged", e);
        }
    }

    // Mod code can ask: does this connected player run the mod too? (server side, Both mods)
    public static bool PeerHasMod(ZNetPeer peer) => peer?.m_rpc != null && ClientHellos.ContainsKey(peer.m_rpc);

    // Server: network version the player's copy said in hello. -1 = no hello or unreadable.
    public static int PeerNetworkVersion(ZNetPeer peer)
    {
        if (peer?.m_rpc == null || !ClientHellos.TryGetValue(peer.m_rpc, out var hello) || hello == null)
        {
            return -1;
        }
        var parts = hello.Split('|');
        return parts.Length >= 2 && parts[0] == Protocol && int.TryParse(parts[1], out var v) ? v : -1;
    }

    // Server: player's side ready (on, deps ok, no error)? Null = unknown (no hello, or old copy that never say).
    public static bool? PeerReady(ZNetPeer peer)
    {
        if (peer?.m_rpc == null || !ClientReady.TryGetValue(peer.m_rpc, out var ready))
        {
            return null;
        }
        return ready;
    }

    // Server: player can play by this mod's rules: has mod, same network version, own side not turned off.
    // Unknown side state (old copy) count as fine: same as before this check existed.
    public static bool PeerCompatible(ZNetPeer peer) =>
        PeerHasMod(peer) && PeerNetworkVersion(peer) == _info.NetworkVersion && PeerReady(peer) != false;

    // Server: short why-not text for logs. Null = compatible.
    public static string PeerProblem(ZNetPeer peer)
    {
        if (!PeerHasMod(peer))
        {
            return "does not have the mod";
        }
        var net = PeerNetworkVersion(peer);
        if (net != _info.NetworkVersion)
        {
            return $"has another version of the mod (network version {net}, the server has {_info.NetworkVersion})";
        }
        return PeerReady(peer) == false ? "has the mod turned off" : null;
    }

    private static string HelloPayload() =>
        $"{Protocol}|{_info.NetworkVersion}|{_info.Version}|{(_isLocalReady() ? "on" : "off")}";

    private static string AckPayload() => $"{Protocol}|{(_isActive() ? "on" : "off")}|{_info.NetworkVersion}|{_info.Version}";

    private static void WorldChanged()
    {
        try
        {
            FeatureRegistry.RefreshAll();
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.WorldChanged", e);
        }
    }

    private static void WorldEnded()
    {
        _server = ServerCheck.None;
        _serverVersion = "";
        _serverNetVersion = 0;
        _serverRpc = null;
        _sentReady = null;
        ClientHellos.Clear();
        ClientReady.Clear();
        WorldChanged();
    }

    private static void OnNewConnection(ZNetPeer peer)
    {
        try
        {
            if (!_info.NeedsServer || peer?.m_rpc == null)
            {
                return; // Server/SinglePlayer mods: peer not counted until ready (PeerInfo), nothing to do yet
            }

            if (ZNet.instance.IsServer())
            {
                // Server: remember who has the mod, answer with our real state.
                peer.m_rpc.Register<string>(HelloRpc, (rpc, hello) =>
                {
                    ClientHellos[rpc] = hello ?? "";
                    var parts = (hello ?? "").Split('|');
                    if (parts.Length >= 4 && parts[0] == Protocol)
                    {
                        ClientReady[rpc] = parts[3] == "on";
                    }
                    else
                    {
                        ClientReady.Remove(rpc);
                    }
                    rpc.Invoke(AckRpc, AckPayload());
                });
                peer.m_rpc.Register<string>(StateRpc, OnClientState);
            }
            else
            {
                // Client: ask server. First answer must come before server PeerInfo; later answers = live changes.
                _server = ServerCheck.Pending;
                _serverVersion = "";
                _serverNetVersion = 0;
                peer.m_rpc.Register<string>(AckRpc, (rpc, ack) =>
                {
                    ParseAck(ack);
                    WorldChanged();
                });
                _serverRpc = peer.m_rpc;
                _sentReady = _isLocalReady();
                peer.m_rpc.Invoke(HelloRpc, HelloPayload());
                WorldChanged();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.OnNewConnection", e);
        }
    }

    // Server: client side ready changed. Only for peers that said hello (state before hello = ignored).
    private static void OnClientState(ZRpc rpc, string state)
    {
        try
        {
            if (rpc == null || !ClientHellos.ContainsKey(rpc))
            {
                return;
            }
            var ready = state == "on";
            if (ClientReady.TryGetValue(rpc, out var old) && old == ready)
            {
                return;
            }
            ClientReady[rpc] = ready;
            var net = ZNet.instance;
            var peer = net != null ? net.GetPeer(rpc) : null;
            Log.Info($"{(peer != null ? peer.m_playerName : "A player")} turned {_info.Name} {(ready ? "on" : "off")} on their game.");
            if (peer != null)
            {
                PeerStateChanged?.Invoke(peer);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.OnClientState", e);
        }
    }

    private static void ParseAck(string ack)
    {
        // "1|on|<net>|<version>". Unknown protocol = treat as mismatch (net version 0).
        var parts = (ack ?? "").Split('|');
        if (parts.Length >= 4 && parts[0] == Protocol)
        {
            _server = parts[1] == "on" ? ServerCheck.Present : ServerCheck.Off;
            int.TryParse(parts[2], out _serverNetVersion);
            _serverVersion = parts[3];
        }
        else
        {
            _server = ServerCheck.Present;
            _serverNetVersion = 0;
            _serverVersion = ack ?? "?";
        }
        Log.Info($"Server answered for {_info.Name}: {_server}, version {_serverVersion} (network {_serverNetVersion}).");
    }

    private static void OnPeerInfo(ZRpc rpc)
    {
        try
        {
            var net = ZNet.instance;
            if (net == null)
            {
                return;
            }

            if (_info.NeedsServer && !net.IsServer() && _server == ServerCheck.Pending)
            {
                _server = ServerCheck.Missing;
                Log.Warning($"Server does not have {_info.Name}; the feature stays off for this session.");
            }
            else if (_info.NeedsServer && net.IsServer() && rpc != null && !ClientHellos.ContainsKey(rpc))
            {
                // Hello always come before client PeerInfo on same connection. None = vanilla/old client.
                // Only count peers that got in (rejected/password-waiting peers not ready).
                var peer = net.GetPeer(rpc);
                if (peer != null && peer.IsReady())
                {
                    Log.Warning($"{peer.m_playerName} connected without {_info.Name}; its multiplayer part will not work for them.");
                }
            }
            WorldChanged();
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.OnPeerInfo", e);
        }
    }

    private static void OnDisconnect(ZNetPeer peer)
    {
        if (peer?.m_rpc != null)
        {
            ClientHellos.Remove(peer.m_rpc);
            ClientReady.Remove(peer.m_rpc);
        }
        WorldChanged();
    }
}
