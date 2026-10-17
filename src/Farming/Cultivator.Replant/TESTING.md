# Cultivator Replant — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.2 (the refusal screen names the mod, section "0.1.2"; before that 0.1.1), build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). 0.1.1
fixed the grow time of young transplants while the mod is off (T39: they kept the grow time of the last rules in force
instead of the player's own GrowTimeMultiplier, and a change of that setting while the mod was off never reached
them): T39 is reset to `[ ]`, T40 and M14 are new. 0.1.1 also keeps a transplant refused away from an Ancient Root and
inside a no-build zone when PlantEasily is installed (PlantEasily's own placement check turned the aimed ghost green
there): E04 is reworded and reset to `[ ]`, E02 (b) is reworded. The self-tests ran on 0.1.1 on 2026-10-13 (game
1.0.17, in-world with PlantEasily 2.2.2 installed and with a dedicated server, every MC mod loaded): all of this mod's
tests passed, `replant.rootplace` on a second run alone (see the automated checks below). History, on 0.1.0 with the
review fixes of 2026-10-03: smoke test (`./tools/Test-Smoke.ps1 -Mod Cultivator.Replant`) passed 2026-10-03. In-world
self-tests (`./tools/Test-InWorld.ps1 -Mod Cultivator.Replant -Only replant.`): all 7 `replant.*` tests passed
2026-10-03 (`replant.network`, `content`, `tiers`, `action`, `grow`, `root`, `icons`; they confirm the prefab values
at runtime). Both runs were made with the third-party mods of the test machine installed (Jotunn, Warfare, PlantEasily
and others); the in-world script still reports two false "missing from the item database" errors from Spyglass and
Sneak Ambush caused by Warfare's item manager (a separate fix) and one PlantEasily exception when a test removes the
last transplant while it is selected (see E02). No hands-on in-game test yet.

**Automated checks (2026-10-17):** 64 of 67 self-tests named in this list's notes passed in real game runs on Valheim
1.0.20 (`./tools/Test-InWorld.ps1`, `./tools/Test-Multiplayer.ps1` with a dedicated server) on the code of commit
`f68eafe`; failing: `replant.x.spyglass-view`, `replant.e.planteasily.grid`, `replant.e.planteasily.root`. An item
ending in "Automated: ..." is checked in full by the named self-tests and is ticked by them alone; a "Partly
automated" item still needs its by-hand part; "FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive, `heal` refills health and stamina, `killall` removes nearby creatures, `debugmode` then `Z` flies.

- **Cultivators:** `spawn Cultivator 1 3` drops a level 3 cultivator in front of you (the last number is the level;
  the spawn command gives at most level 4, so levels 5 to 7 are made at the Forge). To get the Upgrade tab, know the
  Cultivator recipe: pick up its materials once near a Forge (`spawn Bronze 5`, `spawn RoundLog 5`; RoundLog is
  Corewood).
- **Upgrade materials:** `spawn BlackMetal 5`, `spawn LinenThread 10`, `spawn Eitr 15` (Refined eitr),
  `spawn FlametalNew 5` (Flametal), `spawn Gold 5` (Bloodgold); `spawn Upgrader0Weapon` (Wooden Battle Idol).
- **Forge levels:** a Forge is level 1 plus one level per extension (`forge`, `forge_ext1` … `forge_ext6`), so
  cultivator level N needs N − 1 extensions and level 7 all six. Build the Forge and its extensions with the hammer
  while `nocost` is on (building is then free and every piece is unlocked; `forge_ext1` must stand within 2 m of the
  Forge, the others within 5 m), then type `nocost` again to turn it off: it also makes upgrades free.
- **Other stations:** `spawn UpgradeStation` places a Forge of Potential (it is big: step back so you are not inside
  it). `guard_stone` is the ward.
- **Plants:** `spawn RaspberryBush`, `BlueberryBush`, `CloudberryBush`, `LingonberryBush`, `Pickable_Dandelion`,
  `Pickable_Mushroom`, `Pickable_Mushroom_yellow`, `Pickable_Thistle`, `Pickable_Fiddlehead`, `Pickable_SmokePuff`,
  `YggaShoot_small1`, `YggaShoot1` (also `2` and `3`) and `YggdrasilRoot` (the Ancient Root; big, step back). A spawned
  plant appears 2 m in front of you and may float a little; that does not matter. Wild ones: raspberries in the
  Meadows, blueberries in the Black Forest, cloudberries in the Plains, thistle in the Swamp and Black Forest,
  dandelions in the Meadows, Yggdrasil shoots and Ancient Roots in the Mistlands, smoke puffs in the Ashlands,
  lingonberries in the Deep North; yellow mushrooms grow in dungeons.
- **Travel:** `findbiometp mistlands`, `findbiometp ashlands`, `findbiometp deepnorth`, `findbiometp mountain`
  teleport you to the nearest such biome; `findtp YggdrasilRoot` to the nearest Ancient Root. `goto 0 -9000` is in
  the Ashlands and `goto 0 9000` in the Deep North on every world (maybe over the sea: fly).
- **Time:** `skiptime 20000` jumps the world clock 20000 s ahead (5.6 h: every bush, forage and fiddlehead
  transplant is due); `skiptime 8000` is enough for a Yggdrasil transplant. A transplant grows within about 10 s after
  that.
- **Our own prefab names** (from the code): transplant items `MC_Transplant_<Key>` and young plants
  `MC_Sapling_<Key>`, with Key = `Dandelion`, `Thistle`, `Mushroom`, `MushroomYellow`, `RaspberryBush`,
  `BlueberryBush`, `CloudberryBush`, `YggaShoot`, `Fiddlehead`, `SmokePuff`, `LingonberryBush`
  (`spawn MC_Transplant_RaspberryBush 5` gives five raspberry bush transplants).

