using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// What server do with one joined player after the grace.
internal enum JoinVerdict : byte
{
    Skip,        // not for me: not server, player gone or not in, already being kicked
    Compatible,  // their game run me: has mod, same network version, not turned off (framework say)
    Allowed,     // their game no run me, but AllowPlayersWithoutMod on: let in, warn
    Refuse,      // their game no run me: refuse
}

// Me = server (dedicated or host) refuse players whose game no run this mod: every player of a server fight with
// same rules (MC rule: mod that change the game is required on every player). Copy of MC Forge Idol Upgrades' check,
// but verdict from framework: NetworkGate.PeerCompatible (has mod, same ModNetworkVersion, own side not off; copy
// blocked by other dual wield mod count as off through LocalBlocker). Reason for log: NetworkGate.PeerProblem.
// Host own player is not a peer, single player has none: me never touch them.
// Flow: ZNet.RPC_PeerInfo postfix on server, peer ready (m_uid set) = fully in -> me wait Grace s (framework hello
// always come before PeerInfo on same connection, so grace only safety) -> PeerCompatible. Not compatible and setting
// off -> vanilla "Error" rpc with ErrorVersion (their game show "Incompatible version", go back to menu, like vanilla
// version check) + peer in vanilla kick list (ZNet.Update disconnect it DisconnectDelay s later). Me check early and
// cut late: refused game sit on loading screen (long frames); if socket close before it read Error, vanilla
// UpdatePeers drop the Error and menu say "Disconnected". Many seconds = many frames to read it and log out alone.
// Player turn me on or off while connected: framework HelloState -> PeerStateChanged -> me schedule that peer with
// same grace (check read state at deadline: off then on again inside grace = stay).
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

    // OnActivated: listen for players turning me on/off (remove first: never twice), check everyone already in.
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
        NetworkGate.PeerStateChanged -= OnPeerStateChanged;
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

    // RPC_PeerInfo postfix on server (peer ready), PeerStateChanged. Same peer twice = keep first deadline.
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

    // Framework event (server): a connected player's copy turned on or off. Me check them again after the grace.
    private static void OnPeerStateChanged(ZNetPeer peer)
    {
        try
        {
            var net = ZNet.instance;
            if (net != null && net.IsServer())
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
        var verdict = Decide(isServer, connected, ready, kicked, compatible, allow);
#if DEBUG
        LastVerdict = verdict;
        VerdictCount++;
        LastText = "";
#endif
        switch (verdict)
        {
            case JoinVerdict.Compatible:
                Log.Debug($"{Who(peer)} runs {ModInfo.Name}: allowed.");
                break;
            case JoinVerdict.Allowed:
                // Copy off because of another dual wield mod report "turned off" too: that mod may still give them
                // pairs, by its own rules. Text say so, never "cannot dual wield".
                var text = AllowedText(Who(peer), Problem(peer));
#if DEBUG
                LastText = text;
#endif
                Log.Warning(text);
                break;
            case JoinVerdict.Refuse:
                Refuse(peer);
                break;
        }
    }

#if DEBUG
    // Self test (server half) read me: verdict of the last join check, how many checks ran this session, and the
    // warning the last check logged ("" = none).
    internal static JoinVerdict LastVerdict;
    internal static int VerdictCount;
    internal static string LastText = "";
#endif

    // Pure (self test check the wording): server log line for a player let in although their game no run me.
    internal static string AllowedText(string who, string problem) =>
        $"{who} plays without {ModInfo.Name}: their game {problem}. "
        + "AllowPlayersWithoutMod is on, so they may play, without this mod's rules (with another "
        + "dual wield mod installed they may still dual wield, by that mod's rules).";

    // Pure (self test check the wording): server log line for a refused player.
    internal static string RefusedText(string who, string problem) =>
        $"Refused {who}: their game {problem}. This server requires {ModInfo.Name} on every "
        + "player (everyone fights with the same rules). Their game shows \"Incompatible version\". "
        + "To let such players in, set AllowPlayersWithoutMod = true.";

    private static void Refuse(ZNetPeer peer)
    {
        var text = RefusedText(Who(peer), Problem(peer));
#if DEBUG
        LastText = text;
#endif
        Log.Warning(text);
        // Vanilla client: RPC_Error set connection status, Game.FixedUpdate log out, main menu show the text. Me cut
        // socket only DisconnectDelay s later (vanilla kick use 1 s): client read Error first, even on slow loading.
        peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
        if (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
        {
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + DisconnectDelay;
        }
    }

    // Framework reason ("does not have the mod", "has the mod turned off", "has another version ..."). Null only when
    // compatible: never here, but no crash.
    private static string Problem(ZNetPeer peer) => NetworkGate.PeerProblem(peer) ?? "does not run the mod";

    private static string Who(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }
}
