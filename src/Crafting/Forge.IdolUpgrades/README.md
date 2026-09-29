# Forge Idol Upgrades

Upgrade Forge of Potential idols up to 3 stars with metal and trophies from their biome. More stars give better
refinement odds, from 35% for a plain idol to 95% for a 3-star idol, and by default a failed refinement costs one
level instead of destroying your weapon or armor.

## Features

- **Idols have levels.** Every Battle and Protection idol can be upgraded 3 times: plain, 1 star, 2 stars, 3 stars.
  The stars show on the idol's icon in your inventory, chests and hotbar, and its tooltip says its level, its
  refinement chance and what the next upgrade costs.
- **An Idols tab at the Forge of Potential**, next to the vanilla Upgrade tab. It lists every idol you carry below 3
  stars. Pick one, check the cost, press **Upgrade idol**: one idol of the stack gains a star (it takes the usual Forge
  time). An upgrade always succeeds.
- **What an upgrade costs**, on top of the idol itself:

  | Upgrade | Metal of the idol's biome | Trophies of the idol's biome (any mix) |
  |---|---|---|
  | plain → 1 star | 5 | 5 common trophies |
  | 1 star → 2 stars | 10 | 3 elite trophies |
  | 2 stars → 3 stars | 15 | 1 boss trophy |

- **Which metal and trophies** (all lists can be changed in the settings):

  | Idol | Metal | Common trophies | Elite trophies | Boss trophies |
  |---|---|---|---|---|
  | Wooden | Wood | Deer | Boar, Neck | Eikthyr |
  | Bronze | Bronze | Greydwarf, Greydwarf Brute, Greydwarf Shaman, Skeleton | Troll, Rancid Remains, Bear, Ghost | The Elder, Brenna |
  | Iron | Iron | Blob (also dropped by the Oozer), Draugr, Draugr Elite, Leech, Surtling | Abomination, Wraith, Writhan | Bonemass |
  | Silver | Silver | Drake, Wolf, Ulv, Cultist, Frost Blob | Stone Golem, Fenring | Moder, Geirrhafa |
  | Black Metal | Black metal | Deathsquito, Fuling, Fuling Shaman, Growth, Lox | Fuling Berserker, Vile | Yagluth, Zil, Thungr |
  | Black Marble | Black marble | Seeker, Tick, Hare, Dvergr | Seeker Soldier, Gjall | The Queen |
  | Flametal | Flametal | Charred Warrior, Charred Marksman, Asksvin, Volture, Lava Blob | Fallen Valkyrie, Morgen, Charred Warlock, Bonemaw | Fader |
  | Bloodgold | Bloodgold | Moose, Elaking, Eyeless One, Krigen, Pulp, Seal | Barka, Hexen | Crown Jewel (Kall Fimbulbringer has no trophy) |

- **New refinement odds at the Forge of Potential.** The idol you spend sets the chance: **35%** plain, **55%** 1 star,
  **75%** 2 stars, **95%** 3 stars. The Upgrade tab shows the chance on the button ("Attempt Refinement (75%)") and the
  idol that will be used.
- **A failure costs a level, not the item** (setting **OnFailure**). In vanilla a failed refinement (35%) destroys
  the item and gives back only part of its materials. By default, a failure makes the item lose 1 level (setting
  **LevelsLost**, any number from 1; never below level 1); set OnFailure to Destroy to get the vanilla outcome back.
  The idol is always used up.
- **The rest is vanilla.** Like vanilla, a refined (or downgraded) item comes back at full durability, with you as
  its crafter and the current world level, and taken off if you had it equipped. One difference: it is the same
  item, so any extra data other mods keep on it survives (EpicLoot magic, for example); vanilla replaces it with a
  new, blank item.
- **Choose the idol to spend.** When you carry the needed idol at several levels, the Forge uses the one with the most
  stars (setting **IdolChoice**). Click the idol under the requirements to use another level you carry instead; the
  choice lasts until you close the window. The click needs the mouse: with a controller, use the IdolChoice setting
  (or leave the idols you want to keep in a chest).
- With OnFailure = LoseLevels, no more "need free slots" at the Forge: vanilla keeps inventory room for the materials
  a broken item returns, and items never break then. With Destroy, the vanilla rule stays.
