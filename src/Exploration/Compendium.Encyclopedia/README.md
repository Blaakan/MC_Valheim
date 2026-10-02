# Encyclopedia

Adds an Encyclopedia tab to the game's Valheim Compendium: every item, building piece and creature in the game, with recipes, stations, drops, habitats and kill counts. Anything you have not discovered yet stays hidden as ???.

## Features

- **An Encyclopedia tab in the Valheim Compendium.** Open the game's Valheim Compendium as usual (inventory, raven
  button): two tabs sit at its top left, on the title line, in the style of the crafting panel's tabs: **Texts** (the
  game's own content, unchanged: message log, active effects, lore texts, Player Statistics) and **Encyclopedia**.
  Click Encyclopedia and the Encyclopedia takes the dialog's place, same frame, same size, with the same two tabs;
  click Texts to go back (the game's list is filled again, as when you open it). The game's side panel and its
  buttons are not changed.
- **The Encyclopedia** is drawn with the game's own frame, fonts and scroll bars (screen dimmed behind it): a row of
  category tabs, a search field and the list on the left, the details of the selected entry on the right, and
  "Discovered N / M" next to the title.
- **The Valheim Compendium remembers your tab** until you log out: if you left it on Encyclopedia, the raven button
  opens the Encyclopedia directly (Texts is one click or LT away); after logging in it starts on Texts, like the game.
- **Optional side button** (setting `SideButton`, off by default): an Encyclopedia button in the inventory's side panel,
  in the row of the game's own buttons, right after the Valheim Compendium button, with its own book icon and an
  "Encyclopedia" tooltip. To make room, the six buttons of that row are spaced a little closer while it is on (same
  sizes, all inside the panel); turning it off (or the mod) puts the game's buttons back exactly where they were. As
  with the game's own side buttons, it is locked while the Valheim Compendium, Skills, Trophies or Achievements is
  open.
- **Everything is listed**, read from the game's own data when the Encyclopedia first opens in a world, so content added by
  other mods appears too:
  - **items**: weapons, ammo, shields, armour, capes, utility items, trinkets, tools, torches, food, meads and
    potions, materials, fish, trophies and misc items, trader goods and dungeon loot included;
  - **building pieces** of the hammer, hoe and cultivator, crafting stations and their upgrades included;
  - **creatures**, bosses, young animals and farm animals included.
- **Tabs and groups**: Weapons, Armor, Tools and misc, Food and potions, Materials, Trophies, Building, Creatures.
  Inside a tab, entries are grouped (swords, axes, bows..., helmets, chest armour..., building pieces by tool and
  build-menu category, creatures by the first biome they live in).
- **Undiscovered = "???"**: an entry you have not discovered yet is listed as a row with a "?" mark and the name
  "???". Its real name and icon appear nowhere: not in the list, not in its details, not in search results, and not
  through its place in the list (discovered entries come first, A to Z; undiscovered ones follow in the game's own
  order, never alphabetically).
- **Discovered = icon and name.** Discovery follows what the game already records for your character, so an existing
  character sees its progress at once:
  - an **item** is discovered once you have held it, or once you know a recipe that makes it;
  - a **building piece** once you can build it, have seen it (crafting stations), or have placed one;
  - a **creature** once you have killed one, held its trophy, tamed one (with the MC mod Creature Kill and Tame
    Counts), or **met** one: the game showed you its name plate. For most creatures that means you aimed at one
    within 30 m (being near one is not enough, and aiming at one through a wall, a tree or a rock does not count); a
    boss's plate shows when it notices you within 100 m. Meeting creatures is recorded from the day you install this
    mod.
  - A discovered creature shows its trophy's icon only once that trophy is discovered too, else a paw print.
- **Details** of the selected entry, on the right:
  - items: description and stats (the crafting panel's text), every recipe with its station and station level and
    the cost the game charges to craft it, upgrade costs per quality (items that only an upgrade station takes are
    listed in the upgrade rows, never in the crafting cost; a recipe the game only offers for upgrades says so), which
    creatures drop it (amount and chance), where it is picked, mined, chopped or found, what makes it at a smelter,
    kiln, cooking station or fermenter, which tamed animal lays it, whether a trader sells it, what it is used in, and
    your own records (picked up, crafted, eaten);
  - building pieces: description, comfort, build cost, the station it needs nearby, and for crafting stations their
    upgrades, the recipes made there and their conversions (smelter, kiln...);
  - creatures: how many you have killed (the game's own all-time count, 0 included), how many you tamed (with
    Creature Kill and Tame Counts), where they live, what they drop, how to tame them and which saddle they wear;
    after your first kill also their health and damage resistances and weaknesses.
- **Undiscovered things inside the details are hidden too**: an ingredient, a station, a tool, a saddle, a creature,
  a biome or a boss you have not discovered shows as "???", while amounts, chances and station levels stay visible
  ("??? ×2", "At ??? (level 2)"). Long lists ("Used in", "Recipes at this station", a station's conversions) fold the
  undiscovered ones into one line "??? ×N not discovered yet".
- **Search** (mouse and keyboard): type part of a name; the list shows the matching discovered entries of every tab,
  grouped by tab. While you type, your keys only type (no walking, no hotkeys); Esc or Enter leaves the field.
  Clicking a tab leaves the search.
- **Clickable references**: click a discovered ingredient, creature or station in the details to open it.
- **Controller**: in the Valheim Compendium and in the Encyclopedia, **LT** shows Texts and **RT** shows the
  Encyclopedia (like LT / RT for Craft / Upgrade in the crafting panel). In the Encyclopedia, D-pad or left stick
  moves through the list (the details follow), LB/RB change the category tab, the right stick scrolls the details, B
  closes it. The game's controller hints are hidden while it is open, as for the game's own dialogs. With the
  optional side button on: with the side panel selected, **View/Select** opens the Encyclopedia (View/Select anywhere
  else keeps its usual job: the map in the world, Sort Chest on a chest), and the D-pad reaches the button between
  the Valheim Compendium and Skills buttons. Search and clickable references need a mouse and keyboard.
- **Closing**: Esc or B closes the Encyclopedia only (press again to close the inventory), like the Valheim Compendium;
  its Close button and a left click outside it too; Tab, E or Y close both, like the game's own dialogs. While the
  Encyclopedia is open, the inventory behind it cannot be clicked or reached with a controller: a left click (or a
  drag) outside it only closes it, a right click does nothing, so nothing is moved, eaten or equipped by accident.
- **The Encyclopedia remembers** its last category tab, the entry selected in each tab and the search text until you
  log out.
- Turn it off at any time, no restart needed: in the **MC Mods** panel (Esc menu, so with the inventory closed), or
  with ConfigurationManager or the config file, which also work while the Encyclopedia is open. The tabs, the
  Encyclopedia and the optional button disappear at once; the Valheim Compendium and the side panel are exactly the
  game's again.

## Configuration

The config file `BepInEx/config/MC.Exploration.Compendium.Encyclopedia.cfg` is created the first time you launch the
game with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration
manager installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). The display settings apply at
once, also to an open Encyclopedia, and so does SideButton (the button appears or goes at once, the side panel back
exactly as the game makes it).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| Display | ShowUndiscovered | `true` | Show entries you have not discovered yet as "???". Turn off to list only what you have discovered. |
| Display | RevealAll | `false` | Spoiler mode: show every entry and every detail, discovered or not. Your discoveries are still recorded. |
| Display | SideButton | `false` | Also add an Encyclopedia button to the inventory's side panel, right after the Valheim Compendium button (the side panel's buttons move a little closer together to make room). Without it, open the Encyclopedia from the Encyclopedia tab at the top of the Valheim Compendium. |

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers, dedicated servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Only you need it. The mod sends nothing to other players and changes nothing in the world, on items or on creatures.
What you have discovered belongs to your character, like the game's own recipes: every player has their own
Encyclopedia. Kill counts are the game's own: every player who hit a creature gets the kill, even when a friend without
the mod or the server controls that creature. Creatures whose name plate you see are recorded as met whoever controls them, other
players' tames included.

## Good to know

- **Labels are in English** for now; item, piece, creature and biome names follow your game language.
- **The first open in a world takes a moment**: the mod reads the game data over a few frames and shows
  "Preparing entries..." meanwhile. It is done once per world session (again only if another mod adds content later).
- **Some items show "Not found in the world data"** once discovered: loot that only exists inside dungeons or
  locations. The game does not load those places until you visit them, so the mod cannot see where the item comes
  from. They are still listed, as "???" until you get one. Trader goods (Haldor, Hildir, the Bog Witch) say "Sold by a
  trader": the game's traders are found in its data.
- **A few items are listed only once you have them**: items only creatures carry (their attacks), the development
  items of the game, and seasonal items and pieces (Midsummer, Yule...) that can only be learned in season.
- **Drop amounts and chances** are the game's base values for a creature without stars, at normal world settings
  (amounts as the game rolls them, which can differ from wiki numbers by one). "More with stars" means starred
  creatures drop more. Drop, spawn or recipe changes that other mods make per creature while you play are not shown.
- **Habitats** come from the world's spawn lists, nests and spawners, and are inherited by young animals. Bosses and
  dungeon creatures get the biome of their trophy. Creatures with none of these show "Habitat unknown". A biome you
  have not visited shows as "???" (the mod records visited biomes itself, so switching the game language does not
  forget them; biomes visited before installing the mod are recognized in the language you found them in).
- **Resets**: the console command `resetcharacter` also clears the mod's records of met creatures and visited biomes.
  Creatures you killed or tamed and pieces you placed stay discovered after `resetcharacter` or `resetknownitems`,
  because the game keeps those statistics.
- **Removing the mod** leaves two small records (met creatures, visited biomes) in your character file; the game
  ignores them, and they are used again if you reinstall the mod.
- The world modifier "all recipes unlocked" / "all pieces unlocked" (and the debug no-cost mode) makes every craftable
  item and every building piece count as discovered, as the game's crafting and build menus show them. Seasonal items
  out of their season stay hidden, as in the crafting menu.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it only reads game data, plus two small records of its
own in your character file.

- **Content mods** (new items, pieces, creatures, for example with Jotunn): their content is listed and can be
  discovered like the game's own. Their creatures without spawn data show "Habitat unknown".
- **Almanac**: works together. Almanac keeps the Trophies button; the Encyclopedia lives in the Valheim Compendium.
- **PraetorisClient** (Server Guide), **Jewelcrafting**, **EquipmentAndQuickSlots**: they add to or copy the game's
  Valheim Compendium. Their additions stay on its Texts tab; the Encyclopedia carries none of them. The two tabs are
  on the Valheim Compendium only while it is shown, so a mod that copies it never copies them.
- **UI replacements** (AugaLite, Auga, Veneer): not supported. When the game's Valheim Compendium dialog is missing or
  changed, it gets no Encyclopedia tab (one warning in the log); with the optional side button, when the side panel is
  missing no button is added (one warning). Nothing breaks.
- **Mods that move or add buttons in the inventory's side panel** (only with the optional side button on; off, the
  mod never touches the side panel): buttons they move keep their new places and the row is spaced again around them;
  a button they add joins the row. If a mod keeps moving those buttons, the Encyclopedia leaves them alone for the
  rest of the session and puts its button after the last one (one warning in the log).
- **ExtraSlots** and other mods that add panels next to the inventory may overlap the optional button; the log then has
  a warning with the positions.
- **Nameplate mods** that replace the game's creature name plates: meeting creatures may not be recorded; kills,
  trophies and tames still discover them.
- **Drop, spawn and recipe editors** (Drop That, Spawn That, CLLC, server-synced YAML mods): the Encyclopedia shows the
  game data as loaded; recipe changes show at once, per-creature changes made at runtime do not.
- **Creature Kill and Tame Counts** (MC): shows the same kill numbers; its tame counts appear in the creature details.
  Its Player Statistics section stays in the game's Valheim Compendium. Neither mod needs the other.
- **Crafting Search and Sort** (MC): its search field gives up the keyboard when the Encyclopedia opens; typing in the
  Encyclopedia's search never triggers its keys.
- **Loot Pickup Filter** and **Sort Chest** (MC): their buttons and controller shortcuts do nothing while the
  Encyclopedia is open; View/Select on the chest grid still sorts the chest and never opens the Encyclopedia.
- **One Click Repair All, Batch Station Feeding, Harpoon Hooks Tames, Crossbow Stays Loaded** (MC): unrelated, work
  together.
- The mods above have not been tested in game with this one yet.
