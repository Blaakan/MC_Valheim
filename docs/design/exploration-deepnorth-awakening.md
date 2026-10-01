# Deep North Awakening — design

| | |
|---|---|
| Mod | Deep North Awakening |
| GUID / project | `MC.Exploration.DeepNorth.Awakening` (`src/Exploration/DeepNorth.Awakening/`, root namespace `MC.Exploration.DeepNorthAwakeningMod`, package `DeepNorthAwakening`) |
| Category / scope | Exploration / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Deep North revamp` |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/` (PersistentEventSystem, TriggerPersistentEventOnDestroy, Destructible, SpawnSystem, BaseAI, Character, EnvMan, ZoneSystem, ZDOMan, ZRoutedRpc, MessageHud, Terminal, Minimap, WorldGenerator, AltBiomeWorldData); the 1.0.16 asset bundles decoded on 2026-10-01 (the `jotun_invasion` event, `BlackIce_Start`, `BlackIce_Core`, `_SpawnList_DeepNorth`, creature factions); the Valheim wiki and valheim.gaming.tools (2026-10-01); sources of Expand World Data, Spawn That, DropNSpawn, StarLevelSystem, ServersideQoL and World Advancement Progression on GitHub (2026-10-01) |
| Status | Implemented (v0.1.0 code); smoke test passed 2026-10-01; in-world self-tests passed 2026-10-01 on the final code (all 9 `dn.*` tests; an earlier run failed the live part of `dn.hostility` because the test spawned the two creatures back to back, fixed in the test); two adversarial review rounds (19 + 2 confirmed findings, all fixed); not yet tested by hand in game |

## Goal

The user's expected behaviours (run request and two rounds of answers, 2026-10-01), numbered. Each one is scope and
has at least one test (section 8).

1. **G1 — Dormant first.** Until the first Malicious Ice of a Mörkhalla is broken, the Deep North is exactly vanilla
   (few creatures, "stasis").
2. **G2 — First stone awakens the north.** Breaking the first Mörkhalla stone awakens the Deep North instead of
   starting a vanilla Jotun invasion somewhere in the world: invaded areas appear inside the Deep North (about 20 % of
   it) where many of the Jotun army spawn: Krigen (sword or greataxe), Krigen (dual axes), Hexen and Elaking.
3. **G3 — Second stone.** More areas (about 40 %) and higher odds of 1-star Jotun. Still no vanilla invasion.
4. **G4 — Third stone.** About 70 % of the Deep North, higher odds of 2-star Jotun, and 3 vanilla Jotun invasions start
   in the world.
5. **G5 — Later stones.** Each further stone starts 1 vanilla invasion, as in vanilla (the vanilla cap of 3 running at
   once still applies).
6. **G6 — Hidden and permanent areas.** The Deep North areas have no map pin or marker, no Malicious Ice at their
   centre, and cannot be stopped or destroyed (round 4: they can show on the map as a tint, G13).
7. **G7 — Stars scale with the world.** The per-stage star odds go through the vanilla level-up formula, so the world
   modifiers (Enemy level-up rate, World level) still scale them.
8. **G8 — Storms come and go.** An area storms about a third of the time, 5 to 10 minutes at a time: Deep North
   blizzard weather while you stand in it, and Fimbul meteors like the vanilla invasion's.
9. **G9 — Nature fights back.** Once the north is awake, Gammeltroll, Barka and the frost Greydwarfs (and their
   shamans) are hostile to the Jotun army, and the areas sometimes spawn a band of them to fight the invaders: 0-2 big
   ones (Gammeltroll and Barka, any mix) and 5-10 frost Greydwarfs including shamans (user, round 3).
10. **G10 — After Kall.** Once Kall Fimbulbringer is defeated, the areas stop spawning new mobs; killing every Jotun of
    an area clears it for good (no more Jotun, storm or meteors there). Areas outside the loaded range must still have
    their enemies when visited after Kall (user, round 3). The rule (user, round 3): when an area comes into range, its
    creatures spawn (if Kall is defeated) and the area is flagged "to be defeated", which stops further spawns while
    players are in it; cleared = deleted; not cleared = its mobs spawn again the next time it is loaded.
11. **G11 — Old worlds.** On a world where Mörkhalla stones were broken before the mod was installed, the server counts
    them once (explored Mörkhalla whose Malicious Ice is gone) and starts at that stage, without catch-up invasions.
12. **G12 — Vanilla messages.** Every stone shows the vanilla "The Jotun Advance" (user, round 3).
13. **G13 — Areas on the map** (user, round 4). The invaded areas show on the map and the minimap, like a vanilla event
    area but purple. Areas that overlap merge into one region (colours never add up, no separate circles), which
    clearing areas chips away. Hidden by the fog of war; can be turned off in the config.

### Added beyond the request (small)

- **E1** Admin console command `deepnorth_stones [count]`: shows the stone count, stage and areas, or sets the count
  (no messages, no invasions). For admins fixing a world and for testing.
- **E2** The vanilla "The Jotun Retreat" (`$fimbulvinterorb_destroyed`, the invasion core's text) when a player clears
  the area they stand in after Kall.
- **E3** An admin's `pevents start jotun_invasion` (and any other mod's request) still starts an invasion, 5 seconds
  late (decision D4).

### Non-goals

Map pins, icons or a compass for the areas (G13 is a tint only); new creatures, items or locations; changing the vanilla
invasions themselves, the Mörkhalla dungeon, the vanilla Jotun patrols or the `army_jotuns` raid; heavy snow on
buildings; new music.

### Later (cut from v1, one-line sketches)

- Areas that grow outward from the broken Mörkhalla instead of a world-seeded pattern.
- A Deep North variant of the vanilla invasion sky (`JotunInvasion_deepnorth` does not exist; an `EnvMan` clone of
  `JotunInvasion_mountain` could add one).
- Area-specific loot (Jotun drop more Malicious Blood in invaded areas).

## 1. Vanilla behaviour

### 1.1 The Mörkhalla stone and the invasion

