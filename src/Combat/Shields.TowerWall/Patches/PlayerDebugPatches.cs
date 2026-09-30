#if DEBUG
using System;
using HarmonyLib;
using MC.Shared;

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

        internal static void Clear()
        {
            HoldBlock = false;
            HoldAttack = false;
        }
    }
}

namespace MC.Combat.ShieldsTowerWallMod.Patches
{
    // Debug build only: self test hook. After vanilla read the input, me force the block key while HoldBlock and the
    // attack button while HoldAttack. Two static bool reads per call.
    [HarmonyPatch]
    internal static class PlayerDebugPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static void SetControls_Postfix(Player __instance)
        {
            if (!SelfTestHooks.HoldBlock && !SelfTestHooks.HoldAttack)
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
