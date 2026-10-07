using MC.Shared;

namespace MC.Core.ProbeServerMod;

// Me = server probe (tools/Test-Multiplayer.ps1). Never shipped. Idle unless env var MC_MP_SERVER_DIR set. Then me
// wait for the dedicated server world, register probe rpcs and answer the client probe (ServerDriver).
internal sealed partial class Plugin : ModPlugin
{
    protected override void OnActivated() => ServerDriver.Begin(this);

    protected override void OnDeactivated() => ServerDriver.End(this);
}
