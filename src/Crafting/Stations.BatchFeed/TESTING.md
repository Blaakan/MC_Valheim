# Batch Station Feeding — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches cleanly, JitCheck clean: 236 methods, 0 failures). No
in-game test yet.

**Automated checks (2026-10-08):** 33 of 34 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** press F5 for the console → `devcommands`, then `nocost` (every piece shows in the hammer menu and builds
for free, no workbench needed); `god` is optional. `spawn Hammer 1 p`. Spawn items with `spawn <Name> <amount> p`
(the `p` puts them in your inventory; Tab completes names).

Names checked in the 1.0.16 game data (prefab list and English localization):
- items: `CopperOre` (Copper Ore), `TinOre` (Tin Ore), `Coal`, `Wood`, `Resin`, `BlackMetalScrap`, `Sap`,
  `Softtissue` (Soft Tissue), `Barley`, `Flax`, `Ice`, `FrozenFuel` (Liquid Frost), `BoneFragments` (Bone
  Fragments), `RawMeat` (its name in game is **Boar Meat**), `BreadDough` (Bread Dough), `TurretBoltWood` (Wooden
  Missile), `TurretBolt` (Black Metal Missile), `AtgeirGoldUncooked` (Cast: Nord Atgeir, "needs to be hardened with
  frost"); creature `Boar`;
- pieces (display name, prefab name as the T24 dump prints it): Smelter `smelter`, Blast Furnace `blastfurnace`,
  Charcoal Kiln `charcoal_kiln`, Windmill `windmill`, Spinning Wheel `piece_spinningwheel`, Eitr Refinery
  `eitrrefinery`, Frigid Kiln `piece_FrostKiln`, Frost Foundry `piece_FrostFoundry`, Hot Tub `piece_bathtub`,
  Campfire `fire_pit`, Hearth `hearth`, Bonfire `bonfire`, Standing Brazier `piece_brazierfloor01`, Sconce
  `piece_walltorch`, Cooking Station `piece_cookingstation`, Iron Cooking Station `piece_cookingstation_iron`, Stone
  Oven `piece_oven`, Shield Generator `piece_shieldgenerator`, Ballista `piece_turret`.

Capacities read from the game data (1.0.16): Smelter and Blast Furnace 10 ore / 20 coal, Eitr Refinery 20 / 20,
Charcoal Kiln 25, Windmill 50, Spinning Wheel 40, Frigid Kiln 25 Ice (its only slot), Hot Tub 10 Wood (a fuel slot
like a smelter's, not a fire), Cooking Station 2 slots, Iron Cooking Station 5, Stone Oven 4 slots + 10 Wood (no fire
needed), Frost Foundry 1 cast slot + 20 Liquid Frost (no fire needed). The "(x/max)" in each hover is the reference
if a value differs. In the game data no fire that takes fuel can also be turned off (the Resin Candle can be turned
off but not refilled), so T08 and T26 are expected to be `[-]` unless the T24 dump lists one.
**(unverified)**: which casts the Frost Foundry accepts (T10: try `AtgeirGoldUncooked`, else an item from the
`piece_FrostFoundry` `items=` list of the T24 dump), that the Stone Oven takes `BreadDough` (T10), that the Frigid
Kiln's fuel is `Ice` (its hover and the T24 dump say), that holding E on a station switch repeats every 0.2 s (T16),
and the exact item each station takes where not stated above (read it in the hover or in the T24 dump).

Build next to each other: Smelter, Blast Furnace, Charcoal Kiln, Windmill, Spinning Wheel, Eitr Refinery, Frigid Kiln,
Hot Tub, Frost Foundry, Campfire, Hearth, Bonfire, Standing Brazier, Sconce, Cooking Station and Iron Cooking Station
(each over a campfire), Stone Oven, Shield Generator, Ballista, a Fermenter, a chest. A Smelter cannot be emptied
(it has no Empty switch): when a test needs an empty one, build a new one (free with `nocost`), or remove it with the
hammer and build it again (removing it drops its ore and coal). For T24 and the multiplayer checks, turn on Debug
logging (`./tools/Setup.ps1 -DevBepInExConfig`) and keep `./tools/Watch-Log.ps1 -Mine` open: each batch press that
runs its loop logs one `Batch feed: <prefab> <kind> owner=you|other room=.. asked=.. added=.. presses=.. stop=..`
line (`added=0 stop=refused` when the station refused the first item); a batch press handed to vanilla as a single
press (station already full, no owner...) logs nothing, except `owner=gone` (M10); a non-owner press stopped by the
mod's own count logs `stop=pending` or `stop=otherammo`.

## 0.1.0 — single player

- [x] **T01 Smelter input:** empty Smelter, 20 `CopperOre`. Look at the ore slot ("Add item"), press Shift+E once.
  Expected: the hover goes to 5/10, 15 ore left, one message "Added 5 Copper Ore (5/10)", one add sound.
  *Automated: `batchfeed.smelter`.*
- [x] **T02 Capacity clamp:** bring the Smelter to 8/10 with plain E, then Shift+E. Expected: exactly 2 added
  ("Added 2 Copper Ore (10/10)"), inventory −2. Shift+E again: "It's full", nothing removed.
  *Automated: `batchfeed.smelter`.*
- [x] **T03 Inventory clamp:** carry exactly 3 `CopperOre` and no other ore, empty Smelter, Shift+E. Expected: 3
  added, "Added 3 Copper Ore (3/10)", no ore left.
  *Automated: `batchfeed.smelter`.*
- [x] **T04 Mixed input:** carry 3 `CopperOre` and 3 `TinOre`, empty Smelter, Shift+E. Expected: 5 added in total,
  3 of one kind and 2 of the other (the smelter takes the first ore of its own list first); one message lists both,
  e.g. "Added 3 Copper Ore, 2 Tin Ore (5/10)".
  *Automated: `batchfeed.smelter`.*
- [x] **T05 Fuel slot:** Smelter coal slot with 30 `Coal`: Shift+E adds 5 ("Added 5 Coal (5/20)"). Shift+E twice
  more (15/20), plain E twice (17/20), then Shift+E: exactly 3 added, 20/20 (vanilla accepts coal while the fuel is
  at most 19, and burning ore can lower it a little: judge from the hover just before the press).
  *Automated: `batchfeed.smelter`.*
- [x] **T06 Smelter family:** Shift+E on: Blast Furnace (`BlackMetalScrap`, `Coal`), Charcoal Kiln (`Wood`),
  Windmill (`Barley`), Spinning Wheel (`Flax`), Eitr Refinery (`Sap` input, `Softtissue` fuel), Frigid Kiln (`Ice`,
  its only slot, 25 max), Hot Tub (`Wood`, 10 max). Expected: each adds up to 5 (or up to the room), one message
  each, for example "Added 5 Wood (5/10)" on the Hot Tub.
  *Automated: `batchfeed.family`.*
- [x] **T07 Fires:** Shift+E on Campfire, Hearth, Bonfire, Standing Brazier, Sconce, with their fuel (Wood, Coal or
  Resin, as the hover says). Expected: up to 5 fuel, "Adding 5 Wood to the fire (x/max)"; a fire 3 below its maximum
  gets exactly 3; the fuel count in the inventory drops by exactly what the counter rose.
  *Partly automated (`batchfeed.fires`); by hand: Shift+E on a Sconce with Switchable Lights (MC) turned off or not
  installed: up to 5 Resin added, matching message.*
- [x] **T08 Fire that can be turned off:** only if the T24 dump lists a Fireplace with `canRefill=True
  canTurnOff=True infiniteFuel=False` (none in vanilla 1.0.16 game data: else mark `[-]`). Expected: E toggles it as
  in vanilla; Shift+E adds up to 5 fuel and does not toggle.
  *Automated: `batchfeed.fires`.*
- [x] **T09 Cooking:** Cooking Station (2 slots) over a lit Campfire, 10 `RawMeat`: Shift+E fills both slots, "Added
  2 Boar Meat (2/2)". Iron Cooking Station (5 slots) over a lit Campfire: Shift+E, "Added 5 Boar Meat (5/5)". Wait
  until one piece is cooked: the Shift hint disappears; Shift+E takes one cooked piece (like E) and adds no raw meat.
  No fire below: "You need to have a lit fire below the cooking station", nothing removed.
  *Automated: `batchfeed.cooking`.*
- [x] **T10 Stone Oven and Frost Foundry:** Stone Oven fuel slot with 20 `Wood`: "Added 5 Wood (5/10)"; its food slot
  with 6 `BreadDough` (unverified: oven recipe): "Added 4 Bread Dough (4/4)". Frost Foundry Liquid Frost slot with 25
  `FrozenFuel`: Shift+E adds 5 ("Added 5 Liquid Frost (5/20)"), near 20 only what fits. Frost Foundry cast slot
  ("Place Cast") with 3 casts (`AtgeirGoldUncooked`, unverified: if it refuses it, use an item from the
  `piece_FrostFoundry` `items=` list of T24): no Shift hint on that slot, and Shift+E places exactly 1 cast
  (vanilla).
  *Automated: `batchfeed.oven-foundry`.*
- [x] **T11 Shield Generator and Ballista:** Shield Generator fuel slot + 20 `BoneFragments`: up to 5 ("Added 5 Bone
  Fragments (x/max)"). Ballista + 20 `TurretBoltWood`: up to 5 ("Added 5 Wooden Missile (5/max)"). While wood
  missiles are loaded, carry only `TurretBolt` and press Shift+E: "The ballista is already loaded with Wooden
  Missile", nothing removed. Empty Ballista, 2 `TurretBoltWood` in an earlier inventory slot (top-left first) than 20
  `TurretBolt`: Shift+E adds the 2 Wooden Missiles only ("Added 2 Wooden Missile (2/max)"), 20 black-metal kept.
  *Automated: `batchfeed.shield-turret`.*
- [x] **T12 Hover hint:** on every covered spot of T01–T11 a line `[L-Shift + E] <action> x5` appears directly below
  the `[E] ...` line, in the same style (e.g. "Add item x5", "Add Coal x5", "Use Wood x5", "Cook item x5", "Bake item
  x5", "Add bones x5", "Add missile x5"). No hint on: the Windmill's Empty switch, a chest, a bed, the Shield
  Generator's own attack text, a tame, the Frost Foundry's cast slot, a Resin Candle, a Fermenter, a Cooking Station
  holding a cooked piece (the hint comes back within about a quarter second after the last cooked piece is taken).
  With `ShowHint = false` the hint disappears (Shift+E still adds 5); it comes back with `true`.
  *Partly automated (`batchfeed.hint`); by hand: The x5 hint on a Sconce with Switchable Lights (MC) turned off or not
  installed.*
- [x] **T13 Rebinding in the game:** Settings → controls: rebind "Alternative placement" to L-Alt and "Use" to U
  (a free key: F is Forsaken power, and the game does not unbind a key used twice). Expected: hint `[L-Alt + U] ...`
  within half a second; L-Alt+U batches, Shift+U adds 1. Reset the bindings.
  *Automated: `batchfeed.rebind`, `batchfeed.rebind-keys`.*
- [x] **T14 Mod-only key:** with `ModifierKey = None`, Right Shift + E adds 1 (only Left Shift is the default key).
  Set `ModifierKey = RightShift` (ConfigurationManager or the cfg file, no restart). Expected: hint
  `[R-Shift + E] ...`; R-Shift+E batches; L-Shift+E adds 1 (on a fire: 1 fuel). Set it back to `None`.
  *Automated: `batchfeed.settings`, `batchfeed.realkey`, `batchfeed.mp.settings`.*
- [x] **T15 Amount:** set `Amount = 10` live. Expected: hint "x10", Shift+E adds up to 10. Set it back to 5.
  *Automated: `batchfeed.settings`, `batchfeed.mp.settings`.*
- [x] **T16 Hold:** hold Shift+E on a Campfire with little fuel for 2 s. Expected: +5 at once, then +1 about every
  0.2 s until full, and the wood count dropped by exactly the fuel added. Then, with 20 ore, hold plain E for 1 s on
  the ore slot of a new Smelter and note the pace (the game data suggests +1 about every 0.2 s); build another new
  Smelter and hold Shift+E on its ore slot. Expected: +5 at once, then the same pace as the plain hold, never above
  10/10.
  *Automated: `batchfeed.hold`.*
- [x] **T17 Vanilla actions untouched:** Shift+E on a tame (rename box opens), an item stand holding an item (same as
  vanilla: its orientation changes when the stand allows it), a saddled animal (saddle comes off), a dropped item
  (picked up), a chest (opens), the Windmill's Empty switch (empties it), a Fermenter (exactly 1 item, as E), and, if
  one is near (optional), a boss altar (no hint, same result as E). Plain E on every covered piece still adds exactly
  1. With the Hammer equipped, looking at a Smelter: no hover text, no hint, and neither E nor Shift+E does anything
  (vanilla).
  *Partly automated (`batchfeed.vanilla`); by hand: Item stand holding an item, saddled animal (saddle comes off),
  boss altar, a roofed Fermenter taking exactly 1 item, and the Hammer-equipped case (no hover, nothing happens).*
- [x] **T18 Hotbar key:** put Coal in the hotbar, look at the Smelter's coal slot, press its number key. Expected:
  1 coal added (vanilla).
  *Automated: `batchfeed.smelter`.*
- [x] **T19 Refusal messages:** Shift+E with nothing the station takes: Smelter ore slot → "You don't have any
  processable items"; Smelter coal slot without coal → "You don't have any Coal"; Campfire without wood → "You are
  out of Wood". Expected: one message each, no flicker, nothing removed.
  *Automated: `batchfeed.smelter`, `batchfeed.fires`.*
- [ ] **T20 Gamepad:** default controller layout: the hint shows controller glyphs (the Alternative placement button
  + the use button); holding that button and pressing use batches. If you can, switch to an alternative controller
  layout: the hint shows the alt-keys button glyph and that combination batches. The `ModifierKey` description in
  ConfigurationManager names the same buttons.
  *Partly automated (`batchfeed.settings`); by hand: A real controller: the glyphs as drawn on screen, the physical
  button combination batching on the default and on an alternative layout, and the ModifierKey description in
  ConfigurationManager.*
- [x] **T21 Live toggle:** Esc → MC Mods → untick Batch Station Feeding. Expected: no hint, Shift+E adds 1. Tick it
  again: hint back, Shift+E adds 5, no restart.
  *Automated: `batchfeed.mp.toggle`, `batchfeed.toggle`.*
- [x] **T22 Disabled in the config file:** `Enabled = false` in `BepInEx/config/MC.Crafting.Stations.BatchFeed.cfg`,
  restart. Expected: vanilla everywhere, no hint. Set it back to `true`.
- [x] **T23 Clean log:** after a session, no errors or exceptions mentioning `Batch Station Feeding` or
  `MC.Crafting` in `BepInEx/LogOutput.log`.
  *Partly automated (`batchfeed.log`, `batchfeed.mp.log`); by hand: The log after a normal play session (the automated
  run only covers what the self tests do).*
- [x] **T24 Coverage dump (game data check):** with Debug logging, load a world. Expected: no error while loading,
  `Batch feed coverage:` lines ending with a count line, including:
  - `smelter`, `blastfurnace`, `eitrrefinery`: `Smelter ... input covered ... fuel covered`; `charcoal_kiln`,
    `windmill`, `piece_spinningwheel`: input covered, `fuel skipped (no fuel switch)`;
  - `piece_FrostKiln Smelter ... input skipped (no input switch) ... fuel covered (maxFuel=25, fuel=Ice)` (the fuel
    name is unverified: write down what it prints);
  - `piece_bathtub Smelter ... input skipped (no input switch) ... fuel covered (maxFuel=10, fuel=Wood)`;
  - `Fireplace` lines for `fire_pit`, `hearth`, `bonfire`, the braziers, torches and `piece_walltorch`: covered; the
    candles: `skipped (not refillable)`; fires with `infiniteFuel=True`: skipped; a light with `maxFuel=1` (the game
    data has one Standing Wood Torch record like that): `skipped (capacity 1)`, write down its prefab name;
  - `piece_cookingstation` and `piece_cookingstation_iron`: `food covered via station (slots=2 / slots=5 ...)`;
    `piece_oven`: `food covered via switch (slots=4 ...)`, `fuel covered (maxFuel=10, fuel=Wood ...)`;
  - `piece_FrostFoundry CookingStation ... food skipped (capacity 1 slot(s)) via switch ... fuel covered
    (maxFuel=20, fuel=FrozenFuel ...)`;
  - `piece_shieldgenerator ShieldGenerator ... fuel covered`, `piece_turret Turret ... ammo covered`.
  Write down any Fireplace with `canRefill=True canTurnOff=True infiniteFuel=False` (for T08, T26), the Frost
  Foundry's and Stone Oven's `items=` lists, and fix anything above marked unverified. The dump lists vanilla prefabs
  (and modded ones registered before the world loads) only.
  *Automated: `batchfeed.coverage`.*
- [x] **T25 First skill level under the summary:** Cooking at 0 (new character, or console `resetskill Cooking`).
  Iron Cooking Station over a lit fire with 5 free slots, 5 `RawMeat`, Shift+E (each item gives 0.4; with a gain step
  of 1 for Cooking (unverified) and the default skill gain, the 3rd item reaches Cooking 1). Expected: one center
  message with two lines: "Added 5 Boar Meat (5/5)" and, below it, "Skill improved Cooking: 1". Later level-ups show
  in the top-left corner, as in vanilla.
  *Automated: `batchfeed.skill`.*
- [x] **T26 Mod-only key never turns a fire off:** only if T24 found a fire with `canRefill=True canTurnOff=True`
  (else `[-]`). `ModifierKey = RightShift`. Fill that fire to its maximum, then R-Shift+E: "You can't add more ...",
  the fire stays on. Plain E still toggles it. Set `ModifierKey` back to `None`.
  *Automated: `batchfeed.settings`, `batchfeed.realkey`.*
- [x] **T27 Key the game cannot read:** set `ModifierKey = F13` (in the file, or in ConfigurationManager's key list).
  Expected: one warning in the log ("the game cannot read this key ... Alternative placement key"), no error, the
  hover hint at a kiln names the Alternative placement key (for example `[L-Shift + E]`), not F13, and Shift+E still
  batch-feeds. Set `ModifierKey` back to `None`.
  *Automated: `batchfeed.settings`, `batchfeed.mp.settings`.*

## 0.1.0 — multiplayer (needs a second player)

Needs a second player without the mod (host, or a vanilla dedicated server). To make the friend the owner of a
station: stay far away (300 m or more) while the friend walks to the station and stays next to it, then come. The
Debug line of each batch then says `owner=other`.

- [x] **M01 Vanilla server and friend:** you own the station (you arrived first). Shift+E on a Smelter and a
  Campfire. Expected: works as in single player; the friend sees the counters rise; no errors on either side.
  *Partly automated (`batchfeed.mp.owner`, `probe.mp.baseline`); by hand: A real second player's screen (the counters
  as the friend sees them rise) and the friend's / server's log free of errors. The test reads this game's own send
  record, not the server's or a friend's copy: the dedicated test server has no mod and builds no world objects.*
- [x] **M02 Non-owner, repeated batches:** the friend owns an empty Smelter (10). You press Shift+E three times
  within one second with 20 ore. Expected: 5, 5, then "It's full"; exactly 10 ore left your inventory; the hover
  reaches 10/10 after a moment, never above.
  *Partly automated (`batchfeed.mp.nonowner-smelter`, `batchfeed.nonowner-smelter`); by hand: With a second player who
  owns the smelter: the hover (the friend's counter) reaches 10/10 after a moment and never goes above. No automated
  run has a second game that owns and runs a station: the dedicated test server (reference position pinned at
  (1000000, 0, 1000000) by its build) hands every station straight back and runs none of the adds.*
- [x] **M03 Non-owner fire, no loss:** the friend owns a Campfire at 7/10. Shift+E twice quickly with 10 Wood.
  Expected: 3 added, then "You can't add more Wood"; exactly 3 wood left your inventory.
  *Partly automated (`batchfeed.nonowner-others`); by hand: On a friend's Campfire at 7/10 (two players): the fire
  really ends at 10/10, so the 3 wood arrived and none was lost. No automated run has a second game that owns and runs
  a station: the dedicated test server (reference position pinned at (1000000, 0, 1000000) by its build) hands every
  station straight back and runs none of the adds.*
- [x] **M04 Non-owner mixed presses:** the friend owns an empty Smelter. E, E, Shift+E within one second with 20 ore.
  Expected: 7 in the queue, 7 ore gone.
  *Partly automated (`batchfeed.mp.nonowner-smelter`, `batchfeed.nonowner-smelter`); by hand: With a second player who
  owns the smelter: 7 in the friend's queue (hover 7/10) after a moment. No automated run has a second game that owns
  and runs a station: the dedicated test server (reference position pinned at (1000000, 0, 1000000) by its build)
  hands every station straight back and runs none of the adds.*
- [x] **M05 Hand-off:** after M02, the friend (no mod) adds Coal with E, waits for the bars, picks them up and presses
  E on the ore slot. Expected: normal Copper bars come out (one per ore you added), the friend's E adds 1 ore, no
  errors on either side.
  *Partly automated (`batchfeed.mp.nonowner-smelter`, `batchfeed.mp.owner`); by hand: Everything the friend does: a
  second player's game without the mod owns the smelter you batch-fed, adds Coal with E, gets one normal Copper bar
  per ore you added, their E on the ore slot adds 1 ore, and neither log has errors. No automated run has a second
  game that owns and runs a station: the dedicated test server (reference position pinned at (1000000, 0, 1000000) by
  its build) hands every station straight back and runs none of the adds.*
- [x] **M06 Non-owner cooking:** the friend owns an Iron Cooking Station over a fire. Shift+E twice quickly with 10
  `RawMeat`. Expected: never more meat removed from your inventory than there were free slots; nothing vanishes.
  *Partly automated (`batchfeed.nonowner-others`); by hand: On a friend's Iron Cooking Station (two players): all 5
  pieces are on the station afterwards, nothing vanished. No automated run has a second game that owns and runs a
  station: the dedicated test server (reference position pinned at (1000000, 0, 1000000) by its build) hands every
  station straight back and runs none of the adds.*
- [x] **M07 Non-owner ballista, mixed missiles:** the friend owns an empty Ballista. Put 2 `TurretBoltWood` in an
  earlier inventory slot (top-left first) than 20 `TurretBolt`. Press Shift+E, then within 3 s press E and Shift+E
  again. Expected: 2 wooden missiles added (hover "2 / max" after a moment); the later presses show "The ballista is
  already loaded with Wooden Missile" and remove nothing; 20 black-metal missiles kept. Destroy the Ballista (friend
  or you): exactly 2 Wooden Missiles drop.
  *Partly automated (`batchfeed.nonowner-others`); by hand: On a friend's Ballista (two players): the hover shows 2 /
  max after a moment, and destroying the Ballista drops exactly 2 Wooden Missiles. No automated run has a second game
  that owns and runs a station: the dedicated test server (reference position pinned at (1000000, 0, 1000000) by its
  build) hands every station straight back and runs none of the adds.*
- [x] **M08 Non-owner shield and oven fuel never above max:** the friend owns a Shield Generator a few bones from max
  and a Stone Oven a few Wood from max. Press Shift+E three times quickly on each fuel slot. Expected: each counter
  ends at max, never above (hover after a moment), and exactly the added amount left your inventory.
  *Partly automated (`batchfeed.nonowner-others`); by hand: On a friend's Shield Generator and Stone Oven (two
  players): each counter ends at its maximum and never above (hover after a moment). No automated run has a second
  game that owns and runs a station: the dedicated test server (reference position pinned at (1000000, 0, 1000000) by
  its build) hands every station straight back and runs none of the adds.*
- [x] **M09 Take-out after a non-owner batch:** the friend owns a Cooking Station over a lit fire. Put 1 `RawMeat` on
  it with E; a few seconds before it is cooked, Shift+E so that the batch fills the other slot; as soon as the first
  piece is cooked (within 3 s of the batch), press plain E. Expected: the cooked piece comes out (no "There is no
  room"). If T08 found a fire that can be turned off: after a non-owner Shift+E on it, a plain E within 3 s still
  toggles it.
  *Partly automated (`batchfeed.nonowner-others`); by hand: On a friend's Cooking Station over a lit fire (two
  players): plain E as soon as the first piece is cooked, within 3 s of the batch: the cooked piece really comes out.
  No automated run has a second game that owns and runs a station: the dedicated test server (reference position
  pinned at (1000000, 0, 1000000) by its build) hands every station straight back and runs none of the adds.*
- [x] **M10 Owner just left:** the friend owns a Smelter (Debug line `owner=other`) and logs out. Right away (within
  about 2 s) press Shift+E on it with 20 ore. Expected: at most 1 ore leaves your inventory and the log shows
  `owner=gone`. A few seconds later (the station now has a new owner: you), Shift+E adds 5 again.
  *Automated: `batchfeed.mp.ownergone`, `batchfeed.ownergone`.*
- [ ] **M11 Non-owner hold:** the friend owns an empty Smelter with no coal in it (so its queue does not drain). With
  20 `CopperOre`, press Shift+E on its ore slot and keep both keys held for 2 s. Expected: +5 at once, then +1 per
  hold repeat (the same pace as the plain hold of T16; nothing more if holding E added nothing there), then "It's
  full" with nothing more removed; exactly 10 ore left your inventory (5 if holding adds nothing), and the hover
  reaches 10/10 after a moment, never above. Without holding: Shift+E twice, then plain E within one second: "It's
  full", nothing removed.
  *Partly automated (`batchfeed.mp.nonowner-smelter`, `batchfeed.nonowner-smelter`); by hand: On a friend's empty
  Smelter without coal (two players): after the hold the hover reaches 10/10 after a moment and never goes above. No
  automated run has a second game that owns and runs a station: the dedicated test server (reference position pinned
  at (1000000, 0, 1000000) by its build) hands every station straight back and runs none of the adds.*

## 0.1.0 — compatibility (optional, needs another mod)

- [x] **C01 One Click Repair All (MC):** both installed. Repair several items at a workbench (one summary message),
  then Shift+E a smelter (one summary), then repair again. Expected: both summaries show; no message is swallowed
  after a batch.
  *Partly automated (`batchfeed.compat-repair`); by hand: The repair mod's own summary at a workbench before and after
  a batch (its bulk repair is not driven by this test).*
- [x] **C02 Creature Kill and Tame Counts (MC):** both installed, test character, no other tameable creature loaded
  nearby (`tame` tames all of them; see that mod's Setup). Shift+E on a smelter, then right away `spawn Boar`, stand
  next to it and run `tame`. Expected: "Boar has been tamed" shows and the Boar tame count in Valheim Compendium > Player
  Statistics goes up by 1.
  *Partly automated (`batchfeed.compat-tames`); by hand: The Boar tame count as displayed in Valheim Compendium >
  Player Statistics (the test reads the stored count, not the page).*
- [ ] **C03 Other batch mod** (only if BulkSmelt, ComfyAddAllFuel or Add All Fuel And Ore is available; else `[-]`):
  both installed, Shift+E on a Smelter you own and, in multiplayer, on one the friend owns. Expected: one press fills
  what the other mod fills and no more (never above max after a moment); the Debug line says `stop=othermod`; no
  errors.
  *Partly automated (`batchfeed.othermod`); by hand: A real BulkSmelt / ComfyAddAllFuel / Add All Fuel And Ore
  install, in single player and on a friend's station: never above max, no errors.*
- [x] **C04 Switchable Lights (MC):** both installed and on (its default `Lights` list has the Sconce and the
  standing torches). Expected: a Sconce (`piece_walltorch`) and a Standing Wood Torch (`piece_groundtorch_wood`) show
  no "x5" hint; Shift+E switches the light exactly once, like E, and the `Resin` count does not change. Campfire,
  Hearth, Bonfire and Standing Brazier still show the hint and take 5 fuel with Shift+E. (T07 and T12 list the Sconce
  as a batch target: that holds only while Switchable Lights is off, not installed, or has the Sconce removed from
  its `Lights` list.)
  *Automated: `batchfeed.compat-lights`.*
- [!] **C05 Hint follows Switchable Lights (MC) turned on or off:** start with Switchable Lights off (MC Mods panel
  or F1) and look at a Sconce: the "x5" hint shows. Keep the crosshair on the Sconce (or look at a chest and back)
  and turn Switchable Lights on. Expected: within about half a second the hover shows `[E] Turn off` with no batch
  hint, and Shift+E switches the light once. Turn Switchable Lights off again while still looking at the Sconce.
  Expected: the "x5" hint is back within about half a second, without looking at another fire first.
  **FAILED:** automated test: Hover hint keeps its cached covered / not covered answer when another mod flips a fire's
  flags while the crosshair stays on it (self-test `batchfeed.bug.hint-stale`)
- [ ] **C06 Mod that takes a station over before an add** (only if such a mod is installed, for example
  ValheimCommunityPatch with its "Fix Fuel And Ore Loss" setting on, which makes you the owner of a smelter, kiln or
  fire right before fuel or ore is added; else `[-]`): in multiplayer, the friend owns an empty Smelter. Press Shift+E
  three times quickly with 20 `CopperOre`. Expected: 5, 5, then "It's full"; exactly 10 ore left your inventory; the
  hover shows 10/10 at once and never more; the Debug line of the first press says `owner=other`, the next one
  `owner=you` (the station became yours during the first press); no errors on either side. With such a mod the
  non-owner cases M02, M03, M04 and M11 behave like this on smelters, kilns and fires.
  *Partly automated (`batchfeed.compat-claim`); by hand: A real friend's station in multiplayer with such a mod
  installed: the friend's counter, and no errors on either side.*
