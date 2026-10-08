# Forge Idol Upgrades — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). Smoke test (`./tools/Test-Smoke.ps1`) passed 2026-09-30: loads, patches cleanly, JitCheck clean (494
methods, 0 failures). Automated in-world self-tests (`./tools/Test-InWorld.ps1 -Mod Forge.IdolUpgrades`) pass:
`forge.catalog`, `forge.icons`, `forge.tooltip`, `forge.popup`, `forge.idols` (includes what turning the mod off does to a running
idol upgrade), `forge.refine`, `forge.tiers` (the idol tier rule against the ForgeUpgradeChart of the idea sheet, and a
level 6 Stone axe that asks for and spends a Bronze idol at the Forge). No hands-on in-game test yet.

**Automated checks (2026-10-08):** 54 of 59 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). Find a
Forge of Potential (Mountains location), or place one with `spawn UpgradeStation` (it is big: step back so you are
not inside it). Give yourself items with `spawn <name> <amount>`; `spawn Upgrader3Weapon 1 3` gives a 2-star idol
(the last number is the quality: 1 plain, 2/3/4 = 1/2/3 stars; spawned items are marked as cheated, which does not
matter here). Names checked in the 1.0.16 game data: idols `Upgrader0Weapon` … `Upgrader7Weapon` and
`Upgrader0Armor` … `Upgrader7Armor` (Wooden, Bronze, Iron, Silver, Black Metal, Black Marble, Flametal, Bloodgold);
metals `Wood`, `Bronze`, `Iron`, `Silver`, `BlackMetal`, `BlackMarble`, `FlametalNew` (Flametal), `Gold` (Bloodgold);
trophies `TrophyDeer`, `TrophyBoar`, `TrophyNeck`, `TrophyEikthyr`, `TrophyBjorn`, `TrophyGhost`, `TrophyBlob_Frost`, `TrophyWolf`, `TrophyUlv`, `TrophyFenring`,
`TrophySGolem`, `TrophyDragonQueen`, `TrophyForestTroll` / `TrophyFrostTroll` (same "Troll Trophy" item), every other
trophy of the README table, and Kall's drop `CrownJewel` (Crown Jewel). Items and their own idol (game data, listed by
the `forge.tiers` self-test): `AxeStone` (Stone axe) and `ArmorLeatherChest` use Wooden idols (`Upgrader0Weapon`,
`Upgrader0Armor`), `AxeFlint` (Flint axe) a Bronze one (`Upgrader1Weapon`), `AxeBlackMetal` a Black Metal one. Every vanilla idol has 65% success
and 100% break on failure in the game data (so vanilla destroys the item on every failure). Do not use `nocost` in
the idol and refinement tests: it makes everything free.

## 0.1.0 — single player

- [x] **T01 Idols tab:** stand at the Forge of Potential and open it. Expected: it opens on the vanilla **UPGRADE**
  tab; an **IDOLS** tab sits on its left. Click IDOLS: the list shows each idol stack you carry below 3 stars, with its
  stars on the icon.
  *Automated: `forge.idols`, `forge.tab`.*
- [x] **T02 Upgrade plain → 1 star:** carry 3 plain `Upgrader3Weapon`, 5 `Silver`, 3 `TrophyWolf` and 2 `TrophyUlv`.
  Select the silver idol in the Idols tab. Expected: the panel shows the level, both chances, the cost and the common
  trophies; the requirements show 5 Silver, 5 Common trophies (the icon goes round the trophies each second; the
  tooltip lists them with how many you have) and the idol. Press **Upgrade idol**: after the Forge timer, one idol
  has 1 star (new stack), 2 stay plain; the silver and the 5 trophies are gone; a message says it was upgraded.
  *Automated: `forge.idols`, `forge.cost`.*
