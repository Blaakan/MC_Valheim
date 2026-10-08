# Creature Kill and Tame Counts — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches, JitCheck clean: 191 methods, 0 failures). No in-game
test yet.

**Automated checks (2026-10-08):** 21 of 22 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** open the Valheim Compendium: inventory (Tab) → the game's **Valheim Compendium** button (raven icon; with the
MC Encyclopedia mod installed, on its **Texts** tab, not the Encyclopedia tab, which has no Player Statistics) → last
entry **Player Statistics**; the new
"Creatures" section is at the top of the text. The page is built when the Valheim Compendium opens and is not refreshed while
it stays open: "reopen" below means close the inventory and open the Valheim Compendium again. T01 uses your normal character
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

- [x] **T01 Past kills show up:** on a character that has fought since the Call to Arms update, install the mod, open
  Player Statistics. Expected: a "Creatures" section at the top lists creatures you killed before installing, with
  kill counts; kills made before the Deep North update have no weapon type, so such rows show either no parentheses
  or an "other" part (never "mixed"); where parentheses show, the parts add up to the kill count; the grey line reads
  "Kills: the game's own count for this character, in every world. Tames: counted while this mod is on (since
  <today's date>)."; the vanilla text follows unchanged. No player name is listed among the creatures (any would be
  under "Other names").
  *Partly automated (`percreature.page`, `percreature.toggle`); by hand: A real character file that fought before the
  mod was installed (and before Deep North): the test writes such numbers into the probe character instead of loading
  an old save. Open Player Statistics once on such a character and check its past kills are listed and the grey line
  shows today's date on the first run.*
- [x] **T02 Melee kill:** test character, `spawn Greyling`, kill it with `Club` only, reopen Player Statistics.
  Expected: Greyling +1 killed, "melee" +1.
  *Automated: `percreature.club`.*
- [x] **T03 Ranged kill:** `spawn Deer`, kill it with `Bow` + `ArrowWood` only. Expected: Deer +1, "ranged" +1.
  *Automated: `percreature.bow`.*
- [x] **T04 Unarmed vs claws (checks two unverified prefab values):** kill a `Greyling` with bare fists (nothing in
  hands), then another with `FistFenrirClaw`. Expected: first adds "unarmed" +1, second adds "melee" +1 (vanilla
  rule). If the first adds "melee", the fists item is not named "Unarmed": note it and fix the README.
  *Automated: `percreature.fists`, `percreature.data`.*
- [x] **T05 Other (mixed) kill:** note the Greydwarf row (if any). `spawn Greydwarf`, kill it with `Club` only,
  reopen: Greydwarf +1 killed, "melee" +1. `spawn Greydwarf` again, hit it once with `Club`, finish it with `Bow` +
  `ArrowWood`, reopen. Expected: Greydwarf +1 killed, "other" +1, "melee" and "ranged" unchanged; the parts add up to
  the total (on a character with no earlier Greydwarf kills: `Greydwarf: 2 killed (melee 1, other 1)`).
  *Automated: `percreature.mixed`.*
- [x] **T06 Magic kill (optional, needs eitr food):** kill a Greyling with `StaffFireball`. Expected: "magic" +1.
  *Partly automated (`percreature.kills`, `percreature.data`); by hand: A real fireball: the test does not cast
  StaffFireball (the probe character has no eitr), it injects a hit with the staff's skill. Kill one Greyling with a
  real StaffFireball cast and check 'magic' goes up by 1.*
- [x] **T07 Settings:** while in game, set `Display.SortBy = Name` and `Display.ShowWeaponTypes = false`
  (ConfigurationManager F1, or edit the cfg file; the MC Mods panel only turns the mod on or off), reopen Player
  Statistics. Expected: alphabetical list, no parentheses. Set them back: most-killed first, parentheses back. No
  restart.
  *Automated: `percreature.settings`, `percreature.mp.config`.*
