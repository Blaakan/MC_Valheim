# Sailing Skill — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-10-01: loads, patches cleanly, JitCheck clean. In-world self-tests
(`./tools/Test-InWorld.ps1`) passed 2026-10-01 on the final code: all six `sailing.*` tests (`sailing.network`,
`sailing.math`, `sailing.skill`, `sailing.helm`, `sailing.pending`, `sailing.ship`). An earlier run with the same
ship code failed `sailing.ship`'s paddle comparison at 0.25 s (wave noise); the check now compares the 0.5 s and 1 s
speeds. No hands-on in-game test yet.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off), `die` kills you (with `god` off), `heal` refills health. Skills:
`raiseskill Sailing <levels>` adds levels (Tab completes the name), `resetskill Sailing` removes the skill. Ships:
stand in shallow water at a coast and `spawn Karve` (or `spawn Raft`, `spawn VikingShip`, `spawn VikingShip_Ashlands`);
press E at the helm to steer, W/S step the speed setting (Back, Stop, Slow = paddle, Half sail, Full sail), A/D turn.
Wind: `wind <angle> <intensity>` fixes the wind (angle in degrees: 0 blows toward the world's +z, 90 toward +x; for
example `wind 0 1` = full strength), `resetwind` gives the normal wind back; the ship HUD's wind icon shows where it
comes from. Map: `resetmap` hides the explored map again (only in a test world), `exploremap` reveals all. `tod 0.5`
(noon) helps to see. Compare a level with `resetskill Sailing` (level 0) and `raiseskill Sailing 100`: every bonus is
linear, so 0 and 100 show the biggest difference.

