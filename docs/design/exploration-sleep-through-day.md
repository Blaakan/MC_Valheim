# Sleep Through the Day — design

| | |
|---|---|
| Mod | Sleep Through the Day |
| GUID / project | `MC.Exploration.Sleep.ThroughDay` (`src/Exploration/Sleep.ThroughDay/`, root namespace `MC.Exploration.SleepThroughDayMod`) |
| Category / scope | Exploration / QoL |
| Side | Both: the server (or the host) and every player install it (3.9); multiplayer Compatible; network version 1 (no RPC and no ZDO key of its own). **The first Both mod in the repo** (3.9, M10) |
| Sheet idea | `Sleep through the day` |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`), decompiled `assembly_valheim` in `.ref/`; English strings checked in the installed 1.0.16 localization table (`valheim_Data/resources.assets`); bed and fire prefab names checked in `StreamingAssets/SoftRef/manifest_extended`; the sources of BedRules (including its config defaults), Sleepover, SkipSleep, AFKManager and Seasons read on GitHub (2026-09-29) |
| Status | Implemented (0.1.0), build OK, smoke test passed (before the review fixes; to re-run); in-world self-tests to re-run after the review fixes (first run: 2 of 4 failed on a wrong message expectation, fixed); in-game tests pending |

## Goal

Requirements from the user (2026-09-29), made precise:

1. **Sleeping during the day works.** The game counts 06:00 to 18:00 as day (`EnvMan.IsDay`). When the player uses (E)
   their own bed that is their current spawn point during the day, the character lies down exactly as at night,
   provided every other vanilla bed rule passes: roof and cover, a fire nearby, not wet, no enemy sensing the player,
   30 s since the last wake-up. In the **morning** (06:00 to 12:00) vanilla answers "You can't sleep at this time":
   the mod lifts that refusal. In the afternoon vanilla already lets the player lie down.
2. **The sleep skips the day instead of the night.** When everybody online is in bed, the vanilla sleep sequence runs:
   fade to black, "ZZZ", sometimes a random dream text (or a pending dream cinematic), then the 12-second time skip.
   Time moves forward to **nightfall of the same day** (18:00, the moment the game switches to night; configurable,
   decision 2) instead of the next morning. By default this applies to every sleep that starts in the **morning**,
   from 06:00 until noon (decision 1: `IncludeAfternoon` is **off by default**, a lead decision flagged to the user);
   with `IncludeAfternoon` on, also to sleeps that start in the afternoon, until **one in-game hour before the wake-up
   hour** (17:00 by default). Players wake up at nightfall with the vanilla wake-up effects (Rested, the sleep
   statistic, the autosave) and the message "Good evening" instead of "Good morning" (decision 3).
3. **Everything else is vanilla.** Same presentation, same multiplayer rule (everybody must be in bed; a player who
   stays up blocks the skip), same guards (10 s between two sleeps on the server, 30 s cooldown after waking up), same
   bed checks and messages. Sleeping at night (18:00 to 06:00) and, by default, in the afternoon (noon to 18:00) still
   skips to the next morning (with `IncludeAfternoon` on, only the last in-game hour before the wake-up hour does). A
   day sleep does not change the date (except when a host's dream video holds the wake-up past midnight, 3.6): no
   "Day N" message, no "day survived".
4. The mod can be turned off on its own, live, like every MC mod (framework toggle): off = vanilla beds.

Items 1 and 2 are the user's behaviour 1 ("when it's day time, it plays the sleep animation and moves the time to the
night"); items 2 and 3 are behaviour 2 ("like the vanilla sleep, but skip the day instead of the night"). Item 4 is an
MC rule for every mod.

Non-goals: a second bed action or a changed hover text such as "Rest until evening" (the backlog's suggestion;
decision 10); relaxing any bed check (sleep anywhere, without fire, while wet, in other players' beds); partial or
vote-based sleeping (other mods do it, 3.11); changing the day length, the night length or the skip speed; sleeping
until a later day; a hint message when a player without the mod blocks a day sleep (open question 4).

---

## 1. Vanilla behaviour (code trace)

### 1.1 The day clock (`EnvMan`)

- `EnvMan.FixedUpdate` reads the world's **net time** (`m_totalSeconds = ZNet.instance.GetTimeSeconds()`, seconds, a
  double), takes the **raw fraction** of the current day, `(t mod m_dayLengthSec) / m_dayLengthSec`, maps it through
  `RescaleDayFraction` (private) and moves `m_smoothDayFraction` towards it with `Mathf.LerpAngle(..., 0.01)` once per
  rendered frame. The smoothed value therefore trails the raw one by about 100 rendered frames (1-2 s at 60 fps, more
  at low frame rates), and after a jump it needs several seconds to catch up (after a 12-s skip, wait about 10 s).
  `GetDayFraction()` returns the smoothed value. The sun angle is `360 * fraction - 90`, so this fraction reads like a
  24-hour clock: 0.25 = 06:00 (sunrise), 0.5 = noon, 0.75 = 18:00 (sunset). `m_dayLengthSec` is public (code default
  1200 s; the scene value is **1800 s**, printed by the mod's Info line in the in-world self-test run of 2026-09-29;
  the mod never needs it as a constant). `c_MorningL = 0.15` (private const).
- `RescaleDayFraction(float)`: raw 0.15-0.85 → 0.25-0.75 (the day, 70 % of the real day length); raw below 0.15 →
  0-0.25 and raw above 0.85 → 0.75-1 (the night, 30 %). It is a pure function; mods change it (Seasons, section 2).
- At the end of `FixedUpdate` the static flags are recomputed **from the smoothed fraction**: `s_isDay`
  (`CalculateDay`: 0.25 ≤ f ≤ 0.75), `s_isAfternoon` (0.5 ≤ f ≤ 0.75), `s_isNight` (f ≤ 0.25 or f ≥ 0.75), then
  `s_canSleep = CalculateCanSleep()` last, so it sees fresh flags. The public static getters `IsDay()`, `IsAfternoon()`,
  `IsNight()`, `CanSleep()` only return these fields. Both ends are inclusive: at exactly 0.25 or 0.75 two flags are
  true at once.
- Lighting: `FixedUpdate` blends the night, day, morning and evening environment colours with weights computed from the
  smoothed fraction and `m_sunHorizonTransitionH`/`L` (public, code defaults 0.08 and 0.02; scene values
  **unverified**). With the code defaults the day weight reaches 0 at 0.75, the evening weight peaks at 18:00 and
  reaches 0 at 0.76, and from about **18:15** (0.76) the lighting is 100 % night. On a 1200-s day that is about 7 real
  seconds after 18:00 (about 11 s on the game's 1800-s day).
- Date: `GetDay(double t) = (int)(t / m_dayLengthSec)` (public, also `GetDay()` for now): the day index changes at
  midnight (raw 0). `GetMorningStartSec(int day) = day * len + len * 0.15` (06:00; computed in `float`).
- `FixedUpdate` returns at once when it already ran in the same rendered frame (`s_lastFrame`), so the clock, the flags
  and the time skip (1.4) advance once per rendered frame.
- `EnvMan` also runs on a dedicated server: `Game.UpdateSleeping` reads its flags there.

Clock table (vanilla mapping; used by the tests, which read the fraction with the `time` command, 1.8):

| Clock | Fraction (`GetDayFraction`, `time`) | Raw fraction of `m_dayLengthSec` | What happens there |
|---|---|---|---|
| 00:00 | 0.00 | 0.000 | Date changes (`GetDay`); night |
| 06:00 | 0.25 | 0.150 | `IsDay` starts, `IsNight` ends; vanilla sleep wakes you here (`GetMorningStartSec`); "Day N" (1.6); **the mod's day window opens** |
| 12:00 | 0.50 | 0.500 | `IsAfternoon` starts: vanilla allows sleeping again; **the day window closes here by default** (`IncludeAfternoon` off), or when `WakeUpHour` is 13 |
| 13:00 | 0.542 | 0.558 | Earliest wake-up hour allowed by this mod |
| 17:00 | 0.708 | 0.792 | The day window closes here with `IncludeAfternoon` on and the default wake-up hour (one hour before it) |
| 18:00 | 0.75 | 0.850 | `IsNight` starts, `IsDay`/`IsAfternoon` end; evening music (1.6); this mod's default wake-up; latest end of the day window |
| 18:15 | 0.76 | 0.856 | Full night lighting (code defaults, above) |
| 20:00 | 0.833 | 0.900 | |
| 21:00 | 0.875 | 0.925 | |
| 23:00 | 0.958 | 0.975 | Latest wake-up hour allowed by this mod |

So the morning (06:00-12:00) lasts 35 % of the real day length, the whole day (06:00-18:00) 70 %, the night 30 %.
0.01 of the displayed fraction lasts about 17 real seconds during the day and 7 s at night on a 1200-s day (25 s and
11 s on the game's 1800-s day).

### 1.2 Lying down: `Bed.Interact` (runs on the player's own game)

- `Bed.Interact(Humanoid human, bool repeat, bool alt)` ignores repeats and `alt`. Unclaimed bed → `CheckExposure`,
  claim, set spawn ("Spawn point set"). Own bed that is not the current spawn → `CheckExposure`, set spawn ("Spawn
  point set"; no lying down, no refusal). **Own current bed** (`IsCurrent`: its spawn point is within 1 m of the
  profile's custom spawn point) → in this order: `EnvMan.CanSleep()` (else `$msg_cantsleep`, **"You can't sleep at
  this time"**), `CheckEnemies` (`Player.IsSensed()` → "There are enemies nearby"), `CheckExposure`
  (`Cover.GetCoverForPoint`: not under a roof → "The bed must be placed under a roof", cover below 0.8 → "The bed is
  too exposed"), `CheckFire` (`EffectArea.IsPointInsideArea(..., EffectArea.Type.Heat)` → "The bed needs to be placed
  near a fire"), `CheckWet` (`Wet` status effect → "You are wet"); then `human.AttachStart(m_spawnPoint, bed,
  hideWeapons: true, isBed: true, onShip: false, "attach_bed", (0, 0.5, 0))`. Another player's bed: nothing.
- `EnvMan.CalculateCanSleep()` (private, called once per frame): `IsAfternoon() || IsNight()` **and**, when there is a
  local player, `now > Player.m_wakeupTime + m_sleepCooldownSeconds` (public double, code default 30). With no local
  player (dedicated server) only the time window counts. `CanSleep()` is read only by `Bed.Interact` and the `time`
  console command. **So in vanilla the morning is the only time a player cannot lie down**; in the afternoon they can,
  and the sleep goes to the next morning (1.3, 1.4).
- `Player.AttachStart(...)` sets `m_attached`, starts the `attach_bed` animation (`m_zanim.SetBool`, synced to
  everybody) and writes `ZDOVars.s_inBed` on the player's ZDO (owned by that player; the server reads it, 1.3).
  `Player.AttachStop()` clears it and does nothing while `m_sleeping`. `Player.SetControls` calls `AttachStop` on any
  movement, attack, block, jump or crouch input, so a player gets up by moving. **Nothing gets a player up when the
  time of day changes**: a player who lay down at night is still in bed after 06:00. `Player.InBed()` reads the ZDO
  (`GameCamera` uses it for the bed camera).
- `Bed.GetHoverText`: "<owner>'s Bed [E] Sleep" (`$piece_bed_sleep`) on your current bed at any time of day. Bed pieces
  in 1.0.16: `bed` ("Bed"), `piece_bed02` ("Dragon Bed"), `ashwood_bed` ("Ashwood Bed"), all with the `Bed` component
  (the component on the two newer beds is **unverified**: prefab data).

### 1.3 The server loop: `Game.UpdateSleeping`

- `Game.Start` registers the routed RPCs `SleepStart` and `SleepStop` everywhere and, **only on the server**,
  `InvokeRepeating("UpdateSleeping", 2f, 2f)`. `UpdateSleeping` (private) also returns at once when
  `!ZNet.instance.IsServer()`.
- If `m_sleeping` (private bool, not saved): when `!EnvMan.instance.IsTimeSkipping() && !CinematicsManager.IsPlaying()`
  → `m_lastSleepTime = now` (private double), `m_sleeping = false`, routed `SleepStop` to everybody
  (`ZRoutedRpc.Everybody = 0`).
- Else it starts a sleep when **all** of: `!IsTimeSkipping()`, `EnvMan.IsAfternoon() || EnvMan.IsNight()` (the server's
  own flags), `EverybodyIsTryingToSleep()`, and `now - m_lastSleepTime ≥ 10` → `EnvMan.instance.SkipToMorning()`,
  `m_sleeping = true`, routed `SleepStart`.
- `EverybodyIsTryingToSleep()` (private): `ZNet.GetAllCharacterZDOS()` = the host's own character (if any) plus the
  character of every **ready** peer; false when that list is empty; true only when every ZDO has `s_inBed`.
- `Game.m_sleeping` is used nowhere else. **A vanilla server never starts a sleep in the morning**, even when every
  player lies in bed (possible with another mod's "sleep any time", or for a player who lay down before dawn, 1.2):
  the morning part of this mod has to run on the server.
- The 06:00 boundary: the start test uses the smoothed flags, which trail the raw time (1.1). In the first second or two
  after 06:00 the flags can still say night while the raw time is past 06:00; a sleep that starts then gets a
  `SkipToMorning` target of the **next** day's 06:00, a whole day (vanilla behaviour; this mod retargets it, 3.4
  step 3).

### 1.4 The time skip

- `EnvMan.SkipToMorning()` (public): `day = GetDay(now - len * 0.15)`, target `GetMorningStartSec(day + 1)`: from any
  time between 06:00 and midnight → the next day's 06:00; from midnight to 06:00 → the same day's 06:00. It sets the
  private fields `m_skipTime = true`, `m_skipToTime = target`, `m_timeSkipSpeed = (target - now) / 12`
  (`c_TimeSkipDuration = 12.0`, private const) and logs `Time <now>, day:<d>    nextm:<target>  skipspeed:<s>`. Called
  only by `Game.UpdateSleeping` and the console command `sleep` (1.8).
- `EnvMan.UpdateTimeSkip(float dt)` (private, from `FixedUpdate`, **server only**): `now += dt * m_timeSkipSpeed`,
  clamped to `m_skipToTime` (then `m_skipTime = false`), `ZNet.SetNetTime(now)`. `IsTimeSkipping()` returns
  `m_skipTime`. `m_skipToTime` and `m_timeSkipSpeed` keep their last values after a skip. With `dt =
  Time.fixedDeltaTime` at most once per rendered frame, the skip lasts about 12 s while the frame rate is at least the
  physics rate, longer below it (vanilla behaviour).
- Net time: `ZNet.UpdateNetTime` advances it every fixed step on clients, and on the server only while at least one
  player is connected (`GetNrOfPlayers() > 0`). The server sends it to every ready peer every 2 s
  (`ZNet.SendPeriodicData` → private `SendNetTime` → `NetTime` RPC on each peer's `ZRpc`; the client's `RPC_NetTime`
  just sets its clock) and saves it in the world file. A client's clock can therefore trail the server's by up to one
  2-s sync; during a skip that is up to `2 × speed` game seconds.
- `SleepStop` is sent from the server's own 2-s `UpdateSleeping` timer, which is independent of the 2-s `NetTime`
  timer, so it can reach a remote client **before** the final time: at wake-up that client's clock can still be up to
  `2 × speed` behind. In vanilla this only shortens the remote client's 30-s cooldown (`m_wakeupTime` is set from the
  late clock). The mod closes the gap (3.3, 3.6).
- Everything that runs on net time moves forward with a skip: plants, smelters, kilns, fermenters, cooking stations
  (food can burn), respawn timers. A vanilla night skip covers at most 30 % of a day length (18:00 → 06:00); a skip from
  early morning to nightfall covers up to 70 %.

### 1.5 Sleeping and waking on each client

- `Game.SleepStart` (RPC handler) → `Player.m_localPlayer.SetSleeping(true)`. `Game.SleepStop` →
  `SetSleeping(false)`, `AttachStop()`, then a profile and world save if `m_saveTimer > 60` (else the log line "Saved
  recently, skipping sleep save."), then `WearNTear.OnSleep()` on every loaded piece (the owner sets `AddPreSnow`).
- `Player.SetSleeping(bool sleep)` (public; only called from `SleepStart`/`SleepStop` in vanilla, and the only place
  `m_sleeping` changes): acts only on a change. On waking up: `Message(Center, "$msg_goodmorning")` ("Good morning"),
  adds the `Rested` status effect (`SEMan.s_statusEffectRested`, time reset), `m_wakeupTime = now` (starts the 30-s
  cooldown of 1.2), `IncrementPlayerStat(PlayerStatType.Sleep)`. Adding Rested: if the player already has it,
  `SEMan.Internal_AddStatusEffect` only calls `ResetTime()` (no message); otherwise the new effect's
  `SE_Rested.Setup` shows `Message(Center, "$se_rested_start ($se_rested_comfort:N)")` ("You feel rested
  (Comfort:1)" in the in-world run log), which **replaces "Good morning" in the same frame** (next bullet). So in
  vanilla the player sees "Good morning" only when they were still Rested when they lay down; usually they see the
  Rested message.
- While `m_sleeping`: `Player.InCutscene()` is true (no control); `Hud.UpdateBlackScreen` fades the loading screen in
  over `Game.m_fadeTimeSleep` (code default 3 s) and shows `m_sleepingProgress`; `SleepText.OnEnable` fades "ZZZ" in
  and out (2 s), at 4 s calls `CinematicsManager.OnSleep()` (plays a pending dream cinematic, set through
  `CinematicsManager.SetDreamCinematic` by a boss kill (`Character.m_dreamCinematic`) or a boss stone with its trophy,
  "You will dream tonight") or else asks `DreamTexts.GetRandomDreamText`, which returns a text **only by chance**
  (`Random.value ≤ m_chanceToDream`, code default 0.1 per text; scene values **unverified**), and at 5 s calls
  `Game.CollectResourcesCheck`. The server's stop waits for `!CinematicsManager.IsPlaying()` (1.3), i.e. for a video
  playing on the server's own game (the host). `CinematicsManager.Play` calls `Game.Pause()`, which really pauses
  (time scale 0: no world time, no `UpdateSleeping` tick) only when `Game.CanPause()`: single player, or a host with no
  other player connected. With other players online the world time runs on at normal speed while the host's video
  plays, so the stop, and the wake-up, come later than the skip target (3.6). Console: `cinematicsleep <name>` (cheat)
  sets the dream video for everybody's next sleep.
- `Player.Message` (for the owner) → `MessageHud.ShowMessage(type, msg, ..., log: false)`: for type Center the text
  field is **overwritten** (plus a fade entry), so a second Center message in the same frame replaces the first before
  it is drawn. The text is localized there (`$tokens` work). Nothing is shown while the player hid the HUD, and these
  messages are not written to the message log.

### 1.6 Day and night side effects

- `EnvMan.UpdateTriggers` (only with a local player): when the smoothed fraction crosses 0.25 in a small step →
  `OnMorning`: morning music, "$msg_newday" ("Day N") and `Player.SurvivedOneDay` (consecutive-days statistic); when it
  crosses 0.75 → `OnEvening`: evening music. A skip that ends at 18:00 crosses 0.75 as the smoothed value catches up
  (evening music) and never crosses 0.25 (no "Day N").
- Night content: `SpawnSystem`, `CreatureSpawner` and `LootSpawner` test `m_spawnAtDay`/`m_spawnAtNight` against
  `IsDay()`/`IsNight()`; `MonsterAI` despawns `DespawnInDay` creatures while `IsDay()`; environments with
  `m_isColdAtNight`/`m_isFreezingAtNight` use `!IsDay()`; the Ashlands day heat uses `IsDay()`. Waking up at nightfall
  therefore brings night creatures and night cold, as expected.

### 1.7 Strings (1.0.16 English, `resources.assets` localization table)

| Token | English | Used by |
|---|---|---|
| `$msg_goodmorning` | Good morning | `Player.SetSleeping(false)` |
| `$msg_goodnight` | Good night | **nothing in the game code**; translated in every language column except Hindi and Serbian |
| `$msg_cantsleep` | You can't sleep at this time | `Bed.Interact` |
| `$msg_spawnpointset` | Spawn point set | `Bed.Interact` |
| `$piece_bed_sleep` | Sleep | `Bed.GetHoverText` |
| `$msg_newday` | Day $1 | `EnvMan.OnMorning` |
| `$msg_bedwet`, `$msg_bedenemiesnearby`, `$msg_bedneedroof`, `$msg_bedtooexposed`, `$msg_bednofire` | You are wet / There are enemies nearby / The bed must be placed under a roof / The bed is too exposed / The bed needs to be placed near a fire | `Bed` checks |
| `$se_rested_name` | Rested | status effect |
| `$se_rested_start`, `$se_rested_comfort` | You feel rested / Comfort (shown as "You feel rested (Comfort:1)") | `SE_Rested.Setup` (1.5) |
| `$message_dream` | You will dream tonight | boss stone |

There is no "Good evening" token.

### 1.8 Console commands useful for testing (`Terminal`)

- `devcommands` enables cheats (the `time` command is hidden behind it).
- `time`: prints `<net seconds> sec, Day: <day> (<GetDayFraction, 0.00>), Can sleep | Can NOT sleep, Session start:
  ...`. The day comes from the raw net time, the fraction is the **smoothed** one (1.1: after a skip, wait about 10 s
  before reading it). "Can sleep" is `EnvMan.CanSleep()` of the machine that runs it (so it reflects this mod's client
  patch).
- `sleep` (cheat, server/host): `EnvMan.SkipToMorning()` only: skips to 06:00 **without** `Game.m_sleeping`, the sleep
  screen or a wake-up.
- `skiptime [gameseconds]` (cheat, server/host; default 240): adds to the net time at once.
- `tod <0-1>` / `tod -1`: forces the **displayed** time of day (`m_debugTimeOfDay`) and therefore the flags, without
  moving the net time. Misleading for this mod's tests (the flags and the real time disagree): never use it, and run
  `tod -1` if it was used.

### 1.9 Who runs what (multiplayer summary)

| Step | Runs on | State |
|---|---|---|
| `Bed.Interact`, `EnvMan.CanSleep`, cooldown | each player's own game | local flags, `Player.m_wakeupTime` |
| "in bed" | player's ZDO → everybody | `ZDOVars.s_inBed` |
| `Game.UpdateSleeping`, `EnvMan.SkipToMorning`, `UpdateTimeSkip` | server or host only | `Game.m_sleeping`, `m_lastSleepTime`, `EnvMan.m_skipTime`/`m_skipToTime`/`m_timeSkipSpeed` (none saved) |
| Net time | server → clients every 2 s (`NetTime`); saved with the world | `ZNet.m_netTime` |
| `SleepStart`/`SleepStop` | routed RPCs, server → everybody, over the same per-peer `ZRpc` as `NetTime` | none |
| `SetSleeping`, message, Rested, cooldown | each client for its own player | local player |

---

## 2. Existing mods and what they teach

From `docs/research/existing-mods-exploration.md` (2026-09-28) plus the sources read on GitHub (2026-09-29):

| Mod | How (source read where marked) | Lesson |
|---|---|---|
| [BedRules](https://thunderstore.io/c/valheim/p/TastyChickenLegs/BedRules/) (TastyChickenLegs), 2.0.6, 1.0 tagged | Source `TastyChickenLegs/BedRules` (last push 2024-01-16; the 2.0.6 package may differ, **unverified**). `Patches/BedRulesConfigs.cs`: "Ignore Time Restrictions" (`General` / `Ignoretimerestrictions`, "Sleep at any time of day") **defaults to true**. `Patches/BedRulesPatch.cs`: with that option on, a `Game.UpdateSleeping` **prefix that returns false** replaces the loop (no time condition, no 10-s guard, no `m_lastSleepTime`, no wait for a dream cinematic) and calls `EnvMan.SkipToMorning()`; a `Bed.Interact` prefix (`Priority.High`, always returns false) replaces the vanilla interaction with its own time check (`IsAfternoon || IsNight` unless the option is on, no cooldown) and toggleable checks; `Bed.GetHoverText` postfix. | "Sleep any time" means a full skip **to the next morning** (now verified), and it is on by default: every BedRules user is affected by decision 5. Replacing `Bed.Interact` bypasses `EnvMan.CanSleep`: with such a mod, its own time rule decides at the bed. A replacing prefix on `UpdateSleeping` still lets our postfix run and see the sleep it started. |
| [Sleepover](https://thunderstore.io/c/valheim/p/Azumatt/Sleepover/) (Azumatt), 1.1.4, deprecated | Source [AzumattDev/Sleepover](https://github.com/AzumattDev/Sleepover) `Patches.cs` (last push 2024-11-07): same `UpdateSleeping` replacing prefix (`SkipToMorning` at any time when `SleepAnyTime` is on), `Bed.Interact` replacing prefix (`Priority.VeryHigh`) plus prefixes on `Bed.IsCurrent`, `CheckExposure`, `CheckEnemies`... "May be buggy". Needs the server and every client. | Same as BedRules. Its "server and all clients" requirement has the same cause as ours (1.3, 1.2). We keep every check. |
| [Sleep Options](https://www.nexusmods.com/valheim/mods/2716) (Nexus) | Toggles the night, enemy, exposure, fire and wet checks (research survey). | Same bed area; we change only the time rule. |
| [SkipSleep](https://thunderstore.io/c/valheim/p/R1NS3/SkipSleep/) (R1NS3) | Source [RinseV/valheim-skipsleep](https://github.com/RinseV/valheim-skipsleep): `Game.EverybodyIsTryingToSleep` prefix returning false (sleeping ratio). | Our server code **calls** `EverybodyIsTryingToSleep`, so ratio mods of this kind apply to day sleep too. |
| AFKManager (Torokal) | Source `src/SleepIntegration.cs`: relax-only postfix on `Game.EverybodyIsTryingToSleep` (AFK players do not block). | Same: works for day sleep through that call. |
| [LetMeSleep](https://thunderstore.io/c/valheim/p/Blockheim/LetMeSleep/), [SleepyTime](https://thunderstore.io/c/valheim/p/TeamWhupass/SleepyTime/), [SleepSkip](https://thunderstore.io/c/valheim/p/Azumatt/SleepSkip/) (vote popup with a timeout; its GitHub repository `AzumattDev/SleepSkip` returns 404), NowYouSleep | Partial or vote sleeping, server side; hooks not read (**unverified**). | Not established. A vote mod that starts the skip from its own vote handler (outside `UpdateSleeping`) would not be retargeted: its morning sleep would skip to the next morning. A vote mod that opens a vote only when `IsAfternoon`/`IsNight` would never let a morning sleep start. Only a mod that hooks `EverybodyIsTryingToSleep` is known to work (3.11). |
| [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) (shudnal) | Source [shudnal/Seasons](https://github.com/shudnal/Seasons) `SeasonState/EnvManPatches.cs`: `Priority.First` prefixes that replace `EnvMan.RescaleDayFraction` (per-season **night length**, `nightLength` default 30 %; summer 15 %), `GetMorningStartSec` and `SkipToMorning` (its own 12-s skip to its own morning), and a prefix on `GetDay`, `FixedUpdate`, `SkipToMorning`... that writes a per-season `m_dayLengthSec`. | Never hardcode 1200, 0.15, 0.85 or 12: compute 06:00, the window end and nightfall through the live `RescaleDayFraction` and day length, call `GetDay` before reading `m_dayLengthSec`, and call `SkipToMorning` then retarget, so its skip keeps its speed. Its short summer night (morning 90 s after midnight on a 1200-s day) is why the wake-up message must not depend on a late client clock (3.6). |
| [ControlTime](https://thunderstore.io/c/valheim/p/Ujhik/ControlTime/) (Ujhik) | Forced time of day, automatic night skip, cycle length (description only). | A forced time of day drives the flags, not the net time (edge cases). |

Nobody offers "sleep until the evening": the "sleep any time" mods all skip to the next morning. Conclusion: keep the
vanilla flow and every vanilla rule, add only (a) permission to lie down in the morning on the client and (b) on the
server, a start in the morning plus a new end (the same day's nightfall) for every sleep that starts in the day window
(the morning; the afternoon too with `IncludeAfternoon` on), built on the vanilla calls so that ratio, AFK, season and
skip-speed mods keep working.

---

## 3. Design

### 3.1 Core idea

Three small hooks, no RPC of its own, no ZDO key, nothing saved:

1. **Client: lying down in the morning.** A postfix on `EnvMan.CalculateCanSleep` turns `false` into `true` in the
   morning when the vanilla 30-s cooldown has passed (only where there is a local player). `Bed.Interact` is untouched,
   so every other check and message stays vanilla. In the afternoon vanilla already allows lying down.
2. **Server: the day sleep.** A prefix/postfix pair on `Game.UpdateSleeping`. The **day window** is measured in raw
   world time: from 06:00 until noon by default (`IncludeAfternoon` off); with `IncludeAfternoon` on, until one in-game
   hour before the wake-up hour, never later than 18:00. After vanilla's own decision for this 2-s tick:
   - if a sleep has just started inside the window (vanilla in the afternoon when `IncludeAfternoon` is on, a "sleep
     any time" mod, or vanilla in the first second after 06:00 while its flags still say night), **retarget** its time
     skip to the wake-up hour of the same day;
   - if nothing started because it is the morning (the only time vanilla refuses), and everybody is in bed (vanilla
     `EverybodyIsTryingToSleep`) and 10 s passed since the last sleep, start the sleep **exactly as vanilla does**
     (`SkipToMorning`, `m_sleeping = true`, `SleepStart`) and retarget it.

   Vanilla's stop branch ends every sleep. Just before it does, the prefix sends the final world time to the clients
   (vanilla's own `NetTime` message), so every client wakes up with the right clock.
3. **Client: the wake-up message.** A prefix/postfix pair on `Player.SetSleeping` remembers the date and whether the
   sleep started after 06:00 (raw time); waking up on the same date (or just after midnight, 3.6) means a day sleep.
   Then, inside that wake-up call only, a prefix on `Player.Message` swaps vanilla's "$msg_goodmorning" for
   "Good evening" at its source, so vanilla's order stays: the wake-up text, then the Rested message (1.5).

### 3.2 Clock helpers (`DayClock`)

- `IsMorning()` = `!EnvMan.IsAfternoon() && !EnvMan.IsNight()`: exactly where vanilla's time test (`IsAfternoon() ||
  IsNight()`, used by both `CalculateCanSleep` and `UpdateSleeping`) fails, i.e. the smoothed fraction strictly between
  0.25 and 0.5. Used on the client (may I lie down?) and on the server (will vanilla start this itself?), each time
  against the vanilla test it complements.
- `ClockText(float fraction)` → `"HH:MM"` (fraction × 24 h), for logs; `HourText(hour)`; `FractionAt(env, t)` = the
  game clock of raw time `t` through the live `RescaleDayFraction` (`GetDay` first), for logs and the self-tests.
- `WakeHour(float configured)`: the setting clamped to 13-23 (BepInEx clamps too), NaN or infinity → 18.
- `RawFractionForHour(EnvMan env, float hour)`: the raw fraction at which the game's clock shows `hour`, i.e. the
  smallest `r` in [0, 1] with `env.RescaleDayFraction(r) ≥ hour / 24`, found by bisection (32 steps) on the **live**
  method (private, publicized; pure in vanilla and in Seasons). Checks first that `RescaleDayFraction(0) ≤ target ≤
  RescaleDayFraction(0.99999)` and that the function does not decrease between the two ends; otherwise falls back to
  the vanilla inverse (`f < 0.25 → f × 0.6`, `f ≤ 0.75 → 0.15 + (f − 0.25) × 1.4`, else `0.85 + (f − 0.75) × 0.6`) and
  logs one Warning per session: "Could not read the day cycle from the game (another mod changed it?); using the
  vanilla day and night lengths." In vanilla: 06:00 → 0.15, 17:00 → 0.792, 18:00 → 0.85.
- `Today(EnvMan env, double now, float wakeHour, bool includeAfternoon)`: `day = env.GetDay(now)` **first** (Seasons
  writes its `m_dayLengthSec` from a prefix on `GetDay`), then `len = (double)env.m_dayLengthSec`, `start = day * len`,
  and (all in double):
  - `Dawn = start + RawFractionForHour(6) * len`;
  - `LatestStart = start + RawFractionForHour(includeAfternoon ? Min(wakeHour − 1, 18) : 12) * len`;
  - `Target = start + RawFractionForHour(wakeHour) * len`;
  - `InWindow(now) = Dawn ≤ now ≤ LatestStart`.

  It returns a `DayWindow` (readonly struct: `Day`, `DayLength`, `DayStart`, `Dawn`, `LatestStart`, `Target`,
  `LatestStartHour`, `WakeHour`); a day length of 0 throws (caught by the patch, before any state change).
  The range 13-23 of `wakeHour` keeps `LatestStart` at noon or later (so the window always covers the whole morning,
  where the client lets players lie down) and `Target` inside the same date.
- `IsAfterDawn(EnvMan env, double t)`: `t ≥ GetDay(t) * len + RawFractionForHour(6) * len` (client, 3.6).

### 3.3 Patches

All bodies catch their own exceptions and call `PatchGuard.Report(site, e)`. Applied only while the feature is Active
(framework: enabled, and on a client only when the server answered "on").

| Target | Type | What it does | Why |
|---|---|---|---|
| `EnvMan.CalculateCanSleep()` (private, no overload) | Postfix | Return if `__result` is true, or if `Player.m_localPlayer == null` (dedicated server, loading screen: left exactly as vanilla computed it). If `DayClock.IsMorning()` and `ZNet.instance.GetTimeSeconds() > player.m_wakeupTime + __instance.m_sleepCooldownSeconds`: `__result = true`. Never turns `true` into `false`. | The one place the bed asks "may I sleep now?"; `Bed.Interact` stays vanilla with all its checks and messages. Only a local player's bed needs the morning permission; on a dedicated server `CanSleep()` stays vanilla, so no third-party server code sees a changed value. Per-frame: cheap reads first. |
| `Game.UpdateSleeping()` (private) | Prefix, `void`, `[HarmonyPriority(Priority.First)]` | `__state = __instance.m_sleeping` (was a sleep running when this tick began?). On the server, when `m_sleeping && !EnvMan.instance.IsTimeSkipping()` (a stop is due this tick, by vanilla or a replacing mod): `ZNet.instance.SendNetTime()` (private, publicized). Changes nothing else. | First, so it records the state before another mod's replacing prefix (BedRules, Sleepover) starts a sleep. The `NetTime` message goes over the same per-peer `ZRpc` as the routed `SleepStop` that follows (`ZRoutedRpc.RouteRPC` → `peer.m_rpc.Invoke`), and a `ZRpc` connection keeps message order (NetworkGate relies on the same property), so every client reads the final time when it wakes up (3.6). |
| `Game.UpdateSleeping()` | Postfix, `[HarmonyPriority(Priority.Low)]` | `DaySleep.AfterTick(__instance, __state)` (3.4). | A postfix runs even when another mod's prefix skipped the original; it sees vanilla's (or the other mod's) decision for this tick. |
| `Player.SetSleeping(bool)` | Prefix, `void` | `__state = __instance.m_sleeping`; `WakeMessage.Before(__instance, sleep)` (3.6): for the local player, asleep, with `sleep == false`, decides "day sleep?" and, if so and `WakeUpMessage` is not empty, arms the swap text. Any other call disarms. | Lets the postfix see whether the call really changed the state; the decision needs only the start state and the current time, which does not move inside the call. |
| `Player.Message(MessageType, string, int, Sprite, bool)` (override declared in `Player`) | Prefix, `void`, `ref string msg` | Returns at once unless a swap is armed (one static read). While armed: for the local player, type Center and `msg == "$msg_goodmorning"`, sets `msg` to the `WakeUpMessage` text, once. | Swaps the text at its source, inside vanilla's own call: vanilla's Rested message (1.5) still comes after it, as after "Good morning". Hooked on `Player`, not `MessageHud`, so mods that mute or filter at `MessageHud.ShowMessage` see our text like any other message. |
| `Player.SetSleeping(bool)` | Postfix | `WakeMessage.After(__instance, __state)` (3.6): disarms first; then acts only when `__state != __instance.m_sleeping`: remembers the start, or logs the wake-up (which text was shown) and records it for the Debug self-tests. Shows no message. | Reading the state the call produced (not the `sleep` argument) stays right if another mod's prefix skips the original. A message shown here would come **after** the Rested message and hide it (the first implementation did that; review fix). |

No transpiler. Not patched on purpose: the static getters `IsDay`/`IsNight`/`IsAfternoon`/`CanSleep` (tiny, may be
inlined by Mono, and read by spawns, cold, music and heat), `Bed.Interact`, `EnvMan.SkipToMorning` (called, not
patched).

### 3.4 Server: starting and retargeting a day sleep (`DaySleep`)

`AfterTick(Game game, bool wasSleeping)`, every 2 s on the server or host:

1. Return if `wasSleeping` (a sleep was already running: the stop branch owns it), or unless `ZNet.instance != null &&
   ZNet.instance.IsServer()` and `EnvMan.instance != null`. `justStarted = game.m_sleeping && env.IsTimeSkipping()`
   (someone started a sleep in this tick). Return when `!justStarted && !DayClock.IsMorning()` (an afternoon or night
   tick where nothing new happened: vanilla has decided; the cheap exit of most ticks).
2. `now = ZNet.instance.GetTimeSeconds()`. In the morning path (`!justStarted`), vanilla's other start conditions are
   checked first, cheapest first: `!game.m_sleeping && !env.IsTimeSkipping() && now - game.m_lastSleepTime ≥ 10.0 &&
   ZRoutedRpc.instance != null && game.EverybodyIsTryingToSleep()`, else return. Then `w = DayClock.Today(env, now,
   WakeHour(WakeUpHour), IncludeAfternoon)` (both settings read through `Plugin.ReadDaySettings`, the one place that
   reads them; the Debug build lets the self-tests force them there in memory, 3.16); if `!w.InWindow(now)` → return (in the morning path: Debug line, once until
   the next sleep, when the raw time is before 06:00 or after 18:00 although the flags say morning and everybody is in
   bed: a forced time of day, `tod`).
3. **Someone started a sleep in this tick** (`justStarted`) and its end is later than ours (`env.m_skipToTime >
   w.Target`): `Retarget(env, now, w.Target)`, Info line (3.12). This covers:
   - vanilla's own afternoon start (flags afternoon; only while `IncludeAfternoon` is on, since the window then reaches
     past noon);
   - a "sleep any time" mod's start in the morning (BedRules at its default settings, Sleepover), or in the afternoon
     with `IncludeAfternoon` on;
   - vanilla's start in the first second or two after 06:00, while its smoothed flags still say night (1.3): it would
     skip a whole day; the raw time is already in the window, so it ends at nightfall instead.
4. **Start a day sleep where vanilla never starts** (morning path, all conditions of step 2 met, raw time in the
   window): `env.SkipToMorning()`; then, **only if `env.IsTimeSkipping()`** (another mod's `SkipToMorning` may
   deliberately start no skip), `Retarget(env, now, w.Target)`, else a Debug line and no retarget (the sleep then runs
   without a skip, as a vanilla night sleep would with that mod); the Info text is built; then `game.m_sleeping = true`,
   `ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStart")`, Info line. Everything that can throw (the
   window, the call to another mod's patched methods, the log text) happens before `m_sleeping` and the RPC, so a
   failure leaves at worst a plain skip, never a player stuck asleep.
   In the afternoon step 4 never starts anything: vanilla's time test passes there, so vanilla (or a mod that replaced
   the loop) has already decided, and a mod that chose not to start keeps that choice.
