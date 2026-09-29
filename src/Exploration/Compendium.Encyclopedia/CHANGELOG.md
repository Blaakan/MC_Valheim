# Changelog

## 0.1.0

- Initial version: an **Encyclopedia** tab in the game's Valheim Compendium. The Valheim Compendium (raven button) gets
  two tabs at its top left, Texts (the game's own content, unchanged) and Encyclopedia; the Encyclopedia takes the
  dialog's place, drawn like the game's own dialogs: category tabs (Weapons, Armor, Tools and misc, Food and potions,
  Materials, Trophies, Building, Creatures), sub-group headers, a search field, the list on the left and the details on
  the right, "Discovered N / M" next to the title. The Valheim Compendium reopens on the tab you used last until you
  log out. It lists every item, building piece and creature; undiscovered entries show as "???" with a "?" mark (in
  the list, the details and search).
- Optional side button (setting SideButton, off by default): an Encyclopedia button (book icon) in the inventory's side
  panel, right after the Valheim Compendium button (the side panel's buttons are spaced a little closer while it is on).
- Discovery follows what the game already records for your character (items held, recipes and pieces known, stations
  seen, pieces placed, creatures killed, trophies held), plus creatures met (the game showed their name plate: aimed
  at within 30 m, bosses when they notice you) and biomes visited, recorded by the mod from install. Taming counts
  from Creature Kill and Tame Counts are used when present. A creature whose trophy you have not found shows a paw
  print.
- Details: stats, recipes with station levels and the cost the game charges to craft them, upgrade costs, drops with
  amounts and chances, where items are gathered, made, laid (eggs) or sold, what they are used in, habitats, taming
  food and saddles, kill counts, and health and damage modifiers after the first kill. Anything undiscovered inside
  the details shows as "???", numbers kept. Discovered references are links to their entry.
- Controls: mouse and keyboard (search, links), controller (LT / RT switch between Texts and Encyclopedia, D-pad /
  left stick, LB/RB, right stick, B; View/Select opens it from the side panel when the optional button is on). Esc or
  B closes only the Encyclopedia, as do its Close button and a click outside it; Tab, E or Y close the inventory too.
  The inventory behind it cannot be clicked while it is open.
- Settings: ShowUndiscovered, RevealAll (spoiler mode), SideButton.
