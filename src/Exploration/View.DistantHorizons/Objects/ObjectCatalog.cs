using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

internal enum ObjectKind : byte
{
    Skip = 0,
    Tree,
    Bush,
    Rock,
    Piece,
    Log,
    Other,
}

// One placed object, snapshot from game object store (ZDOs pooled: never keep them).
internal struct ObjectInstance
{
    public int PrefabHash;
    public Vector3 Position;
    public Quaternion Rotation;
    public float Scale;
}

// One renderer of prefab far LOD: mesh + submesh + material + transform relative to prefab root.
internal sealed class LodRenderer
{
    public Mesh Mesh;
    public int SubMesh;
    public Material Material;
    public Matrix4x4 Local;
    public int VertexCount;
    // Prefab own renderer (for bake with its full material array).
    public Renderer Source;
}

internal sealed class PrefabInfo
{
    public int Hash;
    public string Name;
    public GameObject Prefab;
    public ObjectKind Kind;
    public bool Distant;
    public bool SyncScale;
    public Vector3 PrefabScale = Vector3.one;

    // Bounds of full-detail LOD in prefab space (root at origin, scale 1).
    public Bounds Bounds;
    public float Height => Bounds.max.y;
    public float Radius => Mathf.Max(Bounds.extents.x, Bounds.extents.z);

    // Lowest LOD renderers with CPU-readable meshes (empty when nothing usable).
    public readonly List<LodRenderer> FarLod = new List<LodRenderer>();
    public int FarLodVertices;
    // Renderers for bake impostor (mid LOD when there is one).
    public readonly List<LodRenderer> BakeLod = new List<LodRenderer>();

    public bool WantsImpostor;
    public int AtlasCell = -1;      // -1 = no bake yet, -2 = bake fail
    public float ImpostorSize;      // side of square card, metres, at scale 1
    public float ImpostorCenterY;   // card centre height above root, at scale 1
}

// Me classify prefabs by hash and pull out what far renderer need from them. All here run on main thread
// (Unity objects), cached per session.
internal static class PrefabCatalog
{
    private static readonly Dictionary<int, PrefabInfo> s_infos = new Dictionary<int, PrefabInfo>();
    private static HashSet<int> s_vegetationHashes;
    private static readonly PrefabInfo s_skip = new PrefabInfo { Kind = ObjectKind.Skip };
    public static readonly int ZoneCtrlHash = "_ZoneCtrl".GetStableHashCode();

    public static int Count => s_infos.Count;

    public static void Clear()
    {
        s_infos.Clear();
        s_vegetationHashes = null;
    }

    private static HashSet<int> VegetationHashes()
    {
        if (s_vegetationHashes != null) return s_vegetationHashes;
        s_vegetationHashes = new HashSet<int>();
        ZoneSystem zs = ZoneSystem.instance;
        if (zs != null)
            foreach (ZoneSystem.ZoneVegetation v in zs.m_vegetation)
                if (v.m_prefab != null) s_vegetationHashes.Add(v.m_prefab.name.GetStableHashCode());
        return s_vegetationHashes;
    }

    public static PrefabInfo Get(int hash)
    {
        if (s_infos.TryGetValue(hash, out PrefabInfo info)) return info;
        info = Build(hash);
        s_infos[hash] = info;
        return info;
    }