5. The end is vanilla: when the skip is done (and no dream cinematic plays), `UpdateSleeping`'s stop branch sets
   `m_lastSleepTime`, clears `m_sleeping` and sends `SleepStop`, right after the prefix sent the final time.

`Retarget(env, now, target)` (callers have checked `IsTimeSkipping()`; it never switches a skip on):
`duration = (env.m_skipToTime - now) / env.m_timeSkipSpeed` as `SkipToMorning` (vanilla or a patched one) just set it;
if not a finite number in (0, 600] s → 12 (`c_TimeSkipDuration`). Then, if `target > now` (always true inside the
window; a guard), `env.m_skipToTime = target`, `env.m_timeSkipSpeed = (target - now) / duration`. The skip keeps the length the game (or a skip-speed or season mod)
chose; only its end moves. Calling `SkipToMorning` first also keeps every other mod's patch on it running (house rule
"call the vanilla method").

### 3.5 Client: lying down in the morning

- With the patch, `EnvMan.CanSleep()` is true in the morning once 30 s passed since the last wake-up. `Bed.Interact`
  then runs its vanilla checks and `AttachStart`: the player lies down, `s_inBed` goes to the server. In the afternoon
  nothing changes on the client: vanilla already allows it.
- The client does not know the server's settings and does not need to: the day window always covers the whole morning
  (the wake-up hour is at least 13:00, so the window closes at noon at the earliest), so a morning sleep is always a day
  sleep.
