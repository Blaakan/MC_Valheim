using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// Vanilla hide every controller hint while one of its side dialogs is open (KeyHints.UpdateHints: all off, return).
// Me do the same while the Encyclopedia is open, as a prefix that skip the original: a postfix would let vanilla switch
// the inventory hints on first and me off after, every frame (OnEnable/OnDisable churn under them). Only turn off
// what is on. Idle cost: one bool.
[HarmonyPatch]
internal static class KeyHintsPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(KeyHints), nameof(KeyHints.UpdateHints))]
    private static bool UpdateHints_Prefix(KeyHints __instance)
    {
        if (!CompendiumWindow.IsOpen)
        {
            return true;
        }
        try
        {
            Off(__instance.m_buildHints);
            Off(__instance.m_combatHints);
            Off(__instance.m_inventoryHints);
            Off(__instance.m_inventoryWithContainerHints);
            Off(__instance.m_fishingHints);
            Off(__instance.m_barberHints);
            Off(__instance.m_radialHints);
        }
        catch (Exception e)
        {
            PatchGuard.Report("KeyHints.UpdateHints prefix", e);
            return true;
        }
        return false;
    }

    private static void Off(GameObject go)
    {
        if (go != null && go.activeSelf)
        {
            go.SetActive(false);
        }
    }
}
