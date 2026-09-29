# Building & Crafting

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> All class/method/field names below were checked against `.ref/decompiled/assembly_valheim/<Class>.cs`.
> Game code is cited as `Class.Method`; behaviour is paraphrased, not copied. Values marked *(prefab)* live in
> Unity prefab data, not in code: verify them at runtime (see [Appendix A](#appendix-a--runtime-verification-helpers)).
> Prefab/asset names quoted from `valheim_Data/StreamingAssets/SoftRef/manifest_extended` are hints, not guarantees.

---

## Overview

Building and crafting in 1.0 are split across three layers:

| Layer | What it is | Main classes |
|---|---|---|
| **World pieces** | Everything placed with a build tool (hammer, hoe, cultivator, feast table…) | `Player` (placement), `Piece`, `PieceTable`, `WearNTear`, plus one behaviour component per piece type: `Fireplace`, `Sign`, `Container`, `PrivateArea`, `CraftingStation`, `StationExtension`, `Smelter`, `CookingStation`, `Fermenter`, `Incinerator`, `ShieldGenerator`, `ItemStand`, `ArmorStand`… |
| **Recipes & crafting UI** | Turning inventory items into other items, upgrades, repair, and the 1.0 refinement "Forge of Potential" | `Recipe`, `Piece.Requirement`, `ObjectDB`, `InventoryGui` (all crafting logic lives here), `Player` (requirement checks, known recipes) |
| **Item data** | Per-item instance state that crafting reads/writes | `ItemDrop.ItemData` (+ `SharedData`), `Inventory`, `VisEquipment`, `ItemStyle`, `StatusEffect`/`SE_Stats` |

### Multiplayer authority primer (applies to every section)

* Every networked object has a `ZNetView` whose `ZDO` has **one owner** (a peer). Ownership is assigned by the server in `ZDOMan.ReleaseNearbyZDOS` to a *nearby player's client*; a dedicated server normally owns nothing near players. So "the owner" of a chest, torch or furnace is usually *some player's game client*, not the server.
* `ZNetView.InvokeRPC(method, …)` (no target) routes to the **ZDO owner**; `InvokeRPC(ZNetView.Everybody, …)` broadcasts. `ZRoutedRpc.InvokeRoutedRPC` executes immediately in-process when the target is the local peer (so the owner sees its own changes instantly; non-owners read stale ZDO values until the next sync).
* Pattern used by almost every piece: *client* validates & removes items from its own inventory → sends an RPC → *owner* mutates ZDO → everyone reads the ZDO in `Update`/`InvokeRepeating`.
* Consequence for mods: anything that changes **simulation** (fuel burn, smelting, support/wear, damage) must be installed on **every client that can become owner** = everyone. Anything that only changes **local input/UI/inventory** can be client-only.
* Crafting/upgrading/repairing is **100 % local** (`InventoryGui` + the local `Player`'s inventory). No RPC is involved.

---

## 1. Piece placement

### Key classes
* **`Player`** – owns the whole build-mode loop: ghost creation, ray test, snapping, validation, placement, removal, hammer repair, cost consumption. Relevant fields: `m_placementGhost`, `m_placementStatus` (`Player.PlacementStatus` enum), `m_buildPieces` (current `PieceTable`), `m_manualSnapPoint`, `m_placeRotation` (steps of `m_placeRotationDegrees` = 22.5°), `m_maxPlaceDistance` = 5, `m_placeDelay` = 0.4 s, `m_removeDelay` = 0.25 s, `m_placeRayMask`/`m_removeRayMask` (layers Default, static_solid, Default_small, piece, piece_nonsolid, terrain, vehicle).
* **`Piece`** – per-prefab placement rules and costs; runtime creator tracking. Nested `Piece.Requirement` is shared with `Recipe`.
* **`PieceTable`** – the list of prefabs a build tool offers (`ItemDrop.ItemData.SharedData.m_buildPieces`), categories, a 15×6 grid per category, `m_canRemovePieces`, `m_canRemoveFeasts`, `m_skill` (skill raised when building), `m_hideAdvancedMenu`.
* 1.0 build menu UI: `BuildUi`, `BuildUiPieceButton`, `BuildUiTagButton`, `FavoritePieceList`, `RecentPieceList`, `ByUsagePieceList`, `ByMaterialPieceList`; tags come from `Piece.m_usage` (`Piece.UsageTagFlags`: Crafting, Building, Floor, Wall, Roof, Furniture, Lighting, Storage, Food, Meads, Feasts, Defense…).

### Flow
1. `Player.Update` → `Player.UpdatePlacement(takeInput, dt)` every frame while `InPlaceMode()` (`m_buildPieces != null`).
2. Selecting a piece → `Player.SetupPlacementGhost()`: instantiates the prefab with `ZNetView.m_forceDisableInit = true`, strips joints, rigidbodies, lights, audio, particles, `TerrainModifier`, `Demister`, `WispSpawner`, disables `Windmill`, moves every transform to layer `ghost`, enables the `_GhostOnly` child.
3. Each frame `Player.UpdatePlacementGhost(flashGuardStone)`:
   * `Player.PieceRayTest` (camera ray, 50 m, but hit must be within `m_maxPlaceDistance + Piece.m_extraPlacementDistance`).
   * Sequential rule checks that set `m_placementStatus`: station-extension needs a station (`StationExtension.FindClosestStationInRange`) and spacing (`OtherExtensionInRange(m_spaceRequirement)`); `m_blockingPieces/m_blockRadius`; `m_mustConnectTo`; **`hit WearNTear.m_supports == false` → Invalid** (this is why nothing can be built on chests and other non-supporting pieces); water/ground/cultivated/vegetation rules; `m_notOnWood`, `m_notOnTiltingSurface`, `m_inCeilingOnly`, `m_notOnFloor`, teleport-area, dungeon (`GlobalKeys.DungeonBuild`), Deep-North snow rules (`m_allowedInDeepSnow`, `m_requireDeepSnow`, `m_deepSnowBuildHeight`).
   * Positioning: closest collider point, or manual snap point (`TabLeft/TabRight` cycles `m_manualSnapPoint` over the ghost's own snap points).
   * Snapping (skipped while AltPlace is held): `Player.FindClosestSnapPoints(ghost, 0.5f, …)` collects snap points from pieces within 10 m (`Piece.GetSnapPoints(point, radius, …)`; a snap point is **any child transform tagged `snappoint`**), picks the closest pair ≤ 0.5 m, rejects if `IsOverlappingOtherPiece`.
   * Final checks: `Location.IsInsideNoBuildLocation`, `PrivateArea.CheckAccess(pos, wardRadius, flash, wardCheck)`, `CheckPlacementGhostVSPlayers`, `m_onlyInBiome`, `m_noClipping` (`TestGhostClipping`).
   * `SetPlacementGhostValid` tints red via `Piece.SetInvalidPlacementHeightlight`.
4. Click (`Attack`/`JoyPlace`) → `Player.HaveRequirements(piece, RequirementMode.CanBuild)` → `Player.TryPlacePiece(piece)` (re-runs `UpdatePlacementGhost(true)`, maps status → message) → `Player.PlacePiece(piece, pos, rot, doAttack, cheated)`.
5. `Player.PlacePiece`: `Object.Instantiate(prefab)` (the new ZDO is owned by the placer), `AddKnownStation` if it contains a `CraftingStation`, `Piece.SetCreator(playerID, platformID)`, `PrivateArea.Setup(name)`, `WearNTear.OnPlaced()`, `ItemDrop.MakePiece(true)` (feasts/placeable items), every `IPlaced.OnPlaced()` (e.g. `Piece.OnPlaced` for harvest pieces), place effects, stats, sets `ZDOVars.s_cheated` if needed.
6. Back in `UpdatePlacement`: `ConsumeResources(piece.m_resources, 0)` unless `ZoneSystem.GetGlobalKey(piece.FreeBuildKey())` (`NoBuildCost`, or `NoCraftCost` for item/feast pieces); stamina `GetBuildStamina()`; skill raise with "remove debt" anti-farm (`m_buildRemoveDebt`); tool durability via `GetPlaceDurability` (reduced by `m_placementDurabilitySkill`).
7. Removal: `Player.RemovePiece()` → checks `m_canBeRemoved`, no-build location, `PrivateArea.CheckAccess`, `CheckCanRemovePiece` (station in range unless `NoWorkbench` key), `Piece.CanBeRemoved()` (only `Container` and `Ship` veto) → `IRemoved.OnRemoved()` → `WearNTear.Remove()` (RPC to owner) which drops resources via `Piece.DropResources` (full `m_amount` for player-built, `/3` for world-generated pieces).
8. Hammer repair: `Player.Repair(tool, repairPiece)` → `WearNTear.Repair()` → `RPC_Repair` on owner.
9. Copy piece: `Player.CopyPiece()` (AltPlace + Remove).
10. Discovery: `Player.UpdateKnownRecipesList` → piece is known when `HaveRequirements(piece, RequirementMode.IsKnown)` (station known + all materials known); `PieceTable.UpdateAvailable` builds the per-category lists.

### Data & persistence
* `Piece` ZDO: `ZDOVars.s_creator` (long player id), `s_creatorIndex` (index into `ZNet.World.m_playerHistory`), `s_cheated`.
* Everything else is prefab data: `m_resources`, `m_craftingStation` (build needs `CraftingStation.HaveBuildStationInRange(name, pos)`), `m_comfort`/`m_comfortGroup`, placement flags (see above), `m_category`, `m_usage`, `m_harvest*`.
* Known pieces are stored by `Piece.m_name` in `Player.m_knownRecipes` (same set as item recipes).

### Multiplayer authority
* Validation and placement are **client-side** (placer). The placer owns the new ZDO initially; later ownership migrates to whoever is nearby.
* No server validation exists — a client-only mod can loosen placement rules, but everything after placement (support, wear, damage) is simulated by the current owner.

### Patch points
| Goal | Patch | Why |
|---|---|---|
| Relax/add a placement rule | `Player.UpdatePlacementGhost` postfix (inspect `m_placementGhost`, overwrite `m_placementStatus`, call `SetPlacementGhostValid`) or transpiler on a specific check | Single place where the status is decided; `TryPlacePiece` re-runs it, so a postfix also governs actual placement |
| React to a placed piece | `Player.PlacePiece` postfix or implement `IPlaced` on a component added to the prefab | Called once per placement on the placer's client |
| Change cost | `Player.ConsumeResources` / `Player.HaveRequirements(Piece, RequirementMode)` | Shared by build and craft paths — filter by caller |
| Add snap points | add child `GameObject` tagged `snappoint` to a prefab at `ZNetScene.Awake` | Snapping is purely data-driven |
| Build menu UX | `PieceTable.UpdateAvailable`, `BuildUi` classes | 1.0 menu uses tags/favourites/recent |

---

## 2. WearNTear & structural stability

### Key classes
* **`WearNTear`** – health, support, weather/biome wear, destruction and fragments for pieces. Updated in batches by **`WearNTearUpdater`** (50 instances/frame).

### Flow
* `WearNTear.UpdateWear(time)` (owner only, after 30 s grace or `OnPlaced`): rain wear (5 % per 60 s when wet & no roof & health > 50 %, `m_noRoofWear`), support (`UpdateSupport`; no support → 100 % damage), persistent-event damage (`m_requiredPersistentEvent`), Deep-North snow load (`m_snowBuildup`, `Game.m_snowDamage` when support colour value < `Game.m_snowSupportLevel`), Ashlands ash/lava damage, wrong-biome damage (`m_requiredBiome`), `ShieldGenerator.IsInsideShieldCached` suppresses rain/ash. `GlobalKeys.NoBuildingFall` disables all of it.
* `WearNTear.UpdateSupport`: overlap boxes around each collider (+0.3 m). Touching **terrain** or **any collider without a `WearNTear`** ⇒ full support. Touching another `WearNTear` counts **only if its `m_supports` is true**. Support decays with distance to the neighbour's COM using horizontal/vertical loss per material (`GetMaterialProperties`). Cached per neighbour; `RPC_ClearCachedSupport` invalidates neighbours on placement/destruction.
* Material table (`WearNTear.GetMaterialProperties`, max / min support / vertical loss / horizontal loss): Wood 100/10/0.125/0.2 · HardWood 140/10/0.1/0.167 · Stone 1000/100/0.125/1 · Iron 1500/20/0.077/0.077 · Marble 1500/100/0.125/0.5 · Ashstone 2000/100/0.1/0.333 · Ancient 5000/100/0.067/0.25 · Ice 1000/100/0.125/0.333 · Timberwood 200/10/0.077/0.2.
* Damage: `WearNTear.Damage(hit)` → `RPC_Damage` on owner → resistances `m_damages`, `PrivateArea.OnObjectDamaged` (ward flash; ward does **not** block damage), tool tier `m_minToolTier`, `ApplyDamage` → `Destroy` (drops resources, `m_onDestroyed` callbacks used by Container/Smelter to spill contents, fragments RPC).
* World level scales piece HP (`Game.m_worldLevelPieceHPMultiplier`).

### Data & persistence
ZDO: `s_health`, `s_support`, `s_snow`, `s_preSnow`. RPCs: `RPC_Damage`, `RPC_Remove`, `RPC_Repair`, `RPC_HealthChanged`, `RPC_ClearCachedSupport`, `RPC_SetSnow`, `RPC_CreateFragments`.

### Multiplayer authority
Owner computes wear/support and applies damage. A non-modded owner will apply vanilla rules regardless of what other clients think.

### Patch points
* `WearNTear.GetMaterialProperties` (postfix) – global stability tuning.
* `WearNTear.UpdateSupport` (transpiler on the `m_supports` test) – let specific pieces lean on non-supporting ones (e.g. signs on chests).
* `WearNTear.RPC_Damage` (prefix, owner side) – protection rules (ward revamp).
* `WearNTear.m_supports`, `m_noSupportWear`, `m_noRoofWear`, `m_burnable` – prefab-level toggles, safest place to change behaviour.

---

## 3. Fireplace (campfires, hearths, torches, sconces, braziers, lamps)

### Key classes
* **`Fireplace`** (implements `Hoverable`, `Interactable`, `IHasHoverMenu` for the 1.0 radial "use item" menu). Every standing torch/sconce/brazier/hearth is a `Fireplace` with different prefab flags.

### Fields (defaults in code; per-prefab values differ)
`m_startFuel` 3, `m_maxFuel` 10, `m_secPerFuel` 3, **`m_infiniteFuel`**, **`m_canTurnOff`**, **`m_canRefill`** (true), `m_holdRepeatInterval` 0.2 (holding E keeps adding fuel), `m_halfThreshold`, `m_lowWetOverHalf`, `m_disableCoverCheck`, `m_fuelItem`, visuals (`m_enabledObject`, `m_enabledObjectLow/High`, `m_full/half/emptyObject`, `m_playerBaseObject`), fireworks list, ignite settings (`m_igniteInterval/Chance/Spread`, `m_firePrefab` – Ashlands cinder spread), `m_snowMelter` (Deep North).

### Flow
* `Fireplace.Awake`: owner initialises `s_fuel = m_startFuel` if absent; registers RPCs; `InvokeRepeating(UpdateFireplace, 0, 2)`, `CheckEnv` every 4 s.
* `Fireplace.UpdateFireplace`: **owner only** subtracts `elapsed / m_secPerFuel` from `s_fuel` when `IsBurning() && !m_infiniteFuel && state == 1`. Time comes from `s_lastTime` (offline catch-up).
* `Fireplace.IsBurning()`: false if blocked (`CheckUnderTerrain`: terrain/solid above or smoke blocked), `s_state != 1`, or under water; otherwise true if fuel > 0 **or `m_infiniteFuel`**.
* `Fireplace.Interact(user, hold, alt)`: claims ownership if unowned; **if `m_canTurnOff && !hold && !alt && fuel > 0` → `RPC_ToggleOn`** (so a turn-off-able piece with 0 fuel cannot be toggled); else if `m_canRefill` (and not infinite) removes 1 `m_fuelItem` and sends `RPC_AddFuel`. Its hold gate compares against `m_lastUseTime`, **which vanilla never assigns**: hold repeats are only limited by `Player.Interact`'s own 0.2 s gate.
* Owner `RPC_AddFuel` **does nothing when `CeilToInt(fuel) >= m_maxFuel`**: the item the sender already removed is lost (a non-owner reading a stale fuel value can waste wood this way).
* `Fireplace.UseItem`: fuel item or firework items.
* `Fireplace.UpdateState`: visuals; **if `m_canTurnOff` and it is wet (rain/wind without cover, only computed for prefabs having both Low/High objects) the owner auto-toggles it off**.
* `Fireplace.GetHoverText` returns **empty string when `m_infiniteFuel`**.
* `Fireplace.CanBeRemoved` exists but nothing calls it (dead code in 1.0.16).
* Radial menu: `Fireplace.TryGetItems` / `CanUseItems` return false for infinite-fuel fires.
* Prefab data (1.0.16, fire records read from the game's main asset bundle; the Batch Station Feeding coverage dump
  lists every prefab in game): the **only fire with `m_canTurnOff`** is the Resin Candle (3 fuel), and it has
  `m_canRefill` false: in vanilla no fire both takes fuel with E and toggles. The Wood fires hold 10 or 20 fuel, three
  fires on other fuels 5, the torches, Sconce, Jack-o-turnip and Snow Lantern 6, and the two Standing Wood Torch
  records 4 and 1 (which prefab each belongs to is unverified). Every fire record uses `m_holdRepeatInterval` 0.2 (the
  code default). The Hot Tub is **not** a `Fireplace` (section 11).

### Data & persistence
ZDO: `s_fuel` (float), `s_state` (int: 1 = on, 2 = off; default 1), `s_lastTime` (ticks). RPCs: `RPC_AddFuel`, `RPC_AddFuelAmount(float)`, `RPC_SetFuelAmount(float)`, `RPC_ToggleOn`. Public helpers: `AddFuel(float)`, `SetFuel(float)`.

### Multiplayer authority
Owner burns fuel and handles toggles; every client computes `IsBurning()` locally from ZDO + its own prefab flags (so a client with modified flags *displays* differently even if the owner is vanilla).

### Patch points
* `ZNetScene.Awake` postfix: edit `Fireplace` flags on prefabs (persistent, no per-frame cost).
* `Fireplace.Interact` prefix (toggle rules), `Fireplace.GetHoverText` postfix, `Fireplace.UpdateFireplace` prefix (fuel economy), `Fireplace.UpdateState` (wet auto-off).

---

## 4. Sign

### Key classes
* **`Sign`** (`Hoverable`, `Interactable`, `TextReceiver`). Text rendered by a `TextMeshProUGUI m_textWidget` child.

### Flow
* `Sign.Interact` → ward check (`PrivateArea.CheckAccess`) → UGC privilege checks (`PlatformManager…CheckPrivilege(ViewUserGeneratedContent)`) → `TextInput.instance.RequestText(this, "$piece_sign_input", m_characterLimit)` (default 50).
* `Sign.SetText(text)`: ward check → **`m_nview.ClaimOwnership()`** → writes `s_text`, `s_author` (platform id or "host"), `s_authorDisplayName`.
* `Sign.UpdateText` (every 2 s, only when `ZDO.DataRevision` changes) → permission/mute/censor (`RelationsManager.CheckPermissionAsync`, `CensorShittyWords.Filter`) → widget text.

### Data & persistence
ZDO: `s_text`, `s_author`, `s_authorDisplayName`. Rich-text tags are stripped only for hover text.

### Multiplayer authority
Writer claims ownership then writes the ZDO directly (no RPC). Everyone renders from the ZDO.

### Patch points
`Sign.SetText` (validation/formatting), `Sign.UpdateText` (render extras such as icons), `Sign.GetHoverText`. For placement onto other pieces see §1 (`UpdatePlacementGhost`) and §2 (`m_supports`).

---

## 5. Container (chests, carts, tombstones, loot containers)

### Key classes
* **`Container`** (`Hoverable`, `Interactable`) wraps an `Inventory`. Also used by `Vagon` (cart, `m_wagon`), `TombStone`, `Incinerator` (obliterator input), `m_destroyedLootPrefab` spill containers.

### Fields
`m_name`, `m_width`/`m_height`, `m_privacy` (`Private` = creator only, `Public`; **`Group` exists but always denies**), `m_checkGuardStone`, `m_autoDestroyEmpty`, `m_defaultItems` (`DropTable`, added once, flag `s_addedDefaultItems`), `m_open`/`m_closed` visuals, `m_rootObjectOverride`, `m_discoverStat`.

### Flow
* `Container.Interact` → ward (if `m_checkGuardStone`) → `CheckAccess(playerID)` → `RPC_RequestOpen(playerID)` to owner.
* Owner `RPC_RequestOpen`: refuse if in use by someone else / cart in use / no access; otherwise `ZDOMan.ForceSendZDO`, **`ZDO.SetOwner(requester)`** and reply `RPC_OpenResponse(true)` → requester's `InventoryGui.Show(container)`.
* While open, the owner (= the viewer) edits its local `Inventory`; `Inventory.m_onChanged` → `Container.Save()` writes the full inventory as a byte array to `s_items`. `CheckForChanges` (1 s) reloads on other clients when `DataRevision` changes (not while in use).
* Stack-all: `Container.StackAll` → `RPC_RequestStack` → ownership transfer → `Inventory.StackAll` on the requester. Take-all: `RPC_RequestTakeAll` (2 s cooldown) → requester claims ownership and `MoveAll`.
* Destroyed (`WearNTear.m_onDestroyed` / `Destructible.m_onDestroyed`): owner drops items or moves them into `m_destroyedLootPrefab` containers.
* Oddity: `Container.Interact` invokes an RPC named `"discovered"` while `Awake` registers `"RPC_Discovered"`, so the per-player discovered flag is never set.

### Data & persistence
ZDO: `s_items` (byte[] = `Inventory.Save`, includes each item's `m_customData`), `s_inUse` (int), `s_addedDefaultItems`, creator via `Piece`.

### Multiplayer authority
**Ownership transfer on open** — the opener becomes owner, so inventory mutations are local to the viewer. This is why chest-sorting/search mods can be client-only.

### Patch points
`Container.Interact` (alt-interaction hooks, e.g. rename/label), `Container.GetHoverText`, `Container.CheckAccess` (private; group/ward permissions), `Container.RPC_RequestOpen`, `InventoryGui.Show(Container, int)`.

---

## 6. Ward (PrivateArea)

### Key classes
* **`PrivateArea`** – player ward ("Guard stone") and also NPC faction areas (`m_ownerFaction` ≠ Players, e.g. Dvergr/Fuling camps that alert AI via `MonsterAI.OnPrivateAreaAttacked`).

### Fields
`m_radius` (code default 10, *(prefab)* larger for the player ward), `m_enabledByDefault`, `m_ownerFaction`, effects (`m_flashEffect`, `m_activateEffect`, …), `m_areaMarker`.

### Flow
* `PrivateArea.Interact`: creator → `ToggleEnabled`; others (only when disabled) → `TogglePermitted(playerID, name)`.
* `PrivateArea.CheckAccess(point, radius, flash, wardCheck)` (**static**, evaluated on the *acting* client): denies if any enabled ward covering the point lacks `HaveLocalAccess()` (creator or permitted) — with `wardCheck` (placing a ward) every overlapping ward must grant access. Flashes denying wards.
* Callers (full list in 1.0.16): `Player.RemovePiece`, `Player.Repair`, `Player.UpdatePlacementGhost`, `Attack.SpawnOnHitTerrain` (hoe/pickaxe terrain ops), `Container` (if `m_checkGuardStone`), `Door` (if `m_checkGuardStone`), `Sign`, `ItemStand`, `ArmorStand`, `Beehive`, `Fermenter`, `Incinerator`, `MapTable`, `SapCollector`, `TeleportWorld`, `Turret`, `Trap`. **Not** called by `Pickable`, `Plant`, `CookingStation`, `Smelter`, `Fireplace`, `Tameable`, or any damage path.
* `PrivateArea.OnObjectDamaged` (from `WearNTear.RPC_Damage`, `Destructible`, `MineRock5`) only flashes the shield / alerts NPC factions.

### Data & persistence
ZDO: `s_enabled` (bool), `s_permitted` (count) + dynamic keys `"pu_id"+i` (long) and `"pu_name"+i` (string), `s_creatorName`. RPCs: `ToggleEnabled(long)`, `TogglePermitted(long,string)`, `FlashShield`.

### Multiplayer authority
Permission list mutated by the owner; **access checks are purely client-side**. There is no server enforcement; unmodded or modified clients can ignore wards for anything the check guards.

### Patch points
`PrivateArea.CheckAccess` (central rule), `PrivateArea.HaveLocalAccess`, `PrivateArea.Interact`/`GetHoverText` (UI), `WearNTear.RPC_Damage` (owner-side damage blocking), missing call sites (`Pickable.Interact`, `CookingStation`, `Smelter` switches, `Fireplace.Interact`).
Related 1.0 system: **`ShieldGenerator`** (fuelled dome; `IsInsideShield`, `IsInsideShieldCached`; intercepts projectiles via `ShieldGenerator.CheckProjectile` from `Projectile`, suppresses weather/ash wear in `WearNTear.UpdateWear`, blocks cinders, and lets non-tolerant plants survive Ashlands heat / Mountain & Deep North cold in `Plant.UpdateHealth`).

---

## 7. CraftingStation & station extensions

### Key classes
* **`CraftingStation`** (`Hoverable`, `Interactable`, `IMonoUpdater`) – workbench, forge, stonecutter, artisan table, black forge, galdr table, cauldron, **Forge of Potential** (`m_upgrader`), etc.
  * Prefab names (1.0.16 game data manifest, checked for Crafting Search and Sort): `piece_workbench`, `forge`, `blackforge`, `piece_magetable` (Galdr Table), `piece_artisanstation`, `piece_cauldron`, `piece_MeadCauldron` (Mead Ketill), `piece_preptable` (Food Preparation Table), `piece_stonecutter`, `UpgradeStation` (Forge of Potential piece). The matching localization keys (`$piece_workbench`, `$piece_forge`, `$piece_blackforge`, `$piece_magetable`, `$piece_artisanstation`, `$piece_cauldron`, `$piece_meadcauldron`, `$piece_preptable`, `$piece_stonecutter`, `$piece_upgradestation`) exist; that they are the stations' `m_name` is assumed *(prefab)*. All of them open the same `InventoryGui` crafting panel (see [ux.md §3](ux.md)).
* **`StationExtension`** – chopping block, anvils, tanning rack, etc.; raises station level.

### Fields
`m_name` (localisation key, used as identity everywhere), `m_discoverRange` 4, `m_rangeBuild` 10, `m_extraRangePerLevel`, `m_craftRequireRoof`, `m_craftRequireFire`, `m_showBasicRecipies`, `m_useDistance` 2, `m_craftingSkill` (default Crafting), `m_hasCraftTab`, `m_canRepair`, **`m_upgrader`**, effect lists (`m_craftItemEffects`, `m_craftItemDoneEffects`, `m_craftItemDoneFailEffects`, `m_repairItemDoneEffects`).
`StationExtension`: `m_craftingStation` (prefab ref, matched by `m_name`), `m_maxStationDistance` 5, `m_stack` (allow duplicates), connection VFX.

### Flow
* `CraftingStation.Interact` → `InUseDistance` → `CheckUsable(player, true)` (roof: `Cover.GetCoverForPoint` must be under roof with ≥ 70 % cover; fire: `m_haveFire` from `CheckFire`, 1 s, `EffectArea.IsPointPlus025InsideBurningArea`) → `Player.SetCraftingStation(this)` → `InventoryGui.instance.Show(null, 3)`.
* `Player.UpdateStations`: discovers stations within `m_discoverRange` every 1 s (`CraftingStation.UpdateKnownStationsInRange` → `Player.AddKnownStation`, which stores the **max level seen per station name** and refreshes known recipes); closes the UI when walking away.
* Level: `GetLevel()` = 1 + extension count; `GetExtensions()` re-scans every 2 s via `StationExtension.FindExtensions` (same `m_name`, within `m_maxStationDistance`, unique extension names unless `m_stack`). Build range = `m_rangeBuild + extensions × m_extraRangePerLevel`.
* Build-range queries: `CraftingStation.HaveBuildStationInRange(name, point)` (XZ distance), `FindStationsInRange`, `FindClosestStationInRange`.

### Data & persistence
No station-specific ZDO state (only `s_cheated`). Level and range are derived at runtime from nearby extensions. Known stations/levels are in the player save (`Player.m_knownStations`).

### Multiplayer authority
All local (the crafting player).

### Patch points
`CraftingStation.CheckUsable` (roof/fire rules), `CraftingStation.GetLevel`/`GetExtentionCount`, `CraftingStation.GetStationBuildRange`, `StationExtension.FindExtensions`, `Player.SetCraftingStation`.

---

## 8. Recipe & requirement model

### Key classes
* **`Recipe`** (ScriptableObject, stored in `ObjectDB.m_recipes`): `m_item` (result `ItemDrop`), `m_amount`, `m_enabled`, `m_listSortWeight`, `m_noCraftOnlyUpgrade`, `m_craftingStation`, `m_repairStation`, `m_minStationLevel`, `m_requireOnlyOneIngredient` (+ `m_qualityResultAmountMultiplier`), `m_resources`.
* **`Piece.Requirement`**: `m_resItem`, `m_amount`, `m_extraAmountOnlyOneIngredient`, `m_amountPerLevel`, **`m_upgraderResource`**, `m_recover`.
* **`ObjectDB`**: `m_items`, `m_recipes`, `m_StatusEffects`, lookup by stable hash of prefab name or by `SharedData`. `ObjectDB.GetRecipe(item)` returns the **first** recipe whose result has the same `m_shared.m_name`.

### Rules (all verified in code)
* `Recipe.GetRequiredStationLevel(q)` = `max(1, m_minStationLevel) + (q − 1)`.
* `Recipe.GetRequiredStation(q)`: `m_craftingStation`, else (q > 1) `m_repairStation`.
* `Piece.Requirement.GetAmount(q)`: q ≤ 1 → `m_amount`; otherwise `floor(f(q) × m_amountPerLevel + (m_upgraderResource ? m_amount : 0))` with f(q) = q − 1 for q < 4 and 4 + (q − 4)/2 for q ≥ 4.
* `Player.RequiredCraftingStation(recipe, q, checkLevel)`: at an **upgrader station always true**; otherwise the required station name must match the current station and level must be sufficient; recipes without a station are hidden at stations that don't `m_showBasicRecipies`.
* `Player.HaveRequirementItems` / `ConsumeResources` / `GetFirstRequiredItem`: at an upgrader station only requirements with `m_upgraderResource == true` count; elsewhere they are ignored. For `m_requireOnlyOneIngredient` any single resource satisfies the recipe and higher-quality ingredients yield more output.
* Recipe discovery: `Player.UpdateKnownRecipesList` adds a recipe when `HaveRequirements(recipe, discover: true, 0)` (station known at `m_minStationLevel` + all materials known, `Player.m_knownMaterial`). `Player.GetAvailableRecipes` then filters by known + station (+ `s_FilterCraft`, + `GlobalKeys.AllRecipesUnlocked`). Seasonal recipes via `SeasonalItemGroup`.

### Patch points
* Add/modify recipes: `ObjectDB.Awake` **and** `ObjectDB.CopyOtherDB` postfix (the main-menu `FejdStartup.SetupObjectDB` copies the prefab DB; the in-game `ObjectDB` awakes separately), or Jotunn `ItemManager`.
* Dynamic costs: `Piece.Requirement.GetAmount` postfix (one hook that feeds UI, check and consume).
* Availability: `Player.GetAvailableRecipes`, `Player.RequiredCraftingStation`.

---

## 9. InventoryGui crafting, upgrade and repair flow

All crafting logic is in **`InventoryGui`** (client UI singleton).

### Tuning constants (serialized defaults)
`m_craftDuration` 2 s, `m_multiCraftAmount` 5, `m_multiCraftDuration` 6 s (hold AltPlace/`JoyLStick` = craft ×5), `m_upgraderDuration` 8 s + `m_upgraderDurationPerLevel` 1 s × target quality, `m_craftDurationSkillMaxDecrease` 0.6 (duration × (1 − skillFactor × 0.6) using the station's `m_craftingSkill`), `m_craftBonusChance` 0.25 × skillFactor per crafted unit, `m_craftBonusAmount` 1 (bonus only for stackable results).

### Flow
1. `UpdateCraftingPanel` → tabs: no station ⇒ craft only; station with `!m_hasCraftTab` ⇒ upgrade only (Forge of Potential); `Player.GetAvailableRecipes` → `UpdateRecipeList`.
2. `UpdateRecipeList`: craft tab lists recipes (`!m_noCraftOnlyUpgrade`); upgrade tab lists, for each recipe with `m_maxQuality > 1`, **every matching inventory item** (normal stations skip max-quality items; the upgrader lists all items whose recipe has an upgrader resource). Sort via player unique key `"sortcraft"` (`SortMethod`), applied **after** the rows are built and followed by a repositioning of every row, so list reorders belong in a postfix. Search and sort UI: [Crafting Search and Sort](../../src/UX/Crafting.SearchSort) ([design](../design/ux-crafting-search-sort.md)); details in [ux.md §3](ux.md).
3. `UpdateRecipe(player, dt)` (every frame): description via `ItemDrop.ItemData.GetTooltip(item, q, crafting: true, worldLevel, stack)`, variant button (only craft tab, `m_variants > 1`), `SetupRequirementList`, station level indicator, **`m_craftButton.interactable`** decision and tooltip; runs the craft timer and calls `DoCrafting` when done.
4. `OnCraftPressed`: snapshots `m_craftRecipe`, `m_craftUpgradeItem`, `m_craftVariant`, `m_multiCrafting`; inventory space checks (upgrader requires `recoverable resources + 1` empty slots); starts the timer.
5. **`DoCrafting(player)`** – the core rule:
   * recheck requirements (`Player.HaveRequirements`), item still in inventory;
   * bonus rolls; DLC check;
   * **upgrade path removes the old item** (`UnequipItem` + `Inventory.RemoveItem`) and creates a **new** one with `Inventory.AddItem(prefabName, amount, quality, variant, crafterID, crafterName, gridPos, cheated)` — the new item gets **fresh `m_customData` (empty), full durability, the upgrader's crafter id/name and the current `Game.m_worldLevel`**;
   * upgrader branch (see §10);
   * consume resources (`Player.ConsumeResources` or the single ingredient), `UpdateCraftingPanel`, raise the recipe station's skill, effects, stats (`PlayerStatType.Craft*`, `Upgrades`).
6. Repair: `OnRepairPressed` → **`RepairOneItem()`**: iterates `Inventory.GetWornItems` (all inventory items with durability below max), repairs the **first** item where `CanRepair(item)` is true, raises Crafting skill by the missing fraction, shows one message and **returns** — hence "one item per click". `CanRepair`: `m_canBeReparied`, recipe exists, recipe `m_repairStation`/`m_craftingStation` name equals the current station (or `item.m_worldLevel < Game.m_worldLevel`), and `min(station level, 4) ≥ recipe.m_minStationLevel`. `UpdateRepair` shows/animates the button (`HaveRepairableItems`) every frame; it hides it when the station has `m_canRepair == false`.
   * **nocost** (`Player.NoCostCheat()`, console `nocost`): `UpdateRepair` shows the button with **no station** (plain inventory), `RepairOneItem` runs without a station (no effects), `CheckUsable` skips roof/fire, and `CanRepair` returns true for **every** `m_canBeReparied` item before any station check (so at a workbench it also repairs forge gear). The `NoCraftCost` world modifier does not affect repair.
   * `RepairOneItem` and `HaveRepairableItems` both require `CraftingStation.CheckUsable(player, false)` (roof, fire). Both refill the shared `m_tempWornItems` buffer: never iterate it while calling them.
   * When nothing matches, `RepairOneItem` shows the hardcoded English Center message "No more item to repair". Per repair it plays the station's `m_repairItemDoneEffects` and a Center `$msg_repaired` ("Repaired $1").
   * The only caller of `RepairOneItem` is `OnRepairPressed` (button listener; gamepad and UI mods go through it too). The console cheat `repairall` is separate: it maxes every worn item with no station, skill or effects.
   * Implemented as a mod: [One Click Repair All](../../src/Crafting/Repair.OneClickAll) ([design](../design/crafting-repair-one-click-all.md)).
7. Variants: `OnShowVariantSelection` → `VariantDialog.Setup(item)` (one button per `m_icons[i]`, `m_variants` count) → `OnVariantSelected(index)` sets `m_selectedVariant`.

### Multiplayer authority
100 % local. The only network effect is the resulting items in the player inventory (saved in the character file).

### Patch points
| Goal | Patch |
|---|---|
| Change the craft result / upgrade semantics | `InventoryGui.DoCrafting` (prefix for a full takeover of a branch, transpiler for surgical edits) |
| Button enable/disable, custom tooltip | `InventoryGui.UpdateRecipe` postfix (`m_craftButton`, `UITooltip`) |
| Pre-craft validation | `InventoryGui.OnCraftPressed` prefix |
| Repair behaviour | `InventoryGui.RepairOneItem` / `OnRepairPressed` / `CanRepair` |
| Recipe list content/sorting | `InventoryGui.UpdateRecipeList`, `AddRecipeToList` |
| Preserve per-item data across upgrades | `InventoryGui.DoCrafting` (capture `m_craftUpgradeItem.m_customData` before, re-apply to the returned `ItemData`) |

Oddity: in the bonus loop `DoCrafting` adds the *cumulative* bonus each time (`num4` accumulates and is re-added), so multi-craft bonuses grow faster than 1 per success.

---

## 10. Forge of Potential (item refinement / "upgrader")

1.0 adds a refinement station that pushes items **beyond** their normal `m_maxQuality`, with success / downgrade / break outcomes.

### Identification
* Station: a `CraftingStation` with **`m_upgrader = true`**, name `$piece_upgradestation` = "Forge of Potential" (localisation). It is a world location (game data: the Mountains location `AncientUpgradeStation` in `SoftRef/manifest`, the piece prefab `UpgradeStation` and the prop `RuneStone_UpgradeStation` in `manifest_extended`; lore stone `lore_upgradestation_*`). Probably `m_hasCraftTab = false` *(prefab)*.
* Catalyst: **Idol** items (`$item_upgrader_name` "Idol"), two families — `$item_upgrader_weapon` "Battle" and `$item_upgrader_armor` "Protection" — in 8 tiers `$item_upgrader_tier0..7` (Wooden, Bronze, Iron, Silver, Black Metal, Black Marble, Flametal, Bloodgold). Models `UpgradeStatuetteWeapon`/`UpgradeStatuetteArmor`. Item prefabs (game data manifest, `Items/Upgrades/`): `Upgrader0Weapon` … `Upgrader7Weapon` and `Upgrader0Armor` … `Upgrader7Armor`.
* Each weapon/armor `Recipe` that can be refined carries an extra `Piece.Requirement` with `m_upgraderResource = true` pointing at the matching idol tier *(prefab)*.

### Per-idol tuning (`ItemDrop.ItemData.SharedData`, header "Upgrader (Refinement Forge)")
`m_upgradeChance` 0.65, `m_breakChance` 0.1, `m_successUpgradeSteps` 1 (**declared but never read in 1.0.16**), `m_breakReturnIngreientsAmount` 0.5. Per-tier values are prefab data.

### Rule (paraphrase of `InventoryGui.DoCrafting`, upgrader branch)
```
idol = first requirement with m_upgraderResource;  r = Random(0..1)
if (idol.upgradeChance >= r)        -> new item at quality q+1           ("$msg_upgrader_success")
else if (idol.breakChance >= 1 - r) -> item destroyed; return ceil((GetAmount(1)+GetAmount(q-1)) * breakReturn) of each m_recover resource
else                                -> new item at quality q-1           ("$msg_upgrader_failed")
resources (idols only, because of the upgrader filter) are consumed in all three cases
```
* Idol cost per attempt = `Requirement.GetAmount(targetQuality)` (upgrader resources add their `m_amount` on top of the per-level scaling).
* Duration 8 s + 1 s × target quality, reduced by the station's crafting skill.
* The original item is removed **before** the roll; success/fail re-create it through `Inventory.AddItem` (loses `m_customData`, resets durability/crafter/world level). UI warning `$inventory_upgraderwarning` "Maximum safe quality. Refinement may break your item."
* Items of any quality with an upgrader-flagged recipe are listed; a failure on a quality-1 item would produce quality 0 — guard against it.
* **Vanilla bug**: the failure message key is `$msg_upgrader_failed` but the localisation file only contains `msg_upgrader_fail`, so failures show a raw key.

### Multiplayer authority
Local (crafting client). Idols are vanilla items.

### Patch points
`InventoryGui.DoCrafting` (outcome), `InventoryGui.OnCraftPressed` (slot check), `InventoryGui.UpdateRecipe` (button text `$inventory_upgraderbutton`, warning, tooltip), `InventoryGui.UpdateRecipeList` (which items are listed), `Piece.Requirement.GetAmount` (idol cost/tiering), `Player.RequiredCraftingStation`.

---

## 11. Smelter family (smelter, blast furnace, charcoal kiln, windmill, spinning wheel, eitr refinery…)

### Key classes
* **`Smelter`** (`IHasHoverMenuExtended`) – generic "queue input → timed output" machine. Interaction goes through child **`Switch`** components: `m_addOreSwitch`, `m_addWoodSwitch` (fuel), `m_emptyOreSwitch`.
* **`Switch`** (`Interactable`, `Hoverable`) – `m_onUse(Switch, Humanoid, ItemData)` delegate, `m_onHover`, `m_holdRepeatInterval` (default −1 = no hold-repeat; *(prefab)* may enable it). `Switch.Interact(character, hold, alt)` **ignores `alt`** and calls `m_onUse(this, character, null)`; `Switch.UseItem` passes the hot-bar item.
* **`Windmill`** – `GetPowerOutput()` = wind intensity step × (1 − cover); `Smelter` multiplies its tick by it.

### Fields (code defaults, overridden per prefab)
`m_maxOre` 10, `m_maxFuel` 10 (0 = no fuel, e.g. kiln/windmill/spinning wheel), `m_fuelPerProduct` 4, `m_secPerProduct` 10, `m_spawnStack` (accumulate output into stacks), `m_requiresRoof`, `m_fuelItem`, `m_conversion` (`ItemConversion {m_from, m_to}`; a null `m_from` = "no source" conversion), `m_windmill`, `m_smokeSpawner` (blocked smoke stops production), `m_addOreAnimationDuration`.

### Flow
* Add ore: `Smelter.OnAddOre(sw, user, item)` — item = hot-bar item or `FindCookableItem(inventory)` (first conversion source found in the inventory); checks `IsItemAllowed`, **`GetQueueSize() >= m_maxOre`** (read from the local ZDO copy); removes **one** item; `InvokeRPC("RPC_AddOre", prefabName, cheated)`.
* Owner `RPC_AddOre` → `IsItemAllowed` → `QueueOre` (appends `"item"+n`, increments `s_queued`) — **no capacity check on the owner**.
* Add fuel: `Smelter.OnAddFuel` checks `GetFuel() > m_maxFuel − 1`, removes one fuel, `RPC_AddFuel` (owner +1, no cap check).
* `Smelter.UpdateSmelter` (`InvokeRepeating` 1 s, owner simulates): elapsed time from `s_startTime` into accumulator `s_accTime` (capped 3600 s ⇒ max 1 h offline catch-up); per simulated second: needs fuel (if any), queued ore, roof (if required), unblocked smoke; fuel −= power / (`m_secPerProduct` / `m_fuelPerProduct`); `s_bakeTimer` += power; on `m_secPerProduct` → `RemoveOneOre` + `QueueProcessed` (spawn or stack in `s_spawnOre`/`s_spawnAmount`). Output is spawned at `m_outputPoint` as `ItemDrop`.
* `OnEmpty` → `RPC_EmptyProcessed` → `SpawnProcessed`.
* Destroyed: owner drops fuel, queue and processed output (`DropAllItems`).
* Radial menu: `Smelter.TryGetItems` / `CanUseItems(player, switch, …)`.

### Data & persistence
ZDO: `s_fuel` (float), `s_queued` (int), `"item0".."itemN"` (strings, `ZDOVars.s_item0` = "item0"), `s_bakeTimer`, `s_accTime`, `s_startTime`, `s_spawnOre`, `s_spawnAmount`, `s_cheatedQueued`. RPCs: `RPC_AddOre(string, bool)`, `RPC_AddFuel`, `RPC_EmptyProcessed`.

### Multiplayer authority
Client removes inventory items and sends RPCs; owner mutates queue and simulates. A client that is not the owner sees queue/fuel values up to one sync behind.

### Patch points
`Switch.Interact` (prefix: detect Shift/alt on a smelter switch), `Smelter.OnAddOre` / `OnAddFuel` (private; reverse-patch or reimplement), `Smelter.OnHoverAddOre` / `OnHoverAddFuel` (hints), `Smelter.UpdateSmelter` (speed/efficiency — owner side), `Smelter.Spawn` (output modification).
Related feeders with the same pattern: `CookingStation` (`OnAddFuelSwitch`, `OnAddFoodSwitch`, `RPC_AddItem`), `Fermenter` (`AddItem`, `RPC_AddItem`), `ShieldGenerator` (`RPC_AddFuel`), `Fireplace`.

### Feeding stations with Use (E): facts shared by every feeder class
Learned while building [Batch Station Feeding](../../src/Crafting/Stations.BatchFeed) ([design](../design/crafting-stations-batch-feed.md), section 1).
* **One entry point.** `Player.Update` computes `alt` once per frame (gamepad on an Alternative 1/2 layout, `ZInput.IsNonClassicFunctionality()` → `JoyAltKeys`; otherwise `AltPlace` (Left Shift only) or `JoyAltPlace`) and calls the private `Player.Interact(go, hold, alt)`, the only caller of `Interactable.Interact` for the local Use key (press and hold). `Switch`, `CookingStation` and `Turret` ignore `alt`; `Fireplace` uses it as "add fuel instead of toggling".
* **Build tools block E**: `Player.UpdateHover` sets `m_hovering = null` while `InPlaceMode()` (hammer, hoe, cultivator equipped), so no hover text and no Interact on stations.
* **Every vanilla add path removes exactly one item per press** (`Smelter.OnAddOre`/`OnAddFuel`, `Fireplace.Interact`, `CookingStation.CookItem`/`OnAddFuelSwitch`, `ShieldGenerator.OnAddFuel`, `Turret.UseItem`). `CookItem` returns true for an item in `m_incompatibleItems` without removing anything.
* **Owner-side caps** (the non-owner's check reads its stale local ZDO copy):

  | Owner handler | Caps? | Stale non-owner sends too many → |
  |---|---|---|
  | `Smelter.RPC_AddOre` / `RPC_AddFuel` | no | overfill |
  | `CookingStation.RPC_AddFuel`, `ShieldGenerator.RPC_AddFuel` | no | overfill |
  | `Turret.RPC_AddAmmo` | no, and **no ammo-type check**: sets `s_ammoType` to the last name received | overfill; mixed missile kinds are all relabelled as the last kind (dupe or loss when the ballista drops its ammo) |
  | `Fireplace.RPC_AddFuel` | yes | items lost |
  | `CookingStation.RPC_AddItem` | yes (free slot) | items lost |
* **Unowned ZDO**: `ZNetView.InvokeRPC` routes to owner 0, which `ZRoutedRpc.InvokeRoutedRPC` handles locally **and** broadcasts; handlers that check `IsOwner()` then do nothing anywhere (the add is lost). `CookingStation.RPC_AddItem` has **no** `IsOwner()` check; it is safe only because `CookItem` claims ownership first. `Fireplace.Interact` also claims; smelter, fuel switches, shield and ballista do not.
* **Owner just left**: `ZNet.Disconnect` → `ZDOMan.RemovePeer` only removes orphaned non-persistent ZDOs; a station keeps the gone owner's uid until the server's next `ZDOMan.ReleaseZDOS` pass (every 2 s) hands it on (longer while a crashed peer has not timed out). Adds sent in that window are dropped by the server (`ZRoutedRpc.RouteRPC` finds no peer). The server sends a new player list right on disconnect (`ZNet.Disconnect` → `SendPlayerList`), so a client can tell whether an owner uid still belongs to a connected game (`PlayerInfo.m_characterID.UserID`).
* **Frost Foundry, Frigid Kiln and Hot Tub** (1.0.16, read from the game data; the Batch Station Feeding coverage dump confirms them in game): `piece_FrostKiln` "Frigid Kiln" is a **`Smelter`** with a fuel switch only (`m_addOreSwitch` null, `m_maxOre` 0, `m_maxFuel` 25, `m_fuelPerProduct` 5, `m_secPerProduct` 30) and a no-source conversion to Liquid Frost (`FrozenFuel`): Ice goes in as fuel (an "Add Ice" text is in the data;
the fuel item reference itself is unverified). `piece_FrostFoundry` "Frost Foundry" is a **`CookingStation`** with **one** slot, a food switch ("Place Cast") and a fuel switch (Liquid Frost, `m_maxFuel` 20, `m_secPerFuel` 10, `m_useFuel`, no fire needed, `m_skill` None), `m_recordCrafter` and `m_spawnFullDurability` on, 30 cast conversions of 50 s. `piece_bathtub` "Hot Tub" is a **`Smelter`**, not a `Fireplace`: fuel switch only (Wood, `m_maxFuel` 10, `m_fuelPerProduct` 1, `m_secPerProduct` 5000, so one Wood burns per 5000 s while it has fuel), `m_maxOre` 0, no conversion.
* **Vanilla capacities** (1.0.16 game data): Smelter and Blast Furnace 10 ore / 20 Coal, Eitr Refinery 20 / 20, Charcoal Kiln 25, Windmill 50, Spinning Wheel 40 (no fuel). Only the Windmill has an Empty switch (`m_emptyOreSwitch`); the others drop their output. The station add switches seem to use `m_holdRepeatInterval` 0.2 (holding E keeps adding; read by record layout,
unverified: `Switch` defaults to −1, which means no repeat). Cooking stations: section 14 of [farming-cooking.md](farming-cooking.md).
* **No-source conversions**: `ItemConversion.m_from` can be null (`Smelter.Awake` sets `m_noSourceConversion`); `FindCookableItem` skips them, but `Smelter.TryGetItems` (radial menu) dereferences `m_from`: null-check it in any code that reads `m_conversion`.
* **Cooking station hover vs press**: the hover switches to "take" only when every slot is done (`IsEverythingCooked`), but E takes a finished item as soon as any slot is done (`HaveDoneItem`).
* **Key hints**: vanilla's combined-key hint (`ItemStand.GetHoverText`) shows `$KEY_AltPlace + $KEY_Use`, or `$KEY_AltKeys` when `IsNonClassicFunctionality() && IsGamepadActive()`. `Localization.Localize` serves whole strings from an LRU cache that a key rebind does not evict (only language and input-layout changes do): read live bindings with `Localization.GetBoundKeyString`.
* **First skill level-up is a Center message**: `Skills.RaiseSkill` uses `MessageType.Center` when the level before the raise was 0 (`TopLeft` afterwards), via `Player.Message` (not logged). Code that mutes Center messages during a loop (cooking raises Cooking 0.4 per item) must keep it.

---

## 12. Incinerator (Obliterator)

`Incinerator` = a `Container` + lever `Switch`. `OnIncinerate` (ward check) → owner `RPC_RequestIncinerate` → coroutine `Incinerate`: after 5–7 s, runs every `IncineratorConversion.AttemptCraft` (sorted by `m_priority`; `m_requirements` list of `{m_resItem, m_amount}`, `m_requireOnlyOneIngredient`, `m_result`, `m_resultAmount`), then converts the remaining item count into `m_defaultResult` (1 per `m_defaultCost`), clears the container and adds results. **Owner-side**, data-driven — a convenient item sink/converter (e.g. gems → dust) by appending to `m_conversions` at `ZNetScene.Awake` (all potential owners need the same list ⇒ everyone).

---

## 13. Trinkets (1.0) and adrenaline pop

* Item type **`ItemDrop.ItemData.ItemType.Trinket` (24)**; one equip slot `Humanoid.m_trinketItem` (`Humanoid.EquipItem`/`UnequipItem`); visual via `VisEquipment.SetTrinketItem(prefabName)` synced with `ZDOVars.s_trinketItem`. Cannot be equipped if `item.m_worldLevel < Game.m_worldLevel` (same as utility items, message `$msg_ng_item_too_low`). Durability drains while equipped if `m_useDurability` (`Humanoid.DrainEquipedItemDurability`).
* Crafted with ordinary `Recipe`s at stations *(prefab)*; SoftRef names include `TrinketBronzeHealth`, `TrinketBronzeStamina`, `TrinketIronHealth`, `TrinketIronStamina`, `TrinketSilverDamage`, `TrinketSilverResist`, `TrinketBlackDamageHealth`, `TrinketBlackStamina`, `TrinketCarapaceEitr`, `TrinketChitinSwim`, `TrinketScaleStaminaDamage`, `TrinketFlametalEitr`, `TrinketFlametalStaminaHealth`, `TrinketBloodGoldHealth`, `TrinketBloodGoldStamina`, `TrinketDNGold`, `TrinketDNNornThread`.
* Stats: `SharedData.m_maxAdrenaline` is one of the 11 equipment modifier fields (`Player.s_equipmentModifierSources`, index 10) summed by `Player.UpdateModifiers` → `GetEquipmentMaxAdrenaline`. `m_equipStatusEffect` is applied by `Humanoid.UpdateEquipmentStatusEffects`.
* Pop: `Player.AddAdrenaline` — when adrenaline reaches max, **every equipped item** with `m_fullAdrenalineSE` adds/refreshes that SE and adrenaline resets to 0 (plus `m_adrenalinePopEffects`); `m_blockAdrenaline`/`m_perfectBlockAdrenaline` live on shields. (Details of adrenaline generation belong to the Combat chapter.)
* Tutorial key `"trinket"` shown on first pickup (`Player.OnInventoryChanged`).

---

## 14. Trophies and creature drops

* **`ItemType.Trophy` (13)**. Picking one up → `Player.AddKnownItem` → `Player.AddTrophy` stores the **prefab name** in `Player.m_trophies` (saved with the character).
* Trophy panel: `InventoryGui.UpdateTrophyList` places each known trophy at `SharedData.m_trophyPos` (grid, × `m_trophieListSpace`), strips a trailing " trophy" from the name and shows `<m_name>_lore` localisation. New trophies need a free `m_trophyPos` or they overlap.
* Drops: **`CharacterDrop`** (`m_drops` list of `Drop {m_prefab, m_amountMin/Max, m_chance, m_onePerPlayer, m_levelMultiplier, m_dontScale}`), `CharacterDrop.GenerateDropList` (chance × 2^(level−1) if `m_levelMultiplier`; **pseudo-random** "bad-luck protection" for chances ≤ 0.3 via static `s_pseudoCounter` unless `GlobalKeys.NoPseudoDrops`; amounts via `Game.ScaleDrops`; cap 100), `CharacterDrop.DropItems`.
* Who drops: `Character.OnDeath` runs the drop only on the **creature's ZDO owner** (`m_onDeath` → `CharacterDrop.OnDeath`); if the death effect spawns a `Ragdoll` with `m_dropItems`, drops are deferred to the ragdoll.
* Display: `ItemStand` (`m_supportedTypes`/`m_supportedItems`, attach visual from the item prefab's `attach` child via `ItemStand.GetAttachPrefab`) stores the full `ItemData` with `ItemDrop.SaveToZDO` (so `m_customData` survives).

---

## 15. Gems and Ashlands magic weapon variants

* Gems: `$item_gemstone_red` Bloodstone, `$item_gemstone_blue` Iolite, `$item_gemstone_green` Jade (SoftRef also lists `GemstoneBlack/Orange/Purple` assets). Their only uses are **recipe resources** and the trader: `StoreGui.SellItem` pays `m_shared.m_value × stack` coins **client-side**.
* Magic variants are **separate item prefabs with their own recipes** (SoftRef: `Recipe_AxeBerzerkr_Blood/_Lightning/_Nature`, `Recipe_MaceEldner_*`, `Recipe_SpearSplitner_*`, `Recipe_CrossbowRipper_*`, `Recipe_BowAshlands_*`, plus Deep-North `*Gold_BloodLightning` recipes). There is **no gem-specific code**: element = different `m_damages`/`m_attackStatusEffect` on the variant prefab.
* `Recipe.m_requireOnlyOneIngredient` / `Piece.Requirement.m_extraAmountOnlyOneIngredient` / `m_qualityResultAmountMultiplier` exist for "any one of these" recipes (which vanilla recipes use it is prefab data) — usable for gem-agnostic recipes such as "any one gem".

---

## 16. Item appearance: variants, ItemStyle, shields, capes, barber

* `ItemData.m_variant` (int, saved) indexes `SharedData.m_icons[]`; `m_variants` = count. Chosen **only at craft time** (`InventoryGui.OnShowVariantSelection` / `VariantDialog` / `OnVariantSelected`; the variant button is hidden in the upgrade tab). Upgrades keep the variant.
* Rendering: `VisEquipment.AttachItem` / `AttachArmor` call `IEquipmentVisual.Setup(variant)`; the only implementation is **`ItemStyle`**, which sets the shader property `_Style` through `MaterialMan` (texture-atlas style index). Used by items with `m_variants > 1` (shield patterns; any shoulder item with variants).
* **Network sync of variants exists only for the left hand, left back and shoulder slots**: `VisEquipment.SetLeftItem/SetLeftBackItem/SetShoulderItem(hash, variant, quality)` with ZDO `s_leftItemVariant`, `s_leftBackItemVariant`, `s_shoulderItemVariant`. Chest, legs and helmet have **no variant** (`SetChestItem/SetLegItem/SetHelmetItem(hash)`; `AttachArmor` called with variant −1). `VisEquipment.UpdateEquipmentVisuals` reads the ZDO on non-owners.
* Player looks: `Barber` (chair piece; attaches player, opens `PlayerCustomizaton` "barber GUI"), hair/beard are `ItemType.Customization` items, colours `s_hairColor`/`s_skinColor`. Armour tint/glow has no vanilla equivalent.

---

## 17. Enchant-like mechanics and per-item data

Vanilla 1.0 has **no enchanting system**. Closest mechanics:
1. **Quality** (`m_quality`) scales damage/armor/durability/block (`ItemData.GetDamage`, `GetArmor`, `GetMaxDurability`, `GetBlockPower`); the Forge of Potential allows quality > `m_maxQuality`.
2. **World level** (`m_worldLevel`, "NG+"): adds `Game.m_worldLevelGearBaseDamage/AC`; low-world-level items can be repaired anywhere and cannot equip utility/trinkets.
3. **Element variants** as separate prefabs (§15).
4. **Set bonuses** (`m_setName`, `m_setSize`, `m_setStatusEffect`) and **equip SEs** (`m_equipStatusEffect`).

Extension points for mods:
* **`ItemData.m_customData` (`Dictionary<string,string>`)** — unused by vanilla, serialised everywhere (`ItemData.Save/Load`, `Inventory.Save`, `ItemDrop.SaveToZDO`, containers, item stands, tombstones). **Lost on crafting upgrades and Forge refinement** (item re-created). Namespace keys (e.g. `"vmods.enchant.v1"`) — item-extension mods such as EpicLoot also store per-item data on items.
* **`Player.m_customData`** — per-character dictionary saved in the player profile (unlocks, per-player state).
* Stat hooks (instance methods on `ItemDrop.ItemData`, hot paths): `GetDamage(int, float)`, `GetArmor(int, float)`, `GetBaseBlockPower(int)`, `GetMaxDurability(int)`, `GetDeflectionForce(int)`; tooltip: static `ItemDrop.ItemData.GetTooltip(item, qualityLevel, crafting, worldLevel, stackOverride, appending)`; SEs: `Humanoid.UpdateEquipmentStatusEffects`.

---

## 18. Magic in non-combat systems (existing hooks)

* **`SE_Stats`** already supports non-combat buffs without code: `m_skillLevel` + `m_skillLevelModifier` (applied in `Skills.GetSkillLevel` via `SEMan.ModifySkillLevel`), `m_raiseSkill` + `m_raiseSkillModifier` (XP gain, `Player.RaiseSkill`), carry weight, speed, stamina modifiers, regen multipliers. Skill factors drive: craft bonus chance and craft duration (`InventoryGui`, station `m_craftingSkill`), harvest radius (`Piece.OnPlaced`, `Attack` for scythe, Farming), fishing (`FishingFloat`), cooking station skill (`CookingStation.m_skill`), riding (`Sadle`). There is no Sailing skill in 1.0.
* **`StatusEffect.m_attributes`** (`StatusAttribute`: `ColdResistance` 1, `DoubleImpactDamage` 2, `SailingPower` 4, `TamingBoost` 8) — OR-ed by `SEMan.Update` and **synced through `ZDOVars.s_seAttrib`**, so *other* clients can query a player's buffs with `SEMan.HaveStatusAttribute` (used by `Ship.IsWindControllActive`, `Tameable`, `ImpactEffect`). New bits can be added by mods; vanilla ignores unknown bits.
* **`EffectArea`** types (`Heat`, `Fire`, `PlayerBase`, `Burning`, `Teleport`, `NoMonsters`, `WarmCozyArea`, `PrivateProperty`) + `m_statusEffect` applied to characters inside.
* **`ShieldGenerator`** — fuelled magical building (`m_fuelItems`, `m_maxFuel`, `m_fuelPerDamage`, dome radius `m_minShieldRadius` 10 – `m_maxShieldRadius` 30, optional attack). Already a "magic helps farming" precedent: `Plant.UpdateHealth` skips the Ashlands-heat and Mountain/Deep-North-cold kill checks inside a shield.
* Growth / processing hooks for "magic boosts": `Plant.GetGrowTime` (private), `CookingStation.UpdateCooking`, `Smelter.UpdateSmelter`, `Fermenter.SlowUpdate`, `Ship.GetSailForce`.

---

## 19. Cross-cutting implementation notes

* **Registering new items/pieces**: add prefab to `ObjectDB.m_items` (+ `UpdateRegisters`, private) and to `ZNetScene.m_prefabs` / `m_namedPrefabs` (built in `ZNetScene.Awake`), recipe to `ObjectDB.m_recipes`, SE to `ObjectDB.m_StatusEffects`. Do it in both `ObjectDB.Awake` and `ObjectDB.CopyOtherDB`. Jotunn automates this and adds a mod-version handshake. New prefabs must exist on **every** peer: items whose prefab is missing are skipped when an inventory loads (`Inventory.AddItem` logs "Failed to find item prefab" and the item is gone at the next save), and when the server/host fails to instantiate a world ZDO it destroys it (`ZNetScene.CreateObjectsSorted` / `CreateDistantObjects`, "Destroyed invalid prefab ZDO").
* **Editing vanilla prefabs** (flags such as `m_infiniteFuel`, `m_supports`): postfix `ZNetScene.Awake` and iterate `m_prefabs`; values then apply to every instance created afterwards.
* **Save compatibility**: vanilla keys only ⇒ uninstall-safe. Custom ZDO keys are ignored by vanilla. Custom item prefabs disappear on uninstall. `m_customData` survives uninstall (ignored).
* **Reading private members**: use a publicized `assembly_valheim` reference or `AccessTools`/`Traverse`; many target methods here are private (`DoCrafting`, `RepairOneItem`, `OnAddOre`, `UpdateSupport`, `CheckAccess` overloads are public/static though).

---

## Feature ideas

Legend: feasibility = trivial / easy / medium / hard / very hard. "Who needs the mod" follows the authority primer above.

### 1. Crafting — Forge of Potential revamp (QoL)
*User: only consume the idol on failure, don't destroy the item; require a higher-tier idol at high levels; maybe a skill requirement.*

* **Feasibility:** medium.
* **Who needs the mod:** client-only (refinement is local). Use a server-synced config so every player on a server gets the same odds.
* **Hooks:** `InventoryGui.DoCrafting` (prefix, active only when `Player.GetCurrentCraftingStation().m_upgrader`), `InventoryGui.OnCraftPressed` (the vanilla empty-slot check is no longer needed), `InventoryGui.UpdateRecipe` (postfix: button state, skill-gate tooltip, show success chance, replace `$inventory_upgraderwarning`), `Piece.Requirement.GetAmount` (postfix: tier bands), `ObjectDB.Awake`/`CopyOtherDB` (inject higher-tier idol requirements), `ItemDrop.ItemData.GetTooltip` (optional chance display).
* **Sketch:** Take over the upgrader branch of `DoCrafting`. Roll with the idol's `m_upgradeChance` (optionally + skill bonus). On success, raise `m_craftUpgradeItem.m_quality` **in place**, keeping durability %, crafter and `m_customData`, and consume the idols. On failure, consume only the idol requirement and leave the item untouched: no break, no downgrade. For tier gating, add extra `m_upgraderResource` requirements (next idol tiers) to each recipe. Then make `GetAmount(q)` return 0 outside each requirement's quality band. The UI, the check and the consumption all respect a 0 amount. You also need to pick "the first upgrader requirement with amount > 0" as the rolling idol (vanilla takes the first one). Check the skill gate with `Player.GetSkills().GetSkillLevel(skill) ≥ threshold(q)` in both `UpdateRecipe` and `DoCrafting`.
* **Risks:** Balance: removing break/downgrade makes quality creep beyond max nearly free, so keep scaling idol costs or add diminishing chance per level. EpicLoot and other item mods patch `DoCrafting`; keep the prefix narrow (upgrader only) and preserve `m_customData`, which is also a compat win. Our odds apply only to players with the mod. Fix the vanilla `$msg_upgrader_failed` key (add a localisation entry). A quality-1 item fail edge case disappears with the new rules.

### 2. Crafting — One-click repair all (QoL, exists in other mods)
* **Status:** implemented as [One Click Repair All](../../src/Crafting/Repair.OneClickAll) (0.1.0). The design that shipped differs from the sketch below: the loop stops at the first press that repairs nothing (cost mods keep `HaveRepairableItems` true when the player cannot pay, so a `while (HaveRepairableItems())` loop freezes the game), and the summary reuses `$msg_repaired` with item names so it stays translated. See [docs/design/crafting-repair-one-click-all.md](../design/crafting-repair-one-click-all.md).
* **Feasibility:** trivial.
* **Who needs the mod:** client-only.
* **Hooks:** `InventoryGui.OnRepairPressed` (postfix) or `InventoryGui.RepairOneItem` (prefix loop), `InventoryGui.HaveRepairableItems`, `InventoryGui.CanRepair`; optional hotkey in `InventoryGui.Update`.
* **Sketch:** After the vanilla single repair, loop `RepairOneItem()` while `HaveRepairableItems()` returns true, with a safety cap of about 64. Calling the vanilla method keeps compatibility with mods that add repair costs. Replace the per-item centre messages with one summary ("Repaired N items"). XP stays the same, because vanilla already raises Crafting per item. Optionally add "repair at any station in range", which is a revamp: extend `CanRepair` to nearby stations.
* **Risks:** Minimal. It has to coexist with "repair requires materials" mods (the loop honours their `CanRepair`/`RepairOneItem` patches). It still repairs only items this station can repair (vanilla rule). Message spam if the per-item message isn't suppressed.

### 3. Building — Torches on/off only, no fuel (QoL, partially exists)
* **Feasibility:** easy.
* **Who needs the mod:** everyone. Fuel burn runs on the torch's ZDO owner, and `IsBurning()` is evaluated per client from its own prefab flags.
* **Hooks:** `ZNetScene.Awake` (postfix: for whitelisted `Fireplace` prefabs set `m_infiniteFuel = true`, `m_canTurnOff = true`, `m_canRefill = false`), `Fireplace.Interact` (prefix: allow `RPC_ToggleOn` when fuel is 0 for infinite pieces), `Fireplace.GetHoverText` (postfix: vanilla returns "" for infinite fuel; show state plus a `[E] Turn on/off` hint), `Fireplace.UpdateState` (keep rain auto-off optional), `Fireplace.TryGetItems`/`CanUseItems` (already false).
* **Sketch:** Use a config list of prefab names (torches, sconces, braziers, standing lamps). Seed it at first run from the Appendix A dump, for example every `Fireplace` piece whose `Piece.m_comfortGroup != ComfortGroup.Fire` (heuristic; review by hand). Hearths, campfires and cooking fires stay fuel-based. Flip the flags on the prefabs. Toggle writes the vanilla `s_state` (1/2), so the state survives uninstall. When toggled on, also top up `s_fuel` to max through `SetFuel`, so the pieces stay lit for a while if the mod is removed.
* **Risks:** Mixed installs: a vanilla owner still burns fuel, and vanilla viewers see it go out when fuel reaches 0. Rain auto-off applies because `m_canTurnOff` becomes true on pieces with Low/High wet visuals. ValheimPlus and other "infinite torches" mods overlap. Pieces that already use `m_canTurnOff`/`m_infiniteFuel` in 1.0 (the "partially exists" part) need a runtime dump to avoid double handling.

### 4. Building — Place a sign on a chest (QoL)
* **Feasibility:** medium.
* **Who needs the mod:** everyone. Placement is local, but the sign's support and wear are simulated by its ZDO owner, and a vanilla owner computes zero support and breaks it.
* **Hooks:** `Player.UpdatePlacementGhost` (transpiler on the `!wearNTear.m_supports ⇒ Invalid` check, or a postfix that re-validates), `WearNTear.UpdateSupport` (transpiler so a `Sign` piece accepts a `Container` neighbour as support), `ZNetScene.Awake` (optional: add `snappoint` children on chest faces), `Sign`.
* **Sketch:** When the ghost has a `Sign` component and the ray hits a piece with a `Container`, skip the `m_supports` rejection. In support calculation, treat a `Container` neighbour as supporting for Sign pieces only, instead of flipping chest `m_supports = true` globally, which would let anything stack on chests. Add face snap points to chest prefabs so signs align. A lighter alternative, "chest label": Shift+E on a chest opens `TextInput`, stores the text in a custom ZDO string on the chest, and renders a small TMP label child. No stability issues, but it only shows for modded clients.
* **Risks:** Transpilers on `UpdatePlacementGhost` conflict with build mods (Gizmo, PlanBuild, Valheim Build Camera, etc.). On uninstall, signs on chests lose support and break (resources drop). Moving or removing the chest drops the sign (intended). The UGC/privilege flow in `Sign.Interact` stays untouched.

### 5. Crafting — Shift+E feeds 5 items to furnaces and kilns (QoL)
* **Status:** implemented as [Batch Station Feeding](../../src/Crafting/Stations.BatchFeed) (0.1.0). The design that shipped differs from the sketch below: it hooks `Player.Interact` (one place for every feeder class: smelters, fires, cooking stations, shield generator, ballista) and repeats the station's own vanilla `Interact` up to N times instead of calling the RPCs itself, so vanilla checks, messages, skills and other mods' patches run per item; a non-owner tracks the adds it sent for 3 s (the owner-side handlers do not cap, or lose the item); spots that hold one item (the Frost Foundry's cast slot) stay vanilla. See [docs/design/crafting-stations-batch-feed.md](../design/crafting-stations-batch-feed.md).
* **Feasibility:** easy.
* **Who needs the mod:** client-only. The client removes items from its own inventory and sends the vanilla `RPC_AddOre`/`RPC_AddFuel`, which any vanilla owner accepts.
* **Hooks:** `Switch.Interact` (prefix: `alt == true` and the switch is `m_addOreSwitch`/`m_addWoodSwitch` of a parent `Smelter`; `alt` comes from `Player.Update` = `AltPlace`, Left Shift by default, or `JoyAltKeys`/`JoyAltPlace` on gamepad), `Smelter.OnAddOre`/`OnAddFuel` (reuse their checks through a reverse patch, or reimplement), `Smelter.OnHoverAddOre`/`OnHoverAddFuel` (append a `[Shift+E] ×5` hint). Optionally the same for `CookingStation.OnAddFuelSwitch`, `Fireplace.Interact` (alt currently means "add fuel instead of toggle") and `ShieldGenerator`.
* **Sketch:** Compute `n = min(5, m_maxOre − GetQueueSize(), inventory count of FindCookableItem's type)` (fuel: `min(5, m_maxFuel − ceil(GetFuel()), count)`). Then n times: `Inventory.RemoveItem(item, 1)` + `m_nview.InvokeRPC("RPC_AddOre", item.m_dropPrefab.name, item.m_cheated)`. Show one "Added N × Copper ore" message and play the add effect once. Make N configurable.
* **Risks:** A non-owner reads a stale `s_queued`/`s_fuel`, and the owner's RPC handlers don't cap, so a simultaneous feed by two players can overfill (vanilla already has this race). Track locally sent amounts for about 1 s to limit it. Conflicts with auto-fuel and V+ smelter tweaks are low because we read `m_maxOre`/`m_maxFuel` live. The binding clashes with nothing on smelters (vanilla `Switch` ignores `alt`).

### 6. Crafting — One trinket per trophy (New)
* **Feasibility:** hard (mechanically medium, but about 70 items with icons, effects and balance).
* **Who needs the mod:** everyone. New item prefabs and status effects must exist on all clients, and the dedicated server should also run it (Jotunn version check).
* **Needs assets:** yes, at least icons. Models can be clones of vanilla trinkets with tinted materials or a trophy mesh on a cord.
* **Hooks:** `ObjectDB.Awake`/`CopyOtherDB` (items, recipes, SEs), `ZNetScene.Awake` (prefabs), `Player.AddAdrenaline` (the vanilla pop applies `m_fullAdrenalineSE`, no patch needed), `Humanoid.UpdateEquipmentStatusEffects` (vanilla applies `m_equipStatusEffect`), `ItemDrop.ItemData.GetTooltip` (optional lore line).
* **Sketch:** Use a data file mapping `trophy prefab → {name, station + level, extra mats, m_maxAdrenaline, equip SE, full-adrenaline SE}`. Clone a vanilla trinket prefab (e.g. `TrinketBronzeHealth` if that is the prefab name), set `SharedData` fields, and composite the trophy icon onto a trinket frame at runtime (render-to-texture) or ship PNGs. The recipe is the trophy plus biome-tier metal. Put the SE logic in a small set of parameterised `SE_Stats` subclasses (e.g. Deer = sprint stamina, Neck = swim, Troll = carry weight) to keep the code volume sane.
* **Risks:** Balance against vanilla trinkets and adrenaline. Removing the mod deletes these items. SE name-hash collisions. The number of assets. Overlap with trophy-buff mods. Boss trophies are already used for the forsaken altars, so decide whether to include them.

### 7. Crafting — Trophies for mobs without a trophy (New)
* **Feasibility:** medium.
* **Who needs the mod:** everyone. Drops are rolled by the creature's ZDO owner, and the new trophy prefabs must exist everywhere.
* **Needs assets:** yes (trophy models and icons; a cloned trophy with a re-textured mesh is the cheapest route).
* **Hooks:** `ZNetScene.Awake` (postfix: scan prefabs having `CharacterDrop`, find those whose `m_drops` contain no `ItemType.Trophy`, append a `CharacterDrop.Drop` with our trophy, `m_chance` configurable, `m_levelMultiplier` false), `ObjectDB` registration, `InventoryGui.UpdateTrophyList` (assign free `m_trophyPos` cells), localisation `<name>` and `<name>_lore`, `ItemStand` (prefab needs an `attach` child).
* **Sketch:** Generate the list at runtime so 1.0 content (Ashlands, Deep North, etc.) is covered automatically, with a config override. Each trophy clones a vanilla trophy prefab, swaps mesh/material/icon and sets `m_trophyPos` into an extra grid row. Then feed them into idea 6.
* **Risks:** Pseudo-random drop smoothing (chance ≤ 0.3) behaves differently from a flat chance. Drop-table mods (Drop That, CLLC, EpicLoot's loot tables) may overwrite `m_drops`, so order after them or apply lazily in `CharacterDrop.Start`. Trophy panel overlap. Some mobs are summons or tames (e.g. staff skeletons) and should be excluded.

### 8. Building — Magic applied to non-combat (crafting, farming, cooking, sailing…) (New)
* **Feasibility:** very hard as a whole. Individual effects are easy to medium.
* **Who needs the mod:** depends. Buffs that affect only the local player (crafting speed and bonus, harvest radius, carry weight) are client-side. Buffs that affect world objects (plant growth, smelter or cooking speed, ship wind) must run on each object's owner, so everyone.
* **Needs assets:** yes (staffs or runes, VFX, icons).
* **Hooks:** `SE_Stats` fields (`m_skillLevel`, `m_raiseSkill`, `m_addMaxCarryWeight`…), `SEMan.ModifySkillLevel`, `StatusEffect.m_attributes` with **new bits synced via `ZDOVars.s_seAttrib`** and `SEMan.HaveStatusAttribute`, `Plant.GetGrowTime`, `CookingStation.UpdateCooking`, `Smelter.UpdateSmelter`, `Ship.GetSailForce`/`Ship.IsWindControllActive`, `Tameable` (`TamingBoost`), `InventoryGui.DoCrafting` (e.g. chance to refund a resource), `EffectArea`.
* **Sketch:** Build a small "blessing" framework. Eitr-costing staffs, meads or rune pieces grant SEs that either modify skills (no patch) or set custom `StatusAttribute` bits. Object owners (plants, stations, ships) poll players in range with `SEMan.HaveStatusAttribute(customBit)` (cached, every few seconds) and apply multipliers. A station-bound variant uses a rune piece placed near a station that writes a ZDO flag the station owner reads.
* **Risks:** Authority: must run where the object is simulated. Performance: range scans, so cache and use timers. Bit collisions with other mods: use high bits and make them configurable. Balance. Scope creep: implement as a shared library used by several category mods.

### 9. Crafting — Enchanting (New)
* **Feasibility:** very hard.
* **Who needs the mod:** everyone. Damage enchants apply on the attacker's client (`HitData` is built there) and armour enchants on the defender's owner, but tooltips, visuals and consistent rules require all clients.
* **Needs assets:** yes (enchanting station piece, VFX, rune or dust items, icons).
* **Hooks:** `ItemDrop.ItemData.m_customData` (storage), `ItemDrop.ItemData.GetTooltip` (display), `ItemDrop.ItemData.GetDamage`/`GetArmor`/`GetBaseBlockPower`/`GetMaxDurability` (stats), `Humanoid.UpdateEquipmentStatusEffects` (granted SEs), `Character.Damage`/`Attack` hit callbacks (procs), `InventoryGui.DoCrafting` (**carry `m_customData` across upgrades and refinement**), a new `CraftingStation` or custom UI (patterned on `InventoryGui` tabs).
* **Sketch:** Store versioned JSON-ish entries in `m_customData["vmods.ench"]` (affix id, tier, roll). Enchant at a new station that consumes gems, essences and eitr (ties into ideas 13 and 1). Apply stats through small postfixes on the `ItemData` getters, cached per item instance and invalidated on change. Keep affixes data-driven.
* **Risks:** Direct overlap with **EpicLoot** (both enchant, both extend tooltips and customData): detect it and disable or bridge. Hot-path performance (`GetDamage`/`GetTooltip` are called often). Vanilla upgrades silently wipe `m_customData`. Save compatibility is good (data is ignored by vanilla). Server-side cheating can't be prevented (client-authoritative inventories).

### 10. Building — Ward revamp (New)
* **Feasibility:** hard.
* **Who needs the mod:** everyone. `PrivateArea.CheckAccess` runs on the acting client and damage runs on the piece owner, so a vanilla client simply follows vanilla rules.
* **Needs assets:** no for a rules/UI revamp. Yes if you add new ward tiers or models.
* **Hooks:** `PrivateArea.CheckAccess` (central), `PrivateArea.HaveLocalAccess`, `PrivateArea.Interact`/`GetHoverText`/`RPC_TogglePermitted` (management UI and roles), `WearNTear.RPC_Damage` (prefix on owner: cancel damage from non-permitted players or monsters inside an active ward), sites that currently don't check wards (`Pickable.Interact`, `CookingStation`, `Smelter` switches via `Switch.Interact`, `Fireplace.Interact`, `Tameable`), `Container.CheckAccess` (implement the unused `Group` privacy as "ward members"), `ShieldGenerator` (optional synergy/fuel).
* **Sketch:** Extend the ward ZDO with custom keys (tier/radius, flags: protect crops, stations, tames, doors, PvP, raids, member roles), keeping the vanilla `pu_id`/`pu_name` list. Put all rules in one `WardRules.Allowed(action, point, player)` called from `CheckAccess` and the new call sites. Add an alt-interact management panel.
* **Risks:** WardIsLove and V+ ward settings conflict heavily. Enforcement is inherently client-side, so it needs a mod-presence check (Jotunn/ServerSync). Performance: `CheckAccess` iterates all wards and is called from hover text every frame, so cache per frame. Raid events (`RandEventSystem`) interplay. Uninstall leaves vanilla wards intact.

### 11. Crafting — Player trophy (New)
* **Feasibility:** medium.
* **Who needs the mod:** everyone. The trophy is spawned by the victim's client in `Player.OnDeath`, and the item prefab must exist on all clients.
* **Needs assets:** yes (trophy model and icon; could reuse a skull/head mesh with a tint).
* **Hooks:** `Player.OnDeath` (postfix, owner only: `m_lastHit.m_hitType == HitData.HitType.PlayerHit` and `m_lastHit.GetAttacker()` is a `Player`), `ObjectDB`/`ZNetScene` registration, `ItemDrop.ItemData.GetTooltip` and `ItemDrop.GetHoverText` (show "Trophy of <victim>"), `ItemStand` (attach child), `InventoryGui.UpdateTrophyList` (exclude, or one generic entry).
* **Sketch:** On a PvP death, instantiate `Trophy_Player` at the body with `m_customData = {victim, victimId, killer, day}`. The data persists in inventories, chests and item stands. Optionally, a peaceful variant: a craftable "bust of yourself" that uses `Player.m_customData` for once-per-player unlocks.
* **Risks:** Griefing or farming (add a per victim/killer cooldown, PvP only, config). `GetAttacker()` needs the attacker to be loaded. The name localisation check expects "... trophy". Only works when the victim has the mod.

### 12. Crafting — Barber shop for armour (shield pattern, armour colour/glow…) (New)
* **Feasibility:** medium for re-styling shields and capes. Hard for armour tint/glow.
* **Who needs the mod:** depends. Changing `m_variant` of shields and capes is client-only because vanilla already syncs `s_leftItemVariant`/`s_leftBackItemVariant`/`s_shoulderItemVariant`. Chest/legs/helmet colour or glow needs new ZDO keys and render code, so everyone.
* **Needs assets:** no for re-selecting existing variants. Yes for new patterns, palettes or glow masks.
* **Hooks:** `InventoryGui.UpdateRecipe` (show `m_variantButton` in the upgrade tab), `InventoryGui.OnShowVariantSelection`/`VariantDialog.Setup`/`InventoryGui.OnVariantSelected` (apply to the selected existing item instead of the next craft), `Humanoid.SetupVisEquipment` (push the change), `ItemStyle.Setup` (`_Style`), `VisEquipment.SetChestEquipped`/`SetLegEquipped`/`SetHelmetEquipped` and `VisEquipment.UpdateEquipmentVisuals` (read custom tint keys, apply a `MaterialPropertyBlock`/`MaterialMan` colour), `Barber` + `PlayerCustomizaton` (UI pattern for a "tailor" station).
* **Sketch:** Phase 1 (client-only): a "Tailor" interaction (new piece, or the upgrade tab of the workbench) lets you pick any existing variant of an owned shield or cape for a small fee (dye, coins) and writes `item.m_variant`. Phase 2: store `tint`/`glow` in `m_customData`, mirror the equipped values to ZDO keys (e.g. `vmods_chestTint`), and apply per renderer on all clients.
* **Risks:** Shader support (`_Style` is an atlas index; tinting needs `_Color`/`_EmissionColor`, which `MaterialMan` already uses for highlights, so avoid fighting with `WearNTear.Highlight`-style overrides). Visuals are invisible to vanilla players. Conflicts with cosmetic mods and armour-model replacers.

### 13. Crafting — Gemstone revamp (New)
*User: give gems a crafting use, gold is too plentiful.*

* **Feasibility:** easy (recipe or data sinks) to medium (with new items or mechanics).
* **Who needs the mod:** depends. New recipes that output vanilla items and `m_value` changes are client-only (crafting and selling are local). New gem items or Obliterator conversions (owner-side) need everyone.
* **Needs assets:** only if you add new items (cut gems, gem dust, socketed jewellery).
* **Hooks:** `ObjectDB.Awake`/`CopyOtherDB` (recipes), `ItemDrop.ItemData.SharedData.m_value` (trader price, read in `StoreGui.SellItem`), `Incinerator.m_conversions` (gem to dust at the Obliterator), the Forge of Potential roll in `InventoryGui.DoCrafting` (gem as an optional catalyst: +X % success per gem), enchanting (idea 9) and trophy trinkets (idea 6) as gem sinks.
* **Sketch:** Make gems the universal magic catalyst. (a) Optional "catalyst" requirement at the Forge of Potential that raises the success chance, which ties into idea 1. (b) Socket or enchant ingredient. (c) Upgrade material for trinkets. (d) Lower `m_value` or add a trader tax to reduce coin inflation. Keep all numbers in a server-synced config.
* **Risks:** Economy balance. Recipe-editing mods (WackyDatabase, etc.) may override. Changing `m_value` changes what players see at the trader (fine with config sync). Idea 1's catalyst patch shares `DoCrafting` with other mods.

---

## Appendix A — runtime verification helpers

Prefab-level values (per-idol chances, which fireplaces can be turned off, smelter capacities, trophy coverage) aren't in the decompiled code. Add a dev-only console command in the shared tools project that dumps them after `ZNetScene.Awake`:

* Every prefab with `Fireplace`: name, `m_infiniteFuel`, `m_canTurnOff`, `m_canRefill`, `m_maxFuel`, `m_secPerFuel`, `m_fuelItem`, presence of `m_enabledObjectLow/High`.
* Every `Smelter`: name, `m_maxOre`, `m_maxFuel`, `m_fuelPerProduct`, `m_secPerProduct`, conversions, `m_addOreSwitch.m_holdRepeatInterval`.
* Every `CraftingStation`: `m_name`, `m_upgrader`, `m_hasCraftTab`, `m_canRepair`, `m_craftingSkill`.
* `ObjectDB.m_recipes` whose `m_resources` contain `m_upgraderResource`: result, idol prefab, `m_amount`/`m_amountPerLevel`, and the idol's `m_upgradeChance`/`m_breakChance`/`m_breakReturnIngreientsAmount`.
* `ObjectDB.m_items` of `ItemType.Trinket` (with `m_maxAdrenaline`, `m_fullAdrenalineSE`, `m_equipStatusEffect`) and of `ItemType.Trophy` (with `m_trophyPos`).
* Every prefab with `CharacterDrop` and no trophy in `m_drops`.
* Container prefabs: `WearNTear.m_supports`, `m_width`×`m_height`, `m_privacy`.
