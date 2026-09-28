# Creature Kill and Tame Counts — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches, JitCheck clean: 191 methods, 0 failures). No in-game
test yet.

**Setup:** open the Compendium: inventory (Tab) → Compendium button → last entry **Player Statistics**; the new
"Creatures" section is at the top of the text. The page is built when the Compendium opens and is not refreshed while
it stays open: "reopen" below means close the inventory and open the Compendium again. T01 uses your normal character
(no cheats needed). For the other tests use a **test character**: F5 console → `devcommands`; if the console then asks
you to confirm cheats, run `confirmcheats` (it only asks when the character, world and session are not already
cheated). Any cheat command marks the character as cheated for achievements for good; kill stats still count. In
multiplayer, cheat commands (`spawn`, `tame`, `ghost`...) work only on the game of the player who hosts the world
(vanilla: not for clients of a dedicated server, even admins), so each multiplayer test says who hosts. Spawn with
`spawn <Name>` (Tab completes; a second number is an amount, not a level: `spawn Boar 3` makes three boars). **Test in
an empty spot**: `tame` tames every loaded tameable creature you own, wild ones included, so first clear the area with
`killenemies` (kills untamed creatures within 1000 m and spares tames; `killall` and `killtame` also kill tames) and
`nospawn` (stops natural spawns). `god` keeps you alive; `ghost` makes creatures ignore you (use it before walking away
from a hostile creature).

Checked names (asset manifest + localization table): creatures `Greyling`, `Greydwarf`, `Boar`, `Wolf`, `Neck`,
`Deer`, `Troll`, `Lox`, `Eikthyr`; items `Club`, `Bow`, `ArrowWood`, `KnifeButcher`, `FistFenrirClaw`,
`StaffFireball`, `Raspberry`, `Blueberries`, `RawMeat`; the setting **Draw distance** in Settings → Graphics (M06).
**(unverified)** (prefab data, not in the game code): the bare-fists item is the one named "Unarmed" (T04),
`FistFenrirClaw` uses the Unarmed skill (T04), `StaffFireball` uses the Elemental Magic skill (T06), `KnifeButcher`
only hits tames and uses the Knives skill (T14), Boar and Wolf are tameable (T08, T10), the Boar food list and taming
times (use `tame` for fast tests).

Keep `./tools/Watch-Log.ps1 -Mine` open. With `Debug` in the BepInEx disk log levels
(`./tools/Setup.ps1 -DevBepInExConfig`):
- each counted tame logs `Tame counted: <name> (message|owner). Stored MC.Exploration.Stats.PerCreature.Tames = <value>`
  (line breaks shown as `\n`, tabs as `\t`);
- the first run for a character logs `Counting started: <date> (tames are counted from this date).`;
- each page build logs `Creatures section: <rows> rows (<n> other names).`

## 0.1.0 — single player

- [ ] **T01 Past kills show up:** on a character that has fought since the Call to Arms update, install the mod, open
  Player Statistics. Expected: a "Creatures" section at the top lists creatures you killed before installing, with
  kill counts; kills made before the Deep North update have no weapon type, so such rows show either no parentheses
  or an "other" part (never "mixed"); where parentheses show, the parts add up to the kill count; the grey line reads
  "Kills: the game's own count for this character, in every world. Tames: counted while this mod is on (since
  <today's date>)."; the vanilla text follows unchanged. No player name is listed among the creatures (any would be
  under "Other names").
- [ ] **T02 Melee kill:** test character, `spawn Greyling`, kill it with `Club` only, reopen Player Statistics.
  Expected: Greyling +1 killed, "melee" +1.
- [ ] **T03 Ranged kill:** `spawn Deer`, kill it with `Bow` + `ArrowWood` only. Expected: Deer +1, "ranged" +1.
- [ ] **T04 Unarmed vs claws (checks two unverified prefab values):** kill a `Greyling` with bare fists (nothing in
  hands), then another with `FistFenrirClaw`. Expected: first adds "unarmed" +1, second adds "melee" +1 (vanilla
  rule). If the first adds "melee", the fists item is not named "Unarmed": note it and fix the README.
