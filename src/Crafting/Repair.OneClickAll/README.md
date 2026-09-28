# One Click Repair All

The repair button at a crafting station repairs every item that station can repair in one click, instead of one item
per click. Also works with the devcommands nocost mode.

## Features

- **One click repairs everything the station can repair**: every damaged item in your inventory, equipped or
  carried. In vanilla you click once per item.
- **Same rules as vanilla**: an item is repaired only if the current station can repair it (its recipe uses this
  station, and the station level is high enough). Items that need another station stay damaged: repair your bronze
  gear at the forge and your leather gear at the workbench.
- **One sound and one message** for the whole click, e.g. "Repaired Bronze sword, Bronze buckler, Leather tunic +2",
  in your game language.
- **Crafting skill** rises exactly as if you had clicked once per item.
- **devcommands `nocost`** is supported: the repair button in your inventory (shown with no station nearby) also
  repairs everything in one click.
- Turn it off in the **MC Mods** panel at any time to get the vanilla one-item-per-click back, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Crafting.Repair.OneClickAll.cfg` is created the first time you launch the game
with the mod. Every setting can also be changed in-game: open the menu (Esc) and click **MC Mods**, or use
ConfigurationManager.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Repairing only changes the items in your own inventory, exactly like clicking the repair button several times.
Players near you hear one repair sound instead of several.

## Good to know

- Only your own inventory is repaired, like in vanilla. Items in an open chest are not.
- With devcommands `nocost` on, vanilla lets you repair any item at any station (or without a station), so one click
  repairs everything, even at the "wrong" station. With `nocost` off, the normal station rules apply.
- Vanilla rule, unchanged: gear made in a lower world level (world modifiers) can be repaired at any station that
  is high enough level.
- The vanilla console cheat `repairall` is a different thing: it repairs everything instantly, with no station and no
  skill gain. This mod keeps the normal repair rules and skill gain.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing on your items or characters.

- **Mods that make repairs cost materials or coins** (for example RepairRequiresCoins, RepairCost,
  RepairRequiresMaterials): each item is repaired and paid for as if you had clicked once per item. The click stops at
  the first item you cannot pay for; the reason is shown in the top-left corner.
- **Crossbow Stays Loaded** (MC): a loaded crossbow stays loaded when it is repaired this way.
- **Auto-repair mods** (repair everything when you open a station, e.g. SmartRepair or ValheimPlus auto repair):
  everything is already repaired, so this mod has nothing left to do.
- **Other "repair all" button mods** (e.g. FastRepairButton, RhythmicRepairs): that mod handles the click; this one
  does nothing extra. Keep only one of them.
- Works with UI mods that keep the vanilla repair button (tested by design with the vanilla UI; Auga and SeneaL UI
  call the same button code).
