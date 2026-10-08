using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me bake side view of each tree prefab into one atlas texture and own the material that draw the crossed-quad
// cards. Bake = render prefab's own meshes and materials with throw-away camera far below world on private layer,
// under flat white ambient light, so card texture is close to plain albedo and vegetation shader light it again
// at draw time like any other leaf.
//
// Atlas keep mipmaps, but me make them here per cell: colour averaged weighted by coverage (never with transparent
// background) and each level's alpha rescaled so alpha test keep same silhouette coverage at every distance.
// Without that, alpha-tested cards thin out and vanish far away.
internal sealed class ImpostorAtlas
{
    public const int AtlasSize = 1024;
    public const float Cutoff = 0.3f;
    private const float FrameMargin = 1.12f;
    private const int DilatePasses = 6;
    private static readonly Vector3 RigPos = new Vector3(0f, -6000f, 0f);

    private readonly int _cell;
    private readonly int _perRow;
    private readonly int _levels;
    private int _next;
    private bool _dirty;
    private float _dirtySince;

    private RenderTexture _rt;
    private Camera _cam;
    private GameObject _rig;
    private Texture2D _scratch;
    private readonly int _layer;

    public Texture2D Texture { get; private set; }
    public Material Material { get; private set; }
    public int Baked => _next;
    public int Capacity => _perRow * _perRow;
    public int Failed { get; private set; }
    public string ShaderName => Material != null && Material.shader != null ? Material.shader.name : "none";

    public ImpostorAtlas(int cellSize)
    {
        _cell = Mathf.Clamp(cellSize, 32, 512);
        _perRow = AtlasSize / _cell;
        _levels = 1;
        while ((_cell >> _levels) >= 1) _levels++;
        _layer = FindFreeLayer();
        Texture = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, true, false)
        {
            name = "DH_ImpostorAtlas",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
        var clear = new Color32[AtlasSize * AtlasSize];
        Texture.SetPixels32(clear);
        Texture.Apply(true, false);
    }

