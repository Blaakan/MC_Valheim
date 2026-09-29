# Forge Idol Upgrades — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the MC
Mods panel). Smoke test (`./tools/Test-Smoke.ps1`) passed 2026-09-29: loads, patches cleanly, JitCheck clean (437
methods, 0 failures). Automated in-world self-tests (`./tools/Test-InWorld.ps1 -Mod Forge.IdolUpgrades`) pass:
`forge.catalog`, `forge.icons`, `forge.tooltip`, `forge.popup`, `forge.idols` (includes what turning the mod off does to a running
idol upgrade), `forge.refine`. No hands-on in-game test yet.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). Find a
Forge of Potential (Mountains location), or place one with `spawn UpgradeStation` (it is big: step back so you are
not inside it). Give yourself items with `spawn <name> <amount>`; `spawn Upgrader3Weapon 1 3` gives a 2-star idol
(the last number is the quality: 1 plain, 2/3/4 = 1/2/3 stars; spawned items are marked as cheated, which does not
matter here). Names checked in the 1.0.16 game data: idols `Upgrader0Weapon` … `Upgrader7Weapon` and
`Upgrader0Armor` … `Upgrader7Armor` (Wooden, Bronze, Iron, Silver, Black Metal, Black Marble, Flametal, Bloodgold);
metals `Wood`, `Bronze`, `Iron`, `Silver`, `BlackMetal`, `BlackMarble`, `FlametalNew` (Flametal), `Gold` (Bloodgold);
trophies `TrophyDeer`, `TrophyBoar`, `TrophyNeck`, `TrophyEikthyr`, `TrophyBjorn`, `TrophyGhost`, `TrophyBlob_Frost`, `TrophyWolf`, `TrophyUlv`, `TrophyFenring`,
`TrophySGolem`, `TrophyDragonQueen`, `TrophyForestTroll` / `TrophyFrostTroll` (same "Troll Trophy" item), every other
trophy of the README table, and Kall's drop `CrownJewel` (Crown Jewel). Every vanilla idol has 65% success
and 100% break on failure in the game data (so vanilla destroys the item on every failure). Do not use `nocost` in
the idol and refinement tests: it makes everything free.

## 0.1.0 — single player

- [ ] **T01 Idols tab:** stand at the Forge of Potential and open it. Expected: it opens on the vanilla **UPGRADE**
  tab; an **IDOLS** tab sits on its left. Click IDOLS: the list shows each idol stack you carry below 3 stars, with its
  stars on the icon.
- [ ] **T02 Upgrade plain → 1 star:** carry 3 plain `Upgrader3Weapon`, 5 `Silver`, 3 `TrophyWolf` and 2 `TrophyUlv`.
  Select the silver idol in the Idols tab. Expected: the panel shows the level, both chances, the cost and the common
  trophies; the requirements show 5 Silver, 5 Common trophies (the icon goes round the trophies each second; the
  tooltip lists them with how many you have) and the idol. Press **Upgrade idol**: after the Forge timer, one idol
  has 1 star (new stack), 2 stay plain; the silver and the 5 trophies are gone; a message says it was upgraded.
- [ ] **T03 Upgrade 1 → 2 → 3 stars:** add 10 Silver + 3 `TrophyFenring`, upgrade the 1-star idol; then 15 Silver + 1
  `TrophyDragonQueen`, upgrade again. Expected: the same idol gains a star each time and stays in its slot; at 3 stars
  it leaves the Idols list.
- [ ] **T04 Missing cost:** select a plain idol without the metal or trophies. Expected: the row is greyed, the missing
  amounts flash red, the button is off and its tooltip says "Missing requirement". With the full cost carried, a stack
  of 2+ plain idols, a full inventory and no 1-star stack: the button tooltip says "You need more free inventory
  space".
- [ ] **T05 Stars everywhere:** look at upgraded idols in your inventory, a chest, the hotbar (drag one into row 1),
  while dragging, and in the pickup message. Expected: 1, 2 or 3 gold stars at the top right of the icon; no quality
  number in the slot corner. Under the Forge requirements the stars sit lower, below the idol's name. Drop a 2-star
  idol on the ground: its hover reads "(2 stars)". Pick up a starred idol you never had (new character): the "Added"
  and "New material" popups show the starred icon, never a blank square, also when you turn the mod off right after
  the pickup (T14) or alt-tab out of the game.
- [ ] **T06 Tooltip:** hover plain, 1, 2 and 3-star idols. Expected: "Level: no star / 1 star / 2 stars / 3 stars
  (n of 3)", "Refinement chance: 35 / 55 / 75 / 95%", and the next upgrade's cost (or "Maximum level"); no "Quality"
  line.
- [ ] **T07 Levels never merge:** drag a plain idol onto a 2-star idol of the same kind (and the other way). Expected:
  they swap places, they never stack together. Picking up or moving idols to a chest keeps levels in separate stacks.
- [ ] **T08 Refinement odds:** carry a Black metal axe (`AxeBlackMetal`) you know the recipe of, and one 3-star
  `Upgrader4Weapon`. In the UPGRADE tab select the axe. Expected: the idol under the requirements shows its 3 stars,
  the text says "95% chance with a 3-star idol. A failure costs 1 level.", the button reads "Attempt
  Refinement (95%)". Watch `./tools/Watch-Log.ps1 -Mine`: each attempt logs "Refinement of AxeBlackMetal to level N
  with a level L idol (C% chance, roll R): success / failed".
