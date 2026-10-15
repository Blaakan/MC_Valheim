# Cheated Tag Remover

Console commands that remove the vanilla cheated tag from a whole world: building pieces, creatures, stations, items in chests, on the ground and on stands, and the items you carry. Nothing is deleted.

Valheim 1.0 marks things made with dev commands as "cheated", and the mark spreads: everything crafted at a marked
workbench is marked, a marked weapon marks what it kills and their drops, marked wood marks what you build with it,
one marked stack marks the stacks it is merged into. On a shared server this ends with the grey tooltip line
"This item was summoned through cheating means." on most items, and the game has no way to remove it. This mod removes
the mark from the saved world, once, when you ask for it.

> **Needs rework for Valheim 1.0.20.** Valheim 1.0.20 (October 2026) cleared every cheated tag made before it, on
> objects and on items, and stores new tags in a new way. This version was written for Valheim 1.0.17: on 1.0.20 it
> still clears the tag of objects, but its count includes old object tags the game no longer sees, and it leaves the
> tag of items saved by 1.0.20 alone (it only knows the item format of 1.0.17). Do not rely on it until a version for
> 1.0.20 is out.

## Features

- **`cheated_clear`** removes the cheated tag from:
  - every building piece, creature, station and other object of the world (also where nobody is);
  - the queues of smelters, kilns, fermenters and every slot of cooking stations and ovens;
  - every item stored in a chest, cart, ship hold, tombstone or any other container;
  - every item lying on the ground (fish included), on item stands and on armor stands;
  - the items you carry.
- **`cheated_scan`** counts the same things and changes nothing. Run it first to see what the world holds.
- Only the tag is changed. Items stay where they are with their stack size, quality, durability, crafter name and the
  data other mods keep on them, including items of mods that are not installed where the command runs.
- Where the world runs (single player, the host, a server with the mod) the command checks its own work: it counts
  again afterwards, repeats what another player's game undid, tells you what is left and why, and then saves the world.
- Not a cheat command: it needs no `devcommands` and does not mark your character.

### How to use it

1. Make a backup of the world (and of your character file if you want to be extra careful).
2. Open the console (F5; add `-console` to the game's launch options in Steam if it does not open) or the chat (Enter).
3. Type `cheated_scan` (in chat: `/cheated_scan`) to see the count, then `cheated_clear` (in chat: `/cheated_clear`).

Where the world is cleaned depends on where you play:

| You are | What `cheated_clear` cleans |
|---|---|
| In single player, or hosting from your game | The whole world and the items you carry. |
| An admin on a server that has the mod | The whole world and the items you carry. The server also asks the game of every other player who has the mod to clean the items that player carries. |
| Not an admin, on a server that has the mod | The items you carry. The world needs an admin. |
| On a server that does not have the mod | The items you carry and the objects loaded around you (your base while you stand in it). The command says so, and lists what another player's game undid. |

To clean a whole dedicated server, install the mod on the server and on your own game, put your ID in the server's
`adminlist.txt`, join and type `cheated_clear`. The answer arrives a few seconds later. If you cannot install mods on
the server: stop it, copy the world files to your PC, open the world in single player with the mod, type
`cheated_clear`, wait for "Saving the world", quit, and put the files back.

### What it cannot reach

- **Items carried by players who are offline or do not have the mod.** They live in each player's character file, not
  in the world. Such a player can install the mod and type `cheated_clear` once, on any server: the command always
  cleans the items of the player who types it.
- **Chests that a player without the mod has open** at that moment. The command leaves them alone (that player's game
  would write its own copy back), tells you how many and where they stand, and cleans them on the next run once they
  are closed. A chest that was open when its player lost the connection can stay marked as open: open and close it
  once, then run the command again.
- **Objects that another player's game is busy changing** (a creature it moves, a station it runs) can come back with
  the tag when that player does not have the mod. The command tries three times and lists what is left. Easiest: run
  it while the other players are away from the base, or when you are alone on the server.
- **On a server without the mod**, your game can only change what is loaded around you, and that server writes the
  change to its save the next time something else changes in that area. A cleaned portal in particular can carry the
  tag again after that server restarts. Only the server install makes the clean-up certain.
- Items that another mod stores in its own format (a backpack inside an item, extra equipment slots).
- A player without this mod whose game runs a mod that moves items in closed chests (quick stack to nearby chests,
  craft from chests): an item moved in the very moment the command runs can be undone. Run the command when such
  players are not using their base, or have them install the mod.

### Keeping the world clean

The mod removes existing tags; it does not stop the game from making new ones. After a clean-up, tags come back from:

- a player who still carries tagged items (crafting, building, fighting, chopping, mining, trading and stacking spread
  it again): have every player run `cheated_clear` once;
- dev commands used again (`spawn`, no-cost building, god mode, ghost mode or fly while fighting);
- items whose total damage is above 10 000: the game tags those again every time a chest or a character loads.

The vanilla command `yesiuseddevcommandsbutiwantmyachievementsanyway` does something else: it makes one character's
game ignore the tags (and hides the tooltip line for that character). The tags stay in the world for everybody else.

Achievements: the tag is one of several things that switch achievements off. This mod does not touch the others (the
"used dev commands" mark of a character, world modifiers set by command), and the game also treats a game that runs
mods as cheated. For players without mods on your server, the "temporarily disabled" state of the achievements ends
once nothing tagged is left in their inventory (unless their character or the world is marked for another reason).

## Configuration

The config file `BepInEx/config/MC.UX.CheatedTag.Remover.cfg` is created the first time you launch the game with the mod. To turn the
mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager installed that button is
hidden: press F1 and use its window). Every setting can be changed in-game with ConfigurationManager, or by editing the
file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. Off: both commands are gone. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |

