# MC framework — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
Automated coverage: `./tools/Test-Framework.ps1` (live toggle, config watching, dependency gating),
`./tools/Test-Smoke.ps1` and `./tools/Test-InWorld.ps1` (in-world self tests; its own checks `probe.baseline` and
`probe.runner` run on every call). The items below need eyes and hands.

**Build under test:** framework v1 (build id shown next to each mod in the MC Mods panel).

## Framework v1 — panel and notices

- [ ] **F01 Panel button:** main menu shows an **MC Mods (N)** button top-right. Also in the pause menu (Esc) in a
  world. It does not show during normal play.
- [ ] **F02 Panel content:** click it. Each MC mod appears under its category with a toggle, version + build id, scope,
  who needs it, multiplayer support, a coloured status line, and its multiplayer notes.
- [x] **F03 Live toggle:** (passed 2026-09-28 as crossbow T21, build f15f765) in a world, pause → MC Mods → untick Crossbow Stays Loaded. Status turns grey "Off".
  Resume: crossbow behaves like vanilla (reload after swap). Tick it again: the mod works again, no restart.
- [ ] **F04 Clicks don't leak:** with the panel open over the pause menu, clicking toggles never triggers the game
  buttons behind (Logout, Settings...). Clicking the MC Mods button itself never clicks what is under it. Closing
  the panel restores the menu's buttons. On the character-select screen, clicking in the panel does not rotate the
  character. No errors in the log while the panel is open (also over Settings > Graphics).
- [ ] **F09 No button during play:** in a world, open a game popup (e.g. build menu favourites > new category):
  the MC Mods button must NOT appear. It only appears in the Esc menu and main menu.
- [ ] **F05 Esc closes cleanly:** press Esc with the panel open (menu closes). The panel disappears; the game
  controls and cursor behave normally.
- [ ] **F06 UI scale:** panel is readable at your resolution (it scales with screen height).
- [ ] **F07 Config file sync:** toggle a feature in the panel, then open its `.cfg`: `Enabled` and `Status` match.
- [ ] **F08 Spawn notice:** make a feature inactive for a reason other than "Off" (for example run
  `Test-Framework.ps1 -KeepProbes`, turn Probe A off, enter a world). A top-left message says how many features are
  inactive and why.
- [ ] **F11 Always-on patch survives the toggle:** (Sneak Ambush, the first mod with `[AlwaysOnPatch]` content) set
  `Enabled = false` in `MC.Combat.Sneak.Ambush.cfg`, start the game and load a character that carries Smoke Screens:
  they are still in the inventory, and no "could not register" error is logged. `./tools/Test-Framework.ps1` checks
  the live-toggle part with Probe A.
- [ ] **F12 Conflict status:** (Dual Wielding, the first mod with a `LocalBlocker`) install another dual wield mod
  next to it (for example RustyMods DualWielder). The MC Mods panel shows Dual Wielding with an orange status naming
  the other mod, the spawn notice lists it, and no Dual Wielding patch runs. `./tools/Test-Framework.ps1` checks the
  mechanism with Probe A.
- [ ] **F10 Quit from a world:** with MC mods active (Sleep Through the Day among them), quit the game from inside a
  world (Esc > Quit > Quit to desktop). In `BepInEx/LogOutput.log`, after the world starts shutting down, no MC mod
  logs "Activated." again and there is no "Could not activate" error. Before the fix (2026-09-29, found by the
  in-world harness), every active MC mod logged "Activated." once more while the game closed, because the world's
  shutdown re-checked plugins that were already destroyed. If the log stops before the shutdown lines, repeat once.

## Framework v1 — multiplayer (needs a second player and/or a dedicated server)

- [ ] **N01 Client mod on vanilla server:** join a server without mods. Client-side features (Crossbow) stay Active.
- [ ] **N02 Both-side mod, server has it:** (the first `Both` mod is Sleep Through the Day) join a server with the
  mod: Active.
- [ ] **N03 Both-side mod, server lacks it:** (Sleep Through the Day) join a vanilla server: status "the server does
  not have this mod", feature off, no errors. Leave and load single-player: Active again.
