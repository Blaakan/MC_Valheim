using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Building.LightsSwitchableMod.Patches;

// World load: every network prefab known now. Me build the light set and say in log which Lights names match nothing,
// match no fire, or match a fire that can cook (those stay normal fires).
[HarmonyPatch]
internal static class ZNetScenePatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    private static void Awake_Postfix()
    {
        try
        {
            LightRules.ReportNow();
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNetScene.Awake postfix", e);
        }
    }
}
