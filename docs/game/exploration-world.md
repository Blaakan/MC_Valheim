# Exploration - World, Biomes & Content

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> Conventions: code is cited as `Class.Method` (files live in `.ref/decompiled/assembly_valheim/<Class>.cs`). "Owner" = the peer that owns a ZDO (`ZNetView.IsOwner()`), usually the client physically closest to the object. "Server" = dedicated server or the hosting client. Prefab/asset names quoted from `valheim_Data/StreamingAssets/SoftRef/manifest_extended` are *asset paths*, not verified component setups: inspect them at runtime before relying on them.

---

## Overview

Exploration content in Valheim is produced by a small number of deterministic, seed-driven generators plus a runtime layer that is split across peers:

| Layer | Main classes | Who computes it |
|---|---|---|
| Biome map / heights | `WorldGenerator`, `AltBiomeWorldData`, `BiomeSector`, `AltBiome`, `HeightmapBuilder`, `Heightmap`, `TerrainLod` | Every peer, deterministically from `World.m_seed` (never networked) |
| Zones, vegetation, locations, dungeons | `ZoneSystem`, `LocationList`, `Location`, `LocationProxy`, `DungeonGenerator`, `DungeonDB`, `Room` | Server generates (Full/Ghost mode); clients rebuild visuals from ZDO data (Client mode) |
| World progression flags | `ZoneSystem` global keys, `GlobalKeys` enum | Server authoritative, broadcast to all |
| Spawning | `SpawnSystem` (on `_ZoneCtrl`), `CreatureSpawner`, `SpawnArea`, `TriggerSpawner`, `RandEventSystem` | ZDO owner of the spawner = a **client** (dedicated servers never run natural spawns) |
| World events | `RandEventSystem` (raids + boss "forced" events), `PersistentEventSystem` (new in Bog Witch / 1.0) | Server picks & saves; clients mirror |
| Weather, day/night, wind | `EnvMan`, `EnvSetup`, `BiomeEnvSetup`, `EnvZone` | Each client locally (deterministic from net time + camera position) |
| Rendering distance | `SimulationDistance`, `ZNetScene`, `ZDOMan.FindSectorObjects`, `TerrainLod`, `Water`, `ClutterSystem` | Client (server caps simulation distance) |
| Bosses | `OfferingBowl`, `BossStone`, `ItemStand`, `Character` (`m_boss`, `m_bossEvent`), `MonsterAI`, `EnemyHud` | Altar owner spawns; boss owner simulates |
| Lore / discovery | `Vegvisir`, `RuneStone`, `Game.DiscoverClosestLocation`, `Minimap`, `Raven`, `DreamTexts` | Client asks server for location position |
| Traders | `Trader`, `StoreGui`, `NpcTalk` | Purely local client logic (no networking at all) |
| Ocean | `WaterVolume`, `Floating`, `Heightmap.GetOceanDepth`, `Character.UpdateSwimming`, `GameCamera` | Local (waves are deterministic from wrapped day time + wind) |

Useful vanilla console commands while developing (all in `Terminal`): `env` (no arg lists current + available + all environment names), `resetenv`, `wind [angle] [intensity]`, `biomeinfo` (sector + AltBiome modifiers at the player), `pevents list|start|stop|here`, `event <name>`, `location <name> [SAVE]`, `genloc`, `find <text>`, `listkeys`, `setkey <key>`, `spawn <prefab> [amount] [level]`.

---

## 1. World generation (`WorldGenerator`)

### Key classes
- `WorldGenerator` - pure-math generator: biome at (x,z), terrain height, rivers/streams/lakes, forest factor. Singleton created by `WorldGenerator.Initialize(World)`.
- `World` - `m_seed`, `m_seedName`, `m_worldGenVersion`, `m_name`, `m_startingGlobalKeys`, `m_biomeData` (`AltBiomeWorldData`), `m_worldVersion` (`Version.World`, currently `DeepNorth = 41`).
- `DUtils`/`FastNoise` - Perlin & cellular noise helpers.

### Flow
1. `ZNet.Start` → (server) `ZNet.ServerLoadWorld` → `ZoneSystem.Load` → `AltBiomeWorldData.VerifyBiomeData` → `ZoneSystem.GenerateLocationsIfNeeded`. Clients: world params arrive in the peer-info package, then `WorldGenerator.Initialize` + `AltBiomeWorldData.VerifyBiomeData` run locally (`ZNet` ~line 1116).
2. `WorldGenerator` ctor: seeds `UnityEngine.Random` with `m_seed`, draws offsets `m_offset0..4`, river/stream seeds, then `Pregenerate` (`FindLakes`, `PlaceRivers`, `PlaceStreams(false)`, `PlaceStreams(true)` for Deep North).
3. `WorldGenerator.GetBiome(wx, wy, oceanLevel=0.02, waterAlwaysOcean=false)` decides the biome **in this order** (the core rule):
   1. `IsAshlands(x,y)` - `Length(x, y-4000) > 12000 + WorldAngle*100`, i.e. farther than 12 km from (0, +4000): everything south of roughly z = -8000 → AshLands. Checked *before* ocean, so the Ashlands sea is AshLands biome.
   2. base height ≤ 0.02 → Ocean.
   3. `IsDeepnorth(x,y)` - `Length(x, y+4000) > 12000 + WorldAngle*100`, i.e. farther than 12 km from (0, -4000): north of roughly z = +8000 → DeepNorth.
   4. base height > 0.4 → Mountain.
   5. Perlin(offset0) > 0.6, 2000 < dist < `maxMarshDistance` (6000; 8000 for worldGen v≤1), 0.05 < baseHeight < 0.25 → Swamp.
   6. Perlin(offset4) > `minDarklandNoise` (0.4; 0.5 for v≤1), 6000+angle < dist < 10000 → Mistlands.
   7. Perlin(offset1) > 0.4, 3000+angle < dist < 8000 → Plains.
   8. Perlin(offset2) > 0.4, 600+angle < dist < 6000 → BlackForest; else dist > 5000+angle → BlackForest.
   9. Meadows.
   `WorldAngle(x,y) = sin(atan2(x,y)*20)`, so all rings wobble ±100 m.
4. `WorldGenerator.GetBiomeHeight(biome, wx, wy, out mask, ...)` dispatches to `GetMeadowsHeight`, `GetForestHeight`, `GetMarshHeight`, `GetPlainsHeight`, `GetSnowMountainHeight`, `GetMistlandsHeight`, `GetAshlandsHeight`, `GetDeepNorthHeight`, `GetOceanHeight`; multiplies by `GetHeightMultiplier()` (200) and by `CreateAshlandsGap`/`CreateDeepNorthGap` (the ocean moats around the two end-game continents). The `mask` color carries vegetation density (alpha) used by location/vegetation filters, and lava/snow data.
5. `GetBaseHeight` blends to ocean between radius 10000 and 10500 (`worldSize`, `waterEdge`) and to -2 at the very edge (world-edge drop).

### Data & persistence
- Nothing from `WorldGenerator` is saved: it is a pure function of `m_seed` + `m_worldGenVersion`. Changing its output changes *terrain of zones not yet modified*; terrain edits are stored separately in `TerrainComp` ZDOs (`ZDOVars.s_TCData`) as deltas relative to generator output, so changing generator math under an existing base corrupts it.
- Client-side caches that must be invalidated if you alter generation: minimap textures (`Minimap`, regenerated when `Version.World` mismatches) and `<save>/cache/<world>_biomedatacache.bin` (`AltBiomeWorldData.RemoveCache`; note 1.0.16 already deletes and rebuilds it on every load in `VerifyBiomeData`).

### Multiplayer authority
Every peer computes the same result locally; any Harmony change to `GetBiome`/`GetBiomeHeight` **must be installed identically on every peer** or collision/terrain/biome logic desyncs.

### Constants
`c_HeightMultiplier 200`, `worldSize 10000`, `waterEdge 10500`, `ashlandsMinDistance 12000`, `ashlandsYOffset -4000`, `deepNorthMinDistance 12000`, `deepNorthYOffset 4000`, `m_minMountainDistance 1000`, rivers 60–100 m wide, 3000 streams of 20 m, river grid 64.

### Patch points
- `WorldGenerator.GetBiome(float,float,float,bool)` - postfix to reshape biomes (e.g. carve a new region). **Hot path, called from the `HeightmapBuilder` worker thread** (`HeightmapBuilder.BuildThread`) and ~4M times at load by `AltBiomeWorldData.GenerateBiomePoints`: must be thread-safe, allocation-free, no Unity API.
- `WorldGenerator.GetBiomeHeight` - postfix for height tweaks (same threading rules).
- `WorldGenerator.VersionSetup` - where legacy constants are switched by world-gen version.
- `WorldGenerator.GetBiomeSector` - map lookup into the sector grid (cheap, main-thread safe).

---

## 2. Biome sectors and AltBiomes (sub-biome "modifiers")

New in 1.0: the world is flood-filled into contiguous **sectors** per biome, and each sector may receive **AltBiome modifiers** that add/block spawns, vegetation, locations and environments, rename the biome and change level-up chance. Vanilla ships `Assets/Systems/AltBiomeLists/AltBiomes_Erik.prefab`.

### Key classes
- `AltBiomeWorldData` - 2048×2048 grid, 12 m per cell (`c_pixelSize`), storing biome index + height per cell (`PointBiomes`, `PointHeights`) and the `BiomeSector` per cell (`PointSectors`). Also `Biomes` (per-biome `BiomeTypeInfo`: `Sectors`, `AllPoints`, `AllPointsAboveSeaLevel`).
- `BiomeSector` - one contiguous region: `Biome`, `EdgeCount`, `Center`, `Min/Max`, `HeightMin/Max/Avg`, `Neighbors`, `DistanceFromCenter`, `AltBiomes`. `GetName()` builds `$biome_<name>` plus modifier prefix/suffix/override. `GetLevelUpChanceMultiplier()`.
- `AltBiome` (serializable data): `m_biome` (flags it can apply to), naming (`m_namePrefix`, `m_nameSuffix`, `m_nameOverride`), overrides (`m_levelUpChanceMultiplier`, `m_forceMusic`, `m_forceEnvironment`, `m_addEnvironments`, `m_blockEnvironments`, `m_spawn`, `m_blockSpawnNames`, `m_addVegetation`, `m_blockVegetationNames`, `m_addLocations`, `m_blockLocationNames`, `m_terrainTextureOverride`), placement rules (`m_minDistanceFromCenter`, `m_min/maxAmountSpawned`, `m_chance`, `m_requireNeighbor`, `m_notNeighbor`, `m_incompatibleAltBiomes`, `m_min/maxEdgeSize`, `m_min/maxAvgHeight`, `m_above/belowWorldX/Y`). Height-changing fields exist but are hidden/disabled (`heightMapChanges = false`).
- `AltBiomeList` (MonoBehaviour) - `m_alts`; `Awake` appends to static `AltBiomeList.m_altBiomes`. Instantiated from `ZoneSystem.m_altBiomeLists` in `ZoneSystem.Awake`.

