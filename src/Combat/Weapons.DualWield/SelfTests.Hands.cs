#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. More in-world self tests of the hands (same helpers as SelfTests.cs):
//   dual.pair     pairing the way a player asks for it: hotbar key / radial menu (UseItem(null, item, false)) and
//                 inventory right-click (UseItem(inventory, item, true)), equipped marks of the hotbar, MaceIron +
//                 KnifeFlint, two SwordIron; main-hand key with a right-click, on a lone sword, and "key + sword, then
//                 axe without the key"; BombSmoke, ExcludedWeapons from one weapon in hand, the unknown-name warning
//                 once; unequip by the hotbar key; first pairing message (text, once, never for a loaded pair); pair
//                 through the inventory save format (marker and equipped flags)
//   dual.swap     swap key through the per-frame key read (test keyboard: Controls.TestKeyDown): other key bound,
//                 no key bound, gamepad button warning, main-hand key unbound; top-left message with icon, equip
//                 effect, action text, the off-hand weapon that blocks after; jump and sprint cancel (sprint message);
//                 key held = one swap per equip time + queue pause; key during a swing; key together with attack
//   dual.hammer   radial menu hammer on and off exactly as Valheim.UI.HammerItemElement does it (queued unequip of
//                 the hammer, then both equips in the same frame)
//   dual.toggle   feature off and on in memory (Plugin.TestSwitchOff / TestSwitchOn: OnDeactivated + patches gone,
//                 patches back + OnActivated; the Enabled setting untouched): off-hand weapon back in the inventory,
//                 vanilla stance, equip and attack, a marked weapon is a normal weapon, a saved pair loads as one
//                 weapon; on again: stale marker cleared, Info line again, pairs work; off while sheathed
internal static partial class SelfTests
{
    private const string PairName = "dual.pair";
    private const string SwapName = "dual.swap";
    private const string HammerTestName = "dual.hammer";
    private const string ToggleName = "dual.toggle";

    // Info line of the default moves (TESTING.md T25).
    private const string DefaultMovesInfo =
        "Pairs use the moves of AxeBerzerkr (dualaxes0-3, special dualaxes_secondary); two knives use "
        + "KnifeSkollAndHati (dual_knives0-2, special dual_knives_secondary).";

    // Every test of the other parts, in run order. Tests that swing (dual.kills, dual.toggle) before the ones that
    // sit, lie down, eat or equip other mods' things (dual.others, dual.places, dual.sheath): in the first full run
    // the player could not start an attack any more after those (dual.others and dual.toggle failed on it), and the
    // tests before them swung fine. dual.log last: it read what the others logged.
    private static void RegisterMore()
    {
        SelfTest.Register(PairName, RunPair);
        SelfTest.Register(SwapName, RunSwap);
        SelfTest.Register(HammerTestName, RunHammer);
        SelfTest.Register(MovesName, RunMoves);
        SelfTest.Register(ElementsName, RunElements);
        SelfTest.Register(StaminaName, RunStamina);
        SelfTest.Register(TreesName, RunTrees);
        SelfTest.Register(ParryName, RunParry);
        SelfTest.Register(WoundedName, RunWounded);
        SelfTest.Register(KillsName, RunKills);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(OthersName, RunOthers);
        SelfTest.Register(PlacesName, RunPlaces);
        SelfTest.Register(SheathName, RunSheath);
        SelfTest.Register(LogName, RunLog);
        RegisterMultiplayerTests();
        LogWatch.Install();
    }

    private static void UnregisterMore()
    {
        SelfTest.Unregister(PairName);
        SelfTest.Unregister(SwapName);
        SelfTest.Unregister(HammerTestName);
        SelfTest.Unregister(MovesName);
        SelfTest.Unregister(ElementsName);
        SelfTest.Unregister(StaminaName);
        SelfTest.Unregister(TreesName);
        SelfTest.Unregister(ParryName);
        SelfTest.Unregister(WoundedName);
        SelfTest.Unregister(PlacesName);
        SelfTest.Unregister(SheathName);
        SelfTest.Unregister(OthersName);
        SelfTest.Unregister(KillsName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(LogName);
        UnregisterMultiplayerTests();
    }

    // ---------- helpers ----------

    // Hotbar key and radial menu: both call UseItem(null, item, fromInventoryGui: false) (Player.UseHotbarItem,
    // Valheim.UI.ItemElement). Something usable under the crosshair would get the item first (vanilla "use item on"):
    // then me make the inventory right-click's call, which skip that step, and say so.
    private static void UseHotbar(Player p, ItemDrop.ItemData item, string test)
    {
        var hover = p.GetHoverObject();
        var onSomething = hover != null && hover.GetComponentInParent<Interactable>() != null;
        if (onSomething)
        {
            SelfTest.Note(test, $"the crosshair is on {hover.name}: hotbar use of {Name(item)} made like an inventory right-click");
        }
        p.UseItem(null, item, onSomething);
    }

    // Me wait: queued unequip (hotbar path) done, lone off-hand weapon settled.
    private static IEnumerator WaitUnequipped(Player p, ItemDrop.ItemData item)
    {
        var until = Time.time + 3f;
        while (Time.time < until && (p.IsItemEquiped(item) || p.IsEquipActionQueued(item)))
        {
            yield return null;
        }
        yield return WaitIdle(p);
        yield return null;
    }

    // Me wait: queued swap ended (done or dropped), queue pause over.
    private static IEnumerator WaitSwap(Player p)
    {
        var until = Time.time + 3f;
        while (Time.time < until && Hands.PendingSwap != null)
        {
            yield return null;
        }
        yield return WaitIdle(p);
        yield return null;
    }

    // What Player.Load does with a saved pair: both weapons flagged equipped in the inventory (the off-hand one carry
    // the marker), then every flagged item equipped in inventory list order (Player.EquipInventoryItems).
    private static void LoadPair(Player p, Inventory inventory, ItemDrop.ItemData main, ItemDrop.ItemData off, bool offFirst)
    {
        var others = inventory.GetAllItems()
            .Where(i => p.IsItemEquiped(i) && !ReferenceEquals(i, main) && !ReferenceEquals(i, off)).ToList();
        p.UnequipItem(main, false);
        p.UnequipItem(off, false);
        Hands.SetMarked(main, false);
        Hands.SetMarked(off, true);
        main.m_equipped = true;
        off.m_equipped = true;
        var first = offFirst ? off : main;
        var second = offFirst ? main : off;
        inventory.m_inventory.Remove(second);
        inventory.m_inventory.Insert(0, second);
        inventory.m_inventory.Remove(first);
        inventory.m_inventory.Insert(0, first);
        p.EquipInventoryItems();
        // Vanilla clear the flag of items it could not equip (already equipped): put theirs back.
        foreach (var other in others)
        {
            other.m_equipped = p.IsItemEquiped(other);
        }
    }

    // Item on a free slot of the top inventory row (the hotbar). -1 = no free slot.
    private static int PutOnHotbar(Inventory inventory, ItemDrop.ItemData item)
    {
        if (item.m_gridPos.y == 0)
        {
            return item.m_gridPos.x;
        }
        for (var x = 0; x < inventory.GetWidth(); x++)
        {
            if (inventory.GetItemAt(x, 0) == null)
            {
                item.m_gridPos = new Vector2i(x, 0);
                inventory.Changed();
                return x;
            }
        }
        return -1;
    }

    // "Equipped" mark of hotbar slot x as the HUD draws it this frame. Null = no hotbar drawn, or no such slot.
    private static bool? HotbarMark(int x)
    {
        if (x < 0)
        {
            return null;
        }
        foreach (var bar in UnityEngine.Object.FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None))
        {
            if (bar == null || !bar.isActiveAndEnabled || x >= bar.m_elements.Count)
            {
                continue;
            }
            var element = bar.m_elements[x];
            if (element != null && element.m_equiped != null)
            {
                return element.m_equiped.activeSelf;
            }
        }
        return null;
    }

