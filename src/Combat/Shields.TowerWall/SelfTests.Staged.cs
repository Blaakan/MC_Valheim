#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only. Self tests that go through a real log out, a real restart with the mod off, or a real leave of a
// server (docs/modding/framework.md, "Staged tests"). One small test per thing, so one can be fixed or dropped alone.
// Registered from Plugin.BindConfig (never removed): the relog and disabled-start tests, and the vanilla-server one
// (mod not active there). Each part say why and stop when the mod is not active.
//   tower.relog.menu         TESTING T41 (and T20): log out to the main menu and load the world again: no '(MISSING)'
//                            Towers warning at the menu or while loading, mod active at the menu, towers work after
//   tower.relog.hands        TESTING M16 (single-player half): mod turned off, sword + tower shield held together,
//                            log out, mod turned on at the main menu, world loaded: only one of the two is equipped,
//                            the other in the inventory, no error
//   tower.disabled.vanilla   TESTING T19: tower shield left in hand in the first game; second game started with
//                            Enabled = false: normal shield (prefab and the saved item), one-handed, normal block
//                            armor, no Braced effect, attack punches
//   tower.mp.leave           TESTING M18 (scenario vanilla-server): log out from a server without the mod: no
//                            '(MISSING)' warning, mod active again at the main menu
//   tower.mp.leave-off       TESTING M18 (scenario modded): the same from a server that has the mod turned off. The
//                            server turns its mod off for 30 s and on again by itself (the client is at the menu then)
internal static partial class SelfTests
{
    private const string RelogMenuName = "tower.relog.menu";
    private const string RelogHandsName = "tower.relog.hands";
    private const string DisabledName = "tower.disabled.vanilla";
    private const string MpLeaveName = "tower.mp.leave";
    private const string MpLeaveOffName = "tower.mp.leave-off";
    private const string OffForStep = "tower.mp.off-for";

    // Crafter name on the items a staged test leaves in the inventory: found again after a load, by nothing else.
    private const string TestCrafter = "MC tower test";
    private const string CarryKey = "tower.disabled.vanilla.snap";
    private const float OffSeconds = 30f;

    // True = the log tap was made at BindConfig, before the mod first went active: it saw every line of this game.
    private static bool _tapFromStart;

    // Plugin.BindConfig (through RegisterAlways). Never removed.
    private static void RegisterAutoAlways()
    {
        _tapFromStart = _tap == null;
        EnsureTap();
        SelfTest.RegisterRelog(RelogMenuName, MenuBefore, MenuAtMenu, MenuAfter);
        SelfTest.RegisterRelog(RelogHandsName, HandsBefore, HandsAtMenu, HandsAfter);
        SelfTest.RegisterDisabledStart(DisabledName, DisabledLeave, DisabledCheck);
        SelfTest.RegisterMultiplayer(MpLeaveName, VanillaServerScenario, RunMpLeave);
    }

