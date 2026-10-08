# One Click Repair All — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-28 (loads, patches, JitCheck clean). No in-game test yet.

**Automated checks (2026-10-08):** 20 of 20 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** press F5 for the console → `devcommands`. Spawn gear with `spawn <Name>` (Tab autocompletes):
- workbench gear (level 1 workbench is enough): `AxeFlint`, `Club`, `KnifeFlint`, `ShieldWood`, `ArmorRagsChest`, `Hoe`;
- forge gear (level 1 forge is enough): `SwordBronze`, `AxeBronze`, `ShieldBronzeBuckler`, `ArmorBronzeChest`;
- higher-level gear for T03: `ArmorLeatherChest` (needs workbench level 2), `SwordIron` (needs forge level 2).

Items spawn at full durability: wear them a little first (hit a tree or rock with the weapons and tools, block a few
hits with the shield, let a Greyling or Boar hit your armor: `spawn Greyling`). A few hits each is enough; the
durability bar under the icon only needs to drop.
Build a workbench and a forge next to each other, under a roof, **without upgrades** (no chopping block, no forge
extensions) so T03 works. Keep `./tools/Watch-Log.ps1 -Mine` open, or check `BepInEx/LogOutput.log` afterwards. If
`Debug` is in the `[Logging.Disk] LogLevels` of `BepInEx.cfg` (`./tools/Setup.ps1 -DevBepInExConfig`), every click
that repairs something while 2 or more items are damaged also logs one `Repair click: ...` line.

## 0.1.0 — single player

- [x] **T01 Repair all at the workbench:** 4 or more damaged workbench items, some equipped and some only in the bag.
  Open the workbench, click the repair button (hammer icon) once. Expected: all of them are repaired; you hear one
  repair sound; one message like "Repaired Flint axe, Club, Wood shield +1" (3 names, then "+N" for the rest); the
  repair button stops glowing.
  *Automated: `repair.bench`.*
- [x] **T02 Only this station's items:** carry damaged workbench gear AND damaged bronze (forge) gear. Repair at the
  workbench. Expected: only the workbench gear is repaired, the bronze gear stays damaged and the button stops
  glowing. Then open the forge and click once. Expected: all the bronze gear is repaired.
  *Automated: `repair.stations`.*
- [x] **T03 Station level:** carry a damaged `ArmorLeatherChest` plus 2 damaged level-1 workbench items, open the
  workbench with no upgrades, click repair. Expected: the two level-1 items are repaired; the leather tunic stays
  damaged, as in vanilla (it needs workbench level 2). Same at the forge with `SwordIron` and bronze gear.
  *Automated: `repair.level`.*
- [x] **T04 Only one damaged item:** exactly one repairable item damaged. Click repair. Expected: exactly like
  vanilla: that item is repaired, message "Repaired <item>", one sound.
  *Automated: `repair.single`.*
- [x] **T05 Nothing to repair:** no damaged item this station can repair. Expected: the button is greyed out and does
  not glow (vanilla).
  *Automated: `repair.none`.*
- [x] **T06 nocost without a station:** walk away from every station, console `nocost` (turns it on), open the
  inventory (Tab). Expected: a repair button shows next to the crafting panel; one click repairs every damaged item,
  workbench and forge gear alike; no repair sound (vanilla has none without a station); one message.
  *Automated: `repair.nocost`.*
- [x] **T07 nocost at a station:** with `nocost` still on, damage workbench AND bronze gear again, open the workbench,
  click repair once. Expected: everything is repaired, bronze included (vanilla nocost ignores the station rules).
  Type `nocost` again to turn it off, check the button in the plain inventory is gone.
  *Automated: `repair.nocost-station`.*
- [x] **T08 Crafting skill:** open the Skills tab and note the Crafting bar, repair several items in one click.
  Expected: the bar grows (about as much as clicking each item in vanilla would give).
  *Automated: `repair.skill`.*
