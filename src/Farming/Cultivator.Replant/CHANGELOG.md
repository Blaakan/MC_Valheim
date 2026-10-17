# Changelog

## 1.0.0

- First release.

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists Cultivator
  Replant under "MC mods:" with the reason (your game does not have it, has another version, or has it turned off or
  cannot run it) and what to do (install it, install the server's version, or turn it on in Esc > MC Mods, or with F1
  when a configuration manager is installed). With Jotunn on the server and on your game, Jotunn's window shows the
  same lines under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them.
- The list needs at least one MC mod in your game that is made to run on both the server and the players (any mod a
  server can refuse you for, Cultivator Replant included, or Sleep Through the Day); the server does not need to have
  that mod. A game whose MC mods are all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it on,
  or a server copy whose version cannot play with yours. That may not be why you were refused.

## 0.1.1

- Fixed: while the mod is turned off (Enabled = false, or on a server without the mod), young transplants kept the
  grow time of the last rules that were in force: a server's, or your own setting as it was before. They now grow on
  your own GrowTimeMultiplier setting, as the README says, and a change of that setting while the mod is off reaches
  them at once, also the ones already in the ground.
- Fixed, with PlantEasily installed: its own placement check turned the ghost green where the game refuses a
  transplant, so a Yggdrasil shoot transplant could be planted away from any Ancient Root (where it never grows) and
  any transplant inside a no-build zone. The transplant you aim at now stays red there and nothing is planted.
  Nothing changes without PlantEasily.

## 0.1.0

- Initial version. Replant: with the cultivator in hand, aim at a wild plant and press E (gamepad X) to dig it up as
  a transplant item. A ripe plant is harvested first. Costs the tool's stamina and a little durability, like removing
  a piece; wards protect plants, no-build zones do not. Not while steering a ship or riding a lox.
- The vanilla (bronze) cultivator digs up dandelions, thistle, mushrooms, yellow mushrooms, raspberry and blueberry
  bushes. Four new cultivator levels, made in the Forge's Upgrade tab (Forge level 4 to 7), unlock more: black metal
  (5 Black metal, 10 Linen thread) cloudberry bushes, eitr (15 Refined eitr) Yggdrasil shoots, flametal (5 Flametal)
  fiddleheads and smoke puffs, bloodgold (5 Bloodgold) lingonberry bushes. The Forge of Potential no longer lists the
  cultivator.
- Transplants are planted with the cultivator, need no cultivated soil and grow into the same vanilla plant, ripe,
  after the plant's own regrowth time on the world clock (bushes 5 h, forage 4 h, Yggdrasil 2 h of play). They grow
  on open ground in any land biome (only inside a shield in the Ashlands, the Mountains and the Deep North);
  fiddleheads and smoke puffs grow only in the Ashlands, lingonberries only in the Deep North; a misplaced transplant
  waits instead of dying, and Replant digs up a young transplant again.
- Yggdrasil shoot transplants are planted 2 to 6 m from an Ancient Root and take 20 of its sap when they grow; they
  wait while the root is too low. Their hover text shows the root's sap.
- Tooltip lines name the cultivator's tier and what it can replant (the upgrade preview says what the next level
  adds); levels 4 to 7 get a coloured gem on the icon, in place of the level number in the inventory; transplant
  items show the produce icon with a sprout in the upper-right corner; a crosshair hint says "[E] Replant" or the
  level needed.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with
  it turned off or with another version of it (AllowPlayersWithoutMod), and its upgrade materials, grow time, root sap
  cost and root range apply to everyone.
