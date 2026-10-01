using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = stop the vanilla stone trigger at the source (design 2.1). A Malicious Ice that start jotun_invasion
// (BlackIce_Start), on its owner: skip the vanilla banner and request, the server count the stone (ZDO destroy) and
// send its own message and invasions. Only once the server's rules came (= server has me); a game still waiting run
// vanilla and the server drop the request. Stop triggers (invasion core) stay vanilla. Called through a delegate
// (Destructible.m_onDestroyed): never inlined.
[HarmonyPatch(typeof(TriggerPersistentEventOnDestroy))]
internal static class TriggerPersistentEventOnDestroyPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(TriggerPersistentEventOnDestroy.OnDestroyed))]
    private static bool OnDestroyed_Prefix(TriggerPersistentEventOnDestroy __instance)
    {
        try
        {
            if (__instance._stopEvent || !Stones.IsInvasionName(__instance._eventInternalName) || ServerRules.IsPending)
            {
                return true;
            }
            var nview = __instance._netView;
            if (nview == null || !nview.IsOwner())
            {
                return true;
            }
            Log.Debug("A Malicious Ice broke here: the server handles the Deep North awakening and the invasions.");
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("TriggerPersistentEventOnDestroy.OnDestroyed prefix", e);
            return true;
        }
    }
}
