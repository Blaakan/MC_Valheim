# Fishing Fight — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-10-01 (loads, patches cleanly). In-world self-tests
(`./tools/Test-InWorld.ps1 -Only fishing.`) passed 2026-10-01 on the final code (build f37c457+dirty):
`fishing.network` 19 checks, `fishing.logic` 50, `fishing.pending` 4 and `fishing.fight` 36 (a live fight with a
Perch on a sea shore: catch bar in and out of the zone, a struggle to the right with the fish swimming right and taking
line, wrong and right side reel, side switch, catch, loss at 0 stamina). An earlier run failed one check (the right
side reel stalled while the fish dragged the float, fixed by allowing 1 m of drag in a fight). The full suite of every
MC mod passed apart from that check. No hands-on in-game test yet.

**Automated checks (2026-10-08):** 21 of 23 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off), `heal` refills health and stamina. Gear: `spawn FishingRod`,
`spawn FishingBait 50`. Fish: stand at the edge of a lake or the sea, facing the water, and `spawn Fish1 3` (three
Perch 2 m ahead of you, in the water; `spawn Fish1 3 3` = with two stars). Equip the rod, cast (attack) next to them,
wait for a nibble (the float dips) and press Block to hook. Skills: `resetskill Fishing` (back to 0),
`raiseskill Fishing 100`. Time: `tod 0.5` (noon), `tod -1` (normal time).

