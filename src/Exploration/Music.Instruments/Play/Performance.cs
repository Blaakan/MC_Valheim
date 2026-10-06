using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

internal enum PerformanceMode : byte
{
    None,
    Auto,      // a song plays by itself (built-in or MIDI); the bard may walk
    MiniGame,  // the player plays the song's notes in the rhythm game; feet stay
    FreePlay,  // the player plays the keyboard like a piano (FreePlayKeys, FreePlayMap); feet stay; no comfort
}

// Me = the local player's performance. Input from Player.SetControls prefix (Attack with an instrument in hand = song
// window; Attack or Block while playing = stop), work in Player.Update postfix (Tick, every frame). Everything stops
// by itself when it no longer makes sense (instrument put away, swimming, ship helm, hit...): Tick checks every frame,
// never trusts events alone.
// Clock: performance seconds, mapped to the audio clock by an anchor (clock 0 = anchor); the mini-game clock freeze
// while the single player game is paused (a song playing by itself plays on). Autoplay schedules the notes LookAhead s ahead
// on our own emitter (2D) and streams them (NoteRelay) every FlushInterval. Mini-game: a hit plays the chart note's
// group of notes at once and streams them right away (Live). Encore = success meter full: Music effect for us and an
// Encore flag in the stream (listeners in BonusRange give it to themselves).
// Player ZDO int InstrumentPose.PlayingKey = instrument while playing (other games pose the arms; owner write it, only
// on change).
internal static class Performance
{
    internal const string PlayerSongsOff = "This server allows only the built-in songs and its own songs.";
    // Free play note lengths: a held flute note lasts while its key is down (note off), at most FreeFluteHold; a lyre
    // string rings, a tambourine hit is short.
    private const float FreeFluteHold = 8f;
    private const float FreeLyreLength = 2.5f;
    private const float FreeHitLength = 0.25f;
    private const float FreeTapLength = 0.12f;  // flute key down and up in the same frame
    private const byte FreeVelocity = 100;
    private const float LookAhead = 0.5f;
    private const float FlushInterval = 0.2f;
    private const float LiveFlushInterval = 0.05f;
    private const float RepeatGap = 1.5f;
    private const float StartDelay = 0.15f;
    private const float WeightIn = 0.35f;
    private const float WeightOut = 0.3f;
    private static readonly Vector3 LocalEmitterOffset = new Vector3(0f, 1.4f, 0.2f);

    private struct Pulse
    {
        internal double At;
        internal byte Pitch;
        internal byte Velocity;
    }

    private static PerformanceMode _mode;
    private static InstrumentKind _kind;
    private static Player _player;
    private static Emitter _emitter;
    private static Note[] _notes = Array.Empty<Note>();
    private static float _length;
    private static string _title = "";
    private static int _next;
    private static float _passOffset;
    private static double _anchor;
    private static float _clock;
    private static float _lastClock;
    private static int _performanceId;
    private static int _seq;
    private static readonly NoteBatch Batch = new NoteBatch();
    private static float _lastFlush;
    private static float _lastSent;
    private const float KeepAlive = 1f;
    private static MiniGame _game;
    private static bool _windowOpen;
    private static bool _openRequest;
    private static bool _stopRequest;
    private static string _stopReason;
    private static float _weight;
    private static readonly List<Pulse> Pulses = new List<Pulse>(64);
    private static float _pendingMessageAt = float.NegativeInfinity;
    // Free play: flute key whose note sounds (FreePlayMap key index, -1 none): one note at a time.
    private static int _freeFluteKey = -1;
    private static int _freeFlutePitch = -1;

    internal static bool Repeat;
    internal static string LastSongId;
    internal static int LastPart = -1;

    internal static PerformanceMode Mode => _mode;

    // Rhythm game or free play: the keyboard is the instrument (game keys held back, feet still, Esc / right click stop).
    internal static bool LiveInput => _mode == PerformanceMode.MiniGame || _mode == PerformanceMode.FreePlay;
    internal static InstrumentKind Instrument => _mode == PerformanceMode.None ? InstrumentKind.None : _kind;
    internal static bool WindowOpen => _windowOpen;
    internal static string Title => _title;
    internal static float SongLength => _length;
    internal static MiniGame Game => _game;
    internal static float PoseWeight => _weight;

