# Crossbow Stays Loaded

Crossbows keep their bolt loaded when you put them away, switch weapons, swim or sit down. Re-equip and fire right
away; firing still uses the loaded bolt as usual.

## Features

- A crossbow you have reloaded **stays loaded** until you fire it: switching weapons, holstering, swimming, using a
  crafting station, sleeping, logging out, storing it in a chest or on an item stand no longer empties it.
- Re-equipping a loaded crossbow costs **no reload time, stamina or eitr**. It only takes the normal equip time.
- **Firing works exactly like vanilla**: the shot uses the load, and the next reload takes the usual time and cost.
- The tooltip of a crossbow that still holds a bolt shows **Loaded**.
- Works for every weapon that must be reloaded before firing (all crossbows), including ones added by other mods.

## Configuration

The config file `BepInEx/config/MC.Combat.Crossbow.StaysLoaded.cfg` is created the first time you launch the game
with the mod. Every setting can also be changed in-game: open the menu (Esc) and click **MC Mods**, or use
ConfigurationManager.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| UI | ShowLoadedInTooltip | `true` | Show "Loaded" in the tooltip of a crossbow that still holds a bolt. |

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer. Other players see your crossbow's loaded state as usual.

A loaded crossbow you give to a friend stays loaded if they have the mod; without it they reload as usual. Once
anyone fires it, with or without the mod, it counts as unloaded.

## Good to know

- Each crossbow keeps its own load: you can carry several loaded crossbows.
- Dying keeps the load: a loaded crossbow recovered from your tombstone is still loaded.
- Upgrading a crossbow at a crafting station gives you a new, unloaded crossbow.
- Repairing keeps it loaded.
- The mod notices a shot by the drop in durability. It cannot tell if a player without the mod fires your crossbow
  and then repairs it before giving it back, or if durability loss is turned off in your world.

## Installation

- **Mod manager (r2modman / Thunderstore Mod Manager):** install it from the mod list.
- **Manual:** install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), then copy the `MC.Combat.Crossbow.StaysLoaded` folder into `Valheim/BepInEx/plugins/`.

## Compatibility

Built for Valheim 1.0.16. Safe to remove at any time: crossbows simply go back to vanilla reloading.
