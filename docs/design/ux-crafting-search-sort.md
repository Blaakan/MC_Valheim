# Crafting Search and Sort — design

| | |
|---|---|
| Mod | Crafting Search and Sort |
| GUID / project | `MC.UX.Crafting.SearchSort` (`src/UX/Crafting.SearchSort/`) |
| Category / scope | UX / QoL |
| Side | Client (only the player who wants it installs it) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim`, `assembly_guiutils`, `assembly_utils`, `gui_framework` in `.ref/`; prefab, recipe asset and English names checked in the installed game data (`StreamingAssets/SoftRef/manifest`, `manifest_extended`, `resources.assets` localization table) |
| Status | Implemented (0.1.0), smoke test passed, in-game testing pending |

## Goal

Requirements from the user (2026-09-28), made precise:

1. **Sort button.** The crafting panel shows a Sort button. Clicking it opens a list of sort options: *Default*
   (vanilla order), *Name (A-Z)*, and item categories (Weapons, each weapon type, Shields, Armor, each armor slot,
   Ammo, Tools and light, Food, Meads and potions, Trinkets, Utility, Materials, Fish, Trophies, Other). Only the
   categories present in the current list are offered.
2. **Category sort semantics.** Choosing a category puts every entry of that category at the top, in the order they
   had; everything else follows **in its previous order, untouched**. *Name* sorts the whole list by the displayed
   (localized) item name. *Default* is exactly the vanilla order.
3. **Search field.** Typing filters the list **live**: an entry stays only if the typed text is found (case and spaces
   ignored) in its **item name**, its **recipe name**, or the **name of one of its ingredients** (the ingredients the
   entry shows). Example: `silver` keeps the Silver Sword and every recipe that uses Silver.
4. **Search follows the sort.** Sort = Weapons and search = `silver` gives the silver weapons first, then every other
   recipe that matches `silver`, and nothing else.
5. **Every crafting station.** Workbench, forge, black forge, galdr table, artisan table, cauldron, mead ketill, food
   preparation table, stonecutter (and any modded `CraftingStation`).
6. **Typing is safe.** While the field has the keyboard, typed keys do nothing else: no walking, the inventory does not
   close (E, Tab, Esc), console key binds do not fire. A controller player can never get stuck in the field.
7. **Framework toggle.** Off = vanilla crafting panel immediately, even while typing or with the sort menu open.

Items 1-2 are the user's behaviour 1, item 3 behaviour 2, item 4 behaviour 3, item 5 behaviour 4. Items 6-7 are
MC rules for any text field and any mod.

Non-goals: a "craftable now" filter, quantity crafting, `@mod` or `!ingredient` prefixes, searching chests or the
build menu, changing what vanilla lists at a station (station level, known recipes), translated category labels
(decision 7), full gamepad text entry (decision 6).

---

## 1. Vanilla behaviour (code trace)

### 1.1 One crafting panel for everything

- `CraftingStation.Interact` → `CheckUsable` (roof, fire) → `Player.SetCraftingStation` →
  `InventoryGui.Show(null, 3)` (crafting UI group active). The plain inventory (Tab) calls `Show(null)`. The crafting
  panel `m_crafting` is always active while the inventory is visible; without a station it shows the hand-crafting
  recipes.
- The nine requested stations are prefabs with a `CraftingStation` (prefab names checked in the game data: see
  `TESTING.md` Setup). The Forge of Potential is a `CraftingStation` with `m_upgrader` and no Craft tab. There is one
  recipe list for all of them, plus hand crafting and `nocost`.

### 1.2 Building the list

`InventoryGui.UpdateCraftingPanel(focusView)` (private) picks the tabs (no station → Craft only; a station without
`m_hasCraftTab` → Upgrade only), calls `Player.GetAvailableRecipes`, then `UpdateRecipeList(recipes)` (private), then
selects a row.

- `Player.GetAvailableRecipes` keeps enabled (or in-season) recipes that pass the console filter `Player.s_FilterCraft`
  (1.5), are known (or `nocost`, or the global key `AllRecipesUnlocked`) and fit the current station
  (`RequiredCraftingStation(recipe, 1, checkLevel: false)`, or `nocost`).
- `UpdateRecipeList` destroys every row of `m_availableRecipes` (`List<RecipeDataPair>`: `Recipe`, `ItemData`,
  `InterfaceElement`, `CanCraft`) and rebuilds it. Craft tab: one row per recipe without `m_noCraftOnlyUpgrade`.
  Upgrade tab: one row **per matching inventory item** for recipes with `m_maxQuality > 1`. `AddRecipeToList`
  instantiates `m_recipeElementPrefab` (children `icon`, `name`, `Durability`, `QualityLevel`, `selected`) at
  `(0, -index * m_recipeListSpace)`.
- It sizes the content to `max(m_recipeListBaseSize, count * m_recipeListSpace)`; `m_recipeListBaseSize` is read once
  in `Awake` from the list root's height.
- **Then it sorts** the rows by the player unique key `"sortcraft"` (`InventoryGui.SortMethod`): craftable first, then
  `m_listSortWeight`, then token / name / type / weight, then quality. `List.Sort` (not stable). **Then it repositions
  every row.** Consequence: a reorder of `recipes` in a prefix is overwritten; a mod must reorder in a **postfix** and
  redo the positions and the content height.

### 1.3 Which ingredients a row shows

`InventoryGui.SetupRequirementList` (called by `UpdateRecipe` with quality 1 in the Craft tab and item quality + 1 in
the Upgrade tab) shows a `Piece.Requirement` only when: at a station, `station.m_upgrader == req.m_upgraderResource`,
and without a station, `!req.m_upgraderResource` (idols only at the Forge of Potential); `req.GetAmount(quality) > 0`
(upgrade-only ingredients are hidden in the Craft tab and the reverse); and for `m_requireOnlyOneIngredient` recipes,
`Player.IsKnownMaterial(name)`.

### 1.4 Selection and scroll after a rebuild

`UpdateCraftingPanel` finds the previous selection again (`GetSelectedRecipeIndex(acceptOneLevelHigher: true)`, by
`Recipe` + `ItemData`); not found → index 0; empty list → `SetRecipe(-1)` (empty details, craft button off).
`SetRecipe` logs `Setting selected recipe <n>` on every call and, with `center`, scrolls through
`m_recipeEnsureVisible.CenterOnItem` (`ScrollRectEnsureVisible`, on the list's `ScrollRect`; it uses its
`maskTransform` and the `ScrollRect` height, so both must shrink together when the list is shortened).

### 1.5 When the list is rebuilt

Not every frame: `Show` (`SetupCrafting`), the tab buttons, `DoCrafting` after a craft, `OnRepairPressed`, dropping an
item outside, moving items (`OnSelectedItem`). `UpdateRecipe` runs every frame but only redraws the selected recipe.
A craft uses the snapshot taken in `OnCraftPressed` (`m_craftRecipe`, `m_craftUpgradeItem`), not the current
selection. `Hide` sets `m_craftTimer = -1`.

Vanilla backends without UI: console `filtercraft <terms>` replaces `Player.s_FilterCraft` (any term found in the
prefab name, `$token` or localized name; no ingredients); `sortcraft <Original|Name|Type|Weight>` writes the unique key
`"sortcraft"`. Unique keys also drive world-rate checks, so mods should not use them for settings.

### 1.6 The 1.0 build-menu search (the reference)

`BuildUi.m_searchField` (private `GuiInputField` on `Hud.instance.m_buildUi`). `BuildUi.UpdateSearch` removes spaces,
lower-cases (`ToLowerInvariant`) and keeps a piece when the term is **contained** in one of its
`BuildUiPieceButton.LocalizedSearchTerms`: the localized piece name and the localized name of every requirement,
refreshed on `Localization.OnLanguageChange`. `BuildUi.NavigationUpdate` focuses it on F (or `JoyLStick`). The text
survives closing the build menu and is cleared when the build tool or the category tab changes (`OpenBuildMenu`,
`SetCurrentTag`). `Player.TakeInput`, `PlayerController.TakeInput` and `Minimap.Update` check `SearchFieldFocused`
next to `Chat.HasFocus()`; `InventoryGui.Update` does not.

### 1.7 What reads the keyboard while the inventory is open

| Reader | Keys | Gate | Effect of typing in a mod field |
|---|---|---|---|
| `InventoryGui.Update` | Tab (`Inventory`), E (`Use`), Esc, `JoyButtonB/Y`; Esc also closes the side dialogs | `Chat.HasFocus()`, Console, Menu, TextViewer, Minimap, craft timer | **E, Tab, Esc close the inventory** |
| `PlayerController.TakeInput` | WASD, Space, Shift, Ctrl... | `Chat.HasFocus`, Menu, Console, TextInput, build search; the inventory blocks it only for a gamepad | **the player walks** |
| `Player.Update` | Use, Hide, GP, AutoPickup, hotbar... | `Player.TakeInput`, false while the inventory is visible | nothing |
| `Chat.Update` bind loop | keys bound with the console `bind` | only the chat's own field | **console binds fire** |
| `UIGamePad.ButtonPressed` | per button: a ZInput button and/or a `KeyCode` (prefab data) | the button's UI group | a panel button with a keyboard key would click |

`Chat.HasFocus()` is only read by gates (`InventoryGui`, `Player`, `PlayerController`, `GameCamera`, `HotkeyBar`,
`Minimap`, `StoreGui`, `TextInput`, build-mode snap keys); `Chat` never calls it. A postfix that ORs a focus flag into
it blocks the whole `InventoryGui.Update` hotkey block (gamepad B/Y included) and all walking, so it must only be
raised while typing. The only caller of `Terminal.TryRunCommand(text, silentFail: true, skipAllowedCheck: true)` is
the bind loop. The chat cannot be opened while the inventory is visible (`Chat.Update`) and neither can the pause menu
(`Menu.Update`): only the console (F5) can be focused next to our field. `GP` (guardian power) is bound to F by
default, but `Player.Update` reads it only when `Player.TakeInput` is true, which is never the case while the inventory
is visible. `Use` and `Inventory` are rebindable, so a player may bind one of them to F.

`InventoryGui.Update` closes the inventory only when `m_shownFrames > 1`, and calls
`ZInput.ResetButtonStatus("Inventory")` right before `Show`: a later `GetButtonDown("Inventory")` in the same frame
misses the press that opened it. `IsVisible()` (`m_hiddenFrames <= 1`) stays true on the frame of `Hide`; the Animator
bool `"visible"` is cleared at once.

### 1.8 UI toolkit facts

- `GuiInputField : TMP_InputField`: `ActivateInputField` turns EventSystem navigation off while typing (keyboard) and
  opens the Steam keyboard in Big Picture; navigation comes back **only on submit, deselect, or when the field is
  disabled or destroyed**, so a field that loses focus must also be deselected
  (`EventSystem.SetSelectedGameObject(null)`, as `Chat.Update` does). `GuiInputField.Start` adds its own listeners.
- A cloned `Selectable` keeps its source's `navigation` (explicit targets into vanilla panels). Unity's automatic
  navigation skips `Navigation.Mode.None`.
- `UIGroupHandler` (assembly_guiutils) selects `m_defaultElement` for gamepad users, deselects its children when its
  group turns inactive, and drives a `CanvasGroup.interactable` on the same object if there is one (prefab data). The
  crafting group is `m_uiGroups[3]`.
- A clone also carries the source's `UIGamePad` (button hotkey + `m_hint` object that may live outside the clone),
  `UIInputHint`, `UITooltip` and `Localize` components, and a `Button`'s persistent `onClick` calls.
- `Localization.Localize` memoises in an LRU cache of 100 entries: localizing hundreds of names per keystroke would
  thrash it. Skill names are `"$skill_" + SkillType` in lower case (`SkillsDialog`); `$skill_unarmed` is "Fists".

### 1.9 Multiplayer

The crafting panel is 100 % local (local `ObjectDB`, known recipes and inventory). `Player.m_customData` is saved in
the character file and unknown keys are ignored.

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28 (Thunderstore pages; GitHub sources for CraftingFilter and Sorted Menus):

| Mod | How | Lesson |
|---|---|---|
| CraftSearch (XineladaLendaria) v2.0.10 | Search field in every crafting panel, category chips, `@mod` search, quantity row; keeps vanilla ordering | Same screen area: the two cannot be used together. |
| AAA Crafting (Azumatt) v2.1.8, deprecated | Amount field, search (`!` = ingredient), paginator, controller virtual keyboard | Ingredient search is wanted (we do it without a prefix, like `BuildUi`); controller text entry is real work. |
| CraftingFilter (cjayride fork) v1.2.1 | Clones of `m_tabCraft` listed under the Craft tab on hover; replaces the `recipes` argument in an `UpdateRecipeList` prefix | Cloning a vanilla crafting-panel control looks native; a cloned button keeps its persistent `onClick`: replace it. Its filter AND-combines with ours. |
| Sorted Menus (Goldenrevolver) v1.3.4 | Sorts `recipes` in an `UpdateRecipeList` prefix per station prefab | In 1.0.16 vanilla re-sorts after building (1.2): sort in a postfix. |
| Vanilla `BuildUi` search | 1.6 | Copy its look, normalisation, name + requirement terms and the F key. |

---

## 3. Implemented design

### 3.1 Core idea

Vanilla builds its list exactly as today (other mods' patches on `GetAvailableRecipes`, `HaveRequirements` and
`UpdateRecipeList` prefixes still run). Then **one postfix on `InventoryGui.UpdateRecipeList`** (`CraftList.Organize`)
drops the rows that do not match the search, reorders the rest by the chosen sort (stable) and redoes the row positions
and the content height. `UpdateCraftingPanel` then selects and scrolls on the final list with its own code (1.4). A
search or sort change simply calls the vanilla `UpdateCraftingPanel(false)` again. The mod never touches
`Player.s_FilterCraft` or `"sortcraft"`: the console commands keep working and combine with ours (vanilla filter AND
our search; Default = whatever vanilla sort is active).

The UI is one row of two controls inserted above the list, cloned from vanilla widgets; the list is shortened by the
row's height, so nothing vanilla is covered.

### 3.2 Search (`RecipeTerms`, `CraftList`)

- **Normalisation** (like vanilla `BuildUi.UpdateSearch`, which removes spaces and lower-cases): `ToLowerInvariant`,
  then every whitespace character removed (not only spaces). Empty term = no filter.
- **Words of a recipe**, cached per `Recipe`: the localized item name (the row's name without `" xN"`), the result
  prefab name (`recipe.m_item.name`, e.g. `SwordSilver`), the recipe asset name without a leading `Recipe_` (asset
  names are `Recipe_<prefab>`, e.g. `Recipe_SwordSilver`; without the strip, `recipe` would match every row), and one
  entry per ingredient (`m_resources` with a live `m_resItem`: normalised localized name + the requirement).
- **Match**: `string.Contains` (ordinal) on a name, or on an ingredient that the row **shows**: the three tests of
  `SetupRequirementList` (1.3) with the row's own quality (`ItemData == null ? 1 : m_quality + 1`), the current station
  and the local player. `$tokens` are not searched (`item_` would match everything).
- **Live update**: `onValueChanged` stores the normalised term at once (every list build reads the latest one) and
  marks a refresh pending. The controller applies it 0.1 s after the last keystroke (each rebuild re-creates every
  row, like a tab switch), and at once on Enter, focus loss, right-click clear or a sort choice.
- **Applying** (`CraftSearch.ApplyNow`): skipped while the inventory is hidden or a craft runs (`m_craftTimer >= 0`;
  the request stays pending). Otherwise it clears the request and asks for a scroll to the top **before** calling
  `UpdateCraftingPanel(false)` (in a try/catch): one attempt per request, so a throwing or skipped rebuild is not
  retried every frame. Any list build that runs the postfix (vanilla `DoCrafting` at the end of a craft, a tab switch)
  already used the latest term and sort, so the postfix clears a pending request and asks for the scroll too.
- **Cache**: `Dictionary<Recipe, Entry>`; an entry is rebuilt when the recipe holds a different `m_resources` array
  (recipe-config mods that swap it in game). Cleared on `Localization.OnLanguageChange`, on a new `InventoryGui`
  (logout) and on deactivation. Not seen: a mod that edits an existing requirement in place (until the next clear).

### 3.3 Sort (`MC.Shared.ItemKinds`, `RecipeCategory`, `CraftList`)

**Shared classifier.** `src/Shared/ItemKinds.cs` (`internal enum ItemKind`, `ItemKinds.Classify(SharedData)`,
`ItemKinds.IsWeaponType(ItemType)`) is compiled into every MC mod so that Sort Chest (built later) puts each item in
the same group. Pure enum compares, no state, no allocation. Documented in `docs/modding/framework.md` (Internals).

| `ItemKind` | Rule (first match wins) |
|---|---|
| `Ammo` | `m_itemType` `Ammo` or `AmmoNonEquipable` |
| `Shield`, `Helmet`, `Chest`, `Legs`, `Hands`, `Cape` | `Shield`, `Helmet`, `Chest`, `Legs`, `Hands`, `Shoulder` |
| `Utility`, `Trinket` | `Utility`, `Trinket` |
| `Tool` | `Tool` (hammer, hoe, cultivator) |
| `Torch` | `Torch`, or a weapon type (below) with `m_animationState` `Torch` / `LeftTorch` |
| `Food` / `Potion` | `Consumable` with `m_food`, `m_foodStamina` or `m_foodEitr` > 0 / any other `Consumable` |
| `Material`, `Fish`, `Trophy`, `Misc` | `Material`, `Fish`, `Trophy`, `Misc` |
| `SkillTool` | weapon type with animation `FishingRod` / `Scythe`, or skill `Pickaxes`, `WoodCutting`, `Fishing`, `Farming` |
| `Misc` | weapon type with animation `Feaster` (tankards) |
| `Weapon` | any other `OneHandedWeapon`, `TwoHandedWeapon`, `TwoHandedWeaponLeft`, `Bow`, `Attach_Atgeir`; family = `m_skillType` |
| `Other` | everything else (`None`, `Customization`, unknown modded types) |

**Options** (menu order; the id is saved in the character and must never change):

| Id | Label | Rows |
|---|---|---|
| `default` / `name` | Default / Name (A-Z) | always listed |
| `weapons` | Weapons | `Weapon` |
| `w_swords` ... `w_unarmed` | `$skill_<skill>` (localized; `[token]` → English) | `Weapon` with skill Swords, Axes, Clubs, Knives, Spears, Polearms, Bows, Crossbows, ElementalMagic, BloodMagic, Unarmed (other skills: Weapons only) |
| `shields` | Shields | `Shield` |
| `armor` | Armor | `Helmet`, `Chest`, `Legs`, `Hands`, `Cape` |
| `helmets` / `chest` / `legs` / `capes` | Helmets / Chest armor / Leg armor / Capes | one slot each (`Hands`: Armor only) |
| `ammo` | Ammo | `Ammo` |
| `tools` | Tools and light | `Tool`, `SkillTool`, `Torch` |
| `food` / `meads` | Food / Meads and potions | `Food` / `Potion` |
| `trinkets` / `utility` | Trinkets / Utility | `Trinket` / `Utility` |
| `materials` / `fish` / `trophies` | Materials / Fish / Trophies | `Material` / `Fish` / `Trophy` |
| `other` | Other | `Misc`, `Other`, and rows another mod added without a recipe or item |

A row belongs to at most two options (e.g. Weapons + Swords, Armor + Helmets). The menu lists Default, Name, every
option with at least one row in the **current tab's full list** (before the search) and the active option. Counts
and the icon of the first row per option are gathered by the postfix on the full list, so the menu does not change
while typing.

**Semantics** (after the search filter, on the rows in their current order):
- Default: no change.
- Category: stable two-pass partition (rows of the option, then the rest, each in its previous order). Never
  `List.Sort`.
- Name: by cached localized name (`StringComparer.CurrentCultureIgnoreCase`), then higher quality first, then original
  position; craftable and greyed rows mix (decision 1). Rows without a recipe or item come after the named ones.

### 3.4 Patches

All bodies catch their own exceptions (`PatchGuard.Report`). Applied only while the feature is Active. No transpiler.

| Target | Type | What it does |
|---|---|---|
| `InventoryGui.UpdateRecipeList(List<Recipe>)` | Postfix, `Priority.Low` | `CraftList.Organize`: make the row if missing, station bookkeeping (3.6), classify and count on the full list, filter, sort, write back, reposition, **always** re-apply the content height, close the menu, refresh the button label, clear a pending refresh. Low priority: rows added by other mods' postfixes of higher priority are filtered and sorted too. |
| `InventoryGui.Hide()` | Postfix | `CraftSearch.OnInventoryHidden`: close the menu, drop the focus, clear pending flags, clear the text unless `KeepSearchText`, reset the sort to Default when `RememberSort` is off. Vanilla calls `Hide` every frame while dead or teleporting: the idle path is a few bool checks. |
| `Chat.HasFocus()` | Postfix | True while our field has the keyboard (`FocusGuard.FieldUntilFrame`); while the sort menu is open (`MenuUntilFrame`), true **only on the frame Esc or `JoyButtonB` is pressed**, so that key closes the menu and not the inventory, while WASD, E and Tab keep their vanilla meaning. Idle cost: two int compares. |
| `Terminal.TryRunCommand(string, bool, bool)` | Prefix | Skips calls with `skipAllowedCheck` (the bind loop) while our field has the keyboard. |
| `UIGamePad.ButtonPressed()` | Prefix | No panel button fires while our field has the keyboard. |

`FocusGuard` holds the two frame numbers. The controller sets them to `Time.frameCount + 1` every frame the condition
holds and on the frame it ends: the Esc (or B) that makes the field lose the focus in frame N is still blocked for
`InventoryGui.Update` in frame N whatever the script order, so the first Esc leaves the field and only the next one
closes the inventory.

Not patched on purpose: `Player.GetAvailableRecipes` (other mods call it for their own UIs), `InventoryGui.Show`,
`UpdateCraftingPanel`, `SetRecipe`, `InventoryGui.Update`.

### 3.5 UI (`SearchUi`, `SearchController`)

**Row placement.** `MC_CraftSearchRow` sits directly above the list, in space the list used to take:
- The `ScrollRect` is the one carrying `m_recipeEnsureVisible`, else the list root's parent `ScrollRect`. None found, a
  `LayoutGroup` on its parent, or a `ContentSizeFitter` on it or its viewport (a UI replacement mod) → one warning
  ("Crafting list layout not recognised..."), no row; the postfix still applies the remembered sort.
- `shift = m_recipeListSpace (30) + 4`. The `ScrollRect`'s top edge moves down by `shift` (`offsetMax.y`). The viewport
  and the `maskTransform` keep their previous gaps to the `ScrollRect` edges (moved only if they did not follow); the
  scrollbar's top moves too when it is outside the `ScrollRect` and started at the list top; `m_recipeListBaseSize` is
  lowered by `shift` converted into the list root's units. Every changed value is saved and restored on deactivation,
  only onto the same `InventoryGui` instance.
- The row takes the freed strip exactly (anchored to the list's top anchor line); it has a `LayoutElement` with
  `ignoreLayout`. Sort button width = `clamp(row width × 0.4, 90, 160)`, the field takes the rest minus 4.

**Clones.** Every clone is stripped before its `Start`: persistent listeners (field: muted, because `GuiInputField.Start`
adds its own listeners to the same events; buttons: `onClick` replaced), `UIGamePad` and `UIInputHint` components and
their hint objects **only when they are inside the clone**, `UITooltip`, `Localize`; every `Selectable` gets
`Navigation.Mode.None`.
- **Search field**: clone of `BuildUi.m_searchField` (fallback `Chat.m_input`; neither → no field, the Sort button
  still works). Single line, 40 characters, Esc keeps the text, select-all on focus, font size of the recipe row name.
  The vanilla placeholder is kept (`Search` if it is empty). `onValueChanged` → store the term; `onSubmit` /
  `onEndEdit` → apply now. Right-click clears the search.
- **Sort button**: clone of `m_takeAllButton` (fallback `m_stackAllButton`), label `Sort: <option>` (ellipsis, no
  wrap). Left click toggles the menu and deselects the button (a later gamepad A cannot click it again). Right-click
  resets to Default, or only closes the menu when the sort is already Default.
- **Sort menu** (`MC_CraftSortMenu`, built on open, destroyed on close): child of `m_crafting`, last sibling, dark
  0.9-alpha base with the list's background sprite on top when it has one. One clone of `m_recipeElementPrefab` per
  option: label (+ ` (count)` for categories), icon of the category's first row, `Durability` / `QualityLevel`
  hidden, `selected` on the active option. Top-left at the row's bottom-left; columns as wide as the list, as many rows
  per column as fit in the visible list; rows shrink to 24 if the columns do not fit; clamped inside `m_crafting`. No
  full-screen blocker (a stuck one would make the inventory unclickable).
- The menu closes on: an option chosen, a left or right mouse-down outside the menu and the button (the click still
  goes through), Esc or B, the inventory hiding, any list rebuild (tab switch, station change, craft), the field
  taking focus, deactivation.

**Keyboard and focus.** `SearchController` (on the row) does all per-frame work; when the inventory is hidden it only
drops a focus that slipped past the `Hide` postfix and closes the menu.

| Input | Result |
|---|---|
| Click the field | TMP focus; typing filters live. |
| `FocusSearchKey` (F) | Recorded in `Update`, applied in `LateUpdate` (so the F is not typed) if the inventory is still shown (Animator `"visible"`), none of `Use`, `Inventory`, `JoyButtonY`, `JoyButtonB` went down this frame, the press is not on the first frame the inventory is shown (the rebound-`Inventory` case, 1.7), no other TMP field is focused, no IMGUI field has the keyboard (`GUIUtility.keyboardControl`, ConfigurationManager), console and pause menu closed, no split/variant dialog or side panel open, and `Chat.HasFocus()` is false. |
| Enter | Field loses focus, text kept, pending refresh applied now. |
| Esc | Field loses focus, text kept, inventory stays open; the next Esc closes it. |
| Gamepad B while focused | Drops the focus like `Chat.Update` does; the guard keeps that B away from `InventoryGui`. |
| Typed keys while focused | Only edit the text (3.4). |
| Focus lost (any reason) | Deselect the field (navigation back) and apply the pending refresh now. |
| Right-click field / Sort button | Clear the search / reset the sort to Default. |

**Gamepad.** No new gamepad binding (decision 6). Navigation `None` keeps the D-pad and stick off our controls; a
gamepad cursor can still click them and B leaves the field. The recipe list keeps its vanilla gamepad navigation and
the remembered sort applies.

**UI-group fallback.** If the crafting group (`m_uiGroups[3]`) has a `CanvasGroup` that is not interactable when the
field or button is pressed (plain inventory with the player grid active), a left press switches to the crafting group
(`SetActiveGroup`, no sound) and activates the field on the next frame. Without such a `CanvasGroup` it does nothing.

### 3.6 Persistence and station bookkeeping (`SortMemory`, `CraftSearch`)

- **Station key** = `GetCurrentCraftingStation().m_name` (`$piece_forge`...), `none` without a station, `unnamed` for
  an empty name; `|` and `=` are replaced with `_`.
- **Sort memory**: `Player.m_customData["MC.UX.Crafting.SearchSort.Sort"]` = `"<station>=<id>|<station>=<id>..."`.
  Default = no entry; malformed parts are dropped. Written when the player picks an option with `RememberSort` on.
  Vanilla ignores the key, so the character loads without the mod.
- **New station key or new local player** (seen by the postfix before filtering, so the build that sees it is already
  right): load that station's sort (Default when `RememberSort` is off), clear the search, close the menu.
- **`RememberSort` changed** (panel, ConfigurationManager, file): the current station's saved sort (or Default) is
  loaded at once, and the list rebuilt if the inventory is open.
- The search text lives in memory only. Nothing is written to `"sortcraft"`, `s_FilterCraft`, items, ZDOs or the world.

### 3.7 Configuration

Section `General` (after the framework's `Enabled` and `Status`); all read when used, so edits apply live.

| Key | Default | Description (user-facing) |
|---|---|---|
| `FocusSearchKey` | `F` | Key that puts the cursor in the crafting search field while your inventory or a crafting station is open (the same key as the build menu search). Leave it empty to only click the field. |
| `RememberSort` | `true` | Remember the sort you picked for each type of crafting station (saved with your character). Off: every station opens in the game's normal order. |
| `KeepSearchText` | `false` | Keep the search text when you close and reopen the same crafting station, like the build menu does. Off: the search is cleared every time the inventory closes. |

### 3.8 Creation, cleanup, live toggle

- Created lazily by the postfix the first time a list is built while Active, and by `OnActivated` when an
  `InventoryGui` exists. A new `InventoryGui` instance (logout, new world) → the old objects died with the scene: the
  mod forgets them and starts fresh (no restore onto the new instance).
- `OnActivated`: `Active = true`, subscribe `Localization.OnLanguageChange`, make the row, rebuild the list if the
  inventory is open (remembered sort applies now).
- `OnDeactivated`: `Active = false` first (the postfix does nothing even though patches are still applied during this
  call); close the menu, drop the focus, destroy the row; restore the layout (same instance only); reset `FocusGuard`
  after the row is destroyed (its `OnDisable` could raise it again); unsubscribe; clear caches and state; rebuild the
  vanilla list if the inventory is open. The next frame is fully vanilla, even when the field had the keyboard.
- Language change: term caches and weapon-type labels dropped, the Sort button label refreshed at once. Known gap: the
  cloned placeholder keeps the old language until the row is rebuilt from the build-search field (next character
  load; after an off/on toggle only if that field's own placeholder already follows the language, unverified).

### 3.9 Performance

- Rebuilds are event-driven (1.5) plus our debounced refresh (at most ~10 per second while typing), each the cost of a
  vanilla tab switch. The postfix reuses static buffers (no per-row allocation); terms and display names are cached
  per recipe (avoids the 100-entry `Localization` LRU); classification is enum compares, not cached.
- Per frame: `Chat.HasFocus` postfix two int compares (plus two key reads while the menu is open), `UIGamePad` prefix
  one compare, the controller a few bool checks and one key read while the inventory is open, the outside-click test
  only on mouse-down frames with the menu open.
- Vanilla `SetRecipe` logs one line per rebuild; the debounce keeps it low.

### 3.10 Multiplayer and hand-off

Client-side, compatible. The mod only reorders and filters the local crafting list and stores one string in the local
character; nothing is sent. Items crafted after a search or sort are ordinary vanilla crafts (M02). A character played
later without the mod loads and shows the vanilla list (M03). Dedicated servers never load it (`BepInProcess` =
client).

### 3.11 Files

`Plugin.cs` (config, `OnActivated` / `OnDeactivated`), `CraftSearch.cs` (state, refresh, station bookkeeping),
`CraftList.cs` (the postfix work, row category dump), `RecipeTerms.cs` (normalisation, term cache, match),
`RecipeCategory.cs` (options, ids, labels), `SortMemory.cs`, `SearchUi.cs` (layout, row, clones, menu, layout dump),
`SearchController.cs` (controller + `SearchFieldPointer` / `SortButtonPointer`), `FocusGuard.cs`, `Patches/`
(`InventoryGuiPatches`, `ChatPatches`, `TerminalPatches`, `UIGamePadPatches`). Shared: `src/Shared/ItemKinds.cs`.

### 3.12 Debug aids (Debug log level only)

- Once per `InventoryGui`: the layout before and after the shift (rects in `m_crafting` space, anchors, offsets,
  components, `CanvasGroup` on `m_crafting`, layout components, `m_uiGroups[3]` and its default element, which rects
  moved), then the hierarchy of the cloned controls. Confirms the placement (T01, T27) without screenshots.
- Once per station and tab: `Crafting list at station '<key>' (<tab> tab), N rows:` then per row
  `<prefab> <m_itemType> <m_skillType> <m_animationState> food=<hp>/<stamina>/<eitr> -> <ItemKind> -> <option(s)>`.
  Confirms the station tokens and the unverified classifications (T21, T22). Sort Chest logs the same `ItemKind` format.

### 3.13 Coordination with other MC mods

- **Sort Chest** (upcoming) puts its controls on the container panel (`m_container`); our row and menu stay inside
  `m_crafting`. Both clone `m_takeAllButton` and should share the menu pattern (recipe-row clones, close on outside
  click, no blocker, navigation `None`, in-clone hint rule). Its type groups are built on `ItemKinds.Classify`, so an
  item lands in the same group in both mods (C02).
- **Loot Pickup Filter** (upcoming) uses the player inventory panel (`m_player`). A text field there uses the same
  `Chat.HasFocus` postfix pattern (ORed postfixes coexist); our F key never takes the focus from a focused TMP field
  (C03). MC hotkeys that work while the inventory is open must check `Chat.instance.HasFocus()`.
- **One Click Repair All** calls `UpdateCraftingPanel` after its loop: our postfix keeps the search and sort (C01).

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Empty search, sort Default | Vanilla list | Identical (only counts, label and content height) |
| `silver` at the forge | — | Rows whose item name, prefab/recipe name or a shown ingredient contains "silver" (Frostner through `MaceSilver`) |
| Ingredient only for upgrading (Craft tab), only for crafting (Upgrade tab), idol at a normal station, unknown "any one of" material | Not shown | Not matched |
| Row added by another mod without a recipe or item | — | Never filtered out, category Other, kept in its order in the "rest", after the named rows by Name |
| `SiLver SWord` | — | Same as `silversword` |
| No match | — | Empty list, no selection, craft button off |
| Selected recipe filtered out / still matching | — | First remaining row selected / stays selected; list scrolled to top |
| Typing while a craft runs | — | The craft finishes with its own item; the list updates with the rebuild vanilla does at the end |
| Category not present in this tab | — | Not listed (unless active: then `(0)`) |
| Upgrade tab, Forge of Potential | Upgrade rows | Filtered by each row's next-level ingredients; categories from the upgrade rows |
| Hand crafting (Tab), also with a chest open | Hand recipes | Same controls, remembered under `none` |
| `nocost` | Every recipe | Works on that list |
| `filtercraft bronze` + search `sword` | Bronze-only list | Bronze swords only |
| `sortcraft Type` | Type order | Default = type order; categories partition it; Name replaces it |
| Language changed | Names re-localized | Caches dropped; labels and button in the new language; placeholder stays old (known gap) |
| A mod swaps a recipe's ingredient array in game | New ingredients shown | Search follows (entry rebuilt); an in-place edit is seen after the next cache clear |
| `RememberSort` changed in game | — | Applied at once, also at the station used last |
| E / Tab / Esc / WASD / 1-8 / binds typed in the field | Close / walk / fire | Only typed |
| Esc (or B) with the menu open | — | Closes the menu only |
| WASD / E / Tab with the menu open, field not focused | Walk / close | Unchanged (the menu closes with the inventory) |
| F while the console, another TMP field or an IMGUI field has the keyboard | — | Not taken |
| `Use` or `Inventory` rebound to F | F closes / opens the inventory | The field is never focused by that press |
| Gamepad D-pad or stick | Vanilla navigation | Never lands on our controls |
| Inventory closes while typing (death, teleport, walking away) | — | Field deselected, menu closed, search cleared unless kept |
| Opening another station type | New list | Its remembered sort, empty search |
| Logout / new world | New `InventoryGui` | Row rebuilt on the new instance |
| Mod off while typing or with the menu open | — | Row gone, focus released, layout restored, vanilla list the same frame |
| First list after launch is short (stonecutter) | Content = base height | No blank scroll strip (content height re-applied) |
| UI replacement mod (layout not recognised) | — | No row, one warning, remembered sort still applied |
| CraftSearch / AAA Crafting | Their field above the list | Not compatible (two fields) |
| CraftingFilter | Category filter | Filters AND-combine; its hover list may cover our row |

---

## 5. Decisions and open questions

### Decisions

All accepted by the lead before implementation. The ones marked **(to confirm with the user)** change what the
player sees and have a simple alternative.

1. **Name sort mixes craftable and greyed rows** (to confirm with the user): "Name (A-Z)" orders the whole list, as
   the request says. Alternative: keep craftable rows first, like vanilla `sortcraft Name`.
2. **Category sort = stable partition of the current order** (from "the rest of the list untouched"): inside the
   category and inside the rest, vanilla's own order (craftable first) is kept.
3. **Search rules = the vanilla build-menu search** (to confirm with the user): lower case, spaces ignored, substring;
   on the localized item name, the internal prefab and recipe names (the user's "recipe name"; English, so `sword`
   also finds swords in a French game) and **the ingredients the row shows** (so every match can be explained after
   clicking the row; unknown "any one of" materials are not revealed). No multi-word AND, no accent folding, no
   `$token` search. Alternative: match every `m_resources` entry, which also finds recipes whose upgrades need the
   material.
4. **Search text cleared when the inventory closes** (to confirm with the user): a forgotten filter never hides
   recipes. `KeepSearchText = true` mirrors the build menu (kept at the same station type, cleared at another).
5. **Sort remembered per station type, per character** in `Player.m_customData` (`RememberSort = false` turns it
   off). Not written to `"sortcraft"` (unique keys drive world-rate checks; uninstalling would leave vanilla's sort
   changed).
6. **Keyboard and mouse only** (to confirm with the user): no gamepad path to the field or the menu in 0.1.0. The
   build search's gamepad key (`JoyLStick`) is vanilla's multi-craft modifier in this panel, and the list's gamepad
   navigation is custom code that cannot reach extra controls. Our controls are unreachable by D-pad on purpose, and B
   always leaves the field: a controller player can never get stuck. The remembered sort applies with a gamepad.
7. **English labels except the weapon types** (to confirm with the user): weapon types use vanilla `$skill_` tokens
   and follow the game language; the other labels, "Sort:" and the placeholder fallback are English in 0.1.0 (no
   shared localization helper yet).
8. **One shared classifier, tools apart from weapons** (to confirm with the user): pickaxes, the scythe, the fishing
   rod and torches are under Tools and light, not under a weapon type (vanilla `IsWeapon()` counts torches as weapons);
   tankards (`Misc` type or `Feaster` animation) are under Other; mead bases follow their item type (Materials if the
   prefab says `Material`, unverified). The rules live in `src/Shared/ItemKinds.cs` (compiled into every MC mod) so
   Sort Chest agrees item by item.
9. **Counts come from the unfiltered tab list**, so the menu does not change while typing.
10. **One control row above the list**; the list loses one row of height. Covering nothing was preferred over
    overlaying the list or squeezing the tab strip.
11. **No vanilla state touched**: `s_FilterCraft` and `"sortcraft"` are left to the console and combine with ours.
12. **The sort menu does not freeze the player**: it only takes Esc/B (to close itself); only the focused text field
    blocks game keys.

### Added beyond the request

Hand crafting and the Forge of Potential get the same controls (T01, T18, T32); F focuses the field (T10);
right-click clears the search / resets the sort (T11, T17); counts and icons in the menu (T12); per-station memory of
the sort (T18); the `KeepSearchText` option (T19); the shared `ItemKinds` classifier (decision 8).

### Differences from the pre-implementation design

- The UI-group fallback is built in (it does nothing unless the crafting group has a non-interactable `CanvasGroup`).
- Clones also lose `UIInputHint` (and its in-clone hint objects) and `Localize`, so no vanilla key glyph or
  re-localization shows on our controls.
- The menu always has a dark base under the list's background sprite (the list's colour alone could be unreadable).
- The Sort button is deselected after a click; right-click on Default only closes the menu; the button falls back to
  a clone of Place stacks when Take all is missing.
- The row category dump runs once per station and tab (not on every station change); a dump of the cloned controls
  was added. An empty station name is saved as `unnamed`.
- The F key ignores the frame the inventory was shown (rebound `Inventory` key); the Sort button label refreshes on a
  language change; a rebuild request is tried once; `RememberSort` changes apply at once; the term cache follows a
  swapped `m_resources` array.

### Open questions

1. Should the menu also offer "Craftable first" or "Weight"? (Not requested; vanilla has them only in the console.)
2. Translations for the English labels (would need a shared localization helper in `src/Shared`).
3. Is two-column wrapping acceptable for the menu, or should it scroll?

### Unverified names and values

- The stations' `m_name` tokens (`$piece_workbench`, `$piece_forge`, `$piece_blackforge`, `$piece_magetable`,
  `$piece_artisanstation`, `$piece_cauldron`, `$piece_meadcauldron`, `$piece_preptable`, `$piece_stonecutter`,
  `$piece_upgradestation`): the localization keys exist, their use as `m_name` is assumed. They are only sort-memory
  keys; the Debug row dump logs the real value.
- The category of vanilla tools, torches, tankards, fishing bait, feasts, meads and mead bases (prefab values of
  `m_itemType`, `m_skillType`, `m_animationState`, food values; T21, T22). The game data files only give asset
  folders (`FishingBait*` under `Items/materials`, `MeadBase*` under `Items/consumables`, `Tankard*` under
  `Items/misc`, `Torch` under `Items/weapons`), not the item types.
- Which station crafts the Tankard and the biome fishing baits (`Recipe_FishingBaitForest`... exist); that the Hammer
  and the Torch are crafted by hand.
- The placement of the row and the list layout (`ScrollRect`, viewport, mask, scrollbar, `CanvasGroup` on the crafting
  group), confirmed only by the Debug layout dump (T01, T27).
- The vanilla build-search placeholder text; whether any `InventoryGui` button has a keyboard `UIGamePad.m_keyCode`.
- Which station crafts `ArmorWolfChest`; that `spawn UpgradeStation` places a working Forge of Potential; which idol
  tier each item needs; whether `AllRecipesUnlocked` counts as a cheat key (`setkey` refuses non-modifier keys on a
  profile that never cheated).

---

## 6. Tests

The in-game checklist lives next to the code: `src/UX/Crafting.SearchSort/TESTING.md` (run
`./tools/Get-TestTodo.ps1 -Mod SearchSort`; smoke test `./tools/Test-Smoke.ps1 -Mod SearchSort`). It covers every goal
item: T01 (goal 5, every station), T02-T06 and T20 (goal 3), T12-T15 and T17 (goals 1-2), T16 (goal 4), T07-T10 and
T28 (goal 6), T29, T30 and T33 (goal 7); T18-T19 persistence, T21-T22 classification, T23-T27 and T31-T32 the rest.
M01-M03 multiplayer (M02 items and M03 character: hand-off), C01-C03 compatibility (C01 One Click Repair All, C02
CraftingFilter, C03 CraftSearch / AAA Crafting). The cross-mod tests with Sort Chest and Loot Pickup Filter live in
those mods' own `TESTING.md` (they are built after this one).
