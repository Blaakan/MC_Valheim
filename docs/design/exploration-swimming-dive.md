# Swim Dive — design

| | |
|---|---|
| Mod | Swim Dive |
| GUID / project | `MC.Exploration.Swimming.Dive` (`src/Exploration/Swimming.Dive/`, root namespace `MC.Exploration.SwimmingDiveMod`, package `SwimmingDive`) |
| Category / scope | Exploration / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Swim dive` |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` and `assembly_utils` in `.ref/` (Character, Player, PlayerController, GameCamera, WaterVolume, Floating, EnvMan, SEMan, SE_Stats, ZInput, Humanoid); research briefs of 2026-10-01 (swim physics and input, under-water visuals, framework patterns); sources of BetterDiving (Apache-2.0), Dive In and UnderTheSea (GPL-3.0, read for ideas only), Crystal's Underwater, Aegir, Improved Swimming and ImpactfulSkills read on GitHub (2026-10-01); prefab values measured by the in-world self-tests (2026-10-01) |
| Status | Implemented (v0.1.0 code); smoke test passed 2026-10-01; in-world self-tests passed 2026-10-01 on the final code (`dive.network`, `dive.logic`, `dive.pending`, `dive.dive`; an earlier run failed one `dive.dive` check, Crouch + Jump held at the surface drained after a wave, fixed by D24); not yet tested by hand in game |

## Goal

The user's expected behaviours (idea sheet: "Allows to dive vertically"; run request, 2026-10-01), numbered. Each one
is scope and has at least one test (section 8).

1. **G1 — Crouch dives.** While swimming at the surface, holding Crouch (default Left Ctrl; gamepad `JoyCrouch`)
   makes the character dive (swim down). Diving costs stamina like swimming.
2. **G2 — Under water only up or down.** Under water the player can only swim down (hold Crouch) or up (hold Jump):
   no horizontal movement at all (no look-direction 3D navigation, no forward/back, no left/right).
3. **G3 — Surface unchanged.** At the surface, vanilla swimming is unchanged, plus Crouch to dive.
4. **G4 — Stamina under water.** Under water, stamina drains at the swimming rate whether the player moves or not; at
   the surface, staying still costs nothing, as in vanilla.

### Added beyond the request (small)

- **E1** The camera follows the diver below the surface (personal setting `UnderwaterCamera`).
- **E2** Under-water fog and tint while the camera is under water (personal settings `UnderwaterFog`,
  `UnderwaterVisibility`, `UnderwaterFogColor`).
- **E3** The water surface is visible from below (personal setting `SurfaceFromBelow`).
- **E4** At 0 stamina the diver floats back up while vanilla drowning damage applies, with an "Out of breath" message.
- **E5** Under a ship hull, a dock or a rock, sideways swimming is allowed under water so a diver cannot be pinned
  (safety exception to G2, decision D5).
- **E6** The sea floor (or the bottom of the water volume) stops the descent; Jump never jumps while diving.

Without E1-E3 a dive of more than a metre or two cannot be seen (vanilla clamps the camera above the water, and the
surface is invisible from below), so they are part of v1.

### Non-goals

Breath meter (stamina is the breath, G4); diving gear; 3D navigation (G2 forbids it); a Diving skill; under-water
audio filter; a dive or stroke animation (the diver keeps the treading-water pose); hiding the surface ripple above a
diver; creatures that dive or chase under water; weapons under water.

### Later (cut from v1, one-line sketches)

- Breath meter tied to the Swim skill, separate from stamina.
- Diving gear (weighted belt, helmet lamp) and an Underwater biome.
- Muffled audio under water (postfix `AudioMan.UpdateSnapshots` into the Indoor snapshot; what it does is unverified).
- A fake `forward_speed` so the swim-stroke animation plays while moving vertically.
- Hide the vanilla surface ripple (`Character.UpdateContinousEffects`) while the diver is deep.

## 1. Vanilla behaviour

### 1.1 Liquid level and swim state

- `WaterVolume.OnTriggerEnter` counts the character in; `WaterVolume.UpdateFloaters` (from `MonoUpdaters.Update`,
  once per rendered frame) pushes `WaterVolume.GetWaterSurface` (x/z only: waves, no depth term) into
  `Character.SetLiquidLevel`. `WaterVolume.OnTriggerExit` sets -10000 only when the last volume is left.
- `Character.CustomFixedUpdate` (every character, every client) runs `CalculateLiquidDepth` (feet depth, forced 0
  while teleporting, standing on or attached to a ship) and `UpdateWater`: `m_swimTimer = 0` while depth >
  `max(0, m_swimDepth - 0.4)`. `IsSwimming()` = `m_swimTimer < 0.5`. There is no upper bound: a submerged diver stays
  "swimming". Player prefab (1.0.16, measured by `dive.dive`): `m_swimDepth` 1.5, so the swim threshold is 1.1 m;
  `m_swimSpeed` 2, `m_swimAcceleration` 0.05, `m_swimTurnSpeed` 100; collider height 1.85, radius 0.49; eye 1.79 m
  above the feet.
- The ocean `WaterVolume` (1.0.16, measured at two spots): trigger bounds y -20..40 (50 m under the water level of 30),
  64 × 64 m in x/z; it reached the sea floor at the 12 m test spot. `WaterVolume` never resizes its collider.
- Owner only: `Character.UpdateMotion` picks `UpdateSwimming` while `IsSwimming()`, else flying or walking. It returns
  early when dead and runs `UpdateDebugFly` in debug fly.

### 1.2 `Character.UpdateSwimming` (private)