- [ ] **T09 Failure costs a level:** with an axe at level 3 or more, refine with plain idols (35%) until one fails
  (the log line says "failed, down to level N"). Expected: the axe loses 1 level (default LevelsLost), one idol is
  gone, the message says "Refining of ... failed, downgraded to level N". Nothing is destroyed, no material comes back.
  The text under the recipe says "A failure costs 1 level." (at level 1: "A failure costs only the idol.", and a
  failure leaves it at level 1).
- [ ] **T10 Success like vanilla:** on a success, the axe gains one level, full durability, you as its crafter, the
  current world level; if it was equipped it is taken off (vanilla does the same). Mod data on it stays (C07).
- [ ] **T19 Failure settings:** set `LevelsLost = 2`: a failure costs 2 levels (never below 1). Set
  `OnFailure = Destroy`: the text says "A failure destroys the item."; a failure destroys it with the vanilla message
  and gives back part of its materials; the Forge again asks for free slots like vanilla.
- [ ] **T11 Choose the idol:** carry a plain and a 2-star idol of the needed kind. Expected: the Forge picks the 2-star
  one (75%). Click the idol under the requirements: it switches to the plain one (35%), click again: back. Close the
  window, set `IdolChoice = Lowest`, reopen the Forge: the plain one is picked first. (A clicked pick lasts until the
  window closes and wins over the setting.) The click needs the mouse; with a controller only the setting applies.
- [ ] **T12 No free slots needed:** with OnFailure = LoseLevels, fill your inventory to one free slot (or none) and
  refine an item. Expected: the refinement starts (vanilla asked for free slots for a broken item's refund).
- [ ] **T13 Settings live:** change `ChanceLevel2` to 60 and `Level1Trophies` to 2 while the game runs. Expected: the
  Forge button and tooltips show 60%, the Idols tab asks 2 trophies, with no restart. Put a typo in a trophy list:
  the log says which name is ignored.
- [ ] **T14 Live toggle:** Esc → MC Mods → untick Forge Idol Upgrades, then go back to the Forge. Expected: no IDOLS
  tab, the refinement text and odds are vanilla again ("Maximum safe quality. Refinement may break your item."), idol
  icons lose their stars and show a quality number (1-4); starred idols still do not merge with plain ones until you
  restart. Tick it again: everything comes back at once.
- [ ] **T18 Turned off at the Forge:** at the Forge, on the IDOLS tab, press Upgrade idol, then during the timer open
  ConfigurationManager (F1) and untick `Enabled`. Expected: the timer stops, nothing is used, the list switches to the
  vanilla Upgrade rows (no idol row left); no idol disappears.
- [ ] **T15 Turned on mid-game:** start the game with `Enabled = false`, open your inventory, then tick the mod on.
  Expected: stars appear on idol icons in the inventory and on the hotbar at once, no restart.
- [ ] **T16 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Crafting.Forge.IdolUpgrades.cfg`,
  start the game. Expected: the Forge is vanilla (65% success, failure destroys the item), no IDOLS tab.
- [ ] **T17 Clean log:** play through T01-T12 while `./tools/Watch-Log.ps1 -Mine` runs. Expected: no error from Forge
  Idol Upgrades, and no "Unknown item name(s)" warning with the default settings.

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod"; the Forge is vanilla, no IDOLS tab.
- [ ] **M04 Player without the mod refused:** a friend without the mod joins a server (or host) with it. Expected:
  about a second after joining their game goes back to the menu with "Incompatible version"; the server log names
  them. With `AllowPlayersWithoutMod = true` on the server they can play (log warning).
- [ ] **M05 Server rules for everyone:** on the host set `ChanceLevel0 = 50` and `OnFailure = Destroy`; the friend
  (own settings left at default) opens a Forge. Expected: their panel shows 50% and "A failure destroys the item.";
  their log says "Using the server's Forge rules". Change the host setting while they play: it follows. Their own
  IdolChoice still applies.
- [ ] **M06 Dedicated server:** install on a dedicated server and on both players. Expected: M04 and M05 behaviour, the
  server log shows the mod loaded, no error.
- [ ] **M02 Hand-off to a player without the mod:** (AllowPlayersWithoutMod on) give a 2-star idol to a friend without the mod. Expected: they see an
  idol without stars or quality number (a dropped one reads "[3]" in its ground hover) and their Forge refuses it on
  its own ("missing requirement"); giving it back to you, it still has 2 stars.
- [ ] **M03 Hand-off through a chest:** put starred idols in a chest, relog, take them out. Expected: levels kept,
  levels never merged.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 Crafting Search and Sort:** with MC Crafting Search and Sort on, open the Idols tab, type part of an idol
  name in its filter, change the sort. Expected: the idol rows filter and sort like any other rows; switching tabs
  keeps working.
- [ ] **C02 Crossbow Stays Loaded:** refine a loaded crossbow (e.g. `CrossbowArbalest` with its idol). Expected: on
  success it needs a reload (full durability counts as a repair), same as vanilla; on failure it stays loaded.
- [ ] **C03 One Click Repair All:** at the Forge, press repair while the Idols tab is open. Expected: repairs happen and
  the Idols tab stays selected.
- [ ] **C04 Another Forge mod:** with ReforgedPotential (or another Forge mod) installed, open the Forge. Expected: a
  warning in the log names that mod; a refinement rolls only once (theirs); an idol upgrade in the IDOLS tab works and
  no idol disappears.
- [ ] **C05 Sort Chest:** put plain, 1-star and 2-star idols of one kind in a chest and sort it (stack merging on).
  Expected: each level stays in its own stack, stars still shown.
- [ ] **C07 EpicLoot:** refine a magic item (success, then a failure). Expected: its magic effects stay after both.
- [ ] **C06 Loot Pickup Filter:** mark an idol for the filter. Expected: its mark and the stars both show (the mark may
  cover one star), and one filter entry covers every level of that idol.
