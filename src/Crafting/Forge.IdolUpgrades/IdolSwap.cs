using System.Collections.Generic;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me put the idol the target level need (IdolTierRule) into a Forge recipe's idol requirement for the length of one
// vanilla call (requirement check, requirement panel), then put the recipe's own idol back. So vanilla show the right
// idol, count it, light the button, and every other mod's patch on those methods keep working. Recipe data change only
// inside the call (finalizer restore, also after an exception). Nested call = no-op: the outer one already swapped,
// and a second swap would count the offset twice.
internal static class IdolSwap
{
    private static readonly List<KeyValuePair<Piece.Requirement, ItemDrop>> Swapped = new List<KeyValuePair<Piece.Requirement, ItemDrop>>();
    private static bool _active;

    // True = me swapped something: caller must call End. Only for the local player at an upgrader station.
    internal static bool Begin(Player player, Recipe recipe, int targetQuality)
    {
        if (_active || recipe == null || recipe.m_resources == null || player == null || player != Player.m_localPlayer)
        {
            return false;
        }
        var station = player.GetCurrentCraftingStation();
        if (station == null || !station.m_upgrader || IdolUpgrade.IsOurs(recipe))
        {
            return false;
        }
        foreach (var req in recipe.m_resources)
        {
            var idol = IdolTierRule.Effective(req, targetQuality);
            if (idol == null)
            {
                continue;
            }
            Swapped.Add(new KeyValuePair<Piece.Requirement, ItemDrop>(req, req.m_resItem));
            req.m_resItem = idol.Prefab;
        }
        _active = Swapped.Count > 0;
        return _active;
    }

    internal static void End()
    {
        foreach (var pair in Swapped)
        {
            pair.Key.m_resItem = pair.Value;
        }
        Swapped.Clear();
        _active = false;
    }
}
