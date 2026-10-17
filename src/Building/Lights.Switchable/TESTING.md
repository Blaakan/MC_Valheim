# Switchable Lights — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.2.1 (the refusal screen names the mod, section "0.2.1"; before that 0.2.0), build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). 0.2.0 changed the server's player check: it now also refuses a player who has the mod turned off or has
another version of it, when they join and about a second after they turn it off while connected (0.1.0 only refused a
player without the mod). M05 and M06 are reworded and stay `[ ]`, M08 to M11 are new; the self-tests ran on 0.2.0 on
2026-10-13 (game 1.0.17, in-world and with a dedicated server, every MC mod loaded): its three in-world tests passed,
and in the each-off scenario the server refuses the player who turns the mod off, while connected and when joining
with it off. History, on 0.1.0: smoke test (`./tools/Test-Smoke.ps1`) passed 2026-09-29: loads, patches cleanly,
JitCheck clean (268 methods, 0 failures). Automated in-world self-tests (`./tools/Test-InWorld.ps1 -Mod
Lights.Switchable`) passed: `lights.torch`, `lights.candle`, `lights.campfire` (includes the cooking-fire rule:
campfires and braziers stay normal fires even when listed; rules wire round trip; player check verdicts). Hands-on
tests on 0.1.0: all 23 items of that version were ticked by hand in the test issue (Blaakan/MC_Valheim#12, closed
2026-10-07). The ticks of the items that 0.2.0 did not change are kept here (T01 to T15, M01 to M04, M07, C01); M05 and
M06 were ticked on 0.1.0 too, and are open again because 0.2.0 changed what they check.