- [x] **T03 Upgrade 1 → 2 → 3 stars:** add 10 Silver + 3 `TrophyFenring`, upgrade the 1-star idol; then 15 Silver + 1
  `TrophyDragonQueen`, upgrade again. Expected: the same idol gains a star each time and stays in its slot; at 3 stars
  it leaves the Idols list.
  *Automated: `forge.idols`, `forge.steps`.*
- [x] **T04 Missing cost:** select a plain idol without the metal or trophies. Expected: the row is greyed, the missing
  amounts flash red, the button is off and its tooltip says "Missing requirement". With the full cost carried, a stack
  of 2+ plain idols, a full inventory and no 1-star stack: the button tooltip says "You need more free inventory
  space".
  *Automated: `forge.idols`, `forge.missing`.*
- [x] **T05 Stars everywhere:** look at upgraded idols in your inventory, a chest, the hotbar (drag one into row 1),
  while dragging, and in the pickup message. Expected: 1, 2 or 3 gold stars at the top right of the icon; no quality
  number in the slot corner. Under the Forge requirements the stars sit lower, below the idol's name. Drop a 2-star
  idol on the ground: its hover reads "(2 stars)". Pick up a starred idol you never had (new character): the "Added"
  and "New material" popups show the starred icon, never a blank square, also when you turn the mod off right after
  the pickup (T14) or alt-tab out of the game.
  *Automated test failing: First starred-icon request while the icon copy is empty returns no icon (white square in
  the 'New material' popup) (self-test `forge.bug.empty-copy-icon`) (the box was ticked by hand).*
- [x] **T06 Tooltip:** hover plain, 1, 2 and 3-star idols. Expected: "Level: no star / 1 star / 2 stars / 3 stars
  (n of 3)", "Refinement chance: 35 / 55 / 75 / 95%", and the next upgrade's cost (or "Maximum level"); no "Quality"
  line.
  *Automated: `forge.tooltip`, `forge.tooltip.text`.*
- [x] **T07 Levels never merge:** drag a plain idol onto a 2-star idol of the same kind (and the other way). Expected:
  they swap places, they never stack together. Picking up or moving idols to a chest keeps levels in separate stacks.
  *Automated: `forge.stacks`.*
- [x] **T08 Refinement odds:** carry a Black metal axe (`AxeBlackMetal`) you know the recipe of, and one 3-star
  `Upgrader4Weapon`. In the UPGRADE tab select the axe. Expected: the idol under the requirements shows its 3 stars,
  the text says "95% chance with a 3-star idol. A failure costs 1 level.", the button reads "Attempt
  Refinement (95%)". Watch `./tools/Watch-Log.ps1 -Mine`: each attempt logs "Refinement of AxeBlackMetal to level N
  with Upgrader4Weapon at level L (C% chance, roll R): success / failed". Keep the axe at level 5 or below here: from
  level 6 it needs Flametal idols (T20).
  *Automated: `forge.refine`, `forge.odds`.*
- [x] **T09 Failure costs a level:** with an axe at level 3 or more, refine with plain idols (35%) until one fails
  (the log line says "failed, down to level N"). Expected: the axe loses 1 level (default LevelsLost), one idol is
  gone, the message says "Refining of ... failed, downgraded to level N". Nothing is destroyed, no material comes back.
  The text under the recipe says "A failure costs 1 level." (at level 1: "A failure costs only the idol.", and a
  failure leaves it at level 1).
  *Automated: `forge.refine`, `forge.failure`.*
- [x] **T10 Success like vanilla:** on a success, the axe gains one level, full durability, you as its crafter, the
  current world level; if it was equipped it is taken off (vanilla does the same). Mod data on it stays (C07).
  *Automated: `forge.refine`, `forge.success`.*
