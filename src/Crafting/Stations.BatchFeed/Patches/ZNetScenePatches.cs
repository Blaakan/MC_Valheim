using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// World load: ZNetScene wake up with every network prefab. Me list covered station parts once (Debug log).
// Last: mods that register their pieces in their own ZNetScene.Awake postfix get listed too.
[HarmonyPatch]
internal static class ZNetScenePatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    private static void Awake_Postfix(ZNetScene __instance)
    {
        try
        {
            CoverageDump.Run(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNetScene.Awake postfix", e);
        }
    }
}
