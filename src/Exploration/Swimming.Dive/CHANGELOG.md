# Changelog

## 1.0.0

- First release.

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists Swim Dive
  under "MC mods:" with the reason (your game does not have it, has another version, or has it turned off or cannot
  run it) and what to do (install it, install the server's version, or turn it on in Esc > MC Mods, or with F1 when a
  configuration manager is installed). With Jotunn on the server and on your game, Jotunn's window shows the same
  lines under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them.
- The list needs at least one MC mod in your game that is made to run on both the server and the players (any mod a
  server can refuse you for, Swim Dive included, or Sleep Through the Day); the server does not need to have that mod.
  A game whose MC mods are all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it on,
  or a server copy whose version cannot play with yours. That may not be why you were refused.

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
