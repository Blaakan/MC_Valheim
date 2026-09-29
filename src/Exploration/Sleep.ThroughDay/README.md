# Sleep Through the Day

Sleep in the morning too: lie down in your bed and wake up at nightfall, with the usual sleep screen, time skip and Rested bonus. Night sleeps, and by default afternoon sleeps, still skip to the next morning.

## Features

- **Sleep in the morning.** In vanilla, a bed refuses you from 06:00 until noon ("You can't sleep at this time").
  With the mod you can lie down in your own bed (your spawn point) in the morning too, as long as every other bed
  rule passes: a roof and enough cover, a fire nearby, not wet, no enemy hunting you, and 30 seconds since you last
  woke up. The bed keeps its usual "[E] Sleep" hint and its usual messages.
- **Wake up at nightfall, not the next morning.** When everybody online is in bed, the usual sleep sequence plays:
  fade to black, "ZZZ", sometimes a dream, then the usual 12-second time skip. The time moves forward to **18:00 of
  the same day**, the moment the game switches to night, instead of 06:00 of the next day. You wake up with the usual
  Rested bonus, the sleep counts in your statistics, the game autosaves as usual, and the game says **"Good
  evening"** where it would say "Good morning". The date does not change (unless a host's dream video holds the
  wake-up past midnight, see "Good to know"): no "Day N" message.
- **Everything else stays vanilla.** Night sleeps (18:00 to 06:00) still skip to the next morning. By default,
  afternoon sleeps (from noon) also skip to the next morning, as in vanilla; turn **IncludeAfternoon** on to have them
  end at the wake-up hour too. Every player must be in bed, as at night; the bed checks, the 30-second cooldown and
  the 10 seconds between two sleeps are unchanged.
- **The wake-up hour can be changed** (13:00 to 23:00, 18:00 by default), and so can the wake-up message.
- Turn it off in the **MC Mods** panel at any time: beds behave as in vanilla again, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Exploration.Sleep.ThroughDay.cfg` is created the first time you launch the game
with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be
changed in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs).
Changes apply from the next sleep.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | WakeUpHour | `18` | In-game hour at which you wake up after a daytime sleep. 18 = nightfall, when the game switches to night. Range 13 to 23; decimals allowed (18.5 = 18:30, 21 = 9 PM). In multiplayer the server's (or host's) setting is used. |
| General | IncludeAfternoon | `false` | Off: only sleeps that start in the morning (06:00 to noon) wake you at the wake-up hour; afternoon sleeps skip to the next morning, as in vanilla. On: sleeps that start in the afternoon also wake you at the wake-up hour, until one in-game hour before it (17:00 with the default wake-up hour); a sleep that starts later skips to the next morning. In multiplayer the server's (or host's) setting is used. |
| General | WakeUpMessage | `Good evening` | Message shown when you wake up from a daytime sleep, instead of "Good morning". As with "Good morning", the game's "You feel rested" message replaces it at once if you were not Rested when you lay down. Game text keys work: `$msg_goodnight` shows the game's own "Good night" in your language. Leave empty to keep "Good morning". Each player's own setting. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game: the server moves the time, and each game must allow lying down in the morning. As with vanilla sleep, time only skips when every player online is in bed. A player without the mod cannot lie down in the morning, so they block morning sleep like a player who stays up at night. The wake-up hour and the afternoon setting of the server (or host) apply to everyone. On a server without the mod it turns itself off.

- **Everybody must be in bed**, exactly as at night: a player who stays up blocks the sleep, and the players already
  in bed stay lying until everybody joins them or they get up.
- **Players without the mod** cannot lie down in the morning (their game refuses, as in vanilla), so while one of
  them is online and up, nobody sleeps in the morning. If such a player lay down before 06:00 and is still in bed
  when the others lie down in the morning, everybody sleeps and wakes up at nightfall; that player sees the usual
  "Good morning" and gets Rested as usual. Nobody gets stuck in bed.
- **Afternoon sleeps** follow the server's IncludeAfternoon setting for everybody. With it on, a player without the
  mod can lie down in the afternoon as usual and wakes up with everybody at nightfall (with "Good morning").
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): beds behave as
  in vanilla.
- **Dedicated servers:** the mod runs on `valheim_server.exe` too (install BepInEx there and put the mod in its
  `BepInEx/plugins` folder). The server's settings file decides the wake-up hour and the afternoon rule.
- The mod saves nothing, and the time jump is the game's own world time. On the network it only exchanges a short
  version check with the server when you join (and when the server turns it on or off), and the server sends the
  world time once more when a sleep ends.

## Good to know

- **It is the time you fall asleep that counts.** By default, if one player lies down at 11:55 and the last one only
  at 12:05, the sleep starts in the afternoon and skips to the next morning, as in vanilla. With IncludeAfternoon on,
  the same applies one in-game hour before the wake-up hour (17:00 by default).
- **Waking up at nightfall means night.** Night creatures, night cold and the evening music come as usual; the light
  turns fully to night shortly after you wake up.
- **A whole day can be skipped quickly**: after waking up at nightfall and the 30-second cooldown, a second sleep
  goes to the next morning.
- **Everything that runs on world time moves forward** during a daytime sleep, as during a night sleep, but a
  morning sleep can skip more than twice as much world time as a night: plants grow, smelters, kilns and
  fermenters work, and food left on a cooking station can burn.
- **"Good evening" is often covered at once by "You feel rested"**, exactly as "Good morning" is in the base game:
  when you were not Rested yet as you lay down, the Rested message comes right after the wake-up message and replaces
  it. You see "Good evening" when you were still Rested (for example after two sleeps in a row).
- A pending dream (after a boss) plays at a daytime sleep too. In multiplayer the game waits for the host's dream to
  end before waking everybody, while the world time goes on, so with a late wake-up hour (23) you may wake up after
  midnight; it still counts as a daytime sleep.
- A sleep that starts in the first second or two after 06:00 would, in vanilla, sometimes skip a whole day; with
  the mod it ends at nightfall instead.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing in the world, on characters or on items.

- **MC mods:** no other MC mod changes beds, sleep or the time of day. **Creature Kill and Tame Counts** reads the
  game's messages and ignores "Good evening". **Batch Station Feeding** and **Loot Pickup Filter** watch the use key
  but leave beds alone.
- **BedRules, Sleepover** ("sleep at any time" options; on by default in BedRules): when they start a sleep in the
  morning (or in the afternoon with IncludeAfternoon on), it ends at nightfall instead of the next morning. Their
  night sleeps are unchanged. With their "any time" option off, their own rule decides whether you may lie down in
  the morning, and it usually refuses.
- **SkipSleep, AFKManager** and other mods that let the sleep start when only some players are in bed (through the
  game's "is everybody sleeping?" check) work for daytime sleeps too.
- **Other vote or partial-sleep mods** (LetMeSleep, SleepyTime, SleepSkip, NowYouSleep) have not been checked: they
  may skip to the next morning or not allow a morning sleep at all.
- **Seasons** and other day-length or night-length mods: "nightfall" follows their day cycle, and the skip keeps
  their speed.
- **Forced time of day** (the `tod` console command, ControlTime): whether you may lie down follows the forced time,
  but when a sleep ends (and whether a sleep can start while the forced time says morning) follows the real world
  time. A forced morning during the real night lets you lie down, but no sleep starts until the forced time ends
  (`tod -1`).
- The mods above have not been tested in game with this one yet.
