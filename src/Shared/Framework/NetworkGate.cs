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
//   client -> server  "<guid>.Hello"     "1|<netVersion>|<version>"         (right after connect)
//   server -> client  "<guid>.HelloAck"  "1|on|<netVersion>|<version>"      (or "1|off|..." when server copy not Active)
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
    private static ServerCheck _server = ServerCheck.None;
    private static string _serverVersion = "";
    private static int _serverNetVersion;
    private static readonly Dictionary<ZRpc, string> ClientHellos = new Dictionary<ZRpc, string>();

    private static string HelloRpc => _info.Guid + ".Hello";
    private static string AckRpc => _info.Guid + ".HelloAck";

    public static void Install(ModDescriptor info, Func<bool> isActive)
    {
        _info = info;
        _isActive = isActive ?? (() => true);
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

    // Mod code can ask: does this connected player run the mod too? (server side, Both mods)
    public static bool PeerHasMod(ZNetPeer peer) => peer?.m_rpc != null && ClientHellos.ContainsKey(peer.m_rpc);

    private static string HelloPayload() => $"{Protocol}|{_info.NetworkVersion}|{_info.Version}";

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
        ClientHellos.Clear();
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
                    rpc.Invoke(AckRpc, AckPayload());
                });
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
                peer.m_rpc.Invoke(HelloRpc, HelloPayload());
                WorldChanged();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("NetworkGate.OnNewConnection", e);
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
        }
        WorldChanged();
    }
}