    private static PrefabInfo Build(int hash)
    {
        ZNetScene scene = ZNetScene.instance;
        GameObject prefab = scene != null ? scene.GetPrefab(hash) : null;
        if (prefab == null) return s_skip;

        try
        {
            var info = new PrefabInfo { Hash = hash, Name = prefab.name, Prefab = prefab };
            info.Kind = Classify(prefab);
            if (info.Kind == ObjectKind.Skip) return s_skip;

            ZNetView nv = prefab.GetComponent<ZNetView>();
            info.Distant = nv != null && nv.m_distant;
            info.SyncScale = nv != null && nv.m_syncInitialScale;
            info.PrefabScale = prefab.transform.localScale;

            ExtractLods(prefab, info);
            if (info.FarLod.Count == 0 && info.BakeLod.Count == 0) return s_skip;
            // Bounds and card size = root-local space WITHOUT root own scale; each instance uniform scale
            // (synced value, or prefab root scale) go on at draw time.

            // Vegetation entry with no tree/rock component: me pick kind by size.
            if (info.Kind == ObjectKind.Other && info.Height >= 6f) info.Kind = ObjectKind.Tree;
            else if (info.Kind == ObjectKind.Other) info.Kind = ObjectKind.Bush;

            info.WantsImpostor = info.Kind == ObjectKind.Tree;
            float side = Mathf.Max(info.Height, 2f * info.Radius);
            info.ImpostorSize = Mathf.Max(side, 0.5f);
            info.ImpostorCenterY = info.Bounds.center.y;
            return info;
        }
        catch (Exception e)
        {
            Log.Warning($"Prefab {prefab.name} could not be catalogued: {e.GetType().Name}: {e.Message}");
            return s_skip;
        }
    }

    private static ObjectKind Classify(GameObject prefab)
    {
        if (prefab.GetComponent<Character>() != null || prefab.GetComponent<ItemDrop>() != null ||
            prefab.GetComponent<Pickable>() != null || prefab.GetComponent<Plant>() != null ||
            prefab.GetComponent<LocationProxy>() != null || prefab.GetComponent<Fish>() != null ||
            prefab.GetComponent<Ship>() != null || prefab.GetComponent<Vagon>() != null ||
            prefab.GetComponent<Projectile>() != null)
            return ObjectKind.Skip;

        if (prefab.GetComponent<Piece>() != null || prefab.GetComponent<WearNTear>() != null) return ObjectKind.Piece;
        if (prefab.GetComponent<TreeBase>() != null) return ObjectKind.Tree;
        if (prefab.GetComponent<TreeLog>() != null) return ObjectKind.Log;
        if (prefab.GetComponent<MineRock>() != null || prefab.GetComponent<MineRock5>() != null) return ObjectKind.Rock;

        bool vegetation = VegetationHashes().Contains(prefab.name.GetStableHashCode());
        if (prefab.GetComponent<Destructible>() != null)
        {
            if (!vegetation) return ObjectKind.Skip;
            return LooksLikeRock(prefab) ? ObjectKind.Rock : ObjectKind.Other;
        }
        if (!vegetation) return ObjectKind.Skip;
        if (prefab.GetComponentInChildren<MeshRenderer>(true) == null) return ObjectKind.Skip;
        return LooksLikeRock(prefab) ? ObjectKind.Rock : ObjectKind.Other;
    }

    private static bool LooksLikeRock(GameObject prefab)
    {
        foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
        return false;
    }

    private sealed class Scope
    {
        public Transform Root;
        public Matrix4x4 RootInverse;
        public readonly HashSet<Transform> Excluded = new HashSet<Transform>();
    }

    // Subtrees game show only in other states: damaged/wet variants of building pieces and fragment set
    // of old-style mine rocks. Inactive children skipped too, generic.
    private static Scope MakeScope(GameObject prefab)
    {
        var scope = new Scope { Root = prefab.transform, RootInverse = prefab.transform.worldToLocalMatrix };
        WearNTear wnt = prefab.GetComponent<WearNTear>();
        if (wnt != null)
        {
            // Some prefabs reuse intact model for other states (WearNTear guard its toggles with
            // m_worn != m_new); never exclude intact visual or one of its ancestors.
            Transform keep = wnt.m_new != null ? wnt.m_new.transform : null;
            ExcludeState(scope, wnt.m_worn, keep);
            ExcludeState(scope, wnt.m_broken, keep);
            ExcludeState(scope, wnt.m_wet, keep);
        }
        MineRock rock = prefab.GetComponent<MineRock>();
        if (rock != null && rock.m_baseModel != null && rock.m_areaRoot != null) scope.Excluded.Add(rock.m_areaRoot.transform);
        return scope;
    }

    private static void ExcludeState(Scope scope, GameObject state, Transform keep)
    {
        if (state == null) return;
        Transform t = state.transform;
        if (keep != null && keep.IsChildOf(t)) return; // same object as intact visual, or its ancestor
        scope.Excluded.Add(t);
    }

