# Encyclopedia — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also shown next to the mod in the
MC Mods panel). Committed build `1254c75` (2026-09-29), nested under the Valheim Compendium and named Encyclopedia:
Debug and Release build without warnings; smoke test (`./tools/Test-Smoke.ps1`, all MC mods) passed; automated
in-world self-tests (T00, `./tools/Test-InWorld.ps1`) passed on the same code at 2560x1440: all 6 `compendium.*` tests
(17/17 tests of the run). No hands-on in-game test yet.

**Setup:** a **new character in a new world** for T05-T08, T13, T21, T25 and T32-T34 (everything starts as "???"), and a
**mid-game character** for T09. Open the Encyclopedia: inventory (Tab) → the game's **Valheim Compendium** button
(raven) → the **Encyclopedia** tab at the dialog's top left (next to **Texts**). The Valheim Compendium reopens on the
tab you used last until you log out. The optional side button (T01, T02, T26, T30, T31, T38): set `SideButton = true`
in `BepInEx/config/MC.Exploration.Compendium.Encyclopedia.cfg` (or ConfigurationManager); it adds a book button
right after the raven, before Skills. For spawning: F5 console → `devcommands` (then `confirmcheats` if asked; cheats
mark the character as cheated for achievements), `spawn <Name>` (Tab completes), `god` keeps you alive, `killenemies`
clears the area. Use the Debug build and `./tools/Setup.ps1 -DevBepInExConfig` for the Debug log lines; keep
`./tools/Watch-Log.ps1 -Mine` open. In the Debug build, the console command `compendium_debug` prints the catalog
counts and your knowledge, and `compendium_debug <token or prefab>` (for example `compendium_debug Wood`) prints one
entry's details as the Encyclopedia would show them.

Checked names (asset manifest `manifest_extended` + 1.0.16 English localization table): creatures `Boar`,
`Greyling`, `Neck`, `Deer`, `Lox`, `Troll`, `Boar_piggy` (Piggy), `Hen`, `Eikthyr`; items `Raspberry`, `Wood`, `Stone`,
`SurtlingCore`, `CopperOre`, `Club`, `SwordBronze`, `Hammer`, `TrophyBoar`, `TrophyDeer`, `BeltStrength`
(Megingjord), `FishingRod`, `HelmetDverger` (Dverger Circlet), `HelmetHat1` (Blue Tied Headscarf), `SaddleLox`,
`ChickenEgg`; pieces `piece_workbench`, `forge`, `smelter`. **(unverified)** (prefab data, not in the game code): that
the Club recipe needs only Wood (T07), that the Smelter costs Stone and Surtling Cores and needs a Workbench (T13), the
Smelter's conversions (T13), that the Lox wears `SaddleLox` (T13), which items an upgrade station takes (T10), that
the Fishing Rod uses the Fishing skill (T34), which item prefabs end up hidden until known (T05). Checked in the
in-world self tests (1.0.16, `NOTE` lines): the trader scan finds the 3 traders, so Megingjord and the Fishing Rod have
a source ("Sold by a trader", T05); the Hen's offspring is the item `ChickenEgg` (T33); 28 vanilla recipes are
upgrade-only (`m_noCraftOnlyUpgrade`); the only upgrade station is the Forge of Potential (`UpgradeStation`), and it
has no Craft tab, so upgrade-station items are never part of a crafting cost (T10); the game makes creature name
plates within **30 m** (`EnemyHud` prefab value; the code default says 10) and shows one for 60 s after the crosshair
was on it (the crosshair ray stops at the first thing it hits: a wall, tree or rock in between blocks it). Checked in the Debug layout dumps and
screenshots of the in-world self tests (1.0.16, 2560x1440): the side panel (`root/Info`, no layout group, the five
controls Texts, Skills, Trophies, Achievements, PVP at a 100-unit step inside the wood panel `Bkg`, explicit
controller navigation), the Valheim Compendium button (`OnOpenTexts`, `Icon` child, tooltip), the Valheim Compendium
dialog (title "Valheim Compendium", two `OnClose` buttons: "Close" and a full-screen click-outside one, list and text
scroll views, no mask on its root; the free space left of its title, where the two tabs go), the Craft tab (TMP label),
the crafting row children, the root canvas (overlay, order 600), and the generic creature icon (a light grey paw print
drawn by the mod, T06). Not checked yet: other resolutions and UI scales (T02, T28, T35).

