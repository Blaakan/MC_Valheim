using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Exploration.ViewDistantHorizonsMod;

internal enum NodeRole : byte
{
    Leaf,
    Internal,
}

// Me stream far terrain for whole world as quadtree of vanilla distant-LOD Heightmaps.
//
// Vanilla TerrainLod keep fixed 3x3 grid of 800 m tiles (2.4 km) and rebuild all nine at once.
// Me keep distance-driven quadtree instead: tiles near camera small and dense, far tiles big and coarse,
// every tile same vertex count. Heights come from game's own HeightmapBuilder thread, meshes from game's
// Heightmap component, so texturing, fog, shadows and shader's near-camera hide distance work exactly like
// vanilla distant terrain.
//
// Display bookkeeping: set of tiles drawn at ground level = "surface", a cut through quadtree. Surface tile
// swap for its children only once all of them built (refine), children swap for their parent only once
// parent built (merge), so ground never show holes or double-drawn (z-fighting) regions while streaming.
internal sealed class LodTerrainManager : MonoBehaviour
{
    public static LodTerrainManager Instance { get; private set; }

    private const float RebuildDebounceSeconds = 0.5f;
    private const int MaxRootGridPerAxis = 8;
    private const int MaxFailures = 3;
    private const float RetryDelaySeconds = 5f;

    private const float FailedRetrySeconds = 120f;

    private Material _material;
    private GameObject _root;

    private readonly Dictionary<TileKey, LodTile> _tiles = new Dictionary<TileKey, LodTile>();
    private Dictionary<TileKey, NodeRole> _desired = new Dictionary<TileKey, NodeRole>();
    private Dictionary<TileKey, NodeRole> _prevDesired = new Dictionary<TileKey, NodeRole>();
    private readonly HashSet<TileKey> _surface = new HashSet<TileKey>();
    private readonly List<LodTile> _inFlight = new List<LodTile>();

    // Scratch buffers (no per-frame allocations).
    private readonly List<TileKey> _keyScratch = new List<TileKey>();
    private readonly List<TileKey> _keyScratch2 = new List<TileKey>();
    private readonly List<LodTile> _tileScratch = new List<LodTile>();

    private Vector3 _lastUpdatePos = new Vector3(99999f, 0f, 99999f);
    private float _updateTimer;
    private bool _dirty = true;
    private bool _rebuildPending;
    private float _rebuildAt;
    private int _rootLevel = -1;
    private int _loggedLayoutKey = int.MinValue;

    private readonly FarWater _farWater = new FarWater();
    internal FarWater Water => _farWater;

    private Camera _farClipCamera;
    private float _originalFarClip = -1f;

    private const float StatsLogIntervalSeconds = 30f;
    private float _nextStatsLog;
    private int _statsLogCount;
    private bool _loggedFirstBuild;

    public int TotalBuilds { get; private set; }
    public int RootLevel => _rootLevel;

    private static DHConfig Cfg => Plugin.Cfg;

    public void Initialize(Material material)
    {
        _material = material;
    }

    private void OnEnable()
    {
        Instance = this;
        _dirty = true;
        if (Cfg != null) Cfg.Changed += OnConfigChanged;
        Camera.onPreRender += OnCameraPreRender;
    }

    // Each step alone: one failure never skip the rest (camera buffers, real zones, far clip must all come back).
    private void OnDisable()
    {
        Camera.onPreRender -= OnCameraPreRender;
        if (Cfg != null) Cfg.Changed -= OnConfigChanged;
        Step("DetachCommandBuffer", DetachCommandBuffer);
        Step("DetachWaterBuffer", DetachWaterBuffer);
        Step("RestoreRealRenderers", RestoreRealRenderers);
        Step("FarWater.Destroy", _farWater.Destroy);
        Step("ClearAll", ClearAll);
        Step("RestoreFarClip", RestoreFarClip);
        Step("DestroyMaterials", DestroyOwnMaterials);
        if (Instance == this) Instance = null;
        // After Instance gone, so Water.ApplySettings postfix leave game's plane alone.
        Step("ReleaseSeaPlane", () => SetSeaPainting(false));
    }

    private void OnDestroy()
    {
        Step("ClearAll", ClearAll);
        Step("RestoreFarClip", RestoreFarClip);
        Step("FarWater.Destroy", _farWater.Destroy);
        Step("DestroyMaterials", DestroyOwnMaterials);
    }

