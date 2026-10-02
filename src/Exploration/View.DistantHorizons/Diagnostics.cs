using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = one-shot dump of facts the distant-object design lean on (environments, vegetation rules,
// prefab LOD structure, what object data this client hold). Me write it to BepInEx log.
internal static class Diagnostics
{
    private static bool _dumped;

    public static void DumpOnce(bool force = false)
    {
        if (_dumped && !force) return;
        _dumped = true;
        Run("environments", () => Log.Info(DescribeEnvironments()));
        Run("terrain materials", () => Log.Info(DescribeTerrainMaterials()));
        Run("object data", () => Log.Info(DescribeObjectData()));
        Run("vegetation", () => Log.Info(DescribeVegetation()));
    }

    // ------------------------------------------------------------------ terrain materials

    public static string DescribeTerrainMaterials()
    {
        var sb = new StringBuilder();
        Material lod = TerrainLink.CurrentMaterial;
        sb.Append("Distant terrain material: ").Append(lod != null ? DescribeMaterial(lod) : "none").AppendLine();
        Material zone = null;
        if (ZoneSystem.instance != null && ZoneSystem.instance.m_zonePrefab != null)
        {
            Heightmap hm = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>(true);
            if (hm != null)
            {
                zone = hm.m_material;
                sb.Append("Zone heightmap: width=").Append(hm.m_width).Append(" scale=").Append(hm.m_scale).AppendLine();
            }
        }
        sb.Append("Zone terrain material: ").Append(zone != null ? DescribeMaterial(zone) : "none");
        return sb.ToString();
    }

    // Shader name + every property with its value now.
    public static string DescribeMaterial(Material m)
    {
        if (m == null || m.shader == null) return "none";
        Shader s = m.shader;
        var sb = new StringBuilder();
        sb.Append(m.name).Append(" (").Append(s.name).Append(") [");
        int n = s.GetPropertyCount();
        for (int i = 0; i < n; i++)
        {
            string name = s.GetPropertyName(i);
            var type = s.GetPropertyType(i);
            if (i > 0) sb.Append(", ");
            sb.Append(name).Append(':').Append(type);
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                {
                    Texture t = m.GetTexture(name);
                    sb.Append('=').Append(t != null ? t.name + "(" + t.width + "x" + t.height + ")" : "null");
                    Vector2 sc = m.GetTextureScale(name);
                    if (sc != Vector2.one) sb.Append(" scale=").Append(sc);
                    break;
                }
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    sb.Append('=').Append(m.GetColor(name));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    sb.Append('=').Append(m.GetFloat(name).ToString("0.####"));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    sb.Append('=').Append(m.GetVector(name));
                    break;
            }
        }
        sb.Append("] keywords=").Append(string.Join(" ", m.shaderKeywords)).Append(" queue=").Append(m.renderQueue);
        return sb.ToString();
    }

    private static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Warning($"Diagnostics ({what}) failed: {e.GetType().Name}: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ environments

    public static string DescribeEnvironments()
    {
        EnvMan env = EnvMan.instance;
        if (env == null) return "Environments: EnvMan not available";
        var sb = new StringBuilder();
        EnvSetup current = env.GetCurrentEnvironment();
        sb.Append("Environments (").Append(env.m_environments.Count).Append("), current=")
          .Append(current != null ? current.m_name : "?")
          .Append(" fogDensity=").Append(RenderSettings.fogDensity.ToString("0.#####"))
          .Append(" fogMode=").Append(RenderSettings.fogMode).AppendLine();
        foreach (EnvSetup e in env.m_environments)
        {
            sb.Append("  ").Append(e.m_name)
              .Append(" day=").Append(e.m_fogDensityDay.ToString("0.#####"))
              .Append(" night=").Append(e.m_fogDensityNight.ToString("0.#####"))
              .Append(" morning=").Append(e.m_fogDensityMorning.ToString("0.#####"))
              .Append(" evening=").Append(e.m_fogDensityEvening.ToString("0.#####"))
              .Append(e.m_isWet ? " wet" : "")
              .Append(e.m_isFreezing ? " freezing" : "")
              .Append(e.m_isCold ? " cold" : "")
              .Append(e.m_alwaysDark ? " dark" : "")
              .Append(e.m_default ? " default" : "")
              .AppendLine();
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ object data this client hold

    public static string DescribeObjectData()
    {
        ZDOMan zdoMan = ZDOMan.instance;
        ZNet znet = ZNet.instance;
        if (zdoMan == null || znet == null) return "Object data: ZDOMan/ZNet not available";
        var sb = new StringBuilder();
        sb.Append("Object data: server=").Append(znet.IsServer())
          .Append(" dedicated=").Append(znet.IsDedicated())
          .Append(" objects=").Append(zdoMan.NrOfObjects());

        var bySector = zdoMan.m_objectsBySector;
        if (bySector != null)
        {
            int sectors = 0;
            long count = 0;
            foreach (List<ZDO> list in bySector)
            {
                if (list == null || list.Count == 0) continue;
                sectors++;
                count += list.Count;
            }
            sb.Append(" sectorsWithObjects=").Append(sectors).Append(" objectsInSectors=").Append(count);
        }

        var byId = zdoMan.m_objectsByID;
        if (byId != null)
        {
            var hist = new Dictionary<int, int>();
            foreach (ZDO zdo in byId.Values)
            {
                int p = zdo.GetPrefab();
                hist.TryGetValue(p, out int n);
                hist[p] = n + 1;
            }
            var top = new List<KeyValuePair<int, int>>(hist);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine().Append("  top prefabs:");
            int shown = 0;
            foreach (KeyValuePair<int, int> kv in top)
            {
                if (shown++ >= 30) break;
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(kv.Key) : null;
                sb.Append(' ').Append(prefab != null ? prefab.name : kv.Key.ToString()).Append(':').Append(kv.Value);
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ vegetation rules and prefab structure

    public static string DescribeVegetation()
    {
        ZoneSystem zs = ZoneSystem.instance;
        if (zs == null) return "Vegetation: ZoneSystem not available";
        var sb = new StringBuilder();
        sb.Append("Vegetation rules: ").Append(zs.m_vegetation.Count).AppendLine();
        var prefabs = new List<GameObject>();
        foreach (ZoneSystem.ZoneVegetation v in zs.m_vegetation)
        {
            if (v.m_prefab == null) continue;
            sb.Append("  ").Append(v.m_name).Append('|').Append(v.m_prefab.name)
              .Append("|biome=").Append(v.m_biome).Append("|area=").Append(v.m_biomeArea)
              .Append("|min=").Append(v.m_min).Append("|max=").Append(v.m_max)
              .Append("|group=").Append(v.m_groupSizeMin).Append('-').Append(v.m_groupSizeMax).Append("|r=").Append(v.m_groupRadius)
              .Append("|forest=").Append(v.m_forestTresholdMin).Append('-').Append(v.m_forestTresholdMax).Append("|inForest=").Append(v.m_inForest)
              .Append("|alt=").Append(v.m_minAltitude).Append("..").Append(v.m_maxAltitude)
              .Append("|tilt=").Append(v.m_minTilt).Append("..").Append(v.m_maxTilt)
              .Append("|scale=").Append(v.m_scaleMin).Append('-').Append(v.m_scaleMax)
              .Append("|block=").Append(v.m_blockCheck).Append("|enable=").Append(v.m_enable)
              .AppendLine();
            if (!prefabs.Contains(v.m_prefab)) prefabs.Add(v.m_prefab);
        }
        sb.Append("Vegetation prefabs (").Append(prefabs.Count).Append("):").AppendLine();
        foreach (GameObject p in prefabs)
        {
            sb.Append("  ");
            try
            {
                DescribePrefab(p, sb);
            }
            catch (Exception e)
            {
                sb.Append(p.name).Append(" (describe failed: ").Append(e.Message).Append(')');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void DescribePrefab(GameObject prefab, StringBuilder sb)
    {
        sb.Append(prefab.name);
        ZNetView nv = prefab.GetComponent<ZNetView>();
        if (nv != null) sb.Append(" distant=").Append(nv.m_distant);
        LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>(true);
        if (lodGroup != null)
        {
            LOD[] lods = lodGroup.GetLODs();
            sb.Append(" lods=").Append(lods.Length).Append(" size=").Append(lodGroup.size.ToString("0.#"));
            for (int i = 0; i < lods.Length; i++)
            {
                sb.Append(" | L").Append(i).Append(" h=").Append(lods[i].screenRelativeTransitionHeight.ToString("0.###")).Append(' ');
                DescribeRenderers(lods[i].renderers, sb);
            }
        }
        else
        {
            sb.Append(" noLodGroup ");
            DescribeRenderers(prefab.GetComponentsInChildren<Renderer>(true), sb);
        }
    }

    private static void DescribeRenderers(Renderer[] renderers, StringBuilder sb)
    {
        int verts = 0;
        long tris = 0;
        bool anyUnreadable = false;
        var types = new HashSet<string>();
        var mats = new HashSet<string>();
        int count = 0;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            count++;
            types.Add(r.GetType().Name);
            Mesh mesh = null;
            if (r is MeshRenderer)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh != null)
            {
                verts += mesh.vertexCount;
                if (!mesh.isReadable) anyUnreadable = true;
                try
                {
                    for (int s = 0; s < mesh.subMeshCount; s++) tris += mesh.GetIndexCount(s) / 3;
                }
                catch (Exception)
                {
                    tris = -1;
                }
            }
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;
                mats.Add(m.name + "(" + (m.shader != null ? m.shader.name : "?") + (m.enableInstancing ? ",inst" : "") + ")");
            }
        }
        sb.Append("r=").Append(count).Append(' ').Append(string.Join("+", types))
          .Append(" v=").Append(verts).Append(" t=").Append(tris)
          .Append(anyUnreadable ? " UNREADABLE" : " readable")
          .Append(" m=").Append(string.Join(",", mats));
    }
}
