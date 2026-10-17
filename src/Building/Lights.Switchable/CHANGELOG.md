# Changelog

## 0.2.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists
  Switchable Lights under "MC mods:" with the reason (your game does not have it, has another version, or has it turned
  off) and what to do. With Jotunn installed, its mod compatibility window shows the same lines. A server with
  this version also sends its reason, so the screen names the mod even when your game does not have it (as
  long as you have another MC mod).

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
