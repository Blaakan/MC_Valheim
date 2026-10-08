#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Later single-player self tests that stay at the spawn (tools in SelfTestsTools.cs):
//   dn.breaks      stones 1-5 broken by damage (the game's own break path): banner once each, real key, log lines, no
//                  invasion for 1 and 2, 3 invasions with their map markers outside the Deep North at 3, one more at 4
//                  when one stopped, none past the cap at 5; awake shares 20 / 40 / 70 % read from the console command;
//                  star chance of the stage on the Krigen entry
//   dn.stars       star rolls of the area entries through the game's own Spawn: shares of 1 and 2 star Krigen per
//                  stage, world modifier "enemy level-up rate" doubles the chance, world level raises it, Hexen and
//                  Elaking never above 1 star
//   dn.command     console command deepnorth_stones: shows, sets (no banner, no invasion), refuses junk
//   dn.request     an admin's invasion request (what pevents start does) runs about 5 s later, also right after a
//                  broken stone
//   dn.mapdetail   map paint: fill and border colours, no border between two invaded areas, sea untouched, minimap
//                  and large map share the texture under the fog, fewer areas at stage 1, none at 0
//   dn.bug.killtarget  real bug, own small test: kill target lowered to or below an area's kills must clear it without
//                  another kill (live and at load); today it stays "9 of 8" until the next Jotun dies
//   dn.x.kills     cross-mod: a player's kill of an area Krigen counts under Krigen in Creature Kill and Tame Counts
//   dn.cleanlog    no error line of this mod since it was turned on (registered last)
internal static partial class SelfTests
{
    // ---------- dn.breaks ----------

