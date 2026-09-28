using BepInEx.Configuration;
using MC.Shared;

namespace __ROOTNS__;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
internal sealed partial class Plugin : ModPlugin
{
    protected override void BindConfig()
    {
        // Example: SomeSetting = Config.Bind("Section", "Key", default, "User-facing description.");
    }
}