Log lines (Debug level unless noted):
- `Encyclopedia catalog built in <ms> ms over <frames> frames: <i> items (<h> hidden until known, <n> with no source
  found), <p> pieces, <c> creatures, <x> item prefabs excluded.` (Info), then the stage times, the scan summary
  (source components found, `Trader` and `OfferingBowl` counts), the counts per tab, and one line per excluded
  prefab, hidden item (`Hidden until known: ...`), item with no source (`No source found: ...`) and creature group
  (`Creature ...: prefabs [...], representative ..., trophy ..., habitat ... (source)`).
- `Catalog build started.`, `Catalog build abandoned (world ended).`, `Catalog build restarted (previous build
  stalled).`, `Encyclopedia catalog: game data changed during the build (...): built again at the next open.`
- `Met <creature> (its name plate was shown).`, `Biome recorded: <biome>.`
- UI (Info in the Debug build, Debug in Release): `Encyclopedia tabs on the Valheim Compendium: texts ..., encyclopedia
  ..., title ..., panes ...`, `Encyclopedia window built from ...`, `Encyclopedia window top tabs: ...`, `Encyclopedia
  window layout: ...`; with the optional button: `Encyclopedia button created from '<anchor>' ...`, `Encyclopedia
  button placed <how> (<rect>): inside the panel, no overlap, on screen. Row: <state>.` (or a Warning when it is
  outside the panel, overlaps something or leaves the screen), `Encyclopedia side row: a side control was moved by
  something else ...`. Debug build only, Info level: `Encyclopedia top tabs on the Valheim Compendium ...` (the tabs,
  title, panes, frame, every controller pad of the dialog and its tree; an `OVERLAP:` or `OUTSIDE:` line is a
  problem), `Encyclopedia texts dialog source ...` (the Valheim Compendium dialog's whole tree, logged when the window
  is first built, even when it refuses), `Encyclopedia window layout, screen ...` (every part of our window; an
  `OVERLAP:` line means one of our parts covers the title, the close button or another of our parts), `Encyclopedia
  side panel layout ...` (optional button only). Debug level: `InventoryGui.Update Esc gate checked: ...` (at
  activation), `Clone strip (<kind>): ...` (what each kind of copy lost), `Knowledge: <k> of <n> entries discovered
  ...` at each open. Warnings: `InventoryGui.Update changed ...` (must not appear on 1.0.16), `Another mod keeps moving
  the inventory side panel buttons ...`.

## 0.1.0 — single player

