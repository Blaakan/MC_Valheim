using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = tag what the areas spawn (design 2.3, 2.5, 2.6). Awake run inside Instantiate, inside vanilla Spawn, inside the
// spawn calls AreaSpawns make (Tagging: Jotun entries; BandTagging: a nature band). ZNetView.Awake run before (vanilla
// Character.Awake already read the ZDO), so the new creature has its ZDO.
//   Jotun: cell in HeldCells.TagKey (the server count the living ones of each area after Kall), noted for the
//          after-Kall burst (AreaSpawns.SpawnedInto).
//   Band:  AreaSpawns.BandKey = 1 (band cap counts them).
// Every other Awake: two bool reads.
[HarmonyPatch(typeof(Character))]
internal static class CharacterPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Character.Awake))]
    private static void Awake_Postfix(Character __instance)
    {
        if (!AreaSpawns.Tagging && !AreaSpawns.BandTagging)
        {
            return;
        }
        try
        {
            var nview = __instance.m_nview;
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null)
            {
                return;
            }
            var prefab = zdo.GetPrefab();
            if (AreaSpawns.Tagging && Hostility.IsArmy(prefab))
            {
                var cell = WorldState.CellAt(__instance.transform.position);
                zdo.Set(HeldCells.TagKey, cell);
                AreaSpawns.NoteTagged(cell);
            }
            else if (AreaSpawns.BandTagging && Hostility.IsNature(prefab))
            {
                zdo.Set(AreaSpawns.BandKey, 1);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Awake postfix", e);
        }
    }
}