- A player lying in bed waits like at night: nothing happens until everybody is in bed; moving gets them up (vanilla).
  If the window closes before everybody is in bed (noon by default; one hour before the wake-up hour, 17:00 by default,
  with `IncludeAfternoon` on), the sleep that then starts is a vanilla one, to the next morning. The rule looks at the
  moment the sleep **starts**.
- The hover text stays "[E] Sleep" (decision 10).

### 3.6 Client: the wake-up message (`WakeMessage`)

- **Day-sleep wake-up** (`IsDaySleepWake(now)`): the sleep started after 06:00 of its date (`startedAfterDawn`) and
  the wake-up is on the same date (`GetDay(now) == startDay`), **or** on the next date before 05:00 (`GetDay(now) ==
  startDay + 1 && FractionAt(now) < 5/24`, `LateWakeLimitHour`).
- `Before(player, sleep)` (SetSleeping prefix): disarms; then, only for `player == Player.m_localPlayer` (Unity `==`)
  when `player.m_sleeping && !sleep` (a wake-up is about to happen): if it is a day-sleep wake-up and `WakeUpMessage`
  is not empty (after `Trim`), arms `SwapText = WakeUpMessage`.
- `Swap(player, type, ref msg)` (Player.Message prefix, only while armed): for the local player, type Center and
  `msg == "$msg_goodmorning"`, not yet swapped in this call: `msg = SwapText` (MessageHud localizes it, so `$tokens`
  work). Vanilla then adds Rested and, when Rested is new, shows its start message, which replaces ours in the same
  frame exactly as it replaces "Good morning" in vanilla (1.5). The player therefore sees "Good evening" when they
  were still Rested as they lay down, and the Rested message otherwise: vanilla's behaviour with our text.
