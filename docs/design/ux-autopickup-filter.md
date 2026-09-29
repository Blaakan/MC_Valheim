# Loot Pickup Filter — design

| | |
|---|---|
| Mod | Loot Pickup Filter |
| GUID / project | `MC.UX.AutoPickup.Filter` (`src/UX/AutoPickup.Filter/`, namespace `MC.UX.AutoPickupFilterMod`) |
| Category / scope | UX / QoL |
| Side | Client (only the player who wants it installs it) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim`, `assembly_utils`, `assembly_guiutils` in `.ref/`; item, creature and piece prefab names checked in the installed game data (`StreamingAssets/SoftRef/manifest_extended`) |
| Status | Implemented (0.1.0), smoke test passed, in-game testing pending |

## Goal

Requirements from the user (2026-09-28):

1. A place in the inventory UI to set up the filter, with three states: (a) everything is picked up normally;
   (b) everything is picked up except ignored items; (c) only selected items are picked up.
2. The in-game auto pickup toggle turns the whole system on and off: auto pickup off picks up nothing; auto pickup on
   goes back to whatever filter is active.
3. Picking up an item manually is not affected by the filter.

Made precise (the numbering used in the tests and the decisions):

1. **Three filter modes, chosen in the inventory UI** (user 1): a button in the player inventory panel shows and
   cycles *Everything* (vanilla), *Skip ignored* (all but the *Ignored* list), *Only selected* (only the *Selected*
   list).
2. **Two separate lists, edited from the inventory UI** (user 1): *Ignored* and *Selected* are independent lists of
   item types (prefab names); switching modes keeps both. The mark gesture (middle-click; controller: right stick
   click on your own inventory) on an item in the player inventory or an open chest adds it to, or removes it from,
   the list of the current mode. Items of the active list carry a red or green badge. Both lists are shown in the
   button tooltip (mouse) and in a lists panel (controller). **0.1.0 scope (lead decision):** no full list editor;
   items you do not carry are handled by picking one up by hand and marking it, or by chat/console commands. The
   editor is planned for 0.2.0.
3. **The vanilla auto pickup toggle stays the master switch** (user 2): off = nothing is auto-picked whatever the
   mode; on = the kept mode applies again. The toggle never resets the mode.
4. **Manual pickup is never filtered** (user 3): E on an item, fishing catches and the console spawn pickup are
   untouched. **Hand harvesting is manual pickup too**: many Use interactions and the scythe spawn their items on the
   ground and only auto pickup collects them (1.6); those drops are exempt ("harvest grace", config
   `Filter.ExemptHarvest`, default on). With auto pickup off they stay on the ground, as in vanilla.
5. **Only the local player's auto pickup changes.** Skipped items stay on the ground as normal items, never claimed:
   other players and the vanilla despawn rules see nothing different.
6. **Settings are saved per character** (mode + both lists), across sessions, deaths and servers.
7. The mod can be turned off on its own, live (framework toggle): off = vanilla at once, lists kept.

Non-goals (0.1.0): filtering manual pickup or hand harvesting; category rules ("all trophies"); a full item browser
or editor panel; marking chest items with a controller; quantity rules; changing the pickup range; a hotkey to change
the mode outside the inventory; remembering the vanilla on/off state across game restarts; server-enforced filters;
touch-screen marking.

---

## 1. Vanilla behaviour (code trace)

### 1.1 Who runs auto pickup

`Player.FixedUpdate` returns unless the player's ZDO is owned and `m_localPlayer == this`, then (alive) calls the
private `Player.AutoPickup(float)`. Auto pickup is 100% local to the local player's client, at physics rate. No RPC,
no server logic.

### 1.2 The gate (`Player.AutoPickup`)

1. Returns at once while teleporting or when the private static `Player.m_enableAutoPickup` (default true) is false.
2. `Physics.OverlapSphereNonAlloc` around `position + up` with the public `m_autoPickupRange` (2) on the `item` layer.
3. Per collider with a rigidbody: its `ItemDrop` (a `FloatingTerrainDummy` resolves to its parent's).
4. **Gate**, short-circuit OR, in this order: no `ItemDrop`, `!m_autoPickup`, **`IsPiece()`**,
   `HaveUniqueKey(m_shared.m_name)` (quest item owned), invalid `ZNetView` → skip.
5. `!CanPickup()` → `RequestOwn()` (asks the current owner for the ZDO), next item.
6. Else: skip items in tar, `Load()`, skip if `!CanAddItem` or overweight or out of range; below 0.3 m
   `Humanoid.Pickup(go)`, otherwise pull the item towards the player at 15 m/s.

Consequence: **`ItemDrop.IsPiece()` is the only `ItemDrop` instance method called in the gate before `RequestOwn`**,
and only for drops whose `m_autoPickup` is true. Making the gate skip there leaves the drop unclaimed. A filter placed
later (the LootFilter mod's `Inventory.CanAddItem` postfix) runs after `RequestOwn`: the client grabs ownership of
items it will never take, again and again. `IsPiece()` itself is `!m_body && m_piece && m_wnt` (item placed on a table
or stand).

### 1.3 The master switch (V)

- `Player.Update`, inside its `TakeInput()` branch: button `AutoPickup` (V), or `JoyAutoPickup` + `JoyAltKeys`, flips
  `m_enableAutoPickup` and shows `$hud_autopickup:` + `$hud_on`/`$hud_off` top-left.
- `Player.TakeInput` is false while the inventory is visible, chat/console/menus are open, dead or teleporting:
  **V does nothing while the inventory is open.**
- The flag is static and never saved: each game start begins with auto pickup on. Nothing else writes it, so within
  one game run it **survives logout and character changes**.

### 1.4 `ItemDrop.m_autoPickup`

Public, default true, can be false in a prefab. `Humanoid.DropItem` calls `ItemDrop.OnPlayerDrop()` for players,
which sets it false **on the dropping client's instance only** (not networked): the dropper never auto-picks it back,
others do. The flag carries vanilla state: a mod must never write it and must only narrow (AND) vanilla's decision.

### 1.5 Ownership and pickup

- `ItemDrop.CanPickup(autoPickupDelay = true)`: false for 0.5 s after spawn, then true only if the local client owns
  the ZDO. `ItemDrop.RequestOwn()` is rate-limited (0.2 s × 2^n, max 30 s) and sends `RPC_RequestOwn`; the owner
  answers with `SetOwner`.
- `ItemDrop.Pickup(Humanoid)` (manual): if owner, pick up; else poll `PickupUpdate` and request ownership.
- `Humanoid.Pickup(go, autoequip, autoPickupDelay)`: sanity checks, quest-item key, `Inventory.AddItem`
  (`$msg_noroom`), network destroy, auto-equip, effects, `$msg_added` top-left.

### 1.6 Every path into `Humanoid.Pickup`, and hand harvesting

| Path | Code | Filtered by the mod? |
|---|---|---|
| Auto pickup | `Player.AutoPickup` | **yes** |
| E / `JoyUse` on an item | `Player.Update` → private `Player.Interact(go, hold, alt)` → `ItemDrop.Interact` → `ItemDrop.Pickup` | no |
| Fishing catch | `FishingFloat.Catch` → `ItemDrop.Pickup` or `Fish.Pickup` | no |
| Console `spawn <item> p` | `Terminal` spawn command → `Player.Pickup(go, autoequip: false, autoPickupDelay: false)` | no |

**Hand harvesting goes through auto pickup.** These interactions spawn an `ItemDrop` on the ground; with V off, vanilla
leaves it there:

| Interaction | Code (spawn) |
|---|---|
| E on a `Pickable` (berries, mushrooms, pickable flint/stone/branches, thistle, crops...) | `Pickable.Interact` → `RPC_Pick` (on the ZDO owner) → `Drop` → `Instantiate` with an upward push |
| Scythe | `Attack` (harvest attack, local player only) calls `Pickable.Interact(Player.m_localPlayer, ...)` **directly** for every harvestable pickable in its radius, not through `Player.Interact` |
| E on a `PickableItem` | `PickableItem.Interact` → `RPC_Pick` → `Drop` |
| Fermenter tap | `Fermenter.Interact` → `RPC_Tap` → `Instantiate(m_to)` |
| Cooking station, take the done item | `CookingStation.Interact` → `RPC_RemoveDoneItem` → `SpawnItem`; a station with an `m_addFoodSwitch` (the stone oven) ignores `Interact` and is used through that `Switch` → `OnAddFoodSwitch` → the same `OnInteract` |
| Beehive, sap collector | `Beehive` / `SapCollector` `RPC_Extract` |
| Item stand, take the item back | `ItemStand.Interact` → `RPC_DropItem` → `DropItem` |
| Armor stand slot, take the item back | E on a slot `Switch` → `ArmorStand.UseItem` → `RPC_DropItemByName` → `DropItem` (the pose switch `m_changePoseSwitch` spawns nothing) |
| Smelter family, empty switch | `Smelter.OnEmpty` (on `m_emptyOreSwitch`) → `RPC_EmptyProcessed` → `SpawnProcessed`; the add-ore/add-fuel switches spawn nothing |
| Archery target, take the arrows | `ArcheryTarget.Interact` → `DropArrows` → `Instantiate(m_returnAmmo[i])` |
| Saddle off (Shift + E on a saddled tame) | `Sadle.Interact` with `alt` → `RPC_RemoveSaddle` → `Tameable.DropSaddle` |

Except the scythe, all of them go through the private `Player.Interact(GameObject, bool hold, bool alt)`, whose gates
are `InAttack()`, `InDodge()` and, for `hold`, 0.2 s since the last interaction; it resolves
`go.GetComponentInParent<Interactable>()`. Smelter output that pops out on its own, a spilling `m_spawnStack` queue
and a destroyed smelter's content are not interactions. Mining, chopping and killing are attacks. Every other vanilla
`Interactable` (door, chest, bed, portal, chair, crafting station, sign, tame, fireplace, turret, ship, cart,
trader...) spawns nothing on Use. In multiplayer the drop is created by the ZDO owner of the bush or station and
reaches us as a new ZDO after the network delay (`ItemDrop.Awake` then sets the local `m_spawnTime`).

### 1.7 Identifying an item type

`ItemDrop.Awake` sets `m_itemData.m_dropPrefab = ObjectDB.instance.GetItemPrefab(GetPrefabName(name))`, the ObjectDB
prefab whose `name` is the spawn name (`Stone`, `Coins`). `ItemDrop.DropItem` instantiates `item.m_dropPrefab`, so a
source item without a drop prefab throws before any drop exists: **a drop with a null `m_dropPrefab` only comes from
the `Awake` lookup failing** (an object whose name is not an ObjectDB item, typically a modded prefab that was not
registered). Fallback key: `Utils.GetPrefabName(drop.gameObject)`. Inventory items get `m_dropPrefab` on add and load.
`m_shared.m_name` is a `$token` that several modded prefabs can share: the key is the prefab name, never the token.

### 1.8 Other auto-pickup facts

- Quest items already owned and items placed on tables/stands (`IsPiece`) are never auto-picked.
- A `Projectile` with `m_respawnItemOnHit` drops the thrown item back through `ItemDrop.DropItem` with `m_autoPickup`
  still true: vanilla auto-picks thrown weapons (which weapons: prefab data, spears).
- `ItemDrop.TimedDestruction` despawns a drop after 3600 s unless inside a player base, a player is within 25 m, it is
  in tar or it is a piece. `ItemDrop.GetHoverText()` builds `name [xN]` + the Use hint; the crosshair calls it every
  frame while hovering.

### 1.9 Inventory screen facts used by the UI

- `InventoryGui.Update` runs every frame; when visible it repaints the player grid and then the container grid
  (container only while owned) through the private `InventoryGrid.UpdateGui(Player, ItemData)`, which rebuilds
  `m_elements` (index `y*width+x`) when the size changes and wires `UIInputHandler` left down/click/up, right down and
  pointer enter per slot. **Middle click is not wired**, although `UIInputHandler` has `m_onMiddleDown/Up/Click`.
- Click semantics: Shift = split, Ctrl = move/drop, right click = `Player.UseItem`. Alt is not read by vanilla but is
  used by QuickStackStore and InventoryActions. Gamepad: D-pad/left stick move, A select (LT split, RT drop), X use.
  `JoyRStick` (right stick click) is not read by vanilla in the inventory. `InventoryGrid.UpdateGamepad` only runs
  while the grid's `m_uiGroup.IsActive` and `ZInput.IsExclusiveGamepadActive()`.
- `InventoryGrid.GetHoveredElement()` is a **pure geometry test** of `ZInput.pointerPosition` against each slot rect:
  it also "finds" a slot covered by another panel (`m_skillsDialog`, `m_textsDialog`, `m_trophiesPanel`,
  `m_achievementsPanel`, `m_variantDialog`, `m_splitDialog`, other mods' menus).
- `InventoryGui.m_uiGroups`: 0 container grid, 1 player grid (default on `Show`), 2 side panels, 3 crafting. Clicking
  a recipe activates group 3, clicking a slot its grid's group. `UIGroupHandler.Update` sets
  `CanvasGroup.interactable` from its priority when its GameObject has a `CanvasGroup` (whether the player group has
  one is prefab data).
- `UIGamePad` clicks its button when its gamepad key is pressed while its group is interactive, **with no modifier
  check** (LT + R3 still fires an R3 `UIGamePad`).
- `InventoryGui.Show(Container, int)` only sets the animator bool and the active group: in a `Show` postfix the panel
  may still sit at its hidden or animating transform. `InventoryGui.Hide` is called every frame while dead or
  teleporting.
- `UITooltip` (not publicized): one static shown instance of `m_tooltipPrefab` with `Topic` and `Text` children,
  `Set(topic, text)`, mouse hover only; `$tokens` in its texts are localized. The shown instance sits in the private
  static `m_tooltip`. `Localize.Start` re-localizes everything under it.

### 1.10 Input facts

`ZInput.GetKeyDown/GetKey(KeyCode, logWarning)` support `Mouse0`-`Mouse4` through the Input System.
`ZInput.IsKeyCodeValid` rejects `None`, `Mouse5`, `Mouse6` and every code above `JoystickButton19`: those never fire.
A keyboard `KeyCode` missing from the private map behind `ZInput.TryKeyCodeToKey` (for example `F13`-`F15`, `Hash`,
`At`) maps to `Key.None`, and reading it **throws `ArgumentOutOfRangeException` on every call** (`logWarning` only adds
a log line before the throw). `TryKeyCodeToKey` is private static (callable through the publicized reference) and
works before `ZInput` exists.

### 1.11 Messages, console, persistence

- `MessageHud` shows **at most one top-left message per second** and queues the rest (identical consecutive texts
  merge as `xN`). `Player.Message` passes `log: false`.
- Console commands: the `Terminal.ConsoleCommand` constructor registers itself in the static `Terminal.commands`;
  defaults are `isCheat: false`, `hideBehindDevCommands: false`; `Chat` refuses only cheat commands. **Tab completion
  only ever completes the first argument** (`Terminal.UpdateInput` calls `tabCycle` on the second word only);
  `GetTabOptions` caches the first fetch unless `alwaysRefreshTabOptions`. The command-name list used to Tab-complete
  the command itself is cached per terminal on first use.
- `Player.m_customData` (public `Dictionary<string,string>`) is written by `Player.Save`, read by `Player.Load`;
  vanilla ignores unknown keys. A respawn copies the dead player's data into the in-memory profile
  (`Game._RequestRespawn` → `PlayerProfile.SavePlayerData`) and loads it into the new body; the `.fch` file is written
  only by `Game.SavePlayerProfile`: periodic world save (1800 s), sleeping, the menu save, server-requested saves,
  logout/quit.

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28 (sources on GitHub / Thunderstore; see also `docs/research/existing-mods-combat-ux.md`):

| Mod | How | Lesson |
|---|---|---|
| Loki's Autoloot Trash Filtering (LCDR, Nexus 116) | Three modes (block all, block the trash list, allow all), list in the config | The three-state model exists; the gap is in-UI editing and an allow-list mode. |
| AutoPickupIgnorer (PipMods) | `Player.AutoPickup` prefix that replaces the loop with a copy adding an ignore check by `m_dropPrefab.name`; hotkey cycles modes; null guard for drops without drop prefab | Key by prefab name with a null guard. Replacing the loop breaks other patches; its copy still calls `IsPiece`, so our hook keeps working under it. |
| LootFilter (wonkotron) | Postfix on `Inventory.CanAddItem` for blacklisted prefab names | Filters after `RequestOwn`: steals ownership of items it never takes. |
| InventoryActions (sighsorry) | Drag onto an "Exclude" button; per-character YAML state; Left Alt + click favourites; controller menu on right stick click | In-UI editing is expected. Alt + click and right stick click are taken: ours must be configurable. |
| Quick Stack Store Sort Trash Restock (Goldenrevolver) | Alt + left/right click favourites (prefixes on `InventoryGrid.OnLeftDown/OnRightDown`), buttons under `m_player/Weight` and above `m_player/Armor`, clones `m_takeAllButton` with its `UIGamePad` disabled | Middle click is free. Keep away from the right-hand column of the player panel. A cloned vanilla button needs its `UIGamePad` handled. |
| RagnarsRokare AutoPickupSelector (deprecated) | Trophies tab turned into a per-item toggle list | Best list-editor idea found: candidate for the 0.2.0 editor. |

Conclusion: filter at the vanilla gate, before `RequestOwn`, by prefab name; leave the loop and every pickup method
untouched; edit the lists where the items are (inventory and chest grids) with a gesture nobody else uses.

---

## 3. Implemented design

### 3.1 Core idea

While the local player's `Player.AutoPickup` runs, the mod answers **"this is a piece"** from `ItemDrop.IsPiece()`
for every drop the filter rejects. Vanilla already skips pieces at that point of the gate: after the `m_autoPickup`
check (the player's own drops stay skipped) and **before** `RequestOwn` (a skipped item is never claimed). Outside
`AutoPickup` the answer is never touched, so manual pickup, hover texts, despawn and eating from tables stay vanilla.

`AutoPickupScope.Active` is opened by a `Player.AutoPickup` prefix (`Priority.First`, local player only, only when
V is on and the mode is not Everything) and closed by a finalizer (it cannot leak, even if vanilla or another mod
throws) and in `OnDeactivated`.

Verdict (`FilterState.Blocks`):

| Mode | Blocked when |
|---|---|
| Everything | never (the scope is not even opened) |
| Skip ignored | the drop's key is in *Ignored*, and it is not a tagged harvest drop |
| Only selected | the drop's key is **not** in *Selected*, and it is not a tagged harvest drop |

Key = `m_itemData.m_dropPrefab.name`, else `Utils.GetPrefabName(drop.gameObject)`. The list verdict is cached per
drop prefab instance id (one dictionary lookup per candidate drop, no allocation after the first sight of a prefab);
drops without a drop prefab use a second cache per drop instance id (cleared past 1024 entries). Both caches are
cleared on any mode, list, setting or character change.

Rejected alternatives: a transpiler on the `m_autoPickup` load (avoidable here, no effect under loop-replacing mods,
collides with other transpilers; kept as the fallback); flipping `m_autoPickup` around the loop (a second overlap
scan every physics frame, mutates shared state); `Inventory.CanAddItem` / `ItemDrop.CanPickup` (after `RequestOwn`,
or shared with manual pickup); replacing the loop (breaks other mods).

### 3.2 Harvest grace (goal 4)

Hand-harvest drops only reach the inventory through auto pickup (1.6), so the mod remembers the local player's own
harvest interactions and exempts the drops they produce (`HarvestGrace`):

- **Record** (never changes the call, `void` prefixes):
  - `Player.Interact(GameObject, bool, bool)` prefix: local player only, vanilla's early gates (attack, dodge, hold
    repeat 0.2 s), and only when `go.GetComponentInParent<Interactable>()` is a kind that drops items on Use:
    `Pickable`, `PickableItem`, `Fermenter`, `CookingStation`, `Beehive`, `SapCollector`, `ItemStand`,
    `ArcheryTarget`, `Sadle` with `alt` (saddle off), a `Switch` that is a smelter's `m_emptyOreSwitch`, a `Switch`
    that is a `CookingStation`'s `m_addFoodSwitch` (the stone oven: taking the done food out), or an `ArmorStand`
    switch other than `m_changePoseSwitch`. Doors, chests, beds, portals, crafting stations, tames,
    signs, items, fish and interactables of other mods record nothing. Position = `go.transform.position`.
  - `Pickable.Interact(Humanoid, bool, bool)` prefix: when `character` is the local player (covers the scythe; for
    E on a pickable both prefixes record, which merges).
  - An entry is a box of positions `{Start, Last, Frame, Min, Max}` in a 64-entry ring. A record merges into the
    newest entry when it is in the **same frame** within 8 m of its box (every plant of one scythe swing, the two
    records of one E press), or within 0.5 m and 0.5 s (holding E). One scythe swing = one entry.
- **Tag, once, when the drop is born**: an `ItemDrop.Awake` postfix calls `CoversSpawn(position, Time.time)`: some
  entry with `Start - 0.05 s <= now <= Last + 3 s` and the spawn position within 4 m of its box. A match puts the
  drop's instance id in a set; an `ItemDrop.OnDestroy` postfix removes it. Owned harvests spawn inside the
  `Interact` call; drops of a bush another client owns arrive within the 3 s window.
- **Verdict**: `Blocks = ListBlocks(drop) && (!ExemptHarvest || !IsTagged(drop))`. A tagged drop stays exempt until it
  is picked up, despawns or unloads, whatever mode, list or setting changes happen meanwhile, and wherever it lands
  (drops walked over much later, drops being pulled towards you). A reloaded drop is a new instance: an ordinary drop.
  Recording and tagging run in every mode and whatever `ExemptHarvest` says, so switching either one later works.
- **Accepted side effect**: a filtered drop that spawns within 3 s and 4 m of your own harvest (a creature killed next
  to the bush) is also exempt. Attacks never record.
- Deciding at spawn replaced a pre-implementation "match position and spawn time when auto pickup looks at the drop",
  which lost scythe drops once the ring had overflowed.

### 3.3 Patches

All bodies catch their own exceptions (`PatchGuard.Report`). Applied only while the feature is Active (framework).
No patch on `Humanoid.Pickup`, `ItemDrop.Pickup`, `ItemDrop.Interact`, `ItemDrop.CanPickup`, `Inventory.CanAddItem`,
`MessageHud.ShowMessage` or the vanilla toggle. No transpiler.

| Target | Type | What it does |
|---|---|---|
| `Player.AutoPickup(float)` *(private)* | Prefix, `Priority.First` | Local player only: `FilterState.EnsureLoaded`; first call after activation: foreign-patch warning (3.8); V flipped back on and mode not Everything → mode message; open the scope. `First`: open before loop-copying prefixes of other mods run. |
| `Player.AutoPickup(float)` | Finalizer | Close the scope. |
| `ItemDrop.IsPiece()` | Postfix | `if (!Active or __result) return;` then `Blocks` → `__result = true`. Outside the scope: one static bool read. |
| `ItemDrop.GetHoverText()` | Postfix | V on, mode not Everything, `ShowInHoverText`, not a real piece, `Blocks`: append a grey `Auto pickup skips this (ignored)` / `(not selected)` line. |
| `ItemDrop.Awake()` *(private)* | Postfix | Harvest grace tag (3.2); Debug line when the lists would block the drop. |
| `ItemDrop.OnDestroy()` *(private)* | Postfix | Untag. |
| `Player.Interact(GameObject, bool, bool)` *(private)* | Prefix (normal priority) | Harvest grace record (3.2). |
| `Pickable.Interact(Humanoid, bool, bool)` | Prefix | Harvest grace record (scythe path). |
| `InventoryGui.Show(Container, int)` | Postfix | Build the button if needed (also on a new `InventoryGui` after a scene reload), mark placement dirty, refresh the label. No geometry here (1.9). |
| `InventoryGui.Hide()` | Postfix | Close the controller lists panel (one bool read when it is not shown). |
| `InventoryGui.Update()` *(private)* | Postfix | Return unless visible; `FilterUi.Tick`: place the button, mark key (in its own try/catch), controller gestures, refresh label/tooltip/lists when the state changed. |
| `InventoryGrid.UpdateGui(Player, ItemData)` *(private)* | Postfix | Badges on the player grid and the container grid only (3.4). |

### 3.4 UI (`FilterUi`, `MarkerOverlay`)

**Mode button.** A clone of `InventoryGui.m_takeAllButton` (same frame, font, sounds), named `MC_LootFilterButton`,
built inside an inactive holder so nothing of it starts before it is stripped of `UIGamePad` (and its in-clone hint),
`UIInputHint`, `UITooltip` and `Localize`. New `onClick` (drops the Take all listener), `Navigation.Mode.None` on
every `Selectable` (the D-pad never lands on it). 220 × 32, docked **above the player panel, left edge on the first
slot's left edge** (`m_player` top-left anchor, pivot bottom-left, 6 px gap, plus `ButtonOffsetX/Y`), outside the
right-hand column QuickStackStore uses. Parent: `m_player`; if `m_player` or a parent below `m_inventoryRoot` carries a
`UIGroupHandler` **and** a `CanvasGroup` (that group would make the button non-interactable after a recipe click), the
button hangs on `m_inventoryRoot` and follows the panel by world position every frame. Placement runs in the `Update`
postfix (never in `Show`), again whenever the panel's size, position or scale or the screen height changes or an
offset setting moves it, skips zero-scale and NaN frames, and is clamped to stay on screen (overlay canvas). Label
(TMP, auto-size down to 10, no wrapping, ellipsis): `Auto pickup: <mode>` or `Auto pickup: Off (<mode>)`. Click: cycle
Everything → Skip ignored → Only selected, mode message, deselect the button (Space/Enter cannot click it again). If
`m_takeAllButton`, `m_player` or `m_playerGrid` is missing (UI reskin), one warning and no button; filter, marking and
commands keep working.

**Tooltip.** A `UITooltip` using the inventory slot tooltip prefab (`m_playerGrid.m_elementPrefab` →
`InventoryElement.m_tooltip.m_tooltipPrefab`), topic `Auto pickup filter`. Text: how to change the mode, the three
modes, how to mark (from the `MarkKey`: `Middle-click`, `Right-click`..., `Hover ... and press <key>`, or that marking
is off), the controller line when a gamepad is active, "Items you do not carry: type /lootfilter in chat", both lists
(the active one first, marked "in use now", sorted by localized name, 40 names each then `+N more`), and the V state
with `$KEY_AutoPickup` / `$KEY_Use`. Item names are `$tokens` from ObjectDB (unknown keys show the raw name). Rebuilt
only when `FilterState.Version`, the V state or the gamepad state changes.

**Lists panel (controller).** Our own instance of the tooltip prefab (`MC_LootFilterLists`, `UITooltip`/`Localize`
removed, `Topic`/`Text` filled with localized strings), bottom-left at the button's top-left, clamped with
`Utils.ClampUIToScreen`. Toggled by RT + R3; closed by the same chord, by `InventoryGui.Hide`, and on deactivate. While
the controller alone is in use (`GamepadControls` on and `ZInput.IsExclusiveGamepadActive()`), the text swaps the mouse
lines for controller ones (LT + R3 mode, R3 mark, RT + R3 close); it switches back when the mouse is used.

**Marking, mouse or keyboard** (`Controls.MarkKey`, default `Mouse2`). Cached on bind and on `SettingChanged`; every
key of the shortcut is checked with `ZInput.IsKeyCodeValid` and, for keyboard keys, `ZInput.TryKeyCodeToKey`: an
unreadable key turns marking off with one warning (and the tooltip says so); an `ArgumentException` the check missed
does the same once. Per frame: `ZInput.GetKeyDown(main, logWarning: false)`, modifiers only after it fired. Then:

1. Skip while dragging, with the split dialog or a covering dialog open (skills, texts, trophies, achievements,
   variant), while `Chat.HasFocus()`, the console or the pause menu is up, while a focused `TMP_InputField` is
   selected (the Crafting Search and Sort field; its `Chat.HasFocus` postfix also covers it), and, for keyboard keys
   only, while an IMGUI field has the keyboard (ConfigurationManager).
2. Slot under the pointer: `m_playerGrid.GetHoveredElement()`, else the container grid's when a container is shown.
3. **Raycast check** (`GetHoveredElement` ignores what covers the slot): `EventSystem.RaycastAll` with a cached
   `PointerEventData`; the first hit, ignoring the vanilla item tooltip (`UITooltip.m_tooltip`), must be inside the
   slot. Only on a key press.
4. `ToggleMark(item)`: key = `m_dropPrefab.name` (none → "This item cannot be filtered."); Everything → hint, no
   change; Skip ignored → toggle in Ignored; Only selected → toggle in Selected. Messages top-left with the item icon
   (`Ignored by auto pickup: $item_stone`...), **rate-limited to one per second** (the MessageHud queue would replay
   them); badges are the main feedback. Marking never touches the item itself.

**Marking, controller** (`Controls.GamepadControls`, default on): only while `ZInput.IsExclusiveGamepadActive()` and
**the player grid's group is active**, on `JoyRStick` down, same skip rules: with `JoyLTrigger` held → cycle the mode;
with `JoyRTrigger` held → toggle the lists panel; else `ToggleMark(m_playerGrid.GetGamepadSelectedItem())`. Chest
slots are mouse only.

**Shared gamepad map of the MC inventory mods** (the Sort Chest design carries the same table):

| Focused group (`InventoryGui.m_uiGroups`) | R3 (`JoyRStick`) | LT + R3 | RT + R3 | L3 (`JoyLStick`) |
|---|---|---|---|---|
| 0 container grid | Sort Chest: sort | Sort Chest: sort (`UIGamePad` has no modifier check); Loot Pickup Filter: nothing | Sort Chest: sort; Loot Pickup Filter: nothing | Sort Chest: change criterion |
| 1 player grid | Loot Pickup Filter: mark / unmark | Loot Pickup Filter: change mode | Loot Pickup Filter: lists panel | vanilla / none |
| 2 side panels, 3 crafting | none of ours | none | none | vanilla multi-craft (crafting group, held) |

Sort Chest's `UIGamePad`s fire only while the container group is active, our gestures only while the player group is
active: one press never reaches both. InventoryActions also uses R3: `GamepadControls = false` leaves it to that mod.

**Badges** (`MarkerOverlay`). An `Image` child per slot (`MC_LootFilterMark`, 16 × 16, 3 px inset,
`raycastTarget = false`), created lazily in the `UpdateGui` postfix for `m_playerGrid` and `ContainerGrid` only. Shown
when `ShowMarkers` is on, the mode is not Everything and the slot's item type is in the active list: red "no entry"
disc (Ignored) or green check disc (Selected), 32 × 32 textures drawn once in code (no asset bundle). The corner is
picked once per grid from the real slot layout: the first of top-right, top-left, bottom-left, bottom-right that
overlaps none of the no-teleport, food, queued and equipped icons, the quality, amount and hotbar-binding texts
(glyph corner only) and the durability bar (else the least covered); logged at Debug. A grid that rebuilds its slots
(other chest size, more rows) rebuilds its badges. Index guard: items outside `0 <= y*width+x < m_elements.Count`
(extended inventory mods) get no badge. `enabled` is set only when it changes; no allocation per frame.

**Messages** (top-left, `Player.Message`, never logged; One Click Repair All mutes only Center):

| When | Message |
|---|---|
| Mode → Everything | `Auto pickup filter: Everything (normal game)` |
| Mode → Skip ignored | `Auto pickup filter: Skip ignored items (N ignored)` |
| Mode → Only selected, list not empty / empty | `Auto pickup filter: Only selected items (N selected)` / `... Nothing is selected yet, so nothing is picked up automatically.` |
| Any of those while V is off | + ` Auto pickup is off: press $KEY_AutoPickup to turn it on.` |
| V turned back on, mode not Everything | the mode message, after vanilla's "Auto pickup: On" (MessageHud queue) |
| Mark / refusal | 3.4 marking (rate-limited) |

The V-flip detection (`AutoPickupScope.LastEnabled`) resets on every character load, so logging in with V still off
shows no message; the next V press does. Texts are English literals; item and key names are vanilla tokens.

### 3.5 Configuration

Section `General` first (framework: `Enabled`, read-only `Status`), then:

| Section | Key | Default | Effect |
|---|---|---|---|
| Filter | `ExemptHarvest` | `true` | Harvest grace on/off (3.2). |
| Controls | `MarkKey` | `Mouse2` | Mark gesture; `None` = off; unreadable keys turn it off with a warning. |
| Controls | `GamepadControls` | `true` | R3 / LT + R3 / RT + R3 in the player grid. |
| Display | `ShowMarkers` | `true` | Badges. |
| Display | `ShowInHoverText` | `true` | Grey hover line. |
| Display | `ButtonOffsetX` / `ButtonOffsetY` | `0` / `0` | Button offset in pixels. |
| Defaults | `IgnoredItems` / `SelectedItems` | empty | Lists of a character that never used the filter (comma-separated spawn names, matched case-insensitively). |

The full player-facing descriptions are in the mod README. Everything applies live: `MarkKey` re-caches, the offsets
mark the placement dirty, the display/harvest/controller settings bump `FilterState.Version` (caches cleared, label and
tooltip redrawn), and the `Defaults` apply at once to a character that never used the filter.

### 3.6 Persistence (goal 6)

Per character, in `Player.m_customData` (saved in the `.fch`, follows the character to any server, ignored by
vanilla): `MC.UX.AutoPickup.Filter.Version` = `1`, `.Mode` = `Everything` | `SkipIgnored` | `OnlySelected`,
`.Ignored` and `.Selected` = comma-separated prefab names, sorted ordinal.

- **Load** lazily whenever `Player.m_localPlayer` is not the player the state was loaded for (AutoPickup prefix, UI,
  commands); also clears the harvest grace and resets the V-flip detection. No `.Mode` key → Everything + the
  `Defaults` lists, **nothing written** until the player changes something (config edits still apply). Unknown mode
  text → Everything + one warning; another format version → one warning, read anyway; unknown item names are kept.
- **Save**: every change (mode, toggle, clear, defaults) writes all four keys into `m_customData`. They reach the file
  when the game saves the character (1.11); a respawn carries them in memory; a crash loses changes since the last
  save, like everything else in the character.
- Why per character, not a global list: what is junk depends on the character's progress, the lists must follow the
  character between machines and servers, and in-game edits would otherwise rewrite the `.cfg` on every click and wake
  the framework's config watcher. The config holds only defaults; the commands cover items not at hand.
- Off or uninstalled: four short unused strings stay in the character.

### 3.7 Chat / console commands (`LootFilterCommand`)

Three non-cheat commands (no achievement taint; they work in chat as `/lootfilter...` and in the F5 console), registered
in `OnActivated` and removed in `OnDeactivated` (only if the entry is still ours). Because only the first argument is
Tab-completed, the item is always the first argument.

| Command | Effect |
|---|---|
| `lootfilter` | Mode, V state, both lists (`key (display name)`), usage line. |
| `lootfilter <item>` | Toggle in the list of the current mode; in Everything mode an explanation, no change. |
| `lootfilter mode everything\|skip\|only` | Set the mode (also `all`, `skipignored`, `ignored`, `onlyselected`, `selected`); mode message. |
| `lootfilter clear ignored\|selected` | Empty one list (also `ignore`, `select`). |
| `lootfilter defaults` | Replace both lists with the `Defaults` config lists. |
| `lootfilter_ignore <item>` / `lootfilter_select <item>` | Toggle in Ignored / Selected whatever the mode; a note when that list is not the one in use. |

`<item>` is matched case-insensitively against ObjectDB item names (stored with the exact name); an unknown name that
is already in the list can still be removed (item of an uninstalled mod); else `Unknown item: X (use the spawn name;
press Tab to complete)`. Tab options come from `ItemCatalog` (sorted names, rebuilt only when ObjectDB changes;
`alwaysRefreshTabOptions` because the first Tab may happen in the main menu). Without a local player: `Load a character
first.`

### 3.8 Creation, cleanup, live toggle, guards

- `OnActivated`: **IL check** (`AutoPickupGuard.Verify`): the original IL of `Player.AutoPickup`
  (`PatchProcessor.GetOriginalInstructions`) must contain, in this order, a load of `ItemDrop.m_autoPickup`, a call to
  `ItemDrop.IsPiece` and a call to `ItemDrop.RequestOwn`; otherwise it throws and the framework shows "Error: could not
  start (the game may have changed)" instead of a silently dead filter. Then reset the scope, the state (reload from the
  character on next use) and the harvest grace, register the commands, and build the button if an `InventoryGui`
  exists (live toggle with the inventory open).
- **Foreign-patch warning**, once per activation on the first `AutoPickup` prefix call (all mods are loaded by then):
  `Harmony.GetPatchInfo(Player.AutoPickup)`; every other owner's transpiler, and every other owner's prefix returning
  `bool` (it can skip the original), is named in one warning: `Another mod changes Player.AutoPickup (<owners>). If
  auto pickup ignores the Loot Pickup Filter, that mod replaces the vanilla item checks.` No other action.
- `OnDeactivated` (patches still on): close the scope, destroy the button, lists panel, badges, sprites and textures,
  unregister the commands, reset the state and the harvest grace; each step in its own try/catch. The lists stay in
  the character. Everything is rebuilt lazily when the feature comes back.

### 3.9 Performance

| Hook | Frequency | Cost |
|---|---|---|
| `IsPiece` postfix | every call in the game | one static bool read outside auto pickup |
| same, inside auto pickup | per candidate drop in 2 m, 50 Hz, mode ≠ Everything, V on | instance id + one dictionary lookup; the tag set only when the list verdict blocks |
| `AutoPickup` prefix / finalizer | 50 Hz | a few field reads |
| `InventoryGui.Update` postfix | every frame | `IsVisible()` and return while closed; while open: one key read (+ `JoyRStick`), version compares, a placement check; the raycast only on a key press |
| `Player.Interact` / `Pickable.Interact` prefixes | per interaction (per frame while E is held; per plant of a scythe swing) | a type switch or position read, a ring write or box merge |
| `ItemDrop.Awake` postfix | every drop created (spawns, zone loads) | one float compare unless something was recorded in the last 3 s; then at most 64 entries |
| `ItemDrop.OnDestroy` postfix | every drop destroyed | one `Count` read when nothing is tagged |
| `UpdateGui` postfix | per visible vanilla grid per frame | loop of the live item list, int lookups, no allocation |
| `GetHoverText` postfix | per frame while hovering an item | lookups; a string concat only when the line is shown |

`Object.name` allocates: it is read once per prefab (verdict cache), never per frame. Label, tooltip and lists text
are rebuilt only on a state change.

### 3.10 Multiplayer and hand-off

Client-side, compatible (`ModMultiplayerNotes`: "Only you need it; it works on vanilla servers and with friends who do
not have it. Items your filter skips stay on the ground as normal items that anyone can pick up."). Nothing is sent,
nothing is written to items or ZDOs. A skipped item is never claimed (no `RequestOwn`), so other players pick it up as
usual, even while you stand on it: a friend without the mod with vanilla rules (M02), a friend with the mod with their
own filter (M03). Marks are per character and item type, never on items: a marked item handed over is a normal item
(M02). A character played later without the mod loads normally (M06). Harvest grace is local: it records only the
local player's interactions and changes only the local decision; drops of a bush another client owns arrive within
the 3 s window (M05). Dedicated servers never load it (client `BepInProcess`).

### 3.11 Files

`Plugin.cs` (config, `OnActivated` / `OnDeactivated`), `FilterMode.cs`, `FilterState.cs` (mode, lists, load/save,
verdict caches, changes), `AutoPickupScope.cs`, `AutoPickupGuard.cs` (IL check, foreign-patch warning),
`HarvestGrace.cs`, `ItemCatalog.cs` (ObjectDB names, tokens, Tab list), `FilterUi.cs` (button, tooltip, lists panel,
gestures, messages), `MarkerOverlay.cs` (badges, corner, sprites), `LootFilterCommand.cs`, `DebugLayoutDump.cs` (Debug
builds only), `Patches/` (`PlayerPatches`, `PickablePatches`, `ItemDropPatches`, `InventoryGuiPatches`,
`InventoryGridPatches`).

### 3.12 Debug aids (Debug log level only)

- Activation: the IL indices of the gate check; the mark key.
- First inventory opening: the button parent (`player panel` or `inventory root ...`). The first time a grid is drawn
  in Skip ignored or Only selected mode: its badge corner with the occupied boxes it avoided. Debug builds also dump once per `InventoryGui` the layout: screen size, every
  child of `m_player`, the chain from `m_player` up with `CanvasGroup` / `UIGroupHandler` flags, `m_uiGroups`, the
  player name, armor, weight, grid, container, crafting and button rects, and the slot prefab children. Confirms the
  unverified placement facts (T01, T13, T27) from the log.
- Each character load, mode or list change, and each harvest drop that gets through the filter.

### 3.13 Coordination with other MC mods

- **Crafting Search and Sort**: its search row and sort menu live in `m_crafting`; our button is above `m_player`. Its
  `Chat.HasFocus` postfix (true while its field has focus) and our focused-`TMP_InputField` check both stop the mark
  gesture while typing; our button is deselected after a click, so Space/Enter typed in its field never cycle the mode
  (C01).
- **Batch Station Feeding**: both prefix `Player.Interact`. Ours runs at normal priority, before its `Priority.Low`
  prefix, and never skips the call, so the harvest record happens even when it replaces the press with a batch; its
  `MessageHud.ShowMessage` prefix mutes only Center messages during its own batch loop, and ours are top-left (C02).
- **One Click Repair All**: mutes only Center messages during its loop; ours are top-left (C03).
- **Sort Chest** (upcoming): buttons on the container panel and R3/L3 while the container grid is focused (gamepad map
  in 3.4). Its cross-mod test with this mod goes into its own `TESTING.md`.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Mode Everything | Every item in 2 m auto-picked | Identical (scope never opened) |
| Skip ignored, ignored item on the ground | Picked up | Stays, not pulled, not claimed; hover line; E picks it up |
| Only selected, item not selected | Picked up | Stays (same) |
| Only selected, empty list | — | Nothing auto-picked; warning when switching |
| V off / back on | Nothing / everything | Nothing / the kept mode; message names it; button shows `Off (<mode>)` |
| V off, log out, log in (same game run) | Still off | Still off; button shows it at once; no mode message until V |
| V pressed with the inventory open | Nothing | Same |
| E on a bush / pickable / fermenter / cooking station / beehive / sap collector / item or armor stand / archery target / smelter empty switch, Shift + E saddle off, scythe | Drops auto-picked | Same (harvest grace), whatever the lists; `ExemptHarvest = false`: filtered |
| E on the stone oven's food switch (take the bread out) | Drops auto-picked | Same (harvest grace): a `Switch` that is a `CookingStation.m_addFoodSwitch` is recorded (T37) |
| Scythe over a big field, drops collected much later; mode or list changed meanwhile | Auto-picked | Still collected (tag at spawn, never expires while the drop exists) |
| Same with V off | Drops stay | Same |
| Kill or mining next to a bush just harvested (3 s, 4 m) | Auto-picked | Also exempt (accepted side effect) |
| Door, chest, bed, portal or crafting station used, kill next to it | Auto-picked | Filtered (these kinds record nothing) |
| Feeding a smelter or kiln; its output popping out on its own | Auto-picked | Filtered |
| Use on another mod's interactable that drops items | Auto-picked | Filtered (no harvest grace for unknown kinds) |
| Item you dropped yourself | Never auto-picked by you | Same |
| Item another player dropped | Auto-picked | Follows your filter |
| Manual pickup, eating from a table, fishing, console spawn pickup | Picked / eaten | Identical in every mode |
| Inventory full, too heavy, in tar, quest item owned, item on a stand | Skipped | Same |
| Thrown spear falling back as an item | Auto-picked | Follows the filter |
| Drop without `m_dropPrefab` (unregistered modded prefab) | Auto-picked | Filtered by object name; such an inventory item cannot be marked (message) |
| Modded items sharing a display name | — | Separate entries (prefab names) |
| An item type in both lists | — | Only the list of the current mode matters |
| Skipped items near a base | Despawn rules | Same rules (they can pile up; vanilla auto-stacking above 200 drops applies) |
| Mark gesture in Everything mode | — | No change; hint |
| Mark gesture while dragging, with the split dialog, over a dialog or another mod's menu | — | Ignored (dialog checks + UI raycast) |
| Marking many items quickly | — | Badges at once; messages at most one per second |
| Controller, container grid focused, R3 | — | Nothing of ours (left to Sort Chest) |
| Controller: mark a chest item | — | Not possible in 0.1.0; move it to your inventory |
| Item type you no longer carry in a list | — | Shown in tooltip / lists panel; removed by command or by picking one up and marking it |
| Recipe clicked, then the button | Crafting group active | Button still clickable (parent choice) |
| Keyboard mark key while typing in chat, console, a TMP field or ConfigurationManager | — | Ignored |
| MarkKey unreadable (Mouse5, Mouse6, F13-F15, `#`, `@`...) | — | Marking off, one warning, tooltip says so; controller and label keep working |
| Controller alone, lists panel open | — | Controller wording; mouse moved → mouse wording |
| Chest sorted by another mod | — | Badges follow the items (recomputed every frame) |
| Inventory rows added | Panel grows | Button re-docked; badges rebuilt |
| Death and respawn, logout, other server, other character | — | Mode and lists kept per character; a new character gets the Defaults |
| Feature turned off | — | Vanilla at once; UI removed; lists kept |
| Another mod replaces the loop (AutoPickupIgnorer) | Its loop | Our scope opens first and its copy still calls `IsPiece`: filter applies |
| Another mod transpiles `AutoPickup` or adds a bool prefix | Its behaviour | One warning naming it |
| A game update removes `IsPiece` from the gate | — | Activation check fails: "Error: could not start" |
| Extended inventory mod adding slots | — | Badges only on valid grid indices; no exception |
| Inventory layout not recognised | — | No button, one warning; filter, marking, commands work |