Names checked in the 1.0.16 game code: the console commands above (and the `wind` angle), `Ship`, the speed settings,
the `Minimap` explore fields. Checked in the game data by the in-world self-tests (1.0.16, 2026-10-01): the ship
prefabs `Karve`, `Raft`, `Trailership` (no sail), `VikingShip` and `VikingShip_Ashlands` (the only one built for the
Ashlands sea); ship health (Raft 300, Karve 500, VikingShip and Trailership 1000, VikingShip_Ashlands 3000); every ship
damages itself in collisions (hit type Boat, from 1.5 m/s, full damage from 7 m/s: blunt 20 Raft, 30 Karve, 50 the
others); the map reveal radius (50 m, every 0.25 s); the normal game's skill numbers (death penalty 5% of the level,
Rested +50% XP for every skill); the level-up text "Skill improved Sailing: N". Not checked **(unverified)**: the
creature `Serpent`.

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Exploration.Sailing.Skill.cfg`; default settings unless an item says otherwise, and put each changed
one back afterwards. Use a world with the default skill-gain world modifier and a coast near the spawn; do not use your
main character for the death and reset items.

## 0.1.0 — single player

- [ ] **T01 The skill exists:** `raiseskill Sailing 10`, open the Skills panel (Tab, Skills). Expected: a "Sailing" row
  at level 10 with a sail icon; its tooltip explains the skill; the console shows "Skill Sailing = 10". Type
  `raiseskill Sa` and press Tab. Expected: it completes to "Sailing" (also for `resetskill`).
- [ ] **T02 XP at the helm:** `resetskill Sailing`, take the helm of a Karve and sail (or paddle) about 400 m, not
  Rested, then let go of the helm. Expected: the first level-up shows the game's big centre message "Skill improved
  Sailing: 1", without an icon; later ones appear at the top left with the sail icon; Sailing is about 5 after 400 m
  (50 XP per km; about 6 when Rested); Debug "At the helm of Karve: earning Sailing XP (50 per km)." when you take the
  helm and "Left the helm of Karve: 0.4 km sailed, 20 Sailing XP (before bonuses)." when you let go.
- [ ] **T03 No XP without moving or steering:** at the helm with the ship at Stop and still, wait a minute; then stand
  on the deck (not at the helm) while the ship moves (Half sail keeps going without a helmsman). Expected: no Sailing
  progress in either case.
- [ ] **T04 Wind at poor angles (G2):** `wind 0 1`, Full sail. At Sailing 0 turn the ship until the HUD wind icon
  greys out close to the bow (the no-go zone) and note the angle; also sail straight downwind and note the speed after
  it settles. `raiseskill Sailing 100` and repeat. Expected: at 100 the sail still pulls well at about 32-37 degrees
  off the wind, where the normal game gives nothing (the icon stays whiter and the ship keeps moving), and only stops
  pulling within about 26 degrees; the downwind speed is higher; beam reach (wind from the side) is about the same.
  Heading straight into the wind never works.
- [ ] **T05 Turning (G3):** at Full sail, hold A for a full 360-degree turn and time it, at Sailing 0 and 100. Also time
  how long the rudder takes to reach full lock (HUD rudder icon). Expected: both clearly faster at 100 (about a third
  less time); the same when paddling (Slow).
- [ ] **T06 Stopping (G4):** at Full sail and full speed, step down to Stop (S three times) and time until the ship
  stands still (or count the metres), at Sailing 0 and 100. Expected: at 100 the ship stops within a few seconds; at 0
  it drifts much longer. With nobody at the helm (let go at Stop) it drifts like the normal game at any level.
- [ ] **T07 Accelerating (G5):** from standing still, go to Full sail with the wind from the side and time until the
  speed stops rising; note that speed. Same with Slow (paddle). Do it at Sailing 0 and 100. Expected: at 100 the top
  speed is reached clearly sooner (about a third less time), and the top speed is about the same.
- [ ] **T08 Map reveal (G6):** in a test world, `resetmap`, open the map. Stand on the deck of a ship at sea (or at the
  helm) for a few seconds at Sailing 0, then move about 400 m and do the same at Sailing 100. Expected: the revealed
  circle at 100 is about twice as wide (about 100 m around you instead of 50 m). Swimming next to the ship, or ashore,
  reveals the normal circle at any level.
- [ ] **T09 Ship damage (G7):** at Sailing 100 (keep `god` on: it protects you, not the ship), let a Serpent
  (`spawn Serpent`, at sea) bite the ship, or ram rocks at speed. Expected: the damage numbers on the ship are half of
  what the same hit does at Sailing 0; Debug "Karve hit (EnemyHit): … -> … damage before resistances (best Sailing
  aboard 100)." (a collision shows "(Boat)": a Karve ramming at 7 m/s or more goes from 30 to 15).
- [ ] **T10 Not reduced:** at Sailing 100, (a) hit your own ship with an axe; (b) capsize a ship (ram it onto a steep
  shore) and let it break up. Expected: (a) full damage numbers, no "hit … best Sailing" Debug line; (b) it loses health
  every second like the normal game until it breaks.
- [ ] **T11 Level kept:** Sailing at some level with progress in the bar; log out and back in. Expected: same level and
  progress. `resetskill Sailing`. Expected: the row is gone.
- [ ] **T12 Console all:** `raiseskill all 5`. Expected: Sailing rises by 5 with the other skills. `resetskill all`.
  Expected: Sailing is gone with the others.
- [ ] **T13 Death penalty:** Sailing 40, `god` off, `die` (not within 10 s of an earlier death), respawn. Expected:
  Sailing dropped like the other skills, by 5% of its level (40 to 38), and its progress bar is empty.
- [ ] **T14 Language:** change the game language in the settings and back. Expected: the skill is still called
  "Sailing" with its description (English in every language).
- [ ] **T15 Rested:** sail the same stretch Rested and not Rested. Expected: about 50% more Sailing progress when
  Rested (the Rested bonus applies like for other skills).
- [ ] **T16 No extra drift or heel (G5):** `wind 0 1`, Full sail with the wind from the side (beam reach: there the
  wind bonus adds nothing), from standing still, at Sailing 0 and 100. Expected: at 100 the ship picks up speed sooner,
  but it drifts sideways and leans (heels) about as much as at 0: the extra push is along the bow only.
- [ ] **L01 Live toggle:** at Sailing 100 at the helm under sail, untick Sailing Skill in the MC Mods panel. Expected:
  the handling is the normal game's at once (slower turns, no brake at Stop), the map reveal is normal, no Sailing XP;
  the Skills panel still shows Sailing 100; Debug "Withdrew the published Sailing level (mod turned off).". Tick it
  again. Expected: bonuses and XP come back; Debug "Published Sailing level 100 …". Toggle several times while sailing.
  Expected: handling ends up the same as before (nothing piles up).
- [ ] **L02 `Enabled = false` + restart:** with Sailing at some level, set `Enabled = false` and start the game, load
  the character, log out, set `Enabled = true`, start again. Expected: `Status` reads "Off (disabled in settings)." in
  the first session and Sailing is still listed with its level and icon; after the second start the level is unchanged;
  no error in the log.
- [ ] **L03 Clean log:** play through T01-T16 while `./tools/Watch-Log.ps1 -Mine` runs, then quit the game from the
  world (Esc, Logout, Exit). Expected: no error and no warning from Sailing Skill (also not while the game quits).
- [ ] **L04 Turned on mid-voyage:** start with `Enabled = false`, take the helm, then tick the mod on while sailing.
  Expected: XP starts ("At the helm of …" Debug line) and the handling changes within a second.

## 0.1.0 — multiplayer

Cheat commands (`raiseskill`, `resetskill`, `spawn`, `wind`, `god`) work only in single player or on the host, never
for a client of a dedicated server or of a host. Prepare each character before joining: load it once in a
single-player world, `devcommands`, then `raiseskill Sailing 100` (or `resetskill Sailing` for Sailing 0), save and
quit. Keep one Sailing-0 and one Sailing-100 character per player for the comparisons. Spawn the Karve and the Serpent
on the host (or build the Karve at a workbench, or meet a wild Serpent at sea). Turn on Debug logging on every player's
game (in `BepInEx/config/BepInEx.cfg`, add `Debug` to `LogLevels` under `[Logging.Disk]`): M03 and M05 read Debug
lines.

- [ ] **M01 Server rules:** a dedicated server (or a host) and two players, all with the mod. Set `XpPerKm = 100` in
  the server's config while they play. Expected: both players log Info "Using the server's rules: 100 XP per km at the
  helm; …"; their own config files are unchanged. Put it back to 50.
- [ ] **M02 Player without the mod refused:** a player without the mod joins. Expected: about a second after joining
  their game goes back to the menu with "Incompatible version"; the server logs a Warning "Refused <player>: does not
  have the mod. This server requires Sailing Skill on every player …". With `AllowPlayersWithoutMod = true` on the
  server they can play, and the server logs a Warning "<player> does not have the mod; AllowPlayersWithoutMod is on, so
  they may play, …". A player with the mod on joins normally (Debug "<player> has Sailing Skill on, with the same
  network version: allowed.").
- [ ] **M03 Passenger simulates, helmsman steers:** player A (Sailing 0) boards a Karve first and stays as a passenger
  (A's game now simulates the ship); player B (a character prepared at Sailing 100) takes the helm. Expected: A's log
  shows Debug "Karve: B at the helm, Sailing 100 (this game simulates the ship)."; the ship turns, accelerates and
  brakes like in T05-T07 at 100 (compare with B's Sailing-0 character). Then A leaves the ship (the ship's simulation
  moves to B's game). Expected: the bonuses stay. Swap roles (B boards first as a passenger, A at the helm at Sailing
  0). Expected: normal handling.
- [ ] **M04 Only the helmsman earns:** A at the helm, B on deck, sail 400 m. Expected: A's Sailing rises, B's does not.
- [ ] **M05 Best sailor protects the ship:** A (Sailing 100) on deck, B (Sailing 0) at the helm; a Serpent bites the
  ship. Expected: half damage (Debug line on the game that simulates the ship: "best Sailing aboard 100"). A jumps off
  and swims away: full damage again.
- [ ] **M06 Map reveal is personal:** A (Sailing 100) and B (Sailing 0) on the same ship. Expected: A reveals the wider
  circle on A's map, B the normal one on B's.
- [ ] **M07 Hand-off to a player without the mod:** `AllowPlayersWithoutMod = true` on the server; player C without the
  mod boards first (C's game simulates the ship), A (Sailing 100, with the mod) takes the helm. Expected: the normal
  game's handling except the rudder, which still swings faster (A's own game), full damage, A still earns Sailing XP,
  no error in any log. C leaves the ship (A's game takes it over). Expected: A's bonuses apply. C at the helm of a ship
  A's game simulates: normal handling (C shares no level).
- [ ] **M08 Server without the mod:** join a server without the mod with a character that has Sailing. Expected: the MC
  Mods panel shows "Inactive: the server does not have this mod. …"; normal handling, no XP; the Skills panel still
  shows Sailing; after logging out and back in single player the level is unchanged.
- [ ] **M09 Mod turned off, refused:** `AllowPlayersWithoutMod = false` (default) on the server. (a) A player with the
  mod and `Enabled = false` joins. Expected: about a second after joining their game shows "Incompatible version" and
  goes back to the menu; the server log says "Refused <player>: has the mod turned off". (b) A player with the mod on
  joins and plays, then unticks Sailing Skill in the MC Mods panel. Expected: they are refused the same way about a
  second later. (c) Untick and tick it again within a second. Expected: they stay.
- [ ] **M10 Other network version:** build a copy with another network version (`dotnet build
  src/Exploration/Sailing.Skill/MC.Exploration.Sailing.Skill.csproj -p:ModNetworkVersion=2 -p:DeployToGame=false`), copy
  that DLL over a second player's copy (put the normal build back afterwards), and join a server with network version
  1. Expected: refused about a second after joining; the server log says "has another version of the mod (network
  version 2, the server has 1)".
- [ ] **M11 Dedicated server:** a dedicated server with the mod and Debug logging (in its `BepInEx/config/BepInEx.cfg`,
  add `Debug` to `LogLevels` under `[Logging.Disk]`). Expected: it starts without errors from Sailing Skill (no icon is
  drawn there); players sail with the server's settings (M01).
- [ ] **M12 Rudder on a ship another game simulates:** B at the helm (Sailing 100) of a ship A's game simulates (M03
  setup). Expected: steering feels the same as in single player at 100 (the rudder swings faster), no jerks beyond the
  normal game's.

## 0.1.0 — other mods

- [ ] **X01 Encyclopedia (MC):** with Sailing at level 0 or missing, browse the Encyclopedia's item pages (weapons,
  tools, food: their text reads skill levels). Expected: the Sailing row in the Skills panel is neither added nor
  removed by it.
- [ ] **X02 Swim Dive (MC):** dive and surface next to a ship at sea at Sailing 100. Expected: while swimming, the
  normal map reveal; climbing aboard gives the wider one; no errors from either mod.
- [ ] **X03 Sneak Ambush (MC):** crouch still on the deck of a moving ship. Expected: Sneak Ambush's holding-still bonus
  ends (carried, its rule); Sneak XP and Sailing XP unchanged.
- [ ] **X04 Other sailing mods (optional):** with GrindstoneSkills, Smoothbrain's Sailing or ImpactfulSkills installed.
  Expected: Info "<name> is installed: it …" once per session (again after the mod is turned off and on); both skills
  level up; `raiseskill sailing 5` raises this mod's Sailing (and maybe theirs); no errors. Note the plugin GUIDs from
  the Debug "Loaded plugins" line.