    // Seconds into the current pass (autoplay) or the mini-game clock.
    internal static float SongTime => _mode == PerformanceMode.Auto ? ShownTime() : _clock;

    // Time into the pass that is sounding (with Repeat the next pass is queued half a second early: the old one still
    // counts until it is over).
    private static float ShownTime()
    {
        var shown = _clock - _passOffset;
        if (shown < 0f && _passOffset > 0f)
        {
            shown += _length + RepeatGap;
        }
        return Mathf.Max(0f, shown);
    }

    internal static InstrumentKind Held
    {
        get
        {
            var p = Player.m_localPlayer;
            return p != null ? InstrumentContent.KindOf(p.GetRightItem()) : InstrumentKind.None;
        }
    }

#if DEBUG
    internal static Emitter LocalEmitter => _emitter;
    internal static int PerformanceId => _performanceId;
    internal static int BatchesSent;
#endif

    // Activation / new world: nothing playing.
    internal static void Reset()
    {
        _mode = PerformanceMode.None;
        _windowOpen = false;
        _openRequest = false;
        _stopRequest = false;
        _game = null;
        _player = null;
        _weight = 0f;
        Pulses.Clear();
    }

    // Feature off, world exit: everything back now.
    internal static void Shutdown()
    {
        Abort(null);
        if (_emitter != null)
        {
            _emitter.Hush();
            _emitter.Destroy();
            _emitter = null;
        }
        CloseWindow();
        SongWindow.Destroy();
        MiniGameHud.Destroy();
        KeyCapture.Release();
    }

    internal static void ApplyVolume()
    {
        if (_emitter != null)
        {
            _emitter.ApplyVolume();
        }
    }

    // ---------- input (Player.SetControls prefix) ----------

    // Returns true when the clicks are ours (caller zeroes attack inputs; zeroAll = also feet, block, jump...;
    // zeroActions = block, jump, dodge, crouch, run but feet free).
    internal static bool OnControls(Player player, bool attack, bool block, out bool zeroAll, out bool zeroActions)
    {
        zeroAll = false;
        zeroActions = false;
        // Feature off (or faked off by a self test): clicks go to the game, the always-on guard stop the punch.
        if (!Plugin.FeatureActive)
        {
            return false;
        }
        var kind = InstrumentContent.KindOf(player.GetRightItem());
        if (kind == InstrumentKind.None)
        {
            return false;
        }
        // Ship helm, saddle: vanilla uses the clicks to let go.
        if (player.GetDoodadController() != null)
        {
            return false;
        }
        if (_windowOpen || LiveInput)
        {
            zeroAll = true;
            return true;
        }
        if (_mode == PerformanceMode.Auto)
        {
            if (attack || block)
            {
                _stopRequest = true;
                SwallowClicks();
            }
            zeroActions = true;
            return true;
        }
        if (attack)
        {
            _openRequest = true;
        }
        return true;
    }

    // Player.OnDamaged postfix (local player): a real hit stops the music (damage over time does not).
    internal static void OnDamaged(HitData hit)
    {
        if (_mode == PerformanceMode.None || hit == null)
        {
            return;
        }
        switch (hit.m_hitType)
        {
            case HitData.HitType.Burning:
            case HitData.HitType.Freezing:
            case HitData.HitType.Poisoned:
            case HitData.HitType.Smoke:
            case HitData.HitType.Drowning:
            case HitData.HitType.Water:
            case HitData.HitType.AshlandsLava:
            case HitData.HitType.AshlandsOcean:
            case HitData.HitType.Self:
                return;
        }
        RequestStop("The hit cut your song short.");
    }

    internal static void RequestStop(string reason)
    {
        if (_mode != PerformanceMode.None)
        {
            _stopRequest = true;
            _stopReason = reason;
        }
    }

    // ---------- every frame (Player.Update postfix, local player) ----------

