# Sort Chest — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches cleanly, JitCheck clean: 278 methods, 0 failures). No
in-game test yet.

**Setup:** use a **test character and a test world** (the console `spawn` marks items as cheated for achievements).
Press F5 for the console → `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`), then
`nocost` to build for free.
- Containers (prefab names checked in the 1.0.16 game data): build with the hammer `piece_chest_wood` (Chest),
  `piece_chest` (Reinforced chest), `piece_chest_blackmetal` (Black metal chest), `piece_chest_private` (Personal
  chest: build it yourself, only its builder can open it), `piece_chest_barrel` (Barrel), `incinerator` (Obliterator).
  Vehicles: `spawn Cart`, and, standing at the shore, `spawn Karve`, `spawn VikingShip` (Longship),
  `spawn VikingShip_Ashlands` (Drakkar).
- Items (`spawn <Name> <amount>`, Tab autocompletes; names checked in the 1.0.16 game data): `Wood`, `Stone`,
  `Resin`, `Flint`, `AxeFlint`, `Club`, `Raspberry`, `TrophyBoar`, `CopperOre`, `Guck`, `SerpentScale`, `SilverOre`,
  `Tar`, `BlackMarble`, `FlametalOreNew`, `Frostwood`, `SwordBronze`, `ArmorIronChest`, `MeadHealthMinor`,
  `PickaxeAntler`, `Torch`, `Tankard`, `CrossbowArbalest`, `BoltBone`. `spawn` drops the amount as single items in
  front of you, and auto pickup only takes them while you stay under your carry weight; add `p` at the end
  (`spawn Wood 65 p`) to put them straight into your inventory. To make partial stacks, split a stack with
  Shift+click.
- The English item names below are the game's 1.0.16 English texts (the prefab name is given in brackets where it
  differs, e.g. Timberwood for `Frostwood`).
- **(unverified)**: the container sizes (wiki: Chest 5x2, Reinforced 6x4, Black metal 8x4, Personal 3x2, Barrel 6x2,
  Cart 6x3, Karve 2x2, Longship 6x3, Drakkar 8x4; the Debug line `Opened <name> <w>x<h>` shows the real size: note
  it); which English name a prefab shows where the names differ (Timberwood for `Frostwood`, Flametal Ore for
  `FlametalOreNew`, Iron Scale Mail for `ArmorIronChest`); Wood's stack size of 50 (T05); the weapon skill of
  `AxeFlint` (Axes, so it sorts before `Club`); the item type, skill and animation of the tools, the torch, the tankard
  and the mead (T14 prints them); the recipes and station costs behind the derived biomes (T14); which controller keys
  the game's Take all / Place stacks use and where the Sort buttons end up (T12, T13: the Debug layout dump shows
  both); that a ship's storage shares the ship's network object (M05, M06).

Keep `./tools/Watch-Log.ps1 -Mine` open. For T09, T12, T13 and T14 turn on Debug logging
(`./tools/Setup.ps1 -DevBepInExConfig`):
- every opening logs `Opened <name> <w>x<h>`;
- the first opening of each container size logs `Container panel layout (...)` (where the panel parts and the Sort
  buttons sit, and the controller keys of every button);
- every sort logs `Sorted <name> by <order>: <n> items, <m> moved, <k> stacks merged`;
- the first "By biome" sort after loading a world logs `Biome index built in <ms> ms: ...` (Info) and one Debug line per
  item: `Biome index: <prefab> -> <biome> (<source>) | type <item type>/<skill>/<animation> -> <group rank>.<rank in
  group> <group>`.

## 0.1.0 — single player

- [ ] **T01 Sort by name:** sort order "By name". In a Chest put, with gaps between them, `Wood`, `Stone`, `Resin`,
  `Flint`, `AxeFlint`, `Club`, `Raspberry`, `TrophyBoar`. Click Sort. Expected (English), in reading order (the top
  row filled left to right, then the next row, whatever the chest width): Boar Trophy, Club, Flint, Flint Axe,
  Raspberries, Resin, Stone, Wood, then only empty slots at the bottom right. One move sound. Close and reopen: same.
