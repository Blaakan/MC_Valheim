using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod.Patches;

// Me = edit the per-swing clone before Attack.Start fire its trigger (design 2.2, 2.5). Only while a move is pending
// (set by our StartAttack prefix for the local player): every other Attack.Start (monsters, normal swings) = one
// compare.
// Order: after MC Dual Wielding (its prefix Priority.High convert a pair's clone to the dual template: our family,
// base name, chain levels and aim come from the converted clone) and after Goo's Combat Overhaul (Harmony id
// unverified; unknown id ignored): a clone GCO renamed first match no family and stay GCO's.
// Skip (design 2.4): attack that start inside a roll only because our StartAttack prefix opened vanilla's InDodge
// gate, but no become a roll attack (family Off, not eligible, stamina fallback) = refused here, __result false, like
// vanilla InDodge refusal: press stay buffered, attack start when roll end. Dual Wielding's postfix see false and
// record nothing.
[HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
internal static class AttackPatches
{
    internal const string DualWieldGuid = "MC.Combat.Weapons.DualWield";

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyAfter(DualWieldGuid, Compat.GcoGuid)]
    private static bool Start_Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        if (!MoveTracker.HasPending)
        {
            return true;
        }
        try
        {
            if (MoveTracker.OnAttackStart(__instance, character, weapon))
            {
                __result = false;
                return false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.Start prefix", e);
        }
        return true;
    }
}
