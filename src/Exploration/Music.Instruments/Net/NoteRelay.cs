using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the note stream over the network. One plain ZRpc "<guid>.Notes" (ZPackage NoteBatch) both ways:
//   performer's game -> server     its batches (a client sends to the server peer; the host relays itself)
//   server -> listener's game      each batch, only to players with a compatible copy of the mod within relay range
// Server stamps who plays from the socket (sender's character ZDOID, only when that ZDO is owned by the sender's own
// game, and that ZDO's position: a game cannot speak for another player), caps the rate per sender, drops notes far from
// their batch's send time, lets an Encore through only from a live (mini-game) batch and at most once per
// 0.8 x SuccessSeconds per sender, and plays the batch for its own local player when it is a host in range.
// Single player: nothing sent (only the performer hears). Dedicated server: relay only, no sound.
// Registered on every connection (ZNetPatches: OnNewConnection, RPC_PeerInfo; Start for a mod turned on mid-session).
// Handlers stay on the rpc after feature off: they act only while Active.
internal static class NoteRelay
{
    internal const string NotesRpc = ModInfo.Guid + ".Notes";
    internal const float RangeMargin = 10f;      // server positions are up to ~100 ms (ZDO) or 2 s (refPos) old
    private const int MaxBatchesPerSecond = 40;

    private static bool _active;
    private static readonly NoteBatch Incoming = new NoteBatch();
    private static readonly Dictionary<ZNetPeer, int> Counts = new Dictionary<ZNetPeer, int>();
    private static readonly Dictionary<long, float> LastEncore = new Dictionary<long, float>();
    // Per sender: current performance id, when it last changed (a new id at most once a second), its End seen.
    private struct SenderId
    {
        internal int Id;
        internal float ChangedAt;
        internal bool Ended;
    }

    private static readonly Dictionary<long, SenderId> CurrentId = new Dictionary<long, SenderId>();
    private const float NewIdGap = 1f;
    private static float _countWindow;
    private static bool _rateLogged;

#if DEBUG
    // Self test: batches seen (sent, relayed, received) since start.
    internal static int SentCount;
    internal static int RelayedCount;
    internal static int ReceivedCount;
#endif

