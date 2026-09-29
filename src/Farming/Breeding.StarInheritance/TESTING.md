# Breeding Star Inheritance — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). Committed build `1254c75` (2026-09-29), the version needed on the server and every player (join check,
server settings for everyone): Debug and Release build without warnings; smoke test (`./tools/Test-Smoke.ps1`, all MC
mods) passed; automated in-world self-tests (`./tools/Test-InWorld.ps1`) passed on the same code: `breeding.rule`,
`breeding.farmer`, `breeding.birth`, `breeding.egg` and `breeding.network` (17/17 tests of the run). No hands-on
in-game test yet; the multiplayer items need two players.

**Setup:** use a test character and a test world. Debug logging on (`./tools/Setup.ps1 -DevBepInExConfig`), F5 →
`devcommands` (then `confirmcheats` if asked). Keep `./tools/Watch-Log.ps1 -Mine` open: every birth writes one Debug
line, and the tests read the partner level, the farmer, the chance and the roll from it.
- **Pen:** a small fenced pen (animals within about 3 m of each other, for example 2 × 2 fence pieces), enemies away
  (alerted animals do not breed). Two tamed adults of the stated levels: `spawn Boar 1 1` = 0★, `spawn Boar 1 2` = 1★,
  `spawn Boar 1 3` = 2★, then `tame` (it tames every untamed tameable creature your game simulates, at any distance:
  the command's 20 m radius is not used; so spawn wild ones only after taming). Drop food in the pen so they stay fed
  (`spawn Raspberry 20`, `spawn Mushroom 20`, `spawn Carrot 10` for boars; `spawn CarrotSeeds 20`, `spawn Barley 20`
  for hens; which foods each animal eats is game data, **(unverified)**). Remove or move babies between births (no
  new pregnancy starts while 5 boars and piglets, or 10 hens, are within 10 m; whether laid eggs count is unverified):
  `killall` is **not** safe (it kills tames too); pick up eggs, lead or harpoon young animals out.
- **Prefab names** checked in the 1.0.16 game data (`StreamingAssets/SoftRef/manifest_extended`): `Boar`,
  `Boar_piggy`, `Hen`, `ChickenEgg`, `Chicken`, `Asksvin`, `AsksvinEgg`, `Asksvin_hatchling`, `Wolf`, `Lox`,
  `Raspberry`, `Mushroom`, `Carrot`, `CarrotSeeds`, `Barley`, `fire_pit`.
- **Breeding values** read in game by the `breeding.birth` and `breeding.egg` self-tests (`NOTE` lines, 1.0.16):
  `Boar` → `Boar_piggy`, partner range 3 m, population 5 within 10 m, pregnancy 60 s, a breeding tick every 30 s,
  3 love points; `Hen` → `ChickenEgg` (hatches into a tamed `Chicken`), partner range 4 m, population 10 within 10 m,
  pregnancy 60 s, tick 30 s, 3 love points; both stay fed for 600 s. **(unverified)**: `Chicken` → `Hen` and
  Asksvin → AsksvinEgg → Asksvin_hatchling → Asksvin (from the wiki), which foods they eat, and the "No skill drain"
  time used by M06 (code default 10 s).
- Stars: read them from the health bar (1★ = level 2, 2★ = level 3); egg levels from the ground hover ("Egg" =
  quality 1, "Egg[2]" = 1★, "Egg[3]" = 2★).
- `skiptime <seconds>` finishes a pregnancy or a growth but makes the animals hungry: feed them again after it.
  `spawn`, `tame`, `raiseskill`, `resetskill`, `skiptime` are cheat commands: they work only in single player or on
  the host.
- Settings are changed in the config file `BepInEx/config/MC.Farming.Breeding.StarInheritance.cfg` or with
  ConfigurationManager; they apply to the next birth. "All chances 0" = `ChanceAtFarming0`, `ChanceAtFarming100` and
  `ChanceWithoutFarmer` all `0`; "all chances 100" = all three `100`. In multiplayer only the host's (or dedicated
  server's) five breeding settings count, for every game: change them there. A client's own file is ignored while
  connected.
- Debug lines: conception `Conceived: <prefab> (level <own>) with partner level <p> at <d> m.`; birth `Birth by
  <prefab> (simulated by this game): own <own>, partner <p> (recorded | found at birth, <d> m | bred alone | no tamed
  partner within <r> m), min offspring <m> -> base <b>; farmer you Farming <f> (own) | farmer <name> Farming <f>
  (published) | no farmer known -> chance <c>%; roll <v> -> level <n>[, extra star]; <own | server | self-test>
  settings.` (or `... -> level <n>, at the cap: no roll; ... settings.`); publish `Published Farming level <f> for
  other players.`; withdraw `Withdrew the published Farming level (mod turned off).`
- Multiplayer log lines. Client, Info: `Using the server's breeding settings: ChanceAtFarming0 <a>, ChanceAtFarming100
  <b>, ChanceWithoutFarmer <c>, FarmerRange <r>, MaxStars <m>. Your own settings apply again in single player and when
  you host.` (once per new set of values). Server or host, Warning: `Refused <player> (<platform id>): their game does
  not run Breeding Star Inheritance, which this server requires on every player ...`, or with
  `AllowPlayersWithoutMod` on `<player> (<platform id>) joined without Breeding Star Inheritance. AllowPlayersWithoutMod
  is on, ...`; Debug: `<player> (<platform id>) has Breeding Star Inheritance installed: allowed.` and `Sent the
  breeding settings to <n> player(s): 1|<a>|<b>|<c>|<r>|<m>.` The framework adds its own Warning on the server for a player
  without the mod (`... connected without Breeding Star Inheritance; its multiplayer part will not work for them.`).
- A player "without the mod": a game without BepInEx, or with this mod's folder removed from
  `BepInEx/plugins/MC_Valheim/Farming/` (a mod turned off with `Enabled = false` still counts as installed).

## 0.1.0 — single player

- [ ] **T01 Lower parent, both parents:** all chances 0. Pen 0★ + 2★ boars: at least 4 births, from **both** parents
  (the Debug birth line names the parent's own level: `own 1` and `own 3`). Expected: every piglet 0★; the lines show
  `partner 3 (recorded)` / `partner 1 (recorded)`.
- [ ] **T02 Never the higher level:** default settings, same pen, at least 8 births. Expected: no piglet is 2★
  (vanilla would give about half); some may be 1★ (the extra star).
- [ ] **T03 Extra star:** all chances 100. Pen 0★ + 2★ → every piglet 1★. Pen 1★ + 1★ → every piglet 2★.
- [ ] **T04 Cap:** all chances 100. Pen 2★ + 2★ → 2★ piglets, Debug `at the cap: no roll`. Then `MaxStars = 1`: pen
  1★ + 1★ → 1★ piglets, `at the cap: no roll`.
- [ ] **T05 Farming decides the chance:** `ChanceWithoutFarmer 0`, `ChanceAtFarming0 0`, `ChanceAtFarming100 100`.
  Pen 0★ + 0★: `resetskill Farming` → piglets 0★, Debug `farmer you Farming 0 (own)`; `raiseskill Farming 100` →
  piglets 1★, `farmer you Farming 100 (own)`.
- [ ] **T06 Chance line:** default settings. Farming 0 / 50 / 100 (`resetskill Farming`, `raiseskill Farming 50`...)
  → Debug `chance 15%` / `32.5%` / `50%`. Walk about 80 m away while the pen keeps breeding (the pen must stay loaded)
  → the Debug line still names you with the same chance. `ChanceAtFarming0 = 10` with Farming 0 → `chance 10%`. Every
  birth line ends with `own settings`, and no `Using the server's breeding settings` line appears in single player.
- [ ] **T07 Eggs:** hens. All chances 100, pen of two 0★ hens → eggs "Egg[2]"; hatch one (next to a `fire_pit`,
  under a roof) → 1★ chick; `skiptime` until it grows → 1★ hen. All chances 0, pen 0★ + 2★ hens → eggs "Egg"
  (quality 1) only.
- [ ] **T08 Asksvin (optional):** as T07 with an Asksvin pair in the Ashlands: "Asksvin Egg[2]" → 1★ hatchling → 1★
  Asksvin.
- [ ] **T09 Partner gone:** all chances 0, pen 2★ + 0★. When the Debug conception line shows the 2★ parent conceived
  (`Conceived: Boar (level 3) with partner level 1`), kill the 0★ partner (or lead it far away) before the birth.
  Expected: the piglet is 0★, Debug `partner 1 (recorded)`.
- [ ] **T10 Pregnancy from before:** all chances 0, pen 0★ + 2★, both fed and within about 3 m. `Enabled = false`
  for about 5 minutes, then `true`. Expected: the next births show `found at birth` (or `recorded` for pregnancies
  that started after turning it on) and no piglet is 2★.
- [ ] **T11 Never lowered:** `spawn Boar 1 5` twice, `tame`, all chances 100. Expected: Debug birth line `level 5, at
  the cap: no roll` (the piglet shows no star, like any level-5 creature in vanilla), never level 3.
- [ ] **T12 Scope:** a 1★ piglet grows (`skiptime`) into a 1★ tamed boar. Untamed boars from `spawn Boar 3 2` stay
  1★, wild animals around keep their usual levels, and no Debug birth line appears for any of them.
- [ ] **T13 Live toggle:** with a breeding pen: Esc → MC Mods → untick Breeding Star Inheritance. Expected: no
  Debug birth line, the withdraw line appears, and over several births a 0★ + 2★ pair gives 2★ piglets again
  (vanilla). Tick it again: the Debug lines resume with the next birth, no restart.
- [ ] **T14 Disabled in the config file:** `Enabled = false` in the config file, restart. Expected: Status "Off
  (disabled in settings)", the log shows no patch of this mod, vanilla breeding. Set it back to `true`.
- [ ] **T15 Clean log:** through T01-T19, no warning or error from `Breeding Star Inheritance` in
  `BepInEx/LogOutput.log`; log out to the main menu and quit the game with the mod on and a pen loaded: no error.
- [ ] **T16 Egg display:** pick up an "Egg" and an "Egg[2]" from the ground. Expected: two separate stacks. Note
  whether the slot shows a quality number and the tooltip a quality line (the `breeding.egg` self-test logs the
  egg's max quality: 1 means no number). If no number is shown, drag the Egg[2] onto the Egg stack and note whether
  they merge (vanilla behaviour; update the README "Good to know" if the wording no longer matches).
- [ ] **T17 No partner at birth (optional):** all chances 0, pen 0★ + 2★. `Enabled = false` until the pair has bred
  at least once (a pregnancy started while off), kill the 0★ boar (or lead it more than 10 m away), then
  `Enabled = true`. Expected: a birth by the 2★ parent shows `partner none (no tamed partner within <r> m)` (r = the
  species' pen range, code default 10) and a 2★ piglet (its own level). If the 0★ one was the pregnant parent, repeat.
- [ ] **T18 Automated in-world self-tests:** Debug build, game closed, run `./tools/Test-InWorld.ps1`. It loads a
  throwaway single-player world and runs this mod's tests. Expected: `[selftest] PASS` and no `FAIL` for
  `breeding.rule` (the pure rule: lower parent, `m_minOffspringLevel` floor, cap only on the extra star, levels above
  the cap never lowered, chance 15% at Farming 0 / 50% at 100 / 10% with no farmer, 0 and 100 exact, 20000-roll Monte
  Carlo within tolerance), `breeding.farmer` (own Farming published on the player object, withdraw writes -1, own
  player counts at 500 m, reading Farming adds no Farming skill entry: the PASS line says "none added" when the
  probe character had no Farming entry; another mod's test that runs first can give it one, so to see that wording run
  `./tools/Test-InWorld.ps1 -Mod Breeding -Only breeding.`), `breeding.birth` (a 1 + 3 boar pair driven through the game's own breeding code: partner note written
  at conception and cleared at birth by overwriting it, not removing it; parent level restored, chance 0 → level 1,
  chance 100 → level 2, defaults → only 1 or 2, fallback partner 6 m away, no partner → own level 3, 3 + 3 → 3,
  5 + 5 → 5, floor 2 → 2 or 3), `breeding.egg` (same with hens: egg quality 1 / 2 / 1-2 / 3) and `breeding.network`
  (server settings on the wire: the defaults travel as `1|15|50|10|60|2`, decimals come back exact also with a
  German system language, out-of-range values are clamped to the config ranges, unreadable messages are refused whole;
  in the single-player world the own settings are used, a settings message from the network is ignored, and the
  self-test override still works; the join-check verdicts refuse only a fully connected player without the mod on a
  server; every species with breeding has a breeding tick longer than the 5 s a refused game can stay (1 s check + 4 s
  disconnect delay): a `NOTE` line lists each species' tick and the shortest one). The screenshot
  `piglets_with_stars` shows piglets with 1★ and one with 2★ in their health bars. If a `NOTE` line gives breeding
  values that differ from the Setup above, update the Setup.
- [ ] **T19 No Farming entry added:** a new character that has never farmed (no Farming row in the Skills tab), in a
  world with boars nearby, mod on. Stay near wild or tamed boars for at least 2 minutes without touching a cultivator
  or a scythe (the game itself may add the row as soon as a farming tool is used or its tooltip shown), then open the
  Skills tab.
  Expected: still no Farming row. Then `raiseskill Farming 10`: the row appears (vanilla), and within about 30 s the
  Debug log shows `Published Farming level 10 for other players.`

## 0.1.0 — multiplayer

Host A (the player who starts the world with "Start server"), client B; both with the mod unless stated, Debug
logging on both. The birth lines appear only on the game that simulates the pen. Every console command on the host A.
Breeding settings are changed on A only: A's settings apply to both games (B's log confirms them with the `Using the
server's breeding settings` line). To give B's character some Farming, B loads it once in a single-player world with
`devcommands` and `raiseskill Farming 100` before joining.

- [ ] **M01 Another player's Farming:** A sets `ChanceWithoutFarmer 0`, `ChanceAtFarming0 0`, `ChanceAtFarming100
  100`, `FarmerRange 10` (B's log shows the server settings line with these values). A sets up a 0★ + 0★ pen and
  leaves far away (more than 200 m, or through a portal). B (a fresh character, Farming 0) arrives first, so B's game
  simulates the pen, and stays about 20 m from it. A runs `raiseskill Farming 100`, comes back and stands in the pen.
  Expected: piglets 1★, B's Debug names A with Farming 100 `(published)` and ends with `server settings`. A steps back
  to 20-30 m: B's Debug names B (`own`, Farming 0), piglets 0★. A sets `FarmerRange 40` (B's log shows the new server
  settings): A counts again.
- [ ] **M02 Turned off on the farmer's game:** M01 settings. A (`resetskill Farming`) sets up a 0★ + 0★ pen and stays
  about 20 m from it, so A's game simulates it; B (Farming 100, see above) stands in the pen. Expected: A's Debug names
  B with Farming 100 `(published)`, piglets 1★. B turns the mod off in the MC Mods panel → B's withdraw line, and B
  stays connected (the server checks that the mod is installed, not that it is on); A's Debug names only A (`own`,
  Farming 0), piglets 0★. B turns it on again → B's log shows the server settings line again, and B counts again
  within a few seconds.
- [ ] **M03 Hand-off (simulating game without the mod):** A sets `AllowPlayersWithoutMod = true`. B without the mod
  joins and is not refused (A's log: `... joined without Breeding Star Inheritance. AllowPlayersWithoutMod is on, ...`).
  B simulates a 0★ + 2★ pen set up by A, A far away. Expected: births follow vanilla (2★ piglets appear), no Debug
  birth line on A; the piglets look normal to both. Set `AllowPlayersWithoutMod` back to `false` afterwards.
- [ ] **M04 Hand-off (egg):** A with `AllowPlayersWithoutMod = true` and all chances 100 gets an "Egg[2]" from a 0★ hen
  pair and gives it to B without the mod; B hatches it at B's base. Expected: a 1★ chick that grows into a 1★ hen; A
  sees the same stars.
- [ ] **M05 Server settings used by a client:** A all chances 100; B's own config file all chances 0. B joins: B's log
  shows `Using the server's breeding settings: ChanceAtFarming0 100, ChanceAtFarming100 100, ChanceWithoutFarmer 100,
  FarmerRange 60, MaxStars 2. ...`. A pen simulated by B (A far away): every birth gets the extra star, B's Debug lines
  show `chance 100%` and end with `server settings`. B leaves and loads a single-player world: births there use B's
  own settings (`chance 0%`, `own settings`), and B's config file still holds its own values.
- [ ] **M06 Publishing after a respawn:** M01 setup, A (Farming 100) in the pen. A runs `die`, respawns (a bed near the
  pen helps) and runs `die` again within the "No skill drain" time (the second death keeps the skills), respawns and
  returns to the pen. Expected: each time A's character appears after a respawn, A's Debug shows a publish line at
  once (`Published Farming level <f> ...`, with the level after the first death), and B's next birth line names A
  with that Farming. Reconnection: B (a client, with the mod and some Farming) leaves and joins the running server
  again while A keeps simulating the pen: B's publish line appears as soon as B's character appears, and A's next
  birth line names B with that Farming when B is the best farmer near the pen.
- [ ] **M07 Player without the mod refused:** default settings (`AllowPlayersWithoutMod = false`). B without the mod
  joins A. Expected: B's game starts loading the world, then, about a second after B got in (usually still during the
  loading screen), goes back to the main menu with the message "Incompatible version". A's log: the framework Warning,
  then `Refused B (<platform id>): their game does not run Breeding Star Inheritance, ...`; B's character is not in
  A's world; A keeps playing, no error on either side. B joins again: same result. If B's menu shows the plain
  disconnection message instead, mark it `[!]` and note how long B's loading screen lasted (the server closes the
  connection 4 s after the refusal; a single frame longer than that loses the message). (unverified: the message is
  read from the game code and its English text table, not seen in game yet)
- [ ] **M08 Allow setting on, then off:** A sets `AllowPlayersWithoutMod = true`; B without the mod joins and stays
  (A's log: the "AllowPlayersWithoutMod is on" Warning). A sets it back to `false` while B is online → B is refused
  about a second later (M07's message and log line). A player with the mod online at the same time stays (A's Debug:
  `... has Breeding Star Inheritance installed: allowed.`).
- [ ] **M09 Settings change live:** B connected. A changes `ChanceAtFarming0` and `MaxStars` in its config file in
  one save. Expected: A's Debug `Sent the breeding settings to 1 player(s): 1|...` once; within a second B's log shows
  one `Using the server's breeding settings` line with both new values; B's next birth uses them (`chance`, cap).
- [ ] **M10 Dedicated server loads it:** first update the Valheim Dedicated Server in Steam to 1.0.16 (see Sleep
  Through the Day M10: its log must print `Valheim version: 1.0.16`). Install BepInExPack Valheim in the dedicated
  server's folder, copy the mod folder `MC.Farming.Breeding.StarInheritance` (Nexus zip in `dist/nexus/` or the build
  output) to `BepInEx/plugins/MC_Valheim/Farming/`, start the server once: its `BepInEx/LogOutput.log` shows the
  `[MC:ready]` line for the mod, `Activated.`, and no error or warning from MC code. Stop it, set `MaxStars = 1` in
  its `BepInEx/config/MC.Farming.Breeding.StarInheritance.cfg`. **Pen:** cheat commands never work for a client of a
  dedicated server (admin or not), so build the pen beforehand: load a test world in single player (or host it) with
  the mod, set up a pen with two tamed 1★ boars (`spawn Boar 1 2` twice, then `tame`) and plenty of food on the ground
  in it, save and quit. Start the dedicated server on that world (`-world <name>` in its start script; a dedicated
  server on the same PC reads the same `worlds_local` save folder unless it is started with `-savedir`, (unverified);
  otherwise copy the world's `.fwl` and `.db` files into the server's world folder). A player with the mod joins: the
  mod is Active in their MC Mods panel, their log shows the server settings line with `MaxStars 1`, and the 1★ + 1★
  pen, which their game simulates (they are the first player near it), gives 1★ piglets (`at the cap: no roll; server
  settings`). A player without the mod joins: refused as in M07, the `Refused` line is in the server's log.
- [ ] **M11 Server turns it off and on live:** B with the mod connected. A turns the mod off in the MC Mods panel →
  B's panel shows "Inactive: the server has this mod turned off (or it is not working there)" and births B simulates
  follow vanilla. B leaves, removes this mod's folder, joins again: not refused while A has it off. A turns it on →
  about a second later B is refused (M07's message); no error on A.
- [ ] **M12 Player with the mod on a server without it:** B joins a host (or server) that does not have this mod:
  B's MC Mods panel shows "Inactive: the server does not have this mod. It must be installed on the server too.",
  births B's game simulates follow vanilla, no error. B leaves and loads a single-player world: Active again.
- [ ] **M13 Leaving during the check (optional):** the check runs about a second after B gets in, during B's loading
  screen, so this is a race. B without the mod joins A and closes its game (Alt+F4, or end `valheim.exe` in the Task
  Manager) the moment the loading screen appears; repeat two or three times. Expected: for each attempt, A's log shows
  either nothing from this mod (B left before the check) or the `Refused` line (B was still there), never an error;
  A keeps playing. (A turning the mod off during that second cancels the pending check: too short to test by hand;
  M11 covers turning it back on with a player without the mod online.)
- [ ] **M14 Turned on while connecting (optional):** A hosts with a password, so B gets the password prompt. B (with
  the mod, `Enabled = false` in its config file) joins A and, while the password prompt is shown, sets `Enabled = true`
  in its config file and saves it; then B enters the password. Expected: once in, B's MC Mods panel shows the mod
  Active, B's log shows `Using the server's breeding settings: ...` with A's values, and the birth lines of a pen B's
  game simulates end with `server settings`; no error.

## 0.1.0 — cross-mod

- [ ] **X01 Sort Chest (MC):** a chest with "Egg" and "Egg[2]" stacks (laid, or `spawn ChickenEgg 3 1` and
  `spawn ChickenEgg 3 2`), Sort with MergeStacks on. Expected: the stacks stay separate; dropped eggs keep their
  ground hover quality.
- [ ] **X02 Creature Kill and Tame Counts (MC):** births, hatchings and growing up add no tame count (its rule); no
  error from either mod. Both mods also act when your character appears (same game method): after a respawn, this
  mod's publish line appears and Creature Kill and Tame Counts keeps working (its counts unchanged); no error.
- [ ] **X03 Harpoon Hooks Tames (MC):** after a conception line, harpoon the partner and drag it away. Expected: the
  piglet uses the recorded partner level (as T09).
- [ ] **X04 Other breeding mods (optional):** with Star Level System: the Info line "Star Level System is installed:
  it decides breeding levels, so Breeding Star Inheritance leaves births to it." at the first breeding tick, no Debug
  birth line from this mod, and a 2★ + 0★ hen pair lays the same egg stacks as with Star Level System alone; no error.
  With BreedingUpgrades or Procreation Plus: extra stars can come from both mods (README note); no error. With Seasons
  in winter (breeding paused): at most one Debug line "Another mod skipped the game's breeding code for a birth that
  was due; ..." and no error.
- [ ] **X05 Sleep Through the Day (MC, also needed on the server and every player):** host A with both mods. B with
  both mods joins: both Active in B's MC Mods panel, no error. B with Sleep Through the Day only: refused by this mod
  (A's log: the framework Warning `B connected without Breeding Star Inheritance ...` and this mod's `Refused` line;
  nothing about Sleep Through the Day), no error. B with this mod only: not refused, this mod Active on B; Sleep
  Through the Day's own rule for a player without it applies (see its README); no error.
