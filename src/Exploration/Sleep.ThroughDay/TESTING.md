# Sleep Through the Day — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). Committed build `1254c75` (2026-09-29): Debug and Release build without warnings; smoke test
(`./tools/Test-Smoke.ps1`, all MC mods) passed (loads, patches apply, JIT check clean); automated in-world self-tests
(T17, `./tools/Test-InWorld.ps1`) passed on the same code: `sleep.morning`, `sleep.night`, `sleep.afternoon.default`
and `sleep.afternoon.included` (17/17 tests of the run). No hands-on in-game test yet.

**Automated checks (2026-10-08):** 39 of 40 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** build a **Bed** (`bed`) under a roof with a **Campfire** (`fire_pit`) lit next to it, claim it and make it
your spawn point (E until "Spawn point set"; the hover text then shows "[E] Sleep"). F5 for the console:
`devcommands` (if the console asks you to confirm cheats, run `confirmcheats`), then `time` prints
`<seconds> sec, Day: <n> (<fraction>), Can sleep | Can NOT sleep, ...`. The fraction reads like a 24-hour clock:
0.25 = 06:00, 0.50 = noon, 0.708 = 17:00, 0.75 = 18:00 (nightfall), 0.875 = 21:00. The day number is exact; the
fraction trails the real time by a second or two, and after a sleep it needs several seconds to catch up, so **after
waking up, wait about 10 s before reading `time`**. To reach the morning: `sleep` (skips to 06:00 without the sleep
screen), then wait or use `skiptime 60` until `time` shows 0.27-0.45. **Never use `tod`**: it fakes the time of day
without moving the world time (run `tod -1` if you did). A dream text appears only sometimes (random); its absence is
not a failure.

**Wake-up message (read before the items):** vanilla shows "Good morning" and then, if you were not Rested when you
lay down, "You feel rested (Comfort: N)", which replaces it at once (same frame), so you only see the Rested message.
The mod keeps that order and only puts its WakeUpMessage ("Good evening") in the place of "Good morning" after a
daytime sleep. So where an item below expects "Good evening" or "Good morning", you see that text only if you were
still Rested when you lay down (for example a second sleep a few minutes after the previous one); otherwise you see
the Rested message in both cases, and the Debug line `Woke up at ...` (below) tells which text was shown under it.

Names checked in the 1.0.16 game data: `bed` (Bed), `piece_bed02` (Dragon Bed), `ashwood_bed` (Ashwood Bed),
`fire_pit` (Campfire), `Greyling`; messages "Good morning", "Good night" (`$msg_goodnight`), "You can't sleep at this
time", "Spawn point set", "Day N". Checked in the in-world run log (2026-09-29): the day length of the game is 1800 s
(the Info line prints it; code default 1200 s) and the Rested message reads "You feel rested (Comfort:1)".
**(unverified)**: that the Dragon Bed and the Ashwood Bed carry the same bed behaviour as the Bed (prefab data; only
T02 uses them); the scene values of the sleep fade (3 s) and the dream chance (only durations and wording depend on
them); how long the dream videos last (T19).

Keep `./tools/Watch-Log.ps1 -Mine` open. Info lines from the server side (the host in single player):
- a morning sleep: `Day sleep: everyone is in bed at 08:24 (day 12). Waking up at 18:00: skipping 1008 s of world
  time in 12 s (day length 1800 s).`
- an afternoon sleep with IncludeAfternoon on (or a sleep started by another mod in the day window): `Day sleep: a
  sleep started at 14:10 by the game or another mod now ends at 18:00 instead of the next morning.`
With Debug logging (`./tools/Setup.ps1 -DevBepInExConfig`) each game also logs `Sleep started at ..., day ... (after
dawn|before dawn).` and `Woke up at ..., day ...: daytime sleep, message "Good evening" instead of "Good morning".` or
`...: night sleep, vanilla message.` (`...: daytime sleep, WakeUpMessage empty, vanilla message kept.` when the setting
is empty). The vanilla line `Time ..., day: ...    nextm: ...` still appears before the mod's lines: its `nextm` is the
target the mod then replaces.

## 0.1.0 — single player

