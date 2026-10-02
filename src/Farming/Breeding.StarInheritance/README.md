# Breeding Star Inheritance

Tamed animals pass on the weaker parent's stars, with a chance of one extra star (up to 2) that grows with your Farming skill. Replaces the coin flip between the parents' levels. Install it on the server and on every player's game.

## Features

- **Babies start from the weaker parent.** When two tamed animals breed (boars, wolves, lox, hens, Asksvin,
  moose...), the baby starts at the level of the parent with fewer stars. In vanilla, the baby simply copies the parent
  that happens to give birth, so a 0★ + 2★ pair gives about half 0★ and half 2★ babies. With the mod, that pair gives
  0★ babies, or 1★ with the extra-star chance, and never 2★.
- **A chance of one extra star.** Every baby then has a chance to get one star more, up to 2★ (the most the game
  shows). A baby whose weaker parent already has 2★ keeps 2★ and gets no roll. Levels above 2★ from other mods are
  never lowered: two 4★ parents still give a 4★ baby.
- **The chance follows the Farming skill**: 15% at Farming 0, rising in a straight line to 50% at Farming 100 (23.75%
  at 25, 32.5% at 50, 41.25% at 75). Your own Farming always counts for the births your game handles, wherever you
  are. Another player's Farming counts when they also have the mod and stand within 60 m of the animal; the best
  farmer counts. Status effects that raise Farming count too. A character that has never farmed counts as Farming 0;
  the mod does not add Farming to its skills list.
- **Eggs too.** A hen or an Asksvin lays an egg whose quality is the result ("Egg" = 0★, "Egg[2]" = 1★, "Egg[3]" = 2★
  in the ground hover). The chick and the grown animal keep that level (vanilla already passes it on at hatching and
  when the young animal grows up).
- **The other parent is the one the game counted.** When an animal becomes pregnant, the mod notes the level of the
  nearest ready partner of its species (the game's own partner rule: tamed, fed, not pregnant, within its partner
  range, about 3 m). The baby uses that level even if the partner has left, died or been dragged away since.
- **Pregnancies from before you installed the mod** (or while it was off) use the nearest tamed animal of the species
  within the pen range at birth (about 10 m). If there is none, the baby starts from its parent's own level (and still
  gets the extra-star chance).
- **The same rule for everyone in multiplayer.** The mod is installed on the server (or the host) and on every player's
  game. The server refuses players who do not have it (unless you allow them), and its settings apply to everyone
  (see Multiplayer).
- Wild spawns, the `spawn` command, taming and summons are not touched: only births from breeding change.
- Turn it off in the **MC Mods** panel at any time: the next births follow the vanilla rule again, no restart needed.

Worked examples (p = the extra-star chance):

| Parents | Vanilla | With the mod |
|---|---|---|
| 0★ + 2★ | 0★ or 2★, about half each | 0★, or 1★ with chance p; never 2★ |
| 1★ + 2★ | 1★ or 2★ | 1★, or 2★ with chance p |
| 0★ + 0★ | 0★ | 0★, or 1★ with chance p |
| 2★ + 2★ | 2★ | 2★ (already at the cap, no roll) |
| 4★ + 4★ (another mod's levels) | 4★ | 4★ (never lowered, no roll) |

## Configuration

The config file `BepInEx/config/MC.Farming.Breeding.StarInheritance.cfg` is created the first time you launch the game
with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). All settings apply to the
next birth. In multiplayer, the five breeding settings of the server (or the host) are used by everyone: see
Multiplayer.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | ChanceAtFarming0 | `15` | Chance (percent, 0-100) that a baby gets one star more than its weaker parent when the best farmer has Farming 0. It rises in a straight line to `ChanceAtFarming100` at Farming 100. |
| General | ChanceAtFarming100 | `50` | Chance (percent, 0-100) of the extra star when the best farmer has Farming 100. |
| General | ChanceWithoutFarmer | `10` | Chance (percent, 0-100) of the extra star when no farmer is known at all: the game handling the birth has no character at that moment (for example while its player respawns) and no other player with the mod is within `FarmerRange`. Rare in normal play. |
| General | FarmerRange | `60` | How close (metres, 5-64) another player must be to the animal giving birth for their Farming to count. Only players who have the mod count, and only while the game handling the birth has them loaded. The Farming of the player whose game handles the birth counts at any distance. |
| General | MaxStars | `2` | The extra star never takes a baby above this many stars (0-10; 2 is the most the game shows). Babies whose weaker parent already has this many stars or more keep that level and get no extra star. `0` turns the extra star off. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or the host). Off: a player whose game does not have the mod installed is refused about a second after joining (their game shows "Incompatible version"). The check only looks at whether the mod is installed: a player who turned it off on their own game is not refused. On: players without the mod may play, and the animals their game simulates breed the normal game way (the baby takes the level of the parent that gives birth). |