- Turn it off in the **MC Mods** panel at any time: the Forge behaves as in vanilla again, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Crafting.Forge.IdolUpgrades.cfg` is created the first time you launch the game
with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be
changed in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs).
In multiplayer the server's (or host's) settings are used for everyone, except IdolChoice.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: players without the mod are refused about a second after joining (their game shows "Incompatible version"). On: they may play; their Forge works the vanilla way. |
| Refinement | ChanceLevel0 | `35` | Chance in percent that a refinement succeeds with a plain idol (0-100). |
| Refinement | ChanceLevel1 | `55` | Same with a 1-star idol. |
| Refinement | ChanceLevel2 | `75` | Same with a 2-star idol. |
| Refinement | ChanceLevel3 | `95` | Same with a 3-star idol. |
| Refinement | OnFailure | `LoseLevels` | What a failed refinement does to the item: `LoseLevels` (it loses LevelsLost levels, never below level 1) or `Destroy` (vanilla: destroyed, part of its materials back). The idol is always used up. |
| Refinement | LevelsLost | `1` | With LoseLevels: how many levels a failure costs (1 to 100). |
| Refinement | IdolChoice | `Highest` | Idol used when you carry several levels: `Highest` (most stars, best chance) or `Lowest` (plain first, keep your starred idols). Clicking the idol still picks another level. Each player's own choice. |
| Upgrade costs | Level1Material, Level1Trophies | `5`, `5` | Metal and common trophies to go from plain to 1 star. |
| Upgrade costs | Level2Material, Level2Trophies | `10`, `3` | Metal and elite trophies to go from 1 to 2 stars. |
| Upgrade costs | Level3Material, Level3Trophies | `15`, `1` | Metal and boss trophies to go from 2 to 3 stars. |
| Tier 0 - Wooden idols ... Tier 7 - Bloodgold idols | Material | see the table above | Item used as metal for that tier (prefab name, as used by the spawn command). |
| (same) | CommonTrophies, EliteTrophies, BossTrophies | see the table above | Comma-separated prefab names of the trophies (or other items) for each step. A name the game does not know is ignored and listed in the log. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game: the server refuses players without it, and its
settings (odds, what a failure does, upgrade costs, trophy lists) apply to everyone. On a server without the mod it
turns itself off. Upgraded idols are normal idols with a higher quality: a game without the mod shows them without
stars and its Forge cannot spend a starred idol on its own.

- **Players without the mod are refused** about a second after they join; their game shows "Incompatible version".
  The server (or host) can let them in with AllowPlayersWithoutMod = true.
- **The server's settings apply to everyone** (your game logs "Using the server's Forge rules"); your own settings
  apply again in single player and when you host. Only IdolChoice stays your own.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): the Forge is
  vanilla there.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the rules.
- **Giving upgraded idols to a player without the mod** (only possible with AllowPlayersWithoutMod on, or on another
  server): their game keeps the idol's level (saves, chests and drops keep
  it) but shows no stars, and a dropped one reads "[3]" in its ground hover (its quality). Their Forge cannot spend a
  starred idol on its own ("missing requirement"); when they also carry a plain one, their Forge may use up the starred
  idol, at vanilla odds. If they drag a plain idol onto an upgraded one (or the other way), the two stacks merge into
  one level. Give starred idols only to players with the mod.

## Good to know

- **Idols are rare**: they come from chests in their biome's dungeons and points of interest. An upgrade uses the idol
  you already have, it does not make a new one.
- **Some trophies drop rarely**, for example 5% for the Charred, Morgen, Fallen Valkyrie, Seeker Soldier, Tick, Fuling
  Berserker, Deathsquito, Stone Golem, Wraith, Surtling, Dvergr and Hare trophies. The costs and lists are settings.
- **Troll trophies**: Trolls drop the "Troll Trophy"; the game has two items by that name (`TrophyForestTroll` and
  `TrophyFrostTroll`) and both count.
- **Hildir's quest bosses** (Brenna, Geirrhafa, Zil, Thungr) count as boss trophies for their tier.
- **Kall Fimbulbringer** drops no trophy: the Bloodgold boss step takes his Crown Jewel.
- **New Game+ worlds** (world level): like every recipe, only metal and trophies from the current world level count.
  Like every vanilla upgrade, an upgraded idol takes the current world level, so an idol found before the world level
  went up becomes usable at the Forge once upgraded.
- **Upgraded idols keep their level when the mod is turned off** (it is the game's own item quality). After turning
  the mod off in the MC Mods panel (until you restart), idol slots show a quality number (1-4) instead of stars, and
  levels still never merge; but the vanilla Forge accepts a starred idol and may use up any level you carry, at vanilla
  odds (a failure destroys the item). Turning the mod off cancels an idol upgrade or refinement in progress (nothing is
  used).
- **Before removing the mod**, spend your starred idols, or drag them onto a plain idol stack (they become plain):
  without the mod, a Forge cannot spend a starred idol on its own.
- **The refinement chance is rolled when the timer ends**, like in vanilla. Closing the window during the timer
  cancels the attempt and uses nothing.
- Idol upgrades don't raise a skill; refinements raise Crafting as in vanilla.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It stores nothing of its own in the world, on characters or on items (an idol's level is
its vanilla quality). Safe to add at any time; before removing it, see "Before removing the mod" above.

- **Other Forge of Potential mods** change the same refinement and cannot be used together with this one:
  ReforgedPotential, OdinBet ForgeOfPotential, JGFoP, Wire's Forge of Potential Rework, Forge of Potential Safe,
  Odin's Blessing, Potential Forge NoFail, Forge No Destroy, Forge of Certainty, Forge of Progression, Potential
  Draught, the Forge settings of G3A3 and ImpactfulSkills' forging bonus. When another mod takes over the refinement
  first, this mod steps aside for that attempt (one roll only) and the log names the other mod; idol upgrades stay
  safe from their takeover.
- **EpicLoot**: a refined item keeps its magic effects (it is the same item, one level up or down).
- **Crafting Search and Sort** (MC): its filter and sort also work in the Idols tab.
- **Sort Chest** (MC): idols of different levels stay in separate stacks.
- **Crossbow Stays Loaded** (MC): a refined crossbow keeps its "loaded" mark, but its full durability counts as a
  repair, so it needs a reload, the same as after vanilla refinement.
- **Loot Pickup Filter** (MC): all levels of an idol share one filter entry; its mark can cover a star.
- Idols on the ground show their stars in the hover ("Silver Battle Idol (2 stars)") for players with the mod.
- **Tab mods** (Recycle_N_Reclaim, VNEI) add their own tab at stations; the Idols tab takes the place of the hidden
  Craft tab at the Forge, so they don't overlap.
- These combinations have not been tested in game yet.