- [ ] **T02 Sort by type:** same items, "By type", Sort. Expected: weapons first (Flint Axe then Club: axes before
  clubs), then Raspberries (food), then the materials alphabetically (Flint, Resin, Stone, Wood), then Boar Trophy.
- [ ] **T03 Sort by biome:** in a Black metal chest put `Frostwood`, `Tar`, `Wood`, `SilverOre`, `FlametalOreNew`,
  `SerpentScale`, `Guck`, `BlackMarble`, `CopperOre`, `SwordBronze`, `ArmorIronChest`, `Raspberry`; "By biome", Sort.
  Expected order: Raspberries, Wood (Meadows: food before materials); Bronze Sword, Copper Ore (Black Forest: weapon
  before material); Iron Scale Mail [`ArmorIronChest`], Guck (Swamp); Serpent Scale (Ocean); Silver Ore (Mountain);
  Tar (Plains); Black Marble (Mistlands); Flametal Ore [`FlametalOreNew`] (Ashlands); Timberwood [`Frostwood`] (Deep
  North).
- [ ] **T04 Sort order button:** click it three times. Expected: label "By name" → "By type" → "By biome" → "By name"
  (cycle from wherever it started); the chest does **not** move; the Sort tooltip follows ("..., by biome."); `SortBy`
  in `BepInEx/config/MC.UX.Container.Sort.cfg` follows. With the chest open, change `SortBy` in the file (or
  ConfigurationManager): the label follows within a second. After a game restart the label shows the last choice.
- [ ] **T05 Merge stacks:** Wood stacks of 20, 30 and 15 (split a spawned stack) spread in the chest. Sort. Expected:
  one stack of 50 and one of 15, adjacent; the container weight number unchanged. Set `MergeStacks = false`, split
  again, Sort. Expected: stacks stay separate, larger first, adjacent.
- [ ] **T06 No lossy merge:** set up in this order (the game's pickup, Ctrl+click and Place stacks would merge the two
  stacks and mark the clean one cheated before the test starts): chop a tree and pick up less than 50 Wood; drag that
  Wood into an empty chest; make sure you carry no Wood; `spawn Wood 20` and pick it up; **drag** it onto an **empty**
  slot of the chest (no Ctrl+click, no Place stacks). Hover both stacks: the spawned one shows the grey "cheated" line,
  the chopped one does not. Sort. Expected: not merged; each tooltip unchanged.
- [ ] **T07 Already sorted:** click Sort twice. Expected: the second click changes nothing and makes no sound (Debug:
  `(already sorted, nothing saved)`).
- [ ] **T08 Drag in progress:** pick up (click) an item in the chest, then click Sort. Expected: the drag is cancelled,
  the chest is sorted, nothing is lost or duplicated. Repeat with an item picked up from your own inventory: expected
  it stays in your inventory.
- [ ] **T09 All container kinds:** in a Reinforced chest, Personal chest, Barrel, Cart, Karve, Longship: Sort and sort
  order buttons visible, not overlapping anything, sorting fills rows left to right from the top-left slot. Note the
  sizes from the `Opened` Debug lines.
- [ ] **T10 Tombstone:** die with at least 5 different items in the inventory. After respawning, make sure the
  tombstone no longer fits in your inventory (otherwise the game loots it at once without opening a panel; it fits
  when you have enough free slots and its weight keeps you under your carry weight): `spawn Stone 400 p` puts the
  stones straight into your inventory; check that the weight shown under your inventory is now above its maximum.
  Open the tombstone. Expected: the tombstone panel opens and shows **no** Sort
  buttons (Take all and Place stacks as usual). Close it, open a chest: the Sort buttons are back.