- [ ] **N04 Server turns it off live:** (Sleep Through the Day + dedicated server or host) with a client connected,
  turn the feature off on the server: the client's status becomes "the server has this mod turned off"; on again:
  Active.
- [ ] **N05 Host notice:** host with Sleep Through the Day, a friend joins without it: the host's log names that
  player.
- [ ] **N07 Player turns a Both mod off while connected:** (any Combat mod of this run, dedicated server or host with
  the mod on) a player with the mod joins, then turns it off in the MC Mods panel. The player's log says "Told the
  server that <mod> is now off on this game."; the server's log says "<player> turned <mod> off on their game.".
  Turned on again: the same two lines with "on". The mod's own join check reacts as its TESTING.md says.
- [ ] **N08 Player joins with the mod turned off:** a player whose copy of a Combat mod is off (`Enabled = false`)
  joins a server that has it on: the server's log names the problem ("has the mod turned off") and the mod's join
  check reacts as its TESTING.md says. A player with the mod on joins normally.
- [ ] **N06 Dedicated server loads a Both mod:** install BepInEx and Sleep Through the Day on a dedicated server
  (`valheim_server.exe`, updated to the same game version as the clients): the server log shows the mod's
  `[MC:ready]` line and "Activated", and no error from the MC Mods panel (the server has no screen).

## In-world self-test harness (`tools/Test-InWorld.ps1`, world probe 0.1.0)

Build under test: the probe's build id is the commit in the `[MC:ready] MC.Core.Probe.World` log line (the run
folder keeps a copy of the log). Every run checks itself with `probe.baseline` and `probe.runner`; the items below
check what the script cannot see by itself.

- [ ] **W01 End to end:** close the game, run `./tools/Test-InWorld.ps1`. After `probe.baseline` and `probe.runner`,
  every deployed mod's self tests run; the summary groups each test's lines under its name and prints
  `mod tests run: N (<mod GUID> <count>, ...)`. The run ends with "IN-WORLD TEST PASSED" (exit code 0) only when every
  test passed, else with "IN-WORLD TEST FAILED" (exit code 1) and the failed tests in red. This item passes when both
  probe tests pass, the verdict matches the tests' results, and the game quits by itself (about 1 minute plus the mod
  tests: about 3 minutes with today's mods).
- [ ] **W02 Screenshots:** open the run's `shots` folder (path printed at the end): `probe.baseline__world.png` shows
  the character at the spawn stones, `probe.baseline__inventory.png` shows the open inventory.
