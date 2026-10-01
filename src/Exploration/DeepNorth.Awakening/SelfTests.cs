using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod DeepNorth.Awakening). Design 7.5:
//   dn.logic      pure: cell ids, Voronoi vs brute force, coverage average over many seeds, stage nesting, storm uptime
//                 and lengths, stage/count parsing, invasions per stone, broken-Morkhalla count, army vs nature, held
//                 wire, spawn gate by stage / Kall / storm
//   dn.network    rules wire (round trip, clamp, unknown layout, cut-off), Select, push debounce, join verdicts
//   dn.vanilla    game data the mod lean on: jotun_invasion event, BlackIce_Start / BlackIce_Core triggers, prefabs,
//                 factions, Kall key, blizzard weather, Morkhalla location; NOTE vanilla invasion spawn entries
//   dn.hostility  army vs nature by IsEnemy (on / off / nature setting off), live: Krigen and Gammeltroll target each
//                 other
//   dn.stones     breaks of spawned BlackIce_Start: 1-2 no invasion, game waiting for rules = vanilla request held and
//                 dropped, 3 = 3 invasions, 4 = vanilla cap, admin request runs after the hold; world put back
//   dn.detect     old-world detection writes the count from the world's Morkhalla
//   dn.kall       after Kall: tagged Jotun hold its cell, dead = cleared, no new Jotun from the gate
//   dn.area       live in the Deep North at stage 3 (storms forced on): area Jotun spawn tagged in awake cells with
//                 stage stars, blizzard weather, meteors; screenshots; travel back
//   dn.map        map overlay: Deep North land scanned, awake cells tinted, sleeping ones not, off with the rule, the
//                 stage 0 north and pending rules
//   dn.morkhalla  live: travel to a Morkhalla, count its Malicious Ice, detection see it intact; travel back
// Me force state only with ServerRules.TestRules / TestPending and WorldState.TestStones / TestKall, never config.
// Real key mc_dn_stones and the persistent events list are put back after dn.stones and dn.detect.
internal static class SelfTests
{
    private const string LogicName = "dn.logic";
    private const string NetworkName = "dn.network";
    private const string VanillaName = "dn.vanilla";
    private const string HostilityName = "dn.hostility";
    private const string StonesName = "dn.stones";
    private const string DetectName = "dn.detect";
    private const string KallName = "dn.kall";
    private const string AreaName = "dn.area";
    private const string MorkhallaName = "dn.morkhalla";
    private const string MapName = "dn.map";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(LogicName, RunLogic);
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(VanillaName, RunVanilla);
        SelfTest.Register(HostilityName, RunHostility);
        SelfTest.Register(StonesName, RunStones);
        SelfTest.Register(DetectName, RunDetect);
        SelfTest.Register(KallName, RunKall);
        SelfTest.Register(MapName, RunMap);
        SelfTest.Register(AreaName, RunArea);
        SelfTest.Register(MorkhallaName, RunMorkhalla);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(LogicName);
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(VanillaName);
        SelfTest.Unregister(HostilityName);
        SelfTest.Unregister(StonesName);
        SelfTest.Unregister(DetectName);
        SelfTest.Unregister(KallName);
        SelfTest.Unregister(MapName);
        SelfTest.Unregister(AreaName);
        SelfTest.Unregister(MorkhallaName);
        ClearOverrides();
#endif
    }

