using MC.Shared;

namespace MC.Core.ProbeAMod;

internal sealed partial class Plugin : ModPlugin
{
    // Me reset so patch log again after each re-activation.
    protected override void OnActivated() => ProbeMenuPatch.Seen = false;

    // Never called. Use a dll that is never deployed, so JitCheck MUST report it (proof JitCheck really compile).
    internal static int UseMissingLib() => new ProbeMissingLib.MissingThing().Touch();
}
