using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only (calls vanish in Release). Tests that need more than one world session, and the log of the whole
// game session (TESTING.md items in brackets):
//   morale.disabled.vanilla  real restart with Enabled = false (probe do it): first game = a Boar is afraid of the
//                            rank 8 player; second game = mod off from the start, none of its patches applied, no
//                            standing published, no "Standing:" line, same creatures attack as in the normal game (L03;
//                            the Status text itself is probe.disabled.<GUID>)
//   morale.session-log       no error line of the mod (or naming its code) since the game started, no warning of the
//                            mod outside its own tests (T24, L04: this game's log; own test because it also see what
//                            other mods' tests made the mod log)
// Me register from Plugin.BindConfig (RegisterAtStart): in the second game the mod is off, OnActivated never run.
// Session log = BepInEx listener added at plugin start, on client and on dedicated server (SelfTestsMp2.cs ask the
// server's copy through a server step).
internal static partial class SelfTests
{
    [Conditional("DEBUG")]
    internal static void RegisterAtStart()
    {
#if DEBUG
        SessionLog.Start();
        SelfTest.RegisterDisabledStart(DisabledName, DisabledLeave, DisabledCheck);
        RegisterMultiplayerAtStart();
#endif
    }

#if DEBUG
    private const string DisabledName = "morale.disabled.vanilla";
    private const string SessionLogName = "morale.session-log";
    private const string DisabledCarryKey = DisabledName + ".baseline";

    // Tests of SelfTestsSleep.cs, SelfTestsBoss.cs, SelfTestsPlayers.cs, SelfTestsOtherMods.cs and SelfTestsMp2.cs,
    // registered with the others in OnActivated (SelfTests.Register). The session log test go last.
    private static void RegisterAuto()
    {
        SelfTest.Register(SleepName, RunSleep);
        SelfTest.Register(DaySleepName, RunDaySleep);
        SelfTest.Register(BossKillName, RunBossKill);
        SelfTest.Register(RaidName, RunRaid);
        SelfTest.Register(NightHuntersName, RunNightHunters);
        SelfTest.Register(TwoPlayersName, RunTwoPlayers);
        SelfTest.Register(TowerBashName, RunTowerBash);
        SelfTest.Register(TowerNgName, RunTowerBashNgPlus);
        SelfTest.Register(DualWieldName, RunDualWield);
        SelfTest.Register(MovesetName, RunMoveset);
        SelfTest.Register(HarpoonHookName, RunHarpoonHook);
        SelfTest.Register(EncyclopediaName, RunEncyclopedia);
        SelfTest.Register(KillCountsName, RunKillCounts);
        SelfTest.Register(SneakXpName, RunSneakXp);
        SelfTest.Register(SessionLogName, RunSessionLog);
        RegisterMultiplayerAuto();
    }

    private static void UnregisterAuto()
    {
        foreach (var name in new[]
                 {
                     SleepName, DaySleepName, BossKillName, RaidName, NightHuntersName, TwoPlayersName, TowerBashName, TowerNgName, DualWieldName,
                     MovesetName, HarpoonHookName, EncyclopediaName, KillCountsName, SneakXpName, SessionLogName,
                 })
        {
            SelfTest.Unregister(name);
        }
        UnregisterMultiplayerAuto();
    }

    // ================================================================ session log

    // Me keep, from plugin start to quit: every line of the mod's own log source, and error lines of other sources
    // that name the mod's code (uncaught exception: Unity log it with the stack). Self test result lines left out.
    private sealed class SessionLog : ILogListener
    {
        private const int MaxLines = 20000;

        internal struct Line
        {
            internal LogLevel Level;
            internal string Text;
            internal double At;      // seconds since the listener started
            internal bool Own;       // mod's own log source
            internal bool InTest;    // a morale test was recording then (its Checks judge that line)
        }

        private static SessionLog _instance;
        private static readonly string CodeName = typeof(SelfTests).Namespace + ".";
        private readonly object _gate = new object();
        private readonly List<Line> _lines = new List<Line>();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private bool _overflowed;

        internal static bool Started => _instance != null;

        // A line came after the list was full: lines since then were not kept, so "no error line" is not proven.
        internal static bool Overflowed => _instance != null && _instance._overflowed;

        internal static void Start()
        {
            if (_instance == null)
            {
                _instance = new SessionLog();
                BepInEx.Logging.Logger.Listeners.Add(_instance);
            }
        }

