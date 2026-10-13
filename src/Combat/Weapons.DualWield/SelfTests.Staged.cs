#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. Staged self tests (same class and helper as SelfTests.cs): thing that only show after a real log
// out or a real restart. The probe run them after every normal test (docs/modding/framework.md, "Staged tests").
//   dual.relog.pair       T19: SwordIron + AxeIron held, real log out to the main menu and back in: same hands
//   dual.relog.off-last   T19: off-hand weapon dragged to another slot first (it is saved and loaded last): same hands
//   dual.relog.main-last  T19: main weapon dragged to another slot first (now it load last): same hands
//   dual.disabled.pair    L02: first game leave the pair held at the quit (so the game quit while dual wielding,
//                         L03); second game, every MC mod Enabled = false from the start: status off, the pair load as
//                         one weapon in the main hand, a second one-handed weapon replace the first (vanilla equip)
// One relog per test, one test per case: each can be fixed or dropped alone. Items left in the bag over the log out or
// the restart carry a mark (custom data) so the part after find and remove them. Relog tests register in OnActivated
// (RegisterAutoPlaces); the disabled-start test in BindConfig (RegisterStaged): the mod never turn on in the second game.
internal static partial class SelfTests
{
    private const string RelogPairName = "dual.relog.pair";
    private const string RelogOffLastName = "dual.relog.off-last";
    private const string RelogMainLastName = "dual.relog.main-last";
    private const string DisabledPairName = "dual.disabled.pair";

    // Mark on item a staged test (or a test that make a new player object) gives: value = test's name.
    private const string TestMarkKey = ModInfo.Guid + ".SelfTest";

    // One relog case. Statics of the mod live over the log out (same game process).
    private sealed class RelogCase
    {
        internal readonly string Name;
        internal readonly int Drag;   // 0 = none, 1 = off-hand weapon dragged to another slot, 2 = main weapon dragged
        internal bool Armed;          // the part before the log out left the pair as wanted
        internal string OwnRight = "";
        internal string OwnLeft = "";
        internal int Warnings;
        internal int Errors;
        internal string Order = "";

        internal RelogCase(string name, int drag)
        {
            Name = name;
            Drag = drag;
        }
    }

    private static readonly RelogCase[] RelogCases =
    {
        new RelogCase(RelogPairName, 0), new RelogCase(RelogOffLastName, 1), new RelogCase(RelogMainLastName, 2),
    };

    // ---------- registration (tests of SelfTests.Input, SelfTests.Mods, SelfTests.Real, SelfTests.MpMore and this file) ----------

    // In-world test that swing, in running order. OnActivated (RegisterMore), before the test that sit, lie down or eat.
    private static void RegisterAutoFights()
    {
        SelfTest.Register(InputSwapName, RunInputSwap);
        SelfTest.Register(InputKeysName, RunInputKeys);
        SelfTest.Register(InputPadName, RunInputPad);
        SelfTest.Register(InputSwapAttackName, RunInputSwapAttack);
        SelfTest.Register(InputSwapWalkName, RunInputSwapWalk);
        SelfTest.Register(XMovesetName, RunXMoveset);
        SelfTest.Register(XAmbushName, RunXAmbush);
        SelfTest.Register(XAmbushMoraleName, RunXAmbushMorale);
        SelfTest.Register(XMoraleName, RunXMorale);
        SelfTest.Register(XKillsPageName, RunXKillsPage);
    }

    // In-world test that use stations, a bed, water, and the two that die (last: they leave a new player object).
    // OnActivated (RegisterMore), right before dual.log. The relog tests run after every normal test anyway.
    private static void RegisterAutoPlaces()
    {
        SelfTest.Register(XForgeName, RunXForge);
        SelfTest.Register(RealBarberName, RunRealBarber);
        SelfTest.Register(RealSleepName, RunRealSleep);
        SelfTest.Register(RealSwimName, RunRealSwim);
        SelfTest.Register(RealSwimSheathName, RunRealSwimSheath);
        SelfTest.Register(RealFeastWaterName, RunRealFeastWater);
        SelfTest.Register(RealDeathName, RunRealDeath);
        SelfTest.Register(RealDeathKeepName, RunRealDeathKeep);
        foreach (var relog in RelogCases)
        {
            var r = relog;
            SelfTest.RegisterRelog(r.Name, () => RelogBefore(r), null, () => RelogAfter(r));
        }
    }