- Horizontal: target `m_moveDir * m_swimSpeed` (× `GetAttackSpeedFactorMovement`, 0 in `InMinorActionSlowdown`,
  then `SEMan.ApplyStatusEffectSpeedMods`), lerped by `m_swimAcceleration` per tick for players; the applied
  velocity-change force has `y = 0`. `m_moveDir` comes from `Player.SetControls` and is always horizontal.
- Vertical ("buoyancy"): target height `GetLiquidLevel() - m_swimDepth`; below it the body speed moves toward up to
  +10 m/s (50 m/s²), above it toward down to -10 m/s (30 m/s²). Then `m_body.useGravity = true`.
- Rotation only when `m_moveDir.magnitude > 0.1` or `AlwaysRotateCamera()`.
- Animator: `inWater = !IsOnGround()` (read at the start), `onGround = false`.
- `OnSwimming(targetVel, dt)` only when **not on ground** at the start of the call; `targetVel` is the horizontal
  target, not the body speed.
- `m_maxAirAltitude` is reset every swim tick: no fall damage while swimming.

### 1.3 Stamina, drowning, Swim XP (`Player.OnSwimming`)

- Only while `targetVel.magnitude > 0.1`: drain `Lerp(m_swimStaminaDrainMinSkill, m_swimStaminaDrainMaxSkill,
  SkillFactor(Swim))` (code defaults 5 and 2 per second; the Player prefab has 6 and 3, measured), plus the equipment
  swim modifier and
  `SEMan.ModifySwimStaminaUsage`, × `Game.m_moveStaminaRate`, through `UseStamina` (which also × `Game.m_staminaRate`
  and resets `m_staminaRegenTimer`); Swim XP once per second of it (`m_swimSkillImproveTimer`).
- Independent of movement: with no stamina, every second `ceil(maxHP / 20)` damage with `HitType.Drowning`.
- `Player.UpdateStats(float)`: stamina regen × 0 while `IsSwimming() && !IsOnGround()`. So at the surface standing
  still there is no drain and no regen; touching the floor (`IsOnGround`) regen resumes.

### 1.4 Input

- `PlayerController.FixedUpdate` passes only the **press edge** of Crouch (`Crouch` / `JoyCrouch`) and Jump to
  `Player.SetControls`; Crouch toggles `m_crouchToggled`, which `Player.UpdateCrouch` cancels every tick while
  swimming. So the held state must be read from `ZInput` directly (`ZInput.GetButton` by name respects rebinds and
  gamepad layouts). Vanilla precedent: `Character.UpdateDebugFly` maps the Jump button = up and the Left Ctrl key (or
  JoyCrouch held 0.33 s) = down.
- `Character.Jump` works in water when `InLiquidSwimDepth() && m_hitWorldTime < 0.25` (any touch of a
  `s_groundRayMask` collider: floor, rock, hull): it launches the body, uses jump stamina and raises Jump.

### 1.5 Camera and rendering

- `GameCamera.GetCameraPosition` (private) clamps `end.y` to `Floating.GetLiquidLevel(end) + m_minWaterDistance` (code
  default 0.3; 0.4 on the 1.0.16 camera, measured; near clip 0.1..0.5, max distance 8) and sets `m_waterClipping`;
  `UpdateNearClipping` (before the clamp, so with last frame's flag) picks the near plane. `UpdateCamera` (LateUpdate) calls it except in free fly, when dead with a ragdoll (look at the ragdoll,
  position not updated) or attached with a camera point.
- No vanilla under-water rendering path exists. `EnvMan.SetEnv` (from `EnvMan.FixedUpdate`, at most once per rendered
  frame, not at all in frames without a physics step) is the only in-game writer of `RenderSettings.fogColor` /
  `fogDensity`; nothing in the game writes `fogMode` or `fogStartDistance` / `fogEndDistance`. Measured in 1.0.16: fog
  on, mode Exponential, day densities 0.003-0.007. `SetEnv` also sets the shader globals `_SunFogColor`
  (`EnvMan.m_sunFogColor`, the fog colour toward the sun: the time-of-day sun fog colours, lerped from the plain fog
  colour by `clamp01(max(night, day) × 3)`), `_SunColor` and `_AmbientColor`, all with `Shader.SetGlobalColor`
  (`MenuScene.Update` writes the same globals in the main menu). The post-processing `FogComponent` declares
  `_SunDir` and `_SunFogColor` for its `Hidden/Post FX/Fog` shader without setting them itself, so that shader most
  likely reads the globals and blends the fog toward `_SunFogColor` in the sun's direction (the shader source is not
  in `.ref`).
  `StealthSystem.GetLightLevel` reads `RenderSettings.ambientLight`.
