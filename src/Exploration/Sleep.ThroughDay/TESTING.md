# Sleep Through the Day — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). Build OK (Debug and Release, 0 warnings). Smoke test (`./tools/Test-Smoke.ps1 -Mod ThroughDay`) passed on
the build before the review fixes (loads, patches apply, JIT check clean); to re-run (the wake-up message now patches
`Player.Message`). In-world self-tests (T17) last run 2026-09-29 on that earlier build: `sleep.morning` and
`sleep.afternoon.included` passed; `sleep.night` and `sleep.afternoon.default` failed on the wake-up message check (the
test expected "Good morning" to be the last message, but vanilla's Rested message comes after it). The wake-up message
code and the tests changed since: to re-run. No in-game test yet.

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

- [ ] **T01 Morning sleep ends at nightfall:** morning (`time` 0.27-0.45, "Can sleep"), note Day n. E on the bed.
  Expected: the character lies down; within 2 s the screen fades to black, "ZZZ" (sometimes a dream text); about
  12 s later you wake up with Rested in the status effects and the center message **"You feel rested (Comfort:
  N)"** if you were not Rested when you lay down (it covers "Good evening", see Setup), else **"Good evening"**;
  Debug line `daytime sleep, message "Good evening" instead of "Good morning"` either way; no "Day n+1" message; 10 s later `time` shows Day n (same) and 0.75-0.77. Info line
  `Day sleep: everyone is in bed at ... Waking up at 18:00`.
- [ ] **T02 Same presentation as vanilla:** compare T01 with a night sleep (T05): same lying animation, fade, ZZZ,
  skip length, autosave line in the log (`Saved recently, skipping sleep save.` or a save). Optional: repeat T01 on a
  Dragon Bed (`piece_bed02`) and an Ashwood Bed (`ashwood_bed`) (unverified that they behave like the Bed).
- [ ] **T03 Afternoon sleep stays vanilla by default:** IncludeAfternoon off (default). At 0.55-0.65, sleep.
  Expected: vanilla: wake at 06:00 of Day n+1, "Good morning", "Day n+1" message; no Info line of the mod.
- [ ] **T04 Window end at noon (default):** lie down when `time` shows 0.46-0.48 (about 11:00-11:30). Expected: wake
  at 18:00 of the same day, "Good evening". Then, another day, lie down when `time` shows 0.51-0.53. Expected: wake
  at 06:00 of the next day, "Good morning", "Day n+1" message, no Info line of the mod.
