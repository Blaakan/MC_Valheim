using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = item reads (design 2.6, 2.0 heal, 2.3/2.5/2.6 UI).
//   GetDamage(int, float) postfix: NG+ world adds +120 per world level spread over the item's damage types, all blunt
//     on a bash: a tower copy get its damage without that term (decision 21). Tooltip read same method. Normal worlds:
//     one float compare.
//   GetTooltip prefix: heal the copy shown (tower data + bash); postfix: tower lines appended.
[HarmonyPatch]
internal static class ItemDataPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), new[] { typeof(int), typeof(float) })]
    private static void GetDamage_Postfix(ItemDrop.ItemData __instance, int quality, float worldLevel,
        ref HitData.DamageTypes __result)
    {
        if (worldLevel <= 0f)
        {
            return;
        }
        try
        {
            var s = __instance.m_shared;
            if (s == null || s.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                || TowerCatalog.TowerOf(__instance) == null)
            {
                return;
            }
            var damages = s.m_damages;
            if (quality > 1)
            {
                damages.Add(s.m_damagesPerLevel, quality - 1);
            }
            __result = damages;
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetDamage postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    private static void GetTooltip_Prefix(ItemDrop.ItemData item)
    {
        try
        {
            if (item != null && TowerCatalog.SnapshotOf(item) != null)
            {
                TowerData.Heal(item, Player.m_localPlayer);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetTooltip prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    private static void GetTooltip_Postfix(ItemDrop.ItemData item, ref string __result)
    {
        try
        {
            if (item == null || __result == null)
            {
                return;
            }
            __result = TowerTooltip.Append(item, __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetTooltip postfix", e);
        }
    }
}