- `WaterVolume.m_waterSurface` (MeshRenderer) is one-sided (seen from below = sky; all dive mods turn it over). Measured
  in 1.0.16: the surface is its own object (not the volume's, not a child of it), local rotation 0, shader
  `Custom/Water` with no `_Cull` property (culling is fixed in the shader), shadows off; 201 of 201 loaded volumes
  were safe to turn.
  `WaterVolume.SetupMaterial` writes `_depth` once (4 corners, `m_forceDepth` x4 or the heightmap corners);
  `WaterVolume.Depth` shows the corner order ([3]→[2] along x at z-, [0]→[1] at z+).

### 1.6 Who runs what

The owner simulates its character (the local client for its own player); `ZSyncTransform` and `ZSyncAnimation` carry
position and animator values to others, who compute their own liquid depth from the synced position. The surface
ripple (`Character.UpdateContinousEffects`, outside the owner block) is drawn by every client above any character in
liquid.

## 2. Design

### 2.1 States (G1, G2, G3)

Local player only. `DiveState` (static, owned by the current `Player.m_localPlayer`):

- **Surface** = vanilla swimming.
- **Diving**: the mod drives the vertical speed, except near the surface when there is nothing to do up or down (2.3:
  vanilla buoyancy then keeps it). Starts when Crouch is held while swimming in water with stamina left.
- **Deep** (inside Diving): feet more than **0.5 m** under the vanilla rest height `restY = GetLiquidLevel() -
  m_swimDepth`; stays until the feet come back above `restY - 0.2 m` (hysteresis). The under-water rules (no sideways
  swimming, drain while still, forced ascent at 0 stamina) apply only while deep.
- Diving continues while deep, or while Crouch is held with stamina. So a diver near the surface without Crouch (a
  short tap, or rising above `restY - 0.2`) goes back to vanilla, whose buoyancy finishes the rise smoothly.
- Diving ends at once when a blocker appears: rules pending (4.3), not swimming, not in water (tar, level lost), dead,
  attached or standing on a ship, teleporting, debug fly.

The pure rules live in `DiveLogic` (`Deep`, `Diving`, `TargetSpeed`, `Blocker`), hammered by `dive.logic`.

### 2.2 Keys (G1, G2)

`DiveInput.Read`: `ZInput.GetButton("Crouch") || GetButton("JoyCrouch")` = down, `GetButton("Jump") ||
GetButton("JoyJump")` = up (JoyJump not while the build menu is open), gated by `Player.TakeInput()` (chat, console,
menus, map, inventory, dead, teleporting), `!Hud.InRadial()`, `!InventoryGui.IsVisible()`, like vanilla movement input.
Debug overrides `TestDown` / `TestUp` drive the live self test.

### 2.3 Vertical speed (G1, G2, E4, E6)

`Character.UpdateSwimming` postfix, after vanilla buoyancy wrote the body speed:

- speed `s` = vanilla swim speed (`m_swimSpeed` × attack movement factor, 0 in minor action slowdown, then
  `ApplyStatusEffectSpeedMods` with the vertical direction) × `DiveSpeedMultiplier`;
- target: Crouch → `-s`, Jump → `+s`, both → 0, none → `IdleRiseSpeed` (0 = hold depth), no stamina → `+s`;
- going down: target 0 when the ray from the feet hits the sea floor within the next step + 0.15 m
  (`Character.s_groundRayMask`), or when the point 0.6 m under the feet is outside every water volume (the trigger's
  depth is prefab data, 50 m under the water level in 1.0.16: leaving it would drop the liquid level and the diver
  would fall);
- **near the surface with nothing to do up or down** (not deep, stamina left, target 0: Crouch and Jump held
  together, or the floor or the water bottom stops the Crouch; `DiveLogic.VanillaVertical`): the mod leaves the
  vertical to vanilla for that tick. No write to the body, gravity left on (vanilla set it), `Vy` = 0 (so the diver
  does not count as moving for the drain and Swim XP rules of 2.5), `OnSwimming` not called on ground (vanilla skips
  it). Vanilla buoyancy, which already ran this tick from the body's own speed, keeps the diver on the waves like any
  swimmer, so there is no one-tick jolt, and a wave can never rise over a diver held at one height and make them deep
  (D24). Taking over again (Crouch alone, Jump alone, or deep): `Vy` starts from the body speed vanilla just wrote, as
  at a dive start, so there is no jolt either way. Right after a hand-back the floor ray and the water-bottom probe
  reach **0.5 m** further (`HandBackFloorMargin`): without it, at a floor less than about 0.7 m under the rest height,
  vanilla buoyancy would lift the diver out of the floor ray's reach, Crouch would push them back down, and so on (a
  bob of a few centimetres that would also drain stamina and pay Swim XP, worked out from the code, not seen);
- otherwise the mod's own speed `Vy` is lerped toward the target by `m_swimAcceleration` per tick (at least 0.05, like
  vanilla horizontal swimming), written into the body's `y` speed, gravity off (vanilla turns it on again at the start
  of every swim or walk tick), body woken if asleep (a sleeping body reads as "on ground"). A deep diver with target 0
  (no key, Crouch and Jump, floor) holds its depth this way (G2, D2). Measured: a 3 m dive from the surface averages
  1.6 m/s (own speed -1.99 m/s at the end, swim speed 2).

### 2.4 Sideways lock (G2, E5)

`Character.UpdateSwimming` prefix: while deep, `m_moveDir = 0` and `m_autoRun = false`. Vanilla then lerps its own
horizontal speed to 0 (a short glide), does not rotate and shows the treading-water pose. `Player.SetControls` writes
`m_moveDir` again every tick, so nothing leaks. **Exception:** a ray from the head up to the surface (+0.5 m) that
hits a solid collider (ship hull, dock, rock) keeps vanilla sideways swimming, so a diver pinned under something can
slide out (D5).

### 2.5 Stamina (G1, G4, E4)

`Player.OnSwimming` prefix + finalizer, local diver only:

- target at or under 0.1 (still, or moving only up/down), while deep or moving up or down → target
  `Vector3.down * 0.5`, so vanilla applies its exact drain (skill, gear, status effects, world rates) and keeps
  drowning at 0 stamina. At the surface (not diving) nothing is touched: still = free, as vanilla. A dive that stays
  at the surface without moving (Crouch held where the floor or the water bottom stops the diver at once, as in water
  about 1.6 m deep; Crouch and Jump held together; standing in chest-deep water) is handed back to vanilla buoyancy
  (2.3): it rides the waves, is neither deep nor moving, so it costs nothing either (G4, T32).
- Swim XP stays tied to moving: when the diver is not moving up or down, `m_swimSkillImproveTimer` is parked for the
  call and put back.
