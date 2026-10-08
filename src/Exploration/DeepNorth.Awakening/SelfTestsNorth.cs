#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Later single-player self tests that travel to the Deep North and back (tools in
// SelfTestsTools.cs; each one its own area, never the one of dn.area):
//   dn.dormant      stage 0 in a place that is invaded later: nothing from the mod (no area Jotun, no band, no blizzard,
//                   no meteor) with storms and bands forced to 100 %; the console says 0 broken, stage 0; then stage 1
//                   in that same place (not one of the first areas): still quiet
//   dn.stage1       an area invaded at stage 1: both Krigen, Hexen and Elaking around the player under the caps, by day
//                   and at night, none hunting; no circle, pin or event for the area, no Malicious Ice in it, awake
//                   whatever the world time
//   dn.storm        blizzard within seconds, meteors never near the player, StormShare 0 = none, Meteors off =
//                   blizzard only, the default schedule of this area over a day, no blizzard past the Deep North line
//   dn.bands        a nature band through the area's own check (timer, chance): size, place, together, no second one,
//                   it fights the Jotun; chance 0 = none; default 10 % over many checks; after Kall still in an area
//                   not cleared, never in a cleared one
//   dn.kallfight    Kall falls while the player stands in an area: waits to be defeated, real deaths count (also a
//                   Jotun that came from another area), nature never counts, half = weakening, target = "The Jotun
//                   Retreat", cleared for good; Kall undone = as before, Kall again = cleared again
//   dn.kallreturn   after Kall: leave the area for real (travel), come back: topped up once, kills kept
//   dn.kallrestart  after Kall: area lists forgotten like at a restart: kills read back, area topped up; a cleared
//                   area stays cleared
//   dn.oldworld     a Morkhalla whose Malicious Ice went away without the mod: counted when the mod meets the world
//                   (no banner, no invasion); key removed mid-game = counted again within a second; next break goes on
//   dn.toggle       mod turned off in the area (framework switch, never the Enabled setting): weather, spawns, fights,
//                   stones, map and command like the normal game; on again: all back, count goes on
//   dn.bug.caps     real bug, own small test: a fresh fill of an area can go over the numbers the README gives (one
//                   Krigen or two Elaking more: the game's last group of a pass)
internal static partial class SelfTests
{
    private static string CurrentEnv()
    {
        var env = EnvMan.instance;
        var current = env != null ? env.GetCurrentEnvironment() : null;
        return current != null ? current.m_name : "";
    }

    private static Vector3 SeedPointOf(int cell)
    {
        Cells.FromId(cell, out var i, out var j);
        Cells.SeedPoint(WorldState.Seed, i, j, out var x, out var z);
        return new Vector3(x, 0f, z);
    }

    // Tagged area Jotun of a cell, loaded and alive.
    private static List<Character> TaggedOf(int cell)
    {
        var list = new List<Character>();
        foreach (var ch in Character.GetAllCharacters())
        {
            if (ch != null && !ch.IsDead() && TagOf(ch) == cell)
            {
                list.Add(ch);
            }
        }
        return list;
    }

    // Slay up to count Jotun army creatures standing in the cell, nearest the point first; returns how many.
    private static int SlayArmy(Vector3 center, int cell, int count, Character spare, float radius = 90f)
    {
        var pool = ArmyNear(center, radius, cell);
        pool.Sort((a, b) => Flat(a.transform.position - center).sqrMagnitude.CompareTo(Flat(b.transform.position - center).sqrMagnitude));
        var n = 0;
        foreach (var ch in pool)
        {
            if (n >= count)
            {
                break;
            }
            if (ch == spare)
            {
                continue;
            }
            Slay(ch);
            n++;
        }
        return n;
    }

    private static IEnumerator WaitServerKills(int cell, int count, float seconds) =>
        Until(() => HeldCells.TestKills(cell) >= count || HeldCells.TestServerCleared(cell), seconds);

    private static void SpawnRing(string prefab, Vector3 center, int count, float radius, List<GameObject> spawned)
    {
        for (var k = 0; k < count; k++)
        {
            var dir = Quaternion.Euler(0f, k * (360f / Mathf.Max(1, count)), 0f) * Vector3.forward;
            spawned.Add(Spawn(prefab, center + dir * (radius + k % 3 * 3f), -dir));
        }
    }

    // What every after-Kall test puts back: no test area left in the world, no forced state, server lists fresh.
    private static void EndKall(params int[] cells)
    {
        foreach (var cell in cells)
        {
            if (cell != HeldCells.NoTag)
            {
                HeldCells.TestUnmarkEverywhere(cell);
            }
        }
        ClearOverrides();
        HeldCells.TestRescan();
        HeldCells.TestClearEngaged();
    }

    private static void KillAll(List<GameObject> spawned)
    {
        foreach (var go in spawned)
        {
            Kill(go);
        }
        spawned.Clear();
    }

    // Area creatures a test leaves around the player go with it (after the forced state is gone: never a kill count).
    private static void SweepArea(Player player)
    {
        if (player == null)
        {
            return;
        }
        DestroyAll(TaggedNear(player.transform.position, 500f));
        DestroyAll(BandLoaded());
    }

    // ---------- dn.dormant ----------

    private sealed class Quiet
    {
        internal int NewTagged;
        internal int NewBand;
        internal bool Storm;
        internal bool Meteor;
        internal int Army;
    }

