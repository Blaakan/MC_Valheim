#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MC.Shared;
using UnityEngine;
using Keyboard = UnityEngine.InputSystem.Keyboard;
using Mouse = UnityEngine.InputSystem.Mouse;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. In-world self tests, fourth part (see the list in SelfTests.Solo2.cs): what other players' games
// would get and hear (made-up players, no network), real key events, and the checks that lean on other mods or on
// the whole run (each in its own small test).
internal static partial class SelfTests
{
    private const string ListenMoreName = "music.listen.more";
    private const string SendName = "music.send";
    private const string KeysName = "music.keys";
    private const string SleepName = "music.sleep";
    private const string CrossModName = "music.crossmod";
    private const string SpyglassName = "music.spyglass";
    private const string CleanLogName = "music.cleanlog";
    private const string NoBonusName = "music.nobonus";
    private const string BadRecipeName = "music.badrecipe";

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Solo3Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(NoBonusName, RunNoBonus),
        new KeyValuePair<string, Func<IEnumerator>>(BadRecipeName, RunBadRecipe),
        new KeyValuePair<string, Func<IEnumerator>>(ListenMoreName, RunListenMore),
        new KeyValuePair<string, Func<IEnumerator>>(SendName, RunSend),
        new KeyValuePair<string, Func<IEnumerator>>(KeysName, RunKeys),
        new KeyValuePair<string, Func<IEnumerator>>(SleepName, RunSleep),
        new KeyValuePair<string, Func<IEnumerator>>(CrossModName, RunCrossMod),
        new KeyValuePair<string, Func<IEnumerator>>(SpyglassName, RunSpyglass),
        // Last: it looks back at the whole run.
        new KeyValuePair<string, Func<IEnumerator>>(CleanLogName, RunCleanLog),
    };

    private static void RegisterSolo3()
    {
        foreach (var t in Solo3Tests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
    }

    private static void UnregisterSolo3()
    {
        foreach (var t in Solo3Tests)
        {
            SelfTest.Unregister(t.Key);
        }
    }

    // ---------- music.nobonus (T38: ComfortBonus 0) ----------

    private static IEnumerator RunNoBonus()
    {
        var c = new Checks(NoBonusName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(NoBonusName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var none = TestRules(6f);
            none.ComfortBonus = 0;
            ServerRules.TestRules = none;
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            Listeners.ForgetEncores();
            Seen.Clear();
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            var baseComfort = player.m_comfortLevel;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var pressed = new HashSet<long>();
            var banner = "";
            var began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < 30f && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 0);
                if (game.Encores > 0)
                {
                    yield return null;
                    banner = MiniGameHud.EncoreShown;
                    break;
                }
                yield return null;
            }
            c.Check(game != null && game.Encores == 1, "the meter still fills and the Encore comes");
            c.Check(banner == "Encore!", $"the banner says 'Encore!' with no comfort number: '{banner}'");
            c.Check(!seman.HaveStatusEffect(InstrumentContent.EffectHash) && Seen.Count(MessageHud.MessageType.TopLeft, "warms") == 0,
                "no Music effect and no message");
            c.Check(player.GetComfortLevel() == baseComfort, $"comfort unchanged ({player.GetComfortLevel()})");
            Performance.TestRequestStop();
            yield return Frames(3);
            // Another player's Encore: nothing either.
            var other = new ZDOID(717171L, 2u);
            Listeners.Receive(BatchOf(other, 81, player.transform.position + player.transform.right * 5f, InstrumentKind.Flute, 0f, BatchFlags.Encore | BatchFlags.Live));
            c.Check(!seman.HaveStatusEffect(InstrumentContent.EffectHash) && Seen.Count(MessageHud.MessageType.TopLeft, "warms") == 0,
                "another player's Encore gives nothing either");
            // Music already running when the bonus goes to 0: shown without a number, worth no comfort.
            ServerRules.TestRules = TestRules(6f);
            c.Check(MusicBonus.Grant(player, false) && player.GetComfortLevel() == baseComfort + 3, "with a bonus of 3: Music, comfort +3");
            ServerRules.TestRules = none;
            var effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
            if (c.Check(effect != null, "the bonus set to 0: the effect stays"))
            {
                var icon = effect.GetIconText();
                c.Check(icon.IndexOf('+') < 0 && icon.IndexOf(':') > 0, $"its icon shows the time left and no '+': '{icon}'");
                c.Check(effect.GetTooltipString() == "Music heard nearby.", "tooltip: " + effect.GetTooltipString());
                c.Check(player.GetComfortLevel() == baseComfort, $"and it raises no comfort ({player.GetComfortLevel()})");
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.badrecipe (T39: mistakes in the recipe settings) ----------

    private static string Cost(InstrumentKind kind)
    {
        var parts = new List<string>();
        foreach (var req in InstrumentContent.RecipeOf(kind).m_resources)
        {
            parts.Add(req.m_resItem.name + ":" + req.m_amount);
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join(",", parts.ToArray());
    }

    private static IEnumerator RunBadRecipe()
    {
        var c = new Checks(BadRecipeName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(BadRecipeName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var wait = new WaitForSeconds(InstrumentContent.RebuildDelaySeconds + 0.3f);
            var mark = LogTap.ProblemMark;
            // An unknown material next to a good one; only an unknown material; an unknown station.
            var bad = TestRules();
            bad.FluteResources = "selftest_nothing:3,FineWood:2";
            bad.LyreResources = "selftest_nothing:1";
            bad.TambourineStation = "selftest_station";
            ServerRules.TestRules = bad;
            yield return wait;
            var flute = InstrumentContent.RecipeOf(InstrumentKind.Flute);
            var lyre = InstrumentContent.RecipeOf(InstrumentKind.Lyre);
            var tambourine = InstrumentContent.RecipeOf(InstrumentKind.Tambourine);
            c.Check(flute.m_enabled && Cost(InstrumentKind.Flute) == "FineWood:2", "unknown material skipped: the flute costs the known one only (" + Cost(InstrumentKind.Flute) + ")");
            c.Check(!lyre.m_enabled, "no valid material: the lyre recipe is hidden");
            c.Check(!tambourine.m_enabled, "unknown station: the tambourine recipe is hidden");
            var lines = LogTap.ProblemsSince(mark);
            c.Note("warnings: " + string.Join(" | ", lines.ToArray()));
            c.Check(LogTap.CountProblems(mark, "recipe material \"selftest_nothing\" is not an item in this game") == 1, "one warning for the unknown material");
            c.Check(LogTap.CountProblems(mark, "recipe has no valid material (\"selftest_nothing:1\")") == 1, "one warning for the recipe left without a material");
            c.Check(LogTap.CountProblems(mark, "recipe station \"selftest_station\" is not a crafting station") == 1, "one warning for the unknown station");
            c.Check(lines.Count == 3, $"three warnings in all ({lines.Count})");
            // The same mistakes again: not said twice.
            var again = TestRules();
            again.FluteResources = bad.FluteResources;
            again.LyreResources = bad.LyreResources;
            again.TambourineStation = bad.TambourineStation;
            ServerRules.TestRules = again;
            yield return wait;
            c.Check(LogTap.ProblemsSince(mark).Count == 3, "the warnings are not repeated");
            // An instrument already owned still plays while its recipe is hidden.
            rig.Hold(InstrumentKind.Lyre);
            yield return new WaitForSeconds(0.4f);
            var plays = Performance.StartAuto(Preset("greensleeves"), -1, out var error);
            c.Check(!lyre.m_enabled && plays, "a lyre already owned still plays: " + error);
            Performance.TestRequestStop();
            yield return Frames(3);
            // A bad amount: skipped with a warning.
            var amount = TestRules();
            amount.FluteResources = "FineWood:selftest";
            ServerRules.TestRules = amount;
            yield return wait;
            c.Check(!flute.m_enabled && LogTap.CountProblems(mark, "has no valid amount") == 1, "a material without a valid amount is skipped (warning), the recipe hidden");
            // Empty station: crafted by hand.
            var hand = TestRules();
            hand.FluteStation = "";
            ServerRules.TestRules = hand;
            yield return wait;
            c.Check(flute.m_enabled && flute.m_craftingStation == null && Cost(InstrumentKind.Flute) == "FineWood:4", "empty station: the flute is crafted by hand");
            // Good settings again: everything back.
            ServerRules.TestRules = TestRules();
            yield return wait;
            c.Check(flute.m_enabled && lyre.m_enabled && tambourine.m_enabled && flute.m_craftingStation != null && flute.m_craftingStation.name == MusicRules.Workbench
                    && Cost(InstrumentKind.Lyre) == "LinenThread:8,Silver:2" && Cost(InstrumentKind.Tambourine) == "FineWood:3,LeatherScraps:4",
                "settings put right: the three recipes are back");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.listen.more (listener side of M02 M04 M10 M11 M18 M19; mixer part of T16) ----------

    private static IEnumerator RunListenMore()
    {
        var c = new Checks(ListenMoreName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ListenMoreName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            var rules = TestRules(20f);
            rules.HearingRange = 20f;
            rules.BonusRange = 20f;
            ServerRules.TestRules = rules;
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            Listeners.Shutdown();
            Listeners.ForgetEncores();
            Seen.Clear();
            var here = player.transform.position;
            var right = player.transform.right;
            var t0 = AudioKit.DspNow;
            float Perf() => (float)(AudioKit.DspNow - t0);
            NoteBatch Notes(ZDOID who, int id, Vector3 place, InstrumentKind kind, int count, BatchFlags flags = BatchFlags.None)
            {
                var b = BatchOf(who, id, place, kind, Perf(), flags);
                for (var i = 0; i < count; i++)
                {
                    b.Notes.Add(new Note(b.SentAt + 0.15f + i * 0.25f, 0.4f, (byte)(60 + i * 2), 100));
                }
                return b;
            }
            var rms = new float[1];

            // Hearing range 20 m: a player at 30 m is not heard at all; one at 12 m is, from their place, fading to
            // nothing at 20 m.
            var far = new ZDOID(700001L, 1u);
            Listeners.Receive(Notes(far, 1, here + right * 30f, InstrumentKind.Flute, 4));
            c.Check(Listeners.EmitterOf(far) == null && Listeners.RemoteCount == 0, "a player 30 m away (hearing range 20): nothing heard");
            var near = new ZDOID(700002L, 1u);
            var nearPlace = here + right * 12f;
            Listeners.Receive(Notes(near, 2, nearPlace, InstrumentKind.Flute, 8));
            var nearEmitter = Listeners.EmitterOf(near);
            if (!c.Check(nearEmitter != null && nearEmitter.Source != null, "a player 12 m away is heard"))
            {
                c.Report();
                yield break;
            }
            var source = nearEmitter.Source;
            c.Check((nearEmitter.transform.position - nearPlace).magnitude < 0.5f, "the sound is at their place");
            c.Check(source.spatialBlend == 1f && source.rolloffMode == AudioRolloffMode.Linear && Mathf.Approximately(source.maxDistance, 20f)
                    && Mathf.Approximately(source.minDistance, Emitter.MinDistance),
                $"3D sound: full up to {F(source.minDistance)} m, softer with distance, nothing from {F(source.maxDistance)} m (the hearing range)");
            c.Check(AudioKit.SfxGroup != null && source.outputAudioMixerGroup == AudioKit.SfxGroup, "it goes through the game's sound effects group (its volume sliders)");
            yield return new WaitForSeconds(0.5f);
            yield return Measure(nearEmitter, 0.5f, rms);
            c.Check(rms[0] > 0.0005f, $"their notes sound (rms {F(rms[0])})");

            // Their Encore, within 20 m: Music for us too, with the message; again at once: counted once.
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            var baseComfort = player.m_comfortLevel;
            Listeners.Receive(BatchOf(near, 2, nearPlace, InstrumentKind.Flute, Perf(), BatchFlags.Encore | BatchFlags.Live));
            c.Check(seman.HaveStatusEffect(InstrumentContent.EffectHash), "their Encore within 20 m gives us Music");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "The music warms you: +3 comfort.") == 1, "message: " + Seen.Tail());
            c.Check(player.GetComfortLevel() == baseComfort + 3, $"our comfort is 3 higher ({baseComfort} -> {player.GetComfortLevel()})");
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            Listeners.Receive(BatchOf(near, 2, nearPlace, InstrumentKind.Flute, Perf(), BatchFlags.Encore | BatchFlags.Live));
            c.Check(!seman.HaveStatusEffect(InstrumentContent.EffectHash), "a second Encore of the same player at once is not counted");
            // A performer farther than 20 m (still heard-range margin): no Music from their Encore.
            var third = new ZDOID(700003L, 1u);
            Listeners.Receive(BatchOf(third, 3, here - right * 23f, InstrumentKind.Lyre, Perf(), BatchFlags.Encore | BatchFlags.Live));
            c.Check(!seman.HaveStatusEffect(InstrumentContent.EffectHash), "an Encore played 23 m away gives nothing");
            // They walk out of hearing: let go.
            Listeners.Receive(Notes(near, 2, here + right * 27f, InstrumentKind.Flute, 2));
            c.Check(Listeners.EmitterOf(near) == null, "walked out of hearing: their performance is let go");

            // Two players at once, a lyre and a flute holding one long note (free play, live).
            var a = new ZDOID(700010L, 1u);
            var b = new ZDOID(700011L, 1u);
            var placeA = here + right * 6f;
            var placeB = here - right * 6f;
            Listeners.Receive(Notes(a, 10, placeA, InstrumentKind.Lyre, 8));
            var held = BatchOf(b, 11, placeB, InstrumentKind.Flute, Perf(), BatchFlags.Live);
            held.Notes.Add(new Note(held.SentAt + 0.05f, 8f, 72, 100));
            Listeners.Receive(held);
            var ea = Listeners.EmitterOf(a);
            var eb = Listeners.EmitterOf(b);
            c.Check(ea != null && eb != null && !ReferenceEquals(ea, eb) && ea.Kind == InstrumentKind.Lyre && eb.Kind == InstrumentKind.Flute,
                "two players at once: each has their own sound, with their own instrument");
            c.Check(ea != null && eb != null && (ea.transform.position - placeA).magnitude < 0.5f && (eb.transform.position - placeB).magnitude < 0.5f,
                "each at their own place");
            yield return new WaitForSeconds(0.5f);
            if (ea != null)
            {
                ea.ResetMeasure();
            }
            yield return Measure(eb, 0.5f, rms);
            c.Check(rms[0] > 0.0005f && ea != null && ea.Rms > 0.0005f, $"both are heard (flute rms {F(rms[0])}, lyre rms {F(ea != null ? ea.Rms : 0f)})");
            // The lyre ends; the flute's note goes on, kept alive by its empty batches once a second.
            Listeners.Receive(BatchOf(a, 10, placeA, InstrumentKind.Lyre, Perf(), BatchFlags.End));
            // A third one meanwhile: a song with a long rest (nothing but keep-alive batches for five seconds).
            var d = new ZDOID(700012L, 1u);
            var placeD = here + player.transform.forward * 7f;
            Listeners.Receive(Notes(d, 12, placeD, InstrumentKind.Lyre, 2));
            var began = Time.realtimeSinceStartup;
            var lastAlive = began;
            while (Time.realtimeSinceStartup - began < 5.2f)
            {
                if (Time.realtimeSinceStartup - lastAlive >= 1f)
                {
                    lastAlive = Time.realtimeSinceStartup;
                    Listeners.Receive(BatchOf(b, 11, placeB, InstrumentKind.Flute, Perf(), BatchFlags.Live));
                    Listeners.Receive(BatchOf(d, 12, placeD, InstrumentKind.Lyre, Perf()));
                }
                yield return null;
            }
            c.Check(Listeners.EmitterOf(a) == null, "the player who stopped is let go");
            c.Check(ReferenceEquals(Listeners.EmitterOf(b), eb) && eb != null, "the other one is still heard, same sound source");
            yield return Measure(eb, 0.4f, rms);
            c.Check(rms[0] > 0.0005f, $"a note held for six seconds still sounds (never cut after about three) (rms {F(rms[0])})");
            var off = BatchOf(b, 11, placeB, InstrumentKind.Flute, Perf(), BatchFlags.Live);
            off.Notes.Add(new Note(off.SentAt, 0f, 72, 0));
            Listeners.Receive(off);
            yield return Real(0.7f);
            yield return Measure(eb, 0.4f, rms);
            c.Check(rms[0] < 0.0002f, $"their key up ends it for us too (rms {F(rms[0])})");
            // The song with the rest goes on after it.
            var ed = Listeners.EmitterOf(d);
            c.Check(ed != null, "a performance resting for five seconds is kept");
            var scheduled = Listeners.NotesScheduled;
            Listeners.Receive(Notes(d, 12, placeD, InstrumentKind.Lyre, 4));
            c.Check(Listeners.NotesScheduled == scheduled + 4 && ReferenceEquals(Listeners.EmitterOf(d), ed), "its next notes play after the rest (same source)");
            yield return Real(0.4f);
            yield return Measure(ed, 0.5f, rms);
            c.Check(rms[0] > 0.0005f, $"and are heard (rms {F(rms[0])})");

            // A network hiccup: nothing for 1.5 s, then five batches at once, each as old as it is late. Late notes are
            // dropped, never played in a burst; the next batch on time plays whole.
            var e = new ZDOID(700013L, 1u);
            var placeE = here - player.transform.forward * 7f;
            Listeners.Receive(Notes(e, 13, placeE, InstrumentKind.Flute, 2));
            var mark = Perf();
            yield return Real(1.5f);
            scheduled = Listeners.NotesScheduled;
            var dropped = Listeners.NotesDropped;
            var sentLate = 0;
            for (var k = 0; k < 5; k++)
            {
                var late = BatchOf(e, 13, placeE, InstrumentKind.Flute, mark + 0.2f + k * 0.25f);
                for (var i = 0; i < 3; i++)
                {
                    late.Notes.Add(new Note(late.SentAt + 0.02f + i * 0.08f, 0.2f, (byte)(64 + i), 100));
                    sentLate++;
                }
                Listeners.Receive(late);
            }
            var playedLate = Listeners.NotesScheduled - scheduled;
            var droppedLate = Listeners.NotesDropped - dropped;
            c.Note($"hiccup: {sentLate} late notes arrived at once, {playedLate} played, {droppedLate} dropped, extra delay {F(Listeners.NewestExtraDelay)} s");
            c.Check(droppedLate >= 6 && playedLate + droppedLate == sentLate, "notes that come too late are dropped");
            c.Check(playedLate <= 9, "never a burst of all the piled-up notes");
            c.Check(Listeners.NewestExtraDelay <= Listeners.MaxDelay + 0.001f, $"the delay stays bounded ({F(Listeners.NewestExtraDelay)} s)");
            yield return Real(0.4f);
            scheduled = Listeners.NotesScheduled;
            dropped = Listeners.NotesDropped;
            Listeners.Receive(Notes(e, 13, placeE, InstrumentKind.Flute, 4));
            c.Check(Listeners.NotesScheduled == scheduled + 4 && Listeners.NotesDropped == dropped, "the next batch on time plays whole");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.send (sender side of M03 M05 M11) ----------

    private static IEnumerator RunSend()
    {
        var c = new Checks(SendName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SendName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            Performance.Repeat = false;
            var folder = x.SongsFolder("MC_MusicSendTest");
            // Own MIDI file: two notes, one note held six seconds, a five second rest, three notes.
            var notes = new List<Note>();
            AddRun(notes, 0.3f, 2, 0.5f);
            notes.Add(new Note(1.5f, 6f, 79, 100));
            AddRun(notes, 12.5f, 3, 0.4f);
            File.WriteAllBytes(Path.Combine(folder, "selftest long.mid"), MidiFrom(notes));
            var list = SongLibrary.ScanMidiFolder(out _);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var played = Performance.NotesPlayed;
            var batches = Performance.BatchesSent;
            if (!c.Check(list.Count == 1 && Performance.StartAuto(list[0], -1, out var error), "own MIDI file plays"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(2);
            var longest = 0f;
            foreach (var n in Performance.TestNotes)
            {
                longest = Mathf.Max(longest, n.Length);
            }
            c.Check(Performance.TestNotes.Length == 6 && longest > 5.9f, $"the long note is kept whole ({F(longest)} s)");
            // 0..3 s: the first notes go out in batches (the notes themselves, no file).
            yield return WaitFor(() => Performance.TestClock > 3f, 5f);
            c.Check(Performance.NotesPlayed == played + 3 && Performance.BatchesSent > batches, "its notes are sent as notes (a listener needs no file)");
            c.Check((Performance.LastFlags & BatchFlags.Live) == 0, "as a song playing by itself (not live)");
            // 3..11.5 s: nothing new to send, yet a batch goes out every second (listeners keep the performance).
            played = Performance.NotesPlayed;
            batches = Performance.BatchesSent;
            var rms = new float[1];
            yield return WaitFor(() => Performance.TestClock > 6f, 5f);
            yield return Measure(Performance.LocalEmitter, 0.5f, rms);
            c.Check(rms[0] > 0.001f, $"the long note still sounds after five seconds (rms {F(rms[0])})");
            yield return WaitFor(() => Performance.TestClock > 9.5f, 6f);
            yield return Measure(Performance.LocalEmitter, 0.5f, rms);
            c.Check(rms[0] < 0.0002f, $"the rest is silent (rms {F(rms[0])})");
            yield return WaitFor(() => Performance.TestClock > 11.5f, 4f);
            var alive = Performance.BatchesSent - batches;
            c.Check(Performance.NotesPlayed == played && alive >= 7 && alive <= 11, $"during the long note and the rest a batch still goes out every second ({alive} in 8.5 s)");
            c.Check(Performance.Mode == PerformanceMode.Auto, "the song is still on");
            // After the rest the song goes on.
            yield return WaitFor(() => Performance.NotesPlayed >= played + 3, 4f);
            c.Check(Performance.NotesPlayed == played + 3, "after the rest the last notes are sent");
            Performance.TestRequestStop();
            yield return Frames(3);

            // Rhythm game: only the notes of the chart notes that were hit go out.
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var pressed = new HashSet<long>();
            var expected = 0;
            var chartPlayed = Performance.ChartNotesPlayed;
            var seen = 0;
            var began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < Judge.LeadIn + 7f && Performance.Mode == PerformanceMode.MiniGame)
            {
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (!pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.005f))
                    {
                        pressed.Add(key);
                        seen++;
                        if (seen % 2 == 1)
                        {
                            LaneKeys.TestPress[game.Lane(v.Value)] = true;
                            expected += game.Chart.Notes[v.Value].Count;
                        }
                    }
                }
                yield return null;
            }
            yield return Frames(3);
            var hits = (seen + 1) / 2;
            c.Check(game != null && game.Judge.Hits == hits && hits >= 5, $"every other note hit ({(game != null ? game.Judge.Hits : 0)} of {seen})");
            c.Check(Performance.ChartNotesPlayed - chartPlayed == expected && expected > 0,
                $"only the hit notes are sent ({Performance.ChartNotesPlayed - chartPlayed} notes for {hits} hits, {expected} expected), none for the missed ones");
            c.Check((Performance.LastFlags & BatchFlags.Live) != 0, "flagged live (listeners play them a quarter second later)");
            c.Check(Mathf.Approximately(Listeners.LiveDelay, 0.25f), "live delay is a quarter of a second");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.keys (real key and mouse events: T04 T08 T15 T22 T30 T32 T33) ----------

    // Everything the mod reads straight from the keyboard and mouse (Esc, right mouse button, arrows, Enter, the lane
    // keys, the piano keys, Space) pressed for real: events put into the game's Input System, the same place a real
    // key press lands. The Input System drops them while the game window is behind another one (first run failed on
    // that): RealKeysOn tells it to ignore focus for this test; when keyboard or mouse still take nothing the test
    // fails and says so.
    private static IEnumerator RunKeys()
    {
        var c = new Checks(KeysName);
        var player = Player.m_localPlayer;
        if (player == null || Menu.instance == null)
        {
            SelfTest.Fail(KeysName, "no player or menu");
            yield break;
        }
        // Window behind another one (an unattended run): the Input System is told to take the events anyway.
        var forced = new bool[1];
        if (!RealKeysOn(forced))
        {
            RealKeysBack();
            SelfTest.Fail(KeysName, $"real key presses cannot be sent: game window in front {Application.isFocused}, keyboard {Keyboard.current != null}"
                                    + $"{(Keyboard.current != null ? " (on " + Keyboard.current.enabled + ")" : "")}, mouse {Mouse.current != null}"
                                    + $"{(Mouse.current != null ? " (on " + Mouse.current.enabled + ")" : "")}, runs in the background {Application.runInBackground}. "
                                    + "Not a fault of the mod: run this test again with the game window in front.");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            c.Note($"game window in front: {Application.isFocused}" + (forced[0] ? "; key and mouse events are sent with focus ignored (Input System setting, put back after)" : ""));
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            x.SongsFolder("MC_MusicRealKeysTest");
            x.Console(true);
            PadInput.Test = false;
            LaneKeys.TestSet(0, KeyCode.D);
            LaneKeys.TestSet(1, KeyCode.F);
            LaneKeys.TestSet(2, KeyCode.J);
            LaneKeys.TestSet(3, KeyCode.K);
            Performance.LastSongId = null;
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            // The game reads keyboard and mouse itself from here on.
            x.Controller(true);
            var ids = SongIds();
            var first = SongWindow.SelectedId;
            if (!c.Check(SongWindow.IsOpen && ids.Count >= 9 && first == ids[0], "window open"))
            {
                c.Report();
                yield break;
            }

            // Down arrow, Enter.
            KeysDown(KeyCode.DownArrow);
            yield return WaitReal(() => SongWindow.SelectedId != first, 1f);
            KeysDown();
            if (!c.Check(SongWindow.SelectedId == ids[1], "Down arrow key chooses the next song"))
            {
                c.Note($"the key event did not reach the game (window in front now: {Application.isFocused}); the rest is not tried");
                c.Report();
                yield break;
            }
            yield return Frames(3);
            KeysDown(KeyCode.Return);
            var typing = false;
            var began = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - began < 1f && Performance.Mode != PerformanceMode.Auto)
            {
                typing |= ChatTyping;
                yield return null;
            }
            KeysDown();
            for (var i = 0; i < 5; i++)
            {
                typing |= ChatTyping;
                yield return null;
            }
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == ids[1], "Enter key plays the selected song");
            c.Check(!typing, "Enter does not open the chat");
            Performance.TestRequestStop();
            yield return Frames(3);

            // Esc closes the window, the menu stays shut.
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen, "window open again");
            KeysDown(KeyCode.Escape);
            yield return WaitReal(() => !SongWindow.IsOpen, 1f);
            var menu = false;
            for (var i = 0; i < 8; i++)
            {
                menu |= MenuShown;
                yield return null;
            }
            KeysDown();
            yield return Frames(3);
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen, "Esc key closes the window");
            c.Check(!menu && !MenuShown, "Esc does not open the menu");
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // Right mouse button closes the window, no block.
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen, "window open a third time");
            MouseRight(true);
            yield return WaitReal(() => !SongWindow.IsOpen, 1f);
            yield return Frames(4);
            c.Check(!SongWindow.IsOpen, "right mouse button closes the window");
            c.Check(!player.IsBlocking() && !player.m_blocking, "the same click does not raise the block");
            MouseRight(false);
            yield return Frames(3);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // Rhythm game: D F J K for real.
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var at = player.transform.position;
            var pressed = new HashSet<long>();
            var down = false;
            var releaseAt = 0;
            began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < Judge.LeadIn + 7f && Performance.Mode == PerformanceMode.MiniGame)
            {
                if (down && Time.frameCount >= releaseAt)
                {
                    KeysDown();
                    down = false;
                }
                else if (!down)
                {
                    foreach (var v in game.Visible)
                    {
                        var key = NoteKey(v);
                        if (!pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.03f))
                        {
                            pressed.Add(key);
                            KeysDown(LaneKeys.Key(game.Lane(v.Value)));
                            down = true;
                            releaseAt = Time.frameCount + 3;
                            break;
                        }
                    }
                }
                yield return null;
            }
            KeysDown();
            yield return Frames(3);
            c.Note($"real lane keys: {pressed.Count} presses, hits {(game != null ? game.Judge.Hits : 0)}, misses {(game != null ? game.Judge.Misses : 0)}, strays {(game != null ? game.Judge.Strays : 0)}");
            c.Check(game != null && pressed.Count >= 6 && game.Judge.Hits >= pressed.Count - 2, "the keys D F J K hit the notes of their lanes");
            c.Check(Flat(player.transform.position, at) < 0.05f && Performance.Mode == PerformanceMode.MiniGame, "and do not walk (D is a walking key)");
            // Lane 1 on A, then on F5 (the console key).
            if (game != null && Performance.Mode == PerformanceMode.MiniGame)
            {
                LaneKeys.TestSet(0, KeyCode.A);
                var before = game.LanePressedAt[0];
                KeysDown(KeyCode.A);
                yield return Frames(4);
                KeysDown();
                c.Check(game.LanePressedAt[0] > before, "lane 1 set to A: the A key plays lane 1");
                yield return new WaitForSeconds(0.3f);
                c.Check(Flat(player.transform.position, at) < 0.05f, "and the game does not walk left");
                LaneKeys.TestSet(0, KeyCode.F5);
                before = game.LanePressedAt[0];
                KeysDown(KeyCode.F5);
                var console = false;
                for (var i = 0; i < 6; i++)
                {
                    console |= global::Console.IsVisible();
                    yield return null;
                }
                KeysDown();
                c.Check(game.LanePressedAt[0] > before, "lane 1 set to F5: the F5 key plays lane 1");
                c.Check(!console && Performance.Mode == PerformanceMode.MiniGame, "and the console does not open");
                LaneKeys.TestSet(0, KeyCode.D);
                yield return Frames(3);
            }
            // Right mouse button stops the rhythm game.
            MouseRight(true);
            yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1f);
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None && !player.m_blocking, "right mouse button stops the rhythm game, no block");
            MouseRight(false);
            yield return Frames(3);
            // Esc stops it and opens the menu.
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs again: " + error);
            yield return Frames(6);
            KeysDown(KeyCode.Escape);
            yield return WaitReal(() => MenuShown, 1.5f);
            yield return Frames(3);
            KeysDown();
            c.Check(MenuShown && Performance.Mode == PerformanceMode.None, "Esc key opens the menu and stops the rhythm game");
            if (MenuShown)
            {
                Menu.instance.Hide();
            }
            yield return Real(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // Free play: Q, Space + Q, Space let go, Tab.
            c.Check(Performance.StartFreePlay(out error), "free play: " + error);
            yield return Frames(3);
            at = player.transform.position;
            KeysDown(KeyCode.Q);
            yield return WaitReal(() => Performance.FreeFlutePitch == 72, 1f);
            c.Check(Performance.FreeFlutePitch == 72 && MiniGameHud.FreeKeyLit(12), "the Q key plays and holds C5");
            yield return new WaitForSeconds(0.4f);
            c.Check(!player.m_autoRun && Flat(player.transform.position, at) < 0.05f, "and does not start auto-run");
            KeysDown();
            yield return WaitReal(() => Performance.FreeFlutePitch < 0, 1f);
            c.Check(Performance.FreeFlutePitch < 0 && MiniGameHud.FreeLitCount == 0, "Q let go: the note ends");
            KeysDown(KeyCode.Space);
            yield return Frames(4);
            var jumped = player.m_body != null && player.m_body.linearVelocity.y > 3f;
            c.Check(MiniGameHud.FreeTitle != null && MiniGameHud.FreeTitle.Contains("octave up"), "the Space key shows the octave up");
            KeysDown(KeyCode.Space, KeyCode.Q);
            yield return WaitReal(() => Performance.FreeFlutePitch == 84, 1f);
            jumped |= player.m_body != null && player.m_body.linearVelocity.y > 3f;
            c.Check(Performance.FreeFlutePitch == 84, "Space + Q plays C6");
            KeysDown(KeyCode.Q);
            yield return Frames(5);
            jumped |= player.m_body != null && player.m_body.linearVelocity.y > 3f;
            c.Check(Performance.FreeFlutePitch == 84, "Space let go while Q is held: the note keeps its pitch");
            c.Check(!jumped, "Space does not jump");
            KeysDown();
            yield return WaitReal(() => Performance.FreeFlutePitch < 0, 1f);
            KeysDown(KeyCode.Tab);
            var inventory = false;
            for (var i = 0; i < 6; i++)
            {
                inventory |= InventoryGui.IsVisible();
                yield return null;
            }
            KeysDown();
            yield return Frames(2);
            c.Check(!inventory && Performance.Mode == PerformanceMode.FreePlay, "the Tab key does not open the inventory");
            // Q and Space held, then the right mouse button: free play stops, the character stays.
            KeysDown(KeyCode.Space, KeyCode.Q);
            yield return Frames(4);
            MouseRight(true);
            yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1f);
            c.Check(Performance.Mode == PerformanceMode.None, "right mouse button stops free play");
            jumped = false;
            began = Time.time;
            while (Time.time - began < 0.6f)
            {
                jumped |= player.m_body != null && player.m_body.linearVelocity.y > 3f;
                yield return null;
            }
            c.Check(!player.m_autoRun && !jumped && Flat(player.transform.position, at) < 0.1f && !player.m_blocking,
                "with Q and Space still held: no auto-run, no jump, no block");
            MouseRight(false);
            KeysDown();
            yield return Frames(4);
            // Pressed again the keys are the game's again (when Q is this game's auto-run key).
            KeysDown(KeyCode.Q);
            yield return Frames(6);
            var autoRunKey = ZInput.GetButton("AutoRun");
            if (autoRunKey)
            {
                yield return WaitFor(() => player.m_autoRun, 1f);
                c.Check(player.m_autoRun, "Q pressed again starts auto-run as usual");
            }
            else
            {
                c.Note("Q is not the auto-run key in this game's key bindings: \"keys work again\" is checked by music.freeplay.keys only");
            }
            KeysDown();
            yield return Frames(3);
            player.m_autoRun = false;
            x.Controller(false);
            rig.TakeControls();
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.sleep (X02: Sleep Through the Day) ----------

    private static IEnumerator RunSleep()
    {
        var c = new Checks(SleepName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SleepName, "no player");
            yield break;
        }
        const string sleepGuid = "MC.Exploration.Sleep.ThroughDay";
        var sleepMod = FeatureRegistry.Find(sleepGuid);
        if (sleepMod == null || !sleepMod.Value.IsActive)
        {
            SelfTest.Fail(SleepName, "Sleep Through the Day is not loaded and active in this game: the two mods cannot be checked together");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var rules = TestRules();
            rules.BonusMinutes = 10f;
            ServerRules.TestRules = rules;
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            var baseComfort = player.m_comfortLevel;
            c.Check(MusicBonus.Grant(player, false) && player.GetComfortLevel() == baseComfort + 3, $"Music on: comfort {baseComfort} + 3");
            Seen.Clear();
            // Falling asleep and waking up, as the game does it for a bed (both mods patch this wake-up).
            player.SetSleeping(true);
            yield return Frames(3);
            player.SetSleeping(false);
            yield return Frames(2);
            var wanted = baseComfort + 3;
            c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start ($se_rested_comfort:" + wanted + ")") == 1,
                $"waking up: \"You feel rested (Comfort: {wanted})\": " + Seen.Tail());
            var rested = seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
            c.Check(rested != null && Mathf.Abs(rested.m_ttl - (rested.m_baseTTL + (wanted - 1) * rested.m_TTLPerComfortLevel)) < 0.5f,
                $"Rested lasts as long as that comfort gives ({F(rested != null ? rested.m_ttl : -1f)} s)");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.crossmod (parts of X03, X04, X05 that need no other mod's code) ----------

    private static IEnumerator RunCrossMod()
    {
        var c = new Checks(CrossModName);
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null || InventoryGui.instance == null)
        {
            SelfTest.Fail(CrossModName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            // Sort Chest and Crafting Search and Sort group items by this shared kind: instruments are tools.
            var hammer = db.GetItemPrefab("Hammer");
            var knife = db.GetItemPrefab(InstrumentContent.VanillaKnifeName);
            var tools = true;
            foreach (var kind in InstrumentContent.Kinds)
            {
                tools &= ItemKinds.Classify(SharedOf(kind)) == ItemKind.Tool;
            }
            c.Check(tools && hammer != null && ItemKinds.Classify(hammer.GetComponent<ItemDrop>().m_itemData.m_shared) == ItemKind.Tool,
                "the three instruments are sorted as tools, like the hammer");
            c.Check(knife != null && ItemKinds.Classify(knife.GetComponent<ItemDrop>().m_itemData.m_shared) != ItemKind.Tool, "a knife is not");
            // Dual Wielding pairs one-handed weapons only: an instrument is a tool, never a weapon.
            c.Check(SharedOf(InstrumentKind.Flute).m_itemType == ItemDrop.ItemData.ItemType.Tool && SharedOf(InstrumentKind.Flute).m_skillType == Skills.SkillType.None,
                "an instrument is no one-handed weapon (nothing to pair)");

            // Another screen with a text field open (inventory with a search field, Encyclopedia): our window does not
            // open over it, a running rhythm game ends, and nothing of ours keeps holding the keys.
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            InventoryGui.instance.Show(null);
            yield return Frames(3);
            c.Check(InventoryGui.IsVisible() && GameScreens.AnyOpen(), "inventory open");
            yield return Click(rig);
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen && !KeyCapture.Active, "with another screen open the click opens no song window");
            InventoryGui.instance.Hide();
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return new WaitForSeconds(0.5f);
            c.Check(KeyCapture.Active && Chat.instance.HasFocus(), "rhythm game holds the keys");
            InventoryGui.instance.Show(null);
            yield return Frames(4);
            c.Check(Performance.Mode == PerformanceMode.None && !KeyCapture.Active, "another screen opening ends the rhythm game and frees the keys");
            InventoryGui.instance.Hide();
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return Frames(3);
            var controller = x.ControllerObject;
            c.Check(!Chat.instance.HasFocus() && controller != null && controller.TakeInput(false) && player.TakeInput(), "afterwards the game takes every key again");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.spyglass (X01: Spyglass) ----------

    private static IEnumerator RunSpyglass()
    {
        var c = new Checks(SpyglassName);
        var player = Player.m_localPlayer;
        var camera = Utils.GetMainCamera();
        const string spyglassItem = "MC_Spyglass";
        if (player == null || camera == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(SpyglassName, "no player or camera");
            yield break;
        }
        if (ObjectDB.instance.GetItemPrefab(spyglassItem) == null)
        {
            SelfTest.Fail(SpyglassName, "the Spyglass mod is not loaded in this game (no " + spyglassItem + " item): the two mods cannot be checked together");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            yield return new WaitForSeconds(0.3f);
            var view = camera.fieldOfView;
            var spyglass = rig.Give(spyglassItem);
            var flute = rig.Give(InstrumentContent.ItemName(InstrumentKind.Flute));
            if (!c.Check(spyglass != null && flute != null, "spyglass and flute given"))
            {
                c.Report();
                yield break;
            }
            // Spyglass in hand, its button held (Block: how the spyglass is raised is the other mod's business; noted).
            player.EquipItem(spyglass, false);
            yield return new WaitForSeconds(0.5f);
            for (var i = 0; i < 60; i++)
            {
                player.SetControls(Vector3.zero, false, false, false, false, i == 0, true, false, false, false, false);
                yield return null;
            }
            c.Note($"spyglass with Block held: view {F(camera.fieldOfView)} degrees (normal {F(view)})");
            c.Check(!SongWindow.IsOpen && Performance.Mode == PerformanceMode.None && Performance.Held == InstrumentKind.None,
                "with the spyglass in hand nothing of the instruments acts");
            // The instrument takes the hands while the spyglass button is still held: the view is normal again.
            player.EquipItem(flute, false);
            rig.TakeControls();
            yield return new WaitForSeconds(1.2f);
            c.Check(player.GetRightItem() == flute && !player.IsItemEquiped(spyglass), "the flute puts the spyglass away");
            c.Check(Mathf.Abs(camera.fieldOfView - view) < 1f, $"the view is the normal one ({F(camera.fieldOfView)} degrees)");
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "the flute plays: " + error);
            yield return new WaitForSeconds(1.3f);
            var emitter = Performance.LocalEmitter;
            var rms = new float[1];
            yield return Measure(emitter, 0.5f, rms);
            c.Check(Performance.Mode == PerformanceMode.Auto && InstrumentPose.Weight(player) > 0.9f && rms[0] > 0.002f, "playing, posed, heard");
            // The spyglass again, mid-song.
            player.EquipItem(spyglass, false);
            yield return Frames(3);
            c.Check(player.GetRightItem() == spyglass && Performance.Mode == PerformanceMode.None, "the spyglass in hand stops the song at once");
            yield return new WaitForSeconds(0.8f);
            yield return Measure(emitter, 0.4f, rms);
            c.Check(InstrumentPose.Weight(player) < 0.01f && PlayingFlag(player) == 0 && rms[0] < 0.0002f, "no playing pose and no sound left");
            yield return Click(rig);
            c.Check(!SongWindow.IsOpen, "a click with the spyglass opens no song window");
            rig.TakeControls();
            // And the flute once more.
            player.EquipItem(flute, false);
            yield return new WaitForSeconds(0.5f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && Mathf.Abs(camera.fieldOfView - view) < 1f, "flute in hand again: the click opens the song window, normal view");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.cleanlog (T21) ----------

    // Every warning and error this mod wrote to the log since it started, but the ones the tests ask for (a lane key
    // the game cannot use, the test files of the server songs folder) and the tests' own lines.
    private static IEnumerator RunCleanLog()
    {
        var all = LogTap.ProblemsSince(0);
        var bad = new List<string>();
        var expected = 0;
        foreach (var line in all)
        {
            if (LogTap.Expected(line))
            {
                expected++;
            }
            else
            {
                bad.Add(line.Length > 220 ? line.Substring(0, 220) + "..." : line);
            }
        }
        SelfTest.Note(CleanLogName, $"{all.Count} warning or error line(s) from Music Instruments since it started; {expected} asked for by the tests or written by them");
        for (var i = 0; i < bad.Count && i < 8; i++)
        {
            SelfTest.Note(CleanLogName, "unexpected: " + bad[i]);
        }
        if (bad.Count == 0)
        {
            SelfTest.Pass(CleanLogName, "no warning or error from Music Instruments in the log (except the ones the tests provoke)");
        }
        else
        {
            SelfTest.Fail(CleanLogName, $"{bad.Count} unexpected warning or error line(s) from Music Instruments, first: {bad[0]}");
        }
        yield break;
    }
}
#endif
