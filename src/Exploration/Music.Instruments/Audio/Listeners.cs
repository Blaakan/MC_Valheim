using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = what this game hears of other players: one Remote per (performer, performance id) with its emitter on that
// player (or at the packet's position when the player is not loaded here), a jitter buffer, the pose pulses, and the
// Encore check. Main thread only (NoteRelay hands batches in; Player.Update postfix calls Update).
// Jitter buffer: the performer's clock is mapped to our audio clock by an anchor set on the first batch
// (base anchor = now - sentAt + delay; delay 0.25 s for live mini-game notes, 0.15 s for autoplay, which already sends
// ahead); the base follows the fastest batch seen since (a first batch that came late is caught up). A batch that comes
// too late for its slot pushes the anchor later, but never more than MaxDelay past the base; a longer stall (crossplay
// resends after 1-3 s) keeps the anchor: notes more than LateDrop seconds in the past are dropped instead of all
// sounding at once, and the next batches play on time again. While batches come early the anchor creeps back toward the
// base (CreepRate of real time at most: no rush heard). The performer's clock never jumps within a performance and a
// batch goes out at least every second, so the anchor is never set again: a performance that timed out (stall) or was
// let go out of range and comes back keeps its timing.
// Never trust the stream: nothing more is taken under a performance id after its End, notes far from their batch's
// send time are dropped, a performer keeps one live performance
// (a new id lets the old one go; two slots at most with its ringing tail), at most MaxRemotes are heard at once (a
// newcomer takes a far one's slot only when clearly closer), an Encore from the same performer counts once per
// 0.8 x SuccessSeconds. A performance times out 3 s after its last batch, but never before its last note has sounded.
// Also the game music duck: while a performance within hearing range is heard (or our own one plays), game music fades
// to the personal GameMusicVolume (MusicVolume.UpdateProximityVolumes postfix reads Duck).
internal static class Listeners
{
    internal const float LiveDelay = 0.25f;
    internal const float AutoDelay = 0.15f;
    internal const float MaxDelay = 0.75f;
    internal const float LateDrop = 0.1f;
    internal const int MaxRemotes = 8;
    internal const float NoteWindowBefore = 2f;  // note may sound this long before its batch was sent (live: ~0)
    internal const float NoteWindowAfter = 3f;   // or this long after (autoplay sends 0.5 s ahead)
    internal const float EncoreGapShare = 0.8f;
    private const float Timeout = 3f;
    private const float CreepBack = 0.01f;      // seconds the anchor comes back per early batch, at most
    private const float CreepRate = 0.03f;      // and at most this share of the time since the batch before
    private static readonly Vector3 ChestOffset = new Vector3(0f, 1.3f, 0.2f);

    private struct Pulse
    {
        internal double At;
        internal byte Pitch;
        internal byte Velocity;
    }

    private sealed class Remote
    {
        internal ZDOID Performer;
        internal int Performance;
        internal InstrumentKind Kind;
        internal Emitter Emitter;
        internal Player Player;
        internal Vector3 Position;
        internal double Anchor;
        internal double BaseAnchor;
        internal double LastBatchDsp;
        internal double SoundUntil;  // end of the last note scheduled (timeout waits for it)
        internal float LastHeard;
        internal bool Ended;
        internal bool EndSent;       // ended by the performer's End flag (not by a timeout): nothing more under this id
        internal double EndAt;
        internal bool Released;
        internal bool Retired;       // replaced by a restart of the same performance: Find skips it
        internal readonly List<Pulse> Pulses = new List<Pulse>();
    }

    private static readonly List<Remote> Remotes = new List<Remote>();
    private static readonly Dictionary<ZDOID, float> LastEncore = new Dictionary<ZDOID, float>();
    private static float _duck = 1f;

#if DEBUG
    internal static int RemoteCount => Remotes.Count;
    internal static int NotesScheduled;
    internal static int NotesDropped;
    internal static int Encores;

    internal static Emitter FirstEmitter => Remotes.Count > 0 ? Remotes[0].Emitter : null;

    // Extra delay the jitter buffer added to the newest remote (anchor - base anchor), seconds.
    internal static float NewestExtraDelay => Remotes.Count > 0
        ? (float)(Remotes[Remotes.Count - 1].Anchor - Remotes[Remotes.Count - 1].BaseAnchor)
        : 0f;

    internal static void ForgetEncores() => LastEncore.Clear();
#endif

    // Game music multiplier (1 = untouched).
    internal static float Duck => _duck;

