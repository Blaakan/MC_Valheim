using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.SwimmingDiveMod.Patches;

// Me = dive physics hooks (design 7.2). Every one run for every character the game own: local player reference
// check first, nothing else for creatures.
//   UpdateMotion prefix:    DiveController.Tick (dive state), every physics tick of the local player, before the
//                           swim or walk code of that tick.
//   UpdateSwimming prefix:  deep diver = no sideways swimming (m_moveDir zero). Remember IsOnGround for the postfix
//                           (vanilla call OnSwimming only when not on ground, checked at the start).
//   UpdateSwimming postfix: vertical speed of the diver (after vanilla buoyancy wrote it), floor drain. Near the
//                           surface with nothing to do up or down: vanilla speed left alone (diver ride the waves).
//   Jump prefix:            no jump while diving: a touch of sea floor, rock or hull (m_hitWorldTime) would make the
//                           Jump key a real jump (launch, jump stamina, Jump XP). Skipped original leave m_jumpTimer
//                           as it was, so Weapon Moveset's Jump postfix see "no jump" (it compare m_jumpTimer).
[HarmonyPatch]
internal static class CharacterPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.UpdateMotion))]
    private static void UpdateMotion_Prefix(Character __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            DiveController.Tick((Player)__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.UpdateMotion prefix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.UpdateSwimming))]
    private static void UpdateSwimming_Prefix(Character __instance, out bool __state)
    {
        __state = false;
        if (!DiveState.Diving || !ReferenceEquals(__instance, Player.m_localPlayer)
            || !ReferenceEquals(__instance, DiveState.Owner))
        {
            return;
        }
        try
        {
            __state = __instance.IsOnGround();
            DiveController.LockSideways((Player)__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.UpdateSwimming prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Character), nameof(Character.UpdateSwimming))]
    private static void UpdateSwimming_Postfix(Character __instance, float dt, bool __state)
    {
        if (!DiveState.Diving || !ReferenceEquals(__instance, Player.m_localPlayer)
            || !ReferenceEquals(__instance, DiveState.Owner))
        {
            return;
        }
        try
        {
            DiveController.ApplyVertical((Player)__instance, dt, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.UpdateSwimming postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    private static bool Jump_Prefix(Character __instance)
    {
        if (!DiveState.Diving || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return true;
        }
        try
        {
            return !ReferenceEquals(__instance, DiveState.Owner);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Jump prefix", e);
            return true;
        }
    }
}
