# Spyglass

Craft a bronze spyglass, hold it in your right hand and click to raise it to your eye: the view narrows and zooms in on
far land, trees and buildings. Aim with the mouse; you stand still while looking.

## Features

### The spyglass

- **A new item, crafted at the Forge** from 2 Bronze and 2 Crystal (the game has no glass, so the lenses are rock
  crystal, like the Viking-age Visby lenses). The recipe shows up once you know both materials and have been near a
  Forge, like any recipe. Server owners can change the materials, the station and its level.
- **A tool, like the hammer and the hoe.** It is held in the right hand and takes both hands (the tooltip says
  two-handed): equipping it puts away what you hold, shield or torch included, and equipping a weapon or a torch puts
  it away. Put away (R), it hangs where the hammer does. It weighs 1, never wears out and cannot be upgraded.
- **Its own look and icon**: telescoping bronze tubes with darker rings, a leather grip and a crystal lens, made by
  the mod itself (no extra files).

### Looking through it

- **Attack (left mouse button) raises it to your eye.** Your character lifts the spyglass to the right eye while the
  camera slides into your eye and zooms in, and the view closes in to the round spyglass view. Attack again, or Block
  (right mouse button), to lower it. With the HoldToLook setting it stays up only while you hold Attack.
- **The spyglass view:** a sharp round view in the middle of the screen, blurred and darker toward the edges. The
  crosshair and the name of what you point at are hidden; health, hotbar and map stay on screen.
- **Zoom:** 4 times by default. The mouse wheel zooms in and out while you look, from x1.5 up to x8 (the server's
  MaxMagnification). On a gamepad, hold the button that switches to the alternate keys and use the camera zoom buttons,
  as for the normal camera zoom. The zoom you pick is kept until you quit the game.
- **Aim, but no walking.** The mouse (or right stick) turns you as usual, but slower the stronger the zoom, so the view
  moves across the screen as fast as without the spyglass (AimSensitivity changes that). While the spyglass is up you
  cannot walk, run, jump, dodge, crouch, attack or block; your body turns to face where you look.
- **It lowers by itself** when you are hit (fire, poison, smoke and drowning damage do not count), staggered or
  knocked back, when you open the inventory, the map, the menu or the chat, and at once (without the animation) when
  you put it away, sit down, take a ship's helm, start swimming, die or teleport.
- **Other players see you** hold the spyglass to your eye (if they have the mod too).

### How far you see

- **With the Distant Horizons mod** (MC), the land is drawn all the way to the horizon, with far trees, rocks and
  buildings. While the spyglass is at your eye, Distant Horizons draws the land in finer detail and the far objects
  farther out in the direction you look, and in clear weather the haze is mostly taken away (FogClearing): you can pick
  out mountains, forests and buildings kilometres away.
- **Without it**, the spyglass zooms into what the normal game draws: about 300 to 500 metres of trees and buildings,
  and land fading into the fog. The fog is left as it is: behind it the normal game draws nothing more.
- **Creatures far away are never shown**, with or without Distant Horizons: the game only places creatures near
  players (a few hundred metres).

### Turning it off

Turn the mod off in the MC Mods panel (or set Enabled = false) at any time, even while you look: the view comes back at
once. Your spyglasses stay in your inventory, but the recipe is hidden and clicking with one does nothing until you
turn it on again.

## Configuration

