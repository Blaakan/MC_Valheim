using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me stream far objects (trees, rocks, buildings) past the loaded zones.
//
// 1024 m tiles cover space; close to camera they split into 512 m and 256 m tiles. Each tile get one distance
// band, picked from its nearest edge with hysteresis: Mesh (lowest-LOD geometry of everything), Full (every tree
// = impostor card, big rocks and buildings = meshes), Thin and Far (deterministic subset of trees as cards).
// Me pick band per tile, so ring boundary sweeping across world change one tile at a time. Tile = one GameObject
// per material, holding merged meshes. Tile no longer wanted keep drawing until its replacement is built, so
// nothing blink.
//
// Object data come from object store this client hold (see ZoneObjectSource). Zone in game's near set go to
// real objects only once those are spawned, and come back to me the moment zone leave the set; same for the
// ring where vanilla spawn its "distant" prefabs. Me seat objects on far terrain
// (LodTerrainManager.TrySampleSurface) so they never float or sink.
internal sealed class DistantObjectManager : MonoBehaviour
{
    public static DistantObjectManager Instance { get; private set; }

    private enum Band : byte
    {
        None = 0,
        Mesh = 1,
        Full = 2,
        Thin = 3,
        Far = 4,
    }

    private sealed class Tile
    {
        public TileKey Key;
        public Band Band;
        public bool PiecesOn;
        public bool RocksOn;
        public GameObject Go;
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<ZoneRef> Zones = new List<ZoneRef>();
        public int Signature;
        public bool Queued;
        public bool Built;
        public float Distance;
        public float RebuildAfter;
        public float RetiredAt;
        public int Instances;
        public int Vertices;
    }

    private struct ZoneRef
    {
        public int X;
        public int Y;
        public bool DistantOwned;
    }

    private static readonly int[] TileSizes = { 256, 512, 1024 };
    private const int TopLevel = 2;
    private const float Hysteresis = 0.08f;
    private const int MaxScansPerUpdate = 600;
    private const int MaxBakesPerFrame = 1;
    private const int StaleChecksPerFrame = 48;
    private const float RetireTimeoutSeconds = 6f;
    private const float NearContentDebounce = 0.3f;
    private const float FarContentDebounce = 5f;
    private const float FailedBuildRetrySeconds = 10f;

    private readonly ZoneObjectSource _source = new ZoneObjectSource();
    private ImpostorAtlas _atlas;
    private readonly Dictionary<TileKey, Tile> _tiles = new Dictionary<TileKey, Tile>();
    private readonly List<Tile> _retiring = new List<Tile>();
    private readonly Dictionary<TileKey, Band> _tileBand = new Dictionary<TileKey, Band>();
    private readonly Dictionary<TileKey, bool> _tilePieces = new Dictionary<TileKey, bool>();
    private readonly Dictionary<TileKey, bool> _tileRocks = new Dictionary<TileKey, bool>();
    private readonly HashSet<TileKey> _desiredKeys = new HashSet<TileKey>();
    private readonly Dictionary<int, TileKey> _zoneTile = new Dictionary<int, TileKey>();
    private readonly List<Tile> _queue = new List<Tile>();
    private readonly List<Placement> _placements = new List<Placement>();
    private readonly List<BuiltMesh> _built = new List<BuiltMesh>();
    private readonly List<ZoneRef> _refScratch = new List<ZoneRef>();
    private readonly List<TileKey> _keyScratch = new List<TileKey>();
    private readonly List<int> _dirtyScratch = new List<int>();
    private readonly Stopwatch _stopwatch = new Stopwatch();

    // Zones handed over to real objects (in game's near set AND fully instantiated). Zone come back to me only
    // when it leave near set, so nothing pop twice while game spawn things.
    private readonly HashSet<int> _handedOver = new HashSet<int>();
    private readonly HashSet<int> _distantHandedOver = new HashSet<int>();

    private GameObject _root;
    private Vector3 _lastPos = new Vector3(99999f, 0f, 99999f);
    private Vector2s _lastRefZone = new Vector2s(short.MinValue, short.MinValue);
    private float _timer;
    private bool _dirty = true;
    private bool _readinessPending;
    private float _readinessTimer;
    private int _bakesThisFrame;
    private int _staleCursor;
    private int _surfaceVersion;
    private float _surfaceTimer;
    private float _nextStatsLog;
    private bool _loggedFirstTile;
    private bool _atlasDumped;

    // Context of one recompute.
    private Vector3 _camPos;
    private Vector2s _refZone;
    private SimulationDistance _sim;
    private float _now;
    private int _scansThisUpdate;
    private bool _scanBudgetHit;

    public int TotalBuilds { get; private set; }

    private static DHConfig Cfg => Plugin.Cfg;

    // ------------------------------------------------------------------ lifecycle

    private void OnEnable()
    {
        Instance = this;
        _dirty = true;
        if (Cfg != null) Cfg.Changed += OnConfigChanged;
    }

