# Changelog

## 0.1.3

- Fixed the death camera after dying with the spyglass at your eye. The camera stayed inside your character's head
  while the dead body lay there. Now it goes back at once to the normal third-person spot behind you (where it would
  be without the spyglass) and turns to your body, as in the normal game.

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says why: under "MC mods:" it
  lists Spyglass with the reason (your game does not have it, has another version, or has it turned off or cannot run
  it) and what to do (install it, install the server's version, or turn it on in Esc > MC Mods, or with F1 when a
  configuration manager is installed). The server sends that line, so the screen names the mod even when your game
  does not have it.
- This needs at least one MC mod in your game that is made to run on both the server and the players (any mod a server
  can refuse you for, or Sleep Through the Day); the server does not need to have that mod. A game whose MC mods are
  all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it on,
  or a server copy whose version cannot play with yours. That may not be why you were refused.
- With Jotunn on the server and on your game, Jotunn's window shows the same lines under "Failed Connection:"; with
  Jotunn only on your game, the vanilla panel shows them.

## 0.1.1

- Fixed a false error at the main menu, "The vanilla Flint Knife (KnifeFlint) is missing from the item database",
  shown when another mod has its own item list there (seen with Jotunn-based mods). The spyglass always worked; only
  the message was wrong.
- Fixed four false recipe warnings in the log ("... is not an item in this game", "... is not a crafting station ...")
  when you left a server that does not have the mod or has it turned off.
- Fixed the zoom you picked being lost after a lower zoom limit. When MaxMagnification (the server's in multiplayer)
  was lowered for a while and raised again, the spyglass stayed at the lower zoom until you used the mouse wheel. A
  lower limit now only limits the view, and your zoom comes back with the limit. If you change the zoom while the
  lower limit applies, that new zoom is kept.
- Fixed wasted work after turning EdgeBlur off while looking through the spyglass: the mod kept copying the picture
  for the blur on every frame until you lowered the spyglass. It now stops at once. Nothing changes on screen.

## 0.1.0

- Initial version. A craftable spyglass (Forge, 2 Bronze and 2 Crystal by default), a tool like the hammer (held in
  the right hand, takes both hands), with its own model and icon.
- Attack raises it to your eye: your arm lifts it to the right eye while the camera slides into your eye and zooms in
  (x4 by default, mouse wheel from x1.5 to x8), and the view closes in to a sharp round view with a blurred, darker
  edge. Attack again or Block lowers it; a HoldToLook option keeps it up only while Attack is held.
- While looking you aim more slowly (the view moves on screen as fast as without zoom) and cannot walk, jump, dodge,
  crouch, attack or block. It lowers by itself when you are hit, staggered, open a menu, sit, swim, put it away, die
  or teleport. Other players with the mod see you hold it to your eye.
- With the Distant Horizons mod the spyglass sees to the horizon: Distant Horizons draws finer land and farther trees,
  rocks and buildings in the direction you look, and the haze of clear weather is mostly cleared (FogClearing).
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with
  it turned off or with another version of it (AllowPlayersWithoutMod), and its recipe and zoom settings apply to
  everyone.
