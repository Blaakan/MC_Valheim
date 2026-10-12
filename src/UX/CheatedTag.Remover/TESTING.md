# Cheated Tag Remover — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel).
Smoke test (`./tools/Test-Smoke.ps1 -Mod CheatedTag.Remover`) passed 2026-10-12 on game 1.0.17, final code (loads, patches
cleanly, JitCheck clean: 490 methods, 0 failures). In-world self-tests (`./tools/Test-InWorld.ps1 -Mod CheatedTag.Remover -Only uncheat.`)
passed 2026-10-12 on the final code: `uncheat.blob` (31 checks), `uncheat.world` (35), `uncheat.scan` (9),
`uncheat.blocked` (6). Multiplayer self-tests with a real dedicated server
(`./tools/Test-Multiplayer.ps1 -Scenario modded,vanilla-server -Only uncheat.`) passed 2026-10-12 on the final code:
`uncheat.mp.sweep` (28 checks), `uncheat.mp.contested` (12), `uncheat.mp.local` (9), and the mod is Active on the
dedicated server. The in-world script itself ended with "failed" because of two start-up error lines of other MC mods
deployed in the game folder (Sneak Ambush, Spyglass); none comes from this mod. No hands-on in-game test yet. Not run
at all: a second real player, a server restart, a large world.

**Automated checks (2026-10-12):** an item ending in "Automated: ..." is checked in full by the named self-tests and
is ticked by them alone; a "Partly automated" item still needs its by-hand part.