    internal static void Tick(Player player, float dt)
    {
        if (!ReferenceEquals(player, _player))
        {
            // New local player (spawn, respawn): whatever the old one had is gone.
            if (_mode != PerformanceMode.None)
            {
                Abort(null);
            }
            CloseWindow();
            _player = player;
            SetPlayingFlag(player, InstrumentKind.None);
        }
        if (_emitter != null)
        {
            _emitter.CheckHealth();
        }

        if (_windowOpen)
        {
            if (ZInput.GetKeyDown(KeyCode.Mouse1, false))
            {
                SwallowClicks();
                CloseWindow();
            }
            else if (Held == InstrumentKind.None || MustAbort(player) || GameScreens.AnyOpen() || _mode != PerformanceMode.None)
            {
                CloseWindow();
            }
            else
            {
                KeyCapture.Hold();
                SongWindow.Update();
            }
        }
        if (_openRequest)
        {
            _openRequest = false;
            OpenWindow();
        }

        if (_mode == PerformanceMode.None)
        {
            _stopRequest = false;
            _weight = Mathf.Max(0f, _weight - dt / WeightOut);
            // Tails rung out and nothing to play: free the audio source (a playing source holds one of Unity's few real
            // voices even when silent).
            if (_emitter != null && _emitter.Silent)
            {
                _emitter.Destroy();
                _emitter = null;
            }
            return;
        }

        // Instrument gone, or swapped for another one mid-song (hotbar keys are free while a song plays by itself).
        if (MustAbort(player) || Held != _kind)
        {
            Abort(null);
            return;
        }
        if (_stopRequest || player.IsStaggering() || player.IsKnockedBack())
        {
            Stop(_stopReason);
            return;
        }
        if (LiveInput && (GameScreens.AnyOpen() || ZInput.GetKeyDown(KeyCode.Mouse1, false)))
        {
            SwallowClicks();
            Stop(null);
            return;
        }

        UpdateClock(dt);
        if (_mode == PerformanceMode.Auto)
        {
            TickAuto();
        }
        else if (_mode == PerformanceMode.FreePlay)
        {
            TickFreePlay();
        }
        else
        {
            TickMiniGame(player);
        }
        if (_mode == PerformanceMode.None)
        {
            return; // song over
        }
        RunPulses(player);
        _weight = Mathf.Min(1f, _weight + dt / WeightIn);
    }

    // Single player pause: the mini-game clock freezes (anchor moves along); a song that plays by itself plays on
    // behind the menu, like the game's own music.
    private static void UpdateClock(float dt)
    {
        var now = AudioKit.DspNow;
        if (_mode == PerformanceMode.MiniGame && global::Game.IsPaused())
        {
            _anchor = now - _clock;
        }
        _lastClock = _clock;
        _clock = (float)(now - _anchor);
    }

    // ---------- window ----------

    internal static void OpenWindow()
    {
        var player = Player.m_localPlayer;
        if (player == null || _windowOpen || _mode != PerformanceMode.None)
        {
            return;
        }
        if (!CanStart(player, out var error))
        {
            if (error != null)
            {
                Notify(player, error);
            }
            return;
        }
        StopFeet(player);
        _windowOpen = true;
        KeyCapture.Hold();
        SongWindow.Open(Held);
    }

    internal static void CloseWindow()
    {
        if (!_windowOpen)
        {
            return;
        }
        _windowOpen = false;
        SongWindow.Close();
    }

    // ---------- start / stop ----------

    internal static bool StartAuto(SongEntry song, int part, out string error) =>
        Start(song, part, PerformanceMode.Auto, out error);

    internal static bool StartMiniGame(SongEntry song, int part, out string error) =>
        Start(song, part, PerformanceMode.MiniGame, out error);

