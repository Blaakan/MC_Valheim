using System.Collections.Generic;
using System.Text;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = the "IDOLS" tab at the Forge of Potential. Own button (clone of the Craft tab, which the Forge hide), own
// flag (Mode): vanilla tabs keep their meaning and vanilla never craft at the Forge. In Mode the recipe list show one
// row per idol stack you carry below 3 stars; the panel show cost, the button upgrade one idol.
internal static class IdolsTab
{
    internal const string Label = "IDOLS";

    internal static bool Mode;

    private static Button _tab;
    private static InventoryGui _tabFor;
    private static readonly List<ItemDrop.ItemData> Stacks = new List<ItemDrop.ItemData>();
    private static readonly StringBuilder Text = new StringBuilder();

    internal static bool AtForge(Player player)
    {
        if (player == null)
        {
            return false;
        }
        var station = player.GetCurrentCraftingStation();
        return station != null && station.m_upgrader;
    }

    // After vanilla UpdateCraftingPanel (it re-select Upgrade on every call at the Forge): show our tab at the Forge
    // only, and draw the selected look (current tab = not interactable, vanilla style).
    internal static void AfterPanelUpdate(InventoryGui gui)
    {
        if (!AtForge(Player.m_localPlayer))
        {
            Leave(gui);
            if (_tab != null)
            {
                _tab.gameObject.SetActive(false);
            }
            return;
        }
        ForgeGuard.WarnForeignPatches();
        var tab = Tab(gui);
        if (tab == null)
        {
            return;
        }
        tab.gameObject.SetActive(true);
        tab.interactable = !Mode;
        gui.m_tabUpgrade.interactable = Mode;
    }

    // Leave Idols mode outside a tab click: put back the vanilla Forge tab look (Upgrade selected), else the next
    // station would show both vanilla tabs unselected.
    internal static void Leave(InventoryGui gui)
    {
        // Only when Craft is the unselected one (vanilla Forge state): never leave both tabs selected.
        if (Mode && gui != null && gui.m_tabUpgrade != null && gui.m_tabCraft != null && gui.m_tabCraft.interactable)
        {
            gui.m_tabUpgrade.interactable = false;
        }
        Mode = false;
    }

