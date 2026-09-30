using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = slow bash swing (design 2.7 "Swing speed", decision 32). Speed(float) is the Unity animation event a clip use
// to set its own animator speed (punch clips: x2 from their first frame). Prefix scale the value for the local
// player's running bash only (BashSpeed); every other character and clip run untouched. Speed events are a few per
// clip, never per frame: one static read when no bash is slowed.
[HarmonyPatch]
internal static class CharacterAnimEventPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
    private static void Speed_Prefix(CharacterAnimEvent __instance, ref float speedScale)
    {
        if (!BashSpeed.Scaling)
        {
            return;
        }
        try
        {
            BashSpeed.OnSpeedEvent(__instance, ref speedScale);
        }
        catch (Exception e)
        {
            PatchGuard.Report("CharacterAnimEvent.Speed prefix", e);
        }
    }
}
