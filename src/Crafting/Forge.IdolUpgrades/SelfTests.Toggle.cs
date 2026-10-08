#if DEBUG
using System;
using System.Collections;
using System.Linq;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = self tests of mod turned off and on while game run. Switch = Plugin.TestSetOff: framework
// really deactivate the mod (OnDeactivated, patches removed) and activate it again, like player's toggle, but me
// write no setting.
//   forge.toggle       off: vanilla Forge (no IDOLS tab, vanilla text, vanilla 65 % roll that break the item), plain
//                      icons with quality number, levels still never merge; on with inventory open: stars at once;
//                      back at the Forge: everything there again
//   forge.off.upgrade  turned off during idol upgrade timer: timer stop, vanilla rows, nothing used
//   forge.off.refine   turned off during refinement timer: timer stop, no vanilla roll, nothing used
//   forge.on.at-forge  turned on with Forge window open: odds, text and starred idol back at once
//   forge.bug.tab-after-on  (real bug, fail until mod fixed) same moment: IDOLS tab back at once too
//   forge.popup.off    starred idol picked up, mod turned off at once: popups still show starred icon
internal static partial class SelfTests
{
    private const string ToggleName = "forge.toggle";
    private const string OffUpgradeName = "forge.off.upgrade";
    private const string OffRefineName = "forge.off.refine";
    private const string OnAtForgeName = "forge.on.at-forge";
    private const string BugTabAfterOnName = "forge.bug.tab-after-on";
    private const string PopupOffName = "forge.popup.off";

