# Encyclopedia (Compendium idea) — design

| | |
|---|---|
| Mod | Encyclopedia (display name confirmed by the user at the pause: "rename it to encyclopedia or nest it under the raven compendium"; both done, decision 1) |
| GUID / project | `MC.Exploration.Compendium.Encyclopedia` (`src/Exploration/Compendium.Encyclopedia/`, root namespace `MC.Exploration.CompendiumEncyclopediaMod`); unchanged by the rename (decision 1) |
| Package name | `CompendiumEncyclopedia` (derived by removing the dots; no `<ModPackageName>` override) |
| Category / scope | Exploration / New |
| Side | Client (only the player who wants it installs it), multiplayer Compatible |
| Sheet idea | `Compendium` |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`; `.ref/game-version.json` is missing), decompiled `assembly_valheim`, `assembly_utils`, `assembly_guiutils` in `.ref/`; English strings checked in the 1.0.16 localization table (`valheim_Data/resources.assets`); prefab names checked in `StreamingAssets/SoftRef/manifest_extended`; third-party sources read on GitHub (2026-09-29) |
| Status | Implemented (0.1.0), build OK, smoke test passed, in-world self tests passed on earlier builds, after the review fixes, after the second review's fixes and again after the nesting under the Valheim Compendium (2026-09-29, build `00103ab+dirty`, runs `20260929-154511` and `20260929-154751`); no hands-on in-game test yet |
| Implementation | Done in two steps, both built: data layer first (catalog, discovery, own records, details, list rows, data patches, data self tests: 3.1-3.5, 3.9 data rows, 3.10), then the UI on top of it (button, window, input, UI patches, layout dumps, UI self tests: 3.6-3.9, 3.15). The UI deviations from the first design are decisions 29-45; the review fixes are decisions 4, 15 and 46-52, the second review's 53-55; the user's pause review (rename, nesting under the Valheim Compendium, optional side button) 1, 2, 12 and 56-63. |

**Naming.** The vanilla inventory already has a button and a dialog called **"Valheim Compendium"**
(`$inventory_texts`, `TextsDialog`: message log, active effects, lore texts, Player Statistics; the raven button).
The user calls it "Logs". The design first called the new window **Encyclopedia** (no vanilla string uses that word,
and no Thunderstore or Nexus mod was found with that name); the lead then picked "Compendium", and at the pause the
user asked to "rename it to encyclopedia or nest it under the raven compendium". Both are done (decision 1): the mod,
its window and its tab are **Encyclopedia**, and it lives **inside the Valheim Compendium** as its second top-level
tab (the first, **Texts**, is the game's own content). The GUID keeps the idea name as its system part
(`MC.Exploration.Compendium.Encyclopedia`, unchanged, permanent at the first release; it is also the assembly, config,
deploy folder and, without dots, package name). Internal names keep "Compendium" (classes such as `CompendiumWindow`,
objects `MC_Compendium_*`, self tests `compendium.*`, the Debug console command `compendium_debug`): nobody sees them,
and renaming them would only churn the code. In this document "Encyclopedia" means this mod; the vanilla dialog is
always "Valheim Compendium".
## Goal

Requirements from the user (2026-09-29), made precise:

1. **Nested under the raven, named Encyclopedia** (user request at the pause; the first version was a sixth side-panel
   button). The game's Valheim Compendium dialog (raven button, `InventoryGui.OnOpenTexts`) gets two top-level tabs at
   its top, styled like the game's own tabs (the crafting panel's Craft tab): **Texts** (its own content, unchanged)
   and **Encyclopedia**. The side panel stays exactly the game's. The earlier side button stays as an **opt-in
   setting** (`Display.SideButton`, default off): then it sits in the row of the vanilla controls, inside the panel's
   background, right after the Valheim Compendium button, with its own icon and a tooltip; the row is re-spaced evenly
   to make room while it is on and put back exactly when it is off (3.6 step 5).
2. **Window.** The Encyclopedia tab shows the Encyclopedia window in the dialog's place and size, drawn with the
   game's own frame, fonts and scroll bars (a stripped clone of the Valheim Compendium dialog), with the same two tabs
   on the same spot, so switching looks like a tab change; the Texts tab brings the Valheim Compendium back, its list
   filled fresh (3.6 "Nested tabs"). The two never show at the same time. The dialog reopens on the tab picked last
   until logout. With the optional button: while a vanilla side dialog (Skills, Valheim Compendium, Trophies,
   Achievements) is open, the side row is locked as in vanilla (1.1), so the button opens ours once that dialog is
   closed. While ours is open, the inventory behind it can be neither clicked nor reached with a controller (3.8), so a
   vanilla dialog can then only be opened by our Texts tab or another mod's code; if that happens, ours closes. Esc or
   B closes only the window, like the vanilla dialogs; the window's close button and a click outside its frame too.
   Controller: LT = Texts, RT = Encyclopedia in both.
3. **Everything is listed.** The window lists every player-facing **item** (materials, weapons, ammo, shields, armour,
   capes, utility items, trinkets, tools, torches, food, meads and potions, fish, trophies, misc items; trader goods and
   dungeon loot included, even when the mod finds no source for them in the loaded data), every **build
   piece** (hammer, hoe, cultivator; crafting stations and their upgrades included) and every **creature** (bosses
   included). The list is built at runtime from the game's own data (`ObjectDB`, piece tables, `ZNetScene`), so
   content added by other mods appears too. Entries are split into tabs (Weapons, Armor, Tools, Food, Materials,
   Trophies, Building, Creatures) with sub-group headers (3.2.6).
4. **Undiscovered = present but obfuscated.** An entry the character has not discovered is listed as a row with a
   "?" mark in the icon slot and **"???"** as its name. Its real icon and name appear nowhere: not in the row, not in
   its details, not in a tooltip, not in search results, and not through its position in the list (3.2.7).
5. **Discovered = icon and name.** A discovered entry shows its real icon (for creatures: their trophy's icon when that
   trophy is discovered too, else a generic creature icon, a paw print, so an unknown trophy's sprite never shows) and its
   translated name. "Discovered" follows what the game itself already records for the character
   (3.3), so existing characters see their progress at once.
6. **Details.** Clicking an entry (or moving to it with a controller) shows its details on the right: for items the
   description and stats, crafting recipe(s) with the crafting station and its level, upgrade costs, which creatures
   drop it, where it is gathered or processed, and what it is used in; for pieces the build cost, required station,
   comfort, and for stations their upgrades and recipes; for creatures where they live, what they drop, how to tame
   them (3.5).
7. **Undiscovered details are obfuscated.** Inside a discovered entry's details, anything that points at something the
   character has not discovered (an item, a piece or station, a tool, a saddle, a conversion input or output, a
   creature, a biome, a boss) is shown as "???" with the "?" mark instead of its icon and name (3.5.5). No detail line
   prints another entry's name without that check.
8. **Kill count.** Every discovered creature entry shows how many the character has killed (the game's own all-time
   count, 0 included).
9. **Live toggle**, like every MC mod: turning the mod off removes the tabs, the window and the optional button at once
   (the Valheim Compendium and the side panel exactly the game's again); turning it on brings them back at once.

Items 1-8 are the user's behaviours in order (2 also covers "an in-game UI window is displayed"; 1 and 2 were revised
at the pause: rename, nesting, opt-in button). Item 9 is the MC rule for every mod.

Non-goals (0.1.0): biomes, locations, dungeons, lore texts, tutorials, status effects, skills or achievements as
entries of their own (biomes appear only as details); 3D creature portraits; exact spawn rules (altitude, weather,
time of day, group size) and raids; the contents of dungeons, locations and traders that the game only loads on
demand (1.8); drop rates changed per creature by other mods at runtime; per-world knowledge (knowledge follows the
character, like vanilla); other players' knowledge; changing anything in the game (the mod only reads, plus two small
records of its own, 3.4); translations of the mod's own English labels; typing a search with a controller.

---

## 1. Vanilla behaviour (code trace)

### 1.1 The inventory side panel and its dialogs

- `InventoryGui` owns the whole Tab screen. The side panel is `m_info` (RectTransform, activated in
  `InventoryGui.Awake`); `m_infoPanel` (Transform) is never used in code. The side controls are **not** `InventoryGui`
  fields, except the PvP toggle `m_pvp` (`Toggle`, runtime listener `OnPvpChanged` added in `OnEnable`).
- Handlers: `OnOpenSkills`, `OnOpenTexts`, `OnOpenTrophies`, `OnOpenAchievements` (all public) and `OnCloseTrophies`,
  `OnCloseAchievements`. **No code in `.ref` calls them**: the buttons call them through listeners saved in the prefab
  (persistent `onClick` calls). A persistent listener can be read with `onClick.GetPersistentMethodName(i)`;
  PraetorisClient finds the Texts button exactly that way in 1.0 (2).
- `OnOpenTexts`: `SetActiveGroup(m_uiGroups[2])`, then `m_textsDialog.Setup(player)`. `OnOpenSkills` and
  `OnOpenTrophies` also activate `m_uiGroups[2]`; `OnOpenAchievements` does not. Opening one dialog never closes the
  others in code.
- `InventoryGui.Awake` deactivates every dialog: `m_trophiesPanel`, `m_achievementsPanel`, `m_variantDialog`,
  `m_skillsDialog`, `m_textsDialog` (so a clone of `m_textsDialog` starts inactive).
- `IsSkillsPanelOpen`, `IsTextPanelOpen`, `IsTrophisPanelOpen`, `IsAchievementsPanelOpen` are read only by
  `KeyHints.UpdateHints` (private), which hides every controller hint while one of them is open.
- Element templates (`.ref`): `m_recipeElementPrefab` (children `icon`, `name`, `Durability`, `QualityLevel`,
  `selected`, a `Button`; rows are spaced `m_recipeListSpace`, default 30); `m_recipeRequirementList[]` slots
  (`res_icon`, `res_name`, `res_amount`, `UITooltip` on the root, filled by the static
  `InventoryGui.SetupRequirement`); `m_tabCraft` / `m_tabUpgrade` (Buttons; the current tab is shown by
  `interactable = false`); `m_recipeIcon`, `m_recipeName`, `m_recipeDecription`, `m_craftingStationIcon`,
  `m_craftingStationName`. `m_secretAchievementIcon` (Sprite) exists but is never used in code.
- The side panel (prefab data, read from the Debug dump of 1.0.16 at 2560x1440, 3.15): `root/Info`, 570×130 units,
  anchored top-right, carrying the `UIGroupHandler` that is `m_uiGroups[2]`, **no layout group**. Children: `Darken`
  (shadow blob, 100 units larger), `selected_frame` (off), `Bkg` (the wood panel, sliced `woodpanel_info_180`, 10 units
  larger than `Info` on every side), `TitlePanel` (character name and braid lines), then five controls in one row:
  `Texts` (`OnOpenTexts`), `Skills`, `Trophies`, `Achievements` (buttons) and `PVP` (`m_pvp`, a `Toggle`), each 64×64
  units, centres at x = -200, -100, 0, 100, 200 and y = -12 around the panel centre, each with a `Background` child
  (round `point3` shade, 104 units) and an icon child (`Icon` or `Image`). Their navigation is `Explicit` (left/right
  neighbours). The icon sprites are light; the gold colour comes from the button's colour tint (our own icon showed
  that: its colours came out multiplied by the tint).
- **The side row is locked while a side dialog is open** (same dump, focus groups): `root` (the inventory's root,
  parent of `Info` and of the dialogs) has a `CanvasGroup` and a `UIGroupHandler` at priority 1; `Info` is priority 1
  too; `Texts`, `Skills`, `Trophies` and `Achievements` are priority 2, each with its own `CanvasGroup`. While one of
  them is active, `UIGroupHandler.Update` (`assembly_guiutils`) makes `root`'s `CanvasGroup` non-interactable, so every
  side control under `root/Info` (a clone added there too, it has no `CanvasGroup` of its own) ignores clicks, and the
  `Info` group is inactive, so a `UIGamePad` of the side panel does not fire (`UIGamePad.IsInteractive`). The `Texts`
  dialog is also a later sibling of `Info` with a full-screen `Closebutton` (click outside its frame), so a click where
  a side button is closes the Valheim Compendium instead. Result: a side button can never open its dialog over another
  one.

### 1.2 Closing keys and the frame counters (`InventoryGui.Update`, `Hide`, `Show`)

- `Update` first increments `m_shownFrames` while the Animator bool `"visible"` is set (else `m_hiddenFrames`). With
  no local player, a dead player, a cutscene or a teleport it calls `Hide()` and returns (so `Hide` runs every frame
  while dead).
- The key block runs only when `m_craftTimer < 0`, `Chat.HasFocus()` is false, the console, `Menu` and `TextViewer`
  are closed, no cutscene, no free-fly camera and the minimap is closed. Then Esc (`ZInput.GetKeyDown(KeyCode.Escape)`)
  or `JoyButtonB` closes the **first** open one of: trophies, achievements (details first), skills, texts
  (`SetActive(false)` directly, not `TextsDialog.OnClose`), split dialog, variant dialog. If none is open and the
  inventory is visible, `Inventory`, `JoyButtonB`, `JoyButtonY`, Escape or `Use` call `Hide()`, **only when
  `m_shownFrames > 1`**, after resetting those named buttons.
- The compiled check (1.0.16, `ilspycmd -il`) is `ldfld m_shownFrames; ldc.i4.1; cgt; ldloc flag2; and; brfalse`:
  the compiler folds `&& flag2` into an `and`, so there is no branch right after the constant.
- `m_shownFrames` is private (publicized), read only there, and reset only by `Hide`. Setting it to 0 before
  `Update` runs makes that frame's check `1 > 1` false: the inventory stays open, nothing else changes. Escape itself
  cannot be reset (it reads the Input System directly); named buttons can (`ZInput.ResetButtonStatus`).
- `Hide()` (when visible) closes trophies, achievements, variant, skills and texts (again with `SetActive(false)`),
  the split dialog and the drag, releases the container. `Show(Container, int)` sets the Animator bool, the active
  group, `m_hiddenFrames = 0`; it does no layout.
- `InventoryGui.IsVisible()` is `m_hiddenFrames <= 1`. `Menu.Update` opens the pause menu on Esc only when the
  inventory is not visible, so Esc in the inventory never reaches the pause menu.

### 1.3 Gamepad focus (`UIGroupHandler`, `UIGamePad`)

- `UIGroupHandler` (`assembly_guiutils`, not publicized): every instance registers in a static list in `Awake`. In
  `Update`, a group is active when it is user-active (`SetActive(true)`) and **no other group that is active in the
  hierarchy has a higher `m_groupPriority`** (the other group's own user-active flag is not checked). An active group
  makes its `CanvasGroup` interactable, and auto-selects `m_defaultElement` for a gamepad.
- `InventoryGui.m_uiGroups[]`: 0 container grid, 1 player grid, 2 side panel, 3 crafting (MC docs, consistent with
  `SetActiveGroup` callers). `m_inventoryGroup` gates `UpdateGamepad` (LB/RB = `JoyTabLeft`/`JoyTabRight` cycle the
  groups; group 3 reads recipe input). `SetActiveGroup(int)` / `SetActiveGroup(UIGroupHandler)` are private
  (publicized); the second uses `Array.IndexOf`, so a group outside `m_uiGroups` gives index -1, then clamps to 0.
- So a panel with its own `UIGroupHandler` whose priority is above every inventory group turns every inventory group
  off while it is active: no grid, crafting or side-panel gamepad input, no LB/RB cycling, their `CanvasGroup`s not
  interactable. **Trap**: if our panel sits under one of those `CanvasGroup`s, it becomes non-interactable too, unless
  it has its own `CanvasGroup` with `ignoreParentGroups`.
- `UIGamePad` fires its `Button` on `m_zinputKey` / `m_keyCode` only while the button `IsInteractable()` and its
  group is active, with one 2-frame lock shared by every pad. A pad with **no** parent group is always interactive.
  `m_hint` may be null. Details that matter here:
  - `Start` sets the private `m_group = GetComponentInParent<UIGroupHandler>()`: the **nearest** parent group, which is
    not necessarily the one the inventory uses for that area (Sort Chest's `FixPadGroups` exists for this reason).
  - `Update` checks `IsInteractive()`, then the static lock (`Time.frameCount - m_lastInteractFrame >= 2`), then
    `ButtonPressed()`. When all three pass it sets `m_lastInteractFrame` **before** calling `m_button.OnSubmit(null)`,
    with no `try`. So a pad that accepts a press takes the lock for every other pad even if our click handler then
    ignores it; a `ButtonPressed` that returns false never takes it.
- `UIGroupHandler.IsHighestPriority` has no visibility check, and `InventoryGui.Hide` only clears the Animator bool
  (no `SetActiveGroup`). While the inventory is hidden its groups stay in the hierarchy (`docs/game/ux.md`), so the
  side-panel group can still count as active. View/Select (`JoyBack`) is the same physical button as `JoyMap` on
  non-PlayStation layouts (`ZInput`), and `Minimap` opens the map with it when the inventory is not visible: players
  press it all the time in the world.
- A higher-priority group only turns the other groups' `CanvasGroup.interactable` off, which affects `Selectable`s.
  Inventory slots take the mouse through `UIInputHandler` pointer handlers (`assembly_guiutils`), which ignore
  `interactable`; `InventoryGrid` gates only its gamepad paths on `m_uiGroup.IsActive`. So a priority group alone does
  **not** stop mouse drags, right-click use and split on the grids.

### 1.4 `TextsDialog` (the clone source)

- Fields: `m_listRoot` (left list content), `m_leftScrollRect`, `m_leftScrollbar`, `m_rightScrollbar`,
  `m_elementPrefab` (children `name`, `selected`, a `Button`), `m_spacing` (default 80), `m_textAreaTopic`,
  `m_textArea` (TMP), `m_recipeEnsureVisible`, `m_totalSkillText`.
- `Setup` → `FillTextList`: destroys the previous rows, calls `UpdateTextsList` (known texts sorted by topic, then
  `AddLog`, `AddActiveEffects`, `AddStats`), instantiates **one row per entry** under `m_listRoot` at `-i * m_spacing`,
  sizes the list with `SetSizeWithCurrentAnchors`. `ShowText` localizes topic and text, toggles `selected`.
- `Update` → `UpdateGamepadInput` runs whenever the dialog is active, only while `ZInput.IsExclusiveGamepadActive()`:
  D-pad up/down or left stick Y (**Y > 0.1 means down**) moves the selection with a 0.1 s delay and a rumble
  (`GamepadRumble.instance.PlayGlobalSelectVibration`), right stick Y scrolls `m_rightScrollbar`. It does not check any
  `UIGroupHandler`.
- `OnClose` = `SetActive(false)`. Other mods patch this class: Jewelcrafting (`AddActiveEffects`, `OnSelectText`,
  `OnClose`), EquipmentAndQuickSlots (`UpdateTextsList`), MC Creature Kill and Tame Counts (`AddStats`).
- The prefab (1.0.16, Debug dump "Encyclopedia texts dialog source", 3.15), in the dialog's 1920×1080 units: root
  `Texts` (full screen; `TextsDialog`, `CanvasGroup`, `UIGroupHandler`, `UIGamePad`) with `blur` (off), `darken`
  (full-screen black at 39 %), `Closebutton` (full-screen, transparent, persistent `OnClose`: a click outside the frame
  closes the dialog) and `Texts_frame` (1200×800, centred): `bkg` (wood panel), `topic` (title "Valheim Compendium",
  Norsebold 32, orange), `TextList` (left pane box, dark `item_background` image, 315×734) holding `SkillListScroll`
  (bar) and `SkillList` (`ScrollRect` + `RectMask2D`, content `ListRoot` = `m_listRoot`, no viewport; the row template
  `SkillElement` = `m_elementPrefab` sits inside it, inactive), `TextArea` (right pane box, 840×641, ending above the
  close button) holding `TextScroll` (bar) and `ScrollArea` (`ScrollRect` + `RectMask2D`, content `Content` with a
  `VerticalLayoutGroup` holding `Name` = `m_textAreaTopic`, AveriaSerifLibre 32, and `Description` = `m_textArea`,
  size 20), and a second `Closebutton` ("Close", 200×46, bottom centre-right, persistent `OnClose`, B glyph hint).
- **Trap**: `m_leftScrollRect` points at the **right** scroll view (`ScrollArea`); vanilla only uses it to size the
  left bar. The list's scroll view has to be found by structure (the `ScrollRect` above `m_listRoot`).
- **Free space on the title line** (same dump): the title `topic` spans x -166..166, y 352..389 in the dialog's units;
  the panes start at y 349 and x -587; so the title line left of the title (x -587..-176) is empty in the vanilla
  dialog, and in our window too (its "Discovered N / M" counter takes the right part, its category tabs sit under the
  line at y 317..349). The wood frame `bkg` spans -610..610 × -410..410.
- **Controller keys in the dialog** (Debug dump of our tabs on it, 1.0.16): its three `UIGamePad`s (on `Texts`,
  `TextList` and the "Close" button) have **no key** (`m_zinputKey` empty, `m_keyCode` None); B is read by
  `InventoryGui.Update` (1.2). `TextsDialog.UpdateGamepadInput` reads only the D-pad up/down, the left stick Y and the
  right stick Y. The crafting panel's `m_tabCraft` / `m_tabUpgrade` carry pads with LT / RT hints (its gamepad hint
  reads "LTrigger"), in the crafting group, inactive while a priority-2 dialog or our window is up (1.3). Other vanilla
  readers of `JoyLTrigger` / `JoyRTrigger`: `InventoryGrid` (split and move modifiers, only while its grid group is
  active), `BuildUi` (build menu), `Chat`, `Console`, `Menu`, `Minimap` (only in combinations with LB, Start or
  View/Select), the free-fly camera; the player's block / attack go through `Player.TakeInput`, off while the inventory
  is visible. MC mods: Loot Pickup Filter reads LT / RT only after its R3 press on an active player grid.
- **Clones of the dialog by other mods** (PraetorisClient's Server Guide, 2): whatever sits in the vanilla dialog when
  they `Instantiate` it is copied. Runtime `onClick` listeners are not copied, components are.

### 1.5 What vanilla records per character (`Player`)

All sets are private (publicized), saved by `Player.Save` and read by `Player.Load` in the `.fch`, so they follow the
character across worlds and are **retroactive** for existing characters.

| Set | Key | Written by | Meaning |
|---|---|---|---|
| `m_knownMaterial` (`HashSet<string>`) | item token `m_shared.m_name` | `Player.AddKnownItem`, called by `OnInventoryChanged` for **every** item in the inventory (not while loading) and by `ItemSets.AddKnown`; `Trader.DiscoverItems` also calls it but has no caller in 1.0.16 | The item has been in the inventory at least once. Read: `IsMaterialKnown` / `IsKnownMaterial`. |
| `m_knownRecipes` (`HashSet<string>`) | recipe item tokens **and** piece names (one namespace) | `Player.UpdateKnownRecipesList` (from `Awake`, `AddKnownItem`, `AddKnownStation`, `OnInventoryChanged`) → `AddKnownRecipe` / `AddKnownPiece` | Recipe: enabled or in season, station known at `m_minStationLevel` (`KnowStationLevel`), DLC installed, every requirement with `m_amount > 0` a known material (one is enough for `m_requireOnlyOneIngredient`; upgrade-only materials skipped outside a station). Piece: only pieces of piece tables **in the inventory**, enabled or in season, station name known (any level), DLC installed, every requirement a known material (`HaveRequirements(piece, IsKnown)`). Read: `IsRecipeKnown`. |
| `m_knownStations` (`Dictionary<string,int>`) | `CraftingStation.m_name` → highest level seen | `AddKnownStation` from `CraftingStation.UpdateKnownStationsInRange` (every second, within `m_discoverRange`, 4 m by default), `SetCraftingStation`, `PlacePiece`, `ItemSets` | Station seen, with the highest level (1 + upgrades) seen. |
| `m_knownBiome` (`HashSet<string>`) | `BiomeSector.GetName()` = **localized** biome name, with alternate-biome override, prefix and suffix | private `AddKnownBiome(BiomeSector)`, from `UpdateBiome` once a second when the sector changes | Biome visited. **Language dependent** (1.10). Read: `IsBiomeKnown(BiomeSector)`. |
| `m_trophies` (`HashSet<string>`) | trophy **prefab name** (`m_dropPrefab.name`) | `AddTrophy`, from `AddKnownItem` for `ItemType.Trophy` | Trophy held once. `GetTrophies()` copies the set. |
| `m_knownTexts`, `m_shownTutorials`, `m_uniques` | label / key | runestones, ravens, tutorials, location names, player keys | Not used by 0.1.0. |
| `m_customData` (`Dictionary<string,string>`) | free | nobody in vanilla | The mod's own records (3.4). Vanilla keeps unknown keys. |

- Resets: `Player.ResetCharacter` (console `resetcharacter`, a cheat) clears recipes, stations, materials, uniques,
  trophies, skills, biomes, texts, seen tutorials and the guardian power cooldown. It keeps `m_customData` (so other
  mods' keys, such as Creature Kill and Tame Counts' tames, survive) and never touches the profile statistics (1.6:
  kills, placed pieces...). `Player.ResetCharacterKnownItems` (console `resetknownitems`, **not** a cheat; also
  `ItemSets`) clears recipes, stations, materials and trophies only.
- World modifiers: `Player.GetAvailableRecipes` shows every recipe with `GlobalKeys.AllRecipesUnlocked` (or the debug
  `m_noPlacementCost`); `UpdateAvailablePiecesList` shows every piece with `GlobalKeys.AllPiecesUnlocked` or
  `m_noPlacementCost`.
- Seasons: `Player.CurrentSeason` (`SeasonalItemGroup`, fields `Pieces`, `Recipes`); the private
  `m_seasonalItemGroups` list holds every group. Disabled seasonal recipes and pieces can be learned only in season.

### 1.6 Profile statistics (`PlayerProfile.m_playerStats[0]`)

- `Game.instance.GetPlayerProfile().m_playerStats[0]` is the lifetime slot (index 0 = `RawStats`, always written;
  the other slots are achievement copies; `PlayerProfile.GetStat` reads one of those copies, never use it). Saved per
  character in the `.fch`.
- `m_enemyStats[0]` (`Dictionary<string,float>`, index = `KillModifiers.MixedAndTotal`) = all-time kills keyed by
  `Character.m_name`; written by `PlayerProfile.IncrementStatEnemy` from `Game.RPC_RegisterKill`, which the victim's
  owner sends to **every player who hit it** (full rules: `docs/design/exploration-stats-per-creature.md` 1.1-1.2).
  Per-creature totals exist since character file version 42 (Call to Arms).
- Other per-key dictionaries: `m_itemPickupStats` (item token, stack sizes), `m_itemCraftStats` (item token;
  `InventoryGui` crafting and `CookingStation`), `m_foodEatenStats` (item token), `m_piecesPlacedStats` (piece
  name), `m_pickableStats` (pickable item **prefab name**; fish by `Fish.m_name`). Values are floats; round them.
- Tames per creature: vanilla keeps only a total. MC Creature Kill and Tame Counts stores per-creature tames in
  `Player.m_customData["MC.Exploration.Stats.PerCreature.Tames"]`, documented format v1 (first line `1`, then
  `count<TAB>name` lines), readable without a reference (its README, "For mod authors").

### 1.7 How vanilla hides unknown things

- Crafting list: `Player.GetAvailableRecipes` returns only known recipes. Requirement panel:
  `InventoryGui.SetupRequirementList` hides unknown materials only for `m_requireOnlyOneIngredient` recipes.
- Build menu: `PieceTable.UpdateAvailable` skips pieces not in `m_knownRecipes`.
- Trophies panel: `InventoryGui.UpdateTrophyList` instantiates only owned trophies (gaps, no silhouettes); the text is
  `Localize(token + "_lore")`.
- Achievements: locked icon; a secret, locked achievement shows the literal `"???"` and `"??? / ???"`
  (`AchievementsGui.OnOpenAchievementDetails`). **This is the vanilla precedent for "???".**
- Creature names are never hidden in vanilla (`EnemyHud` always shows `GetHoverName()`).

### 1.8 Game data reachable in a world

| Source | What | Notes |
|---|---|---|
| `ObjectDB.instance.m_items` (`List<GameObject>` with `ItemDrop`) | Every item, including creature attack items, test items and hair/beard customizations | `m_shared` (`SharedData`): `m_name`, `m_description`, `m_icons` (`Sprite[]`; `ItemData.GetIcon()` indexes it and throws on an empty array), `m_itemType`, `m_maxQuality`, `m_dlc`, `m_buildPieces` (a `PieceTable` on tools), food and combat values. Sort Chest's log counted 1522 item prefabs in 1.0.16. |
| `ObjectDB.instance.m_recipes` (`List<Recipe>`) | Recipes | `m_item`, `m_amount`, `m_enabled`, `m_craftingStation`, `m_repairStation`, `m_minStationLevel`, `m_requireOnlyOneIngredient`, `m_resources` (`Piece.Requirement`: `m_resItem`, `m_amount`, `m_amountPerLevel`, `m_upgraderResource`). `Requirement.GetAmount(q)`, `Recipe.GetRequiredStationLevel(q)` = `max(1, m_minStationLevel) + q - 1`, `Recipe.GetRequiredStation(q)` (falls back to `m_repairStation` for q > 1 without a crafting station). |
| Piece tables (`SharedData.m_buildPieces.m_pieces`, `m_categories`, `m_categoryLabels`) | Build pieces per tool | `Piece`: `m_name`, `m_description`, `m_icon`, `m_enabled`, `m_category` (`PieceCategory`: Misc, Crafting, BuildingWorkbench, BuildingStonecutter, Furniture, DeepNorth, Feasts, Food, Meads; All = 100), `m_resources`, `m_craftingStation`, `m_comfort`, `m_dlc`, `m_repairPiece`, `m_removePiece`. Stations: `CraftingStation` (`m_name`, `m_icon`, `GetLevel()` = 1 + extensions); upgrades: `StationExtension.m_craftingStation`. |
| `ZNetScene.instance.m_prefabs` (hard `List<GameObject>`) | Every networked prefab: creatures, pickables, rocks, trees, chests, smelters, nests | `Character` (`m_name`, `m_faction`, `m_boss`, `m_defeatSetGlobalKey`, `m_health`, `m_damageModifiers`, `m_hideHud`, `m_killedForAchievements`), `CharacterDrop.m_drops`, `Tameable` (`m_saddleItem`), `MonsterAI.m_consumeItems`, `Procreation.m_offspring`, `Growup.m_grownPrefab`; sources: `Pickable` (`m_itemPrefab`, `m_extraDrops`), `PickableItem`, `MineRock`/`MineRock5` (`m_dropItems`), `TreeBase`/`TreeLog`, `Destructible.m_spawnWhenDestroyed`, `DropOnDestroyed`, `Container.m_defaultItems`, `Smelter`/`CookingStation`/`Fermenter` (`m_conversion`: `m_from`, `m_to`), `Beehive.m_honeyItem`, `SapCollector.m_spawnItem`. Creature equipment: `Humanoid.m_defaultItems`, `m_randomWeapon`, `m_randomArmor`, `m_randomShield` (`GameObject[]`), `m_randomSets[].m_items`, `m_randomItems[].m_prefab`, `m_unarmedWeapon` (`ItemDrop`; the player's is `PlayerUnarmed`). |
| `ZNetScene.instance.m_nonNetViewPrefabs` (`List<GameObject>`) | Prefabs without a `ZNetView`, also registered by name in `ZNetScene.Awake` | `Trader` (`m_items`: `TradeItem.m_prefab`) has no `ZNetView` (it is local logic, `docs/game/exploration-world.md`), so it can never be in `m_prefabs`. The prefabs `Haldor`, `Hildir` and `BogWitch` exist in `manifest_extended`; the 1.0.16 in-world run finds 3 `Trader` components in the loaded prefabs (split per list in the Debug scan line, 3.2.3). |
| `ZoneSystem.instance.m_vegetation` (`ZoneVegetation`: `m_prefab` hard, `m_biome`, `m_enable`) | What grows or lies in each biome | Gives the biome of pickables, rocks, trees, nests (`SpawnArea.m_prefabs`, `CreatureSpawner.m_creaturePrefab`). |
| Spawn lists: `ZoneSystem.m_zoneCtrlPrefab`'s `SpawnSystem.m_spawnLists` plus loaded `SpawnSystemList`s; `AltBiomeList.m_altBiomes` (static; `AltBiome.m_spawn`, `m_biome`) | World spawns (`SpawnSystem.SpawnData`: `m_prefab`, `m_enabled`, `m_biome`, `m_requiredGlobalKey`) | Fish items spawn here too. Sort Chest's `BiomeIndex.ScanSpawns` already walks them. |
| **Not reachable cheaply** | Locations (`ZoneSystem.ZoneLocation.m_prefab`) and dungeon rooms (`DungeonDB.RoomData.m_prefab`) are `SoftReference<GameObject>` | Boss altars, dungeon-only creature spawners, dungeon loot spawners and trader locations are inside them. Loading every location to find the traders would be far too expensive. Whether the altar components also exist in `ZNetScene` is **unverified** (logged, 3.2.3). |

- When it exists: the main menu has an `ObjectDB` copy but no `ZNetScene`, no player and no `InventoryGui`. In a
  world, `ObjectDB.Awake`, `ZNetScene.Awake`, `ZoneSystem`, `AltBiomeList.Awake` run before the player spawns; mods
  using Jotunn register items, pieces and creatures at those points; server-synced config mods may change recipes or
  drops **after** connecting. The Encyclopedia can only open in a world with a local player, so building on first
  open is always late enough.
- Do not reuse `ObjectDB.GetAllCraftableWeapons` / `GetAllCreatableFood` / `GetAllFoodItems` / `GetAllBuildPieces`:
  they cache on first call and never refresh.
- `Achievements.FindCreaturesForAchievements` lists `ZNetScene` characters whose `m_killedForAchievements ==
  Utils.AchievementInclusion.Included` (enum Undefined, Included, Excluded), deduplicated by `m_name`;
  `Character.ValidatePrefabSetup` logs an error for Undefined. Which vanilla creatures are Excluded is prefab data
  (**unverified**, logged). The Encyclopedia does **not** use it to hide creatures: babies, hens or quest creatures
  could be Excluded and they are real creatures a player meets.
- Development prefabs in 1.0.16 (`manifest_extended`): items `CapeTest`, `SwordCheat`, `SledgeCheat`; character
  `DvergerTest` (in the `Characters/Dverger` folder; whether it is in `ZNetScene` is **unverified**). Their names end
  in `Test` or `Cheat`.

### 1.9 Drops (`CharacterDrop.GenerateDropList`)

- Per `Drop`: chance `m_chance`, × 2^(level-1) when `m_levelMultiplier`; chances ≤ 0.3 use pseudo-random counters
  unless `GlobalKeys.NoPseudoDrops` (same average rate). Amount = `Random.Range(int min, int max)` (Unity: **max
  exclusive** when max > min; min when equal) or `Game.ScaleDrops` with the world resource rate; × 2^(level-1) when
  `m_levelMultiplier`; `m_onePerPlayer` = number of players; capped at 100.
- So at normal world settings a 0-star creature drops `m_amountMin` to `m_amountMax - 1` (or exactly `m_amountMin`)
  with chance `m_chance`.

### 1.10 Biome names and discovery

- `BiomeSector.GetBiomeName(biome)` = `"$biome_" + biome.ToString().ToLower()` (tokens `$biome_meadows`,
  `$biome_blackforest`, ... exist in the English table). `BiomeSector.GetName()` applies the alternate biomes'
  `m_nameOverride` / `m_namePrefix` / `m_nameSuffix` and returns **`Localization.instance.Localize(...)`**.
- `Player.AddKnownBiome` stores that localized string; saves older than `ChunkedNorth` are loaded as the raw token.
  So a biome found in French is stored as its French name, and after switching to English `IsBiomeKnown` is false
  again (vanilla even shows "biome found" again). Reading `m_knownBiome` reliably therefore needs matching against the
  token and the current-language names; the mod also keeps its own language-independent record (3.4).

### 1.11 First encounter: `EnemyHud`

- `EnemyHud.LateUpdate` sets its reference point to the **local player's position** (`m_refPoint`, not the camera)
  and calls the private `ShowHud(Character c, bool isMount)` every frame for every character (except the local
  player, the ridden mount and `m_hideHud` characters) that `TestShow` accepts: within `m_maxShowDistance` of that
  point, or a boss within `m_maxShowDistanceBoss` (100 m) that is alerted (a boss within 100 m that is not alerted gets
  no plate at all, even close). `TestShow` is a distance test only: no line of sight, no camera. The code default of
  `m_maxShowDistance` is 10, the 1.0.16 prefab value is **30 m** (logged by `compendium.knowledge`). `ShowHud` creates
  the plate object (`HudData`, its `m_gui` instantiated and set active) only when `m_huds` has no entry for `c`.
- **Created is not shown.** `UpdateHuds` (called at the end of the same `LateUpdate`, before anything is drawn) then
  switches every plate on or off: players, bosses and the mount always on; any other creature only while
  `m_hoverTimer < m_hoverShowDuration` (60 s). The timer starts at 99999 and is reset to 0 only while the creature is
  `Player.GetHoverCreature()`: `Player.FindHoverObject` casts the camera-forward ray (50 m, `m_interactMask`: items,
  pieces, `Default`, `static_solid`, terrain, characters...), sorts the hits by distance, skips the player's own body
  and looks at the **first** hit only: it counts only when that collider is a `Character` that is not asleep and not
  hidden by mist, and the loop ends there either way. So a wall, a built piece, terrain, a tree or a rock in front of
  the creature stops the ray: aiming at a creature through them never resets its timer. A non-boss plate is also off
  while the creature is behind the camera. So an ordinary creature's name plate appears only after the player aimed
  at it within 30 m with nothing in between, and stays 60 s. A plate created within range but never shown is
  invisible: "a plate was created" does **not** mean the player saw the creature (the first design made that
  mistake). Plates keep their state while the HUD is hidden (Ctrl+F3 only hides `m_hudRoot`).

### 1.12 Localization

- `Localization.instance.Localize(text)`: replaces `$words`, a missing key gives `"[word]"`, results go in an LRU cache
  of **100** entries. `Localization.OnLanguageChange` (static `Action`) fires after a language switch.
- Names can carry TMP rich text (e.g. `<color=orange>`), to strip for sorting and search.
- Vanilla tokens reusable for labels (1.0.16 English): `$hud_materials` Materials, `$hud_food` Food, `$hud_building`
  Building, `$inventory_trophies` Trophies, `$hud_crafting` Crafting, `$inventory_dmgmod` Damage modifier, damage type
  words (`$inventory_blunt` ...), modifier words (`$inventory_weak`, `$inventory_resistant`, `$inventory_immune` ...),
  `$se_health` Health, `$menu_close` Close, `$biome_*`, `$skill_*`. No token exists for "Weapons",
  "Armor", "Creatures", "Drops", "Kills", "Biome", "Search".

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28/29 (backlog research, `docs/research/existing-mods-exploration.md` §15, source files on GitHub).

| Mod | How | Lesson |
|---|---|---|
| Almanac (RustyMods, 3.8.0, 1.0) | Items, pieces, creatures and much more in a HUD window opened by **taking over the Trophies button** (`OnOpenTrophies` prefix returns false, `UpdateTrophyList` prefix skipped). Unknown items get a black silhouette + "???"; creature known after 1 kill; "Hide Unknown" option. Rows recreated per tab. Patches `InventoryGui.Awake`, `Hide` (prefix skips it while its search is focused), `IsVisible` (postfix ORs its panel), `Chat.HasFocus`, `Player.TakeInput`, `PlayerController.TakeInput`. | Never touch the Trophies button; never use the real sprite for an unknown entry (a silhouette leaks the shape); gate our per-frame code on the Animator bool, not `IsVisible()`. |
| VNEI (MSchmoecker, 0.17.6, 1.0) | Item/recipe browser in an extra crafting tab; paged icon grid; "Show only known" hides unknown entries. Paths `Crafting/RecipeList`, `Crafting/Decription`. | Paging/virtualization for ~1500 entries; no overlap with the side panel. |
| PraetorisClient "Server Guide" (jneb802, 1.0) | Finds the Texts button by its persistent method `"OnOpenTexts"`, clones `m_textsDialog`, destroys the `TextsDialog` component, retitles the text containing "compendium", closes vanilla dialogs on open, `SetAsLastSibling`. `InventoryGui.Update` prefix returns false on Esc/B while its window is open; `Hide` postfix closes it. | The closest pattern. We keep vanilla `Update` running (1.2: reset `m_shownFrames` instead of skipping the method, which would also skip other mods' transpiled code). |
| Jewelcrafting (blaxxun) | Adds a page to the vanilla Compendium; transpiles `InventoryGui.Update`; cleans its elements in `TextsDialog.OnClose` only. | A clone of `m_textsDialog` may carry its leftover elements: strip foreign components and children. Close the vanilla dialog with `OnClose`. |
| AugaLite (ZenDragonX) / Auga, Veneer (Slatyo) | Replace the inventory; AugaLite destroys `root/Info` and uses 3 `m_uiGroups`; Veneer hides `root`. | No anchor → one warning, no button. Never index `m_uiGroups[3]` blindly. |
| ExtraSlots (shudnal), EquipmentAndQuickSlots | Panels to the right of `m_player`. | Possible overlap with the side column: cross-mod check. |
| TrophyTooltip (vaSto), TrophyHuntMod (oathorse) | Trophy tooltips with HP, resistances, drops, kills. | Same data; no UI overlap. |
| Bestiary (Radamanto) | A **creature pack** (new creatures, not a UI). | Name to avoid; its creatures should appear in our Creatures tab (content from `ZNetScene`). |
| MC Creature Kill and Tame Counts | Per-creature kills and tames at the top of Player Statistics; stable read contract. | Kills: read vanilla directly; tames: read its documented key softly, no dependency. |

Conclusion: a separate, modal, vanilla-looking window cloned from the Compendium dialog, a data catalog built from the
game's own tables (so modded content appears), spoiler-safe placeholders that never touch the real sprite, reuse of
vanilla knowledge sets (retroactive), and virtualized rows.

---

## 3. Design

### 3.1 Core idea and structure

Three layers, each testable on its own through the Debug log (and the in-world self tests, 3.15):

1. **Catalog** (`Catalog/`): what exists. Built lazily on the first open of a world session from `ObjectDB`, the piece
   tables, `ZNetScene`, vegetation and spawn lists; cached; rebuilt when a fingerprint changes. Holds entries, tabs,
   sub-groups and the relation indexes (recipes, drops, sources, uses, habitats). No player data. Owned by
   `CatalogService` (build, handle, swap); `ListBuilder` turns (catalog, knowledge, tab) into spoiler-safe list rows.
2. **Discovery** (`Discovery/`): what this character knows. `Knowledge.Take` reads the vanilla sets and stats (1.5,
   1.6), the tames of Creature Kill and Tame Counts (`TamesReader`), and the mod's two own records (`OwnRecords`,
   3.4). Recomputed on every open, tab change and search change (a few thousand hash lookups). Never cached across
   opens, never writes vanilla data.
3. **Details** (`Details/`): `DetailBuilder` turns (entry, knowledge) into a UI-agnostic `DetailView` (title, icon,
   subtitle, lines of text and `RefTarget` parts already resolved against the knowledge); `Presentation` is the only
   spoiler-safe way to get an entry's or biome's name and icon.
4. **UI** (`Ui/`): the two top tabs on the Valheim Compendium (`TopTabs`), the window (top tabs, category tabs,
   search, virtualized list, detail pane), input, the optional side button. The window only renders `ListRow`s and
   `DetailView` lines.

Flow: raven button → Valheim Compendium with the two tabs → Encyclopedia tab (or the remembered tab, or the optional
side button) → close vanilla dialogs → window shown in the dialog's place → `CatalogService.EnsureReady()` (null while built over a
few frames: "Preparing entries..." row; `CatalogReady` fires when done) → `OwnRecords.RecordCurrentBiome()` →
`Knowledge.Take` → `ListBuilder.BuildTab` → selection → `DetailBuilder.Build`.

### 3.2 Catalog

#### 3.2.1 Entries

`Entry` (`Catalog/Entry.cs`): `Index` (dense position in `Catalog.Entries`, the knowledge snapshot indexes by it),
`Kind` (Item | Piece | Creature), `Key`, `NameToken`, `Prefab` / `PrefabName` (representative), `Tab`, `SubGroup`,
`GameOrder`, `Icon` (real sprite for items and pieces; null for creatures, whose icon follows the trophy rule),
`HiddenUntilKnown` + `HiddenRule`, `Seasonal`, and one kind record: `ItemInfo` (prefabs, `ItemDrop`, `ItemKind`,
recipes, `DroppedBy`, `Gathered` per `GatherKind`, chests, `MadeFrom` conversions, `ProducedBy`, fish biomes, trader
count, `PlaceableWith`, `UsedIn` links, `Tames`, `HasSource`), `PieceInfo` (`Piece`, tool entry, category label,
`StationName`, `ExtensionOf`, `Extensions`, `RecipesHere`, `Conversions`, `Produces`) or `CreatureInfo` (prefabs,
representative `Character`, drops, trophy entry, habitat mask and its source, `AfterBosses`, taming food, saddle, boss
flag). Cached per language (`Names.Stamp`, bumped by `Localization.OnLanguageChange`): `DisplayName` (may carry TMP
tags), `SortKey` (tags stripped), `SearchKey`. Keys: items by token `m_shared.m_name`, pieces by `Piece.m_name`,
creatures by `Character.m_name` (the key of the kill stats). Relations hold live game objects (`Recipe`,
`CharacterDrop.Drop`, `Piece`, `Character`): details read their fields at click time.

#### 3.2.2 Build stages (coroutine, time budget per frame)

`CatalogService` (`Catalog/CatalogBuild.cs`) drives `CatalogBuilder` (`CatalogBuilder.cs` + `SourceScan.cs`) as a
coroutine with a **4 ms budget per frame** (`BuildBudget`, a Stopwatch restarted after each yield; every stage copies
the game list it walks, so a mod adding to it between frames cannot break the walk). Each stage runs in its own
`try`: a failing non-critical stage logs one Warning ("stage '<name>' failed, the data it adds is missing") and the
others still run, like Sort Chest's `BiomeIndex`; a broken modded prefab inside a stage is skipped (first 5 logged at
Debug). The critical stage (finalize) and the driver itself report through `PatchGuard.Report` and abandon the build,
never half published. No `yield` sits inside a `try` (C#): each step is check → guarded `MoveNext` → yield.

**Host and life cycle.** Unity stops every coroutine of a GameObject when that object is deactivated and never resumes
it, and refuses `StartCoroutine` on an inactive object. The window is closed with `SetActive(false)` (Esc, Tab, death,
teleport...), so the build must **not** run on the window. It runs on the plugin's own `MonoBehaviour` (the BepInEx
manager object, always active; `CatalogService.SetHost(this)` in `OnActivated`):

- The build fills a **new** `Catalog` object. Only when every stage has finished is it swapped in, in one assignment
  (`CatalogService.Current`), then `CatalogReady` fires (each subscriber guarded). The window never sees a half-built
  catalog.
- A build handle (the `Coroutine`, a generation number, the `ObjectDB` / `ZNetScene` references it started with, the
  unscaled time of its last step) marks a build in progress. Each step first checks that its generation is still the
  current one and that those two references are still the current instances; if not (the world ended, or a newer
  build replaced it), the build stops ("Catalog build abandoned (world ended).") and clears its handle.
- Unity does not run an iterator's `finally` block when a coroutine is stopped, so the handle is not trusted blindly:
  `EnsureReady()` treats a handle whose last step is more than 1 s (unscaled time) old as dead and starts a new build
  ("Catalog build restarted (previous build stalled)."). `OnDeactivated` (`CatalogService.Stop`) stops the coroutine,
  clears the handle and forgets the catalog.
- `EnsureReady()` (called by the window's `Open()`, after the window is shown) returns the finished catalog when its
  fingerprint matches, else starts a build when no live build is running and returns null. `Ready` returns the
  matching catalog without starting anything. Closing the window never stops a build. When a build finishes while the
  window is open, the list refreshes (`CatalogReady`); when it is closed, the next open uses the new catalog. The first
  step runs inside `StartCoroutine`, so the first 4 ms happen in the click frame.

Stages:

1. **items**: `ObjectDB.m_items`, grouped by token (3.2.4); tools with `m_buildPieces` remembered (ObjectDB order).
2. **recipes**: seasonal groups read from the local `Player` (else the `Player` prefab of `ZNetScene`);
   `ObjectDB.m_recipes` → per item token (source "crafted"), per ingredient ("used in", also upgrade-only
   ingredients), per station name.
3. **representatives** (own stage, so a broken recipe stage still leaves every item usable): per item the prefab
   that has a recipe, else the first in `ObjectDB` order; its icon and `ItemKind`.
4. **pieces**: every remembered tool's table (a table shared by two tools counted once), table order; dishes, stations
   and extensions indexed; station upgrades and recipes per station linked.
5. **creatures**: `ZNetScene.m_prefabs` with `Character`, grouped by `m_name`; creature equipment of every `Humanoid`
   (3.2.4); per group: representative, boss flag and defeat key, drops → per item ("dropped by"), trophy (first
   trophy-type drop), `Tameable` → `m_saddleItem` and `MonsterAI.m_consumeItems` → per item ("tames"), offspring and
   grown-up links.
6. **world sources**: one pass over `ZNetScene.m_prefabs` with root `TryGetComponent` for `Pickable`, `PickableItem`,
   `MineRock`, `MineRock5`, `TreeBase`, `TreeLog`, `Destructible` (a spawned item only; spawned objects are scanned on
   their own), `DropOnDestroyed`, `Container` (default items), `Smelter` / `CookingStation` / `Fermenter` (conversions,
   station = the piece entry of that prefab), `Beehive`, `SapCollector`, `OfferingBowl` (counted); `Trader` on the root
   and children (depth ≤ 3) of both `m_prefabs` and `m_nonNetViewPrefabs`.
7. **vegetation**: `ZoneSystem.m_vegetation` plus every enabled alternate biome's `m_addVegetation` (biome = the
   entry's mask within the alternate biome's): children searched like `BiomeIndex.CollectDrops` (depth ≤ 3) to give
   gathered items their biomes; `SpawnArea` and `CreatureSpawner` in children give creature habitats (nests, spawners).
8. **spawn lists**: the zone controller's `SpawnSystem.m_spawnLists` plus loaded `SpawnSystemList`s, and every
   enabled alternate biome's `m_spawn` (biome = spawn mask within the alternate biome's): creature habitats, fish
   ("caught in"), required global keys.
9. **habitats**: offspring and grown-up inheritance (two passes), trophy fallback (3.2.5), "appears after defeating".
10. **finalize** (critical): item sources and hidden-until-known flags (3.2.4), item tabs and sub-groups (a broken
    modded item goes to Tools and misc / Other, so every entry has a group), creature sub-groups, order (groups by
    rank, entries by game order), localized names computed once, counts.

Log at Info: `Encyclopedia catalog built in <ms> ms over <frames> frames: <i> items (<ih> hidden until known, <in> with
no source found), <p> pieces, <c> creatures, <x> item prefabs excluded.` At Debug: `Encyclopedia catalog stages:` (time
per stage), `Encyclopedia catalog scan:` (source components found on prefab roots per type, `Trader` components in
networked and non-networked prefabs, `OfferingBowl` components, scan errors), `Encyclopedia catalog tabs:` (entries per
tab), then one line per excluded prefab (items, pieces, creatures) with its rule, per hidden item or piece
(`Hidden until known: ...`), per item with no source found (`No source found: ...`, the list to review for new rules,
T05), per trader found, and per creature group (prefabs, representative, trophy, habitat and its source, bosses
required, boss, tameable).

**Fingerprint** (`Fingerprint.Take`, checked by `EnsureReady` / `Ready`; rebuild when it differs): `ObjectDB.instance`
reference, `m_items.Count`, `m_recipes.Count`, `ZNetScene.instance` reference, `m_prefabs.Count`, the sum of the sizes
of the piece tables the catalog found. A new world is a new `ObjectDB`. Details are computed from the live objects at
click time, so in-place recipe edits show without a rebuild. The published fingerprint holds the item, recipe and
prefab counts taken when the build **started** (before any stage copied a list) and the piece-table sizes the pieces
stage really read: content added while a build runs (a server-synced mod right after login) makes it differ from the
world, so the next `EnsureReady` builds again (Debug line `Encyclopedia catalog: game data changed during the build
...`). The first version took the fingerprint at the end and kept a stale catalog for the whole session.

#### 3.2.3 Item sources ("where it comes from")

An item has a **source** when at least one of these exists; the kinds are shown in its details under "Where to get
it" (3.5.1):

| Source | From | Detail text |
|---|---|---|
| Crafted | enabled recipe (or in any seasonal group) that the Craft tab offers: not upgrade-only (`m_noCraftOnlyUpgrade`, `InventoryGui.UpdateRecipeList`; 28 in 1.0.16) | recipe block ("Crafting") |
| Dropped | `CharacterDrop.m_drops` of a listed creature | "Dropped by:" + one line per creature |
| Gathered | root `Pickable`, `PickableItem` (item and random items), `MineRock`, `MineRock5`, `TreeBase`, `TreeLog`, `Destructible` (spawned item), `DropOnDestroyed` of every `ZNetScene` prefab; vegetation entries also searched in children and through logs, sub-logs and destructible spawns (depth ≤ 3) | "Picked / Mined / Chopped from trees / Found by breaking objects" + " in <biomes>" when vegetation gave biomes |
| Found in chests | `Container.m_defaultItems` of `ZNetScene` prefabs | "Found in chests" |
| Processed | `Smelter`, `CookingStation`, `Fermenter` conversions | "Made from <item> at <station>" (" at <station>" left out when that prefab is not a listed piece) |
| Produced | `Beehive.m_honeyItem`, `SapCollector.m_spawnItem` | "Produced by <piece>" ("Produced by a building" when that prefab is not a listed piece) |
| Laid | `Procreation.m_offspring` / `m_noPartnerOffspring` of a listed creature when that prefab is an item, not a `Character` (`Procreation.Procreate` sets the item's quality instead of taming it; 1.0.16: the Hen lays `ChickenEgg`) | "Laid by a tamed <creature>" |
| Caught | a spawn-list entry whose prefab is the item (fish) | "Caught in <biomes>" |
| Sold | `Trader.m_items` of a `Trader` on the root or children (depth ≤ 3) of a prefab in `ZNetScene.m_prefabs` or `m_nonNetViewPrefabs` (counts logged; the 1.0.16 run finds the 3 traders) | "Sold by a trader" |

The first design expected no trader to be found; the in-world run finds the 3 traders (Haldor, Hildir, the Bog
Witch), so their goods (Megingjord, Fishing Rod, Dverger Circlet, Hildir's clothes...) read "Sold by a trader". Loot
that only exists inside locations or dungeon rooms has **no source** in the loaded data (17 listed items in 1.0.16,
`compendium.catalog` notes them). That does not hide them: they are listed like every other item (3.2.4), and their
details say "Not found in the world data: it may come from a trader, a location or a dungeon." instead of a source
list (the trader in that text covers a modded trader the scan does not find).

#### 3.2.4 Inclusion and exclusion rules (rules, no name lists)

**Items** (one entry per token; representative = the prefab that has a recipe, else the first in `ObjectDB` order;
trophy prefab names kept for `m_trophies`):

- Never listed: null or no `ItemDrop`; empty `m_icons` or a null first icon; `ItemType.None` or `Customization`
  (hair and beards); DLC not installed (`DLCMan.instance.IsDLCInstalled`, like `Player.HaveRequirements`); the
  localized name is empty or a missing key (`"[...]"`).
- **Hidden until known** (listed only once discovered, no "???" row). Narrow, positive rules for things a player
  cannot own in normal play; everything else is listed, with or without a source (decision 5):
  - **Creature equipment**: every prefab of the token is referenced by the equipment of a non-player `Humanoid`
    (`m_defaultItems`, `m_randomWeapon`, `m_randomArmor`, `m_randomShield`, `m_randomSets[].m_items`,
    `m_randomItems[].m_prefab`) or is any character's `m_unarmedWeapon`, **and** the token has no recipe and no other
    source (3.2.3). This catches creature attack items (`Abomination_attack1`, `bjorn_bite`...) and `PlayerUnarmed`.
    A creature item that players can also get (dropped, crafted...) has a source and stays listed.
  - **Development items**: every prefab of the token has a name ending in `Test` or `Cheat` (vanilla `CapeTest`,
    `SwordCheat`, `SledgeCheat`, 1.8), and the token has no recipe and no other source.
  - **Seasonal items**: every recipe of the token is a disabled recipe of a `SeasonalItemGroup` and the token has no
    other source (same rule as seasonal pieces, below: they become learnable only in season, and show once known).
- Items with no source found that match none of these rules are **listed** (as "???" until discovered). A few vanilla
  items that normal play never gives may therefore stay "???" forever; the Debug list of no-source items (3.2.2) shows
  them, and a clear pattern becomes a rule later (open question 9).
- Placeable dishes: a piece prefab that also has an `ItemDrop` (Serving Tray / Feaster table) is **not** a separate
  piece; its item entry says "Can be placed with the <tool>", or "Can be placed like a building piece." when the tool
  is the item itself (feasts; whether the vanilla dishes carry an `ItemDrop` is **unverified**, logged at Debug).

**Pieces** (one entry per `m_name`, first table wins, other prefabs of that name point to it; table order = build-menu
order; tool tables in `ObjectDB` order, a table shared by two tools read once):

- Never listed: no `Piece`; `m_repairPiece` or `m_removePiece`; empty or missing name; no icon; DLC not installed; not
  `m_enabled` and not in any `SeasonalItemGroup.Pieces` (the `OLD_*` pieces); dishes (above).
- Hidden until known: seasonal pieces (not `m_enabled`).

**Creatures** (one entry per `m_name`; representative = the shortest prefab name in the group, then ordinal, e.g.
`Skeleton` before `Skeleton_NoArcher`):

- Never listed: no `Character`; the `Player` prefab; faction `Players`, `PlayerSpawned` (summons) or `TrainingDummy`;
  empty or missing name; a prefab whose name ends in `Test` (vanilla `DvergerTest`) is left out of its group, so its
  drops and habitat are not merged into a real creature.
- Every other creature is listed, whatever its `m_killedForAchievements` (1.8): babies (`Boar_piggy`, "Piggy"), hens
  and quest creatures are real creatures a player meets.

**Recipes**: enabled, or in any seasonal group (then marked "Seasonal"); `m_item` not null. Several recipes for one
item are all shown.

#### 3.2.5 Creature habitats

Habitat = union of biome masks from: enabled world spawn entries (`SpawnData.m_biome`), alternate-biome spawns
(`AltBiome.m_spawn`, biome `AltBiome.m_biome`), nests and spawners in vegetation (`SpawnArea.m_prefabs[].m_prefab`,
`CreatureSpawner.m_creaturePrefab`), and inheritance (offspring and grown-up forms share their parent's habitat,
`Procreation.m_offspring`, `Growup.m_grownPrefab`). Creatures with no habitat after that (bosses, dungeon-only
creatures) fall back to the biome of their **trophy** from a copy of the trophy rows of Sort Chest's curated
`BiomeTable` (about 70 prefab names, all checked against the 1.0.16 game data; copied with a "keep in sync" note,
decision 20). Still nothing: "Habitat unknown". Raids are not used (a raid spawns forest creatures in the Meadows,
which is not where they live). Alternate-biome vegetation (`AltBiome.m_addVegetation`) counts like vegetation.
"Appears after defeating <boss>" is shown only when **every** world spawn entry of the creature (spawn lists and
alternate-biome spawns) requires a boss’s `m_defeatSetGlobalKey` and the creature has no nest or spawner habitat
(decision 24): a creature that also spawns without a key, or from nests, does not need the boss.

#### 3.2.6 Tabs and sub-groups

| Tab | Label | Contents (`MC.Shared.ItemKinds`) | Sub-groups (header rows), in order |
|---|---|---|---|
| Weapons | "Weapons" | `Weapon`, `Ammo`, `Shield` | weapon family by `m_skillType` in Sort Chest `TypeGroups` order (swords, axes, clubs, knives, spears, polearms, unarmed, bows, crossbows, elemental magic, blood magic; labels `"$skill_" + skill.ToString().ToLower()`, the vanilla skill names), "Other weapons" (unknown skill), "Ammo", "Shields" |
| Armor | "Armor" | `Helmet`, `Chest`, `Legs`, `Hands`, `Cape`, `Utility`, `Trinket` | "Helmets", "Chest armor", "Leg armor", "Hand armor", "Capes", "Utility items", "Trinkets" |
| Tools | "Tools and misc" | `Tool`, `SkillTool`, `Torch`, `Misc`, `Other` | "Building tools", "Gathering tools", "Torches", "Misc", "Other" |
| Food | "Food and potions" | `Food`, `Potion` | `$hud_food` ("Food"), "Meads and potions" |
| Materials | `$hud_materials` | `Material`, `Fish` | `$hud_materials` ("Materials"), "Fish" |
| Trophies | `$inventory_trophies` | `Trophy` | one group without header |
| Building | `$hud_building` | pieces | one group per (tool, category): tools in `ObjectDB` order, then the table's category order; header "<tool> · <category label>" (`m_categoryLabels`, localized), the tool name through the knowledge check ("??? · Crafting" while the tool is undiscovered); no label (hoe, cultivator) → tool name only |
| Creatures | "Creatures" | creatures | first biome of the habitat in progression order (Meadows, Black Forest, Swamp, Ocean, Mountain, Plains, Mistlands, Ashlands, Deep North), then "Habitat unknown". The header shows the biome name only if the character knows that biome, else "???" |

Classification reuses `MC.Shared.ItemKinds.Classify` on the representative (same groups as Sort Chest and Crafting
Search and Sort). All labels live in `Labels` (English code strings, vanilla tokens where they exist, decision 13).
`SubGroup.HeaderText(knowledge)` is the only way to get a header text (tool and biome go through the knowledge check).

#### 3.2.7 Order inside a sub-group

`ListBuilder.BuildTab(catalog, knowledge, tab, showUndiscovered)` returns the rows (header rows and entry rows, each
entry row already resolved: "???" and no sprite when undiscovered):

1. Discovered entries, by translated name (tags stripped, `CompareInfo.Compare` invariant culture, ignore case and
   accents).
2. Then undiscovered entries in **game order** (`ObjectDB` order for items, table order for pieces, `ZNetScene` order
   for creatures), never by name, so the position of a "???" row does not reveal the first letter of its name
   (decision 6). A newly discovered entry moves into the sorted part at the next refresh. Left out: undiscovered
   hidden-until-known entries always, every undiscovered entry when `ShowUndiscovered` is off. A sub-group with no row
   left gets no header.

`ListBuilder.BuildSearch(catalog, knowledge, query)` (search, 3.8) returns, per tab with matches, a header (tab label)
and the matching discovered entries A→Z.

### 3.3 Discovery rules

`Knowledge.Take(catalog, revealAll)` (`Discovery/Knowledge.cs`) is the snapshot, taken at open, tab change and search
change; `RevealAll` setting = every entry and biome known. It keeps one bool per entry (by `Entry.Index`), the known
biome mask, and references to the profile's slot-0 dictionaries and the tames it read for the count lines:

| Kind | Discovered when (any of) | Retroactive |
|---|---|---|
| Item | `m_knownMaterial` contains the token (held once); `m_knownRecipes` contains it (the crafting list shows it); for a trophy, `m_trophies` contains one of its prefab names; `AllRecipesUnlocked` world key or debug no-cost mode (`m_noPlacementCost`) and the item has a recipe the Craft tab offers (enabled, or in the current season like `Player.GetAvailableRecipes`; not upgrade-only like `InventoryGui.UpdateRecipeList`) | Yes |
| Piece | `m_knownRecipes` contains `m_name` (buildable); for a station piece, `m_knownStations` contains its `CraftingStation.m_name`; placed at least once (`m_piecesPlacedStats`, slot 0); `AllPiecesUnlocked` or debug no-cost mode | Yes |
| Creature | killed ≥ 1 (`m_enemyStats[0]`, slot 0); its trophy item is discovered (item rule above: `m_trophies` or held); tamed ≥ 1 (Creature Kill and Tame Counts data, when present); **met** (the mod's `Seen` record, 3.4: the game showed its name plate, 1.11) | Kills, trophies, tames: yes. Met: from the day the mod runs |
| Biome (details only) | the mod's `Biomes` record; or `m_knownBiome` holds the biome's token, its name in the current language, or a single alternate-biome name of it (override, "prefix name", "name suffix"; raw and localized) | Yes, except biomes found in another language before install (decision 17) |

Everything is read-only on vanilla data. Tokens in the vanilla sets that are not in the catalog (removed mods, renamed
items such as `$item_flametal_old`) are ignored. No local player (main menu, respawn wait) = nothing known. The
snapshot also gives `DiscoveredCount` and `ListedCount` (every entry except undiscovered hidden-until-known ones) for
the "Discovered N / M" header, per-kind counts, `Kills`, `Tames`, `Placed`, `PickedUp`, `Crafted`, `Eaten`,
`StationLevel` and a Debug `Summary()` line. `Knowledge.Take(..., honourUnlocks: false)` and `Knowledge.Synthetic` exist
for the self tests only.

### 3.4 The mod's own records (`Player.m_customData`)

| Key | Content | Written |
|---|---|---|
| `MC.Exploration.Compendium.Encyclopedia.Seen` | Creatures met. Format v1: first line `1`, then one `Character.m_name` per line, sorted ordinal, `\n` separated. | When the game **shows** the name plate of a non-player character whose name is not in the set yet: `EnemyHud.UpdateHuds` postfix, every `HudData` whose `m_gui` is active after vanilla switched it (1.11; skipped when there is no camera, as `UpdateHuds` then stops before its loop). |
| `MC.Exploration.Compendium.Encyclopedia.Biomes` | Biomes visited, language independent. Format v1: first line `1`, second line the `Heightmap.Biome` flags as a decimal integer (invariant culture). | `Player.AddKnownBiome` postfix (the sector's `Biome`, `None` skipped); also the current biome (`Player.GetCurrentBiome()`) on activation and on each open. |

Rules (same contract style as Creature Kill and Tame Counts' `CounterStore`): the new value is built completely and
assigned once, only when something new is added; parse cached on the raw string reference; unreadable lines skipped;
names with a line break refused; if the first line is not `1` (a newer version wrote it) the mod reads what it can and
never writes (one Warning). Only for `Player.m_localPlayer`. Saved by vanilla with the character (autosave, logout,
before respawn). Removing the mod leaves both keys in the `.fch`, ignored by vanilla.

`Player.ResetCharacter` postfix (local player): removes both keys, because that reset forgets the vanilla biomes and a
character reset means a fresh start. It is **not** a full wipe of what the Encyclopedia shows: the vanilla reset keeps
the profile statistics and other mods' keys (1.5), so creatures with kills or tames and pieces placed at least once
stay discovered (section 4). The known-items reset (`resetknownitems`) keeps both keys (they are not items).

Tames are read, never written, from `MC.Exploration.Stats.PerCreature.Tames` (format v1 of that mod; a copy of its
~30-line parser; missing key = 0). No reference to that mod, no `ModRequires` (decision 8).
Code: `OwnRecords` (`MarkSeen`, `RecordBiome`, `RecordCurrentBiome`, `ResetFor`, `GetSeen`, `GetBiomes`) and
`TamesReader` in `Discovery/OwnRecords.cs`; the `EnemyHud.UpdateHuds` postfix skips players and records only while a
local player exists.

### 3.5 Details

`DetailBuilder.Build(entry, knowledge)` (`Details/DetailBuilder.cs`) returns a `DetailView` (`Details/DetailModel.cs`):
`Title` and `Icon` / `IconKind` already resolved (`Presentation`), `Subtitle` (plain text, never a name), `Known`, and
`Lines`. A `DetailLine` has a `Kind` (`Header`, `Paragraph` = multi-line rich text, `Row`, `Collapsed` = "??? ×N not
discovered yet" with `CollapsedCount`), an `Indent` level and `Parts`: plain text parts and `RefTarget` parts
(`Entry` or biome, `Known`, `Name` = real name only when known else "???", `IconKind`, `Icon` = sprite only when known,
`Clickable`); `OwnText` marks the entry's own game text (description, stats, lore). `DetailLine.IconRef` = first ref
(row icon). The pane renders lines top to bottom. All labels are English code strings in `Labels`, vanilla tokens where
they exist (decision 13). Numbers use invariant culture. In the lists below, `<name>` in angle brackets always means a
`RefTarget` (3.5.5), never plain text.

`Build` never throws: it runs inside a `try` (it calls vanilla and other mods' code: `ItemData.GetTooltip` reads
`Player.m_localPlayer` without a null check and is postfixed by many tooltip mods). On an exception it calls
`PatchGuard.Report` and returns the title plus one paragraph "Details unavailable (see the log)." (`Failed`) instead
of a half-built pane. Each block (stats, crafting, sources...) also has its own `try`: a failing block removes the
lines it added, counts in `FailedBlocks`, adds its message to `Errors` and reports once per block site.

#### 3.5.1 Item

1. Title: icon, name; subtitle: tab and sub-group (e.g. "Weapons · Swords"), "· Seasonal" when every recipe is
   seasonal.
2. **Description and stats** (paragraph, own text): `Localize(ItemDrop.ItemData.GetTooltip(prefab.m_itemData, 1,
   crafting: true, Game.m_worldLevel))`, the text the crafting panel shows (description, damage, armour, food values,
   weight, set bonus...); without a local player only the description. For a trophy, also its lore
   `Localize(token + "_lore")` when that key exists, like the trophy panel. **No side effect**: `GetTooltip` reads
   skill levels through the private `Skills.GetSkill`, which *adds* a level-0 entry for a skill the character never
   had (it would show in the Skills dialog and be saved; vanilla does it only when the real item is hovered). The
   builder notes the skill keys before the call and, in a `finally`, removes the entries it added that are still at
   level 0 with accumulator 0 (1.0.16 run: 1614 such entries while building every item's details; `compendium.details`
   checks the skill list is unchanged).
3. **Crafting** (header `$hud_crafting`; per recipe), **as the Craft tab charges it**: "At <station> (level N)" or
   "By hand" (+ "· Seasonal" for a seasonal recipe); "Makes N" when `m_amount > 1`; "Any one of:" for
   `m_requireOnlyOneIngredient`; one row per requirement **without** `m_upgraderResource` with `GetAmount(1) > 0`
   ("<name> ×N"). Upgrade-station requirements are never part of the quality-1 cost: crafting drops them
   (`Player.HaveRequirementItems`, `ConsumeResources`, `InventoryGui.SetupRequirementList` skip `m_upgraderResource`
   without an upgrader station), and an upgrader station only upgrades (target quality ≥ 2). The game code has no such
   rule by itself: at an upgrader station `Player.RequiredCraftingStation` accepts every recipe and only the
   `m_upgraderResource` requirements are charged, at any quality; what keeps quality 1 out is the station prefab's
   `CraftingStation.m_hasCraftTab` (`InventoryGui.UpdateCraftingPanel` then shows only the Upgrade tab). **Verified**
   in the 1.0.16 in-world run (`compendium.catalog` NOTE): the only prefab with `m_upgrader` is `UpgradeStation`
   (`$piece_upgradestation`, "Forge of Potential"), with `m_hasCraftTab` **false**; 225 recipes carry an upgrade-station
   requirement, all 225 with a quality-1 amount that no one is ever charged. A content mod adding an upgrader with a
   Craft tab would make that quality-1 cost real; the NOTE names such a station, the details do not show it.
   An upgrade-only recipe (`m_noCraftOnlyUpgrade`: the Craft tab never lists it, `InventoryGui.UpdateRecipeList`) has
   no head and no cost, one row "Cannot be crafted, only upgraded." instead. **Upgrades** (header, when `m_maxQuality > 1`): for each quality
   2..`m_maxQuality`, a row "Quality q (<station> level L)" from `GetRequiredStation(q)` / `GetRequiredStationLevel(q)`
   ("Quality q" alone without station), then the normal requirements with `GetAmount(q) > 0`, then the
   `m_upgraderResource` ones with `GetAmount(q) > 0` under "At an upgrade station:" (the other way to reach that
   quality: an upgrader station charges only those). When the recipe has an upgrade-station requirement, a last row
   "Beyond quality N: at an upgrade station only" (the upgrader ignores `m_maxQuality`, `InventoryGui.UpdateRecipe`;
   there is no upper end, so no list). 1.0.16: 225 items show upgrade-station rows.
4. **Where to get it** (header; only when the item has a non-recipe source, or no source at all): "Dropped by:" then
   one row per creature "<creature> N-M (P%)" (amounts per 1.9 at 0 stars; ", more with stars" inside the brackets
   when `m_levelMultiplier`; "1 per player" for `m_onePerPlayer`); "Picked / Mined / Chopped from trees / Found by
   breaking objects" + " in <biome>, <biome>" when known from vegetation; "Made from <item> at <station>";
   "Produced by <piece>"; "Laid by a tamed <creature>"; "Found in chests"; "Caught in <biomes>" (or "Caught by
   fishing"); "Sold by a trader". Nothing found at all (no craftable recipe either): "Not found in the world data: it
   may come from a trader, a location or a dungeon." (3.2.3). A crafted-only item has no such header (the crafting
   block says it).
5. **Used in** (header; reverse index, one row per target): recipe outputs, pieces and conversion outputs (with
   "at <station>") that need it, known A→Z, unknown folded (3.5.5); then "Tames <creature>" rows for tame food.
6. **Your records** (header, non-zero only): "Picked up N", "Crafted N", "Eaten N" (profile slot 0).
7. Dishes: "Can be placed with the <tool>", or "Can be placed like a building piece." when the item is its own tool.

Item 2 is the item's own text and is shown as the game writes it; it names no other entry of the catalog in vanilla
(set bonuses name a status effect, not an item).

#### 3.5.2 Piece

1. Title: icon, name; subtitle "Building · <category>" (+ "· Seasonal"); first row "Built with <tool>".
2. Description (paragraph, own text, `Localize(m_description)`); "Comfort N" when `m_comfort > 0`.
3. **Build cost** (header): one row per requirement with `m_amount > 0` ("<name> ×N"); "Needs a <station> nearby"
   (`m_craftingStation`).
4. An extension: "Upgrade for the <station>" (extra). A station: "Highest level you have seen: N" (`m_knownStations`,
   when > 0); **Upgrades** (header): one row per extension piece (known A→Z, then each unknown on its own row);
   **Recipes at this station** (header): one row per item made there "<item> (level N)" (lowest level of its recipes
   here; upgrade-only recipes skipped: nothing is made from them), known A→Z, unknown folded.
5. Smelter, kiln, cooking station, fermenter: **Conversions** (header), "Turns <from> into <to>" (3.5.5). Beehive,
   sap collector: "Produces <item>" (extra).
6. **Your records**: "Placed N" (`m_piecesPlacedStats`, when > 0).

#### 3.5.3 Creature

1. Title: icon, name; subtitle "Creatures", plus "· Boss" when `m_boss`. The icon is the trophy's sprite **only when
   that trophy item is discovered too** (or `RevealAll`); otherwise (trophy unknown, or no trophy) the generic creature
   icon, a paw print drawn in code (`IconKind.GenericCreature`, `Ui/PawIcon.cs`, decision 15). The same rule applies to the creature's list row and to every `Ref`
   that points at the creature (`Presentation.Icon`). A creature met but never killed therefore never shows the sprite
   of a trophy the Trophies tab still lists as "???".
2. **Killed: N** (all-time, slot 0 total, rounded; 0 shown). **Tamed: N** when Creature Kill and Tame Counts data
   says N ≥ 1.
3. **Habitat** (header): one row per biome of the habitat (3.2.5), in progression order; "Appears after defeating
   <boss>"; "Habitat unknown" when none.
4. **Drops** (header): union over the group's prefabs (representative's numbers first), one row per item
   "<item> N-M (P%)", in data order.
5. **Taming** (header, when the group has a `Tameable`): "Can be tamed. Eats:" + one row per `m_consumeItems` item;
   "Can wear: <saddle>" (`m_saddleItem`), so an unknown saddle reads "Can wear: ???".
6. **After your first kill** (header; kills ≥ 1 or `RevealAll`, extra, decision 3): "Health N" (representative
   `m_health`, 0 stars, world level 0) and "Damage modifier:" (`$inventory_dmgmod`) with one row per non-Normal,
   non-Ignore entry of `m_damageModifiers` in vanilla words ("Blunt: Weak", like `SE_Stats`).

#### 3.5.4 Undiscovered entry

Title "???" with the "?" mark (`IconKind.Unknown`, no sprite); subtitle = tab (already visible from the list); one
paragraph: "Not discovered yet." plus a hint per kind: item "Hold one, or learn a recipe that makes it."; piece "Learn to
build it: carry the right tool and its materials."; creature "Aim at one up close to see its name, kill one or tame one."
Nothing else (no stats, no sources).

#### 3.5.5 References and obfuscation (goal 7)

- **One rule for every detail line**: any name of another catalog entry (item, piece, station, tool, saddle, creature,
  boss, conversion input or output, producer, "made from", "tames") is a `Ref`, and the line text is assembled from
  the resolved `Ref`s, never by formatting a localized name into a sentence. `DetailBuilder` has no other way to print
  an entry's name. Biomes follow the same rule with the biome knowledge (3.3).
- Every `Ref` is resolved against the knowledge snapshot: a discovered target shows its icon (creatures: 3.5.3 rule)
  and name (and is a clickable link, extra); an undiscovered one shows the "?" mark and "???", **keeping** amounts and
  chances ("??? ×2", "??? 1-2 (50%)"). A target the catalog excludes (a hidden-until-known item that is unknown, a
  creature attack item) is also "???".
- Biome not known → "???". Boss in "Appears after defeating" not known → "???". Station not known (its piece entry
  undiscovered) → "At ??? (level N)" (the level is a number, not a spoiler). Tool not known → "Built with ???".
- **Long lists** ("Used in", "Recipes at this station", a station's **Conversions**): discovered targets listed A→Z,
  undiscovered ones collapsed into one line "??? ×N not discovered yet" (decision 7). A conversion counts as
  discovered when both sides are known; when only one side is known it is listed with the other side as "???"; when
  both are unknown it is collapsed. Example: a Smelter seen before any of its ores or bars is known shows one line
  "??? ×N not discovered yet" (N = its conversion count, prefab data), never "Turns Scrap Iron into Iron"; once Copper
  Ore is held it adds "Turns Copper Ore into ???". Conversions with both sides known are sorted A→Z by input; the
  half-known ones follow in data order. "Used in" has one row per target (a target reached through a recipe and a
  conversion counts once). Every other block lists each unknown target on its own line (recipe ingredients, drops,
  habitats, tame food, station upgrades are short and their position matters).
- `RefTarget.Of(entry, knowledge)` / `RefTarget.OfBiome(biome, knowledge)` resolve every reference, through
  `Presentation.Name` / `Presentation.Icon` / `Presentation.BiomeName`. A target the catalog does not list at all
  (entry null: an excluded item, a station prefab that is not a listed piece) is always "???".

### 3.6 UI: the nested tabs (`Ui/TopTabs.cs`) and the optional side button (`Ui/SideButton.cs`)

**Cloning rule (every clone the mod makes).** One shared helper, `CloneUtil.Strip(clone, kind, navigation, keep)`
(`Ui/CloneUtil.cs`), runs on **every** clone: the side button, the dialog, the 8 tab clones and the 2 top tab clones (in
both dialogs) of `m_tabCraft`, the row
template cloned from `m_recipeElementPrefab` and every list and detail row made from it, the detail text template and
every pooled text made from it, the counter, the search field and the head block's texts. It runs while the clone is
still under an inactive holder (the mod's `MC_Compendium_Templates` object, or a temporary `MC_Compendium_Build`),
before any of its `Awake`/`Start` can run, and:

- destroys every `UIGamePad` (and its hint object when it is inside the clone), `UIInputHint` (and its hint objects),
  `Localize` and `UITooltip` (except the ones in `keep`: only the button's own tooltip is kept) and every component
  whose assembly is not Unity, TextMeshPro or one of the game's (`assembly_*`, `gui_framework`,
  `SoftReferenceableAssets`, `Splatform`), in reverse order so a component another one needs goes last;
- replaces every `Button.onClick` with a new, empty event (a cloned `Button` keeps its persistent prefab calls
  otherwise, and `RemoveAllListeners` does not remove them); the caller then adds our listener;
- sets `Navigation.mode` on every `Selectable` on purpose: `None` for rows, tabs, texts and the search field (the
  `WindowController` moves the selection itself), `Automatic` for the side button;
- logs at Debug level what it removed, once per kind of clone (`Clone strip (<kind>): ...`, also repeated in the
  layout dumps of Debug builds, 3.15).

So no leftover pad on a tab can take our window's group in `Start` and click a tab on a vanilla key, and no leftover
hint glyph shows on tabs or rows. The new side-button pad is added after the strip.

#### Nested tabs (`Ui/TopTabs.cs`, `Ui/DialogParts.cs`, `Patches/TextsDialogPatches.cs`)

Two top-level tabs, **Texts** and **Encyclopedia**, in two places with the same rects: on the game's Valheim
Compendium dialog (`m_textsDialog`) and on our window (its clone, 3.7).

- **Where**: on the title line, left of the title, from the panes' left edge (1.4: that part of the line is free in
  both dialogs). Measured by `DialogParts` in the dialog's own units by math (the same rules the window build uses):
  pane area = union of the two pane boxes and their bars (the scroll views found by structure, 1.4 trap), title = the
  text reading `$inventory_texts` (`DialogParts.FindTitle`, our own strip skipped). Tab height = the Craft tab's
  (clamped 24-40: 32); width = min(150, half the room between the panes' left edge and the title's left edge minus
  8 units); 4 units apart; vertically centred on the title line, never lower than 2 units above the panes. 1.0.16:
  Texts x -587..-437, Encyclopedia -433..-283, y 354..386 (title -166..166 × 352..389, panes top 349, category tabs of
  our window 317..349, counter x 174..589 on the title line). Less than 60 units each → no tabs (Warning).
- **Look**: clones of the crafting panel's Craft tab (`m_tabCraft`, fallback `m_tabUpgrade`; the window alone falls
  back to the row template), stripped (`CloneUtil`: no pad, hint, tooltip, `Localize`, prefab click call; navigation
  None), label auto-sized to the Craft tab's size, one line; the vanilla `ButtonTextColor` stays. Current tab = not
  interactable (the vanilla tab look: lighter, not clickable): Texts on the vanilla dialog, Encyclopedia on ours.
  Labels: "Texts" (plain English: no vanilla token fits; `$inventory_texts` reads "Valheim Compendium", the dialog
  title itself; `$inventory_logs` "Message log" is one of its entries) and "Encyclopedia". Mixed case like our
  category tabs (the vanilla Craft tab's own label is upper case, "CRAFT").
- **On the vanilla dialog**: an object `MC_Compendium_TopTabs` (full-size, no graphic) holding the two tabs and the
  `TopTabsController`, added as the dialog's **last child** (above its full-screen click-outside button) by the
  `TextsDialog.Setup` postfix, i.e. every time the dialog is shown or refilled, whoever calls it (raven, our Texts tab,
  another mod); built inactive, cloned into, then switched on. It is **destroyed when the dialog hides** (the
  controller's `OnDisable`: Esc, B, Close, click outside, our Encyclopedia tab, `Hide`) and when the feature goes off.
  So the vanilla dialog is never changed (nothing of it is moved, renamed or recoloured), a closed dialog is exactly
  the game's, and a mod that clones the dialog (PraetorisClient, 1.4) never copies our tabs; our own window build
  removes a copied strip by name first. Feature on while the dialog is shown: the strip is added at once
  (`TopTabs.OnActivated`).
- **Encyclopedia** (click, or RT): remembered, then `CompendiumWindow.Open()`: it closes the vanilla dialog the
  vanilla way (`TextsDialog.OnClose`, where Jewelcrafting cleans up) and shows our window in the same frame, same place
  and size (a clone of the same full-screen dialog, same frame), with the two tabs on the same spot, Encyclopedia
  current: it looks like a tab change. Refused (window cannot be built, UI mod): the vanilla dialog stays or is opened
  again, Texts is remembered and the tabs go for the session (`TopTabs.HideForThisSession`).
- **Texts** (click in our window, or LT): remembered, our window closed, then `InventoryGui.OnOpenTexts()` (the vanilla
  way: side panel group, `TextsDialog.Setup` fills its list fresh, so a text learned while the Encyclopedia was open
  is there).
- **Controller**: **LT = Texts, RT = Encyclopedia**, in both dialogs (decision 56). Read by the
  `TopTabsController.Update` on the vanilla dialog (only while it is shown, its group active, `IsExclusiveGamepadActive`,
  our search guard off) and by the `WindowController` in ours. A direct pick, not a toggle, so the frame of a switch
  (both controllers may read the same press) changes nothing more. RT in our window (Encyclopedia already shown)
  changes nothing, the remembered tab included: the window may have been opened from the optional side button.
- **Remembered tab** (session memory, decision 57): the last pick made with these tabs (click or LT/RT), until a new
  `InventoryGui` (logout: Texts again, like vanilla). The `InventoryGui.OnOpenTexts` postfix (raven button, another
  mod calling it) reopens it: last pick Encyclopedia → the vanilla dialog opens as usual, then our window replaces it
  in the same frame. Our Texts tab remembers Texts before calling `OnOpenTexts`, so it never bounces back. Opening the
  window from the optional side button does not change it. Kept across a live toggle, like the window's own memory
  (decision 35).

#### Optional side button (`Ui/SideButton.cs`, `Ui/SideRow.cs`)

Only while `Display.SideButton` is on (default **off**, decision 58; code reads `Plugin.SideButtonOn`, which a Debug-only
in-memory override `Plugin.TestSideButton` can force for the self tests). Off, nothing below exists and the side panel
is never touched. `SideButton.Sync` follows the setting live (`SettingChanged`, only while the feature is active, and
`OnActivated`): on = the button is made (placed and checked at once when the inventory is shown); off = the button is
destroyed and the row put back exactly (`SideRow.Restore`, step 5). `Create` (the `Awake` postfix) and `OnShow` (the
`Show` postfix) do nothing while it is off.

1. **Find the anchor** in the `InventoryGui.Awake` postfix (and in `Sync` / the `Show` postfix when the button
   does not exist yet): scan `gui.GetComponentsInChildren<Button>(true)` for a button with a persistent `onClick`
   method `OnOpenTexts` (fallback, in this order: `OnOpenTrophies`, `OnOpenSkills`, `OnOpenAchievements`). None → one
   Warning per game session "Valheim Compendium button not found (UI mod?): no Encyclopedia button." and no button
   (AugaLite, Veneer).
2. **Clone** under an inactive holder (nothing wakes up), then `CloneUtil.Strip` keeping the root `UITooltip`: the
   persistent `OnOpenTexts` call goes with the replaced `onClick`, which gets our guarded listener. Name
   `MC_Compendium_Button`, parent = the anchor's parent. A text of the clone that reads `$inventory_texts` (raw or
   localized) becomes "Encyclopedia" (none expected: the side buttons look like icons).
3. **Icon**: the child `Image` named like "icon", else the largest active child `Image` with a sprite, else the root
   `Image` (a button that is only an icon) gets a sprite **drawn in code** (`Ui/BookIcon.cs`: an open book seen from
   the front, cover, pages, spine and outlines, ink lines, in light greys only; 64×64, transparent, 4×4
   supersampling), with `preserveAspect`; its colour stays the anchor icon's, and the cloned button's colour tint turns
   it gold like the vanilla icons (1.1: the tint multiplies the sprite; a first version drawn in browns came out orange
   and dark next to them). When the button's transition is a sprite
   swap on that same image (it would show the vanilla sprites on hover), it becomes a colour tint. Built once, texture
   and sprite destroyed on deactivation (decision 16; the MarkerOverlay pattern of Loot Pickup Filter). No asset.
4. **Tooltip**: the clone's `UITooltip` (kept; given the inventory slot tooltip prefab when it has none), or a new one
   with the inventory slot tooltip prefab when the anchor has none; `Set("Encyclopedia", "Every item, building piece
   and creature you have discovered, with recipes, drops and where to find them.")` (`Labels.ButtonTooltipTopic` /
   `ButtonTooltipText`); `m_gamepadFocusObject` cleared. No prefab found → no tooltip (Debug line).