    private static bool Start(SongEntry song, int part, PerformanceMode mode, out string error)
    {
        var player = Player.m_localPlayer;
        if (player == null || song == null)
        {
            error = "No song.";
            return false;
        }
        var kind = Held;
        if (kind == InstrumentKind.None)
        {
            error = "Hold an instrument to play.";
            return false;
        }
        if (_mode != PerformanceMode.None)
        {
            Stop(null);
        }
        if (!CanStart(player, out error))
        {
            error ??= "You cannot play now.";
            return false;
        }
        if (song.Source == SongSource.Midi && !ServerRules.Current.AllowPlayerSongs)
        {
            error = PlayerSongsOff;
            return false;
        }
        if (!SongLibrary.Arrange(song, kind, part, out var notes, out var length, out error))
        {
            error ??= "This song cannot be played.";
            return false;
        }
        if (notes == null || notes.Length == 0)
        {
            error = $"\"{song.Title}\" has nothing the {InstrumentContent.DisplayName(kind).ToLowerInvariant()} can play.";
            return false;
        }
        var rules = ServerRules.Current;
        var songLength = Mathf.Max(length, notes[notes.Length - 1].End);
        Begin(player, kind, mode, song.Title, notes, songLength);
        LastSongId = song.Id;
        LastPart = part;
        _game = mode == PerformanceMode.MiniGame
            ? new MiniGame(Chart.Build(notes, kind, _length), kind, song.Title, rules.SuccessSeconds, rules.SuccessAccuracy)
            : null;
        Log.Debug($"Playing \"{song.Title}\" on the {kind} ({mode}, {notes.Length} notes, {_length:0.#} s).");
        return true;
    }

