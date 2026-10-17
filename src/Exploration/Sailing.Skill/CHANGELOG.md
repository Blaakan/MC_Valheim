# Changelog

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists
  Sailing Skill under "MC mods:" with the reason (your game does not have it, has another version, or has it turned
  off or cannot run it) and what to do. A server with this version sends the reason itself, so the screen names the
  mod even when your game does not have it.
- This needs at least one MC mod in your game that is made to run on both the server and the players (any mod a server
  can refuse you for, or Sleep Through the Day); the server does not need to have that mod. A game whose MC mods are
  all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it
  on, or a server copy whose version cannot play with yours. That may not be why you were refused.
- With Jotunn on the server and on your game, Jotunn's window shows the same lines under "Failed Connection:"; with
  Jotunn only on your game, the vanilla panel shows them.

## 0.1.1

- Fixed: at the helm of a ship simulated by the game of a player without the mod, or with it turned off (only possible
  when the server has AllowPlayersWithoutMod on), the wind icon on the ship's HUD stayed white at angles where the ship
  got no pull: it showed your Sailing bonus although that ship sails like in the normal game. The icon now shows the
  pull the ship really gets. On your own ship, and on a ship simulated by the server or by another player with the mod,
  nothing changes.
- Fixed: when the wind shifted while the sail was still filling, the sail's pull could swing slightly past its normal
  strength for a moment (about half a percent at Sailing 100, too small to notice). The faster sail of a higher
  Sailing level now follows the normal game's own curve, only sooner, and no longer goes past the full pull for the
  heading. At Sailing 100 the sail still fills and empties in about a second instead of about two; the last part of
  the way is a little gentler than before (90% of the pull after about 1.0 seconds instead of 0.9).

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
