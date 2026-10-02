# Loot Pickup Filter

Choose what auto pickup collects: everything, everything except your ignored items, or only your selected items.
Set it up from your inventory: one button picks the mode, a middle-click on an item adds it to the list. The auto
pickup key (V) still turns auto pickup on and off, and picking up or harvesting by hand is never filtered.

## Features

- **Three modes, one button in your inventory.** A button `Auto pickup: Everything` sits just above the top-left
  corner of your inventory panel. Click it to go to the next mode: **Everything** (every item is picked up, exactly
  like the normal game), **Skip ignored** (every item except the items in your *Ignored* list), **Only selected**
  (only the items in your *Selected* list). A message in the top-left corner confirms the new mode (for Skip ignored
  and Only selected, with the number of items in its list).
- **Mark items with a middle-click.** Middle-click an item in your inventory or in an open chest (or cart, ship,
  tombstone: any container) to add it to the list of the current mode; middle-click it again to remove it. In Skip
  ignored mode that is the Ignored list, in Only selected mode the Selected list. A mark applies to the whole item
  type (all Stone), not to that one stack. The key can be changed (`MarkKey`).
- **Two separate lists.** Ignored and Selected are kept apart: switching modes never changes them. Only the list of
  the current mode is used.
- **Red and green marks.** In Skip ignored mode, ignored items show a small red "no entry" mark; in Only selected
  mode, selected items show a small green check. The marks show in your inventory and in chests, in the slot corner
  that hides the least of the game's own slot details (stack size, quality number, no-portal icon). No marks in
  Everything mode.
- **See your lists.** Hover the button: its tooltip explains the modes and lists both lists (the one in use first),
  up to 40 names each.
