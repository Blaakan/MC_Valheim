using System;

namespace MC.Building.LightsSwitchableMod;

// Me = the gameplay setting of the mod (which pieces are lights). Server send its own to every player (ServerRules):
// same lights for everybody.
internal sealed class LightsRules
{
    internal string Lights = "";

    internal static LightsRules Own() => new LightsRules { Lights = Plugin.Lights.Value ?? "" };

    // Wire: version, then values in fixed order. Version bump = other layout (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(Lights ?? "");
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had).
    internal static bool TryRead(ZPackage pkg, out LightsRules rules)
    {
        rules = null;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            rules = new LightsRules { Lights = pkg.ReadString() ?? "" };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal string Describe() =>
        Lights == Plugin.DefaultLights ? "default light list" : "light list: " + Lights;
}
