using System;
using System.Globalization;
using BepInEx.Configuration;

namespace MC.Exploration.ViewSpyglassMod;

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same
// recipe, same zoom limit, same fog clearing for everybody. Personal choices (section Controls and View: start zoom,
// aim speed, hold or click, overlay) and server-only AllowPlayersWithoutMod are not in here. Snapshot never change
// after build: new rules = new object. Pending = client still wait for server rules: built in code, never from
// config, never sent. Game code must check IsPending (recipe hidden, spyglass not raised), not trust the numbers.
internal sealed class SpyglassRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const int StationLevelMin = 1;
    internal const int StationLevelMax = 10;
    internal const float MagnificationMin = 2f;
    internal const float MagnificationMax = 20f;
    internal const int TextMax = 1000; // recipe strings from wire: longer = cut

    internal const string DefaultRecipeResources = "Bronze:2,Crystal:2";
    internal const string DefaultRecipeStation = "forge";

    // Recipe: "Name:amount,..." item prefab names, station prefab name ("" = by hand), station level.
    internal string RecipeResources = DefaultRecipeResources;
    internal string RecipeStation = DefaultRecipeStation;
    internal int RecipeStationLevel = 1;

    // Strongest zoom anyone may use (x times closer).
    internal float MaxMagnification = 8f;

    // Share of the fog taken away while the spyglass is at the eye (only with Distant Horizons, only clear weather).
    internal float FogClearing = 0.75f;

    // True only on Pending: client wait for server rules, spyglass stay down, recipe hidden.
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly SpyglassRules Default = new SpyglassRules();

    // Client of a server with me, before server rules came.
    internal static readonly SpyglassRules Pending = new SpyglassRules
    {
        IsPending = true,
        RecipeResources = "",
        RecipeStation = "",
    };

    // This game's own config. Entry not bound (bind blew up) = default.
    internal static SpyglassRules Own()
    {
        var d = Default;
        return new SpyglassRules
        {
            RecipeResources = V(Plugin.RecipeResources, d.RecipeResources) ?? "",
            RecipeStation = V(Plugin.RecipeStation, d.RecipeStation) ?? "",
            RecipeStationLevel = V(Plugin.RecipeStationLevel, d.RecipeStationLevel),
            MaxMagnification = V(Plugin.MaxMagnification, d.MaxMagnification),
            FogClearing = V(Plugin.FogClearing, d.FogClearing),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(RecipeResources ?? "");
        pkg.Write(RecipeStation ?? "");
        pkg.Write(RecipeStationLevel);
        pkg.Write(MaxMagnification);
        pkg.Write(FogClearing);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out SpyglassRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var d = Default;
            var r = new SpyglassRules();
            r.RecipeResources = Text(pkg.ReadString(), ref clamped);
            r.RecipeStation = Text(pkg.ReadString(), ref clamped);
            r.RecipeStationLevel = Clamp(pkg.ReadInt(), StationLevelMin, StationLevelMax, ref clamped);
            r.MaxMagnification = Clamp(pkg.ReadSingle(), MagnificationMin, MagnificationMax, d.MaxMagnification,
                ref clamped);
            r.FogClearing = Clamp(pkg.ReadSingle(), 0f, 1f, d.FogClearing, ref clamped);
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // One log line with every value (also the "did rules change" test of ServerRules: same text = same rules).
    internal string Describe()
    {
        if (IsPending)
        {
            return "waiting for the server's rules (no spyglass recipe and no zoom until they arrive)";
        }
        var station = string.IsNullOrEmpty(RecipeStation) ? "by hand" : RecipeStation + " level " + RecipeStationLevel;
        return "recipe " + RecipeResources + " at " + station + ", zoom up to x" + F(MaxMagnification)
               + ", fog clearing " + F(FogClearing);
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // Not a number (NaN, infinity) = default value.
    private static float Clamp(float value, float min, float max, float fallback, ref bool clamped)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            clamped = true;
            return fallback;
        }
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }

    private static string Text(string value, ref bool clamped)
    {
        if (value == null)
        {
            return "";
        }
        if (value.Length > TextMax)
        {
            clamped = true;
            return value.Substring(0, TextMax);
        }
        return value;
    }
}