- [ ] **T05 Night unchanged:** at 0.80-0.95 and again at 0.05-0.20, sleep. Expected: vanilla (the next 06:00, "Good
  morning"), no Info line of the mod.
- [ ] **T06 IncludeAfternoon on:** set it on. At 0.55-0.65 sleep → wake at 18:00 of the same day, "Good evening",
  Info line `a sleep started at ... now ends at 18:00`. At 0.68-0.69 sleep → 18:00 of the same day. At 0.72-0.74
  sleep → 06:00 of the next day, "Good morning", "Day n+1", no Info line. A morning sleep still ends at 18:00.
  Set it off again.
- [ ] **T07 Bed checks kept in the morning:** in the morning, each in turn: wet (swim, then E on the bed), fire out
  (remove the campfire), roof removed, a Greyling (`spawn Greyling`) hunting you. Expected: the vanilla message each
  time ("You are wet", "The bed needs to be placed near a fire", "The bed must be placed under a roof", "There are
  enemies nearby"), no lying down. Last: E on a second bed of yours → "Spawn point set", no lying down (vanilla);
  then E on the setup bed until "Spawn point set" again. Rebuild the setup.
- [ ] **T08 Cooldown:** right after T01 (nightfall), E on the bed within 30 s → "You can't sleep at this time"; after
  30 s → a night sleep to the next morning, "Good morning".
- [ ] **T09 Day after a night:** after a vanilla night sleep (wake at 06:00), wait 30 s; `time` shows "Can sleep";
  sleep → 18:00 of the same day, "Good evening" stays on screen (you are still Rested from the night sleep, so no
  Rested message follows it, as in vanilla).
- [ ] **T10 Client permission:** in the morning `time` says "Can sleep" (vanilla: "Can NOT sleep"); within 30 s of
  waking up it says "Can NOT sleep".
- [ ] **T11 WakeUpHour:** set 21 → a morning sleep wakes at about 0.875, "Good evening". Set 13 → a morning sleep
  wakes at about 0.54. With IncludeAfternoon on and 13: a sleep at 0.52-0.53 goes to the next morning. Set 30 in the
  file → clamped to 23 (a morning sleep wakes at about 0.96). Restore 18 (and IncludeAfternoon off).
- [ ] **T12 WakeUpMessage:** lie down while still Rested (as in T09) so the text stays on screen. `$msg_goodnight` →
  "Good night" (in the game language); empty → "Good morning". Restore `Good evening`.
- [ ] **T13 Live toggle:** in the morning, turn the mod off in the MC Mods panel → the bed says "You can't sleep at
  this time", `time` says "Can NOT sleep". Turn it on → a morning sleep works again, no restart.
- [ ] **T14 Off during a day sleep:** during the sleep screen, set `Enabled = false` (Esc → MC Mods if the menu opens
  while asleep, else edit the mod's `.cfg` file from outside the game; the game picks it up while running).
  Expected: the skip still ends at 18:00; the message is "Good morning"; no error.
- [ ] **T15 `Enabled = false` + restart:** vanilla behaviour in the morning (refused) and in the afternoon (next
  morning); the log shows the mod "Off (disabled in settings)" and no patch of it.
- [ ] **T16 Clean log:** no error or warning from the mod during T01-T14 (the day-cycle warning "Could not read the
  day cycle from the game" must not appear without a day-cycle mod).
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

## 0.1.0 — multiplayer (needs a second player)

Host = the player who starts the world with "Start server". The results of M01, M05, M07, M08 and M10 also tick the
framework tests N02-N05 in `src/Shared/TESTING.md` (this is the first mod that needs the server too).

- [ ] **M01 Everybody in bed** (framework N02): host + one remote player, both with the mod; the remote player's MC
  Mods panel shows the mod Active. Morning: A lies down → nothing happens, A stays lying; B lies down → both sleep,
  both wake at 18:00 of the same day with "Good evening" and Rested.
- [ ] **M02 Server's settings win:** host `WakeUpHour` 21, client 18 → both wake at 21:00 after a morning sleep. Host
  IncludeAfternoon on, client off → an afternoon sleep ends at 18:00 for both; host off, client on → an afternoon
  sleep goes to the next morning for both. Each player sees their own `WakeUpMessage`.
- [ ] **M03 Remote wake-up message after a day-to-morning sleep:** default settings; host and remote player both lie
  down when `time` shows 0.55-0.65 → both wake at 06:00 of the next day and **both** see "Good morning", not "Good
  evening" (or the Rested message; each game's Debug line says `night sleep, vanilla message`). Optional (where the final-time message matters): repeat at about 0.52 with Seasons in summer, or with a
  skip-speed mod.
- [ ] **M04 Split start across the window end:** default settings; A lies down at about 0.47; B lies down only after
  `time` shows 0.51 or more → vanilla sleep to the next morning, "Good morning" for both, no "Good evening" for A.
- [ ] **M05 Hand-off: player without the mod, up** (framework N05): modded host + a player without the mod. The
  host's log names the player who joined without the mod. Morning: that player gets "You can't sleep at this time";
  the host in bed stays lying, no skip. Afternoon (0.55-0.65), default settings: both lie down → both wake at the
  next morning (vanilla). Host IncludeAfternoon on: both lie down in the afternoon → both wake at 18:00; the player
  without the mod sees "Good morning" and gets Rested; no error on either side. Night: both sleep normally.
- [ ] **M06 Hand-off: player without the mod in bed since before dawn:** modded host + a player without the mod. That
  player lies down at about 0.20 while the host stays up; wait until `time` shows 0.27 or more (the player is still
  lying), then the host lies down → both sleep; both wake at 18:00, that player with "Good morning" and Rested, the
  host with "Good evening"; no error.
- [ ] **M07 Player with the mod on a server without it** (framework N03): the panel shows "Inactive: the server does
  not have this mod"; the morning bed is refused; afternoon and night sleeps are normal (next morning); no error.
  Leave and load a single-player world: Active again.
- [ ] **M08 Host turns it off live** (framework N04): the remote player's status follows ("the server has this mod
  turned off"), their morning bed is refused; back on → Active, a morning sleep works.
- [ ] **M09 A player has it off, the host on:** that player cannot lie down in the morning; the host in bed stays
  lying, no skip.
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
- [ ] **M11 Late wake-up after a dream** (optional; host + one remote player, both with the mod; Debug logging on
  both): host `WakeUpHour` 23. On the host: `devcommands`, then `cinematicsleep ` and Tab to pick a dream video name
  (sets it for everybody's next sleep). Morning: both lie down. The host watches the dream video; the server wakes
  everybody only when it ends, while the world time goes on (in single player the game pauses during the video, so
  this only happens with other players online). If the wake-up comes after midnight (`time` shows Day n+1 below
  0.21), both games' Debug lines still say `daytime sleep` (message "Good evening" or the Rested message, never
  "Good morning"). If the video ends before midnight, the item cannot show the case: mark it `[-]` with the clock
  seen. (unverified: how long the dream videos last)

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **X01 Creature Kill and Tame Counts (MC)** (postfixes `Player.Message`, where this mod swaps "Good morning" for
  "Good evening"): with it installed, T09's "Good evening" shows, no tame is counted, no error.
- [ ] **X02 Batch Station Feeding and Loot Pickup Filter (MC)** (both prefix `Player.Interact`, the path of E on a
  bed): with both installed, T01 works (claim, spawn point, lying down), no batch hint on the bed, no error.
- [ ] **X03 BedRules 2.0.6 at its default settings** ("Ignore Time Restrictions" on): a morning sleep → wake at 18:00
  of the same day (retarget Info line), not the next morning; an afternoon sleep with IncludeAfternoon off → next
  morning; with IncludeAfternoon on → 18:00; a night sleep → next morning. Then turn BedRules' "Ignore Time
  Restrictions" off: BedRules refuses the morning bed; with IncludeAfternoon on, an afternoon sleep still ends at
  18:00.
- [ ] **X04 SkipSleep** with ratio 0.5, two players: one in bed in the morning → the day sleep starts for both, both
  wake at 18:00.
- [ ] **X05 Seasons with a non-default night length:** a morning sleep wakes you at the moment `time` flips to night
  in that season (fraction about 0.75); "Good evening"; no day-cycle warning in the log.
