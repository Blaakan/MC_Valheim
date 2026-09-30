using MC.Shared;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = the refinement roll at the Forge of Potential (vanilla Upgrade tab), replacing the vanilla upgrader branch of
// InventoryGui.DoCrafting. Vanilla: 65% success, else item destroyed (every idol prefab has break chance 1 in
// 1.0.16), and the idol spent in both cases. Me: chance from the level of the idol actually spent; success = item
// one level up; failure = item loses LevelsLost levels (default 1, never below 1) or is destroyed like vanilla
// (setting OnFailure). Idol always spent.
internal static class ForgeRefine
{
#if DEBUG
    // Self test force the roll (0..1) instead of Unity random. Null = normal.
    internal static float? TestRoll;
#endif

    internal struct Plan
    {
        internal Piece.Requirement Idol;   // the idol requirement that set the chance
        internal ItemDrop IdolPrefab;      // idol to spend: the recipe's own, or a higher tier (IdolTierRule)
        internal string IdolName;
        internal int Amount;
        internal int Level;                // -1 = none held (only possible with no-cost)
        internal float Chance;
        internal int BaseTier;             // tier of the recipe's own idol, -1 = modded idol
        internal int Tier;                 // tier of IdolPrefab (= BaseTier when not raised)
    }

    // Idol requirement of a Forge recipe at target quality: first upgrader requirement with an amount (vanilla take
    // the first one whatever its amount). Null = not an idol recipe. The idol is the one the item level need.
    internal static bool TryPlan(Inventory inventory, Recipe recipe, int targetQuality, out Plan plan)
    {
        plan = default;
        if (recipe == null || recipe.m_resources == null)
        {
            return false;
        }
        foreach (var req in recipe.m_resources)
        {
            if (req == null || !req.m_upgraderResource || req.m_resItem == null)
            {
                continue;
            }
            var amount = req.GetAmount(targetQuality);
            if (amount <= 0)
            {
                continue;
            }
            plan.Idol = req;
            var own = IdolCatalog.IdolOfPrefab(req.m_resItem);
            var needed = IdolTierRule.Effective(req, targetQuality);
            plan.IdolPrefab = needed != null ? needed.Prefab : req.m_resItem;
            plan.BaseTier = own != null ? own.Tier : -1;
            plan.Tier = needed != null ? needed.Tier : plan.BaseTier;
            plan.IdolName = plan.IdolPrefab.m_itemData.m_shared.m_name;
            plan.Amount = amount;
            plan.Level = IdolChoice.LevelToSpend(inventory, plan.IdolName, amount);
            plan.Chance = IdolLevels.Chance(Mathf.Max(0, plan.Level));
            return true;
        }
        return false;
    }

    // Whole refinement. Called from DoCrafting prefix when the timer end. Same checks, stats, skill and effects
    // as vanilla, so the Forge feel the same. True = me handled it (skip vanilla).
    internal static bool Run(InventoryGui gui, Player player)
    {
        var recipe = gui.m_craftRecipe;
        var item = gui.m_craftUpgradeItem;
        var station = player.GetCurrentCraftingStation();
        var inventory = player.GetInventory();
        var quality = item.m_quality + 1;
        if (!TryPlan(inventory, recipe, quality, out var plan))
        {
            return false; // no idol requirement: leave it to vanilla
        }
        var free = IdolUpgrade.Free();
        if ((!free && plan.Level < 0) || !inventory.ContainsItem(item))
        {
            Log.Info($"Refinement of {item.m_shared.m_name} stopped: {(plan.Level < 0 ? "no idol left" : "item no longer in inventory")}.");
            return true;
        }
        var dlc = recipe.m_item.m_itemData.m_shared.m_dlc;
        if (dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(dlc))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_dlcrequired");
            return true;
        }

