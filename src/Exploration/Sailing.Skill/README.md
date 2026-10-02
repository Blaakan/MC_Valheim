# Sailing Skill

Adds a Sailing skill, earned at the helm. The higher it is, the better your ship catches the wind at poor angles,
turns, stops and speeds up, the wider you reveal the map at sea, and the less damage your ship takes.

## Features

### A new skill: Sailing

Sailing appears in the Skills panel like the game's own skills, with its own icon and description, and levels up with
the game's usual level-up message ("Skill improved Sailing: N"). It works like the other skills: the Rested bonus (50%
more XP) and the world's skill-gain setting speed it up, and it drops when you die like the others (the normal game's
death penalty: 5% of the level, and the progress toward the next level).

- **How you earn it:** by steering. While you hold a ship's helm, every kilometre the ship travels gives 50 Sailing XP,
  under sail or paddling. Passengers earn nothing. Sailing 20 takes about 8 km at the helm, Sailing 50 about 73 km,
  Sailing 100 about 400 km.
- **Console:** `raiseskill Sailing 10`, `resetskill Sailing`, and `raiseskill all` / `resetskill all` include Sailing
  (Tab completes the name).

### What a higher level does

Every bonus grows evenly with your level: Sailing 50 gives half of the Sailing 100 numbers below, Sailing 0 is the
normal game.

- **Catch the wind at poor angles.** In the normal game the sail pulls best with the wind from the side, only 70% as
  well with the wind straight from behind or close to the bow, and not at all within about 37 degrees of the bow (the
  no-go zone). At Sailing 100 the worst usable angle still pulls 90%, and the no-go zone shrinks to about 26 degrees.
  You can never sail straight into the wind. The wind icon on the ship's HUD shows it: it stays whiter at poor angles.
- **Speed up faster.** At Sailing 100 the ship picks up speed 50% faster, under sail or paddling, and the sail fills
  and empties in about a second instead of about two. Only the push along the bow grows: this bonus adds no sideways
  push (no extra drift or heel). It does not raise the top speed either: you reach it sooner.
- **Turn faster.** At Sailing 100 the rudder swings 50% faster and the ship turns with 50% more force, under sail and
  paddling.
- **Stop faster.** At Stop, the ship actively brakes: at Sailing 100 its forward speed halves in about a second, on top
  of the water's drag (in the normal game it drifts a long way). Only while someone holds the helm.
- **See farther at sea.** While you are aboard a ship (on its deck or at its helm), you reveal the map in a wider
  circle: the normal game reveals 50 m around you, Sailing 100 reveals 100 m. This uses your own level, for your own
  map.
- **Protect the ship.** A ship takes less damage from collisions (ramming rocks or the shore), water impacts,
  creatures (serpents and the like), fire and the Ashlands sea: half at Sailing 100. The best sailor aboard counts, at
  the helm or not. Damage from players (including your own axe), a capsized ship's damage and weather wear are never
  reduced.

The handling bonuses (wind, speed, turning, stopping) use the level of whoever holds the helm, whoever else is
aboard.

### Turning it off

Turn it off in the **MC Mods** panel at any time: ships sail, reveal the map and take damage like the normal game
again at once, and you stop earning Sailing XP. Your Sailing level stays on your character (the skill stays
registered while the mod is off) and comes back into use when you turn the mod on again.

## Configuration