- **The auto pickup key stays the master switch.** Press V (the game's own key) to turn auto pickup off: nothing is
  picked up automatically, whatever the mode, and the button reads `Auto pickup: Off (Skip ignored)`. Press V again:
  your mode applies again, and in Skip ignored or Only selected a message names it right after the game's "Auto
  pickup: On". The mod never turns auto pickup on or off by itself.
- **Picking up by hand is never filtered.** Pressing E on an item and fishing pick up as usual in every mode.
  **Harvesting by hand counts as picking up by hand**: berries, mushrooms, flint, stones and branches you pick,
  crops, the fermenter, cooking station, stone oven, beehive, sap collector, item stand, armor stand, archery target, a station's
  "empty" switch, taking a saddle off (Shift + E) and a scythe swing all drop their items on the ground, and the
  game only collects them with auto pickup. The mod lets those drops in whatever the mode (you can
  turn this off: `ExemptHarvest`). With auto pickup off (V), they stay on the ground, as in the normal game.
- **Skipped items stay on the ground** as normal items: you do not pull them towards you, your game does not claim
  them, and anyone can pick them up. Look at one (with auto pickup on) and a grey line under its name says `Auto
  pickup skips this (ignored)` or `(not selected)`.
- **Saved with your character**: the mode and both lists, per character, on every server. A character that has
  never used the filter starts in Everything mode with the default lists of the config file (empty unless you set
  them).
- **Chat and console commands** for items you do not carry (see below).
- **Controller support** (see below).
- Turn it off in the **MC Mods** panel at any time: auto pickup is back to normal at once, the button and marks
  disappear, and your lists stay saved in the character for when you turn it back on. No restart needed.

## Adding or removing an item you do not carry

In this version the inventory screen can only mark items you can see there: in your inventory or in an open chest.
A full list editor is planned for version 0.2.0. Until then, for any other item:

- **Pick one up by hand and middle-click it.** Picking up with E is never filtered, so walk to the item, press E,
  then middle-click it in your inventory. (Or put one in a chest and middle-click it there.)
- **Or type a command**, in the chat (close your inventory, press Enter, start with `/`) or in the F5 console (if F5
  does nothing, turn the console on in the game's settings):

| Command | What it does |
|---|---|
| `lootfilter` | Shows the mode, whether auto pickup is on, both lists and the list of commands. |
| `lootfilter <item>` | Adds the item to the list of the current mode, or removes it (like a middle-click). In Everything mode it changes nothing and says which command to use instead. |
| `lootfilter_ignore <item>` | Adds the item to the Ignored list, or removes it, whatever the mode. |
| `lootfilter_select <item>` | Adds the item to the Selected list, or removes it, whatever the mode. |
| `lootfilter mode everything`, `skip` or `only` | Sets the mode. |
| `lootfilter clear ignored` or `clear selected` | Empties one list. |
| `lootfilter defaults` | Replaces both lists with the default lists of the config file. |

`<item>` is the item's spawn name, as used by the game's `spawn` command (`Stone`, `Wood`, `Resin`, `Coins`,
`TrophyBoar`...), in any capitals; press Tab to complete it (in the chat or the F5 console). To remove the item of a
mod you uninstalled, type its name as the list shows it. These commands are not cheats: they work without
`devcommands` and do not affect achievements.

## Controller

With a controller, while **your inventory grid** is selected (not the chest grid):

- click the right stick: add the selected item to the list of the current mode, or remove it;
- hold LT and click the right stick: change the mode;
- hold RT and click the right stick: show or hide a panel with both lists next to the button (it also closes with
  the inventory).

Chest slots can only be marked with the mouse: on a controller, move the item into your inventory first. The button
itself cannot be selected with the D-pad (it never takes the selection away from your items). Turn the controller
gestures off with `GamepadControls` if another mod uses the right stick in the inventory.

## Configuration

The config file `BepInEx/config/MC.UX.AutoPickup.Filter.cfg` is created the first time you launch the game with the mod.
To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager installed
that button is hidden: press F1 and use its window). Every setting can be changed in-game with ConfigurationManager, or
by editing the file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| Filter | ExemptHarvest | `true` | Items that drop when you harvest or collect by hand (the use key on berry bushes, mushrooms, crops, pickable stones, a fermenter, a cooking station or stone oven, a beehive, a sap collector, an item or armor stand, an archery target, the empty switch of a production station, or a scythe swing) are picked up whatever the filter mode, like picking them up by hand. Turn this off to filter them like any other item. Mining, chopping and fighting drops are filtered, except a drop that lands within 4 m of something you harvested in the last 3 seconds. |
| Controls | MarkKey | `Mouse2` | Key or mouse button that adds the item under the mouse pointer to the list of the current filter mode, or removes it, in your inventory or an open chest. Default: middle mouse button (Mouse2). Avoid Mouse0 and Mouse1: the inventory already uses the left and right buttons. Set to None to turn marking off. Mouse5, Mouse6 and some keys (F13-F15, some symbol keys such as # or @) cannot be read by the game; if you pick one, marking is turned off and the log says so. |
| Controls | GamepadControls | `true` | Controller, with your inventory grid selected: click the right stick to add or remove the selected item; hold LT and click the right stick to change the filter mode; hold RT and click the right stick to show both lists. Chest slots can only be marked with the mouse. Turn this off if another mod uses the right stick in the inventory. |
| Display | ShowMarkers | `true` | Show a small mark on items of the active list in your inventory and in chests (red: ignored, green: selected). |
| Display | ShowInHoverText | `true` | When you look at an item on the ground that auto pickup will skip, say so under its name. |
| Display | ButtonOffsetX | `0` | Moves the Auto pickup button sideways (pixels, positive = right) if it overlaps something added by another mod. |
| Display | ButtonOffsetY | `0` | Moves the Auto pickup button up or down (pixels, positive = up). |
| Defaults | IgnoredItems | (empty) | Ignored items for a character that has never used the filter: comma-separated item names as used by the spawn command (for example Stone,Wood,Resin). Once you change the filter in game, that character keeps its own lists. |
| Defaults | SelectedItems | (empty) | Selected items for a character that has never used the filter, same format (for example Coins,Amber,Ruby). |

Every setting applies at once, while you play. A character "has used the filter" as soon as you change its mode or
one of its lists: from then on its own lists are saved with it and the `Defaults` no longer apply to it
(`lootfilter defaults` copies them again). Names in the `Defaults` lists are matched in any capitals; a name the
game does not know is kept as typed (an item of a mod you have not installed yet).

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Only you need it; it works on vanilla servers and with friends who do not have it. Items your filter skips stay on the ground as normal items that anyone can pick up.

The filter only changes what **your** auto pickup collects. Your game never claims the items it skips, so a friend
standing on the same pile picks them up normally: with the vanilla game if they do not have the mod, or with their
own filter if they do. The mod sends nothing over the network and stores nothing on items or in the world: an item
you marked and then give to a friend is a normal item for them. Your mode and lists are stored in your character, so
they follow you to every server.

When another player's game runs a bush or a station you harvest, its drops reach you a moment later; the mod waits up
to 3 seconds for them, so they are still collected as a hand harvest.

## Good to know

- **Marks are per item type.** Middle-clicking one stack of Stone ignores every Stone. Items are told apart by their
  spawn name, so two modded items that share a display name are separate entries.
- **In Everything mode the mark gesture changes nothing**: a message asks you to choose Skip ignored or Only selected
  first, since the marks and the lists only matter in those modes.
- **Only selected with an empty Selected list picks up nothing** automatically. The mode message warns you when you
  switch to it.
- **Items you drop yourself** are never picked up again by your own auto pickup, whatever the mode: that is the normal
  game. Press E to take them back.
- **A thrown weapon that lands as an item** (a spear, for example) follows the filter like any item: in Only
  selected mode, pick it up with E or select it.
- **Harvest drops, in detail**: drops that appear within 4 m of what you just harvested or collected by hand, up to
  3 seconds after it, are picked up whatever the mode, even if you walk over them much later. As a side effect, the
  drops of a creature you kill (or a rock you mine) right next to a bush you just picked also get through. Opening
  doors, chests, beds, portals, crafting stations and the like does not count, so drops next to them stay filtered;
  neither does feeding a smelter or kiln (its output is filtered when it comes out by itself), nor using an object
  added by another mod.
- **Messages**: the game shows at most one top-left message per second. When you mark many items quickly, some mark
  messages are skipped; the marks on the slots always show the result. Mode messages are never skipped.
- **Marking is ignored** while you drag an item, while the split dialog is open, when a window (skills, compendium,
  trophies...) or another mod's menu covers the slot, and while you type in the chat, the console or a text field
  (for example the crafting search of Crafting Search and Sort).
- **Auto pickup on or off is the game's own setting**: the game turns it back on each time you start it. If you turn
  it off and log out, it is still off for the next character you load in the same game session; the button shows it.
- **Saving**: your filter is saved with your character whenever the game saves it (periodic world save, sleeping, the
  menu's save, logging out). If the game crashes, changes made since the last save are lost.
- The mod's own texts (button, tooltip, messages) are in English; item and key names follow your game language.
- If the button overlaps something added by another mod, move it with `ButtonOffsetX` / `ButtonOffsetY`.
- If the inventory layout is not recognised (a UI replacement mod), the button is not shown and the log says so; the
  filter, middle-click marking and the commands still work.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores four short entries in your character (your
mode and lists), which the game ignores without the mod, and nothing on items or in the world.

- **Mods that change or replace the game's auto pickup code** (for example AutoPickupIgnorer): this mod plugs into
  the game's own check that skips items placed on item stands and tables. A mod that copies the auto pickup code keeps
  working with it as long as its copy still makes that check (AutoPickupIgnorer does, according to its source). If
  another mod rewrites auto pickup, the log shows one warning naming it; if the filter then stops working, that mod
  replaces the game's item checks. Mods that only change the pickup range should work: the filter applies at any
  range.
- **Other loot filter mods** (Loki's Autoloot Trash Filtering, LootFilter, AutoPickupIgnorer): two filters at once
  are confusing, keep one. With both installed, an item is normally picked up only when both allow it.
- **Quick Stack Store Sort Trash Restock**: uses Alt + click and buttons on the right side of the inventory panel;
  this mod uses middle-click and sits above the top-left corner. They should work together.
- **InventoryActions**: it also uses the right stick click in the inventory. Set `GamepadControls = false` to leave
  the right stick to it.
- **Extended inventory mods** (for example AzuExtendedPlayerInventory): the extra slots they add outside the normal
  grid get no marks.
- **UI replacement mods** (for example Auga): the button may not be shown (see Good to know).
- **Crafting Search and Sort** (MC): works together. Its search field is in the crafting panel; typing in it never
  marks items or changes the mode.
- **Batch Station Feeding** (MC): works together. Both react to the use key on stations; E and Shift + E on stations
  work as with that mod alone, and cooked food you take out of a cooking station is still collected as a hand
  harvest.
- **One Click Repair All, Crossbow Stays Loaded, Creature Kill and Tame Counts, Harpoon Hooks Tames** (MC):
  unrelated, work together.
- The mods above have not been tested in game with this one yet.
