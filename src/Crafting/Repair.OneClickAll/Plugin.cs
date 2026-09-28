using MC.Shared;

namespace MC.Crafting.RepairOneClickAllMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No setting besides Enabled: turn off = vanilla one item per click again, no restart.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones).
internal sealed partial class Plugin : ModPlugin
{
}