### Flow
1. `AltBiomeWorldData.VerifyBiomeData` → `GenerateBiomePoints` (samples `WorldGenerator.GetBiome/GetBiomeHeight` on the grid; beyond radius 10500 forced Ocean) → `GenerateSectors` (AshLands, DeepNorth and Ocean are each **one global sector**; other biomes flood-filled) → `GenerateAltBiomes` (seeded with world seed + 920, then per biome+modifier name hash; shuffles sectors, applies `BiomeSector.CanAddModifier` then `AddModifier`).
2. Consumers:
   - `ZoneSystem.SetupLocations` merges each AltBiome's `m_addLocations`/`m_addVegetation` into the global lists and tags them via `AltBiomeParent` (placement then requires the sector to carry that modifier, and `m_block*Names` blocks by name - see `ZoneSystem.GenerateLocationsTimeSliced`).
   - `SpawnSystem.UpdateSpawning` runs every AltBiome's `m_spawn` list for AltBiomes touching the zone's heightmap corners (`Heightmap.m_cornerAltBiomes`) and `UpdateSpawnList` skips entries in `m_blockSpawnNames`.
   - `EnvMan.GetAvailableEnvironments(BiomeSector)` adds `m_addEnvironments` and removes `m_blockEnvironments`; `EnvMan.GetEnvironmentOverride` honours `m_forceEnvironment`.
   - `SpawnSystem.GetLevelUpChance`, `SpawnArea.GetLevelUpChance` multiply by `BiomeSector.GetLevelUpChanceMultiplier`.
   - `Heightmap.GetBiomeColor(BiomeSector)` applies `m_terrainTextureOverride` (reuse another biome's terrain texture).
   - `MusicMan` uses `m_forceMusic`; `Player.AddKnownBiome`/`Minimap` show the modified name.

### Data & persistence
Not saved in the world: recomputed on every load from the seed and the *current* AltBiome definitions. Adding a modifier to an existing world re-rolls placement for all modifiers of that biome (order/seed changes) - locations already registered in the save are unaffected, but spawns/envs/names shift immediately.

### Multiplayer authority
Local on every peer → the AltBiome list must be identical on all peers (a mod that adds AltBiomes is **everyone**).

### Patch points
- Add custom `AltBiome` entries to `AltBiomeList.m_altBiomes` in a `ZoneSystem.Awake` postfix (before `ZoneSystem.Start`→`SetupLocations` and before `ZNet.ServerLoadWorld` builds sectors). This is the cleanest data-only way to create "sub-biomes" (e.g. a haunted forest, an ocean reef) without touching the `Heightmap.Biome` enum.
- `BiomeSector.CanAddModifier` - custom placement rules.
- `BiomeSector.GetLevelUpChanceMultiplier` - dynamic difficulty per region (e.g. scale with a global key).

### Vanilla quirks
- `AltBiomeWorldData.RandomBiomeFromBiomes` (used when a location lists several biomes): Plains maps to BlackForest, the Ocean branch tests the Meadows flag and `Random.Range(0, num-1)` never picks the last candidate. Multi-biome locations are sampled with a skewed distribution.
- `GenerateSectors` computes `MaxZone` from `Min` (copy/paste), so `BiomeSector.IsDiscovered` is effectively never set.

---

## 3. Heightmaps and terrain

### Key classes
- `Heightmap` - one terrain tile (zone tile or distant LOD tile). `m_width`, `m_scale`, `m_isDistantLod`, `m_cornerBiomes[4]`, `m_cornerAltBiomes`, paint mask texture (dirt/cultivated/paved/**lava**; in Deep North the cultivation channel doubles as **deep snow depth** - see `Heightmap.GetCultivationMask` uses in `Character`, `Vagon`, `SnowRoller`, `Player` build checks).
- `HeightmapBuilder` - background thread building height/mask arrays (`RequestTerrainSync`, `IsTerrainReady`, `Build`). One FIFO thread for real zones and distant terrain alike: `m_toBuild` queue, `m_ready` results capped at 16 (oldest dropped), one `m_lock`; `Build(HMBuildData)` runs outside the lock. The private `RequestTerrain` takes a ready result (removing it from `m_ready`) or queues the job and returns null, never blocking; `RequestTerrainSync` spins on it. A distant build uses one biome per vertex, then smooths; a real-zone build blends the four corner biomes with smoothstep. `instance` is created on first access and disposed in `Game.OnApplicationQuit` (`m_lock` becomes null).
- `TerrainComp`/`TerrainModifier`/`TerrainOp` - player terrain edits (ZDO `s_TCData`).
- `TerrainLod` - distant terrain: a 3x3 grid of 800 m heightmaps at 10 m spacing (see §9). `Heightmap.Awake` adds every map whose `m_isDistantLod` is still false to `s_heightmaps` (what `GetAllHeightmaps`/`FindHeightmap` return); the `IsDistantLod` setter removes it again, so `TerrainLod` tiles join that list for a moment. `Generate` reuses an injected `m_buildData` when centre, scale, size and generator match.

### Flow
`Heightmap.Regenerate` → `Generate` → `HeightmapBuilder.RequestTerrainSync` → `ApplyModifiers` (TerrainComp deltas) → `RebuildCollisionMesh`/`RebuildRenderMesh`. Biome at a point inside a tile is a weighted choice of the 4 corner biomes (`Heightmap.GetBiome`) - biome resolution is effectively per zone corner. Terrain textures are chosen from vertex colors (`Heightmap.GetBiomeColor(Biome)`), which only encode a fixed set of combos (Meadows=none, Swamp=R, Mountain=G, BlackForest=B, Plains=A, AshLands=R+A, DeepNorth=G (same channel as Mountain), Mistlands=B+A). **A truly new biome terrain texture needs terrain-shader work**; reuse via `AltBiome.m_terrainTextureOverride` is free.

### Patch points
`Heightmap.GetBiome(Vector3,...)`, `Heightmap.FindBiome`, `Heightmap.GetOceanDepth`, `Heightmap.IsLava`, `Heightmap.GetCultivationMask` (snow depth in DN), `Heightmap.ApplySettings`/`GetLodHideDistance` (LOD handoff).

---

## 4. ZoneSystem: zones, vegetation, locations, global keys

### Key classes
- `ZoneSystem` (singleton `ZoneSystem.instance`) - zone lifecycle, vegetation & location placement, location registry, global keys, ground queries (`GetGroundHeight`, `GetSolidHeight`, `FindFloor`, `GetGroundData`, `IsBlocked`, `IsLava`).
- `ZoneSystem.ZoneVegetation` - `m_prefab`, `m_biome`, `m_biomeArea`, `m_min/m_max` (per zone), altitude, **ocean depth** (`m_minOceanDepth/m_maxOceanDepth`), tilt, terrain delta, forest factor, group size, `m_snapToWater`, distance-from-center, surround check.
- `ZoneSystem.ZoneLocation` - `m_prefab` (`SoftReference<GameObject>`), `m_biome`, `m_biomeArea`, `m_quantity`, `m_prioritized`, `m_centerFirst`, `m_unique`, `m_group`/`m_minDistanceFromSimilar`, `m_groupMax`/`m_maxDistanceFromSimilar`, `m_iconAlways`/`m_iconPlaced`, `m_randomRotation`, `m_slopeRotation`, `m_snapToWater`, `m_interior/m_exteriorRadius`, `m_clearArea`, terrain delta, vegetation, forest, center/min/max distance, altitude. `Hash` = stable hash of prefab name.
- `ZoneSystem.LocationInstance` - `{m_location, m_position, m_placed}`; registry `m_locationInstances : Dictionary<Vector2s zone, LocationInstance>` (**max one location per zone**).
- `LocationList` (MonoBehaviour in location scenes / `m_locationLists`) - `m_locations`, `m_vegetation`, `m_environments`, `m_biomeEnvironments`, `m_events`, `m_clutter`, `m_sortOrder`. Vanilla lists: `_LocationList_cp1`, `_Mistlands`, `_Ashlands`, `_DeepNorth`, `_Hildir`, `_MountainCaves`.

### Flow
- `ZoneSystem.Start` → `SetupLocations` (merges all `LocationList`s sorted by `m_sortOrder`, appends envs to `EnvMan`, clutter to `ClutterSystem`, events to `RandEventSystem`, AltBiome additions; builds `m_locationsByHash`).
- Location **generation** (server, once per world or when `m_locationVersion` changes): `GenerateLocations` → coroutine `GenerateLocationsTimeSliced()` orders by `m_prioritized`; for each ZoneLocation `GenerateLocationsTimeSliced(location, ...)` seeds `Random` with world seed + prefab-name hash, then up to 60000 (prioritized) / 12000 zone picks (zone picked from `AltBiomeWorldData` points of the right biome, or near center with `m_centerFirst`), up to 6 point samples per zone checking in order: zone already has a location → biomeArea → center distance → biome → altitude (relative to water 30) → forest → distance-from-center → terrain delta → similar/not-similar → vegetation mask → AltBiome parent/block → surround vegetation; success → `RegisterLocation(location, pos, generated:false)`. **Only zones not yet generated are eligible.**
- Zone **spawning** (`ZoneSystem.Update` every 0.1 s): `CreateLocalZones` around `ZNet.GetReferencePosition()` (`PokeLocalZone` → `SpawnZone`). Mode: server & zone never generated → `Full`; otherwise `Client`. Server additionally runs `CreateGhostZones` around every peer (generates ZDOs for zones near remote players, then destroys the GameObjects). `SpawnZone` waits for `HeightmapBuilder.IsTerrainReady` and for location prefab async load (`PokeCanSpawnLocation`), instantiates `m_zonePrefab` (`_Zone`: Heightmap + WaterVolume), and on first generation calls `PlaceLocations` → `PlaceVegetation` → `PlaceZoneCtrl` (`_ZoneCtrl` with `SpawnSystem`) then `SetZoneGenerated`.
- `PlaceLocations`: rotation (random 22.5° steps or slope), seed = world seed + zone.x·4271 + zone.y·9187, `SpawnLocation(..., mode)`, marks `m_placed`, `RemoveUnplacedLocations` for unique ones, `SendLocationIcons` if `m_iconPlaced`.
- `SpawnLocation`: runs `RandomSpawn.Randomize`/`RandomObject.Randomize` deterministically, instantiates every enabled `ZNetView` child as its own networked object (with `DungeonGenerator.Generate(mode)` for generators, `s_preSnow` on `WearNTear`), then `CreateLocationProxy` (a `LocationProxy` ZDO storing `s_location` hash + `s_seed`). On clients, `LocationProxy.SpawnLocation` → `ZoneSystem.SpawnProxyLocation` → `SpawnLocation(Client)` instantiates the **non-networked** part of the prefab.
- `PlaceVegetation`: per `ZoneVegetation`, seeded with world seed + zone.x·4271 + zone.y·9187 + prefab-name hash; respects clear areas of locations and AltBiome parent/blocks.
- Zone unload: `UpdateTTL` destroys a zone root after `m_zoneTTL` (4 s) once no instance remains in the sector.

### Data & persistence (world `.db` via `ZoneSystem.Save`/`Load`)
`m_generatedZones`, `m_locationVersion`, global keys (only non-server-option keys; server options live in `World.m_startingGlobalKeys`), `LocationsGenerated`, and every `LocationInstance` as (prefab hash, position, placed). Unknown location hashes on load are dropped (`ZoneSystem.Load` logs "Failed to find location").

### Global keys
- API: `SetGlobalKey(string|GlobalKeys[,float])`, `RemoveGlobalKey`, `GetGlobalKey(name[, out string|float value])`, `GetGlobalKeys`, `CheckKey(key, GameKeyType.Global|Player)`. Keys are lower-cased; a key may carry a value (`"name value"`, re-adding replaces the old value).
- Authority: `SetGlobalKey` sends routed RPC `"SetGlobalKey"` to the server; server `RPC_SetGlobalKey` → `GlobalKeyAdd` → `SendGlobalKeys(0)` (RPC `"GlobalKeys"` full list to everyone). `OnNewPeer` sends keys + location icons (`"LocationIcons"`).
- `GlobalKeys` enum includes world modifiers (`WorldLevel`, `EnemyLevelUpRate`, `NoMap`, `NoBossPortals`, `NoHeavySnow`, `AllHeavySnow`, `DungeonBuild`, ...), progression (`defeated_*`, `KilledTroll`, `killed_surtling`, `KilledBat`, `StoneCircle`, `AshlandsOcean`) and counters (`activeBosses`). Custom string keys fall into `NonServerOption` and **are saved with the world**.

### Multiplayer authority
Server: location generation, registry, global keys, ghost-zone generation. Clients: only Client-mode zone visuals. Any mod adding locations/vegetation must be on the server *and* on clients (clients must know the prefabs to build proxies/ZNetScene objects).

### Constants
`c_ZoneSize 64`, `c_WaterLevel 30`, `m_zoneTTL 4`, `c_ZonesPerChunk 8`, generation attempts 60000/12000, 6 samples per zone, `c_GenerateLocationsTimeBuffer 1/150`.

### Patch points
- `ZoneSystem.SetupLocations` (postfix) - inject `ZoneLocation`/`ZoneVegetation` (Jotunn's zone manager does this for you).
- `ZoneSystem.GenerateLocationsTimeSliced(ZoneLocation, ...)` - private coroutine; custom placement rules are easier as a separate server-side pass.
- `ZoneSystem.PlaceLocations` / `SpawnLocation` (private) - per-instance tweaks at generation time.
- `ZoneSystem.SpawnLocationMidGame` - only **registers** a location; it is physically placed only when that zone is generated for the first time. For already-generated zones you must call the private `SpawnLocation(location, seed, pos, rot, SpawnMode.Full, list)` yourself on the server and `RegisterLocation(..., generated:true)`.
- `ZoneSystem.GetLocationIcons` / `GetLocationIcon` - map icons.

---

## 5. Locations and dungeons

### Key classes
- `Location` (on location prefab root) - `m_exteriorRadius`, `m_noBuild`, `m_noBuildRadiusOverride`, `m_clearArea`, `m_discoverLabel` (text shown by `Player.UpdateBiome` → `AddKnownLocationName`), `m_applyRandomDamage`, interior (`m_hasInterior`, `m_interiorRadius`, `m_interiorEnvironment`, `m_interiorTransform`, `m_useCustomInteriorTransform`, `m_generator`, `m_interiorPrefab`), spawner overrides (`m_enemyMinLevelOverride`, `m_enemyMaxLevelOverride`, `m_enemyLevelUpOverride`, `m_excludeEnemyLevelOverrideGroups`, `m_blockSpawnGroups`). Statics: `GetLocation`, `GetZoneLocation`, `IsInsideNoBuildLocation`, `IsInsideActiveBossDungeon`.
- `LocationProxy` - networked stub (`s_location`, `s_seed`) that rebuilds the non-networked location on each client; holds the zone in "loading" state until the prefab is loaded (`ZoneSystem.SetLoadingInZone`).
- `DungeonGenerator` - `m_algorithm` (`Dungeon`, `CampGrid`, `CampRadial`), `m_minRooms`/`m_maxRooms`, `m_minRequiredRooms`/`m_requiredRooms`, `m_alternativeFunctionality` (weighted rooms/endcaps), `m_themes` (`Room.Theme` flags), `m_doorTypes`/`m_doorChance`, camp settings, `m_zoneSize` (**room bounds, default 64³**), `m_useCustomInteriorTransform`, `m_addBaseSeedToRandomSpawn`.
- `DungeonDB` / `RoomList` / `DungeonDB.RoomData` (`m_prefab` soft ref, `m_enabled`, `m_theme`, `Hash`).
- `Room` - `m_size`, `m_theme`, `m_entrance`, `m_endCap`, `m_divider`, `m_endCapPrio`, `m_minPlaceOrder`, `m_weight`, `m_faceCenter`, `m_perimeter`, `m_musicPrefab`. `RoomConnection` children define doors (`m_type`, `m_entrance`, `m_allowDoor`, ...).
- `Room.Theme`: Crypt, SunkenCrypt, Cave, ForestCrypt, GoblinCamp, MeadowsVillage, MeadowsFarm, DvergerTown, DvergerBoss, ForestCryptHildir, CaveHildir, PlainsFortHildir, AshlandRuins, FortressRuins, Hole, NorthVillage (0x10000), MorkHalla (0x20000). **0x8000 and ≥0x40000 are free bits** for custom themes.
- `Teleport` (dungeon doors), `EnvZone` (interior environment), `MusicVolume`, `CreatureSpawner` (see §7).

### Flow
- Interiors live 5000 m above the location (`Location.Awake` instantiates `m_interiorPrefab` scaled to 64×500×64 at the zone center, with `EnvZone.m_environment = m_interiorEnvironment`). `Character.InInterior(pos)` is simply `pos.y > 3000` (disables weather, random-event areas, some building rules).
- **Interior rules worth knowing (1.0.16, checked 2026-10-02 for the dungeon-instances investigation, [exploration-dungeon-instances.md](../design/exploration-dungeon-instances.md) §2.3):**
  - The AI navmesh only exists between y -500 and 5500 (`Pathfinding` tiles are centred at y 2500 and 6000 m tall, `Pathfinding.BuildTile`), so walking AI cannot path above 5500.
  - Loading, ownership and natural spawning ignore height (`ZNetScene.PointInsideActiveArea`, `ZDOMan.ReleaseNearbyZDOS` and `SpawnSystem` work on XZ zones): a player inside an interior can own creatures and trigger biome spawns on the ground below, and a surface player can own creatures inside.
  - World level still enlarges non-player characters indoors (`Character.Awake`: scale × (1 + world level × `m_worldLevelEnemyMoveSpeedMultiplier`), no `InInterior` check); only the enemy speed/size world modifier is skipped inside.
  - A dungeon `Teleport` never checks `Humanoid.IsTeleportable` (ore goes in) and uses `distantTeleport: false`; the teleport then waits for `ZNetScene.IsAreaReady` with no timeout (`Player.UpdateTeleport`), so a zone left in the loading state (`ZoneSystem.SetLoadingInZone` never released) hangs the arrival.
  - `Player.m_interactMask` has no `character_trigger` (layer 14): a collider on that layer alone can never be hovered or used, which is why vanilla gateways pair a trigger box with a non-trigger collider.
  - Code defaults that bite when components are added at runtime: `EnvZone.m_force = true`, `RandomEvent.m_random = true` and `m_nearBaseOnly = true` (an event added with defaults becomes a random raid).
  - `GlobalKeys.activeBosses` goes up once per boss in `BaseAI.SetAlerted` and down only in `Character.OnDeath`; any other removal of an alerted boss leaks it (with `NoBossPortals`, every portal stays blocked until `removekey activeBosses`).
- `DungeonGenerator.Generate(seed, mode)` (server, Full/Ghost): `SetupAvailableRooms` (theme match + enabled) → `GenerateRooms` (Dungeon: `PlaceStartRoom`, `PlaceRooms` until `m_maxRooms` or required rooms + `m_minRooms`, `PlaceEndCaps`, `PlaceDoors`) → `Save`. Each candidate is rejected by `TestCollision` if it leaves the `m_zoneSize` box centred on the zone (`IsInsideDungeon`) or overlaps another room.
- Seed: `DungeonGenerator.GetSeed` = world seed + zone and position hash (or `m_forceSeed`).
- Clients: `DungeonGenerator.Awake` → `Load` (reads room list) → `LoadRoomPrefabsAsync` → `Spawn` → `PlaceRoom(..., Client)` rebuilds non-networked geometry. Networked children of rooms (spawners, chests, doors) are independent ZDOs.
- Locations: the server generates a zone in `ZoneSystem.SpawnZone`, which places its location with `ZoneSystem.SpawnLocation`: `SpawnMode.Full` when the zone becomes a local zone of the server itself (`ZoneSystem.PokeLocalZone`; also the `location` console command through `ZoneSystem.TestSpawnLocation`), `SpawnMode.Ghost` for the zones generated ahead of the players (`ZoneSystem.CreateGhostZones`). Both modes instantiate every enabled, active `ZNetView` child of the location prefab as its own networked object, copied from the nested child. `ZNetView.Awake` records its prefab as `Utils.GetPrefabName` (the name cut at the first `(` or space), and every later load re-creates it with `ZNetScene.CreateObject` from the standalone prefab of that name. Ghost objects are destroyed right away (`ZoneSystem.SpawnZone`), and `ZoneSystem.Update` ghost-generates zones up to the total simulation distance around the server and every peer, so almost every networked child is first loaded as a re-created standalone prefab. The rest of the location (the root and its children without a `ZNetView`) is rebuilt on every peer by `LocationProxy` through `ZoneSystem.SpawnProxyLocation` (`SpawnMode.Client`, networked children left out). So a component that the location prefab puts on its root or on a non-networked child exists on every peer, but a component it adds to a nested networked child exists only on the instance made at generation. Vanilla 1.0.16 loses Wishbone beacons this way (§6 and the "Wish bone deep north fix" idea).

### Data & persistence
`ZDOVars.s_roomData` byte array on the generator ZDO: count, then per room (room prefab hash, position, rotation). Legacy format `s_rooms` + `room{i}`, `room{i}_pos`, ... is migrated on save. Missing room hashes are skipped ("Missing room").

### Multiplayer authority
Server generates; all peers need the room prefabs (`DungeonDB`) to rebuild. Room geometry is a child of the generator GameObject: it exists only while the **generator's zone** is instantiated for that client.

### Patch points
- `DungeonDB.Start`/`SetupRooms` (postfix: append `RoomData` with a custom theme bit).
- `DungeonGenerator.Generate(int, SpawnMode)` (prefix: change `m_maxRooms`, `m_zoneSize`, themes per instance), `DungeonGenerator.PlaceRoom`/`GetRandomWeightedRoom` (room selection), `DungeonGenerator.CheckRequiredRooms`.
- `Location.Awake` (interior env box), `Teleport.Interact` (entry rules), `Location.IsInsideActiveBossDungeon`.

---

## 6. Biome specifics: Ashlands, Deep North, and the PersistentEventSystem

### Ashlands (south, AshLands biome)
- Lava is a paint-mask channel: `Heightmap.IsLava`, `Heightmap.GetLava`, `ZoneSystem.IsLava`, `ZoneSystem.IsLavaPreHeightmap`; spawns have `m_inLava/m_outsideLava` (`SpawnSystem.IsSpawnPointGood`), AI has `m_avoidLava` (`BaseAI`).
- Boiling ocean: `WorldGenerator.GetAshlandsOceanGradient` drives damage (`Character` hit type `HitData.HitType.AshlandsOcean`), ship damage (`Ship` sets `GlobalKeys.AshlandsOcean` the first time), minimap tint.
- Fire spread: `Cinder`, `CinderSpawner`; shields: `ShieldGenerator`; siege: `Catapult`, `SiegeMachine`.

### Deep North (north, DeepNorth biome) - what 1.0.16 actually contains
- Content assets exist: `_LocationList_DeepNorth`, `_SpawnList_DeepNorth`, `_RoomList_DeepNorth` (theme NorthVillage), `_RoomList_MorkHalla` (theme MorkHalla), `DvergerDeepNorth`, `GoblinDeepNorth`, `Skeleton_DeepNorth`, `Jotnar`, `FrozenKing` (boss; item `$item_frozenking_drop`), `LastBossGate`, `MemorialStone_*` + `Minimap.PinType.Memorial`, `TreasureChest_morkhalla`, `FeastDeepNorth`, `GP_DeepNorth` power, and the event props `BlackIce_Core`, `BlackIce_Start`, `FimbulvinterOrb`, `FimbulvinterOrb_start`, `projectile_FimbulvinterMeteor`, `Ice_FimbulWinter/*` shards.
- Deep snow: the cultivation mask is snow depth. `Character` slows in deep snow (`m_deepSnowSlowMax`, `m_deepSnowSlowStartHeight`, `m_deepSnowWalkObj`), `Vagon`/`SnowRoller` flatten it, `Player` build rules `Piece.m_allowedInDeepSnow` / `m_requireDeepSnow`, `Attack.m_snowShovel`, `TreeLog` snow, `Fireplace.m_snowMelter`, `Plant.m_tolerateCold`.
- Snow load on buildings: `WearNTear` accumulates `m_snowBuildup` from `EnvMan.GetSnowBuildup()` (`EnvSetup.m_snowBuildup`) and damages pieces below `Game.m_snowSupportLevel` (0.25) unless `m_snowDamageImmune`; world modifiers `GlobalKeys.NoHeavySnow` / `AllHeavySnow`. `SnowDestruction` removes snow props.
- Waves fade near the DN coast: `WorldGenerator.DeepNorthWaveFade` (used by `WaterVolume.GetWaterSurface`, `Fish`).
- Camera: `GameCamera.RayTestPoint` special-cases DeepNorth to keep the camera above deep snow.
- Wishbone targets (checked in the 1.0.16 asset bundles; the Wishbone pings the closest `Beacon`, see [exploration-player.md §9](exploration-player.md)). Standalone networked prefabs with a `Beacon` (range in m): `TreasureChest_meadows_buried` (40), `TreasureChest_memorial_buried` (40, Memorial Site tombstones), `TreasureChest_deepnorth_village` (30: villages, huts, lumber camps, graveyards), `TreasureChest_meadows_combat` (30), `Pickable_MountainRemains01_buried` (40, on a child), `silvervein`, `goldvein` and `TrollFrost_Dead` (50, on a child named `Becon`), `mudpile_beacon` (25). Location roots with a `Beacon` (not networked, rebuilt on every peer, so they work): `DN_gammeltrollFrac01`/`02` (20). Beacons that exist only on the nested copy inside a location, so they are lost (§5): `Pickable_MorkHallaTreasure` in the Deep North Viking Graveyard `ShipSetting02`/`03` (20; the same prefab is used without a Beacon in the Mörkhalla rooms), `shipwreck_vikingship_chest` in `ShipWreck01_DN`/`02_DN` (20), `shipwreck_karve_chest` in `ShipWreck01` to `04` (20). The Meadows graveyard `ShipSetting01` gives a Beacon only to its chest; its valuables `Pickable_DolmenTreasure` have none. None of these chests has `Container.m_autoDestroyEmpty`, so an emptied chest keeps its Beacon until it is broken.
- **Activation mechanic available in vanilla**: `TriggerPersistentEventOnDestroy` (requires `Destructible` + `ZNetView`): when the destructible is destroyed, the *owner* shows `_centralTextOnTriggered` to all and calls `PersistentEventSystem.TriggerEvent(_eventInternalName)` (or `StopEvent` when `_stopEvent`). This is the vanilla "break a stone → world reacts" primitive. `m_onDestroyed` runs only on the owner (`Destructible.Destroy`); other peers just see the ZDO go (`ZDOMan.HandleDestroyedZDO` → `m_onZDODestroyed`, which the server also gets for every destroy).
- **The Jotun invasion chain (1.0.16 asset bundles, decoded 2026-10-01):**
  - The Mörkhalla stone is **`BlackIce_Start`** ("Malicious Ice", `$blackice_triggeritem`; `Destructible` 1000 HP, chop/pickaxe/fire weak, no drop; persistent `ZNetView`), placed only in the end rooms `morkhalla_endcap01/02` of `DG_MorkHalla` (location `MorkBorg`, up to 40 per world, Deep North). Its trigger: `_eventInternalName = "jotun_invasion"`, `_stopEvent = false`, text `$fimbulvinterorb_start` ("The Jotun Advance"). Vanilla persists nothing else when it breaks (no global key, no stat): only the ZDO goes and one invasion may start.
  - `m_possibleEvents` (on `_GameMain`) holds exactly one event, **`jotun_invasion`** (`$jotuninvasion`): radius 100-300, `maxConcurrent` 3, biomes Meadows, Black Forest, Swamp, Mountain, Plains (never the Deep North), 1000-12000 m from the centre, height 35-1000, slope 25, 400 m from another, `environmentOverride` `JotunInvasion` with `perBiomeEnvironments` (envs `JotunInvasion_meadows/_blackforest/_swamp/_mountain/_plains/_mistlands`; **no `_deepnorth`**), `graphicalEffects` -1 (snow). `objectsToSpawn`: `FimbulLocation01` (important, at the centre; not a ZoneSystem location) and 250-800 `IceShelf_01`-`10` in water.
  - `FimbulLocation01` holds `BlackIce_Core` (1000 HP; drops `HatefulBlood` "Malicious Blood"; trigger `_stopEvent = true`, text `$fimbulvinterorb_destroyed` "The Jotun Retreat"; `SpawnArea` of `JotunWarrior` ×2 weight, `JotunWitch`, `JotunWarriorDualWield`, levels 1-3, 15 % level-up, every 50 s, max 6 within 50 m) and about 46 ice shards. `FimbulvinterOrb` / `FimbulvinterOrb_start` (150 HP) carry the same triggers but no room or location places them.
  - Invasion spawns are `_SpawnList_DeepNorth` entries with `m_biome = -1` and `m_requiredPersistentEvent = "jotun_invasion"`: `JotunWarrior` (max 4, 500 s, group 1-2), `JotunWitch` (max 2, 500 s), `Elaking` (max 6, 1000 s, group 1-3), `projectile_FimbulvinterMeteor` (no cap, every 5 s, group 1-3 in 30 m, 150 m up). All levels 1-1: invasion creatures are always 0★.
  - The same list's daytime patrols `JotunWarrior` / `JotunWitch` (5 %, 3000 s, max 2, hunt the player) need the key `jotun_killed` (set by killing any Krigen or Hexen). The raid `army_jotuns` (in `_LocationList_DeepNorth.m_events`; Krigen + Elaking, near bases) needs `jotun_killed` and stops once `defeated_frozenking_p3` is set (Kall's last phase, `FrozenKing_p3.m_defeatSetGlobalKey`).
  - Factions: `JotunWarrior`, `JotunWarriorDualWield` (both "Krigen"), `JotunWitch` ("Hexen"), `Elaking`, `TrollFrost` ("Gammeltroll"), `Barka`, `Moose` and the frost Greydwarfs are all `Character.Faction.DeepNorth`, so `BaseAI.IsEnemy` never makes them fight each other.
  - The Deep North is one single `BiomeSector` (`AltBiomeWorldData.GenerateSectors` makes one sector each for Ashlands, Deep North and Ocean), so sector level-up multipliers are uniform across it. Its land inside the 10 km edge is a crescent of about 30 km² (2 km deep along x = 0).
  - Deep North weathers (asset names): `Twilight_Clear`, `Twilight_Snow`, `Twilight_SnowStorm` (wiki: clear 40 %, snow 40 %, blizzard 20 %).
  - Spawn caps (`SpawnSystem.UpdateSpawnList`): a due entry gets `min(m_maxSpawned, elapsed / interval)` tries at once;
    each try compares `GetNrOfZDOInstances` (every ZDO of that prefab in the 5 × 5 zones around the zone control,
    whoever spawned it) plus what this call already spawned against the cap. Resetting the timer stamp to 0 therefore
    fills an entry up to its cap in one call; a group can overshoot the cap by up to its size minus one.
  - Map textures (`Minimap`, 1.0.16): `m_mapTexture` (RGB24, 2048 × 2048, 12 m per pixel; the biome colour per pixel,
    the Deep North colour is white like the sea), `m_heightTexture` (RHalf world heights), `m_forestMaskTexture`,
    `m_fogTexture`; one shader material per map (large, small) draws water from the height and puts the fog on top.
    Made once per world in `Minimap.Update` (`TryLoadMinimapTextureData` from the cache, else `GenerateWorldMap`); the
    cache is written from the generated arrays, so later edits of the texture are never saved. Pixel centre of column j:
    `(j - size/2) × pixelSize + pixelSize/2`.
  - A dead player has no character ZDO while waiting to respawn: `Game._RequestRespawn` (10 s after death) destroys the
    player and sets the character id to none, then waits at least `m_respawnLoadDuration` (8 s) with the reference
    position at the spawn point (bed). Server code that counts players by `ZNet.GetAllCharacterZDOS` misses them then;
    `ZNetPeer.GetRefPos` still has a position.
  - Used by [Deep North Awakening](../../src/Exploration/DeepNorth.Awakening) ([design](../design/exploration-deepnorth-awakening.md)).

### `PersistentEventSystem` (new, saved since world version BogWitch)
- Data: `m_possibleEvents : List<PersistentEvent>` (prefab-configured) with `internalName`, `mapTokenString`, `minRadius/maxRadius`, `maxConcurrent`, `locationToSpawn`, `hasDuration`, `min/maxDurationInSeconds`, `objectsToSpawn` (`ObjectSpawnSettings`: prefabs, count, spacing, slope, water rule), placement (`biomes`, center distance, height, slope, `minDistanceFromSimilar`), `environmentOverride` (+ `perBiomeEnvironments` → `<env>_<Biome>`), `graphicalEffects` (`EventGraphicalEffects.Snow`). Runtime: `m_activePersistentEvents.list` of `ActivePersistentEvent {sourceEventId, eventId, position, radius, startTime, duration}`.
- Flow: `TriggerEvent(name)` → routed RPC `"RequestStartEvent"(index)` → server `RPC_RequestStartEvent`: checks `maxConcurrent`, `PersistentEvent.GenerateEventLocation` (random point in the biome ring, ≥ maxRadius+150 from every character, not near similar events), optionally `ZoneSystem.SpawnLocationMidGame(locationToSpawn)`, spawns `objectsToSpawn`, adds the active event, `UpdateClientEventsList` (routed `"UpdateClientEventsList"` with JSON). Server `Update` expires events with `duration ≥ 0` every 30 s. Note: `RPC_RequestStartEvent` never sets `duration`, so events are permanent unless stopped (or a mod sets duration).
- Consumers: `EnvMan.GetEnvironmentOverride` (env inside event radius), `SpawnSystem.UpdateSpawnList` (`SpawnData.m_requiredPersistentEvent`), `WearNTear` (`m_requiredPersistentEvent`, `m_takeDamageIfInsideEvent`, `m_eventDamage`: pieces that only survive inside/outside an event), `Minimap.UpdatePersistentEventPins` (EventArea circle + animated RandomEvent pin), shaders (`_PersistentEventData` compute buffer), `PersistentEventSystemDirectionHelper` (particles pointing to the nearest event, optionally only with `NoMap`).
- Persistence: `PrepareSave` serializes the active list to JSON + Brotli, saved after `RandEventSystem` in the world `.db`.
- Authority: server; RPC uses the **index** into `m_possibleEvents`, so every peer needs the same list (mods appending events are *everyone*).
- `RPC_RequestStartEvent` never checks the sender; `maxConcurrent` and `minDistanceFromSimilar` count events of the same **index** only; placement rejects points within `maxRadius + 150` m of any player. A failed start (cap, no spot) only logs: the trigger's banner has already shown. The saved JSON holds `sourceEventId` (index) with no name: an entry whose index is out of range on a peer makes `ActivePersistentEvent.Source` throw every frame (`UpdateComputeBuffer`), in the minimap pins, the weather and spawns, and blocks every `StopEvent`. Do not add mod entries to these lists unless every peer and every future load has them.
- `StopEvent(name, pos)` stops the only event of that name whatever the distance when there is one, else the nearest; `RPC_RequestStopEvent` has no guard for an unknown id. `GetActiveEvent(pos)` returns the **first** event whose 3D sphere contains the point (list order = start order).
- `EnvMan.UpdateEnvironment` / `EnvMan.GetBiome` call `WorldGenerator.IsDeepnorth(position.x, position.y)` with the camera **height** as y (the method expects x, z), so the `m_deepnorthOverride` env entries almost never apply in the real Deep North.

### Patch points
`PersistentEventSystem.RPC_RequestStartEvent` / `GenerateEventLocation` (custom placement, e.g. at a given position), `PersistentEventSystem.GetActiveEvent`, `TriggerPersistentEventOnDestroy.OnDestroyed`, `Destructible.m_onDestroyed` (delegate, no patch needed), `WearNTear.UpdateWear` region for snow load.

---

## 7. Spawning

### Key classes
- `SpawnSystem` (on `_ZoneCtrl`, one per zone) with `m_spawnLists : List<SpawnSystemList>`; `SpawnSystem.SpawnData` fields: `m_prefab`, `m_enabled`, `m_biome`, `m_biomeArea`, `m_maxSpawned`, `m_spawnInterval`, `m_spawnChance`, `m_spawnDistance`, `m_spawnRadiusMin/Max`, `m_requiredGlobalKey`, `m_requiredEnvironments`, `m_requiredPersistentEvent`, `m_groupSizeMin/Max`, `m_groupRadius`, `m_spawnAtNight/Day`, altitude, tilt, forest, lava, `m_canSpawnCloseToPlayer`, `m_insidePlayerBase`, ocean depth, `m_huntPlayer`, ground offset, center distance, `m_minLevel/m_maxLevel`, `m_levelUpMinCenterDistance`, `m_overrideLevelupChance`. Vanilla lists: `_SpawnList_base`, `_mistlands`, `_ashlands`, `_DeepNorth`.
- `CreatureSpawner` - fixed spawner in locations: `m_creaturePrefab`, levels, `m_respawnTimeMinuts`, `m_triggerDistance` (60), `m_triggerNoise`, day/night, `m_requireSpawnArea`, `m_spawnInPlayerBase`, `m_wakeUpAnimation`, `m_spawnInterval` (5 s), `m_requiredGlobalKey`, `m_blockingGlobalKey`, `m_setPatrolSpawnPoint`, group blocking (`m_spawnGroupID`, `m_maxGroupSpawned`, `m_spawnGroupRadius`, `m_spawnerWeight`).
- `SpawnArea` - spawner objects (greydwarf nests, bone piles...): weighted `m_prefabs`, `m_levelupChance` (15), `m_spawnIntervalSec` (30), `m_triggerDistance` (256), `m_maxNear`/`m_nearRadius`, `m_maxTotal`/`m_farRadius`.
- `TriggerSpawner` - spawns on `"Trigger"` RPC (`TriggerSpawner.TriggerAllInRange`), used by events/dungeons.
- `RandEventSystem` supplies extra `SpawnData` lists while an event is active (`GetCurrentSpawners`).

### Flow & core rules
- `SpawnSystem.UpdateSpawning` (every 1 s after 10 s): only if this peer **owns the zone ctrl ZDO and has a local player inside the zone** → runs base lists (salt `"b_"`), active event list (`"e_"`), AltBiome lists (`"m{i}"`). Dedicated servers have no local player, so **all natural spawning runs on clients**.
- `SpawnSystem.UpdateSpawnList`: per entry, a timestamp stored in the zone ctrl ZDO (key = stable hash of salt + prefab name + index, value = ticks) decides how many spawn attempts are due; checks chance, global key, environment (`EnvMan.IsEnvironment` - evaluated on the owner's local weather), day/night, `m_maxSpawned` via `GetNrOfZDOInstances` over a 5×5 zone area (`SimulationDistance.OriginalDistance`), `FindBaseSpawnPoint` (40–80 m from a random player in the zone), persistent-event requirement, `m_spawnDistance`, then `IsSpawnPointGood` and `Spawn`.
- `SpawnSystem.Spawn`: instantiates, `SetHuntPlayer`, level roll (`m_minLevel` + repeated `GetLevelUpChance` rolls up to `m_maxLevel`; `Character.SetLevel`; fish get `ItemDrop.SetQuality`), `MonsterAI.SetDespawnInDay` for night-only, `SetEventCreature` for event spawns.
- Level-up chance (`SpawnSystem.GetLevelUpChance(Vector3, float)`): base 10% (or override); with world level > 0: `min(70, base^(worldLevel·1.15)) × sectorMultiplier`; else `base × Game.m_enemyLevelUpRate × sectorMultiplier`. `SpawnArea.GetLevelUpChance` uses the same formula with its own base. `CreatureSpawner.Spawn` uses `Location` overrides.
- What a star means (`Character.SetLevel` → `SetupMaxHealth`): HP = base HP × level; damage (`Attack.GetLevelDamageFactor`) = 1 + 0.5 × (level−1); drops (`CharacterDrop.GenerateDropList`) × 2^(level−1) for drops with `m_levelMultiplier`; visuals from `LevelEffects.m_levelSetups[level-2]` (scale, hue/sat/value, emissive, enable object).
- `CreatureSpawner.UpdateSpawner` (owner only): location spawn-group block, respawn timer via ZDO connection `ZDOExtraData.ConnectionType.Spawned` to the spawned creature + `s_aliveTime`, global key require/block, player-in-range, group weighting.

### Data & persistence
Creature ZDO: `s_level`, `s_tamed`, `s_health`, `s_maxHealth`, `s_huntPlayer`, `s_despawnInDay`, `s_eventCreature`, `s_patrol`/`s_patrolPoint`, `s_spawnPoint`, `s_bossCount`, `s_modifiers` (kill modifiers), `s_attackers`. Custom ZDO keys (e.g. `"vm_variant".GetStableHashCode()`) are persisted automatically (only `ZDOVars.s_sessionHashes` are dropped on save).

### Multiplayer authority
Spawner owner (client) spawns → the new creature's ZDO is owned by that client; ownership later migrates to the closest peer (`ZDOMan.ReleaseNearbyZDOS` on the server, using `ZNetScene.InActiveArea`). Level, variant or stat changes must be written into the ZDO at spawn time and re-applied from the ZDO in `Character.Awake` on every peer.

### Patch points
- Data: mutate `SpawnSystemList.m_spawners` (e.g. set `m_requiredGlobalKey`) in a `SpawnSystem.Awake` prefix or at `ZoneSystem.Start`.
- `SpawnSystem.UpdateSpawnList` / `IsSpawnPointGood` / `Spawn` (private), `SpawnSystem.GetLevelUpChance(Vector3, float)` (public static, single choke point for natural + `CreatureSpawner` level-ups), `SpawnArea.SpawnOne`, `CreatureSpawner.Spawn` (returns the `ZNetView` → easy postfix), `Character.SetLevel`, `Character.Awake` (apply ZDO state).
- Because `SpawnSystem.Spawn`/`SpawnArea.SpawnOne` do not return the object, use a `[ThreadStatic]`/static "spawn context" set in a prefix and consumed in a `Character.Awake` postfix (Instantiate → Awake is synchronous).

---

## 8. Environments, weather and wind

### Key classes
- `EnvMan` (singleton) - day/night (`m_dayLengthSec` code default 1200), environment period (`m_environmentDuration` code default 20; check prefab value at runtime), transitions, wind, gameplay flags (`IsWet`, `IsCold`, `IsFreezing`, `IsDay`, `IsNight`, `IsDaylight`, `CanSleep`), `GetSnowBuildup`, `m_environments`, `m_biomes`.
- `EnvSetup` - one weather preset: gameplay (`m_isWet`, `m_isFreezing(AtNight)`, `m_isCold(AtNight)`, `m_alwaysDark`, `m_snowBuildup`), ambient/fog/sun colors and fog density per time of day, `m_lightIntensityDay/Night`, `m_sunAngle`, `m_windMin/Max`, aurora, clouds, `m_envObject`, `m_psystems` (+ `m_psystemsOutsideOnly`), `m_rainCloudAlpha`, AO, ambience audio, music overrides.
- `BiomeEnvSetup` - per biome weighted `EnvEntry` list (`m_environment`, `m_weight`, `m_ashlandsOverride`, `m_deepnorthOverride`) + music names.
- `EnvZone` - trigger volume; `m_force` → `EnvMan.SetForceEnvironment`, else returned by `EnvZone.GetEnvironment()`.

### Flow
`EnvMan.FixedUpdate`: day fraction from `ZNet.GetTimeSeconds()`; `GetBiome()` (sector at the **camera**; Ashlands/DN below sea-level height use the global sector) → `UpdateTriggers` (morning/evening music) → `UpdateEnvironment` → `InterpolateEnvironment` → `UpdateWind` → `SetEnv` (applies light, `RenderSettings.fog*`, ambient, particles, clouds, AO, audio, shader globals) → recompute gameplay flags.

`UpdateEnvironment` core rule: if `GetEnvironmentOverride()` returns a name → queue it. Priority order inside `GetEnvironmentOverride`: `m_debugEnv` (console `env`) → intro env → `RandEventSystem.GetEnvOverride()` (active event/boss event with `m_forceEnvironment`, only in the event's biome) → first AltBiome `m_forceEnvironment` → `PersistentEventSystem.GetEnvironmentOverride()` → non-forced `EnvZone`. Otherwise, once per period (`sec / m_environmentDuration`) seed `Random` with the period index and pick `SelectWeightedEnvironment(GetAvailableEnvironments(sector))` (entries flagged `m_ashlandsOverride`/`m_deepnorthOverride` replace the pick when inside those regions). A forced `EnvZone` (`m_forceEnv`) wins over everything in `GetCurrentEnvironment`.

Wind (`EnvMan.UpdateWind`): 4 octaves seeded by `timeSec / (m_windPeriodDuration/octave)`, intensity lerped between the env's `m_windMin/Max`; near the world edge wind blows outward; when the local ship has Moder's power (`Ship.IsWindControllActive`) the wind direction follows the ship. `SetTargetWind`/`UpdateWindTransition` blend over `m_windTransitionDuration`.

### Data & persistence
Nothing saved: weather is a deterministic function of net time, biome sector, event state. Everything is recomputed per client.

### Multiplayer authority
Each client computes its own weather at its own camera. Anything that must look/act the same for all players (a spell that changes weather) needs its state synced (global key, ZDO, or persistent event) and the override applied on every client. Side effects: `IsWet/IsCold/IsFreezing` drive player status effects locally; `SpawnData.m_requiredEnvironments` is evaluated on the zone-ctrl owner's weather.

### Constants
`m_transitionDuration 2`, `m_windPeriodDuration 10`, `m_windTransitionDuration 5`, `m_edgeOfWorldWidth 500`, `m_wetTransitionDuration 15`, `m_sleepCooldownSeconds 30`, `m_oceanLevelEnvCheckAshlandsDeepnorth 20` (code defaults; prefab may override).

### Patch points
- `EnvMan.GetEnvironmentOverride` (private, postfix) - cleanest point to inject a synced override (keeps the vanilla priority chain explicit).
- `EnvMan.GetAvailableEnvironments(BiomeSector)` (public, postfix) - change weights/lists per biome dynamically (e.g. calmer Deep North until awakened).
- `EnvMan.SelectWeightedEnvironment`, `EnvMan.SetEnv` (fog/light post-processing), `EnvMan.UpdateWind`/`SetTargetWind`, `EnvMan.AppendEnvironment` / `AppendBiomeSetup` (public API to register custom `EnvSetup`s, e.g. cloned from vanilla ones), `EnvMan.ForceInstantEnvironmentSwitch`.

### Vanilla quirk
`EnvMan.UpdateEnvironment` and `EnvMan.GetBiome` call `WorldGenerator.IsDeepnorth(position.x, position.y)` - **y (height) instead of z**, so the Deep North `m_deepnorthOverride` / below-sea-level branch practically never triggers. Keep this in mind before "fixing" DN weather.

---

## 9. View and render distance

What limits how far you see:

| Factor | Where | Default |
|---|---|---|
| Simulation distance (zones with full objects = near ring; far ring loads only `ZNetView.m_distant` objects) | `SimulationDistance.GetSimulationDistance(level)`; graphics setting range 0..6 (`GraphicsSettingIntExtentions.GetRange`) | level 0 → near 1/far 2 square; 1 → 2/2 circular; 2 → 2/2 square (= `OriginalDistance`); 3..6 → near = level, far 2 circular |
| Server cap | `ZNet.SimulationDistanceServerHandshake`, `RPC_RequestValidSimulationDistance` (server clamps to its own), `RPC_ValidatedSimulationDistance`, `ZNet.GetSyncedSimulationDistance`; dedicated server CLI `-simulationdistance N` (`FejdStartup`) | |
| Object instantiation | `ZNetScene.CreateDestroyObjects` → `ZDOMan.FindSectorObjects(zone, simDist, near, distant)` | near ring: all ZDOs; far ring: `ZDO.Distant` only |
| Ownership/AI radius | `ZNetScene.InActiveArea` / `PointInsideActiveArea` (Chebyshev ≤ 1.5 zones, 1 zone when near=1; ≤1.75 zones circle for 2/2 circular) | |
| Zone terrain tiles | `ZoneSystem.CreateLocalZones` (near ring) | |
| Distant terrain | `TerrainLod` (`m_terrainSize` 2400 m square around camera, `m_regionsPerAxis` 3, `m_vertexDistance` 10, rebuild every `m_updateStepDistance` 256 m) | beyond ±1200 m: nothing |
| LOD terrain hand-off | `Heightmap.GetLodHideDistance` = max(1,near) × √2 × 64 → shader `_LodHideDistance` | |
| Water mesh | `Water.ApplySettings` → `_VisibleMaxDistance` = near × 64 | |
| Fog | `EnvMan.SetEnv` sets `RenderSettings.fogDensity` from the env's `m_fogDensity*` every fixed update | |
| Camera | `GameCamera.m_camera.farClipPlane` (prefab value, never set in code), `m_skyCamera` | |
| Mesh LOD | `GraphicsSettingsManager.GetLodBias` (1, 1.5, 2, 5) → `QualitySettings.lodBias`; `LodFadeInOut` | |
| Shadows | `GraphicsSettingsManager.ApplyQualitySettings`: 80/120/150 m | |
| Grass/clutter | `ClutterSystem.m_distance` (40), `m_grassPatchSize` 8, vegetation quality | |
| Lights | `LightLod` (`m_lightLimit`, `m_shadowLimit`, `m_shadowDistance`) | |
| Distant fog puffs | `DistantFogEmitter` (100–500 m) | |

Multiplayer: rendering-only tweaks are client-only. Raising the number of **loaded** zones requires the server to allow it (server clamps clients; the server's own setting comes from its graphics settings or `-simulationdistance`). Increasing simulation distance increases ZDO sync traffic (`ZDOMan.CreateSyncList` uses the peer's simulation distance) and client CPU.

Patch points: `TerrainLod.OnEnable` prefix (set private fields before `CreateMeshes`), `GameCamera.Awake` postfix (far clip), `EnvMan.SetEnv` postfix (scale `RenderSettings.fogDensity`), `SimulationDistance.GetSimulationDistance` + `GraphicsSettingIntExtentions.GetRange` (extra levels), `Heightmap.GetLodHideDistance`, `Water.ApplySettings`, `ZNetView.Awake` (mark more prefab types `m_distant`), `GraphicsSettingsManager.GetLodBias`.

Vanilla quirk: `ZDOMan.FindSectorObjects` far ring tests the radius of the `y-k` row for the `y+k` row (symmetric, harmless).

Learned while porting Distant Horizons (2026-10-02, 1.0.16):
- `TerrainLod.CreateMeshes`/`ResetMeshes` are its only mesh calls; `ResetMeshes` clears `m_heightmaps` at once (the
  objects are destroyed at the end of the frame) and resets its point, so a later `CreateMeshes` rebuilds around the
  camera on the next `Update`. With no meshes, `Update` does nothing useful. Nothing else in the game references it.
- `Water.ApplySettings` (writes `_VisibleMaxDistance` in a property block) only runs in `Water.Awake`, `OnEnable` and
  `ZNet.ApplySimulationDistance`: a mod that changes it must call `Water.ApplySettingsOnAll` itself to undo.
- `EnvMan.SetEnv` writes `RenderSettings.fogDensity` fresh on every call that has a main camera and returns early
  without one; `FixedUpdate` calls it at most once per rendered frame with a physics step, never while paused.
  `InterpolateEnvironment` keeps the previous environment's `m_isWet` during the 2 s blend.
- Simulation levels 0 and 2 (classic) load a square of zones (`ZoneSystem.CreateLocalZones`), the others a circle of
  radius near x 64 + 32 m (`ZoneSystem.ZonesWithinRadius`).
- Shader facts (not in `.ref`, from a disassembly of the 1.0 shaders, 2026-09-22): `Custom/Heightmap` fades albedo to
  black between 200 m and 400 m from the camera with literal constants (every variant); `Custom/Water` fades alpha to
  zero between 300 m and 800 m (literal), and for LOD water `_VisibleMaxDistance` is a fade-in radius. This is why
  vanilla terrain or water can never be seen far away without fog, whatever the settings.
- Portal ZDOs live in `ZDOMan.m_portalObjects`, not in the sector lists.

---

## 10. Boss altars, summoning and boss AI

### Key classes
- `OfferingBowl` (altar, `Hoverable`, `Interactable`): `m_bossItem` + `m_bossItems` (cost), `m_bossPrefab` or `m_itemPrefab` (item altars), `m_setGlobalKey`, spawn params (`m_spawnBossDelay` 5, `m_spawnBossMaxDistance` 40, `m_spawnBossMinDistance`, `m_spawnBossMaxYDistance`, `m_enableSolidHeightCheck`, `m_spawnPointClearingRadius`, `m_spawnYOffset`, `m_spawnAreaOffset`, `m_spawnPoints`), `m_alertOnSpawn`, item-stand mode (`m_useItemStands`, `m_itemStandPrefix`, `m_itemstandMaxRange`), effects.
- `BossStone` (start temple stones): watches its `ItemStand` (trophy), plays activation steps and sets/removes `m_setsWorldKey`; `RuneStone.m_dreamCinematic` hooks into it. `ItemStand` also handles guardian powers (`GP_*`).
- `Character`: `m_boss`, `m_bossEvent` (name of a `RandomEvent` used as "forced event" while the boss HUD is up), `m_defeatSetGlobalKey`, `m_dontHideBossHud`, `m_dreamCinematic`. `BaseAI.SetAlerted` increments `GlobalKeys.activeBosses` once per boss (`s_bossCount`); `Character.OnDeath` decrements it and sets `m_defeatSetGlobalKey` (global key + queued player unique key).
- `EnemyHud.GetActiveBoss` / `m_maxShowDistanceBoss`; `RandEventSystem.GetForcedEvent`/`SetForcedEvent` (boss music, env and **add spawns** via the event's `m_spawn`).
- Boss AI = `MonsterAI` + `Humanoid` attack items: `MonsterAI.SelectBestAttack` → `Humanoid.EquipBestWeapon` chooses among items using `SharedData.m_aiAttackInterval`, `m_aiPrioritized`, `m_aiAttackRange(Min)`, `m_aiAttackMaxAngle`, `m_aiTargetType`, `m_aiWhenFlying*`, `m_aiInDungeonOnly`, `m_aiInMistOnly`, `m_aiMax/MinHealthPercentage` (phase gates). Loadouts: `Humanoid.m_defaultItems`, `m_randomWeapon`, `m_randomSets`, `m_randomItems`. Adds via `SpawnAbility`.
- Drops: `CharacterDrop.m_drops` (`m_chance`, `m_amountMin/Max`, `m_onePerPlayer`, `m_levelMultiplier`, `m_dontScale`), `GenerateDropList` (pseudo-random for chances ≤ 0.3 unless `NoPseudoDrops`).

### Flow
1. Local player hotbar-uses item on altar → `OfferingBowl.UseItem` (checks item name, count; world-level-matched items via `Inventory.CountItems(..., matchWorldLevel)`), or `Interact` for item-stand altars (all stands must be filled).
2. `InitiateSpawnBoss` → `m_nview.InvokeRPC("RPC_SpawnBoss", point, removeItems)` → **altar ZDO owner** runs `RPC_SpawnBoss`: `CanSpawnBoss` (100 random tries within `m_spawnBossMaxDistance`, solid height checks), `SpawnBoss` (start effects, `Invoke("DelayedSpawnBoss", delay)`), then `"RPC_BossSpawnInitiated"` and `"RPC_RemoveBossSpawnInventoryItems"` back to the sender (or `RemoveAltarItems` for stands).
3. `DelayedSpawnBoss` instantiates the boss on the owner (`BaseAI.SetPatrolPoint`, optional `Alert`), creates done-effects (projectiles set up with the boss as owner).
4. Global key `m_setGlobalKey` is set when offering. Kill: `Character.OnDeath` (boss owner) → drops, `m_defeatSetGlobalKey`, `Game.RegisterKill` → routed `"RPC_RegisterKill"` to every player flagged in the boss ZDO attackers list.

### Multiplayer authority
Altar owner spawns; boss owner simulates AI and computes drops; global keys go through the server. Offer/cost checks run on the offering client.

### Patch points
`OfferingBowl.UseItem` / `Interact` (cost & tier selection, hover text in `GetHoverText`), `OfferingBowl.RPC_SpawnBoss` (authoritative), `OfferingBowl.DelayedSpawnBoss` (prefix/finalizer "spawn context"), `OfferingBowl.CanSpawnBoss`, `Character.Awake` (apply tier from ZDO), `Character.OnDeath`, `CharacterDrop.GenerateDropList`, `Character.GetDamageModifiers`, `Attack.ModifyDamage` (private), `BaseAI.SetAlerted`, `RandEventSystem.GetForcedEvent`.

### Vanilla quirk
`OfferingBowl.RPC_SpawnBoss` guards with `(bool)m_nview || !m_nview.IsValid()` (always true) - harmless.

---

## 11. Vegvisirs, runestones and lore

- `Vegvisir.Interact`: for each `VegvisrLocation` (`m_locationName`, `m_pinName`, `m_pinType`, `m_discoverAll`, `m_showMap`) calls `Game.DiscoverClosestLocation`; optional `m_setsGlobalKey` / `m_setsPlayerKey`.
- `Game.DiscoverClosestLocation` → routed `"RPC_DiscoverClosestLocation"` to the **server** (`ZoneSystem.FindClosestLocation` / `FindLocations` - only the server has the full registry) → `"RPC_DiscoverLocationResponse"` to the sender → `Minimap.DiscoverLocation(pos, pinType, name, showMap)` + look-at. Any client-side mod can reuse this vanilla RPC to locate named locations.
- `RuneStone.Interact`: optional location discovery (like Vegvisir), text via `TextViewer.ShowText(TextViewer.Style.Rune, ...)`, `Player.AddKnownText(label, text)` (lore compendium), deterministic `m_randomTexts` by position, optional dream cinematic if its `BossStone` has a trophy.
- `Raven` (Hugin/Munin tutorials, `RavenText` with `m_key`, `m_priority`, `m_munin`, `m_guidePoint`), `Tutorial`, `DreamTexts` (sleep texts gated by `m_trueKeys`/`m_falseKeys`), `WayStone` (points to spawn), `Minimap.PinType` includes `Hildir1..3` and `Memorial`.
- Persistence: known texts, known biomes, location names, unique keys are **per character** (`Player` save: `m_knownTexts`, `m_uniques`, `m_customData`); map pins in the player map data.

Patch points: `Vegvisir.Interact`, `RuneStone.GetRandomText`/`Interact`, `Game.RPC_DiscoverLocationResponse` (client), `Minimap.DiscoverLocation`, `Player.AddKnownText`.

---

## 12. Traders and NPCs

### Key classes
- `Trader` (Haldor, Hildir, Bog Witch all use it): `m_name`, ranges (`m_standRange` 15, `m_greetRange` 5, `m_byeRange` 5), `m_items : List<TradeItem>` (`m_prefab`, `m_stack`, `m_price`, `m_requiredGlobalKey`, `m_levelUpEffect`, `m_buyPlayerEffects`, player-key purchases: `m_icon`, `m_name`, `m_tooltip`, `m_buyKey`, `m_incrementKey`, `m_incrementAmount`), `m_useItems : List<TraderUseItem>` (`m_prefab`, `m_setsGlobalKey`, `m_removesItem`, `m_dialog`) - the **Hildir quest turn-in**, `m_randomTalk*`/greets/goodbyes/buy/sell lines, `m_randomTalkConditionals` (`ConditionalDialog`: key list, `KeySetType` All/Any/None/Exclusive, `GameKeyType` Global/Player, placement).
- `StoreGui` (singleton UI): `Show(Trader)`, `FillList`, `BuySelectedItem`, `SellItem`, `GetSellableItem` (first valuable non-coin item from `Inventory.GetValuableItems`, sells whole stack at `m_shared.m_value × stack`), `m_coinPrefab`.
- `NpcTalk` - talking `MonsterAI` NPCs (Dvergr): greets, random talk, faction-base lines, alarm/aggro/death lines.

### Flow
`Trader.Interact` → `StoreGui.Show`. `Trader.GetAvailableItems` filters by `m_requiredGlobalKey` and hides already-bought `m_buyKey` items. Buying (`StoreGui.BuySelectedItem`) adds the item to the **local** inventory and removes coins; `m_buyKey` → `Player.AddUniqueKey`; `m_incrementKey` → `Player.AddUniqueKeyValue` (the special key `"invrows"` → `Player.SetInventorySize`, clamp 0..9 - vanilla purchasable inventory rows). `Trader.UseItem` (give item) sets `m_setsGlobalKey` (server), optionally removes the item.

The Hildir quest loop, fully data-driven: Hildir dungeons (themes `ForestCryptHildir`, `CaveHildir`, `PlainsFortHildir`) host named mini-bosses (`Skeleton_Hildir`, `Fenring_Cultist_Hildir`, `GoblinBrute_Hildir`, `GoblinShaman_Hildir`, with `_nochest` variants) that drop key chests → give to Hildir (`m_useItems` sets a global key) → `TradeItem.m_requiredGlobalKey` unlocks stock and `ConditionalDialog` changes her lines.

### Multiplayer authority
**None - trading is 100% local**. The `Trader` has no RPCs; stock is not shared or limited. Only `m_setsGlobalKey` goes through the server.

### Patch points
`Trader.GetAvailableItems` (postfix: dynamic stock), `Trader.Start` (append `TradeItem`s), `StoreGui.FillList` / `BuySelectedItem` / `SellItem` / `GetSellableItem` / `UpdateBuyButton`, `Trader.UseItem`, `Trader.CheckConditionals` (private).

---

## 13. Ocean and underwater

- Water level is the constant 30 (`ZoneSystem.m_waterLevel` / `c_WaterLevel`). Each `_Zone` contains a `WaterVolume` (trigger + `m_waterSurface` renderer). Surface height = volume Y + `CalcWave` (10 trochoid waves driven by global wind, scaled by normalized ocean depth from `Heightmap.GetOceanDepth` corners, faded by `DeepNorthWaveFade`); beyond radius 10500 the surface is lowered 100 m (world edge).
- Queries: `Floating.GetLiquidLevel(p, waveFactor, LiquidType)`, `Floating.GetWaterLevel`, `Floating.IsUnderWater`; `IWaterInteractable` objects are tracked by `WaterVolume.OnTriggerEnter/Exit` and updated in `WaterVolume.UpdateFloaters`. Tar uses `LiquidVolume`.
- Swimming: `Character.UpdateSwimming` holds the body at `GetLiquidLevel() - m_swimDepth` (code 2, 1.5 on the Player prefab; runtime dump, 1.0.16) with a spring on vertical velocity - no diving. `Character.InLiquidSwimDepth`, `IsSwimming`.
- Camera: `GameCamera.GetCameraPosition` clamps the camera to `liquidLevel + m_minWaterDistance` (code 0.3, 0.4 on the prefab; runtime dump, 1.0.16) - the camera never goes below the surface; there is **no underwater rendering path** (no underwater fog/post effect; the surface shader `Custom/Water` has no `_Cull` property, so the surface is one-sided and invisible from below). [Swim Dive](../../src/Exploration/Swimming.Dive) works around this without assets: it lifts the clamp for each `GetCameraPosition` call while the camera follows a diver (a finalizer puts it back), builds the under-water fog from a cached base in a `GameCamera.UpdateCamera` postfix (`EnvMan.SetEnv` rewrites the fog only on frames with a physics step), and turns each loaded surface 180° about its local X axis with its `_depth` corners swapped ([design doc](../design/exploration-swimming-dive.md)).
- Ocean content hooks: `ZoneVegetation.m_minOceanDepth/m_maxOceanDepth`, `SpawnData.m_minOceanDepth/m_maxOceanDepth` (serpents, fish), `Fish`, `Leviathan`, `ClutterSystem.Clutter.m_min/maxOceanDepth`.
- Authority: waves are deterministic per client (wrapped day time + wind); ships and floaters are simulated by their ZDO owner.

---

## Feature ideas

### Boss summon revamp (New, exists: partially)
- **Status:** partly covered by [Forge Idol Upgrades](../../src/Crafting/Forge.IdolUpgrades) (0.1.0): boss trophies
  (for Kall, the Crown Jewel) upgrade idols to 3 stars, which gives re-summoning bosses a lasting reward. The boss
  tiers, affixes and rewards below are not done.
- **Feasibility:** medium (tier system with vanilla assets); hard if each tier gets bespoke boss mechanics.
- **Who needs the mod:** everyone. The altar owner spawns (`OfferingBowl.RPC_SpawnBoss`/`DelayedSpawnBoss` run on a client), the boss owner computes AI/damage/drops, boss events/HUD are per client.
- **Assets:** optional (reuse vanilla VFX; a "boss essence" reward item can be a Jotunn clone with a tinted icon).
- **Hooks:** `OfferingBowl.UseItem`/`Interact` (tier choice & extra cost), `OfferingBowl.GetHoverText`, `OfferingBowl.DelayedSpawnBoss` (prefix sets a static spawn context, finalizer clears it), `Character.Awake` (postfix: if context && owner → write `vm_tier` to ZDO; on every peer read `vm_tier` and apply), `Character.SetLevel`, `Character.GetDamageModifiers` (postfix), `Attack.ModifyDamage` (postfix, attacker owner), `Character.m_bossEvent` → custom `RandomEvent` (appended to `RandEventSystem.m_events`) with `m_spawn` adds and `m_forceEnvironment`, `CharacterDrop.GenerateDropList` (postfix), `Character.OnDeath` (kill counter), `ZoneSystem.SetGlobalKey("vm_bosskills_<prefab> N")`, `EnemyHud` (name suffix).
- **Sketch:** Keep a per-world kill counter per boss in a valued global key; the next summon rolls tier = f(kills, offered item count) and the altar owner stamps `vm_tier` on the boss ZDO right after `Instantiate`. All peers apply tier from the ZDO in `Character.Awake` (HP/damage via level or explicit multipliers, extra resistances, a tier-specific `m_bossEvent` that spawns adds and changes weather) and the boss owner appends tier rewards in `GenerateDropList`.
- **Risks:** `Character.SetLevel` also multiplies `m_levelMultiplier` drops by 2^(level−1) (trophies/materials explode) - scale via custom multipliers or strip the level factor for bosses. Conflicts with CLLC boss affixes, Drop That/Spawn That, EpicLoot (its loot tables key on creature level and it patches death/drop generation). `GlobalKeys.activeBosses`/`NoBossPortals` bookkeeping must stay intact. World level (`Game.m_worldLevel`) already scales bosses and requires world-level summon items. Ownership can migrate mid-fight (store everything in the ZDO, apply idempotently).

### Big unique dungeon (New, exists: no)
- **Status:** investigated 2026-10-02 as part of "Dungeon Instances" (hand-crafted instanced dungeons with scripted multi-boss encounters, placed by admin command): [docs/design/exploration-dungeon-instances.md](../design/exploration-dungeon-instances.md). It recommends a data-driven engine mod with entrance ZDOs and locally built geometry instead of the location + `DungeonGenerator` sketch below.
- **Feasibility:** hard (framework) → very hard (one handcrafted end-game dungeon per biome).
- **Who needs the mod:** everyone. Server generates the location/dungeon; clients need all location and room prefabs to rebuild from `LocationProxy` and `s_roomData`.
- **Assets:** yes (rooms with `Room`/`RoomConnection`, props, spawners, loot, music, likely a custom boss).
- **Hooks:** `ZoneSystem.SetupLocations` (register `ZoneLocation` with `m_unique`, `m_quantity=1`, `m_prioritized`, biome, center distance), `DungeonDB.SetupRooms` (rooms with a new `Room.Theme` bit, e.g. 0x8000), `DungeonGenerator` fields (`m_zoneSize`, `m_useCustomInteriorTransform` with `Location.m_useCustomInteriorTransform`, `m_minRooms/m_maxRooms`, `m_requiredRooms`, `m_alternativeFunctionality`), `Location` (interior env, enemy level overrides, `m_blockSpawnGroups`), `CreatureSpawner` (group IDs, `m_requiredGlobalKey` gating), `Teleport`, `EnvZone`, `Game.DiscoverClosestLocation` (vegvisir), `ZoneSystem.SpawnLocation` (private) for existing worlds.
- **Sketch:** Build one location prefab per biome with a `DungeonGenerator` using a dedicated theme and a larger `m_zoneSize`, registered as unique+prioritized so world generation places exactly one; a vegvisir/NPC reveals it. For already-explored worlds, a server-side command/startup pass picks an ungenerated or empty zone in the right biome and spawns it via the private `SpawnLocation` + `RegisterLocation(generated:true)`.
- **Risks:** room geometry is parented to the generator, which exists only while its zone is instantiated for that client: the whole dungeon must fit within the player's near simulation ring of the entrance zone (≈1 zone at the lowest setting) - otherwise chain several zone-sized generators linked by `Teleport` pairs. The interior env box from `Location.Awake` covers only 64×500×64 m (add EnvZones). One location per zone; placement only in ungenerated zones; generation time rises with prioritized attempts. Removing the mod leaves orphan ZDOs/rooms ("Missing room"). Overlap/hash collisions with location packs (Warpalicious, Expand World, Better Continents). Performance: many rooms × renderers/lights.

### Distant horizon (New, exists: no)
- **Status:** implemented as [Distant Horizons](../../src/Exploration/View.DistantHorizons) (0.1.0, [design](../design/exploration-view-distant-horizons.md)), client-only: a level-of-detail quadtree of the game's own distant heightmaps over the whole world (replacing `TerrainLod`'s 3x3 grid), far tiles painted in a scaled-down space to defeat the shader's 200-400 m fade, far trees as baked impostor cards plus big rocks and buildings from the client's object store, a far sea, and clear-weather fog thinning. It does not raise the loaded-zone cap. The notes below are the original research; its actual hooks are listed in the design doc (3.6).
- **Feasibility:** medium for "see farther terrain + less fog + more loaded zones"; very hard for true distant objects (impostors of unloaded vegetation/locations).
- **Who needs the mod:** client-only for rendering; host/server must also run it (or set `-simulationdistance`) to raise the loaded-zone cap, since the server clamps clients.
- **Assets:** no (yes for an impostor system).
- **Hooks:** `TerrainLod.OnEnable` (prefix: raise `m_terrainSize`, tune `m_vertexDistance`/`m_regionsPerAxis`/`m_updateStepDistance`), `GameCamera.Awake` (far clip of `m_camera`/sky camera), `EnvMan.SetEnv` (postfix scaling `RenderSettings.fogDensity`), `SimulationDistance.GetSimulationDistance` + `GraphicsSettingIntExtentions.GetRange` (more levels), `ZNet.RPC_RequestValidSimulationDistance` (server cap), `Heightmap.GetLodHideDistance`, `Water.ApplySettings`, `GraphicsSettingsManager.GetLodBias`/`ApplyQualitySettings` (LOD bias, shadow distance), `ZNetView.Awake` (flag large landmarks `m_distant` so they appear in the far ring), `ClutterSystem.m_distance`.
- **Sketch:** Ship a client "horizon profile" (terrain LOD size 6–8 km, far clip to match, fog multiplier, optional LOD bias) plus an optional server component that accepts higher simulation levels; mark big landmark prefabs (mountains' rock formations, Yggdrasil branches, large trees) as distant so they stream in the far ring.
- **Risks:** CPU/GPU cost (distant LOD rebuilds on the `HeightmapBuilder` thread cause hitches when crossing 256 m steps; larger meshes), network cost of higher simulation distance, far-plane depth precision (z-fighting), sky/fog color mismatch at the new horizon, `m_distant` changes alter ZDO flags persisted in saves. Overlaps with Render Limits-style mods and ValheimPlus camera/graphics options.

### Underwater biome (New, exists: no)
- **Feasibility:** very hard as a real explorable underwater space; medium as an "ocean sub-biome" (reefs, seabed gatherables, new sea creatures, surface-visible features) using AltBiomes.
- **Who needs the mod:** everyone.
- **Assets:** yes (seabed props, creatures, underwater post-process/fog shader, breath UI).
- **Hooks:** `AltBiomeList.m_altBiomes` (AltBiome with `m_biome = Ocean`, `m_addVegetation` with ocean-depth filters, `m_spawn`, `m_addEnvironments`, `m_forceMusic`), `ZoneSystem.PlaceVegetation`, `SpawnSystem.IsSpawnPointGood` (ocean depth), `Character.UpdateSwimming` (allow diving below `m_swimDepth`), `GameCamera.GetCameraPosition` (remove water clamp when diving), `Floating.IsUnderWater`, `WaterVolume.GetWaterSurface`, a new `StatusEffect` for breath, `BaseAI` (`m_avoidWater`, pathfinding), `Fish`.
- **Sketch:** Phase 1: an ocean AltBiome ("Coral Shallows"/"Abyss") that places seabed vegetation and spawns via vanilla fields, visible from boats and reachable by shallow diving. Phase 2: diving - patch swimming to allow controlled descent with a breath SE, let the camera go under the surface, and render an underwater fog/tint pass when the camera is below `GetLiquidLevel`.
- **Risks:** the `Heightmap.Biome` enum/`BiomeIndex` switches (`BiomeHelpers`), terrain shader, minimap colors and `AltBiomeWorldData` all assume the vanilla biome set - avoid a new enum value. Water surface is one-sided (seen from below = sky), no caustics/underwater lighting, navmesh does not cover the seabed (AI must swim/fly). Conflicts with existing diving mods (same `UpdateSwimming`/camera patches). AltBiome lists must match on all peers.

### Deep North revamp (New, exists: no)
- **Status:** implemented as [Deep North Awakening](../../src/Exploration/DeepNorth.Awakening) (0.1.0,
  [design](../design/exploration-deepnorth-awakening.md)), with a different shape than the sketch below: the dormant
  north stays vanilla; each broken `BlackIce_Start` (counted on the server, key `mc_dn_stones`) wakes more of the Deep
  North as seeded hidden cells with their own Jotun-army spawns, stage star odds, storms (blizzard + meteors) and nature
  hostile to the Jotun (with nature bands); the third stone starts 3 vanilla invasions; after Kall an area spawns its
  Jotun once each time a player walks into it, then waits (server list) while anyone is near, and is cleared for good
  once 8-13 Jotun are killed inside it (counted by the server from the destroyed Jotun ZDOs, kept on zone-control ZDOs). The areas show as a purple tint on the explored map (the
  vanilla map colour texture). No `RandEventSystem` raids, no heavy snow, no new assets.
- **Feasibility:** medium (dormant → awakened state machine with vanilla data); hard if "very active" means new creatures/events.
- **Who needs the mod:** everyone (spawning and weather run on clients; the state is a server global key).
- **Assets:** optional (vanilla DN creatures, `FimbulvinterOrb`/`BlackIce` props, meteors, blizzard envs exist).
- **Hooks:** `SpawnSystemList.m_spawners` entries for DeepNorth (`SpawnData.m_requiredGlobalKey`, `m_spawnChance`, `m_maxSpawned`), `SpawnSystem.GetLevelUpChance(Vector3, float)` (postfix multiplier in DN when awakened), `CreatureSpawner.m_requiredGlobalKey/m_blockingGlobalKey`, `RandEventSystem.m_events` (DN raids with `m_requiredGlobalKeys`, `m_biome = DeepNorth`, `m_standaloneInterval`), `EnvMan.GetAvailableEnvironments` (postfix: calm envs when dormant, blizzards when awake), `PersistentEventSystem` (`TriggerEvent`, `m_possibleEvents` with `environmentOverride`/`objectsToSpawn`), `TriggerPersistentEventOnDestroy.OnDestroyed` / `Destructible.m_onDestroyed` / `Destructible.RPC_Damage`, `ZoneSystem.SetGlobalKey`, `WearNTear` snow load (`GlobalKeys.AllHeavySnow`).
- **Sketch:** Define a staged key (`vm_dn_stage 0..N`, server-owned). Dormant: DN spawn entries require the stage key (few passive spawns), calm weather weights, no DN events. When the owner of the first "stone" destructible sees `m_onDestroyed` (vanilla `TriggerPersistentEventOnDestroy` objects or our own marker component on chosen DN props), it sets the key: spawn lists unlock, level-up multiplier and DN raids/persistent blizzard events start, optionally heavy snow for everyone.
- **Risks:** the exact vanilla "stone" objects (BlackIce/Fimbulvinter) and their current event wiring must be identified at runtime; the vanilla `IsDeepnorth(x, y)` quirk in `EnvMan` means DN-override envs barely work today. Existing worlds where players already progressed need an initial stage migration. Spawn-list editors (Spawn That, Expand World Spawns, CLLC) touch the same data; Monstrum-style DN content packs may add unconditioned spawns. Softlock if the trigger object despawns without an owner (always gate on the owner and set the key via the server).

### Mob variant (New, exists: no)
- **Feasibility:** medium.
- **Who needs the mod:** everyone (spawns happen on clients; damage modifiers on the victim owner; attack damage on the attacker owner; visuals on all).
- **Assets:** optional (vanilla fire/frost/poison/lightning VFX and material tints suffice; custom shaders look better).
- **Hooks:** spawn roll: `SpawnSystem.Spawn`, `CreatureSpawner.Spawn` (returns `ZNetView`), `SpawnArea.SpawnOne`, `TriggerSpawner` (static context + `Character.Awake` postfix); state in ZDO `vm_variant`; apply in `Character.Awake`/`Start` postfix (tint via `MaterialPropertyBlock` like `LevelEffects.SetupLevelVisualization`, attach VFX, rename `m_name` prefix); stats: `Character.GetDamageModifiers` (resist/weakness), `Attack.ModifyDamage` (add elemental damage/status effect to `HitData`), `Character.SetupMaxHealth`/`SetMaxHealth`; death behaviour: `Character.OnDeath` (e.g. blood variant heals nearby mobs, elemental variant explodes); rewards: `CharacterDrop.GenerateDropList` (magic-themed drops).
- **Sketch:** On spawn, the spawning client rolls a variant from a biome/star-weighted table (fire, frost, lightning, poison, spirit, blood) and stores its id in the creature ZDO. Every peer applies visuals from the ZDO on Awake; the owner applies gameplay effects through damage-modifier and attack-damage postfixes, and adds a variant-specific drop (e.g. elemental essence used by the Crafting/Magic mods).
- **Risks:** direct overlap with CLLC infusions/effects (same hooks, double buffs); EpicLoot damage modifiers stack; tamed/procreated offspring inheriting variants; boss exclusion; performance of per-creature particle systems; variant ids persisted in ZDOs (harmless if the mod is removed).

### Weather staff (New, exists: no)
- **Feasibility:** medium.
- **Who needs the mod:** everyone. Weather is computed per client, so the override must be synced (server-stored) and applied on each client; otherwise wet/cold effects, visuals and environment-gated spawns differ between players.
- **Assets:** yes, light (a staff item: clone a vanilla staff with Jotunn, new icon/tint; optional totem VFX).
- **Hooks:** item `Attack` (eitr cost `m_attackEitr`, `m_attackProjectile`/`m_spawnOnTrigger` to place a totem), `EnvMan.GetEnvironmentOverride` (postfix), `EnvMan.AppendEnvironment` (optional custom envs), `EnvMan.ForceInstantEnvironmentSwitch`, `EnvMan.UpdateWind`/`SetTargetWind` (wind control), `PersistentEventSystem` (alternative carrier: area + minimap pin + persistence), `ZoneSystem.SetGlobalKey` (alternative global carrier), `Character.InInterior` (skip dungeons).
- **Sketch:** Casting spawns a networked "weather totem" (ZNetView, `m_distant`) whose ZDO stores env name, radius and expiry; each client's `GetEnvironmentOverride` postfix returns that env when the local player is inside an active totem radius (priority below boss/raid overrides). A radial menu or item variants choose Clear/Rain/Storm/Fog/Snow from `EnvMan.m_environments` filtered by biome (no clear sky in Ashlands/Mistlands unless intended).
- **Risks:** override priority vs raids/boss events/AltBiome/PersistentEvent envs; forced `EnvZone`s (dungeons) win; balancing (Mistlands mist comes from `ParticleMist`, which is biome-gated via its own `m_biome` - verify at runtime whether any env's `m_envObject`/`m_psystems` also carries mist before promising "clear the mist"); ship sailing exploits with wind control (Moder power overlap); clients without the mod see vanilla weather; EpicLoot/other weather or env mods patching `EnvMan`.

### Better shops (New, exists: partially)
- **Feasibility:** easy → medium.
- **Who needs the mod:** client-only (trading is fully local). Server needed only for shared/limited stock or server-enforced configs.
- **Assets:** no (reuse `StoreGui`; optional UI prefab edits).
- **Hooks:** `Trader.GetAvailableItems` (dynamic/rotating stock, global/player-key unlocks), `Trader.Start` (append `TradeItem`s from config), `StoreGui.FillList`/`BuySelectedItem` (quantity selector, bulk buy, stack handling), `StoreGui.SellItem`/`GetSellableItem` (sell selected item or sell-all instead of "first valuable"), `ItemDrop.SharedData.m_value` (sell prices), `TradeItem.m_buyKey`/`m_incrementKey` (one-off upgrades like vanilla `invrows`), `Trader.UseItem` (turn-ins), `Trader.CheckConditionals` (dialogue).
- **Sketch:** Config-driven stock per trader (Haldor/Hildir/Bog Witch/custom) with biome-progression unlocks, a buy-quantity slider, sell-selected/sell-all, buyback of the last N sold items, and optional daily rotation seeded by world day + trader position (deterministic across clients without networking).
- **Risks:** EpicLoot heavily extends Haldor's `StoreGui` (bounties, gamble/adventure tabs) - UI and patch-order conflicts; other trader overhauls; coin exploits via buy/sell price asymmetry; world-level item variants (`m_worldLevel`) and quality/variant when adding items; per-client configs diverge unless synced.

### Late Game quest (New, exists: partially)
- **Feasibility:** hard for the full idea (contract generator, board and tracker UI, curated late-game content, reward balance); a client-only v1 on vanilla traders with vanilla rewards is medium, because every contract type can be built on vanilla RPCs and ZDO keys.
- **Who needs the mod:** depends. A client-only v1 covers all four contract types. Kill credit arrives through vanilla `Game.RPC_RegisterKill`, which the victim's owner sends to every flagged attacker. A named target spawned by the quester's client keeps its name and stars for players without the mod (`ZDOVars.s_overrideHoverName`, `ZDOVars.s_level`). Location targets come from the vanilla server RPC `RPC_DiscoverClosestLocation`, which `Game.Start` registers only on the server. Deliveries only touch the local inventory. Everyone needs the mod only for new prefabs (a quest-giver NPC, custom reward items or creatures) or for server-checked or shared party contracts.
- **Assets:** optional. None for a board on vanilla traders with vanilla rewards; a new NPC or unique reward items need models and icons.
- **Hooks:** `Trader.Interact` (vanilla ignores its `alt` argument, so Shift+E (`AltPlace`) on Haldor, Hildir or the Bog Witch is free for a contract board), `Trader.GetHoverText` (advertise the board), `Trader.UseItem` (hand in from the hotbar), `StoreGui.Show`/`Hide`, `Chat.SetNpcText` or `Trader.Say` (private, callable through the publicized assembly) for NPC lines, `Player.m_customData` + `ZNet.GetWorldUID` (per-character, per-world state; clients receive the world UID in `ZNet.RPC_PeerInfo`), `EnvMan.GetDay` (daily rotation), `ZoneSystem.GetGlobalKey` (late-boss gating), `Game.RPC_RegisterKill` (postfix: kill credit by creature `m_name`), `ZDOMan.m_onZDODestroyed` (a delegate that tells when a tracked target is gone, no patch needed). Spawning a target: `ZNetScene.GetPrefab`, `ZoneSystem.IsZoneLoaded` and `ZoneSystem.GetSolidHeight` (spawn point), `Character.SetLevel`, `ZDOVars.s_overrideHoverName` (read by `Character.GetHoverName` and shown by `EnemyHud`; the vanilla `WarriorNames` component writes the same key), `BaseAI.SetPatrolPoint`, `ZNetView.ClaimOwnership` (to remove an expired target). Survey contracts: `Game.DiscoverClosestLocation` + `Game.RPC_DiscoverLocationResponse` (prefix), `ZoneSystem.FindClosestLocation`/`FindLocations` (server), `Location.GetLocation`. Also `Inventory.CountItems`/`RemoveItem`, `Minimap.AddPin`/`RemovePin`, `MessageHud.ShowMessage`. Content pattern: Hildir's `Trader.TraderUseItem.m_setsGlobalKey` + `Trader.TradeItem.m_requiredGlobalKey`.
- **Sketch:** Once a late boss is down (global key check), Shift+E on a vanilla trader opens a board with three contracts per in-game day. The list is seeded by the world UID and `EnvMan.GetDay`, so every player of a world sees the same one; accepted contracts are stored in `Player.m_customData` under the world UID. (1) **Slay a named elite:** the mod picks a spot in a late biome and pins it. When the quester comes within range and the zone is loaded, the quester's client instantiates the creature, sets its stars, writes a generated name to `s_overrideHoverName` and a contract id to a custom ZDO key, and calls `BaseAI.SetPatrolPoint` so it stays around the spot. The contract is done when `Game.RPC_RegisterKill` arrives for that creature's `m_name` while the tracked ZDO has 0 health (`ZDOVars.s_health`; `Character.IsDead` is local to the owner) or is destroyed. (2) **Hunt N:** count matching `RPC_RegisterKill` calls while the player is in the contract's world. (3) **Deliver:** `Inventory.CountItems` then `RemoveItem` at the trader, by hotbar use through a `Trader.UseItem` prefix or with a button on the board. (4) **Survey:** `Game.DiscoverClosestLocation(name, point, pinName, ...)` with `discoverAll` false and a chosen point (for example a random point a few km away) makes the server answer with the one instance closest to that point. A `Game.RPC_DiscoverLocationResponse` prefix recognises the request's unique `pinName`, swallows that reply and keeps the position. Arrival is a distance check, or `Location.GetLocation`, which `Player.UpdateBiome` calls about once per second to record location names. Rewards are rare vanilla items added to the inventory, with no new currency.
- **Risks:** Kill credit: `Character.RPC_Damage` runs on the victim's owner and flags each attacking player in the victim's ZDO with a bool keyed by `ZDOVars.s_attackers` plus the player's name. Only the owner sends `RPC_RegisterKill`: `Character.OnDeath` returns before the credit loop on other peers (it is reached from `CheckDeath` in the owner's `CustomFixedUpdate`, or from the `CharacterAnimEvent.Die` animation event for creatures with a death animation). The RPC carries the creature's `m_name`, boss order, kill modifiers, attacker count and a cheat flag, but no ZDO id. So a specific target needs a second signal, two players with the same name share credit, a player who never hit the creature gets nothing, and hooks on the owner's code path (`RPC_Damage`) miss kills of creatures that another peer owns. The per-creature totals in `PlayerProfile.m_playerStats[0].m_enemyStats` (what the MC mod Creature Kill and Tame Counts shows) cover every world the character played and also grow on cheated kills, so a "kills since accepted" delta could be filled in another world: count kills per world instead. Spawned targets never despawn: only `SpawnSystem` sets the despawn-in-day and event-creature flags (`MonsterAI.SetDespawnInDay`/`SetEventCreature`), so a creature the mod spawns stays in the world save until it dies. An expired or abandoned contract must remove it (as owner, or after `ZNetView.ClaimOwnership`), and any player can kill it. Because the daily list is shared, two questers with the same contract spawn two elites unless the spawn first looks for a tagged creature. `ZDOMan.m_onZDODestroyed` fires only on peers that hold the ZDO. Survey: with `discoverAll`, the server sends one reply per instance of the location type. Each reply that is not intercepted calls `Minimap.DiscoverLocation` (a pin) and, when the map is closed, turns the player (`Character.SetLookDir`). The prefix must not swallow the reply to a vanilla vegvisir read made at the same time. The RPC only knows registered locations, and the chosen one may already be looted. Late-boss keys are data: the `GlobalKeys` enum names only the first five bosses' `defeated_*` keys; later keys come from each boss prefab's `Character.m_defeatSetGlobalKey` (read the prefab at runtime, or check with `listkeys` after a kill). `Inventory.CountItems`/`RemoveItem` skip items below the current world level by default (`Game.m_worldLevel`). `Trader.UseItem` returns false on a trader whose `m_useItems` list is empty, and the "give item" hover line appears only when that list is not empty (`Trader.GetHoverText`), so hotbar hand-ins at such a trader need patches on both. Vanilla Hildir hand-ins set a world-wide global key (`Trader.UseItem` calls `ZoneSystem.SetGlobalKey`), so her quests are shared, not per player. Overlap: EpicLoot (its own window at Haldor with bounties and treasure maps), HaldorBounties (a board in Haldor's shop, needs HaldorOverhaul) and TradersExtended already change the trader UI, hence the separate Shift+E board; check that no other trader mod uses alt interact. Balance: reward inflation; `Player.m_customData` follows the character across worlds, so key contracts by world UID to stop cross-world farming.

### Wish bone deep north fix (QoL, exists: no)
- **Reading of the idea:** the "meadow-like POI" is the Deep North Viking Graveyard (`ShipSetting02`, `ShipSetting03`), the Deep North version of the Meadows stone ship `ShipSetting01` (the wiki makes the same comparison). The Deep North shipwrecks (`ShipWreck01_DN`, `ShipWreck02_DN`) have the same problem with their chest, which is buried below the mast, so the fix covers them too.
- **Why the Wishbone misses them:** the Wishbone pings only loaded objects that carry a `Beacon` ([exploration-player.md §9](exploration-player.md)). Checked in the 1.0.16 asset bundles: `ShipSetting02` is one stone ship (90%) with five buried gem pickables `Pickable_MorkHallaTreasure` (50% each, about 1.5 m deep) and a buried `TreasureChest_deepnorth_village` (50%); `ShipSetting03` is three stone ships (50% each), each with one gem (50%) and one chest (50%). The shipwrecks bury a `shipwreck_vikingship_chest` (75%). The location prefabs give the gems and the wreck chest a 20 m `Beacon`, but only on the copies nested in the location. The standalone prefabs have none, and every load after generation re-creates the objects from the standalone prefab (see §5), so the Beacon is practically never there. The graveyard chest (30 m) and the Memorial Site chests (40 m) carry their own Beacon and are detected, but half of the stone ships roll no chest, and then the Wishbone stays silent over the gems. The Meadows graveyard never gave its valuables (`Pickable_DolmenTreasure`) a Beacon, so this is a Deep North gap, not a Meadows rule. Full list of Beacon prefabs: §6.
- **Feasibility:** easy.
- **Who needs the mod:** technically client-only: `Beacon` and `SE_Finder` are local, nothing is written to ZDOs, and a player without the mod just has the vanilla Wishbone (nothing to hand off). It ships as Both under the house rule for gameplay mods, because it changes what a player's Wishbone finds: the server refuses players without the mod and sends its settings to everyone. Open choice: as a pure vanilla bug fix, it could ship client-only.
- **Assets:** none.
- **Hooks:** `PickableItem.Awake` (postfix: add a `Beacon` with `m_range` 20 to a `Pickable_MorkHallaTreasure` that has none and is not in an interior, `Character.InInterior(Vector3)`; the same prefab sits without a Beacon in the Mörkhalla rooms, which are 5000 m up), `Container.Awake` (postfix: the same for `shipwreck_vikingship_chest`, optionally `shipwreck_karve_chest`). Both only when `ZNetView.GetZDO()` is set, matched on `ZDO.GetPrefab()` against precomputed hashes. `Container.CheckForChanges` (optional postfix, runs once per second per loaded container: remove the `Beacon` of an emptied chest, `Container.GetInventory()` and `Inventory.NrOfItems()`, and put it back if items return); `Beacon.m_range`; `ZNetScene.m_instances` (live toggle). Read only: `SE_Finder.UpdateStatusEffect`, `Beacon.FindClosestBeaconInRange`, `Beacon.m_instances`, `ZoneSystem.SpawnLocation`, `ZNetScene.CreateObject`.
- **Sketch:** Give each loaded graveyard gem and Deep North wreck chest the Beacon its location meant it to have (20 m, the value on the nested copies), and only when it has none (children included), so a game fix, or the rare copy made at generation, turns the mod into a no-op. Settings (server-synced): graveyard gems (on), Deep North wreck chests (on), the older wreck chests of the Black Forest, Swamp and Plains (`shipwreck_karve_chest`, the same gap; added beyond the request), and "ignore emptied chests" (none of these chests has `Container.m_autoDestroyEmpty`, so a looted chest pings until it is broken and, being the closest Beacon, can hide a gem beside it). For that option, remove the Beacon component of an emptied chest (`Beacon.OnDestroy` unregisters it) instead of filtering `Beacon.FindClosestBeaconInRange`: Exploration (Smoothbrain) transpiles that method to scale ranges, and Smart Wishbone never calls it. Live toggle: on activation, add beacons to the objects already loaded (`ZNetScene.m_instances`); on deactivation, destroy the beacons the mod added and put back the ones it removed (`SE_Finder` null-checks its cached beacon). Testing: the `location` console command spawns in Full mode (and turns world saving off until restart unless you add `SAVE`), so its fresh copy pings even in vanilla. Test after leaving the zone and coming back, after a relog, or at a naturally generated site (`findtp ShipSetting02`); `spawn Pickable_MorkHallaTreasure` is a quick check (no ping in vanilla).
- **Risks:** The targets and ranges are game data (1.0.16 bundles), not code: an update may fix the prefabs (the "has no Beacon" guard makes the mod a no-op) or rename them (log once when a target is missing from `ZNetScene`). The interior filter assumes every outdoor `Pickable_MorkHallaTreasure` is a graveyard gem (true for the vanilla prefabs: outdoors it only appears in `ShipSetting02`/`03`; location packs may place it elsewhere). Smart Wishbone replaces the Wishbone's `SE_Finder` with a finder that only tracks prefab names from its own YAML, so it ignores these beacons unless they are listed. Exploration scales every Beacon range with its skill, which also applies to the restored beacons. The emptied-chest check runs on every loaded container, base chests included: track only containers that carry a Beacon so it stays one lookup. Balance: restoring the designers' own Beacon makes the Deep North wreck chests and the graveyard gems easier to find; that is the point of the fix, but players get more loot per trip than today.

---

## Cross-cutting notes

- **Who spawns things:** natural spawns, altar bosses, creature spawners and events' creatures are created by *client* owners. Anything that changes spawning must be on every client, and must write its decisions into the ZDO at creation time.
- **Deterministic local systems:** biome map, AltBiomes, weather, waves and vegetation seeds are recomputed by every peer; mods changing them must be installed identically everywhere (consider a version handshake).
- **Server-authoritative stores available without custom networking:** global keys (valued keys, saved in the world, broadcast on change), ZDO fields (persisted unless in `ZDOVars.s_sessionHashes`), `PersistentEventSystem` (saved, synced, minimap-visible), location registry. Per-character: `Player.m_customData`, unique keys, known texts.
- **Existing worlds:** new locations only appear in zones never generated; AltBiome changes apply instantly; generator math changes break terrain edits. Plan migrations explicitly.
- **Thread safety:** `WorldGenerator.GetBiome/GetBiomeHeight` and `BiomeSector` lookups are called from the `HeightmapBuilder` worker thread.
