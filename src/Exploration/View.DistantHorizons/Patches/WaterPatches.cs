using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod.Patches;

// Me = while my far sea get painted, game's 4 km distant water plane would sit inside it, same height, blended
// twice. For that plane (_IsLod water) _VisibleMaxDistance = fade-IN radius: huge value make it see-through and
// depth-silent. Only that plane, never per-zone water tiles. Not painting (sea off, feature off): me do nothing,
// so game's own value come back (LodTerrainManager.SetSeaPainting ask game to re-apply).
[HarmonyPatch(typeof(Water), nameof(Water.ApplySettings))]
internal static class WaterPatches
{
    private const float OutOfReach = 1000000f;
    private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    private static readonly int IsLodId = Shader.PropertyToID("_IsLod");
    private static readonly int VisibleMaxDistanceId = Shader.PropertyToID("_VisibleMaxDistance");

    [HarmonyPostfix]
    private static void Postfix(Water __instance)
    {
        var manager = LodTerrainManager.Instance;
        if (manager == null || !manager.SeaPainting)
        {
            return;
        }
        try
        {
            var r = __instance.GetComponent<MeshRenderer>();
            var m = r != null ? r.sharedMaterial : null;
            if (m == null || !m.HasProperty(IsLodId) || m.GetFloat(IsLodId) < 0.5f)
            {
                return; // only the 4 km LOD plane
            }
            r.GetPropertyBlock(Block);
            Block.SetFloat(VisibleMaxDistanceId, OutOfReach);
            r.SetPropertyBlock(Block);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Water.ApplySettings postfix", e);
        }
    }
}