    private void OnDisable()
    {
        if (Cfg != null) Cfg.Changed -= OnConfigChanged;
        SafeClearAll("OnDisable");
        if (Instance == this) Instance = null;
    }

    private void OnDestroy()
    {
        SafeClearAll("OnDestroy");
    }

    private void SafeClearAll(string site)
    {
        try
        {
            ClearAll();
        }
        catch (Exception e)
        {
            PatchGuard.Report("DistantObjectManager." + site, e);
        }
    }

    private void OnConfigChanged(BepInEx.Configuration.ConfigEntryBase entry)
    {
        DHConfig c = Cfg;
        if (c == null || entry == null) return;
        // Me wait until the setting stop changing (a ConfigurationManager slider change it every frame while dragged).
        if (entry == c.ImpostorResolution || entry == c.ImpostorShader || entry == c.ImpostorBakeBrightness || entry == c.ObjectsEnabled || entry == c.FarObjectWind)
        {
            _pendingReset = PendingReset.All; // atlas or material change: start over
            _resetAt = Time.unscaledTime + SettingDebounceSeconds;
        }
        else if (entry.Definition.Section == DHConfig.ObjectsSection)
        {
            if (_pendingReset < PendingReset.Bands) _pendingReset = PendingReset.Bands;
            _resetAt = Time.unscaledTime + SettingDebounceSeconds;
        }
    }

    private enum PendingReset : byte
    {
        None,
        Bands,
        All,
    }

    private const float SettingDebounceSeconds = 0.5f;
    private PendingReset _pendingReset;
    private float _resetAt;

    // Me apply settled setting change. True while one still settling (keep drawing what is there).
    private bool ApplyPendingReset()
    {
        if (_pendingReset == PendingReset.None) return false;
        if (Time.unscaledTime < _resetAt) return true;
        PendingReset what = _pendingReset;
        _pendingReset = PendingReset.None;
        if (what == PendingReset.All)
        {
            ClearAll();
            return false;
        }
        _tileBand.Clear();
        _tilePieces.Clear();
        _tileRocks.Clear();
        foreach (Tile t in _tiles.Values) t.Signature = 0;
        _dirty = true;
        return false;
    }

    public void ClearAll()
    {
        // Each part alone: throw while freeing tiles must never keep ZDO hook or bake rig alive.
        Step("tiles", () =>
        {
            foreach (Tile t in _tiles.Values) DestroyTile(t);
            foreach (Tile t in _retiring) DestroyTile(t);
        });
        _tiles.Clear();
        _retiring.Clear();
        _tileBand.Clear();
        _tilePieces.Clear();
        _tileRocks.Clear();
        _desiredKeys.Clear();
        _zoneTile.Clear();
        _queue.Clear();
        _handedOver.Clear();
        _distantHandedOver.Clear();
        _lastRefZone = new Vector2s(short.MinValue, short.MinValue);
        _surfaceVersion = 0;
        _source.Unhook();
        _source.Clear();
        Step("catalog", PrefabCatalog.Clear);
        Step("materials", ObjectTileBuilder.ClearMaterials);
        Step("atlas", () =>
        {
            if (_atlas != null)
            {
                ImpostorAtlas atlas = _atlas;
                _atlas = null;
                atlas.Dispose();
            }
        });
        if (_root != null)
        {
            Destroy(_root);
            _root = null;
        }
        _dirty = true;
    }

