using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Farming.BreedingStarInheritanceMod;

// Me = other mods me must know about. Star Level System replace breeding level code (own cache for live babies,
// parent level = egg COUNT): our level swap would change its egg count in silence. So me leave births to it.
// Me look late (first use, in world), not in OnActivated: first activation run inside our Awake, and BepInEx put a
// plugin in PluginInfos only when it load it ("MidnightsFX" load after "MC." when no dependency order).
internal static class Compat
{
    // GUID in its StarLevelSystem.cs (source read at 1.18.1).
    internal const string StarLevelSystemGuid = "MidnightsFX.StarLevelSystem";

    private static bool _detected;
    private static bool _starLevelSystem;

    internal static bool StarLevelSystemLoaded
    {
        get
        {
            if (!_detected)
            {
                Detect();
            }
            return _starLevelSystem;
        }
    }

    // OnActivated: look again at next use (Info line once per activation).
    internal static void Reset()
    {
        _detected = false;
        _starLevelSystem = false;
    }

    private static void Detect()
    {
        _detected = true;
        _starLevelSystem = Chainloader.PluginInfos.ContainsKey(StarLevelSystemGuid);
        if (_starLevelSystem)
        {
            Log.Info("Star Level System is installed: it decides breeding levels, so Breeding Star Inheritance leaves births to it.");
        }
    }
}