    // Free play: the keyboard is a piano (FreePlayKeys -> FreePlayMap), streamed live like the rhythm game. No song,
    // no meter, no comfort (design D18).
    internal static bool StartFreePlay(out string error)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            error = "No player.";
            return false;
        }
        var kind = Held;
        if (kind == InstrumentKind.None)
        {
            error = "Hold an instrument to play.";
            return false;
        }
        if (_mode != PerformanceMode.None)
        {
            Stop(null);
        }
        if (!CanStart(player, out error))
        {
            error ??= "You cannot play now.";
            return false;
        }
        Begin(player, kind, PerformanceMode.FreePlay, "Free play", Array.Empty<Note>(), 0f);
        _game = null;
        LastSongId = SongLibrary.FreePlayId;
        LastPart = -1;
        Log.Debug($"Free play on the {kind}.");
        return true;
    }

    // What every performance starts with: mode, clock and anchor, a new performance id, the sound source, feet, flag.
    private static void Begin(Player player, InstrumentKind kind, PerformanceMode mode, string title, Note[] notes, float length)
    {
        var rules = ServerRules.Current;
        CloseWindow();
        _mode = mode;
        _kind = kind;
        _notes = notes;
        _length = length;
        _title = title;
        _next = 0;
        _passOffset = 0f;
        _performanceId = UnityEngine.Random.Range(1, int.MaxValue);
        _seq = 0;
        Batch.Clear();
        _stopRequest = false;
        _stopReason = null;
        Pulses.Clear();
        ResetFreeKeys();
        _anchor = AudioKit.DspNow + StartDelay;
        _clock = -StartDelay;
        _lastClock = _clock;
        // First batch goes out at once (listeners anchor on it: its notes lead its send time like every later batch).
        _lastFlush = _clock - FlushInterval;
        _lastSent = _clock;
        if (_emitter != null && (_emitter.Kind != kind || _emitter.Failed))
        {
            _emitter.Hush();
            _emitter.Destroy();
            _emitter = null;
        }
        if (_emitter == null)
        {
            _emitter = Emitter.Create(player.transform, LocalEmitterOffset, kind, true, rules.HearingRange);
        }
        StopFeet(player);
        SetPlayingFlag(player, kind);
    }

    // Graceful stop: notes ring out, End sent, pose blends back.
    internal static void Stop(string reason)
    {
        if (_mode == PerformanceMode.None)
        {
            return;
        }
        var player = _player != null ? _player : Player.m_localPlayer;
        if (_emitter != null)
        {
            _emitter.ReleaseAll();
        }
        var wasLive = LiveInput;
        Batch.Flags |= BatchFlags.End;
        Flush();
        _mode = PerformanceMode.None;
        if (wasLive)
        {
            ForgetHeldKeys(player);
        }
        _game = null;
        ResetFreeKeys();
        _stopRequest = false;
        _stopReason = null;
        Pulses.Clear();
        MiniGameHud.Hide();
        KeyCapture.Release();
        if (player != null)
        {
            SetPlayingFlag(player, InstrumentKind.None);
            if (reason != null)
            {
                Notify(player, reason);
            }
        }
    }

    // Me stop now: instrument gone, swimming, teleport... Notes let go (short fade, no pop), pose back at once.
    internal static void Abort(string reason)
    {
        var wasPlaying = _mode != PerformanceMode.None;
        // Stop let every voice go (short natural fade: no pop from cutting a wave half way).
        Stop(reason);
        if (wasPlaying)
        {
            InstrumentPose.ReleaseLocal();
            _weight = 0f;
        }
    }

    // ---------- autoplay ----------

    private static void TickAuto()
    {
        var horizon = _clock + LookAhead;
        while (true)
        {
            if (_next >= _notes.Length)
            {
                if (!Repeat)
                {
                    break;
                }
                // Next pass starts after the song's length and a short gap.
                _passOffset += _length + RepeatGap;
                _next = 0;
            }
            var n = _notes[_next];
            var t = _passOffset + n.Time;
            if (t > horizon)
            {
                break;
            }
            // Already too late (the game stalled longer than the look-ahead): skip, never pile notes on one sample.
            if (t < _clock - Listeners.LateDrop)
            {
                _next++;
                continue;
            }
            PlayNote(t, n);
            _next++;
        }
        if (_clock - _lastFlush >= FlushInterval || Batch.Notes.Count >= NoteBatch.MaxNotes - 4)
        {
            Flush();
        }
        if (_clock - _lastSent >= KeepAlive)
        {
            Flush(keepAlive: true);
        }
        MiniGameHud.UpdateAutoplay(_title, ShownTime(), _length, Repeat);
        // Last pass over (and its notes sent): stop.
        if (!Repeat && _next >= _notes.Length && _clock > _passOffset + _length + 0.3f)
        {
            Stop(null);
        }
    }

    // One note at performance time t: our own emitter, the stream, a pose pulse.
    private static void PlayNote(float t, Note n)
    {
        var start = _anchor + t;
        if (_emitter != null)
        {
            _emitter.Schedule(start, start + n.Length, n.Pitch, n.Velocity);
        }
        if (Batch.Notes.Count >= NoteBatch.MaxNotes)
        {
            Flush();
        }
        Batch.Notes.Add(new Note(t, n.Length, n.Pitch, Math.Max((byte)1, n.Velocity))); // 0 = note off on the wire
        if (Pulses.Count < 256)
        {
            Pulses.Add(new Pulse { At = start, Pitch = n.Pitch, Velocity = n.Velocity });
        }
    }

    // ---------- mini-game ----------

    private static void TickMiniGame(Player player)
    {
        var game = _game;
        if (game == null)
        {
            Stop(null);
            return;
        }
        KeyCapture.Hold();
        var paused = global::Game.IsPaused();
        game.Clock = _clock;
        game.MissedThisFrame.Clear();
        game.Judge.Expire(_clock, game.MissedThisFrame);
        foreach (var miss in game.MissedThisFrame)
        {
            game.LastJudgement = Judgement.Miss;
            game.LastJudgementAt = _clock;
            game.LastJudgementLane = game.Lane(miss.Value);
            game.RecentMisses.Add(new KeyValuePair<KeyValuePair<int, int>, float>(miss, _clock));
        }
        if (!paused && !GameScreens.TextFieldHasKeyboard())
        {
            for (var lane = 0; lane < LaneKeys.Count; lane++)
            {
                if (!LaneKeys.Down(lane))
                {
                    continue;
                }
                game.LanePressedAt[lane] = _clock;
                var result = game.Judge.Press(_clock, lane, out var loop, out var index);
                game.LastJudgement = result;
                game.LastJudgementAt = _clock;
                game.LastJudgementLane = lane;
                if (result == Judgement.Perfect || result == Judgement.Good)
                {
                    game.LaneHitAt[lane] = _clock;
                    PlayChartNote(game, loop, index);
                }
            }
        }
        var dt = paused ? 0f : Mathf.Max(0f, _clock - _lastClock);
        var near = game.Judge.NoteNear(_clock, SuccessMeter.RestWindow);
        if (game.Meter.Update(dt, game.Judge.RecentAccuracy, near))
        {
            Encore(player, game);
        }
        game.RefreshVisible();
        if (_clock - _lastFlush >= LiveFlushInterval && Batch.Notes.Count > 0)
        {
            Flush();
        }
        if (_clock - _lastSent >= KeepAlive)
        {
            Flush(keepAlive: true);
        }
        MiniGameHud.Update(game);
    }

    // A hit: the chart note's notes sound now (their spacing kept), streamed at once.
    private static void PlayChartNote(MiniGame game, int loop, int index)
    {
        var c = game.Chart.Notes[index];
        var now = AudioKit.DspNow;
        for (var k = c.First; k < c.First + c.Count && k < game.Chart.Song.Length; k++)
        {
            var n = game.Chart.Song[k];
            var offset = Mathf.Max(0f, n.Time - c.Time);
            var start = now + offset;
            if (_emitter != null)
            {
                _emitter.Schedule(start, start + n.Length, n.Pitch, n.Velocity);
            }
            if (Batch.Notes.Count >= NoteBatch.MaxNotes)
            {
                Flush();
            }
            Batch.Notes.Add(new Note(_clock + offset, n.Length, n.Pitch, Math.Max((byte)1, n.Velocity)));
            if (Pulses.Count < 256)
            {
                Pulses.Add(new Pulse { At = start, Pitch = n.Pitch, Velocity = n.Velocity });
            }
        }
    }

    // ---------- free play ----------

    private static void TickFreePlay()
    {
        KeyCapture.Hold();
        var now = AudioKit.DspNow;
        var octaveUp = FreePlayKeys.OctaveUp;
        for (var key = 0; key < FreePlayMap.KeyCount; key++)
        {
            // Down and up can both come in one frame (a quick tap during a hitch): not held now = pressed then let go
            // (a tap); still held = let go then pressed again (the press ends the old note anyway).
            var down = FreePlayKeys.Down(key);
            var up = FreePlayKeys.Up(key);
            if (down && up && !FreePlayKeys.Held(key))
            {
                FreeTap(key, FreePlayMap.Pitch(_kind, key, octaveUp), now);
            }
            else if (down)
            {
                FreePress(key, FreePlayMap.Pitch(_kind, key, octaveUp), now);
            }
            else if (up)
            {
                FreeRelease(key, now);
            }
        }
        if (_clock - _lastFlush >= LiveFlushInterval && Batch.Notes.Count > 0)
        {
            Flush();
        }
        if (_clock - _lastSent >= KeepAlive)
        {
            Flush(keepAlive: true);
        }
        MiniGameHud.UpdateFreePlay(_kind, octaveUp);
    }

    // Key down: its note sounds now and goes out live. Flute: one note at a time (the one held ends where the new one
    // starts: legato), held until the key comes up (Space let go meanwhile: the note keeps its pitch).
    private static void FreePress(int key, int pitch, double now)
    {
        if (pitch < 0)
        {
            return;
        }
        float length;
        switch (_kind)
        {
            case InstrumentKind.Flute:
                if (_freeFlutePitch >= 0)
                {
                    FreeNoteOff(_freeFlutePitch, now);
                }
                _freeFluteKey = key;
                _freeFlutePitch = pitch;
                length = FreeFluteHold;
                break;
            case InstrumentKind.Lyre:
                length = FreeLyreLength;
                break;
            default:
                length = FreeHitLength;
                break;
        }
        var p = (byte)pitch;
        if (_emitter != null)
        {
            _emitter.Schedule(now, now + length, p, FreeVelocity);
        }
        AddLive(new Note(_clock, length, p, FreeVelocity));
        if (Pulses.Count < 256)
        {
            Pulses.Add(new Pulse { At = now, Pitch = p, Velocity = FreeVelocity });
        }
    }

    // Key down and up in one frame: lyre and tambourine as a press; flute a short note with no held state and no note
    // off (never a note held for 8 s by a key that is already up).
    private static void FreeTap(int key, int pitch, double now)
    {
        if (_kind != InstrumentKind.Flute)
        {
            FreePress(key, pitch, now);
            return;
        }
        if (pitch < 0)
        {
            return;
        }
        if (_freeFlutePitch >= 0)
        {
            FreeNoteOff(_freeFlutePitch, now);
        }
        _freeFluteKey = -1;
        _freeFlutePitch = -1;
        var p = (byte)pitch;
        if (_emitter != null)
        {
            _emitter.Schedule(now, now + FreeTapLength, p, FreeVelocity);
        }
        AddLive(new Note(_clock, FreeTapLength, p, FreeVelocity));
        if (Pulses.Count < 256)
        {
            Pulses.Add(new Pulse { At = now, Pitch = p, Velocity = FreeVelocity });
        }
    }

    // Key up: the flute note of that key ends (lyre strings and tambourine hits ring by themselves).
    private static void FreeRelease(int key, double now)
    {
        if (_freeFluteKey != key || _freeFlutePitch < 0)
        {
            return;
        }
        FreeNoteOff(_freeFlutePitch, now);
        _freeFluteKey = -1;
        _freeFlutePitch = -1;
    }

    private static void FreeNoteOff(int pitch, double now)
    {
        if (_emitter != null)
        {
            _emitter.EndNote(now, (byte)pitch);
        }
        AddLive(new Note(_clock, 0f, (byte)pitch, 0)); // velocity 0 = note off (NoteBatch layout 2)
    }

    private static void AddLive(Note note)
    {
        if (Batch.Notes.Count >= NoteBatch.MaxNotes)
        {
            Flush();
        }
        Batch.Notes.Add(note);
#if DEBUG
        if (note.Velocity == 0)
        {
            NoteOffsSent++;
        }
        else
        {
            NoteOnsSent++;
        }
        SentRing[SentRingPos] = new KeyValuePair<byte, byte>(note.Pitch, note.Velocity);
        SentRingPos = (SentRingPos + 1) % SentRing.Length;
#endif
    }

    private static void ResetFreeKeys()
    {
        _freeFluteKey = -1;
        _freeFlutePitch = -1;
    }