        // Copy of the lines `keep` say yes to.
        internal static List<Line> Lines(Func<Line, bool> keep)
        {
            var found = new List<Line>();
            var log = _instance;
            if (log == null)
            {
                return found;
            }
            lock (log._gate)
            {
                foreach (var line in log._lines)
                {
                    if (keep(line))
                    {
                        found.Add(line);
                    }
                }
            }
            return found;
        }

        internal static double Seconds => _instance != null ? _instance._clock.Elapsed.TotalSeconds : 0.0;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                var own = eventArgs.Source.SourceName == ModInfo.Name;
                var bad = (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                if (!own && !bad)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? (eventArgs.Data != null ? eventArgs.Data.ToString() : null);
                if (text == null || text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                if (!own && text.IndexOf(CodeName, StringComparison.Ordinal) < 0 && text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) < 0)
                {
                    return;
                }
                var inTest = LogWatch.Recording;
                lock (_gate)
                {
                    if (_lines.Count < MaxLines)
                    {
                        _lines.Add(new Line { Level = eventArgs.Level, Text = text, At = _clock.Elapsed.TotalSeconds, Own = own, InTest = inTest });
                    }
                    else
                    {
                        _overflowed = true;
                    }
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
        }
    }

    private static bool IsError(SessionLog.Line line) => (line.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;

    private static string Short(string text) => text == null ? "" : text.Length > 240 ? text.Substring(0, 240) + "..." : text;

    // Error lines of this game session: the mod's own, and other sources' errors that name its code.
    private static List<SessionLog.Line> SessionErrors() => SessionLog.Lines(IsError);

    // Warnings of the mod logged while no morale test was recording (world start, other mods' tests, between tests).
    private static List<SessionLog.Line> SessionStrayWarnings() =>
        SessionLog.Lines(line => line.Own && !line.InTest && (line.Level & LogLevel.Warning) != 0);

    private static int SessionCount(string part) =>
        SessionLog.Lines(line => line.Own && line.Text.IndexOf(part, StringComparison.Ordinal) >= 0).Count;

    // ================================================================ morale.session-log

    private static IEnumerator RunSessionLog()
    {
        yield return null;
        if (!SessionLog.Started)
        {
            SelfTest.Fail(SessionLogName, "the session log listener was never started (Plugin.BindConfig did not register it)");
            yield break;
        }
        if (SessionLog.Overflowed)
        {
            SelfTest.Fail(SessionLogName, "the session log is full: lines logged after that were not kept, so this game's log cannot be judged here");
            yield break;
        }
        var errors = SessionErrors();
        var warnings = SessionStrayWarnings();
        var own = SessionLog.Lines(line => line.Own).Count;
        SelfTest.Note(SessionLogName, $"{own} line(s) of {ModInfo.Name} in {F(SessionLog.Seconds)} s of this game session");
        if (errors.Count > 0)
        {
            SelfTest.Fail(SessionLogName, $"{errors.Count} error line(s) of {ModInfo.Name} (or naming its code) since the game started, first: {Short(errors[0].Text)}");
        }
        else
        {
            SelfTest.Pass(SessionLogName, $"no error line of {ModInfo.Name} and none naming its code since the game started");
        }
        if (warnings.Count > 0)
        {
            SelfTest.Fail(SessionLogName, $"{warnings.Count} warning line(s) of {ModInfo.Name} outside its own tests since the game started, first: {Short(warnings[0].Text)}");
        }
        else
        {
            SelfTest.Pass(SessionLogName, $"no warning line of {ModInfo.Name} outside its own tests since the game started");
        }
    }

    // ================================================================ morale.disabled.vanilla

    private static bool PatchedByMod(MethodBase method)
    {
        if (method == null)
        {
            return false;
        }
        var info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }
        foreach (var owner in info.Owners)
        {
            if (owner == ModInfo.Guid)
            {
                return true;
            }
        }
        return false;
    }