- The stone is the prefab **`BlackIce_Start`** ("Malicious Ice", 1000 HP, weak to chop, pickaxe and fire), placed in
  the Mörkhalla end rooms `morkhalla_endcap01/02`. Its `TriggerPersistentEventOnDestroy` has `_eventInternalName =
  "jotun_invasion"`, `_stopEvent = false` and `_centralTextOnTriggered = "$fimbulvinterorb_start"` ("The Jotun
  Advance"). Mörkhalla location: `MorkBorg`, up to 40 per world, Deep North only (asset bundles; wiki).
- `Destructible.RPC_Damage` runs on the stone's ZDO owner (usually a client). At 0 health `Destructible.Destroy` runs the
  effects, then `m_onDestroyed`, then `ZNetScene.Destroy`. `TriggerPersistentEventOnDestroy.OnDestroyed` (subscribed in
  its `Awake`) runs only on the owner: `MessageHud.MessageAll(Center, Localize(text))`, then
  `PersistentEventSystem.TriggerEvent(name)`. Other peers only see the ZDO go away (`ZDOMan.HandleDestroyedZDO` →
  `m_onZDODestroyed`, no `Destructible.Destroy` on them).
- `TriggerEvent` looks the name up in `m_possibleEvents` and sends the routed `RequestStartEvent(index)` to the server.
  The server's `RPC_RequestStartEvent` checks `maxConcurrent`, picks a spot with `PersistentEvent.GenerateEventLocation`,
  instantiates `objectsToSpawn`, adds an `ActivePersistentEvent` (no duration: permanent) and broadcasts the whole list
  as JSON. It never checks who sent the request.
- `PersistentEventSystem.m_possibleEvents` holds one event, **`jotun_invasion`** (index 0): radius 100-300 m,
  `maxConcurrent` 3, biomes Meadows, Black Forest, Swamp, Mountain and Plains (never the Deep North), 1000-12000 m from
  the world centre, 400 m apart, `environmentOverride` `JotunInvasion` per biome (`JotunInvasion_<biome>`; there is no
  `_deepnorth`), snow shader effect. `objectsToSpawn`: `FimbulLocation01` at the centre (whose `BlackIce_Core` drops
  Malicious Blood, `HatefulBlood`, and stops the event when broken, "The Jotun Retreat") and 250-800 water ice shelves.
- Consumers of active events: `Minimap.UpdatePersistentEventPins` (circle + animated pin for every event, no filter),
  `PersistentEventSystemDirectionHelper` (No Map particles), `EnvMan.GetEnvironmentOverride`, the snow compute buffer in
  `PersistentEventSystem.UpdateComputeBuffer`, `SpawnSystem.UpdateSpawnList` (`m_requiredPersistentEvent`) and
  `WearNTear` (`m_requiredPersistentEvent`). The list is saved in the world file by **index** into `m_possibleEvents`.

### 1.2 Spawning and stars

- `SpawnSystem` sits on each zone's `_ZoneCtrl`. `UpdateSpawning` runs every second on the client that owns that
  zone-control ZDO, only while a player is in the zone (a dedicated server never spawns). For each `SpawnSystemList` it
  calls `UpdateSpawnList(spawners, now, eventSpawners, salt)`: per entry, a timer stored on the zone-control ZDO
  (key = stable hash of salt + prefab name + 1-based index) allows `min(maxSpawned, elapsed / interval)` attempts; a
  zone visited for the first time bursts up to `m_maxSpawned` attempts. Per attempt: chance, global key, environments,
  day/night, cap (`GetNrOfZDOInstances`: every ZDO of that prefab in the 5 × 5 zones, 320 × 320 m, around the zone),
  `FindBaseSpawnPoint` (20 tries 40-80 m from a random player in the zone, each through `IsSpawnPointGood`), persistent
  event, spacing, then a group whose members each pass `IsSpawnPointGood`.
- `IsSpawnPointGood` checks biome (ground data at the point), biome area, blocked, altitude (`y - 30`), tilt, distance
  from the centre, no player within 40 m (unless `m_canSpawnCloseToPlayer`), not inside a player base (unless
  `m_insidePlayerBase`), forest, lava and ocean depth.
- `Spawn`: instantiate, `SetHuntPlayer` if `m_huntPlayer`, then the level roll: from `m_minLevel`, while
  `level < m_maxLevel` and `Random(0,100) <= GetLevelUpChance(point, entry)`, add one. `GetLevelUpChance(Vector3,
  SpawnData)` passes `m_overrideLevelupChance` to `GetLevelUpChance(Vector3, float)`: base = override if > 0 else 10;
  with a World level modifier `min(70, base^(worldLevel × 1.15)) × sector multiplier`, else `base ×
  Game.m_enemyLevelUpRate × sector multiplier`. The Deep North is one single biome sector
  (`AltBiomeWorldData.GenerateSectors`). With chance p and levels 1-3: 0★ = 1-p, 1★ = p(1-p), 2★ = p².
- Vanilla `_SpawnList_DeepNorth` (asset bundles) has the invasion entries `JotunWarrior` (max 4, 500 s, group 1-2),
  `JotunWitch` (max 2, 500 s), `Elaking` (max 6, 1000 s, group 1-3) and `projectile_FimbulvinterMeteor` (every 5 s,
  group 1-3, radius 30, 150 m up), all `m_biome = -1`, `m_requiredPersistentEvent = "jotun_invasion"`, levels 1-1 (the
  vanilla invasion creatures are always 0★). The rest of the list: seals, frost Greydwarfs (`Greydwarf_Frozen`,
  `Greydwarf_Shaman_Frozen`), `Skeleton_DeepNorth`, night `Elaking`/`ElakingLantern`, `Barka` (12 %, 1000 s),
  `Moose`, `Spawner_TrollFrost` (the Gammeltroll comes from a spawner object), and the daytime patrols `JotunWarrior` /
  `JotunWitch` (5 %, 3000 s) gated by the global key `jotun_killed`.

### 1.3 Factions and the final boss

- `JotunWarrior`, `JotunWarriorDualWield` ("Krigen"), `JotunWitch` ("Hexen"), `Elaking`, `TrollFrost` ("Gammeltroll"),
  `Barka`, `Moose` and the frost Greydwarfs are all `Character.Faction.DeepNorth` (13), so `BaseAI.IsEnemy` (same
  faction → false) never lets them fight each other.
- Kall Fimbulbringer: `FrozenKing`, `FrozenKing_p2`, `FrozenKing_p3`; the last phase sets the global key
  `defeated_frozenking_p3`, the key vanilla uses to stop the Jotun raids.

### 1.4 Weather

- `EnvMan.UpdateEnvironment` asks `GetEnvironmentOverride()` every frame: debug env, intro, the active raid's env,
  a forced AltBiome env, the persistent event's env, a non-forced `EnvZone`. A non-empty answer is queued (blended in);
  otherwise the biome's weighted weather for the current period. Each client computes its own weather.
- Deep North weathers (asset names): `Twilight_Clear`, `Twilight_Snow`, `Twilight_SnowStorm` (wiki: clear 40 %,
  snowing 40 %, blizzard 20 %).

### 1.5 Who runs what

| Piece | Runs on |
|---|---|
| Stone destroyed (`OnDestroyed`) | the stone's ZDO owner (any client, or the host) |
| Invasion start | the server (`RPC_RequestStartEvent`) |
| Global keys | the server stores them, broadcasts the full list on change |
| Area spawns | the client owning each zone control |
| Creature AI (`IsEnemy`) | the creature's owner |
| Weather | every client, for itself |

## 2. Design

### 2.1 Stone count and stages (G1-G5, G11)

- The world's stone count is the valued global key **`mc_dn_stones <n>`** (saved in the world, sent to every player,
  readable by `listkeys`). Stage = `min(n, 3)`; awake = stage ≥ 1. Only the server writes it.
