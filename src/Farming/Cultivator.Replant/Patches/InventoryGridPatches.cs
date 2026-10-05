using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Inventory and chest grids, every frame: cultivator level 4-7 hide game quality number. Number sit top right, right
// on the tier gem; gem (and tooltip "Tier:" line) say the level. Also set gem sprite here as backup, in case tiny
// GetIcon got inlined before ItemIconPatches patched it. Number hide only when slot really show a painted gem: gem
// paint failed = number stay, level never lost. Past bloodgold (other mod refine to 8+) = number stay too, gem there
// is bloodgold for every level. Level 1-3 untouched. Normal feature patch (not always on): mod off = game show number
// again next frame. Hotbar and drag icon have no quality number: nothing to do there. Forge Idol Upgrades postfix
// same method for idols (Material type) only: cultivator is Tool, no clash.
[HarmonyPatch]
internal static class InventoryGridPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
    private static void UpdateGui_Postfix(InventoryGrid __instance)
    {
        var inventory = __instance.m_inventory;
        if (inventory == null)
        {
            return;
        }
        try
        {
            var width = inventory.GetWidth();
            foreach (var item in inventory.GetAllItems())
            {
                // Hot path: int compare first, name check only for level 4+ items. List foreach = no allocation.
                var quality = item.m_quality;
                if (quality < PlantCatalog.FirstNewTier || !CultivatorTiers.IsCultivator(item))
                {
                    continue;
                }
                var element = __instance.GetElement(item.m_gridPos.x, item.m_gridPos.y, width);
                if (element == null)
                {
                    continue;
                }
                var sprite = element.m_icon.sprite;
                if (sprite != null && !IconPainter.IsPainted(sprite))
                {
                    // GetIcon postfix did not run on this one: me put gem here.
                    var gem = TierIcons.CultivatorIcon(sprite, quality);
                    if (gem != null)
                    {
                        element.m_icon.sprite = gem;
                        sprite = gem;
                    }
                }
                if (quality <= PlantCatalog.MaxTier && IconPainter.IsPainted(sprite))
                {
                    element.m_quality.enabled = false;
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGrid.UpdateGui postfix", e);
        }
    }
}