- [x] **T08 Tame near you:** empty spot (Setup), `spawn Boar`, stand next to it, console `tame`. Expected: "Boar has
  been tamed" message; reopen Player Statistics: Boar +1 tamed per boar the command tamed (one here; a boar with no
  kills shows "Boar: 1 tamed"). Log: `Tame counted: $enemy_boar (message)`.
  *Automated: `percreature.tames`.*
- [x] **T09 No double count:** run `tame` again with the same (already tame) boar around. Expected: Boar tame count
  unchanged (the vanilla "Creature Tamed" line in the dump may still go up: vanilla quirk).
  *Automated: `percreature.tames`.*
- [x] **T10 Tame while far away:** empty spot, `ghost` on (the wolf ignores you), `spawn Wolf`, walk about 40 m away
  (keep it in sight), console `tame`. Expected: no "has been tamed" message (vanilla), but Player Statistics shows
  Wolf +1 tamed (+1 per wolf the command tamed); log `Tame counted: $enemy_wolf (owner)`. `ghost` off after.
  *Automated: `percreature.tames`.*
- [x] **T11 Natural taming (optional, slow):** trap a wild Boar, drop `Raspberry` or `Blueberries` near it, wait
  until it is tamed. Expected: Boar +1 tamed, whether you stayed next to it or walked away.
  *Automated: `percreature.feed`.*
- [x] **T12 Saved with the character:** after T08-T10, die (e.g. `spawn Troll` without `god`) and respawn, then log
  out to the main menu and back in. Expected: tame counts and the "since" date unchanged; kill counts unchanged.
  *Partly automated (`percreature.store`); by hand: A real death and respawn, and a real logout to the main menu and
  back in: check tame counts, the 'since' date and kill counts are unchanged.*
- [x] **T13 Other worlds:** kill a Greyling in world A, then open Player Statistics in world B with the same
  character. Expected: that kill is included.
  *Partly automated (`percreature.kills`); by hand: Open Player Statistics in a second world with the same character
  and see the kill made in the first world.*
- [x] **T14 Butcher knife (checks an unverified prefab value):** tame a Boar, kill it with `KnifeButcher`. Expected:
  Boar +1 killed with "melee" +1 (vanilla counts it; melee if the knife uses the Knives skill), tame count
  unchanged.
  *Automated: `percreature.butcher`, `percreature.data`.*
- [ ] **T15 Gamepad:** with a controller, open the Valheim Compendium, go down to Player Statistics. Expected: the section is
  at the top of the text, the right stick scrolls as in vanilla.
  *Partly automated (`percreature.scroll`); by hand: A real controller: go down the Valheim Compendium list to Player
  Statistics with the D-pad or left stick and scroll the text with the right stick.*
- [x] **T16 Live toggle:** Esc → MC Mods → untick Creature Kill and Tame Counts → reopen Player Statistics.
  Expected: vanilla page, no "Creatures" section. `spawn Boar` + `tame` while off, and kill a `Greyling`. Tick it
  again, reopen. Expected: section back, that boar is **not** counted, the Greyling kill **is** (vanilla counted
  it), "since" date unchanged, no restart.
  *Automated: `percreature.toggle`, `percreature.mp.config`.*
- [x] **T17 Disabled in the config file:** `Enabled = false` in `BepInEx/config/MC.Exploration.Stats.PerCreature.cfg`,
  restart. Expected: vanilla page. Set it back to `true`.
  *Partly automated (`percreature.mp.config`, `percreature.toggle`); by hand: Start the game with Enabled = false
  already in BepInEx/config/MC.Exploration.Stats.PerCreature.cfg and check the page is vanilla, then set it back to
  true.*
- [x] **T18 Clean log:** after a session, no errors or exceptions mentioning `Creature Kill and Tame Counts` or
  `MC.Exploration` in `BepInEx/LogOutput.log`.
  *Partly automated (`percreature.log`); by hand: A normal play session: afterwards read BepInEx/LogOutput.log for
  errors or exceptions that mention the mod (the test only covers the self-test run).*
