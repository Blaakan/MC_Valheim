using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewSpyglassMod.Patches;

// ALWAYS ON. The spyglass is a Tool with no build pieces: for the game a click with it attacks with the fists
// (Humanoid.GetCurrentWeapon give the unarmed weapon for a tool). Me skip the attack input while the right hand hold
// a spyglass, also while the feature is off (then clicking with it does nothing, like an item of a turned-off mod
// should). Feature on: the SetControls prefix already took the clicks, this changes nothing. Bow draw and weapon
// loading live in the same method: a spyglass hand has neither. Local player only (owner, method is owner-only).
[AlwaysOnPatch]
[HarmonyPatch(typeof(Player), nameof(Player.PlayerAttackInput))]
internal static class PlayerAttackGuardPatches
{
    [HarmonyPrefix]
    private static bool Prefix(Player __instance)
    {
        try
        {
            return !ReferenceEquals(__instance, Player.m_localPlayer)
                   || !SpyglassContent.IsSpyglass(__instance.GetRightItem());
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.PlayerAttackInput prefix", e);
            return true;
        }
    }
}