The mod has no other setting: it does nothing until a command is typed.

## Multiplayer

- **Who needs it:** the server (or the player who hosts), and the admin who types the command. Other players only if
  they want the items they carry cleaned.
- **Multiplayer support:** Works in multiplayer.

Install it where the world runs (the dedicated server, or the player who hosts) to clean the whole world, and on the game of the admin who types the command. Other players do not need it; those who have it also get the items they carry cleaned. On a server without it, the command only cleans your own items and what is loaded around you.

- Nobody is refused: players without the mod can join a server that has it. The MC Mods panel lists the mod as
  "server + every player need it": that is the panel's label for every mod that runs on both the server and the
  players' games. For this one, read the line above.
- On a server without the mod, the mod's status says "Inactive: the server does not have this mod. It must be
  installed on the server too." and a message at spawn lists the mod as inactive. The two commands still work there for
  your own side, as described above.
- Only an admin can clean or count the whole world of a server (the same admin list as `kick` and `save`). Everybody can
  clean the items they carry.
- A player without the mod sees cleaned items and objects like any others: the tag is plain vanilla data, and the mod
  adds nothing of its own to the world.
- The server's log names every request: who asked, what was removed.

## Good to know

- The clean-up of a whole world is done in one go, so the game (or the server) can freeze for a moment on a large
  world. The save that follows can be longer than usual because every changed area is written again. The server's log
  says how long the pass took.
- Chests near players reload their contents once after the clean-up.
- `cheated_scan` and `cheated_clear` appear in the character's list of used commands like any console command. That
  list is not a cheat mark.
- Turning the mod off or uninstalling it changes nothing in the world: cleaned things stay clean.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.17.

- **Mods that delete tagged items** (NoCheatItems, ItemIntegrity ServerOnly, CheatTagEnforcement): they can delete an
  item before it is cleaned. Remove them before you run `cheated_clear` (NoCheatItems and ItemIntegrity ServerOnly can
  be switched to their dry-run setting instead). CheatTagEnforcement also kicks players who carry tagged items.
- **Mods that clear or hide the tag by themselves** (AchievementEnabler, NoCheatedTag, OdinLookedAway, NoCheatFlag,
  KeepAchievements and similar): no conflict. With one of them the scan may already find nothing.
- **Server Devcommands**: its `disable_cheat_tracking` setting stops admins from creating new tags; this mod removes the
  ones that are already there. They work together.
- **MC mods**: items keep the data of Crossbow Stays Loaded, Dual Wielding and the other MC mods. Sort Chest merges a
  formerly tagged stack with a normal one after the clean-up, as it should.
