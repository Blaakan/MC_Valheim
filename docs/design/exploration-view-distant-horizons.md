# Distant Horizons — design

| | |
|---|---|
| Mod | Distant Horizons |
| GUID / project | `MC.Exploration.View.DistantHorizons` (`src/Exploration/View.DistantHorizons/`, root namespace `MC.Exploration.ViewDistantHorizonsMod`, package `DistantHorizons`) |
| Category / scope | Exploration / New |
| Side | **Client**, multiplayer **Compatible**. It only draws: nothing is sent, nothing is stored in the world or the character. |
| Sheet idea | `Distant horizon` |
| Origin | The standalone mod `D:\Gits\valheim-distant-horizons` (plugin `com.distanthorizons.valheim` 0.1.0, last commit 2026-09-28), written and tested in game by its author 2026-09-21 to 09-28, ported into the MC framework on 2026-10-02 |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/` (TerrainLod, Heightmap, HeightmapBuilder, ZoneSystem, ZDOMan, ZNetScene, EnvMan, Water, RenderGroupSystem, Terminal, Game); shader facts from the standalone's disassembly of `Custom/Heightmap` and `Custom/Water` (2026-09-22, not in `.ref`); sources of 26 other mods and three ConfigurationManager builds (2026-10-02) |
| Status | Implemented (v0.1.0 code); smoke test passed 2026-10-02 with all 24 MC mods (JitCheck 592 methods, 0 failures); in-world self tests passed 2026-10-02 (4/4 `horizons.*`; full run of every mod's tests with it deployed 108/108); one adversarial review round, 18 findings fixed (5.4); near-ground fix after a user report (5.5): smoke test and `horizons.*` 6/6 passed 2026-10-02; not yet tested by hand in game in its MC form |

## Goal

The run request (2026-10-02): add the "Distant horizon" backlog idea by integrating the mod already written in
`D:\Gits\valheim-distant-horizons`, and make sure MC_Valheim is compatible with Configuration Manager (Nexus mod 740).
The expected behaviours are the standalone's features, plus the framework's rules and the ConfigurationManager part:

1. **G1 — Terrain to the edge of the world.** The vanilla 3x3 distant grid (2.4 km) is replaced by a level-of-detail
   quadtree that covers the whole world, fine near the camera, coarse far away, streamed without holes.
2. **G2 — Meets the real ground.** Near levels compute heights exactly like real zones; no step, gap, flicker or
   melting band where the far terrain meets the loaded zones.
3. **G3 — Lit far land.** Far tiles show the same textures and light as the ground around the player, without the
   terrain shader's 200-400 m fade to black; the same for the real terrain with a large simulation distance.
4. **G4 — Far objects.** Trees (impostor cards), big rocks and buildings beyond the loaded zones, seated on the far
   terrain, handed over to the real objects without popping twice.
5. **G5 — Far sea.** Sea past the 300-800 m water fade, without fighting the game's own water.
6. **G6 — Thinner clear-weather fog**, storms and rain keep theirs.
7. **G7 — Live toggle.** On and off live (MC Mods panel, file, ConfigurationManager), also inside a world; off is
   exactly vanilla (distant terrain, fog, distant water, camera range, real terrain).
8. **G8 — Settings live** and listed in a sensible order in ConfigurationManager; console commands for diagnosis.
9. **G9 — Client only**, works on vanilla servers and with vanilla friends.
10. **G10 — ConfigurationManager compatibility of the whole collection** (Nexus 740 named by the user).

### Added beyond the request (small)

- Fog thinning multiplies the fog the game and other mods set instead of replacing it (stacks with Seasons,
  GammaOfNightLights, NoFogBruh, ValheimPlus; research 2026-10-02 found the standalone silently cancelled them).
- The far sea follows `ZoneSystem.m_waterLevel` (Expand World Rivers) and ends at `WorldRadius`; it is hidden in
  interiors.
- Far tiles are created with the distant flag set before `Heightmap.Awake`, so they never join the game's list of real
  ground heightmaps (and Valheim Community Patch's registry, which would otherwise take a far tile for a loaded zone's
  ground).
- Turning the far sea off gives back the game's distant water plane at once (standalone bug: it stayed hidden).
- `LocalBlocker` for the standalone itself and for New Horizons: Treelines (same job).
- Far-object setting changes are debounced (a ConfigurationManager slider changes them every frame).
- ConfigurationManager fixes in other mods and the framework (section 3.12).

### Non-goals

- Vegetation for never-generated zones regenerated from the world seed (the standalone's planned next step; research
  in `D:\Gits\valheim-distant-horizons\docs\research\`).
- Far mist for the Mistlands (its mist is particles with a 100-500 m radius).
- Automatic world radius for Expand World Size (`WorldRadius` is set by hand), rebuilding after a world mod
  regenerates terrain (`dh rebuild`), Seasons' winter sea and seasonal far trees, VR.

## 1. Vanilla behaviour (code trace)

- **Distant terrain.** `TerrainLod` (game scene object) makes 9 `Heightmap`s of 800 m at 10 m spacing in
  `CreateMeshes` (`OnEnable`), with `IsDistantLod = true`, and rebuilds all nine once the camera moved 256 m
  (`Update` → `UpdateHeightmaps`, waits for `HeightmapBuilder.IsTerrainReady` for all nine). `ResetMeshes`
  (`OnDisable`) destroys them and resets its point. Nothing else in the game references `TerrainLod`.
- **Heights.** `HeightmapBuilder` runs one FIFO builder thread (`m_toBuild`, `m_ready` capped at 16, one `m_lock`);
  `Build` makes either a real-zone build (four corner biomes blended with smoothstep) or a distant build (one biome per
  vertex, then smoothed). The private `RequestTerrain` returns the result when ready (removing it from `m_ready`) or
  queues the job, never blocking; `RequestTerrainSync` spins on it. Real zones wait on the same queue
  (`ZoneSystem.SpawnZone`).
- **Heightmap.** `Awake` adds non-distant maps to `s_heightmaps` (what `GetAllHeightmaps`/`FindHeightmap` return),
  creates renderer and `RenderGroupSubscriber` (Overworld group: disabled in interiors), and disables itself without a
  material. The `IsDistantLod` setter removes it from `s_heightmaps`. `OnEnable` joins `Instances`, applies
  `_LodHideDistance` (near simulation distance x zone diagonal, about 181 m); `ApplySettingsOnAll` re-applies it on
  every enabled map after a simulation-distance change. `Generate` reuses an injected `m_buildData` when centre,
  scale, size and generator match.
- **Shader (not in `.ref`; standalone disassembly 2026-09-22).** `Custom/Heightmap` multiplies albedo by
  `1 - smoothstep((distance - 200) / 200)` with literal constants in every variant (black beyond 400 m); the LOD
  variant sinks the mesh near the camera, the zone variant dissolves beyond `_LodHideDistance`; water-relative bands
  (wet, sand, snow) are literal metres above `_WaterLevel`. With the game's Tessellation setting on (global keyword
  `TESSELATION_ON`) the domain stage adds a bump of up to 0.25 m whose pattern comes from the vertex world position,
  tessellated within 50 m of the camera; it also lifts ground above water by up to 2 m between 100 and 300 m (XZ)
  from the camera (details: `docs/game/exploration-world.md`, section 9). `Custom/Water` multiplies alpha by
  `1 - saturate((distanceXZ - 300) / 500)`; for LOD water `_VisibleMaxDistance` is a fade-in radius and `_WaterEdge` a
  radial discard.
- **Water.** Per-zone water tiles plus one distant plane following the player (`_IsLod` material). `Water.ApplySettings`
  writes `_VisibleMaxDistance = near x 64 m` in a property block; it runs in `Awake`, `OnEnable` and
  `ZNet.ApplySimulationDistance` only.
- **Fog.** `EnvMan.SetEnv` (from `FixedUpdate`, at most once per rendered frame with a physics step, never while
  paused) writes `RenderSettings.fogDensity` fresh from the environment's four densities when a main camera exists,
  and returns early otherwise.
- **Camera.** No game code writes `Camera.farClipPlane`.
- **Objects.** Real objects exist only in the near set (radius near x 64 + 32 m, or a square in classic simulation)
  plus `m_distant` prefabs out to total x 64 + 51 m (`ZoneSystem.ZonesWithinRadius`). The client's object store
  (`ZDOMan.m_objectsBySector`, 512 x 512 sectors) holds every generated zone on a host or in single player, and only
  synced areas on a client (dedicated server or a player-hosted game). Portals live in `m_portalObjects`, not in the sectors.

## 2. Existing mods and what they teach

Survey 2026-10-02 (`docs/research/existing-mods-exploration.md`, section 10): nothing else draws the whole world with
level of detail and objects from the save data. New Horizons: Treelines (closed source, AI, pre-1.0) draws a terrain
underlay and tree cards for three biomes; Valheim Performance Overhaul (Skarif) raises the far plane to 3 km and thins
fog; Render Limits is dead on 1.0, its MagiCorp fork maps to the 1.0 simulation distance. Lessons: fog mods write the
density in `SetEnv` postfixes or by changing the environment fields (so multiply, never recompute); Expand World Size
changes the world radius through generator patches, which the far terrain follows by itself; Valheim Community Patch
registers heightmaps in an `Awake` postfix (so set the distant flag before `Awake`).

ConfigurationManager builds (source code read 2026-10-02): `docs/modding/framework.md`, "ConfigurationManager".

## 3. Implemented design

### 3.1 Terrain quadtree (G1, G2) — `Terrain/LodTerrainManager.cs`, `TileKey.cs`, `LodTile.cs`

Root tiles cover the world disc (`WorldRadius`) with a grid clamped between 2x2 and 8x8; each level halves the tile
(`BaseTileSize` 256 m at `BaseVertexSpacing` 2.5 m, 7 levels up to 16.4 km). Every `UpdateInterval` and
`UpdateStepDistance` the desired set is recomputed: a node splits while the camera's Chebyshev distance to it is under
`SplitFactor` x its size (with `SplitHysteresis` once split) and it is within `ViewDistance`. Builds go to the game's
builder thread (`RequestTerrain`, nearest and coarsest first, at most `MaxBuildsInFlight`, only while the active area
is loaded with `WaitForZones`), meshes are made on the main thread (`MaxMeshBuildsPerFrame`) by a real `Heightmap`
created like `TerrainLod.CreateMesh` with the build data injected, so `Generate` never blocks. The drawn surface is a
cut through the tree: a tile is replaced by its children only when all four are built, children by their parent only
when it is built. `FillCracks` draws parents lowered under their children.

Exact heights (`Terrain/ExactTerrainBuilder.cs`): levels with spacing at or below `ExactMaxSpacing` queue an
`ExactBuildData` job; a `HeightmapBuilder.Build` prefix computes it on the builder thread exactly like a real zone (per
64 m zone, four corner biomes, smoothstep). Exact tiles sit `NearTerrainOffset` below their true height. Wherever a
real zone is loaded (`ZoneSystem.m_zones`), the tile's vertices under it (eroded by one vertex) drop 60 m every frame
(the "skirt"), so the tile becomes a wall under the real ground; a tile fully under real ground is not drawn.

### 3.2 Lit far terrain (G3)

`FarTerrainDraw = shrink`: tile renderers still draw (they give depth to fog, water, occlusion and ambient occlusion),
sunk by a property block (`_WaterLevel` = 1e6 flips the domain stage's lift into a sink). A `CommandBuffer` on the main
camera (`AfterGBuffer` deferred, `AfterForwardOpaque` forward) paints the same meshes 0.1 m higher in a space scaled by
a power-of-two factor per draw (the largest that keeps the draw's farthest point within 100 shader metres), with view,
projection, `_WorldSpaceCameraPos`, fog constants, `_UVScale` and `_WaterLevel` (pinned at the shore band's top)
compensated, and ambient-probe constants supplied (a command-buffer draw gets none). The far tiles use a clone of the
real zone material (`TerrainMaterial = zone`, tessellation off). In shrink mode a tile's renderer sits a further
0.5 m lower than its paint (the paint's bump pattern follows the shrunk position, the renderer's the true one, up to
0.5 m apart); `TrySampleSurface` adds it back, so far objects keep sitting on the drawn surface.

`RealTerrainFadeFix` (real zones, fixed 2026-10-02): a zone is left to the game while every point of it is within
120 m (XZ) and 200 m (3D) of the camera (there the vanilla lift is at most 0.056 m and there is no fade, so it keeps
the game's own tessellation and meets painted neighbours without a step). Every other zone keeps its renderer for
depth (sunk as above, and `_Tess` 1 in its property block) and gets a copy painted 0.05 m higher at **true size**,
untessellated, with `_WorldSpaceCameraPos` moved for that draw to a point at most 50 m from the zone's centre on the
line to the real camera, so the shader's lift (XZ under 100 m) and fade (under 200 m) never start. Copy and depth
renderer then have the same mesh, bump and bands, and view directions keep their sense (only the snow glint uses
them). Before the fix copies were shrunk like far tiles: their tessellation bump followed the shrunk position, so the
depth renderer poked through in dark oval patches near the player (only with Tessellation on), and shore bands were
stretched (wider pale shore); a zone at the camera was usually painted too (its bounds' diagonal alone is 90 m).

### 3.3 Far sea (G5) — `Terrain/FarWater.cs`

A second buffer before the transparent pass paints a polar ring (inner: one zone inside the game's water, outer:
`ViewDistance`) with the game's own LOD water material, in 6 radial bands each with its own scale factor. Foam,
refraction and the grab-pass background are switched off, the default reflection probe is supplied, fog is rescaled
(`FarWaterFog`). While painting, the `Water.ApplySettings` postfix pushes the game's distant plane's fade-in radius out
of reach (1e6); `SetSeaPainting(false)` (sea off, interior, feature off) asks the game to re-apply its settings, which
brings its plane back. Level: `ZoneSystem.m_waterLevel`; edge (`_WaterEdge`): `WorldRadius`.

### 3.4 Fog (G6) — `Patches/EnvManPatches.cs`

`SetEnv` postfix, only when a main camera exists (else vanilla wrote nothing): the environment's own density decides a
factor (full `FogDensityMultiplier` at or below `FogClearDensity`, none at or above `FogStormDensity`, a linear ramp between;
wet weather untouched with `KeepWetWeatherFog`), and the rendered density is multiplied by it. The value before and
after is remembered so turning off writes the fog back at once when it still holds the mod's value.

### 3.5 Far objects (G4) — `Objects/*`

1024 / 512 / 256 m tiles, one band each from the nearest edge with hysteresis: Mesh (< `ObjectMeshDistance`: lowest
LOD of every enabled kind), Full (< `ObjectFullDistance`: trees as cards, big rocks and buildings as meshes), Thin and
Far (one big tree in N, enlarged). Object lists are snapshots of the client's sector lists (`ZoneObjectSource`),
refreshed after `ObjectCacheSeconds` (jittered) or when `ZDOMan.m_onZDODestroyed` marks a zone. A zone is handed to
the real objects only once `ZNetScene.HaveInstance` says they all exist, and taken back when it leaves the near set
(also for the distant ring). Cards are baked at run time into a 1024x1024 atlas on a free layer, drawn with a clone of
the game's leaf material; meshes are merged per material with wind off. Objects sit on the far terrain
(`TrySampleSurface`; rocks and buildings shift with their zone's height error).

### 3.6 Framework integration (G7) — `Plugin.cs`, `TerrainLink.cs`, `Patches/TerrainLodPatches.cs`

- `TerrainLod.OnEnable` prefix (world load): attach both managers to the `TerrainLod` object and skip `CreateMeshes`
  (vanilla runs when the material is missing or the patch throws). `OnDisable` postfix (logout): managers stop; the
  scene unload destroys them.
- `OnActivated` in a world: `FindAnyObjectByType<TerrainLod>` and attach; the terrain manager removes the vanilla grid
  at its first `Update` (`TerrainLink.SetVanillaActive`, from `TerrainLod.m_heightmaps.Count`).
- `OnDeactivated` (patches still on): object manager then terrain manager disabled and `DestroyImmediate`d (their
  `OnDisable` detaches both camera buffers, unsubscribes `Camera.onPreRender` and the config event, puts the real
  zones back, destroys tiles, root objects and material clones, restores the far plane and releases the sea plane);
  then `TerrainLod.ResetMeshes` + `CreateMeshes` bring the vanilla grid back and the fog is written back. The reset
  matters: vanilla `TerrainLod.Update` keeps running while the far tiles show and moves its point along with the
  camera on its empty grid, so without it the new 3x3 would stay without meshes until the camera moved 256 m (found
  by the review, 5.4). At game quit nothing is touched (scene teardown).
- The standalone's `TerrainLod.Update` prefix is gone: with no vanilla meshes, vanilla `Update` only moves its point
  (handled by the reset above).

| Patch | Kind | Why |
|---|---|---|
| `TerrainLod.OnEnable` | prefix | take over at world load |
| `TerrainLod.OnDisable` | postfix | stop at logout |
| `Heightmap.ApplySettingsOnAll` | postfix | put the far tiles' hide distance and water level back after the game rewrites them |
| `Water.ApplySettings` | postfix | hide the game's distant water plane while the far sea is painted |
| `EnvMan.SetEnv` | postfix | clear-weather fog thinning |
| `HeightmapBuilder.Build` | prefix, `[AlwaysOnPatch]` | exact heights on the builder thread (3.1, decision 5.1) |

### 3.7 Configuration (G8) — `DHConfig.cs`

Sections LOD layout, Streaming, Rendering, Objects, Logging (plain names that sort after General: aedenthorn's manager,
Nexus 740, lists sections by name, and the standalone's numbered sections would have come before General there,
against the house rule "General with Enabled first"; shudnal's and the upstream manager keep the bind order). Every entry gets an `Order`
(first bound = 100) so managers keep this order. Layout entries rebuild the terrain 0.5 s after the last change;
object entries reset bands, or everything for atlas and material entries, 0.5 s after the last change; the rest is read
live. `DHConfig.Changed` runs each handler in its own try.

### 3.8 Console — `DhCommand.cs`

`dh` (stats), `rebuild`, `envs`, `objects [on|off|rebuild]`, `get|set` (this mod's settings only, not General), `off`.
Debug builds add `dump`, `atlas`, `shot` and `terrain` (tile inspector and live render experiments;
`dh terrain abshots [seconds]` saves the current view once per draw mode, the same modes as the self test
`horizons.near`, plus once with the mod off, and logs the numbers: for bug reports about wrong ground). Registered in
`OnActivated`, removed in `OnDeactivated` (only our own entry).

### 3.9 Multiplayer and hand-off (G9)

Everything runs on the local client and only reads: world generator, local object store, `ZNetScene.HaveInstance`.
No RPC, no ZDO write, no object instantiated from a prefab. Nothing reaches another player. On a host the far objects
show every generated zone, other players' buildings included; on a client (dedicated server or a player-hosted game)
only synced areas.

### 3.10 Performance

Defaults: 64 to 125 terrain tiles at rest (about 0.5 to 1 million triangles; around 160 tiles briefly while moving),
far objects up to 6 km (standalone figures, computed from the layout, not measured by frame rate). One builder thread
is shared with zone loading (`WaitForZones`, `MaxBuildsInFlight`). Card bakes are one per frame and use a synchronous
read-back. The README lists the settings that lower the cost.

### 3.11 Files

`Plugin.cs`, `DHConfig.cs`, `TerrainLink.cs`, `DhCommand.cs`, `ForeignMods.cs`, `Diagnostics.cs`, `SelfTests.cs`,
`Terrain/` (manager, tile, key, exact builder, far water), `Objects/` (manager, catalog, zone source, tile builder,
impostor atlas), `Patches/` (one file per game class).

### 3.12 ConfigurationManager compatibility across the collection (G10)

- Framework: `Status` gets `HideDefaultButton` and a `CustomDrawer` that draws plain text
  (`ConfigurationManagerAttributes.ReadOnlyText`); `ReadOnly` alone only looks read-only in shudnal's build.
- Crossbow Stays Loaded and Creature Kill and Tame Counts: `Order` on every setting (they had none, so managers sorted
  by name and Crossbow's "the two settings above" was wrong there).
- Forge Idol Upgrades (`LevelsLost`, upgrade costs) and Deep North Awakening (`ClearKillsMin/Max`):
  `ShowRangeAsPercent = false` (aedenthorn's and the upstream manager turn int ranges 0..100 or 1..100 into percentage sliders on their
  own; shudnal's does not).
- Batch Station Feeding (`ModifierKey`) and Crafting Search and Sort (`FocusSearchKey`): a key the game cannot read is
  refused with a warning (a manager can store any key; before, the first silently stopped batch feeding and the second
  broke the search refresh every frame).
- Sneak Ambush's self test no longer fails when a mod thins the rendered fog.
- The MC Mods button hides itself when a configuration manager is installed (user decision 2026-10-02, after the
  question "remove the panel or nest it under Configuration Manager?"): F1 is then the one place; the spawn notice
  points there. Without a manager the panel works as before (it is the only in-game switch for most players).
- Not changed, follow-up task: Switchable Lights, Sneak Ambush, Forge Idol Upgrades, Dual Wielding and Breeding Star
  Inheritance push server rules on every change without a delay (a slider drag sends one push per frame); the other
  Both mods already wait.
- Nexus 740 itself (0.5.0, 2021) is likely broken on 1.0 (main-menu button, console hook): nothing an MC mod can fix;
  documented, test F14 in `src/Shared/TESTING.md`.

### 3.13 Coordination with other MC mods

- Sneak Ambush: also postfixes `SetEnv` but reads the environment data, so its fog bonus ignores the thinning.
- Swim Dive: takes the current fog as its under-water base and puts back only its own value; compatible with the
  multiply. Its surface flip does not touch the distant water plane's material.
- Deep North Awakening: its forced blizzard goes through `SetEnv`; thinned only if its density is under
  `FogStormDensity` (unverified). It also hooks `m_onZDODestroyed` (multicast, both coexist).
- Sailing Skill patches `Terminal.InitTerminal`; no longer relevant (the `dh` command is registered live).

### 3.14 Spyglass boost (added 2026-10-02 with the Spyglass mod) — `ViewBoost.cs`

The MC Spyglass ([design](exploration-view-spyglass.md), 2.6) zooms the main camera's field of view, which DH's paint
already follows (`PaintFor` uses the frame's projection), but DH picks detail by XZ distance only, so a zoomed far view
showed coarse land and no buildings beyond `PieceDistance`. While a spyglass is up it writes a `double[]` in the
AppDomain slot `MC.ViewBoost.v1` every frame (layout 1, frame, zoom, weight, forward, half fields of view); no assembly
reference either way, a reading older than 5 frames is ignored. `ViewBoost.Read` (once per frame) turns it into a
state; `State.Factor(cam, tile box)` = `min(zoom × weight, SpyglassMaxBoost)` for tiles within the view's half
horizontal field of view + 4° (XZ angle minus the tile's angular radius), fading to 1 over 20° (smoothstep), 1 for
everything else and whenever no spyglass is up (code paths then identical to before). Terrain: `Visit` splits when
`cheb < SplitFactor × h × size × factor`; `Priority` divides by the factor (looked-at tiles build first). Objects:
`Visit` uses `distance ÷ factor` for the band and the building and rock distances, keeps the Mesh band within its true
distance (farther = cards), puts the terrain keys in the signature of boosted tiles too (the land under them refines
two levels right after they build, so their trees and buildings are seated again), and `RecomputeDesired` widens the
search by the largest factor (tiles outside the view stop at their band limit at once). `ViewBoost.Poll` asks both managers for a recompute when the boost starts or stops, the
view turns by a third of its width (at least 2°) or the zoom changes by 15%, at most every 0.2 s. Settings (section
Spyglass, no rebuild on change): `SpyglassDetail` (true), `SpyglassMaxBoost` (4, 1-8). Measured (`horizons.boost`):
9 far leaf tiles within 8° of the view before, 37 with x4, 9 again after. Known limits: the cone edge can put tiles two
(x8: three) levels apart (crack filling covers it as for any refinement); panning streams new tiles at the normal build budget.

## 4. Edge cases

- Feature turned on in a world: the vanilla grid disappears at once and the far terrain streams in over a few
  seconds (sky can show at the horizon meanwhile).
- Off and on within one frame (file watcher, console): managers are destroyed immediately, so the new attach never
  finds a dying component.
- Logout with the feature on: managers die with the scene; `Current` is cleared by the `OnDisable` postfix.
- Quit from a world: `OnDeactivated` touches nothing (`Game.IsShuttingDown`); the builder is already disposed
  (`m_lock` null is checked).
- Jobs queued on the builder when the feature turns off: the always-on prefix still computes exact ones; distant ones
  are built by vanilla; both age out of the ready list.
- Interiors: tiles are skipped by the renderer check (RenderGroupSubscriber), the far sea by
  `RenderGroupSystem.IsGroupActive(Overworld)`.
- A far-object tile whose build throws: half-made meshes are destroyed and the tile retries after 10 s (one error).
- Paused single player: `SetEnv` does not run, so turning off writes the fog back itself.
- Config file from the standalone is not read (new GUID); same keys, new section names.

## 5. Decisions and open questions

### 5.1 Decisions

1. **Client side** (to confirm): a pure view mod changes nothing for other players, so it is not "required
   everywhere". It does reveal far land and, on a host, every building of the world; a server cannot stop a player
   from using it. If the user wants servers to control it, it becomes a Both mod with a server setting.
2. **GUID `MC.Exploration.View.DistantHorizons`, name "Distant Horizons", package `DistantHorizons`** (permanent at
   release). Same name as the well-known Minecraft mod it imitates and as the standalone.
3. **One mod**, not terrain/objects/fog as separate mods: the parts share the quadtree and the far-terrain sampling;
   each part has its own switch (`ObjectsEnabled`, `FarWater`, `FogDensityMultiplier = 1`, `RealTerrainFadeFix`).
4. **Console:** `dh on` / `dh toggle` dropped (a turned-off mod has no command); `dh off` kept. Experiments Debug only.
5. **`HeightmapBuilder.Build` prefix is `[AlwaysOnPatch]`** although it registers no content: the method runs on the
   builder thread outside any lock, so patching it while that thread runs (live toggle) could tear the detour. Patched
   once at start, before the thread exists. Safe while off (acts only on the mod's own job type).
6. **Managers destroyed with `DestroyImmediate`** on deactivate (no reuse of a dying component).
7. **`DebugLogging` kept** as a user setting (stats and atlas dump for bug reports), unlike other MC mods.
8. **Mid-world activation drops the vanilla grid at once** instead of keeping it until the far terrain is built
   (keeping both would draw two terrains in the same place for a few seconds).
9. **Real-zone copies at true size with a moved shader camera** (2026-10-02, after the "patches next to the player"
   report, 5.5) instead of shrunk like far tiles. Only true size keeps the game's tessellation bump and its
   water-relative bands exact; the shader reads the camera position only for the lift, the fade, the dissolve and the
   snow glint, so moving it changes nothing else. Cost: painted zones (they start about 30 m from the camera) are not
   tessellated; the game tessellates only within 50 m (and below factor 2 beyond 30 m), so no visible change.

### 5.2 Open questions

- Should the far objects hide other players' buildings on a host (privacy), or follow a server rule?
- Automatic world radius for Expand World Size (read `_WaterEdge` from the water material, or EWS's config).
- Rebuild after Expand World Size / Expand World Data regenerate terrain (`WorldGenerator.Pregenerate` postfix).
- Seasons: re-bake far objects on a season change; carry its frozen-sea tint to the far sea.

### 5.3 Unverified names and values

- Shader constants and properties (`_LodHideDistance`, `_WaterLevel`, `_UVScale`, `_Tess`, `_IsLod`,
  `_VisibleMaxDistance`, `_WaterEdge`, `_FoamColor`, the 200/400 m and 300/800 m fades): from the standalone's shader
  disassembly, not in `.ref`; all uses are guarded by `HasProperty` or harmless when absent.
- Weather fog densities (Clear 0.003, Rain 0.03, Mist 0.02+, SnowStorm 0.05; standalone `dh envs` 2026-09-21);
  `Twilight_SnowStorm` not measured.
- Size of the game's distant water plane (about 4 km).
- The free layer used for card bakes (picked at run time, logged).

### 5.4 Review

One adversarial review round on the port and the ConfigurationManager changes (five reviewers: life cycle, game API,
robustness, config, docs; three skeptics tried to refute each finding), 2026-10-02. 18 findings, all confirmed and
fixed:

- **High:** turning the mod off brought back the vanilla 3x3 grid without meshes until the camera moved 256 m
  (reported by three reviewers): the standalone froze `TerrainLod.Update` with a prefix, the port dropped it, and
  vanilla `Update` kept moving its point on the empty grid. Fixed with `ResetMeshes` before `CreateMeshes`
  (`TerrainLink.RestoreVanillaGrid`); `horizons.detach` now waits until the nine vanilla tiles have meshes.
- Batch Station Feeding's hover hint named an unreadable `ModifierKey` after the fallback; it now names the game key.
- The `Status` drawer cached its style from the first draw (font size frozen); it now follows the manager's skin.
- Section names: `Distant objects` and `Debug` became `Objects` and `Logging` so `General` is first also in
  aedenthorn's manager, which sorts sections by name.
- A live layout change now logs the layout (and the unreachable-spacing warning) again.
- Docs: ConfigurationManager order and percent claims limited to the builds they hold for, the five mods that still
  push rules on every change named, Seasons' winter sea, the 20 km / 30 km caps for big worlds, the game's own
  console setting, fog wording for light mist, "rarely float or sink", "someone else's game" instead of "dedicated
  server", the Release console commands, the live layout log.

### 5.5 Fixes after in-game reports

- **Patches of wrong ground next to the player** (user report 2026-10-02, screenshots at a Meadows sea shore: dark
  smooth-edged blobs on the grass, as if a second terrain crossed the real one). Reproduced in the probe world with
  the new self test `horizons.near` (Tessellation on, simulation distance 6): dark ovals inside zones the mod repaints
  and a thin dark line along the edge of a zone the game draws; gone with real zones left to the game, gone with the
  depth renderers hidden, gone with the `TESSELATION_ON` keyword off; far tiles, their shadows and far objects not
  involved (each hidden in turn), and no far-tile vertex above the real ground. Cause: the shrunk copy evaluated the
  tessellation bump at shrunk positions (other pattern than its depth renderer's, up to 0.5 m apart, against a 0.1 m
  lift), and nearly every zone was copied (the old rule used the bounds' diagonal, 90 m, so the zone at the camera
  was copied as well). Fix: 3.2 (true-size copies, tighter left-to-game rule, 0.05 m lift) and decision 9; far tiles
  got the 0.5 m depth drop for the same bump mismatch. Verified with `horizons.near` before and after at the same spot
  (every zone forced to be painted: ovals before, none after). The pale shore strip still looks wider than with the
  mod off, also with real zones left to the game: that is the thinner fog (G6), not the copies.

## 6. Tests

`src/Exploration/View.DistantHorizons/TESTING.md`: T00 (automated in-world tests `horizons.logic`, `.terrain`,
`.objects`, `.boost`, `.detach`, `.near`), T01-T22 single player (one or more per goal; T21-T22: ground next to
the player and at the edge of the loaded area), M01-M03 multiplayer (vanilla server,
friend without the mod, both with it), C01-C08 cross-mod (C07-C08: Spyglass boost). ConfigurationManager items for the collection: `src/Shared/TESTING.md`
F13-F15, Batch Station Feeding T27, Crafting Search and Sort T34.
