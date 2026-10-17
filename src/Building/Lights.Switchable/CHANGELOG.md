# Changelog

## 0.2.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: under "MC mods:" it
  lists Switchable Lights with the reason (your game does not have it, has another version, or has it turned off or
  cannot run it) and what to do. With Jotunn on the server and on your game, Jotunn's window shows the same lines
  under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them.
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

- The server (or the host) now also refuses a player who has the mod turned off, or who has a version of it that
  cannot talk to the server's, and not only a player without the mod: the lights their game runs would burn fuel and
  go out. A player who turns the mod off while connected is refused about a second later (their game shows
  "Incompatible version"). `AllowPlayersWithoutMod` lets all of them play, as before.

## 0.1.0

- Initial version: torches, sconces, the Jack-o-turnip, the snow lantern, the resin candle and the unlit castle
  torches never need fuel; E switches them on or off. Fires you can cook on (campfires, the iron fire pit, braziers,
  the hearth, the bonfire) still burn fuel, even when added to the list.
- Hover text shows On, Off or blocked and what E does. Lights offer no fuel in the hotbar or the radial menu.
- Setting `Lights`: the list of pieces that are switchable lights (prefab names), changeable while the game runs.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod
  (setting `AllowPlayersWithoutMod` lets them in) and its `Lights` list applies to everyone. Uses only the game's
  own fuel and on/off values, so nothing stays in the world after removal.
