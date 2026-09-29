# Changelog

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
