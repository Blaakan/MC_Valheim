#if DEBUG
using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod.Patches;

// Debug build only: self tests read the attacker-side damage of each local player hit (before the victim's
// backstab, stagger x2 and resistances) and which hand struck it. Records only while DualSwing.Recording is on.
[HarmonyPatch(typeof(Character))]
internal static class CharacterPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Character.Damage))]
    private static void Damage_Prefix(Character __instance, HitData hit)
    {
        if (!DualSwing.Recording)
        {
            return;
        }
        try
        {
            DualSwing.RecordDamage(__instance, hit);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Damage prefix (self test record)", e);
        }
    }
}
#endif