- [ ] **T11 Obliterator:** put items in, Sort works; obliterating afterwards works as usual.
- [ ] **T12 Gamepad:** with a controller, open a chest, move the focus to the chest grid (bumpers). Expected: glyphs
  on both buttons (View/Select on Sort, left stick on the sort order button), shown only while the chest grid is
  focused; View/Select sorts; a left stick click changes the sort order only (nothing moves); with your own inventory
  or the crafting panel focused, both do nothing to the chest; moving around the chest grid with the left stick never
  sorts; the right stick click does nothing to the chest; the game's Take all / Place stacks controller buttons still
  work. No `already uses the controller key` warning in the log. (While the left stick is held down, the crafting
  panel may show its x5 multi-craft preview: that is the game's own use of that button, nothing is crafted.)
- [ ] **T13 Layout and tooltips:** hover each button: tooltip "Sort" / "Rearrange this container from top left to
  bottom right, by ..." and "Sort order" / "Click to change how Sort orders items: ...". With Debug logging on, open a
  Chest, the Karve, the Black metal chest and the Drakkar: in each, no overlap with Take all, Place stacks, the name,
  the weight, the grid or its scrollbar, nothing off screen, and no `Sort buttons ... on a` warning in the log (an Info
  line `they go in a second row` is fine if the buttons are then in a clean second row: note it). Then
  `inventorysize 6` (2 extra rows) and open the Chest and the Black metal chest again: same checks. Put nothing in the
  extra rows and set `inventorysize 4` afterwards (lowering the size drops the items of the removed rows).
- [ ] **T14 Biome and type index log (Debug level):** the first "By biome" sort after loading a world writes `Biome
  index built in … ms` (Info) and one Debug line per item. Expected: `Wood -> Meadows (Table)`, `Bronze -> BlackForest
  (Derived)`, `BoarJerky -> BlackForest (Derived)` (made at the cauldron, which needs Tin), `MeadHealthMinor ->
  BlackForest (Derived)` with type `-> 6.1 Food and potions`, `Coal -> BlackForest (Table)`, `MushroomBlue -> <biome>
  (Scan)` or `Unknown`; type part: `Hammer`, `Hoe`, `Cultivator` `-> 5.0 Tools and light`, `PickaxeAntler`,
  `FishingRod`, `Scythe` `-> 5.1 Tools and light`, `Torch` `-> 5.2 Tools and light`, `Tankard` `-> 10.0 Other`,
  `SwordBronze` `-> 0.0 Weapons`; no exception; the time well under a second. A second "By biome" sort in the same world
  does not rebuild. (The recipes and prefab values behind these lines are unverified: if a line differs, note it.)
- [ ] **T15 Live toggle:** with a chest open, set `Enabled = false` for Sort Chest without closing the chest: in
  ConfigurationManager (F1) if installed, or by editing `BepInEx/config/MC.UX.Container.Sort.cfg` (alt-tab; the game
  picks the change up while running). Not through Esc → MC Mods: Esc closes the inventory first (T18). Expected: both
  buttons disappear at once, chest untouched. Set it back to `true` with the chest still open. Expected: the buttons
  appear without reopening the chest, and sorting works. No restart.
- [ ] **T16 Disabled in the config file:** `Enabled = false` in `BepInEx/config/MC.UX.Container.Sort.cfg`, restart.
  Expected: vanilla panel, no buttons. Set it back to `true`.
- [ ] **T17 Clean log:** after a session, no errors or exceptions mentioning `Sort Chest` or `MC.UX` in
  `BepInEx/LogOutput.log`, and no `Sort buttons` warning.
- [ ] **T18 Live toggle from the MC Mods panel:** close the inventory, Esc → MC Mods → untick Sort Chest, open a
  chest. Expected: vanilla panel, no buttons. Tick it again, open the chest: the buttons are back, sorting works, no
  restart.

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Vanilla friend sees the result:** on a server (or hosting) where a friend does NOT have the mod, sort a
  shared chest and close it. The friend opens it. Expected: sorted layout, no errors on either side.
- [ ] **M02 Hand-off:** sort with merging, then the friend without the mod takes the merged stacks and uses or sells
  them. Expected: normal items, same tooltips, nothing lost.
- [ ] **M03 In use:** while you have the chest open (and sort it), the friend tries to open it (E). Expected: the
  game's "in use" message for the friend; after you close it, the friend opens it and sees the sorted content.
- [ ] **M04 Dedicated server:** on a dedicated server without the mod, sort, log out, log back in. Expected: the chest
  is still sorted.
- [ ] **M05 Ship storage hand-off:** the friend stands aboard a Karve or Longship; you stay on the dock and open the
  ship's storage. Expected: within about 2 seconds the container panel closes itself (the game gives the ship to the
  player aboard) and the Sort buttons go with it; a Sort clicked before that sorts the storage once; no error on either
  side; the friend can then open the storage and sees consistent content, nothing lost or duplicated.