- `UnderwaterStaminaMultiplier` (≠ 1) scales `m_swimStaminaDrainMinSkill/MaxSkill` for the call and puts them back.
- On the sea floor vanilla skips `OnSwimming`; the postfix of 2.3 calls it itself (zero target, so the prefix above
  applies) and keeps the animator's `inWater` on while deep. The drain resets the regen timer, so stamina does not come
  back on the floor either. With `UnderwaterStaminaMultiplier = 0` the drain is `UseStamina(0)`, which returns early
  and leaves the regen timer alone: stamina then comes back only while the diver touches a slope or rock
  (`IsOnGround`); a diver hovering above a flat floor (FloorClearance) is not on ground and gets no regen.
- At 0 stamina: no dive start; deep = forced ascent; "Out of breath" top left once per dive.

### 2.6 Camera under water (E1)

`GameCamera.GetCameraPosition` prefix / postfix / finalizer (`DiveCamera`):

- prefix: local player diving, `UnderwaterCamera` on, eye under water by more than 0.35 m (off again under 0.15 m) →
  `m_minWaterDistance = -10000` for this call; the finalizer always puts the saved value back (Aegir and other mods
  see their own value);
- postfix: the camera is kept at least `clearance = clamp(eyeDepth / 2, 0.05, 0.5)` under the surface at its own spot,
  pulled along the eye→camera line (it stays in line of sight, like vanilla wall collision); within 1 m of the surface
  `m_waterClipping = true` and the near plane goes to `m_nearClipPlaneMin` at once, so the 0.5 m near plane never cuts
  a hole in the surface.
- No change to zoom, `m_maxDistance`, tilt or rotation.

### 2.7 Under-water view (E2, E3)

`GameCamera.UpdateCamera` postfix (`UnderwaterView`), every frame. View on = the **camera** is more than 2 cm under the
water surface at its own spot (`Floating.GetLiquidLevel(camera, 1, LiquidType.Water)`; -10000 = no water), not in free
fly, local player present. Held while the local player is teleporting.

- **Fog:** base = `RenderSettings.fogColor/fogDensity` taken when the view starts. Each field is handled on its own:
  its base is taken again whenever RenderSettings no longer hold the value the mod wrote there (SetEnv or another mod
  wrote this frame; SetEnv writes only colour and density). Written: colour =
  `UnderwaterFogColor × clamp(base brightness / 0.5, 0.08, 1) × clamp(1 - depth × 0.015, 0.35, 1)`, density =
  `max(base, 3.912 / UnderwaterVisibility)` (1.978 / V for exponential squared). Start/end are touched only in linear
  mode, which only a fog mod can set (vanilla is exponential): start 0, end `min(base end, V)`, with their own base
  taken the same way, so a SetEnv frame never makes the mod lose the linear start/end. Values are read back after
  writing and compared with a small tolerance. On exit each field gets its base back only if it still holds the mod's
  value. Never `EnvMan.SetForceEnvironment`, never `ambientLight`.
- **Sun-side fog:** the shader global `_SunFogColor` (the fog colour toward the sun, written by `EnvMan.SetEnv` with
  `Shader.SetGlobalColor`) gets the same under-water colour as `RenderSettings.fogColor`, with the same rule: its base
  is taken when the view starts and again whenever the global no longer holds the value the mod wrote, and on exit it
  gets its base back only if it still holds the mod's value. The mod writes the tint with `Shader.SetGlobalColor` (the
  same colour-space conversion vanilla applies), but takes the base, compares and puts back the raw stored value
  (`Shader.GetGlobalVector` / `SetGlobalVector`, no conversion), so the value put back is exactly the one that was
  there in the game's Linear colour space. `_SunColor`, `_AmbientColor` and the camera's `SunShafts.sunColor` (also
  set by `SetEnv`) are light, not fog, and are left alone.
- **Surface from below:** each volume in `WaterVolume.Instances` whose surface is loaded gets
  `localRotation = saved × Euler(180, 0, 0)` and `_depth` = vanilla corners in order [3],[2],[1],[0]. Same material,
  same position. Volumes loaded while under are turned as they come. On exit every saved rotation is written back
  exactly and `_depth` rewritten as `WaterVolume.SetupMaterial` does. A surface whose transform carries the volume's
  collider is never turned (the trigger would move; one warning). In 1.0.16 no surface does (1.5); the live dive
  turned 184 surfaces, none unsafe, and the screenshot from 2 m down shows the surface with its waves from below.
- Every exit restores everything: camera above water, setting off, free fly, logout (no local player), camera replaced,
  feature off (`OnDeactivated`).

## 3. Decisions

- **D1 Velocity override, not `m_swimDepth`.** Raising `m_swimDepth` (what every other dive mod does) also moves the
  swim threshold (`m_swimDepth - 0.4`), so over shallow floors `IsSwimming()` turns off ("swimming on land" and
  "floating after surfacing" bugs of BetterDiving and VikingsDoSwim), and it is read by `Jump`, falling and sinking
  platforms. Overriding the body's `y` speed leaves every vanilla read of `m_swimDepth` alone.
