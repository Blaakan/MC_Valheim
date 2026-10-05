using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// ALWAYS ON. Item database ready (main menu copy and in-game one, also dedicated server): transplant items registered
// (idempotent), cultivator recipe captured for the new levels, content rebuilt. Main menu: first Awake run on empty
// lists (skipped silently), CopyOtherDB then fill them. Code here never need feature active or bound config.
[AlwaysOnPatch]
[HarmonyPatch]
internal static class ObjectDBPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static void Awake_Postfix(ObjectDB __instance)
    {
        Register(__instance, "ObjectDB.Awake postfix");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static void CopyOtherDB_Postfix(ObjectDB __instance)
    {
        Register(__instance, "ObjectDB.CopyOtherDB postfix");
    }

    private static void Register(ObjectDB db, string site)
    {
        try
        {
            TransplantContent.RegisterInObjectDB(db);
        }
        catch (Exception e)
        {
            PatchGuard.Report(site, e);
        }
    }
}