    private static readonly string[] AutoTests =
    {
        InputSwapName, InputKeysName, InputPadName, InputSwapAttackName, InputSwapWalkName, XMovesetName, XAmbushName,
        XAmbushMoraleName, XMoraleName, XKillsPageName, XForgeName, RealBarberName, RealSleepName, RealSwimName,
        RealSwimSheathName, RealFeastWaterName, RealDeathName, RealDeathKeepName,
    };

    // OnDeactivated (UnregisterMore).
    private static void UnregisterAuto()
    {
        foreach (var name in AutoTests)
        {
            SelfTest.Unregister(name);
        }
        foreach (var relog in RelogCases)
        {
            SelfTest.UnregisterRelog(relog.Name);
        }
        UnregisterMoreMultiplayerTests();
    }

    // Plugin.BindConfig (RegisterAlways: every start). Never unregistered: in the second game the mod is off from the
    // start, OnActivated never runs.
    private static void RegisterStaged()
    {
        SelfTest.RegisterDisabledStart(DisabledPairName, DisabledPairLeave, DisabledPairCheck);
    }

    // ---------- marked items, own hands ----------

    private static ItemDrop.ItemData GiveMarked(Player p, string prefab, string mark)
    {
        var item = p.GetInventory().AddItem(prefab, 1, 1, 0, 0L, "", false);
        if (item != null)
        {
            item.m_customData[TestMarkKey] = mark;
        }
        return item;
    }

    private static List<ItemDrop.ItemData> Marked(Player p, string mark) =>
        p.GetInventory().GetAllItems()
            .Where(i => i != null && i.m_customData.TryGetValue(TestMarkKey, out var m) && m == mark).ToList();

    private static void RemoveMarked(Player p, string mark)
    {
        foreach (var item in Marked(p, mark))
        {
            p.RemoveEquipAction(item);
            p.UnequipItem(item, false);
            p.GetInventory().RemoveItem(item);
        }
    }

    // Prefab name of an item ("" = none).
    private static string PrefabOf(ItemDrop.ItemData item) => item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : "";

    // Both hand and both hidden hands emptied through vanilla (items stay in the bag).
    private static void EmptyHands(Player p)
    {
        foreach (var held in new[] { p.m_rightItem, p.m_leftItem, p.m_hiddenRightItem, p.m_hiddenLeftItem })
        {
            if (held != null)
            {
                p.UnequipItem(held, false);
            }
        }
    }

    // The player's own hand items (never a test item) held again, by prefab name: main first, then off hand.
    private static void HoldAgain(Player p, string right, string left)
    {
        foreach (var prefab in new[] { right, left })
        {
            if (string.IsNullOrEmpty(prefab))
            {
                continue;
            }
            var item = p.GetInventory().GetAllItems().FirstOrDefault(i => i != null && PrefabOf(i) == prefab
                                                                         && !i.m_customData.ContainsKey(TestMarkKey)
                                                                         && !p.IsItemEquiped(i));
            if (item != null)
            {
                p.EquipItem(item, false);
            }
        }
    }