- **D2 Hold depth with no key** (`IdleRiseSpeed = 0`): the literal reading of G2 ("only down with Crouch, up with
  Jump"). The server can make divers float up instead.
- **D3 Under-water rules start 0.5 m under the rest height** (off at 0.2 m): steadier than a head-bone test (the head
  bobs with the animation). Near the surface, going down, the diver still swims normally sideways.
- **D4 Keys read held from ZInput** with the vanilla gates (2.2); Crouch is a toggle cancelled while swimming.
- **D5 Sideways swimming allowed when something solid is above** (flag at the pause): a vanilla-consistent safety
  exception to G2, so a boat drifting over a diver or a dock cannot pin them until they drown.
- **D6 Swim XP only while moving up or down** (flag): vanilla ties Swim XP to moving; holding still under water drains
  stamina but gives no XP.
- **D7 Drain through vanilla `OnSwimming`** with a substituted target: the exact vanilla formula, gear, effects, world
  modifiers and drowning, no copy of it.
- **D8 `UnderwaterStaminaMultiplier` applies to the whole dive** (from the first Crouch, not only once deep) and also
  to the vanilla sideways drain while diving under a ceiling.
- **D9 At 0 stamina: forced ascent**, no new dive, constant "Out of breath" message (no setting). Drowning as in
  vanilla swimming with no stamina.
- **D10 No jump while diving** (all `Character.Jump` calls on the local diver, grappling pull included).
- **D11 Vertical speed = vanilla swim speed** with status effects, × `DiveSpeedMultiplier`; smoothing by
  `m_swimAcceleration` per tick (min 0.05). A faster constant can be chosen if 0.05 feels sluggish in game.
- **D12 Stop at the bottom of the water volume** as well as at the sea floor (the ocean trigger reaches 50 m under the
  water level in 1.0.16, so this only matters where the sea is deeper).
- **D13 Tar refused, Ashlands allowed** without a setting: vanilla heat already punishes diving there.
- **D14 Encumbered players may dive** (vanilla lets them swim).
- **D15 Camera lifted only for a diver whose eye is under water**; `m_maxDistance` never touched; the water distance
  always put back by a finalizer.
- **D16 View keyed on the camera, not the diver:** a death under water keeps the tint on the ragdoll camera, and another
  mod's under-water camera (Crystal's Underwater) gets it too.
- **D17 Fog from a cached base, never compounding** (the `_SunFogColor` global included); never
  `SetForceEnvironment`, never `ambientLight` (traps of BetterDiving and UnderTheSea).