**Automated checks (2026-10-17):** 3 of 3 self-tests named in this list's notes passed in real game runs on Valheim
1.0.20 (`./tools/Test-InWorld.ps1`, `./tools/Test-Multiplayer.ps1` with a dedicated server) on the code of commit
`ec1b7dc`. An item ending in "Automated: ..." is checked in full by the named self-tests and is ticked by them alone;
a "Partly automated" item still needs its by-hand part; "FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`), then
`nocost` to build without materials (it does not affect fire fuel: refuelling always takes the item).
Build with the hammer, or spawn with `spawn <name>`. Names checked in the 1.0.16 game data: `piece_groundtorch_wood`
(Standing wood torch), `piece_groundtorch` (Standing iron torch), `piece_groundtorch_green`,
`piece_groundtorch_blue`, `piece_walltorch` (Sconce), `piece_jackoturnip` (Jack-o-turnip), `piece_snowlantern` (Snow lantern), `Candle_resin` (Resin candle),
`CastleKit_groundtorch_unlit` (unlit castle torch, not in the hammer menu), `fire_pit` (Campfire), `fire_pit_iron`,
`hearth`, `bonfire`, `piece_brazierfloor01` (Standing brazier), `piece_brazierfloor02` (Standing blue-burning
brazier), `piece_brazierceiling01` (Hanging brazier); fuel items `Resin`, `Coal`, `GreydwarfEye`, `Guck`, `Wood`. Their maximum fuel (torches 4 or 6,
castle torch 1, braziers 5, candle 3) and burn time (10000-20000 s per fuel, candle 5000 s) come from the same data. **(unverified)**: that a
dedicated server of 1.0.16, like 1.0.15, never runs lights itself (only the players' games do).

## 0.1.0 — single player

- [x] **T01 Torch switches:** build a Standing wood torch. Expected: it comes on at once, full; its hover shows
  "Standing Wood Torch ( On )" and "[E] Turn off". E: it goes dark, hover "( Off )" and "[E] Turn on". E again: back on.
  Nothing is taken from your inventory at any time (carry some Resin to check).
- [x] **T02 No fuel burns:** switch a torch on, leave it for a long time (for example `skiptime 40000`, then wait a few
  seconds near it). Expected: it is still on and its fuel never goes down (hover stays "( On )").
- [x] **T03 Hold and Shift:** hold E on a torch for 3 seconds. Expected: it switches once, no flicker. Shift+E switches
  it too (once).
- [x] **T04 Every light of the list:** build each light of the Setup list (sconce, the iron, green and blue torches,
  Jack-o-turnip, snow lantern). Expected: each switches with E and never needs fuel.
- [x] **T05 Resin candle:** build a Resin candle, switch it off and on with E. Expected: same as a torch; it never burns
  down. Put one outside, without a roof, in the rain: it goes out as in vanilla, and E does not relight it while it is
  still raining; once the rain stops (or under a roof) E lights it again.
- [x] **T06 Castle torch:** find an unlit standing torch in a castle ruin (or `spawn CastleKit_groundtorch_unlit`).
  Expected: E lights it without Resin; E again puts it out.
- [x] **T07 Fires stay vanilla:** build a Campfire, an iron fire pit, a hearth, a bonfire and the three braziers.
  Expected: their hover still shows Fuel n/max and "[E] Use Wood" (braziers: Coal, the blue one Greydwarf eye); E
  adds one; they burn fuel as in vanilla.
- [x] **T08 No fuel menu on lights:** with Resin in the hotbar, look at a torch and press the hotbar key, then open the
  radial "use item" menu (hold the hotbar use / the controller button) on it. Expected: no fuel is added and the
  radial offers no Resin for lights; on a campfire it still works with Wood.
- [x] **T09 Blocked light:** build a torch right under a low roof beam or partly in the ground so it cannot burn.
  Expected: hover "( On, blocked )"; move it and it burns.
- [x] **T10 Live toggle:** Esc → MC Mods → untick Switchable Lights. Expected: a lit torch now shows the vanilla hover
  (Fuel n/max, "[E] Use Resin") and burns fuel again; an off torch is empty and takes Resin. Tick it again: switching
  works at once, with no restart.
- [x] **T11 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Building.Lights.Switchable.cfg`,
  start the game. Expected: lights behave exactly as vanilla; the MC Mods panel shows it off.
- [x] **T12 Uninstall:** remove the mod with a lit torch and an off torch in the world. Expected: the lit torch is
  full and burns normally; the off torch is empty and takes Resin; a candle switched off can be switched on with E.
- [x] **T13 Clean log:** play a few minutes with the mod (T01-T09) while `./tools/Watch-Log.ps1 -Mine` runs. Expected:
  no error or warning from Switchable Lights.
- [x] **T14 Custom list:** remove `piece_groundtorch_wood` from `Lights` in the config while the game runs.
  Expected, with no restart: a lit wood torch shows the vanilla hover (Fuel n/4, [E] Use Resin) and burns fuel again
  (check with `skiptime`); a wood torch that was switched off (Fuel 0/4) takes Resin and lights. Put it back: it
  switches again. Add `piece_brazierfloor01` and a typo name: the log says the brazier "can be used for cooking: it
  stays a normal fire that burns fuel" and the typo is ignored (at the next world load); the brazier keeps its
  vanilla hover and burns coal.
- [x] **T15 Infinite-fire mod:** with an infinite-fire mod (for example ValheimInfiniteFire) on, look at a torch it keeps
  burning. Expected: this mod leaves it alone (the other mod's behaviour and hover).

## 0.1.0 — multiplayer (needs a second player)

Unless a test says otherwise, both players have the mod turned on and `AllowPlayersWithoutMod` is `false` (default).

- [x] **M01 Both with the mod:** host and friend have the mod. One switches a torch off, the other sees it go dark
  within a second or two, and can switch it on again. Nobody's torches burn fuel (T02 with both players near).
- [x] **M02 Friend without the mod (hand-off):** the host sets `AllowPlayersWithoutMod = true`; the friend has no mod.
  Expected: the friend
  sees the same lights; a light you switched off looks empty to them ("Fuel 0/6"), and when they add Resin it comes on
  for everybody. A light the friend runs (they stand near it alone) burns fuel as usual and can go out; you can switch
  it back on for free. A resin candle works as in vanilla for them (E switches it).
- [x] **M03 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod"; lights behave as vanilla.
- [x] **M04 Host turns it off live:** the host unticks the mod while you play. Expected: your status changes to
  "Inactive: the server has this mod turned off" and lights go back to vanilla for you too; ticking it on again brings
  switching back.
- [ ] **M05 Dedicated server:** install on a dedicated server and on both players. Expected: the behaviour of M01 and
  of M06 to M09; the server log shows the mod loaded, no error.
  *Partly automated (`probe.mp.refused`, `probe.mp.off.MC.Building.Lights.Switchable`); by hand: With two real players
  on a dedicated server: M01 (one switches a torch, the other sees it) and M07 (the server's list reaches the player);
  the server log shows the mod loaded.*
- [ ] **M06 Player without the mod refused:** default settings. A friend without the mod joins. Expected: about a
  second after joining (usually still in the loading screen) their game goes back to the main menu with "Incompatible
  version"; the host's log: `Refused <name> (<id>): does not have the mod. This server requires Switchable Lights on
  every player ...`. The host sets `AllowPlayersWithoutMod = true`: the friend can join and play (host log:
  `<name> (<id>) does not have the mod; AllowPlayersWithoutMod is on, so they may play, ...`). Setting it back to
  `false` while they are online refuses them about a second later; a player with the mod turned on stays.
  (unverified: the menu message is read from the game code, not seen in game yet)
  *Partly automated (`probe.mp.refused`, `scenario:open-server`); by hand: Read the host's log for this mod's own line
  "Refused <name>: does not have the mod ..." (several mods refuse the same player and only the first one writes it);
  set AllowPlayersWithoutMod back to false while the player without the mod is online: refused about a second later, a
  player with the mod stays.*
- [x] **M07 Server list for everyone:** the host removes `piece_walltorch` from its `Lights`; the friend keeps the
  default list. Expected: the friend's log shows `Using the server's light list: ...` once; for both players a sconce
  shows the vanilla hover and burns resin, torches still switch. The host puts the default list back: the friend's log
  shows `Using the server's default light list.` and sconces switch again, no rejoin needed. The friend's own
  config file is unchanged; in a single-player world the friend's own list applies.

## 0.2.0 — multiplayer: players with the mod turned off (needs a second player)

Both players have the mod. Unless a test says otherwise, `AllowPlayersWithoutMod` is `false` (default) on the host.

- [x] **M08 Joins with the mod turned off:** the friend sets `Enabled = false` for Switchable Lights (in
  `BepInEx/config/MC.Building.Lights.Switchable.cfg`, or unticked in the MC Mods panel from the main menu), then joins.
  Expected: about a second after joining (usually still in the loading screen) their game goes back to the main menu
  with "Incompatible version".
  *Automated: `probe.mp.off.MC.Building.Lights.Switchable`.*
- [ ] **M09 Turned off while connected:** the friend joins with the mod on and plays, then unticks Switchable Lights in
  the MC Mods panel (Esc). Expected: about a second later their game goes back to the main menu with "Incompatible
  version"; the host keeps playing. The host's log shows `<name> turned Switchable Lights off on their game.`, then
  `Refused <name> (<id>): has the mod turned off. This server requires Switchable Lights on every player ...`. The
  friend joins again with the mod on, then unticks and ticks it again within a second. Expected: they stay.
  *Partly automated (`probe.mp.off.MC.Building.Lights.Switchable`); by hand: Read the host's log for the two lines
  ("<name> turned Switchable Lights off on their game." and "Refused <name>: has the mod turned off ..."), and untick
  and tick the mod again within a second: the player stays.*
- [ ] **M10 AllowPlayersWithoutMod and a player with the mod turned off:** the host sets `AllowPlayersWithoutMod =
  true`. (a) The friend joins with the mod turned off. Expected: they can play; the host's log shows
  `<name> (<id>) has the mod turned off; AllowPlayersWithoutMod is on, so they may play, ...`. (b) The friend ticks the
  mod on, then unticks it again while connected. Expected: they stay both times (the same host log line about a second
  after the untick); with it off, lights show them the vanilla hover (Fuel n/max) and the lights their game runs burn
  fuel; with it on, E switches lights again. (c) While the friend's mod is off, the host sets `AllowPlayersWithoutMod`
  back to `false`. Expected: the friend is refused about a second later ("Incompatible version").
- [ ] **M11 Another version of the mod (needs a test build):** the friend uses a build of the mod with another
  `ModNetworkVersion` than the host's. Expected: refused about a second after joining ("Incompatible version"); the
  host's log: `Refused <name> (<id>): has another version of the mod (network version <n>, the server has <m>). ...`.
  Every build so far has network version 1, so this can only be tried with a build made for the test; skip it
  (`[-]`) until a second network version exists.

## 0.1.0 — compatibility (optional, needs another mod)

- [x] **C01 Batch Station Feeding:** with MC Batch Station Feeding on, look at a torch with Resin in your inventory
  and press Shift+E. Expected: no batch hint on lights; Shift+E switches the light once. On a campfire, Shift+E still
  adds 5 wood.

## 0.2.1 — the refusal screen names the mod

0.2.1 (2026-10-16, the framework's screen fixes 2026-10-17): when a server refuses you because of this mod, the
"Incompatible version" screen lists it under "MC mods:" with the reason and what to do (framework `RefusalNotes`,
`docs/modding/framework.md`, "Who needs to install it, and multiplayer"). This needs at least one MC mod in your game
that is made to run on both the server and the players (any mod a server can refuse you for, or Sleep Through the
Day); the server does not need to have that mod. A game whose MC mods are all client-side shows only "Incompatible
version". When a server refuses you for several MC mods, the screen names the first one that refused you; once that
one is fixed, the next join names the next one. When the server sends no line (a refusal that is not about an MC mod,
or a server with an older version of the MC mods), the screen lists what your own game noticed while connected: an MC
mod turned off while the server has it on, or a server copy whose version cannot play with yours. That may not be why
you were refused. With Jotunn on the server and on your game, Jotunn's window shows the same lines under "Failed
Connection:"; with Jotunn only on your game, the vanilla panel shows them. Run on 2026-10-17 (dedicated test server,
code before the screen fixes): `probe.mp.off.MC.Building.Lights.Switchable` passed, and the screen named this mod, and
only this mod, both times (turned off while in, joining with it off).
Fixed since in the framework: a new connection clears the notes (told by the connection object, not by its hash; in
the same run another mod's screen kept a line of an earlier connection), `probe.mp.off.<GUID>` now fails when the
screen lists any other mod, and the "turned off" line also names F1 for a game with a configuration manager. Not run
yet on the fixed code. `probe.refusal` passed on 2026-10-17 (before its new checks of the connection reset).

- [ ] **M12 Refusal screen names the mod:** on a server with Switchable Lights, turn Switchable Lights off in Esc > MC
  Mods while you are in. Expected: you go back to the menu, and the "Incompatible version" screen lists under "MC
  mods:" only "- Switchable Lights: it is turned off or cannot run in your game, and this server requires it. Turn it
  on in Esc > MC Mods (or F1 when a configuration manager is installed; its status there says what stops it) and join
  again." (with Jotunn on the server and on your game: in Jotunn's window, under "Failed Connection:"; with Jotunn
  only on your game: on the vanilla panel). Join again with it off: the same. Turn it on and join: you stay in. A game
  with a test build of this mod made with another version (for example `-p:ModNetworkVersion=99 -p:Version=0.0.9`;
  with only another network version the line names the same version twice): the line reads "the server has version
  <server's> and your game 0.0.9, and they cannot play together. Install version <server's>." A friend without this
  mod, with Sleep Through the Day (or another MC mod made to run on both the server and the players) and with every
  other MC mod the server needs: the line reads "this server requires it (version <server's>) and your game does not
  have it. Install it."
  *Partly automated (`probe.mp.off.MC.Building.Lights.Switchable`, `probe.refusal`); by hand: The screen as seen, also
  inside Jotunn's window; the test build with another version; the friend without this mod.*