    // Top-left messages that came after me was made (MessageHud queue them, then show one a second).
    private sealed class TopLeftWatch
    {
        private readonly HashSet<MessageHud.MsgData> _old = new HashSet<MessageHud.MsgData>();

        internal TopLeftWatch()
        {
            foreach (var message in All())
            {
                _old.Add(message);
            }
        }

        private static IEnumerable<MessageHud.MsgData> All()
        {
            var hud = MessageHud.instance;
            if (hud == null)
            {
                yield break;
            }
            if (hud.currentMsg != null)
            {
                yield return hud.currentMsg;
            }
            foreach (var message in hud.m_msgQeue)
            {
                yield return message;
            }
        }

        // icon null = any icon.
        internal bool Saw(string text, Sprite icon)
        {
            foreach (var message in All())
            {
                if (!_old.Contains(message) && message.m_text == text && (icon == null || message.m_icon == icon))
                {
                    return true;
                }
            }
            return false;
        }

        internal string NewTexts()
        {
            var texts = All().Where(m => !_old.Contains(m)).Select(m => $"'{m.m_text}'{(m.m_icon != null ? " (icon " + m.m_icon.name + ")" : "")}").ToArray();
            return texts.Length == 0 ? "none" : string.Join(", ", texts);
        }
    }

    // ---------- dual.pair ----------