- [ ] **T05 Other (mixed) kill:** note the Greydwarf row (if any). `spawn Greydwarf`, kill it with `Club` only,
  reopen: Greydwarf +1 killed, "melee" +1. `spawn Greydwarf` again, hit it once with `Club`, finish it with `Bow` +
  `ArrowWood`, reopen. Expected: Greydwarf +1 killed, "other" +1, "melee" and "ranged" unchanged; the parts add up to
  the total (on a character with no earlier Greydwarf kills: `Greydwarf: 2 killed (melee 1, other 1)`).
- [ ] **T06 Magic kill (optional, needs eitr food):** kill a Greyling with `StaffFireball`. Expected: "magic" +1.
- [ ] **T07 Settings:** while in game, set `Display.SortBy = Name` and `Display.ShowWeaponTypes = false`
  (ConfigurationManager F1, or edit the cfg file; the MC Mods panel only turns the mod on or off), reopen Player
  Statistics. Expected: alphabetical list, no parentheses. Set them back: most-killed first, parentheses back. No
  restart.
- [ ] **T08 Tame near you:** empty spot (Setup), `spawn Boar`, stand next to it, console `tame`. Expected: "Boar has
  been tamed" message; reopen Player Statistics: Boar +1 tamed per boar the command tamed (one here; a boar with no
  kills shows "Boar: 1 tamed"). Log: `Tame counted: $enemy_boar (message)`.
- [ ] **T09 No double count:** run `tame` again with the same (already tame) boar around. Expected: Boar tame count
  unchanged (the vanilla "Creature Tamed" line in the dump may still go up: vanilla quirk).
- [ ] **T10 Tame while far away:** empty spot, `ghost` on (the wolf ignores you), `spawn Wolf`, walk about 40 m away
  (keep it in sight), console `tame`. Expected: no "has been tamed" message (vanilla), but Player Statistics shows
  Wolf +1 tamed (+1 per wolf the command tamed); log `Tame counted: $enemy_wolf (owner)`. `ghost` off after.
- [ ] **T11 Natural taming (optional, slow):** trap a wild Boar, drop `Raspberry` or `Blueberries` near it, wait
  until it is tamed. Expected: Boar +1 tamed, whether you stayed next to it or walked away.
- [ ] **T12 Saved with the character:** after T08-T10, die (e.g. `spawn Troll` without `god`) and respawn, then log
  out to the main menu and back in. Expected: tame counts and the "since" date unchanged; kill counts unchanged.
- [ ] **T13 Other worlds:** kill a Greyling in world A, then open Player Statistics in world B with the same
  character. Expected: that kill is included.
- [ ] **T14 Butcher knife (checks an unverified prefab value):** tame a Boar, kill it with `KnifeButcher`. Expected:
  Boar +1 killed with "melee" +1 (vanilla counts it; melee if the knife uses the Knives skill), tame count
  unchanged.
- [ ] **T15 Gamepad:** with a controller, open the Compendium, go down to Player Statistics. Expected: the section is
  at the top of the text, the right stick scrolls as in vanilla.
- [ ] **T16 Live toggle:** Esc → MC Mods → untick Creature Kill and Tame Counts → reopen Player Statistics.
  Expected: vanilla page, no "Creatures" section. `spawn Boar` + `tame` while off, and kill a `Greyling`. Tick it
  again, reopen. Expected: section back, that boar is **not** counted, the Greyling kill **is** (vanilla counted
  it), "since" date unchanged, no restart.
- [ ] **T17 Disabled in the config file:** `Enabled = false` in `BepInEx/config/MC.Exploration.Stats.PerCreature.cfg`,
  restart. Expected: vanilla page. Set it back to `true`.
