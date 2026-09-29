# Changelog

## 0.1.0

- Initial version: torches, sconces, the Jack-o-turnip, the snow lantern, the resin candle and the unlit castle
  torches never need fuel; E switches them on or off. Fires you can cook on (campfires, the iron fire pit, braziers,
  the hearth, the bonfire) still burn fuel, even when added to the list.
- Hover text shows On, Off or blocked and what E does. Lights offer no fuel in the hotbar or the radial menu.
- Setting `Lights`: the list of pieces that are switchable lights (prefab names), changeable while the game runs.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod
  (setting `AllowPlayersWithoutMod` lets them in) and its `Lights` list applies to everyone. Uses only the game's
  own fuel and on/off values, so nothing stays in the world after removal.
