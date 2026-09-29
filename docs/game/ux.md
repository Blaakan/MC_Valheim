# UX & UI

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

Also referenced: `assembly_utils` (ZInput, PlatformPrefs), `assembly_guiutils` (Localization, UITooltip, UIGroupHandler), `gui_framework` (GuiInputField and friends). All names below were checked against the decompiled 1.0.16 code. Private members are marked *(private)*. Reach them with `AccessTools` or `Traverse`.

## Overview

The UX surface splits into a few systems:

| Area | Main classes | Authority |
|---|---|---|
| Item storage model | `Inventory`, `ItemDrop.ItemData` | whoever owns the ZDO holding the inventory (player file for the player, container ZDO owner for chests) |
| Inventory screen (grids, drag, split, containers, crafting, repair) | `InventoryGui`, `InventoryGrid`, `InventoryElement`, `SplitDialog` | local client |
| Containers | `Container` (+ `Vagon`, `TombStone`, `Incinerator`) | ZDO owner (ownership is handed to the player who opens it) |
| Picking up items | `Player.AutoPickup`, `Humanoid.Pickup`, `ItemDrop` | ItemDrop ZDO owner (pickup requests ownership first) |
| Notifications | `MessageHud`, `Hud` | local client (some are broadcast through routed RPCs) |
| Chat, shouts, pings | `Chat` (a `Terminal`), `Talker` | the sender broadcasts; receivers render |
| Map pins and pings | `Minimap` | local client (pins live in the character file; shared through `MapTable`) |
| Input | `ZInput` (Unity Input System), `PlayerController`, `Player.TakeInput` | local |
| UI toolkit | TextMeshPro, `GuiInputField`, `UIGroupHandler`, `UIInputHandler`, `UITooltip`, radial `Valheim.UI.RadialBase` | local |
| Settings / localization / console | `Settings` + `Valheim.SettingsGui.*`, `Localization`, `Terminal`/`Console`/`Chat` | local, except server commands |

Almost every UX feature in this chapter is client-only. A few need care: anything that writes to a container must happen while the local client owns that container's ZDO, and anything that shows new data on other players' screens (pings) needs the mod on those clients.

---

## 1. Inventory model (`Inventory`, `ItemDrop.ItemData`)

### Key classes
- `Inventory`: a grid of `ItemDrop.ItemData`, backed by a flat `List<ItemData> m_inventory` *(private)*. Each item carries its own `m_gridPos`. There is no 2D array, so every `GetItemAt(x,y)` is a linear scan.
- `ItemDrop.ItemData`: per-instance item state plus `m_shared` (`ItemDrop.ItemData.SharedData`, shared from the prefab).
- `Humanoid.m_inventory` is created as `new Inventory("Inventory", null, 8, 4)` (8×4 player grid). `Container.Awake` creates `new Inventory(m_name, m_bkg, m_width, m_height)`.
- `Inventory(bool _)` builds a temporary inventory: no bounds check and no `Changed()`. Only `ZDOMan` uses it, for container data conversion.

### Flow and rules
- **Adding (`Inventory.AddItem(ItemData)`)**: for stackables it loops one unit at a time. It first tops up an existing stack found by `FindFreeStackItem` (same `m_shared.m_name` + `m_quality` + `m_worldLevel`, not full), then puts the remainder into `FindEmptySlot(TopFirst(item))`.
  - `TopFirst` is true for weapons, `Tool`, `Shield`, `Utility`, `Misc` and `Trinket`. Those scan from row 0 (the hotbar) downward. Everything else (materials, food, trophies…) scans from the **bottom row upward**, which is why materials land at the bottom.
- `AddItem(ItemData, int amount, int x, int y)` *(private)* targets an exact slot and merges if `IsSameType`. `AddItem(ItemData, Vector2i)` is public. `AddItem(string name, stack, quality, variant, crafterID, crafterName, Vector2i position, cheated, pickedUp, dropIfFullInv)` is the crafting path: it instantiates the prefab with `ZNetView.m_forceDisableInit` and drops the item at the player's feet if the inventory is full.
- `CanAddItem(item, stack)` = `FindFreeStackSpace(name, worldLevel)` + free slots × max stack.
- **Moving between inventories**:
  - `MoveAll(from)` ("Take all") tries each item's same grid position first, then any slot.
  - `StackAll(from, message)` ("Place stacks") only moves items whose `m_shared.m_name` already exists in the target. It skips items the local player has equipped and shows `$msg_stackall N` / `$msg_stackall_none`.
  - `MoveItemToThis(from, item)` (Ctrl-click) and `MoveItemToThis(from, item, amount, x, y)` (drag/drop).
- `ItemData.IsSameType(other)` compares `m_shared.m_name` + `m_worldLevel`, plus `m_quality` when `m_maxQuality > 1`. **It ignores `m_customData`**, so merging stacks can lose per-item custom data.
- `Inventory.Changed(bool success=false, bool cheatedStateChanged=false)` *(private)* recomputes `m_totalWeight`, shows the 1.0 "picked up cheated item" center messages, then calls `m_onChanged`. Every public mutator calls it, so N single-item operations mean N change events.
- There is **no sort API**. Helper views exist: `GetAllItemsInGridOrder()` (used by the radial menu, `Valheim.UI.ItemGroupConfig` / `HammerItemElement`), `GetAllItemsSortedByName()` (unused), `GetAllItemsOfType(...)`, `GetHotbar(includeEmpty)` (row 0, 8 slots) and `GetBoundItems()`.
- `GetAllItems()` returns the **live list**. Changing `m_gridPos` or `m_stack` on those items directly and then calling `Changed()` once is the cheapest way to rearrange an inventory.

### "Bags" in 1.0
There is no bag or backpack item and no nested inventory in vanilla 1.0. The candidates for "bags" are:
1. **The player inventory itself.** Its height can grow: `Player.SetInventorySize(rows)` clamps to 0–9, sets `Inventory.SetHeight`, stores the unique key `"invrows"` (`Player.InventoryRowsKey`) and resizes the panel through `InventoryGui.SetInventorySize`. `Player.OnSpawned` reapplies it (default `"4"`). Rows are bought through `StoreGui.OnBuyItem` when a trader's `Trader.TradeItem.m_incrementKey == "invrows"`; which trader item uses this still has to be confirmed in game data. There is also a console command `inventorysize` (cheat, server only). Width stays 8.
2. **Carts**: `Vagon.m_container`, a `Container` whose ZNetView comes from `Container.m_rootObjectOverride`. Carts also refuse opening while `Vagon.InUse()`.
3. **Ship storage**: a `Container` on the ship prefab. `Ship.cs` holds no inventory code.
4. **Tombstones** (`TombStone`, which uses `Container.TakeAll`) and the obliterator (`Incinerator.m_container`).

A real nested-bag item would be a "New" feature. It could serialize an `Inventory` into the owning item's `ItemData.m_customData` (string → e.g. base64 of `Inventory.Save`), the same way containers serialize into `ZDOVars.s_items`.

### Data and persistence
- `Inventory.Save(ZPackage)` writes version `109`, a `ushort` count, then `ItemData.Save` per item. That format has compact flags: bit `1` = `m_pickedUp`, bit `128` = has custom data, and so on. `Inventory.Load` handles every older version.
- ItemData fields that matter for UX:
  - `m_stack`, `m_durability`, `m_quality`, `m_variant`
  - `m_worldLevel`: 1.0 world-level scaling. Crafting and counting ignore items below `Game.m_worldLevel`.
  - `m_pickedUp`, `m_crafterID/Name`
  - `m_customData`: a `Dictionary<string,string>` that is saved, synced inside container data, and copied by `Clone()`
  - `m_gridPos`, `m_equipped`
  - `m_cheated`: 1.0 achievements; a cheated flag propagates onto stacks it merges into
  - `m_dropPrefab`
- SharedData fields that matter for UX: `m_name` ($token), `m_itemType`, `m_maxStackSize`, `m_weight`, `m_value`, `m_teleportable`, `m_questItem`, `m_autoStack`, `m_maxQuality`, `m_icons[]`.
- `ItemType` enum values: None 0, Material 1, Consumable 2, OneHandedWeapon 3, Bow 4, Shield 5, Helmet 6, Chest 7, Ammo 9, Customization 10, Legs 11, Hands 12, Trophy 13, TwoHandedWeapon 14, Torch 15, Misc 16, Shoulder 17, Utility 18, Tool 19, Attach_Atgeir 20, Fish 21, TwoHandedWeaponLeft 22, AmmoNonEquipable 23, Trinket 24.

### Multiplayer authority
- Player inventory: the local player. It is saved in the character file (`Player.Save`).
- Container inventory: the container's ZDO owner. The owner writes `ZDOVars.s_items`; everyone else reloads it (see §3).

### Patch points
- `Inventory.Changed` (private): the single funnel for "inventory changed" reactions. A postfix is cheap. **Do not** prefix-skip it: container saving hangs off `m_onChanged`.
- `Inventory.AddItem(ItemData)` / `FindEmptySlot` / `TopFirst` (private): change where new items land, for example "keep hotbar free" or "materials top-first".
- `Inventory.StackAll`, `Inventory.MoveAll`: quick-stack and take-all behaviour.
- `ItemDrop.ItemData.IsSameType`: stacking rules. Global and risky, because it is also used by crafting drag/drop.
- Reverse-patch or reflect `Inventory.Changed` when you rearrange the list yourself.

---

## 2. Inventory screen (`InventoryGui`, `InventoryGrid`, `InventoryElement`, `SplitDialog`)

### Key classes
- `InventoryGui` (singleton `InventoryGui.instance`) owns the whole Tab screen: player grid `m_playerGrid`, container panel `m_container` + `m_containerGrid`, crafting panel `m_crafting`, info panel `m_info`, and the skills / texts / trophies / achievements / variant / split dialogs.
- `InventoryGrid` is a generic grid view over one `Inventory`. It raises delegates `m_onSelected(grid,item,pos,Modifier)`, `m_onRightClick`, `m_onReleased` and `m_onEnter`, plus `OnMoveToUpperInventoryGrid` / `OnMoveToLowerInventoryGrid` (gamepad hops between grids).
- `InventoryElement` is one slot. Public visuals: `m_icon`, `m_amount`, `m_quality`, `m_equiped`, `m_queued`, `m_selected`, `m_noteleport`, `m_food`, `m_tooltip` (a `UITooltip`), `m_durability` (`GuiBar`), `m_dropFocus` and `m_button`, plus the `Position` property.
- `SplitDialog` is the stack-split slider (`m_splitSlider`, `SplitAccepted`/`SplitCanceled` events).

