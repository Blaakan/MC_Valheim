# Crafting Search and Sort — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches cleanly, JitCheck clean: 269 methods, 0 failures). No
in-game test yet.

**Setup:** use a **test character and a test world** (cheats mark the character). Press F5 for the console →
`devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). Type `nocost`, build a roofed shed
with the hammer and place the stations in it, then type `nocost` again to turn it off before testing lists (with
`nocost` on, every station lists every recipe; T24 tests that on purpose). If a station refuses to open, it needs a
roof or a fire: build a `fire_pit` under the cauldron (and under any other station that asks for a fire).
- Stations (prefab names checked in the 1.0.16 game data; they can also be placed with `spawn <name>`):
  `piece_workbench` (Workbench), `forge` (Forge), `blackforge` (Black Forge), `piece_magetable` (Galdr Table),
  `piece_artisanstation` (Artisan Table), `piece_cauldron` (Cauldron), `piece_MeadCauldron` (Mead Ketill),
  `piece_preptable` (Food Preparation Table), `piece_stonecutter` (Stonecutter).
- To see every recipe of a station: `setkey AllRecipesUnlocked` (a world key; the game refuses it with "Did not apply
  key" on a character that has not used a cheat yet: run it after `nocost`). `removekey AllRecipesUnlocked` undoes it.
- Items (`spawn <Name>`, Tab autocompletes; names checked in the 1.0.16 game data): `SwordSilver` (Silver Sword),
  `MaceSilver` (Frostner), `KnifeSilver` (Silver Knife), `ShieldSilver` (Silver Shield), `AtgeirBronze` (Bronze
  Atgeir), `ArmorWolfChest` (Wolf Hide Chestpiece), `PickaxeAntler` (Antler Pickaxe), `Torch`, `Hammer`, `ArrowWood`
  (Wood Arrow), `FishingBait` (Fishing Bait), `Silver`, `Wood`. Idols for the Forge of Potential (T32):
  `Upgrader0Weapon` ... `Upgrader7Weapon` (Battle idols) and `Upgrader0Armor` ... `Upgrader7Armor` (Protection idols),
  tiers Wooden, Bronze, Iron, Silver, Black Metal, Black Marble, Flametal, Bloodgold.
- **(unverified)**: which station crafts `ArmorWolfChest` (T20); that `spawn UpgradeStation` places a working Forge
  of Potential (the prefab exists; otherwise use a world where you found one, a Mountains location); which idol tier
  each item needs (the Forge of Potential shows it); the internal station names in the Debug lines (`$piece_forge`,
  `$piece_workbench`...: these localization keys exist, their use by the stations is assumed); the category of
  tools, torches, tankards, mead bases, fishing bait and food (T21, T22); which station crafts the Tankard and the
  biome fishing baits (`FishingBaitForest`... recipes exist); that the Hammer and the Torch are crafted by hand.

Keep `./tools/Watch-Log.ps1 -Mine` open. With Debug logging (`./tools/Setup.ps1 -DevBepInExConfig`):
- the first crafting list after each character load logs `Crafting panel layout (before search row) ...`, then
  `(after search row)` and `Crafting search row built: ...` (where the list and the new row sit, used by T01 and T27);
- the first list at each station and tab (again after each character load) logs `Crafting list at station '<name>'
  (craft tab), N rows:` then one line per recipe: `<prefab> <item type> <skill> <animation> food=<hp>/<stamina>/<eitr> -> <kind> -> <category>` (T21, T22).

The vanilla log line `Setting selected recipe <n>` appears on every list update: it is normal.

## 0.1.0 — single player

- [ ] **T01 Controls at every station:** right after launching the game, open the **stonecutter first** (a short
  list) and try to scroll the list: it must not scroll into blank space. Then open each of the other stations above,
  and the plain inventory (Tab). Expected: a search field and a `Sort: Default` button in one row directly above the
  recipe list; the station name and icon, the Craft/Upgrade tabs, the repair button and the first recipe row are fully
  visible (nothing covered); no recipe row is drawn in or behind the new row, even while scrolling; scrolling reaches
  the last recipe with no empty gap below it.
- [ ] **T02 Search by name:** at the forge (`AllRecipesUnlocked` on) type `silver`. Expected: while you type, the list
  shrinks to the rows whose name contains "silver" or whose requirement list shows Silver (Silver Sword, Frostner...).
  Click every remaining row: each has silver in its name, shows Silver in its requirements, or has silver in its
  internal name (Frostner = `MaceSilver`); no other row is left.
- [ ] **T03 Search by ingredient:** at the workbench type `wood`. Expected: the list keeps every recipe whose
  requirement list shows an ingredient with "wood" in its name (Wood, Fine Wood, Core Wood...; check the requirements
  of 3 rows) plus the items with "wood" in their name (Wood Arrow...). A recipe that needs such wood only to upgrade
  (if any) is not kept in the Craft tab.
- [ ] **T04 Case and spaces:** at the forge type `SiLver SWord`. Expected: the Silver Sword is listed (same as
  `silversword`).
- [ ] **T05 No match and back:** type `zzzz`. Expected: empty list, empty details, craft button off, no error. Delete
  the text. Expected: the full list returns, first row selected.
- [ ] **T06 Selection:** at the forge, clear the field, select the Silver Sword, type `silver`. Expected: the Silver
  Sword stays selected and the list is at the top. Change the text to `bronze` (the Silver Sword does not match it):
  the first remaining row is selected.
- [ ] **T07 Typing is safe:** click the field and type `wasd e tab q r v x c 1 2 3 4 5 6 7 8 space`, plus capitals
  with Shift. Expected: only the text changes: the player does not move or jump, the inventory stays open, no hotbar
  item is used, auto-pickup does not toggle. With a chest open (hand-crafting list shown), typing `e` repeatedly never
  closes the chest or stacks items into it.
- [ ] **T08 Console binds:** in the console `bind j say bindtest`, close it, then type `j` in the field. Expected:
  nothing is said. Run `unbind j` afterwards.
- [ ] **T09 Esc and Enter:** type `bronze`, press Esc. Expected: the cursor leaves the field, `bronze` stays, the list
  stays filtered, the inventory stays open; a second Esc closes the inventory. Reopen, type, press Enter: same (text
  kept, cursor gone). After each, keyboard navigation in the pause menu (Esc) still works.
- [ ] **T10 Focus key:** with the inventory open and nothing focused, press F. Expected: the cursor is in the field
  and no "f" was typed. With the console (F5) open and focused, F types an f there and does not move the cursor to our
  field (the chat cannot be opened while the inventory is open, so only the console applies). If BepInEx
  ConfigurationManager is installed: F1, click into one of its text fields, type `f`: it goes there, our field stays
  unfocused. Settings → Controls: rebind Use to F, open a station, press F. Expected: the inventory closes (vanilla);
  reopen it: no cursor in our field, and Tab/Esc close the inventory normally. Rebind Use back to E. Rebind Inventory
  to F, press F to open the inventory. Expected: no cursor in the search field, and F closes the inventory again.
  Rebind Inventory back to Tab. Set `FocusSearchKey` empty: F does nothing.
- [ ] **T11 Right-click clears:** type `silver`, right-click the field. Expected: text cleared, full list back.
- [ ] **T12 Sort menu:** at the forge click `Sort`. Expected: a menu in the recipe-row style with Default, Name (A-Z)
  and only the categories present at the forge, each with a count and the icon of its first recipe (e.g. `Swords
  (4)`); the active one is highlighted. Click outside it: it closes (and the click acts normally). Open it, press Esc:
  it closes, the inventory stays open. Open it, hold W: the player walks (as with the inventory open in vanilla).
  Open it, press E (or Tab): the inventory closes, and the menu is gone when you reopen it. Open it, switch to the
  Upgrade tab: it closes.
- [ ] **T13 Category first, rest untouched:** note the order of the first 10 rows on Default. Choose Weapons.
  Expected: every weapon row moves to the top in the same relative order; the non-weapon rows follow in their previous
  relative order; the button reads `Sort: Weapons`; the list is scrolled to the top.
- [ ] **T14 Weapon type:** choose the first weapon type listed (e.g. Swords). Expected: swords first, then all other
  rows (other weapons included) in their previous order.
- [ ] **T15 Name:** choose Name (A-Z). Expected: the whole list is alphabetical by displayed name, greyed rows mixed
  in with the craftable ones.
- [ ] **T16 Search follows sort (user example):** at the forge, sort Weapons, type `silver`. Expected: the silver weapons first
  (Silver Sword, Frostner...), then the other recipes that match `silver`, nothing else. Change the sort to
  Default: the same rows, in vanilla order.
- [ ] **T17 Reset:** right-click the Sort button. Expected: `Sort: Default`, vanilla order. With the menu open and the
  sort already on Default, right-click the button: the menu closes, nothing else changes.
- [ ] **T18 Remembered per station:** forge = Weapons, workbench = Name, plain inventory (hand crafting) = any other
  option. Close and reopen each. Expected: each opens with its own sort. Log out and load the character again.
  Expected: still remembered. Set `RememberSort = false`: every station opens on Default, and a sort you pick lasts
  until you close the inventory. Set it back to `true`: the saved sorts are back. Then, with the inventory closed right
  after using the forge, set `RememberSort = false` and reopen the forge. Expected: Default. Set it back to `true` and
  reopen the forge. Expected: Weapons (its saved sort) again, without visiting another station first. Last, with the
  forge open on Weapons, set `RememberSort = false` (ConfigurationManager, or the config file): the list goes back to
  vanilla order at once.
- [ ] **T19 Search cleared on close:** type `silver`, close the inventory, reopen the same station. Expected: empty
  field, full list. Set `KeepSearchText = true`: reopening the same station keeps `silver` and the filter; opening
  another station (or the plain inventory) starts empty.
- [ ] **T20 Upgrade tab:** carry `AtgeirBronze`, `SwordSilver` and `ArmorWolfChest` (quality 1; the wolf armor's
  station is unverified: if it is not in the forge's Upgrade tab, go on without it). Open the forge, switch to
  Upgrade. Expected: searching `silver` keeps the Silver Sword and any other row whose next-level requirement list
  shows Silver (click it to check); the menu lists only the categories of the upgrade rows; the chosen sort applies.
- [ ] **T21 Food stations:** at the cauldron, mead ketill and food preparation table open the menu. Expected: sensible
  categories (Food, Meads and potions, Materials); choosing one moves those rows first. Note where the mead bases land
  (Materials or Meads and potions: their item type is unverified) and, at whichever station lists the biome fishing
  baits (station unverified), that they are under Ammo (unverified). Note any item that lands in a surprising category
  (unverified classification: with Debug logging, copy its row line from the log).
- [ ] **T22 Tools and torches:** at the workbench choose Tools and light, then do the same in the plain inventory
  (the Hammer and the Torch should be crafted by hand; unverified). Expected: the tools (Hammer, Antler Pickaxe,
  Hoe...) and the Torch come first wherever they are listed; they are not listed under a weapon type. With Debug
  logging, the row lines show `PickaxeAntler ... -> SkillTool -> tools` and `Torch ... -> Torch -> tools`. If the
  Tankard is listed at the workbench (unverified), it is under Other, not under Weapons or a weapon type.
- [ ] **T23 Crafting with a filter:** search `arrow` at the workbench, craft Wood Arrows (and x5 with Shift+click).
  Expected: crafting works; after it the list is still filtered and sorted. Type during a craft: the craft finishes
  with the item you started, then the list updates.
- [ ] **T24 nocost:** `nocost` on, open the workbench. Expected: search and sort work on the full list. `nocost` off.
- [ ] **T25 Console interplay:** at the forge (`AllRecipesUnlocked` on), `filtercraft bronze`, then type `sword` in the
  field. Expected: only the Bronze Sword. `filtercraft` (no argument) clears it. `sortcraft Weight` with our sort on Default: vanilla weight order;
  `sortcraft` (no argument) resets it.
- [ ] **T26 Language:** pick a weapon-type sort first (e.g. Swords). Settings → change the game language, reopen the
  forge. Expected: searching uses the new names (type part of a translated name); the weapon-type labels in the menu
  and on the Sort button are in the new language, the other labels stay English. Known gap: the hint text in the empty
  field may stay in the old language until you load a character again (note it, not a failure).
- [ ] **T27 Plain inventory click:** open the plain inventory with Tab (player grid active) and click straight into
  the search field. Expected: it takes the cursor at once. Same with a click on the Sort button: the menu opens at
  once.
- [ ] **T28 Gamepad:** with a controller, open the forge. Expected: D-pad list navigation and crafting work as
  vanilla; the remembered sort applies; no controller button opens or clicks our controls. From the craft button, the
  Craft/Upgrade tabs, the repair button and the first and last recipe rows, push the D-pad and the left stick in every
  direction: the highlight never lands on our field, the Sort button or a menu row. If your setup has a gamepad
  cursor: click the field with it, then press B: the cursor leaves the field and the inventory stays open; a second B
  closes the inventory. Open the Sort menu with the cursor, press B: the menu closes, the inventory stays open.
- [ ] **T29 Live toggle while in use (config file):** at the forge type `silver` and leave the cursor in the field.
  Set `Enabled = false` in `BepInEx/config/MC.UX.Crafting.SearchSort.cfg` without closing the inventory (text editor
  + alt-tab; if alt-tab removes the cursor, note it) or with ConfigurationManager (F1) if installed (its click may
  remove the cursor first: note it). Expected within a second: the row is gone, the list is full height, unfiltered
  and in vanilla order; E and Esc close the inventory again; after closing, keyboard navigation in the pause menu
  works. Set `Enabled = true` with the station open: the row is back with the remembered sort, no restart. Repeat
  with the Sort menu open instead of the cursor: the menu disappears with the row.
- [ ] **T30 Disabled in the config file:** `Enabled = false` in `BepInEx/config/MC.UX.Crafting.SearchSort.cfg`,
  restart. Expected: vanilla crafting panel (no row, full-height list, vanilla order). Set it back to `true`.
- [ ] **T31 Clean log:** after a session, no errors or exceptions mentioning `Crafting Search and Sort` or
  `MC.UX.Crafting` in `BepInEx/LogOutput.log`, and no `Crafting list layout not recognised` warning.
- [ ] **T32 Forge of Potential:** carry a weapon and an armor piece that can be refined, plus the matching idols, and
  open a Forge of Potential (setup above; if you cannot reach one, mark `[-]` with the reason). Expected: the row is
  shown above the upgrade-only list (no Craft tab); searching part of an idol's name as the requirement list shows it
  (for example `idol`) keeps the rows whose requirement list shows that idol; searching an item name keeps that item; the Sort menu and a
  category sort (Weapons, Armor) work on this list.
- [ ] **T33 Live toggle from the MC Mods panel:** close the inventory, Esc → MC Mods → untick Crafting Search and
  Sort, back to a station. Expected: vanilla panel (no row, full-height list, vanilla order). Tick it again, reopen:
  the row is back with the remembered sort, no restart.

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Vanilla server and friend:** join a server (or host) where a friend does NOT have the mod; both use the
  same workbench. Expected: search and sort work for you as in single player; the friend's panel is vanilla; no
  errors on either side.
- [ ] **M02 Hand-off (items):** craft a few items after searching and sorting, drop them (or put them in a chest) for
  the friend without the mod. Expected: normal items for them, no errors.
- [ ] **M03 Hand-off (character):** after T18 (sorts remembered), set `Enabled = false` (or remove the mod), load the
  same character. Expected: the character loads, vanilla list, no errors. Turn the mod back on: the remembered sorts
  are back.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 With One Click Repair All (MC):** with `silver` typed and the sort on Weapons at the forge, click repair
  with several damaged forge items. Expected: all repaired in one click; the list stays filtered and sorted; the
  Upgrade tab's durability bars update.
- [ ] **C02 With CraftingFilter (optional):** filter by its category and search in ours. Expected: both apply (only
  rows matching both are listed); note whether its hover list covers our row.
- [ ] **C03 With CraftSearch or AAA Crafting (optional):** documented as not compatible (two search fields over the
  list). Note what happens; no errors expected from this mod.

Cross-mod tests with Sort Chest and Loot Pickup Filter (MC) are in those mods' own `TESTING.md`.
