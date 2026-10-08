using MC.Shared;

namespace MC.Crafting.RepairOneClickAllMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No setting besides Enabled: turn off = vanilla one item per click again, no restart.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones).
internal sealed partial class Plugin : ModPlugin
{
#if DEBUG
    // Debug build only (Release = class stay empty, same as before). Self tests come and go with the feature.

    // Self test put text here = feature off (framework ask LocalBlocker at every refresh), null = on again.
    // In memory only: test never write Enabled (BepInEx save the real cfg at once).
    internal static string TestBlocker;

    protected override string LocalBlocker() => TestBlocker;

    protected override void OnActivated() => SelfTests.Register();

    protected override void OnDeactivated() => SelfTests.Unregister();
#endif
}
