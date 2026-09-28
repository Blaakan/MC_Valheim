# Exploration - Player Movement, Navigation & Progression

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> Conventions: code is cited as `Class.Method` (file = `.ref/decompiled/assembly_valheim/<Class>.cs`).
> "Owner" means the owner of the object's ZDO (`ZNetView.IsOwner()`); "server" means `ZNet.instance.IsServer()` (host in listen servers, the dedicated server otherwise).
> Field defaults quoted below are the **C# initialisers**. Many are overridden by serialized prefab values (Player prefab, ship prefabs, `_GameMain` scene objects). When a number matters for balance, log the live value at runtime before relying on it.

---

## Overview

| System | Main classes | Authority | Persistence |
|---|---|---|---|
| Swimming | `Character`, `Player`, `WaterVolume`, `LiquidSurface`, `Floating` | Local player (owner of own character ZDO) | none (skill level in player save) |
| Sailing | `Ship`, `ShipControlls`, `EnvMan` (wind), `Hud.UpdateShipHud` | Ship ZDO owner (a player on board) simulates physics; helmsman sends RPCs | ZDO: `forward`, `rudder`, `user` |
| Map & minimap | `Minimap`, `MapTable`, `Game` (location discovery), `Chat` (pings) | 100% client-local; map table data lives in the table ZDO | Player profile per world (`PlayerProfile.SetMapData`), table ZDO `data` |
| Sleep & time | `Bed`, `Game.UpdateSleeping`, `EnvMan`, `Player.SetSleeping`, `SleepText` | Server decides and skips time (`ZNet.SetNetTime`) | Net time in world save; bed owner in bed ZDO |
| Player statistics | `PlayerProfile`, `PlayerStatType`, `KillModifiers`, `Game.RPC_RegisterKill` | Each client's own profile; kills are pushed by the creature's owner via routed RPC | `.fch` profile |
| Lore / compendium / tutorials | `TextsDialog`, `Tutorial`, `Raven`, `RuneStone`, `TextViewer`, `InventoryGui` (trophies) | Client-local | Player save (`m_knownTexts`, `m_shownTutorials`, `m_trophies`...) |
| Skills | `Skills`, `Skills.SkillDef`, `Skills.SkillType`, `SkillsDialog` | Local player | Player save (`Skills.Save/Load`) |
| Camera | `GameCamera`, `PlayerController` | Client-local | `PlatformPrefs` settings only |
| Lights & utility slot | `Humanoid.EquipItem`, `VisEquipment`, `SE_Demister` | Owner equips; visuals replicated via ZDO item hashes | Inventory in player save |

Key take-aways for this area:

* Almost everything here is **client-authoritative** (player movement, map, stats, lore, camera). Only **sleep/time** is server-authoritative and **ship physics** runs on whichever client owns the ship ZDO.
* `Player.m_customData` (`Dictionary<string,string>`, saved in `Player.Save`, **unused by vanilla**) is the natural per-character persistence slot for mods in this chapter.
* Custom skills and custom pin types are rejected by vanilla code paths (enum checks) and need explicit injection (see Skills and Minimap).

---

## 1. Swimming

### Key classes
* `Character`: generic swim physics for every creature, including the player. Fields `m_canSwim`, `m_swimDepth` (default 2), `m_swimSpeed` (2), `m_swimTurnSpeed` (100), `m_swimAcceleration` (0.05). Private state `m_waterLevel`, `m_tarLevel`, `m_liquidLevel`, `m_swimTimer`, `m_cashedInLiquidDepth`.
* `Player`: swim stamina, drowning, Swim skill gain (`Player.OnSwimming` override), `m_swimStaminaDrainMinSkill` (5/s at skill 0) and `m_swimStaminaDrainMaxSkill` (2/s at skill 100).
* `WaterVolume` (ocean/lakes, one per heightmap tile) and `LiquidSurface` (tar pits etc.) push the surface height into anything implementing `IWaterInteractable` (`Character`, `Floating`, `Fish`).
* `Floating` static helpers: `Floating.GetLiquidLevel(Vector3)`, `Floating.GetWaterLevel(Vector3, ref WaterVolume)`, `Floating.IsUnderWater(Vector3, ref WaterVolume)`.

### Flow
1. `WaterVolume.OnTriggerEnter` registers the rigidbody's `IWaterInteractable` and increments its per-liquid counter (`Character.Increment(LiquidType)`); `WaterVolume.UpdateFloaters` then calls `Character.SetLiquidLevel(surfaceY, LiquidType.Water, ...)` every frame using `WaterVolume.GetWaterSurface` (includes waves). `OnTriggerExit` resets the level to -10000 when the counter reaches 0.
2. `Character.CustomFixedUpdate` (every character, every client): `CalculateLiquidDepth` (depth = liquid level - feet Y; forced to 0 when standing on / attached to a ship or teleporting) then `UpdateWater`.
3. `Character.UpdateWater`: if `m_canSwim` and depth > `max(0, m_swimDepth - 0.4)` it resets `m_swimTimer`. **`IsSwimming()` is simply `m_swimTimer < 0.5`.** The owner also applies the `Wet` (or `Tared`) status effect.
4. Owner only: `Character.UpdateMotion` picks `UpdateSwimming` when `IsSwimming()`.
5. `Character.UpdateSwimming` (private) is the core rule:
   * horizontal velocity: `m_moveDir * m_swimSpeed` (scaled by attack-speed factor and `SEMan.ApplyStatusEffectSpeedMods`), lerped with `m_swimAcceleration` for players; the applied force has **y = 0**, so move input never drives vertical motion;
   * vertical: the body is pushed toward the target height `GetLiquidLevel() - m_swimDepth` (up to +10 m/s when below it, down to -10 m/s when above it). **This single target height is what keeps characters at the surface - there is no dive in vanilla 1.0.16.**
   * rotation uses `m_swimTurnSpeed`; animator gets `inWater = !IsOnGround()`; then calls virtual `OnSwimming(targetVel, dt)` when not touching ground.
6. `Player.OnSwimming`: while moving, drains stamina `lerp(5, 2, SwimSkillFactor)` per second, plus equipment swim modifier (`Player.GetEquipmentSwimStaminaModifier`, modifier index 7) and `SEMan.ModifySwimStaminaUsage`, times `Game.m_moveStaminaRate`; raises `Skills.SkillType.Swim` every 1 s of movement. With no stamina: every 1 s deals `ceil(maxHP/20)` damage with `HitData.HitType.Drowning`.
7. Side rules while swimming (not on ground):
   * `Player.UpdateStats(float dt)` (the stamina/food overload; not the parameterless distance-stats overload) sets the stamina regen multiplier to 0;
   * `Humanoid.UpdateEquipment` hides hand items (`HideHandItems`), `Player.Update` re-shows them only when not swimming;
   * `Humanoid.EquipItem` refuses to equip anything;
   * `Player.UpdateCrouch` cancels crouch;
   * `Character.Jump` only works in water if the character touched ground in the last 0.25 s.
8. `GameCamera.GetCameraPosition` clamps the camera to `liquidLevel + m_minWaterDistance` (0.3), so the camera can never go under water.

### Data & persistence
No swim state is persisted or synced beyond the character transform (ZSyncTransform) and animator bools (`inWater`). Swim skill lives in `Skills`.

### Multiplayer authority
The character owner (for the player: the local client) runs `UpdateMotion`/`UpdateSwimming`; everyone else only sees the synced transform and animator parameters. A movement mod for the local player is therefore **client-only** in function.

### Patch points
| Target | Why |
|---|---|
| `Character.UpdateSwimming` (private) postfix | Override vertical velocity after vanilla (dive/ascend), filter `this == Player.m_localPlayer`. |
| `Character.UpdateWater` / `IsSwimming` | Change what counts as swimming (depth threshold). |
| `Player.OnSwimming` (protected override) | Swim stamina, drowning, skill gain, breath meters. |
| `Player.GetEquipmentSwimStaminaModifier` | Equipment-based swim buffs. |
| `GameCamera.GetCameraPosition` (private) | Remove/relax the above-water camera clamp. |
| `Character.SetLiquidLevel` | Observe liquid surface per character. |

