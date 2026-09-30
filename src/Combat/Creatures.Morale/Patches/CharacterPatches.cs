using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod.Patches;

// Me = a player's hit on a creature, on the creature's owner, before vanilla resolve it (design 2.6, E3):
// sneak-attack rule first (attitude before this hit), then provocation + target. Only while Active.
// Order against Tower Shield Wall's and Sneak Ambush's prefixes on the same method does not matter: they never change
// the damage before resistances or the attacker, and me only lower m_backstabBonus (design 6.1).
// Cheap exits first (not owner, not creature AI, attacker not a player). Body catch own errors.
[HarmonyPatch]
internal static class CharacterPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    private static void RPC_Damage_Prefix(Character __instance, HitData hit)
    {
        if (hit == null || __instance.m_baseAI == null || !hit.HaveAttacker())
        {
            return;
        }
        try
        {
            Provocation.OnHit(__instance, hit);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.RPC_Damage prefix", e);
        }
    }
}
