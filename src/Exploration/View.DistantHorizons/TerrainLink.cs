using MC.Exploration.ViewDistantHorizonsMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = glue between game TerrainLod (vanilla far ground: 3x3 grid of 800 m heightmaps, lives in game scene) and my
// two managers. Managers sit on TerrainLod object, so they die with the scene at logout.
//   World load (feature on):  TerrainLod.OnEnable prefix -> Attach, vanilla CreateMeshes skipped.
//   Feature on mid-world:     OnActivated -> AttachIfInWorld (TerrainLod.OnEnable already ran: vanilla 3x3 exist,
//                             terrain manager drop them at its first Update).
//   Feature off:              OnDeactivated -> Detach: managers gone now (DestroyImmediate, so a quick off-on in one
//                             frame never find a dying one), vanilla 3x3 back, fog and sea plane back.
internal static class TerrainLink
{
    // Live TerrainLod (null outside a world).
    internal static TerrainLod Current { get; private set; }

    // Vanilla distant-terrain material (null outside a world).
    internal static Material CurrentMaterial => Current != null ? Current.m_material : null;

    // Vanilla 3x3 meshes exist now. Me read truth from TerrainLod, never keep own copy (logout, toggle, quit).
    internal static bool VanillaActive => Current != null && Current.m_heightmaps.Count > 0;

    // TerrainLod.OnEnable prefix. True = let vanilla make its 3x3 (no material: me cannot draw).
    internal static bool OnTerrainLodEnable(TerrainLod tl)
    {
        Attach(tl);
        if (tl.m_material == null)
        {
            Log.Error("TerrainLod has no material; leaving vanilla distant terrain in place.");
            return true;
        }
        Log.Info("Replacing vanilla TerrainLod with the Distant Horizons quadtree.");
        return false;
    }

    // TerrainLod.OnDisable postfix (logout): managers stop now, scene unload destroy them.
    internal static void OnTerrainLodDisable(TerrainLod tl)
    {
        var terrain = tl.GetComponent<LodTerrainManager>();
        if (terrain != null)
        {
            terrain.enabled = false;
        }
        var objects = tl.GetComponent<DistantObjectManager>();
        if (objects != null)
        {
            objects.enabled = false;
        }
        if (Current == tl)
        {
            Current = null;
        }
    }

    // OnActivated: in a world already? Then take over now. Main menu: nothing, OnEnable prefix do it at world load.
    internal static void AttachIfInWorld()
    {
        var tl = Object.FindAnyObjectByType<TerrainLod>();
        if (tl == null || !tl.isActiveAndEnabled)
        {
            return;
        }
        Attach(tl);
        Log.Info("Activated in a world: the far terrain replaces the vanilla distant terrain now.");
    }

    private static void Attach(TerrainLod tl)
    {
        Current = tl;
        var terrain = tl.GetComponent<LodTerrainManager>();
        if (terrain == null)
        {
            terrain = tl.gameObject.AddComponent<LodTerrainManager>();
        }
        terrain.Initialize(tl.m_material);
        terrain.enabled = true;

        var objects = tl.GetComponent<DistantObjectManager>();
        if (objects == null)
        {
            objects = tl.gameObject.AddComponent<DistantObjectManager>();
        }
        objects.enabled = true;
    }

    // Terrain manager call this every frame: vanilla 3x3 on only while my tiles are not wanted (no material, console
    // compare). Never both at once.
    internal static void SetVanillaActive(bool on)
    {
        if (Current == null || on == VanillaActive)
        {
            return;
        }
        if (on)
        {
            RestoreVanillaGrid();
        }
        else
        {
            Current.ResetMeshes();
        }
    }

    // Vanilla TerrainLod.Update keep running while my tiles show: on its empty grid it move its point along with the
    // camera, so new 3x3 made now would wait until camera move 256 m. ResetMeshes on the empty list destroy nothing,
    // only put that point far away: next Update rebuild all nine around the camera at once.
    private static void RestoreVanillaGrid()
    {
        Current.ResetMeshes();
        Current.CreateMeshes();
    }

    // OnDeactivated (patches still on). Also run at game quit: then me touch nothing, scene teardown end it all.
    // Objects first (they read the terrain), then terrain (its OnDisable put back camera buffers, real zones, far
    // clip, sea plane), then vanilla 3x3 and fog.
    internal static void Detach()
    {
        if (Game.instance != null && Game.instance.IsShuttingDown())
        {
            Current = null;
            return;
        }
        Step("objects", RemoveObjects);
        Step("terrain", RemoveTerrain);
        Step("vanilla terrain", () =>
        {
            if (Current != null && Current.isActiveAndEnabled && Current.m_material != null && !VanillaActive)
            {
                RestoreVanillaGrid();
            }
        });
        Step("fog", EnvManPatches.RestoreFog);
    }

    private static void RemoveObjects()
    {
        var manager = DistantObjectManager.Instance;
        if (manager == null && Current != null)
        {
            manager = Current.GetComponent<DistantObjectManager>();
        }
        if (manager != null)
        {
            manager.enabled = false;
            Object.DestroyImmediate(manager);
        }
    }

    private static void RemoveTerrain()
    {
        var manager = LodTerrainManager.Instance;
        if (manager == null && Current != null)
        {
            manager = Current.GetComponent<LodTerrainManager>();
        }
        if (manager != null)
        {
            manager.enabled = false;
            Object.DestroyImmediate(manager);
        }
    }

    private static void Step(string what, System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception e)
        {
            PatchGuard.Report("TerrainLink.Detach " + what, e);
        }
    }
}
