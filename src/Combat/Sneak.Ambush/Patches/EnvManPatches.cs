using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = fog density for the fog bonus (design 2.4), from GAME DATA: same inputs vanilla render (environment's four
// fog densities x day-part weights), not RenderSettings.fogDensity, so fog removers and visibility mods change
// nobody's bonus. EnvMan.FixedUpdate call SetEnv every frame with current (blended or forced) environment. Postfix
// run also when vanilla returned early (no camera): me only need the inputs. Four multiply-adds.
[HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv),
    new[] { typeof(EnvSetup), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) })]
internal static class EnvManPatches
{
    [HarmonyPostfix]
    private static void Postfix(EnvSetup env, float dayInt, float nightInt, float morningInt, float eveningInt)
    {
        if (env == null)
        {
            return;
        }
        try
        {
            StealthState.FogDensity = env.m_fogDensityNight * nightInt + env.m_fogDensityDay * dayInt
                                      + env.m_fogDensityMorning * morningInt + env.m_fogDensityEvening * eveningInt;
            StealthState.FogDensityKnown = true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("EnvMan.SetEnv postfix", e);
        }
    }
}