### Flow
- **Open and close.** `InventoryGui.Update` toggles on button `"Inventory"` (Tab) or `"JoyButtonY"` when hidden. It closes on `"Inventory"`, `"JoyButtonB"`, `"JoyButtonY"`, Escape or **`"Use"`** (E). That hotkey block only runs while Chat has no focus, the Console / Menu / TextViewer / Minimap are closed and no craft is in progress (`m_craftTimer < 0`).
- `Show(Container container, int activeGroup = 1)` sets the Animator bool `"visible"`, calls `SetupCrafting()` → `UpdateCraftingPanel(focusView: true)` and stores `m_currentContainer`. `Hide()` clears the drag and dialogs and calls `m_currentContainer.SetInUse(false)`.
- `InventoryGui.IsVisible()` (static) returns `m_hiddenFrames <= 1`.
- **Per frame while visible** (`Update`):
  - `UpdateGamepad`
  - `UpdateInventory` → `m_playerGrid.UpdateInventory(inv, player, dragItem)`
  - `UpdateContainer`
  - `UpdateItemDrag`, `UpdateCharacterStats`, the weight updates, `UpdateSplitDialog`, `UpdateRecipe`, `UpdateRepair`.
- **Gamepad groups.** `m_uiGroups[]` holds `UIGroupHandler`s: index 0 = container grid, 1 = player grid (default), 2 = side panels (skills/texts/trophies/PvP), 3 = crafting. `SetActiveGroup(int)` is private. JoyTabLeft/Right cycle the groups.
- **`InventoryGrid.UpdateGui`** *(private)* runs every frame for each visible grid.
  - When width or height changes it rebuilds `m_elements` *(private `List<InventoryElement>`, index = `y*width + x`)* and wires the `UIInputHandler` callbacks: left down/click/up, right down and pointer enter. **Middle click is not wired.**
  - It then repaints every used slot: icon (greyed if dragged), durability, equipped/queued/no-teleport markers, food colour, quality, `"stack/max"`.
  - For the hovered or gamepad-selected slot it calls `CreateItemTooltip` → `UITooltip.Set(name, item.GetTooltip(), anchor)`.
- **Click semantics** (`InventoryGrid.OnLeftDown`):
  - Shift (or JoyLTrigger) = `Modifier.Split`
  - Ctrl (or JoyLBumper) = `Modifier.Move`
  - otherwise `Select`
  - Gamepad: A = select (LT = split, RT = drop), X = use (LT+X = move).
- **`InventoryGui.OnSelectedItem`** *(private)* is the core rule method.
  - While dragging it calls `grid.DropItem(fromInv, item, amount, pos)`. That swaps different items, merges same-type items or moves the item, and equip state is preserved around the move.
  - Otherwise:
    - `Move` transfers to the other inventory (`MoveItemToThis`), or drops the item when no container is open.
    - `Drop` calls `Player.DropItem`.
    - `Split` opens `ShowSplitDialog`; Ctrl gives alt mode.
    - Default starts `SetupDragItem`.
  - Quest items cannot leave their inventory.
- **Right click**: `OnRightClickItem` → `Player.UseItem(inventory, item, fromInventoryGui: true)`.
- **Split dialog**: digits (top row and keypad) are typed into the amount with a 0.5 s timeout (`m_splitNumInputTimeoutSec`). Left/Right step by one and Enter confirms (`UpdateSplitDialog`).
- **Container panel (`UpdateContainer`)**
  - Visible **only while `m_currentContainer.IsOwner()`**. It calls `SetInUse(true)` every frame.
  - Auto-closes beyond `m_autoCloseDistance = 4f`.
  - Holding Use while the container is open runs `Container.StackAll()` after `m_containerHoldPlaceStackDelay` (0.5 s), then `Hide()` after a further 0.5 s.
  - Buttons: `m_takeAllButton` → `OnTakeAll` (`playerInv.MoveAll(container)`), `m_stackAllButton` → `OnStackAll` (`containerInv.StackAll(playerInv)`). Both call `SetupDragItem(null, null, 1)` first. `m_containerName` and `m_containerWeight` are TMP texts.

### Data and persistence
None of its own. The grid shows the live `Inventory`. `m_dragItem` / `m_dragInventory` / `m_dragAmount` *(private)* hold the drag state.

### Multiplayer authority
Local. Container edits are legal only because the panel is shown only while the local client owns the container's ZDO.

### Patch points
- `InventoryGui.Awake` (postfix): add buttons, search fields or overlays. Clone `m_stackAllButton` / `m_takeAllButton` to keep the vanilla look.
- `InventoryGui.Show` / `Hide` / `CloseContainer` *(private)*: set up and tear down per-open state. `CloseContainer` runs while the container is still owned, before `SetInUse(false)`.
- `InventoryGui.UpdateContainer` *(private)* (postfix): per-frame container-panel logic.
- `InventoryGrid.UpdateGui` *(private)* (postfix): per-slot overlays such as dimming, highlight or badges. After this runs, `m_elements[y*w+x]` corresponds to each item's `m_gridPos`.
- `InventoryGui.OnSelectedItem` / `OnRightClickItem` *(private)*: custom click modifiers, e.g. Alt+click.
- `InventoryGrid.DropItem`: drop, swap and merge rules.

---

## 3. Crafting panel and recipe list (inside `InventoryGui`)

### Key classes
`InventoryGui` (crafting part), `Player.GetAvailableRecipes`, `Player.GetCurrentCraftingStation`, `CraftingStation`, `Recipe`, and the private struct `InventoryGui.RecipeDataPair` (`Recipe`, `ItemData`, `InterfaceElement`, `CanCraft`).