---

## 5. Decisions and open questions

### Decisions

Accepted by the lead before implementation. The ones marked **(to confirm with the user)** change what the player
sees and have a simple alternative.

1. **Gate hook = `ItemDrop.IsPiece` postfix scoped to `Player.AutoPickup`** (implementer): no transpiler, filter before
   `RequestOwn`, works under loop-replacing mods, guarded by the activation IL check. Risk: if a future runtime inlined
   `IsPiece` the postfix would not be seen (T03 would catch it); fallback = a transpiler on the gate.
2. **The mark gesture edits the list of the current mode; in Everything mode it only shows a hint** (to confirm with
   the user). Badges show only the active list, so a mark added in Everything mode would be invisible. Alternative:
   Everything mode edits the Ignored list.
3. **Only selected with an empty list picks up nothing** (to confirm with the user): the literal reading of "only
   selected items are picked up"; the switch message warns about it.
4. **The vanilla toggle stays the only on/off switch and is not saved** (to confirm with the user): the mode survives
   the toggle; the on/off state resets to on at each game start, as in vanilla. The button never toggles V.
5. **"Manual pickup" includes fishing, the console spawn pickup and hand harvesting** (to confirm with the user):
   hand-harvest drops are exempt through the harvest grace (3.2, `ExemptHarvest`, default on), decided once when the
   drop spawns, only for the local player's own Use on kinds that drop items (and the scythe). Side effect: a filtered
   drop that appears within 3 s and 4 m of such a harvest is exempt too. With V off harvest drops stay on the ground.
