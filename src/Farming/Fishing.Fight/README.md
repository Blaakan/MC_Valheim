# Fishing Fight

A new way to reel in a fish. Once a fish is on the hook, a catch bar appears: keep the fish inside its green catch zone
and the line comes in for free. Every few seconds the fish fights: the bar goes away and the fish runs to one side. Turn so your
rod points the other way and reel, or it takes line back. Casting, bait and the bite work as in the normal game.

## Features

### The catch bar (calm fish)

- **A catch bar next to the crosshair.** As soon as a fish is hooked, a vertical bar shows to the right of the
  crosshair. The fish's own icon moves up and down in it on its own, and the green catch zone is the part you move.
- **Hold Block to raise the zone, let go to let it fall.** Block is the normal reel button (right mouse button; the
  game's block button on a gamepad, left trigger on the default layout). The zone speeds up while you hold it, falls
  when you let go and bounces a little on the bottom, like the Stardew Valley fishing bar.
- **Fish in the zone = the line comes in for free.** While the fish's icon is inside the zone (it turns green), the
  line reels in on its own and costs no stamina at all, so your stamina comes back (a bit slower while you hold Block,
  as in the normal game).
- **Fish out of the zone = no line, and stamina drains.** The zone turns orange, no line comes in, and you lose
  stamina at the normal game's reeling cost.
- **The line meter** beside the bar fills up as the line comes in; the usual line length (in metres) still shows in
  the middle of the screen.

### The fight (struggling fish)

- **The fish fights every few seconds.** The catch bar goes away and the hooked fish runs hard to your left or right:
  you see the fish, the float and the line move that way, with splashes. There is no on-screen hint by default: watch
  the float and the line. When the fight ends, the catch bar comes back.
- **Point your rod the other way and reel.** Your rod points where your character faces (while you hold Block your
  character turns with the camera). If the fish runs to your right, turn left, at least 30 degrees off the line, and
  hold Block: the line comes in and costs stamina like reeling a fighting fish in the normal game.
- **Reeling the wrong way costs a lot.** Pointing the rod toward the side the fish runs to, or straight at it, while
  you reel costs four times as much stamina, and no line comes in.
- **Not reeling lets the fish take line.** If you let go during a fight, nothing costs stamina, but the fish takes line
  back (about 1 to 2 metres per second, faster for harder fish). If the line gets longer than 30 metres, it breaks.
- **The fish can change sides** in the middle of a fight, more often for harder fish: turn your rod the other way
  again. A fish that runs into shallow water turns around when the other way is deeper.
- **Staying in view.** While you reel, the view turns with your rod: about 30 to 45 degrees off the line should keep
  the float at the side of the screen; you can turn further if you like.

### What makes a fish hard

- **The species.** Fish from later biomes are harder: the Perch of the Meadows is the easiest, the Northern Salmon of
  the Deep North the hardest. Hard fish move more wildly in the bar, fight more often and for longer, take line faster
  and change sides more often. Some fish have their own style: Pike, Tetra and Magmafish dart, Giant Herring and Tuna
  glide, Grouper and Anglerfish sink, the Pufferfish floats up.
- **Stars.** Each star makes the fish harder and its fights longer.
- **Your Fishing skill** makes the zone bigger (about a quarter of the bar at skill 0, more than a third at 100),
  lowers the stamina costs and reels faster, as in the normal game.
- Fish from other mods get the difficulty of the biome they swim in.

### Same as the normal game

- Casting, bait, which fish bite, and hooking a fish (reel or jerk the float right after a nibble) do not change.
- **Out of stamina = the fish gets away** ("Lost catch"). The float stays in the water.
- **The line breaks** when the float is pulled too far, or when you walk too far away (30 m).
- **Attacking or drawing a bow** cancels the fishing, as in the normal game.
- **The Fishing skill** rises while the line comes in (in the zone, or reeling the right way in a fight).
- Reeling in an empty line (no fish yet) is the normal game's reel.

### Turning it off

Turn the mod off in the MC Mods panel (or set Enabled = false) at any time, even with a fish on the hook: the bar goes
away and the normal game's reel takes over the fish where it is.

## Configuration

The config file `BepInEx/config/MC.Farming.Fishing.Fight.cfg` is created the first time you launch the game with the
mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be changed
in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs). In
multiplayer the server's (or host's) settings in the Fight section are used for everyone; Enabled and the Display
section are each player's own, and AllowPlayersWithoutMod is read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or host). Off: players without the mod, with it turned off or with another version of it are refused. On: they may play, and fish like in the normal game. |
| Fight | BarSize | `0.24` | Height of the catch zone at Fishing 0, as a part of the bar (0.1 to 0.8). Server's setting in multiplayer. |
| Fight | BarSizeAtMaxSkill | `0.38` | Height of the catch zone at Fishing 100 (0.1 to 0.8). Server's setting in multiplayer. |
| Fight | FishDifficulty | `1` | How hard every fish is, as a multiple (0.25 to 2). Server's setting in multiplayer. |
| Fight | OffBarStamina | `1` | Stamina used per second while the fish is outside the zone, as a multiple of the normal game's reeling cost (0 = free, up to 5). Server's setting in multiplayer. |
| Fight | StruggleStamina | `1` | Stamina used while reeling a fighting fish the right way, as a multiple of the normal game's cost (0 to 5). Server's setting in multiplayer. |
| Fight | WrongSideStamina | `4` | Reeling a fighting fish the wrong way costs this many times more, with no line in (1 to 10). Server's setting in multiplayer. |
| Fight | RodAngle | `30` | How many degrees off the line your rod must point, away from the fish's run (10 to 90). Server's setting in multiplayer. |
| Fight | CalmSeconds | `7` | Average time between two fights, in seconds (2 to 30). Server's setting in multiplayer. |
| Fight | StruggleSeconds | `3` | Average length of a fight, in seconds (1 to 10). Stars add half a second each. Server's setting in multiplayer. |
| Fight | LineRunSpeed | `1.5` | Metres of line per second a fighting fish takes while you do not reel (an average fish; 0 to 5). Server's setting in multiplayer. |
| Fight | ReelSpeed | `1` | How fast the line comes in, as a multiple of the normal game's reel speed, which rises with your Fishing skill (0.25 to 4). A hooked fish also slows the reel down as in the normal game. Server's setting in multiplayer. |
| Display | ShowStruggleArrow | `false` | While the fish fights, show an arrow beside the crosshair pointing the way to turn your rod; it is red until your rod points far enough that way, then green. Your own choice. |
| Display | BarScale | `1` | Size of the catch bar on screen (0.5 to 2). Your own choice. |
| Display | BarOffsetX | `260` | Distance of the catch bar to the right of the screen centre (negative = left). Your own choice. |
| Display | BarOffsetY | `0` | Distance of the catch bar above the screen centre (negative = below). Your own choice. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. The fishing fight changes stamina costs and how
fish are caught, so the server refuses players who do not have the mod, have it turned off or have another version of
it (their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the
server (or host) apply to everyone. The fight runs on the fisher's game; other players see the hooked fish run and
splash. Nothing is saved on items, creatures or buildings.

- **Players who cannot play by the server's rules are refused** about a second after they join; their game shows
  "Incompatible version". This covers a player without the mod, a player whose version of the mod cannot talk to the
  server's, and a player who has it turned off (Enabled = false, or unticked in the MC Mods panel). A player who turns
  it off while connected is refused about a second later. The server (or host) can let such players in with
  AllowPlayersWithoutMod = true: they fish like in the normal game, and the server log names each of them.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"). Until they arrive after you
  join, you fish like in the normal game. Your own settings apply again in single player and when you host. When the
  server's settings change, players get them half a second after the last change.
- **Each fisher's own game runs their fight**, as in the normal game (the fisher owns the float and takes the fish
  when it bites). Other players see the fish run to its side and splash while it fights, with or without the mod.
  If a fisher leaves the game in the middle of a fight, the fish swims off, and it stops splashing a few seconds later
  once a player with the mod is near it (in the normal game such a fish keeps splashing until someone catches it).