**Setup:** use a **test character and a copy of the world** (the tests make cheated things on purpose, and
`cheated_clear` cleans the whole world it runs in). F5 for the console (add `-console` to the launch options) →
`devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). Ways to make tagged things:
- `spawn Wood 50`, `spawn Stone 50`, `spawn CopperOre 20`, `spawn RawMeat 10`, `spawn Club`: tagged items on the ground
  (pick them up: their tooltip has the grey line "This item was summoned through cheating means.").
- Build with tagged Wood or Stone in the inventory, or after `nocost`: the piece is tagged (nothing shows it; breaking
  it gives tagged materials back). A workbench built that way tags everything crafted at it.
- `spawn Boar`, or hit any creature while `god` is on: tagged creature (its drops are tagged).
- Put tagged ore in a smelter, tagged meat on a cooking station, tagged items in a chest, on an item stand or an armor
  stand, in a cart or a ship hold.
- `cheated_scan` counts, `cheated_clear` removes. Both also work in chat as `/cheated_scan` and `/cheated_clear`.
- Server tests: put your ID (shown in the server log when you join, or F2 in game) in the server's `adminlist.txt`.
- "A player without the mod" needs a second PC or a friend; one game cannot play both.

**Names checked:** `Wood`, `Stone`, `Club`, `RawMeat`, `CopperOre`, `Boar`, `piece_chest_wood`, `wood_floor`,
`piece_cookingstation`, `smelter` are used by passing self-tests on 1.0.17. The console commands `spawn`, `nocost`,
`god`, `devcommands`, `confirmcheats`, `save` are in the game code (`Terminal`). The tooltip sentence was read in the
installed game's localization table (token `$achievements_cheated_item_inventory`).

## 0.1.0 — single player

- [x] **T01 Count only (E1):** make a tagged piece and a chest with a tagged stack, type `cheated_scan`. Expected: one
  line "Cheated tags found (nothing was changed): ..." that lists at least 1 building piece and 1 item in 1 chest, then
  "Type cheated_clear to remove them."; the stack's tooltip still has the grey line.
  *Automated: `uncheat.scan`.*
- [ ] **T02 Clear the world (G1-G3, E2, E3):** with tagged things of every kind from the Setup (pieces, a creature, a
  chest, a ground item, items on stands, a smelter queue, cooking slots, items you carry), type `cheated_clear`.
  Expected: "Cheated tags removed: ..." with a count per kind, "Checked again: no cheated tag is left in the world.",
  "Saving the world."; no tooltip has the grey line any more (reopen the chest); stacks, quality, durability and
  crafter names are unchanged; typing it again says "No cheated tag found.".
  *Partly automated (`uncheat.world`); by hand: the tooltips as seen on screen, and a real smelter, fermenter, item
  stand, armor stand, cart and ship hold (the self-test writes their saved data directly).*
- [ ] **T03 The spreading stops (G2):** before `cheated_clear`: craft at a tagged workbench (the result is tagged),
  break a tagged piece (tagged materials), take a bar out of a smelter fed with tagged ore (tagged), kill a tagged
  Boar (tagged drops). After `cheated_clear`, with nothing tagged left in the inventory: the same four give clean
  items.
- [ ] **T04 It stays after a restart (E3):** build a portal and a chest from tagged wood, type `save` and wait for
  "World saved" (a portal built in the same session would be saved anyway), then `cheated_clear`; wait for the save to
  end, quit to the desktop, start again, load the world: `cheated_scan` says "No cheated tag found.".
- [ ] **T05 Chest open while the command runs (G3):** open a chest that holds a tagged stack, type `/cheated_clear` in
  the chat with the chest still open. Expected: the grey line is gone from the open chest at once; move a stack, close,
  reopen: still clean, nothing lost or doubled.
  *Partly automated (`uncheat.world`: chest marked open on its owner, then an item move); by hand: the real chest
  window.*
- [x] **T06 Unknown data is left alone:** nothing to do by hand.
  Expected: a saved item list in a format the mod does not know, item bytes under a key the game does not use for
  items, and a number key that only looks like a cooking slot tag are not changed; an object without a tag gets no
  new key and is not marked as changed; a container blob cut short or with bytes too many is not touched; the bytes a
  stand keeps after its item was taken off are cleaned but not counted as an item.
  *Automated: `uncheat.blob`, `uncheat.world`.*
- [x] **T07 Only the tag changes:** a tagged stack that carries another mod's data, a crafted weapon with quality and
  crafter name, long names and long data lists. Expected: after the clean-up every other value reads back the same.
  *Automated: `uncheat.blob`, `uncheat.world`.*
- [ ] **T08 Not a cheat command (G1):** on a character that never used dev commands, in a world without tagged things,
  type `/cheated_scan` and `/cheated_clear` in the chat (no `devcommands`). Expected: both answer; the stats page of
  the character (Esc → compendium) does not start with "Cheater!".
  *Partly automated (`uncheat.blocked`: neither command is a cheat command; `uncheat.world`: the character's cheat
  mark and counter are unchanged); by hand: typed in chat on a character that never cheated.*
- [ ] **T09 Live toggle:** in the MC Mods panel (or F1 with a configuration manager) untick the mod, type
  `cheated_scan`. Expected: "'cheated_scan' is not a recognized command". Tick it again: the command answers.
- [ ] **T10 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.UX.CheatedTag.Remover.cfg`,
  start the game, load the world. Expected: no such commands; tagged things stay tagged.
- [x] **T11 A copy that cannot run says so:** nothing to do by hand.
  Expected: when the mod is blocked, `cheated_clear` answers "Cheated Tag Remover is not running." with the status,
  and changes nothing.
  *Automated: `uncheat.blocked`.*
- [ ] **T12 Main menu and logging out:** at the main menu type `cheated_scan` in the console. Expected: "No world
  loaded.". Then, in a world, type `cheated_clear` and log out at once: no error, and no answer that claims a
  clean-up of a world that is closing.
- [ ] **T13 A real, large world:** on a copy of your own world type `cheated_scan`, then `cheated_clear`. Expected: the
  counts look right; the game freezes for a moment at most; `BepInEx/LogOutput.log` has the line "Removed cheated
  tags: ... N ms of scanning and writing, M objects in the world" (write N and M down here); a walk through the base
  shows no grey line and nothing missing.
- [ ] **T14 Clean log:** after the tests above, `BepInEx/LogOutput.log` has no error line from Cheated Tag Remover.

## 0.1.0 — multiplayer

- [ ] **M01 Dedicated server, whole world (G5):** server and your game with the mod, you are admin. Tag things at two
  bases far apart (walk away from the first so it unloads), type `cheated_clear` at the second. Expected: "Asked the
  server ...", then "The server is removing ...", then within about ten seconds the count for the whole world, "Checked
  again: no cheated tag is left in the world." and "Saving the world."; go back to the first base: clean. The server's
  log names you and the counts.
  *Partly automated (`uncheat.mp.sweep`: objects the server holds far from every player, and the player's own chest and
  portal); by hand: two real bases, visited afterwards.*
