using System;
#if DEBUG
using System.Collections.Generic;
#endif
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod.Patches;

// Me = pair swings (design 2.5, 2.6). Attack runs for every attacker on this game: first check is one reference
// compare (our clone? / local player inside StartAttack?).
//   Start          prefix (High): template move + stamina into the clone (Weapon Moveset's prefix run after,
//                  [HarmonyAfter] + Low on its side, and see the converted clone). Postfix: fired trigger -> record
//                  the swing and its hand pattern.
//   DoMeleeAttack  prefix (First) + postfix + finalizer: hand of this hit event, off-hand weapon swapped in for its
//                  hit, both-hands second call, everything put back (also after an exception).
//   AddHitPoint    postfix, Debug build only: self test record of the objects a melee sweep touched.
[HarmonyPatch(typeof(Attack))]
internal static class AttackPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(nameof(Attack.Start))]
    private static void Start_Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon,
        ref Attack previousAttack)
    {
        if (!Hands.InStartAttack || !ReferenceEquals(character, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            DualSwing.TryConvert(__instance, (Player)character, weapon, ref previousAttack);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.Start prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Attack.Start))]
    private static void Start_Postfix(Attack __instance, bool __result)
    {
        if (!DualSwing.HasConverted)
        {
            return;
        }
        try
        {
            DualSwing.OnStarted(__instance, __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.Start postfix", e);
        }
    }

    // First: other mods' DoMeleeAttack prefixes see the striking weapon already in place.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(nameof(Attack.DoMeleeAttack))]
    private static void DoMeleeAttack_Prefix(Attack __instance)
    {
        if (!ReferenceEquals(__instance, DualSwing.RecordedClone))
        {
            return;
        }
        try
        {
            DualSwing.BeforeHit(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.DoMeleeAttack prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Attack.DoMeleeAttack))]
    private static void DoMeleeAttack_Postfix(Attack __instance)
    {
        if (!ReferenceEquals(__instance, DualSwing.RecordedClone))
        {
            return;
        }
        try
        {
            DualSwing.AfterHit(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.DoMeleeAttack postfix", e);
        }
    }

    // Void finalizer: vanilla exception still go up as without me; me only put the clone back.
    [HarmonyFinalizer]
    [HarmonyPatch(nameof(Attack.DoMeleeAttack))]
    private static void DoMeleeAttack_Finalizer(Attack __instance)
    {
        if (!ReferenceEquals(__instance, DualSwing.RecordedClone))
        {
            return;
        }
        try
        {
            DualSwing.Restore(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.DoMeleeAttack finalizer", e);
        }
    }

#if DEBUG
    // Debug build only: self tests read how many objects the local player's melee sweep touched (vanilla lower the
    // damage per hit when more than one). Records only while DualSwing.Recording is on.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Attack.AddHitPoint))]
    private static void AddHitPoint_Postfix(Attack __instance, List<Attack.HitPoint> list)
    {
        if (!DualSwing.Recording || !ReferenceEquals(__instance.m_character, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            DualSwing.RecordHitPoints(__instance, list);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Attack.AddHitPoint postfix (self test record)", e);
        }
    }
#endif
}
