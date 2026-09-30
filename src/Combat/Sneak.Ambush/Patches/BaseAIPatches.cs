using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = smoke rule on creature senses (design 2.9), on each creature's owner. Every sense check reach these two
// statics (instance overloads, CanSenseTarget, FindEnemy, MonsterAI.UpdateTarget, AnimalAI, turrets, NpcTalk,
// PassiveMobs backstab test). Only turn true into false; Creature Morale's CanSenseTarget postfix compose.
// HOT: exit on false result or no cloud loaded (one bool, one int); with clouds a few float ops per cloud.
[HarmonyPatch]
internal static class BaseAIPatches
{
    private static readonly Vector3 HearOffset = Vector3.up;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget),
        new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(bool), typeof(Character) })]
    private static void CanSeeTarget_Postfix(Transform me, Vector3 eyePoint, Character target, ref bool __result)
    {
        if (!__result || SmokeRegistry.Count == 0)
        {
            return;
        }
        try
        {
            if (SmokeRegistry.Hides(me, eyePoint, target, hearing: false, out _))
            {
                __result = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.CanSeeTarget postfix", e);
        }
    }

    // Hearing: observer point = creature position + 1 m. BlocksHearing off = only blind block it.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), new[] { typeof(Transform), typeof(float), typeof(Character) })]
    private static void CanHearTarget_Postfix(Transform me, Character target, ref bool __result)
    {
        if (!__result || SmokeRegistry.Count == 0)
        {
            return;
        }
        try
        {
            if (me != null && SmokeRegistry.Hides(me, me.position + HearOffset, target, hearing: true, out _))
            {
                __result = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.CanHearTarget postfix", e);
        }
    }
}