6. **Thrown weapons that fall back as items are ordinary items** (vanilla-consistent, to confirm with the user): in
   Only selected mode a thrown spear stays on the ground unless selected.
7. **Storage per character in `Player.m_customData`**, config only for defaults (to confirm with the user; reasons in
   3.6).
8. **Key = prefab name**, never the localized token.
9. **Default gesture = middle click**, not Alt + click (QuickStackStore and InventoryActions use Alt); configurable.
10. **Button above the top-left of the player panel**, cloned from Take all (room to verify in game, T01; offsets
    configurable). The right-hand column is left to QuickStackStore.
11. **English UI texts** in 0.1.0 (to confirm with the user); item and key names are vanilla tokens.
12. **Badges only on the two vanilla grids** (player and container panel, which also shows carts, ships, tombstones).
13. **No full list editor in 0.1.0** (lead decision, to confirm with the user): the three modes are set with the
    button; the lists are edited in the UI only for item types in your inventory or an open chest (mouse; controller:
    your inventory only) and shown in full in the tooltip and the lists panel. For anything else the README explains
    the two ways: pick one up by hand (never filtered) and middle-click it, or use `lootfilter` /
    `lootfilter_ignore` / `lootfilter_select`. The editor panel is planned for 0.2.0.
14. **Shared gamepad map** (3.4): our controller gestures work only while the player grid is focused; chest slots are
    marked with the mouse only. This keeps R3 free for Sort Chest in the container grid.