    private static IEnumerator RunPair()
    {
        var c = new Checks(PairName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(PairName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var firstShown = Hands.TestFirstPairShown;
        var hud = MessageHud.instance;
        var center = hud != null ? hud.m_messageCenterText : null;
        var centerBefore = center != null ? center.text : null;
        try
        {
            bench = new Bench(player);
            if (bench.FreeSlots < 7)
            {
                c.Check(false, $"needs 7 free inventory slots, has {bench.FreeSlots}");
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var sword2 = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var mace = bench.Give(Mace);
            var knifeFlint = bench.Give(KnifeFlintName);
            var knifeBlack = bench.Give(KnifeBlack);
            var bomb = bench.Give("BombSmoke");
            if (sword == null || sword2 == null || axe == null || mace == null || knifeFlint == null || knifeBlack == null
                || bomb == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var inventory = bench.Inventory;
            // Key names of the message: the default keys, whatever the player bound.
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.H;
            Controls.CacheKeys();
            yield return null;

            // T29: a saved pair that loads shows no message; the first pairing the player asks for does, once.
            const string Marker = "dual.pair marker";
            c.Check(center != null && !Hud.IsUserHidden(), "the HUD shows messages (message text found, HUD not hidden)");
            if (center != null)
            {
                Hands.TestFirstPairShown = false;
                center.text = Marker;
                LoadPair(player, inventory, sword, axe, offFirst: false);
                yield return null;
                c.Check(Holds(player, sword, axe) && center.text == Marker && !Hands.TestFirstPairShown,
                    $"a saved pair that loads shows no pairing message (centre text '{center.text}'; {HandsText(player)})");
            }
            bench.Empty();
            Hands.SetMarked(axe, false);
            yield return WaitIdle(player);

            // T01: hotbar key (and radial menu): sword, then axe. Queued equips, as for a player.
            UseHotbar(player, sword, PairName);
            yield return WaitEquipped(player, sword);
            UseHotbar(player, axe, PairName);
            yield return WaitEquipped(player, axe);
            c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword) && StateI(player) == 15,
                $"hotbar keys, sword then axe: sword main, axe off, dual axe stance (statei {StateI(player)}; {HandsText(player)})");
            Invariant(c, player, "hotbar pairing");
            if (center != null)
            {
                var expected = "Dual wielding: " + Localization.instance.Localize(axe.m_shared.m_name)
                               + " is in your off hand. Hold Left Alt while equipping to replace your main weapon instead. "
                               + "Press H to swap hands.";
                c.Check(center.text == expected && Hands.TestFirstPairShown,
                    $"first pairing asked by the player: centre message '{expected}' (got '{center.text}')");
            }
            // Equipped marks: the inventory draws item.m_equipped, the hotbar its own "equiped" object per slot.
            var swordSlot = PutOnHotbar(inventory, sword);
            var axeSlot = PutOnHotbar(inventory, axe);
            yield return null;
            yield return null;
            var swordMark = HotbarMark(swordSlot);
            var axeMark = HotbarMark(axeSlot);
            c.Check(sword.m_equipped && axe.m_equipped && swordMark == true && axeMark == true,
                $"both weapons marked as equipped in the inventory ({sword.m_equipped}, {axe.m_equipped}) and on the hotbar "
                + $"(slot {swordSlot + 1}: {MarkText(swordMark)}, slot {axeSlot + 1}: {MarkText(axeMark)})");

            // T29: the next pairing shows nothing. T01: two SwordIron.
            if (center != null)
            {
                center.text = Marker;
            }
            UseHotbar(player, sword2, PairName);
            yield return WaitEquipped(player, sword2);
            c.Check(Holds(player, sword, sword2) && !axe.m_equipped && sword.m_equipped && sword2.m_equipped
                    && StateI(player) == 15,
                $"hotbar key of the second SwordIron: it replaces the off-hand axe, two swords in the dual axe stance ({HandsText(player)})");
            c.Check(center == null || center.text == Marker,
                $"the next pairing of the session shows no message (centre text '{(center != null ? center.text : "")}')");
            Invariant(c, player, "two swords");

            // T29 with no key bound: the message names no key. T01: MaceIron + KnifeFlint (not two knives: axe moves).
            bench.Empty();
            Hands.TestFirstPairShown = false;
            Controls.TestMainKey = KeyCode.None;
            Controls.TestSwapKey = KeyCode.None;
            Controls.CacheKeys();
            if (center != null)
            {
                center.text = Marker;
            }
            UseHotbar(player, mace, PairName);
            yield return WaitEquipped(player, mace);
            UseHotbar(player, knifeFlint, PairName);
            yield return WaitEquipped(player, knifeFlint);
            c.Check(Holds(player, mace, knifeFlint) && mace.m_equipped && knifeFlint.m_equipped && StateI(player) == 15,
                $"MaceIron then KnifeFlint: mace main, knife off, dual axe stance (statei {StateI(player)}; {HandsText(player)})");
            if (center != null)
            {
                var bare = "Dual wielding: " + Localization.instance.Localize(knifeFlint.m_shared.m_name) + " is in your off hand.";
                c.Check(center.text == bare, $"first pairing with no key bound: the message names no key ('{bare}', got '{center.text}')");
            }
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.H;
            Controls.CacheKeys();

            // T01: inventory right-clicks (InventoryGui.OnRightClickItem: UseItem(inventory, item, true)).
            bench.Empty();
            Hands.SetMarked(knifeFlint, false);
            yield return WaitIdle(player);
            player.UseItem(inventory, sword, true);
            yield return WaitEquipped(player, sword);
            player.UseItem(inventory, axe, true);
            yield return WaitEquipped(player, axe);
            c.Check(Holds(player, sword, axe) && sword.m_equipped && axe.m_equipped && Hands.IsMarked(axe)
                    && StateI(player) == 15,
                $"inventory right-clicks, sword then axe: sword main, axe off, dual axe stance ({HandsText(player)})");
            Invariant(c, player, "right-click pairing");

            // T22: unequip the main weapon with its hotbar key (queued unequip): the axe becomes the main weapon.
            UseHotbar(player, sword, PairName);
            yield return WaitUnequipped(player, sword);
            c.Check(Holds(player, axe, null) && !Hands.IsMarked(axe) && !sword.m_equipped,
                $"hotbar key of the main weapon: it is put away, the axe moves to the main hand, marker cleared ({HandsText(player)})");
            Invariant(c, player, "unequip main by hotbar");
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            UseHotbar(player, axe, PairName);
            yield return WaitUnequipped(player, axe);
            c.Check(Holds(player, sword, null) && !axe.m_equipped,
                $"hotbar key of the off-hand weapon: it is put away, the sword stays ({HandsText(player)})");
            Invariant(c, player, "unequip off hand by hotbar");

            // T03: main-hand key held with an inventory right-click.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            Controls.TestMainHandHeld = true;
            player.UseItem(inventory, knifeBlack, true);
            Controls.TestMainHandHeld = false;
            yield return WaitEquipped(player, knifeBlack);
            c.Check(Holds(player, knifeBlack, null) && !sword.m_equipped && !axe.m_equipped,
                $"main-hand key + inventory right-click: the knife alone in the main hand, sword and axe put away ({HandsText(player)})");
            Invariant(c, player, "main-hand key, right-click");
            // T03: sword alone, key + the second SwordIron: it replaces the sword.
            bench.Empty();
            player.EquipItem(sword);
            yield return WaitIdle(player);
            Controls.TestMainHandHeld = true;
            UseHotbar(player, sword2, PairName);
            Controls.TestMainHandHeld = false;
            yield return WaitEquipped(player, sword2);
            c.Check(Holds(player, sword2, null) && !sword.m_equipped,
                $"main-hand key + the second SwordIron next to a lone sword: it replaces the sword ({HandsText(player)})");
            Invariant(c, player, "main-hand key, lone sword");
            // T03: mace in hand, key + sword, then at once the axe without the key: two queued equips, the key only
            // for the sword. Sword main, axe off.
            bench.Empty();
            Hands.SetMarked(sword2, false);
            player.EquipItem(mace);
            yield return WaitIdle(player);
            Controls.TestMainHandHeld = true;
            UseHotbar(player, sword, PairName);
            Controls.TestMainHandHeld = false;
            UseHotbar(player, axe, PairName);
            var bothQueued = player.IsEquipActionQueued(sword) && player.IsEquipActionQueued(axe);
            var until = Time.time + 5f;
            while (Time.time < until && (player.IsEquipActionQueued(sword) || player.IsEquipActionQueued(axe)))
            {
                yield return null;
            }
            yield return WaitIdle(player);
            yield return null;
            SelfTest.Note(PairName, $"main-hand key + sword, then axe: both equips waited in the game's queue = {bothQueued}");
            c.Check(Holds(player, sword, axe) && !mace.m_equipped && Hands.IsMarked(axe),
                $"main-hand key + sword, then the axe without the key: sword main, axe off, mace put away ({HandsText(player)})");
            Invariant(c, player, "key for the first of two equips");

            // T07: a bomb never pairs: both weapons put away.
            bench.Pair(sword, axe);
            yield return null;
            player.EquipItem(bomb);
            yield return null;
            c.Check(Holds(player, bomb, null) && !sword.m_equipped && !axe.m_equipped,
                $"BombSmoke while paired: the bomb alone, both weapons put away ({HandsText(player)})");
            Invariant(c, player, "bomb");
            // T07: ExcludedWeapons = AxeIron, sword in hand, then the axe: it replaces the sword.
            ServerRules.TestRules = Rules(excluded: Axe);
            bench.Empty();
            player.EquipItem(sword);
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, axe, null) && !sword.m_equipped && !Hands.IsMarked(axe),
                $"ExcludedWeapons = AxeIron: the axe replaces the sword, no pair ({HandsText(player)})");
            Invariant(c, player, "excluded weapon");
            // T07: unknown name: one warning, also when the rules come again (dual.data used the name up: forget it).
            const string Unknown = "NoSuchSword";
            Eligibility.TestForgetWarned(Unknown);
            var warnings = Eligibility.WarningCount;
            ServerRules.TestRules = Rules(excluded: Unknown);
            Eligibility.Refresh(ServerRules.Current);
            var afterFirst = Eligibility.WarningCount;
            var warning = Eligibility.LastWarning;
            ServerRules.TestRules = Rules(excluded: Unknown);
            Eligibility.Refresh(ServerRules.Current);
            yield return null;
            yield return null;
            c.Check(afterFirst == warnings + 1 && Eligibility.WarningCount == warnings + 1
                    && warning == "ExcludedWeapons: no item named 'NoSuchSword' in this game; it is ignored. Use prefab "
                    + "names as the spawn command does (for example AxeBronze).",
                $"ExcludedWeapons = NoSuchSword: one warning, not again when the rules come again ({Eligibility.WarningCount - warnings} "
                + $"logged, text '{warning}')");
            ServerRules.TestRules = DualRules.Defaults;
            yield return null;

            // T30: the MAIN weapon becomes excluded while paired: the off-hand axe is put away, the sword stays alone
            // in its own stance. Cleared: pairing works again.
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"pair before the rules change ({HandsText(player)})");
            ServerRules.TestRules = Rules(excluded: Sword);
            yield return null;
            yield return null;
            c.Check(Holds(player, sword, null) && !axe.m_equipped && !Hands.IsMarked(axe)
                    && StateI(player) == (int)sword.m_shared.m_animationState,
                $"ExcludedWeapons = SwordIron while paired: the off-hand axe is put away, the sword stays alone (statei "
                + $"{StateI(player)}; {HandsText(player)})");
            Invariant(c, player, "main weapon excluded");
            ServerRules.TestRules = DualRules.Defaults;
            yield return null;
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"exclusion cleared: pairing works again ({HandsText(player)})");

