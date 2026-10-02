using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Sea only exist near player: game give each loaded zone a water tile and add one 4 km plane that follow player,
// and its water shader multiply alpha by 1 - saturate((distanceXZ to camera - 300) / 500), with literal constants,
// so nothing using that shader can be seen past 800 m however it set up. Me own mesh, material and pass that paint
// sea beyond that, in same shrunk space as far terrain, where those 300 and 800 m never arrive.
internal sealed class FarWater
{
    private const int AngularSegments = 128;
    private const int RadialSegments = 48;
    // Sheet split into radial bands, each drawn with own shrink factor. One factor for whole ring = set by its
    // farthest point, about 1/128, and every distance shader work with (fog, normal map tiling, water depth) would
    // be wrong by that much everywhere, even a few hundred metres out.
    private const int Bands = 6;
    // Sheet follow camera in steps this big so its vertices never crawl.
    private const float SnapStep = 16f;
    // ZoneSystem.m_waterLevel (30, also hard-coded in Heightmap.UpdateCornerDepths); world mods can move it.
    public static float WaterLevel => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
    // Shader discard LOD water past this radius from world origin: the world edge, WorldRadius.
    public static float WorldEdge => Plugin.Cfg != null ? Plugin.Cfg.WorldRadius.Value : 10500f;

    private Mesh _mesh;
    private Material _material;
    private int _pass = -1;
    private float _builtInner = -1f;
    private float _builtOuter = -1f;

    private readonly float[] _bandOuter = new float[Bands];

    public Mesh Mesh => _mesh;
    public int BandCount => Bands;
    // Outer radius of a band: it decide band's shrink factor.
    public float BandOuter(int i) => _bandOuter[Mathf.Clamp(i, 0, Bands - 1)];
    public Material Material => _material;
    public int Pass => _pass;
    public float Inner { get; private set; }
    public float Outer { get; private set; }
    public Vector3 Center { get; private set; }
    public bool Ready => _mesh != null && _material != null && _pass >= 0;

    // Where game's own water stop mattering: one zone inside its fade, so the two overlap.
    public static float AutoInnerRadius()
    {
        int near = 2;
        if (ZNet.instance != null) near = Mathf.Max(1, ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance);
        return Mathf.Max(64f, near * 64f - 64f);
    }

    // The 4 km plane that follow player, not a zone tile: it = the one built to be seen at distance.
    private static Material FindLodWaterMaterial()
    {
        Material fallback = null;
        for (int i = 0; i < Water.Instances.Count; i++)
        {
            Water w = Water.Instances[i];
            if (w == null) continue;
            MeshRenderer r = w.GetComponent<MeshRenderer>();
            Material m = r != null ? r.sharedMaterial : null;
            if (m == null) continue;
            if (m.HasProperty("_IsLod") && m.GetFloat("_IsLod") >= 0.5f) return m;
            if (m.name != null && m.name.IndexOf("lod", System.StringComparison.OrdinalIgnoreCase) >= 0) return m;
            if (fallback == null) fallback = m;
        }
        return fallback;
    }

    // Four of the six passes called FORWARD: a depth-only one, an additive one, and the visible one. Names no can
    // tell them apart, so me pick pass by its LightMode tag: that = what Unity itself dispatch on.
    private static int FindVisibleForwardPass(Material m)
    {
        Shader sh = m.shader;
        int sub = 0;
        for (int i = 0; i < sh.subshaderCount; i++)
            if (sh.GetPassCountInSubshader(i) == m.passCount) { sub = i; break; }

        var lightMode = new UnityEngine.Rendering.ShaderTagId("LightMode");
        int basePass = -1, lastForward = -1;
        var table = new System.Text.StringBuilder();
        for (int i = 0; i < m.passCount; i++)
        {
            string name = m.GetPassName(i);
            string mode = "?";
            try
            {
                UnityEngine.Rendering.ShaderTagId tag = sh.FindPassTagValue(sub, i, lightMode);
                if (!string.IsNullOrEmpty(tag.name)) mode = tag.name;
            }
            catch (System.Exception)
            {
                // older shader: no tag information
            }
            table.Append(i).Append(':').Append(name).Append('/').Append(mode).Append(' ');
            if (string.Equals(mode, "ForwardBase", System.StringComparison.OrdinalIgnoreCase)) basePass = i;
            if (string.Equals(name, "FORWARD", System.StringComparison.OrdinalIgnoreCase)) lastForward = i;
        }
        Log.Info("Far water passes (subshader " + sub + "): " + table);
        // No tags: visible pass = the one before additive tail, not the last.
        return basePass >= 0 ? basePass : (lastForward > 0 ? lastForward - 1 : lastForward);
    }