- [x] **T20 Higher idols at high levels:** `spawn AxeStone 1 5` (a level 5 Stone axe), 2 plain `Upgrader0Weapon`.
  Expected: the UPGRADE tab asks for the Wooden Battle Idol; refine it to level 6 (retry on a failure). At level 6 the
  requirement becomes the **Bronze** Battle Idol, the text adds "From level 6 this item needs Bronze idols." (with
  "You carry none." when you have no Bronze idol), the row is greyed and the button is off although you still carry
  a Wooden idol. `spawn Upgrader1Weapon`: the button lights up; a refinement spends the Bronze idol, the Wooden idol
  stays, and the log line names `Upgrader1Weapon` "(own idol tier 0, raised by item level)". Then check:
  `spawn ArmorLeatherChest 1 6` asks for the Bronze **Protection** Idol (`Upgrader1Armor`); `spawn AxeFlint 1 4` asks
  for its own Bronze idol, and at level 6 for an Iron one; a level 4 Stone axe asks for the Wooden idol again.
  *Automated: `forge.tiers`, `forge.tiers.stone`.*
- [x] **T21 Tier settings live:** while the game runs set `LevelsOnOwnIdol = 3`: a level 4 Stone axe now asks for the
  Bronze idol ("From level 4 ..."). Set `LevelsPerIdolTier = 1`: level 5 asks for Iron, level 6 for Silver. Set
  `HigherIdolAtHighLevels = false`: every level asks for the Wooden idol again. Put the defaults back, then
  `spawn AxeStone 1 40`: it asks for the **Bloodgold** Battle Idol (`Upgrader7Weapon`) with "From level 30 this item
  needs Bloodgold idols."
  *Automated: `forge.tiers`, `forge.tiers.live`, `forge.rules.wiring`, `forge.mp.tiers`.*
- [x] **T19 Failure settings:** set `LevelsLost = 2`: a failure costs 2 levels (never below 1). Set
  `OnFailure = Destroy`: the text says "A failure destroys the item."; a failure destroys it with the vanilla message
  and gives back part of its materials; the Forge again asks for free slots like vanilla.
  *Automated: `forge.refine`, `forge.failure.rules`, `forge.rules.wiring`, `forge.mp.rules`.*
- [x] **T11 Choose the idol:** carry a plain and a 2-star idol of the needed kind. Expected: the Forge picks the 2-star
  one (75%). Click the idol under the requirements: it switches to the plain one (35%), click again: back. Close the
  window, set `IdolChoice = Lowest`, reopen the Forge: the plain one is picked first. (A clicked pick lasts until the
  window closes and wins over the setting.) The click needs the mouse; with a controller only the setting applies.
  *Partly automated (`forge.refine`, `forge.choice`); by hand: With a real controller: check there is no way to change
  the idol at the Forge and that only the IdolChoice setting applies.*
