using System;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;

namespace MC.Building.LightsSwitchableMod.Patches;

// Fires (Fireplace). Only pieces in the Lights list change; campfire, hearth and others stay vanilla (first check
// in every patch). Light = E switch on/off, no fuel item, no burn, own hover text, no fuel radial menu.
[HarmonyPatch]
internal static class FireplacePatches
{
    // New light in world: instance flag (see LiveLights).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
    private static void Awake_Postfix(Fireplace __instance)
    {
        try
        {
            LiveLights.Apply(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.Awake postfix", e);
        }
    }

    // E on a light: switch, never add fuel. Hold E = nothing (vanilla repeat every 0.2 s would flicker it).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
    private static bool Interact_Prefix(Fireplace __instance, bool hold, ref bool __result)
    {
        try
        {
            if (!LightRules.IsManaged(__instance))
            {
                return true;
            }
            __result = LightSwitch.Toggle(__instance, hold);
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.Interact prefix", e);
            return true;
        }
    }

    // Owner tick every 2 s: no fuel burn for lights. Me still run vanilla UpdateState (flame, wet look).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateFireplace))]
    private static bool UpdateFireplace_Prefix(Fireplace __instance)
    {
        try
        {
            if (!LightRules.IsManaged(__instance))
            {
                return true;
            }
            if (!__instance.m_nview.IsValid())
            {
                return false;
            }
            // Instance flag again (cheap): a light another mod stopped making infinite become ours only now.
            LiveLights.Apply(__instance);
            LightSwitch.OwnerTick(__instance);
            __instance.UpdateState();
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.UpdateFireplace prefix", e);
            return true;
        }
    }

    // Hover: "Standing iron torch ( On )  [E] Turn off".
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    private static void GetHoverText_Postfix(Fireplace __instance, ref string __result)
    {
        try
        {
            if (LightRules.IsManaged(__instance) && __instance.m_nview.IsValid())
            {
                __result = LightSwitch.HoverText(__instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.GetHoverText postfix", e);
        }
    }

    // Hotbar item on a light: no fuel (vanilla UseItem also add fireworks to fires; lights have none).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UseItem))]
    private static bool UseItem_Prefix(Fireplace __instance, ref bool __result)
    {
        try
        {
            if (!LightRules.IsManaged(__instance))
            {
                return true;
            }
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.UseItem prefix", e);
            return true;
        }
    }

    // 1.0 radial "use item" menu: no fuel entry on a light.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.TryGetItems))]
    private static bool TryGetItems_Prefix(Fireplace __instance, ref List<string> items, ref bool __result)
    {
        try
        {
            if (!LightRules.IsManaged(__instance))
            {
                return true;
            }
            items = new List<string>();
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.TryGetItems prefix", e);
            return true;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.CanUseItems))]
    private static bool CanUseItems_Prefix(Fireplace __instance, ref bool __result)
    {
        try
        {
            if (!LightRules.IsManaged(__instance))
            {
                return true;
            }
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.CanUseItems prefix", e);
            return true;
        }
    }
}
