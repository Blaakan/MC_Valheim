#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SleepThroughDayMod;

// Debug build only. Single-player tests of every day flows (TESTING.md T01-T17), on rig of SleepSelfTests.cs.
// Every test force the three settings in memory (defaults unless test say other), so result never depend on player's
// config file; sleep.wakehour check that with nothing forced the mod read the config.
internal static partial class SleepSelfTests
{
    private const string Morning = "sleep.morning";
    private const string Night = "sleep.night";
    private const string NightLate = "sleep.night.late";
    private const string AfternoonDefault = "sleep.afternoon.default";
    private const string AfternoonIncluded = "sleep.afternoon.included";
    private const string AfternoonWindow = "sleep.afternoon.window";
    private const string WindowNoon = "sleep.window.noon";
    private const string BedChecks = "sleep.bedchecks";
    private const string Cooldown = "sleep.cooldown";
    private const string MorningRested = "sleep.morning.rested";
    private const string CanSleepName = "sleep.cansleep";
    private const string WakeHourName = "sleep.wakehour";
    private const string WakeMessageName = "sleep.wakemessage";
    private const string Toggle = "sleep.toggle";
    private const string ToggleAsleep = "sleep.toggle.asleep";
    private const string Restore = "sleep.restore";
    private const string Presentation = "sleep.presentation";
    private const string Beds = "sleep.beds";

    private static KeyValuePair<string, Func<IEnumerator>>[] _singleTests;

    private static KeyValuePair<string, Func<IEnumerator>>[] SingleTests() => _singleTests ??= new[]
    {
        T(Morning, RunMorning),
        T(Night, RunNight),
        T(NightLate, RunNightLate),
        T(AfternoonDefault, RunAfternoonDefault),
        T(AfternoonIncluded, RunAfternoonIncluded),
        T(AfternoonWindow, RunAfternoonWindow),
        T(WindowNoon, RunWindowNoon),
        T(BedChecks, RunBedChecks),
        T(Cooldown, RunCooldown),
        T(MorningRested, RunMorningRested),
        T(CanSleepName, RunCanSleep),
        T(WakeHourName, RunWakeHour),
        T(WakeMessageName, RunWakeMessage),
        T(Toggle, RunToggle),
        T(ToggleAsleep, RunToggleAsleep),
        T(Restore, RunRestore),
        T(Presentation, RunPresentation),
        T(Beds, RunBeds),
    };