- [x] **M02 Not an admin:** same server, your ID not in `adminlist.txt`, a tagged stack in your inventory:
  `cheated_scan`, then `cheated_clear`. Expected: both answer "Only an admin can do this for the whole world: ..." and
  nothing changes in the world; `cheated_scan` counts your carried items ("On your game: ..."), `cheated_clear` removes
  the tag from them ("Cheated tags removed on your game: ...").
  *Automated: `uncheat.mp.sweep`.*
- [ ] **M03 Carried items (E2, hand-off):** two players online, one with the mod and one without, both carrying tagged
  stacks; the admin types `cheated_clear`. Expected: the player with the mod sees "The cheated tag was removed from the
  items you carry." and clean tooltips; the player without it keeps the grey line, sees cleaned items like any others
  (hand a cleaned stack over: normal, no error), and the admin's answer says "1 player online has this mod ..." and
  "1 player online does not have this mod ...".
  *Partly automated (`uncheat.mp.sweep`: one player with the mod, then the same player ignoring the request); by hand:
  a real second player without the mod.*
- [ ] **M04 Objects of a player without the mod (G5):** the player without the mod stands in a base they built with
  tagged wood (closed chests with tagged stacks, a tagged idle workbench); the admin types `cheated_clear` from
  elsewhere. Expected: everything is counted as removed; for the player without the mod, a chest opened afterwards
  shows clean stacks, nothing lost or doubled.
  *Partly automated (`uncheat.mp.sweep`: the server alone cleans an idle chest its only player owns, that game takes
  the server's version and its loaded chest reloads); by hand: a real second player.*
- [ ] **M05 Chest open on a player without the mod:** that player keeps a chest with tagged stacks open while the
  admin types `cheated_clear`. Expected: the answer says "Not removed: N items in 1 chest that a player has open right
  now, or that a player who lost the connection left open (at X Z). Open and close that chest once, then type
  cheated_clear again."; after the chest is closed, a second `cheated_clear` removes them; no stack lost or doubled.
  *Partly automated (`uncheat.mp.contested`: one game playing that player with the chest marked open); by hand: a real
  second player with the real chest window, and nothing lost or doubled.*
- [ ] **M06 Busy objects of a player without the mod:** that player fights a tagged Boar (without killing it), runs a
  smelter fed with tagged ore and cooks tagged meat while the admin types `cheated_clear`. Expected: either all of it
  is removed, or the answer lists what is "Still tagged" and says to type the command again; typed again once the
  fight is over and the stations are idle: nothing left.
  *Partly automated (`uncheat.mp.contested`: an object its owner tags again every frame is written three times and
  listed as "Still tagged"); by hand: a real fight, smelter and cooking station on a second player's game.*
- [ ] **M07 Server without the mod (G4):** join a vanilla server with the mod, carry tagged stacks, stand next to a
  chest with tagged stacks, a tagged piece and a portal built from tagged wood. `cheated_scan`, then `cheated_clear`.
  Expected: the count, then "Cheated tags removed: ..." for your items and the loaded objects, and "The server does not
  have Cheated Tag Remover (or has it turned off), so only the items you carry and the objects loaded around you were
  cleaned. ..."; log out, join again: still clean. Then restart that server and scan again: write down what came back
  (the portal may; the README says so).
  *Partly automated (`uncheat.mp.local`: counted, cleaned, said so, the change sent to the server); by hand: still
  clean after joining again, the restart, and a second player there sees the chest clean.*
- [ ] **M08 Player-hosted game:** you host from your game with the mod, a friend joins (with and without the mod).
  `cheated_clear` typed by the host. Expected: like M01-M04 with the host as server; the friend cannot run it for the
  world ("Only an admin ...") unless listed as admin. With the friend as admin typing it: the friend's answer does not
  count the host's carried items, and the host sees its own line "Cheated tags removed on your game: ...".
- [ ] **M09 Dedicated server restart (E3):** build a portal and a chest from tagged wood, restart the server (so both
  are in its save), join and stand next to them (your game then owns them), build a second tagged portal far away and
  leave it, `cheated_clear`, wait for "World saved" in the server log, restart the server, join: `cheated_scan` says "No
  cheated tag found.".
  *Partly automated (`uncheat.mp.sweep`: the server marks the objects the player's game cleaned, the portal file
  included, before it saves; `uncheat.world`: the marking alone); by hand: the restart.*
- [ ] **M10 Left with a chest open:** a player opens a chest with tagged stacks and loses the connection (close the
  game with Alt+F4) with it open. (a) Nobody else near that chest: the admin types `cheated_clear`: that chest is
  cleaned (not reported as open). (b) A second player without the mod stands next to the chest: the answer reports it
  as "open ... or left open"; after somebody opens and closes it once, the next `cheated_clear` cleans it.
- [x] **M11 Twice in a minute on a dedicated server:** type `cheated_clear` twice within a minute, each time with
  something tagged. Expected: both answer normally and end with "Saving the world."; no error in the server's log.
  *Automated: `uncheat.mp.sweep`.*
- [ ] **M12 Mod turned off on the server:** the server's `Enabled = false` while you are connected. Expected: your
  mod's status says the server has it turned off, and `cheated_clear` then cleans only your side and says so.
- [ ] **M13 Joining while it runs:** a player with the mod and tagged carried items joins (still on the loading
  screen) while the admin types `cheated_clear`. Expected: once that player has spawned, their carried items are clean
  and they see "The cheated tag was removed from the items you carry."; or, if they joined after the server asked,
  nothing happens and their own `cheated_clear` cleans them.
- [ ] **M14 Clean log:** after the multiplayer tests, no error line from Cheated Tag Remover in the server's and the
  players' `BepInEx/LogOutput.log`.

## 0.1.0 — cross-mod

- [ ] **C01 Sort Chest (MC):** a chest with a `spawn Wood 20` stack and a chopped Wood stack: Sort keeps them apart
  (Sort Chest T06). After `cheated_clear` (chest closed), open it and Sort: the two stacks merge, no "This container
  changed elsewhere" message.
- [ ] **C02 Forge Idol Upgrades (MC):** a Forge of Potential built with tagged materials gives a tagged result; after
  `cheated_clear` and with clean inputs the result is clean; idol star levels in the inventory and in chests are
  unchanged.
- [ ] **C03 Crossbow Stays Loaded and Dual Wielding (MC):** a loaded, spawned crossbow in the inventory and one in a
  chest, two spawned weapons paired in the hands: after `cheated_clear` the crossbows are still loaded and the pair is
  still remembered; the grey line is gone.
- [ ] **C04 Batch Station Feeding (MC):** batch-feed a smelter with spawned ore, `cheated_clear` while it is queued,
  collect the bars: no grey line; the batch count shown on the next feed is right.
- [ ] **C05 Items of other MC mods:** a chest with a Spyglass, a Smoke Screen stack, an instrument, a transplant stack
  and a tower shield, all spawned. `cheated_clear` typed on a game or server where those mods are turned off: every
  item is still there with its level and stack size once the mods are on again, and clean.
- [ ] **C06 Creature mods (MC: Creature Morale, Breeding Star Inheritance):** hit a pregnant tame and a fleeing
  creature in god mode so both are tagged, `cheated_clear`: the tame still gives birth with the recorded partner level,
  the fleeing creature still flees.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **X01 A mod that deletes tagged items** (NoCheatItems or ItemIntegrity ServerOnly, in dry-run mode): with it in
  dry run, `cheated_clear` works and the other mod then reports nothing to delete.
- [ ] **X02 Server Devcommands:** with its `disable_cheat_tracking` on, an admin spawns items (no tag), old tagged
  items are removed by `cheated_clear`; both mods' commands work side by side.
- [ ] **X03 A mod that moves items in closed chests** (quick stack to nearby chests, craft from chests) on the game of
  a player without Cheated Tag Remover: that player quick-stacks into tagged chests again and again while the admin
  types `cheated_clear`. Expected: nothing lost or doubled (the README warns that it can happen; write down if it
  does).
