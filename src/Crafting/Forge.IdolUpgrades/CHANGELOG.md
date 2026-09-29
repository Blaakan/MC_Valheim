# Changelog

## 0.1.0

- Initial version: Forge of Potential idols get 3 star levels. An **Idols** tab at the Forge upgrades an idol one star
  at a time with metal and common, elite or boss trophies of its biome (5/5, 10/3, 15/1 by default).
- Refinement odds come from the idol spent: 35% plain, 55% 1 star, 75% 2 stars, 95% 3 stars. A failure costs the
  item 1 level by default (settings OnFailure and LevelsLost; Destroy gives the vanilla outcome back). Refined items
  keep other mods' data.
- Starred idol icons everywhere the game shows the item; idol tooltips show level, chance and next cost; a dropped
  idol's ground hover shows its stars.
- Click the idol under the Forge requirements to choose which level to spend (default: most stars).
- Settings: the four chances, what a failure does, the idol choice, the six upgrade costs, and each tier's metal and
  trophy lists.
- Required on the server (or the host) and on every player's game: the server refuses players without it
  (AllowPlayersWithoutMod) and its settings apply to everyone.
