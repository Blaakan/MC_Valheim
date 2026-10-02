# Sort Chest

Adds a Sort button to chests, carts and ships. It rearranges the contents from top left to bottom right by name, type or biome, and can first combine partial stacks of the same item.

## Features

- **Sort button on every container**: chests of every kind, barrels, your personal chest, carts, ships (Karve,
  Longship, Drakkar), the Obliterator, dungeon and loot chests. Two buttons appear next to the game's **Take all** and
  **Place stacks** buttons: **Sort** and the **sort order** button. If there is no room next to them (for example a
  UI mod moved them), the two buttons go in a second row, on the side away from the chest slots.
- **Three sort orders**, chosen with the button next to Sort, which cycles **By name** → **By type** → **By biome**:
  - **By name**: alphabetical, using the item names of your game language (capital letters and accents ignored).
  - **By type**: weapons, ammo, shields, armour, utility and trinkets, tools and light, food and potions, materials,
    fish, trophies, then everything else (tankards and other items). Inside a group items are alphabetical, except
    that weapons go by kind (swords, axes, clubs, knives, spears, polearms, fists, bows, crossbows, elemental magic,
    blood magic), armour by slot (helmet, chest, legs, hands, cape), utility items come before trinkets, tools
    (hammer, hoe, cultivator) before pickaxes, fishing rods and scythes, then torches, and food before meads and
    potions. The groups follow the MC Crafting Search and Sort menu (its Food and Meads and potions are one group
    here, and so are its Utility and Trinkets), so an item lands in the matching group in both mods.
  - **By biome**: Meadows, Black Forest, Swamp, Ocean, Mountain, Plains, Mistlands, Ashlands, Deep North, then items
    whose biome is unknown (items added by other mods that the game cannot place). Inside a biome, by type, then by
    name. The biome of an item is where players normally get it first: raw materials come from a built-in list and
    from where plants, ores and creatures appear in your world; crafted items take the latest biome of their
    ingredients and of the station that makes them (a Bronze sword is Black Forest, Iron armour is Swamp).
- **From top left to bottom right**: Sort packs the items row by row, starting at the top-left slot, in the chosen
  order, with no gaps. Empty slots end up at the bottom right. Among identical items, higher quality and bigger stacks
  come first.
- **Your choice is remembered**: the sort order is saved in the config file and used by every chest until you change
  it. Changing the order does not sort the chest: click Sort when you are ready.
- **Combines partial stacks** first (can be turned off): 20 + 30 + 15 Wood become 50 + 15. Only items that are exactly
  the same are combined (same quality, world level, crafter, extra data from other mods, cheated mark), so nothing is
  ever lost or changed except the stack sizes.
- **Safe**: clicking Sort while you hold an item cancels that move first, as Take all does (the item goes back to its
  slot). A chest that is already sorted is not touched. Tombstones get no Sort button, so Take all still gives you
  back your own inventory layout and hotbar.
- **Controller**: while the chest's slots are selected, the **View/Select** button sorts and a **left stick click**
  changes the sort order. The buttons show these controller glyphs the way the game's own buttons do. They do nothing
  while your own inventory or the crafting panel is selected. The right stick click is not used.
- Turn it off at any time in the **MC Mods** panel (Esc menu), or with ConfigurationManager or the config file, which
  also work while a chest is open: the buttons disappear at once, no restart needed.

## Configuration

The config file `BepInEx/config/MC.UX.Container.Sort.cfg` is created the first time you launch the game with the mod. To
turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager installed that
button is hidden: press F1 and use its window). Every setting can be changed in-game with ConfigurationManager, or by
editing the file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | SortBy | `Type` | How the Sort button orders a container: `Name`, `Type` or `Biome`. The button next to Sort changes this setting. |
| General | MergeStacks | `true` | Before sorting, combine partial stacks of exactly the same item (quality, world level, crafter, extra data from other mods, cheated mark) into full stacks. |

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Only you need it; it works on vanilla servers and with friends who do not have it. Sorting saves the chest the same way as moving items by hand while you have it open, so everyone sees the new order.

- You can only sort a container while you have it open, just as you can only move its items by hand then. While it is
  open, other players get the game's usual "in use" message.
- If the game has not yet loaded another player's latest change to the open container (for example a ship's storage
  that a player aboard just used), Sort refuses with the message "This container changed elsewhere. Close it and open
  it again to sort." and changes nothing. Close the container, open it again, and sort.
- Players without the mod simply see a sorted chest. Combined stacks are ordinary stacks.

## Good to know

- **Labels are in English** for now ("Sort", "By name", "By type", "By biome"); the item names used by "By name"
  follow your game language.
- **Items from other mods** are sorted by name and type like any other item. By biome, they follow their recipe when
  it uses known materials, or the biome of the creature or plant that drops them; otherwise they go last.
- **Chests that grew** (very old saves that show extra rows): Sort packs the items from the top, so the extra rows
  empty out when everything fits in the normal rows; the chest shows its normal size again the next time the game
  loads it (for example after you leave the area and come back).
- **Sort does nothing** while you are teleporting (like the game's Take all) or while the split-stack dialog is open.
- The first "By biome" sort after loading a world can take a moment longer: the mod works out the biomes once, then
  reuses them.
- With Debug logging, the mod writes where its buttons sit, the container size, and the biome and type group of every
  item, which helps when reporting a problem.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing in your character or the world except
the new item order (and the combined stacks) of the chests you sort.

- **Other chest sort mods** (Quick Stack Store Sort Trash Restock, HexQuickStackStorage, InventoryActions,
  SonicChestFilters): they put their own buttons near Take all, and the buttons may overlap. Keep only one chest sort
  mod.
- **Auga** and other UI replacements: not supported. If the game's Place stacks button is missing, no Sort button is
  added (one warning in the log).
- **MultiUserChest**: only the player who owns the chest at that moment can sort it; for the others the Sort button
  does nothing.
- **EpicLoot** and **Crossbow Stays Loaded** (MC): safe. Items with extra data from other mods keep it; items whose
  data differ are never combined.
- **Loot Pickup Filter** (MC): works together. Its markers on chest slots follow the sorted items; its controller
  gestures work in your own inventory, Sort Chest's in the chest.
- **Crafting Search and Sort** (MC): works together. Both put an item in matching type groups; typing in its search
  field never triggers Sort.
- **One Click Repair All, Batch Station Feeding, Harpoon Hooks Tames, Creature Kill and Tame Counts** (MC): unrelated,
  work together.
- The mods above have not been tested in game with this one yet.
