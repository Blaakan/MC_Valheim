# Changelog

## 0.1.1

- Fixed: in a storm, holding Crouch and Jump together at the surface could get you held under a rising wave, unable to
  swim sideways and losing stamina fast. A wave that washes over you is no longer taken for a dive: you only count as
  under water once Crouch has taken you down, so at the surface you keep riding the waves and keep your stamina, also
  when you press Crouch a moment before Jump.

## 0.1.0

- Initial version. While swimming, hold Crouch to dive straight down and hold Jump to rise, at your swimming speed;
  with neither key held you stay at your depth. Under water the movement keys do nothing (only up or down), except
  under a ship, a dock or a rock, where you can swim sideways to get out. You stop just above the sea floor.
- Diving costs stamina at the normal swimming rate, also while you hold still under water or touch the sea floor; at
  the surface floating still stays free and you ride the waves as usual, even while you hold Crouch where the water is
  too shallow to go down or hold Crouch and Jump together. With no stamina left you float up ("Out of breath") and
  drown as when swimming. Moving up or down raises the Swim skill; holding still does not.
- The camera follows you under the surface, with an under-water tint (about 25 m of visibility, darker at night and
  deeper down) and the water surface visible from below. All three are personal settings.
- The dive speed, the rise speed with no key held and the stamina drain while diving are settings. Required on the
  server (or the host) and on every player's game: the server refuses players without the mod, with it turned off or
  with another version of it (AllowPlayersWithoutMod), and its settings apply to everyone. Stays off next to
  BetterDiving, Dive In, UnderTheSea, VikingsDoSwim and the original Valheim Diving Mod.
