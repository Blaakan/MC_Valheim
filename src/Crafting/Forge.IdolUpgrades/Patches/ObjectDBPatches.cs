using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod.Patches;

// Item database ready (main menu copy and in-game one): idol levels raised, idol table built again.
[HarmonyPatch]
internal static class ObjectDBPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static void Awake_Postfix()
    {
        Refresh("ObjectDB.Awake postfix");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static void CopyOtherDB_Postfix()
    {
        Refresh("ObjectDB.CopyOtherDB postfix");
    }

    private static void Refresh(string site)
    {
        try
        {
            IdolCatalog.Invalidate();
            IdolCatalog.RaiseIdolLevels();
        }
        catch (Exception e)
        {
            PatchGuard.Report(site, e);
        }
    }
}