- [ ] **T18 Clean log:** after a session, no errors or exceptions mentioning `Creature Kill and Tame Counts` or
  `MC.Exploration` in `BepInEx/LogOutput.log`.
- [ ] **T19 API and storage format (for the future Compendium):** create a new test character with the Debug log on
  and open Player Statistics before doing anything. Expected: the section shows "Nothing killed or tamed yet." Then do
  T08 once. Expected: the `Tame counted` log line shows
  `Stored MC.Exploration.Stats.PerCreature.Tames = 1\n1\t$enemy_boar` exactly (version line `1`, count, tab, token;
  plain digits); the `Counting started` line shows the date as `yyyy-MM-dd` with today's Gregorian year in plain
  digits (optional: repeat on a character after switching the Windows region format to Thai, whose calendar year
  differs: still the Gregorian year). The page (built only through the public `CreatureCounts` API) shows the same
  numbers: Boar 1 tamed. Log out and back in: the page still shows it (the stored value was read back through the
  API).
- [ ] **T20 Hidden HUD:** test character, empty spot, `spawn Boar`, stand next to it, press left Ctrl + F3 (hides the
  HUD), console `tame`, then left Ctrl + F3 again. Expected: no message was shown (vanilla hides messages with the
  HUD), but Player Statistics shows Boar +1 tamed.

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Shared kill, vanilla server:** join a vanilla server (or host) with a friend who does NOT have the mod.
  Both hit the same Greyling, the friend kills it. Expected: your Greyling count +1 (vanilla gives every attacker the
  kill); no errors on either side.
- [ ] **M02 Hand-off: tame from an unmodded owner:** the friend without the mod hosts the world (cheats) and spawns a
  Boar (their game owns it); you stand closer to the boar than the friend, within 30 m; the friend runs `tame`.
  Expected: you see "Boar has been tamed" and your Boar tame count +1; the friend sees nothing unusual.
- [ ] **M03 Closest player only:** both players have the mod, both within 30 m of a boar; the host spawns it and runs
  `tame`. Expected: only the player who gets the "has been tamed" message has +1 tamed.
- [ ] **M04 Nobody close, owner closest:** you host the world (cheats). Empty spot, `ghost` on; you `spawn Boar` (your
  game owns it); the friend stands about 100 m away; you walk about 40 m from the boar (you are the closest player,
  but not within 30 m) and run `tame`. Expected: no message for anyone; +1 Boar tamed for you (owner), nothing for
  the friend.
- [ ] **M05 Hand-off: tame for a friend without the mod:** tame a wolf with the mod (counted; as the host you can use
  `tame`), then the friend without the mod pets it and commands it. Expected: an ordinary tame for them, no errors on
  either side.
- [ ] **M06 Nobody close, a non-owner closer:** you host the world (cheats); on both games set Settings → Graphics →
  **Draw distance** above the lowest level. Empty spot, `ghost` on; you `spawn Boar` (your game owns it); the friend
  (with the mod) stands about 35 m from the boar; you walk about 45-50 m from it (the friend is now the closest
  player, nobody within 30 m) and run `tame`. Expected: the boar is tamed, no message for anyone, and **nobody** gets
  +1 Boar tamed (neither you nor the friend); log `Tame counted` absent on both sides. If the boar is not tamed at
  all, it changed owner to the friend's game: move a little closer and spawn a new boar.

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 With One Click Repair All:** both mods on. Repair several items in one click (still one summary message),
  then tame a boar with `tame` next to you. Expected: repair unchanged; Boar +1 tamed.
- [ ] **C02 With a mod that edits the Compendium (EquipmentAndQuickSlots or SecondaryAttacks):** open the
  Compendium. Expected: that mod's entries or changes are there as usual, and Player Statistics still starts with the
  "Creatures" section; no errors.
- [ ] **C03 With DudeWhatAreMyStats:** open its stats window and Player Statistics. Expected: the kill counts of the
  creatures it lists match the "Creatures" section; no errors.
