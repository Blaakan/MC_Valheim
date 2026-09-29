using MC.Shared;

namespace MC.Farming.HarpoonHooksTamesMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// No setting besides Enabled: pull, line break, stamina drain stay vanilla (they run on the game that own the animal,
// maybe without me). Me make no UI object. Off = patches gone = harpoon fly through tames again, like vanilla.
internal sealed partial class Plugin : ModPlugin
{
    // Just turned on: start clean (cache may hold old world status effects).
    protected override void OnActivated()
    {
        HarpoonEffect.Clear();
        HitScope.Close();
        Patches.ProjectilePatches.ClearSkipLog();
    }

    // Turned off: forget cache and scope. Hook already on a tame stay until vanilla end it (line break, block).
    protected override void OnDeactivated()
    {
        HarpoonEffect.Clear();
        HitScope.Close();
        Patches.ProjectilePatches.ClearSkipLog();
    }
}
