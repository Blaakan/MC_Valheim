#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Tools of the later self tests (SelfTestsMore.cs single player, SelfTestsMp.cs multiplayer), and
// their registration (called from SelfTests.Register / Unregister):
//   log tap     listen to this mod's own log lines (count a line since a mark; count its errors);
//   clock       seconds a test used, room left before the probe timeout (the trip home always keeps its share);
//   hud log     the message log of the HUD emptied for the test and put back after (count a banner exactly);
//   console     run a console command like typed, read the lines it printed;
//   stones      a Malicious Ice broken by damage (the game's own break path), creatures killed by damage;
//   land        one invaded area per travelling test, never the one of dn.area: what a test leaves there (Jotun alive,
//               kill counts) never meets the next test.
// Same rule as SelfTests.cs: state forced only in memory (test rules, test stones, test Kall); the real stone key and
// the persistent events are put back by the test that changed them; never a config value in a single-player test.
internal static partial class SelfTests
{
    private const string BreaksName = "dn.breaks";
    private const string StarsName = "dn.stars";
    private const string CommandName = "dn.command";
    private const string RequestName = "dn.request";
    private const string MapDetailName = "dn.mapdetail";
    // Real bugs, each alone in its own test (they fail until the mod is fixed; nothing else waits on them).
    private const string KillTargetName = "dn.bug.killtarget";
    private const string CapsName = "dn.bug.caps";
    private const string KillCountName = "dn.x.kills";
    private const string DormantName = "dn.dormant";
    private const string Stage1Name = "dn.stage1";
    private const string StormName = "dn.storm";
    private const string BandsName = "dn.bands";
    private const string KallFightName = "dn.kallfight";
    private const string KallReturnName = "dn.kallreturn";
    private const string KallRestartName = "dn.kallrestart";
    private const string OldWorldName = "dn.oldworld";
    private const string ToggleName = "dn.toggle";
    private const string CleanLogName = "dn.cleanlog";

    private const string MpRulesName = "dn.mp.rules";
    private const string MpStoneName = "dn.mp.stone";
    private const string MpCommandName = "dn.mp.command";
    private const string MpKallName = "dn.mp.kall";
    private const string MpClearedName = "dn.mp.cleared";

    // Server halves (run on the dedicated server by the server probe).
    private const string StepState = "dn.mp.state";
    private const string StepSet = "dn.mp.set";
    private const string StepKill = "dn.mp.kill";

    private const string OwnNamespace = "MC.Exploration.DeepNorthAwakeningMod";
    private const string StatsModGuid = "MC.Exploration.Stats.PerCreature";

    // Land slots: dn.area own the nearest invaded area (FindAwakeLand), each later test the next ones.
    private const int SlotStars = 1;
    private const int SlotStorm = 1;
    private const int SlotBands = 2;
    private const int SlotKallFight = 3;
    private const int SlotKallReturn = 4;
    private const int SlotKallRestart = 5;
    private const int SlotToggle = 6;
    private const int SlotKillTarget = 7;
    private const int SlotOther = 8;
    private const int SlotsUsed = 9;