            // T19: the pair in the inventory save format (what the character file holds): both flagged equipped, the
            // marker on the off-hand weapon only. Then loaded in that state, off-hand weapon first.
            bench.Pair(sword, axe);
            yield return null;
            var pkg = new ZPackage();
            inventory.Save(pkg);
            pkg.SetPos(0);
            var copy = new Inventory("dual.pair save copy", null, inventory.GetWidth(), inventory.GetHeight());
            copy.Load(pkg);
            var swordCopy = copy.GetItemAt(sword.m_gridPos.x, sword.m_gridPos.y);
            var axeCopy = copy.GetItemAt(axe.m_gridPos.x, axe.m_gridPos.y);
            c.Check(swordCopy != null && axeCopy != null && !ReferenceEquals(axeCopy, axe) && swordCopy.m_equipped
                    && axeCopy.m_equipped && Hands.IsMarked(axeCopy) && !Hands.IsMarked(swordCopy),
                "saved and loaded inventory: both weapons flagged equipped, the off-hand marker on the axe only "
                + $"(sword {(swordCopy != null ? swordCopy.m_equipped.ToString() : "missing")}, axe "
                + $"{(axeCopy != null ? axeCopy.m_equipped + ", marked " + Hands.IsMarked(axeCopy) : "missing")})");
            LoadPair(player, inventory, sword, axe, offFirst: true);
            yield return null;
            c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                $"the saved pair loaded with the off-hand weapon first: same hands ({HandsText(player)})");
            Invariant(c, player, "loaded pair");
            c.Report();
        }
        finally
        {
            Hands.TestFirstPairShown = firstShown;
            if (center != null && centerBefore != null)
            {
                center.text = centerBefore;
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    private static string MarkText(bool? mark) => mark.HasValue ? (mark.Value ? "marked" : "not marked") : "no such hotbar slot drawn";

    // ---------- dual.swap ----------

    private static IEnumerator RunSwap()
    {
        var c = new Checks(SwapName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SwapName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var controller = player.GetComponent<PlayerController>();
        var controllerOn = controller != null && controller.enabled;
        var walkBefore = player.GetWalk();
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Blocking, Skills.SkillType.Jump, Skills.SkillType.Run,
                Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Clubs);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var mace = bench.Give(Mace);
            if (sword == null || axe == null || mace == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // Key reads go through the test keyboard (TestKeyHeld / TestKeyDown), keys bound in memory.
            Controls.TestForgetWarned();
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.J;
            Controls.CacheKeys();

            // T31: SwapHandsKey = J: J swaps, H does nothing.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            c.Check(Controls.SwapKey == KeyCode.J && Controls.MainKey == KeyCode.LeftAlt,
                $"keys bound: main hand {Controls.MainKey}, swap {Controls.SwapKey}");
            Controls.TestKeyDown = KeyCode.H;
            yield return null;
            yield return null;
            c.Check(Hands.PendingSwap == null && player.GetActionQueueCount() == 0 && Holds(player, sword, axe),
                $"SwapHandsKey = J: H does nothing ({HandsText(player)})");
            Controls.TestKeyDown = KeyCode.J;
            yield return null;
            var jQueued = ReferenceEquals(Hands.PendingSwap, axe) || Holds(player, axe, sword);
            yield return WaitSwap(player);
            c.Check(jQueued && Holds(player, axe, sword), $"SwapHandsKey = J: J swaps the hands ({HandsText(player)})");
            // T31: no key bound: nothing swaps.
            Controls.TestSwapKey = KeyCode.None;
            Controls.CacheKeys();
            Controls.TestKeyDown = KeyCode.J;
            yield return null;
            yield return null;
            var noneJ = Hands.PendingSwap == null && player.GetActionQueueCount() == 0;
            Controls.TestKeyDown = KeyCode.H;
            yield return null;
            yield return null;
            c.Check(Controls.SwapKey == KeyCode.None && noneJ && Hands.PendingSwap == null
                    && player.GetActionQueueCount() == 0 && Holds(player, axe, sword),
                $"SwapHandsKey = None: no key swaps ({HandsText(player)})");
            // T31: gamepad button: one warning, the key is off.
            var keyWarnings = Controls.WarningCount;
            Controls.TestSwapKey = KeyCode.JoystickButton0;
            Controls.CacheKeys();
            var keyWarning = Controls.LastWarning;
            Controls.CacheKeys();
            Controls.TestKeyDown = KeyCode.JoystickButton0;
            yield return null;
            yield return null;
            c.Check(Controls.SwapKey == KeyCode.None && Controls.WarningCount == keyWarnings + 1
                    && keyWarning == "Controls.SwapHandsKey = JoystickButton0: gamepad buttons are not supported yet, so "
                    + "swapping your weapons between your hands is off. Pick a keyboard key or a mouse button.",
                $"SwapHandsKey = JoystickButton0: one warning ({Controls.WarningCount - keyWarnings} logged, '{keyWarning}')");
            c.Check(Hands.PendingSwap == null && player.GetActionQueueCount() == 0 && Holds(player, axe, sword),
                $"SwapHandsKey = JoystickButton0: no key swaps ({HandsText(player)})");
            // T31: MainHandKey = None: Left Alt + equip pairs like a normal equip. Control: bound, it replaces.
            // Here the main-hand key is read from the test keyboard (TestKeyHeld), not forced released.
            Controls.TestMainHandHeld = null;
            Controls.TestSwapKey = KeyCode.H;
            Controls.TestMainKey = KeyCode.None;
            Controls.CacheKeys();
            bench.Empty();
            Hands.SetMarked(sword, false);
            player.EquipItem(sword);
            yield return WaitIdle(player);
            Controls.TestKeyHeld = KeyCode.LeftAlt;
            UseHotbar(player, mace, SwapName);
            Controls.TestKeyHeld = null;
            yield return WaitEquipped(player, mace);
            c.Check(Controls.MainKey == KeyCode.None && Holds(player, sword, mace),
                $"MainHandKey = None: Left Alt + equip pairs like a normal equip ({HandsText(player)})");
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.CacheKeys();
            bench.Empty();
            player.EquipItem(sword);
            yield return WaitIdle(player);
            Controls.TestKeyHeld = KeyCode.LeftAlt;
            UseHotbar(player, mace, SwapName);
            Controls.TestKeyHeld = null;
            yield return WaitEquipped(player, mace);
            c.Check(Holds(player, mace, null) && !sword.m_equipped,
                $"control, MainHandKey = LeftAlt held: the mace alone in the main hand ({HandsText(player)})");
            Controls.TestMainHandHeld = false;
            Invariant(c, player, "keys");

            // T32 reference: a vanilla hotbar equip of a weapon, sampled like the swap below.
            var bar = Hud.instance != null ? Hud.instance.m_actionBarRoot : null;
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            UseHotbar(player, mace, SwapName);
            var vanillaSample = new ActionSample();
            var vanillaBar = false;
            var sampleUntil = Time.time + 3f;
            while (Time.time < sampleUntil && player.IsEquipActionQueued(mace))
            {
                vanillaSample.Take(player);
                vanillaBar |= bar != null && bar.activeSelf;
                yield return null;
            }
            yield return WaitIdle(player);

            // T04 through the key: queued, action text, message, equip effect, the sword blocks after.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            c.Check(!Hud.IsUserHidden() && MessageHud.instance != null, "the HUD shows messages (not hidden)");
            var watch = new TopLeftWatch();
            var effectFrame = player.m_lastEquipEffectFrame;
            Controls.TestKeyDown = KeyCode.H;
            yield return null;
            c.Check(ReferenceEquals(Hands.PendingSwap, axe) || Holds(player, axe, sword),
                $"swap key read by the per-frame update: the swap is queued ({HandsText(player)})");
            string actionText = null;
            var progress = -1f;
            var rising = false;
            var swapBar = false;
            var swapSample = new ActionSample();
            sampleUntil = Time.time + 3f;
            while (Time.time < sampleUntil && Hands.PendingSwap != null)
            {
                player.GetActionProgress(out var text, out var now);
                if (text != null)
                {
                    actionText = text;
                    rising |= progress >= 0f && now > progress;
                    progress = now;
                }
                swapBar |= bar != null && bar.activeSelf;
                swapSample.Take(player);
                yield return null;
            }
            yield return null;
            c.Check(Holds(player, axe, sword) && Hands.IsMarked(sword) && !Hands.IsMarked(axe),
                $"swap key: the hands swapped ({HandsText(player)})");
            var mainText = "Main hand: " + Localization.instance.Localize(axe.m_shared.m_name);
            c.Check(watch.Saw(mainText, axe.GetIcon()),
                $"top-left message '{mainText}' with the axe's icon (messages since the key: {watch.NewTexts()})");
            c.Check(player.m_lastEquipEffectFrame != effectFrame, "the swap triggered the game's equip effect (the equip sound)");
            c.Check(actionText == "$hud_equipping " + axe.m_shared.m_name && rising,
                $"while the swap runs the game's action is 'Equipping <axe>' and its progress rises (text '{actionText}', "
                + $"last progress {F2(progress)})");
            c.Check(swapSample.Equipping && vanillaSample.Equipping && swapBar == vanillaBar,
                "the swap runs the game's equip animation like a hotbar equip, with the action bar shown or not as for "
                + $"that equip (swap: equipping {swapSample.Equipping}, bar shown {swapBar}; hotbar equip of the mace: "
                + $"equipping {vanillaSample.Equipping}, bar shown {vanillaBar})");
            SelfTest.Note(SwapName, $"while queued, the swap: {swapSample.Text()}; a hotbar equip of the mace: "
                                    + $"{vanillaSample.Text()} (walk speed: InMinorActionSlowdown)");
            SelfTest.Note(SwapName, $"'Equipping' bar on screen during the swap = {swapBar} (the game's HUD draws it only for "
                                    + $"actions longer than 0.5 s; AxeIron equips in {F2(axe.m_shared.m_equipDuration)} s)");
            c.Check(ReferenceEquals(player.GetCurrentBlocker(), sword), "after the swap the sword (off hand) is the blocker");
            CheckBlock(c, player, sword.GetBaseBlockPower(), sword.m_shared.m_timedBlockBonus, false,
                "after the swap, block with the sword's block power");
            Invariant(c, player, "swap by key");

            // T32: a jump clears the game's action queue: no swap.
            yield return WaitIdle(player);
            var right = player.m_rightItem;
            var left = player.m_leftItem;
            var jumpQueued = Hands.TrySwap(player);
            player.Jump(true);
            yield return null;
            yield return null;
            yield return null;
            c.Check(jumpQueued && Hands.PendingSwap == null && !player.IsEquipActionQueued(left) && Holds(player, right, left),
                $"swap key, then a jump: no swap (queued {jumpQueued}; {HandsText(player)})");
            var landBy = Time.time + 4f;
            while (Time.time < landBy && !player.IsOnGround())
            {
                yield return null;
            }
            yield return WaitIdle(player);

            // T32: sprint started right after the key: the game clears the queue while running; the message says why.
            var runWatch = new TopLeftWatch();
            var sprintQueued = Hands.TrySwap(player);
            if (controller != null)
            {
                controller.enabled = false;
            }
            var sawRunning = false;
            var runUntil = Time.time + 1f;
            while (Time.time < runUntil && (Hands.PendingSwap != null || !sawRunning))
            {
                player.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, true, false);
                yield return null;
                Pin(player, spot);
                sawRunning |= player.IsRunning();
            }
            var sprintDropped = Hands.PendingSwap == null && !player.IsEquipActionQueued(left);
            var sprintMessage = runWatch.Saw(Hands.SprintMessage, null);
            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            if (controller != null)
            {
                controller.enabled = controllerOn;
            }
            yield return null;
            yield return null;
            c.Check(sprintQueued && sawRunning && sprintDropped && Holds(player, right, left),
                $"swap key, then sprinting at once: no swap (queued {sprintQueued}, sprinting seen {sawRunning}; {HandsText(player)})");
            c.Check(sprintMessage,
                $"sprint cancelled the swap: top-left message '{Hands.SprintMessage}' (messages since the key: {runWatch.NewTexts()})");
            yield return WaitIdle(player);

            // T04: key pressed every frame: one swap per equip time plus the game's queue pause, never faster.
            var changes = new List<float>();
            var lastMain = player.m_rightItem;
            var holdUntil = Time.time + 1.8f;
            while (Time.time < holdUntil)
            {
                Controls.TestKeyDown = Controls.SwapKey;
                yield return null;
                if (!ReferenceEquals(player.m_rightItem, lastMain))
                {
                    changes.Add(Time.time);
                    lastMain = player.m_rightItem;
                }
            }
            Controls.TestKeyDown = null;
            yield return WaitSwap(player);
            var minGap = float.MaxValue;
            for (var i = 1; i < changes.Count; i++)
            {
                minGap = Mathf.Min(minGap, changes[i] - changes[i - 1]);
            }
            var minEquip = Mathf.Min(sword.m_shared.m_equipDuration, axe.m_shared.m_equipDuration);
            c.Check(changes.Count >= 2 && minGap >= minEquip + 0.25f && minGap >= 0.45f,
                $"swap key pressed every frame for 1.8 s: {changes.Count} swaps, never closer than "
                + $"{(changes.Count >= 2 ? F2(minGap) : "?")} s (equip time {F2(minEquip)} s + queue pause 0.3 s; at most "
                + "one every half second)");
            Invariant(c, player, "key held");

            // T04: key during a swing does nothing.
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            right = player.m_rightItem;
            left = player.m_leftItem;
            player.m_queuedAttackTimer = 0.5f;
            var attackBy = Time.time + 1.5f;
            while (Time.time < attackBy && !player.InAttack())
            {
                yield return null;
            }
            player.m_queuedAttackTimer = 0f;
            var inAttack = player.InAttack();
            Controls.TestKeyDown = Controls.SwapKey;
            yield return null;
            yield return null;
            c.Check(inAttack && Hands.PendingSwap == null && !player.IsEquipActionQueued(left) && Holds(player, right, left),
                $"swap key during a swing: nothing (in attack {inAttack}; {HandsText(player)})");
            yield return WaitIdle(player);

            // T04: key together with the attack button: the swing first and no swap, or the swap first and the swing
            // after it. Never a swing whose weapons change midway.
            yield return new WaitForSeconds(0.4f);
            yield return WaitMinor(player);
            DualSwing.ResetRecords();
            DualSwing.Recording = true;
            right = player.m_rightItem;
            left = player.m_leftItem;
            player.m_queuedAttackTimer = 0.5f;
            Controls.TestKeyDown = Controls.SwapKey;
            var swappedAt = -1f;
            var watchUntil = Time.time + 2.5f;
            while (Time.time < watchUntil)
            {
                if (swappedAt < 0f && Holds(player, left, right))
                {
                    swappedAt = Time.time;
                }
                yield return null;
            }
            player.m_queuedAttackTimer = 0f;
            yield return WaitIdle(player);
            var mixed = DualSwing.Hits.Where(h => h.Swing >= 0 && h.Swing < DualSwing.Swings.Count
                                                  && !ReferenceEquals(h.Weapon, h.Hand == Hand.Off
                                                      ? DualSwing.Swings[h.Swing].Off
                                                      : DualSwing.Swings[h.Swing].Main))
                .Select(h => $"{h.Trigger} event {h.Event} {h.Hand} struck with {Name(h.Weapon)}").ToArray();
            // Swap came first: every swing must have the new main weapon (a swing started before the hands changed
            // would still have the old one).
            var early = swappedAt >= 0f
                ? DualSwing.Swings.Where(s => !ReferenceEquals(s.Main, left))
                    .Select(s => $"{s.Trigger} with {Name(s.Main)} main").ToArray()
                : new string[0];
            SelfTest.Note(SwapName, swappedAt >= 0f
                ? $"swap key + attack in one frame: the swap came first, {DualSwing.Swings.Count} swing(s) after it"
                : $"swap key + attack in one frame: the swing came first ({DualSwing.Swings.Count} swing(s)), no swap");
            c.Check(mixed.Length == 0 && early.Length == 0 && (swappedAt >= 0f || Holds(player, right, left)),
                "swap key + attack in one frame: no swing changes weapons midway, and a swing never starts with the old "
                + $"hands once the swap is under way (mixed: {string.Join("; ", mixed)}; early: {string.Join("; ", early)})");
            Invariant(c, player, "key with attack");
            c.Report();
        }
        finally
        {
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            player.SetWalk(walkBefore);
            if (controller != null)
            {
                controller.enabled = controllerOn;
            }
            Pin(player, spot);
            player.transform.rotation = facing;
            if (player.m_body != null)
            {
                player.m_body.rotation = facing;
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.hammer ----------

    // Radial menu hammer, the game's own steps (Valheim.UI.HammerItemElement.SetInteraction). On: remember LeftItem and
    // RightItem, UseItem(null, hammer, false). Off: UseItem(null, hammer, false), then EquipItem(last left) and
    // EquipItem(last right) at once. A hammer with an equip time only QUEUES its unequip in that call, so both equips
    // run while the hammer is still in the right hand.
    private static IEnumerator RunHammer()
    {
        var c = new Checks(HammerTestName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(HammerTestName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        try
        {
            bench = new Bench(player);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var hammer = bench.Give(HammerName);
            if (sword == null || axe == null || hammer == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            c.Check(Holds(player, sword, axe), $"pair before the hammer ({HandsText(player)})");
            var lastLeft = player.LeftItem;
            var lastRight = player.RightItem;
            UseHotbar(player, hammer, HammerTestName);
            yield return WaitEquipped(player, hammer);
            c.Check(Holds(player, hammer, null) && !sword.m_equipped && !axe.m_equipped,
                $"radial hammer on: the hammer in hand, both weapons put away ({HandsText(player)})");
            UseHotbar(player, hammer, HammerTestName);
            var unequipQueued = player.IsEquipActionQueued(hammer);
            if (lastLeft != null)
            {
                player.EquipItem(lastLeft);
            }
            if (lastRight != null)
            {
                player.EquipItem(lastRight);
            }
            yield return null;
            yield return WaitIdle(player);
            yield return null;
            SelfTest.Note(HammerTestName, $"Hammer equip time {F2(hammer.m_shared.m_equipDuration)} s; its unequip was queued "
                                          + $"when the two weapons were equipped again = {unequipQueued}; hands after: "
                                          + HandsText(player));
            c.Check(Holds(player, sword, axe),
                "radial hammer off: the same hands as before, sword main and axe off (hammer unequip queued "
                + $"{unequipQueued}; {HandsText(player)})");
            c.Check(!hammer.m_equipped && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                $"radial hammer off: hammer put away, the off-hand marker on the axe only ({HandsText(player)})");
            Invariant(c, player, "radial hammer");
            c.Report();
        }
        finally
        {
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.toggle ----------

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var player = Player.m_localPlayer;
        var plugin = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;
        if (player == null || plugin == null)
        {
            SelfTest.Fail(ToggleName, player == null ? "no local player" : "the plugin instance was not found");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var switchedOff = false;
        try
        {
            bench = new Bench(player);
            var sword = bench.Give(Sword);
            var sword2 = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || sword2 == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var inventory = bench.Inventory;
            var vis = player.m_visEquipment;
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && StateI(player) == 15, $"pair before turning off ({HandsText(player)})");
            var infoCount = MoveTemplates.InfoCount;

            // L01: off. The off-hand weapon is back in the inventory at once; everything after is the game's own.
            plugin.TestSwitchOff();
            switchedOff = true;
            c.Check(Holds(player, sword, null) && !axe.m_equipped && !Hands.IsMarked(axe) && inventory.ContainsItem(axe),
                $"turned off: the axe is back in the inventory (not equipped, no marker), the sword stays ({HandsText(player)})");
            yield return null;
            yield return null;
            c.Check(StateI(player) == (int)sword.m_shared.m_animationState,
                $"turned off: the sword's own stance (statei {StateI(player)}, {(int)sword.m_shared.m_animationState} expected)");
            player.EquipItem(sword2);
            yield return null;
            c.Check(Holds(player, sword2, null) && !sword.m_equipped,
                $"turned off: a second one-handed weapon replaces the first ({HandsText(player)})");
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            // The game's own gates open first (a test before may have left the player sitting, lying or frozen): a
            // refused start then names its cause.
            yield return WaitCanAttack(player);
            var gate = _attackGate;
            var started = player.StartAttack(null, false);
            var attack = player.m_currentAttack;
            c.Check(started && attack != null && ReferenceEquals(attack.m_weapon, sword2)
                    && attack.m_attackAnimation == sword2.m_shared.m_attack.m_attackAnimation
                    && DualSwing.RecordedClone == null,
                $"turned off: the attack is the weapon's own (started {started}, "
                + $"{(attack != null ? attack.m_attackAnimation : "no attack")}; before the press: "
                + $"{gate ?? "nothing in the game's way"})");
            yield return WaitIdle(player);
            // M11 (items): a weapon that still carries the off-hand marker is a normal weapon for a game without the
            // feature: it equips in the main hand and swings its own move.
            Hands.SetMarked(axe, true);
            var equipped = player.EquipItem(axe);
            yield return null;
            c.Check(equipped && Holds(player, axe, null) && !sword2.m_equipped,
                $"turned off: a weapon carrying the off-hand marker equips like any weapon ({HandsText(player)})");
            yield return WaitMinor(player);
            yield return WaitCanAttack(player);
            gate = _attackGate;
            started = player.StartAttack(null, false);
            attack = player.m_currentAttack;
            c.Check(started && attack != null && ReferenceEquals(attack.m_weapon, axe)
                    && attack.m_attackAnimation == axe.m_shared.m_attack.m_attackAnimation,
                $"turned off: it swings its own move (started {started}, {(attack != null ? attack.m_attackAnimation : "no attack")}; "
                + $"before the press: {gate ?? "nothing in the game's way"})");
            yield return WaitIdle(player);
            // L02: a saved pair loads as one weapon, no error, whichever comes first in the inventory.
            foreach (var offFirst in new[] { true, false })
            {
                LoadPair(player, inventory, sword, axe, offFirst);
                yield return null;
                var one = player.m_leftItem == null
                          && (ReferenceEquals(player.m_rightItem, sword) || ReferenceEquals(player.m_rightItem, axe))
                          && sword.m_equipped != axe.m_equipped
                          && sword.m_equipped == ReferenceEquals(player.m_rightItem, sword);
                c.Check(one,
                    $"turned off: a saved pair ({(offFirst ? "off-hand" : "main")} weapon first) loads as one weapon in the "
                    + $"main hand, the other not equipped ({HandsText(player)}; flags sword {sword.m_equipped}, axe {axe.m_equipped})");
            }

            // On again: a marker on the main-hand weapon (it got there while off) is cleared, the Info line comes
            // again, pairs work.
            bench.Empty();
            Hands.SetMarked(axe, true);
            player.EquipItem(axe);
            plugin.TestSwitchOn();
            switchedOff = false;
            ServerRules.TestRules = DualRules.Defaults;
            Controls.TestMainHandHeld = false;
            c.Check(Holds(player, axe, null) && !Hands.IsMarked(axe),
                $"turned on: the marker a main-hand weapon still carried is cleared ({HandsText(player)})");
            yield return null;
            yield return null;
            c.Check(MoveTemplates.InfoCount == infoCount + 1 && MoveTemplates.LastInfo == DefaultMovesInfo,
                $"turned on: the moves Info line is logged again, once ({MoveTemplates.InfoCount - infoCount} logged: "
                + $"'{MoveTemplates.LastInfo}')");
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe) && StateI(player) == 15,
                $"turned on: pairs work again, dual axe stance ({HandsText(player)})");
            Invariant(c, player, "on again");

            // L01: off while the pair is sheathed: only the sword stays on the back, where the game puts it, and R
            // draws only the sword.
            player.HideHandItems();
            yield return null;
            yield return null;
            c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"pair sheathed before turning off ({HandsText(player)})");
            plugin.TestSwitchOff();
            switchedOff = true;
            c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && player.m_hiddenLeftItem == null
                    && !Hands.IsMarked(axe) && !axe.m_equipped,
                $"turned off while sheathed: the off-hand weapon is forgotten, the sword stays sheathed ({HandsText(player)})");
            yield return null;
            yield return null;
            yield return null;
            if (vis != null)
            {
                c.Check(vis.m_leftBackItemInstance == null, "turned off while sheathed: no second weapon drawn on the back");
                CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, vis.m_backMelee,
                    "turned off while sheathed: the sword");
            }
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, null) && !axe.m_equipped,
                $"turned off while sheathed: R draws only the sword ({HandsText(player)})");
            plugin.TestSwitchOn();
            switchedOff = false;
            ServerRules.TestRules = DualRules.Defaults;
            Controls.TestMainHandHeld = false;
            yield return null;
            yield return null;
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && StateI(player) == 15,
                $"turned on after the sheathed case: pairs work ({HandsText(player)})");
            Invariant(c, player, "on after sheathed");
            c.Report();
        }
        finally
        {
            if (switchedOff)
            {
                try
                {
                    plugin.TestSwitchOn();
                }
                catch (Exception e)
                {
                    SelfTest.Fail(ToggleName, $"could not turn the feature back on after the test: {e}");
                }
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }
}
#endif
