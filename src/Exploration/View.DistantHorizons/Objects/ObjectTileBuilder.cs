using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Placement = what to draw for one object inside a tile.
internal struct Placement
{
    public PrefabInfo Info;
    public Vector3 Position;
    public Quaternion Rotation;
    public float Scale;       // uniform synced scale, or PrefabScale.x when prefab no sync scale
    public float ScaleMul;    // extra enlarge for thinned far bands
    public bool Card;         // draw as impostor card, not mesh
}

internal struct BuiltMesh
{
    public Mesh Mesh;
    public Material Material;
    public int Instances;
}

// Me turn list of placements into handful of meshes: one combined mesh per material for real geometry
// (prefabs' lowest LOD, merged with Mesh.CombineMeshes) and one card mesh for all impostors.
//
// Merged meshes have single origin, but vegetation shader sway vertices relative to object's origin: merged forest
// would slide around. Unless far wind enabled, merged geometry use clone of each material with sway and ripple
// amplitudes set to zero.
internal static class ObjectTileBuilder
{
    private static readonly List<Vector3> s_verts = new List<Vector3>();
    private static readonly List<Vector2> s_uvs = new List<Vector2>();
    private static readonly List<Vector3> s_normals = new List<Vector3>();
    private static readonly List<Vector4> s_tangents = new List<Vector4>();
    private static readonly List<Color32> s_colors = new List<Color32>();
    private static readonly List<int> s_indices = new List<int>();
    private static readonly Dictionary<Material, List<CombineInstance>> s_groups = new Dictionary<Material, List<CombineInstance>>();
    private static readonly Dictionary<Material, Material> s_still = new Dictionary<Material, Material>();
    private static readonly string[] s_windProps = { "_SwayDistance", "_RippleDistance", "_SwaySpeed", "_RippleSpeed", "_PushDistance" };

    // Material clone with wind animation off (cached per source material).
    public static Material StillMaterial(Material source)
    {
        if (source == null) return null;
        if (s_still.TryGetValue(source, out Material still) && still != null) return still;
        bool any = false;
        foreach (string p in s_windProps) if (source.HasProperty(p)) { any = true; break; }
        if (!any)
        {
            s_still[source] = source;
            return source;
        }
        still = new Material(source) { name = source.name + " (still)" };
        foreach (string p in s_windProps) if (still.HasProperty(p)) still.SetFloat(p, 0f);
        s_still[source] = still;
        return still;
    }

    public static void ClearMaterials()
    {
        foreach (KeyValuePair<Material, Material> kv in s_still)
            if (kv.Value != null && kv.Value != kv.Key) Object.Destroy(kv.Value);
        s_still.Clear();
    }

    public static void Build(List<Placement> items, ImpostorAtlas atlas, int maxVertsPerMesh, List<BuiltMesh> output, bool wind)
    {
        BuildMeshes(items, maxVertsPerMesh, output, wind);
        BuildCards(items, atlas, output);
    }

