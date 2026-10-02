# Harpoon Hooks Tames

The Abyssal Harpoon hooks your tamed animals like wild creatures, but the hit never hurts them: no damage, no knockback. Pull a stray animal back to its pen. Only the thrower needs the mod.

## Features

- **The Abyssal Harpoon hooks tamed creatures.** In vanilla, a harpoon thrown at a tame flies straight through it
  (unless your PvP is on). With the mod, it hooks the tame exactly like a wild creature: the same "Boar harpooned"
  message, the same line, the same pull when you walk away, the same stamina drain, the same release rules ("Line
  broke" when you go too far, "Boar released" when your stamina runs out or when you block or attack after the first
  2 seconds), and the same distance limit ("Target too far").
- **The hit never hurts the tame**: no damage, no damage number, no knockback, no stagger, no sneak-attack bonus. The
  tame does not flee or turn on you. The hit cannot be blocked, so even a tame that is blocking with a shield is
  hooked.
- **Every tamed creature counts**, including summons (for example the skeletons of the Dead Raiser), whatever it is
  doing: fighting, chasing prey, running away, or watching a wild creature behind a fence. The only exception is a
  tame someone is riding (see Multiplayer). Because every tame counts, a tame standing between you and an enemy
  catches the harpoon (see Good to know).
- **Nothing else changes**: wild creatures, players, bosses, and every other weapon and projectile behave as in
  vanilla. Only harpoon hits on tamed creatures are changed.
- Turn it off in the **MC Mods** panel at any time: the harpoon behaves as in vanilla again (it flies through tames
  while your PvP is off), no restart needed.

## Configuration

The config file `BepInEx/config/MC.Farming.Harpoon.HooksTames.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |

There is no other setting. The pull, the line length and the stamina drain stay vanilla: they are run by the game that
controls the animal, which may be a friend's game without the mod.

## Multiplayer

- **Who needs it:** only the player who throws the harpoon. It works on vanilla servers and with friends who don't
  have it.
- **Multiplayer support:** works in multiplayer.

Only the player who throws the harpoon needs it. It works on vanilla servers and with friends who do not have it: the pull itself is done by the normal game on whichever computer runs the animal. Friends without the mod cannot hook tames while their PvP is off; with PvP on the normal game already lets them, and their hit can hurt the animal. If the computer running the animal does not run the mod (not installed, or turned off), the game may later credit you with the animal's kill.

- **Kill credit.** In vanilla, every player who hits a creature, even for no damage, is credited with its kill if it
  dies later while that player is connected. A hook is not an attack, so the mod removes that mark again. It can only
  do that on the game that runs the animal: always in single player, and in multiplayer when that game runs the mod
  (installed and turned on). Otherwise the mark stays, and if the tame dies later (a wolf, fire, a friend's Butcher
  Knife) while you are connected, the game may count it as your kill.
- **Ridden tames** follow the vanilla PvP rule: with your PvP off, the harpoon flies past a tame someone is riding;
  with your PvP on, it is hooked (mount and rider are pulled), still without damage.
- The mod only protects tames from **your own** harpoon. It sends nothing of its own over the network and stores
  nothing on creatures.

## Good to know

- **A tame in the line of fire catches the harpoon.** A harpoon stops at the first creature it can hook, and with the
  mod every tame can be hooked, even one that is fighting. So a harpoon thrown at an enemy behind your own wolf or
  skeleton hooks the tame instead: the tame is not hurt, and the enemy is not hooked. Block or attack to release the
  tame, and throw from an angle where none of your tames is in the way. The game already does the same with your PvP
  on (and there the tame is hurt; with the mod it is not). In exchange, you can pull back a tame whatever it is busy
  with: chasing prey, running away, fighting, or watching a wild creature it cannot reach.
- **PvP on:** vanilla already lets you hook tames with PvP on, and damages them. With the mod, your harpoon never
  damages a tame, PvP or not. To hurt a tame on purpose, use another weapon or the Butcher Knife.
- **No Spears skill and no adrenaline** for hooking a tame (otherwise a pen would be a free training spot). Harpoon
  hits on wild creatures give both, as usual.
- **"Stay" still applies:** a tame told to stay walks back to its stay spot after you release it. To move it for good,
  tell it to follow, drag or lead it to the new spot, then tell it to stay there.
- **Heavy tames drain more stamina**: the game's pull cost grows with the creature's weight. Dragging a lox: to verify
  in game.
- **Ships:** a tame standing on a ship deck is hooked but not pulled, and a tame in the water cannot be pulled up onto
  a deck (vanilla harpoon rules).
- **Where you drag it is up to you.** The mod only takes the harm out of the harpoon hit. A tame dragged into fire,
  lava or Ashlands water takes that damage as usual (creatures never take fall damage). A saddled mount whose saddle
  stamina is empty slowly drowns in deep water, as in vanilla.
- A hook still adds 1 to the game's own "Enemy Hits" statistic when your game runs the animal, like any harpoon hit.
- Turning the mod off while a tame is hooked keeps that line until it breaks or you release it; the next throws
  behave as in vanilla.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing on items, creatures or characters.

- **Creature Kill and Tame Counts** (MC): a harpooned tame that dies later is not counted as your kill when the game
  running the tame runs this mod (always in single player). When that game does not run it (a friend without the mod,
  or with it turned off), the game may credit you with the kill, and it then shows in the Creatures list. A tame you
  kill yourself (Butcher Knife) counts once, as usual.
- **Crossbow Stays Loaded** (MC): works together; switching between a loaded crossbow and the harpoon keeps the
  crossbow loaded.
- **One Click Repair All, Batch Station Feeding** (MC): unrelated, work together.
- **HarpoonExtended** with creature pulling on: it replaces the harpoon's hook with its own rope, so this mod sees no
  hook and does nothing. With your PvP off, tames are not hooked; with PvP on, HarpoonExtended pulls them with its
  own rope and they take damage (unless its own no-damage option is on). One line in the log says so.
- **ValheimPlus** with immortal tames: ValheimPlus empties the hit first, so tames are not hooked (no "harpooned"
  message, no pull) and take no damage.
- **EpicLoot** enchanted harpoons: the hit and the enchantment effects that strike the hooked tame right away are
  blocked. Delayed effects (meteors, projectiles fired a moment later, slow or paralysis applied afterwards) may still
  hurt it.
- **TamedHarpoon** does the same thing as this mod: install only one of them.
- The mods above have not been tested in game with this one yet.