For a single straight line from 10% at Farming 0 to 50% at Farming 100, set `ChanceAtFarming0` to `10`.

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. A birth is decided by the game of the player that is simulating the animal, and a game without the mod would use the normal game rule, so the server refuses players who do not have the mod (their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on. The breeding settings of the server (or host) apply to everyone. Other players' Farming counts when they stand near the animal. On a server without the mod it turns itself off. Babies and eggs are normal for everyone.

- **Who decides a birth:** the game that simulates the pregnant animal at that moment. That is usually the first
  player who came near the pen, not necessarily the one standing next to it. Its own player's Farming (always) and the
  Farming of other players with the mod near the animal decide the result, with the server's settings. A game without
  the mod would give the baby the level of the parent that gives birth (the vanilla coin flip): that is why every
  player needs the mod.
- **Players without the mod are refused.** About a second after such a player has joined (usually while their game
  is still loading the world), the server refuses them: their game goes back to the main menu with the game's own
  message "Incompatible version" (the game cannot show a mod name there), and the server closes the connection a few
  seconds later if their game is still there. On a very slow machine the message can be lost, and the menu then shows
  the game's plain disconnection message instead. The server's log names the player and says why. To let such players
  play anyway, set `AllowPlayersWithoutMod = true` on the server: the animals their game simulates then breed the
  normal game way, and they never count as farmers. Setting it back to `false` refuses the ones who are online about a
  second later.
- **The server's settings apply to everyone.** When you join a server that has the mod, your game asks for its five
  breeding settings (`ChanceAtFarming0`, `ChanceAtFarming100`, `ChanceWithoutFarmer`, `FarmerRange`, `MaxStars`),
  uses them for the births it handles, and says so once in its log ("Using the server's breeding settings: ...").
  The server sends them again whenever they change there. Your own settings file is not changed, and your own
  settings apply again in single player and when you host. `AllowPlayersWithoutMod` is read only by the server.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): births there
  follow the normal game rule.
- **Turning it off on the server** (or the host) turns it off for every connected player too (their panel says the
  server has it turned off), and births follow the normal game rule until it is back on. When it is turned back on,
  players without the mod who are online are refused about a second later (unless they are allowed).
- **A player who turns the mod off on their own game** stays connected (the server only checks that the mod is
  installed): while it is off, the animals their game simulates breed the normal game way and they do not count as a
  farmer.
- **Dedicated servers:** the mod runs on `valheim_server.exe` too (install BepInEx there and put the mod in its
  `BepInEx/plugins` folder). The server's settings file decides the settings for everyone. A dedicated server does not
  run the animals itself: it checks joining players and shares its settings.
- **Farming of other players:** each game with the mod shares its player's Farming level so that the game deciding a
  birth can use it. A friend counts while they stand within `FarmerRange` of the animal and while the deciding game has
  them loaded (a friend at the pen is not seen if the player whose game handles the pen is far away on the other side).
  A friend's Farming is shared as soon as their character appears (joining, respawning, reconnecting). A later change
  (a level gained, a status effect) is shared at the next breeding tick of an animal near them (about every 30 seconds
  for boars and hens).
- **Babies and eggs are normal game data**: they look the same to everyone, and an egg given to a player without the
  mod (on a server that allows them) still hatches at its level.
- **On the network** the mod exchanges a short version check with the server when you join (and when the server turns
  it on or off), a request for the server's settings, and the settings themselves (when you join and when they
  change). It also shares each player's Farming level on their character (see above).
- Mods that make a dedicated server simulate the world move births to the server, which runs this mod too. The server
  has no character of its own, so only players with the mod within `FarmerRange` count as farmers there (else
  `ChanceWithoutFarmer`). Not tested.

## Good to know

- **Stars and levels:** a creature's level 1 is 0★, level 2 is 1★, level 3 is 2★. The health bar shows 1★ and 2★
  only; higher levels from other mods show no star.
- **Keep breeding pairs apart** from other animals of the same species if you want a predictable result. With three or
  more ready animals close together (common with hens), the nearest one when the pregnancy starts counts as the other
  parent.
- **Both parents can still become pregnant**, one after the other, as in vanilla. Each birth follows the rule, so a
  mixed pair never passes on the higher level (unless the extra star lands on it).
- **Nothing is shown** when the extra star happens: you see it in the baby's stars, or in the egg's ground hover. With
  Debug logging on, the BepInEx log explains every birth (partner level, farmer, chance, roll).
- **Eggs in inventories:** the ground hover always shows the egg's level ("Egg[2]"). Whether the inventory slot and the
  tooltip show it depends on the egg item itself (not confirmed yet). Picking up eggs keeps different levels in
  separate stacks, but dragging an egg onto a stack of eggs of another level in the inventory may merge them and lose
  the level (game behaviour): pick up or drop eggs instead of dragging them onto other stacks.
- **Your Farming counts from anywhere** for the births your game handles, for example a birth that fell due while you
  were away and happens when you come back.
- Removing the mod leaves two small values on animals that became pregnant while it was running (the recorded partner
  level and a pregnancy marker); the game ignores them.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16.

- **Star Level System**: not compatible. It decides breeding levels itself (and uses the parent's level for the number
  of eggs), so when it is installed this mod leaves every birth to it and says so once in the log.
- **Creature Genetics / AllTameable** and **TameCraft** with its genetics option replace the breeding rules: do not
  combine them with this mod.
- **BreedingUpgrades** and **Procreation Plus** add their own chance of an extra star on top of this mod's (two
  chances): use only one of them.
- **Seasons**, **FeedLikeGrandma**, **TameGuildWars**: work together (when a mod pauses breeding, for example Seasons in
  winter, this mod leaves those calls alone).
- **Sort Chest** (MC): eggs of different levels are never merged by a sort.
- **Creature Kill and Tame Counts** (MC): babies, hatchlings and grown young animals are not counted as tames (its own
  rule); no conflict.
- **Harpoon Hooks Tames** (MC): dragging the partner away after the pregnancy started does not change the baby: the
  recorded partner level is used.
- **Sleep Through the Day** (MC): unrelated, works together. It is also needed on the server and every player; each
  mod checks only itself (a player with Sleep Through the Day but without this mod is refused by this mod).
- Version-check mods (for example Jotunn or ServerSync based ones) refuse their players before they are fully in; this
  mod only looks at players who got in. No conflict expected (not tested).
- **Loot Pickup Filter**, **Crossbow Stays Loaded**, **One Click Repair All**, **Batch Station Feeding**, **Crafting
  Search and Sort**, **Encyclopedia** (MC): unrelated, work together.
- The mods above have not been tested in game with this one yet.