- **D18 Surface turned over, not moved, material kept,** `_depth` corners swapped (Dive In's seam fix).
- **D19 Pending = vanilla swimming** (combat mods' pending rule): a client of a server never plays by its own config;
  Crouch does nothing until the server's rules arrive.
- **D20 Teleport ends the dive** (physics, vanilla takes over at the destination); the view is held during the teleport
  and decided again at the destination.
- **D21 Other dive mods block** (`LocalBlocker`); camera and swim tweaks only get a log line (6.3).
- **D22 Visual settings are personal;** gameplay settings (section Diving) are the server's.
- **D23 Ships and chairs:** no dive while attached or standing on a ship (vanilla already forces liquid depth 0 there).
- **D24 Near-surface zero target = vanilla buoyancy** (2.3; in-world fix of 2026-10-01). A diver who is not deep and
  has nothing to do up or down (Crouch and Jump together, or Crouch stopped by a shallow floor) is left to vanilla
  buoyancy and gravity, so they ride the waves like a surface swimmer. Holding them at one height (the first version)
  let a wave crest rise over them: they turned deep and the under-water rules started (drain while still, no sideways
  swimming) although they never went down. Deep divers still hold their depth (G2); forced ascent at 0 stamina,
  idle rise and the sea-floor stop are unchanged. After a hand-back the floor must be 0.5 m further down before Crouch
  takes over again, so a diver above a shallow floor floats instead of bobbing against it.

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Where |
|---|---|
| Dive state, keys, vertical speed, sideways lock, stamina, jump block | the diver's own game (owner of its body) |
| Camera, fog, surface from below | each player's own game, for their own camera only |
| Join check (`PlayerCheck`), rules push (`ServerRules`) | server (dedicated or host) |
| What others see | synced position and animator (vanilla), surface ripple (vanilla, every client) |

### 4.2 RPCs, ZDO keys, network version

- `MC.Exploration.Swimming.Dive.SettingsRequest` (int layout, client → server) and
  `MC.Exploration.Swimming.Dive.Settings` (ZPackage `DiveRules` layout 1, server → client).
- No other RPC, no ZDO key, no prefab, nothing saved. `ModNetworkVersion` 1 (bump with the rules layout).

### 4.3 Server settings and join check

- `DiveRules`: `DiveSpeedMultiplier`, `IdleRiseSpeed`, `UnderwaterStaminaMultiplier`; wire read with clamping to the
  config ranges (NaN / infinity → default), unknown layout or cut-off package refused.
- `ServerRules` = Sneak Ambush's copy with three upgrades: push 0.5 s after the last settings change (Moveset
  `PushDelay`), push only to `NetworkGate.PeerCompatible` peers (Creature Morale), no store while the client copy is
  off (Moveset).
- Pending: a client of a server uses `DiveRules.Pending` (no dive) until the server's rules arrive; rules belong to
  the `ZNet` session they came on.
- `PlayerCheck` (verbatim copy): refuse after 1 s grace players whose game is not `PeerCompatible` (no mod, off, other
  network version) with vanilla "Incompatible version", unless `AllowPlayersWithoutMod`.
- `ModMultiplayerNotes`: "Install it on the server (or the host) and on every player's game. Diving changes what
  players can do in water, so the server refuses players who do not have the mod, have it turned off or have another
  version of it (their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the
  settings of the server (or host) apply to everyone. Other players see a diver move up and down in the normal swimming
  pose. Nothing is saved on items, creatures or buildings."

### 4.4 Hand-off cases

- Nothing persistent: no item, structure or ZDO is touched, so nothing reaches a player without the mod.
- A diver seen by a player without the mod (AllowPlayersWithoutMod): the synced position shows them going down and
  up in the treading-water pose, with the vanilla ripple above them (M03).
- Creatures owned by another game see the diver's synced position; swimming creatures cannot dive (their own
  `m_swimDepth`), so a deep diver is out of their reach (unverified reach, T31, M08).

### 4.5 Dedicated server

The server runs only the join check and the rules push (no local player, no camera). Its config decides the Diving
settings for everyone.

## 5. Config

| Section | Key | Default | Range | Scope |
|---|---|---|---|---|
| General | Enabled | true | | own |
| General | AllowPlayersWithoutMod | false | | server only |
| Diving | DiveSpeedMultiplier | 1 | 0.25-3 | server wins |
| Diving | IdleRiseSpeed | 0 | 0-2 m/s | server wins |
| Diving | UnderwaterStaminaMultiplier | 1 | 0-5 | server wins |
| Visuals | UnderwaterCamera | true | | personal |
| Visuals | UnderwaterFog | true | | personal |
| Visuals | SurfaceFromBelow | true | | personal |
| Visuals | UnderwaterVisibility | 25 | 5-100 m | personal |
| Visuals | UnderwaterFogColor | `1A5261FF` | colour | personal |

A change in section Diving makes a new own snapshot and (server) a push; General and Visuals never do.

## 6. Compatibility

### 6.1 Sibling mods (same release)

- **Sailing Skill** (MC, same run): no shared method. A diver cannot be on a ship; jumping off a ship into water then
  diving is plain vanilla + this mod.
- **Trinkets on Demand** (MC, same run): no shared method.

### 6.2 Other MC mods

- **Weapon Moveset**: prefixes/postfixes `Character.Jump` and compares `m_jumpTimer`. A skipped original (this mod's
  prefix returns false while diving) leaves the timer unchanged, so it sees "no jump" (X01). Its `MoveTracker` reads
  `IsSwimming()`; diving is swimming.
- **Dual Wielding**: hides and shows hands on `IsSwimming() && !IsOnGround()`; a diver hovering above the sea floor
  (FloorClearance) is not on ground, so both hands stay hidden; only a touch of a slope or rock counts as on ground,
  like vanilla `EquipItem` (X02).
- **Sneak Ambush**: reads `IsCrouching()` (swimming cancels crouch) and computes its fog bonus from the environment in
  an `EnvMan.SetEnv` postfix, not from RenderSettings, so the under-water fog gives no stealth (X03).

### 6.3 Popular external mods

| Mod | GUID | Overlap | Action |
|---|---|---|---|
| BetterDiving | `MainStreetGaming.BetterDiving` | dive controller, camera, fog, surface | LocalBlocker |
| Dive In | `sighsorry.DiveIn` | same keys, dive controller, camera, fog, surface | LocalBlocker |
| UnderTheSea | `Searica.Valheim.UnderTheSea` | as BetterDiving | LocalBlocker |
| VikingsDoSwim | `blacks7ar.VikingsDoSwim` (from other mods' incompatibility attributes) | crouch dive, fog | LocalBlocker |
| Valheim Diving Mod (original) | `ch.easy.develope.vh.diving.mod` | as BetterDiving (dead on 1.0) | LocalBlocker |
| Underwater (Crystal) | `dev.crystal.underwater` | camera ignores water (transpiler), walk on the sea floor | log only |
| Aegir | `Aegir` (inferred) | `m_minWaterDistance` in debug fly | log only (the finalizer restores its value) |
| NoUnderwaterCamera | name match | pushes the camera back above water | log only (the view never turns on) |
| Improved Swimming | `projjm.improvedswimming` | swim speed and stamina (old method names) | log only |
| ImpactfulSkills | `MidnightsFX.ImpactfulSkills` (source read 2026-10-01, updated for 1.0) | transpilers: swim speed (`Character.UpdateSwimming`, replaces the `m_swimSpeed` read), swim stamina (`Player.OnSwimming`, scales the cost right before `UseStamina`), jump force (`Character.Jump`) | log only: the dive speed does not get its swim bonus (`DiveController.SwimSpeed` reads `m_swimSpeed`); its drain reduction composes with the still-diver target; no jump while diving (original skipped); X06 |

## 7. Implementation plan

### 7.1 Files (`src/Exploration/Swimming.Dive/`)

`Plugin.cs` (config, life cycle, `LocalBlocker`), `DiveRules.cs`, `ServerRules.cs`, `PlayerCheck.cs`,
`Patches/ZNetPatches.cs` (Both plumbing, Sneak Ambush copies + upgrades), `DiveState.cs` (state + `DiveLogic` pure
rules), `DiveInput.cs`, `DiveController.cs` (tick, sideways lock, vertical speed, floor and ceiling rays),
`Patches/CharacterPatches.cs`, `Patches/PlayerPatches.cs`, `DiveCamera.cs` (hook A), `UnderwaterView.cs` (hook B: fog,
surface), `Visuals.cs` (personal settings accessor), `Patches/GameCameraPatches.cs`, `ForeignMods.cs`, `SelfTests.cs`.

### 7.2 Harmony patches and other entry points

| Target | Kind | Why |
|---|---|---|
| `Character.UpdateMotion(float)` (private) | prefix | dive state every owner tick of the local player (reference check first) |
| `Character.UpdateSwimming(float)` (private) | prefix | sideways lock; remember `IsOnGround` for the postfix |
| `Character.UpdateSwimming(float)` | postfix | vertical speed, gravity off, floor drain |
| `Character.Jump(bool)` | prefix (bool) | no jump while diving |
| `Player.OnSwimming(Vector3, float)` (protected override) | prefix + finalizer | drain while still, XP rule, stamina multiplier |
| `GameCamera.GetCameraPosition(float, out Vector3, out Quaternion)` (private) | prefix + postfix + finalizer | camera under water |
| `GameCamera.UpdateCamera(float)` (private) | postfix | fog, surface from below |
| `ZNet.OnNewConnection`, `ZNet.RPC_PeerInfo`, `ZNet.Update` | postfix | rules and join check |

All single overloads in 1.0.16. No transpiler.

### 7.3 State and lifetime

`DiveState` (static, owner = `Player.m_localPlayer`, reset when it changes), `DiveCamera._lifted`, `UnderwaterView`
(fog and sun-fog base and last written values, turned volumes with saved rotations, unsafe set, camera instance).
Nothing saved.

### 7.4 Live toggle

`OnDeactivated` (patches still on): self tests unregistered (overrides cleared), `DiveController.Shutdown` (gravity
back on, state reset), camera lift cleared, `UnderwaterView.RestoreAll` (fog and `_SunFogColor` back if still ours,
every surface back), join check and rules stopped. From the next tick vanilla buoyancy lifts a diver. `OnActivated`:
state reset, rules and join check started, self tests registered.

### 7.5 Debug in-world self-tests (`SelfTests.cs`)

- `dive.network`: `DiveRules` wire round trip, clamping, layout refusal, only `Pending` pending, `ServerRules.Receive`
  refused on a server, `Select` four cases, `Settled` debounce, `PlayerCheck.Decide` seven cases.
- `dive.logic`: `DiveLogic` rules (including `VanillaVertical`: near the surface with target 0 and stamina = left to
  vanilla; deep, moving, idle rise or 0 stamina = not), camera maths, fog maths, corner swap, `ForeignMods.Classify`;
  fog override on
  RenderSettings in one frame (no compounding, base taken again, put back, left alone when overwritten, linear
  start/end put back after a SetEnv that rewrote colour and density); the same for `_SunFogColor` (holds the fog
  tint, base not overwritten by the mod's own value, taken again after a SetEnv-style `SetGlobalColor`, put back
  exactly, left alone when someone else wrote), with a NOTE of how Unity stores a global colour in the game's colour
  space; surface flip round trip on a loaded
  `WaterVolume` (rotation and `_depth` back exactly; fails when surfaces are loaded but none is safe to turn); NOTE of
  the surface prefab layout.
- `dive.pending`: pending rules refuse the dive, pending wins over test rules.
- `dive.dive` (live): deep water found with `WorldGenerator` (the probe spawn is far from water), `Player.TeleportTo`
  there and back; pending = no dive (rest height sampled again at the check: it follows the waves); Crouch dives
  > 2 m; release holds depth (< 0.3 m in 2 s); camera under, fog on, at least one surface turned, water distance never
  left changed; screenshots (under water, looking up, surface); open ocean = the ceiling rule never fires, and forward
  input moves < 0.4 m; `Jump` with a fresh world touch does not jump; still under water drains > 1 stamina in 2 s
  (world StaminaRate modifier removed and put back); Jump rises and hands back at the surface (gravity on again); still
  at the surface drains < 0.2; Crouch and Jump held at the surface: `Deep` and the hand-back sampled every physics tick
  of the 2 s window, the vertical is left to vanilla (hand-back, gravity on) on every tick and the drain is < 0.2 (NOTE
  of the ride: deep ticks, hand-back ticks, lowest feet under the rest height, largest own speed; NOTE and skip only
  if a wave still made the diver deep, which D24 should prevent); view back to normal. NOTE dumps: player swim values, collider, eye/head height, body,
  fixed dt, camera values, water volume at the spot (trigger bottom vs ground), fog values at the surface and under
  (with `_SunFogColor`, whether it equals the fog colour, and vanilla `EnvMan.GetSunFogColor`).
  Soft skip (NOTE + pass) when no deep water exists within 5 km.

### 7.6 Shared framework used

`ModPlugin` (life cycle, `LocalBlocker`), `NetworkGate` (`PeerCompatible`, `PeerProblem`, `PeerStateChanged`),
`PatchGuard`, `Log`, `SelfTest`, `ConfigurationManagerAttributes`. Nothing added to `src/Shared`.

### 7.7 Implementation notes

- The class for hook A is `DiveCamera` (a class `UnderwaterCamera` would clash with the config field
  `Plugin.UnderwaterCamera`).
- The dive state is evaluated in a `Character.UpdateMotion` prefix rather than in `UpdateSwimming`, so it is also
  cleared on ticks where vanilla does not swim (land, debug fly, death).
- `DiveState.Owner` guards every patch: after a logout the stale state never applies to the new local player.

## 8. Test plan (`TESTING.md`)

### Setup

Console (`devcommands`, `god`, `heal`, `resetskill Swim`, `tod`, `env`, `debugmode` + Z, `freefly`, `goto`; all in
`Terminal`), deep water, Debug log. Prefab names `Karve` and `Serpent` are checked in the 1.0.16 runtime dump
(`docs/game/exploration-player.md`, `docs/design/combat-creatures-morale.md`).

### Single player

T01-T32: G1 (T01, T11, T12), G2 (T02, T03, T04, T13, T14), G3 (T05, T29), G4 (T06, T07, T08, T27, T32), E4 (T09),
Swim XP (T10), E1-E3 (T15-T17, T23), tar and Ashlands (T18, T19), exits (T20-T22, T24), settings (T25-T27),
encumbered (T28), menus (T30), serpent (T31).

### Multiplayer

M01 server rules, M02 refused / allowed, M03 how others see a diver (hand-off), M04 other version / turned off, M05
server without the mod, M06 two divers, M07 host, M08 creature owned by another game.

### Cross-mod

X01 Weapon Moveset, X02 Dual Wielding, X03 Sneak Ambush, X04 other dive mods, X05 Crystal's Underwater, X06
ImpactfulSkills.

### Live toggle, Enabled = false, clean log

L01 (toggle mid-dive: vanilla buoyancy lifts, visuals back), L02, L03.

## 9. Open questions and unverified

### Open questions for the user

- D5 (sideways swimming under a ceiling) and D6 (no Swim XP while holding still) are vanilla-consistent choices to
  confirm.
- Vertical smoothing (D11): 0.05 per tick reaches about 92 % of the target speed in 1 s; the live dive averaged
  1.6 m/s over 3 m at a swim speed of 2. It may still feel sluggish by hand; a faster constant is a one-line change.
- Diving makes serpents easy to escape (only stamina limits it). Keep, or add a cost later?
- ImpactfulSkills (6.3): past its Swim level, surface swimming gets its speed bonus but diving does not, so a skilled
  player swims forward faster than they dive. Align later (mirror its formula, or read the speed through its public
  `ModifySwimSpeedbySkill`), or leave it: the dive speed is the vanilla swim speed by design (D11).
- **Olive haze when looking up (in-world screenshot `underwater-looking-up`, noon, 2 m down, pitch -50): first fix in,
  look to confirm.** Looking level the view was right (teal tint, the turned surface with its waves), but looking up
  toward the noon sun the top of the screen turned olive/yellow instead of water-blue. Likely cause (the shader source
  is not in `.ref`): the fog shader blends toward the shader global `_SunFogColor` (`EnvMan.m_sunFogColor`, the day's
  sun-side fog colour, set by `EnvMan.SetEnv`) in the sun's direction, and the mod only replaced
  `RenderSettings.fogColor`. Done: while the view is on, `_SunFogColor` gets the same under-water tint as the fog
  colour, with the fog colour's cached-base rule (2.7, "Sun-side fog"); every exit puts it back if still the mod's.
  The `dive.logic` round trip checks the override, no compounding, the base taken again, the put back and the "left
  alone" case, and `dive.dive` NOTEs the global under water. Whether the haze is gone is to be confirmed by the next
  in-world `underwater-looking-up` screenshot and by hand in T17 at noon. If it stays, the next candidates are the
  camera's `SunShafts` (`sunColor` = the sun light colour from `SetEnv`; an image effect the fog does not cover) and
  the sky and sun seen through the water shader's transparency (the sky is not fogged).

### Measured by the in-world self-tests (2026-10-01, Valheim 1.0.16)

Player `m_swimDepth` 1.5, `m_swimSpeed` 2, `m_swimAcceleration` 0.05, swim drain 6 (Swim 0) to 3 (Swim 100) per second;
camera `m_minWaterDistance` 0.4; ocean `WaterVolume` trigger y -20..40 (50 m under the water level; reached the floor at
the 12 m spot); 201 loaded volumes, all safe to turn (surface is its own object, shader `Custom/Water`, no `_Cull`); fog
on, Exponential; live dive: 1.6 m/s average descent, 6 stamina per second while holding still at Swim 0. Names:
`Karve`, `Serpent` (1.0.16 runtime dump), `goto` (`Terminal`), ImpactfulSkills GUID (its source).

### Still unverified (and how to verify)

| What | How |
|---|---|
| No seams or squares between water tiles after the flip; the surface seen from below at noon (olive haze above gone with the `_SunFogColor` override) | next in-world `underwater-looking-up` screenshot, T17, T16 |
| Body sleeps when held at 0 speed | handled by `WakeUp`; T03 |
| Near-surface hand-back (D24): the diver rides storm waves without turning deep; no bob above a floor 0.2-0.7 m under the rest height (`HandBackFloorMargin`) | next in-world `dive.dive` run (ride NOTE); T32 by hand |
| Serpent reach vs a deep diver | T31, M08 |
| Tombstone floats up from depth; logout under water | T20, T22 |
| Other ocean volumes (Ashlands, Deep North) have the same 50 m trigger | T19; the mod stops at the trigger bottom anyway (D12) |
| GUIDs of VikingsDoSwim (from other mods' attributes), Aegir (inferred), NoUnderwaterCamera (name) | X04 (optional) |

## Implementation notes

- Exit rule refined against the spec: a dive ends when the diver is not deep and Crouch is not held with stamina (the
  spec's "feet above `restY - 0.2` and Crouch not held" is the rising case of it); Deep uses the 0.5 / 0.2 m
  hysteresis. This avoids a hover state near the surface with neither the under-water rules nor vanilla swimming.
- Added the water-volume-bottom stop (D12) and the forced `inWater` animator value on the sea floor.
- Review fixes (2026-10-01): the still-diver drain applies only while deep or moving up or down (a dive held at the
  surface by a shallow floor or by Crouch + Jump cost stamina before; T32); fog fields are tracked one by one and
  start/end only in linear mode (a SetEnv frame could make the mod keep its own start/end as the base); self tests made
  stricter (open water must not count as a ceiling, at least one surface must be turned, the pending check samples the
  rest height again because it follows the waves); ImpactfulSkills classified as log only.
- Olive haze fix (2026-10-01, after the in-world screenshot looking up): `_SunFogColor` now gets the under-water tint
  too (2.7), with the same cached-base and "put back only if still ours" rule as the fog colour; base and put back
  use the raw stored vector so the Linear colour-space conversion of `Shader.SetGlobalColor` cannot shift the value
  put back. Self tests extended (`dive.logic` round trip, `dive.dive` NOTE); T17 expects no haze toward the sun.
- Wave fix (2026-10-01, after the in-world `dive.dive` failure "Crouch and Jump held at the surface: no drain",
  20.9 -> 14.1 stamina in 2 s; the run before skipped the same check because a wave took the diver under): a near-surface
  diver with target 0 was held at an absolute height with gravity off, so the waves rose over them, `Deep` turned on and
  the under-water rules started. Now the near-surface zero target is left to vanilla buoyancy (2.3, D24), with
  `DiveState.HandedBack`, the pure rule `DiveLogic.VanillaVertical` (checked by `dive.logic`) and the 0.5 m floor
  margin after a hand-back. `dive.dive` now also checks that every tick of the window is handed back and NOTEs the
  ride. T32 rewritten (waves, 2 m water).