- [ ] **T00 Automated in-world self tests:** `./tools/Test-InWorld.ps1 -Mod Compendium -Only compendium.` (Debug
  build; the game must be closed). Expected: `[selftest] PASS` for `compendium.catalog` (every tab filled, no
  duplicate entry, every entry named and grouped, Wood / Bronze Sword / Boar Trophy / Workbench / Forge / Boar
  present, Forge has recipes and upgrades, every item a creature gives birth to has a "laid by" source),
  `compendium.details` (details of every entry built with the probe character's knowledge, with everything revealed
  and with half of everything known: nothing throws, every undiscovered reference is "???" without icon, no label text
  holds an undiscovered name, no list header names an undiscovered tool or an unvisited biome, list rows and search
  never show an undiscovered name; no upgrade-station item in a quality-1 crafting cost and no crafting line for an
  upgrade-only recipe; the character's skills are the same before and after) and `compendium.knowledge` (a fresh
  character knows little; holding an item, holding a trophy, a kill, a met creature, a placed piece, a seen station
  and a visited biome each discover the right entry; the no-cost mode discovers craftable items and pieces but not
  seasonal items out of season or upgrade-only recipes; a Deer spawned 5 m away gets its hidden name plate and is not
  met, and is met once its plate shows (the hover timer reset that the crosshair does); everything is put back
  afterwards), then the UI tests `compendium.ui` (display settings and the optional button forced to their defaults
  in memory; no side button; the raven opens the Valheim Compendium with the Texts and Encyclopedia tabs on its title
  line, Texts current, inside the frame and clear of the title, the Close button and both panes; its Encyclopedia tab
  opens our window in the same place and size, with the same two tabs on the same spot, Encyclopedia current, clear
  of the category tabs, title, counter and search; the Esc gate check passed at activation; the window is modal: its
  focus group is active, every live inventory focus group is off, its canvas sorts above the inventory, the click
  blocker covers the screen and a click on the raven or on an inventory slot lands on the window; every category tab
  lists rows, no undiscovered row shows a sprite and no header names an undiscovered tool or biome; an undiscovered
  entry's details are "???" with the "?" mark; a discovered item shows its name and icon; a creature with a kill shows
  "Killed: N" and the paw print (head and row); the Texts tab brings the Valheim Compendium back with its list filled
  again; what LT / RT run switches both ways and twice in a row changes nothing, the Encyclopedia keeping its tab and
  entry; the raven reopens the tab used last (Encyclopedia, then Texts); the window opened the way the optional button
  does, then RT in it: Texts stays remembered and the raven reopens Texts; the Esc path: our `InventoryGui.Update`
  prefix is there, closes only the window and zeroes `m_shownFrames`; closing the inventory closes everything and
  the tabs leave the Valheim Compendium), `compendium.toggle` (with the Valheim Compendium open, Enabled off removes
  its two tabs at once and leaves it open exactly as the game made it, object by object, with nothing named
  `MC_Compendium_*` left; Enabled on puts the tabs back at once; the Encyclopedia tab then starts a fresh catalog
  build, the window is closed during it, the build still finishes and the raven reopens it with rows at once; Enabled
  off with the Encyclopedia open destroys it; on again, the raven opens a new window, the Encyclopedia tab still
  remembered) and `compendium.siderow` (optional button off: no button, the game's side row untouched; on, forced in
  memory: our button right after the raven, six controls evenly spaced inside the panel; off live, then Enabled off
  with it on: every side control back exactly, navigation included; the raven hidden as a UI mod would: the row layout
  is dropped, every side control is back on its place and both controller links are the game's again; the raven back:
  row and links again; then "another mod" moves a side control three times: twice its place is kept and the row is
  spaced again, the third time the Encyclopedia gives up and leaves the row alone; after turning the mod off and on in
  the same frame the row and the View/Select shortcut are back; a window that could not be built on this inventory
  (played in memory, as with a UI mod), then the optional button turned on, and off and on again: no button, the
  game's row exactly; once that is put back, the button is back; off again: the game's row exactly). Paste the `NOTE`
  lines (counts, hidden-until-known rules, unverified prefab data, items with no source, upgrade stations, name plate
  distances, the met check, tab rects in both dialogs, where test clicks land, the object-by-object comparison, row
  state and control rects, the dropped row, the three moves, the refused window, the interrupted build) in the test
  notes, and look at the screenshots in the run folder (`side-panel`, `texts-tabs`, `window`, one per tab,
  `undiscovered`, `discovered-item`, `creature`, `texts-again`, `disabled`, `reactivated-texts`, `reactivated`,
  `default-off`, `opt-in`, `gave-up`, `refused-window`).
- [ ] **T01 Optional side button:** with `SideButton = true`, the Encyclopedia button sits in the row of the vanilla
  side controls, right after the Valheim Compendium button (raven) and before Skills, with a gold book icon in the same
  style as the others and the tooltip "Encyclopedia"; hovering shows the tooltip text; the six buttons are evenly
  spaced inside the wood panel; clicking it opens the Encyclopedia; the Debug side panel dump and the "Encyclopedia
  button placed ... Row: 6 controls ..." line are in the log (G1).
- [ ] **T02 Placement (optional button):** with `SideButton = true`, at 1920x1080 and 2560x1440, with and without
  extra inventory rows: the six buttons stay inside the panel, evenly spaced, nothing overlaps, everything on screen
  (G1).
- [ ] **T03 Window:** the Encyclopedia tab of the Valheim Compendium opens the Encyclopedia in the vanilla style, in
  the dialog's place and size, title "Encyclopedia", "Discovered N / M" on the title line. With the optional button
  on: while a vanilla side dialog is open, the side row is locked as it is for the game's own side buttons: with the
  Valheim Compendium open, a click where the Encyclopedia button is only closes the Valheim Compendium (its
  click-outside area covers the screen); with Skills, Trophies or Achievements open, a click on the Encyclopedia button
  does what a click on a vanilla side button does there (nothing, or it only closes that dialog), never opening ours on
  top of it; with a controller, View/Select does nothing while any of them is open. Once the dialog is closed, one
  click opens ours (G2).