The config file `BepInEx/config/MC.Exploration.View.Spyglass.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). In multiplayer the server's
(or host's) settings in the Recipe and Zoom sections are used for everyone; Enabled and the Controls and View sections
are each player's own, and AllowPlayersWithoutMod is read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or host). Off: players without the mod, with it turned off or with another version of it are refused. On: they may play, but never see spyglasses (on the ground, in chests or in hands), and a chest loses its spyglasses when such a player takes or adds an item in it. |
| Recipe | RecipeResources | `Bronze:2,Crystal:2` | Materials of one spyglass, as item names with amounts. Unknown names are skipped (with a warning in the log); with no valid material the recipe is hidden. Server's setting in multiplayer. |
| Recipe | RecipeStation | `forge` | Crafting station by its object name (`forge`, `piece_workbench`, `blackforge`, `piece_artisanstation`...). Empty: crafted from the inventory. Server's setting in multiplayer. |
| Recipe | RecipeStationLevel | `1` | Station level the recipe needs (1 to 10). Server's setting in multiplayer. |
| Zoom | MaxMagnification | `8` | Strongest zoom (2 to 20 times). The mouse wheel goes from x1.5 to this. Server's setting in multiplayer. |
| Zoom | FogClearing | `0.75` | How much of the haze the spyglass looks through in clear weather (0 to 1). Only with Distant Horizons; rain, storms and mist are never cleared. Server's setting in multiplayer. |
| Controls | HoldToLook | `false` | Off: click Attack to raise, click again to lower. On: it stays up only while you hold Attack. Block always lowers it. Your own choice. |
| Controls | StartMagnification | `4` | Zoom when you first raise it (1.5 to 20, never more than MaxMagnification). Your own choice. |
| Controls | AimSensitivity | `1` | How fast you aim while looking (0.25 to 2). 1 = the view moves on screen as fast as without the spyglass. Your own choice. |
| View | ClearViewSize | `0.7` | Size of the sharp round view as a share of the screen height (0.3 to 1). Your own choice. |
| View | EdgeBlur | `true` | Blur the view outside the sharp circle. Off: the edge is only darkened. Your own choice. |
| View | EdgeDarkness | `0.85` | How dark the screen gets outside the sharp circle (0 to 1). Your own choice. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. The spyglass is a new craftable item, so the server
refuses players who do not have the mod, have it turned off or have another version of it (their game shows
Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the server (or host) apply
to everyone. The zoomed view only changes your own screen; other players with the mod see you hold the spyglass to
your eye. Spyglasses only exist for games with the mod: a player without it never sees them (on the ground, in chests
or in hands), and a chest loses its spyglasses when such a player takes or adds an item in it. On a server without the
mod, spyglasses dropped on the ground can be deleted.

- **Players who cannot play by the server's rules are refused** about a second after they join; their game shows
  "Incompatible version". This covers a player without the mod, a player whose version of the mod cannot talk to the
  server's, and a player who has it turned off. A player who turns it off while connected is refused about a second
  later. With AllowPlayersWithoutMod = true they may play, but their game does not know the spyglass: they cannot see
  or pick up a dropped one, they see an empty right hand when you hold one, and a spyglass in a chest does not show
  for them, so when they take or add an item in that chest it is saved without the spyglass, which is then lost for
  everyone. Keep spyglasses out of chests such players use.
- **On a server without the mod** the mod turns itself off for you (the spyglass stays down, no recipe), and a
  spyglass you drop on the ground there can be deleted by the server.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"): the recipe, the strongest
  zoom and the fog clearing. Until they arrive after you join, the recipe is hidden and the spyglass stays down ("The
  server has not sent the spyglass settings yet."). Your own settings apply again in single player and when you host.
- **The view settings are yours:** how you raise it, the start zoom, the aim speed and the look of the round view
  only change your own game.
- **Other players see your pose:** a raised spyglass sets a small flag on your character that other games with the
  mod read to lift your arm the same way.
- **Distant Horizons** is a client-only mod: each player can use it or not. A server does not need it.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the Recipe and Zoom settings for everyone.

## Good to know

- **Far creatures, ships and carts never show**, whatever the zoom: the game only places them near players. Far trees,
  rocks and buildings come from Distant Horizons; when you join someone else's server, only from the areas you have
  been near this session (single player and the host see the whole explored world).
- **What you see is what the game draws.** Zooming does not make far land sharper than the game's (or Distant Horizons')
  level of detail; with Distant Horizons the looked-at direction gets finer land within a few seconds.
- **Blocking and clicking.** As with the hammer, Block with the spyglass in hand blocks with your fists (not while it
  is at your eye). A click with it never punches, also while the mod is turned off.
- **Your own body** is hidden while the camera is at your eye, so it never fills the view; it comes back as you lower
  the spyglass.
- **Ships:** you can look while standing on a moving ship, but not while steering it.
- **Grass** near you is not drawn farther away when you zoom (the game draws grass only around you).
- **Uninstalling** the mod removes every spyglass from inventories and chests the next time they load, as with any
  item a mod adds. Turning it off (Enabled = false) keeps them.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It adds one item (`MC_Spyglass`) and saves one temporary flag on your character while you
look; nothing else is stored.

- **Distant Horizons** (MC): far land and objects through the spyglass, plus finer detail and farther objects in the
  direction you look (its SpyglassDetail and SpyglassMaxBoost settings). Works without it, with the normal view
  distance.
- **Swim Dive** (MC): the spyglass cannot be raised while swimming (the game puts it away in water).
- **Dual Wielding** (MC): the spyglass is never part of a dual-wield pair; equipping it puts a pair away, as the
  hammer does.
- **Weapon Moveset** (MC): the spyglass has no attack, so its moves never start with it.
- **Tower Shield Wall** (MC): as with any tool, equipping the spyglass puts the shield away.
- **Sort Chest**, **Crafting Search and Sort** (MC): the spyglass is sorted with the tools (hammer, hoe).
- **Other MC mods:** no overlap.
- **First-person and camera mods** (FirstPersonMode, Landoria FirstPerson, ImmersiveFirstPerson, Customizable Camera,
  Valheim+ camera settings...): the spyglass places the camera after them and zooms from their field of view; when you
  lower it, theirs comes back.
- **Advize's Spyglass** and other zoom mods: a different item; do not raise both at once (each sets the camera's field
  of view).
