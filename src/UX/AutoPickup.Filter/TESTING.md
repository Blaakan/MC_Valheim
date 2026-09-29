# Loot Pickup Filter — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches cleanly, the activation check of `Player.AutoPickup`
passed, JitCheck clean: 310 methods, 0 failures). No in-game test yet.

**Setup:** use a **test character and a test world** (cheats mark the character). Press F5 for the console →
`devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `nocost` lets you build any piece
without materials or a workbench nearby (type it again to turn it off); pieces can also be placed with
`spawn <name>`.
- Spawn with `spawn <Name> [amount]` (Tab autocompletes). Items, creatures and pieces below were checked in the
  1.0.16 game data (`StreamingAssets/SoftRef/manifest_extended`):
  - items: `Stone`, `Wood`, `Resin`, `Flint`, `Coins`, `Amber`, `Ruby`, `Raspberry`, `Feathers`, `BoneFragments`,
    `LeatherScraps`, `TrophyBoar`, `SpearFlint`, `ArrowWood`, `CopperOre` (not teleportable: shows the vanilla
    no-portal icon), `TinOre`, `RawMeat`, `Coal`, `Barley`, `GreydwarfEye`; tools `Hammer`, `PickaxeAntler`,
    `Scythe`, `Cultivator`, `FishingRod`, `FishingBait`, `SaddleLox`;
  - creatures: `Boar`, `Greydwarf`, `Lox`;
  - pieces (build them with the hammer): wood chest `piece_chest_wood`, `fire_pit`, `piece_cookingstation`, stone
    oven `piece_oven`, item stand `itemstand`, `ArmorStand`, `piece_ArcheryTarget`, `wood_door`, `charcoal_kiln`,
    `fermenter`, `piece_beehive`, `piece_sapcollector`; items for them: `CookedMeat` (what `RawMeat` becomes),
    `BreadDough`;
  - pickables: `RaspberryBush`, `Pickable_Flint`, `Pickable_Stone`, `Pickable_Mushroom`, `Pickable_Barley`,
    `Pickable_Carrot`.
- Spawned items appear about 2 m in front of you, at the edge of the auto pickup range (some get picked up at
  once): turn auto pickup **off with V before spawning** (or step back), set the filter, then press V again and walk
  over them. `removedrops` clears the ground. V does nothing while the inventory is open (vanilla).
- **(unverified)**: that spawned pickables and crops (`spawn RaspberryBush`, `spawn Pickable_Barley`) can be picked
  and scythed at once (otherwise use wild ones: raspberry bushes, flint and mushrooms in the Meadows); the scythe's
  harvest radius; cooking, fermenting and bee/sap production times; which production stations have an "empty" switch
  that drops their output (windmill, spinning wheel); which thrown weapons fall back to the ground as items (spears);
  that a Lox can be saddled with `SaddleLox` right after `tame`; that an item stand (`itemstand`) takes a
  `TrophyBoar` (T24); that `SpearFlint` shows a quality number in its slot (T13); that the stone oven bakes
  `BreadDough` into bread, and how long it takes (T37); where the button, the badges and the controller lists panel
  end up on screen (T01, T13, T18, T27); the physical buttons behind the right stick click, LT and RT on the
  Alternative 1 and 2 gamepad layouts (T18).

Keep `./tools/Watch-Log.ps1 -Mine` open. With Debug logging (`./tools/Setup.ps1 -DevBepInExConfig`), the mod also
logs:
- at start: `Player.AutoPickup gate checked: ...` and `Loot filter mark key: Mouse2.`;
- on the first inventory opening: `Loot filter button parent: player panel.` (or `inventory root ...`); Debug builds
  (the default `dotnet build`) also log `Inventory layout (world rects = screen pixels ...)` with the rects of the
  player panel, its children, the player name, armor, weight, grid and the button;
- the first time a grid is drawn in Skip ignored or Only selected mode: `Loot filter badges on the player grid:
  corner ...` (and `... container grid ...` for chests) with the slot parts it avoided;
- each character load (`Loot filter loaded for this character: ...` or `... character never used it ...`), each
  mode or list change (`Loot filter: added Stone to Ignored ...`), and each harvest drop that gets through the filter
  (`Harvest grace: Raspberry dropped by your own harvest will be picked up despite the filter.`).

## 0.1.0 — single player

- [ ] **T01 Button and modes (user behaviour 1):** open the inventory (Tab), at 1080p and, if you can, 1440p.
  Expected: a button `Auto pickup: Everything` just above the top-left corner of your inventory panel, its left edge
  lined up with the first slot, not overlapping the player name, the armor/weight numbers, the grid, the left column (skills,
  trophies...) or the crafting panel, and fully on screen (unverified position: with Debug logging, note the
  `Inventory layout` line). Click it three times. Expected: `Skip ignored` → `Only selected` → `Everything`, with a
  top-left message each time (`Auto pickup filter: Skip ignored items (0 ignored)`...). Hover it. Expected: a tooltip
  `Auto pickup filter` that explains the modes, says how to mark items and lists both lists (in Skip ignored or Only
  selected, the list in use comes first, marked "in use now"). Press Space and Enter after a click: the mode does
  not change again.
- [ ] **T02 Everything = normal game:** mode Everything, V off, `spawn Stone 10`, `spawn Wood 10`, V on, walk over.
  Expected: both picked up as usual.
- [ ] **T03 Skip ignored:** carry some Stone. Mode Skip ignored, middle-click the Stone slot. Expected: a red mark on
  the Stone slot, message "Ignored by auto pickup: Stone". V off, spawn 10 Stone and 10 Wood, V on, walk over.
  Expected: Wood picked up; Stone stays on the ground and is not pulled towards you.
- [ ] **T04 Hover line and manual pickup:** look at a Stone on the ground (still ignored). Expected: under its name a
  grey line "Auto pickup skips this (ignored)"; press E: it is picked up. Set `ShowInHoverText = false`: the line is
  gone at once. Set it back to `true`.
- [ ] **T05 Unmark:** middle-click the Stone slot again. Expected: mark gone, message "No longer ignored: Stone"; walk
  over the remaining Stone: picked up.
- [ ] **T06 Only selected:** pick up some Coins (Everything mode), switch to Only selected, middle-click the Coins
  slot. Expected: green mark, message "Selected for auto pickup: Coins". V off, spawn 5 Coins, 5 Stone and 5 Wood,
  V on, walk over. Expected: only the Coins are picked up; the others show "Auto pickup skips this (not selected)".
- [ ] **T07 Lists kept per mode:** ignore Stone (Skip ignored) and select Coins (Only selected). Switch between the
  modes. Expected: in Skip ignored only Stone shows a red mark; in Only selected only Coins shows a green mark; in
  Everything no marks; the tooltip lists both lists in every mode. Set `ShowMarkers = false`: the marks disappear
  at once; set it back to `true`: they are back.
- [ ] **T08 Mark in Everything mode:** mode Everything, middle-click an item. Expected: message "Choose Skip ignored or
  Only selected on the Auto pickup button first.", no mark, lists unchanged (tooltip).
- [ ] **T09 Empty Selected list:** `lootfilter clear selected` in the F5 console, then switch to Only selected.
  Expected: the message "... Nothing is selected yet, so nothing is picked up automatically."; walk over spawned
  items: nothing is auto-picked; E still picks up.
- [ ] **T10 V is the master switch (user behaviour 2):** mode Skip ignored with Stone ignored, inventory closed, press
  V. Expected: the game's "Auto pickup: Off"; walking over Wood picks up nothing; open the inventory: the button
  reads `Auto pickup: Off (Skip ignored)` and its tooltip says auto pickup is off. Close it, press V again. Expected: the game's
  "Auto pickup: On", then "Auto pickup filter: Skip ignored items (1 ignored)"; Wood is picked up, Stone is not. In
  Everything mode, V on shows only the game's message.
- [ ] **T11 Your own drops:** in each mode, drop a Wood stack from the inventory (drag it out) and stand on it.
  Expected: never auto-picked back (normal game), E picks it up.
- [ ] **T12 Marking from a chest:** put Resin in a wood chest, open it, mode Skip ignored, middle-click the Resin slot
  in the chest. Expected: red mark on the chest slot (and on Resin in your inventory if you carry some); spawned
  Resin is not auto-picked. Close and reopen the chest: mark still there. Also try a cart or ship storage if you have
  one (same panel).
- [ ] **T13 Mark placement (unverified corner):** ignore `CopperOre` (not teleportable) and `SpearFlint` (has a
  quality level), carry a stack of `ArrowWood`, put the spear on the hotbar (first row), equip it and hit a tree once
  so its durability bar shows. Expected: our mark, the vanilla no-portal icon, the quality number, the stack amount,
  the hotbar number, the equipped marker and the durability bar are all readable; none hides another. With Debug logging, note the corner in the `Loot filter badges
  on the player grid` line (and the chest one).
- [ ] **T14 Thrown spear (unverified which weapons fall back as items):** Only selected with Coins only, throw
  `SpearFlint` (secondary attack) at the ground, walk over it. Expected: it stays on the ground (it is not
  selected), E picks it up.
- [ ] **T15 Persistence:** set Skip ignored with 2 ignored and 1 selected item. Log out to the main menu and load the
  character again. Expected: same mode, same lists, marks back. Type `die` in the F5 console, respawn. Expected:
  same. Quit the game completely and start it again: same.
- [ ] **T16 Per character and defaults:** set `Defaults.IgnoredItems = stone,Wood` (small s on purpose), create a new
  character, open its inventory, switch to Skip ignored. Expected: `Stone` and `Wood` already ignored (tooltip and
  `lootfilter`); the first character still has its own lists. On a second new character, stay in Everything mode and
  change `IgnoredItems` to `Resin` in ConfigurationManager or the file: `lootfilter` shows `Resin` at once. Then
  change the mode once, change `IgnoredItems` again: this character keeps its lists. Set the defaults back to empty.
- [ ] **T17 Commands:** `/lootfilter` in chat (inventory closed, Enter) prints the mode, auto pickup on/off, both lists
  and the command list. `/lootfilter_ignore resin` adds `Resin` to Ignored (exact name shown) in any mode, with a
  note when Ignored is not the list in use; `/lootfilter_select Amber` adds `Amber` to Selected. Mode Skip ignored:
  `/lootfilter Wood` toggles Wood in Ignored. Mode Everything: `/lootfilter Wood` explains that there is no list to
  change. `/lootfilter_ignore Nonsense` → "Unknown item: Nonsense ...". In the F5 console, `lootfilter_ignore Rasp` +
  Tab completes `Raspberry` (Tab again cycles other matches, if any); `lootfilter Rasp` + Tab too; in the chat,
  `/lootfilter_ignore Rasp` + Tab completes the same way. `lootfilter mode only`
  switches the mode (the button follows); `lootfilter clear selected` empties Selected; `lootfilter defaults` copies
  the config defaults. Remove an item you do not carry with `/lootfilter_ignore <it>`: it leaves the tooltip list. No
  cheat prompt at any time.
- [ ] **T18 Gamepad:** with a controller, open the inventory (your grid focused), select a slot with the D-pad, click
  the right stick. Expected: the item is marked or unmarked (list of the current mode). Hold LT and click the right
  stick. Expected: the mode cycles, with its message. Hold RT and click the right stick. Expected: a panel next to the
  button (unverified position: fully on screen, not over the grid) lists both lists; again: it closes; close the
  inventory with it open, reopen: it is gone. In Everything mode, the right stick click shows "... hold LT and click
  the right stick.". Switch to the chest grid with a chest open (bumpers) and click the right stick: nothing of ours
  happens. The D-pad never selects the Auto pickup button. Set `GamepadControls = false`: right stick clicks do
  nothing. If you can, repeat on the Alternative 1 and 2 layouts (unverified buttons).
- [ ] **T19 Custom mark key:** set `MarkKey = L`. Hover an item and press L. Expected: marked (the tooltip now says
  "Hover an item ... and press L"). Open the F5 console and type a word with "l" while hovering a slot. Expected: no
  mark toggles. Set it back to `Mouse2`.
- [ ] **T20 Button offsets and rows:** set `ButtonOffsetX = 50`, `ButtonOffsetY = 20`. Expected: the button moves
  right and up at once, no restart. Set them back to `0`. Optional: `inventorysize 6` in the F5 console (cheat,
  single player or host only): the button stays on top of the taller panel and marks show on the new rows. Before
  going back with `inventorysize 4`, empty rows 5 and 6: a smaller size drops the items that no longer fit.
- [ ] **T21 Live toggle:** Esc → MC Mods → untick Loot Pickup Filter. Expected: button and marks disappear at once;
  ignored Stone is auto-picked again (normal game); no grey hover line; `lootfilter` in the F5 console answers
  `'lootfilter' is not a recognized command`. Tick it
  again. Expected: button and marks back, same mode and lists, Stone skipped again, no restart. Repeat with the
  inventory open (ConfigurationManager, or edit the file): the button appears and disappears without closing it.
- [ ] **T22 Disabled in the config file:** `Enabled = false` in `BepInEx/config/MC.UX.AutoPickup.Filter.cfg`,
  restart. Expected: normal auto pickup, no button, no marks, `lootfilter` answers `'lootfilter' is not a recognized
  command`. Set it back to `true`.
- [ ] **T23 Clean log:** after a session, no errors or exceptions mentioning `Loot Pickup Filter` or `MC.UX` in
  `BepInEx/LogOutput.log`, no repeated ZInput key warnings, no `Inventory layout not recognised` or `lists panel
  cannot be shown` warning; the `[MC:ready]` line is present.
- [ ] **T24 Hand harvesting is not filtered (user behaviour 3):** mode Only selected with only Coins selected, auto
  pickup on. Press E on a raspberry bush, a pickable flint and a mushroom; cook `RawMeat` on a cooking station over
  a fire and take it off with E once cooked; hang a `TrophyBoar` on an item stand and take it back with E. Expected:
  every one of these drops is picked up at once, as in the normal game. Optional (unverified names or timings): tap
  a finished fermenter; take honey from a beehive and sap from a sap collector with product; take an item back from
  an armor stand slot; press E on an archery target with arrows in it; take the output of a windmill or spinning
  wheel with its empty switch; tame a Lox (`tame`), saddle it, take the saddle off with Shift + E; swing a scythe
  over ripe crops. Expected: collected too.
- [ ] **T25 Other drops stay filtered:** same mode, more than 5 m away from any bush: `spawn Boar`, kill it; mine a
  rock. Expected: its drops and the stone are not picked up (E picks them up). Set `ExemptHarvest = false`, press E
  on another raspberry bush. Expected: the berries stay on the ground; set it back to `true`. V off, E on a bush:
  the berries stay (normal game).
- [ ] **T26 No marking through dialogs:** open the Skills dialog (and then the Trophies panel) so that it covers part
  of the inventory grid, middle-click on the dialog above a filled slot. Expected: nothing is marked, no message.
  Start dragging an item and middle-click another slot: nothing is marked. Same with the split dialog open
  (Shift + click a stack).
- [ ] **T27 Button after a recipe click:** open a workbench, click a recipe in the crafting panel, then click the Auto
  pickup button. Expected: the mode still cycles (the button stays clickable). Same after clicking a chest slot.
  With Debug logging, note which parent the `Loot filter button parent` line names.
- [ ] **T28 V off survives logout:** V off, log out to the character menu, load a character. Expected: the button
  reads `Auto pickup: Off (<that character's mode>)` right away and no mode message appears; press V: the game's
  "On", then that character's mode message (not in Everything mode).
- [ ] **T29 Message flood:** in Skip ignored, middle-click 6 different items within 2 seconds. Expected: all 6 marks
  appear at once; only one or two top-left messages, not a queue of 6 over the next seconds.
- [ ] **T30 Big scythe harvest (unverified: scythe radius, spawned crops):** mode Only selected with only Coins
  selected, auto pickup on. Scythe a ripe field of at least 40 crops (Barley or Carrots, not selected) in several
  swings, in lanes or in several directions, without walking over the drops left at the side. Then walk back over
  every drop. Expected: all of them are auto-picked; none shows "Auto pickup skips this".
- [ ] **T31 Filter change after a harvest:** same as T30 (or E on 3 raspberry bushes), but between the harvest and the
  walk over the drops, cycle the mode once around (back to Only selected) and run `/lootfilter_select Stone`.
  Expected: the harvested drops are still collected.
- [ ] **T32 A door does not let kills through:** Skip ignored with `Resin` and `GreydwarfEye` ignored. F5 →
  `debugmode`. Build a wood door, `spawn Greydwarf` next to it, press E on the door, then press K at once (kills
  nearby enemies in debug mode). Expected: the Resin and eyes stay on the ground (hover line "Auto pickup skips this
  (ignored)"). Type `debugmode` again.
- [ ] **T33 Feeding a kiln does not let its output through:** Only selected with `Coal` not selected. Build a
  charcoal kiln, press E on it a few times to feed Wood, and stay next to it. Expected: the coal that comes out stays on the
  ground.
- [ ] **T34 Unreadable MarkKey:** set `MarkKey = F13` in the cfg (and once `MarkKey = Mouse5`). Open the inventory.
  Expected: one clear warning in the log naming the key (at start, or when the setting changes in game); the button
  tooltip says marking with the mouse or keyboard is off; no repeated errors and no `Update_Postfix` or
  `HandleMarkKey` error. With a controller, the right stick gestures still work, and the button label follows
  `lootfilter mode skip`. Set it back to `Mouse2`: middle-click marks again.
- [ ] **T35 Controller lists panel wording:** with a controller only, open the lists panel with RT + right stick click.
  Expected: it explains LT + right stick click (mode), right stick click (mark) and RT + right stick click (close),
  and has no "Click to change the mode" or "Middle-click" line. Move the mouse while it is open: the text switches to
  the mouse wording; touch the controller again: it switches back.
- [ ] **T36 Other manual pickups (user behaviour 3):** Skip ignored with Stone ignored: `spawn Stone 5 p` (spawn and
  pick up). Expected: the Stone goes into your inventory. Optional: Only selected with Coins only, catch a fish
  (`FishingRod`, `FishingBait`, at the shore). Expected: the fish goes into your inventory as usual.
- [ ] **T37 Stone oven (user behaviour 3):** mode Only selected with only Coins selected, auto pickup on. Build a
  stone oven (`piece_oven`), give it Wood (its fuel) and `BreadDough` (unverified: recipe and baking time), wait until
  the bread is done and take it out with E. Expected: the bread is picked up at once, like cooked meat from a cooking
  station (T24).

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Vanilla server and friend:** join a server (or host) where a friend does NOT have the mod. Use all three
  modes. Expected: works for you as in single player; the friend sees nothing unusual; no errors on either side.
- [ ] **M02 Hand-off (items):** in Skip ignored (Stone ignored), stand on a pile of Stone; the friend without the mod
  walks onto the same pile. Expected: the friend auto-picks the Stone at once and normally (your game never claims
  it). Give the friend an item type you marked (drop it or use a chest). Expected: a normal item for them.
- [ ] **M03 Two players with the mod:** both have the mod with different filters (you ignore Stone, the friend is in
  Only selected with Stone selected). Walk over a mixed pile together. Expected: each picks up only what their own
  filter allows; the friend gets the Stone.
- [ ] **M04 Other server:** join another server (or your own world) with the same character. Expected: same mode and
  lists.
- [ ] **M05 Harvest on a bush another player's game runs:** the friend stands near a berry field first (their game
  runs it); you arrive, Only selected with Coins only, press E on a bush. Expected: the berries are picked up by you
  as in the normal game, even with some lag. The friend's own harvests are not affected by your filter.
- [ ] **M06 Hand-off (character):** after T15, set `Enabled = false` (or remove the mod) and load the same character
  on the server. Expected: it loads, normal auto pickup, no errors. Turn the mod back on: your mode and lists are
  back.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 With Crafting Search and Sort (MC):** open a workbench. Expected: its search row (crafting panel) and the
  Auto pickup button do not overlap. Click the Auto pickup button once, then click into the crafting search field,
  type `flint axe` (with the space) and press Enter. Expected: the text goes into the field and the list filters;
  the mode does not change again; no item is marked. Click into the field again and middle-click a filled inventory slot.
  Expected: nothing is marked while the cursor is in the field (if the click takes the cursor out of the field, the
  next middle-click marks as usual). With `MarkKey = L` (T19), click into the field, hover a slot with the mouse and
  type `l`: only the field gets the letter, no mark. If its Sort menu covers any of your slots when open, middle-click
  on the menu there: nothing is marked. Set `MarkKey` back to `Mouse2`.
- [ ] **C02 With Batch Station Feeding (MC):** Only selected with Coins only. Shift + E on a smelter or kiln adds 5
  (its own message), and plain E adds one, exactly as with that mod alone. Cook `RawMeat` on a cooking station, take
  the cooked meat with E and (a second time) with Shift + E. Expected: the meat is picked up both times (with Debug
  logging, a `Harvest grace: CookedMeat ...` line). E on a raspberry bush: the berries are collected.
- [ ] **C03 With One Click Repair All (MC):** at a workbench with several damaged items, click repair, then at once
  click the Auto pickup button. Expected: everything is repaired with one centre message, and the mode message shows
  top-left as usual (neither mod hides the other's message).
- [ ] **C04 Quick Stack Store Sort Trash Restock (optional):** Alt + click favouriting still works; middle-click marks;
  its buttons (right side of the panel) and ours (above the top-left corner) do not overlap.
- [ ] **C05 InventoryActions (optional):** its right-stick menu and our gamepad mark both react to the right stick;
  with `GamepadControls = false` only its menu opens.
- [ ] **C06 Extended inventory mod (optional):** with AzuExtendedPlayerInventory (it also changes the inventory grid),
  mark items in the main grid. Expected: marks on the right slots, none on its extra slots unless they are real grid
  slots, no exception in the log.
- [ ] **C07 Another auto pickup mod (optional):** with AutoPickupIgnorer (or another mod that patches
  `Player.AutoPickup`), load a character. Expected: at most one warning `Another mod changes Player.AutoPickup (...)`
  naming it; in Skip ignored, our ignored items are still skipped (and its own ignored items too).

Cross-mod tests with Sort Chest (MC, upcoming) go into that mod's `TESTING.md` (shared controller map:
`docs/design/ux-autopickup-filter.md`, section 3.4).
