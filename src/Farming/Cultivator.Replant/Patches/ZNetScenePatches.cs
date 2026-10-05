using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// ALWAYS ON. Network prefab list ready (every game and dedicated server): transplant items and saplings registered
// (idempotent), saplings put in the cultivator's piece table. Without me games log "Missing prefab hash" for dropped
// transplants and planted saplings, and the host delete them as unknown prefabs. Dedicated server has no main menu: me
// build the items here from the scene's produce when no database built them yet.
// Me run AFTER PlantEverything and PlantEasily postfixes: they snapshot the prefab list in a first prefix and, in
// their last postfix, take every new Plant prefab as "modded crop" (PlantEverything force cultivated ground and
// destroy-if-cant-grow on it; PlantEasily register it for replant-on-harvest). Our saplings added after them = never
// seen, keep our own rules.
[AlwaysOnPatch]
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class ZNetScenePatches
{
    [HarmonyPostfix]
    [HarmonyAfter("advize.PlantEverything", "advize.PlantEasily")]
    private static void Postfix(ZNetScene __instance)
    {
        try
        {
            TransplantContent.RegisterInZNetScene(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNetScene.Awake postfix", e);
        }
    }
}
