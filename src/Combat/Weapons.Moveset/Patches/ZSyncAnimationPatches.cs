using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod.Patches;

// Me = other players' roll flow (design 2.9). Vanilla RPC_SetTrigger set a trigger on this peer's copy of a
// character's Animator (every peer, local one too). Other player's roll attack trigger while their Animator still in
// the roll = me cross-fade it out of the roll here too, like the attacker's own game (no own RPC). Every trigger of
// every character in range pass here: rules flag and twelve string compares first, Animator reads only after.
[HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.RPC_SetTrigger))]
internal static class ZSyncAnimationPatches
{
    [HarmonyPostfix]
    private static void RPC_SetTrigger_Postfix(ZSyncAnimation __instance, string name)
    {
        try
        {
            RollFlow.OnRemoteTrigger(__instance, name);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZSyncAnimation.RPC_SetTrigger postfix", e);
        }
    }
}
