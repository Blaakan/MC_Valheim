# Forge Idol Upgrades — design

| | |
|---|---|
| Mod | Forge Idol Upgrades |
| GUID / project | `MC.Crafting.Forge.IdolUpgrades` (`src/Crafting/Forge.IdolUpgrades/`, root namespace `MC.Crafting.ForgeIdolUpgradesMod`) |
| Category / scope | Crafting / QoL |
| Side | Both: the server (or the host) and every player install it (3.8); multiplayer Compatible; network version 2 (RPCs `<guid>.Settings` / `<guid>.SettingsRequest`; rules layout 2 since the idol tier rule) |
| Sheet idea | `Forge of potential revamp` (also covers part of `Boss summon revamp`: boss trophies get a lasting use) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/`; idols, trophies, creatures, recipes, stations, UI layout and English strings dumped at runtime (Unity 6000.0.75f1) on 2026-09-29; other Forge and UI mods' sources read on GitHub (2026-09-29) |
| Status | Implemented (0.1.0), in-world self-tests pass (`forge.catalog`, `forge.icons`, `forge.tooltip`, `forge.popup`, `forge.idols`, `forge.refine`, `forge.tiers`); in-game tests pending |

## Goal

Requirements from the user (2026-09-29), numbered:

1. **Idols can be upgraded at the Forge of Potential**, three times: plain → 1 star with common ("base") trophies,
   1 → 2 stars with elite trophies, 2 → 3 stars with a boss trophy, all from the idol's tier (biome), plus the tier's
   metal.
2. **Costs:** 5 metal + 5 common trophies (level 1), 10 metal + 3 elite trophies (level 2), 15 metal + 1 boss trophy
   (level 3), each on top of the previous-level idol.
3. **Trophy lists per tier** exactly as given by the user (Wood: deer / bears, neck / Eikthyr; … Bloodgold: moose,
   Elaking, Eyeless One, Krigen, Pulp, seal / Barka, Hexen / Kall). Resolved to prefab names in 3.2.
4. **Refinement odds by idol level:** level 0 = 35% success (vanilla: 35% failure), 1 = 55%, 2 = 75%, 3 = 95%.
5. **Idol upgrades happen at the Forge of Potential, in a tab separate from the vanilla weapon/armor upgrade.**
6. **Upgraded idol icons** are the vanilla icon plus 1, 2 or 3 stars.
7. **The idol description reflects its level.**
8. The mod can be turned off on its own, live (framework toggle).

Added by the user at the pause (2026-09-29):

9. **What a failure does is a setting:** lose X levels (X from 1 upward, default 1) or destroy the item like vanilla.
10. **The mod is required by the server and every player** (user rule: mods that change the experience are required
    on every player of a server).
11. Trophy list corrections: Wooden elite = Boar and Neck (not Bear); Bear and Ghost are Bronze elite; Frost Blob is
    Silver common; Kall's step uses the Crown Jewel; Kvastur and Serpent in no list.

Added by the user on 2026-09-30 (balancing change):

12. **Higher levels need higher idols.** "Someone can craft a flint axe and upgrade it to tens of levels with just
    Meadows trophies": when an item reaches a certain level, the next idol tier is required. Workbench armor at level 4
    matches the next tier's level 1, so each tier covers 4 ranks, with some overlap so that a fully upgraded item does
    not need a new idol at once. The user's ForgeUpgradeChart (idea sheet): an item of base tier T needs a tier-T idol
    at levels 1-5, T+1 at 6-9, T+2 at 10-13, and so on, capped at Bloodgold. Example: a level 4 flint axe takes two
    wooden idols to level 6, then Bronze idols up to level 10.

Not done from the sheet: a skill gate (non-goal).

## 1. Vanilla behaviour (code trace)

### 1.1 Refinement (`InventoryGui.DoCrafting`, upgrader branch)

- The Forge of Potential is a `CraftingStation` with `m_upgrader = true`, `m_hasCraftTab = false` (dump: prefab
  `UpgradeStation`, `$piece_upgradestation`, no roof or fire needed, can repair, skill Crafting).
- Every refinable recipe carries one idol requirement `m_upgraderResource = true`, amount 1, per level 0 (dump): one
  idol per attempt at every level. The idol tier does not always match the item's craft tier.
- `DoCrafting` removes the item (whole stack) **before** rolling, takes the chance from the idol **prefab**
  (`m_upgradeChance`), and re-creates the item with `Inventory.AddItem(name, …)` (new `m_customData`, refiner as
  crafter, current world level, full durability).
- Every idol prefab has `m_upgradeChance = 0.65`, **`m_breakChance = 1.0`**, return 0.35 (dump; the code defaults are
  0.65 / 0.1 / 0.5). So vanilla = 65% success, 35% **destroyed** with 35% of the materials back; the downgrade branch
  and its missing `$msg_upgrader_failed` key are unreachable in 1.0.16.
- `Player.ConsumeResources(..., itemQuality: -1)` removes idols from the first stacks in list order, any quality.
- `OnCraftPressed` at an upgrader requires `recover requirements + 1` empty slots (room for a broken item's refund).
- Duration: 8 s + 1 s × target quality, reduced by the station's crafting skill (`UpdateRecipe`).

### 1.2 Tabs, list and panel (`InventoryGui`)

- Tab selection lives only in `m_tabCraft` / `m_tabUpgrade` (`interactable == false` = selected). `UpdateCraftingPanel`
  re-forces them at a station without a craft tab on **every** call: Craft hidden, Upgrade selected. The tabs are
  children of `TabsButtons` (Craft at x −418, UPGRADE at −311, 100×32). Their `onClick` listeners are persistent
  (prefab), so a cloned button keeps calling `OnTabCraftPressed` unless `onClick` is replaced. The Craft tab carries a
  `Localize` component and a gamepad `UIGamePad`.
- `UpdateRecipeList`: craft rows when `InCraftTab()`, else one row per inventory item of each available recipe with
  `m_maxQuality > 1` (at an upgrader: only recipes with an upgrader requirement). `Player.GetAvailableRecipes` at an
  upgrader returns every known recipe (`RequiredCraftingStation` is always true there).
- `AddRecipeToList` draws the **recipe prefab's** icon; `UpdateRecipe` reads `m_shared.m_icons[...]` directly for the
  big icon; `SetupRequirement` draws the requirement prefab's icon. 4 requirement slots (`res_icon`, `res_name`,
  `res_amount`, `UITooltip`); more than 4 requirements page every second.
- `Player.HaveRequirementItems` counts, per requirement, the largest single-quality count over qualities
  1..`m_maxQuality`: with idols at max quality 1, only plain idols count.

### 1.3 Items, quality and shared data

- `ItemData.m_quality` is saved in inventories, chests and ground items and is **never clamped** to `m_maxQuality` on
  load. `Inventory.FindFreeStackItem` stacks only equal quality and world level; `ItemData.IsSameType` (drag merge)
  compares quality only when the target's `m_shared.m_maxQuality > 1`. `InventoryGrid.UpdateGui` prints the quality
  number and `GetTooltip` a "Quality" line (when not crafting) only when `m_maxQuality > 1`.
- **`m_shared` is a copy per item object** in a build: every `Object.Instantiate` of an item prefab (inventory load,
  `Inventory.AddItem(name, …)`, crafting, chests) deep-copies the serialized `SharedData`; only `ItemData.Clone()`
  shares it (`ItemDrop.Awake` relinks to the prefab only in the editor). A prefab edit reaches items made later, not
  items already loaded.
- `ItemData.GetIcon()` = `m_shared.m_icons[m_variant]`, IL 19 bytes: under Mono's 20-byte inline limit. MonoMod marks
  a patched method NoInlining for the session (also after unpatch), but callers compiled before keep the inlined copy.
- Idol icons are packed in a 4096² BC7 sRGB atlas, not CPU-readable; 64×64 at 100 ppu, rectangle packing. The game
  runs in Linear color space (self-test note).
- `Humanoid.UnequipItem` / `SetupVisEquipment` pass the quality to hand, back and shoulder visuals.

### 1.4 Trophies and creatures (runtime dump)

All trophy prefabs of the user's lists exist, with these notes: Oozer (`BlobElite`) drops `TrophyBlob`; Charred
Twitcher drops no trophy; Kall Fimbulbringer (`FrozenKing_p3`) drops `FrozenKingDrop` (Sacrificial Blood) and
`CrownJewel`, no trophy; `TrophyForestTroll` and `TrophyFrostTroll` share the item name `$item_trophy_troll` (Troll
Trophy); `TrophyDraugrFem` shares `$item_trophy_draugr`; the Bear (`Bjorn`) belongs to the Black Forest, the Writhan
to the swamp (Undead faction). Hildir's bosses are not `m_boss`.

## 2. Existing mods and what they teach

- Forge odds mods (ReforgedPotential, OdinBet, JGFoP, Wire's, ForgeOfPotentialSafe, Potential Forge NoFail, Forge No
  Destroy, Forge of Certainty, Forge of Progression, Potential Draught, G3A3, ImpactfulSkills, CombatAdjustments) take
  over or tweak the same `DoCrafting` branch or the idol data. Nobody upgrades idols with trophies; OdinBet and JGFoP
  craft idols from boss trophies (Sacrificial Blood for Bloodgold).
- HarmonyX runs every prefix even after one returned false: two takeovers of `DoCrafting` would both roll.
- Tab mods (Recycle_N_Reclaim, Jewelcrafting, VNEI, EpicLoot's old tabs, a Forge-only "Affinity" tab) clone a vanilla
  tab button, replace `onClick`, strip `Localize`, and handle their tab with a flag plus prefixes on
  `UpdateRecipeList` / `UpdateRecipe` / `DoCrafting`.
- Icon overlays: GetIcon postfixes (Vanity, AzuEPI, Community Patch) at plugin load; generated sprites through a
  RenderTexture blit (VentureValheim notes).
- EpicLoot keeps its data by `m_customData`; an in-place quality raise keeps it with no special code.

## 3. Design

### 3.1 Idol level = item quality

Level 0..3 = `m_quality` 1..4 (`IdolLevels`). No new prefab, nothing stored besides a vanilla field: saves, chests,
drops and players without the mod keep it. The 16 idols' `m_maxQuality` is raised to 4 so levels never merge, show
separately and get tooltips. Because shared data is per item object (1.3), `IdolCatalog` raises it on the prefabs
(`ObjectDB.Awake` / `CopyOtherDB`), on the player's inventory at activation, and on any idol it identifies later
("heal on sight": grids run it every frame for every shown item, so an idol from a chest loaded earlier is fixed
before it can be dragged). Never lowered while the game runs (decision 6).

Idols are identified by `m_dropPrefab` (set on every real item), with the shared name as fallback (the prefab's own
data in recipe panels).

### 3.2 Tier table (`IdolTiers.cs`, config per tier)

| Tier | Idols | Material | Common | Elite | Boss |
|---|---|---|---|---|---|
| 0 Wooden | `Upgrader0Weapon/Armor` | `Wood` | `TrophyDeer` | `TrophyBoar, TrophyNeck` | `TrophyEikthyr` |
| 1 Bronze | `Upgrader1…` | `Bronze` | `TrophyGreydwarf, TrophyGreydwarfShaman, TrophyGreydwarfBrute, TrophySkeleton` | `TrophyForestTroll, TrophySkeletonPoison, TrophyBjorn, TrophyGhost` | `TrophyTheElder, TrophySkeletonHildir` |
| 2 Iron | `Upgrader2…` | `Iron` | `TrophyBlob, TrophyDraugr, TrophyDraugrElite, TrophyLeech, TrophySurtling` | `TrophyAbomination, TrophyWraith, TrophyWrithan` | `TrophyBonemass` |
| 3 Silver | `Upgrader3…` | `Silver` | `TrophyHatchling, TrophyWolf, TrophyUlv, TrophyCultist, TrophyBlob_Frost` | `TrophySGolem, TrophyFenring` | `TrophyDragonQueen, TrophyCultist_Hildir` |
| 4 Black Metal | `Upgrader4…` | `BlackMetal` | `TrophyDeathsquito, TrophyGoblin, TrophyGoblinShaman, TrophyGrowth, TrophyLox` | `TrophyGoblinBrute, TrophyBjornUndead` | `TrophyGoblinKing, TrophyGoblinBruteBrosShaman, TrophyGoblinBruteBrosBrute` |
| 5 Black Marble | `Upgrader5…` | `BlackMarble` | `TrophySeeker, TrophyTick, TrophyHare, TrophyDvergr` | `TrophySeekerBrute, TrophyGjall` | `TrophySeekerQueen` |
| 6 Flametal | `Upgrader6…` | `FlametalNew` | `TrophyCharredMelee, TrophyCharredArcher, TrophyAsksvin, TrophyVolture, TrophyBlob_Lava` | `TrophyFallenValkyrie, TrophyMorgen, TrophyCharredMage, TrophyBonemawSerpent` | `TrophyFader` |
| 7 Bloodgold | `Upgrader7…` | `Gold` | `TrophyMoose, TrophyElaking, TrophyMole, TrophyJotunWarrior, TrophyBlob_Morkhalla, TrophySeal` | `TrophyBarka, TrophyJotunWitch` | `CrownJewel` |

Pools are keyed by item name (the inventory counts and stacks by name), so both troll trophies count. Unknown names are
logged once per rebuild; the table rebuilds lazily when the ObjectDB changes or a setting changes (skipped while the
menu's ObjectDB is still empty).

### 3.3 The Idols tab (`IdolsTab`)

- A clone of `m_tabCraft` (the Craft tab is hidden at the Forge, so its place and its gamepad trigger are free),
  labelled IDOLS, `Localize` removed, `onClick` replaced. Shown only while the current station is an upgrader.
- Own flag `Mode` (never the vanilla tab flags): `UpdateCraftingPanel` postfix shows the tab and draws the selection
  (Idols: our button selected, UPGRADE clickable). `OnTabUpgradePressed` / `OnTabCraftPressed`, `Show` and `Hide`
  clear it. The Forge still opens on UPGRADE.
- `UpdateRecipeList` prefix (Mode only): one row per idol stack under 3 stars, built with vanilla `AddRecipeToList`
  and a private `Recipe` per idol prefab (never in `ObjectDB.m_recipes`, empty requirement list), then the row icon is
  set to the stack's starred icon and the quality number hidden; list size and row positions as vanilla. Other mods'
  postfixes (Crafting Search and Sort) still run on these rows.
- `UpdateRecipe` postfix on an idol row: starred icon of the next level, description (level, both chances, cost, the
  pool's trophy names), "Upgrade to N stars", 3 requirement slots (metal, "Common/Elite/Boss trophies" whose icon goes
  round the pool each second with a tooltip listing each kind and the count held, and the idol itself), button
  "Upgrade idol", interactable only when `IdolUpgrade.Blocker` is null and the station is usable.
- `OnCraftPressed` prefix: our checks, then the vanilla timer (`ForgeUi.StartTimer`: same fields, vibration, the
  station's craft effect).
- `DoCrafting` prefix (Priority.First, our recipe only): `IdolUpgrade.Upgrade`, message, the station's done effect,
  `UpdateCraftingPanel`; then it empties the craft snapshot (`m_craftRecipe`, `m_craftUpgradeItem`) in a `finally`,
  because HarmonyX still runs the other prefixes and a Forge takeover (ReforgedPotential) would remove
  `m_craftUpgradeItem`, our idol stack.

### 3.4 One upgrade (`IdolUpgrade`)

- Cost of level L: `MaterialCost[L]` of the tier material + `TrophyCost[L]` from pool L, any mix (decision 3).
  Counting and removing use the vanilla world-level rule.
- Trophies are taken from the kind held most first (rare kinds stay).
- Result: a stack of 1 with no matching next-level stack is upgraded in place (same object and slot); otherwise one
  idol leaves the stack and a `Clone()` at quality+1 is added (joins a next-level stack or takes a free slot). The room
  is checked before and again at the end of the timer; nothing is consumed when it fails.
- Like every vanilla upgrade, the result takes the current world level (an idol from a lower New Game+ level becomes
  usable, since counting at the Forge filters by world level) and the cheated mark when the idol, the metal or a pool
  trophy is cheated, or the no-cost cheat is on.
- No stats and no skill for idol upgrades.

### 3.5 Refinement (`ForgeRefine`, `ForgePanel`)

- `ForgeRefine.TryPlan`: the first upgrader requirement with an amount; level to spend from `IdolChoice` (click
  choice, else `IdolChoice` setting: most stars or plain first); chance from the level's setting.
- `OnCraftPressed` prefix: when an idol is held (or no-cost) and OnFailure is LoseLevels, start the timer without the
  vanilla free-slot rule (decision 5); with Destroy, vanilla's press (and its free-slot rule) runs.
- `DoCrafting` prefix (Priority.Last, skipped when `__runOriginal` is already false: another mod took over): replaces
  the vanilla upgrader branch. Same guards (item present, DLC), roll with `Random.Range(0, 1)`, success when
  `chance >= roll` (vanilla comparison):
  - success: `Remake` at quality + 1: what vanilla's `Inventory.AddItem` re-creation gives (taken off, full
    durability, the refiner as crafter, current world level, cheated mark from inputs or station) but on the same
    item object, so other mods' custom data survives (decision 2); vanilla `$msg_upgrader_success`;
  - failure, OnFailure = LoseLevels (default): `Remake` at max(1, quality - LevelsLost), vanilla
    `$msg_upgrader_fail` ("downgraded to level N"); at level 1 nothing more is lost (own message);
  - failure, OnFailure = Destroy: vanilla break: item removed, `$msg_upgrader_broke`, each recoverable material back
    at ceil((amount at level 1 + amount at the item's level) × the idol's return share, 0.35);
  - consume exactly one idol of the rolled level (`RemoveItem(name, 1, quality)`), and any other upgrader requirement
    the vanilla way; then `UpdateCraftingPanel`, `RaiseSkill(recipe station skill, 1)`, the station's done or fail
    effect, stats `CraftsOrUpgrades` + `Upgrades`, `Gogan.LogEvent`; an Info log line per attempt.
  - Any exception: no fallback to vanilla (vanilla removes the item first).
- `ForgePanel` (UpdateRecipe postfix at the Forge's Upgrade tab): the idol slot shows the starred icon of the level
  that will be spent, the craft-type line "N% chance with a X-star idol." plus the failure rule ("A failure costs 1
  level." / "A failure costs only the idol." at level 1 / "A failure destroys the item.") replaces the vanilla break
  warning, the button reads "Attempt Refinement (N%)". A click on the idol slot (`ForgeUi.SlotClick`,
  added to the 4 slots) cycles through the levels held; the choice lasts until the window closes.

### 3.5b Idol tier by item level (`IdolTierRule`, `IdolSwap`)

- **Rule** (goal 12): base tier = the tier of the idol the recipe itself asks for (decision 12). Tier offset for an
  item now at level L: 0 while L <= `LevelsOnOwnIdol` (5), else 1 + (L - 6) div `LevelsPerIdolTier` (4), capped at
  Bloodgold (tier 7). Same family: `Upgrader{tier}Weapon` stays a battle idol, `Upgrader{tier}Armor` a protection
  idol. Off (`HigherIdolAtHighLevels` = false): offset 0 at every level. The self-test `forge.tiers` compares the rule
  with the user's chart cell by cell (8 base tiers, levels 1-40).
- **How vanilla is made to ask for it** (decision 13): `IdolSwap` sets the recipe's idol requirement's `m_resItem` to
  the needed idol for the length of one vanilla call, then puts the recipe's own idol back (finalizer, also after an
  exception). Calls: `Player.HaveRequirementItems` (discover = false only; target quality is its `qualityLevel`:
  list row colour, Refine button, the vanilla `DoCrafting` check), `InventoryGui.SetupRequirementList` (the
  requirement slot: icon, name, count, red blink), and `Inventory.ItemCheated` in `ForgeRefine.Run`. Only for the
  local player at an upgrader station, never for our Idols-tab recipes, and a nested call is a no-op (a second swap
  would add the offset twice). `ForgeRefine.TryPlan` computes the same idol itself (plan: `IdolPrefab`, `BaseTier`,
  `Tier`), so the chance, the idol choice, the click cycle, the removal and the break refund use it.
- **Panel**: when the idol is raised, the craft-type line adds "From level 6 this item needs Bronze idols." (the level
  where that tier starts, also under the Bloodgold cap), or "... You carry none." when none is held. The Info log line
  names the idol prefab and "(own idol tier N, raised by item level)".
- **Game data** (`forge.tiers` notes, 1.0.16): the vanilla idol does not follow the biome everywhere. Wooden battle
  idol: `AxeStone`, `KnifeFlint`, `SpearFlint`, `Club`, `Bow`, tools. Bronze battle idol: `AxeFlint`, `SwordBronze`,
  the 1.0 wooden weapons (`SwordWood`, `AxeWood`, ...). `AxeBronze` asks for Iron, `AxeIron` for Silver, `AtgeirGold`
  for Bronze, `AxeGold` for Black Metal. Wooden protection idol: rags, leather, tunics, dresses, hats, `ShieldWood`.

### 3.6 Icons and tooltip

- `StarIcons` (generated sprites): GPU blit of the icon's atlas square into a temporary RenderTexture (default
  read/write = sRGB in Linear space), `ReadPixels`, 1-3 gold stars with a dark rim drawn on the CPU (signed-distance
  anti-aliasing) along the top edge, right-aligned (the hotbar key digit sits top left, the vanilla quality number
  top right), `Sprite.Create` with the same size, pivot and ppu. Cached per (sprite, stars, layout),
  `HideAndDontSave`, **kept for the whole game session**, also after a toggle off: messages waiting in MessageHud's
  queues (pickup "Added", "New material" unlock) hold the sprite, and a destroyed sprite shows as a white square there
  (reported by the user in game, 2026-09-29). A copy that comes back empty (all transparent, e.g. game window
  minimized) is never cached: plain icon, retried 1 s later. Any other failure: plain icon, logged once.
- `ItemData.GetIcon` postfix (quality ≥ 2 idols only: cheap fast path). `IconInlineGuard` patches and unpatches
  GetIcon at plugin load, so it stays non-inlinable even when the mod starts disabled and is turned on later.
- `InventoryGrid.UpdateGui` postfix: hides the quality number of idols and sets the starred sprite (also covers any
  caller compiled with an inlined GetIcon).
- Recipe rows, the big recipe icon and requirement slots are set explicitly (they use prefab data or `m_icons`).
  Requirement slots use a second star placement, across the icon's middle (cached separately): those slots print the
  item name over the icon's top edge.
- `ItemDrop.GetHoverText` postfix: a starred idol on the ground reads "(2 stars)" instead of vanilla's raw "[3]".
- `ItemData.GetTooltip` (static overload) postfix: replaces the vanilla "Quality: q" line with "Level: … (n of 3)",
  "Refinement chance: N%", and the next upgrade's cost or "Maximum level".

### 3.7 Configuration

`General`: `AllowPlayersWithoutMod` (false). `Refinement`: `ChanceLevel0..3` (35/55/75/95), `OnFailure`
(LoseLevels/Destroy), `LevelsLost` (1, 1..100), `HigherIdolAtHighLevels` (true), `LevelsOnOwnIdol` (5, 1..1000),
`LevelsPerIdolTier` (4, 1..1000), `IdolChoice` (Highest/Lowest). `Upgrade costs`: `Level1..3Material`
(5/10/15), `Level1..3Trophies` (5/3/1). One section per tier: `Material`, `CommonTrophies`, `EliteTrophies`,
`BossTrophies` (prefab names). Everything applies live (tier settings rebuild the table). All but `IdolChoice` (personal) and
`AllowPlayersWithoutMod` (server only) are **rules**: `ForgeRules` snapshots them, and every game reads them only
through `ServerRules.Current` (the server's in multiplayer, 3.8).

### 3.8 Multiplayer and hand-off

Both side (user rule). Refinement and crafting are local (`InventoryGui` + the local inventory), so technically
client-side, but the user wants every player of a server on the same rules:
- **Join check** (`PlayerCheck`, same contract as the sibling mods Weapons.Moveset, Sneak.Ambush, Creatures.Morale):
  the server (dedicated or host) waits 1 s after a player is ready, then refuses a player that is not
  `NetworkGate.PeerCompatible`: no mod, another network version (the build before the idol tier rule), or the mod
  turned off on their game; a player who turns it on or off while connected (`NetworkGate.PeerStateChanged`) is
  checked again after the same grace. Refusal = vanilla "Error" RPC with ErrorVersion (their game shows "Incompatible
  version"; socket closed 4 s later); the log gives the framework's reason (`PeerProblem`).
  `AllowPlayersWithoutMod` lets them in (log warning). Until 2026-09-30 the check only asked `PeerHasMod`; with the
  network version bump that would have let the older build in, whose mod then turns itself off (server mismatch)
  and refines the vanilla way.
- **Server rules** (`ServerRules`, same flow as Breeding's ServerSettings): the client asks after the handshake, the
  server answers with a `ZPackage` of `ForgeRules` (layout 2: chances, failure mode, levels lost, the idol tier rule,
  costs, the 32 tier strings) and pushes again when its settings change. Layout 2 came with the idol tier rule, and the
  network version went to 2 with it, so a game with the older build is refused by the handshake instead of reading a
  package it does not understand. The client clamps values to the config ranges, refuses an
  unknown layout, and uses the rules only for that ZNet session; single player, host and server use their own config.
- Framework gate: on a server without the mod the feature is inactive on the client.
- Hand-off (AllowPlayersWithoutMod on, or another server): a starred idol keeps its quality but shows plain; their
  Forge counts only plain idols (`HaveRequirementItems` with max quality 1) and their drag merge ignores quality.
  Their Forge also asks for the recipe's own idol at every level (no tier rule): that is why the server refuses them
  by default.

### 3.9 Live toggle

Off (`OnDeactivated`, patches still applied during the call): first `IdolsTab.BeforeOff` cancels a running idol
upgrade or Forge refinement (nothing is consumed before the timer ends), leaves Idols mode with the vanilla tab look,
clears an idol selection and rebuilds the list with vanilla rows. Without it, vanilla would take our private recipe
(no requirements) for a refinement and `DoCrafting` would remove the whole idol stack. Then the Idols tab is
destroyed, slot click components removed, choices forgotten; star sprites stay alive (3.6).
Raised max qualities stay until restart (decision 6). On: table rebuilt, the inventory's idols raised again. Leaving the Forge (window closed, walking away) while in Idols mode also restores the vanilla tab look, so the
next station does not show both vanilla tabs unselected.

### 3.10 Compatibility and coordination

- Other Forge mods: warned once per session (`ForgeGuard`: foreign transpilers or bool prefixes on `DoCrafting`,
  first Forge visit); the refinement prefix steps aside when another takeover already skipped vanilla.
- MC Crafting Search and Sort: its `UpdateRecipeList` postfix (Low) filters/sorts our rows; ingredient search sees no
  requirements on our private recipes (name search works).
- MC One Click Repair All calls `UpdateCraftingPanel`: the Idols tab state survives (own flag).
- MC Sort Chest merges only equal quality: levels stay apart. MC Loot Pickup Filter: one entry per idol prefab.
- MC Crossbow Stays Loaded: a successful refinement keeps its custom data but tops durability up, which its stamp
  reads as a repair (needs a reload, same as vanilla refinement).
- EpicLoot: same `ItemData` and custom data after a success: magic kept.

### 3.11 In-world self-tests (`SelfTests.cs`, Debug only)

- `forge.catalog`: 16 idols at max quality 4, every tier's metal and pools, every default name exists in the game,
  troll trophy shared name, level mapping.
- `forge.icons`: starred sprites (new, cached, same size and ppu, gold star pixel, atlas copy not empty; PNGs written
  next to the screenshots), GetIcon of a 3-star idol, the **hotbar** slot (fed only by GetIcon) shows stars.
- `forge.tooltip`: level line, chance, vanilla quality line gone, next cost / maximum, metal named, ground hover of a
  dropped 2-star idol.
- `forge.popup`: real pickup of a 2-star idol never seen before, fresh star cache: the "Added" message and the "New
  material" unlock message show the starred sprite (screenshots).
- `forge.idols`: Forge spawned 3 m away (use range enlarged on the test copy), opens on UPGRADE, IDOLS tab, rows,
  slots, button, upgrade 0→1 from a stack of 3 (craft snapshot empty afterwards), 1→2 and 2→3 in place, 3-star idol
  leaves the list, `BeforeOff` with an idol upgrade running (timer cancelled, vanilla rows, nothing used), back to
  UPGRADE.
- `forge.refine`: a known one-handed weapon at level 3 with an idol recipe; default pick = 3 stars, button shows the
  chance, failure text; click cycles levels; forced failure (roll above 95%) drops the same object to level 2 (full
  durability, refiner as crafter) and spends only the 3-star idol; forced success (roll under 35%) raises it back to 3
  with custom data kept; no idol → button off; Destroy rule (test override, never the config file): the weapon is gone,
  the idol used, materials came back.
- `forge.catalog` also: rules round trip on the wire (the tier rule fields too), clamping, unknown layout refused,
  single player never takes rules from a peer, join check verdicts.
- `forge.tiers`: the rule against the user's chart (8 base tiers × levels 1-40), rule off, first levels 6/10/.../30;
  family kept and Bloodgold cap on real recipes (`AxeStone`, `ArmorLeatherChest`); at the Forge a level 6 Stone axe
  with only a wooden idol: greyed row, button off, slot names the Bronze idol, panel note; with a Bronze idol: button
  on, forced success spends the Bronze idol and not the wooden one; level 4 asks for the wooden idol again; the recipe
  keeps its own idol outside the checks. Notes list every refinable item by its own idol tier.

## 4. Edge cases

- Several levels of the needed idol: default most stars; click cycles; choice kept until the window closes.
- The player switches tabs during the 8-12 s timer: `DoCrafting` decides by the snapshot recipe, not the tab.
- Closing the window during the timer cancels (vanilla `Hide`), nothing consumed.
- Idol stack upgraded while the inventory fills up during the timer: re-checked at the end; nothing consumed, message.
- New Game+: counts and removals follow the vanilla world-level rule; idols of different world levels never stack; an
  upgraded idol takes the current world level (3.4).
- The mod turned off at the Forge (ConfigurationManager or a config edit while the window is open): 3.9.
- Removing the mod with starred idols: a vanilla Forge counts only plain idols, so starred ones cannot be spent alone,
  and with a plain one carried `ConsumeResources` (any quality) may use up a starred one; the README says to spend or
  merge them first.
- No-cost cheat or `NoCraftCost` world modifier: upgrades and refinements consume nothing (vanilla rule); a refinement
  without any idol rolls with the plain chance.
- A modded recipe with several upgrader requirements: the first sets the chance; the others are consumed vanilla-style.
- An idol level above 3 (another mod or console): treated as 3 stars, not listed in the Idols tab.
- Another Forge mod that takes the roll over (its `DoCrafting` prefix skips vanilla) consumes with its own code and
  never sees the raised idol; raising only the check would let a player pass it with a higher idol and spend
  nothing (ReforgedPotential spends `m_craftRecipe.m_resources` through `ConsumeResources`). So while a foreign
  transpiler or bool prefix sits on `DoCrafting` (`ForgeGuard.ForeignTakeover`, cached, logged once), the tier rule
  is off on that game.
- Crafting Search and Sort caches ingredient terms per recipe, outside any swap: at the Forge its search matches the
  recipe's own idol, not the raised one (documented, test C08).
- A modded recipe with several idol requirements: every one is raised in the checks, and our refinement spends the
  raised one of each.
- An item above the chart (level 30 and up for a wooden-tier item): Bloodgold idols, whatever the level.

## 5. Decisions and open questions

### Decisions

1. **A failed refinement costs levels, not the item, by default** (user, at the pause): `OnFailure` = LoseLevels
   (default) with `LevelsLost` = 1 (1..100, never below level 1), or Destroy = vanilla break. Vanilla destroys the
   item on every failure (1.1). The idol is always used up.
2. **Success and level loss look like vanilla's re-creation** (full durability, refiner as crafter, current world
   level, taken off) **but keep the same item object**: our takeover skips the vanilla code where other mods (EpicLoot)
   carry their data over, so a new item would lose it. The user asked why not stick to vanilla: this is the only
   difference left, and it only keeps data vanilla has none of.
3. **"5 base trophies" = any mix of the tier's common trophies** (not five of one kind); taken from the biggest pile
   first.
4. **Trophy lists** as the user confirmed at the pause: Wooden elite Boar and Neck; Bronze elite Troll, Rancid Remains,
   Bear, Ghost; Silver common adds Frost Blob; Kall's step uses the Crown Jewel (no trophy); Charred Twitcher has no
   trophy (left out); Oozer drops the Blob trophy; Kvastur, Serpent and the white deer trophy in no list. Metals: `Wood`,
   `Bronze`, `Iron`, `Silver`, `BlackMetal`, `BlackMarble`, `FlametalNew` (the item named Flametal), `Gold`
   (Bloodgold).
5. **No free-slot rule at the Forge** for refinement with LoseLevels: vanilla reserved room for a broken item's
   refund. With Destroy the vanilla rule stays.
11. **Both side, join check, server rules for everyone** (user rule, 3.8); IdolChoice stays personal.
6. **Raised max qualities stay until restart** after a live toggle off (protects starred idols from merging and a
   toggle exploit that would make starred idols from plain ones); a deviation from "off = not installed", flagged.
7. **Idol choice**: default most stars (`IdolChoice`), plus a click on the idol slot (mouse only). Gamepad players get
   the setting.
8. **Two `DoCrafting` prefixes**: idol upgrades First (our recipe), refinement Last with a `__runOriginal` check, so a
   refinement never rolls twice with another Forge mod.
9. Idol upgrades take the vanilla Forge duration (8 s + target quality), give no skill and no stats.
10. English strings (no new `$keys`); vanilla `$msg_upgrader_success`, `$inventory_upgraderbutton`,
    `$msg_missingrequirement`, `$inventory_needspace` reused.
12. **Base tier = the idol the recipe itself asks for** (vanilla data), not the item's biome. The two differ for some
    items (3.5b): the Flint axe asks for a Bronze idol, so it needs Bronze idols up to level 5 and Iron from 6, not
    the Wooden-then-Bronze of the user's example. Vanilla-consistent (the rule never asks for a lower idol than the
    game does), flagged at the pause; a curated item → tier table would be the alternative.
13. **The requirement is swapped during vanilla's own calls** instead of giving the recipe list per-level recipe
    copies: vanilla re-finds the selected row by recipe reference after each refinement (`GetSelectedRecipeIndex`),
    so a copy that changes at level 6 would lose the selection; the swap keeps every vanilla check, count and colour
    and other mods' patches on those methods.
14. **Network version 2** with the rules layout 2 (3.8). The mod is not released yet, so the version stays 0.1.0.

### Added beyond the request

- The idol choice (setting + click), no free-slot rule, in-place success, the Info log line per refinement, stars in
  every inventory view (grids, hotbar, drag, messages, radial), the warning about other Forge mods.

### Open questions

- None (the pause questions were answered on 2026-09-29).

### Unverified names and values

- None in the defaults (all prefab names checked in the runtime dump). The gamepad trigger of the cloned tab (LT, like
  the Craft tab) is unverified.

## 6. Tests

The list of record is `src/Crafting/Forge.IdolUpgrades/TESTING.md` (`./tools/Get-TestTodo.ps1 -Mod Forge.IdolUpgrades`).
