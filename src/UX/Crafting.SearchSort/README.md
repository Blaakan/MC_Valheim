# Crafting Search and Sort

Adds a search field and a sort button above the recipe list of every crafting station. Search matches item and
ingredient names as you type; sort puts the category you pick (weapons, swords, armor, food...) at the top.

## Features

- **At every crafting station**: workbench, forge, black forge, galdr table, artisan table, cauldron, mead ketill,
  food preparation table, stonecutter, the Forge of Potential and crafting stations added by other mods. Also in your
  plain inventory (Tab), where you craft by hand. One row sits directly above the recipe list: the search field on the
  left, the Sort button on the right. The list gets one row shorter; nothing else in the panel moves or is covered.
- **Search as you type**: the list keeps only the recipes that match what you type, and updates while you type. A
  recipe matches when the text is found in:
  - the item name, as shown in your game language;
  - the item's internal name or recipe name (English, for example `SwordSilver` or `MaceSilver`);
  - the name of one of the ingredients the recipe shows.

  Example: at the forge, typing `silver` keeps the Silver Sword, Frostner, and every recipe that shows Silver among its
  ingredients. Capital letters and spaces are ignored (`SiLver SWord` finds the Silver Sword). Delete the text, or
  right-click the field, to get the full list back.
- **Sort button**: click it to open a menu in the style of the recipe list:
  - **Default**: the game's normal order.
  - **Name (A-Z)**: the whole list in alphabetical order.
  - **A category**: every recipe of that category moves to the top, in the order it had; the rest of the list follows,
    untouched, in its normal order. Categories: Weapons, each weapon type (Swords, Axes, Clubs, Knives, Spears,
    Polearms, Bows, Crossbows, Elemental Magic, Blood Magic, Fists), Shields, Armor, Helmets, Chest armor, Leg armor,
    Capes, Ammo, Tools and light, Food, Meads and potions, Trinkets, Utility, Materials, Fish, Trophies, Other.

  The menu only lists the categories that exist in the list you are looking at (plus the active sort, shown with 0
  when that list has none of it), each with its number of recipes and the icon of its first recipe. The counts ignore
  the search. The button shows the active sort (`Sort: Weapons`). Right-click the button to go back to Default.
- **Search follows the sort**: with Weapons selected and `silver` typed, the silver weapons come first, then every
  other recipe that matches `silver`, and nothing else.
- **Both tabs**: the Craft tab and the Upgrade tab are searched and sorted the same way.
- **Your sort is remembered** for each type of station (all your forges share one), and saved with your character.
- **Press F** (the same key as the build menu search) to jump into the search field while your inventory or a station
  is open. Enter or Esc leaves the field and keeps the text; a second Esc closes the inventory as usual.
- **Typing is safe**: while the field has the cursor, the keys you type only go into the field. You do not walk, the
  inventory does not close on E, Tab or Esc, and console key binds do not fire.
- Turn it off at any time in the **MC Mods** panel (Esc menu), or with ConfigurationManager or the config file, which
  also work while a station is open and you are typing: the crafting panel is back to vanilla at once, no restart
  needed.

## Configuration

The config file `BepInEx/config/MC.UX.Crafting.SearchSort.cfg` is created the first time you launch the game with the
mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | FocusSearchKey | `F` | Key that puts the cursor in the crafting search field while your inventory or a crafting station is open (the same key as the build menu search). Leave it empty to only click the field. |
| General | RememberSort | `true` | Remember the sort you picked for each type of crafting station (saved with your character). Off: every station opens in the game's normal order. |
| General | KeepSearchText | `false` | Keep the search text when you close and reopen the same crafting station, like the build menu does. Off: the search is cleared every time the inventory closes. |

With `RememberSort` off, the sort you pick lasts until you close the inventory. With `KeepSearchText` on, the text is
kept for the same type of station; opening another type of station (or your plain inventory after a station) always
starts with an empty search.

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Only you need it; it works on vanilla servers and with friends who do not have it. It only changes how your own crafting list is shown. The sort you pick for each station is saved with your character.

The mod sends nothing over the network and changes nothing in the world. Items you craft after a search or a sort are
ordinary items: you can give them to players who don't have the mod. Other players at the same station see their own
normal list.

## Good to know

- **Ingredients count only when the recipe shows them.** The search matches the ingredients you see in the
  requirement list when you click the recipe: the crafting ingredients in the Craft tab, the ingredients of the next
  level in the Upgrade tab, and idols only at the Forge of Potential. An ingredient of an "any one of these" recipe
  that you have not discovered yet is not matched, so the search never reveals it.
- **Internal names are English.** Typing `sword` also finds the swords in a game set to another language, because
  their internal names are searched too. You never see these names in the game.
- **Name (A-Z) mixes** the recipes you can craft now with the greyed-out ones (the game's normal order puts the
  craftable ones first).
- **Labels**: the weapon types use the game's own skill names, so they follow your game language. The other labels
  (Default, Name (A-Z), Weapons, Armor, Tools and light, "Sort:"...) are in English for now.
- **Tools and light** holds the hammer, hoe, cultivator, pickaxes, the scythe and torches, so they do not show up
  under a weapon type. Tankards go under Other. Everything else follows its item type in the game: mead bases, for
  example, are under Materials if the game types them as a material. Which category a few items land in (tankards,
  mead bases, fishing bait) is not confirmed in game yet.
- **Keyboard and mouse only.** With a controller, the list works as in vanilla and your remembered sort still
  applies, but the controller's D-pad never moves onto the search field or the Sort button, so you can never get stuck
  in them. If you use a gamepad cursor to click the field, B leaves it.
- **While a craft is running**, the list updates when the craft finishes; the item being crafted never changes.
- **No match** gives an empty list with nothing selected. Clear the text to get everything back.
- The game's console commands `filtercraft` and `sortcraft` still work and combine with the mod: `filtercraft` and
  the search must both match, and Default keeps the order set by `sortcraft`.
- Each search update writes the game's normal `Setting selected recipe` line to the log, as switching tabs does.
- After you change the game language, the hint text inside the empty field stays in the old language until you load
  a character again. The search and the weapon-type labels use the new language at once.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: the only thing it stores is your remembered sorts, as
one entry in your character that the game ignores without the mod.

- **CraftSearch** and **AAA Crafting**: not compatible. They put their own search field above the recipe list, in the
  same place. Keep only one crafting search mod.
- **CraftingFilter**: works together. Its category filter and this mod's search both apply. Its category list, shown
  when you hover the Craft tab, may cover the search row while it is open.
- **Other mods that sort or add recipes to the list**: Default keeps the order the list has after the game and those
  mods; a category or Name sort is applied on top. Recipes that another mod adds to the list very late (after this
  mod) are not searched or sorted.
- **Mods that change recipe ingredients** while the game runs (recipe config mods, server-synced configs): the search
  follows the new ingredients, at the latest the next time you load a character.
- **UI replacement mods** (for example Auga): if the mod does not recognise the crafting list layout, it shows no
  search row and writes one warning in the log; your remembered sort still applies. Not tested in game yet.
- **One Click Repair All** (MC): works together. Repairing refreshes the list and keeps your search and sort.
- **Crossbow Stays Loaded, Creature Kill and Tame Counts, Batch Station Feeding, Harpoon Hooks Tames** (MC):
  unrelated, work together.
- The mods above have not been tested in game with this one yet.