- [ ] **M06 Stale ship storage:** the friend is aboard a Karve. You open the ship storage from the dock, wait until the
  panel closes itself, then close your inventory. The friend opens the storage, takes some items and closes it. You
  open the storage again from the dock and press Sort at once. Expected: either the center message "This container
  changed elsewhere. Close it and open it again to sort." with nothing changed, or (if your copy was already current)
  a normal sort. The items the friend took never reappear in the storage and nothing the friend added disappears.
  After you close and reopen the storage, you see the current content and Sort works.
- [ ] **M07 Controller on a chest another player owned:** with a controller, open a chest the friend used last (so the
  friend's game owned it). Expected: from this first opening, the glyphs on the Sort buttons show only while the chest
  grid is focused, and View/Select / left stick click act only there.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 Crossbow Stays Loaded (MC):** put a loaded crossbow (`CrossbowArbalest`, loaded with `BoltBone`, then put
  away) and an unloaded one in a chest, sort. Expected: two separate items; take the loaded one and equip it: still
  loaded; the other one is not.
- [ ] **C02 Loot Pickup Filter (MC):** both mods on, open a chest. Expected: both UIs visible, no overlap, both work.
  - Set its mode to Skip ignored and middle-click three different chest items to mark them (badges shown; its marks
    are per item type, so every stack of a marked item shows one). Sort. Expected: the badges move with the marked
    items to their new slots; no badge on a slot that is now empty or holds an unmarked item.
  - After sorting, middle-click a chest slot: that item is marked / unmarked as before.
  - Controller: with the chest grid focused, the right stick click (alone, with LT, with RT) does nothing (no mark,
    no mode change, no sort); View/Select sorts; the left stick click only changes the sort order. With your own
    inventory focused, the right stick click marks the selected item, LT + right stick changes its mode, RT + right
    stick opens its lists panel, and View/Select / the left stick click do nothing to the chest. No press ever does
    both mods' actions.
- [ ] **C03 Crafting Search and Sort (MC):** both mods on. Open a chest: its search row and Sort button show above the
  crafting list as usual, our buttons on the chest panel.
  - No overlap between its row (and its open Sort menu) and our buttons. Open its Sort menu, then click our Sort:
    its menu closes and the chest is sorted. Open its menu again, click our sort order button: its menu closes and our
    label changes. Then use its menu normally: it opens, sorts the recipe list and closes as usual.
  - Click into its search field and type text with spaces, `sort`, then Enter: the chest does not sort, our label
    does not change. With a controller connected, while the cursor is in its field, press View/Select and click the
    left stick: nothing happens to the chest.
  - Put `PickaxeAntler`, `Torch`, `Tankard`, `MeadHealthMinor`, a `Raspberry` and some `Wood` in the chest, sort by
    type. Expected: the pickaxe and the torch next to each other (Tools and light), then Raspberries (food), then the
    mead (Meads and potions come right after food), then Wood, and the tankard last (Other). In its menu (with
    `nocost` on, as in Setup, every recipe is listed, even in your plain inventory), "Tools and light" brings the
    Antler Pickaxe and the Torch to the top and "Other" brings the Tankard; the mead's T14 line says
    `6.1 Food and potions` (its kind Potion is that mod's "Meads and potions").
- [ ] **C04 Other chest sort mods (optional):** with Quick Stack Store Sort Trash Restock (or InventoryActions,
  HexQuickStackStorage), open a chest. Note whether its buttons and ours overlap; both sorts work; no errors from this
  mod.

One Click Repair All, Batch Station Feeding, Harpoon Hooks Tames and Creature Kill and Tame Counts (MC) touch neither
containers nor the container panel: no cross-mod test.
