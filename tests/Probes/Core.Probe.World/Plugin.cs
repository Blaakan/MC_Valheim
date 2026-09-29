using HarmonyLib;
using MC.Shared;

namespace MC.Core.ProbeWorldMod;

// Me = world probe (tools/Test-InWorld.ps1). Never shipped. Idle unless env var MC_INWORLD_DIR set. Then me:
//   1. point every save at <dir>/saves and turn cloud saves off (SaveIsolation), before menu read anything;
//   2. at main menu make character + world "MCProbe", start it like the Start button (MenuDriver);
//   3. when world settle: god mode, no stamina drain, run probe.baseline + every SelfTest (ProbeDriver, SafeRunner);
//   4. log "[selftest] DONE ...", quit game (unless MC_INWORLD_KEEP=1).
internal sealed partial class Plugin : ModPlugin
{
    // Me no PatchAll here. Isolation patch sit on own Harmony (SaveIsolation) and stay until game exit: live toggle
    // of this probe must never bring real cloud saves back in the middle of a run.
    protected override void ApplyPatches(Harmony harmony)
    {
    }

    protected override void OnActivated() => ProbeDriver.Begin(this);

    protected override void OnDeactivated() => ProbeDriver.End(this);
}
