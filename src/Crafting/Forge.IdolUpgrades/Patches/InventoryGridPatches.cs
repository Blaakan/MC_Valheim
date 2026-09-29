using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod.Patches;

// Inventory and chest grids, every frame: idols hide the quality number (1..4, the stars on the icon say the level)
// and get the starred icon set here too (safe even where the tiny GetIcon was compiled inline before me patched it).
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
                // Idols are materials: cheap enum test first. IsIdol also fix an idol's max quality copy (see
                // IdolCatalog), before the player can drag it onto another level.
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Material || !IdolCatalog.IsIdol(item))
                {
                    continue;
                }
                var element = __instance.GetElement(item.m_gridPos.x, item.m_gridPos.y, width);
                if (element == null)
                {
                    continue;
                }
                element.m_quality.enabled = false;
                var level = IdolLevels.Of(item);
                if (level > 0 && element.m_icon.sprite != null && !StarIcons.IsStarSprite(element.m_icon.sprite))
                {
                    element.m_icon.sprite = StarIcons.Get(element.m_icon.sprite, level);
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGrid.UpdateGui postfix", e);
        }
    }
}
