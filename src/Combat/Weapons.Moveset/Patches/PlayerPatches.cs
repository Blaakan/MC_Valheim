using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod.Patches;

// Me = per-tick heart of the moves (design 2.4, 2.7, 2.9). Player.UpdateDodge run every physics tick on the local
// player (owner, alive), after PlayerAttackInput of the same tick, and refresh m_inDodge: false -> true = roll start,
// true -> false = roll end (or roll cut by our roll attack). Postfix run even when a dash mod's prefix skip the
// vanilla body (dash mods set m_inDodge by hand).
// MoveTracker.Tick: player change, roll edges, jump token end on landing, move watch, animation state learning.
// No allocation.
[HarmonyPatch(typeof(Player), nameof(Player.UpdateDodge))]
internal static class PlayerPatches
{
    [HarmonyPostfix]
    private static void UpdateDodge_Postfix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            MoveTracker.Tick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateDodge postfix", e);
        }
    }
}
