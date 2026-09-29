using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod.Patches;

// Idol items: starred icon everywhere the game ask the item for its icon (grids, hotbar, drag, messages, radial),
// and level + chance in the tooltip.
[HarmonyPatch]
internal static class ItemDataPatches
{
    // Hot path (every slot, every frame): quality check first, then one dictionary lookup, no allocation.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon))]
    private static void GetIcon_Postfix(ItemDrop.ItemData __instance, ref Sprite __result)
    {
        if (__instance.m_quality < 2)
        {
            return;
        }
        try
        {
            if (IdolCatalog.IsIdol(__instance))
            {
                __result = StarIcons.Get(__result, IdolLevels.Of(__instance));
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetIcon postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    private static void GetTooltip_Postfix(ItemDrop.ItemData item, int qualityLevel, bool crafting, ref string __result)
    {
        try
        {
            if (item == null || __result == null || !IdolCatalog.IsIdol(item))
            {
                return;
            }
            __result = IdolTooltip.Rewrite(item, qualityLevel, crafting, __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetTooltip postfix", e);
        }
    }
}
