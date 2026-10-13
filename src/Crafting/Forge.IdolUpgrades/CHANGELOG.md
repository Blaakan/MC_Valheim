# Changelog

## 0.1.1

- Fixed a false warning, "Unknown item name(s) in the idol settings, ignored: ...", that listed every default item. It
  was logged at game start with some other item mods installed (seen with Jotunn-based mods) and at every return to
  the main menu. The idol settings were read correctly all along; a real typo is still reported once a world loads.
- Fixed: a tier whose Material setting is empty or is not an item of the game blocked the idol upgrades of that tier
  without saying why. The Idols tab now says it in red ("No valid metal is set for this tier in the settings."), and
  the cost shown in the tab and in the idol's tooltip still counts that metal ("5 metal of this tier") instead of
  leaving it out. The log warning names an empty Material setting instead of listing an empty name.
- Fixed: turning the mod on while the Forge window is open brings the IDOLS tab back at once. Before, only the odds
  and the text came back and the tab was missing until the window was closed and opened again.
- Fixed: with an upgrade cost set to 0, the idol's tooltip ended its upgrade line with a stray comma ("... Idols tab:
  5 Silver, " with Level1Trophies = 0), or with nothing after "Idols tab:" when both costs were 0. A cost of 0 is now
  left out cleanly ("... Idols tab: 5 Silver"); with both at 0 the line only says where to upgrade ("Upgrade at a
  Forge of Potential, Idols tab.").
- Fixed: a starred idol icon first needed while the game could not copy icons (game window minimized) came out
  empty, so the "New material" popup of a starred idol picked up at that moment showed a white square. The plain icon
  now shows until the starred one can be made.

## 0.1.0

- Initial version: Forge of Potential idols get 3 star levels. An **Idols** tab at the Forge upgrades an idol one star
  at a time with metal and common, elite or boss trophies of its biome (5/5, 10/3, 15/1 by default).
- Refinement odds come from the idol spent: 35% plain, 55% 1 star, 75% 2 stars, 95% 3 stars. A failure costs the
  item 1 level by default (settings OnFailure and LevelsLost; Destroy gives the vanilla outcome back). Refined items
  keep other mods' data.
- Starred idol icons everywhere the game shows the item; idol tooltips show level, chance and next cost; a dropped
  idol's ground hover shows its stars.
- Higher levels need higher idols: an item refines with its own idol up to level 5, then needs an idol one tier
  higher from level 6 and one more tier every 4 levels, up to Bloodgold (settings HigherIdolAtHighLevels,
  LevelsOnOwnIdol, LevelsPerIdolTier). The Upgrade tab names the idol needed and from which level.
- Click the idol under the Forge requirements to choose which level to spend (default: most stars).
- Settings: the four chances, what a failure does, which idol high levels need, the idol choice, the six upgrade
  costs, and each tier's metal and trophy lists.
- Required on the server (or the host) and on every player's game: the server refuses players without it, with a
  version that cannot talk to this one, or with it turned off (AllowPlayersWithoutMod), and its settings apply to
  everyone.
- ConfigurationManager shows LevelsLost and the upgrade costs as numbers, not as percentages.
