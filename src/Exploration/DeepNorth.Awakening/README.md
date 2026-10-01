# Deep North Awakening

The Deep North starts as quiet as in the normal game. Break the first Malicious Ice at the bottom of a Mörkhalla and
the Jotun army wakes up, but instead of one invasion far away, it invades the Deep North itself. Hidden invaded areas
spread with every Malicious Ice you break, full of Krigen, Hexen and Elaking, with blizzards and Fimbul meteors, and
the creatures of the north fight back. From the third Malicious Ice on, the Jotun also march on the rest of the world.

## Features

### A quiet north until you break the ice

Until the first Malicious Ice of a Mörkhalla is broken, the Deep North is exactly the normal game: few creatures, the
frozen calm. Nothing of this mod shows yet.

### Each Malicious Ice wakes more of the north

The world counts the Malicious Ice broken in Mörkhalla dungeons (anyone's, the whole world shares one count). Every one
shows the normal "The Jotun Advance":

| Malicious Ice broken | What happens |
|---|---|
| 1 | About 20 % of the Deep North is invaded. No invasion starts elsewhere. |
| 2 | About 40 % invaded, and the Jotun there are more often 1 star. No invasion elsewhere. |
| 3 | About 70 % invaded, more 2-star Jotun, and 3 normal Jotun invasions start in the world. |
| 4 and more | One normal Jotun invasion starts, as in the normal game. |

The normal game never runs more than 3 Jotun invasions at once; that limit stays.

### The invaded areas

- **Where the Jotun army spawns.** Krigen (sword or greataxe), Krigen with two axes, Hexen and Elaking appear around
  you in an invaded area, day and night, about as many as in a Jotun invasion: up to 3 Krigen, 2 dual-axe Krigen,
  2 Hexen and 6 Elaking within about 160 m. They come back over time when killed.
- **No markers.** The areas have no map pin and no Malicious Ice at their centre, and cannot be stopped or destroyed.
  You find them by the Jotun, the storms, and the purple on your map once you have explored there (below).
- **The same for everyone.** The areas are worked out from the world itself: the same places for every player, every
  time you load the world. Each new stage keeps the areas of the stage before and adds more, so the first pockets grow
  and merge (a later stage never covers less, whatever the settings).
- **Stronger at each stage.** Chance of stars for the Jotun in the areas: stage 1 as in the normal game (about 9 %
  one star, 1 % two stars), stage 2 about 19 % one star and 6 % two stars, stage 3 about 25 % one star and 20 % two
  stars. Krigen can get two stars, Hexen and Elaking one. The world modifiers for enemy level-ups still apply on top.
- **Never inside your base.** As with every creature in the game, they do not appear inside the area of a workbench or
  a fire, nor within 40 m of a player.

### On the map

The invaded areas show on the map and the minimap as a purple tint over the Deep North land they cover, only where
you have explored (the fog of war still hides the rest). Areas that touch merge into one purple region with a darker
border, never a pile of separate circles. After Kall, each area you clear disappears from it, so you chip the purple
away. Turn it off with ShowAreas in the Map section (the server's setting is used in multiplayer); the areas are then
hidden again.

### Storms

Each invaded area storms about a third of the time, for 5 to 10 minutes at a time, on its own schedule (the same for
every player): while you stand in it, the Deep North blizzard blows, and Fimbul meteors fall around you like in a Jotun
invasion (never within 40 m of a player or inside a base, but they hurt and can break what they hit). Storms stop at
the edge of the Deep North: the land and sea beyond it keep their own weather.

### Nature fights back

Once the north is awake, Gammeltroll, Barka and frost Greydwarfs (and their shamans) are enemies of the Jotun army,
wherever they meet. Now and then a band of them appears in an invaded area to fight the Jotun: 5 to 10 frost
Greydwarfs (1 or 2 of them shamans) and up to 2 big ones (Gammeltroll or Barka, in any mix). There is never more than
one band around you at a time. They still attack players too.

### After Kall Fimbulbringer

Once Kall is defeated, the invaded areas stop spawning over time. The area you stand in when Kall falls (and the areas
next to you that still have Jotun) keep the Jotun they have: nothing new appears there while you stay around. After
that, each time you walk into an area that is not cleared, its Jotun appear at once, up to its usual numbers (all of
its Jotun still alive count, wherever they are), and then it waits to be defeated: nothing more appears until every
player has gone far enough for it to unload (about 750 m from its centre). Leave without clearing it and its Jotun
come back the next time you walk in. Kill every Jotun of an area and it is cleared for good: no more Jotun, storms or
meteors there, ever. If you stand in it when that happens: "The Jotun Retreat". Areas nobody visited yet still have
their Jotun the first time you walk into them.

### Worlds that already broke Malicious Ice

The first time the mod runs on a world, the server counts the explored Mörkhalla whose Malicious Ice is already gone
and starts at that stage, without messages and without starting invasions.

### Admin command

`deepnorth_stones` (F5 console) shows the number of Malicious Ice broken, the stage and how many areas are awake, on any
player's game (after Kall also the area you stand in: its living Jotun and whether it waits to be defeated or is
cleared); `deepnorth_stones <count>` sets the count (0 to 100) without messages or invasions. On a multiplayer
client the new count is sent to the server, which needs you to be an admin (its answer goes to the server's log; run
`deepnorth_stones` again to see the count).

### Turning it off

Turn the mod off in the MC Mods panel (or set Enabled = false) at any time: the Deep North is the normal game again at
once (a Malicious Ice starts one normal invasion, no area spawns, normal weather, nature and Jotun at peace). The world
keeps its count of broken Malicious Ice and uses it again when you turn the mod back on. Creatures already spawned
stay.

## Configuration

The config file `BepInEx/config/MC.Exploration.DeepNorth.Awakening.cfg` is created the first time you launch the game
with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be
changed in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs). In
multiplayer the server's (or host's) settings in the Awakening and Storms sections are used for everyone; Enabled is
each player's own; AllowPlayersWithoutMod and the Invasions section are read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or host). Off: players without the mod, with it turned off or with another version of it are refused. On: they may play; their game runs the Deep North like the normal game. |
| Awakening | CoverageStage1 | `20` | Share of the Deep North invaded after the first Malicious Ice, in percent. |
| Awakening | CoverageStage2 | `40` | Share invaded after the second, in percent. |
| Awakening | CoverageStage3 | `70` | Share invaded after the third and later, in percent. |
| Awakening | StarChanceStage1 | `10` | Chance per star roll of the area Jotun at stage 1, in percent (normal game: 10). 0 = no stars. |
| Awakening | StarChanceStage2 | `25` | Chance per star roll at stage 2. |
| Awakening | StarChanceStage3 | `45` | Chance per star roll at stage 3 and later. |
| Awakening | JotunDensity | `100` | How many Jotun an area holds, in percent of the normal amount (25 to 400). |
| Awakening | NatureFightsBack | `true` | Gammeltroll, Barka and frost Greydwarfs fight the Jotun once the north is awake, and the areas spawn bands of them. |
| Awakening | NatureBandChance | `10` | Chance, in percent, of a nature band each time a place in an invaded area checks (every 20 minutes per place, at once on the first visit). 0 = no bands. |
| Storms | StormShare | `33` | Share of the time an area storms, in percent. 0 = never. |
| Storms | StormMinMinutes | `5` | Shortest storm, in real minutes (1 to 60). |
| Storms | StormMaxMinutes | `10` | Longest storm, in real minutes (1 to 60). |
| Storms | Meteors | `true` | Fimbul meteors fall during storms. Off = blizzard only. |
| Map | ShowAreas | `true` | Show the invaded areas as a purple tint on the map and minimap, only where explored; cleared areas disappear from it after Kall. Off = the areas stay hidden. |
| Invasions | InvasionsAtThirdStone | `3` | Normal Jotun invasions started when the third Malicious Ice breaks (0 to 10). Later ones start one each. Server only. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. The awakening changes creature spawns, weather,
creature fights and the Jotun invasions for the whole world, so the server refuses players who do not have the mod,
have it turned off or have another version of it (their game shows Incompatible version), unless its
AllowPlayersWithoutMod setting is on, and the settings of the server (or host) apply to everyone. The world remembers
how many Malicious Ice were broken in the global key mc_dn_stones.