### Flow
- `CraftingStation.Interact` → `InventoryGui.instance.Show(null, 3)`, which opens with the crafting group active. The plain inventory (Tab) calls `Show(null)`. The crafting panel `m_crafting` is always active while the inventory is visible; without a station it lists the hand-crafting recipes. Every crafting station (workbench, forge, cauldron, mead ketill, food preparation table, stonecutter, Forge of Potential...) and hand crafting share this one recipe list.
- `UpdateCraftingPanel(bool focusView = false)` *(private)* picks the tabs (`m_tabCraft` / `m_tabUpgrade`, `InCraftTab()` / `InUpradeTab()` [sic]), calls `Player.GetAvailableRecipes(ref list)`, `UpdateRecipeList(list)` *(private)* and `SetRecipe(index, center)`.
- It is **not called every frame**. It runs on `Show`, tab switches, after crafting and repair, after dropping an item outside and after moving items (`OnSelectedItem`). Picking items up does not rebuild it. This makes it the right refresh call for filters.
- **Selection after a rebuild**: `GetSelectedRecipeIndex(acceptOneLevelHigher: true)` finds the previous selection again by `Recipe` + `ItemData`; not found → index 0; empty list → `SetRecipe(-1)` (empty details, craft button off). `SetRecipe` logs `Setting selected recipe <n>` (`ZLog.Log`) on every call and, with `center`, scrolls through `m_recipeEnsureVisible.CenterOnItem` (`ScrollRectEnsureVisible` on the list's `ScrollRect`; it uses its `maskTransform` and the `ScrollRect` height). A craft in progress uses the snapshot taken by `OnCraftPressed` (`m_craftRecipe`, `m_craftUpgradeItem`), not the current selection.
- **`Player.GetAvailableRecipes`** starts from `ObjectDB.instance.m_recipes` and keeps recipes that are enabled or in the current season. It then applies **`Player.s_FilterCraft`**, a public static `List<string>` set by the non-cheat console command `filtercraft`. A recipe survives if any term matches the prefab name, the `$token` or the localized name; an empty term never matches.
  - A list containing only `""` therefore hides everything. Call `Clear()` instead.
  - Remaining conditions: DLC owned; known recipe (`m_knownRecipes`) or `m_noPlacementCost` or global key `AllRecipesUnlocked`; and `RequiredCraftingStation(recipe, 1, false)`.
- **`UpdateRecipeList`** destroys and re-instantiates one `m_recipeElementPrefab` per entry, spaced `m_recipeListSpace = 30`. Children are `icon`, `name`, `Durability`, `QualityLevel` and `selected`.
  - The craft tab skips `m_noCraftOnlyUpgrade` recipes. The upgrade tab lists player items with `m_maxQuality > 1`; upgrader stations need an `m_upgraderResource` requirement.
  - Sort order comes from the player unique key `"sortcraft"` (console `sortcraft`), parsed into `InventoryGui.SortMethod { Original, Name, Type, Weight, Count }`. Every mode sorts craftable first, then `Recipe.m_listSortWeight`, then name/type/weight, then quality level. `Count` has no switch case, so it does not sort.
  - Order inside the method: build the rows, size the content (`max(m_recipeListBaseSize, count * m_recipeListSpace)`; `m_recipeListBaseSize` *(private)* is read once in `Awake` from the list root's height), **then** sort `m_availableRecipes` (`List.Sort`, not stable), **then** reposition every row. A reorder of the `recipes` argument in a prefix is therefore overwritten (only ties survive): reorder in a postfix, then redo `anchoredPosition` and the content height.
- **Which ingredients a row shows** (`SetupRequirementList`, called from `UpdateRecipe` with quality 1 in the Craft tab and item quality + 1 in the Upgrade tab): a requirement is listed only when `station.m_upgrader == req.m_upgraderResource` (without a station: `!req.m_upgraderResource`, so idols show only at the Forge of Potential), `req.GetAmount(quality) > 0` (upgrade-only ingredients are hidden in the Craft tab and craft-only ones in the Upgrade tab), and, for `m_requireOnlyOneIngredient` recipes, `Player.IsKnownMaterial(name)`.
- Recipe assets are named `Recipe_<result prefab>` (e.g. `Recipe_SwordSilver`, game data manifest `Assets/GameElements/Recipes/...`). A search on `recipe.name` must strip that prefix, or `recipe` matches every row.
- **`UpdateRecipe(player, dt)`** runs every frame.
  - It builds the description with `ItemDrop.ItemData.GetTooltip(item, quality, crafting: true, worldLevel, stack)` (static overload).
  - The requirement list has `m_recipeRequirementList` slots and pages every second when there are more than 4.
  - It sets craft button state and tooltips.
  - Multi-craft ×`m_multiCraftAmount` (5) applies when AltPlace (Shift) or JoyLStick is held.
  - Durations: `m_craftDuration` 2 s, `m_multiCraftDuration` 6 s, upgrader `m_upgraderDuration` 8 s + 1 s per level. All are reduced by skill factor × `m_craftDurationSkillMaxDecrease` (0.6).
- `OnCraftPressed` → timer → `DoCrafting(player)` *(private)*. That method revalidates, rolls the bonus (`m_craftBonusChance` 0.25 × skill factor, stackables only), calls `Inventory.AddItem(name, …)`, then `Player.ConsumeResources`, raises the skill and records stats.
- **Repair.** `OnRepairPressed` → `RepairOneItem()` repairs **one** item per click. `CanRepair(item)` accepts an item whose repair or crafting station name matches and where `min(stationLevel, 4) >= m_minStationLevel`. Items from a lower world level are also accepted. Under `nocost` every repairable item is accepted, and the button also shows without a station. Details: [building-crafting.md §9](building-crafting.md); mod: [One Click Repair All](../../src/Crafting/Repair.OneClickAll).

### Data and persistence
The sort choice lives in player unique keys, which are saved in the character. `s_FilterCraft` is static and unsaved, and it survives across screens.

### Multiplayer authority
Local player. Crafting runs entirely client-side against the player inventory.

### Patch points
- `Player.GetAvailableRecipes` (postfix): feeds both tabs, but other mods call it for their own UIs (recipe browsers), so a filter there must be scoped to the crafting panel.
- `InventoryGui.UpdateRecipeList` (postfix): filter, re-sort or decorate rows after vanilla sorting. Row positions are `anchoredPosition = (0, -i*m_recipeListSpace)`. A postfix that removes rows must also destroy their GameObjects and set the content height again. Mod: [Crafting Search and Sort](../../src/UX/Crafting.SearchSort) filters and sorts here (postfix at `Priority.Low`) and refreshes with `UpdateCraftingPanel(false)` ([design](../design/ux-crafting-search-sort.md)).
- `InventoryGui.UpdateCraftingPanel` (call it to refresh), `InventoryGui.SetRecipe`, `InventoryGui.DoCrafting`, `InventoryGui.RepairOneItem` (for one-click repair all).

---

## 4. Containers: open/close and ownership (`Container`)

### Key classes
`Container` (Hoverable, Interactable), `Vagon` (cart), `TombStone`, `Incinerator`, `PrivateArea` (wards).

### Flow
- `Container.Awake`:
  - builds the `Inventory` and subscribes `OnContainerChanged` to `m_onChanged`
  - registers the RPCs `RPC_RequestOpen`, `RPC_OpenResponse`, `RPC_RequestStack`, `RPC_StackResponse`, `RPC_RequestTakeAll`, `RPC_TakeAllResponse` and `RPC_Discovered`
  - hooks `WearNTear` / `Destructible.m_onDestroyed`
  - adds `m_defaultItems` once on the owner, guarded by `ZDOVars.s_addedDefaultItems`
  - runs `InvokeRepeating("CheckForChanges", 0, 1)`
- **Opening.**
  1. `Container.Interact` checks the ward (`PrivateArea.CheckAccess` when `m_checkGuardStone` is set), then `CheckAccess(playerID)`. Privacy: `Public` → true, `Private` → creator only, `Group` → always false.
  2. It sends `m_nview.InvokeRPC("RPC_RequestOpen", playerID)`, which goes to the current ZDO owner.
  3. The owner runs `RPC_RequestOpen`. If the container is in use by someone else (or the cart is in use), it answers `false`. Otherwise it calls `ZDOMan.instance.ForceSendZDO(uid, …)`, then `GetZDO().SetOwner(uid)`, then answers `true`.
  4. The requester's `RPC_OpenResponse` calls `InventoryGui.instance.Show(this)`.
- The same request → owner check → hand ownership → response pattern is used by `StackAll()` (RPC_RequestStack) and `TakeAll()` (tombstones; a 2 s cooldown and `ClaimOwnership()` on the requester).
- **In-use state.** `m_inUse` is local to the owner. `UpdateUseVisual` mirrors it into `ZDOVars.s_inUse` (int) so other clients swap the `m_open` / `m_closed` visuals.
- **Saving.** `OnContainerChanged` → `Save()` when the local client is owner and not loading. `Save` writes `Inventory.Save` bytes into `ZDOVars.s_items` and remembers `DataRevision`.
- **Loading.** `CheckForChanges` (1 s) → `Load()` runs only if the ZDO `DataRevision` changed and the local client is **not** using the container, then calls `UpdateRows()`, which grows the height to fit legacy items.
- Destruction (`OnDestroyed`, owner only) drops everything, or moves it into `m_destroyedLootPrefab` containers.
- `m_autoDestroyEmpty` containers (e.g. the temporary drop containers spawned from a `m_destroyedLootPrefab`) self-destroy when the owner sees them empty and unused.
- Vanilla bug worth knowing: `Awake` registers `"RPC_Discovered"` but `Interact` invokes `"discovered"`. The discover flag never persists, so `m_discoverStat` increments on every open.

### Data and persistence
ZDO keys: `s_items` (byte[]), `s_inUse` (int), `s_addedDefaultItems` (bool), `s_cheated` (bool, affects default items).

### Multiplayer authority
The **ZDO owner** is authoritative. Only one player can hold a container open. Ownership moves to the opener and stays with them after closing. Any code that edits `container.GetInventory()` must run on the owner, which is always true while `InventoryGui` shows the container. Reading is always safe: every client in range reloads `s_items` every second, so any client can *search* nearby containers read-only.

### Patch points
- `Container.RPC_RequestOpen` (owner-side access policy)
- `Container.CheckAccess` (private)
- `Container.OnContainerChanged` / `Save` / `Load`
- `Container.GetHoverText` (e.g. show a contents summary)
- `Container.Interact`
- To edit a container the player does not have open, reuse the `RPC_RequestOpen` handshake or `m_nview.ClaimOwnership()` (only safe when no one is using it; check `s_inUse`).

---

## 5. Picking up items (`Player.AutoPickup`, `Humanoid.Pickup`, `ItemDrop`)

### Key classes
`Player` (auto pickup), `Humanoid` (the pickup itself), `ItemDrop` (world item: MonoBehaviour, Hoverable, Interactable).

### Flow
- `Player.FixedUpdate` calls `AutoPickup(dt)` *(private)*. It returns early while teleporting or when `m_enableAutoPickup` *(private static)* is false. That flag is toggled by button `"AutoPickup"` (V) or JoyAutoPickup + JoyAltKeys and shows `$hud_autopickup:$hud_on/off` top-left.
- `AutoPickup` runs `Physics.OverlapSphereNonAlloc(pos + up, m_autoPickupRange = 2f, m_colliders[100], "item" layer)`. For each `ItemDrop` found (floating-terrain dummies resolve to their parent):
  1. **Skip** if `!m_autoPickup`, `IsPiece()` (items placed on item stands or tables), `HaveUniqueKey(m_shared.m_name)` (quest item already owned), or the view is invalid.
  2. If `!CanPickup()`, call `RequestOwn()`.
  3. Otherwise skip items in tar. Then `Load()`, skip if `!m_inventory.CanAddItem` or the item would exceed `GetMaxCarryWeight()`. Pull the item towards the player at 15 m/s and call `Pickup(go)` below 0.3 m.
- `ItemDrop.CanPickup(autoPickupDelay = true)` fails for 0.5 s after spawn (`c_AutoPickupDelay`) and requires **ZDO ownership**. `RequestOwn()` retries with backoff (0.2 s × 2^n, capped at 30 s) and sends `RPC_RequestOwn`. The current owner answers by `SetOwner(requester)`.
- Manual pickup: `ItemDrop.Interact` → `ItemDrop.Pickup(Humanoid)`. If not owner, it polls `PickupUpdate` every 0.05 s until ownership arrives.
- **`Humanoid.Pickup(GameObject go, bool autoequip = true, bool autoPickupDelay = true)`** is the core rule method:
  - checks that icons and variant are valid, `CanPickup`, not already contained, and the quest-item unique key
  - `m_inventory.AddItem(itemData)` (else `$msg_noroom`)
  - `ZNetScene.instance.Destroy(go)`
  - auto-equips a weapon if the hands are empty
  - pickup effects
  - `ShowPickupMessage(item, stack)`, which shows top-left `"$msg_added <name>"` with amount and icon
  - informs the radial menu (`Hud.instance.m_radialMenu.OnAddItem`)
- A player drop (`Humanoid.DropItem`) calls `ItemDrop.OnPlayerDrop()`, which sets `m_autoPickup = false`. This is **local to the dropping client's instance and not networked**; other players will still auto-pick it up.
- World item upkeep: `ItemDrop.SlowUpdate` runs every 10 s on the owner.
  - `TerrainCheck`
  - `TimedDestruction`: despawn after 3600 s unless inside a player base, a player is within 25 m, it sits in tar, or it is a piece
  - `AutoStackItems` when more than 200 `ItemDrop` instances exist (`c_AutoStackThreshold`): merges same name and quality within 4 m if `m_autoStack`.

### Data and persistence
ZDO `s_itemData` (byte[]: version byte 109 + `ItemData.Save`), `s_spawnTime`, `s_piece`. `ItemDrop.SaveToZDO` / `LoadFromZDO` are public statics. The instance `Load()` is public and `Save()` is private (owner only). `ItemDrop.s_instances` *(private static List)* holds all live drops.

### Multiplayer authority
The ZDO owner of the ItemDrop. Pickup is a client ownership grab followed by a local inventory add and a network destroy. No server logic is involved, so pickup filters are **client-only**.

### Patch points
- `Player.AutoPickup` (private): the filter decision. The only per-item gate is the `ldfld ItemDrop::m_autoPickup` check, so a small transpiler can replace it with `MyFilter.Allow(itemDrop)`. Otherwise use a prefix that re-implements the loop, which conflicts more easily.
- `Humanoid.Pickup`: blocks both manual and auto pickup. Also a good place for "on picked up" reactions (postfix, `__result`).
- `Character.ShowPickupMessage`: rewrite or silence pickup toasts.
- `Player.m_autoPickupRange` is a public field and can be set directly.

---

## 6. HUD and notifications (`Hud`, `MessageHud`)

### Key classes
- `Hud` (singleton `Hud.instance`, root `m_rootObject`): health, stamina, **adrenaline** (1.0), eitr and food bars, status effects, guardian power, crosshair and hover text (`m_hoverName`, set in `UpdateCrosshair` from `Hoverable.GetHoverText()`), build HUD, ship HUD, the 1.0 **`BuildUi`** (`m_buildUi`) and the radial menu (`m_radialMenu`, a `Valheim.UI.RadialBase`). Ctrl+F3 toggles `m_userHidden`, read through `Hud.IsUserHidden()`.
- `MessageHud` (singleton `MessageHud.instance`) holds four independent channels.

### MessageHud channels
| Channel | API | Rendering | Timing / limits |
|---|---|---|---|
| Top-left | `ShowMessage(MessageType.TopLeft, text, amount, icon, showDespiteHiddenHUD, log)` | one `m_messageText` + one `m_messageIcon` | queued in `m_msgQeue` [sic]; **one message is shown per ≥1 s** (`m_msgQueueTimer >= 1f`); each cross-fades out over **4 s**; if the next queued message has the same text and icon within 4 s it is merged as `" xN"` |
| Center | `ShowMessage(MessageType.Center, …)` | one `m_messageCenterText` | no queue; a new message **overwrites** immediately; fades over 4 s |
| Unlocks | `QueueUnlockMsg(icon, topic, description)` | `m_unlockMsgPrefab` instances | `m_maxUnlockMessages = 4` slots (list built in `Start`), vertical spacing `m_maxUnlockMsgSpace = 110`; lifetime comes from the prefab Animator (destroyed when the state has tag `"done"`, one per frame); when the queue drains it shows `"N $inventory_logs_new"` top-left |
| Biome / area | `ShowBiomeFoundMsg(text, playStinger)` | `m_biomeFoundPrefab` | waits until the top-left queue is empty and 2 s have passed |

- `UpdateMessage(dt)` *(private)* also drains `_crossFadeTextBuffer` **one entry per frame** for both top-left and center. Skipping the whole method breaks center-text fading.
- History: `AddLog` *(private)* keeps the last `m_maxLogMessages = 50` messages, readable through `GetLog()`. They appear in the inventory **Texts** dialog (`TextsDialog.AddLog`, `$inventory_logs`). Note `Player.Message(type, msg, amount, icon, log = false)`: messages sent through the player (pickups, most gameplay toasts) are **not logged** by default, while direct `MessageHud.ShowMessage` calls default to `log = true`.
- Network paths:
  - `MessageHud.MessageAll(type, text)` broadcasts routed RPC `"ShowMessage"(int, string)` to everybody (boss spawn/death, world-save warnings).
  - `Player.Message` on a non-owned player sends the ZNetView RPC `"Message"(int, string, int)` to the owner, where `Player.RPC_Message` passes it to `MessageHud.ShowMessage`. The text travels as the raw `$` token string and is localized on arrival.
- `Update` calls `HideAll()` while the HUD is user-hidden, unless `showDespiteHiddenHUD`. `ShowMessage` itself **returns at once** while the HUD is user-hidden (Ctrl+F3) and `showDespiteHiddenHUD` is false: such messages are dropped, not queued. To react to a gameplay message reliably (hidden HUD, or another mod muting `ShowMessage`), hook `Player.Message` / `Player.RPC_Message` instead (Creature Kill and Tame Counts does, for the tame message).

### Multiplayer authority
Local rendering. Only the text arrives over RPC.

### Patch points
- `MessageHud.ShowMessage`: prefix to reroute TopLeft/Center into a custom feed, keeping `AddLog`.
- `MessageHud.UpdateMessage` (private): the queue cadence.
- `MessageHud.Awake`: change `m_maxUnlockMessages` before `Start` builds the slot list.
- `MessageHud.UpdateUnlockMsg` (private): postfix to slow new instances' Animator speed, which lengthens their lifetime.
- `MessageHud.QueueUnlockMsg`, `MessageHud.HideAll`.
- `Character.ShowPickupMessage` / `ShowRemovedMessage`.
- `Hud.UpdateCrosshair` (private): hover-text additions.

---

## 7. Chat, shouts and pings (`Chat`, `Talker`)

### Key classes
- `Chat : Terminal` (singleton `Chat.instance`).
- `Talker` (on the player prefab) with `Talker.Type { Whisper, Normal, Shout, Ping }` and distances `m_visperDistance = 4`, `m_normalDistance = 15`, `m_shoutDistance = 70`.
- `Chat.WorldTextInstance` (`m_talkerID`, `m_position`, `m_timer`, `m_gui`, `m_textMeshField`, `m_type`, `m_text`, `m_name`).

### Flow
- Typing goes through `Chat.InputText`, which prefixes `say` unless the text starts with `/`, then `TryRunCommand`. Built-ins: `/w`, `/s`, `/die`, `/resetspawn` and emotes.
- **Say/Whisper**: `Talker.Say` → ZNetView RPC `"Say"` to each permitted user. Receivers' `Talker.RPC_Say` apply the distance filter and call `Chat.OnNewChatMessage`.
- **Shout**: `Chat.SendText(Shout)` → routed RPC `"ChatMessage"(Vector3 headPoint, int 2, UserInfo, string)` sent per permitted user.
- **Ping**: `Chat.SendPing(Vector3 pos)` sets `pos.y` to the local player's y, then broadcasts routed RPC `"ChatMessage"(pos, 3, UserInfo, "")` to `0L` (everybody, including self).
- **Receiving (`OnNewChatMessage`)**:
  1. Check the platform relation permission (block lists; async through `RelationsManager.CheckPermissionAsync`).
  2. Replace `<` and `>` with spaces; apply `CensorShittyWords.Filter` when the permission result is `GrantedRequiresFiltering`.
  3. Non-ping text goes to the chat log (`AddString`).
  4. `AddInworldText(go, senderID, pos, type, user, text)` *(private)* runs unless the local player is in **no-map mode** (`Game.m_noMap` forces `Minimap.m_mode == None`) and the event is further than `Minimap.m_nomapPingDistance = 50 m`.
- **World texts** (`AddInworldText`) keep **one instance per sender** (`FindExistingWorldText(senderID)`), so a new ping or shout replaces the previous one.
  - Pings render as the literal `"PING"` (the payload text is ignored) in colour (0.6, 0.7, 1). Shouts render yellow and upper-case. Both are prefixed with the display name.
  - `UpdateWorldTexts` (LateUpdate) drifts them up 0.15 m/s, clamps them to the screen edge (`ClampToScreenEdge`), and removes **one** expired entry per frame after `m_worldTextTTL = 5 s`.
- In vanilla, pings can **only** be sent from the large map: `Minimap.OnMapMiddleClick`, `OnMapDblClick` with the Ping icon selected, or gamepad X held in `UpdateMap` (fires every frame while held). The `SpawnSystem` / `Terminal` debug paths also send them. There is no in-world ping key.
- `Chat.GetPingWorldTexts` / `GetShoutWorldTexts` feed the minimap (§8).

### Data and persistence
None. Pings are transient.

### Multiplayer authority
The sender broadcasts and every receiver renders locally. Vanilla does no server validation or rate limiting. A dedicated server without the mod still relays **any** routed RPC. `ZRoutedRpc.RouteRPC` forwards broadcasts to all peers, and `HandleRoutedRPC` silently ignores method hashes it has no handler for, so custom routed RPCs are safe against vanilla peers.

### Patch points
- `Chat.SendPing`
- `Chat.OnNewChatMessage` (before the permission check; avoid bypassing block lists)
- `Chat.AddInworldText` (private; after permission and sanitising, which is the best place to intercept a ping and render a custom marker)
- `Chat.UpdateWorldTexts` (private; TTL and positioning)
- `Chat.m_worldTextTTL` (public field)
- `Chat.m_worldTextBase` (public template GameObject with a `"Text"` child, cloneable for custom markers)

---

## 8. Minimap pins and pings (`Minimap`)

### Key classes
- `Minimap` (singleton `Minimap.instance`), `Minimap.PinData`, `Minimap.PinNameData`.
- `Minimap.PinType` enum: Icon0–3, Death, Bed, Icon4, Shout, None, Boss, Player, RandomEvent, **Ping**, EventArea, Hildir1–3, Memorial.
- `Minimap.MapMode { None, Small, Large }`.

### Flow
- `Update`:
  1. Generates the map on first run (texture cache per world seed).
  2. Handles the `"Map"` / `"JoyMap"` toggle while no text field has focus (Chat, Console, TextInput, Menu, InventoryGui and `Hud.instance.m_buildUi.SearchFieldFocused` all block it).
  3. `UpdateMap` (input: zoom, drag, pin placement)
  4. `UpdateDynamicPins`: profile/bed pin, shout pins, **ping pins**, player pins, location pins every 5 s, random event and persistent 1.0 event pins
  5. `UpdatePins()` when `m_pinUpdateRequired`
- `UpdatePingPins` *(private)* rebuilds the `m_pingPins` list whenever the number of ping world texts changes. It adds non-saved `PinType.Ping` pins (`m_doubleSize`, `m_animate`, name `"<name>: PING"`) and copies positions every frame.
- `UpdatePins` *(private)* instantiates `m_pinPrefab` under `m_pinRootSmall/Large`. Sizes are `m_pinSizeSmall = 32` and `m_pinSizeLarge = 48` (doubled for `m_doubleSize`); animated pins pulse. Names show only in Large mode when `LargeZoom < m_showNamesZoom (0.5)`. Pin types can be hidden per type (`m_visibleIconTypes`, `ToggleIconFilter`).
- **User pins**:
  - `OnMapDblClick` → `ShowPinNameInput` → `AddPin(pos, m_selectedType, "", save: true, …)`, then a name input
  - left click toggles `m_checked` (or clears `m_ownerID` for shared pins)
  - right click / long touch → `RemovePinUnderPointer`
  - gamepad: A places, DPad selects the icon, TabLeft/Right check/remove
- Public API:
  - `AddPin(pos, type, name, save, isChecked, ownerID = 0, author = default)` returns `PinData`. Setting `pinData.m_icon` to any `Sprite` gives a custom icon with no asset bundle.
  - `RemovePin(PinData)`, `RemovePin(Vector3, radius)`
  - `DiscoverLocation(pos, type, name, showMap)`
  - `ShowPointOnMap(pos)`, `SetMapMode(mode)`
  - `IsOpen()`, `InTextInput()`
- Sprites: `m_icons` (List<SpriteData> per PinType), `m_locationIcons`, `m_pingIcon`.

### Data and persistence
- `GetMapData()` / `SetMapData()` *(private)*: map format version 8, compressed. It contains explored and exploredOthers bitmaps, every pin with `m_save == true` (name, pos, type, checked, ownerID, author), and the public-position flag.
- It is stored per world in the character file (`PlayerProfile.SetMapData/GetMapData`, inside `m_worldData`).
- Shared map data (cartography table, `MapTable` RPC `"MapData"`, ZDO `s_data`) goes through `Minimap.GetSharedMapData` / `AddSharedMapData`. Shared pins arrive with `m_ownerID != 0` and render greyed.
- Unknown `PinType` values are coerced to `Icon3` with a warning (`AddPin`). **Do not persist custom enum values.** Use `PinType.None` plus a custom `m_icon` with `save: false`, or keep your own save.

### Multiplayer authority
Local, except player-position pins (`ZNet.GetOtherPublicPlayers`) and pings/shouts, which come from `Chat`.

### Patch points
- `Minimap.UpdatePingPins` / `UpdateShoutPins` (private; replace ping pins with typed pins)
- `Minimap.OnMapMiddleClick` / `OnMapDblClick` (ping sending)
- `Minimap.AddPin` (observe or decorate)
- `Minimap.UpdatePins` (private; per-pin visuals)
- `Minimap.m_nomapPingDistance`

---

## 9. Input (`ZInput`, `PlayerController`, `Player.TakeInput`)

### Key classes
- `ZInput` (assembly_utils; singleton `ZInput.instance`, mostly static API) sits on the **Unity Input System** package (`Unity.InputSystem.dll`). Buttons are `ButtonDef`s in `m_buttons` *(private Dictionary<string, ButtonDef>)*, created by `AddButton` *(private)*.
- `InputLayout` (Default, Alternative1, Alternative2) selects the gamepad layout. `ZInput.OnInputLayoutChanged` is an event.
- `Player.TakeInput()` (protected override) and `PlayerController.TakeInput(bool look)` *(private)* gate gameplay input on the UI state checks in §14.

### Flow and API
- Queries:
  - `ZInput.GetButtonDown/GetButton/GetButtonUp(name)`
  - `GetKeyDown/GetKey/GetKeyUp(KeyCode)`: the low-level path through `Keyboard.current` / `Mouse.current` / `Gamepad.current`
  - `GetMouseButton*`
  - `GetButtonPressedTimer`, `GetButtonLastPressedTimer`
  - `ResetButtonStatus(name)`: swallows a press for the rest of the frame (used heavily by `InventoryGui`)
  - `IsGamepadActive()`, `IsExclusiveGamepadActive()`, `IsMouseActive()`, `IsTouchActive()`, `ZInput.pointerPosition`
- Default keyboard and mouse buttons (from `ResetKBMButtons`; rebindable ones are marked):
  - Movement: `Forward/Left/Backward/Right` WASD, `Jump` Space, `Crouch` LCtrl, `Run` LShift, `AltPlace` LShift, `ToggleWalk` C, `AutoRun` Q, `Sit` X, `Hide` R
  - Interaction and combat: `Use` E, `GP` F (guardian power), `Attack` LMB, `Block` RMB, `SecondaryAttack` MMB
  - Screens: `Inventory` Tab, `Map` M, `BuildMenu` RMB, `Remove` MMB, `AutoPickup` V
  - `Console` F5 and `OpenRadial` G / `OpenEmote` T are rebindable
  - `Chat` Enter and `Hotbar1..8` are fixed; `Hotbar{i}Alt` is rebindable
  - `TabLeft` Q / `TabRight` E
  - Gamepad button names are prefixed `Joy` (e.g. `JoyButtonA`, `JoyUse`, `JoyMap`, `JoyTabLeft`, `JoyAltKeys`, `JoyLStick`).
- Persistence: `ZInput.Save()` writes `PlatformPrefs` `"kbmBinding_<name>"` for every rebindable button. `Load()` rebinds them. Rebinding UI: `ZInput.instance.StartBindKey(name)`.
- `$KEY_<Button>` in localized strings resolves to the bound key, or the `Joy<Button>` glyph when a gamepad is active (§12).

### Adding custom keybinds (options)
1. **Simplest.** Store a BepInEx `ConfigEntry<KeyboardShortcut>` and poll it with `ZInput.GetKeyDown(shortcut.MainKey)` plus `ZInput.GetKey(modifier)`. This reads the Input System directly and works whether or not legacy `UnityEngine.Input` is enabled. BepInEx's own `UnityInput.Current` also abstracts both. Players edit it through BepInEx ConfigurationManager.
2. **Native.** Postfix `ZInput.ResetKBMButtons` (private) and call the private `AddButton(name, path, altKey, showHints, rebindable: true)` by reflection. `GetButtonDown("MyButton")` then works, and `ZInput.Save` persists it. It still will **not** appear in the Settings key list, which is a serialized prefab list (`KeyboardMouseSettings.m_keys` of `KeySetting`), unless you also clone a row into that page. Jotunn's `InputManager`/`ButtonConfig` automates this.
3. Gamepad keys are scarce. Prefer chords with `JoyAltKeys`, as vanilla does, or a radial entry.

### Patch points
- `Player.TakeInput` / `PlayerController.TakeInput` (block gameplay while a custom UI is focused)
- `ZInput.ResetKBMButtons` (add buttons)
- `Chat.HasFocus` (postfix-OR a custom text field's focus: see §14)

Inventory screen timing (learned for Crafting Search and Sort):
- `InventoryGui.Update` calls `ZInput.ResetButtonStatus("Inventory")` (and `"JoyButtonY"`) right before `Show`, so a later `GetButtonDown("Inventory")` in the same frame misses the press that opened the inventory. It closes only when `m_shownFrames > 1`, on `Inventory`, `JoyButtonB`, `JoyButtonY`, Escape or `Use`, and resets those buttons first. `GP` (guardian power) is F by default, but `Player.Update` reads it only when `Player.TakeInput` is true, never while the inventory is visible. `Use` and `Inventory` are rebindable, so a player may bind one of them to F (the build-search key).
- `InventoryGui.IsVisible()` stays true on the frame of `Hide`; the Animator bool `"visible"` (`m_animator`, private) is cleared at once by `Hide`, so use it for a same-frame check.
- With keyboard and mouse the player can walk while the inventory is open: `PlayerController.TakeInput` blocks movement for the inventory only when a gamepad is active. `Player.TakeInput` is false while the inventory is visible (no hotbar, Use, auto-pickup toggle).
- The build search's focus (`Hud.instance.m_buildUi.SearchFieldFocused`) is checked by `Player.TakeInput`, `PlayerController.TakeInput` and `Minimap.Update`, not by `InventoryGui.Update`.

---

## 10. UI toolkit (TextMeshPro, gui_framework, navigation, radial)

- **Text**: TextMeshPro everywhere (`TMP_Text` / `TextMeshProUGUI`). Game fonts live in the `tmp_fonts` bundle, so **clone an existing TMP text** (e.g. `InventoryGui.m_containerName`, `MessageHud.m_messageText`) instead of creating one; that keeps the font, material and outline. Rich text works. Gamepad glyphs use `<sprite=…>` (see `Hud.UpdateCrosshair`).
- **gui_framework** (`GUIFramework` namespace):
  - `GuiInputField : TMP_InputField` adds virtual keyboard / Steam gamepad text input support, `OnInputSubmit`, `VirtualKeyboardOnActivate`, `CaretOnGamepadUsage` and the static `GuiInputField.VirtualKeyboardOpen`.
  - `GuiButton : Button`, `GuiToggle`, `GuiDropdown`, `GuiSlider`, `GuiStepper`.
- **Ready-made input fields to clone**:
  - `Hud.instance.m_buildUi` → `BuildUi.m_searchField` *(private SerializeField GuiInputField)*: the 1.0 build-menu **search bar**, already styled for a panel header.
  - `TextInput.instance.m_inputField` (sign dialog).
  - `Chat.instance.m_input`.
- `BuildUi.UpdateSearch(term)` is the vanilla search reference. It strips spaces, lower-cases (`ToLowerInvariant`), and toggles buttons by `Contains` against pre-computed `BuildUiPieceButton.LocalizedSearchTerms`. It focuses on `F` or JoyLStick and swaps `TabHandler.m_keybaordInput` on select/deselect.
- **Click plumbing** (assembly_guiutils):
  - `UIInputHandler` exposes delegates `m_onLeftClick/LeftDown/LeftUp`, `m_onRightClick/RightDown/RightUp`, `m_onMiddleClick/MiddleDown/MiddleUp`, `m_onPointerEnter/Exit`.
  - `UIDragHandler` exposes `m_onBeginDrag/EndDrag/ReleasedOn/Drag`.
  - `GuiUtils.SetNavigation*` helpers wire explicit `Selectable` navigation.
- **Gamepad focus: `UIGroupHandler`.**
  - All groups register in a static list. Each frame the group with the highest `m_groupPriority` among active objects whose `SetActive(true)` was called becomes `IsActive`.
  - It toggles its `CanvasGroup.interactable` and auto-selects `m_defaultElement` when a gamepad is active.
  - New panels should carry their own `UIGroupHandler` with a priority above the inventory's, or be added to `InventoryGui.m_uiGroups`.
  - `ScrollRectEnsureVisible.CenterOnItem` keeps the selection in view.
- **Radial menu (1.0, gamepad-first)**: `Hud.instance.m_radialMenu` (`Valheim.UI.RadialBase`: `Open(IRadialConfig, back)`, `QueuedOpen`, `Refresh`, `Back`, `QueuedClose`). Configs implement `IRadialConfig { LocalizedName; Sprite; InitRadialConfig(RadialBase) }`. `OpenRadialConfig` picks Main or Emote groups, or a context radial from the hover object. This is a natural host for a ping wheel or a sort-mode wheel.
- **Localize component**: `Localize.Start` localizes every Text/TMP under it and re-localizes on language or input-layout change. Put `$tokens` in cloned labels **before** they start, so they stay translatable. Remove it from a clone whose text you set yourself.
- **`GuiInputField` focus and navigation**: `ActivateInputField` turns `EventSystem.sendNavigationEvents` off while a keyboard user types (it does nothing for a gamepad user) and opens the Steam keyboard in Big Picture. Navigation comes back **only on submit or deselect** (listeners added in `GuiInputField.Start`) or when the field is disabled or destroyed, so when a field loses focus any other way (Esc, a click elsewhere, a mod releasing it), also call `EventSystem.current.SetSelectedGameObject(null)`, as `Chat.Update` does.
- **Cloning checklist** (learned for Crafting Search and Sort):
  - a cloned `Button` keeps its persistent `onClick` calls: assign a new `Button.ButtonClickedEvent`. For a cloned `GuiInputField`, mute the persistent listeners (`SetPersistentListenerState(i, UnityEventCallState.Off)`) instead of replacing the events, which would drop the listeners `GuiInputField.Start` adds;
  - a cloned `Selectable` keeps its source's `navigation`, explicit targets included: set `Navigation.Mode.None` to keep the D-pad off a control that must not trap a gamepad user (Unity's automatic navigation skips `None`);
  - destroy cloned `UIGamePad` (button hotkey), `UIInputHint`, `UITooltip` and `Localize` components before the clone's `Start`; destroy their hint GameObjects only when they are inside the clone (`Instantiate` keeps references to objects outside the cloned hierarchy, which belong to the vanilla source);
  - instantiate under an inactive parent (or deactivate first) so nothing runs before you strip it.

---

## 11. Settings menu (`Settings`, `Valheim.SettingsGui.*`, `PlatformPrefs`)

- `Settings` (singleton) holds a `TabHandler` of pages, each implementing `ISettingsTab`:
  - interface: `Initialize`, `OnTabOpen`, `OnOkAsync`, `OnBack`, `SharedSettingChanged`
  - pages: `GameplaySettings`, `KeyboardMouseSettings`, `GamepadSettings`, `GraphicsSettings`, `AudioSettings`, `AccessibilitySettings`, `RadialSettings`
  - Settings are applied on OK (`ApplyAndClose`); Escape runs `OnBack`.
- Values persist through `PlatformPrefs` (assembly_utils: `GetInt/SetInt/GetString/SetString/HasKey/DeleteKey`), not `PlayerPrefs`. Examples: `"EnableConsole"`, `"KeyHints"`, `"ConsoleBindings"`, `"language"`, `"kbmBinding_*"`.
- The key-binding page is data-driven from a serialized `List<KeySetting>` (`m_keyName`, `m_keyTransform`, `m_blockedButtons`) and calls `ZInput.instance.StartBindKey`.
- **Recommendation for our mods**: expose options through BepInEx config and ConfigurationManager (F1), not by injecting into the vanilla Settings prefab. Use Jotunn only if a mod already depends on it. Per-character state belongs in `Player.m_customData` (§15).

---

## 12. Localization (`Localization`, assembly_guiutils)

- `Localization.instance`:
  - `Localize(string)` replaces each `$word` (terminated by one of `" (){}[]+-!?/\&%,.:-=<>\n"`) through `Translate`. Missing keys render as `"[word]"`. `$KEY_<button>` resolves to bound key names.
  - `Localize(text, params string[] words)` substitutes `$1..$n` afterwards.
  - Results are memoised in an **LRU cache of 100** (`m_cache`).
- Sources: CSV `TextAsset`s from `LocalizationSettings.Localizations`, with columns per language and English as fallback. `SetLanguage` clears the table, calls `SetupLanguage` and raises `Localization.OnLanguageChange` (a plain static `Action`: subscribe with `Delegate.Combine`). By then the new words are in place, so a handler can re-localize its labels at once.
- Ready-made labels: skill names are `"$skill_" + SkillType.ToString().ToLower()` (`SkillsDialog`), e.g. `$skill_swords` "Swords", `$skill_elementalmagic` "Elemental Magic"; note `$skill_unarmed` is "Fists" in English. Station names: `$piece_workbench`, `$piece_forge`, `$piece_blackforge`, `$piece_magetable` (Galdr Table), `$piece_artisanstation`, `$piece_cauldron`, `$piece_meadcauldron` (Mead Ketill), `$piece_preptable` (Food Preparation Table), `$piece_stonecutter`, `$piece_upgradestation` (Forge of Potential) exist in the 1.0.16 English table.
- **There is no public API to add words.** `AddWord(key, text)` and `m_translations` are private. To add tokens:
  1. postfix `Localization.SetupLanguage(language)` (it runs at construction and on every language change)
  2. call `AddWord` by reflection for our tokens (English fallback plus any translations)
  3. evict `m_cache` if words are added after the first `Localize`

  Jotunn's `LocalizationManager` does the same. Prefix our tokens (`$vm_…`) to avoid clashes.

---

## 13. Tooltips (`UITooltip`, item tooltips)

- `UITooltip` (assembly_guiutils) has a single static tooltip instance (`m_current`, `m_tooltip`) instantiated from `m_tooltipPrefab` on hover. `Set(topic, text, anchor = null, fixedPosition = default)` returns early if nothing changed. `UITooltip.HideTooltip()` is static.
- Item tooltips:
  - `ItemDrop.ItemData.GetTooltip(int stackOverride = -1)` → `static GetTooltip(ItemData item, int qualityLevel, bool crafting, float worldLevel, int stackOverride = -1, bool appending = false)`.
  - Used by `InventoryGrid.CreateItemTooltip`, which runs **every frame** for the hovered slot, by the crafting description (`InventoryGui.UpdateRecipe`) and by `StoreGui`.
  - Postfix the static overload to append lines. Keep it allocation-light or cache per `(item, quality)`.
- Hover text in the world comes from `Hoverable.GetHoverText()` per component (e.g. `Container.GetHoverText`, `ItemDrop.GetHoverText`) and is rendered by `Hud.UpdateCrosshair`.
- The settings tooltips (`Valheim.SettingsGui.SettingsTooltip`) are a separate system.

---

## 14. UI state checks

Use these to decide whether a hotkey may fire or whether the player is "in a menu".

| Check | Meaning |
|---|---|
| `InventoryGui.IsVisible()` | inventory, container or crafting screen open (true for 1 frame after hide) |
| `InventoryGui.instance.IsContainerOpen()` | a container is attached to the screen |
| `Menu.IsVisible()` / `Menu.IsActive()` | pause menu (or a `UnifiedPopup`) |
| `TextInput.IsVisible()` | sign/text dialog |
| `Console.IsVisible()` | F5 console open |
| `Chat.instance.HasFocus()` | chat input focused |
| `Minimap.IsOpen()` / `Minimap.InTextInput()` | large map / pin-name typing |
| `StoreGui.IsVisible()` | trader |
| `TextViewer.instance.IsVisible()` | rune/lore text |
| `Hud.IsPieceSelectionVisible()`, `Hud.InBuildUi()`, `Hud.InRadial()`, `Hud.instance.m_buildUi.SearchFieldFocused` | build UI, radial, build search |
| `Hud.IsUserHidden()` | HUD hidden with Ctrl+F3 |
| `GameCamera.InFreeFly()`, `PlayerCustomizaton.IsBarberGuiVisible()` | free camera, barber |

`Chat.HasFocus()` is checked by `InventoryGui.Update` (hotkeys, including close-on-`Use`/Tab), `Player.TakeInput`, `PlayerController.TakeInput`, `GameCamera`, `Minimap.Update`, `StoreGui` and `TextInput`. **A postfix `__result |= OurTextFieldFocused` on `Chat.HasFocus` is the smallest patch that stops the game reacting to keys typed into a custom text field.**

One gap remains: `Chat.Update` runs console `bind` keys (`Terminal.m_binds`) whenever the chat's own input is unfocused. Also suppress those if needed, for example with a prefix that skips binds while our field is focused. The bind loop is the only caller of `Terminal.TryRunCommand(text, silentFail: true, skipAllowedCheck: true)`, so a prefix that skips calls with `skipAllowedCheck` leaves typed commands alone. `UIGamePad.ButtonPressed` can also fire a panel button from a keyboard `m_keyCode` (prefab data).

Cautions (learned for Crafting Search and Sort):
- The `Chat.HasFocus` postfix blocks the **whole** hotkey block of `InventoryGui.Update` (gamepad B and Y included) and all keyboard walking. Raise it only while a field really has the keyboard, never for a mouse-only popup. For a popup that should close on Esc/B, return true only on the frame Esc or `JoyButtonB` goes down, so that key closes the popup and not the inventory.
- Raise the flag until "this frame + 1": the Esc that makes the field lose focus in frame N must still be blocked for `InventoryGui.Update` in frame N, whatever the script order.
- While the inventory is visible, the chat cannot be opened (`Chat.Update`) and neither can the pause menu (`Menu.Update`): only the console (F5) can have the keyboard next to a field in the inventory screen. An IMGUI field (ConfigurationManager) shows as `GUIUtility.keyboardControl != 0`.

---

## 15. Terminal and console commands (`Terminal`, `Console`, `Chat`)

- `Terminal` is the base class of both `Console` (F5; enabled by the `PlatformPrefs` `"EnableConsole"` toggle in Gameplay settings) and `Chat`.
- Commands are `Terminal.ConsoleCommand` objects registered into the static `Terminal.commands` dictionary by their constructor:
  - signature: `ConsoleCommand(command, description, ConsoleEvent | ConsoleEventFailable action, isCheat, isNetwork, onlyServer, isSecret, allowInDevBuild, hideBehindDevCommands, optionsFetcher, alwaysRefreshTabOptions, remoteCommand, onlyAdmin)`
  - vanilla registers in `Terminal.InitTerminal()` *(private static, runs once)*
- `ConsoleCommand.RunAction`:
  - **Cheat commands require `confirmcheats` first**, but only while `Achievements.IsCheatedAtAll()` is false (profile `m_usedCheats`, a cheated world, a cheated item carried, or `Game.isModded` make it true). Every cheat command that runs (including `confirmcheats` itself) permanently marks the profile `m_usedCheats` and increments `PlayerStatType.Cheats`, which in 1.0 affects achievements.
  - Commands returning `false` or a string print an error.
  - Chat refuses cheat commands (`Chat.isAllowedCommand`).
  - `remoteCommand` forwards to the server via `ZNet.RemoteCommand` when not valid locally.
- Cheat commands run only where `Terminal.IsCheatsEnabled()` is true (`devcommands` on **and** `ZNet.IsServer()`): in multiplayer only on the host's own game, never on a client of a dedicated server, even an admin (only `remoteCommand` commands are forwarded to the server). Details: [core-engine.md](core-engine.md), console section.
- UX-relevant vanilla commands:
  - `filtercraft <terms>` (`Player.s_FilterCraft`)
  - `sortcraft <Original|Name|Type|Weight>` (unique key `"sortcraft"`)
  - `inventorysize <rows>` (cheat)
  - `bind <keycode> <command>` / `unbind` / `resetbinds` (stored in `PlatformPrefs` `"ConsoleBindings"`, executed from `Chat.Update`)
- **Our debug commands**: postfix `Terminal.InitTerminal` and create `new Terminal.ConsoleCommand("vm_…", …, isCheat: false)`. Use `isCheat: true` only when the command really cheats, because it taints the player's achievements.

### Per-character and per-machine persistence summary (for UX mods)
| Store | Scope | API |
|---|---|---|
| `Player.m_customData` (public `Dictionary<string,string>`) | per character, saved in the .fch file, ignored by vanilla if unknown | read and write directly |
| Player unique keys (`AddUniqueKeyValue`) | per character | avoid for mod settings: they also drive world-rate and event checks (`RemoveUniqueKeyValue` calls `ZoneSystem.UpdateWorldRates`) |
| `ItemData.m_customData` | per item, saved and synced with its inventory | read and write, then `Changed()` |
| ZDO custom keys | per world object, synced | `zdo.Set("vm_key", …)`, owner only |
| `PlatformPrefs` / BepInEx config | per machine | — |

---

## Feature ideas

### Loot filter (QoL, exists: https://www.nexusmods.com/valheim/mods/116)

- **Feasibility**: easy.
- **Who needs the mod**: client-only. Pickup is an ownership grab followed by a local inventory add (§5); nothing server-side changes.
- **Hooks**:
  - `Player.AutoPickup` (private): transpile the `ldfld ItemDrop::m_autoPickup` into `LootFilter.Allow(ItemDrop)`
  - `ItemDrop.m_autoPickup`, `ItemDrop.m_itemData.m_dropPrefab.name` / `m_shared.m_name`, `ItemDrop.m_itemData.m_shared.m_itemType`
  - `InventoryGui.OnRightClickItem` (private) or a middle-click delegate on `InventoryGrid` elements (not wired by vanilla; add in an `InventoryGrid.UpdateGui` postfix when `m_elements` is rebuilt)
  - `InventoryGrid.UpdateGui` (overlay for filtered items)
  - `Player.m_customData` (per-character list)
  - `Player.m_enableAutoPickup` (private static; V toggle)
- **Sketch**:
  - A transpiler on `Player.AutoPickup` replaces the `m_autoPickup` field check with a static filter that returns `drop.m_autoPickup && !Blocked(prefabName)`. Blocking is per prefab and optionally per `ItemType` or category (e.g. "never trophies, never stone"). Manual E-pickup stays untouched because `Humanoid.Pickup` is not patched.
  - Items are toggled from the inventory with Alt+right-click (or middle-click) and marked by a tinted clone of `InventoryElement.m_noteleport`. The list is stored as a CSV in `Player.m_customData["vm.lootfilter"]`, with a BepInEx config default list.
  - Optional third mode on the V key: Off / All / Filtered.
- **Risks**:
  - Transpiler collisions with other `Player.AutoPickup` patchers (pickup-range, auto-pickup-selector or "craft/pickup" mods). Match the single `ldfld` defensively and fall back to a prefix if it is not found.
  - Filtering by `m_shared.m_name` can collide for modded items that share a token; prefer the prefab name.
  - Remember `m_autoPickup` also carries vanilla's "player just dropped this" state, so never overwrite it; only AND with it.
  - No save or network risk.

### Sort chest (QoL, exists)

- **Feasibility**: easy.
- **Who needs the mod**: client-only. The opener owns the container ZDO while the panel is visible (`InventoryGui.UpdateContainer` requires `Container.IsOwner()`), and `Inventory.Changed` → `Container.OnContainerChanged` → `Save()` writes `ZDOVars.s_items` for everyone.
- **Hooks**:
  - `InventoryGui.Awake` (postfix: clone `m_stackAllButton` into a "Sort" button, label `$vm_sort`)
  - `InventoryGui.m_currentContainer` (private), `Container.IsOwner`, `Container.GetInventory`
  - `Inventory.GetAllItems`, `Inventory.GetWidth/GetHeight`, `Inventory.Changed` (private; reverse patch)
  - `InventoryGui.SetupDragItem` (private; cancel drag first, like `OnStackAll`)
  - optional auto-sort on close: `InventoryGui.CloseContainer` / `Hide` (prefix, still owner)
- **Sketch**:
  1. On click, abort if `!container.IsOwner()` or a drag is in progress.
  2. Take `GetAllItems()`, merge partial stacks of `IsSameType` items (sum `m_stack` up to `m_maxStackSize`, OR the `m_cheated` flags, drop emptied entries from the live list).
  3. Order by a configurable key: item type group → localized name → quality desc → stack desc.
  4. Reassign `m_gridPos` row-major from (0,0), then call `Inventory.Changed()` **once**.
  5. Optional per-container "locked slots" and auto-sort-on-close config.
- **Risks**:
  - `IsSameType` ignores `m_customData`. Never merge stacks whose `m_customData` differ (EpicLoot and other item-data mods).
  - Do not use `Inventory.AddItem` to rebuild (it moves one unit per loop iteration and fires `Changed` per call).
  - UI-replacement mods (e.g. Auga-style reskins) may move or hide `m_stackAllButton`, so null-check and fall back to a hotkey.
  - Other sort mods (Quick Stack–Store–Sort–Trash, AzuAutoStore) add their own buttons in the same spot.

### Sort bags (QoL, exists)

- **Feasibility**: easy.
- **Who needs the mod**: client-only. The player inventory is local and saved in the character file; carts and ships are containers and follow "Sort chest".
- **Hooks**:
  - `InventoryGui.Awake` (button on `m_player`)
  - `Player.GetInventory`, `Inventory.GetHeight` (dynamic: 4 by default, up to 9 via `Player.SetInventorySize` / `"invrows"`)
  - `Inventory.GetHotbar` / row 0
  - `Player.IsItemEquiped`, `Inventory.Changed`
  - `InventoryGui.SetInventorySize` (the panel grows with rows)
  - `Vagon.m_container` and ship `Container` (same code path as chests)
- **Sketch**:
  - Treat "bags" as the player inventory plus cart and ship storage (vanilla 1.0 has no bag items; §1).
  - For the player inventory, sort only rows 1..H-1 and leave row 0 (hotbar) and optionally equipped items where they are (config). Materials fill from the bottom row upward to mirror vanilla `TopFirst` placement.
  - Use the same merge/order/`Changed()` routine as Sort chest, shared from a common library.
  - A "Sort" button next to the player grid, plus a hotkey (checked with `ZInput.GetKeyDown`, gated on `InventoryGui.IsVisible()`).
- **Risks**:
  - The gamepad **radial menu lists items in grid order** (`Inventory.GetAllItemsInGridOrder`), so sorting changes radial slots. Offer "keep hotbar and equipped fixed".
  - Mods adding extra equipment slots (e.g. equipment-slot mods that park items in extra rows) break if their rows get sorted, so detect `GetHeight() > "invrows"` and skip the extra rows.
  - The same `m_customData` caveat as chests.

### Search chest (QoL, new)

- **Feasibility**: medium (UI, focus handling and gamepad).
- **Who needs the mod**: client-only. It only reads inventories; nearby-container search is also read-only, because every client already reloads `s_items` every second (`Container.CheckForChanges`).
- **Hooks**:
  - `InventoryGui.Awake` (create field)
  - `Hud.instance.m_buildUi` → `BuildUi.m_searchField` (private; template to clone)
  - `InventoryGrid.UpdateGui` (private postfix: dim non-matches via `InventoryElement.m_icon.color` using the private `m_elements[y*w+x]`)
  - `InventoryGui.Show/Hide/CloseContainer` (clear state)
  - `Chat.HasFocus` (postfix to block Tab/E/Esc closing while typing; §14)
  - `Localization.instance.Localize`
  - optional: `WearNTear.Highlight`, `PrivateArea.CheckAccess`, `Container.CheckAccess` (private) for "find in nearby chests"
- **Sketch**:
  - Clone the 1.0 build-menu search field into the container header (and optionally the player panel). Normalise the term like `BuildUi.UpdateSearch` (strip spaces, lower-invariant). Match against a cached lower-cased localized name, the `$token`, the prefab name and the `ItemType` name, with prefixes like `t:` / `@crafter`.
  - A postfix on `InventoryGrid.UpdateGui` greys non-matching slots each frame, so the layout never changes and nothing is written.
  - Focus on F / JoyLStick like BuildUi; the field is a `GuiInputField`, so the virtual keyboard works.
  - v2 "where is it": scan `Container` instances within N m (cache them from a `Container.Awake` postfix), skip ones failing ward or privacy access, and `Highlight()` those containing matches.
- **Risks**:
  - Keystrokes leaking into game hotkeys: E and Tab close the inventory in `InventoryGui.Update`, and console binds fire. The `Chat.HasFocus` postfix plus a bind guard fix this. Test on gamepad (`InventoryGrid.UpdateGamepad` ignores focus).
  - Performance: `UpdateGui` runs per frame per grid, so cache localized names (vanilla's `Localization` LRU holds only 100 entries).
  - Conflicts with other mods that also reuse `BuildUi.m_searchField` or re-layout the container panel.
  - Revealing contents of warded or private chests if nearby search ignores access checks.

### Search crafting station (QoL, new)

- **Status**: implemented as [Crafting Search and Sort](../../src/UX/Crafting.SearchSort) (0.1.0). The design that shipped differs from the sketch below: it never touches `Player.s_FilterCraft`, the `"sortcraft"` key or `GetAvailableRecipes` (the console commands keep working and combine with it). One `InventoryGui.UpdateRecipeList` postfix removes the rows that do not match (item name, internal prefab/recipe name, and only the ingredients the row shows) and reorders the rest (a category first as a stable partition, or by name), and a search or sort change calls the vanilla `UpdateCraftingPanel(false)` again. The sort is a button with a menu of categories built on the shared `src/Shared/ItemKinds.cs` classifier, remembered per station type in `Player.m_customData`. See [docs/design/ux-crafting-search-sort.md](../design/ux-crafting-search-sort.md).
- **Feasibility**: easy to medium. Vanilla already has the filter backend (`Player.s_FilterCraft`) and the sort backend (`"sortcraft"`).
- **Who needs the mod**: client-only.
- **Hooks**:
  - `InventoryGui.Awake` (field above `m_recipeListRoot` in `m_crafting`)
  - `Player.GetAvailableRecipes` (postfix filter) or `Player.s_FilterCraft` (vanilla OR filter)
  - `InventoryGui.UpdateCraftingPanel` (private; call to refresh)
  - `InventoryGui.UpdateRecipeList` (private)
  - `InventoryGui.SortMethod` + unique key `"sortcraft"` (sort dropdown)
  - `InventoryGui.Hide` (clear)
  - `Chat.HasFocus` (input block)
  - `Recipe.m_resources[].m_resItem.m_itemData.m_shared.m_name` (ingredient search)
- **Sketch**:
  - MVP: set `Player.s_FilterCraft` from the text field and call `UpdateCraftingPanel(false)` on each change. `Clear()` the list when the text is empty (a `[""]` filter hides everything).
  - Better: a postfix on `Player.GetAvailableRecipes` that does AND matching, including ingredient names and a "craftable only" toggle. Leave `s_FilterCraft` to the console.
  - Add a small dropdown bound to `"sortcraft"` (Original/Name/Type/Weight).
  - Clear on `Hide` so the next station opens unfiltered.
- **Risks**:
  - `s_FilterCraft` is static and shared with the `filtercraft` console command, and a stale filter survives screens. `GetAvailableRecipes` is also called by other mods (recipe browsers, EpicLoot enchant UIs), so our postfix must only filter while our field has text *and* the call comes from the crafting panel. Set a flag around `UpdateCraftingPanel`.
  - Other UI-replacement or crafting-list mods (e.g. "craft from containers", Jotunn-based custom station UIs) re-layout the crafting panel.

### Notifications stay longer and show multiple (QoL, new)

- **Feasibility**: medium.
- **Who needs the mod**: client-only. Rendering is local; network messages arrive as `"ShowMessage"`/`"Message"` RPCs and still end in `MessageHud.ShowMessage`.
- **Hooks**:
  - `MessageHud.ShowMessage` (prefix: reroute TopLeft, and optionally Center, into our feed; keep `AddLog`)
  - `MessageHud.UpdateMessage` (private; leave it running for the center cross-fade buffer)
  - `MessageHud.Awake` (raise the `m_maxUnlockMessages` field before `Start`)
  - `MessageHud.UpdateUnlockMsg` (private; set `Animator.speed` on new unlock instances to lengthen them)
  - `MessageHud.HideAll` (private)
  - `MessageHud.m_messageText` / `m_messageIcon` (templates to clone)
  - `Character.ShowPickupMessage`, `Player.Message` (`log` default false)
  - `Hud.IsUserHidden`
- **Sketch**:
  - Replace vanilla's single top-left line (one message per second, 4 s fade, merged only when consecutive) with a stacked feed: N configurable rows cloned from `m_messageText` + `m_messageIcon`.
  - Each row gets a configurable lifetime and fade. Rows with the same text key (e.g. `$msg_added <item>`) aggregate into "×N" and refresh their timer instead of queueing.
  - Center messages get a small queue with a minimum display time instead of overwrite-on-arrival.
  - Unlock toasts get more slots and a slower Animator. Optionally log pickups into the 50-entry history (`AddLog`) so they show in the Texts dialog.
- **Risks**:
  - Mods that also prefix `ShowMessage` or read `MessageHud` internals (chat/notification overhauls, some Jotunn helpers) conflict.
  - More than 4 unlock slots at 110 px spacing can run off-screen at high UI scale, so clamp to screen height.
  - Honour `Hud.IsUserHidden()` / `showDespiteHiddenHUD`.
  - Performance is negligible if TMP objects are pooled rather than instantiated per message.

### Ping system revamp (QoL, new)

- **Feasibility**: medium to hard (networking, world-space UI, input, gamepad).
- **Who needs the mod**: depends.
  - Every client that should **see** rich pings needs it.
  - The server needs nothing: vanilla routes unknown routed RPCs and ignores unknown method hashes.
  - Vanilla clients keep seeing a basic "PING" if we also emit the vanilla ping or encode our payload in it (below).
- **Hooks**:
  - `Chat.SendPing` (vanilla broadcast of routed `"ChatMessage"` type 3)
  - `Chat.AddInworldText` (private; intercept after permission and sanitising)
  - `Chat.UpdateWorldTexts` (private), `Chat.m_worldTextTTL`, `Chat.m_worldTextBase` (template)
  - `Minimap.UpdatePingPins` (private), `Minimap.AddPin` / `RemovePin` (`PinType.None` + custom `m_icon`)
  - `Minimap.OnMapMiddleClick` / `OnMapDblClick`
  - `ZRoutedRpc.instance.Register<ZPackage>` / `InvokeRoutedRPC(ZRoutedRpc.Everybody, …)`
  - `Player.FindHoverObject` (private; reference raycast: 50 m, `m_interactMask`)
  - `Hoverable.GetHoverName`, `Character`, `ItemDrop`, `Pickable`
  - `Hud.instance.m_radialMenu` (`IRadialConfig`) for a gamepad ping wheel
  - `Game.m_noMap`, `Minimap.m_nomapPingDistance`
- **Sketch**:
  1. **In-world ping key** (config `KeyboardShortcut`, gated by the §14 checks). Raycast from `GameCamera` (~250 m, using the same layers as the private `Player.m_interactMask`: item, piece, piece_nonsolid, Default, static_solid, Default_small, character, character_net, terrain, vehicle, character_ghost). Classify the hit as enemy (`Character`), loot (`ItemDrop`), resource (`Pickable`/`MineRock`/`TreeBase`), structure or ground ("go here"). Double-tap sends "danger".
  2. **Networking.** Send a routed RPC `"VM_Ping"` carrying a `ZPackage` (version, type, true world position, target `ZDOID` if any, localized-name token, colour) to `Everybody`, rate-limited. For vanilla peers also call `Chat.SendPing(pos)`. Modded receivers suppress the vanilla ping from a sender whose `VM_Ping` arrived within ~0.5 s, via an `AddInworldText` prefix.
  3. **Receivers render**:
     - a world-space marker (clone `Chat.m_worldTextBase`, add the item or creature icon from ObjectDB / `Minimap.m_icons`, and live distance)
     - an edge-of-screen arrow
     - a typed minimap pin via `AddPin(save:false)`
     - several simultaneous pings per player
     - configurable TTL
     - an optional sound from an existing `sfx_*` prefab
  4. Tracked targets follow the `ZDOID`. A radial "ping wheel" covers gamepad.
- **Risks**:
  - Spam or griefing: rate-limit on send *and* receive.
  - `OnNewChatMessage` permission checks (platform block lists) are bypassed by a custom RPC unless we reuse `RelationsManager.CheckPermissionAsync`.
  - No-map servers: vanilla hides pings beyond 50 m when `Game.m_noMap`, so respect that for fairness; a client-only mod cannot enforce it.
  - Key conflicts: middle mouse is Attack2/Remove, and gamepad buttons are scarce.
  - World-space UI cost: pool markers.
  - Coexistence with other ping or waypoint mods that patch `Chat.AddInworldText` or `Minimap.UpdatePingPins`.

---

## Cross-cutting notes for UX mods

- **Build UI by cloning vanilla widgets** (`m_stackAllButton`, `BuildUi.m_searchField`, `MessageHud.m_messageText`, `Chat.m_worldTextBase`, `m_recipeElementPrefab`). This avoids AssetBundles and inherits fonts and styles. Use item and pin sprites from `ObjectDB` / `Minimap.m_icons` for icons.
- **A shared `VM.Common.UI` library** in the mono-repo should provide:
  - clone-a-button / clone-a-search-field helpers
  - the `Chat.HasFocus` focus-block registry
  - localization token registration (`SetupLanguage` postfix + `AddWord`)
  - a `Inventory.Changed` reverse patch
  - the stack-merge/sort routine used by all sort features
- **Grouping items "by type"** (sort chest, sort bags, filters): build the groups on `MC.Shared.ItemKinds.Classify(SharedData)` (`src/Shared/ItemKinds.cs`, added with Crafting Search and Sort), so the same item lands in the same group in every MC mod. It keeps tools, pickaxes, the scythe and torches out of the weapons (vanilla `IsWeapon()` counts torches as weapons) and puts tankards (`Feaster` animation) with Misc.
- **Everything here is client-only except pings**, which are opt-in per client. Container edits are safe as long as they happen while `InventoryGui` shows the container (ownership guaranteed).
- **Compatibility checklist**:
  - EpicLoot (`ItemData.m_customData`, tooltips, inventory overlays): never merge stacks with differing custom data; append rather than replace tooltip text.
  - Jotunn (`LocalizationManager`, `InputManager`, GUIManager styles): fine alongside if we do not assume exclusive ownership of `Localization`.
  - ValheimPlus: largely outdated for 1.0; its inventory, map and crafting patches overlap `Player.AutoPickup`, `InventoryGui` and `Minimap`.
  - UI reskins: always null-check cloned vanilla references.
