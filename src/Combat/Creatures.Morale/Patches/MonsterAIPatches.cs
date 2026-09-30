using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod.Patches;

// Me = the creature's brain on its owner (design 2.5, 2.6, 2.7, 7.3). Only while Active. Bodies catch own errors.
//   UpdateAI prefix (Priority.Low, 20 Hz x every creature on every game): non-owner = IsOwner and out. Owner = gain
//       detection, once-per-second check, and rout or fear frames (vanilla skipped THAT frame only). Nothing when another
//       prefix already skipped vanilla (__runOriginal false, e.g. TruePassiveMobs).
//   HuntPlayer postfix: night hunter standing down = false (not forced alert, no fallback to players). Non-hunters
//       exit at once (vanilla false). Rules changed since the check (NightHuntersCanBeAfraid off, pending) = stand-down
//       over at once, same moment Judge see the new rules.
//   RPC_OnNearProjectileHit prefix: impact within NearMissRange provoke; an afraid creature farther away ignore it.
[HarmonyPatch]
internal static class MonsterAIPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    private static bool UpdateAI_Prefix(MonsterAI __instance, float dt, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return true; // another mod took over this frame; true never undo its skip
        }
        var nview = __instance.m_nview;
        if (nview == null || !nview.IsOwner())
        {
            return true;
        }
        try
        {
            return CreatureCheck.Update(__instance, dt, ref __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("MonsterAI.UpdateAI prefix", e);
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.HuntPlayer))]
    private static void HuntPlayer_Postfix(MonsterAI __instance, ref bool __result)
    {
        if (!__result)
        {
            return;
        }
        try
        {
            if (CreatureState.TryGet(__instance, out var state) && state.HuntStandDown && __instance.m_nview.IsOwner()
                && CreatureCheck.StandDownHolds(state))
            {
                __result = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("MonsterAI.HuntPlayer postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.RPC_OnNearProjectileHit),
        new[] { typeof(long), typeof(Vector3), typeof(float), typeof(ZDOID) })]
    private static bool RPC_OnNearProjectileHit_Prefix(MonsterAI __instance, Vector3 center, ZDOID attackerID)
    {
        try
        {
            return Provocation.OnNearMiss(__instance, center, attackerID);
        }
        catch (Exception e)
        {
            PatchGuard.Report("MonsterAI.RPC_OnNearProjectileHit prefix", e);
            return true;
        }
    }
}
