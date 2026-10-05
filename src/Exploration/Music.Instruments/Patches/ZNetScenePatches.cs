using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.MusicInstrumentsMod.Patches;

// ALWAYS ON. Network prefab list ready (every game and dedicated server): instruments registered (idempotent).
// Without me games log "Missing prefab hash" for dropped instruments and the host delete them as unknown prefabs.
// Dedicated server has no main menu: me build the items here from the ZNetScene's Flint Knife when no database built
// them yet.
[AlwaysOnPatch]
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class ZNetScenePatches
{
    [HarmonyPostfix]
    private static void Postfix(ZNetScene __instance)
    {
        try
        {
            InstrumentContent.RegisterInZNetScene(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNetScene.Awake postfix", e);
        }
    }
}
