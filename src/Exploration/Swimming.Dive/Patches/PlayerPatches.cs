using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod.Patches;

// Me = swim stamina while diving (G1, G4, design 2.5). Vanilla Player.OnSwimming drain stamina only while the swim
// target speed is above 0.1 (horizontal only: vertical dive speed never reach it), and pay Swim XP each second of it.
//   Prefix (local diver only): target at or under 0.1 = diver still or only going up/down. Deep, or really moving up
//     or down -> target set to a small down vector, so vanilla apply its exact drain (skill lerp, gear, status
//     effects, world stamina rate) and keep drowning at 0 stamina. Not deep and not moving (Crouch held but floor or
//     water bottom stop the diver at the surface, Crouch + Jump at the surface, standing in chest-deep water: vertical
//     handed back to vanilla, own speed 0) = target left alone: still at the surface cost nothing (G4). Swim XP stay
//     tied to moving (vanilla): diver not moving up
//     or down = XP timer parked for this call. UnderwaterStaminaMultiplier (server rule) = both drain fields scaled
//     for this call, whole dive (D8).
//   Finalizer: timer and drain fields put back as before the call (also when vanilla or another patch throw).
// Surface swimming (not diving) never touched: staying still there cost nothing, as vanilla.
[HarmonyPatch(typeof(Player), nameof(Player.OnSwimming))]
internal static class PlayerPatches
{
    internal struct SwimSave
    {
        internal bool Timer;
        internal float TimerValue;
        internal bool Drain;
        internal float DrainMin;
        internal float DrainMax;
    }

    // Timer parked far below 0: vanilla add dt, stay under 1, no XP this call. Put back after.
    private const float ParkedTimer = -1000f;

    [HarmonyPrefix]
    private static void OnSwimming_Prefix(Player __instance, ref Vector3 targetVel, out SwimSave __state)
    {
        __state = default;
        if (!DiveState.Diving || !ReferenceEquals(__instance, Player.m_localPlayer)
            || !ReferenceEquals(__instance, DiveState.Owner))
        {
            return;
        }
        try
        {
            if (targetVel.magnitude <= 0.1f && (DiveState.Deep || DiveState.MovingVertically))
            {
                targetVel = Vector3.down * DiveLogic.StillDrainTarget;
                if (!DiveState.MovingVertically)
                {
                    __state.Timer = true;
                    __state.TimerValue = __instance.m_swimSkillImproveTimer;
                    __instance.m_swimSkillImproveTimer = ParkedTimer;
                }
            }
            var multiplier = ServerRules.Current.UnderwaterStaminaMultiplier;
            if (multiplier != 1f)
            {
                __state.Drain = true;
                __state.DrainMin = __instance.m_swimStaminaDrainMinSkill;
                __state.DrainMax = __instance.m_swimStaminaDrainMaxSkill;
                __instance.m_swimStaminaDrainMinSkill *= multiplier;
                __instance.m_swimStaminaDrainMaxSkill *= multiplier;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnSwimming prefix", e);
        }
    }

    // Void finalizer: an exception of vanilla (or another patch) still go up as without me.
    [HarmonyFinalizer]
    private static void OnSwimming_Finalizer(Player __instance, SwimSave __state)
    {
        if (!__state.Timer && !__state.Drain)
        {
            return;
        }
        try
        {
            if (__state.Timer)
            {
                __instance.m_swimSkillImproveTimer = __state.TimerValue;
            }
            if (__state.Drain)
            {
                __instance.m_swimStaminaDrainMinSkill = __state.DrainMin;
                __instance.m_swimStaminaDrainMaxSkill = __state.DrainMax;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnSwimming finalizer", e);
        }
    }
}