- `After(player, wasSleeping)` (SetSleeping postfix): disarms; then only for the local player and when `wasSleeping !=
  player.m_sleeping` (a real change):
  - sleep started (`player.m_sleeping` now true): remember `startDay = EnvMan.instance.GetDay(now)` and
    `startedAfterDawn = DayClock.IsAfterDawn(env, now)`; Debug line.
  - woke up (now false): Debug line saying which text was shown (ours instead of "Good morning", vanilla kept because
    `WakeUpMessage` is empty, vanilla for a night sleep, or "no Good morning to replace" when another mod changed or
    skipped it); `SleepSelfTests.RecordWake` (Debug build); clear the remembered state.
- Why this rule: a sleep that starts after 06:00 and is not a day sleep (vanilla, or another mod's) ends at a later
  date's morning, at 06:00 or later; a vanilla sleep that ends on the same date started after midnight, before 06:00. A
  day sleep starts after 06:00 and normally ends the same date (target at most 23:00). It can end later when the
  server's stop is held: on a host with other players online, a dream video on the host keeps the stop waiting while
  the world time runs on (1.5), and with `WakeUpHour` 23 the wake-up can pass midnight (23:00 to midnight is 45 real
  seconds on the 1800-s day, 30 s on a 1200-s day, less under Seasons' short summer night). The next-date window up to
  05:00 keeps such a wake-up a day sleep, and stays an in-game hour away from the 06:00 end of every other sleep. Both
  ends read the **raw** world time, not the flags, so a sleep retargeted in the first second after 06:00 (flags still
  night) still reads as a day sleep, and the client never needs the server's `WakeUpHour` or `IncludeAfternoon`.
- Why swap at the source: the first implementation showed `WakeUpMessage` from the postfix, i.e. after the Rested
  message, which it then hid (the in-world self-test run caught the vanilla order). Swapping inside the call keeps
  every vanilla message and its order.
- The clock is right at both ends: at the start the client's clock is at most a normal sync behind (no skip yet), and at
  the wake-up the server has just sent the final time ahead of `SleepStop` on the same connection (3.3). Without that,
  a remote client's clock could still be up to `2 × speed` behind at wake-up (1.4), and a sleep that started in the
  day but ends at the next morning could read as a day sleep whenever that lag reaches back before 05:00 (the late
  wake-up window above; before that window existed, back before midnight): for example an afternoon sleep from 12:00
  with `IncludeAfternoon` off (the default) under Seasons' summer night (morning 90 s after midnight on a 1200-s day,
  lag up to about 113 s), or with a 6-s skip-speed mod (lag up to about 258 s). The player would then see "Good
  evening" at dawn. With the final time sent first, the lag at wake-up is zero.
- Side effect, also for night sleeps: a remote client's `m_wakeupTime` is now set from the final time, so its 30-s
  cooldown after any sleep is a real 30 s (in vanilla it often passes at once, 1.4).
- The state is a few static fields per game (one local player): the start (`startDay`, `startedAfterDawn`) and the
  swap (`SwapText`, `swapped`, set only inside one wake-up call); the postfix and every `SetSleeping` prefix disarm
  the swap, and `Plugin.OnDeactivated` clears everything.

### 3.7 Configuration

Section `General`, `Enabled` first (framework) and `Status` (framework).

| Section | Setting | Type / default | Description (user-facing) |
|---|---|---|---|
| General | Enabled | bool `true` | (framework) Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | string, read-only | (framework) Written by the mod: shows whether the feature is active, and if not, why. Editing it has no effect. |
| General | WakeUpHour | float `18`, range 13-23 (`AcceptableValueRange`, values outside are clamped) | In-game hour at which you wake up after sleeping during the day. 18 = nightfall, when the game switches to night (vanilla sleep wakes you at 6). Range 13 to 23; decimals allowed (18.5 = 18:30, 21 = 9 PM). Sleeps that start in the morning (06:00 to noon) wake you at this hour; with IncludeAfternoon on, sleeps that start in the afternoon do too, until one in-game hour before this hour. Every other sleep skips to the next morning as usual. In multiplayer the server's (or host's) setting is used. (Order 90) |
| General | IncludeAfternoon | bool **`false`** (lead decision, D1) | Off (default): only sleeps that start in the morning (06:00 to noon) wake you at the wake-up hour; afternoon sleeps skip to the next morning, as in vanilla. On: sleeps that start in the afternoon also wake you at the wake-up hour, until one in-game hour before it. In multiplayer the server's (or host's) setting is used. (Order 85) |
| General | WakeUpMessage | string `Good evening` | Message shown when you wake up from a daytime sleep, instead of "Good morning". As with "Good morning", the game's "You feel rested" message replaces it at once if you were not Rested when you lay down. Game text keys work: `$msg_goodnight` shows the game's own "Good night" in your language. Leave empty to keep "Good morning". (Order 80) |

