# Deep North Awakening — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-10-01 (loads, patches cleanly, JitCheck clean). In-world self-tests
(`./tools/Test-InWorld.ps1 -Mod DeepNorth.Awakening -Only dn.`) passed 2026-10-01 on the final code: `dn.logic`,
`dn.network`, `dn.vanilla`, `dn.hostility` (a Krigen and a Gammeltroll target each other), `dn.stones` (stones 1-4,
held-back requests), `dn.detect`, `dn.kall` (server engagement kept near a player and released far, clearing after
the last death, cleared area written and read back), `dn.area` (stage 3 in the Deep North: 10-12 tagged area Jotun
with stars, blizzard, meteors, a nature band; then Kall killed there: no refill; entered again: one burst, no refill
past the local hold, its own Krigen back despite 3 other Krigen near; all killed: cleared, no storm, gone from the
map), `dn.map` (map scanned; awake area purple, sleeping one not; own colours back with ShowAreas off, at stage 0 and
while waiting for the server's rules) and `dn.morkhalla`. No hands-on in-game test yet.

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

- [ ] **T01 Dormant north (G1):** new world, `deepnorth_stones`, then `goto 0 9000` and stay 5 minutes. Expected:
  "0 Malicious Ice broken, stage 0"; the Deep North is as quiet as the normal game (no Krigen, Hexen or Elaking
  groups appearing, no blizzard from the mod).
- [ ] **T02 First Malicious Ice (G2):** `spawn BlackIce_Start`, break it. Expected: the normal centre message "The Jotun
  Advance" (once); log "A Malicious Ice was broken at ...: 1 broken in this world, Deep North stage 1."; `pevents list`
  shows no new invasion; `listkeys` shows `mc_dn_stones 1`; `deepnorth_stones` shows about 20 % of the areas awake.
- [ ] **T03 Invaded areas (G2):** after T02, travel through the Deep North (`goto`, then walk or fly a few hundred
  metres). Expected: in some places Krigen (both kinds), Hexen and Elaking appear around you (up to about a dozen), day
  and night; in others the north stays quiet. Going back to a quiet place, it stays quiet; an invaded place stays
  invaded after leaving and loading the world again.
- [ ] **T04 Second Malicious Ice (G3):** break another. Expected: "The Jotun Advance"; still no new invasion in
  `pevents list`; `deepnorth_stones` about 40 % awake; the invaded places of T03 are still invaded.
  Over about 20 area Jotun, more 1-star ones than at stage 1 (roughly one in five).
- [ ] **T05 Third Malicious Ice (G4):** break a third. Expected: "The Jotun Advance"; `pevents list` shows 3 `jotun_invasion`, the map shows 3 invasion markers outside the Deep North; log "Started 3 Jotun
  invasion(s) in the world."; about 70 % of the areas awake; 2-star Krigen show up (roughly one in five).
- [ ] **T06 Later stones (G5):** with fewer than 3 invasions running (`pevents stop jotun_invasion` near one, or a fresh
  stage-3 world with one stopped), break a fourth. Expected: "The Jotun Advance"; one more invasion. With 3 running,
  break another: the message shows, no fourth invasion (log "Started 0 of 1 ...").
- [ ] **T07 Hidden areas (G6):** stand in an invaded area, open the map. Expected: no circle, no pin, no marker for the
  area (only the purple tint of T22 where explored); there is no Malicious Ice to break in it; it never ends by itself.
- [ ] **T08 Stars and world modifiers (G7):** at stage 3, set the world modifier "Enemy level-up rate" (or World
  level) higher, travel to an area you have not visited. Expected: clearly more starred Jotun than with the default
  modifiers. Hexen and Elaking never above 1 star.
- [ ] **T09 Storms (G8):** set StormShare = 100, stand in an invaded area. Expected: the Deep North blizzard within a
  few seconds; meteors fall around you (never within about 40 m) and can hurt you. Leave the area: normal weather comes
  back. Walk south out of the Deep North next to an invaded area at its edge: the blizzard stops at the Deep North
  line. StormShare = 0: never a blizzard from the mod. Default (33): over an hour, an area storms now and then for 5-10
  minutes.
- [ ] **T10 Nature fights back (G9):** at stage 0, `spawn JotunWarrior` and `spawn TrollFrost` next to each other (stand
  well away): they ignore each other. At stage 1 or more (`deepnorth_stones 1`): they fight. A `Moose` stays out of it.
  NatureFightsBack = false: they ignore each other again.
- [ ] **T11 Nature bands (G9):** set NatureBandChance = 100 and walk into invaded areas you have not visited. Expected:
  a band appears 40-80 m away: 5 to 10 frost Greydwarfs (1 or 2 shamans) and 0 to 2 Gammeltroll or Barka, together;
  they attack the Jotun. Never a second band while one is still around you. Default (10): now and then only.
- [ ] **T12 After Kall, cleared areas (G10):** stage 3, `goto` to an invaded area you have not visited, wait for its
  Jotun without walking around (Jotun of the area left in unloaded places must die too), `setkey defeated_frozenking_p3`. Expected: log "Kall is defeated: ... area(s) around the players wait to be
  defeated ..."; while you stay, no new Jotun appear (wait a few minutes); wait 30 s, then kill every Jotun around (`killall` works): "The Jotun Retreat", log "... the area is
  cleared for good", and with StormShare = 100 the blizzard stops there. Leave (`goto` far away), come back, and save,
  quit and load the world again: no Jotun, no blizzard there. `deepnorth_stones` there says the area is cleared.
  `removekey defeated_frozenking_p3` undoes Kall (test world only): the area spawns and storms again as before Kall;
  its cleared mark stays in the world and applies again if Kall is defeated again.
- [ ] **T20 After Kall, areas not cleared (G10):** after Kall, kill only some Jotun of an invaded area, then go more
  than 800 m away (`goto`), wait a few seconds and walk back into the area from another side. Expected: as you step
  into it, its Jotun are back up to their usual numbers (the survivors count, even far ones: never a second set;
  `deepnorth_stones` shows its living Jotun per kind), and nothing more appears while you stay. Walk out to its edge
  and back in without going far: nothing new. Die there with your bed inside the area and respawn: nothing new. An
  invaded area nobody visited before Kall has its Jotun as soon as you walk into it, also one right next to where you
  stood when Kall fell.
- [ ] **T21 After Kall, restart (G10):** after Kall, kill some Jotun of an area that is not cleared, save and quit while
  standing in it, load the world again. Expected: its Jotun are topped up right after you arrive (the "waiting to be
  defeated" list is not saved); a cleared area stays cleared.
- [ ] **T13 Old world (G11):** a world (copy) where Malicious Ice were broken before installing the mod. Expected: log
  "Counted the broken Malicious Ice of this world: N explored Morkhalla, K with its Malicious Ice already broken. Deep
  North stage S."; no centre message, no new invasion; `deepnorth_stones` shows K.
- [ ] **T14 Admin command (E1):** `deepnorth_stones`, then `deepnorth_stones 2`, then `deepnorth_stones 0`. Expected: it
  shows and sets the count and stage; no message, no invasion.
- [ ] **T15 Admin invasion (E3):** `pevents start jotun_invasion` (with fewer than 3 running), also right after
  breaking a Malicious Ice. Expected: an invasion starts about 5 seconds later (`pevents list`) both times.
- [ ] **T22 Areas on the map (G13):** stage 3, open the map (M) over the Deep North. Expected: no purple where the
  map is not explored. Walk or fly through the Deep North: as the map gets explored, invaded areas show purple on the
  land (not on the sea), with a darker border; touching areas form one region; the minimap shows the same around you.
  `deepnorth_stones 1`: fewer purple areas; `deepnorth_stones 0`: none. ShowAreas = false: the purple goes within a
  second; true: it comes back. After Kall, clear an area (T12): it disappears from the map. Untick the mod in the MC Mods
  panel: normal map colours at once.
- [ ] **T19 Key removed mid-game (G11):** at stage 2 with a Mörkhalla explored and its Malicious Ice broken, `removekey
  mc_dn_stones`. Expected: within a second the log "The global key mc_dn_stones was removed: counting the broken
  Malicious Ice again." and "Counted the broken Malicious Ice of this world: ..."; `deepnorth_stones` shows the count
  of explored Mörkhalla without their Malicious Ice (spawned test stones are not counted). Break another: the count goes
  on from there.
- [ ] **T16 Live toggle:** at stage 3 in an invaded area with StormShare = 100, untick the mod in the MC Mods panel.
  Expected: within a few seconds normal weather; no new area Jotun; Krigen and a Gammeltroll ignore each other;
  breaking a Malicious Ice shows "The Jotun Advance" and starts one invasion; `listkeys` still shows `mc_dn_stones`.
  Tick it again: the blizzard and the area spawns come back, the count goes on from where it was.
- [ ] **T17 `Enabled = false` + restart:** the game starts with no error; the Deep North is the normal game; the MC Mods
  panel shows the mod off.
- [ ] **T18 Clean log:** after T01-T15, no error or exception from the mod in `BepInEx/LogOutput.log`.

## 0.1.0 — multiplayer

- [ ] **M01 Shared awakening:** dedicated server and two players with the mod. A player breaks a Malicious Ice.
  Expected: both see "The Jotun Advance" once; the server log counts it; both see the same invaded areas and the same
  storms at the same places.
- [ ] **M02 Refused without the mod:** a player without the mod joins. Expected: refused about a second later
  ("Incompatible version"); the server log names them.
- [ ] **M03 Hand-off, player without the mod (AllowPlayersWithoutMod = true):** that player breaks a Malicious Ice at
  stage 0. Expected: every player, them included, sees "The Jotun Advance" (it may show twice); the server counts it
  (stage 1) and about 5 seconds later `pevents list` (server) shows no new invasion. They see and fight area creatures that players with the mod spawned like any creature; they never
  get the blizzard.
- [ ] **M04 Server settings:** change CoverageStage1 on the server. Expected: players log "Using the server's rules"
  with the new value; `deepnorth_stones` on a player shows the new share.
- [ ] **M05 Joining after Kall:** after T12-style Kall on the server with one area cleared, a player joins and goes to
  an invaded area that is not cleared (StormShare = 100 on the server). Expected: Jotun spawn when they arrive and the
  blizzard blows; in the cleared area none of either.
- [ ] **M06 Admin command remote:** a player runs `deepnorth_stones` (state shown on their own console), then an admin
  player runs `deepnorth_stones 1` and a non-admin player runs `deepnorth_stones 2`. Expected: both see "Sent to the
  server ..."; the admin's count is applied (server log, and `deepnorth_stones` shows 1); the non-admin gets "You are
  not admin" and nothing changes.
- [ ] **M07 Turning the mod back on after Kall:** AllowPlayersWithoutMod = true on the server, after Kall with one area
  cleared, a player stands in that cleared area with StormShare = 100 on the server, unticks the mod and ticks it
  again. Expected: still no blizzard there (the area lists come back with the server's rules); in an area not
  cleared the blizzard is there.
- [ ] **M08 One area, two players, after Kall:** dedicated server, two players with the mod, Kall defeated. Both walk
  into the same invaded area that is not cleared, from two sides, a few seconds apart. Expected: one set of Jotun (about
  a dozen at most), not two. Player A stays in the area while player B goes more than 800 m away and comes back: no new
  Jotun (the area waits to be defeated while A is near). Both leave more than 800 m, then come back: it is topped up.
- [ ] **M09 Map setting from the server:** dedicated server with ShowAreas = false, a player with ShowAreas = true in
  their own config. Expected: no purple on their map. Server ShowAreas = true: purple appears on every player's map
  where they explored, the same areas for both players.

## 0.1.0 — cross-mod

- [ ] **X01 Creatures Morale:** in an invaded area, fight area Jotun. Expected: they never flee; Gammeltroll and Barka
  fighting them follow Morale's normal rules.
- [ ] **X02 Sneak Ambush:** sneak up on area Jotun. Expected: sneaking works on them like on other creatures (they do
  not hunt you down from far away).
- [ ] **X03 Creature Kill and Tame Counts:** kill area Krigen. Expected: counted under Krigen.