    private static void Step(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PatchGuard.Report("LodTerrainManager " + what, e);
        }
    }

    // Material clones me made: far-tile template and real-zone copy materials.
    private void DestroyOwnMaterials()
    {
        foreach (Material m in _copyMaterials.Values)
            if (m != null) Destroy(m);
        _copyMaterials.Clear();
        _realRenderers.Clear();
        _realSunk.Clear();
        if (_zoneTemplate != null)
        {
            Destroy(_zoneTemplate);
            _zoneTemplate = null;
        }
        _zoneTemplateFailed = false;
    }

    // Only settings that change tile identity or geometry force (debounced) full rebuild. Desired-set knobs just
    // mark tree dirty; existing refine/merge logic then stream the difference in. Everything else = read live
    // where used.
    private void OnConfigChanged(ConfigEntryBase entry)
    {
        DHConfig c = Cfg;
        if (c == null) return;

        if (entry == c.BaseTileSize || entry == c.BaseVertexSpacing || entry == c.LodLevels || entry == c.WorldRadius
            || entry == c.ExactMaxSpacing || entry == c.TerrainMaterial || entry == c.FarTerrainDraw)
        {
            _rebuildPending = true;
            _rebuildAt = Time.unscaledTime + RebuildDebounceSeconds;
        }
        else if (entry == c.SplitFactor || entry == c.SplitHysteresis || entry == c.ViewDistance || entry == c.FillCracks)
        {
            _dirty = true;
        }
        else if (entry == c.CrackFillDepth)
        {
            foreach (LodTile t in _tiles.Values)
                if (t.Mode == DisplayMode.Lowered) SetDisplay(t, DisplayMode.Lowered);
        }
        else if (entry == c.NearTerrainOffset)
        {
            foreach (LodTile t in _tiles.Values)
                if (t.Exact && t.Mode == DisplayMode.Surface) SetDisplay(t, DisplayMode.Surface);
        }
        else if (entry == c.LodHideDistance)
        {
            foreach (LodTile t in _tiles.Values) ApplyHideDistance(t);
        }
        else if (entry == c.RaiseCameraFarClip || entry == c.CameraFarClip)
        {
            RestoreFarClip(); // ApplyCamera re-capture game's value, re-apply next frame
        }
        if (entry == c.FarTerrainDraw || entry == c.RealTerrainFadeFix) RestoreRealRenderers();
    }

    // Me destroy every tile and forget all state. Safe to call any time.
    public void ClearAll()
    {
        foreach (LodTile t in _tiles.Values)
        {
            CancelRequest(t);
            DestroyTileObject(t);
        }
        _tiles.Clear();
        _desired.Clear();
        _prevDesired.Clear();
        _surface.Clear();
        _inFlight.Clear();
        if (_root != null)
        {
            Destroy(_root);
            _root = null;
        }
        // Whatever clear us, next enabled frame must recompute from scratch (cover Enabled toggle too).
        _lastUpdatePos = new Vector3(99999f, 0f, 99999f);
        _dirty = true;
        _zoneTemplateFailed = false;
    }

    // Force full rebuild now (console command / debugging).
    public void Rebuild()
    {
        _rebuildPending = false;
        ClearAll();
        RestoreFarClip();
        _dirty = true;
    }

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            PatchGuard.Report("LodTerrainManager.Update", e);
        }
    }

    private void Tick()
    {
        DHConfig cfg = Cfg;
        bool useOurs = cfg != null && _material != null && !_vanillaCompare;
        TerrainLink.SetVanillaActive(!useOurs);
        if (!useOurs)
        {
            if (_tiles.Count > 0) ClearAll();
            RestoreFarClip();
            DetachWaterBuffer();
            _farWater.Destroy();
            SetSeaPainting(false);
            return;
        }

        if (_rebuildPending)
        {
            if (Time.unscaledTime < _rebuildAt) return; // layout slider still being dragged; wait till it settle
            _rebuildPending = false;
            ClearAll();
            _dirty = true;
            LogLayoutChange();
        }

        if (WorldGenerator.instance == null || HeightmapBuilder.instance == null) return;
        if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected) return;

        Camera cam = Utils.GetMainCamera();
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;

        _updateTimer += Time.deltaTime;
        if (_dirty || (_updateTimer >= cfg.UpdateInterval.Value && Utils.DistanceXZ(camPos, _lastUpdatePos) >= cfg.UpdateStepDistance.Value))
        {
            _dirty = false;
            _updateTimer = 0f;
            _lastUpdatePos = camPos;
            RecomputeLayout();
            RecomputeDesired(camPos);
            CreateWanted();
            RemoveStale(); // never hand builder a tile that just became unwanted
        }

        PumpBuilds(camPos);
        UpdateSurface();
        ApplyCamera(cam);

        if (cfg.DebugLogging.Value && Time.unscaledTime >= _nextStatsLog)
        {
            _nextStatsLog = Time.unscaledTime + StatsLogIntervalSeconds;
            _statsLogCount++;
            Log.Info($"{GetStats()} cam=({camPos.x:0},{camPos.y:0},{camPos.z:0}) farClip={cam.farClipPlane:0} fogDensity={RenderSettings.fogDensity:0.#####} fogMode={RenderSettings.fogMode}");
            if (_statsLogCount == 2) Diagnostics.DumpOnce(); // world settled by now
        }
    }

    // Skirt run after every script's Update, so it see this frame's zone spawns and removals (ZoneSystem.Update)
    // and this frame's refine/merge, before anything render.
    private void LateUpdate()
    {
        if (_tiles.Count == 0 || Cfg == null) return;
        try
        {
            UpdateCoverage();
        }
        catch (Exception e)
        {
            PatchGuard.Report("LodTerrainManager.LateUpdate", e);
        }
    }

    // ------------------------------------------------------------------ layout

    // Layout setting changed live: log the layout again (RecomputeLayout log only when its key change) and warn when
    // BaseVertexSpacing cannot be reached, same as at game start.
    private void LogLayoutChange()
    {
        _loggedLayoutKey = int.MinValue;
        DHConfig c = Cfg;
        if (c != null && Mathf.Abs(c.EffectiveVertexSpacing - c.BaseVertexSpacing.Value) > 0.01f * c.BaseVertexSpacing.Value)
            Log.Warning($"BaseVertexSpacing {c.BaseVertexSpacing.Value} m is not reachable with BaseTileSize {c.BaseTileSize.Value} m (tiles are limited to 8..250 quads per edge); using {c.EffectiveVertexSpacing:0.##} m.");
    }

    private float TileSize(int level) => Cfg.BaseTileSize.Value * (1 << level);

    // Me pick coarsest level really used. Configured LodLevels clamped so root grid never coarser than needed to
    // cover world with 2x2 grid (extra levels = 25 km+ tiles that lie almost all outside world) and never finer
    // than 8x8 grid (that = thousands of tiles).
    private void RecomputeLayout()
    {
        float r = Cfg.WorldRadius.Value;
        float b = Cfg.BaseTileSize.Value;
        int configured = Cfg.LodLevels.Value - 1;

        int maxRoot = 0;
        while (maxRoot < 24 && b * (1 << maxRoot) < r) maxRoot++;
        int minRoot = 0;
        while (minRoot < 24 && Mathf.CeilToInt(2f * r / (b * (1 << minRoot))) > MaxRootGridPerAxis) minRoot++;

        int root = Mathf.Clamp(configured, minRoot, maxRoot);
        int key = root * 64 + configured;
        if (key != _loggedLayoutKey)
        {
            _loggedLayoutKey = key;
            if (root != configured)
                Log.Warning($"LodLevels={Cfg.LodLevels.Value} clamped to {root + 1} levels (root tiles {TileSize(root):0} m) so the root grid stays between 2x2 and {MaxRootGridPerAxis}x{MaxRootGridPerAxis} for WorldRadius {r:0} m.");
            else
                Log.Info($"LOD layout: {root + 1} levels, finest {TileSize(0):0} m tiles at {Cfg.EffectiveVertexSpacing:0.##} m, root tiles {TileSize(root):0} m.");
        }
        _rootLevel = root;
    }

    private void GetBounds(TileKey key, out float minX, out float minZ, out float maxX, out float maxZ)
    {
        float s = TileSize(key.Level);
        minX = key.X * s;
        minZ = key.Y * s;
        maxX = minX + s;
        maxZ = minZ + s;
    }

    private static float AabbDist(float px, float pz, float minX, float minZ, float maxX, float maxZ)
    {
        float dx = Mathf.Max(minX - px, 0f, px - maxX);
        float dz = Mathf.Max(minZ - pz, 0f, pz - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float AabbCheb(float px, float pz, float minX, float minZ, float maxX, float maxZ)
    {
        float dx = Mathf.Max(minX - px, 0f, px - maxX);
        float dz = Mathf.Max(minZ - pz, 0f, pz - maxZ);
        return Mathf.Max(dx, dz);
    }

    private bool InWorld(TileKey key)
    {
        GetBounds(key, out float minX, out float minZ, out float maxX, out float maxZ);
        return AabbDist(0f, 0f, minX, minZ, maxX, maxZ) <= Cfg.WorldRadius.Value;
    }

    // ------------------------------------------------------------------ desired set

    private void RecomputeDesired(Vector3 camPos)
    {
        // Me keep last pass's roles for hysteresis, then rebuild.
        Dictionary<TileKey, NodeRole> tmp = _prevDesired;
        _prevDesired = _desired;
        _desired = tmp;
        _desired.Clear();

        float rootSize = TileSize(_rootLevel);
        float r = Cfg.WorldRadius.Value;
        int min = Mathf.FloorToInt(-r / rootSize);
        int max = Mathf.CeilToInt(r / rootSize) - 1;
        for (int y = min; y <= max; y++)
            for (int x = min; x <= max; x++)
                Visit(new TileKey(_rootLevel, x, y), camPos);
    }

    private void Visit(TileKey key, Vector3 camPos)
    {
        if (!InWorld(key)) return;

        bool split = false;
        if (key.Level > 0)
        {
            GetBounds(key, out float minX, out float minZ, out float maxX, out float maxZ);
            float size = maxX - minX;
            // Hysteresis: node already split stay split a bit longer (on both thresholds), so hovering around
            // a boundary no destroy and rebuild its children on every crossing.
            float h = 1f;
            if (_prevDesired.TryGetValue(key, out NodeRole prev) && prev == NodeRole.Internal)
                h += Cfg.SplitHysteresis.Value;
            float cheb = AabbCheb(camPos.x, camPos.z, minX, minZ, maxX, maxZ);
            float dist = AabbDist(camPos.x, camPos.z, minX, minZ, maxX, maxZ);
            // ViewDistance limit refinement only: far node stay one coarse tile, never hole.
            split = cheb < Cfg.SplitFactor.Value * h * size && dist <= Cfg.ViewDistance.Value * h;
        }

        if (split)
        {
            _desired[key] = NodeRole.Internal;
            for (int i = 0; i < 4; i++) Visit(key.Child(i), camPos);
        }
        else
        {
            _desired[key] = NodeRole.Leaf;
        }
    }

    private void CreateWanted()
    {
        bool fill = Cfg.FillCracks.Value;
        foreach (KeyValuePair<TileKey, NodeRole> kv in _desired)
        {
            if (_tiles.ContainsKey(kv.Key)) continue;
            if (kv.Value == NodeRole.Internal && !fill) continue; // internal nodes made only for crack filling
            _tiles.Add(kv.Key, NewTile(kv.Key));
        }
    }

    private LodTile NewTile(TileKey key)
    {
        float size = TileSize(key.Level);
        int width = Cfg.TileWidth;
        float scale = size / width;
        var center = new Vector3((key.X + 0.5f) * size, 0f, (key.Y + 0.5f) * size);
        return new LodTile(key, size, center, width, scale)
        {
            Exact = scale <= Cfg.ExactMaxSpacing.Value,
        };
    }

    // Tiles not wanted and not on surface give nothing: me drop them.
    private void RemoveStale()
    {
        _tileScratch.Clear();
        foreach (LodTile t in _tiles.Values)
            if (!_desired.ContainsKey(t.Key) && !_surface.Contains(t.Key)) _tileScratch.Add(t);
        foreach (LodTile t in _tileScratch) RemoveTile(t);
    }

    // ------------------------------------------------------------------ building

    private float Priority(LodTile t, Vector3 cam)
    {
        // Nearest first, but coarse tile covering camera beat fine tile far away.
        return AabbDist(cam.x, cam.z, t.MinX, t.MinZ, t.MaxX, t.MaxZ) / t.Size;
    }

    private void PumpBuilds(Vector3 camPos)
    {
        HeightmapBuilder builder = HeightmapBuilder.instance;
        WorldGenerator wg = WorldGenerator.instance;
        float now = Time.unscaledTime;

        // 1. Poll tiles queued on builder thread. HeightmapBuilder.RequestTerrain take one lock and return data the
        //    moment it ready (remove it from builder's 16-entry list so it cannot be evicted), else enqueue it
        //    (once) and return null. It never block.
        for (int i = _inFlight.Count - 1; i >= 0; i--)
        {
            LodTile t = _inFlight[i];
            if (t.State != TileState.Requested) continue;
            HeightmapBuilder.HMBuildData data = builder.RequestTerrain(t.Center, t.Width, t.Scale, true, wg);
            if (data != null)
            {
                t.BuildData = data;
                t.State = TileState.Ready;
            }
        }

        // 2. Ready heights become meshes (main-thread work), oldest first, limited per frame.
        int budget = Cfg.MaxMeshBuildsPerFrame.Value;
        for (int i = 0; i < _inFlight.Count && budget > 0;)
        {
            LodTile t = _inFlight[i];
            if (t.State != TileState.Ready)
            {
                i++;
                continue;
            }
            _inFlight.RemoveAt(i);
            BuildTile(t);
            budget--;
        }

        // 3. Queue more work, nearest/coarsest first, but only while zone loading idle.
        if (Cfg.WaitForZones.Value && (ZoneSystem.instance == null || !ZoneSystem.instance.IsActiveAreaLoaded()))
            return;

        int max = Cfg.MaxBuildsInFlight.Value;
        while (_inFlight.Count < max)
        {
            LodTile best = null;
            float bestP = float.MaxValue;
            foreach (LodTile t in _tiles.Values)
            {
                // Failed tiles get new chance after long cooldown, so passing problem heal in place.
                if (t.State != TileState.Wanted && t.State != TileState.Failed) continue;
                if (now < t.RetryAfter) continue;
                if (!_desired.ContainsKey(t.Key)) continue;
                float p = Priority(t, camPos);
                if (p < bestP || (p == bestP && best != null && t.Key.Level > best.Key.Level))
                {
                    best = t;
                    bestP = p;
                }
            }
            if (best == null) break;

            best.State = TileState.Requested;
            _inFlight.Add(best);
            // Exact tiles put own request on builder queue; poll below then find it like any other.
            if (best.Exact && !ExactTerrainBuilder.TryEnqueue(builder, best.Center, best.Width, best.Scale, wg))
                best.Exact = false;
            HeightmapBuilder.HMBuildData data = builder.RequestTerrain(best.Center, best.Width, best.Scale, true, wg);
            if (data != null)
            {
                best.BuildData = data; // builder cached it already
                best.State = TileState.Ready;
            }
        }
    }

    private void EnsureRoot()
    {
        if (_root != null) return;
        _root = new GameObject("DistantHorizons_LOD");
        _root.transform.position = Vector3.zero;
    }

    private void BuildTile(LodTile t)
    {
        EnsureRoot();
        var go = new GameObject("DH_" + t.Key);
        // Me make it switched off first: Heightmap.Awake then wait until every field below is set.
        go.SetActive(false);
        go.transform.position = t.Center; // y = 0: Heightmap bake absolute world heights relative to this
        go.transform.SetParent(_root.transform, true);

        // Same recipe as vanilla TerrainLod.CreateMesh, but distant flag set on field before Awake run: then Awake
        // never add tile to game's list of real ground heightmaps (vanilla add it, IsDistantLod setter take it out
        // again), so no mod that register heightmaps in Awake patch (for example Valheim Community Patch's lookup)
        // ever take far tile for ground of loaded zone.
        Heightmap hm = go.AddComponent<Heightmap>();
        hm.m_width = t.Width;
        hm.m_scale = t.Scale;
        hm.m_material = TileMaterial(out t.UsesZoneMaterial);
        hm.m_isDistantLod = true;
        go.SetActive(true); // Awake (material set, so component stay enabled) and OnEnable run now
        hm.enabled = true;

        if (t.BuildData != null)
        {
            // Hand over heights me already took from builder, so Generate() never fetch (and never block).
            hm.m_buildData = t.BuildData;
            if (t.BuildData.m_cornerBiomes != null) hm.m_cornerBiomes = t.BuildData.m_cornerBiomes;
        }

        try
        {
            hm.Regenerate();
        }
        catch (Exception e)
        {
            OnBuildFailed(t, go, e);
            return;
        }

        t.BuildData = null;
        t.Go = go;
        t.Heightmap = hm;
        t.Renderer = go.GetComponent<MeshRenderer>();
        MeshFilter mf = go.GetComponent<MeshFilter>();
        t.Mesh = mf != null ? mf.sharedMesh : null;
        t.Drawn = true;
        // Renderer always draw: it give tile correct depth (depth texture, fog, water, occlusion of objects).
        // In shrink mode command buffer then paint over its faded colour.
        t.Renderer.forceRenderingOff = false;
        t.State = TileState.Built;
        t.FailCount = 0;
        TotalBuilds++;

        // Hidden till surface logic say safe to show.
        go.SetActive(false);
        t.Mode = DisplayMode.Hidden;

        if (!HasAncestorInSurface(t.Key) && !HasDescendantInSurface(t.Key))
        {
            _surface.Add(t.Key);
            SetDisplay(t, DisplayMode.Surface);
        }

        if (!_loggedFirstBuild)
        {
            _loggedFirstBuild = true;
            Log.Info($"First LOD tile built: {t.Key} ({t.Width}x{t.Width} quads at {t.Scale:0.#} m) at {t.Center.x:0},{t.Center.z:0}.");
        }
        if (Cfg.DebugLogging.Value)
            Log.Info($"Built {t.Key} scale={t.Scale:0.#} at {t.Center.x:0},{t.Center.z:0} (surface={_surface.Count}, tiles={_tiles.Count})");
    }

    private void OnBuildFailed(LodTile t, GameObject go, Exception e)
    {
        Destroy(go);
        t.BuildData = null;
        t.FailCount++;
        if (t.FailCount >= MaxFailures)
        {
            t.State = TileState.Failed;
            t.RetryAfter = Time.unscaledTime + FailedRetrySeconds;
            if (t.FailCount == MaxFailures)
                Log.Error($"Giving up on LOD tile {t.Key} after {t.FailCount} failures (retrying every {FailedRetrySeconds:0} s): {e}");
            else
                Log.Warning($"LOD tile {t.Key} still failing ({t.FailCount} attempts): {e.GetType().Name}: {e.Message}");
        }
        else
        {
            t.State = TileState.Wanted;
            t.RetryAfter = Time.unscaledTime + RetryDelaySeconds * t.FailCount;
            Log.Warning($"LOD tile {t.Key} failed (attempt {t.FailCount}), retrying in {RetryDelaySeconds * t.FailCount:0} s: {e.GetType().Name}: {e.Message}");
        }
    }

    private void SetDisplay(LodTile t, DisplayMode mode)
    {
        if (t.Go == null) return;
        t.Mode = mode;
        // Exact near tiles sit a bit under real terrain, so two never z-fight where both drawn. Shrink mode: renderer
        // only give depth, TileDepthDrop lower than paint (PaintFor and SampleTile add it back).
        float y = mode == DisplayMode.Lowered ? -Cfg.CrackFillDepth.Value * t.Scale : (t.Exact ? -Cfg.NearTerrainOffset.Value : 0f);
        y -= TileDrop;
        Transform tr = t.Go.transform;
        if (tr.position.y != y) tr.position = new Vector3(t.Center.x, y, t.Center.z);
        if (mode == DisplayMode.Hidden && t.CoverSignature != 0) RestoreCoverage(t);
        bool active = mode != DisplayMode.Hidden;
        if (t.Go.activeSelf != active) t.Go.SetActive(active);
        // Heightmap.OnEnable re-apply vanilla shadow settings and hide distance on every activation, so me
        // override both after.
        ApplyShadowMode(t);
        ApplyHideDistance(t);
    }

    private static readonly MaterialPropertyBlock s_hideBlock = new MaterialPropertyBlock();
    // Shader's non-LOD variant hide pixels BEYOND _LodHideDistance (real zones fade out where LOD fade in), so
    // tiles on zone material get distance they can never reach.
    private const float NoHide = 1000000f;
    private float _hideOverride = -1f; // console experiment; negative = use config
    private bool _vanillaCompare;      // console experiment; show vanilla's 3x3 LOD, not our tiles

    // Camera distance inside which shader dissolve tile. Vanilla use near simulation distance x zone diagonal
    // (about 181 m), but game only guarantee real zones out to near simulation distance x 64 m (128 m) from
    // player, so vanilla band can show past loaded zones as melting ground.
    internal float HideDistance()
    {
        if (_hideOverride >= 0f) return _hideOverride;
        float v = Cfg.LodHideDistance.Value;
        if (v > 0f) return v;
        // Zones load in disc of near x 64 m around player's zone centre; player can stand 45 m from that centre,
        // so about one zone less than that = what always covered from camera's point of view.
        return Mathf.Max(32f, NearSimulationDistance() * 64f - 64f);
    }

    private static int NearSimulationDistance()
    {
        if (ZNet.instance == null) return 2;
        return Mathf.Max(1, ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance);
    }

    // Material now on renderer = distant-LOD variant? (experiments can swap it)
    private static bool OnLodVariant(LodTile t)
    {
        Material m = t.Renderer != null ? t.Renderer.sharedMaterial : null;
        return m != null ? m.IsKeywordEnabled("_ISDISTANTLOD_ON") : !t.UsesZoneMaterial;
    }

    private void ApplyHideDistance(LodTile t)
    {
        if (t.Renderer == null || t.Mode == DisplayMode.Hidden) return;
        // Zone variant hide pixels BEYOND the distance (real zones fade out where LOD fade in), so tiles on it
        // must never reach it; only LOD variant get near-camera distance (and console override).
        float d = OnLodVariant(t) ? (_hideOverride >= 0f ? _hideOverride : HideDistance()) : NoHide;
        t.Renderer.GetPropertyBlock(s_hideBlock);
        s_hideBlock.SetFloat(s_lodHideDistanceId, d);
        // In shrink mode renderer only give depth; command buffer paint over its faded colour, drawing same mesh
        // RenderOffset higher. Domain stage lift terrain above water level by up to 1 m beyond 300 m (y + far),
        // which would put renderer above paint; huge water level flip that to y - far, so renderer always stay
        // under.
        Material m = t.Renderer.sharedMaterial;
        float water = m != null && m.HasProperty(s_waterLevelId) ? m.GetFloat(s_waterLevelId) : 30f;
        s_hideBlock.SetFloat(s_waterLevelId, ShrinkMode ? SunkWaterLevel : water);
        t.Renderer.SetPropertyBlock(s_hideBlock);
    }

    // Game rewrite _LodHideDistance on every enabled Heightmap (simulation-distance changes); me put ours back.
    internal void ReapplyHideDistances()
    {
        foreach (LodTile t in _tiles.Values) ApplyHideDistance(t);
    }

    // ------------------------------------------------------------------ shrunk-space drawing

    // Custom/Heightmap multiply albedo by (1 - smoothstep((|worldPos - cameraPos| - 200) * 0.005)) in every
    // variant: black beyond 400 m from camera, literal constants, no property to switch it off. Vanilla never
    // show terrain that far without fog. So me draw far tiles with command buffer inside main camera's render:
    // object-to-world matrices scaled by s (about 0.007) around world origin, view matrix for camera at
    // cameraPos * s, projection with near and far planes scaled by s, and _WorldSpaceCameraPos = cameraPos * s.
    // Perspective projection = scale invariant, so screen positions and NDC depth same as normal draw (depth
    // test against real world just work), while every world-space distance shader compute is s times smaller:
    // fade never start. World-space texture tiling and water level scaled back through property block.

    private const string ShrinkModeName = "shrink";
    private CommandBuffer _cmd;
    private Camera _cmdCamera;
    private CameraEvent _cmdEvent;
    private int _cmdPassIndex = -1;
    private string _cmdPassName;
    private int _cmdDrawsLastFrame;
    private int _cmdRealDrawsLastFrame;
    private readonly Dictionary<Heightmap, MeshRenderer> _realRenderers = new Dictionary<Heightmap, MeshRenderer>();
    private readonly List<Heightmap> _realPrune = new List<Heightmap>();
    private readonly MaterialPropertyBlock _cmdBlock = new MaterialPropertyBlock();
    private readonly Plane[] _planes = new Plane[6];
    private static readonly int s_uvScaleId = Shader.PropertyToID("_UVScale");
    private static readonly int s_waterLevelId = Shader.PropertyToID("_WaterLevel");
    private static readonly int s_lodHideDistanceId = Shader.PropertyToID("_LodHideDistance");
    private static readonly int s_lodHideModifierId = Shader.PropertyToID("_LodHideModifier");
    private static readonly int s_tessId = Shader.PropertyToID("_Tess");
    private static readonly int s_cameraPosId = Shader.PropertyToID("_WorldSpaceCameraPos");
    private static readonly int s_fogParamsId = Shader.PropertyToID("unity_FogParams");
    private static readonly string[] s_shNames = { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };
    private static readonly int[] s_shIds = BuildIds(s_shNames);
    private static readonly Vector4[] s_sh = new Vector4[7];
    // Console: skip ambient-probe constants, to compare with and without.
    internal bool SkipAmbient = false;

    private static int[] BuildIds(string[] names)
    {
        var ids = new int[names.Length];
        for (int i = 0; i < names.Length; i++) ids[i] = Shader.PropertyToID(names[i]);
        return ids;
    }

    // Renderer get its ambient-probe constants from engine; CommandBuffer.DrawMesh no, so painted ground came
    // out a bit darker than ground game draw, seam following 64 m zone grid. These = same constants Unity
    // derive from ambient probe.
    private static void CollectAmbient()
    {
        UnityEngine.Rendering.SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
        for (int c = 0; c < 3; c++)
        {
            s_sh[c] = new Vector4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]);
            s_sh[c + 3] = new Vector4(sh[c, 4], sh[c, 5], sh[c, 6] * 3f, sh[c, 7]);
        }
        s_sh[6] = new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1f);
    }

    private bool ShrinkMode => string.Equals(Cfg.FarTerrainDraw.Value, ShrinkModeName, StringComparison.OrdinalIgnoreCase);


    // ---- far sea
    private CommandBuffer _waterCmd;
    private Camera _waterCmdCamera;
    private CameraEvent _waterCmdEvent;
    private int _waterDrawsLastFrame;
    private float _waterShrink = 1f;

    // True while far sea painted; Water.ApplySettings postfix only hide game's plane then.
    internal bool SeaPainting { get; private set; }

    // Game re-apply its water settings only when water object spawn or simulation distance change, so every
    // change here ask for it at once: on, postfix hide game's 4 km plane; off, game's own value come back.
    // Skipped while quitting or outside world (Water.ApplySettings need ZNet and ZoneSystem).
    internal void SetSeaPainting(bool on)
    {
        if (SeaPainting == on) return;
        SeaPainting = on;
        if (ZNet.instance == null || ZoneSystem.instance == null) return;
        if (Game.instance != null && Game.instance.IsShuttingDown()) return;
        try
        {
            global::Water.ApplySettingsOnAll(); // game's component, not my Water property
            if (on) Log.Info("Far water ready; the game's own distant water plane steps aside while it is drawn.");
        }
        catch (Exception e)
        {
            Log.Warning("Could not re-apply water settings: " + e.Message);
        }
    }

    // Console: feed sea black background, not whatever unrun grab pass left bound.
    internal bool SeaBlackBackground = true;
    private static readonly int s_backgroundTexId = Shader.PropertyToID("_BackgroundTex");
    private static readonly int s_glossinessId = Shader.PropertyToID("_Glossiness");
    private static readonly int s_foamColorId = Shader.PropertyToID("_FoamColor");
    private static readonly int s_foamDepthId = Shader.PropertyToID("_FoamDepth");
    private static readonly int s_specCube0Id = Shader.PropertyToID("unity_SpecCube0");
    private static readonly int s_specCube0HdrId = Shader.PropertyToID("unity_SpecCube0_HDR");
    private static readonly int s_specCube0BoxMinId = Shader.PropertyToID("unity_SpecCube0_BoxMin");
    private static readonly int s_specCube0ProbePosId = Shader.PropertyToID("unity_SpecCube0_ProbePosition");
    private static readonly int s_specCube1ProbePosId = Shader.PropertyToID("unity_SpecCube1_ProbePosition");
    private static readonly int s_fogDensityId = Shader.PropertyToID("unity_FogDensity");
    private static readonly int s_fogStartId = Shader.PropertyToID("unity_FogStart");
    private static readonly int s_fogEndId = Shader.PropertyToID("unity_FogEnd");
    private static readonly int s_isLodId = Shader.PropertyToID("_IsLod");
    private static readonly int s_visibleMaxDistanceId = Shader.PropertyToID("_VisibleMaxDistance");
    private static readonly int s_waterEdgeId = Shader.PropertyToID("_WaterEdge");
    private static readonly int s_normalScaleId = Shader.PropertyToID("_NormalScale");
    private static readonly int s_refractionScaleId = Shader.PropertyToID("_RefractionScale");
    private static readonly int s_refractionMaxId = Shader.PropertyToID("_RefractionMax");

    private void DetachWaterBuffer()
    {
        if (_waterCmd != null && _waterCmdCamera != null)
        {
            try
            {
                _waterCmdCamera.RemoveCommandBuffer(_waterCmdEvent, _waterCmd);
            }
            catch (Exception)
            {
                // camera already destroyed
            }
        }
        if (_waterCmd != null) _waterCmd.Release();
        _waterCmd = null;
        _waterCmdCamera = null;
    }

    // Me paint far sea just before game's own transparent pass (BeforeForwardAlpha), in same shrunk space as
    // terrain. Water shader fade its alpha to zero between 300 m and 800 m from camera with literal constants, so
    // this = only way to have sea further out; in shrunk space those distances never reached.
    private void EmitFarWater(Camera cam, Vector3 c, Matrix4x4 view, Matrix4x4 proj)
    {
        _waterDrawsLastFrame = 0;
        // Interiors (dungeons sit about 5 km up): game hide overworld, so sea must go too.
        bool ready = RenderGroupSystem.IsGroupActive(RenderGroup.Overworld) && _farWater.Prepare(cam);
        if (!ready)
        {
            if (_waterCmd != null) _waterCmd.Clear();
            SetSeaPainting(false);
            return;
        }

        // Game's own 4 km plane sit at same height as our sheet and fight it pixel by pixel = heavy flicker.
        // Water.ApplySettings postfix push that plane's fade-in radius out of reach while sheet painted, and let
        // game put it back the moment sheet stop.
        SetSeaPainting(true);

        // Before transparent phase, not after: game's low mist, spray and other particles = transparent, write no
        // depth, so sheet drawn after them paint straight over them and cut them off along its edge.
        const CameraEvent evt = CameraEvent.BeforeForwardAlpha;
        if (_waterCmd == null || _waterCmdCamera != cam || _waterCmdEvent != evt)
        {
            DetachWaterBuffer();
            _waterCmd = new CommandBuffer { name = "DistantHorizons far sea" };
            _waterCmdCamera = cam;
            _waterCmdEvent = evt;
            cam.AddCommandBuffer(evt, _waterCmd);
        }
        _waterCmd.Clear();

        Material m = _farWater.Material;
        float height = Mathf.Abs(c.y - FarWater.WaterLevel);
        bool fogScaled = RenderSettings.fog && Cfg.FarWaterFog.Value;
        float lastS = -1f;
        for (int band = 0; band < _farWater.BandCount; band++)
        {
            // Each band centred on camera, so its farthest point = its own outer radius.
            float farthest = _farWater.BandOuter(band) + height + 1f;
            float s = 1f;
            while (s * farthest > FadeSafeDistance && s > 1f / 4096f) s *= 0.5f;

            if (s != lastS)
            {
                lastS = s;
                _waterShrink = s;
                Matrix4x4 viewShrunk = Matrix4x4.Scale(new Vector3(s, s, s)) * view * Matrix4x4.Scale(new Vector3(1f / s, 1f / s, 1f / s));
                Matrix4x4 projShrunk = proj;
                projShrunk.m23 *= s;
                _waterCmd.SetViewProjectionMatrices(viewShrunk, projShrunk);
                _waterCmd.SetGlobalVector(s_cameraPosId, new Vector4(c.x * s, c.y * s, c.z * s, 0f));
                // This shader fog itself from unity_FogDensity/Start/End, not unity_FogParams, against own eye
                // depth, which here = s times real one.
                if (fogScaled)
                {
                    _waterCmd.SetGlobalFloat(s_fogDensityId, RenderSettings.fogDensity / s);
                    _waterCmd.SetGlobalFloat(s_fogStartId, RenderSettings.fogStartDistance * s);
                    _waterCmd.SetGlobalFloat(s_fogEndId, RenderSettings.fogEndDistance * s);
                }
            }

            _cmdBlock.Clear();
            if (!SkipAmbient)
                for (int k = 0; k < s_shIds.Length; k++) _cmdBlock.SetVector(s_shIds[k], s_sh[k]);
            _cmdBlock.SetFloat(s_isLodId, 1f);              // flat plate: no wave displacement to scale wrong
            _cmdBlock.SetFloat(s_visibleMaxDistanceId, 0f); // for LOD water this = fade-IN radius; 0 = full alpha
            _cmdBlock.SetFloat(s_waterEdgeId, FarWater.WorldEdge * s); // keep world-edge cut at its true radius
            if (m.HasProperty(s_tessId)) _cmdBlock.SetFloat(s_tessId, 1f);
            // Grab pass that fill _BackgroundTex belong to pass 0, no run for command-buffer draw.
            if (m.HasProperty(s_refractionScaleId)) _cmdBlock.SetFloat(s_refractionScaleId, 0f);
            if (m.HasProperty(s_refractionMaxId)) _cmdBlock.SetFloat(s_refractionMaxId, 0f);
            if (m.HasProperty(s_normalScaleId)) _cmdBlock.SetFloat(s_normalScaleId, m.GetFloat(s_normalScaleId) / s);
            if (SeaBlackBackground) _cmdBlock.SetTexture(s_backgroundTexId, Texture2D.blackTexture);
            if (m.HasProperty(s_glossinessId) && Cfg.FarWaterGloss.Value >= 0f) _cmdBlock.SetFloat(s_glossinessId, Cfg.FarWaterGloss.Value);
            // Foam placed by world position with fixed scales, so in shrunk space it stretch into pale streaks
            // across whole sea; its tint near-white = what paint sheet out.
            if (!Cfg.FarWaterFoam.Value)
            {
                if (m.HasProperty(s_foamColorId)) _cmdBlock.SetColor(s_foamColorId, new Color(0f, 0f, 0f, 0f));
                if (m.HasProperty(s_foamDepthId)) _cmdBlock.SetFloat(s_foamDepthId, 0f);
            }
            // Command-buffer draw get no reflection-probe data, and water sample cubemap for its reflection:
            // unbound, that read as white on surface this glossy. Me hand it default probe and switch off box
            // projection and second probe blend.
            if (ReflectionProbe.defaultTexture != null)
            {
                _cmdBlock.SetTexture(s_specCube0Id, ReflectionProbe.defaultTexture);
                _cmdBlock.SetVector(s_specCube0HdrId, ReflectionProbe.defaultTextureHDRDecodeValues);
            }
            _cmdBlock.SetVector(s_specCube0BoxMinId, new Vector4(0f, 0f, 0f, 1f));
            _cmdBlock.SetVector(s_specCube0ProbePosId, Vector4.zero);
            _cmdBlock.SetVector(s_specCube1ProbePosId, Vector4.zero);

            _waterCmd.DrawMesh(_farWater.Mesh, Matrix4x4.Scale(new Vector3(s, s, s)) * Matrix4x4.Translate(_farWater.Center),
                               m, band, _farWater.Pass, _cmdBlock);
            _waterDrawsLastFrame++;
        }

        _waterCmd.SetViewProjectionMatrices(view, proj);
        _waterCmd.SetGlobalVector(s_cameraPosId, new Vector4(c.x, c.y, c.z, 0f));
        if (fogScaled)
        {
            _waterCmd.SetGlobalFloat(s_fogDensityId, RenderSettings.fogDensity);
            _waterCmd.SetGlobalFloat(s_fogStartId, RenderSettings.fogStartDistance);
            _waterCmd.SetGlobalFloat(s_fogEndId, RenderSettings.fogEndDistance);
        }
    }

    private void DetachCommandBuffer()
    {
        if (_cmd != null && _cmdCamera != null)
        {
            try
            {
                _cmdCamera.RemoveCommandBuffer(_cmdEvent, _cmd);
            }
            catch (Exception)
            {
                // camera already destroyed
            }
        }
        if (_cmd != null) _cmd.Release();
        _cmd = null;
        _cmdCamera = null;
        _cmdPassIndex = -1;
        _cmdPassName = null;
    }

    private struct ShrinkDraw
    {
        public Mesh Mesh;
        public Vector3 Pos;
        public Material Mat;
        public Heightmap Hm;
        public float S;
        // Real-zone copy: drawn at true size (S = 1) with shader camera moved to Cam (see RelocatedCamera).
        public bool Relocate;
        public Vector3 Cam;
    }

    // Farthest pixel of a draw stay inside this many shader metres of camera. 100 not 200 (where albedo fade
    // start) because domain stage also lift terrain above water by 2 * smoothstep((d - 100)/200) m (and sink it
    // below), d = XZ distance to camera. Under 100 both lift and fade = exactly zero.
    private const float FadeSafeDistance = 100f;

    // Top of widest band shader draw above _WaterLevel (sand and flat-ground override that also cancel snow).
    // Every such band = literal shader-metre count, so in draw shrunk by s it cover 1/s real metres: pin this top
    // to its true height = visible land stay exactly like vanilla, widened rest pushed below water surface, where
    // ocean hide it.
    private const float ShoreBandTop = 0.4f;
    private readonly List<ShrinkDraw> _draws = new List<ShrinkDraw>(512);
    private static readonly Comparison<ShrinkDraw> s_byShrinkDesc = (a, b) => b.S.CompareTo(a.S);
    private readonly float[] _depthScratch = new float[4];
    private static readonly int s_depthId = Shader.PropertyToID("_depth");
    private float _minShrinkLastFrame = 1f;
    private int _shrinkGroupsLastFrame;

    // Shrink factor for one draw: big as possible (1 = no shrink, same as normal draw) while farthest point of
    // its bounds stay within FadeSafeDistance. Shader's shoreline, snow and cliff bands = metre constants, so every
    // height-based effect widen by 1/s; s near 1 for near ground keep them near real size. Quantised to powers of
    // two so draws share matrices.
    private static float ShrinkFor(Vector3 c, Bounds b)
    {
        float farthest = Mathf.Sqrt(b.SqrDistance(c)) + b.size.magnitude; // upper bound of farthest corner
        if (farthest <= FadeSafeDistance) return 1f;
        float s = FadeSafeDistance / farthest;
        float q = 1f;
        while (q > s && q > 1f / 4096f) q *= 0.5f;
        return q;
    }

    private void OnCameraPreRender(Camera cam)
    {
        if (cam == null) return;
        try
        {
            PaintFor(cam);
        }
        catch (Exception e)
        {
            // Half-filled buffer would leave shrunk matrices set for rest of camera's render.
            if (_cmd != null) _cmd.Clear();
            if (_waterCmd != null) _waterCmd.Clear();
            PatchGuard.Report("LodTerrainManager.OnCameraPreRender", e);
        }
    }

    private void PaintFor(Camera cam)
    {
        // Not gated on _tiles.Count: rebuild empty it for a frame or two, and real zones would flash back to
        // vanilla faded look each time.
        Camera main = Utils.GetMainCamera();
        bool wanted = ShrinkMode && enabled && Cfg != null && !_vanillaCompare && cam == main;
        if (!wanted)
        {
            if (_cmdCamera == cam && _cmd != null) _cmd.Clear();
            if (_waterCmdCamera == cam && _waterCmd != null) _waterCmd.Clear();
            // Only main camera decide; reflection probes and other cameras also raise onPreRender.
            if (cam == main)
            {
                if (_realRenderersOff) RestoreRealRenderers();
                SetSeaPainting(false);
            }
            return;
        }

        bool deferred = cam.actualRenderingPath == RenderingPath.DeferredShading;
        CameraEvent evt = deferred ? (UseBeforeGBuffer ? CameraEvent.BeforeGBuffer : CameraEvent.AfterGBuffer)
                                   : (UseBeforeGBuffer ? CameraEvent.BeforeForwardOpaque : CameraEvent.AfterForwardOpaque);
        string passName = deferred ? "DEFERRED" : "FORWARD";
        if (_cmd == null || _cmdCamera != cam || _cmdEvent != evt)
        {
            DetachCommandBuffer();
            _cmd = new CommandBuffer { name = "DistantHorizons far terrain" };
            _cmdCamera = cam;
            _cmdEvent = evt;
            cam.AddCommandBuffer(evt, _cmd);
        }
        _cmd.Clear();
        _cmdPassName = passName;
        _cmdDrawsLastFrame = 0;
        _cmdRealDrawsLastFrame = 0;
        _draws.Clear();
        CollectAmbient();

        Vector3 c = cam.transform.position;
        GeometryUtility.CalculateFrustumPlanes(cam, _planes);

        // Our tiles.
        foreach (LodTile t in _tiles.Values)
        {
            if (!t.Drawn || t.Mesh == null || t.Go == null || t.Renderer == null || t.Mode == DisplayMode.Hidden) continue;
            if (t.State != TileState.Built) continue;
            // RenderGroupSubscriber disable these renderers while player in interior (dungeons sit 5 km up);
            // without this, painted ground show through walls.
            if (!PaintOnly && (!t.Renderer.enabled || !t.Renderer.gameObject.activeInHierarchy)) continue;
            Material m = t.Renderer.sharedMaterial;
            if (m == null) continue;
            Vector3 pos = t.Go.transform.position;
            Bounds b = t.Mesh.bounds;
            b.center += pos;
            if (!GeometryUtility.TestPlanesAABB(_planes, b)) continue;
            pos.y += Lift + TileDrop; // nearer than renderer, so paint always win depth test
            if (t.Renderer.forceRenderingOff != (PaintOnly || !t.Drawn)) t.Renderer.forceRenderingOff = PaintOnly || !t.Drawn;
            _draws.Add(new ShrinkDraw { Mesh = t.Mesh, Pos = pos, Mat = m, Hm = t.Heightmap, S = ShrinkFor(c, b) });
            _cmdDrawsLastFrame++;
        }

        // Real zones beyond fade: me draw them, not their renderers.
        if (Cfg.RealTerrainFadeFix.Value && !NoRealCopies) CollectRealZoneCopies(c);
        else if (_realRenderersOff) RestoreRealRenderers();

        // Emit, grouped by shrink factor (largest first), so matrices change as rarely as possible.
        _draws.Sort(s_byShrinkDesc);
        Matrix4x4 view = cam.worldToCameraMatrix;
        Matrix4x4 proj = cam.projectionMatrix;
        bool fixFog = !deferred && RenderSettings.fog;
        Vector4 fog = fixFog ? FogParams() : Vector4.zero;
        float curS = -1f;
        Vector3 curCam = new Vector3(float.NaN, 0f, 0f);
        Matrix4x4 scale = Matrix4x4.identity;
        _shrinkGroupsLastFrame = 0;
        _minShrinkLastFrame = 1f;
        for (int i = 0; i < _draws.Count; i++)
        {
            ShrinkDraw d = _draws[i];
            if (d.S != curS)
            {
                curS = d.S;
                _shrinkGroupsLastFrame++;
                if (curS < _minShrinkLastFrame) _minShrinkLastFrame = curS;
                scale = Matrix4x4.Scale(new Vector3(curS, curS, curS));
                // view' = S(s) * view * S(1/s): camera see shrunk world from cameraPos * s; projection keep its
                // shape, only its near/far translation term scale. Screen positions and NDC depth same as normal
                // draw; every world-space distance shader compute is s times smaller.
                Matrix4x4 viewShrunk = scale * view * Matrix4x4.Scale(new Vector3(1f / curS, 1f / curS, 1f / curS));
                Matrix4x4 projShrunk = proj;
                projShrunk.m23 *= curS;
                // SetViewProjectionMatrices take projection in Camera.projectionMatrix convention and convert it
                // itself (feed it GL.GetGPUProjectionMatrix output = every painted pixel land at near plane).
                _cmd.SetViewProjectionMatrices(viewShrunk, projShrunk);
                if (fixFog) _cmd.SetGlobalVector(s_fogParamsId, new Vector4(fog.x / curS, fog.y / curS, fog.z / curS, fog.w));
            }
            // Shader camera: real-zone copies get theirs moved next to them, everything else the shrunk real one.
            Vector3 shaderCam = d.Relocate ? d.Cam : c * curS;
            if (shaderCam != curCam)
            {
                curCam = shaderCam;
                _cmd.SetGlobalVector(s_cameraPosId, new Vector4(shaderCam.x, shaderCam.y, shaderCam.z, 0f));
            }
            Material m = d.Mat;
            int pass = m.FindPass(passName);
            if (pass < 0) continue;
            _cmdPassIndex = pass;

            _cmdBlock.Clear();
            if (!SkipAmbient)
                for (int k = 0; k < s_shIds.Length; k++) _cmdBlock.SetVector(s_shIds[k], s_sh[k]);
            _cmdBlock.SetFloat(s_uvScaleId, m.GetFloat(s_uvScaleId) / curS);
            // Band tops, not water line: see ShoreBandTop. Scaling water level alone made every lowland below
            // roughly 50 m render as wet sand with its snow cancelled.
            float water = m.GetFloat(s_waterLevelId);
            _cmdBlock.SetFloat(s_waterLevelId, curS == 1f ? water : (water + ShoreBandTop) * curS - ShoreBandTop);
            // _depth (four corner ocean depths) compared against literals on its own, never against shrunk
            // height, so me leave it exactly as material has it.
            if (m.IsKeywordEnabled("_ISDISTANTLOD_ON"))
            {
                // LOD variant: domain stage sink mesh by 80 * (1 - saturate(dist / (hide + modifier)));
                // tiny positive divisor make that 0 everywhere.
                _cmdBlock.SetFloat(s_lodHideDistanceId, 0.01f);
                _cmdBlock.SetFloat(s_lodHideModifierId, 0f);
            }
            else _cmdBlock.SetFloat(s_lodHideDistanceId, NoHide); // zone variant: discard beyond distance
            if (m.HasProperty(s_tessId)) _cmdBlock.SetFloat(s_tessId, 1f);

            _cmd.DrawMesh(d.Mesh, scale * Matrix4x4.Translate(d.Pos), m, 0, pass, _cmdBlock);
        }

        _cmd.SetViewProjectionMatrices(view, proj);
        _cmd.SetGlobalVector(s_cameraPosId, new Vector4(c.x, c.y, c.z, 0f));
        if (fixFog) _cmd.SetGlobalVector(s_fogParamsId, fog);

        EmitFarWater(cam, c, view, proj);
    }

    // ------------------------------------------------------------------ real terrain beyond 200 m

    // Shader's fade to black start 200 m from camera (3D), its domain lift 100 m (XZ). Shader's own dissolve cannot
    // remove real draw (its dither keep every pixel whose noise sample = zero), so zone that reach past those keep
    // its renderer for depth (sunk, see SetRealSunk) and command buffer paint copy of it over that.
    // Zone left to game while every point of it within RealGameMaxXZ (XZ) and FadeStart (3D): there lift at most
    // 2 * smoothstep(0.1) = 0.056 m (about RealCopyLift) and no fade, so it meet painted neighbour without step and
    // keep game's own tessellation near camera.
    // Copy drawn at true size, not shrunk: game's Tessellation setting (keyword TESSELATION_ON) add bump to ground in
    // domain stage, phase from world position (sin 1.4x * sin 2.235x * cos 1.5z * cos 2.435z, up to 0.25 m), and
    // every shore, snow and cliff band = literal metres above _WaterLevel. Shrunk copy got bump of other pattern than
    // its depth renderer (renderer poked through in dark ovals) and bands stretched by 1/s (wide pale shore). True
    // size keep both exact; shader camera moved next to zone instead (RelocatedCamera) so lift and fade never start.
    private const float RealGameMaxXZ = 120f;
    private const float FadeStart = 200f;
    // Copy and its depth renderer = same mesh, same bump, both untessellated: lift only break depth ties.
    private const float RealCopyLift = 0.05f;
    // Moved shader camera sit at most this far from zone centre: zone point then within 50 + 45 m (XZ) of it.
    private const float RelocateRadius = 50f;
    // Painted copy of far tile sit this far above renderer that give its depth.
    private const float RenderOffset = 0.1f;
    // Far tile renderer sit this much lower again (shrink mode): its bump pattern (true position) and its shrunk
    // paint's (shrunk position) differ by up to 2 x 0.25 m, so renderer could poke through paint near camera.
    private const float TileDepthDrop = 0.5f;
    // Water level for depth-only renderers: everything count as underwater, so domain stage sink instead of
    // lift (y - far) and snow displacement off.
    private const float SunkWaterLevel = 1000000f;
    private readonly Dictionary<Heightmap, bool> _realSunk = new Dictionary<Heightmap, bool>();

    // Sunk = depth only under painted copy: sink beyond 100 m, and tessellation off like copy (bump on same
    // vertices in both). Not sunk = game's own values back.
    private static void SetRealSunk(MeshRenderer r, bool sunk)
    {
        Material m = r.sharedMaterial;
        float water = m != null && m.HasProperty(s_waterLevelId) ? m.GetFloat(s_waterLevelId) : 30f;
        r.GetPropertyBlock(s_hideBlock);
        s_hideBlock.SetFloat(s_waterLevelId, sunk ? SunkWaterLevel : water);
        if (m != null && m.HasProperty(s_tessId)) s_hideBlock.SetFloat(s_tessId, sunk ? 1f : m.GetFloat(s_tessId));
        r.SetPropertyBlock(s_hideBlock);
    }

    // Zone (renderer bounds) need painted copy: some point of it past lift start (XZ) or fade start (3D).
    private bool NeedsCopy(Vector3 c, Bounds b)
    {
        if (PaintAllRealZones) return true;
        float dx = Mathf.Max(Mathf.Abs(c.x - b.min.x), Mathf.Abs(c.x - b.max.x));
        float dz = Mathf.Max(Mathf.Abs(c.z - b.min.z), Mathf.Abs(c.z - b.max.z));
        float dy = Mathf.Max(Mathf.Abs(c.y - b.min.y), Mathf.Abs(c.y - b.max.y));
        float xz = dx * dx + dz * dz;
        return xz > RealGameMaxXZ * RealGameMaxXZ || xz + dy * dy > FadeStart * FadeStart;
    }

    // Shader camera for real-zone copy: on line from zone centre toward real camera, at most RelocateRadius away.
    // Every distance shader measure then stay under lift start (100 m XZ) and fade start (200 m), and view
    // direction keep its real sense (snow glint only use it).
    private static Vector3 RelocatedCamera(Vector3 cam, Bounds b)
    {
        Vector3 toCam = cam - b.center;
        float dist = toCam.magnitude;
        return dist <= RelocateRadius ? cam : b.center + toCam * (RelocateRadius / dist);
    }

    private bool _realRenderersOff;
    private int _realRenderersOffCount;

    // Console diagnostics for real-zone copies.
    // 0 = no copies (renderers still off), 1 = zone's own material instance, 2 = far-tile template with zone's
    // mask, 3 = LOD template with zone's mask.
    internal int CopyMode = 1;
    // Draw command buffer before G-buffer / opaque pass, not after.
    internal bool UseBeforeGBuffer;
    // Skip skirt (tiles stay at true height under loaded zones).
    internal bool SkirtDisabled;
    // Console: hide every renderer that only give depth, so paint seen alone.
    internal bool PaintOnly = false;
    // Self test: real zones left to game (renderers back, no copies), far tiles still painted.
    internal bool NoRealCopies;
    // Self test: every real zone painted, also the ones near enough for game to draw (hardest case for near ground).
    internal bool PaintAllRealZones;
    // Console: how far above its renderer paint drawn (default RenderOffset).
    internal float PaintLift = -1f;
    private float Lift => PaintLift >= 0f ? PaintLift : RenderOffset;
    // How far far-tile renderers sit under their own surface (shrink mode only, see TileDepthDrop).
    private float TileDrop => Cfg != null && ShrinkMode ? TileDepthDrop : 0f;
    private readonly Dictionary<Heightmap, Material> _copyMaterials = new Dictionary<Heightmap, Material>();

    internal void SetCommandBufferEvent(bool before)
    {
        UseBeforeGBuffer = before;
        DetachCommandBuffer();
    }

    internal void SetSkirtDisabled(bool off)
    {
        SkirtDisabled = off;
        if (off)
            foreach (LodTile t in _tiles.Values)
                if (t.CoverSignature != 0) RestoreCoverage(t);
    }

    private Material CopyMaterial(Heightmap hm, Material own)
    {
        if (CopyMode == 1 || own == null) return own;
        Material template = CopyMode == 2 ? TileMaterial(out _) : _material;
        if (template == null) return own;
        if (!_copyMaterials.TryGetValue(hm, out Material m) || m == null || m.shader != template.shader || m.IsKeywordEnabled("_ISDISTANTLOD_ON") != template.IsKeywordEnabled("_ISDISTANTLOD_ON"))
        {
            if (m != null) Destroy(m);
            m = new Material(template) { name = template.name + " (DH copy)" };
            _copyMaterials[hm] = m;
        }
        m.SetTexture("_ClearedMaskTex", own.GetTexture("_ClearedMaskTex"));
        m.SetFloatArray("_depth", hm.GetOceanDepth());
        return m;
    }

    internal bool RealFadeFixActive => enabled && ShrinkMode && Cfg != null && Cfg.RealTerrainFadeFix.Value && !_vanillaCompare;

    // Me give every real zone its renderer back.
    private void RestoreRealRenderers()
    {
        _realRenderersOff = false;
        _realRenderersOffCount = 0;
        List<Heightmap> real = Heightmap.GetAllHeightmaps();
        if (real != null)
        {
            for (int i = 0; i < real.Count; i++)
            {
                Heightmap hm = real[i];
                if (hm == null || hm.IsDistantLod) continue;
                MeshRenderer r = hm.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (r.forceRenderingOff) r.forceRenderingOff = false;
                if (_realSunk.TryGetValue(hm, out bool sunk) && sunk) SetRealSunk(r, false);
            }
        }
        _realSunk.Clear();
    }

    private void CollectRealZoneCopies(Vector3 c)
    {
        List<Heightmap> real = Heightmap.GetAllHeightmaps();
        if (real == null) return;
        _realRenderersOff = true;
        _realRenderersOffCount = 0;
        if (_realRenderers.Count > 1024)
        {
            _realPrune.Clear();
            foreach (KeyValuePair<Heightmap, MeshRenderer> kv in _realRenderers)
                if (kv.Key == null || kv.Value == null) _realPrune.Add(kv.Key);
            foreach (Heightmap k in _realPrune)
            {
                _realRenderers.Remove(k);
                _realSunk.Remove(k);
                if (_copyMaterials.TryGetValue(k, out Material dead))
                {
                    if (dead != null) Destroy(dead);
                    _copyMaterials.Remove(k);
                }
            }
        }
        for (int i = 0; i < real.Count; i++)
        {
            Heightmap hm = real[i];
            if (hm == null || hm.IsDistantLod) continue;
            if (!_realRenderers.TryGetValue(hm, out MeshRenderer r) || r == null)
            {
                r = hm.GetComponent<MeshRenderer>();
                _realRenderers[hm] = r;
            }
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds b = r.bounds;
            // Zone that reach past lift or fade start keep rendering for depth, but sunk under its painted copy.
            bool off = NeedsCopy(c, b);
            if (!off && r.forceRenderingOff) r.forceRenderingOff = false;
            bool wantSunk = off;
            if (!_realSunk.TryGetValue(hm, out bool sunk) || sunk != wantSunk)
            {
                SetRealSunk(r, wantSunk);
                _realSunk[hm] = wantSunk;
            }
            if (off) _realRenderersOffCount++;
            if (CopyMode == 0 || !off) continue;
            if (!GeometryUtility.TestPlanesAABB(_planes, b)) continue;
            MeshFilter mf = hm.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            Material m = CopyMaterial(hm, r.sharedMaterial);
            if (mesh == null || m == null) continue;
            Vector3 pos = hm.transform.position;
            pos.y += PaintLift >= 0f ? PaintLift : RealCopyLift; // above sunk renderer, so copy win depth test everywhere
            if (r.forceRenderingOff != PaintOnly) r.forceRenderingOff = PaintOnly;
            _draws.Add(new ShrinkDraw
            {
                Mesh = mesh, Pos = pos, Mat = m, Hm = hm, S = 1f, Relocate = true, Cam = RelocatedCamera(c, b),
            });
            _cmdRealDrawsLastFrame++;
        }
    }

    // unity_FogParams like Unity build it from RenderSettings.
    private static Vector4 FogParams()
    {
        float d = RenderSettings.fogDensity;
        float start = RenderSettings.fogStartDistance, end = RenderSettings.fogEndDistance;
        float range = Mathf.Max(end - start, 0.0001f);
        return new Vector4(d / Mathf.Sqrt(Mathf.Log(2f)), d / Mathf.Log(2f), -1f / range, end / range);
    }

    // ------------------------------------------------------------------ tile material

    private Material _zoneTemplate;
    private bool _zoneTemplateFailed;

    // Real-zone terrain material with tessellation off: same textures and sun lighting as ground around player.
    // Game's own distant-LOD material render every biome as dark, ambient-lit ground once fog thin (it only ever
    // meant to be seen through fog), so me keep it only as option.
    private Material TileMaterial(out bool zone)
    {
        zone = false;
        if (!string.Equals(Cfg.TerrainMaterial.Value, "zone", StringComparison.OrdinalIgnoreCase)) return _material;
        if (_zoneTemplate == null && !_zoneTemplateFailed)
        {
            Material src = ZoneMaterial();
            if (src == null)
            {
                _zoneTemplateFailed = true;
                Log.Warning("Real-zone terrain material not found; far tiles use the distant-LOD material.");
            }
            else
            {
                _zoneTemplate = new Material(src) { name = src.name + " (DH far)" };
                if (_zoneTemplate.HasProperty("_Tess")) _zoneTemplate.SetFloat("_Tess", 1f);
                if (_zoneTemplate.HasProperty("_Displacement")) _zoneTemplate.SetFloat("_Displacement", 0f);
                Log.Info("Far tiles use the real-zone terrain material (tessellation off).");
            }
        }
        if (_zoneTemplate == null) return _material;
        zone = true;
        return _zoneTemplate;
    }

    // ------------------------------------------------------------------ real-terrain coverage (skirt)

    // How far LOD surface pushed down under loaded real zones.
    private const float SkirtDepth = 60f;
    // Every frame: zone removed from ZoneSystem.m_zones in its Update, its objects destroyed at end of that frame,
    // so check in next Update restore tile before anything render without real ground.
    private const float CoverageInterval = 0f;
    private float _coverageTimer;
    private bool[] _coverScratch = new bool[256];
    private int[] _colZoneScratch = new int[256];
    private int[] _rowZoneScratch = new int[256];

    // Real zone's terrain exist exactly while its root in ZoneSystem.m_zones (IsZoneLoaded also wait for objects).
    private static bool ZoneExists(ZoneSystem zs, int zx, int zy)
    {
        return zs.m_zones.ContainsKey(new Vector2s(zx, zy));
    }

    // Wherever real zone loaded, LOD vertices under it pushed SkirtDepth down. Vertices just outside stay put, so
    // along edge of loaded area tile become wall that go down under real terrain: nothing poke through, no
    // near-camera dissolve needed, and tile fully under real terrain simply not drawn. Only tiles touching loaded
    // square around player get examined.
    private void UpdateCoverage()
    {
        if (SkirtDisabled) return;
        _coverageTimer += Time.deltaTime;
        if (CoverageInterval > 0f && _coverageTimer < CoverageInterval) return;
        _coverageTimer = 0f;
        ZoneSystem zs = ZoneSystem.instance;
        if (zs == null) return;

        // Zones load around ZNet's reference position (local player), not camera.
        Vector3 pos = ZNet.instance != null ? ZNet.instance.GetReferencePosition() : (Utils.GetMainCamera() != null ? Utils.GetMainCamera().transform.position : Vector3.zero);
        int r = NearSimulationDistance() + 1;
        int pzx = Mathf.FloorToInt((pos.x + 32f) / 64f);
        int pzy = Mathf.FloorToInt((pos.z + 32f) / 64f);
        float minX = (pzx - r) * 64f - 32f, maxX = (pzx + r) * 64f + 32f;
        float minZ = (pzy - r) * 64f - 32f, maxZ = (pzy + r) * 64f + 32f;

        foreach (LodTile t in _tiles.Values)
        {
            if (t.Mode == DisplayMode.Hidden || t.Go == null || t.Renderer == null || t.Mesh == null) continue;
            if (!(t.MaxX > minX && t.MinX < maxX && t.MaxZ > minZ && t.MinZ < maxZ))
            {
                if (t.CoverSignature != 0) RestoreCoverage(t);
                continue;
            }
            // Zones overlapping tile plus one vertex spacing around it (erosion below look at neighbours), clipped
            // to square that can hold loaded zones.
            int zx0 = Mathf.Max(pzx - r, Mathf.FloorToInt((t.MinX - t.Scale + 32f) / 64f));
            int zx1 = Mathf.Min(pzx + r, Mathf.FloorToInt((t.MaxX + t.Scale + 32f) / 64f));
            int zy0 = Mathf.Max(pzy - r, Mathf.FloorToInt((t.MinZ - t.Scale + 32f) / 64f));
            int zy1 = Mathf.Min(pzy + r, Mathf.FloorToInt((t.MaxZ + t.Scale + 32f) / 64f));
            int w = zx1 - zx0 + 1, h = zy1 - zy0 + 1;
            if (w <= 0 || h <= 0) { if (t.CoverSignature != 0) RestoreCoverage(t); continue; }
            if (_coverScratch.Length < w * h) _coverScratch = new bool[w * h];
            int sig = 17;
            bool any = false;
            for (int zy = zy0; zy <= zy1; zy++)
                for (int zx = zx0; zx <= zx1; zx++)
                {
                    bool loaded = ZoneExists(zs, zx, zy);
                    _coverScratch[(zy - zy0) * w + (zx - zx0)] = loaded;
                    if (loaded)
                    {
                        any = true;
                        sig = unchecked(sig * 31 + zy * 4096 + zx);
                    }
                }
            if (!any) sig = 0;
            if (sig == t.CoverSignature) continue;
            if (sig == 0) RestoreCoverage(t);
            else ApplyCoverage(t, sig, zx0, zy0, w, h);
        }
    }

    private void ApplyCoverage(LodTile t, int sig, int zx0, int zy0, int w, int h)
    {
        Mesh mesh = t.Mesh;
        int n = t.Width + 1;
        if (mesh == null || mesh.vertexCount != n * n) return;
        if (t.BaseVertices == null || t.BaseVertices.Length != n * n)
        {
            t.BaseVertices = mesh.vertices;
            t.WorkVertices = null;
        }
        if (t.WorkVertices == null || t.WorkVertices.Length != n * n) t.WorkVertices = new Vector3[n * n];
        if (_colZoneScratch.Length < n + 2) { _colZoneScratch = new int[n + 2]; _rowZoneScratch = new int[n + 2]; }

        Vector3[] b = t.BaseVertices;
        Vector3[] work = t.WorkVertices;
        Vector3 origin = t.Go.transform.position;
        // Grid axis aligned: zone index per column (x) and per row (z) relative to scratch window, with one extra
        // entry each side for neighbours of edge vertices.
        float x0 = origin.x + b[0].x, z0 = origin.z + b[0].z;
        for (int j = -1; j <= n; j++) _colZoneScratch[j + 1] = Mathf.FloorToInt((x0 + j * t.Scale + 32f) / 64f) - zx0;
        for (int i = -1; i <= n; i++) _rowZoneScratch[i + 1] = Mathf.FloorToInt((z0 + i * t.Scale + 32f) / 64f) - zy0;

        int lowered = 0;
        for (int i = 0; i < n; i++)
        {
            int zy = _rowZoneScratch[i + 1];
            int zyPrev = _rowZoneScratch[i], zyNext = _rowZoneScratch[i + 2];
            for (int j = 0; j < n; j++)
            {
                int idx = i * n + j;
                int zx = _colZoneScratch[j + 1];
                // Eroded by one vertex: vertex drop only when it and its four neighbours all lie in loaded zones,
                // so wall it make sit one spacing inside loaded area, under real ground, not straddling boundary
                // as visible slot.
                bool covered = Loaded(zx, zy, w, h)
                               && Loaded(_colZoneScratch[j], zy, w, h) && Loaded(_colZoneScratch[j + 2], zy, w, h)
                               && Loaded(zx, zyPrev, w, h) && Loaded(zx, zyNext, w, h);
                Vector3 v = b[idx];
                if (covered)
                {
                    v.y -= SkirtDepth;
                    lowered++;
                }
                work[idx] = v;
            }
        }
        t.CoverSignature = sig;
        if (lowered == n * n)
        {
            SetDrawn(t, false); // all under real terrain
            return;
        }
        SetDrawn(t, true);
        mesh.SetVertices(work);
        mesh.RecalculateBounds();
    }

    private void SetDrawn(LodTile t, bool drawn)
    {
        t.Drawn = drawn;
        // RenderGroupSubscriber own Renderer.enabled (it toggle it for interiors), so me never write that flag.
        if (t.Renderer != null) t.Renderer.forceRenderingOff = !drawn;
    }

    private bool Loaded(int zx, int zy, int w, int h)
    {
        return zx >= 0 && zx < w && zy >= 0 && zy < h && _coverScratch[zy * w + zx];
    }

    private void RestoreCoverage(LodTile t)
    {
        t.CoverSignature = 0;
        SetDrawn(t, true);
        Mesh mesh = t.Mesh;
        if (t.BaseVertices != null && mesh != null && mesh.vertexCount == t.BaseVertices.Length)
        {
            mesh.SetVertices(t.BaseVertices);
            mesh.RecalculateBounds();
        }
        t.BaseVertices = null;
        t.WorkVertices = null;
    }

    // Lowered crack-fill tiles sit underground, can shadow nothing: no submit them to cascades.
    private void ApplyShadowMode(LodTile t)
    {
        if (t.Renderer == null || t.Mode == DisplayMode.Hidden) return;
        ShadowCastingMode want = t.Mode == DisplayMode.Lowered
            ? ShadowCastingMode.Off
            : (DistantShadowsEnabled() ? ShadowCastingMode.On : ShadowCastingMode.Off);
        if (t.Renderer.shadowCastingMode != want) t.Renderer.shadowCastingMode = want;
    }

    private static bool DistantShadowsEnabled()
    {
        GraphicsSettingsManager gsm = GraphicsSettingsManager.Instance;
        return gsm == null || gsm.ActiveSettings.m_distantShadows;
    }

    private void DestroyTileObject(LodTile t)
    {
        t.BuildData = null;
        if (t.SwapMaterial != null)
        {
            Destroy(t.SwapMaterial);
            t.SwapMaterial = null;
        }
        t.OriginalMaterial = null;
        if (t.Go != null)
        {
            Destroy(t.Go);
            t.Go = null;
            t.Heightmap = null;
            t.Renderer = null;
        }
    }

    private void RemoveTile(LodTile t)
    {
        if (Cfg.DebugLogging.Value && t.State == TileState.Built)
            Log.Info($"Removed {t.Key}");
        CancelRequest(t);
        DestroyTileObject(t);
        _tiles.Remove(t.Key);
        _surface.Remove(t.Key);
        _inFlight.Remove(t);
    }

    // Pull not-yet-started job for this tile off builder's FIFO, so zone requests queued behind it no wait for
    // terrain nobody want any more. Index 0 never touched: builder thread read it between two separate lock
    // acquisitions, so removing it could throw on that thread. Job me cannot cancel just run, its result age
    // out of builder's ready list.
    private void CancelRequest(LodTile t)
    {
        if (t.State != TileState.Requested) return;
        // Builder disposed in OnApplicationQuit, before our OnDisable; its getter would only log warning.
        if (Game.instance != null && Game.instance.IsShuttingDown()) return;
        HeightmapBuilder builder = HeightmapBuilder.instance;
        WorldGenerator wg = WorldGenerator.instance;
        if (builder == null || wg == null) return;
        try
        {
            object gate = builder.m_lock;
            List<HeightmapBuilder.HMBuildData> queue = builder.m_toBuild;
            if (gate == null || queue == null) return;
            lock (gate)
            {
                for (int i = queue.Count - 1; i >= 1; i--)
                {
                    HeightmapBuilder.HMBuildData job = queue[i];
                    if (job.m_distantLod && job.IsEqual(t.Center, t.Width, t.Scale, true, wg)) queue.RemoveAt(i);
                }
            }
        }
        catch (Exception e)
        {
            if (Cfg.DebugLogging.Value) Log.Warning($"Could not cancel builder job for {t.Key}: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ surface cut maintenance

    private bool HasAncestorInSurface(TileKey key)
    {
        foreach (TileKey s in _surface)
            if (s.IsAncestorOf(key)) return true;
        return false;
    }

    private bool HasDescendantInSurface(TileKey key)
    {
        foreach (TileKey s in _surface)
            if (key.IsAncestorOf(s)) return true;
        return false;
    }

    private bool IsBuilt(TileKey key)
    {
        return _tiles.TryGetValue(key, out LodTile t) && t.State == TileState.Built;
    }

    // True when every desired child subtree can take over ground from this node.
    private bool ChildrenReady(TileKey key)
    {
        int n = 0;
        for (int i = 0; i < 4; i++)
        {
            TileKey c = key.Child(i);
            if (!_desired.TryGetValue(c, out NodeRole role)) continue; // culled by world disc
            n++;
            if (!SubtreeReady(c, role)) return false;
        }
        return n > 0;
    }

    private bool SubtreeReady(TileKey key, NodeRole role)
    {
        if (IsBuilt(key)) return true;
        return role == NodeRole.Internal && ChildrenReady(key);
    }

    // Me collect built nodes that will form new surface under key.
    private void CollectReady(TileKey key, List<TileKey> list)
    {
        if (IsBuilt(key))
        {
            list.Add(key);
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            TileKey c = key.Child(i);
            if (_desired.ContainsKey(c)) CollectReady(c, list);
        }
    }

    private bool TryFindDesiredAncestor(TileKey key, out TileKey ancestor, out NodeRole role)
    {
        TileKey k = key;
        while (k.Level < _rootLevel)
        {
            k = k.Parent;
            if (_desired.TryGetValue(k, out role))
            {
                ancestor = k;
                return true;
            }
        }
        ancestor = default;
        role = NodeRole.Leaf;
        return false;
    }

    private void UpdateSurface()
    {
        bool fill = Cfg.FillCracks.Value;

        _keyScratch.Clear();
        _keyScratch.AddRange(_surface);
        foreach (TileKey key in _keyScratch)
        {
            if (!_surface.Contains(key)) continue; // earlier merge this pass handled it already
            if (!_tiles.TryGetValue(key, out LodTile tile) || tile.State != TileState.Built)
            {
                _surface.Remove(key);
                continue;
            }

            if (_desired.TryGetValue(key, out NodeRole role))
            {
                if (role == NodeRole.Internal && ChildrenReady(key))
                {
                    // Refine: children take over ground in same frame parent step down.
                    _surface.Remove(key);
                    _keyScratch2.Clear();
                    for (int i = 0; i < 4; i++)
                    {
                        TileKey c = key.Child(i);
                        if (_desired.ContainsKey(c)) CollectReady(c, _keyScratch2);
                    }
                    foreach (TileKey k in _keyScratch2)
                    {
                        _surface.Add(k);
                        SetDisplay(_tiles[k], DisplayMode.Surface);
                    }
                    if (fill) SetDisplay(tile, DisplayMode.Lowered);
                    else RemoveTile(tile);
                }
                else if (tile.Mode != DisplayMode.Surface)
                {
                    SetDisplay(tile, DisplayMode.Surface);
                }
            }
            else
            {
                // Not wanted any more: merge into nearest wanted ancestor once that one exist.
                if (!TryFindDesiredAncestor(key, out TileKey anc, out NodeRole ancRole) || ancRole == NodeRole.Internal)
                {
                    // Out of range, or outside world disc: nothing will replace it.
                    _surface.Remove(key);
                    RemoveTile(tile);
                    continue;
                }
                if (_tiles.TryGetValue(anc, out LodTile ancTile) && ancTile.State == TileState.Built)
                {
                    _keyScratch2.Clear();
                    foreach (TileKey s in _surface)
                        if (anc.IsAncestorOf(s)) _keyScratch2.Add(s);
                    foreach (TileKey s in _keyScratch2)
                    {
                        _surface.Remove(s);
                        if (_tiles.TryGetValue(s, out LodTile st)) RemoveTile(st);
                    }
                    _surface.Add(anc);
                    SetDisplay(ancTile, DisplayMode.Surface);
                }
            }
        }

        RemoveStale();

        // Wanted, built, off-surface tiles: internal ones sit lowered under their children (or dropped when crack
        // filling off); leaf with nothing above or below it on surface = orphan, must show.
        _tileScratch.Clear();
        foreach (LodTile t in _tiles.Values)
        {
            if (t.State != TileState.Built || _surface.Contains(t.Key)) continue;
            if (!_desired.TryGetValue(t.Key, out NodeRole role)) continue;
            if (role == NodeRole.Internal)
            {
                if (HasDescendantInSurface(t.Key))
                {
                    if (!fill) _tileScratch.Add(t);
                    else if (t.Mode != DisplayMode.Lowered) SetDisplay(t, DisplayMode.Lowered);
                    else ApplyShadowMode(t); // vanilla re-enable shadows on graphics-settings change
                }
                else if (!HasAncestorInSurface(t.Key))
                {
                    _surface.Add(t.Key);
                    SetDisplay(t, DisplayMode.Surface);
                }
                else if (t.Mode != DisplayMode.Hidden)
                {
                    SetDisplay(t, DisplayMode.Hidden);
                }
            }
            else if (!HasAncestorInSurface(t.Key) && !HasDescendantInSurface(t.Key))
            {
                _surface.Add(t.Key);
                SetDisplay(t, DisplayMode.Surface);
            }
            else if (t.Mode != DisplayMode.Hidden)
            {
                SetDisplay(t, DisplayMode.Hidden);
            }
        }
        foreach (LodTile t in _tileScratch) RemoveTile(t);
        UpdateSurfaceVersion();
    }

    // ------------------------------------------------------------------ camera

    private void ApplyCamera(Camera cam)
    {
        if (!Cfg.RaiseCameraFarClip.Value)
        {
            RestoreFarClip();
            return;
        }
        if (_farClipCamera != cam)
        {
            RestoreFarClip();
            _farClipCamera = cam;
            _originalFarClip = cam.farClipPlane;
        }
        float want = Cfg.CameraFarClip.Value;
        if (cam.farClipPlane < want) cam.farClipPlane = want;
    }

    private void RestoreFarClip()
    {
        if (_farClipCamera != null && _originalFarClip > 0f)
            _farClipCamera.farClipPlane = _originalFarClip;
        _farClipCamera = null;
        _originalFarClip = -1f;
    }

    // ------------------------------------------------------------------ console diagnostics and experiments

    // Show vanilla's own 3x3 LOD, not ours, while fog thinning and far objects stay on.
    internal void SetVanillaCompare(bool on)
    {
        _vanillaCompare = on;
        _dirty = true;
        RestoreRealRenderers();
    }

    private static Material ZoneMaterial()
    {
        if (ZoneSystem.instance == null || ZoneSystem.instance.m_zonePrefab == null) return null;
        Heightmap hm = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>(true);
        return hm != null ? hm.m_material : null;
    }

    // Draw every tile with instance of real-zone terrain material (or restore LOD one).
    internal int SetMaterialExperiment(bool zone)
    {
        // Tiles maybe built on either template; swap to clone of asked one, or back.
        Material template = zone ? ZoneMaterial() : _material;
        if (template == null) return 0;
        int n = 0;
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Renderer == null || t.Heightmap == null) continue;
            if (t.OriginalMaterial == null) t.OriginalMaterial = t.Renderer.sharedMaterial;
            bool originalMatches = t.OriginalMaterial != null && t.OriginalMaterial.IsKeywordEnabled("_ISDISTANTLOD_ON") == !zone;
            if (originalMatches)
            {
                t.Renderer.sharedMaterial = t.OriginalMaterial;
            }
            else
            {
                if (t.SwapMaterial != null) Destroy(t.SwapMaterial);
                t.SwapMaterial = new Material(template) { name = template.name + " (DH swap)" };
                if (t.SwapMaterial.HasProperty("_Tess")) t.SwapMaterial.SetFloat("_Tess", 1f);
                if (t.OriginalMaterial != null) t.SwapMaterial.SetTexture("_ClearedMaskTex", t.OriginalMaterial.GetTexture("_ClearedMaskTex"));
                t.SwapMaterial.SetFloatArray("_depth", t.Heightmap.GetOceanDepth());
                t.Renderer.sharedMaterial = t.SwapMaterial;
            }
            ApplyHideDistance(t);
            n++;
        }
        return n;
    }

    // Toggle distant-LOD shader keyword on whatever material tiles show now.
    internal int SetKeywordExperiment(bool on)
    {
        int n = 0;
        foreach (LodTile t in _tiles.Values)
        {
            Material m = t.Renderer != null ? t.Renderer.sharedMaterial : null;
            if (m == null) continue;
            if (on) m.EnableKeyword("_ISDISTANTLOD_ON");
            else m.DisableKeyword("_ISDISTANTLOD_ON");
            m.SetFloat("_IsDistantLod", on ? 1f : 0f);
            ApplyHideDistance(t);
            n++;
        }
        return n;
    }

    internal void SetHideOverride(float metres)
    {
        _hideOverride = metres;
        foreach (LodTile t in _tiles.Values) ApplyHideDistance(t);
    }

    // Zero (or restore) four corner ocean depths terrain shader get per tile.
    internal int SetDepthExperiment(bool zero)
    {
        int n = 0;
        float[] zeros = new float[4];
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Heightmap == null || t.Renderer == null || t.Renderer.sharedMaterial == null) continue;
            t.Renderer.sharedMaterial.SetFloatArray("_depth", zero ? zeros : t.Heightmap.GetOceanDepth());
            n++;
        }
        return n;
    }

    internal int SetShadowExperiment(bool cast, bool receive)
    {
        int n = 0;
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Renderer == null) continue;
            t.Renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            t.Renderer.receiveShadows = receive;
            n++;
        }
        return n;
    }

    internal int SetLayerExperiment(int layer)
    {
        int n = 0;
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Go == null) continue;
            t.Go.layer = layer;
            n++;
        }
        return n;
    }

    // Save PNG of game view after short delay (so console can be closed first).
    internal string Screenshot(float delaySeconds)
    {
        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "DistantHorizons_shot_" + DateTime.Now.ToString("HHmmss") + ".png");
        StartCoroutine(ShotAfter(delaySeconds, path));
        return path;
    }

    private System.Collections.IEnumerator ShotAfter(float delay, string path)
    {
        yield return new WaitForSecondsRealtime(delay);
        ScreenCapture.CaptureScreenshot(path);
        Log.Info("Screenshot requested: " + path);
    }

    // Everything that decide how surface tile under pos is lit and textured, next to real zone there.
    internal string DescribeTileAt(Vector3 pos)
    {
        var sb = new StringBuilder();
        sb.Append("Globals: _SunColor=").Append(Shader.GetGlobalColor("_SunColor"))
          .Append(" _AmbientColor=").Append(Shader.GetGlobalColor("_AmbientColor"))
          .Append(" _SunFogColor=").Append(Shader.GetGlobalColor("_SunFogColor"))
          .Append(" _SunDir=").Append(Shader.GetGlobalVector("_SunDir"))
          .Append(" fog=").Append(RenderSettings.fog).Append('/').Append(RenderSettings.fogMode).Append('/').Append(RenderSettings.fogDensity.ToString("0.#####"))
          .Append(" fogColor=").Append(RenderSettings.fogColor)
          .Append(" ambient=").Append(RenderSettings.ambientMode).Append('/').Append(RenderSettings.ambientLight);
        Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
        if (sun != null) sb.Append(" sun=").Append(sun.color).Append('x').Append(sun.intensity.ToString("0.##")).Append(" shadows=").Append(sun.shadows);
        sb.Append(" shadowDistance=").Append(QualitySettings.shadowDistance).Append(" hideDistance=").Append(HideDistance());
        if (sun != null) sb.Append(" sunCull=").Append(sun.cullingMask.ToString("X")).Append(" sunMode=").Append(sun.renderMode);
        Camera main = Utils.GetMainCamera();
        if (main != null) sb.Append(" camCull=").Append(main.cullingMask.ToString("X")).Append(" camNear=").Append(main.nearClipPlane).Append(" camFar=").Append(main.farClipPlane);
        sb.Append(" skyAlphaPos=").Append(Shader.GetGlobalVector("_SkyAlphaPosition"));
        DepthCamera depthCam = FindFirstObjectByType<DepthCamera>();
        Camera dc = depthCam != null ? depthCam.GetComponent<Camera>() : null;
        if (dc != null) sb.Append(" depthCam: ortho=").Append(dc.orthographic).Append('/').Append(dc.orthographicSize).Append(" cull=").Append(dc.cullingMask.ToString("X")).Append(" far=").Append(dc.farClipPlane).Append(" tex=").Append(dc.targetTexture != null ? dc.targetTexture.width : 0);
        sb.Append(" vanillaCompare=").Append(_vanillaCompare);
        if (main != null) sb.Append(" renderPath=").Append(main.actualRenderingPath).Append(" hdr=").Append(main.allowHDR);
        if (main != null)
        {
            Matrix4x4 pm = main.projectionMatrix;
            Matrix4x4 gm = GL.GetGPUProjectionMatrix(pm, false);
            sb.Append(" proj=[").Append(pm.m00.ToString("0.####")).Append(',').Append(pm.m11.ToString("0.####")).Append(',').Append(pm.m02.ToString("0.####")).Append(',').Append(pm.m12.ToString("0.####"))
              .Append(" | z:").Append(pm.m20.ToString("0.####")).Append(',').Append(pm.m21.ToString("0.####")).Append(',').Append(pm.m22.ToString("0.######")).Append(',').Append(pm.m23.ToString("0.######"))
              .Append(" | w:").Append(pm.m30.ToString("0.####")).Append(',').Append(pm.m31.ToString("0.####")).Append(',').Append(pm.m32.ToString("0.####")).Append(',').Append(pm.m33.ToString("0.####")).Append(']')
              .Append(" gpuZ:").Append(gm.m22.ToString("0.######")).Append(',').Append(gm.m23.ToString("0.######"))
              .Append(" near=").Append(main.nearClipPlane).Append(" far=").Append(main.farClipPlane)
              .Append(" target=").Append(main.targetTexture != null ? main.targetTexture.name : "screen")
              .Append(" reversedZ=").Append(SystemInfo.usesReversedZBuffer).Append(" api=").Append(SystemInfo.graphicsDeviceType)
              .Append(" jitterDiff=").Append((main.projectionMatrix.m02 - main.nonJitteredProjectionMatrix.m02).ToString("0.######"))
              .Append(" paintOnly=").Append(PaintOnly).Append(" lift=").Append(Lift).Append(" ambient=").Append(!SkipAmbient);
        }
        sb.Append(" shrink=").Append(ShrinkMode).Append(" minS=").Append(_minShrinkLastFrame.ToString("0.####")).Append(" groups=").Append(_shrinkGroupsLastFrame).Append(" cmdPass=").Append(_cmdPassName ?? "-").Append('/').Append(_cmdPassIndex).Append(" cmdDraws=").Append(_cmdDrawsLastFrame).Append(" sea=").Append(_waterDrawsLastFrame).Append('@').Append(_waterShrink.ToString("0.#####")).Append(" realCopies=").Append(_cmdRealDrawsLastFrame).Append(" realRenderersOff=").Append(_realRenderersOffCount).Append(" realFadeFix=").Append(RealFadeFixActive).Append(" copyMode=").Append(CopyMode).Append(" event=").Append(UseBeforeGBuffer ? "before" : "after").Append(" skirt=").Append(!SkirtDisabled);

        LodTile best = null;
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Mode != DisplayMode.Surface || t.Go == null) continue;
            if (pos.x < t.MinX || pos.x >= t.MaxX || pos.z < t.MinZ || pos.z >= t.MaxZ) continue;
            if (best == null || t.Size < best.Size) best = t;
        }
        if (best == null) sb.AppendLine().Append("No surface tile under ").Append(pos);
        else DescribeRenderer(sb, "LOD tile " + best.Key + " exact=" + best.Exact + " scale=" + best.Scale.ToString("0.##") + " mode=" + best.Mode + " zoneMaterial=" + best.UsesZoneMaterial + " coverSig=" + best.CoverSignature, best.Renderer, best.Heightmap);

        Heightmap zone = Heightmap.FindHeightmap(pos);
        if (zone != null) DescribeRenderer(sb, "Real zone at " + zone.transform.position, zone.GetComponent<MeshRenderer>(), zone);
        else sb.AppendLine().Append("No real zone heightmap under ").Append(pos);
        return sb.ToString();
    }

    private static void DescribeRenderer(StringBuilder sb, string title, MeshRenderer r, Heightmap hm)
    {
        sb.AppendLine().Append(title).Append(": ");
        if (r == null)
        {
            sb.Append("no renderer");
            return;
        }
        sb.Append("layer=").Append(r.gameObject.layer).Append('(').Append(LayerMask.LayerToName(r.gameObject.layer)).Append(") pos=").Append(r.transform.position)
          .Append(" scale=").Append(r.transform.lossyScale).Append(" rot=").Append(r.transform.rotation.eulerAngles)
          .Append(" cast=").Append(r.shadowCastingMode).Append(" receive=").Append(r.receiveShadows)
          .Append(" probes=").Append(r.lightProbeUsage).Append(" enabled=").Append(r.enabled).Append(" visible=").Append(r.isVisible);
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        sb.Append(" block._LodHideDistance=").Append(block.GetFloat("_LodHideDistance"));
        Material m = r.sharedMaterial;
        float[] depth = m != null ? m.GetFloatArray("_depth") : null;
        sb.Append(" _depth=").Append(depth != null ? string.Join("/", depth) : "unset");
        if (hm != null) sb.Append(" oceanDepth=").Append(string.Join("/", hm.GetOceanDepth()));

        MeshFilter mf = r.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh != null)
        {
            Vector3[] normals = mesh.normals;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < normals.Length; i++) sum += normals[i];
            Vector3[] verts = mesh.vertices;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].y < minY) minY = verts[i].y;
                if (verts[i].y > maxY) maxY = verts[i].y;
            }
            var hist = new Dictionary<uint, int>();
            Color32[] colors = mesh.colors32;
            for (int i = 0; i < colors.Length; i++)
            {
                uint key = ((uint)colors[i].r << 24) | ((uint)colors[i].g << 16) | ((uint)colors[i].b << 8) | colors[i].a;
                hist.TryGetValue(key, out int c);
                hist[key] = c + 1;
            }
            sb.Append(" verts=").Append(mesh.vertexCount).Append(" tangents=").Append(mesh.tangents.Length).Append(" uvs=").Append(mesh.uv.Length)
              .Append(" avgNormal=").Append(normals.Length > 0 ? (sum / normals.Length).ToString("0.###") : "none")
              .Append(" localY=").Append(minY.ToString("0.#")).Append("..").Append(maxY.ToString("0.#"))
              .Append(" colours=");
            int shown = 0;
            foreach (KeyValuePair<uint, int> kv in hist)
            {
                if (shown++ == 8) { sb.Append(" ..."); break; }
                sb.Append(shown > 1 ? " " : "").Append(kv.Key.ToString("X8")).Append('x').Append(kv.Value);
            }
            if (hist.Count == 0) sb.Append("none");
        }
        else sb.Append(" no mesh");
        sb.AppendLine().Append("  material: ").Append(Diagnostics.DescribeMaterial(m));
    }