    private static void Step(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PatchGuard.Report("DistantObjectManager.ClearAll " + what, e);
        }
    }

    public void Rebuild()
    {
        ClearAll();
    }

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            PatchGuard.Report("DistantObjectManager.Update", e);
        }
    }

    private void Tick()
    {
        DHConfig cfg = Cfg;
        if (cfg == null || !cfg.ObjectsEnabled.Value)
        {
            _pendingReset = PendingReset.None;
            if (_tiles.Count > 0 || _retiring.Count > 0 || _atlas != null) ClearAll();
            return;
        }
        if (ApplyPendingReset()) return;
        if (ZDOMan.instance == null || ZoneSystem.instance == null || ZNetScene.instance == null) return;
        if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected) return;
        Camera cam = Utils.GetMainCamera();
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;

        _source.Hook();
        _bakesThisFrame = 0;
        if (_atlas == null) _atlas = new ImpostorAtlas(cfg.ImpostorResolution.Value);

        // Game's near/distant sets follow player's reference zone; me re-check the moment it change, so zones
        // behind player get far objects back as soon as real ones are destroyed.
        Vector2s refZone = ZoneSystem.GetZone(ZNet.instance != null ? ZNet.instance.GetReferencePosition() : camPos);
        if (refZone.x != _lastRefZone.x || refZone.y != _lastRefZone.y)
        {
            _lastRefZone = refZone;
            _dirty = true;
        }
        _readinessTimer += Time.deltaTime;
        if (_readinessPending && _readinessTimer >= 0.1f) _dirty = true; // some near zones still spawning

        // Objects sit on far terrain: its surface cut change = re-check (throttled while it stream in).
        _surfaceTimer += Time.deltaTime;
        LodTerrainManager terrain = LodTerrainManager.Instance;
        if (cfg.SnapObjectsToTerrain.Value && terrain != null && terrain.SurfaceVersion != _surfaceVersion && _surfaceTimer >= 0.25f)
        {
            _surfaceVersion = terrain.SurfaceVersion;
            _surfaceTimer = 0f;
            _dirty = true;
        }

        _timer += Time.deltaTime;
        if (_dirty || (_timer >= cfg.ObjectUpdateInterval.Value && Utils.DistanceXZ(camPos, _lastPos) >= cfg.UpdateStepDistance.Value))
        {
            _dirty = false;
            _timer = 0f;
            _readinessTimer = 0f;
            _lastPos = camPos;
            RecomputeDesired(camPos, refZone);
        }

        RefreshStale();
        ProcessQueue();
        RetireCheck();
        _atlas.ApplyIfDirty(_bakesThisFrame > 0);

        if (cfg.DebugLogging.Value && Time.unscaledTime >= _nextStatsLog)
        {
            _nextStatsLog = Time.unscaledTime + 30f;
            Log.Info(GetStats());
            if (!_atlasDumped && _atlas.Baked >= 4)
            {
                _atlasDumped = true;
                DumpAtlas();
            }
        }
    }

    // Me write impostor atlas as PNG next to BepInEx log and log card material's properties.
    public string DumpAtlas()
    {
        if (_atlas == null) return null;
        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "DistantHorizons_atlas.png");
        try
        {
            _atlas.DumpPng(path);
            Log.Info($"Impostor atlas written to {path}. Material: {_atlas.DescribeMaterial()}");
            return path;
        }
        catch (Exception e)
        {
            Log.Warning($"Could not write atlas PNG: {e.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------ game's own object sets

    private static bool InNearSet(Vector2s zone, Vector2s refZone, SimulationDistance sim)
    {
        int n = sim.NearSimulationDistance;
        if (sim.IsClassic) return Mathf.Max(Mathf.Abs(zone.x - refZone.x), Mathf.Abs(zone.y - refZone.y)) <= n;
        float r = n * 64f + 32f;
        return (ZoneSystem.GetZonePos(zone) - ZoneSystem.GetZonePos(refZone)).sqrMagnitude < r * r;
    }

    // Where vanilla instantiate Distant-flagged prefabs (big trees, big rocks).
    private static bool InDistantSet(Vector2s zone, Vector2s refZone, SimulationDistance sim)
    {
        int t = sim.TotalSimulationDistance;
        if (sim.IsClassic) return Mathf.Max(Mathf.Abs(zone.x - refZone.x), Mathf.Abs(zone.y - refZone.y)) <= t;
        float r = t * 64f + 64f * 0.8f;
        return (ZoneSystem.GetZonePos(zone) - ZoneSystem.GetZonePos(refZone)).sqrMagnitude < r * r;
    }

    // True once (nearly) every drawable object stored for zone have live instance.
    private bool ZoneReady(int zx, int zy)
    {
        ZNetScene scene = ZNetScene.instance;
        if (scene == null) return true;
        int missing = 0, total = 0;
        IReadOnlyList<ZDO> list = _source.EnumerateSector(zx, zy);
        for (int i = 0; i < list.Count; i++)
        {
            ZDO zdo = list[i];
            if (!zdo.Persistent) continue;
            if (PrefabCatalog.Get(zdo.GetPrefab()).Kind == ObjectKind.Skip) continue;
            total++;
            if (!scene.HaveInstance(zdo)) missing++;
        }
        return missing <= 2 || missing * 50 <= total;
    }

    // True once every Distant-flagged object stored for zone have live instance.
    private bool DistantReady(int zx, int zy)
    {
        ZNetScene scene = ZNetScene.instance;
        if (scene == null) return true;
        int missing = 0;
        IReadOnlyList<ZDO> list = _source.EnumerateSector(zx, zy);
        for (int i = 0; i < list.Count; i++)
        {
            ZDO zdo = list[i];
            if (!zdo.Persistent || !zdo.Distant) continue;
            if (PrefabCatalog.Get(zdo.GetPrefab()).Kind == ObjectKind.Skip) continue;
            if (!scene.HaveInstance(zdo)) missing++;
        }
        return missing <= 1;
    }

    // Real objects own this zone? Sticky while zone stay in near set.
    private bool HandedOver(int zx, int zy)
    {
        int key = ZoneObjectSource.ZoneKey(zx, zy);
        if (!InNearSet(new Vector2s(zx, zy), _refZone, _sim))
        {
            // Released: anything built or felled while game owned it = unknown to snapshot.
            if (_handedOver.Count > 0 && _handedOver.Remove(key)) _source.MarkDirty(zx, zy);
            return false;
        }
        if (_handedOver.Contains(key)) return true;
        if (ZoneReady(zx, zy))
        {
            _handedOver.Add(key);
            return true;
        }
        _readinessPending = true;
        return false;
    }

    // Vanilla's own instances of Distant-flagged prefabs own this zone? Sticky while in ring.
    private bool DistantHandedOver(int zx, int zy)
    {
        int key = ZoneObjectSource.ZoneKey(zx, zy);
        if (!InDistantSet(new Vector2s(zx, zy), _refZone, _sim))
        {
            if (_distantHandedOver.Count > 0 && _distantHandedOver.Remove(key)) _source.MarkDirty(zx, zy);
            return false;
        }
        if (_distantHandedOver.Contains(key)) return true;
        if (DistantReady(zx, zy))
        {
            _distantHandedOver.Add(key);
            return true;
        }
        _readinessPending = true;
        return false;
    }

    // ------------------------------------------------------------------ desired tiles

    private static float AabbDist(float px, float pz, float minX, float minZ, float maxX, float maxZ)
    {
        float dx = Mathf.Max(minX - px, 0f, px - maxX);
        float dz = Mathf.Max(minZ - pz, 0f, pz - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static int FloorDiv(float v, int size) => Mathf.FloorToInt(v / size);

    private Band ComputeBand(float d, float mul)
    {
        DHConfig c = Cfg;
        if (d < c.ObjectMeshDistance.Value * mul) return Band.Mesh;
        if (d < c.ObjectFullDistance.Value * mul) return Band.Full;
        if (d < c.ObjectThinDistance.Value * mul) return Band.Thin;
        if (d < c.ObjectFarDistance.Value * mul) return Band.Far;
        return Band.None;
    }

    private Band TileBand(TileKey key, float d)
    {
        Band raw = ComputeBand(d, 1f);
        if (!_tileBand.TryGetValue(key, out Band prev) || prev == Band.None || prev == raw)
        {
            if (raw == Band.None) _tileBand.Remove(key);
            else _tileBand[key] = raw;
            return raw;
        }
        // Moving outward (coarser) or inward (finer): change only once clearly past boundary.
        Band next = raw > prev ? ComputeBand(d, 1f + Hysteresis) : ComputeBand(d, 1f - Hysteresis);
        if (next == Band.None) _tileBand.Remove(key);
        else _tileBand[key] = next;
        return next;
    }

    private static bool WithinHyst(Dictionary<TileKey, bool> memory, TileKey key, float d, float threshold)
    {
        bool inside;
        if (memory.TryGetValue(key, out bool prev))
            inside = prev ? d < threshold * (1f + Hysteresis) : d < threshold * (1f - Hysteresis);
        else
            inside = d < threshold;
        memory[key] = inside;
        return inside;
    }

    private void ForgetTile(TileKey key)
    {
        _tileBand.Remove(key);
        _tilePieces.Remove(key);
        _tileRocks.Remove(key);
    }

    private void RecomputeDesired(Vector3 camPos, Vector2s refZone)
    {
        DHConfig cfg = Cfg;
        _camPos = camPos;
        _refZone = refZone;
        _sim = ZNet.instance != null ? ZNet.instance.GetSyncedSimulationDistance() : SimulationDistance.OriginalDistance;
        _now = Time.unscaledTime;
        _scansThisUpdate = 0;
        _scanBudgetHit = false;
        _readinessPending = false;
        _desiredKeys.Clear();
        _zoneTile.Clear();

        float limit = cfg.ObjectFarDistance.Value * (1f + Hysteresis) + 64f;
        int size = TileSizes[TopLevel];
        int minX = FloorDiv(camPos.x - limit, size), maxX = FloorDiv(camPos.x + limit, size);
        int minZ = FloorDiv(camPos.z - limit, size), maxZ = FloorDiv(camPos.z + limit, size);
        for (int ty = minZ; ty <= maxZ; ty++)
            for (int tx = minX; tx <= maxX; tx++)
                Visit(new TileKey(TopLevel, tx, ty));

        if (_scanBudgetHit) _dirty = true; // keep filling next frame

        // Tiles no longer wanted keep drawing until replacement built (see RetireCheck).
        _keyScratch.Clear();
        foreach (KeyValuePair<TileKey, Tile> kv in _tiles)
            if (!_desiredKeys.Contains(kv.Key)) _keyScratch.Add(kv.Key);
        foreach (TileKey k in _keyScratch)
        {
            Tile t = _tiles[k];
            _tiles.Remove(k);
            if (t.Queued)
            {
                _queue.Remove(t);
                t.Queued = false;
            }
            if (t.Built && t.Go != null)
            {
                t.RetiredAt = _now;
                _retiring.Add(t);
            }
            else DestroyTile(t);
        }
        _queue.Sort((a, b) => a.Distance.CompareTo(b.Distance));
    }

    private void Visit(TileKey key)
    {
        DHConfig cfg = Cfg;
        int size = TileSizes[key.Level];
        float minX = key.X * size, minZ = key.Y * size, maxX = minX + size, maxZ = minZ + size;
        if (AabbDist(0f, 0f, minX, minZ, maxX, maxZ) > cfg.WorldRadius.Value)
        {
            ForgetTile(key);
            return;
        }
        float d = AabbDist(_camPos.x, _camPos.z, minX, minZ, maxX, maxZ);
        Band band = TileBand(key, d);
        if (band == Band.None)
        {
            ForgetTile(key);
            return;
        }

        bool split = key.Level == TopLevel ? band <= Band.Full : (key.Level > 0 && band == Band.Mesh);
        if (split)
        {
            for (int i = 0; i < 4; i++) Visit(key.Child(i));
            return;
        }

        bool piecesOn = cfg.DrawPieces.Value && WithinHyst(_tilePieces, key, d, cfg.PieceDistance.Value);
        bool rocksOn = cfg.DrawRocks.Value && WithinHyst(_tileRocks, key, d, cfg.RockDistance.Value);

        int sig = 17;
        unchecked
        {
            sig = sig * 31 + (int)band;
            sig = sig * 31 + (piecesOn ? 1 : 0);
            sig = sig * 31 + (rocksOn ? 1 : 0);
            // Near tiles follow terrain under them when it refine; far cards seated once at build time
            // (metre of error at 2 km = below a pixel, and rebuilding them all = expensive).
            LodTerrainManager terrain = LodTerrainManager.Instance;
            if (cfg.SnapObjectsToTerrain.Value && terrain != null && band <= Band.Full)
            {
                sig = sig * 31 + TerrainKeyHash(terrain, minX + 1f, minZ + 1f);
                sig = sig * 31 + TerrainKeyHash(terrain, maxX - 1f, minZ + 1f);
                sig = sig * 31 + TerrainKeyHash(terrain, minX + 1f, maxZ - 1f);
                sig = sig * 31 + TerrainKeyHash(terrain, maxX - 1f, maxZ - 1f);
                sig = sig * 31 + TerrainKeyHash(terrain, (minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            }
        }

        // Zones with centre inside tile (centres sit at multiples of 64 m).
        int zx0 = Mathf.RoundToInt(minX / 64f), zx1 = Mathf.RoundToInt(maxX / 64f) - 1;
        int zy0 = Mathf.RoundToInt(minZ / 64f), zy1 = Mathf.RoundToInt(maxZ / 64f) - 1;
        _refScratch.Clear();
        for (int zy = zy0; zy <= zy1; zy++)
        {
            for (int zx = zx0; zx <= zx1; zx++)
            {
                if (HandedOver(zx, zy)) continue; // real objects own this zone
                ZoneObjects zo = GetZoneObjects(zx, zy);
                if (zo == null) continue;
                // Objects made since scan (generation, arrivals, builds) change live list length.
                if (_source.EnumerateSector(zx, zy).Count != zo.LiveCount) _source.MarkDirty(zx, zy);
                if (zo.Items.Count == 0) continue;
                bool distantOwned = DistantHandedOver(zx, zy);
                _refScratch.Add(new ZoneRef { X = zx, Y = zy, DistantOwned = distantOwned });
                int zkey = ZoneObjectSource.ZoneKey(zx, zy);
                _zoneTile[zkey] = key;
                unchecked
                {
                    sig = sig * 31 + zkey;
                    sig = sig * 31 + zo.ContentHash;
                    sig = sig * 31 + (distantOwned ? 1 : 0);
                }
            }
        }

        _desiredKeys.Add(key);
        if (!_tiles.TryGetValue(key, out Tile tile))
        {
            // Retiring tile with same key come back as is.
            for (int i = 0; i < _retiring.Count; i++)
            {
                if (_retiring[i].Key != key) continue;
                tile = _retiring[i];
                _retiring.RemoveAt(i);
                break;
            }
            if (tile == null) tile = new Tile { Key = key };
            _tiles[key] = tile;
        }
        tile.Distance = d;
        tile.Band = band;
        tile.PiecesOn = piecesOn;
        tile.RocksOn = rocksOn;
        if (tile.Signature != sig || !tile.Built)
        {
            tile.Signature = sig;
            tile.Zones.Clear();
            tile.Zones.AddRange(_refScratch);
            Enqueue(tile);
        }
    }

    private ZoneObjects GetZoneObjects(int zx, int zy)
    {
        if (_source.TryGetCached(zx, zy, out ZoneObjects zo)) return zo;
        if (_scansThisUpdate >= MaxScansPerUpdate)
        {
            _scanBudgetHit = true;
            return null;
        }
        _scansThisUpdate++;
        return _source.Get(zx, zy, _now);
    }

    // Me keep cached zones current, never re-read them all at once: zones marked dirty (destroyed objects,
    // released hand-overs, changed live counts) first, then a few of oldest each frame. Change only invalidate
    // tile that draw the zone, with debounce so remote events cannot thrash far tiles.
    private void RefreshStale()
    {
        float now = Time.unscaledTime;
        float maxAge = Cfg.ObjectCacheSeconds.Value;

        _dirtyScratch.Clear();
        _source.TakeDirty(_dirtyScratch);
        foreach (int key in _dirtyScratch)
            if (_source.Refresh(key, now)) InvalidateZone(key, now);

        int count = _source.KeyCount;
        for (int i = 0; i < StaleChecksPerFrame && count > 0; i++)
        {
            if (_staleCursor >= count) _staleCursor = 0;
            int key = _source.KeyAt(_staleCursor++);
            if (_source.IsStale(key, now, maxAge) && _source.Refresh(key, now)) InvalidateZone(key, now);
        }
    }

    private void InvalidateZone(int zoneKey, float now)
    {
        if (_zoneTile.TryGetValue(zoneKey, out TileKey tk) && _tiles.TryGetValue(tk, out Tile tile))
        {
            tile.Signature = 0;
            float debounce = tile.Band >= Band.Thin ? FarContentDebounce : NearContentDebounce;
            if (!tile.Queued || tile.RebuildAfter < now + debounce) tile.RebuildAfter = now + debounce;
            Enqueue(tile);
        }
        else
        {
            _dirty = true; // zone that was empty (no tile map it) got content
        }
    }

    private void Enqueue(Tile t)
    {
        if (t.Queued) return;
        t.Queued = true;
        _queue.Add(t);
    }

    // ------------------------------------------------------------------ building

    private void ProcessQueue()
    {
        if (_queue.Count == 0) return;
        float budget = Cfg.ObjectBuildBudgetMs.Value;
        float now = Time.unscaledTime;
        _stopwatch.Restart();
        int passes = _queue.Count; // each tile at most once per frame
        while (passes-- > 0 && _queue.Count > 0 && _stopwatch.Elapsed.TotalMilliseconds < budget)
        {
            Tile t = _queue[0];
            _queue.RemoveAt(0);
            t.Queued = false;
            if (!_desiredKeys.Contains(t.Key)) continue;
            if (now < t.RebuildAfter)
            {
                Enqueue(t); // debounced content change, not yet due
                continue;
            }
            try
            {
                if (!BuildTile(t)) Enqueue(t); // wait for impostor bakes; try again next frame
            }
            catch (Exception e)
            {
                FailBuild(t, e, now);
            }
        }
    }

    // Me throw mid-build (a prefab mesh another mod destroyed, ...): free what was half made, try that tile later.
    private void FailBuild(Tile t, Exception e, float now)
    {
        PatchGuard.Report("DistantObjectManager.BuildTile", e);
        foreach (BuiltMesh bm in _built)
            if (bm.Mesh != null && !t.Meshes.Contains(bm.Mesh)) Destroy(bm.Mesh);
        _built.Clear();
        _placements.Clear();
        DestroyMeshes(t);
        t.Built = false;
        t.RebuildAfter = now + FailedBuildRetrySeconds;
        Enqueue(t);
    }

    // Me destroy retired tiles once tiles now covering their zones are built (or after timeout).
    private void RetireCheck()
    {
        if (_retiring.Count == 0) return;
        float now = Time.unscaledTime;
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            Tile t = _retiring[i];
            bool covered = true;
            if (now - t.RetiredAt < RetireTimeoutSeconds)
            {
                foreach (ZoneRef z in t.Zones)
                {
                    if (!_zoneTile.TryGetValue(ZoneObjectSource.ZoneKey(z.X, z.Y), out TileKey tk)) continue; // nobody draw it now
                    if (!_tiles.TryGetValue(tk, out Tile owner) || !owner.Built)
                    {
                        covered = false;
                        break;
                    }
                }
            }
            if (!covered) continue;
            DestroyTile(t);
            _retiring.RemoveAt(i);
        }
    }

    private void EnsureRoot()
    {
        if (_root != null) return;
        _root = new GameObject("DistantHorizons_Objects");
        _root.transform.position = Vector3.zero;
    }

    private static int PositionHash(Vector3 p)
    {
        unchecked
        {
            int h = Mathf.RoundToInt(p.x * 8f) * 73856093 ^ Mathf.RoundToInt(p.z * 8f) * 19349663;
            return h & 0x7fffffff;
        }
    }

    private static int TerrainKeyHash(LodTerrainManager terrain, float x, float z)
    {
        return terrain.TrySampleSurface(x, z, out _, out TileKey key) ? key.GetHashCode() : 0;
    }

    // Far-terrain height minus true generated height at zone centre = LOD's own error there.
    // Rocks and buildings shift by it as a block, so building on slope get moved, never distorted.
    private static float ZoneGroundDelta(LodTerrainManager terrain, ref LodTile hint, int zx, int zy)
    {
        WorldGenerator wg = WorldGenerator.instance;
        if (wg == null) return 0f;
        float cx = zx * 64f, cz = zy * 64f;
        if (!terrain.TrySampleSurface(cx, cz, ref hint, out float lod, out _)) return 0f;
        return lod - wg.GetHeight(cx, cz);
    }

    // Me return false when some impostor bakes got deferred to later frame.
    private bool BuildTile(Tile tile)
    {
        DHConfig cfg = Cfg;
        bool deferred = false;
        int thinFactor = Mathf.Max(1, cfg.ObjectThinFactor.Value);
        int farFactor = Mathf.Max(1, cfg.ObjectFarFactor.Value);
        LodTerrainManager terrain = cfg.SnapObjectsToTerrain.Value ? LodTerrainManager.Instance : null;
        bool nonTreesPossible = tile.Band == Band.Mesh || tile.RocksOn || tile.PiecesOn;
        LodTile hint = null;

        _placements.Clear();
        foreach (ZoneRef r in tile.Zones)
        {
            if (!_source.TryGetCached(r.X, r.Y, out ZoneObjects zo)) continue;
            float groundDelta = terrain != null && nonTreesPossible ? ZoneGroundDelta(terrain, ref hint, r.X, r.Y) : 0f;
            foreach (ObjectInstance src in zo.Items)
            {
                ObjectInstance o = src;
                PrefabInfo info = PrefabCatalog.Get(o.PrefabHash);
                if (info.Kind == ObjectKind.Skip) continue;
                if (r.DistantOwned && info.Distant) continue; // vanilla draw real one
                bool isTree = info.Kind == ObjectKind.Tree;
                bool bigTree = isTree && info.Height >= cfg.BigTreeHeight.Value;
                bool cardTree = isTree && cfg.DrawTrees.Value && (bigTree || cfg.SmallTreeCards.Value);
                bool bigRock = info.Kind == ObjectKind.Rock && Mathf.Max(info.Height, info.Radius * 2f) >= cfg.BigRockSize.Value;

                // Decide first; sample terrain only for what really get drawn.
                bool draw = false, card = false;
                float mul = 1f;
                switch (tile.Band)
                {
                    case Band.Mesh:
                        if (!KindEnabled(info.Kind, cfg) || info.FarLod.Count == 0) continue;
                        draw = true;
                        card = info.FarLodVertices > cfg.MaxVertsPerObject.Value && info.WantsImpostor;
                        break;
                    case Band.Full:
                    case Band.Thin:
                    case Band.Far:
                        if (isTree)
                        {
                            if (!cardTree) continue;
                            if (tile.Band == Band.Thin)
                            {
                                if (PositionHash(o.Position) % thinFactor != 0) continue;
                                mul = cfg.ObjectThinScale.Value;
                            }
                            else if (tile.Band == Band.Far)
                            {
                                if (PositionHash(o.Position) % farFactor != 0) continue;
                                mul = cfg.ObjectFarScale.Value;
                            }
                            draw = true;
                            card = true;
                        }
                        else if (info.Kind == ObjectKind.Rock && tile.RocksOn && bigRock && info.FarLod.Count > 0) draw = true;
                        else if (info.Kind == ObjectKind.Piece && tile.PiecesOn && info.FarLod.Count > 0) draw = true;
                        break;
                }
                if (!draw) continue;
                if (card && !TryCard(info, ref deferred)) continue;

                if (terrain != null)
                {
                    // Trees stand on far terrain; everything else shift with zone's ground offset.
                    if (isTree && terrain.TrySampleSurface(o.Position.x, o.Position.z, ref hint, out float ground, out _))
                        o.Position.y = ground;
                    else if (!isTree)
                        o.Position.y += groundDelta;
                }
                _placements.Add(new Placement { Info = info, Position = o.Position, Rotation = o.Rotation, Scale = o.Scale, ScaleMul = mul, Card = card });
            }
        }

        // Nothing new can draw until more bakes happen: keep what is on screen.
        if (deferred && tile.Built)
        {
            _placements.Clear();
            return false;
        }

        EnsureRoot();
        DestroyMeshes(tile);
        if (tile.Go == null)
        {
            tile.Go = new GameObject("DH_obj_" + tile.Key);
            tile.Go.transform.SetParent(_root.transform, false);
        }
        _built.Clear();
        if (_placements.Count > 0)
            ObjectTileBuilder.Build(_placements, _atlas, cfg.MaxVertsPerMesh.Value, _built, cfg.FarObjectWind.Value);

        tile.Instances = 0;
        tile.Vertices = 0;
        foreach (BuiltMesh bm in _built)
        {
            var go = new GameObject(bm.Material != null ? bm.Material.name : "mesh");
            go.transform.SetParent(tile.Go.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = bm.Mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bm.Material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            if (bm.Material != null && bm.Material != _atlas.Material) ApplyPieceDefaults(mr, bm.Material);
            RenderGroupSubscriber sub = go.AddComponent<RenderGroupSubscriber>();
            sub.Group = RenderGroup.Overworld;
            tile.Meshes.Add(bm.Mesh);
            tile.Instances += bm.Instances;
            tile.Vertices += bm.Mesh.vertexCount;
        }
        _built.Clear();
        _placements.Clear();
        tile.Built = true;
        TotalBuilds++;

        if (!_loggedFirstTile && tile.Instances > 0)
        {
            _loggedFirstTile = true;
            Log.Info($"First object tile built: {tile.Key} band {tile.Band} with {tile.Instances} objects, {tile.Vertices} vertices; impostor shader {_atlas.ShaderName}.");
        }
        return !deferred;
    }

    private static readonly int s_rippleDistance = Shader.PropertyToID("_RippleDistance");
    private static readonly int s_valueNoise = Shader.PropertyToID("_ValueNoise");
    private static readonly int s_snowLevel = Shader.PropertyToID("_SnowLevel");
    private static readonly MaterialPropertyBlock s_block = new MaterialPropertyBlock();

    // Game write these per real piece (build ripple, noise, snow build-up); merged meshes would else show
    // material's authoring defaults.
    private static void ApplyPieceDefaults(MeshRenderer mr, Material m)
    {
        bool any = false;
        s_block.Clear();
        if (m.HasProperty(s_rippleDistance)) { s_block.SetFloat(s_rippleDistance, 0f); any = true; }
        if (m.HasProperty(s_valueNoise)) { s_block.SetFloat(s_valueNoise, 0f); any = true; }
        if (m.HasProperty(s_snowLevel)) { s_block.SetFloat(s_snowLevel, 0f); any = true; }
        if (any) mr.SetPropertyBlock(s_block);
    }

    private static bool KindEnabled(ObjectKind kind, DHConfig cfg)
    {
        switch (kind)
        {
            case ObjectKind.Tree: return cfg.DrawTrees.Value;
            case ObjectKind.Bush: return cfg.DrawBushes.Value;
            case ObjectKind.Rock: return cfg.DrawRocks.Value;
            case ObjectKind.Piece: return cfg.DrawPieces.Value;
            case ObjectKind.Log: return cfg.DrawLogs.Value;
            case ObjectKind.Other: return cfg.DrawBushes.Value;
            default: return false;
        }
    }

    private bool TryCard(PrefabInfo info, ref bool deferred)
    {
        if (info.AtlasCell >= 0) return true;
        if (info.AtlasCell == -2) return false;
        if (_bakesThisFrame >= MaxBakesPerFrame)
        {
            deferred = true;
            return false;
        }
        _bakesThisFrame++;
        return _atlas.TryBake(info);
    }

    private static void DestroyMeshes(Tile t)
    {
        foreach (Mesh m in t.Meshes) if (m != null) Destroy(m);
        t.Meshes.Clear();
        if (t.Go != null)
        {
            for (int i = t.Go.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = t.Go.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }
    }

    private static void DestroyTile(Tile t)
    {
        DestroyMeshes(t);
        if (t.Go != null)
        {
            Destroy(t.Go);
            t.Go = null;
        }
        t.Built = false;
    }

    // ------------------------------------------------------------------ stats

    public string GetStats()
    {
        int tiles = 0, built = 0, instances = 0, verts = 0;
        var perBand = new int[5];
        foreach (Tile t in _tiles.Values)
        {
            tiles++;
            if (!t.Built) continue;
            built++;
            instances += t.Instances;
            verts += t.Vertices;
            perBand[(int)t.Band]++;
        }
        var sb = new StringBuilder();
        sb.Append("DistantObjects: tiles=").Append(tiles).Append(" built=").Append(built)
          .Append(" retiring=").Append(_retiring.Count)
          .Append(" queued=").Append(_queue.Count)
          .Append(" objects=").Append(instances).Append(" verts=").Append(verts)
          .Append(" perBand(mesh/full/thin/far)=").Append(perBand[1]).Append('/').Append(perBand[2]).Append('/').Append(perBand[3]).Append('/').Append(perBand[4])
          .Append(" zonesCached=").Append(_source.CachedZones).Append(" scans=").Append(_source.Scans)
          .Append(" handedOver=").Append(_handedOver.Count).Append('/').Append(_distantHandedOver.Count)
          .Append(" prefabs=").Append(PrefabCatalog.Count)
          .Append(" impostors=").Append(_atlas != null ? _atlas.Baked : 0).Append('/').Append(_atlas != null ? _atlas.Capacity : 0)
          .Append(" bakeFailed=").Append(_atlas != null ? _atlas.Failed : 0)
          .Append(" shader=").Append(_atlas != null ? _atlas.ShaderName : "none")
          .Append(" totalBuilds=").Append(TotalBuilds);
        return sb.ToString();
    }
}