    // Every game method the mod patches while it be on (all of Patches/*.cs: 15), by name for the report. New patch
    // in Patches/ = new line here.
    private static List<KeyValuePair<string, MethodBase>> ModTargets()
    {
        return new List<KeyValuePair<string, MethodBase>>
        {
            new KeyValuePair<string, MethodBase>("MonsterAI.UpdateAI", AccessTools.Method(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))),
            new KeyValuePair<string, MethodBase>("MonsterAI.HuntPlayer", AccessTools.Method(typeof(MonsterAI), nameof(MonsterAI.HuntPlayer))),
            new KeyValuePair<string, MethodBase>("MonsterAI.RPC_OnNearProjectileHit",
                AccessTools.Method(typeof(MonsterAI), nameof(MonsterAI.RPC_OnNearProjectileHit),
                    new[] { typeof(long), typeof(Vector3), typeof(float), typeof(ZDOID) })),
            new KeyValuePair<string, MethodBase>("BaseAI.CanSenseTarget",
                AccessTools.Method(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), new[] { typeof(Character), typeof(bool) })),
            new KeyValuePair<string, MethodBase>("BaseAI.FindEnemy", AccessTools.Method(typeof(BaseAI), nameof(BaseAI.FindEnemy))),
            new KeyValuePair<string, MethodBase>("BaseAI.OnDeath", AccessTools.Method(typeof(BaseAI), nameof(BaseAI.OnDeath))),
            new KeyValuePair<string, MethodBase>("BaseAI.InStealthRange", AccessTools.Method(typeof(BaseAI), nameof(BaseAI.InStealthRange))),
            new KeyValuePair<string, MethodBase>("Character.RPC_Damage", AccessTools.Method(typeof(Character), nameof(Character.RPC_Damage))),
            new KeyValuePair<string, MethodBase>("Game.RPC_RegisterKill", AccessTools.Method(typeof(Game), nameof(Game.RPC_RegisterKill))),
            new KeyValuePair<string, MethodBase>("Player.OnSpawned", AccessTools.Method(typeof(Player), nameof(Player.OnSpawned))),
            new KeyValuePair<string, MethodBase>("ZNet.Awake", AccessTools.Method(typeof(ZNet), nameof(ZNet.Awake))),
            new KeyValuePair<string, MethodBase>("ZNet.OnNewConnection", AccessTools.Method(typeof(ZNet), nameof(ZNet.OnNewConnection))),
            new KeyValuePair<string, MethodBase>("ZNet.RPC_PeerInfo", AccessTools.Method(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))),
            new KeyValuePair<string, MethodBase>("ZNet.Update", AccessTools.Method(typeof(ZNet), nameof(ZNet.Update))),
            new KeyValuePair<string, MethodBase>("ZNet.OnDestroy", AccessTools.Method(typeof(ZNet), nameof(ZNet.OnDestroy))),
        };
    }

    // Every patched method of this game process that carry a patch of the mod (its Harmony id = its GUID; test-only
    // patches use other ids). Me ask Harmony itself, so a patch the list above forgot is seen too.
    private static List<string> PatchedAnywhere()
    {
        var names = new List<string>();
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            if (PatchedByMod(method))
            {
                names.Add((method.DeclaringType != null ? method.DeclaringType.Name + "." : "") + method.Name);
            }
        }
        return names;
    }

    private static string PatchedList(bool wanted, out int found, out int count)
    {
        var names = new List<string>();
        found = 0;
        count = 0;
        foreach (var target in ModTargets())
        {
            if (target.Value == null)
            {
                names.Add(target.Key + " (not found in this game)");
                continue;
            }
            count++;
            if (PatchedByMod(target.Value))
            {
                found++;
                if (wanted)
                {
                    continue;
                }
                names.Add(target.Key);
            }
            else if (wanted)
            {
                names.Add(target.Key);
            }
        }
        return names.Count == 0 ? "none" : string.Join(", ", names.ToArray());
    }

    // First game, mod on: the same creature this character will meet with the mod off is afraid of it now.
    private static IEnumerator DisabledLeave()
    {
        var c = new Checks(DisabledName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(DisabledName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(8);
            var missing = PatchedList(true, out var patched, out var targets);
            var everywhere = PatchedAnywhere();
            c.Check(Plugin.Live && patched == targets && everywhere.Count == targets,
                $"first game: the mod is on and its patches are applied ({patched} of {targets}; missing: {missing}; Harmony lists {everywhere.Count} game method(s) "
                + $"with a patch of the mod: {string.Join(", ", everywhere.ToArray())})");
            // Same listener and same search as the second game's "no Standing: line" check: it must see such lines.
            c.Check(SessionLog.Started && SessionCount("Standing:") > 0,
                $"first game: the session log sees the mod's \"Standing:\" lines ({SessionCount("Standing:")} since the game started)");
            var spot = s.Spot(here, s.Forward, 6f, true);
            var boar = SpawnMeadows(s, c, "Boar", spot, here - spot);
            if (boar != null)
            {
                Action hold = () =>
                {
                    s.Noise();
                    Stage.Place(boar.m_character, spot, player.transform.position - spot);
                };
                yield return Until(() => FearOf(boar) == player && boar.m_targetCreature == null, 4f, hold, w);
                c.Check(w.Met && Attitudes.Judge(boar, player) == Attitude.Afraid,
                    $"first game, mod on, boss rank 8: a Boar 6 m away is afraid of the player and runs (Judge {Attitudes.Judge(boar, player)}, "
                    + $"runs from {Who(FearOf(boar))}, target {Who(boar.m_targetCreature)})");
                c.Check(player.m_nview.GetZDO().GetByteArray(CreatureKeys.Standing) != null, "first game: the player's standing is published");
            }
            var carried = SelfTest.Carry(DisabledCarryKey, w.Met ? "afraid" : "not afraid");
            c.Check(carried, "the result of the first game could be kept for the second game");
            c.Report(" (first game; the second game starts with Enabled = false)");
        }
        finally
        {
            Finish(s);
        }
    }

    // Second game: every MC mod Enabled = false before the game started. Me call no rule code of the mod here (it
    // would read the config and could log): only its switches, the game's patch table, the player ZDO and creatures.
    private static IEnumerator DisabledCheck()
    {
        var c = new Checks(DisabledName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            c.Check(SelfTest.Stage == SelfTest.DisabledStage, $"this is the second game of the run, started with the mods off (stage '{SelfTest.Stage}')");
            var baseline = SelfTest.Carried(DisabledCarryKey);
            c.Check(baseline == "afraid", $"the first game saw the Boar afraid with the mod on (carried: '{baseline ?? "nothing"}')");
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(view != null && view.Value.IsDisabledByUser && !Plugin.Live,
                $"the mod is off: state {(view != null ? view.Value.State : "not registered")}, status \"{(view != null ? view.Value.Status : "")}\"");
            var left = PatchedList(false, out var patched, out var targets);
            var everywhere = PatchedAnywhere();
            c.Check(patched == 0 && targets > 0 && everywhere.Count == 0,
                $"none of the mod's patches is applied ({patched} of {targets} applied: {left}; game methods with a patch of the mod anywhere: "
                + $"{(everywhere.Count == 0 ? "none" : string.Join(", ", everywhere.ToArray()))})");
            c.Check(SessionLog.Started && !SessionLog.Overflowed && SessionCount("Standing:") == 0,
                $"no \"Standing:\" line in the log since the game started ({SessionCount("Standing:")} found; session log running {SessionLog.Started}, full {SessionLog.Overflowed})");
            var errors = SessionErrors();
            var warnings = SessionLog.Lines(line => line.Own && (line.Level & LogLevel.Warning) != 0);
            c.Check(errors.Count == 0 && warnings.Count == 0,
                $"no warning or error line of the mod since the game started ({warnings.Count} warning(s), {errors.Count} error(s)"
                + (errors.Count > 0 ? $", first error: {Short(errors[0].Text)}" : warnings.Count > 0 ? $", first warning: {Short(warnings[0].Text)}" : "") + ")");

            s = Stage.Begin(DisabledName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            c.Check(player.m_nview.GetZDO().GetByteArray(CreatureKeys.Standing) == null,
                "the player's game object carries no standing of the mod (nothing published)");
            // Same forced rank as in the first game: it must change nothing while the mod is off.
            Standing.TestOverride = new Standing.Override(8);
            c.Check(player.m_nview.GetZDO().GetByteArray(CreatureKeys.Standing) == null, "a forced boss rank 8 publishes nothing while the mod is off");
            foreach (var prefab in new[] { "Boar", "Greydwarf" })
            {
                var spot = s.Spot(here, s.Forward, 6f, true);
                var creature = s.Spawn(prefab, spot, here - spot);
                if (!c.Check(creature != null, $"could not spawn {prefab}"))
                {
                    continue;
                }
                var ran = false;
                Action hold = () =>
                {
                    s.Noise();
                    Stage.Place(creature.m_character, spot, player.transform.position - spot);
                    ran |= FearOf(creature) != null;
                };
                yield return Until(() => creature.m_targetCreature == player && creature.IsAlerted(), 8f, hold, w);
                c.Check(w.Met && !ran && Attitudes.Judge(creature, player) == Attitude.Hostile,
                    $"mod off: a {prefab} 6 m away behaves as in the normal game: it targets the player and is alerted within 8 s, never runs "
                    + $"(target {Who(creature.m_targetCreature)}, alerted {creature.IsAlerted()}, ran {ran}, waited {F(w.Took)} s)");
                c.Check(ProvokerCountOf(creature) == 0 && RoutUntilOf(creature) == 0L, $"mod off: the {prefab} carries no record of the mod");
                s.Destroy(creature);
                yield return null;
            }
            c.Report(" (second game, mod off from the start)");
        }
        finally
        {
            Finish(s);
        }
    }
#endif
}
