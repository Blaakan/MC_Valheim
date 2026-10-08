#if DEBUG
using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod
{
    // Debug build only: switches self tests flip to drive the local player (design 7.4). Self test clear them in
    // finally. Never in Release.
    internal static class SelfTestHooks
    {
        // True = local player hold block (as if the block key was held), whatever the input say.
        internal static bool HoldBlock;

        // True = local player hold the attack button (vanilla PlayerAttackInput then call StartAttack every tick).
        internal static bool HoldAttack;

        // True = local player walk along Move (world direction, flat, length 1), run key held when Run. Vanilla
        // movement code do the rest (speed factors, status effects): test read the speed it aim for.
        internal static bool Moving;
        internal static Vector3 Move;
        internal static bool Run;

        internal static void Clear()
        {
            HoldBlock = false;
            HoldAttack = false;
            Moving = false;
            Move = Vector3.zero;
            Run = false;
        }
    }
}

namespace MC.Combat.ShieldsTowerWallMod.Patches
{
    // Debug build only: self test hook. After vanilla read the input, me force the block key while HoldBlock, the
    // attack button while HoldAttack and the move direction while Moving. Three static bool reads per call.
    [HarmonyPatch]
    internal static class PlayerDebugPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static void SetControls_Postfix(Player __instance)
        {
            if (!SelfTestHooks.HoldBlock && !SelfTestHooks.HoldAttack && !SelfTestHooks.Moving)
            {
                return;
            }
            try
            {
                if (ReferenceEquals(__instance, Player.m_localPlayer))
                {
                    if (SelfTestHooks.HoldBlock)
                    {
                        __instance.m_blocking = true;
                    }
                    if (SelfTestHooks.HoldAttack)
                    {
                        __instance.m_attackHold = true;
                    }
                    if (SelfTestHooks.Moving)
                    {
                        __instance.m_moveDir = SelfTestHooks.Move;
                        __instance.m_run = SelfTestHooks.Run;
                    }
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("Player.SetControls postfix", e);
            }
        }
    }
}
#endif
