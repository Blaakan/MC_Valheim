# Cultivator Replant

Dig up wild berry bushes, mushrooms, thistle and dandelions with the cultivator and replant them at home. Four new
cultivator levels (black metal, eitr, flametal, bloodgold) unlock cloudberries, Yggdrasil shoots, Ashlands plants and
lingonberries.

## Features

### Replant: dig up a wild plant

- **Hold the cultivator, aim at a wild plant and press E** (the game's Use key; a rebound Use key works too). On a
  gamepad, press **X**.
- **The plant comes out of the ground as a transplant item** ("Raspberry bush transplant", "Mushroom transplant"...)
  that goes into your inventory. With a full inventory it drops where the plant stood.
- **A ripe plant is harvested first**: the berries, mushroom, thistle or fiddleheads drop as if you had picked them
  by hand, then the plant is dug up. Nothing is lost.
- **A crosshair hint** under the plant's name says "[E] Replant" (the crosshair turns yellow), or which cultivator
  level the plant needs, for example "Needs a black metal cultivator (level 4)". On a gamepad it shows the button.
- **Not while you steer a ship or ride a lox**: there is no Replant hint then, and E (or X) does what it does in the
  game (E lets go of the helm). Let go first, then dig.
- **It costs what removing a piece with the hammer costs**: the tool's stamina (5, less with a high Farming skill), a
  little durability, the swing and its sound. Digging up gives no Farming skill (the harvest before it raises the
  skill as picking by hand does) and no wood: a dug-up bush drops no wood.
- **Wards protect plants**: inside someone else's ward you cannot dig, as with removing a piece. **No-build zones do
  not stop it**: you can pick plants there, and yellow mushrooms grow inside dungeons.
- **A forage plant you have already picked cannot be dug up** until it has regrown (its stalk is hidden while picked).
  A picked berry bush can be dug up.
- **Wild plants never come back**: the game places wild vegetation once per area, so a dug-up wild plant is gone for
  good. Plant it again somewhere else.

### What each cultivator level can dig up

| Cultivator level | Tier | New plants |
|---|---|---|
| Levels 1-3 (vanilla) | Bronze cultivator | Dandelion, Thistle, Mushroom, Yellow mushroom, Raspberry bush, Blueberry bush |
| Level 4 | Black metal cultivator | Cloudberry bush |
| Level 5 | Eitr cultivator | Yggdrasil shoot (the small shoots and the shoot trees of the Mistlands) |
| Level 6 | Flametal cultivator | Fiddlehead, Smoke puff |
| Level 7 | Bloodgold cultivator | Lingonberry bush |

Each level also digs up everything the levels below it can.

### Upgrading the cultivator

Levels 1 to 3 are the vanilla cultivator, made and upgraded at the Forge with Bronze and Corewood as before. Levels 4
to 7 are made in the Forge's Upgrade tab, each from the level below, and cost only their own materials (no Bronze or
Corewood); server owners can change the materials:

| Upgrade to | Materials (default) | Station |
|---|---|---|
| Level 4, black metal | 5 Black metal, 10 Linen thread | Forge level 4 |
| Level 5, eitr | 15 Refined eitr | Forge level 5 |
| Level 6, flametal | 5 Flametal | Forge level 6 |
| Level 7, bloodgold | 5 Bloodgold | Forge level 7 (all six extensions) |

- **The tool stays the vanilla Cultivator**: same name and model, and each level adds 200 durability as the vanilla
  levels do (1400 at level 7).
- **The tooltip names the tier** right after the level ("Tier: Black metal cultivator") and lists what it can
  replant ("Replants: ..."). In the Upgrade tab, the preview of levels 4 to 7 also says what the new level adds
  ("New: Cloudberry bush").
- **A coloured gem on the icon** marks levels 4 to 7 in the inventory, the hotbar and messages: dark grey (black
  metal), cyan (eitr), orange (flametal), crimson (bloodgold). In the inventory and in chests, levels 4 to 7 show the
  gem instead of the level number (levels 1 to 3 keep their number); the tooltip names the level and the tier.
- **The Forge of Potential no longer lists the cultivator** while the mod is on: a Wooden Battle Idol could otherwise
  skip a level's materials. A cultivator already refined past level 3 counts as that level's tier.

### Transplants and where they grow

- **A transplant item** shows the plant's produce icon with a small sprout on a soil mound in its upper-right
  corner, so it never looks like the food. It is a material, not food: weight 0.5, stacks of 20, can go through
  portals, worth nothing to traders.
- **Plant it with the cultivator**: once you have had a transplant, it appears at the end of the cultivator's build
  menu. It goes on the ground (not on floors) and needs no cultivated soil. Monsters leave the young plant alone, and
  neither the hammer nor the cultivator can remove it (Replant can, see below).
