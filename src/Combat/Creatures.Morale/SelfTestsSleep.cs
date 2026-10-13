#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. Real sleeps next to afraid creatures (TESTING.md items in brackets):
//   morale.sleep      night, a claimed bed, three afraid Greydwarfs running 10 m away: the Use key's call lie the
//                     player down with no "enemies nearby" message, the game's own sleep starts, the night is skipped,
//                     the player wake the next morning; nobody targets or senses the player, no combat music state (T03)
//   morale.day-sleep  morning, with MC Sleep Through the Day: hostile Greydwarfs (rank 0) make the bed refuse; afraid
//                     ones do not, calm or running; the day sleep start and ends the same day (X04)
// Bed, time skip and wake-up are vanilla's (Bed.Interact through Player.Interact, Game.UpdateSleeping, EnvMan). The
// bed's roof, fire and wet checks are answered "fine" for the test bed only (temporary Harmony prefix: they are the
// normal game's shelter rules, nothing the mod touches); time of day and the enemy check stay real.
// Me put back: world time, spawn point, sleep timers, Rested, a waiting dream, the player's place.
internal static partial class SelfTests
{
    private const string SleepName = "morale.sleep";
    private const string DaySleepName = "morale.day-sleep";
    private const string SleepThroughDayGuid = "MC.Exploration.Sleep.ThroughDay";
    private const string EnemiesNearbyToken = "$msg_bedenemiesnearby";

    // Part of the day (0-1 of the day length) the tests jump to. Vanilla day: first and last 15 % are night.
    private const float NightRaw = 0.95f;   // about 22:00
    private const float MorningRaw = 0.38f; // about 10:00

    private static Bed _testBed;

    // Harmony prefix (test bed only): roof, fire, wet = fine.
    private static bool BedShelterFine(Bed __instance, ref bool __result)
    {
        if (ReferenceEquals(_testBed, null) || !ReferenceEquals(__instance, _testBed))
        {
            return true;
        }
        __result = true;
        return false;
    }

    // Me = what a sleep test changes outside its stage, and how to put it back.
    private sealed class BedRig
    {
        internal Bed Bed;
        internal int Day0;
        internal double Length;
        private readonly Player _player;
        private readonly EnvMan _env;
        private readonly ZNet _net;
        private readonly Game _game;
        private readonly PlayerProfile _profile;
        private readonly double _time;
        private readonly bool _hadSpawn;
        private readonly Vector3 _spawn;
        private readonly double _wakeup;
        private readonly double _lastSleep;
        private readonly bool _hadRested;
        private readonly float _restedTime;
        private readonly float _restedTtl;
        private readonly string _dream;
        private readonly string _debugEnv;
        private Harmony _bypass;

        private BedRig(Player player, EnvMan env, ZNet net, Game game, PlayerProfile profile)
        {
            _player = player;
            _env = env;
            _net = net;
            _game = game;
            _profile = profile;
            _time = net.GetTimeSeconds();
            _hadSpawn = profile.HaveCustomSpawnPoint();
            _spawn = profile.GetCustomSpawnPoint();
            _wakeup = player.m_wakeupTime;
            _lastSleep = game.m_lastSleepTime;
            var rested = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
            _hadRested = rested != null;
            _restedTime = rested != null ? rested.m_time : 0f;
            _restedTtl = rested != null ? rested.m_ttl : 0f;
            _dream = CinematicsManager.m_dreamCinematic;
            _debugEnv = env.m_debugEnv;
            Length = env.m_dayLengthSec;
            Day0 = env.GetDay(_time);
        }

