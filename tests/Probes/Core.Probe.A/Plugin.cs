using BepInEx.Configuration;
using MC.Shared;

namespace MC.Core.ProbeAMod;

internal sealed partial class Plugin : ModPlugin
{
    // Always-on probe read me (it must not ask framework: it run while feature off).
    internal static bool FeatureOn;

    // Test-Framework.ps1 set me true in cfg: LocalBlocker then block feature (Conflict status), false = back.
    private static ConfigEntry<bool> _block;

    protected override void BindConfig()
    {
        _block = Config.Bind("Test", "Block", false, "Framework test only: true blocks the feature through LocalBlocker.");
    }

    protected override string LocalBlocker() =>
        _block != null && _block.Value ? "Inactive: blocked by the Block setting of the probe." : null;

    // Me reset so patch log again after each re-activation.
    protected override void OnActivated()
    {
        ProbeMenuPatch.Seen = false;
        FeatureOn = true;
    }

    protected override void OnDeactivated() => FeatureOn = false;

    // Never called. Use a dll that is never deployed, so JitCheck MUST report it (proof JitCheck really compile).
    internal static int UseMissingLib() => new ProbeMissingLib.MissingThing().Touch();
}