- [x] **T01 Morning sleep ends at nightfall:** morning (`time` 0.27-0.45, "Can sleep"), note Day n. E on the bed.
  Expected: the character lies down; within 2 s the screen fades to black, "ZZZ" (sometimes a dream text); about
  12 s later you wake up with Rested in the status effects and the center message **"You feel rested (Comfort:
  N)"** if you were not Rested when you lay down (it covers "Good evening", see Setup), else **"Good evening"**;
  Debug line `daytime sleep, message "Good evening" instead of "Good morning"` either way; no "Day n+1" message; 10 s later `time` shows Day n (same) and 0.75-0.77. Info line
  `Day sleep: everyone is in bed at ... Waking up at 18:00`.
  *Automated: `sleep.morning`, `sleep.morning.rested`.*
- [ ] **T02 Same presentation as vanilla:** compare T01 with a night sleep (T05): same lying animation, fade, ZZZ,
  skip length, autosave line in the log (`Saved recently, skipping sleep save.` or a save). Optional: repeat T01 on a
  Dragon Bed (`piece_bed02`) and an Ashwood Bed (`ashwood_bed`) (unverified that they behave like the Bed).
  *Partly automated (`sleep.presentation`, `sleep.beds`); by hand: Look at a day sleep and a night sleep (or the
  day-fade / night-fade and day-awake / night-awake screenshots of sleep.presentation): the lying animation, the fade
  and the ZZZ must look the same. The autosave log line itself ("Saved recently, skipping sleep save." or a save) is
  only checked through the save timer.*
- [x] **T03 Afternoon sleep stays vanilla by default:** IncludeAfternoon off (default). At 0.55-0.65, sleep.
  Expected: vanilla: wake at 06:00 of Day n+1, "Good morning", "Day n+1" message; no Info line of the mod.
  *Automated: `sleep.afternoon.default`.*
- [x] **T04 Window end at noon (default):** lie down when `time` shows 0.46-0.48 (about 11:00-11:30). Expected: wake
  at 18:00 of the same day, "Good evening". Then, another day, lie down when `time` shows 0.51-0.53. Expected: wake
  at 06:00 of the next day, "Good morning", "Day n+1" message, no Info line of the mod.
  *Automated: `sleep.window.noon`.*
