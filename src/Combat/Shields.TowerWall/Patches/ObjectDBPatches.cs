using System;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = item database hooks (design 2.0, 2.1, 6.3).
//   Awake / CopyOtherDB postfix (Low: after other mods' item setup): database with items = new tower catalog, rules
//   in force (vanilla while a client wait) written on prefabs and live copies (TowerSync). First pass with items
//   (main menu: every plugin loaded and patched) = foreign shield mod warning. Main menu Awake has no items: skipped.
//   GetAllCraftableWeapons postfix: towers out of the (cached) list, so "craft every weapon" achievement and craft
//   progress stay vanilla (towers count as weapons now: TwoHandedWeaponLeft).
[HarmonyPatch]
internal static class ObjectDBPatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static void Awake_Postfix(ObjectDB __instance)
    {
        Refresh(__instance, "ObjectDB.Awake postfix");
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static void CopyOtherDB_Postfix(ObjectDB __instance)
    {
        Refresh(__instance, "ObjectDB.CopyOtherDB postfix");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetAllCraftableWeapons))]
    private static void GetAllCraftableWeapons_Postfix(List<ItemDrop> __result)
    {
        try
        {
            if (__result == null)
            {
                return;
            }
            // Any item that ever was a tower this session: only an applied one can be in the list (IsWeapon).
            for (var i = __result.Count - 1; i >= 0; i--)
            {
                var drop = __result[i];
                if (drop != null && TowerCatalog.SnapshotOfPrefab(drop.gameObject) != null)
                {
                    __result.RemoveAt(i);
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("ObjectDB.GetAllCraftableWeapons postfix", e);
        }
    }

    private static void Refresh(ObjectDB db, string site)
    {
        try
        {
            if (db == null || db.m_items == null || db.m_items.Count == 0)
            {
                return;
            }
            TowerSync.OnObjectDB(db);
            TowerGuard.WarnOnce();
        }
        catch (Exception e)
        {
            PatchGuard.Report(site, e);
        }
    }
}
