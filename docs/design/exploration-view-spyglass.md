# Spyglass — design

| | |
|---|---|
| Mod | Spyglass |
| GUID / project | `MC.Exploration.View.Spyglass` (`src/Exploration/View.Spyglass/`, root namespace `MC.Exploration.ViewSpyglassMod`, package `Spyglass`) |
| Category / scope | Exploration / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Spyglass` |
| Game version checked | Valheim 1.0.16 (Unity 6000.0.75f1, built-in render pipeline, deferred), decompiled `assembly_valheim`, `assembly_utils` and `assembly_postprocessing` in `.ref/` (GameCamera, Player, PlayerController, Humanoid, Character, CharacterAnimEvent, VisEquipment, ItemDrop, ObjectDB, Hud, EnvMan, ZoneSystem, ZNetScene, ZDOMan, TerrainLod, Heightmap, PostProcessingBehaviour, CameraEffects, UpscaledFrameBuffer, FrameBufferScaler); research briefs of 2026-10-02 (camera, input, items, Distant Horizons, rendering, repo patterns, existing mods); Advize's Spyglass (GPL-3.0, read for ideas only), Azumatt's FirstPersonMode (AGPL-3.0), Landoria FirstPerson, ImmersiveFirstPerson, Customizable Camera, CameraTweaks and ValheimPlayerModels read on GitHub (2026-10-02); runtime facts from the in-world self-tests (2026-10-02) |
| Status | Implemented (v0.1.0 code); smoke test passed 2026-10-02; in-world self-tests passed 2026-10-02 (`spyglass.network`, `spyglass.item`, `spyglass.view`, `spyglass.pose`, `spyglass.far`, `spyglass.export`, and Distant Horizons' `horizons.boost`), then a multi-agent review (11 findings, all fixed: section 9); on the fixes (with that day's Distant Horizons near-ground update) the smoke test of all 25 mods, the framework test and the in-world run (14/14) passed again; after the change to a tool taking both hands (user request, D2) the smoke test of all 25 mods and the in-world run (12/12) passed again; not yet tested by hand in game |

## Goal

The user's expected behaviours (idea sheet: "See things far away"; run request, 2026-10-02), numbered. Each one is
scope and has at least one test (section 8).

1. **G1 — Recipe.** A new item recipe crafts a spyglass from glass and bronze.
2. **G2 — Right hand.** Equipping the item takes the right-hand slot like a one-handed tool.
3. **G3 — Raise animation.** Using the item (left click) plays a short animation of the character placing the
   spyglass in front of the eye while the camera zooms in, transitioning to the spyglass view.
4. **G4 — Spyglass view.** While looking, the view is narrower (clear in the middle of the screen, blurry on the sides)
   and shows terrain and objects far away in the aimed direction.
5. **G5 — Aim, no walking.** While looking, the player can aim to look around (with a smaller sensitivity since the
   view is zoomed) but not move around.
6. **G6 — Beyond the vanilla range.** Terrain and objects shown may be outside the vanilla rendering range; they must
   be visible regardless (Distant Horizons if it can help; loading the looked-at area only if seamless).
7. **G7 — Assets.** The icon and the tool model are made for the mod.

### Added beyond the request (small)

- **E1** Mouse wheel (and the gamepad camera zoom buttons) change the zoom while looking, x1.5 to the server's
  `MaxMagnification` (default x8).
- **E2** `HoldToLook` option: up only while Attack is held.
- **E3** Other players with the mod see the raised pose (player ZDO bool, each game runs the arm pose).
- **E4** Fog clearing in clear weather while looking, only with Distant Horizons (`FogClearing`).
- **E5** The spyglass lowers by itself on a real hit, stagger, knockback, menus; drops at once when put away, sitting,
  swimming, ship helm, death, teleport.
- **E6** Distant Horizons gains a "view boost" (its new settings `SpyglassDetail`, `SpyglassMaxBoost`): finer land and
  farther objects in the looked-at direction while a spyglass is up (G6).
- **E7** A click with the spyglass never punches, also while the feature is off (always-on attack-input guard).

### Non-goals

Revealing the map or dropping pins from the spyglass, showing the name or stars of the looked-at creature (backlog
"Inspiration" ideas, cut to keep the run's scope); upgrades and quality levels; durability; a crow's-nest piece;
seeing far creatures (impossible, 1.6); loading far zones (rejected, D6).

### Later (cut from v1, one-line sketches)

- Map pin on the looked-at point (`Minimap.AddPin` at a long raycast hit) and map reveal in the view cone
  (`Minimap.Explore`, throttled), tying in with the Cartography table revamp.
- Target readout (name, stars, distance) from a long raycast (`Physics.Raycast` against characters within the loaded
  area).
- Upgrade levels (`m_maxQuality`) for stronger zoom.
- Distant Horizons option "draw the far world only through the spyglass" (DH would build tiles but show them only while
  a spyglass is up).

## 1. Vanilla behaviour

### 1.1 Camera (`GameCamera`)

- `LateUpdate` runs `UpdateFOV`, `UpdateBaseOffset`, `UpdateMouseCapture`, then `UpdateCamera(unscaledDeltaTime)`.
  `UpdateCamera` writes `m_camera.fieldOfView = m_fov` and `m_skyCamera.fieldOfView = m_fov` **every frame**, reads the
  wheel into `m_distance` (behind a menu gate), then places the camera (ragdoll look-at, attach camera point, or
  `GetCameraPosition`: eye pulled back along `-m_eye.forward` by `m_distance`, collision, near clip, water clamp) and
  applies shake. Prefab values (runtime): `m_fov` 65, `m_minDistance` 1, near clip 0.5, far clip 20000.
- `SetTempFOV`/`ResetTempFOV` (only user: `GrapplingPoint`) capture `m_fovBase` once per session and pull `m_fov`
  forever: unusable for a zoom. `PlayerController.m_mouseSens` is rewritten by the settings screen: unusable for aim
  speed.
- There is no vanilla first-person mode; `m_fpsOffset` is used only when `m_distance <= 0`, which `m_minDistance` 1
  forbids.
- `m_eye` is a child of the player root at local (0, 1.808, 0) (runtime); only its rotation is written
  (`Player.UpdateEyeRotation`).

### 1.2 Body hiding

`Player.FixedUpdate` calls `SetVisible(false)` when the camera is within 2 m of the feet; `Character.CustomFixedUpdate`
calls `SetVisible(true)` every tick (owner). `SetVisible` moves the visual's `LODGroup.localReferencePoint` far away
and back, only when `m_lodVisible` changes. Equipped items join LOD 0 (`VisEquipment.UpdateLodgroup`), so they hide
with the body. In normal play the camera comes that close only when something pulls it in (a wall or the ground
behind the player, a steep look up).

### 1.3 Input

`PlayerController.FixedUpdate` builds the move vector and click edges (`attack` = pressed this tick) and calls
`Player.SetControls(movedir, attack, attackHold, secondaryAttack, secondaryAttackHold, block, blockHold, jump, crouch,
run, autoRun, dodge)`. `PlayerController.LateUpdate` calls `Player.SetMouseLook(delta)` (mouse, gamepad stick; gyro
from `Player.UpdateGyro`). `Player.PlayerAttackInput` calls `Humanoid.StartAttack`, which needs
`HavePrimaryAttack()` (a non-empty attack animation). Auto-run keeps `m_moveDir` without input; a toggled block keeps
`m_blocking`.

### 1.4 Items and hands

`Humanoid.EquipItem`: `Tool` empties both hands; `OneHandedWeapon` takes the right hand and keeps a shield or torch in
the left (a right-hand torch moves left). `GetCurrentWeapon` returns the right item only if it `IsWeapon()`, else the
unarmed weapon (so a `Tool` without build pieces punches). `VisEquipment.AttachItem` instantiates the prefab's direct
child `attach` under the hand bone and disables its colliders (`CleanupInstance`); `m_attachOverride` picks the back
slot (`Tool` = hip). The vanilla `KnifeFlint` (one-handed, `Unarmed` grip, hip) has its only look in `attach`, which is
also its look on the ground (runtime). There is no glass item in 1.0.16 (`Obsidian` is "dark volcanic glass",
`Crystal` "a shard of crystal").

### 1.5 Fog

`EnvMan.SetEnv` (from `FixedUpdate`, at most once per frame, not in frames without a physics step) writes
`RenderSettings.fogDensity` (Exponential fog, clear days about 0.003-0.007); Distant Horizons multiplies it by 0.25 in
clear weather in a postfix.

### 1.6 What is drawn far away

Without Distant Horizons: real zones and all objects within the simulation area (about 300-560 m); `TerrainLod` draws a
3x3 grid of 10 m terrain to ±1200 m that the terrain shader fades to black beyond about 400 m, hidden by fog. Creatures,
ships and carts exist only around players (ZNetScene active area). With Distant Horizons: terrain over the whole world
(quadtree, detail by distance only), far trees, rocks, bushes, logs and buildings from the ZDOs the game holds (all of
them for single player and the host; areas visited this session for a client), never creatures.

## 2. Design

### 2.1 Item (G1, G2, G7)

- Clone of `KnifeFlint` under an inactive `DontDestroyOnLoad` holder (Smoke Screen pattern): name `MC_Spyglass`,
  `Tool` like the hammer and hoe (D2: right hand, both hands emptied on equip, tool back slot when put away, tooltip
  "two-handed"), no build pieces (no build mode), `Unarmed` grip, skill `None`, no damage, empty primary and secondary
  attack animations, no durability, weight 1, quality 1, icon drawn in code. For a tool `Humanoid.GetCurrentWeapon` is
  the unarmed weapon, so a click would punch with the fists: an always-on prefix on `Player.PlayerAttackInput` skips
  the attack input while the right hand holds a spyglass (feature on: the `SetControls` prefix already took the
  click). Block blocks with the fists, as with the hammer.
- Model (`SpyglassModel`): a lathe mesh (three telescope tubes, rings, leather wrap, flared lens end, crystal lenses)
  with four materials copied from the knife's material (`Custom/Creature` shader at runtime) with textures drawn in
  code (other texture slots of the vanilla material cleared). Laid along the knife blade's axis (measured from the
  knife mesh bounds) and as long as the knife +5%, grip at the attach point, eyepiece toward the thumb. An empty child
  marks the eyepiece for the arm pose. A capsule collider on the `item` layer for the dropped copy.
- Recipe `Recipe_MC_Spyglass` at `forge` level 1, `Bronze:2,Crystal:2` (server rules). Registered always on (item
  database, network prefab list); enabled only while the feature is active and the rules are not pending.
- Icon (`SpyglassIcon`): 128 px, drawn in code (4x4 supersampling, cylinder shading, outline). The package icon
  (256 px, dark plate) comes from the same drawing (Debug self-test export).

### 2.2 States (G3, G5)

`Scope` (local player): `Idle` → `Raising` (0.6 s) → `Raised` → `Lowering` (0.4 s) → `Idle`, with a progress p 0..1.
Input from a `Player.SetControls` prefix (click edges already computed by `PlayerController`): Attack while Idle or
Lowering = raise; Attack (toggle mode) or Block while up = lower; in hold mode, releasing Attack lowers. While
`Raising`/`Raised` every input is zeroed (look stays free through `SetMouseLook`); on raise auto-run, move direction,
run and block are cleared. When the player is sitting, in an emote or on a doodad the click is left to vanilla.
Checks every frame (`Player.Update` postfix): put away / dead / teleporting / cutscene / attached / bed / sleeping /
ship helm or saddle / riding / swimming / build mode / debug fly / free camera = abort at once; menus (`TakeInput`),
stagger, knockback, radial, piece selection = animated lower; a real hit (`Player.OnDamaged`, not fire, frost, poison,
smoke or drowning) = lower. Rules pending = no raise, with a message at most every 3 s.

### 2.3 Camera and view (G3, G4, G5)

`GameCamera.UpdateCamera` postfix (`Priority.Last`, after first-person mods and Swim Dive), from p:

| What | Curve |
|---|---|
| Camera slides from the vanilla spot to `m_eye`, turns to the eye's look | smoothstep p 0.10 → 0.70 |
| Field of view: `2·atan(tan(vanilla/2) / M^z)` on the camera and the sky camera | z = smoothstep p 0.30 → 1 |
| Own body hidden while the camera is within 0.45 m of the eye, or within 2 m of the feet (vanilla's own rule) | distance |
| Near plane down to vanilla's minimum (0.1) while moved: walls and roofs right before the face stay drawn | c > 0 |
| Overlay (round view) closes in from 3.2 times its size, blur and dark edge fade in | smoothstep p 0.40 → 1 |
| Fog: density × (1 − FogClearing × o), only with Distant Horizons, faded out between 0.006 and 0.02 density | o |
| Distant Horizons message (zoom, weight o, forward, field of view) | every frame |

Vanilla fields are never written (`m_fov`, `m_distance`): vanilla rewrites position, rotation and field of view every
frame, so lowering gives the normal camera back by itself. The prefix keeps `m_distance` and the postfix puts it back
while the spyglass is up, so the wheel zooms the spyglass instead of the camera. Aim: `SetMouseLook` prefix scales the
input by `AimSensitivity / magnification` (the angle a mouse movement turns shrinks with the field of view, so the
view moves on screen at the same speed). `AlwaysRotateCamera` postfix turns the body with the aim. `UpdateHover`
postfix clears the hover objects; `Hud.UpdateCrosshair` postfix switches the crosshair `Image` off (vanilla re-shows
its GameObject every frame) and clears the hover text and piece health bar.
Body: while up, `ScopeCamera` owns the LOD reference point (written once per rendered frame, after every physics tick,
from the slid camera: hidden in the head or by vanilla's 2 m rule, so it never flickers and never shows a body vanilla
hid); on release it writes what vanilla wants (`m_lodVisible`). Lowering and raising run on real time
(`Time.unscaledDeltaTime`): the pause menu (time scale 0) does not freeze the view half up. World exit: the camera
postfix aborts once `Game.IsShuttingDown()`, and a `GameCamera.OnDestroy` postfix aborts when the game scene unloads, so
the overlay (which outlives scenes) never stays over the main menu.

### 2.4 Overlay without a shader (G4)

No asset bundle means no custom shader. `ScopeCapture` (`OnRenderImage` on a component added last to the main camera,
on only while the overlay shows) shrinks the finished frame by 2 four times and back up once (bilinear = soft blur)
into a 1/8-size render texture without alpha (`RGB111110Float`, else `RGB565`), then copies the frame through
unchanged. The overlay is its own screen-space canvas at sort order HUD−1 (399; above the render-scale picture at 0),
with two UI graphics building ring meshes: `BlurRing` (RawImage showing the small texture full screen, vertex alpha 0
inside the sharp circle rising to 1 at 1.3 times its radius) and `DarkRing` (black, from 1.02 to 1.7 times the radius,
then up to `EdgeDarkness`). `UI/Default` multiplies the texture by the vertex colour, so vertex alpha draws the soft
edges. Circle radius = `ClearViewSize` × half the screen height (round on any aspect).

### 2.5 Arm pose (G3, E3)

Procedural (no new clip is possible without a bundle; vanilla emotes, eat and drink are one-shots tied to other
systems). `ArmPose` in a `Player.LateUpdate` postfix (every player, every game): weight = smoothstep p 0 → 0.6 (local
player from `Scope`; others from their ZDO bool `<guid>.Raised` with their own 0.6 s / 0.4 s blend). Target: the
eyepiece 3 cm in front of the right eye (eyes = head bone + look rotation × (0, 0.15, 0.07), the offset first-person
mods use) with the tube along the look (local: `m_eye.forward`; others: `CharacterAnimEvent.m_headLookDir`, which every
game already receives through the vanilla look target). The hand turn is the smallest rotation that brings the tube
onto the look; the arm is a two-bone IK (law of cosines, elbow out and down), blended in local rotations. The animator
runs with the physics (`AnimatePhysics`), so frames without an evaluation keep the last write: the pose remembers the
animated rotations and puts them back before posing again when the bones still hold its own write. Bones from the
humanoid avatar (`RightArm`, `RightForeArm`, `RightHand` at runtime). Measured: eyepiece within 1 mm of its target,
tube along the look (local and remote paths).

### 2.6 Far view (G6) and the Distant Horizons boost (E6)

Decision D6: rely on Distant Horizons. The spyglass writes a `double[]` in the AppDomain slot `MC.ViewBoost.v1` every
frame while up: `[layout 1, frame, zoom, weight, forward xyz, half horizontal fov, half vertical fov]`; no reference
either way, a reading older than 5 frames counts as no boost. Distant Horizons (`ViewBoost`) turns it into a factor per
tile: `min(zoom × weight, SpyglassMaxBoost)` inside the view (half horizontal fov + 4°), fading to 1 over 20° beyond
(XZ angles; no boost when looking almost straight up or down). Terrain: a node splits when `cheb < SplitFactor × h ×
size × factor` and builds earlier (priority ÷ factor). Objects: band, buildings and big rocks use `distance ÷ factor`
(the Mesh band stays within its true distance: farther = cards), and the search area grows by the largest factor. A
recompute is asked when the boost starts or stops, the view turns by a third of its width or the zoom changes 15%,
at most every 0.2 s. Measured: 9 far leaf tiles within 8° of the view before, 37 with x4, 9 after. With factor 1 the
code paths are unchanged.

## 3. Decisions

- **D1 Glass = Crystal.** The game has no glass. Crystal (rock crystal, the Viking-age Visby lens material) over
  Obsidian ("dark volcanic glass", but dark). Both are Mountain materials, so the recipe unlocks after the Mountains
  (discovery needs every material known). Configurable (`RecipeResources`); flagged to the user.
- **D2 Tool, like the hammer and hoe** (user decision, 2026-10-02, after a first version as a one-handed weapon that
  kept the left hand free): the left hand is not free, equipping the spyglass empties both hands and the tooltip says
  two-handed, as for every vanilla tool. A tool without build pieces punches with the fists on a click, so an always-on
  guard skips the attack input while a spyglass is held (E7). Vanilla item lists and MC `ItemKinds` file it with the
  tools by its type; it is not a weapon (no achievement or weapon-mod interaction).
- **D3 Click toggles** (vanilla emote/sit style), Block always lowers; hold mode as an option.
- **D4 Camera owned in a postfix**, never through `SetTempFOV`, `m_fov`, `m_distance` or `m_minDistance` (1.1): nothing
  to restore, and first-person/camera mods keep their own values.
- **D5 Own body hidden** near the eye rather than a near-clip trick (the head, hair and helmet would fill a narrow
  view).
- **D6 No far-zone loading.** Moving the reference point or poking far zones causes world generation on the host,
  ownership and AI hand-over, object destruction around the player and ground unloading (`ZNet.SetReferencePosition`
  every frame, `ZNetScene.RemoveObjects`, `ZoneSystem.UpdateTTL`, `CreateGhostZones`, `ReleaseNearbyZDOS`): not
  seamless. Distant Horizons is used instead, as an optional partner (not `ModRequires`: it is client-only, a
  dedicated server would mark the spyglass inactive and switch it off for everyone).
- **D7 Fog clearing only with Distant Horizons**: without it the land behind the fog is not drawn (1.6), so clearing
  would only show the vanilla terrain's dark fade.
- **D8 Overlay under the HUD** (sort order HUD−1): health, hotbar and messages stay readable; the crosshair alone is
  hidden.
- **D9 The pose runs on every game** from a ZDO bool (owner writes only on change): no RPC, a vanilla player sees an
  empty hand (unknown item) and no pose.

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Where |
|---|---|
| Item, recipe | every game (always-on registration); recipe from the rules in force |
| Raise state, input, camera, overlay, fog, Distant Horizons slot | the looker's own game |
| Arm pose | every game with the mod, for every player holding a spyglass |
| Join check (`PlayerCheck`), rules push (`ServerRules`) | server (dedicated or host) |

### 4.2 RPCs, ZDO keys, network version

RPCs `MC.Exploration.View.Spyglass.Settings` / `.SettingsRequest` (rules layout 1); player ZDO bool
`MC.Exploration.View.Spyglass.Raised` (stable hash); prefab `MC_Spyglass`, recipe `Recipe_MC_Spyglass`. Network
version 1.

### 4.3 Server settings and join check

Copied from Swim Dive (`ServerRules`, `PlayerCheck`, `Patches/ZNetPatches.cs`) with Sneak Ambush's `Changed` event:
the server pushes its rules 0.5 s after the last change to compatible players; a client uses them instead of its own
config for that session and is pending (recipe hidden, no raise) until they arrive; the recipe is rebuilt 0.5 s after
the rules stop changing (`SpyglassContent.RequestRebuild`, driven by the `ZNet.Update` postfix). Players without the
mod, with it off or another network version are refused after a 1 s grace unless `AllowPlayersWithoutMod`.

### 4.4 Hand-off cases

- A dropped spyglass is a ZDO with an unknown prefab for a player without the mod (allowed in): `ZNetScene.CreateObject`
  logs "Missing prefab hash" and makes nothing, so they never see or pick it up; it stays for everyone else (only a
  server destroys such ZDOs: on a server without the mod, dropped spyglasses are deleted).
- In a chest: opening it makes them its owner (`Container.RPC_RequestOpen`); their `Inventory.Load` skips the unknown
  item ("Failed to find item prefab"), and their next change saves the chest without it (`Container.OnContainerChanged`
  → `Save`), so the spyglass is lost for every player. Documented in the README, the multiplayer notes and
  `AllowPlayersWithoutMod`; test M04.
- A player without the mod sees an empty right hand (`VisEquipment.AttachItem` logs "Missing attach item") and no pose.
- The `Raised` flag on the player ZDO is ignored by games without the mod.

### 4.5 Dedicated server

The item is built from the network prefab list's `KnifeFlint` (no main-menu item database). No graphics device: no
icon, mesh, texture or material is made (the object, the eyepiece marker and the collider are). The rules and the join
check run on the server.

## 5. Config

| Section | Key | Default | Range | Scope |
|---|---|---|---|---|
| General | Enabled | true | | own |
| General | AllowPlayersWithoutMod | false | | server only |
| Recipe | RecipeResources | `Bronze:2,Crystal:2` | text | server wins |
| Recipe | RecipeStation | `forge` | text | server wins |
| Recipe | RecipeStationLevel | 1 | 1-10 | server wins |
| Zoom | MaxMagnification | 8 | 2-20 | server wins |
| Zoom | FogClearing | 0.75 | 0-1 | server wins |
| Controls | HoldToLook | false | | personal |
| Controls | StartMagnification | 4 | 1.5-20 | personal |
| Controls | AimSensitivity | 1 | 0.25-2 | personal |
| View | ClearViewSize | 0.7 | 0.3-1 | personal |
| View | EdgeBlur | true | | personal |
| View | EdgeDarkness | 0.85 | 0-1 | personal |

Distant Horizons (its own config, client side): `Spyglass/SpyglassDetail` (true), `Spyglass/SpyglassMaxBoost` (4,
1-8).

## 6. Compatibility

### 6.1 Sibling mods (same release)

- **Distant Horizons**: partner for G6 (2.6); its fog thinning becomes the spyglass's fog base; its far clip is left
  alone.

### 6.2 Other MC mods

- **Swim Dive** patches `GameCamera.UpdateCamera`/`GetCameraPosition` (under-water camera and fog): the spyglass never
  goes up while swimming, and its postfix runs after Swim Dive's.
- **Dual Wielding**: eligibility needs a one-handed melee weapon: never pairs the spyglass; equipping it puts a pair
  away (tool rule).
- **Weapon Moveset**: keys on the attack animation name: empty, ignored.
- **Tower Shield Wall**: equipping the spyglass puts any shield away (vanilla tool rule).
- **Sneak Ambush**: fog bonus read from the environment, not the screen: the spyglass's fog clearing does not change
  stealth.
- **Sort Chest, Crafting Search and Sort**: `ItemKinds` files it under Tool by its item type.
- **Fishing Fight** (`Hud.Update` postfix): independent of the crosshair postfix.

### 6.3 Popular external mods

| Mod | Overlap | Action |
|---|---|---|
| FirstPersonMode (Azumatt), Landoria FirstPerson, ImmersiveFirstPerson, BetterCharacterController | camera position, field of view in `UpdateCamera` | spyglass postfix after them (`HarmonyAfter`), zooms from their field of view |
| Valheim+ camera, Customizable Camera, CameraTweaks | `m_fov`, `m_minDistance`, `m_mouseSens` | untouched by the spyglass |
| Advize's Spyglass | another item writing `m_fov` in an `UpdateCamera` prefix, vignette through the post-processing profile | different item; raising both at once would stack zooms (documented) |

## 7. Implementation plan

### 7.1 Files

`Plugin.cs` (config, life cycle, `FeatureActive`), `SpyglassRules.cs`, `ServerRules.cs`, `PlayerCheck.cs`,
`SpyglassContent.cs`, `SpyglassModel.cs`, `SpyglassIcon.cs`, `Scope.cs`, `ScopeCamera.cs`, `ScopeOverlay.cs`
(`RingMesh`, `BlurRing`, `DarkRing`, `ScopeCapture`), `ScopeHud.cs`, `ArmPose.cs`, `ViewBoost.cs`, `Patches/`
(`ObjectDBPatches`, `ZNetScenePatches` and `PlayerAttackGuardPatches` always on; `PlayerPatches`, `GameCameraPatches`, `HudPatches`,
`ZNetPatches`), `SelfTests.cs`. Distant Horizons: `ViewBoost.cs`, `DHConfig.cs` (Spyglass section),
`Terrain/LodTerrainManager.cs`, `Objects/DistantObjectManager.cs`, `SelfTests.cs`.

### 7.2 Turning off

World exit (logout, disconnect, quit) with the feature still on: `Scope.Abort` from the camera postfix
(`Game.IsShuttingDown`) and from a `GameCamera.OnDestroy` postfix. `OnDeactivated`: `Scope.Shutdown` (abort: camera next frame, body, overlay destroyed, capture component removed, fog,
slot, ZDO flag false, crosshair back), `ArmPose.Shutdown` (every arm back to its animated pose), recipe hidden, join
check and rules stopped. The item stays registered.

## 8. Test plan

In-world self-tests (Debug): `spyglass.network` (wire, clamp, layouts, rule selection, debounce, join verdicts, zoom
maths), `spyglass.item` (registration, data, recipe, tool taking both hands, a torch next replaces it, no punch, hand model,
dropped copy with collider), `spyglass.view` (raise, zoom, camera at the eye, body, overlay, crosshair, ZDO flag, Distant
Horizons slot and fog, feet locked, aim scale, zoom cap, lower restores everything, put away = instant, pending =
no raise), `spyglass.pose` (eyepiece at the eye and tube along the look, local and remote paths), `spyglass.far`
(far view from a shore at x3/x8, clear weather), `spyglass.export` (icons, rig and canvas dumps); Distant Horizons
`horizons.boost`. Hands-on list: `src/Exploration/View.Spyglass/TESTING.md` (G1 T01, G2 T02, G3 T03, G4 T04, G5
T05, G6 T11-T13, G7 T01/T14).

## 9. Open questions and unverified

- Feel of the raise timing (0.6 s) and the default zoom (x4) and clear-view size (0.7): to tune in game.
- Render scale below 100%: the overlay is above the render-scale picture (order 0) by design; untested by hand.
- Vanilla block values of the Flint Knife (kept on the spyglass) were not read (prefab data).
- `ShieldWood` prefab name in TESTING.md is unverified.

### Review (2026-10-02)

Six reviewers (state machine, camera and overlay, arm pose, item, multiplayer, Distant Horizons boost), each finding
checked by two skeptics. Fixed: the overlay could stay over the main menu after a logout while looking (abort on
`Game.IsShuttingDown` and in a `GameCamera.OnDestroy` postfix; lowering on real time so the pause menu does not freeze
it); the right-hand check read `Object.name` every physics tick (reference compares only); the near plane stayed at
0.5 m with the camera at the eye (walls and roofs before the face were cut); the body was forced visible during the
slide where vanilla hid it (vanilla's 2 m rule kept); boosted far trees and buildings stayed on the coarse land after
it refined (terrain keys in their signature); docs: the hand-off loss is a shared chest changed by a player without the
mod, not their inventory (README, notes, `AllowPlayersWithoutMod`, M04); blocking wording; Distant Horizons' `SplitFactor` text no longer promises one level everywhere.

Change request after the review (2026-10-02): the spyglass is a Tool like the hammer, not a one-handed weapon (D2).
The weapon-only parts went away (achievement-list postfix, the shared `ItemKinds` rule for attack-less weapons) and
the always-on attack-input guard came in.