- [x] **T05 Night unchanged:** at 0.80-0.95 and again at 0.05-0.20, sleep. Expected: vanilla (the next 06:00, "Good
  morning"), no Info line of the mod.
  *Automated: `sleep.night`, `sleep.night.late`.*
- [x] **T06 IncludeAfternoon on:** set it on. At 0.55-0.65 sleep → wake at 18:00 of the same day, "Good evening",
  Info line `a sleep started at ... now ends at 18:00`. At 0.68-0.69 sleep → 18:00 of the same day. At 0.72-0.74
  sleep → 06:00 of the next day, "Good morning", "Day n+1", no Info line. A morning sleep still ends at 18:00.
  Set it off again.
  *Automated: `sleep.afternoon.included`, `sleep.afternoon.window`.*
- [x] **T07 Bed checks kept in the morning:** in the morning, each in turn: wet (swim, then E on the bed), fire out
  (remove the campfire), roof removed, a Greyling (`spawn Greyling`) hunting you. Expected: the vanilla message each
  time ("You are wet", "The bed needs to be placed near a fire", "The bed must be placed under a roof", "There are
  enemies nearby"), no lying down. Last: E on a second bed of yours → "Spawn point set", no lying down (vanilla);
  then E on the setup bed until "Spawn point set" again. Rebuild the setup.
  *Automated: `sleep.bedchecks`.*
- [x] **T08 Cooldown:** right after T01 (nightfall), E on the bed within 30 s → "You can't sleep at this time"; after
  30 s → a night sleep to the next morning, "Good morning".
  *Automated: `sleep.cooldown`.*
- [x] **T09 Day after a night:** after a vanilla night sleep (wake at 06:00), wait 30 s; `time` shows "Can sleep";
  sleep → 18:00 of the same day, "Good evening" stays on screen (you are still Rested from the night sleep, so no
  Rested message follows it, as in vanilla).
  *Automated: `sleep.morning.rested`.*
- [x] **T10 Client permission:** in the morning `time` says "Can sleep" (vanilla: "Can NOT sleep"); within 30 s of
  waking up it says "Can NOT sleep".
  *Automated: `sleep.cansleep`.*
- [x] **T11 WakeUpHour:** set 21 → a morning sleep wakes at about 0.875, "Good evening". Set 13 → a morning sleep
  wakes at about 0.54. With IncludeAfternoon on and 13: a sleep at 0.52-0.53 goes to the next morning. Set 30 in the
  file → clamped to 23 (a morning sleep wakes at about 0.96). Restore 18 (and IncludeAfternoon off).
  *Automated: `sleep.wakehour`, `sleep.mp.dedicated`.*
- [x] **T12 WakeUpMessage:** lie down while still Rested (as in T09) so the text stays on screen. `$msg_goodnight` →
  "Good night" (in the game language); empty → "Good morning". Restore `Good evening`.
  *Automated: `sleep.wakemessage`.*
- [x] **T13 Live toggle:** in the morning, turn the mod off in the MC Mods panel → the bed says "You can't sleep at
  this time", `time` says "Can NOT sleep". Turn it on → a morning sleep works again, no restart.
  *Automated: `sleep.toggle`, `sleep.mp.client-off`.*
- [x] **T14 Off during a day sleep:** during the sleep screen, set `Enabled = false` (Esc → MC Mods if the menu opens
  while asleep, else edit the mod's `.cfg` file from outside the game; the game picks it up while running).
  Expected: the skip still ends at 18:00; the message is "Good morning"; no error.
  *Automated: `sleep.toggle.asleep`, `sleep.mp.client-off`.*
- [ ] **T15 `Enabled = false` + restart:** vanilla behaviour in the morning (refused) and in the afternoon (next
  morning); the log shows the mod "Off (disabled in settings)" and no patch of it.
  *Partly automated (`sleep.toggle`); by hand: Start the game with Enabled = false in the .cfg and check the log line
  "Off (disabled in settings)" and the vanilla beds: a self-test cannot run in a game where the mod is disabled from
  the start.*
- [x] **T16 Clean log:** no error or warning from the mod during T01-T14 (the day-cycle warning "Could not read the
  day cycle from the game" must not appear without a day-cycle mod).
  *Automated: `sleep.morning`, `sleep.presentation`, `sleep.afternoon.default`, `sleep.window.noon`, `sleep.night`,
  `sleep.night.late`, `sleep.afternoon.included`, `sleep.afternoon.window`, `sleep.bedchecks`, `sleep.cooldown`,
  `sleep.morning.rested`, `sleep.cansleep`, `sleep.wakehour`, `sleep.wakemessage`, `sleep.toggle`,
  `sleep.toggle.asleep`.*
- [ ] **T17 Automated in-world self-tests:** Debug build, game closed, run `./tools/Test-InWorld.ps1`. It loads a
  throwaway single-player world and runs this mod's tests. Each test records the messages the game shows during the
  wake-up and checks their order. Expected: `[selftest] PASS` lines and no `FAIL` for `sleep.morning` (08:00: the bed
  lets you lie down through the vanilla interaction, the skip aims at the wake-up hour of the same day, wake-up
  there; the wake-up shows the WakeUpMessage where vanilla shows "Good morning", then the Rested message; Rested),
  `sleep.night` (22:00: skip and wake-up at 06:00 of the next day; "Good morning", then the Rested message),
  `sleep.afternoon.default` (14:00, IncludeAfternoon forced off: next morning; "Good morning", then the Rested
  message) and `sleep.afternoon.included` (14:00, IncludeAfternoon forced on and WakeUpHour forced to 18: retargeted
  to 18:00 of the same day; the WakeUpMessage, then the Rested message). The forced settings live in memory only:
  the settings in `MC.Exploration.Sleep.ThroughDay.cfg` are the same after the run (check the values, not the file
  date: BepInEx rewrites every config file when the game starts), and the script prints "mod config files as before
  the run".
  Screenshots `fade` (screen going black) and `awake` (dusk after a daytime sleep, morning after the others) look
  right. The world time, spawn point, Rested and the player's position are put back after each test, also when a
  test fails while the player is asleep (the player is woken up first).
  *Partly automated (`sleep.morning`, `sleep.night`, `sleep.afternoon.default`, `sleep.afternoon.included`,
  `sleep.restore`); by hand: Look at the fade and awake screenshots (dusk after the daytime sleeps, morning after the
  others), and read the script's line "mod config files as before the run".*
- [x] **T18 Sleep in the first seconds after 06:00:** (automated by the self-test `sleep.dawn`; by hand it needs
  luck with the timing) at the end of the night, wait next to the bed until the world time has just passed 06:00
  while `time` still shows a fraction below 0.25 (the fraction trails the real time by a second or two), and lie down
  at once. Expected: the game starts the sleep as a night sleep, and the mod ends it at 18:00 of the same day
  instead of skipping a whole day; Info line `Day sleep: a sleep started at 06:0x by the game or another mod now ends
  at 18:00`; the wake-up shows "Good evening" (or the Rested message over it) and the Debug line says `daytime
  sleep`. A "Day n" message may appear around the wake-up (the dawn you slept through): not a failure.
  *Automated: `sleep.dawn`.*
- [x] **T19 Wake-up held past midnight:** (automated by the self-test `sleep.latewake`, which moves the end of a
  running time skip; the real case needs a host's dream video, see M11) `WakeUpHour` 23, a morning sleep whose
  wake-up only comes on the next day. Expected: woken before 05:00 of the next day (tested at 00:30 and 04:30) it
  still counts as a daytime sleep: "Good evening", Debug line `daytime sleep`. Woken at 05:00 or later (tested at
  05:30) it counts as a night sleep: "Good morning", Debug line `night sleep, vanilla message`. The 05:00 limit is
  only the boundary of the rule: no dream video is long enough to reach it in play.
  *Automated: `sleep.latewake`.*
- [x] **T20 Forced time of day (`tod`):** the only item that uses `tod`. At about 22:00 (`time` 0.90-0.93) run
  `tod 0.35` (forces the morning), then E on the bed. Expected: you lie down, but no sleep starts while the forced
  time lasts (wait 10 s); with Debug logging, one line `Day sleep not started: everyone is in bed, but the world time
  22:.. is outside the day window 06:00-12:00 (forced time of day?)`, not repeated. Run `tod -1` while still lying:
  within a few seconds a normal night sleep starts and you wake at 06:00 of the next day with "Good morning"; no
  `Day sleep:` Info line.
  *Automated: `sleep.tod`.*
- [!] **T21 Sleep without a time skip:** (automated by the self-test `sleep.noskip`, which stands in for another mod
  that makes the game's skip to the morning do nothing; no such mod is known, so it cannot be done by hand) a sleep
  that starts at about 20:00 and ends 2 s later on the same day, without any time skip. Expected: it stays a night
  sleep: "Good morning" (not "Good evening"), Debug line `night sleep, vanilla message`.
  **FAILED:** automated test: A sleep that starts after 06:00 and ends on the same date without a time skip is treated
  as a daytime sleep ("Good evening") (self-test `sleep.bug.noskip-evening`)
- [x] **T22 WakeUpMessage made of spaces only:** as T12, with `WakeUpMessage` set to a few spaces. Expected: it
  counts as empty: "Good morning" after a daytime sleep, Debug line `daytime sleep, WakeUpMessage empty, vanilla
  message kept`.
  *Automated: `sleep.wakemessage`.*

## 0.1.0 — multiplayer (needs a second player)

Host = the player who starts the world with "Start server". The results of M01, M05, M07, M08 and M10 also tick the
framework tests N02-N05 in `src/Shared/TESTING.md` (this is the first mod that needs the server too).

- [ ] **M01 Everybody in bed** (framework N02): host + one remote player, both with the mod; the remote player's MC
  Mods panel shows the mod Active. Morning: A lies down → nothing happens, A stays lying; B lies down → both sleep,
  both wake at 18:00 of the same day with "Good evening" and Rested.
  *Partly automated (`sleep.mp.day`, `sleep.sim.waiting`); by hand: A real second player on a host: A lies down and
  waits while B is really standing, then both screens fade and both wake at 18:00 with "Good evening" and Rested.*
- [x] **M02 Server's settings win:** host `WakeUpHour` 21, client 18 → both wake at 21:00 after a morning sleep. Host
  IncludeAfternoon on, client off → an afternoon sleep ends at 18:00 for both; host off, client on → an afternoon
  sleep goes to the next morning for both. Each player sees their own `WakeUpMessage`.
  *Automated: `sleep.mp.settings`, `sleep.mp.dedicated`.*
- [x] **M03 Remote wake-up message after a day-to-morning sleep:** default settings; host and remote player both lie
  down when `time` shows 0.55-0.65 → both wake at 06:00 of the next day and **both** see "Good morning", not "Good
  evening" (or the Rested message; each game's Debug line says `night sleep, vanilla message`). Optional (where the final-time message matters): repeat at about 0.52 with Seasons in summer, or with a
  skip-speed mod.
  *Automated: `sleep.mp.message`, `sleep.afternoon.default`.*
- [ ] **M04 Split start across the window end:** default settings; A lies down at about 0.47; B lies down only after
  `time` shows 0.51 or more → vanilla sleep to the next morning, "Good morning" for both, no "Good evening" for A.
  *Partly automated (`sleep.sim.split`); by hand: Two real players: B lies down only after noon on another machine;
  both see "Good morning".*
- [ ] **M05 Hand-off: player without the mod, up** (framework N05): modded host + a player without the mod. The
  host's log names the player who joined without the mod. Morning: that player gets "You can't sleep at this time";
  the host in bed stays lying, no skip. Afternoon (0.55-0.65), default settings: both lie down → both wake at the
  next morning (vanilla). Host IncludeAfternoon on: both lie down in the afternoon → both wake at 18:00; the player
  without the mod sees "Good morning" and gets Rested; no error on either side. Night: both sleep normally.
  *Partly automated (`sleep.mp.handoff`, `sleep.mp.client-off`); by hand: A game that really has no copy of the mod:
  the host's log names the player who joined without it; the host lying in bed in the morning stays lying; the default
  afternoon sleep and the night sleep with both players.*
- [ ] **M06 Hand-off: player without the mod in bed since before dawn:** modded host + a player without the mod. That
  player lies down at about 0.20 while the host stays up; wait until `time` shows 0.27 or more (the player is still
  lying), then the host lies down → both sleep; both wake at 18:00, that player with "Good morning" and Rested, the
  host with "Good evening"; no error.
  *Partly automated (`sleep.mp.handoff`, `sleep.sim.predawn`); by hand: A game that really has no copy of the mod
  together with a real host who lies down in the morning: host sees "Good evening", the other player "Good morning".*
- [ ] **M07 Player with the mod on a server without it** (framework N03): the panel shows "Inactive: the server does
  not have this mod"; the morning bed is refused; afternoon and night sleeps are normal (next morning); no error.
  Leave and load a single-player world: Active again.
  *Partly automated (`probe.mp.baseline`, `sleep.mp.vanilla-server`); by hand: On a server without the mod: an
  afternoon sleep and a night sleep (normal, next morning), no error over the session; then leave and load a
  single-player world: the mod is Active again.*
- [x] **M08 Host turns it off live** (framework N04): the remote player's status follows ("the server has this mod
  turned off"), their morning bed is refused; back on → Active, a morning sleep works.
  *Automated: `sleep.mp.server-off`.*
- [ ] **M09 A player has it off, the host on:** that player cannot lie down in the morning; the host in bed stays
  lying, no skip.
  *Partly automated (`sleep.mp.client-off`, `sleep.sim.waiting`); by hand: Both in one session: the host really lying
  in bed while the player with the mod turned off stands next to the bed.*
- [ ] **M10 Dedicated server** (mandatory; first mod that runs on `valheim_server.exe`): first update the Valheim
  Dedicated Server in Steam to 1.0.16: the one installed on the dev machine is 1.0.15 (read from its
  `assembly_valheim.dll`, 2026-09-29), and since both versions use network version 40, 1.0.16 players join it without
  any error, so the test would silently run against the older game. Its log must print `Valheim version: 1.0.16
  (network version 40)` at start. Then install BepInExPack Valheim in the dedicated server's folder (start the server through BepInEx as its readme says), copy the mod folder
  `MC.Exploration.Sleep.ThroughDay` (from the Nexus zip in `dist/nexus/` or the build output) to
  `BepInEx/plugins/MC_Valheim/Exploration/`, start the server once. Its `BepInEx/LogOutput.log` must show the
  `[MC:ready]` line for the mod, `Activated.`, and no error or warning from MC code. Stop it, set `WakeUpHour = 20`
  and `IncludeAfternoon = true` in its `BepInEx/config/MC.Exploration.Sleep.ThroughDay.cfg`, start it again. Two
  players with the mod join: both show the mod Active. Morning: both lie down → both wake at 20:00 (fraction about
  0.83) with "Good evening"; the `Day sleep:` Info line is in the server log. Afternoon (0.55-0.65): both wake at
  20:00 too.
  *Partly automated (`sleep.mp.dedicated`, `probe.mp.baseline`); by hand: Two players on the dedicated server (the
  test has one), and the manual install in the dedicated server's own folder (BepInExPack, the mod folder copied, the
  .cfg edited between two starts).*
- [ ] **M11 Late wake-up after a dream** (optional; host + one remote player, both with the mod; Debug logging on
  both): host `WakeUpHour` 23. On the host: `devcommands`, then `cinematicsleep ` and Tab to pick a dream video name
  (sets it for everybody's next sleep). Morning: both lie down. The host watches the dream video; the server wakes
  everybody only when it ends, while the world time goes on (in single player the game pauses during the video, so
  this only happens with other players online). If the wake-up comes after midnight (`time` shows Day n+1 below
  0.21), both games' Debug lines still say `daytime sleep` (message "Good evening" or the Rested message, never
  "Good morning"). If the video ends before midnight, the item cannot show the case: mark it `[-]` with the clock
  seen. (unverified: how long the dream videos last)
  *Partly automated (`sleep.latewake`); by hand: The real case: a host with another player online watches a dream
  video (cinematicsleep) while the world time runs on past midnight; both games' Debug lines say daytime sleep.*

## 0.1.0 — compatibility (optional, needs another mod)

- [x] **X01 Creature Kill and Tame Counts (MC)** (postfixes `Player.Message`, where this mod swaps "Good morning" for
  "Good evening"): with it installed, T09's "Good evening" shows, no tame is counted, no error.
  *Automated: `sleep.compat.counts`.*
- [x] **X02 Batch Station Feeding and Loot Pickup Filter (MC)** (both prefix `Player.Interact`, the path of E on a
  bed): with both installed, T01 works (claim, spawn point, lying down), no batch hint on the bed, no error.
  *Automated: `sleep.compat.interact`.*
- [ ] **X03 BedRules 2.0.6 at its default settings** ("Ignore Time Restrictions" on): a morning sleep → wake at 18:00
  of the same day (retarget Info line), not the next morning; an afternoon sleep with IncludeAfternoon off → next
  morning; with IncludeAfternoon on → 18:00; a night sleep → next morning. Then turn BedRules' "Ignore Time
  Restrictions" off: BedRules refuses the morning bed; with IncludeAfternoon on, an afternoon sleep still ends at
  18:00.
  *Partly automated (`sleep.compat.anytime`); by hand: The real BedRules 2.0.6: its own bed interaction and settings,
  including "Ignore Time Restrictions" off (BedRules then refuses the morning bed).*
- [ ] **X04 SkipSleep** with ratio 0.5, two players: one in bed in the morning → the day sleep starts for both, both
  wake at 18:00.
  *Partly automated (`sleep.compat.ratio`); by hand: The real SkipSleep mod with ratio 0.5 and two players.*
- [ ] **X05 Seasons with a non-default night length:** a morning sleep wakes you at the moment `time` flips to night
  in that season (fraction about 0.75); "Good evening"; no day-cycle warning in the log.
  *Partly automated (`sleep.compat.daycycle`); by hand: The real Seasons mod with a non-default night length.*
