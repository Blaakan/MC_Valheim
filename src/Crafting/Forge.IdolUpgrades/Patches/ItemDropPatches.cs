using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod.Patches;

// Idol lying on the ground: vanilla hover print raw quality ("Silver Battle Idol[3]" for a 2-star idol). Me print
// the stars count instead, same words as the tooltip.
[HarmonyPatch]
internal static class ItemDropPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverText))]
    private static void GetHoverText_Postfix(ItemDrop __instance, ref string __result)
    {
        var item = __instance.m_itemData;
        if (item == null || item.m_quality < 2 || __result == null)
        {
            return;
        }
        try
        {
            if (!IdolCatalog.IsIdol(item))
            {
                return;
            }
            var mark = "[" + item.m_quality + "] ";
            var at = __result.IndexOf(mark, StringComparison.Ordinal);
            if (at >= 0)
            {
                __result = __result.Remove(at, mark.Length).Insert(at, " (" + IdolsTab.Stars(IdolLevels.Of(item)) + ") ");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.GetHoverText postfix", e);
        }
    }
}
