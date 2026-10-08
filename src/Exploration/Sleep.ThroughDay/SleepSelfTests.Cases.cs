#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SleepThroughDayMod;

// Debug build only. Single-player tests of cases a lone player cannot make by hand:
//   sleep.sim.*     second player stood in for by the game's "is everybody in bed?" answer (TESTING.md M01, M04,
//                   M06, M09): server part is the same code in single player and on a host;
//   sleep.latewake, sleep.dawn, sleep.tod, sleep.noskip   edge cases (T18-T21);
//   sleep.bug.noskip-evening   the one check of T21 the mod fail (real bug, not fixed here): alone, so others pass;
//   sleep.compat.*  other MC mods that are really loaded (X01, X02), and stand-ins for what BedRules, SkipSleep and
//                   Seasons do to the game (X03-X05): real third-party mod still need a hand test.
internal static partial class SleepSelfTests
{
    private const string SimWaiting = "sleep.sim.waiting";
    private const string SimSplit = "sleep.sim.split";
    private const string SimPredawn = "sleep.sim.predawn";
    private const string LateWake = "sleep.latewake";
    private const string Dawn = "sleep.dawn";
    private const string Tod = "sleep.tod";
    private const string NoSkipName = "sleep.noskip";
    private const string BugNoSkipEvening = "sleep.bug.noskip-evening";
    private const string CompatCounts = "sleep.compat.counts";
    private const string CompatInteract = "sleep.compat.interact";
    private const string CompatAnyTime = "sleep.compat.anytime";
    private const string CompatRatio = "sleep.compat.ratio";
    private const string CompatDayCycle = "sleep.compat.daycycle";

    private const string CountsGuid = "MC.Exploration.Stats.PerCreature";
    private const string BatchFeedGuid = "MC.Crafting.Stations.BatchFeed";
    private const string PickupFilterGuid = "MC.UX.AutoPickup.Filter";

    private static KeyValuePair<string, Func<IEnumerator>>[] _caseTests;

    private static KeyValuePair<string, Func<IEnumerator>>[] CaseTests() => _caseTests ??= new[]
    {
        T(SimWaiting, RunSimWaiting),
        T(SimSplit, RunSimSplit),
        T(SimPredawn, RunSimPredawn),
        T(LateWake, RunLateWake),
        T(Dawn, RunDawn),
        T(Tod, RunTod),
        T(NoSkipName, RunNoSkip),
        T(BugNoSkipEvening, RunBugNoSkipEvening),
        T(CompatCounts, RunCompatCounts),
        T(CompatInteract, RunCompatInteract),
        T(CompatAnyTime, RunCompatAnyTime),
        T(CompatRatio, RunCompatRatio),
        T(CompatDayCycle, RunCompatDayCycle),
    };

    // ---------- second player stood in for ----------