    // Warning and error line the mod logged after these counts, less the ones tests provoke on purpose and the
    // tests' own FAIL lines.
    private static string[] LogProblemsSince(int warnings, int errors)
    {
        var odd = LogWatch.WarningLines().Skip(warnings)
            .Where(w => !ProvokedWarnings.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
        var bad = LogWatch.ErrorLines().Skip(errors).Where(e => !e.Contains(SelfTest.Prefix + " FAIL"));
        return odd.Concat(bad).ToArray();
    }

    // ---------- dual.relog.* (T19) ----------

    private static IEnumerator RelogBefore(RelogCase r)
    {
        r.Armed = false;
        var player = Player.m_localPlayer;
        if (player == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(r.Name, "before the log out: no local player");
            yield break;
        }
        yield return WaitIdle(player);
        var c = new Checks(r.Name);
        var ok = true;
        try
        {
            r.OwnRight = PrefabOf(player.m_rightItem ?? player.m_hiddenRightItem);
            r.OwnLeft = PrefabOf(player.m_leftItem ?? player.m_hiddenLeftItem);
            r.Warnings = LogWatch.WarningLines().Length;
            r.Errors = LogWatch.ErrorLines().Length;
            EmptyHands(player);
            // Built-in default rule and no main-hand key over the whole relog (statics live on): the load must not
            // depend on the player's own settings.
            ServerRules.TestRules = DualRules.Defaults;
            Controls.TestMainHandHeld = false;
            RemoveMarked(player, r.Name);
            var sword = GiveMarked(player, Sword, r.Name);
            var axe = GiveMarked(player, Axe, r.Name);
            if (sword == null || axe == null)
            {
                ok = false;
                c.Check(false, $"could not give {Sword} and {Axe} (inventory full?)");
            }
            else
            {
                player.EquipItem(sword, false);
                player.EquipItem(axe, false);
                yield return null;
                yield return null;
                var paired = Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword);
                ok &= paired;
                c.Check(paired, $"before the log out: {Sword} in the main hand, {Axe} in the off hand ({HandsText(player)})");
                if (paired && r.Drag != 0)
                {
                    // T19: the drag as the inventory screen do it (InventoryGrid.DropItem, or the same move by hand
                    // while the grid have not shown the inventory yet): the dragged weapon is re-added, so it is last
                    // in the inventory list, which is the order the save keep and the load equip in.
                    var inventory = player.GetInventory();
                    var gui = InventoryGui.instance;
                    var grid = gui != null ? gui.m_playerGrid : null;
                    if (grid == null || !ReferenceEquals(grid.GetInventory(), inventory))
                    {
                        grid = null;
                    }
                    var empty = EmptySlot(inventory);
                    var room = empty.x >= 0;
                    ok &= room;
                    c.Check(room, "a free inventory slot to drag the weapon to");
                    if (room)
                    {
                        Drag(player, inventory, grid, r.Drag == 1 ? player.m_leftItem : player.m_rightItem, empty);
                        yield return null;
                        yield return null;
                    }
                }
                if (ok)
                {
                    var main = player.m_rightItem;
                    var off = player.m_leftItem;
                    var list = player.GetInventory().m_inventory;
                    var mainAt = main != null ? list.IndexOf(main) : -1;
                    var offAt = off != null ? list.IndexOf(off) : -1;
                    var held = PrefabOf(main) == Sword && PrefabOf(off) == Axe && Marked(player, r.Name).Count == 2
                               && Hands.IsMarked(off) && !Hands.IsMarked(main) && main.m_equipped && off.m_equipped;
                    ok &= held;
                    c.Check(held, $"before the log out{(r.Drag != 0 ? ", after the drag" : "")}: the same hands, both flagged "
                                  + $"equipped, the marker on the off-hand weapon ({HandsText(player)})");
                    r.Order = mainAt < offAt ? "main weapon first, off-hand weapon last" : "off-hand weapon first, main weapon last";
                    if (r.Drag != 0)
                    {
                        var last = r.Drag == 1 ? offAt > mainAt : mainAt > offAt;
                        ok &= last && mainAt >= 0 && offAt >= 0;
                        c.Check(last && mainAt >= 0 && offAt >= 0,
                            $"the dragged {(r.Drag == 1 ? "off-hand" : "main")} weapon is now the later one of the pair in the "
                            + $"inventory list, so it loads last (list places: main {mainAt}, off hand {offAt})");
                    }
                    Invariant(c, player, "before the log out");
                    ok &= Broken(player) == null;
                }
            }
            r.Armed = ok;
        }
        finally
        {
            if (!r.Armed)
            {
                // Nothing to carry over the log out: bag and hand as before, overrides gone.
                RemoveMarked(player, r.Name);
                HoldAgain(player, r.OwnRight, r.OwnLeft);
                Bench.ClearOverrides();
            }
        }
        c.Report($" (before the log out; inventory list: {r.Order})");
    }

