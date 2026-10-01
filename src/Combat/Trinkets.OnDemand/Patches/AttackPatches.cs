using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod.Patches;

// Me = ranged pay context (G6, RangedBonus). FireProjectileBurst prefix open it for the local player's bow or crossbow
// attack (one call per burst); ProjectilePatches scale each new projectile inside the call; void finalizer close it
// (also after an exception, so a context never leak to other Setup calls).
[HarmonyPatch(typeof(Attack))]
internal static class AttackPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Attack.FireProjectileBurst))]
    private static void FireProjectileBurst_Prefix(Attack __instance, out bool __state)
    {
        __state = false;
        var player = Player.m_localPlayer;
        if ((object)player == null || !ReferenceEquals(__instance.m_character, player))
        {
            return;
        }
        try
        {
            __state = RangedBonus.Begin(__instance, player, ServerRules.Current);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.FireProjectileBurst prefix", e);
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(nameof(Attack.FireProjectileBurst))]
    private static void FireProjectileBurst_Finalizer(bool __state)
    {
        if (!__state)
        {
            return;
        }
        try
        {
            RangedBonus.Close();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.FireProjectileBurst finalizer", e);
        }
    }
}