- [ ] **T04 Close paths:** Esc and B close only the Encyclopedia, the next press closes the inventory (no
  "InventoryGui.Update changed" warning in the log); the Close button and a left click outside it close it only; Tab,
  E and Y close both; death and teleport close it (G2).
- [ ] **T05 Everything listed:** every tab has entries; the Info "catalog built" line gives the counts; crafting
  stations and their upgrades are under Building. New character: Megingjord, Fishing Rod, Dverger Circlet and Blue
  Tied Headscarf are listed as "???" rows (count the "???" rows of their groups, or check with `RevealAll = true`);
  Piggy and Hen are creature rows. Not listed: `Abomination_attack1`, `bjorn_bite`, `PlayerUnarmed`, `CapeTest`,
  `SwordCheat`, `SledgeCheat`, `DvergerTest`, hair and beards. `spawn BeltStrength` and pick it up: Megingjord is
  discovered and its details say "Where to get it: Sold by a trader" (the self tests found the 3 traders). Paste both
  Debug lists (hidden until known, no source found) in the test notes (G3).
- [ ] **T06 Undiscovered = "???":** new character: undiscovered rows show a "?" mark and "???"; no real name or icon
  in rows, details or tooltips; undiscovered rows come after the discovered ones of their group, not alphabetical;
  their details say "Not discovered yet." plus a hint. `spawn Boar`, then turn away so it is never under your
  crosshair, and stay near it (within 30 m): the Boar stays "???" (its name plate never showed). Then aim at it (its
  name plate appears) without killing it: at the next open the Boar row and title show the paw print icon (light grey,
  not a red "!"), not the Boar Trophy; the Boar Trophy stays "???" in the Trophies tab and in the Boar's drops (G4).
- [ ] **T07 Item discovery:** pick up Raspberries: discovered at the next open. Hold Wood: the Club is discovered by
  its recipe before you ever hold one (G5).
- [ ] **T08 Piece and creature discovery:** Workbench after carrying a Hammer and Wood; Forge after standing next to
  one; Greyling after aiming at one within 30 m without killing it (its name plate shows); a Greyling (or any
  creature) behind a wall, a tree trunk or a rock, aimed at through it, stays "???" (the crosshair ray stops at the
  first thing it hits, no name plate, no Debug `Met ...` line); a Neck after a kill with a bow from more than 30 m
  away, never aimed at closer (so it was never met: the kill alone discovers it); Deer by holding `TrophyDeer` only. A
  boss met without aiming: with `god` on, `spawn Eikthyr`, look away from it (straight up) and let it notice you: its
  health bar appears at the top of the screen (a boss's plate shows when it is alerted within 100 m); open the
  Encyclopedia before killing it: Eikthyr is discovered with "Killed: 0" (Debug `Met Eikthyr` line); then
  `killenemies` (G5).
- [ ] **T09 Existing character:** the mid-game character's first open shows its materials, recipes, trophies, known
  pieces and killed creatures at once (G5).
- [ ] **T10 Item details:** Bronze Sword: stats text, recipe "At Forge (level N)" with exactly the ingredients and
  amounts the game's crafting panel shows for it (no "At an upgrade station:" line under the recipe), upgrade costs per
  quality as the Upgrade tab shows them; when the recipe also takes an item at an upgrade station, that item appears
  only in the quality rows, followed by "Beyond quality N: at an upgrade station only" (which items carry such a
  requirement is prefab data). Wood: where it is chopped (with biomes), "Used in" list. Raspberries: picked in
  Meadows. Your records (picked up, crafted, eaten) match what you did (G6).
- [ ] **T11 Piece details:** Workbench: build cost, comfort if any; Forge: "Needs a ..." line where relevant, its
  upgrades, "Recipes at this station" (G6).
- [ ] **T12 Creature details:** Boar: habitat Meadows, drops with amounts and chances, taming food; health and damage
  modifiers appear only after your first kill. A creature spawning only after a boss says "Appears after defeating
  ..." (G6).
- [ ] **T13 Obfuscated details:** an unknown drop, station, biome and boss show "???" with numbers kept; "Used in"
  folds unknown targets into "??? ×N not discovered yet". New character: build a Workbench and stand next to it, then
  hold a Hammer, Stone and Surtling Cores so the Smelter becomes buildable (it needs a known Workbench, prefab data),
  without holding any ore or metal: its details show one folded conversions line and no ore or bar name; pick up
  Copper Ore: "Turns Copper Ore into ???" appears. `spawn Lox` and aim at it (met, not killed): it says "Can wear:
  ???" and never names the saddle (G7).
- [ ] **T14 Kill count:** on a new character, kill three Boars (kills made with `god` on count too; the game only keeps
  them out of achievements): "Killed: 3" in the Boar's details, and +1 after one more kill and reopening; "Killed: 0" for a
  met-only creature. With Creature Kill and Tame Counts installed, the same number as its Boar line in the Valheim
  Compendium's Player Statistics (Texts tab; the game's own Player Statistics page has no per-creature kill count) (G8).