        // Vanilla: stats count the cheated inputs; the new item is marked cheated when inputs or the station are.
        // Inputs = the idol the level need (swap), not the recipe's own.
        var swapped = IdolSwap.Begin(player, recipe, quality);
        bool cheatedInputs;
        try
        {
            cheatedInputs = inventory.ItemCheated(recipe.m_resources);
        }
        finally
        {
            if (swapped)
            {
                IdolSwap.End();
            }
        }
        var cheated = cheatedInputs || player.NoCostCheat();
        var stationView = station != null ? station.GetComponent<ZNetView>() : null;
        var stationCheated = stationView != null && stationView.GetZDO() != null && stationView.GetZDO().GetBool(ZDOVars.s_cheated);
        var roll = Random.Range(0f, 1f);
#if DEBUG
        if (TestRoll.HasValue)
        {
            roll = TestRoll.Value;
        }
#endif
        var success = plan.Chance >= roll;
        var name = item.m_shared.m_name;
        var rules = ServerRules.Current;
        var markCheated = (cheated || stationCheated) && !PlayerProfile.s_bypassCheatChecks;
        var level = item.m_quality;
        var lower = Mathf.Max(1, level - rules.LevelsLost);
        var outcome = success ? "success"
            : rules.Failure == FailureMode.Destroy ? "failed, item destroyed"
            : lower < level ? $"failed, down to level {lower}" : "failed, item stays at level 1";
        Log.Info($"Refinement of {(item.m_dropPrefab != null ? item.m_dropPrefab.name : name)} to level {quality} with {plan.IdolPrefab.name} "
                 + $"at level {plan.Level}{(plan.Tier != plan.BaseTier ? $" (own idol tier {plan.BaseTier}, raised by item level)" : "")} "
                 + $"({plan.Chance * 100f:0}% chance, roll {roll * 100f:0.0}): {outcome}.");
        if (success)
        {
            Remake(player, item, quality, markCheated);
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize("$msg_upgrader_success", name, quality.ToString()), 0, null);
        }
        else if (rules.Failure == FailureMode.Destroy)
        {
            Break(player, recipe, item, plan, level, markCheated);
        }
        else if (lower < level)
        {
            Remake(player, item, lower, markCheated);
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize("$msg_upgrader_fail", name, lower.ToString()), 0, null);
        }
        else
        {
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize(name) + " refinement failed. It stays at level 1.");
        }

        if (!free)
        {
            // Exact level me rolled with. Other upgrader requirements (mods) go the vanilla way.
            inventory.RemoveItem(plan.IdolName, plan.Amount, IdolLevels.Quality(plan.Level));
            foreach (var req in recipe.m_resources)
            {
                if (req != null && req != plan.Idol && req.m_upgraderResource && req.m_resItem != null)
                {
                    var amount = req.GetAmount(quality);
                    if (amount > 0)
                    {
                        // Same idol the checks asked for (IdolSwap raise every idol requirement, not only ours).
                        var raised = IdolTierRule.Effective(req, quality);
                        var drop = raised != null ? raised.Prefab : req.m_resItem;
                        inventory.RemoveItem(drop.m_itemData.m_shared.m_name, amount);
                    }
                }
            }
        }
        gui.UpdateCraftingPanel();
        if (recipe.m_craftingStation != null && recipe.m_craftingStation.m_craftingSkill != Skills.SkillType.None)
        {
            player.RaiseSkill(recipe.m_craftingStation.m_craftingSkill, 1f);
        }
        if (station != null)
        {
            (success ? station.m_craftItemDoneEffects : station.m_craftItemDoneFailEffects).Create(player.transform.position, Quaternion.identity);
        }
        var profile = Game.instance.GetPlayerProfile();
        profile.IncrementStat(PlayerStatType.CraftsOrUpgrades, 1f, cheated);
        profile.IncrementStat(PlayerStatType.Upgrades, 1f, cheated);
        Gogan.LogEvent("Game", "Crafted", name, quality);
        return true;
    }

    // Vanilla re-make the item at the new level (Inventory.AddItem): taken off, full durability, current world level,
    // the refiner as crafter, cheated mark. Me do the same on the same item object, so data other mods keep on it
    // (EpicLoot magic, MC Crossbow mark) survive: our takeover skip the vanilla code where they carry it over.
    private static void Remake(Player player, ItemDrop.ItemData item, int quality, bool cheated)
    {
        player.UnequipItem(item);
        item.m_quality = quality;
        item.m_durability = item.GetMaxDurability();
        item.m_worldLevel = Game.m_worldLevel;
        item.m_crafterID = player.GetPlayerID();
        item.m_crafterName = player.GetPlayerName();
        if (cheated)
        {
            item.m_cheated = true;
        }
        player.GetInventory().Changed();
    }

    // Vanilla break (InventoryGui.DoCrafting, upgrader branch): item gone, part of each recoverable material back,
    // ceil((cost at level 1 + cost at the item's level) x the idol's return share).
    private static void Break(Player player, Recipe recipe, ItemDrop.ItemData item, Plan plan, int level, bool cheated)
    {
        var inventory = player.GetInventory();
        var name = item.m_shared.m_name;
        player.UnequipItem(item);
        inventory.RemoveItem(item);
        player.Message(MessageHud.MessageType.Center,
            Localization.instance.Localize("$msg_upgrader_broke", name, level.ToString()), 0, null);
        var share = plan.IdolPrefab.m_itemData.m_shared.m_breakReturnIngreientsAmount;
        if (share <= 0f)
        {
            return;
        }
        foreach (var req in recipe.m_resources)
        {
            if (req == null || !req.m_recover || req.m_resItem == null)
            {
                continue;
            }
            var amount = Mathf.CeilToInt((req.GetAmount(1) + req.GetAmount(level)) * share);
            if (amount > 0)
            {
                inventory.AddItem(req.m_resItem.name, amount, req.m_resItem.m_itemData.m_quality, req.m_resItem.m_itemData.m_variant,
                    player.GetPlayerID(), player.GetPlayerName(), new Vector2i(-1, -1), cheated);
            }
        }
    }
}
