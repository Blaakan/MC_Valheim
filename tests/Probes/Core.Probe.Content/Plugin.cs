using HarmonyLib;
using MC.Shared;

namespace MC.Core.ProbeContentMod;

// Me = stand-in third-party content mod (never shipped). Idle unless a test run started the game. Then me register
// made-up modded content the way real content libraries do (ContentRegistry, Patches/ContentPatches.cs), so self
// tests can check that every MC mod picks up third-party items, pieces and creatures. Like a real content mod me never
// take content back: every hook is [AlwaysOnPatch], the live toggle change nothing.
internal sealed partial class Plugin : ModPlugin
{
    protected override void ApplyPatches(Harmony harmony)
    {
    }
}
