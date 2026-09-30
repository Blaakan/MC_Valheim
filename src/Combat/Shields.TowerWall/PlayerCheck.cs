using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// What server do with one joined player after the grace.
internal enum JoinVerdict : byte
{
    Skip,        // not for me: not server, player gone or not in, already being kicked
    Compatible,  // has the mod, same network version, not turned off: fine
    Allowed,     // not compatible, but AllowPlayersWithoutMod on: let in, warn
    Refuse,      // not compatible: refuse
}

// Me = server (dedicated or host) refuse players whose game cannot play by the server's tower rules (design 4, decision
// 24; user rule: mod that change combat is required on every player). Copy of MC Forge Idol Upgrades' check, but the
// verdict come from the framework: NetworkGate.PeerCompatible = has the mod, same ModNetworkVersion, own copy not
// turned off (failed start count as off). NetworkGate.PeerProblem say why not, for the log.
// Host own player is not a peer, single player has none: me never touch them.
// Flow: ZNet.RPC_PeerInfo postfix on server, peer ready (m_uid set) = fully in -> me wait Grace s (our hello always
// come before PeerInfo on same connection, so grace only safety) -> verdict. Not compatible and setting off -> vanilla
// "Error" rpc with ErrorVersion (their game show "Incompatible version", go back to menu, like vanilla version check)
// + peer in vanilla kick list (ZNet.Update disconnect it DisconnectDelay s later). Me check early and cut late:
// refused game sit on loading screen (long frames); if socket close before it read Error, vanilla UpdatePeers drop
// the Error and menu say "Disconnected". Many seconds = many frames to read it and log out alone.
// Player turn the mod off (or on) while connected: framework fire PeerStateChanged, me check that player again after
// a fresh grace (turned back on within it = stay).
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

    // Pure: self test hammer it. Compatible = NetworkGate.PeerCompatible in the game.
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

    // OnActivated: server turned on with players already in = check them all. Listen for players turning the mod
    // on/off (off first: never twice). Event only fire on a server.
    internal static void Start()
    {
        _active = true;
        Queue.Clear();
        NetworkGate.PeerStateChanged -= OnPeerStateChanged;
        NetworkGate.PeerStateChanged += OnPeerStateChanged;
        ScheduleAllConnected();
    }

    // OnDeactivated: pending checks cancelled. Player already refused still go (vanilla kick list).
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

    // RPC_PeerInfo postfix on server, peer ready. Same peer twice = keep first deadline, unless restart (state
    // changed: fresh grace from now, so "off then on again quickly" is not refused).
    internal static void Schedule(ZNetPeer peer, bool restart = false)
    {
        if (!_active || peer == null || !peer.IsReady())
        {
            return;
        }
        var due = Time.unscaledTime + GraceSeconds;
        for (var i = 0; i < Queue.Count; i++)
        {
            if (ReferenceEquals(Queue[i].Peer, peer))
            {
                if (restart)
                {
                    Queue[i] = new Waiting { Peer = peer, Due = due };
                }
                return;
            }
        }
        Queue.Add(new Waiting { Peer = peer, Due = due });
    }

    // Framework: a connected player's own copy turned on or off (HelloState).
    private static void OnPeerStateChanged(ZNetPeer peer)
    {
        try
        {
            Schedule(peer, restart: true);
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
                Log.Debug($"{Who(peer)} runs {ModInfo.Name}: allowed.");
                break;
            case JoinVerdict.Allowed:
                Log.Warning($"{Who(peer)} joined, but their game {Problem(peer)}. AllowPlayersWithoutMod is on, so "
                            + "they may play; for them tower shields are normal shields.");
                break;
            case JoinVerdict.Refuse:
                Refuse(peer);
                break;
        }
    }

    private static void Refuse(ZNetPeer peer)
    {
        Log.Warning($"Refused {Who(peer)}: their game {Problem(peer)}. This server requires {ModInfo.Name} on every "
                    + "player, turned on and at a version that can talk to the server's (everybody plays with the same "
                    + "tower shields). Their game shows \"Incompatible version\". To let such players in, set "
                    + "AllowPlayersWithoutMod = true.");
        // Vanilla client: RPC_Error set connection status, Game.FixedUpdate log out, main menu show the text. Me cut
        // socket only DisconnectDelay s later (vanilla kick use 1 s): client read Error first, even on slow loading.
        peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
        if (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
        {
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + DisconnectDelay;
        }
    }

    // Framework reason ("does not have the mod", "has another version of the mod (...)", "has the mod turned off").
    private static string Problem(ZNetPeer peer) => NetworkGate.PeerProblem(peer) ?? "cannot play with the server's version of the mod";

    private static string Who(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }
}
