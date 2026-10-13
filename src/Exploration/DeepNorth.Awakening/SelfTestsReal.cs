#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Single-player self tests that do the real thing where the older ones took a shortcut (tools in
// SelfTestsTools.cs; registered from there, before dn.cleanlog). Each one its own area, own small test:
//   dn.areas.return     T03: an invaded place, a trip to a quiet place, back: quiet stays quiet, invaded stays invaded
//                       (same Jotun, no second set)
//   dn.storm.leave      T09: walk over the edge of a storming area: the game is no longer given the blizzard and plays
//                       one of the place's own weathers; walk back in: blizzard again
//   dn.storm.meteorhit  T09: god mode off, the player under a falling meteor of the storm: it hurts
//   dn.kall.stay        T12: the real Kall key set while standing in the area, then about 40 s there: nothing new;
//                       then 3 kills and every spawn timer of the place due: still nothing new
//   dn.kall.undo        T12: the real Kall key removed: storms and real new Jotun as before Kall; set again: the kept
//                       mark clears the area again
//   dn.cleared.return   T12: a cleared area, a real trip far away and back: still quiet, no blizzard, off the map
//   dn.kall.edge        T20: over the edge of an area that waits to be defeated and back in: nothing new
//   dn.kall.nextdoor    T20: Kall falls while the player stands in one area: the area next door has its Jotun when
//                       the player walks in
//   dn.kall.death       T20: a real death with the bed inside the area, respawn there: nothing new
// Kall here = the real key defeated_frozenking_p3 (what the console commands setkey / removekey write), put back in
// finally. Stage and rules stay forced in memory like in the older tests (never the config, never the real count).
internal static partial class SelfTests
{
    private const string AreasReturnName = "dn.areas.return";
    private const string StormLeaveName = "dn.storm.leave";
    private const string MeteorHitName = "dn.storm.meteorhit";
    private const string KallStayName = "dn.kall.stay";
    private const string KallUndoName = "dn.kall.undo";
    private const string ClearedReturnName = "dn.cleared.return";
    private const string KallEdgeName = "dn.kall.edge";
    private const string KallNextDoorName = "dn.kall.nextdoor";
    private const string KallDeathName = "dn.kall.death";

    // Land slots of the newer tests (after SlotsUsed of the older ones; LandSlot wraps when the world has fewer areas).
    private const int SlotStay = 9;
    private const int SlotUndo = 10;
    private const int SlotClearedReturn = 11;
    private const int SlotEdge = 12;
    private const int SlotDeath = 13;
    private const int SlotMeteor = 14;
    private const int SlotStormLeave = 15;
    private const int SlotFog = 16;
    private const int SlotMorale = 17;
    private const int SlotSneak = 18;
    private const int SlotRelogCleared = 19;
    private const int SlotRelogKills = 20;
    private const int SlotRelogTarget = 21;
    private const int SlotNextDoor = 22;
    private const int SlotDisabled = 23;
    private const int SlotMapOpen = 24;
    private const int SlotMapScreen = 25;

    private static readonly string[] RealNames =
    {
        AreasReturnName, StormLeaveName, MeteorHitName, KallStayName, KallUndoName, ClearedReturnName, KallEdgeName,
        KallNextDoorName, KallDeathName,
    };

    // Called from RegisterMore, before dn.cleanlog (it looks back on these too).
    private static void RegisterReal()
    {
        SelfTest.Register(AreasReturnName, RunAreasReturn);
        SelfTest.Register(StormLeaveName, RunStormLeave);
        SelfTest.Register(MeteorHitName, RunMeteorHit);
        SelfTest.Register(KallStayName, RunKallStay);
        SelfTest.Register(KallUndoName, RunKallUndo);
        SelfTest.Register(ClearedReturnName, RunClearedReturn);
        SelfTest.Register(KallEdgeName, RunKallEdge);
        SelfTest.Register(KallNextDoorName, RunKallNextDoor);
        SelfTest.Register(KallDeathName, RunKallDeath);
        RegisterMap();
        RegisterOtherMods();
        RegisterStaged();
    }

    private static void UnregisterReal()
    {
        foreach (var name in RealNames)
        {
            SelfTest.Unregister(name);
        }
        UnregisterMap();
        UnregisterOtherMods();
        UnregisterStaged();
        UnregisterMpMore();
    }

    // ---------- tools ----------

    // The real Kall key of the world.
    private static bool RealKall()
    {
        var zs = ZoneSystem.instance;
        return zs != null && zs.GetGlobalKey(WorldState.KallKey);
    }

    // Same calls as the console commands "setkey defeated_frozenking_p3" / "removekey defeated_frozenking_p3"
    // (Terminal: ZoneSystem.SetGlobalKey / RemoveGlobalKey), then the mod reads the keys like twice a second.
    private static void SetRealKall(bool on)
    {
        var zs = ZoneSystem.instance;
        if (zs == null)
        {
            return;
        }
        if (on)
        {
            zs.SetGlobalKey(WorldState.KallKey);
        }
        else
        {
            zs.RemoveGlobalKey(WorldState.KallKey);
        }
        WorldState.Refresh();
    }

    // Area Jotun (any area) within a radius of a place, loaded or not: gone. Call only when Kall is off (else each one
    // is a kill for the server).
    private static int PurgeTagged(Vector3 center, float radius)
    {
        var zdos = ZDOMan.instance;
        var scene = ZNetScene.instance;
        if (zdos == null || scene == null)
        {
            return 0;
        }
        var n = 0;
        foreach (var prefab in Hostility.Army)
        {
            foreach (var zdo in Stones.AllZdos(prefab))
            {
                if (zdo.GetInt(HeldCells.TagKey, HeldCells.NoTag) == HeldCells.NoTag
                    || Flat(zdo.GetPosition() - center).magnitude > radius)
                {
                    continue;
                }
                var view = scene.FindInstance(zdo);
                if (view != null)
                {
                    scene.Destroy(view.gameObject);
                }
                else
                {
                    // Not loaded: this game take it, then destroy (ZDOMan destroy only what it own).
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    zdos.DestroyZDO(zdo);
                }
                n++;
            }
        }
        return n;
    }

    // End of a test that used the real Kall key: key as before, test areas unmarked, forced state gone, server lists
    // fresh, then the Jotun around the places of the test gone (key off first: never a kill).
    private static void EndRealKall(bool hadKall, int[] cells, params Vector3[] places)
    {
        SetRealKall(hadKall);
        EndKall(cells);
        foreach (var place in places)
        {
            PurgeTagged(place, 600f);
        }
    }