---

## 2. Sailing

### Key classes
* `Ship` (on `Raft`, `Karve`, `VikingShip`, `VikingShip_Ashlands` prefabs; also implements `IMonoUpdater`). Physics, sail state, rudder, wind force, damage, ownership hand-off.
* `ShipControlls` (sic): the helm; `Interactable` + `IDoodadController`. Handles control requests and attaches the player.
* `EnvMan`: global wind (`GetWindDir`, `GetWindIntensity`, `UpdateWind`).
* `Hud.UpdateShipHud`: sail/rudder/wind UI. `Minimap.UpdateWindMarker`: wind arrow on the minimap.
* `Player.GetControlledShip` (doodad controller is a Ship) vs `Character.GetStandingOnShip` / `Ship.GetLocalShip` (ship volume the local player is in).
* `ShipEffects`: wake/sound only.

### Flow
1. **Taking the helm**: `ShipControlls.Interact` (player must stand on this ship and not be encumbered) → RPC `RequestControl(playerID)` to the ship owner → `ShipControlls.RPC_RequestControl` writes ZDO `user` if free (or already this player) and replies `RequestRespons` → `Player.StartDoodadControl` + `Player.AttachStart(..., onShip: true, ...)`. Releasing: `ShipControlls.OnUseStop` → RPC `ReleaseControl`.
2. **Input**: `Player.SetDoodadControlls` → `ShipControlls.ApplyControlls` → `Ship.ApplyControlls(moveDir)`:
   * forward/back edge-triggered → RPC `Forward` / `Backward` (owner steps `Ship.Speed`: `Back ↔ Stop ↔ Slow ↔ Half ↔ Full`);
   * rudder: `m_rudderValue += dir.x * lerp(0.5,1,|rudder|) * m_rudderSpeed * dt`, clamped [-1,1], sent via RPC `Rudder` at most every 0.2 s;
   * `Ship.Stop()` / RPC `Stop` exists but **nothing in vanilla calls it** (free hook for an "instant stop" key).
3. **Simulation** `Ship.CustomFixedUpdate` (all clients run `UpdateControlls`, `UpdateSail`, `UpdateRudder` for visuals; **only the owner** continues):
   * forces speed to `Stop` when nobody is aboard and `Slow/Back` to `Stop` when nobody holds the helm;
   * buoyancy from 5 water samples around `m_floatCollider` (`m_force`, `m_forceDistance`, `m_waterLevelOffset`), damping (`m_damping`, `m_dampingForward`, `m_dampingSideway`, `m_angularDamping`), strong horizontal damping (x0.1) when no player aboard;
   * **sail force** `Ship.GetSailForce(sailSize, dt)`: `sailSize` 0.5 (Half) or 1 (Full); target = `normalize(windDir + forward) * GetWindAngleFactor() * lerp(0.25, 1, windIntensity) * m_sailForceFactor * sailSize`, smoothed with `Vector3.SmoothDamp` (smooth time 1 s), applied at `m_sailForceOffset` above centre of mass;
   * **wind angle rule** `Ship.GetWindAngleFactor` (public, also used by the HUD icon colour): with `d = dot(windDir, -forward)`, factor = `lerp(0.7, 1, 1-|d|) * (1 - LerpStep(0.75, 0.8, d))`: best at beam reach, 70 % with tail wind, zero when the wind comes from within about 37-41° of the bow;
   * **steering**: a lateral force at `m_stearForceOffset` proportional to forward speed (`m_stearVelForceFactor * -rudder`), so turning needs speed; in `Slow`/`Back` an extra paddle force `m_backwardForce * (1-|rudder|)` plus direct turn force `m_stearForce`;
   * `Ship.ApplyEdgeForce` pushes ships back beyond radius 10420; `Ship.UpdateUpsideDmg`, `Ship.UpdateWaterForce` (impact damage), `Ship.TakeAshlandsDamage` (skipped if `m_ashlandsReady`, i.e. the Ashlands drakkar).
4. **Sync**: `Ship.UpdateControlls`: owner writes ZDO `forward` (int speed) and `rudder`; non-owners read them (rudder only if they did not send one in the last 1 s).
5. **Ownership**: `Ship.UpdateOwner` (InvokeRepeating 2 s, owner only): if the owner's local player is not on board, ownership moves to the first aboard player with a valid owner id. **The helmsman is not necessarily the owner** - the physics may run on a passenger's machine using the helmsman's RPC-fed `m_speed`/`m_rudderValue`.
6. **Wind** `EnvMan.UpdateWind`: deterministic from net time (4 octaves seeded by `timeSec / (m_windPeriodDuration / octave)`), intensity mapped to the current environment's `m_windMin..m_windMax`, transition over `m_windTransitionDuration`. Near the world edge the wind blows outward. If `Ship.GetLocalShip().IsWindControllActive()` (any player aboard with `StatusEffect.StatusAttribute.SailingPower`, i.e. Moder's power) the wind is set to the ship's forward. Wind is computed **locally on every client**; it matches across clients except for the Moder/edge overrides which use the local player's situation.

### Data & persistence
* ZDO keys (`ZDOVars`): `forward` (int `Ship.Speed`), `rudder` (float), `user` (long, helmsman player id on the ship ZDO, written by `ShipControlls`).
* Stats: `Player.UpdateStats()` (parameterless overload, every 2.5 s) adds `DistanceSail` when `Ship.GetLocalShip() != null` and `DistanceSailHelm` when also `GetControlledShip() != null`.
* **There is no sailing skill in 1.0.16** (`Skills.SkillType` has no Sailing entry; 109 is unused).

