using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Me = cultivator level 4-7 show tier gem everywhere game ask item for its icon (grids, hotbar, drag, messages).
// Recipe panel big icon and recipe rows read m_icons straight, so they stay vanilla. Forge Idol Upgrades also
// postfix GetIcon (idols only, quality 2+): other items, no clash. IconInlineGuard keep GetIcon from being inlined.
[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon))]
internal static class ItemIconPatches
{
    // Hot path (every slot, every frame): quality check first, no allocation after first paint.
    [HarmonyPostfix]
    private static void Postfix(ItemDrop.ItemData __instance, ref Sprite __result)
    {
        if (__instance.m_quality < PlantCatalog.FirstNewTier)
        {
            return;
        }
        try
        {
            if (!CultivatorTiers.IsCultivator(__instance))
            {
                return;
            }
            var icon = TierIcons.CultivatorIcon(__result, __instance.m_quality);
            if (!ReferenceEquals(icon, null))
            {
                __result = icon;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetIcon postfix", e);
        }
    }
}
