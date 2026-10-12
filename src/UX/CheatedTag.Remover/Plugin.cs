using System;
using MC.Shared;

namespace MC.UX.CheatedTagRemoverMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me take the vanilla "cheated" tag off a whole world on command (ClearCommand): saved object data (WorldSweep), the
// copies loaded objects keep in memory and the items the player carry (LiveSweep). Nothing run by itself: no patch
// touch items or objects, tags only go when somebody type the command. No setting of me own.
// Both side with no player check: world live on the server, so the server must load me, but players without me are
// fine (their carried items just keep the tag).
internal sealed partial class Plugin : ModPlugin
{
    internal static Plugin Instance { get; private set; }

#if DEBUG
    // Self test turn me off and on like the tick in MC Mods, never through Enabled (that write the config file): text
    // here = framework take my patches off and show it as Status. Null = normal. Set it, then FeatureRegistry.RefreshAll().
    // Release build has no LocalBlocker at all (base one say nothing).
    internal static string TestBlocked { get; set; }

    protected override string LocalBlocker() => TestBlocked;
#endif

    protected override void BindConfig()
    {
        Instance = this;
        // Commands follow Enabled, not Active: client of a server without me is "inactive" for the framework, and
        // must still clean its own side. BindConfig run once at start, whatever the state.
        Enabled.SettingChanged += (_, _) => SyncCommands();
        SyncCommands();
        SelfTests.RegisterAlways();
    }

    protected override void OnActivated()
    {
        ServerLink.Start();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    protected override void OnDeactivated()
    {
        // Conditional method (gone in Release): no delegate to it, so own try instead of Step().
        try
        {
            SelfTests.Unregister();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated SelfTests.Unregister", e);
        }
        Step("SweepJob.Stop", SweepJob.Stop);
        Step("ServerLink.Stop", ServerLink.Stop);
    }

    private void SyncCommands()
    {
        try
        {
            if (Enabled.Value)
            {
                ClearCommand.Register();
            }
            else
            {
                ClearCommand.Unregister();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.SyncCommands", e);
        }
    }

    private static void Step(string site, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated " + site, e);
        }
    }
}