- **The Display settings are yours:** the bar's size and place and the arrow only change your own screen.
- **On a server without the mod** the mod turns itself off: you fish like in the normal game.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the Fight settings for everyone.

## Good to know

- **Where to look.** While you hold Block your character turns with the camera, so turning the rod away from the fish
  turns your view too. Around 30 to 45 degrees should keep the float in sight; the float and the line show where the
  fish is going.
- **Toggle block.** With the game's accessibility option "Toggle block" on, Block toggles the reel instead of holding
  it: the zone keeps rising until you press Block again.
- **Block and Jump** while reeling is a dodge, as in the normal game. During the roll you are not reeling: in a fight
  the fish takes line, and the catch zone falls.
- **Pausing.** In single player the Esc menu pauses the fight. With the inventory or the map open you cannot reel, so
  the zone falls, as in the normal game where you cannot reel there either (with Toggle block on, the reel stays on
  while the inventory is open).
- **A full inventory**: the caught fish stays where you pulled it in, as in the normal game.
- **Several floats** (mods that let you cast more than one): only one fish at a time gets the catch bar; another hooked
  float on the same player reels as in the normal game.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It stores nothing of its own in the world, on characters or on items, so it is safe to add
or remove at any time.

- **Other MC mods:** none of them changes fishing. **Swim Dive**: swimming puts the rod away and ends the fishing, as
  in the normal game. **Trinkets on Demand**: its gamepad trigger (LT + right stick press) works while you reel; LT
  stays the reel. **Harpoon Hooks Tames**: casting or reeling releases a harpoon line, as in the normal game.
- **Trolling Fishing, PeasFishing, ChillHook and ChillFishing** also run the hooked fight: with any of them installed
  this mod stays off (the MC Mods panel says "Inactive: ... also handles the fishing fight. Remove one of them.") and a
  server that requires this mod refuses that game. Keep one.
- **GrindstoneSkills** and **Hooked**: their own fishing fight must be off ([50 - Fishing] Fishing Enabled = false in
  GrindstoneSkills, [2 - Minigame] Enabled = false in Hooked). While it is on, this mod stays off; turning it off (in
  ConfigurationManager, or from the server for GrindstoneSkills' synced setting) turns this mod on at once. After
  editing their config file by hand, restart the game.
- **EpicLoot** (fishing stamina shard), **FeastMaster** and **Reely Good Rod**: their lower fishing stamina costs
  apply to this mod's costs too.
- **ComfyFishing**: its own better rods keep its auto-reel (with its default VanillaRodVanillaReel setting); the
  normal fishing rod gets this mod's fight.
- **Angler's Eye**: its smart reel turns itself off next to this mod; its "struggling" hint still shows.
- **ValheimVR**: not tested in VR; the rod direction is the way your body faces.
