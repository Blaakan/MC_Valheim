# Changelog

## 0.1.0

- Initial version. New skill, Sailing, shown in the Skills panel with its own icon: the helmsman earns 50 XP per
  kilometre the ship travels (passengers earn nothing). Rested bonus, world skill-gain setting and the death penalty
  apply as for the game's skills. Console: `raiseskill Sailing`, `resetskill Sailing`, and `all` includes it.
- A higher level (all values at Sailing 100, lower levels in proportion): the sail pulls 90% instead of 70% at the
  worst usable wind angles and the no-go zone shrinks from about 37 to about 26 degrees; the ship picks up speed 50%
  faster (more push along the bow, the same sideways drift and heel) without a higher top speed from it, and the sail
  fills in about a second; the rudder swings and the ship turns 50% faster; at Stop the ship brakes (its speed halves in
  about a second); the map is revealed in twice the radius while aboard (100 m instead of 50 m);
  the ship takes half damage from collisions, creatures, fire and the Ashlands sea (the best sailor aboard counts; not
  from players, not when capsized, not weather wear).
- The helmsman's level drives the handling, whichever player's game simulates the ship.
- Every number is a setting. Required on the server (or the host) and on every player's game: the server refuses
  players without the mod, with it turned off or with another version of it (AllowPlayersWithoutMod), and its settings
  apply to everyone. Your Sailing level stays on your character while the mod is turned off.
