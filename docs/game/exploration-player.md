# Exploration - Player Movement, Navigation & Progression

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> Conventions: code is cited as `Class.Method` (file = `.ref/decompiled/assembly_valheim/<Class>.cs`).
> "Owner" means the owner of the object's ZDO (`ZNetView.IsOwner()`); "server" means `ZNet.instance.IsServer()` (host in listen servers, the dedicated server otherwise).
> Field defaults quoted below are the **C# initialisers**. Many are overridden by serialized prefab values (Player prefab, ship prefabs, `_GameMain` scene objects). When a number matters for balance, log the live value at runtime before relying on it.
> Values marked "runtime dump, 1.0.16" were read in the game by the in-world self-tests of Sailing Skill and Swim Dive (`./tools/Test-InWorld.ps1`, 2026-10-01).

---

## Overview

| System | Main classes | Authority | Persistence |
|---|---|---|---|
| Swimming | `Character`, `Player`, `WaterVolume`, `LiquidSurface`, `Floating` | Local player (owner of own character ZDO) | none (skill level in player save) |
| Sailing | `Ship`, `ShipControlls`, `EnvMan` (wind), `Hud.UpdateShipHud` | Ship ZDO owner (a player on board while anyone is aboard) simulates physics; helmsman sends RPCs | ZDO: `forward`, `rudder`, `user` |
| Map & minimap | `Minimap`, `MapTable`, `Game` (location discovery), `Chat` (pings) | 100% client-local; map table data lives in the table ZDO | Player profile per world (`PlayerProfile.SetMapData`), table ZDO `data` |
| Sleep & time | `Bed`, `Game.UpdateSleeping`, `EnvMan`, `Player.SetSleeping`, `SleepText` | Server decides and skips time (`ZNet.SetNetTime`) | Net time in world save; bed owner in bed ZDO |
| Player statistics | `PlayerProfile`, `PlayerStatType`, `KillModifiers`, `Game.RPC_RegisterKill` | Each client's own profile; kills are pushed by the creature's owner via routed RPC | `.fch` profile |
| Lore / compendium / tutorials | `TextsDialog`, `Tutorial`, `Raven`, `RuneStone`, `TextViewer`, `InventoryGui` (trophies) | Client-local | Player save (`m_knownTexts`, `m_shownTutorials`, `m_trophies`...) |
| Skills | `Skills`, `Skills.SkillDef`, `Skills.SkillType`, `SkillsDialog` | Local player | Player save (`Skills.Save/Load`) |
| Camera | `GameCamera`, `PlayerController` | Client-local | `PlatformPrefs` settings only |
| Lights & utility slot | `Humanoid.EquipItem`, `VisEquipment`, `SE_Demister` | Owner equips; visuals replicated via ZDO item hashes | Inventory in player save |
| Comfort, resting & music | `SE_Rested`, `SE_Cozy`, `Player.UpdateBaseValue`, `AudioMan`, `MusicMan` | Local player computes its own comfort and effects; music is client-local | none (effects are not saved) |

Key take-aways for this area:

* Almost everything here is **client-authoritative** (player movement, map, stats, lore, camera). Only **sleep/time** is server-authoritative and **ship physics** runs on whichever client owns the ship ZDO.
* `Player.m_customData` (`Dictionary<string,string>`, saved in `Player.Save`, **unused by vanilla**) is the natural per-character persistence slot for mods in this chapter.
* Custom skills and custom pin types are rejected by vanilla code paths (enum checks) and need explicit injection (see Skills and Minimap).

---

## 1. Swimming

### Key classes
* `Character`: generic swim physics for every creature, including the player. Fields `m_canSwim`, `m_swimDepth` (default 2; **1.5 on the Player prefab**), `m_swimSpeed` (2), `m_swimTurnSpeed` (100), `m_swimAcceleration` (0.05); the Player prefab keeps those last three (runtime dump, 1.0.16). Private state `m_waterLevel`, `m_tarLevel`, `m_liquidLevel`, `m_swimTimer`, `m_cashedInLiquidDepth`.
* `Player`: swim stamina, drowning, Swim skill gain (`Player.OnSwimming` override), `m_swimStaminaDrainMinSkill` and `m_swimStaminaDrainMaxSkill` (code 5/s at skill 0 and 2/s at skill 100; **6/s and 3/s on the Player prefab**, runtime dump, 1.0.16). The player's collider is 1.85 m tall (radius 0.49), the eye 1.79 m above the feet, and the body mass 10 (runtime dump).
* `WaterVolume` (ocean/lakes, one per heightmap tile) and `LiquidSurface` (tar pits etc.) push the surface height into anything implementing `IWaterInteractable` (`Character`, `Floating`, `Fish`). An ocean volume's trigger spans world height −20 to 40 over its 64 × 64 m tile, deep enough to reach the sea floor where tested; its surface renderer (`m_waterSurface`, shader `Custom/Water`) is a separate object at the volume's position, not a child of it (runtime dump, 1.0.16).
* `Floating` static helpers: `Floating.GetLiquidLevel(Vector3)`, `Floating.GetWaterLevel(Vector3, ref WaterVolume)`, `Floating.IsUnderWater(Vector3, ref WaterVolume)`.

### Flow
1. `WaterVolume.OnTriggerEnter` registers the rigidbody's `IWaterInteractable` and increments its per-liquid counter (`Character.Increment(LiquidType)`); `WaterVolume.UpdateFloaters` then calls `Character.SetLiquidLevel(surfaceY, LiquidType.Water, ...)` once per rendered frame (from `MonoUpdaters.Update`) using `WaterVolume.GetWaterSurface` (includes waves; it depends only on x/z, never on how deep the character is). `LiquidSurface` (tar) updates its floaters in FixedUpdate instead. `OnTriggerExit` resets the level to -10000 when the counter reaches 0.
2. `Character.CustomFixedUpdate` (every character, every client): `CalculateLiquidDepth` (depth = liquid level - feet Y; forced to 0 when standing on / attached to a ship or teleporting) then `UpdateWater`.
3. `Character.UpdateWater`: if `m_canSwim` and depth > `max(0, m_swimDepth - 0.4)` it resets `m_swimTimer`. **`IsSwimming()` is simply `m_swimTimer < 0.5`.** The owner also applies the `Wet` (or `Tared`) status effect.
4. Owner only: `Character.UpdateMotion` (private) is the per-owner-tick movement entry (swim, fly or walk). It returns early when the character is dead, runs `UpdateDebugFly` instead in debug fly, and picks `UpdateSwimming` when `IsSwimming()`. Vanilla debug fly already maps Jump to up and LeftControl / a held `JoyCrouch` to down (`Character.UpdateDebugFly`).
5. `Character.UpdateSwimming` (private) is the core rule:
   * horizontal velocity: `m_moveDir * m_swimSpeed` (scaled by attack-speed factor and `SEMan.ApplyStatusEffectSpeedMods`), lerped with `m_swimAcceleration` for players; the applied force has **y = 0**, so move input never drives vertical motion;
   * vertical: the body is pushed toward the target height `GetLiquidLevel() - m_swimDepth` (up to +10 m/s when below it, down to -10 m/s when above it). **This single target height is what keeps characters at the surface - there is no dive in vanilla 1.0.16.** Gravity stays on: the method sets `m_body.useGravity = true` every tick, and this velocity rule cancels it;
   * it resets `m_maxAirAltitude` every tick, so there is no fall damage while swimming;
   * rotation uses `m_swimTurnSpeed`; animator gets `inWater = !IsOnGround()`; then calls virtual `OnSwimming(targetVel, dt)` only when the character was **not on ground** at the start of the call. On the sea floor there is no swim drain and no drowning. `Character.IsOnGround()` is also true while the rigidbody sleeps (`m_body.IsSleeping()`).
6. `Player.OnSwimming`: while moving, drains stamina `lerp(m_swimStaminaDrainMinSkill, m_swimStaminaDrainMaxSkill, SwimSkillFactor)` per second (6 to 3 on the Player prefab), plus equipment swim modifier (`Player.GetEquipmentSwimStaminaModifier`, modifier index 7) and `SEMan.ModifySwimStaminaUsage`, times `Game.m_moveStaminaRate`; raises `Skills.SkillType.Swim` every 1 s of movement. With no stamina: every 1 s deals `ceil(maxHP/20)` damage with `HitData.HitType.Drowning`. Each tick goes through `Character.Damage` as an unblockable hit, so it also costs `m_nonBlockDamageAdrenaline` (0 on the Player prefab: [combat.md §8](combat.md)).
7. Side rules while swimming (not on ground):
   * `Player.UpdateStats(float dt)` (the stamina/food overload; not the parameterless distance-stats overload) sets the stamina regen multiplier to 0 (only while `IsSwimming() && !IsOnGround()`, so regen resumes on the sea floor);
   * `Humanoid.UpdateEquipment` hides hand items (`HideHandItems`). Nothing re-shows them when the player leaves the water: `Player.Update` re-shows them only when the player presses the Hide key, and only when `!IsSwimming() || IsOnGround()`;
   * `Humanoid.EquipItem` refuses to equip anything;
   * `Player.UpdateCrouch` cancels crouch;
   * `Character.Jump` works in water only within swim depth (`InLiquidSwimDepth()`) and when the character touched the world in the last 0.25 s: any contact with a `Character.s_groundRayMask` collider counts (floor, rock, wall or ship hull; `Character.OnCollisionStay` resets `m_hitWorldTime`).
8. `GameCamera.GetCameraPosition` clamps the camera to `liquidLevel + m_minWaterDistance` (code 0.3, **0.4 on the prefab**, runtime dump, 1.0.16), so the camera can never go under water (details in section 8).
9. The surface ripple and water effect follow a submerged character: `Character.UpdateContinousEffects` places them at `GetLiquidLevel() + 0.05`, outside the owner block, so every client draws them.

### Data & persistence
No swim state is persisted or synced beyond the character transform (ZSyncTransform) and animator bools (`inWater`). Swim skill lives in `Skills`.

