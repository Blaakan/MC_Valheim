# Swim Dive — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-10-01 (loads, patches cleanly, JitCheck clean). In-world self-tests
(`./tools/Test-InWorld.ps1`) passed 2026-10-01 on the final code: `dive.network`, `dive.logic`, `dive.pending` and
`dive.dive` (a live dive in 12 m deep sea: descent, hold depth, no sideways movement, drain while still, free at the
surface, camera, fog and surface from below). No hands-on in-game test yet.

**Automated checks (2026-10-08):** 26 of 27 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off), `heal` refills health and stamina, `killall` removes nearby creatures.
Skills: `resetskill Swim` (back to 0), `raiseskill Swim <levels>`. Time and weather: `tod 0` (midnight), `tod 0.5`
(noon), `tod -1` (normal time); `env <name>` forces a weather, `env` alone lists the names, `resetenv` gives the normal
weather back. `debugmode` then `Z` flies (handy to reach deep water fast), `freefly` is the free camera, `goto <x> <z>`
(whole numbers) teleports you far away. Deep water = the dark blue sea well away from the shore; the tests need at
least 10 m of water unless an item says otherwise. Diving keys: hold Crouch (Left Ctrl) to go down, hold Jump (Space)
to go up.

**Names checked:** every console command above is in the game code (`Terminal`); the ship `Karve` (`spawn Karve`; any
ship works) and the creature `Serpent` are prefab names from the 1.0.16 runtime dump (`docs/game/exploration-player.md`,
`docs/design/combat-creatures-morale.md`). Values measured by the in-world run (Valheim 1.0.16, `dive.dive` and
`dive.logic` NOTE lines): swim depth 1.5 m (you swim once the water is deeper than 1.1 m), swim speed 2 m/s, swim
stamina drain 6 per second at Swim 0 down to 3 at Swim 100, camera minimum distance above water 0.4 m, the ocean water
zone reaches 50 m below the sea surface (it reached the floor at the 12 m test spot), fog mode Exponential. The live
dive went down at 1.6 m/s on average over 3 m and drained 6 stamina per second while holding still.

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Exploration.Swimming.Dive.cfg`; default settings unless an item says otherwise, and put each changed
one back afterwards. Stamina items need `god` off only where they say so.

## 0.1.0 — single player

- [x] **T01 Crouch dives (G1):** swim out to deep water, stop, then hold Crouch. Expected: you go straight down at about
  your swimming speed; Debug "Dive started (dive speed x1, no key under water: hold depth, stamina drain while diving
  x1)."; the stamina bar goes down while you descend. Release Crouch just below the surface (before your head is under):
  the normal game lifts you back up ("Dive ended: back at the surface.").
  *Automated: `dive.motion`, `dive.keys`.*
- [x] **T02 Jump rises (G2):** from 5 m down, hold Jump. Expected: you rise at the same speed; just below the surface
  you are handed back to normal swimming (Debug "Dive ended: back at the surface.") and float at the usual height, with
  no jump out of the water.
  *Automated: `dive.motion`, `dive.keys`.*
- [x] **T03 Hold depth (G2):** from 5 m down, release both keys for 10 s, then hold Crouch and Jump together for 5 s.
  Expected: you stay at the same depth both times (a slight drift in the first second is normal).
  *Automated: `dive.motion`.*
- [x] **T04 Only up or down under water (G2):** 5 m down, press W, A, S, D, turn the mouse, and press auto-run.
  Expected: you do not move sideways and your character does not turn; Crouch and Jump still move you. Going down
  just below the surface (head still out) W still swims forward.
  *Automated: `dive.motion`.*
- [x] **T05 Surface unchanged (G3):** at the surface swim around, stop, press Jump in open water, tap Crouch once.
  Expected: normal swimming: stamina drains only while you move, Jump does nothing in open water, a short tap of
  Crouch dips you a little and the normal game lifts you back; next to the shore Jump still climbs out as usual.
  *Automated: `dive.stamina`, `dive.motion`, `dive.keys`.*
- [x] **T06 Still under water drains (G4):** `god`, `heal`, dive 5 m, hold still 10 s. Expected: stamina drops about as
  fast as when you swim forward at the surface for 10 s (6 per second at Swim 0), and does not come back while you
  stay down.
  *Automated: `dive.stamina`.*
- [x] **T07 Still at the surface is free (G4):** `heal`, float still at the surface for 10 s. Expected: stamina stays
  the same (normal game).
  *Automated: `dive.stamina`.*
- [x] **T08 Sea floor:** find water 5-8 m deep, hold Crouch. Expected: you stop just above the floor and hover (no
  walking on it); stamina keeps draining while you touch it; Jump takes you back up. Weapons stay put away while you
  hover (only where you touch a slope or rock may the hide key show them, as in the normal game).
  *Automated: `dive.floor`.*
- [x] **T09 Out of breath:** `god` off, dive 5 m and hold still until the stamina bar is empty. Expected: "Out of
  breath" at the top left once, you float up even while holding Crouch, and drowning damage every second as when
  swimming with no stamina; at the surface Crouch does not start a new dive until you have stamina. Swim to the shore
  to recover. `god` back on.
  *Automated: `dive.stamina`.*
- [x] **T10 Swim skill:** `resetskill Swim`, then dive down and up repeatedly for 30 s. Expected: Swim skill rises.
  Then hold still 5 m down for 30 s: Swim does not rise while you hold still.
  *Automated: `dive.stamina`.*
- [ ] **T11 Gamepad:** with a gamepad on the Default layout, press and hold the crouch button (left stick press) in
  deep water, then the jump button. Expected: dive and rise like T01-T02. Repeat on one Alternative layout (Settings,
  Controls) with its crouch button.
  *Partly automated (`dive.keys`); by hand: A real controller: the crouch button (left stick press) dives and the jump
  button rises, on the Default layout and on one Alternative layout.*
- [ ] **T12 Rebound keys:** rebind Crouch to C and Jump to V in the game's settings. Expected: C dives, V rises, Left
  Ctrl and Space do nothing special. Put the keys back.
  *Partly automated (`dive.keys`); by hand: Rebind Crouch to C and Jump to V in the settings: C dives, V rises, Left
  Ctrl and Space do nothing special.*
- [x] **T13 Under a hull (ceiling rule):** in deep water `spawn Karve`, dive below it and rise into its hull. Expected:
  under the hull W, A, S, D swim you sideways so you can get out; once nothing is above you, the movement keys do
  nothing again. Same under a dock you build over deep water.
  *Automated: `dive.ceiling`.*
- [x] **T14 No jump under water:** dive next to a rock or the sea floor and tap Jump several times while touching it.
  Expected: you only rise; no jump sound, no stamina for a jump, no Jump skill gain.
  *Automated: `dive.ceiling`, `dive.floor`.*
- [ ] **T15 Camera under water (added):** dive until your head is under. Expected: the camera goes under with you and
  stays a little below the surface when you zoom out or orbit; looking up shows the water surface from below, with no
  hole around the camera near the surface. Rising: the camera pops back above as your head comes out. With
  `UnderwaterCamera = false` the camera stays above the water as in the normal game.
  *Partly automated (`dive.view`); by hand: Looks: that the surface seen from below looks right when you look up and
  that there is no hole around the camera near the surface (screenshots dive.view__looking-up,
  dive.view__zoomed-out-looking-down).*
- [ ] **T16 Under-water fog (added):** `tod 0.5`, dive 5 m, look around, then dive to 15 m; `tod 0`; `env` a stormy
  weather while under. Expected: blue-green tint and about 25 m visibility; darker deeper and at night; no flicker when
  the weather changes. Surface: the fog is exactly as before the dive. `UnderwaterVisibility = 60`: you see much
  further. `UnderwaterFog = false`: no tint under water.
  *Partly automated (`dive.view`); by hand: Looks: that the tint reads as blue-green, that 25 m and 60 m look right
  and that nothing flickers to the eye (screenshots dive.view__midnight and dive.dive__underwater).*
- [ ] **T17 Surface from below (added):** 5 m down look up at noon, move to different spots. Expected: the surface is
  seen from below with its waves; no sky showing through; no seams or squares at the edges between water tiles.
  Looking up toward the sun, the top of the screen stays water-blue like the rest of the view (no olive or yellow
  haze); after surfacing, the sky toward the sun looks as before the dive.
  `SurfaceFromBelow = false`: the sky shows through the surface again. After surfacing, the surface looks normal from
  above everywhere (no square left upside down).
  *Partly automated (`dive.view`); by hand: Looks: waves on the surface from below, no sky showing through, no seams
  or squares between water tiles, no olive haze toward the sun, and the surface from above after surfacing.*
- [ ] **T18 Tar pit refused:** in a Plains tar pit, hold Crouch. Expected: no dive, the normal tar behaviour.
  *Partly automated (`dive.exits`, `dive.logic`); by hand: A real Plains tar pit: Crouch does not dive and the normal
  tar behaviour is unchanged.*
- [x] **T19 Ashlands ocean:** dive in the hot Ashlands sea. Expected: diving works; the heat still burns you (normal
  game), no worse and no better at depth.
  *Automated: `dive.ashlands`.*
- [ ] **T20 Death under water:** `god` off, drown under water. Expected: while dead the camera stays where it was
  (normal game) and keeps the under-water tint as long as it is under water; after respawning at your bed the view is
  normal (no tint, surface normal) and the log shows no error. Your tombstone floats up (normal game, unverified from
  depth).
  *Partly automated (`dive.exits`); by hand: A real death by drowning under water: the dead camera stays where it was
  and keeps the tint while under water; after respawning the view is normal and the log has no error; the tombstone
  floats up.*
- [x] **T21 Teleport while under water:** 5 m down, move far away with `goto <x> <z>` (or the map teleport of
  `debugmode`). Expected: the dive ends (Debug "Dive ended: Teleporting."), you arrive normally, no tint or turned
  surface left anywhere.
  *Automated: `dive.exits`.*
- [ ] **T22 Log out under water:** 5 m down, log out to the menu, then back in. Expected: the main menu looks normal
  (no tint); in the world the normal game lifts you to the surface; no leftovers.
  *Partly automated (`dive.view`); by hand: A real log out 5 m down: the main menu looks normal; after logging back in
  the normal game lifts you to the surface; nothing is left over.*
- [x] **T23 Free camera:** 5 m down, `freefly`, fly the camera around (also under water), `freefly` again. Expected:
  in the free camera the view is the normal game's (no tint, surface not turned, even under water); back in the
  diver's view everything is as in T15.
  *Automated: `dive.view`.*
- [x] **T24 Debug fly:** `debugmode`, dive 5 m, press Z. Expected: the dive ends at once and you fly normally; Z again
  and you drop back into the water and swim normally.
  *Automated: `dive.exits`.*
- [x] **T25 IdleRiseSpeed = 0.5:** under water release all keys. Expected: you float up at about half a metre per
  second and are handed back to normal swimming at the surface.
  *Automated: `dive.rules`, `dive.network`.*
- [x] **T26 DiveSpeedMultiplier = 2:** dive and rise. Expected: twice as fast both ways.
  *Automated: `dive.rules`, `dive.network`.*
- [x] **T27 UnderwaterStaminaMultiplier:** `= 0`: diving and holding still cost nothing, and stamina does not come
  back while you hover under water (it does while you touch a slope or rock of the sea floor); `= 2`: twice the drain
  of T06. Surface swimming is the same in both cases.
  *Automated: `dive.multiplier`, `dive.network`.*
- [x] **T28 Encumbered:** carry more than your limit and swim out. Expected: diving works as for T01 (the normal game
  lets you swim encumbered).
  *Automated: `dive.rules`.*
- [x] **T29 Ship and chair:** stand on a ship's deck over deep water and hold Crouch; sit in a ship chair. Expected: no
  dive (crouch on the deck as usual); jump into the water and T01 works.
  *Automated: `dive.ceiling`.*
- [x] **T30 Menus under water:** 5 m down open the inventory, the map and the chat while holding Crouch. Expected: you
  stay at your depth while a menu is open; Crouch and Jump work again once it is closed.
  *Automated: `dive.menus`.*
- [ ] **T31 Serpent escape:** in deep ocean with a Serpent chasing you (`spawn Serpent`), dive 6 m or more. Expected:
  it stays near the surface and cannot bite you; note how deep you needed to go.
  *Partly automated (`dive.serpent`); by hand: That it "stays near the surface" and how deep you need to go: read the
  dive.serpent NOTE (how close it came, how deep it went) or repeat by hand; one Serpent, one run, 7-8 m only.*
- [!] **T32 No drain when the dive cannot go down (G4):** `heal`, then in water about 1.6 m deep (you swim, but your
  feet are near the floor: a gentle shore slope) hold Crouch for 10 s; repeat a little further out, in water about 2 m
  deep. Then in deep water at the surface, with big waves (`env` a stormy weather), hold Crouch and Jump together for
  10 s. Expected: each time you float at the normal swimming height and rise and fall with the waves as when swimming
  normally, and the stamina bar stays full. In the 2 m water you may first dip a little (a small cost while you go
  down), then float back up and stay there while you hold Crouch, with no quick bobbing up and down against the floor.
  **FAILED:** automated test: Storm waves: holding Crouch + Jump at the surface gets the swimmer counted as deep, held
  under the rising wave and drained (self-test `dive.bug.storm-waves`)
- [x] **T33 Bottom of the water (world edge):** the water itself ends about 50 m under the surface, and only the sea at
  the edge of the world is deeper than that: go to open sea about 10,300 m from the world centre (`goto 10300 0` or
  another direction that is neither Ashlands nor Deep North), and stay inside 10,420 m: beyond that line the normal
  game kills anyone more than 40 m down. `god`, then hold Crouch until you stop. Expected: you stop about 50 m down,
  a little above the bottom of the water, with the sea floor still several metres below you, and stay there while
  you hold Crouch; you keep swimming the whole time (no drop to the sea floor, no fall damage); Jump brings you back
  to the surface.
  *Automated: `dive.bottom`.*
- [x] **L01 Live toggle:** 5 m down, untick Swim Dive in the MC Mods panel. Expected: the normal game lifts you to the
  surface within a second or two, the camera goes above water, tint and surface back to normal, no error. Tick it
  again: Crouch dives again.
  *Automated: `dive.exits`.*
- [ ] **L02 `Enabled = false` + restart:** set `Enabled = false`, restart the game. Expected: Status shows the mod is
  off; Crouch does nothing in water; no tint under water ever (the camera never goes under).
  *Partly automated (`dive.exits`); by hand: Set Enabled = false and restart the game: Status says the mod is off,
  Crouch does nothing in water, the camera never goes under.*
- [ ] **L03 Clean log:** play through T01-T32 while `./tools/Watch-Log.ps1 -Mine` runs, then quit the game from the
  menu. Expected: no error or warning from Swim Dive.
  *Partly automated (`dive.motion`, `dive.stamina`, `dive.rules`, `dive.multiplier`, `dive.waves`, `dive.keys`,
  `dive.menus`, `dive.moveset`, `dive.view`, `dive.exits`, `dive.floor`, `dive.ceiling`, `dive.shallow`,
  `dive.ashlands`, `dive.bottom`, `dive.serpent`); by hand: Play normally, quit from the menu and look through the log
  for Swim Dive warnings or errors (the tests end the game with a plain quit).*

## 0.1.0 — multiplayer

- [x] **M01 Server rules:** a dedicated server (or a host) and a player, both with the mod. Set `DiveSpeedMultiplier =
  2` on the server. Expected: the player's log shows "Using the server's rules: dive speed x2, ..." and they dive twice
  as fast; change it to 1 on the server while connected: half a second later the player logs the new rules and dives
  normally. The player's own Diving settings are ignored while connected.
  *Automated: `dive.mp.rules`.*
- [ ] **M02 Player without the mod refused:** a player without the mod joins. Expected: about a second after joining
  their game shows "Incompatible version" and the server logs "Refused …". With `AllowPlayersWithoutMod = true` they
  may play; the server logs a warning naming them; they swim normally and cannot dive.
  *Partly automated (`probe.mp.refused`, `scenario:open-server`); by hand: With AllowPlayersWithoutMod = true: the
  player without the mod swims normally and Crouch does nothing for them.*
- [ ] **M03 How others see a diver (hand-off):** A dives and rises next to B (both with the mod), then again with B
  without the mod (`AllowPlayersWithoutMod = true`). Expected: B sees A go down and up in the treading-water pose, with
  the ripple on the surface above A; B's camera, tint and surface are not affected by A's dive.
  *Partly automated (`dive.mp.diver`); by hand: A second player watching: the diver goes down and up in the
  treading-water pose with the ripple on the surface above; the watcher's camera, tint and surface are not affected;
  the same with a watcher who has no mod.*
- [x] **M04 Other network version and turned off:** a player with the mod turned off (or an older version with another
  network version) joins, then a player turns it off while connected. Expected: both refused about a second later
  ("Incompatible version").
  *Automated: `probe.mp.off.MC.Exploration.Swimming.Dive`.*
- [x] **M05 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows Swim Dive
  inactive because the server does not have it; you swim normally, Crouch does nothing.
  *Automated: `probe.mp.baseline`, `dive.mp.server-missing`.*
- [ ] **M06 Two divers:** A and B dive at the same time at the same spot. Expected: each dives with their own keys,
  each sees the other move up and down; each only has their own tint.
- [ ] **M07 Host dives:** the host (not a dedicated server) dives. Expected: host uses its own settings; a connected
  player uses the host's.
  *Partly automated (`dive.rules`, `dive.mp.rules`); by hand: A real host (not a dedicated server) with a joined
  player: the host dives by its own settings and the player by the host's.*
- [ ] **M08 Creature from another game:** B's game owns a Serpent (B came first); A dives 6 m below it. Expected: as
  in T31 it cannot reach A.
- [x] **M09 Server turns the mod off while a player is under water:** a player dives 3 m; the server owner (or the
  host) sets `Enabled = false` for Swim Dive, then `true` again. Expected: the player's Swim Dive goes inactive ("the
  server has this mod turned off"), the normal game lifts them to the surface, camera, tint and surface are back to
  normal and Crouch does nothing; when the server turns it on again the player's log shows "Using the server's rules:
  ..." and Crouch dives again. Nobody is disconnected.
  *Automated: `dive.mp.server-off`.*

## 0.1.0 — other mods

- [ ] **X01 Weapon Moveset (MC):** dive next to the sea floor and tap Jump, then attack. Expected: no jump attack
  (under water Jump is never a jump); after swimming ashore, jump attacks work as usual.
  *Partly automated (`dive.moveset`, `dive.ceiling`); by hand: Dive next to the sea floor, tap Jump, then attack: no
  jump attack; after swimming ashore jump attacks work as usual. Worth one extra try: jump from the shore into the
  water, dive at once and attack where you touch the floor.*
- [ ] **X02 Dual Wielding (MC):** with a pair equipped, swim and dive. Expected: both weapons are put away while
  swimming and while you hover above the sea floor; where you touch a slope or a rock of the floor, the hide key shows
  the pair as it would a single weapon in the normal game; rising puts them away again. No error.
  *Partly automated (`dive.floor`); by hand: Where you touch a slope or rock the hide key shows the pair; rising puts
  both away again; no error from Dual Wielding in the log.*
- [ ] **X03 Sneak Ambush (MC):** hold Crouch at the surface: you dive, you never sneak (no stealth icon). Under water
  the tint gives no fog stealth icon (its fog comes from the weather).
  *Partly automated (`dive.keys`, `dive.view`); by hand: On screen: no stealth icon while holding Crouch in water and
  no fog stealth icon from the under-water tint.*
- [ ] **X04 Other dive mods (optional):** install BetterDiving (or Dive In). Expected: Status "Inactive: BetterDiving
  also handles diving. Remove one of them.", the log warns once, and only the other mod dives.
  *Partly automated (`dive.exits`, `dive.logic`); by hand: A real BetterDiving (or Dive In) install: only that mod
  dives and nothing fights.*
- [ ] **X05 Underwater by Crystal (optional):** both installed. Expected: both load; with its walk-on-the-floor mode on
  you walk on the sea floor and Crouch does nothing; its camera under water gets this mod's tint and surface.
  *Partly automated (`dive.logic`); by hand: Everything with Underwater by Crystal installed: both load; with its
  floor-walk mode you walk and Crouch does nothing; its camera under water gets this mod's tint and surface.*
- [ ] **X06 ImpactfulSkills (optional):** both installed, `raiseskill Swim 50` (or more). Expected: the log says it is
  installed; diving works; the drain under water matches its reduced swim drain; the dive speed stays the normal swim
  speed (its bonus speeds up surface swimming only); Jump under water never jumps.
  *Partly automated (`dive.logic`); by hand: Everything with ImpactfulSkills installed: the log line, diving works,
  the drain follows its reduced swim drain, the dive speed stays the normal swim speed, Jump under water never jumps.*