    // Mod turning off (patches still on during this call). Vanilla must never get our private idol recipe: it would
    // take it for a refinement and remove the whole idol stack. So: cancel a running idol upgrade (or a refinement
    // started under our rules; nothing is used before the timer ends), drop the selection and rebuild the list with
    // vanilla rows.
    internal static void BeforeOff()
    {
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            Mode = false;
            return;
        }
        if (gui.m_craftTimer >= 0f && (IdolUpgrade.IsOurs(gui.m_craftRecipe) || AtForge(Player.m_localPlayer)))
        {
            gui.m_craftTimer = -1f;
            gui.m_craftRecipe = null;
            gui.m_craftUpgradeItem = null;
        }
        var ours = Mode || IdolUpgrade.IsOurs(gui.m_selectedRecipe.Recipe);
        Leave(gui);
        if (ours)
        {
            gui.m_selectedRecipe = default;
            // Vanilla UpdateCraftingPanel read the local player (gone while the game quits).
            if (InventoryGui.IsVisible() && Player.m_localPlayer != null)
            {
                gui.UpdateCraftingPanel();
            }
        }
    }

    internal static void Restore()
    {
        Mode = false;
        if (_tab != null)
        {
            Object.Destroy(_tab.gameObject);
        }
        _tab = null;
        _tabFor = null;
    }

    private static Button Tab(InventoryGui gui)
    {
        if (_tab != null && ReferenceEquals(_tabFor, gui))
        {
            return _tab;
        }
        if (_tab != null)
        {
            Object.Destroy(_tab.gameObject);
        }
        var source = gui.m_tabCraft;
        if (source == null)
        {
            return null;
        }
        var go = Object.Instantiate(source.gameObject, source.transform.parent);
        go.name = "MC_IdolsTab";
        // Vanilla Localize component would write "CRAFT" back on language change.
        foreach (var localize in go.GetComponentsInChildren<Localize>(true))
        {
            Object.Destroy(localize);
        }
        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = Label;
        }
        var button = go.GetComponent<Button>();
        // Prefab onClick call OnTabCraftPressed (persistent listener): new event, only ours.
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => OnClick(gui));
        (go.transform as RectTransform).anchoredPosition = (source.transform as RectTransform).anchoredPosition;
        go.SetActive(false);
        _tab = button;
        _tabFor = gui;
        return button;
    }

    private static void OnClick(InventoryGui gui)
    {
        try
        {
            if (gui == null || Mode)
            {
                return;
            }
            Mode = true;
            gui.SetActiveGroup(gui.m_uiGroups[3]);
            gui.UpdateCraftingPanel();
        }
        catch (System.Exception e)
        {
            PatchGuard.Report("Idols tab click", e);
        }
    }

    // Rows: one per idol stack under 3 stars, sorted by tier, family, level. Same row prefab and list code as
    // vanilla (AddRecipeToList), so Crafting Search and Sort and gamepad selection work as usual.
    internal static void BuildRows(InventoryGui gui, Player player)
    {
        foreach (var row in gui.m_availableRecipes)
        {
            Object.Destroy(row.InterfaceElement);
        }
        gui.m_availableRecipes.Clear();

        Stacks.Clear();
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (IdolCatalog.IsIdol(item) && IdolLevels.Of(item) < IdolLevels.Max)
            {
                Stacks.Add(item);
            }
        }
        Stacks.Sort((a, b) =>
        {
            var ia = IdolCatalog.IdolOf(a);
            var ib = IdolCatalog.IdolOf(b);
            var c = ia.Tier.CompareTo(ib.Tier);
            if (c == 0)
            {
                c = string.CompareOrdinal(ia.PrefabName, ib.PrefabName);
            }
            if (c == 0)
            {
                c = a.m_quality.CompareTo(b.m_quality);
            }
            if (c == 0)
            {
                c = (a.m_gridPos.y * 100 + a.m_gridPos.x).CompareTo(b.m_gridPos.y * 100 + b.m_gridPos.x);
            }
            return c;
        });

        foreach (var item in Stacks)
        {
            var idol = IdolCatalog.IdolOf(item);
            var canCraft = IdolUpgrade.Blocker(player, item, out _) == null;
            gui.AddRecipeToList(player, IdolUpgrade.RecipeFor(idol), item, canCraft);
            var element = gui.m_availableRecipes[gui.m_availableRecipes.Count - 1].InterfaceElement;
            var icon = element.transform.Find("icon")?.GetComponent<Image>();
            if (icon != null)
            {
                // Vanilla put the prefab icon (plain); me show the stack's own stars.
                icon.sprite = StarIcons.Get(PlainIcon(idol), IdolLevels.Of(item));
            }
            var quality = element.transform.Find("QualityLevel")?.GetComponent<TMP_Text>();
            if (quality != null)
            {
                // Vanilla print quality (1..4); stars already on icon.
                quality.gameObject.SetActive(false);
            }
        }
        Stacks.Clear();

        // Same list size and row places as the end of vanilla UpdateRecipeList.
        var height = Mathf.Max(gui.m_recipeListBaseSize, gui.m_availableRecipes.Count * gui.m_recipeListSpace);
        gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        for (var i = 0; i < gui.m_availableRecipes.Count; i++)
        {
            (gui.m_availableRecipes[i].InterfaceElement.transform as RectTransform).anchoredPosition =
                new Vector2(0f, i * -gui.m_recipeListSpace);
        }
    }

    internal static Sprite PlainIcon(IdolCatalog.Idol idol)
    {
        var icons = idol.Prefab.m_itemData.m_shared.m_icons;
        return icons != null && icons.Length > 0 ? icons[0] : null;
    }

    // Every frame after vanilla UpdateRecipe while an idol row is selected: icon, text, cost slots, button.
    internal static void AfterUpdateRecipe(InventoryGui gui, Player player)
    {
        var item = gui.m_selectedRecipe.ItemData;
        var idol = IdolCatalog.IdolOf(item);
        if (idol == null)
        {
            return;
        }
        var level = IdolLevels.Of(item);
        var target = Mathf.Min(level + 1, IdolLevels.Max);
        var blocker = IdolUpgrade.Blocker(player, item, out var cost);
        var free = IdolUpgrade.Free();

        gui.m_recipeIcon.sprite = StarIcons.Get(PlainIcon(idol), target);
        gui.m_recipeDecription.text = Localization.instance.Localize(Description(idol, level, cost));
        gui.m_itemCraftType.gameObject.SetActive(true);
        gui.m_itemCraftType.text = level >= IdolLevels.Max ? "Maximum level" : $"Upgrade to {Stars(target)}";

        var slots = gui.m_recipeRequirementList;
        var used = 0;
        if (level < IdolLevels.Max)
        {
            if (cost.MaterialNeed > 0 && cost.Material != null && used < slots.Length)
            {
                ForgeUi.ShowSlot(slots[used++].transform, cost.Material.m_itemData.GetIcon(),
                    cost.Material.m_itemData.m_shared.m_name, cost.MaterialNeed, free || cost.MaterialOk,
                    Localization.instance.Localize(cost.Material.m_itemData.m_shared.m_name) + $" (you have {cost.MaterialHave})");
            }
            if (cost.TrophyNeed > 0 && cost.Pool != null && cost.Pool.Items.Count > 0 && used < slots.Length)
            {
                // One slot for the whole pool: icon go round the trophies each second, tooltip list them all.
                var shown = cost.Pool.Items[(int)Time.time % cost.Pool.Items.Count];
                ForgeUi.ShowSlot(slots[used++].transform, shown.m_itemData.GetIcon(), ClassName(target) + " trophies",
                    cost.TrophyNeed, free || cost.TrophiesOk, PoolTooltip(player.GetInventory(), cost));
            }
            if (used < slots.Length)
            {
                ForgeUi.ShowSlot(slots[used++].transform, StarIcons.Get(PlainIcon(idol), level, true), item.m_shared.m_name, 1, true,
                    Localization.instance.Localize(item.m_shared.m_name) + (level > 0 ? $" ({Stars(level)})" : "") + ": the idol itself");
            }
        }
        for (; used < slots.Length; used++)
        {
            InventoryGui.HideRequirement(slots[used].transform);
        }

        var station = player.GetCurrentCraftingStation();
        var usable = station == null || station.CheckUsable(player, false);
        gui.m_craftButton.interactable = blocker == null && usable;
        var label = gui.m_craftButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = "Upgrade idol";
        }
        var tooltip = gui.m_craftButton.GetComponent<UITooltip>();
        if (tooltip != null)
        {
            tooltip.m_text = blocker != null ? Localization.instance.Localize(blocker) : usable ? "" : Localization.instance.Localize("$msg_missingstation");
        }
    }

    internal static string Stars(int level) => level == 1 ? "1 star" : level + " stars";

    internal static string ClassName(int targetLevel) =>
        targetLevel == 1 ? "Common" : targetLevel == 2 ? "Elite" : "Boss";

    private static string Description(IdolCatalog.Idol idol, int level, IdolUpgrade.Cost cost)
    {
        Text.Clear();
        Text.Append(idol.Prefab.m_itemData.m_shared.m_description).Append("\n\n");
        Text.Append("Level: <color=orange>").Append(level == 0 ? "no star" : Stars(level)).Append("</color> (")
            .Append(IdolLevels.ChancePercent(level)).Append("% refinement chance)");
        if (level < IdolLevels.Max)
        {
            var target = level + 1;
            Text.Append("\nAfter upgrade: <color=orange>").Append(Stars(target)).Append("</color> (")
                .Append(IdolLevels.ChancePercent(target)).Append("% refinement chance)");
            Text.Append("\n\nCost: ");
            var parts = 0;
            if (cost.MaterialNeed > 0 && cost.Material != null)
            {
                Text.Append(cost.MaterialNeed).Append(' ').Append(cost.Material.m_itemData.m_shared.m_name);
                parts++;
            }
            if (cost.TrophyNeed > 0)
            {
                Text.Append(parts > 0 ? ", " : "").Append(cost.TrophyNeed).Append(' ').Append(ClassName(target).ToLowerInvariant())
                    .Append(cost.TrophyNeed == 1 ? " trophy" : " trophies").Append(" of this tier (any mix)");
                parts++;
            }
            Text.Append(parts > 0 ? " and " : "").Append("this idol.");
            if (cost.Pool != null && cost.Pool.Names.Count > 0 && cost.TrophyNeed > 0)
            {
                Text.Append("\n").Append(ClassName(target)).Append(" trophies: ");
                for (var i = 0; i < cost.Pool.Names.Count; i++)
                {
                    Text.Append(i > 0 ? ", " : "").Append(cost.Pool.Names[i]);
                }
            }
            else if (cost.TrophyNeed > 0)
            {
                Text.Append("\n<color=red>No trophy is set for this step in the settings.</color>");
            }
        }
        return Text.ToString();
    }

    private static string PoolTooltip(Inventory inventory, IdolUpgrade.Cost cost)
    {
        Text.Clear();
        Text.Append("Any mix of ").Append(cost.TrophyNeed).Append(": ");
        for (var i = 0; i < cost.Pool.Names.Count; i++)
        {
            Text.Append(i > 0 ? ", " : "").Append(Localization.instance.Localize(cost.Pool.Names[i]))
                .Append(" (").Append(inventory.CountItems(cost.Pool.Names[i])).Append(')');
        }
        return Text.ToString();
    }

    // Press of the button on an idol row: checks, then the vanilla timer (Forge duration) and sound.
    internal static bool Press(InventoryGui gui, Player player)
    {
        var item = gui.m_selectedRecipe.ItemData;
        var blocker = IdolUpgrade.Blocker(player, item, out _);
        if (blocker != null)
        {
            player.Message(MessageHud.MessageType.Center, blocker);
            return false;
        }
        ForgeUi.StartTimer(gui, player);
        return true;
    }

    // Timer done (DoCrafting): upgrade one idol of the stack.
    internal static void Craft(InventoryGui gui, Player player)
    {
        var item = gui.m_craftUpgradeItem;
        var station = player.GetCurrentCraftingStation();
        var blocker = IdolUpgrade.Blocker(player, item, out var cost);
        if (blocker != null || !IdolUpgrade.Upgrade(player, item))
        {
            player.Message(MessageHud.MessageType.Center, blocker ?? "$msg_missingrequirement");
            gui.UpdateCraftingPanel();
            return;
        }
        player.Message(MessageHud.MessageType.Center,
            Localization.instance.Localize(item.m_shared.m_name) + " upgraded to " + Stars(cost.TargetLevel) + ".", 0, null);
        if (station != null)
        {
            station.m_craftItemDoneEffects.Create(player.transform.position, Quaternion.identity);
        }
        gui.UpdateCraftingPanel();
    }
}
