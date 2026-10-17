# Changelog

## 1.0.0

- First release.

## 0.2.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: under "MC mods:" it
  lists Breeding Star Inheritance with the reason (your game does not have it, has another version, or has it turned
  off or cannot run it) and what to do. With Jotunn on the server and on your game, Jotunn's window shows the same
  lines under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them.
- The server sends this reason, so the screen names the mod even when your game does not have it. This needs at least
  one MC mod in your game that is made to run on both the server and the players (any mod a server can refuse you
  for, or Sleep Through the Day); the server does not need to have that mod. A game whose MC mods are all client-side
  shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it
  on, or a server copy whose version cannot play with yours. That may not be why you were refused.

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
