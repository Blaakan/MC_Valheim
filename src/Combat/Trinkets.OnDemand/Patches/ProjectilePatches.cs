using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod.Patches;

// Me = ranged pay (G6, RangedBonus). Projectile.Setup (public, called through IProjectile by Attack.FireProjectileBurst)
// postfix scale the new instance's own m_adrenaline while AttackPatches hold a context open. Setup calls outside it
// (child projectiles spawned on hit, other shooters, monsters) stay vanilla. Nothing at hit time.
[HarmonyPatch(typeof(Projectile))]
internal static class ProjectilePatches
{
    // Every projectile set up on this game: one bool read while no context.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Projectile.Setup))]
    private static void Setup_Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item)
    {
        if (!RangedBonus.Open)
        {
            return;
        }
        try
        {
            RangedBonus.Apply(__instance, owner, item);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Projectile.Setup postfix", e);
        }
    }
}