- **Counting (server).** The server subscribes to `ZDOMan.m_onZDODestroyed` and counts every destroyed ZDO whose prefab
  is `BlackIce_Start` (once per ZDOID per session: a stale ZDO update can make the server destroy the same ZDOID twice).
  This works whoever broke the stone, with or without the mod.
- **Effects of stone n (server):** every stone sends the vanilla "The Jotun Advance" (`$fimbulvinterorb_start`, each game
  localises it; G12); n = 1 and 2: no invasion; n = 3: `InvasionsAtThirdStone` (3) vanilla invasions; n ≥ 4: 1
  invasion. Each invasion is started by calling the server's `RPC_RequestStartEvent(own uid, index of jotun_invasion)` directly, so the
  vanilla cap of 3 and the placement rules apply; the log says how many started.
- **The vanilla trigger is stopped at the source** on a game with the mod: prefix on
  `TriggerPersistentEventOnDestroy.OnDestroyed` for a start trigger of `jotun_invasion` on the owner skips the vanilla
  banner and request (the server sends its own). It only does this once the server's rules arrived (so the server has
  the mod); a game still waiting runs vanilla and the server drops the request (next bullet).
- **Any other start request** for `jotun_invasion` (a player without the mod breaking a stone, an admin's `pevents
  start`, another mod) is held by the server for 5 s (prefix on `RPC_RequestStartEvent`). If a stone break arrives
  within the 5 s **after** it, the request is dropped (the stone is already handled); otherwise it runs. Only after: a
  vanilla owner sends its request before its stone's ZDO destroy (`m_onDestroyed` runs before `ZNetScene.Destroy`; the
  destroy goes out on the next `ZDOMan.Update`, on the same connection), so an earlier break is another stone and must
  not eat an admin's request. A player without the mod still sends the vanilla banner to everyone for their own break
  (D16).
- **Old worlds (G11).** When the key is missing, once the server's world is loaded (`ZoneSystem.LocationsGenerated`),
  it counts the placed `MorkBorg` locations (`ZoneSystem.m_locationInstances`, `m_placed`) that have no `BlackIce_Start`
  ZDO within 160 m (horizontal) of their position, and writes that count (0 on a new world). Zones and their dungeons
  are only generated by the server (`ZoneSystem.SpawnZone` in Full or Ghost mode), so a placed Mörkhalla always had its
  stone ZDO. No messages, no invasions. The count runs again when the key goes from present to missing (`removekey`,
  `resetkeys`), also when a stone breaks in the half second before the cache notices. Never every tick: a mod that
  blocks the key (World Advancement Progression) gets one warning instead.

### 2.2 Areas: a seeded cell pattern (G2-G4, G6)

- The world is cut into **cells** of a jittered grid: one seed point per 400 m square, moved by up to ±35 % of the cell
  size (hash of the world seed and the square). A position belongs to the cell of the nearest seed point among the 3 ×
  3 squares around it (Voronoi). A cell is a **Deep North cell** when its seed point is in the Deep North
  (`WorldGenerator.IsDeepnorth`).
- Each Deep North cell has a fixed random value in [0, 1) from the world seed. It is **awake** at stage s when that
  value is below `Coverage(s)` (20 / 40 / 70 %). The same cells stay awake at the next stage: stage 1's pockets grow
  into stage 2's patches and merge into stage 3's invaded north. `Coverage(s)` is cumulative (the largest of the
  settings up to s), so a lower setting for a later stage never puts areas back to sleep.
- Every game (server and clients) computes the same cells from the world seed and the stage key: nothing to save,
  nothing to send, nothing on the map. About 110 Deep North cells exist on a typical world (about 30 km² inside the
  10 km world edge).

### 2.3 Area spawns (G2-G4, G7, G9)

- The mod has its own `SpawnData` entries (not in any `SpawnSystemList`, so spawn-list editors never see or remove
  them), all `m_biome = DeepNorth`, day and night, at or above sea level, never inside a player base, not hunting:

  | Entry | Prefab | Max per 320 m | Interval | Chance | Group | Levels |
  |---|---|---|---|---|---|---|
  | Krigen | `JotunWarrior` | 3 | 500 s | 100 % | 1-2 | 1-3 |
  | Krigen (dual axes) | `JotunWarriorDualWield` | 2 | 500 s | 100 % | 1 | 1-3 |
  | Hexen | `JotunWitch` | 2 | 500 s | 100 % | 1 | 1-2 |
  | Elaking | `Elaking` | 6 | 1000 s | 100 % | 1-3 | 1-2 |
  | Meteors (storm only) | `projectile_FimbulvinterMeteor` | no cap | 5 s | 100 % | 1-3 (radius 30, 150 m up) | — |

  Nature comes as bands (2.5), not as entries.

  Jotun values copy the vanilla invasion (Krigen 4 split 3 + 2 with the dual-axe variant that vanilla only uses at the
  invasion core); `JotunDensity` scales the Jotun caps. The level ranges follow vanilla data (Hexen and Elaking stop at
  1★).
- **Runner.** Postfix on `SpawnSystem.UpdateSpawning`: on the zone-control owner with a local player, when the north is
  awake and the zone touches an awake cell, refill `m_tempNearPlayers` (another mod's prefix may have skipped the
  original) and run each due entry with its own `UpdateSpawnList([entry], now, false, "mcdn<i>_")` call (own ZDO timer
  key; no shared counter between entries). Due = the same timer test vanilla makes, read first, so the 5 × 5 zone object
  search runs only when an entry can spawn.
- **Gate.** Prefix on `SpawnSystem.IsSpawnPointGood` for our entries only (reference check): the point must be in an
  awake cell (meteors: an awake, storming, not cleared cell; after Kall, Jotun only during a burst, into its cells,
  2.6). It runs for each of the 20 centre tries and each group member, so creatures only appear inside the areas.
- **Stars (G7).** Jotun entries carry `m_overrideLevelupChance` = `StarChanceStage<s>` (10 / 25 / 45 %), so vanilla
  `GetLevelUpChance` applies Enemy level-up rate and World level. Stage 1 = vanilla odds (9 % 1★, 1 % 2★); stage 2
  ≈ 19 % 1★, 6 % 2★; stage 3 ≈ 25 % 1★, 20 % 2★ (Krigen). Nature bands keep the vanilla chance. Measured in the probe
  world: the Deep North sector has no level-up multiplier, so the effective chance is the setting.
- **Tag.** While a Jotun entry runs, a postfix on `Character.Awake` writes the cell id on the new creature's ZDO
  (`<GUID>.Cell`, int). This is the "Jotun of this area" of G10.

### 2.4 Storms (G8)

- Each awake cell has its own schedule from the world time (`ZNet.GetTimeSeconds`, the same on every game): cycles of
  `C = (StormMinMinutes + StormMaxMinutes) / 2 / StormShare` minutes (22.5 min by default) with a per-cell phase; in
  each cycle one storm of a seeded length between 5 and 10 minutes at a seeded start. Uptime ≈ `StormShare` (33 %).
- **Blizzard.** Postfix on `EnvMan.GetEnvironmentOverride`: when vanilla has no override and the local player is
  outdoors inside the Deep North line (`WorldGenerator.IsDeepnorth(x, z)`, the cells' own geometry, sea included: a
  border cell reaches up to about 300 m past the line, into other biomes) in an awake, storming cell that is not
  cleared (after Kall), return the Deep North blizzard
  (`Twilight_SnowStorm`, checked at runtime; fallback: a Deep North weather whose name contains "Storm"). Raids, boss
  fights and the vanilla invasion's weather keep priority. Cached per half second.
- **Meteors.** The meteor entry runs only while the zone touches a storming cell, and its gate requires a storming cell.

### 2.5 Nature fights back (G9)

- Postfix on `BaseAI.IsEnemy(Character, Character)`: when the north is awake (and `NatureFightsBack`), vanilla said
  "not enemies", both are `Faction.DeepNorth`, not tamed, and one is of the Jotun army (`JotunWarrior`,
  `JotunWarriorDualWield`, `JotunWitch`, `Elaking`) while the other is nature (`TrollFrost`, `Barka`,
  `Greydwarf_Frozen`, `Greydwarf_Shaman_Frozen`), the answer becomes true. It applies to every such creature, not only
  the ones the areas spawn (the user named the species). Players are unaffected (they were already enemies of both).
- **Bands.** Each zone the runner serves rolls for a band every 20 minutes (`NatureBandChance`, 10 %; the first roll at
  once since the zone has no timer yet). A band: 5-10 frost Greydwarfs of which 1 (5-7) or 2 (8-10) are shamans, and
  0-2 big ones, each Gammeltroll or Barka 50/50. Its centre comes from vanilla `FindBaseSpawnPoint` (40-80 m from a
  player, through the gate: awake cell), each member is placed within 7 m (big ones 10 m) through vanilla
  `IsSpawnPointGood` and `Spawn` (vanilla levels: Greydwarfs up to 2★, Gammeltroll and Barka 0★ as in the vanilla
  Deep North list). Members carry `<GUID>.Band = 1`; no new band while 3 or more members live within 160 m. Before Kall
  only.

### 2.6 After Kall (G10)

- Kall defeated = global key `defeated_frozenking_p3`. No timed spawns any more (Jotun entries, bands); meteors keep
  falling in storming cells that are not cleared.
- **Entered again = burst (user rule).** When the runner serves a zone with a player in the Deep North, the cell under
  that player is **entered** if it is awake, not cleared and not engaged. For each entered cell the runner resets the
  timers of the Jotun entries on that zone control and runs them once: vanilla tries up to each cap at once. During
  that pass the gate lets Jotun spawn only into that cell, and the cap count (`SpawnSystem.GetNrOfZDOInstances`
  prefix) is the cell's own living Jotun of that kind: the larger of its tagged ones in the 5 × 5 zones and the
  server's count for the cell (its Jotun that roam far away still count; other areas' Jotun and vanilla ones do not).
  So a fresh area gets a full set, a half-cleared one is topped up, and entering from another side never adds a
  second set. Only the cell under a player: the vanilla spawn ring (40-80 m from a player) could not put Jotun in a
  cell the player only sees from its edge, and such a cell would be flagged without ever getting its Jotun.
- **Engaged ("to be defeated", server).** The game that bursts tells the server which cells got Jotun or have living
  ones (full: nothing to top up) with routed `<GUID>.Engage`; a cell where nothing could spawn and none live (no valid
  spot) is tried again 5 s later. The server keeps a cell engaged until no player is within the release distance of
  its seed point: every point of the cell (481 m) plus the loaded zones around a player (near simulation distance + 1
  zones on the diagonal: 754 m at the default distance), so the cell left everybody's loaded range. A player counts by
  their character and by their reference position (a dead player waiting to respawn has no character; the game puts
  the reference at the bed). Checked every 2 s. Engaged cells go to every player with the cleared list; a game also
  holds a cell it just burst for 10 s (network delay). Engagement is not saved: after a restart every cell not cleared
  bursts again when entered.
- **Kall killed in the session.** When Kall falls while players are in the world, the server engages at once the cell
  under each player in the Deep North and the cells around them (the zone samples the runner uses) that still have
  living area Jotun: no refill in the area of the fight. A cell nearby that nobody entered and that has no Jotun is
  left alone: it bursts when entered. At world load with Kall already dead nothing is engaged.
- **Living and cleared (server).** When the key appears (or at world load when it is already there, or the mod is
  turned back on), the server scans the ZDOs of the four Jotun-army prefabs (`ZDOMan.GetAllZDOsWithPrefabIterative`)
  and groups the tagged ones by cell and kind, with rescans 5, 15 and 30 s later (a client may still spawn tagged
  creatures in the moment before the Kall key reaches it). After that it follows deaths through `m_onZDODestroyed`:
  each death of a tagged Jotun (after 1 s) and each engage (after 4 s, never sooner because of a death) recounts that
  cell's tagged Jotun in the 19 × 19 zones around its seed point (`ZDOMan.FindSectorObjects`), so Jotun of later
  bursts are counted too. A cell whose death recount finds no living Jotun **died out**; once settled (last rescan
  done, so also in the first 30 s after a load) it is **cleared** for good: no burst, no storm, no meteors. Cells with
  no living Jotun at Kall (never visited, or emptied before Kall) are not cleared: they burst when entered.
- **Remembered in the world.** The server writes each cleared cell into one of 8 int slots (`<GUID>.Cleared0..7`,
  value = cell id) of a zone control that no player owns (or the server owns), so no owner's next write replaces it; at
  load it reads every `_ZoneCtrl` ZDO for those slots. Zone controls are persistent vanilla objects, so nothing new is
  saved and nothing breaks if the mod is removed. The server sends cleared cells, engaged cells and living counts per
  cell and kind to every player (routed `<GUID>.Cleared`) on change (a death never delays a send already waiting),
  and to a player in answer to each rules request (join, turned back on). A game forgets them when it sees no Kall key,
  so a new Kall waits for the server's new lists.
- A player standing in a cell that becomes cleared (inside the Deep North line) gets the vanilla centre message "The
  Jotun Retreat".
- `deepnorth_stones` after Kall also shows, from the lists the game has, the area the player stands in: its living
  Jotun per kind and whether it waits to be defeated, spawns when entered, or is cleared.

### 2.7 Admin command (E1)

`deepnorth_stones` runs on any game: no argument prints the stone count, stage, awake share and Kall state (every game
has the key, the server's rules and the seed; cleared areas only on the server). `deepnorth_stones <n>` sets the count to
n (0-100) without messages or invasions: on the server (single player, host) at once; on a client it is sent to the
server as a vanilla remote command (`ZNet.RemoteCommand`: the server checks the admin list, its answer stays in the
server's console and log).

### 2.8 Areas on the map (G13)

- Rule `ShowAreas` (section Map, default on, sent with the server's rules). Each player's game tints the vanilla map
  colour texture (`Minimap.m_mapTexture`, 2048 × 2048 pixels of 12 m) where an awake area that is not cleared covers
  Deep North land: 60 % blend to purple (150, 70, 215); pixels next to Deep North land that is not shown, 90 % blend to
  dark purple (95, 25, 160), the region's border. The large map and the minimap both draw that texture under the
  vanilla fog of war, so only explored places show it, and areas that touch merge into one region (one colour per
  pixel, never added).
- Land only: the map shader draws water from the height texture. The mod tints the pixels inside the world (water
  edge), past the Deep North line (`WorldGenerator.IsDeepnorth`) and above the water level in the map height texture.
  The Deep North map colour is white, the same as the sea, so the colour alone cannot tell them apart.
- Scan once per map texture, 40 000 pixels a frame (about 30 frames after the world loads): each pixel of the north
  band gets its cell (`Cells.Grid`: seed points hashed once, same answer as `Cells.At`) and keeps its own colour.
  Paint when ShowAreas, the awake share of the stage, Kall or the cleared list change (a storm or star setting
  never repaints): the RGB24 bytes are written in place and uploaded
  once. Rule off, stage 0, rules not arrived yet, mod off: own colours back.
- The map cache on disk (`Minimap.SaveMapTextureDataToDisk`) is written from the generated arrays, never from the
  texture, so the tint is never saved. When the game makes or loads the texture again (`GenerateWorldMap`,
  `TryLoadMinimapTextureData`), the scan runs again.
- No pins or icons (one merged region, not many circles).

## 3. Decisions

- **D1 — Areas are not persistent events.** Real `ActivePersistentEvent`s would draw a circle and pin per area on every
  map, add snow-shader and No-Map particles, count toward the vanilla cap of 3 and the 400 m spacing (blocking vanilla
  invasions), be found by `StopEvent` (breaking a vanilla core could stop an area), and be saved by list index (a world
  would throw every frame once the mod is removed). Own cells avoid all of it.
- **D2 — Cells, not circles.** 70 % coverage needs about 100 circles; seeded cells give exact nesting between stages,
  "areas" that can be cleared one by one (G10), and cost nothing to sync.
- **D3 — Count destroyed stones on the server**, not requests: requests carry no position and also come from admins and
  other mods; the ZDO destroy reaches the server from any client.
- **D4 — Hold foreign start requests 5 s** rather than block them: admin commands keep working, a player without the mod
  never gets a free invasion at stones 1-2. Trade-off: an admin request made in the 5 s before an unrelated stone break
  is taken for that stone's request and dropped.
- **D5 — Spawn values.** Jotun entries copy the vanilla invasion; `m_minAltitude = 0` (the invasion's -1000 also lets
  them appear in water, which in the Deep North means in the sea); no `m_huntPlayer` (vanilla invasion creatures do not
  hunt either).
- **D6 — Stars through `m_overrideLevelupChance`**, not a `GetLevelUpChance` patch: no patch, other mods that read it
  (Expand World Data, Vandi) compose, and world modifiers apply (G7). The vanilla `(Vector3, float)` overload is also
  used by dungeon spawners, which must stay vanilla.
- **D7 — Hostility for the species, gated by the awakening.** The user listed the species; before the first stone
  nothing changes (G1). Moose stays neutral (not named). The Jotun army side is the four invasion prefabs.
- **D8 — One storm schedule per cell**, from world time and seed: the same storm for everyone without any network
  traffic. A region of several cells can storm in some cells and not others; weather blends over a few seconds.
- **D9 — Third stone starts the invasions it held back** (3 at once, setting `InvasionsAtThirdStone`), then 1 per stone.
  The vanilla cap of 3 running invasions applies to all of them (G5 "like in vanilla").
- **D10 — An admin `deepnorth_stones` changes the stage only** (no messages, no invasions), like old-world detection.
- **D11 — Mod turned off:** vanilla at once (a stone starts an invasion, no area spawns, normal weather, no hostility).
  The stone count stays in the world and applies again when the mod is turned back on. Creatures already spawned stay.
- **D12 — Vanilla messages only** (G12): every stone `$fimbulvinterorb_start` ("The Jotun Advance"), a cleared area
  `$fimbulvinterorb_destroyed` ("The Jotun Retreat", the invasion core's text): localised on each game, no English
  of our own.
- **D13 — Gammeltroll spawns directly** (`TrollFrost`), not through vanilla's `Spawner_TrollFrost` object.
- **D17 — Nature as bands** (G9): one band at one spot (the members fight the Jotun together), at most one band around
  a player (no new band while 3 members live within 160 m), 10 % per zone check every 20 minutes. Members are tagged, not
  counted for G10.
- **D18 — After Kall, "entering" an area = standing in its cell** (Deep North line, awake, not cleared, not engaged),
  not just loading a zone near it: only then can the vanilla spawn ring place its Jotun. "Left the range" = no player
  (character or reference position) within the cell's reach plus the loaded zones (754 m from its seed at the default
  simulation distance). Engagement is decided by the server (one list for everybody), and a burst's caps count the
  area's own Jotun wherever they are, so two players entering one area from two sides get one set of Jotun, not two.
  Kall killed in the session engages the cell under each player and the nearby cells that still have Jotun (no refill
  in the area of the fight).
- **D19 — Cleared areas are kept on zone controls** (vanilla persistent ZDOs, written by the server on ones no
  player owns), not in a global key (a key's value string is copied into every player's profile at each change) nor in a file next to the world (lost
  with cloud saves and world copies).
- **D20 — The map overlay tints the vanilla map texture** (G13) instead of pins or an extra image. Vanilla event
  circles are pins: one per area, colours add up where they overlap, and they show through the fog. An extra image
  would need its own fog handling and must follow every pan and zoom of both maps. Tinting the colour texture gets the
  fog, both maps, pan and zoom from vanilla, with one colour per pixel. Cost: one scan per world, spread over frames,
  and a texture upload when the areas change.
- **D14 — Player bases.** Spawns obey the vanilla rule (never inside a base's area, never within 40 m of a player). A
  base inside an area still sees Jotun walk in from around it.
- **D15 — Kall.** "Defeated" = `defeated_frozenking_p3`, the key vanilla uses to end its Jotun raids.
- **D16 — A player without the mod (AllowPlayersWithoutMod on)** who owns the stone they break (or a player with the mod
  whose server rules have not arrived yet) runs the vanilla trigger: its "The Jotun Advance" goes to every player, then
  the server's one (the same text) too. The stone is counted and its stage applies normally; the request is dropped
  (D4). Filtering that relayed message is not worth it.

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Runs on |
|---|---|
| Stone count, stage key, messages, vanilla invasions, living, engaged and cleared areas, cleared-area slots, old-world detection, deferred requests | server (dedicated or host; single player = local) |
| Skipping the vanilla trigger | the stone's owner (with the mod and the server's rules) |
| Area spawns, bands, meteors, tags, after-Kall bursts (then it tells the server) | the zone-control owner |
| Nature hostility | each creature's AI owner |
| Blizzard | each player for themselves |
| Map overlay | each player for themselves (with the server's ShowAreas) |

### 4.2 RPCs, ZDO keys, network version

- `<GUID>.Settings` (server → client, ZPackage `AwakeningRules` layout 1) and `<GUID>.SettingsRequest` (client → server,
  int layout): the Swim Dive pattern.
- Routed `<GUID>.Cleared` (server → everybody on change, or one peer after its rules request): int layout 1; int count
  and the cleared cell ids; int count and the engaged cell ids; int count and, per cell, the cell id and 4 living tagged Jotun counts (Krigen, dual-axe Krigen, Hexen, Elaking).
- Routed `<GUID>.Engage` (a game that burst → server): int layout 1, int count, then the cell ids.
- ZDO int `<GUID>.Cell` on Jotun-army creatures spawned by an area, `<GUID>.Band` (1) on nature band members,
  `<GUID>.Cleared0..7` (cell ids) on zone controls; zone-control longs for the entry timers (`mcdn<i>_<prefab>1`,
  `mcdnBand_NatureBand1`).
- Global key `mc_dn_stones <n>`.
- Network version 1. Bump on any change to the above.

### 4.3 Server settings and join check

Copy of Swim Dive: `PlayerCheck` (refuse after 1 s grace unless `AllowPlayersWithoutMod`), `ServerRules` (send
`AwakeningRules` to every compatible player; a client waiting for them behaves like vanilla: no spawns, no storm, no
hostility, no trigger skip), `Patches/ZNetPatches.cs`.

### 4.4 Hand-off cases

- **Creatures spawned by an area** are ordinary creatures: a player without the mod sees and fights them normally;
  the cell tag is an unknown ZDO key to them.
- **A zone owned by a player without the mod** (AllowPlayersWithoutMod on) spawns vanilla only while they own it.
- **Hostility** is decided by each creature's owner: a creature whose AI runs on the game of a player without the mod
  keeps the vanilla peace with the other side (the other side, owned by a modded game, still attacks it); with the
  default join check this cannot happen.
- **Weather** is local: a player without the mod has no blizzard.
- **A stone broken by a player without the mod** is counted (D3); their vanilla request is held and dropped (D4).

### 4.5 Dedicated server

The server never spawns creatures (vanilla), so area spawns run on clients; the server counts stones, writes the key,
starts invasions, decides cleared areas and answers the console command. It needs the mod for everything except the
spawns, which every client runs with the server's rules.

## 5. Config

| Section | Key | Default | Range | Sent to players |
|---|---|---|---|---|
| General | Enabled | true | | |
| General | AllowPlayersWithoutMod | false | | no (server only) |
| Awakening | CoverageStage1 / 2 / 3 | 20 / 40 / 70 | 0-100 % | yes |
| Awakening | StarChanceStage1 / 2 / 3 | 10 / 25 / 45 | 0-100 % per star roll | yes |
| Awakening | JotunDensity | 100 | 25-400 % | yes |
| Awakening | NatureFightsBack | true | | yes |
| Awakening | NatureBandChance | 10 | 0-100 % per zone check (20 min) | yes |
| Storms | StormShare | 33 | 0-100 % | yes |
| Storms | StormMinMinutes / StormMaxMinutes | 5 / 10 | 1-60 | yes |
| Storms | Meteors | true | | yes |
| Map | ShowAreas | true | | yes |
| Invasions | InvasionsAtThirdStone | 3 | 0-10 | no (server only) |

## 6. Compatibility

### 6.1 Sibling mods

None (first Deep North mod).

### 6.2 Other MC mods

- **Creatures Morale**: Jotun have no rank (never afraid, `MoraleRules` has no Deep North home list), so area Jotun
  never flee. Nature creatures fighting Jotun use Morale's normal rules.
- **Sneak Ambush**: area creatures do not hunt, so stealth works on them as on any creature.
- **Creature Kill and Tame Counts**: area kills count under the vanilla creature names.
- **Encyclopedia, Container Sort**: they read `SpawnSystemList`s; our entries are not in a list, so habitats stay
  vanilla (the Jotun already have the Deep North habitat).
- **Breeding Star Inheritance**: tamed animals only; nothing shared.

### 6.3 Popular external mods

- **Expand World Data, Spawn That, DropNSpawn** replace or re-index `SpawnSystem.m_spawnLists`; our entries live
  outside them and keep working. Their own copies of the vanilla invasion entries still require `jotun_invasion`.
- **StarLevelSystem, CLLC, ReefLevels, CreatureManager, ServersideQoL CreatureLevelUp** take over creature levels:
  the stage star odds are then theirs (logged as a note at start). Mob_Cap caps our stars (intended).
- **World Advancement Progression** blocks unknown global keys by default: add `mc_dn_stones` to its allowed keys, or
  the stone count never sticks (warned at start).
- **HexenBeGone** blocks Hexen from "natural" spawns by default, which includes the area Hexen.
- **Map mods**: a mod that makes the map texture again through the vanilla methods, or swaps in a new texture object,
  gets a new scan; a bigger map (`m_textureSize`, `m_pixelSize`) is scanned at its own size. A map texture in another
  format than vanilla's RGB24 (or not matching its own size setting) is left alone (one warning in the log, no
  overlay). A mod that paints the same Deep North pixels after us loses those pixels at our next repaint.

## 7. Implementation plan

### 7.1 Files

`Plugin.cs`, `AwakeningRules.cs`, `ServerRules.cs`, `PlayerCheck.cs` (Swim Dive copies), `Cells.cs` (grid, hashes,
coverage, storm schedule), `WorldState.cs` (stone key, stage, Kall, seed), `ServerWorld.cs` (server tick, ZDO destroy delegate), `Stones.cs` (server:
count, messages, invasions, held-back requests, old-world detection), `HeldCells.cs` (after Kall: engaged areas, server scan and clearing, routed
RPC, client receive and message), `AreaSpawns.cs` (entries, runner, tag context), `Hostility.cs`, `Storms.cs`,
`AdminCommand.cs`, `Compat.cs`, `MapOverlay.cs` (map scan and tint), `SelfTests.cs`; `Patches/ZNetPatches.cs`,
`SpawnSystemPatches.cs`, `MinimapPatches.cs`,
`CharacterPatches.cs`, `BaseAIPatches.cs`, `EnvManPatches.cs`, `PersistentEventSystemPatches.cs`,
`TriggerPersistentEventOnDestroyPatches.cs`.

### 7.2 Harmony patches and other entry points

| Target | Kind | Why |
|---|---|---|
| `ZNet.Awake` | postfix | register the routed `Cleared` and `Engage` RPCs on the new `ZRoutedRpc` |
| `ZNet.OnNewConnection`, `ZNet.RPC_PeerInfo`, `ZNet.Update`, `ZNet.OnDestroy` | postfix | rules (the rules request is answered with the area lists after Kall), join check, timers, world end |
| `SpawnSystem.UpdateSpawning` | postfix | area spawn runner |
| `SpawnSystem.IsSpawnPointGood` | prefix | area gate for our entries |
| `SpawnSystem.GetNrOfZDOInstances` | prefix | only during an after-Kall burst: the cap count is the burst cell's own living Jotun of that kind |
| `Minimap.Update` | postfix | map overlay: scan a slice, or repaint when the areas changed |
| `Minimap.GenerateWorldMap`, `Minimap.TryLoadMinimapTextureData` | postfix | the map colours were made again: scan again |
| `Character.Awake` | postfix | cell tag while a Jotun entry spawns |
| `BaseAI.IsEnemy(Character, Character)` | postfix | nature vs Jotun |
| `EnvMan.GetEnvironmentOverride` | postfix | blizzard |
| `PersistentEventSystem.RPC_RequestStartEvent` | prefix | hold foreign `jotun_invasion` requests |
| `TriggerPersistentEventOnDestroy.OnDestroyed` | prefix | skip the vanilla stone trigger |
| `ZDOMan.m_onZDODestroyed` | delegate (server) | count stones, track tagged Jotun |
| `Terminal.ConsoleCommand` | registered in `OnActivated` | `deepnorth_stones` |

### 7.3 State and lifetime

World state (stone cache, cleared and engaged areas, living counts, map scan, counted ZDOIDs, held-back requests, detection done, scan done) is cleared on
`ZNet.OnDestroy` and on deactivate. The ZDO delegate is added once per `ZDOMan` instance and removed on deactivate.

### 7.4 Live toggle

Off: patches gone, delegate removed, held-back requests that no stone break claimed run at once (vanilla), command
removed, weather back to vanilla within a few seconds, map colours back at once. On again inside a world: delegate added, rules asked (the
server answers with the area lists after Kall); on the server, living area Jotun recounted after Kall (no area engaged
until a player enters one).

### 7.5 Debug in-world self-tests

`dn.logic` (cell lookup vs brute force, map grid lookup = cell lookup on 20 000 points, coverage over 20 seeds, nesting and cumulative coverage, storm uptime and
lengths, parsing, invasions per stone, request claim window, broken-Morkhalla count, army vs nature, nature band rolls, area lists wire, vanilla texts, no
blizzard past the Deep North line, spawn gate by stage, Kall, storm and pending rules), `dn.network` (rules wire,
clamp, select, join verdict), `dn.vanilla` (game data the mod relies on: event, stone trigger, prefabs, factions, Kall
key, blizzard env, Mörkhalla location; notes the vanilla invasion spawn entries), `dn.hostility` (IsEnemy matrix, live
fight), `dn.stones` (stone breaks 1-4: key, no invasion at 1-2, a game waiting for rules runs vanilla and its request
is dropped, invasions at 3, vanilla cap at 4, admin request runs after the hold), `dn.detect` (old-world count),
`dn.kall` (world loaded with Kall: tagged Jotun counted per kind, no spawn outside a burst, server engagement kept while a player
is near and released when far, an empty burst retried, clearing after the last death, cleared area written on a zone
control and read back by a new scan), `dn.area` (Deep North at stage 3 with storms forced: tagged area Jotun inside
awake cells, star override, blizzard, meteors, a forced nature band, the area purple on the map (large map shot);
then Kall killed there: the area engaged at once and no refill; entered again: one burst, then engaged; a few killed
and the player stays past the 10 s local hold: no refill, still engaged; all its Krigen killed and 3 other Krigen
placed nearby, entered again: its own Krigen come back; all its Jotun killed: cleared, written, no storm, no spawn, gone
from the map), `dn.map` (map scanned for Deep North land; at stage 3 an awake area is purple and a sleeping one is
not; own colours back with ShowAreas off, at stage 0 and while waiting for the server's rules), `dn.morkhalla` (Malicious Ice per Mörkhalla, placed flag, detection on
an intact one). Tests force rules from the design defaults, never the player's config.

### 7.6 Shared framework used

`ModPlugin`, `NetworkGate` (PeerCompatible, PeerStateChanged), `PatchGuard`, `Log`, `SelfTest`.

## 8. Test plan (TESTING.md)

Setup: devcommands; `spawn` names `BlackIce_Start` (checked in the asset bundles), `JotunWarrior`, `TrollFrost`;
`deepnorth_stones`; `pevents list`; `listkeys`. Single player: G1-G11 and E1-E3 one by one; live toggle;
`Enabled = false` + restart; clean log. Multiplayer: dedicated server with two players; a stone broken by a client;
a player without the mod refused; AllowPlayersWithoutMod on with a stone broken by that player; area lists to a player
who joins after Kall; two players entering one area after Kall get one set of Jotun. Cross-mod: Morale (Jotun not afraid), Sneak (area creatures).

## 9. Open questions and unverified

### Measured by the in-world self-tests (1.0.16, 2026-10-01)

- One `BlackIce_Start` per Mörkhalla (`dn.morkhalla`: one Malicious Ice 2.3 m from the location, at y about 4950).
- Deep North weathers `Twilight_SnowStorm` (weight 0.5), `Twilight_Snow`, `Twilight_Clear` (`dn.vanilla`); the
  blizzard override applies (`dn.area`).
- The Deep North sector has no level-up multiplier: with default world modifiers the effective star chance of the
  area Jotun is the setting (45 % at stage 3, `dn.area`).
- The four Jotun-army prefabs have a persistent `ZNetView` (`dn.vanilla`): unloading keeps their ZDO (`ZNetScene.RemoveObjects`
  destroys only ZDOs that are not persistent), so an unloaded area Jotun stays counted and is never taken for a death.
- Map (`dn.map`, `dn.area`): 2048 × 2048 pixels of 12 m, Deep North map colour white (as the sea), about 109 000
  pixels of Deep North land in the probe world; the tint shows on land only, merged, under the fog (large map shot).
- `MorkBorg`: 40 per world; the vanilla invasion spawn entries match section 1.2 (`dn.vanilla`).
- After Kall (`dn.area`): Kall killed in an area engages it at once (no refill); entered again it gets one burst
  (about a dozen Jotun), no refill while the player stays past the 10 s local hold; with none of its Krigen left and 3
  other Krigen placed 30 m away, entering again brings its own Krigen back (vanilla counting would spawn none).
- A Krigen and a Gammeltroll target each other once the north is awake (`dn.hostility`; they must be able to sense
  each other: vanilla sight needs the target inside the view angle until alerted).

### Still unverified (and how to verify)

- How the blizzard looks while a vanilla invasion's own weather is near: invasion weather wins (priority); check in
  game near an invasion that reaches the Deep North border (rare: invasions never start in the Deep North).
- Multiplayer behaviour (dedicated server, clients, AllowPlayersWithoutMod): TESTING.md M01-M07.