### Multiplayer authority
Owner-simulated physics, RPC control (`Forward`, `Backward`, `Stop`, `Rudder` on the ship's `ZNetView`; `RequestControl`, `ReleaseControl`, `RequestRespons` on the same view from `ShipControlls`). `ZNetView.InvokeRPC(method, ...)` targets the **ZDO owner**. Any sailing rule change must run on the owner, i.e. **every client that can end up owning a ship needs the mod**.

### Patch points
| Target | Why |
|---|---|
| `Ship.CustomFixedUpdate` prefix/postfix | Scale public tuning fields per-tick (`m_sailForceFactor`, `m_stearForce`, `m_stearVelForceFactor`, `m_backwardForce`, `m_dampingForward`) based on helmsman data; restore in postfix. |
| `Ship.GetSailForce` (private) | Change acceleration curve (SmoothDamp time) and low-wind floor. |
| `Ship.GetWindAngleFactor` | "Catch the wind" rules; HUD reuses it automatically. |
| `Ship.ApplyControlls` | Rudder responsiveness (`m_rudderSpeed`), new keys (instant stop). |
| `Ship.RPC_Forward/RPC_Backward/RPC_Stop` | Speed-state machine changes (e.g. skip states). |
| `ShipControlls.RPC_RequestControl` | Hand ZDO ownership to the helmsman (`GetZDO().SetOwner(sender)`), write helmsman data to ship ZDO. |
| `Ship.UpdateOwner` (private) | Keep the helmsman as owner. |
| `EnvMan.UpdateWind` (private) | Wind rules (careful: global for all systems - windmills, cloth, particles). |
| `Hud.UpdateShipHud` (private) | Extra sailing UI. |

---

## 3. Map & minimap (incl. cartography table)

### Key classes
* `Minimap` (singleton `Minimap.instance`): fog of war, pins, small/large map UI, biome label, player/ship markers, shared-map merge, texture cache.
* `Minimap.PinType` enum: `Icon0..Icon4` (user pins; `Icon4` is 7th in the enum), `Death`, `Bed`, `Shout`, `None`, `Boss`, `Player`, `RandomEvent`, `Ping`, `EventArea`, `Hildir1..3`, `Memorial`. `Minimap.PinData` (name, type, icon, pos, `m_save`, `m_ownerID`, `m_author`, `m_checked`, `m_doubleSize`, `m_animate`, `m_worldSize`, UI refs).
* `MapTable` (prefab `piece_cartographytable`): two `Switch`es (read/write).
* `Game.DiscoverClosestLocation` / `Game.RPC_DiscoverClosestLocation` (server) / `Game.RPC_DiscoverLocationResponse` → `Minimap.DiscoverLocation` (used by `RuneStone.m_locationName`, i.e. vegvisirs).
* `Chat.SendPing` (routed `ChatMessage` type 3 to everybody), `ZNet.SetPublicReferencePosition` (show me on map).

### Flow
* **Generation**: `Minimap.Update` on first frame with a `WorldGenerator`: `TryLoadMinimapTextureData(seed)` (cached `cacheMinimap*` textures in the world save folder) else `GenerateWorldMap`, then `LoadMapData` from the profile.
* **Exploration**: `Minimap.UpdateExplore` (private, called from `Update` even when the map is hidden) every `m_exploreInterval` (2 s) calls private `Explore(player.position, m_exploreRadius)` (radius 100 m, **constant: no altitude, ship or skill dependency**). `Explore(Vector3, float)` marks pixels in `m_explored` (BitArray) and clears the fog texture red channel. Shared (others') exploration is `m_exploredOthers` / green channel. `Minimap.ExploreAll` reveals everything.
* **World↔map**: private `WorldToPixel`, `WorldToMapPoint`, `MapPointToWorld`, `ScreenToWorldPoint` use `m_textureSize` and `m_pixelSize` (code defaults 256 / 64; prefab values differ - read at runtime).
* **User pins**: large map double-click → `OnMapDblClick` → `ShowPinNameInput` (adds pin of `m_selectedType` immediately) → `OnPinTextEntered` names it. Left click toggles `m_checked` (or clears `m_ownerID` for shared pins), right click `RemovePinUnderPointer`, middle click pings. Icon buttons call `OnPressedIcon0..4` → `IconPressed` → `SelectIcon`/`ToggleIconFilter` (double-tap or alt).
* **`Minimap.AddPin(pos, type, name, save, isChecked, ownerID, author)`** is public and the universal entry point. It **rejects any `type` outside the enum length** (`m_visibleIconTypes.Length`, sized from `Enum.GetValues(PinType)` in `Minimap.Start`) and turns it into `Icon3`. `GetSprite(type)` looks the sprite up in `m_icons` (`List<SpriteData>`).
* **Dynamic pins** (`UpdateDynamicPins`): bed spawn point (`UpdateProfilePins`), shouts, pings, public players, unique location icons from `ZoneSystem.GetLocationIcons` (every 5 s, `m_locationIcons` sprites), random events and persistent events. None are saved.
* **Rendering**: `Minimap.UpdatePins` instantiates `m_pinPrefab` per visible pin; filtered by `m_visibleIconTypes[(int)type]` and shared-data fade.
* **Cartography table**:
  * Read: `MapTable.OnRead` → decompress ZDO `data` → `Minimap.AddSharedMapData(bytes)`: ORs exploration into `m_exploredOthers`; imports pins whose owner != local player (dedupe by `HavePinInRange(pos, 1f)`), removes stale shared pins.
  * Write: `MapTable.OnWrite` (requires `PrivateArea.CheckAccess`) first reads, then `Minimap.GetSharedMapData(oldData)` (union of explored bits + all saved non-Death pins with owner id/author) → compress → RPC `MapData` to the table owner → ZDO `data`.
* **Location discovery**: server searches `ZoneSystem` for the closest location instance and replies to the sender; client `Minimap.DiscoverLocation` adds a saved pin (dedupe via `HaveSimilarPin`) and optionally opens the large map (`ShowPointOnMap`).
* **No-map mode**: `Game.m_noMap` (global key `NoMap` or per-player pref) forces `MapMode.None` in `Minimap.SetMapMode`.

### Data & persistence
* `Minimap.GetMapData`/`SetMapData` (map version 8, compressed ZPackage): texture size, `m_explored`, `m_exploredOthers`, saved pins (name, pos, type as int, checked, ownerID, author), public-position flag. Stored per world in the player profile (`PlayerProfile.SetMapData` → `WorldPlayerData.m_mapData`), written by `Minimap.SaveMapData`.
* Table: ZDO `data` byte array (shared-map version 3).
* Pin type is stored as a raw int: a vanilla client loading an unknown type silently degrades it to `Icon3` (data kept, icon changed).

### Multiplayer authority
Client-local except the table ZDO (owner writes via RPC `MapData`) and location lookup (server RPC). Pings/shouts are chat routed RPCs.

### Patch points
| Target | Why |
|---|---|
| `Minimap.Start` (private) postfix | Resize `m_visibleIconTypes`, append `m_icons`, `m_selectedIcons`, clone icon buttons for **new pin types** (must happen before first `LoadMapData`). |
| `Minimap.AddPin` prefix | Intercept/redirect pin creation (auto-pins). |
| `Minimap.UpdateExplore` (private) prefix / `m_exploreRadius` (public field) | Variable reveal radius (ships, spyglass, altitude). |
| `Minimap.Explore(Vector3, float)` (private) | Reveal arbitrary areas (reflection). |
| `Minimap.GetSharedMapData` / `AddSharedMapData` | Table behaviour: filter/extend what is shared. |
| `MapTable.OnRead` / `OnWrite` (private, Switch callbacks) | Table interactions; `MapTable.Start` postfix to register instances (no vanilla list). |
| `Minimap.SetMapMode` | React to map open/close (e.g. "opened at a table"). |
| `Minimap.UpdatePins` (private) postfix | Custom pin rendering (colours, sizes). |

---

## 4. Sleeping & time

### Key classes
`Bed` (spawn point + sleep entry), `Game` (server sleep loop, RPCs `SleepStart`/`SleepStop`), `EnvMan` (day cycle, time skip, `CanSleep`), `Player` (`AttachStart` writes `inBed`, `SetSleeping`, `m_wakeupTime`), `Hud` (sleep fade/progress), `SleepText`/`DreamTexts` (dream screen), `CinematicsManager` (boss dream cinematics).

### Time model (`EnvMan`)
* Net time `ZNet.GetTimeSeconds()` (server-owned). Day length `m_dayLengthSec` = 1200 s.
* Raw day fraction = `time % 1200 / 1200`, then `EnvMan.RescaleDayFraction`: raw [0.15, 0.85] maps to [0.25, 0.75] (day = 840 s), the night remainder to the rest (360 s). `m_smoothDayFraction` lags the raw value (LerpAngle 0.01 per FixedUpdate).
* `EnvMan.FixedUpdate` recomputes static flags from the **rescaled** fraction: `IsDay` 0.25-0.75, `IsAfternoon` 0.5-0.75, `IsNight` <0.25 or >0.75, `IsDaylight` (not `m_alwaysDark` env), and `s_canSleep = CalculateCanSleep()`.
* `EnvMan.UpdateTriggers`: crossing rescaled 0.25 → `OnMorning` (music, "$msg_newday", `Player.SurvivedOneDay` stat); crossing 0.75 → `OnEvening`.
* `EnvMan.GetMorningStartSec(day)` = `day*1200 + 1200*0.15`.

### Sleep flow
1. `Bed.Interact`: unclaimed → claim (needs roof/cover via `CheckExposure`: `Cover.GetCoverForPoint` ≥ 0.8 and under roof) and set spawn; own non-current bed → set spawn; own current bed → requires `EnvMan.CanSleep()` (afternoon or night **and** `now > Player.m_wakeupTime + m_sleepCooldownSeconds (30)`), `CheckEnemies` (`Player.IsSensed`), `CheckExposure`, `CheckFire` (`EffectArea.Type.Heat`), `CheckWet`; then `Player.AttachStart(..., isBed: true, ...)` which sets ZDO `inBed`.
2. Server `Game.UpdateSleeping` (InvokeRepeating 2 s): if not skipping, `EnvMan.IsAfternoon() || IsNight()`, `EverybodyIsTryingToSleep()` (every ZDO from `ZNet.GetAllCharacterZDOS` has `inBed`) and ≥10 s since last sleep → `EnvMan.SkipToMorning()`, `m_sleeping = true`, routed RPC `SleepStart` to everybody.
3. `EnvMan.SkipToMorning` sets private `m_skipTime`, `m_skipToTime` (next morning start) and `m_timeSkipSpeed = delta / 12` (so the skip always lasts ~12 s real time). `EnvMan.UpdateTimeSkip` (server only) advances with `ZNet.SetNetTime`.
4. Clients: `Game.SleepStart` → `Player.SetSleeping(true)` (HUD fade, `SleepText` → dream text, `Game.CollectResourcesCheck`).
5. When the skip ends (and no cinematic): server sends `SleepStop` → `Player.SetSleeping(false)` ("$msg_goodmorning", `Rested` SE, `m_wakeupTime`, stat `Sleep`), `Player.AttachStop`, an autosave if >60 s since last, and `WearNTear.OnSleep` on all pieces.

### Data & persistence
ZDO bed: `owner` (long), `ownerName`. Player ZDO: `inBed`, `wakeup`. Spawn point in `PlayerProfile` world data (`SetCustomSpawnPoint`). Time is the world's net time.

### Multiplayer authority
Server decides the skip (`Game.UpdateSleeping`, `EnvMan.UpdateTimeSkip`); **bed entry rules are evaluated on the client** (`Bed.Interact`, `EnvMan.CanSleep` uses the local player's wakeup time). On a dedicated server `Player.m_localPlayer` is null so `CalculateCanSleep` only checks the time window.

### Patch points
| Target | Why |
|---|---|
| `EnvMan.CalculateCanSleep` (private) postfix | Change when players may lie down (client side). |
| `Bed.Interact` prefix | Alternative sleep modes (e.g. alt-interact), relaxed checks. |
| `Game.UpdateSleeping` (private) | Server-side start condition and skip target. |
| `EnvMan.SkipToMorning` / private fields `m_skipTime`, `m_skipToTime`, `m_timeSkipSpeed` | Custom skip target/speed. |
| `Game.EverybodyIsTryingToSleep` (private) | Partial-sleep (percentage) rules. |
| `Player.SetSleeping` | Wake-up message/buffs. |

---

## 5. Player statistics & kill tracking

### Key classes
`PlayerProfile` (+ nested `PlayerProfile.PlayerStats`), `PlayerStatType` (≈205 values incl. distances, deaths by cause, kills, crafting, building, fishing, trees/mining tiers, exploration of the world edges), `KillModifiers` (`MixedAndTotal`, `Unarmed`, `Magic`, `Ranged`, `Melee`, `CountNone`), `Game.IncrementPlayerStat`, `Game.RegisterKill`/`RPC_RegisterKill`, `Achievements` (in-game achievements use the same stats), `TextsDialog.AddStats`.

### Data model
* `PlayerProfile.m_playerStats` is `PlayerStats[10]`: index 0 = raw stats (always), 1 = achievement-eligible, and further indices per achievement difficulty category (`Achievements.GetCurrentAchievementDifficultyIndex`).
* Each `PlayerStats` has `m_stats` (by `PlayerStatType`) and string-keyed dictionaries: `m_enemyStats[5]` (indexed by `KillModifiers`), `m_itemPickupStats`, `m_itemCraftStats`, `m_pickableStats`, `m_foodEatenStats`, `m_piecesPlacedStats`, `m_knownWorlds`, `m_knownWorldKeys`, `m_knownCommands`.
* **Per-creature kill counts already exist**: `m_enemyStats[0][Character.m_name]` (e.g. `"$enemy_greyling"`) is incremented on every kill credit; `m_enemyStats[(int)modifier]` additionally when all hits used a single damage family. So per creature `[0] >= [1] + [2] + [3] + [4]`; the rest are mixed kills, kills with no family, and kills from before the buckets were saved (below).
* `PlayerProfile.IncrementStatEnemy` always writes slot 0. Only when `Achievements.CanGetAchievements(cheated)` is true does it also write slot 1 and every slot from 3 up to the current difficulty index. `CountNone` logs an error and adds only to the total. Kills made with cheats on therefore still count in slot 0.
* **PvP kills are not recorded per victim in 1.0.16**: `Character.OnDeath` has a branch that adds the killed player's name to `m_enemyStats`, but `Player.OnDeath` overrides `OnDeath` without calling the base method, so that branch never runs. Older saves may still hold player names as keys (filter keys that do not start with `$`).
* `PlayerProfile.GetStat` returns the **current difficulty slot** when achievements are allowed, not slot 0: read `m_playerStats[0]` directly for lifetime numbers, and never add the slots up. `Achievements.ResetAchievement` / `ResetAllAchievements` set values to 0 in every slot and keep the keys, so entries with 0 exist.
* Saved with the profile in `PlayerProfile.SavePlayerToDisk` / `LoadPlayerFromDisk` (i.e. **per character, across all worlds**). History (`LoadPlayerFromDisk` branches): per-creature totals exist since character file version 42 (`Version.Player.CallToArms`); the weapon buckets `[1..4]` are read only from versions 46 (`DeepNorth`) and 44 (`AbandonedDN`), while versions 42, 43 and 45 keep only slot 0 `[0]`. Kills made before Deep North have a total but no weapon bucket; kills made before Call to Arms are only in the `EnemyKills` total.

### Kill flow
1. `Character.RPC_Damage` (runs on the owner of the victim): a hit from a `Player` sets ZDO bool key `"<s_attackers><playerName>"` and increments ZDO `Attackers`; the hit's `m_skill` updates ZDO `Modifiers` (`CountNone` → first family; different family → `MixedAndTotal`). The flag is set before resistances and before `ApplyDamage` returns early for tiny damage, so **a zero-damage hit counts as an attack**. Families: Swords/Knives/Clubs/Polearms/Spears/Blocking/Axes/Pickaxes/WoodCutting → Melee, Bows/Crossbows → Ranged, ElementalMagic/BloodMagic → Magic; Unarmed skill → Unarmed only for the bare-fists weapon (`m_shared.m_name == "Unarmed"`, marked in `Attack.DoMeleeAttack`), any other Unarmed-skill weapon → Melee.
2. `Character.SetHealth` on the owner resets ZDO `Modifiers` to `MixedAndTotal` whenever health reaches max: a creature that regenerated to full health after being hurt becomes a mixed kill whatever finishes it. Attacker flags are never cleared.
3. `Character.OnDeath` (victim owner): for each connected player flagged as attacker → local `Game.RPC_RegisterKill` or routed RPC `RPC_RegisterKill` to that peer. **Every player who hit the creature gets full credit**, not only the last hitter; a player who logged out before the death gets nothing. `EnemyKillsLastHits` / `BossLastHits` are the separate last-hit stats; they only count when the victim's owner is the local player and landed the last hit (`m_lastHit` is set on the owner only).
4. `Game.RPC_RegisterKill` (receiving client) → `PlayerProfile.IncrementStatEnemy(name, 1, modifier, cheated)` and `EnemyKills`/`BossKills` (+ solo/multiplayer boss stats).
5. No credit when the attacker is not a player (tames, other creatures) or for the console kill commands (no attacker), unless the player had hit that creature before. `killall` and `killtame` kill every non-player character within 1000 m, tames included; `killenemies` spares tames.

### Existing UI
`TextsDialog.AddStats` dumps all stats as one text entry ("$inventory_stats" = "Player Statistics", appended last to the Compendium list) in hardcoded English, one block per difficulty slot, but its "Enemies:" block only prints modifier headers and **never lists the per-enemy numbers**. It rebuilds the text on every Compendium open (no refresh while open) and writes the whole text to the log each time. `TextsDialog.ShowText` localizes the topic and text again when the entry is shown. Mod: [Creature Kill and Tame Counts](../../src/Exploration/Stats.PerCreature) prepends a per-creature section to this entry from an `AddStats` postfix.

### Multiplayer authority
Each client owns its own profile; kills arrive by routed RPC from the victim's owner. A display-only mod is client-only.

### Patch points
`PlayerProfile.IncrementStatEnemy` / `IncrementStat` (observe), `Game.RPC_RegisterKill` (react to kills), `TextsDialog.AddStats` (private, fix/extend the dump).

---

## 6. Lore, compendium & tutorials

### Key classes
* `TextsDialog`: the inventory "Compendium" panel (`InventoryGui.OnOpenTexts` → `TextsDialog.Setup`). `UpdateTextsList` builds `List<TextInfo>(topic, text)`: known texts sorted by topic, then inserts `AddLog` (MessageHud log) and `AddActiveEffects` at the top and appends `AddStats`. Simple left list / right text layout.
* `Player` knowledge sets (all private, saved in `Player.Save`): `m_knownTexts` (label → text), `m_shownTutorials` (tutorial keys, also location names via `AddKnownLocationName`), `m_knownBiome`, `m_knownMaterial`, `m_knownRecipes` (items and pieces), `m_knownStations`, `m_trophies` (prefab names), `m_uniques` (player keys). Public API: `AddKnownText`, `GetKnownTexts`, `GetTrophies`, `IsBiomeKnown`, `IsRecipeKnown`, `IsMaterialKnown`, `HaveSeenTutorial`, `SetSeenTutorial`, `ShowTutorial`.
* `Tutorial` (singleton): `m_texts` (`TutorialText`: name, topic, label, text, `m_globalKeyTrigger`, `m_tutorialTrigger`, `m_isMunin`). `Tutorial.Update` checks global keys every `m_GlobalKeyCheckRateSec`; `ShowText` → `SpawnRaven` → `Raven.AddTempText`.
* `Raven`: Hugin/Munin NPC. `Raven.Talk` marks the tutorial seen, `AddKnownText(label, text)` when the text has a label, increments `RavenTalk`.
* `RuneStone`: `Interact` → optional `Game.DiscoverClosestLocation` (vegvisir), `AddKnownText`, `TextViewer.ShowText(Style.Rune, ...)`; random text chosen deterministically from position.
* `TextViewer` (styles `Rune`, `Intro`, `Raven`), `InventoryGui.UpdateTrophyList` (trophy panel: icon, name, `<name>_lore` description), `InventoryGui.OnOpenAchievements`.

### Multiplayer / persistence
All client-local; knowledge is in the character save, so it follows the character between worlds.

### Patch points
`TextsDialog.UpdateTextsList` (private) postfix to inject entries into private `m_texts` before `FillTextList` draws them; `TextsDialog.Setup`; `Player.AddKnownText` (observe unlocks); `InventoryGui.UpdateTrophyList` (private) postfix to decorate trophy entries; `Tutorial.m_texts` to add raven tutorials.

---

## 7. Skills and their gameplay effects

### Key classes
* `Skills` component on the player: `m_skills` (`List<SkillDef>`: skill, icon, description, `m_increseStep`), runtime `m_skillData` (`Dictionary<SkillType, Skill>`), `m_DeathLowerFactor` (0.25), optional `m_useSkillCap`/`m_totalSkillCap`.
* `Skills.SkillType`: `Swords 1, Knives 2, Clubs 3, Polearms 4, Spears 5, Blocking 6, Axes 7, Bows 8, ElementalMagic 9, BloodMagic 10, Unarmed 11, Pickaxes 12, WoodCutting 13, Crossbows 14, Jump 100, Sneak 101, Run 102, Swim 103, Fishing 104, Cooking 105, Farming 106, Crafting 107, Dodge 108, Ride 110, All 999`.
* `Skills.Skill.Raise`: xp `m_increseStep * factor * Game.m_skillGainRate`; next level needs `floor(level+1)^1.5 * 0.5 + 0.5`; max 100.
* `Skills.GetSkillLevel` = base level + `SEMan.ModifySkillLevel` (e.g. `SE_Stats.m_skillLevel/m_skillLevelModifier` from meads/set bonuses), floored. `GetSkillFactor` = level/100. `GetRandomSkillFactor` = random in `lerp(0.4,1,f) ± 0.15`.
* `Player.RaiseSkill` applies `SEMan.ModifyRaiseSkill` then `Skills.RaiseSkill` → on level up `Player.OnSkillLevelup` (effect only) + message "$msg_skillup $skill_<enum name lowercase>".
* Death: `Skills.OnDeath` → `LowerAllSkills(m_DeathLowerFactor * Game.m_skillReductionRate)` (also resets accumulators).
* Persistence: `Skills.Save/Load` (version 2: type int, level, accumulator). **`Skills.Load` drops any type failing `Skills.IsSkillValid` (`Enum.IsDefined`)**, and `Skills.GetSkill` builds a `Skill` with `m_info = null` for unknown types (so `RaiseSkill` would NRE). Custom skills require patching `IsSkillValid`, `GetSkillDef` (or adding to `m_skills`), localization, and `CheatRaiseSkill` - which is what Jotunn's `SkillManager` does.

### Vanilla effects (where the factor is read)
| Skill | Effect | Where |
|---|---|---|
| Weapon skills | Damage roll (`GetRandomSkillFactor`), stamina/eitr/health cost -33 % at 100 | `Attack.GetAttackStamina`, `GetAttackEitr`, `GetAttackHealth`, damage in `Attack` hit code |
| Bows/Crossbows | Draw time to 20 % (`Humanoid.GetAttackDrawPercentage`), reload time to 50 % (`ItemDrop.ItemData.GetWeaponLoadingTime`) | |
| Blocking | Block power (`ItemData.GetBlockPower(skillFactor)`), perfect-block SE level | `Humanoid.BlockAttack` |
| Run | Stamina drain x`lerp(1,0.5)`, speed +25 % | `Player.CheckRun`, `Player.GetRunSpeedFactor` |
| Jump | Jump force +40 % | `Character.Jump` |
| Swim | Swim stamina 5→2 /s | `Player.OnSwimming` |
| Sneak | Stamina x`lerp(1,0.25,sqrt f)`, stealth factor | `Player.OnSneaking`, `Player.UpdateStealth` |
| Dodge | Dodge stamina x`lerp(1,0.5)` | `Player.GetDodgeStaminaUse` |
| Ride | Mount control | `Sadle.ApplyControlls` |
| Fishing | Hooked stamina drain | `FishingFloat.FixedUpdate` |
| Farming | Harvest radius (`Piece.m_harvestRadius→m_harvestRadiusMaxLevel`, scythe radius in `Attack.DoMeleeAttack`), bonus yield chance (`Pickable.m_maxLevelBonusChance`) | `Piece.OnPlaced`, `Pickable.Interact` |
| Cooking | Cooking station results | `CookingStation.OnInteract` |
| Crafting | Craft duration, crafting results, repair skill gain | `InventoryGui.UpdateRecipe`, `InventoryGui.DoCrafting` |
| Build tools | Placement stamina -50 %, durability | `Player.GetBuildStamina`, `Player` placement durability |
| Blood magic etc. | Summon level/max instances by skill threshold | `SpawnAbility.m_levelUpSettings` |

### Patch points
`Skills.RaiseSkill`, `Skills.GetSkillLevel`, `Player.OnSkillLevelup`, `Skills.LowerAllSkills`, `SkillsDialog.Setup` (per-skill tooltip is `skill.m_info.m_description` - append perk info here).

---

## 8. Camera & zoom

### Key classes
`GameCamera` (singleton `GameCamera.instance`, on the main camera; `m_skyCamera` mirrors FOV), `PlayerController` (mouse look, static `m_mouseSens`, `m_invertMouse`).

### Flow
* `GameCamera.LateUpdate` → `UpdateFOV` (temp-FOV interpolation) → `UpdateBaseOffset` → `UpdateMouseCapture` → `UpdateCamera(Time.unscaledDeltaTime)` → `UpdateListner`.
* `GameCamera.UpdateCamera`: sets `fieldOfView = m_fov` (65); if no UI blocks input, scroll wheel (`m_zoomSens` 10) or gamepad alt-keys change private `m_distance`, clamped to `[m_minDistance, m_maxDistance (6)]`, or `m_maxDistanceBoat` when the local player controls a ship. Dead → look at ragdoll; attached with `GetAttachCameraPoint` → fixed attach camera; else `GetCameraPosition`.
* `GameCamera.GetCameraPosition`: eye position + offset (`GetCameraOffset`: `m_fpsOffset` when `m_distance <= 0`, i.e. **first person is simply distance 0**; else `m_3rdOffset` / `m_3rdCombatOffset` when `Humanoid.UseMeleeCamera`), `CollideRay2` wall collision, `UpdateNearClipping`, **water clamp** (`m_minWaterDistance`), optional ship tilt (`ApplyCameraTilt`, pref `ShipCameraTilt`).
* **Temp FOV API**: `GameCamera.SetTempFOV(target, inertia)` stores `m_fovBase` on first use and animates `m_fov` toward the target; `ResetTempFOV()` returns to base. Currently only used by `GrapplingPoint` (1.0 grappling).
* Free-fly debug camera (`ToggleFreeFly`, `m_freeFlyMinFov/MaxFov`).

### Multiplayer
Purely local.

### Patch points
`GameCamera.UpdateCamera` (private), `GameCamera.GetCameraPosition` (private), `SetTempFOV`/`ResetTempFOV` (public API), public fields `m_maxDistance`, `m_maxDistanceBoat`, `m_minWaterDistance`, `m_fov`; `PlayerController.m_mouseSens` (static) for zoomed sensitivity.

---

## 9. Equippable lights and the utility slot

### Key classes
`Humanoid` (equipment fields `m_rightItem`, `m_leftItem`, `m_chestItem`, `m_legItem`, `m_helmetItem`, `m_shoulderItem`, `m_utilityItem`, `m_trinketItem`, `m_ammoItem`, hidden hand items), `ItemDrop.ItemData.ItemType` (`Torch = 15`, `Utility = 18`, `Trinket = 24`, ...), `VisEquipment` (visual attachment + replication), `SE_Demister` (Wisplight ball).

### Flow
* `Humanoid.EquipItem`: `Torch` goes to the left hand if a one-handed weapon is in the right hand, otherwise to the right hand (unequipping non-shield left items). `Utility` replaces `m_utilityItem` (**one utility slot**: Megingjord `BeltStrength`, `Wishbone`, Wisplight `Demister`); `Trinket` has its own slot (1.0). Utility/Trinket items below the current world level are refused in NG+ worlds (`Game.m_worldLevel`).
* `Humanoid.UpdateEquipment`: durability drain for right/left/utility/trinket items with `m_useDurability`; hides hand items while swimming (torches go out of view; a utility item stays).
* Equip status effects (`m_shared.m_equipStatusEffect`) are collected in `Humanoid.UpdateEquipmentStatusEffects` (the Wisplight uses `SE_Demister`, which spawns and steers a local ball prefab).
* **Visual replication**: `VisEquipment.SetUtilityItem(hash)` writes ZDO `UtilityItem` (int prefab hash). Every client's `VisEquipment.UpdateEquipmentVisuals` reads it and `SetUtilityEquipped` → `AttachArmor(hash)`: for each child of the item prefab named `attach_<BoneName>` it instantiates the child under that bone of the player skeleton (`attach_skin` = skinned mesh). Hand items use the `attach` child (`AttachItem`). **Lights inside those children render on all clients that have the prefab** in `ObjectDB`.
* Vanilla `Lantern` (`Assets/GameElements/Items/weapons/Lantern.prefab`, `$item_lantern`) is a `Torch`-type hand item.

### Patch points
`Humanoid.EquipItem` / `UnequipItem`, `Humanoid.UpdateEquipment` (private), `VisEquipment.SetUtilityEquipped` / `AttachArmor` (private), `ObjectDB.Awake` / `ObjectDB.CopyOtherDB` (register cloned items; or Jotunn `ItemManager`).

---

## 10. Skill-gated abilities in vanilla

Vanilla has **no discrete "unlock at level X" abilities**; skills scale continuous values (table in section 7). The only threshold mechanisms are:
* `SpawnAbility.m_levelUpSettings` (`LevelUpSettings`: `m_skill`, `m_skillLevel`, `m_setLevel`, `m_maxSpawns`): summons get a higher level/more instances when the caster's skill reaches a threshold (Blood Magic staffs).
* `SE_Stats.m_skillLevel` + `m_skillLevelModifier`: temporary skill boosts that feed every factor.
* `ItemDrop.ItemData.GetStatusEffectTooltip(quality, skillLevel)`: skill-dependent tooltip values.

So a perk system is new content; the clean hook set is `Skills.GetSkillLevel` (effective level incl. SE boosts), `Player.OnSkillLevelup` (announce unlocks), `Skills.LowerAllSkills` (death penalty can drop you below a threshold), `SkillsDialog.Setup` (show perks).

---

## Cross-cutting notes for this chapter

* **Persistence choices**: per character → `Player.m_customData` (strings, saved in the character, vanilla-safe if the mod is removed: data is kept and ignored). It is saved on autosave, logout and by `Game._RequestRespawn` before the dead player is destroyed (so it survives death), and loaded by `Game.SpawnPlayer` → `PlayerProfile.LoadPlayerData` just before `Player.OnSpawned`. From `_RequestRespawn` (about 10 s after death) until the new spawn, `Player.m_localPlayer` is null while the game and profile still exist. Per item → `ItemDrop.ItemData.m_customData`. Per world object → ZDO custom keys (namespace them, e.g. `"vm.sailing.skill"`). Per profile/world map → do not change the `Minimap.GetMapData` format.
* **Custom items vanish without the mod**: `Inventory.AddItem(int prefabHash, ...)` logs "Failed to find item prefab" and returns false, so a spyglass/hip lantern is **deleted from inventories** that are loaded (and then saved) without the mod.
* **Private members**: most patch targets here are private (`UpdateSwimming`, `UpdateExplore`, `Explore`, `UpdateSleeping`, `CalculateCanSleep`, `GetSailForce`...). Use a publicized `assembly_valheim` reference for compile-time access and `AccessTools`/`Traverse` in patches.
* **Custom skills** need injection (see section 7). Either depend on Jotunn's `SkillManager` or implement a small shared injector in the repo's Core library; pick one project-wide to avoid double registration.
* **Owner-authoritative physics**: anything touching `Ship` or other players' characters must run where the ZDO is owned, which makes it "everyone" in practice.

---

## Feature ideas

### Cartography table revamp - "More pins when near table" (QoL)
Interpretation: when the large map is used at / near a cartography table, the player gets an **extended pin palette** (more icons, optionally colours), and table-related tools (e.g. copy/share selected pins). Alternative reading ("the table shows more pins") is covered by the same hooks (`AddSharedMapData`).

* **Feasibility**: medium (UI cloning + enum extension; data format unchanged).
* **Who needs the mod**: client-only. Pins are local and table data stores the type as int; unmodded players who read the table see extended pins as `Icon3` (graceful degradation). Everyone should install for identical icons.
* **Hooks**: `Minimap.Start` (postfix: extend `m_visibleIconTypes`, `m_icons`, `m_selectedIcons`, clone the `Icon4` button GameObject and wire it to private `SelectIcon`/`ToggleIconFilter`), `Minimap.AddPin` (types ≥ 18 now accepted once the array is extended), `Minimap.SetMapMode` (show/hide extra palette), `MapTable.Start` postfix (register tables for the proximity check) or `MapTable.OnRead` (open the large map in "cartography mode"), `Minimap.GetSharedMapData`/`AddSharedMapData` (optional filtering), `Minimap.UpdatePins` (tint by pin colour).
* **Sketch**: Reserve custom `PinType` ints (e.g. 100+) and register sprites (vanilla location icons from `Minimap.m_locationIcons` or item icons can be reused). In `Minimap.Start` postfix resize `m_visibleIconTypes` to cover them and append `SpriteData` entries; build a second icon row that is only active while the local player is within N m of a registered `MapTable` (or the map was opened via the table). Optional extras: a "copy pin to table" action, pin colours stored as a prefix in the pin name.
* **Risks**: Array resize must happen before the first `LoadMapData` or saved custom pins are downgraded to `Icon3` on load; other pin mods (Pinnacle, AutoMapPins-style mods, Jotunn `MinimapManager` overlays) may also touch `m_icons`/`m_visibleIconTypes`: coordinate the id range and append instead of replacing. Removing the mod keeps pins but as `Icon3`.

### Sleep through the day (QoL)
* **Feasibility**: easy.
* **Who needs the mod**: everyone (server makes the skip; bed-entry gating is client-side and all players must be in bed; unmodded clients simply cannot lie down in the morning, which blocks the skip safely).
* **Hooks**: `EnvMan.CalculateCanSleep` (postfix, allow `IsDay()` mornings, keep the `m_sleepCooldownSeconds` check), `Bed.Interact` (prefix: e.g. alt-interact = "sleep until evening", write intent to the player ZDO), `Game.UpdateSleeping` (server: new branch for daytime), `EnvMan` private `m_skipTime`/`m_skipToTime`/`m_timeSkipSpeed`, `Game.EverybodyIsTryingToSleep`, `Player.SetSleeping` (wake message).
* **Sketch**: Client: allow sleeping when `EnvMan.IsDay() && !IsAfternoon()` (vanilla already allows afternoon → next morning) and set ZDO key `vm.sleep.mode=evening` on the player before `AttachStart`. Server: in a `Game.UpdateSleeping` prefix, if everyone is in bed and it is morning, set the three EnvMan skip fields to `day*m_dayLengthSec + m_dayLengthSec*X` (X configurable, raw fraction ~0.80 = dusk; remember the raw→rescaled mapping) with speed `delta/12`, set `Game.m_sleeping` and send `SleepStart`; vanilla's stop branch finishes. Patch `Player.SetSleeping(false)` to show "good evening" instead of "good morning" when the mode was evening.
* **Risks**: Time jumps advance everything that uses net time (plants, smelters, fermenters, respawn timers) exactly like vanilla sleep, but twice per day now: balance. `Rested` is granted on every wake. Day-length mods (ValheimPlus time settings, etc.) change `m_dayLengthSec`: always compute from the live field. Use `ZNet.GetTimeSeconds()`, never Unity time.

### Per creature kill count (QoL)
* **Status**: implemented as [Creature Kill and Tame Counts](../../src/Exploration/Stats.PerCreature) (0.1.0). The shipped design shows the list only at the top of Compendium > Player Statistics (no nameplate or trophy tooltip), adds per-creature **tame** counts (vanilla has only a total, so the mod counts them itself from the "has been tamed" message, see [farming-cooking.md §6](farming-cooking.md)), and exposes a public read API (`CreatureCounts`) for a future Compendium. See [docs/design/exploration-stats-per-creature.md](../design/exploration-stats-per-creature.md).
* **Feasibility**: trivial (data already exists).
* **Who needs the mod**: client-only.
* **Hooks**: read `Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[0]` (key = `Character.m_name`); display in `EnemyHud.UpdateHuds`/`ShowHud` (private, append to `m_name` TMP text), `InventoryGui.UpdateTrophyList` (private, append to the trophy description), `TextsDialog.AddStats` (private, list per-enemy lines), optionally `Game.RPC_RegisterKill` postfix for "kill #N" messages.
* **Sketch**: Build a `trophy prefab → Character.m_name` map once (scan `ZNetScene.instance.m_prefabs` for `Character` + `CharacterDrop` and read trophy drops) so the trophy panel can show "Killed: N (melee x / ranged y / magic z)". Add the count to the enemy nameplate and to a bestiary page (see Compendium). Retroactive: existing characters already have the counts.
* **Risks**: Counts are per character across all worlds, not per world. Key is the localization token, shared by creature variants that reuse `m_name`; PvP victims from saves of older game versions can be in the same dictionary (1.0.16 no longer records them; filter keys that do not start with `$`). Achievement difficulty buckets (indices 1+) are not what players expect: always read index 0. Minor UI conflicts with nameplate mods.

### Sailing revamp - sailing skill (New; partially exists: Nexus mod 922)
Skill gains make it easier to catch the wind, turn, stop and accelerate, and widen the map reveal radius at sea.

* **Feasibility**: medium (custom skill injection + owner-side physics scaling).
* **Who needs the mod**: everyone (physics runs on the ship ZDO owner, who may be a passenger).
* **Hooks**: custom skill registration (Jotunn `SkillManager` or patches on `Skills.IsSkillValid`, `Skills.GetSkillDef`, `Skills.Load`), `ShipControlls.RPC_RequestControl` (write helmsman skill factor to ship ZDO key and optionally `SetOwner` to the helmsman), `Ship.UpdateOwner` (keep helmsman owner), `Ship.CustomFixedUpdate` (scale `m_sailForceFactor`, `m_stearForce`, `m_stearVelForceFactor`, `m_backwardForce` in prefix/restore in postfix), `Ship.GetWindAngleFactor` (widen the usable angle: raise the 0.7 floor, move the 0.75/0.8 headwind cut-off), `Ship.GetSailForce` (shorter SmoothDamp time = faster acceleration, higher low-wind floor than 0.25), `Ship.ApplyControlls` (rudder speed, "instant stop" key calling the unused `Ship.Stop`), `Minimap.UpdateExplore` / `m_exploreRadius` (bigger reveal when `Ship.GetLocalShip()` is set), `Player.Update` postfix for XP.
* **Sketch**: Register `Sailing` skill; award XP every few seconds to the helmsman while `GetControlledShip()` has sail up and speed > threshold (bonus for good wind angle). On taking the helm, write `vm.sail.skill` (float factor) to the ship ZDO; the owner's `Ship.CustomFixedUpdate` prefix reads it and scales tuning fields, restoring them in the postfix so prefab values are never permanently changed. Map reveal radius lerps from 100 to e.g. 200-300 m by skill while aboard.
* **Risks**: Must never double-apply scaling (restore in postfix, handle exceptions with finalizer). Mixed-mod crews desync (unmodded owner ignores the skill). Conflicts with ship-physics mods (ValheimRAFT, sail-speed/wind mods, ValheimPlus-style multipliers) and with the linked Nexus mod: check their patches. Skill id must be stable (Jotunn uses a name hash); removing the mod drops the skill data from saves (`Skills.Load` filters it). Wind overrides via `EnvMan` affect windmills/cloth globally: avoid, scale the ship's use of wind instead.

### Spyglass (New)
* **Feasibility**: medium (easy for a keybind MVP).
* **Who needs the mod**: depends: a keybind/utility-requirement version is client-only; a real held item needs everyone for visuals and to avoid item loss.
* **Hooks**: `GameCamera.SetTempFOV`/`ResetTempFOV` (public), `GameCamera.UpdateCamera` (private; block scroll-zoom, force `m_distance = 0` first person while scoped), `PlayerController.m_mouseSens` (scale while zoomed), `Player.Update` postfix (input, equipped check via the public `Humanoid.RightItem`/`LeftItem` properties or `Humanoid.GetInventory()`), custom item via Jotunn `ItemManager` or `ObjectDB.Awake` clone, optional `Minimap.AddPin` ("mark target") and `Minimap.Explore(Vector3,float)` ("survey" a small radius at the look point), `Hud` overlay.
* **Sketch**: Hold a key (or secondary action when the spyglass is equipped) to switch to first person, `SetTempFOV(65/zoom)` and scale mouse sensitivity; show a vignette overlay and, via a long `Physics.Raycast` from the camera, the name/distance of the hovered `Character` or location. Optional key drops a map pin at the hit point, making it a natural companion of the cartography revamp.
* **Risks**: Objects outside the synced simulation distance (`ZNet.GetSyncedSimulationDistance`, `ZNetScene` active area) do not exist, so far creatures cannot be seen, only terrain/distant LOD. `GrapplingPoint` also uses the temp-FOV API (`m_fovBase` captured once): reset properly. Camera mods (first-person mods, camera distance mods) may fight over `m_distance`/FOV. Held-item version needs a model/icon (assets) and is deleted from inventories without the mod.

### Swim dive (New; partially exists as community mods)
* **Feasibility**: hard (physics is easy, presentation is not).
* **Who needs the mod**: client-only for the mechanic (owner-simulated movement, others see the synced position); everyone for consistent visuals (dive pose/tilt).
* **Hooks**: `Character.UpdateSwimming` (postfix: override `m_body.linearVelocity.y` from Crouch/Jump input for the local player), `Character.UpdateWater`/`IsSwimming` (keep swimming state at depth; deeper is fine because the check is depth > `m_swimDepth - 0.4`), `Player.OnSwimming` (breath meter/drowning), `Player.UpdateCrouch` (crouch is cancelled while swimming, so read `ZInput` "Crouch"/"JoyCrouch" directly), `GameCamera.GetCameraPosition` (remove water clamp while submerged), `Character.UnderWorldCheck` (ground-height teleport), `Hud` (breath bar).
* **Sketch**: While swimming, holding Crouch sets a dive vertical speed and suppresses vanilla's pull toward `liquidLevel - m_swimDepth`; releasing it lets buoyancy return the player (optionally hold depth with Jump/Crouch). Track "head under water" (`Character.GetHeadPoint().y < GetLiquidLevel()`), drain a breath value stored on the player component, apply `HitType.Drowning` damage when empty. When the camera is below the surface, disable the clamp and apply an underwater look (fog colour/density override, screen tint).
* **Risks**: The water volume trigger must still contain the player at depth, otherwise `WaterVolume.OnTriggerExit` sets the level to -10000 and the player falls (verify with debug logs per biome/ocean depth). The ocean surface shader may be invisible from below and there is no underwater post-processing: needs custom work/assets (dive animation, underwater effect). Ashlands water (`Character.UpdateAshlandsWater`) and tar behave differently. Other clients see an upright swimmer sinking unless they also run the mod.

### Hip lantern as utility item (New; exists already as a community mod)
* **Feasibility**: easy.
* **Who needs the mod**: everyone (the wearer's `UtilityItem` hash is replicated; clients without the prefab log "Missing attach item" and simply show nothing).
* **Hooks**: item registration (`ObjectDB.Awake`/`CopyOtherDB` postfix or Jotunn `CustomItem` cloning `Lantern`), `Humanoid.EquipItem` (Utility branch), `VisEquipment.AttachArmor` (uses `attach_<Bone>` children), `Humanoid.UpdateEquipment` (durability drain applies to utility items), recipe via `ObjectDB.m_recipes`.
* **Sketch**: Clone `Lantern`, set `m_shared.m_itemType = Utility`, rename/describe, and add a child `attach_Hips` (or a thigh bone) containing the lantern's visual + `Light` under an offset holder (AttachArmor zeroes local position/rotation of the attach child). Copy durability settings from the Lantern and reduce light range to keep torches relevant. Because utility visuals are not hidden while swimming, the light stays on in water.
* **Risks**: Occupies the single utility slot (competes with Megingjord/Wishbone/Wisplight); an alternative is an extra equipment slot, which conflicts with slot mods (ExtraSlots, AzuExtendedPlayerInventory, Equipment & Quickslots). Items disappear if the mod is removed. Light count/performance with many players (limit range, disable shadows).

### Compendium (New)
Vanilla already has a basic "Compendium" (`TextsDialog`: log, active effects, collected lore texts, raw stats dump). The idea is a real encyclopedia: bestiary, lore, re-readable tutorials, biomes, discovered items.

* **Feasibility**: medium (mostly UI work, data is available).
* **Who needs the mod**: client-only.
* **Kill and tame counts**: read them from [Creature Kill and Tame Counts](../../src/Exploration/Stats.PerCreature) (`CreatureCounts` API, or its documented `Player.m_customData` keys without a reference: see that mod's README, "For mod authors"). Per-creature tames exist only through that mod.
* **Hooks**: `TextsDialog.UpdateTextsList` / `FillTextList` / `ShowText` (private) or a new panel opened from `InventoryGui.OnOpenTexts`; data: `Player.GetKnownTexts`, private `m_shownTutorials` + `Tutorial.instance.m_texts` (re-read raven tips), `m_knownBiome`, `m_knownMaterial`, `GetTrophies`, `PlayerProfile.m_playerStats[0]` (kills, pickups, crafts, food eaten), `ZNetScene` prefabs (`Character`, `CharacterDrop`) and `ObjectDB.m_items` for static info; `Player.AddKnownText` postfix to track discovery order in `Player.m_customData`.
* **Sketch**: Category tabs (Bestiary / Lore / Tutorials / Biomes / Items / Stats) built by cloning the existing TextsDialog list element; bestiary entries unlock on first kill/trophy and show icon, biome, kill counts, known drops (only those the player has picked up: `m_knownMaterial`), and weaknesses from `Character.m_damageModifiers` once killed N times. Tutorials tab lists `Tutorial.m_texts` whose key is in `m_shownTutorials`.
* **Risks**: Must not spoil content (gate everything on player knowledge). Localization of new texts (Jotunn `LocalizationManager` or `Localization.instance.AddWord`). UI mods restyling the inventory (e.g. Auga-style UI overhauls) may break cloned UI paths (`Utils.FindChild` names). Large text rebuilds each open: cache.

### New ability depending on skill level (New)
* **Feasibility**: medium for the framework; each perk varies (easy to hard).
* **Who needs the mod**: depends on the perk: movement/self perks are client-only (owner-authoritative player), perks affecting ships/creatures/other players need everyone, world-state perks need the server.
* **Hooks**: `Skills.GetSkillLevel` (effective level, includes `SE_Stats` boosts), `Player.OnSkillLevelup` (unlock notification), `Skills.RaiseSkill`, `Skills.LowerAllSkills`/`Skills.OnDeath` (losing a threshold on death), `SkillsDialog.Setup` (list perks in the tooltip), `Player.m_customData` (persist "highest level reached" or chosen perks); perk-specific hooks (e.g. `Character.UpdateSwimming` for a Swim perk, `Character.Jump` for Jump, `Player.CheckRun` for Run, `Ship.*` for Sailing).
* **Sketch**: A small shared "SkillPerks" library: perks declared as `(SkillType, minLevel, id, description, IsActive(player))`, evaluated from `GetSkillLevel` so meads can temporarily unlock them; other mods (Swim dive at Swim 25, faster dive at 50; Sailing instant-stop at 30; Run slide; Jump ledge-grab) query `Perks.Has(player, id)`. `Player.OnSkillLevelup` postfix shows an unlock message; `SkillsDialog.Setup` postfix appends locked/unlocked perks per skill.
* **Risks**: Death penalty (-25 % levels by default, world modifier `Game.m_skillReductionRate`) can remove perks unexpectedly: decide between "current level" and "peak level" gating. Balance with skill-changing mods (e.g. skill XP multipliers, Jotunn-based skill mods). Keep perk logic owner-side to avoid desync; never trust a remote player's skill for authoritative effects without syncing it via ZDO.
