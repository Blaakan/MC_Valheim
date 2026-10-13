#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Staged self tests (docs/modding/framework.md, "Staged tests"): they run after the normal tests of a
// single-player run, each through a real log out to the main menu and a load of the same world, or through a real
// restart of the game with every MC mod turned off.
// Relog tests (before: in the world; the probe logs out and loads the world again; after). What must live in the world
// is REAL here: the count key mc_dn_stones and the Kall key defeated_frozenking_p3, saved with the world and read back.
// Only the rules stay forced in memory (the game process is the same, a static lives through the log out):
//   dn.relog.areas       T03: logged out in an invaded place at stage 1: after the load it is still invaded (same
//                        Jotun, no second set, purple), a quiet place still quiet
//   dn.relog.cleared     T12, T21: logged out in an area cleared after Kall: still cleared, quiet, no blizzard, off the
//                        map, the console says so
//   dn.relog.kills       T21: logged out in an area with 2 of 6 kills: the kills are read back, the area is topped up
//                        on arrival (it did not wait: that list is not saved), the console still shows the kills
//   dn.relog.killtarget  T24: 9 of 12 kills, log out, at the main menu the target goes down to 8, load: cleared at once,
//                        the server's log line once, never "9 of 8"
// Disabled-start tests (leave: end of the first game, mod on; check: second game, every MC mod Enabled = false):
//   dn.disabled.north    T17: the world left with 3 broken; second game: none of the mod's patches in the game, no
//                        command, a place that would be invaded is quiet, no weather forced, own map colours, Krigen and
//                        Gammeltroll friends, a broken Malicious Ice runs like in the normal game
//   dn.disabled.cleanlog T18: no error line of the mod in the whole first game (staged tests too), none in the second
// A before-part that fails cleans up at once and its after-part says so; an after-part always cleans up.
internal static partial class SelfTests
{
    private const string RelogAreasName = "dn.relog.areas";
    private const string RelogClearedName = "dn.relog.cleared";
    private const string RelogKillsName = "dn.relog.kills";
    private const string RelogTargetName = "dn.relog.killtarget";
    private const string DisabledNorthName = "dn.disabled.north";
    private const string DisabledLogName = "dn.disabled.cleanlog";

    private static void RegisterStaged()
    {
        SelfTest.RegisterRelog(RelogAreasName, RelogAreasBefore, null, RelogAreasAfter);
        SelfTest.RegisterRelog(RelogClearedName, RelogClearedBefore, null, RelogClearedAfter);
        SelfTest.RegisterRelog(RelogKillsName, RelogKillsBefore, null, RelogKillsAfter);
        SelfTest.RegisterRelog(RelogTargetName, RelogTargetBefore, RelogTargetMenu, RelogTargetAfter);
    }

    private static void UnregisterStaged()
    {
        SelfTest.UnregisterRelog(RelogAreasName);
        SelfTest.UnregisterRelog(RelogClearedName);
        SelfTest.UnregisterRelog(RelogKillsName);
        SelfTest.UnregisterRelog(RelogTargetName);
    }

    // BindConfig call me (through SelfTests.RegisterEarly): in the second game the mod is off, OnActivated never runs.
    // Log tap from now on too (both games).
    private static void RegisterDisabledStart()
    {
        EnsureTap();
        SelfTest.RegisterDisabledStart(DisabledNorthName, DisabledNorthLeave, DisabledNorthCheck);
        SelfTest.RegisterDisabledStart(DisabledLogName, DisabledLogLeave, DisabledLogCheck);
    }

    // ---------- relog tools ----------

    // What a relog test keeps from its part before the log out (statics live on: same game process).
    private sealed class RelogState
    {
        internal bool Handed;
        internal bool Away;
        internal bool Kall;
        internal SavedKey Stones;
        internal bool HadKall;
        internal int Cell = HeldCells.NoTag;
        internal Vector3 Point;
        internal Vector3 LogoutAt;
        internal int MarkValue;
        internal int Left;
        internal int LogMark;
    }

    private static RelogState _relogAreas;
    private static RelogState _relogCleared;
    private static RelogState _relogKills;
    private static RelogState _relogTarget;

    // World as before the test: real keys back, test areas unmarked, forced state gone, the test's Jotun gone.
    private static void RelogCleanup(RelogState s, Player player)
    {
        if (s.Kall)
        {
            SetRealKall(s.HadKall);
            EndKall(s.Cell);
        }
        else
        {
            ClearOverrides();
        }
        RestoreStonesKey(s.Stones);
        SweepArea(player);
        PurgeTagged(s.Point, 600f);
    }