    private static IEnumerator RelogAfter(RelogCase r)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            r.Armed = false;
            Bench.ClearOverrides();
            SelfTest.Fail(r.Name, "after logging back in: no local player");
            yield break;
        }
        if (!r.Armed)
        {
            RemoveMarked(player, r.Name);
            Bench.ClearOverrides();
            SelfTest.Fail(r.Name, "after logging back in: nothing checked, the part before the log out did not leave the pair held");
            yield break;
        }
        var c = new Checks(r.Name);
        try
        {
            yield return null;
            var mine = Marked(player, r.Name);
            var sword = mine.FirstOrDefault(i => PrefabOf(i) == Sword);
            var axe = mine.FirstOrDefault(i => PrefabOf(i) == Axe);
            c.Check(mine.Count == 2 && sword != null && axe != null,
                $"after logging back in: both test weapons are in the bag ({mine.Count} marked item(s))");
            if (sword != null && axe != null)
            {
                c.Check(Holds(player, sword, axe),
                    $"after logging back in ({r.Order}): the same hands, {Sword} main and {Axe} off ({HandsText(player)})");
                c.Check(sword.m_equipped && axe.m_equipped && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                    $"both flagged equipped, the off-hand marker on the axe only (sword: equipped {sword.m_equipped}, marker "
                    + $"{Hands.IsMarked(sword)}; axe: equipped {axe.m_equipped}, marker {Hands.IsMarked(axe)})");
                c.Check(player.m_hiddenRightItem == null && player.m_hiddenLeftItem == null,
                    $"nothing put away after the load ({HandsText(player)})");
                Invariant(c, player, "after logging back in");
                var vis = player.m_visEquipment;
                c.Check(vis != null && vis.m_currentRightItemHash == Sword.GetStableHashCode()
                        && vis.m_currentLeftItemHash == Axe.GetStableHashCode(),
                    "the character shows the sword in the right hand and the axe in the left hand");
                // The pair work for the player object of this log in: put away and drawn, the same hands.
                player.HideHandItems();
                yield return null;
                yield return null;
                var hidden = ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, axe);
                player.ShowHandItems();
                yield return null;
                yield return null;
                c.Check(hidden && Holds(player, sword, axe),
                    $"after logging back in: put away and drawn again, the same hands (both put away {hidden}; {HandsText(player)})");
            }
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(feature != null && feature.Value.IsActive,
                $"the mod is active after logging back in ({(feature != null ? feature.Value.Status : "not registered")})");
            SelfTest.Note(r.Name, ReferenceEquals(ServerRules.TestRules, DualRules.Defaults)
                ? "the built-in default rules stayed in force over the whole relog"
                : $"the test's in-memory rules were dropped during the relog: the load ran with {ServerRules.Current.Describe()}");
            var problems = LogProblemsSince(r.Warnings, r.Errors);
            c.Check(problems.Length == 0,
                $"no warning or error from {ModInfo.Name} from the pairing to after the log in ({problems.Length}: {string.Join(" | ", problems)})");
        }
        finally
        {
            r.Armed = false;
            RemoveMarked(player, r.Name);
            HoldAgain(player, r.OwnRight, r.OwnLeft);
            Bench.ClearOverrides();
            Hands.ResetState();
        }
        c.Report(" (after logging back in)");
    }

    // ---------- dual.disabled.pair (L02; the quit while dual wielding of L03) ----------

    // First game, mod on, last thing before the quit: the pair held (saved with the character). The player's own
    // settings rule here (no in-memory rules: they would not be there at the quit).
    private static IEnumerator DisabledPairLeave()
    {
        var player = Player.m_localPlayer;
        if (player == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(DisabledPairName, "first game: no local player");
            yield break;
        }
        yield return WaitIdle(player);
        var c = new Checks(DisabledPairName);
        var held = false;
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        c.Check(feature != null && feature.Value.IsActive,
            $"first game: the mod is active ({(feature != null ? feature.Value.Status : "not registered")})");
        SelfTest.Carry(DisabledPairName + ".right", PrefabOf(player.m_rightItem ?? player.m_hiddenRightItem));
        SelfTest.Carry(DisabledPairName + ".left", PrefabOf(player.m_leftItem ?? player.m_hiddenLeftItem));
        EmptyHands(player);
        RemoveMarked(player, DisabledPairName);
        var sword = GiveMarked(player, Sword, DisabledPairName);
        var axe = GiveMarked(player, Axe, DisabledPairName);
        if (sword != null && axe != null)
        {
            player.EquipItem(sword, false);
            player.EquipItem(axe, false);
            yield return null;
            yield return null;
            held = Holds(player, sword, axe) && Hands.IsMarked(axe) && sword.m_equipped && axe.m_equipped;
        }
        c.Check(held, $"first game (mod on): {Sword} in the main hand and {Axe} in the off hand for the quit ({HandsText(player)}; "
                      + $"rules in force: {ServerRules.Current.Describe()})");
        SelfTest.Carry(DisabledPairName + ".pair", held ? "1" : "0");
        if (!held)
        {
            RemoveMarked(player, DisabledPairName);
        }
        c.Report(" (first game: the pair is held when the game quits)");
    }

    // Second game: Enabled = false in the cfg since before the start. The mod's patches not applied and
    // OnActivated never ran: only vanilla call and pure helper here.
    private static IEnumerator DisabledPairCheck()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(DisabledPairName, "second game: no local player");
            yield break;
        }
        var c = new Checks(DisabledPairName);
        try
        {
            yield return WaitIdle(player);
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && feature != null
                    && feature.Value.State == nameof(ModState.Disabled) && feature.Value.Status == "Off (disabled in settings).",
                $"started with Enabled = false: stage '{SelfTest.Stage}', state {(feature != null ? feature.Value.State : "?")}, "
                + $"status '{(feature != null ? feature.Value.Status : "?")}'");
            var carried = SelfTest.Carried(DisabledPairName + ".pair");
            c.Check(carried == "1", $"the first game quit with the pair held (carried '{carried ?? "nothing"}')");
            var mine = Marked(player, DisabledPairName);
            var sword = mine.FirstOrDefault(i => PrefabOf(i) == Sword);
            var axe = mine.FirstOrDefault(i => PrefabOf(i) == Axe);
            c.Check(mine.Count == 2 && sword != null && axe != null,
                $"both weapons of the saved pair are in the bag ({mine.Count} marked item(s))");
            if (sword != null && axe != null)
            {
                // L02: the pair load as one weapon. Vanilla equip every flagged item in inventory list order; the
                // second one-handed weapon take the first one's place in the main hand.
                var inHand = mine.Where(i => player.IsItemEquiped(i)).ToList();
                var one = inHand.Count == 1 ? inHand[0] : null;
                var other = ReferenceEquals(one, sword) ? axe : sword;
                c.Check(one != null && ReferenceEquals(player.m_rightItem, one) && player.m_leftItem == null
                        && player.m_hiddenRightItem == null && player.m_hiddenLeftItem == null,
                    $"the saved pair loads as one weapon, in the main hand, the off hand empty ({inHand.Count} of the two "
                    + $"equipped; {HandsText(player)})");
                c.Check(one != null && one.m_equipped && !other.m_equipped,
                    $"only the weapon in hand is flagged equipped (sword {sword.m_equipped}, axe {axe.m_equipped})");
                // The save really held the pair (the first game quit while dual wielding; "held when the leave part
                // ended" alone no prove it: other mods' leave parts run after). Proof in the loaded character: the game
                // equip every flagged item in bag list order, so of two flagged one-handed weapons the later one
                // stays in hand; and the axe still carry the off-hand marker: an equipped weapon carry it only in the
                // off hand (in the main hand the mod take it off). Only the sword held at the quit = the sword (the
                // earlier one) in hand here; only the axe held = it was the main weapon, no marker.
                var list = player.GetInventory().m_inventory;
                var later = list.IndexOf(sword) > list.IndexOf(axe) ? sword : axe;
                c.Check(one != null && ReferenceEquals(one, later) && Hands.IsMarked(axe),
                    $"the character was saved with the pair held: the later one of the two in the bag list ({Name(later)}) is the "
                    + $"one in hand ({Name(one)}), and the axe still carries the off-hand marker ({Hands.IsMarked(axe)})");
                if (one != null)
                {
                    // L02: equipping is vanilla. The inventory right-click on the other weapon (a queued equip).
                    player.UseItem(null, other, true);
                    var until = Time.time + 4f;
                    while (Time.time < until && !player.IsItemEquiped(other))
                    {
                        yield return null;
                    }
                    yield return WaitIdle(player);
                    yield return null;
                    c.Check(ReferenceEquals(player.m_rightItem, other) && player.m_leftItem == null && !player.IsItemEquiped(one)
                            && !one.m_equipped && other.m_equipped,
                        $"equipping is vanilla: the second one-handed weapon replaces the first ({HandsText(player)})");
                }
            }
        }
        finally
        {
            RemoveMarked(player, DisabledPairName);
            HoldAgain(player, SelfTest.Carried(DisabledPairName + ".right"), SelfTest.Carried(DisabledPairName + ".left"));
        }
        c.Report(" (second game, every MC mod off from the start)");
    }
}
#endif