    private static void BuildMeshes(List<Placement> items, int maxVertsPerMesh, List<BuiltMesh> output, bool wind)
    {
        s_groups.Clear();
        foreach (Placement p in items)
        {
            if (p.Card) continue;
            Vector3 scaleVec = p.Info.SyncScale ? Vector3.one * p.Scale : p.Info.PrefabScale;
            Matrix4x4 world = Matrix4x4.TRS(p.Position, p.Rotation, scaleVec * p.ScaleMul);
            foreach (LodRenderer lr in p.Info.FarLod)
            {
                Material mat = wind ? lr.Material : StillMaterial(lr.Material);
                if (!s_groups.TryGetValue(mat, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>();
                    s_groups[mat] = list;
                }
                list.Add(new CombineInstance { mesh = lr.Mesh, subMeshIndex = lr.SubMesh, transform = world * lr.Local });
            }
        }

        foreach (KeyValuePair<Material, List<CombineInstance>> kv in s_groups)
        {
            List<CombineInstance> all = kv.Value;
            int start = 0;
            while (start < all.Count)
            {
                int verts = 0;
                int end = start;
                while (end < all.Count)
                {
                    int v = all[end].mesh.vertexCount;
                    if (end > start && verts + v > maxVertsPerMesh) break;
                    verts += v;
                    end++;
                }
                CombineInstance[] chunk = all.GetRange(start, end - start).ToArray();
                var mesh = new Mesh { name = "DH_objects_" + kv.Key.name };
                if (verts > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(chunk, true, true, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                output.Add(new BuiltMesh { Mesh = mesh, Material = kv.Key, Instances = chunk.Length });
                start = end;
            }
        }
        s_groups.Clear();
    }

    private static void BuildCards(List<Placement> items, ImpostorAtlas atlas, List<BuiltMesh> output)
    {
        if (atlas == null || atlas.Material == null) return;
        s_verts.Clear();
        s_uvs.Clear();
        s_normals.Clear();
        s_tangents.Clear();
        s_colors.Clear();
        s_indices.Clear();

        int cards = 0;
        var bottom = new Color32(0, 0, 0, 0);       // no sway at trunk base...
        var top = new Color32(255, 255, 255, 255);  // ...full sway at crown, like painted tree meshes
        var tangent = new Vector4(1f, 0f, 0f, -1f);
        foreach (Placement p in items)
        {
            if (!p.Card || p.Info.AtlasCell < 0) continue;
            Rect uv = atlas.CellUv(p.Info.AtlasCell);
            float size = p.Info.ImpostorSize * p.Scale * p.ScaleMul;
            float half = size * 0.5f;
            float centreY = p.Position.y + p.Info.ImpostorCenterY * p.Scale * p.ScaleMul;
            float yaw = p.Rotation.eulerAngles.y * Mathf.Deg2Rad;

            for (int q = 0; q < 2; q++)
            {
                float a = yaw + q * (Mathf.PI * 0.5f);
                var dir = new Vector3(Mathf.Cos(a) * half, 0f, Mathf.Sin(a) * half);
                int b = s_verts.Count;
                s_verts.Add(new Vector3(p.Position.x - dir.x, centreY - half, p.Position.z - dir.z));
                s_verts.Add(new Vector3(p.Position.x + dir.x, centreY - half, p.Position.z + dir.z));
                s_verts.Add(new Vector3(p.Position.x + dir.x, centreY + half, p.Position.z + dir.z));
                s_verts.Add(new Vector3(p.Position.x - dir.x, centreY + half, p.Position.z - dir.z));
                s_uvs.Add(new Vector2(uv.xMin, uv.yMin));
                s_uvs.Add(new Vector2(uv.xMax, uv.yMin));
                s_uvs.Add(new Vector2(uv.xMax, uv.yMax));
                s_uvs.Add(new Vector2(uv.xMin, uv.yMax));
                s_colors.Add(bottom);
                s_colors.Add(bottom);
                s_colors.Add(top);
                s_colors.Add(top);
                for (int i = 0; i < 4; i++)
                {
                    s_normals.Add(Vector3.up);
                    s_tangents.Add(tangent);
                }
                // both windings so card visible from either side even with back-face culling
                s_indices.Add(b); s_indices.Add(b + 2); s_indices.Add(b + 1);
                s_indices.Add(b); s_indices.Add(b + 3); s_indices.Add(b + 2);
                s_indices.Add(b); s_indices.Add(b + 1); s_indices.Add(b + 2);
                s_indices.Add(b); s_indices.Add(b + 2); s_indices.Add(b + 3);
            }
            cards++;
        }
        if (cards == 0) return;

        var mesh = new Mesh { name = "DH_cards" };
        if (s_verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(s_verts);
        mesh.SetUVs(0, s_uvs);
        mesh.SetNormals(s_normals);
        mesh.SetTangents(s_tangents);
        mesh.SetColors(s_colors);
        mesh.SetIndices(s_indices, MeshTopology.Triangles, 0, true);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        output.Add(new BuiltMesh { Mesh = mesh, Material = atlas.Material, Instances = cards });

        s_verts.Clear();
        s_uvs.Clear();
        s_normals.Clear();
        s_tangents.Clear();
        s_colors.Clear();
        s_indices.Clear();
    }
}