- **It grows into the same vanilla plant, ripe**, after the plant's own regrowth time: bushes and fiddleheads 5 hours
  of play, the other forage 4 hours, Yggdrasil shoots 2 hours (each young plant varies by 10%; see "Grow times run on
  the world clock" below). The grown plant is an ordinary plant: you pick it and it regrows like a wild one.
- **Outside its biome the placement ghost is red.** A transplant planted where it cannot grow (too hot, too cold, no
  room, no open sky) waits instead of dying, and its hover text says why.
- **Dig up a transplant again**: aim at any transplant that has not grown yet (yours or another player's) and press
  E (same hint): it comes back as a transplant item, to move a misplaced one. This needs the cultivator level of that
  plant, and wards protect transplants as they protect wild plants.

| Transplant | Where it grows |
|---|---|
| Dandelion, Thistle, Mushroom, Yellow mushroom, Raspberry bush, Blueberry bush, Cloudberry bush | On open ground in any land biome, no tilling needed; in the Ashlands (too hot), the Mountains and the Deep North (too cold) only inside a shield |
| Fiddlehead, Smoke puff | Only in the Ashlands, without a shield, but not on lava |
| Lingonberry bush | Only in the Deep North, without a shield, also on deep snow |
| Yggdrasil shoot | 2 to 6 m from an Ancient Root (see below) |

### Yggdrasil shoots and the Ancient Root

- **The eitr cultivator digs up Yggdrasil shoots**, small or tree-sized, into a Yggdrasil shoot transplant.
- **It only grows near an Ancient Root**: plant it 2 to 6 m from a root (setting RootRange gives the 6). Away from
  one the placement ghost is red and the hint says "Plant 2 to 6 m from an Ancient Root". The ghost is green right
  against the root too, but closer than about 2 m the young shoot has no room to grow ("Needs more room to grow");
  under a root arch it needs an open sky. Replant gives a misplaced one back.
- **It drinks the root's sap**: when its time is up, it takes 20 sap from the root (setting RootSapCost) and grows
  into a Yggdrasil shoot tree. An Ancient Root holds 50 sap and regains about 9 per hour, the same reserve Sap
  Extractors draw from: one tree costs about as much as 20 Sap from an extractor. While the root has 20 or less, the
  young shoot waits and tries again every few seconds.
- **Its hover text shows the root**: "Draws 20 sap from the Ancient Root when grown (root: 34 / 50)", with "waiting
  for it to refill" while the root is too low, or "Needs an Ancient Root within 6 m" when none is near.

### Turning it off

Turn the mod off in the MC Mods panel (or set Enabled = false) at any time. Replant stops, the transplants leave the
cultivator's build menu and the cultivator goes back to vanilla: the Upgrade tab stops at level 3 and the Forge of
Potential lists it again. Your transplant items stay in your inventory, transplants already in the ground keep growing
(Yggdrasil shoots without taking sap while the mod is off), and a level 4-7 cultivator keeps its level and durability
but shows its level number instead of the gem, and no tier lines, until you turn the mod on again. A level 3
cultivator you take out of a chest later is not offered a level 4 upgrade either.

## Configuration

The config file `BepInEx/config/MC.Farming.Cultivator.Replant.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). In multiplayer the server's
(or host's) settings in the Upgrades and Growing sections are used for everyone; Enabled is each player's own, and
AllowPlayersWithoutMod is read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or host). Off: players without the mod, with it turned off or with another version of it are refused. On: they may play. A player without the mod loses any transplant items they receive and never sees transplant saplings, and a chest loses its transplants when such a player takes or adds an item in it. A player who has the mod turned off (or another version) keeps transplants and sees the saplings, but saplings whose area their game runs grow on that player's own grow time, and Yggdrasil transplants grow there without an Ancient Root and without taking sap. |
| Upgrades | BlackMetalLevel | `BlackMetal:5,LinenThread:10` | Materials of the upgrade from level 3 to level 4 (the black metal cultivator), as item names with amounts: `Name:amount,Name:amount`. Unknown names and bad amounts are skipped (with a warning in the log). With no valid material, cultivators stop at level 3. Server's setting in multiplayer. |
| Upgrades | EitrLevel | `Eitr:15` | Materials of the upgrade from level 4 to level 5 (the eitr cultivator). With no valid material, cultivators stop at level 4. Server's setting in multiplayer. |
| Upgrades | FlametalLevel | `FlametalNew:5` | Materials of the upgrade from level 5 to level 6 (the flametal cultivator). With no valid material, cultivators stop at level 5. Server's setting in multiplayer. |
| Upgrades | BloodgoldLevel | `Gold:5` | Materials of the upgrade from level 6 to level 7 (the bloodgold cultivator). With no valid material, cultivators stop at level 6. Server's setting in multiplayer. |
| Growing | GrowTimeMultiplier | `1` | A transplant grows in the plant's own regrowth time, on the world clock like crops (bushes 5 h, forage 4 h, fiddlehead 5 h, Yggdrasil 2 h of play) times this (0.1 to 10): 0.5 = twice as fast, 2 = twice as slow. Applies at once, also to transplants already in the ground. Server's setting in multiplayer. |
| Growing | RootSapCost | `20` | Sap a Yggdrasil transplant takes from its Ancient Root when it grows (1 to 49). A root holds 50 and regains about 9 per hour; while the root has this much or less, the transplant waits. Server's setting in multiplayer. |
| Growing | RootRange | `6` | How close to an Ancient Root a Yggdrasil transplant must be planted, in metres (4 to 20). It also needs about 2 m of room from the root itself to grow, so it goes 2 m to this far from the root. Server's setting in multiplayer. |

The item names of the upgrade materials are the game's own: `BlackMetal` (Black metal), `LinenThread` (Linen thread),
`Eitr` (Refined eitr), `FlametalNew` (Flametal; the old `Flametal` is Ancient metal) and `Gold` (Bloodgold). A level
with no valid material cannot be made, and neither can the levels above it.

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. The transplant items, the transplant saplings and
the new cultivator levels change the game, so the server refuses players who do not have the mod, have it turned off
or have another version of it (their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is
on, and the settings of the server (or host) apply to everyone. A player without the mod loses any transplant items
they receive and does not see transplant saplings; plants that have finished growing are ordinary game plants. On a
server without the mod, transplants dropped on the ground can be deleted.

- **Players who cannot play by the server's rules are refused** about a second after they join; their game shows
  "Incompatible version". This covers a player without the mod, a player whose version of the mod cannot talk to the
  server's, and a player who has it turned off. A player who turns it off while connected is refused about a second
  later (a young transplant their game runs may still grow by the game's own rules in that second).
- **With AllowPlayersWithoutMod = true, players without the mod** may play, but their game does not know the
  transplants:
  - a transplant item dropped on the ground is invisible to them and they cannot pick it up; one in a chest does not
    show for them, so when they take or add an item in that chest it is saved without the transplant, which is then
    lost for everyone. Keep transplants out of chests such players use;
  - a young transplant in the ground is invisible to them, and while their game runs that area (they are the only
    player near it) it does not grow; it grows again once a player with the mod is near;
  - a grown plant is an ordinary game plant: they see it, pick it and can chop a grown Yggdrasil shoot;
  - a level 4-7 cultivator in their hands is a normal cultivator with that level's durability; they cannot dig up
    plants with it, and the Forge of Potential lets them refine it further.
- **With AllowPlayersWithoutMod = true, players who have the mod turned off** (or another version of it) may play
  too. Their game knows the transplants: they see them, keep transplant items and chests keep them. But the server's
  rules do not apply on their game: a young transplant whose area their game runs grows on that player's own grow
  time, and a Yggdrasil transplant grows there without an Ancient Root and without taking sap. They cannot dig up
  plants.
- **On a server without the mod** the mod turns itself off for you, as in "Turning it off": no Replant, no
  transplants in the build menu, cultivator levels up to 3, and young transplants your game runs there grow by the
  game's own rules (on your own grow time; a Yggdrasil shoot without an Ancient Root). Your transplant items stay in
  your inventory, but a transplant you drop on the ground there can be deleted (a host without the mod deletes it once
  it is in the area around the host).
- **The server's settings apply to everyone** (your game logs "Using the server's rules"): the upgrade materials,
  the grow time, the root sap cost and the root range. Until they arrive after you join, the transplants are hidden
  from the build menu, the cultivator stays at the vanilla levels, Replant does nothing and young transplants do not
  grow. Your own settings apply again in single player and when you host.
- **Who does what:** digging up runs on the digger's game (it takes over the plant first, as removing a piece does).
  A young transplant grows on the game of the player who runs its area (usually the nearest player), and the root's
  sap is taken by the game that runs the root.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the Upgrades and Growing settings for everyone.

## Good to know

- **Grow times run on the world clock**, like crops and berry regrowth: a transplant keeps growing while nobody is
  near (it catches up when you come back), sleeping through the night moves it ahead, and nothing grows while the
  world is not running (in single player: while you are not playing).
- **Moving a plant never gives more**: the dig harvests what is ripe, and the replanted plant is ripe only after the
  plant's own regrowth time, when the wild one would have regrown anyway.
- **A scythe swing destroys a transplant that cannot grow** (too hot, too cold, no room...), as it does with any
  sickly crop. A Yggdrasil shoot waiting for sap stays healthy and is safe.
- **Two players digging the same plant in the same instant** may both get a transplant, the same race as two players
  removing one piece with the hammer. In the same way, rarely, Yggdrasil transplants of two different players at one
  Ancient Root may both grow on a single sap drain. Several transplants of one player never share a drain.
- **Cheated materials:** while levels 4 to 7 are on, carrying a cheat-spawned stack of a tier material (black metal,
  linen thread, refined eitr, flametal, bloodgold) also marks a level 1-3 cultivator crafted or upgraded with it as a
  cheated item, as the game does for any material in an item's recipe. The next upgrade made without cheated
  materials clears it.
- **Turning the mod off** lets the Forge of Potential refine a cultivator again; a cultivator refined past level 3
  that way counts as that level's tier when the mod is back on.
- **Uninstalling** the mod removes every transplant item from inventories and chests the next time they load, as with
  any item a mod adds. Transplants planted but not grown yet, and transplants dropped on the ground, are lost in single
  player or when you host: the game deletes each one for good the first time its area loads without the mod (the log
  says "Destroyed invalid prefab ZDO"), starting around the spot where you load in. Those in places you have not
  visited since stay until you go there. On a dedicated server they stay in the save (players see nothing there) and
  come back when the mod is installed again on the server and the players' games. Grown plants stay: they are
  ordinary game plants. Level 4-7 cultivators stay as working cultivators with their level and durability; the game
  then upgrades them no further at the Forge, and the Forge of Potential refines them as any other tool. Turning the
  mod off (Enabled = false) keeps the transplants.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It adds eleven transplant items (`MC_Transplant_<plant>`) and eleven transplant saplings
(`MC_Sapling_<plant>`), and while it is on it raises the vanilla Cultivator's maximum level to 7 and adds the level
4-7 materials to its recipe; nothing else is stored.

- **Forge Idol Upgrades** (MC): the cultivator is not listed at the Forge of Potential, so its refinement never
  touches it. This mod leaves the Forge's crafting step alone, so Forge Idol Upgrades keeps all its features.
- **Crafting Search and Sort** (MC): an ingredient search finds the cultivator upgrade that uses it at that level
  (for example "black metal" with a level 3 cultivator in the Upgrade tab); a level 4-7 upgrade is not found by
  "bronze".
- **Encyclopedia** (MC): lists the transplant items and the transplant saplings (among the cultivator's pieces) with
  their names and icons, the cultivator's materials and Forge level for each level, and the cultivator under "used
  in" of each upgrade material. The per-level rows show no Wooden Battle Idol, but the cultivator's page still says
  it can go beyond level 7 at an upgrade station, and the Wooden Battle Idol's page still lists the cultivator under
  "used in", although the Forge of Potential does not list the cultivator while this mod is on.
- **Loot Pickup Filter** (MC): what a ripe plant drops when you dig it up counts as a hand harvest, so it is picked
  up whatever the filter says (as long as its ExemptHarvest setting is on).
- **Spyglass** (MC): no overlap; the spyglass and the cultivator are never in the hand together.
- **Other MC mods:** no overlap.
- **PlantEverything**: its plants and the transplants share the cultivator's build menu. Its Remove button (for its
  own flora) keeps working; Replant uses E or X, never Remove. Wild bushes it turned into plantable pieces can still
  be dug up with Replant. The transplants keep their own rules (no cultivated soil needed, a misplaced one waits).
  It also has its own Yggdrasil sapling (paid with Sap, no root needed); both can be used.
- **PlantEasily**: recognises the cultivator at every level and plants transplants in a grid (each copy costs a
  transplant). Its grid does not check the Ancient Root rule: Yggdrasil copies placed away from a root wait ("Needs an
  Ancient Root within 6 m"), and Replant gives them back. Its replant-on-harvest does not use transplants.
- **Groundwork, EarthWright**: they have their own ways to remove wild plants with the cultivator; both work side by
  side with Replant. A plant grown from a transplant counts as wild for them.
- **Recipe changing mods** (WackysDatabase, RecipeCustomization and the like): a cultivator recipe they change
  becomes the cost of levels 1 to 3, and this mod adds the level 4-7 materials on top again, also when they change the
  recipe while you play. Another cultivator recipe added by a mod only makes new cultivators and upgrades up to level
  3: levels 4 to 7 are always made with this mod's materials.
- **EarthWright, MoreBettererUpgrades, FurtherUpgrades** and other mods that change the cultivator's maximum level:
  not supported together. Whichever mod writes last decides the cultivator's levels, so the tiers may not match the
  table above.