    // Watch a place: creatures that appear with an area tag or a band mark, a blizzard from the mod, meteors.
    private static IEnumerator WatchQuiet(Player player, float seconds, HashSet<Character> known, Quiet q)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            var here = player.transform.position;
            q.Army = 0;
            foreach (var ch in Character.GetAllCharacters())
            {
                var zdo = ZdoOf(ch);
                if (zdo == null || Flat(ch.transform.position - here).magnitude > 250f)
                {
                    continue;
                }
                if (Hostility.IsArmy(zdo.GetPrefab()))
                {
                    q.Army++;
                }
                if (known.Contains(ch))
                {
                    continue;
                }
                if (zdo.GetInt(HeldCells.TagKey, HeldCells.NoTag) != HeldCells.NoTag)
                {
                    q.NewTagged++;
                    known.Add(ch);
                }
                else if (zdo.GetInt(AreaSpawns.BandKey, 0) != 0)
                {
                    q.NewBand++;
                    known.Add(ch);
                }
            }
            if (Storms.Override() != null)
            {
                q.Storm = true;
            }
            if (Meteors().Count > 0)
            {
                q.Meteor = true;
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    private static IEnumerator RunDormant()
    {
        var c = new Checks(DormantName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        try
        {
            // The real world first: a new world reads 0 broken (the mod counted it at load).
            WorldState.Refresh();
            var realZero = WorldState.HasStonesKey && WorldState.Stones == 0;
            c.Check(realZero, $"a new world has its count saved as 0 (key there {WorldState.HasStonesKey}, {WorldState.Stones} broken)");
            // Storms and bands forced to always: a north that is awake by mistake would show at once.
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 100;
                r.NatureBandChance = 100;
            });
            if (!realZero)
            {
                WorldState.TestStones = 0;
            }
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(QuietLand(out var point, out var cell),
                    "found a dry Deep North area that is invaded at stage 3 and still asleep at stage 1, with no stage 1 area around"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var known = new HashSet<Character>(Character.GetAllCharacters());
                var q0 = new Quiet();
                yield return WatchQuiet(player, 16f, known, q0);
                c.Check(WorldState.Stage == 0 && q0.NewTagged == 0 && q0.NewBand == 0,
                    $"stage 0, 16 s in the Deep North: no area Jotun and no nature band appear ({q0.NewTagged} Jotun, {q0.NewBand} band members)");
                c.Check(!q0.Storm && !q0.Meteor && Storms.Override() == null,
                    $"stage 0: no blizzard from the mod, no meteor (blizzard {q0.Storm}, meteor {q0.Meteor}; weather {CurrentEnv()})");
                c.Check(!AreaSpawns.Gate(EntryKind.Jotun, pos) && !AreaSpawns.Gate(EntryKind.Nature, pos)
                        && !AreaSpawns.Gate(EntryKind.Meteor, pos) && MapOverlay.TestPixel(pos) != 2,
                    "stage 0: the place takes no area spawn and is not purple on the map");
                c.Note($"Jotun army creatures of the normal game within 250 m: {q0.Army}");
                var line = First(RunConsole(AdminCommand.Name));
                c.Check(line.StartsWith("Deep North: 0 Malicious Ice broken, stage 0", StringComparison.Ordinal),
                    $"deepnorth_stones says 0 broken, stage 0: '{line}'");

                // Stage 1: this place is not one of the first areas. It stays quiet.
                WorldState.TestStones = 1;
                WorldState.Refresh();
                AreaSpawns.TestInvalidate();
                WorldState.CountAwake(ServerRules.Current, out var awake, out var total);
                c.Check(WorldState.Stage == 1 && awake > 0 && !WorldState.IsAwakeCell(cell, ServerRules.Current),
                    $"stage 1: {awake} of {total} areas are invaded, this one is not");
                var q1 = new Quiet();
                yield return WatchQuiet(player, clock.Cap(10f), known, q1);
                c.Check(q1.NewTagged == 0 && q1.NewBand == 0 && !q1.Storm && !q1.Meteor,
                    $"stage 1 in a place that is not invaded: still quiet ({q1.NewTagged} Jotun, {q1.NewBand} band members, "
                    + $"blizzard {q1.Storm}, meteor {q1.Meteor})");
                c.Check(MapOverlay.TestPixel(pos) != 2 && !AreaSpawns.Gate(EntryKind.Jotun, pos),
                    "stage 1: the quiet place takes no area spawn and is not purple on the map");
            }
        }
        finally
        {
            ClearOverrides();
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.stage1 ----------

    private static int EventPins(Minimap map)
    {
        var n = 0;
        foreach (var pin in map.m_pins)
        {
            if (pin != null && (pin.m_type == Minimap.PinType.EventArea || pin.m_type == Minimap.PinType.RandomEvent))
            {
                n++;
            }
        }
        return n;
    }

    private static bool IsMorkhalla(ZoneSystem.LocationInstance li) =>
        li.m_location != null && string.Equals(li.m_location.m_prefabName, Stones.MorkhallaPrefab, StringComparison.OrdinalIgnoreCase);

    private static IEnumerator RunStage1()
    {
        var c = new Checks(Stage1Name);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        var env = EnvMan.instance;
        var envSaved = false;
        var savedDebug = false;
        var savedDebugTime = 0.5f;
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
            var map = Minimap.instance;
            var pes = PersistentEventSystem.instance;
            var zs = ZoneSystem.instance;
            if (!c.Check(Stage1Land(out var point, out var cell) && map != null && env != null && pes != null,
                    "a dry area invaded at stage 1 exists"))
            {
                c.Report();
                yield break;
            }
            var eventPins = EventPins(map);
            var eventMarks = map.m_persistentEventPins.Count;
            var pinsBefore = new HashSet<Minimap.PinData>(map.m_pins);
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the stage 1 area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var rules = ServerRules.Current;
                var spawner = new Found<SpawnSystem>();
                yield return WaitSpawner(pos, 12f, spawner);
                c.Check(spawner.Value != null && WorldState.CellAt(pos) == cell, "this game owns the zone control in the area");
                // A full first pass, like in a place never visited (also when an earlier pass already ran here).
                DestroyAll(TaggedNear(pos, 400f));
                yield return Frames(3);
                AreaSpawns.TestResetTimers(spawner.Value, true, false);
                yield return WaitTagged(pos, 250f, 4, clock.Cap(25f));
                var first = TaggedNear(pos, 250f);
                var wrongKind = 0;
                var asleep = 0;
                var hunters = 0;
                var names = new List<string>();
                foreach (var ch in first)
                {
                    if (!Hostility.IsArmy(Hostility.PrefabOf(ch)))
                    {
                        wrongKind++;
                    }
                    if (!Cells.IsAwake(WorldState.Seed, TagOf(ch), rules.Coverage(1)))
                    {
                        asleep++;
                    }
                    var ai = ch.GetComponent<BaseAI>();
                    if (ai != null && ai.HuntPlayer())
                    {
                        hunters++;
                    }
                    names.Add($"{ch.name.Replace("(Clone)", "")} lvl {ch.GetLevel()}");
                }
                c.Note("area creatures: " + string.Join(", ", names.ToArray()));
                c.Check(first.Count > 0 && wrongKind == 0, $"stage 1: Jotun appear around the player in an invaded area ({first.Count}: {Kinds(first)})");
                c.Check(CountPrefab(first, Hostility.Krigen) > 0 && CountPrefab(first, Hostility.KrigenDual) > 0
                        && CountPrefab(first, Hostility.Hexen) > 0 && CountPrefab(first, Hostility.Elaking) > 0,
                    $"both kinds of Krigen, Hexen and Elaking are there ({Kinds(first)})");
                // The exact numbers of the README are dn.bug.caps' own check: one pass of the game can go a group over.
                c.Check(OneSet(first) && first.Count <= SetMost(),
                    $"about a dozen, one set of the area: 3 Krigen, 2 dual-axe Krigen, 2 Hexen, 6 Elaking, at most one last group more ({Kinds(first)})");
                c.Check(asleep == 0, $"every one came from an area invaded at stage 1 ({asleep} did not)");
                c.Check(hunters == 0, $"none of them hunts the player from afar ({hunters} hunt)");
                var entriesOk = true;
                foreach (var name in Hostility.Army)
                {
                    var e = AreaSpawns.TestEntry(name);
                    entriesOk &= e != null && e.m_spawnAtDay && e.m_spawnAtNight && !e.m_huntPlayer
                                 && Mathf.Approximately(e.m_overrideLevelupChance, rules.StarChance(1));
                }
                c.Check(entriesOk, "the four Jotun entries spawn by day and at night, never as hunters, with the stage 1 star chance (10 %)");

                // The other half of the day (the game's own debug clock, put back in finally): they come too.
                var wasDay = EnvMan.IsDay();
                savedDebug = env.m_debugTimeOfDay;
                savedDebugTime = env.m_debugTime;
                envSaved = true;
                env.m_debugTimeOfDay = true;
                env.m_debugTime = wasDay ? 0f : 0.5f;
                yield return Until(() => EnvMan.IsDay() != wasDay, 4f);
                var flipped = EnvMan.IsDay() != wasDay;
                DestroyAll(TaggedNear(pos, 400f));
                yield return Frames(3);
                AreaSpawns.TestResetTimers(spawner.Value, true, false);
                yield return WaitTagged(pos, 250f, 1, clock.Cap(12f));
                var second = TaggedNear(pos, 250f);
                c.Check(flipped && second.Count > 0,
                    $"area Jotun come {(wasDay ? "by day" : "at night")} ({first.Count}) and {(wasDay ? "at night" : "by day")} ({second.Count})");
                env.m_debugTimeOfDay = savedDebug;
                env.m_debugTime = savedDebugTime;
                envSaved = false;

                // Hidden (T07): nothing marks the area but the tint; no Malicious Ice in it; it does not end with time.
                yield return new WaitForSeconds(1.2f);
                c.Check(EventPins(map) == eventPins && map.m_persistentEventPins.Count == eventMarks
                        && eventMarks == pes.m_activePersistentEvents.list.Count && pes.GetActiveEvent(pos) == null,
                    $"no circle, no marker and no event for the area on the map ({EventPins(map)} event pins, {eventPins} before the trip)");
                // Any pin that came during the visit within 600 m, but the game's own of other things (location icons,
                // players, pings, shouts, death, bed), would mark the area.
                var newPins = new List<string>();
                var areaPins = 0;
                foreach (var pin in map.m_pins)
                {
                    if (pin == null || pinsBefore.Contains(pin) || Flat(pin.m_pos - pos).magnitude >= 600f)
                    {
                        continue;
                    }
                    newPins.Add(pin.m_type.ToString());
                    if (!map.m_locationPins.ContainsValue(pin) && !map.m_playerPins.Contains(pin) && !map.m_pingPins.Contains(pin)
                        && !map.m_shoutPins.Contains(pin) && pin != map.m_deathPin && pin != map.m_spawnPointPin)
                    {
                        areaPins++;
                    }
                }
                c.Note("other pins the game added within 600 m during the visit: " + (newPins.Count > 0 ? string.Join(", ", newPins.ToArray()) : "none"));
                c.Check(areaPins == 0, $"no pin of any kind was added for the area during the visit ({areaPins} new within 600 m that are not the game's own)");
                c.Check(MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2,
                    $"the only sign of the area on the map is its purple tint (pixel state {MapOverlay.TestPixel(pos)} under the player)");
                var stray = 0;
                foreach (var z in Stones.AllZdos(Stones.StonePrefab))
                {
                    var p = z.GetPosition();
                    if (Flat(p - pos).magnitude > 500f)
                    {
                        continue;
                    }
                    var atMorkhalla = false;
                    foreach (var li in zs.m_locationInstances.Values)
                    {
                        if (IsMorkhalla(li) && Flat(li.m_position - p).magnitude <= Stones.StoneSearchRadius)
                        {
                            atMorkhalla = true;
                            break;
                        }
                    }
                    if (!atMorkhalla)
                    {
                        stray++;
                    }
                }
                c.Check(stray == 0 && AreaSpawns.TestEntry(Stones.StonePrefab) == null,
                    $"no Malicious Ice in the area but those of a Morkhalla ({stray} within 500 m)");
                var net = ZNet.instance;
                var now = net.m_netTime;
                net.m_netTime = now + 30.0 * 24.0 * 3600.0;
                var later = WorldState.IsAwakeCell(cell, rules) && AreaSpawns.Gate(EntryKind.Jotun, pos);
                net.m_netTime = now;
                c.Check(later && WorldState.IsAwakeCell(cell, rules) && AreaSpawns.Gate(EntryKind.Jotun, pos),
                    "the area never ends by itself: still invaded with the world clock a month later");
            }
        }
        finally
        {
            if (envSaved && env != null)
            {
                env.m_debugTimeOfDay = savedDebug;
                env.m_debugTime = savedDebugTime;
            }
            ClearOverrides();
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.storm ----------

    // A point past the Deep North line that still belongs to an invaded area reaching over it (dry land first).
    private static bool BorderPoint(float coverage, out Vector3 point, out int cell, out bool dry)
    {
        point = Vector3.zero;
        cell = HeldCells.NoTag;
        dry = false;
        var wg = WorldGenerator.instance;
        if (wg == null)
        {
            return false;
        }
        var seed = WorldState.Seed;
        var found = false;
        for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
        {
            for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
            {
                if (!Cells.IsAwake(seed, i, j, coverage))
                {
                    continue;
                }
                var id = Cells.Id(i, j);
                Cells.SeedPoint(seed, i, j, out var sx, out var sz);
                for (var a = 0; a < 360; a += 15)
                {
                    for (var d = 40f; d <= 300f; d += 20f)
                    {
                        var px = sx + Mathf.Sin(a * Mathf.Deg2Rad) * d;
                        var pz = sz + Mathf.Cos(a * Mathf.Deg2Rad) * d;
                        if (WorldGenerator.IsDeepnorth(px, pz) || px * px + pz * pz > 10000f * 10000f || Cells.At(seed, px, pz) != id)
                        {
                            continue;
                        }
                        var h = wg.GetHeight(px, pz);
                        if (h >= 32f)
                        {
                            point = new Vector3(px, h, pz);
                            cell = id;
                            dry = true;
                            return true;
                        }
                        if (!found)
                        {
                            found = true;
                            point = new Vector3(px, Mathf.Max(h, 30f), pz);
                            cell = id;
                        }
                    }
                }
            }
        }
        return found;
    }

    private static AwakeningRules StormRules(int share, bool meteors) => Rules(r =>
    {
        r.StormShare = share;
        r.Meteors = meteors;
        r.NatureBandChance = 0;
    });

    private static IEnumerator RunStorm()
    {
        var c = new Checks(StormName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        try
        {
            ServerRules.TestRules = StormRules(100, true);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(LandSlot(SlotStorm, out var point, out var cell), "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                var t0 = Time.realtimeSinceStartup;
                yield return Until(() => Storms.Override() == blizzard && CurrentEnv() == blizzard, 6f);
                c.Check(blizzard != null && Storms.Override() == blizzard && CurrentEnv() == blizzard,
                    $"StormShare 100: the Deep North blizzard blows {F(Time.realtimeSinceStartup - t0)} s after arriving (weather {CurrentEnv()})");

                // Meteors: where each one is first seen, against the player who stands still.
                var seen = new HashSet<int>();
                var nearest = float.MaxValue;
                var end = Time.realtimeSinceStartup + clock.Cap(22f, 2);
                while (Time.realtimeSinceStartup < end && seen.Count < 3)
                {
                    foreach (var m in Meteors())
                    {
                        if (!seen.Add(m.GetInstanceID()))
                        {
                            continue;
                        }
                        var under = m.transform.position;
                        under.y = ZoneSystem.instance.GetGroundHeight(under, out var h) ? h : WorldGenerator.instance.GetHeight(under.x, under.z);
                        nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, under));
                    }
                    yield return new WaitForSeconds(0.1f);
                }
                c.Check(seen.Count > 0 && nearest >= 36f,
                    $"meteors fall around the player, never near: {seen.Count} seen, the nearest aimed {F(nearest)} m away (the game keeps 40 m)");
                var prefab = ZNetScene.instance.GetPrefab(AreaSpawns.MeteorPrefab);
                var projectile = prefab != null ? prefab.GetComponent<Projectile>() : null;
                if (projectile != null)
                {
                    var onHit = projectile.m_spawnOnHit != null ? projectile.m_spawnOnHit.GetComponentInChildren<Aoe>(true) : null;
                    c.Note($"meteor prefab: projectile damage {F(projectile.m_damage.GetTotalDamage())}, area on hit "
                           + (onHit != null ? F(onHit.m_damage.GetTotalDamage()) : "none"));
                }

                // StormShare 0: never; back to 100 with meteors off: blizzard only.
                ServerRules.TestRules = StormRules(0, true);
                yield return Until(() => Storms.Override() == null, 2f);
                c.Check(Storms.Override() == null && !AreaSpawns.Gate(EntryKind.Meteor, pos), "StormShare 0: no blizzard from the mod within a second, no meteor");
                ServerRules.TestRules = StormRules(100, false);
                AreaSpawns.TestInvalidate();
                yield return Until(() => Storms.Override() == blizzard, 2f);
                c.Check(Storms.Override() == blizzard && !AreaSpawns.Gate(EntryKind.Meteor, pos),
                    "StormShare 100 again: the blizzard is back within a second; Meteors off: blizzard only");

                // Default settings: this area's own storm plan over the next day (the same on every game).
                var d = AwakeningRules.Default;
                d.StormSeconds(out var min, out var max);
                var from = ZNet.instance.GetTimeSeconds();
                var seedNow = WorldState.Seed;
                var on = 0;
                var run = 0;
                var calm = 0;
                var longestCalm = 0;
                var runs = 0;
                var shortRuns = 0;
                var longRuns = 0;
                var firstRun = true;
                const int steps = 8640;
                for (var k = 0; k < steps; k++)
                {
                    if (Cells.IsStorming(seedNow, cell, from + k * 10.0, d.StormShare / 100f, min, max))
                    {
                        on++;
                        run++;
                        calm = 0;
                        continue;
                    }
                    calm++;
                    longestCalm = Mathf.Max(longestCalm, calm);
                    if (run > 0)
                    {
                        if (!firstRun)
                        {
                            runs++;
                            if (run * 10 < min - 10f)
                            {
                                shortRuns++;
                            }
                            if (run * 10 > 2f * max + 10f)
                            {
                                longRuns++;
                            }
                        }
                        firstRun = false;
                        run = 0;
                    }
                }
                var uptime = (float)on / steps;
                c.Check(uptime >= 0.25f && uptime <= 0.42f && runs >= 30 && shortRuns == 0 && longRuns == 0 && longestCalm * 10 <= 3000,
                    $"default settings: over a day this area storms {F(uptime * 100f)} % of the time in {runs} storms of 5 to 10 minutes "
                    + $"({shortRuns} shorter, {longRuns} longer than two in a row), never calm longer than {longestCalm * 10 / 60} minutes");

                // Past the Deep North line, in an invaded area that reaches over it: no blizzard.
                if (c.Check(clock.Room(TripSeconds + 3f) && BorderPoint(DesignCoverage(3), out var border, out var borderCell, out var dry),
                        "time left and a place past the Deep North line inside an invaded area"))
                {
                    BorderPoint(DesignCoverage(3), out border, out borderCell, out dry);
                    ServerRules.TestRules = StormRules(100, true);
                    yield return TeleportAndWait(player, border, TravelTimeout, box);
                    if (c.Check(box.Ok, $"travelled to {F(border)} ({(dry ? "land" : "sea")}) just past the Deep North line"))
                    {
                        yield return new WaitForSeconds(1.2f);
                        var p = player.transform.position;
                        var here = WorldState.CellAt(p);
                        c.Check(!WorldGenerator.IsDeepnorth(p.x, p.z) && WorldState.IsAwakeCell(here, ServerRules.Current)
                                && WorldState.IsStormingCell(here, ServerRules.Current) && Storms.Override() == null,
                            $"an invaded, storming area past the Deep North line: no blizzard from the mod there (area {here}, weather {CurrentEnv()})");
                    }
                }
            }
        }
        finally
        {
            ClearOverrides();
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.bands ----------

    private static AwakeningRules BandRules(int chance, int kills) => Rules(r =>
    {
        r.StormShare = 0;
        r.NatureBandChance = chance;
        r.ClearKillsMin = kills;
        r.ClearKillsMax = kills;
    });

    private static IEnumerator RunBands()
    {
        var c = new Checks(BandsName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        var spawned = new List<GameObject>();
        var cell = HeldCells.NoTag;
        try
        {
            ServerRules.TestRules = BandRules(100, 60);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(LandSlot(SlotBands, out var point, out cell), "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            var spawner = new Found<SpawnSystem>();
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                yield return WaitSpawner(player.transform.position, 12f, spawner);
            }
            if (box.Ok && c.Check(spawner.Value != null, "this game owns the zone control in the area"))
            {
                var pos = player.transform.position;
                var ss = spawner.Value;
                // A band left by an earlier pass would count against the cap.
                DestroyAll(BandLoaded());
                yield return Frames(3);

                // The area's own check: timer due (a place never visited) and the chance roll (100 %).
                var bands = AreaSpawns.TestBands;
                AreaSpawns.TestResetTimers(ss, false, true);
                yield return Until(() => AreaSpawns.TestBands > bands, 8f);
                c.Check(AreaSpawns.TestBands == bands + 1, $"NatureBandChance 100: a band comes at the area's next check ({AreaSpawns.TestBands - bands})");
                var roll = AreaSpawns.TestLastBand;
                var center = AreaSpawns.TestLastBandCenter;
                var made = AreaSpawns.TestLastBandSpawned;
                var members = BandLoaded();
                var gd = CountPrefab(members, Hostility.FrostGreydwarf);
                var sh = CountPrefab(members, Hostility.FrostShaman);
                var tr = CountPrefab(members, Hostility.Gammeltroll);
                var ba = CountPrefab(members, Hostility.Barka);
                var small = roll.Greydwarfs + roll.Shamans;
                var big = roll.Gammeltrolls + roll.Barkas;
                c.Check(small >= 10 && small <= 20 && roll.Shamans >= 2 && roll.Shamans <= 4 && big >= 1 && big <= 3,
                    $"the band is 10 to 20 frost Greydwarfs with 2 to 4 shamans, and 1 to 3 Gammeltroll or Barka (rolled {roll.Greydwarfs} + "
                    + $"{roll.Shamans} shamans, {roll.Gammeltrolls} Gammeltroll, {roll.Barkas} Barka)");
                c.Check(members.Count == made && gd + sh + tr + ba == made && made >= small + big - 2 && gd <= roll.Greydwarfs && sh <= roll.Shamans
                        && tr <= roll.Gammeltrolls && ba <= roll.Barkas && gd + sh >= 8 && sh >= 1 && tr + ba >= 1,
                    $"they are all there, marked as band members ({gd} Greydwarfs, {sh} shamans, {tr} Gammeltroll, {ba} Barka; "
                    + $"{small + big - made} place(s) refused by the terrain)");
                var dist = Flat(center - pos).magnitude;
                var spread = 0f;
                foreach (var m in members)
                {
                    spread = Mathf.Max(spread, Flat(m.transform.position - center).magnitude);
                }
                c.Check(dist >= 39f && dist <= 81f && spread <= 30f,
                    $"the band appears together, 40 to 80 m from the player ({F(dist)} m away, all within {F(spread)} m of each other's middle)");

                // Never a second band while one is around.
                AreaSpawns.TestResetTimers(ss, false, true);
                yield return new WaitForSeconds(3f);
                c.Check(AreaSpawns.TestBands == bands + 1 && BandLoaded().Count <= members.Count,
                    $"with a band around, the next check brings no second one ({AreaSpawns.TestBands - bands} band(s))");

                // They attack the Jotun.
                var foe = Spawn(Hostility.Krigen, center + (pos - center).normalized * 8f, center - pos);
                spawned.Add(foe);
                var fought = false;
                var until = Time.realtimeSinceStartup + clock.Cap(10f, 1);
                while (!fought && Time.realtimeSinceStartup < until)
                {
                    foreach (var ch in Character.GetAllCharacters())
                    {
                        var zdo = ZdoOf(ch);
                        var ai = zdo != null ? ch.GetComponent<MonsterAI>() : null;
                        var target = ai != null ? ai.m_targetCreature : null;
                        var targetZdo = ZdoOf(target);
                        if (targetZdo == null)
                        {
                            continue;
                        }
                        var band = zdo.GetInt(AreaSpawns.BandKey, 0) != 0;
                        var targetBand = targetZdo.GetInt(AreaSpawns.BandKey, 0) != 0;
                        if ((band && Hostility.IsArmy(targetZdo.GetPrefab())) || (targetBand && Hostility.IsArmy(zdo.GetPrefab())))
                        {
                            fought = true;
                            break;
                        }
                    }
                    yield return new WaitForSeconds(0.25f);
                }
                c.Check(fought, "the band and the Jotun fight: one of them targets the other within 10 s");

                // Chance 0: no band.
                KillAll(spawned);
                DestroyAll(BandLoaded());
                yield return Frames(3);
                ServerRules.TestRules = BandRules(0, 60);
                bands = AreaSpawns.TestBands;
                AreaSpawns.TestResetTimers(ss, false, true);
                yield return new WaitForSeconds(2.5f);
                c.Check(AreaSpawns.TestBands == bands && BandLoaded().Count == 0, "NatureBandChance 0: no band");

                // Default chance (10 %): now and then only. 120 checks of the area, one a frame, each band gone at once.
                ServerRules.TestRules = BandRules(AwakeningRules.Default.NatureBandChance, 60);
                bands = AreaSpawns.TestBands;
                const int rolls = 120;
                for (var k = 0; k < rolls; k++)
                {
                    AreaSpawns.TestResetTimers(ss, false, true);
                    AreaSpawns.Run(ss);
                    DestroyAll(BandLoaded());
                    yield return null;
                }
                DestroyAll(BandLoaded());
                yield return Frames(3);
                var came = AreaSpawns.TestBands - bands;
                c.Check(came >= 2 && came <= 28, $"default NatureBandChance (10 %): {came} bands in {rolls} checks of the area (about 12 expected)");

                // After Kall: still in an area that is not cleared, never in a cleared one.
                if (c.Check(clock.Room(16f), "time left for the checks after Kall"))
                {
                    ServerRules.TestRules = BandRules(100, 60);
                    HeldCells.TestSawNoKall(false);
                    WorldState.TestKall = true;
                    yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
                    bands = AreaSpawns.TestBands;
                    AreaSpawns.TestResetTimers(ss, false, true);
                    yield return Until(() => AreaSpawns.TestBands > bands, 5f);
                    c.Check(HeldCells.Scanned && !HeldCells.IsCleared(cell) && AreaSpawns.TestBands > bands,
                        "after Kall a band still comes in an area that is not cleared");
                    DestroyAll(BandLoaded());
                    yield return Frames(3);
                    // One kill clears it now (test rules).
                    ServerRules.TestRules = BandRules(100, 1);
                    FakeKill(Hostility.Krigen, pos + new Vector3(3f, 0f, 3f));
                    yield return Until(() => HeldCells.IsCleared(cell), 5f);
                    bands = AreaSpawns.TestBands;
                    AreaSpawns.TestResetTimers(ss, false, true);
                    yield return new WaitForSeconds(3f);
                    c.Check(HeldCells.IsCleared(cell) && AreaSpawns.TestBands == bands && BandLoaded().Count == 0,
                        $"never a band in a cleared area (cleared {HeldCells.IsCleared(cell)}, {AreaSpawns.TestBands - bands} band(s))");
                }
            }
        }
        finally
        {
            EndKall(cell);
            KillAll(spawned);
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kallfight ----------

    private static IEnumerator RunKallFight()
    {
        var c = new Checks(KallFightName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        var spawned = new List<GameObject>();
        var hud = new HudLog();
        var tap = EnsureTap();
        var cell = HeldCells.NoTag;
        var otherCell = HeldCells.NoTag;
        try
        {
            // Design rules (each area needs 8 to 13 kills), storms always on.
            ServerRules.TestRules = StormRules(100, true);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(LandSlot(SlotKallFight, out var point, out cell) && LandSlot(SlotKallReturn, out _, out otherCell) && otherCell != cell,
                    "two dry invaded Deep North areas exist"))
            {
                c.Report();
                yield break;
            }
            hud.Begin();
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                var weak = HeldCells.WeakeningText;
                var retreat = Localize(HeldCells.ClearedText);
                yield return WaitTagged(pos, 250f, 3, clock.Cap(20f));
                yield return Until(() => Storms.Override() == blizzard, 3f);
                c.Check(TaggedNear(pos, 250f).Count > 0 && Storms.Override() == blizzard,
                    $"before Kall: area Jotun around the player ({TaggedNear(pos, 250f).Count}) and the blizzard");

                // Kall falls while the player stands in the area.
                var mark = tap.Mark();
                HeldCells.TestSawNoKall(true);
                WorldState.TestKall = true;
                yield return Until(() => HeldCells.Scanned && HeldCells.TestServerEngaged(cell) && HeldCells.IsEngaged(cell), 5f);
                c.Check(HeldCells.TestServerEngaged(cell) && HeldCells.IsEngaged(cell), "Kall defeated: the area under the player waits to be defeated at once");
                c.Check(tap.Count(mark, "Kall is defeated:", "around the players wait to be defeated") == 1,
                    "log line \"Kall is defeated: ... around the players wait to be defeated ...\" once");
                var target = ServerRules.Current.KillTarget(WorldState.Seed, cell);
                var half = (target + 1) / 2;
                var line = First(RunConsole(AdminCommand.Name));
                c.Check(ReadOf(line, JotunDefeated, out var k0, out var n0) && k0 == 0 && n0 == target && target >= 8 && target <= 13
                        && line.Contains("waits to be defeated") && line.Contains("Kall defeated"),
                    $"deepnorth_stones shows 0 of N Jotun defeated, N from 8 to 13 ('{line}')");
                var count0 = TaggedNear(pos, 300f).Count;
                yield return new WaitForSeconds(4f);
                c.Check(TaggedNear(pos, 300f).Count <= count0, $"while the player stays no new Jotun appear ({count0} before, {TaggedNear(pos, 300f).Count} after 4 s)");

                // Nature kills never count.
                var nature = new List<GameObject>
                {
                    Spawn(Hostility.Barka, pos + new Vector3(12f, 0f, 0f)),
                    Spawn(Hostility.FrostGreydwarf, pos + new Vector3(-12f, 0f, 0f)),
                    Spawn(Hostility.Gammeltroll, pos + new Vector3(0f, 0f, 14f)),
                };
                spawned.AddRange(nature);
                yield return Frames(3);
                foreach (var go in nature)
                {
                    Slay(go != null ? go.GetComponent<Character>() : null);
                }
                yield return Until(() => nature.TrueForAll(g => g == null), 8f);
                yield return new WaitForSeconds(0.5f);
                c.Check(nature.TrueForAll(g => g == null) && HeldCells.TestKills(cell) == 0,
                    $"a Barka, a frost Greydwarf and a Gammeltroll killed in the area count for nothing ({HeldCells.TestKills(cell)} counted)");

                // Enough Jotun at hand for the whole count (the area's own, plus Krigen from anywhere), and one Krigen
                // that carries another area's tag (it came from there).
                var need = target + 2 - ArmyNear(pos, 45f, cell).Count;
                if (need > 0)
                {
                    SpawnRing(Hostility.Krigen, pos, need, 16f, spawned);
                }
                var strayObject = Spawn(Hostility.Krigen, pos + new Vector3(9f, 0f, 9f));
                spawned.Add(strayObject);
                yield return Frames(3);
                var stray = strayObject != null ? strayObject.GetComponent<Character>() : null;
                var strayZdo = ZdoOf(stray);
                if (strayZdo != null)
                {
                    strayZdo.Set(HeldCells.TagKey, otherCell);
                }
                HeldCells.TestResetNews();

                // Up to one short of half: counted one by one, no news.
                Slay(stray);
                var slain = 1 + SlayArmy(pos, cell, Mathf.Max(0, half - 2), stray, 45f);
                yield return WaitServerKills(cell, half - 1, 10f);
                yield return new WaitForSeconds(0.3f);
                HeldCells.TestLastNews(out var kind, out _, out _);
                c.Check(strayZdo != null && slain == half - 1 && HeldCells.TestKills(cell) == half - 1 && kind == 0 && hud.Count(weak) == 0,
                    $"{slain} Jotun killed for real in the area: each one counts ({HeldCells.TestKills(cell)} of {target}), no news before half way");
                c.Check(HeldCells.TestKills(otherCell) == 0,
                    $"a Krigen that came from another area counts where it died, not for its own area ({HeldCells.TestKills(otherCell)} there)");

                // Half way: weakening, top left, for the player in the area.
                mark = tap.Mark();
                slain = SlayArmy(pos, cell, 1, null, 45f);
                yield return WaitServerKills(cell, half, 10f);
                yield return Until(() => HeldCells.KillsIn(cell) == half, 3f);
                HeldCells.TestLastNews(out kind, out var newsCell, out var shown);
                c.Check(slain == 1 && HeldCells.TestKills(cell) == half && kind == HeldCells.NewsWeakening && newsCell == cell && shown
                        && hud.Count(weak) == 1,
                    $"half way ({half} of {target}): \"{weak}\" shown to the player, once ({hud.Count(weak)})");
                c.Check(tap.Count(mark, $"{half} of {target} Jotun defeated in Deep North area", "is weakening") == 1, "its log line, once");
                line = First(RunConsole(AdminCommand.Name));
                c.Check(ReadOf(line, JotunDefeated, out var k1, out var n1) && k1 == half && n1 == target,
                    $"deepnorth_stones counts them: {k1} of {n1} ('{line}')");

                // One short of the target: not cleared yet.
                slain = SlayArmy(pos, cell, target - half - 1, null, 45f);
                yield return WaitServerKills(cell, target - 1, 10f);
                c.Check(slain == target - half - 1 && HeldCells.TestKills(cell) == target - 1 && !HeldCells.IsCleared(cell),
                    $"{target - 1} of {target}: not cleared yet ({HeldCells.TestKills(cell)} counted)");

                // The last one: cleared for good.
                mark = tap.Mark();
                HeldCells.TestResetNews();
                slain = SlayArmy(pos, cell, 1, null, 45f);
                yield return Until(() => HeldCells.IsCleared(cell), 10f);
                HeldCells.TestLastNews(out kind, out newsCell, out shown);
                c.Check(slain == 1 && HeldCells.IsCleared(cell) && !HeldCells.MayBurst(cell) && HeldCells.TestMarkedAnywhere(cell)
                        && HeldCells.TestKills(cell) == 0 && HeldCells.TestKillSlots(cell) == 0,
                    $"{target} Jotun killed: the area is cleared for good and written in the world");
                c.Check(kind == HeldCells.NewsCleared && newsCell == cell && shown && hud.Count(retreat) == 1 && CenterText() == retreat,
                    $"\"{retreat}\" shown in the centre, once ({hud.Count(retreat)}, centre text '{CenterText()}')");
                c.Check(tap.Count(mark, $"{target} Jotun defeated in Deep North area", "the area is cleared for good") == 1,
                    "log line \"... the area is cleared for good\" once");
                var survivors = ArmyNear(pos, 300f, cell).Count;
                var taggedLeft = TaggedNear(pos, 300f).Count;
                yield return Until(() => Storms.Override() == null, 2f);
                c.Check(Storms.Override() == null && !Storms.StormAt(pos, ServerRules.Current), "the blizzard stops there (StormShare 100)");
                yield return new WaitForSeconds(clock.Cap(4f));
                c.Check(survivors >= 1 && ArmyNear(pos, 300f, cell).Count >= 1 && TaggedNear(pos, 300f).Count <= taggedLeft,
                    $"the Jotun still alive stay ({ArmyNear(pos, 300f, cell).Count} of {survivors}), nothing new appears");
                c.Check(MapOverlay.TestPixel(pos) == 1, $"the area is gone from the map (pixel state {MapOverlay.TestPixel(pos)})");
                line = First(RunConsole(AdminCommand.Name));
                c.Check(line.Contains("is cleared") && line.Contains("1 area(s) cleared"), $"deepnorth_stones says the area is cleared ('{line}')");

                // Kall undone (the key removed): as before Kall, the mark stays; Kall again: cleared again.
                if (c.Check(clock.Room(9f), "time left for undoing Kall"))
                {
                    WorldState.TestKall = false;
                    WorldState.Refresh();
                    yield return Until(() => !HeldCells.IsCleared(cell) && Storms.Override() == blizzard, 3f);
                    c.Check(!HeldCells.IsCleared(cell) && Storms.Override() == blizzard && AreaSpawns.Gate(EntryKind.Jotun, pos)
                            && HeldCells.TestMarkedAnywhere(cell),
                        "Kall undone: the area storms and takes Jotun spawns again; its cleared mark stays in the world");
                    HeldCells.TestSawNoKall(false);
                    WorldState.TestKall = true;
                    WorldState.Refresh();
                    yield return Until(() => HeldCells.Scanned && HeldCells.IsCleared(cell), 5f);
                    yield return Until(() => Storms.Override() == null, 2f);
                    c.Check(HeldCells.IsCleared(cell) && Storms.Override() == null && !AreaSpawns.Gate(EntryKind.Nature, pos),
                        "Kall defeated again: the mark applies again (cleared, no blizzard)");
                }
            }
        }
        finally
        {
            hud.End();
            EndKall(cell, otherCell);
            KillAll(spawned);
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kallreturn ----------

    private static AwakeningRules KallRules(int storm, int kills) => Rules(r =>
    {
        r.StormShare = storm;
        r.NatureBandChance = 0;
        r.ClearKillsMin = kills;
        r.ClearKillsMax = kills;
    });

    private static IEnumerator RunKallReturn()
    {
        var c = new Checks(KallReturnName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var cell = HeldCells.NoTag;
        try
        {
            // No clearing by the kills of this test.
            ServerRules.TestRules = KallRules(0, 60);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(LandSlot(SlotKallReturn, out var point, out cell), "a dry invaded Deep North area exists"))
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
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f, 3));
                c.Check(TaggedOf(cell).Count > 0, $"before Kall: area Jotun around the player ({Kinds(TaggedOf(cell))})");
                HeldCells.TestSawNoKall(true);
                WorldState.TestKall = true;
                yield return Until(() => HeldCells.Scanned && HeldCells.TestServerEngaged(cell), 5f);
                var slain = SlayArmy(pos, cell, 2, null);
                yield return WaitServerKills(cell, slain, 8f);
                yield return new WaitForSeconds(0.5f);
                c.Check(HeldCells.TestServerEngaged(cell) && slain == 2 && HeldCells.TestKills(cell) == 2,
                    $"Kall defeated in the area, 2 of its Jotun killed: 2 counted, the area waits to be defeated ({HeldCells.TestKills(cell)})");
                var left = TaggedOf(cell).Count;

                // Leave for real: farther from the area's centre than the release distance (about 750 m by default).
                var seedPoint = SeedPointOf(cell);
                var far = Flat(home - seedPoint).magnitude;
                yield return TeleportAndWait(player, home, TravelTimeout, box);
                away = !box.Ok;
                c.Check(box.Ok && far > HeldCells.ReleaseDistance() + 100f,
                    $"left the area: {F(far)} m from its centre (it lets go beyond {F(HeldCells.ReleaseDistance())} m)");
                yield return new WaitForSeconds(HeldCells.ReleaseCheckSeconds + 1.5f);
                c.Check(!HeldCells.TestServerEngaged(cell) && !HeldCells.IsEngaged(cell) && HeldCells.MayBurst(cell),
                    "a few seconds later the area no longer waits: it spawns again when entered");

                // Come back.
                away = true;
                yield return TeleportAndWait(player, point, TravelTimeout, box);
                if (c.Check(box.Ok, "travelled back into the area"))
                {
                    yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > left, clock.Cap(15f));
                    yield return WaitTagged(pos, 400f, left + 1, clock.Cap(5f));
                    var back = TaggedOf(cell);
                    c.Check(HeldCells.TestServerEngaged(cell) && back.Count > left && OneSet(back),
                        $"stepping in again tops the area up to one set, the survivors count: {left} were left, now {Kinds(back)}");
                    c.Check(HeldCells.TestKills(cell) == 2 && HeldCells.KillsIn(cell) == 2, $"the kills made before are kept ({HeldCells.KillsIn(cell)})");
                    var line = First(RunConsole(AdminCommand.Name));
                    c.Check(ReadOf(line, JotunDefeated, out var k, out var n) && k == 2 && n == 60 && line.Contains("waits to be defeated"),
                        $"deepnorth_stones still shows them: '{line}'");
                    var now = back.Count;
                    yield return new WaitForSeconds(clock.Cap(5f));
                    c.Check(TaggedOf(cell).Count <= now && HeldCells.TestServerEngaged(cell),
                        $"nothing more appears while the player stays ({now} then, {TaggedOf(cell).Count} after)");
                }
            }
        }
        finally
        {
            EndKall(cell);
            SweepArea(player);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.kallrestart ----------

    private static IEnumerator RunKallRestart()
    {
        var c = new Checks(KallRestartName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        var cell = HeldCells.NoTag;
        try
        {
            // 4 kills clear an area (test rules); storms always on.
            ServerRules.TestRules = KallRules(100, 4);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(LandSlot(SlotKallRestart, out var point, out cell), "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            // Kall is already defeated when the player comes: nobody visited this area before.
            HeldCells.TestSawNoKall(false);
            WorldState.TestKall = true;
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
            c.Check(HeldCells.Scanned && HeldCells.TestKnown && HeldCells.MayBurst(cell), "after Kall the area lists are there; the area spawns when entered");
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0, clock.Cap(20f));
                yield return WaitTagged(pos, 400f, 1, 4f);
                c.Check(HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0 && OneSet(TaggedOf(cell)),
                    $"an invaded area nobody visited before Kall has its Jotun as soon as the player walks in, one set ({Kinds(TaggedOf(cell))})");
                yield return Until(() => Storms.Override() == blizzard, 2f);
                c.Check(Storms.Override() == blizzard, "and its blizzard (StormShare 100)");

                var slain = SlayArmy(pos, cell, 2, null);
                yield return WaitServerKills(cell, 2, 8f);
                yield return new WaitForSeconds(0.5f);
                c.Check(slain == 2 && HeldCells.TestKills(cell) == 2 && HeldCells.TestKillSlots(cell) == 1,
                    $"2 of its Jotun killed: counted and kept on a zone control ({HeldCells.TestKills(cell)})");
                var before = TaggedOf(cell).Count;

                // Restart: what the mod remembers of the areas is gone (same call as at world end); the world keeps the kills.
                HeldCells.Clear();
                c.Check(!HeldCells.Scanned && !HeldCells.TestKnown && !HeldCells.TestServerEngaged(cell), "restart: nothing remembered, no area waits");
                yield return Until(() => HeldCells.Scanned && HeldCells.TestKills(cell) == 2 && HeldCells.KillsIn(cell) == 2, 5f);
                c.Check(HeldCells.TestKills(cell) == 2 && HeldCells.KillsIn(cell) == 2, $"the kills are read back from the world ({HeldCells.TestKills(cell)})");
                yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > before, clock.Cap(12f));
                yield return WaitTagged(pos, 400f, before + 1, 3f);
                var after = TaggedOf(cell);
                c.Check(HeldCells.TestServerEngaged(cell) && after.Count > before && OneSet(after),
                    $"the area is topped up right after, to one set: {before} were left, now {Kinds(after)}");
                var line = First(RunConsole(AdminCommand.Name));
                c.Check(ReadOf(line, JotunDefeated, out var k, out var n) && k == 2 && n == 4, $"deepnorth_stones still shows the kills: '{line}'");

                // A cleared area stays cleared over a restart.
                if (c.Check(clock.Room(14f), "time left for the cleared area"))
                {
                    slain = SlayArmy(pos, cell, 2, null);
                    yield return Until(() => HeldCells.IsCleared(cell), 10f);
                    c.Check(slain == 2 && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell), "2 more kills: the area is cleared (4 of 4)");
                    HeldCells.Clear();
                    yield return Until(() => HeldCells.Scanned && HeldCells.IsCleared(cell), 5f);
                    var count = TaggedOf(cell).Count;
                    yield return Until(() => Storms.Override() == null, 2f);
                    yield return new WaitForSeconds(clock.Cap(4f));
                    c.Check(HeldCells.IsCleared(cell) && !HeldCells.MayBurst(cell) && TaggedOf(cell).Count <= count,
                        $"after a restart it is still cleared and spawns nothing ({count} Jotun then, {TaggedOf(cell).Count} after 4 s)");
                    c.Check(Storms.Override() == null && MapOverlay.TestPixel(pos) == 1,
                        $"no blizzard there, not on the map (pixel state {MapOverlay.TestPixel(pos)})");
                    line = First(RunConsole(AdminCommand.Name));
                    c.Check(line.Contains("is cleared"), $"deepnorth_stones says the area is cleared: '{line}'");
                }
            }
        }
        finally
        {
            EndKall(cell);
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.oldworld ----------

    private static List<Vector3> MorkhallaPlaces(bool placedOnly)
    {
        var list = new List<Vector3>();
        var zs = ZoneSystem.instance;
        if (zs == null)
        {
            return list;
        }
        foreach (var li in zs.m_locationInstances.Values)
        {
            if (IsMorkhalla(li) && (li.m_placed || !placedOnly))
            {
                list.Add(li.m_position);
            }
        }
        return list;
    }

    private static List<Vector3> StonePlaces()
    {
        var list = new List<Vector3>();
        foreach (var z in Stones.AllZdos(Stones.StonePrefab))
        {
            list.Add(z.GetPosition());
        }
        return list;
    }

    private static List<ZDO> StonesAt(Vector3 morkhalla)
    {
        var list = new List<ZDO>();
        foreach (var z in Stones.AllZdos(Stones.StonePrefab))
        {
            if (Flat(z.GetPosition() - morkhalla).magnitude <= Stones.StoneSearchRadius)
            {
                list.Add(z);
            }
        }
        return list;
    }

    private static bool HasPlace(List<Vector3> places, Vector3 place)
    {
        foreach (var p in places)
        {
            if (Flat(p - place).magnitude < 1f)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerator RunOldWorld()
    {
        var c = new Checks(OldWorldName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var zs = ZoneSystem.instance;
        var saved = SaveStonesKey();
        var events = pes != null ? new List<PersistentEventSystem.ActivePersistentEvent>(pes.m_activePersistentEvents.list) : null;
        var stones = new List<GameObject>();
        var hud = new HudLog();
        var tap = EnsureTap();
        var travelled = false;
        try
        {
            ServerRules.TestRules = Rules(null);
            // Not the Morkhalla dn.morkhalla looks at (the nearest): the second nearest loses its Malicious Ice here.
            var all = MorkhallaPlaces(false);
            all.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
            if (!c.Check(all.Count > 0 && index >= 0, $"a Morkhalla is planned in this world ({all.Count})"))
            {
                c.Report();
                yield break;
            }
            var target = all[all.Count > 1 ? 1 : 0];
            hud.Begin();
            var advance = Localize(Stones.StoneText);
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, target + new Vector3(0f, 0f, 25f), TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the Morkhalla at {F(target)}"))
            {
                var pos = player.transform.position;
                yield return Until(() => StonesAt(target).Count > 0 && HasPlace(MorkhallaPlaces(true), target), 10f);
                var own = StonesAt(target);
                var broken0 = Stones.CountBroken(MorkhallaPlaces(true), StonePlaces(), Stones.StoneSearchRadius);
                WorldState.Refresh();
                var count0 = WorldState.Stones;
                if (c.Check(own.Count > 0 && HasPlace(MorkhallaPlaces(true), target), $"the explored Morkhalla has its Malicious Ice ({own.Count})"))
                {
                    // The stone goes away like in a game without the mod: no count, no banner, no invasion request.
                    foreach (var zdo in own)
                    {
                        Stones.TestIgnoreBreak(zdo.m_uid);
                        var view = ZNetScene.instance.FindInstance(zdo);
                        if (view != null)
                        {
                            view.ClaimOwnership();
                            ZNetScene.instance.Destroy(view.gameObject);
                        }
                        else
                        {
                            zdo.SetOwner(ZDOMan.GetSessionID());
                            ZDOMan.instance.DestroyZDO(zdo);
                        }
                    }
                    yield return Until(() => StonesAt(target).Count == 0, 4f);
                    yield return Frames(3);
                    WorldState.Refresh();
                    c.Check(StonesAt(target).Count == 0 && WorldState.Stones == count0 && hud.Count(advance) == 0,
                        $"its Malicious Ice is gone without the mod counting it ({WorldState.Stones} broken, was {count0})");

                    // T13: the mod meets this world for the first time (no count saved): it counts, quietly.
                    var mark = tap.Mark();
                    var invasions = Stones.ActiveInvasions(pes, index);
                    zs.RemoveGlobalKey(WorldState.StonesKey);
                    Stones.Clear();
                    WorldState.Refresh();
                    yield return Until(() => zs.GetGlobalKey(WorldState.StonesKey), 4f);
                    WorldState.Refresh();
                    var placed = MorkhallaPlaces(true);
                    var expect = Stones.CountBroken(placed, StonePlaces(), Stones.StoneSearchRadius);
                    c.Check(expect == broken0 + 1 && WorldState.HasStonesKey && RealKeyIs(expect),
                        $"the mod counted the Morkhalla without its Malicious Ice: {WorldState.Stones} broken (expected {expect}, {broken0} before)");
                    var countedLine = tap.Last(mark, "Counted the broken Malicious Ice of this world:");
                    c.Check(tap.Count(mark, "Counted the broken Malicious Ice of this world:") == 1
                            && countedLine.Contains($"{placed.Count} explored Morkhalla, {expect} with its Malicious Ice already broken. Deep North stage {WorldState.StageOf(expect)}."),
                        $"log line once: '{countedLine}'");
                    c.Check(tap.Count(mark, "was removed: counting") == 0, "no \"key was removed\" line: a first meeting, not a removal");
                    yield return new WaitForSeconds(1.2f);
                    c.Check(hud.Count(advance) == 0 && Stones.ActiveInvasions(pes, index) == invasions && Stones.HeldCount == 0,
                        $"no \"{advance}\", no new invasion (running {Stones.ActiveInvasions(pes, index)}, was {invasions})");
                    var line = First(RunConsole(AdminCommand.Name));
                    c.Check(line.StartsWith($"Deep North: {expect} Malicious Ice broken, stage {WorldState.StageOf(expect)}", StringComparison.Ordinal),
                        $"deepnorth_stones shows it: '{line}'");

                    // T19: one more counted from a spawned test stone, then the key is removed mid-game.
                    WorldState.WriteStones(expect + 1);
                    yield return Frames(3);
                    mark = tap.Mark();
                    var t0 = Time.realtimeSinceStartup;
                    zs.RemoveGlobalKey(WorldState.StonesKey);
                    yield return Until(() => RealKeyIs(expect), 3f);
                    var took = Time.realtimeSinceStartup - t0;
                    c.Check(RealKeyIs(expect) && took <= 1.5f,
                        $"key removed: {F(took)} s later the count is back, the Morkhalla without Malicious Ice only ({expect}; spawned test stones are not counted)");
                    c.Check(tap.Count(mark, "The global key mc_dn_stones was removed: counting the broken Malicious Ice again.") == 1
                            && tap.Count(mark, "Counted the broken Malicious Ice of this world:") == 1,
                        "both log lines, once each");
                    line = First(RunConsole(AdminCommand.Name));
                    c.Check(line.StartsWith($"Deep North: {expect} Malicious Ice broken, stage {WorldState.StageOf(expect)}", StringComparison.Ordinal),
                        $"deepnorth_stones shows the count of the Morkhalla without Malicious Ice again: '{line}'");
                    var fwd = Flat(player.transform.forward).normalized;
                    yield return BreakCounted(pos + (fwd == Vector3.zero ? Vector3.forward : fwd) * 7f, stones, expect + 1, box);
                    yield return new WaitForSeconds(1.2f);
                    c.Check(box.Ok && RealKeyIs(expect + 1) && hud.Count(advance) == 1,
                        $"another one broken: the count goes on from there ({WorldState.Stones}), \"{advance}\" shown");
                }
            }
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
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Note("the Malicious Ice of that Morkhalla stays gone in the throwaway world");
        c.Report();
    }

    // ---------- dn.toggle ----------

    private const string ToggleBlocker = "Inactive: turned off for a moment by the dn.toggle self test.";

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var saved = SaveStonesKey();
        var events = pes != null ? new List<PersistentEventSystem.ActivePersistentEvent>(pes.m_activePersistentEvents.list) : null;
        var spawned = new List<GameObject>();
        var stones = new List<GameObject>();
        var hud = new HudLog();
        var travelled = false;
        try
        {
            if (!c.Check(index >= 0 && ModActive() && LandSlot(SlotToggle, out _, out _), "the mod is on and a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            LandSlot(SlotToggle, out var point, out var cell);
            hud.Begin();
            pes.m_activePersistentEvents.list.Clear();
            pes.UpdateClientEventsList(0L);
            // The real key: the game without the mod must find it too.
            WorldState.WriteStones(3);
            ServerRules.TestRules = StormRules(100, true);
            AreaSpawns.TestInvalidate();
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                var advance = Localize(Stones.StoneText);
                var fwd = Flat(player.transform.forward).normalized;
                if (fwd == Vector3.zero)
                {
                    fwd = Vector3.forward;
                }
                var side = Vector3.Cross(Vector3.up, fwd);
                yield return Until(() => Storms.Override() == blizzard && CurrentEnv() == blizzard, 6f);
                yield return WaitTagged(pos, 250f, 2, clock.Cap(18f));
                c.Check(Storms.Override() == blizzard && CurrentEnv() == blizzard && TaggedNear(pos, 250f).Count > 0,
                    $"on: blizzard and area Jotun around the player ({TaggedNear(pos, 250f).Count})");
                yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, 6f);
                Color32 own = default;
                Color32 tinted = default;
                var purple = MapOverlay.TestPixel(pos) == 2 && MapOverlay.TestOwnColor(pos, out own) && MapOverlay.TestRawColor(pos, out tinted)
                             && !SameColor(own, tinted);
                c.Check(purple, "on: the area is purple on the map under the player");
                var ss = OwnedSpawner(pos);

                // Off (framework switch; the Enabled setting is never touched).
                Plugin.TestBlocked = ToggleBlocker;
                FeatureRegistry.RefreshAll();
                c.Check(!ModActive(), "the mod is off");
                c.Check(purple && MapOverlay.TestRawColor(pos, out var offColor) && SameColor(offColor, own),
                    "off: the map has its own colours back at once");
                c.Check(!Terminal.commands.ContainsKey(AdminCommand.Name), "off: the deepnorth_stones command is gone");
                yield return Frames(3);
                var forced = EnvMan.instance.GetEnvironmentOverride();
                c.Check(string.IsNullOrEmpty(forced) || forced != blizzard,
                    $"off: the blizzard is no longer forced (the game forces '{forced}', weather now {CurrentEnv()})");
                DestroyAll(TaggedNear(pos, 500f));
                yield return Frames(3);
                // Every area timer due: a mod that still ran would spawn within a second.
                AreaSpawns.TestResetTimers(ss, true, true);
                var krigenObject = Spawn(Hostility.Krigen, pos + fwd * 30f, fwd);
                var trollObject = Spawn(Hostility.Gammeltroll, pos + fwd * 36f, -fwd);
                spawned.Add(krigenObject);
                spawned.Add(trollObject);
                yield return null;
                var krigen = krigenObject != null ? krigenObject.GetComponent<Character>() : null;
                var troll = trollObject != null ? trollObject.GetComponent<Character>() : null;
                if (c.Check(krigen != null && troll != null, "spawned a Krigen and a Gammeltroll face to face"))
                {
                    c.Check(!BaseAI.IsEnemy(krigen, troll) && !BaseAI.IsEnemy(troll, krigen), "off: Krigen and Gammeltroll are friends (stage 3 in the world)");
                    var krigenAi = krigen.GetComponent<MonsterAI>();
                    var trollAi = troll.GetComponent<MonsterAI>();
                    var fought = false;
                    var until = Time.realtimeSinceStartup + 6f;
                    while (Time.realtimeSinceStartup < until && krigen != null && troll != null)
                    {
                        fought |= (krigenAi != null && krigenAi.m_targetCreature == troll) || (trollAi != null && trollAi.m_targetCreature == krigen);
                        yield return new WaitForSeconds(0.25f);
                    }
                    c.Check(!fought, "off: for 6 s neither targets the other");
                }
                else
                {
                    yield return new WaitForSeconds(6f);
                }
                c.Check(TaggedNear(pos, 500f).Count == 0, $"off: no new area Jotun in 6 s with every area timer due ({TaggedNear(pos, 500f).Count})");
                KillAll(spawned);

                // A Malicious Ice broken while off: the normal game (banner, one invasion at once), the count untouched.
                var running = Stones.ActiveInvasions(pes, index);
                yield return BreakStone(pos + side * 8f, stones, 0f, box);
                c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == running + 1,
                    $"off: a broken Malicious Ice starts one invasion at once ({Stones.ActiveInvasions(pes, index)} running, was {running})");
                yield return new WaitForSeconds(1.2f);
                c.Check(hud.Count(advance) == 1 && RealKeyIs(3), $"off: \"{advance}\" shown ({hud.Count(advance)}); the world still has mc_dn_stones 3");

                // On again.
                Plugin.TestBlocked = null;
                FeatureRegistry.RefreshAll();
                ServerRules.TestRules = StormRules(100, true);
                AreaSpawns.TestInvalidate();
                WorldState.Refresh();
                c.Check(ModActive() && Terminal.commands.ContainsKey(AdminCommand.Name) && WorldState.Stones == 3,
                    $"on again: the count is where it was ({WorldState.Stones})");
                yield return Until(() => Storms.Override() == blizzard && EnvMan.instance.GetEnvironmentOverride() == blizzard, 4f);
                c.Check(Storms.Override() == blizzard && EnvMan.instance.GetEnvironmentOverride() == blizzard, "on again: the blizzard is back");
                yield return WaitTagged(pos, 250f, 1, clock.Cap(10f));
                c.Check(TaggedNear(pos, 250f).Count > 0, $"on again: the area spawns its Jotun ({TaggedNear(pos, 250f).Count})");
                yield return BreakCounted(pos - side * 8f, stones, 4, box);
                c.Check(box.Ok && RealKeyIs(4), $"on again: the next Malicious Ice counts on from there ({WorldState.Stones})");
                yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, clock.Cap(8f));
                c.Check(MapOverlay.TestPixel(pos) == 2, $"on again: the area is purple on the map again (pixel state {MapOverlay.TestPixel(pos)})");
            }
        }
        finally
        {
            if (Plugin.TestBlocked != null)
            {
                Plugin.TestBlocked = null;
                FeatureRegistry.RefreshAll();
            }
            foreach (var go in stones)
            {
                KillStone(go);
            }
            KillAll(spawned);
            RestoreEvents(pes, events);
            RestoreStonesKey(saved);
            hud.End();
            ClearOverrides();
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Note("the two test invasions left their ice and Fimbul locations far from the area (events removed)");
        c.Report();
    }

    // ---------- dn.bug.caps ----------

    // Real bug, alone here (README and design 2.3: "up to 3 Krigen, 2 dual-axe Krigen, 2 Hexen and 6 Elaking"). The mod
    // let vanilla UpdateSpawnList count: a pass stop at the cap, but its last group is sized against the count from
    // before the pass, so a fresh fill can end one Krigen or two Elaking over. Me fill one area fresh several times
    // (timers back to "never ran", like a place nobody visited) and stop at the first fill over a number. Expected to
    // fail until the mod (or the README) is fixed; the other tests only ask for one set (OneSet).
    private static IEnumerator RunCaps()
    {
        var c = new Checks(CapsName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var travelled = false;
        try
        {
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 0;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            if (!c.Check(LandSlot(SlotOther, out var point, out var cell), "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            var spawner = new Found<SpawnSystem>();
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                yield return WaitSpawner(player.transform.position, 12f, spawner);
            }
            if (box.Ok && c.Check(spawner.Value != null, "this game owns the zone control in the area"))
            {
                var pos = player.transform.position;
                // A fill go one Krigen over about 3 times in 8 (two groups of 2, or 1 + 1 + 2): 10 fills miss it 1 run in 100.
                const int wanted = 10;
                var fills = 0;
                var most = new int[Hostility.Army.Length];
                var over = "";
                for (var k = 0; k < wanted && over.Length == 0 && clock.Room(5f); k++)
                {
                    DestroyAll(TaggedNear(pos, 400f));
                    yield return Frames(3);
                    AreaSpawns.TestResetTimers(spawner.Value, true, false);
                    // The four entries run in one pass of the zone (once a second; the first up to 10 s after the zone
                    // came).
                    yield return Until(() => TaggedNear(pos, 400f).Count > 0, clock.Cap(k == 0 ? 14f : 4f));
                    yield return Frames(2);
                    var got = TaggedNear(pos, 400f);
                    if (got.Count == 0)
                    {
                        continue;
                    }
                    fills++;
                    for (var i = 0; i < most.Length; i++)
                    {
                        most[i] = Mathf.Max(most[i], CountPrefab(got, Hostility.Army[i]));
                    }
                    if (!UnderCaps(got))
                    {
                        over = Kinds(got);
                    }
                }
                c.Check(fills >= 3 && over.Length == 0,
                    $"{fills} fresh fill(s) of one invaded area: never more than 3 Krigen, 2 dual-axe Krigen, 2 Hexen and 6 Elaking at once "
                    + $"(most seen: {most[0]} Krigen, {most[1]} dual-axe Krigen, {most[2]} Hexen, {most[3]} Elaking"
                    + (over.Length > 0 ? $"; one fill gave {over}" : "") + ")");
            }
        }
        finally
        {
            ClearOverrides();
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }
}
#endif
