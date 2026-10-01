using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = blizzard in a storming area (design 2.4). Vanilla ask every frame; me answer only when it has no override
// (raids, bosses, AltBiome, vanilla invasion, env zones keep priority). Storms cache the answer half a second.
[HarmonyPatch(typeof(EnvMan))]
internal static class EnvManPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(EnvMan.GetEnvironmentOverride))]
    private static void GetEnvironmentOverride_Postfix(ref string __result)
    {
        if (!string.IsNullOrEmpty(__result))
        {
            return;
        }
        try
        {
            var env = Storms.Override();
            if (env != null)
            {
                __result = env;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("EnvMan.GetEnvironmentOverride postfix", e);
        }
    }
}