All are read when used (no cache), so edits apply at the next sleep. `WakeUpHour` and `IncludeAfternoon` matter only
where the server runs and are read only through `Plugin.ReadDaySettings` (Debug build: in-memory test overrides,
3.16); `WakeUpMessage` only on each player's game.

### 3.8 Data, state and persistence

- Saved: nothing. No ZDO key, no item or player custom data, no world data. The time jump itself is the vanilla net
  time, saved by vanilla.
- Server: `__state` of the `UpdateSleeping` patches (per tick) and one static "Debug line already written" flag.
  Vanilla fields written: `Game.m_sleeping` (as vanilla), `EnvMan.m_skipToTime`/`m_timeSkipSpeed` (as `SkipToMorning`
  does). None of them is saved by the game.
- Client: `WakeMessage.startDay`/`startedAfterDawn` and the swap state `SwapText`/`swapped` (static; cleared on use,
  at every `SetSleeping` call and in `OnDeactivated`).
- Network: no RPC and no ZDO key of its own; `ModNetworkVersion` 1. Traffic: the framework handshake
  (`<GUID>.Hello`/`HelloAck`) and one extra vanilla `NetTime` message per sleep end (every 2 s while a dream cinematic
  holds the stop).

### 3.9 Multiplayer and hand-off

Why Both: the start of a sleep is decided only on the server (`Game.UpdateSleeping`, 1.3), and whether a player may lie
down is decided only on that player's game (`Bed.Interact`, 1.2). With the mod on the server alone nobody can lie down
in the morning (with `IncludeAfternoon` on, afternoon sleeps would still end at nightfall); with the mod on a client alone the player would lie in
bed forever in the morning (a vanilla server never starts a morning sleep). The framework's `NetworkGate` makes the
client part inactive when the server lacks the mod.

**First Both mod.** No MC mod so far is `Both` or `Server`: the framework's Both paths (handshake, "the server does not
have this mod", live follow, host notice: framework tests N02-N05 in `src/Shared/TESTING.md`, all unticked) and loading
on `valheim_server.exe` (Client mods get `BepInProcess("valheim.exe")`, Both mods none, so this DLL, the framework and
`JitCheck` load in the dedicated server for the first time) have never run. `Test-Smoke.ps1` and the Debug deploy
cover the game client only. The safety claims below depend on that code; M01, M05, M07, M08 and M10 exercise it and
their results tick N02-N05. Checked at implementation (not run): the generated `ModInfo.g.cs` has no `BepInProcess`
attribute (`Directory.Build.targets` emits it only for `ModSide` Client), so BepInEx loads the DLL in any process;
`NetworkGate.Install` patches the `ZNet` connection methods for Both mods (`NeedsServer`), and on the server
`NetworkGate.Check` returns "may run" (`IsServer()`); the installed dedicated server's `assembly_valheim.dll` (a
different build from the client's, dated 2026-09-25) contains every member name the mod and its Debug self-tests use
(string search only, not a signature check). That installed dedicated server is **Valheim 1.0.15**, not 1.0.16
(`Version..cctor` builds `GameVersion(1, 0, 15)`; the client's builds `(1, 0, 16)`; both have `c_networkVersion` 40,
read with Mono.Cecil on 2026-09-29), so 1.0.16 clients join it without any error: M10 starts by updating it to 1.0.16
in Steam (its log prints `Valheim version: ...` at start, `FejdStartup.Awake`). The dedicated server has no BepInEx
installed yet.

| Setup | Result |
|---|---|
| Single player | Works (the game is its own server). |
| Host (with the mod) + players with the mod | Works; the **host's** `WakeUpHour` and `IncludeAfternoon`; each player's own `WakeUpMessage`. |
| Dedicated server with the mod + players with the mod | Expected to work, not run yet (M10): the mod loads on `valheim_server.exe`; the client patch leaves `CanSleep` alone there (no local player, 3.3); the **server's** `WakeUpHour` and `IncludeAfternoon`. |
| Server with the mod + a player **without** it (hand-off) | **Morning**: that player cannot lie down ("You can't sleep at this time"), so while they are up nobody day-sleeps: the players with the mod who lie down stay in bed until they get up, as at night when someone stays up. Exception: a player without the mod who lay down before 06:00 (at night, while someone was still up) is still in bed in the morning (1.2); when the others lie down, everybody sleeps and that player wakes up at nightfall with the vanilla "Good morning" and Rested (M06). **Afternoon**: by default (`IncludeAfternoon` off) vanilla for everyone (next morning); with it on, they lie down as usual (vanilla allows it) and, when everybody is in bed, everybody wakes up at nightfall, that player with "Good morning" and Rested. **Night**: vanilla for everyone. The host's log names the player who joined without the mod (framework). Nobody gets stuck: the vanilla client handles `SleepStart`/`SleepStop` with its own vanilla code. |
| Server **without** the mod + a player with it | The mod is inactive on that player's game ("Inactive: the server does not have this mod..."): vanilla beds, no player stuck in bed. |
| Server has the mod turned off | Players' copies go inactive and follow the server live (framework). |
| A player has it turned off, the server on | That player cannot lie down in the morning: blocks morning sleeps like a player without the mod. |
| Partial/vote sleep mod on the server | A mod that hooks `EverybodyIsTryingToSleep` (SkipSleep, AFKManager) applies to day sleep too; players not in bed get the sleep screen, as that mod does at night. Others: **unverified** (3.11). |

The mod changes shared world state (the time), but only the way vanilla sleep does: on the server, through the vanilla
sleep flow, when everybody agreed by lying down.

`ModMultiplayerNotes` (player-facing, as in the csproj): "Install it on the server (or the host) and on every player's
game: the server moves the time, and each game must allow lying down in the morning. As with vanilla sleep, time only
skips when every player online is in bed. A player without the mod cannot lie down in the morning, so they block
morning sleep like a player who stays up at night. The wake-up hour and the afternoon setting of the server (or host)
apply to everyone. On a server without the mod it turns itself off."

`ModDescription`: "Sleep in the morning too: lie down in your bed and wake up at nightfall, with the usual sleep screen,
time skip and Rested bonus. Night sleeps, and by default afternoon sleeps, still skip to the next morning."

### 3.10 Live toggle

| When | Effect |
|---|---|
| Turned off, nobody in bed | Patches removed: the morning bed answers "You can't sleep at this time" again (`s_canSleep` is recomputed every frame); with `IncludeAfternoon` on, afternoon sleeps go to the next morning again (server). |
| Turned off on a player's game while they lie in bed in the morning | They stay lying (vanilla lets them) until they move. If the server still starts a day sleep (everybody in bed), they sleep and wake up at nightfall with "Good morning" (message patch gone). |
| Turned off on the server during a day sleep | The skip was already set in `EnvMan`: it ends at nightfall and vanilla's stop branch wakes everybody (without the extra `NetTime` message). Players' copies go inactive (framework), so they see "Good morning". |
| Turned on | The client patch applies from the next frame, the server patch from the next 2-s tick. Nothing to create (`OnActivated` only registers the Debug self-tests, 3.16). |
| `Enabled = false` + restart | Not patched at all: vanilla. |

### 3.11 Compatibility

| Other mod patching... | Interaction |
|---|---|
| `Game.UpdateSleeping` with a replacing prefix (BedRules, Sleepover "sleep any time"; **on by default in BedRules**) | Our first-priority prefix records the state (and sends the time before their stop), their prefix runs, our postfix runs (postfixes run even when the original was skipped). Their start inside the day window (the morning; the afternoon too with `IncludeAfternoon` on) is retargeted to the wake-up hour (3.4 step 3); their night sleeps and their sleeps after the window are untouched; their stop branch ends the sleep. So with both mods at their default settings, **every morning sleep** a BedRules user takes ends at nightfall instead of the next morning (decision 5); their afternoon sleeps still go to the next morning unless `IncludeAfternoon` is on. A replacement that ignored `Game.m_sleeping` would never stop vanilla's own night sleeps either, so it is not our concern. |
| `Bed.Interact` with a replacing prefix (BedRules, Sleepover) | They do not call `EnvMan.CanSleep`: their own time rule decides whether a player may lie down in the morning (BedRules also skips the 30-s cooldown). With their "sleep any time" on (BedRules default), players can lie down in the morning and the sleep ends at nightfall. With it off, their own check refuses the morning, so this mod cannot let anyone lie down then; with `IncludeAfternoon` on, afternoon sleeps still end at nightfall. README note. |
| `Game.EverybodyIsTryingToSleep` (SkipSleep, AFKManager, other ratio mods) | We call it: their rule applies to day sleep. |
| Vote or partial-sleep mods with other hooks (LetMeSleep, SleepyTime, SleepSkip, NowYouSleep) | **Unverified**: a mod that starts the skip from its own vote handler (outside `UpdateSleeping`) is not retargeted, so its morning sleep skips to the next morning; a mod that opens its vote only in the afternoon or at night never lets a morning sleep start. README note. |
| `EnvMan.SkipToMorning` (Seasons, skip-speed mods) | We call it, then keep the duration it chose and move only the end. If it starts no skip, we do not force one (3.4 step 4). |
| `EnvMan.RescaleDayFraction`, `m_dayLengthSec` (Seasons night length and day length, day-length mods) | 06:00, the window end and nightfall are computed through the live method and field (3.2), so "18:00" is the moment their night starts. |
| `EnvMan.CalculateCanSleep` / `CanSleep` (other "sleep any time" mods) | Our postfix only turns false into true in the morning, for a local player; theirs may do more. |
| `Player.SetSleeping` (wake-up message or buff mods) | Our prefix only decides and arms the swap, our postfix only records and logs; neither shows a message. A mod that replaces the whole method (prefix returning false) keeps its own messages: nothing is swapped, the Debug line says so. |
| `Player.Message` / `MessageHud.ShowMessage` (message filters, translators, loggers) | Our `Player.Message` prefix changes one argument (`"$msg_goodmorning"` → `WakeUpMessage`) during one call per day-sleep wake-up; every other message passes untouched after a single static read. Their patches see our text instead of the token. |
| Forced time of day (`tod`, ControlTime) | Flags follow the forced time, the day window follows the real time. Forced afternoon or night during the real day: vanilla starts, we retarget (real time in the window). Forced morning during the real evening or night: players may lie down but nobody starts a sleep (Debug line) until the forced time ends. |