    private static void RegisterAutoMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpLeaveOffName, SelfTest.Modded, RunMpLeaveOff);
        SelfTest.RegisterServerStep(OffForStep, ServerOffFor);
    }

    private static void UnregisterAutoMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpLeaveOffName);
        SelfTest.UnregisterServerStep(OffForStep);
    }

    // ---------- helpers ----------

    private static string StateText()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view == null ? "not registered" : $"{view.Value.State}: {view.Value.Status}";
    }

    // Mod on and at work in a world, own settings in force (a test before may have left test rules or the switch off).
    private static bool ActiveInWorld()
    {
        if (Plugin.DebugOff)
        {
            Plugin.DebugSwitch(true);
        }
        ServerRules.TestRules = null;
        ClearOwn();
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return Player.m_localPlayer != null && view != null && view.Value.IsActive && TowerSync.Active;
    }

    // Mark in the item's custom data: a save keeps it. Crafter name alone is not enough: with crafter id 0 a loaded
    // character has the item with an empty crafter name (seen in the game), and the test said its items were gone.
    private const string TestMark = ModInfo.Guid + ".SelfTest";

    private static ItemDrop.ItemData Marked(ItemDrop.ItemData item)
    {
        if (item != null)
        {
            item.m_customData[TestMark] = "1";
        }
        return item;
    }

    private static List<ItemDrop.ItemData> TestItems(Player p) =>
        p.GetInventory().GetAllItems().Where(i => i != null && (i.m_crafterName == TestCrafter || i.m_customData.ContainsKey(TestMark))).ToList();

    private static ItemDrop.ItemData TestItem(Player p, string prefab) =>
        TestItems(p).FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);

    private static void RemoveTestItems(Player p)
    {
        if (p == null)
        {
            return;
        }
        foreach (var item in TestItems(p))
        {
            if (p.IsItemEquiped(item) || ReferenceEquals(p.m_hiddenLeftItem, item) || ReferenceEquals(p.m_hiddenRightItem, item))
            {
                p.UnequipItem(item, false);
            }
            p.GetInventory().RemoveItem(item);
        }
    }

    // '(MISSING)' Towers warnings since the mark that name a shield every game has: the false warning of 0.1.0.
    private static List<string> FalseMissing(int mark) =>
        Tap.WarningsSince(mark).Where(w => w.Contains("(MISSING)") && (w.Contains(IronTower) || w.Contains("ShieldWoodTower"))).ToList();

    private static List<string> AnyMissing(int mark) => Tap.WarningsSince(mark).Where(w => w.Contains("(MISSING)")).ToList();

    // ---------- tower.relog.menu ----------

    private static bool _menuArmed;
    private static bool _menuDefault;
    private static int _menuMark;
    private static int _menuBad;

    private static IEnumerator MenuBefore()
    {
        yield return null;
        _menuArmed = false;
        if (!ActiveInWorld())
        {
            SelfTest.Fail(RelogMenuName, $"the mod is not active in the world ({StateText()}): nothing was checked");
            yield break;
        }
        yield return Until(() => !TowerSync.HasWork, 3f);
        _menuDefault = TowerRules.Own().Key == TowerRules.Defaults().Key;
        _menuMark = Tap.WarningMark();
        _menuBad = Tap.Bad().Count;
        _menuArmed = true;
        SelfTest.Pass(RelogMenuName, $"before the log out: in the world, {TowerCatalog.Count} tower shield(s) known, own settings {(_menuDefault ? "default" : "not the default ones")}");
    }

    private static void CheckNoMissing(Checks c, string when)
    {
        var wrong = _menuDefault ? AnyMissing(_menuMark) : FalseMissing(_menuMark);
        c.Check(wrong.Count == 0, wrong.Count == 0
            ? $"no '(MISSING)' Towers warning {when}"
            : $"{wrong.Count} '(MISSING)' Towers warning(s) {when}: {wrong[0]}");
        c.Check(Tap.Bad().Count == _menuBad, $"no error from the mod {when}");
    }

    private static IEnumerator MenuAtMenu()
    {
        if (!_menuArmed)
        {
            yield break;
        }
        yield return new WaitForSecondsRealtime(2f); // the menu's own item database is set up by now
        var c = new Checks(RelogMenuName);
        var view = FeatureRegistry.Find(ModInfo.Guid);
        c.Check(FejdStartup.instance != null && Player.m_localPlayer == null, "at the main menu");
        CheckNoMissing(c, "while the world closed and at the main menu");
        c.Check(view != null && view.Value.IsActive, $"at the main menu the mod is active ({StateText()})");
        c.Report(" (main menu)");
    }

    private static IEnumerator MenuAfter()
    {
        if (!_menuArmed)
        {
            yield break;
        }
        _menuArmed = false;
        var c = new Checks(RelogMenuName);
        if (!ActiveInWorld())
        {
            c.Check(false, $"after loading the world again the mod is not active ({StateText()})");
            c.Report();
            yield break;
        }
        CheckNoMissing(c, "while the world loaded again");
        var rig = new Rig(RelogMenuName);
        try
        {
            var p = rig.P;
            yield return UseRules(TowerRules.Defaults());
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            if (tower == null || sword == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron");
                c.Report();
                yield break;
            }
            p.EquipItem(sword);
            p.EquipItem(tower);
            yield return Fixed;
            c.Check(TowerCatalog.Count == TowerRules.Defaults().Entries.Count && Only(p, tower, left: true) && !sword.m_equipped,
                $"in the loaded world every default tower shield is known ({TowerCatalog.Count}) and the Iron one takes both hands ({Hands(p)})");
            yield return FreshBash(p, tower);
            var s = new Swing();
            yield return Miss(p, RelogMenuName, s);
            c.Check(s.Started && s.Trigger == BashAttack.ShieldArmTrigger, $"the attack button bashes (started {s.Started}, '{s.Trigger}')");
            c.Report(" (world loaded again)");
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.relog.hands ----------

    private static bool _handsArmed;
    private static int _handsBad;

    private static IEnumerator HandsBefore()
    {
        yield return null;
        _handsArmed = false;
        if (!ActiveInWorld())
        {
            SelfTest.Fail(RelogHandsName, $"the mod is not active in the world ({StateText()}): nothing was checked");
            yield break;
        }
        var c = new Checks(RelogHandsName);
        var p = Player.m_localPlayer;
        var ok = false;
        try
        {
            RemoveTestItems(p);
            yield return WaitIdle(p);
            if (p.m_rightItem != null || p.m_leftItem != null)
            {
                SelfTest.Note(RelogHandsName, $"the player held something before the test ({Hands(p)}): it is put away and stays in the inventory");
            }
            var inv = p.GetInventory();
            var sword = Marked(inv.AddItem("SwordIron", 1, 1, 0, 0L, TestCrafter, false));
            var tower = Marked(inv.AddItem(IronTower, 1, 1, 0, 0L, TestCrafter, false));
            if (sword == null || tower == null)
            {
                c.Check(false, "could not add SwordIron and ShieldIronTower");
                c.Report();
                yield break;
            }
            _handsBad = Tap.Bad().Count;
            // Off the way the MC Mods tick does it (OnDeactivated, patches off), with no settings write.
            var off = Plugin.DebugSwitch(false);
            c.Check(off && tower.m_shared.m_itemType == ItemType.Shield, $"mod turned off: the tower shield is a normal shield ({tower.m_shared.m_itemType})");
            p.EquipItem(sword);
            p.EquipItem(tower);
            yield return Fixed;
            var both = ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower) && sword.m_equipped && tower.m_equipped;
            c.Check(both, $"with the mod off a sword and a tower shield are held together ({Hands(p)})");
            ok = off && both;
            c.Report(" (before the log out)");
        }
        finally
        {
            if (ok)
            {
                _handsArmed = true;
            }
            else
            {
                if (Plugin.DebugOff)
                {
                    Plugin.DebugSwitch(true);
                }
                RemoveTestItems(Player.m_localPlayer);
            }
        }
    }

    private static IEnumerator HandsAtMenu()
    {
        yield return null;
        if (!_handsArmed)
        {
            if (Plugin.DebugOff)
            {
                Plugin.DebugSwitch(true);
            }
            yield break;
        }
        // On again at the main menu, like ticking the mod there.
        var on = Plugin.DebugSwitch(true);
        yield return new WaitForSecondsRealtime(1f);
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (on && !Plugin.DebugOff && TowerSync.Active && view != null && view.Value.IsActive)
        {
            SelfTest.Pass(RelogHandsName, "at the main menu the mod is turned on again");
        }
        else
        {
            SelfTest.Fail(RelogHandsName, $"at the main menu the mod did not come on again ({StateText()})");
        }
    }

    private static IEnumerator HandsAfter()
    {
        yield return null;
        if (Plugin.DebugOff)
        {
            Plugin.DebugSwitch(true);
        }
        var p = Player.m_localPlayer;
        if (!_handsArmed)
        {
            RemoveTestItems(p);
            yield break;
        }
        _handsArmed = false;
        var c = new Checks(RelogHandsName);
        try
        {
            var sword = p != null ? TestItem(p, "SwordIron") : null;
            var tower = p != null ? TestItem(p, IronTower) : null;
            if (sword == null || tower == null)
            {
                // First runs: items gone after the load. Me say what the loaded character does carry.
                if (p != null)
                {
                    SelfTest.Note(RelogHandsName, "the loaded character carries: " + string.Join(", ", p.GetInventory().GetAllItems()
                        .Select(i => $"{(i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name)} (crafter '{i.m_crafterName}', id {i.m_crafterID}, equipped {i.m_equipped})").ToArray()));
                }
                c.Check(false, "the sword and the tower shield held before the log out are not in the loaded character's inventory");
                c.Report();
                yield break;
            }
            yield return Fixed;
            var towerHeld = tower.m_equipped && ReferenceEquals(p.m_leftItem, tower);
            var swordHeld = sword.m_equipped && ReferenceEquals(p.m_rightItem, sword);
            c.Check(TowerSync.Active && TowerData.IsAppliedTower(tower), $"the mod is on when the character loads: its tower shield is two-handed ({tower.m_shared.m_itemType})");
            c.Check(towerHeld != swordHeld, $"only one of the two is equipped after loading ({Hands(p)}; tower shield equipped {tower.m_equipped}, sword equipped {sword.m_equipped})");
            c.Check(towerHeld
                    ? !sword.m_equipped && p.m_rightItem == null
                    : !tower.m_equipped && !ReferenceEquals(p.m_leftItem, tower) && !ReferenceEquals(p.m_hiddenLeftItem, tower),
                "the other one is in the inventory, not in a hand");
            c.Check(Tap.Bad().Count == _handsBad, "no error from the mod since before the log out");
            SelfTest.Note(RelogHandsName, $"the game kept the {(towerHeld ? "tower shield" : swordHeld ? "sword" : "neither")} (the one it equips last, from the order it stored the items)");
            c.Report(" (world loaded again)");
        }
        finally
        {
            RemoveTestItems(p);
        }
    }

    // ---------- tower.disabled.vanilla ----------

    private static string R(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    // First game, mod on: a tower shield in hand (two-handed) and a sword in the inventory stay in the save; the normal
    // game's numbers of the shield (the mod's own snapshot) go to the second game.
    private static IEnumerator DisabledLeave()
    {
        yield return null;
        if (!ActiveInWorld())
        {
            SelfTest.Fail(DisabledName, $"the mod is not active in the first game ({StateText()}): nothing left for the second game");
            yield break;
        }
        yield return Until(() => !TowerSync.HasWork, 3f);
        var p = Player.m_localPlayer;
        yield return WaitIdle(p);
        RemoveTestItems(p);
        var inv = p.GetInventory();
        var sword = Marked(inv.AddItem("SwordIron", 1, 1, 0, 0L, TestCrafter, false));
        var tower = Marked(inv.AddItem(IronTower, 1, 1, 0, 0L, TestCrafter, false));
        var snap = SnapshotOf(IronTower);
        if (sword == null || tower == null || snap == null)
        {
            SelfTest.Fail(DisabledName, "first game: could not add SwordIron and ShieldIronTower (or the mod has no snapshot of the shield)");
            yield break;
        }
        p.EquipItem(tower);
        yield return Fixed;
        var applied = tower.m_shared.m_blockPower;
        var text = string.Join("|", new[]
        {
            R(snap.BlockPower), R(snap.BlockPowerPerLevel), ((int)snap.ItemType).ToString(CultureInfo.InvariantCulture), R(snap.MovementModifier), R(applied),
        });
        var carried = SelfTest.Carry(CarryKey, text);
        var held = Only(p, tower, left: true) && TowerData.IsAppliedTower(tower);
        if (carried && held)
        {
            SelfTest.Pass(DisabledName, $"first game (mod on): ShieldIronTower held two-handed with block armor {F(applied)} (normal game: {F(snap.BlockPower)}), a SwordIron in the inventory; both left for the second game");
        }
        else
        {
            SelfTest.Fail(DisabledName, $"first game: the tower shield is not held two-handed ({Hands(p)}, type {tower.m_shared.m_itemType}) or nothing could be carried to the second game ({carried})");
        }
    }

    // Second game: every MC mod Enabled = false from the start (no patch of this mod applied, OnActivated never ran).
    private static IEnumerator DisabledCheck()
    {
        yield return null;
        var c = new Checks(DisabledName);
        var p = Player.m_localPlayer;
        try
        {
            var parts = (SelfTest.Carried(CarryKey) ?? "").Split('|');
            if (p == null || parts.Length != 5)
            {
                c.Check(false, "nothing was carried over from the first game (its part did not run or failed), or there is no player");
                c.Report();
                yield break;
            }
            var blockPower = ParseFloat(parts[0]);
            var perLevel = ParseFloat(parts[1]);
            var type = (ItemType)ParseInt(parts[2]);
            var movement = ParseFloat(parts[3]);
            var applied = ParseFloat(parts[4]);
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && view != null && view.Value.IsDisabledByUser && !TowerSync.Active && TowerCatalog.Count == 0,
                $"the game started with Enabled = false: '{StateText()}', no tower rules applied");
            var prefab = SharedOf(IronTower);
            c.Check(prefab != null && prefab.m_itemType == ItemType.Shield && prefab.m_itemType == type && Near(prefab.m_blockPower, blockPower)
                    && Near(prefab.m_blockPowerPerLevel, perLevel) && Near(prefab.m_movementModifier, movement, 0.0001f),
                $"the ShieldIronTower prefab is the normal game's (type {(prefab != null ? prefab.m_itemType.ToString() : "missing")}, block armor {(prefab != null ? F(prefab.m_blockPower) : "?")}; "
                + $"the normal game has {type}, {F(blockPower)})");
            var tower = TestItem(p, IronTower);
            var sword = TestItem(p, "SwordIron");
            if (tower == null || sword == null)
            {
                c.Check(false, "the tower shield and the sword left in the first game are not in the loaded character's inventory");
                c.Report();
                yield break;
            }
            yield return Fixed;
            var own = blockPower + perLevel * (tower.m_quality - 1);
            c.Check(tower.m_shared.m_itemType == ItemType.Shield && !tower.IsTwoHanded() && Near(tower.GetBaseBlockPower(), own),
                $"the shield held two-handed in the first game is a normal shield with normal block armor now (type {tower.m_shared.m_itemType}, block armor {F(tower.GetBaseBlockPower())}; with the mod on it was {F(applied)})");
            var tip = tower.GetTooltip();
            c.Check(tip.Contains("\n$item_onehanded") && !tip.Contains("$item_twohanded") && !tip.Contains("Cannot parry") && !tip.Contains("Bash stagger"),
                $"its tooltip is the normal one ('{OneLine(tip)}')");
            yield return WaitIdle(p);
            if (p.m_rightItem != null && !ReferenceEquals(p.m_rightItem, sword))
            {
                p.UnequipItem(p.m_rightItem, false);
            }
            var drawn = p.EquipItem(sword);
            if (!ReferenceEquals(p.m_leftItem, tower))
            {
                p.EquipItem(tower);
            }
            yield return Fixed;
            c.Check(drawn && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower), $"it is one-handed: a sword is held next to it ({Hands(p)})");
            c.Check(!p.GetSEMan().HaveStatusEffect(Brace.Hash), "no Braced effect");
            p.UnequipItem(sword, false);
            yield return Fixed;
            var punched = p.StartAttack(null, false);
            var punch = punched ? p.m_currentAttack : null;
            c.Check(punched && punch != null && p.m_unarmedWeapon != null && ReferenceEquals(punch.GetWeapon(), p.m_unarmedWeapon.m_itemData),
                "attack with only the tower shield punches: no bash");
            var until = Time.time + 3f;
            while (Time.time < until && p.InAttack())
            {
                yield return null;
            }
            c.Report();
        }
        finally
        {
            RemoveTestItems(p);
        }
    }

    // ---------- tower.mp.leave, tower.mp.leave-off ----------

    // Log out like the menu's button (character saved), wait for the main menu and three seconds there.
    private static IEnumerator LeaveToMenu(Box<bool> atMenu)
    {
        atMenu.Value = false;
        var game = Game.instance;
        if (game == null || game.m_shuttingDown)
        {
            yield break;
        }
        game.Logout();
        var end = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < end && !(FejdStartup.instance != null && Player.m_localPlayer == null))
        {
            yield return null;
        }
        if (FejdStartup.instance == null)
        {
            yield break;
        }
        yield return new WaitForSecondsRealtime(3f);
        atMenu.Value = FejdStartup.instance != null;
    }

    private static void CheckAfterLeave(Checks c, int mark, int bad, string server)
    {
        var missing = AnyMissing(mark);
        c.Check(missing.Count == 0, missing.Count == 0
            ? $"no '(MISSING)' Towers warning while the world of {server} closed"
            : $"{missing.Count} '(MISSING)' Towers warning(s) while the world of {server} closed: {missing[0]}");
        c.Check(Tap.Bad().Count == bad, "no error from the mod meanwhile");
        var view = FeatureRegistry.Find(ModInfo.Guid);
        c.Check(view != null && view.Value.IsActive, $"back at the main menu the mod is active again ({StateText()})");
    }

    // Scenario vanilla-server: the mod is inactive in the world (registered from BindConfig).
    private static IEnumerator RunMpLeave()
    {
        var c = new Checks(MpLeaveName);
        yield return null;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var inactive = view != null && view.Value.State == nameof(ModState.ServerMissing)
                       && view.Value.Status.StartsWith("Inactive: the server does not have this mod", StringComparison.Ordinal);
        c.Check(inactive && Player.m_localPlayer != null, $"joined to a server without the mod: '{StateText()}'");
        if (!inactive || Player.m_localPlayer == null)
        {
            c.Report();
            yield break;
        }
        var mark = Tap.WarningMark();
        var bad = Tap.Bad().Count;
        var menu = new Box<bool>();
        yield return LeaveToMenu(menu);
        c.Check(menu.Value, "logged out to the main menu");
        CheckAfterLeave(c, mark, bad, "a server without the mod");
        c.Report();
    }

    // Scenario modded. The test ends at the main menu; the probe joins again before the next test.
    private static IEnumerator RunMpLeaveOff()
    {
        var c = new Checks(MpLeaveOffName);
        var a = new Answer();
        yield return null;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (view == null || !view.Value.IsActive || !Connected)
        {
            c.Check(false, $"the mod is not active on a server at the start ({StateText()})");
            c.Report();
            yield break;
        }
        yield return Ask(R(OffSeconds), a, OffForStep);
        var asked = Time.realtimeSinceStartup;
        if (!a.Ok)
        {
            c.Check(false, $"the server did not turn its mod off for a while: {a.Text}");
            c.Report();
            yield break;
        }
        yield return Until(() => view.Value.State == nameof(ModState.ServerMissing), 10f);
        var off = view.Value.State == nameof(ModState.ServerMissing) && view.Value.Status.Contains("turned off");
        c.Check(off, $"the server turned the mod off: this game's copy says '{StateText()}'");
        if (off && Player.m_localPlayer != null)
        {
            var mark = Tap.WarningMark();
            var bad = Tap.Bad().Count;
            var menu = new Box<bool>();
            yield return LeaveToMenu(menu);
            c.Check(menu.Value, "logged out to the main menu");
            CheckAfterLeave(c, mark, bad, "a server that has the mod turned off");
        }
        // The server turns its mod on again by itself: not before that may the next test join.
        while (Time.realtimeSinceStartup - asked < OffSeconds + 4f)
        {
            yield return null;
        }
        c.Report();
    }

    // Server half: the mod off on the server now, on again by itself after <arg> seconds (the client is at the main
    // menu then and cannot ask). The wait runs on the plugin object: it lives on while the mod is off.
    private static IEnumerator ServerOffFor(string arg, object[] reply)
    {
        yield return null;
        var seconds = ParseFloat(arg);
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var enabled = view != null ? view.Value.Enabled : null;
        if (enabled == null || !enabled.Value || float.IsNaN(seconds) || seconds < 5f || seconds > 60f)
        {
            SelfTest.Answer(reply, false, $"the mod is not registered or already off on the server, or '{arg}' is not 5 to 60 seconds");
            yield break;
        }
        if (!Plugin.DebugRun(BackOnLater(enabled, seconds)))
        {
            SelfTest.Answer(reply, false, "no plugin object to run the wait on");
            yield break;
        }
        enabled.Value = false;
        yield return null;
        SelfTest.Answer(reply, view.Value.State == nameof(ModState.Disabled), $"off on the server for {F(seconds)} s: {view.Value.Status}");
    }

    private static IEnumerator BackOnLater(ConfigEntry<bool> enabled, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (!enabled.Value)
        {
            enabled.Value = true;
        }
        SelfTest.Note(MpLeaveOffName, "the server turned its mod on again by itself");
    }
}

internal sealed partial class Plugin
{
    // Self test: run a routine on the plugin object (it lives on while the mod is off). False = no plugin object.
    internal static bool DebugRun(IEnumerator routine)
    {
        if (_self == null || routine == null)
        {
            return false;
        }
        _self.StartCoroutine(routine);
        return true;
    }
}
#endif
