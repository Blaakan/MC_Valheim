# Cultivator Replant — design

| | |
|---|---|
| Mod | Cultivator Replant |
| GUID / project | `MC.Farming.Cultivator.Replant` (`src/Farming/Cultivator.Replant/`, root namespace `MC.Farming.CultivatorReplantMod`, package `CultivatorReplant`) |
| Category / scope | Farming / Revamp |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Cultivator revamp` |
| Game version checked | Valheim 1.0.16 (Unity 6000.0.75f1), decompiled `assembly_valheim`, `assembly_utils` and `assembly_guiutils` in `.ref/` (Player, Humanoid, Hud, ZInput, Localization, InventoryGui, Inventory, ItemDrop, Recipe, Piece, PieceTable, ObjectDB, ZNetScene, ZNetView, ZRoutedRpc, Pickable, Plant, TreeBase, Destructible, ResourceRoot, SapCollector, CraftingStation, StationExtension, PrivateArea, Location, Heightmap, Attack); prefab values read offline from the installed game's SoftRef bundles (2026-10-03, scratch tools built on AssetsTools.NET); research briefs of 2026-10-03 (crafting, plants, input, repo patterns, external mods, prefab data, critic pass); PlantEverything, PlantEasily, Groundwork, EarthWright (GPL-3.0) and RestlessPlant source read on GitHub for ideas and compatibility only |
| Status | Implemented (v0.1.0 code); review of 2026-10-03: 22 findings confirmed and fixed (section 9); smoke test passed 2026-10-03; in-world self-tests passed 2026-10-03 (all 7 `replant.*`; runs made with the test machine's third-party mods installed: Jotunn, Warfare, PlantEasily and others); not yet tested by hand in game |

## Goal

The user's expected behaviours (idea sheet "Cultivator revamp"; run request, 2026-10-03), numbered. Each one is scope
and has at least one test (section 8).

1. **G1 — Bronze cultivator replant.** The vanilla (bronze) Cultivator works as before, plus a **Replant** action:
   with the cultivator equipped, aim at a wild Dandelion, Raspberry bush, Blueberry bush, Mushroom, Yellow mushroom or
   Thistle and use the action. The plant comes out of the ground as a new item (a transplant) that can be planted
   elsewhere with the cultivator.
2. **G2 — Black metal cultivator.** Does everything the bronze one does, plus Cloudberry bushes. Made from the bronze
   cultivator plus 5 Black metal and 10 Linen thread.
3. **G3 — Eitr cultivator.** Adds Yggdrasil shoots. A Yggdrasil transplant only grows near an Ancient Root and drains
   the root's energy (sap) to grow. Made from the black metal cultivator plus 15 Refined eitr.
4. **G4 — Flametal cultivator.** Adds Fiddlehead and Smoke puff, which only grow in the Ashlands. Made from the eitr
   cultivator plus 5 Flametal.
5. **G5 — Bloodgold cultivator.** Adds Lingonberry bushes, which only grow in the Deep North. Made from the flametal
   cultivator plus 5 Bloodgold.

User decisions taken at the start of the run (2026-10-03):

- **Tiers are upgrade levels of the vanilla Cultivator** (not new items): the vanilla levels 1-3 are the bronze
  cultivator, levels 4, 5, 6 and 7 are the black metal, eitr, flametal and bloodgold cultivators, made in the Forge's
  Upgrade tab. The tool keeps its name; the tooltip names the tier and the icon gets a tier mark.
- **The Yggdrasil transplant drains the Ancient Root's sap** (the reserve the Sap Extractor uses), not the planter's
  eitr.

### Added beyond the request (small)

- **E1** Replant harvests a ripe plant first (berries, mushroom, thistle...), then digs it up: the produce is not lost.
- **E2** Replant also digs up any transplant sapling that has not grown yet (not only your own; wards protect it, as
  they protect wild plants) and gives the transplant back (to move a misplaced one).
- **E3** The cultivator's tooltip says its tier and what it can replant; an upgrade's preview says what the next level
  unlocks. Levels 4-7 get a coloured gem on the icon.
- **E4** Transplant items show the plant's produce icon with a small sprout mark, so they never look like the produce.
- **E5** Crosshair hint over a plant: "[E] Replant" or the tier needed; while placing a Yggdrasil transplant, a hint
  says how far from an Ancient Root it must be ("Plant 2 to 6 m from an Ancient Root").
- **E6** A Yggdrasil transplant's hover text shows the root it will draw from and its sap level.
- **E7** Server settings for the four upgrade costs, the grow time, the sap cost and the root range.

### Non-goals

New tool models; a different crafting station per tier; blue mushrooms and any plant not listed; planting gated by
tier (the transplant item is the gate); farming skill changes; tree saplings for the other trees.

### Later (cut from v1)

- Per-tier extras for planting (bigger PlantEasily-style grids or faster growth at higher tiers).
- Replanting other wild flora (blue mushrooms, Ashlands vines, decorative plants).
- A fern under replanted fiddleheads (`FernFiddleHeadAshlands`) for looks.

## 1. Vanilla behaviour

### 1.1 The Cultivator and its upgrades

- Prefab values (asset read): `Cultivator` `$item_cultivator`, Tool, `m_maxQuality` 3, durability 200 + 200 per level,
  `m_useDurabilityDrain` 1, `m_destroyBroken` 0, attack stamina 5, `m_buildPieces` = `_CultivatorPieceTable` (24
  pieces, `m_canRemovePieces` 0, `m_canRemoveFeasts` 0, skill Farming).
- `Recipe_Cultivator`: Forge, `m_minStationLevel` 1; RoundLog (Corewood) 5 +1/level, Bronze 5 +1/level, and the
  Wooden Battle Idol `Upgrader0Weapon` ×1 as an upgrader resource (Forge of Potential).
- `Piece.Requirement.GetAmount(q)`: q ≤ 1 → `m_amount`; else `floor(f(q) × m_amountPerLevel)` (+ `m_amount` for
  upgrader rows), f = q − 1 below 4, then 4, 4.5, 5, 5.5. Callers: `InventoryGui.SetupRequirementList` /
  `SetupRequirement` (0 hides the row), `Player.HaveRequirementItems` (0 always passes), `Player.ConsumeResources`
  (0 removes nothing; also called for pieces at quality 0), the Forge of Potential refund.
- `Recipe.GetRequiredStationLevel(q)` = `max(1, m_minStationLevel) + q − 1`. The Forge has six extensions, so it
  reaches level 7: quality 7 is possible at the Forge without a patch.
- Upgrade tab (`InventoryGui.UpdateRecipeList`): lists inventory items by name below the *instance's*
  `m_shared.m_maxQuality`; `DoCrafting` checks the *prefab's* `m_maxQuality`, unequips and removes the old item and
  adds a new one at quality + 1 in the same slot (empty `m_customData`, full durability).
- Forge of Potential (`CraftingStation.m_upgrader`): lists items of recipes with an upgrader requirement at any
  quality and lets them go past `m_maxQuality`; Forge Idol Upgrades replaces its refinement roll (`ForgeRefine`) and
  treats any foreign `bool` prefix or transpiler on `InventoryGui.DoCrafting` as a takeover (`ForgeGuard`).
- `SharedData`: items made by `Instantiate` (crafting, save/load, `AddItem(string…)`) carry their own copy of the
  prefab's `SharedData`; `AddItem(GameObject, int)` clones `ItemData` and shares it. Raising `m_maxQuality` on the
  prefab reaches items loaded later, not copies already alive (an item in a chest loaded earlier, or on the ground,
  keeps its copy; `Container.Load` re-reads only when the ZDO's data revision changes).
- `InventoryGui.DoCrafting` marks the result cheated when `Inventory.ItemCheated(recipe.m_resources)` finds a cheated
  inventory item whose name matches any requirement row, whatever that row's amount.
- `InventoryGrid.UpdateGui` shows the item's quality number in the slot's upper-right corner whenever
  `m_shared.m_maxQuality > 1`, and the stack amount ("5/20") centred along the slot's bottom edge (slot layout read
  from the game's UI prefab: `amount` 64 × 22 at the bottom, TextMeshPro size 15). The hotbar has no quality number;
  its key digit sits in the upper-left corner of every hotbar slot.

### 1.2 The plants

Prefab values (asset read, layers: 0 Default, 12 item, 15 static_solid, 16 piece_nonsolid, 20 Default_small):

| Prefab | Kind | Respawn (min) | Hide child | Colliders | Other |
|---|---|---|---|---|---|
| `Pickable_Dandelion` | forage | 240 | `visual` | item layer, on `visual` | yield 1 |
| `Pickable_Mushroom` | forage | 240 | `visual` | item layer, on `visual` | yield 1 |
| `Pickable_Mushroom_yellow` | forage | 240 | `visual` | item layer, on `visual` | yield 1, grows in dungeons |
| `Pickable_Thistle` | forage | 240 | `visual` | item layer, on `visual/default` | yield 1, scale synced |
| `Pickable_Fiddlehead` | forage | 300 | `visual` | item layer, on `visual` | yield 1 + 2 extra |
| `Pickable_SmokePuff` | forage | 240 | `visual` | item layer, on `visual` | yield 1 |
| `RaspberryBush`, `BlueberryBush` | bush | 300 | `Berrys` | Default_small capsule + piece_nonsolid sphere | Destructible 30, wood 1-2 |
| `CloudberryBush` | bush | 300 | `Berrys` | piece_nonsolid sphere | Destructible 30, no drop |
| `LingonberryBush` | bush | 300 | `Berries` | Default capsules | Destructible 30, wood 1-2 |
| `YggaShoot1/2/3` | tree | – | – | Default mesh | `TreeBase` 100 hp, tool tier 4 |
| `YggaShoot_small1` | shoot | – | – | Default capsule | `Destructible` (Tree) 20 hp, tool tier 4 |
| `YggdrasilRoot` | root | – | – | static_solid mesh | `ResourceRoot`: max 50, regen 0.0025/s (9/h), high 40, low 5 |

- **All forage regrows in vanilla** (respawn 240-300 min with a hidden child): a planted copy of the vanilla prefab
  regrows with no patch. The earlier backlog note that they are one-shot was wrong.
- A picked forage plant cannot be targeted: its only collider is on the hidden `visual` child. Picked bushes can.
- Wild vegetation is placed once per zone (`ZoneSystem.SpawnZone`); a removed wild plant never comes back.
- `Pickable.Interact` → `RPC_Pick` on the owner (drops the yield, `RPC_SetPicked`). Once this machine owns the ZDO the
  routed call runs synchronously. `ZNetScene.Destroy` clears the ZDO first (no `Destructible` drops) and destroys the
  ZDO only on the owner.

### 1.3 Saplings and growth (`Plant`)

- `Plant.SUpdate` runs on every SlowUpdater pass; once `TimeSincePlanted > GetGrowTime()` the owner calls `Grow()` on
  every pass. `Grow` on a non-Healthy plant does nothing or destroys it (`m_destroyIfCantGrow`); otherwise it
  instantiates a random `m_grownPrefabs` entry, sets a random scale (saved only with `m_syncInitialScale`), calls
  `TreeBase.Grow`, and destroys the sapling.
- `UpdateHealth`: WrongBiome (`m_biome`), NotCultivated (`m_needCultivatedGround`), TooHot (Ashlands without
  `m_tolerateHeat` or a shield), TooCold (Mountain / Deep North without `m_tolerateCold` or a shield), NoSun
  (`HaveRoof`), NoSpace (`HaveGrowSpace`: any non-`Plant` collider within `m_growRadius` on Default, static_solid,
  Default_small, piece, piece_nonsolid).
- The scythe destroys any non-Healthy `Plant` it touches, so a waiting sapling must stay Healthy.
- Grow times are per instance: `Instantiate` copies the prefab's `m_growTime` / `m_growTimeMax`, `Plant.GetGrowTime`
  reads the instance's fields, and `Plant.SUpdate` never grows a plant in its first 10 s after `Awake`. Growth runs on
  `ZNet` world time (it jumps ahead on sleep and stands still while the world is not running).
- Placement (`Player.UpdatePlacementGhost`): `m_onlyInBiome`, `m_cultivatedGroundOnly`, `m_vegetationGroundOnly`
  (in the Ashlands: not on lava), `m_allowedInDeepSnow`, and `m_mustConnectTo` (an `OverlapSphere` of
  `m_connectRadius` that accepts any parent `ZNetView` whose name contains the target prefab's name; otherwise
  status Invalid). Nothing re-checks the connection during growth.
- Templates (asset read): `Beech_Sapling` (tree sapling: ground only, Tree-type Destructible, no monster targeting,
  no grown visuals), `Birch_Sapling`, `sapling_magecap` and `sapling_carrot` (crop saplings: cultivated ground only,
  monsters target them, grown visuals at 50%).

### 1.4 Input in build mode

- With a build tool, `Player.UpdateHover` clears `m_hovering`, so the Use key (E) does nothing; the gamepad X button is
  `JoySit` (ignored in build mode) in the Default layout and `JoyUse` (no hover, no effect) in the alternative
  layouts. The gamepad-mouse context (`ZInput.IsGamepadMouseActive`) binds X to `JoyUse`, which there toggles the
  build menu, but it is the Switch Joy-Con mouse layout: the PC build never reaches it (`ZInput.m_inputSource` is
  never GamepadMouse; `GetActiveInputSource` returns only Gamepad or KeyboardMouse).
- At a ship's helm or on a lox saddle (`m_doodadController` set, `hideWeapons: false`) the player can still equip the
  cultivator and be in build mode; with nothing hovered, the Use press then calls `StopDoodadControl` (lets go).
- `Player.UpdatePlacement` reads input after `UpdateBuildGuiInput`; removal fires on the Remove *release*; nothing
  can be removed with the vanilla cultivator (both remove flags 0).
- `Hud.UpdateCrosshair` blanks `m_hoverName` in build mode.

## 2. Design

### 2.1 Plant table (`PlantCatalog`)

| Key | Tier (min quality) | Wild prefabs dug up | Grows into | Transplant cloned from (model, icon) | Sapling template | Grow (min) | Where it grows |
|---|---|---|---|---|---|---|---|
| `Dandelion` | 1 (bronze) | `Pickable_Dandelion` | same | `Dandelion` | `sapling_carrot` | 240 | land (*) |
| `Thistle` | 1 | `Pickable_Thistle` | same | `Thistle` | `sapling_carrot` | 240 | land (*) |
| `Mushroom` | 1 | `Pickable_Mushroom` | same | `Mushroom` | `sapling_magecap` | 240 | land (*) |
| `MushroomYellow` | 1 | `Pickable_Mushroom_yellow` | same | `MushroomYellow` | `sapling_magecap` | 240 | land (*) |
| `RaspberryBush` | 1 | `RaspberryBush` | same | `Raspberry` | `Beech_Sapling` | 300 | land (*) |
| `BlueberryBush` | 1 | `BlueberryBush` | same | `Blueberries` | `Beech_Sapling` | 300 | land (*) |
| `CloudberryBush` | 4 (black metal) | `CloudberryBush` | same | `Cloudberry` | `Beech_Sapling` | 300 | land (*) |
| `YggaShoot` | 5 (eitr) | `YggaShoot_small1`, `YggaShoot1`, `YggaShoot2`, `YggaShoot3` | `YggaShoot1`, `YggaShoot2`, `YggaShoot3` | `YggdrasilWood` | `Birch_Sapling` | 120 | near an Ancient Root (§2.6) |
| `Fiddlehead` | 6 (flametal) | `Pickable_Fiddlehead` | same | `Fiddleheadfern` | `sapling_carrot` | 300 | Ashlands only, tolerates heat, not on lava |
| `SmokePuff` | 6 | `Pickable_SmokePuff` | same | `MushroomSmokePuff` | `sapling_magecap` | 240 | Ashlands only, tolerates heat, not on lava |
| `LingonberryBush` | 7 (bloodgold) | `LingonberryBush` | same | `Lingonberry` | `Beech_Sapling` | 300 | Deep North only, tolerates cold, deep snow allowed |

(*) `Heightmap.Biome.Land` with neither tolerance and no cultivated ground: on open ground in any land biome, no
tilling needed; in the Ashlands (too hot), the Mountain and the Deep North (too cold) only inside a shield, by
vanilla's heat and cold rules for every `Plant`. Item and piece text: "Grows on open ground in any land biome, no
tilling needed; in the Ashlands, the Mountains and the Deep North only inside a shield." (Vanilla crops are narrower:
they need cultivated ground and most have Meadows, Black Forest, Plains and Ashlands only.)

- Transplant item prefab `MC_Transplant_<Key>`, sapling prefab `MC_Sapling_<Key>` (permanent after release).
  Display names: item and piece "`<plant>` transplant" ("Raspberry bush transplant", "Yggdrasil shoot transplant").
- Grow time = the table value (the plant's own regrowth time for bushes and forage) × `GrowTimeMultiplier`, with
  `m_growTime` = 0.9× and `m_growTimeMax` = 1.1× (seconds). Scales: forage 1-1 (forage prefabs do not sync scale),
  bushes 0.9-1.1, Yggdrasil 0.8-1.2. Grow radius: forage 0.5, bushes 1.0, Yggdrasil 2.0.

### 2.2 Transplant items and saplings (`TransplantContent`)

- **Items** (built once per session from the first `ObjectDB` that has every source item; also from `ZNetScene` on a
  dedicated server): `Object.Instantiate(produce)` under an inactive `DontDestroyOnLoad` holder (Spyglass pattern),
  renamed; `SharedData` edited: `m_name` / `m_description` plain English, `m_itemType` Material, every food field 0,
  `m_consumeStatusEffect` null, `m_maxStackSize` 20, `m_weight` 0.5, `m_teleportable` true, `m_value` 0,
  `m_icons` = [produce icon + sprout mark] (§2.5; the produce icon on a dedicated server or when painting fails).
  Registered idempotently in `ObjectDB` (`m_items`, `m_itemByHash`, `m_itemByData`) and `ZNetScene`. Always on
  (`[AlwaysOnPatch]`): items never vanish while the mod is installed.
- **Saplings** (built once from `ZNetScene`, which has the templates and the grown prefabs): clone the template,
  rename; `Plant`: `m_name`, `m_grownPrefabs`, grow times, scale, `m_growRadius`, `m_biome`, `m_tolerateHeat`,
  `m_tolerateCold`, `m_needCultivatedGround` false, `m_destroyIfCantGrow` false (a misplaced transplant waits);
  `Piece`: `m_name`, `m_description`, `m_icon` (the transplant icon), `m_resources` = [transplant ×1, not
  recovered], `m_cultivatedGroundOnly` false, `m_groundOnly` true, `m_onlyInBiome`, `m_vegetationGroundOnly`,
  `m_allowedInDeepSnow`, `m_primaryTarget` / `m_randomTarget` false, `m_canBeRemoved` false, Yggdrasil:
  `m_mustConnectTo` = `YggdrasilRoot`'s `ZNetView`, `m_connectRadius` = `RootRange`. Registered in `ZNetScene` from a
  postfix ordered after PlantEverything's (`[HarmonyAfter("advize.PlantEverything")]`), so its "modded plant"
  rewrite never sees them. Added once to the cultivator's piece table (the `Cultivator` prefab's `m_buildPieces`).
- **Toggle:** `Piece.m_enabled` = feature active and server rules in force; the local player's build menu is refreshed
  (`Player.UpdateAvailablePiecesList`). Saplings already in the ground keep growing while the feature is off (vanilla
  `Plant`), Yggdrasil ones without draining sap.
- **Rebuild** (`Rebuild`, debounced 0.5 s after a rules change, Spyglass pattern): grow times, `m_connectRadius`,
  piece enablement, then `CultivatorTiers.Apply()`. When enabled it also rewrites `m_growTime` / `m_growTimeMax` of
  the saplings already loaded (walks `ZNetScene.instance.m_instances`, matches the ZDO prefab with
  `PlantCatalog.TryFind` and `isSapling`), so a rules change reaches planted transplants at once, not only saplings
  instantiated later.
- **Pending rules:** while a client waits for the server's rules, the `Plant.Grow` prefix holds every MC sapling
  (`__result` null, return false; a hash-set check on the ZDO prefab). A sapling instantiated in that window still
  carries the prefab's own-config grow time, but it cannot grow before the rules arrive, and the Rebuild that follows
  them rewrites its times (vanilla never grows a plant in its first 10 s anyway).

### 2.3 Tiers (`CultivatorTiers`)

- Active and rules in force: the `Cultivator` prefab's `m_maxQuality` = 7, and `Recipe_Cultivator.m_resources` = a
  **new array**: the vanilla rows plus one row per material of each tier cost (default black metal `BlackMetal:5,
  LinenThread:10`, eitr `Eitr:15`, flametal `FlametalNew:5`, bloodgold `Gold:5`), each with `m_amount` 0,
  `m_amountPerLevel` 1 (so Compendium lists the Cultivator as a use), `m_recover` false.
- `Piece.Requirement.GetAmount` postfix, keyed by requirement object reference (never by item): a tier row returns its
  amount at its quality and 0 otherwise; the vanilla Bronze and Corewood rows return 0 from quality 4 up. The idol
  (upgrader) row returns 0 from quality 2 up (quality 1 stays vanilla): only a Forge of Potential reads it, and that
  station does not list the Cultivator, so this only keeps the Encyclopedia from showing an idol per level. Recipe
  discovery reads `m_amount`, not `GetAmount`, and the upgrader check of `UpdateRecipeList` only reads the flag, so
  neither changes.
- Off, or rules pending: `m_maxQuality` and `m_resources` back to the captured vanilla values (otherwise vanilla
  `GetAmount(4)` would sell a level 4 for 4 Bronze + 4 Corewood); every `GetAmount` answer is vanilla again.
- Stale copies: an always-on (`[AlwaysOnPatch]`, own class) `InventoryGui.UpdateRecipeList` prefix sets
  `m_shared.m_maxQuality` of every cultivator in the player's inventory to the prefab's value (cheap: a reference
  walk), in both directions: the Upgrade tab lists a level-3 cultivator after a live toggle on, and while the feature
  is off a cultivator picked up later from a chest or the ground (a copy made while the tiers were on, still at 7) is
  healed back to 3, so the tab never offers "Upgrade Cultivator to 4" at the vanilla price. It needs no config or
  feature state, and is a no-op on an unmodded save.
- Re-sync: recipe-config mods may replace `Recipe_Cultivator.m_resources` after our `Apply` (a ServerSync push on
  connect, an `ObjectDB.Awake` postfix that runs after ours, a live reload). Their new requirement objects are not
  keys of our rows, so levels 4-7 would fall back to the vanilla formula. `CultivatorTiers.Resync()` (called from the
  always-on `UpdateRecipeList` prefix before the heal, and from the `ZNet.Update` postfix: two field reads) runs
  `Apply()` again when the tiers are in force and the recipe's array is not ours: the foreign array becomes the base
  (it prices levels 1-3; its rows read 0 from level 4) and the tier rows go on top again. A different `m_maxQuality`
  alone never triggers it, so mods that rewrite the levels do not start a tug of war on every refresh (section 6.2).
- Other recipes whose item is the Cultivator (added by other mods): while the tiers are in force, the
  `AddRecipeToList` prefix drops their Upgrade-tab rows for a cultivator at level 3 or more, so levels 4-7 are only
  made with the tier materials. Their Craft-tab rows stay.
- Forge of Potential: the same `InventoryGui.AddRecipeToList` prefix (a feature patch) drops the Cultivator's rows
  while the station is an upgrader (`m_upgrader`), so an idol cannot push a cultivator into a tier. No `bool` prefix
  on `DoCrafting`.
- Cheated items (accepted, documented in the README): the tier rows are in the one array used at every quality, and
  `Inventory.ItemCheated` ignores amounts, so while the tiers are on, carrying a cheat-spawned stack of a tier
  material marks a level 1-3 cultivator crafted or upgraded with it as cheated, as vanilla does for any material in an
  item's recipe. The next upgrade made without cheated materials clears it.
- Station: the Forge, level = quality (vanilla formula). No patch.
- Tooltip (static `ItemData.GetTooltip` postfix): adds "Tier: Black metal cultivator" and "Replants: …" for the
  item's quality, or, in the crafting panel, for the target quality.
- Tier of a cultivator = its quality (a cultivator refined past 3 before install counts as that tier).

### 2.4 Replant (`Replant`, `ReplantHint`)

- Trigger: `Player.UpdatePlacement` prefix (High priority), local player only, `takeInput`, build mode, right-hand
  item is the cultivator (`m_shared.m_buildPieces` is the cultivator's table), piece menu closed, no radial menu, no
  input delay, not attacking or dodging. Not at a ship's helm or on a lox saddle: a `Player.UpdateHover` postfix marks
  every frame with a doodad controller, and the frame the player lets go (vanilla's Use block clears the controller
  before `UpdatePlacement` runs); in a marked frame no target is set, so there is no hint, no yellow crosshair and no
  press is consumed (E stays vanilla's: it lets go of the helm).
- Target: vanilla `Player.FindHoverObject(out go, out _)` (first hit of `m_interactMask` within reach) →
  `GetComponentInParent<ZNetView>()` → ZDO prefab hash → `PlantCatalog` (wild prefab or any of our saplings, whoever
  planted it). Stored per frame for the hint.
- Keys: keyboard `Use` (E, follows rebinding). Gamepad `JoyButtonX` (X) without `JoyAltKeys` held, in every layout
  (X is free in build mode on PC: section 1.4; the gamepad-mouse context where X opens the build menu is
  Switch-only). The pressed button is reset (`ZInput.ResetButtonStatus`), also when the action is refused, so no
  other reader sees it. The default E is also `TabRight` (build mode "cycle snap point", read in
  `Player.UpdatePlacementGhost`): when it was pressed in the same frame it is reset too.
- Checks, before any side effect: tier (center message "Needs a black metal cultivator (level 4)"), rules pending
  (refused), ward (`PrivateArea.CheckAccess`, flashes, `$msg_privatezone`), stamina (`HaveStamina(attack
  stamina)`, flash). **No no-build-zone check** (D4).
- Action: `ClaimOwnership`; if a `Pickable` that `CanBePicked`, `Interact(player, false, false)` (E1, synchronous now);
  if the view is still valid, `ZNetScene.Destroy(nv.gameObject)`; give one transplant (inventory, or dropped at the
  plant with `$msg_noroom` when full; `ItemDrop.OnCreateNew`); top-left "added" message; then the vanilla removal
  costs and feedback: `m_lastToolUseTime`, clear `m_placePressedTime` / `m_removePressedTime`, `FaceLookDirection`,
  the tool's attack animation trigger, `AddNoise(50)`, `UseStamina(GetBuildStamina())`, durability −=
  `GetPlaceDurability(tool) × Game.m_durabilityRate`, the tool's destroy effect and the player's remove effects. No
  Farming raise and no build-remove debt.
- Hint (`Hud.UpdateCrosshair` postfix, only when vanilla left `m_hoverName` empty): "`<plant name>`\n[E] Replant"
  (gamepad: vanilla's bracket stripping and the glyph of X; when `GetBoundKeyString("JoyButtonX")` is empty, the
  glyph of the button X is bound to in the current layout, `JoySit` in the classic Default layout and `JoyUse` in the
  alternative layouts, never the keyboard key), or "`<plant name>`\nNeeds a `<tier>` cultivator (level N)". Crosshair
  yellow when allowed. Cache by (kind, sapling, allowed, gamepad), cleared on `ZInput.OnInputLayoutChanged` and
  `Localization.OnLanguageChange`. With no target and the Yggdrasil transplant selected for placement: "Plant G to N m
  from an Ancient Root" (G = the Yggdrasil kind's `GrowRadius`, 2; N = `RootRange`).

### 2.5 Icons (`IconPainter`, `TierIcons`)

- `IconPainter.Compose`: copy a sprite out of its (non-readable) atlas through a `RenderTexture` and `ReadPixels`,
  paint on the CPU, `Sprite.Create` (Forge Idol Upgrades' `StarIcons.Compose` adapted: colour space, empty-copy
  retry, refuses tight-packed or rotated sprites). Main thread, graphics device only, cached by key, never destroyed.
- Transplant icon (E4): produce icon + a small soil mound with a green sprout in the upper-right corner. That corner
  is always empty on a transplant slot: no quality number (the cloned produce has `m_maxQuality` 1), no no-portal icon
  (teleportable), no food icon (a Material), and the hotbar slot has none of these. The lower-right corner is not
  used: the stack amount ("5/20") runs along the slot's bottom (section 1.1). The Debug package-icon export keeps its
  own layout (a package icon shows no amount).
- Cultivator tiers (E3): `ItemData.GetIcon` postfix (quality ≥ 4 and the item is the cultivator) returns the vanilla
  icon with a gem in the upper-right corner: black metal dark grey, eitr cyan, flametal orange, bloodgold crimson.
  `IconInlineGuard` (copied from Forge Idol Upgrades) pins `GetIcon` as not inlined. The recipe panel's large icon
  and recipe rows read `m_icons` directly and stay vanilla.
- The gem sits where the inventory grid prints the quality number, so an `InventoryGrid.UpdateGui` postfix (feature
  patch, like Forge Idol Upgrades' for idols) hides that number for cultivators at quality ≥ 4 and sets the gem sprite
  again as an inline backup: levels 4-7 show a gem instead of the level number (the tooltip names level and tier),
  levels 1-3 keep their number, and turning the feature off brings the number back.

### 2.6 Yggdrasil transplant and the root (`RootGate`)

- Placement: `m_mustConnectTo` = the root (`YggdrasilRoot`), `m_connectRadius` = `RootRange` (default 6 m): the ghost
  is red away from a root (status Invalid; the hint of §2.4 explains). Vanilla grow rules still apply: closer to the
  root mesh than `m_growRadius` (2 m) is "Needs more room to grow", a root arch overhead is "Needs an open sky".
  Both checks query the same static_solid root mesh from the same point, so the usable ring is `m_growRadius` to
  `RootRange`: `RootRange` starts at 4 (`RootRangeMin`, the 2 m grow radius plus a usable 2 m band; at 2 nothing
  could grow). The texts give both limits ("Plant 2 to 6 m from an Ancient Root"; item text "within a few metres of
  an Ancient Root, but not right against it"). No too-close check on the ghost: the vanilla ghost stays green right
  against the root, the young plant then says "Needs more room to grow", and Replant gives it back.
- `Plant.Grow` prefix (feature patch), keyed by the ZDO prefab hash: while the rules are pending it holds every MC
  sapling (§2.2). Otherwise only `MC_Sapling_YggaShoot`, and only when Healthy (else vanilla runs): at most one
  attempt every 10 s per sapling (a `RootDrawer` component on the prefab keeps the next attempt time and the cached
  root). Finds the root: cached, else `Physics.OverlapSphereNonAlloc(pos, RootRange + 1)` on the layers of the root
  prefab's colliders only (`RootGate.RootMask`, built when the saplings are built: static_solid, which is also the
  fallback), then the nearest `ResourceRoot` in parents. Searching one layer keeps building pieces, items and
  triggers out of the hit buffer, so a busy base cannot crowd the root out; if the buffer (up to 1024 hits) still
  fills, one log line says so. No root, or too little sap → skip `Grow` (`__result` null, return false): the sapling
  stays Healthy and tries again. Else `root.Drain(RootSapCost)` (RPC to the root's owner) and let `Grow` run.
- Sap check with a per-peer ledger: `CanDrain` / `Drain` read this peer's copy of the root ZDO, and when another peer
  owns the root, `Drain` is only sent; the owner's `RPC_Drain` re-checks and silently skips a drain the level cannot
  pay. So this peer keeps, per root, all the sap it has drained but not yet seen leave the root's level (several
  drains add up): the check (and the hover's "waiting for it to refill") uses the effective level, the root level
  minus that pending sap, and the entry clears once the drop shows in the root's level, or after a timeout (a refused
  or lost drain). Several saplings of one player (often due in the same SlowUpdater pass after a zone loads) therefore
  never share one drain. When this peer owns the root the drain is synchronous and exact.
- Known limit: saplings owned by two different players at one root can still both grow on one drain (each peer's
  check passes on its own stale copy; the owner refuses the second drain, and the second sapling grows anyway). An
  exact fix would need a routed request and grant (root owner drains, then tells the sapling owner to grow) and a
  network version bump; not done.
- `Plant.GetHoverText` postfix (our Yggdrasil sapling): "Draws 20 sap from an Ancient Root when grown (root: 34 / 50)"
  or "Needs an Ancient Root within 6 m".
- Default cost 20: the root holds 50 and regains 9 per hour, so one tree costs about 2 h 15 min of root regeneration
  (about 20 Sap an extractor would have made). `CanDrain` is `level > cost`, so the cost range stops at 49.

## 3. Decisions

- **D1 Tiers as upgrade levels 4-7** (user decision): keeps `$item_cultivator`, which PlantEasily and
  PlantEverything need; an uninstall leaves a working high-level cultivator. Cost: one name and model for all tiers
  (tooltip and icon gem show the tier).
- **D2 Station:** every tier at the Forge, station level = quality (4-7). The Forge reaches 7 with its six extensions;
  no station patch, the UI and Compendium stay right.
- **D3 Forge of Potential:** no longer lists the Cultivator while the mod is on (otherwise one idol would skip a tier's
  cost). A cultivator refined past level 3 before install counts as that tier.
- **D4 No-build zones do not block Replant** (picking is not blocked there either, and yellow mushrooms grow in
  dungeons, which are no-build locations). Wards do block it.
- **D5 Grow time = the plant's own regrowth time** (bushes 5 h, forage 4 h, fiddlehead 5 h, Yggdrasil 2 h of play, on
  the same world clock as crops and berry regrowth: sleeping skips ahead, and nothing grows while the world is closed)
  and the grown plant is ripe: moving a plant never yields more than leaving it (uproot gives the produce, the
  replanted plant gives the next one when it would have regrown anyway). `GrowTimeMultiplier` changes it.
- **D6 Planting is not tier-gated**: the transplant item is the gate; transplants can be traded.
- **D7 Sap drain once, when the sapling is ready to grow**; it waits while the root has too little.
- **D8 Keys:** E and gamepad X (free in build mode in every PC layout). The gamepad-mouse context, where X opens the
  build menu, is the Switch Joy-Con mouse layout and never happens on PC, so there is no second gamepad key.
- **D9 One Yggdrasil transplant** for the small shoot and the three tree prefabs; it grows into a tree
  (`YggaShoot1-3`).
- **D10 Biomes:** bronze and black metal plants grow on open ground in any land biome with no tilling, under
  vanilla's heat and cold rules (a shield in the Ashlands, the Mountain and the Deep North); fiddlehead
  and smoke puff only in the Ashlands (tolerate heat, not on lava); lingonberry only in the Deep North (tolerates
  cold, deep snow allowed); Yggdrasil wherever a root is in range.
- **D11 Costs like a vanilla removal**: the tool's stamina and durability; no Farming skill (planting raises it).
- **D12 Uproot gives no wood** (destroyed through `ZNetScene`, not `Destructible`).

## 4. Multiplayer

### 4.1 Who runs each piece

- Replant: the digger's game (local player), claiming the plant's ZDO first, like vanilla removal.
- Growth and the root drain: the sapling's owner (usually a nearby player); `Drain` is applied by the root's owner.
- Upgrades: the crafting player's game.
- Rules: the server (or host) sends its own; clients use them.

### 4.2 RPCs, ZDO keys, network version

RPCs `<guid>.Settings` / `<guid>.SettingsRequest` (`CultivatorRules` layout 1). Prefabs `MC_Transplant_<Key>`,
`MC_Sapling_<Key>`. No ZDO keys of our own. Network version 1.

### 4.3 Server settings and join check

Copied from Spyglass (`ServerRules`, `PlayerCheck`, `Patches/ZNetPatches.cs`): the server refuses players without the
mod, with it off or with another network version after a 1 s grace (`AllowPlayersWithoutMod` false by default) and
pushes its rules; clients wait for them (pending: pieces hidden, tiers vanilla, Replant refused, no MC sapling grows,
roots never drained). On a server without the mod the framework turns the feature off for the session (the server
never answers the handshake), so the client plays as with the feature off (§2.2): no Replant, sapling pieces hidden,
tiers vanilla, and transplant saplings its game runs grow by vanilla rules.

### 4.4 Hand-off cases

- A transplant item given to a player without the mod (only with `AllowPlayersWithoutMod`): it vanishes from their
  inventory at the next load; in a chest they touch, it is lost.
- A sapling near a player without the mod: an unknown prefab for them (not shown); if they own the zone it does not
  grow until a player with the mod owns it.
- A player who has the mod but turned it off, or another network version (only with `AllowPlayersWithoutMod`): the
  items and saplings are registered by always-on patches, so their game knows them: they see transplant saplings,
  keep transplant items, and chests they touch keep them. But the feature patches are off there: transplant saplings
  in areas their game runs grow as plain vanilla plants, on that player's own prefab grow time (not the server's
  `GrowTimeMultiplier`), and Yggdrasil ones grow without an Ancient Root and without taking sap (also PlantEasily
  copies placed away from a root). Accepted and documented (README, `AllowPlayersWithoutMod` description, the
  server's "allowed" warning picks its sentence per `NetworkGate.PeerProblem`); an always-on growth gate would break
  the framework rule that only registration stays on, and the single-player toggle behaviour of §2.2. With the
  default setting such a player is refused about a second after turning the mod off; a due sapling they run may
  still grow by vanilla rules in that window (accepted race).
- A grown plant: a vanilla prefab, everyone sees and picks it.
- A level 4-7 cultivator in the hands of a player without the mod: a working vanilla cultivator with more durability.
- A server or host without the mod: on the server, `ZNetScene.CreateObjectsSorted` / `CreateDistantObjects` destroy
  every ZDO with an unknown prefab that comes into its active area ("Destroyed invalid prefab ZDO",
  docs/game/core-engine.md §2). A host or single-player game does this around its player; a dedicated server pins its
  reference position far outside the world and never instantiates world objects (core-engine.md §15), so it keeps
  them. A player with the mod on a vanilla host cannot plant (the feature is off there, §4.3) but can still drop transplants, which
  the host deletes once they are in its area. Hence the line in `ModMultiplayerNotes`: "On a server without the mod,
  transplants dropped on the ground can be deleted."
- Uninstall: the same rule. In single player or as host, young transplants and dropped transplants are deleted for
  good the first time their area loads without the mod (starting where the player loads in); those in areas not
  loaded since stay in the save. A dedicated server keeps them until the mod is installed again.

### 4.5 Dedicated server

Items and saplings are registered from `ZNetScene` (no icons painted); rules come from the server's config.

## 5. Config

| Section | Key | Default | Range | Scope |
|---|---|---|---|---|
| General | Enabled | true | | own |
| General | AllowPlayersWithoutMod | false | | server only |
| Upgrades | BlackMetalLevel | `BlackMetal:5,LinenThread:10` | text | server wins |
| Upgrades | EitrLevel | `Eitr:15` | text | server wins |
| Upgrades | FlametalLevel | `FlametalNew:5` | text | server wins |
| Upgrades | BloodgoldLevel | `Gold:5` | text | server wins |
| Growing | GrowTimeMultiplier | 1 | 0.1-10 | server wins |
| Growing | RootSapCost | 20 | 1-49 | server wins |
| Growing | RootRange | 6 | 4-20 m (§2.6) | server wins |

A tier cost with no valid material would make that level free, so the cultivator's maximum level stops below it
(and below every level above it), with one warning in the log.

## 6. Compatibility

### 6.1 Other MC mods

- **Forge Idol Upgrades:** no `bool` prefix on `DoCrafting`; the Cultivator is hidden at the Forge of Potential, so its
  refinement never touches it.
- **Crafting Search & Sort:** reads `GetAmount`, sees our rows at their level; the new `m_resources` array rebuilds its
  cache.
- **Encyclopedia (Compendium):** lists the transplant items and saplings (plain names, icons), the upgrade rows per
  level and the Forge level per level. The idol row reads 0 from quality 2 while the tiers are on (§2.3), so no level
  shows a Wooden Battle Idol at an upgrade station. Two lines stay wrong while the mod is on (the Forge of Potential
  hides the Cultivator, D3): the Cultivator's page still says "Beyond quality 7: at an upgrade station only"
  (Compendium sets it from any upgrader row in the recipe), and the idol's "Used in" still lists the Cultivator (it
  reads `m_amount`, 1). Removing them would need the idol row out of the recipe, which makes vanilla log "has no
  upgrader resource" at a Forge of Potential and changes recipe discovery, or a Compendium change; not worth it.
- **AutoPickup Filter:** produce picked by Replant counts as hand-picked; a transplant dropped because the inventory
  is full falls under its filter.
- **Spyglass:** never in the right hand at the same time as the cultivator.

### 6.2 External mods

| Mod | Effect |
|---|---|
| PlantEverything | Its saplings and ours share the cultivator table. It takes the Remove button for flora; we use E / X. Our saplings are registered after its "modded plant" pass. With it, wild bushes carry a `Piece`; Replant still works (prefab hash). |
| PlantEasily | Recognises the cultivator (same name). Its grid can place Yggdrasil transplants away from a root (they wait; Replant gives them back). |
| Groundwork, EarthWright | Their own cultivator removal of wild pickables: no conflict, both remove the plant. Replanted bushes count as wild for them. |
| Recipe-config mods (WackysDatabase, RecipeCustomization) | They may replace `Recipe_Cultivator.m_resources` after our `Apply` (on connect, on reload). The re-sync (§2.3) adopts their array as the base: their rows price levels 1-3, and the tier rows are added again on top, also when they swap it while playing. A second Cultivator recipe they add crafts new cultivators and upgrades up to level 3 only (its Upgrade rows for level 3+ are dropped while the tiers are on). |
| EarthWright, MoreBettererUpgrades, FurtherUpgrades | Rewrite the cultivator's levels: the last writer wins. Not supported together. A different `m_maxQuality` alone does not trigger the re-sync, so there is no tug of war on every refresh. |

## 7. Implementation plan

### 7.1 Files

Root namespace `MC.Farming.CultivatorReplantMod`. Templates: Spyglass (`src/Exploration/View.Spyglass`) for the
plugin, rules, join check, content registration and self tests; Forge Idol Upgrades for `StarIcons` /
`IconInlineGuard`.

| File | Owns | API used by other files |
|---|---|---|
| `Plugin.cs` | config, life cycle | `Plugin.FeatureActive`; config entries `AllowPlayersWithoutMod`, `BlackMetalLevel`, `EitrLevel`, `FlametalLevel`, `BloodgoldLevel`, `GrowTimeMultiplier`, `RootSapCost`, `RootRange`; Debug `Plugin.TestInactive`. `BindConfig` calls `IconInlineGuard.Apply()`. `OnActivated`: `ServerRules.Start()`, `PlayerCheck.Start()`, `TransplantContent.Rebuild()`, `SelfTests.Register()`. `OnDeactivated` (each step guarded): `SelfTests.Unregister()`, `Replant.Reset()`, `ReplantHint.Reset()`, `TransplantContent.Rebuild()`, `PlayerCheck.Stop()`, `ServerRules.Stop()`. Rules changed → `TransplantContent.RequestRebuild()`. |
| `CultivatorRules.cs` | rule snapshot, wire | fields `BlackMetalLevel`, `EitrLevel`, `FlametalLevel`, `BloodgoldLevel` (string), `GrowTimeMultiplier` (float), `RootSapCost` (int), `RootRange` (float); `string CostOf(int quality)` (4-7, else ""); `IsPending`; `Default`, `Pending`, `Own()`, `Write`, `TryRead`, `Describe()`; consts for ranges. |
| `ServerRules.cs`, `PlayerCheck.cs`, `Patches/ZNetPatches.cs` | Spyglass copies | `ServerRules.Current` (never null), `ServerRules.Changed`, `ServerRules.OwnChanged()`, Debug `TestRules` / `TestPending`; `PlayerCheck.Decide`, `ScheduleAllConnected` (the "allowed" warning picks its sentence per `NetworkGate.PeerProblem`: without the mod vs turned off or another version); `ZNet.Update` postfix calls `TransplantContent.UpdatePendingRebuild()` and `CultivatorTiers.Resync()`. |
| `PlantCatalog.cs` | the plant table of §2.1 | `sealed class PlantKind` (fields of the table: `Key`, `DisplayName`, `Tier`, `WildPrefabs`, `GrownPrefabs`, `ProduceItem`, `SaplingTemplate`, `GrowMinutes`, `MinScale`, `MaxScale`, `GrowRadius`, `Biome`, `OnlyInBiome`, `TolerateHeat`, `TolerateCold`, `AllowedInDeepSnow`, `VegetationGroundOnly`, `NeedsRoot`; derived `ItemName`, `SaplingName`, `ItemDisplayName`, `ItemHash`, `SaplingHash`); `static IReadOnlyList<PlantKind> All`; `static bool TryFind(int prefabHash, out PlantKind kind, out bool isSapling)`; `static PlantKind ByKey(string)`; `const int FirstNewTier = 4, MaxTier = 7, VanillaMaxQuality = 3`; `static string TierName(int quality)` ("bronze", "black metal", "eitr", "flametal", "bloodgold"); `static string TierTitle(int quality)` ("Black metal cultivator"…); `static IEnumerable<PlantKind> UnlockedAt(int quality)`, `static IEnumerable<PlantKind> NewAt(int quality)`. |
| `TransplantContent.cs` | items, saplings, table, toggle | `RegisterInObjectDB(ObjectDB)`, `RegisterInZNetScene(ZNetScene)`, `Rebuild()`, `RequestRebuild()`, `UpdatePendingRebuild()`, `RebuildPending`; `GameObject ItemPrefab(PlantKind)`, `GameObject SaplingPrefab(PlantKind)`, `bool ItemsBuilt`, `bool SaplingsBuilt`, `PieceTable CultivatorTable`, `GameObject CultivatorPrefab`; `PlantKind KindOfItem(ItemDrop.ItemData)`. The one cultivator check everywhere is `CultivatorTiers.IsCultivator` (by `m_shared.m_name`, as PlantEasily and PlantEverything do). `RegisterInObjectDB` also calls `CultivatorTiers.Capture(db)`; `Rebuild` also rewrites the grow times of loaded sapling instances (when enabled) and ends with `CultivatorTiers.Apply()`; building the saplings sets `RootGate.RootMask` from the root prefab's collider layers. |
| `Patches/ObjectDBPatches.cs`, `Patches/ZNetScenePatches.cs` | `[AlwaysOnPatch]` postfixes | call `TransplantContent`; `ZNetScene.Awake` postfix carries `[HarmonyAfter("advize.PlantEverything")]`. |
| `CultivatorTiers.cs` | recipe rows, max quality, amounts, Forge of Potential, tooltip | `Capture(ObjectDB)`, `Apply()`, `Resync()` (re-apply when the recipe's array was swapped while in force), `bool TryAmount(Piece.Requirement, int quality, out int amount)` (tier rows, base rows, and the idol row: 0 from quality 2 while in force), `HealLocal()`, `HealInventory(Inventory)`, `bool HideAtUpgrader(InventoryGui, Recipe)`, `string TooltipLines(int quality, bool crafting)`, `bool IsCultivator(ItemDrop.ItemData)` (by `m_shared.m_name`), `int TierOf(ItemDrop.ItemData)` (quality, at least 1). A tier whose cost has no valid material caps `m_maxQuality` below it (warning once). |
| `Patches/PieceRequirementPatches.cs`, `Patches/InventoryGuiPatches.cs`, `Patches/InventoryGuiHealPatches.cs`, `Patches/ItemTooltipPatches.cs` | tier patches | `GetAmount` postfix; `UpdateRecipeList` prefix (re-sync, then heal) in its own `[AlwaysOnPatch]` class, so it also runs while the feature is off; `AddRecipeToList` prefix (feature patch: Cultivator hidden at an upgrader, other Cultivator recipes' Upgrade rows dropped at level 3+); static `GetTooltip` postfix (`ItemData, int, bool, float, int, bool`). |
| `IconPainter.cs`, `TierIcons.cs`, `IconInlineGuard.cs`, `Patches/ItemIconPatches.cs`, `Patches/InventoryGridPatches.cs` | icons | `IconPainter.Compose(Sprite source, string key, Action<Color32[], int, int> paint)` → `Sprite` or null; `TierIcons.TransplantIcon(Sprite produce)` (sprout upper-right); `TierIcons.CultivatorIcon(Sprite vanilla, int quality)` → null below 4; `GetIcon` postfix; `InventoryGrid.UpdateGui` postfix (feature patch: quality number hidden for cultivators at quality ≥ 4, gem sprite set again as inline backup); Debug `TierIcons.ExportPngs(string folder)`. |
| `Replant.cs`, `ReplantHint.cs`, `Patches/PlayerPatches.cs`, `Patches/HudPatches.cs` | the action and the hint | `Replant.OnUpdatePlacement(Player, bool takeInput)` (no target in a helm or saddle frame); `Replant.Target` (per frame: `ZNetView View`, `PlantKind Kind`, `bool IsSapling`, `bool Allowed`, `int Frame`); `bool Replant.TryReplant(Player, ItemDrop.ItemData tool, ZNetView view, PlantKind kind, bool isSapling, out string refusal)` (no input; for self tests); `Replant.Reset()`; `ReplantHint.Update(Hud, Player)`; `ReplantHint.Reset()`; `UpdateHover` postfix marks every frame with a doodad controller (helm, saddle). Gamepad key is always X. |
| `RootGate.cs`, `RootDrawer.cs`, `Patches/PlantPatches.cs` | sapling growth gate, Yggdrasil root | `RootGate.BeforeGrow(Plant, ref GameObject result)` → bool (run original; holds every MC sapling while rules are pending); `RootGate.HoverLines(Plant)`; `ResourceRoot RootGate.FindRoot(Vector3 pos, float range)` (root collider layers only); `RootGate.RootMask`; per-peer drain ledger (effective level); `RootDrawer : MonoBehaviour` (next attempt time, cached root, last state for the hover). |
| `SelfTests.cs` | Debug in-world tests | section 8 |

### 7.2 Turning off

`OnDeactivated` (patches still applied during the call, each step guarded): self tests unregistered, target and hint
cleared, `TransplantContent.Rebuild()` with the feature off disables the sapling pieces, refreshes the build menu and
calls `CultivatorTiers.Apply()`, which puts the recipe and `m_maxQuality` back and heals the player's inventory;
join check and rules stop. Items, saplings, table entries and the `UpdateRecipeList` heal stay on (always on), so a
cultivator copy met later (chest, ground) is healed too. The `InventoryGrid` postfix is unpatched, so levels 4-7 show
their number again. At game quit the revert skips work on destroyed objects (Unity null checks).

## 8. Test plan

In-world self tests (`./tools/Test-InWorld.ps1 -Mod Cultivator.Replant`):

- `replant.network`: rules round trip, clamping, pending, `PlayerCheck.Decide`.
- `replant.content`: every item and sapling registered (ObjectDB, ZNetScene, piece table), fields per the table,
  pieces enabled/disabled with the feature and pending rules.
- `replant.tiers`: `m_maxQuality` 7 / 3 with the toggle, rows and amounts per quality 2-7, Forge level per quality, a
  level 3 → 4 upgrade through vanilla requirement checks, Forge of Potential hides the Cultivator.
- `replant.action`: spawned bushes and forage in front of the player; Replant with level 1 (raspberry: transplant +
  berries, bush gone), level 3 on a cloudberry (refused), level 4 (allowed); full inventory drops the transplant; own
  sapling gives its transplant back; ward refuses.
- `replant.grow`: each sapling forced to grow (`Plant.Grow`) becomes the vanilla prefab; biome rules (status in the
  Meadows for fiddlehead: wrong biome).
- `replant.root`: a spawned root and Yggdrasil sapling; drain at grow; waits at low level; no root → waits.
- `replant.icons`: transplant and tier icons painted; Debug export of the package icon.

In-game tests by hand: `TESTING.md` (G1-G5, E1-E7, live toggle, `Enabled = false` + restart, clean log, multiplayer
hand-off to a player without the mod, to a player with it turned off and to a host without it, PlantEasily /
PlantEverything / recipe-config mods, and the review items T33-T37, M10-M13).

## 9. Open questions and unverified

- Glyph shown for `JoyButtonX` in the hint (registered with `showHints: false`; fallback: the glyph of `JoySit` /
  `JoyUse`); check with a gamepad.
- Where fiddleheads and yellow mushrooms spawn (no vegetation list; locations or dungeons).
- Whether Mistlands canopy or root arches often give "Needs an open sky" near roots.
- Build-menu overflow with PlantEverything's pieces (15 × 6 grid per category).
- How far the slot's quality number really covers the gem (slot layout from the UI prefab, not in `.ref`); the number
  is hidden at levels 4-7 either way.

### Review (2026-10-03)

Multi-agent review; each finding was checked by a verifier who tried to refute it. 22 confirmed, all fixed:

- **C1** The hint showed "[E] Replant" while steering a ship (or riding a lox), but E let go of the helm: no target,
  hint or consumed press in a helm or saddle frame (§2.4).
- **C2** The LB key of the "gamepad-mouse mode" can never happen on PC (Switch-only context): the branch and its
  hint mode removed; the gamepad key is always X, with a glyph fallback to `JoySit` / `JoyUse` (§1.4, §2.4, D8).
- **C3** The Encyclopedia showed a Wooden Battle Idol at an upgrade station for every level: the idol row reads 0 from
  quality 2 while the tiers are on; the two lines that remain are documented (§2.3, §6.1).
- **C4** Cultivator copies at max quality 7 picked up after turning the feature off offered a phantom "Upgrade to 4":
  the heal prefix moved to its own always-on class (§2.3, §7.1).
- **C5** A recipe array swapped in by another mod after `Apply` made levels 4-7 cost the vanilla formula:
  `CultivatorTiers.Resync()` re-applies on an array swap; other Cultivator recipes lose their Upgrade rows at level
  3+ (§2.3, §6.2).
- **C6** Tier materials extend vanilla's "cheated item" tagging to level 1-3 crafts: accepted and documented (§2.3,
  README).
- **C7** Saplings loaded while a client waited for the rules kept the player's own grow time: `Rebuild` rewrites the
  grow times of loaded saplings, and every MC sapling is held while the rules are pending (§2.2).
- **C8** `RootRange` could be set to 2 m, equal to the Yggdrasil grow radius, where no transplant can ever grow:
  minimum raised to 4 (§2.6, §5).
- **C9** The README said young transplants survive an uninstall; a single-player or host game deletes them: README
  and §4.4 corrected.
- **C10** Same root as C8, plus the texts gave no lower bound: the hint, the `RootRange` description and the item text
  now say "2 to N m" / "not right against it" (§2.6).
- **C11** Several saplings of one player could grow on one drain when another player owns the root: per-peer drain
  ledger with an effective level; the two-owner case stays a known limit (§2.6).
- **C12** The root search queried every layer with a 1024-hit cap, so a busy base could crowd the root out: it now
  searches only the root prefab's collider layers and logs once if the cap is reached (§2.6).
- **C13** A connected player with the mod turned off grows Yggdrasil transplants with no root and no sap: documented
  as a hand-off (§4.4, README, `AllowPlayersWithoutMod`, the server's warning), no always-on gate.
- **C14** Same case as C13 from the text side: the setting description and the "allowed" warning now separate players
  without the mod from players with it turned off or another version.
- **C15** Same root as C7 (pending window on join): fixed by the C7 change.
- **C16** The multiplayer notes left out that a server or host without the mod deletes dropped transplants:
  `ModMultiplayerNotes`, README and §4.4 say so.
- **C17** The `GrowTimeMultiplier` description said "real time"; growth runs on the world clock: wording fixed (D5,
  README).
- **C18** The tier gem sat under the slot's quality number: an `InventoryGrid.UpdateGui` postfix hides the number at
  levels 4-7 and sets the gem again (§2.5).
- **C19** The transplant sprout sat under the stack amount text: moved to the upper-right corner (§2.5).
- **C20** Same root as C8 and C10 (minimum range and missing lower bound): fixed there.
- **C21** Same root as C7 (rules change and join window reach planted saplings): fixed by the C7 change; the README no
  longer says "once their area loads again".
- **C22** The transplant text "Grows anywhere a crop would." did not match the rule: now "Grows on open ground in any
  land biome, no tilling needed; in the Ashlands, the Mountains and the Deep North only inside a shield." (§2.1).

Also from the docs pass: E2 digs up any transplant sapling that has not grown yet, not only your own (wards protect
them); the goal text says so. Not taken from the review: a too-close placement check on the Yggdrasil ghost (the
range minimum and the texts cover it), an exact cross-peer root drain (needs a new RPC and a network version bump).

Found by the test runs (2026-10-03):

- **Item database without vanilla items.** With ItemManager-based mods (Warfare) the main menu's first `ObjectDB.Awake`
  holds only that mod's items; the mod took it for the real database and logged "missing" errors for every source
  item. Registration and the tier capture now wait for a database that holds vanilla `Wood`
  (`TransplantContent.HasVanillaItems`). Spyglass, Sneak Ambush, Forge Idol Upgrades and Tower Shield Wall have the
  same false errors (separate fix).
- **E is also `TabRight`.** In build mode the default E cycles the placement snap point
  (`Player.UpdatePlacementGhost`); a Replant press now also clears `TabRight` when it was pressed in the same frame.
- **Self-test layout.** The Forge test looks farther for a free spot (the spawn stones fill the close ring) and places
  each extension inside its own reach (`forge_ext1` 2 m).
