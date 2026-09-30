using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod.Patches;

// Me = jump token (design 2.3). Character.Jump from input (force false) on the local player: m_jumpTimer above 0
// before and exactly 0 after = ForceJump ran inside this call = player really jumped. Not counted: grappling pull
// (no ForceJump), catapult, AoE launch, grappling point (call ForceJump direct), step-up (OnAutoJump), animation
// event jumps (force true). Tired jump (no stamina) still a jump. Monsters jump too: one compare.
[HarmonyPatch(typeof(Character), nameof(Character.Jump))]
internal static class CharacterPatches
{
    [HarmonyPrefix]
    private static void Jump_Prefix(Character __instance, bool force, out float __state)
    {
        __state = -1f;
        if (force || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            __state = __instance.m_jumpTimer;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Jump prefix", e);
        }
    }

    [HarmonyPostfix]
    private static void Jump_Postfix(Character __instance, float __state)
    {
        if (__state <= 0f)
        {
            return;
        }
        try
        {
            if (__instance.m_jumpTimer == 0f)
            {
                MoveTracker.OnJumped(__instance as Player);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Jump postfix", e);
        }
    }
}
