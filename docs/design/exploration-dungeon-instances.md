# Dungeon Instances: investigation and design

| | |
|---|---|
| Mod (proposed) | Dungeon Instances |
| GUID / project (proposed) | `MC.Exploration.Dungeon.Instances` (`src/Exploration/Dungeon.Instances/`, root namespace `MC.Exploration.DungeonInstancesMod`, package `DungeonInstances`). Alternatives in section 8. |
| Category / scope | Exploration / New |
| Side | **Both**, required everywhere: the server refuses players without the mod (copy of Swimming.Dive's `PlayerCheck` / `ServerRules` / `Patches/ZNetPatches.cs`), multiplayer Compatible, network version 1 |
| Sheet idea | `Big unique dungeon`, or a new row (open decision D10) |
| Game version checked | Valheim 1.0.16 on Unity 6000.0.75f1. Decompiled code in `.ref/decompiled/assembly_valheim` and `.ref/decompiled/SoftReferenceableAssets`. The 1.0.16 SoftRef bundles and `globalgamemanagers` were decoded with scratch readers on 2026-10-01 (boss prefabs, attack items, animator triggers, layer table, collision matrix, `Pathfinding` scene values, Kall and Queen arenas). Mod sources on GitHub and the full Thunderstore Valheim listing were read on 2026-10-01. |
| Status | **Investigation** (no code), 2026-10-02. Built from seven research reports (vanilla interiors, streaming and instances, Unity authoring, in-game authoring, boss AI, prior art, repo fit). Each report was checked by an adversarial verifier. Only confirmed or high-confidence claims are used here. Verifier corrections are applied except where a claim is marked (unverified). A second review pass on 2026-10-02 re-checked in `.ref` the zone-hold and teleport waits, ownership revisions, parent `ZNetView` lookups, the SoftRef loader order, `EnvZone`, world-level scaling, `Teleport.Interact`, `ZoneSystem.SpawnLocation`, door components, save paths and `WaterVolume`, and fixed the design where they broke it. |

---

## 1. Summary and recommendation

Vanilla Valheim has no instances. Every dungeon is shared, persistent world state: a location whose interior hangs about
5000 m above the entrance, linked by a non-networked `Teleport` pair. Kall's arena (`DN_Bossroom`) is the closest vanilla
precedent for what you want: a fully hand-built interior with no `DungeonGenerator`, a sleeping boss nested in it, an
altar and a global-key gate. Nothing on Thunderstore combines hand-made dungeons, an instanced run and scripted
multi-boss encounters for 1.0 (full Thunderstore listing read 2026-10-01; Nexus could not be checked automatically, it
answers automated reads with HTTP 403), so this mod would fill a real gap. The closest prior art covers parts only:
craigins/valheimmods (MIT, GitHub only, untested in game) makes temporary per-instance copies of procedural
dungeons beyond the world edge; Mayheim Míticas has a run lifecycle (start lock, respawn at the entrance) but rebuilds
a vanilla dungeon in place; ExtendedBosses (MIT) adds owner-run phases to the vanilla bosses (section 9).

The recommendation is one engine mod, `MC.Exploration.Dungeon.Instances`. Each dungeon is a **data folder**: a layout
JSON, an encounter JSON and an optional asset bundle. The engine does five things:

1. It places small networked **entrance** objects anywhere in a world, including worlds already explored. The server
   writes them from a per-world placements file that the admin, or Claude, can edit.
2. It builds each dungeon's static geometry **locally on every game** in a fixed sky band (y 3200 to 3950) above the
   entrance's zone. One persistent networked "anchor" per 64 m zone builds and owns that zone's geometry, the same
   pattern as the vanilla `LocationProxy`, but the geometry sits under a separate non-networked root, never under the
   anchor itself (section 3.4). Only stateful things (spawners, chests, doors, bosses) become networked objects.
3. It moves players in and out with the vanilla non-distant teleport. If the floor is missing, vanilla sends the player
   back instead of letting them fall.
4. It runs encounters from JSON. A networked **encounter controller** per arena handles the seal, the phases, sequenced,
   linked or council bosses, wipes and resets. Bosses are runtime clones of vanilla creatures that have rich animators,
   driven by a `BossAI : MonsterAI` subclass, with the vanilla death chain, loot and progression key removed. The
   server grants each engage to exactly one player, whose game spawns the bosses, so vanilla owner-side setup runs and
   no boss set is spawned twice. All of their state lives in the ZDO, and every ZDO has a single writer, so state
   survives a change of owner.
5. It resets the dungeon when it is empty, never deleting players' tombstones. A catch zone under every footprint and a
   boss leash bring back anything that falls out.

You author the areas in a Unity 6000.0.75f1 copy of the ripped game, with vanilla kit pieces and your own Blender meshes.
Claude-written editor tools (driven through MCP for Unity) turn that into a "mocked" bundle that contains no game files.
A no-Unity, in-game path (build, then capture to JSON) is available for blockouts and quick dungeons.

Key decisions:

- **Instance model (v1):** one copy per placed entrance. It is shared and persistent, with a run lifecycle: it fills on
  first entry, seals arenas during fights, resets when empty for N minutes, and has optional lockouts. Everything sits
  behind a "slot" abstraction, so true per-party copies (a pool of slots in a reserved far region) can be added later
  without changing the authoring format. (Open decision D1.)
- **Where an instance lives:** the column above the entrance's zone centre. All geometry stays between y 3200 and 3950,
  with the entry floor at y 3500. Above 3000 vanilla treats you as inside. Below 4000, Venture Location Reset leaves it
  alone. It is far below the AI navmesh ceiling of 5500 and below every vanilla interior (about 4750 and up).
- **Geometry:** built per client from data by persistent anchors, one per 64 m zone, with `ZDO` type Terrain. Among
  the objects a client has received, Terrain-type ones are instantiated first (`ZNetScene.ZDOCompare`), and while an
  anchor holds its zone with `SetLoadingInZone`, lower-type objects in that zone wait (`ZoneSystem.IsZoneReadyForType`).
  The server still sends other peers' `Prioritized` objects before everything else (`ZDOMan.ServerSendCompare`), and
  only the arrival zone's anchor is guaranteed to have built its geometry before the player lands; neighbouring cells
  may still be loading. The zone hold is dangerous: vanilla waits for it with no timeout when entering and when logging
  in at the exit point, so every hold is released in `finally`, in `OnDestroy` and by a 20 s timeout (section 3.4, hard
  rules). The geometry is never saved as thousands of pieces and leaves almost nothing behind if the mod is removed.
- **Authoring:** a hybrid. Unity (rip copy plus mocks plus our own resolver) is the main path for hand-crafted and new
  assets. In-game build plus `dungeon capture` covers blockouts and simple dungeons. One layout format serves both.
- **No Jotunn.** We write a small own resolver that reads Jotunn's `JVLmock_` naming, using only public
  `SoftReferenceableAssets` API (`Runtime.MakeAllAssetsLoadable`, `GetAllAssetPathsInBundleMappedToAssetID`,
  `SoftReference<T>`, `Utils.Instantiate`).
- **Bosses:** three layers. (a) Data-defined clones of vanilla creatures with cloned attack items, using health windows
  for phases. (b) A `BossAI` subclass plus an encounter controller for scripted actions. (c) A JSON encounter format
  with a fixed action vocabulary.
- **Placement:** the server creates entrance ZDOs directly. A custom vanilla location is possible (a direct
  `ZoneSystem.SpawnLocation` call in Full mode with our own rotation, plus `RegisterLocation`), but it needs a SoftRef
  AssetID for the location prefab (`Runtime.AddManifest` before the loader exists) and allows one location per zone
  (`ZoneSystem.RegisterLocation`). The plain ZDO entrance needs neither, at the cost of re-implementing the few
  vanilla `Location` features we want (section 7.1). The admin command is `dungeon ...`, with its own admin-checked RPC
  that replies. The `dungeon suggest` command plus the placements file let Claude choose and set positions per world; on
  a remote server you paste the output into chat and Claude replies with the commands.
- **Vanilla-consistent exit rule:** on a world with the `NoBossPortals` modifier, our exit refuses to leave while an
  alerted boss with a boss event stands inside the same dungeon, as vanilla `Teleport.Interact` does for vanilla
  dungeons (section 3.5).
- **Repo fit:** Both-side and required everywhere. Prefabs are registered always-on. Dungeons ship under `Content/`.
  The logout-point fix is an `[AlwaysOnPatch]`. In-world self-tests validate every referenced name.

---

## 2. What "instance" can mean in Valheim

### 2.1 The vanilla baseline

- `Character.InInterior(Vector3)` is simply `position.y > 3000`. Vanilla interiors hang 5000 m above their location.
  The `Interior` transform sits at local y 5000 in `Mistlands_DvergrBossEntrance1` and `MountainCave02`, and at 5002 in
  `DN_Bossroom` (asset dumps). `Location.Awake` puts the interior `EnvZone` box at the zone centre, location y + 5000,
  scaled 64 x 500 x 64.
- The door is a `Teleport`. It has no `ZNetView` and points at a partner `Teleport` in the same prefab. It fires on
  trigger-enter or interact and calls `TeleportTo(..., distantTeleport: false)`. It never checks
  `Humanoid.IsTeleportable` (ore goes in), and is blocked only by `NoBossPortals` + `InInterior` +
  `Location.IsInsideActiveBossDungeon`, by `hold`, or by a missing target (`Teleport.Interact`). `Player.TeleportTo`
  also refuses during a running teleport and for 2 s after one. That cooldown is what stops a player from bouncing
  straight back out of the partner trigger.
- Dungeons are persistent and shared. `CreatureSpawner` with `m_respawnTimeMinuts <= 0` spawns once and never again.
  `Container` default loot is rolled once (`s_addedDefaultItems`). No vanilla boss room seals.
- Dungeon Splitter / SkadiNet, often described as "instancing", do **not** copy anything: they filter network sync by
  height (y >= 1500), so all players above one spot still share one dungeon (section 9). The only real per-instance
  copies in prior art are craigins/valheimmods' `CraiginsValheimInstances` (MIT, GitHub only, marked untested): one
  procedural dungeon copy per instance in its own zone (zones 176 to 255, y 20000), non-persistent ZDOs, reaped 20 s
  after the last player leaves, tombstones and items inside lost. At y 20000 walking AI would have no navmesh
  (inference from `Pathfinding`, see 2.3).

### 2.2 Three possible models

| Model | What the player gets | Cost | Fit |
|---|---|---|---|
| **M1. Vanilla style**: one persistent shared interior per entrance, never reset | A secret place that stays as you left it | Lowest | Story dungeons, one-time clears |
| **M2. Shared copy with a run lifecycle** (recommended v1): one copy per entrance, seals during fights, resets after N empty minutes, optional lockouts and a "one party at a time" lock | An MMO-like run on a private or small server | Medium | The requested experience for most groups |
| **M3. Per-party copies**: a pool of identical slots in a reserved far region; each party gets its own copy | True instances: two groups can run the same dungeon at once | High (edge-of-world hazards, relog redirect, far teleport handshake) | Later phase, only if needed |

M2 and M3 share the same engine if a "slot" is an abstraction: a column (X, Z) plus a base height, holding one copy of a
layout. M2 is a pool of one slot per entrance, placed above it. M3 is a pool of N slots in a far grid.

### 2.3 Constraints that shape every model

**Height.**

- Above y 3000 (`Character.InInterior`), vanilla changes these things:
  - Random events skip you (`RandEventSystem.IsInsideRandomEventArea`).
  - Building needs `Piece.m_allowedInDungeons`, `EnvMan.CheckInteriorBuildingOverride` or the `DungeonBuild` global
    key (`Player` placement, `NotInDungeon`). The check uses the player's own `InInterior()`.
  - AI hearing is capped at 12 m (`BaseAI.CanHearTarget`).
  - The world modifier enemy speed and size (`Game.m_enemySpeedSize`) is not applied.
  - **The world level still enlarges creatures indoors.** `Character.Awake` (Character.cs 724-733) multiplies a
    non-player's `localScale` by `1 + Game.m_worldLevel x m_worldLevelEnemyMoveSpeedMultiplier` with no interior check
    (code default 0.2, Game.cs 178; the serialized value is unverified). On a world-level-3 world a boss is about 1.6x
    its prefab size, so a TrollSize boss (agent height 7 m) stands about 11 m tall. The navmesh agent type
    (`BaseAI.m_pathAgentType`, `Pathfinding.SetupAgents`) does not grow, so the boss can path into doors and ceilings
    its body no longer fits (Part B step 4 sizes clearance for this).
  - **The sun is vertex-lit inside.** While the local player is `InInterior`, `EnvMan` sets the directional light to
    `LightRenderMode.ForceVertex` (EnvMan.cs 605-612), so the look of a dungeon comes from realtime point and spot
    lights, not from the sun (Part B step 7).
  - Items with `m_aiInDungeonOnly` work. `Attack.m_cantUseInDungeon` blocks some player attacks.
  - `RenderGroupSystem.LateUpdate` turns the Overworld group off and the Interior group on. In code, only `Heightmap`
    (terrain) and `ClutterSystem` (grass) use the Overworld group, so only terrain and grass disappear. Trees, rocks,
    buildings, ships and water surfaces about 3.3 km below stay rendered: the `main.unity` dump (bundle `17245031`)
    shows the Main Camera far clip at 20,000 m, and no game code changes `farClipPlane`. Any opening in a shell (open
    floor, chasm, window, open sky) shows the world below unless the dungeon's environment has dense fog. Shells must
    be closed, or use a foggy `EnvSetup` (authoring rule in section 4.3 Part B; probe P12).
- The AI navmesh only exists between y -500 and 5500. `Pathfinding` tiles are 32 m wide and 6000 m tall, centred at
  y 2500 (`Pathfinding.GetTilePos`, `BuildTile`). Above 5500, bosses cannot path.
- Vanilla interior geometry starts at about location y + 4750 (the `EnvZone` box; `DungeonGenerator` zone boxes reach
  about 128 m below their generator), so roughly 4700 m and up for ground-level locations.
- Venture Location Reset (very popular, MIT) deletes and regenerates root objects at **y >= 4000** around "sky
  locations" (`LocationReset.cs` `LOCATION_MINIMUM = 4000f`).
- Dungeon Splitter and SkadiNet split network layers at y 1500.
- (Inference from the decompile, effect in game unverified.) `EnvMan` evaluates `IsDeepnorth(camera.x, camera.y)`. At a
  camera height of 3950 this is true only for |x| above about 8800, so entrances far east or west could see Deep North
  weather if no `EnvZone` covers the player. Our `EnvZone` covers the whole interior, which should make this moot;
  placement only warns about it until probe P9 confirms either way (section 7.3).

**Streaming, ownership and spawning ignore height.**

- `ZNetScene.PointInsideActiveArea` sets `y = 0`. `ZoneSystem.GetZone` uses x and z. `ZDOMan.FindSectorObjects` and
  `ReleaseNearbyZDOS` work on 64 m zones. So:
  - A networked root (and its non-networked children) stays loaded at every graphics setting only within 96 m of its
    zone centre (224 m if `m_distant`). Higher settings give more (`SimulationDistance` 0 to 6, default 2, capped by the
    server). Big dungeons need **one root per zone**.
  - Ownership goes to the first peer, server first then peers in list order, whose active area (Chebyshev <= 1.5
    zones, 1 zone at level 0) contains the object. It is not the nearest peer. AI runs only on the owner
    (`BaseAI.UpdateAI`), so a creature is guaranteed to be simulated only within about 64 m of a player (32 m if the
    server runs level 0).
  - `SpawnSystem` counts players by XZ. Players inside cause the surface biome's spawns 40 to 80 m away, on the terrain
    below. Vanilla dungeons do exactly the same.
  - Ownership is not limited to players inside or straight above: any peer whose active area covers the boss's zone
    can own it, including a player on the surface one or more zones away (1.5 zones Chebyshev at the default level,
    more at higher `SimulationDistance` settings; `ZDOMan.ReleaseNearbyZDOS` with `peer.m_refPos`).
  - Player-count damage scaling (`Game.GetPlayerDifficulty`, `Player.GetPlayersInRangeXZ`, 100 m, code default 0.3 per
    extra player, cap 5; prefab values unverified) counts surface players under the arena.
- The ZDO sector grid only covers zones -256..255, so x and z must stay in [-16416, 16352)
  (`ZoneSystem.SectorToIndex`). Beyond that everything shares sector 0.

**Persistence.**

- Only persistent ZDOs are saved. In 1.0 the save is per dirty chunk, and a new ZDO only marks its chunk dirty if
  `Persistent` is set **before** later writes (`ZDO.Initialize`, `ZDOMan.SetDirtySector`). So create persistent ZDOs in
  the order `ZNetView.Awake` uses: Persistent, Type, Distant, then `SetPrefab`, `SetRotation`, then the keys.
- Non-persistent ZDOs owned by the local game are destroyed by `ZNetScene.RemoveObjects` as soon as they leave the local
  area. On a host or in single player this deletes server-created non-persistent objects for everyone when the host
  walks away. Anchors must therefore be **persistent**.
- Saved rotations are truncated to 0.5 degree steps (`ZPackage.WriteSmallRotation`), and tilts below 1 degree are lost.
  This only matters for networked pieces; static geometry built from data is exact.

**Edge cases and the v1 answer to each (vanilla-consistent unless noted).**

| Case | Vanilla behaviour | v1 behaviour |
|---|---|---|
| Logout inside | `PlayerProfile.SaveLogoutPoint` stores the position; `Game.FindSpawnPoint` only raises it to the terrain hit from y 6000. If the geometry is gone, the player falls (player fall damage is capped at 100 HP before modifiers, `Character.UpdateGroundContact`) | `[AlwaysOnPatch]` postfix on `SaveLogoutPoint` stores the entrance's exit point when the player is inside an instance. Resuming inside is a later option (D8) |
| Death inside | Tombstone at the death point (`Player.CreateTombStone`); `TombStone.PositionCheck` snaps it back to `s_spawnPoint` if it moves more than 4 m in XZ | Tombstones stay where they are. A reset **moves** them (and `ZDOVars.s_spawnPoint`) to the entrance and never deletes them |
| Portals | `TeleportWorld` pieces cannot be built inside (`NotInDungeon`), assuming the portal pieces keep `m_allowedInDungeons = false` (prefab value, unverified). Under `NoBossPortals`, every portal in the world is blocked while `activeBosses > 0` or while the local `EnemyHud` shows a boss with a `m_bossEvent` (`TeleportWorld.Teleport`, TeleportWorld.cs 130). Under `NoBossPortals`, a dungeon `Teleport` door also refuses to leave an interior while the active boss stands in the player's zone (`Teleport.Interact`, `Location.IsInsideActiveBossDungeon`, which compares zones only) | Keep. Our wipe, reset **and purge** must decrement `activeBosses` (sections 3.6, 7.2). Our exit applies the `NoBossPortals` rule itself, using the dungeon footprint instead of one zone (section 3.5) |
| Building | Blocked inside unless `DungeonBuild` or `m_allowedInDungeons` | Keep (no building inside) |
| Tames | Nothing follows a teleport (`Character.TeleportTo` returns false for non-players; `MonsterAI.Follow` has no teleport) | Keep |
| Carts | `Vagon.CanAttach` refuses while teleporting | Keep |
| Ore and metal | `Teleport` ignores `IsTeleportable` | Keep (vanilla-consistent); a per-dungeon flag can forbid it |
| Map | `Minimap` uses XZ: inside, you appear above the entrance | Keep; add an entrance pin once discovered |
| Biome | `Player.UpdateBiome` reports the biome below | Keep |
| Surface spawns under an occupied dungeon | Yes (`SpawnSystem`, XZ only) | Keep (vanilla dungeons do it too); optional fix later |
| Surface players owning the boss | Possible (XZ ownership; any peer whose active area covers the zone, possibly zones away) | Bosses are spawned by the game of the engaged player inside whom the server granted the engage, so that player owns them from the start (sections 3.8, 6.5). After a handover a surface player can still own them; the state carries over |
| Falling out of the dungeon | Players take fall damage, capped at 100 HP before modifiers. Creatures take none: only players get fall damage (`Character.UpdateGroundContact`, `IsPlayer() && num > 4f`). `ZSyncTransform` only rescues objects below y -5000. A boss or add that drops through a gap, is pushed out, or is created over a floor that is not built yet lands about 3.3 km below and roams the overworld alive | **Catch zone** under the whole footprint at origin -320 (y 3180): each game checks the objects it owns every 0.5 s; players go to the entry marker, creatures go back to their spawn point or marker. **Boss leash** in `BossAI`: if y < 3000 or the boss is outside its arena, teleport it back. Floors stay inside their own cell (section 2.4). Self-test `dungeon.fall-catch` |
| A vanilla location in the same zone | Its interior (location y + about 4750 to 5250, `Location.Awake`) shares the zone and the XZ | Footprints are refused when a zone holds a location with `m_hasInterior` (section 2.4). "Inside" is a height band, not y > 3000 (section 3.3) |

### 2.4 Recommended coordinate scheme (v1)

| Quantity | Value | Reason |
|---|---|---|
| Column centre (X, Z) | The centre of the entrance's zone: `X = 64 * floor((x + 32) / 64)`, same for Z | `ZoneSystem.GetZone`; aligns the dungeon to the zone grid, so each 64 m cell is exactly one zone |
| Dungeon orientation | Always axis-aligned (not rotated with the entrance) | Cells stay zone-aligned; the `EnvZone` box stays axis-aligned |
| Dungeon origin Y (entry floor) | **3500** | Interior (> 3000), well inside the navmesh, below 4000 |
| Geometry band | **3200 <= y <= 3950** (origin -300 to +450) | Above 3000 (interior). Below 4000 (Venture Location Reset). Far below 5500 (navmesh) and below vanilla interiors (about 4700+). Above 1500 (layer filters keep it all on one layer) |
| Footprint | 1 zone (64 x 64 m) by default; v1 allows up to 3 x 3 zones (cells -1..1, so x and z in [-96, 96) around the column) | The engine limit is **one anchor per zone**, not 3 x 3: any number of cells works the same way, each with its own anchor. The 3 x 3 cap is a v1 simplicity choice (overlap checks, reset scans). Only the arrival zone is guaranteed built on arrival: `ZNetScene.IsAreaReady` checks the loading flag of the target zone only, and only requires the ZDOs of the 8 neighbours to be instantiated, not built |
| Shell piece size | Each shell prefab's pivot inside its cell; its bounds no more than 32 m past the cell edge. **Floors that players or creatures stand on stay inside their own cell** | The geometry around the player is always in the near ring (96 m at level 0). `ZoneSystem.SetLoadingInZone` only holds back lower-type objects in the anchor's own zone (`IsZoneReadyForType`), so a creature in zone A standing on a floor built by anchor B could be created and simulated while B is still loading. The alternative is that every anchor builds synchronously in `Awake` |
| Catch zone | A box under the whole footprint, from origin -320 (y 3180) down to y 3000 | Falls (section 2.3 table). Still above 3000, so still "interior" for vanilla |
| Boss arena | At most about 100 m across; keep players within about 60 m of the bosses | Ownership and simulation guarantee (section 2.3) |
| Placement checks | Refuse overlap with another instance footprint. **Refuse** a footprint whose zones hold a location with `m_hasInterior`. Warn only for exterior-only locations | A vanilla interior in the same zone breaks three things. (1) Presence, the logout patch and lockouts would count its visitors as inside our dungeon if "inside" were y > 3000. (2) `Location.IsInsideActiveBossDungeon` compares zones only (`ZoneSystem.GetZone`), so with `NoBossPortals` on, our boss with a `m_bossEvent` would lock that vanilla dungeon's `Teleport` doors. (3) `Location.GetLocation` for interior points returns the zone's location, whose `m_enemyMin/MaxLevelOverride` and `m_blockSpawnGroups` would apply to our spawners (`CreatureSpawner.UpdateSpawner`). Exterior-only locations still cause (3), hence the warning |
| Inside test | 3000 < y <= 4000 **and** XZ inside a footprint | Used by presence, the logout patch, lockouts and evacuation. Covers the band (3200 to 3950), a 50 m margin above and the catch zone below, and excludes vanilla interiors (about 4750 and up) |
| World range | Entrances anywhere inside the playable world (radius < 10000) | Well inside the sector grid |

**Per-party slots (M3, later)** reuse the same layout, but the column moves to a reserved far region. The streaming
report computed a grid in the northern crescent beyond the world edge, the only area where the biome grid and
`WorldGenerator.GetBiome` agree. Elsewhere beyond the edge, `Player.UpdateBiome` logs "GetBiome error" every second.

- Centres at `(1024 * i, 12288 + 1024 * j)` for j = 0..3 and |i| <= 7, 6, 5, 2 per row: 44 slots, independently
  recomputed by the verifier.
- Content within +-185 m of each centre (recommended +-160 m), isolated up to `SimulationDistance` 6. The worst-case load
  reach is about 654 m.

Each slot costs extra work, which is why it is a later phase:

- Water: every `WaterVolume` with `m_forceDepth < 0` (the default, WaterVolume.cs 18) has its surface lowered by 100 m
  beyond radius 10500 (`WaterVolume.GetWaterSurface`, WaterVolume.cs 178-181), so pools in a far slot would hold no
  water. **Authoring rule from v1 on** (so layouts stay portable to slots): dungeon water uses `m_forceDepth >= 0` and
  no `m_heightmap`; Validate and the catalog check it.
- Falling and swimming: `Player.EdgeOfWorldKill` (Player.cs 1721-1740) pushes a player outward beyond 10420 m when
  swimming or below y 30, and kills below y -10. A swimming player in a slot pool would be pushed, so the slot
  abstraction includes a guard that skips the push for players inside a slot (y > 3000).
- Environment: a slot needs a **forced** `EnvZone`, because nothing else sets a sensible environment that far out.
- Teleport: the far teleport needs `distantTeleport: true` (at least 8 s) and the server pushing the slot's anchors to
  the party first (`ZDOMan.ForceSendZDO`).
- Ownership: content created by the server stays server-owned and frozen until `ReleaseNearbyZDOS` hands it to a party
  member.
- Relog: a relog into a reset slot must be redirected (`Game.FindSpawnPoint`).
- Map: the minimap texture ends at +-12288 m.
- Zones: the first use of a slot makes the server generate every ungenerated zone within the total simulation distance
  of each player there (`ZoneSystem.CreateGhostZones`, ZoneSystem.cs 1222-1242, `SpawnMode.Ghost`), and those zones go
  into the save. That is 9 x 9 = 81 zones at a total distance of 4 in classic mode, fewer with the radius check; the
  exact count depends on the server's simulation settings (unverified for the defaults).
- Other mods: world-size mods (Expand World Size 1.42.0) move the edge.

The forced `EnvZone`, the edge guard and the water rule are part of the slot abstraction, so a layout authored for M2
moves to M3 unchanged.

---

## 3. Architecture

### 3.1 Component map

| Component | Runs on | Main game hooks |
|---|---|---|
| **Content registry** (generic prefabs, boss clones, attack items) | Every peer, always on | `[AlwaysOnPatch]` postfixes on `ZNetScene.Awake` (`m_prefabs`, `m_namedPrefabs`), `ObjectDB.Awake` and `ObjectDB.CopyOtherDB` (`m_items`, `m_itemByHash`), following Sneak.Ambush's `SmokeContent` |
| **Catalog** (dungeon data folders) | Every peer | Plugin `BindConfig` only reads the JSON from `<ModFolder>/Dungeons`, `BepInEx/config/<GUID>/Dungeons` and the Debug `DataFolder` and computes a content hash per dungeon (section 5). Name validation needs `ZNetScene` and `ObjectDB`, which do not exist at plugin `Awake`, so it runs later, in the always-on `ZNetScene.Awake` and `ObjectDB` postfixes |
| **Asset resolver** | Clients | Both guards Jotunn uses: `Runtime.MakeAllAssetsLoadable()` from `BindConfig`, and an `[AlwaysOnPatch]` prefix on `AssetBundleLoader.InitializeDataSide` that forces `allAssetsLoadable = true` (Jotunn `AssetManager.cs`, line 78). `MakeAllAssetsLoadable` only logs "must be called before loading any assets via SoftRef!" and does nothing once `Runtime.s_assetLoader` exists. **Hard rule: no SoftRef getter or load before the vanilla loader exists.** The internal `Runtime.Loader` getter creates the `AssetBundleLoader` on first use (SoftReferenceableAssets/Runtime.cs 18-30), and `GetAllAssetPathsInBundleMappedToAssetID`, `GetAssetPath` and every `SoftReference.Load` go through it. Once it exists, `Runtime.AddManifest` only logs "AddManifest() must be called before loading any assets via SoftRef!" and returns, so an early call from our plugin would silently refuse the manifest of More World Locations AIO, which adds it from an `EntryPointSceneLoader.Start` prefix (MWL `AssetBundles.cs` 24-31). So the startup check (resolve one AssetID that exists only in `manifest_extended`) runs from an `[AlwaysOnPatch]` postfix on `FejdStartup.Awake`, which runs after `EntryPointSceneLoader` has loaded the first scene through SoftRef, and caches its result. `LocalBlocker` only returns the cached flag (null until the check has run); on failure it logs a clear line and turns the feature off. Then `Runtime.GetAllAssetPathsInBundleMappedToAssetID`; `SoftReference<T>`; `SoftReferenceableAssets.Utils.Instantiate`; `AssetBundle.LoadFromFile` |
| **Entrance** (`MC_DI_Entrance`, networked, persistent) | Every peer | `Interactable` + `Hoverable` on the root; a non-trigger hover collider (section 3.2); visual built locally from the layout's `entrance` block; hover state read from a server-written key; reads the plugin's `FeatureActive` flag |
| **Anchor** (`MC_DI_Anchor`, networked, persistent, ZDO type Terrain, not distant) | Every peer instantiates it; nobody writes to it after creation | Its `Awake` builds that zone's slice of the shell under a **separate non-networked root** that is not its child, tracks that root and destroys it in `OnDestroy` (section 3.4). `ZoneSystem.SetLoadingInZone` / `UnsetLoadingInZone` while walkable floors load asynchronously, as `DungeonGenerator.LoadRoomPrefabsAsync` does, with the release rules of section 3.4. Adds `EnvZone`, exits, music and the catch zone, all trigger volumes on layer 14 `character_trigger` |
| **Instance manager + registry** (`MC_DI_Registry`) | Server | `ZNet.Update` postfix heartbeat (DeepNorth `ServerWorld` pattern); `ZDOMan.CreateNewZDO`, `DestroyZDO`, `FindSectorObjects`; `ZDOMan.m_onZDODestroyed`; registry state in one versioned `ZPackage` byte array on one server-written ZDO |
| **Exit and return** | Clients (local player) | Local exit component in the shell (trigger + interact, like `Teleport`); `Player.TeleportTo(..., distantTeleport: false)`; `[AlwaysOnPatch]` postfix on `PlayerProfile.SaveLogoutPoint`; server evacuation through the vanilla routed RPC `RPC_TeleportPlayer` (`Chat.TeleportPlayer`) |
| **Reset and lockouts** | Server | `FindSectorObjects` over the footprint; `SetOwner(ZDOMan.GetSessionID())` + `DestroyZDO`; tombstone moves (`SetOwner` to the server first, then `SetPosition` + `ZDOVars.s_spawnPoint`); `activeBosses` decrement; layout-hash check; lockouts in the registry and player unique keys |
| **Environment, music, minimap, no-build** | Clients | `EnvZone` sized to the footprint; `EnvMan.AppendEnvironment` for custom `EnvSetup`s; `MusicVolume` / `MusicLocation`; `Minimap` pin; vanilla `NotInDungeon` rule |
| **Encounter controller** (`MC_DI_Controller`, networked, persistent, per arena) | Data on its ZDO, written only by its current owner. Arena logic runs on that owner (the engaged player the server granted the engage claims it); run results go to the server by routed RPC | `<GUID>.EngageRequest` / `<GUID>.EngageGrant` to and from the server (one grant per attempt id); routed RPCs for presentation and for requests to the owner (`ZNetView.InvokeRPC` routes to the ZDO owner); `Door` state `s_state` after the granted peer claims the door; local barrier colliders toggled from the ZDO; bosses spawned on the granted peer's game |
| **Boss runtime** | Boss ZDO owner | `BossAI : MonsterAI` (override of `UpdateAI`); a `BaseAI.CanUseAttack` postfix, or per-boss inventory swaps, for phase gating; `Humanoid.EquipItem` + `Character.StartAttack`; `Attack.StartWithoutAnimation`; `Character.RPC_Damage` prefix for shields; `EnemyHud.UpdateHuds` postfix to stack boss bars |
| **Server rules and join check** | Server + clients | Copy of Swimming.Dive `PlayerCheck`, `ServerRules`, `Patches/ZNetPatches.cs` (`NetworkGate.PeerCompatible`); rules also carry one content hash per dungeon |
| **Admin command** | Client to server | `Terminal.ConsoleCommand` built in `OnActivated`; own routed RPCs `<GUID>.Admin` / `<GUID>.AdminReply`; admin check as in `ZNet.RPC_RemoteCommand` (`ZNet.IsAdmin` on the socket host name) |

### 3.2 Entrance prefab and interaction

- `MC_DI_Entrance` is one generic registered prefab: a `ZNetView` (persistent, type Default) plus our `Entrance`
  component (`Hoverable` + `Interactable`) on the root. Its ZDO stores `<GUID>.Placement` (placement id) and
  `<GUID>.Dungeon` (dungeon id). The visual (arch, door, cave mouth) comes from the dungeon's layout `entrance` block
  and is built locally. The entrance itself adds no per-dungeon prefab name (boss and item clones do: section 8).
- **Colliders.** The player's hover raycast (`Player.FindHoverObject`) only uses `Player.m_interactMask`: item, piece,
  piece_nonsolid, Default, static_solid, Default_small, character, character_net, terrain, vehicle, character_ghost
  (`Player.Awake`, Player.cs line 671). It stops at the first hit. Layer 14 `character_trigger` is not in that mask, so
  a collider on layer 14 alone can never be hovered or used with E. Vanilla gateways pair a trigger box on the
  `Teleport` object with a child non-trigger `Cube` collider (dumps of `MountainCave02` and `DN_Bossroom`); `Interact`
  finds the `Interactable` with `GetComponentInParent`. The entrance therefore gets:
  - a **non-trigger hover collider** on Default, piece or static_solid, as a child of the `Entrance` root;
  - optionally a **walk-in trigger** on layer 14, which still fires `OnTriggerEnter` for players, as vanilla gateways do.
- **Hover text**: the dungeon name plus its state (Open / Run in progress / Sealed: fight in progress / Resets in 12 min
  / Locked for you until ... / Disabled on this server). Data path: the server is the **only writer** of the entrance
  ZDO: the placement keys, the position (also when it snaps to the ground, section 7.1) and one state key,
  `<GUID>.State` (state, reset time, party lock). The entrance has no component that writes to its own ZDO, and clients
  only send requests or reports by RPC, so this single writer is safe (section 3.3). Per-player lockouts are pushed to
  each client by a routed RPC (`<GUID>.Lockouts`) at join and on change.
- Interact:
  1. The client sends `<GUID>.Enter(placementId)` to the server.
  2. The server checks the feature, the content hash, the state, the lock, the lockout and the player count. It
     answers with `<GUID>.EnterReply(ok, reason, targetPos, targetRot, ticket)` and records a **provisional** entry
     (ticket, player, time). It does not change the run state yet.
  3. The client checks that it is not teleporting and that the 2 s teleport cooldown has passed. It caches the
     footprint and the exit point for the logout patch, then calls `Player.TeleportTo(target, rot, distantTeleport:
     false)`. If that returns false (a teleport is running, or within the 2 s cooldown, `Player.TeleportTo`), it tells
     the server `<GUID>.EnterCancel(ticket)`.
  4. When the player has arrived (inside test true, `!IsTeleporting()`), the client sends `<GUID>.Arrived(ticket)`.
     Only then does it consume the key item, if the dungeon needs one (inventories are client-owned), and only then
     does the server commit the run state (lock, Running). A bounce (`$msg_portal_blocked`, `Player.UpdateTeleport`
     puts the player back at `m_teleportFromPos`) or an `EnterCancel` rolls the provisional entry back at once. With
     neither `Arrived` nor `EnterCancel`, the provisional entry expires after 30 s, which is longer than the 2 s teleport
     delay plus the 20 s anchor-hold timeout (section 3.4), so a slow but healthy entry is not rolled back while the
     teleport is still pending.
  5. **Late `Arrived`** (after a rollback or expiry): the server re-validates the entry as if it were new (feature,
     content hash, state, lock, lockout, player count). If it still passes, it commits it. If not, it evacuates the
     player to the exit point (`RPC_TeleportPlayer`, section 3.5) with a center message giving the reason, and the
     key item is not consumed. Presence (section 3.3) also catches a player who is inside without any committed entry
     and applies the same rule.
- The teleport moves the player after 2 s, with a black fade (`Hud.UpdateBlackScreen`). It then waits for
  `ZNetScene.IsAreaReady`: the zone loaded, nothing in it marked loading (`ZoneSystem.IsZoneLoaded` is false while the
  zone is in `m_loadingObjectsInZones`, ZoneSystem.cs 1296-1303), and every ZDO in the 3 x 3 zones instantiated
  (ZNetScene.cs 156-174). **A non-distant teleport waits for this with no timeout** (`Player.UpdateTeleport`,
  Player.cs 5935: the 15 s limit only applies after `IsAreaReady` is true), so a zone hold that is never released
  leaves the player on a black screen. Then `ZoneSystem.FindFloor` casts 1000 m down from the target + 1 m on
  `m_solidRayMask` (Default, static_solid, Default_small, piece, terrain). With no floor, vanilla returns the player
  with `$msg_portal_blocked`. Above the entrance's zone, the anchors are in the player's near ring before they enter,
  so the geometry usually already exists.

### 3.3 Server instance manager and registry

- **Placement state** per placement id (one model, used everywhere in this document):
  - **Placed**: the entrance and the anchors exist (both are created together, section 7.1); the layout's networked
    objects are not created yet.
  - **Ready**: networked objects created, nobody inside.
  - **Running**: players inside or recently inside (committed on `Arrived`, section 3.2).
  - **Cleared**: all bosses dead, loot chest spawned.
  - **Resetting**: the server is moving tombstones and re-creating objects; then back to Ready.

  Placed goes to Ready at the first entry request. Ready, Running and Cleared go to Resetting by the timers in 3.6.
- **Registry**: one persistent ZDO of the registered prefab `MC_DI_Registry`, written only by the server. It sits at a
  fixed spot inside the sector grid that no player ever reaches (for example x 0, z -15040). It holds one versioned
  `ZPackage` byte array:
  - instances: placement, dungeon, state, run start, last occupied;
  - reset times on `ZNet.GetTimeSeconds()`;
  - lockouts;
  - boss kill records;
  - the ZDOIDs the run spawned;
  - per placement, the **layout hash** its networked objects were created from (section 3.6).

  A self-test plus a save/reload must confirm that it stays server-owned and saves correctly (unverified). Fallback: a
  sidecar file keyed by `ZNet.GetWorldUID()`.
- **Presence**: the server knows who is inside from each peer's character ZDO position. This is better than
  `ZNetPeer.m_refPos`, which updates only every 2 s; Dungeon Splitter 1.10 made the same switch. A peer counts as
  inside when the inside test of section 2.4 passes: 3000 < y <= 4000 and XZ inside a footprint. The logout patch,
  lockouts and evacuation use the same test.
- **One writer per ZDO.** Valheim's sync loses concurrent writes: `ZDOMan.RPC_ZDOData` (ZDOMan.cs 1142-1200) replaces
  the whole ZDO, owner included, only when the incoming `DataRevision` is higher, so with two writers one side's keys
  are silently lost. **Ownership claims race too.** `ZNetView.ClaimOwnership` calls `ZDO.SetOwner`, which only
  increments `OwnerRevision` (ZDO.cs 1373-1380), and a received owner is taken only when its `OwnerRevision` is
  strictly greater (`num3 > zDO.OwnerRevision`) or when it comes with newer data. Two peers that claim the same object
  within one sync interval both see `IsOwner()` true until sync settles it, and equal-revision claims do not settle
  reliably. So no gameplay decision (spawning bosses, sealing) may rest on "I claimed it"; it rests on a server grant
  (section 3.8). And the server cannot
  keep a persistent ZDO near a player: `ZDOMan.ReleaseNearbyZDOS` gives an object to a peer whose area contains it
  when its current owner's area does not, and `IsInPeerActiveArea` tests the server against its own reference position
  (far away on a dedicated server), so a peer takes it within about 2 s. Rules:

  | ZDO | Only writer | How others change it |
  |---|---|---|
  | Registry (`MC_DI_Registry`, far from every player) | Server | Clients send requests by routed RPC to the server; the server pushes results by routed RPC |
  | Entrance | Server (`<GUID>.State`, placement keys, position including the ground snap) | Read only. A client that measures the ground sends `<GUID>.SnapReport(placementId, y)`; only the server writes the position (section 7.1) |
  | Anchor | Nobody after creation | - (The `MC_DI_Anchor` prefab's `ZNetView` must have `m_persistent = true`, `m_type = Terrain` and `m_distant = false`, exactly what the server writes. Otherwise `ZNetView.Awake` on the owner rewrites the type or distant flag (ZNetView.cs 59-66), which is a write to the anchor. The same holds for the entrance, controller and registry prefabs.) |
  | Encounter controller | Its current owner (claimed at engage by the one peer the server granted, then vanilla handover) | Requests go to the owner with `ZNetView.InvokeRPC`, which routes to `m_zdo.GetOwner()`. Engage goes through the server first (section 3.8). Run results go from the owner to the server by routed RPC. When no peer is near (nobody owns it in practice), the server claims it with `SetOwner` to act on it |
  | Bosses and adds | Their owner (vanilla) | Damage through vanilla `RPC_Damage`; scripted requests by RPC to the owner |
  | Doors used as seals | The granted peer, for the current attempt | Only the peer the server granted for this attempt calls `ClaimOwnership` on the door, then writes `s_state`. (Door's own `UseDoor` RPC toggles the state, so it is racy for a seal.) |
  | Tombstones and items moved at reset | Server, after `SetOwner(server session)` | `ZSyncTransform.OwnerSync` only reads the ZDO position when it **newly** gains ownership, so the server takes ownership first, then writes `SetPosition` and `s_spawnPoint`; the next owner reads the new position. On a host or in single player, where the mover may already own and have instantiated the object, also move the local instance's transform (`ZNetScene.FindInstance`) |

  Probe P7 tests two clients writing at once.
- **Plan file**: the per-world placements JSON (section 5.5). It is reconciled at world load and on change: for each
  entry, an entrance ZDO with that placement id must exist. Entrances lost to a host without the mod or to zone-reset
  tools come back. Orphan entrances are reported, never silently deleted.

### 3.4 Layout loader and spawner

**Static shell** (per client, never saved). On `Awake`, each anchor builds every shell entry whose pivot falls in its
zone.

**The shell never hangs under the anchor.** Many vanilla components that have no `ZNetView` of their own look one up
with `GetComponentInParent<ZNetView>()` and would take over the anchor's: `OfferingBowl` (OfferingBowl.cs 92),
`MaterialVariation` (MaterialVariation.cs 35: in `Awake` it registers `RPC_UpdateMaterial`, and its owner writes
`MatVar<i>`), `RandomMaterialValues` (RandomMaterialValues.cs 55: in `Start` its owner writes `RandMatSeed`), `SpawnArea`,
`CinderSpawner`, `WispSpawner`, `SpawnPrefab`, `Smelter`, `Radiator`, `ArcheryTarget`, `ObjectSwitcher` (with
`m_syncAndSave`), `LineConnect`, `StaticRotation`, `Aoe`, `ShieldGenerator` and `ZSFX` (read only), found by a grep of
`.ref`. `ZNetView.Register` uses `Dictionary.Add` (ZNetView.cs 273-306), so the second borrower of the same type under
one anchor throws in its `Awake`, and all borrowers share the same ZDO keys (one `MatVar0` for every rock). The vanilla
interiors verification saw the same thing in `DN_Bossroom`, where `OfferingBowl` borrows the `LocationProxy`'s
`ZNetView`. So:

- The anchor builds into a **separate root GameObject with no `ZNetView`**, not parented to the anchor. The anchor
  keeps a reference to it and destroys it in `OnDestroy`. Nothing in the shell can find a `ZNetView` by parent search.
- Every type above is on the runtime strip list. Stripping happens while the new instance is still inactive (it is
  built under an inactive holder and activated afterwards), because some of them throw without a `ZNetView`: for
  example `MaterialVariation.Awake` calls `m_nview.Register` on a null `m_nview` (MaterialVariation.cs 35-45).
  Visual-only ones (`MaterialVariation`, `RandomMaterialValues`, `StaticRotation`) can later get a local replacement
  seeded from the entry's `seed`, so vanilla rooms keep their look variations (unverified effort).
- Anything that really needs RPCs or saved state, such as an altar `OfferingBowl`, becomes a networked layout object
  with its own `ZNetView` (`objects`, below).
- Validate and `dungeon info` list these types per room, kit piece and bundle prefab.

Entry types:

- **Bundle prefabs** from our asset bundle. The resolver replaces `JVLmock_<name>` children with
  `SoftReferenceableAssets.Utils.Instantiate(new SoftReference<GameObject>(id), ...)`. The reference is counted per
  instance and released when the anchor's geometry is destroyed. Mocked meshes, materials, textures and shaders are
  swapped in. A mock that names a networked prefab follows the same rule as the `kit` type below.
- **SoftRef kit prefabs by AssetID** (for example `Assets/world/Props/CastleBuildingKit/SunkenKit_int_wall_1x4.prefab`
  = `cdd872779cf004a1cbb8edbbe3f85321`). Only **pure-geometry kit bundles such as `5b5d26ec`** are safe as-is: the
  verifier decoded that bundle and found no `MonoBehaviour` at all, every object on layer 15 `static_solid`. Many kit
  pieces are networked `ZNetScene` prefabs (1.0.16 dump of bundle `c4210710`):
  - `CastleKit_groundtorch`: root `ZNetView` (persistent) plus `WearNTear`;
  - `MountainKit_wood_gate`: `ZNetView`, `Destructible`, a `Door` with key fields, `DropOnDestroyed`;
  - `CastleKit_brazier`: `ZNetView` (type Solid), `Destructible`, `DropOnDestroyed`;
  - `Morkhalla_Floor_4x4`: `ZNetView`, layer 10 `piece`;
  - most `dvergrtown_*` pieces (`Destructible` or `WearNTear` plus `ZNetView`).

  A networked prefab instantiated this way on every client runs `ZNetView.Awake` with no `m_initZDO`, which creates a
  **new persistent ZDO on every client, every time the zone loads**: duplicate torches and gates and a growing world
  save. The runtime `kit` loader therefore checks the loaded prefab: if its hierarchy has a `ZNetView`, it either routes
  it to the stripped-decoration path (`prefab` type below, with `m_forceDisableInit`) or refuses it with a warning. The
  Unity Validate step labels each mock as geometry-only or networked from its components.
- **Vanilla dungeon rooms** by path (358 room prefabs under `Assets/world/Rooms/` in the base manifest). They are
  instantiated the way `DungeonGenerator.PlaceRoom` does in Client mode: `ZNetView` children set inactive on the source,
  `Utils.Instantiate`, then reactivated on the source. **This drops every networked child.** In vanilla, Full and Ghost
  modes instantiate those children as separate world objects and Client mode skips them (`DungeonGenerator.PlaceRoom`).
  So doors, gates, networked torches and braziers, spawners, chests and destructibles in a vanilla room disappear, and
  doorways stand open. Validate and `dungeon info` list the dropped networked children with their transforms, and can
  convert them into `objects` entries, which the author then keeps or deletes.
- **Vanilla `ZNetScene` prefabs used as decoration**: `ZNetView.m_forceDisableInit` and
  `TerrainOp.m_forceDisableTerrainOps` around `Instantiate`, as `Player.SetupPlacementGhost` does, then a strip list:
  `EffectArea` (PlayerBase, NoMonsters and Heat would otherwise block spawners and warm players), `Piece`, `Fireplace`,
  `Container`, `Door`, `Smelter`, `Rigidbody`, `TerrainModifier`, `WearNTear`, `Destructible`, plus every
  parent-`ZNetView` borrower listed above. `m_forceDisableInit` makes `ZNetView.Awake` destroy the piece's own
  `ZNetView` (ZNetView.cs 45-49); a component that looks its `ZNetView` up later, in `Start`, could then climb past it
  to whatever is above (timing unverified), which is one more reason the shell root has no `ZNetView` above it.
  Colliders keep their solid layers. Whether lights, LODs and worn/broken children look right after stripping is
  unverified per kit family; probe P4.
- Then the anchor adds the `EnvZone`, the exits, `MusicVolume`s, the catch zone and the `RenderGroupSubscriber`s.
  - **Every local trigger volume** (`EnvZone`, exit, arena, trigger, music, catch) goes on layer 14
    `character_trigger`. That layer is not in `Player.m_interactMask`, nor in `ZoneSystem.m_solidRayMask`, and still
    fires `OnTriggerEnter` with players, as vanilla gateways do. A large trigger box on Default or static_solid could
    be the first thing the hover raycast hits, and block hovering chests and doors inside, if
    `Physics.queriesHitTriggers` is on (project setting, unverified; vanilla raycasts such as `FindHoverObject` use the
    global default).
  - Adding the subscribers with group Interior, as `Heightmap` does with Overworld, is unverified as an API choice. A
    `RenderGroupSubscriber` drives one `MeshRenderer`, so hiding a shell from surface players needs one per renderer;
    probe P12.
- An anchor that loads walkable floors asynchronously holds its zone with `SetLoadingInZone` until those floors exist;
  floors stay inside the anchor's own cell (section 2.4).

**Zone hold: hard rules.** The entrance, its exit point (the logout point the patch writes, section 3.5) and the
arrival cell's anchor are all in the same zone. While an anchor holds that zone, `ZoneSystem.IsZoneLoaded` is false
(ZoneSystem.cs 1296-1303), so `ZNetScene.IsAreaReady` stays false (ZNetScene.cs 156-174), and vanilla waits for it with
no timeout in two places: a non-distant teleport (`Player.UpdateTeleport`, Player.cs 5935) and the login at the logout
point (`Game.FindSpawnPoint`, Game.cs 534-566). `ZoneSystem.IsZoneReadyForType` (ZoneSystem.cs 3033-3051) also holds
back every Default, Prioritized and Solid object in that zone (`ZNetScene.CreateObjectsSorted`). One hold that is never
released therefore means: a black screen for anyone entering, an endless loading screen for anyone logging in at the
exit point, hung portal trips into that zone, and missing surface objects in the entrance zone for every visitor.
Exceptions in the build, a missing bundle or AssetID, or the anchor being destroyed mid-load can all leave the hold set.
Vanilla releases its own hold in `LocationProxy.OnDestroy` (LocationProxy.cs 27-33) with a "held" field, and
`ZoneSystem.UnsetLoadingInZone` indexes `m_loadingObjectsInZones[sector]` directly (ZoneSystem.cs 3066-3074), so a
second release throws `KeyNotFoundException`. Rules:

1. One `held` field per anchor, as `LocationProxy.m_zdoSetToBeLoadingInZone`: set it when calling `SetLoadingInZone`,
   and release only through one method that checks and clears it, so the hold is never released twice.
2. Release in a `try/finally` around the build, and in the anchor's `OnDestroy`.
3. A hard timeout (20 s, setting `[Instances] AnchorHoldTimeoutSeconds`, section 8): when it fires, release the
   hold, log the dungeon, the cell and the asset that failed to load, and build only what has loaded.
4. Hold only for walkable floors, never for optional decoration, which loads after the release.
5. Self-test `dungeon.anchor-load-failure`: (a) a layout with a missing AssetID, (b) an anchor destroyed in the middle
   of an async build. After each, `IsAreaReady` at the entrance becomes true again within the timeout, entering does
   not hang, and a login at the exit point completes.

**Networked objects** (stateful, one ZDO each). The server creates them from the layout at the first Ready and at every
reset:

- Two creation paths, both usable on a dedicated server, both writing in the `ZNetView.Awake` order (Persistent, Type,
  Distant first, then `SetPrefab`, `SetRotation`, then the keys, so the 1.0 chunked save marks the chunk dirty):
  - **raw ZDO** (`ZDOMan.CreateNewZDO(pos, prefabHash)`; it does not set the prefab, so `SetPrefab` is required): only
    for prefabs with no owner-only setup in `Awake` (anchors, entrances, the registry, plain props);
  - **ghost init** (below): for everything else that is not a creature.
- **Owner-only `Awake` setup.** A raw ZDO created by the server is owned by the server when clients first instantiate
  it, and `Awake` never runs again after ownership moves to a peer, so every owner-only step in `Awake` is skipped:
  - `Character.Awake` runs `SetupMaxHealth` only when `IsOwner`; `GetMaxHealth` then falls back to the prefab's
    `m_health` (`ZDOVars.s_maxHealth` default), so a level-2 creature, or any creature on world level > 0, keeps
    unscaled max HP until someone reloads it as owner;
  - `Container.Awake`: `AddDefaultItems` (owner only, unless `s_addedDefaultItems`);
  - `Fireplace.Awake`: initial fuel `s_fuel = m_startFuel` (owner only), so a torch or brazier with a `Fireplace`
    starts unfuelled;
  - `Pickable.Awake`: writes `s_enabled` (owner only);
  - `Beehive.Awake`: `s_lastTime` (owner only).

  (List from a read of these `Awake` methods; `dungeon info` should list the components of every networked prefab a
  layout uses so the list can be extended.) Two fixes, used together:
  - **Creatures are never created as raw ZDOs.** Bosses and adds are spawned on the client the server granted the
    engage (section 3.8) with
    `Object.Instantiate` and then `Character.SetLevel` (which writes `s_level` and runs `SetupMaxHealth`), the vanilla
    pattern of `CreatureSpawner.Spawn` and `SpawnAbility`. Spawners stay `CreatureSpawner`s, which spawn on their owner.
  - **Other networked layout objects** are ghost-initialised by the server, as `ZoneSystem.SpawnLocation` does in Ghost
    mode: `ZNetView.StartGhostInit()`, `Object.Instantiate`, `ZNetView.FinishGhostInit()`, then write the override
    keys, then destroy the local ghost instance. `Awake` then runs once as owner and its ZDO writes are kept. Note that
    `ZNetView.Awake` returns before `LoadFields` in ghost mode, so overrides only take effect on the peers that later
    instantiate the object. For chests, the server may instead write the `items` byte array and `addedDefaultItems`
    itself.
- Per-object overrides go through vanilla `ZNetView.LoadFields`, which every peer applies, even unmodded ones. The keys
  are `HasFields`, `HasFields<Type>` and `<Type>.<field>`, for public int, float, bool, Vector3, string, GameObject and
  ItemDrop fields. This covers the spawner creature, levels and respawn, the door key, and `Piece.m_canBeRemoved`. It
  does not cover `DropTable`: chest contents go into the `items` byte array plus `addedDefaultItems`. `LoadFields` loops
  over the component's public fields and reads matching keys, so **a key that names no field is silently ignored**.
  The catalog checks every `fields` key against the component's public instance fields and their types at load, and
  the `dungeon.data` self-test repeats it, with a clear warning; a game update that renames a field then shows up at
  once instead of silently dropping the override.
- Every spawned ZDO gets `<GUID>.Inst` = the placement id, so a reset can find it. The mod never sets `cheated`, and the
  capture strips it.
- Unknown prefab names are skipped with a warning before any ZDO is created. A host destroys a ZDO whose prefab it does
  not know ("Destroyed invalid prefab ZDO", `ZNetScene.CreateObjectsSorted`).

Why this split: one anchor per zone costs 1 ZDO, while a networked piece costs about 90 B per full send and about
40 to 60 B in the save. Thousands of networked pieces per dungeon (the Expand World Data blueprint and Dungeon Creation
Kit style) are what Dungeon Splitter exists to work around.

### 3.5 Exit and return

- The exit is a local component built into the shell at the `exit` marker, shaped like a vanilla gateway: a walk-in
  trigger box on layer 14 plus a non-trigger hover collider on Default, piece or static_solid (section 3.2). It reads
  the entrance's exit point (in front of the entrance, above its floor) and calls a non-distant `TeleportTo`. It
  refuses:
  - while the player is inside a sealed arena;
  - **under `NoBossPortals`, as vanilla does** (decision in section 1): when
    `ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals)` is set and `EnemyHud.instance.GetActiveBoss()` returns
    a boss with a non-empty `m_bossEvent` that stands inside this dungeon's footprint, it shows `$msg_blockedbyboss`.
    Vanilla `Teleport.Interact` (Teleport.cs 42-46) applies this rule to its own doors only, and through
    `Location.IsInsideActiveBossDungeon`, which compares single zones (Location.cs 179-195); our exit is not a
    `Teleport`, and our footprint can span several zones, so the exit checks the footprint. Without this, our dungeons
    would be less strict than vanilla ones on such worlds. Test item: with `NoBossPortals` on and the boss alerted, the
    exit refuses; after the boss dies or the wipe, it works.
- Logout: the `[AlwaysOnPatch]` postfix on `PlayerProfile.SaveLogoutPoint` replaces the saved point with the cached
  exit point when the inside test passes (3000 < y <= 4000 and XZ in a footprint, section 2.4). It must be always-on:
  - A client that turns the mod off on a server that requires it is refused after 1 s (`PlayerCheck.GraceSeconds`).
    As soon as it reads the vanilla `Error` RPC, `Game.FixedUpdate` sees the connection status change and logs out
    (`Game.Logout`, `Shutdown`, `SavePlayerProfile(setLogoutPoint: true)`), about 1 s after the toggle; the 4 s
    `DisconnectDelay` only closes the socket. That logout save happens after its feature patches are gone.
  - `SaveLogoutPoint` is reached from every save that sets the logout point: `Game.Shutdown`, the autosave in
    `Game.UpdateSaving`, the menu logout (`Menu`), and `ZNet` saves including `RPC_SavePlayerProfile`. The postfix
    leaves the dead-player case alone (vanilla then saves the bed or home point).
  - `ModPlugin.OnDestroy` also calls `OnDeactivated` at quit.
- Evacuation (server turns the mod off, dungeon removed, purge): the server calls the vanilla routed RPC
  `RPC_TeleportPlayer` (registered in `Chat.Awake` on every client, even without the mod). That RPC calls
  `TeleportTo`, which silently fails during the 2 s cooldown, so the server must confirm by position and retry.
- Before uninstalling, the README tells admins to run `dungeon purge`, and says why: besides removing the mod's
  objects, purge lowers `GlobalKeys.activeBosses` for every counted boss it removes. Without purge, a boss that was
  alerted when the mod was removed keeps the counter raised forever (section 7.2), and on a `NoBossPortals` world every
  portal stays blocked.

### 3.6 Reset and lockouts

- **Encounter reset (wipe)**: no living engaged player inside the arena for `WipeSeconds` (default 30). The controller's
  owner detects it; if no peer is near any more, the server claims the controller (section 3.3) and runs the wipe.
  1. Destroy the tagged adds.
  2. Destroy the bosses (claim ownership first). Do not heal them in place: healing keeps kill credit for the wiped
     players and the `s_worldTimeHash` regeneration state. They are spawned fresh at the next engage (section 3.8).
  3. Decrement `GlobalKeys.activeBosses` for each boss whose `s_bossCount` was set. Only `Character.OnDeath` decrements
     it in vanilla (Character.cs 2994-2998).
  4. Reopen the seal, reset the controller ZDO to Idle, and tell the server the attempt is over, so it can grant the
     next engage (section 3.8).
- **Dungeon reset**: when nobody has been inside for `ResetAfterEmptyMinutes` (default 30), or `ReopenAfterClearMinutes`
  after a clear (default 120).
  1. Move tombstones and optionally dropped items to the entrance: `SetOwner` to the server first, then `SetPosition`
     and `ZDOVars.s_spawnPoint` (section 3.3).
  2. Destroy every ZDO tagged `<GUID>.Inst` = placement, plus untagged non-player characters inside the footprint.
  3. Re-create the layout's networked objects (ghost init, section 3.4) and store the current layout hash.
  4. Anchors stay: static geometry is rebuilt from data on every load anyway.
- **Layout updates.** A mod update that changes a dungeon's layout leaves the world's persisted networked objects
  (spawners, chests, doors at old positions, possibly inside new walls) in place until the next reset. The registry
  stores the layout hash each placement's objects were created from; on a mismatch at world load, the server forces a
  dungeon reset the next time that dungeon is empty, moving tombstones as usual, and logs it.
- **Lockouts** (optional, default off): per character in the registry, keyed by player id. Vanilla records boss kills
  per player with unique keys: `Character.OnDeath` queues `m_defeatSetGlobalKey` into `Player.m_addUniqueKeyQueue` on
  every peer that has the boss loaded. Lockouts per platform account are stricter but harder to show; see D7.

### 3.7 Environment, minimap and no-build rules

- **Environment**: one `EnvZone` sized to the footprint (not the vanilla 64 x 500 x 64 box).
  - Non-forced by default, as vanilla interiors are (the `InteriorEnvironmentZone` dump has `m_force=0`). **But the
    code default is forced**: `EnvZone.m_force = true` (EnvZone.cs line 7), so an `EnvZone` added with `AddComponent`
    is forced unless the builder sets it. The builder always sets `m_force` explicitly from `dungeon.json`
    (`environment.force`, default false).
  - A non-forced zone has the lowest priority in `EnvMan.GetEnvironmentOverride`. Before it come the debug and intro
    environments, the active `RandEventSystem` event (boss events), an `AltBiome` forced environment under the player's
    XZ, and a persistent event sphere.
  - A forced `EnvZone` (`m_force`) sets `EnvMan.m_forceEnv` and beats everything, boss music environments included.
  - **Teardown of a forced zone.** A forced zone clears `EnvMan.m_forceEnv` only in its `OnTriggerExit`
    (EnvZone.cs 38-53). If the shell is torn down while the local player is inside (`dungeon reload`, hot reload,
    purge, a bundle reload), the forced environment would stay stuck until the player enters and leaves another forced
    zone. So when the builder destroys a forced `EnvZone` that is the current `EnvZone.s_triggered`, or while the local
    player stands inside it, it calls `EnvMan.instance.SetForceEnvironment("")`. A non-forced zone needs nothing:
    `EnvZone.GetEnvironment` ignores a destroyed `s_triggered`. The reload self-test covers this case.
  - Vanilla interior environments to copy from (unverified list, not checked against a dump: Crypt, CryptHildir,
    SunkenCrypt, Caves, CavesHildir, InfectedMine, Morkhalla, Queen, DN_Bossroom, TheHollow). Only `Interior` is
    confirmed (the `InteriorEnvironmentZone` dump). `dungeon info` lists the real names at runtime from
    `EnvMan.instance.m_environments`, and the catalog checks every `copyOf` against that list.
  - A dungeon can add its own `EnvSetup` (cloned and tinted) through `EnvMan.AppendEnvironment`. A dungeon with any
    opening in its shell uses a foggy `EnvSetup` (section 2.3, render groups).
- **Boss music**: a custom `RandomEvent` per boss, appended to `RandEventSystem.m_events`, with `m_forceMusic` and
  `m_forceEnvironment`.
  - **`m_random = false`.** `RandomEvent` defaults to `m_random = true` and `m_nearBaseOnly = true` (RandomEvent.cs
    lines 14-18), and `RandEventSystem.GetPossibleRandomEvents` considers every event with `m_enabled && m_random`
    whose global keys match (RandEventSystem.cs line 417). With the defaults, our spawn-less boss event would fire as a
    random raid near any player base: start message, forced boss music and environment in the middle of a base. (Vanilla
    boss events are started by their bosses, not by the roll; their serialized `m_random` value was not dumped.)
  - Keep `m_enabled = true`. Keep `m_biome` = all biomes, because `RandomEvent.InEventBiome` reads
    `EnvMan.GetCurrentBiome()` from the XZ below the column (only the environment needs it; `m_forceMusic` has no biome
    check). Keep its spawn list empty: a forced event also applies its `m_spawn` (`RandEventSystem.GetForcedEvent`).
  - Register the events on every peer from an always-on hook (`RandEventSystem` exists on every peer and the boss's
    owner starts the event).
  - Self-test `dungeon.events-not-random`: none of our events ever appears in `GetPossibleRandomEvents`.
- Room music: `MusicVolume` (`MusicMan`).
- **Minimap**: the player shows above the entrance (vanilla-consistent). Add an entrance pin once discovered.
- **No-build**: keep the vanilla `NotInDungeon` rule. Admin authoring worlds use `setkey dungeonbuild`.
- Networked props get `WearNTear` immunity: ZDO `health = -1` plus `HasFields` `WearNTear.m_health = -1`, which
  ignores damage, wear and support. On worlds with a world level above 0, `WearNTear.Awake` scales `m_health` and the
  piece may show a worn look (inferred). They also get `m_canBeRemoved = false` and `Piece.m_randomTarget` /
  `m_primaryTarget = false`. Stripping `creator` alone does not stop monsters from targeting pieces, because
  `m_targetNonPlayerBuilt` defaults to true. `WearNTear.UpdateBiome` would otherwise apply the surface biome's ash or
  snow damage (it casts down from y + 5000).

### 3.8 Encounter controller

One persistent networked object per arena, at the arena marker. Its ZDO is the single source of truth for the arena,
and only its current owner writes it (section 3.3): `state` (Idle, Engaged, Phase n, Cleared, Resetting), `attempt`,
the boss ZDOIDs, `seal`, `phaseStartTime` (network time), and the add count. Per-boss flags live in each boss's own ZDO,
written by the boss's owner, not in the controller ZDO.

- **Engage is arbitrated by the server**, which is already the single writer of the registry. Letting the engaging
  player claim the controller and spawn would duplicate bosses: when two players step into the arena trigger within
  one sync interval (normal in group play), both claim the controller at the same `OwnerRevision`, both games see
  `IsOwner()` true, and both would seal and spawn the full boss set, count `activeBosses` twice and write the
  controller ZDO from two games (section 3.3, "Ownership claims race too").
  1. A player enters the arena volume (layer 14 trigger on that player's game). That game sends
     `<GUID>.EngageRequest(placementId, arenaId)` to the server.
  2. The server grants **exactly one peer per attempt**: if the arena's registry entry is Idle, it records a new
     attempt id and the granted peer, and answers that peer `<GUID>.EngageGrant(arenaId, attemptId)`. Every other
     request for the same arena while that attempt is open is answered "already engaged" (a no-op for the client). If
     the granted peer does not confirm within a few seconds (it left, died, or lost the race to load), the server
     closes the attempt and grants the next request.
  3. Only the granted peer calls `ClaimOwnership` on the controller and writes the attempt id into it. Before spawning,
     it re-checks that its controller copy is owned by it (`IsOwner()`) and that the stored attempt id equals the
     granted one; if not, it reports failure to the server and does nothing.
  4. It seals (below).
  5. It **spawns the bosses on its own game** at their markers (`Object.Instantiate`, then `Character.SetLevel`), so
     it owns them and their owner-only `Awake` setup runs (section 3.4). Bosses are spawned at engage, not left asleep
     in the arena, because a hunter boss (`m_enableHuntPlayer`) alerts itself on every owner tick and raises
     `activeBosses` early.
  6. It alerts the bosses and confirms "engaged, attempt n, boss ZDOIDs" to the server (routed RPC), which records it
     in the registry.

  The grant, not ownership, authorizes the spawn, so even if vanilla moves the controller to another peer later, no
  second boss set appears for the same attempt. The same grant covers pinning linked bosses to one owner and claiming
  the seal doors (sections 6.4, 6.5). Test item: two clients enter the arena trigger in the same frame; exactly one
  boss set spawns and `activeBosses` rises by the number of bosses, not twice that.
- **Seal**: the granted peer calls `ClaimOwnership` on each arena door, writes `s_state = 0`, and our `Door.Interact`
  prefix blocks it while engaged. Sealing needs a real `Door` component; `Morkhalla_GateDoor` has none (it is a
  `ZNetView` + `Destructible` barrier), so Validate checks that every `seal` entry has a `Door`.
  - **Seal doors must be indestructible.** Most door prefabs that can seal also carry a breakable component (Jotunn
    prefab list, generated from Valheim 1.0.7: https://valheim-modding.github.io/Jotunn/data/prefabs/prefab-list.html):
    `dungeon_forestcrypt_door`, `dungeon_sunkencrypt_irongate` and `iron_grate` have `WearNTear`;
    `MountainKit_wood_gate` has `Destructible`; only `dvergrtown_slidingdoor` has neither. Players, or boss AoE
    (`Aoe.m_hitProps` defaults to true, Aoe.cs 119), could break a seal and escape. So `indestructible` is mandatory
    for every `seal` entry, and Validate and the catalog refuse a seal without it. ZDO `health = -1` protects both
    kinds: `WearNTear.RPC_Damage` returns when the health is <= 0 (WearNTear.cs 1195-1197), and so does
    `Destructible.RPC_Damage` (Destructible.cs 106-110).
  - Every client also turns on local barrier walls from the controller state:
  - `static_solid` stops bodies and projectiles;
  - `blocker` stops bodies but not arrows or hits;
  - `pathblocker` only steers AI.

  The layers are confirmed from the collision matrix in `globalgamemanagers`.
- **Clear**: all bosses dead. The controller owner unseals, sets Cleared and reports to the server, which closes the
  attempt, sets the cleared flag, runs the arena's `onClear` actions, for example the optional world key
  `mc_di_<dungeon>_cleared`, and **spawns the loot chest** when `onClear` holds a `spawnChest` action (the only chest
  trigger: ghost init plus the `items` written from `loot.json`, section 5.6). Spawning the chest at clear needs no
  lock: vanilla `Container` has no lock field (only `m_privacy`, Container.cs), and a patch on `Container.Interact` and
  `GetHoverText` would be the alternative.
- **Presentation**: routed RPCs to every peer for center messages, telegraph VFX and local hazards. An `Aoe` with no
  `ZNetView` on itself **or any parent** only hits characters owned by the machine running it (`Aoe.ShouldHit`), so each
  player dodges on their own game. Set `m_hitProps = false` on such hazards, or props are hit once per peer. Each peer
  must also call the hazard's `Setup` with the boss as owner: with `m_owner` null the friend/enemy filter is skipped,
  and the hazard hits the boss and adds that peer owns. A networked `Aoe` with `m_useTriggers` is not owner-gated
  either (`Aoe.OnTriggerStay`); only the overlap path in `CustomFixedUpdate` runs on the owner alone.

### 3.9 Boss script runtime

Section 6 covers it in detail. In short:

- Bosses are clones of vanilla creatures with a `BossAI : MonsterAI` swapped in. Serialized fields are copied by
  reflection on the inactive clone before any `Awake`.
- Phase state and next-allowed times live in the boss ZDO, in network time.
- Attacks are gated by phase tags.
- Scripted actions run only on the owner. Ownership handovers lose nothing that matters.

---

## 4. Authoring pipeline

### 4.1 Options compared

| | U. Unity rip copy + mocked bundle (own resolver) | G. In-game build + capture to JSON | H. Hybrid (recommended) |
|---|---|---|---|
| How | Build in a Unity 6000.0.75f1 copy of the AssetRipper rip with real vanilla prefabs and your own meshes. Claude's editor tools replace every ripped object with a `JVLmock_` placeholder and build a bundle with no game files | Build in a creative world with vanilla pieces (hammer, Infinity Hammer, World Edit Commands). `dungeon capture` writes the layout JSON: static pieces become shell entries, stateful pieces become networked objects, `@` signs become markers | Unity for final hand-crafted shells and new assets; in-game capture for blockouts, quick dungeons and networked-object placement. One layout format |
| Reuse vanilla art | Everything in `manifest` + `manifest_extended` (22,473 assets: 6,519 prefabs, 2,166 materials, 3,236 models, 61 shaders) | Everything in `ZNetScene` (pieces, props, spawners, chests, doors) + vanilla rooms by name | Both |
| New or tweaked assets | Yes (Blender meshes, own textures on vanilla shaders, runtime tints) | Runtime tints and scale only | Yes |
| Exact layout control, lighting | Full | Limited (snap grid, 0.5 degree saved rotation for networked pieces; static ones are exact) | Full |
| Setup cost | About 1 day (install, rip, settings, MCP) + 4 to 7 days of Claude tooling (resolver 2 to 4, editor tools 2 to 3) | About 1 to 2 days of Claude tooling (capture, importer) | Sum, staged |
| Legal | Rip stays local; the build fails if any ripped asset is a dependency | Nothing ripped at all | Same as U |
| Iteration | Export + `dungeon reload` | Live in game | Both |

Two more options were rejected. Depending on Jotunn mocks (`ZoneManager` / `DungeonManager`, the warpalicious and RtD
pattern) adds a runtime dependency the repo does not use (section 8). Depending on Expand World Data makes every
object a networked piece: its blueprint rooms go through a procedural `DungeonGenerator`, and its blueprint locations
(a fixed hand-built layout, `docs/locations.md` line 13) avoid the generator but are still saved as one networked
object per piece, and are placed at world generation or with `genloc` / Upgrade World rather than as instances.

### 4.2 Recommended path

Use **H**:

1. **Blockout in game.** Build the floor plan fast with pieces, then `dungeon capture`. Claude converts the capture to
   layout JSON and, if you want, imports it into Unity as a scene, the way probablykory/location-tools imports PlanBuild
   blueprints. That code has no licence, so we study it only.
2. **Final shell in Unity**, using vanilla kit pieces and whole vanilla rooms, plus your Blender meshes.
3. **Networked objects, markers and encounters** live in the same scene as marker objects, or are hand-edited in JSON
   with Claude.

### 4.3 Authoring guide (target workflow)

Most of this guide describes the workflow once the tools exist. Each part says what it needs:

| Part | Needs | Milestone |
|---|---|---|
| A steps 1 to 4 (Unity, AssetRipper, rip, authoring copy) | Nothing | Today |
| A step 5 (MC Dungeon Kit) and all of B (New, Validate, Export) | The kit | 12.3 |
| A step 6 (MCP for Unity) | Your explicit OK; useful once the kit exists | 12.3 |
| C (new and tweaked assets) | Blender and Unity today; the runtime `tweaks` block and boss clones need the mod | 12.2 / 12.3 |
| D (test in game) and E (in-game path: `dungeon place`, `reload`, `capture`, `import`, `DataFolder`) | The mod | 12.2 (capture and import: 12.3) |

Order of work: probes P1 and P2 (12.1), then the MVP (12.2), then the kit (12.3), then your first dungeon.

**What you can do today**

1. Part A steps 1 to 4: install Unity 6000.0.75f1 and AssetRipper, rip the game, make the authoring copy.
2. Get familiar with the rip's kit folders: `Assets/world/Props/CastleBuildingKit`, `Caverocks`, `Dvergr`, `Morkhalla`,
   `CryptKit`, `DeepNorth_TimberHall`, and the rooms under `Assets/world/Rooms/`. Note which pieces you like. In the
   Inspector, a piece with a `ZNetView` component is networked (section 3.4): it can be used, but as a networked object
   or as stripped decoration, not as plain geometry.
3. Sketch the first dungeon on paper or on a grid, in metres, with +Z leading in from the entry point:
   - rooms and corridors, with floor heights;
   - the 64 m cells (cell 0,0 centred on the entry) and which room falls in which cell;
   - each boss arena with its size (at most about 100 m across) and the boss list, with the vanilla creature each boss
     is based on (section 6.6), so door widths and ceiling heights can be checked against its agent size (Part B
     step 4);
   - doors to seal, chests, spawners and the exit.

   Claude turns the sketch into layout and encounter JSON once the mod exists.

**Part A: one-time setup (about one day of your time; steps 1 to 4 today, 5 and 6 with milestone 12.3)**

Prerequisite: **Git on PATH.** Unity Package Manager installs packages from git URLs (the AssetBundle Browser in step 3
and MCP for Unity in step 6) by running the Git client, which must be installed and on PATH (Unity manual, "Git
dependencies": https://docs.unity3d.com/6000.0/Documentation/Manual/upm-git.html). Git is already installed on this
machine. MCP for Unity's server side also needs Python 3.10+ through `uv` (unity-mcp `README.md`, "Requirements"),
which your existing `uvx` setup already provides.

1. **Unity.** In Unity Hub, install exactly **6000.0.75f1** (`unityhub://6000.0.75f1/26349cd2a5c8`, released 2026-05-13).
   It is the version that built the 1.0.16 bundles (UnityFS header of `SoftRef/Bundles/10ba9da1`) and the version the
   Valheim-Modding wiki pins for 1.0.
   - Your 6000.0.63f1 would usually work, because older bundles usually load in newer players.
   - **Do not use 6000.3.2f1 or 6000.4.0f1.** Unity does not support bundles from a newer editor in an older player.
   - The default Windows editor install is enough.
2. **AssetRipper 2.0.0** (2026-08-24, GPL-3.0, the free GUI; a tool, so its licence does not affect our output) from
   https://github.com/AssetRipper/AssetRipper/releases.
3. **Rip the game**, following the wiki guide step by step
   (https://github.com/Valheim-Modding/Wiki/wiki/Valheim-Unity-Project-Guide, updated for 1.0 on 2026-09-14). The
   numbers below are the wiki's own.
   - **Disk space and time.** The wiki states neither, except that the first Unity import "can take up to 30 minutes"
     (Step 2, item 7). The SoftRef bundles alone are 4.1 GB compressed; an exported project plus Unity's `Library`
     cache is several times larger, and you will keep two copies. Measure your first rip before making the copy
     (estimate, unverified).
   - **Wiki Step 1 (AssetRipper), items 1 to 9:**
     - Item 1: run `AssetRipper.GUI.Free.exe` (it opens a console and a browser page), then File, Settings.
     - Item 2: use the settings shown in the wiki's screenshot (https://i.imgur.com/juJgQLP.png). This guide picks the
       Dummy shader setting.
     - Item 3: File, Open Folder, select the top-level `Valheim` folder. An error dialog may appear; the wiki says to
       ignore it.
     - Item 4: Export, Export all Files, into a new empty folder, for example `D:/UnityProjects/rip_out`.
     - Item 5: Select Folder, then Export Unity Project, and wait.
     - Item 6: the export folder now holds `AuxiliaryFiles` and `ExportedProject`. Delete `AuxiliaryFiles`, then
       **rename `ExportedProject` to `ValheimRIP` and move it to its permanent place**, for example
       `D:/UnityProjects/ValheimRIP`. This renamed folder is the Unity project; the export folder itself is not.
     - Item 7: in `ValheimRIP/Packages/manifest.json`, add `"com.unity.ugui": "2.0.0"` (the wiki links a copy of the
       file in case yours is damaged).
     - Item 8: before opening the project, delete `Assets/Plugins/Unity.TextMeshPro.dll` and
       `Assets/Plugins/UnityEngine.UI.dll` with their `.meta` files.
     - Item 9: only if you picked YAML shaders, copy the contents of `YamlShaders.zip` into `Assets/Shader`.
   - Shaders:
     - Dummy shaders (the guide's default): editable but wrong look. AssetRipper's dummy export is also the source of
       the `JVLmock_Custom/*` stub shaders.
     - YAML shaders: accurate look, read-only, warnings. If you pick YAML, Claude hand-writes the stub shaders.
   - **Wiki Step 2 (open in Unity), items 1 to 11** (the wiki numbers two items "11"; items 12 to 16 are optional):
     - Items 1 to 4: in Unity Hub, Add (or Open) and select the `ValheimRIP` folder. Use 6000.0.75f1.
     - Item 5: if the "Precompiled Assemblies Update Consent Request" appears, click Yes.
     - Item 6: if "API Update Required" appears, click "I Made a Backup. Go Ahead!". If "Do you want to enable the
       backends?" appears, click Yes.
     - Item 7: wait for the first import (up to 30 minutes).
     - Item 8: Edit, Project Settings, Graphics, Built-in Shader Settings, Deferred: change Custom Shader to Built-in
       Shader.
     - Item 9: Edit, Project Settings, Player, Other Settings, Rendering, Color Space: Linear.
     - Item 10: open `portal_wood`; with dummy shaders the particles show white squares, but the portal material should
       look right.
     - Item 11: with `portal_wood` still open, check the root has all its components and no missing scripts.
     - Item 11 (second): Window, AssetBundle Browser, select the first bundle, Ctrl+A, delete, and wait for the
       re-import. The AssetBundle Browser is not installed by default: in Package Manager, "Add package from git URL",
       paste `https://github.com/Unity-Technologies/AssetBundles-Browser.git`
       (https://github.com/Unity-Technologies/AssetBundles-Browser).
4. **Authoring copy** (wiki Step 2, item 14). Copy `ValheimRIP` to `D:/UnityProjects/ValheimRIPCustom`. You build
   dungeons there and keep `ValheimRIP` clean. Neither folder ever goes into git. The rip is for your machine only
   (section 4.5).
5. **MC Dungeon Kit** (Claude writes it). It lives in the repo at `unity/MC.DungeonKit/`: editor scripts, stub shaders
   named `JVLmock_Custom/Piece`, `JVLmock_Custom/StaticRock` and `JVLmock_Custom/Creature`, marker components, and your
   own assets. Link it into the authoring project as a local package
   (`"com.mc.dungeonkit": "file:D:/Gits/ValheimMods/unity/MC.DungeonKit"`). Whether Unity bundles assets from a local
   package is unverified; the fallback is that the export copies them under `Assets/MC_Generated` before building.
6. **MCP for Unity** (optional but recommended):
   - Add the MCP for Unity package (CoplayDev/unity-mcp, MIT) to `ValheimRIPCustom`.
   - Register the `UnityMCP` server for this repo with the same `uvx --offline --from mcpforunityserver==9.6.6
     mcp-for-unity` command your two other Unity projects use. Version 9.6.6 already has `execute_code` and
     `manage_build`, so no upgrade is needed.
   - Pin the Unity package to the same release as the server. The package lives in the repo's `MCPForUnity`
     subfolder, so a git URL without `?path=` fails in Package Manager. Add this line to
     `ValheimRIPCustom/Packages/manifest.json` under `dependencies`:
     `"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v9.6.6"`.
     (`MCPForUnity/package.json` at tag `v9.6.6` is `com.coplaydev.unity-mcp` version 9.6.6, read with `gh api` on
     2026-10-02.) Your Tetrarch project uses `...unity-mcp.git?path=/MCPForUnity#main` while the server is pinned to
     9.6.6 offline, which can give a client/server version mismatch (repo-fit research; not re-checked).
   - This changes your Claude configuration, so Claude only does it with your explicit OK. `execute_code` runs any C#
     in the editor; keep it to this local project.

**Part B: build a dungeon in Unity (needs the MC Dungeon Kit, milestone 12.3)**

1. **Scene.** Use `MC > Dungeon > New` (from the kit). It creates a scene `Dungeon_<id>` with a root at the origin and the
   children `Shell`, `Entrance`, `Networked`, `Markers` and `Volumes`.
   - Origin = the entry floor point. 1 unit = 1 m, Y up, +Z leads into the dungeon.
   - A gizmo draws the 64 m zone cells (cell 0,0 centred on the origin) and the height band (origin -300 m to +450 m).
2. **Shell.** Drag vanilla pieces from the rip. The kit tells you which kind each piece is (Validate labels it from its
   components), because only geometry-only pieces may go in `Shell` as they are:
   - **Geometry-only kit pieces** (no `ZNetView`; verified for the SunkenKit walls in bundle `5b5d26ec`): for example
     `Assets/world/Props/CastleBuildingKit` `SunkenKit_int_wall_1x4`, `_2x4`, `_4x4`, `SunkenKit_int_floor_2x2`, `_4x4`,
     `SunkenKit_int_arch`, `SunkenKit_int_stair`, decals. Other families (`MountainKit_int_wall_4x4`,
     `MountainKit_int_floor`, `caverock_*`, `dvergrprops_*`, `Props/CryptKit`, `DeepNorth_TimberHall`) must be checked
     by Validate (unverified).
   - **Networked pieces** (they have a `ZNetView`): `CastleKit_groundtorch` (plus `WearNTear`), `MountainKit_wood_gate`
     (a real `Door`), `CastleKit_brazier` (`Destructible`), `Morkhalla_Floor_4x4` and other `Props/Morkhalla` pieces,
     most `dvergrtown_*` pieces (bundle `c4210710` dump). Put them either under `Networked` (when they must work:
     gates, lit torches that can be used) or mark them as **stripped decoration** (the layout's `prefab` type: the
     `ZNetView` and gameplay components are removed on load). Never as plain shell geometry: every client would create
     a new saved object for each one, every time the zone loads (section 3.4).
   - **Whole rooms** from `Assets/world/Rooms/...` (forestcrypt, sunkencrypt, cave, mistlands, morkhalla). A room keeps
     only its non-networked parts: its doors, gates, networked torches and braziers, spawners, chests and destructibles
     **disappear**, and its doorways stand open (vanilla Client-mode placement, section 3.4). Validate lists what was
     dropped, with positions, and can turn it into `Networked` entries for you to keep or delete.
   - Your own prefabs.

   Put each piece under the `Cell_i_j` child of the 64 m cell that holds its pivot. Floors that players or creatures
   stand on must not cross a cell edge (section 2.4).
3. **Colliders and layers** (from the 1.0.16 layer table, collision matrix and `Pathfinding` scene data):
   - Walls, floors and ceilings: Box or Mesh colliders on **`static_solid` (15)**. It is in the character ground mask
     (`Character.s_groundRayMask`), the AI sight and solid masks (`BaseAI.m_solidRayMask`, `m_viewBlockMask`),
     `FindFloor` (`ZoneSystem.m_solidRayMask`), the `WearNTear` support mask, and the navmesh layers
     (`Pathfinding.m_layers` = Default, piece, terrain, static_solid, Default_small, blocker, pathblocker, decoded from
     `main.unity` by this investigation, not checked in game). Camera collision uses the serialized
     `GameCamera.m_blockCameraMask`, whose value was not decoded (unverified).
   - Small props: `Default_small` (20). Whether it keeps the camera from bumping into them depends on that camera mask
     (unverified).
   - Invisible walls: `blocker` (23) stops bodies but not arrows or hits; `pathblocker` (24) steers AI only and players
     walk through; `viewblock` (25) blocks AI sight (`BaseAI.m_viewBlockMask`; its effect on the camera depends on
     `m_blockCameraMask`, unverified).
   - Never use the `terrain` layer. `ZoneSystem.GetGroundHeight`, `Character.UnderWorldCheck`, `ItemDrop.TerrainCheck`
     and `TombStone.PositionCheck` would snap everything below onto it.
   - Not navmesh: `vehicle`, `piece_nonsolid`, others. AI cannot walk on them.
   - **Trigger volumes** (env, exit, arena, trigger, music; the engine adds the catch zone): always layer 14
     `character_trigger`. It is not in the player's hover mask nor in `FindFloor`'s mask, and still fires with players,
     as vanilla gateways do. A big trigger on Default or static_solid could block hovering chests and doors inside.
   - **Things the player hovers and uses with E** (entrance, exit, your own levers): a non-trigger collider on Default,
     piece or static_solid. A collider on layer 14 alone can never be hovered (section 3.2).
   - MeshColliders on your own meshes: enable Read/Write in the importer. Runtime navmesh collection may need it
     (unverified).
4. **Sizes for the bosses you plan** (`Pathfinding.SetupAgents`, agent slope 85):

   | Agent type | Radius | Height | Step (climb) |
   |---|---|---|---|
   | Humanoid / HumanoidNoSwim / HumanoidAvoidWater | 0.4 | 1.8 | 0.3 |
   | HumanoidBig | 0.5 | 2.5 | 0.3 |
   | GoblinBruteSize, HorseSize | 0.8 | 3.5 / 2.5 | 0.3 |
   | TrollSize | 1.0 | 7 | 0.6 |
   | Abomination | 1.5 | 5 | 0.6 |
   | SeekerQueen | 1.5 | 7 | 0.6 |
   | HugeSize | 2.0 | 10 | 0.6 |

   - Openings must be wider than twice the radius and taller than the height, **after scaling**: the boss's body is
     scaled by the encounter's `scale` field and by the world level (`1 + 0.2 x world level`, section 2.3), but its
     navmesh agent is not. Size clearance for agent size x `scale` x (1 + 0.2 x the highest world level the server
     will use), or set a maximum supported world level in `dungeon.json` and refuse higher ones. Example: a TrollSize
     boss with `scale` 1 needs about 7 m of clear ceiling at world level 0 and about 11 m at world level 3.
   - Steps higher than the climb value need an invisible ramp collider.
   - Each boss's actual agent type is prefab data (`BaseAI.m_pathAgentType`); the validator reads it.
5. **Networked objects.** Drop vanilla networked prefabs under `Networked`: `Spawner_*` (of the 98, 89 are
   `CreatureSpawner`, 8 are `SpawnArea` nests, and `Spawner_Hole_double` has only `Destructible` at its root; Jotunn
   prefab list for 1.0.7, re-counted), `TreasureChest_*`, `TriggerSpawner_*`, braziers, torches, and these doors, which
   have a real `Door` component and can **seal** an arena: `dungeon_forestcrypt_door`, `dungeon_sunkencrypt_irongate`,
   `iron_grate`, `MountainKit_wood_gate`, `dvergrtown_slidingdoor`. All but `dvergrtown_slidingdoor` can be broken
   (`WearNTear` or `Destructible`), so every seal door must be marked `indestructible` (section 3.8; Validate refuses a
   seal without it). `Morkhalla_GateDoor` is **not a door**: it has `ZNetView` + `Destructible` and no `Door` (1.0.16
   bundle `c4210710` dump), so use it as a breakable barrier, never as a seal. An `OfferingBowl` altar or any other
   piece that needs RPCs goes here too, never under `Shell` (section 3.4). Set fields in the inspector. The exporter
   writes only your changes (prefab overrides) and only the field types `LoadFields` supports. The loot chest is not
   placed here: put a LootChest marker instead; the engine spawns the chest at clear (section 3.8).
6. **Markers.** Add the kit's marker components: Entry, Exit, BossSpawn, AddSpawn, TeleportTarget, Waypoint,
   ArenaVolume, TriggerVolume, LootChest, EnvVolume, MusicVolume.
7. **Lights, water and openings.** Vanilla interiors are always dark. Use kit torches and braziers or your own realtime
   point and spot lights: inside, the sun is forced to vertex lighting (`EnvMan`, section 2.3), so it cannot light a
   room well. Baked lighting is not supported in v1, because the geometry is instantiated at runtime. Water volumes use
   `m_forceDepth >= 0` and no heightmap, so the layout also works in a far slot later (section 2.4).
   **Keep the shell closed** (floor, walls, ceiling), or give the dungeon a dense-fog environment: only terrain and
   grass are hidden while inside, so trees, rocks, buildings and water about 3.3 km below show through any opening
   (section 2.3).
8. **Validate** (`MC > Dungeon > Validate`). It checks:
   - no dependency outside our own folders (no ripped file in the bundle);
   - every mock name resolves in `manifest` / `manifest_extended`. 143 file names are duplicated across the two
     manifests (140 within `manifest_extended` alone, 31 of them prefab/mat/fbx/obj; 166 when compared ignoring case),
     so ambiguous ones are stored by AssetID or child path;
   - each mock and each `Shell` piece is labelled **geometry-only** or **networked** from its components; a networked
     piece under `Shell` is an error unless it is marked as stripped decoration;
   - whole rooms: the networked children that will be dropped are listed;
   - per room, kit piece and bundle prefab: the components that look up a parent `ZNetView`
     (`GetComponentInParent<ZNetView>`: `OfferingBowl`, `MaterialVariation`, `RandomMaterialValues`, `SpawnArea` and the
     others in section 3.4), which the runtime strips; one that matters for gameplay must move to `Networked`;
   - colliders on allowed layers; trigger volumes on layer 14; hover colliders not on layer 14;
   - door widths, ceiling heights and step heights against each boss's agent, scaled by its `scale` and the highest
     supported world level (step 4);
   - cell and height bounds; walkable floors inside their own cell;
   - every `seal` entry has a `Door` component and is `indestructible`;
   - every `WaterVolume` has `m_forceDepth >= 0`;
   - the shell is closed, or the dungeon's environment is foggy (a warning, checked by casting rays down and up from
     sample points);
   - no ProBuilder or other editor-only components left over;
   - our AssetIDs and paths never collide with vanilla ones (only needed if we ever call `Runtime.AddManifest`; a
     duplicate there breaks the game's loader).

   Optionally, it bakes a navmesh preview in the editor with Valheim's agent settings.
9. **Export** (`MC > Dungeon > Export`). It clones the root and replaces ripped instances with empty
   `JVLmock_<name>` objects (same transform). It points your materials at the stub shaders and writes the layout JSON
   (shell, networked objects, markers, volumes). It then builds `<id>.bundle` with `BuildAssetBundleOptions
   .ChunkBasedCompression` for `StandaloneWindows64`, with type trees kept, into
   `src/Exploration/Dungeon.Instances/Content/Dungeons/<id>/`, and writes the bundle's SHA-256 into `dungeon.json`
   (`bundleHash`, section 5). It can overwrite the bundle while the game runs only because the Debug mod never loads
   a bundle from the `DataFolder` path itself (Part D step 1).

**Part C: tweaked and brand-new assets**

1. **Brand new.** Model in Blender at 1 unit = 1 m. Export FBX with Apply Scalings set to "FBX Units Scale", Forward -Z,
   Up Y (or use Bake Axis Conversion in Unity's importer). Keep a separate low-poly collision mesh. Import into
   `unity/MC.DungeonKit/Assets/<dungeon>/`. Use your own textures on a material that uses a stub shader:
   `JVLmock_Custom/Piece` for architecture (`_MainTex`, `_BumpMap`, `_EmissionMap`, `_TriplanarMap`, `_AddSnow`),
   `JVLmock_Custom/StaticRock` for rock, `JVLmock_Custom/Creature` for creatures. Add colliders on the right layer and
   make a prefab.
2. **Tweaks of vanilla assets, done at runtime, never by shipping edited rips.** List them in the layout's `tweaks`
   block:
   - a material copy with `_Color` / `_EmissionColor`;
   - `Custom/Creature` `_Hue` / `_Saturation` / `_Value`, as `LevelEffects.SetupLevelVisualization` does;
   - scale;
   - a vanilla material on your own mesh (`JVLmock_<material>`).
3. **New boss looks** start as runtime clones of vanilla creatures with a tint and scale (section 6).
   - Clip swaps between creatures on the same rig use `AnimatorOverrideController`, which can be built at runtime with
     the `UnityEngine` API (https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorOverrideController.html).
     Vanilla ships two such controllers as editor-made assets, not built by game code: `FrozenKing_p3` uses
     `FrozenKing_p3_OverrideController` over `FrozenKing_animator`, and `Charred_Mage` uses
     `TheCharred_Mage_OverrideAnimator` over `TheCharred_Melee_animator` (1.0.16 bundle `c4210710` dump;
     `assembly_valheim` never references `AnimatorOverrideController`).
   - A brand-new rig needs a new Animator controller built in the editor, with Valheim's parameters, the `attack` tag
     and `Hit` / `OnAttackTrigger` clip events. That is a large job, kept for later.
   - Custom shaders (not vanilla stubs) need per-API variants: D3D11 + Vulkan on Windows, plus Linux and OpenGL.

**Part D: test in game**

1. Close the game and run `dotnet build ValheimMods.slnx` (a Debug build). Then point the mod at the repo, so a Unity
   export plus `dungeon reload <id>` rebuilds the geometry in place without a deploy. Set this one line in
   `<Valheim>/BepInEx/config/MC.Exploration.Dungeon.Instances.cfg`, section `[Developer]`:
   `DataFolder = D:/Gits/ValheimMods/src/Exploration/Dungeon.Instances/Content/Dungeons`
   (section 8 lists every setting). Content files are not deployed when you build with `-p:DeployToGame=false`.
   - **Bundle files and file locks.** `AssetBundle.LoadFromFile` normally keeps the file open until the bundle is
     unloaded, so on Windows a Unity export could not overwrite a bundle the running game holds (typical Windows file
     locking; unverified for this setup, probe P5 checks it). So in Debug, the mod never loads a bundle from the
     `DataFolder` path itself: it copies it to a temporary versioned file (`%TEMP%/MC_DI/<id>.<hash>.bundle`) and loads
     that copy (or reads it with `AssetBundle.LoadFromMemory`). `dungeon reload <id>` tears the geometry down, unloads
     the old bundle, then loads the new copy. Release builds load straight from the deployed folder.
2. In a test world: `devcommands`, `dungeon place <id>` at your feet, then walk in.
3. Iterate: `dungeon reload <id>` rebuilds the shell from the latest export; `dungeon reset <placement>` respawns
   objects and encounters.
4. Automated checks: `./tools/Test-InWorld.ps1 -Mod Dungeon.Instances`.
   - `dungeon.data`: every name in every shipped dungeon resolves; every referenced prefab is instantiated once.
   - Enter, exit, logout point, evacuation, registry round trip.
   - One smoke test per boss.

**Part E: the no-Unity path (in game)**

1. Dev-only mods, never shipped and only in a **separate authoring world** (devcommands and no-cost building flag
   characters as cheated):
   - Server Devcommands 1.115.0, World Edit Commands 1.79.0 and Infinity Hammer 1.86.0 (all Unlicense);
   - Comfy Gizmo 1.16.0 (GPL-3.0) for free rotation;
   - a build camera;
   - optionally More Vanilla Build Prefabs 1.5.0 (GPL-3.0).
2. Run `devcommands`, `setkey dungeonbuild`, `setkey nobuildingfall`, then god, fly and ghost.
   - Build on flat ground, or near y 3500 on an unscaled `Morkhalla_Floor_4x4` grid or on `Ice_floor`. Static
     networked props without `WearNTear` give full support to pieces resting on them. Do not count on scaled bases:
     vanilla keeps a placed object's scale only when its `ZNetView.m_syncInitialScale` is set (`ZNetView.Awake` reads
     the saved scale only then). `Ice_floor` is on Infinity Hammer's list of scalable objects
     (https://github.com/JereKuusela/valheim-infinity_hammer/blob/main/scalable_objects.md); no Mörkhalla piece is.
     A scaled Mörkhalla floor would lose its scale on reload unless the authoring world also runs Structure Tweaks
     (or a similar mod).
   - Vanilla rooms can be added by name in the layout. Infinity Hammer's `hammer_room` only persists into a
     `DungeonGenerator` that already has room data, so do not rely on it.
3. Place `sign` pieces with marker texts: `@origin` (entry floor; its yaw is snapped to 90 degrees), `@exit`,
   `@boss:<id>`, `@adds:<id>`, `@arena:<id> 30x8x30`, `@trigger:<id> 4x3x4 enter close:gate_a`.
4. Run `dungeon capture <id> radius=60`, **in single player or as the host** (capture writes files on the machine that
   runs it, so on a dedicated server the file would land on the server; v1 refuses capture there). The game collects
   the ZDOs in the box with `ZDOMan.FindSectorObjects`. It skips players, creatures, item drops and non-persistent
   objects. Pieces with no state become shell entries; stateful ones become networked objects. It strips `creator`,
   `support`, `cheated` and timestamps, and writes `layout.json` to the local `DataFolder`. ZDO keys are stored as
   32-bit hashes, so only key names the mod knows (the `ZDOVars` names and the `HasFields` keys it can build from
   component fields) are mapped back to names; unknown hashes are kept as hashes, with a warning per key.
5. Or save with `hammer_save` and run `dungeon import <file>.blueprint`. The PlanBuild piece line is
   `name;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;info;scaleX;scaleY;scaleZ`, with `;zdoData;chance` appended by
   Infinity Hammer and Expand World Data. Decoding IH's base64 `zdoData` is unverified.

### 4.4 How Claude helps

- Write and maintain the MC Dungeon Kit: New, Validate, Export, the navmesh preview, and the layout/blueprint importers.
- Through MCP for Unity:
  - place kit modules from your sketch or a grid spec (`manage_gameobject` with `prefab_path`);
  - set layers and colliders;
  - build prefabs (`manage_prefabs`);
  - run Validate/Export (`execute_menu_item`, `execute_code`) and read the console;
  - review scene screenshots and the navmesh preview (`manage_camera`).
- For brand-new meshes, a Blender MCP server (`blender-mcp`) is already registered on your machine for another project;
  registering it for this work would also need your OK.
- Write and tune the layout, encounter and loot JSON; check names against `dungeon info` output.
- Choose and write world placements (section 7).
- Build the dungeon in in-world self-tests and read the screenshots.

Limits: dummy shaders render wrong in the editor, there is no Valheim gameplay in the editor, judging space from
screenshots is approximate, and the rip project is large and slow to query.

### 4.5 Using vanilla assets legally

- The Coffee Stain EULA (last updated 2025-06-11, https://www.valheimgame.com/eula/) forbids reverse engineering,
  decompiling, modifying the game and making copies available. Iron Gate tolerates mods (`Game.messageForModders`,
  `Game.isModded`) and asks that they be free and labelled unofficial
  (https://www.dsogaming.com/news/valheim-developers-issue-official-statement-about-paid-mods/).
- Rule: the rip and anything copied from it stays on your machine, outside git, like `.ref/`. The shipped bundle holds
  only our own meshes and textures plus **name-only placeholders** (`JVLmock_<name>`), which the game resolves from its
  own files at runtime. The Validate step fails the build on any ripped dependency. Package-Mod already states that the
  zip contains no game files.
- Clones of vanilla prefabs made at runtime (bosses, tinted materials) redistribute nothing.

---

## 5. Layout and encounter data formats

All files are JSON and parsed with the Newtonsoft.Json 13.0.2 that ships with both the client and the dedicated server
(`valheim_Data/Managed`, `valheim_server_Data/Managed`); the csproj references it with `Private="false"`. Conventions:

- Unity axes: x right, y up, z forward. Positions in metres relative to the dungeon origin. Rotations as quaternions
  `[x, y, z, w]`.
- `format` and `version` fields; unknown major versions are refused.
- Unknown prefab or asset names log a warning and the entry is skipped (fail soft).
- **Content hash.** The dungeon's content hash, which the server sends in its rules (section 8), covers the JSON files
  only. The bundle enters it indirectly: Export writes the bundle's SHA-256 into `dungeon.json` (`bundleHash`), so the
  hash changes whenever the bundle does. A dedicated server therefore needs the same JSON files but not the bundle,
  and it never loads one. Each client checks its own bundle file against `bundleHash` at load; a missing or different
  bundle seals that dungeon for that player with a clear message, exactly like a hash mismatch.

### 5.1 Folder layout

```
src/Exploration/Dungeon.Instances/Content/Dungeons/frost_halls/
  dungeon.json        meta: name, recommended level, rules, entrance look
  layout.json         shell, networked objects, markers, volumes
  encounters.json     bosses, arenas, phases, actions
  loot.json           loot tables
  frost_halls.bundle  optional (Unity export)
```

The build deploys it as `BepInEx/plugins/MC_Valheim/Exploration/MC.Exploration.Dungeon.Instances/Dungeons/frost_halls/`.
A second search path, `BepInEx/config/MC.Exploration.Dungeon.Instances/Dungeons/`, holds server-local dungeons that mod
updates never overwrite.

### 5.2 `dungeon.json`

```json
{
  "format": "mc.dungeon", "version": 1,
  "id": "frost_halls",
  "name": "$mc_di_frost_halls",
  "nameFallback": "The Frost Halls",
  "gameVersion": "1.0.16",
  "tier": "Mountain",
  "bundleHash": "sha256:<written by Export; empty when the dungeon has no bundle>",
  "maxPlayers": 0,
  "maxWorldLevel": 3,
  "rules": { "allowOreIn": true, "keyItem": "", "consumeKey": false, "lockoutMinutes": 0 },
  "environment": { "name": "MC_DI_FrostHalls", "copyOf": "Caves", "force": false },
  "placement": { "biomes": ["Mountain"], "minDistanceFromCenter": 2500, "avoidLocations": true }
}
```

- `maxPlayers` and `rules.lockoutMinutes` override the server-wide defaults of section 8 for this dungeon (0 = use the
  default).
- `maxWorldLevel` is the highest world level the layout's clearances were sized for (section 4.3 Part B step 4); on a
  world above it the entrance shows "Not available at this world level".
- `environment.force` sets `EnvZone.m_force` explicitly (section 3.7).

### 5.3 `layout.json`

```json
{
  "format": "mc.dungeon.layout", "version": 1, "id": "frost_halls",
  "origin": { "y": 3500 },
  "bounds": { "center": [0, 20, 32], "size": [128, 80, 160] },
  "entrance": {
    "shell": [ { "type": "bundle", "prefab": "FH_EntranceArch", "pos": [0, 0, 0], "rot": [0, 0, 0, 1] } ],
    "interact": [0, 1.2, 1.5],
    "exitPoint": [0, 0.2, -3]
  },
  "shell": [
    { "type": "bundle", "prefab": "FH_Hall_A", "pos": [0, 0, 0], "rot": [0, 0, 0, 1] },
    { "type": "kit", "asset": "SunkenKit_int_wall_1x4", "assetId": "cdd872779cf004a1cbb8edbbe3f85321",
      "pos": [6, 0, 12], "rot": [0, 0.7071, 0, 0.7071] },
    { "type": "room", "room": "Assets/world/Rooms/<theme>/<room>.prefab", "pos": [0, -8, 64], "rot": [0, 0, 0, 1], "seed": 1234 },
    { "type": "prefab", "prefab": "Morkhalla_Floor_4x4", "pos": [0, -0.1, 0], "scale": [2, 1, 2] }
  ],
  "tweaks": [
    { "target": "FH_Hall_A/Braziers/*", "material": { "_EmissionColor": [0.3, 0.6, 1.0, 1] } }
  ],
  "objects": [
    { "id": "sp_hall_1", "prefab": "Spawner_Draugr_Elite", "pos": [10, 0, 20], "rot": [0, 1, 0, 0],
      "fields": { "CreatureSpawner.m_minLevel": 2, "CreatureSpawner.m_maxLevel": 3,
                  "CreatureSpawner.m_respawnTimeMinuts": 0.0, "CreatureSpawner.m_triggerDistance": 20.0 } },
    { "id": "gate_throne", "prefab": "dungeon_sunkencrypt_irongate", "pos": [0, 0, 96],
      "fields": { "Door.m_canNotBeClosed": false }, "indestructible": true },
    { "id": "chest_side", "prefab": "TreasureChest_forestcrypt", "pos": [-20, 0, 60],
      "chest": { "table": "frost_side" } }
  ],
  "markers": [
    { "id": "entry", "type": "entry", "pos": [0, 0.2, 2], "rot": [0, 0, 0, 1] },
    { "id": "exit", "type": "exit", "pos": [0, 0, -2], "size": [3, 3, 1] },
    { "id": "boss_jarl", "type": "bossSpawn", "pos": [0, 0, 128] },
    { "id": "adds_a", "type": "addSpawn", "pos": [-12, 0, 120] },
    { "id": "adds_b", "type": "addSpawn", "pos": [12, 0, 120] },
    { "id": "tp_1", "type": "teleportTarget", "pos": [-15, 0, 135] },
    { "id": "tp_2", "type": "teleportTarget", "pos": [15, 0, 135] },
    { "id": "lootChest_throne", "type": "lootChest", "pos": [0, 0, 140], "rot": [0, 1, 0, 0] }
  ],
  "volumes": [
    { "id": "arena_throne", "type": "arena", "center": [0, 6, 128], "size": [44, 14, 44],
      "barriers": [ { "center": [0, 3, 105], "size": [8, 6, 1], "layer": "static_solid", "vfx": "barrier_ice" } ] },
    { "id": "env_all", "type": "env", "center": [0, 20, 32], "size": [128, 80, 160] },
    { "id": "music_halls", "type": "music", "center": [0, 6, 40], "size": [40, 12, 60], "music": "<MusicMan name>" }
  ]
}
```

Rules:

- `type` per shell entry:
  - `bundle`: our bundle prefab, with mocks resolved.
  - `kit`: a SoftRef prefab by name plus AssetID.
  - `room`: a vanilla room by path, with `ZNetView` children disabled. `RandomSpawn` / `RandomObject` are seeded from
    `seed`. `SnapToGround` children are removed, because they would snap to the terrain 3.5 km below.
  - `prefab`: a `ZNetScene` prefab used as stripped decoration.
- `fields` map straight onto vanilla `HasFields` keys, using the concrete component type name. Floats must be written
  as floats: the vanilla `spawn` parser stores `-1` as an int, which a float field ignores.
- `indestructible` expands to ZDO `health = -1`, `WearNTear.m_health = -1`, `Piece.m_canBeRemoved = false`, and not
  targetable. ZDO health -1 also protects a `Destructible` (its `RPC_Damage` returns when health <= 0). It is
  **mandatory** for every object an arena lists under `seal`; the catalog refuses the arena otherwise (section 3.8).
- `prefab` and `room` entries are stripped of every component that looks up a parent `ZNetView` (section 3.4); an
  entry that needs RPCs (an altar `OfferingBowl`, a lever) goes in `objects` instead.
- Markers stay in the file and never become objects; volumes become local trigger boxes.
- Some vanilla room and kit paths are placeholders in the example; the validator lists real ones.

### 5.4 `encounters.json` (shape; the full boss example is in section 6.7)

```json
{
  "format": "mc.dungeon.encounters", "version": 1,
  "bosses": { "<bossId>": { "base": "<vanilla creature>", "...": "see 6.7" } },
  "arenas": {
    "<arenaId>": {
      "volume": "arena_throne", "wipeSeconds": 30, "seal": ["gate_throne"],
      "steps": [
        { "mode": "single|linked|council|transform",
          "bosses": { "<bossId>": "<bossSpawn or addSpawn marker id>" },
          "rules":  [ { "when": "<trigger>", "do": [ { "<action>": "..." } ] } ],
          "phases": [ { "id": "p1", "when": "<trigger>", "do": [ "..." ], "enable": ["<attack tag>"] } ] }
      ],
      "onClear": [ { "unseal": true }, { "spawnChest": "chest_final" } ]
    }
  }
}
```

- An arena always holds a list of `steps` that run in order: the next step starts when every boss of the current one
  is dead. A one-step arena is the plain single-boss case; several steps make the **sequence** (gauntlet) mode of
  section 6.4. Each step has its own mode, bosses (boss id to spawn marker), rules and phases.
- `onClear` runs once, on the server, when the last step is cleared. `spawnChest` there is the **only** way a
  `loot.json` chest from the `chests` block appears (section 5.6).
- Loot tables live in `loot.json`, not here.

### 5.5 Per-world placements file

Stored next to the world save. The folder is not hard-coded: Valheim picks it from the world's file source
(`SaveSystem.GetWorldsSaveRootPath`, SaveSystem.cs 921-924: `/worlds_local` only when the source is local, otherwise
`/worlds`; `Utils.GetSaveDataPath` returns the `-savedir` override when one is set, and an empty root for cloud saves).
So the server takes the folder of `World.GetDBPath()` for the loaded world (World.cs 106-114), which follows the world's
`m_fileSource` and a dedicated server's `-savedir`, and writes `<WorldName>.<WorldUID>.mc_dungeons.json` there. For a
source that is not a plain local file (a Steam cloud world), it falls back to
`BepInEx/config/MC.Exploration.Dungeon.Instances/worlds/<WorldName>.<WorldUID>.mc_dungeons.json` and logs which path
it uses. Under this choice (D9 (a)), the probe world's isolated save path
(`tests/Probes/Core.Probe.World/SaveIsolation.cs`) keeps self-test placements out of your real saves for free, and the
file follows world backups on that machine. The alternative is always using the `BepInEx/config` folder; see D9.

```json
{
  "format": "mc.dungeon.placements", "version": 1,
  "world": { "name": "Midgard", "uid": 123456789 },
  "placements": [
    { "id": "frost_halls_1", "dungeon": "frost_halls", "pos": [1834.2, 212.6, -3021.7], "yaw": 135,
      "snapToGround": true, "discovered": false, "note": "north face of the big Mountain east of base" }
  ]
}
```

### 5.6 `loot.json`

Loot tables follow the vanilla `DropTable` model (`m_drops` with weights, `m_dropMin` / `m_dropMax`, `m_dropChance`,
`m_oneOfEach`), because the server fills chests itself: it writes the `items` byte array (the `Inventory.Save` format,
`Container.Save`) and `addedDefaultItems = true` when it spawns the chest, so no client rolls the prefab's own
`m_defaultItems` (`Container.Awake`, owner only). `LoadFields` cannot carry a `DropTable`, which is why chests do not
use `fields`.

```json
{
  "format": "mc.dungeon.loot", "version": 1,
  "tables": {
    "frost_final": {
      "rolls": [2, 3], "oneOfEach": true,
      "drops": [
        { "item": "Silver", "min": 10, "max": 20, "weight": 3 },
        { "item": "FreezeGland", "min": 3, "max": 6, "weight": 2 },
        { "item": "TrophyFrostTroll", "min": 1, "max": 1, "weight": 1, "chance": 0.25 }
      ],
      "perPlayer": false
    },
    "frost_side": {
      "rolls": [1, 2],
      "drops": [ { "item": "Coins", "min": 20, "max": 60, "weight": 1 },
                 { "item": "Obsidian", "min": 4, "max": 8, "weight": 1 } ]
    }
  },
  "chests": {
    "chest_final": { "prefab": "TreasureChest_forestcrypt", "marker": "lootChest_throne", "table": "frost_final" }
  }
}
```

- `rolls` is the number of stacks drawn (min, max); `weight`, `min`, `max`, `chance` and `oneOfEach` mean what they mean
  in `DropTable`.
- `perPlayer: true` spawns one chest per player who was inside at the clear (each player opens their own); `false` is
  one shared chest per run (decision D7).
- A `chests` entry only describes a chest (prefab, marker, table). It appears only when an encounter runs a
  `spawnChest` action naming it, usually in the arena's `onClear` (sections 3.8, 5.4); there is no second trigger in
  this file, so a chest can never be spawned twice at one clear. A chest placed in the layout's `objects` with a
  `chest.table` is different: it exists from the start and is refilled at every dungeon reset.
- Item names are checked against `ObjectDB` at load (the names in the example are illustrative, unverified for 1.0.16).
- The server never sets the `cheated` key on a chest or its items (vanilla flags loot from cheated containers).

---

## 6. Boss scripting design

### 6.1 Vanilla building blocks (checked in 1.0.16 code and bundle data)

- **Attacks are inventory items.** `Humanoid.GiveDefaultItems` runs on every peer (seeded by ZDO `s_seed`).
  - On the owner, `MonsterAI.SelectBestAttack` runs `Humanoid.EquipBestWeapon` at most once per second, never during an
    attack. Items must pass `BaseAI.CanUseAttack`: `m_aiInDungeonOnly` (that is, `InInterior`), the health window
    `m_aiMinHealthPercentage..m_aiMaxHealthPercentage`, flying, walking, swimming and mist.
  - Then come range (`m_aiAttackRange`, `m_aiAttackRangeMin`), the cooldown (`m_aiAttackInterval` against
    `ItemData.m_lastAttackTime`), the angle (`m_aiAttackMaxAngle`), and priority (`m_aiPrioritized`,
    `m_aiPrioritizedIfAngleCheckValid`).
  - With no usable item, the creature falls back to `Humanoid.m_unarmedWeapon` if it has one, otherwise it idles.
    **Every phase must keep one always-valid attack.**
- **Animator constraint.** `Attack.Start` sends the trigger `m_attackAnimation`. It adds the chain level when
  `m_attackChainLevels > 1`, or a random index **0..n-1** when `m_attackRandomAnimations >= 2`. The hit happens later
  through a clip event (`CharacterAnimEvent.Hit` / `OnAttackTrigger`, which calls `Humanoid.OnAttackTrigger`, owner
  only). A trigger missing from the controller silently wastes the cooldown. Vanilla has a dead roll of its own:
  `SeekerQueen_Slap` sends `attack_slash0..3`, but the controller has `attack_slash1..4` (bundle data, not checked in
  game). New triggers cannot be added at runtime (`AnimatorController` is editor-only).
  - Work-around 1: `Attack.StartWithoutAnimation` (public; vanilla uses it in `Humanoid.BlockAttack`) fires an item's
    payload (projectile, `SpawnAbility`, `Aoe`, `TeleportAbility`) at once, with no trigger. Multi-burst projectiles
    may not work this way (unverified).
  - Work-around 2: `AnimatorOverrideController` swaps clips on the same rig at runtime.
  - Work-around 3: `Animator.Play` / `CrossFade` enter an existing state by name with no trigger, and the Playables API
    (`AnimationClipPlayable`) plays any clip. `ZSyncAnimation` only syncs triggers (RPC) and bool/float/int parameters
    (ZDO), so these need our own RPC to every peer, and `Humanoid.InAttack` depends on the `attack` animator tag, so
    the AI's in-attack checks behave differently. Kept for later.
  - The validator checks names with `ZSyncAnimation.HasParameter`, expanding chain and random suffixes exactly as
    `Attack.Start` does.
- **Payloads.** `Attack.AttackType` Horizontal, Vertical, Projectile, None, Area, TriggerProjectile.
  - `m_attackProjectile` can be any `IProjectile`: `Projectile`, `SpawnAbility`, `TeleportAbility`,
    `TriggerSpawnAbility`, `Aoe`. `m_spawnOnTrigger`.
  - `SpawnAbility`: `m_spawnPrefab`, `m_min/maxToSpawn`, `m_maxSpawned` (counts every loaded instance of that prefab,
    whoever owns it), `m_spawnRadius`, `m_circleSpawn`, `m_targetType`, `m_levelUpSettings`.
    - **Trap 1:** `m_snapToTerrain` casts down from `y + m_getSolidHeightMargin` (default 1000), so under a roof adds
      land on the roof. Kall's `spawn_tendril` uses 10.
    - **Trap 2:** on a miss it spawns at y 0. Keep `m_spawnRadius` inside the floor.
  - `TeleportAbility` moves the caster to a random object tagged `m_targetTag` within range. The Queen uses
    `SeekerQueenTeleportTarget`, 200 m. Tags cannot be created at runtime, and the search covers the whole scene, so
    two arenas within 200 m would share markers.
  - `TriggerSpawnAbility` calls `TriggerSpawner.TriggerAllInRange` (Queen: 30 m).
- **Phases in vanilla data** (bundle dump, 1.0.16):
  - Fader: `Fader_Fissure` 0.35-0.85 (30 s), `Fissure_Intense` 0-0.35 (20 s), `Roar` 0.35-0.55, `Roar_Intense` 0-0.35,
    `Meteors` 0.25-1 (25 s), `Meteors_Intense` 0-0.25 (18 s), `WallOfFire` 0.15-0.9 (60 s, `SpawnAbility` with 12 in a
    circle).
  - Queen: `SeekerQueen_Teleport` 0-0.9, dungeon only, 60 s; `SeekerQueen_Call` 0-0.99, dungeon only, prioritized, 60 s.
  - Kall p1: `FrozenKing_ChainSlam_L/R` 0.5-1, then the `_double` copies at <= 0.75 / <= 0.5.
  - "Intense" copies reuse the trigger with a shorter cooldown.
  - No vanilla boss item uses `m_attackKillsSelf`.
  - Eikthyr, the Elder, Bonemass, Moder and Yagluth have no health gates.
- **Phase as a new creature.** Kall's three phases are three prefabs chained by `Character.m_deathEffects` (owner only).
  `FrozenKing` spawns `FrozenKing_p2`, which spawns `FrozenKing_p3`; only p3 drops loot. Hildir's `GoblinBruteBros`
  drops the creature `GoblinShaman_Hildir` through `CharacterDrop` (any prefab). Those two are not `m_boss`: no boss
  bar, no `activeBosses`, no boss event. Their ragdoll `GoblinBrute_Hildir_ragdoll` has `m_dropItems = 0`, so the
  shaman appears at once from `CharacterDrop.OnDeath` on the owner (bundle data). Kall's death effects also spawn
  `FrozenKing_P2_Projectile_Eikthyr` next to `FrozenKing_p2`; how phase 2's Aspect summons are driven was not traced
  (unverified).
- **Boss flags.**
  - Fields: `Character.m_boss`, `m_bossOrder`, `m_bossEvent`, `m_dontHideBossHud`, `m_defeatSetGlobalKey`.
  - `EnemyHud.TestShow` shows a boss bar within 100 m (code default) while alerted. `EnemyHud.UpdateHuds` never
    positions boss HUDs per boss, so **several bosses overlap** (from the code, not checked in game).
    `EnemyHud.GetActiveBoss` returns one boss, so only one boss event (music and environment) plays.
  - `m_bossOrder > 0` triggers boss-kill stats, platform activities and `CampaignProgress` (`Game.RPC_RegisterKill`):
    keep it 0. `m_boss` still adds `BossLastHits` stats.
  - `BaseAI.SetAlerted` raises `GlobalKeys.activeBosses` once per boss (`s_bossCount`); only `Character.OnDeath` lowers
    it.
- **More vanilla knobs worth using** (code, 1.0.16):
  - `Character.m_aiCannotTargetOthers`: `MonsterAI.UpdateTarget` returns at once with no hearing or sight. A cheap
    switch for dormant or council bosses that must not engage before their turn.
  - `Attack.m_damageMultiplierByTotalHealthMissing` and `m_damageMultiplierPerMissingHP` (`Attack.ModifyHit`): a
    no-code enrage that grows as the boss loses health.
  - `Character.m_regenAllHPTime` (read by `BaseAI.UpdateRegeneration`): regeneration runs during the fight too
    (every 2 s on the owner, from world time), so high-HP clones need it set high.
  - `SpawnAbility` uses `Random.Range(int, int)`, which excludes the maximum: the count is `m_minToSpawn` to
    `m_maxToSpawn - 1` unless both are equal. Its `m_maxSpawned` cap counts instances by the name `<prefab>(Clone)`, so
    adds that need a per-encounter cap need their own clone names.
  - `Attack.m_attackHealthPercentage` is a health **cost** paid by the attacker, not a gate.
- **Scaling.** `Character.SetLevel`: max HP = `m_health` x world-level factor x level. `Attack.GetLevelDamageFactor` =
  1 + (level - 1) x 0.5. `CharacterDrop` multiplies `m_levelMultiplier` drops by 2^(level-1). Max HP survives a reload
  only while hurt (`Character.Awake` recomputes it), so set boss HP on the clone's `m_health`.
- **Ownership.**
  - Only the owner runs AI, attacks, death, drops and death effects.
  - On a handover, the ZDO state survives: health, level, alert, haveTarget, huntPlayer, bossCount, spawnpoint,
    attackers. All private `MonsterAI` state is lost, and **every attack cooldown resets**: each peer has its own item
    copies, and `m_lastAttackTime` is set only by `Attack.Start` on the owner.
  - An unowned boss regenerates for the elapsed world time when someone returns (`BaseAI.UpdateRegeneration`).
  - Only Eikthyr, the Elder, Bonemass, Moder and Yagluth have `m_enableHuntPlayer = true`. The Queen, Fader and every
    Kall phase have false; `docs/game/combat.md` section 11 needs this correction.

### 6.2 The scripted layer

1. **Content registry (data, option a).** Always on, in the `ZNetScene.Awake` and `ObjectDB` postfixes, following
   Sneak.Ambush `SmokeContent`:
   - Clone the base creature under an inactive `DontDestroyOnLoad` holder (no `Awake` runs). Rename it
     `MC_DI_<dungeon>_<boss>`.
   - Set `m_name` (a token), `m_boss = true`, `m_bossOrder = 0`, `m_bossEvent` (custom), `m_defeatSetGlobalKey`
     (`mc_di_<dungeon>_<boss>`), `m_health`, `m_damageModifiers`, `m_faction`, `m_group`, and
     `MonsterAI.m_enableHuntPlayer = false`.
   - Set `Humanoid.m_defaultItems` to cloned attack items, renamed and registered in `ObjectDB` (`VisEquipment` logs
     "Missing attach item" otherwise, and `ItemDrop.Awake` needs it for `m_dropPrefab`). `Object.Instantiate` already
     deep-copies `ItemData`, `SharedData` and `Attack` (all `[Serializable]`), so `Attack.Clone()` is not needed.
   - Each item's trigger is checked against the clone's animator.
   - `SpawnAbility` margins are set low for indoor use.
2. **Boss brain (option b).** `BossAI : MonsterAI`. `MonsterAI.UpdateAI` is a public override of the virtual
   `BaseAI.UpdateAI`, and `MonsterAI` is not sealed, so no hot-path Harmony patch touches every creature. Other mods'
   patches on `MonsterAI.UpdateAI` still run through `base.UpdateAI`, and `BossAI` must respect its return value
   (Creatures.Morale's prefix can skip vanilla).
   - `UpdateAI` on the owner: read the phase, flags and next-allowed times from the ZDO (network time). Evaluate the
     triggers. Run at most one scripted action at a time. Otherwise call `base.UpdateAI`, so vanilla targeting,
     movement and item selection carry on.
   - **How a scripted action pauses vanilla AI.** Actions are either *instant* (`say`, `teleport`, `spawnAdds`,
     `shield`, `despawn`...) or *running* (`moveTo`, `holdPosition`, `forceAttack` until the attack starts, `sleep`).
     While a running action is active, `BossAI.UpdateAI` does not call `base.UpdateAI` at all: no vanilla targeting,
     movement or weapon reselection that tick. Instead it drives the boss itself (for `moveTo`, the protected
     `BaseAI.MoveTo(dt, point, dist, run)` every tick, BaseAI.cs 969) and keeps the private
     `MonsterAI.m_updateWeaponTimer` pushed forward so `SelectBestAttack` cannot swap the weapon the moment vanilla
     resumes (MonsterAI.cs 727-730). Every running action has a timeout. When it ends (done, timeout, boss stunned or
     dead), `BossAI` calls `StopMoving()`, clears its private path state and resumes `base.UpdateAI` the next tick; the
     target is found again by vanilla. The running action, its start time and its timeout are written to the boss ZDO,
     so a new owner after a handover resumes or cancels it instead of losing it.
   - Because a skipped `base.UpdateAI` also skips other mods' patches on `MonsterAI.UpdateAI`, running actions stay
     short (seconds), and the cross-mod tests include Creatures.Morale.
   - **Phase gating**, two options:
     - a cheap `BaseAI.CanUseAttack` postfix that returns false for items whose phase tag is off. It runs for every
       monster's items once per second, so do a reference check first;
     - or no global patch: the owner adds, removes and retunes phase items in the boss's **own** inventory (each peer
       has its own copies) and re-applies them from the ZDO phase after a handover (unverified alternative).
3. **Encounter controller** (section 3.8) for engage, seal, mode rules, wipe and clear.
4. **JSON authoring (option c)** with a fixed action vocabulary. Every name is validated at load (`ZNetScene`,
   `ObjectDB`, `ZSyncAnimation.HasParameter` on the clone, with chain and random suffixes expanded exactly). Failures
   are soft, with clear log lines.

### 6.3 Phases, adds and arena mechanics

- **Triggers**: `hp<=x`, `time>=t` (since the phase started, network time), `addsDead`, `bossDead:<id>`,
  `allBossesDead`, `partnerHp<=x`, `enter:<volume>`.
- **Actions**:

  | Action | Effect |
  |---|---|
  | `say(text, center)` | Message to everyone |
  | `telegraph(fx, marker, seconds)` | Warning VFX on every peer through a routed RPC; reuse vanilla "start" effects (names to check; unverified) |
  | `forceAttack(item)` | `Humanoid.EquipItem` + `StartAttack` when not in an attack. `MonsterAI.SelectBestAttack` re-runs `EquipBestWeapon` every second whenever `!InAttack()`, including the frames before the animator enters the `attack` state, and unequipping stops the current attack; so `BossAI` holds weapon reselection (pushes the private `m_updateWeaponTimer`) until the forced attack has started |
  | `fire(item)` | `Attack.StartWithoutAnimation` payload while any animation plays |
  | `teleport(marker)` | Owner sets the position; synced by `ZSyncTransform`; path cache reset |
  | `moveTo(marker, run, timeout)` | Running action: the owner calls `BaseAI.MoveTo` toward the marker each tick and skips `base.UpdateAI` until the boss arrives or the timeout passes (section 6.2). Pair with `holdPosition` and `forceAttack` for "walk to the altar and channel" |
  | `holdPosition(seconds)` | Running action: the boss stands still (`StopMoving`), keeps facing its target, and does not reselect weapons; vanilla AI resumes afterwards |
  | `untargetable(seconds)` | The boss is immune to damage (flag in the `Character.RPC_Damage` prefix on the owner, which drops the hit) and ignored by other creatures' AI (`Character.m_aiSkipTarget`, read in `BaseAI` target search, BaseAI.cs 1402). With `"passive": true` it also stops picking targets itself (`Character.m_aiCannotTargetOthers`, MonsterAI.cs 235). Flags and end time live in the boss ZDO, so a new owner restores them |
  | `sleep` / `wake` | `MonsterAI.Sleep` / `Wakeup` (private, callable through the publicized assembly): they write `ZDOVars.s_sleeping` and send `RPC_Sleep` / `RPC_Wakeup` to every peer (MonsterAI.cs 871-911). Only for bases whose animator has the `sleeping` bool (checked with `ZSyncAnimation.HasParameter`); others fall back to `untargetable` + `holdPosition`. While scripted asleep, `BossAI` skips `base.UpdateAI`, because `MonsterAI.UpdateSleep` would wake it by range |
  | `despawn(boss)` | The boss's owner destroys it (`ZNetScene.Destroy`), with no death, drops or kill credit; it decrements `activeBosses` first when `s_bossCount` is set (section 3.6) |
  | `retreat(marker, seconds)` | Shorthand for `untargetable` + `moveTo(marker, run: true)` + `holdPosition`, the usual "leaves the fight for a phase" pattern |
  | `spawnAdds(prefab, count, marker, level)` | Owner spawns adds tagged with the controller ZDOID |
  | `hazard(aoe, marker, seconds)` | Local `Aoe` on every peer (`m_hitProps = false`) |
  | `shield(factor, seconds)` | Damage multiplier in a `Character.RPC_Damage` prefix on the boss owner |
  | `enableAttacks(tags)` / `disableAttacks(tags)` | Phase gating |
  | `seal` / `unseal`, `openDoor(id)` / `closeDoor(id)` | Doors and barriers |
  | `nextPhase` | Advance the phase |
  | `spawnBoss(id, marker)` | Bring in the next boss |
  | `setKey(name)` | Global key |
  | `spawnChest(id)` | Server spawns and fills a `loot.json` chest at its marker (section 5.6) |
  | `music(event)` | Switch the boss event |

- **Arena mechanics** built from vanilla parts: `TriggerSpawner`s placed in the arena (fired by `TriggerSpawnAbility` or
  `spawnAdds`); `TeleportAbility` with the `SeekerQueenTeleportTarget` tag on markers (only one such arena within
  200 m), or our own `teleport(marker)`; local hazard zones; doors and barriers; destructible "nests" as networked
  objects that gate a shield (the ExtendedBosses pattern).
- **Wipe and reset**: section 3.6. Respawn the bosses, never heal them. Decrement `activeBosses`. Remove tagged adds.
  The server also removes orphaned adds whose controller is gone, as ExtendedBosses' `ScanOrphans` does, but from the
  ZDOs (`ZDOMan.FindSectorObjects` over the footprint, then `SetOwner` + `DestroyZDO`), not from
  `ZNetScene.m_instances`: a dedicated server instantiates no world objects, so an instance-based scan finds nothing
  there.

### 6.4 Multiple bosses

| Mode | Behaviour | Implementation |
|---|---|---|
| **sequence** (gauntlet) | Boss B appears when A dies, in the same or the next arena | The controller watches the boss ZDOs (destroyed or dead) and runs `spawnBoss` |
| **linked** (twins) | Both fight at once; when one dies the other enrages, or they share one HP pool | Both are spawned by the one peer the server granted the engage (section 3.8), so they start with one owner; never pinned by a client-side `ClaimOwnership` race. Enrage through `partnerHp`/`bossDead` triggers; a shared pool kept in the controller ZDO and mirrored in an `ApplyDamage` postfix |
| **council** | Several present, one active at a time; the others support (buffs, adds) | Gate the attacks of the inactive bosses through phase tags; rotate on time or HP |
| **transform** | The boss becomes another creature (the Kall pattern) | `m_deathEffects` spawns the next phase prefab (kill credit counts at each step), or our `spawnBoss` after a scripted "death" |
| **wings** | Separate arenas gate the final door | Arena clear flags in the instance registry |

**HUD**: only one creature with a `m_bossEvent` at a time (one music and environment). An `EnemyHud.UpdateHuds` postfix
stacks the boss bars vertically for simultaneous `m_boss` creatures (Sneak.Ambush and Compendium.Encyclopedia also
patch `EnemyHud`: cross-mod tests). Lieutenants that should not get a bar keep `m_boss = false` and faction Boss. That
faction is also the one Creatures.Morale exempts.

### 6.5 Multiplayer ownership

- All scripted state lives in the boss and controller ZDOs. Timers use `ZNet.GetTimeSeconds()`. Local timers are rebuilt
  after a handover (the ExtendedBosses pattern).
- At engage, the server grants exactly one engaged player inside (section 3.8); that player's game spawns the bosses,
  so it owns them from creation. No client claims bosses on its own initiative.
  - The bosses stay with that player while they are in that player's area (`ReleaseNearbyZDOS` only moves an owned ZDO
    out of the owner's area).
  - If that player dies or leaves, ownership moves to another peer whose area contains the bosses, and the state
    carries over.
  - Content the server creates starts server-owned. A dedicated server's reference position is far away, so a peer
    takes it within about 2 s.
- Presentation goes through routed RPCs to every peer. Damage stays with vanilla (`RPC_Damage` to the owner).
- Required everywhere: a player without the mod could own the boss and run it without the script. The `PlayerCheck`
  pattern refuses such players.

### 6.6 Good vanilla bases (verified names; usable attack triggers after the verifier's corrections)

| Base prefab | Animator | Usable attack triggers | Good for |
|---|---|---|---|
| `FrozenKing` (Kall p1) | `FrozenKing_animator` (18 parameters) | chain_whirl, punch_aoe, chain_slamL/R and their doubles, chain_sweepL/R, spike_rain, rush, chain_flurry, double_sweep, taunt, spawn. `attack_jump` has no attack clip | Heavy melee champion |
| `Charred_Melee` (also `Charred_Melee_Dyrnwyn`; its HP is unverified) | `TheCharred_Melee_animator` (16, all with clips) | swing, bow, thrust, swingfeint, volley, thrustfeint, bolt, fireaoe, beam (start, loop, end), throw, scratch_l/r, block, taunt | Multi-weapon humanoid champion |
| `Fader` | 15 | flamebreath, bite, roar, WallOfFire, Fissure, ClawL/R, Spin, taunt, jumps | Large quadruped beast |
| `SeekerQueen` | 14 | call, spit, slash1-4, pierce, teleport, rush, bite, taunt | Insect queen |
| `Morgen` | 14 | bite, slam, swipe_1-6, roll_left/right, taunt | Brute |
| `JotunWarrior` | `Jotun_Animator` | slash, cleave, charge, dodge, dodger, slashdw, taunt (the troll triggers are leftovers with no clips) | Giant warrior |
| `JotunWitch` | 15 parameters, 7 usable + taunt | magicblast, lightningbolt, lightningstorm (not used by its default items), dodgeL/R/Up/Down | Flying caster |
| `FallenValkyrie` | 8 usable | wingspin, claws, rageattack, poisonbreath, airdrop, swoop, screech, taunt | Flyer |
| `GoblinKing` (Yagluth) | about 4 + taunt | cast1, nova, beam | Caster |
| `Dragon` (Moder) | 10 (flying) | bite, claws, iceball, breath, takeoff/land | Flying boss |
| `DvergrStaff_animator` users | 10 | poke, cast, protect, heal, icicle, nova, fireshower | Mage lieutenant |

Whether triggers unused by the default items have working transitions is unverified (probe P10). `Hive` / `TheHive`
(boss = true) were not analysed.

### 6.7 Example boss script

Item names that are not in the verified dump are illustrative; `dungeon info` lists the real ones.

```json
{
  "format": "mc.dungeon.encounters", "version": 1,
  "bosses": {
    "jarl": {
      "base": "FrozenKing",
      "prefabName": "MC_DI_FrostHalls_Jarl",
      "name": "$mc_di_jarl", "nameFallback": "The Rime Jarl",
      "health": 18000, "level": 1, "faction": "Boss",
      "bossEvent": { "id": "mc_di_frosthalls_jarl", "music": "boss_frozenking", "environment": "MC_DI_FrostHalls" },
      "defeatKey": "mc_di_frost_halls_jarl",
      "tint": { "_Hue": -0.05, "_Saturation": 0.2, "_Value": 0.1 }, "scale": 0.9,
      "attacks": [
        { "id": "slamL", "from": "FrozenKing_ChainSlam_L", "tags": ["p1", "p2", "p3"], "hp": [0, 1] },
        { "id": "slamR", "from": "FrozenKing_ChainSlam_R", "tags": ["p1", "p2", "p3"], "hp": [0, 1] },
        { "id": "slamDouble", "from": "FrozenKing_ChainSlam_L_double", "tags": ["p2", "p3"], "interval": 12 },
        { "id": "spikes", "from": "FrozenKing_SpikeRain", "tags": ["p3"], "interval": 25 },
        { "id": "roots", "from": "FrozenKing_tendrilspawn", "tags": ["p3"], "interval": 45,
          "payload": { "getSolidHeightMargin": 8 } }
      ]
    },
    "warden_left":  { "base": "JotunWarrior", "health": 6000, "faction": "Boss", "boss": false,
                      "attacks": [ { "from": "<JotunWarrior cleave item>", "tags": ["all"] } ] },
    "warden_right": { "base": "JotunWarrior", "health": 6000, "faction": "Boss", "boss": false,
                      "attacks": [ { "from": "<JotunWarrior slash item>", "tags": ["all"] } ] }
  },
  "arenas": {
    "throne": {
      "volume": "arena_throne", "mode": "sequence", "wipeSeconds": 30, "seal": ["gate_throne"],
      "steps": [
        { "mode": "linked", "bosses": { "warden_left": "adds_a", "warden_right": "adds_b" },
          "rules": [ { "when": "bossDead:warden_left",  "do": [ { "shield": { "boss": "warden_right", "factor": 0.5, "seconds": 10 } } ] },
                     { "when": "bossDead:warden_right", "do": [ { "shield": { "boss": "warden_left",  "factor": 0.5, "seconds": 10 } } ] } ] },
        { "mode": "single", "bosses": { "jarl": "boss_jarl" },
          "phases": [
            { "id": "p1", "enable": ["p1"] },
            { "id": "p2", "when": "hp<=0.7",
              "do": [ { "say": "The ice remembers you." }, { "teleport": "tp_1" },
                      { "spawnAdds": { "prefab": "Draugr_Elite", "count": 2, "markers": ["adds_a", "adds_b"], "level": 2 } } ],
              "enable": ["p2"] },
            { "id": "p3", "when": "hp<=0.35",
              "do": [ { "untargetable": { "seconds": 8 } },
                      { "moveTo": { "marker": "boss_jarl", "run": true, "timeout": 6 } },
                      { "telegraph": { "fx": "<vanilla start fx>", "marker": "boss_jarl", "seconds": 2 } },
                      { "holdPosition": 2 },
                      { "hazard": { "aoe": "<frost aoe prefab>", "marker": "boss_jarl", "seconds": 12 } },
                      { "forceAttack": "spikes" } ],
              "enable": ["p3"] }
          ] }
      ],
      "onClear": [ { "unseal": true }, { "spawnChest": "chest_final" }, { "setKey": "mc_di_frost_halls_cleared" } ]
    }
  }
}
```

The example shows the full grammar of section 5.4: an arena with two steps (a linked twin step that enrages the
survivor, then a three-phase final boss with a teleport, adds, a scripted walk back to the throne while untargetable, a
telegraphed hazard and a forced attack). Every phase keeps the two plain slams, so the boss is never without an attack.
The chest appears only through `onClear`'s `spawnChest`. Item names checked against the 1.0.16 bundle dump:
`FrozenKing_ChainSlam_L/R` and `_L_double` are FrozenKing (p1) items; `FrozenKing_SpikeRain` and
`FrozenKing_tendrilspawn` are FrozenKing_p3 items whose triggers (`attack_spike_rain`, `spawn`) exist on
`FrozenKing_animator`. Only `roots` gets a height margin: its payload `spawn_tendril` snaps to the ground
(`m_snapToTerrain = 1`), while `spawn_frozenking_spikerain` has `m_snapToTerrain = 0` (it drops spikes from 15 m above),
so a margin there would do nothing.

---

## 7. Placing dungeons in the world

### 7.1 How an entrance gets into a world

- **Why not a vanilla location as the entrance.** The vanilla paths, as read in 1.0.16:
  - `ZoneSystem.SpawnLocationMidGame` (used by `PersistentEventSystem`) clamps the point into its zone and calls
    `RegisterLocation(generated: false)`, so the location is only built when that zone is generated for the first time
    (`SpawnZone`, then `PlaceLocations`); `CanSpawnLocationMidGame` refuses generated zones that already hold a
    location (ZoneSystem.cs 2178, 2321-2349).
  - The console `location` command (`ZoneSystem.TestSpawnLocation`, ZoneSystem.cs 2275-2319) clamps the point into
    the zone, picks a random rotation in 22.5 degree steps and a random seed, spawns in Full mode, marks the objects
    `cheated`, does not register the instance, and turns world saving off unless `SAVE` is given.
  - But the core call, `ZoneSystem.SpawnLocation(location, seed, pos, Quaternion rot, mode, ...)` (private, callable
    through the publicized assembly, ZoneSystem.cs 2401), takes our rotation and does no clamping. The vanilla
    interiors research recommended exactly this from a placement command: `SpawnLocation` in Full mode plus
    `RegisterLocation(generated: true)`. So a custom location entrance **is possible**. Its real costs: the location
    prefab needs a SoftRef AssetID (`Runtime.AddManifest` with our own manifest, called before the loader exists,
    section 3.1), and `RegisterLocation` allows **one location per zone** (it logs "Location already exist in zone"
    and returns, ZoneSystem.cs 2600-2616), so no entrance in a zone that already has one.
  - What a ZDO entrance gives up, and must re-implement where wanted: the `NoBossPortals` door rule through
    `Location.IsInsideActiveBossDungeon` (our exit applies it itself, section 3.5); the location's no-build area
    (`Location.m_noBuild`, default true; v1 relies on the vanilla `NotInDungeon` rule inside and has no no-build ring
    at the entrance); `LocationProxy` rebuilding the location from its seed on every load (our anchors and the
    placements reconcile do this); and the location registry used by map icons, `Location.GetLocation` and tools such
    as Upgrade World (our registry and map pins replace it).

  Recommendation: the plain ZDO entrance (simpler, no SoftRef manifest, any zone without an interior location). The
  **server creates a persistent ZDO** of the registered `MC_DI_Entrance` prefab (`ZDOMan.CreateNewZDO`; Persistent
  first, then `SetPrefab`, `SetRotation` and the keys). This works in explored and unexplored worlds, on a dedicated
  server, and in unloaded zones. Clients instantiate it when they load the zone.
- **Height** (the server stays the only writer of the entrance ZDO, section 3.3):
  - `dungeon place` records the admin's own feet height, so no snap is needed. This is the default.
  - A placements-file entry with `snapToGround: true` is created at `WorldGenerator.GetHeight`, which ignores terrain
    edits, with a "snap pending" flag; entry is refused while it is pending. The first client that has the zone loaded
    measures `ZoneSystem.GetGroundHeight` and sends `<GUID>.SnapReport(placementId, y)` to the server. The server
    accepts the first report, writes the position once, clears the flag, and updates the placements file
    (`snapToGround: false`, the measured y). Later reports are ignored. Every client then sees the same hover collider
    and exit point.
- The anchors are created at the same time, one per footprint zone, at y 3500. The networked layout objects are created
  at the first entry request (state Placed to Ready, section 3.3).
- Explored zones only by default: `dungeon place` uses the admin's own (generated) zone. For a placements-file entry in
  a zone that has never been generated, the reconcile waits until `ZoneSystem.IsZoneGenerated` is true, because zone
  generation would otherwise place vegetation and locations without knowing about the entrance (design choice; the
  exact interaction is unverified). Trees already standing on the spot are cleared by hand in v1.

### 7.2 Admin commands

One command, `dungeon`. The host and single player always pass the admin check. Remote admins go through
`<GUID>.Admin` / `<GUID>.AdminReply`, because vanilla remote commands send no output back (`ZNet.RPC_RemoteCommand`) and
cheat commands only run on the server (`Terminal.IsCheatsEnabled`).

| Command | Effect |
|---|---|
| `dungeon list [radius]` | Placement id, dungeon, x/y/z, distance, state, players inside, reset timer |
| `dungeon info <dungeonId>` | Loaded data, content hash, missing names, boss items and their animator triggers |
| `dungeon suggest <dungeonId> [count] [near x z] [biome b]` | The server samples `WorldGenerator.GetBiome` / `GetHeight` for flat spots matching the dungeon's placement rules. It skips zones with locations, water and steep ground, prints the candidates and writes `suggestions.json` for Claude |
| `dungeon place <dungeonId> [placementId]` | At your feet, facing your look direction; writes the placements file |
| `dungeon move <placementId>` / `dungeon rotate <placementId> <deg>` | Edit a placement (moves the entrance; the column follows its new zone) |
| `dungeon remove <placementId> [purge]` | Removes the entrance; `purge` also removes anchors and objects, lowering `activeBosses` for counted bosses as `dungeon purge` does. Tombstones are moved, never deleted |
| `dungeon reset <placementId\|all> [lockouts]` | Encounter and dungeon reset |
| `dungeon goto <placementId> [inside]` | Admin teleport through our RPC (vanilla `goto` cannot run on an admin client of a dedicated server) |
| `dungeon reload [dungeonId]` | Re-read the data; rebuild loaded shells |
| `dungeon capture <dungeonId> radius=<r>` | In-game authoring capture (section 4.3 E); single player or host only, writes locally |
| `dungeon import <file>` | PlanBuild `.blueprint` / `.vbuild` import |
| `dungeon boss <placementId> <bossId> phase <n>\|kill\|respawn` | Test helpers |
| `dungeon purge` | Evacuates players, moves tombstones to the entrances, then removes every object of the mod. For every boss it removes whose ZDO has `s_bossCount` set, it first lowers `GlobalKeys.activeBosses` by one, as the wipe does. Run it before uninstalling |

**Why purge must fix `activeBosses`.** Only `Character.OnDeath` lowers the counter (Character.cs 2994-2998). When the
mod is removed, a host destroys every ZDO whose prefab it no longer knows with `SetOwner` + `DestroyZDO` ("Destroyed
invalid prefab ZDO", `ZNetScene.CreateObjectsSorted`, ZNetScene.cs 229-235), which never runs `OnDeath`; a dedicated
server keeps the ZDO, but no game can instantiate it again. Either way an `MC_DI_*` boss that was alerted at that
moment keeps `activeBosses` raised forever, and with `NoBossPortals` every `TeleportWorld` portal in the world stays
blocked (TeleportWorld.cs 130). Self-test `dungeon.purge`: alert a boss, run purge, check that `activeBosses` is back to
its value before the fight and that no `MC_DI_*` ZDO remains.

### 7.3 How Claude helps choose and set positions

1. You describe the spot ("north face of the big Mountain east of my base", "near Moder's altar", "one per biome").
2. Claude asks you for `pos` output (devcommands) or runs nothing in game itself. You run
   `dungeon suggest frost_halls 10 near 1800 -3000 biome Mountain`.
3. Claude reads `suggestions.json` and the placements file, checks the footprint rules, and writes the chosen entry into
   the placements file with a note. Rules: no overlap with another instance, no vanilla location with an interior in the
   footprint zones (a warning for exterior-only locations), not inside a player base (`PrivateArea`), not in water. A
   warning, not a refusal, when |x| > 8800 (the Deep North camera quirk of section 2.3, an inference until probe P9).
4. The server reconciles live (file watcher). You walk there and check it; adjust with `dungeon move` / `rotate`, or ask
   Claude to edit the file.

On a remote or rented server, where Claude cannot read the server's files: paste the `dungeon suggest` or `dungeon list`
output into the chat, and Claude replies with the exact `dungeon place` / `dungeon move` commands, or the coordinates to
stand on before running `dungeon place`.

**Existing worlds**: everything above. **New worlds and public releases** (later): an `AutoPlaceMissing` server option
places each dungeon by its `placement` rules once per world, using the same ZDO path. Placing during zone generation (a
`ZoneSystem` hook) is not needed.

---

## 8. Fit in this repo

**Identity** (permanent after release):

| Option | GUID | Display name | Package |
|---|---|---|---|
| **Recommended** | `MC.Exploration.Dungeon.Instances` | Dungeon Instances | `DungeonInstances` |
| Alternative | `MC.Exploration.Dungeons.Handcrafted` | Handcrafted Dungeons | `DungeonsHandcrafted` |
| Alternative | `MC.Exploration.Delves.Instanced` | Instanced Delves | `DelvesInstanced` |

- Root namespace `MC.Exploration.DungeonInstancesMod`.
- Permanent prefab names: `MC_DI_Entrance`, `MC_DI_Anchor`, `MC_DI_Controller`, `MC_DI_Registry`, plus
  `MC_DI_<Dungeon>_<Boss>` and `MC_DI_<Dungeon>_<Item>` for clones.
- ZDO keys `<GUID>.<Name>`; global keys `mc_di_*`.
- The assembly name is the GUID, so bundles never contain our `MonoBehaviour`s. Marker objects are turned into
  components by the engine.

**csproj**:

- `ModSide=Both`, `ModMultiplayer=Compatible`, `ModScope=New`, `ModNetworkVersion=1`. Bump the network version when RPC
  names or payloads, ZDO keys or the registry blob change.
- `ModMultiplayerNotes`: required everywhere; the server refuses players without it; the server's rules apply to all.
- `ModIdea=Big unique dungeon`, or a new row (D10).
- Reference `Newtonsoft.Json.dll` with `Private="false"`.
- `SoftReferenceableAssets.dll` is referenced by `Directory.Build.props` without publicizing. Everything the resolver
  calls is public; only the `AssetBundleLoader.InitializeDataSide` prefix needs a private target (Harmony by name, or
  add the DLL to the publicizer).

**Folders**:

```
src/Exploration/Dungeon.Instances/
  Plugin.cs                      BindConfig (config, catalog JSON, Runtime.MakeAllAssetsLoadable only; no other SoftRef call),
                                 LocalBlocker (returns the cached SoftRef check), OnActivated, OnDeactivated, FeatureActive
  PlayerCheck.cs ServerRules.cs DungeonRules.cs   copied from Swimming.Dive; rules carry one content hash per dungeon
  Prefabs/                       generic prefabs built under an inactive holder (Prefabs.cs). Not under Content/:
                                 everything in Content/ is deployed and zipped (Directory.Build.props ModContent)
  Catalog/                       DungeonDef, DungeonCatalog (search paths, validation, hashes), LayoutReader (+ blueprint)
  Assets/                        Resolver (mocks, shaders by name table Custom/Piece -> Piece.shader), BundleCache
  World/                         Registry, Placements (file watcher, reconcile), InstanceManager, Anchors, Builder, Entrance, Exit
  Encounters/                    Controller, BossAI, Actions, BossClones, HudStack
  Patches/                       ZNetScenePatches + ObjectDBPatches + PlayerProfilePatches + FejdStartupPatches (SoftRef
                                 check) + AssetBundleLoaderPatches ([AlwaysOnPatch]), ZNetPatches, EnemyHudPatches,
                                 BaseAIPatches (phase gating), CharacterPatches (shields, untargetable)
  AdminCommand.cs MapPins.cs SelfTests.cs (#if DEBUG)
  Content/Dungeons/<id>/...      shipped data (deployed next to the DLL; ModPlugin.ModFolder)
  README.md (with an uninstall section: run "dungeon purge" first) CHANGELOG.md TESTING.md icon.png
unity/MC.DungeonKit/             our own Unity sources and editor tools (no ripped assets; Library ignored)
docs/design/exploration-dungeon-instances.md
```

**Jotunn: no.**

- The repo's 20+ mods use plain BepInEx plus the MC framework. The build only generates soft dependencies from
  `ModRequires` (MC mods). A hard Jotunn dependency would make BepInEx skip our plugin with no status line when Jotunn
  is missing.
- `ModDependencies` only becomes Nexus requirement text and Thunderstore manifest dependencies. The all-mods pack's
  Requirements line lists only BepInExPack.
- Jotunn applies its patches from static initialisers under its own Harmony instance, outside our live toggle. Its
  network compatibility default is NotEnforced.
- The tests would need Jotunn in the game folder, and we would wait for its updates after game patches.
- What we would take from it is small: mock resolution for bundles. We reimplement that with public SoftRef API and
  keep the `JVLmock_` naming (with the `__` child separator) so Jotunn's Unity guides still apply. Jotunn 2.30.2 is MIT
  if we ever port pieces of it.

**Framework, live toggle and required everywhere.**

- `Plugin` overrides only `BindConfig`, `OnActivated` and `OnDeactivated`. The hot-reload flag is polled from a game hook
  (a `ZNet.Update` postfix, as `PlayerCheck` does), not from `Update`.
- Content registration and the logout patch are `[AlwaysOnPatch]`.
- While the feature is off: entrances stay and show "sealed", geometry stays, encounter scripts stop, nothing is
  deleted. When the server turns it off, players inside are evacuated (`RPC_TeleportPlayer`). `OnDeactivated` must do
  nothing at game quit.
- Join check: copy Swimming.Dive's `PlayerCheck`, `ServerRules` and `Patches/ZNetPatches.cs` (`AllowPlayersWithoutMod`,
  default false). The rules carry each dungeon's content hash, because `ModNetworkVersion` does not cover data. A
  mismatch seals that dungeon for that player with a clear message.
- **SoftRef order.** No SoftRef getter or load before the vanilla loader exists (section 3.1): `BindConfig` only calls
  `Runtime.MakeAllAssetsLoadable`; the `manifest_extended` check runs in a `FejdStartup.Awake` postfix and is cached;
  `LocalBlocker` only reads that cache (null until the check has run). Prefab and item name validation runs in the
  `ZNetScene.Awake` / `ObjectDB` postfixes. Cross-mod test with More World Locations AIO installed: its locations still
  appear in a new world, and the log has no "AddManifest() must be called before loading any assets" line.

**Config** (`BepInEx/config/MC.Exploration.Dungeon.Instances.cfg`; descriptions in the README config table). "Synced"
means the server sends its value to every player in `ServerRules`, and the server's value is the one in force.

| Section | Key | Default | Applies on | Synced | What it does |
|---|---|---|---|---|---|
| General | Enabled | true | every game | no (framework; the server's state gates players) | Turns the mod on or off live |
| General | AllowPlayersWithoutMod | false | server | no | Lets players without the mod join (they cannot enter dungeons) |
| Instances | MaxPlayers | 0 (no limit) | server | yes (hover text) | Players allowed inside one dungeon at once; `dungeon.json` `maxPlayers` overrides it when set |
| Instances | OnePartyAtATime | false | server | yes (hover text) | While a run is in progress, only the players who started it may enter (D1 (b)) |
| Instances | WipeSeconds | 30 | server | **yes** (the controller owner, a client, detects the wipe) | Seconds with no living engaged player in a sealed arena before the encounter resets |
| Instances | ResetAfterEmptyMinutes | 30 | server | no (the state key shows the timer) | Minutes a dungeon must stay empty before a full reset |
| Instances | ReopenAfterClearMinutes | 120 | server | no | Minutes after a clear before the dungeon resets for a new run |
| Instances | LockoutMinutes | 0 (off) | server | no (pushed per player by `<GUID>.Lockouts`) | Per-character lockout after a clear; `dungeon.json` `rules.lockoutMinutes` overrides it when set |
| Instances | AnchorHoldTimeoutSeconds | 20 | every game | no | Hard timeout of an anchor's zone hold (section 3.4) |
| Map | EntrancePins | true | client | no | Adds a map pin for each discovered entrance |
| Placement | WatchPlacementsFile | true | server | no | Re-reads the placements file when it changes (section 7.3) |
| Placement | AutoPlaceMissing | false | server | no | Later phase: places each dungeon once per world by its `placement` rules |
| Developer (Debug builds only) | DataFolder | "" (use the deployed `Dungeons` folder) | every game | no (the content hash catches a mismatch) | Extra dungeon folder, for example the repo's `src/Exploration/Dungeon.Instances/Content/Dungeons` (Part D) |
| Developer (Debug builds only) | HotReload | true | every game | no | Watches `DataFolder` and rebuilds loaded shells after an export (polled from a `ZNet.Update` postfix) |

**Packaging.**

- `Content/**` is deployed next to the DLL and copied into the Nexus and Thunderstore zips (`Directory.Build.props`
  `ModContent`, `Package-Mod.ps1`). The dungeon mod would be the first user.
- Any data change requires a version bump at release.
- `DeployMod` never deletes stale deployed files, so the catalog warns about unknown folders.
- The first edit that dirties the tree recompiles every mod (the build id becomes `<hash>+dirty`).
- Bundles are built for `StandaloneWindows64` with LZ4 chunk compression and loaded with `AssetBundle.LoadFromFile`
  (in Debug from a temporary copy, Part D). Dedicated servers do not need the bundle files: they never instantiate
  world objects, and the content hash they send covers the JSON files, which carry each bundle's `bundleHash`
  (section 5). They do need the same JSON files as the clients. macOS clients would need a separate build (unverified
  need).
- Nexus pages need the "AI Assisted" + "AI Media" tags and the in-game tests (CLAUDE.md).

**Self-tests** (`./tools/Test-InWorld.ps1 -Mod Dungeon.Instances`). Under D9 (a), the recommended choice, the
placements file lives next to the world save, which in a test run is the probe world's isolated save folder
(`SaveIsolation.cs`), so the self-tests may write the real file there. Only under D9 (b) (`BepInEx/config/<GUID>/`,
which `Test-InWorld.ps1` does not back up: it restores only `BepInEx/config/MC.*.cfg`) would they have to keep
placements in memory.

- `dungeon.data`: every shipped dungeon parses; every name resolves; every referenced prefab is instantiated once.
- `dungeon.enter-exit`: place a test entrance, `Interact`, wait for `!IsTeleporting`, check `InInterior`, the band, the
  environment and a screenshot, then exit.
- `dungeon.logout-point`.
- `dungeon.anchors-host-away`: walk 300 m away and back; the geometry is rebuilt and the anchors still exist.
- `dungeon.anchor-load-failure`: a layout with a missing AssetID, then an anchor destroyed mid-build; after each,
  `IsAreaReady` at the entrance becomes true again within the hold timeout, entering does not hang, and a login at the
  exit point completes (section 3.4).
- `dungeon.reload-forced-env`: reload the shell while the local player stands in a forced `EnvZone`; the forced
  environment is cleared (section 3.7).
- `dungeon.registry-roundtrip`: save, reload, read back.
- `dungeon.reset`: tombstone moved, tagged objects re-created, `activeBosses` balanced.
- `dungeon.purge`: alert a boss, purge; `activeBosses` back to its value before the fight, no `MC_DI_*` ZDO left.
- `dungeon.evacuate`.
- `dungeon.exit-noboss-portals`: with `NoBossPortals` set and a boss alerted, the exit refuses with
  `$msg_blockedbyboss`; after the wipe it works.
- `dungeon.events-not-random` (section 3.7).
- One smoke test per boss: spawn, phase transitions through `dungeon boss ... phase`, missing-trigger detection.

Multiplayer items for `TESTING.md` (not automatable in one game): two clients enter the arena trigger in the same
frame and exactly one boss set spawns (section 3.8); a late `Arrived` after a rollback is re-validated or evacuated
(section 3.2).

**Cross-mod tests** (`TESTING.md`):

- DeepNorth.Awakening: its `EnvMan` postfix and `Storms` skip interior players, but `HeldCells` / `AreaSpawns` work from
  XZ and may react to players in an instance above a cell.
- Creatures.Morale: rates interior creatures by the location or terrain biome below, and exempts boss faction and
  `IsBoss`.
- Sneak.Ambush and Compendium.Encyclopedia (`EnemyHud`).
- Stats.PerCreature (new creature names).
- Weapons.Moveset, Weapons.DualWield, Shields.TowerWall, Trinkets.OnDemand, Crossbow.StaysLoaded (attack and damage
  path).
- Harpoon.HooksTames (`SE_Harpooned` checks `IsBoss`).
- Sleep.ThroughDay.

**Docs corrected with this investigation** (2026-10-02; the claims were re-checked: Dungeon Splitter's README, Dungeon
Creation Kit's Thunderstore README, the Queen and Kall `MonsterAI` dumps, `AssetBundleManifest.SerializeToDisk`):

- Dungeon Splitter does not "move dungeons into a separate instance"; it filters network sync by height, and it is
  deprecated. The claim is in `docs/research/existing-mods-exploration.md:223` (Expand World Data row) and
  `docs/research/idea-research.json:2432`, which feeds `docs/backlog.md:963`.
- Dungeon Creation Kit does not build "instanced areas": its README says the cave it builds is "way up in the sky"
  above an entrance (prior-art verification F9; https://thunderstore.io/c/valheim/p/MoonTower/Dungeon_Creation_Kit/).
  The claim ("Build ... instanced areas out of pieces") is in `docs/research/existing-mods-exploration.md:224` and
  `docs/research/idea-research.json:2438`, which feeds `docs/backlog.md:964`.
- The inspiration sentence "Borrow ... Dungeon Splitter's instancing" is in
  `docs/research/existing-mods-exploration.md:238` and `docs/research/idea-research.json:2441`, which
  `tools/Update-Backlog.ps1` copies into `docs/backlog.md:966`. Replace it with what can really be borrowed (its
  height-filter idea for performance, not instancing).
- `docs/backlog.md` is generated: fix `idea-research.json` (and `existing-mods-exploration.md`), then regenerate the
  backlog with `./tools/Update-Backlog.ps1`; never edit `backlog.md` by hand.
- `docs/game/core-engine.md` section 13: `Runtime.AddManifest` and the manifest writer are public and documented, not
  "tooling not public".
- `docs/game/combat.md` section 11: `m_enableHuntPlayer` is true only for the bosses up to Yagluth.

**Sheet cells** (you maintain the sheet): no cell changes yet for an investigation. If you choose a new idea row (D10),
add it yourself. When the mod ships, set its Status to `Implemented`.

---

## 9. Prior art and compatibility traps

| Mod | Version / date / licence | What it does | Lesson or trap |
|---|---|---|---|
| [Dungeon Splitter](https://github.com/JereKuusela/valheim-dungeon_splitter) (JereKuusela) | 1.9.0 2026-08-11, **deprecated**, Unlicense | Filters ZDO sync and instantiation by height (>= 1500 m); everyone above one spot still shares one dungeon | Not an instance system. Its code uses members 1.0.16 removed (`ZDOMan.m_objectsByOutsideSector`, `ZoneSystem.m_activeArea`). If a server runs it, piece-built floors need its "Always send" list |
| [SkadiNet](https://github.com/sighsorry1029/SkadiNet) (sighsorry) | 1.1.6 2026-09-18, GPL-3.0, AI-generated | 1.0 version of the same layer filter: off by default, hard-coded 1500 m, an always-send list | Our whole instance is above 1500, so it stays on one layer. Probe P11 with filtering on |
| [More World Locations AIO](https://github.com/jneb802/MoreWorldLocations_All) (warpalicious) | 5.1.7 2026-09-30, Jotunn 2.30.2, **All Rights Reserved** | 188 hand-made locations + 2 procedural dungeons; bundles with `JVLmock_`, kit clones (stripping per clone, default none), `Runtime.AddManifest` from an `EntryPointSceneLoader.Start` prefix | Proves the bundle + mocks pipeline on 1.0. Study only. Patches `DungeonGenerator.Generate`, `PlaceOneRoom`, `PlaceRoom`, `Location.Awake` |
| [Jotunn](https://github.com/Valheim-Modding/Jotunn) | 2.30.2 2026-09-21, MIT | Mocks, `ZoneManager`, `DungeonManager` (patches `DungeonDB.Start`, `GetRoom`, `SetupAvailableRooms`, `ZoneSystem.SetupLocations`), `TestMod/TestModDungeon.cs` example | We keep the `JVLmock_` naming. Both mods forcing `manifest_extended` is harmless |
| [Expand World Data](https://github.com/JereKuusela/valheim-expand_world_data) | 1.74.0 2026-10-01, Unlicense | YAML dungeons and rooms from in-game blueprints (server side). Also **blueprint locations**: a location's `prefab` can be a blueprint file name (`docs/locations.md` line 13), which places a hand-built layout as one fixed set of objects | Blueprint format reference; every object networked. Its dungeons go through a procedural `DungeonGenerator`, but a blueprint location is a fixed hand-built layout (placed at world generation or with `genloc` / Upgrade World), so EWD is also prior art for hand-made, non-instanced places |
| [Infinity Hammer](https://github.com/JereKuusela/valheim-infinity_hammer), [World Edit Commands](https://github.com/JereKuusela/valheim-world_edit_commands), Server Devcommands, Structure Tweaks | 1.86.0 / 1.79.0 / 1.115.0 / 1.37.0, Sept 2026, Unlicense | In-game placing of any prefab, room or location; `field=` (vanilla `HasFields`); invulnerability (`health -1`); `.blueprint` save | Dev-only authoring tools; patterns reusable (Unlicense) |
| [PlanBuild](https://github.com/sirskunkalot/PlanBuild) | 0.20.0 2026-10-01, WTFPL | `.blueprint` capture, `Piece` objects only | Import format |
| [location-tools](https://github.com/probablykory/location-tools) | no licence | Unity `ScriptedImporter` for `.blueprint` / `.vbuild` into the ripped project | Idea for our Unity importer; study only |
| [Dungeon Creation Kit](https://thunderstore.io/c/valheim/p/MoonTower/Dungeon_Creation_Kit/) (MoonTower) | 3.1.2 2026-09-27, closed, AI-generated | In-game cave building "way up in the sky", spawners, treasures; no bosses | UX reference only |
| [OdinsHollow](https://github.com/GraveofBears/OdinsHollowDungeonBuilder) | 2.2.3 2026-09-29, source without licence (GitHub API: no licence) | Random dungeon layouts since 2.2 (Thunderstore README). The layout string is saved on the location's `LocationProxy` ZDO (key `OH_DG_Layout`, `DungeonGen/OHDungeon.cs`, read and written around lines 64-128); room templates are instantiated locally from it; networked chests and spawners are spawned once and tagged `OH_DG_Id` (`DungeonGen/DungeonManager.cs` lines 62-66, 591-604) | Same split as ours (local geometry from saved data, few networked objects); study only |
| [Basements](https://github.com/elgthedev/Basements) (OdinPlus) | 2.0.0 2026-09-17, MIT | Placeable piece that teleports into a pocket interior. `Basements/Patches/Character_Patches.cs` is a Harmony postfix on `Character.InInterior(Transform)` that returns false for the local player while the environment is named "Basement". In 1.0.16 that overload is static (Character.cs 4366), and the postfix asks for `__instance`, so whether it still applies is unverified | Trap if it applies: an `InInterior` override changes our dungeon rules; cross-mod test |
| [ExtendedBosses](https://github.com/tbsj1ga/ExtendedBossesValheim) (j1gA) | 0.8.6 2026-09-26 on Thunderstore (GitHub main says 0.8.7), MIT, AI-generated, "early version" | Phases, adds, nests, shields, threat on seven vanilla bosses, Eikthyr to Fader, not Kall: `src/ExtendedBossesPlugin.Bosses.cs` on GitHub main has entries for `Eikthyr`, `gd_king`, `Bonemass`, `Dragon`, `GoblinKing`, `SeekerQueen` and `Fader`, `FIGHTS.md` has one section for each, and the 0.8.6 README says "from Eikthyr to Fader". (The verifier counted 5 entries in a partial copy of the source.) Owner-run controller, ZDO state in network time, reset (heals in place), orphan scan | Main reference for the boss controller (adapt with attribution); keys on vanilla prefab names, so our unique clone names avoid it |
| [HostOwner](https://github.com/tbsj1ga/HostOwnerValheim) | 0.1.0, MIT | Host takes ownership of chosen objects | Prior art for pinned ownership; not for dedicated servers |
| BossAdd (LJS) | 1.2.0, closed, AI-generated | Per-boss mechanics (Bonemass split in order) | Ideas only |
| [Enhanced Bosses Redone](https://github.com/mdf25/enhanced-bosses-redone) | 2023, no licence | Clones a boss's own items to reuse animations; HP thresholds in `m_aiMaxHealthPercentage` | Confirms the item-clone approach; study only |
| CreatureManager (sighsorry), [MonsterDB](https://github.com/RustyMods/MonsterDB) (RustyMods) | Thunderstore listing 2026-10-01: CreatureManager 1.2.4 (2026-09-30; no source link, only a Discord link, so treated as closed, licence unknown), MonsterDB 0.4.0 (2026-09-20; its GitHub repo has no licence) | YAML creature, attack and AI clones (CreatureManager README; MonsterDB description "Edit and clone Valheim creatures through YAML configuration") | What to expose in our JSON; study only |
| [StarLevelSystem](https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded) | 1.20.0 (top of `StarLevelSystem/Package/CHANGELOG.md`), GPL-3.0 | Level rerolls, boss modifiers, a location reset module (`StarLevelSystem/modules/LocationReset/`), and a public API with `SetCreatureSpawnManaged` (`StarLevelSystem/API/API.cs`) | Mark our bosses managed when it is present; study only |
| CLLC (Smoothbrain) | deprecated | Boss affixes, level rerolls | Can reroll or delete our creatures; cross-mod test if found |
| [ValheimFortress](https://github.com/MidnightsFX/Valheim_Fortress) | 0.37.2 (Jotunn 2.29.2, pre-1.0), GPL-3.0 | Wave arenas; counts living creatures by ZDOID | Pattern only |
| DreadRifts (Ketanol) | 0.1.23, closed, "no tests performed" | Rift arenas on real terrain | Not instanced (per-party unverified) |
| Mayheim Míticas (MayheimDEV) | 0.1.2 2026-09-26, closed | A key used outside a vanilla dungeon rebuilds it in place (nobody may be inside) with one mythic boss in the farthest room | Not instanced, not hand-made, but the best reference for the **run lifecycle**: start lock, respawn at the entrance with no tombstone, reward for everyone inside, boss HP +50 % per extra player, 5-day timeout, admin commands |
| [craigins/valheimmods](https://github.com/craigins/valheimmods) | 2026-09/10, MIT, GitHub only, "untested" | `CraiginsValheimInstances`: one copy of a procedural dungeon per instance in its own zone (zones 176-255, y 20000), non-persistent ZDOs, reaped 20 s after empty, exit intercepted, logout point rewritten. `CraiginsValheimOffMapDungeons`: Mörkhalla interiors moved to spaced off-map slots | The closest instancing prior art; reusable with attribution. Tombstones and items inside are lost on reap. At y 20000 walking AI has no navmesh (inference from `Pathfinding`) |
| BlightedWorldHeart (Wubarrk) | 1.0.7 2026-08-01 (pre-1.0), AI-generated | A custom raid boss at boss altars that summons biome sub-bosses at HP thresholds, with its own boss bar | Reference for multi-boss phases; not instanced |
| ChallengeHub Dream Bridge (Zeitsurfer) | 0.5.0 | A portal that saves the character and reconnects it to another dedicated server | Isolation by a separate server; rejected here (needs a second server per instance) |
| RtDLegends (Soloredis) | 1.3.65, forbids redistribution and decompiling | Procedural dungeons with token-summoned bosses | Do not read decompiled; README only |
| [Venture Location Reset](https://github.com/OrianaVenture/VentureValheim) | 1.1.1 2026-09-27, MIT, ~201k downloads | Resets sky locations (`m_hasInterior` or a generator above 4000) and deletes root objects at **y >= 4000** in their radius; skips while players are near | Our band stays below 4000; our entrance is not a location |
| Idavoll, FreshWorld, Dvala, MoreDungeonResets, ValheimResetNow | various | Dungeon or zone resets | FreshWorld restores zones and locations on a schedule: cross-mod test |
| Upgrade World | 1.83.0, Unlicense | `zones_reset`, `locations_add` | A zone reset can delete our entrance and anchors; reconcile re-creates them |
| Expand World Size | 1.42.0, Unlicense | Moves the 10000 / 10500 world edge | Breaks far-grid slots (M3); harmless for v1 |
| FastTeleport (GemHunter1), FastLoading (Ivvty) | Thunderstore listing 2026-10-01: 1.1.1 / 0.6.4 (2026-09-30) | Per their Thunderstore descriptions: FastTeleport shortens teleport loading and makes dungeon entry instant; FastLoading speeds up portal and dungeon-entrance loading while keeping the game's arrival checks | Must not skip `IsAreaReady`; cross-mod test (FastTeleport's instant dungeon entry is the risky one; how it does it is unverified) |
| FiresEasyBakeMeshes (VerdantsAscent) | 1.2.15, 2026-09-09 (Thunderstore listing) | Per its description: merges a zone's structural pieces into combined meshes and, on multiplayer clients, stops creating the objects the bake already draws | Could interfere with networked pieces (doors, chests, spawners); cross-mod test |
| Dungeonheim (Nexus 1997), Helheim (Nexus 2360) | world saves | Hand-built worlds with dungeons and bosses | Not plugins; details unverified (Nexus blocks automated reads) |

---

## 10. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Floor colliders not on `FindFloor` / navmesh layers | Entry bounces ("portal blocked"); bosses stand still (`BaseAI.MoveTo` calls `StopMoving` when `FindPath` fails) | `static_solid` by default; validator; probes P2, P3 |
| Teleport arrives before the shell is built | Bounce back | Build synchronously in the anchor's `Awake`, or hold the zone with `SetLoadingInZone` (keeps `IsAreaReady` false) |
| Zone hold never released (exception in the build, missing bundle or AssetID, anchor destroyed mid-load) | Black screen on entry (`Player.UpdateTeleport` waits for `IsAreaReady` with no timeout); endless loading screen at login at the exit point (`Game.FindSpawnPoint`); hung portal trips into the zone; surface objects in the entrance zone never appear (`IsZoneReadyForType`) | Hard rules of section 3.4: one `held` flag, release in `try/finally` and `OnDestroy` (never twice: `UnsetLoadingInZone` throws on a second call), 20 s timeout that releases and logs, hold only for walkable floors; self-test `dungeon.anchor-load-failure` |
| Provisional entry rolled back while the teleport is still pending | Player arrives inside after the server rolled the entry back | 30 s expiry (longer than the hold timeout); a late `Arrived` is re-validated and committed, or the player is evacuated (section 3.2) |
| Two players engage in the same sync interval | Doubled boss set, `activeBosses` counted twice, two writers on the controller | Engage granted by the server, one peer per attempt id; the owner re-checks `IsOwner()` and the attempt id before spawning (section 3.8); multiplayer test item |
| Shell components borrow the anchor's `ZNetView` (`GetComponentInParent<ZNetView>`) | `Awake` throws on duplicate RPC registration; shared `MatVar`/`RandMatSeed` keys; writes to the "nobody writes" anchor | Shell under a separate root with no `ZNetView`; borrowers stripped before activation; RPC-dependent pieces become networked objects (section 3.4) |
| Our SoftRef call runs before the vanilla loader exists | Other mods' `Runtime.AddManifest` silently refused (More World Locations AIO loses its locations) | No SoftRef getter before the loader exists; check in a `FejdStartup.Awake` postfix, cached; cross-mod test with MWL (section 3.1) |
| Seal doors broken by players or boss AoE | Players escape a sealed arena | `indestructible` mandatory on every seal entry, enforced by Validate and the catalog (section 3.8) |
| World level enlarges bosses indoors | Bosses stuck in doors or ceilings sized for their agent | Clearance sized for agent x `scale` x (1 + 0.2 x world level), or `maxWorldLevel` per dungeon (section 4.3 Part B step 4) |
| Forced `EnvZone` destroyed with the player inside | Forced environment stuck after a reload or purge | Builder clears it with `SetForceEnvironment("")` on teardown (section 3.7) |
| Non-persistent anchors deleted by the host (`ZNetScene.RemoveObjects`) | Floor vanishes under clients | Persistent anchors (decided); self-test `anchors-host-away` |
| New persistent ZDOs not saved (dirty-chunk order) | Entrances or the registry lost after restart | Create in the `ZNetView.Awake` order; save/reload test |
| `activeBosses` leak on wipe, reset, despawn or purge | With `NoBossPortals`, every portal in the world blocked | Decrement on every removal of a boss whose `s_bossCount` is set: wipe, reset, `despawn`, `dungeon remove ... purge` and `dungeon purge` (section 7.2) |
| Ownership handover mid-fight | Cooldowns reset; specials fire at once | All timers and the running action in the ZDO; the granted peer spawns and owns the bosses at engage |
| Surface player owns the boss (XZ ownership, possibly zones away) | Laggy fight | Spawned by the engaged player inside whom the server granted; handovers keep state |
| Several `m_boss` at once | Overlapping bars, one boss event | `EnemyHud` stacking patch; one `m_bossEvent` at a time |
| Missing animator trigger | Silent no-op attacks | Load-time check with `ZSyncAnimation.HasParameter`, suffixes expanded |
| `SpawnAbility` indoors | Adds on the roof, or at y 0 | Low `m_getSolidHeightMargin`; spawn radius inside the floor |
| Stripped vanilla prefabs used as decoration | Wrong lights or LOD; gameplay side effects (`EffectArea` PlayerBase blocks spawners) | Strip list; per-family probe P4; prefer kit SoftRef prefabs (no `MonoBehaviour`s) |
| Asset loading cost | Loading one kit piece loads its whole vanilla bundle | Reference-counted `Utils.Instantiate` per anchor; release on destroy |
| Name ambiguity and game patches | 143 duplicate file names; paths may move between patches | Store AssetIDs (stable Unity GUIDs per the Iron Gate FAQ); re-run Validate and `dungeon.data` after each `Update-GameRefs.ps1` |
| Bundle and engine version | Newer-editor bundles do not load | Unity 6000.0.75f1 only; rebuild when the game's Unity version changes |
| Ripped asset slips into a bundle | Redistributing game content | Validate fails the build on any rip dependency |
| Data differs between peers | Different walls on different games | Content hash of the JSON files (with `bundleHash`) in the server rules; each client checks its bundle against `bundleHash`; dungeon sealed on mismatch. The server needs the same JSON files, not the bundles (section 5) |
| Bundle file locked by the running game (unverified) | A Unity export cannot overwrite the bundle during hot reload | Debug loads a temporary copy, never the `DataFolder` file (section 4.3 Part D); probe P5 checks it |
| Tombstones and dropped items at reset or purge | Lost player items | Always move them to the entrance; never delete |
| Mod removed | Inert anchor, entrance and registry ZDOs (destroyed by a host, kept by a dedicated server); instance creatures fall to the terrain below | `dungeon purge` before uninstall; logout point already at the entrance |
| Mod removed while a boss is alerted | `activeBosses` stays raised forever: a host destroys the unknown boss ZDO without `OnDeath`, a dedicated server keeps it but nobody can load it; with `NoBossPortals` every portal stays blocked | `dungeon purge` lowers the counter for each counted boss; the README uninstall section says purge is required for this reason; self-test `dungeon.purge`. If it already happened: an admin runs the vanilla `removekey activeBosses` (Terminal.cs 575-586, server only); a missing key counts as no active boss for `TeleportWorld` (TeleportWorld.cs 130) |
| Venture Location Reset, zone-reset tools | Instance content wiped | Band below 4000; reconcile re-creates entrances and anchors |
| Surface spawns under an occupied dungeon | Creatures on the ground below (vanilla does the same) | Accept (vanilla-consistent); optional `SpawnSystem` skip later |
| Environment override order | AltBiome or event environment wins over our non-forced `EnvZone` | Probe P9; `force` option per dungeon |
| EnvMan Deep North camera-y quirk (inference) | Deep North weather far east or west | `EnvZone` covers the interior; placement warns when \|x\| > 8800 until probe P9 settles it |
| Instance visible or costly from the ground | GPU cost for surface players | `RenderGroupSubscriber` with group Interior (probe P12) |
| Registry spot ownership (unverified) | Registry writes lost | Server-only writes; probe P13; sidecar fallback |
| Licences | GPL or all-rights-reserved code copied by mistake | Copy only MIT, Unlicense or WTFPL code, with attribution; others study-only |
| AI-generated, untested prior art | Wrong assumptions | Treat their READMEs as claims, not facts |
| Scope | "Very hard" idea; art-heavy | Probes first; one dungeon MVP |

---

## 11. Open decisions for the user

| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | What does "instance" mean for you? | (a) one shared copy per entrance with a run lifecycle and reset; (b) (a) plus a "one party at a time" lock; (c) true per-party copies (far slot grid) | (a) for v1, with (b) as a config option. Plan (c) only if two groups must run the same dungeon at the same time |
| D2 | Where does the copy live? | (a) the column above the entrance zone, y 3200-3950; (b) the far northern grid (44 slots) | (a). (b) only with D1 (c), after probe P14 |
| D3 | Main authoring path | (a) Unity rip + mocked bundle; (b) in-game build + capture; (c) both, Unity for final art | (c) |
| D4 | Jotunn | (a) no, own resolver with `JVLmock_` naming; (b) hard dependency | (a) |
| D5 | Simultaneous bosses | (a) `EnemyHud` stacking patch and several `m_boss`; (b) one `m_boss` at a time, lieutenants without bars | (a) for bars, plus only one active `m_bossEvent` |
| D6 | Seal | (a) seal arena doors and barriers on engage until clear or wipe; (b) no seal (vanilla-like) | (a); the dungeon exit outside arenas stays usable |
| D7 | Reset, lockouts and loot | Reset after N empty minutes; lockout per character, per account or none; loot chest per run or per player | Reset 30 min empty / 120 min after clear; no lockout by default (per character when on); one chest per run |
| D8 | Logout inside | (a) always back at the entrance; (b) resume inside while the run is valid | (a) for v1 (never strands anyone); (b) later |
| D9 | Placements file location | (a) next to the world save, derived from `World.GetDBPath()` (follows local/legacy sources and `-savedir`), with a `BepInEx/config` fallback for cloud worlds; (b) always `BepInEx/config/<GUID>/worlds/` | (a); self-tests then write inside the isolated probe save (section 8) |
| D10 | Sheet idea | (a) `Big unique dungeon`; (b) a new row ("Dungeon instances") | (b): the request (instances, many dungeons, multi-boss) differs from "one unique dungeon per biome"; list both in `ModIdea` if you agree |
| D11 | Boss animations | (a) vanilla rigs, item clones, runtime override controllers; (b) new rigs from Unity | (a) for v1 |
| D12 | Unity sources in git | (a) our own assets + editor tools in `unity/` (LFS optional); (b) outside the repo | (a), never the rip |
| D13 | Release scope | (a) your own world or server only; (b) Nexus with auto-placement | (a) first |
| D14 | Ore and metals through entrances | (a) allowed (vanilla `Teleport`); (b) refused | (a) |
| D15 | Exit under `NoBossPortals` | (a) refuse while an alerted boss with a boss event is inside the dungeon, as vanilla doors do; (b) always allow | (a), vanilla-consistent (section 3.5); flagged here because it makes our exits stricter than "outside arenas stays usable" in D6 on such worlds |
| D16 | Entrance as a vanilla location | (a) plain ZDO entrance; (b) custom location through `SpawnLocation` + `RegisterLocation` (needs our own SoftRef manifest, one per zone) | (a) (section 7.1) |

---

## 12. Milestones

### 12.1 Probes first (throwaway probe mods under `tests/Probes/`, never shipped)

| Probe | Retires | Pass criteria |
|---|---|---|
| **P1 SoftRef kit** | Loading vanilla pieces by AssetID | `MakeAllAssetsLoadable` in `BindConfig` and no other SoftRef call before a `FejdStartup.Awake` postfix runs the `manifest_extended` check; with More World Locations AIO installed, its manifest is still accepted (no "AddManifest() must be called before" error); `SunkenKit_int_wall_1x4` + `SunkenKit_int_floor_4x4` instantiated at y 3500; renders correctly; layer 15; `FindFloor` hits; the bundle unloads when the instance is destroyed |
| **P2 Enter and exit** | Teleport, interior rules, environment | Entrance ZDO created by the server; persistent anchor builds a floor; non-distant teleport arrives at y 3500 in under 5 s; `InInterior` true; terrain and grass hidden; `EnvZone` environment active; exit back; missing floor gives "portal blocked", not a fall |
| **P3 Navmesh** | AI on custom geometry | Troll and FrozenKing clones path across box and mesh colliders and through a 3 m door; ramps work; logs `Pathfinding` layers |
| **P4 Stripped decoration** | Vanilla prefabs as visuals | `blackmarble_*`, `CastleKit_brazier`, `Morkhalla_Floor_4x4`, a vanilla room: lights, LODs and worn states look right; no `EffectArea` / `Piece` side effects |
| **P5 Mocked bundle** | The Unity pipeline end to end | A 6000.0.75f1 bundle with `JVLmock_` children and a `JVLmock_Custom/Piece` material renders like vanilla; no pink; no ripped file in the bundle; while the game holds a bundle loaded with `LoadFromFile`, check whether a Unity export can overwrite that file (the lock question of Part D), and that the Debug temp-copy load avoids it |
| **P6 Host walks away** | Anchor persistence and save | Host leaves 300 m while a client is inside: the floor stays; save and reload keeps entrances and anchors |
| **P7 Boss ownership** | Handover, state and engage arbitration | Two clients; the boss owner leaves mid-phase: phase, timers and a running scripted action continue from the ZDO; reset balances `activeBosses`; both clients enter the arena trigger in the same frame: one grant, one boss set |
| **P8 Multi-boss HUD** | Bars and boss events | Two alerted `m_boss` creatures: confirm the overlap, then the stacking patch |
| **P9 Environment priority** | `EnvZone` vs boss event vs AltBiome | Non-forced and forced zones under a custom boss `RandomEvent` (all biomes); music and environment as intended |
| **P10 Unused triggers** | Free extra moves | `attack_chain_sweepL/R`, `attack_taunt` (FrozenKing), `attack_lightningstorm` (JotunWitch), `attack_beam` / `attack_throw` (Charred) fire and hit |
| **P11 SkadiNet layer filter** | Teleport across layers | With `DungeonLayerFiltering` on, entry still works |
| **P12 Seen from below** | Render cost | Frame time on the ground under a large instance, with and without Interior render-group subscribers |
| **P13 Registry ZDO** | World state store | Server-only writes survive a join, a save and a reload; nobody else takes ownership |
| **P14 Far slot** (only if D1 (c)) | Per-party copies | Northern crescent slot: zone generation, water, edge kill, minimap, distant teleport with `ForceSendZDO`, relog redirect |
| **P15 Payload without animation** | `Attack.StartWithoutAnimation` | `SpawnAbility` and `Aoe` payloads fire while another animation plays |
| **P16 Field overrides and ghost init** | `ZNetView.LoadFields` order; server-created objects | A ghost-initialised `Spawner_*`, `TreasureChest_*` and door with `HasFields` keys: on a client, the overrides are in effect (the order of `ZNetView.Awake` against the other components' `Awake` is Unity script-order data, not in the decompile); no `cheated` key; a dedicated server creates them without errors |

### 12.2 MVP (one dungeon, one boss)

1. Scaffold with `./tools/New-Mod.ps1` (Exploration, `Dungeon.Instances`, Both).
2. `PlayerCheck` / rules, registry, catalog, generic prefabs, entrance + anchors + builder (kit, room, prefab and bundle
   shells, separate shell root, zone-hold rules), exit (with the `NoBossPortals` rule), logout patch.
3. `dungeon place`, `list`, `remove`, `reset`, `reload`, `info`, `purge`.
4. One encounter: a `FrozenKing` or `Charred_Melee` clone, server-granted engage, 3 HP phases, adds, indestructible
   seal, wipe reset, `activeBosses` bookkeeping (wipe, reset and purge).
5. Self-tests (section 8); smoke test; `TESTING.md` with single-player and multiplayer items, plus a hand-off item (a
   player without the mod is refused; a client toggles off while inside).

Rough size: engine about 2 to 3 weeks, encounter core 3 to 5 weeks (report estimates).

### 12.3 Authoring pipeline

The MC Dungeon Kit (New, Validate, Export, navmesh preview, importers), MCP wiring (with your OK), and `dungeon capture`
/ `import`. The first dungeon is authored by you in Unity.

### 12.4 Content and lifecycle

- Multi-boss modes (sequence, linked, council), HUD stacking, lockouts, tombstone moves, map pins, `dungeon suggest`,
  hot reload.
- Then your dungeons, placed in your world with Claude.
- Later: per-party slots (D1 (c)), auto-placement and Nexus release.

---

## 13. Sources

**Game code** (`.ref/decompiled/assembly_valheim/`, Valheim 1.0.16):

- `Character` (`InInterior`, `OnDeath`, `SetLevel`, `TeleportTo`, `RPC_TeleportTo`, `UpdateGroundContact`,
  `UnderWorldCheck`)
- `Location` (`Awake`, `GetLocation`, `IsInsideActiveBossDungeon`); `LocationProxy`
- `ZoneSystem` (`SpawnLocation`, `SpawnLocationMidGame`, `FindFloor`, `GetSolidHeight`, `GetGroundHeight`, `GetZone`,
  `SectorToIndex`, `SetLoadingInZone`, `IsZoneLoaded`, `m_solidRayMask`)
- `ZNetScene` (`IsAreaReady`, `CreateObjectsSorted`, `RemoveObjects`, `PointInsideActiveArea`)
- `ZDOMan` (`ReleaseNearbyZDOS`, `FindSectorObjects`, `CreateNewZDO`, `DestroyZDO`, `ForceSendZDO`, save filter);
  `ZDO` (`Initialize`, `Persistent`, `Save`)
- `ZNetView` (`Awake`, `LoadFields`, `m_forceDisableInit`); `ZNet` (`RPC_RemoteCommand`, `IsAdmin`); `Terminal`
  (`ConsoleCommand`, `IsCheatsEnabled`)
- `Player` (`TeleportTo`, `UpdateTeleport`, `EdgeOfWorldKill`, `SetupPlacementGhost`, placement `NotInDungeon`,
  `GetPlayersInRangeXZ`); `PlayerProfile.SaveLogoutPoint`; `Game` (`FindSpawnPoint`, `GetPlayerDifficulty`,
  `RPC_RegisterKill`)
- `Teleport`, `TeleportWorld`, `Chat` (`RPC_TeleportPlayer`)
- `DungeonGenerator` (`PlaceRoom`, `Save`, `Load`, `LoadRoomPrefabsAsync`), `DungeonDB`
- `Pathfinding` (`SetupAgents`, `BuildTile`, `GetTilePos`)
- `RenderGroupSystem`, `Heightmap`, `ClutterSystem`, `EnvMan` (`GetEnvironmentOverride`, `AppendEnvironment`),
  `EnvZone`, `RandEventSystem` (`GetForcedEvent`), `RandomEvent.InEventBiome`
- `BaseAI` (`UpdateAI`, `CanUseAttack`, `SetAlerted`, `MoveTo`), `MonsterAI` (`UpdateAI`, `SelectBestAttack`),
  `Humanoid` (`GiveDefaultItems`, `EquipBestWeapon`, `OnAttackTrigger`, `BlockAttack`)
- `Attack` (`Start`, `StartWithoutAnimation`, `OnAttackTrigger`), `SpawnAbility`, `TeleportAbility`,
  `TriggerSpawnAbility`, `TriggerSpawner`, `Aoe`, `EnemyHud`, `CharacterDrop`, `CreatureSpawner`, `Container`, `Door`,
  `WearNTear`, `TombStone`, `WaterVolume`, `WorldGenerator`, `SpawnSystem`, `ZSyncAnimation`, `LevelEffects`
- Re-checked in the 2026-10-02 review pass: `ZoneSystem` (`IsZoneLoaded`, `IsZoneReadyForType`, `SetLoadingInZone`,
  `UnsetLoadingInZone`, `SpawnLocation`, `TestSpawnLocation`, `SpawnLocationMidGame`, `RegisterLocation`,
  `CreateGhostZones`), `ZNetScene.IsAreaReady` / `CreateObjectsSorted`, `Player.UpdateTeleport` / `EdgeOfWorldKill`,
  `Game.FindSpawnPoint`, `LocationProxy.OnDestroy`, `ZDO.SetOwner`, `ZDOMan.RPC_ZDOData` / `ReleaseNearbyZDOS`,
  `ZNetView.Awake` / `Register` / `ClaimOwnership`, the `GetComponentInParent<ZNetView>` users (`OfferingBowl`,
  `MaterialVariation`, `RandomMaterialValues` and others), `EnvZone`, `EnvMan` (interior vertex light),
  `Character.Awake` (world-level scale), `Game.m_worldLevelEnemyMoveSpeedMultiplier`, `Teleport.Interact`,
  `Location.IsInsideActiveBossDungeon`, `EnemyHud.GetActiveBoss`, `RandEventSystem.GetBossEvent`, `Destructible` /
  `WearNTear` `RPC_Damage`, `Aoe.m_hitProps`, `MonsterAI` (`Sleep`, `Wakeup`, `UpdateTarget`, `m_updateWeaponTimer`),
  `Character.m_aiSkipTarget` / `m_aiCannotTargetOthers`, `SaveSystem.GetWorldsSaveRootPath`, `World.GetDBPath`,
  `Terminal` (`removekey`), `EntryPointSceneLoader.Start`; `assembly_utils` `Utils.GetSaveDataPath`,
  `FileSourceHelper.IsLocal`

**SoftRef** (`.ref/decompiled/SoftReferenceableAssets/`): `Runtime` (`Loader`, `MakeAllAssetsLoadable`,
`AddManifest`), `AssetBundleLoader.InitializeDataSide`, `SoftReference`, `Utils.Instantiate`, `AssetBundleManifest`.

**Game data, read locally on 2026-10-01**:

- `E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/manifest` (587 assets) and
  `manifest_extended` (21,886 assets)
- Bundles `c4210710` (bosses, items, animators), `5b5d26ec` (SunkenKit), `17245031` (`main.unity`, `Pathfinding`
  values), `e06fccc7` (`DN_Bossroom`), `ba621cac` and `cd0f218` (Queen)
- `valheim_Data/globalgamemanagers` (layers, collision matrix)
- The dedicated server's `assembly_valheim.dll` (`Game` reference position)

**Repo**: `CLAUDE.md`; `docs/game/exploration-world.md` (section 5, "Big unique dungeon"); `docs/game/core-engine.md`
(sections 2, 7, 13, 15); `docs/game/combat.md` (section 11); `docs/modding/framework.md`; `docs/modding/toolchain-research.md`;
`docs/backlog.md`; `docs/research/existing-mods-exploration.md`; `src/Shared/Framework/ModPlugin.cs`;
`src/Combat/Sneak.Ambush/SmokeContent.cs` and `Patches/ZNetScenePatches.cs`; `src/Exploration/Swimming.Dive/PlayerCheck.cs`;
`src/Exploration/DeepNorth.Awakening/*`; `Directory.Build.props` and `.targets`; `tools/Package-Mod.ps1`;
`tools/Test-InWorld.ps1`.

**Web** (read 2026-10-01):

- Valheim-Modding wiki: https://github.com/Valheim-Modding/Wiki/wiki/Valheim-Unity-Project-Guide and the Layers and
  shader pages
- Unity: https://docs.unity3d.com/6000.3/Documentation/Manual/AssetBundlesIntro.html ,
  https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetBundle.LoadFromFile.html ,
  https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Shader.Find.html ,
  https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorOverrideController.html ,
  https://unity.com/releases/editor/whats-new/6000.0.75f1
- Iron Gate and the EULA: https://www.valheimgame.com/support/modding-faq-for-the-asset-bundle-update-0-217-40/ ,
  https://www.valheimgame.com/eula/
- Tools and libraries: https://github.com/AssetRipper/AssetRipper ; https://github.com/CoplayDev/unity-mcp ;
  https://github.com/Valheim-Modding/Jotunn (and https://valheim-modding.github.io/Jotunn/tutorials/asset-mocking.html ,
  `networkcompatibility.html`)
- Mods: every mod URL in section 9, plus https://github.com/craigins/valheimmods (README "Instanced dungeons",
  `src/CraiginsValheimInstances/InstanceRegion.cs`), https://thunderstore.io/c/valheim/p/MayheimDEV/MayheimMiticas/ ,
  https://thunderstore.io/c/valheim/p/j1gA/HostOwner/ ; the Thunderstore Valheim API listing
  (https://thunderstore.io/c/valheim/api/v1/package/)
- Read 2026-10-02 with `gh api`: `CoplayDev/unity-mcp` `MCPForUnity/package.json` at tag `v9.6.6` and `README.md`;
  `elgthedev/Basements` `Basements/Patches/Character_Patches.cs`; `GraveofBears/OdinsHollowDungeonBuilder`
  `DungeonGen/DungeonManager.cs` and `DungeonGen/OHDungeon.cs`; `MidnightsFX/Valheim_Star_Levels_Expanded`
  `StarLevelSystem/API/API.cs` and `Package/CHANGELOG.md`; `RustyMods/MonsterDB` (licence); `tbsj1ga/ExtendedBossesValheim`
  `src/ExtendedBossesPlugin.Bosses.cs` and `FIGHTS.md`; `JereKuusela/valheim-expand_world_data` `docs/locations.md`;
  `JereKuusela/valheim-infinity_hammer` `scalable_objects.md`; More World Locations AIO `AssetBundles.cs`
- Unity manual, Git dependencies: https://docs.unity3d.com/6000.0/Documentation/Manual/upm-git.html
- Jotunn prefab list (generated from Valheim 1.0.7): https://valheim-modding.github.io/Jotunn/data/prefabs/prefab-list.html

---

## Appendix: load-bearing claims (for verification)

| Id | Claim | Evidence |
|---|---|---|
| K1 | `InInterior` is y > 3000; the AI navmesh spans y -500 to 5500; Venture Location Reset deletes root objects at y >= 4000 near sky locations; so the band y 3200 to 3950 is interior, walkable and safe from that reset | `Character.InInterior(Vector3)`; `Pathfinding.GetTilePos` (2500), `BuildTile` (6000-tall bounds); Venture `LocationReset.cs` `LOCATION_MINIMUM = 4000f` |
| K2 | Loading, ownership and natural spawning ignore height; ownership goes to the first peer whose area (1.5 zones Chebyshev at the default level) contains the object, not the nearest one | `ZNetScene.PointInsideActiveArea` (y = 0); `ZDOMan.ReleaseNearbyZDOS` (ZDOMan.cs 967-993); `SpawnSystem.InsideZone` |
| K3 | A non-distant teleport waits for `IsAreaReady` (3 x 3 zones) **with no timeout**, and then needs `FindFloor` on `m_solidRayMask`; with no floor it returns the player with `$msg_portal_blocked`. A login at the logout point also waits for `IsAreaReady` with no timeout. So a zone hold (`SetLoadingInZone`) that is never released hangs entry and login | `Player.UpdateTeleport` (Player.cs 5935); `Game.FindSpawnPoint` (Game.cs 534-566); `ZNetScene.IsAreaReady` (ZNetScene.cs 156-174); `ZoneSystem.IsZoneLoaded` (1296-1303); `ZoneSystem.FindFloor` |
| K4 | Non-persistent ZDOs owned by a host are destroyed when they leave its area, so anchors must be persistent | `ZNetScene.RemoveObjects` |
| K5 | Kall's `DN_Bossroom` is a hand-built interior with no `DungeonGenerator`, the vanilla precedent | Bundle `e06fccc7` dump: `m_generator` null, `Interior` at y 5002 |
| K6 | `ZNetView.LoadFields` applies `HasFields` overrides on every peer, including unmodded ones | `ZNetView.Awake`, `LoadFields` |
| K7 | Any `manifest`/`manifest_extended` asset loads by name with public API after `Runtime.MakeAllAssetsLoadable`, provided no SoftRef getter runs before the vanilla loader exists (the first getter creates the loader, after which `AddManifest` and `MakeAllAssetsLoadable` only log an error) | `AssetBundleLoader.InitializeDataSide`; `Runtime.Loader`, `MakeAllAssetsLoadable`, `AddManifest` (SoftReferenceableAssets/Runtime.cs 18-57); `SoftReference`; `Utils.Instantiate` |
| K8 | Kit pieces such as `SunkenKit_int_wall_1x4` contain no `MonoBehaviour` and sit on `static_solid`, a layer in `Pathfinding.m_layers` (Default, piece, terrain, static_solid, Default_small, blocker, pathblocker) and in `FindFloor`'s mask | Bundle `5b5d26ec` decode; `main.unity` in bundle `17245031`, decoded with a scratch reader (not checked in game; probe P3 logs the layers at runtime); `ZoneSystem.m_solidRayMask` |
| K9 | Many vanilla components without their own `ZNetView` use `GetComponentInParent<ZNetView>()`, and `ZNetView.Register` throws on a duplicate name, so shell geometry must not hang under a networked anchor | grep of `.ref` (OfferingBowl.cs 92, MaterialVariation.cs 35, RandomMaterialValues.cs 55, and 14 more); `ZNetView.Register` (ZNetView.cs 273-306) |
| K10 | Monster attacks are items gated by `BaseAI.CanUseAttack` health windows; vanilla 1.0 bosses phase through them | `Humanoid.EquipBestWeapon`; `BaseAI.CanUseAttack`; bundle `c4210710` dump |
| K11 | On an ownership handover, private `MonsterAI` state and all attack cooldowns are lost; the ZDO state survives | `BaseAI.UpdateAI`; `Humanoid.Start`; `Attack.Start` (`m_lastAttackTime`) |
| K12 | `activeBosses` goes up in `BaseAI.SetAlerted` and down only in `Character.OnDeath`, so every non-death removal of a counted boss (wipe, reset, despawn, purge, a host destroying an unknown-prefab ZDO) must lower it by hand | grep of both writers; Character.cs 2994-2998; `ZNetScene.CreateObjectsSorted` (ZNetScene.cs 229-235); `TeleportWorld` line 130 |
| K13 | Two peers that claim the same ZDO within one sync interval can both believe they own it: `SetOwner` only bumps `OwnerRevision`, and a received owner wins only with a strictly greater revision or newer data. Engage therefore needs a server grant | `ZNetView.ClaimOwnership`; `ZDO.SetOwner` (ZDO.cs 1373-1380); `ZDOMan.RPC_ZDOData` (ZDOMan.cs 1142-1200) |
| K14 | The server can move any player, even without the mod, via `RPC_TeleportPlayer` | `Chat.Awake` registration, `Chat.TeleportPlayer` |
