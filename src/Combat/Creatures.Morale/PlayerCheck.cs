using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// What server do with one joined player after the grace.
internal enum JoinVerdict : byte
{
    Skip,        // not for me: not server, player gone or not in, already being kicked
    Compatible,  // their game has me, same network version, turned on: fine
    Allowed,     // not compatible, but AllowPlayersWithoutMod on: let in, warn
    Refuse,      // not compatible: refuse
}

// Me = server (dedicated or host) refuse players whose game cannot play by this server's creature rules (design 4.5):
// no mod, other network version, or mod turned off there (creature brains run on whichever game owns the creature,
// so such a game would make creatures attack everyone the vanilla way). Copy of Forge Idol Upgrades' check, but the
// verdict come from the framework: NetworkGate.PeerCompatible (has mod, same network version, not turned off) and the
// why from NetworkGate.PeerProblem. Me never duplicate that test here.
// Host own player is not a peer, single player has none: me never touch them.
// When: peer ready (ZNet.RPC_PeerInfo postfix), peer turn me on or off while connected (NetworkGate.PeerStateChanged),
// server copy turn on (Start: every ready peer), AllowPlayersWithoutMod back to off (every ready peer). Each one wait
// the same Grace; peer already waiting keep its deadline; check read state at the deadline (off and on again inside
// the grace = nothing).
// Refuse = vanilla "Error" rpc with ErrorVersion (their game show "Incompatible version", go back to menu) + peer in
// vanilla kick list (ZNet.Update disconnect it DisconnectDelay s later: slow loading game still read the Error first).
// Timer = ZNet.Update postfix: empty list = one compare. Feature off = list cleared, event unsubscribed.
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

    // Pure: self test hammer it. Why a player is not compatible is the framework's business (PeerCompatible).
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

    // OnActivated: listen to players turning me on/off (off first: never twice), server turned on with players
    // already in = check them all.
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

    // Same peer twice = keep first deadline.
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

    // Framework (server): a connected player's copy turned on or off. Same grace as a join.
    private static void OnPeerStateChanged(ZNetPeer peer)
    {
        try
        {
            var net = ZNet.instance;
            if (_active && net != null && net.IsServer())
            {
                Schedule(peer);
            }
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
                Log.Debug($"{Who(peer)} has {ModInfo.Name} turned on: allowed.");
                break;
            case JoinVerdict.Allowed:
                Log.Warning($"{Who(peer)}: their game {Problem(peer)}. AllowPlayersWithoutMod is on, so they may play: "
                            + "creatures behave as in the normal game toward them, and the creatures their game "
                            + "controls attack everyone the normal way.");
                break;
            case JoinVerdict.Refuse:
                Refuse(peer);
                break;
        }
    }

    private static void Refuse(ZNetPeer peer)
    {
        Log.Warning($"Refused {Who(peer)}: their game {Problem(peer)}. {ModInfo.Name} is required on every player of "
                    + "this server (the creatures their game controls must follow the same rules). Their game shows "
                    + "\"Incompatible version\". To let such players in, set AllowPlayersWithoutMod = true.");
        // Vanilla client: RPC_Error set connection status, Game.FixedUpdate log out, main menu show the text. Me cut
        // socket only DisconnectDelay s later (vanilla kick use 1 s): client read Error first, even on slow loading.
        peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
        if (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
        {
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + DisconnectDelay;
        }
    }

    // Framework text ("does not have the mod", "has the mod turned off"...). Null only when compatible.
    private static string Problem(ZNetPeer peer) => NetworkGate.PeerProblem(peer) ?? "cannot play by this server's rules";

    private static string Who(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }
}