### Multiplayer authority
The character owner (for the player: the local client) runs `UpdateMotion`/`UpdateSwimming`; everyone else only sees the synced transform and animator parameters. A movement mod for the local player is therefore **client-only** in function.

### Patch points
| Target | Why |
|---|---|
| `Character.UpdateSwimming` (private) postfix | Override vertical velocity after vanilla (dive/ascend), filter `this == Player.m_localPlayer`. |
| `Character.UpdateMotion` (private) prefix | Per-owner-tick hook for the local player's movement state; runs before swim, walk or fly, also on ticks where `UpdateSwimming` is not called. |
| `Character.UpdateWater` / `IsSwimming` | Change what counts as swimming (depth threshold). |
| `Player.OnSwimming` (protected override) | Swim stamina, drowning, skill gain, breath meters. |
| `Player.GetEquipmentSwimStaminaModifier` | Equipment-based swim buffs. |
| `GameCamera.GetCameraPosition` (private) | Remove/relax the above-water camera clamp. |
| `GameCamera.UpdateCamera` (private) postfix | The frame's final camera position, after `EnvMan.SetEnv`: an under-water view (fog, surface) set here holds for the frame (section 8). |
| `Character.SetLiquidLevel` | Observe liquid surface per character. |

---

## 2. Sailing

### Key classes
* `Ship` (on the 5 prefabs `Raft`, `Karve`, `VikingShip`, `VikingShip_Ashlands` and `Trailership`, which has no sail; runtime dump, 1.0.16; also implements `IMonoUpdater`). Physics, sail state, rudder, wind force, damage, ownership hand-off. The private `m_players` list holds remote players too (every game fills it in `Ship.OnTriggerEnter`); the helmsman is the one whose player id equals `ShipControlls.GetUser()` (`ShipControlls.HaveValidUser`, `Ship.HaveControllingPlayer`).
* `ShipControlls` (sic): the helm; `Interactable` + `IDoodadController`. Handles control requests and attaches the player.
* `EnvMan`: global wind (`GetWindDir`, `GetWindIntensity`, `UpdateWind`).
* `Hud.UpdateShipHud`: sail/rudder/wind UI. `Minimap.UpdateWindMarker`: wind arrow on the minimap.
* `Player.GetControlledShip` (doodad controller is a Ship) vs `Character.GetStandingOnShip` / `Ship.GetLocalShip` (ship volume the local player is in).
* `ShipEffects`: wake/sound only.

### Flow
1. **Taking the helm**: `ShipControlls.Interact` (player must stand on this ship and not be encumbered) → RPC `RequestControl(playerID)` to the ship owner → `ShipControlls.RPC_RequestControl` writes ZDO `user` if free (or already this player) and replies `RequestRespons` → `Player.StartDoodadControl` (there is no `StartShipControl` in 1.0.16) + `Player.AttachStart(..., onShip: true, ...)`. Releasing: `ShipControlls.OnUseStop` → RPC `ReleaseControl`. The request carries only the player id, and skills are local to each game, so the owner cannot read the helmsman's skills: the helmsman's own game has to publish them (on its player ZDO or with its own RPC).
2. **Input**: `Player.SetDoodadControlls` → `ShipControlls.ApplyControlls` → `Ship.ApplyControlls(moveDir)`:
   * forward/back edge-triggered → RPC `Forward` / `Backward` (owner steps `Ship.Speed`: `Back ↔ Stop ↔ Slow ↔ Half ↔ Full`);
   * rudder: `m_rudderValue += dir.x * lerp(0.5,1,|rudder|) * m_rudderSpeed * dt`, clamped [-1,1], sent via RPC `Rudder` at most every 0.2 s. This is the only use of `m_rudderSpeed`, and it runs on the helmsman's game;
   * `Ship.Stop()` / RPC `Stop` exists but **nothing in vanilla calls it** (free hook for an "instant stop" key).
3. **Simulation** `Ship.CustomFixedUpdate`, every physics step (0.02 s; runtime dump, 1.0.16). All clients run `UpdateControlls`, `UpdateSail`, `UpdateRudder` for visuals (`UpdateSail` turns the mast and `UpdateSailSize` sets the cloth and effects; neither adds a force); **only the owner** continues:
   * forces speed to `Stop` when nobody is aboard and `Slow/Back` to `Stop` when nobody holds the helm;
   * buoyancy from 5 water samples around `m_floatCollider` (`m_force`, `m_forceDistance`, `m_waterLevelOffset`), damping (`m_damping`, `m_dampingForward`, `m_dampingSideway`, `m_angularDamping`), strong horizontal damping (x0.1) when no player aboard;
   * **sail force** `Ship.GetSailForce(sailSize, dt)`: `sailSize` 0.5 (Half) or 1 (Full); target = `normalize(windDir + forward) * GetWindAngleFactor() * lerp(0.25, 1, windIntensity) * m_sailForceFactor * sailSize`, smoothed with `Vector3.SmoothDamp` (the smooth time is the literal 1 s, not a field: only a transpiler, or a postfix that moves `m_sailForce` further toward the same target, changes the response), applied at `m_sailForceOffset` above centre of mass;
   * **wind angle rule** `Ship.GetWindAngleFactor` (public, also used by the HUD icon colour): with `d = dot(windDir, -forward)`, factor = `lerp(0.7, 1, 1-|d|) * (1 - LerpStep(0.75, 0.8, d))`: best at beam reach, 70 % with tail wind, zero when the wind comes from within about 37-41° of the bow. The force itself points along the bisector of wind and bow (`normalize(windDir + forward)`), so only `cos(θ/2)` of it drives the ship forward (θ = angle between the wind's direction and the bow). The forward drive is therefore nearly flat (0.70 to 0.74 of the full force) from dead run to beam reach: "best at beam reach" holds for the factor and the HUD icon, not for forward speed;
   * **steering**: a lateral force at `m_stearForceOffset` proportional to forward speed (`m_stearVelForceFactor * -rudder`), so turning needs speed; in `Slow`/`Back` an extra paddle force `m_backwardForce * (1-|rudder|)` plus direct turn force `m_stearForce`. `m_stearForce`, `m_stearVelForceFactor`, `m_backwardForce` and `m_dampingForward` are read only here, on the owner;
   * `Ship.ApplyEdgeForce` pushes ships back beyond radius 10420. This private method is called only at the end of the owner's in-water block, after every vanilla force, which makes it a free "owner, in water, after the forces" hook;
   * damage: `Ship.UpdateUpsideDmg`, `Ship.UpdateWaterForce` (water slam, only with players aboard), `Ship.TakeAshlandsDamage` (hit type `AshlandsOcean`; skipped if `m_ashlandsReady`, which only `VikingShip_Ashlands` sets, runtime dump). These, creature and player hits, fire and collisions (every ship prefab has an `ImpactEffect` with hit type Boat and damage to itself, from no damage at 1.5 m/s up to its full blunt damage at 7 m/s, at most every 0.5 s; runtime dump) all go through `WearNTear.Damage` → RPC `WearNTear.RPC_Damage` on the owner. Weather wear (rain, ash, lava, Deep North snow) calls `WearNTear.ApplyDamage` directly from `WearNTear.UpdateWear` and never reaches `RPC_Damage`.
4. **Sync**: `Ship.UpdateControlls`: owner writes ZDO `forward` (int speed) and `rudder`; non-owners read them (rudder only if they did not send one in the last 1 s).
5. **Ownership**: `Ship.UpdateOwner` (InvokeRepeating 2 s, owner only): if the owner's local player is not on board, ownership moves to the first aboard player with a valid owner id. With nobody aboard, the owner is whoever `ZDOMan.ReleaseZDOS` picks. **The helmsman is not necessarily the owner** - the physics may run on a passenger's machine using the helmsman's RPC-fed `m_speed`/`m_rudderValue`.
6. **Wind** `EnvMan.UpdateWind`: deterministic from net time (4 octaves seeded by `timeSec / (m_windPeriodDuration / octave)`), intensity mapped to the current environment's `m_windMin..m_windMax`, transition over `m_windTransitionDuration`. Near the world edge the wind blows outward. If `Ship.GetLocalShip().IsWindControllActive()` (any player aboard with `StatusEffect.StatusAttribute.SailingPower`, i.e. Moder's power) the wind is set to the ship's forward. Wind is computed **locally on every client**; it matches across clients except for the Moder/edge overrides which use the local player's situation. For tests, the console commands `wind <angle> <intensity>` (`EnvMan.SetDebugWind`) and `resetwind` fix and release the wind.

### Data & persistence
* ZDO keys (`ZDOVars`): `forward` (int `Ship.Speed`), `rudder` (float), `user` (long, helmsman player id on the ship ZDO, written by `ShipControlls`).
* Stats: `Player.UpdateStats()` (parameterless overload, every 2.5 s) adds `DistanceSail` when `Ship.GetLocalShip() != null` and `DistanceSailHelm` when also `GetControlledShip() != null`.
* **There is no sailing skill in 1.0.16** (`Skills.SkillType` has no Sailing entry; 109 is unused).
* Ship prefab tuning (runtime dump, 1.0.16):

  | Prefab | Mass | Health | `m_sailForceFactor` | `m_rudderSpeed` | `m_stearForce` / `m_stearVelForceFactor` | `m_backwardForce` | Damping forward / sideways / angular | Impact blunt | Helm range |
  |---|---|---|---|---|---|---|---|---|---|
  | `Raft` | 1000 | 300 | 0.05 | 1 | 0.3 / 0.2 | 0.5 | 0.005 / 0.1 / 0.05 | 20 | 2 m |
  | `Karve` | 1000 | 500 | 0.03 | 1 | 0.2 / 0.18 | 0.2 | 0.001 / 0.15 / 0.05 | 30 | 10 m |
  | `VikingShip` | 2000 | 1000 | 0.05 | 1 | 1 / 0.8 | 0.2 | 0.001 / 0.15 / 0.3 | 50 | 10 m |
  | `VikingShip_Ashlands` | 3000 | 3000 | 0.085 | 0.5 | 1.9 / 1.05 | 0.25 | 0.002 / 0.5 / 0.95 | 50 | 10 m |
  | `Trailership` (no sail) | 1000 | 1000 | 0 | 0.5 | 1.5 / 0.5 | 0.5 | 0.005 / 0.05 / 0.1 | 50 | 10 m |

  Every ship: rudder angle up to 45°, 20 damage per second while upside down, water slams of 10 damage at most every 2 s, pierce Resistant, chop and pickaxe Immune, fire Weak (`VikingShip_Ashlands`: VeryResistant, and immune to ash), no rain or support wear.

