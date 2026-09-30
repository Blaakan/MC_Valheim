using HarmonyLib;
using MC.Shared;

namespace MC.Core.ProbeAMod;

// Me = always-on probe patch (Test-Framework.ps1). Main menu call FejdStartup.Update every frame. Me stay patched
// whatever feature state: me log each time feature state change, so test see me run while Probe A is Off too.
[AlwaysOnPatch]
[HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Update))]
internal static class AlwaysOnProbe
{
    private static int _logged = -1; // -1 none yet, 0 logged off, 1 logged on

    private static void Postfix()
    {
        var on = Plugin.FeatureOn ? 1 : 0;
        // First line always "on" (test count lines: on 1, off 1, on 2), so me wait for first activation.
        if (_logged == -1 && on == 0)
        {
            return;
        }
        if (on != _logged)
        {
            _logged = on;
            Log.Info($"[probe] always-on patch running, feature {(on == 1 ? "on" : "off")}");
        }
    }
}
