using HarmonyLib;
using MC.Shared;

// Me linked into every probe. Global namespace + internal: each probe dll has own copy, no clash.

// Main menu call FejdStartup.Update every frame. Patched = me log once per activation, so test see patch really on.
[HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Update))]
internal static class ProbeMenuPatch
{
    internal static bool Seen;

    private static void Postfix()
    {
        if (!Seen)
        {
            Seen = true;
            Log.Info("[probe] patch running");
        }
    }
}