#if DEBUG
    // Self test (horizons.near): far-tile vertices within radius (XZ) of centre whose drawn surface sit above real
    // ground there (they would poke through it), and how each real zone near centre get drawn from cam (game, or
    // painted copy over a depth renderer). mismatched = painted zones whose depth renderer still tessellate (its
    // bump then differ from its copy's).
    internal string ProbeNearGround(Vector3 cam, Vector3 center, float radius, out int poking, out int mismatched)
    {
        poking = 0;
        mismatched = 0;
        float drop = TileDrop;
        float worst = 0f;
        string worstAt = "-";
        int checkedVerts = 0;
        var tiles = new StringBuilder();
        float r2 = radius * radius;
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Mode == DisplayMode.Hidden || !t.Drawn || t.Mesh == null || t.Go == null) continue;
            if (t.MaxX < center.x - radius || t.MinX > center.x + radius || t.MaxZ < center.z - radius || t.MinZ > center.z + radius) continue;
            Vector3[] verts = t.Mesh.vertices;
            Vector3 o = t.Go.transform.position;
            int above = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = o + verts[i];
                float dx = w.x - center.x, dz = w.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                if (!Heightmap.GetHeight(w, out float realY)) continue;
                checkedVerts++;
                float excess = w.y + drop - realY;
                if (excess <= 0.02f) continue;
                above++;
                if (excess > worst)
                {
                    worst = excess;
                    worstAt = $"{t.Key} ({w.x:0.#},{w.z:0.#})";
                }
            }
            poking += above;
            tiles.Append(' ').Append(t.Key).Append(':').Append(t.Mode).Append(t.Exact ? "/exact" : "")
                 .Append(" sig=").Append(t.CoverSignature != 0 ? "on" : "off").Append(" above=").Append(above);
        }
        var sb = new StringBuilder();
        sb.Append("far-tile vertices within ").Append(radius.ToString("0")).Append(" m checked=").Append(checkedVerts)
          .Append(" aboveReal=").Append(poking).Append(" worst=").Append(worst.ToString("0.00")).Append(" m at ").Append(worstAt)
          .Append(" | tiles:").Append(tiles);
        sb.Append(" | real zones (from camera):");
        List<Heightmap> real = Heightmap.GetAllHeightmaps();
        var block = new MaterialPropertyBlock();
        if (real != null)
        {
            foreach (Heightmap hm in real)
            {
                if (hm == null) continue;
                MeshRenderer r = hm.GetComponent<MeshRenderer>();
                if (r == null) continue;
                Vector3 p = hm.transform.position;
                if (Mathf.Abs(p.x - center.x) > radius || Mathf.Abs(p.z - center.z) > radius) continue;
                Bounds b = r.bounds;
                bool painted = RealFadeFixActive && !NoRealCopies && NeedsCopy(cam, b);
                r.GetPropertyBlock(block);
                Material m = r.sharedMaterial;
                float blockTess = block.GetFloat(s_tessId); // 0 = not set in block
                float tess = m != null && m.HasProperty(s_tessId) ? (blockTess > 0f ? blockTess : m.GetFloat(s_tessId)) : 1f;
                if (painted && tess > 1f) mismatched++;
                sb.Append(' ').Append('(').Append(p.x.ToString("0")).Append(',').Append(p.z.ToString("0")).Append(")=")
                  .Append(painted ? $"painted cam {Vector3.Distance(RelocatedCamera(cam, b), b.center):0} m from centre" : "game")
                  .Append(" tess=").Append(tess.ToString("0.#"))
                  .Append(r.forceRenderingOff ? "/off" : "");
            }
        }
        return sb.ToString();
    }

    // Self test: far tiles cast no shadow (off = true), or back to what ApplyShadowMode want.
    internal void SetTileShadowsOffForTest(bool off)
    {
        foreach (LodTile t in _tiles.Values)
        {
            if (t.Renderer == null) continue;
            if (off) t.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            else ApplyShadowMode(t);
        }
    }