- [ ] **T15 Controller:** in the Encyclopedia, D-pad and left stick move with rumble; LB and RB change the category
  tab; the right stick scrolls the details; B closes; the inventory does not react while it is open; no button glyph
  on tabs or rows; the key hints at the bottom of the screen (key hints on in the game settings) disappear while it is
  open, as for the game's own side dialogs, and come back as soon as it closes (also after closing it with the mouse).
  With the optional button on: View/Select opens it from the side panel; View/Select with the player grid or the
  crafting panel selected does nothing of ours.
- [ ] **T16 Search:** typing never walks, closes the Encyclopedia or triggers a key; Esc leaves the field first;
  results come from every tab, discovered entries only.
- [ ] **T17 Links:** clicking a discovered ingredient in the details opens its entry; "???" references do nothing.
- [ ] **T18 Settings:** `ShowUndiscovered = false` lists only discovered entries; `RevealAll = true` shows every
  entry and detail; both apply at once to an open Encyclopedia.
- [ ] **T19 Language switch:** names and their order follow the new language; biomes visited before stay known.
- [ ] **T20 Relog and new world:** the Encyclopedia works after logging out and in, and in another world; the Info
  "catalog built" line appears again; note the build time and counts.
- [ ] **T21 Resets:** on a character with a killed Boar, a met-only Greyling, a placed Workbench and a Deer known only
  by its trophy. `resetknownitems`: items "???"; Workbench still discovered (placed); Deer "???"; Boar, Greyling and
  biomes still discovered. Then `resetcharacter`: items and the met-only Greyling "???"; biomes "???" except the one
  you stand in (after reopening); Boar still discovered with its kill count; Workbench still discovered.
- [ ] **T22 Live toggle:** (a) with the Encyclopedia open, set `Enabled = false` by editing
  `BepInEx/config/MC.Exploration.Compendium.Encyclopedia.cfg` (alt-tab; the game reloads the file while it runs), or
  with ConfigurationManager (F1) if installed (a click in its window may also land on the Encyclopedia's click-outside
  area and close it first: then use the file). Not through Esc → MC Mods: Esc closes the Encyclopedia, then the
  inventory, before the menu can open. Expected: the Encyclopedia vanishes, no error. Open the Valheim Compendium: no
  tabs, exactly the game's dialog (compare with a screenshot taken without the mod). With the Valheim Compendium still
  open, set it back to `true`: the Texts / Encyclopedia tabs appear at once and the Encyclopedia tab opens it. Same
  with the Valheim Compendium open while you set `Enabled = false`: its tabs vanish at once, the dialog stays open and
  unchanged. With `SideButton = true` too: the button vanishes with the mod and the five vanilla side buttons are back
  exactly where the game puts them; back on: the button appears at once in the re-spaced row. (b) MC Mods panel:
  close the inventory, Esc → MC Mods → untick the Encyclopedia, open the inventory and the Valheim Compendium: no tabs;
  tick it again: the tabs are back (G9).
- [ ] **T23 Enabled = false + restart:** no tabs, no button, the game's Valheim Compendium and side panel unchanged, no
  error (G9).
- [ ] **T24 Clean log:** a normal session with the Encyclopedia opened in several tabs logs no error or warning from
  the mod (only the Info "catalog built" line and Debug lines).
