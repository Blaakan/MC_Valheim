using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.Crossbow.StaysLoaded;

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
[BepInProcess("valheim.exe")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<bool> Enabled;

    private Harmony _harmony;

    private void Awake()
    {
        Log.Init(Logger);

        Enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");

        // Me patch every [HarmonyPatch] class in this dll. Harmony id = mod guid.
        _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, ModInfo.Guid);

        Log.Ready(ModInfo.Guid, ModInfo.Version);
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
