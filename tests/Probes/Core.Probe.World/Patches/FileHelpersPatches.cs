using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Core.ProbeWorldMod.Patches;

// Me say "cloud saves off" for whole probe session (same state as a player who turned Steam Cloud off). Then game
// never list, read or write Steam Cloud characters/worlds; every save path fall back to Utils save override
// (<run dir>/saves). Applied by SaveIsolation on own Harmony, only when MC_INWORLD_DIR set, never removed.
[HarmonyPatch(typeof(FileHelpers), nameof(FileHelpers.CloudStorageSupportedAndEnabled), MethodType.Getter)]
internal static class FileHelpersPatches
{
    private static void Postfix(ref bool __result)
    {
        try
        {
            __result = false;
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(FileHelpersPatches), e);
        }
    }
}