- [ ] **W03 Your saves untouched:** before a run, note the "date modified" of your files in
  `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\characters_local` and `worlds_local`. After the run nothing there
  changed (only the game's own `Player.log`). The log copy in the run folder shows
  `save data path local='...\saves' cloud='...\saves' ... cloud saves enabled=False` and the game's own line
  "Cloud storage is not supported or enabled". Start the game normally: your Steam Cloud characters and worlds are all
  there, unchanged.
- [ ] **W04 Menu still remembers you:** after a run, start the game normally: your usual character is preselected on
  the character screen and your usual world in the world list.
- [ ] **W05 Clean game folder:** after a run there is no `BepInEx/plugins/MC_Valheim/Core/MC.Core.Probe.World` folder
  and no `BepInEx/config/MC.Core.Probe.World.cfg`; the run folder holds `shots\`, `config-backup\` and `LogOutput.log`
  but no `saves\`. The "Clean up" step prints "mod config files as before the run" or names each config file it put
  back; every `MC.*.cfg` in `BepInEx/config` is identical to its copy in `config-backup\`.
- [ ] **W06 Failures are reported:** run `./tools/Test-InWorld.ps1 -NoBuild -TestTimeoutSec 1`. It ends with
  "IN-WORLD TEST FAILED" (exit code 1): both probe tests show `FAIL ... timed out after 1 s`, the second test still
  runs after the first one failed, and the game quits by itself.
- [ ] **W07 Keep running:** run `./tools/Test-InWorld.ps1 -NoBuild -KeepRunning`. After the verdict (PASSED or FAILED,
  depending on the mod tests as in W01) the game stays in the world. Close it: within about 10 s the probe folder, its
  `.cfg` and the run's `saves\` are gone, and every `MC.*.cfg` in `BepInEx/config` is identical to its copy in the
  run's `config-backup\` (`cleanup.log` in the run folder names any file that was put back).
- [ ] **W08 A stray probe does nothing:** deploy the probe by hand
  (`dotnet build tests/Probes/Core.Probe.World/MC.Core.Probe.World.csproj`) and start the game normally with
  `./tools/Start-Game.ps1`: the main menu lists your real characters, nothing starts by itself, and the log says
  "MC_INWORLD_DIR not set: world probe idle". Close the game, then remove it
  (`dotnet build tests/Probes/Core.Probe.World/MC.Core.Probe.World.csproj -t:UndeployMod`) and delete
  `BepInEx/config/MC.Core.Probe.World.cfg`.
- [ ] **W09 Steam closed:** quit Steam and run the script: it stops at once with "Steam is not running", without
  launching the game.
- [ ] **W10 Mod tests and filter:** with a mod that registers self tests deployed (e.g. Sleep Through Day), run
  `./tools/Test-InWorld.ps1 -NoBuild -Only sleep.`: only the `sleep.*` tests run after the two probe tests, each test's
  lines are grouped under its name in the summary, and screenshots taken by those tests are listed with their size.
  Each `BEGIN` line names the mod that owns the test (`BEGIN sleep.morning (MC.Exploration.Sleep.ThroughDay)`), the
  other mods' tests are listed in `NOTE probe: filtered out: ...`, and `mod tests run` counts only
  `MC.Exploration.Sleep.ThroughDay`.
- [ ] **W11 Configs put back after Ctrl+C:** run `./tools/Test-InWorld.ps1 -NoBuild -Only sleep.`. While the console
  shows "Wait for the probe" (the world takes about 30 s), change a harmless value in
  `BepInEx/config/MC.Exploration.Sleep.ThroughDay.cfg` (e.g. `WakeUpHour`, not `Enabled`) and save, and create an
  empty `BepInEx/config/MC.Test.cfg`. Then press Ctrl+C. The script closes the game, prints "config put back as before
  the run: MC.Exploration.Sleep.ThroughDay.cfg (the run was cut short before the tests cleaned up)" and "config
  created during the run removed: MC.Test.cfg", removes the probe and the saves. The Sleep file is identical to its
  copy in the run's `config-backup\`, `MC.Test.cfg` is gone, and the run's `config-backup\pending.txt` is gone.
- [ ] **W13 A killed script is noticed:** start `./tools/Test-InWorld.ps1 -NoBuild -Only sleep.` and, while it waits
  for the probe, close its console window (the script itself dies, nothing is put back); close the game. Run the
  script again: it stops at once with "the run <folder> ended before it put the MC mod config files back", naming
  the backup folder. After deleting that run's `config-backup\pending.txt`, the next run starts normally.
- [ ] **W12 A run that tests nothing fails:** run `./tools/Test-InWorld.ps1 -NoBuild -Only nosuchtest`: both probe tests
  pass, yet it ends with "IN-WORLD TEST FAILED" (exit code 1), saying `-Only 'nosuchtest' matched no test` (with the
  tests the mods registered) and "no mod test ran, only the probe's own". Run `./tools/Test-InWorld.ps1 -Mod Crossbow`:
  the other mods' tests still run, it warns `MC.Combat.Crossbow.StaysLoaded ran no test: has no self tests` and
  fails with "no test of the mods picked with -Mod Crossbow ran". Set `Enabled = false` in
  `MC.Exploration.Sleep.ThroughDay.cfg` and run `./tools/Test-InWorld.ps1 -NoBuild -Only sleep.`: it fails with
  `-Only 'sleep.' matched no test` and the warning `MC.Exploration.Sleep.ThroughDay ran no test: it is turned off in
  its .cfg (Enabled = false)`. Set `Enabled = true` again afterwards (the run puts the file back as it found it).