#if DEBUG
    internal static int FreeFlutePitch => _freeFlutePitch;
    internal static int NoteOnsSent;
    internal static int NoteOffsSent;
    internal static BatchFlags LastFlags;
    // Last 8 free play notes put in a batch (pitch, velocity), oldest overwritten.
    internal static readonly KeyValuePair<byte, byte>[] SentRing = new KeyValuePair<byte, byte>[8];
    internal static int SentRingPos;

    // n = 1: the last note sent, 2: the one before...
    internal static KeyValuePair<byte, byte> SentBack(int n) => SentRing[((SentRingPos - n) % SentRing.Length + SentRing.Length) % SentRing.Length];
#endif

    private static void Encore(Player player, MiniGame game)
    {
        game.Encores++;
        game.LastEncoreAt = _clock;
        MusicBonus.Grant(player, fromOther: false);
        Batch.Flags |= BatchFlags.Encore;
        Flush();
        Log.Debug($"Encore {game.Encores} in \"{game.Title}\" (accuracy {game.Judge.RecentAccuracy:0.00}).");
    }

    // ---------- network ----------

    // keepAlive: send even an empty batch (listeners keep the performance during long rests and held notes).
    private static void Flush(bool keepAlive = false)
    {
        _lastFlush = _clock;
        if (Batch.Notes.Count == 0 && Batch.Flags == BatchFlags.None && !keepAlive)
        {
            return;
        }
        _lastSent = _clock;
        var player = _player != null ? _player : Player.m_localPlayer;
        Batch.Performance = _performanceId;
        Batch.Instrument = _kind;
        Batch.Seq = _seq++;
        Batch.SentAt = _clock;
        if (LiveInput)
        {
            Batch.Flags |= BatchFlags.Live;
        }
        if (player != null)
        {
            Batch.Performer = player.GetZDOID();
            Batch.Position = player.transform.position;
        }
        try
        {
            NoteRelay.Send(Batch);
#if DEBUG
            BatchesSent++;
            LastFlags = Batch.Flags;
#endif
        }
        finally
        {
            Batch.Clear();
        }
    }

    // ---------- pose pulses ----------

    private static void RunPulses(Player player)
    {
        if (Pulses.Count == 0)
        {
            return;
        }
        var now = AudioKit.DspNow;
        for (var i = 0; i < Pulses.Count;)
        {
            if (Pulses[i].At <= now)
            {
                InstrumentPose.Pulse(player, _kind, Pulses[i].Pitch, Pulses[i].Velocity);
                Pulses.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    // Keyboard was the instrument: keys still down at the stop are also game buttons (Q = auto-run, Space = jump,
    // E = use, the letters = walking). Forget them until pressed again, or the bard runs off or jumps at once.
    private static void ForgetHeldKeys(Player player)
    {
        foreach (var button in HeldButtons)
        {
            ZInput.ResetButtonStatus(button);
        }
        if (player != null)
        {
            player.m_autoRun = false;
        }
    }

    private static readonly string[] HeldButtons = { "AutoRun", "Jump", "Use", "Forward", "Backward", "Left", "Right", "Crouch" };

    // A click that stops or closes is the game's Attack/Block too: forget it now, or the still-held button reaches
    // vanilla next tick (a seated player stands up, a toggled block switches on). Same as the game's own windows do.
    private static void SwallowClicks()
    {
        ZInput.ResetButtonStatus("Block");
        ZInput.ResetButtonStatus("JoyBlock");
        ZInput.ResetButtonStatus("Attack");
        ZInput.ResetButtonStatus("JoyAttack");
    }

    // ---------- checks ----------

    internal static bool CanStart(Player player, out string error)
    {
        error = null;
        if (player == null || InstrumentContent.KindOf(player.GetRightItem()) == InstrumentKind.None)
        {
            return false;
        }
        if (ServerRules.IsPending)
        {
            error = "The server has not sent the instrument settings yet.";
            return false;
        }
        if (MustAbort(player) || player.IsStaggering() || player.IsKnockedBack() || player.InAttack()
            || player.InDodge() || GameScreens.AnyOpen() || PlayerController.HasInputDelay)
        {
            return false;
        }
        return true;
    }

    // Instant stop: no instrument in hand, dead, teleporting, cutscene, bed, sleep, ship helm, saddle, swimming,
    // build mode, debug fly, free camera. Chairs and the sit emote are fine (play by the fire).
    internal static bool MustAbort(Player player)
    {
        return player == null || player.IsDead() || InstrumentContent.KindOf(player.GetRightItem()) == InstrumentKind.None
               || player.IsTeleporting() || player.InCutscene() || player.InBed() || player.IsSleeping()
               || player.GetDoodadController() != null || player.IsRiding() || player.IsSwimming()
               || player.InPlaceMode() || player.IsDebugFlying() || GameCamera.InFreeFly();
    }

    // Stop what the feet were doing: auto-run keeps running without input, a toggled block stays up.
    private static void StopFeet(Player player)
    {
        player.m_autoRun = false;
        player.m_moveDir = Vector3.zero;
        player.m_run = false;
        player.m_blocking = false;
    }

    // Owner write; ZDO only send a change.
    private static void SetPlayingFlag(Player player, InstrumentKind kind)
    {
        var view = player != null ? player.m_nview : null;
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return;
        }
        var zdo = view.GetZDO();
        if (zdo.GetInt(InstrumentPose.PlayingKey) != (int)kind)
        {
            zdo.Set(InstrumentPose.PlayingKey, (int)kind);
        }
    }

    private static void Notify(Player player, string text)
    {
        if (Time.unscaledTime - _pendingMessageAt < 1.5f)
        {
            return;
        }
        _pendingMessageAt = Time.unscaledTime;
        player.Message(MessageHud.MessageType.TopLeft, text);
    }

#if DEBUG
    // Self tests: drive without mouse clicks.
    internal static void TestRequestOpen() => _openRequest = true;

    internal static void TestRequestStop() => _stopRequest = true;
#endif
}