    private static HashSet<ZDOID> IdsOf(List<Character> list)
    {
        var ids = new HashSet<ZDOID>();
        foreach (var ch in list)
        {
            var zdo = ZdoOf(ch);
            if (zdo != null)
            {
                ids.Add(zdo.m_uid);
            }
        }
        return ids;
    }

    // How many of a list are (in = true) or are not (in = false) in a set of ids.
    private static int CountIds(List<Character> list, HashSet<ZDOID> ids, bool inSet)
    {
        var n = 0;
        foreach (var ch in list)
        {
            var zdo = ZdoOf(ch);
            if (zdo != null && ids.Contains(zdo.m_uid) == inSet)
            {
                n++;
            }
        }
        return n;
    }

    // Like walking a few dozen metres inside the loaded area: the game's own short teleport (2 s, no loading screen),
    // feet on the ground. Vanilla refuse a new one within 2 s of the last: retry.
    private static IEnumerator Hop(Player player, Vector3 target, Box result)
    {
        result.Ok = false;
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(target, out var ground))
        {
            target.y = ground + 0.5f;
        }
        var until = Time.time + 12f;
        while (!player.TeleportTo(target, player.transform.rotation, false))
        {
            if (Time.time > until)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        while (player.IsTeleporting() && Time.time < until)
        {
            yield return new WaitForSeconds(0.1f);
        }
        result.Ok = !player.IsTeleporting() && Flat(player.transform.position - target).magnitude < 5f;
    }