- **Players who cannot play by the server's rules are refused** about a second after they join; their game shows
  "Incompatible version". This covers a player without the mod, with another version of it, or with it turned off. The
  server (or host) can let such players in with AllowPlayersWithoutMod = true; the server log names each of them.
- **The server counts the Malicious Ice** and decides the stage, the messages and the invasions, whoever broke it.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"). Until they arrive after you
  join, your game runs the Deep North like the normal game.
- **The creatures of an area are spawned by the game of a player near them**, like every creature in Valheim; they are
  normal creatures that every player sees and fights.
- **Weather is each player's own**, as always in Valheim: every player in the same storming area gets the blizzard.
- **The map tint is drawn by each player's game** from the server's ShowAreas setting, on their own explored map.
- **A player let in without the mod** (AllowPlayersWithoutMod on) sees the normal weather and causes no area spawns
  where only they are. If they break a Malicious Ice, every player gets "The Jotun Advance" (twice: from their game and
  from the server), the server counts the stage as usual, and no extra invasion starts.
- **After Kall, the server keeps one list for everybody**: which areas are cleared and which wait to be defeated. Two
  players walking into the same area from two sides get one set of Jotun, not two, and an area stays waiting while any
  player is near it. A player who joins later (or turns the mod back on) gets the lists at once.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the settings for everyone.