        // Null (with a failed check) when a sleep cannot be tested now.
        internal static BedRig Begin(Stage s, Checks c, Vector3 bedAt)
        {
            var player = s.Player;
            var env = EnvMan.instance;
            var net = ZNet.instance;
            var game = Game.instance;
            var profile = game != null ? game.GetPlayerProfile() : null;
            if (!c.Check(env != null && net != null && net.IsServer() && game != null && profile != null && MessageHud.instance != null
                         && ZRoutedRpc.instance != null && Localization.instance != null && !Hud.IsUserHidden(),
                    "no world of this game's own (or the HUD is hidden): a sleep and its messages cannot be checked"))
            {
                return null;
            }
            if (!c.Check(!game.m_sleeping && !env.IsTimeSkipping() && !player.IsSleeping() && !player.IsAttached() && !env.m_debugTimeOfDay
                         && env.m_dayLengthSec > 0,
                    "a sleep or time skip is running, the player is attached to something, or the time of day is forced (tod): no sleep test now"))
            {
                return null;
            }
            var rig = new BedRig(player, env, net, game, profile);
            // A dream video waiting (a boss died earlier) would play in this sleep and pause the game: held back.
            CinematicsManager.m_dreamCinematic = "";
            // Rain would make the player wet in the open; the bed's own wet check is answered for the test bed anyway.
            env.m_debugEnv = "Clear";
            try
            {
                rig._bypass = new Harmony(ModInfo.Guid + ".selftest.bed");
                var prefix = new HarmonyMethod(typeof(SelfTests), nameof(BedShelterFine));
                rig._bypass.Patch(AccessTools.Method(typeof(Bed), nameof(global::Bed.CheckExposure)), prefix: prefix);
                rig._bypass.Patch(AccessTools.Method(typeof(Bed), nameof(global::Bed.CheckFire)), prefix: prefix);
                rig._bypass.Patch(AccessTools.Method(typeof(Bed), nameof(global::Bed.CheckWet)), prefix: prefix);
            }
            catch (Exception e)
            {
                c.Check(false, $"could not answer the test bed's roof, fire and wet checks: {e.Message}");
                rig.End();
                return null;
            }
            var bedGo = s.SpawnAny("bed", Stage.Ground(bedAt), s.Forward);
            rig.Bed = bedGo != null ? bedGo.GetComponent<Bed>() : null;
            if (!c.Check(rig.Bed != null, "could not spawn a bed"))
            {
                rig.End();
                return null;
            }
            _testBed = rig.Bed;
            return rig;
        }

        // World time = that part of the day the test began on (plus days). Smoothed clock snapped: the day and night
        // flags follow at the next physics step.
        internal void SetClock(float raw, int dayOffset = 0)
        {
            SetTime((Day0 + dayOffset) * Length + raw * Length);
        }

        private void SetTime(double t)
        {
            _net.SetNetTime(t);
            var raw = Mathf.Clamp01((float)(t % Length / Length));
            _env.m_smoothDayFraction = _env.RescaleDayFraction(raw);
        }

        // Sleep cooldowns over (30 s after a wake-up, 10 s between two sleeps), no Rested: a fresh evening.
        internal void FreshTimers()
        {
            var now = _net.GetTimeSeconds();
            _player.m_wakeupTime = now - 1000.0;
            _game.m_lastSleepTime = now - 1000.0;
            _player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectRested, true);
        }

        // Part of the day (0-1 of the day length) of the world time now.
        internal float RawNow() => Mathf.Clamp01((float)(_net.GetTimeSeconds() % Length / Length));

        internal int DayNow() => _env.GetDay(_net.GetTimeSeconds());

        internal void End()
        {
            _testBed = null;
            Step("sleep", () =>
            {
                if (ZNet.instance == null || Game.instance == null)
                {
                    return;
                }
                // Test cut short while asleep: stop the skip and wake now, as vanilla's stop branch does.
                if (_env != null && _env.IsTimeSkipping())
                {
                    _env.m_skipTime = false;
                }
                if (_game.m_sleeping && ZRoutedRpc.instance != null)
                {
                    _game.m_sleeping = false;
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
                }
                if (_player != null && _player.IsSleeping())
                {
                    _player.SetSleeping(false);
                }
                if (_player != null && _player.IsAttached())
                {
                    _player.AttachStop();
                }
            });
            Step("bed checks", () =>
            {
                if (_bypass != null)
                {
                    _bypass.UnpatchSelf();
                    _bypass = null;
                }
            });
            Step("spawn point", () =>
            {
                if (ZNet.instance == null)
                {
                    return;
                }
                if (_hadSpawn)
                {
                    _profile.SetCustomSpawnPoint(_spawn);
                }
                else
                {
                    _profile.ClearCustomSpawnPoint();
                }
            });
            Step("time", () =>
            {
                if (ZNet.instance == null || _env == null)
                {
                    return;
                }
                SetTime(_time);
                _game.m_lastSleepTime = _lastSleep;
                if (_player != null)
                {
                    _player.m_wakeupTime = _wakeup;
                }
            });
            Step("rested", () =>
            {
                if (_player == null)
                {
                    return;
                }
                var seman = _player.GetSEMan();
                if (!_hadRested)
                {
                    seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                    return;
                }
                if (!seman.HaveStatusEffect(SEMan.s_statusEffectRested))
                {
                    seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
                }
                var rested = seman.GetStatusEffect(SEMan.s_statusEffectRested);
                if (rested != null)
                {
                    rested.m_ttl = _restedTtl;
                    rested.m_time = _restedTime;
                }
            });
            Step("weather and dream", () =>
            {
                if (_env != null)
                {
                    _env.m_debugEnv = _debugEnv;
                }
                CinematicsManager.m_dreamCinematic = _dream ?? "";
            });
        }