15. **Mark messages rate-limited** to one per second; badges are the main feedback.
16. **Display name "Loot Pickup Filter"** (lead decision, to confirm with the user: the GUID `MC.UX.AutoPickup.Filter`
    and the display name are permanent after release). UI labels keep "Auto pickup".

### Added beyond the request

The grey hover line (`ShowInHoverText`, T04), the mode message when V turns auto pickup back on (T10), marking from
chests (T12), controller gestures and the lists panel (T18, T35), the three commands (T17), the `Defaults` config lists
(T16), the foreign-patch warning (C07), the automatic badge corner (T13), the unreadable-`MarkKey` guard (T34). The
harvest grace is not an extra: it is how goal 4 holds for hand harvesting.

### Differences from the pre-implementation design

- The mod name is "Loot Pickup Filter" (not "Loot Filter") in the MC Mods panel, the `lootfilter` status line, the
  command descriptions and the foreign-patch warning; the other log and console lines keep the short prefix
  `Loot filter`.
- **Harvest grace**: decided once at spawn (`ItemDrop.Awake` tag, `OnDestroy` untag) instead of at pickup time; ring of
  64 boxes, one per frame (one per scythe swing); records only for Use targets that drop items (list in 3.2; added
  `ArcheryTarget` and Shift + E saddle off, excluded the armor stand's pose switch); the `Player.Interact` prefix
  skips the calls vanilla refuses (attack, dodge, hold repeat).
- **Badge corner** chosen from the real slot layout instead of a fixed top-right.
- **MarkKey** validated on bind (`IsKeyCodeValid` + `TryKeyCodeToKey`) with a catch for the rest; the mark key has its
  own try/catch in `Tick`.
- Extra skip rules: the pause menu; for keyboard mark keys, a focused IMGUI field. The chat, console and focused
  text-field checks apply to the mouse too.
- Button placement is redone while the panel moves or scales (open animation) and when the screen height changes, and
  is clamped on screen. The lists panel moves only when the button moved.
- The clone is built inside an inactive holder and also loses `UIInputHint` and `UITooltip` (as in Crafting Search and
  Sort). No extra click sound (the clone keeps its own). The label uses `textWrappingMode = NoWrap`
  (`enableWordWrapping` is obsolete in this TMP version).
- The tooltip marks the active list "in use now" and puts it first; the lists panel speaks controller only while the
  controller alone is in use.
- `Defaults` names are matched case-insensitively against ObjectDB; editing them applies at once to a character that
  never used the filter.
- Commands: extra mode and clear words; an unknown item already in a list can be removed; `lootfilter_ignore` /
  `lootfilter_select` note when their list is not in use; the status ends with a usage line; `lootfilter <item>` in
  Everything mode prints an explanation instead of the mark hint.
- A saved format version other than `1` logs a warning and is read anyway.
- Files: `DebugLayoutDump.cs` at the mod root (Debug builds only, also logs the player name, armor, weight rects and
  the screen size); `AutoPickupGuard.cs`; new `ItemCatalog.cs`.

### Open questions

1. **Editor panel (0.2.0)**: a panel opened from the button listing the active list (icon + name, click to remove) and
   known items (`Player.m_knownMaterial` mapped to ObjectDB prefabs) with a search field and per-item toggles, like
   the deprecated AutoPickupSelector. Needs its own panel, gamepad group and text-focus handling (share the pattern of
   Crafting Search and Sort).
2. Category rules ("all trophies", "all Meadows materials").
3. Marking items on the ground (hover + key); middle mouse is a gameplay button outside the inventory, so it needs
   another key.
4. A hotkey to change the mode outside the inventory.
5. **Stone oven** (found in the documentation review, fixed before the first commit): the oven is used through its
   food `Switch` (`CookingStation.m_addFoodSwitch`, see 1.6 and `docs/game/farming-cooking.md` section 14), so the
   `Player.Interact` prefix resolves that `Switch`. `DropsOnUse` now records a `Switch` that is a `CookingStation`'s
   `m_addFoodSwitch` (not its fuel switch), so baked food taken out of the oven is exempt like food from a cooking
   station (T37). Any other `CookingStation` with a food switch (the Frost Foundry's cast slot) follows the same
   rule; whether its output drops on the ground is unverified.

### Unverified names and values

- Where the button ends up (room above the panel at 1080p and 1440p, overlap with the player name), which parent it
  gets (whether the player group's `UIGroupHandler` has a `CanvasGroup`), the badge corner and the lists panel
  position: confirmed only by the Debug layout lines and in game (T01, T13, T18, T27).
- The physical buttons behind `JoyRStick`, `JoyLTrigger`, `JoyRTrigger` on the Alternative 1 and 2 layouts.
- Which thrown weapons use `m_respawnItemOnHit` (spears); which smelter-family stations have an empty switch
  (`m_emptyOreSwitch`: windmill, spinning wheel?); cooking, fermenting and production times; the scythe's harvest
  radius; whether spawned pickables and crops can be picked and scythed at once; saddling a Lox right after `tame`;
  whether the item stand takes a `TrophyBoar`; whether `SpearFlint` shows a quality number (`m_maxQuality > 1`); the
  oven's `BreadDough` recipe and baking time.
- The `m_player` children `Armor` / `Weight` (names from QuickStackStore's source; the dump logs `m_armor` /
  `m_weight` rects instead).
- Checked: item, creature and piece prefab names used in `TESTING.md` exist in the 1.0.16 game data; `debugmode` + K
  runs `killenemies` (`Player.Update`); `spawn <item> p` picks up through `Player.Pickup`; `inventorysize` is a cheat,
  server-only command and shrinking drops items that no longer fit; `nocost` lists every piece in the hammer menu
  (`PieceTable.UpdateAvailable`) and skips the workbench check; Tab completion also works in the chat
  (`Chat.m_tabPrefix` is `/`); a removed command answers `'<name>' is not a recognized command`
  (`Terminal.TryRunCommand`).

---

## 6. Tests

The in-game checklist lives next to the code: `src/UX/AutoPickup.Filter/TESTING.md` (run
`./tools/Get-TestTodo.ps1 -Mod AutoPickup`; smoke test `./tools/Test-Smoke.ps1 -Mod AutoPickup`). It covers every goal
item: T01-T03, T06-T09 and T12 (goals 1-2 / user behaviour 1), T10 and T28 (goal 3 / user behaviour 2), T04, T11,
T24-T25, T30-T33, T36 and T37 (goal 4 / user behaviour 3), M01-M03 and M05 (goal 5), T15-T16 and M04 (goal 6), T21-T22
(goal 7); T13, T17-T20, T23, T26-T27, T29, T34-T35 the rest. M02 (items) and M06 (character) are the hand-off tests.
C01-C03 cover the MC mods that share methods or screens (Crafting Search and Sort, Batch Station Feeding, One Click
Repair All); C04-C07 third-party mods. The cross-mod test with Sort Chest goes into that mod's own `TESTING.md`.
