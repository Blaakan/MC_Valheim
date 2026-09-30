using System;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = one hit per bash and one heavy stagger per bash (design 2.6, 2.7, decisions 28, 35). Only OUR running bash
// clone: every other attack (vanilla, other mods, Dual Wielding pairs) run untouched; other mods' OnAttackTrigger
// postfixes (Crossbow Stays Loaded) still run.
//   OnAttackTrigger prefix: a clip with two Hit events (Custom, e.g. dual axes) would hit twice for one stamina cost:
//     me skip the second and later Hit events. The count also feed watchdog (B) (BashWatch). The first one open the
//     BashTarget scope. One reference compare per Hit event.
//   OnAttackTrigger finalizer: scope closed, also when vanilla or another patch throw.
//   AddHitPoint postfix: vanilla's hit list of our clone's sweep to BashTarget. One reference compare per ray hit.
[HarmonyPatch]
internal static class AttackPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    private static bool OnAttackTrigger_Prefix(Attack __instance, out bool __state)
    {
        __state = false;
        if (!ReferenceEquals(__instance, BashWatch.Running))
        {
            return true;
        }
        try
        {
            if (!BashWatch.OnHitEvent())
            {
                return false;
            }
            BashTarget.Begin(__instance);
            __state = true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.OnAttackTrigger prefix", e);
        }
        return true;
    }

    // Void finalizer: an exception of vanilla (or another patch) still go up as without me.
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    private static void OnAttackTrigger_Finalizer(bool __state)
    {
        if (!__state)
        {
            return;
        }
        try
        {
            BashTarget.End();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.OnAttackTrigger finalizer", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Attack), nameof(Attack.AddHitPoint))]
    private static void AddHitPoint_Postfix(Attack __instance, List<Attack.HitPoint> list)
    {
        if (!ReferenceEquals(__instance, BashTarget.Open))
        {
            return;
        }
        try
        {
            BashTarget.OnHitList(list);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.AddHitPoint postfix", e);
        }
    }
}
