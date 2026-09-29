# Batch Station Feeding

Hold Left Shift and press E on a smelter, kiln, furnace, fire, cooking station, windmill, spinning wheel and more to add 5 items at once instead of 1. The amount and the key are configurable. Works on vanilla servers.

## Features

- **Shift + E adds up to 5 items in one press.** Hold the modifier key (default: Left Shift, the game's own
  "Alternative placement" key) and press Use (E) on a station: it adds up to 5 of what a plain E press would add
  (ore, fuel, raw food, missiles...). Plain E still adds one, as in vanilla.
- **Never more than you carry, never more than fits.** With fewer than 5 of what the station takes, the press adds
  what you have. When the station has room for fewer than 5, the press adds exactly what fills it. On a full station
  you get the game's own "full" message and nothing leaves your inventory.
- **Every station that takes fuel or items with E** (Valheim 1.0.16):
  - Smelter, Blast Furnace, Eitr Refinery: ore (or sap) and fuel;
  - Charcoal Kiln, Windmill, Spinning Wheel: what they process;
  - Frigid Kiln: Ice; Hot Tub: Wood;
  - Frost Foundry: Liquid Frost. Its cast slot holds a single cast, so Shift+E places one cast there, as in
    vanilla (no hint on that slot);
  - Campfire, Iron Fire Pit, Hearth, Bonfire, braziers, and the torches and sconces that you refill: fuel;
  - Cooking Station, Iron Cooking Station, Stone Oven: raw food (and the oven's wood);
  - Shield Generator: bones; Ballista: missiles (one missile kind per press).
- **Modded stations** built on the same game parts as the ones above work too.
- **Not changed**: the Fermenter (it holds one batch at a time), boss altars and offering bowls, item and armor
  stands, beehives, sap collectors, chests, Empty switches (windmill), taking cooked food out, fires that never need
  fuel. Shift+E on a tame, a saddle, an item stand or a dropped item still does what it does in vanilla.
- **Hint when you look at a station**: a line `[L-Shift + E] Add Coal x5` right below the game's `[E] Add Coal` line,
  in the same style. It always shows the keys that really work: your rebound keys, or the controller buttons.
- **Remappable key.** Rebind "Alternative placement" and "Use" in the game's Settings (controls), or pick a key just
  for this mod with `ModifierKey`. The hint follows the change within half a second.
- **One message per press**, e.g. "Added 5 Copper Ore (5/10)" or "Adding 3 Wood to the fire (10/10)", in your game
  language, instead of one message per item. On a station your game runs, you also hear one add sound per press
  (cooking stations keep one sound per food slot).
- **Same rules as pressing E several times**: the game picks the item each time (a smelter takes the first ore of its
  list that you carry, so copper and tin can go in with one press), and cooking raises your Cooking skill as much as
  5 presses would.
- Turn it off in the **MC Mods** panel at any time to get vanilla one-item presses back, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Crafting.Stations.BatchFeed.cfg` is created the first time you launch the game with the mod.
To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | Amount | `5` | How many items one batch press adds at most (2 to 50). Fewer are added when the station has less room or you carry fewer. |
| General | ModifierKey | `None` | Key to hold while pressing Use (E) to add several items. None = the game's own "Alternative placement" key, which is Left Shift by default (Right Shift is not part of it); you can change it in the game's Settings, controls. Pick a key here to use a different key for this mod only, for example RightShift or LeftAlt, which the game does not use while you play; avoid LeftControl (Crouch) and other keys that already do something. With a controller, this setting is ignored and the game's own button is always used: Alternative placement on the default controller layout, the alt-keys button (the one you hold for alternative functions) on the Alternative 1 and 2 layouts. The hover hint shows which button to hold. |
| General | ShowHint | `true` | Show the batch key hint below the Use hint when you look at a station. |

Turning `ShowHint` off only hides the hint line: the batch key keeps working.

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Only you need it; it works on vanilla servers and with friends who do not have it. Each item is added through the game's normal add action, exactly as if you pressed E several times, so stations filled this way are normal for everyone.

A friend without the mod sees the same counters on a station you filled, and can add, empty and take out products
as usual. The mod sends nothing of its own over the network and stores nothing on stations.

When another player's game runs a station (usually the first player who came near it), your game sees its contents a
moment late. The mod remembers what you just added for up to 3 seconds, so your quick presses do not overfill a
station or waste items on a full fire or cooking station (unless the other game takes longer than that to answer):
right after a batch you may get the "full" message a little early instead. A ballista gets one missile kind at a time,
so a batch cannot turn wooden missiles into black-metal ones.

## Good to know

- The default key is **Left Shift** only. Right Shift is not bound to anything while you play (the game only uses it
  to split stacks in the inventory); set `ModifierKey = RightShift` to use it instead.
- Left Shift is also Run: pressing Shift+E while standing next to a station does not move you.
- With a hammer, hoe or cultivator in your hands the game shows no hover text on stations and E does nothing
  (vanilla); put the tool away first.
- A cooking station that holds a cooked item gives it back first, like E: Shift+E takes one cooked item, and no hint
  is shown while a cooked item is waiting there.
- A spot that holds only one item (for example the Frost Foundry's cast slot) stays vanilla: no hint, one item per
  press.
- The Hot Tub is fed like a smelter's fuel slot, so its message reads "Added 5 Wood (5/10)".
- Holding Shift+E adds a batch, then keeps adding one at a time at the game's normal hold speed, as holding E does.
- The number keys (hotbar "use item", 1-8) and the radial use-item menu stay vanilla. On a station another player's
  game runs, pressing a number key right after a batch is not tracked, so it can race like fast vanilla presses (a
  smelter can go over its maximum, an item put on a full fire can be lost).
- Right after the player whose game runs a station leaves the game (for about 2 seconds, longer if their game
  crashed), Shift+E adds only one item, like E. The game may lose that one item, as it does in vanilla; the mod never
  loses more.
- During a batch press, the per-item messages in the middle of the screen are replaced by one summary. A tame message
  always shows, and the first level of a skill (for example Cooking 1 on a new character) is shown under the summary.
  Nothing is hidden outside the batch press.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing on stations, items or characters.

- **Other "fill all" mods** (BulkSmelt, ComfyAddAllFuel, Add All Fuel And Ore): according to their pages they also
  use Shift+E (BulkSmelt's key cannot be changed). With both installed, one press adds what the other mod adds and
  this mod stops right after it. Keep only one of them, or give this mod another key with `ModifierKey`.
- **AutomaticFuel, ServersideQoL auto-processing** (stations pull from chests): should work together; this mod reads
  the station contents live when you press.
- **ValheimPlus and other mods that change capacities or make fires infinite**: bigger capacities are used as they are,
  infinite fires get no hint.
- The third-party mods above have not been tested in game with this one yet.
- **One Click Repair All** (MC): both hide per-item messages only during their own action and show one summary each;
  they work together.
- **Creature Kill and Tame Counts** (MC): tame messages are never hidden, so tames are still counted.
- **Crossbow Stays Loaded** (MC): unrelated, works together.