## Good to know

- **"Kringen" is Krigen.** The two Krigen are `JotunWarrior` (sword or greataxe) and `JotunWarriorDualWield` (two
  axes); Hexen is `JotunWitch`.
- **Near a Mörkhalla** the Jotun inside the dungeon count toward an area's limit, so fewer spawn right at its entrance.
- **Raids and boss fights win over the storms**: during a Jotun raid, a boss fight or near a normal Jotun invasion,
  you get their weather, not the area blizzard.
- **Server log.** The server logs each Malicious Ice ("A Malicious Ice was broken at ..."), the invasions it starts and,
  after Kall, every area that becomes cleared.
- **Cleared areas are remembered in the world**, on the game's own zone data, so they stay cleared after a restart and
  for every player.
- **The map tint is never saved.** The map files on disk keep their normal colours; turning the mod off gives the map
  its normal colours back at once. Right after a world loads, the tint appears within about a second.
- **The count is a global key** (`listkeys` shows `mc_dn_stones`). Removing it (`removekey mc_dn_stones`) makes the
  server count the explored Mörkhalla again right away.
- **Another mod or an admin starting a Jotun invasion** (`pevents start jotun_invasion`) still works, about 5 seconds
  later. Only a Malicious Ice broken in the 5 seconds after such a request can cancel it (it is then taken for that
  stone's own request).
- **Right after Kall** (and in the first 30 seconds after the world loads or the mod is turned back on) an area whose
  last Jotun dies is cleared only once those 30 seconds are over: the server first makes sure no player's game spawned
  a Jotun there just as Kall fell.
- **After a server restart** (after Kall), every area that is not cleared spawns its Jotun again when you walk into it,
  even one you left a moment before the restart (the "waiting to be defeated" list is not saved; the cleared list is).
- **An area counts the Jotun it spawned**, wherever they wander, loaded or not. A Jotun of an area that follows you far
  out and is left behind when you go (it stays where it was until that place loads again, then walks back toward its
  area) still has to die for that area to be cleared, and counts toward its numbers when you walk in again.
- **Dying in an area** does not make it spawn again when you come back to a bed inside it: while you wait to respawn,
  the server counts you where you will respawn.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It keeps one global key in the world (`mc_dn_stones`), a number on the creatures the areas
spawn and, after Kall, the cleared areas on the game's zone data; removing the mod leaves all of them harmless (the
normal game ignores them).

- **Creatures Morale** (MC): the Jotun are never afraid (no rank in the Deep North), so area Jotun never flee; nature
  creatures that fight them follow Morale's normal rules.
- **Sneak Ambush** (MC): area creatures do not hunt players, so sneaking works on them as on any creature.
- **Creature Kill and Tame Counts** (MC): area kills count under the normal creature names.
- **Encyclopedia, Container Sort** (MC): unchanged (the area spawns are not in the game's spawn lists).
- **Other MC mods:** no overlap.
- **Expand World Data, Spawn That, DropNSpawn**: they edit the game's spawn lists; the area spawns live outside those
  lists and keep working.
- **StarLevelSystem, Creature Level and Loot Control, ReefLevels, CreatureManager, ServersideQoL CreatureLevelUp**
  decide creature levels themselves: the stage star odds then are theirs (the log says so at start). Mob_Cap's star
  cap applies.
- **World Advancement Progression** blocks unknown global keys by default: add `mc_dn_stones` to its allowed keys, or
  the Deep North never awakens (the log warns).
- **HexenBeGone** blocks Hexen from natural spawns by default, which includes the area Hexen.
- **Map mods**: mods that redraw the map in the normal way, or change its resolution, keep the purple tint. A map
  texture stored in another format than the normal game's gets no tint (the log says so).