    private static IEnumerator RunBreaks()
    {
        var c = new Checks(BreaksName);
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var map = Minimap.instance;
        var player = Player.m_localPlayer;
        var saved = SaveStonesKey();
        var events = pes != null ? new List<PersistentEventSystem.ActivePersistentEvent>(pes.m_activePersistentEvents.list) : null;
        var stones = new List<GameObject>();
        var hud = new HudLog();
        var tap = EnsureTap();
        try
        {
            if (!c.Check(index >= 0 && map != null && MessageHud.instance != null && global::Console.instance != null,
                    "jotun_invasion, the map, the message HUD and the console exist"))
            {
                c.Report();
                yield break;
            }
            hud.Begin();
            pes.m_activePersistentEvents.list.Clear();
            pes.UpdateClientEventsList(0L);
            ServerRules.TestRules = Rules(null);
            // The test force the default (never read the player's config): the default itself must be 3.
            c.Check(Plugin.InvasionsAtThirdStone != null && Equals(Plugin.InvasionsAtThirdStone.DefaultValue, 3),
                $"the default of InvasionsAtThirdStone is 3 ({(Plugin.InvasionsAtThirdStone != null ? Plugin.InvasionsAtThirdStone.DefaultValue : null)})");
            Stones.TestInvasionsAtThirdStone = 3;
            WorldState.WriteStones(0);
            // Map markers of the events just removed go at the map's next look (once a second).
            yield return new WaitForSeconds(1.3f);
            var advance = Localize(Stones.StoneText);
            var fwd = Flat(player.transform.forward).normalized;
            if (fwd == Vector3.zero)
            {
                fwd = Vector3.forward;
            }
            var side = Vector3.Cross(Vector3.up, fwd);
            var basePos = player.transform.position + fwd * 9f;
            var box = new Box();
            var rules = ServerRules.Current;
            var seed = WorldState.Seed;

            // Stone 1 (T02): banner once, count 1, no invasion, about 20 % awake.
            var mark = tap.Mark();
            yield return BreakCounted(basePos - side * 6f, stones, 1, box);
            c.Check(box.Ok && RealKeyIs(1), $"stone 1 broken by damage and counted: the world keeps mc_dn_stones 1 ({WorldState.Stones})");
            c.Check(tap.Count(mark, "A Malicious Ice was broken at", ": 1 broken in this world, Deep North stage 1.") == 1,
                "stone 1: log line \"A Malicious Ice was broken at ...: 1 broken in this world, Deep North stage 1.\" once");
            c.Check(Stones.ActiveInvasions(pes, index) == 0 && Stones.HeldCount == 0,
                $"stone 1: no invasion, no request waiting (active {Stones.ActiveInvasions(pes, index)}, held {Stones.HeldCount})");
            yield return new WaitForSeconds(1.5f);
            c.Check(hud.Count(advance) == 1 && CenterText() == advance,
                $"stone 1: \"{advance}\" shown in the centre, once ({hud.Count(advance)} time(s), centre text '{CenterText()}')");
            c.Check(map.m_persistentEventPins.Count == 0 && Stones.ActiveInvasions(pes, index) == 0,
                $"stone 1: still no invasion and no invasion marker on the map 1.5 s later ({map.m_persistentEventPins.Count} markers)");
            var line = First(RunConsole(AdminCommand.Name));
            c.Check(ReadOf(line, AreasAwake, out var a1, out var t1) && t1 > 0 && Mathf.Abs((float)a1 / t1 - 0.2f) <= 0.08f,
                $"stone 1: deepnorth_stones shows about 20 % of the areas awake ({a1} of {t1}; '{line}')");

            // Stone 2 (T04): banner, no invasion, about 40 % awake, stage 1 areas kept, stage 2 star chance.
            mark = tap.Mark();
            yield return BreakCounted(basePos - side * 3f, stones, 2, box);
            c.Check(box.Ok && RealKeyIs(2), $"stone 2 broken by damage and counted: mc_dn_stones 2 ({WorldState.Stones})");
            c.Check(tap.Count(mark, ": 2 broken in this world, Deep North stage 2.") == 1, "stone 2: its log line, once");
            yield return new WaitForSeconds(1.5f);
            c.Check(hud.Count(advance) == 2, $"stone 2: \"{advance}\" shown once more ({hud.Count(advance)} in all)");
            c.Check(Stones.ActiveInvasions(pes, index) == 0 && Stones.HeldCount == 0 && map.m_persistentEventPins.Count == 0,
                $"stone 2: still no invasion, no request, no marker (active {Stones.ActiveInvasions(pes, index)}, held {Stones.HeldCount})");
            line = First(RunConsole(AdminCommand.Name));
            c.Check(ReadOf(line, AreasAwake, out var a2, out var t2) && t2 > 0 && Mathf.Abs((float)a2 / t2 - 0.4f) <= 0.08f && a2 >= a1,
                $"stone 2: deepnorth_stones shows about 40 % of the areas awake ({a2} of {t2})");
            var awakeAt1 = 0;
            var asleepAgain = 0;
            for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
            {
                for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
                {
                    if (!Cells.IsAwake(seed, i, j, rules.Coverage(1)))
                    {
                        continue;
                    }
                    awakeAt1++;
                    if (!Cells.IsAwake(seed, i, j, rules.Coverage(2)) || !Cells.IsAwake(seed, i, j, rules.Coverage(3)))
                    {
                        asleepAgain++;
                    }
                }
            }
            c.Check(awakeAt1 > 0 && asleepAgain == 0,
                $"the {awakeAt1} areas invaded at stage 1 in this world are all still invaded at stages 2 and 3 ({asleepAgain} are not)");
            AreaSpawns.TestInvalidate();
            AreaSpawns.TestBuild();
            var entry = AreaSpawns.TestEntry(Hostility.Krigen);
            c.Check(entry != null && Mathf.Approximately(entry.m_overrideLevelupChance, 25f) && entry.m_maxLevel == 3,
                $"stage 2: the Krigen of the areas roll stars at 25 % ({(entry != null ? F(entry.m_overrideLevelupChance) : "no entry")})");

            // Stone 3 (T05): banner, 3 invasions outside the Deep North with their map markers, about 70 % awake.
            mark = tap.Mark();
            yield return BreakCounted(basePos, stones, 3, box);
            c.Check(box.Ok && RealKeyIs(3), $"stone 3 broken by damage and counted: mc_dn_stones 3 ({WorldState.Stones})");
            c.Check(Stones.ActiveInvasions(pes, index) == 3,
                $"stone 3: 3 jotun_invasion run ({Stones.ActiveInvasions(pes, index)}; the game allows {pes.m_possibleEvents[index].maxConcurrent})");
            c.Check(tap.Count(mark, "Started 3 Jotun invasion(s) in the world.") == 1, "stone 3: log line \"Started 3 Jotun invasion(s) in the world.\" once");
            yield return new WaitForSeconds(1.6f);
            c.Check(hud.Count(advance) == 3, $"stone 3: \"{advance}\" shown once more ({hud.Count(advance)} in all)");
            var markersOk = map.m_persistentEventPins.Count == 3;
            var outside = 0;
            foreach (var e in pes.m_activePersistentEvents.list)
            {
                if (WorldGenerator.instance.GetBiome(e.position) != Heightmap.Biome.DeepNorth
                    && !WorldGenerator.IsDeepnorth(e.position.x, e.position.z))
                {
                    outside++;
                }
                markersOk &= Stones.IsInvasionName(e.internalName)
                             && map.m_persistentEventPins.TryGetValue(e.eventId, out var pair)
                             && pair.Item1.m_type == Minimap.PinType.EventArea && pair.Item2.m_type == Minimap.PinType.RandomEvent
                             && Flat(pair.Item2.m_pos - e.position).magnitude < 1f;
            }
            c.Check(markersOk, $"stone 3: the map has one area circle and one marker for each of the 3 invasions ({map.m_persistentEventPins.Count} marked)");
            c.Check(outside == pes.m_activePersistentEvents.list.Count && outside == 3, $"stone 3: every invasion is outside the Deep North ({outside} of 3)");
            line = First(RunConsole(AdminCommand.Name));
            c.Check(ReadOf(line, AreasAwake, out var a3, out var t3) && t3 > 0 && Mathf.Abs((float)a3 / t3 - 0.7f) <= 0.08f && a3 >= a2,
                $"stone 3: deepnorth_stones shows about 70 % of the areas awake ({a3} of {t3})");
            AreaSpawns.TestInvalidate();
            AreaSpawns.TestBuild();
            entry = AreaSpawns.TestEntry(Hostility.Krigen);
            c.Check(entry != null && Mathf.Approximately(entry.m_overrideLevelupChance, 45f) && entry.m_maxLevel == 3,
                $"stage 3: the Krigen of the areas roll stars at 45 %, up to 2 stars ({(entry != null ? F(entry.m_overrideLevelupChance) : "no entry")})");

            // Stone 4 (T06) with fewer than 3 running: one more.
            pes.m_activePersistentEvents.list.RemoveAt(0);
            pes.UpdateClientEventsList(0L);
            c.Check(Stones.ActiveInvasions(pes, index) == 2, "one invasion stopped: 2 running");
            Stones.TestLastRequested = -1;
            Stones.TestLastStarted = -1;
            yield return BreakCounted(basePos + side * 3f, stones, 4, box);
            c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == 3 && Stones.TestLastRequested == 1 && Stones.TestLastStarted == 1,
                $"stone 4 with 2 running: one more invasion (now {Stones.ActiveInvasions(pes, index)}, asked {Stones.TestLastRequested}, started {Stones.TestLastStarted})");

            // Stone 5 with 3 running: banner, no fourth invasion.
            mark = tap.Mark();
            Stones.TestLastRequested = -1;
            Stones.TestLastStarted = -1;
            yield return BreakCounted(basePos + side * 6f, stones, 5, box);
            c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == 3 && Stones.TestLastRequested == 1 && Stones.TestLastStarted == 0,
                $"stone 5 with 3 running: no fourth invasion (now {Stones.ActiveInvasions(pes, index)}, asked {Stones.TestLastRequested}, started {Stones.TestLastStarted})");
            c.Check(tap.Count(mark, "Started 0 of 1 Jotun invasion(s)") == 1, "stone 5: log line \"Started 0 of 1 Jotun invasion(s) ...\" once");
            yield return new WaitForSeconds(1.5f);
            c.Check(hud.Count(advance) == 5, $"stones 4 and 5: \"{advance}\" shown for each ({hud.Count(advance)} in all)");
            c.Check(map.m_persistentEventPins.Count == 3, $"the map still marks 3 invasions ({map.m_persistentEventPins.Count})");
        }
        finally
        {
            foreach (var go in stones)
            {
                KillStone(go);
            }
            RestoreEvents(pes, events);
            RestoreStonesKey(saved);
            hud.End();
            ClearOverrides();
        }
        c.Note("the test invasions left their ice and Fimbul locations far from the spawn (events removed)");
        c.Report();
    }

    // ---------- dn.stars ----------

    // Levels of creatures the game's own Spawn makes with an area entry at a place (the entry's star rolls there), each
    // gone again in the same frame. levels[0] = how many spawned, levels[1..3] = how many at level 1, 2, 3.
    private static IEnumerator SampleLevels(SpawnSystem ss, SpawnSystem.SpawnData entry, Vector3 at, int count, int[] levels)
    {
        var all = Character.GetAllCharacters();
        var done = 0;
        while (done < count)
        {
            for (var k = 0; k < 15 && done < count; k++)
            {
                done++;
                var before = all.Count;
                ss.Spawn(entry, at, false);
                if (all.Count <= before)
                {
                    continue;
                }
                var ch = all[all.Count - 1];
                levels[0]++;
                levels[Mathf.Clamp(ch.GetLevel(), 1, levels.Length - 1)]++;
                ZNetScene.instance.Destroy(ch.gameObject);
            }
            yield return null;
        }
    }

    // Share within what chance p allows over n rolls (4 sigma, never tighter than 6 points).
    private static bool Near(float share, float p, int n)
    {
        var sigma = Mathf.Sqrt(Mathf.Max(0f, p * (1f - p)) / Mathf.Max(1, n));
        return Mathf.Abs(share - p) <= Mathf.Max(0.06f, 4f * sigma);
    }

    private static IEnumerator RunStars()
    {
        var c = new Checks(StarsName);
        var zs = ZoneSystem.instance;
        var player = Player.m_localPlayer;
        var hadRate = false;
        string savedRate = null;
        var rateSet = false;
        try
        {
            ServerRules.TestRules = Rules(null);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            var ss = OwnedSpawner(player.transform.position);
            if (ss == null && SpawnSystem.m_instances.Count > 0)
            {
                ss = SpawnSystem.m_instances[0];
            }
            if (!c.Check(ss != null && LandSlot(SlotStars, out _, out _), "a spawner and a dry invaded Deep North place exist"))
            {
                c.Report();
                yield break;
            }
            LandSlot(SlotStars, out var point, out _);
            var at = point + Vector3.up * 2f;
            const int n = 150;
            var starred = new int[4];
            var oneStar = new int[4];
            var share3 = 0f;
            var eff3 = 0f;
            SpawnSystem.SpawnData krigen = null;
            for (var stage = 1; stage <= 3; stage++)
            {
                WorldState.TestStones = stage;
                WorldState.Refresh();
                AreaSpawns.TestInvalidate();
                AreaSpawns.TestBuild();
                krigen = AreaSpawns.TestEntry(Hostility.Krigen);
                var want = ServerRules.Current.StarChance(stage);
                if (!c.Check(krigen != null && Mathf.Approximately(krigen.m_overrideLevelupChance, want) && krigen.m_maxLevel == 3,
                        $"stage {stage}: Krigen entry star chance {(krigen != null ? F(krigen.m_overrideLevelupChance) : "none")} % (setting {F(want)}), up to 2 stars"))
                {
                    continue;
                }
                var eff = SpawnSystem.GetLevelUpChance(at, krigen);
                var p = Mathf.Clamp01(eff / 100f);
                var levels = new int[4];
                yield return SampleLevels(ss, krigen, at, n, levels);
                var got = levels[0];
                var star = got > 0 ? (float)(levels[2] + levels[3]) / got : 0f;
                var one = got > 0 ? (float)levels[2] / got : 0f;
                var two = got > 0 ? (float)levels[3] / got : 0f;
                starred[stage] = levels[2] + levels[3];
                oneStar[stage] = levels[2];
                c.Note($"stage {stage}: {got} Krigen spawned by the game with the area entry at {F(at)}: {levels[1]} without star, "
                       + $"{levels[2]} with 1 star, {levels[3]} with 2 stars; effective chance per roll {F(eff)} %");
                c.Check(got == n && Near(star, p, got), $"stage {stage}: {F(star * 100f)} % of the Krigen have a star (chance {F(eff)} %)");
                if (stage == 2)
                {
                    c.Check(Near(one, p * (1f - p), got) && one >= 0.08f && one <= 0.32f,
                        $"stage 2: 1-star Krigen are roughly one in five ({F(one * 100f)} %)");
                }
                if (stage == 3)
                {
                    share3 = star;
                    eff3 = eff;
                    c.Check(levels[3] > 0 && Near(two, p * p, got) && two >= 0.08f && two <= 0.34f,
                        $"stage 3: 2-star Krigen show up, roughly one in five ({F(two * 100f)} %)");
                }
            }
            c.Check(starred[2] > starred[1] && starred[3] > starred[2],
                $"more starred Krigen with each stage: {starred[1]}, {starred[2]}, {starred[3]} of {n}");
            c.Check(oneStar[2] > oneStar[1], $"more 1-star Krigen at stage 2 than at stage 1: {oneStar[2]} against {oneStar[1]} of {n}");

            // World modifier "Enemy level-up rate" (the game's own key, put back in finally): chance doubles.
            hadRate = zs.GetGlobalKey(GlobalKeys.EnemyLevelUpRate, out savedRate);
            zs.SetGlobalKey(GlobalKeys.EnemyLevelUpRate, 200f);
            rateSet = true;
            yield return null;
            var effRate = krigen != null ? SpawnSystem.GetLevelUpChance(at, krigen) : 0f;
            c.Check(Mathf.Approximately(Game.m_enemyLevelUpRate, 2f) && Mathf.Abs(effRate - 2f * eff3) < 0.01f && effRate > eff3,
                $"enemy level-up rate 200 %: the area Krigen roll stars at {F(effRate)} % instead of {F(eff3)} %");
            if (krigen != null)
            {
                var levels = new int[4];
                yield return SampleLevels(ss, krigen, at, 100, levels);
                var star = levels[0] > 0 ? (float)(levels[2] + levels[3]) / levels[0] : 0f;
                c.Check(levels[0] == 100 && star - share3 >= 0.2f,
                    $"enemy level-up rate 200 %: clearly more starred Krigen ({F(star * 100f)} % against {F(share3 * 100f)} % with the default modifiers)");
            }
            foreach (var name in new[] { Hostility.Hexen, Hostility.Elaking })
            {
                var entry = AreaSpawns.TestEntry(name);
                if (!c.Check(entry != null && entry.m_maxLevel == 2, $"{name} entry: at most 1 star (max level {(entry != null ? entry.m_maxLevel : 0)})"))
                {
                    continue;
                }
                var levels = new int[4];
                yield return SampleLevels(ss, entry, at, 60, levels);
                c.Check(levels[0] == 60 && levels[3] == 0 && levels[2] > 0,
                    $"{name} with the raised modifier: {levels[2]} of {levels[0]} with 1 star, {levels[3]} above 1 star");
            }
            if (hadRate)
            {
                zs.SetGlobalKey(GlobalKeys.EnemyLevelUpRate + " " + savedRate);
            }
            else
            {
                zs.RemoveGlobalKey(GlobalKeys.EnemyLevelUpRate);
            }
            rateSet = false;
            yield return null;
            c.Check(Mathf.Approximately(Game.m_enemyLevelUpRate, hadRate ? Game.m_enemyLevelUpRate : 1f),
                $"enemy level-up rate put back ({F(Game.m_enemyLevelUpRate)})");

            // World level: set, read, put back in one go (nothing else sees it).
            if (krigen != null)
            {
                var hadLevel = zs.GetGlobalKey(GlobalKeys.WorldLevel, out string savedLevel);
                zs.SetGlobalKey(GlobalKeys.WorldLevel, 1f);
                var level = Game.m_worldLevel;
                var effLevel = SpawnSystem.GetLevelUpChance(at, krigen);
                if (hadLevel)
                {
                    zs.SetGlobalKey(GlobalKeys.WorldLevel + " " + savedLevel);
                }
                else
                {
                    zs.RemoveGlobalKey(GlobalKeys.WorldLevel);
                }
                c.Check(level == 1 && effLevel > eff3,
                    $"world level 1: the area Krigen roll stars at {F(effLevel)} % instead of {F(eff3)} % (world level now {Game.m_worldLevel})");
            }
        }
        finally
        {
            if (rateSet && zs != null)
            {
                if (hadRate)
                {
                    zs.SetGlobalKey(GlobalKeys.EnemyLevelUpRate + " " + savedRate);
                }
                else
                {
                    zs.RemoveGlobalKey(GlobalKeys.EnemyLevelUpRate);
                }
            }
            ClearOverrides();
        }
        c.Report();
    }

    // ---------- dn.command ----------

    private static IEnumerator RunCommand()
    {
        var c = new Checks(CommandName);
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var map = Minimap.instance;
        var saved = SaveStonesKey();
        var hud = new HudLog();
        try
        {
            if (!c.Check(global::Console.instance != null && Terminal.commands.ContainsKey(AdminCommand.Name) && map != null,
                    "the console knows deepnorth_stones"))
            {
                c.Report();
                yield break;
            }
            hud.Begin();
            ServerRules.TestRules = Rules(null);
            var invasions = Stones.ActiveInvasions(pes, index);
            var markers = map.m_persistentEventPins.Count;
            var advance = Localize(Stones.StoneText);
            Stones.TestLastRequested = -1;
            WorldState.WriteStones(1);
            yield return null;

            var lines = RunConsole(AdminCommand.Name);
            c.Check(lines.Count == 1 && First(lines).StartsWith("Deep North: 1 Malicious Ice broken, stage 1, ", StringComparison.Ordinal)
                    && First(lines).Contains(AreasAwake) && First(lines).Contains("Kall not defeated"),
                $"deepnorth_stones shows the count, the stage and the awake areas: '{First(lines)}'");

            lines = RunConsole(AdminCommand.Name + " 2");
            c.Check(lines.Count == 1 && First(lines).StartsWith("Deep North set: 2 Malicious Ice broken, stage 2", StringComparison.Ordinal) && RealKeyIs(2),
                $"deepnorth_stones 2 sets the count: '{First(lines)}', mc_dn_stones {WorldState.Stones}");

            lines = RunConsole(AdminCommand.Name + " 3");
            c.Check(First(lines).StartsWith("Deep North set: 3 Malicious Ice broken, stage 3", StringComparison.Ordinal) && RealKeyIs(3)
                    && Stones.ActiveInvasions(pes, index) == invasions && Stones.TestLastRequested == -1,
                $"deepnorth_stones 3 sets stage 3 and starts no invasion (running {Stones.ActiveInvasions(pes, index)}, was {invasions})");

            lines = RunConsole(AdminCommand.Name + " 7");
            c.Check(First(lines).StartsWith("Deep North set: 7 Malicious Ice broken, stage 3", StringComparison.Ordinal) && RealKeyIs(7),
                $"deepnorth_stones 7: 7 broken is still stage 3: '{First(lines)}'");

            lines = RunConsole(AdminCommand.Name + " 0");
            c.Check(First(lines).StartsWith("Deep North set: 0 Malicious Ice broken, stage 0", StringComparison.Ordinal) && RealKeyIs(0),
                $"deepnorth_stones 0 puts the north back to sleep: '{First(lines)}'");
            lines = RunConsole(AdminCommand.Name);
            c.Check(First(lines).StartsWith("Deep North: 0 Malicious Ice broken, stage 0, Kall not defeated", StringComparison.Ordinal),
                $"deepnorth_stones then shows 0 broken, stage 0: '{First(lines)}'");

            var refused = 0;
            foreach (var bad in new[] { "101", "-1", "x", "1.5" })
            {
                lines = RunConsole(AdminCommand.Name + " " + bad);
                if (First(lines).StartsWith("Syntax: " + AdminCommand.Name + " [count]", StringComparison.Ordinal) && RealKeyIs(0))
                {
                    refused++;
                }
            }
            c.Check(refused == 4, $"a count that is not a whole number from 0 to 100 is refused and changes nothing ({refused} of 4)");

            yield return new WaitForSeconds(1.5f);
            c.Check(hud.Count(advance) == 0, $"setting the count shows no \"{advance}\" ({hud.Count(advance)})");
            c.Check(Stones.ActiveInvasions(pes, index) == invasions && Stones.HeldCount == 0 && map.m_persistentEventPins.Count == markers
                    && Stones.TestLastRequested == -1,
                $"setting the count starts no invasion (running {Stones.ActiveInvasions(pes, index)}, held {Stones.HeldCount})");
        }
        finally
        {
            RestoreStonesKey(saved);
            hud.End();
            ClearOverrides();
        }
        c.Report();
    }

    // ---------- dn.request ----------

    private static IEnumerator RunRequest()
    {
        var c = new Checks(RequestName);
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var player = Player.m_localPlayer;
        var saved = SaveStonesKey();
        var events = pes != null ? new List<PersistentEventSystem.ActivePersistentEvent>(pes.m_activePersistentEvents.list) : null;
        var stones = new List<GameObject>();
        try
        {
            if (!c.Check(index >= 0, "jotun_invasion exists"))
            {
                c.Report();
                yield break;
            }
            pes.m_activePersistentEvents.list.Clear();
            pes.UpdateClientEventsList(0L);
            ServerRules.TestRules = Rules(null);
            WorldState.WriteStones(0);
            yield return null;

            // What the console command "pevents start jotun_invasion" does (the command itself is a cheat command:
            // running it would mark this character as a cheater for the other mods' tests).
            var t0 = Time.unscaledTime;
            pes.TriggerEvent(Stones.InvasionEvent);
            c.Check(Stones.HeldCount == 1 && Stones.ActiveInvasions(pes, index) == 0, "a request with nothing broken around: it waits, no invasion yet");
            yield return new WaitForSecondsRealtime(3.5f);
            c.Check(Stones.HeldCount == 1 && Stones.ActiveInvasions(pes, index) == 0, "3.5 s later it still waits");
            yield return Until(() => Stones.ActiveInvasions(pes, index) == 1, 4f);
            var after = Time.unscaledTime - t0;
            c.Check(Stones.ActiveInvasions(pes, index) == 1 && Stones.HeldCount == 0 && after >= 4.5f && after <= 7f,
                $"the invasion starts about 5 s after the request ({F(after)} s, {Stones.ActiveInvasions(pes, index)} running)");

            // Right after a broken Malicious Ice (stone 1 starts nothing itself).
            var fwd = Flat(player.transform.forward).normalized;
            var box = new Box();
            yield return BreakCounted(player.transform.position + (fwd == Vector3.zero ? Vector3.forward : fwd) * 9f, stones, 1, box);
            c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == 1, $"a stone broken by damage: counted, no invasion of its own ({WorldState.Stones})");
            t0 = Time.unscaledTime;
            pes.TriggerEvent(Stones.InvasionEvent);
            c.Check(Stones.HeldCount == 1, "a request right after the break: it waits");
            yield return Until(() => Stones.ActiveInvasions(pes, index) == 2, 8f);
            after = Time.unscaledTime - t0;
            c.Check(Stones.ActiveInvasions(pes, index) == 2 && Stones.HeldCount == 0 && after >= 4.5f && after <= 7f && RealKeyIs(1),
                $"it starts about 5 s later too ({F(after)} s, {Stones.ActiveInvasions(pes, index)} running, {WorldState.Stones} broken)");
        }
        finally
        {
            foreach (var go in stones)
            {
                KillStone(go);
            }
            RestoreEvents(pes, events);
            RestoreStonesKey(saved);
            ClearOverrides();
        }
        c.Note("the two test invasions left their ice and Fimbul locations far from the spawn (events removed)");
        c.Report();
    }

    // ---------- dn.mapdetail ----------

    private static bool SameColor(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    private static Color32 Blend(Color32 own, Color32 tint, float mix) =>
        new Color32(MapOverlay.Mix(own.r, tint.r, mix), MapOverlay.Mix(own.g, tint.g, mix), MapOverlay.Mix(own.b, tint.b, mix), 255);

    private static string Rgb(Color32 c) => $"{c.r},{c.g},{c.b}";

    private static IEnumerator RunMapDetail()
    {
        var c = new Checks(MapDetailName);
        try
        {
            yield return Until(() => MapOverlay.Ready, 25f);
            var map = Minimap.instance;
            if (!c.Check(MapOverlay.Ready && map != null, "the map was scanned for Deep North land"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = Rules(null);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            yield return Frames(3);
            var coverage = ServerRules.Current.Coverage(3);
            var land3 = MapOverlay.TestChangedPixels(true);
            var all3 = MapOverlay.TestChangedPixels(false);
            c.Check(MapOverlay.Tinted && land3 > 0 && all3 == land3,
                $"stage 3: {land3} land pixels are tinted, no sea pixel is ({all3 - land3} changed off the land)");

            // Inside an invaded area: the purple fill over the map's own colour.
            if (c.Check(MapOverlay.TestFind(coverage, true, out var fill) && MapOverlay.TestRawColor(fill, out var fillNow)
                        && MapOverlay.TestOwnColor(fill, out var fillOwn), "found a pixel inside an invaded area"))
            {
                MapOverlay.TestRawColor(fill, out fillNow);
                MapOverlay.TestOwnColor(fill, out fillOwn);
                var want = Blend(fillOwn, MapOverlay.FillColor, MapOverlay.FillMix);
                c.Check(SameColor(fillNow, want) && !SameColor(fillNow, fillOwn),
                    $"inside an invaded area the map is purple over its own colour ({Rgb(fillNow)}, expected {Rgb(want)}, own {Rgb(fillOwn)})");
            }
            // Border: next to land of an area that is not invaded.
            if (c.Check(MapOverlay.TestFindSpot(coverage, MapOverlay.TestSpotEdge, out var edge), "found a pixel on the border of the invaded land"))
            {
                MapOverlay.TestRawColor(edge, out var now);
                MapOverlay.TestOwnColor(edge, out var own);
                var want = Blend(own, MapOverlay.EdgeColor, MapOverlay.EdgeMix);
                var darker = MapOverlay.EdgeColor.r + MapOverlay.EdgeColor.g + MapOverlay.EdgeColor.b
                             < MapOverlay.FillColor.r + MapOverlay.FillColor.g + MapOverlay.FillColor.b;
                c.Check(SameColor(now, want) && darker,
                    $"the border of the invaded land is the darker purple ({Rgb(now)}, expected {Rgb(want)}, own {Rgb(own)})");
            }
            // Two invaded areas that touch: one region, no border between them.
            if (c.Check(MapOverlay.TestFindSpot(coverage, MapOverlay.TestSpotSeam, out var seam), "found a pixel where two invaded areas touch"))
            {
                MapOverlay.TestRawColor(seam, out var now);
                MapOverlay.TestOwnColor(seam, out var own);
                var want = Blend(own, MapOverlay.FillColor, MapOverlay.FillMix);
                c.Check(SameColor(now, want), $"where two invaded areas touch there is no border: one region ({Rgb(now)}, expected {Rgb(want)})");
            }
            // Sea inside an invaded area keeps its own colour.
            if (c.Check(MapOverlay.TestFindSpot(coverage, MapOverlay.TestSpotSea, out var sea), "found a sea pixel inside an invaded area"))
            {
                MapOverlay.TestRawColor(sea, out var now);
                MapOverlay.TestOwnColor(sea, out var own);
                c.Check(MapOverlay.TestPixel(sea) == 0 && SameColor(now, own), $"the sea of an invaded area keeps its own colour ({Rgb(now)}, own {Rgb(own)})");
            }

            // Minimap and large map draw the same texture, under the game's fog of war.
            var small = map.m_mapSmallShader;
            var large = map.m_mapLargeShader;
            c.Check(small != null && large != null && small.GetTexture("_MainTex") == map.m_mapTexture
                    && large.GetTexture("_MainTex") == map.m_mapTexture && map.m_mapImageSmall.material == small
                    && map.m_mapImageLarge.material == large,
                "the minimap and the large map draw the same tinted texture");
            c.Check(small != null && large != null && map.m_fogTexture != null && small.GetTexture("_FogTex") == map.m_fogTexture
                    && large.GetTexture("_FogTex") == map.m_fogTexture,
                "both draw the game's fog of war over it (places not explored stay hidden)");

            // Fewer areas at earlier stages, none while the north sleeps.
            WorldState.TestStones = 2;
            WorldState.Refresh();
            yield return Frames(3);
            var land2 = MapOverlay.TestChangedPixels(true);
            WorldState.TestStones = 1;
            WorldState.Refresh();
            yield return Frames(3);
            var land1 = MapOverlay.TestChangedPixels(true);
            WorldState.TestStones = 0;
            WorldState.Refresh();
            yield return Frames(3);
            var land0 = MapOverlay.TestChangedPixels(false);
            c.Check(land1 > 0 && land1 < land2 && land2 < land3, $"tinted land grows with the stage: {land1}, {land2}, {land3} pixels");
            c.Check(land0 == 0 && !MapOverlay.Tinted, $"stage 0: nothing tinted ({land0} pixels changed)");
        }
        finally
        {
            ClearOverrides();
        }
        yield return Frames(2);
        c.Report();
    }

    // ---------- dn.bug.killtarget ----------

    // Real bug, alone here (the run said "9 of 8 Jotun defeated, spawns its Jotun when entered"): the server lowers
    // ClearKillsMin / Max to or below the kills an area already has. Expected: cleared on the server's next look, no
    // further kill needed; never more kills shown than the target. Today only the next Jotun death compares kills and
    // target (HeldCells.OnDestroyed); a rules change and the load (ScanMarks) do not. Fails until the mod is fixed.
    private static IEnumerator RunKillTarget()
    {
        var c = new Checks(KillTargetName);
        var cell = HeldCells.NoTag;
        try
        {
            ServerRules.TestRules = Rules(r =>
            {
                r.ClearKillsMin = 12;
                r.ClearKillsMax = 12;
            });
            WorldState.TestStones = 3;
            WorldState.Refresh();
            if (!c.Check(LandSlot(SlotKillTarget, out var land, out cell), "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            HeldCells.TestSawNoKall(false);
            WorldState.TestKall = true;
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
            c.Check(HeldCells.Scanned && HeldCells.TestKnown && ServerRules.Current.KillTarget(WorldState.Seed, cell) == 12,
                "after Kall the area needs 12 kills (test rules)");

            // Live: 9 kills, then the settings change to 8.
            for (var k = 0; k < 9; k++)
            {
                FakeKill(Hostility.Army[k % Hostility.Army.Length], land);
            }
            yield return Until(() => HeldCells.TestKills(cell) >= 9, 4f);
            var counted = HeldCells.TestKills(cell) == 9 && !HeldCells.IsCleared(cell);
            ServerRules.TestRules = Rules(r =>
            {
                r.ClearKillsMin = 8;
                r.ClearKillsMax = 8;
            });
            // What a changed setting does (Plugin.OnSettingChanged).
            ServerRules.OwnChanged();
            WorldState.Refresh();
            yield return Until(() => HeldCells.IsCleared(cell), 3f);
            var text = HeldCells.DescribeArea(land, ServerRules.Current);
            c.Check(counted && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell) && HeldCells.TestKillSlots(cell) == 0,
                $"9 kills, then the target lowered to 8: the area is cleared without another kill (cleared {HeldCells.IsCleared(cell)}, "
                + $"{HeldCells.TestKills(cell)} kills kept, status '{text}')");
            c.Check(text.IndexOf("9 of 8", StringComparison.Ordinal) < 0 && text.Contains("is cleared"), $"deepnorth_stones says the area is cleared and never shows more kills than it needs ('{text}')");

            // At load: the world holds 9 kills, the server starts with the target at 8.
            HeldCells.TestUnmarkEverywhere(cell);
            ServerRules.TestRules = Rules(r =>
            {
                r.ClearKillsMin = 12;
                r.ClearKillsMax = 12;
            });
            HeldCells.TestRescan();
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown && !HeldCells.IsCleared(cell), 5f);
            for (var k = 0; k < 9; k++)
            {
                FakeKill(Hostility.Army[k % Hostility.Army.Length], land);
            }
            yield return Until(() => HeldCells.TestKills(cell) >= 9, 4f);
            counted = HeldCells.TestKills(cell) == 9 && !HeldCells.IsCleared(cell) && HeldCells.TestKillSlots(cell) == 1;
            ServerRules.TestRules = Rules(r =>
            {
                r.ClearKillsMin = 8;
                r.ClearKillsMax = 8;
            });
            HeldCells.TestRescan();
            yield return Until(() => HeldCells.Scanned && HeldCells.IsCleared(cell), 4f);
            text = HeldCells.DescribeArea(land, ServerRules.Current);
            c.Check(counted && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell),
                $"9 kills kept in the world, then a server start with the target at 8: the area is cleared at once (cleared "
                + $"{HeldCells.IsCleared(cell)}, {HeldCells.TestKills(cell)} kills read back, status '{text}')");
            c.Check(text.IndexOf("9 of 8", StringComparison.Ordinal) < 0 && text.Contains("is cleared"), $"deepnorth_stones says cleared, no \"9 of 8\", after the start too ('{text}')");
        }
        finally
        {
            if (cell != HeldCells.NoTag)
            {
                HeldCells.TestUnmarkEverywhere(cell);
            }
            ClearOverrides();
            HeldCells.TestRescan();
            HeldCells.TestClearEngaged();
        }
        c.Report();
    }

    // ---------- dn.x.kills ----------

    // Cross-mod (Creature Kill and Tame Counts): the number that mod shows for Krigen goes up by one when the player
    // kills a Krigen of an area. Read through that mod's own public door (reflection: no hard link to it).
    private static IEnumerator RunKillCount()
    {
        var c = new Checks(KillCountName);
        var player = Player.m_localPlayer;
        GameObject spawned = null;
        Dictionary<string, float>[] stats = null;
        string name = null;
        var had = new bool[2];
        var old = new float[2];
        var buckets = new[] { (int)KillModifiers.MixedAndTotal, (int)KillModifiers.Melee };
        try
        {
            var other = FeatureRegistry.Find(StatsModGuid);
            // That mod's assembly through its loaded plugin (BepInEx loads plugins by file: a lookup by name may miss it).
            Type api = null;
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(StatsModGuid, out var info) && info != null && info.Instance != null)
            {
                api = info.Instance.GetType().Assembly.GetType("MC.Exploration.StatsPerCreatureMod.CreatureCounts");
            }
            var getKills = api != null ? api.GetMethod("GetKills", new[] { typeof(string) }) : null;
            if (!c.Check(other != null && other.Value.IsActive && getKills != null,
                    "Creature Kill and Tame Counts is installed and active in this run"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = Rules(null);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            AreaSpawns.TestBuild();
            var entry = AreaSpawns.TestEntry(Hostility.Krigen);
            var ss = OwnedSpawner(player.transform.position);
            if (!c.Check(entry != null && ss != null && LandSlot(SlotOther, out _, out _), "the Krigen entry of the areas and a spawner here exist"))
            {
                c.Report();
                yield break;
            }
            LandSlot(SlotOther, out _, out var tagCell);
            var fwd = Flat(player.transform.forward).normalized;
            var at = player.transform.position + (fwd == Vector3.zero ? Vector3.forward : fwd) * 8f;
            if (ZoneSystem.instance.GetGroundHeight(at, out var ground))
            {
                at.y = ground + 0.5f;
            }
            // A Krigen made by the game's Spawn with the area's entry, tagged with an area like the area spawns tag theirs.
            var all = Character.GetAllCharacters();
            var before = all.Count;
            ss.Spawn(entry, at, false);
            var krigen = all.Count > before ? all[all.Count - 1] : null;
            if (!c.Check(krigen != null && ZdoOf(krigen) != null, "an area Krigen spawned next to the player"))
            {
                c.Report();
                yield break;
            }
            spawned = krigen.gameObject;
            ZdoOf(krigen).Set(HeldCells.TagKey, tagCell);
            name = krigen.m_name;
            stats = Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats;
            for (var b = 0; b < buckets.Length; b++)
            {
                had[b] = stats[buckets[b]].TryGetValue(name, out old[b]);
            }
            var killsBefore = (int)getKills.Invoke(null, new object[] { name });
            yield return Frames(3);
            Slay(krigen, player);
            yield return Until(() => krigen == null, 10f);
            yield return Until(() => (int)getKills.Invoke(null, new object[] { name }) > killsBefore, 3f);
            var killsAfter = (int)getKills.Invoke(null, new object[] { name });
            c.Check(krigen == null && killsAfter == killsBefore + 1,
                $"a player's kill of an area Krigen counts under Krigen ('{name}', '{Localize(name)}'): {killsBefore} -> {killsAfter}");
            c.Note($"counted under the creature's own name '{name}', shown as '{Localize(name)}'");
        }
        finally
        {
            // The kill numbers of this character as before (the two the kill changed).
            if (stats != null && name != null)
            {
                for (var b = 0; b < buckets.Length; b++)
                {
                    if (had[b])
                    {
                        stats[buckets[b]][name] = old[b];
                    }
                    else
                    {
                        stats[buckets[b]].Remove(name);
                    }
                }
            }
            Kill(spawned);
            ClearOverrides();
        }
        c.Report();
    }

    // ---------- dn.cleanlog ----------

    // Runs last: looks back on the whole session. Depends on everything that ran before (and on the other mods
    // installed): own small test.
    private static IEnumerator RunCleanLog()
    {
        var tap = EnsureTap();
        tap.Errors(out var errors, out var first);
        if (errors == 0)
        {
            SelfTest.Pass(CleanLogName, $"no error line from {ModInfo.Name} since it was turned on ({tap.Seen} of its log lines seen)");
        }
        else
        {
            SelfTest.Fail(CleanLogName, $"{errors} error line(s) from {ModInfo.Name} since it was turned on, first: {first}");
        }
        yield break;
    }
}
#endif