**Names checked:** every vanilla prefab name above is in the 1.0.16 game data (SoftRef manifest, checked in game
data); the plant, Cultivator, recipe and Ancient Root values quoted below were read from the 1.0.16 game data
(asset read, not yet at runtime). `Raft` (T33) is in the 1.0.16 runtime prefab dump
(`docs/game/exploration-player.md`). Every console command above is in the game code (`Terminal`). Not checked: the
gamepad glyph shown in the hint (T22), how the game names each plant in the hint, how WackysDatabase and
RecipeCustomization are set up to change a recipe (E03).

Some expected results name log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and follow the
log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Farming.Cultivator.Replant.cfg`; default settings unless an item says otherwise, and put each
changed one back afterwards.

## 0.1.0 — single player

- [x] **T01 Replant a raspberry bush (G1, E1, E5):** level 1 cultivator in hand (build mode), a ripe raspberry bush
  (`spawn RaspberryBush`). Aim at it. Expected: under the crosshair the bush's name and "[E] Replant", crosshair
  yellow. Press E. Expected: the raspberries drop as when picked by hand, the bush disappears with no wood dropped,
  "Raspberry bush transplant" is added to the inventory (top-left message with its icon), the cultivator swings, a
  little stamina is used and the cultivator loses about 1 durability. E is also the game's build-mode "cycle
  snapping" key: the same press does nothing else (no snap change on the selected piece).
  *Automated: `replant.dig`, `replant.swing`.*
- [x] **T02 The other bronze plants (G1):** with the level 1 cultivator, dig up a ripe blueberry bush, mushroom,
  yellow mushroom, thistle and dandelion. Expected: each gives its own transplant ("Blueberry bush transplant",
  "Mushroom transplant", "Yellow mushroom transplant", "Thistle transplant", "Dandelion transplant") and its produce.
  *Automated: `replant.bronze`.*
- [x] **T03 Plant a transplant (G1):** open the cultivator's build menu. Expected: the transplants you have had are at
  the end of the list, with the transplant icon; their description says where they grow. Place the raspberry bush
  transplant on plain, uncultivated ground. Expected: it can be placed (no cultivated soil needed) and uses one
  transplant; a young plant appears; its hover text (put the cultivator away) shows "Raspberry bush transplant" and
  a healthy status. It cannot be placed on a wooden floor.
  *Automated: `replant.plant`.*
- [x] **T04 It grows into the vanilla plant, ripe (G1, D5):** after T03, `skiptime 20000`. Expected: within about
  10 s the young plant becomes a raspberry bush with berries, a little bigger or smaller than usual. Pick it: normal
  berries; `skiptime 20000`: the berries are back. Do the same with a mushroom transplant: it grows into a ripe
  mushroom that regrows after picking.
  *Automated: `replant.clock`.*
- [x] **T05 Heat and cold (D10):** `findbiometp mountain`, plant a raspberry bush transplant. Expected: its hover says
  "Too cold"; after `skiptime 20000` it is still a young plant (it waits, it is not destroyed). Same in the Ashlands
  ("Too hot"). Inside a working shield generator's dome it grows (optional).
  *Automated: `replant.far.mountain`, `replant.far.ashlands`.*
- [x] **T06 Harvest first, details (E1):** (a) pick a raspberry bush by hand, then dig it up. Expected: no berries,
  one transplant. (b) Pick a mushroom by hand, then aim at the spot. Expected: no hint, E does nothing (a picked
  forage plant cannot be dug until it regrows). (c) `skiptime 20000`, then dig it up. Expected: mushroom and
  transplant.
  *Automated: `replant.picked`.*
- [x] **T07 Dig up a young transplant (E2):** plant a raspberry bush transplant and aim at the young plant. Expected:
  hint "Raspberry bush transplant" and "[E] Replant". Press E. Expected: it disappears and you get the transplant
  back (no produce). With a level 4 cultivator, aim at a young Yggdrasil shoot transplant (after T13). Expected:
  "Needs an eitr cultivator (level 5)", E refused.
  *Automated: `replant.sapling`.*
- [x] **T08 The vanilla cultivator is unchanged (G1):** craft a cultivator at a Forge: 5 Corewood and 5 Bronze.
  Upgrade it to level 2 (1 Corewood, 1 Bronze, Forge level 2) and level 3 (2 and 2, Forge level 3). Expected: as in
  the normal game. With it, cultivate ground, plant a carrot seed and use "Grass" (replant grass): all as before.
  *Automated: `replant.forge.low`, `replant.vanilla`.*
- [x] **T09 Level needed (G2, E5):** level 3 cultivator, aim at a cloudberry bush (`spawn CloudberryBush`). Expected:
  "Needs a black metal cultivator (level 4)" in grey under its name, crosshair not yellow. Press E. Expected: the
  same text in the middle of the screen; nothing else happens (no stamina, no swing).
  *Automated: `replant.tier`.*
- [x] **T10 Upgrade to black metal (G2, E3):** level 3 cultivator, a Forge with two extensions (level 3), 5 Black
  metal and 10 Linen thread. Upgrade tab. Expected: the Cultivator is listed; it asks for 5 Black metal and 10 Linen
  thread only (no Bronze, no Corewood, no idol) and Forge level 4, shown in red; the preview says "Tier: Black metal
  cultivator", "Replants: Dandelion, Thistle, Mushroom, Yellow mushroom, Raspberry bush, Blueberry bush, Cloudberry
  bush" and "New: Cloudberry bush". Build a third extension (Forge level 4) and upgrade. Expected: the materials are
  used, the cultivator is level 4 with 800 durability and a dark grey gem in the upper-right corner of its icon, and
  its inventory slot shows no level number.
  *Automated: `replant.forge.low`, `replant.gems`.*
- [x] **T11 Cloudberries (G2):** with the level 4 cultivator, dig up a cloudberry bush; plant the transplant; after
  `skiptime 20000` it is a cloudberry bush with berries.
  *Automated: `replant.cloudberry`.*
- [x] **T12 Eitr level and Yggdrasil shoots (G3):** Forge level 5, 15 Refined eitr: upgrade the level 4 cultivator.
  Expected: costs 15 Refined eitr only, cyan gem. In the Mistlands (or with `spawn YggaShoot_small1` and
  `spawn YggaShoot1`), dig up a small shoot and a shoot tree. Expected: each gives a "Yggdrasil shoot transplant", no
  wood; a level 4 cultivator gets "Needs an eitr cultivator (level 5)" instead.
  *Automated: `replant.forge.high`, `replant.levels`, `replant.gems`.*
- [x] **T13 Planting near an Ancient Root (G3, E5):** `findtp YggdrasilRoot` (in the Mistlands) or `spawn
  YggdrasilRoot` in an open spot. Select the Yggdrasil shoot transplant in the build menu, aim at ground away from
  the root. Expected: the ghost is red and the hint says "Plant 2 to 6 m from an Ancient Root". Aim at ground 3 to 5 m
  from the root. Expected: the ghost turns green; place it. Right against the root (under 2 m): the ghost is green
  too, but the young plant says "Needs more room to grow" (vanilla rule); Replant gives it back. In
  ConfigurationManager, RootRange cannot go below 4.
  *Automated: `replant.rootplace`.*
- [x] **T14 It takes the root's sap (G3, E6):** before planting set GrowTimeMultiplier = 0.1 and RootSapCost = 40.
  Plant one Yggdrasil transplant 3-6 m from a root nobody has tapped (full). Put the cultivator away and hover it.
  Expected: "Draws 40 sap from the Ancient Root when grown (root: 50 / 50)". `skiptime 900`. Expected: within about
  20 s it grows into a Yggdrasil shoot tree (trunk grow animation).
  *Automated: `replant.sap`, `replant.mp.rules`, `replant.trunk`.*
- [x] **T15 It waits for sap (G3, D7):** right after T14, plant a second Yggdrasil transplant near the same root, at
  least 4 m from the new tree. Expected: hover "(root: 10 / 50)" or a little more, with "- waiting for it to refill".
  `skiptime 900`. Expected: after 30 s it is still a young plant, still healthy. `skiptime 12000`. Expected: within
  about 20 s it grows; a third transplant then reads about "root: 2 / 50". Put both settings back.
  *Automated: `replant.sap`.*
- [x] **T16 No root in reach (G3):** set RootRange = 20, plant a Yggdrasil transplant about 15 m from a root (the
  ghost is green), then set RootRange back to 6. Expected: its hover says "Needs an Ancient Root within 6 m"; after
  `skiptime 8000` it is still a young plant. Dig it up with Replant: the transplant comes back.
  *Automated: `replant.range`, `replant.mp.rules`.*
- [x] **T17 Flametal level, Ashlands plants (G4):** Forge level 6, 5 Flametal: upgrade the level 5 cultivator.
  Expected: costs 5 Flametal only, orange gem. `findbiometp ashlands`: dig up a smoke puff and a fiddlehead (or spawn
  them). Expected: transplants, plus 1 smoke puff and 3 fiddleheads when ripe; a level 5 cultivator gets "Needs a
  flametal cultivator (level 6)". Select the fiddlehead transplant in the Meadows. Expected: red ghost. In the
  Ashlands: green on ash ground, red on lava; planted outside any shield, after `skiptime 20000` both are grown
  fiddleheads and smoke puffs.
  *Automated: `replant.forge.high`, `replant.gems`, `replant.levels`, `replant.wrongbiome`, `replant.far.ashlands`.*
- [x] **T18 Bloodgold level, lingonberries (G5):** Forge level 7 (all six extensions), 5 Bloodgold: upgrade the level
  6 cultivator. Expected: costs 5 Bloodgold only, crimson gem, 1400 durability. `findbiometp deepnorth`: dig up a
  lingonberry bush (a level 6 cultivator gets "Needs a bloodgold cultivator (level 7)"). The transplant's ghost is red
  outside the Deep North, green in it, also on deep snow; planted outside any shield, after `skiptime 20000` it is a
  lingonberry bush with berries.
  *Automated: `replant.forge.high`, `replant.gems`, `replant.levels`, `replant.wrongbiome`, `replant.far.north`.*
- [x] **T19 Tooltip (E3):** hover cultivators of each level in the inventory. Expected: right after "Quality: N",
  "Tier: Bronze cultivator" (levels 1-3), "Black metal", "Eitr", "Flametal", "Bloodgold cultivator" (4-7), then
  "Replants: ..." listing the plants of that level and below. In the Upgrade tab, the preview of levels 2 and 3 has no
  "New:" line; levels 4 to 7 name the new plants.
  *Automated: `replant.tooltip`.*
- [ ] **T20 Icons (E3, E4):** Expected: levels 4-7 show their gem (dark grey, cyan, orange, crimson) in the
  inventory, the hotbar, a chest and the top-left messages; levels 1-3 show the plain icon. Each transplant shows its
  produce icon (raspberry, mushroom, Yggdrasil wood...) with a brown soil mound and a green sprout in the upper-right
  corner; the same icon in the build menu. (Readability against the slot's texts: T36 and T37.)
  *Partly automated (`replant.slots`, `replant.gems`, `replant.icons`); by hand: That the transplant mark really looks
  like a brown soil mound with a green sprout on the produce icon.*
- [x] **T21 Transplant item (E4):** hover a transplant. Expected: plain English name and description (what it is, how
  to plant it, where it grows), weight 0.5, no food values; right-click does not eat it; it stacks to 20; it can go
  through a portal. A bush or forage transplant says it grows on open ground in any land biome with no tilling, and
  only inside a shield in the Ashlands, the Mountains and the Deep North; the Yggdrasil one says within a few metres
  of an Ancient Root, but not right against it. Drop one: it lies on the ground as the produce model and can be
  picked up again.
  *Automated: `replant.tooltip`.*
- [ ] **T22 Gamepad (E5, D8):** with a gamepad, Default layout: aim at a bush. Expected: the hint shows the X button
  (glyph unverified; never a keyboard key) and "Replant"; X digs it up and does not make you sit; the build menu
  still opens with its own button; holding the alternate-keys button (LT in the Default layout) while pressing X does
  not dig; LB does not dig. In the Alternative 1 and 2 layouts, X digs as well and the hint shows its glyph.
  *Partly automated (`replant.pad`); by hand: A real controller: the glyph actually drawn in the hint, a physical X
  press that does not also sit (the test player has its controller script off), real LT and LB.*
- [ ] **T23 Costs and refusals:** (a) sprint until stamina is empty, press E on a plant. Expected: the stamina bar
  flashes, nothing is dug. (b) Rebind Use to another key in the settings. Expected: the hint shows the new key and it
  digs; E does not. Put it back. (c) Open the build menu and press E or X over a plant: nothing is dug.
  *Partly automated (`replant.costs`, `replant.input.rebind`); by hand: The Settings > Controls window itself:
  clicking the Use row there and pressing OK (the test starts the game's key-binding step directly while the pause
  menu is open; clicks on UI elements are not driven, and the test never saves the binding).*
- [ ] **T24 No-build zone and dungeon (D4):** inside a Burial Chamber or Troll Cave, dig up a yellow mushroom.
  Expected: it works (no "can't build here" refusal). Trying to plant the transplant inside the dungeon is refused
  by the game as usual.
  *Partly automated (`replant.nobuild`); by hand: Inside a real dungeon (Burial Chamber or Troll Cave): digging works
  and planting the transplant is refused.*
- [x] **T25 Full inventory:** fill every inventory slot, then dig up a bush. Expected: the transplant drops where the
  bush stood with the game's "no room" message; pick it up after making room.
  *Automated: `replant.full`.*
- [x] **T26 Settings (E7):** (a) BlackMetalLevel = `Wood:2`. Expected: within a second the Upgrade tab asks 2 Wood for
  level 4. (b) BlackMetalLevel = `Nothing:5`. Expected: the log warns that "Nothing" is not an item and that cultivators
  stop at level 3; a level 3 cultivator is no longer listed for upgrade; a level 4 one stays level 4. (c) EitrLevel
  empty: cultivators stop at level 4. (d) GrowTimeMultiplier = 0.1, then plant a raspberry transplant: it grows after
  `skiptime 2000`. Put every setting back.
  *Automated: `replant.settings`, `replant.clock`, `replant.mp.rules`.*
- [x] **T27 Forge of Potential (D3):** at a Forge of Potential, carry a level 1, a level 3 and a level 4 cultivator, a
  Stone axe (`spawn AxeStone`) and a Wooden Battle Idol. Expected: its upgrade list shows the axe but no cultivator;
  the log has no "has no upgrader resource" warning for the cultivator.
  *Automated: `replant.potential`.*
- [x] **T28 A cultivator refined before (D3):** `spawn Cultivator 1 4` (a level 4 cultivator made without the
  mod's upgrade). Expected: it is a black metal cultivator (gem, tooltip) and digs up cloudberries; the Upgrade tab
  offers level 5 for 15 Refined eitr.
  *Automated: `replant.levels`, `replant.forge.high`.*
- [x] **T29 Scythe:** swing a scythe (`spawn Scythe`) over a raspberry transplant in the Mountains (too cold, as in
  T05) and over a Yggdrasil transplant that waits for sap (as in T15). Expected: the too-cold one is destroyed (the
  game's rule for sickly crops); the Yggdrasil one stays.
  *Automated: `replant.far.mountain`, `replant.scythe`.*
- [x] **T30 Live toggle:** with transplants in your inventory, a young transplant in the ground and a level 4
  cultivator, untick Cultivator Replant in the MC Mods panel. Expected: E on a plant does nothing and no hint shows;
  the transplants leave the build menu; the young transplant stays and keeps growing; the transplant items stay; the
  level 4 cultivator keeps level 4 but loses its gem and tier lines and shows its level number again; a level 3
  cultivator is no longer listed in the Upgrade tab; the Forge of Potential lists the cultivator again. Tick it again:
  everything is back.
  *Automated: `replant.toggle`.*
- [x] **T31 Enabled = false + restart:** set Enabled = false, restart the game and load the character and world.
  Expected: the transplants are still in the inventory and in chests; planted young transplants are still there
  (hover shows their name) and grow; no transplants in the build menu; the level 4 cultivator is still level 4;
  Status says off. Set it back to true.
  *Automated: `replant.toggle`, `replant.disabled.kept`, `probe.disabled.MC.Farming.Cultivator.Replant`.*
- [ ] **T33 Ship helm and lox saddle:** stand in shallow water at a coast and `spawn Raft`, take the helm, then
  equip the cultivator (hotbar key) and steer close to a bush (`spawn RaspberryBush` on the shore) so it is within
  reach. Expected: no Replant hint and no yellow crosshair while steering. Press E. Expected: you let go of the helm
  and nothing is dug; the next E digs the bush. With a gamepad while steering: no hint, X digs nothing. Optional: the
  same on a saddled, tamed lox.
  *Partly automated (`replant.helm`); by hand: The same while really seated at the helm of a ship on water (the test
  holds the helm from the ground beside a ship frozen on land), and the optional lox.*
- [x] **T34 Level 3 cultivator from a chest after turning off:** with the mod on, put a level 3 cultivator in a chest
  and stay near it. Untick Cultivator Replant in the MC Mods panel, take the cultivator out and open the Upgrade tab
  at a Forge of level 4 or more with 5 Bronze and 5 Corewood. Expected: no Cultivator row ("Upgrade Cultivator to 4"
  never shows). Tick the mod on again: the level 3 cultivator is listed for level 4 with the black metal materials.
  *Automated: `replant.toggle`.*
- [x] **T35 Grow time change reaches planted transplants:** plant a raspberry transplant with the default
  GrowTimeMultiplier 1 and stay near it. Set GrowTimeMultiplier = 0.1, wait a second, then `skiptime 2000`. Expected:
  it grows within about 10 s, without leaving the area. Put the setting back.
  *Automated: `replant.clock`, `replant.mp.rules`.*
- [ ] **T36 Gem instead of the level number:** level 4, 5, 6 and 7 cultivators in the inventory and in a chest.
  Expected: each slot shows its gem fully, with no level number on it, and neither the hotbar key number (first row)
  nor the durability bar covers it; the tooltip names the level ("Quality: 4") and the tier. Level 1, 2 and 3
  cultivators still show their number. Untick the mod: levels 4-7 show their number again (no gem).
  *Partly automated (`replant.slots`, `replant.toggle`, `replant.forge.low`); by hand: That the gem is fully visible
  in the slot: not covered by the hotbar key number of the first row or by the durability bar.*
- [ ] **T37 Sprout and stack amount:** a stack of 5 and a stack of 20 raspberry transplants in the inventory, in a
  chest and on the hotbar. Expected: the sprout mark (upper-right) and the amount ("5/20", "20/20"; "5 / 20" on the
  hotbar) are both fully readable, and no other slot mark (hotbar key number, food or no-portal icon) covers the
  sprout.
  *Partly automated (`replant.slots`); by hand: That sprout and amount are both fully readable and nothing overlaps
  the sprout (hotbar key number).*
- [x] **T38 Root already found, then RootRange shrinks (G3):** set RootRange = 20 and plant a Yggdrasil transplant
  about 15 m from a full Ancient Root (as in T16). Put the cultivator away and hover it. Expected: "Draws 20 sap from
  the Ancient Root when grown (root: 50 / 50)". Set RootRange back to 6 and hover it again. Expected: "Needs an Ancient
  Root within 6 m" at once (the root it had found is out of reach now). After `skiptime 8000` it is still a young
  plant, and the root has lost no sap. Replant gives the transplant back.
  *Automated: `replant.range`.*
- [ ] **T39 Own grow time while the mod is off:** set GrowTimeMultiplier = 0.1 and wait a second with the mod on (or
  play on a server whose rules use another grow time than yours), then untick Cultivator Replant in the MC Mods panel
  and only then set GrowTimeMultiplier back to 1. `spawn MC_Sapling_RaspberryBush`, wait 15 s, then `skiptime 2000`.
  Expected: it stays a young plant (with the mod off a transplant grows on your own GrowTimeMultiplier, as the README
  says: 1 = 5 h for a bush); after `skiptime 20000` it grows. The same after a restart with Enabled = false and
  GrowTimeMultiplier = 0.5: a transplant then needs 2.5 h (`skiptime 10000`), not 5 h.
  *Partly automated (`replant.toggle`, `replant.mp.allowed`, `replant.disabled.owngrow`, `replant.mp.offgrow`); by
  hand: GrowTimeMultiplier = 0.5 really written in the config file before the restart (the test reads the file's own
  value after a real restart with the mod off and halves it in memory: a test may not write settings), and the setting
  changed in the config window after unticking the mod in single player.*
- [x] **T40 Transplant planted before the mod is turned off:** with the mod on set GrowTimeMultiplier = 0.1, wait a
  second, plant a raspberry transplant and stay near it. Untick Cultivator Replant in the MC Mods panel, then set
  GrowTimeMultiplier = 1, wait 15 s and `skiptime 2000`. Expected: it stays a young plant (the setting changed while
  the mod is off reaches the transplant already in the ground at once); after `skiptime 20000` it grows.
  *Automated: `replant.toggle`, `replant.mp.allowed`.*
- [ ] **T32 Clean log:** after every other single-player item, the log has no error or warning from Cultivator
  Replant (`./tools/Watch-Log.ps1 -Mine`), apart from the warnings T26 asked for.
  *Partly automated (`replant.log`, `probe.relog.MC.Farming.Cultivator.Replant`,
  `probe.quit.MC.Farming.Cultivator.Replant`, `probe.disabled.MC.Farming.Cultivator.Replant`); by hand: The log of a
  hands-on play session (the tests only see what the automated run did).*

### Cross-mod (MC)

- [x] **X01 Forge Idol Upgrades:** with it on, at a Forge of Potential with a cultivator, a Stone axe and idols.
  Expected: no cultivator in the list; refining the axe follows Forge Idol Upgrades' rules; its log has no "Another
  mod may take over refinement" or "Another mod changes crafting or the Forge of Potential" warning naming this mod.
  At the Forge, upgrading a cultivator from level 3 to 4 still works.
  *Automated: `replant.x.idol`, `replant.x.idol-refine`.*
- [x] **X02 Crafting Search and Sort:** at a Forge of level 4 or more, Upgrade tab, level 3 cultivator: search
  `black metal`. Expected: the Cultivator is listed. Search `bronze`: it is not. With a level 1 cultivator: `bronze`
  lists it, `black metal` does not.
  *Automated: `replant.x.search`.*
- [ ] **X03 Encyclopedia:** after having a transplant, open the Encyclopedia. Expected: the transplant is listed
  (Materials) with its name, sprout icon and description; the transplant young plants are listed among the
  cultivator's pieces; the Cultivator page lists levels 4 to 7 with their materials and Forge levels 4 to 7, and no
  level from 2 to 7 shows a Wooden Battle Idol at an upgrade station; Black metal lists the Cultivator under "used
  in". Known: the page still has the line "Beyond quality 7: at an upgrade station only", and the Wooden Battle
  Idol's page still lists the Cultivator under "used in".
  *Partly automated (`replant.x.encyclopedia`); by hand: In the Encyclopedia window itself: the transplant's row in
  the Materials list with its sprout icon, and the young transplants in the list among the cultivator's pieces (the
  test reads the pages' text, not the window, its lists or its icons).*
- [x] **X04 Loot Pickup Filter:** mode "Only selected" with an empty list. Dig up a ripe raspberry bush. Expected: the
  raspberries are picked up (a hand harvest) and the transplant goes to the inventory.
  *Automated: `replant.x.lootfilter`.*
- [x] **X05 Spyglass:** with the cultivator in hand the Replant hint shows; switch to the spyglass and raise it: the
  spyglass view as usual, no hint; back to the cultivator: the hint is back, E digs.
  *Automated: `replant.x.spyglass`, `replant.x.spyglass-view`.*

### Other mods

- [ ] **E01 PlantEverything:** with PlantEverything installed. Expected: (a) the cultivator's build menu shows its
  plants and the transplants (note whether the menu runs out of room); (b) E digs up a wild raspberry bush and gives
  a transplant; (c) its Remove button still removes its own flora; on a young transplant it does nothing; (d) a
  raspberry transplant still goes on uncultivated ground and grows; one in the Mountains waits (not destroyed);
  (e) no error in the log.
- [ ] **E02 PlantEasily:** with PlantEasily installed. Expected: (a) a grid of raspberry transplants costs one
  transplant per plant and they all grow; (b) a Yggdrasil grid aimed within 6 m of a root (E04 for an aim farther
  away): copies farther than 6 m say "Needs an Ancient Root within 6 m" and wait; Replant gives them back; (c) with
  its ReplantOnHarvest on, picking a wild raspberry bush while carrying a raspberry transplant plants nothing;
  (d) with the grid on, plant your last transplant of a kind while it is selected: note whether PlantEasily logs
  "ArgumentOutOfRangeException ... GhostGrid.GetGhostObject" (seen in the in-world tests when a test removed the last
  transplant while it was selected; a PlantEasily error, not ours); (e) no other error in the log.
  *Partly automated (`replant.e.planteasily.grid`, `replant.e.planteasily.root`); by hand: (c) with PlantEasily's
  ReplantOnHarvest turned on: picking a wild raspberry bush while carrying a raspberry transplant plants nothing (the
  setting is off in this PC's PlantEasily config and a test may not change another mod's settings).*
- [ ] **E03 Recipe changing mod (optional):** with WackysDatabase or RecipeCustomization changing the Cultivator
  recipe (for example 3 Wood for level 1, 1 Wood per level). Expected: levels 1 to 3 cost what that mod says; the
  upgrade from level 3 to 4 still asks only for 5 Black metal and 10 Linen thread (no Wood), also after that mod
  reloads its recipes while you play (the next refresh of the Upgrade tab shows the tier materials again). If that
  mod adds a second Cultivator recipe, the Upgrade tab offers no second "Upgrade to 4" row.
  *Partly automated (`replant.tiers`); by hand: The real WackysDatabase or RecipeCustomization, including its reload
  while playing.*
- [ ] **E04 PlantEasily and refused spots:** with PlantEasily installed and turned on (its grid at any size).
  (a) Select the Yggdrasil shoot transplant and aim at ground away from any Ancient Root. Expected: the aimed ghost is
  red (the grid copies around it may stay green), the hint says "Plant 2 to 6 m from an Ancient Root", and the place
  button plants nothing, no grid copy either, and uses no transplant, as in T13. (b) Aim 3 to 5 m from a root.
  Expected: the aimed ghost is green and the transplant is planted (grid copies farther than 6 m from the root wait,
  E02 b). (c) Select any transplant and aim at ground inside a no-build zone: a place where the hammer refuses every
  piece because of the place itself (a dungeon as in T24; which outdoor places are no-build zones is unverified).
  Expected: the aimed ghost is red (inside a dungeon PlantEasily may put the ghost out of sight) and the place button
  plants nothing and uses no transplant. Until 0.1.1 PlantEasily's own placement check turned the aimed ghost green
  in (a) and (c) and the transplant was planted (in-world tests of 2026-10-07, PlantEasily 2.2.2, grid 7 x 7). Known,
  not changed by this mod: PlantEasily still lets vanilla seeds and saplings be planted inside a no-build zone; and
  its placement check does not look at wards either, so inside another player's ward it may let a plant or a
  transplant be planted (read from its code, not seen in game).
  *Partly automated (`replant.rootplace`, `replant.nobuild`); by hand: That the aimed ghost really looks red among
  PlantEasily's grid copies, (b) the green ghost and the planting 3 to 5 m from a root with PlantEasily on, and (c) at
  a real dungeon.*

## 0.1.0 — multiplayer

- [x] **M01 Server rules:** host (or dedicated server) with BlackMetalLevel `Wood:1`, GrowTimeMultiplier 0.5 and
  RootSapCost 10; a friend with the mod joins. Expected: their log says "Using the server's rules: black metal level
  Wood:1, eitr level Eitr:15, flametal level FlametalNew:5, bloodgold level Gold:5, grow time x0.5, root sap cost 10,
  root range 6 m"; their Upgrade tab asks 1 Wood for level 4; a Yggdrasil transplant's hover says "Draws 10 sap".
  *Automated: `replant.mp.rules`.*
- [ ] **M02 Refused without the mod:** a friend without the mod joins a server with it (AllowPlayersWithoutMod false).
  Expected: about a second after loading in, their game shows "Incompatible version"; the server log says "Refused
  ..." and names them. A friend with the mod who turns it off while connected is refused the same way.
  *Partly automated (`probe.mp.refused`, `probe.mp.off.MC.Farming.Cultivator.Replant`, `replant.network`); by hand:
  That for a player without the mod the "Refused ..." line is written by Cultivator Replant itself and names the
  player (in the all-mods run another MC mod may refuse first), about a second after loading in.*
- [ ] **M03 Hand-off to a player without the mod:** AllowPlayersWithoutMod = true, a friend without the mod joins.
  (a) Drop a transplant next to them. Expected: they see nothing and cannot pick it up; it stays for you. (b) Plant a
  transplant. Expected: they see nothing there. (c) Let it grow (`skiptime 20000` on the host). Expected: they see and
  can pick the grown bush. (d) Drop a level 5 cultivator for them. Expected: a working cultivator for them, level 5,
  1000 durability; they cannot dig up plants with it. (e) Put a transplant and some wood in a chest; they open it (no
  transplant shown) and take the wood; you open the chest again. Expected: the transplant is gone, as the README warns.
  *Partly automated (`scenario:open-server`); by hand: Everything the player without the mod sees: (a) dropped
  transplant, (b) young transplant, (c) grown bush, (d) level 5 cultivator, (e) the chest losing its transplant.*
- [ ] **M04 Two players, one bush:** both aim at the same ripe bush; one presses E. Expected: the bush disappears on
  both screens, its berries drop once and only the digger gets a transplant. Then both press E on another bush at
  the same moment. Expected: no error; at most both get a transplant (known race, same as the hammer).
  *Partly automated (`replant.ward`); by hand: Two real players: the bush disappears on both screens, and both
  pressing E at the same moment.*
- [ ] **M05 Root owned by another player:** your friend stands at an Ancient Root first (their game runs it); you
  plant a Yggdrasil transplant there and wait for it (GrowTimeMultiplier 0.1 on the host, then `skiptime 900`).
  Expected: it grows; on both screens the next transplant's hover shows the root lower by RootSapCost.
  *Partly automated (`replant.root`); by hand: A real second player whose game runs the root: it grows and both
  screens show the root lower.*
- [ ] **M06 Growing on someone else's game:** plant a raspberry transplant, walk far away so your friend (with the mod)
  is the only player near it, `skiptime 20000` on the host. Expected: it grows while you are away; you see the bush
  when you come back.
- [ ] **M07 Wards:** your friend places and activates a ward; you are not on its list. Inside it, press E on a plant.
  Expected: the game's ward message, the ward flashes, nothing is dug. Once they add you, it works. Also: a young
  transplant your friend planted outside the ward can be dug up by you (you get the transplant); one inside their
  ward cannot while you are not on its list.
  *Partly automated (`replant.ward`, `replant.action`); by hand: A real friend's ward: the flash, and being added by
  them.*
- [x] **M08 Pending rules:** join a server with the mod and watch the first second (hard to time; or watch the log).
  Expected: until the server's rules arrive, no transplants in the build menu, the cultivator stays at vanilla levels,
  E does nothing and no young transplant grows; then everything works.
  *Automated: `replant.mp.pending`.*
- [ ] **M09 Dedicated server:** start a dedicated server with the mod. Expected: it loads without errors (no graphics
  device: the transplant items get the plain produce icon on the server, which players do not see); players with the
  mod can dig, plant, grow and upgrade; dropped transplants and young transplants are still there after a server
  restart.
  *Partly automated (`replant.mp.server`, `replant.relog.world`); by hand: A real dedicated server restart: dropped
  and planted transplants still there afterwards on the server's own world (the multiplayer run cannot restart its
  server inside a scenario).*
- [ ] **M10 Hand-off to a player with the mod turned off:** AllowPlayersWithoutMod = true on the host; a friend who
  has the mod with Enabled = false joins. Expected: they stay connected; the host log warns that they play without
  the server's rules (not that they lose transplants). (a) Drop a transplant next to them: they see it and can pick
  it up and keep it. (b) On the host set RootRange = 20, plant a Yggdrasil transplant about 15 m from a root, then
  set RootRange back to 6 (it now has no root in reach). Walk away so only your friend is near it, `skiptime 8000`
  on the host. Expected: it grows on their game with no root and no sap taken (accepted, see the README). (c) Without
  AllowPlayersWithoutMod (default), the same friend is refused about a second after joining.
  *Partly automated (`replant.mp.allowed`, `probe.mp.off.MC.Farming.Cultivator.Replant`); by hand: (a) with a
  transplant really dropped by the host for that friend (the test item was spawned by the same game); (b) set up by
  the host planting 15 m from a root and changing RootRange.*
- [x] **M11 Own grow time never used on a server:** on your game set GrowTimeMultiplier = 0.1 (your own setting;
  the host keeps 1). The host plants a raspberry transplant where you will load in (for example next to your bed on
  the host's world), runs `skiptime 3600` (1 h: due with 0.1, not with 1) and walks far away. Join. Expected: your log
  says "Using the server's rules ... grow time x1"; the transplant stays young while you stand next to it for a
  minute. `skiptime 20000` on the host: it grows. Put your setting back.
  *Automated: `replant.mp.owngrow`.*
- [ ] **M12 One root, several transplants, another root owner:** your friend stands at a full Ancient Root first
  (their game runs it). You plant three Yggdrasil transplants near it (GrowTimeMultiplier 0.1 on the host), stay
  near them, and `skiptime 900` on the host. Expected: only two grow (root 50: 50 - 20 - 20 = 10, not enough for a
  third); the third waits and its hover says "waiting for it to refill"; the root's level on both screens drops by
  40. Known limit: transplants of two different players at one root may, rarely, both grow on one drain.
  *Partly automated (`replant.root`); by hand: A real friend whose game runs the root: only two grow and the root
  drops by 40 on both screens.*
- [ ] **M13 Dropped transplant on a host without the mod:** a friend without the mod hosts; you join with the mod.
  Expected: the MC Mods panel shows the mod inactive because the server does not have it (no Replant, no
  transplants in the build menu). Drop a transplant next to the
  host player. Expected: it disappears within a few seconds, and the host's log says "Destroyed invalid prefab ZDO"
  (as the README warns).
  *Partly automated (`probe.mp.baseline`, `replant.mp.noserver`); by hand: A friend without the mod hosting: their
  game deleting a dropped transplant within a few seconds and logging 'Destroyed invalid prefab ZDO' (a dedicated
  server holds no objects and never does that).*
- [ ] **M14 Own grow time of a player with the mod turned off:** AllowPlayersWithoutMod = true on the host, whose
  GrowTimeMultiplier stays 1. A friend with the mod sets their own GrowTimeMultiplier = 0.1, joins and unticks
  Cultivator Replant in the MC Mods panel. Plant a raspberry transplant next to the friend and walk far away so only
  the friend is near it, then `skiptime 2000` on the host. Expected: it grows on the friend's game within about 10 s
  (their own setting: 0.1 = 30 min for a bush; the server's 1 would need 5 h), as the README says for players with the
  mod turned off.
  *Partly automated (`replant.mp.allowed`, `replant.mp.offgrow`); by hand: Two real players: the host plants it and
  walks away so that only the friend's game, with the mod turned off, runs the area (here one client plants it, turns
  the mod off and runs it).*

## 0.1.2 — the refusal screen names the mod

0.1.2 (2026-10-16): when a server refuses you because of this mod, the "Incompatible version" screen lists it under
"MC mods:" with the reason and what to do (framework `RefusalNotes`, `docs/modding/framework.md`, "Who needs to
install it, and multiplayer"). With Jotunn on the server and on your game, Jotunn's window shows the same lines under
"Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them. The list needs at least one MC mod
in your game that is made to run on both the server and the players (any mod a server can refuse you for, or Sleep
Through the Day); the server does not need to have that mod. A game whose MC mods are all client-side shows only
"Incompatible version". When a server refuses you for several MC mods, the screen names the first one that refused
you; once that one is fixed, the next join names the next one. When the server sends no line (a refusal that is not
about an MC mod, or a server with an older version of the MC mods), the screen lists what your own game noticed
while connected: an MC mod turned off while the server has it on, or a server copy whose version cannot play with
yours. That may not be why you were refused. Run on 2026-10-17 (dedicated test server):
`probe.mp.off.MC.Farming.Cultivator.Replant` passed, the screen named this mod both times (turned off while in,
joining with it off); `probe.refusal` passed. That run was on the code before the framework fix of the same day (new
line texts; only the server's lines when it sent any; no line left over from an earlier connection, which
`probe.mp.off` now also checks). M15 and M16 are written for the fixed code: the self-tests they name wait for a new
run, and both items still need their by-hand part.

- [ ] **M15 Refusal screen names the mod:** on a server with Cultivator Replant, turn Cultivator Replant off in Esc >
  MC Mods while you are in. Expected: you go back to the menu, and under "MC mods:" the "Incompatible version" screen
  lists only "- Cultivator Replant: it is turned off or cannot run in your game, and this server requires it. Turn it
  on in Esc > MC Mods (or F1 when a configuration manager is installed; its status there says what stops it) and join
  again." With Jotunn on the server and on your game the lines are in Jotunn's window, under "Failed Connection:";
  with Jotunn only on your game, on the vanilla panel. Join again with it off: the same. Turn it on and join: you stay
  in. Then join with a test copy of another version: build it with `dotnet build
  src/Farming/Cultivator.Replant/MC.Farming.Cultivator.Replant.csproj -p:ModNetworkVersion=99 -p:Version=0.0.9
  -p:DeployToGame=false` and copy `src/Farming/Cultivator.Replant/bin/Debug/MC.Farming.Cultivator.Replant.dll` over
  your game's copy (put the normal build back afterwards). Expected: "- Cultivator Replant: the server has version X
  and your game 0.0.9, and they cannot play together. Install version X." (X = the server's version). Last, join
  without the mod: move the folder `BepInEx/plugins/MC_Valheim/Farming/MC.Farming.Cultivator.Replant` out of your game
  (put it back afterwards), keep at least one other MC mod made to run on both the server and the players (for example
  Sleep Through the Day), and join a server where only Cultivator Replant would refuse you (your game has every other
  MC mod the server has, turned on and in the server's version). Expected: the list shows only "- Cultivator Replant:
  this server requires it (version X) and your game does not have it. Install it."
  *Partly automated (`probe.mp.off.MC.Farming.Cultivator.Replant`, `probe.refusal`); by hand: The screen as seen (in
  Jotunn's window when Jotunn runs on both sides, on the vanilla panel otherwise), the line for the test copy of
  another version (it names both versions) and the line for a game without the mod.*
- [ ] **M16 Only the mod that refused is listed:** a server with Cultivator Replant set to AllowPlayersWithoutMod =
  true and another MC mod that refuses players (for example Swim Dive, default settings). Turn both off in your game
  and join. Expected: you are refused by the other mod, and under "MC mods:" the screen lists only that mod, not
  Cultivator Replant (the server lets you in for Cultivator Replant, so your game's own note about it is not shown
  when the server sent its reason).
  *Partly automated (`probe.refusal`); by hand: The screen with a real server, where your game also had its own note
  about Cultivator Replant.*