#if DEBUG
    private const float TravelTimeout = 35f;

    // Where the travelling tests bring the player back (first one to start records it): a failed trip back never moves
    // "home" for the next test.
    private static Vector3? _home;

    // Area dn.area cleared and wrote in the world (unmarked after).
    private static int? _areaCleared;

    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        WorldState.TestStones = null;
        WorldState.TestKall = null;
        Stones.TestInvasionsAtThirdStone = null;
        WorldState.Refresh();
    }

    // ---------- helpers ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    private sealed class Box
    {
        internal bool Ok;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    // Design defaults (never the player's config), edited.
    private static AwakeningRules Rules(Action<AwakeningRules> edit)
    {
        var r = new AwakeningRules();
        edit?.Invoke(r);
        return r;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static string Localize(string token) =>
        Localization.instance != null ? Localization.instance.Localize(token) : token;

    private static bool KnownText(string token)
    {
        var s = Localize(token);
        return s != token && !s.StartsWith("[", StringComparison.Ordinal);
    }

    // Vanilla distant teleport (loading screen, hold until the area is there). Vanilla refuse a new one within 2 s of
    // the last: retry. (Copy of Sailing Skill's.)
    private static IEnumerator TeleportAndWait(Player player, Vector3 target, float seconds, Box result)
    {
        result.Ok = false;
        var until = Time.time + seconds;
        while (!player.TeleportTo(target, player.transform.rotation, true))
        {
            if (Time.time > until)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        while (player.IsTeleporting() && Time.time < until)
        {
            yield return new WaitForSeconds(0.25f);
        }
        result.Ok = !player.IsTeleporting() && Flat(player.transform.position - target).magnitude < 20f;
    }

    private static GameObject Spawn(string prefab, Vector3 at, Vector3 facing = default)
    {
        var p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
        if (p == null)
        {
            return null;
        }
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(at, out var h))
        {
            at.y = h + 0.5f;
        }
        var flat = Flat(facing);
        var rot = flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flat) : Quaternion.identity;
        return Object.Instantiate(p, at, rot);
    }

    private static void Kill(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var nview = go.GetComponent<ZNetView>();
        if (nview != null && nview.IsValid() && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
        else
        {
            Object.Destroy(go);
        }
    }

    private static int Prefab(Component c)
    {
        var nview = c != null ? c.GetComponent<ZNetView>() : null;
        var zdo = nview != null ? nview.GetZDO() : null;
        return zdo != null ? zdo.GetPrefab() : 0;
    }

    private static int TagOf(Component c)
    {
        var nview = c != null ? c.GetComponent<ZNetView>() : null;
        var zdo = nview != null ? nview.GetZDO() : null;
        return zdo != null ? zdo.GetInt(HeldCells.TagKey, HeldCells.NoTag) : HeldCells.NoTag;
    }

    // Saved real stone key: absent or value.
    private struct SavedKey
    {
        internal bool Had;
        internal string Value;
    }

    private static SavedKey SaveStonesKey()
    {
        var s = new SavedKey();
        var zs = ZoneSystem.instance;
        if (zs != null)
        {
            s.Had = zs.GetGlobalKey(WorldState.StonesKey, out s.Value);
        }
        return s;
    }

    private static void RestoreStonesKey(SavedKey s)
    {
        var zs = ZoneSystem.instance;
        if (zs == null)
        {
            return;
        }
        if (s.Had)
        {
            zs.SetGlobalKey(WorldState.StonesKey + " " + s.Value);
        }
        else
        {
            zs.RemoveGlobalKey(WorldState.StonesKey);
        }
        WorldState.Refresh();
    }

    // Awake Deep North cell (at coverage) whose seed point is dry land; the one nearest the world centre.
    private static bool FindAwakeLand(float coverage, out Vector3 point, out int cell)
    {
        point = Vector3.zero;
        cell = 0;
        var wg = WorldGenerator.instance;
        if (wg == null)
        {
            return false;
        }
        var seed = WorldState.Seed;
        var best = float.MaxValue;
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
                var d = x * x + z * z;
                if (d < best)
                {
                    best = d;
                    point = new Vector3(x, wg.GetHeight(x, z), z);
                    cell = Cells.Id(i, j);
                }
            }
        }
        return best < float.MaxValue;
    }

    // ---------- dn.logic ----------

    private static IEnumerator RunLogic()
    {
        var c = new Checks(LogicName);
        try
        {
            // Cell ids.
            foreach (var (i, j) in new[] { (0, 0), (-27, 26), (27, -27), (-1, -1), (5, 20) })
            {
                Cells.FromId(Cells.Id(i, j), out var bi, out var bj);
                c.Check(bi == i && bj == j, $"cell id round trip ({i},{j}) gave ({bi},{bj})");
            }
            // Determinism and Voronoi vs brute force 5 x 5.
            var rnd = new System.Random(7);
            var seed = 12345;
            var wrong = 0;
            for (var k = 0; k < 300; k++)
            {
                var x = (float)(rnd.NextDouble() * 20000 - 10000);
                var z = (float)(rnd.NextDouble() * 20000 - 10000);
                var id = Cells.At(seed, x, z);
                if (id != Cells.At(seed, x, z))
                {
                    wrong++;
                }
                var best = float.MaxValue;
                var bestId = 0;
                var si = Cells.Square(x);
                var sj = Cells.Square(z);
                for (var di = -2; di <= 2; di++)
                {
                    for (var dj = -2; dj <= 2; dj++)
                    {
                        Cells.SeedPoint(seed, si + di, sj + dj, out var px, out var pz);
                        var d = (px - x) * (px - x) + (pz - z) * (pz - z);
                        if (d < best)
                        {
                            best = d;
                            bestId = Cells.Id(si + di, sj + dj);
                        }
                    }
                }
                if (bestId != id)
                {
                    wrong++;
                }
            }
            c.Check(wrong == 0, $"cell lookup: {wrong} of 300 points not in the nearest seed's cell (or not stable)");
            var grid = new Cells.Grid(seed, Cells.Square(-10500f) - 2, Cells.Square(1800f) - 2, Cells.Square(10500f) + 2,
                Cells.Square(10500f) + 2);
            var gridWrong = 0;
            for (var k = 0; k < 20000; k++)
            {
                var x = (float)(rnd.NextDouble() * 21000 - 10500);
                var z = (float)(rnd.NextDouble() * 12000 - 2000);
                if (grid.At(x, z) != Cells.At(seed, x, z))
                {
                    gridWrong++;
                }
            }
            c.Check(gridWrong == 0, $"map grid lookup = cell lookup ({gridWrong} of 20000 points differ)");
            c.Check(MapOverlay.Mix(100, 200, 0.5f) == 150 && MapOverlay.Mix(255, 0, 1f) == 0 && MapOverlay.Mix(7, 250, 0f) == 7,
                "map colour blend");

            // Coverage average over 20 seeds; nesting.
            foreach (var cov in new[] { 0.2f, 0.4f, 0.7f })
            {
                var awake = 0;
                var total = 0;
                var nestBreak = 0;
                for (var s = 0; s < 20; s++)
                {
                    for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
                    {
                        for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
                        {
                            if (!Cells.IsDeepNorthCell(s * 7919 + 1, i, j))
                            {
                                continue;
                            }
                            total++;
                            var a = Cells.IsAwake(s * 7919 + 1, i, j, cov);
                            if (a)
                            {
                                awake++;
                            }
                            if (a && !Cells.IsAwake(s * 7919 + 1, i, j, cov + 0.2f))
                            {
                                nestBreak++;
                            }
                        }
                    }
                }
                var share = total > 0 ? (float)awake / total : 0f;
                c.Check(Mathf.Abs(share - cov) < 0.03f,
                    $"coverage {F(cov)}: {F(share)} of {total} Deep North cells awake over 20 seeds");
                c.Check(nestBreak == 0, $"coverage {F(cov)}: {nestBreak} awake cells asleep at {F(cov + 0.2f)}");
            }
            c.Check(!Cells.IsAwake(1, 0, 25, 0f), "coverage 0 wakes nothing");

            // Storm uptime and lengths (one cell, 2000 cycles, 10 s steps).
            const float min = 300f;
            const float max = 600f;
            var on = 0;
            var steps = 0;
            var run = 0;
            var shortRuns = 0;
            var longRuns = 0;
            var runs = 0;
            var first = true;
            for (double t = 0; t < 2000 * 1350.0; t += 10.0)
            {
                var storm = Cells.IsStorming(99, 3, 22, t, 0.33f, min, max);
                steps++;
                if (storm)
                {
                    on++;
                    run++;
                }
                else if (run > 0)
                {
                    if (!first)
                    {
                        runs++;
                        if (run * 10 < min - 10)
                        {
                            shortRuns++;
                        }
                        // Two storms can touch across a cycle edge: longer than max then.
                        if (run * 10 > 2 * max + 10)
                        {
                            longRuns++;
                        }
                    }
                    first = false;
                    run = 0;
                }
            }
            var uptime = (float)on / steps;
            c.Check(Mathf.Abs(uptime - 0.33f) < 0.03f, $"storm uptime {F(uptime)} (expected about 0.33)");
            c.Check(shortRuns == 0 && longRuns == 0 && runs > 1000,
                $"storm lengths: {runs} storms, {shortRuns} shorter than {F(min)} s, {longRuns} longer than two storms");
            c.Check(!Cells.IsStorming(1, 1, 1, 500, 0f, min, max) && Cells.IsStorming(1, 1, 1, 500, 1f, min, max),
                "storm share 0 = never, 1 = always");

            // Stage, count parsing, invasions, messages.
            c.Check(WorldState.StageOf(0) == 0 && WorldState.StageOf(1) == 1 && WorldState.StageOf(2) == 2
                    && WorldState.StageOf(3) == 3 && WorldState.StageOf(9) == 3 && WorldState.StageOf(-2) == 0,
                "stage of 0, 1, 2, 3, 9, -2 stones");
            c.Check(WorldState.ParseCount("4") == 4 && WorldState.ParseCount(" 2 ") == 2 && WorldState.ParseCount("-3") == 0
                    && WorldState.ParseCount("100000") == WorldState.MaxStones && WorldState.ParseCount("") == 0,
                "stone count parsing (4, ' 2 ', -3, 100000, empty)");
            c.Check(Stones.InvasionsFor(1, 3) == 0 && Stones.InvasionsFor(2, 3) == 0 && Stones.InvasionsFor(3, 3) == 3
                    && Stones.InvasionsFor(3, 0) == 0 && Stones.InvasionsFor(4, 3) == 1 && Stones.InvasionsFor(12, 3) == 1,
                "vanilla invasions per stone: 0, 0, 3 (setting), 1, 1");
            // Missing token = "[token]" (Localization.Translate).
            c.Check(Localization.instance == null || KnownText(Stones.StoneText) && KnownText(HeldCells.ClearedText),
                $"vanilla texts exist: '{Localize(Stones.StoneText)}', '{Localize(HeldCells.ClearedText)}'");

            // Nature band composition over many rolls.
            var rng = new System.Random(11);
            var bandOk = true;
            var sawBig = new bool[3];
            for (var k = 0; k < 2000; k++)
            {
                var b = AreaSpawns.RollBand((min, max) => rng.Next(min, max));
                var gd = b.Greydwarfs + b.Shamans;
                var big = b.Gammeltrolls + b.Barkas;
                bandOk &= gd >= 5 && gd <= 10 && b.Shamans >= 1 && b.Shamans <= 2 && big >= 0 && big <= 2;
                sawBig[big] = true;
            }
            c.Check(bandOk && sawBig[0] && sawBig[1] && sawBig[2],
                "nature bands: 5-10 frost Greydwarfs with 1-2 shamans, 0-2 Gammeltroll or Barka (each seen)");
            c.Check(Stones.ClaimsRequest(10f, 10f) && Stones.ClaimsRequest(14.9f, 10f) && !Stones.ClaimsRequest(9f, 10f)
                    && !Stones.ClaimsRequest(15.5f, 10f),
                "a held request is claimed only by a break 0-5 s after it");
            var cumulative = Rules(r =>
            {
                r.CoverageStage1 = 50;
                r.CoverageStage2 = 30;
                r.CoverageStage3 = 10;
            });
            c.Check(Mathf.Approximately(cumulative.Coverage(2), 0.5f) && Mathf.Approximately(cumulative.Coverage(3), 0.5f)
                    && cumulative.Coverage(0) == 0f,
                "a later stage never covers less than the one before");

            // Broken Morkhalla count.
            var mork = new List<Vector3> { new Vector3(0, 0, 9000), new Vector3(1000, 0, 9000), new Vector3(-2000, 0, 8500) };
            var stones = new List<Vector3> { new Vector3(30, 5030, 9020), new Vector3(-2100, 5000, 8560) };
            c.Check(Stones.CountBroken(mork, stones, 160f) == 1, "broken Morkhalla: 1 of 3 without a stone within 160 m");

            // Army vs nature.
            int H(string s) => s.GetStableHashCode();
            c.Check(Hostility.AreFoes(H("JotunWarrior"), H("TrollFrost")) && Hostility.AreFoes(H("Barka"), H("JotunWitch"))
                    && Hostility.AreFoes(H("Greydwarf_Frozen"), H("Elaking"))
                    && Hostility.AreFoes(H("JotunWarriorDualWield"), H("Greydwarf_Shaman_Frozen")),
                "army vs nature foes both ways");
            c.Check(!Hostility.AreFoes(H("JotunWarrior"), H("Moose")) && !Hostility.AreFoes(H("TrollFrost"), H("Barka"))
                    && !Hostility.AreFoes(H("JotunWarrior"), H("JotunWitch")) && !Hostility.AreFoes(0, H("TrollFrost")),
                "not foes: Moose, nature with nature, army with army, unknown");

            // Area lists wire.
            var pkg = HeldCells.TestPackage(new List<int> { 5, -7, int.MaxValue }, new List<int> { 9 },
                new Dictionary<int, int[]> { { 9, new[] { 1, 0, 2, 1 } }, { -7, new[] { 0, 0, 0, 0 } } });
            pkg.SetPos(0);
            c.Check(HeldCells.TryRead(pkg, out var cleared, out var engaged, out var living) && cleared.Count == 3
                    && cleared.Contains(-7) && engaged.Count == 1 && engaged.Contains(9)
                    && living.TryGetValue(9, out var living9) && living9.Length == 4 && living9[0] == 1 && living9[2] == 2
                    && living9[3] == 1 && living.Count == 2,
                "area lists read back (cleared, engaged, living counts per Jotun kind)");
            var bad = new ZPackage();
            bad.Write(HeldCells.Layout + 1);
            bad.SetPos(0);
            c.Check(!HeldCells.TryRead(bad, out _, out _, out _), "area lists with an unknown layout refused");
            var cut = new ZPackage();
            cut.Write(HeldCells.Layout);
            cut.Write(2);
            cut.Write(1);
            cut.SetPos(0);
            c.Check(!HeldCells.TryRead(cut, out _, out _, out _), "cut area lists refused");

            // Blizzard stops at the Deep North line even where an awake border cell reaches past it.
            ServerRules.TestRules = Rules(r => r.StormShare = 100);
            WorldState.TestStones = 3;
            var borderChecked = false;
            var seedHere = WorldState.Seed;
            var cov3 = ServerRules.Current.Coverage(3);
            for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares && !borderChecked; i++)
            {
                for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares && !borderChecked; j++)
                {
                    if (!Cells.IsAwake(seedHere, i, j, cov3))
                    {
                        continue;
                    }
                    Cells.SeedPoint(seedHere, i, j, out var sx, out var sz);
                    for (var a = 0; a < 360 && !borderChecked; a += 15)
                    {
                        for (var d = 40f; d <= 260f && !borderChecked; d += 20f)
                        {
                            var px = sx + Mathf.Sin(a * Mathf.Deg2Rad) * d;
                            var pz = sz + Mathf.Cos(a * Mathf.Deg2Rad) * d;
                            if (WorldGenerator.IsDeepnorth(px, pz) || Cells.At(seedHere, px, pz) != Cells.Id(i, j))
                            {
                                continue;
                            }
                            borderChecked = true;
                            c.Check(Storms.StormAt(new Vector3(sx, 0f, sz), ServerRules.Current)
                                    && !Storms.StormAt(new Vector3(px, 0f, pz), ServerRules.Current),
                                $"border cell {i},{j}: blizzard at its seed point, none at ({F(px)}, {F(pz)}) past the Deep North line");
                        }
                    }
                }
            }
            c.Check(borderChecked, "found an awake cell reaching past the Deep North line");

            // Spawn gate by stage, Kall and storm.
            if (FindAwakeLand(ServerRules.Current.Coverage(3), out var land, out var landCell))
            {
                ServerRules.TestRules = Rules(r => r.StormShare = 100);
                WorldState.TestStones = 0;
                c.Check(!AreaSpawns.Gate(EntryKind.Jotun, land), "gate: dormant north refuses area Jotun");
                WorldState.TestStones = 3;
                c.Check(AreaSpawns.Gate(EntryKind.Jotun, land), $"gate: stage 3 allows area Jotun in awake cell {landCell}");
                c.Check(AreaSpawns.Gate(EntryKind.Meteor, land), "gate: meteors in a storming awake cell");
                c.Check(!AreaSpawns.Gate(EntryKind.Jotun, new Vector3(0f, 30f, 0f)),
                    "gate: no area Jotun at the world centre");
                ServerRules.TestRules = Rules(r => r.StormShare = 0);
                c.Check(!AreaSpawns.Gate(EntryKind.Meteor, land), "gate: no meteors without storms");
                WorldState.TestKall = true;
                c.Check(!AreaSpawns.Gate(EntryKind.Jotun, land) && !AreaSpawns.Gate(EntryKind.Nature, land),
                    "gate: after Kall no area Jotun outside a burst, no nature band");
                ServerRules.TestPending = true;
                WorldState.TestKall = null;
                c.Check(!AreaSpawns.Gate(EntryKind.Jotun, land), "gate: game waiting for the server's rules = vanilla");
            }
            else
            {
                c.Check(false, "no dry awake Deep North cell found in this world at 70 %");
            }
        }
        finally
        {
            ClearOverrides();
        }
        c.Report();
        yield break;
    }

    // ---------- dn.network ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        var src = Rules(r =>
        {
            r.CoverageStage1 = 11;
            r.CoverageStage2 = 22;
            r.CoverageStage3 = 88;
            r.StarChanceStage1 = 1.5f;
            r.StarChanceStage2 = 33f;
            r.StarChanceStage3 = 66f;
            r.JotunDensity = 150;
            r.NatureFightsBack = false;
            r.StormShare = 50;
            r.StormMinMinutes = 2f;
            r.StormMaxMinutes = 7f;
            r.Meteors = false;
            r.MapAreas = false;
        });
        var pkg = new ZPackage();
        src.Write(pkg);
        pkg.SetPos(0);
        c.Check(AwakeningRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Describe() == src.Describe(),
            "rules round trip: " + (back != null ? back.Describe() : "unreadable"));

        var wild = new ZPackage();
        wild.Write(AwakeningRules.Layout);
        wild.Write(-5);
        wild.Write(500);
        wild.Write(50);
        wild.Write(float.NaN);
        wild.Write(250f);
        wild.Write(10f);
        wild.Write(5);
        wild.Write(true);
        wild.Write(-20);
        wild.Write(101);
        wild.Write(0f);
        wild.Write(1000f);
        wild.Write(true);
        wild.Write(true);
        wild.SetPos(0);
        c.Check(AwakeningRules.TryRead(wild, out var w, out var wc) && wc && w.CoverageStage1 == 0
                && w.CoverageStage2 == 100 && w.StarChanceStage1 == AwakeningRules.Default.StarChanceStage1
                && w.StarChanceStage2 == 100f && w.JotunDensity == AwakeningRules.DensityMin && w.NatureBandChance == 0
                && w.StormShare == 100
                && w.StormMinMinutes == AwakeningRules.StormMinutesMin && w.StormMaxMinutes == AwakeningRules.StormMinutesMax,
            "wild rules clamped into range: " + (w != null ? w.Describe() : "unreadable"));

        var unknown = new ZPackage();
        unknown.Write(AwakeningRules.Layout + 1);
        unknown.SetPos(0);
        c.Check(!AwakeningRules.TryRead(unknown, out _, out _), "unknown layout refused");
        var cut = new ZPackage();
        cut.Write(AwakeningRules.Layout);
        cut.Write(20);
        cut.SetPos(0);
        c.Check(!AwakeningRules.TryRead(cut, out _, out _), "cut-off package refused");

        var own = AwakeningRules.Own();
        c.Check(ReferenceEquals(ServerRules.Select(false, src, own), own), "not a client: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(true, src, own), src), "client with server rules: server's");
        c.Check(ServerRules.Select(true, null, own).IsPending, "client without server rules: pending");
        c.Check(!ServerRules.Settled(1f, 0.8f) && ServerRules.Settled(1f, 0.4f), "push waits PushDelay after a change");

        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible
                && PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed
                && PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "join verdicts");
        c.Report();
        yield break;
    }

    // ---------- dn.vanilla ----------

    private static IEnumerator RunVanilla()
    {
        var c = new Checks(VanillaName);
        var scene = ZNetScene.instance;
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        if (c.Check(index >= 0, "the jotun_invasion persistent event exists"))
        {
            var e = pes.m_possibleEvents[index];
            c.Check(e.maxConcurrent == 3, $"jotun_invasion runs at most 3 at once (maxConcurrent {e.maxConcurrent})");
            c.Check(!e.biomes.HasFlag(Heightmap.Biome.DeepNorth), $"jotun_invasion never lands in the Deep North (biomes {e.biomes})");
            c.Note($"jotun_invasion: index {index} of {pes.m_possibleEvents.Count}, radius {F(e.minRadius)}-{F(e.maxRadius)}, "
                   + $"env {e.environmentOverride} (per biome {e.perBiomeEnvironments}), effects {e.graphicalEffects}, "
                   + $"objects {e.objectsToSpawn.Count}");
        }
        CheckTrigger(c, scene, Stones.StonePrefab, stop: false);
        CheckTrigger(c, scene, "BlackIce_Core", stop: true);

        var missing = new List<string>();
        var notDeepNorth = new List<string>();
        foreach (var name in new List<string>(Hostility.Army) { Hostility.Gammeltroll, Hostility.Barka,
                     Hostility.FrostGreydwarf, Hostility.FrostShaman, AreaSpawns.MeteorPrefab })
        {
            var p = scene.GetPrefab(name);
            if (p == null)
            {
                missing.Add(name);
                continue;
            }
            var ch = p.GetComponent<Character>();
            if (ch != null && ch.m_faction != Character.Faction.DeepNorth)
            {
                notDeepNorth.Add($"{name} ({ch.m_faction})");
            }
        }
        c.Check(missing.Count == 0, "prefabs exist: missing " + string.Join(", ", missing.ToArray()));
        c.Check(notDeepNorth.Count == 0, "army and nature are all Faction.DeepNorth: " + string.Join(", ", notDeepNorth.ToArray()));

        // Area clearing count on it: a saved creature keep its ZDO when unloaded (ZNetScene.RemoveObjects destroy only
        // ZDOs that are not persistent), so unload is never taken for a death.
        var notSaved = new List<string>();
        foreach (var name in Hostility.Army)
        {
            var p = scene.GetPrefab(name);
            var nv = p != null ? p.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.m_persistent)
            {
                notSaved.Add(name);
            }
        }
        c.Check(notSaved.Count == 0, "the Jotun army is saved in the world when unloaded (persistent ZNetView): not saved "
                                     + string.Join(", ", notSaved.ToArray()));

        var kall = scene.GetPrefab("FrozenKing_p3");
        var kallChar = kall != null ? kall.GetComponent<Character>() : null;
        c.Check(kallChar != null && kallChar.m_defeatSetGlobalKey == WorldState.KallKey,
            $"Kall's last phase sets {WorldState.KallKey} (got '{(kallChar != null ? kallChar.m_defeatSetGlobalKey : "no FrozenKing_p3")}')");

        var env = EnvMan.instance;
        c.Check(env != null && env.GetEnv(Storms.PreferredEnv) != null, $"weather {Storms.PreferredEnv} exists");
        if (env != null)
        {
            foreach (var b in env.m_biomes)
            {
                if (b != null && b.m_biome == Heightmap.Biome.DeepNorth)
                {
                    var names = new List<string>();
                    foreach (var en in b.m_environments)
                    {
                        names.Add(en.ToString());
                    }
                    c.Note($"Deep North weathers: {string.Join(", ", names.ToArray())}");
                }
            }
        }

        var zs = ZoneSystem.instance;
        var morkLocations = 0;
        var morkInstances = 0;
        foreach (var loc in zs.m_locations)
        {
            if (loc != null && string.Equals(loc.m_prefabName, Stones.MorkhallaPrefab, StringComparison.OrdinalIgnoreCase))
            {
                morkLocations++;
                c.Note($"Morkhalla location: name '{loc.m_name}', prefab '{loc.m_prefabName}', quantity {loc.m_quantity}, enabled {loc.m_enable}");
            }
        }
        foreach (var li in zs.m_locationInstances.Values)
        {
            if (li.m_location != null && string.Equals(li.m_location.m_prefabName, Stones.MorkhallaPrefab, StringComparison.OrdinalIgnoreCase))
            {
                morkInstances++;
            }
        }
        c.Check(morkLocations > 0 && morkInstances > 0,
            $"Morkhalla ({Stones.MorkhallaPrefab}) in the location list ({morkLocations}) and placed in this world ({morkInstances} planned)");

        // Vanilla invasion spawn entries (for the record and the design table).
        var ctrl = zs.m_zoneCtrlPrefab != null ? zs.m_zoneCtrlPrefab.GetComponent<SpawnSystem>() : null;
        if (ctrl != null)
        {
            foreach (var list in ctrl.m_spawnLists)
            {
                if (list == null)
                {
                    continue;
                }
                foreach (var d in list.m_spawners)
                {
                    if (d != null && d.m_prefab != null && (Stones.IsInvasionName(d.m_requiredPersistentEvent)
                                                            || d.m_prefab.name.StartsWith("Jotun", StringComparison.Ordinal)))
                    {
                        c.Note($"vanilla spawn '{d.m_name}': {d.m_prefab.name}, max {d.m_maxSpawned}, every {F(d.m_spawnInterval)} s, "
                               + $"chance {F(d.m_spawnChance)}, group {d.m_groupSizeMin}-{d.m_groupSizeMax}, levels {d.m_minLevel}-{d.m_maxLevel}, "
                               + $"key '{d.m_requiredGlobalKey}', event '{d.m_requiredPersistentEvent}', hunt {d.m_huntPlayer}");
                    }
                }
            }
        }
        c.Report();
        yield break;
    }

    private static void CheckTrigger(Checks c, ZNetScene scene, string prefab, bool stop)
    {
        var p = scene.GetPrefab(prefab);
        var t = p != null ? p.GetComponentInChildren<TriggerPersistentEventOnDestroy>(true) : null;
        var nview = p != null ? p.GetComponent<ZNetView>() : null;
        if (!c.Check(t != null && p.GetComponent<Destructible>() != null,
                $"{prefab} has a Destructible with a TriggerPersistentEventOnDestroy"))
        {
            return;
        }
        c.Check(Stones.IsInvasionName(t._eventInternalName) && t._stopEvent == stop,
            $"{prefab} {(stop ? "stops" : "starts")} jotun_invasion (event '{t._eventInternalName}', stop {t._stopEvent})");
        c.Check(nview != null && nview.m_persistent, $"{prefab} is saved in the world (persistent ZNetView)");
        c.Note($"{prefab}: text '{t._centralTextOnTriggered}', health {F(p.GetComponent<Destructible>().m_health)}, "
               + $"min tool tier {p.GetComponent<Destructible>().m_minToolTier}");
    }

    // ---------- dn.hostility ----------

    private static IEnumerator RunHostility()
    {
        var c = new Checks(HostilityName);
        var player = Player.m_localPlayer;
        var spawned = new List<GameObject>();
        try
        {
            var fwd = Flat(player.transform.forward).normalized;
            if (fwd == Vector3.zero)
            {
                fwd = Vector3.forward;
            }
            // Face to face 6 m apart, 45 m from the player: vanilla sight needs the target inside the view angle until
            // alerted, and a creature standing still makes almost no noise.
            var spot = player.transform.position + fwd * 45f;
            var krigen = Spawn(Hostility.Krigen, spot, fwd);
            var troll = Spawn(Hostility.Gammeltroll, spot + fwd * 6f, -fwd);
            var moose = Spawn("Moose", spot + Vector3.Cross(Vector3.up, fwd) * 30f);
            spawned.Add(krigen);
            spawned.Add(troll);
            spawned.Add(moose);
            yield return null;
            var k = krigen != null ? krigen.GetComponent<Character>() : null;
            var t = troll != null ? troll.GetComponent<Character>() : null;
            var m = moose != null ? moose.GetComponent<Character>() : null;
            if (!c.Check(k != null && t != null && m != null, "spawned a Krigen, a Gammeltroll and a Moose"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = Rules(null);
            WorldState.TestStones = 0;
            WorldState.Refresh();
            c.Check(!BaseAI.IsEnemy(k, t) && !BaseAI.IsEnemy(t, k), "dormant north: Krigen and Gammeltroll are friends (vanilla)");
            WorldState.TestStones = 1;
            WorldState.Refresh();
            c.Check(BaseAI.IsEnemy(k, t) && BaseAI.IsEnemy(t, k), "awake north: Krigen and Gammeltroll are foes, both ways");
            c.Check(!BaseAI.IsEnemy(k, m) && !BaseAI.IsEnemy(m, t), "awake north: the Moose stays friends with both");
            c.Check(BaseAI.IsEnemy(k, player) && BaseAI.IsEnemy(t, player), "both still attack players");
            ServerRules.TestRules = Rules(r => r.NatureFightsBack = false);
            WorldState.Refresh();
            c.Check(!BaseAI.IsEnemy(k, t), "NatureFightsBack off: friends again");
            ServerRules.TestRules = Rules(null);
            WorldState.Refresh();

            // Live: they pick each other (the player is 45 m away, they are 6 m apart and face each other).
            var kai = k.GetComponent<MonsterAI>();
            var tai = t.GetComponent<MonsterAI>();
            var until = Time.time + 15f;
            var fought = false;
            while (Time.time < until && !fought)
            {
                fought = (kai != null && kai.m_targetCreature == t) || (tai != null && tai.m_targetCreature == k);
                yield return new WaitForSeconds(0.25f);
            }
            if (!fought && kai != null && tai != null)
            {
                c.Note($"Krigen asleep {kai.IsSleeping()} alerted {kai.IsAlerted()} sees {kai.CanSeeTarget(t)}; Gammeltroll "
                       + $"asleep {tai.IsSleeping()} alerted {tai.IsAlerted()} sees {tai.CanSeeTarget(k)}; distance "
                       + $"{F(Vector3.Distance(k.transform.position, t.transform.position))} m");
            }
            c.Check(fought, $"live: Krigen or Gammeltroll targets the other within 15 s (Krigen target "
                            + $"{(kai != null && kai.m_targetCreature != null ? kai.m_targetCreature.name : "none")}, Gammeltroll target "
                            + $"{(tai != null && tai.m_targetCreature != null ? tai.m_targetCreature.name : "none")})");
            if (fought)
            {
                yield return null;
                SelfTest.Screenshot(HostilityName, "krigen_vs_gammeltroll");
                yield return null;
                yield return null;
            }
        }
        finally
        {
            foreach (var go in spawned)
            {
                Kill(go);
            }
            ClearOverrides();
        }
        c.Report();
    }

    // ---------- dn.stones ----------

    private static IEnumerator RunStones()
    {
        var c = new Checks(StonesName);
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var saved = SaveStonesKey();
        var events = new List<PersistentEventSystem.ActivePersistentEvent>(pes.m_activePersistentEvents.list);
        var player = Player.m_localPlayer;
        var stonesLeft = new List<GameObject>();
        try
        {
            if (!c.Check(index >= 0, "jotun_invasion exists"))
            {
                c.Report();
                yield break;
            }
            pes.m_activePersistentEvents.list.Clear();
            pes.UpdateClientEventsList(0L);
            Stones.TestInvasionsAtThirdStone = 3;
            WorldState.WriteStones(0);
            var fwd = Flat(player.transform.forward).normalized;
            var basePos = player.transform.position + (fwd == Vector3.zero ? Vector3.forward : fwd) * 8f;

            IEnumerator Break(int expect, Box ok)
            {
                ok.Ok = false;
                var go = Spawn(Stones.StonePrefab, basePos + Vector3.right * (expect * 3f));
                stonesLeft.Add(go);
                yield return null;
                var d = go != null ? go.GetComponent<Destructible>() : null;
                if (d == null)
                {
                    yield break;
                }
                d.DestroyNow();
                var until = Time.time + 8f;
                while (Time.time < until)
                {
                    WorldState.Refresh();
                    if (WorldState.Stones == expect)
                    {
                        ok.Ok = true;
                        break;
                    }
                    yield return new WaitForSeconds(0.1f);
                }
                // Stone effects (invasions) run on the server tick after the count.
                yield return new WaitForSeconds(0.5f);
            }

            var box = new Box();
            yield return Break(1, box);
            c.Check(box.Ok, $"stone 1 counted (key {WorldState.Stones})");
            c.Check(Stones.ActiveInvasions(pes, index) == 0 && Stones.HeldCount == 0,
                $"stone 1: no vanilla invasion, no request (active {Stones.ActiveInvasions(pes, index)}, held {Stones.HeldCount})");

            // A game still waiting for the server's rules runs vanilla: its request is held, then dropped.
            ServerRules.TestPending = true;
            yield return Break(2, box);
            c.Check(box.Ok, $"stone 2 (vanilla trigger) counted (key {WorldState.Stones})");
            c.Check(Stones.HeldCount == 1, $"stone 2: the vanilla invasion request is held ({Stones.HeldCount})");
            ServerRules.TestPending = false;
            yield return new WaitForSeconds(Stones.HoldSeconds + 0.5f);
            c.Check(Stones.HeldCount == 0 && Stones.ActiveInvasions(pes, index) == 0,
                $"stone 2: request dropped after the hold, no invasion (active {Stones.ActiveInvasions(pes, index)})");

            yield return Break(3, box);
            var expected = Mathf.Min(3, pes.m_possibleEvents[index].maxConcurrent);
            c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == expected,
                $"stone 3: {Stones.ActiveInvasions(pes, index)} vanilla invasions started (expected {expected})");

            yield return Break(4, box);
            c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == expected,
                $"stone 4: vanilla cap holds at {Stones.ActiveInvasions(pes, index)} invasions");
            c.Check(Stones.HeldCount == 0, "no request held after stones 3 and 4");

            // An admin's pevents start, a moment after the mod's own breaks: held, then runs (earlier breaks never claim
            // a later request).
            pes.m_activePersistentEvents.list.Clear();
            pes.UpdateClientEventsList(0L);
            yield return new WaitForSeconds(1f);
            pes.TriggerEvent(Stones.InvasionEvent);
            c.Check(Stones.HeldCount == 1 && Stones.ActiveInvasions(pes, index) == 0, "admin request held");
            yield return new WaitForSeconds(Stones.HoldSeconds + 0.5f);
            c.Check(Stones.HeldCount == 0 && Stones.ActiveInvasions(pes, index) == 1,
                $"admin request ran after the hold ({Stones.ActiveInvasions(pes, index)} invasion)");
        }
        finally
        {
            ServerRules.TestPending = false;
            foreach (var go in stonesLeft)
            {
                Kill(go);
            }
            pes.m_activePersistentEvents.list.Clear();
            pes.m_activePersistentEvents.list.AddRange(events);
            pes.UpdateClientEventsList(0L);
            RestoreStonesKey(saved);
            ClearOverrides();
        }
        c.Note("the test invasions left their ice and Fimbul location far from the spawn (events removed)");
        c.Report();
    }

    // ---------- dn.detect ----------

    private static IEnumerator RunDetect()
    {
        var c = new Checks(DetectName);
        var saved = SaveStonesKey();
        try
        {
            var zs = ZoneSystem.instance;
            zs.RemoveGlobalKey(WorldState.StonesKey);
            WorldState.Refresh();
            c.Check(!WorldState.HasStonesKey, "key removed");
            Stones.TestResetDetection();
            var until = Time.time + 3f;
            while (Time.time < until && !WorldState.HasStonesKey)
            {
                yield return null;
                WorldState.Refresh();
            }
            var placed = new List<Vector3>();
            foreach (var li in zs.m_locationInstances.Values)
            {
                if (li.m_placed && li.m_location != null
                                && string.Equals(li.m_location.m_prefabName, Stones.MorkhallaPrefab, StringComparison.OrdinalIgnoreCase))
                {
                    placed.Add(li.m_position);
                }
            }
            var stones = new List<Vector3>();
            foreach (var z in Stones.AllZdos(Stones.StonePrefab))
            {
                stones.Add(z.GetPosition());
            }
            var expect = Stones.CountBroken(placed, stones, Stones.StoneSearchRadius);
            c.Check(WorldState.HasStonesKey && WorldState.Stones == expect,
                $"detection wrote {WorldState.Stones} (expected {expect}: {placed.Count} placed Morkhalla, {stones.Count} Malicious Ice ZDOs)");
        }
        finally
        {
            RestoreStonesKey(saved);
            ClearOverrides();
        }
        c.Report();
    }

    // ---------- dn.kall ----------

    private static IEnumerator RunKall()
    {
        var c = new Checks(KallName);
        var player = Player.m_localPlayer;
        GameObject krigen = null;
        try
        {
            const int cell = 0x7FFE0001;
            ServerRules.TestRules = Rules(null);
            WorldState.TestStones = 3;
            var hasLand = FindAwakeLand(ServerRules.Current.Coverage(3), out var land, out _);
            c.Check(hasLand && AreaSpawns.Gate(EntryKind.Jotun, land), "before Kall the gate allows area Jotun in an awake cell");
            krigen = Spawn(Hostility.Krigen, player.transform.position + Vector3.forward * 40f);
            yield return null;
            var nview = krigen != null ? krigen.GetComponent<ZNetView>() : null;
            if (!c.Check(nview != null && nview.GetZDO() != null, "spawned a Krigen"))
            {
                c.Report();
                yield break;
            }
            var id = nview.GetZDO().m_uid;
            nview.GetZDO().Set(HeldCells.TagKey, cell);
            c.Check(!HeldCells.IsCleared(cell), "before Kall no cell is cleared");

            // World loaded with Kall already dead (no engage around the players).
            HeldCells.TestSawNoKall(false);
            WorldState.TestKall = true;
            var until = Time.time + 5f;
            while (Time.time < until && !(HeldCells.Scanned && HeldCells.MayBurst(cell) && HeldCells.Living(cell) == 1))
            {
                yield return null;
            }
            c.Check(HeldCells.Scanned && HeldCells.TestTracks(id) && !HeldCells.IsCleared(cell) && HeldCells.MayBurst(cell)
                    && HeldCells.Living(cell) == 1 && HeldCells.LivingOf(cell, 0) == 1 && HeldCells.LivingOf(cell, 3) == 0,
                $"after Kall the tagged Krigen is counted and its area spawns again when entered (tracked "
                + $"{HeldCells.TrackedCreatures}, areas with Jotun {HeldCells.HeldCount}, living here {HeldCells.Living(cell)})");
            c.Check(hasLand && !AreaSpawns.Gate(EntryKind.Jotun, land), "after Kall no area Jotun outside a burst");

            // Engaged = no second burst while a player is near; a burst that got nothing tries again later.
            var here = WorldState.CellAt(player.transform.position);
            const int empty = 0x7FFE0003;
            var burstAt = Time.time;
            HeldCells.AfterBurst(new List<int> { cell, here }, new HashSet<int> { cell, here });
            HeldCells.AfterBurst(new List<int> { empty }, new HashSet<int>());
            c.Check(!HeldCells.MayBurst(here) && !HeldCells.MayBurst(cell) && !HeldCells.MayBurst(empty),
                "an engaged area does not spawn again; an empty burst waits before trying again");
            yield return new WaitForSeconds(HeldCells.ReleaseCheckSeconds + 1.5f);
            c.Check(HeldCells.TestServerEngaged(here) && HeldCells.IsEngaged(here) && !HeldCells.TestServerEngaged(cell)
                    && !HeldCells.TestServerEngaged(empty),
                "the server keeps an area engaged while a player is near it, releases the far one, never engaged the empty one");
            // The game's own hold (network delay) runs out too.
            yield return new WaitForSeconds(Mathf.Max(0f, burstAt + HeldCells.LocalHoldSeconds + 0.5f - Time.time));
            c.Check(HeldCells.MayBurst(empty) && HeldCells.MayBurst(cell) && !HeldCells.MayBurst(here),
                "the released and the empty areas may spawn again; the area here may not");

            // Real servers wait for late-spawn rescans (30 s) before clearing: skip that here.
            HeldCells.TestSettle();
            ZNetScene.instance.Destroy(krigen);
            krigen = null;
            until = Time.time + 5f;
            while (Time.time < until && !HeldCells.IsCleared(cell))
            {
                yield return null;
            }
            c.Check(HeldCells.IsCleared(cell) && !HeldCells.TestTracks(id) && !HeldCells.MayBurst(cell)
                    && HeldCells.Living(cell) == 0,
                "killing its last Jotun cleared the area for good");
            c.Check(HeldCells.TestMarkedAnywhere(cell), "the server wrote the cleared area on a zone control (kept in the world)");

            // Read back from the world: a fresh server scan finds it again.
            HeldCells.TestRescan();
            until = Time.time + 5f;
            while (Time.time < until && !(HeldCells.Scanned && HeldCells.ClearedCount > 0))
            {
                yield return null;
            }
            yield return null;
            c.Check(HeldCells.IsCleared(cell), $"after a new scan the area is still cleared ({HeldCells.ClearedCount} cleared)");
        }
        finally
        {
            Kill(krigen);
            // Leave no test area in the world.
            HeldCells.TestUnmarkEverywhere(0x7FFE0001);
            ClearOverrides();
            HeldCells.TestRescan();
            HeldCells.TestClearEngaged();
        }
        c.Report();
    }

    // Zone-control ZDOs this game owns now.
    private static List<ZDO> OwnedZoneCtrls()
    {
        var list = new List<ZDO>();
        foreach (var ss in SpawnSystem.m_instances)
        {
            var nview = ss != null ? ss.m_nview : null;
            var zdo = nview != null && nview.IsValid() && nview.IsOwner() ? nview.GetZDO() : null;
            if (zdo != null)
            {
                list.Add(zdo);
            }
        }
        return list;
    }

    private static SpawnSystem OwnedSpawner(Vector3 position)
    {
        var zone = ZoneSystem.GetZone(position);
        foreach (var ss in SpawnSystem.m_instances)
        {
            if (ss != null && ZoneSystem.GetZone(ss.transform.position) == zone && ss.m_nview != null
                && ss.m_nview.IsValid() && ss.m_nview.IsOwner())
            {
                return ss;
            }
        }
        return null;
    }

    // ---------- dn.map ----------

    private static IEnumerator RunMap()
    {
        var c = new Checks(MapName);
        try
        {
            var until = Time.time + 20f;
            while (Time.time < until && !MapOverlay.Ready)
            {
                yield return null;
            }
            if (!c.Check(MapOverlay.Ready && Minimap.instance != null, "the map was scanned for Deep North land"))
            {
                c.Report();
                yield break;
            }
            var land = MapOverlay.TestLandPixels();
            c.Note($"map {Minimap.instance.m_textureSize} px of {F(Minimap.instance.m_pixelSize)} m, Deep North land pixels {land}, "
                   + $"Deep North map colour {Minimap.instance.m_deepnorthColor}");
            c.Check(land > 1000, $"the scan found Deep North land on the map ({land} pixels)");

            ServerRules.TestRules = Rules(null);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            var coverage = ServerRules.Current.Coverage(3);
            var hasAwake = MapOverlay.TestFind(coverage, true, out var awakePos);
            var hasAsleep = MapOverlay.TestFind(coverage, false, out var asleepPos);
            c.Check(hasAwake && hasAsleep, $"found an awake ({F(awakePos)}) and a sleeping ({F(asleepPos)}) area on the map");
            yield return null;
            yield return null;
            c.Check(MapOverlay.Tinted && MapOverlay.TestPixel(awakePos) == 2 && MapOverlay.TestPixel(asleepPos) == 1,
                $"stage 3: the awake area is purple on the map, the sleeping one is not (pixels {MapOverlay.TestPixel(awakePos)}, "
                + $"{MapOverlay.TestPixel(asleepPos)})");
            c.Check(MapOverlay.TestPixel(new Vector3(0f, 0f, 0f)) == -1, "the meadows centre is outside the scanned band");

            ServerRules.TestRules = Rules(r => r.MapAreas = false);
            yield return null;
            yield return null;
            c.Check(!MapOverlay.Tinted && MapOverlay.TestPixel(awakePos) == 1, "ShowAreas off: the map has its own colours back");

            ServerRules.TestRules = Rules(null);
            yield return null;
            yield return null;
            var back = MapOverlay.TestPixel(awakePos);
            WorldState.TestStones = 0;
            WorldState.Refresh();
            yield return null;
            yield return null;
            c.Check(back == 2 && MapOverlay.TestPixel(awakePos) == 1,
                $"ShowAreas on again: purple again ({back}); stage 0: no purple ({MapOverlay.TestPixel(awakePos)})");

            WorldState.TestStones = 3;
            ServerRules.TestPending = true;
            WorldState.Refresh();
            yield return null;
            yield return null;
            c.Check(MapOverlay.TestPixel(awakePos) == 1, "a game waiting for the server's rules shows no purple");
        }
        finally
        {
            ClearOverrides();
        }
        yield return null;
        yield return null;
        c.Report();
    }

    // ---------- dn.area ----------

    private static IEnumerator RunArea()
    {
        var c = new Checks(AreaName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var travelled = false;
        try
        {
            // No band rolls of its own: the band check counts the one it forces.
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 100;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = 3;
            WorldState.Refresh();
            if (!c.Check(FindAwakeLand(ServerRules.Current.Coverage(3), out var target, out var cell),
                    "a dry awake Deep North cell exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, target, TravelTimeout, box);
            // No exit from here on: the trip back after finally always runs.
            if (c.Check(box.Ok, $"travelled to the awake cell {cell} at {F(target)}"))
            {
                yield return AreaChecks(c, player);
            }
        }
        finally
        {
            // Leave no cleared test area in the world.
            if (_areaCleared.HasValue)
            {
                HeldCells.TestUnmarkEverywhere(_areaCleared.Value);
                _areaCleared = null;
            }
            ClearOverrides();
            HeldCells.TestRescan();
            HeldCells.TestClearEngaged();
        }
        if (travelled)
        {
            var back = new Box();
            yield return TeleportAndWait(player, home, TravelTimeout, back);
            c.Check(back.Ok, "travelled back to the start");
        }
        c.Report();
    }

    // Body of dn.area once in the awake cell (rules and stage already forced by the caller).
    private static IEnumerator AreaChecks(Checks c, Player player)
    {
        {
            var tagged = new List<Character>();
            var meteorSeen = false;
            var blizzard = false;
            var until = Time.time + 40f;
            while (Time.time < until && (tagged.Count == 0 || !meteorSeen || !blizzard))
            {
                tagged.Clear();
                foreach (var ch in Character.GetAllCharacters())
                {
                    if (ch != null && TagOf(ch) != HeldCells.NoTag
                                   && Vector3.Distance(ch.transform.position, player.transform.position) < 200f)
                    {
                        tagged.Add(ch);
                    }
                }
                if (!meteorSeen)
                {
                    foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                    {
                        if (p != null && p.name.StartsWith(AreaSpawns.MeteorPrefab, StringComparison.Ordinal))
                        {
                            meteorSeen = true;
                            break;
                        }
                    }
                }
                var current = EnvMan.instance != null ? EnvMan.instance.GetCurrentEnvironment() : null;
                blizzard = current != null && current.m_name == Storms.Env();
                yield return new WaitForSeconds(0.5f);
            }
            c.Check(tagged.Count > 0, $"area Jotun spawned and tagged near the player ({tagged.Count})");
            var outside = 0;
            var names = new List<string>();
            foreach (var ch in tagged)
            {
                var tag = TagOf(ch);
                if (!Cells.IsAwake(WorldState.Seed, tag, ServerRules.Current.Coverage(3)))
                {
                    outside++;
                }
                names.Add($"{ch.name.Replace("(Clone)", "")} lvl {ch.GetLevel()}");
            }
            c.Check(outside == 0, $"every tagged Jotun came from an awake cell ({outside} did not)");
            c.Note("area creatures: " + string.Join(", ", names.ToArray()));
            var entry = AreaSpawns.TestEntry(Hostility.Krigen);
            if (entry != null)
            {
                var here = player.transform.position;
                c.Note($"level-up here: sector multiplier {F(WorldGenerator.instance.GetBiomeSector(here).GetLevelUpChanceMultiplier())}, "
                       + $"world level {Game.m_worldLevel}, enemy level-up rate {F(Game.m_enemyLevelUpRate)}, effective chance "
                       + $"{F(SpawnSystem.GetLevelUpChance(here, entry))} % for stage {WorldState.Stage} "
                       + $"(setting {F(entry.m_overrideLevelupChance)} %)");
            }
            c.Check(entry != null && Mathf.Approximately(entry.m_overrideLevelupChance, ServerRules.Current.StarChance(3))
                    && entry.m_maxLevel == 3,
                $"Krigen entry: star chance {(entry != null ? F(entry.m_overrideLevelupChance) : "none")} at stage 3, max level "
                + $"{(entry != null ? entry.m_maxLevel : 0)}");
            c.Check(blizzard, $"blizzard weather in a storming area (current {(EnvMan.instance != null && EnvMan.instance.GetCurrentEnvironment() != null ? EnvMan.instance.GetCurrentEnvironment().m_name : "none")})");
            c.Check(meteorSeen, "Fimbul meteors fell during the storm");
            yield return null;
            SelfTest.Screenshot(AreaName, "awakened_area");
            yield return null;
            yield return null;
            foreach (var ch in tagged)
            {
                if (ch != null)
                {
                    ZNetScene.instance.Destroy(ch.gameObject);
                }
            }

            // A nature band here (forced, not waiting for its 10 % roll).
            var spawner = OwnedSpawner(player.transform.position);
            var band = new List<Character>();
            if (c.Check(spawner != null, "this game owns the zone control here"))
            {
                // Band members already here (an earlier run, a roll before the rules): not this band.
                var before = new HashSet<Character>();
                foreach (var ch in Character.GetAllCharacters())
                {
                    var nv = ch != null ? ch.m_nview : null;
                    var z = nv != null ? nv.GetZDO() : null;
                    if (z != null && z.GetInt(AreaSpawns.BandKey, 0) != 0)
                    {
                        before.Add(ch);
                    }
                }
                // Gone first: the band cap counts them.
                foreach (var ch in before)
                {
                    ZNetScene.instance.Destroy(ch.gameObject);
                }
                yield return null;
                AreaSpawns.TrySpawnBand(spawner, new List<Player> { player });
                yield return null;
                var gd = 0;
                var big = 0;
                foreach (var ch in Character.GetAllCharacters())
                {
                    var nv = ch != null ? ch.m_nview : null;
                    var z = nv != null ? nv.GetZDO() : null;
                    if (z == null || z.GetInt(AreaSpawns.BandKey, 0) == 0 || before.Contains(ch))
                    {
                        continue;
                    }
                    band.Add(ch);
                    var p = z.GetPrefab();
                    if (p == Hostility.FrostGreydwarf.GetStableHashCode() || p == Hostility.FrostShaman.GetStableHashCode())
                    {
                        gd++;
                    }
                    else
                    {
                        big++;
                    }
                }
                c.Check(gd >= 3 && gd <= 10 && big <= 2,
                    $"a nature band spawned together: {gd} frost Greydwarfs (5-10 rolled, some spots may be refused), {big} big");
                c.Note("band: " + string.Join(", ", band.ConvertAll(b => b.name.Replace("(Clone)", "") + " lvl " + b.GetLevel()).ToArray()));
            }
            foreach (var ch in band)
            {
                if (ch != null)
                {
                    ZNetScene.instance.Destroy(ch.gameObject);
                }
            }

            // Map overlay (design 2.8): the area under the player is purple on the map; large map shot (explored around).
            yield return null;
            var mapHere = MapOverlay.TestPixel(player.transform.position);
            c.Check(mapHere == 2, $"the invaded area is purple on the map where the player stands (pixel state {mapHere})");
            var map = Minimap.instance;
            if (map != null)
            {
                map.Explore(player.transform.position, 1500f);
                map.SetMapMode(Minimap.MapMode.Large);
                map.LargeZoom = 0.12f;
                yield return null;
                yield return null;
                yield return null;
                SelfTest.Screenshot(AreaName, "map_large");
                yield return null;
                yield return null;
                map.SetMapMode(Minimap.MapMode.Small);
                yield return null;
            }

            // After Kall (design 2.6). Kall falls while players stand in the area: it waits to be defeated at once, no
            // refill in the area of the fight.
            yield return new WaitForSeconds(0.5f);
            KillTaggedLoaded();
            yield return null;
            HeldCells.TestSawNoKall(true);
            WorldState.TestKall = true;
            var hereCell = WorldState.CellAt(player.transform.position);
            var until2 = Time.time + 5f;
            while (Time.time < until2 && !(HeldCells.Scanned && HeldCells.TestServerEngaged(hereCell) && HeldCells.IsEngaged(hereCell)))
            {
                yield return null;
            }
            c.Check(HeldCells.TestServerEngaged(hereCell) && HeldCells.IsEngaged(hereCell),
                $"Kall killed while in an awake area: the area waits to be defeated at once ({HeldCells.EngagedCount} engaged)");
            yield return new WaitForSeconds(6f);
            var refill = CountTagged(hereCell);
            c.Check(refill == 0, $"no Jotun refill in the area of the fight ({refill} spawned)");

            // Players left and came back (engagement forgotten): entering it spawns its Jotun once, then it is engaged.
            HeldCells.TestClearEngaged();
            var burstTagged = 0;
            until2 = Time.time + 15f;
            while (Time.time < until2 && (burstTagged == 0 || !HeldCells.TestServerEngaged(hereCell)))
            {
                yield return new WaitForSeconds(0.5f);
                burstTagged = CountTagged(hereCell);
            }
            c.Check(burstTagged > 0 && HeldCells.TestServerEngaged(hereCell),
                $"after Kall the area spawned its Jotun when entered again ({burstTagged}) and is now engaged");
            yield return null;
            SelfTest.Screenshot(AreaName, "after_kall_burst");
            yield return null;

            // Engaged: kill a few (room under the caps), stay past the game's own hold: nothing new, still engaged.
            var killedFew = KillSome(hereCell, 3, null);
            var left = burstTagged - killedFew;
            yield return new WaitForSeconds(HeldCells.LocalHoldSeconds + 2f);
            var second = CountTagged(hereCell);
            c.Check(killedFew > 0 && second <= left && HeldCells.TestServerEngaged(hereCell) && HeldCells.IsEngaged(hereCell),
                $"while you stay the area waits to be defeated: {burstTagged} spawned, {killedFew} killed, {second} now (no refill)");

            // Entered again with other Krigen standing near: the area's caps count only its own (they come back).
            // (The kills above may have taken them all already.)
            var killedKrigen = KillSome(hereCell, 100, Hostility.Krigen);
            var decoys = new List<GameObject>();
            for (var k = 0; k < 3; k++)
            {
                decoys.Add(Spawn(Hostility.Krigen, player.transform.position + new Vector3(30f + 4f * k, 0f, 30f), Vector3.back));
            }
            yield return new WaitForSeconds(HeldCells.RecountDelay + 1f);
            var hasOthers = CountTagged(hereCell) > 0;
            HeldCells.TestClearEngaged();
            var krigenBack = 0;
            until2 = Time.time + 12f;
            while (Time.time < until2 && (krigenBack == 0 || !HeldCells.TestServerEngaged(hereCell)))
            {
                yield return new WaitForSeconds(0.5f);
                krigenBack = CountTagged(hereCell, Hostility.Krigen);
            }
            foreach (var d in decoys)
            {
                Kill(d);
            }
            // Vanilla counting would see the 3 others (cap 3) and spawn none.
            c.Check(!hasOthers || krigenBack > 0 && HeldCells.TestServerEngaged(hereCell),
                $"entered again with none of its Krigen left and 3 other Krigen near: {krigenBack} of the area's own Krigen "
                + $"came back ({killedKrigen} more killed just before)" + (hasOthers ? "" : " (skipped: the burst had only Krigen)"));

            // Kill them all: cleared for good (no spawn, no storm, kept in the world).
            HeldCells.TestSettle();
            var killed = KillArea(hereCell);
            until2 = Time.time + 6f;
            while (Time.time < until2 && !HeldCells.IsCleared(hereCell))
            {
                yield return null;
            }
            if (HeldCells.TestMarkedAnywhere(hereCell))
            {
                _areaCleared = hereCell;
            }
            c.Check(HeldCells.IsCleared(hereCell) && !HeldCells.MayBurst(hereCell) && HeldCells.TestMarkedAnywhere(hereCell),
                $"killing the area's {killed} Jotun cleared it for good (written on a zone control)");
            c.Check(!Storms.StormAt(player.transform.position, ServerRules.Current),
                "no storm in a cleared area (storm share 100 %)");
            yield return null;
            yield return null;
            c.Check(MapOverlay.TestPixel(player.transform.position) == 1,
                $"the cleared area left the map (pixel state {MapOverlay.TestPixel(player.transform.position)})");
            yield return new WaitForSeconds(4f);
            var after = CountTagged(hereCell);
            c.Check(after == 0, $"a cleared area spawns nothing ({after})");
        }
    }

    // Tagged area Jotun of a cell, loaded and alive (prefab null = any).
    private static int CountTagged(int cell, string prefab = null)
    {
        var n = 0;
        var hash = prefab != null ? prefab.GetStableHashCode() : 0;
        foreach (var ch in Character.GetAllCharacters())
        {
            if (ch != null && !ch.IsDead() && TagOf(ch) == cell && (prefab == null || Hostility.PrefabOf(ch) == hash))
            {
                n++;
            }
        }
        return n;
    }

    // Up to max loaded tagged Jotun of a cell (prefab null = any) destroyed; returns how many.
    private static int KillSome(int cell, int max, string prefab)
    {
        var n = 0;
        var hash = prefab != null ? prefab.GetStableHashCode() : 0;
        foreach (var ch in Character.GetAllCharacters().ToArray())
        {
            if (n >= max)
            {
                break;
            }
            if (ch != null && !ch.IsDead() && TagOf(ch) == cell && (prefab == null || Hostility.PrefabOf(ch) == hash))
            {
                ZNetScene.instance.Destroy(ch.gameObject);
                n++;
            }
        }
        return n;
    }

    private static void KillTaggedLoaded()
    {
        foreach (var ch in Character.GetAllCharacters().ToArray())
        {
            if (ch != null && TagOf(ch) != HeldCells.NoTag)
            {
                ZNetScene.instance.Destroy(ch.gameObject);
            }
        }
    }

    // Every Jotun of an area, loaded or not (test world): returns how many.
    private static int KillArea(int cell)
    {
        var n = 0;
        foreach (var prefab in Hostility.Army)
        {
            foreach (var zdo in Stones.AllZdos(prefab))
            {
                if (zdo.GetInt(HeldCells.TagKey, HeldCells.NoTag) != cell)
                {
                    continue;
                }
                var go = ZNetScene.instance.FindInstance(zdo);
                if (go != null)
                {
                    ZNetScene.instance.Destroy(go.gameObject);
                }
                else
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(zdo);
                }
                n++;
            }
        }
        return n;
    }

    // ---------- dn.morkhalla ----------

    private static IEnumerator RunMorkhalla()
    {
        var c = new Checks(MorkhallaName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var zs = ZoneSystem.instance;
        var found = false;
        var target = Vector3.zero;
        var best = float.MaxValue;
        foreach (var li in zs.m_locationInstances.Values)
        {
            if (li.m_location == null
                || !string.Equals(li.m_location.m_prefabName, Stones.MorkhallaPrefab, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var d = li.m_position.sqrMagnitude;
            if (d < best)
            {
                best = d;
                target = li.m_position;
                found = true;
            }
        }
        if (!c.Check(found, "a Morkhalla is planned in this world"))
        {
            c.Report();
            yield break;
        }
        var box = new Box();
        yield return TeleportAndWait(player, target + new Vector3(0f, 0f, 25f), TravelTimeout, box);
        c.Check(box.Ok, $"travelled to the Morkhalla at {F(target)}");
        yield return new WaitForSeconds(2f);
        var near = 0;
        var all = new List<Vector3>();
        foreach (var z in Stones.AllZdos(Stones.StonePrefab))
        {
            var p = z.GetPosition();
            all.Add(p);
            if ((Flat(p) - Flat(target)).magnitude <= Stones.StoneSearchRadius)
            {
                near++;
                c.Note($"Malicious Ice at {F(p)}, {F((Flat(p) - Flat(target)).magnitude)} m from the location");
            }
        }
        c.Check(near >= 1, $"{near} Malicious Ice in this Morkhalla (within {F(Stones.StoneSearchRadius)} m)");
        var placed = false;
        foreach (var li in zs.m_locationInstances.Values)
        {
            if (li.m_position == target)
            {
                placed = li.m_placed;
            }
        }
        c.Check(placed, "the location is marked placed once its zone was generated");
        c.Check(Stones.CountBroken(new List<Vector3> { target }, all, Stones.StoneSearchRadius) == 0,
            "detection counts this intact Morkhalla as not broken");
        yield return TeleportAndWait(player, home, TravelTimeout, box);
        c.Check(box.Ok, "travelled back to the start");
        c.Report();
    }
#endif
}
