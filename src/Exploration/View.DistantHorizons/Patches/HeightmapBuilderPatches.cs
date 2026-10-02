using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewDistantHorizonsMod.Patches;

// Me = compute exact-height far tiles on game's own terrain builder thread (ExactTerrainBuilder.TryBuild).
// [AlwaysOnPatch]: patched once at game start, before that thread exist, never re-patched by live toggle. Build run
// on builder thread outside any lock, so detour written while it run could tear. Safe when feature off: me only act
// on my own ExactBuildData jobs, and only active feature queue them (job left in queue at turn-off just get computed,
// then age out of builder's ready list). No config read here.
[AlwaysOnPatch]
[HarmonyPatch(typeof(HeightmapBuilder), nameof(HeightmapBuilder.Build))]
internal static class HeightmapBuilderPatches
{
    // Builder thread. TryBuild never throw (it catch and log once by itself: PatchGuard is main thread only).
    [HarmonyPrefix]
    private static bool Prefix(HeightmapBuilder.HMBuildData data)
    {
        return !ExactTerrainBuilder.TryBuild(data);
    }
}
