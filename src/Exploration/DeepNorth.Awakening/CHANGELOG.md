# Changelog

## 0.1.0

- Initial version. The Deep North stays like the normal game until the first Malicious Ice of a Mörkhalla is broken.
  The first and second Malicious Ice awaken the Deep North instead of starting a Jotun invasion elsewhere: hidden
  invaded areas (about 20 %, then 40 % of the Deep North) spawn Krigen, dual-axe Krigen, Hexen and Elaking. The third
  covers about 70 % and starts 3 normal Jotun invasions in the world; every later one starts one, as in the normal game.
- Jotun in the areas get more stars at each stage (world modifiers still apply). Each area storms about a third of the
  time with the Deep North blizzard and Fimbul meteors.
- Every Malicious Ice shows the normal "The Jotun Advance".
- Once the north is awake, Gammeltroll, Barka and frost Greydwarfs fight the Jotun army, and now and then a band of
  them (5-10 frost Greydwarfs with 1-2 shamans, up to 2 Gammeltroll or Barka) appears in an invaded area.
- After Kall Fimbulbringer is defeated, the areas stop spawning over time. An area that is not cleared spawns its Jotun
  at once each time players walk into it, then waits to be defeated while anyone is near; killing every Jotun it
  spawned clears it for good ("The Jotun Retreat"), remembered in the world.
- The invaded areas show on the map and minimap as one merged purple region, only where explored; cleared areas
  disappear from it (setting ShowAreas).
- Worlds that already broke Malicious Ice start at the matching stage. Admin console command `deepnorth_stones`.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with it
  turned off or with another version of it (AllowPlayersWithoutMod), and its settings apply to everyone.
