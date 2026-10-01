using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// What server do with one joined player after the grace.
internal enum JoinVerdict : byte
{
    Skip,        // not for me: not server, player gone or not in, already being kicked
    Compatible,  // has me, same network version, not turned off: fine
    Allowed,     // not compatible, but AllowPlayersWithoutMod on: let in, warn
    Refuse,      // not compatible: refuse
}

// Me = server (dedicated or host) refuse players whose game cannot play by the server's rules: no mod, mod turned off,
// or other network version (user rule: mod that change the game is required on every player). Copy of Sneak Ambush's
// check (from Forge Idol Upgrades), with framework verdict NetworkGate.PeerCompatible (has mod + same network version +
// side not off) and reason NetworkGate.PeerProblem. Host own player is not a peer, single player has none: me never
// touch them.
// Flow: ZNet.RPC_PeerInfo postfix on server, peer ready (m_uid set) = fully in -> me wait Grace s (our hello always
// come before PeerInfo on same connection, so grace only safety) -> PeerCompatible. Not compatible and setting off ->
// vanilla "Error" rpc with ErrorVersion (their game show "Incompatible version", go back to menu, like vanilla version
// check) + peer in vanilla kick list (ZNet.Update disconnect it DisconnectDelay s later). Me check early and cut late:
// refused game sit on loading screen (long frames); if socket close before it read Error, vanilla UpdatePeers drop
// the Error and menu say "Disconnected". Many seconds = many frames to read it and log out alone.
// Live re-check: player turn me off (or on) while connected -> framework HelloState -> NetworkGate.PeerStateChanged
// -> me schedule that peer again with same grace. Peer already waiting keep first deadline (off + on within grace =
// pass).
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

    // OnActivated: listen for players who turn me on/off (never twice), check everyone already in.
    internal static void Start()
    {
        _active = true;
        Queue.Clear();
        NetworkGate.PeerStateChanged -= OnPeerStateChanged;
        NetworkGate.PeerStateChanged += OnPeerStateChanged;
        ScheduleAllConnected();
    }

    // OnDeactivated (and game quit): stop listening, pending checks cancelled. Player already refused still go
    // (vanilla kick list).
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

    // RPC_PeerInfo postfix on server (peer ready), and live state change. Same peer twice = keep first deadline.
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

    // Framework event (server): a connected player turned me on or off on their game. Event raised from rpc code:
    // never throw back.
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
                Log.Debug($"{Who(peer)} has {ModInfo.Name} on, with the same network version: allowed.");
                break;
            case JoinVerdict.Allowed:
                // Their game run none of my patches: their bar drain, pop on its own, no key, no income.
                Log.Warning($"{Who(peer)} {Problem(peer)}; AllowPlayersWithoutMod is on, so they may play, but their "
                            + "adrenaline and trinkets work like the normal game (the bar drains, a full bar fires "
                            + "the trinket at once, no income while fighting, no ranged bonus).");
                break;
            case JoinVerdict.Refuse:
                Refuse(peer);
                break;
        }
    }

    private static void Refuse(ZNetPeer peer)
    {
        Log.Warning($"Refused {Who(peer)}: {Problem(peer)}. This server requires {ModInfo.Name} on every player "
                    + "(everyone plays by the same adrenaline and trinket rules). Their game shows \"Incompatible "
                    + "version\". To let such players in, set AllowPlayersWithoutMod = true.");
        // Vanilla client: RPC_Error set connection status, Game.FixedUpdate log out, main menu show the text. Me cut
        // socket only DisconnectDelay s later (vanilla kick use 1 s): client read Error first, even on slow loading.
        peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
        if (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
        {
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + DisconnectDelay;
        }
    }

    // Framework reason; null (became compatible between Decide and log, never expected) = generic text.
    private static string Problem(ZNetPeer peer) => NetworkGate.PeerProblem(peer) ?? "cannot play by this server's rules";

    private static string Who(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }
}
