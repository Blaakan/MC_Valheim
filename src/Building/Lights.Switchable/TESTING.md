# Switchable Lights — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). Smoke test (`./tools/Test-Smoke.ps1`) passed 2026-09-29: loads, patches cleanly, JitCheck clean (268
methods, 0 failures). Automated in-world self-tests (`./tools/Test-InWorld.ps1 -Mod Lights.Switchable`) pass:
`lights.torch`, `lights.candle`, `lights.campfire` (includes the cooking-fire rule: campfires and braziers stay
normal fires even when listed; rules wire round trip; player check verdicts). No hands-on in-game test yet.

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

- [ ] **T01 Torch switches:** build a Standing wood torch. Expected: it comes on at once, full; its hover shows
  "Standing Wood Torch ( On )" and "[E] Turn off". E: it goes dark, hover "( Off )" and "[E] Turn on". E again: back on.
  Nothing is taken from your inventory at any time (carry some Resin to check).
- [ ] **T02 No fuel burns:** switch a torch on, leave it for a long time (for example `skiptime 40000`, then wait a few
  seconds near it). Expected: it is still on and its fuel never goes down (hover stays "( On )").
- [ ] **T03 Hold and Shift:** hold E on a torch for 3 seconds. Expected: it switches once, no flicker. Shift+E switches
  it too (once).
- [ ] **T04 Every light of the list:** build each light of the Setup list (sconce, the iron, green and blue torches,
  Jack-o-turnip, snow lantern). Expected: each switches with E and never needs fuel.
- [ ] **T05 Resin candle:** build a Resin candle, switch it off and on with E. Expected: same as a torch; it never burns
  down. Put one outside, without a roof, in the rain: it goes out as in vanilla, and E does not relight it while it is
  still raining; once the rain stops (or under a roof) E lights it again.
- [ ] **T06 Castle torch:** find an unlit standing torch in a castle ruin (or `spawn CastleKit_groundtorch_unlit`).
  Expected: E lights it without Resin; E again puts it out.
- [ ] **T07 Fires stay vanilla:** build a Campfire, an iron fire pit, a hearth, a bonfire and the three braziers.
  Expected: their hover still shows Fuel n/max and "[E] Use Wood" (braziers: Coal, the blue one Greydwarf eye); E
  adds one; they burn fuel as in vanilla.
- [ ] **T08 No fuel menu on lights:** with Resin in the hotbar, look at a torch and press the hotbar key, then open the
  radial "use item" menu (hold the hotbar use / the controller button) on it. Expected: no fuel is added and the
  radial offers no Resin for lights; on a campfire it still works with Wood.
- [ ] **T09 Blocked light:** build a torch right under a low roof beam or partly in the ground so it cannot burn.
  Expected: hover "( On, blocked )"; move it and it burns.
- [ ] **T10 Live toggle:** Esc → MC Mods → untick Switchable Lights. Expected: a lit torch now shows the vanilla hover
  (Fuel n/max, "[E] Use Resin") and burns fuel again; an off torch is empty and takes Resin. Tick it again: switching
  works at once, with no restart.
- [ ] **T11 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Building.Lights.Switchable.cfg`,
  start the game. Expected: lights behave exactly as vanilla; the MC Mods panel shows it off.
- [ ] **T12 Uninstall:** remove the mod with a lit torch and an off torch in the world. Expected: the lit torch is
  full and burns normally; the off torch is empty and takes Resin; a candle switched off can be switched on with E.
- [ ] **T13 Clean log:** play a few minutes with the mod (T01-T09) while `./tools/Watch-Log.ps1 -Mine` runs. Expected:
  no error or warning from Switchable Lights.
- [ ] **T14 Custom list:** remove `piece_groundtorch_wood` from `Lights` in the config while the game runs.
  Expected, with no restart: a lit wood torch shows the vanilla hover (Fuel n/4, [E] Use Resin) and burns fuel again
  (check with `skiptime`); a wood torch that was switched off (Fuel 0/4) takes Resin and lights. Put it back: it
  switches again. Add `piece_brazierfloor01` and a typo name: the log says the brazier "can be used for cooking: it
  stays a normal fire that burns fuel" and the typo is ignored (at the next world load); the brazier keeps its
  vanilla hover and burns coal.
- [ ] **T15 Infinite-fire mod:** with an infinite-fire mod (for example ValheimInfiniteFire) on, look at a torch it keeps
  burning. Expected: this mod leaves it alone (the other mod's behaviour and hover).

## 0.1.0 — multiplayer (needs a second player)

Unless a test says otherwise, both players have the mod and `AllowPlayersWithoutMod` is `false` (default).

- [ ] **M01 Both with the mod:** host and friend have the mod. One switches a torch off, the other sees it go dark
  within a second or two, and can switch it on again. Nobody's torches burn fuel (T02 with both players near).
- [ ] **M02 Friend without the mod (hand-off):** the host sets `AllowPlayersWithoutMod = true`; the friend has no mod.
  Expected: the friend
  sees the same lights; a light you switched off looks empty to them ("Fuel 0/6"), and when they add Resin it comes on
  for everybody. A light the friend runs (they stand near it alone) burns fuel as usual and can go out; you can switch
  it back on for free. A resin candle works as in vanilla for them (E switches it).
- [ ] **M03 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod"; lights behave as vanilla.
- [ ] **M04 Host turns it off live:** the host unticks the mod while you play. Expected: your status changes to
  "Inactive: the server has this mod turned off" and lights go back to vanilla for you too; ticking it on again brings
  switching back.
- [ ] **M05 Dedicated server:** install on a dedicated server and on both players. Expected: M01, M06 and M07
  behaviour; the server log shows the mod loaded, no error.
- [ ] **M06 Player without the mod refused:** default settings. A friend without the mod joins. Expected: about a
  second after joining (usually still in the loading screen) their game goes back to the main menu with "Incompatible
  version"; the host's log: `Refused <name> (<id>): their game does not run Switchable Lights, ...`. The host sets
  `AllowPlayersWithoutMod = true`: the friend can join and play (host log: the "AllowPlayersWithoutMod is on"
  warning). Setting it back to `false` while they are online refuses them about a second later; a player with the mod
  stays. (unverified: the menu message is read from the game code, not seen in game yet)
- [ ] **M07 Server list for everyone:** the host removes `piece_walltorch` from its `Lights`; the friend keeps the
  default list. Expected: the friend's log shows `Using the server's light list: ...` once; for both players a sconce
  shows the vanilla hover and burns resin, torches still switch. The host puts the default list back: the friend's log
  shows `Using the server's default light list.` and sconces switch again, no rejoin needed. The friend's own
  config file is unchanged; in a single-player world the friend's own list applies.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 Batch Station Feeding:** with MC Batch Station Feeding on, look at a torch with Resin in your inventory
  and press Shift+E. Expected: no batch hint on lights; Shift+E switches the light once. On a campfire, Shift+E still
  adds 5 wood.