    // A test's own int on a creature's ZDO: saved with the world. The game gives every object a new id at each load
    // (ZDO.Load), so "the same Jotun as before the log out" can only be told by something saved on them.
    private static readonly int RelogMarkKey = (ModInfo.Guid + ".SelfTestMark").GetStableHashCode();

    private static int MarkAll(List<Character> list, int mark)
    {
        var n = 0;
        foreach (var ch in list)
        {
            var zdo = ZdoOf(ch);
            if (zdo != null)
            {
                zdo.Set(RelogMarkKey, mark);
                n++;
            }
        }
        return n;
    }

    // How many of a list carry the mark (marked = true) or do not (they came after the marking).
    private static int CountMarked(List<Character> list, int mark, bool marked)
    {
        var n = 0;
        foreach (var ch in list)
        {
            var zdo = ZdoOf(ch);
            if (zdo != null && (zdo.GetInt(RelogMarkKey, 0) == mark) == marked)
            {
                n++;
            }
        }
        return n;
    }

    private static bool RelogReady(string name, RelogState s)
    {
        if (s != null && s.Handed)
        {
            return true;
        }
        SelfTest.Fail(name, "the part before the log out did not get its place ready (see above): nothing was checked after the load");
        return false;
    }

    // Start of an after-Kall relog test: the real count (3) and the real Kall key in the world, the rules in memory, the
    // player in the area, its Jotun there (the area was entered after Kall). ok = all of it.
    private static IEnumerator RelogEnter(Checks c, Player player, Clock clock, RelogState s, int slot, AwakeningRules rules, Box ok)
    {
        ok.Ok = false;
        s.Kall = true;
        s.Stones = SaveStonesKey();
        s.HadKall = RealKall();
        ServerRules.TestRules = rules;
        WorldState.TestStones = null;
        WorldState.TestKall = null;
        WorldState.WriteStones(3);
        AreaSpawns.TestInvalidate();
        var haveArea = LandSlot(slot, out s.Point, out s.Cell);
        if (!c.Check(!s.HadKall && haveArea && RealKeyIs(3) && WorldState.Stage == 3,
                $"Kall is not defeated in this world, a dry invaded Deep North area exists and the world's own count is 3 now (stage {WorldState.Stage})"))
        {
            yield break;
        }
        var cell = s.Cell;
        SetRealKall(true);
        yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 5f);
        c.Check(WorldState.KallDefeated && HeldCells.Scanned && HeldCells.TestKnown && HeldCells.MayBurst(cell),
            "the real key defeated_frozenking_p3 set: the area lists are there, the area spawns when entered");
        var box = new Box();
        s.Away = true;
        yield return TeleportAndWait(player, s.Point, TravelTimeout, box);
        if (!c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(s.Point)}"))
        {
            yield break;
        }
        var pos = player.transform.position;
        yield return Until(() => HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0, clock.Cap(20f, 0));
        yield return WaitTagged(pos, 400f, 1, 4f);
        ok.Ok = c.Check(HeldCells.TestServerEngaged(cell) && TaggedOf(cell).Count > 0,
            $"the area has its Jotun and waits to be defeated ({Kinds(TaggedOf(cell))})");
    }

    // After the load: the keys came back from the save, the forced rules lived on, the player stands where it logged out.
    private static void RelogLoaded(Checks c, Player player, RelogState s, int stones, bool kall)
    {
        WorldState.Refresh();
        c.Check(WorldState.TestStones == null && WorldState.TestKall == null && RealKeyIs(stones) && WorldState.Stage == WorldState.StageOf(stones)
                && RealKall() == kall && WorldState.KallDefeated == kall,
            $"after a real save, log out and load the world still says {stones} Malicious Ice broken and Kall "
            + (kall ? "defeated" : "not defeated") + $" (read from the save: stage {WorldState.Stage}, Kall {WorldState.KallDefeated})");
        var moved = Flat(player.transform.position - s.LogoutAt).magnitude;
        c.Check(ServerRules.TestRules != null && moved < 30f, $"the player is back where it logged out ({F(moved)} m away), the test rules are still in force");
    }

    // ---------- dn.relog.areas (T03) ----------

    private static IEnumerator RelogAreasBefore()
    {
        var c = new Checks(RelogAreasName);
        var s = _relogAreas = new RelogState { MarkValue = 1 };
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        try
        {
            s.Stones = SaveStonesKey();
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 0;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = null;
            WorldState.TestKall = null;
            // The world's own count: what the server writes when the first Malicious Ice breaks.
            WorldState.WriteStones(1);
            AreaSpawns.TestInvalidate();
            var haveArea = Stage1Land(out s.Point, out s.Cell);
            var haveQuiet = QuietLand(out _, out _);
            if (c.Check(haveArea && haveQuiet && RealKeyIs(1) && WorldState.Stage == 1 && !WorldState.KallDefeated,
                    $"a dry area invaded at stage 1 and a quiet one exist, and the world's own count is 1 now (stage {WorldState.Stage})"))
            {
                var cell = s.Cell;
                var box = new Box();
                s.Away = true;
                yield return TeleportAndWait(player, s.Point, TravelTimeout, box);
                if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(s.Point)}"))
                {
                    var pos = player.transform.position;
                    var spawner = new Found<SpawnSystem>();
                    yield return WaitSpawner(pos, 12f, spawner);
                    DestroyAll(TaggedNear(pos, 400f));
                    yield return Frames(3);
                    AreaSpawns.TestResetTimers(spawner.Value, true, false);
                    yield return WaitTagged(pos, 250f, 4, clock.Cap(22f, 0));
                    var first = TaggedNear(pos, 400f);
                    s.Left = MarkAll(first, s.MarkValue);
                    yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, 6f);
                    var good = c.Check(spawner.Value != null && first.Count > 0 && WorldState.IsAwakeCell(cell, ServerRules.Current)
                                       && AreaSpawns.Gate(EntryKind.Jotun, pos) && MapOverlay.TestPixel(pos) == 2,
                        $"before the log out: the place is invaded ({first.Count} Jotun around: {Kinds(first)}; purple on the map)");
                    // Back on the spot of the arrival for the log out (the Jotun push a player around): the place whose
                    // spawn timers just ran is the one the player loads into.
                    yield return new WaitForSeconds(1f);
                    Put(player, pos);
                    yield return null;
                    s.LogoutAt = player.transform.position;
                    s.Handed = good;
                }
            }
        }
        finally
        {
            if (!s.Handed)
            {
                RelogCleanup(s, player);
            }
        }
        if (!s.Handed && s.Away)
        {
            yield return GoHome(c, player, _home.Value);
        }
        c.Report(s.Handed ? "; the player logs out in the invaded place" : "");
    }

    private static IEnumerator RelogAreasAfter()
    {
        var s = _relogAreas;
        if (!RelogReady(RelogAreasName, s))
        {
            yield break;
        }
        var c = new Checks(RelogAreasName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        try
        {
            var cell = s.Cell;
            var pos = s.LogoutAt;
            var rules = ServerRules.Current;
            RelogLoaded(c, player, s, 1, false);
            yield return Until(() => CountMarked(TaggedNear(pos, 400f), s.MarkValue, true) >= s.Left, 10f);
            var same = CountMarked(TaggedNear(pos, 400f), s.MarkValue, true);
            yield return new WaitForSeconds(4f);
            var fresh = CountMarked(TaggedNear(pos, 400f), s.MarkValue, false);
            c.Check(same >= Mathf.Max(1, s.Left - 1) && fresh == 0,
                $"the invaded place has its Jotun again, the same ones ({same} of {s.Left}); no second set came with the load ({fresh} new)");
            yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, 15f);
            c.Check(WorldState.IsAwakeCell(cell, rules) && AreaSpawns.Gate(EntryKind.Jotun, pos) && MapOverlay.TestPixel(pos) == 2,
                $"it is still invaded: takes area spawns, purple on the map (pixel state {MapOverlay.TestPixel(pos)})");

            // A quiet place, after the load.
            var haveQuiet = QuietLand(out var quietPoint, out var quietCell);
            if (c.Check(haveQuiet && clock.Room(10f, 2), "a quiet Deep North area and time to go there"))
            {
                var box = new Box();
                yield return TeleportAndWait(player, quietPoint, TravelTimeout, box);
                if (c.Check(box.Ok, $"travelled to the quiet area {quietCell} at {F(quietPoint)}"))
                {
                    var quietPos = player.transform.position;
                    var known = new HashSet<Character>(Character.GetAllCharacters());
                    var q = new Quiet();
                    yield return WatchQuiet(player, clock.Cap(8f), known, q);
                    c.Check(!WorldState.IsAwakeCell(quietCell, rules) && q.NewTagged == 0 && q.NewBand == 0 && !q.Storm && !q.Meteor
                            && !AreaSpawns.Gate(EntryKind.Jotun, quietPos) && MapOverlay.TestPixel(quietPos) != 2,
                        $"the quiet place is still quiet after the load ({q.NewTagged} Jotun, {q.NewBand} band members, blizzard {q.Storm}, "
                        + $"map pixel state {MapOverlay.TestPixel(quietPos)})");
                }
            }
        }
        finally
        {
            s.Handed = false;
            RelogCleanup(s, player);
        }
        yield return GoHome(c, player, _home.Value);
        c.Report();
    }

    // ---------- dn.relog.cleared (T12, T21) ----------

    private static IEnumerator RelogClearedBefore()
    {
        var c = new Checks(RelogClearedName);
        var s = _relogCleared = new RelogState { MarkValue = 2 };
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        try
        {
            var ok = new Box();
            // Storms always, 2 kills clear the area.
            yield return RelogEnter(c, player, clock, s, SlotRelogCleared, KallRules(100, 2), ok);
            if (ok.Ok)
            {
                var cell = s.Cell;
                var pos = player.transform.position;
                var slain = SlayArmy(pos, cell, 2, null);
                yield return Until(() => HeldCells.IsCleared(cell), 10f);
                yield return Until(() => Storms.Override() == null && MapOverlay.TestPixel(pos) == 1, 3f);
                var good = c.Check(slain == 2 && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell) && Storms.Override() == null
                                   && MapOverlay.TestPixel(pos) == 1,
                    $"2 of its Jotun killed: the area is cleared, written in the world, no blizzard, off the map (killed {slain})");
                yield return new WaitForSeconds(1.5f);
                s.Left = MarkAll(TaggedNear(pos, 400f), s.MarkValue);
                s.LogoutAt = player.transform.position;
                s.Handed = good;
            }
        }
        finally
        {
            if (!s.Handed)
            {
                RelogCleanup(s, player);
            }
        }
        if (!s.Handed && s.Away)
        {
            yield return GoHome(c, player, _home.Value);
        }
        c.Report(s.Handed ? "; the player logs out in the cleared area" : "");
    }

    private static IEnumerator RelogClearedAfter()
    {
        var s = _relogCleared;
        if (!RelogReady(RelogClearedName, s))
        {
            yield break;
        }
        var c = new Checks(RelogClearedName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        try
        {
            var cell = s.Cell;
            var pos = s.LogoutAt;
            var rules = ServerRules.Current;
            RelogLoaded(c, player, s, 3, true);
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown, 10f);
            c.Check(HeldCells.TestServerCleared(cell) && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell) && !HeldCells.MayBurst(cell),
                "the cleared area is still cleared: its mark was read back from the saved world");
            var blizzard = Storms.Env();
            var storm = false;
            var w = new StayWatch();
            var watch = WatchStay(() => CountMarked(TaggedNear(pos, 400f), s.MarkValue, false), HeldCells.NoTag, clock.Cap(10f), w);
            while (watch.MoveNext())
            {
                storm |= Storms.Override() != null || EnvMan.instance.GetEnvironmentOverride() == blizzard || MeteorOver(cell);
                yield return watch.Current;
            }
            c.Check(w.NewJotun == 0, $"no new Jotun in the cleared area for {F(w.Seconds)} s after the load ({w.NewJotun} new, {s.Left} were left alive)");
            c.Check(!storm && blizzard != null && WorldState.IsStormingCell(cell, rules) && !Storms.StormAt(pos, rules),
                "no blizzard and no meteor there, while an area that is not cleared would storm at this very moment (StormShare 100)");
            yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 1, 15f);
            c.Check(MapOverlay.Ready && MapOverlay.TestPixel(pos) == 1, $"no purple on the map there (pixel state {MapOverlay.TestPixel(pos)})");
            var line = First(RunConsole(AdminCommand.Name));
            c.Check(line.Contains("is cleared") && line.Contains("1 area(s) cleared"), $"deepnorth_stones there says the area is cleared: '{line}'");
        }
        finally
        {
            s.Handed = false;
            RelogCleanup(s, player);
        }
        yield return GoHome(c, player, _home.Value);
        c.Report();
    }

    // ---------- dn.relog.kills (T21) ----------

    private static IEnumerator RelogKillsBefore()
    {
        var c = new Checks(RelogKillsName);
        var s = _relogKills = new RelogState { MarkValue = 3 };
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        try
        {
            var ok = new Box();
            // Storms always, 6 kills clear the area.
            yield return RelogEnter(c, player, clock, s, SlotRelogKills, KallRules(100, 6), ok);
            if (ok.Ok)
            {
                var cell = s.Cell;
                var pos = player.transform.position;
                var slain = SlayArmy(pos, cell, 2, null);
                yield return WaitServerKills(cell, 2, 8f);
                yield return Until(() => HeldCells.KillsIn(cell) == 2, 3f);
                var line = First(RunConsole(AdminCommand.Name));
                var good = c.Check(slain == 2 && HeldCells.TestKills(cell) == 2 && HeldCells.TestKillSlots(cell) == 1 && !HeldCells.IsCleared(cell)
                                   && ReadOf(line, JotunDefeated, out var k, out var n) && k == 2 && n == 6,
                    $"2 of its Jotun killed: counted and kept on a zone control, deepnorth_stones shows 2 of 6 ('{line}')");
                yield return new WaitForSeconds(1.5f);
                var left = TaggedOf(cell);
                s.Left = MarkAll(left, s.MarkValue);
                s.LogoutAt = player.transform.position;
                s.Handed = good && c.Check(s.Left > 0, $"{s.Left} of the area's Jotun are left alive");
            }
        }
        finally
        {
            if (!s.Handed)
            {
                RelogCleanup(s, player);
            }
        }
        if (!s.Handed && s.Away)
        {
            yield return GoHome(c, player, _home.Value);
        }
        c.Report(s.Handed ? "; the player logs out standing in the area" : "");
    }

    private static IEnumerator RelogKillsAfter()
    {
        var s = _relogKills;
        if (!RelogReady(RelogKillsName, s))
        {
            yield break;
        }
        var c = new Checks(RelogKillsName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        try
        {
            var cell = s.Cell;
            var pos = s.LogoutAt;
            RelogLoaded(c, player, s, 3, true);
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKills(cell) == 2 && HeldCells.KillsIn(cell) == 2, 10f);
            c.Check(HeldCells.TestKills(cell) == 2 && HeldCells.KillsIn(cell) == 2 && !HeldCells.IsCleared(cell),
                $"the kills made before are read back from the saved world ({HeldCells.TestKills(cell)} of 6)");
            // Topped up on arrival: Jotun that were not there before the log out.
            yield return Until(() => HeldCells.TestServerEngaged(cell) && CountMarked(TaggedOf(cell), s.MarkValue, false) > 0, 15f);
            yield return WaitTagged(pos, 400f, 1, 4f);
            var now = TaggedOf(cell);
            var fresh = CountMarked(now, s.MarkValue, false);
            var same = CountMarked(now, s.MarkValue, true);
            c.Check(fresh > 0 && OneSet(now),
                $"the area is topped up right after the player arrives: {fresh} new Jotun joined the {same} of {s.Left} left before the log out, "
                + $"one set ({Kinds(now)})");
            c.Check(HeldCells.TestServerEngaged(cell) && HeldCells.IsEngaged(cell),
                "so it did not wait to be defeated at the load (that list is not saved); it does now");
            var line = First(RunConsole(AdminCommand.Name));
            c.Check(ReadOf(line, JotunDefeated, out var k, out var n) && k == 2 && n == 6 && line.Contains("waits to be defeated"),
                $"deepnorth_stones still shows the kills made before: '{line}'");
        }
        finally
        {
            s.Handed = false;
            RelogCleanup(s, player);
        }
        yield return GoHome(c, player, _home.Value);
        c.Report();
    }

    // ---------- dn.relog.killtarget (T24) ----------

    private static IEnumerator RelogTargetBefore()
    {
        var c = new Checks(RelogTargetName);
        var s = _relogTarget = new RelogState { MarkValue = 4 };
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        var spawned = new List<GameObject>();
        try
        {
            var ok = new Box();
            // ClearKillsMin = ClearKillsMax = 12.
            yield return RelogEnter(c, player, clock, s, SlotRelogTarget, KallRules(100, 12), ok);
            if (ok.Ok)
            {
                var cell = s.Cell;
                var pos = player.transform.position;
                // 9 Jotun to kill: the area's own, and Krigen from anywhere when it has fewer at hand (those die first:
                // none of them is left in the world).
                var extra = Mathf.Max(0, 9 - ArmyNear(pos, 90f, cell).Count);
                if (extra > 0)
                {
                    SpawnRing(Hostility.Krigen, pos, extra, 14f, spawned);
                    yield return Frames(3);
                }
                var slain = 0;
                foreach (var go in spawned)
                {
                    var ch = go != null ? go.GetComponent<Character>() : null;
                    if (ch != null && WorldState.CellAt(ch.transform.position) == cell)
                    {
                        Slay(ch);
                        slain++;
                    }
                }
                slain += SlayArmy(pos, cell, 9 - slain, null);
                yield return WaitServerKills(cell, 9, 12f);
                yield return Until(() => HeldCells.KillsIn(cell) == 9, 3f);
                var line = First(RunConsole(AdminCommand.Name));
                var good = c.Check(slain == 9 && HeldCells.TestKills(cell) == 9 && !HeldCells.IsCleared(cell) && HeldCells.TestKillSlots(cell) == 1
                                   && ReadOf(line, JotunDefeated, out var k, out var n) && k == 9 && n == 12,
                    $"ClearKillsMin = ClearKillsMax = 12 and 9 Jotun killed in the area: deepnorth_stones shows 9 of 12 ('{line}')");
                yield return new WaitForSeconds(1.5f);
                s.Left = MarkAll(TaggedNear(pos, 400f), s.MarkValue);
                s.LogoutAt = player.transform.position;
                s.Handed = good;
            }
        }
        finally
        {
            KillAll(spawned);
            if (!s.Handed)
            {
                RelogCleanup(s, player);
            }
        }
        if (!s.Handed && s.Away)
        {
            yield return GoHome(c, player, _home.Value);
        }
        c.Report(s.Handed ? "; the player logs out standing in the area" : "");
    }

    // At the main menu, no world loaded: the lower values. (A person edits the config file here; a test never writes it.)
    private static IEnumerator RelogTargetMenu()
    {
        var s = _relogTarget;
        if (s == null || !s.Handed)
        {
            SelfTest.Fail(RelogTargetName, "at the main menu: the part before the log out did not get its place ready, the kill target was not lowered");
            yield break;
        }
        ServerRules.TestRules = KallRules(100, 8);
        s.LogMark = EnsureTap().Mark();
        if (Player.m_localPlayer == null && ZoneSystem.instance == null && ServerRules.Current.ClearKillsMin == 8 && ServerRules.Current.ClearKillsMax == 8)
        {
            SelfTest.Pass(RelogTargetName, "at the main menu, no world loaded: ClearKillsMin = ClearKillsMax = 8 are the rules for the next load (in memory)");
        }
        else
        {
            SelfTest.Fail(RelogTargetName, $"at the main menu: player {Player.m_localPlayer != null}, world {ZoneSystem.instance != null}, "
                                           + $"kill target {ServerRules.Current.ClearKillsMin}-{ServerRules.Current.ClearKillsMax} (expected no player, no world, 8-8)");
        }
    }

    private static IEnumerator RelogTargetAfter()
    {
        var s = _relogTarget;
        if (!RelogReady(RelogTargetName, s))
        {
            yield break;
        }
        var c = new Checks(RelogTargetName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var clock = new Clock();
        var tap = EnsureTap();
        try
        {
            var cell = s.Cell;
            var pos = s.LogoutAt;
            // First thing, no wait: the world is loaded since several seconds.
            var atOnce = HeldCells.Scanned && HeldCells.TestServerCleared(cell);
            RelogLoaded(c, player, s, 3, true);
            c.Check(ServerRules.Current.ClearKillsMin == 8 && ServerRules.Current.ClearKillsMax == 8, "the world was loaded with ClearKillsMin = ClearKillsMax = 8");
            yield return Until(() => HeldCells.Scanned && HeldCells.TestKnown && HeldCells.IsCleared(cell), 6f);
            c.Check(atOnce && HeldCells.IsCleared(cell) && HeldCells.TestMarkedAnywhere(cell) && HeldCells.TestKills(cell) == 0
                    && HeldCells.TestKillSlots(cell) == 0,
                $"9 kills kept in the world, the target lowered to 8 while no world was loaded: the area is cleared at once after the load, without "
                + $"another kill (cleared when the test looked first: {atOnce}; written in the world {HeldCells.TestMarkedAnywhere(cell)})");
            Cells.FromId(cell, out var ci, out var cj);
            var logLine = $"9 Jotun defeated in Deep North area {ci},{cj}, where 8 are enough now: the area is cleared for good.";
            c.Check(tap.Count(s.LogMark, logLine) == 1, $"the log has \"{logLine}\" once since the main menu ({tap.Count(s.LogMark, logLine)})");
            var line = First(RunConsole(AdminCommand.Name));
            c.Check(line.Contains("is cleared") && line.IndexOf("9 of 8", StringComparison.Ordinal) < 0,
                $"deepnorth_stones there says the area is cleared and never shows more kills than it needs: '{line}'");
            var w = new StayWatch();
            yield return WatchStay(() => CountMarked(TaggedNear(pos, 400f), s.MarkValue, false), HeldCells.NoTag, clock.Cap(6f), w);
            c.Check(w.NewJotun == 0 && !HeldCells.MayBurst(cell) && Storms.Override() == null,
                $"cleared like in T12: no new Jotun in {F(w.Seconds)} s ({w.NewJotun}), no blizzard (StormShare 100)");
        }
        finally
        {
            s.Handed = false;
            RelogCleanup(s, player);
        }
        yield return GoHome(c, player, _home.Value);
        c.Report();
    }

    // ---------- dn.disabled.north (T17) ----------

    private const string CarryStones = DisabledNorthName + ".stones";

    // Patched game methods that carry a patch of this mod (its Harmony id = its GUID), of how many patched methods.
    private static int OwnPatches(out int all)
    {
        var mine = 0;
        all = 0;
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            all++;
            var info = Harmony.GetPatchInfo(method);
            if (info != null && info.Owners.Contains(ModInfo.Guid))
            {
                mine++;
            }
        }
        return mine;
    }

    // First game, mod on: the world is left awake for a game with the mod (the real count key; the save keeps it).
    private static IEnumerator DisabledNorthLeave()
    {
        WorldState.TestStones = null;
        WorldState.TestKall = null;
        WorldState.WriteStones(3);
        var mine = OwnPatches(out var all);
        var carried = SelfTest.Carry(CarryStones, "3");
        if (ModActive() && RealKeyIs(3) && mine > 0 && carried && SelfTest.Stage == "")
        {
            SelfTest.Pass(DisabledNorthName, $"first game, mod on ({mine} of {all} patched game methods carry its patches): the world is left with "
                                             + "mc_dn_stones 3, stage 3 for a game with the mod");
        }
        else
        {
            SelfTest.Fail(DisabledNorthName, $"first game: mod active {ModActive()}, world count 3 {RealKeyIs(3)}, own patches {mine} of {all}, "
                                             + $"carried {carried}, stage '{SelfTest.Stage}'");
        }
        yield break;
    }

    // Second game, every MC mod Enabled = false from the start: the Deep North is the normal game. Only game objects and
    // pure helpers here: the mod's patches are not applied, OnActivated never ran.
    private static IEnumerator DisabledNorthCheck()
    {
        var c = new Checks(DisabledNorthName);
        var player = Player.m_localPlayer;
        var home = player.transform.position;
        var clock = new Clock();
        var zs = ZoneSystem.instance;
        var pes = PersistentEventSystem.instance;
        var events = pes != null ? new List<PersistentEventSystem.ActivePersistentEvent>(pes.m_activePersistentEvents.list) : null;
        var spawned = new List<GameObject>();
        var stones = new List<GameObject>();
        var hud = new HudLog();
        var away = false;
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && view != null && view.Value.IsDisabledByUser,
                $"second game: the mod is off from the start (stage '{SelfTest.Stage}', state {(view != null ? view.Value.State : "?")}, "
                + $"status \"{(view != null ? view.Value.Status : "")}\")");
            var has = zs.GetGlobalKey(WorldState.StonesKey, out string value);
            c.Check(has && value == "3" && SelfTest.Carried(CarryStones) == "3",
                $"the saved world still says 3 Malicious Ice broken (mc_dn_stones '{value}'): a game with the mod would be at stage 3");
            var mine = OwnPatches(out var all);
            c.Check(mine == 0, $"none of the mod's patches is in the game ({mine} of {all} patched game methods carry one)");
            c.Check(!Terminal.commands.ContainsKey(AdminCommand.Name), "the deepnorth_stones command does not exist");
            var haveArea = LandSlot(SlotDisabled, out var point, out var cell);
            var index = Stones.InvasionIndex(pes);
            if (!c.Check(haveArea && pes != null && index >= 0 && EnvMan.instance != null && Minimap.instance != null,
                    "an area that the mod's default settings invade at stage 3 exists (and the game has its Jotun invasion event)"))
            {
                c.Report();
                yield break;
            }
            hud.Begin();
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var env = EnvMan.instance;
                // 18 s there (a zone starts its spawn checks 10 s after it loads): nothing of the mod.
                var known = new HashSet<Character>(Character.GetAllCharacters());
                var marked = 0;
                var forced = "";
                var meteor = false;
                var watched = clock.Cap(18f, 1);
                var end = Time.realtimeSinceStartup + watched;
                while (Time.realtimeSinceStartup < end)
                {
                    foreach (var ch in Character.GetAllCharacters())
                    {
                        var zdo = ZdoOf(ch);
                        if (zdo == null || known.Contains(ch))
                        {
                            continue;
                        }
                        known.Add(ch);
                        if (zdo.GetInt(HeldCells.TagKey, HeldCells.NoTag) != HeldCells.NoTag || zdo.GetInt(AreaSpawns.BandKey, 0) != 0)
                        {
                            marked++;
                        }
                    }
                    var now = env.GetEnvironmentOverride();
                    if (!string.IsNullOrEmpty(now))
                    {
                        forced = now;
                    }
                    meteor |= Meteors().Count > 0;
                    yield return new WaitForSeconds(0.5f);
                }
                // The weather: whatever the game itself decides there (no patch of the mod is on its weather question, see
                // above); a weather the game forces by itself in that place is only told.
                c.Check(marked == 0 && !meteor,
                    $"{F(watched)} s in a place the mod would invade: no area Jotun and no nature band appear ({marked}), no meteor ({meteor}); "
                    + $"the game plays {CurrentEnv()}" + (forced.Length > 0 ? $" (it forces '{forced}' there by itself)" : " (nothing forces a weather)"));

                // The map: the game's own colour under the player.
                var map = Minimap.instance;
                var size = map.m_textureSize;
                var pixel = map.m_pixelSize;
                var row = Mathf.FloorToInt((pos.z - pixel / 2f) / pixel) + size / 2;
                var column = Mathf.FloorToInt((pos.x - pixel / 2f) / pixel) + size / 2;
                var biome = WorldGenerator.instance.GetBiome((column - size / 2) * pixel + pixel / 2f, (row - size / 2) * pixel + pixel / 2f);
                Color32 own = map.GetPixelColor(biome);
                var read = MapOverlay.TestRawColor(pos, out var raw);
                c.Check(read && SameColor(raw, own), $"the map has the game's own colour under the player ({raw} on the map, {own} for {biome}): no purple");

                // Creatures: Krigen and Gammeltroll are friends, as in the normal game.
                var fwd = Flat(player.transform.forward).normalized;
                if (fwd == Vector3.zero)
                {
                    fwd = Vector3.forward;
                }
                var side = Vector3.Cross(Vector3.up, fwd);
                var krigenObject = Spawn(Hostility.Krigen, pos + fwd * 30f, fwd);
                var trollObject = Spawn(Hostility.Gammeltroll, pos + fwd * 36f, -fwd);
                spawned.Add(krigenObject);
                spawned.Add(trollObject);
                yield return null;
                var krigen = krigenObject != null ? krigenObject.GetComponent<Character>() : null;
                var troll = trollObject != null ? trollObject.GetComponent<Character>() : null;
                if (c.Check(krigen != null && troll != null, "spawned a Krigen and a Gammeltroll face to face"))
                {
                    var krigenAi = krigen.GetComponent<MonsterAI>();
                    var trollAi = troll.GetComponent<MonsterAI>();
                    var fought = false;
                    var until = Time.realtimeSinceStartup + 5f;
                    while (Time.realtimeSinceStartup < until && krigen != null && troll != null)
                    {
                        fought |= (krigenAi != null && krigenAi.m_targetCreature == troll) || (trollAi != null && trollAi.m_targetCreature == krigen);
                        yield return new WaitForSeconds(0.25f);
                    }
                    c.Check(krigen != null && troll != null && !BaseAI.IsEnemy(krigen, troll) && !BaseAI.IsEnemy(troll, krigen) && !fought,
                        "Krigen and Gammeltroll are friends and for 5 s neither targets the other (3 broken in the world)");
                }
                KillAll(spawned);

                // A Malicious Ice broken: the normal game (its banner, one invasion at once), the saved count untouched.
                pes.m_activePersistentEvents.list.Clear();
                pes.UpdateClientEventsList(0L);
                var advance = Localize(Stones.StoneText);
                yield return BreakStone(pos + side * 8f, stones, 0f, box);
                c.Check(box.Ok && Stones.ActiveInvasions(pes, index) == 1,
                    $"a broken Malicious Ice starts one Jotun invasion at once, like in the normal game ({Stones.ActiveInvasions(pes, index)} running)");
                yield return new WaitForSeconds(1.2f);
                has = zs.GetGlobalKey(WorldState.StonesKey, out value);
                c.Check(hud.Count(advance) == 1 && has && value == "3",
                    $"\"{advance}\" shown once ({hud.Count(advance)}); nobody counts the stone: the world still has mc_dn_stones {value}");
            }
        }
        finally
        {
            foreach (var go in stones)
            {
                KillStone(go);
            }
            KillAll(spawned);
            RestoreEvents(pes, events);
            hud.End();
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.disabled.cleanlog (T18) ----------

    // End of the first game: every normal and staged test of this run is behind.
    private static IEnumerator DisabledLogLeave()
    {
        var tap = EnsureTap();
        tap.Errors(out var errors, out var first);
        if (errors == 0)
        {
            SelfTest.Pass(DisabledLogName, $"first game, staged tests included: no error line from {ModInfo.Name} since the game loaded it "
                                           + $"({tap.Seen} of its log lines seen)");
        }
        else
        {
            SelfTest.Fail(DisabledLogName, $"first game: {errors} error line(s) from {ModInfo.Name} since the game loaded it, first: {first}");
        }
        yield break;
    }

    private static IEnumerator DisabledLogCheck()
    {
        var tap = EnsureTap();
        tap.Errors(out var errors, out var first);
        if (errors == 0 && SelfTest.Stage == SelfTest.DisabledStage)
        {
            SelfTest.Pass(DisabledLogName, $"second game, mod off: no error line from {ModInfo.Name} since the game loaded it ({tap.Seen} of its log lines seen)");
        }
        else
        {
            SelfTest.Fail(DisabledLogName, $"second game (stage '{SelfTest.Stage}'): {errors} error line(s) from {ModInfo.Name}, first: {first}");
        }
        yield break;
    }
}
#endif