The config file `BepInEx/config/MC.Exploration.Sailing.Skill.cfg` is created the first time you launch the game with the
mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs); changes apply at once. In
multiplayer the server's (or host's) settings are used for everyone; AllowPlayersWithoutMod is read only by the server.
Every "AtMax" setting is the value at Sailing 100; lower levels get a share in proportion (Sailing 50 = half).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. Your Sailing level is kept while it is off. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: a player whose game does not have this mod, has it turned off or has another version of it is refused about a second after joining, or about a second after turning it off (their game shows "Incompatible version"). On: they may play; a ship their game simulates sails and takes damage like in the normal game (only the rudder of a helmsman who has the mod still swings faster), they earn no Sailing, and their level gives no bonus to ships they steer. |
| Skill | XpPerKm | `50` | Sailing XP the helmsman earns per kilometre the ship travels (0-1000; 0 = no XP). |
| Handling | WindFloorAtMax | `0.9` | How well the sail pulls with the wind straight from behind or close to the bow, compared with the best angle (0.7-1; the normal game gives 0.7). |
| Handling | NoGoShiftAtMax | `0.1` | Shrinks the no-go zone around the bow (0-0.15): 0.1 takes it from about 37 to about 26 degrees on each side, 0.15 to about 18. |
| Handling | AccelerationBonusAtMax | `0.5` | How much faster the ship picks up speed, under sail or paddling (0-2; 0.5 = 50% more push along the bow; the sail's sideways push is unchanged). It does not raise the top speed; the ship also slows down sooner when the push stops. |
| Handling | SailResponseAtMax | `1.5` | How much faster the sail fills and empties when you change speed or the wind changes, per second (0-5; 1.5 takes it from about 2 seconds to about 1). |
| Handling | TurnBonusAtMax | `0.5` | How much faster the ship turns, under sail and paddling (0-2; 0.5 = 50% more steering force). |
| Handling | RudderBonusAtMax | `0.5` | How much faster the rudder swings when you steer (0-2; 0.5 = 50% faster). |
| Handling | BrakeAtMax | `0.8` | Braking at Stop: share of the forward speed lost per second on top of the water's drag (0-3; 0.8 = the speed halves in about a second). Needs someone at the helm. |
| Map | RevealBonusAtMax | `1` | How much wider you reveal the map while aboard a ship (0-3; 1 = twice the normal radius: 100 m instead of 50 m). |
| Damage | DamageReductionAtMax | `0.5` | How much less damage a ship takes (0-0.9; 0.5 = half). The best Sailing level aboard counts. Damage from players, a capsized ship and weather wear are never reduced. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. It changes how ships sail for everyone on board, so
the server refuses players who do not have the mod, have it turned off or have another version of it (their game shows
Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the server (or host) apply
to everyone. The helmsman's Sailing level applies to the whole ship, and the best sailor aboard protects it from
damage. With AllowPlayersWithoutMod on, a ship simulated by the game of a player without the mod sails and takes damage
like in the normal game (only the rudder of a helmsman who has the mod still swings faster). Removing the mod removes
the Sailing skill from your character at its next save.

- **Refused players:** about a second after joining, a player whose game does not have the mod, has it turned off or
  has another version of it goes back to the menu with "Incompatible version"; the server log names them and the
  reason. Turning the mod off in the MC Mods panel while connected gets you refused the same way about a second later
  (turning it off and on again within that second is fine). The server (or host) can let such players in with
  AllowPlayersWithoutMod = true.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"); your own settings apply
  again in single player and when you host. Until the server's settings arrive (normally before your character
  appears), the mod changes nothing and you earn no Sailing XP.
- **Who runs what:** a ship is normally simulated by the game of one player aboard (usually the first to board; it
  can be a passenger, not the helmsman). Each player's game shares its own Sailing level with the others, so the game
  that simulates the ship uses the helmsman's level for the handling and the best level aboard for damage. The rudder
  is turned on the helmsman's own game, with their own level. Map reveal is each player's own.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): ships sail like
  the normal game and you earn no Sailing XP. Your level stays on your character.
- **Players without the mod** (only possible with AllowPlayersWithoutMod on, which also lets in players with the mod
  turned off): a ship their game simulates gets none of the bonuses that run on the simulating game (wind, speed-up,
  turning force, brake, damage); only the rudder of a helmsman who has the mod still swings faster (the rudder turns on
  the helmsman's own game). Their own level (if any) counts for nothing, and they earn no Sailing. A player with the
  mod who steers a ship simulated by their game gets the normal game's handling, except for their faster rudder.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the rules and it checks who joins. It normally does not simulate ships (the players' games do);
  a ship left near the world centre can be simulated by the server itself, which then uses the published levels the
  same way.

## Good to know

- **Your Sailing level lives on your character**, like the other skills. The mod keeps it while turned off. Removing
  the mod removes it: the game drops the unknown skill the next time the character loads and the level is lost at the
  next save. Only a character backup brings it back.
- **The helmsman steers the handling, the crew protects the ship.** A passenger with a high level makes the ship take
  less damage, but only the helmsman's level changes how it sails. With nobody at the helm, a ship keeps its sail
  setting and drifts or sails like the normal game (no brake).
- **Running downwind gets a real boost.** With the wind straight from behind, the sail pulls 0.9 instead of 0.7 at
  Sailing 100, about 29% more push. That also counts with Moder's power (the wind always blows from behind).
- **Back (rowing backwards) brakes harder too**, because the acceleration bonus also strengthens paddling.
- **Top speed with the wind from the side never changes.** The acceleration bonus makes a ship quicker to reach its
  speed, to turn and to stop, not faster. Only the wind bonus raises the speed at poor wind angles (running downwind
  about 13% faster at Sailing 100).
- **Skill numbers:** the bonuses use your level plus the "+N" that the Skills panel shows next to it for meads and
  other effects that raise all skills.
- **Every ship counts**, from the Raft to the ship built for the Ashlands. Only that last one ignores the Ashlands sea;
  on the others the Ashlands sea still hurts, only less at a high level.
- **The console command** `raiseskill sailing` raises this mod's skill; another mod that adds its own Sailing skill
  may react to the same name.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It adds the Sailing skill to your character's skills (a number the game saves with the
other skills) and a small value on each player (their Sailing level, for the other players' games). It changes no
ship, map or world data. Safe to add at any time; see "Good to know" before removing it.

- **Encyclopedia** (MC): reading its pages never adds or removes the Sailing skill.
- **Swim Dive** (MC): a swimmer next to a ship does not count as aboard (no wider map reveal); climbing back aboard
  does.
- **Sneak Ambush** (MC): Sneak XP is unchanged; holding still ends while a moving ship carries you, as before.
- **Creature Kill and Tame Counts** (MC) and the other MC mods: no overlap.
- **GrindstoneSkills**, **Smoothbrain's Sailing** and **ImpactfulSkills** (Voyager): they add their own sailing skill
  and bonuses. Both skills level up side by side and the bonuses stack; the log says which of them it found.
- **ValheimRAFT** and other mods that change how ships are simulated: this mod's handling bonuses may not apply to
  their ships.
- **Mods that list skills themselves** (skill UI mods that go through the game's list of skill names) may not show
  Sailing; the game's own Skills panel does.
- These combinations have not been tested in game yet.
