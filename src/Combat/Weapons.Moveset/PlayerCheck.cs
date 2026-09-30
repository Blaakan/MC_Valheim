using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// What server do with one joined player after the grace.
internal enum JoinVerdict : byte
{
    Skip,        // not for me: not server, player gone or not in, already being kicked
    Compatible,  // has the mod, same network version, not turned off on their game: fine
    Allowed,     // not compatible, but AllowPlayersWithoutMod on: let in, warn
    Refuse,      // not compatible: refuse
}

// Me = server (dedicated or host) refuse players whose game would not fight by the server's moves: no mod, other
// network version, or mod turned off on their game (MC rule: mod that change combat is required on every player).
// Copy of MC Forge Idol Upgrades' check with two changes (design section 4): verdict = NetworkGate.PeerCompatible
// (Forge still ask PeerHasMod), and a player who turn the mod on or off while connected (NetworkGate.PeerStateChanged,
// from their HelloState) get checked again after same grace. Same contract as the sibling combat mods: every check
// decide with PeerCompatible, log PeerProblem, re-check on PeerStateChanged: player refused by all for same reasons.
// Host own player is not a peer, single player has none: me never touch them.
// Flow: ZNet.RPC_PeerInfo postfix on server, peer ready (m_uid set) = fully in -> me wait Grace s (our hello always
// come before PeerInfo on same connection, so grace only safety) -> PeerCompatible. Not compatible and setting off ->
// vanilla "Error" rpc with ErrorVersion (their game show "Incompatible version", go back to menu, like vanilla version
// check) + peer in vanilla kick list (ZNet.Update disconnect it DisconnectDelay s later). Me check early and cut late:
// refused game sit on loading screen (long frames); if socket close before it read Error, vanilla UpdatePeers drop
// the Error and menu say "Disconnected". Many seconds = many frames to read it and log out alone.
// Timer = ZNet.Update postfix: empty list = one compare.
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
    internal static JoinVerdict Decide(bool isServer, bool connected, bool ready, bool beingKicked, bool compatible,
        bool allowWithoutMod)
    {
        if (!isServer || !connected || !ready || beingKicked)
        {
            return JoinVerdict.Skip;
        }
        if (compatible)
        {
            return JoinVerdict.Compatible;
        }
        return allowWithoutMod ? JoinVerdict.Allowed : JoinVerdict.Refuse;
    }

    // OnActivated: listen for players turning the mod on/off; server turned on with players already in = check all.
    internal static void Start()
    {
        _active = true;
        Queue.Clear();
        NetworkGate.PeerStateChanged -= OnPeerStateChanged;
        NetworkGate.PeerStateChanged += OnPeerStateChanged;
        ScheduleAllConnected();
    }

    // OnDeactivated: stop listening, pending checks cancelled. Player already refused still go (vanilla kick list).
    internal static void Stop()
    {
        _active = false;
        NetworkGate.PeerStateChanged -= OnPeerStateChanged;
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

    // RPC_PeerInfo postfix on server (peer ready), or player's copy turned on/off. Same peer twice = keep first
    // deadline (check read the state at the deadline anyway).
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

    // Framework event (server side, from the player's HelloState): same grace, then same check.
    private static void OnPeerStateChanged(ZNetPeer peer)
    {
        try
        {
            Schedule(peer);
        }
        catch (Exception e)
        {
            PatchGuard.Report("PlayerCheck.OnPeerStateChanged", e);
        }
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
        var compatible = connected && NetworkGate.PeerCompatible(peer);
        var allow = Plugin.AllowPlayersWithoutMod != null && Plugin.AllowPlayersWithoutMod.Value;
        switch (Decide(isServer, connected, ready, kicked, compatible, allow))
        {
            case JoinVerdict.Compatible:
                Log.Debug($"{Who(peer)} has {ModInfo.Name}: allowed.");
                break;
            case JoinVerdict.Allowed:
                // Not "joined": a re-check after PeerStateChanged land here too.
                Log.Warning($"Not refusing {Who(peer)}: their game {Problem(peer)}, but AllowPlayersWithoutMod is on. "
                            + "Their own jumps and rolls stay normal (no jump or roll attacks).");
                break;
            case JoinVerdict.Refuse:
                Refuse(peer);
                break;
        }
    }

    private static void Refuse(ZNetPeer peer)
    {
        Log.Warning($"Refused {Who(peer)}: their game {Problem(peer)}, and this server requires {ModInfo.Name} on every "
                    + "player (everybody fights with the same moves). Their game shows \"Incompatible version\". "
                    + "To let such players in, set AllowPlayersWithoutMod = true.");
        // Vanilla client: RPC_Error set connection status, Game.FixedUpdate log out, main menu show the text. Me cut
        // socket only DisconnectDelay s later (vanilla kick use 1 s): client read Error first, even on slow loading.
        peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
        if (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
        {
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + DisconnectDelay;
        }
    }

    // Framework reason ("does not have the mod", "has another version of the mod (...)", "has the mod turned off").
    private static string Problem(ZNetPeer peer) => NetworkGate.PeerProblem(peer) ?? "cannot play by this server's moves";

    private static string Who(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }
}
