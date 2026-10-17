# Changelog

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists
  Deep North Awakening under "MC mods:" with the reason (your game does not have it, has another version, or has it turned
  off) and what to do. With Jotunn installed, its mod compatibility window shows the same lines. A server with
  this version also sends its reason, so the screen names the mod even when your game does not have it (as
  long as you have another MC mod).

## 0.1.1

- Fixed: after Kall, lowering ClearKillsMin or ClearKillsMax to or below the Jotun kills an area already had left the
  area uncleared until one more Jotun died there, and `deepnorth_stones` showed more kills than the area needed
  ("9 of 8 Jotun defeated"). The server now clears such an area by itself, like after a last kill ("The Jotun Retreat"
  for the players in it): about two seconds after the settings stop changing, or when the world loads if they were
  changed while the game was closed.
- An area cleared this way stays cleared when the numbers are raised again. That is why the server waits for the
  settings to rest: a value that only passes by (a slider being dragged, a number being typed) clears nothing.
- Until the server has cleared such an area, `deepnorth_stones` shows at most the number the area needs ("8 of 8").
- README and the JotunDensity description: an area can hold one group more than the listed numbers. The game's spawner
  aims at 3 Krigen, 2 dual-axe Krigen, 2 Hexen and 6 Elaking, and when it fills a place in one go its last group can
  take that to 4 Krigen and 8 Elaking. Nothing changed in how the areas spawn; the text now says what happens.

## 0.1.0

- Initial version. The Deep North stays like the normal game until the first Malicious Ice of a Mörkhalla is broken.
  The first and second Malicious Ice awaken the Deep North instead of starting a Jotun invasion elsewhere: hidden
  invaded areas (about 20 %, then 40 % of the Deep North) spawn Krigen, dual-axe Krigen, Hexen and Elaking. The third
  covers about 70 % and starts 3 normal Jotun invasions in the world; every later one starts one, as in the normal game.
- Jotun in the areas get more stars at each stage (world modifiers still apply). Each area storms about a third of the
  time with the Deep North blizzard and Fimbul meteors.
- Every Malicious Ice shows the normal "The Jotun Advance".
- Once the north is awake, Gammeltroll, Barka and frost Greydwarfs fight the Jotun army, and now and then a band of
  them (10-20 frost Greydwarfs with 2-4 shamans, 1-3 Gammeltroll or Barka) appears in an invaded area, also after Kall
  until the area is cleared.
- After Kall Fimbulbringer is defeated, the areas stop spawning Jotun over time. An area that is not cleared spawns its
  Jotun at once each time players walk into it, then waits to be defeated while anyone is near. Killing 8 to 13 Jotun
  inside it (its own fixed number) clears it for good ("The Jotun Retreat"; "The Jotun army is weakening" half way),
  remembered in the world.
- The invaded areas show on the map and minimap as one merged purple region, only where explored; cleared areas
  disappear from it (setting ShowAreas).
- Worlds that already broke Malicious Ice start at the matching stage. Admin console command `deepnorth_stones`.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with it
  turned off or with another version of it (AllowPlayersWithoutMod), and its settings apply to everyone.
- ConfigurationManager shows ClearKillsMin and ClearKillsMax as numbers, not as percentages.