    private static void RegisterMore()
    {
        EnsureTap();
        // No travel first, then the travelling ones, clean log last (it looks back on all of them).
        SelfTest.Register(BreaksName, RunBreaks);
        SelfTest.Register(StarsName, RunStars);
        SelfTest.Register(CommandName, RunCommand);
        SelfTest.Register(RequestName, RunRequest);
        SelfTest.Register(MapDetailName, RunMapDetail);
        SelfTest.Register(KillTargetName, RunKillTarget);
        SelfTest.Register(KillCountName, RunKillCount);
        SelfTest.Register(DormantName, RunDormant);
        SelfTest.Register(Stage1Name, RunStage1);
        SelfTest.Register(StormName, RunStorm);
        SelfTest.Register(BandsName, RunBands);
        SelfTest.Register(KallFightName, RunKallFight);
        SelfTest.Register(KallReturnName, RunKallReturn);
        SelfTest.Register(KallRestartName, RunKallRestart);
        SelfTest.Register(OldWorldName, RunOldWorld);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(CapsName, RunCaps);
        SelfTest.Register(CleanLogName, RunCleanLog);

        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpStoneName, SelfTest.Modded, RunMpStone);
        SelfTest.RegisterMultiplayer(MpCommandName, SelfTest.Modded, RunMpCommand);
        SelfTest.RegisterMultiplayer(MpKallName, SelfTest.Modded, RunMpKall);
        SelfTest.RegisterMultiplayer(MpClearedName, SelfTest.Modded, RunMpCleared);
        SelfTest.RegisterServerStep(StepState, ServerState);
        SelfTest.RegisterServerStep(StepSet, ServerSet);
        SelfTest.RegisterServerStep(StepKill, ServerKill);
    }

    private static void UnregisterMore()
    {
        foreach (var name in new[]
                 {
                     BreaksName, StarsName, CommandName, RequestName, MapDetailName, KillTargetName, KillCountName,
                     DormantName, Stage1Name, StormName, BandsName, KallFightName, KallReturnName, KallRestartName,
                     OldWorldName, ToggleName, CapsName, CleanLogName,
                 })
        {
            SelfTest.Unregister(name);
        }
        foreach (var name in new[] { MpRulesName, MpStoneName, MpCommandName, MpKallName, MpClearedName })
        {
            SelfTest.UnregisterMultiplayer(name);
        }
        SelfTest.UnregisterServerStep(StepState);
        SelfTest.UnregisterServerStep(StepSet);
        SelfTest.UnregisterServerStep(StepKill);
    }

    // ---------- log tap ----------

    // Me hear every log line of this game (BepInEx listener, any thread: lock). Keep this mod's own lines (last Keep),
    // count its error lines: own source with level Error / Fatal, and error lines of any source that name this mod's
    // code (exception that escaped). Self test result lines are not errors of the mod.
    private sealed class LogTap : ILogListener
    {
        private const int Keep = 800;
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();
        private int _first;
        private int _errors;
        private string _firstError;

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e == null)
                {
                    return;
                }
                var text = e.Data as string ?? (e.Data != null ? e.Data.ToString() : null);
                if (text == null)
                {
                    return;
                }
                var own = e.Source != null && e.Source.SourceName == ModInfo.Name;
                var error = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                if (!own && !(error && text.IndexOf(OwnNamespace, StringComparison.Ordinal) >= 0))
                {
                    return;
                }
                lock (_gate)
                {
                    if (own)
                    {
                        _lines.Add(text);
                        if (_lines.Count > Keep)
                        {
                            _lines.RemoveAt(0);
                            _first++;
                        }
                    }
                    if (error && text.IndexOf(SelfTest.Prefix, StringComparison.Ordinal) < 0)
                    {
                        _errors++;
                        if (_firstError == null)
                        {
                            var cut = text.IndexOf('\n');
                            _firstError = cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
        }

        // Number of the next line: Count / Last with it look only at lines logged after now.
        internal int Mark()
        {
            lock (_gate)
            {
                return _first + _lines.Count;
            }
        }

        internal int Seen => Mark();

        // Own lines since the mark that hold every piece.
        internal int Count(int since, params string[] pieces)
        {
            lock (_gate)
            {
                var n = 0;
                for (var i = Math.Max(0, since - _first); i < _lines.Count; i++)
                {
                    if (Holds(_lines[i], pieces))
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        // Newest own line since the mark that holds every piece ("" = none).
        internal string Last(int since, params string[] pieces)
        {
            lock (_gate)
            {
                for (var i = _lines.Count - 1; i >= Math.Max(0, since - _first); i--)
                {
                    if (Holds(_lines[i], pieces))
                    {
                        return _lines[i];
                    }
                }
                return "";
            }
        }

        internal void Errors(out int count, out string first)
        {
            lock (_gate)
            {
                count = _errors;
                first = _firstError ?? "";
            }
        }

        private static bool Holds(string line, string[] pieces)
        {
            foreach (var p in pieces)
            {
                if (line.IndexOf(p, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static LogTap _tap;

    // Once per game (the listener stays: a Debug build only).
    private static LogTap EnsureTap()
    {
        if (_tap == null)
        {
            _tap = new LogTap();
            BepInEx.Logging.Logger.Listeners.Add(_tap);
        }
        return _tap;
    }

    // ---------- time ----------

    // Seconds the probe gives one test (its own setting, same variable the probe reads).
    private static float TestSeconds()
    {
        var raw = Environment.GetEnvironmentVariable("MC_SELFTEST_TIMEOUT");
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0f ? t : 120f;
    }

    // Trip home of a travelling test (vanilla distant teleport: 8 s at least, then the area).
    private const float TripSeconds = 16f;

    private sealed class Clock
    {
        private readonly float _start = Time.realtimeSinceStartup;
        private readonly float _limit = TestSeconds() - 6f;

        internal float Used => Time.realtimeSinceStartup - _start;

        internal float Left => _limit - Used;

        // Room for a step of this many seconds, and the trips still to make after it?
        internal bool Room(float step, int tripsAfter = 1) => Left >= step + tripsAfter * TripSeconds;

        // Wait no longer than what is left once the trips after it are set aside (never under 1 s).
        internal float Cap(float wanted, int tripsAfter = 1) => Mathf.Max(1f, Mathf.Min(wanted, Left - tripsAfter * TripSeconds));
    }

    private static IEnumerator Until(Func<bool> done, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end && !done())
        {
            yield return null;
        }
    }

    private static IEnumerator Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return null;
        }
    }

    // ---------- HUD messages ----------

    // The HUD keeps the last 50 messages. Me empty the list for the test (counts are exact then) and put the old lines
    // back after: what the test showed is gone from the list again.
    private sealed class HudLog
    {
        private List<string> _saved;

        internal void Begin()
        {
            var hud = MessageHud.instance;
            if (hud == null || _saved != null)
            {
                return;
            }
            _saved = new List<string>(hud.GetLog());
            hud.GetLog().Clear();
        }

        // Messages shown since Begin whose text is exactly this (already localised).
        internal int Count(string text)
        {
            var hud = MessageHud.instance;
            if (hud == null)
            {
                return 0;
            }
            var n = 0;
            foreach (var line in hud.GetLog())
            {
                if (line == text)
                {
                    n++;
                }
            }
            return n;
        }

        internal void End()
        {
            var hud = MessageHud.instance;
            if (hud == null || _saved == null)
            {
                return;
            }
            hud.GetLog().Clear();
            hud.GetLog().AddRange(_saved);
            _saved = null;
        }
    }

    private static string CenterText()
    {
        var hud = MessageHud.instance;
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text : "";
    }

    // ---------- console ----------

    private struct ConsoleMark
    {
        internal int Count;
        internal string Last;
    }

    private static ConsoleMark MarkConsole()
    {
        var m = new ConsoleMark();
        var console = global::Console.instance;
        if (console != null)
        {
            var buffer = console.m_chatBuffer;
            m.Count = buffer.Count;
            m.Last = buffer.Count > 0 ? buffer[buffer.Count - 1] : null;
        }
        return m;
    }

    // Console lines printed since the mark. The console keeps 300 lines: when full it drops old ones, then me look
    // for the last line of before (same string object) from the end.
    private static List<string> ConsoleSince(ConsoleMark mark)
    {
        var lines = new List<string>();
        var console = global::Console.instance;
        if (console == null)
        {
            return lines;
        }
        var buffer = console.m_chatBuffer;
        var from = mark.Count;
        if (buffer.Count < mark.Count || (buffer.Count == mark.Count && buffer.Count >= 300))
        {
            from = 0;
            for (var i = buffer.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(buffer[i], mark.Last))
                {
                    from = i + 1;
                    break;
                }
            }
        }
        for (var i = Math.Max(0, from); i < buffer.Count; i++)
        {
            lines.Add(buffer[i]);
        }
        return lines;
    }

    // A console command run like typed in the console (F5); the lines it printed at once.
    private static List<string> RunConsole(string command)
    {
        var console = global::Console.instance;
        if (console == null)
        {
            return new List<string>();
        }
        var mark = MarkConsole();
        console.TryRunCommand(command);
        return ConsoleSince(mark);
    }

    private static string First(List<string> lines) => lines.Count > 0 ? lines[0] : "";

    private static bool AnyStarts(List<string> lines, string start)
    {
        foreach (var l in lines)
        {
            if (l != null && l.StartsWith(start, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool AnyHolds(List<string> lines, string piece)
    {
        foreach (var l in lines)
        {
            if (l != null && l.IndexOf(piece, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    // "<a> of <b> <what>" in a status line; false = not there.
    private static bool ReadOf(string line, string what, out int a, out int b)
    {
        a = 0;
        b = 0;
        var m = Regex.Match(line ?? "", @"(\d+) of (\d+) " + Regex.Escape(what));
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
               && int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
    }

    private const string AreasAwake = "Deep North areas awake";
    private const string JotunDefeated = "Jotun defeated";

    // ---------- stones ----------

    private static bool RealKeyIs(int count)
    {
        var zs = ZoneSystem.instance;
        return zs != null && zs.GetGlobalKey(WorldState.StonesKey, out string value)
               && value == count.ToString(CultureInfo.InvariantCulture)
               && zs.GetGlobalKeys().Contains(WorldState.StonesKey + " " + count.ToString(CultureInfo.InvariantCulture));
    }

    // The hit of a tool that breaks anything (pickaxe, axe, fire, blunt, top tier), from the local player.
    private static HitData StoneHit(Vector3 point)
    {
        var hit = new HitData();
        hit.m_damage.m_pickaxe = 1000000f;
        hit.m_damage.m_chop = 1000000f;
        hit.m_damage.m_fire = 1000000f;
        hit.m_damage.m_blunt = 1000000f;
        hit.m_toolTier = 100;
        hit.m_point = point;
        hit.m_hitType = HitData.HitType.PlayerHit;
        if (Player.m_localPlayer != null)
        {
            hit.SetAttacker(Player.m_localPlayer);
        }
        return hit;
    }

    // A Malicious Ice spawned and broken like a player breaks it: damage on its Destructible, so the game's own break
    // path runs (trigger, destroy). settle = seconds between spawn and hit (multiplayer: the server must know the stone
    // before it goes). ok = it broke.
    private static IEnumerator BreakStone(Vector3 at, List<GameObject> spawned, float settle, Box ok)
    {
        ok.Ok = false;
        var go = Spawn(Stones.StonePrefab, at);
        spawned.Add(go);
        var d = go != null ? go.GetComponent<Destructible>() : null;
        if (d == null)
        {
            yield break;
        }
        // A Destructible takes no damage before its first frame is over.
        yield return null;
        yield return null;
        if (settle > 0f)
        {
            yield return new WaitForSeconds(settle);
        }
        yield return HitStone(go, ok);
    }

    // The breaking hit on a stone that stands (spawned at least two frames ago). ok = it broke.
    private static IEnumerator HitStone(GameObject go, Box ok)
    {
        ok.Ok = false;
        var d = go != null ? go.GetComponent<Destructible>() : null;
        if (d == null)
        {
            yield break;
        }
        d.Damage(StoneHit(go.transform.position));
        var until = Time.realtimeSinceStartup + 3f;
        while (go != null && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        ok.Ok = go == null;
    }

    // Break a stone and wait for the count the server writes (real key).
    private static IEnumerator BreakCounted(Vector3 at, List<GameObject> spawned, int expect, Box ok)
    {
        var broke = new Box();
        yield return BreakStone(at, spawned, 0f, broke);
        ok.Ok = false;
        if (!broke.Ok)
        {
            yield break;
        }
        var until = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < until)
        {
            WorldState.Refresh();
            if (WorldState.Stones == expect)
            {
                ok.Ok = true;
                break;
            }
            yield return null;
        }
        // Message and invasions come on the server tick of the count.
        yield return new WaitForSeconds(0.3f);
    }

    // Stone a test left standing: gone without a count (its destroy is handled a frame later, after the key went back).
    private static void KillStone(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var zdo = ZdoOf(go.GetComponent<ZNetView>());
        if (zdo != null)
        {
            Stones.TestIgnoreBreak(zdo.m_uid);
        }
        Kill(go);
    }

    private static void RestoreEvents(PersistentEventSystem pes, List<PersistentEventSystem.ActivePersistentEvent> events)
    {
        if (pes == null || events == null)
        {
            return;
        }
        pes.m_activePersistentEvents.list.Clear();
        pes.m_activePersistentEvents.list.AddRange(events);
        pes.UpdateClientEventsList(0L);
    }

    // ---------- creatures ----------

    private static ZDO ZdoOf(ZNetView nview) => nview != null && nview.IsValid() ? nview.GetZDO() : null;

    private static ZDO ZdoOf(Character ch) => ch != null ? ZdoOf(ch.m_nview) : null;

    // A death like the killall command gives (huge damage through the game's damage path). attacker = the player's
    // own kill (sword hit).
    private static void Slay(Character ch, Player attacker = null)
    {
        if (ch == null || ch.IsDead())
        {
            return;
        }
        var hit = new HitData(1E+10f)
        {
            m_blockable = false,
            m_dodgeable = false,
            m_point = ch.GetCenterPoint(),
        };
        if (attacker != null)
        {
            hit.SetAttacker(attacker);
            hit.m_skill = Skills.SkillType.Swords;
            hit.m_hitType = HitData.HitType.PlayerHit;
        }
        ch.Damage(hit);
    }

    // Loaded, alive creatures tagged by an area, within a radius (flat) of a point.
    private static List<Character> TaggedNear(Vector3 center, float radius)
    {
        var list = new List<Character>();
        foreach (var ch in Character.GetAllCharacters())
        {
            if (ch != null && !ch.IsDead() && TagOf(ch) != HeldCells.NoTag
                && Flat(ch.transform.position - center).magnitude <= radius)
            {
                list.Add(ch);
            }
        }
        return list;
    }

    // Loaded, alive nature band members.
    private static List<Character> BandLoaded()
    {
        var list = new List<Character>();
        foreach (var ch in Character.GetAllCharacters())
        {
            var zdo = ZdoOf(ch);
            if (zdo != null && !ch.IsDead() && zdo.GetInt(AreaSpawns.BandKey, 0) != 0)
            {
                list.Add(ch);
            }
        }
        return list;
    }

    // Loaded, alive Jotun army creatures (tagged or not) within a radius of a point and standing in a cell.
    private static List<Character> ArmyNear(Vector3 center, float radius, int cell)
    {
        var list = new List<Character>();
        foreach (var ch in Character.GetAllCharacters())
        {
            if (ch == null || ch.IsDead() || !Hostility.IsArmy(Hostility.PrefabOf(ch)))
            {
                continue;
            }
            var p = ch.transform.position;
            if (Flat(p - center).magnitude <= radius && WorldState.CellAt(p) == cell)
            {
                list.Add(ch);
            }
        }
        return list;
    }

    private static int CountPrefab(List<Character> list, string prefab)
    {
        var hash = prefab.GetStableHashCode();
        var n = 0;
        foreach (var ch in list)
        {
            if (ch != null && Hostility.PrefabOf(ch) == hash)
            {
                n++;
            }
        }
        return n;
    }

    private static string Kinds(List<Character> list)
    {
        var parts = new List<string>();
        foreach (var name in Hostility.Army)
        {
            parts.Add($"{CountPrefab(list, name)} {name}");
        }
        return string.Join(", ", parts.ToArray());
    }

    // Every area cap respected by the tagged Jotun of a list (design 2.3: 3 Krigen, 2 dual-axe Krigen, 2 Hexen,
    // 6 Elaking at density 100). Only dn.bug.caps ask this: one pass of the game can go over (see OneSet).
    private static bool UnderCaps(List<Character> tagged)
    {
        foreach (var name in Hostility.Army)
        {
            if (CountPrefab(tagged, name) > AreaSpawns.TestBaseCap(name))
            {
                return false;
            }
        }
        return true;
    }

    // One set of an area, never two: each kind at most its cap plus what the game's own spawn pass can add. Vanilla
    // UpdateSpawnList stop a pass at the cap, but size its last group against the count from before the pass
    // (m_maxSpawned - count, not minus what the pass already made): cap + biggest group - 1 = 4 Krigen, 2 dual-axe
    // Krigen, 2 Hexen, 8 Elaking. Seen in the run: "Spawned JotunWarrior x 1, x 1, x 2" with cap 3. A second set would
    // show on the kinds that come one by one (more than 2 dual-axe Krigen or Hexen).
    private static bool OneSet(List<Character> tagged)
    {
        foreach (var name in Hostility.Army)
        {
            if (CountPrefab(tagged, name) > AreaSpawns.TestBaseCap(name) + AreaSpawns.TestGroupMax(name) - 1)
            {
                return false;
            }
        }
        return true;
    }

    // Most Jotun one set can hold (16 at density 100).
    private static int SetMost()
    {
        var n = 0;
        foreach (var name in Hostility.Army)
        {
            n += AreaSpawns.TestBaseCap(name) + AreaSpawns.TestGroupMax(name) - 1;
        }
        return n;
    }

    private static void DestroyAll(IEnumerable<Character> list)
    {
        foreach (var ch in list)
        {
            if (ch != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(ch.gameObject);
            }
        }
    }

    // Fimbul meteors in the air now.
    private static List<Projectile> Meteors()
    {
        var list = new List<Projectile>();
        foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
        {
            if (p != null && p.name.StartsWith(AreaSpawns.MeteorPrefab, StringComparison.Ordinal))
            {
                list.Add(p);
            }
        }
        return list;
    }

    // Tagged Jotun appear and their number stays still for 2.5 s (the first pass of the area is over), or time is up.
    private static IEnumerator WaitTagged(Vector3 center, float radius, int atLeast, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        var last = -1;
        var stillSince = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup < end)
        {
            var n = TaggedNear(center, radius).Count;
            if (n != last)
            {
                last = n;
                stillSince = Time.realtimeSinceStartup;
            }
            else if (n >= atLeast && Time.realtimeSinceStartup - stillSince >= 2.5f)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
    }

    private sealed class Found<T> where T : class
    {
        internal T Value;
    }

    // This game owns the zone control under a point (it runs the area spawns there).
    private static IEnumerator WaitSpawner(Vector3 position, float seconds, Found<SpawnSystem> found)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            found.Value = OwnedSpawner(position);
            if (found.Value != null)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
    }

    // ---------- land ----------

    private struct Land
    {
        internal int Cell;
        internal Vector3 Point;
    }

    // Dry Deep North cells awake at a coverage, nearest the world centre first (seed point = where a player stands).
    private static List<Land> AwakeLand(float coverage)
    {
        var list = new List<Land>();
        var wg = WorldGenerator.instance;
        if (wg == null)
        {
            return list;
        }
        var seed = WorldState.Seed;
        for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
        {
            for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
            {
                if (!Cells.IsAwake(seed, i, j, coverage))
                {
                    continue;
                }
                Cells.SeedPoint(seed, i, j, out var x, out var z);
                if (wg.GetBiome(x, z) != Heightmap.Biome.DeepNorth || wg.GetHeight(x, z) < 40f)
                {
                    continue;
                }
                list.Add(new Land { Cell = Cells.Id(i, j), Point = new Vector3(x, wg.GetHeight(x, z), z) });
            }
        }
        list.Sort((a, b) => (a.Point.x * a.Point.x + a.Point.z * a.Point.z).CompareTo(b.Point.x * b.Point.x + b.Point.z * b.Point.z));
        return list;
    }

    // Coverages of the design (tests force design rules, never the player's config).
    private static float DesignCoverage(int stage) => AwakeningRules.Default.Coverage(stage);

    // The area of a test: slot 0 = the one dn.area uses, 1.. = the next invaded dry areas of stage 3.
    private static bool LandSlot(int slot, out Vector3 point, out int cell)
    {
        point = Vector3.zero;
        cell = HeldCells.NoTag;
        var list = AwakeLand(DesignCoverage(3));
        if (list.Count == 0)
        {
            return false;
        }
        var pick = list[slot % list.Count];
        point = pick.Point;
        cell = pick.Cell;
        return true;
    }

    // A dry area invaded at stage 1 already, not one of the slots the other tests use.
    private static bool Stage1Land(out Vector3 point, out int cell)
    {
        point = Vector3.zero;
        cell = HeldCells.NoTag;
        var taken = new HashSet<int>();
        var slots = AwakeLand(DesignCoverage(3));
        for (var k = 0; k < slots.Count && k < SlotsUsed; k++)
        {
            taken.Add(slots[k].Cell);
        }
        var early = AwakeLand(DesignCoverage(1));
        foreach (var land in early)
        {
            if (!taken.Contains(land.Cell))
            {
                point = land.Point;
                cell = land.Cell;
                return true;
            }
        }
        if (early.Count == 0)
        {
            return false;
        }
        point = early[0].Point;
        cell = early[0].Cell;
        return true;
    }

    // A dry Deep North area still asleep at stage 1 (invaded later), with no stage 1 area within reach of the spawn ring
    // around its seed point, and not a slot of another test.
    private static bool QuietLand(out Vector3 point, out int cell)
    {
        point = Vector3.zero;
        cell = HeldCells.NoTag;
        var seed = WorldState.Seed;
        var early = DesignCoverage(1);
        var slots = AwakeLand(DesignCoverage(3));
        for (var k = SlotsUsed; k < slots.Count; k++)
        {
            var land = slots[k];
            if (Cells.IsAwake(seed, land.Cell, early))
            {
                continue;
            }
            var quiet = true;
            for (var ring = 80f; ring <= 240f && quiet; ring += 80f)
            {
                for (var a = 0; a < 360 && quiet; a += 30)
                {
                    var px = land.Point.x + Mathf.Sin(a * Mathf.Deg2Rad) * ring;
                    var pz = land.Point.z + Mathf.Cos(a * Mathf.Deg2Rad) * ring;
                    quiet = !Cells.IsAwake(seed, Cells.At(seed, px, pz), early);
                }
            }
            if (quiet)
            {
                point = land.Point;
                cell = land.Cell;
                return true;
            }
        }
        return false;
    }

    // Trip home after a travelling test (always the last step; its result is a check of the test).
    private static IEnumerator GoHome(Checks c, Player player, Vector3 home)
    {
        var back = new Box();
        yield return TeleportAndWait(player, home, TravelTimeout, back);
        c.Check(back.Ok, "travelled back to the start");
    }

    // This mod as the framework sees it.
    private static bool ModActive()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null && view.Value.IsActive;
    }
}
#endif
