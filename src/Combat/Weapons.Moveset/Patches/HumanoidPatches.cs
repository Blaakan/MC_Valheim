using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod.Patches;

// Me = attack start of the local player (design 2.1, 2.7, 7.2). Monsters call this too: one reference compare.
//   Prefix:    move of ours still starting (not in its animation yet) = refuse restart (__result false, skip
//              original). Else Decide: move for this start, or none. Roll attack inside the roll = open vanilla's
//              InDodge gate (m_inDodge false) for this call only.
//   Postfix:   success = eat tokens (first attack of jump/roll only); roll attack = cross-fade out of the roll
//              (RollFlow); move applied = watch it; else learn the swing's animation state.
//   Finalizer: clear the call state and close the roll gate (m_inDodge back true) on every path (other prefixes may
//              skip original, StartAttack may return before Attack.Start).
// Sibling MC mods on same method: Dual Wielding (Priority.First prefix + finalizer), Sneak Ambush (always-on prefix
// that can skip), Tower Shield Wall (prefix that skip a bash in its cooldown + postfix). Me tolerate a skipped
// original: my logic only on __result.
[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack), typeof(Character), typeof(bool))]
internal static class HumanoidPatches
{
    [HarmonyPrefix]
    private static bool StartAttack_Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
    {
        var player = Player.m_localPlayer;
        if (!ReferenceEquals(__instance, player) || player == null)
        {
            return true;
        }
        try
        {
            if (MoveTracker.IsStarting(player))
            {
                // Same as vanilla once Animator show the attack (InAttack), a few ticks sooner: held button, buffered
                // press or two physics ticks in one frame no stop the move for a plain first swing.
                __result = false;
                MoveTracker.OnRefused();
                return false;
            }
            MoveTracker.BeforeStart(player, secondaryAttack);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack prefix", e);
        }
        return true;
    }

    [HarmonyPostfix]
    private static void StartAttack_Postfix(Humanoid __instance, bool __result)
    {
        var player = Player.m_localPlayer;
        if (!ReferenceEquals(__instance, player) || player == null)
        {
            return;
        }
        try
        {
            MoveTracker.AfterStart(player, __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack postfix", e);
        }
    }

    [HarmonyFinalizer]
    private static void StartAttack_Finalizer()
    {
        try
        {
            MoveTracker.ClearCall();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack finalizer", e);
        }
    }
}
