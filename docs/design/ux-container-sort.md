# Sort Chest — design

| | |
|---|---|
| Mod | Sort Chest |
| GUID / project | `MC.UX.Container.Sort` (`src/UX/Container.Sort/`, root namespace `MC.UX.ContainerSortMod`) |
| Category / scope | UX / QoL |
| Side | Client (only the player who wants it installs it), multiplayer Compatible |
| Sheet idea | `Sort chest` |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`), decompiled `assembly_valheim`, `assembly_utils`, `assembly_guiutils` in `.ref/`; item and piece prefab names checked against the installed 1.0.16 game data (`StreamingAssets/SoftRef/manifest_extended`) and the JotunnDoc item list (generated from 1.0.7) / piece list (1.0.12); item sources checked on valheim.weirdgloop.org (2026-09-28) |
| Status | Implemented (0.1.0), smoke test passed 2026-09-29 (loads, patches cleanly, JitCheck clean: 278 methods, 0 failures), in-game testing pending |

"Container" and not "Chest" in the GUID: carts, ships and barrels use the same panel. A "Sort bags" mod for the
player inventory would have been `Inventory.Sort` (the idea is cancelled in the sheet).

## Goal

Requirements from the user (2026-09-28), made precise:

1. The container panel of the inventory screen (any container opened with E: all chests, barrels, the personal
   chest, carts, ships, the obliterator, dungeon and loot chests; **not** tombstones, decision 5) gets a **Sort
   button**.
2. The player chooses the sort **criterion**: **by name**, **by type** or **by biome**, with a second button next to
   Sort. The choice is remembered (config), and changing it does not sort (decision 1).
3. Clicking Sort **rearranges the container's content from the top-left slot to the bottom-right slot**, row by row,
   in the chosen order, with no gaps between items (empty slots end up at the bottom right).
4. The mod can be turned off on its own, live, like every MC mod (framework toggle): off = vanilla panel, no button.

Items 1-2 are the user's behaviour 1, item 3 behaviour 2. Item 4 is an MC rule for every mod.

Precise meaning of the criteria (3.4):

- **Name**: translated item name A→Z (game language, accent- and case-insensitive), then higher quality first, then
  bigger stack first.
- **Type**: fixed group order (weapons by weapon kind, ammo, shields, armour by slot, utility and trinkets, tools and
  light, food and potions, materials, fish, trophies, other), then name. The groups are built on the shared
  `MC.Shared.ItemKinds` classifier, so an item lands in the same group as in the Crafting Search and Sort menu.
- **Biome**: progression order Meadows → Black Forest → Swamp → Ocean → Mountain → Plains → Mistlands → Ashlands →
  Deep North, items of unknown biome (modded, unobtainable test items) last; inside a biome by type, then name.

Non-goals: sorting the player inventory (the separate "Sort bags" idea is cancelled in the sheet), auto-sort on open or close (players
dislike losing their spatial memory), sorting chests that are not open (quick-stack / remote containers), locked or
favourite slots, a keyboard hotkey, search/filter (separate idea "Search chest"), sorting by weight or value.

---

## 1. Vanilla behaviour (code trace)

### 1.1 The container panel and its two buttons

- `InventoryGui` (singleton `InventoryGui.instance`) owns the whole Tab screen. The container part is `m_container`
  (RectTransform), `m_containerGrid` (`InventoryGrid`, private; public accessor `ContainerGrid`), `m_containerName`,
  `m_containerWeight` (TMP), `m_takeAllButton` and `m_stackAllButton` (label "Place stacks", token
  `$inventory_stackall`).
- `InventoryGui.Awake` hides `m_container` and binds `m_takeAllButton.onClick → OnTakeAll` and
  `m_stackAllButton.onClick → OnStackAll` with `AddListener` (runtime listeners: **not** copied by
  `Object.Instantiate`).
- `InventoryGui.Show(Container container, int activeGroup = 1)` (the only overload) stores `m_currentContainer`
  (private). It is called by `Container.RPC_OpenResponse` (container), `CraftingStation.Interact` (`Show(null, 3)`) and
  the Tab key (`Show(null)`).
- `InventoryGui.UpdateContainer(Player)` (private) runs **every frame** while the screen is visible. If
  `m_currentContainer` is set **and** `m_currentContainer.IsOwner()`, it calls `SetInUse(true)`, activates
  `m_container`, refreshes `m_containerGrid.UpdateInventory(inv, null, m_dragItem)` (so the grid repaints from the
  live `Inventory` every frame), sets the name, auto-closes beyond `m_autoCloseDistance` and handles "hold Use = Place
  stacks then close". Otherwise it hides `m_container` and cancels a drag that comes from a non-player inventory. So
  the panel only appears once this client owns the container's ZDO, which on a server can be some frames after
  `Show`.
- `InventoryGui.OnTakeAll` / `OnStackAll`: both return if `Player.m_localPlayer.IsTeleporting()` or there is no
  container, then call `SetupDragItem(null, null, 1)` (cancel any drag), then `playerInv.MoveAll(containerInv)` /
  `containerInv.StackAll(playerInv)`. **This is the pattern we copy**: vanilla does not refuse a click while dragging,
  it cancels the drag first.
- `InventoryGui.CloseContainer` (private) and `Hide` cancel drags from the container, call
  `m_currentContainer.SetInUse(false)` and clear `m_currentContainer`.
- Drag state: `m_dragItem`, `m_dragInventory`, `m_dragAmount`, `m_dragGo`; set and cleared only in
  `InventoryGui.SetupDragItem(ItemData, Inventory, int)` (private). A dragged item **never leaves its slot** until it
  is dropped (`OnSelectedItem` → `InventoryGrid.DropItem`), so cancelling a drag loses nothing.
- Split dialog: `m_splitDialog` (`SplitDialog`), `SplitDialog.IsActive` = `gameObject.activeSelf`.
- Gamepad groups: `m_uiGroups[]`, index 0 = container grid, 1 = player grid, 2 = side panels, 3 = crafting
  (`UpdateGamepad`, `SetActiveGroup`).
- Effects: `m_moveItemEffects` (public `EffectList`), played by `SetupDragItem` and by `Container.RPC_StackResponse`
  after a successful Place stacks.

### 1.2 Gamepad buttons (`UIGamePad`)

- A `UIGamePad` next to a `Button` fires `Button.OnSubmit` when its `m_zinputKey` (ZInput button name) or
  `m_keyCode` is pressed (`ButtonPressed`), only while the button is interactable and its `UIGroupHandler` is active
  (`IsInteractive`), and at most once per 2 frames for **all** UIGamePads together (static `m_lastInteractFrame`). It
  shows `m_hint` (a child object holding the glyph) while a gamepad is active. `UIGamePad.Start` caches
  `m_group = GetComponentInParent<UIGroupHandler>()` and hides `m_hint`; there is no `Awake`/`OnEnable`, so a freshly
  instantiated clone cannot fire before its first `Update`. There is no modifier check: LT/RT + the key fire it too.
- Two serialized fields are inherited by a clone: `alternativeGroupHandler` (private; when that group is active,
  `IsInteractive` returns true **whatever the button's own group**) and `m_blockingElements` (the key is ignored while
  any of them is active). Both are prefab data.
- Which keys `m_takeAllButton` / `m_stackAllButton` use is prefab data, not in `.ref`: the Debug layout dump (3.12)
  prints it, and `SortChestUi.CheckVanillaKeys` gives up our key (mouse only, one Warning) if a vanilla button already
  uses it.
- Glyph text: `Localization.instance.Localize("$KEY_JoyBack")` → `ZInput.GetBoundKeyString` returns a TMP sprite tag
  (xbox, ps5 or switch2 by controller). It works for every registered button.
- Which gamepad buttons are free while the inventory shows a container:
  - `Player.TakeInput` returns false while `InventoryGui.IsVisible()`: player actions do not read any button.
  - `JoyBack` (the View/Select button): no reader in `assembly_valheim`. On Xbox layouts `JoyMap` shares the Select
    button, but `Minimap.Update` ignores it while `InventoryGui.IsVisible()`. **Free.**
  - `JoyLStick` (left stick click): read by `InventoryGui` only as a **held** multi-craft modifier, whatever group is
    active (recipe preview, and when Craft is pressed): pressing it with the container group active only shows the
    multi-craft preview while held. `BuildUi` (focus the build search) and `GameCamera` (free fly) are not active with
    the inventory open. The left stick also **moves** the grid selection (`InventoryGrid`, `JoyLStickLeft/Right/Up/
    Down`), so accidental clicks are likely: only a harmless action may sit on it.
  - `JoyRStick` (right stick click): no vanilla reader in the inventory. The MC Loot Pickup Filter reads it (and
    LT/RT + R3) **only while the player grid is focused** (`FilterUi.HandleGamepad` checks
    `m_playerGrid.m_uiGroup.IsActive`); chest slots are marked with the mouse only. This mod never binds it, so no MC
    mod reads R3 in the container grid.
  - Crafting Search and Sort binds no gamepad key (its decision 6); its `UIGamePad.ButtonPressed` prefix blocks every
    `UIGamePad` while its search field has the keyboard.
  - So: Sort = `JoyBack`, criterion = `JoyLStick` (decision 9).

### 1.3 Containers and ownership (`Container`)

- `Container.Awake`: builds `m_inventory = new Inventory(m_name, m_bkg, m_width, m_height)`, subscribes
  `OnContainerChanged` to `m_inventory.m_onChanged`, registers `RPC_RequestOpen/OpenResponse`,
  `RPC_RequestStack/StackResponse`, `RPC_RequestTakeAll/TakeAllResponse`, and `InvokeRepeating("CheckForChanges", 0, 1)`.
  `m_nview` is the ZNetView of `m_rootObjectOverride` when set (expected on carts, `Vagon.m_container`, and on ships;
  which prefabs set it is prefab data, **unverified**).
- Opening: `Container.Interact` → ward check, `CheckAccess(playerID)` → `InvokeRPC("RPC_RequestOpen")`. The **current
  ZDO owner** runs `RPC_RequestOpen`: if `IsInUse()` (or the cart `Vagon.InUse()`) and the requester is someone else →
  answer false ("$msg_inuse"); else `ForceSendZDO`, **`GetZDO().SetOwner(requester)`**, answer true. The requester's
  `RPC_OpenResponse` calls `InventoryGui.instance.Show(this)`.
- **In use.** `m_inUse` is local to the owner; `SetInUse` only works on the owner and mirrors into `ZDOVars.s_inUse`
  for the lid visuals. `UpdateContainer` calls `SetInUse(true)` every frame while the panel shows **and** the local
  client owns the ZDO. So while the local player has a container open and owns it, the requests of other players are
  answered by us with "in use": `RPC_RequestOpen`, `RPC_RequestTakeAll` (sent only by `TombStone.Interact` →
  `Container.TakeAll`) and `Incinerator.RPC_RequestIncinerate`. `RPC_RequestStack` is sent only by
  `Container.StackAll()`, which only the panel's own "hold Use" calls; there is **no** "hold E from outside" path
  (`Container.Interact` returns false on hold). In vanilla, nobody else can change or look into the container while we
  have it open **and own it**.
- **Saving.** `Container.OnContainerChanged` → `Save()` if `!m_loading && IsOwner()`. `Save` writes `Inventory.Save`
  bytes to `ZDOVars.s_items` **once per call** and remembers the ZDO `DataRevision` (`m_lastRevision`).
- **Loading.** `CheckForChanges` (every 1 s) → `Load()` only if the ZDO revision changed **and** `!m_inUse`
  (`Container.Load` returns early while the local `m_inUse` is true); then `UpdateRows()`, which sets the height to
  `max(m_height, highest item y + 1)` (legacy chests that grew). Our own `Save` updates `m_lastRevision`, so the owner
  never reloads its own write. Other clients in range reload it within a second, so they see the latest layout.
- **Exception: ship storage, and the stale local copy.** A ship's container uses the ship's `ZNetView` through
  `m_rootObjectOverride` (which prefabs set it is prefab data, **unverified**). `Ship.UpdateOwner` (every 2 s) hands
  the ship ZDO to a player aboard when its owner is not aboard, with no in-use check. A player who opened the ship
  chest from the dock while someone else is aboard therefore loses ownership within 2 s: `UpdateContainer` hides
  `m_container`, `m_currentContainer` stays set, and that player's `m_inUse` **stays true locally** (`SetInUse` needs
  ownership), even after the inventory closes. The new owner's own `m_inUse` is false, so others can open it and
  change it. Because the old owner's stuck `m_inUse` blocks `Container.Load`, that player's copy stops following the
  ZDO. When the same player later owns and opens the container again (boards, the other player leaves), the local
  copy can be older than the ZDO's `s_items`, and **any** save (a vanilla item move or our Sort) overwrites the newer
  data: items the other player took come back (duplicated), items they added vanish. The same applies to an open
  granted within about 1 s of another player's last change (`CheckForChanges` runs once per second and stops as soon
  as `SetInUse(true)` runs). Carts are protected: `Vagon.RPC_RequestOwn` refuses while `m_container.IsInUse()`.
  So owning the ZDO at click time is needed but not enough: Sort also checks that the local copy matches the saved
  bytes (`LocalCopyCurrent`, 3.4) and refuses with a message otherwise.
- **Conclusion: the client that shows the container panel, owns its ZDO at click time, and whose local copy matches
  `ZDOVars.s_items` may rewrite that inventory freely, in one frame, and one `Inventory.Changed()` call saves it once
  for everyone.**

### 1.4 Inventory internals (`Inventory`, `ItemDrop.ItemData`)

- `m_inventory` (private `List<ItemData>`) is a flat list; each item carries `m_gridPos` (`Vector2i`, struct with
  `==`). `GetAllItems()` returns **the live list**. `m_width`/`m_height` via `GetWidth()`/`GetHeight()`.
- `Inventory.Changed(bool success = false, bool cheatedStateChanged = false)` (private): recomputes the weight, maybe
  shows the cheated-item popups (only with `success && cheatedStateChanged`, or when `m_cheatedPopup` was set), then
  invokes `m_onChanged` (→ `Container.OnContainerChanged` → `Save`). Every public mutator calls it: `AddItem(ItemData)`
  moves stackables **one unit per loop iteration** and calls `Changed` once at the end; `RemoveItem(ItemData)` calls
  `Changed`; `MoveAll` / `StackAll` call it several times. **Rearranging with those methods would save the ZDO many
  times and could drop items when a slot is taken.** Writing `m_gridPos` / `m_stack` directly on the live items and
  calling `Changed()` once is the correct path (QuickStackStore does exactly this).
- Loading a container (`Inventory.Load` → private `AddItem(int prefabHash, …, skipValidPositionCheck: true)`): clamps
  the stack to `m_maxStackSize` (**excess lost**) and adds at the saved position through the private
  `AddItem(item, amount, x, y)`, which **fails (item lost)** if `x` is out of range or the slot holds a
  non-`IsSameType` item, and **merges** if it holds a same-type item. So a sort must always produce **unique positions
  inside `0 ≤ x < width`, `0 ≤ y < height`** and **stacks ≤ `m_maxStackSize`**. `Inventory.Load` ends with a
  `Changed()`; on an `Inventory` nobody listens to (no `m_onChanged`) that only recomputes the weight.
- Stacking rules in vanilla:
  - `ItemData.IsSameType`: same `m_shared.m_name` and `m_worldLevel`, plus same `m_quality` when `m_maxQuality > 1`.
    **Ignores** `m_customData`, `m_variant`, `m_crafterID/m_crafterName`, `m_durability`, `m_pickedUp`, `m_cheated`.
  - `Inventory.AddItem(ItemData)` (pickup, Place stacks through `StackAll`, Ctrl+click moves) tops up
    `FindFreeStackItem(name, quality, worldLevel)` and ORs `m_cheated` into the target; the drag merge
    (`InventoryGrid.DropItem` → `MoveItemToThis` → private `AddItem(item, amount, x, y)`) only adds to
    `itemAt.m_stack`: it keeps **all of the target's** per-item data, **including its `m_cheated` flag**, and silently
    drops the source's custom data.
- `ItemData.Save` stores the durability as `(int)(m_durability * 100)` and `Load` reads it back `* 0.01f`: an item
  created in memory and an identical item reloaded from a save can differ in float bits; only the saved integer
  matters.
- Per-item fields: `m_stack`, `m_durability`, `m_quality`, `m_variant`, `m_worldLevel`, `m_pickedUp`, `m_crafterID`,
  `m_crafterName`, `m_customData` (`Dictionary<string,string>`, used by EpicLoot and by our Crossbow Stays Loaded),
  `m_gridPos`, `m_equipped`, `m_cheated` (console `spawn` marks items cheated; the tooltip shows a grey "cheated"
  line), `m_dropPrefab`.
- `m_dropPrefab` is set in `ItemDrop.Awake` to the `ObjectDB` item prefab, so it is reliable for items loaded from a
  save. `m_shared` is **a per-instance copy** in builds: never compare `m_shared` by reference.
- `ItemData.m_shared.m_maxStackSize` is read at runtime (stack-size mods change it).

### 1.5 Tombstones, corpses, obliterator, carts

- `TombStone.Interact` opens the grave through `Container.Interact` when it does not fit in the inventory.
  `Inventory.MoveInventoryToGrave` copies the player's **width/height and slot positions**, and Take all
  (`Inventory.MoveAll`) first tries each item's **same grid position**: the player gets their hotbar and layout back.
  Sorting a tombstone would destroy that.
- `Corpse`, loot containers with `m_autoDestroyEmpty`, the obliterator (`Incinerator.m_container`, refuses to run
  while `m_container.IsInUse()`), carts (`Vagon.m_container`, open refused while the cart is pulled, `Vagon.InUse`) and
  ship storage are ordinary `Container`s: sorting them is safe.

### 1.6 What vanilla does not have

- No sort API: `Inventory.GetAllItemsSortedByName` (unused) and `GetAllItemsInGridOrder` only return lists.
- No item → biome data. `Heightmap.Biome` (Meadows 1, Swamp 2, Mountain 4, BlackForest 8, Plains 0x10, AshLands 0x20,
  DeepNorth 0x40, Ocean 0x100, Mistlands 0x200) appears on world data only: `ZoneSystem.ZoneVegetation.m_biome`,
  `SpawnSystem.SpawnData.m_biome`, `Piece.m_onlyInBiome`, `ZoneLocation.m_biome`. Items, recipes and stations carry
  none.
- No vanilla localization token for "Sort" or "Type" (checked in the 1.0.16 localization table in
  `resources.assets`: only `$inventory_takeall`, `$inventory_stackall`, `$menu_name`, `$hud_category`, `$biome_*`
  exist).

---

## 2. Existing mods and what they teach

From `docs/research/existing-mods-combat-ux.md` (2026-09-28) plus a read of QuickStackStore's MIT source
(`SortModule.cs`, `ButtonRenderer.cs`, `ControllerButtonHintHelper.cs`, 2026-09-28):

| Mod | How | Lesson |
|---|---|---|
| Quick Stack Store Sort Trash Restock (Goldenrevolver, ~940K downloads) | Clones `m_takeAllButton` with `Object.Instantiate(button, parent)`, `onClick.RemoveAllListeners()`, sets the clone's `UIGamePad.m_zinputKey = null` and drives the controller itself, writes the glyph into `m_hint`'s `TextMeshProUGUI` one frame later. Sort: copy of `m_inventory`, optional merge, `List.Sort`, assign `m_gridPos`, one `inventory.Changed()`. Sorts only when `m_nview.IsOwner()`, else sends its own RPC (for MultiUserChest). Positions buttons relative to Take all. | Same write path as ours (confirmed safe at scale). Its merge groups by name + quality only, skips custom-data items, ignores world level and cheated flag, and removes items with `RemoveItem` (one `Changed` each): **we do a stricter merge and a single `Changed`**. Glyph handling pattern confirmed. |
| QuickStackPlus (Goneryx) | Sorting only on demand | Do not auto-sort on open. |
| Stackmaster (JStack424) | Auto-sorts on open | Players complain about lost spatial memory → non-goal. |
| RunicStorage (Chazman) | Routing rules by item and **biome**, sort | Biome grouping is a real player need; no reusable data. |
| HexQuickStackStorage, InventoryActions (sighsorry), SonicChestFilters | A Sort button under/next to the container | Same screen area: our buttons may overlap theirs (compatibility note, not fixable generically). |
| MultiUserChest (compat target of QSSSTR) | Lets several players open one chest | With it, the panel can show while we are not the owner: our Sort must then do nothing (ownership check at click time). |
| Auga (UI replacement) | May remove `m_takeAllButton` | Null-check every vanilla reference; no button when missing. |

Conclusion: clone the vanilla button, keep vanilla's ownership guarantee (and check the local copy is current),
rewrite positions in place, one `Changed()`. Our additions: a strict, lossless merge, a biome criterion with real data,
tombstone exclusion.

---

## 3. Implemented design

### 3.1 Core idea

Two buttons cloned from **Place stacks** (`m_stackAllButton`) are added to the container panel: **Sort** and a
**criterion button** that shows the current criterion ("By name" / "By type" / "By biome"). A click on Sort, only when
the local client owns the open container and its local copy matches the saved data, cancels any drag (like Take all),
optionally merges partial stacks of **identical** items, orders the items with the chosen comparator, reassigns
`m_gridPos` row by row from (0,0), reorders the live list to match, and calls `Inventory.Changed()` **once** if
anything was written. `Container.OnContainerChanged` then saves the ZDO once; the grid repaints next frame.

No per-frame patch at all: the buttons live under the vanilla panel and hide with it.

### 3.2 Patches

All bodies catch their own exceptions and call `PatchGuard.Report(site, e)`. Applied only while the feature is Active
(framework); turning the mod off removes them and `OnDeactivated` destroys the buttons.

| Target | Type | What it does | Why |
|---|---|---|---|
| `InventoryGui.Awake()` (private, no overload) | Postfix | `SortChestUi.Create(__instance)`: clone the two buttons (3.3). No measuring here (`m_container` was just deactivated, and other UI mods may not have moved anything yet). | Normal load path: one creation per `InventoryGui` (world session). |
| `InventoryGui.Show(Container, int)` | Postfix | `SortChestUi.OnShow(__instance)`: read **`__instance.m_currentContainer`** (not the argument: HarmonyX runs postfixes even when another mod's prefix skipped the original); create the buttons if missing; set them active only for an eligible container (3.6); re-apply the layout from the **current** positions of Take all / Place stacks with the row choice remembered for this size combination (3.3); refresh labels, tooltips and glyphs; put our pads on the container grid group; Debug log `Opened <name> <w>x<h>`; start the layout self-check for a size combination not seen before. | `m_currentContainer` only changes here, so eligibility is decided once per opening; language/controller changes and UI mods that move the vanilla buttons after `Awake` are picked up at each opening. |

Button clicks and the criterion change are **UnityEvent listeners**, not patches: they exist only while the buttons
exist, i.e. while the feature is active. Their bodies also use `try/catch` + `PatchGuard.Report`.

### 3.3 UI (`SortChestUi`)

**Elements** (both children of `m_stackAllButton.transform.parent`, so they share the panel's look, scaling and
visibility):

| Element | GameObject name | Label (English, decision 8) | Tooltip (topic / text) | Click |
|---|---|---|---|---|
| Sort button | `MC_ContainerSort_Sort` | `Sort` | "Sort" / "Rearrange this container from top left to bottom right, <criterion>." (e.g. "by type") | sort now |
| Criterion button | `MC_ContainerSort_Criterion` | `By name` / `By type` / `By biome` | "Sort order" / "Click to change how Sort orders items: by name, by type (weapons, armour, food, materials...) or by biome (Meadows to Deep North)." | next criterion Name → Type → Biome → Name; **does not sort** (decision 1); saves `SortBy`; updates both labels/tooltips |

**Cloning** (`CloneButton` / `Configure`):

1. No Place stacks button (UI mod) → one Warning "Place stacks button not found (UI mod?), Sort button not added.", no
   buttons.
2. The clone is instantiated under an **inactive holder** (so nothing on it wakes up before it is stripped), then
   moved under Place stacks' parent, right after Place stacks in sibling order.
3. `onClick` is replaced by a new event with our listener (drops any persistent listener); every `Selectable` in the
   clone gets `Navigation.Mode.None` (the D-pad never moves onto our buttons).
4. Only the `UIGamePad` on the button itself is kept; other pads in children are removed. Vanilla glyph switchers
   (`UIInputHint`) and their in-clone hint objects are removed (they would show Place stacks' keys), except our pad's
   own hint. `Localize` components are removed (our texts are plain English, never `$tokens`).
5. Label = the first `TMP_Text` not inside a hint object.
6. Gamepad, in the same frame as the Instantiate: `m_keyCode = None`, `m_zinputKey = <our key>`,
   `alternativeGroupHandler = null` (the clone never fires because some **other** group is active). The glyph
   (`Localize("$KEY_" + key)`) is written into the hint's `TMP_Text`, refreshed at each opening (the controller type may
   change); a hint without text is hidden (key still works). The inherited `m_blockingElements` are kept.
7. Tooltip: Place stacks' `UITooltip` if it has a prefab, else the inventory slot tooltip prefab; `Set(topic, text)`.
   `m_gamepadFocusObject` is cleared (it may point outside the clone).
8. `CheckVanillaKeys`: if Take all or Place stacks already uses one of our keys, our button for that key becomes mouse
   only (one Warning); a press would otherwise fire whichever pad updates first (shared 2-frame lock).
9. `FixPadGroups`: `UIGamePad.Start` takes the nearest parent `UIGroupHandler`; after `Start` ran, our pads' group is
   set to the container grid's group (`ContainerGrid.m_uiGroup`, fallback `m_uiGroups[0]`), so our keys and glyphs
   work only while the container grid is focused. Called at each opening and by the self-check as soon as the panel is
   up. Safety net for the frames before that: a click that arrives while our key is down and the container group is
   not active is ignored (`PadPressOutsideGrid`).

**Gamepad map** (decision 9). Shared gamepad map of the MC inventory mods (the Loot Pickup Filter design carries the
same table, `docs/design/ux-autopickup-filter.md` 3.4):

| Focused group (`InventoryGui.m_uiGroups`) | View/Select (`JoyBack`) | L3 (`JoyLStick`) | R3 (`JoyRStick`) | LT + R3 | RT + R3 |
|---|---|---|---|---|---|
| 0 container grid | Sort Chest: sort | Sort Chest: change criterion | none of ours | none of ours | none of ours |
| 1 player grid | none of ours | none of ours | Loot Pickup Filter: mark / unmark | Loot Pickup Filter: change mode | Loot Pickup Filter: lists panel |
| 2 side panel | Encyclopedia: open the window (only with its optional side button; default: none of ours) | none of ours | none of ours | none of ours | none of ours |
| 3 crafting | none of ours | none of ours | none of ours | none of ours | none of ours |
| Valheim Compendium (vanilla dialog, priority 2, above every inventory group while open) | none of ours | none of ours | none of ours | none of ours | none of ours |
| Encyclopedia window (its own group, above every inventory group while open) | none of ours (every inventory group is off, so no MC pad fires) | none of ours | none of ours | none of ours | none of ours |

The Encyclopedia's optional View/Select pad (`docs/design/exploration-compendium-encyclopedia.md` 3.6) belongs to the side panel
group, and its `UIGamePad.ButtonPressed` gate rejects the press in any other group before the shared lock is taken, so
View/Select on the chest grid always reaches Sort Chest. In the Valheim Compendium and the Encyclopedia window, LT / RT
alone pick the Encyclopedia's Texts / Encyclopedia tabs (read by its own code, not a pad; the inventory groups are off
there). A new MC binding must not use View/Select in the side panel, nor LT / RT alone in those two dialogs.

Sort Chest's `UIGamePad`s (View/Select, L3; no modifier check, so LT/RT + those keys act the same) fire only while the
container group is active, Loot Pickup Filter's R3 gestures only while the player group is active: one press never
reaches both, and no MC mod reads R3 in the container grid. Vanilla reads View/Select nowhere in the inventory and L3
only while held, as the multi-craft modifier (any group). InventoryActions also uses R3 (Loot Pickup Filter's
`GamepadControls = false` leaves it to that mod). The right stick click is never bound by this mod.

Sort sits on a button that is never pressed by accident; the left stick click, easy to hit while moving in the grid,
only changes the criterion label (harmless: nothing moves).

**Placement** (`ApplyLayout`): offsets from Place stacks in its parent's space, so the buttons follow the vanilla
buttons (and UI mods that move them). Rects are measured in the parent's space from world corners (any pivot,
anchor or scale). The row direction and gap are taken from Take all → Place stacks (gap kept if 0-24 px, else 8 px;
no usable Take all → horizontal, 8 px). `pitch` = Place stacks' width (or height for a vertical row) + gap.

- **Row A** (default): Sort at `PlaceStacks + pitch`, criterion at `PlaceStacks + 2·pitch` (the row continues).
- **Row B** (second row): a new row on the side of the vanilla buttons **away from the container grid**
  (row height + 6 px): criterion under Place stacks, Sort under the slot before it (under Take all).
- If the buttons' parent has a `LayoutGroup`, it places them and we only check.

**Layout self-check** (coroutine on `gui`, once per `(container width, container height, player inventory rows)`
combination per session):

1. Wait until `m_container` is active in the hierarchy, for up to **2 s of unscaled time** (the panel only appears
   once this client owns the ZDO, which on a server can be many frames after `Show`); then one more frame
   (`UIGamePad.Start` ran). Panel never came up, or another container is open now → forget the combination and try
   again at the next opening.
2. `FixPadGroups` at once.
3. If the panel's `lossyScale` is still collapsed (x or y below 1e-3: the open animation can start near zero scale,
   where every rect is one point and the overlap test sees nothing), wait up to 60 frames; still collapsed → judge
   nothing, store nothing, try again at the next opening. (1e-3 and not a larger threshold: the overlap tests do not
   depend on scale, and a small GUI scale must still be checked.)
4. `CheckLayout`: `Canvas.ForceUpdateCanvases()`, try row A; a **problem** is an overlap between our two buttons, or
   with Take all, Place stacks, the container name, the container weight, the container grid or its scrollbar, or a
   button outside the panel (the panel rect grown to hold the vanilla buttons). A has no problem → keep A. Else try B;
   B has none → keep B and log once at Info `No room for the Sort buttons next to Place stacks on a <w>x<h> container
   (<problem>): they go in a second row.` Neither → back to A and one Warning `Sort buttons <problem> on a <w>x<h>
   container with <rows> inventory rows; another UI mod may have moved the panel.` The choice is remembered for the
   combination and re-applied at every opening.
5. Wait until the panel stops moving/scaling (2 stable frames, at most 60), then the screen check (overlay canvas
   only: world units = pixels): a button off screen → the same Warning with `are off screen`. Then the Debug layout
   dump (3.12).

The check never runs per frame for long and never touches the vanilla buttons.

Sketch (row A, expected):

```
+------------------------------------------------------------------+
| Chest                                                  [w] 42    |
| [][][][][]                                                       |
| [][][][][]                                                       |
| [Take all] [Place stacks] [Sort] [By type]                       |
+------------------------------------------------------------------+
```

**Visibility**: `OnShow` sets both GameObjects active iff `m_currentContainer != null` (Unity null) `&&
IsEligible(c)` (3.6). Because they are children of the container panel they are hidden with it (no container,
walked away, ownership lost: `UpdateContainer` hides `m_container`).

**Input focus**: no text field, so nothing to block. Mouse: normal `Button`. Gamepad: see the map above. Keyboard:
none (non-goal).

**Feedback**: when the sort wrote something, `gui.m_moveItemEffects.Create(player position)` once (the sound Place
stacks makes). No message. Nothing at all when already sorted. When the local copy is out of date (3.4), a center
message: "This container changed elsewhere. Close it and open it again to sort."

### 3.4 Sorting algorithm (`ContainerSorter`)

`TrySortOpenContainer()` (called by the Sort button):

1. `gui = InventoryGui.instance`, `player = Player.m_localPlayer`; return if either is null, `player.IsTeleporting()`,
   or the split dialog is open.
2. `container = gui.m_currentContainer`; return if null; if `!container.IsOwner()` → Debug `Sort skipped: this client
   does not own the open container.` and return (MultiUserChest-style mods, ship owner changed); return if
   `!IsEligible(container)`.
3. `LocalCopyCurrent(container)` false → Debug `Sort skipped: local copy of the container is out of date.`, center
   message "This container changed elsewhere. Close it and open it again to sort.", return. Nothing is written.
   - `LocalCopyCurrent`: the ZNetView must be valid. `stored = ZDO.GetByteArray(ZDOVars.s_items)`; null → true
     (nothing ever saved, nothing newer to overwrite). `local = Inventory.Save` bytes of the open container; equal to
     `stored` → true (always the case right after any own save). Else `stored` is loaded into a scratch
     `Inventory(name, null, width, height)` through vanilla `Inventory.Load` and saved again; equal to `local` → true
     (a copy loaded from the same bytes: old item format, durability round trip and stack clamp give the same result),
     else false. The scratch inventory has no `m_onChanged` subscriber and is not the player inventory, so its `Load`
     never saves and never shows the cheated-item message; it only instantiates and destroys item prefabs, as
     vanilla's `Container.Load` does. Runs only on a click. An exception propagates to the click handler's
     `PatchGuard.Report` before anything is written.
4. `gui.SetupDragItem(null, null, 1)` (cancel a drag, exactly like `OnStackAll`).
5. `Sort(container.GetInventory(), SortBy, MergeStacks, run)` inside `try`; in `finally`, if anything was written:
   `inventory.Changed()` (private, publicized: direct call) and the move sound. Even after an error the state is valid
   (stacks only moved between identical items), so what was written is saved.
6. Debug: `Sorted <name> by <criterion>: <n> items, <moved> moved, <merged> stacks merged` (+ `(already sorted,
   nothing saved)`).

`Sort(inventory, criterion, merge, run)`:

1. `items = inventory.GetAllItems()` (live list), `w`, `h`. Nothing to do if empty.
2. `merge` → `MergeStacks` (3.5).
3. If `items.Count > w * h`: one Warning per game run (`'<name>' holds more items (<n>) than slots (<w>x<h>); sorting
   skipped.`) and stop (never lose an item; only possible with broken legacy data).
4. All keys first (they can throw: localization, biome index), write only after: per item a `SortKey { Item, Biome,
   Type, Sub, Name, Prefab, Slot, ListIndex }` with `Name = Localization.instance.Localize(m_shared.m_name)`,
   `Biome` only for the Biome criterion, `Slot = y * w + x` of the current position. `Array.Sort` with the comparator.
5. For `i` in order: `pos = (i % w, i / w)`; if `m_gridPos != pos` → written, moved++; assign.
6. Reorder the live list to the sorted order, so the saved data is in slot order too. List order alone is invisible
   and does not count as written.

**Comparator** (first non-zero wins; a total order, so sorting again moves nothing):

| Step | Name | Type | Biome |
|---|---|---|---|
| 1 | | | biome rank asc (Unknown last) |
| 2 | | type rank asc | type rank asc |
| 3 | | sub rank asc | sub rank asc |
| 4 | name | name | name |
| 5 | quality desc | same | same |
| 6 | stack desc | same | same |
| 7 | prefab name, ordinal | same | same |
| 8 | durability desc | same | same |
| 9 | variant asc | same | same |
| 10 | old slot asc | same | same |
| 11 | old list index asc | same | same |

Name comparison: `CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, IgnoreCase | IgnoreNonSpace)` (same result
on every machine whatever the OS culture; "Épée" sorts with "E").

**Type ranks** (`TypeGroups.Rank(SharedData) → (type rank, sub rank)`), built on `MC.Shared.ItemKinds.Classify`
(added by Crafting Search and Sort; this mod calls it directly and does not change `src/Shared`). Every group of the
crafting mod's sort menu stays one block in the chest, so an item lands in the same group in both mods:

| Type rank | Group | `ItemKind` | Sub rank | Crafting Search and Sort menu group |
|---|---|---|---|---|
| 0 | Weapons | `Weapon` | by `m_skillType`: Swords 0, Axes 1, Clubs 2, Knives 3, Spears 4, Polearms 5, Unarmed 6, Bows 7, Crossbows 8, ElementalMagic 9, BloodMagic 10, anything else 11 | Weapons (and its weapon types) |
| 1 | Ammo | `Ammo` | 0 | Ammo |
| 2 | Shields | `Shield` | 0 | Shields |
| 3 | Armour | `Helmet`, `Chest`, `Legs`, `Hands`, `Cape` | Helmet 0, Chest 1, Legs 2, Hands 3, Cape 4 | Armor (Helmets, Chest armor, Leg armor, Capes) |
| 4 | Utility and trinkets | `Utility`, `Trinket` | Utility 0, Trinket 1 | Utility, Trinkets |
| 5 | Tools and light | `Tool`, `SkillTool`, `Torch` | Tool 0, SkillTool 1, Torch 2 | Tools and light |
| 6 | Food and potions | `Food`, `Potion` | Food 0, Potion 1 | Food, Meads and potions |
| 7 | Materials | `Material` | 0 | Materials |
| 8 | Fish | `Fish` | 0 | Fish |
| 9 | Trophies | `Trophy` | 0 | Trophies |
| 10 | Other | `Misc` (tankards), `Other` | Misc 0, Other 1 | Other |

`ItemKinds` rules in short: item type first (ammo, shield, armour slots, utility, trinket, tool, torch, consumable →
Food if it restores health, stamina or eitr else Potion, material, fish, trophy, misc); a weapon-type item with a
`Torch`/`LeftTorch` animation is a Torch, with a `FishingRod`/`Scythe` animation or a `Pickaxes`/`WoodCutting`/
`Fishing`/`Farming` skill a SkillTool, with the `Feaster` animation (tankards) Misc, else a Weapon. The prefab values
of the vanilla tools, torch and tankards are **unverified**; the T14 Debug dump prints them.

### 3.5 Merge partial stacks (`MergeStacks`, option, default on)

Lossless by construction: two items merge **only if they would save identically apart from `m_stack` and
`m_gridPos`**.

`CanMerge(a, b)`: `a.m_shared.m_maxStackSize > 1` and all of: same `m_shared.m_name` (ordinal); same prefab name
(`ItemPrefab.Name`, 3.7); same `m_quality`, `m_worldLevel`, `m_variant`, `m_crafterID`, `m_crafterName` (ordinal), the
**saved** durability `(int)(d * 100f)` (the value `ItemData.Save` writes; a float compare would refuse an in-memory
item and an identical reloaded one), `m_pickedUp`, `m_cheated`, `m_equipped`; `CustomDataEqual` (null or empty on
both, or same count and every key has the same value, ordinal).

Algorithm (in current slot order, so the top-left stacks fill first): for each `a` with `0 < a.m_stack < max`, for
each later `b` with `b.m_stack > 0` and `CanMerge(a, b)`: `move = min(max − a.m_stack, b.m_stack)`, `a += move`,
`b −= move`, stop when `a` is full. Then the items **we** emptied (reference compare) leave the live list (no
`RemoveItem`: that would call `Changed` per item; a zero stack from old data stays where it is). Never raise a stack
above `m_maxStackSize`: a stack already above it (from a removed stack-size mod) is never topped up, but as a later
stack it can still give items to a partial stack of the same item (it may stay above the maximum; vanilla clamps it on
the next load).

Consequences: total count per item unchanged; weight unchanged; a cheated stack never taints a clean one and a clean
one never launders a cheated one; EpicLoot / MC custom data never merged away (Crossbow Stays Loaded's
`MC.Combat.Crossbow.StaysLoaded.*` keys included; crossbows do not stack anyway). Stricter than vanilla's own merges
(decision 6).

### 3.6 Which containers

`IsEligible(Container c)`: `c != null && c.GetInventory() != null && c.GetComponent<TombStone>() == null`.

- Included: every chest (`piece_chest_wood`, `piece_chest` reinforced, `piece_chest_private` personal,
  `piece_chest_barrel`, `piece_chest_blackmetal`, and any other chest piece), `Cart`, `Karve`, `VikingShip` (Longship),
  `VikingShip_Ashlands` (Drakkar), `incinerator` (Obliterator), dungeon/loot chests, `Corpse` containers, modded
  containers opened through `InventoryGui.Show(container)`. Sizes are prefab data (**unverified**; the `Opened <name>
  <w>x<h>` Debug line shows them in game; nothing in the code depends on them).
- Excluded: tombstones (decision 5). Never the player inventory (non-goal).

### 3.7 Biome index (`BiomeIndex`, `ItemPrefab`)

Maps an item to a biome rank: `Meadows 0, BlackForest 1, Swamp 2, Ocean 3, Mountain 4, Plains 5, Mistlands 6,
Ashlands 7, DeepNorth 8, Unknown 9` (decision 3). Rule: **the biome where players normally obtain the item first.**

**Lookup**: `ItemPrefab.Name(item)` = `item.m_dropPrefab.name`, or (no drop prefab: an item another mod built by hand)
the first `ObjectDB` item with the same `m_shared.m_name` token (map rebuilt when `ObjectDB.instance` or its item count
changes); then the index; missing → Unknown.

**Build** (lazily, on the first "By biome" sort, and again whenever `ObjectDB.instance` is a different object; never
at plugin start). Each stage runs in its own `try` (one failing stage logs a Warning; the others still run):

1. **Curated table** (`BiomeTable`, 3.8): final entries.
2. **World scan** (fills only items not in the table; each hit keeps the **earliest** rank):
   - `FromMask(mask)`: the first biome of the progression order present in the mask, else skipped.
   - Vegetation: `ZoneSystem.instance.m_vegetation` (`m_enable && m_prefab != null`), `CollectDrops(m_prefab)` with a
     fresh visited set per entry: the prefab itself if it is an item; else, on it or its children, `Pickable`
     (`m_itemPrefab`, `m_extraDrops`), `DropOnDestroyed`, `MineRock`, `MineRock5` drop tables, `TreeBase` (drops and
     `m_logPrefab`, recursed), `TreeLog` (drops and `m_subLogPrefab`, recursed), `Destructible.m_spawnWhenDestroyed`
     (recursed). Depth ≤ 3. Only GameObjects with an `ItemDrop` count as items. A broken modded prefab is skipped
     (first three logged at Debug).
   - Creatures: spawn lists of `ZoneSystem.m_zoneCtrlPrefab`'s `SpawnSystem` ∪ every loaded `SpawnSystemList`
     (deduplicated); each enabled `SpawnData` with a prefab: the prefab itself if it is an item (fish), and its
     `CharacterDrop.m_drops[].m_prefab`.
3. **Derivation** (recipes, conversions, stations), never overwriting table entries. A **candidate** = one way to
   obtain an output item: inputs (items) plus an optional station piece.
   - Recipes: every enabled `Recipe` in `ObjectDB.instance.m_recipes` with an item. Inputs = `m_resources` with
     `m_resItem != null && m_amount > 0` (**base craft only**: upgrade-only materials would push an item later than
     where it is first crafted; a recipe with no such input gives no candidate). Station = `m_craftingStation` (null =
     hand craft). Value = **max** of the input ranks (`m_requireOnlyOneIngredient` → **min**), then
     `max(value, station rank)`.
   - Conversions: `ZNetScene.instance.m_prefabs` scanned once for `Smelter` (smelter, blast furnace, charcoal kiln,
     windmill, spinning wheel, eitr refinery…), `CookingStation` and `Fermenter`; each `m_conversion` entry is a
     candidate with input `m_from`, station = that piece, output `m_to`.
   - **Station rank** = max rank of the station piece's `Piece.m_resources` (`m_amount > 0`), **recomputed every
     pass** (the stations' own materials are often derived themselves: forge = copper, fermenter = bronze…).
   - **Pass A (exact)**: a candidate counts only when **every** input **and every station material** is known ("one
     of" recipes: at least one input known). Full passes until nothing changes (cap 20, Warning if hit). Ranks only
     decrease, so the result is the exact min-max over all candidates.
   - **Pass B (fallback, items still Unknown)**: unknown inputs and station materials are ignored (at least one input
     known); compute all, then write, then Pass A again; repeat until a sweep assigns nothing (cap 20). Pass B values
     are logged as `Derived?` (approximate).
4. Log at Info: `Biome index built in <ms> ms: <t> table, <s> scan, <d> derived (<a> approximate), <u> items
   unknown.` At Debug, one line per ObjectDB item: `Biome index: <prefab> -> <Biome> (<Table|Scan|Derived|Derived?>) |
   type <itemType>/<skillType>/<animationState> -> <typeRank>.<subRank> <group>` (or `-> Unknown`), plus one line
   listing table names that are not items of this game (typo or removed by an update).

Examples (recipes and station costs are prefab data, taken from the wiki: **unverified**; the T14 dump shows the real
result): `SwordBronze` (Bronze + Wood at the forge; Bronze from Copper + Tin) → Black Forest; `ArmorIronChest` →
Swamp; `BoarJerky` (boar meat + honey, both Meadows, but made at the cauldron, which needs Tin) → Black Forest;
`MeadHealthMinor` (fermenter, which needs Bronze, from its mead base made at the cauldron) → Black Forest;
`CookedMooseMeat` → Deep North; `Bell` (Black Forge, from `BellFragment`) → Ashlands.

### 3.8 Curated biome table (vanilla 1.0.16, 355 prefab names)

All prefab names below exist in the installed 1.0.16 game data (`manifest_extended`, checked 2026-09-28) and in the
JotunnDoc item list. Biome evidence: **✓** = the item's source was checked on valheim.weirdgloop.org; **R** = assigned
by a rule of this design (trader location: Hildir = Meadows, Haldor = Black Forest, Bog Witch = Swamp; Hildir quest
items by their dungeon; feasts and spices by the biome in their name; progression decisions, decision 3); **U** = biome
**unverified** (memory or asset path only); no mark = standard, well-known vanilla fact. Crafted items (bars, food,
meads, weapons, armour, casts, idols…) are **not** in the table, except the feasts (rule R): derivation (3.7) handles
them. Deliberately left out
(scan/derivation/Unknown decide): `MushroomBlue`, `TrophyFrostTroll`, `TrophyDeerWhite`, `Larva`, `Voidplasm`,
`AxeHead1`, `AxeHead2`, `VegvisirShard_Bonemass`, `HealthUpgrade_*`, `StaminaUpgrade_*`.

| Biome | Prefab names |
|---|---|
| **Meadows** (34) | `Wood`, `Stone`, `Flint` ✓, `Resin` ✓, `Feathers` ✓, `LeatherScraps`, `DeerHide`, `DeerMeat`, `RawMeat`, `NeckTail` ✓, `HardAntler` ✓, `Raspberry` ✓, `Mushroom` ✓, `Dandelion` ✓, `Honey` ✓, `QueenBee` ✓, `Acorn` ✓, `BeechSeeds` ✓, `BirchSeeds` ✓, `StoneRock` ✓, `Amber` ✓, `AmberPearl` ✓, `Coins` ✓, `SilverNecklace` ✓, `BarberKit` R, `Ironpit` R, `FeastMeadows` R, `FeastMeadows_Material` R, `TrophyBoar`, `TrophyDeer`, `TrophyNeck`, `TrophyEikthyr`, `Fish1` ✓, `Fish2` ✓ |
| **Black Forest** (46) | `FineWood` R, `RoundLog` ✓, `PineCone` ✓, `FirCone` ✓, `CopperOre` ✓, `TinOre` ✓, `SurtlingCore` ✓, `Coal` R, `GreydwarfEye`, `TrollHide`, `BoneFragments`, `AncientSeed` ✓, `Thistle` ✓, `Blueberries` ✓, `CarrotSeeds` ✓, `Carrot` ✓, `MushroomYellow` ✓, `Ruby` ✓, `Ectoplasm` ✓, `CryptKey` ✓, `Pukeberries` ✓, `BjornHide` ✓, `BjornMeat` ✓, `BjornPaw` ✓, `BeltStrength` R, `BarrelRings` R, `Thunderstone` ✓, `YmirRemains` ✓, `ChickenEgg` R, `ChickenMeat` R, `HildirKey_forestcrypt` R, `chest_hildir1` R, `SpiceForests` R, `FeastBlackforest` R, `FeastBlackforest_Material` R, `TrophyGreydwarf`, `TrophyGreydwarfBrute`, `TrophyGreydwarfShaman`, `TrophyForestTroll`, `TrophySkeleton`, `TrophySkeletonPoison`, `TrophyGhost`, `TrophyTheElder`, `TrophyBjorn` ✓, `TrophySkeletonHildir` R, `Fish5` ✓ |
| **Swamp** (38) | `IronScrap`, `IronOre` ✓, `Guck` ✓, `Bloodbag` ✓, `Entrails` ✓, `Ooze` ✓, `WitheredBone` ✓, `ElderBark` ✓, `Root`, `Chain` ✓, `TurnipSeeds` ✓, `Turnip` ✓, `WrithanRoots` ✓, `Wishbone`, `CandleWick` ✓R, `BlobVial` ✓R, `CuredSquirrelHamstring` ✓R, `FragrantBundle` ✓R, `FreshSeaweed` ✓R, `VineGreenSeeds` ✓R, `PowderedDragonEgg` ✓R, `PungentPebbles` ✓R, `ScytheHandle` ✓R, `MushroomBzerker` ✓R, `FeastSwamps` R, `FeastSwamps_Material` R, `TrophyDraugr`, `TrophyDraugrElite`, `TrophyDraugrFem`, `TrophyBlob`, `TrophyLeech`, `TrophySurtling`, `TrophyAbomination`, `TrophyWraith`, `TrophyBonemass`, `TrophyKvastur` ✓, `TrophyWrithan` ✓, `Fish6` ✓ |
| **Ocean** (9) | `SerpentMeat`, `SerpentScale`, `Chitin` ✓, `SpiceOceans` R, `FeastOceans` R, `FeastOceans_Material` R, `TrophySerpent`, `Fish3` ✓, `Fish8` ✓ |
| **Mountain** (29) | `SilverOre` ✓, `Obsidian` ✓, `Crystal` ✓, `WolfPelt` ✓, `WolfMeat`, `WolfFang` ✓, `WolfClaw` ✓, `WolfHairBundle` ✓, `FreezeGland` ✓, `DragonTear` ✓, `DragonEgg` ✓, `OnionSeeds` ✓, `Onion` ✓, `JuteRed` ✓, `HildirKey_mountaincave` R, `chest_hildir2` R, `SpiceMountains` R, `FeastMountains` R, `FeastMountains_Material` R, `TrophyWolf`, `TrophyFenring`, `TrophyHatchling`, `TrophySGolem`, `TrophyCultist`, `TrophyUlv`, `TrophyDragonQueen`, `TrophyCultist_Hildir` R, `TrophyBlob_Frost` U, `Fish4_cave` ✓ |
| **Plains** (28) | `Barley` ✓, `Flax` ✓, `Cloudberry` ✓, `Tar` ✓, `Needle` ✓, `LoxMeat`, `LoxPelt` ✓, `BlackMetalScrap` ✓, `GoblinTotem` ✓, `YagluthDrop` ✓, `UndeadBjornRibcage` ✓, `RottenMeat` ✓, `HildirKey_plainsfortress` R, `chest_hildir3` R, `SpicePlains` R, `FeastPlains` R, `FeastPlains_Material` R, `TrophyDeathsquito`, `TrophyGoblin`, `TrophyGoblinBrute`, `TrophyGoblinShaman`, `TrophyGoblinKing`, `TrophyGrowth` ✓, `TrophyLox`, `TrophyGoblinBruteBrosBrute` R, `TrophyGoblinBruteBrosShaman` R, `TrophyBjornUndead` ✓, `Fish7` ✓ |
| **Mistlands** (34) | `BlackMarble` ✓, `BlackCore` ✓, `Sap` ✓, `Softtissue` ✓, `YggdrasilWood` ✓, `Carapace` ✓, `Mandible` ✓, `BugMeat`, `RoyalJelly` ✓, `QueenDrop` ✓, `GiantBloodSack` ✓, `Bilebag` ✓, `ScaleHide` ✓, `HareMeat`, `DvergrKeyFragment` ✓, `DvergrNeedle` ✓, `CopperScrap` ✓, `Hook` ✓, `JuteBlue` U, `Wisp` R, `MushroomJotunPuffs` ✓, `MushroomMagecap` ✓, `SpiceMistlands` R, `FeastMistlands` R, `FeastMistlands_Material` R, `TrophyDvergr`, `TrophyGjall`, `TrophyHare`, `TrophySeeker`, `TrophySeekerBrute`, `TrophySeekerQueen`, `TrophyTick` ✓, `Fish9` ✓, `Fish12` ✓R |
| **Ashlands** (58) | `Blackwood` ✓, `CharcoalResin` ✓, `Grausten` ✓, `FlametalOreNew` ✓, `FlametalOre` U, `Flametal` U, `SulfurStone` ✓, `ProustitePowder` ✓, `MorgenHeart` ✓, `MorgenSinew` ✓, `MoltenCore` ✓, `CharredBone` ✓, `Charredskull` ✓, `CharredCogwheel` ✓, `AskHide` ✓, `AskBladder` ✓, `AsksvinMeat`, `AsksvinCarrionNeck` U, `AsksvinCarrionPelvic` U, `AsksvinCarrionRibcage` U, `AsksvinCarrionSkull` U, `AsksvinEgg` U, `VoltureMeat`, `VoltureEgg` ✓, `BoneMawSerpentMeat`, `BonemawSerpentScale`, `BonemawSerpentTooth` ✓, `CelestialFeather` ✓, `FaderDrop`, `FaderEmber` ✓R, `DyrnwynBladeFragment` U, `DyrnwynHiltFragment` U, `DyrnwynTipFragment` U, `BellFragment` ✓, `GemstoneBlue` ✓, `GemstoneGreen` ✓, `GemstoneRed` ✓, `BronzeScrap` ✓, `Pot_Shard_Green` ✓, `Pot_Shard_Red` U, `Fiddleheadfern` ✓, `Vineberry` ✓, `VineberrySeeds` ✓, `MushroomSmokePuff` ✓, `SpiceAshlands` R, `FeastAshlands` R, `FeastAshlands_Material` R, `TrophyAsksvin`, `TrophyBonemawSerpent`, `TrophyCharredArcher`, `TrophyCharredMage`, `TrophyCharredMelee`, `TrophyFader`, `TrophyFallenValkyrie`, `TrophyMorgen`, `TrophyVolture`, `TrophyBlob_Lava`, `Fish11` ✓ |
| **Deep North** (79) | `Frostwood` ✓, `FirConeFrost` ✓, `BarkaBranch` ✓, `Ice` ✓, `FrozenFuel` ✓, `FrostCore` ✓, `GoldOre` ✓, `MooseHide` ✓, `MooseMeat` ✓, `MooseSinew` ✓, `SealBlubber` ✓, `SealHide` ✓, `ElakingHairBundle` ✓, `MoleClaws` ✓, `NornThread` ✓, `Leatherstraps` ✓, `MemorialCoal` ✓, `OozeMork` ✓, `OrbFrostFire` U, `OrbThunderBlood` U, `CrownJewel` ✓, `FrozenKingDrop` ✓, `HatefulBlood` ✓, `AncientCoin` ✓, `AncientGemstoneBlack` ✓, `AncientGemstoneGreen` ✓, `AncientGemstoneOrange` ✓, `AncientGemstonePurple` ✓, `GlowWorm` ✓, `Kale` ✓, `KaleSeeds` ✓, `Oat` ✓, `OatSeeds` ✓, `Poteitr` ✓, `PoteitrSeeds` ✓, `Lingonberry` ✓, `LastBossGate_RuneTile` U, `SpiceDeepNorth` R, `FeastDeepNorth` R, `FeastDeepNorth_Material` R, moulds (30, guide: Deep North dungeon loot): `MoldArmorGoldChest`, `MoldArmorGoldHelmet`, `MoldArmorGoldLegs`, `MoldArmorMageChest`, `MoldArmorMageHelmet`, `MoldArmorMageLegs`, `MoldArmormediumChest` (sic, lower-case m), `MoldArmorMediumHelmet`, `MoldArmorMediumLegs`, `MoldAtgeir`, `MoldAxe`, `MoldAxe2H`, `MoldBow`, `MoldCrossbow`, `MoldFistweapon`, `MoldKeys`, `MoldKnife`, `MoldMace`, `MoldMace2H`, `MoldShieldBuckler`, `MoldShieldRound`, `MoldShieldTower`, `MoldSmallParts`, `MoldSpear`, `MoldStafffrostorbs`, `MoldStaffOrbofAhri`, `MoldStaffspiritcaller`, `MoldStaffthunderblood`, `MoldSword`, `MoldSword2H`; `TrophyBarka`, `TrophyBlob_Morkhalla`, `TrophyElaking`, `TrophyJotunWarrior`, `TrophyJotunWitch`, `TrophyMole`, `TrophyMoose` ✓, `TrophySeal` ✓, `Fish10` ✓ |

Notes on individual entries: `Fish2` (Pike) lives in Meadows and Black Forest → Meadows. `Fish12` (Pufferfish) lives
in the deep Ocean and on Mistlands shores and needs Misty bait → Mistlands. `Hook`: the wiki says Mistlands chests
(infested mines, Dvergr towers); a 1.0 guide lists it with Deep North materials → Mistlands (earliest). `FaderEmber`
(Embers): collected from an Eternal Pyre after killing Fader → Ashlands (obtained before reaching the Deep North).
`WrithanRoots` / Writhan: the wiki says Swamp. `IronOre`: legacy item still obtainable (`Pickable_BogIronOre` exists;
Giant Herring bonus).

### 3.9 Configuration

Section `General`, `Enabled` first (framework) and `Status` (framework).

| Section | Setting | Type / default | Description (user-facing) |
|---|---|---|---|
| General | Enabled | bool `true` | (framework) Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | string, read-only | (framework) Written by the mod: shows whether the feature is active, and if not, why. Editing it has no effect. |
| General | SortBy | enum `SortCriterion { Name, Type, Biome }`, default `Type` (decision 2) | How the Sort button orders a container. Name: alphabetical in your game language. Type: weapons, ammo, shields, armour, utility and trinkets, tools and light, food and potions, materials, fish, trophies, then everything else; inside a group by kind (weapon kind, armour slot...), then alphabetical. Biome: Meadows, Black Forest, Swamp, Ocean, Mountain, Plains, Mistlands, Ashlands, Deep North, then items of unknown biome; by type and name inside each biome. The button next to Sort changes this setting. (Order 90) |
| General | MergeStacks | bool `true` | Before sorting, combine partial stacks of the same item into full stacks. Stacks only combine when the items are exactly the same (quality, world level, crafter, extra data from other mods, cheated mark), so nothing is ever lost. (Order 80) |

`SortBy.SettingChanged` (subscribed in `OnActivated`, removed in `OnDeactivated`) refreshes the labels/tooltips live
when the setting is edited in ConfigurationManager, the MC Mods panel or the file. `SortCriterion` names are config
values: never rename them.

### 3.10 Persistence

- The criterion: BepInEx config (per machine, shared by all characters). Clicking the criterion button sets
  `SortBy.Value` (BepInEx saves the file; the framework's watcher reloads it harmlessly).
- Nothing on items, containers or characters: the sort only changes `m_gridPos`, `m_stack` and list order, which
  vanilla already saves in `ZDOVars.s_items`. No ZDO key, no custom data, no RPC.

### 3.11 Multiplayer and hand-off

- Client-side, compatible. Only the player who opened the container, **owns its ZDO at click time and whose local
  copy matches the saved data** runs the sort; the vanilla save path publishes the new layout; other clients reload
  it (`Container.CheckForChanges`) whether or not they have the mod. A player without the mod who opens the chest later
  sees a normal, sorted chest (M01, M02).
- **House rule "client-side mods only change the local player's own state"**: this mod rewrites a world container,
  but only through the vanilla owner path that any manual item move in an open chest uses
  (`Container.OnContainerChanged` → `Save`), only while the local player has that chest open and owns it, and only in
  ways a player could do by hand (move items, combine identical stacks). The rule's intent (no client writing state it
  does not own, no stale overwrite) is kept (decision 11). The README multiplayer section says it in one sentence.
- While we have the chest open **and own it**, other players get "in use" when they try to open it, when their
  tombstone take-all targets it, or when they pull the obliterator lever (M03). There is no way to Place-stacks into a
  chest from outside (`Container.Interact` ignores hold; `Container.StackAll` is only called by the open panel's "hold
  Use").
- Ship storage opened from the dock while someone else is aboard: `Ship.UpdateOwner` hands the ship ZDO (also the
  container's `m_nview`) to a player aboard within 2 s; the panel hides (vanilla), the buttons hide with it, and a
  Sort that was somehow triggered does nothing (click-time `IsOwner()` check); nothing is written (M05). If that
  player later owns and opens the storage again while the local copy is still the old one (stuck `m_inUse`, 1.3), Sort
  refuses with the center message and writes nothing; closing and reopening the storage loads the current content
  (M06). Vanilla item moves in that state are not guarded (vanilla behaviour, not changed by this mod).
- MultiUserChest-like mods (panel shown while not owner): Sort does nothing (Debug log). Safe by construction.
- Dedicated servers do not need the mod (the server only stores the ZDO bytes).

### 3.12 Debug aids (Debug log level only)

- `Sort buttons created.` (with `(parent has a layout group: it places them)` when relevant);
  `Opened <root prefab>[/<child>] <w>x<h>` at each opening (container sizes, T09).
- Once per size combination, after the panel settled: `Container panel layout (<w>x<h> container, <rows> inventory
  rows, screen <W>x<H>):` then, for `m_container`, Take all, Place stacks, Sort, Criterion, name, weight, grid, grid
  root, scrollbar, `m_player`: path, active, under the container panel, anchors, pivot, position, size, rect, world
  rect; the components on the buttons' parent and on `m_container`; the root canvas mode; `m_uiGroups`; and each
  button's `UIGamePad` (key, keyCode, hint, alternative group, parent group, current group, blocking elements). This
  is the data that confirms the layout and the vanilla keys in game (T12, T13).
- `<button>: controller group '<x>' -> chest grid group '<y>'.` when `FixPadGroups` changes a pad's group.
- Sort: `Sorted …`, `Sort skipped: this client does not own the open container.`, `Sort skipped: local copy of the
  container is out of date.`
- Biome index: the Info summary and one Debug line per item (3.7, T14).

### 3.13 Performance

- No per-frame patch. `Show` postfix: a few Unity null checks, `SetActive`, a few `Vector2` operations (layout), one
  Debug line, per opening. The self-check coroutine runs only for a size combination not seen before in the session
  (a few frames, one `Canvas.ForceUpdateCanvases`, a few dozen corner compares, one Debug dump).
- Sort click: n ≤ a few dozen items; one key array, one `Array.Sort`, one localization lookup per item, one
  `Inventory.Save` for the freshness check (plus one scratch load/save only when the bytes differ), one `Changed()`,
  one ZDO write.
- Biome index: built once per world session on the first biome sort (a few thousand `GetComponent` calls, a few
  hundred recipes and conversions), logged with its time. Never built if the player never sorts by biome.

### 3.14 Files

| File | Responsibility |
|---|---|
| `MC.UX.Container.Sort.csproj` | Metadata, `<ModIdea>Sort chest</ModIdea>`. |
| `Plugin.cs` | `BindConfig` (`SortBy`, `MergeStacks`); `OnActivated`: subscribe `SortBy.SettingChanged`, create the buttons and run `OnShow` when an `InventoryGui` exists (buttons appear at once with a chest open, T15); `OnDeactivated`: unsubscribe, destroy the buttons, clear the biome index and the prefab map. |
| `SortCriterion.cs` | `enum SortCriterion { Name, Type, Biome }` (config values; never rename). |
| `Patches/InventoryGuiPatches.cs` | `Awake` postfix, `Show(Container, int)` postfix (3.2). |
| `SortChestUi.cs` | Clone, layout A/B, self-check, labels, tooltips, gamepad keys, glyphs and groups, click listeners, Debug dump, `Destroy()`. |
| `ContainerSorter.cs` | `TrySortOpenContainer`, `IsEligible`, `LocalCopyCurrent`, `Sort`, `MergeStacks`, `CanMerge`, comparator. |
| `TypeGroups.cs` | `ItemKinds.Classify` kind → type rank and sub rank (3.4). |
| `BiomeIndex.cs` | `BiomeRank`, lazy build (table, scan, derivation), lookup, Info/Debug log. |
| `BiomeTable.cs` | The curated table (3.8). |
| `ItemPrefab.cs` | Item → prefab name (drop prefab, else ObjectDB token lookup). |

### 3.15 Coordination with other MC mods

- **Crafting Search and Sort**: its row and menu stay inside `m_crafting`, ours inside the container panel. Its menu
  closes on an outside click without a blocker, so a click on our buttons closes it and still reaches our button. It
  binds no gamepad key and blocks every `UIGamePad` while its search field has the keyboard, so typing never fires our
  keys; we have no keyboard key. The type groups are built on the same `ItemKinds.Classify` (C03).
- **Loot Pickup Filter**: its mode button sits above the player panel; its badges are recomputed in an
  `InventoryGrid.UpdateGui` postfix from the live items' `m_gridPos` at every repaint (every frame while the panel
  shows), so they follow sorted items; middle-click marking reads the item in the hovered slot at the moment of the
  click. Its per-item marks live in the character (`Player.m_customData`, by prefab name), not on the items, so sorting
  and merging never touch them. Controller: its gestures only in the player grid, ours only in the container grid
  (gamepad map, 3.3) (C02).
- **Crossbow Stays Loaded**: per-item custom data on crossbows (`MC.Combat.Crossbow.StaysLoaded.*`); sorting never
  touches item data, and merging never merges items whose custom data differ (C01).
- **One Click Repair All, Batch Station Feeding, Harpoon Hooks Tames, Creature Kill and Tame Counts**: unrelated (no
  shared methods, no container state); no cross-mod test.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Chest with gaps and mixed items | Items where players left them | Packed from top left, row by row, in criterion order; empty slots at bottom right |
| Already sorted chest, click Sort | — | Nothing moves, no `Changed()`, no ZDO write, no sound |
| Empty chest | — | Nothing happens |
| 3 Wood stacks 20 + 30 + 15 (MergeStacks on) | Stay 3 stacks | One 50 + one 15, adjacent; total 65 |
| Same, MergeStacks off | — | 3 stacks adjacent, 30, 20, 15 |
| A spawned (cheated) Wood stack + a gathered Wood stack | Drag-merge keeps the target's flag; pickup, Ctrl+click and Place stacks mark the target cheated | Never merged, sorted next to each other; both flags unchanged |
| Items with different custom data (EpicLoot, Crossbow Stays Loaded) | Drag-merge keeps only the target's data | Never merged; custom data untouched |
| Same item, different quality or world level | Separate stacks | Separate; higher quality first |
| Drag in progress (from chest or player inventory) | Take all / Place stacks cancel it | Cancelled first; the item never left its slot, then the sort runs |
| Split dialog open | Modal dialog | Click ignored |
| Player teleporting | Take all refused | Sort refused |
| Tombstone | Take all restores the player's slot layout | No Sort buttons on tombstones |
| Obliterator, cart, ship, barrel, personal chest, dungeon chest, corpse | Normal containers | Sortable |
| Cart being pulled / chest opened by someone else | Open refused "in use" | Unchanged (cannot open, cannot sort) |
| Legacy chest whose rows grew (`UpdateRows`) | Extra rows shown | Items packed into the first rows; the height shrinks back on the next load (vanilla `UpdateRows`) |
| More items than slots (corrupt legacy data) | — | Sort skipped with a warning (merge may still run); nothing lost |
| Stack above `m_maxStackSize` (a stack-size mod was removed) | Clamped on next load | Never topped up by the merge (it can give items to a partial stack of the same item); sorting still works |
| Modded item not in ObjectDB / no prefab | — | Sorted by name/type; biome Unknown (last) |
| Modded item crafted from vanilla materials | — | Biome derived from its recipe and station |
| Modded raw material dropped by a modded creature in a spawn list | — | Biome from the spawn list (scan) |
| Item names in another language | — | Name sort uses the translated names, accent/case-insensitive |
| Container panel not owned (MultiUserChest) | — | Sort does nothing |
| Ship chest opened from the dock while others are aboard | `Ship.UpdateOwner` moves the ship ZDO to a player aboard within 2 s; the panel hides; the dock player's `m_inUse` stays stuck, so their copy stops reloading | Buttons hide with the panel; the click-time `IsOwner()` check makes Sort do nothing (M05). Later, owning and reopening with the old copy: Sort refuses with "This container changed elsewhere. Close it and open it again to sort.", nothing written (M06) |
| Chest opened within about 1 s of another player's change | Local copy may miss that change until the chest is closed (`Load` skipped while in use) | Sort refuses with the same message; reopening fixes it |
| Panel appears many frames after the open (server, other player owned the area) | Panel shows when ownership arrives | Self-check waits up to 2 s; pads get the chest grid group as soon as the panel is up (M07) |
| Open animation still near zero scale | — | Layout not judged until the scale is real; re-tried next opening if it stays collapsed |
| Extra inventory rows (bought, or `inventorysize`) | `m_player` grows (`SetInventorySize`) | Buttons stay in the container panel; layout re-checked for the new combination; warning once if they overlap or leave the screen (T13) |
| No room next to Place stacks | — | Second row, away from the grid (Info line) |
| UI mod removed `m_stackAllButton` (Auga) | — | No buttons, one warning; mod otherwise inert |
| Another sort mod's button at the same place | — | May overlap (known limitation, README) |
| Vanilla button already on View/Select or L3 | — | That Sort button is mouse only, one warning |
| Feature turned off while the panel is open | — | Buttons destroyed immediately |
| Feature turned on while the panel is open | — | Buttons appear immediately (`OnActivated` → `Create` + `OnShow`) (T15) |
| Gamepad: container grid not the active group | — | View/Select and left stick click do nothing for our buttons |
| Gamepad: right stick click on a chest slot | Nothing | Nothing (Loot Pickup Filter reads R3 only in the player grid; Sort never binds `JoyRStick`) (C02) |
| Gamepad: left stick clicked by accident while moving in the chest grid | Grid selection moves | The criterion label changes (no sort, nothing moves); two more presses bring it back |
| Typing in Crafting Search and Sort's field with a chest open | — | Our buttons and keys never fire (C03) |
| Language changed | Vanilla labels relocalize | Our English labels stay English (decision 8) |

---

## 5. Decisions and open questions

### Decisions

All accepted by the lead before implementation. The ones marked **(to confirm with the user)** change what the player
sees and have a simple alternative.

1. **The criterion button only changes the criterion; it does not sort** (to confirm with the user). One action per
   button, predictable, no unexpected ZDO writes. Alternative: the criterion button also sorts immediately.
2. **Default criterion: Type** (to confirm with the user). For a material-only chest Type equals Name; for mixed
   chests it groups equipment, food and materials.
3. **Biome order and rules** (to confirm with the user): Meadows, Black Forest, Swamp, **Ocean** (serpents, chitin,
   deep fish: the iron-age sea; the 1.0 feasts put Oceans between Swamps and Mountains), Mountain, Plains, Mistlands,
   Ashlands, Deep North, then Unknown. "The biome where players normally obtain the item first": `FineWood` and `Coal`
   → Black Forest; trader goods by trader location (Hildir Meadows, Haldor Black Forest, Bog Witch Swamp), so the
   chicken egg counts as Black Forest; Pufferfish → Mistlands; Embers → Ashlands. Inside a biome: type, then name.
4. **Biome data source**: curated table (verified names) + world scan + recipe/conversion derivation, unknown last. A
   table alone would miss modded items; derivation alone cannot place raw materials. The Debug dump makes every
   assignment inspectable.
5. **Tombstones are never sortable** (vanilla-consistent: Take all on a tombstone restores the player's own slots and
   hotbar).
6. **Merge is stricter than vanilla** (vanilla-consistent superset): only items identical in every saved field merge,
   so nothing changes except stack counts.
7. **A drag in progress is cancelled, not a reason to refuse** (vanilla-consistent with Take all / Place stacks); the
   dragged item never left its slot.
8. **English-only labels in 0.1.0** (to confirm with the user): vanilla has no "Sort"/"Type" token; translations can
   come later.
9. **Gamepad keys** (to confirm with the user): **View/Select (`JoyBack`) = Sort**, **left stick click (`JoyLStick`) =
   change criterion**, only while the container grid is focused. The right stick click stays free (no MC mod reads it
   in the container grid; Loot Pickup Filter reads it only in the player grid). If the game's Take all or Place stacks already uses one of
   these keys, that Sort button is mouse only (warning).
10. **Only when something was written**: no save, no sound when already sorted.
11. **A client-side mod that writes a shared container** (to confirm with the user): sorting writes the open chest
    through the vanilla owner path, exactly like moving items by hand, only while the local player has it open, owns
    it and holds its current content (3.11).
12. **Type classification source**: `MC.Shared.ItemKinds` (added by Crafting Search and Sort), called directly; this
    mod adds nothing to `src/Shared`. Misc (tankards) and Other share one "Other" group, like the crafting menu.
13. **Refuse a sort on a stale copy** (added by the review): owning the ZDO is not enough (1.3); Sort compares the
    local copy with the saved bytes and refuses with a center message when they differ. A vanilla item move in that
    state is not guarded (vanilla behaviour).

### Added beyond the request

`MergeStacks` (default on), the remembered criterion (config), gamepad support, the vanilla move sound after a sort,
the stale-copy guard.

### Differences from the pre-implementation design

- **Layout**: instead of one hard-coded candidate chosen from a manual runtime dump, row A is the default and the
  self-check picks A or B per size combination at runtime (B only when A overlaps something), with a warning when
  neither fits. The runtime dump is built in (Debug level).
- **Self-check timing**: waits up to 2 s (unscaled) for the panel instead of 5 frames, fixes the pad groups as soon as
  the panel is up, and never judges a collapsed (open-animation) panel.
- **Clones**: built under an inactive holder; extra child `UIGamePad`s, `UIInputHint` glyph switchers and `Localize`
  components are removed; a tooltip prefab falls back to the inventory slot tooltip.
- **Gamepad**: our pads are moved to the container grid's `UIGroupHandler` (`FixPadGroups`), plus a click-time check
  that ignores a pad press while the container grid is not focused. A vanilla button already on our key makes ours
  mouse only instead of "stop and choose again".
- **Type groups**: Misc and Other are one group ("Other", sub ranks 0 and 1) to match the crafting menu.
- **Stale-copy guard** (`LocalCopyCurrent`) added after the review.
- **Tie-breaks**: old slot, then old list index.
- **Biome index**: scan looks at components on children too; one stage failing does not stop the others; a Debug line
  lists table names that are not items of this game; the Debug item line also carries the type classification.

### Open questions

1. Final display name ("Sort Chest" vs "Chest Sorting"): permanent after release.
2. A button-offset setting if UI mods collide (not added).
3. Translations for the English labels.
4. Whether sorting should be offered for tombstones.
5. Closed: a "Sort bags" mod would have shared the sort and merge routine, but the idea is cancelled in the sheet.

### Unverified names and values

- Container sizes (Chest 5x2, Reinforced 6x4, Black metal 8x4, Personal 3x2, Barrel 6x2, Cart 6x3, Karve 2x2, Longship
  6x3, Drakkar 8x4: wiki only); which prefabs set `m_rootObjectOverride`.
- Which gamepad keys the vanilla Take all / Place stacks use, their `m_blockingElements`, and whether the inventory
  canvas is screen-space overlay (the Debug layout dump shows them).
- The placement of the buttons (row A expected; confirmed only in game, T09, T13).
- The prefab values (`m_itemType`, `m_skillType`, `m_animationState`, food values) of tools, the torch, tankards and
  meads, hence their type group (T14, C03).
- The recipes and station costs behind the derived biomes (T14): e.g. `BoarJerky` at the cauldron, the fermenter's
  Bronze, the forge's Copper.
- The biome entries marked **U** in 3.8. The English names used in T01-T03 exist in the 1.0.16 English text
  (`$item_frostwood` = Timberwood, `$item_flametalore` = Flametal Ore, `$item_chest_iron` = Iron Scale Mail), but which
  token each prefab uses (`Frostwood`, `FlametalOreNew`, `ArmorIronChest`) is prefab data.

---

## 6. Tests

The in-game checklist lives next to the code: `src/UX/Container.Sort/TESTING.md` (run
`./tools/Get-TestTodo.ps1 -Mod Container.Sort`; smoke test `./tools/Test-Smoke.ps1 -Mod Container.Sort`; plain `Sort`
would also match Crafting Search and Sort). It covers every goal
item: T01-T03 (goals 2-3, the three criteria, top left to bottom right), T04 (goal 2, criterion button and memory),
T09-T11 (goal 1, container kinds, tombstone excluded), T15-T16 and T18 (goal 4); T05-T08 merge, already sorted and
drag, T12 gamepad, T13 layout, T14 index log, T17 clean log. M01-M07 multiplayer (M01-M02 hand-off to players without
the mod, M05-M06 ship storage and stale copy, M07 late panel), C01-C03 cross-mod (Crossbow Stays Loaded, Loot Pickup
Filter, Crafting Search and Sort), C04 other sort mods (optional).
