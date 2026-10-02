using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod.Patches;

// Me = thinner fog in clear weather, so eye see far land. Vanilla SetEnv write RenderSettings.fogDensity fresh
// every call (when main camera exist), so me MULTIPLY what is there: never compound, and other fog mods (Seasons,
// GammaOfNightLights, NoFogBruh, ValheimPlus) keep own effect under mine. Environment's own densities only decide
// how clear weather is (clear = full thinning, storm = none, linear ramp between). Wet weather keep its fog.
// Me remember last value me wrote, so turn-off put fog back at once, also while paused (SetEnv not run then).
[HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv),
    new[] { typeof(EnvSetup), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) })]
internal static class EnvManPatches
{
    private static float _before = -1f; // density the game (and mods before me) wrote
    private static float _after = -1f;  // density me wrote on top

    [HarmonyPostfix]
    private static void Postfix(EnvSetup env, float dayInt, float nightInt, float morningInt, float eveningInt)
    {
        var c = Plugin.Cfg;
        if (c == null || env == null)
        {
            return;
        }
        try
        {
            var mult = c.FogDensityMultiplier.Value;
            // No main camera = vanilla return early, no fog write: me multiply nothing (would compound).
            if (mult == 1f || Utils.GetMainCamera() == null)
            {
                return;
            }
            if (c.KeepWetWeatherFog.Value && env.m_isWet)
            {
                return; // rain and thunderstorms keep their fog
            }
            var density = env.m_fogDensityNight * nightInt
                          + env.m_fogDensityDay * dayInt
                          + env.m_fogDensityMorning * morningInt
                          + env.m_fogDensityEvening * eveningInt;
            // Clear weather = full thinning, storms/mist/blizzards = none, linear ramp between.
            var clear = c.FogClearDensity.Value;
            var storm = Mathf.Max(c.FogStormDensity.Value, clear + 0.00001f);
            var t = Mathf.Clamp01((density - clear) / (storm - clear));
            var before = RenderSettings.fogDensity;
            RenderSettings.fogDensity = before * Mathf.Lerp(mult, 1f, t);
            _before = before;
            _after = RenderSettings.fogDensity;
        }
        catch (Exception e)
        {
            PatchGuard.Report("EnvMan.SetEnv postfix", e);
        }
    }

    // Feature off: fog back now, only when it still hold my value (another mod may have written since).
    internal static void RestoreFog()
    {
        if (_after >= 0f && RenderSettings.fogDensity == _after)
        {
            RenderSettings.fogDensity = _before;
        }
        _before = -1f;
        _after = -1f;
    }
}
