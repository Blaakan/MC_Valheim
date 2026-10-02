using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewSpyglassMod.Patches;

// ALWAYS ON. Network prefab list ready (every game and dedicated server): spyglass item registered (idempotent).
// Without me games log "Missing prefab hash" for dropped spyglasses and the host delete them as unknown prefabs.
// Dedicated server has no main menu: me build the item here from the ZNetScene's Flint Knife when no database built
// it yet.
[AlwaysOnPatch]
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class ZNetScenePatches
{
    [HarmonyPostfix]
    private static void Postfix(ZNetScene __instance)
    {
        try
        {
            SpyglassContent.RegisterInZNetScene(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNetScene.Awake postfix", e);
        }
    }
}