#endif

    // ------------------------------------------------------------------ surface sampling (for far objects)

    // Change whenever set of tiles drawn at ground level change (refine, merge, orphan).
    public int SurfaceVersion { get; private set; }

    private void UpdateSurfaceVersion()
    {
        int h = 0;
        foreach (TileKey k in _surface) h ^= k.GetHashCode() * 486187739;
        if (h != SurfaceVersion) SurfaceVersion = h;
    }

    // Height of far terrain drawn at (x, z): exactly what on screen, sampled on same triangles tile mesh use, so
    // objects placed with it sit on LOD ground, not float or sink.
    public bool TrySampleSurface(float x, float z, out float y, out TileKey key)
    {
        LodTile none = null;
        return TrySampleSurface(x, z, ref none, out y, out key);
    }

    // Same, with hint: back-to-back samples mostly fall in same surface tile, so caller keep last hit and level
    // search only run on miss.
    public bool TrySampleSurface(float x, float z, ref LodTile hint, out float y, out TileKey key)
    {
        if (hint != null && hint.State == TileState.Built && hint.Heightmap != null
            && x >= hint.MinX && x < hint.MaxX && z >= hint.MinZ && z < hint.MaxZ
            && _surface.Contains(hint.Key) && SampleTile(hint, x, z, out y))
        {
            key = hint.Key;
            return true;
        }
        for (int level = 0; level <= _rootLevel; level++)
        {
            float size = TileSize(level);
            var k = new TileKey(level, Mathf.FloorToInt(x / size), Mathf.FloorToInt(z / size));
            if (!_surface.Contains(k)) continue;
            if (_tiles.TryGetValue(k, out LodTile t) && t.State == TileState.Built && t.Heightmap != null && SampleTile(t, x, z, out y))
            {
                key = k;
                hint = t;
                return true;
            }
        }
        y = 0f;
        key = default;
        return false;
    }

    private bool SampleTile(LodTile t, float x, float z, out float y)
    {
        y = 0f;
        List<float> heights = t.Heightmap.m_heights;
        int w = t.Width;
        int num = w + 1;
        if (heights == null || heights.Count != num * num) return false;
        float scale = t.Scale;
        float ox = t.Center.x - w * scale * 0.5f;
        float oz = t.Center.z - w * scale * 0.5f;
        float u = (x - ox) / scale;
        float v = (z - oz) / scale;
        int l = Mathf.Clamp(Mathf.FloorToInt(u), 0, w - 1);
        int k = Mathf.Clamp(Mathf.FloorToInt(v), 0, w - 1);
        float fu = Mathf.Clamp01(u - l);
        float fv = Mathf.Clamp01(v - k);
        float hA = heights[k * num + l];
        float hB = heights[k * num + l + 1];
        float hC = heights[(k + 1) * num + l];
        float hD = heights[(k + 1) * num + l + 1];
        // Same diagonal as Heightmap.RebuildCollisionMesh: triangles (A, C, B) and (B, C, D).
        if (fu + fv <= 1f) y = hA + fu * (hB - hA) + fv * (hC - hA);
        else y = hD + (1f - fu) * (hC - hD) + (1f - fv) * (hB - hD);
        // Surface tiles sit at y = 0 (exact ones a bit under); in shrink mode renderer TileDrop under what is drawn.
        y += t.Go.transform.position.y + TileDrop;
        return true;
    }

    // ------------------------------------------------------------------ stats

    public string GetStats()
    {
        int wanted = 0, requested = 0, ready = 0, built = 0, failed = 0, lowered = 0, hidden = 0;
        long verts = 0;
        foreach (LodTile t in _tiles.Values)
        {
            switch (t.State)
            {
                case TileState.Wanted: wanted++; break;
                case TileState.Requested: requested++; break;
                case TileState.Ready: ready++; break;
                case TileState.Failed: failed++; break;
                case TileState.Built:
                    built++;
                    if (t.Mode == DisplayMode.Lowered) lowered++;
                    else if (t.Mode == DisplayMode.Hidden) hidden++;
                    if (t.Mode != DisplayMode.Hidden && t.Drawn) verts += (long)(t.Width + 1) * (t.Width + 1);
                    break;
            }
        }
        int covered = 0, skirted = 0;
        foreach (LodTile t in _tiles.Values)
        {
            if (t.CoverSignature == 0) continue;
            if (!t.Drawn) covered++;
            else skirted++;
        }
        var sb = new StringBuilder();
        sb.Append("DistantHorizons: tiles=").Append(_tiles.Count)
          .Append(" underRealTerrain=").Append(covered).Append(" skirted=").Append(skirted)
          .Append(" draw=").Append(ShrinkMode ? "shrink" : "renderer").Append('/').Append(_cmdPassName ?? "-").Append('/').Append(_cmdDrawsLastFrame).Append('+').Append(_cmdRealDrawsLastFrame)
          .Append(" surface=").Append(_surface.Count)
          .Append(" lowered=").Append(lowered)
          .Append(" hidden=").Append(hidden)
          .Append(" wanted=").Append(wanted)
          .Append(" queued=").Append(requested)
          .Append(" ready=").Append(ready)
          .Append(" built=").Append(built)
          .Append(" failed=").Append(failed)
          .Append(" drawnVerts=").Append(verts)
          .Append(" totalBuilds=").Append(TotalBuilds)
          .Append(" exactBuilds=").Append(ExactTerrainBuilder.Computed);
        if (_tiles.Count > 0 && _rootLevel >= 0)
        {
            sb.Append(" perLevel=");
            for (int l = 0; l <= _rootLevel; l++)
            {
                int n = 0;
                foreach (LodTile t in _tiles.Values) if (t.Key.Level == l && t.State == TileState.Built) n++;
                sb.Append(l == 0 ? "" : "/").Append(n);
            }
        }
        return sb.ToString();
    }
}
