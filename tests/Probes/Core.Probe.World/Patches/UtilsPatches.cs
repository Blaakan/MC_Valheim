using System;
using System.Diagnostics;
using HarmonyLib;
using MC.Shared;

namespace MC.Core.ProbeWorldMod.Patches;

// Me make Utils.GenerateUID differ on every call of a multiplayer probe session. Game build it from host name hash +
// UnityEngine.Random, and ZDOMan take its session id from it at every join. Probe that leave server and join again from
// main menu in same process got the SAME session id each time (seen 2026-10-07, each-off scenario: -964981125 twice), so
// new player ZDO had id of the old session's player. Server keep ids of destroyed ZDOs (ZDOMan.m_deadZDOs) and destroy
// a new ZDO with such id (ZDOMan.RPC_ZDOData): player vanish right after spawn. Real player spend other time in menu
// each time; probe click same frames. Applied by SaveIsolation on own Harmony, multiplayer run only, never removed.
[HarmonyPatch(typeof(Utils), nameof(Utils.GenerateUID))]
internal static class UtilsPatches
{
    private static int _calls;

    private static void Postfix(ref long __result)
    {
        try
        {
            _calls++;
            __result += (Stopwatch.GetTimestamp() & 0x3FFFFFFF) + _calls;
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UtilsPatches), e);
        }
    }
}