- [x] **T19 API and storage format (for the MC Encyclopedia mod):** create a new test character with the Debug log on
  and open Player Statistics before doing anything. Expected: the section shows "Nothing killed or tamed yet." Then do
  T08 once. Expected: the `Tame counted` log line shows
  `Stored MC.Exploration.Stats.PerCreature.Tames = 1\n1\t$enemy_boar` exactly (version line `1`, count, tab, token;
  plain digits); the `Counting started` line shows the date as `yyyy-MM-dd` with today's Gregorian year in plain
  digits (optional: repeat on a character after switching the Windows region format to Thai, whose calendar year
  differs: still the Gregorian year). The page (built only through the public `CreatureCounts` API) shows the same
  numbers: Boar 1 tamed. Log out and back in: the page still shows it (the stored value was read back through the
  API).
  *Partly automated (`percreature.store`); by hand: A real new character file, and the last step: log out and back in
  once and check the page still shows Boar 1 tamed (the test checks the save data and a re-read of the stored text,
  not a real logout).*
- [x] **T20 Hidden HUD:** test character, empty spot, `spawn Boar`, stand next to it, press left Ctrl + F3 (hides the
  HUD), console `tame`, then left Ctrl + F3 again. Expected: no message was shown (vanilla hides messages with the
  HUD), but Player Statistics shows Boar +1 tamed.
  *Automated: `percreature.tames`.*
- [!] **T21 Tame that finishes while you wait to respawn:** the `percreature.bug.respawn-gap` self-test checks the mod's
  side of this without a real death (the game tames a boar while it has no local character; the test fails until
  the mod counts such a tame). A real death still has to be checked by hand, in single player: base with your bed
  as spawn point and a pen next to it; feed a wild Boar and wait until its taming is nearly done (hover text close
  to 100%), then die next to the pen (`god` off, for example `spawn Troll`) and wait for the respawn. Expected: a
  boar that becomes tame between the moment your body disappears (about 10 s after death) and your respawn is
  counted once you are back: Boar +1 tamed, like the game's own "Creature Tamed" total (in single player you always
  get your own tames). The same holds for a boar that becomes tame while the world is still loading at login.
  **FAILED:** automated test: A tame that finishes while the game has no local character (respawn wait, world still
  loading) is never counted (self-test `percreature.bug.respawn-gap`)
- [x] **T22 Stored tame counts stay readable and safe:** covered by the `percreature.format` self-test (the stored
  value cannot be edited in game). Expected: a stored value with Windows line ends, empty lines, unreadable lines,
  signed or space-padded numbers or zero counts is read without error (unreadable lines are skipped, two lines of
  the same creature are added up), and the next tame rewrites it clean: version line `1`, then one line per
  creature, sorted by name. A count at 2147483647 stays there. A value whose first line is not `1` (written by a
  newer version of this mod) is still shown but never rewritten: new tames are not counted and one warning is logged
  per session. A creature name with a line break is refused with a warning. The "since" date never moves.
  *Automated: `percreature.format`.*
