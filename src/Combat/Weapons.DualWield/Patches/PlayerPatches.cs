using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod.Patches;

// Me = local player's requests and per-frame upkeep (design 2.2, 2.3, 2.4).
//   ToggleEquipped      prefix + postfix (+ finalizer): main-hand key intent per item, toggle window, "player asked"
//   UpdateActionQueue   prefix + finalizer: queue window around the queued equip (main-hand key intent, end of a
//                       queued swap), queued swap dropped when its pair is gone (50 Hz, two bool writes, one null
//                       check)
//   Update              postfix: rules / ObjectDB / player change, eat return, lone off hand, queued swap upkeep,
//                       swap key
//   GetRandomSkillFactor postfix, Debug build only: self test record of the roll of each hit
[HarmonyPatch(typeof(Player))]
internal static class PlayerPatches
{
    // __state = main-hand key window opened for this item.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.ToggleEquipped))]
    private static void ToggleEquipped_Prefix(Player __instance, ItemDrop.ItemData item, out bool __state)
    {
        __state = false;
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            __state = Hands.BeginToggle(__instance, item);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.ToggleEquipped prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.ToggleEquipped))]
    private static void ToggleEquipped_Postfix(Player __instance, ItemDrop.ItemData item, bool __state)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Hands.EndToggle(__instance, item, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.ToggleEquipped postfix", e);
        }
    }

    // Vanilla threw = postfix skipped: window closed anyway (no stray intent for later equips).
    [HarmonyFinalizer]
    [HarmonyPatch(nameof(Player.ToggleEquipped))]
    private static void ToggleEquipped_Finalizer()
    {
        try
        {
            Hands.CloseToggle();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.ToggleEquipped finalizer", e);
        }
    }

    // Queued swap whose pair is gone: out of the queue here, before vanilla can end it and fire the draw trigger (pair
    // lost inside a physics step never wait for Player.Update).
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.UpdateActionQueue))]
    private static void UpdateActionQueue_Prefix(Player __instance)
    {
        try
        {
            if (ReferenceEquals(__instance, Player.m_localPlayer))
            {
                Hands.QueueWindow = true;
                if (Hands.PendingSwap != null)
                {
                    Hands.KeepSwap(__instance);
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateActionQueue prefix", e);
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(nameof(Player.UpdateActionQueue))]
    private static void UpdateActionQueue_Finalizer()
    {
        try
        {
            Hands.QueueWindow = false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateActionQueue finalizer", e);
        }
    }

    // Every frame per Player object: one reference compare, then (local player) three reference compares, three
    // slot checks, one null check (queued swap) and one key check. No alloc.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.Update))]
    private static void Update_Postfix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Hands.Tick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Update postfix", e);
        }
    }

#if DEBUG
    // Debug build only: self tests read the random skill factor vanilla rolled for each local player hit (DoMeleeAttack
    // roll one per object, then build its HitData). Records only while DualSwing.Recording is on.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.GetRandomSkillFactor))]
    private static void GetRandomSkillFactor_Postfix(Player __instance, float __result)
    {
        if (!DualSwing.Recording || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            DualSwing.RecordSkillFactor(__result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.GetRandomSkillFactor postfix (self test record)", e);
        }
    }
#endif
}
