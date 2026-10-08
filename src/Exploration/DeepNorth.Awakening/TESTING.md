# Deep North Awakening — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-10-01 (loads, patches cleanly, JitCheck clean). In-world self-tests
(`./tools/Test-InWorld.ps1 -Mod DeepNorth.Awakening -Only dn.`) passed 2026-10-01 on the final code: `dn.logic`,
`dn.network`, `dn.vanilla`, `dn.hostility` (a Krigen and a Gammeltroll target each other), `dn.stones` (stones 1-4,
held-back requests), `dn.detect`, `dn.kall` (server engagement kept near a player and released far; kill count with
deaths made without creatures: half = weakening news, target = cleared and written, nature and outside kills not
counted, progress and cleared read back), `dn.area` (stage 3 in the Deep North: 10-12 tagged area Jotun with stars,
blizzard, meteors, a nature band; then Kall killed there: no refill; entered again: one burst, no refill past the local
hold, its own Krigen back despite 3 other Krigen near; kills there counted; a band after Kall; the target reached:
cleared, "The Jotun Retreat" shown, no storm, no new Jotun, gone from the map), `dn.map` (map scanned; awake area purple, sleeping one not; own colours back with ShowAreas off, at stage 0 and
while waiting for the server's rules) and `dn.morkhalla`. No hands-on in-game test yet.

**Automated checks (2026-10-08):** 28 of 30 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive, `killall` removes nearby creatures, `goto <x> <z>` (whole numbers) teleports you (the Deep North is
the far north: try `goto 0 9000`, or any land north of z = 8000 on the map), `tod 0.5` noon, `tod -1` normal time.
`deepnorth_stones` shows the count of broken Malicious Ice, the stage and the awake areas; `deepnorth_stones <n>` sets
the count without messages or invasions. `spawn BlackIce_Start` places a Malicious Ice in front of you (break it with
a pickaxe, an axe or fire); a real one sits at the bottom of a Mörkhalla. `pevents list` lists the running Jotun
invasions. `listkeys` shows the world keys (`mc_dn_stones <n>`). `setkey defeated_frozenking_p3` marks Kall as
defeated, `removekey defeated_frozenking_p3` undoes it. `spawn JotunWarrior`, `spawn TrollFrost`, `spawn Barka`,
`spawn Moose`.

**Names checked:** `BlackIce_Start` (the Mörkhalla Malicious Ice), `jotun_invasion` (the event), `JotunWarrior`,
`JotunWarriorDualWield`, `JotunWitch`, `Elaking`, `TrollFrost`, `Barka`, `Greydwarf_Frozen`, `Greydwarf_Shaman_Frozen`,
`Moose`, `projectile_FimbulvinterMeteor`, `MorkBorg` and the key `defeated_frozenking_p3` were read in the 1.0.16 game
data (asset bundles, 2026-10-01) and confirmed by `dn.vanilla` at runtime (1.0.16), with the Deep North weathers
`Twilight_SnowStorm` (weight 0.5), `Twilight_Snow` and `Twilight_Clear`, and one Malicious Ice per Mörkhalla
(`dn.morkhalla`); the console commands are in the game code (`Terminal`). Measured in the probe world: the Deep North
sector has no level-up multiplier (effective Jotun star chance = the setting with default world modifiers).

Most expected results name log lines: follow the log with `./tools/Watch-Log.ps1 -Mine` (Debug lines need
`./tools/Setup.ps1 -DevBepInExConfig`). Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Exploration.DeepNorth.Awakening.cfg`; default settings unless an item says otherwise, and put each
changed one back afterwards. Use a new world for T01-T05 (or `deepnorth_stones 0` first).

## 0.1.0 — single player

- [x] **T01 Dormant north (G1):** new world, `deepnorth_stones`, then `goto 0 9000` and stay 5 minutes. Expected:
  "0 Malicious Ice broken, stage 0"; the Deep North is as quiet as the normal game (no Krigen, Hexen or Elaking
  groups appearing, no blizzard from the mod).
  *Automated: `dn.dormant`.*
- [x] **T02 First Malicious Ice (G2):** `spawn BlackIce_Start`, break it. Expected: the normal centre message "The Jotun
  Advance" (once); log "A Malicious Ice was broken at ...: 1 broken in this world, Deep North stage 1."; `pevents list`
  shows no new invasion; `listkeys` shows `mc_dn_stones 1`; `deepnorth_stones` shows about 20 % of the areas awake.
  *Automated: `dn.breaks`.*
- [ ] **T03 Invaded areas (G2):** after T02, travel through the Deep North (`goto`, then walk or fly a few hundred
  metres). Expected: in some places Krigen (both kinds), Hexen and Elaking appear around you (up to about a dozen), day
  and night; in others the north stays quiet. Going back to a quiet place, it stays quiet; an invaded place stays
  invaded after leaving and loading the world again.
  *Partly automated (`dn.stage1`, `dn.dormant`); by hand: Leave an invaded place and come back, and quit and load the
  world again: the same places are still invaded and the quiet ones still quiet (the test visits each place once and
  does not reload the world).*
- [x] **T04 Second Malicious Ice (G3):** break another. Expected: "The Jotun Advance"; still no new invasion in
  `pevents list`; `deepnorth_stones` about 40 % awake; the invaded places of T03 are still invaded.
  Over about 20 area Jotun, more 1-star ones than at stage 1 (roughly one in five).
  *Automated: `dn.breaks`, `dn.stars`.*
- [x] **T05 Third Malicious Ice (G4):** break a third. Expected: "The Jotun Advance"; `pevents list` shows 3 `jotun_invasion`, the map shows 3 invasion markers outside the Deep North; log "Started 3 Jotun
  invasion(s) in the world."; about 70 % of the areas awake; 2-star Krigen show up (roughly one in five).
  *Automated: `dn.breaks`, `dn.stars`.*
- [x] **T06 Later stones (G5):** with fewer than 3 invasions running (`pevents stop jotun_invasion` near one, or a fresh
  stage-3 world with one stopped), break a fourth. Expected: "The Jotun Advance"; one more invasion. With 3 running,
  break another: the message shows, no fourth invasion (log "Started 0 of 1 ...").
  *Automated: `dn.breaks`.*
- [x] **T07 Hidden areas (G6):** stand in an invaded area, open the map. Expected: no circle, no pin, no marker for the
  area (only the purple tint of T22 where explored); there is no Malicious Ice to break in it; it never ends by itself.
  *Automated: `dn.stage1`.*
- [x] **T08 Stars and world modifiers (G7):** at stage 3, set the world modifier "Enemy level-up rate" (or World
  level) higher, travel to an area you have not visited. Expected: clearly more starred Jotun than with the default
  modifiers. Hexen and Elaking never above 1 star.
  *Automated: `dn.stars`.*
- [ ] **T09 Storms (G8):** set StormShare = 100, stand in an invaded area. Expected: the Deep North blizzard within a
  few seconds; meteors fall around you (never within about 40 m) and can hurt you. Leave the area: normal weather comes
  back. Walk south out of the Deep North next to an invaded area at its edge: the blizzard stops at the Deep North
  line. StormShare = 0: never a blizzard from the mod. Default (33): over an hour, an area storms now and then for 5-10
  minutes.
  *Partly automated (`dn.storm`, `dn.dormant`); by hand: With god mode off, let a meteor land on you: it hurts. Walk
  out of a storming area: normal weather comes back. The look and sound of the blizzard.*
- [x] **T10 Nature fights back (G9):** at stage 0, `spawn JotunWarrior` and `spawn TrollFrost` next to each other (stand
  well away): they ignore each other. At stage 1 or more (`deepnorth_stones 1`): they fight. A `Moose` stays out of it.
  NatureFightsBack = false: they ignore each other again.
  *Automated: `dn.hostility`.*
- [x] **T11 Nature bands (G9):** set NatureBandChance = 100 and walk into invaded areas you have not visited. Expected:
  a band appears 40-80 m away: 10 to 20 frost Greydwarfs (2 to 4 shamans) and 1 to 3 Gammeltroll or Barka, together;
  they attack the Jotun. Never a second band while one is still around you. Default (10): now and then only. After Kall
  (`setkey defeated_frozenking_p3`), bands still come in areas that are not cleared, never in a cleared one.
  *Automated: `dn.bands`.*
- [ ] **T12 After Kall, cleared areas (G10):** stage 3, stand in an invaded area with Jotun around,
  `setkey defeated_frozenking_p3`. Expected: log "Kall is defeated: ... around the players wait to be defeated ...";
  while you stay, no new Jotun appear (wait a few minutes). `deepnorth_stones` shows "0 of N Jotun defeated" (N between
  8 and 13). Kill Jotun inside the area (sword, or `killall` for the ones near): `deepnorth_stones` counts them; at half
  of N a small "The Jotun army is weakening" top left; at N "The Jotun Retreat", log "... the area is cleared for good".
  Jotun still alive stay; nothing new appears; with StormShare = 100 the blizzard stops there; Gammeltroll, Barka and
  Greydwarf kills never count. Leave (`goto` far away), come back, and save,
  quit and load the world again: no new Jotun, no blizzard, no purple on the map there. `deepnorth_stones` there says
  the area is cleared.
  `removekey defeated_frozenking_p3` undoes Kall (test world only): the area spawns and storms again as before Kall;
  its cleared mark stays in the world and applies again if Kall is defeated again.
  *Partly automated (`dn.kallfight`, `dn.kallrestart`, `dn.area`); by hand: One real trip far away and back, and one
  real save, quit and load: the cleared area is still quiet and off the map (the test makes the mod forget its lists
  instead of restarting the game, waits 4 s instead of a few minutes, and checks 'as before Kall' by the weather and
  the spawn gate, not by waiting for new Jotun).*
- [ ] **T20 After Kall, areas not cleared (G10):** after Kall, kill fewer Jotun of an invaded area than it needs, then
  go more than 800 m away (`goto`), wait a few seconds and walk back into the area. Expected: as you step into it, its
  Jotun are back up to their usual numbers (the survivors around count), nothing more appears while you stay, and
  `deepnorth_stones` still shows the kills made before. Walk out to its edge
  and back in without going far: nothing new. Die there with your bed inside the area and respawn: nothing new. An
  invaded area nobody visited before Kall has its Jotun as soon as you walk into it, also one right next to where you
  stood when Kall fell.
  *Partly automated (`dn.kallreturn`, `dn.kallrestart`, `dn.area`); by hand: Walk to the edge of the area and back in
  without going far, and die with your bed inside the area and respawn: nothing new appears. Walk into the area right
  next to where you stood when Kall fell: it has its Jotun.*
- [ ] **T21 After Kall, restart (G10):** after Kall, kill some Jotun of an area that is not cleared, save and quit while
  standing in it, load the world again. Expected: its Jotun are topped up right after you arrive (the "waiting to be
  defeated" list is not saved), `deepnorth_stones` still shows the kills made before; a cleared area stays cleared.
  *Partly automated (`dn.kallrestart`); by hand: A real save, quit and load while standing in the area: topped up on
  arrival, kills kept, a cleared area still cleared.*
- [x] **T23 Kills from elsewhere (G10):** after Kall, lure Jotun of one area into a neighbouring invaded area and kill
  them there. Expected: `deepnorth_stones` in the neighbouring area counts them (kills count where they happen), the
  first area's count does not move.
  *Automated: `dn.kallfight`.*
- [!] **T24 Kill target lowered (G10):** after Kall, set ClearKillsMin = ClearKillsMax = 12 and kill 9 Jotun in an
  invaded area (`deepnorth_stones` shows "9 of 12 Jotun defeated"). Then set ClearKillsMin = ClearKillsMax = 8.
  Expected: within a couple of seconds, without another kill, the area is cleared for good like in T12
  (`deepnorth_stones` there says the area is cleared), and `deepnorth_stones` never shows more kills than the area
  needs (no "9 of 8"). The same when the lower values are set while the game is closed: after loading the world the
  area is cleared at once.
  **FAILED:** automated test: Lowering ClearKillsMin/Max to or below an area's kill count does not clear the area
  (shows '9 of 8 Jotun defeated') (self-test `dn.bug.killtarget`)
- [!] **T25 Area numbers (G2):** at stage 1 or more, walk into several invaded places you have not visited and count
  the Jotun that appear around you each time (JotunDensity = 100). Expected: never more than 3 Krigen, 2 dual-axe
  Krigen, 2 Hexen and 6 Elaking at once, the numbers the README gives. The same after Kall when you walk into an area
  that is not cleared.
  **FAILED:** automated test: An area can hold more Jotun than the README and design say: 4 Krigen instead of 3, up to
  8 Elaking instead of 6 (self-test `dn.bug.caps`)
- [x] **T13 Old world (G11):** a world (copy) where Malicious Ice were broken before installing the mod. Expected: log
  "Counted the broken Malicious Ice of this world: N explored Morkhalla, K with its Malicious Ice already broken. Deep
  North stage S."; no centre message, no new invasion; `deepnorth_stones` shows K.
  *Automated: `dn.oldworld`.*
- [x] **T14 Admin command (E1):** `deepnorth_stones`, then `deepnorth_stones 2`, then `deepnorth_stones 0`. Expected: it
  shows and sets the count and stage; no message, no invasion.
  *Automated: `dn.command`.*
- [x] **T15 Admin invasion (E3):** `pevents start jotun_invasion` (with fewer than 3 running), also right after
  breaking a Malicious Ice. Expected: an invasion starts about 5 seconds later (`pevents list`) both times.
  *Automated: `dn.request`, `dn.stones`.*
- [ ] **T22 Areas on the map (G13):** stage 3, open the map (M) over the Deep North. Expected: no purple where the
  map is not explored. Walk or fly through the Deep North: as the map gets explored, invaded areas show purple on the
  land (not on the sea), with a darker border; touching areas form one region; the minimap shows the same around you.
  `deepnorth_stones 1`: fewer purple areas; `deepnorth_stones 0`: none. ShowAreas = false: the purple goes within a
  second; true: it comes back. After Kall, clear an area (T12): it disappears from the map. Untick the mod in the MC Mods
  panel: normal map colours at once.
  *Partly automated (`dn.map`, `dn.mapdetail`, `dn.area`, `dn.toggle`, `dn.kallfight`); by hand: Open the map once:
  the purple shows only where the map is explored, and looks right on the large map and the minimap (the test reads
  the map texture, not what is drawn on screen).*
- [x] **T19 Key removed mid-game (G11):** at stage 2 with a Mörkhalla explored and its Malicious Ice broken, `removekey
  mc_dn_stones`. Expected: within a second the log "The global key mc_dn_stones was removed: counting the broken
  Malicious Ice again." and "Counted the broken Malicious Ice of this world: ..."; `deepnorth_stones` shows the count
  of explored Mörkhalla without their Malicious Ice (spawned test stones are not counted). Break another: the count goes
  on from there.
  *Automated: `dn.oldworld`.*
- [x] **T16 Live toggle:** at stage 3 in an invaded area with StormShare = 100, untick the mod in the MC Mods panel.
  Expected: within a few seconds normal weather; no new area Jotun; Krigen and a Gammeltroll ignore each other;
  breaking a Malicious Ice shows "The Jotun Advance" and starts one invasion; `listkeys` still shows `mc_dn_stones`.
  Tick it again: the blizzard and the area spawns come back, the count goes on from where it was.
  *Automated: `dn.toggle`.*
- [ ] **T17 `Enabled = false` + restart:** the game starts with no error; the Deep North is the normal game; the MC Mods
  panel shows the mod off.
- [ ] **T18 Clean log:** after T01-T15, no error or exception from the mod in `BepInEx/LogOutput.log`.
  *Partly automated (`dn.cleanlog`); by hand: After your own hands-on checks, look once at BepInEx/LogOutput.log for
  errors from this mod.*

## 0.1.0 — multiplayer

- [ ] **M01 Shared awakening:** dedicated server and two players with the mod. A player breaks a Malicious Ice.
  Expected: both see "The Jotun Advance" once; the server log counts it; both see the same invaded areas and the same
  storms at the same places.
  *Partly automated (`dn.mp.stone`); by hand: With two players: the second one also sees the banner once, and the same
  invaded areas and storms.*
- [ ] **M02 Refused without the mod:** a player without the mod joins. Expected: refused about a second later
  ("Incompatible version"); the server log names them.
  *Partly automated (`probe.mp.refused`, `probe.mp.off.MC.Exploration.DeepNorth.Awakening`); by hand: Read the server
  log: the line "Refused <player> ..." from Deep North Awakening names the player, about a second after they joined.*
- [ ] **M03 Hand-off, player without the mod (AllowPlayersWithoutMod = true):** that player breaks a Malicious Ice at
  stage 0. Expected: every player, them included, sees "The Jotun Advance" (it may show twice); the server counts it
  (stage 1) and about 5 seconds later `pevents list` (server) shows no new invasion. They see and fight area creatures that players with the mod spawned like any creature; they never
  get the blizzard.
  *Partly automated (`scenario:open-server`, `dn.mp.stone`); by hand: With a real player who does not have the mod:
  they see the banner, fight area creatures spawned by players with the mod like any creature, and never get the
  blizzard.*
- [x] **M04 Server settings:** change CoverageStage1 on the server. Expected: players log "Using the server's rules"
  with the new value; `deepnorth_stones` on a player shows the new share.
  *Automated: `dn.mp.rules`.*
- [ ] **M05 Joining after Kall:** after T12-style Kall on the server with one area cleared, a player joins and goes to
  an invaded area that is not cleared (StormShare = 100 on the server). Expected: Jotun spawn when they arrive and the
  blizzard blows; in the cleared area none of either.
  *Partly automated (`dn.mp.kall`, `dn.mp.cleared`); by hand: One real join after Kall was defeated (the test asks for
  the rules and lists again on the open connection instead of reconnecting).*
- [x] **M06 Admin command remote:** a player runs `deepnorth_stones` (state shown on their own console), then an admin
  player runs `deepnorth_stones 1` and a non-admin player runs `deepnorth_stones 2`. Expected: both see "Sent to the
  server ..."; the admin's count is applied (server log, and `deepnorth_stones` shows 1); the non-admin gets "You are
  not admin" and nothing changes.
  *Automated: `dn.mp.command`.*
- [x] **M07 Turning the mod back on after Kall:** AllowPlayersWithoutMod = true on the server, after Kall with one area
  cleared, a player stands in that cleared area with StormShare = 100 on the server, unticks the mod and ticks it
  again. Expected: still no blizzard there (the area lists come back with the server's rules); in an area not
  cleared the blizzard is there.
  *Automated: `dn.mp.cleared`.*
- [ ] **M08 One area, two players, after Kall:** dedicated server, two players with the mod, Kall defeated. Both walk
  into the same invaded area that is not cleared, from two sides, a few seconds apart. Expected: one set of Jotun (about
  a dozen at most), not two. Player A stays in the area while player B goes more than 800 m away and comes back: no new
  Jotun (the area waits to be defeated while A is near). Both leave more than 800 m, then come back: it is topped up.
  Both kill Jotun there: every kill counts on both players' `deepnorth_stones`; both see "The Jotun army is weakening"
  and "The Jotun Retreat" when they stand in the area.
  *Partly automated (`dn.mp.kall`); by hand: Everything that needs two players: both entering from two sides get one
  set; A staying keeps the area from refilling while B leaves and returns; both leaving and returning tops it up; both
  see the kill count and both messages.*
- [ ] **M09 Map setting from the server:** dedicated server with ShowAreas = false, a player with ShowAreas = true in
  their own config. Expected: no purple on their map. Server ShowAreas = true: purple appears on every player's map
  where they explored, the same areas for both players.
  *Partly automated (`dn.mp.rules`); by hand: With two players: both maps show the same purple areas where each
  explored.*

## 0.1.0 — cross-mod

- [ ] **X01 Creatures Morale:** in an invaded area, fight area Jotun. Expected: they never flee; Gammeltroll and Barka
  fighting them follow Morale's normal rules.
- [ ] **X02 Sneak Ambush:** sneak up on area Jotun. Expected: sneaking works on them like on other creatures (they do
  not hunt you down from far away).
  *Partly automated (`dn.stage1`); by hand: With Sneak Ambush on, sneak up on an area Jotun: sneaking works like on
  any other creature.*
- [x] **X03 Creature Kill and Tame Counts:** kill area Krigen. Expected: counted under Krigen.
  *Automated: `dn.x.kills`.*
