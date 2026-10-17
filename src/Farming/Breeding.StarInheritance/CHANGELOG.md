# Changelog

## 0.2.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists
  Breeding Star Inheritance under "MC mods:" with the reason (your game does not have it, has another version, or has it turned
  off) and what to do. With Jotunn installed, its mod compatibility window shows the same lines. A server with
  this version also sends its reason, so the screen names the mod even when your game does not have it (as
  long as you have another MC mod).

## 0.2.0

- The server (or host) now also refuses a player who has the mod turned off, or who has another version of it that
  cannot talk to the server's, and not only a player without the mod: such a game would breed the animals it simulates
  the normal game way. This happens about a second after joining, and about a second after a player turns the mod off
  while connected (their game shows "Incompatible version"; turn the mod back on and join again). Before, a player
  with the mod installed but turned off could stay.
- `AllowPlayersWithoutMod = true` on the server lets all of these players stay. The server's log names each of them
  and says why (does not have the mod, has it turned off, or has another version).
- Fixed the log of a player who has the mod turned off on a server that lets them stay: when the server changed its
  breeding settings meanwhile, that player's log said "Using the server's breeding settings: ..." although their game
  was breeding the normal game way, and the line was then missing when they turned the mod back on. The log now stays
  silent while the mod is off and shows the line once, with the server's current settings, when it is turned on again.
  Births were not affected.

## 0.1.0

- Initial version.
- Babies of tamed animals (live babies and eggs) start from the weaker parent's level instead of copying the parent
  that gives birth.
- One extra star possible, up to 2★: 15% at Farming 0 rising to 50% at Farming 100 (your own Farming, or another
  player's with the mod within 60 m, the best counts); 10% when no farmer is known. Levels above the cap are never
  lowered.
- The partner's level is recorded when the pregnancy starts; pregnancies from before the mod use the nearest tamed
  animal of the species in the pen at birth.
- Settings: `ChanceAtFarming0`, `ChanceAtFarming100`, `ChanceWithoutFarmer`, `FarmerRange`, `MaxStars`.
- Needed on the server (or the host) and on every player's game: the server refuses players who do not have the mod
  installed about a second after they join (their game shows "Incompatible version"), unless its new setting
  `AllowPlayersWithoutMod` is on. On a server without the mod it turns itself off.
- In multiplayer the server's (or host's) five breeding settings apply to everyone; the server sends them again when
  they change.
- Leaves births to Star Level System when it is installed.
