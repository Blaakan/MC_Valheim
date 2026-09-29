using System.Collections.Generic;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = rules of one idol upgrade (Idols tab): cost for next level, what player hold, room check, and the upgrade
// itself. Counting and removing use vanilla world-level rule (in a New Game+ world, items from a lower world level
// don't count), same as every vanilla recipe.
internal static class IdolUpgrade
{
    // Private recipe per idol prefab: only so the vanilla recipe list and panel can show a row. Never put in
    // ObjectDB (no other station, recipe book or mod see them). Empty requirement list: vanilla checks pass, me do
    // the real checks.
    private static readonly Dictionary<ItemDrop, Recipe> Recipes = new Dictionary<ItemDrop, Recipe>();
    private static readonly HashSet<Recipe> Ours = new HashSet<Recipe>();

    internal struct Cost
    {
        internal int TargetLevel;
        internal ItemDrop Material;
        internal int MaterialNeed;
        internal int MaterialHave;
        internal IdolCatalog.Pool Pool;
        internal int TrophyNeed;
        internal int TrophyHave;

        internal bool MaterialOk => MaterialNeed <= 0 || (Material != null && MaterialHave >= MaterialNeed);
        internal bool TrophiesOk => TrophyNeed <= 0 || (Pool != null && Pool.Names.Count > 0 && TrophyHave >= TrophyNeed);
        internal bool Enough => MaterialOk && TrophiesOk;
    }

    internal static Recipe RecipeFor(IdolCatalog.Idol idol)
    {
        if (!Recipes.TryGetValue(idol.Prefab, out var recipe) || recipe == null)
        {
            recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "MC_IdolUpgrade_" + idol.PrefabName;
            recipe.m_item = idol.Prefab;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_resources = new Piece.Requirement[0];
            recipe.hideFlags = HideFlags.HideAndDontSave;
            Recipes[idol.Prefab] = recipe;
            Ours.Add(recipe);
        }
        return recipe;
    }

    internal static bool IsOurs(Recipe recipe) => recipe != null && Ours.Contains(recipe);

    internal static Cost CostFor(Inventory inventory, IdolCatalog.Idol idol, int targetLevel)
    {
        var cost = new Cost { TargetLevel = targetLevel };
        var tier = IdolCatalog.TierOf(idol);
        if (tier == null || targetLevel < 1 || targetLevel > IdolLevels.Max)
        {
            return cost;
        }
        cost.Material = tier.Material;
        cost.MaterialNeed = ServerRules.Current.Material[targetLevel];
        cost.MaterialHave = tier.Material != null ? inventory.CountItems(tier.Material.m_itemData.m_shared.m_name) : 0;
        cost.Pool = tier.Pools[targetLevel];
        cost.TrophyNeed = ServerRules.Current.Trophies[targetLevel];
        cost.TrophyHave = 0;
        if (cost.Pool != null)
        {
            foreach (var name in cost.Pool.Names)
            {
                cost.TrophyHave += inventory.CountItems(name);
            }
        }
        return cost;
    }

    // Room for the upgraded idol: stack of 1 = upgrade in place; else it join a stack of the next level, or need a
    // free slot.
    internal static bool HasRoom(Inventory inventory, ItemDrop.ItemData stack)
    {
        if (stack.m_stack <= 1)
        {
            return true;
        }
        return inventory.FindFreeStackItem(stack.m_shared.m_name, stack.m_quality + 1, Game.m_worldLevel) != null
               || inventory.HaveEmptySlot();
    }

    // Why this stack cannot be upgraded now (null = it can). Player-facing English.
    internal static string Blocker(Player player, ItemDrop.ItemData stack, out Cost cost)
    {
        cost = default;
        var inventory = player.GetInventory();
        var idol = IdolCatalog.IdolOf(stack);
        if (idol == null || !inventory.ContainsItem(stack))
        {
            return "$msg_missingrequirement";
        }
        var level = IdolLevels.Of(stack);
        if (level >= IdolLevels.Max)
        {
            return "This idol already has 3 stars.";
        }
        cost = CostFor(inventory, idol, level + 1);
        if (!Free() && !cost.Enough)
        {
            return "$msg_missingrequirement";
        }
        if (!HasRoom(inventory, stack))
        {
            return "$inventory_needspace";
        }
        return null;
    }

