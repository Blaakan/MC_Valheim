# Swim Dive

Dive straight down while swimming. Hold **Crouch** to go down and **Jump** to come back up. Under water you move only
up or down, and diving costs stamina like swimming, even while you hold your depth. The camera follows you below the
surface, with an under-water tint and the water surface seen from below.

## Features

### Diving

- **Hold Crouch to dive.** While you swim at the surface, hold Crouch (Left Ctrl by default; on a gamepad the game's
  crouch button, the left stick press on the default layout) and you go straight down at your swimming speed.
  Swimming gear and effects that change your swimming speed change your diving speed too. You cannot start a dive with
  an empty stamina bar.
- **Hold Jump to come back up.** Under water, hold Jump (Space by default; the game's jump button on a gamepad) and you
  rise at the same speed. Near the surface you are handed back to normal swimming, which lifts you the last bit.
- **Let go to stay at your depth.** With neither key held you stay where you are (the server can make divers float up
  slowly instead, see IdleRiseSpeed). Holding both keys also keeps you in place under water; at the surface it leaves
  you floating on the waves as usual.
- **Only up or down under water.** Once you are half a metre below your normal swimming height, the movement keys do
  nothing: no forward, back or sideways swimming, and your character keeps facing the same way. Only Crouch and Jump
  move you. In that first half metre, while you are still going down, you can still swim normally.
- **The sea floor stops you.** You stop just above the floor (or at the bottom of the water, wherever the game's water
  ends) and hover there; Jump takes you back up.
- **Jump does not jump under water.** Touching a rock, the floor or a hull while diving does not turn the Jump key into
  a real jump; it only makes you rise.

### Stamina and breath

- **Diving costs stamina like swimming.** While you dive, stamina drains at the normal swimming rate (your Swim skill,
  gear and effects count, as when swimming), also while you hold still under water and while you touch the sea floor.
  Stamina does not come back while you dive (except while you touch a slope or rock of the sea floor when the server
  sets UnderwaterStaminaMultiplier to 0).
- **At the surface nothing changes.** Floating still at the surface costs nothing, as in the normal game, also while
  you hold Crouch where the water is too shallow to go down, or hold Crouch and Jump together: you then float and ride
  the waves like a normal swimmer.
- **Out of breath.** When your stamina runs out under water, "Out of breath" shows at the top left and you float up
  whatever you press. As when swimming with no stamina in the normal game, you take drowning damage every second, and
  stamina only comes back once you stand on the ground or reach the shore: surface with some stamina left.
- **Swim skill.** Moving up or down while diving raises the Swim skill like swimming does; holding still does not.

### Seeing under water

- **The camera follows you under the surface** once your head is under water, and stays a little below the surface so
  it never looks through it. Zoom and orbit work as usual. Setting UnderwaterCamera.
- **Under-water view.** While the camera is under water, the view is tinted blue-green and you see about 25 metres
  (darker at night and deeper down). Setting UnderwaterFog, with UnderwaterVisibility and UnderwaterFogColor.
- **The surface from below.** In the normal game the water surface cannot be seen from below (the sky shows through
  it). While the camera is under water, the mod turns the surface over so you see it from below. Setting
  SurfaceFromBelow.
- These are your own choices: they only change what your screen shows.

### Turning it off

Turn the mod off in the MC Mods panel (or set Enabled = false) at any time, even under water: the dive stops at once,
the normal game lifts you back to the surface, and the camera, fog and water surface are back to normal right away.

## Configuration

The config file `BepInEx/config/MC.Exploration.Swimming.Dive.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be changed
in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs). In
multiplayer the server's (or host's) settings in the Diving section are used for everyone; Enabled and the Visuals
section are each player's own, and AllowPlayersWithoutMod is read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or host). Off: players without the mod, with it turned off or with another version of it are refused. On: they may play, and swim like in the normal game (they cannot dive). |
| Diving | DiveSpeedMultiplier | `1` | How fast you go down and up, as a multiple of your swimming speed (0.25 to 3). Server's setting in multiplayer. |
| Diving | IdleRiseSpeed | `0` | Under water with no key held: 0 = you stay at your depth; above 0 = you float up this many metres per second (up to 2). Server's setting in multiplayer. |
| Diving | UnderwaterStaminaMultiplier | `1` | Stamina used while diving, as a multiple of the normal swimming drain (0 to 5). Swimming at the surface is not changed. Server's setting in multiplayer. |
| Visuals | UnderwaterCamera | `true` | The camera follows you under the surface. Off: it stays above the water as in the normal game. Your own choice. |
| Visuals | UnderwaterFog | `true` | Under-water tint and short view distance while the camera is under water. Your own choice. |
| Visuals | SurfaceFromBelow | `true` | Show the water surface from below while the camera is under water. Turn it off if the surface looks wrong on your graphics card. Your own choice. |
| Visuals | UnderwaterVisibility | `25` | How far you see under water, in metres (5 to 100). Thicker normal fog stays. Your own choice. |
| Visuals | UnderwaterFogColor | teal (`1A5261FF`) | Colour of the water in daylight; darker at night and deeper down. Your own choice. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. Diving changes what players can do in water, so the
server refuses players who do not have the mod, have it turned off or have another version of it (their game shows
Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the server (or host) apply
to everyone. Other players see a diver move up and down in the normal swimming pose. Nothing is saved on items,
creatures or buildings.

- **Players who cannot play by the server's rules are refused** about a second after they join; their game shows
  "Incompatible version". This covers a player without the mod, a player whose version of the mod cannot talk to the
  server's, and a player who has it turned off (Enabled = false, or unticked in the MC Mods panel). A player who turns
  it off while connected is refused about a second later. The server (or host) can let such players in with
  AllowPlayersWithoutMod = true: they swim like in the normal game, and the server log names each of them.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"). Until they arrive after you
  join, you swim like in the normal game and Crouch does not dive. Your own settings apply again in single player and
  when you host. When the server's settings change, players get them half a second after the last change.
- **Each player's own game runs their dive**, as with all movement in Valheim. Other players see you go down and up
  (with or without the mod), in the normal treading-water pose, with the usual ripple on the surface above you.
- **The view settings are yours:** the camera, fog and surface from below only change your own screen.
- **On a server without the mod** the mod turns itself off: you swim like in the normal game.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the Diving settings for everyone.

## Good to know

- **Diving is an escape.** Serpents and other swimming creatures cannot dive: they keep to their swimming depth near
  the surface, so deep enough they cannot reach you. Your stamina decides how long you can stay down.
- **Under a ship, a dock or a rock**, where something solid is between you and the surface, you can swim sideways
  under water, so you can always get out from under it.
- **Hands are empty in water**, as in the normal game: your weapons are put away while swimming. You hover just above
  the sea floor, so they stay put away there too; only where you touch a slope or a rock does the normal game let you
  take them out (with the hide key).
- **Just under the surface** the camera comes close to you, and your character may turn see-through like in first
  person. It moves back as you go deeper.
- **Tar pits** cannot be dived into; only water. The hot water of the Ashlands still burns you while diving.
- **Ships and chairs:** you cannot dive while standing on a ship or sitting; jump into the water first.
- **Crouch at the surface** only starts a dive; it never makes you sneak in water (the normal game cancels crouching
  while swimming anyway).
- **Logging out under water** saves your place; when you come back, the normal game lifts you to the surface.
- **Where the game's water ends.** Deep water in Valheim is a water zone of a fixed depth: about 50 metres below the
  surface of the sea. If the sea floor is deeper than that somewhere, you stop at the zone's bottom instead of the
  floor.
- **Diving speed.** You reach full diving speed after about a second; a dive of a few metres goes down at about 1.6
  metres per second on average with the normal swimming speed of 2 metres per second.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It stores nothing of its own in the world, on characters or on items, so it is safe to add
or remove at any time.

- **Weapon Moveset** (MC): Jump under water never counts as a jump, so there is no jump attack from a dive.
- **Dual Wielding** (MC): swimming hides both hands as in the normal game, also while you hover above the sea floor;
  where you touch a slope or a rock its hand rules see you standing, like the normal game does.
- **Sneak Ambush** (MC): Crouch while swimming dives, it never sneaks (the normal game cancels crouching in water). Its
  fog bonus is read from the weather, not from the screen, so the under-water fog does not hide you from creatures.
- **Other MC mods:** no overlap.
- **BetterDiving, Dive In, UnderTheSea, VikingsDoSwim and the original Valheim Diving Mod** do the same job: with any
  of them installed this mod stays off (the MC Mods panel says "Inactive: ... also handles diving. Remove one of
  them.") and a server that requires this mod refuses that game. Keep one diving mod.
- **Underwater (Crystal)**: works next to it. While its walk-on-the-sea-floor mode is on you do not swim, so you cannot
  dive; its under-water camera gets this mod's tint and surface from below.
- **NoUnderwaterCamera** keeps the camera above water, so the under-water view never shows; diving still works.
- **Improved Swimming**: works next to it; you dive at the swimming speed and stamina drain it gives you (it is probably
  not updated for Valheim 1.0).
- **ImpactfulSkills**: works next to it; its lower swim stamina cost also applies while diving, but its Swim speed
  bonus only speeds up surface swimming, not diving. Its higher jumps never happen under water (Jump only makes you
  rise while diving).
- **Aegir** and other camera mods that change how close the camera may get to the water: this mod only changes it for
  the moment it places the camera and always puts their value back.
- **Weather and fog mods** (for example Seasonality): the under-water fog is worked out from whatever fog the game (or
  the other mod) has just set, and their fog is put back when you surface.
