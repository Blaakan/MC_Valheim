using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.BreedingStarInheritanceMod;

// What server do with one joined player after the grace.
internal enum JoinVerdict : byte
{
    Skip,     // not for me: not server, player gone or not in, already being kicked
    HasMod,   // their game answered our handshake: fine
    Allowed,  // no mod, but AllowPlayersWithoutMod on: let in, warn
    Refuse,   // no mod: refuse
}

// Me = server (dedicated or host) refuse players whose game no have this mod: their game would breed the animals it
// simulate the vanilla way (50/50), and user want that never. Host own player is not a peer, single player has none:
// me never touch them. Me only see "installed" (framework hello), not "turned on".
// Flow: ZNet.RPC_PeerInfo postfix on server, peer ready (m_uid set) = fully in -> me wait Grace s (our hello always
// come before PeerInfo on same connection, so grace only safety) -> NetworkGate.PeerHasMod. No mod and setting off ->
// vanilla "Error" rpc with ErrorVersion (their game show "Incompatible version", go back to menu, like vanilla version
// check) + peer in vanilla kick list (ZNet.Update disconnect it DisconnectDelay s later). Me check early and cut late:
// refused game sit on loading screen (long frames); if socket close before it read Error, vanilla UpdatePeers drop
// the Error and menu say "Disconnected". Many seconds = many frames to read it and log out alone.
// Timer = ZNet.Update postfix: empty list = one compare.
// Grace + delay must stay below first Procreate tick of object new on their game (m_updateInterval after Awake, 30 s
// boar and hen): self test breeding.network check every species. Their game never run a birth before gone.
// Feature off mid-grace = list cleared (cancel). Feature on later, or setting switched back to refuse = check all.
internal static class PlayerCheck
{
    internal const float GraceSeconds = 1f;
    internal const float DisconnectDelay = 4f;

    private struct Waiting
    {
        internal ZNetPeer Peer;
        internal float Due;
    }

    private static readonly List<Waiting> Queue = new List<Waiting>();
    private static bool _active;

    internal static bool HasWork => Queue.Count > 0;

    // Pure: self test hammer it.
    internal static JoinVerdict Decide(bool isServer, bool connected, bool ready, bool beingKicked, bool hasMod,
        bool allowWithoutMod)
    {
        if (!isServer || !connected || !ready || beingKicked)
        {
            return JoinVerdict.Skip;
        }
        if (hasMod)
        {
            return JoinVerdict.HasMod;
        }
        return allowWithoutMod ? JoinVerdict.Allowed : JoinVerdict.Refuse;
    }

    // OnActivated: server turned on with players already in = check them all.
    internal static void Start()
    {
        _active = true;
        Queue.Clear();
        ScheduleAllConnected();
    }

    // OnDeactivated: pending checks cancelled. Player already refused still go (vanilla kick list).
    internal static void Stop()
    {
        _active = false;
        Queue.Clear();
    }

    // Server: every ready peer checked again after the grace (feature on later, setting back to refuse).
    internal static void ScheduleAllConnected()
    {
        var net = ZNet.instance;
        if (!_active || net == null || !net.IsServer())
        {
            return;
        }
        foreach (var peer in net.GetPeers())
        {
            Schedule(peer);
        }
    }

    // RPC_PeerInfo postfix on server, peer ready. Same peer twice = keep first deadline.
    internal static void Schedule(ZNetPeer peer)
    {
        if (!_active || peer == null || !peer.IsReady())
        {
            return;
        }
        for (var i = 0; i < Queue.Count; i++)
        {
            if (ReferenceEquals(Queue[i].Peer, peer))
            {
                return;
            }
        }
        Queue.Add(new Waiting { Peer = peer, Due = Time.unscaledTime + GraceSeconds });
    }

    // ZNet.Update postfix, only when HasWork.
    internal static void Update()
    {
        var now = Time.unscaledTime;
        for (var i = Queue.Count - 1; i >= 0; i--)
        {
            if (i >= Queue.Count || Queue[i].Due > now)
            {
                continue;
            }
            var peer = Queue[i].Peer;
            Queue.RemoveAt(i);
            Check(peer);
        }
    }

    private static void Check(ZNetPeer peer)
    {
        var net = ZNet.instance;
        var isServer = net != null && net.IsServer();
        // Left before deadline = gone from list (ZNet.Disconnect) or socket closed.
        var connected = isServer && peer != null && net.GetPeers().Contains(peer) && peer.m_rpc != null
                        && peer.m_rpc.IsConnected();
        var ready = connected && peer.IsReady();
        var kicked = connected && ZNet.PeersToDisconnectAfterKick.ContainsKey(peer);
        var hasMod = connected && NetworkGate.PeerHasMod(peer);
        var allow = Plugin.AllowPlayersWithoutMod != null && Plugin.AllowPlayersWithoutMod.Value;
        switch (Decide(isServer, connected, ready, kicked, hasMod, allow))
        {
            case JoinVerdict.HasMod:
                Log.Debug($"{Who(peer)} has Breeding Star Inheritance installed: allowed.");
                break;
            case JoinVerdict.Allowed:
                Log.Warning($"{Who(peer)} joined without Breeding Star Inheritance. AllowPlayersWithoutMod is on, so "
                            + "the animals their game simulates breed the vanilla way (the pregnant parent's own level).");
                break;
            case JoinVerdict.Refuse:
                Refuse(peer);
                break;
        }
    }

    private static void Refuse(ZNetPeer peer)
    {
        Log.Warning($"Refused {Who(peer)}: their game does not run Breeding Star Inheritance, which this server requires "
                    + "on every player (a game without it would breed the animals it simulates the vanilla way). Their "
                    + "game shows \"Incompatible version\". To let such players in, set AllowPlayersWithoutMod = true.");
        // Vanilla client: RPC_Error set connection status, Game.FixedUpdate log out, main menu show the text. Me cut
        // socket only DisconnectDelay s later (vanilla kick use 1 s): client read Error first, even on slow loading.
        peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
        if (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
        {
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + DisconnectDelay;
        }
    }

    private static string Who(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }
}
