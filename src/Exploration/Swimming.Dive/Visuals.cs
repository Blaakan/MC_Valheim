using BepInEx.Configuration;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Me = the personal view settings (section Visuals, each player's own, never sent). One accessor per setting so the
// camera and view code read one place, and a Debug self test can force them in memory (never the config file).
internal static class Visuals
{
    internal const float VisibilityMin = 5f;
    internal const float VisibilityMax = 100f;
    internal const float DefaultVisibility = 25f;
    internal static readonly Color DefaultFogColor = new Color(0.1f, 0.32f, 0.38f, 1f);

#if DEBUG
    // Self test: true = camera, fog and surface on with default look; null = config.
    internal static bool? TestAllOn;
#endif

    internal static bool Camera => Flag(Plugin.UnderwaterCamera);

    internal static bool Fog => Flag(Plugin.UnderwaterFog);

    internal static bool Surface => Flag(Plugin.SurfaceFromBelow);

    internal static float Visibility
    {
        get
        {
#if DEBUG
            if (TestAllOn.HasValue)
            {
                return DefaultVisibility;
            }
#endif
            var v = Plugin.UnderwaterVisibility != null ? Plugin.UnderwaterVisibility.Value : DefaultVisibility;
            return Mathf.Clamp(v, VisibilityMin, VisibilityMax);
        }
    }

    internal static Color FogColor
    {
        get
        {
#if DEBUG
            if (TestAllOn.HasValue)
            {
                return DefaultFogColor;
            }
#endif
            return Plugin.UnderwaterFogColor != null ? Plugin.UnderwaterFogColor.Value : DefaultFogColor;
        }
    }

    private static bool Flag(ConfigEntry<bool> entry)
    {
#if DEBUG
        if (TestAllOn.HasValue)
        {
            return TestAllOn.Value;
        }
#endif
        return entry == null || entry.Value;
    }
}