    // M01 / M09, server part: morning, one player in bed, another one up ("everybody in bed?" = no). Nothing start,
    // player stay lying. Other one lie down too = day sleep.
    private static IEnumerator RunSimWaiting()
    {
        const string N = SimWaiting;
        yield return Settle();
        var error = Begin(N, 8f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 8f);
            _everybodyForced = false; // another player is up
            var asked = _everybodyAsked;
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            var t0 = c.Net.GetTimeSeconds();
            var r0 = Time.realtimeSinceStartup;
            var started = false;
            while (Time.realtimeSinceStartup - r0 < 6.5f)
            {
                started = started || _asleep || c.Game.m_sleeping || c.Env.IsTimeSkipping();
                yield return null;
            }
            var flowed = c.Net.GetTimeSeconds() - t0;
            var lines = _watch.Count(c.LogMark, StartLine) + _watch.Count(c.LogMark, RetargetLine);
            var questions = _everybodyAsked - asked;
            Report(c, !started && c.Player.IsAttached() && c.Player.InBed() && questions >= 2 && lines == 0 && Math.Abs(flowed - 6.5) < 2.0,
                $"another player is up: over 6.5 s (3 server checks) sleep started {started}, player still lying {c.Player.InBed()}, the server "
                + $"asked \"is everybody in bed?\" {questions} time(s), world time moved {flowed:0.0} s, \"Day sleep:\" lines {lines} "
                + "(expected no sleep, still lying, at least 2 questions, about 6.5 s, no line)");

            _everybodyForced = null; // the other player lies down too
            c.LieDownReal = Time.realtimeSinceStartup;
            var plan = Plan.ToHour(8f, 18f);
            plan.InBed = true;
            plan.CheckDelay = true;
            yield return Sleep(c, plan);
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // M04: A in bed at 11:55, B only at 12:05. It is the time the sleep START that count: vanilla sleep to next
    // morning, "Good morning" for A (no "Good evening"), no Info line.
    private static IEnumerator RunSimSplit()
    {
        const string N = SimSplit;
        yield return Settle();
        var error = Begin(N, 11.92f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return WaitFlags(c, 11.92f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 11.92f);
            _everybodyForced = false; // B is still up
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            var r0 = Time.realtimeSinceStartup;
            var started = false;
            while (Time.realtimeSinceStartup - r0 < 2.5f)
            {
                started = started || _asleep || c.Game.m_sleeping;
                yield return null;
            }
            Report(c, !started && c.Player.InBed(), $"A lies in bed at {Clock(c.Env, c.Net.GetTimeSeconds())} while B is up: sleep started {started} (expected no)");

            // Noon pass while A lie there.
            SetTime(c.Env, c.Net, c.Day * c.Length + DayClock.RawFractionForHour(c.Env, 12.0833f) * c.Length);
            yield return WaitFlags(c, 12.0833f);
            if (!c.OK)
            {
                yield break;
            }
            Report(c, c.Player.InBed() && !_asleep, $"A still lies in bed at {Clock(c.Env, c.Net.GetTimeSeconds())}, no sleep yet");
            _everybodyForced = null; // B lies down
            var plan = Plan.ToMorning(12.0833f);
            plan.InBed = true;
            yield return Sleep(c, plan);
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // M06, server and modded-player part: a player in bed since before dawn (lay down at night, vanilla rule) is
    // still lying when the last one lie down in the morning: day sleep to 18:00 for everybody.
    private static IEnumerator RunSimPredawn()
    {
        const string N = SimPredawn;
        yield return Settle();
        var error = Begin(N, 5.75f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return WaitFlags(c, 5.75f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 5.75f);
            _everybodyForced = false; // the other player stays up
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            var r0 = Time.realtimeSinceStartup;
            var started = false;
            while (Time.realtimeSinceStartup - r0 < 2.5f)
            {
                started = started || _asleep || c.Game.m_sleeping;
                yield return null;
            }
            Report(c, !started && c.Player.InBed(), $"in bed before dawn ({Clock(c.Env, c.Net.GetTimeSeconds())}) while the other player is up: sleep started {started} (expected no)");

            SetTime(c.Env, c.Net, c.Day * c.Length + DayClock.RawFractionForHour(c.Env, 6.5f) * c.Length);
            yield return WaitFlags(c, 6.5f);
            if (!c.OK)
            {
                yield break;
            }
            Report(c, c.Player.InBed() && !_asleep, $"still lying at {Clock(c.Env, c.Net.GetTimeSeconds())}, in bed since before dawn, no sleep yet");
            _everybodyForced = null; // the other player lies down, in the morning
            var plan = Plan.ToHour(6.5f, 18f);
            plan.InBed = true;
            yield return FallAsleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            Report(c, WakeMessage.HaveStart && WakeMessage.StartedAfterDawn && WakeMessage.StartDay == c.Day,
                $"the game of the player who lay down before dawn notes the sleep START: after dawn {WakeMessage.StartedAfterDawn}, day {WakeMessage.StartDay} (expected after dawn, day {c.Day})");
            yield return WakeUp(c, plan);
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // ---------- edge cases ----------

    // T19 (and client rule of M11): WakeUpHour 23, stop of a morning sleep come late, on next date (in play: server
    // wait for host's dream video while world time run on). Test move end of the running skip there. Before 05:00
    // of next date = still daytime sleep; after = night sleep.
    private static IEnumerator RunLateWake()
    {
        const string N = LateWake;
        var hours = new[] { 0.5f, 4.5f, 5.5f };
        var tags = new[] { "woken at 00:30", "woken at 04:30", "woken at 05:30" };
        yield return Settle();
        var setup = new Setup { WakeHour = 23f };
        var error = Begin(N, 8f, setup, out var c, tags[0]);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            for (var i = 0; i < hours.Length; i++)
            {
                if (i > 0)
                {
                    SetRun(c, 8f, setup, tags[i]);
                }
                var plan = Plan.ToHour(8f, 23f);
                yield return FallAsleep(c, plan);
                if (!c.OK)
                {
                    yield break;
                }
                var late = (c.Day + 1) * c.Length + DayClock.RawFractionForHour(c.Env, hours[i]) * c.Length;
                c.Env.m_skipToTime = late;
                c.SkipTo = late;
                var daytime = hours[i] < 5f;
                plan.EndDay = 1;
                plan.EndClock = hours[i] / 24f;
                plan.Day = daytime;
                plan.First = daytime ? DefaultMessage : WakeMessage.GoodMorningToken;
                Note(c, $"wake-up held back: end of the running skip moved from 23:00 to {Clock(c.Env, late)} of day {c.Day + 1}");
                yield return WakeUp(c, plan);
                if (!c.OK)
                {
                    yield break;
                }
            }
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T18: raw world time just past 06:00, smoothed clock (day flags) still say night. Vanilla start a sleep that
    // would skip a whole day; mod cut it to wake hour of same date, and player's game call it daytime sleep.
    private static IEnumerator RunDawn()
    {
        const string N = Dawn;
        yield return Settle();
        var error = Begin(N, 6f, new Setup { ExtraSeconds = 5.0 }, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var env = c.Env;
            // Hold smoothed clock just before 06:00 (what it show for a second or two after dawn), so case is sure.
            env.m_debugTime = 0.24f;
            env.m_debugTimeOfDay = true;
            var until = Time.realtimeSinceStartup + FlagWaitSeconds;
            while (!EnvMan.IsNight() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            yield return FixedSteps(3);
            var now = c.Net.GetTimeSeconds();
            var ready = EnvMan.IsNight() && DayClock.IsAfterDawn(env, now) && EnvMan.CanSleep();
            Report(c, ready, $"world time {Clock(env, now)} is past 06:00 ({now - c.Day * c.Length - VanillaRaw(0.25f) * c.Length:0.0} s) while the day flags "
                             + $"still say night ({EnvMan.IsNight()}); vanilla lets the player sleep: {EnvMan.CanSleep()}");
            if (!ready)
            {
                yield break;
            }
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitStart(c, StartWaitSeconds);
            env.m_debugTimeOfDay = false; // clock free again at once
            if (!c.OK)
            {
                yield break;
            }
            var plan = Plan.ToHour(6f, 18f, DefaultMessage, Starter.Retarget);
            CheckStart(c, plan);
            Report(c, WakeMessage.HaveStart && WakeMessage.StartedAfterDawn && WakeMessage.StartDay == c.Day,
                $"the player's game notes the start as after dawn {WakeMessage.StartedAfterDawn}, day {WakeMessage.StartDay} (expected after dawn, day {c.Day})");
            yield return WakeUp(c, plan);
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T20: real time 22:00, time of day forced to morning (console `tod 0.35`). Bed let player lie down, but no sleep
    // start (world time outside day window), mod say why once (Debug). Forced time gone = vanilla night sleep.
    private static IEnumerator RunTod()
    {
        const string N = Tod;
        yield return Settle();
        var error = Begin(N, 22f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var env = c.Env;
            env.m_debugTime = 0.35f;
            env.m_debugTimeOfDay = true;
            var until = Time.realtimeSinceStartup + FlagWaitSeconds;
            while (!DayClock.IsMorning() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            yield return FixedSteps(3);
            c.Tag = "forced morning";
            var can = EnvMan.CanSleep();
            Report(c, DayClock.IsMorning() && can, $"world time {Clock(env, c.Net.GetTimeSeconds())}, day flags say morning {DayClock.IsMorning()}: Can sleep {can} (expected yes)");
            if (!can)
            {
                yield break;
            }
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            var r0 = Time.realtimeSinceStartup;
            var started = false;
            while (Time.realtimeSinceStartup - r0 < 10f)
            {
                started = started || _asleep || c.Game.m_sleeping || env.IsTimeSkipping();
                yield return null;
            }
            var lines = _watch.Count(c.LogMark, OutsideLine);
            Report(c, !started && c.Player.InBed() && DaySleep.OutsideWindowLogged && lines == 1,
                $"over 10 s (5 server checks) sleep started {started}, player still lying {c.Player.InBed()}, Debug line "
                + $"{Quote(_watch.First(c.LogMark, OutsideLine))} written {lines} time(s) (expected no sleep, still lying, the line once)");

            env.m_debugTimeOfDay = false; // tod -1
            var todOff = Time.realtimeSinceStartup;
            until = Time.realtimeSinceStartup + FlagWaitSeconds;
            while (!EnvMan.IsNight() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            c.Tag = "forced time ended";
            c.LieDownReal = Time.realtimeSinceStartup;
            var plan = Plan.ToMorning(22f);
            plan.InBed = true;
            yield return FallAsleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            Report(c, c.StartReal - todOff <= 8f, $"the night sleep started {c.StartReal - todOff:0.0} s after the forced time of day ended, "
                                                  + "the player still lying (expected within a few seconds: 8 s at most)");
            Report(c, !DaySleep.OutsideWindowLogged, $"the \"not started\" line may show again for the next sleep: flag cleared {!DaySleep.OutsideWindowLogged}");
            yield return WakeUp(c, plan);
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T21: 20:00, another mod's SkipToMorning start no time skip (stand-in: prefix that skip it). Vanilla start the
    // sleep and stop it 2 s later, same date; server part of the mod no touch it (no Info line), player get Rested.
    private static IEnumerator RunNoSkip() => NoSkipSleep(NoSkipName, false);

    // T21, the one check the mod fail (run 1: "Good evening", "daytime sleep"): server never made that 20:00 sleep a
    // day sleep, so it must stay night sleep with "Good morning". WakeMessage.IsDaySleepWake call every sleep that
    // start after 06:00 and wake on same date a day sleep. Alone here, so sleep.noskip can pass. Fail until mod fixed.
    private static IEnumerator RunBugNoSkipEvening() => NoSkipSleep(BugNoSkipEvening, true);

    private static IEnumerator NoSkipSleep(string N, bool messageCheck)
    {
        yield return Settle();
        var setup = new Setup
        {
            Patches = h => h.Patch(Find(typeof(EnvMan), nameof(EnvMan.SkipToMorning)), prefix: new HarmonyMethod(typeof(SleepSelfTests), nameof(NoSkip))),
        };
        var error = Begin(N, 20f, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            _noSkip = true;
            yield return WaitFlags(c, 20f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 20f);
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitStart(c, StartWaitSeconds);
            if (!c.OK)
            {
                yield break;
            }
            var noSkip = !c.Skipping && c.Player.IsSleeping();
            if (!messageCheck)
            {
                Report(c, noSkip, $"sleep started at {Clock(c.Env, c.StartTime)} without a time skip (time skip running: {c.Skipping})");
            }
            else if (!noSkip)
            {
                Fail(c, $"cannot make the case: the sleep started at {Clock(c.Env, c.StartTime)} with a time skip (running: {c.Skipping})");
                yield break;
            }
            yield return WaitWake(c, 10f);
            if (!c.OK)
            {
                yield break;
            }
            var wakeDay = c.Env.GetDay(_wakeTime);
            var centre = WakeCentre();
            var verdict = !_modWoke ? "the mod said nothing" : _modWakeDay ? "the mod calls it a daytime sleep" : "the mod calls it a night sleep";
            if (messageCheck && wakeDay != c.Day)
            {
                Fail(c, $"cannot make the case: the sleep ended on day {wakeDay}, not on the day it started ({c.Day})");
            }
            else if (messageCheck)
            {
                // The one check of sleep.bug.noskip-evening.
                var line = _watch.First(c.LogMark, WokeLine);
                Report(c, _modWoke && !_modWakeDay && centre.Count > 0 && centre[0] == WakeMessage.GoodMorningToken
                          && _watch.Count(c.LogMark, WokeLine) == 1 && _watch.Has(c.LogMark, WokeLine, "night sleep, vanilla message."),
                    $"a sleep that started at {Clock(c.Env, c.StartTime)}, after nightfall, ended at {Clock(c.Env, _wakeTime)} of the same day without a time "
                    + $"skip, and that the server never turned into a daytime sleep: wake-up messages {DescribeWake()}, {verdict}, Debug line {Quote(line)} "
                    + $"(expected \"{Localize(WakeMessage.GoodMorningToken)}\" first, night sleep, \"{WokeLine} ...: night sleep, vanilla message.\")");
            }
            else
            {
                Report(c, wakeDay == c.Day && _wakeTime - c.StartTime < 10.0,
                    $"woke up at {Clock(c.Env, _wakeTime)} of day {wakeDay}, {_wakeTime - c.StartTime:0.0} s of world time later (expected the same day {c.Day})");
                var lines = _watch.Count(c.LogMark, StartLine) + _watch.Count(c.LogMark, RetargetLine);
                Report(c, lines == 0, $"no \"Day sleep:\" Info line of the mod for this sleep, started by the game at night (found {lines})");
                var rested = c.Player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
                Report(c, rested && centre.Count > 0, $"the wake-up ran as usual: Rested {rested}, {centre.Count} center message(s)");
                Note(c, $"wake-up messages {DescribeWake()}, {verdict}: the text and the label are checked by {BugNoSkipEvening}");
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        if (!messageCheck)
        {
            CheckRestored(c);
        }
    }

    private static bool NoSkip() => !_noSkip;

    // ---------- other mods ----------

    // X01: Creature Kill and Tame Counts postfix Player.Message, where mod swap "Good morning". Rested kept, so
    // "Good evening" stay on screen. No tame counted, no error.
    private static IEnumerator RunCompatCounts()
    {
        const string N = CompatCounts;
        yield return Settle();
        var other = FeatureRegistry.Find(CountsGuid);
        if (other == null || !other.Value.IsActive)
        {
            SelfTest.Fail(N, $"this test needs the MC mod {CountsGuid} (Creature Kill and Tame Counts) loaded and active in the same game: "
                             + (other == null ? "not loaded" : $"{other.Value.State}, {other.Value.Status}"));
            yield break;
        }
        var error = Begin(N, 8f, new Setup { KeepRested = true }, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var message = AccessTools.Method(typeof(Player), nameof(Player.Message),
                new[] { typeof(MessageHud.MessageType), typeof(string), typeof(int), typeof(Sprite), typeof(bool) });
            var info = Harmony.GetPatchInfo(message);
            Report(c, info != null && Owns(info.Postfixes, CountsGuid) && Owns(info.Prefixes, ModInfo.Guid),
                $"both mods patch Player.Message: {other.Value.Name} (postfix) {info != null && Owns(info.Postfixes, CountsGuid)}, this mod (prefix) "
                + $"{info != null && Owns(info.Prefixes, ModInfo.Guid)}");
            var key = CountsGuid + ".Tames";
            c.Player.m_customData.TryGetValue(key, out var tamesBefore);

            yield return Sleep(c, Plan.ToHour(8f, 18f));
            if (!c.OK)
            {
                yield break;
            }
            var centre = WakeCentre();
            Report(c, centre.Count == 1 && centre[0] == DefaultMessage && _wakeScreenText == DefaultMessage,
                $"\"{_wakeScreenText}\" on screen after the daytime sleep (expected \"{DefaultMessage}\" alone)");
            c.Player.m_customData.TryGetValue(key, out var tamesAfter);
            Report(c, tamesBefore == tamesAfter, $"tame counts of {other.Value.Name} unchanged by the wake-up message: {tamesBefore == tamesAfter}");
            var errors = _watch.Errors(c.ErrMark);
            Report(c, errors.Count == 0, $"error lines in the log during this sleep: {errors.Count}" + (errors.Count > 0 ? $", first: {errors[0]}" : ""));
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // X02: Batch Station Feeding and Loot Pickup Filter prefix Player.Interact (door of the Use key). E pressed
    // through that door: claim + spawn point, then lying down and day sleep; bed hover text is vanilla (no batch hint).
    private static IEnumerator RunCompatInteract()
    {
        const string N = CompatInteract;
        yield return Settle();
        var feed = FeatureRegistry.Find(BatchFeedGuid);
        var filter = FeatureRegistry.Find(PickupFilterGuid);
        if (feed == null || !feed.Value.IsActive || filter == null || !filter.Value.IsActive)
        {
            SelfTest.Fail(N, $"this test needs the MC mods {BatchFeedGuid} (Batch Station Feeding) and {PickupFilterGuid} (Loot Pickup Filter) loaded and "
                             + $"active in the same game: {(feed == null ? "not loaded" : feed.Value.State)} / {(filter == null ? "not loaded" : filter.Value.State)}");
            yield break;
        }
        var error = Begin(N, 8f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var interact = AccessTools.Method(typeof(Player), nameof(Player.Interact), new[] { typeof(GameObject), typeof(bool), typeof(bool) });
            var info = Harmony.GetPatchInfo(interact);
            Report(c, info != null && Owns(info.Prefixes, BatchFeedGuid) && Owns(info.Prefixes, PickupFilterGuid),
                $"both mods prefix Player.Interact: {feed.Value.Name} {info != null && Owns(info.Prefixes, BatchFeedGuid)}, {filter.Value.Name} "
                + $"{info != null && Owns(info.Prefixes, PickupFilterGuid)}");
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }

            // First E: claim and spawn point.
            var mark = Messages.Count;
            c.Player.Interact(c.Bed.gameObject, false, false);
            yield return null;
            Report(c, c.Bed.IsCurrent() && LastCentre(mark) == SpawnSetToken && !c.Player.IsAttached(),
                $"first E through Player.Interact: spawn point set {c.Bed.IsCurrent()}, message \"{Localize(LastCentre(mark))}\", lying {c.Player.IsAttached()}");

            // Hover text of the bed: exactly what vanilla Bed.GetHoverText build, no extra line.
            var vanillaHover = Localization.instance.Localize(c.Profile.GetName() + "'s $piece_bed\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_sleep");
            var hover = c.Bed.GetHoverText();
            Report(c, hover == vanillaHover, $"hover text of the bed is the vanilla one, no batch hint: \"{hover.Replace("\n", " / ")}\"");

            var plan = Plan.ToHour(8f, 18f);
            plan.ViaPlayer = true;
            plan.SkipFlags = true;
            yield return Sleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            Report(c, !FeedMuting(), $"{feed.Value.Name} is not muting messages after the bed was used: {!FeedMuting()}");
            var errors = _watch.Errors(c.ErrMark);
            Report(c, errors.Count == 0, $"error lines in the log during this sleep: {errors.Count}" + (errors.Count > 0 ? $", first: {errors[0]}" : ""));
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // Batch Station Feeding hide game messages while it feed a station (BatchFeeder.Muting). Other mod's type: only
    // by name, never by reference. Not found = not muting.
    private static bool FeedMuting()
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(BatchFeedGuid, out var info) || info.Instance == null)
            {
                return false;
            }
            var type = info.Instance.GetType().Assembly.GetType("MC.Crafting.StationsBatchFeedMod.BatchFeeder");
            var field = type?.GetField("Muting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                                                 | System.Reflection.BindingFlags.NonPublic);
            return field != null && field.GetValue(null) is bool muting && muting;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // X03, stand-in for BedRules / Sleepover "sleep at any time": Game.UpdateSleeping replaced by vanilla logic
    // without the time test. Morning sleep started that way end at 18:00 (retarget line); afternoon follow
    // IncludeAfternoon; night untouched.
    private static IEnumerator RunCompatAnyTime()
    {
        const string N = CompatAnyTime;
        yield return Settle();
        var setup = new Setup
        {
            Patches = h => h.Patch(Find(typeof(Game), nameof(Game.UpdateSleeping)), prefix: new HarmonyMethod(typeof(SleepSelfTests), nameof(AnyTimeSleep))),
        };
        var error = Begin(N, 8f, setup, out var c, "08:00");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            _anyTime = true;
            yield return Sleep(c, Plan.ToHour(8f, 18f, DefaultMessage, Starter.Retarget));
            if (!c.OK)
            {
                yield break;
            }

            SetRun(c, 14f, setup, "14:00, IncludeAfternoon off");
            yield return Sleep(c, Plan.ToMorning(14f));
            if (!c.OK)
            {
                yield break;
            }

            var on = new Setup { Afternoon = true };
            SetRun(c, 14f, on, "14:00, IncludeAfternoon on");
            yield return Sleep(c, Plan.ToHour(14f, 18f, DefaultMessage, Starter.Retarget));
            if (!c.OK)
            {
                yield break;
            }

            SetRun(c, 22f, setup, "22:00");
            yield return Sleep(c, Plan.ToMorning(22f));
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // Vanilla Game.UpdateSleeping without "(IsAfternoon || IsNight)": what "ignore time restrictions" mods do with a
    // replacing prefix. The mod's own prefix (first) and postfix still run around it.
    private static bool AnyTimeSleep(Game __instance)
    {
        if (!_anyTime)
        {
            return true;
        }
        try
        {
            var net = ZNet.instance;
            var env = EnvMan.instance;
            if (net == null || env == null || ZRoutedRpc.instance == null || !net.IsServer())
            {
                return false;
            }
            if (__instance.m_sleeping)
            {
                if (!env.IsTimeSkipping())
                {
                    __instance.m_lastSleepTime = net.GetTimeSeconds();
                    __instance.m_sleeping = false;
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
                }
            }
            else if (!env.IsTimeSkipping() && __instance.EverybodyIsTryingToSleep() && !(net.GetTimeSeconds() - __instance.m_lastSleepTime < 10.0))
            {
                env.SkipToMorning();
                __instance.m_sleeping = true;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStart");
            }
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} any-time stand-in failed: {e}");
        }
        return false;
    }

    // X04, stand-in for SkipSleep (ratio of players in bed is enough): "is everybody in bed?" answer yes while local
    // player stand. Morning: day sleep start for everybody, player who is up sleep too and wake at 18:00.
    private static IEnumerator RunCompatRatio()
    {
        const string N = CompatRatio;
        yield return Settle();
        var error = Begin(N, 8f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            _everybodyForced = true; // enough other players are in bed
            yield return WaitStart(c, 6f);
            _everybodyForced = null;
            if (!c.OK)
            {
                yield break;
            }
            Report(c, c.Player.IsSleeping() && !c.Player.IsAttached(), $"the sleep started for the player who is up: asleep {c.Player.IsSleeping()}, in bed {c.Player.IsAttached()}");
            var plan = Plan.ToHour(8f, 18f);
            CheckStart(c, plan);
            yield return WakeUp(c, plan);
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // X05, stand-in for Seasons with longer night (40 % of the day instead of 30 %): EnvMan.RescaleDayFraction
    // changed. Mod read day cycle from that live method: 18:00 at 80 % of the day, morning sleep end there, no
    // warning. Method broken (go down) = vanilla numbers and ONE warning.
    private static IEnumerator RunCompatDayCycle()
    {
        const string N = CompatDayCycle;
        yield return Settle();
        var setup = new Setup
        {
            Patches = h => h.Patch(Find(typeof(EnvMan), nameof(EnvMan.RescaleDayFraction)), prefix: new HarmonyMethod(typeof(SleepSelfTests), nameof(CycleStub))),
        };
        _cycle = 1;
        var error = Begin(N, 8f, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var env = c.Env;
            // Stand-in itself first: game's method must now say 06:00 at 20 % and 18:00 at 80 % of the day. Not so =
            // test rig problem, nothing to learn about the mod.
            var at20 = env.RescaleDayFraction(0.2f);
            var at80 = env.RescaleDayFraction(0.8f);
            if (Mathf.Abs(at20 - 0.25f) > 0.0005f || Mathf.Abs(at80 - 0.75f) > 0.0005f)
            {
                Fail(c, $"the stand-in day cycle is not in effect: EnvMan.RescaleDayFraction gives {at20:0.000} at 20 % and {at80:0.000} at 80 % of the day "
                        + "(expected 0.250 and 0.750)");
                yield break;
            }
            var dawn = DayClock.RawFractionForHour(env, DayClock.DawnHour);
            var dusk = DayClock.RawFractionForHour(env, DayClock.NightfallHour);
            Report(c, Mathf.Abs(dawn - 0.2f) < 0.001f && Mathf.Abs(dusk - 0.8f) < 0.001f && !DayClock.WarnedFallback,
                $"night made 40 % of the day: the mod reads 06:00 at {dawn:0.000} and 18:00 at {dusk:0.000} of the day (expected 0.200 and 0.800), "
                + $"fallback warning {DayClock.WarnedFallback}");

            yield return Sleep(c, Plan.ToHour(8f, 18f));
            if (!c.OK)
            {
                yield break;
            }
            var into = (c.SkipTo - c.Day * c.Length) / c.Length;
            var problems = _watch.Problems(null);
            Report(c, Math.Abs(into - 0.8) < 0.001 && !DayClock.WarnedFallback && problems.Count == 0,
                $"the skip ended {into:0.000} of the way through the day, where that day cycle turns to night (expected 0.800; vanilla 0.850); "
                + $"warnings of the mod: {problems.Count}");

            // Broken method: same frame, game never run a tick with it.
            var mark = _watch.Mark();
            _cycle = 2;
            var brokenDusk = DayClock.RawFractionForHour(env, DayClock.NightfallHour);
            var brokenDawn = DayClock.RawFractionForHour(env, DayClock.DawnHour);
            _cycle = 1;
            var warnings = _watch.Count(mark, CycleWarning);
            Report(c, Mathf.Abs(brokenDusk - 0.85f) < 0.0001f && Mathf.Abs(brokenDawn - 0.15f) < 0.0001f && warnings == 1 && DayClock.WarnedFallback,
                $"day cycle that goes backwards: the mod falls back to the vanilla day (06:00 at {brokenDawn:0.000}, 18:00 at {brokenDusk:0.000}) and "
                + $"warns once (warning lines: {warnings})");
            DayClock.ResetFallbackWarning(); // deliberate: next real warning must show again
            c.AllowedWarning = CycleWarning;
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // 1: night 40 % of the day (06:00 at 20 %, 18:00 at 80 %). 2: go down (broken). 0: vanilla method run.
    // Prefix that replace the method, not postfix: vanilla RescaleDayFraction write its result into its own argument,
    // so a postfix get the already rescaled number (run 1: stub on top of vanilla = 06:00 at 12 %, 18:00 at 88 %).
    private static bool CycleStub(float fraction, ref float __result)
    {
        if (_cycle == 1)
        {
            if (fraction < 0.2f)
            {
                __result = fraction / 0.2f * 0.25f;
            }
            else if (fraction <= 0.8f)
            {
                __result = 0.25f + (fraction - 0.2f) / 0.6f * 0.5f;
            }
            else
            {
                __result = 0.75f + (fraction - 0.8f) / 0.2f * 0.25f;
            }
            return false;
        }
        if (_cycle == 2)
        {
            __result = 1f - fraction;
            return false;
        }
        return true;
    }
}
#endif
