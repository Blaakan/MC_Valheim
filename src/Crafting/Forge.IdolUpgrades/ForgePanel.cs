using TMPro;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = the vanilla Upgrade tab at the Forge of Potential (refinement of weapons and armor): show which idol level
// will be spent and the chance, replace the vanilla "may break your item" warning, let a click on the idol pick
// another level you carry.
internal static class ForgePanel
{
    private static int _idolSlot = -1;
    private static string _idolName;
    private static int _amount;

    internal static void Forget()
    {
        _idolSlot = -1;
        _idolName = null;
    }

    internal static void AfterUpdateRecipe(InventoryGui gui, Player player)
    {
        _idolSlot = -1;
        var selected = gui.m_selectedRecipe;
        if (selected.Recipe == null || selected.ItemData == null)
        {
            return;
        }
        var quality = selected.ItemData.m_quality + 1;
        var inventory = player.GetInventory();
        if (!ForgeRefine.TryPlan(inventory, selected.Recipe, quality, out var plan))
        {
            return;
        }
        var level = Mathf.Max(0, plan.Level);
        var percent = IdolLevels.ChancePercent(level);
        var held = IdolChoice.LevelsHeld(inventory, plan.IdolName, plan.Amount);

        gui.m_itemCraftType.gameObject.SetActive(true);
        gui.m_itemCraftType.text = plan.Level < 0
            ? "You carry no idol of this kind."
            : $"{percent}% chance with a {Kind(level)} idol. {FailureText(selected.ItemData.m_quality)}";

        var label = gui.m_craftButton.GetComponentInChildren<TMP_Text>();
        if (label != null && plan.Level >= 0)
        {
            label.text = Localization.instance.Localize("$inventory_upgraderbutton") + $" ({percent}%)";
        }

        // Idol slot = place of our requirement among the ones vanilla show at the Forge (upgrader ones, amount > 0).
        var slot = 0;
        foreach (var req in selected.Recipe.m_resources)
        {
            if (req == null || !req.m_upgraderResource || req.GetAmount(quality) <= 0)
            {
                continue;
            }
            if (req == plan.Idol)
            {
                break;
            }
            slot++;
        }
        var slots = gui.m_recipeRequirementList;
        if (slot >= slots.Length)
        {
            return;
        }
        var plainIcon = plan.Idol.m_resItem.m_itemData.m_shared.m_icons.Length > 0 ? plan.Idol.m_resItem.m_itemData.m_shared.m_icons[0] : null;
        var name = Localization.instance.Localize(plan.IdolName);
        var tooltip = plan.Level < 0
            ? name
            : $"{name} ({Kind(level)}): {percent}% chance."
              + (held > 1 ? " Click to use another level you carry." : "");
        ForgeUi.ShowSlot(slots[slot].transform, StarIcons.Get(plainIcon, level, true), plan.IdolName, plan.Amount,
            plan.Level >= 0 || IdolUpgrade.Free(), tooltip);
        ForgeUi.EnsureClickable(gui);
        _idolSlot = slot;
        _idolName = plan.IdolName;
        _amount = plan.Amount;
    }

    // What a failure does to an item of this level, per the rules in force.
    internal static string FailureText(int itemLevel)
    {
        var rules = ServerRules.Current;
        if (rules.Failure == FailureMode.Destroy)
        {
            return "A failure destroys the item.";
        }
        var lost = Mathf.Min(rules.LevelsLost, itemLevel - 1);
        return lost <= 0 ? "A failure costs only the idol."
            : lost == 1 ? "A failure costs 1 level."
            : $"A failure costs {lost} levels.";
    }

    // "plain", "1-star", "2-star", "3-star".
    internal static string Kind(int level) => level <= 0 ? "plain" : level + "-star";

    internal static void OnSlotClicked(int index)
    {
        var player = Player.m_localPlayer;
        if (player == null || index != _idolSlot || _idolName == null || IdolsTab.Mode || !IdolsTab.AtForge(player))
        {
            return;
        }
        IdolChoice.Cycle(player.GetInventory(), _idolName, _amount);
    }
}