5. **Placement** (`Place`, run at creation, at every `Show` and at the self-check; math only: `localPosition`,
   `anchoredPosition` and `rect` in the row parent's space, so it works in `Awake` and at any animation scale):
   - the parent has a `LayoutGroup` → sibling index right after the anchor: the layout places it right after the
     Valheim Compendium button (the vanilla buttons after it shift by one slot);
   - otherwise (vanilla, 1.1) **the row is laid out again** (`Ui/SideRow.cs`): 5 vanilla controls at a 100-unit step
     leave no room for a sixth inside the panel, so while the button is on the row holds the vanilla controls
     plus ours, **ours right after the anchor** (next to the Valheim Compendium button it relates to; PvP stays last),
     evenly spaced and centred where the vanilla row was. Controls = the active direct children of the parent holding
     an active `Selectable`. Each run first puts every vanilla control back on its **baseline** (its own
     `anchoredPosition`, `localScale`, `sizeDelta`, recorded the first time it is seen), then measures and applies, in
     the same frame (nothing is drawn in between). Line = axis with the larger spread of the centres. Sizes along the
     line: control (64) and visual (control plus its child graphics: the 104-unit round shade). **Region** = the panel
     background: the smallest active sibling with an enabled `Image` and no `Selectable` whose rect holds every
     control (vanilla: `Bkg`; not the bigger `Darken` shade), else the parent's rect. Inset = the vanilla row's own
     margin to that region, capped at 4 % of its length (vanilla: 43 → 23.6 units). Step = the vanilla step (median
     gap), shrunk so that `(n - 1) × step + visual` fits the region minus the insets (vanilla: 87.8 units); when even a
     tight step (control + 12 %) does not fit, every control is scaled down to fit (never under 50 %: then the old
     placement below is used). The row stays centred on the vanilla row's centre, clamped into the region. Each
     control is moved along the line only (its cross position is kept; ours takes the anchor's). Scale 1 in vanilla:
     sizes never change.
   - **Exact restore**: `SideRow.Restore` puts every control that is still where we put it back on its baseline
     (position, scale, size, float for float) when the feature deactivates, the setting goes off or the button hides for the session. A new
     `InventoryGui` (logout) just forgets the old controls (they died with it). A resolution or UI-scale change moves
     nothing: everything is in the panel's own units.
   - **Never fight another mod**: at each run, a control found neither where we put it nor on its baseline was moved by
     someone else: its current place becomes its baseline and the row is laid out again (one Info line in Debug
     builds). A control put back on its baseline by someone else is laid out again too. The third such change for one
     `InventoryGui` → we give up: our changes are undone (controls still where we put them go back to their
     baselines, the moved ones stay where the other mod put them), one Warning "Another mod keeps moving the inventory
     side panel buttons: the Encyclopedia leaves them alone and puts its button after the last one.", and our button
     uses the old placement below for the rest of the session. A control we do not hold now (row not laid out yet, 6
     controls did not fit, gave up) is never pulled back: at each run its current place becomes its baseline, and
     `Restore` (feature off, button hidden) touches only controls still where we put them (the first version also
     snapped the others back to baselines recorded at give-up time, over the other mod's newer layout);
   - old placement (row given up, or 6 controls cannot fit): the centres of the controls are measured; step = median
     gap; cross position = median of the line; ours goes one step after the last control (right of the rightmost, or
     below the lowest); fewer than two controls → right of the anchor by its width + 4. The clone has the anchor's
     anchors, pivot and size, so it is moved by the centre delta in parent space;
   - **self-check** once per `InventoryGui` (`SelfCheck` coroutine started from the `Show` postfix on the
     `InventoryGui`): waits until our button is active, not at zero scale and has not moved for 2 frames (at most 90
     frames; the inventory closed before that or never settled → tried again at the next `Show`), fixes the pad group,
     places again (another mod's `Show` postfix may have run after ours), then checks the world rect of our button:
     **inside the side panel background** (the region above, 1 px tolerance), no overlap with any other control of
     the row nor with a small sibling graphic (siblings over 4× our area that are not controls are shades, skipped),
     clear of the player, crafting and container panels (when they are not our ancestors), and on screen (overlay
     canvas). With the old placement: problem after the last control → one step before the first control; still a
     problem → back after the last. Any remaining problem → one Warning "Encyclopedia button <problem> at <rect> (screen
     WxH); another UI mod may have moved the side panel." The result goes to the line `Encyclopedia button placed <how>
     (<rect>): inside the panel, no overlap, on screen. Row: <state>.` and to the side panel dump (3.15).
6. **Gamepad**: the vanilla side controls use explicit navigation. When the anchor's right neighbour is the next
   control and that control's left neighbour is the anchor, both links are rewired to our button while the row is
   laid out (ours: explicit, left = anchor, right = next, up/down = the anchor's), so the D-pad walks the row in its
   visible order; restored exactly (only when still as we set them) with the positions, and ours set back to
   `Automatic`, whenever the row layout is no longer applied: feature off, button hidden, gave up, and also when a
   later `Apply` fails because the anchor left the row, no other control is left or 6 controls no longer fit (the
   first version restored them only on giving up, so a failed layout left the D-pad walking from Skills to our button
   parked after the last control; `compendium.siderow` now hides the anchor to check it). Otherwise ours keeps
   `Automatic` navigation and the vanilla links are not touched. A new `UIGamePad` with `m_zinputKey = "JoyBack"`
   (View/Select), no key code, no hint, empty blocking list. It is added only when `m_uiGroups[2]` exists and no other
   pad in that group already uses `JoyBack` (else a Warning in the second case, and the button is mouse only); the pad
   of our own old button does not count (feature off and on in the same frame: `Object.Destroy` only takes effect at
   the end of the frame; the first version warned and left the new button without its pad, seen in the
   `compendium.siderow` clean-up). Its group is **not** left to the nearest parent (1.3):
   once its `Start` has run (`m_button` set; checked in the `Show` postfix and in the self-check, Sort Chest's
   `FixPadGroups` pattern), `pad.m_group` is set to `gui.m_uiGroups[2]`. The real gate is in the
   `UIGamePad.ButtonPressed` prefix (3.9): for our pad it returns false unless the inventory is visible (Animator bool
   `"visible"`), `gui.ActiveGroup == 2`, the window is closed, the local player is alive, not teleporting and not in a
   cutscene, and the search focus guard is off (`SideButton.PadAllowed`). Because `ButtonPressed` runs before the
   shared 2-frame lock is taken, a rejected press never blocks another pad: View/Select on the chest grid always
   reaches Sort Chest, and View/Select in the world always reaches the map.
7. **Click**: `CompendiumWindow.Open()` (3.7), inside a `try` (`PatchGuard.Report`): `UIGamePad.Update` calls
   `OnSubmit` with no guard. It does not change the remembered top tab.
8. **Window cannot be built** on this `InventoryGui` (3.7 creation step 1 or a failed layout): the button hides for
   the rest of the world session (`HideForThisSession`) and the vanilla row goes back exactly as it was; the top tabs
   go too (`TopTabs.HideForThisSession`). Turning the setting on later in that session (or off and on) makes no button
   either: `Create` asks `CompendiumWindow.RefusedFor(gui)` first, since a button there would do nothing on click.

### 3.7 UI: the window (`Ui/CompendiumWindow.cs`, `.Build.cs`, `.Details.cs`; rows `Ui/RowView.cs`)

**Creation** (`Build`, at the first open per `InventoryGui`; a new `InventoryGui` = a new session: objects and session
state forgotten):

1. `m_textsDialog` or `m_recipeElementPrefab` null → Warning "Encyclopedia window cannot be built (<why>; UI mod?): no
   Encyclopedia tab or button for this session." and the top tabs and the optional button go for this session. Same for
   every later "cannot be built" case.
2. `Instantiate(m_textsDialog.gameObject)` under an inactive holder; our top tabs strip (`MC_Compendium_TopTabs`, on the
   vanilla dialog while it is shown: we are usually built from its Encyclopedia tab) is destroyed in the copy first. Read its `TextsDialog` references first
   (`m_listRoot`, `m_textAreaTopic`, `m_textArea`, `m_elementPrefab`, the bars `m_leftScrollbar` / `m_rightScrollbar`
   as fallbacks). The two scroll views are found **by structure** (1.4 trap: `m_leftScrollRect` points at the text
   view): list view = the `ScrollRect` above `m_listRoot`, detail view = the `ScrollRect` above `m_textArea`, each
   one's own vertical bar first. Every button with a persistent `OnClose` is collected (vanilla: the "Close" button
   and the full-screen click-outside one). Then `DestroyImmediate` the component (no other mod's `TextsDialog` patch
   ever runs on our window: Jewelcrafting, EquipmentAndQuickSlots, Creature Kill and Tame Counts). List root, a scroll
   view, topic or text area missing, or both scroll views the same → cannot be built (the Warning says which part).
3. Strip: children of `m_listRoot` (rows left by vanilla or by Jewelcrafting); the vanilla row template
   `m_elementPrefab` when it sits inside the clone (vanilla: in the list's scroll view); every `UIGroupHandler` and
   `ScrollRectEnsureVisible`; then `CloneUtil.Strip` (3.6: also removes the B glyph hint of the "Close" button). Every
   collected close button gets our guarded `Close`, like vanilla (the "Close" button, and a click outside the frame
   on the full-screen one); the visible one (`_close`, used by the layout) = the one with a text label, the smallest
   if several. None found → Debug line; Esc, B and Tab still close.
4. Title: the first `TMP_Text` outside the two scroll views, the topic, the text area and the close buttons whose text
   is `$inventory_texts` (raw or localized; vanilla prefab text: "Valheim Compendium"); else the top-most such text
   above both scroll views → "Encyclopedia" (`Labels.WindowTitle`). Not found → a copy of `m_textAreaTopic` at the top
   (Warning "Encyclopedia window: the dialog title was not found; a title was added at the top.").
5. Root: name `MC_Compendium_Window`, parent = `m_textsDialog.transform.parent`, same rect as the vanilla dialog,
   inactive until opened. `CanvasGroup` (the clone's, or a new one) with `ignoreParentGroups = true` (1.3 trap), alpha
   1, interactable, blocks raycasts. `UIGroupHandler` (no default element) whose `m_groupPriority` is set at every
   open to 1 + the highest priority of every `UIGroupHandler` under the inventory **and** of every active one in the
   scene (HUD or menu groups that could outrank ours would otherwise make our window non-interactable). The
   `WindowController` component (per-frame input, search focus, blocker size; the catalog build does **not** run
   here, 3.2.2). An inactive `MC_Compendium_Templates` child holds the templates and is where every clone is made and
   stripped before it moves to its place.
6. **Mouse modality** (1.3: a priority group does not stop pointer handlers): the root also gets a nested `Canvas`
   (the clone's, or a new one) with `overrideSorting = true`, the root canvas's sorting layer and `sortingOrder` = the
   root canvas order + 1 (set again after each activation: a canvas added while inactive may lose it), and a
   `GraphicRaycaster`, so it draws and raycasts above every inventory element whatever the sibling order (Loot Pickup
   Filter's button on `m_inventoryRoot`, later siblings added by other mods). Its first child is
   `MC_Compendium_Blocker`: a transparent `Image` (`raycastTarget = true`, alpha 0) sized to the whole root canvas plus
   8 units (converted to the window's local space; at zero scale a 20000-unit square), at open and every frame while
   open (set only when it changed). Above it the clone keeps the vanilla `darken` (the screen behind is dimmed, as
   for the vanilla dialog) and the full-screen close button: a click outside the frame closes the window, like the
   Valheim Compendium, and never reaches the inventory; the blocker catches whatever that button does not cover. The
   Debug dumps log the canvases and their sorting orders.
7. Fonts: every new text is a clone of `m_textArea` (detail texts, the head block, the counter), `m_textAreaTopic`
   (fallback title) or the row's `name` (rows, "?" marks), so game font, material and outline.
8. Row template (`RowView.BuildTemplate`): clone of `m_recipeElementPrefab`, `Durability` and `QualityLevel` removed,
   stripped, any `LayoutElement` removed; top-stretched, height = `m_recipeListSpace` clamped to 22-48. The mod lays
   out its children itself: `icon` (created when missing) at the left, square, `rowH - 6`; a "?" mark (copy of `name`,
   bold, grey, over the icon rect); `name` stretched after the icon, one line, ellipsis; `selected` kept (highlight).
   The row's background = the `Button`'s target graphic (1.0.16: child `bkg`, raw colour light grey, tinted almost
   clear by the button): it is turned off with the button on rows that are not clickable (headers, plain detail rows),
   else they would show a light grey bar. No `name` text → cannot be built.

**Layout** (`TryLayout`, once, at the first open right after `SetActive(true)`: the rects of a hierarchy that never
ran may be stale. All measured in the window's own space by math: `localPosition` and `localScale` up to the root, so
the inventory's open animation does not matter. A failure → `PatchGuard`, window destroyed, top tabs and button gone
for the session; the Debug window dump confirms the result):

```
+--------------------------------------------------------------------------------------+
| [Texts][Encyclopedia]          Encyclopedia                   Discovered 312 / 1480   |
| [Weapons][Armor][Tools and misc][Food and potions][Materials][Trophies][Building][Creatures] |
| +-[Search...          ]------+ +----------------------------------------------------+ |
| | Swords                      | |                  Bronze Sword                      | |
| | [ic] Bronze Sword           | |  [ICON] Weapons · Swords                           | |
| | [ic] Iron Sword             | |  A sturdy blade... Damage 35 ...                   | |
| | [?]  ???                    | |  Crafting                                          | |
| | Axes                        | |    At Forge (level 1)                              | |
| | [ic] Flint Axe              | |    [ic] Bronze ×8        [?] ??? ×2          [scr] | |
| | ...                   [scr] | +----------------------------------------------------+ |
| |                             |                  [ Close ]                            |
| +-----------------------------+                                                      |
+--------------------------------------------------------------------------------------+
```

- **Pane boxes**: for each scroll view, its ancestor right under the object that holds both views (vanilla: `TextList`
  and `TextArea` under `Texts_frame`, with their dark backgrounds and bars inside); a view with no such ancestor is its
  own box. **Pane area** = union of the two boxes and their scroll bars (vanilla: x -587..589, top 349, in the
  dialog's units; the title sits above it). Measured by `DialogParts`, like on the vanilla dialog.
- **Top tabs** "Texts | Encyclopedia" (3.6 "Nested tabs"): measured first, from the pane area and the title before
  anything moves, so they land on the same rects as on the vanilla dialog; `MC_Compendium_TopTabs` under the root,
  Encyclopedia current (set again at each open). No room or no tab source → none (Warning).
- **Category tab strip**: clones of `m_tabCraft` (fallback `m_tabUpgrade`, then a row clone with its icon hidden) in a band at
  the top of the pane area: one row of 8 when each tab gets at least min(90, source width) units, else two rows of 4
  (Debug line). Tab height = the source's, clamped to 24-40; 4-unit gaps; 6 units under the band (vanilla: one row of
  143.5×32). The two pane **boxes**' top edges (and scroll bars outside them) move down by the band, so each box's
  background moves with its content; a topic outside both boxes moves down with them. `m_tabCraft`
  calls `OnTabCraftPressed` through a listener saved in the prefab (no code caller), so each clone goes through
  `CloneUtil.Strip` (new `onClick` event, any pad, hint, tooltip or `Localize` removed, navigation None). Labels:
  `Tabs.LocalizedLabel`, auto-size down to 10, one line, ellipsis (a legacy `Text` label is also set). Current tab =
  `interactable false` (vanilla look); while a search is shown every tab is clickable (a click leaves the search).
- **Search row** (extra): a clone of `Hud.instance.m_buildUi.m_searchField` (fallback `Chat.instance.m_input`, as
  Crafting Search and Sort's `SearchUi`) in a row-high strip at the top of the left pane box, inset 4 units (the list
  scroll view and its bar start under it; the box's background stays behind both); placeholder "Search...", row font
  size, one line, 40 characters, Esc keeps the text. Its
  persistent listeners (`onValueChanged`, `onEndEdit`, `onSubmit`, `onSelect`, `onDeselect`, `OnInputSubmit`) are
  muted, not replaced (GuiInputField adds its own navigation listeners in `Start`); `CloneUtil.Strip` runs on it too.
  No source field → no search row (Debug line).
- **Counter** "Discovered N / M": a copy of the detail text (`m_textArea`: body font, its size, auto-size down to 10),
  light grey, right-aligned, on the title's line from the title's right edge to the pane area's right edge (at most
  40 % of the pane width), ending left of the visible close button when that button sits there.
- **List** (left): content = the clone's `m_listRoot`, set as the scroll view's content, top-stretched in the viewport
  (its horizontal insets kept when it was already stretched), vertical only; the left scroll bar becomes the scroll
  view's vertical bar when it had none. **Virtualized**: "visible rows + 2" row objects (grown on demand), content
  height = row count × row height (at least the viewport), list row `i` bound to pool object `i mod n` so scrolling by
  one row rebinds one object; `onValueChanged` and every bind recompute the first visible index (viewport top in
  content space; `anchoredPosition.y` while the window is at zero scale). Header rows: no background, no button, no
  icon, name from the left edge, orange. Entry rows: background and button on, icon or "?" mark, name white (known)
  or grey (unknown), `selected` on the selected row. One click listener per row object, mapped to the bound index.
  No layout group anywhere, never a `GridLayoutGroup` (`AchievementsGui.Start` reads the first one it finds in the
  scene). No nested list canvas (decision 30): the window's own sorted canvas already keeps list redraws off the
  inventory canvas, and the clone's scroll view mask clips the rows.
- **Detail pane** (right; `CompendiumWindow.Details.cs`): the right scroll view's content is replaced by our own
  `MC_Compendium_Detail` (top-stretched in the viewport); the vanilla `m_textArea` is hidden. `m_textAreaTopic` shows
  the view's title: when it sits inside the scroll view (vanilla: first child of the old content, placed by its
  `VerticalLayoutGroup`), it moves into our content as its first line (layout components removed, centred, wrapped,
  height from `GetPreferredValues`), so it scrolls with the text as in vanilla, and the old content object is hidden;
  a topic outside the scroll view stays where it is. The right scroll bar becomes the vertical bar when the view had
  none. Under the topic a **head block**: the entry icon (1.6 × row height; the sprite, the
  paw print, or the "?" mark) and the subtitle (grey, one line). Then the `DetailBuilder` lines, stacked
  by hand from pooled objects (pools grow to the largest detail seen): **Header** = orange text (8 units above);
  **Paragraph** = text wrapped at the pane width (height from `GetPreferredValues`), shown as the game writes it;
  **Row** = a row clone (icon or "?" mark + text, one line, text at the paragraph size instead of the crafting row's
  smaller one) when the line has an icon reference or a link, else wrapped text; **Collapsed** = a row with the "?"
  mark and grey text. Indent = 18 units per level. A row is a link (button
  and background on) only when one of its references is `Clickable`: a click (rumble) opens the first such reference's
  entry (`OpenEntry`: leaves the search, switches to its tab, selects it). Paragraph and header templates are clones
  of `m_textArea` without children or layout components, left/top aligned, rich text, no raycast. The pane scrolls
  back to the top on every new selection.
- **"?" mark**: the icon `Image` is disabled (its sprite set to null) and the row's "?" text is shown; the real sprite
  is never assigned for an undiscovered entry (decision 15). A discovered creature without a discovered trophy shows the paw print (`PawIcon`), in the row and the head.
- **Preparing**: while the catalog builds, the list shows one grey row "Preparing entries...", the pane and the
  counter are empty. When the build finishes while the window is open, the list binds at once (`CatalogReady`,
  3.2.2).

**Open** (`Open()`, every step guarded, 3.9; callers: the Encyclopedia top tab and RT, the remembered-tab redirect of the
`OnOpenTexts` postfix, the optional side button; returns true when the window is open afterwards):

1. **Guard**: return at once unless `InventoryGui.instance` exists, its Animator bool `"visible"` is true, and the
   local player exists, is alive, not teleporting and not in a cutscene. (The pad is already gated, 3.6; this also
   covers a late click or another mod calling the button.) A new `InventoryGui` starts a new session. Build on the
   first open (a refused build hides the top tabs and the button for the session).
2. Close the vanilla dialogs with their own close paths, each only when open: `m_textsDialog.OnClose()` (the normal
   path: the Encyclopedia tab sits on that dialog, so this is the tab switch), `m_skillsDialog.OnClose()`,
   `OnCloseTrophies()`, `OnCloseAchievements()` (a safety net only: while one of them is open the side row is locked,
   1.1, so neither the optional button nor its pad can call `Open`; reviewed, not testable in game), hide the split
   and variant dialogs, cancel a drag (`SetupDragItem(null, null, 1)`, as vanilla does); deselect a focused text field
   that is not ours
   (`EventSystem.SetSelectedGameObject(null)`, releases Crafting Search and Sort's field); `SetActiveGroup` to the side
   panel group when it is in `m_uiGroups`.
3. Group priority (creation step 5), `SetActive(true)`, the one-time layout (first open), `SetAsLastSibling()`,
   sorting order again, blocker size, tab labels (language), Encyclopedia as the current top tab, the session's search
   text back in the field.
4. Record the current biome (`OwnRecords.RecordCurrentBiome`), then **Refresh**: catalog (`EnsureReady`; null =
   "Preparing entries...", a build starts on the plugin host when none is alive, 3.2.2); knowledge snapshot
   (`Knowledge.Take(catalog, RevealAll)`, its `Summary()` logged at Debug on open); rows (`ListBuilder.BuildSearch`
   when the normalized search text is not empty, else `BuildTab` with `ShowUndiscovered`); counter; selection = the
   session's remembered entry for this tab (or for the search), else the first entry row; list bound and scrolled to
   it; details of it. Refresh also runs on every tab change, search change, reference click, `CatalogReady` and
   display setting change (new snapshot each time).

With the blocker and our top-priority group in place, the split dialog cannot open while the window is open (the
shift-drag that opens it cannot reach a slot), so Esc never has to close both at once.

**Close** (`Close(bool selectButton)`): drop search focus (a pending search is kept in the session text),
`SetActive(false)` (our `UIGroupHandler` leaves, the vanilla groups come back by themselves); with a gamepad and
`selectButton` (Esc, B, the close button), select the optional side button when it is there, else the game's Valheim
Compendium button (raven), where the player came from (`EventSystem.SetSelectedGameObject`). From the `Hide`
postfix, our Texts tab, the `OnOpen*` postfixes or the controller's safety check nothing is selected. Cheap when already closed
(called from `Hide` every frame while dead). A running catalog build is not touched.

**Safety close**: the `WindowController` closes the window when the inventory's Animator bool is off (a path that
skipped our `Hide` postfix).

**Session state**: last tab, last selected entry per tab (by kind and key, so it survives a catalog rebuild), the
search's selected entry, search text, and the remembered top tab (`TopTabs`): static, kept until a new `InventoryGui`
(logout, other world); kept across a live toggle; never saved.

### 3.8 Input

- **Mouse**: tabs, rows, reference links (extra), search field, close button, wheel scrolls either pane. The window is
  modal for the mouse **and** the controller: the clone's full-screen close button, the blocker and the sorting-order
  canvas (3.7 step 6) catch every pointer outside the window, so nothing behind it reacts (no drag, right-click use,
  eat or equip, split, side buttons or other mods' buttons). A left press and release outside the frame (a click, a
  drag or a shift-drag: the close button has no drag handler, so a drag still ends in a click) closes only the
  window, like the vanilla Compendium (decision 41); a right click does nothing. Our top-priority group turns every
  inventory group off (gamepad). A priority group alone would not be enough for the mouse (1.3).
- **Esc / B** (`InventoryGui.Update` prefix): when the window is open **and** vanilla's own key gate would run (1.2:
  no craft running, `Chat.HasFocus()` false, console, menu, text viewer, minimap closed, no cutscene, no free fly):
  on Escape or `JoyButtonB` → `CloseFromKey`: `Close(selectButton: true)`, `ZInput.ResetButtonStatus("JoyButtonB")`,
  `__instance.m_shownFrames = 0`.
  Vanilla then skips its `Hide` for that frame (`1 > 1` is false); the next Esc closes the inventory (decision 19).
  While our search field has the keyboard, `Chat.HasFocus()` is true (below), so the first Esc only leaves the field.
- **Tab, E, Y** close the whole inventory, which closes the window through the `Hide` postfix (same as vanilla dialogs).
- **Search focus guard** (only while our field has the keyboard, copied from Crafting Search and Sort's `FocusGuard`
  pattern with our own frame counter): `Chat.HasFocus` postfix ORs "until this frame + 1", `Terminal.TryRunCommand`
  prefix skips console binds (`skipAllowedCheck` calls), `UIGamePad.ButtonPressed` prefix blocks pad hotkeys. Typing
  never walks, closes, or fires a key. Enter or Esc leaves the field (then the search applies at once); while typing,
  the list refreshes 0.1 s after the last keystroke (`WindowController` → `TickSearch`). A controller user in the
  field leaves it with B (like chat). The `WindowController` sets the guard's frame number (`FocusGuard`) every frame
  the field has the keyboard and on the frame it lets go; its `OnDisable` (window closed or destroyed) does it too.
- **Search**: normalise like vanilla `BuildUi.UpdateSearch` (lower-case invariant, whitespace removed, tags stripped),
  `Contains` on the discovered entries' translated names and prefab names (`ListBuilder.BuildSearch`). With text, the
  list shows matches from **all tabs**, one header per tab. Undiscovered entries never match (goal 4). A tab click or
  LB/RB leaves the search.
- **Gamepad** (only while `ZInput.IsExclusiveGamepadActive()` and the search field does not have the keyboard, read
  by `WindowController.Update`): D-pad up/down or left stick Y (> 0.1 = down, like `TextsDialog`) moves the selection
  with a 0.1 s repeat and a rumble, skipping headers; the list scrolls to it and the details follow; LB/RB
  (`JoyTabLeft`/`JoyTabRight`) previous/next tab (wrapping); right stick Y scrolls the detail pane with vanilla's
  formula; B closes (above); LT = the Texts top tab (back to the Valheim Compendium), RT = Encyclopedia (already
  shown: nothing). Y is not used (vanilla closes the inventory on Y). Search and reference links are mouse/keyboard only
  (decision 12).
- **Top tabs on the Valheim Compendium** (3.6 "Nested tabs"): mouse click on either tab; controller LT = Texts
  (already shown: nothing), RT = Encyclopedia, read by our `TopTabsController` while the dialog is shown and its
  group active; the dialog's own input is untouched (D-pad and left stick move its list, right stick scrolls its text,
  B closes it: `InventoryGui.Update`). Why LT/RT (decision 56): the vanilla tab pair of the inventory, Craft / Upgrade,
  is on LT / RT; nothing else reads them in either dialog (1.4: the dialog's pads have no key, `TextsDialog` reads the
  sticks and the D-pad, the crafting tabs' LT/RT pads and the grids' LT/RT modifiers belong to inventory groups that
  are inactive under a priority-2 dialog and under our window); LB/RB are our category tabs; View/Select, L3 and R3
  are Sort Chest's and Loot Pickup Filter's on their grids; A/X/Y/B are vanilla's.
- **Key hints**: a `KeyHints.UpdateHints` prefix hides every controller hint while the window is open and skips the
  original, as vanilla does for its dialogs (3.9).

**MC gamepad map** (shared table of the MC inventory mods, `docs/design/ux-autopickup-filter.md` 3.4 and
`ux-container-sort.md` 3.3 carry the same rows):

| Focused group | View/Select (`JoyBack`) | L3 | R3 |
|---|---|---|---|
| 0 container grid | Sort Chest: sort | Sort Chest: criterion | none of ours |
| 1 player grid | none of ours | none of ours | Loot Pickup Filter |
| **2 side panel** | **Encyclopedia: open** (optional button only; default: none of ours) | none of ours | none of ours |
| Valheim Compendium (priority 2) | none | none | none |
| Encyclopedia window (own group) | none | none | none |

LT / RT in the Valheim Compendium and the Encyclopedia: Texts / Encyclopedia top tabs (none of the rows above reads
them).

### 3.9 Patches

All bodies catch their own exceptions (`PatchGuard.Report`). Applied only while the feature is Active; no
transpiler; default priority. Files: `Patches/InventoryGuiPatches.cs`, `TextsDialogPatches.cs`, `KeyHintsPatches.cs`,
`EnemyHudPatches.cs`, `PlayerPatches.cs`, `ChatPatches.cs`, `TerminalPatches.cs`, `UIGamePadPatches.cs`. The same guard covers every callback
that vanilla code or Unity calls directly, not only patches (see "Not patches" below).

| Target | Type | What it does | Why |
|---|---|---|---|
| `InventoryGui.Awake()` (private) | Postfix | `SideButton.Create(__instance)` (nothing while the optional button is off) | One button per `InventoryGui` (world session). |
| `InventoryGui.Show(Container, int)` | Postfix | `SideButton.OnShow` (nothing while the optional button is off): create the button if missing, re-apply the placement, set our pad's `m_group` to `m_uiGroups[2]` once its `Start` has run, start the once-per-`InventoryGui` self-check (3.6) | UI mods may move vanilla buttons after `Awake`; `Show` does no layout, so the check waits for a real scale. |
| `InventoryGui.Update()` (private) | Prefix | Esc / B closes only our window (3.8) | Must run before vanilla's key block; keeps vanilla `Update` running (other mods transpile it). Idle cost: one bool. |
| `InventoryGui.Hide()` | Postfix | `Close(selectButton: false)` | Tab/E/Y, death, teleport, cutscene. Cheap when closed (runs every frame while dead). |
| `InventoryGui.OnOpenTexts` / `OnOpenSkills` / `OnOpenTrophies` / `OnOpenAchievements` | Postfix | `Close(selectButton: false)`; `OnOpenTexts` then `TopTabs.OnRavenOpened`: remembered tab Encyclopedia (and the tabs on the dialog) → our window opens in its place | One side dialog at a time. While our window is open no vanilla path reaches these (blocker, top-priority group), so only our Texts tab (which closes the window first and remembers Texts) or another mod's code can (a hotkey mod, Almanac's Trophies takeover; postfixes run even when its prefix skips the original). `OnOpenTexts` is the raven path, so the remembered tab follows it; `Setup` (below) would also catch another mod refilling an open dialog. |
| `TextsDialog.Setup(Player)` | Postfix | `TopTabs.OnVanillaShown`: our two tabs on the dialog (made when missing, Texts current) | `Setup` is what shows the dialog, whoever calls it; vanilla and other mods' list patches run first, we only add a child. Cheap when the tabs are there (one reference compare). |
| `KeyHints.UpdateHints()` (private) | Prefix | While open: turn off every hint object that is on and skip the original (return false) | Vanilla does the same for its dialogs (1.1: all off, return). A postfix (first version) let vanilla switch the inventory hints on and turned them off after it, every frame (enable/disable churn under them). |
| `EnemyHud.UpdateHuds(Player, Sadle, float)` (private) | Postfix | With a local player and a main camera: every `HudData` whose `m_gui` is active (the plate vanilla just decided to show) and whose character is not a player: record `c.m_name` in `Seen` | First encounter = the name plate really shown (1.11, decision 4). Replaces the first design's `ShowHud` prefix, which recorded every character within range, seen or not. Cost per frame: one loop over the plates within 30 m. |
| `Player.AddKnownBiome(BiomeSector)` (private) | Postfix | Record `biome.Biome` in `Biomes` (local player only) | Language-independent biome knowledge (1.10). |
| `Player.ResetCharacter()` | Postfix | Remove our two keys (local player only) | Follows the vanilla reset. |
| `Chat.HasFocus()` | Postfix | OR our field focus | Search focus guard. |
| `Terminal.TryRunCommand(string, bool, bool)` | Prefix | Skip bind calls while our field has the keyboard | Search focus guard. |
| `UIGamePad.ButtonPressed()` | Prefix | Return false (skip original) while our field has the keyboard; for **our** pad (`ReferenceEquals(__instance, SideButton.Pad)`), also return false unless `SideButton.PadAllowed()` (3.6) | Search focus guard; View/Select gate. `ButtonPressed` runs before the shared 2-frame lock is taken, so a rejected press never blocks Sort Chest's pad or the map. |

Not patches, but guarded the same way (each in a `try` with `PatchGuard.Report`; an exception must never reach
vanilla code, and in a multicast delegate it would stop the later subscribers): the button, tab, row, link and close
`onClick` listeners (`UIGamePad.Update` calls `OnSubmit` without a guard), the search field and list scroll listeners,
the self-check coroutine steps, the top tabs' `TopTabsController.Update` and `OnDisable`, `SettingChanged` of the
optional button setting (`SideButton.Sync`, only while active); `Localization.OnLanguageChange` (subscribed
in `OnActivated`; drops cached names and sort keys; an exception there would stop vanilla `Localize` components that
subscribed after us from re-localizing); `SettingChanged` of the display settings (raises `Plugin.DisplaySettingsChanged`, each subscriber guarded; the UI
refreshes an open window); `CatalogService.CatalogReady` subscribers (each guarded); every
step of the build coroutine (3.2.2); `WindowController.Update` and `OnDisable`; `DetailBuilder.Build` (3.5); window
build and layout (`TryBuild`, `TryLayout`).

**Activation check** (`EscGuard.Verify`, in `OnActivated`, pattern of Loot Pickup Filter's `AutoPickupGuard`): the
original IL of `InventoryGui.Update` must hold `ldfld m_shownFrames; ldc.i4.1;` followed by a "greater than" test,
either as a value (`cgt` / `cgt.un`: what 1.0.16 compiles, 1.2) or as a branch (`ble`, `bgt` and their short and
unsigned forms), and later a `Hide` call. The increment at the top also loads the field then 1, but adds, so it never
matches. If a game update breaks that, one Warning ("InventoryGui.Update changed (<what>): Esc may close the whole
inventory instead of only the Encyclopedia.") and the mod keeps working: only the Esc nicety degrades (decision 19).
OK = a Debug line. The result is kept (`EscGuard.Problem`) and `compendium.ui` fails when the check did not pass.
(The first version accepted only a branch right after the constant and warned wrongly on 1.0.16.)

### 3.10 Configuration

| Section | Setting | Type / default | Description (user-facing) |
|---|---|---|---|
| General | Enabled | bool `true` | (framework) Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | string, read-only | (framework) Shows whether the feature is active, and if not, why. |
| Display | ShowUndiscovered | bool `true` | Show entries you have not discovered yet as "???". Turn off to list only what you have discovered. |
| Display | RevealAll | bool `false` | Spoiler mode: show every entry and every detail, discovered or not. Your discoveries are still recorded. |
| Display | SideButton | bool `false` | Also add an Encyclopedia button to the inventory's side panel, right after the Valheim Compendium button (the side panel's buttons move a little closer together to make room). Without it, open the Encyclopedia from the Encyclopedia tab at the top of the Valheim Compendium. |

Both display settings apply at once to an open window (`SettingChanged` → `Plugin.DisplaySettingsChanged`) and at the
next open otherwise. Code reads them only through `Plugin.ShowUndiscoveredOn` (passed to `ListBuilder.BuildTab`) and
`Plugin.RevealAllOn` (passed to `Knowledge.Take`; also the Debug console command). In Debug builds these accessors
return `Plugin.TestDisplay` when a self test set it: `compendium.ui` forces the defaults in memory, so a tester's
changed `.cfg` (T18) never makes it fail, and no test writes the `ConfigEntry` (the harness puts `.cfg` files back
only after the run). `SideButton` applies at once (`SettingChanged` → `SideButton.Sync`: button made, or destroyed with the
row put back exactly) and is read only through `Plugin.SideButtonOn`; its Debug-only in-memory override
`Plugin.TestSideButton` lets `compendium.ui` and `compendium.toggle` force it off and `compendium.siderow` force it on
and off (then `SideButton.Sync`), never writing the `ConfigEntry`.

### 3.11 Multiplayer and hand-off

- Client-side, compatible, `ModNetworkVersion` not needed: no RPC, no ZDO key, nothing on items, pieces or other
  players. Works on vanilla and dedicated servers.
- Knowledge is per character (vanilla sets and the two `m_customData` keys), like vanilla recipes. Kill counts are the
  game's own: the victim's owner credits every player who hit it, modded or not (M01).
- Hand-off: nothing the mod does can reach another player. A friend without the mod sees nothing different; creatures
  their game simulates are still "met" by us through our own name plates (shown when we aim at them), and kills they
  own are still credited to us by vanilla (M02).
- The catalog reflects the local game's data: a server-synced recipe or drop mod that changes data after connecting
  is picked up by the fingerprint at the next open (counts change, also when it lands during a build, 3.2.2) or by the
  click-time details (in-place edits).

### 3.12 Live toggle, creation and cleanup

- `OnActivated`: data: `CatalogService.SetHost(this)`, clear the records and tames caches, subscribe
  `Localization.OnLanguageChange` (`Names.OnLanguageChanged`), record the current biome, register the data self tests
  and the Debug console command (the settings' `SettingChanged` are subscribed once in `BindConfig` and only raise
  `Plugin.DisplaySettingsChanged`; `BindConfig` also keeps `Plugin.Instance` for the toggle self test). UI:
  `EscGuard.Verify`; subscribe `CatalogService.CatalogReady` and `Plugin.DisplaySettingsChanged` (window refresh);
  register the UI self tests; if `InventoryGui.instance` exists: the top tabs at once on a shown Valheim Compendium
  (`TopTabs.OnActivated`), and the optional button (`SideButton.Sync`: `OnShow` when the inventory is shown, so it is
  placed and checked at once, else `Create`; it appears at once, even with the inventory open, T22). The window is
  built at its first open.
- `OnDeactivated` (patches still applied; each step in its own `try`): UI first: close the window, destroy it (its
  `UIGroupHandler` leaves the static list in `OnDestroy`; blocker, canvas, templates and pools go with it) and, when
  it was open, `SetActiveGroup` back to the side panel group; the top tabs off the Valheim Compendium (hidden and
  destroyed at once, the dialog left open and exactly the game's, `TopTabs.Destroy`); reset the focus guard; put the vanilla side controls
  back exactly (positions, scales, sizes and the two navigation links, `SideRow.Restore`, 3.6 step 5); destroy the
  button (its pad and tooltip go with it) and the drawn sprites and textures (book icon, paw print); unsubscribe the two window hooks; reset the clone-strip log;
  unregister the UI self tests. Then data: unsubscribe the language hook; stop the build coroutine on the plugin host
  and clear its handle, forget the catalog (`CatalogService.Stop`, 3.2.2); clear the records and tames caches;
  unregister the data self tests and the console command. The session memory (tab, selections, search, remembered top
  tab) is kept.
- While off, nothing is recorded (met creatures, biomes); kills, trophies, materials and recipes keep being recorded
  by the game and show when the mod is back.
- A new `InventoryGui` (logout, new world): the old objects died with it; statics are checked with
  `ReferenceEquals` / Unity `== null` and rebuilt lazily. A build still running for the old world stops by itself at
  its next step (its `ObjectDB` / `ZNetScene` references are no longer current, 3.2.2).

### 3.13 Compatibility

**Other mods**

- **Almanac**: it owns the Trophies button and its own HUD window; ours lives in the Valheim Compendium. Its `InventoryGui.Hide`
  prefix may skip `Hide` while its search is focused (our `Hide` postfix still runs: our window closes, which is
  fine). Its `IsVisible` postfix does not matter: we never gate on `IsVisible()`. Its `Chat.HasFocus` postfix forces
  false only while its own search is focused (cannot be at the same time as ours).
- **PraetorisClient** (Server Guide): both clone the Valheim Compendium; opening ours does not close its window (it is
  not a vanilla dialog); both `Update` prefixes act only while their own window is open. Our top tabs sit on the vanilla
  dialog only while it is shown, so its clone never copies them (1.4; X11).
- **Jewelcrafting, EquipmentAndQuickSlots**: their `TextsDialog` patches never touch our clone (component destroyed);
  leftover elements are stripped; their pages stay on the Texts tab (the vanilla dialog), which we close with its own
  `OnClose` (Jewelcrafting's clean-up) when switching to the Encyclopedia. Jewelcrafting transpiles `InventoryGui.Update`: our prefix leaves vanilla's method
  (and its transpiled code) running.
- **UI overhauls** (AugaLite, Auga, Veneer): no Valheim Compendium dialog or a changed one → no top tabs (one warning);
  with the optional button, no anchor → one warning, no button; no crash.
- **Mods that move or add side panel controls** (optional button on only; off, the side panel is never touched): a
  control another mod adds to the row joins our even spacing (it is
  a control of the row); a control another mod moves keeps its new place (it becomes its baseline) and the row is laid
  out again; a mod that keeps moving them makes us give up after the third change (3.6 step 5: our changes undone,
  our button after the last control, one Warning). A mod that puts a layout group on the row: our button becomes a
  sibling right after the anchor and the layout places it.
- **ExtraSlots, EquipmentAndQuickSlots panels**: may overlap the side column (optional button); the self-check warns
  (X09).
- **Content mods** (Jotunn items, pieces, creatures; Bestiary by Radamanto; Monstrum...): appear automatically when
  registered in `ObjectDB` / piece tables / `ZNetScene`. Their tokens are translated by their own localization.
  Modded creatures without a spawn entry get "Habitat unknown".
- **Drop, spawn and recipe editors** (Drop That, Spawn That, CLLC, ServerSync YAML mods): the Encyclopedia shows the
  prefab data at build time; per-creature runtime changes do not show (README "Good to know").
- **Nameplate mods** that skip or replace `EnemyHud.UpdateHuds`: "met" is not recorded; kills, trophies and tames still
  discover creatures (README).

**MC mods**

- **Creature Kill and Tame Counts**: same kill numbers (both read slot 0); its tames are read from its documented key;
  its Player Statistics section stays in the vanilla Compendium (its `AddStats` postfix never runs on our clone). No
  dependency in either direction (X01).
- **Crafting Search and Sort**: its field loses focus when our window opens. Both focus guards are OR-ed postfixes and
  coexist; while our field has the keyboard, its `CanFocusNow` sees `Chat.HasFocus()` true and never steals it. Open
  risk: its F key focuses the crafting search from its own `Update` (a key, so the blocker does not stop it); while
  our window is open the crafting panel's `CanvasGroup` is not interactable, so its field should refuse focus
  (**unverified**, X02). If it does not, that mod
  gets a one-line check later (its `CanFocusNow` already refuses for the vanilla dialogs).
- **Loot Pickup Filter**: its R3 gestures need the player grid group (inactive while our window is open); its middle
  click and its Auto pickup button (hung on `m_inventoryRoot`, outside any group `CanvasGroup`) are behind our blocker
  and sorting-order canvas (3.7 step 6). Its lists panel is instantiated under the canvas root when opened; it cannot
  be opened while our window is open, and if it is already open when ours opens, our higher sorting order draws over
  it (X03).
- **Sort Chest**: its pads need the container group (inactive while open) (X04). View/Select on the chest grid: our
  pad's `ButtonPressed` gate rejects the press before the shared lock is taken, so Sort Chest's pad always gets it
  (3.6, X04).
- **One Click Repair All, Batch Station Feeding, Harpoon Hooks Tames, Crossbow Stays Loaded**: no shared method or
  state; no cross-mod test.

### 3.14 Performance

- Catalog build: once per world session (and on fingerprint change), spread at 4 ms per frame. Expected work: about
  1500 item prefabs, ~480 recipes, ~450 pieces, a few thousand `ZNetScene` prefabs with root `TryGetComponent` calls,
  a few hundred vegetation entries, the spawn lists (counts **unverified**, logged with the time). Localized names
  computed once per language (the game's 100-entry cache would thrash otherwise).
- Open: fingerprint compare + knowledge snapshot (a few thousand hash lookups) + one list bind (~20 rows); the group
  priority scan (`GetComponentsInChildren` + `FindObjectsByType<UIGroupHandler>`) once per open. First open only: the
  window build and layout (one clone, 8 tabs, a few templates).
- Scrolling: only rows whose index changed are rebound; no allocation per frame.
- Selection: one `DetailBuilder` pass (a few dozen lines, the long reverse lists at most a few hundred references),
  pooled objects; one `GetPreferredValues` per text line.
- Window open, per frame (`WindowController`): an Animator bool, the search focus check, the blocker's 4 corners (set
  only when changed), the gamepad reads when a controller is active. Closed: nothing (the object is inactive).
- Per-frame patches: `InventoryGui.Update` prefix (one bool while closed), `EnemyHud.UpdateHuds` postfix (one loop over
  the plates of the characters within 30 m, `activeSelf` each, a set lookup for the shown ones), `KeyHints.UpdateHints`
  prefix (one bool while closed), focus guard postfixes
  (one int compare), `UIGamePad.ButtonPressed` prefix (one int compare and one reference compare per pad that is
  about to read its key; the visibility and group checks only for our pad).

### 3.15 Debug aids (Debug build / Debug log level)

- **Log level**: BepInEx's default log file keeps Info and above only, and `Test-InWorld.ps1` reads that file. So the
  layout dumps (Debug builds only) log at **Info**, and the UI trace lines (`UiUtil.Trace`: button created / placed,
  pad group, row laid out again, window built, window layout, tabs in two rows, no search field, no close button) log
  at Info in Debug builds and at Debug in Release builds. The first build's dumps logged at Debug and never showed,
  which hid that the window refused to build (1.4 trap).
- **Layout dumps** (`Ui/DebugLayoutDump.cs`, `[Conditional("DEBUG")]`, the Loot Pickup Filter pattern), four Info
  lines. Each object line gives: name, on/off, components, rect (in the dump's space, or world rect), anchors, pivot,
  size delta, anchored position, scale when not 1; texts (text, font, size, auto-size range, alignment, colour);
  images (sprite, type, colour, disabled, raycast); scroll rects (content, viewport, directions, bar, movement,
  sensitivity); scroll bars (direction, handle); layout groups (padding, alignment, spacing); persistent click
  handlers; `notInteractable` buttons; `UIGamePad` key and hint; `UIGroupHandler` priority and state; which
  `TextsDialog` field points at it. At most 400 object lines per dump.
  - `Encyclopedia top tabs on the Valheim Compendium ...`, once per `InventoryGui`, the first time our tabs sit on
    the vanilla dialog: both tabs (world rect, interactable), the title, both pane boxes, the wood frame; `OVERLAP:`
    (a tab over the title, the visible close button, a pane or the other tab) and `OUTSIDE:` (a tab leaving the frame)
    lines; every `UIGamePad` of the dialog with its key (1.0.16: three, none with a key, 1.4); the dialog's tree to
    depth 4 in its own space.
  - `Encyclopedia side panel layout ...` (optional button only), once per `InventoryGui` at the end of the button
    self-check (panel settled):
    the anchor and its handler; the side panel parent of the anchor and of `m_pvp` (path, components, `LayoutGroup`
    settings, nearest `UIGroupHandler` priority and index in `m_uiGroups`, nearest `CanvasGroup`, world rect); every
    `Selectable` there (type, active, world rect, navigation and its explicit links, transition and colour tint,
    persistent handlers, `UIGamePad` key, `UITooltip` topic, child images and sprite names); the row's ancestors up to
    the inventory (world rects, images); the row parent's tree to depth 3 in world rects; the row layout state
    (`SideRow.State`: controls, step, vanilla step, scale, inset, background, axis, navigation linked, outside
    changes, or why it gave up); our button (rect, anchored position, sibling index, placement, self-check result,
    icon, tooltip, pad key and group); every `UIGroupHandler` under the inventory (priority, index, active, canvas
    group) and every other active one in the scene; the root canvas (mode, order, scale, scaler settings), every
    sorting canvas under it and other root canvases; `m_secretAchievementIcon` name and size; `m_tabCraft` and
    `m_recipeElementPrefab` to depth 2; what `CloneUtil.Strip` removed per kind (3.6).
  - `Encyclopedia texts dialog source ...`, once per `InventoryGui` when the window build starts, **before any check**
    (so a refused build still shows the real structure): every `TextsDialog` reference with its path (or "OUTSIDE the
    dialog"), `m_spacing`, the `ScrollRect`s above `m_textArea`, the dialog's world rect and parent, and its whole tree
    to depth 9 in the dialog's space (plus `m_elementPrefab`'s tree when it lives outside).
  - `Encyclopedia window layout, screen ...`, once per window, on its first open frame (`WindowController` →
    `AfterFirstFrame`): our window (world rect, canvas override and order vs the root canvas, group priority and
    state); title, counter, topic, visible close button, search, blocker, list and detail viewports, each category
    tab, both top tabs and the first list row; `OVERLAP:` lines when a tab, a top tab, the counter or the search
    covers the title, the visible close button or another of these; our window's tree to
    depth 5 (pooled rows and the templates not expanded); the canvases again; the clone strip log.
  - Trace lines (Info in Debug builds, Debug in Release): `Encyclopedia window built from '<dialog>': list '<view>',
    details '<view>', title '<text>', close buttons <n> (visible '<name>'), row height <h>, group priority <p>.` and
    `Encyclopedia window layout: panes ... (boxes '<left>', '<right>'), tabs ..., band ..., search ..., title ...,
    counter ....`, `Encyclopedia tabs: 8 tabs do not fit ... two rows.`, `Encyclopedia window top tabs: texts ...,
    encyclopedia ..., title ... (window units).`, `Encyclopedia tabs on the Valheim Compendium: texts ..., encyclopedia
    ..., title ..., panes ... (dialog units).` (once per `InventoryGui`).
- Catalog log (3.2.2), plus `Catalog build started.`, `Catalog build abandoned (world ended).`, `Catalog build
  restarted (previous build stalled).` (or `(world changed).`). Knowledge summary per open (`Knowledge.Summary()`,
  logged by the UI at open): `Knowledge: <k> of <n> entries discovered (<items>/<pieces>/<creatures>), <s> creatures
  met, biomes <mask>.`
- Records: `Met <name> (its name plate was shown).`, `Biome recorded: <biome>.`, `Character reset: Encyclopedia records of met
  creatures and visited biomes cleared.`
- Button (trace lines): `Encyclopedia button created from '<anchor>' (<handler>), icon on '<image>', tooltip <bool>,
  pad <key>.`, `Encyclopedia button pad group '<old>' -> side panel group '<name>'.`, placement `Encyclopedia button
  placed <how> (<rect>): inside the panel, no overlap, on screen. Row: <state>.` (or `...: after the last control it
  <problem>.`), `Encyclopedia side row: a side control was moved by something else (<n> time(s)): row laid out again.`,
  the Warnings of 3.6 step 5, and `InventoryGui.Update Esc gate checked: ... (<opcode>) ...` at activation (Debug).
- **Console command** `compendium_debug [token or prefab]` (`DebugCommand.cs`, Debug build only, not a cheat): no
  argument = catalog counts, discovered / listed per tab and the knowledge summary; with an argument = that entry's
  `DetailView.Dump()` (the lines the window would show, with the character's real knowledge). Printed in the console
  and the log.
- **In-world self tests** (`SelfTests.cs`, Debug build only, registered in `OnActivated`, removed in `OnDeactivated`;
  run by `tools/Test-InWorld.ps1` with the fresh probe character "MCProbe"; each ends with one PASS or FAIL line, NOTE
  lines carry the numbers):
  - `compendium.catalog`: waits for the build (60 s max); every tab above a minimum far below the vanilla numbers
    (counts per tab noted); no duplicate (kind, key); every entry has an index, a name, a prefab, a sub-group of its tab
    and (items, pieces) an icon; every entry in exactly one sub-group; vanilla anchors present and not hidden (Wood,
    Bronze Sword, Boar Trophy, Workbench and Forge as stations, Boar with the Boar Trophy, Forge with recipes and
    upgrades); at least half of the creatures have a habitat; every item a listed creature's `Procreation` gives
    birth to has that creature as a "laid by" source. Notes: hidden-until-known count per rule, the unverified prefab
    data (`PlayerUnarmed`, `CapeTest`, `SwordCheat`, `Abomination_attack1`, `bjorn_bite`, `BeltStrength`,
    `FishingRod`, `DvergerTest`, `Boar_piggy`, `Hen`, the Hen's offspring, traders found, upgrade-only recipes), the
    prefab names of the listed items with no source found, and every prefab of `ZNetScene` (networked and not) with a
    `CraftingStation` whose `m_upgrader` is set: prefab, `m_name`, English name and `m_hasCraftTab`, plus how many
    recipes carry an upgrade-station requirement and how many of those have a quality-1 amount (3.5.1; 1.0.16:
    `UpgradeStation` "Forge of Potential", no Craft tab; 225 and 225).
  - `compendium.details`: `DetailBuilder.Build` for every entry with three knowledges (the probe's real one, reveal all,
    a synthetic "every other entry and five biomes known"), each entry in its own `try`; FAIL lists the first entries
    that throw or have a failed block. Spoiler checks with each knowledge: an undiscovered entry's view has title
    "???", no icon, no reference and no own text; every reference's `Known` matches the knowledge, and an unknown one
    is "???" with no sprite; a creature never shows the sprite of an undiscovered trophy; no label text part (not a
    reference, not the entry's own game text; subtitles excluded, they are game category labels) contains an
    undiscovered entry's name (whole word, names of 4+ letters); list rows of every tab: undiscovered rows are "???"
    with no sprite, come after the discovered rows of their group, hidden-until-known ones never appear undiscovered,
    `ShowUndiscovered = false` lists none; list headers of a group whose tool is undiscovered start with "???" and never
    name the tool, headers of an unvisited biome group are "???" and never name the biome (`SelfTests.HeaderLeak`;
    the note counts the headers this really tested); search for 40 undiscovered names returns no undiscovered entry.
    With everything revealed, each item's crafting block is checked as the Craft tab charges it: "At an upgrade
    station:" only inside a quality row, one head per craftable recipe, none for an upgrade-only one (note: how many
    items list upgrade-station rows). The character's skill list (type, level, accumulator) is the same before and after
    building every detail (note: both lists and how many level-0 entries the tooltips added and the builder removed).
  - `compendium.knowledge`: the fresh character knows under a quarter of the entries (unlock modes ignored); then,
    the vanilla way or with the own records, it holds an item (`Player.AddKnownItem`, Raspberries first), holds a
    trophy (Boar Trophy: the trophy and the Boar), gets a kill (profile slot 0, Greyling), meets a creature (`Seen`,
    Deer), places a piece (profile, Workbench), sees a station (`m_knownStations`, Forge) and visits a biome (`Biomes`);
    each flips to discovered, the kill count reads 1 and the Boar shows its trophy icon while the Greyling shows the
    generic creature icon; the Greyling's details contain "Killed: 1". Everything is put back in a `finally` in the
    same frame (known sets, stations, profile values, both custom data keys, `UpdateEvents`) and a last snapshot must
    match the first one. **Unlock modes**: with `m_noPlacementCost` set for the same frame (the no-cost path, shared
    with the world keys), an item with a recipe the Craft tab offers and a piece become discovered, and no item whose
    recipes the Craft tab does not offer now (seasonal out of season, upgrade-only) does (note: counts, the season).
    **Met through the real name plate**: a Deer (or the first undiscovered creature) is spawned 5.3 m away, in front of
    the camera but off the crosshair, and held in place; within 60 frames it must get its `HudData` (plate created),
    then its plate must be off and the Deer not met and not discovered; after its `m_hoverTimer` is set to 0 (what the
    crosshair on it does in `UpdateHuds`) its plate must show and the Deer be met and discovered. Clean-up: the timer
    back to 99999 (plate off, nothing recorded before the destroy takes effect), the Deer destroyed, `Seen` put back,
    and again one frame later. The note gives `EnemyHud`'s distances and duration (1.0.16: 30 m, 100 m, 60 s).
- **In-world UI self tests** (`Ui/UiSelfTests.cs`, Debug build only, registered after the data tests in `OnActivated`,
  removed in `OnDeactivated`; same runner and rules):
  - `compendium.ui`: waits for the catalog; forces the display settings to their defaults and the optional button off
    in memory (`Plugin.TestDisplay`, `Plugin.TestSideButton`, 3.10), Texts as the remembered tab; makes one item
    discovered (`m_knownMaterial` gets Raspberries' token, or the first undiscovered listed item with an icon) and
    adds one kill to a creature (Greyling, profile slot 0), all put back in `finally`; opens the inventory
    (`InventoryGui.Show(null)`, 1.5 s); **Esc gate**: the activation check passed (`EscGuard.Problem` null); **no side
    button** (nothing named `MC_Compendium_Button`); screenshot `side-panel`. **Vanilla dialog**: the raven button's
    `onClick.Invoke()` (its prefab-saved `OnOpenTexts`, as a click): the dialog is shown with our strip as its last
    child, Texts not interactable, Encyclopedia interactable, labels "Texts" / "Encyclopedia", both tabs inside the wood
    frame, not overlapping each other, the title (rect grown to its rendered text), the visible close button or either
    pane box (NOTE: every rect); screenshot `texts-tabs`. **Switch**: the vanilla Encyclopedia tab's `onClick.Invoke()`:
    our window open, the vanilla dialog closed, Encyclopedia remembered; window root and frame on the vanilla dialog's
    and frame's world rects (1 px), our two top tabs on the vanilla tabs' rects (1 px), Encyclopedia current, clear of
    the 8 category tabs, the title, the counter, the search field and the close button (NOTE); **window**: not
    "Preparing entries..." within 30 s; **modal**: our focus group active, every `m_uiGroups` group whose object is
    active in the hierarchy inactive (an inactive object, like the container grid with no chest open, never runs its
    `Update`: its flag is stale and it takes no input), our canvas overrides sorting above the root canvas, the blocker
    covers the screen, and an `EventSystem.RaycastAll` at the raven button's centre and at the first inventory slot's
    centre lands on an object of our window (NOTE: what each lands on); screenshot `window`; **tabs**: each of the 8
    category tabs opens, lists rows, no bound row of an undiscovered entry shows a sprite (icon off, "?" mark on, name
    "???"), and every bound header row shows its header text and passes `HeaderLeak`; screenshot per tab
    (`tab<N>-<Tab>`); **undiscovered entry** (first unknown row of the item's tab): details title "???", "?" mark, no
    sprite; screenshot `undiscovered`; **discovered item** (`OpenEntry`): selected, title = its name, icon shown;
    screenshot `discovered-item`; **creature**: selected, a rendered line equals "Killed: N", and (trophy undiscovered)
    the head and its list row show the paw print sprite; screenshot `creature`. **Back to Texts**: our Texts tab's
    `onClick.Invoke()`: window closed, vanilla dialog shown with the tabs (Texts current), its list filled by its own
    `Setup` (every entry's row alive), Texts remembered; screenshot `texts-again`. **LT / RT** (a trigger press cannot be
    faked: `TopTabs.Select`, what both controllers run): Encyclopedia twice in one frame → one window, the vanilla
    dialog closed, the window back on the category tab and entry it had (session memory); Texts twice → the vanilla
    dialog, as above. **Remembered tab**: Encyclopedia, then the Esc path, then the raven → our window directly;
    Texts, the vanilla dialog closed (`OnClose`), the raven → the vanilla dialog with its tabs. **Side button way**:
    the vanilla dialog closed, `CompendiumWindow.Open()` alone (what the optional button runs), then Encyclopedia
    (RT) in the window: Texts still remembered; the window closed, the raven → the vanilla dialog. **Esc path** (a key
    press cannot be faked): our prefix is on `InventoryGui.Update` (`Harmony.GetPatchInfo`, owner = our GUID), then
    `CloseFromKey` (what the prefix runs after its key gate) closes the window, does not show the vanilla dialog, and
    leaves `m_shownFrames` at 0, which makes vanilla's `m_shownFrames > 1` gate skip `Hide` that frame (with the IL
    check at activation, this covers the chain; the real key is T04). **Hide** closes everything and the strip leaves
    the vanilla dialog. Window, vanilla dialog and inventory are closed, the remembered tab and the forced settings put
    back (then `SideButton.Sync`) in `finally`.
  - `compendium.toggle`: optional button forced off, Texts remembered; inventory shown; **a.** the raven opens the
    vanilla dialog with our tabs; after 0.5 s (its text layout and list snap settle) a snapshot of the dialog: every
    object except our strip, the list content and the scroll bars' insides (driven by the scroll views): path, on/off,
    sibling index, anchors, pivot, anchored position, size, scale, rotation and text, exact values. **b.** `Enabled =
    false` with it open: the feature inactive, the strip gone, the dialog still open and equal to the snapshot, nothing
    named `MC_Compendium_*` under the inventory (NOTE; screenshot `disabled`). **c.** `Enabled = true`: the strip back
    at once (Texts current), the rest still equal (screenshot `reactivated-texts`). **d.** the Encyclopedia tab: `Enabled
    = false` forgot the catalog (`CatalogService.Stop`), so this open starts a fresh build (T25 path, by hand only
    about 0.1-0.2 s): one frame later the window must say "Preparing entries..." with a build running; it is closed at
    once, the build must finish with the window closed (60 s max; 1.0.16: 7 more frames), and the raven (Encyclopedia
    remembered) must reopen it with rows at once, not "Preparing", the vanilla dialog closed (NOTE). **e.** `Enabled =
    false` with our window open: the window object destroyed (Unity null after a frame), no static still points at it
    or at a button, nothing named `MC_Compendium_*` left. **f.** `Enabled = true`: no side button appears; the raven
    opens a **new** window at once (the remembered tab survives the toggle); screenshot `reactivated`. `Enabled`, the
    forced setting and the remembered tab put back, window, dialog and inventory closed in `finally`.
  - `compendium.siderow`: **default**: optional button forced off, inventory shown: no button, nothing named
    `MC_Compendium_Button`, `SideRow` untouched ("not laid out", no baseline); the game's row noted (position, scale,
    size, navigation of every control; NOTE; screenshot `default-off`). **Opt-in cycle**: forced on + `SideButton.Sync`:
    button there, placement `Row`, `FindProblem` finds nothing, View/Select pad, "Encyclopedia" tooltip, our button
    right after the raven, every control inside the panel background, no two overlapping, centre gaps equal within
    1.5 px (NOTE: row state and rects; screenshot `opt-in`); forced off + `Sync`: button object gone, every control
    exactly as noted (float for float, navigation `Equals`); on again: row again; `Enabled = false` with it on: exactly as
    noted; `Enabled = true`: row again. **Row dropped**: the anchor (the raven) is deactivated, as a UI mod hiding it
    would, and `SideButton.OnShow` runs: placement no longer `Row`, navigation no longer linked, both vanilla links equal
    to their baselines, ours `Automatic`, every control on its baseline; the anchor back and `OnShow` again: placement
    `Row`, links as before, no outside change counted (NOTE lines). Then **outside moves**: the first vanilla control
    that is not the anchor (Skills) plays a control "another mod" moves: three times, it is moved 3 units up and
    `SideButton.OnShow` runs (what the `Show` postfix runs). Moves 1 and 2: the row is laid out again, the moved control
    keeps its new height, the change is counted, `FindProblem` finds nothing. Move 3: we gave up (placement no longer
    `Row`), the moved control stays where the "other mod" put it, every other control is back on its baseline;
    screenshot `gave-up`. Clean-up in `finally`: the anchor active again, `Enabled` off, the moved control back on its
    vanilla place, `Enabled` on; then the row must be laid out again and the button must have its View/Select pad (off
    and on happened in the same frame). **Refused window**: forced off + `Sync`, the window's refusal played on this
    `InventoryGui` (`CompendiumWindow.SetRefusedForTest`, Debug only; its flag only, put back in `finally`), forced on +
    `Sync`: no button, every control exactly as noted; off and on again: still none (screenshot `refused-window`);
    refusal put back, off and on: the button is back in the row. **Back to the default**: forced off + `Sync`: no
    button, every control exactly as noted at the start. The override cleared (`Sync` to the real setting) and the inventory closed in `finally` (NOTE
    lines give every step). The give-up and "outside the side panel background" Warnings of this test are expected in
    its log.
### 3.16 Files

Built in the data step (all under `src/Exploration/Compendium.Encyclopedia/`):

| File | Responsibility |
|---|---|
| `MC.Exploration.Compendium.Encyclopedia.csproj` | Metadata, `ModName` Encyclopedia, `<ModIdea>Compendium</ModIdea>`, Client, Compatible. |
| `Plugin.cs` | Config (`ShowUndiscovered`, `RevealAll`, `DisplaySettingsChanged`, `SideButton` and its live sync), `OnActivated`, `OnDeactivated` (3.12). |
| `Labels.cs` | Every English label and vanilla token the mod shows (window title, tooltip, top tabs, category tabs, groups, detail words). |
| `Catalog/Entry.cs` | `Entry`, `EntryKind`, `CatalogTab` + `Tabs`, `IconKind`, `SubGroup`, `ItemInfo` / `PieceInfo` / `CreatureInfo` and relation records. |
| `Catalog/Catalog.cs` | Entries, sub-groups per tab, lookups, counts; `Fingerprint`. |
| `Catalog/CatalogBuild.cs` | `CatalogService`: build coroutine on the plugin host, handle, generation, atomic swap, abort and stall rules, `CatalogReady`, logs (3.2.2). |
| `Catalog/CatalogBuilder.cs` | `BuildBudget`, `BuildStage`; stages items, recipes, pieces, creatures, finalize; inclusion and hidden rules (3.2.4); tabs and sub-groups (3.2.6). |
| `Catalog/SourceScan.cs` | Stages world sources, vegetation, spawn lists, habitats: gathering, chests, conversions, producers, traders, fish, nests, inheritance, trophy fallback, bosses (3.2.3, 3.2.5). |
| `Catalog/TrophyBiomes.cs` | Trophy → biome fallback (copy of Sort Chest's trophy rows, "keep in sync"). |
| `Catalog/Biomes.cs` | Progression order, biome tokens, first biome of a mask. |
| `Catalog/Names.cs` | Localize, missing-key test, tag stripping, sort compare, search keys, language stamp. |
| `Catalog/ListBuilder.cs` | `ListRow`, `ListBuilder.BuildTab` / `BuildSearch` / `CountTab` (3.2.7, 3.8). |
| `Discovery/Knowledge.cs` | Discovery rules and snapshot (3.3). |
| `Discovery/OwnRecords.cs` | `OwnRecords` (`Seen` and `Biomes` stores), `TamesReader` (Creature Kill and Tame Counts' tames) (3.4). |
| `Details/DetailModel.cs` | `DetailView`, `DetailLine`, `DetailPart`, `RefTarget` (3.5). |
| `Details/DetailBuilder.cs` | Detail lines per kind, obfuscation, drop amounts (3.5). |
| `Details/Presentation.cs` | Spoiler-safe name, icon kind and biome name (3.5.3, 3.5.5); the generic creature sprite itself is `Ui/PawIcon.cs`. |
| `Patches/EnemyHudPatches.cs`, `Patches/PlayerPatches.cs` | Data patches (3.9). |
| `DebugCommand.cs` | Debug-only `compendium_debug` console command (3.15). |
| `SelfTests.cs` | Debug-only in-world self tests (3.15). |
| `README.md`, `CHANGELOG.md`, `TESTING.md`, `icon.png` | Per MC rules. |

Built in the UI step:

| File | Responsibility |
|---|---|
| `EscGuard.cs` | IL check of `InventoryGui.Update` (3.9). |
| `Ui/TopTabs.cs` | The Texts / Encyclopedia tabs: strip on the vanilla dialog while shown (`TopTabsController`: LT / RT, removed on hide), shared place and build for both dialogs, switching, remembered tab, raven redirect (3.6 "Nested tabs"). |
| `Ui/DialogParts.cs` | Parts of a Valheim Compendium dialog found by structure (close buttons, title, pane boxes and area, scroll views), shared by the vanilla dialog's tabs and the window build (3.6, 3.7). |
| `Ui/SideButton.cs` | Optional button: live sync with the setting, anchor, clone, tooltip, placement, self-check, pad and its gate (3.6). |
| `Ui/SideRow.cs` | The side row laid out with our button in it, baselines, exact restore, outside changes and giving up, navigation links (3.6 step 5-6). |
| `Ui/BookIcon.cs` | The button's book sprite, drawn in code (3.6). |
| `Ui/PawIcon.cs` | The generic creature sprite (a paw print), drawn in code (3.5.3, decision 15). |
| `Ui/CompendiumWindow.cs` | Open / close, Esc path, vanilla dialogs, group priority, sorting, blocker, refresh, tabs, selection, virtualized list, search, session memory (3.7-3.8). |
| `Ui/CompendiumWindow.Build.cs` | Clone of the texts dialog, strip, root components, title, tabs, search row, counter, list setup, one-time layout (3.7). |
| `Ui/CompendiumWindow.Details.cs` | Detail pane: own content, head block, pooled texts and rows, links (3.7). |
| `Ui/RowView.cs` | Row template from the crafting row and the row object (modes, icon or "?" mark, selection) (3.7). |
| `Ui/CloneUtil.cs` | The shared strip helper applied to every clone (3.6). |
| `Ui/WindowController.cs` | MonoBehaviour on the window: gamepad input, search focus tracking and delay, blocker size, safety close, first-frame dump. |
| `Ui/FocusGuard.cs` | Frame number read by the focus-guard patches. |
| `Ui/UiUtil.cs` | Rect helpers (world rect, rect in an ancestor by math, placing, overlap tests); `Trace` (UI lines at Info in Debug builds). |
| `Ui/DebugLayoutDump.cs` | Debug-only dumps (3.15). |
| `Ui/UiSelfTests.cs` | Debug-only UI self tests (3.15). |
| `Patches/InventoryGuiPatches.cs`, `TextsDialogPatches.cs`, `KeyHintsPatches.cs`, `ChatPatches.cs`, `TerminalPatches.cs`, `UIGamePadPatches.cs` | UI patches (3.9). |

At implementation also update: root `README.md`, `packaging/nexus/pages.json`, `PACK_CHANGELOG.md`,
`docs/game/exploration-player.md` (status link on "Compendium (New)", facts 1.10-1.11), `docs/game/ux.md` (side panel
facts once dumped), the MC gamepad map rows in the Loot Pickup Filter and Sort Chest designs. Done, and updated again
for the rename and the nesting: the root `README.md` row, `PACK_CHANGELOG.md`, `docs/game/exploration-player.md` (the
Status bullet and the name plate facts of 1.11), `docs/game/ux.md` (the "MC map" sentence and the Search chest risk:
View/Select on the side panel only with the optional button, LT / RT in the two dialogs), the gamepad map rows in
`ux-autopickup-filter.md` and `ux-container-sort.md`, and the MC Creature Kill and Tame Counts README / TESTING
mentions of this mod.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| New character, first open | — | Almost every row "???"; Meadows basics known after the first pickups |
| Existing late-game character, first install | Sets and kills already saved | Everything held, learnt, killed, trophied shows at once; "met" only from now |
| Item held once, then dropped | Stays in `m_knownMaterial` | Stays discovered |
| Recipe known but item never held (e.g. Club after holding Wood, recipe data unverified) | Shown in the crafting list | Discovered |
| Trader-only item never held (Megingjord, Fishing Rod, Dverger Circlet, Hildir's hats) | Sold by Haldor, Hildir or the Bog Witch | Listed as "???"; once discovered its details say "Where to get it: Sold by a trader" (the scan finds the 3 traders, 3.2.3) |
| Dungeon-only or location-only loot never held | Only loaded with the location | Listed as "???"; once discovered its details say "Not found in the world data: it may come from a trader, a location or a dungeon." (decision 5) |
| Egg (`ChickenEgg`) | Laid by a tamed Hen (`Procreation` item offspring) | "Laid by a tamed <Hen>" under "Where to get it" |
| Upgrade-only recipe (`m_noCraftOnlyUpgrade`, 28 in 1.0.16) | Never in the Craft tab; Upgrade tab for items held | "Cannot be crafted, only upgraded." plus its upgrade rows; not a source; not unlocked by the unlock modes |
| Recipe with an upgrade-station requirement (idol) | Craft: never charged; upgrader station: only that, any quality | Not in the crafting cost; "At an upgrade station:" rows per quality; "Beyond quality N: at an upgrade station only" |
| Creature attack item, `PlayerUnarmed` (creature equipment rule), `CapeTest` / `SwordCheat` / `SledgeCheat` (development rule) | In `ObjectDB` | Not listed until held (hidden until known) |
| Vanilla item no rule catches and normal play never gives | In `ObjectDB` | Stays "???" forever; listed in the Debug no-source list (open question 9) |
| `DvergerTest` | Character prefab | Not listed, not merged into the Dvergr entry |
| Baby or farm animal (Piggy, hen) | May be excluded from achievements | Listed like any creature; habitat from its parent, else "Habitat unknown" |
| Hair and beards | `Customization` items | Never listed |
| DLC item or piece without the DLC | Not craftable | Not listed |
| Seasonal recipe or piece out of season | Not learnable, not in the Craft tab even with the unlock keys | Listed only once known; the unlock modes do not discover seasonal items out of season (pieces: vanilla's build menu shows every piece in no-cost mode, so they count) |
| `AllRecipesUnlocked` / `AllPiecesUnlocked` world modifier, debug no-cost mode | Everything craftable/buildable | Every item with a recipe the Craft tab offers, and every piece, counts as discovered |
| Creature variants sharing `m_name` (`Skeleton`, `Skeleton_NoArcher`) | One kill key | One entry; drops merged |
| Summoned skeleton (faction `PlayerSpawned`) | — | Not a separate entry |
| Creature near (within 30 m) but never aimed at: behind the player, in the next cave room, or behind a wall, a tree or a rock and aimed at through it | Plate created but never shown (the aim ray stops at the first thing it hits: the wall, not the creature) | Not met |
| Creature aimed at within 30 m with nothing in between | Plate shown for 60 s | Met (vanilla showed its name) |
| Boss seen from far, not alerted | No nameplate | Not met until alerted within 100 m or killed |
| HUD hidden (Ctrl+F3) | Plates still switched on and off, root hidden | Still recorded when aimed at |
| Another player's tame walks by | Plate shown when aimed at | Recorded as met once aimed at |
| Biome found in French, game now in English | `IsBiomeKnown` false | Known if our record has it; else unknown until visited again |
| Creature drops an item the character never held | — | "??? N-M (P%)" line |
| Station never seen, recipe known item | — | "At ??? (level 2)" |
| Wood's "Used in" (hundreds) | — | Known targets A→Z, one "??? ×N not discovered yet" line |
| Item with several recipes (e.g. a 5-pack recipe) | Separate crafting rows | One block per recipe |
| Creature with no trophy | — | Generic creature icon (paw print) |
| Creature met or killed, its trophy never held | — | Paw print in the row, title and references; the trophy stays "???" in the Trophies tab and in the drops |
| Station seen before its inputs and outputs are known (Smelter before any ore) | — | "Conversions": one "??? ×N not discovered yet" line; no ore or bar name |
| Creature met before its saddle is known (Lox) | — | "Can wear: ???" |
| Station or piece known, its tool not held yet | — | "Built with ???" |
| Creature with no spawn data and no trophy biome | — | "Habitat unknown" |
| Language switch while open | Vanilla relocalizes | Names and sort order rebuilt at the next refresh (tab/search change or reopen) |
| Mod adds items after connecting (ServerSync), even while the first build runs | — | Fingerprint (counts from the build start) differs at next open → rebuilt |
| Search typed | — | Walking, hotkeys, binds and pads blocked; Esc leaves the field, next Esc closes the window |
| Esc with the window open | Would close the inventory | Closes only the window; next Esc closes the inventory |
| Tab, E or Y with the window open | Closes the inventory | Closes both |
| Vanilla dialog opened by another mod's code while ours is open | — | Ours closes |
| Optional button on; vanilla side dialog (Valheim Compendium, Skills, Trophies, Achievements) open, our button clicked or View/Select pressed | Side row locked (1.1): no side button reacts; a click on the row closes the Valheim Compendium (its full-screen close button) | Same as the vanilla side buttons: ours does not open until that dialog is closed (T03); `Open` would close it anyway (safety net, 3.7 step 2) |
| Mouse on the inventory behind the window: click, drag, shift-split, right-click eat/equip, side buttons, Loot Pickup Filter's button | Would act (the vanilla Compendium closes on a left click outside its frame) | Nothing reaches the inventory: a left press and release outside the frame (click, drag, shift-drag) closes only the window, like the vanilla dialog; a right click does nothing (full-screen close button, blocker) |
| Optional button on; View/Select pressed in the world (map on Xbox layouts) or with the chest or player grid focused | Map / Sort Chest / nothing | The window never opens; the map and Sort Chest get the press every time |
| Window closed (Esc, Tab, death...) during the first catalog build | — | The build keeps running on the plugin host; the next open shows the full list (or "Preparing entries..." until it finishes) |
| World ends during a build | — | The build stops at its next step; the new world builds its own catalog |
| Death, teleport, cutscene while open | `Hide` every frame | Window closes |
| Logout / new world while open | `InventoryGui` destroyed | Objects gone; rebuilt lazily next session |
| Mod turned off while open | — | Window and button destroyed at once |
| Mod turned off while the Valheim Compendium is open | — | Our two tabs vanish at once; the dialog stays open, exactly the game's |
| Mod turned on while the inventory is open | — | Tabs appear at once on a shown Valheim Compendium; the optional button (when on) at once |
| Raven clicked, Encyclopedia picked last this session | Valheim Compendium opens | It opens, then the Encyclopedia replaces it in the same frame; Texts is one click (or LT) away |
| Raven clicked after a logout | Valheim Compendium opens | Texts (the memory is per session) |
| Text learned (runestone) while the Encyclopedia is open, then Texts | — | The Valheim Compendium is opened the vanilla way (`OnOpenTexts`): its list is filled fresh, the new text is there |
| Another mod copies the Valheim Compendium (PraetorisClient) | Its window is a copy of the dialog | Our tabs exist on the dialog only while it is shown: the copy never has them |
| LT / RT with a controller in the Valheim Compendium or the Encyclopedia | Nothing (the dialog's pads have no key; the crafting tabs' LT / RT pads are in an inactive group) | Texts / Encyclopedia; the tab already shown: nothing |
| Optional button turned on or off with the inventory open | — | Button appears in the re-spaced row / vanishes with the row exactly the game's |
| `resetcharacter` | Recipes, stations, materials, trophies, biomes, skills, texts cleared; profile statistics and other mods' `m_customData` keys kept | Our `Seen` and `Biomes` cleared. Items "???"; biomes "???" (except the one you stand in, recorded again at the next open); met-only creatures "???"; creatures with kills (profile) or tames (Creature Kill and Tame Counts key) stay discovered; pieces placed at least once stay discovered, other pieces "???" |
| `resetknownitems` | Recipes, stations, materials, trophies cleared | Items "???"; pieces "???" unless placed at least once; creatures discovered only through their trophy "???"; killed, tamed and met creatures and biomes stay discovered |
| UI mod removed the side panel | — | Optional button on: no button, one warning; the tabs are not affected |
| UI mod changed the Valheim Compendium (no list, no text area, no room left of the title) | — | No tabs on it (one warning); a refused window build removes the tabs for the session |
| Optional button on: no room for one more control in the side-panel row (vanilla) | — | The row is laid out again: 6 controls evenly spaced inside the panel background, ours after the Valheim Compendium button; restored exactly when the mod turns off |
| Another mod keeps moving the side controls | — | Its places are kept; after the third change we give up: our layout undone, our button after the last control, one warning; the controls it moved are never pulled back later, also not when the mod turns off |
| Row laid out, then another mod hides the Valheim Compendium button (or adds controls until the row no longer fits at 50 % scale) | — | At the next `Show` the row layout is dropped: the controls we still hold back on their places, both D-pad links back to vanilla, ours `Automatic` and after the last control; laid out again when the button is back |
| Controller user, side panel focused, optional button on | — | View/Select opens; the D-pad walks the row in its visible order (ours linked between the Valheim Compendium button and Skills) |
| Detail building throws (another mod's tooltip patch) | — | Title + "Details unavailable (see the log)."; one error in the log; the window keeps working |
| Crafting Search and Sort field focused, the Encyclopedia opened (tab or optional button) | — | Its field is released; our window opens |
| Newer `Seen` format in the save | — | Read what can be read, never written, one warning |
| Details of an item whose skill the character never had (a Fishing Rod on a new character) | Hovering the real item adds that skill at level 0 (`Skills.GetSkill`) | Reading the details adds nothing: the level-0 entries `GetTooltip` added are removed at once (3.5.1) |

---

## 5. Decisions and open questions

### Decisions

The ones marked **(to confirm with the user)** change what the player sees and have a simple alternative.

1. **Name "Encyclopedia", nested under the raven** (user at the pause: "rename it to encyclopedia or nest it under the
   raven compendium if possible"; lead decision: do both). Display name `ModName` "Encyclopedia", window title, top tab,
   optional button tooltip (`Labels.WindowTitle`, `TabEncyclopedia`, `ButtonTooltipTopic`); the vanilla "Valheim
   Compendium" is never renamed. The earlier pick "Compendium" put a second "Compendium" next to the game's; with the
   nesting the two are "Valheim Compendium" (the dialog, raven) and its tabs "Texts" / "Encyclopedia". GUID
   `MC.Exploration.Compendium.Encyclopedia` and package `CompendiumEncyclopedia` unchanged (they already carried the
   word; permanent at the first release). Internal names keep "Compendium" (decision 29).
2. **Tabs in the vanilla Compendium, the window in its place** (user at the pause; the first version was a new button
   and a separate window, because the user first asked for "a new element next to the vanilla ones"): the Valheim
   Compendium dialog gets a Texts / Encyclopedia tab pair and our window, a clone of that dialog, takes its place when
   Encyclopedia is picked (3.6 "Nested tabs"). Not a page inside `TextsDialog` itself (the backlog sketch): our window
   needs its own list, detail pane, tabs, search and input, and the vanilla dialog then stays exactly the game's for
   every other mod that patches it (Jewelcrafting, EquipmentAndQuickSlots, Creature Kill and Tame Counts). The side
   button stays as an opt-in (decision 58).3. **Scope: items, build pieces (stations included) and creatures** (to confirm with the user). Biomes, locations,
   lore texts, tutorials and status effects are not entries in 0.1.0 (biomes appear as details). Small extra: health
   and damage modifiers of a creature after the first kill (a first "knowledge tier", backlog idea).
4. **Discovery follows vanilla records** (to confirm with the user): item = held once or recipe known; piece = known
   buildable, station seen, or placed; creature = killed, trophy owned, tamed (with Creature Kill and Tame Counts), or
   **met** = the game **showed** its name plate to the player (1.11): for an ordinary creature, after the player aimed
   at it within 30 m (the plate then stays 60 s); for a boss, when it is alerted within 100 m. It is read from vanilla's
   own result (`UpdateHuds` postfix: plates whose object is active), so it follows the prefab values and never counts a
   creature the player could not see. The first version recorded the plate's *creation*, which happens for every
   character within range, seen or not (behind the player, behind a wall, a Leech under water): creatures the player
   never saw became discovered (review finding, fixed). Other options weighed: a direct interaction (hit it or was hit
   by it) adds little over kills and misses peaceful creatures; the plate shown is the one moment vanilla itself tells
   the player the creature's name. Everything except "met" is retroactive; "met" counts from install. Vanilla never
   records "seen creature"; a stricter rule (kill only, like Almanac) would hide peaceful creatures for a long time.
5. **Hidden until known: narrow, positive rules** (to confirm with the user). Only three kinds of items are listed
   once discovered, without a "???" row: creature equipment (items that only non-player creatures carry, plus unarmed
   weapons such as `PlayerUnarmed`), development items (prefab name ending in `Test` or `Cheat`), and seasonal items
   and pieces (learnable only in season). Test creatures (prefab name ending in `Test`) are never listed. Every other
   item and creature is listed, as "???" until discovered. That includes dungeon and location loot the mod cannot
   find a source for in the loaded data (locations are not loaded up front; trader goods do have a source: the scan
   finds the 3 traders, 3.2.3), and babies, hens and other creatures the game excludes from its achievements.
   Cost: a few vanilla items that normal play never gives may stay "???" forever; the Debug log lists every no-source
   item so a new rule can be added (open question 9). Rejected alternatives: hiding every item with no source (it hid
   the Megingjord, the Fishing Rod, the Dverger Circlet, Hildir's clothes and the Bog Witch's goods), and a
   hand-maintained exclusion list.
6. **Order**: sub-groups, then discovered A→Z, then undiscovered in game order. An alphabetical place would reveal the
   first letter of a hidden name.
7. **Obfuscated details keep numbers** (amounts, chances, station levels) and collapse unknown targets of long lists
   ("Used in", "Recipes at this station", a station's conversions) into one "??? ×N not discovered yet" line (to
   confirm with the user). Short blocks show each unknown target as its own "???" line. Every name of another entry in
   any detail line (tools, saddles, conversion inputs and outputs, producers, bosses) goes through the same knowledge
   check; no detail line formats a name as plain text.
8. **Kill counts read directly from the game** (`m_playerStats[0].m_enemyStats[0]`), no dependency on Creature Kill
   and Tame Counts; its per-creature tames are read softly from its documented `m_customData` key when present. A
   `ModRequires` would make the Encyclopedia inactive without it for one extra line.
9. **Habitat** = world spawn lists, alternate-biome spawns, nests and spawners, offspring; fallback: the trophy's biome
   (copied curated table); raids ignored.
10. **Drop amounts as the game rolls them** at normal world settings and 0 stars: `min` to `max - 1` when `max > min`
    (Unity's integer `Random.Range`), which can differ from wiki numbers (to confirm with the user).
11. **Modal window, for the mouse and the controller**: its own top-priority gamepad group turns the inventory groups
    off, and a full-screen transparent raycast blocker on a canvas sorted above the inventory stops every click behind
    it (a priority group alone does not stop the grids' pointer handlers). The vanilla dialogs are only gamepad-modal;
    ours is stricter so that no drag, eat, equip or split can happen unseen behind it. Esc/B close only the window;
    Tab/E/Y close everything (vanilla dialog behaviour). A left click outside the frame closes the window, as it closes
    the vanilla Compendium (the clone's full-screen close button kept, decision 41).
12. **Gamepad scope** (revised at the pause): LT / RT pick the Texts / Encyclopedia top tab in both dialogs (decision
    56). With the optional button on: open with View/Select only while the inventory is visible and the side panel
    group is focused (and with the D-pad when Unity's automatic navigation reaches the button); the pad is tied to
    `m_uiGroups[2]` and rejected in `ButtonPressed` otherwise, so View/Select on the chest grid stays Sort Chest's and
    View/Select in the world stays the map; off (default), View/Select on the side panel does nothing of ours. In the
    window: move with D-pad/left stick; LB/RB change category tabs; right stick scrolls the details; B closes. Search
    and reference links are mouse/keyboard only.
13. **English labels** for the mod's own words; vanilla tokens where they exist (tab names Materials, Building,
    Trophies, damage words, biome names). Translations later.
14. **Lazy build over several frames** on the first open of a world session, rebuilt when the fingerprint changes;
    knowledge read on every open, never cached.
15. **"?" mark, never the real sprite** for undiscovered entries (a silhouette would leak the shape). A discovered
    creature shows its trophy's sprite only when that trophy is discovered too; otherwise, and for creatures without a
    trophy, a **paw print drawn in code** (`Ui/PawIcon.cs`: a main pad and four toes, light greys with a darker
    outline, 64×64, like `BookIcon`, so any image colour or tint works; built once, destroyed on deactivation), in the
    list row, the detail head and references. The first version used the map's random-event pin
    (`Minimap.GetSprite(PinType.RandomEvent)`): the 1.0.16 screenshots showed a **red exclamation mark**, which read like
    a warning (lead decision: replace it with a neutral icon).
16. **Button icon drawn in code** (open book), no asset file; drawn in light greys so the button's gold tint colours it
    like the vanilla icons.
17. **Own biome record**: vanilla stores biome names in the game language, so a language switch would forget biomes;
    the mod records `Heightmap.Biome` flags from install and also matches vanilla's names for older discoveries.
18. **Session-only UI memory** (last tab, selection, search); nothing saved.
19. **Esc handling by resetting `m_shownFrames`** (not by skipping vanilla `Update`, which would also skip other mods'
    transpiled code, nor by faking chat focus, which blocks more keys). If a game update changes that code, a warning
    is logged and Esc closes the inventory too; the rest of the mod keeps working.
20. **Copy, don't share, Sort Chest's trophy-biome rows** (~70 names) and its scan approach: `src/Shared` stays tiny
    and Sort Chest is not touched (its T14 index test is still pending).
21. **Resets**: `resetcharacter` also clears our records; `resetknownitems` keeps them. Neither reset touches the
    profile statistics or other mods' keys, so killed or tamed creatures and placed pieces stay discovered after both
    (section 4). The mod does not try to hide them (that would need a new record of "forgotten" entries).
22. **The catalog build runs on the plugin's own object**, not on the window: closing the window (which deactivates
    it) must not kill the build. The build fills a new catalog that is swapped in only when complete; a stalled or
    orphaned build is restarted at the next open; a build from an ended world stops by itself.
23. **One strip helper for every clone** (button, dialog, tabs, rows, detail lines, search field): no cloned pad, hint,
    tooltip, `Localize` or prefab click handler survives unless re-set on purpose, and navigation is set explicitly.
24. **"Appears after defeating <boss>" only when the boss is really needed**: shown when every world spawn entry of
    the creature requires a boss's defeat key and it has no nest or spawner habitat. The design showed it for any
    spawn entry with such a key, which would claim a boss for creatures that also spawn without one.
25. **Creature discovered by its trophy item** (the trophy entry's own rule: `m_trophies` or held), not only by
    `m_trophies`: same result in vanilla (`AddKnownItem` fills both), and it keeps the creature and its trophy icon
    rule consistent.
26. **"Where to get it"** (design: "Obtained from") is shown only when the item has a source other than a recipe, or
    no source at all (then the "Not found in the world data" text); a crafted-only item shows its crafting block
    only. "Made from <item> at <station>" drops " at <station>" when the converting prefab is not a listed piece, and
    a producer that is not a listed piece reads "Produced by a building" (instead of a permanent "???").
27. **Sub-group labels** (English, `Labels`): "Helmets", "Chest armor", "Leg armor", "Hand armor", "Capes",
    "Utility items", "Trinkets", "Building tools", "Gathering tools", "Torches", "Misc", "Other", "Other weapons",
    "Meads and potions", "Fish", "Habitat unknown"; vanilla tokens for weapon families (`$skill_*`), Food and
    Materials. Building headers are "<tool> · <category>" with the tool name through the knowledge check.
28. **Data API for the UI step**: the list and detail models carry already-resolved names and icons (`ListRow`,
    `RefTarget`, `DetailView`), so the window never touches an entry's real name or sprite; `Presentation` is the only
    other way. A Debug-only console command `compendium_debug` and three in-world self tests check the data layer
    without the UI.
29. **Internal names keep "Compendium"** (decision 1): classes `CompendiumWindow`, `SideButton`, `RowView`,
    `BookIcon`, `TopTabs`; objects `MC_Compendium_Window`, `_Button`, `_Blocker`, `_Templates`, `_Tab<N>`, `_TopTabs`,
    `_TopTab_Texts`, `_TopTab_Encyclopedia`, `_Search`, `_Counter`, `_Detail`, `_Head`, `_Row`; the root namespace
    `MC.Exploration.CompendiumEncyclopediaMod` (from the GUID); the self tests `compendium.*` and the Debug console
    command `compendium_debug`. Nobody sees them; renaming them would only churn the code and the test history. Log
    lines and warnings, which players and testers read, say "Encyclopedia".
30. **No nested list canvas** (the first design wanted the list content under its own `Canvas` + `GraphicRaycaster`
    with a `RectMask2D`): the window's own sorted canvas already keeps list redraws off the inventory canvas; a nested
    canvas that overrides sorting would cut the scroll view's mask chain, and one that does not adds nothing. The rows
    are clipped by the cloned scroll view's own mask.
31. **Layout measured at the first open**, right after the window becomes active (the first design: at creation):
    the rects of a hierarchy that never ran may be stale. Everything is measured in the window's own space by math,
    so the inventory's open animation does not matter.
32. **Focus group priority** = 1 + the highest priority of every group under the inventory **and** of every active
    group in the scene, recomputed at each open (the first design: groups under the inventory only): a higher,
    always-active group elsewhere would otherwise leave our window non-interactable.
33. **Rows laid out by the mod**: icon at the left, the "?" mark over it, the name after it, whatever the crafting
    row's inner layout; header rows turn the row's background and button off.
34. **Detail pane rendering**: a head block (big icon or "?" mark, plus the subtitle) under the topic; a Row line
    with neither an icon reference nor a link is shown as wrapped text instead of a one-line row (long lines wrap
    instead of being cut); a link row opens the first clickable reference of its line.
35. **Search and tabs**: while a search is shown every tab is clickable, and a tab click (or LB/RB) leaves the search;
    each tab and the search keep their own selected entry; the session memory survives a live toggle (only a new
    `InventoryGui` resets it).
36. **Safety nets**: the `WindowController` closes the window when the inventory's Animator flag drops by a path that
    skipped our `Hide` postfix, and re-sizes the blocker every frame while open (screen or scale changes).
37. **Side button details**: a text label is renamed only when it reads the vanilla dialog name; a sprite swap on the
    icon image becomes a colour tint (the vanilla sprites would show on hover); no pad when a pad of the side panel
    group already uses View/Select (Warning, mouse only); the placement line = the direct children of the anchor's
    parent that hold a `Selectable`, cross position = the line's median; the self-check also avoids the player,
    crafting and container panels.
38. **Esc path in the self test**: `compendium.ui` calls `CloseFromKey` (what the `Update` prefix runs after its key
    gate) because a key press cannot be faked, and checks what makes the real key safe: our prefix is on
    `InventoryGui.Update` and `CloseFromKey` leaves `m_shownFrames` at 0 (with the activation IL check). The key gate
    itself is covered by code review and T04. (The first version checked that the inventory stayed shown, which could
    never fail without a key press.)
39. **Our button in the vanilla row, the row laid out again** (user request after the first in-world run: the first
    placement "one step before the first control" landed on the panel's left border, outside its background): while
    the feature is active the vanilla side controls move (6 evenly spaced controls inside the panel background, same
    sizes, step 87.8 instead of 100 units in 1.0.16), ours **right after the Valheim Compendium button** (it belongs
    next to the dialog it complements; PvP, a toggle, stays last as in vanilla). Every vanilla control goes back to its
    exact baseline (position, scale, size, navigation) when the feature turns off or the button hides. Controls are
    scaled only when a tight step does not fit (never in vanilla). Rejected: keeping the vanilla step and letting the
    row spill over the panel border; shrinking the icons; a second row (the panel has no room under the row).
40. **Never fight another mod over the row**: a control moved by someone else keeps its new place (it becomes its
    baseline; we cannot know whether the other mod set an absolute place or nudged ours, and absolute layouts are the
    common case); at the third outside change in one session we give up for that session (our changes undone, our
    button after the last control, one Warning). Re-applying at every `Show` without a limit would ping-pong with a mod
    that re-applies its own layout at every `Show`.
41. **Click outside the frame closes the window**, as in the vanilla Compendium (its full-screen close button is part
    of the clone and now calls our `Close`): the window stays modal (nothing reaches the inventory), and the vanilla
    look and feel is kept. The first design had every outside click do nothing (the blocker alone).
42. **Scroll views found by structure**: `TextsDialog.m_leftScrollRect` points at the text scroll view in 1.0.16 (1.4
    trap), which made the first build refuse the window. The list view is the `ScrollRect` above `m_listRoot`, the
    detail view the one above `m_textArea`; their own bars first, the dialog's bar fields as fallbacks.
43. **Pane boxes move, not the scroll views**: the tabs band and the search row push the vanilla pane boxes
    (`TextList`, `TextArea`, with their dark backgrounds) down, so no background shows behind the tabs; the search row
    sits inside the left box. The topic moves into our detail content (it lived in the old content's layout group).
44. **UI trace lines at Info in Debug builds**, Debug in Release (3.15): the in-world harness reads the default log
    file, which drops Debug lines.
45. **Detail rows use the paragraph text size**: the crafting row's own text (auto-size up to 16) looked small next
    to the 20-unit paragraphs of the same pane. List rows keep the crafting list size.
46. **Crafting cost exactly as the Craft tab charges it** (review, lead decision): upgrade-station requirements
    (`m_upgraderResource`, idols) never appear in the quality-1 cost, only in the upgrade rows, and a recipe that has
    one ends with "Beyond quality N: at an upgrade station only" (no list: the upgrader has no upper end). The first
    version printed "At an upgrade station: <idol> ×m_amount" under every refinable item's crafting cost, a cost
    nobody pays.
47. **Upgrade-only recipes are never shown as craftable** (review, lead decision): a recipe with
    `m_noCraftOnlyUpgrade` (28 in 1.0.16; the Craft tab never lists it) gets "Cannot be crafted, only upgraded." and its
    upgrade rows, no crafting head; it is not a source (an item with only such recipes and nothing else reads "Not found
    in the world data"), it is skipped in "Recipes at this station", and the unlock modes do not discover its item.
48. **Unlock modes follow the Craft tab**: with "all recipes unlocked" or no-cost, an item counts as discovered only
    when the Craft tab would offer one of its recipes (enabled or in the current season, not upgrade-only), like
    `Player.GetAvailableRecipes` and `InventoryGui.UpdateRecipeList`; pieces keep "every piece" (vanilla's build menu
    shows every piece in those modes). The first version also revealed the two seasonal-only items out of season.
49. **Eggs: "Laid by a tamed <creature>"**: an item a creature's `Procreation` gives birth to (vanilla supports item
    offspring; 1.0.16: Hen → `ChickenEgg`) is a source; the first version called the egg "Not found in the world
    data". The creature goes through the knowledge check like every other reference.
50. **Reading details changes nothing**: `ItemDrop.ItemData.GetTooltip` adds level-0 skills to the character through
    `Skills.GetSkill`; the details builder removes the entries the call added (still level 0, accumulator 0) in a
    `finally`, and `compendium.details` checks the skill list is unchanged (coordinator request).
51. **Self tests never depend on the user's settings**: the window reads the display settings through
    `Plugin.ShowUndiscoveredOn` / `RevealAllOn`, which a Debug-only in-memory override (`Plugin.TestDisplay`) can
    force; no test writes a `ConfigEntry` value for this (lead decision).
52. **Catalog fingerprint from the build start** (3.2.2): data added while a build runs makes the next open build
    again, instead of a stale catalog for the session.
53. **Our side button follows the vanilla side-row lock** (second review, lead decision): while a vanilla side dialog
    is open, the game makes the whole side row non-interactable and the Valheim Compendium's click-outside button
    covers it (1.1), so our button cannot open ours over a vanilla dialog, exactly like the vanilla side buttons. The
    code is kept as it is (no `ignoreParentGroups` canvas group to break out of the lock); goal 2, T03 and the README
    describe that behaviour. Closing the vanilla dialogs in `Open` stays as a safety net (3.7 step 2).
54. **D-pad links restored whenever the row layout is dropped** (second review): not only when giving up, also when a
    later `Apply` fails (anchor gone, nothing else in the row, no room), and our button's navigation goes back to
    `Automatic` (3.6 step 6).
55. **Upgrade-station items stay out of the quality-1 cost, now verified**: the only vanilla upgrader
    (`UpgradeStation`, "Forge of Potential") has no Craft tab (`m_hasCraftTab` false, 1.0.16 NOTE), so no quality-1
    cost made of upgrade-station items exists in vanilla; no "alternative cost" line is added (3.5.1).
56. **LT / RT switch the top tabs** (pause review, lead decision): LT = Texts, RT = Encyclopedia, in the Valheim
    Compendium and in our window, like the inventory's only other tab pair (the crafting panel's Craft / Upgrade on LT /
    RT). A direct pick, not a toggle, so both dialogs reading the same press in the switch frame change nothing more.
    Checked free (1.4, the Debug dump of the dialog's pads): the vanilla dialog's three pads have no key,
    `TextsDialog` reads only the D-pad and the sticks, B closes it in `InventoryGui.Update`; the crafting tabs' LT / RT
    pads and the grids' LT / RT modifiers belong to inventory groups that are inactive under a priority-2 dialog and
    under our window; LB/RB are our category tabs; View/Select, L3 and R3 are Sort Chest's and Loot Pickup Filter's on
    their grids. Rejected: View/Select (the map's button in the world, Sort Chest's on the chest grid: one button with
    three meanings in the same screen), X/Y (Y closes the inventory in vanilla), LB/RB (our category tabs), D-pad
    left/right (easy to hit while moving up and down the lists with the D-pad). No glyph on the tabs: the vanilla hint objects are
    stripped with the pads (decision 23) and the game hides its key hints while a side dialog is open; README and
    TESTING document the keys.
57. **The raven reopens the last top tab, for the session only** (pause review, lead preference): picked with the tabs
    (click or LT/RT), kept until logout (a new `InventoryGui`), never saved; the `OnOpenTexts` postfix shows our window
    at once when Encyclopedia was picked last. The optional side button does not change it (players who use both
    get the raven for texts and the button for the Encyclopedia). After a logout the raven opens Texts, as in vanilla.
58. **The side button is an opt-in** (`Display.SideButton`, default off; pause review): the default leaves the side
    panel exactly the game's (no re-spaced row), so the Encyclopedia is reached only through the Valheim Compendium.
    Live: turning it on adds the button (placed at once when the inventory is shown), off removes it and puts the row
    back exactly (`SideButton.Sync`). The self tests force it in memory (`Plugin.TestSideButton`), never in the
    `.cfg` (decision 51).
59. **Top tabs on the title line, left of the title** (pause review): the only free spot in both dialogs (1.4); the
    window's counter keeps the right part, its category tabs stay under the line. Same measure and the same clone
    source in both dialogs, so the tabs do not move when switching. Rejected: above the wood frame (outside the dialog,
    over the dimmed screen, where the `button_tab` sprite has nothing to sit on), inside the pane area (would push the
    vanilla dialog's panes: the vanilla dialog must stay untouched).
60. **The vanilla dialog is never changed**: our strip is added as its last child while it is shown and destroyed when
    it hides or the feature goes off; nothing of it moves or is renamed, so closed it is exactly the game's, a copy by
    another mod never carries our tabs, and `compendium.toggle` compares it object by object.
61. **Switching goes through the vanilla paths**: Encyclopedia closes the dialog with `TextsDialog.OnClose` (other
    mods clean up there) before our window shows; Texts calls `InventoryGui.OnOpenTexts` (group, sound, fresh list).
62. **Label "Texts"** (plain English): no vanilla token fits (`$inventory_texts` is the dialog's own title "Valheim
    Compendium", `$inventory_logs` "Message log" is one of its entries). Mixed case, like our category tabs; the
    vanilla Craft tab's own label is upper case ("CRAFT"), open question 12.
63. **Live toggle with the vanilla dialog open**: off removes the tabs at once and leaves the dialog open (the player
    was reading it); on adds them at once (`TopTabs.OnActivated`).

### Added beyond the request

Search field (all tabs, discovered names only); "Discovered N / M" total; "Upgrade for the <station>" on station
extensions and "Produces <item>" on beehives and sap collectors; clickable references that open the target
entry; settings `ShowUndiscovered` and `RevealAll`; creature health and damage modifiers after the first kill; "Your
records" lines (picked up, crafted, eaten, placed); View/Select shortcut (optional button); controller key hints hidden while open;
"Laid by a tamed <creature>" for eggs and "Beyond quality N: at an upgrade station only" (review fixes).

### Open questions

1. ~~Final display name (decision 1).~~ Resolved at the pause: Encyclopedia, nested under the Valheim Compendium.
2. Later tabs: Biomes / Atlas (discovered locations, `m_shownTutorials` location labels), Lore (known texts),
   Tutorials (re-read raven tips) — the rest of the backlog idea.
3. ~~Traders: capture a trader's stock when its shop opens?~~ Resolved: the scan finds the 3 vanilla traders, their
   goods read "Sold by a trader". Naming the trader ("Sold by Hildir") stays a possible extra.
4. A keyboard shortcut or console command to open the window without the Valheim Compendium (players of AugaLite /
   Veneer, whose dialogs get no tabs).
5. Knowledge tiers beyond the first kill (e.g. drop rates only after N kills)?
6. Translations of the mod's English labels.
7. `m_secretAchievementIcon` as the "?" visual instead of a TMP glyph, if the dump shows a suitable sprite.
8. If X02 shows that Crafting Search and Sort's F key focuses its field behind our window, add a check in that mod.
9. After the first run, review the Debug list of items with no source found (T05): if some vanilla items that normal
   play never gives show up there as permanent "???" rows, add a rule for them (a rule, not a name list, when a
   pattern exists).
10. Modality (decision 11): keep the window modal for the mouse, or let the inventory behind it take clicks like the
    vanilla dialogs do (then Esc must also handle a split dialog opened underneath)?
11. Canvas order: the window sorts at the root canvas order + 1 (1.0.16: inventory root canvas 600, ours 601). The
    Debug dump shows the console (5000), the menu (1700) and the HUD messages (`HudMessage` 1000: "New item", "Trophy
    collected", "Day N") as their own root canvases, so they draw above the open window, as they do above the vanilla
    dialog (seen in the self-test screenshots: the top-left "New item" messages cover the top of the list and the top
    tabs for a few seconds); the chat (300) and the HUD (400) draw under it. Keep as is?
12. Top tab labels in upper case ("TEXTS" / "ENCYCLOPEDIA"), like the vanilla Craft / Upgrade tabs, or mixed case like
    our category tabs (current)?

### Unverified names and values

- Verified from the Debug dumps of 1.0.16 at 2560x1440 (no longer open): the side panel (1.1: `root/Info`, no layout
  group, 5 controls, `Bkg`, explicit navigation, `OnOpenTexts` on `Texts`, its `Icon` child and `UITooltip`);
  the Valheim Compendium dialog (1.4: tree, title "Valheim Compendium" in `topic`, two `OnClose` buttons, the
  `m_leftScrollRect` trap, no mask on its root, the free title line left of the title, its three `UIGamePad`s with
  no key; our two tabs on it and in our window on the same rects, no `OVERLAP:` / `OUTSIDE:` line);
  `m_recipeElementPrefab` (`bkg`, `selected`, `icon`, `name`,
  `Durability`, `QualityLevel`, a `Button`, no `UIGamePad`) and `m_tabCraft` (TMP label "CRAFT", a `UIGamePad` and a
  `gamepad_hint`, both stripped); the root canvas `Inventory_screen` (screen-space overlay, order 600, scale 1.333);
  8 tabs fit in one row (143.5 units each). Still unverified: other resolutions and UI scales (T02, T28, T35), and the
  side panel and the dialog at 1920x1080 (same units, expected identical); the controller keys by hand (T37: the
  self tests call what LT / RT run, a trigger press cannot be faked).
- Prefab data: which variants share `m_name`, whether dish pieces carry an `ItemDrop`, whether `OfferingBowl` is in
  `ZNetScene`, that `Abomination_attack1` and `bjorn_bite` (both in `manifest_extended`) are referenced by creature
  equipment, whether `DvergerTest` is registered in `ZNetScene` and which `m_name` it uses, which items end up hidden
  until known (Debug list, T05), which items an upgrade station takes (T10), that the Smelter needs a Workbench
  (T13), that the Fishing Rod uses the Fishing skill (T34). Verified in the 1.0.16 in-world run (`NOTE` lines): 3
  `Trader` components found (trader goods have a source); the Hen's offspring is the item `ChickenEgg`; 28 recipes
  are upgrade-only; 225 items list upgrade-station costs; the only upgrader station is `UpgradeStation` ("Forge of
  Potential", `$piece_upgradestation`) and it has no Craft tab (`m_hasCraftTab` false; 225 recipes with an
  upgrade-station requirement, all with a quality-1 amount); 17 listed items have no source found (`compendium.catalog`
  names them); `EnemyHud` makes plates within 30 m (code default 10), bosses 100 m, and shows a creature's plate 60 s
  after the crosshair.
- Counts and build time (logged, T20); that root-level `TryGetComponent` finds the source components of networked
  prefabs (Debug counts per component type).
- Whether a non-interactable `CanvasGroup` stops Crafting Search and Sort's field from taking focus (X02).
- English names used in tests exist in the 1.0.16 table (`$enemy_boar` Boar, `$enemy_greyling` Greyling,
  `$enemy_neck` Neck, `$enemy_deer` Deer, `$item_raspberries` Raspberries, `$item_wood` Wood, `$item_sword_bronze`
  Bronze Sword, `$item_trophy_boar` Boar Trophy, `$item_club` Club, `$piece_workbench` Workbench, `$piece_forge`
  Forge; checked in this revision: `$item_beltstrength` Megingjord, `$item_fishingrod` Fishing Rod,
  `$item_helmet_dverger` Dverger Circlet, `$item_helmet_hat1` Blue Tied Headscarf, `$enemy_boarpiggy` Piggy,
  `$enemy_hen` Hen, `$enemy_lox` Lox, `$item_saddlelox` Lox Saddle, `$item_trophy_lox` Lox Trophy, `$piece_smelter`
  Smelter, `$item_copperore` Copper Ore, `$item_copper` Copper, `$item_ironscrap` Scrap Iron, `$item_iron` Iron); the
  prefabs `Boar`, `Greyling`, `Neck`, `Deer`, `Raspberry`, `Wood`, `Club`, `SwordBronze`, `TrophyBoar`, `TrophyDeer`,
  `piece_workbench`, `forge`, `Hammer`, `BeltStrength`, `FishingRod`, `HelmetDverger`, `HelmetHat1`, `Boar_piggy`,
  `Hen`, `Lox`, `SaddleLox`, `smelter`, `CopperOre` exist in `manifest_extended`; which token each prefab uses, the
  recipes used in T07/T10 (Club from Wood only), the Smelter's conversions and that the Lox's `m_saddleItem` is
  `SaddleLox` are prefab data.

---

## 6. Tests (outline for `TESTING.md`)

Setup: a new character in a new world (for T05-T08, T13, T25, T32-T34) and a mid-game character (T09); `devcommands` for
spawning (`spawn Boar`, `spawn Lox`, `spawn TrophyDeer`, `spawn Hammer`, `spawn Stone`, `spawn SurtlingCore`,
`spawn CopperOre`, `spawn BeltStrength`), Debug build and `./tools/Setup.ps1 -DevBepInExConfig` for the Debug dump.
Every goal maps to at least one test: G1 T35 and T38 (optional button: T01-T02, T30-T31), G2 T03-T04, T25-T27 and
T35-T37, G3 T05, G4 T06, G5 T07-T09 and T32,
G6 T10-T12 and T33, G7 T13, G8 T14, G9 T22-T23. Two parts of G2 have no in-game path in vanilla and are covered by code
review only: a vanilla dialog opened by another mod's code while ours is open closes ours (the `OnOpen*` postfixes),
and opening ours closes an open vanilla side dialog (`CloseVanillaDialogs`: the side row is locked while one is open,
1.1; T03 checks that lock instead). G3-G5, G7 and G8 are also covered without the UI by the in-world self tests (T00,
3.15); G1 (tabs, optional button), G2 (tabs both ways, same place, remembered tab, LT / RT paths, modal, Esc path, a
window closed during the catalog build), G4-G5 in the window, G8 in the window and G9 (tabs and window, vanilla dialog
exactly as before) by the UI self tests (`compendium.ui`, `compendium.toggle`, `compendium.siderow`).

**Single player**

- T00 Automated in-world self tests (`./tools/Test-InWorld.ps1 -Mod Compendium -Only compendium.`):
  `compendium.catalog`, `compendium.details`, `compendium.knowledge`, `compendium.ui`, `compendium.toggle`,
  `compendium.siderow` all PASS; their NOTE lines go in the test notes and the screenshots are looked at.
- T01 Optional button (`SideButton = true`) in the vanilla side row right after the Valheim Compendium button, own
  icon (gold like the others), tooltip "Encyclopedia"; the row evenly spaced inside the panel; Debug dumps logged (G1).
- T02 Optional button placement at 1920x1080 and 2560x1440, with and without extra inventory rows: inside the panel background, no
  overlap, evenly spaced, on screen (G1).
- T03 The Encyclopedia tab opens the window in the vanilla style, in the dialog's place, title "Encyclopedia". Optional
  button on: while a vanilla side dialog is open the side row is locked as in vanilla (1.1): with the Valheim Compendium open, a click on our button's spot only closes the
  Valheim Compendium (its click-outside button); with Skills, Trophies or Achievements open, our button does what a
  vanilla side button does there and never opens ours on top; View/Select does nothing; once the dialog is closed, one
  click opens ours (G2). (Closing a vanilla side dialog when ours opens, and the reverse direction: code review only,
  see above.)
- T04 Close paths: Esc/B close only the window then the inventory; the "Close" button; a click outside the frame;
  Tab/E/Y close both; death and teleport close it (G2).
- T05 Catalog: every tab filled; Info counts; stations and upgrades under Building. On a new character, trader goods
  are listed as "???" rows: Megingjord, Fishing Rod, Dverger Circlet, Blue Tied Headscarf (count the "???" rows of
  their sub-groups, or check with `RevealAll`); Piggy and Hen are listed as creature rows. Not listed:
  `Abomination_attack1`, `bjorn_bite`, `PlayerUnarmed`, `CapeTest`, `SwordCheat`, `SledgeCheat`, `DvergerTest`, hair
  and beards. `spawn BeltStrength` and pick it up: Megingjord is discovered and its details show "Sold by a trader"
  (the scan finds the 3 traders). Both Debug lists (hidden until known, no source found) reviewed and pasted in the
  test notes (G3).
- T06 New character: undiscovered rows "?" + "???"; no real name or icon in rows, details, tooltips; unknown rows
  after known ones, not alphabetical; the undiscovered detail text. `spawn Boar`, turn away and stay near it without
  ever aiming at it: still "???". Then aim at it (its plate shows) without killing it: the Boar row and title show
  the paw print, not the Boar Trophy sprite; the Boar Trophy stays "???" in the Trophies tab and in the Boar's drops
  (G4).
- T07 Item discovery: pick up Raspberries → discovered at next open; Club discovered by its recipe after holding
  Wood, before ever holding a club (the Club recipe needing only Wood is recipe data, **unverified**) (G5).
- T08 Piece and creature discovery: Workbench after carrying a Hammer and Wood; Forge after seeing one; Greyling met
  (aimed at within 30 m) without a kill; a creature aimed at through a wall, a tree or a rock stays "???" (1.11); a
  Neck by a bow kill from beyond 30 m, never aimed at closer (the kill alone discovers it; the Boar of T06 is already
  met); Deer by holding `TrophyDeer` only; a boss met without aiming (`spawn Eikthyr`, look away, let it notice you:
  its health bar shows, it is discovered with "Killed: 0") (G5).
- T09 Retroactive: the mid-game character's first open shows its materials, recipes, trophies and killed creatures
  (G5).
- T10 Item details: stats text, recipe with station and level and exactly the cost the crafting panel shows (no
  upgrade-station row there), upgrade costs per quality as the Upgrade tab shows them (upgrade-station items only in
  the quality rows, then "Beyond quality N: at an upgrade station only"), dropped by, gathered in a biome, used in,
  your records (G6).
- T11 Piece details: build cost, required station, comfort; Forge: upgrades and recipes at this station (G6).
- T12 Creature details: habitat, "appears after defeating", drops with amounts and chance, taming food; health and
  damage modifiers only after a kill (G6).
- T13 Obfuscated details: an unknown drop, an unknown station, an unknown biome and an unknown boss show "???" with
  numbers kept; "Used in" collapses unknowns. New character: build a Workbench and stand next to it (vanilla
  `HaveRequirements(piece, IsKnown)` needs the piece's station in `m_knownStations`), hold a Hammer, Stone and a
  Surtling Core (the Smelter's cost and station, prefab data **unverified**) so the Smelter becomes buildable, without
  holding any ore or bar: its details show one collapsed "??? ×N not discovered yet" conversions line and no ore or
  bar name; pick up Copper Ore: "Turns Copper Ore into ???" appears. `spawn Lox` and aim at it (met, not killed): its
  details say "Can wear: ???" and never name the saddle (G7).
- T14 Kill count: on a new character, kill three Boars: "Killed 3", +1 after a kill and reopen; "Killed 0" for a
  met-only creature; the same number as Creature Kill and Tame Counts' line when installed (the vanilla Player
  Statistics page lists no per-creature kills, `TextsDialog.AddStats`) (G8).
- T15 Gamepad: D-pad/stick move with rumble; LB/RB category tabs; right stick scrolls;
  B closes; inventory groups quiet while open; no button glyph shows on tabs or rows, and no face button clicks a
  tab; the key hints are hidden while the window is open and back once it closes (`KeyHints.UpdateHints` prefix);
  optional button on: View/Select opens from the side panel, and with the player grid or the crafting panel focused
  does nothing of ours.
- T16 Search (extra): typing never walks or closes; Esc leaves the field first; matches across tabs, discovered only.
- T17 Reference links (extra): clicking a known ingredient opens its entry; "???" links do nothing.
- T18 Settings (extra): `ShowUndiscovered = false` lists only discovered; `RevealAll = true` shows everything; both
  live.
- T19 Language switch: names and order follow; biomes found before stay known.
- T20 Relog and new world: works, catalog rebuilt (Info log); build time and counts noted.
- T21 Resets, on a character with a killed Boar, a met-only Greyling, a placed Workbench and a Deer known only by its
  trophy. `resetknownitems`: items "???"; Workbench still discovered (placed); Deer "???"; Boar, Greyling and biomes
  still discovered. Then `resetcharacter` (cheat): items and met-only Greyling "???"; biomes "???" except the current
  one after reopening; Boar still discovered with its kill count; Workbench still discovered.
- T22 Live toggle: (a) `Enabled = false` with the window open, through the `.cfg` file (alt-tab; `ModPlugin` watches
  it) or ConfigurationManager (F1; a click there may hit our click-outside button first), not through the MC Mods
  panel (it only exists in the pause and main menus, and the pause menu cannot open while the inventory is visible,
  `Menu.Update`, 1.2) → the window vanishes, no error; the Valheim Compendium has no tabs and is exactly the game's;
  `true` again with the Valheim Compendium open → its tabs appear at once. The same toggle with the Valheim Compendium
  open: its tabs vanish at once, the dialog stays open, unchanged. With the optional button on, the five vanilla side
  controls are back exactly where vanilla puts them (compare with a screenshot without the mod) and the button comes
  back at once in the re-spaced row. (b) MC Mods panel with the inventory closed: off → no tabs at the next open; on →
  tabs back (G9).
- T23 `Enabled = false` + restart: no tabs, no button, vanilla Compendium and side panel unchanged (G9).
- T24 Clean log: no errors or warnings in a normal session (except expected Info/Debug lines).
- T25 Build interrupted: automated in `compendium.toggle` (window closed during a fresh build, next open lists rows).
  By hand: the catalog is built once per world session and takes about 0.1-0.2 s (1.0.16 runs: 102-178 ms over 9-12
  frames), so log out and in before each try, click the Encyclopedia tab and press Esc (then, after another login, Tab) at
  once; the try counts only when the window closed while it still said "Preparing entries..."; reopen: the list is
  there (or fills while you watch), one Info "built" line per session, no error (G2).
- T26 View/Select outside the side panel (optional button on): with the inventory closed, press View/Select ten times: the map opens and
  closes as usual and the Encyclopedia never opens; open the inventory afterwards: the window is closed (G2).
- T27 Mouse modality: with the window open, right-click a food item in a visible inventory slot: nothing (not
  eaten). A left click on a slot, a drag, a shift-drag or a left click on a vanilla side button each only closes the
  window (like a click outside the vanilla Compendium): no item picked up, moved, used or split, no side dialog. After
  closing, everything works again (G2).
- T28 Window layout at 1920x1080 and 2560x1440: top tabs on the title line at the left, category tabs fit (one or two
  rows), search at the top of the list, counter
  clear of the close button and title, rows and wrapped details readable; no `OVERLAP:` line in the Debug window
  dump; note whether the console draws above or below the window (open question 11).
- T29 Session memory: tab and per-tab entry, and the search, come back after closing and reopening; a new login
  starts fresh.
- T30 Side row, controller (optional button on): with the side panel focused, the D-pad walks Valheim Compendium → Encyclopedia → Skills →
  Trophies → Achievements → PvP and back in that order; View/Select on any of them opens the Encyclopedia (G1).
- T31 Side row after a logout (or another world), optional button on: the row is re-spaced with the button in it again; turning the mod
  off gives the exact vanilla row (G1).
- T32 Unlock modes: `nocost`, then `setkey AllRecipesUnlocked` / `AllPiecesUnlocked`: every item the crafting panel
  offers and every piece discovered, seasonal items out of season not; back to "???" after turning each off (G5).
- T33 Eggs: a Chicken Egg's details say "Laid by a tamed <Hen>" ("???" until the Hen is discovered) (G6).
- T34 Reading changes nothing: with `RevealAll`, a new character opens the Fishing Rod's details: no Fishing skill
  appears in the Skills dialog.
- T35 Tabs in the Valheim Compendium (mouse), 1920x1080 and 2560x1440: Texts (current) and Encyclopedia on the title
  line at the left, crafting-tab style, inside the frame, clear of the title, the list and the text pane, the rest of
  the dialog exactly the game's; Encyclopedia shows the window in the same place and size with the tabs on the same
  spot (no flicker, still dimmed); Texts brings the dialog back with its list filled again (G1, G2).
- T36 Remembered tab: the raven reopens the Encyclopedia after it was picked (whatever closed it), Texts after Texts;
  after a logout Texts first; the optional button does not change it (G2).
- T37 Controller tabs: RT = Encyclopedia, LT = Texts in both dialogs, the tab already shown: nothing; the Texts list
  and scrolling, our LB/RB, the crafting tabs behind and B keep their own jobs; no MC mod shortcut fires from LT / RT
  (G2).
- T38 Optional button live: default off = the game's side panel exactly; on with the inventory open = button at once in
  the re-spaced row; off = gone, the five buttons exactly back (G1).

**Multiplayer**

- M01 Dedicated server without the mod: works; a creature owned by another player's game and killed together counts
  for you.
- M02 Hand-off: a friend without the mod: nothing changes for them (no tabs in their Valheim Compendium); their tames and creatures near you, once aimed at, are recorded as
  met by you; no error on either side.
- M03 Two players with the mod: knowledge per character, independent.

**Cross-mod**

- X01 Creature Kill and Tame Counts: same kills; "Tamed N" shown; its Player Statistics section still in the vanilla
  Compendium (Texts tab); a tame alone discovers a creature (new character, look straight up, `spawn Boar`, `tame`: no `Met Boar`
  line, the Boar discovered with "Tamed: 1").
- X02 Crafting Search and Sort: its field released on open; F with our window open does not focus it; typing in ours
  never triggers its keys.
- X03 Loot Pickup Filter: R3 and middle click do nothing through the window; its Auto pickup button cannot be clicked
  while the window is open; with its lists panel open, opening ours draws over it.
- X04 Sort Chest: chest open, chest grid focused (optional button on): View/Select sorts every time (ten presses) and never opens the
  Encyclopedia; with our window open, its pads are quiet.
- X05 Almanac: Trophies button still Almanac's; ours in the Valheim Compendium; Esc behaviour unchanged.
- X06 Jewelcrafting (or EquipmentAndQuickSlots): their additions stay on the Texts tab; our window carries none of them.
- X07 AugaLite or Veneer: no tabs (no button with the optional setting), at most one warning, no error.
- X08 A content mod (Jotunn items or creatures): its entries listed, discoverable.
- X09 ExtraSlots (optional button on): no overlap with our button (or the self-check warning).
- X10 A mod that moves or adds side panel buttons, optional button on (if one is available; else code review and `compendium.siderow`):
  moved buttons keep their places, an added one joins the even spacing, a mod that keeps moving them gets one Warning
  and our button after the last control; nothing flickers back and forth.
- X11 A mod that copies the Valheim Compendium (PraetorisClient Server Guide), if available: its window never shows our
  tabs; neither window closes the other.
