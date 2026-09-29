using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod.Patches;

// IsPiece = the gate hook. Vanilla AutoPickup skip "pieces" after the m_autoPickup check and before RequestOwn, so a
// drop me call piece is skipped and never claimed. Only while scope open (inside local AutoPickup): everywhere else
// (despawn timer, hover, eating food on tables) the answer stay vanilla. Outside scope: one static bool read.
// GetHoverText postfix = grey line under the name when filter will skip the item.
// Awake postfix = harvest grace decided once when drop is born; OnDestroy postfix = forget it.
[HarmonyPatch]
internal static class ItemDropPatches
{
    // Every drop born (any mode, so mode change right after harvest still work). No harvest spot in last 3 s = one
    // float compare. Owned harvest spawn right inside the Interact call; remote-owned drop come from ZNetScene at
    // the ZDO position, inside the 3 s window.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Awake))]
    private static void Awake_Postfix(ItemDrop __instance)
    {
        try
        {
            if (!HarvestGrace.CoversSpawn(__instance.transform.position, Time.time))
            {
                return;
            }
            HarvestGrace.Tag(__instance);
            if (FilterState.Mode != FilterMode.Everything && FilterState.ListBlocks(__instance))
            {
                Log.Debug($"Harvest grace: {Utils.GetPrefabName(__instance.gameObject)} dropped by your own harvest "
                          + "will be picked up despite the filter.");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Awake_Postfix), e);
        }
    }

    // Tag set stay as big as live tagged drops. Reloaded drop = new instance id = ordinary drop.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnDestroy))]
    private static void OnDestroy_Postfix(ItemDrop __instance)
    {
        try
        {
            HarvestGrace.Untag(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnDestroy_Postfix), e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.IsPiece))]
    private static void IsPiece_Postfix(ItemDrop __instance, ref bool __result)
    {
        if (!AutoPickupScope.Active || __result)
        {
            return;
        }
        try
        {
            if (FilterState.Blocks(__instance))
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            // Leave vanilla answer.
            PatchGuard.Report(nameof(IsPiece_Postfix), e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverText))]
    private static void GetHoverText_Postfix(ItemDrop __instance, ref string __result)
    {
        if (!Player.m_enableAutoPickup || FilterState.Mode == FilterMode.Everything || __result == null)
        {
            return;
        }
        try
        {
            if (!Plugin.ShowInHoverText.Value || !FilterState.EnsureLocal() || FilterState.Mode == FilterMode.Everything
                || __instance.IsPiece() || !FilterState.Blocks(__instance))
            {
                return;
            }
            __result += FilterState.Mode == FilterMode.SkipIgnored
                ? "\n<color=#A0A0A0>Auto pickup skips this (ignored)</color>"
                : "\n<color=#A0A0A0>Auto pickup skips this (not selected)</color>";
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(GetHoverText_Postfix), e);
        }
    }
}