MC mods: none patches the sleep methods. **Creature Kill and Tame Counts** postfixes `Player.Message`, where our
prefix swaps the wake-up text: its postfix sees "Good evening" and only parses tame messages ("...
$hud_tamedone"), so ours is ignored (X01). **One Click Repair All** and **Batch Station Feeding** prefix
`MessageHud.ShowMessage` but mute Center messages only while their own repair or feeding loop runs, never during a
wake-up. **Batch Station
Feeding** and **Loot Pickup Filter** prefix `Player.Interact`, the path of the E press on a bed: a bed is neither a
feed target nor a pickup, so they pass it to vanilla (checked in their code; X02 repeats T01 with both installed). The
other MC mods (including Encyclopedia and Breeding Star Inheritance, in progress at implementation time) touch no sleep
or time state.

### 3.12 Logging

- Server, Info (start): `Day sleep: everyone is in bed at 08:24 (day 12). Waking up at 18:00: skipping 1008 s of world
  time in 12 s (day length 1800 s).` (08:24 = raw 0.29, 18:00 = raw 0.85 of the game's 1800-s day)
- Server, Info (retarget): `Day sleep: a sleep started at 14:10 by the game or another mod now ends at 18:00 instead of
  the next morning.`
- Server, Debug: `Day sleep: SkipToMorning started no time skip (another mod?); the sleep runs without one.`, followed
  by the Info start line in its short form `Day sleep: everyone is in bed at 08:24 (day 12); no time skip.`
- Server, Debug (once until the next sleep): `Day sleep not started: everyone is in bed, but the world time 19:05 is
  outside the day window 06:00-12:00 (forced time of day?).` (window end 17:00 with `IncludeAfternoon` on)
- Warning, once per session: the day-cycle fallback of 3.2.
- Client, Debug: `Sleep started at 08:24, day 12 (after dawn).` / `Woke up at 18:00, day 12: daytime sleep, message
  "Good evening" instead of "Good morning".` / `...: daytime sleep, WakeUpMessage empty, vanilla message kept.` /
  `...: daytime sleep, but the game showed no "Good morning" to replace (another mod?).` / `Woke up at 06:00, day 13:
  night sleep, vanilla message.`
- Vanilla's own `Time ..., day: ... nextm: ...` line still appears before ours (from `SkipToMorning`); its target is the
  one we then replace.

### 3.13 Performance

- `CalculateCanSleep` postfix: every frame on every machine; returns after reading `__result` (afternoon and night), the
  local player check (dedicated server) or two static flags (not morning). In the morning with `__result` false: one
  double compare. No allocation.
- `UpdateSleeping` patches: every 2 s on the server; a few field reads; `EverybodyIsTryingToSleep` (one list of player
  ZDOs, as vanilla does at night) only in the morning; the window (three inversions of about 80 calls each of a pure
  float function: 48 monotonic samples plus 32 bisection steps) only when a sleep just started, or in the morning when
  everybody is in bed. One `SendNetTime` per sleep end.
- `SetSleeping` patches: twice per sleep.
- `Player.Message` prefix: every message of every player; one static field read (`SwapText == null`) unless a swap is
  armed, which happens only inside one wake-up call. No allocation.

### 3.14 Files

| File | Responsibility |
|---|---|
| `MC.Exploration.Sleep.ThroughDay.csproj` | Metadata: `ModName` Sleep Through the Day, `Version` 0.1.0, `ModScope` QoL, `<ModIdea>Sleep through the day</ModIdea>`, `ModSide` Both, `ModMultiplayer` Compatible, `ModMultiplayerNotes` (3.9), `ModNetworkVersion` 1, `ModDescription`. |
| `Plugin.cs` | `BindConfig` (`WakeUpHour`, `IncludeAfternoon`, `WakeUpMessage`); `ReadDaySettings` (the one reader of `WakeUpHour`/`IncludeAfternoon`, with the Debug test overrides); `OnActivated`: register the Debug self-tests; `OnDeactivated`: `WakeMessage.Reset()`, `DaySleep.Reset()`, unregister and clean up the self-tests. |
| `Patches/EnvManPatches.cs` | `CalculateCanSleep` postfix. |
| `Patches/GamePatches.cs` | `UpdateSleeping` prefix (state, final time) and postfix. |
| `Patches/PlayerPatches.cs` | `SetSleeping` prefix (state, arm the swap) and postfix; `Message` prefix (the swap). |
| `DayClock.cs` | `IsMorning`, `WakeHour`, `ClockText`/`HourText`/`FractionAt`, `RawFractionForHour` (bisection + fallback), `Today` (dawn, window end, target) returning the `DayWindow` struct, `IsAfterDawn`. |
| `DaySleep.cs` | `BeforeTick` (final time), `AfterTick`, `Retarget`, `Reset`, server log lines. |
| `WakeMessage.cs` | Start/wake tracking, day-sleep rule (`IsDaySleepWake`), `Before`/`Swap`/`After`, `Reset`. |
| `SleepSelfTests.cs` | Debug build only (body in `#if DEBUG`, entry points `[Conditional("DEBUG")]`): the in-world self-tests (3.16), `RecordWake` (called by `WakeMessage`, vanishes in Release) and the in-memory setting overrides read by `Plugin.ReadDaySettings`. |

### 3.15 Pitfalls

1. Two clocks, never mixed in one test: the **flags** (smoothed) answer "does vanilla's time test pass?" (client
   permission, and the server's "will vanilla start this itself?"); the **raw** world time answers "is this a day sleep,
   and when does it end?" (the day window, the target, the wake-up message).
2. No hardcoded 1200, 0.15, 0.85 or 12 (section 2, Seasons). `m_dayLengthSec` is a `long`: convert to double before
   adding the fraction.
3. `UpdateSleeping` is called by `InvokeRepeating` by name: Harmony's detour applies (BedRules and Sleepover rely on
   it too).
4. Unity null semantics: `Player.m_localPlayer == null`, never `?.`.
5. Never throw between `SkipToMorning` and `SleepStart` (3.4 step 4).
6. Do not patch the static flag getters (3.3).
7. `Retarget` only moves a running skip; it never sets `m_skipTime` (3.4 step 4).
8. The final `NetTime` goes in the **prefix**: the postfix runs after `SleepStop` has been sent.
9. Never show the wake-up text from the `SetSleeping` postfix: it would come after vanilla's Rested message and hide
   it. Swap `"$msg_goodmorning"` inside the call (`Player.Message` prefix), so vanilla's order stays (3.6).
10. Self-tests never assign `ConfigEntry.Value`: BepInEx writes the user's `.cfg` at once, and a killed game never puts
    it back. Force settings through the in-memory overrides (3.16).

### 3.16 In-world self-tests (Debug build only)

Registered with `MC.Shared.SelfTest` in `OnActivated`, removed in `OnDeactivated`; run one after another by the world
probe (`tools/Test-InWorld.ps1`) in a throwaway single-player world (the local game is the server, so the server and
client parts both run). Each test:

1. Saves what it changes (world time, `Player.m_wakeupTime`, `Game.m_lastSleepTime`, the profile's custom spawn point,
   Rested, the player's position) and fails early if a sleep or skip runs, the player is attached, or `tod` is forcing
   the clock. Settings a test depends on are **forced in memory only** (`SleepSelfTests.IncludeAfternoonOverride` /
   `WakeHourOverride`, read by `Plugin.ReadDaySettings`): the tests never assign `ConfigEntry.Value`, which BepInEx
   would write to the user's `.cfg` at once (and a killed game would never put back). The Info line of the test says
   which settings it used and whether they were forced.
2. Sets the world time to the test hour of the current date (`ZNet.SetNetTime`) and **snaps** `EnvMan.m_smoothDayFraction`
   to it (test only, so the flags follow at the next `FixedUpdate` instead of lagging for seconds); clears the 30-s
   cooldown and the 10-s guard; removes Rested.
3. Spawns a `bed` 2.5 m in front of the player on the ground (`ZoneSystem.GetGroundHeight`; a new `WearNTear` piece is
   not checked for support for 30 s anyway) and installs temporary Harmony patches (own id `<GUID>.selftest`): a prefix
   on `Bed.CheckEnemies`/`CheckExposure`/`CheckFire`/`CheckWet` that returns true **for that bed only** (the time
   check, `EnvMan.CanSleep` with the mod's client patch, stays real), and a **wake message recorder**: a
   `Player.SetSleeping` prefix (`Priority.First`) and postfix (`Priority.Last`) that frame the local player's wake-up
   call, and a `MessageHud.ShowMessage` prefix that records every message (type and raw text, after the mod's swap)
   shown inside it, plus whether Rested was already on when the call began.
4. Presses E twice through vanilla `Bed.Interact` (claim + "Spawn point set", then lie down). If the second press does
   not lay the player down, that is a FAIL, and the test goes on with the same `AttachStart` call the bed makes, so the
   server part is still checked.
5. Waits for the server loop to start the sleep (`Player.IsSleeping`), checks `EnvMan.m_skipToTime` against the
   expected wake-up (`DayClock.Today(...).Target`, or 06:00 of the next date), screenshots `fade` 2 s later, waits for
   the wake-up recorded by `WakeMessage` (`SleepSelfTests.RecordWake`: wake time, day or night sleep), then checks the
   wake time (same date and at most 15 world seconds after the target, or 06:00 of the next date), the recorded
   messages and Rested, and screenshots `awake` 2.5 s later. Message checks (vanilla order, 1.5): the first Center
   message is `WakeUpMessage` after a daytime sleep (and "$msg_goodmorning" does not appear), or "$msg_goodmorning"
   after a night sleep (or with an empty `WakeUpMessage`); then, since step 2 removed Rested, the next Center message
   is the Rested start message (`$se_rested_start ...`); had Rested been on, only the wake-up text is expected.
6. Cleans up in a `finally` block and again at the start of the next test (in case the probe cut a test short): if the
   player is still asleep (a failed or cut test), stops the skip and wakes everybody at once as vanilla's stop branch
   does (`Game.m_sleeping = false`, routed `SleepStop`: `SetSleeping(false)` + `AttachStop`), so no late stop re-adds
   Rested or moves the wake-up time after the restore; then detaches the player, destroys the bed
   (`ZNetScene.Destroy`), removes the bypass and the recorder, restores everything saved in step 1 (the time with a
   snapped clock again) and clears the setting overrides.

| Test | Setup | Asserts |
|---|---|---|
| `sleep.morning` | 08:00, settings from the config | `CanSleep()` true at 08:00; lying down through `Bed.Interact`; skip aimed at `WakeUpHour` of the same date; wake-up there; a daytime sleep was detected; `WakeUpMessage` (or "Good morning" when empty) first, then the Rested message; Rested |
| `sleep.night` | 22:00, settings from the config | lying down; skip aimed at 06:00 of the next date (vanilla); wake-up there; night sleep; "Good morning" first, then the Rested message; Rested |
| `sleep.afternoon.default` | 14:00, `IncludeAfternoon` forced off (in memory) | as `sleep.night`: vanilla next-morning skip, no retarget |
| `sleep.afternoon.included` | 14:00, `IncludeAfternoon` forced on and `WakeUpHour` forced to 18 (in memory; always 18, so the window reaches 17:00 whatever the config says, e.g. 15 would close it at 14:00) | vanilla starts the sleep and the mod's postfix retargets it (3.4 step 3): skip aimed at 18:00 of the same date; `WakeUpMessage` first, then the Rested message |

Timing: about 20-25 s per test at normal frame rates (fade 3 s, skip 12 s, next 2-s tick, screenshots); internal
timeouts (5 s flags, 15 s start, 75 s wake-up) keep each test under the probe's 120 s.

---

## 4. Edge cases

| Scenario | Vanilla | Mod (default settings) |
|---|---|---|
| Morning, own current bed, every check passes | "You can't sleep at this time" | Lie down; when everybody is in bed: sleep screen, wake at 18:00 the same date, "Good evening", Rested |
| Waking up without Rested (the usual case after a long day) | "Good morning", at once replaced by "You feel rested (Comfort: N)" (1.5) | Same after a day sleep, with "Good evening" in place of "Good morning": the player sees the Rested message (3.6) |
| Waking up while still Rested (two sleeps in a row) | "Good morning" stays (Rested only has its time reset) | "Good evening" stays after a day sleep |
| Afternoon, 12:00-17:00 | Sleep to the next morning | Default (`IncludeAfternoon` off): unchanged, next morning, "Good morning" (decision 1). `IncludeAfternoon` on: sleep to 18:00 the same date, "Good evening" |
| 17:00-18:00 (last hour before the wake-up hour) | Sleep to the next morning | Unchanged: next morning, "Good morning" |
| Night before midnight / after midnight | Next morning / the same date's 06:00 | Unchanged |
| Lie down at 11:59, the others join after noon | — | Default: the sleep starts after the window: vanilla, next morning, "Good morning" for all (README "Good to know"). `IncludeAfternoon` on: day sleep to 18:00 |
| Lie down at 16:55, the others join after 17:00 (`IncludeAfternoon` on) | — | The sleep starts after the window: next morning, "Good morning" for all (the start time counts) |
| 06:00 after a vanilla night sleep | Cannot sleep until noon | After the 30-s cooldown, a day sleep to 18:00 |
| The last player lies down just before 06:00, the server's tick comes just after (its flags still say night) | A skip to the **next** day's 06:00 (a whole day) | Retargeted to 18:00 the same date (the raw time is in the window, 3.4 step 3); "Good evening" (raw start after 06:00). A start a fraction of a second either side of 06:00 on the client may show "Good morning" instead: message only |
| Within 30 s after waking at nightfall | — | "You can't sleep at this time" (vanilla cooldown); afterwards a night sleep to the next morning. A whole day and night can be skipped in about a minute of real time (balance note in the README) |
| Wet, no fire, no roof, too exposed, enemies near | Vanilla messages | Unchanged, in the morning too |
| Own bed that is not the spawn point | "Spawn point set" (no sleep) | Unchanged |
| One player in bed in the morning, another up | — | Nothing happens; the one in bed stays lying (vanilla rule) |
| Player without the mod online, up | — | Cannot lie down in the morning: no morning sleep while they are up (3.9) |
| Player without the mod already in bed since before 06:00 | Stays in bed (nothing happens) | When the others lie down: everybody day-sleeps; that player wakes at 18:00 with "Good morning" and Rested (3.9, M06) |
| Player without the mod, afternoon | Sleeps with everyone to the next morning | Default: unchanged. `IncludeAfternoon` on: sleeps with everyone to 18:00, sees "Good morning" |
| Player with the mod on a vanilla server | — | Mod inactive; vanilla beds |
| Dream cinematic pending (boss killed, boss stone) | Played at the next sleep | Played at a day sleep too; the stop waits for it (vanilla) |
| Host with other players online plays a dream video during a day sleep with `WakeUpHour` 23 | The stop waits for the video while the world time runs (1.5) | The wake-up may pass midnight; before 05:00 of the next date it still counts as a day sleep: "Good evening" (3.6, M11). Single player pauses during the video: no delay |
| Console `sleep` in the morning | Skip to the next morning | Unchanged (no `Game.m_sleeping`, not retargeted) |
| Console `skiptime` | Jump | Unchanged |
| `tod` forced to morning while the real time is later | Flags follow the forced time | Players may lie down; no sleep starts while the real time is outside the window (Debug line when the real time is before 06:00 or after 18:00); `tod -1` restores |
| Seasons with a long winter night or a short summer night | — | Wake-up at Seasons' nightfall (its `RescaleDayFraction`); the message stays right (3.6) |
| Skip-speed mod (skip lasts 6 s) | — | The day skip lasts 6 s too; the message stays right (3.6) |
| Another mod's `SkipToMorning` starts no skip | Sleep screen, no time change | Same in the morning: no forced skip (3.4 step 4) |
| BedRules at its defaults ("sleep any time" on) | Any-time sleep to the next morning | A sleep started in the morning (and in the afternoon with `IncludeAfternoon` on) ends at nightfall; afternoon (by default), night and the last hour before the wake-up hour unchanged |
| Vote or AFK sleep mod hooking `EverybodyIsTryingToSleep` | Partial sleep at night | Partial sleep in the day too |
| Other vote mods | Partial sleep at night | **Unverified** (3.11) |
| Turned off during a day sleep | — | The skip ends at nightfall; "Good morning" (3.10) |
| HUD hidden (Ctrl+F3) | No message | No message |
| `WakeUpMessage` empty | — | "Good morning" stays (no swap armed) |
| `WakeUpMessage` = `$msg_goodnight` | — | "Good night" in the game language |
| `WakeUpHour` = 30 in the file | — | Clamped to 23 by BepInEx |
| `WakeUpHour` = 13 | — | Window 06:00-12:00 whatever `IncludeAfternoon` says: morning sleeps wake at 13:00, afternoon sleeps go to the next morning |
| `WakeUpHour` = 23, `IncludeAfternoon` on | — | Window 06:00-18:00: a sleep at 17:50 wakes at 23:00; at 18:05 it is a night sleep, to the next morning |
| Remote client after any sleep | Cooldown often already over when the late time arrives | A real 30-s cooldown (3.6) |
| Player dies, disconnects or gets up during the wait | Vanilla | Vanilla |
| Player joins during a day skip | Sees the time race (night skip) | Same |
| Night creatures and cold right after waking | — | Yes: it is night (1.6); full night lighting from about 18:15 |
| Plants, smelters, fermenters, cooking stations during the skip | Advance with a night skip (≤ 30 % of a day) | Advance up to 70 % of a day (food left on a cooking station can burn, as with vanilla sleep) |

---

## 5. Decisions and open questions

### Decisions

The ones marked **(to confirm with the user)** change what the player sees or pick between readings of the request.

1. **"Day time" = the morning, 06:00 to noon, by default; the afternoon is optional** (**flagged to the user**: the
   lead changed the designed default at implementation). The design first proposed the whole day (06:00-18:00 minus
   the last in-game hour before the wake-up hour), since the user asked that sleeping "when it's day time" moves the
   time to the night and the game counts 06:00-18:00 as day (`IsDay`). **Decision (lead): `IncludeAfternoon` defaults
   to false**, which is vanilla-consistent: by default only a morning sleep (06:00 until noon, exactly where vanilla
   refuses) goes to nightfall; afternoon and night sleeps keep vanilla's skip to the next morning. With
   `IncludeAfternoon` on (server setting; the server's value wins), afternoon sleeps also end at the wake-up hour,
   except a sleep that starts less than one in-game hour before it (17:00-18:00 by default), which keeps the vanilla
   target because it would otherwise wake you almost at once. The window is measured in raw world time on the server
   and judged at the moment the sleep starts. Cost of the default: the multiplayer trap the whole-day window avoided
   comes back (one player lies down at 11:50, the last one at 12:05: the sleep starts in the afternoon and skips to the
   next morning); the README says so under "Good to know", and `IncludeAfternoon` removes it. To confirm with the user:
   keep the default off, or switch it back on.
2. **Wake up at nightfall, 18:00, the same date** (to confirm with the user), configurable 13-23 (`WakeUpHour`, the
   server's value). 18:00 is when the game counts as night (`IsNight`, evening music, night spawns), the mirror of
   vanilla waking you at 06:00 when the day starts. The lighting is at its evening colours at 18:00 and fully night
   from about 18:15, about 7 real seconds later (1.1; code defaults of the lighting fields), so the player wakes up to
   the night.
3. **"Good evening" after a day sleep** (to confirm with the user): English text from the config, so players can
   translate it or use the vanilla, translated but unused `$msg_goodnight` ("Good night"). Vanilla has no "Good evening"
   token. Alternative: default to `$msg_goodnight` (translated, but reads oddly when waking up). **Shown exactly where
   vanilla shows "Good morning"** (vanilla-consistent, lead decision after the first in-world run, **flagged to the
   user**): the text is swapped inside vanilla's wake-up call, so vanilla's Rested message still follows and, when
   Rested is new, replaces it in the same frame, as it replaces "Good morning" in vanilla (1.5). The player therefore
   sees "Good evening" only when still Rested as they lay down, and "You feel rested" otherwise. The first
   implementation showed the text after the call instead, which kept "Good evening" visible but hid the Rested message.
4. **Reuse the vanilla flow**: the morning sleep starts with vanilla's own three steps (`SkipToMorning`, `m_sleeping`,
   `SleepStart`) and only the end of the skip moves (`Retarget` keeps the skip duration, and never forces a skip that
   `SkipToMorning` did not start). Vanilla's stop branch, the dream, the sleep screen, Rested, the statistic, the
   autosave, `WearNTear.OnSleep` all stay vanilla.
5. **A postfix on `Game.UpdateSleeping`, and retargeting every sleep that starts in the day window** (to confirm with the
   user: it changes what a "sleep any time" mod does during the day when both are installed). With this mod installed,
   a sleep that starts in the window always ends at the wake-up hour, whoever started it: vanilla in the afternoon,
   vanilla in the first second after 06:00 (which would otherwise skip a whole day), or a "sleep any time" mod.
   **This applies to BedRules at its default settings** (its "Ignore Time Restrictions" is on by default): every
   morning sleep of a BedRules user (and afternoon sleep with `IncludeAfternoon` on) ends at nightfall instead of the
   next morning. A postfix never needs to skip vanilla or other mods.
6. **Every vanilla rule stays** (vanilla-consistent): everybody in bed, 10 s between sleeps, 30-s cooldown, every bed
   check, Rested on every wake-up. A day sleep does not change the date (except a wake-up held past midnight on a host, 3.6; no "Day N", no day survived); it counts as a
   sleep in the statistics.
7. **Side Both, multiplayer Compatible** (to confirm with the user: "Limited" is the alternative label). It works fully
   when installed as required; a player without the mod blocks morning sleeps while up (like a player who stays up at
   night) and otherwise sleeps with everyone, waking at nightfall with "Good morning": sane.
8. **No own network data**: no RPC, no ZDO key (network version 1). The server needs nothing from the clients beyond
   vanilla's `inBed`; each client tells a day sleep from a night sleep by itself (3.6), from the raw world time at both
   ends.
9. **Nightfall from the live day cycle**: `GetDay` first, the live `m_dayLengthSec`, and the inverse of the live
   `RescaleDayFraction` (for 06:00, the window end and the target), so day-length and night-length mods (Seasons) are
   respected.
10. **No new bed action and no hover change** (to confirm with the user): the backlog suggested a second option "Rest
    until evening"; the request is a plain sleep, and "[E] Sleep" stays true. Alternative: "[E] Sleep until nightfall"
    in the morning (open question 3).
11. **The server's `WakeUpHour` and `IncludeAfternoon` win** (vanilla-consistent: the server owns the time); the message
    is each player's own setting.
12. **The server sends the final world time just before every `SleepStop`** (vanilla `NetTime` message, called from the
    `UpdateSleeping` prefix). It makes the client's wake-up date exact whatever the skip speed or night length (3.6),
    and as a side effect gives remote clients the real 30-s cooldown vanilla intends. No new message type.

### Implementation notes and deviations (0.1.0)

- **D1 default changed by the lead**: `IncludeAfternoon` = false (above). Everything that depended on it (goal, clock
  table, 3.1, 3.5, 3.7, 3.9, 3.10, 3.11, edge cases, tests, README, `ModMultiplayerNotes`, `ModDescription`) follows
  the new default.
- **Order of the morning checks** (3.4): vanilla's non-time start conditions, including `EverybodyIsTryingToSleep`,
  are checked before the window is computed, so the window math runs in the morning only when everybody is in bed, and
  the "outside the window" Debug line needs no second `EverybodyIsTryingToSleep` call. Same behaviour as designed.
- **Extra Info line** when `SkipToMorning` starts no skip: the short start line `...; no time skip.` follows the
  designed Debug line (3.12).
- **Guards**: `Retarget` leaves the skip alone if the target is not ahead of now (cannot happen inside the window);
  `WakeUpHour` NaN/infinity falls back to 18; a day length of 0 throws before any state change.
- **In-world self-tests** (Debug only, 3.16), with a fourth test `sleep.afternoon.included` beyond the three requested:
  it is the only automated check of the retarget path (3.4 step 3). The tests skip the bed's roof, fire, enemy and wet
  checks for their own bed only (temporary Harmony prefix), keep the time check real, and snap the smoothed day
  fraction after each time jump.
- **Test IDs**: cross-mod items use the `X` prefix (X01-X05) instead of the `C` prefix used in the first draft; T17 is
  the automated self-test item. T03, T04, T06, T11, M02-M05, M10 and X03 were adapted to the default.
- **Review fixes (after the first in-world run, 2026-09-29):**
  - Wake-up text swapped at its source (`Player.Message` prefix inside the wake-up call) instead of shown from the
    `SetSleeping` postfix, so vanilla's Rested message keeps its place after it (decision 3, 3.6). The self-tests now
    record the messages of the wake-up call and check their order; `sleep.night` and `sleep.afternoon.default` had
    failed because they expected "Good morning" as the last text.
  - Day-sleep rule widened to a wake-up before 05:00 of the next date, for a stop held past midnight by a dream video
    on a host with other players online (3.6, M11).
  - Self-tests force `IncludeAfternoon`/`WakeUpHour` in memory (`Plugin.ReadDaySettings`) instead of writing the
    user's config file; `sleep.afternoon.included` always forces `WakeUpHour` 18 (with 15 in the config its window
    closed at 14:00 and the test failed); a test cut short while asleep now wakes the player before restoring.
  - Docs: the network traffic of the mod (README), forced time of day (README), the dedicated server's game version
    (3.9, M10), the day length (1800 s) and the sample Info line.

### Added beyond the request

`WakeUpHour`, `IncludeAfternoon` and `WakeUpMessage` settings; retargeting daytime sleeps started by other mods, and
vanilla's whole-day skip right after 06:00 (decision 5); the final-time message before `SleepStop` (decision 12); the
Debug self-test `sleep.afternoon.included`.

### Open questions

1. The one-hour margin before the wake-up hour (with `IncludeAfternoon` on): fixed (as designed), or a setting?
2. Wake-up text: "Good evening" (English) or the translated vanilla "Good night"?
3. A morning hover hint ("Sleep until nightfall")?
4. A hint for mixed servers: when every player with the mod is in bed in the morning but a connected player without it
   is up, the server could send those in bed a Center message such as "Day sleep needs every player to have Sleep
   Through the Day" through the vanilla routed `ShowMessage` RPC (registered by `MessageHud`; no network version
   change), once per attempt. Vanilla gives no hint at night either, so it is left out for now.
5. Final display name "Sleep Through the Day": permanent after release.
6. `IncludeAfternoon` off by default (decision 1, lead decision): keep it, or make the whole day the default again?

### Unverified names and values

- **The mod and the framework loading and running on `valheim_server.exe`** (first Both mod: no `BepInProcess` filter,
  `FeaturePanel`/`JitCheck` in a headless server), and **the framework's Both paths** (`NetworkGate` handshake, live
  follow, host notice: framework tests N02-N05, never run). `Test-Smoke.ps1` covers the client only. M01, M05, M07,
  M08 and M10 (mandatory) test them.
- The scene values of `Game.m_fadeTimeSleep` (3 s), `EnvMan.m_sunHorizonTransitionH`/`L` (0.08/0.02; the "full night
  from about 18:15" figure depends on them) and `DreamTexts` `m_chanceToDream` (0.1): only the real durations and the
  test wording depend on them; the code uses the live values. (`m_dayLengthSec` is now checked: 1800 s, printed by the
  Info line in the in-world run.)
- How long the dream videos last, i.e. whether M11 can show a wake-up after midnight at all.
- That `piece_bed02` and `ashwood_bed` carry the `Bed` component (prefab data; the tests use `bed`, and T02 optionally
  the others).
- That Mono does not inline `EnvMan.CalculateCanSleep` (expected: about 50 bytes of IL with calls and branches); T10
  proves the patch runs.
- BedRules 2.0.6 and Sleepover 1.1.4 package code (their GitHub sources were read; the packages may differ).
- The hooks of LetMeSleep, SleepyTime, SleepSkip (source repository 404) and NowYouSleep: they may skip to the next
  morning or not allow a morning sleep at all (3.11).
- Whether the Esc/MC Mods menu can be opened while asleep (T14 offers a config-file edit instead).
- Self-tests (3.16): the first in-world run (2026-09-29) showed that a `bed` instantiated at runtime claims and
  attaches through `Bed.Interact`, that the `Bed.Check*` bypass works (not inlined) and that snapping
  `m_smoothDayFraction` lets the flags follow (all four tests passed those steps). Not run yet: the wake message
  recorder (`Player.SetSleeping` and `MessageHud.ShowMessage` patched at runtime) and the in-memory setting overrides.
- The framework's `FeaturePanel` on a headless dedicated server (added by the leader mod in `ModPlugin.Start`; its
  `Update` reads `UnifiedPopup`, `FejdStartup`, `Menu`): not changed here (framework), exercised by M10.

### Docs to update at implementation (outside this mod's folder: left to the lead)

- `docs/game/exploration-player.md` (section 4 and the feature note): the clock table, the smoothed-flag lag and the
  vanilla whole-day skip right after 06:00, `$msg_goodnight` (exists, unused), the skip advancing once per rendered
  frame, `SleepStop` possibly arriving before the final `NetTime`, nothing getting a player out of bed at dawn, Seasons
  patching `RescaleDayFraction`/`SkipToMorning`/`m_dayLengthSec`, and a status link; the sketch there (a ZDO key, an
  alt-interact option, morning only) is superseded by this design.
- `docs/research/existing-mods-exploration.md` and `idea-research.json`: BedRules and Sleepover now verified from
  source (they skip to the next morning from a replacing `UpdateSleeping` prefix; BedRules' "Ignore Time Restrictions"
  is on by default).
- `src/Shared/TESTING.md`: tick N02-N05 from M01, M05, M07, M08 and M10, and replace "needs a Both mod, none exists yet".
- `docs/modding/framework.md`: note that Sleep Through the Day is the first Both mod, and what M10 showed about
  loading on a dedicated server.

---

## 6. Tests (outline; the list of record is `src/Exploration/Sleep.ThroughDay/TESTING.md`)

Run with `./tools/Get-TestTodo.ps1 -Mod ThroughDay`, smoke test `./tools/Test-Smoke.ps1 -Mod ThroughDay` (client only),
in-world self-tests `./tools/Test-InWorld.ps1` (Debug build, 3.16).

**Setup** (checked names): a **Bed** (`bed`) under a roof with a lit **Campfire** (`fire_pit`), claimed as the spawn
point; `devcommands`, then `time` for the day and the fraction (0.25 = 06:00, 0.50 = noon, 0.708 = 17:00, 0.75 =
18:00; after waking, wait about 10 s before reading it); `sleep` and `skiptime` to reach a time; never `tod`.

Single player (default settings unless stated):

| ID | What | Goals / decisions |
|---|---|---|
| T01 | Morning sleep → 18:00 of the same day, Rested, the Rested message covering "Good evening" (Debug line names it), no "Day N", Info start line | 1, 2, D3 |
| T02 | Same presentation as a vanilla night sleep (optional: Dragon Bed, Ashwood Bed) | 2, 3 |
| T03 | Afternoon sleep with the default (`IncludeAfternoon` off) → vanilla next morning, no Info line | 3, D1 |
| T04 | Window end at noon: lie down at 0.46-0.48 → 18:00; at 0.51-0.53 → next morning | 2, 3, D1 |
| T05 | Night sleeps (before and after midnight) unchanged | 3 |
| T06 | `IncludeAfternoon` on: 0.55-0.65 and 0.68-0.69 → 18:00 (retarget Info line); 0.72-0.74 → next morning | 2, D1 |
| T07 | Every other bed check and message kept in the morning; own bed that is not the spawn point unchanged | 1, 3 |
| T08 | 30-s cooldown after waking at nightfall; then a night sleep to the next morning | 3 |
| T09 | After a vanilla night sleep and the cooldown, a morning sleep → 18:00; still Rested, so "Good evening" stays on screen | 1, 3, D3 |
| T10 | `time` "Can sleep" in the morning (client patch), "Can NOT sleep" within 30 s of waking | 1 |
| T11 | `WakeUpHour` 21, 13 (with `IncludeAfternoon` on: 12:30 → next morning), 30 → clamped to 23 | 2 |
| T12 | `WakeUpMessage` `$msg_goodnight` → "Good night"; empty → "Good morning" (lying down while still Rested) | 2, D3 |
| T13 | Live toggle in the morning (refused while off, works again when on) | 4 |
| T14 | Turned off during a day sleep: still ends at 18:00, "Good morning", no error | 4 |
| T15 | `Enabled = false` + restart: vanilla | 4 |
| T16 | Clean log | — |
| T17 | Automated in-world self-tests (3.16): `sleep.morning`, `sleep.night`, `sleep.afternoon.default`, `sleep.afternoon.included` all PASS (including the wake-up message order), screenshots `fade`/`awake` right, the config file untouched | 1, 2, 3, D1, D3 |

Multiplayer (M01, M05, M07, M08 and M10 also tick framework tests N02-N05 in `src/Shared/TESTING.md`):

| ID | What |
|---|---|
| M01 | Host + remote player with the mod: one in bed waits; both in bed → both wake at 18:00 with "Good evening" (N02) |
| M02 | The host's `WakeUpHour` and `IncludeAfternoon` win (both directions for the afternoon); each player's own message |
| M03 | Afternoon sleep (default) → next morning: the remote player sees "Good morning", not "Good evening" (D12; optional: Seasons summer or skip-speed mod) |
| M04 | Split start across the window end (0.47 then 0.51+) → vanilla next morning for both |
| M05 | Hand-off, player without the mod, up: refused in the morning, host stays lying; afternoon vanilla by default; with `IncludeAfternoon` on, both wake at 18:00, that player with "Good morning" and Rested (N05) |
| M06 | Hand-off, player without the mod in bed since before dawn: everybody wakes at 18:00, that player with "Good morning" |
| M07 | Player with the mod on a server without it: inactive, vanilla beds (N03) |
| M08 | Host turns it off live: remote copy follows (N04) |
| M09 | A player has it off, the host on: that player blocks morning sleep |
| M10 | Dedicated server (mandatory), first updated to 1.0.16: loads on `valheim_server.exe` (`[MC:ready]`, `Activated.`, clean log); server's `WakeUpHour = 20` and `IncludeAfternoon = true` apply to both players (morning and afternoon → 20:00) |
| M11 | Optional: host + remote player, host `WakeUpHour` 23 and a dream video (`cinematicsleep`): a wake-up held past midnight still counts as a day sleep |

Cross-mod:

| ID | What |
|---|---|
| X01 | Creature Kill and Tame Counts (MC, `Player.Message` postfix): T09's "Good evening" shows, no tame counted |
| X02 | Batch Station Feeding and Loot Pickup Filter (MC, `Player.Interact` prefixes): T01 works with both installed |
| X03 | BedRules 2.0.6 defaults: morning → 18:00 (retarget line); afternoon → next morning (18:00 with `IncludeAfternoon` on); night → next morning; its "any time" off refuses the morning |
| X04 | SkipSleep (ratio 0.5): one of two players in bed in the morning → day sleep for both |
| X05 | Seasons with a non-default night length: wake-up when `time` flips to night; no day-cycle warning |

Goal coverage: goal 1 → T01, T07, T09, T10, T17, M01; goal 2 → T01, T02, T04, T06, T11, T12, T17, M01, M02, M10; goal 3
→ T02-T09, T17, M01, M03-M09; goal 4 → T13-T15, M08; decision 1 (default) → T03, T04, T06, T17, M02, M04, M05; hand-off →
M05, M06; clean log → T16; framework Both paths → M01, M05, M07, M08, M10; wake-up message in vanilla's place (D3) →
T01, T09, T12, T17, X01; late wake-up after a dream (3.6) → M11.
