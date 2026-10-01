using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = the server decide every Jotun invasion (design 2.1, D4). Server only: a jotun_invasion request me did not make
// (Stones.Bypass) is held HoldSeconds; a broken Malicious Ice in that window eat it, else it run (admin pevents, other
// mods). Other events, other games: vanilla. The rpc is reached through a delegate (RoutedMethod): never inlined.
[HarmonyPatch(typeof(PersistentEventSystem))]
internal static class PersistentEventSystemPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(PersistentEventSystem.RPC_RequestStartEvent))]
    private static bool RPC_RequestStartEvent_Prefix(PersistentEventSystem __instance, long sender, int sourceEventId)
    {
        try
        {
            if (Stones.Bypass)
            {
                return true;
            }
            var net = ZNet.instance;
            if (net == null || !net.IsServer() || !Stones.IsInvasionIndex(__instance, sourceEventId))
            {
                return true;
            }
            Stones.Hold(sender, sourceEventId);
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("PersistentEventSystem.RPC_RequestStartEvent prefix", e);
            return true;
        }
    }
}