- [ ] **T25 Build interrupted:** the automated `compendium.toggle` (T00) closes the window during a fresh catalog build
  and checks the next open lists rows at once. By hand: log out and back in (the catalog is built once per world
  session, so only the first open of a session shows "Preparing entries..."), open the inventory, the Valheim
  Compendium, click the Encyclopedia tab and press Esc at once. The build takes about 0.1-0.2 s (the Info "catalog
  built" line gives the time), so the list may already be filled when Esc lands: the case is hit only when the window
  closed while it still said "Preparing entries...". Reopen: the list is there (or fills while you watch), no error.
  Log out and in again and repeat with Tab instead of Esc. One Info "catalog built" line per world session (G2).
- [ ] **T26 View/Select outside the side panel (optional button on):** with the inventory closed, press View/Select
  ten times: the map opens and closes as usual and the Encyclopedia never opens (G2).
- [ ] **T27 Mouse modality:** with the Encyclopedia open, right-click a food item in a visible inventory slot: nothing
  happens (not eaten). Then, reopening it before each: left-click an inventory slot, drag an item, shift-drag a stack,
  left-click a vanilla side button: each one only closes the Encyclopedia (like a click outside the game's Valheim
  Compendium); no item is picked up, moved, used or split (no split dialog) and the side button does not open its
  dialog. After closing, everything works again (G2).
- [ ] **T28 Window layout:** at 1920x1080 and 2560x1440: the screen behind is dimmed; the Texts / Encyclopedia tabs
  sit on the title line at the left, the 8 category tabs fit in one row under them above the list and the details,
  the search field sits at the top of the list box, "Discovered N / M" sits on the title line at the right and is
  readable (not cut), rows show icon and name without overlap, header rows are orange text with no background, detail
  rows have the same text size as the paragraphs, long detail texts wrap and scroll; the Debug window dump has no
  `OVERLAP:` line. Open the console (F5) with the Encyclopedia open: it draws above it.
- [ ] **T29 Session memory:** pick a category tab and an entry, close the Encyclopedia and reopen: the same tab and
  entry come back (each tab remembers its own entry); type a search, close and reopen: the search and its results come
  back; after logging out and in, the Encyclopedia starts on the first tab with an empty search.
- [ ] **T30 Side row with a controller (optional button on):** side panel focused: the D-pad walks Valheim
  Compendium → Encyclopedia → Skills → Trophies → Achievements → PvP and back, in that order; View/Select on any of
  them opens the Encyclopedia (G1).
- [ ] **T31 Side row after a logout (optional button on):** log out and back in (or join another world), open the
  inventory: the row is re-spaced with the button in it again; turn the mod off: the vanilla row is exact again (G1).
- [ ] **T32 Unlock modes:** new character, `devcommands`, then `nocost`: at the next open every craftable item (with
  a recipe the crafting panel offers) and every building piece is discovered, seasonal items out of season are not;
  `nocost` again: they are "???" again. Same with `setkey AllRecipesUnlocked` (items) and `setkey AllPiecesUnlocked`
  (pieces), then `removekey` for each (G5).
- [ ] **T33 Eggs:** hold a Chicken Egg (`spawn ChickenEgg`): its details say "Laid by a tamed ???" until the Hen is
  discovered, then "Laid by a tamed Hen" (G6).
- [ ] **T34 Reading changes nothing:** new character with no Fishing skill yet: set `RevealAll = true` and open the
  Fishing Rod's details (without hovering a real rod anywhere): the game's Skills dialog still has no Fishing line (the
  item's tooltip used to add "Fishing 0"; that the Fishing Rod uses the Fishing skill is prefab data). Set
  `RevealAll = false` again.
- [ ] **T35 Tabs in the Valheim Compendium (mouse):** at 1920x1080 and 2560x1440, open the Valheim Compendium (raven):
  two tabs sit at its top left on the title line, **Texts** (current, lighter, not clickable) and **Encyclopedia**, in
  the style of the crafting panel's Craft tab, inside the wood frame, clear of the title "Valheim Compendium", the list
  and the text pane; the rest of the dialog looks exactly as without the mod. Click Encyclopedia: the Encyclopedia
  appears in exactly the same place and size, the two tabs on the same spot, Encyclopedia now current; it feels like
  switching tabs (no flicker, the screen stays dimmed). Click Texts: the Valheim Compendium is back, its list filled
  again (a lore text or message picked up meanwhile is there). Hover and click feel the same as the crafting panel's
  tabs (G2).
- [ ] **T36 Remembered tab:** leave the Valheim Compendium on the Encyclopedia tab (Esc, B, Close, a click outside or
  Tab), then click the raven again: the Encyclopedia opens directly. Switch to Texts, close, click the raven: Texts.
  Log out and back in: the raven opens Texts first, like the game. With the optional button on, opening the
  Encyclopedia from it does not change which tab the raven reopens (G2).
- [ ] **T37 Controller tabs:** controller only. Valheim Compendium open: RT shows the Encyclopedia, LT the Texts; in
  the Encyclopedia: LT back to Texts, RT does nothing. Pressing LT on Texts, or RT on the Encyclopedia, changes
  nothing. In both, LT and RT never do anything else: the game's list selection and text scrolling in Texts (D-pad,
  sticks) keep working, the Encyclopedia's LB/RB still change its category tab, the crafting panel's Craft / Upgrade
  tabs behind do not change, and B closes whichever one is shown. With Sort Chest, Loot Pickup Filter and Crafting
  Search and Sort installed, none of their controller shortcuts fires from LT or RT here (G2).
- [ ] **T38 Optional button setting live:** default (`SideButton = false`): the inventory's side panel is exactly the
  game's (five buttons at their places; compare with a screenshot without the mod). Set `SideButton = true` with the
  inventory open: the book button appears at once right after the raven, the six buttons evenly spaced; set it back
  to `false`: the button vanishes at once and the five buttons are exactly back where the game puts them (G1).

## 0.1.0 — multiplayer

- [ ] **M01 Dedicated server without the mod:** everything works; a creature controlled by another player's game and
  killed together counts in your "Killed: N".
- [ ] **M02 Hand-off, friend without the mod:** nothing changes for them (their Valheim Compendium has no tabs), no
  error on either side; their tames and the creatures their game controls near you are recorded as met by you once you
  aim at them (Debug `Met ...` line).
- [ ] **M03 Two players with the mod:** each has their own discoveries; one player's discoveries do not show for the
  other.

## 0.1.0 — with other mods

- [ ] **X01 Creature Kill and Tame Counts (MC):** same kill numbers in both; "Tamed: N" shows in the creature details
  after a tame; its Player Statistics section is still in the Valheim Compendium's Texts tab, not in the Encyclopedia.
  A tame alone discovers a creature: on a new character (Boar never met, killed or held as a trophy), look straight up,
  `spawn Boar`, keep looking up and run `tame` (it tames the creatures around you): no Debug `Met Boar` line; at the
  next open the Boar is discovered, with "Tamed: 1" and "Killed: 0".
- [ ] **X02 Crafting Search and Sort (MC):** its search field lets go of the keyboard when the Encyclopedia opens; its F
  key does not focus its field while the Encyclopedia is open; typing in our search never triggers its keys.
- [ ] **X03 Loot Pickup Filter (MC):** R3 and middle click do nothing through the Encyclopedia; its Auto pickup button
  cannot be clicked while the Encyclopedia is open; with its lists panel open, the Encyclopedia draws over it.
- [ ] **X04 Sort Chest (MC):** chest open, chest grid selected (optional button on): View/Select sorts every time (ten
  presses) and never opens the Encyclopedia; with the Encyclopedia open, its buttons and controller shortcuts do
  nothing.
- [ ] **X05 Almanac:** the Trophies button is still Almanac's; the Encyclopedia tab is in the Valheim Compendium; Esc
  behaviour unchanged.
- [ ] **X06 Jewelcrafting or EquipmentAndQuickSlots:** their additions to the Valheim Compendium are on its Texts tab
  as usual, and the Encyclopedia carries none of them.
- [ ] **X07 AugaLite or Veneer:** no tab (and no button with the optional setting on), at most one warning in the log,
  no error.
- [ ] **X08 A content mod (Jotunn items or creatures):** its entries are listed and can be discovered.
- [ ] **X09 ExtraSlots (optional button on):** no overlap with our button (or the placement warning in the log).
- [ ] **X10 A mod that moves or adds side panel buttons (optional button on)** (if one is available): its buttons keep
  their places, an added button joins the even spacing; a mod that keeps moving them gets one "Another mod keeps
  moving ..." warning and our button after the last control; nothing flickers back and forth.
- [ ] **X11 A mod that copies the Valheim Compendium (PraetorisClient Server Guide)** (if available): its window never
  shows the Texts / Encyclopedia tabs; opening the Encyclopedia does not close it and it does not close the
  Encyclopedia.
