using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = nature fights the Jotun (design 2.5). IsEnemy run for every creature pair an AI look at: cheap exits first
// (vanilla already said enemies, or hostility off), then faction and prefab hash compares. Static method with two
// arguments (the one-argument instance IsEnemy call it).
[HarmonyPatch(typeof(BaseAI))]
internal static class BaseAIPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
    private static void IsEnemy_Postfix(Character a, Character b, ref bool __result)
    {
        if (__result || !WorldState.HostilityOn)
        {
            return;
        }
        try
        {
            if (Hostility.MakeFoes(a, b))
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.IsEnemy postfix", e);
        }
    }
}