    internal static void Start()
    {
        _active = true;
        Counts.Clear();
        LastEncore.Clear();
        CurrentId.Clear();
        var net = ZNet.instance;
        if (net == null)
        {
            return;
        }
        // Turned on mid-session: listen on the connections that exist already.
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.m_rpc != null)
            {
                Register(peer.m_rpc);
            }
        }
    }

    internal static void Stop()
    {
        _active = false;
        Counts.Clear();
        LastEncore.Clear();
        CurrentId.Clear();
    }

    // Register replace: safe twice.
    internal static void Register(ZRpc rpc)
    {
        rpc?.Register<ZPackage>(NotesRpc, OnNotes);
    }

    // Range the server forwards notes to: whoever may hear them or get the Encore.
    internal static float RelayRange(MusicRules rules) => Mathf.Max(rules.HearingRange, rules.BonusRange) + RangeMargin;

    // Performer's game: send one batch (already filled). Client -> server; host -> relay now; single player -> nothing.
    internal static void Send(NoteBatch batch)
    {
        var net = ZNet.instance;
        if (!_active || net == null || batch == null)
        {
            return;
        }
        if (net.IsServer())
        {
            if (net.GetPeers().Count == 0)
            {
                return;
            }
            Forward(null, batch);
            return;
        }
        var server = net.GetServerPeer();
        if (server == null || server.m_rpc == null)
        {
            return;
        }
        server.m_rpc.Invoke(NotesRpc, batch.ToPackage());
#if DEBUG
        SentCount++;
#endif
    }

    private static void OnNotes(ZRpc rpc, ZPackage pkg)
    {
        try
        {
            var net = ZNet.instance;
            if (!_active || net == null || rpc == null)
            {
                return;
            }
            if (net.IsServer())
            {
                // Sender checked before anything is parsed: an unready, incompatible or flooding connection costs one
                // lookup.
                var sender = net.GetPeer(rpc);
                if (sender == null || !sender.IsReady() || !NetworkGate.PeerCompatible(sender) || !Allow(sender))
                {
                    return;
                }
                if (!NoteBatch.TryRead(pkg, Incoming, readPerformer: false) || !IdAllowed(sender, Incoming))
                {
                    return;
                }
                // Who plays and where: from the sender's own character, never from the packet. Vanilla takes the
                // character id a game sends without checks: the character must be owned by it.
                if (sender.m_characterID.IsNone())
                {
                    return;
                }
                var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(sender.m_characterID) : null;
                if (zdo == null || zdo.GetOwner() != sender.m_uid)
                {
                    return;
                }
                Incoming.Performer = sender.m_characterID;
                Incoming.Position = zdo.GetPosition();
                Sanitize(sender, Incoming);
                Forward(sender, Incoming);
                return;
            }
            if (!NoteBatch.TryRead(pkg, Incoming))
            {
                return;
            }
#if DEBUG
            ReceivedCount++;
#endif
            Listeners.Receive(Incoming);
        }
        catch (Exception e)
        {
            PatchGuard.Report("NoteRelay.OnNotes", e);
        }
    }

    // Server: a new performance id from a sender at most once a second (each new id makes every listener in range
    // set up a new sound source). The current id pass up to its End, nothing after it: an honest game never sends
    // more under an id it ended (next song = new id), and a restart under the same id would make listeners set up
    // a new sound source each time.
    private static bool IdAllowed(ZNetPeer sender, NoteBatch batch)
    {
        var now = Time.unscaledTime;
        if (CurrentId.TryGetValue(sender.m_uid, out var current))
        {
            if (current.Id == batch.Performance)
            {
                if (current.Ended)
                {
                    return false;
                }
                if (batch.IsEnd)
                {
                    current.Ended = true;
                    CurrentId[sender.m_uid] = current;
                }
                return true;
            }
            if (now - current.ChangedAt < NewIdGap)
            {
                return false;
            }
        }
        if (CurrentId.Count > 256)
        {
            CurrentId.Clear();
        }
        CurrentId[sender.m_uid] = new SenderId { Id = batch.Performance, ChangedAt = now, Ended = batch.IsEnd };
        return true;
    }

    // Server: notes far from the batch's send time dropped (they would keep listeners' sound alive for nothing); Encore
    // only from a live batch, once per 0.8 x SuccessSeconds per sender (autoplay never earns one).
    private static void Sanitize(ZNetPeer sender, NoteBatch batch)
    {
        for (var i = batch.Notes.Count - 1; i >= 0; i--)
        {
            var t = batch.Notes[i].Time;
            if (t < batch.SentAt - Listeners.NoteWindowBefore || t > batch.SentAt + Listeners.NoteWindowAfter)
            {
                batch.Notes.RemoveAt(i);
            }
        }
        if (!batch.IsEncore)
        {
            return;
        }
        var now = Time.unscaledTime;
        var gap = ServerRules.Current.SuccessSeconds * Listeners.EncoreGapShare;
        if (!batch.Live || (LastEncore.TryGetValue(sender.m_uid, out var last) && now - last < gap))
        {
            batch.Flags &= ~BatchFlags.Encore;
            return;
        }
        if (LastEncore.Count > 256)
        {
            LastEncore.Clear();
        }
        LastEncore[sender.m_uid] = now;
    }

    // Server (dedicated or host): to every compatible peer in range but the sender; and to the host's own player.
    private static void Forward(ZNetPeer sender, NoteBatch batch)
    {
        var net = ZNet.instance;
        var rules = ServerRules.Current;
        if (rules.IsPending)
        {
            return;
        }
        var local = Player.m_localPlayer;
        if (sender == null)
        {
            // Host's own performance: stamp it here.
            if (local == null)
            {
                return;
            }
            batch.Performer = local.GetZDOID();
            batch.Position = local.transform.position;
        }
        var range = RelayRange(rules);
        var rangeSq = range * range;
        ZPackage pkg = null;
        foreach (var peer in net.GetPeers())
        {
            if (peer == null || ReferenceEquals(peer, sender) || peer.m_rpc == null || !peer.IsReady()
                || !NetworkGate.PeerCompatible(peer))
            {
                continue;
            }
            var where = PeerPosition(peer);
            if ((where - batch.Position).sqrMagnitude > rangeSq)
            {
                continue;
            }
            pkg ??= batch.ToPackage();
            peer.m_rpc.Invoke(NotesRpc, pkg);
#if DEBUG
            RelayedCount++;
#endif
        }
        // Host listening to a client's performance.
        if (sender != null && local != null && local.GetZDOID() != batch.Performer)
        {
            Listeners.Receive(batch);
        }
    }

    private static Vector3 PeerPosition(ZNetPeer peer)
    {
        if (!peer.m_characterID.IsNone() && ZDOMan.instance != null)
        {
            var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (zdo != null)
            {
                return zdo.GetPosition();
            }
        }
        return peer.m_refPos;
    }

    // Per sender, per second: MaxBatchesPerSecond (a broken or hostile game cannot flood the others).
    private static bool Allow(ZNetPeer sender)
    {
        var now = Time.unscaledTime;
        if (now - _countWindow >= 1f)
        {
            _countWindow = now;
            Counts.Clear();
        }
        Counts.TryGetValue(sender, out var count);
        if (count >= MaxBatchesPerSecond)
        {
            if (!_rateLogged)
            {
                _rateLogged = true;
                Log.Warning($"{sender.m_playerName} sent more than {MaxBatchesPerSecond} note batches in a second; "
                            + "the extra ones are dropped.");
            }
            return false;
        }
        Counts[sender] = count + 1;
        return true;
    }
}