    private static Sprite HotbarIcon(int x)
    {
        foreach (var bar in Object.FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None))
        {
            if (bar != null && bar.isActiveAndEnabled && bar.m_elements != null && x >= 0 && x < bar.m_elements.Count && bar.m_elements[x].m_used)
            {
                return bar.m_elements[x].m_icon.sprite;
            }
        }
        return null;
    }

    // Me move stack to a free slot of hotbar row. Give back the stack there (new object) and its column, or -1.
    private static ItemDrop.ItemData ToHotbar(Rig rig, ItemDrop.ItemData item, out int column)
    {
        column = -1;
        if (item == null)
        {
            return null;
        }
        if (item.m_gridPos.y == 0)
        {
            column = item.m_gridPos.x;
            return item;
        }
        for (var x = 0; x < rig.Inv.GetWidth(); x++)
        {
            if (rig.Inv.GetItemAt(x, 0) == null)
            {
                rig.Inv.MoveItemToThis(rig.Inv, item, item.m_stack, x, 0);
                column = x;
                return rig.Inv.GetItemAt(x, 0);
            }
        }
        return item;
    }

    // Mod off: one vanilla refinement of this item with a roll me know. Me seed Unity random so its next number
    // (vanilla DoCrafting roll = its first random call for an item that no stack) lie in [low, high].
    private static IEnumerator VanillaRoll(Rig rig, ItemDrop.ItemData item, float low, float high)
    {
        yield return rig.Select(item);
        var gui = rig.Gui;
        var seed = -1;
        var saved = UnityEngine.Random.state;
        try
        {
            for (var s = 1; s < 20000 && seed < 0; s++)
            {
                UnityEngine.Random.InitState(s);
                var roll = UnityEngine.Random.Range(0f, 1f);
                if (roll >= low && roll <= high)
                {
                    seed = s;
                }
            }
        }
        finally
        {
            UnityEngine.Random.state = saved;
        }
        if (rig.Row < 0 || seed < 0)
        {
            rig.C.Check(false, $"vanilla roll not possible (row {rig.Row}, seed {seed})");
            yield break;
        }
        gui.OnCraftPressed();
        if (gui.m_craftTimer < 0f)
        {
            rig.C.Check(false, $"the vanilla press did not start the timer (message '{rig.Center}')");
            yield break;
        }
        saved = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(seed);
            gui.DoCrafting(rig.P);
        }
        finally
        {
            UnityEngine.Random.state = saved;
            gui.m_craftTimer = -1f;
        }
        yield return null;
    }

    // ---------- forge.toggle (T14, T15, T16) ----------

    private static IEnumerator RunToggle()
    {
        var rig = new Rig(ToggleName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            IdolChoice.TestPick = IdolPick.Highest;
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var inv = rig.Inv;
            var idolName = NameOf(WoodIdol);
            var plainIcon = PlainIconOf(WoodIdol);
            var axeName = NameOf("AxeStone");
            var button = L("$inventory_upgraderbutton");
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 3, 1);
            var star = ToHotbar(rig, rig.Give(WoodIdol, 1, 3), out var column);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.IdolsTabButton() != null && rig.ButtonLabel == button + " (75%)", $"before: IDOLS tab there, button '{rig.ButtonLabel}'");
            yield return rig.CloseWindow();

            // ---- Off, then back to the Forge ----
            c.Check(SetModOff(true) && !Plugin.TestActive, $"mod turned off ({Plugin.TestStatus})");
            yield return rig.ReopenForge();
            yield return rig.Select(axe);
            var tabs = gui.m_tabCraft.transform.parent;
            c.Check(tabs.Find("MC_IdolsTab") == null && gui.m_tabUpgrade.gameObject.activeSelf && !gui.m_tabUpgrade.interactable, "off: no IDOLS tab, UPGRADE selected");
            c.Check(rig.Row >= 0 && rig.CraftText == L("$inventory_upgraderwarning"), $"off: the vanilla refinement text, is '{rig.CraftText}'");
            if (English)
            {
                c.Check(rig.CraftText == "Maximum safe quality. Refinement may break your item.", $"off: text reads as in vanilla, is '{rig.CraftText}'");
            }
            c.Check(rig.ButtonLabel == button, $"off: the vanilla button without odds, is '{rig.ButtonLabel}'");
            c.Check(!OursPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting)) && !OursPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))
                    && !OursPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui)) && !OursPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon)),
                "off: none of this mod's patches is left on the Forge, the grid or the icons");
            var shared = Prefab(WoodIdol).m_itemData.m_shared;
            c.Check(Mathf.Abs(shared.m_upgradeChance - 0.65f) < 0.001f && shared.m_breakChance >= 1f,
                $"idol game data untouched: {shared.m_upgradeChance * 100f:0}% success, break chance {shared.m_breakChance:0.##} on a failure");
            var slot = rig.Element(gui.m_playerGrid, star);
            c.Check(star != null && star.GetIcon() == plainIcon && slot != null && slot.m_icon.sprite == plainIcon && rig.SlotIcon(0) == plainIcon,
                $"off: idol icons lose their stars (slot {Describe(slot != null ? slot.m_icon.sprite : null)})");
            c.Check(slot != null && slot.m_quality.enabled && slot.m_quality.text == "3", $"off: the 2-star idol shows its quality number 3 ('{(slot != null ? slot.m_quality.text : "")}')");

            // Starred and plain still never merge.
            var plain = rig.Find(WoodIdol, 1);
            c.Check(shared.m_maxQuality == 4 && star != null && star.m_shared.m_maxQuality == 4, "off: idols keep their 4 quality levels in the game data");
            if (plain != null && star != null)
            {
                var plainPos = plain.m_gridPos;
                var starPos = star.m_gridPos;
                gui.OnSelectedItem(gui.m_playerGrid, plain, plainPos, InventoryGrid.Modifier.Select);
                gui.OnSelectedItem(gui.m_playerGrid, star, starPos, InventoryGrid.Modifier.Select);
                yield return null;
                c.Check(Is(inv.GetItemAt(starPos.x, starPos.y), idolName, 1, 3) && Is(inv.GetItemAt(plainPos.x, plainPos.y), idolName, 3, 1)
                        && rig.Has(WoodIdol, 1) == 3 && rig.Has(WoodIdol, 3) == 1, "off: plain idols dropped on the starred one swap places, they do not merge");
                gui.OnSelectedItem(gui.m_playerGrid, inv.GetItemAt(plainPos.x, plainPos.y), plainPos, InventoryGrid.Modifier.Select);
                gui.OnSelectedItem(gui.m_playerGrid, inv.GetItemAt(starPos.x, starPos.y), starPos, InventoryGrid.Modifier.Select);
                yield return null;
                gui.SetupDragItem(null, null, 1);
            }

            // Vanilla odds: roll about 0.5 succeed (our 35 % with plain idol would fail), roll about 0.68 fail (our
            // 75 % with 2-star idol would succeed) and destroy the item.
            var idols = rig.Has(WoodIdol);
            yield return VanillaRoll(rig, axe, 0.45f, 0.6f);
            var refined = rig.Find("AxeStone", 4);
            c.Check(refined != null && rig.Has("AxeStone") == 1 && rig.Has(WoodIdol) == idols - 1,
                $"off: a roll of about 0.5 is a success (vanilla 65 %): axe level {(refined != null ? "4" : "not 4")}, {idols - rig.Has(WoodIdol)} idol used");
            if (refined != null)
            {
                var recover = RecoverNames(RecipeOf("AxeStone"));
                var before = CountAll(rig, recover);
                rig.ClearCenter();
                yield return VanillaRoll(rig, refined, 0.66f, 0.72f);
                var after = CountAll(rig, recover);
                c.Check(rig.Has("AxeStone") == 0 && rig.Center == L(Localization.instance.Localize("$msg_upgrader_broke", axeName, "4")),
                    $"off: a roll of about 0.68 is a failure and destroys the item (vanilla), message '{rig.Center}'");
                c.Check(recover.Length > 0 && Enumerable.Range(0, recover.Length).Any(i => after[i] > before[i]), "off: part of its materials comes back (vanilla)");
            }
            yield return rig.CloseWindow();

            // ---- Inventory open, mod turned on: stars at once ----
            star = rig.Find(WoodIdol, 3) ?? rig.Give(WoodIdol, 1, 3);
            star = ToHotbar(rig, star, out column);
            yield return new WaitForSecondsRealtime(0.3f);
            c.Check(column >= 0 && HotbarIcon(column) == plainIcon, $"off: the hotbar shows the plain icon ({Describe(HotbarIcon(column))})");
            yield return rig.OpenInventory();
            slot = rig.Element(gui.m_playerGrid, star);
            c.Check(slot != null && slot.m_icon.sprite == plainIcon && slot.m_quality.enabled, "off, inventory open: plain icon with a quality number");
            c.Check(SetModOff(false) && Plugin.TestActive, $"mod turned on again ({Plugin.TestStatus})");
            yield return Frames(3);
            c.Check(slot != null && slot.m_icon.sprite == StarIcons.Get(plainIcon, 2) && !slot.m_quality.enabled,
                $"on: stars appear in the open inventory at once, no quality number ({Describe(slot != null ? slot.m_icon.sprite : null)})");
            yield return rig.CloseWindow();
            yield return new WaitForSecondsRealtime(0.3f);
            c.Check(column >= 0 && HotbarIcon(column) == StarIcons.Get(plainIcon, 2), $"on: and on the hotbar ({Describe(HotbarIcon(column))})");

            // ---- Back at the Forge: everything is there again ----
            axe = rig.Give("AxeStone", 1, 3);
            yield return rig.ReopenForge();
            yield return rig.Select(axe);
            var tab = rig.IdolsTabButton();
            c.Check(tab != null && tab.gameObject.activeSelf, "on: the IDOLS tab is back at the Forge");
            c.Check(rig.Row >= 0 && rig.ButtonLabel == button + " (75%)" && rig.CraftText == "75% chance with a 2-star idol. A failure costs 1 level.",
                $"on: the odds and the text are back, '{rig.ButtonLabel}' / '{rig.CraftText}'");
            c.Check(OursPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting)) && StarIcons.IsStarSprite(rig.SlotIcon(0)), "on: patches back, starred idol under the requirements");
            yield return rig.ClickIdolsTab();
            c.Check(IdolsTab.Mode && gui.m_availableRecipes.Count > 0 && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)), "on: the Idols tab lists the idols again");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.off.upgrade (T18) ----------

    private static IEnumerator RunOffUpgrade()
    {
        var rig = new Rig(OffUpgradeName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 1, 1);
            rig.Give(SilverIdol, 2, 1);
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 5);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            var stack = rig.Find(SilverIdol, 1);
            yield return rig.Select(stack);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the plain idol stack is not listed in the Idols tab");
                c.Report();
                yield break;
            }
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f && IdolUpgrade.IsOurs(gui.m_craftRecipe), "idol upgrade timer running");

            c.Check(SetModOff(true) && !Plugin.TestActive, $"mod turned off during the timer ({Plugin.TestStatus})");
            c.Check(gui.m_craftTimer < 0f && gui.m_craftRecipe == null && gui.m_craftUpgradeItem == null, "the timer stops at once");
            yield return Frames(3);
            var tabs = gui.m_tabCraft.transform.parent;
            c.Check(!IdolsTab.Mode && tabs.Find("MC_IdolsTab") == null && InventoryGui.IsVisible(), "the IDOLS tab is gone, the window stays open");
            c.Check(gui.m_availableRecipes.Count > 0 && gui.m_availableRecipes.All(r => !IdolUpgrade.IsOurs(r.Recipe)) && RowOf(gui, axe) >= 0
                    && !IdolUpgrade.IsOurs(gui.m_selectedRecipe.Recipe),
                $"the list switches to the vanilla Upgrade rows, no idol row left ({gui.m_availableRecipes.Count} rows)");
            yield return new WaitForSecondsRealtime(1.5f);
            c.Check(gui.m_craftTimer < 0f && rig.Has(SilverIdol, 1) == 2 && rig.Has(SilverIdol, 2) == 0 && rig.Has("Silver") == 5 && rig.Has("TrophyWolf") == 5
                    && rig.Has(WoodIdol) == 1 && rig.Inv.ContainsItem(axe) && axe.m_quality == 3,
                $"nothing is used, no idol disappears ({rig.Has(SilverIdol, 1)} plain silver idols, silver {rig.Has("Silver")}, trophies {rig.Has("TrophyWolf")})");
            c.Check(SetModOff(false) && Plugin.TestActive, $"mod on again ({Plugin.TestStatus})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.off.refine (T23) ----------

    private static IEnumerator RunOffRefine()
    {
        var rig = new Rig(OffRefineName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 1, 1);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            axe.m_durability = axe.GetMaxDurability() * 0.5f;
            var durability = axe.m_durability;
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f && ReferenceEquals(gui.m_craftUpgradeItem, axe), "refinement timer running");

            c.Check(SetModOff(true) && !Plugin.TestActive, $"mod turned off during the timer ({Plugin.TestStatus})");
            c.Check(gui.m_craftTimer < 0f && gui.m_craftRecipe == null && gui.m_craftUpgradeItem == null, "turning off cancels the running refinement at once");
            yield return Frames(3);
            c.Check(InventoryGui.IsVisible() && rig.CraftText == L("$inventory_upgraderwarning") && rig.ButtonLabel == L("$inventory_upgraderbutton"),
                $"the panel shows the vanilla refinement text and button ('{rig.CraftText}' / '{rig.ButtonLabel}')");
            yield return new WaitForSecondsRealtime(1.5f);
            c.Check(gui.m_craftTimer < 0f && rig.Inv.ContainsItem(axe) && axe.m_quality == 3 && Mathf.Approximately(axe.m_durability, durability) && rig.Has(WoodIdol) == 1,
                $"no vanilla roll follows: the item keeps its level and durability, the idol is not used (level {axe.m_quality}, {rig.Has(WoodIdol)} idol)");
            c.Check(SetModOff(false) && Plugin.TestActive, $"mod on again ({Plugin.TestStatus})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.on.at-forge (T28) ----------

    private static IEnumerator RunOnAtForge()
    {
        yield return OnAtForge(OnAtForgeName, false);
    }

    // ---------- forge.bug.tab-after-on (T28: real bug, fail until mod fixed) ----------

    // Run 2026-10-07: 5 frames after mod turned on with Forge window open, odds and text were back but there was no
    // MC_IdolsTab button. Tab is made only by the crafting panel refresh (IdolsTab.AfterPanelUpdate) and
    // Plugin.OnActivated ask for none, so it come back only when window is opened again or the list rebuilt.
    private static IEnumerator RunBugTabAfterOn()
    {
        yield return OnAtForge(BugTabAfterOnName, true);
    }

    // Same play for both: Forge open, mod off, mod on, window never closed. tabOnly = only the IDOLS tab check.
    private static IEnumerator OnAtForge(string name, bool tabOnly)
    {
        var rig = new Rig(name);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            IdolChoice.TestPick = IdolPick.Highest;
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 1, 3);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            var tabs = gui.m_tabCraft.transform.parent;
            var before = rig.IdolsTabButton();
            c.Check(rig.Row >= 0 && before != null && before.gameObject.activeSelf && SetModOff(true) && !Plugin.TestActive,
                $"IDOLS tab there, then mod turned off with the Forge window open ({Plugin.TestStatus})");
            yield return Frames(3);
            c.Check(tabs.Find("MC_IdolsTab") == null && rig.CraftText == L("$inventory_upgraderwarning"), "off: no IDOLS tab, vanilla text");

            c.Check(SetModOff(false) && Plugin.TestActive, $"mod turned on again, the window still open ({Plugin.TestStatus})");
            yield return Frames(5);
            if (!tabOnly)
            {
                c.Check(InventoryGui.IsVisible() && rig.ButtonLabel.EndsWith("(75%)", StringComparison.Ordinal) && rig.CraftText.StartsWith("75% chance", StringComparison.Ordinal),
                    $"on: the odds and the text are back at once ('{rig.ButtonLabel}' / '{rig.CraftText}')");
                c.Check(StarIcons.IsStarSprite(rig.SlotIcon(0)), $"on: the idol under the requirements shows its stars again: {Describe(rig.SlotIcon(0))}");
            }
            else
            {
                var tab = rig.IdolsTabButton();
                c.Check(InventoryGui.IsVisible() && tab != null && tab.gameObject.activeSelf, "on: the IDOLS tab is back at once, without closing the Forge window");
            }
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.popup.off (T05) ----------

    // Like forge.popup, but mod turned off right after pickup: 'added' message and 'New material' popup were queued
    // with starred icon, and that icon must still be alive when they show.
    private static IEnumerator RunPopupOff()
    {
        var rig = new Rig(PopupOffName);
        var c = rig.C;
        var hud = MessageHud.instance;
        try
        {
            var player = rig.P;
            var idolName = NameOf(SilverIdol);
            StarIcons.Clear();
            player.m_knownMaterial.Remove(idolName);
            var dropped = rig.Drop(SilverIdol, 3, 1, 1.5f);
            yield return null;

            // Same clean screen as forge.popup: next top-left message and next unlock popup are this pickup's.
            hud.ClearUnlockQueue();
            hud.m_msgQeue.Clear();
            hud._crossFadeTextBuffer.Clear();
            hud.HideAll();
            hud.currentMsg = new MessageHud.MsgData();
            hud.m_msgQueueTimer = 1f;
            c.Check(dropped != null && player.Pickup(dropped.gameObject, false, false), "pickup of a never-seen 2-star idol");
            c.Check(SetModOff(true) && !Plugin.TestActive, $"mod turned off right after the pickup ({Plugin.TestStatus})");

            var addedText = L("$msg_added " + idolName);
            var mine = hud.m_msgQeue.FirstOrDefault(m => m != null && m.m_text == addedText);
            var shownBy = Time.realtimeSinceStartup + 3f;
            while (mine != null && !ReferenceEquals(hud.currentMsg, mine) && Time.realtimeSinceStartup < shownBy)
            {
                yield return null;
            }
            var shown = hud.m_messageIcon.sprite;
            c.Check(mine != null && ReferenceEquals(hud.currentMsg, mine), $"the idol's own 'added' message shows ('{hud.m_messageText.text}')");
            c.Check(shown != null && StarIcons.IsStarSprite(shown) && shown.texture != null, $"off: the 'added' message still shows the starred icon: {Describe(shown)}");

            Image unlockIcon = null;
            var wanted = L(idolName);
            var until = Time.realtimeSinceStartup + 6f;
            while (unlockIcon == null && Time.realtimeSinceStartup < until)
            {
                foreach (var msg in hud.m_unlockMessages)
                {
                    if (msg != null && msg.transform.Find("UnlockMessage/UnlockDescription")?.GetComponent<TMP_Text>() is { } text && text.text == wanted)
                    {
                        unlockIcon = msg.transform.Find("UnlockMessage/icon_bkg/UnlockIcon")?.GetComponent<Image>();
                    }
                }
                yield return null;
            }
            c.Check(unlockIcon != null, $"a 'New material' popup for {wanted} appears");
            if (unlockIcon != null)
            {
                yield return new WaitForSecondsRealtime(0.8f);
                c.Check(unlockIcon.sprite != null && StarIcons.IsStarSprite(unlockIcon.sprite) && unlockIcon.sprite.texture != null,
                    $"off: the 'New material' popup still shows the starred icon, not a blank square: {Describe(unlockIcon.sprite)}");
                SelfTest.Screenshot(PopupOffName, "unlock-after-off");
                yield return null;
                yield return null;
            }
            c.Check(SetModOff(false) && Plugin.TestActive, $"mod on again ({Plugin.TestStatus})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }
}
#endif