    // T01: 08:00, default settings. Lie down, sleep screen, 12 s skip to 18:00 of same date, WakeUpMessage where
    // vanilla say "Good morning", Rested, no "Day N" message, Info and Debug lines, sleep counted, clock after.
    private static IEnumerator RunMorning()
    {
        const string N = Morning;
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
            var env = c.Env;
            // Mod read day cycle from the game: vanilla = 06:00, noon, 18:00 at 15 %, 50 %, 85 % of the day.
            if (VanillaCycle(env))
            {
                var dawn = DayClock.RawFractionForHour(env, DayClock.DawnHour);
                var noon = DayClock.RawFractionForHour(env, DayClock.NoonHour);
                var dusk = DayClock.RawFractionForHour(env, DayClock.NightfallHour);
                Report(c, Mathf.Abs(dawn - 0.15f) < 0.001f && Mathf.Abs(noon - 0.5f) < 0.001f && Mathf.Abs(dusk - 0.85f) < 0.001f
                          && !DayClock.WarnedFallback,
                    $"day cycle read from the game: 06:00, 12:00 and 18:00 at {dawn:0.000}, {noon:0.000} and {dusk:0.000} of the day "
                    + $"(vanilla 0.150, 0.500, 0.850; day length {c.Length:0} s), fallback warning {DayClock.WarnedFallback}");
            }
            else
            {
                Note(c, "the day cycle is not the vanilla one here (a day-length mod?): vanilla numbers not checked");
            }

            var sleeps = c.Profile.GetStat(PlayerStatType.Sleep);
            var days = c.Profile.GetStat(PlayerStatType.ConsecutiveDaysSurvived);
            var plan = Plan.ToHour(8f, 18f).Real();
            plan.Shots = true;
            plan.Screen = true;
            plan.CheckLength = true;
            plan.CheckDelay = true;
            yield return Sleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            Report(c, c.AttachAnimation == "attach_bed", $"lying animation \"{c.AttachAnimation}\" (the bed's own \"attach_bed\")");

            // Date did not change: game show no new-day message. Then what console command `time` print 10 s later.
            yield return WaitDayMessage(c, 10f, false);
            var fraction = env.GetDayFraction().ToString("0.00", CultureInfo.InvariantCulture);
            var dayNow = env.GetDay(c.Net.GetTimeSeconds());
            Report(c, dayNow == c.Day && (fraction == "0.75" || fraction == "0.76" || fraction == "0.77"),
                $"10 s after waking up `time` would print Day: {dayNow} ({fraction}) (expected Day {c.Day} and 0.75 to 0.77)");

            var sleepsNow = c.Profile.GetStat(PlayerStatType.Sleep);
            var daysNow = c.Profile.GetStat(PlayerStatType.ConsecutiveDaysSurvived);
            Report(c, Mathf.Approximately(sleepsNow, sleeps + 1f) && Mathf.Approximately(daysNow, days),
                $"statistics: sleeps {sleeps:0} -> {sleepsNow:0} (expected +1), days survived {days:0} -> {daysNow:0} (expected unchanged: no morning passed)");
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T05: 22:00, vanilla night sleep: next 06:00, "Good morning", mod write no Info line.
    private static IEnumerator RunNight()
    {
        const string N = Night;
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
            var plan = Plan.ToMorning(22f).Real();
            plan.Shots = true;
            plan.CheckLength = true;
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

    // T05, after midnight: 02:00, vanilla night sleep to 06:00 of the SAME date.
    private static IEnumerator RunNightLate()
    {
        const string N = NightLate;
        yield return Settle();
        var error = Begin(N, 2f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToMorning(2f));
            if (c.OK)
            {
                Report(c, c.Env.GetDay(_wakeTime) == c.Day, $"a sleep that starts before dawn wakes on its own date: day {c.Env.GetDay(_wakeTime)} (started day {c.Day})");
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T03: 14:00, IncludeAfternoon off: vanilla. Next 06:00, "Good morning", "Day n+1", no Info line.
    private static IEnumerator RunAfternoonDefault()
    {
        const string N = AfternoonDefault;
        yield return Settle();
        var error = Begin(N, 14f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var plan = Plan.ToMorning(14f).Real();
            plan.Shots = true;
            yield return Sleep(c, plan);
            if (c.OK)
            {
                yield return WaitDayMessage(c, 30f, true);
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T06: 14:00, IncludeAfternoon on: vanilla start it, mod move its end to 18:00 of same date (retarget line).
    private static IEnumerator RunAfternoonIncluded()
    {
        const string N = AfternoonIncluded;
        yield return Settle();
        var error = Begin(N, 14f, new Setup { Afternoon = true }, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var plan = Plan.ToHour(14f, 18f, DefaultMessage, Starter.Retarget).Real();
            plan.Shots = true;
            plan.CheckLength = true;
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

    // T06, edges: IncludeAfternoon on, WakeUpHour 18 = window until 17:00. 16:30 in, 08:00 still in, 17:30 out.
    private static IEnumerator RunAfternoonWindow()
    {
        const string N = AfternoonWindow;
        yield return Settle();
        var setup = new Setup { Afternoon = true };
        var error = Begin(N, 16.5f, setup, out var c, "16:30");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToHour(16.5f, 18f, DefaultMessage, Starter.Retarget));
            if (!c.OK)
            {
                yield break;
            }
            SetRun(c, 8f, setup, "08:00");
            yield return Sleep(c, Plan.ToHour(8f, 18f));
            if (!c.OK)
            {
                yield break;
            }
            // After window end: vanilla, with game's own skip length so the new-day message come as in play.
            SetRun(c, 17.5f, setup, "17:30");
            yield return Sleep(c, Plan.ToMorning(17.5f).Real());
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitDayMessage(c, 30f, true);
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T04: default window end at noon. 11:15 = day sleep to 18:00; 12:30 = vanilla to next morning.
    private static IEnumerator RunWindowNoon()
    {
        const string N = WindowNoon;
        yield return Settle();
        var setup = new Setup();
        var error = Begin(N, 11.25f, setup, out var c, "11:15");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToHour(11.25f, 18f));
            if (!c.OK)
            {
                yield break;
            }
            SetRun(c, 12.5f, setup, "12:30");
            yield return Sleep(c, Plan.ToMorning(12.5f).Real());
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitDayMessage(c, 30f, true);
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T07: 08:00, mod say "may sleep". Every other vanilla bed check still say no with its own message: one check
    // real at a time (others skipped for test bed). Then own bed that is not spawn point only set spawn point.
    private static IEnumerator RunBedChecks()
    {
        const string N = BedChecks;
        yield return Settle();
        var error = Begin(N, 8f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var addedWet = false;
        try
        {
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 8f);
            var player = c.Player;
            var bed = c.Bed;

            // First E: claim, spawn point. No lying down.
            var mark = Messages.Count;
            bed.Interact(player, false, false);
            yield return null;
            Report(c, bed.IsCurrent() && LastCentre(mark) == SpawnSetToken && !player.IsAttached(),
                $"first E on the unclaimed bed: spawn point set {bed.IsCurrent()}, message \"{Localize(LastCentre(mark))}\", lying {player.IsAttached()}");

            // Enemy: Player.OnTargeted(sensed) = what a creature's AI call when it notice the player.
            var sensed = player.m_timeSinceSensed;
            _realChecks = BedCheck.Enemies;
            player.OnTargeted(true, false);
            var isSensed = player.IsSensed();
            mark = Messages.Count;
            bed.Interact(player, false, false);
            yield return null;
            Refused(c, mark, "$msg_bedenemiesnearby", $"an enemy senses the player (Player.IsSensed {isSensed})");
            player.m_timeSinceSensed = Mathf.Max(sensed, 2f);

            // Roof: bed stand in the open.
            Cover.GetCoverForPoint(bed.GetSpawnPoint(), out var cover, out var underRoof);
            if (underRoof && cover >= 0.8f)
            {
                Fail(c, $"cannot test the roof check here: the test bed is under a roof with {cover:P0} cover");
            }
            else
            {
                _realChecks = BedCheck.Exposure;
                mark = Messages.Count;
                bed.Interact(player, false, false);
                yield return null;
                Refused(c, mark, underRoof ? "$msg_bedtooexposed" : "$msg_bedneedroof", $"no roof over the bed (under roof {underRoof}, cover {cover:P0})");
            }

            // Fire: no heat near the bed.
            var heat = EffectArea.IsPointInsideArea(bed.transform.position, EffectArea.Type.Heat);
            if (heat != null)
            {
                Fail(c, $"cannot test the fire check here: a fire ({heat.name}) already warms the test bed");
            }
            else
            {
                _realChecks = BedCheck.Fire;
                mark = Messages.Count;
                bed.Interact(player, false, false);
                yield return null;
                Refused(c, mark, "$msg_bednofire", "no fire near the bed");
            }

            // Wet.
            var seman = player.GetSEMan();
            if (!seman.HaveStatusEffect(SEMan.s_statusEffectWet))
            {
                seman.AddStatusEffect(SEMan.s_statusEffectWet);
                addedWet = seman.HaveStatusEffect(SEMan.s_statusEffectWet);
            }
            _realChecks = BedCheck.Wet;
            mark = Messages.Count;
            bed.Interact(player, false, false);
            yield return null;
            Refused(c, mark, "$msg_bedwet", $"the player is wet ({seman.HaveStatusEffect(SEMan.s_statusEffectWet)})");
            if (addedWet)
            {
                seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                addedWet = false;
            }

            // Control: every check passing = player lie down (so each refusal above came from its check, not the hour).
            // Up again in same frame: no server tick may start a sleep here.
            _realChecks = BedCheck.None;
            bed.Interact(player, false, false);
            var lying = player.IsAttached() && player.InBed();
            player.AttachStop();
            Report(c, lying, $"with every check passing the same E lays the player down in the morning: {lying}");
            yield return null;

            // Fire next to the bed (when it burns here: rain put a fire in the open out): fire check alone let player in.
            var firePrefab = ZNetScene.instance.GetPrefab("fire_pit");
            if (firePrefab == null)
            {
                Note(c, "prefab 'fire_pit' not found: fire check not tested with a fire lit");
            }
            else
            {
                SpawnObject(c, firePrefab, 2.5f, 1.8f);
                var until = Time.realtimeSinceStartup + 5f;
                while (EffectArea.IsPointInsideArea(bed.transform.position, EffectArea.Type.Heat) == null && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
                if (EffectArea.IsPointInsideArea(bed.transform.position, EffectArea.Type.Heat) == null)
                {
                    Note(c, "the campfire spawned next to the bed gave no heat within 5 s (rain, or too far): fire check not tested with a fire lit");
                }
                else
                {
                    _realChecks = BedCheck.Fire;
                    bed.Interact(player, false, false);
                    lying = player.IsAttached() && player.InBed();
                    player.AttachStop();
                    _realChecks = BedCheck.None;
                    Report(c, lying, $"with a campfire burning next to the bed the real fire check lets the player lie down: {lying}");
                    yield return null;
                }
            }

            // Second bed of the player: E claim it and move spawn point there. Back on first bed: E only set spawn
            // point again (vanilla), no lying down, although time and every check would allow sleep.
            var second = SpawnBed(c, "bed", 2.5f, -3f);
            mark = Messages.Count;
            second.Interact(player, false, false);
            yield return null;
            Report(c, second.IsCurrent() && !bed.IsCurrent() && LastCentre(mark) == SpawnSetToken && !player.IsAttached(),
                $"E on a second bed: spawn point moved there {second.IsCurrent()}, message \"{Localize(LastCentre(mark))}\", lying {player.IsAttached()}");
            mark = Messages.Count;
            bed.Interact(player, false, false);
            yield return null;
            Report(c, bed.IsCurrent() && LastCentre(mark) == SpawnSetToken && !player.IsAttached(),
                $"E on the first bed, no longer the spawn point: spawn point set again {bed.IsCurrent()}, message "
                + $"\"{Localize(LastCentre(mark))}\", lying {player.IsAttached()} (expected no lying down)");
            EndChecks(c);
        }
        finally
        {
            _realChecks = BedCheck.None;
            if (addedWet && c.Player != null)
            {
                c.Player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            }
            Cleanup();
        }
        CheckRestored(c);
    }

    private static void Refused(Case c, int mark, string token, string why)
    {
        var message = LastCentre(mark);
        var lying = c.Player.IsAttached() || c.Player.InBed();
        Report(c, message == token && !lying,
            $"{why}: bed says \"{Localize(message)}\" (expected \"{Localize(token)}\"), lying {lying} (expected no)");
    }

    // T08: wake at nightfall, bed say no for 30 s (vanilla cooldown), then night sleep to next morning.
    private static IEnumerator RunCooldown()
    {
        const string N = Cooldown;
        yield return Settle();
        var error = Begin(N, 8f, new Setup(), out var c, "day sleep");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            // Game's own skip length: day flags at wake are those a player meet.
            yield return Sleep(c, Plan.ToHour(8f, 18f).Real());
            if (!c.OK)
            {
                yield break;
            }
            var player = c.Player;
            c.Tag = "right after waking up";
            yield return FixedSteps(3);
            var mark = Messages.Count;
            var can = EnvMan.CanSleep();
            c.Bed.Interact(player, false, false);
            yield return null;
            Report(c, !can && LastCentre(mark) == CantSleepToken && !player.IsAttached(),
                $"`time` would say {(can ? "Can sleep" : "Can NOT sleep")}; bed says \"{Localize(LastCentre(mark))}\", lying {player.IsAttached()} "
                + $"(expected Can NOT sleep and \"{Localize(CantSleepToken)}\")");

            // 30 s cooldown is world time since Player.m_wakeupTime: move that back instead of waiting.
            c.Tag = "25 s after waking up";
            player.m_wakeupTime = c.Net.GetTimeSeconds() - 25.0;
            yield return FixedSteps(3);
            mark = Messages.Count;
            can = EnvMan.CanSleep();
            c.Bed.Interact(player, false, false);
            yield return null;
            Report(c, !can && LastCentre(mark) == CantSleepToken && !player.IsAttached(),
                $"`time` would say {(can ? "Can sleep" : "Can NOT sleep")}; bed says \"{Localize(LastCentre(mark))}\", lying {player.IsAttached()}");

            var now = c.Net.GetTimeSeconds();
            player.m_wakeupTime = now - 31.0;
            c.Game.m_lastSleepTime = now - 11.0; // server's 10 s between two sleeps
            yield return FixedSteps(3);
            MarkRun(c, "31 s after waking up", c.Env.GetDay(now));
            var night = Plan.ToMorning(18.5f);
            night.SkipFlags = true;
            Report(c, EnvMan.CanSleep(), $"`time` would say {(EnvMan.CanSleep() ? "Can sleep" : "Can NOT sleep")} (expected Can sleep)");
            yield return Sleep(c, night);
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

    // T09: vanilla night sleep, 30 s, then morning sleep: 18:00 of same date, and "Good evening" stay on screen
    // (player still Rested from the night: vanilla show no Rested message over it).
    private static IEnumerator RunMorningRested()
    {
        const string N = MorningRested;
        yield return Settle();
        var error = Begin(N, 22f, new Setup(), out var c, "night");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToMorning(22f).Real());
            if (!c.OK)
            {
                yield break;
            }
            // "Wait 30 s": cooldowns moved back. Smoothed clock need some seconds to pass 06:00 after a skip: wait
            // for it like a player would (no snap), else vanilla would still see night.
            var now = c.Net.GetTimeSeconds();
            c.Player.m_wakeupTime = now - 31.0;
            c.Game.m_lastSleepTime = now - 11.0;
            c.Tag = "after the night";
            var t0 = Time.realtimeSinceStartup;
            while (!DayClock.IsMorning() && Time.realtimeSinceStartup - t0 < 30f)
            {
                yield return null;
            }
            if (!DayClock.IsMorning())
            {
                Fail(c, $"the day flags did not reach the morning within 30 s after waking up at 06:00 (clock fraction {c.Env.GetDayFraction():0.000})");
                yield break;
            }
            Note(c, $"day flags say morning {Time.realtimeSinceStartup - t0:0.0} s after waking up at 06:00");
            yield return FixedSteps(3);

            MarkRun(c, "morning", c.Env.GetDay(c.Net.GetTimeSeconds()));
            var plan = Plan.ToHour(6.5f, 18f);
            plan.SkipFlags = true;
            yield return Sleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            var centre = WakeCentre();
            Report(c, centre.Count == 1 && centre[0] == DefaultMessage && _wakeScreenText == DefaultMessage,
                $"still Rested from the night: the wake-up shows only \"{(centre.Count > 0 ? Localize(centre[0]) : "")}\" and it stays on screen "
                + $"(\"{_wakeScreenText}\"); expected \"{DefaultMessage}\" alone");
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // T10: what `time` print as "Can sleep" / "Can NOT sleep" = EnvMan.CanSleep(). Morning: yes, but not within
    // 30 s of waking up (mod's own cooldown test).
    private static IEnumerator RunCanSleep()
    {
        const string N = CanSleepName;
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
            var player = c.Player;
            var net = c.Net;
            Report(c, EnvMan.CanSleep(), $"morning, cooldown over: `time` would print \"{TimeLine(c)}\" (expected Can sleep; vanilla says Can NOT sleep)");

            player.m_wakeupTime = net.GetTimeSeconds() - 10.0;
            yield return FixedSteps(3);
            Report(c, !EnvMan.CanSleep(), $"morning, 10 s after waking up: `time` would print \"{TimeLine(c)}\" (expected Can NOT sleep)");

            player.m_wakeupTime = net.GetTimeSeconds() - 25.0;
            yield return FixedSteps(3);
            Report(c, !EnvMan.CanSleep(), $"morning, 25 s after waking up: `time` would print \"{TimeLine(c)}\" (expected Can NOT sleep)");

            player.m_wakeupTime = net.GetTimeSeconds() - 31.0;
            yield return FixedSteps(3);
            Report(c, EnvMan.CanSleep(), $"morning, 31 s after waking up: `time` would print \"{TimeLine(c)}\" (expected Can sleep)");

            // Afternoon: vanilla's own answer stand (its own 30 s too), mod add nothing.
            SetRun(c, 14f, new Setup(), "");
            yield return WaitFlags(c, 14f);
            if (!c.OK)
            {
                yield break;
            }
            var open = EnvMan.CanSleep();
            player.m_wakeupTime = net.GetTimeSeconds() - 10.0;
            yield return FixedSteps(3);
            Report(c, open && !EnvMan.CanSleep(),
                $"afternoon: Can sleep {open} with the cooldown over, {EnvMan.CanSleep()} 10 s after waking up (vanilla's own rule, unchanged)");
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // Text console command `time` build (Terminal "time"): same sources, same format.
    private static string TimeLine(Case c)
    {
        var t = c.Net.GetTimeSeconds();
        return $"{t.ToString("0.00", CultureInfo.InvariantCulture)} sec, Day: {c.Env.GetDay(t)} "
               + $"({c.Env.GetDayFraction().ToString("0.00", CultureInfo.InvariantCulture)}), {(EnvMan.CanSleep() ? "Can sleep" : "Can NOT sleep")}";
    }

    // T11: WakeUpHour 21 and 13; 13 with IncludeAfternoon at 12:30 = vanilla (window ended at noon); 30 = 23.
    private static IEnumerator RunWakeHour()
    {
        const string N = WakeHourName;
        yield return Settle();

        // Nothing forced: the mod read the three settings from the config (the only door to them).
        Plugin.ReadDaySettings(out var configHour, out var configAfternoon);
        var reads = !WakeHourOverride.HasValue && !IncludeAfternoonOverride.HasValue && WakeUpMessageOverride == null
                    && Mathf.Approximately(configHour, DayClock.WakeHour(Plugin.WakeUpHour.Value))
                    && configAfternoon == Plugin.IncludeAfternoon.Value
                    && Plugin.ReadWakeUpMessage() == (Plugin.WakeUpMessage.Value ?? "");
        Report(N, reads, $"with nothing forced the mod uses the config: WakeUpHour {configHour:0.##} (file {Plugin.WakeUpHour.Value:0.##}), "
                         + $"IncludeAfternoon {configAfternoon} (file {Plugin.IncludeAfternoon.Value}), WakeUpMessage \"{Plugin.ReadWakeUpMessage()}\"");

        // Range of the setting itself (BepInEx clamp every value written or read from the file with it).
        var range = Plugin.WakeUpHour.Description.AcceptableValues as AcceptableValueRange<float>;
        var rangeOk = range != null && Mathf.Approximately(range.MinValue, 13f) && Mathf.Approximately(range.MaxValue, 23f)
                      && range.Clamp(30f) is float high && Mathf.Approximately(high, 23f)
                      && range.Clamp(5f) is float low && Mathf.Approximately(low, 13f) && !range.IsValid(30f);
        Report(N, rangeOk, "the WakeUpHour setting carries the range 13 to 23: BepInEx turns 30 into "
                           + $"{(range != null ? range.Clamp(30f) : "?")} and 5 into {(range != null ? range.Clamp(5f) : "?")}");

        var setup = new Setup { WakeHour = 21f };
        var error = Begin(N, 8f, setup, out var c, "WakeUpHour 21");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToHour(8f, 21f));
            if (!c.OK)
            {
                yield break;
            }

            setup = new Setup { WakeHour = 13f };
            SetRun(c, 8f, setup, "WakeUpHour 13");
            yield return Sleep(c, Plan.ToHour(8f, 13f));
            if (!c.OK)
            {
                yield break;
            }

            setup = new Setup { WakeHour = 13f, Afternoon = true };
            SetRun(c, 12.5f, setup, "WakeUpHour 13, IncludeAfternoon, 12:30");
            yield return Sleep(c, Plan.ToMorning(12.5f));
            if (!c.OK)
            {
                yield break;
            }

            setup = new Setup { WakeHour = 30f };
            SetRun(c, 8f, setup, "WakeUpHour 30");
            Plugin.ReadDaySettings(out var clamped, out _);
            Report(c, Mathf.Approximately(clamped, 23f), $"a wake-up hour of 30 is used as {clamped:0.##} (expected 23)");
            yield return Sleep(c, Plan.ToHour(8f, 23f));
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

    // T12: WakeUpMessage. Player Rested when lying down, so wake-up text stay alone on screen. "$msg_goodnight" =
    // game's own "Good night"; empty (or blanks) = vanilla "Good morning".
    private static IEnumerator RunWakeMessage()
    {
        const string N = WakeMessageName;
        const string token = "$msg_goodnight";
        yield return Settle();
        var setup = new Setup { Message = token, KeepRested = true };
        var error = Begin(N, 8f, setup, out var c, token);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToHour(8f, 18f, token));
            if (!c.OK)
            {
                yield break;
            }
            var text = Localize(token);
            var english = Localization.instance.GetSelectedLanguage() == "English";
            Report(c, _wakeScreenText == text && text != token && text.Length > 0 && (!english || text == "Good night"),
                $"the game text key is shown in the game language ({Localization.instance.GetSelectedLanguage()}): \"{_wakeScreenText}\" "
                + $"on screen (expected \"{text}\"{(english ? ", \"Good night\"" : "")})");

            setup = new Setup { Message = "", KeepRested = true };
            SetRun(c, 8f, setup, "empty");
            yield return Sleep(c, Plan.ToHour(8f, 18f, ""));
            if (!c.OK)
            {
                yield break;
            }
            Report(c, _wakeScreenText == Localize(WakeMessage.GoodMorningToken),
                $"empty WakeUpMessage: \"{_wakeScreenText}\" on screen (expected \"{Localize(WakeMessage.GoodMorningToken)}\")");

            setup = new Setup { Message = "   ", KeepRested = true };
            SetRun(c, 8f, setup, "blanks only");
            yield return Sleep(c, Plan.ToHour(8f, 18f, "   "));
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

    // T13 (and vanilla half of T15): mod taken down live (in memory, the steps framework do when Enabled go false) =
    // vanilla beds at once (no patch of the mod left, morning bed refused, afternoon sleep to next morning); up
    // again = morning sleep work, no restart.
    private static IEnumerator RunToggle()
    {
        const string N = Toggle;
        yield return Settle();
        var setup = new Setup();
        var error = Begin(N, 8f, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var held = _holdTests;
        _holdTests = true;
        try
        {
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            var player = c.Player;
            var patched = PatchedByMod();
            Report(c, patched == 4 && EnvMan.CanSleep(), $"mod on: {patched} game methods carry its patches (expected 4), Can sleep {EnvMan.CanSleep()}");
            c.Bed.Interact(player, false, false); // claim
            yield return null;

            c.Tag = "turned off";
            SetOff(true); // what framework do when Enabled go false, in memory
            patched = PatchedByMod();
            Report(c, patched == 0, $"{patched} game methods still carry a patch of the mod (expected 0)");
            yield return FixedSteps(3);
            var mark = Messages.Count;
            var can = EnvMan.CanSleep();
            c.Bed.Interact(player, false, false);
            yield return null;
            Report(c, !can && LastCentre(mark) == CantSleepToken && !player.IsAttached(),
                $"morning bed: `time` would say {(can ? "Can sleep" : "Can NOT sleep")}, bed says \"{Localize(LastCentre(mark))}\", lying "
                + $"{player.IsAttached()} (expected Can NOT sleep and \"{Localize(CantSleepToken)}\")");

            // Off in the afternoon: plain vanilla sleep, mod say and do nothing.
            SetRun(c, 14f, setup, "turned off, 14:00");
            var vanilla = Plan.ToMorning(14f);
            vanilla.ModSilent = true;
            yield return Sleep(c, vanilla);
            if (!c.OK)
            {
                yield break;
            }

            SetRun(c, 8f, setup, "turned on again");
            SetOff(false);
            patched = PatchedByMod();
            Report(c, patched == 4, $"{patched} game methods carry the mod's patches again (expected 4), no restart");
            yield return Sleep(c, Plan.ToHour(8f, 18f));
            if (c.OK)
            {
                EndChecks(c);
            }
        }
        finally
        {
            Cleanup(); // feature back on there, while the list is still held
            _holdTests = held;
        }
        CheckRestored(c);
    }

    // T14: feature off while asleep in a day sleep. Skip already aim at 18:00 and stay; wake-up is vanilla ("Good
    // morning"), no error.
    private static IEnumerator RunToggleAsleep()
    {
        const string N = ToggleAsleep;
        yield return Settle();
        var error = Begin(N, 8f, new Setup(), out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var held = _holdTests;
        _holdTests = true;
        try
        {
            var plan = Plan.ToHour(8f, 18f);
            yield return FallAsleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitReal(1.5f);

            SetOff(true);
            var patched = PatchedByMod();
            Report(c, patched == 0 && c.Player.IsSleeping() && c.Env.IsTimeSkipping()
                      && Math.Abs(c.Env.m_skipToTime - c.SkipTo) < 0.001,
                $"turned off 1.5 s into the sleep: {patched} patches of the mod left, still asleep {c.Player.IsSleeping()}, "
                + $"time skip still aims at {Clock(c.Env, c.Env.m_skipToTime)} of day {c.Env.GetDay(c.Env.m_skipToTime)}");

            plan.First = WakeMessage.GoodMorningToken; // nothing swap it any more
            plan.ModSilent = true;
            yield return WakeUp(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            var errors = _watch.Errors(c.ErrMark);
            Report(c, errors.Count == 0, $"error lines in the log during this sleep: {errors.Count}" + (errors.Count > 0 ? $", first: {errors[0]}" : ""));
            EndChecks(c);
        }
        finally
        {
            Cleanup();
            _holdTests = held;
        }
        CheckRestored(c);
    }

    // T17: test cut short while player asleep (what `finally` of a failing test do): clean-up wake player first,
    // then put everything back; late SleepStop of the server change nothing after.
    private static IEnumerator RunRestore()
    {
        const string N = Restore;
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
            yield return FallAsleep(c, Plan.ToHour(8f, 18f).Real());
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitReal(1.5f);
            var wasAsleep = c.Player.IsSleeping() && c.Game.m_sleeping && c.Env.IsTimeSkipping();
            var cut = Time.realtimeSinceStartup;
            Cleanup();
            Report(c, wasAsleep && !c.Player.IsSleeping() && !c.Game.m_sleeping && !c.Env.IsTimeSkipping() && !c.Player.IsAttached(),
                $"clean-up 1.5 s into a sleep (asleep and skipping before: {wasAsleep}): player asleep {c.Player.IsSleeping()}, sleep running "
                + $"{c.Game.m_sleeping}, time skip running {c.Env.IsTimeSkipping()}, in bed {c.Player.IsAttached()} (expected all no)");
            CheckRestored(c);

            // Two server ticks later: still as before the test.
            yield return WaitReal(4.5f);
            var rested = c.Player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
            var drift = c.Net.GetTimeSeconds() - c.Before.Time - (Time.realtimeSinceStartup - cut);
            Report(c, !c.Player.IsSleeping() && !c.Game.m_sleeping && rested == c.Before.HadRested && Math.Abs(drift) < 3.0
                      && Math.Abs(c.Player.m_wakeupTime - c.Before.Wakeup) < 0.001,
                $"4.5 s later: asleep {c.Player.IsSleeping()}, Rested {rested} (before the test {c.Before.HadRested}), world time "
                + $"{drift:0.0} s off its normal course, bed cooldown time put back {Math.Abs(c.Player.m_wakeupTime - c.Before.Wakeup) < 0.001}");
        }
        finally
        {
            Cleanup();
        }
    }

    // What a sleep looked like, in numbers (T02).
    private sealed class Look
    {
        internal string Animation;
        internal bool InBed;
        internal bool Zzz;
        internal float BlackAfter;
        internal double Planned;
        internal float Asleep;
        internal int Stops;
        internal bool SaveRule;
        internal string Save;
    }

    // T02: day sleep look like vanilla night sleep, as far as state tell: same lying animation, ZZZ object, black
    // screen after same time, same planned skip length, about same real length, one SleepStop with its autosave rule.
    private static IEnumerator RunPresentation()
    {
        const string N = Presentation;
        yield return Settle();
        var setup = new Setup();
        var error = Begin(N, 8f, setup, out var c, "day");
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            var day = Plan.ToHour(8f, 18f).Real();
            day.Shots = true;
            day.Screen = true;
            day.CheckLength = true;
            var saved = _watch.SavedRecently;
            yield return Sleep(c, day);
            if (!c.OK)
            {
                yield break;
            }
            yield return null;
            var dayLook = TakeLook(c, saved);

            SetRun(c, 22f, setup, "night");
            var night = Plan.ToMorning(22f).Real();
            night.Shots = true;
            night.Screen = true;
            night.CheckLength = true;
            saved = _watch.SavedRecently;
            yield return Sleep(c, night);
            if (!c.OK)
            {
                yield break;
            }
            yield return null;
            var nightLook = TakeLook(c, saved);

            c.Tag = "";
            Report(c, dayLook.Animation == "attach_bed" && nightLook.Animation == dayLook.Animation && dayLook.InBed && nightLook.InBed,
                $"lying: day \"{dayLook.Animation}\" in bed {dayLook.InBed}, night \"{nightLook.Animation}\" in bed {nightLook.InBed} (expected the same \"attach_bed\")");
            Report(c, dayLook.Zzz && nightLook.Zzz && dayLook.BlackAfter >= 0f && nightLook.BlackAfter >= 0f
                      && Mathf.Abs(dayLook.BlackAfter - nightLook.BlackAfter) <= 0.75f,
                $"sleep screen: ZZZ object day {dayLook.Zzz} / night {nightLook.Zzz}; screen black after {dayLook.BlackAfter:0.0} s (day) and "
                + $"{nightLook.BlackAfter:0.0} s (night), expected within 0.75 s of each other");
            Report(c, Math.Abs(dayLook.Planned - nightLook.Planned) <= 0.1 && Mathf.Abs(dayLook.Asleep - nightLook.Asleep) <= 2.5f,
                $"length: time skip planned {dayLook.Planned:0.0} s (day) and {nightLook.Planned:0.0} s (night); asleep for {dayLook.Asleep:0.0} s "
                + $"(day) and {nightLook.Asleep:0.0} s (night) of real time, expected within 2.5 s of each other (the stop comes at a 2 s tick)");
            Report(c, dayLook.Stops == 1 && nightLook.Stops == 1 && dayLook.SaveRule && nightLook.SaveRule,
                $"wake-up handler (Game.SleepStop, with the autosave): ran {dayLook.Stops} time(s) after the day sleep ({dayLook.Save}) and "
                + $"{nightLook.Stops} time(s) after the night sleep ({nightLook.Save})");

            var sleepText = Hud.instance != null && Hud.instance.m_sleepingProgress != null
                ? Hud.instance.m_sleepingProgress.GetComponentInChildren<SleepText>(true)
                : null;
            Note(c, $"scene values: sleep fade time {c.Game.m_fadeTimeSleep:0.##} s; sleep text object {(sleepText != null ? "found" : "not found")}"
                    + (sleepText != null && sleepText.m_textField != null ? $", text \"{sleepText.m_textField.text}\"" : "")
                    + (sleepText != null && sleepText.m_dreamTexts != null ? $", dream texts {sleepText.m_dreamTexts.m_texts.Count}" : ""));
            EndChecks(c);
        }
        finally
        {
            Cleanup();
        }
        CheckRestored(c);
    }

    // Game.SleepStop: save timer over 60 s = save (timer back to 0), else line "Saved recently, skipping sleep save.".
    private static Look TakeLook(Case c, int savedBefore)
    {
        var look = new Look
        {
            Animation = c.AttachAnimation,
            InBed = c.LayDown,
            Zzz = c.SawZzz,
            BlackAfter = c.BlackAfter,
            Planned = c.PlannedSeconds,
            Asleep = _wakeReal - c.StartReal,
            Stops = _sleepStops - c.StopMark,
        };
        var timer = c.Game.m_saveTimer;
        var lines = _watch.SavedRecently - savedBefore;
        if (c.SaveTimerAsleep > 60f)
        {
            look.SaveRule = timer < c.SaveTimerAsleep;
            look.Save = $"save timer {c.SaveTimerAsleep:0} s -> {timer:0} s: saved";
        }
        else
        {
            look.SaveRule = timer >= c.SaveTimerAsleep;
            look.Save = $"save timer {c.SaveTimerAsleep:0} s: saved recently, no save ({lines} \"Saved recently\" log line(s) seen)";
        }
        return look;
    }

    // T02, other beds: Dragon Bed and Ashwood Bed carry the same Bed component; morning sleep on each.
    private static IEnumerator RunBeds()
    {
        const string N = Beds;
        var beds = new[] { "piece_bed02", "ashwood_bed" };
        yield return Settle();
        var setup = new Setup { BedPrefab = beds[0] };
        var error = Begin(N, 8f, setup, out var c, beds[0]);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            yield return Sleep(c, Plan.ToHour(8f, 18f));
            if (!c.OK)
            {
                yield break;
            }

            // Next bed in place of the first.
            var old = c.Bed;
            c.Bed = null;
            if (old != null)
            {
                TestBeds.Remove(old);
                Spawned.Remove(old.gameObject);
                ZNetScene.instance.Destroy(old.gameObject);
            }
            yield return null;
            setup = new Setup { BedPrefab = beds[1] };
            c.Bed = SpawnBed(c, beds[1], 2.5f, 0f);
            if (c.Bed == null)
            {
                SelfTest.Fail(N, $"prefab '{beds[1]}' with a Bed component not found");
                yield break;
            }
            SetRun(c, 8f, setup, beds[1]);
            yield return Sleep(c, Plan.ToHour(8f, 18f));
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
}
#endif
