using MC.Shared;

namespace MC.Core.ProbeBMod;

internal sealed partial class Plugin : ModPlugin
{
    // Me reset so patch log again after each re-activation.
    protected override void OnActivated() => ProbeMenuPatch.Seen = false;
}
