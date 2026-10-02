# Switchable Lights

Torches, sconces, lanterns and candles no longer need fuel: press E to switch them on or off. Fires you can cook on
(campfires, braziers, the hearth, the bonfire) still burn fuel.

## Features

- **Lights never run out.** A light that is on stays on for good, with no resin, coal, guck or greydwarf eyes. New
  lights come on full as soon as they are built.
- **E switches a light on or off.** Look at a light: its name shows **( On )** or **( Off )**, and **[E] Turn off** or
  **[E] Turn on**. Switching takes nothing from your inventory. Holding E does not make it flicker, and Shift+E switches
  it too.
- **Lights covered by the switch** (the default list): Standing wood torch, Standing iron torch, Standing
  green-burning and blue-burning iron torches, Sconce, Jack-o-turnip, Snow lantern, Resin candle, and the unlit
  standing torches found in the world.
- **Fires for cooking and crafting stay as they are**: the campfire, the iron fire pit, the hearth, the bonfire and
  the braziers still take fuel and burn it. A fire you can cook on (or light a cauldron with) always stays a normal
  fire, even if you add it to the list (the log says so). Smelters, kilns, furnaces, ovens and the hot tub are not
  fires and are not touched.
- **The resin candle** switches off the way the game already switches it off, and it never burns down. A candle that
  burned out before you installed the mod comes back on with E.
- **Lights another mod keeps burning without fuel** (infinite-fire mods) are left to that mod: this mod does not
  switch them.
- **Blocked lights**: a light that is on but cannot burn (too close to a roof or to the ground above it, or under
  water) says **( On, blocked )**.
- **Other mods' pieces**: any fire that works like a torch can be added to the list by its prefab name.
- Turn it off in the **MC Mods** panel at any time: lights take fuel and burn it again, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Building.Lights.Switchable.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | Lights | every vanilla torch, sconce, lantern and candle (see below) | Pieces that become switchable lights: comma-separated prefab names (the names the spawn command uses). Pieces from other mods can be added if they are fires that work like a torch. A fire that can be used for cooking always keeps burning fuel, even when listed. In multiplayer the list of the server (or host) is used for everyone. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or the host). Off: a player whose game does not have the mod installed is refused about a second after joining (their game shows "Incompatible version"). The check only looks at whether the mod is installed: a player who turned it off on their own game is not refused. On: players without the mod may play; the lights their game runs burn fuel the normal game way. |

Default `Lights`: `piece_groundtorch_wood, piece_groundtorch, piece_groundtorch_green, piece_groundtorch_blue,
piece_walltorch, piece_jackoturnip, piece_snowlantern, Candle_resin, CastleKit_groundtorch_unlit`.

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game: the server refuses players without it, because the
lights their game runs would burn fuel and go out, and its list of lights applies to everyone. Players without the mod
(only with AllowPlayersWithoutMod on) see the same lights on or off; a light switched off simply looks empty to them
and they can refuel it. On a server without the mod it turns itself off.

- **Why everyone:** in Valheim, the game of one nearby player runs each light (its "owner"). If that player has the
  mod, the light burns nothing. If not, it burns fuel at the normal rate and can go out; anyone with the mod can
  switch it back on for free. When you switch a light, your game becomes its owner.
- **Players without the mod are refused.** About a second after such a player has joined, the server refuses them:
  their game goes back to the main menu with the game's own message "Incompatible version" (the game cannot show a mod
  name there). The server's log names the player and says why. To let them play anyway, set
  `AllowPlayersWithoutMod = true` on the server. Setting it back to `false` refuses the ones who are online about a
  second later.
- **Players without the mod** (when allowed in) see exactly the same lights: on is a full light, off is an empty one.
  They can refuel an off light with the usual fuel, which turns it on for everybody. They cannot switch a light off,
  except the resin candle, which works as in vanilla for them (E switches it on or off; it never needs refuelling).
  The lights their game runs burn fuel as usual.
- **The server's list applies to everyone.** When you join a server that has the mod, your game uses its `Lights`
  list and says so once in its log ("Using the server's ..."). Your own settings file is not changed; your own list
  applies again in single player and when you host. `AllowPlayersWithoutMod` is read only by the server.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): lights behave as
  in vanilla.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder. The server
  itself never runs lights (the players' games do): it checks that each player has the mod and sends them its list.
- The mod adds nothing to the world save: on and off are the game's own fuel and on/off values.

## Good to know

- **Braziers keep needing coal or greydwarf eyes**: they give warmth, count as a fire for resting and beds, and can
  light a cauldron, so a free one would be a free cooking fire. The same goes for any fire you can cook on, also from
  other mods: it stays a normal fire even when listed. Torches and the sconce still scare creatures that fear fire and
  keep the Mistlands mist away while lit; the Jack-o-turnip and the snow lantern give comfort.
- **Rain (or strong wind with little cover) puts the resin candle out** when it is not under a roof, as in vanilla. It
  cannot be lit again while it is still wet: press E once the rain stops, or move it under a roof.
- **Anyone can switch any light**, also inside someone else's ward, just as anyone can refuel a light in vanilla.
- In the Ashlands, or with the Fire world modifier, a burning torch can set nearby things on fire, as in
  vanilla; a light that never goes out keeps that risk.
- **Removing the mod:** lights that are on stay full and start burning fuel normally (a full torch lasts many hours).
  Lights that are off are simply empty and take fuel again. Resin candles you switched off can be switched on with E
  in vanilla too.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing of its own in the world, on characters
or on items.

- **MC mods:** **Batch Station Feeding** leaves switchable lights alone (no Shift+E batch, no batch hint on them): there
  is no fuel to add. It still feeds campfires, the hearth and the bonfire. No other MC mod touches fires.
- **Other infinite-fire or light mods** (ValheimInfiniteFire, TimedTorchesStayLit, FuelDaylightSaving,
  HexInfiniteFuel, ServersideQoL PrefabConfigurator, the fire settings of ValheimPlus): they do the same job in their
  own way. Use one of them for a given light, not both. A light another mod makes burn without fuel is left to that
  mod (this mod does not switch it). Mods that decide by themselves when a light burns (schedules) win over the switch.
- These other mods have not been tested in game with this one.
