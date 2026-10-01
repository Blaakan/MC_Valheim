using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod.Patches;

// Me = the hit signals of the combat state (G2). Both prefixes change nothing; they only stamp a time.
//   Character.Damage:     runs on the attacker's game for every melee, area, projectile and AOE hit, before the hit
//                         goes to the victim's owner. Local player hit a real foe = exchange.
//   Character.RPC_Damage: runs on the victim's owner. Local player hit by a real foe (blocked or not) = exchange.
//                         Prefix = before vanilla's early exits (debug fly, not owner).
[HarmonyPatch(typeof(Character))]
internal static class CharacterPatches
{
    // Every hit on this game: attacker id compare first (a struct compare), then the foe test.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Character.Damage))]
    private static void Damage_Prefix(Character __instance, HitData hit)
    {
        var local = Player.m_localPlayer;
        if (hit == null || (object)local == null || hit.m_attacker.IsNone())
        {
            return;
        }
        try
        {
            if (hit.m_attacker == local.GetZDOID() && CombatState.IsRealFoe(local, __instance))
            {
                CombatState.MarkExchange();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Damage prefix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Character.RPC_Damage))]
    private static void RPC_Damage_Prefix(Character __instance, HitData hit)
    {
        if (hit == null || !ReferenceEquals(__instance, Player.m_localPlayer) || hit.m_attacker.IsNone())
        {
            return; // not me, or fall / drowning / environment
        }
        try
        {
            if (CombatState.IsRealFoe(Player.m_localPlayer, hit.GetAttacker()))
            {
                CombatState.MarkExchange();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.RPC_Damage prefix", e);
        }
    }
}