    // Vanilla "no cost" cheat or world modifier: crafting take nothing.
    internal static bool Free()
    {
        var player = Player.m_localPlayer;
        return (player != null && player.NoCostCheat())
               || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
    }

    // Do it: take metal and trophies, turn one idol of the stack into one idol of the next level (copy of the stack
    // item: keeps its data). Like every vanilla upgrade, the result get the current world level (New Game+), so an
    // idol found in a lower world level becomes usable here, and the cheated mark when a cheated item went in.
    internal static bool Upgrade(Player player, ItemDrop.ItemData stack)
    {
        if (Blocker(player, stack, out var cost) != null)
        {
            return false;
        }
        var inventory = player.GetInventory();
        var cheated = player.NoCostCheat() || stack.m_cheated
                      || (cost.Material != null && inventory.ItemCheated(cost.Material.m_itemData.m_shared.m_name));
        if (!cheated && cost.Pool != null)
        {
            foreach (var name in cost.Pool.Names)
            {
                cheated |= inventory.ItemCheated(name);
            }
        }
        // Vanilla also mark anything made at a cheated station (built with no-cost).
        var station = player.GetCurrentCraftingStation();
        var stationView = station != null ? station.GetComponent<ZNetView>() : null;
        cheated |= stationView != null && stationView.GetZDO() != null && stationView.GetZDO().GetBool(ZDOVars.s_cheated);
        cheated &= !PlayerProfile.s_bypassCheatChecks;
        if (!Free())
        {
            if (cost.MaterialNeed > 0)
            {
                inventory.RemoveItem(cost.Material.m_itemData.m_shared.m_name, cost.MaterialNeed);
            }
            TakeTrophies(inventory, cost.Pool, cost.TrophyNeed);
        }

        var target = IdolLevels.Quality(cost.TargetLevel);
        var worldLevel = Game.m_worldLevel;
        var joinStack = inventory.FindFreeStackItem(stack.m_shared.m_name, target, worldLevel);
        if (stack.m_stack <= 1 && joinStack == null)
        {
            // Last idol of the stack: upgrade in place, keep its slot.
            stack.m_quality = target;
            stack.m_worldLevel = worldLevel;
            stack.m_cheated |= cheated;
            inventory.Changed();
            return true;
        }
        var upgraded = stack.Clone();
        upgraded.m_stack = 1;
        upgraded.m_quality = target;
        upgraded.m_worldLevel = worldLevel;
        upgraded.m_cheated |= cheated;
        inventory.RemoveItem(stack, 1);
        if (!inventory.AddItem(upgraded))
        {
            // Should not happen (room checked), but never lose the idol: give back the old one.
            if (inventory.ContainsItem(stack))
            {
                stack.m_stack++;
                inventory.Changed();
            }
            else
            {
                stack.m_stack = 1;
                inventory.AddItem(stack);
            }
            return false;
        }
        return true;
    }

    // Any mix of the pool's trophies. Me spend the kind you hold most of first, so rare ones stay.
    private static void TakeTrophies(Inventory inventory, IdolCatalog.Pool pool, int need)
    {
        if (pool == null || need <= 0)
        {
            return;
        }
        var counts = new int[pool.Names.Count];
        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] = inventory.CountItems(pool.Names[i]);
        }
        // One at a time from the biggest pile (need is small: 5 by default).
        var taken = new int[counts.Length];
        for (; need > 0; need--)
        {
            var best = -1;
            for (var i = 0; i < counts.Length; i++)
            {
                if (counts[i] - taken[i] > 0 && (best < 0 || counts[i] - taken[i] > counts[best] - taken[best]))
                {
                    best = i;
                }
            }
            if (best < 0)
            {
                break;
            }
            taken[best]++;
        }
        for (var i = 0; i < taken.Length; i++)
        {
            if (taken[i] > 0)
            {
                inventory.RemoveItem(pool.Names[i], taken[i]);
            }
        }
    }
}