- [x] **T09 Upgrade tab refresh:** get and damage `Club` and `ShieldWood` first, then `AxeFlint` last (vanilla
  repairs the item you got first, so the axe is fixed by one of the mod's extra presses). Open the workbench on the
  Upgrade tab: the axe shows a durability bar. Click repair once. Expected: the axe's durability bar disappears from
  the Upgrade list right away (the game hides the bar on undamaged items), no need to switch tabs.
  *Automated: `repair.upgrade-tab`.*
- [ ] **T10 Gamepad:** with a controller, open the workbench with damaged items and press the controller button shown
  on the repair button. Expected: everything repaired in one press, as with the mouse.
  *Partly automated (`repair.pad`); by hand: With a real controller, open the workbench with several damaged items and
  press the button shown on the repair button: check the hint shows the right button and that one press repairs
  everything.*
- [x] **T11 Live toggle:** Esc → MC Mods → untick One Click Repair All → back at the workbench with 3 damaged items,
  click repair. Expected: vanilla: one item per click. Tick it again, damage items, click once. Expected: all
  repaired, no restart.
  *Automated: `repair.toggle`.*
- [x] **T12 Disabled in the config file:** set `Enabled = false` in `BepInEx/config/MC.Crafting.Repair.OneClickAll.cfg`,
  restart the game. Expected: vanilla (one item per click). Set it back to `true`.
  *Partly automated (`repair.toggle`); by hand: Set Enabled = false in
  BepInEx/config/MC.Crafting.Repair.OneClickAll.cfg, restart the game and check the mod starts turned off (one item
  per click); then set it back to true.*
- [x] **T13 With Crossbow Stays Loaded:** with both mods on, load a crossbow, damage it (fire a few bolts, reload), and
  damage 2 other items that the same station repairs (the station the crossbow is crafted at; or use `nocost` from
  T06 to repair anywhere). Repair all in one click, then equip the crossbow. Expected: it is still loaded (no reload).
  *Automated: `repair.crossbow`.*
- [x] **T14 Clean log:** after a session, no errors or exceptions mentioning `One Click Repair All` or
  `MC.Crafting` in `BepInEx/LogOutput.log`.
  *Automated: `repair.clean`.*

## 0.1.0 — multiplayer (needs a second player)

- [x] **M01 Vanilla server and friend:** join a server (or host) where a friend does NOT have the mod; repair all at a
  shared workbench while the friend stands next to you. Expected: works for you exactly as in single player; the
  friend sees and hears nothing unusual, no errors on either side.
  *Partly automated (`repair.mp.bulk`, `repair.mp.vanilla-server`, `probe.mp.baseline`); by hand: A second player
  without the mod stands next to you while you repair all at a shared workbench: they see and hear nothing unusual
  (one repair sound) and no error shows on their side.*
- [x] **M02 Hand-off:** repair several items in one click, then drop them (or put them in a chest) for the friend
  without the mod. They take and equip them. Expected: full durability, normal items, no errors on either side.
  *Partly automated (`repair.handoff`, `repair.mp.handoff`); by hand: A second player without the mod picks the items
  up from the ground and from the chest and equips them: normal items at full durability, no error on their side.*

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 Repair-cost mod:** install a mod that makes repairs cost something (RepairRequiresCoins or RepairCost),
  carry enough to pay for only some of the damaged items, click repair once. Expected: items are repaired and paid one
  by one until you cannot pay; the reason shows top-left; the game never freezes; the remaining items stay damaged.
  *Partly automated (`repair.cost`); by hand: Install RepairRequiresCoins or RepairCost, carry enough to pay for only
  some of the damaged items and click repair once: the items are repaired and paid one by one, that mod's own reason
  shows top-left, the game does not freeze, the rest stay damaged.*
- [x] **C02 Repair refused part-way (simulated cost mod):** covered by the `repair.cost` self-test
  (`./tools/Test-InWorld.ps1 -Mod Repair.OneClickAll`), which stands in for a repair-cost mod, so no other mod is
  needed. With 5 damaged workbench items it lets 3 repairs through and refuses the 4th with a message in the middle of
  the screen, in two ways: the repair itself is refused (what RepairRequiresCoins and RepairCost do), and the item is
  reported as not repairable while a repair runs (what RepairRequiresMats does; the game then adds its own "No more
  item to repair"). Expected, both ways: exactly the first 3 items are repaired and the other 2 keep their durability;
  the click stops at the first refused repair (no freeze); the message is "Repaired <item>, <item>, <item>" with no
  "+N"; the refusal text shows top-left once, and never "No more item to repair"; one repair sound; the repair button
  stays usable for the 2 items left. This item needs no other mod; the same check with a real cost mod is C01.
  *Automated: `repair.cost`.*
- [x] **C03 Remaining repairs blocked without a reason (simulated):** covered by the `repair.blocked` self-test, which
  stands in for a mod that takes over the repairs or refuses them without telling the player (RhythmicRepairs repairs
  the items itself, one after the other). With 4 damaged workbench items: (a) every repair after the first does
  nothing. Expected: only the first item is repaired, with the game's own "Repaired <item>" message and no summary;
  the mod tries once more, sees that nothing changed and stops; nothing shows top-left. (b) After 2 repairs the items
  are reported as not repairable, without a message. Expected: the first 2 items are repaired and the other 2 keep
  their durability; the message is "Repaired <item>, <item>"; nothing shows top-left (the game's "No more item to
  repair" is not repeated there). This item needs no other mod (RhythmicRepairs itself is not installed for it).
  *Automated: `repair.blocked`.*