- [x] **T23 Page rows at the limits:** covered by the `percreature.page` self-test (in normal play these depend on
  your character's history). Expected: a creature whose counts are all 0 (what an achievement reset leaves behind)
  has no row; a creature with only old kills shows no parentheses (`Neck: 40 killed`); a creature that was only
  tamed shows `Wolf: 2 tamed`; kills and tames together read `Deer: 3 killed (melee 3), 1 tamed`; a creature whose
  name has no translation is listed among the creatures the way the game shows a missing translation
  (`[enemy_...]`); a name that is not a translation key (a player name from an old PvP kill) is listed only under
  the grey "Other names" line; after the last row come two empty lines, then the vanilla text.
  *Automated: `percreature.page`.*

## 0.1.0 — multiplayer (needs a second player)

- [x] **M01 Shared kill, vanilla server:** join a vanilla server (or host) with a friend who does NOT have the mod.
  Both hit the same Greyling, the friend kills it. Expected: your Greyling count +1 (vanilla gives every attacker the
  kill); no errors on either side.
  *Partly automated (`percreature.mp.session`, `percreature.kills`, `probe.mp.baseline`); by hand: A real friend
  without the mod landing the last hit on a Greyling you both hit, so the kill message really comes from another game;
  and no errors on the friend's side.*
- [x] **M02 Hand-off: tame from an unmodded owner:** the friend without the mod hosts the world (cheats) and spawns a
  Boar (their game owns it); you stand closer to the boar than the friend, within 30 m; the friend runs `tame`.
  Expected: you see "Boar has been tamed" and your Boar tame count +1; the friend sees nothing unusual.
  *Partly automated (`percreature.tame-paths`, `percreature.mp.session`); by hand: A real friend without the mod
  hosting, spawning the boar and running `tame` while you stand closest: the message then comes from the friend's
  game. Check you see it and get +1, and that the friend sees nothing unusual.*
- [x] **M03 Closest player only:** both players have the mod, both within 30 m of a boar; the host spawns it and runs
  `tame`. Expected: only the player who gets the "has been tamed" message has +1 tamed.
- [x] **M04 Nobody close, owner closest:** you host the world (cheats). Empty spot, `ghost` on; you `spawn Boar` (your
  game owns it); the friend stands about 100 m away; you walk about 40 m from the boar (you are the closest player,
  but not within 30 m) and run `tame`. Expected: no message for anyone; +1 Boar tamed for you (owner), nothing for
  the friend.
  *Partly automated (`percreature.tames`, `percreature.mp.session`); by hand: The friend's half: a second player
  standing about 100 m away gets no message and no tame.*
- [x] **M05 Hand-off: tame for a friend without the mod:** tame a wolf with the mod (counted; as the host you can use
  `tame`), then the friend without the mod pets it and commands it. Expected: an ordinary tame for them, no errors on
  either side.
  *Partly automated (`percreature.mp.session`); by hand: A real friend without the mod petting the wolf and telling it
  to follow and stay: an ordinary tame for them, no errors on either side.*
- [x] **M06 Nobody close, a non-owner closer:** you host the world (cheats); on both games set Settings → Graphics →
  **Draw distance** above the lowest level. Empty spot, `ghost` on; you `spawn Boar` (your game owns it); the friend
  (with the mod) stands about 35 m from the boar; you walk about 45-50 m from it (the friend is now the closest
  player, nobody within 30 m) and run `tame`. Expected: the boar is tamed, no message for anyone, and **nobody** gets
  +1 Boar tamed (neither you nor the friend); log `Tame counted` absent on both sides. If the boar is not tamed at
  all, it changed owner to the friend's game: move a little closer and spawn a new boar.

## 0.1.0 — compatibility (optional, needs another mod)

- [x] **C01 With One Click Repair All:** both mods on. Repair several items in one click (still one summary message),
  then tame a boar with `tame` next to you. Expected: repair unchanged; Boar +1 tamed.
  *Partly automated (`percreature.tame-paths`, `percreature.tames`); by hand: The repair click itself with both mods
  on: several items repaired in one click with one summary message (covered by One Click Repair All's own tests).*
- [ ] **C02 With a mod that edits the Valheim Compendium (EquipmentAndQuickSlots or SecondaryAttacks):** open the
  Valheim Compendium. Expected: that mod's entries or changes are there as usual, and Player Statistics still starts with the
  "Creatures" section; no errors.
  *Partly automated (`percreature.compat`); by hand: The real EquipmentAndQuickSlots or SecondaryAttacks mod
  installed: its entries or changes are there as usual and Player Statistics still starts with the Creatures section.*
- [ ] **C03 With DudeWhatAreMyStats:** open its stats window and Player Statistics. Expected: the kill counts of the
  creatures it lists match the "Creatures" section; no errors.
  *Partly automated (`percreature.kills`); by hand: The real DudeWhatAreMyStats window next to Player Statistics: its
  kill counts match the Creatures section.*