    // Unnamed layer nothing else use, so bake camera only ever see bake object.
    private static int FindFreeLayer()
    {
        for (int i = 31; i >= 8; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        return -1;
    }

    public void Dispose()
    {
        if (_rt != null)
        {
            _rt.Release();
            UnityEngine.Object.Destroy(_rt);
            _rt = null;
        }
        if (_scratch != null)
        {
            UnityEngine.Object.Destroy(_scratch);
            _scratch = null;
        }
        if (_rig != null)
        {
            UnityEngine.Object.Destroy(_rig);
            _rig = null;
            _cam = null;
        }
        if (Material != null)
        {
            UnityEngine.Object.Destroy(Material);
            Material = null;
        }
        if (Texture != null)
        {
            UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }

    // UV rect of a cell, inset to un-margined picture: bake frame = FrameMargin x card size, so inset make prefab's
    // y = 0 land exactly on card's bottom edge (snapped ground). Inset always at least a few texels: this also keep
    // neighbour cells out of bilinear footprint.
    public Rect CellUv(int cell)
    {
        int cx = cell % _perRow;
        int cy = cell / _perRow;
        float s = (float)_cell / AtlasSize;
        float padPx = Mathf.Max(3f, _cell * (FrameMargin - 1f) / (2f * FrameMargin));
        float pad = padPx / AtlasSize;
        return new Rect(cx * s + pad, cy * s + pad, s - 2f * pad, s - 2f * pad);
    }

    // Upload pending cells, but not on frames still baking (one upload per burst).
    public void ApplyIfDirty(bool bakedThisFrame)
    {
        if (!_dirty || Texture == null) return;
        if (bakedThisFrame && Time.unscaledTime - _dirtySince < 0.5f) return;
        Texture.Apply(false, false); // mips already written per cell
        _dirty = false;
    }

    public void DumpPng(string path)
    {
        if (Texture == null) return;
        byte[] png = Texture.EncodeToPNG();
        System.IO.File.WriteAllBytes(path, png);
    }

    // Shader name plus every property of card material, with texture assignments, for log.
    public string DescribeMaterial()
    {
        if (Material == null || Material.shader == null) return "none";
        Shader s = Material.shader;
        var sb = new System.Text.StringBuilder();
        sb.Append(s.name).Append(" layer=").Append(_layer).Append(" [");
        int n = s.GetPropertyCount();
        for (int i = 0; i < n; i++)
        {
            string name = s.GetPropertyName(i);
            var type = s.GetPropertyType(i);
            if (i > 0) sb.Append(", ");
            sb.Append(name).Append(':').Append(type);
            if (type == ShaderPropertyType.Texture)
            {
                Texture t = Material.GetTexture(name);
                sb.Append('=').Append(t != null ? t.name : "null");
            }
            else if (type == ShaderPropertyType.Color)
                sb.Append('=').Append(Material.GetColor(name));
            else if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                sb.Append('=').Append(Material.GetFloat(name).ToString("0.###"));
        }
        sb.Append("] keywords=").Append(string.Join(" ", Material.shaderKeywords))
          .Append(" queue=").Append(Material.renderQueue);
        return sb.ToString();
    }

    private bool EnsureMaterial(PrefabInfo sample)
    {
        if (Material != null) return true;
        string mode = Plugin.Cfg.ImpostorShader.Value.Trim().ToLowerInvariant();
        Material source = null;
        if (mode != "standard")
        {
            foreach (LodRenderer lr in sample.BakeLod)
                if (lr.Material != null && lr.Material.shader != null && lr.Material.shader.name.IndexOf("Vegetation", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    source = lr.Material;
                    break;
                }
        }

        if (source != null)
        {
            Material = new Material(source) { name = "DH_Impostor" };
            Material.mainTexture = Texture;
            foreach (string p in new[] { "_BumpMap", "_EmissiveTex", "_EmissionMap", "_MetallicGlossMap", "_MossTex", "_DetailTex", "_MaskTex" })
                if (Material.HasProperty(p)) Material.SetTexture(p, null);
            if (Material.HasProperty("_Cutoff")) Material.SetFloat("_Cutoff", Cutoff);
            if (Material.HasProperty("_Color")) Material.SetColor("_Color", Color.white);
            Material.mainTextureScale = Vector2.one;
            Material.mainTextureOffset = Vector2.zero;
            if (!Plugin.Cfg.FarObjectWind.Value)
            {
                // Sway computed relative to object origin; all cards of a tile share one origin.
                foreach (string p in new[] { "_SwayDistance", "_RippleDistance", "_SwaySpeed", "_RippleSpeed", "_PushDistance" })
                    if (Material.HasProperty(p)) Material.SetFloat(p, 0f);
            }
            // Every card normal point up, so snow cover that shader add to upward-facing leaves in snowy weather
            // would paint whole cards white (seen as bleached far forests from mountain). Same for rain.
            if (Material.HasProperty("_AddSnow")) Material.SetFloat("_AddSnow", 0f);
            Material.DisableKeyword("_ADDSNOW_ON");
            if (Material.HasProperty("_AddRain")) Material.SetFloat("_AddRain", 0f);
            Material.DisableKeyword("_ADDRAIN_ON");
            Log.Info($"Impostor material cloned from {source.name} ({source.shader.name}).");
        }
        else
        {
            Shader std = Shader.Find("Standard");
            if (std == null)
            {
                Log.Error("No usable shader for impostor cards (no vegetation material and no Standard shader).");
                return false;
            }
            Material = new Material(std) { name = "DH_Impostor(Standard)" };
            Material.mainTexture = Texture;
            Material.SetFloat("_Mode", 1f);
            Material.EnableKeyword("_ALPHATEST_ON");
            Material.SetFloat("_Cutoff", Cutoff);
            Material.SetFloat("_Glossiness", 0f);
            Material.renderQueue = 2450;
            Log.Info("Impostor material uses the Standard shader in cutout mode.");
        }
        Material.enableInstancing = false;
        return true;
    }

    // Me bake prefab into free cell. Return false (and remember it) when me cannot.
    public bool TryBake(PrefabInfo info)
    {
        if (info.AtlasCell >= 0) return true;
        if (info.AtlasCell == -2) return false;
        if (_next >= Capacity || info.BakeLod.Count == 0 || !EnsureMaterial(info))
        {
            info.AtlasCell = -2;
            Failed++;
            return false;
        }
        try
        {
            int cell = _next;
            if (!Render(info, cell))
            {
                info.AtlasCell = -2;
                Failed++;
                return false;
            }
            info.AtlasCell = cell;
            _next++;
            if (!_dirty) _dirtySince = Time.unscaledTime;
            _dirty = true;
            if (Plugin.Cfg.DebugLogging.Value)
                Log.Info($"Baked impostor {info.Name} into cell {cell} (size {info.ImpostorSize:0.#} m, centre {info.ImpostorCenterY:0.#} m).");
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Impostor bake failed for {info.Name}: {e.GetType().Name}: {e.Message}");
            info.AtlasCell = -2;
            Failed++;
            return false;
        }
    }

    private void EnsureRig()
    {
        if (_cam != null && _rt != null) return;
        _rig = new GameObject("DH_ImpostorBakeRig");
        _rig.transform.position = RigPos;
        var camGo = new GameObject("cam");
        camGo.transform.SetParent(_rig.transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.enabled = false;
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 500f;
        _cam.allowHDR = false;
        _cam.allowMSAA = false;
        _cam.useOcclusionCulling = false;
        _cam.cullingMask = _layer >= 0 ? 1 << _layer : ~0;
        _cam.renderingPath = RenderingPath.Forward;
        _rt = new RenderTexture(_cell, _cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1, useMipMap = false, name = "DH_ImpostorRT" };
        _rt.Create();
        _cam.targetTexture = _rt;
        _scratch = new Texture2D(_cell, _cell, TextureFormat.RGBA32, false, false) { name = "DH_ImpostorScratch" };
    }

    private static void SetLocal(Transform t, Matrix4x4 m)
    {
        t.localPosition = m.GetColumn(3);
        t.localRotation = m.rotation;
        t.localScale = m.lossyScale;
    }

    private Color[] ReadBack()
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = _rt;
        _scratch.ReadPixels(new Rect(0, 0, _cell, _cell), 0, 0, false);
        RenderTexture.active = previous;
        return _scratch.GetPixels();
    }

    private bool Render(PrefabInfo info, int cell)
    {
        EnsureRig();

        var go = new GameObject("DH_bake_" + info.Name);
        go.transform.position = RigPos;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one; // bounds and card size live in unscaled root space
        if (_layer >= 0) go.layer = _layer;
        var seen = new HashSet<Renderer>();
        foreach (LodRenderer lr in info.BakeLod)
        {
            if (lr.Source == null || !seen.Add(lr.Source)) continue;
            var child = new GameObject("r");
            child.transform.SetParent(go.transform, false);
            if (_layer >= 0) child.layer = _layer;
            SetLocal(child.transform, lr.Local);
            child.AddComponent<MeshFilter>().sharedMesh = lr.Mesh;
            MeshRenderer mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterials = lr.Source.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        float side = info.ImpostorSize;
        Vector3 centre = RigPos + new Vector3(info.Bounds.center.x, info.ImpostorCenterY, info.Bounds.center.z);
        _cam.orthographicSize = side * 0.5f * FrameMargin;
        _cam.transform.rotation = Quaternion.identity; // look along +Z
        _cam.transform.position = centre + new Vector3(0f, 0f, -(info.Radius + 100f));
        _cam.farClipPlane = info.Radius * 2f + 200f;

        // Game's shaders light with own globals (_SunColor, _AmbientColor) as well as Unity's light, so me override
        // both for bake: no sun, flat ambient. EnvMan rewrite globals next frame anyway.
        AmbientMode ambientMode = RenderSettings.ambientMode;
        Color ambient = RenderSettings.ambientLight;
        bool fog = RenderSettings.fog;
        Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
        float sunIntensity = sun != null ? sun.intensity : 0f;
        Color sunColor = Shader.GetGlobalColor("_SunColor");
        Color ambientColor = Shader.GetGlobalColor("_AmbientColor");
        float brightness = Plugin.Cfg.V(Plugin.Cfg.ImpostorBakeBrightness);
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * brightness;
            RenderSettings.fog = false;
            if (sun != null) sun.intensity = 0f;
            Shader.SetGlobalColor("_SunColor", Color.black);
            Shader.SetGlobalColor("_AmbientColor", Color.white * brightness);

            // Two passes with different backgrounds: where they differ, background show through, so coverage no
            // depend on whether shader write meaningful alpha.
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.Render();
            Color[] onBlack = ReadBack();
            _cam.backgroundColor = new Color(1f, 1f, 1f, 1f);
            _cam.Render();
            Color[] onWhite = ReadBack();

            Color[] px = Compose(onBlack, onWhite, out int opaque);
            if (opaque < px.Length / 400)
            {
                Log.Warning($"Impostor bake of {info.Name} produced an empty card ({opaque} opaque pixels).");
                return false;
            }
            WriteCell(cell, px);
            return true;
        }
        finally
        {
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog;
            if (sun != null) sun.intensity = sunIntensity;
            Shader.SetGlobalColor("_SunColor", sunColor);
            Shader.SetGlobalColor("_AmbientColor", ambientColor);
            go.SetActive(false); // unregister renderers now; Destroy alone = deferred to end of frame
            UnityEngine.Object.Destroy(go);
        }
    }

    // Coverage from two backgrounds, un-premultiplied colour, and colour dilated into transparent texels.
    private Color[] Compose(Color[] onBlack, Color[] onWhite, out int opaque)
    {
        int n = onBlack.Length;
        var px = new Color[n];
        var solid = new bool[n];
        opaque = 0;
        for (int i = 0; i < n; i++)
        {
            Color a = onBlack[i], b = onWhite[i];
            float diff = (Mathf.Abs(b.r - a.r) + Mathf.Abs(b.g - a.g) + Mathf.Abs(b.b - a.b)) / 3f;
            float cov = Mathf.Clamp01(1f - diff);
            if (cov < 0.05f)
            {
                px[i] = new Color(0f, 0f, 0f, 0f);
                continue;
            }
            // Black pass hold colour * coverage for partially covered texels.
            float inv = 1f / Mathf.Max(cov, 0.05f);
            px[i] = new Color(Mathf.Clamp01(a.r * inv), Mathf.Clamp01(a.g * inv), Mathf.Clamp01(a.b * inv), cov);
            solid[i] = true;
            if (cov >= Cutoff) opaque++;
        }

        // Me dilate colour outward so bilinear filtering and mip averaging never mix in black.
        int w = _cell;
        var nextSolid = new bool[n];
        for (int pass = 0; pass < DilatePasses; pass++)
        {
            Array.Copy(solid, nextSolid, n);
            for (int y = 0; y < w; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (solid[i]) continue;
                    float r = 0f, g = 0f, bl = 0f;
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= w) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            int j = yy * w + xx;
                            if (!solid[j]) continue;
                            r += px[j].r;
                            g += px[j].g;
                            bl += px[j].b;
                            count++;
                        }
                    }
                    if (count == 0) continue;
                    px[i] = new Color(r / count, g / count, bl / count, 0f);
                    nextSolid[i] = true;
                }
            }
            Array.Copy(nextSolid, solid, n);
        }
        return px;
    }

    // Me write level 0 and coverage-preserving mip chain for one cell.
    private void WriteCell(int cell, Color[] level0)
    {
        int cx = cell % _perRow, cy = cell / _perRow;
        Texture.SetPixels(cx * _cell, cy * _cell, _cell, _cell, level0, 0);

        int covered0 = 0;
        for (int i = 0; i < level0.Length; i++) if (level0[i].a >= Cutoff) covered0++;
        float coverage0 = (float)covered0 / level0.Length;

        Color[] prev = level0;
        int size = _cell;
        for (int level = 1; level < _levels; level++)
        {
            int half = size / 2;
            if (half < 1) break;
            var cur = new Color[half * half];
            for (int y = 0; y < half; y++)
            {
                for (int x = 0; x < half; x++)
                {
                    Color c00 = prev[(2 * y) * size + 2 * x];
                    Color c10 = prev[(2 * y) * size + 2 * x + 1];
                    Color c01 = prev[(2 * y + 1) * size + 2 * x];
                    Color c11 = prev[(2 * y + 1) * size + 2 * x + 1];
                    float sumA = c00.a + c10.a + c01.a + c11.a;
                    Color rgb;
                    if (sumA > 0.0001f)
                        rgb = (c00 * c00.a + c10 * c10.a + c01 * c01.a + c11 * c11.a) / sumA;
                    else
                        rgb = (c00 + c10 + c01 + c11) * 0.25f; // dilated colours, never background
                    cur[y * half + x] = new Color(rgb.r, rgb.g, rgb.b, sumA * 0.25f);
                }
            }
            // Rescale alpha so alpha test keep same fraction of card at this level.
            if (half >= 2 && coverage0 > 0f)
            {
                float lo = 1f, hi = 16f;
                for (int iter = 0; iter < 12; iter++)
                {
                    float mid = (lo + hi) * 0.5f;
                    int covered = 0;
                    for (int i = 0; i < cur.Length; i++) if (Mathf.Min(cur[i].a * mid, 1f) >= Cutoff) covered++;
                    if ((float)covered / cur.Length < coverage0) lo = mid; else hi = mid;
                }
                float scale = (lo + hi) * 0.5f;
                if (scale > 1.001f)
                    for (int i = 0; i < cur.Length; i++) cur[i].a = Mathf.Min(cur[i].a * scale, 1f);
            }
            Texture.SetPixels((cx * _cell) >> level, (cy * _cell) >> level, half, half, cur, level);
            prev = cur;
            size = half;
        }
    }
}