### Multiplayer authority
Owner-simulated physics, RPC control (`Forward`, `Backward`, `Stop`, `Rudder` on the ship's `ZNetView`; `RequestControl`, `ReleaseControl`, `RequestRespons` on the same view from `ShipControlls`). `ZNetView.InvokeRPC(method, ...)` targets the **ZDO owner**. Any sailing rule change must run on the owner, i.e. **every client that can end up owning a ship needs the mod**.

### Patch points
| Target | Why |
|---|---|
| `Ship.CustomFixedUpdate` prefix/postfix | Scale public tuning fields per-tick (`m_sailForceFactor`, `m_stearForce`, `m_stearVelForceFactor`, `m_backwardForce`, `m_dampingForward`) based on helmsman data; restore in postfix. |
| `Ship.GetSailForce` (private) | Low-wind floor. The SmoothDamp time is a literal: change the response with a transpiler or a postfix that moves `m_sailForce` further toward the target. |
| `Ship.GetWindAngleFactor` | "Catch the wind" rules; HUD reuses it automatically. |
| `Ship.ApplyControlls` | Rudder responsiveness (`m_rudderSpeed`), new keys (instant stop). |
| `Ship.RPC_Forward/RPC_Backward/RPC_Stop` | Speed-state machine changes (e.g. skip states). |
| `ShipControlls.RPC_RequestControl` (private, runs on the ship owner) | Hand ZDO ownership to the helmsman (`GetZDO().SetOwner(sender)`). It carries only the player id, so helmsman data (skills) must come from the helmsman's own game. |
| `Ship.ApplyEdgeForce` (private) | "Owner, in water, after every vanilla force" hook. |
| `WearNTear.RPC_Damage` (private, owner) | Every ship damage path except weather wear. |
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
* **Exploration**: `Minimap.UpdateExplore` (private, called from `Update` even when the map is hidden) every `m_exploreInterval` calls private `Explore(player.position, m_exploreRadius)`: **50 m every 0.25 s** on the prefab (runtime dump, 1.0.16; 100 m and 2 s are only the C# initialisers), **constant: no altitude, ship or skill dependency**. The timer adds `Time.deltaTime`, not the method's `dt` argument. `Explore(Vector3, float)` marks pixels in `m_explored` (BitArray) and clears the fog texture red channel. Shared (others') exploration is `m_exploredOthers` / green channel. `Minimap.ExploreAll` reveals everything.
* **World↔map**: private `WorldToPixel`, `WorldToMapPoint`, `MapPointToWorld`, `ScreenToWorldPoint` use `m_textureSize` and `m_pixelSize` (code defaults 256 / 64; on the prefab 2048 pixels of 12 m, runtime dump, 1.0.16).
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
* Pin type is stored as a raw int: a client whose `m_visibleIconTypes` does not cover it (vanilla, or a mod that did not grow the array before the pins loaded) turns an unknown type into `Icon3` with a log warning (`Minimap.AddPin`) and saves it that way (pin kept, icon lost).

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
* `Skills` component on the player: `m_skills` (`List<SkillDef>`: skill, icon, description, `m_increseStep`), runtime `m_skillData` (`Dictionary<SkillType, Skill>`), `m_DeathLowerFactor` (code 0.25, **0.05 on the Player prefab**), optional `m_useSkillCap`/`m_totalSkillCap` (off, 500 on the prefab). Runtime dump, 1.0.16.
* Raise step (`m_increseStep`) per skill on the Player prefab (runtime dump, 1.0.16): Bows and Spears 1.5; Swords, Axes, WoodCutting, Unarmed, Knives, Clubs, Polearms, Pickaxes, ElementalMagic, BloodMagic and Crossbows 1; Blocking, Jump and Sneak 0.5; Swim 0.3; Fishing, Cooking, Farming, Crafting and Dodge 0.25; Run and Ride 0.2. The Rested effect raises all skill gains by 50% (`m_raiseSkill` All, `m_raiseSkillModifier` 0.5).
* `Skills.SkillType`: `Swords 1, Knives 2, Clubs 3, Polearms 4, Spears 5, Blocking 6, Axes 7, Bows 8, ElementalMagic 9, BloodMagic 10, Unarmed 11, Pickaxes 12, WoodCutting 13, Crossbows 14, Jump 100, Sneak 101, Run 102, Swim 103, Fishing 104, Cooking 105, Farming 106, Crafting 107, Dodge 108, Ride 110, All 999`.
* `Skills.Skill.Raise`: xp `m_increseStep * factor * Game.m_skillGainRate`; next level needs `floor(level+1)^1.5 * 0.5 + 0.5`; max 100.
* `Skills.GetSkillLevel` = base level + `SEMan.ModifySkillLevel` (e.g. `SE_Stats.m_skillLevel/m_skillLevelModifier` from meads/set bonuses), floored. `GetSkillFactor` = level/100. `GetRandomSkillFactor` = random in `lerp(0.4,1,f) ± 0.15`. **Reading a level creates the skill:** the private `Skills.GetSkill` adds a level-0 entry to `m_skillData` when the player has none, which then shows in the Skills dialog and is saved in the character. To read without side effects, check `m_skillData.TryGetValue` first (a missing skill = 0 plus the `SEMan.ModifySkillLevel` boost), as Breeding Star Inheritance does.
* `Player.RaiseSkill` applies `SEMan.ModifyRaiseSkill` then `Skills.RaiseSkill` → on level up `Player.OnSkillLevelup` (effect only) + message "$msg_skillup $skill_<enum name lowercase>".
* Death: `Skills.OnDeath` → `LowerAllSkills(m_DeathLowerFactor * Game.m_skillReductionRate)` (also resets accumulators). The prefab's 0.05 matches the wiki's 5 % loss per hard death on the Normal preset (1 % to 7.5 % depending on the preset, full reset on Hardcore).
* Persistence: `Skills.Save/Load` (version 2: type int, level, accumulator). **`Skills.Load` drops any type failing `Skills.IsSkillValid` (private static, `Enum.IsDefined`)** without a message, and the dropped skill is gone at the next save. `Skills.GetSkillDef` returns null for a type that is not in `m_skills`, so `Skills.GetSkill` builds a `Skill` with `m_info = null` and `Skill.Raise`, `Skills.Save` and `SkillsDialog.Setup` then throw.
* Custom skills: patch both `IsSkillValid` and `GetSkillDef` (or add the def to `m_skills`), and keep those patches applied even while the mod's feature is off, or levels are lost at the next save. `Skills.CheatRaiseSkill` / `CheatResetSkill` match enum names only, and their "all" loops the enum only. The `raiseskill` / `resetskill` Tab lists (built once per process in `Terminal.InitTerminal`) are the enum names, cached at first use (`Terminal.ConsoleCommand.GetTabOptions`). The name token `"$skill_" + type.ToString().ToLower()` gives a number for a value outside the enum, so the words must be added under that number. `Localization.AddWord` is private in the non-publicized `assembly_guiutils`, and `Localization.SetLanguage` clears every word. Jotunn's `SkillManager` covers these points; [Sailing Skill](../../src/Exploration/Sailing.Skill) does it with its own patches.

### Vanilla effects (where the factor is read)
| Skill | Effect | Where |
|---|---|---|
| Weapon skills | Damage roll (`GetRandomSkillFactor`), stamina/eitr/health cost -33 % at 100 | `Attack.GetAttackStamina`, `GetAttackEitr`, `GetAttackHealth`, damage in `Attack` hit code |
| Bows/Crossbows | Draw time to 20 % (`Humanoid.GetAttackDrawPercentage`), reload time to 50 % (`ItemDrop.ItemData.GetWeaponLoadingTime`) | |
| Blocking | Block power (`ItemData.GetBlockPower(skillFactor)`), perfect-block SE level | `Humanoid.BlockAttack` |
| Run | Stamina drain x`lerp(1,0.5)`, speed +25 % | `Player.CheckRun`, `Player.GetRunSpeedFactor` |
| Jump | Jump force +40 % | `Character.Jump` |
| Swim | Swim stamina 6→3 /s on the Player prefab (code 5→2) | `Player.OnSwimming` |
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
* `GameCamera.UpdateCamera`: sets `fieldOfView = m_fov` (65); if no UI blocks input, scroll wheel (`m_zoomSens` 10) or gamepad alt-keys change private `m_distance`, clamped to `[m_minDistance, m_maxDistance]` (code 6, **8 on the prefab**, runtime dump, 1.0.16), or `m_maxDistanceBoat` when the local player controls a ship. Dead → look at ragdoll (the position is not updated then, only `LookAt`); attached with `GetAttachCameraPoint` → fixed attach camera; else `GetCameraPosition`.
* `GameCamera.GetCameraPosition`: eye position + offset (`GetCameraOffset`: `m_fpsOffset` when `m_distance <= 0`; the prefab's `m_minDistance` is **1** (runtime dump, 1.0.16), so vanilla never reaches distance 0 and has no first person; else `m_3rdOffset` / `m_3rdCombatOffset` when `Humanoid.UseMeleeCamera`), `CollideRay2` wall collision, `UpdateNearClipping`, **water clamp** (`m_minWaterDistance`: code 0.3, **0.4 on the prefab**; the clamp sets `m_waterClipping`), optional ship tilt (`ApplyCameraTilt`, pref `ShipCameraTilt`). `UpdateNearClipping` runs before the clamp, so it uses the previous frame's `m_waterClipping`; it picks `m_nearClipPlaneMin` (0.1) while clipping or near a wall, else `m_nearClipPlaneMax` (0.5) (prefab values, runtime dump, 1.0.16).
* **Fog**: `EnvMan.SetEnv` is the only game code that writes `RenderSettings.fogColor` / `fogDensity`. It is called from `EnvMan.FixedUpdate`, so at most once per rendered frame and not at all in frames without a physics step: a mod that overrides the fog for one view (under water, for example) must compute it from a cached base, never from the current value. The fog mode is Exponential (densities 0.003 to 0.007 seen in the self-tests; runtime dump, 1.0.16).
* **Water surface seen from below** (one-sided, see [exploration-world.md §13](exploration-world.md)): `WaterVolume.SetupMaterial` writes the shader's `_depth` corners once, in `Start` (`m_forceDepth` four times, or the heightmap corners). `WaterVolume.Depth` shows the corner order: [3]→[2] along x at the −z edge, [0]→[1] at the +z edge, so a surface turned 180° about its local X axis needs the corners as [3], [2], [1], [0]. A volume joins `WaterVolume.Instances` in `OnEnable`, before its `Start`, so its own `SetupMaterial` can overwrite a change made as soon as it appears.
* **Temp FOV API**: `GameCamera.SetTempFOV(target, inertia)` stores `m_fovBase` on first use **ever** (never cleared) and animates `m_fov` toward the target every frame from then on (frame-rate dependent); `ResetTempFOV()` returns to base. Only used by `GrapplingPoint` (1.0 grappling), whose break and deactivate reset it: not usable for a mod zoom.
* **Field of view is rewritten every frame**: `UpdateCamera` starts with `m_camera.fieldOfView = m_skyCamera.fieldOfView = m_fov`, so a postfix that writes both cameras' field of view (and never `m_fov`) zooms for one frame and leaves nothing to restore (MC Spyglass). Camera shake multiplies the rotation, so it grows with a narrow field of view; a postfix that sets the rotation drops it.
* **Mouse sensitivity**: `PlayerController.m_mouseSens` / `m_gamepadSens` are static and rewritten by the settings screen (`KeyboardMouseSettings`, `GamepadSettings`); a prefix on `Player.SetMouseLook(Vector2)` that scales its argument covers mouse, stick and gyro without touching them.
* **Body near the camera**: `Player.FixedUpdate` hides the local body (`Character.SetVisible(false)`: the visual's `LODGroup.localReferencePoint` moved far away) when the camera is within 2 m of the **feet**, and `Character.CustomFixedUpdate` shows it again every tick, so a camera at the eye (1.81 m up, `m_eye` local (0, 1.808, 0), runtime dump) makes it flip. A mod that needs it hidden writes the reference point once per rendered frame (camera postfix, after every physics tick) and on release writes what `m_lodVisible` says. Held items are in LOD 0 and hide with the body.
* **Crosshair**: `Hud.UpdateCrosshair` re-activates `m_crosshair`'s GameObject every frame but only ever writes its colour: switch the `Image` component off instead. The HUD canvas is Screen Space Overlay at sort order 400; the render-scale picture (`FrameBufferScaler`, canvas "Scaled 3D Viewport") is at 0 (runtime dump, 1.0.16).
* **Rendering**: built-in pipeline, deferred, HDR, Unity 6000.0.75f1; post-processing stack v1 (`PostProcessingBehaviour`, `OnRenderImage`). Only `Shader.Find` of shaders shipped in the build works (`UI/Default`, `Sprites/Default`, `Hidden/BlitCopy`... not `Standard`/`Unlit/*` reliably); a component added last to the main camera with `OnRenderImage` gets the finished frame before any UI (MC Spyglass's blurred edge).
* Free-fly debug camera (`ToggleFreeFly`, `m_freeFlyMinFov/MaxFov`).

### Multiplayer
Purely local.

### Patch points
`GameCamera.UpdateCamera` (private; a postfix sees the frame's final camera position, after `EnvMan.SetEnv`, and can place the camera and set the field of view for this frame), `GameCamera.GetCameraPosition` (private; a prefix can lift the water clamp for one call and a finalizer put `m_minWaterDistance` back), public fields `m_maxDistance`, `m_maxDistanceBoat`, `m_minWaterDistance`; `Player.SetMouseLook` prefix for zoomed sensitivity. Avoid `SetTempFOV`, `m_fov` and `PlayerController.m_mouseSens` (above).

---

## 9. Equippable lights and the utility slot

### Key classes
`Humanoid` (equipment fields `m_rightItem`, `m_leftItem`, `m_chestItem`, `m_legItem`, `m_helmetItem`, `m_shoulderItem`, `m_utilityItem`, `m_trinketItem`, `m_ammoItem`, hidden hand items), `ItemDrop.ItemData.ItemType` (`Torch = 15`, `Utility = 18`, `Trinket = 24`, ...), `VisEquipment` (visual attachment + replication), `SE_Demister` (Wisplight ball).

### Flow
* `Humanoid.EquipItem`: `Torch` goes to the left hand if a one-handed weapon is in the right hand, otherwise to the right hand (unequipping non-shield left items). `Utility` replaces `m_utilityItem` (**one utility slot**: Megingjord `BeltStrength`, `Wishbone`, Wisplight `Demister`); `Trinket` has its own slot (1.0). Utility/Trinket items below the current world level are refused in NG+ worlds (`Game.m_worldLevel`).
* `Humanoid.UpdateEquipment`: durability drain for right/left/utility/trinket items with `m_useDurability`; hides hand items while swimming (torches go out of view; a utility item stays).
* Equip status effects (`m_shared.m_equipStatusEffect`) are collected in `Humanoid.UpdateEquipmentStatusEffects` (the Wisplight uses `SE_Demister`, which spawns and steers a local ball prefab). The Wishbone's effect is the `SE_Finder` asset `Wishbone` (checked in the 1.0.16 asset bundles). It runs only for the owner (`Character.CustomFixedUpdate` calls `SEMan.Update` only when the ZDO is owned). `SE_Finder.UpdateStatusEffect` asks `Beacon.FindClosestBeaconInRange` once per second for the closest loaded `Beacon` whose 3D distance is below that Beacon's own `m_range`, then pings more often as the XZ distance shrinks (every 5 s at the edge, every 0.5 s on top). `Beacon` registers itself in the static `Beacon.m_instances` in `Awake`; nothing else in the game uses it. Deep North targets: [exploration-world.md §6](exploration-world.md).
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

## 11. Comfort, resting and music

### Key classes
`SE_Rested` (Rested; static comfort calculation), `SE_Cozy` (Resting), `Player` (`UpdateBaseValue`, `UpdateEnvStatusEffects`, `m_comfortLevel`), `Piece` (`m_comfort`, `m_comfortGroup`), `AudioMan`, `MusicMan`, `MusicVolume`, `GameCamera` (audio listener). Checked in 1.0.16 (research 2026-10-05, Music Instruments).

### Comfort and Rested
* Comfort is computed on the **local player only, every 2 s**: `Player.FixedUpdate` → `Player.UpdateBaseValue` → `SE_Rested.CalculateComfortLevel(Player)` → `CalculateComfortLevel(InShelter(), position)`, stored in the private `m_comfortLevel` and returned by `Player.GetComfortLevel()` (0 without a ZNetView; remote players never compute it). A higher value raises the `MaxComfort` profile stat and its platform achievement stat (the "Comfort is King" achievement needs 20).
* The value is 1, and **only in shelter** (`Player.InShelter`: cover ≥ 0.8 and under a roof, refreshed every 1 s) +1 and the best piece of each `Piece.ComfortGroup` within 10 m (ungrouped pieces each count, duplicate names once; an unlit fire counts 0). Outside a shelter comfort is always 1. No cap in code.
* Readers: `SE_Cozy.GetIconText` (the Resting icon's "Comfort: N"), `SE_Rested.UpdateTTL` (length) and `SE_Rested.Setup` (the "You feel rested" message with comfort, only when Rested is newly added). The Rested icon shows the time left.
* Resting (`Player.UpdateEnvStatusEffects`): not sensed by an enemy (`IsSensed`, last 1 s), sitting **or** in shelter, near a heat `EffectArea` (fire) in the last 0.25 s, not cold/freezing, not wet (unless in a warm cozy area), not burning. While it holds, Resting is re-added each tick; after its delay (prefab data, wiki: 20 s) `SE_Cozy` adds Rested with a time reset every tick, so Rested stays pinned at `m_baseTTL + (comfort - 1) × m_TTLPerComfortLevel` (code defaults 300/60; prefab values 480/60, confirmed in game by the Music Instruments self-test `music.perform`). `UpdateTTL` only ever lengthens Rested. Waking up (`Player.SetSleeping(false)`) also adds Rested with a reset.
* `Humanoid.IsSitting` is the animator tag `sitting`: a pose made by writing bones keeps a seated player "sitting" (Resting outdoors needs it).

### Status effects for other players
* Effects tick only on their owner (`Character.CustomFixedUpdate` → `SEMan.Update`); only the `s_seAttrib` bitmask is synced. `SEMan.AddStatusEffect(int hash, resetTime, ...)` on a non-owned character sends `RPC_AddStatusEffect` to the owner, which looks the hash up in its own `ObjectDB.m_StatusEffects` (unknown hash: silently nothing). `AddStatusEffect(StatusEffect)` has no owner check: on a remote copy it never ticks. Effects are not saved and are cleared on death. `ObjectDB.CopyOtherDB` shares the effect list between the two databases.

### Sound
* The audio listener is a child of the main camera; `GameCamera.UpdateListner` moves it to `Player.m_localPlayer.m_eye.position` every LateUpdate (orientation stays the camera's).
* `AudioMan.m_masterMixer` (asset `MasterMixer`) has groups `SFX`, `SFX_LARGE` and others; the Sound effects slider drives the exposed parameter `SfxVol` (and `GuiVol`) through `AudioMan.SetSFXVolume(master × sfx)`. The music slider is not a mixer parameter: `MusicMan.m_masterMusicVolume` multiplies the music source's volume. An `AudioSource` without an output group ignores both sliders.
* Audio settings: Unity defaults (stereo, sample rate from the device, DSP buffer 1024, 32 real voices). The game itself never uses `OnAudioFilterRead`, `AudioClip.Create` or `AudioSettings.dspTime`. Unity applies a source's volume, 3D rolloff and panning before custom filters (`OnAudioFilterRead`).
* `MusicMan.Update`: `UpdateCurrentMusic`, `UpdateCombatMusic`, then `m_currentMusicVolMax = MusicVolume.UpdateProximityVolumes(m_musicSource)` (static), then `UpdateMusic` fades the source toward it. The "home" track plays while `Player.IsSafeInHome()` (Resting in shelter).

### Patch points
`Player.GetComfortLevel` (postfix: add comfort without touching the MaxComfort stat), `SE_Rested.CalculateComfortLevel(Player)` (postfix: the community's usual hook; raises the stat), `SE_Rested.UpdateTTL` (Rested length), `MusicVolume.UpdateProximityVolumes` (postfix: fade the game music), `Chat.HasFocus` (postfix: hold the game's keys back while a mod window or mini-game runs; `Menu.Update` does not ask it).

---

## Cross-cutting notes for this chapter

* **Persistence choices**: per character → `Player.m_customData` (strings, saved in the character, vanilla-safe if the mod is removed: data is kept and ignored). It is saved on autosave, logout and by `Game._RequestRespawn` before the dead player is destroyed (so it survives death), and loaded by `Game.SpawnPlayer` → `PlayerProfile.LoadPlayerData` just before `Player.OnSpawned`. From `_RequestRespawn` (about 10 s after death) until the new spawn, `Player.m_localPlayer` is null while the game and profile still exist. Per item → `ItemDrop.ItemData.m_customData`. Per world object → ZDO custom keys (namespace them with the mod GUID, e.g. `"MC.Exploration.Sailing.Skill.Level"` on the player ZDO). Per profile/world map → do not change the `Minimap.GetMapData` format.
* **Custom items vanish without the mod**: `Inventory.AddItem(int prefabHash, ...)` logs "Failed to find item prefab" and returns false, so a spyglass/hip lantern is **deleted from inventories** that are loaded (and then saved) without the mod.
* **Private members**: most patch targets here are private (`UpdateSwimming`, `UpdateExplore`, `Explore`, `UpdateSleeping`, `CalculateCanSleep`, `GetSailForce`...). Use a publicized `assembly_valheim` reference for compile-time access and `AccessTools`/`Traverse` in patches.
* **Custom skills** need injection (see section 7). Either depend on Jotunn's `SkillManager` or implement a small shared injector in the repo's Core library; pick one project-wide to avoid double registration. The first MC custom skill, Sailing Skill, registers itself with its own always-on patches (no Jotunn).
* **Owner-authoritative physics**: anything touching `Ship` or other players' characters must run where the ZDO is owned, which makes it "everyone" in practice.

---

## Feature ideas

### Cartography table revamp - extra POI pin icons at the table (QoL)
The sheet text (rewritten 2026-09-29) asks for three things: (1) next to a cartography table, the large map offers extra pin icons for points of interest (dungeon, cave, tower, stone, plant, creature paw, "etc."); (2) away from a table the map stays as in vanilla; (3) a pin that another player shared through the table and that the player removed from their own map is not added again by the next table read.

* **Vanilla facts** (1.0.16):
  * **Pin icons.** Players pick from `Icon0`..`Icon4`: the buttons call `Minimap.OnPressedIcon0..4` → private `IconPressed` → `SelectIcon`, or `ToggleIconFilter` on a double tap (`OnAltPressedIcon0..4` also toggle the filter). `Minimap.Start` sizes the private `m_visibleIconTypes` from the enum (18 values) and fills the private `m_selectedIcons` with each button's highlight `Image` (`ToggleIconFilter` greys that image's parent); the gamepad D-pad in `UpdateMap` walks the same dictionary. `Minimap.AddPin` turns any type `>= m_visibleIconTypes.Length` into `Icon3` with a warning, and re-shows a type that was hidden with the filter. `GetSprite` looks the type up in the public `m_icons` list; `SpriteData` is a struct, so a valid type with no entry gets a null sprite (drawn as a white square, as Cartur's Map Pins' 1.5.0 changelog notes). Saved pins are loaded after `Start`, on the first `Minimap.Update` (`LoadMapData` → `SetMapData` → `AddPin`), so a `Minimap.Start` postfix can make extra type ints valid before any saved pin is read.
  * **Why a removed shared pin comes back.** `MapTable.OnRead` → `Minimap.AddSharedMapData` marks every other player's shared pin for deletion, then for each pin stored on the table: keeps a local saved pin within 1 m (`HavePinInRange`), skips pins authored by the local player, and adds every other pin again with its author's `m_ownerID`; finally it deletes the marked pins that are no longer on the table. Nothing remembers a removal.
  * **Only the author can take a pin off a table.** `MapTable.OnWrite` runs the same read first (without message), then stores `Minimap.GetSharedMapData`: the union of explored areas plus every saved non-Death pin on the writer's map (other players' pins keep their `m_ownerID`). Only the table's explored area is merged, not its old pin list. So a foreign pin the writer removed is re-imported by that read and written back, the author's own removed pin is skipped by the read and leaves the table, and a foreign pin within 1 m of one of the writer's saved pins is not re-imported and is replaced on the table by the writer's pin.
  * **Removing and claiming.** Right click or a long touch (`RemovePinUnderPointer`) and gamepad `JoyTabRight` (`UpdateMap`) call `Minimap.RemovePin(Vector3, float)`, which only finds saved pins that are drawn (`GetClosestPin`); shared pins are not drawn while the shared-map toggle is off. A left click (`OnMapLeftClick`, gamepad `JoyTabLeft`) on a shared pin sets its `m_ownerID` to 0: it becomes the player's own and is written with their id next time. `RemovePin(PinData)` is also called for unsaved pins (bed spawn point, location icons, public players, pings, shouts, random and persistent events, the console `find` and `findbiome` pins) and by `AddSharedMapData` for stale shared pins; `ResetSharedMapData` (console `resetsharedmap`) removes shared pins directly from the list. The death marker is a saved `Death` pin added by `Player.OnDeath`, and `GetSharedMapData` never shares `Death` pins.
  * `MapTable` keeps no instance list, and no other game class refers to it. While the large map is open `Player.TakeInput` is false (`Minimap.IsOpen`), so the player does not walk away from a table with the map open (a moving ship can still carry them).
* **Feasibility**: medium (UI cloning, a registry of tables and a small per-world list; no change to the map or table data formats).
* **Who needs the mod**: client-only, and it ships client-side: the house rule that experience-changing mods ship as Both does not apply, because it is a map UI helper that changes no odds, costs or world rules, and it never deletes another player's pin from a table. Pins, the palette and the dismissal list are local; the table stores pin types as plain ints. A player without the mod gets the new icons as `Icon3` (vanilla logs a warning) and, when they write to the table, stores them as `Icon3` there, so players who share a table should all install it to see the same icons. Dismissals never change what other players get from the table.
* **Assets**: a small set of white pin icons (one per POI type, in the vanilla pin style) shipped as PNG files in the mod folder and turned into sprites at load. Vanilla item icons (a trophy for creatures, a berry for plants) could stand in for a first version, but the vanilla `mapicon_*` sprites (Jotunn sprite list) have nothing for a dungeon, cave or tower. Simple shapes can also be drawn in code, as MC's Encyclopedia mod draws its creature paw print (no asset file).
* **Hooks**: `Minimap.Start` (postfix: grow `m_visibleIconTypes`, append `SpriteData` entries to `m_icons`, clone the `Icon4` button into an extra row and add each clone's highlight image to `m_selectedIcons`), `Minimap.IconPressed` / `SelectIcon` / `ToggleIconFilter` (called by the new buttons; a `SelectIcon` prefix refuses the new types away from a table), `Minimap.SetMapMode` and `Minimap.Update` (postfix, throttled: show or hide the row while the large map is open), `MapTable.Start` (postfix: register tables), `Minimap.AddPin` (prefix: skip dismissed pins during a table read), `Minimap.AddSharedMapData` (prefix and finalizer: table-read flag; postfix: correct the "synced" result), `Minimap.ExploreOthers` (postfix: note real exploration changes), `Minimap.RemovePin(PinData)` (prefix: record removals), `MapTable.OnWrite` (prefix and finalizer: write flag) and `Minimap.GetSharedMapData` (prefix and postfix: put dismissed table pins back for the write), `Player.m_customData` (storage).
* **Sketch**:
  1. **Extra icons.** A fixed block of new `PinType` ints for Dungeon, Cave, Tower, Stone (rune stones, standing stones), Plant and Creature (paw), plus proposed extras for the "etc." (for example Ore, Camp, Chest, Danger), placed well clear of other icon mods (Cartur's Map Pins uses 100 upward) and never changed after release. In a `Minimap.Start` postfix: grow `m_visibleIconTypes` to cover the block (keep a larger array another mod made; new entries visible), append one `SpriteData` per type to `m_icons`, clone the `Icon4` button (the parent of `m_selectedIcon4`) once per type into a second row of the large map's icon panel, set its icon, replace its click wiring (the clone keeps vanilla's wiring to `Icon4`, right click included) with `IconPressed(type)` and the filter toggle, and add its highlight image to `m_selectedIcons` so vanilla `SelectIcon`, `ToggleIconFilter` and the D-pad handle it. `Minimap.Start` runs once per world load, so when the feature is switched on mid-session `OnActivated` runs the same setup on `Minimap.instance` (pins already loaded as `Icon3` stay so). A double click then places a pin of the selected type through vanilla `ShowPinNameInput` / `AddPin`; it is saved and shared like any other pin.
  2. **Only next to a table.** A `MapTable.Start` postfix adds the table to a static list (entries that became `null` are dropped). When the large map opens (`SetMapMode`) and every half second while it stays open, the mod checks whether the local player is within `TableRange` (default about 5 m, the code default of `Player.m_maxInteractDistance`; vanilla measures the reach from the eye to the hovered point, and the prefab value is unverified) of a listed table and shows or hides the extra row. Away from a table the palette is vanilla: a `SelectIcon` prefix refuses the new types (so the D-pad stops at the vanilla icons) and a selected new type falls back to `Icon0`. Pins already placed with a new icon stay visible and removable everywhere (reading of "the map does not change": the palette does not change); if the sheet means these pins should only be drawn at a table, the same check can switch their `m_visibleIconTypes` entries off away from it. No-map worlds (`Game.m_noMap`) have no map, so nothing shows.
  3. **A removed shared pin stays removed.** A `Minimap.RemovePin(PinData)` prefix records the type and XZ position of every saved, non-Death pin removed while the local player exists and outside a table read or the mod's own temporary pins: this covers the right click, the gamepad, pin-manager mods and pins the player first claimed with a left click (own pins are recorded too, which is harmless: a table never gives a player their own pins back). The list lives in `Player.m_customData` under a per-world key (`ZNet.GetWorldUID`, like the vanilla map data), capped with the oldest entries dropped. During `AddSharedMapData` (flag set in a prefix, cleared in a finalizer) a `Minimap.AddPin` prefix skips any incoming pin of the same type within 1 m of a recorded entry (vanilla's own dedupe distance; the name is ignored so pin editors cannot defeat it). Vanilla reports "map synced" as soon as it calls `AddPin`, so a postfix recomputes the result from real changes (pins added or removed, `ExploreOthers` returning true). The dismissal stays personal: during `MapTable.OnWrite` the skipped pins are kept aside, added back to `m_pins` with their author id for the duration of `GetSharedMapData` and removed again, so a write never deletes another player's pin from the table (the vanilla rule). A setting (or console command) forgets the list for the current world. Optional small extra, off by default: open the large map after reading a table.
* **Risks**: Type ints can collide with other icon mods (Cartur's Map Pins uses 100 upward; BetterMap, the MoreMapPins family and Multiplayer Tweaks assign their own), and another mod may replace `m_visibleIconTypes` with a smaller array or iterate all of it (pin filter panels): grow only, pick the block once. A client without the mod, or this mod removed or disabled when a world loads (the framework removes every patch of a turned-off feature), turns the new pins into `Icon3` at load and saves them that way: the pin survives, its icon is lost for good (say so in the README). Cartur's Map Pins avoids the disabled case by growing the array even while its custom icons are switched off; the MC framework would need a patch outside the feature toggle for that. A sturdier but larger design saves a vanilla type and keeps the real icon in a side record. The icon panel's hierarchy is prefab data (names unverified); UI overhauls and other icon grids (Cartur's grid, BetterMap's legend rows, RavenwoodMapPins / MoreMapPins menus) may overlap the new row. Better Cartography Table, Asocial Cartography (its `GetSharedMapData` patch swaps `m_pins` for a filtered list during the write, so add and remove the kept-aside pins by reference), Pintervention, PlayerMapLayers, The Greatest Map and ServersideQoL AutoMapTables hook the same table read/write path or shared pins: test each or list it as incompatible. Matching on type + 1 m also hides a later pin of the same type on the same spot. A new type hidden with the filter toggle can only be shown again at a table (or when a pin of that type is added). When the feature is toggled off mid-session, keep the grown array and sprites (`UpdatePins` indexes the array by pin type, and a valid type without a sprite draws as a white square).

### Sleep through the day (QoL)
* **Status**: implemented as [Sleep Through the Day](../../src/Exploration/Sleep.ThroughDay) (0.1.0), the first MC mod that the server and every player need (`ModSide` Both). The shipped design keeps `Bed.Interact` untouched: a `EnvMan.CalculateCanSleep` postfix allows lying down in the morning (06:00 to noon; the afternoon stays vanilla unless `IncludeAfternoon` is on), a server `Game.UpdateSleeping` postfix starts the sleep with vanilla's own steps and moves the end of the `SkipToMorning` skip to 18:00 of the same day, and a `Player.SetSleeping` patch replaces "Good morning" with "Good evening". Verified in a single-player world (2026-09-29): a morning sleep wakes at 18:00 of the same day with Rested. Vanilla facts: `Game.UpdateSleeping` only skips while `EnvMan.IsAfternoon() || EnvMan.IsNight()`, so a vanilla server never skips in the morning; there is no "Good evening" token (`$msg_goodnight` exists but is unused). See [docs/design/exploration-sleep-through-day.md](../design/exploration-sleep-through-day.md).
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

* **Status**: implemented as [Sailing Skill](../../src/Exploration/Sailing.Skill) (0.1.0, in development); design: [docs/design/exploration-sailing-skill.md](../design/exploration-sailing-skill.md). Differences from the sketch: the skill (id 697262889, a hash of the mod GUID, saved in characters) is registered by always-on patches (`Skills.IsSkillValid`, `Skills.Awake`, `Skills.GetSkillDef`, plus words, console and Tab list), without Jotunn; XP is 50 per km of ship distance at the helm; each player publishes its own level on its player ZDO (`MC.Exploration.Sailing.Skill.Level`) instead of a ship ZDO key, and ownership is not handed to the helmsman; there is no instant-stop key (an active brake at Stop instead); acceleration scales thrust and forward drag together (same top speed), plus a sail catch-up after the SmoothDamp; the map reveal radius doubles at Sailing 100; ship damage is cut by the best level aboard (`WearNTear.RPC_Damage`).
* **Feasibility**: medium (custom skill injection + owner-side physics scaling).
* **Who needs the mod**: everyone (physics runs on the ship ZDO owner, who may be a passenger).
* **Hooks**: custom skill registration (Jotunn `SkillManager` or patches on `Skills.IsSkillValid`, `Skills.GetSkillDef`, `Skills.Load`), `ShipControlls.RPC_RequestControl` (optionally `SetOwner` to the helmsman; it carries only the player id, so the helmsman's skill must come from the helmsman's own game, section 2), `Ship.UpdateOwner` (keep helmsman owner), `Ship.CustomFixedUpdate` (scale `m_sailForceFactor`, `m_stearForce`, `m_stearVelForceFactor`, `m_backwardForce` in prefix/restore in postfix), `Ship.GetWindAngleFactor` (widen the usable angle: raise the 0.7 floor, move the 0.75/0.8 headwind cut-off), `Ship.GetSailForce` (shorter SmoothDamp time = faster acceleration, higher low-wind floor than 0.25), `Ship.ApplyControlls` (rudder speed, "instant stop" key calling the unused `Ship.Stop`), `Minimap.UpdateExplore` / `m_exploreRadius` (bigger reveal when `Ship.GetLocalShip()` is set), `Player.Update` postfix for XP.
* **Sketch**: Register `Sailing` skill; award XP every few seconds to the helmsman while `GetControlledShip()` has sail up and speed > threshold (bonus for good wind angle). On taking the helm, write `vm.sail.skill` (float factor) to the ship ZDO; the owner's `Ship.CustomFixedUpdate` prefix reads it and scales tuning fields, restoring them in the postfix so prefab values are never permanently changed. Map reveal radius grows by skill while aboard, from the vanilla 50 m (section 3) to e.g. 2-3 times that.
* **Risks**: Must never double-apply scaling (restore in postfix, handle exceptions with finalizer). Mixed-mod crews desync (unmodded owner ignores the skill). Conflicts with ship-physics mods (ValheimRAFT, sail-speed/wind mods, ValheimPlus-style multipliers) and with the linked Nexus mod: check their patches. Skill id must be stable (Jotunn uses a name hash); removing the mod drops the skill data from saves (`Skills.Load` filters it). Wind overrides via `EnvMan` affect windmills/cloth globally: avoid, scale the ship's use of wind instead.

### Spyglass (New)
* **Status**: implemented as [Spyglass](../../src/Exploration/View.Spyglass) (0.1.0, in development); design: [docs/design/exploration-view-spyglass.md](../design/exploration-view-spyglass.md). Differences from the sketch: a craftable tool like the hammer (clone of `KnifeFlint` made a `Tool` with no build pieces, both hands taken, model and icon made in code; an always-on guard skips the fist punch a tool click would do) raised with Attack, with an arm pose made in code (two-bone IK after the animator, synced by a player ZDO bool); the camera is moved to the eye and zoomed in a `GameCamera.UpdateCamera` postfix (no `SetTempFOV`, no `m_distance`/`m_mouseSens` changes: section 8); aim scaled in `Player.SetMouseLook`; a round sharp view with a blurred, darker edge drawn without a shader of its own; far land and objects come from MC's Distant Horizons, which draws finer land and farther objects in the looked-at direction while a spyglass is up. Map pins, map reveal and the target readout are not in 0.1.0.
* **Feasibility**: medium (easy for a keybind MVP).
* **Who needs the mod**: depends: a keybind/utility-requirement version is client-only; a real held item needs everyone for visuals and to avoid item loss.
* **Hooks**: `GameCamera.UpdateCamera` postfix (private; camera to the eye and field of view of both cameras for the frame; keep `m_distance` against the scroll wheel), `Player.SetMouseLook` prefix (scale while zoomed; not `SetTempFOV` nor `PlayerController.m_mouseSens`, section 8), `Player.Update` postfix (input, equipped check via the public `Humanoid.RightItem`/`LeftItem` properties or `Humanoid.GetInventory()`), custom item via Jotunn `ItemManager` or `ObjectDB.Awake` clone, optional `Minimap.AddPin` ("mark target") and `Minimap.Explore(Vector3,float)` ("survey" a small radius at the look point), `Hud` overlay.
* **Sketch**: Hold a key (or secondary action when the spyglass is equipped) to switch to first person, narrow the field of view by the zoom and scale mouse sensitivity; show a vignette overlay and, via a long `Physics.Raycast` from the camera, the name/distance of the hovered `Character` or location. Optional key drops a map pin at the hit point, making it a natural companion of the cartography revamp.
* **Risks**: Objects outside the synced simulation distance (`ZNet.GetSyncedSimulationDistance`, `ZNetScene` active area) do not exist, so far creatures cannot be seen, only terrain/distant LOD. `GrapplingPoint` also uses the temp-FOV API (`m_fovBase` captured once): reset properly. Camera mods (first-person mods, camera distance mods) may fight over `m_distance`/FOV. Held-item version needs a model/icon (assets) and is deleted from inventories without the mod.

### Music instruments (New; partially exists as community mods)
* **Status**: implemented as [Music Instruments](../../src/Exploration/Music.Instruments) (0.1.0, in development); design: [docs/design/exploration-music-instruments.md](../design/exploration-music-instruments.md). Three craftable instruments (flute, lyre, tambourine: `KnifeFlint` clones made Tools, models and icons made in code) played by a synthesizer of the mod's own (no audio files): built-in songs, MIDI files from a folder, or a four-lane rhythm mini-game. The performer's game streams notes to the server, which relays them to compatible players in hearing range; each game plays them at the performer's position. Twenty seconds of good mini-game play give the Music status effect (+3 comfort via a `Player.GetComfortLevel` postfix, which `SE_Rested.UpdateTTL` reads, so it lengthens Rested also when resting outdoors by a campfire) to the performer and to every player in range, each game applying it to its own player.
* **Feasibility**: hard (sound, network timing, UI and assets all new).
* **Who needs the mod**: everyone (new items, a comfort bonus, the relay).
* **Hooks**: section 11; `Player.SetControls` (take the clicks, keep a seated player seated), `Player.LateUpdate` (arm pose), `Chat.HasFocus` (keys held back during the mini-game), `ObjectDB.Awake`/`CopyOtherDB` and `ZNetScene.Awake` (items and effect, always on), plain `ZRpc` per connection for the notes.
* **Risks**: Rested only grows while resting, so the bonus only helps players who rest during or after a performance; comfort displays that call `CalculateComfortLevel` themselves do not show the bonus; game music mods may fight over the music volume; crossplay resends lost packets after 1-3 s (listeners drop notes that come too late).

### Swim dive (New; partially exists as community mods)
* **Status**: implemented as [Swim Dive](../../src/Exploration/Swimming.Dive) (0.1.0, in development); design: [docs/design/exploration-swimming-dive.md](../design/exploration-swimming-dive.md). Differences from the sketch: no breath meter (swim stamina drains under water, also while still and on the sea floor, and drowning at 0 stamina is vanilla); under water the diver moves only up or down (Crouch down, Jump up, no key holds depth; sideways swimming only under a ceiling); a `Character.UpdateSwimming` postfix sets the vertical velocity and `m_swimDepth` is never changed; the camera clamp is lifted per call, the under-water fog is built from a cached base and the water surface mesh is turned over, with no custom assets; it ships as a Both mod with server rules.
* **Feasibility**: hard (physics is easy, presentation is not).
* **Who needs the mod**: client-only for the mechanic (owner-simulated movement, others see the synced position); everyone for consistent visuals (dive pose/tilt).
* **Hooks**: `Character.UpdateSwimming` (postfix: override `m_body.linearVelocity.y` from Crouch/Jump input for the local player), `Character.UpdateWater`/`IsSwimming` (keep swimming state at depth; deeper is fine because the check is depth > `m_swimDepth - 0.4`), `Player.OnSwimming` (breath meter/drowning), `Player.UpdateCrouch` (crouch is cancelled while swimming, so read `ZInput` "Crouch"/"JoyCrouch" directly), `GameCamera.GetCameraPosition` (remove water clamp while submerged), `Hud` (breath bar). `Character.UnderWorldCheck` needs no patch: it only fires below the terrain.
* **Sketch**: While swimming, holding Crouch sets a dive vertical speed and suppresses vanilla's pull toward `liquidLevel - m_swimDepth`; releasing it lets buoyancy return the player (optionally hold depth with Jump/Crouch). Track "head under water" (`Character.GetHeadPoint().y < GetLiquidLevel()`), drain a breath value stored on the player component, apply `HitType.Drowning` damage when empty. When the camera is below the surface, disable the clamp and apply an underwater look (fog colour/density override, screen tint).
* **Risks**: The water volume trigger must still contain the player at depth, otherwise `WaterVolume.OnTriggerExit` sets the level to -10000 and the player falls (the ocean trigger reaches down to world height −20, which covered the sea floor where tested: section 1). The ocean surface shader (`Custom/Water`, no `_Cull` property) is one-sided, so it is invisible from below, and there is no underwater post-processing: needs custom work/assets (dive animation, underwater effect). Ashlands water (`Character.UpdateAshlandsWater`) and tar behave differently. Other clients see an upright swimmer sinking unless they also run the mod.

### Hip lantern as utility item (New; exists already as a community mod)
* **Feasibility**: easy.
* **Who needs the mod**: everyone (the wearer's `UtilityItem` hash is replicated; clients without the prefab log "Missing attach item" and simply show nothing).
* **Hooks**: item registration (`ObjectDB.Awake`/`CopyOtherDB` postfix or Jotunn `CustomItem` cloning `Lantern`), `Humanoid.EquipItem` (Utility branch), `VisEquipment.AttachArmor` (uses `attach_<Bone>` children), `Humanoid.UpdateEquipment` (durability drain applies to utility items), recipe via `ObjectDB.m_recipes`.
* **Sketch**: Clone `Lantern`, set `m_shared.m_itemType = Utility`, rename/describe, and add a child `attach_Hips` (or a thigh bone) containing the lantern's visual + `Light` under an offset holder (AttachArmor zeroes local position/rotation of the attach child). Copy durability settings from the Lantern and reduce light range to keep torches relevant. Because utility visuals are not hidden while swimming, the light stays on in water.
* **Risks**: Occupies the single utility slot (competes with Megingjord/Wishbone/Wisplight); an alternative is an extra equipment slot, which conflicts with slot mods (ExtraSlots, AzuExtendedPlayerInventory, Equipment & Quickslots). Items disappear if the mod is removed. Light count/performance with many players (limit range, disable shadows).

### Compendium (New)
Vanilla already has a basic "Compendium" (`TextsDialog`: log, active effects, collected lore texts, raw stats dump). The idea is a real encyclopedia: bestiary, lore, re-readable tutorials, biomes, discovered items.

* **Status**: implemented as [Encyclopedia](../../src/Exploration/Compendium.Encyclopedia) (0.1.0), client-side. The shipped design nests it under the raven: the vanilla Valheim Compendium dialog (`$inventory_texts`, `TextsDialog`) gets two top-level tabs on its title line, Texts (its own content, unchanged) and Encyclopedia, which shows a clone of the dialog in its place with 8 category tabs (weapons, armor, tools, food, materials, trophies, building, creatures). Entries come from `ObjectDB`/`ZNetScene` at runtime (1.0.16: 910 listed items, 424 pieces, 109 creatures) and are discovered from vanilla per-character data (known materials, recipes, stations, trophies, biomes, profile kills and placements) plus two small records of its own (creatures met, biomes visited). Undiscovered entries and details show as "???". Kill counts are read from profile stats slot 0. An optional side-panel button (setting SideButton, off by default) opens it too. See [docs/design/exploration-compendium-encyclopedia.md](../design/exploration-compendium-encyclopedia.md).
* **Vanilla facts learned (creature name plates, `EnemyHud`)**: `EnemyHud.LateUpdate` sets `m_refPoint` to the local player's position (not the camera) and calls the private `ShowHud`, which creates a plate (`HudData`, its `m_gui` set active) for every character within `m_maxShowDistance` of that point (code default 10 m, the prefab uses **30 m**, measured 2026-09-29), or a boss within `m_maxShowDistanceBoss` (100 m) that is alerted (`TestShow`, a distance test only; a boss that is not alerted gets no plate, even close). `UpdateHuds`, at the end of the same `LateUpdate`, then switches each plate on or off: players, bosses and the mount stay on; any other creature's plate is **shown** only while its `m_hoverTimer` is under `m_hoverShowDuration` (60 s), a timer reset only while the creature is `Player.GetHoverCreature()`. That comes from `Player.FindHoverObject`: the camera-forward ray (50 m, `m_interactMask`) looks at the **first** collider it hits (the player's own body skipped) and counts it only if it is a `Character` that is not asleep and not hidden by mist, so a wall, a built piece, terrain, a tree or a rock in between blocks it. So "plate created" does not mean "seen". The Encyclopedia records a creature as met from an `UpdateHuds` postfix only when `m_gui.activeSelf`. Building an item's vanilla tooltip (`ItemDrop.ItemData.GetTooltip`) calls `GetSkillLevel` for the item's skill and so adds a missing level-0 skill entry (see section 7).

* **Feasibility**: medium (mostly UI work, data is available).
* **Who needs the mod**: client-only.
* **Kill and tame counts**: read them from [Creature Kill and Tame Counts](../../src/Exploration/Stats.PerCreature) (`CreatureCounts` API, or its documented `Player.m_customData` keys without a reference: see that mod's README, "For mod authors"). Per-creature tames exist only through that mod.
* **Hooks**: `TextsDialog.UpdateTextsList` / `FillTextList` / `ShowText` (private) or a new panel opened from `InventoryGui.OnOpenTexts`; data: `Player.GetKnownTexts`, private `m_shownTutorials` + `Tutorial.instance.m_texts` (re-read raven tips), `m_knownBiome`, `m_knownMaterial`, `GetTrophies`, `PlayerProfile.m_playerStats[0]` (kills, pickups, crafts, food eaten), `ZNetScene` prefabs (`Character`, `CharacterDrop`) and `ObjectDB.m_items` for static info; `Player.AddKnownText` postfix to track discovery order in `Player.m_customData`.
* **Sketch**: Category tabs (Bestiary / Lore / Tutorials / Biomes / Items / Stats) built by cloning the existing TextsDialog list element; bestiary entries unlock on first kill/trophy and show icon, biome, kill counts, known drops (only those the player has picked up: `m_knownMaterial`), and weaknesses from `Character.m_damageModifiers` once killed N times. Tutorials tab lists `Tutorial.m_texts` whose key is in `m_shownTutorials`.
* **Risks**: Must not spoil content (gate everything on player knowledge). Localization of new texts (Jotunn `LocalizationManager` or `Localization.instance.AddWord`). UI mods restyling the inventory (e.g. Auga-style UI overhauls) may break cloned UI paths (`Utils.FindChild` names). Large text rebuilds each open: cache.

### New ability depending on skill level (New)
Interpretation (sheet: "at 25 - 50 - 75 - 100"): each skill gets four named, separate abilities, one per tier. They come on top of vanilla's continuous scaling (section 7), because vanilla has no discrete unlocks (section 10). They are announced when reached and listed in the skills panel. The idea is filed under Exploration, so the first pack covers the movement skills: Run, Jump, Swim, Sneak, Dodge and Ride. Sailing gets its tiers from the Sailing revamp, and other skills can follow through the same registry.

* **Feasibility**: medium. The tier framework is small, and many passive tiers need no Harmony patch (hidden `SE_Stats`, see Sketch). The cost is content: 6 movement skills x 4 tiers = 24 abilities, and all 24 vanilla skills would mean 96. Abilities that need new animations or new physics (ledge mantle, glide pose, diving) are hard and stay in their own ideas (Swim dive). Prefer abilities built on existing animation triggers.
* **Who needs the mod**: depends on the ability.
  * Abilities on the local player are client-only. The owner simulates its own character. Stealth and noise reach enemies through the player ZDO (`Player.UpdateStealth` writes `ZDOVars.s_stealth`, `Character` writes `ZDOVars.s_noise`). The `jump` and `dodge` triggers replicate through `ZSyncAnimation.SetTrigger` (an RPC to everybody). A rider becomes the mount's ZDO owner (`Sadle.RPC_RequestControl` → `SetOwner`), and `Sadle.ApplyControlls` sends the rider's Ride factor in the `Controls` RPC.
  * Ship abilities (Sailing) and abilities that act on other players or creatures need everyone.
  * Skills are client-authoritative (saved in the character by `Skills.Save`), so a server cannot enforce tiers anyway. Server-synced settings would need config sync, which the MC framework does not have yet ([Infra] Server-authoritative config sync).
* **Hooks**:
  * Level: `Skills.GetSkillLevel` returns `floor(base level + SEMan.ModifySkillLevel boosts)`, so meads and set bonuses count. It is not clamped at 100 (`GetSkillFactor` is). It creates a level-0 entry for a skill the player never used (through private `Skills.GetSkill`), so read only skills listed by `Skills.GetSkillList`.
  * Level-up: `Skills.RaiseSkill` postfix. Every natural raise goes through it (`Player.RaiseSkill`, and `Player.Dodge` directly). `Skills.Skill.Raise` adds at most one level per call (max 100). Then `Player.OnSkillLevelup` plays the effect and the `$msg_skillup` message shows. `Skills.CheatRaiseSkill` (console) changes levels without that path.
  * Death: `Player.OnDeath` calls `Skills.Clear` when the `DeathSkillsReset` global key is set. Otherwise it calls `Skills.OnDeath` → `Skills.LowerAllSkills`, on hard deaths only (private `Player.HardDeath`). Then it calls `SEMan.RemoveAllStatusEffects`.
  * Life cycle and UI: `Player.OnSpawned` (re-apply), `Player.Update` (private; 1 s re-evaluation timer, only for `Player.m_localPlayer`), and a `SkillsDialog.Setup` postfix. Vanilla sets each row's tooltip with `UITooltip.Set` from `SkillDef.m_description`.
  * Storage: `SEMan.AddStatusEffect(StatusEffect)` / `SEMan.RemoveStatusEffect`, and `Player.m_customData` for optional peak levels.
  * Ability hooks:
    * `Player.SetControls` (jump and dodge input flags).
    * `Character.Jump` works only on the ground, or at swim depth within 0.25 s of touching the world. In the air it only pulls an attached grappling hook.
    * `Character.ForceJump` sets the `jump` trigger, and `Player.OnJump` takes the stamina.
    * `Player.Dodge` (private) queues a dodge for 0.5 s in `m_queuedDodgeTimer`. `Player.UpdateDodge` fires it once `IsOnGround`. `Player.GetDodgeStaminaUse` is private.
    * `Player.CheckRun`, `Player.OnSwimming` and `Player.UpdateStealth`.
    * `Sadle.UpdateRiding`: mount run and swim stamina = drain x `lerp(1, 0.5, rider skill)`.
  * Fall damage: `Character.UpdateGroundContact` (private). Players take fall damage above 4 m: `clamp01((height - 4) / 16) * 100`, passed through `SEMan.ModifyFallDamage`. It depends only on the height fallen (`m_maxAirAltitude`), not on fall speed. This method is also where `IsOnGround` turns true, so it runs before a queued dodge fires.
* **Sketch**:
  * Registry: abilities are declared as `(SkillType, tier 25|50|75|100, id, name, description, passive|active)`. Injected custom skills (Sailing) register with their own `SkillType` value.
  * Evaluation: on `Player.OnSpawned`, in a `Skills.RaiseSkill` postfix and about every second, compute each skill's tier from `GetSkillLevel` and compare it with the cached set. Do not rely on the level-up event alone, because deaths, meads, set bonuses and the console change levels without it. Keep the cache per character, not per `Player` object, or every respawn re-announces all tiers.
  * Feedback: a new tier shows "New ability: <name>" through `Player.Message`, with the skill icon (`SkillDef.m_icon`). A lost tier shows a quieter top-left line.
  * Passive tiers: one hidden `SE_Stats` per ability, created at runtime.
    * Set `m_hidden = true`: the HUD lists only effects that have an icon and are not hidden.
    * Set `m_ttl = 0`, so it never expires.
    * Give it a unique object name, because `StatusEffect.NameHash` hashes that name.
    * `SEMan.AddStatusEffect(StatusEffect)` clones it without an `ObjectDB` entry.
    * Its fields already cover fall damage (`m_fallDamageModifier`), the fall-speed cap (`m_maxMaxFallSpeed`, applied through `SEMan.ModifyWalkVelocity` in `Character.UpdateWalking`), jump force (`m_jumpModifier`), swim speed (`m_swimSpeedModifier`), stealth (`m_stealthModifier`), noise (`m_noiseModifier`) and the run, jump, dodge, swim and sneak stamina costs. These tiers need no patch.
    * Re-add them on spawn, because death removes all status effects.
  * Active tiers reuse vanilla triggers that other players already see. Example abilities:
    * Jump 25 soft landing (passive fall damage reduction).
    * Jump 50 landing roll: vanilla already accepts a Dodge press in the air and keeps it queued for 0.5 s. An `UpdateGroundContact` prefix only has to see that the queue is set and cut the fall damage. The normal roll then plays by itself.
    * Jump 75 one extra jump in the air: a `Player.SetControls` prefix calls `Character.ForceJump`, with normal stamina. Check in game that the `jump` animation plays in the air.
    * Jump 100 slow fall while Jump is held: the fall-speed cap plus a fall damage cut, since the damage ignores speed.
    * Swim 25 dive (Swim dive idea).
    * Sneak 50 silent steps (`m_noiseModifier`).
    * Ride 50 cheaper mount sprint.
    * Run 25 second wind (`Player.CheckRun`).
  * Skills panel: a `SkillsDialog.Setup` postfix appends the four tiers to each skill's tooltip (unlocked, or greyed with the level needed).
  * Config: each ability has its own toggle.
* **Risks**:
  * Death penalty: on the Normal preset a hard death costs 5 % of every skill. The presets cost 1 % (Casual, Very easy), 2.5 % (Easy), 5 % (Normal) and 7.5 % (Hard), and Hardcore sets `DeathSkillsReset` (wiki). `Game.m_skillReductionRate` defaults to 1, and the Player prefab sets `Skills.m_DeathLowerFactor` to 0.05 (section 7; the C# initialiser is 0.25).
    * With 25-point tiers, a Normal death drops a tier only when the base level is less than about 5 % above it (25 to 26.3, 50 to 52.6, 75 to 78.9). It always drops the level-100 ability (100 becomes 95).
    * Decide between the current level (vanilla-consistent, default) and "highest level reached" (a SkillPeak-style `Player.m_customData` record).
  * Meads and set bonuses unlock and relock tiers temporarily. Avoid message spam, and decide whether boosts count at all.
  * An injected skill without a `SkillDef` gets a `Skill` with `m_info = null` from `GetSkillLevel`, which breaks `SkillsDialog.Setup` and `Skills.Save`. Register the def first (section 7).
  * Balance: tiers stack on vanilla's linear scaling.
  * Stealth stacking: a Sneak tier's `m_stealthModifier` adds up with the *Sneak revamp*'s standing-still status effect (`SE_Stats.ModifyStealth` adds base × modifier for each, and `Player.UpdateStealth` only clamps the sum to 0..1), so together they could reach a factor of 0: unseen at any range. The Sneak revamp floors the combined factor while its effect is active; tune the Sneak tiers with it loaded.
  * The air jump must not fire while a grappling hook is attached (`GrapplingPoint.m_localGrappler`).
  * Compatibility:
    * ImpactfulSkills patches `Character.Jump`, `Character.UpdateGroundContact`, `Character.UpdateSwimming`, `Character.UpdateWalking`, `Player.OnSwimming` and `Character.AddNoise`.
    * PIXPIX Movement adds its own double jump and landing roll.
    * GrindstoneSkills (skill-book page) and SkillPeak also patch the skills panel.
    * Skill overhauls, XP multipliers and death-penalty settings change how fast players reach or lose tiers.
    * Mods that clear status effects remove passive tiers until the next check.
  * Keep abilities on the owner's side. Never trust a remote player's skill for authoritative effects unless it is synced through a ZDO.