    // Player moved at once to a place of the loaded area (feet on the ground, no fall).
    private static void Put(Player player, Vector3 at)
    {
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(at, out var ground))
        {
            at.y = ground + 0.2f;
        }
        player.transform.position = at;
        player.m_body.position = at;
        player.m_body.linearVelocity = Vector3.zero;
        player.m_maxAirAltitude = at.y;
        Physics.SyncTransforms();
    }

    private static bool DryAt(WorldGenerator wg, Vector3 p) => wg.GetHeight(p.x, p.z) >= 36f;

    private struct Edge
    {
        internal int Cell;
        internal int OutCell;
        internal Vector3 Inside;
        internal Vector3 Outside;
    }

    // A place 40 m inside an invaded dry area and one 25 m outside it, straight across its edge, on dry land of an area
    // that is not invaded (at stage 3): 65 m apart, both loaded when the player stands on either. deepNorthOutside =
    // the outside place is inside the Deep North line too (the blizzard could blow there).
    private static bool FindEdge(int firstSlot, bool deepNorthOutside, out Edge edge)
    {
        edge = default;
        var wg = WorldGenerator.instance;
        if (wg == null)
        {
            return false;
        }
        var seed = WorldState.Seed;
        var coverage = DesignCoverage(3);
        var list = AwakeLand(coverage);
        for (var k = 0; k < list.Count; k++)
        {
            var land = list[(firstSlot + k) % list.Count];
            for (var a = 0; a < 360; a += 20)
            {
                var dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad));
                var d = 20f;
                var found = false;
                for (; d <= 420f; d += 5f)
                {
                    var p = land.Point + dir * d;
                    if (Cells.At(seed, p.x, p.z) != land.Cell)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found || d < 90f)
                {
                    continue;
                }
                var inside = land.Point + dir * (d - 40f);
                var outside = land.Point + dir * (d + 25f);
                var outCell = Cells.At(seed, outside.x, outside.z);
                // One edge only between the two places: the area itself, then one area that is not invaded.
                if (Cells.At(seed, inside.x, inside.z) != land.Cell || outCell == land.Cell || Cells.IsAwake(seed, outCell, coverage)
                    || Cells.At(seed, (land.Point + dir * (d + 10f)).x, (land.Point + dir * (d + 10f)).z) != outCell)
                {
                    continue;
                }
                if (!WorldGenerator.IsDeepnorth(inside.x, inside.z) || (deepNorthOutside && !WorldGenerator.IsDeepnorth(outside.x, outside.z)))
                {
                    continue;
                }
                var dry = true;
                for (var s = d - 40f; s <= d + 25f && dry; s += 13f)
                {
                    dry = DryAt(wg, land.Point + dir * s);
                }
                if (!dry)
                {
                    continue;
                }
                inside.y = wg.GetHeight(inside.x, inside.z);
                outside.y = wg.GetHeight(outside.x, outside.z);
                edge = new Edge { Cell = land.Cell, OutCell = outCell, Inside = inside, Outside = outside };
                return true;
            }
        }
        return false;
    }

    // Two invaded dry areas side by side (their seed points 250 to 600 m apart, nothing but the two between them).
    private static bool FindNeighbours(int firstSlot, out Land first, out Land second)
    {
        first = default;
        second = default;
        var seed = WorldState.Seed;
        var list = AwakeLand(DesignCoverage(3));
        for (var k = 0; k < list.Count; k++)
        {
            var a = list[(firstSlot + k) % list.Count];
            foreach (var b in list)
            {
                var gap = Flat(b.Point - a.Point).magnitude;
                if (b.Cell == a.Cell || gap < 250f || gap > 600f)
                {
                    continue;
                }
                var touching = true;
                for (var t = 0.05f; t < 1f && touching; t += 0.05f)
                {
                    var p = Vector3.Lerp(a.Point, b.Point, t);
                    var cell = Cells.At(seed, p.x, p.z);
                    touching = cell == a.Cell || cell == b.Cell;
                }
                if (touching)
                {
                    first = a;
                    second = b;
                    return true;
                }
            }
        }
        return false;
    }

    // Weathers the game itself can pick at a place (its biome's list), as names.
    private static List<string> OwnWeathers(Vector3 at)
    {
        var names = new List<string>();
        var env = EnvMan.instance;
        var wg = WorldGenerator.instance;
        if (env == null || wg == null)
        {
            return names;
        }
        var entries = env.GetAvailableEnvironments(wg.GetBiomeSector(at));
        if (entries != null)
        {
            // The game hands out its own working list: copy now.
            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.m_environment))
                {
                    names.Add(entry.m_environment);
                }
            }
        }
        return names;
    }

    // A Fimbul meteor in the air over an area (a neighbour area that storms may drop its own next to it).
    private static bool MeteorOver(int cell)
    {
        foreach (var m in Meteors())
        {
            if (WorldState.CellAt(m.transform.position) == cell)
            {
                return true;
            }
        }
        return false;
    }

    // Watch a place after Kall: area Jotun that were not there before (the caller counts them), and whether the area
    // waited to be defeated on the server the whole time.
    private sealed class StayWatch
    {
        internal int NewJotun;
        internal bool AlwaysWaiting = true;
        internal float Seconds;
    }

    private static IEnumerator WatchStay(Func<int> newJotun, int cell, float seconds, StayWatch w)
    {
        var start = Time.realtimeSinceStartup;
        var end = start + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            w.NewJotun = Mathf.Max(w.NewJotun, newJotun());
            w.AlwaysWaiting &= cell == HeldCells.NoTag || HeldCells.TestServerEngaged(cell);
            yield return new WaitForSeconds(0.5f);
        }
        w.Seconds = Time.realtimeSinceStartup - start;
    }

    // ---------- dn.areas.return (T03) ----------

    private static IEnumerator RunAreasReturn()
    {
        var c = new Checks(AreasReturnName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var point = Vector3.zero;
        var haveArea = false;
        try
        {
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 0;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = 1;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            haveArea = Stage1Land(out point, out var cell);
            var haveQuiet = QuietLand(out var quietPoint, out var quietCell);
            if (!c.Check(haveArea && haveQuiet, "a dry area invaded at stage 1 and a dry Deep North area that is quiet at stage 1 exist"))
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var spawner = new Found<SpawnSystem>();
                yield return WaitSpawner(pos, 12f, spawner);
                // A full first pass, like in a place never visited.
                DestroyAll(TaggedNear(pos, 400f));
                yield return Frames(3);
                AreaSpawns.TestResetTimers(spawner.Value, true, false);
                yield return WaitTagged(pos, 250f, 4, clock.Cap(20f, 3));
                var first = TaggedNear(pos, 400f);
                var ids = IdsOf(first);
                yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, 6f);
                c.Check(spawner.Value != null && first.Count > 0 && WorldState.IsAwakeCell(cell, rules) && AreaSpawns.Gate(EntryKind.Jotun, pos)
                        && MapOverlay.TestPixel(pos) == 2,
                    $"first visit: the place is invaded ({first.Count} Jotun around: {Kinds(first)}; map pixel state {MapOverlay.TestPixel(pos)})");

                // Leave for a quiet place.
                yield return TeleportAndWait(player, quietPoint, TravelTimeout, box);
                if (c.Check(box.Ok, $"left for the quiet area {quietCell} at {F(quietPoint)}, {F(Flat(quietPoint - point).magnitude)} m away"))
                {
                    var quietPos = player.transform.position;
                    var known = new HashSet<Character>(Character.GetAllCharacters());
                    var q = new Quiet();
                    yield return WatchQuiet(player, clock.Cap(8f, 2), known, q);
                    c.Check(!WorldState.IsAwakeCell(quietCell, rules) && q.NewTagged == 0 && q.NewBand == 0 && !q.Storm && !q.Meteor
                            && !AreaSpawns.Gate(EntryKind.Jotun, quietPos) && MapOverlay.TestPixel(quietPos) != 2,
                        $"the quiet place is quiet after a visit to an invaded one ({q.NewTagged} Jotun, {q.NewBand} band members, "
                        + $"blizzard {q.Storm}, map pixel state {MapOverlay.TestPixel(quietPos)})");
                    var kept = 0;
                    foreach (var id in ids)
                    {
                        if (ZDOMan.instance.GetZDO(id) != null)
                        {
                            kept++;
                        }
                    }
                    c.Check(kept == ids.Count, $"the Jotun of the invaded place stay in the world while nobody is there ({kept} of {ids.Count})");
                }

                // Come back.
                if (c.Check(clock.Room(16f, 2), "time left to come back to the invaded place"))
                {
                    yield return TeleportAndWait(player, point, TravelTimeout, box);
                    if (c.Check(box.Ok, "travelled back into the invaded area"))
                    {
                        yield return Until(() => CountIds(TaggedNear(pos, 400f), ids, true) >= ids.Count, 6f);
                        var same = CountIds(TaggedNear(pos, 400f), ids, true);
                        // A place starts its spawn checks 10 s after it loads: wait past that.
                        var waited = clock.Cap(11f);
                        yield return new WaitForSeconds(waited);
                        var later = TaggedNear(pos, 400f);
                        var fresh = CountIds(later, ids, false);
                        c.Check(same >= Mathf.Max(1, ids.Count - 1) && fresh == 0,
                            $"back in the invaded place: its Jotun are the same ones ({same} of {ids.Count}), no second set came ({fresh} new in {F(waited)} s more)");
                        yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, 6f);
                        c.Check(WorldState.IsAwakeCell(cell, rules) && AreaSpawns.Gate(EntryKind.Jotun, pos) && MapOverlay.TestPixel(pos) == 2,
                            $"it is still invaded: takes area spawns, purple on the map (pixel state {MapOverlay.TestPixel(pos)})");
                    }
                }
            }
        }
        finally
        {
            ClearOverrides();
            SweepArea(player);
            if (haveArea)
            {
                PurgeTagged(point, 600f);
            }
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.storm.leave (T09) ----------

    private static IEnumerator RunStormLeave()
    {
        var c = new Checks(StormLeaveName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var away = false;
        var edge = default(Edge);
        var haveEdge = false;
        try
        {
            ServerRules.TestRules = StormRules(100, false);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            haveEdge = FindEdge(SlotStormLeave, true, out edge);
            if (!c.Check(haveEdge && EnvMan.instance != null,
                    "an invaded dry area with dry Deep North land of an area that is not invaded right across its edge exists"))
            {
                c.Report();
                yield break;
            }
            var env = EnvMan.instance;
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, edge.Inside, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to {F(edge.Inside)}, 40 m inside the storming area {edge.Cell}"))
            {
                var blizzard = Storms.Env();
                yield return Until(() => env.GetEnvironmentOverride() == blizzard && CurrentEnv() == blizzard, 8f);
                c.Check(blizzard != null && WorldState.CellAt(player.transform.position) == edge.Cell && env.GetEnvironmentOverride() == blizzard
                        && CurrentEnv() == blizzard,
                    $"StormShare 100, inside the area: the game is given the blizzard and plays it (weather {CurrentEnv()})");

                // Out, 65 m further: over the edge, still inside the Deep North line.
                yield return Hop(player, edge.Outside, box);
                var outPos = player.transform.position;
                var outCell = WorldState.CellAt(outPos);
                if (c.Check(box.Ok && outCell != edge.Cell && !WorldState.IsAwakeCell(outCell, ServerRules.Current)
                            && WorldGenerator.IsDeepnorth(outPos.x, outPos.z),
                        $"walked out over the edge to {F(outPos)}: an area that is not invaded, still in the Deep North"))
                {
                    var t0 = Time.realtimeSinceStartup;
                    yield return Until(() => Storms.Override() == null && env.GetEnvironmentOverride() != blizzard, 3f);
                    var took = Time.realtimeSinceStartup - t0;
                    // The game may force a weather of its own in some places (events, special parts of a biome).
                    var given = env.GetEnvironmentOverride() ?? "";
                    c.Check(Storms.Override() == null && given != blizzard,
                        $"left the storming area: the game is no longer given the blizzard {F(took)} s later (it is given '{given}')");
                    var own = OwnWeathers(outPos);
                    if (given.Length > 0)
                    {
                        own.Add(given);
                    }
                    yield return Until(() => own.Contains(CurrentEnv()), 15f);
                    c.Check(own.Count > 0 && own.Contains(CurrentEnv()),
                        $"normal weather comes back: the game plays '{CurrentEnv()}', one of the place's own weathers ({string.Join(", ", own.ToArray())})");
                    if (CurrentEnv() == blizzard)
                    {
                        c.Note($"the place's own weather of the moment is {blizzard} too: only the game's weather choice tells the two apart here");
                    }
                }

                // Back in.
                yield return Hop(player, edge.Inside, box);
                if (c.Check(box.Ok && WorldState.CellAt(player.transform.position) == edge.Cell, "walked back into the storming area"))
                {
                    yield return Until(() => env.GetEnvironmentOverride() == blizzard && CurrentEnv() == blizzard, 10f);
                    c.Check(env.GetEnvironmentOverride() == blizzard && CurrentEnv() == blizzard,
                        $"back inside: the blizzard blows again (weather {CurrentEnv()})");
                }
            }
        }
        finally
        {
            ClearOverrides();
            SweepArea(player);
            if (haveEdge)
            {
                PurgeTagged(edge.Inside, 600f);
            }
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.storm.meteorhit (T09) ----------

    // God mode off, the player walks under a falling meteor of the area's storm (they never aim nearer than 40 m): the
    // hit of the game's own projectile hurts. The player is made sturdy for the test (base health, the field the game
    // reads; put back after), so the hit cannot kill. Area Jotun are taken away as they come (before Kall: no kill), so
    // nothing else hits the player; the cold of the Deep North hurts too, but never as a ranged hit.
    private static IEnumerator RunMeteorHit()
    {
        var c = new Checks(MeteorHitName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var point = Vector3.zero;
        var haveArea = false;
        var baseHp = player.m_baseHP;
        var health = player.GetHealth();
        var effects = new HashSet<int>();
        foreach (var se in player.GetSEMan().GetStatusEffects())
        {
            effects.Add(se.NameHash());
        }
        var rigged = false;
        try
        {
            ServerRules.TestRules = StormRules(100, true);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            haveArea = LandSlot(SlotMeteor, out point, out var cell);
            if (!c.Check(haveArea, "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the storming area {cell} at {F(point)}"))
            {
                var start = player.transform.position;
                rigged = true;
                player.m_baseHP = 5000f;
                player.UpdateFood(0f, true);
                player.SetHealth(player.GetMaxHealth());
                player.SetGodMode(false);
                c.Check(!player.InGodMode() && player.GetHealth() >= 5000f, $"god mode is off; the player has {F(player.GetHealth())} health for the test");

                var tried = new HashSet<int>();
                var hurt = false;
                var lost = 0f;
                var tries = 0;
                var hitText = "";
                var end = Time.realtimeSinceStartup + clock.Cap(45f);
                while (Time.realtimeSinceStartup < end && !hurt && tries < 5)
                {
                    DestroyAll(TaggedNear(player.transform.position, 400f));
                    // A meteor still high up, over dry ground.
                    Projectile pick = null;
                    var under = Vector3.zero;
                    foreach (var m in Meteors())
                    {
                        var p = m.transform.position;
                        if (tried.Contains(m.GetInstanceID()) || !ZoneSystem.instance.GetGroundHeight(p, out var ground) || ground < 31f
                            || p.y - ground < 35f)
                        {
                            continue;
                        }
                        pick = m;
                        under = new Vector3(p.x, ground, p.z);
                        break;
                    }
                    if (pick == null)
                    {
                        yield return new WaitForSeconds(0.2f);
                        continue;
                    }
                    tried.Add(pick.GetInstanceID());
                    tries++;
                    Put(player, under);
                    player.SetHealth(player.GetMaxHealth());
                    yield return null;
                    var before = player.GetHealth();
                    var lowest = before;
                    var last = player.m_lastHit;
                    HitData meteorHit = null;
                    // Until it is down (the game removes it), then a second for what it leaves behind.
                    var fallEnd = Time.realtimeSinceStartup + 14f;
                    var goneAt = -1f;
                    while (Time.realtimeSinceStartup < fallEnd && (goneAt < 0f || Time.realtimeSinceStartup - goneAt < 1.2f))
                    {
                        if (goneAt < 0f && pick == null)
                        {
                            goneAt = Time.realtimeSinceStartup;
                        }
                        lowest = Mathf.Min(lowest, player.GetHealth());
                        var now = player.m_lastHit;
                        if (now != null && !ReferenceEquals(now, last))
                        {
                            last = now;
                            if (now.m_ranged && now.GetAttacker() == null && now.GetTotalDamage() > 0f)
                            {
                                meteorHit = now;
                            }
                        }
                        // The player stays where the meteor comes down.
                        if (pick != null && Flat(player.transform.position - under).magnitude > 1.5f)
                        {
                            Put(player, under);
                        }
                        yield return null;
                    }
                    if (meteorHit != null && before - lowest >= 1f)
                    {
                        hurt = true;
                        lost = before - lowest;
                        hitText = $"{F(meteorHit.GetTotalDamage())} damage in the hit, hit type {meteorHit.m_hitType}";
                    }
                    else
                    {
                        c.Note($"meteor {tries}: down {(goneAt >= 0f ? "yes" : "not within 14 s")}, health {F(before)} -> {F(lowest)}, "
                               + $"ranged hit without attacker {(meteorHit != null ? "yes" : "no")}");
                        // Back to the middle of the area for the next one (meteors aim 40 to 80 m from the player).
                        Put(player, start);
                    }
                }
                c.Check(tries > 0, $"meteors fall in the storming area ({tries} tried)");
                c.Check(hurt, hurt
                    ? $"god mode off, the player under a falling meteor: it hurts ({F(lost)} health lost; {hitText}; meteor {tries})"
                    : $"god mode off, the player under a falling meteor: no meteor hurt the player ({tries} tried)");
                c.Check(!player.IsDead(), "the player lives");
            }
        }
        finally
        {
            if (rigged)
            {
                player.SetGodMode(true);
                player.m_baseHP = baseHp;
                player.UpdateFood(0f, true);
                player.SetHealth(Mathf.Min(health, player.GetMaxHealth()));
                // What the hits left on the player (burning, frost...): gone.
                foreach (var se in new List<StatusEffect>(player.GetSEMan().GetStatusEffects()))
                {
                    if (!effects.Contains(se.NameHash()))
                    {
                        player.GetSEMan().RemoveStatusEffect(se, true);
                    }
                }
            }
            ClearOverrides();
            SweepArea(player);
            if (haveArea)
            {
                PurgeTagged(point, 600f);
            }
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kall.stay (T12) ----------

    private static IEnumerator RunKallStay()
    {
        var c = new Checks(KallStayName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var cell = HeldCells.NoTag;
        var point = Vector3.zero;
        var hadKall = RealKall();
        var tap = EnsureTap();
        try
        {
            // No storm, no band, no clearing: only the Jotun count.
            ServerRules.TestRules = KallRules(0, 60);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotStay, out point, out cell);
            if (!c.Check(!hadKall && haveArea, "Kall is not defeated in this world and a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f));
                c.Check(TaggedOf(cell).Count > 0, $"before Kall: area Jotun around the player ({Kinds(TaggedOf(cell))})");

                // The real key, while the player stands there.
                var mark = tap.Mark();
                SetRealKall(true);
                yield return Until(() => HeldCells.Scanned && HeldCells.TestServerEngaged(cell) && HeldCells.IsEngaged(cell), 5f);
                c.Check(WorldState.KallDefeated && WorldState.TestKall == null && HeldCells.TestServerEngaged(cell) && HeldCells.IsEngaged(cell),
                    "the real key defeated_frozenking_p3 set: Kall is defeated, the area under the player waits to be defeated");
                c.Check(tap.Count(mark, "Kall is defeated:", "around the players wait to be defeated") == 1,
                    "log line \"Kall is defeated: ... around the players wait to be defeated ...\" once");

                var known = IdsOf(TaggedNear(pos, 400f));
                var w = new StayWatch();
                // As long as the clock allows, the second part (16 s) and the trip home set aside.
                var stay = Mathf.Max(1f, Mathf.Min(42f, clock.Left - TripSeconds - 16f));
                yield return WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), cell, stay, w);
                c.Check(w.Seconds >= 25f && w.NewJotun == 0 && w.AlwaysWaiting,
                    $"the player stays {F(w.Seconds)} s (the game's own 10 s hold is long over): no new Jotun appear ({w.NewJotun} new, "
                    + $"{known.Count} were there), the area waits to be defeated the whole time ({w.AlwaysWaiting})");
                var line = First(RunConsole(AdminCommand.Name));
                c.Check(ReadOf(line, JotunDefeated, out var k, out _) && k == 0 && line.Contains("waits to be defeated"),
                    $"deepnorth_stones after the wait: '{line}'");

                // A wait of minutes would only show a spawn when the area's timers (500 and 1000 s) come due. So: room
                // under the caps (3 Jotun killed, they count) and every timer of the place set to "never ran". A mod
                // that still spawned over time, or topped the area up while the player stays, would do it within a second.
                var slain = SlayArmy(pos, cell, 3, null);
                yield return WaitServerKills(cell, slain, 6f);
                var spawner = new Found<SpawnSystem>();
                yield return WaitSpawner(pos, 2f, spawner);
                AreaSpawns.TestResetTimers(spawner.Value, true, true);
                var w2 = new StayWatch();
                yield return WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), cell, clock.Cap(10f), w2);
                c.Check(slain == 3 && spawner.Value != null && HeldCells.TestKills(cell) == 3 && w2.NewJotun == 0 && w2.AlwaysWaiting,
                    $"3 of its Jotun killed ({HeldCells.TestKills(cell)} counted) and every spawn timer of the place due: still nothing new in "
                    + $"{F(w2.Seconds)} s ({w2.NewJotun} new): no spawns over time after Kall, no top-up while the player stays");
            }
        }
        finally
        {
            EndRealKall(hadKall, new[] { cell }, point);
            SweepArea(player);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kall.undo (T12) ----------

    private static IEnumerator RunKallUndo()
    {
        var c = new Checks(KallUndoName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var cell = HeldCells.NoTag;
        var point = Vector3.zero;
        var hadKall = RealKall();
        try
        {
            // Storms always, 2 kills clear the area.
            ServerRules.TestRules = KallRules(100, 2);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotUndo, out point, out cell);
            if (!c.Check(!hadKall && haveArea, "Kall is not defeated in this world and a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f, 1));
                SetRealKall(true);
                yield return Until(() => HeldCells.Scanned && HeldCells.TestServerEngaged(cell), 5f);
                var slain = SlayArmy(pos, cell, 2, null);
                yield return Until(() => HeldCells.IsCleared(cell), 10f);
                yield return Until(() => Storms.Override() == null, 2f);
                c.Check(slain == 2 && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell) && Storms.Override() == null,
                    $"Kall defeated (the real key) and 2 Jotun killed: the area is cleared, written in the world, no blizzard (killed {slain})");

                // removekey defeated_frozenking_p3.
                SetRealKall(false);
                yield return Until(() => !HeldCells.IsCleared(cell) && EnvMan.instance.GetEnvironmentOverride() == blizzard, 4f);
                c.Check(!WorldState.KallDefeated && !HeldCells.IsCleared(cell) && Storms.Override() == blizzard
                        && EnvMan.instance.GetEnvironmentOverride() == blizzard,
                    "the key removed: Kall is not defeated, the area storms again (the game is given the blizzard)");
                c.Check(AreaSpawns.Gate(EntryKind.Jotun, pos) && HeldCells.TestMarkedAnywhere(cell),
                    "it takes Jotun spawns again; its cleared mark stays in the world");
                // New Jotun for real: none of the area's left, its timers as in a place nobody visited (they would be
                // due again after 500 to 1000 s).
                yield return Frames(3);
                DestroyAll(TaggedNear(pos, 400f));
                yield return Frames(3);
                var spawner = new Found<SpawnSystem>();
                yield return WaitSpawner(pos, 4f, spawner);
                AreaSpawns.TestResetTimers(spawner.Value, true, false);
                yield return WaitTagged(pos, 250f, 2, clock.Cap(14f));
                var again = TaggedNear(pos, 250f);
                c.Check(spawner.Value != null && again.Count > 0,
                    $"and it spawns Jotun again over time like before Kall ({again.Count} new: {Kinds(again)})");

                // Defeated again: the mark applies again.
                if (c.Check(clock.Room(9f), "time left to defeat Kall again"))
                {
                    SetRealKall(true);
                    yield return Until(() => HeldCells.Scanned && HeldCells.IsCleared(cell), 5f);
                    yield return Until(() => Storms.Override() == null, 2f);
                    var known = IdsOf(TaggedNear(pos, 400f));
                    var w = new StayWatch();
                    yield return WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), HeldCells.NoTag, clock.Cap(5f), w);
                    c.Check(HeldCells.IsCleared(cell) && !HeldCells.MayBurst(cell) && Storms.Override() == null && w.NewJotun == 0
                            && !AreaSpawns.Gate(EntryKind.Jotun, pos),
                        $"Kall defeated again: the kept mark clears the area again (no blizzard, {w.NewJotun} new Jotun in {F(w.Seconds)} s)");
                }
            }
        }
        finally
        {
            EndRealKall(hadKall, new[] { cell }, point);
            SweepArea(player);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.cleared.return (T12) ----------

    private static IEnumerator RunClearedReturn()
    {
        var c = new Checks(ClearedReturnName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var cell = HeldCells.NoTag;
        var point = Vector3.zero;
        var hadKall = RealKall();
        try
        {
            ServerRules.TestRules = KallRules(100, 2);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotClearedReturn, out point, out cell);
            if (!c.Check(!hadKall && haveArea, "Kall is not defeated in this world and a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            // Kall falls while the player is far from the Deep North.
            SetRealKall(true);
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
            c.Check(WorldState.KallDefeated && HeldCells.Scanned && HeldCells.TestKnown && HeldCells.MayBurst(cell),
                "Kall defeated (the real key): the area lists are there, the area spawns when entered");
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0, clock.Cap(18f, 3));
                yield return WaitTagged(pos, 400f, 1, 3f);
                var slain = SlayArmy(pos, cell, 2, null);
                yield return Until(() => HeldCells.IsCleared(cell), 10f);
                yield return Until(() => Storms.Override() == null && MapOverlay.TestPixel(pos) == 1, 3f);
                c.Check(slain == 2 && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell) && Storms.Override() == null
                        && MapOverlay.TestPixel(pos) == 1,
                    $"2 of its Jotun killed: the area is cleared, no blizzard, off the map (killed {slain}, pixel state {MapOverlay.TestPixel(pos)})");
                yield return new WaitForSeconds(1f);
                var survivors = TaggedNear(pos, 400f);
                var known = IdsOf(survivors);

                // A real trip far away.
                var far = Flat(home - SeedPointOf(cell)).magnitude;
                yield return TeleportAndWait(player, home, TravelTimeout, box);
                away = !box.Ok;
                c.Check(box.Ok && far > HeldCells.ReleaseDistance() + 100f,
                    $"left the area: {F(far)} m from its centre (an area lets go beyond {F(HeldCells.ReleaseDistance())} m)");
                yield return new WaitForSeconds(HeldCells.ReleaseCheckSeconds + 1.5f);
                c.Check(HeldCells.IsCleared(cell) && !HeldCells.MayBurst(cell) && !HeldCells.TestServerEngaged(cell),
                    "far away, a few seconds later: the area is still cleared and will not spawn when entered");

                // And back.
                if (c.Check(clock.Room(10f, 2), "time left to come back"))
                {
                    away = true;
                    yield return TeleportAndWait(player, point, TravelTimeout, box);
                    if (c.Check(box.Ok, "travelled back into the cleared area"))
                    {
                        var storm = false;
                        var w = new StayWatch();
                        var watch = WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), HeldCells.NoTag, clock.Cap(12f), w);
                        while (watch.MoveNext())
                        {
                            storm |= Storms.Override() != null || EnvMan.instance.GetEnvironmentOverride() == blizzard || MeteorOver(cell);
                            yield return watch.Current;
                        }
                        c.Check(w.NewJotun == 0 && w.Seconds >= 9f, $"back in the cleared area: no new Jotun in {F(w.Seconds)} s, past the 10 s a place takes to start its spawn checks ({w.NewJotun} new, {known.Count} were left alive)");
                        c.Check(!storm && HeldCells.IsCleared(cell), "no blizzard and no meteor there (StormShare 100)");
                        yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 1, 6f);
                        c.Check(MapOverlay.TestPixel(pos) == 1, $"no purple on the map there (pixel state {MapOverlay.TestPixel(pos)})");
                        var line = First(RunConsole(AdminCommand.Name));
                        c.Check(line.Contains("is cleared"), $"deepnorth_stones there says the area is cleared: '{line}'");
                    }
                }
            }
        }
        finally
        {
            EndRealKall(hadKall, new[] { cell }, point);
            SweepArea(player);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kall.edge (T20) ----------

    private static IEnumerator RunKallEdge()
    {
        var c = new Checks(KallEdgeName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var edge = default(Edge);
        var haveEdge = false;
        var hadKall = RealKall();
        try
        {
            ServerRules.TestRules = KallRules(0, 60);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            haveEdge = FindEdge(SlotEdge, false, out edge);
            if (!c.Check(!hadKall && haveEdge, "Kall is not defeated in this world and an invaded dry area with dry land of an area that is not invaded across its edge exists"))
            {
                c.Report();
                yield break;
            }
            var cell = edge.Cell;
            SetRealKall(true);
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, edge.Inside, TravelTimeout, box);
            if (c.Check(box.Ok, $"after Kall, travelled to {F(edge.Inside)}, 40 m inside the area {cell}"))
            {
                var pos = player.transform.position;
                yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0, clock.Cap(20f));
                yield return WaitTagged(pos, 400f, 1, 4f);
                c.Check(HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0 && OneSet(TaggedOf(cell)),
                    $"the area has its Jotun and waits to be defeated ({Kinds(TaggedOf(cell))})");
                // The game's own short hold over: only the server's list keeps the area from spawning now.
                yield return new WaitForSecondsRealtime(HeldCells.LocalHoldSeconds + 1f);
                c.Check(HeldCells.IsEngaged(cell) && !HeldCells.MayBurst(cell), $"{F(HeldCells.LocalHoldSeconds + 1f)} s later it still waits (the server's list)");
                var known = IdsOf(TaggedNear(pos, 400f));
                var before = TaggedOf(cell).Count;

                // Out over the edge, 65 m, not far.
                yield return Hop(player, edge.Outside, box);
                var outPos = player.transform.position;
                var gap = Flat(outPos - SeedPointOf(cell)).magnitude;
                if (c.Check(box.Ok && WorldState.CellAt(outPos) != cell && gap < HeldCells.ReleaseDistance() - 100f,
                        $"walked out over the edge of the area to {F(outPos)} ({F(gap)} m from its centre; it lets go beyond {F(HeldCells.ReleaseDistance())} m)"))
                {
                    var w = new StayWatch();
                    yield return WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), cell, 2f * HeldCells.ReleaseCheckSeconds + 1f, w);
                    c.Check(w.AlwaysWaiting && w.NewJotun == 0, $"outside for {F(w.Seconds)} s: the area still waits to be defeated, nothing new ({w.NewJotun})");
                }

                // And back in.
                yield return Hop(player, edge.Inside, box);
                if (c.Check(box.Ok && WorldState.CellAt(player.transform.position) == cell, "walked back into the area"))
                {
                    var w = new StayWatch();
                    yield return WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), cell, clock.Cap(7f), w);
                    c.Check(w.NewJotun == 0 && w.AlwaysWaiting && TaggedOf(cell).Count <= before,
                        $"back in without having gone far: nothing new in {F(w.Seconds)} s ({w.NewJotun} new Jotun, {TaggedOf(cell).Count} of the area now, {before} before)");
                    var line = First(RunConsole(AdminCommand.Name));
                    c.Check(line.Contains("waits to be defeated"), $"deepnorth_stones there: '{line}'");
                }
            }
        }
        finally
        {
            EndRealKall(hadKall, new[] { haveEdge ? edge.Cell : HeldCells.NoTag }, edge.Inside);
            SweepArea(player);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kall.nextdoor (T20) ----------

    private static IEnumerator RunKallNextDoor()
    {
        var c = new Checks(KallNextDoorName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var a = default(Land);
        var b = default(Land);
        var havePair = false;
        var hadKall = RealKall();
        try
        {
            ServerRules.TestRules = KallRules(0, 60);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            havePair = FindNeighbours(SlotNextDoor, out a, out b);
            if (!c.Check(!hadKall && havePair, "Kall is not defeated in this world and two invaded dry areas side by side exist"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, a.Point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {a.Cell} at {F(a.Point)}; the area {b.Cell} lies next to it, {F(Flat(b.Point - a.Point).magnitude)} m away"))
            {
                var pos = player.transform.position;
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f, 2));
                var nextDoor = TaggedOf(b.Cell).Count;
                c.Check(TaggedOf(a.Cell).Count > 0 && nextDoor == 0,
                    $"before Kall: Jotun of this area around the player ({Kinds(TaggedOf(a.Cell))}), none of the area next door loaded ({nextDoor})");

                // Kall falls while the player stands here.
                SetRealKall(true);
                yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown && HeldCells.TestServerEngaged(a.Cell), 5f);
                c.Check(HeldCells.TestServerEngaged(a.Cell) && !HeldCells.TestServerEngaged(b.Cell) && HeldCells.MayBurst(b.Cell),
                    "Kall defeated (the real key): this area waits to be defeated, the one next door does not: it spawns when entered");

                // Walk into the area next door.
                yield return TeleportAndWait(player, b.Point, TravelTimeout, box);
                if (c.Check(box.Ok && WorldState.CellAt(player.transform.position) == b.Cell, $"travelled into the area next door at {F(b.Point)}"))
                {
                    var there = player.transform.position;
                    yield return Until(() => HeldCells.TestServerEngaged(b.Cell) && TaggedOf(b.Cell).Count > 0, clock.Cap(20f));
                    yield return WaitTagged(there, 400f, 1, clock.Cap(4f));
                    var got = TaggedOf(b.Cell);
                    c.Check(HeldCells.TestServerEngaged(b.Cell) && got.Count > 0 && OneSet(got),
                        $"the area next to where the player stood when Kall fell has its Jotun as soon as the player walks in, one set ({Kinds(got)})");
                    c.Check(HeldCells.TestServerEngaged(a.Cell),
                        $"the first area, {F(Flat(there - SeedPointOf(a.Cell)).magnitude)} m away, still waits to be defeated (it lets go beyond {F(HeldCells.ReleaseDistance())} m)");
                }
            }
        }
        finally
        {
            EndRealKall(hadKall, havePair ? new[] { a.Cell, b.Cell } : new int[0], a.Point, b.Point);
            SweepArea(player);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kall.death (T20) ----------

    // First bed the game has (a prefab with Bed and Piece), by scanning: no name typed by hand.
    private static GameObject BedPrefab()
    {
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab != null && prefab.GetComponent<Bed>() != null && prefab.GetComponent<Piece>() != null && prefab.GetComponent<ZNetView>() != null)
            {
                return prefab;
            }
        }
        return null;
    }

    // The profile has no door to forget a death point: its flag through reflection (the world data type is private).
    // Not found = the point stays set on the throwaway character.
    private static void ClearDeathPoint(PlayerProfile profile)
    {
        try
        {
            var data = AccessTools.Method(typeof(PlayerProfile), "GetWorldData").Invoke(profile, new object[] { ZNet.instance.GetWorldUID() });
            AccessTools.Field(data.GetType(), "m_haveDeathPoint").SetValue(data, false);
        }
        catch (Exception)
        {
            // Me leave it.
        }
    }

    // A real death (god mode off, a killing hit) with the player's bed inside the area, and the respawn there. The death
    // is made cheap for the test with the game's own means, all put back: the world modifier "keep the inventory" (no
    // tombstone) and the soft-death window (no skill loss); the death tutorial counts as seen (no raven).
    private static IEnumerator RunKallDeath()
    {
        var c = new Checks(KallDeathName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var cell = HeldCells.NoTag;
        var point = Vector3.zero;
        var hadKall = RealKall();
        var profile = Game.instance.GetPlayerProfile();
        // Spawn point, death point and home point of this character in this world (the profile's own doors).
        var hadSpawn = profile.HaveCustomSpawnPoint();
        var oldSpawn = profile.GetCustomSpawnPoint();
        var hadDeath = profile.HaveDeathPoint();
        var oldDeath = profile.GetDeathPoint();
        var oldHome = profile.GetHomePoint();
        var sinceDeath = player.m_timeSinceDeath;
        var zs = ZoneSystem.instance;
        var keptInventory = zs.GetGlobalKey(GlobalKeys.DeathKeepInventory);
        var sawTutorial = player.m_shownTutorials.Contains("death");
        var deathStats = new Dictionary<PlayerStatType, float>[profile.m_playerStats.Length];
        for (var i = 0; i < deathStats.Length; i++)
        {
            deathStats[i] = new Dictionary<PlayerStatType, float>();
            foreach (var pair in profile.m_playerStats[i].m_stats)
            {
                if (pair.Key.ToString().StartsWith("Death", StringComparison.Ordinal) || pair.Key == PlayerStatType.ConsecutiveDaysSurvived)
                {
                    deathStats[i][pair.Key] = pair.Value;
                }
            }
        }
        var pinsBefore = new HashSet<Minimap.PinData>(Minimap.instance != null ? Minimap.instance.m_pins : new List<Minimap.PinData>());
        GameObject bedObject = null;
        var died = false;
        try
        {
            ServerRules.TestRules = KallRules(0, 60);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotDeath, out point, out cell);
            var bedPrefab = BedPrefab();
            if (!c.Check(!hadKall && haveArea && bedPrefab != null, "Kall is not defeated in this world, a dry invaded Deep North area exists and the game has a bed"))
            {
                c.Report();
                yield break;
            }
            SetRealKall(true);
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"after Kall, travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0, clock.Cap(20f, 1));
                yield return WaitTagged(pos, 400f, 1, 4f);
                c.Check(HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0, $"the area has its Jotun and waits to be defeated ({Kinds(TaggedOf(cell))})");

                // The player's bed, inside the area: claimed and set as spawn point (the two things the game does when a
                // player uses a bed under a roof).
                // On the ground itself (a piece in the air has no support and breaks).
                var bedAt = pos + new Vector3(2.5f, 0f, 0f);
                if (zs.GetGroundHeight(bedAt, out var bedGround))
                {
                    bedAt.y = bedGround;
                }
                bedObject = Object.Instantiate(bedPrefab, bedAt, Quaternion.identity);
                yield return Frames(3);
                var bed = bedObject != null ? bedObject.GetComponent<Bed>() : null;
                if (c.Check(bed != null, $"a bed ({bedPrefab.name}) stands in the area"))
                {
                    bed.SetOwner(profile.GetPlayerID(), profile.GetName());
                    profile.SetCustomSpawnPoint(bed.GetSpawnPoint());
                    yield return Frames(3);
                    var spawnAt = bed.GetSpawnPoint();
                    c.Check(bed.IsCurrent() && WorldState.CellAt(spawnAt) == cell, "it is the player's bed and spawn point, inside the area");

                    var known = IdsOf(TaggedNear(pos, 400f));
                    var before = TaggedOf(cell).Count;
                    // Cheap death: inventory kept (no tombstone), soft death (no skill loss), no raven.
                    if (!keptInventory)
                    {
                        zs.SetGlobalKey(GlobalKeys.DeathKeepInventory);
                    }
                    player.m_timeSinceDeath = 0f;
                    player.m_shownTutorials.Add("death");
                    player.SetGodMode(false);
                    died = true;
                    player.Damage(new HitData(1E+10f));

                    // Dead, then the game's own respawn (10 s, then the place loads): the area is watched all the way.
                    var newJotun = 0;
                    var waiting = true;
                    var dead = false;
                    var t0 = Time.realtimeSinceStartup;
                    var end = t0 + clock.Cap(45f);
                    Player back = null;
                    while (Time.realtimeSinceStartup < end)
                    {
                        var now = Player.m_localPlayer;
                        dead |= now == null || now.IsDead();
                        newJotun = Mathf.Max(newJotun, CountIds(TaggedNear(pos, 400f), known, false));
                        waiting &= HeldCells.TestServerEngaged(cell);
                        if (dead && now != null && !ReferenceEquals(now, player) && !now.IsDead() && !Game.instance.WaitingForRespawn()
                            && !now.IsTeleporting() && ZNetScene.instance.IsAreaReady(now.transform.position))
                        {
                            back = now;
                            break;
                        }
                        yield return new WaitForSeconds(0.25f);
                    }
                    var took = Time.realtimeSinceStartup - t0;
                    if (c.Check(dead && back != null, $"the player died and respawned {F(took)} s later"))
                    {
                        player = back;
                        c.Check(Flat(player.transform.position - spawnAt).magnitude < 10f && WorldState.CellAt(player.transform.position) == cell,
                            $"respawned at the bed inside the area ({F(Flat(player.transform.position - spawnAt).magnitude)} m from it)");
                        c.Check(waiting && newJotun == 0,
                            $"while dead and while respawning the area kept waiting to be defeated ({waiting}) and nothing new appeared ({newJotun})");
                        var w = new StayWatch();
                        yield return WatchStay(() => CountIds(TaggedNear(pos, 400f), known, false), cell, clock.Cap(8f), w);
                        c.Check(w.NewJotun == 0 && w.AlwaysWaiting && TaggedOf(cell).Count <= before && !HeldCells.MayBurst(cell),
                            $"{F(w.Seconds)} s after the respawn: nothing new ({w.NewJotun} new Jotun, {TaggedOf(cell).Count} of the area now, {before} before the death)");
                    }
                }
            }
        }
        finally
        {
            // The player object of now (a respawn made a new one).
            var now = Player.m_localPlayer;
            if (died && now != null)
            {
                now.SetGodMode(true);
                now.m_timeSinceDeath = sinceDeath;
                if (!sawTutorial)
                {
                    now.m_shownTutorials.Remove("death");
                }
            }
            if (died)
            {
                if (!keptInventory)
                {
                    zs.RemoveGlobalKey(GlobalKeys.DeathKeepInventory);
                }
                if (hadDeath)
                {
                    profile.SetDeathPoint(oldDeath);
                }
                else
                {
                    ClearDeathPoint(profile);
                }
                profile.SetHomePoint(oldHome);
                for (var i = 0; i < deathStats.Length; i++)
                {
                    var stats = profile.m_playerStats[i].m_stats;
                    foreach (var key in new List<PlayerStatType>(stats.Keys))
                    {
                        if (key.ToString().StartsWith("Death", StringComparison.Ordinal) || key == PlayerStatType.ConsecutiveDaysSurvived)
                        {
                            if (deathStats[i].TryGetValue(key, out var old))
                            {
                                stats[key] = old;
                            }
                            else
                            {
                                stats.Remove(key);
                            }
                        }
                    }
                }
                var map = Minimap.instance;
                if (map != null)
                {
                    foreach (var pin in new List<Minimap.PinData>(map.m_pins))
                    {
                        if (pin != null && pin.m_type == Minimap.PinType.Death && !pinsBefore.Contains(pin))
                        {
                            map.RemovePin(pin);
                        }
                    }
                }
            }
            if (hadSpawn)
            {
                profile.SetCustomSpawnPoint(oldSpawn);
            }
            else
            {
                profile.ClearCustomSpawnPoint();
            }
            Kill(bedObject);
            EndRealKall(hadKall, new[] { cell }, point);
            SweepArea(now);
        }
        // The trip home with the player of now.
        player = Player.m_localPlayer;
        if (away && player != null)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }
}
#endif