        // Each step alone: one failing must not skip the others.
        private static void Step(string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                Log.Error($"{SelfTest.Prefix} sleep test clean-up of {what} failed: {e}");
            }
        }
    }

    // What must never happen while afraid creatures are near a resting or sleeping player.
    private sealed class QuietWatch
    {
        internal bool Sensed;
        internal bool Targeted;
        internal bool Attacked;
        internal bool Combat;
        internal bool Ran;

        internal void Look(Player player, List<MonsterAI> pack)
        {
            var music = MusicMan.instance;
            Sensed |= player.IsSensed();
            Targeted |= player.IsTargeted();
            Combat |= music != null && music.m_combatTimer > 0f;
            foreach (var g in pack)
            {
                if (g == null)
                {
                    continue;
                }
                Attacked |= g.m_targetCreature == player;
                Ran |= FearOf(g) == player;
            }
        }

        public override string ToString() => $"ran {Ran}, targeted the player {Attacked}, player sensed {Sensed}, player targeted {Targeted}, combat music state {Combat}";
    }

    private static List<MonsterAI> SpawnPack(Stage s, Vector3 here, List<Vector3> spots)
    {
        var pack = new List<MonsterAI>();
        foreach (var angle in new[] { -25f, 0f, 25f })
        {
            var spot = s.Spot(here, Turn(s.Forward, angle), 10f, true);
            var g = s.Spawn("Greydwarf", spot, here - spot);
            if (g != null)
            {
                pack.Add(g);
                spots.Add(spot);
            }
        }
        return pack;
    }

    // What the Use key do on the bed (Player.Interact -> Bed.Interact, every vanilla check and message).
    private static void UseBed(Player player, Bed bed) => player.Interact(bed.gameObject, false, false);

    // Use the bed and say what it answered in the middle of the screen ("" = nothing). Me empty that text first, so
    // what stand there after is this use's own answer (the bed answer in the same call; the game's message log drop
    // its oldest line at 50, so a count over the log could lose one).
    private static string UseBedAnswer(Player player, Bed bed)
    {
        var hud = MessageHud.instance;
        if (hud != null && hud.m_messageCenterText != null)
        {
            hud.m_messageCenterText.text = "";
        }
        UseBed(player, bed);
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text : "";
    }

    private static IEnumerator ClaimBed(Stage s, Checks c, BedRig rig, string label, Action eachFrame, Waiter w)
    {
        UseBed(s.Player, rig.Bed);
        yield return Until(() => rig.Bed.IsCurrent(), 3f, eachFrame, w);
        c.Check(w.Met, $"{label} setup: the first use claims the bed and makes it the player's spawn point");
    }

    // ================================================================ morale.sleep

    private static IEnumerator RunSleep()
    {
        var c = new Checks(SleepName);
        Stage s = null;
        BedRig rig = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(SleepName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            rig = BedRig.Begin(s, c, here - s.Right * 3f);
            if (rig == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(4);
            rig.SetClock(NightRaw);
            yield return Until(() => EnvMan.IsNight() && !EnvMan.IsDay(), 5f, null, w);
            if (!c.Check(w.Met, $"T03 setup: it is night (night {EnvMan.IsNight()}, day {EnvMan.IsDay()})"))
            {
                c.Report();
                yield break;
            }
            rig.FreshTimers();

            var spots = new List<Vector3>();
            var pack = SpawnPack(s, here, spots);
            if (!c.Check(pack.Count == 3, "could not spawn three Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            Action hold = () =>
            {
                s.Noise();
                for (var i = 0; i < pack.Count; i++)
                {
                    Stage.Place(pack[i].m_character, spots[i], player.transform.position - spots[i]);
                }
            };
            var music = MusicMan.instance;
            // Fights of earlier tests: the combat music state and the sensed timer run out first.
            yield return Until(() => !player.IsSensed() && !player.IsTargeted() && (music == null || music.m_combatTimer <= 0f), 10f, hold, w);
            c.Check(w.Met && music != null, "T03 setup: nobody targets the player and the combat music state is off before the watch starts");
            yield return ClaimBed(s, c, rig, "T03", hold, w);

            var watch = new QuietWatch();
            Action look = () =>
            {
                hold();
                watch.Look(player, pack);
            };
            yield return Until(() => watch.Ran, 4f, look, w);
            c.Check(w.Met, $"T03: the afraid Greydwarfs 10 m away run from the noisy rank 4 player ({DescribePack(pack, player)})");

            var refusal = Localization.instance.Localize(EnemiesNearbyToken);
            var answer = UseBedAnswer(player, rig.Bed);
            yield return Until(() => player.IsAttached() && player.InBed(), 2f, look, w);
            if (!c.Check(w.Met && answer != refusal,
                    $"T03: at night, with afraid Greydwarfs running 10 m away, the bed lets the player lie down and shows no \"{refusal}\" "
                    + $"(lying {player.InBed()}, the bed answered \"{answer}\")"))
            {
                c.Report();
                yield break;
            }
            yield return Until(() => player.IsSleeping(), 15f, look, w);
            if (!c.Check(w.Met, $"T03: the game starts the sleep within 15 s of lying down (asleep {player.IsSleeping()}, still lying {player.InBed()}, {watch})"))
            {
                c.Report();
                yield break;
            }
            SelfTest.Note(SleepName, $"asleep {F(w.Took)} s after lying down");
            yield return Until(() => !player.IsSleeping(), 45f, look, w);
            var morning = rig.DayNow() == rig.Day0 + 1 && rig.RawNow() >= 0.149f && rig.RawNow() < 0.3f;
            c.Check(w.Met && morning && !player.InBed(),
                $"T03: the player sleeps through the night and wakes the next morning (woke {w.Met} after {F(w.Took)} s, day {rig.Day0} -> {rig.DayNow()}, "
                + $"part of the day {F(rig.RawNow())}, still in bed {player.InBed()})");
            yield return Wait(1f, look);
            c.Check(!watch.Attacked && !watch.Sensed && !watch.Targeted,
                $"T03: from lying down to waking up no Greydwarf ever targets the player, who is never sensed or targeted ({watch})");
            c.Check(!watch.Combat, "T03: no combat music: the game's combat music state never started during the night");
            c.Report();
        }
        finally
        {
            if (rig != null)
            {
                rig.End();
            }
            Finish(s);
        }
    }

    // ================================================================ morale.day-sleep

    private static IEnumerator RunDaySleep()
    {
        if (!OtherModOn(DaySleepName, SleepThroughDayGuid, "MC Sleep Through the Day", "X04"))
        {
            yield break;
        }
        var c = new Checks(DaySleepName);
        Stage s = null;
        BedRig rig = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(DaySleepName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            rig = BedRig.Begin(s, c, here - s.Right * 3f);
            if (rig == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(4);
            rig.SetClock(MorningRaw);
            yield return Until(() => EnvMan.IsDay() && !EnvMan.IsNight() && !EnvMan.IsAfternoon(), 5f, null, w);
            if (!c.Check(w.Met, $"X04 setup: it is morning (day {EnvMan.IsDay()}, afternoon {EnvMan.IsAfternoon()}, night {EnvMan.IsNight()})"))
            {
                c.Report();
                yield break;
            }
            rig.FreshTimers();

            var spots = new List<Vector3>();
            var pack = SpawnPack(s, here, spots);
            if (!c.Check(pack.Count == 3, "could not spawn three Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var distance = 18f;
            Action hold = () =>
            {
                s.Noise();
                for (var i = 0; i < pack.Count; i++)
                {
                    var spot = Stage.Ground(here + (spots[i] - here).normalized * distance);
                    Stage.Place(pack[i].m_character, spot, player.transform.position - spot);
                }
            };
            var music = MusicMan.instance;
            yield return Until(() => !player.IsSensed() && !player.IsTargeted() && (music == null || music.m_combatTimer <= 0f), 10f, hold, w);
            c.Check(w.Met, "X04 setup: nobody targets the player before the bed is used");
            yield return ClaimBed(s, c, rig, "X04", hold, w);
            var refusal = Localization.instance.Localize(EnemiesNearbyToken);

            // ----- hostile ones (rank 0) stop a day sleep -----
            Standing.TestOverride = new Standing.Override(0);
            distance = 6f;
            yield return Until(() => player.IsSensed(), 8f, hold, w);
            c.Check(w.Met, $"X04 contrast setup: at rank 0 the Greydwarfs 6 m away go for the player, who is sensed ({DescribePack(pack, player)})");
            hold();
            var answer = UseBedAnswer(player, rig.Bed);
            c.Check(answer == refusal && !player.IsAttached(),
                $"X04: hostile Greydwarfs near the bed stop a day sleep: the bed shows \"{refusal}\" and the player does not lie down "
                + $"(the bed answered \"{answer}\", lying {player.IsAttached()})");
            yield return Wait(0.3f, hold);
            if (player.IsAttached())
            {
                player.AttachStop();
            }

            // ----- afraid and calm (18 m, beyond the fear range): the bed let the player lie down -----
            Standing.TestOverride = new Standing.Override(4);
            distance = 18f;
            yield return Until(() => !player.IsSensed() && !player.IsTargeted() && pack.TrueForAll(g => FearOf(g) == null && !g.IsAlerted() && g.m_targetCreature == null),
                12f, hold, w);
            c.Check(w.Met, $"X04 setup: back at rank 4 the Greydwarfs drop the player and are calm 18 m away ({DescribePack(pack, player)})");
            answer = UseBedAnswer(player, rig.Bed);
            c.Check(player.IsAttached() && player.InBed() && answer != refusal,
                $"X04: afraid Greydwarfs that are calm 18 m from the bed do not stop a day sleep: the player lies down, no \"{refusal}\" "
                + $"(lying {player.InBed()}, the bed answered \"{answer}\")");
            if (player.IsAttached())
            {
                player.AttachStop(); // up again before the game's next sleep check: the sleep itself is the next step
            }
            yield return null;

            // ----- afraid and running (10 m): lie down, the day sleep start and ends the same day -----
            distance = 10f;
            var watch = new QuietWatch();
            Action look = () =>
            {
                hold();
                watch.Look(player, pack);
            };
            yield return Until(() => watch.Ran, 5f, look, w);
            c.Check(w.Met, $"X04: the afraid Greydwarfs 10 m away run from the noisy rank 4 player ({DescribePack(pack, player)})");
            answer = UseBedAnswer(player, rig.Bed);
            yield return Until(() => player.IsAttached() && player.InBed(), 2f, look, w);
            if (!c.Check(w.Met && answer != refusal,
                    $"X04: in the morning, with afraid Greydwarfs running 10 m away, the bed lets the player lie down (lying {player.InBed()}, "
                    + $"the bed answered \"{answer}\")"))
            {
                c.Report();
                yield break;
            }
            var startRaw = rig.RawNow();
            yield return Until(() => player.IsSleeping(), 20f, look, w);
            if (!c.Check(w.Met, $"X04: the day sleep starts within 20 s of lying down (asleep {player.IsSleeping()}, still lying {player.InBed()}, {watch})"))
            {
                c.Report();
                yield break;
            }
            yield return Until(() => !player.IsSleeping(), 45f, look, w);
            // Wake-up hour is a setting of that mod (13:00 to 23:00): any time of the same day after 13:00 fits.
            var sameDay = rig.DayNow() == rig.Day0 && rig.RawNow() > 0.55f && rig.RawNow() > startRaw;
            c.Check(w.Met && sameDay && !player.InBed(),
                $"X04: the day sleep runs to its end and the player wakes later the same day (woke {w.Met} after {F(w.Took)} s, day {rig.Day0} -> {rig.DayNow()}, "
                + $"part of the day {F(startRaw)} -> {F(rig.RawNow())}, still in bed {player.InBed()})");
            c.Check(!watch.Attacked && !watch.Sensed && !watch.Targeted,
                $"X04: during the day sleep no Greydwarf ever targets the player, who is never sensed or targeted ({watch})");
            c.Report();
        }
        finally
        {
            if (rig != null)
            {
                rig.End();
            }
            Finish(s);
        }
    }
}
#endif