- [x] **T12 No free slots needed:** with OnFailure = LoseLevels, fill your inventory to one free slot (or none) and
  refine an item. Expected: the refinement starts (vanilla asked for free slots for a broken item's refund).
  *Automated: `forge.space`.*
- [x] **T13 Settings live:** change `ChanceLevel2` to 60 and `Level1Trophies` to 2 while the game runs. Expected: the
  Forge button and tooltips show 60%, the Idols tab asks 2 trophies, with no restart. Put a typo in a trophy list:
  the log says which name is ignored.
  *Automated: `forge.rules.live`, `forge.rules.wiring`, `forge.mp.rules`.*
- [x] **T14 Live toggle:** Esc → MC Mods → untick Forge Idol Upgrades, then go back to the Forge. Expected: no IDOLS
  tab, the refinement text and odds are vanilla again ("Maximum safe quality. Refinement may break your item."), idol
  icons lose their stars and show a quality number (1-4); starred idols still do not merge with plain ones until you
  restart. Tick it again: everything comes back at once.
  *Automated: `forge.idols`, `forge.toggle`.*
- [x] **T18 Turned off at the Forge:** at the Forge, on the IDOLS tab, press Upgrade idol, then during the timer open
  ConfigurationManager (F1) and untick `Enabled`. Expected: the timer stops, nothing is used, the list switches to the
  vanilla Upgrade rows (no idol row left); no idol disappears.
  *Automated: `forge.idols`, `forge.off.upgrade`.*
- [x] **T15 Turned on mid-game:** start the game with `Enabled = false`, open your inventory, then tick the mod on.
  Expected: stars appear on idol icons in the inventory and on the hotbar at once, no restart.
  *Partly automated (`forge.toggle`); by hand: Start the game with Enabled = false in the config file (the mod never
  active since launch), open the inventory, tick the mod on: stars must appear at once on the inventory and the
  hotbar.*
- [x] **T16 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Crafting.Forge.IdolUpgrades.cfg`,
  start the game. Expected: the Forge is vanilla (65% success, failure destroys the item), no IDOLS tab.
  *Partly automated (`forge.toggle`); by hand: Set Enabled = false in the config file, restart the game and check the
  Forge: vanilla text and odds, no IDOLS tab.*
- [x] **T17 Clean log:** play through T01-T12 while `./tools/Watch-Log.ps1 -Mine` runs. Expected: no error from Forge
  Idol Upgrades, and no "Unknown item name(s)" warning with the default settings.
  *Automated test failing: False 'Unknown item name(s)' warning listing every default item, at the main menu
  (self-test `forge.bug.menu-warning`) (the box was ticked by hand).*
- [!] **T22 Starred icon while the game window is minimized:** the first time a starred icon is needed while the game
  cannot copy the icon (window minimized), for example a never-seen 2-star idol taken by auto pickup. It cannot be
  forced by hand: the self-test `forge.icons.empty-copy` forces it. Expected: the plain icon shows until the starred
  one can be made about a second later; the "New material" popup shows the plain icon, never a white square.
  **FAILED:** automated test: First starred-icon request while the icon copy is empty returns no icon (white square in
  the 'New material' popup) (self-test `forge.bug.empty-copy-icon`)
- [x] **T23 Turned off during a refinement:** at the Forge, on the UPGRADE tab, press Attempt Refinement, then during
  the timer open ConfigurationManager (F1) and untick `Enabled`. Expected: the timer stops, the panel shows the vanilla
  refinement text, the item keeps its level and durability and the idol is not used (no vanilla roll follows).
  *Automated: `forge.off.refine`.*
- [!] **T24 Unknown metal name:** set `Material` of "Tier 3 - Silver idols" to a name the game does not know
  (`Silverr`), carry a plain `Upgrader3Weapon`, 5 `Silver` and 5 `TrophyWolf`, open the Idols tab. Expected: the
  upgrade stays blocked (it never becomes free), the panel says in red why (no metal is set for this tier), the
  idol's tooltip does not show the cost as trophies only, and the log warning names `Silverr`. The same with an empty
  `Material`.
  **FAILED:** automated test: Unknown or empty tier Material blocks the idol upgrade with no visible reason (self-test
  `forge.bug.bad-material-silent`)
- [!] **T25 Cost of 0 in the tooltip:** set `Level1Trophies = 0` and hover a plain idol. Expected: the cost line reads
  "Upgrade at a Forge of Potential, Idols tab: 5 Silver", with no comma after it. With `Level1Material = 0` instead:
  only the trophies. With both at 0: no "Idols tab:" line with nothing after it.
  **FAILED:** automated test: Idol tooltip cost line ends with a dangling comma, or is empty, when a cost is 0
  (self-test `forge.bug.tooltip-zero-cost`)
- [x] **T26 Tab switched during a timer:** on the IDOLS tab press Upgrade idol and click UPGRADE while the timer runs;
  then on the UPGRADE tab press Attempt Refinement and click IDOLS while the timer runs. Expected: each started job
  still ends, once: the idol gains its star and its cost is used once; the refinement rolls once and uses one idol;
  nothing else is touched.
  *Automated: `forge.timer.tabs`.*
- [x] **T27 Something leaves the inventory during a timer:** press Upgrade idol, then drop the metal (or the idol
  stack) before the timer ends; press Attempt Refinement, then drop the idol (or the item) before the timer ends.
  Expected: nothing is upgraded or refined and nothing else is used; for the idol upgrade a message says the
  requirement is missing.
  *Automated: `forge.timer.loss`.*
- [!] **T28 Turned on with the Forge open:** with the mod off, open the Forge window, then tick `Enabled` in
  ConfigurationManager (F1) without closing the window. Expected: the refinement odds and text are back at once, and
  so is the IDOLS tab (no need to close and reopen the Forge).
  **FAILED:** automated test: Turned on while the Forge window is open: the IDOLS tab does not come back until the
  panel is rebuilt (self-test `forge.bug.tab-after-on`)
- [!] **T29 No false "Unknown item name(s)" warning:** with the default settings, start the game while
  `./tools/Watch-Log.ps1 -Mine` runs, load a world, then log out to the main menu. Expected: the log never shows an
  "Unknown item name(s) in the idol settings, ignored: ..." warning that lists items the game has (`Upgrader0Weapon`,
  `Wood`, `TrophyDeer`, ...), neither at the start nor back at the main menu. The self-test `forge.bug.menu-warning`
  checks the part from the start of the game to the end of the tests; the return to the main menu needs a look at the
  log.
  **FAILED:** automated test: False 'Unknown item name(s)' warning listing every default item, at the main menu
  (self-test `forge.bug.menu-warning`)

## 0.1.0 — multiplayer (needs a second player)

- [x] **M01 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod"; the Forge is vanilla, no IDOLS tab.
  *Automated: `probe.mp.baseline`, `forge.mp.vanilla-server`.*
- [x] **M04 Player without the mod refused:** a friend without the mod joins a server (or host) with it. Expected:
  about a second after joining their game goes back to the menu with "Incompatible version"; the server log names
  them and says why ("does not have the mod"). The same for a friend with the older build (network version 1, commit
  `a74d917`: "has another version of the mod") and for a friend who unticks the mod in the MC Mods panel while
  playing (refused about a second later: "has the mod turned off"). With `AllowPlayersWithoutMod = true` on the
  server they can play (log warning).
  *Partly automated (`probe.mp.refused`, `probe.mp.off.MC.Crafting.Forge.IdolUpgrades`, `scenario:open-server`,
  `forge.mp.dedicated`, `forge.catalog`); by hand: A friend with the older build (network version 1) must be refused
  with 'has another version of the mod'. Read once that this mod's own server log line names the player and says 'does
  not have the mod' (in the automated run another MC mod may be the one that refuses first) and that the refusal comes
  about a second after joining.*
- [x] **M05 Server rules for everyone:** on the host set `ChanceLevel0 = 50` and `OnFailure = Destroy`; the friend
  (own settings left at default) opens a Forge. Expected: their panel shows 50% and "A failure destroys the item.";
  their log says "Using the server's Forge rules". Change the host setting while they play: it follows. Their own
  IdolChoice still applies.
  *Automated: `forge.mp.rules`.*
- [x] **M07 Server idol tiers for everyone:** on the host set `LevelsOnOwnIdol = 3`; the friend (own settings left
  at default) selects a level 4 Stone axe at a Forge. Expected: their Forge asks for the Bronze idol; their log's
  "Using the server's Forge rules" line says "idol one tier higher from level 4, then every 4 level(s)". Back to 5 on
  the host: a level 4 axe asks for the Wooden idol again, without rejoining.
  *Automated: `forge.mp.tiers`.*
- [x] **M06 Dedicated server:** install on a dedicated server and on both players. Expected: M04 and M05 behaviour, the
  server log shows the mod loaded, no error.
  *Partly automated (`forge.mp.dedicated`, `forge.mp.rules`, `probe.mp.refused`,
  `probe.mp.off.MC.Crafting.Forge.IdolUpgrades`); by hand: Two real players on the dedicated server (the run has one
  client), and M04's older-build case.*
- [x] **M02 Hand-off to a player without the mod:** (AllowPlayersWithoutMod on) give a 2-star idol to a friend without the mod. Expected: they see an
  idol without stars or quality number (a dropped one reads "[3]" in its ground hover) and their Forge refuses it on
  its own ("missing requirement"); giving it back to you, it still has 2 stars.
  *Partly automated (`forge.mp.handoff`, `forge.mp.items`, `forge.tooltip`); by hand: A real friend without the mod on
  a server with AllowPlayersWithoutMod on: what their screen and their Forge show, and handing the idol back.*
- [x] **M03 Hand-off through a chest:** put starred idols in a chest, relog, take them out. Expected: levels kept,
  levels never merged.
  *Partly automated (`forge.stacks`, `forge.mp.items`); by hand: A real relog (log out, log in again), then take the
  idols out of the chest.*

## 0.1.0 — compatibility (optional, needs another mod)

- [x] **C01 Crafting Search and Sort:** with MC Crafting Search and Sort on, open the Idols tab, type part of an idol
  name in its filter, change the sort. Expected: the idol rows filter and sort like any other rows; switching tabs
  keeps working.
  *Automated: `forge.compat.search`.*
- [x] **C02 Crossbow Stays Loaded:** refine a loaded crossbow (e.g. `CrossbowArbalest` with its idol). Expected: on
  success it needs a reload (full durability counts as a repair), same as vanilla; on failure it stays loaded.
  *Partly automated (`forge.compat.crossbow`); by hand: Nothing to look at, but the item's words are only true at
  level 1: at level 2 or more a failure re-makes the crossbow and it needs a reload (item C09). The owner should
  decide whether C02 means level 1 only before it is ticked from tests.*
- [x] **C03 One Click Repair All:** at the Forge, press repair while the Idols tab is open. Expected: repairs happen and
  the Idols tab stays selected.
  *Automated: `forge.compat.repair`.*
- [ ] **C04 Another Forge mod:** with ReforgedPotential (or another Forge mod) installed, open the Forge. Expected: a
  warning in the log names that mod; a second warning says the higher idols at high levels are off on this game; a
  level 6 Stone axe asks for its own Wooden idol (no Bronze); a refinement rolls only once (theirs); an idol upgrade
  in the IDOLS tab works and no idol disappears.
  *Partly automated (`forge.compat.other-forge`); by hand: The same with the real ReforgedPotential (or another real
  Forge mod) installed: its roll and its UI next to this mod.*
- [x] **C05 Sort Chest:** put plain, 1-star and 2-star idols of one kind in a chest and sort it (stack merging on).
  Expected: each level stays in its own stack, stars still shown.
  *Automated: `forge.compat.sort-chest`.*
- [x] **C08 Crafting Search and Sort at the Forge:** with MC Crafting Search and Sort on, carry a level 6 Stone axe
  and open the Forge's UPGRADE tab. Expected: the axe's requirement shows the Bronze idol; typing "wooden" in the
  filter keeps the axe, typing "bronze" hides it (known limitation: the search reads the idol the game asks for).
  *Automated: `forge.compat.search-forge`.*
- [ ] **C07 EpicLoot:** refine a magic item (success, then a failure). Expected: its magic effects stay after both.
  *Partly automated (`forge.refine`, `forge.failure`, `forge.success`); by hand: With EpicLoot installed: refine a
  magic item (a success, then a failure) and check its magic effects are still there.*
- [x] **C06 Loot Pickup Filter:** mark an idol for the filter. Expected: its mark and the stars both show (the mark may
  cover one star), and one filter entry covers every level of that idol.
  *Automated: `forge.compat.loot-filter`.*
- [x] **C09 Crossbow Stays Loaded, failure at level 2 or more:** refine a loaded crossbow that is at level 2 or more
  until it fails (OnFailure = LoseLevels). Expected: it comes back one level lower at full durability, like after a
  success, so it needs a reload, also when it was damaged (only a failure at level 1 leaves the crossbow untouched and
  loaded, see C02). With OnFailure = Destroy the crossbow is gone.
  *Automated: `forge.compat.crossbow-down`.*