**Names checked:** `FishingRod`, `FishingBait`, `Fish1` and the float `FishingRodFloat` are used by the live self-test
(`fishing.fight`), and all 12 fish prefabs (`Fish1`..`Fish12` with `Fish4_cave`) are checked to exist by
`fishing.logic`. The console commands above are in the game code (`Terminal`; `spawn <name> <amount> <level>`
places the objects 2 m ahead of you). Which fish takes which bait is game data (unverified here): the Perch is
expected to take the normal Fishing bait. The quoted game messages ("Hooked", "Lost catch", "Line broke", "Caught",
"It's not taking the bait") are the English texts of the game's `$msg_fishing_*` keys, read in the installed game's
localization data (1.0.16). Values measured by the live run (Valheim 1.0.16): the rod's reel speed is 2 m/s at
Fishing 0 and 6 at Fishing 100, its own pull cost 0 (x0.2 at Fishing 100), break distance 10 m, longest line 30 m; a
Perch costs 3 stamina per second calm and 10 fighting. In the fight a Perch's line came in at about 0.8 m/s in the zone
and 1 m/s on the right side in a fight; off the zone it drained 3 stamina/s, a wrong-side reel 40/s, and a fighting
Perch took line at 1.1 m/s while swimming sideways at 2.6 m/s.

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Farming.Fishing.Fight.cfg`; default settings unless an item says otherwise, and put each changed
one back afterwards. Stamina items need `god` off only where they say so (god mode does not stop stamina use).

## 0.1.0 — single player

- [ ] **T01 Cast and bite unchanged (G1):** cast near the fish without pressing anything. Expected: same as the normal
  game: the line length shows, a fish swims to the float and nibbles (the float dips), wrong bait says "It's not taking
  the bait", pressing Block right after a nibble shows "Hooked". Reeling in before any bite reels the empty line as in
  the normal game (no catch bar) and gives the bait back.
  *Partly automated (`fishing.cast`); by hand: A fish swimming to the float and nibbling by itself (the float visibly
  dipping): in the run no Perch nibbled within 8 s, so the test delivered the nibble through the float's own nibble
  call; the fish's approach is not asserted.*
- [x] **T02 Catch bar appears (G2):** hook a Perch. Expected: a vertical bar to the right of the crosshair with the
  Perch icon moving in it, a green zone at the bottom and a thin line meter beside it; Debug "Fight started: Fish1
  quality 1, difficulty 15 (Mixed), line ... m."
  *Automated: `fishing.fight`, `fishing.logic`.*
- [x] **T03 Zone follows Block (G2):** hold Block, then let go. Expected: the zone speeds up toward the top and stops
  there while held; let go, it falls, bounces a little on the bottom and settles. With the fish inside the zone it
  rises and falls more softly.
  *Automated: `fishing.logic`, `fishing.fight`.*
- [x] **T04 Fish in the zone = free line (G3):** `heal`, then keep the icon in the zone for 5 s. Expected: the zone is
  green, the line length in the middle of the screen goes down (about 0.8 m per second for a Perch at Fishing 0 in the
  in-world run), the meter fills, and the stamina bar does not go down; spend some stamina first (sprint) and it comes
  back while the fish stays in the zone.
  *Automated: `fishing.fight`.*
- [x] **T05 Fish out of the zone = stamina (G4):** let the icon leave the zone for 3 s. Expected: the zone turns
  orange, the line length stays the same, and stamina drops (about 3 per second at Fishing 0 for a Perch without
  stars, measured).
  *Automated: `fishing.fight`.*
- [x] **T06 The fish fights (G5):** wait in the calm phase (about 5 to 11 s for a Perch). Expected: the bar
  disappears, the fish runs hard to your left or right: the float and the line move that way, with splashes; Debug "Fight: the fish fights
  for ... s, running right/left." After a few seconds the bar comes back (Debug "Fight: calm for ... s.") (G10).
  *Automated: `fishing.phases`, `fishing.logic`.*
- [x] **T07 Rod the other way (G6):** in a fight where the fish runs right, turn left so you face about 45 degrees left
  of the line, and hold Block. Expected: the line comes in (about 1 m per second in the in-world run) and stamina drops
  at the normal cost of reeling a fighting fish (about 10 per second for a Perch); the float stays in view at the edge
  of the screen. Same mirrored for a fish running left.
  *Automated: `fishing.fight`.*
- [x] **T08 Rod the wrong way (G7):** in a fight, face the side the fish runs to (or straight at the float) and hold
  Block for 2 s. Expected: no line comes in and stamina drops about four times faster than in T07.
  *Automated: `fishing.fight`, `fishing.logic`.*
- [x] **T09 Fish takes line (G8):** in a fight, do not reel. Expected: no stamina cost, the line length grows (about
  1 m per second for a Perch). `FishDifficulty = 2`, `LineRunSpeed = 5`, `StruggleSeconds = 10`: keep not reeling until
  the line passes 30 m. Expected: "Line broke", the float is gone, the fish swims off.
  *Automated: `fishing.fight`, `fishing.exits`, `fishing.logic`.*
- [x] **T10 Side change (G9):** `FishDifficulty = 2`, hook a Perch and watch several fights. Expected: sometimes the
  fish turns to the other side mid-fight (the float and line swing across); the side
  that was right becomes wrong (T08) until you turn the rod across.
  *Automated: `fishing.phases`, `fishing.fight`, `fishing.logic`.*
- [x] **T11 Arrow setting (added):** `ShowStruggleArrow = true`, fight a fish. Expected: during fights only, an arrow
  beside the crosshair points the way to turn the rod, red until you face far enough that way, then green; it flips
  when the fish changes side. Off again: no arrow.
  *Automated: `fishing.fight`, `fishing.phases`.*
- [x] **T12 Catch:** keep the fish in the zone until the line reaches 0. Expected: "Caught Perch" (as in the normal
  game), the fish is in your inventory, the bar disappears; Debug "Fight ended: caught." Fishing skill rose (Skills
  panel) while the line came in.
  *Automated: `fishing.fight`.*
- [x] **T13 Out of stamina:** `god` off, hook a fish and let the icon stay out of the zone until stamina is empty.
  Expected: "Lost catch", the bar disappears, the float stays in the water (empty) and the fish swims away free (it
  does not keep fighting the empty float). `god` back on.
  *Automated: `fishing.fight`.*
- [x] **T14 Harder fish (added):** `raiseskill Fishing 100` then `resetskill Fishing`, comparing the zone size (bigger
  at 100). Then hook a two-star Perch (`spawn Fish1 3 3`). Expected: the icon moves more wildly and fights last longer
  than with a plain Perch; Debug shows a higher difficulty (31).
  *Automated: `fishing.phases`, `fishing.logic`.*
- [x] **T15 Cancel:** with a fish hooked, attack, then hook another and draw a bow (switch weapons), then hook another
  and put the rod away. Expected: each time the fishing ends as in the normal game, the bar disappears, and the fish
  swims off free. The attack with the rod is a cast: the hooked float goes away and a new, empty float is cast (one
  bait used).
  *Automated: `fishing.exits`.*
- [ ] **T16 Gamepad:** with a gamepad on the Default layout, hold the block button (left trigger) to raise the zone and
  turn with the right stick in a fight. Expected: same as T03 and T07.
- [x] **T17 Toggle block option:** turn on the game's accessibility option "Toggle block". Expected: one press of
  Block keeps the zone rising until the next press. Turn the option off again.
  *Automated: `fishing.input`.*
- [x] **T18 Pause and menus:** with a fish hooked, press Esc (single player), wait 5 s, close it. Expected: everything
  paused, then goes on. Open the inventory for 3 s: you cannot reel, the zone falls, the fight goes on.
  *Automated: `fishing.input`.*
- [x] **T19 Bar settings:** `BarScale = 1.5`, `BarOffsetX = -300`, `BarOffsetY = 100`. Expected: bigger bar left of
  the crosshair and higher, at once. Ctrl+F3 (hide HUD) hides the bar too. Put them back.
  *Automated: `fishing.input`.*
- [ ] **T20 Live toggle:** with a fish in the calm phase, untick the mod in the MC Mods panel. Expected: the bar
  disappears at once and the fish goes on with the normal game's reel (hold Block, stamina drain). Tick it again and
  hook a new fish: the bar is back. Repeat during a fight (`StruggleSeconds = 10`): the fish stops running at once,
  lies still about a second, then fights the normal game's way (wiggle, splashes) for a normal short time.
  *Partly automated (`fishing.toggle`); by hand: By eye, the sentence "the fish stops running at once, lies still
  about a second": the test only asserts that the fish is no longer escaping or steered. Its speed half a second after
  the turn-off was 1.46 m/s (2.41 before) in the first run and 0.26 m/s (2.65 before) in the second: it glides to a
  stop rather than lying still at once. Decide whether that look is right, and watch the normal game's wiggle and
  splashes that follow.*
- [ ] **T21 Enabled = false + restart:** set `Enabled = false`, restart the game. Expected: normal game fishing, no
  bar, MC Mods panel shows the mod off. Set it back.
  *Partly automated (`fishing.toggle`); by hand: Set Enabled = false, restart the game: the MC Mods panel (or F1)
  shows the mod off and fishing is the normal game's from the start.*
- [ ] **T22 Clean log:** after T01-T25, the BepInEx log has no error or exception from this mod (warnings about
  "Failed to find fishing rod top" also come from the normal game when the rod is put away).
  *Partly automated (`fishing.network`, `fishing.logic`, `fishing.pending`, `fishing.fight`, `fishing.cast`,
  `fishing.phases`, `fishing.exits`, `fishing.swim`, `fishing.toggle`, `fishing.input`, `fishing.dual`,
  `fishing.harpoon`); by hand: The log after the hands-on items that no test drives (gamepad, a real logout, real
  second player).*
- [ ] **T23 Logout mid-fight:** hook a fish, log out during the calm phase, load a world again (and once more logging
  out during a fight with `ShowStruggleArrow = true`). Expected: no catch bar or arrow on screen before the next fish
  is hooked; Debug "Fight ended: the mod was turned off." or "... its float or fish is gone." at logout.
  *Partly automated (`fishing.exits`); by hand: A real logout mid-fight and loading a world again: no bar or arrow on
  screen before the next fish is hooked.*
- [ ] **T24 Fight in the shallows:** fishing from a beach, keep the fish in the zone until it is in the last 2-3 m of
  shallow water and wait for a fight. Expected: the fish runs along the shore and may turn once if the other way is
  deeper, but the side does not swap every second (with `ShowStruggleArrow = true` the arrow stays steady).
  *Partly automated (`fishing.logic`, `fishing.phases`); by hand: Fishing from a real beach with the fish in the last
  metres of shallow water: it runs along the shore and the side (arrow) stays steady.*
- [!] **T25 Struggle reel near the end:** in a fight, reel on the right side until the fish is close. Expected: the
  line keeps coming in while the fish pulls the float sideways (no stall), and the line does not break.
  **FAILED:** automated test: Reeling on the right side in a fight gets slower as the line gets short and stalls about
  0.8 m from the catch (self-test `fishing.bug.struggle-reel`)
- [!] **T26 Fish out of the water (edge case):** from a dock or a shore 1-2 m above the water, `CalmSeconds = 2` and
  `StruggleSeconds = 10`: keep the fish in the zone until the last metres of line lift it out of the water (or drag
  it onto the beach), then wait. Expected: no fight starts while the fish hangs or lies out of the water (the catch
  bar goes on until it is back in the water); or, if one does start, the fish takes no line and holding Block toward
  it does not cost the wrong-side price. Put the settings back.
  **FAILED:** automated test: A fight starts and takes line while the hooked fish is out of the water (self-test
  `fishing.bug.stranded`)
- [x] **T27 Hooked fish picked up (edge case):** with a fish hooked and lifted out of the water near you (or lying on
  the shore), pick it up with Use. Expected: the fish is in your inventory with no "Caught" message, the bar is gone
  at once, Debug "Fight ended: its float or fish is gone." (or "Fight ended: the fish is gone."), the float stays
  where it is, empty, holding Block reels it in as in the normal game with no bait back, and there is no error.
  *Automated: `fishing.exits`.*

## 0.1.0 — multiplayer

- [x] **M01 Server rules:** dedicated server (or host) with `WrongSideStamina = 8` and `BarSize = 0.5`; join as a
  client with defaults. Expected: client log "Using the server's rules: catch bar 50% ..."; in a fight the wrong side
  costs about eight times the right side, and the zone is half the bar.
  *Automated: `fishing.mp.rules`.*
- [ ] **M02 Refused without the mod:** a player without the mod joins. Expected: refused after about a second
  ("Incompatible version"); the server log names them. With `AllowPlayersWithoutMod = true` they join and fish like
  in the normal game.
  *Partly automated (`probe.mp.refused`, `scenario:open-server`); by hand: That a player let in without the mod fishes
  like in the normal game (hold Block to reel, no bar).*
- [ ] **M03 Hand-off: watching a fight:** two players with the mod; one hooks a fish and lets it fight while the
  other watches from 10 m away. Expected: the watcher sees the fish and float run to one side and splash during
  fights, and calm between them (no splashes); no errors on either game. Repeat with the watcher allowed in without
  the mod (`AllowPlayersWithoutMod = true`): same sight, no errors.
  *Partly automated (`fishing.mp.watch`); by hand: A second player watching from 10 m: the fish and float visibly run
  to one side and splash during fights and are calm between them, with no errors on either game; the same with a
  watcher let in without the mod.*
- [ ] **M04 Hand-off: fisher leaves:** while a fish is hooked, the fisher disconnects (once in the calm phase, once
  during a fight). Expected: on the other game the float disappears and the fish swims off normally; when it was
  fighting, its splashes stop within a few seconds (the other game, with the mod, clears the "hooked" flag the
  fisher left behind); no errors.
  *Partly automated (`fishing.mp.left-hook`, `fishing.exits`); by hand: A real fisher disconnecting in the calm phase
  and during a fight, seen from another player's game: the float disappears, the fish swims off and its splashes stop
  within a few seconds, no errors. On a dedicated server the game that clears the flag is the next player near the
  fish, not the server: the test has only one client, so the fish comes back to the game that set the flag instead of
  reaching a second player.*
- [x] **M05 Turned off while connected:** a client unticks the mod while connected. Expected: refused about a second
  later. Other version of the mod (change ModNetworkVersion in a test build): refused.
  *Automated: `probe.mp.off.MC.Farming.Fishing.Fight`, `fishing.mp.version`.*
- [x] **M06 Server without the mod:** join a server that does not have it. Expected: the mod turns itself off on the
  client (MC Mods panel says why) and fishing is the normal game's.
  *Automated: `probe.mp.baseline`, `fishing.mp.no-server`.*

## 0.1.0 — other mods

- [x] **X01 Swim Dive:** fish while standing in chest-deep water, then step into deep water. Expected: the fishing
  ends as in the normal game when you start swimming (rod put away), the bar disappears; no error.
  *Automated: `fishing.swim`.*
- [ ] **X02 Trinkets on Demand:** gamepad, full adrenaline bar, a stamina trinket: hold LT (reel) in a fight and press
  the right stick. Expected: the trinket fires, LT keeps reeling, stamina shows the trinket's effect.
- [ ] **X03 Harpoon Hooks Tames:** harpoon a tame, then switch to the rod and cast. Expected: the harpoon line
  releases as in the normal game, and the fishing fight works normally afterwards.
  *Partly automated (`fishing.harpoon`, `fishing.cast`); by hand: Throwing a real harpoon at a tame (the test puts the
  harpoon effect on the boar itself, no throw), then switching to the rod.*
- [x] **X04 Weapon Moveset and Dual Wielding:** with both on, equip the rod from a pair of axes and cast. Expected:
  both axes leave the hands, a plain cast; Block + Jump while reeling is a normal dodge and the bar goes on after it.
  *Automated: `fishing.dual`.*
- [ ] **X05 Loot Pickup Filter:** with the inventory full, catch a fish. Expected: as in the normal game the fish stays
  where you pulled it in (on the shore or in the shallows); the filter rules apply when you walk over it.
  *Partly automated (`fishing.swim`); by hand: Walking over the fish afterwards with Loot Pickup Filter rules set: the
  filter decides whether it is picked up.*