    private static bool IsHidden(Transform t, Scope scope)
    {
        while (t != null && t != scope.Root)
        {
            if (!t.gameObject.activeSelf || scope.Excluded.Contains(t)) return true;
            t = t.parent;
        }
        return false;
    }

    private static void ExtractLods(GameObject prefab, PrefabInfo info)
    {
        Scope scope = MakeScope(prefab);
        LODGroup group = prefab.GetComponentInChildren<LODGroup>(true);
        LOD[] lods = group != null ? group.GetLODs() : null;

        var boundsSet = false;
        var bounds = new Bounds();

        if (lods != null && lods.Length > 0)
        {
            // Full-detail bounds come from LOD0.
            CollectRenderers(lods[0].renderers, scope, null, ref bounds, ref boundsSet);
            // Lowest LOD with something drawable and readable = far mesh...
            for (int i = lods.Length - 1; i >= 0 && info.FarLod.Count == 0; i--)
                CollectRenderers(lods[i].renderers, scope, info.FarLod, ref bounds, ref boundsSet, requireReadable: true);
            // ...and mid LOD (or LOD0) for bake impostor picture.
            int bakeIndex = lods.Length >= 2 ? 1 : 0;
            CollectRenderers(lods[bakeIndex].renderers, scope, info.BakeLod, ref bounds, ref boundsSet);
            if (info.BakeLod.Count == 0) CollectRenderers(lods[0].renderers, scope, info.BakeLod, ref bounds, ref boundsSet);
        }
        else
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            CollectRenderers(renderers, scope, info.FarLod, ref bounds, ref boundsSet, requireReadable: true);
            CollectRenderers(renderers, scope, info.BakeLod, ref bounds, ref boundsSet);
        }

        // Prefab with all visuals in excluded/inactive subtrees: better draw it in some state than not at all.
        if (info.FarLod.Count == 0 && info.BakeLod.Count == 0 && (scope.Excluded.Count > 0))
        {
            scope.Excluded.Clear();
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            CollectRenderers(renderers, scope, info.FarLod, ref bounds, ref boundsSet, requireReadable: true);
            CollectRenderers(renderers, scope, info.BakeLod, ref bounds, ref boundsSet);
        }

        info.Bounds = boundsSet ? bounds : new Bounds(Vector3.up, Vector3.one * 2f);
        info.FarLodVertices = 0;
        foreach (LodRenderer lr in info.FarLod) info.FarLodVertices += lr.VertexCount;
    }

    private static void CollectRenderers(Renderer[] renderers, Scope scope, List<LodRenderer> into,
        ref Bounds bounds, ref bool boundsSet, bool requireReadable = false)
    {
        if (renderers == null) return;
        Matrix4x4 rootInverse = scope.RootInverse;
        foreach (Renderer r in renderers)
        {
            if (r == null || !(r is MeshRenderer) || !r.enabled) continue;
            if (IsHidden(r.transform, scope)) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) continue;
            if (requireReadable && !mesh.isReadable) continue;
            Matrix4x4 local = rootInverse * r.transform.localToWorldMatrix;

            Bounds b = TransformBounds(mesh.bounds, local);
            if (!boundsSet)
            {
                bounds = b;
                boundsSet = true;
            }
            else bounds.Encapsulate(b);

            if (into == null) continue;
            Material[] mats = r.sharedMaterials;
            int subs = mesh.subMeshCount;
            for (int s = 0; s < subs; s++)
            {
                Material m = s < mats.Length ? mats[s] : (mats.Length > 0 ? mats[mats.Length - 1] : null);
                if (m == null || m.shader == null) continue;
                if (m.shader.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                into.Add(new LodRenderer
                {
                    Mesh = mesh,
                    SubMesh = s,
                    Material = m,
                    Local = local,
                    VertexCount = mesh.vertexCount,
                    Source = r,
                });
            }
        }
    }

    private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        Vector3 min = b.min, max = b.max;
        var result = new Bounds(m.MultiplyPoint3x4(min), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z);
            result.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return result;
    }
}