    // A performance not let go yet, within hearing range of our player.
    internal static bool AnyHeard
    {
        get
        {
            var local = Player.m_localPlayer;
            if (local == null)
            {
                return false;
            }
            var range = ServerRules.Current.HearingRange;
            var here = local.transform.position;
            foreach (var r in Remotes)
            {
                if (!r.Released && (Where(r) - here).sqrMagnitude <= range * range)
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal static void Receive(NoteBatch batch)
    {
        var local = Player.m_localPlayer;
        if (local == null || batch == null || local.GetZDOID() == batch.Performer)
        {
            return;
        }
        var rules = ServerRules.Current;
        if (rules.IsPending)
        {
            return;
        }
        var player = FindPlayer(batch.Performer);
        var position = player != null ? player.transform.position : batch.Position;
        // Encore first: also a game without sound (or out of hearing range but in bonus range) gets the effect.
        if (batch.IsEncore && MusicBonus.InRange(local, position) && EncoreAllowed(batch.Performer, rules))
        {
#if DEBUG
            Encores++;
#endif
            MusicBonus.Grant(local, fromOther: true);
        }
        var now = AudioKit.DspNow;
        var remote = Find(batch.Performer, batch.Performance);
        Remote inherit = null;
        if (remote != null && (remote.Ended || remote.Released))
        {
            // Ended by its End flag: nothing more under this id (a game picks a new id for each song). A late End for
            // one that timed out: same.
            if (remote.EndSent || batch.IsEnd)
            {
                remote.EndSent = true;
                return;
            }
            // Timed out (stall) or let go out of range: starts over in a new remote on the old timing (fresh emitter,
            // music fade, End and timeout work again); the old one rings out.
            Release(remote, now);
            remote.Retired = true;
            inherit = remote;
            remote = null;
        }
        var distance = (local.transform.position - position).magnitude;
        if (remote == null)
        {
            if (batch.IsEnd || batch.Notes.Count == 0 || distance > rules.HearingRange + 5f || !AudioKit.Available)
            {
                return;
            }
            // One live performance per performer (its old one let go, an older tail of theirs freed: two slots at most),
            // and a few at most: a newcomer only takes a slot from a far one when clearly closer.
            Remote tail = null;
            foreach (var r in Remotes)
            {
                if (r.Performer == batch.Performer)
                {
                    Release(r, now);
                    if (tail == null || r.LastHeard > tail.LastHeard)
                    {
                        tail = r;
                    }
                }
            }
            for (var i = Remotes.Count - 1; i >= 0; i--)
            {
                if (Remotes[i].Performer == batch.Performer && Remotes[i] != tail)
                {
                    Drop(i);
                }
            }
            if (Remotes.Count >= MaxRemotes && !TryMakeRoom(distance))
            {
                return;
            }
            remote = new Remote
            {
                Performer = batch.Performer,
                Performance = batch.Performance,
                Kind = batch.Instrument,
            };
            if (inherit != null)
            {
                remote.Anchor = inherit.Anchor;
                remote.BaseAnchor = inherit.BaseAnchor;
                remote.LastBatchDsp = inherit.LastBatchDsp;
            }
            Remotes.Add(remote);
        }
        else if (distance > rules.HearingRange + 5f)
        {
            // Walked out of hearing (the server relays a bit farther, for the Encore): let go.
            remote.Position = position;
            Release(remote, now);
            return;
        }
        remote.Player = player;
        remote.Position = position;
        remote.LastHeard = Time.unscaledTime;
        EnsureEmitter(remote, rules);

        var delay = batch.Live ? LiveDelay : AutoDelay;
        // Most urgent time of the batch: its earliest good note (autoplay notes lead the send time), else the send time.
        var earliest = batch.SentAt;
        var anyNote = false;
        foreach (var note in batch.Notes)
        {
            if (note.Time >= batch.SentAt - NoteWindowBefore && note.Time <= batch.SentAt + NoteWindowAfter
                && (!anyNote || note.Time < earliest))
            {
                earliest = note.Time;
                anyNote = true;
            }
        }
        var onTime = now - System.Math.Min(batch.SentAt, earliest) + delay;
        if (remote.LastBatchDsp <= 0)
        {
            remote.BaseAnchor = onTime;
            remote.Anchor = onTime;
        }
        else
        {
            if (onTime < remote.BaseAnchor)
            {
                remote.BaseAnchor = onTime;
            }
            // Slack before the batch's most urgent note would play. Too little: push the anchor later while the extra
            // delay stays within MaxDelay; past that, keep it (late notes drop, the next batches are on time). Plenty of
            // slack again: creep back toward the base anchor.
            var slack = remote.Anchor + earliest - now;
            if (slack < 0.03)
            {
                var pushed = remote.Anchor + (0.03 - slack) + 0.05;
                if (pushed - remote.BaseAnchor <= MaxDelay - delay)
                {
                    remote.Anchor = pushed;
                }
            }
            else if (slack > delay + 0.1 && remote.Anchor > remote.BaseAnchor)
            {
                var step = System.Math.Min(CreepBack, CreepRate * (now - remote.LastBatchDsp));
                remote.Anchor = System.Math.Max(remote.BaseAnchor, remote.Anchor - step);
            }
        }
        remote.LastBatchDsp = now;

        // A late note on is moved to now; a note off after it moves by as much (a quick tap keeps its note off).
        var shift = 0.0;
        foreach (var note in batch.Notes)
        {
            // Note far from its batch's send time: broken or hostile stream (it would keep the emitter alive).
            if (note.Time < batch.SentAt - NoteWindowBefore || note.Time > batch.SentAt + NoteWindowAfter)
            {
#if DEBUG
                NotesDropped++;
#endif
                continue;
            }
            var start = remote.Anchor + note.Time;
            if (note.Velocity == 0)
            {
                // Note off (free play held note): end it there (late = now). No pulse, no new sound.
                if (remote.Emitter != null)
                {
                    remote.Emitter.EndNote(Math.Max(start + shift, now), note.Pitch);
                }
                continue;
            }
            if (start < now - LateDrop)
            {
#if DEBUG
                NotesDropped++;
#endif
                continue;
            }
            if (start < now)
            {
                shift = Math.Max(shift, now - start);
                start = now;
            }
            if (remote.Emitter != null)
            {
                remote.Emitter.Schedule(start, remote.Anchor + note.Time + note.Length, note.Pitch, note.Velocity);
                remote.SoundUntil = System.Math.Max(remote.SoundUntil, remote.Anchor + note.Time + note.Length);
#if DEBUG
                NotesScheduled++;
#endif
            }
            if (remote.Pulses.Count < 256)
            {
                remote.Pulses.Add(new Pulse { At = start, Pitch = note.Pitch, Velocity = note.Velocity });
            }
        }
        if (batch.IsEnd && !remote.Ended)
        {
            remote.Ended = true;
            remote.EndSent = true;
            remote.EndAt = remote.Anchor + batch.SentAt;
        }
    }

    // Same performer's Encore again too soon: ignored (a performer earns one per SuccessSeconds of good play).
    private static bool EncoreAllowed(ZDOID performer, MusicRules rules)
    {
        var now = Time.unscaledTime;
        if (LastEncore.TryGetValue(performer, out var last) && now - last < rules.SuccessSeconds * EncoreGapShare)
        {
            return false;
        }
        if (LastEncore.Count > 64)
        {
            LastEncore.Clear();
        }
        LastEncore[performer] = now;
        return true;
    }

    // Player.Update postfix (local player), every frame: pulses due, ends, timeouts, clean-up, music duck.
    internal static void Update()
    {
        var now = AudioKit.DspNow;
        var unscaled = Time.unscaledTime;
        for (var i = Remotes.Count - 1; i >= 0; i--)
        {
            var r = Remotes[i];
            if (r.Emitter != null)
            {
                r.Emitter.CheckHealth();
            }
            // Player left the loaded area or was destroyed: emitter went with it.
            if (r.Player == null && r.Emitter == null)
            {
                Remotes.RemoveAt(i);
                continue;
            }
            for (var p = 0; p < r.Pulses.Count;)
            {
                if (r.Pulses[p].At <= now)
                {
                    if (r.Player != null)
                    {
                        InstrumentPose.Pulse(r.Player, r.Kind, r.Pulses[p].Pitch, r.Pulses[p].Velocity);
                    }
                    r.Pulses.RemoveAt(p);
                }
                else
                {
                    p++;
                }
            }
            if (!r.Ended && unscaled - r.LastHeard > Timeout && now > r.SoundUntil + 0.5)
            {
                r.Ended = true;
                r.EndAt = now;
            }
            if (r.Ended && !r.Released && now >= r.EndAt)
            {
                Release(r, now);
            }
            if (r.Released && (r.Emitter == null || r.Emitter.Silent))
            {
                if (r.Emitter != null)
                {
                    r.Emitter.Destroy();
                }
                Remotes.RemoveAt(i);
            }
        }
        UpdateDuck(Time.unscaledDeltaTime);
    }

    // Let go: queued notes dropped, tails ring, emitter freed once silent (Update).
    private static void Release(Remote r, double now)
    {
        if (r.Released)
        {
            return;
        }
        r.Ended = true;
        r.EndAt = now;
        r.Released = true;
        r.Pulses.Clear();
        if (r.Emitter != null)
        {
            r.Emitter.ReleaseAll();
        }
    }

    // All slots taken: a tail still ringing goes first (oldest); else the farthest live one, but only when the newcomer
    // (at distance) is clearly closer (margin: two far performers never swap slots back and forth). False = no room.
    private static bool TryMakeRoom(float distance)
    {
        var oldestReleased = -1;
        for (var i = 0; i < Remotes.Count; i++)
        {
            if (Remotes[i].Released && (oldestReleased < 0 || Remotes[i].LastHeard < Remotes[oldestReleased].LastHeard))
            {
                oldestReleased = i;
            }
        }
        if (oldestReleased >= 0)
        {
            Drop(oldestReleased);
            return true;
        }
        var local = Player.m_localPlayer;
        if (local == null)
        {
            return false;
        }
        var here = local.transform.position;
        var farthest = -1;
        var farDistance = 0f;
        for (var i = 0; i < Remotes.Count; i++)
        {
            var d = (Where(Remotes[i]) - here).magnitude;
            if (d > farDistance)
            {
                farDistance = d;
                farthest = i;
            }
        }
        if (farthest < 0 || distance + 8f >= farDistance)
        {
            return false;
        }
        Drop(farthest);
        return true;
    }

    // Gone now (sound cut): slot freed.
    private static void Drop(int index)
    {
        var r = Remotes[index];
        if (r.Emitter != null)
        {
            r.Emitter.Hush();
            r.Emitter.Destroy();
        }
        Remotes.RemoveAt(index);
    }

    private static void UpdateDuck(float dt)
    {
        var heard = AnyHeard || Performance.Mode != PerformanceMode.None;
        var target = heard && Plugin.GameMusicVolume != null ? Plugin.GameMusicVolume.Value : 1f;
        _duck = Mathf.MoveTowards(_duck, target, dt * 0.8f);
    }

    // Volume setting changed: every emitter follows.
    internal static void ApplyVolume()
    {
        foreach (var r in Remotes)
        {
            if (r.Emitter != null)
            {
                r.Emitter.ApplyVolume();
            }
        }
    }

    // Feature off, world exit: silence and destroy everything.
    internal static void Shutdown()
    {
        foreach (var r in Remotes)
        {
            if (r.Emitter != null)
            {
                r.Emitter.Hush();
                r.Emitter.Destroy();
            }
        }
        Remotes.Clear();
        LastEncore.Clear();
        _duck = 1f;
    }

    private static Vector3 Where(Remote r) => r.Player != null ? r.Player.transform.position : r.Position;

    private static Remote Find(ZDOID performer, int performance)
    {
        foreach (var r in Remotes)
        {
            if (r.Performer == performer && r.Performance == performance && !r.Retired)
            {
                return r;
            }
        }
        return null;
    }

    // Emitter on the performer (chest height) when the player is loaded here, else alone at the packet position.
    private static void EnsureEmitter(Remote r, MusicRules rules)
    {
        if (r.Emitter != null)
        {
            if (r.Player == null)
            {
                r.Emitter.transform.position = r.Position;
            }
            else if (r.Emitter.transform.parent != r.Player.transform)
            {
                // Player loaded here since the first notes: sound moves onto them.
                r.Emitter.transform.SetParent(r.Player.transform, false);
                r.Emitter.transform.localPosition = ChestOffset;
            }
            // The server's hearing range may have changed (cheap when it did not).
            r.Emitter.SetHearingRange(rules.HearingRange);
            return;
        }
        r.Emitter = Emitter.Create(r.Player != null ? r.Player.transform : null,
            r.Player != null ? ChestOffset : r.Position, r.Kind, false, rules.HearingRange);
    }

    private static Player FindPlayer(ZDOID id)
    {
        var scene = ZNetScene.instance;
        if (scene == null || id.IsNone())
        {
            return null;
        }
        var go = scene.FindInstance(id);
        return go != null ? go.GetComponent<Player>() : null;
    }
}