    // Me build what missing and place sheet. Return true when something to draw.
    public bool Prepare(Camera cam)
    {
        DHConfig cfg = Plugin.Cfg;
        if (cfg == null || !cfg.FarWater.Value || cam == null || ZNet.instance == null) return false;

        // Game's own material, used direct, not cloned: game retint water per environment and biome, and clone taken
        // once at startup drift away from near water as weather change. Our own settings ride on per-draw property
        // block, so nothing here leak into game's rendering.
        Material source = FindLodWaterMaterial();
        if (source == null) return false; // no water in world yet
        if (!ReferenceEquals(source, _material))
        {
            _material = source;
            _pass = FindVisibleForwardPass(_material);
            Log.Info($"Far water uses {source.name} ({source.shader.name}); " +
                               $"visible pass {_pass} of {_material.passCount} ('{(_pass >= 0 ? _material.GetPassName(_pass) : "none")}').");
            if (_pass < 0) Log.Warning("Far water has no usable forward pass; the sea will not be drawn.");
        }
        if (_pass < 0) return false;

        float inner = cfg.FarWaterInnerRadius.Value > 0f ? cfg.FarWaterInnerRadius.Value : AutoInnerRadius();
        float outer = Mathf.Max(inner + 256f, Mathf.Min(cfg.ViewDistance.Value, WorldEdge * 1.5f));
        EnsureMesh(inner, outer);

        Vector3 p = cam.transform.position;
        Center = new Vector3(Mathf.Round(p.x / SnapStep) * SnapStep, WaterLevel, Mathf.Round(p.z / SnapStep) * SnapStep);
        return Ready;
    }

    private void EnsureMesh(float inner, float outer)
    {
        if (_mesh != null && Mathf.Approximately(inner, _builtInner) && Mathf.Approximately(outer, _builtOuter)) return;
        _builtInner = inner;
        _builtOuter = outer;
        Inner = inner;
        Outer = outer;
        if (_mesh != null) Object.Destroy(_mesh);
        _mesh = BuildRing(inner, outer);
        int bandSegments = RadialSegments / Bands;
        float bandRatio = outer / Mathf.Max(inner, 1f);
        for (int b = 0; b < Bands; b++)
            _bandOuter[b] = inner * Mathf.Pow(bandRatio, (b + 1) * bandSegments / (float)RadialSegments);
        Log.Info($"Far water sheet built: {inner:0} m to {outer:0} m ({_mesh.vertexCount} verts).");
    }

    // Flat polar ring, radial steps grow geometrically. Wound so faces point up (shader cull back faces) and given
    // fixed tangent frame: horizontal plane need that, and recalculating from polar mesh no produce it.
    private static Mesh BuildRing(float inner, float outer)
    {
        int av = AngularSegments + 1;
        int rv = RadialSegments + 1;
        var verts = new Vector3[av * rv];
        var uvs = new Vector2[av * rv];
        var normals = new Vector3[av * rv];
        var tangents = new Vector4[av * rv];
        float ratio = outer / Mathf.Max(inner, 1f);
        for (int r = 0; r < rv; r++)
        {
            float radius = inner * Mathf.Pow(ratio, r / (float)RadialSegments);
            for (int a = 0; a < av; a++)
            {
                float ang = a / (float)AngularSegments * Mathf.PI * 2f;
                int i = r * av + a;
                verts[i] = new Vector3(Mathf.Sin(ang) * radius, 0f, Mathf.Cos(ang) * radius);
                uvs[i] = new Vector2(verts[i].x * 0.01f, verts[i].z * 0.01f);
                normals[i] = Vector3.up;
                tangents[i] = new Vector4(1f, 0f, 0f, 1f);
            }
        }
        var mesh = new Mesh { name = "DH_FarWaterSheet", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.subMeshCount = Bands;
        int perBand = RadialSegments / Bands;
        var tris = new int[perBand * AngularSegments * 6];
        for (int b = 0; b < Bands; b++)
        {
            int t = 0;
            for (int r = b * perBand; r < (b + 1) * perBand; r++)
                for (int a = 0; a < AngularSegments; a++)
                {
                    int i0 = r * av + a, i1 = i0 + 1, i2 = i0 + av, i3 = i2 + 1;
                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            mesh.SetTriangles(tris, b);
        }
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(outer * 2f, 4f, outer * 2f));
        return mesh;
    }

    public void Destroy()
    {
        if (_mesh != null) Object.Destroy(_mesh);
        _mesh = null;
        _material = null; // borrowed from game: me never destroy it here
        _pass = -1;
        _builtInner = -1f;
        _builtOuter = -1f;
    }

    public string Describe()
    {
        int surfaces = 0;
        for (int i = 0; i < Water.Instances.Count; i++)
            if (Water.Instances[i] != null) surfaces++;
        int hidden = 0;
        for (int i = 0; i < Water.Instances.Count; i++)
        {
            Water w = Water.Instances[i];
            MeshRenderer r = w != null ? w.GetComponent<MeshRenderer>() : null;
            Material m = r != null ? r.sharedMaterial : null;
            if (m != null && m.HasProperty("_IsLod") && m.GetFloat("_IsLod") >= 0.5f) hidden++;
        }
        return Ready
            ? $"far water: on, {Inner:0} m to {Outer:0} m, pass {_pass} '{_material.GetPassName(_pass)}', {(_mesh != null ? _mesh.vertexCount : 0)} verts, centre {Center.x:0},{Center.z:0}, {Center.magnitude:0} m from the world origin (edge {WorldEdge:0}); game water surfaces: {surfaces}, of them LOD planes: {hidden}"
            : $"far water: not ready (material {(_material != null ? "ok" : "missing")}, pass {_pass}); game water surfaces: {surfaces}";
    }
}
