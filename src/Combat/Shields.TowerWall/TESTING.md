# Tower Shield Wall — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-09-30: loads, patches cleanly, JitCheck clean. In-world self-tests
(`./tools/Test-InWorld.ps1 -Mod Shields.TowerWall`) passed 2026-09-30 in a full run with every MC mod (71 of 71
tests): `tower.data`, `tower.rules`, `tower.hands`, `tower.slow`, `tower.block`, `tower.bash`, `tower.lock`,
`tower.ng`, `tower.toggle`. Their NOTE lines in the log record the values the design lists as unverified. First
hands-on test by the user on build `3cda33b+dirty` (2026-09-30): the bash was reworked from that feedback (2 s
cooldown, slower swing, 20 stamina, 8 s stagger lock, ShieldUp removed; design decisions 30-34), so the bash items
below are reset. A review of that change then added: one heavy stagger per bash (only the creature nearest the middle
of the swing, decision 35), a press made before the cooldown ends is kept until it ends, and `BashAnimation` names in
any case. Built (Debug and Release, no warnings). The in-world self-tests then ran on it (2026-09-30, full run, 66 of
71 passed): `tower.bash` passed; `tower.lock` and `tower.ng` failed because a rock stood between the player and the
test Draugr, so the bash hit the rock (each bash's punch steps the player forward, and the earlier bash tests had
walked him up to it; the screenshot `tower.ng__bash.png` shows it). The self-tests now turn the player to a free lane
before each bash and put him back where he started after each test; rebuilt (Debug, no warnings), not run again yet.
The smoke test has not been run on this build yet.

**Automated checks (2026-10-08):** 40 of 41 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). Give
yourself items with `spawn <name> <amount> <quality> p` (`p` puts it straight into your inventory:
`spawn ShieldIronTower 1 1 p`; quality 2-4 for upgraded items). Spawn creatures with `spawn <name> 1 1` (level 1 =
0 stars). `god` keeps your health at 1 or more (stamina, stagger and knockback still count), `heal` refills health and
stamina, `killall` removes nearby creatures, `raiseskill Blocking 100` / `resetskill Blocking` set the skill.
New Game+ (T23): `setkey WorldLevel 1` makes the world level 1 (use a throwaway world), `removekey WorldLevel` puts it
back. Settings are changed with ConfigurationManager (F1) or in `BepInEx/config/MC.Combat.Shields.TowerWall.cfg`; put
each one back to its default after the item that changed it. A config file from the first test build keeps
`BashStaggerCooldown` (no longer read: the setting is now `BashStaggerLock`) and may say `BashAnimation = ShieldUp`
(read as ShieldPunch); deleting the file gives the new defaults. Follow the log with `./tools/Watch-Log.ps1 -Mine`.
Expected numbers are for single player, 0-star creatures, default world modifiers, Blocking 0 and no body armor unless
the item says otherwise.

Names checked in the 1.0.16 game data: items `ShieldWoodTower`, `ShieldBoneTower`, `ShieldIronTower`,
`ShieldSerpentscale`, `ShieldBlackmetalTower`, `ShieldFlametalTower`, `ShieldGoldTower`, `ShieldIronSquare`,
`ShieldBanded`, `ShieldWood`, `ShieldIronBuckler`, `SwordIron`, `KnifeCopper`, `Torch`, `Bow`, `Battleaxe`, `Hammer`,
`CrossbowArbalest`, `SpearChitin` (the harpoon), `StaffShield` (its attack is unblockable and deals no damage),
`StaffIceShards`;
creatures `Greydwarf` (stagger threshold 12), `Greydwarf_Shaman`, `Draugr` (50), `Troll` (180; its punch is 60
blunt), `Blob` (never staggers), `Skeleton`, `Greyling`, `Boar`, `FallenWarrior`, and the training dummy
`piece_TrainingDummy`; the animations `unarmed_attack0`, `unarmed_attack1`, `unarmed_kick`, `throw_bomb`,
`spear_poke`, `mace_secondary`, `dualaxes3`, `emote_wave` and `staff_rapidfire` (`StaffIceShards`' held attack).
Not in the checked data **(unverified)**: the English item names used below (Nord Greatshield = `ShieldGoldTower`,
Abyssal Harpoon = `SpearChitin`, Staff of Protection = `StaffShield`), the piece names of the item stand and the armor
stand (build them with the hammer), crossbow bolts (`BoltBone`; Tab autocompletes), armor slowdown values (T30), which
arm strikes in each bash animation (T15; the clips are known from the in-world run: ShieldPunch plays `Punchstep 2`,
OtherPunch `Punchstep 1`, Kick `Kickstep`), and the bash timings at the default speed 0.6 (about 0.8 s from the press
to the hit, estimated from the clip data; T34).

## 0.1.0 — single player

- [x] **T01 Two-handed:** hold `SwordIron` + `ShieldBanded`, then equip `ShieldIronTower`. Expected: the sword and the
  banded shield are put away, only the tower shield is in your left hand. Its tooltip says "Two-handed", "Cannot
  parry", "Bash stagger: ×25", "Bash cooldown: 2 s" and "Braced: movement -30%, blocks attacks from the front that
  normally cannot be blocked; while you have stamina for another block: stagger -80%, no knockback from the front".
  *Automated: `tower.hands`, `tower.tips`, `tower.data`.*
- [x] **T02 Swaps:** with the tower shield held, equip in turn `SwordIron`, `Torch`, `ShieldBanded`, `Bow`,
  `Battleaxe`, `Hammer`. Expected: each one puts the tower shield away; re-equipping the tower shield empties both
  hands again.
  *Automated: `tower.equip`, `tower.hands`.*
- [ ] **T03 Look:** hold the tower shield, then put it away (R) and swim. Expected: in hand it sits on the left arm as
  before; put away (and while swimming) it hangs in the shield slot on your back, not like a two-handed weapon. Place it
  on an armor stand (shield slot) and on an item stand. Expected: both take it and show it like a shield.
  *Partly automated (`tower.hands`, `tower.stand`, `tower.data`); by hand: How it looks: on the left arm in hand, on
  the back when put away and while swimming (swimming is not driven), and on both stands (screenshots
  tower.hands__tower-on-back, tower.stand__item-stand, tower.stand__armor-stand).*
- [x] **T04 Pickup:** drop a tower shield, hold `ShieldBanded` with an empty right hand, pick the tower shield up.
  Expected: it goes to the inventory; the banded shield stays equipped. Hold the tower shield and pick up a dropped
  `SwordIron`. Expected: the sword goes to the inventory, not into your hand.
  *Automated: `tower.hands`.*
- [x] **T05 Carried slow:** jog and sprint the same stretch with and without the tower shield in hand (same armor).
  Expected: much slower with it (about 2.8 m/s jogging instead of 3.6 with a normal tower shield; tooltip "Movement
  -30%"); sprinting is still faster than jogging; walking and crouching speeds do not change.
  *Automated: `tower.speed`, `tower.tips`, `tower.slow`.*
- [x] **T06 Braced slow:** hold block with the tower shield and walk, then turn. Expected: noticeably slower advance
  and slower turning than blocking with `ShieldBanded`; release block: back to the carried speed.
  *Automated: `tower.speed`, `tower.slow`.*
- [x] **T07 No parry:** block right as a `Greydwarf` hits, many times. Expected: never a parry (no parry flash or
  sound, the Greydwarf is never staggered by your block). `ShieldGoldTower` tooltip: no parry adrenaline line, a
  "Cannot parry" line.
  *Automated: `tower.parry`, `tower.tips`, `tower.data`.*
- [x] **T08 Block armor:** `resetskill Blocking`, then read the block armor on the tooltips at quality 1. Expected:
  Wood 25, Iron 130, Black metal 260, Nord Greatshield 395; +15 per quality level (`spawn ShieldIronTower 1 2 p`: 145).
  Then `god` off, no armor, alone: block the punches of a 0-star `Troll` with the Iron tower shield. Expected: about 7
  damage and 4 stamina per blocked punch (a normal Iron tower shield: about 17 and 8).
  *Automated: `tower.tips`, `tower.wall`, `tower.data`.*
- [x] **T09 The wall holds:** `god` on, food eaten (max health 75 or more, so your stagger threshold is 30 or more), no
  body armor, `ShieldWoodTower`, a `Troll`: block its punches from full stamina. Expected: about 35 damage gets through
  each block (god mode keeps you alive) and each block costs about 10 stamina; you are not staggered while the HUD
  shows "Braced"; when stamina is down to about one block, the icon flashes "Braced (exhausted)" and the next punch
  lands in full and staggers you; after about a second of regen the brace does not come back until stamina is above
  one block again.
  *Automated: `tower.wall`, `tower.block`.*
- [x] **T10 Unblockable attacks from the front:** `ShieldIronTower`, brace facing a `Blob`, then a `Greydwarf_Shaman`.
  Expected: their poison shows "Blocked" and you get little poison (a fifth or less of it). Turn your back: the full
  poison. With `BlockUnblockableAttacks = false`: the poison goes through as in the normal game.
  *Automated: `tower.poison`, `tower.block`.*
- [ ] **T11 Still unblockable:** while braced, take fall damage, stand in fire, and let a `Greydwarf` hit you from
  behind. Expected: none of them is blocked.
  *Partly automated (`tower.wall`, `tower.block`); by hand: Stand in a real campfire while braced and check the burn
  is not blocked (no 'Blocked' text, no stamina spent): the test's fire hit is built in code, the campfire's own
  damage was not driven.*
- [ ] **T12 Bash:** press attack with the tower shield. Expected: a shield-arm strike (ShieldPunch), about 20 stamina,
  low damage numbers; the tooltip lists Blunt, stamina use 20, knockback, "Bash stagger: ×25" and "Bash cooldown:
  2 s"; bashing a creature raises the Blocking skill.
  *Partly automated (`tower.train`, `tower.tips`, `tower.bash`, `tower.data`); by hand: Watch one bash (or the
  screenshot tower.bash__default-1): it is the shield arm that strikes.*
- [ ] **T13 Bash stagger:** `ShieldIronTower`, Blocking 0. Expected: a `Greydwarf` and a `Draugr` stagger in one bash,
  a `Troll` in two to four bashes (2 s apart; two or three for about four Trolls in five, four for the rest, more when
  you bash slower). Hit a staggered enemy with a sword right away (quick swap). Expected: the critical hit effect
  (double damage).
  *Partly automated (`tower.follow`, `tower.bash`); by hand: The Troll count is a matter of dice and the test only
  accepts two to six and notes how many it took. The item (and the README) say two to four, but the re-run needed five
  bashes at the 2 s cadence, so 'four for the rest' does not always hold: bash a few Trolls, note the counts, and
  decide whether the wording or the numbers change.*
- [x] **T14 Bash from guard:** hold block and press attack. Expected: the bash plays and the guard is back right after
  it (keep block held).
  *Automated: `tower.guard`.*
- [ ] **T15 Bash animations:** `BashAnimation` offers ShieldPunch, OtherPunch, Kick and Custom (ShieldUp is gone). Try
  each, and Custom with `BashCustomTrigger` = `throw_bomb`, `spear_poke`, `mace_secondary`. Note which one looks best,
  which arm swings, and whether the log says an option fell back. Custom with `emote_wave` or a made-up name.
  Expected: the Warning "BashCustomTrigger '...' is not a player attack animation; using ShieldPunch." and the
  ShieldPunch animation plays. Custom with `staff_rapidfire`. Expected: the Warning "BashCustomTrigger
  'staff_rapidfire' is the animation of an attack that is held, aimed or reloaded, which a bash cannot play; using
  ShieldPunch.", ShieldPunch plays, and you can block, dodge and switch weapons right after the bash. Write
  `BashAnimation = ShieldUp` in the config file. Expected: ShieldPunch plays, no warning. Write `BashAnimation = kick`
  (lower case). Expected: the kick plays and the setting reads `Kick`. Write `BashAnimation = Kicks`. Expected:
  ShieldPunch plays and the log has the Warning "BashAnimation 'Kicks' is not one of ShieldPunch, OtherPunch, Kick,
  Custom; ShieldPunch is used." (On a server only the host's choice applies.)
  *Partly automated (`tower.warn`, `tower.guard`, `tower.bash`, `tower.rules`, `tower.mp.rules`); by hand: Which
  animation looks best and which arm swings in each option; Custom with spear_poke and mace_secondary is only checked
  as allowed, not swung.*
- [x] **T16 Settings live:** with the tower shield equipped, change `BlockArmorMultiplier`, `BlockForcePercent`,
  `CarrySlowPercent`, `BraceSlowPercent`, `BraceKnockbackResistPercent`, `BashStagger`, `BashStaggerLock`,
  `BashCooldown`, `BashAnimationSpeed` and `BashAnimation`. Expected: tooltips, speed, push and bash change within
  about a second, without re-equipping.
  *Automated: `tower.live`, `tower.mp.rules`, `tower.rules`.*
- [x] **T17 Creature copies untouched:** `spawn FallenWarrior` (a few times: one of its random equipment sets is a
  black metal sword with a tower shield). Expected: one with a tower shield fights with its sword and its shield as in
  the normal game.
  *Automated: `tower.creature`, `tower.data`, `tower.hands`.*
- [ ] **T21 Lock limit:** alone, `ShieldIronTower`, bash a `Draugr` continuously. Expected: it staggers on the first
  bash, then not again for about 8 s however often you bash, and it attacks you between staggers. With
  `BashStaggerLock = 0`: every bash that lands after the previous stagger ended staggers it again (with bashes 2 s
  apart and a Draugr stagger of about 2.5 s, about every other bash; it is staggered most of the time). Note how long
  each stagger lasts.
  *Partly automated (`tower.cadence`, `tower.lock`); by hand: That the Draugr attacks you between staggers (the test
  Draugr has its AI off).*
- [x] **T22 Immovable:** braced with stamina, take a `Troll` punch from the front. Expected: it does not push you back;
  from behind it does; with stamina at one block or less, the punch that breaks your guard pushes you. With
  `BraceKnockbackResistPercent = 0`: blocked punches push you again (less than unblocked ones).
  *Automated: `tower.wall`, `tower.block`.*
- [x] **T23 New Game+:** on a throwaway world, `setkey WorldLevel 1`, then `spawn ShieldIronTower 1 1 p`. Expected:
  its tooltip blunt is 12 (no +120). A `Draugr` still staggers in one bash, and the bash shows almost no damage. Crouch
  up behind an unaware `Draugr`, bash it, then hit it with a sword right away (quick swap). Expected: the Draugr turns
  to fight after the bash; the sword hit is not a sneak attack (no sneak-attack effect; the critical hit effect of a
  staggered target still shows). `removekey WorldLevel` afterwards.
  *Automated: `tower.ng`, `tower.follow`.*
- [x] **T24 Braced HUD:** hold block with a tower shield. Expected: a status icon with the tower shield's picture and
  "Braced"; it disappears when you release block; when stamina is low it flashes "Braced (exhausted)".
  *Automated: `tower.block`, `tower.slow`.*
- [x] **T25 Stagger resistance from behind:** braced, let a `Greydwarf` hit you from behind. Expected: full damage,
  but little stagger.
  *Automated: `tower.wall`.*
- [ ] **T26 Brace ends with the tower shield:** while blocking, unequip the tower shield from the inventory. Expected:
  the Braced icon is gone and you move at normal speed. Die while blocking. Expected: after respawn no Braced icon,
  normal speed.
  *Partly automated (`tower.block`); by hand: Die while blocking: after respawn no Braced icon and normal speed (the
  probe player is never killed).*
- [x] **T27 No parry adrenaline:** with `ShieldGoldTower`, block right as a `Greydwarf` hits. Expected: adrenaline
  rises by the normal block amount (2), never by 15.
  *Automated: `tower.parry`.*
- [x] **T28 Put away:** put the tower shield away on your back (R), jog and sprint. Expected: normal speed; draw it:
  slow again.
  *Automated: `tower.speed`, `tower.slow`.*
- [ ] **T29 Block push:** block `Greydwarf` and `Draugr` hits with `ShieldIronTower`. Note whether the attacker ends
  up beyond bash range (1.8 m) after the push. Repeat with `BlockForcePercent = 50` and note the difference.
  *Partly automated (`tower.push`); by hand: Read the noted distances (or try it) and judge whether attackers stay
  within bash range; the item asks for a note, not a fixed outcome.*
- [x] **T30 Heavy armor sprint:** wear the heaviest armor you have (its tooltips show "Movement" lines), tower shield
  in hand. Expected: sprinting is never slower than jogging.
  *Automated: `tower.speed`, `tower.slow`.*
- [x] **T31 Pickup with empty hands:** with both hands empty, pick up a tower shield. Expected: it goes to the
  inventory, not into your hands.
  *Automated: `tower.hands`.*
- [x] **T32 Training:** `spawn piece_TrainingDummy` and bash it (it strikes back and pushes you: bash it from its side
  or from behind). Expected: the Blocking skill rises.
  *Automated: `tower.train`.*
- [x] **T33 Bash cooldown:** hold the attack button with the tower shield. Expected: one bash every 2 s, not one right
  after another. Press attack once, right after a bash's swing is over (about 1.3 s after you pressed for it).
  Expected: the next bash starts on its own 2 s after that bash started, not earlier and not never, and the waiting
  press costs no stamina until it starts. With `BashCooldown = 0`: the bashes follow each other as soon as the swing
  ends.
  *Automated: `tower.bash`, `tower.train`.*
- [ ] **T34 Slow swing:** bash with `BashAnimationSpeed = 1`, then with the default 0.6. Expected: at 0.6 the swing is
  visibly slower and its hit (damage number, stagger) comes later, about 0.8 s after the press. Try 0.5 and 0.7 and
  note which looks best. With `BashAnimation = Kick` the kick is slowed too. After a bash, jogging, blocking and other
  attacks play at their normal speed.
  *Partly automated (`tower.guard`, `tower.bash`); by hand: Try 0.5 and 0.7 and say which looks best; that the slower
  swing looks right.*
- [ ] **T35 Stagger cost against a buckler:** `god` on, Blocking 0. Bash a `Draugr` (it staggers in one bash) and
  watch the stamina bar: about 20 per bash, also when the bash misses. Then with `ShieldIronBuckler`, parry a `Draugr`'s
  swings (raise the shield just before each hit) until one staggers it. Note how many tries it took and what the
  missed ones cost in stamina and health, and whether staggering with the bash still feels easier than with the
  buckler.
  *Partly automated (`tower.train`, `tower.bash`, `tower.follow`); by hand: The buckler half: parry a Draugr with
  ShieldIronBuckler, note tries and costs, and judge whether the bash still feels easier.*
- [x] **T36 One stagger per bash in a group:** `ShieldIronTower`, Blocking 0, `spawn Draugr 1 1` twice and let both
  come at you side by side. Face one and bash when both are in front of you (within about 1.8 m). Expected: both take
  the bash's damage number and are pushed, but only the one you face staggers. Bash again 2 s later at the same one
  (it is in its 8 s limit). Expected: neither staggers. Turn to face the other and bash. Expected: it staggers. Same
  with three `Greydwarf` at once: one bash staggers one of them.
  *Automated: `tower.group`, `tower.lock`.*
- [!] **T37 Kept press ends with the tower shield:** set `BashCooldown = 6`. Bash, press attack once right after the
  swing is over, wait a second, then unequip the tower shield from the inventory. Another time, equip `SwordIron`
  instead. Expected: nothing swings by itself, no punch and no sword swing: the press was waiting for the next bash
  only. (A press made less than half a second before the switch may still start an attack, as in the normal game.)
  **FAILED:** automated test: A press kept for the bash cooldown starts a punch or a sword swing once the tower shield
  leaves the hand (self-test `tower.bug.keptpress`)
- [x] **T38 Tower shield leaves the list mid-bash:** set `BashAnimationSpeed = 0.3` (a slow swing), bash a `Troll`
  and, during the swing, remove `ShieldIronTower` from `Towers` (on a server the host does it). Expected: no hit and
  no damage number from that bash; the shield in your hand is a normal one-handed shield at once (no "Two-handed" on
  its tooltip, normal block armor), the Braced icon is gone, the rest of the swing plays at its normal speed, and the
  log has no "...; using ..." Warning about the bash animation. (Hard to time by hand: the `tower.switch` self-test
  checks the same case, so mark this `[-]` if you cannot make it.)
  *Automated: `tower.switch`.*
- [x] **T39 No parry adrenaline, with a trinket:** as T27, but wear any trinket first (for example
  `TrinketBronzeHealth`, a name from Trinkets On Demand's `TESTING.md`), with its adrenaline bar not full. Without a
  trinket the bar has no capacity, no block adds adrenaline, and T27 shows no change at all. Expected: each block with
  `ShieldGoldTower`, also one timed right as the `Greydwarf` hits, raises adrenaline by the normal block amount (2 at
  the default world adrenaline rate), never by a parry amount; the Greydwarf is never staggered by the block.
  *Automated: `tower.parry`.*
- [x] **T40 Own Towers list:** set `Towers = ShieldIronTower:12, ShieldBanded:7, SwordIron:5, NoSuchItem`. Expected:
  the log has one Warning for the sword ("The Towers setting names items that are not shields, ignored: SwordIron.
  ..."), one for the unknown name ("... names items this game does not have (MISSING), ignored: NoSuchItem.") and one
  for the round shield ("... names shields that can parry: ShieldBanded. As tower shields they can no longer parry.").
  `ShieldBanded` is now two-handed, its tooltip says "Cannot parry" and lists 7 blunt, and a block timed right as a
  `Greydwarf` hits no longer staggers it. The tower shields left out of the list (`ShieldWoodTower`) are normal
  shields again. Put the default list back. Expected: `ShieldBanded` is a round shield that parries again.
  *Automated: `tower.rules`, `tower.warn`, `tower.parry`.*
- [x] **T18 Live toggle:** hold a tower shield, then Esc → MC Mods → untick Tower Shield Wall. Expected: it becomes a
  one-handed shield in place (tooltip: no "Two-handed", normal block armor), you can add a sword, attack punches, the
  Braced icon is gone; tower shields in a chest and on the ground are normal too. Tick it again while holding a sword
  and a tower shield. Expected: the sword is put away and the tower shield is two-handed again.
  *Automated: `tower.switch`, `tower.toggle`.*
- [x] **T18b Toggle mid-bash:** set `BashAnimation = Kick` (slow hit), bash a `Troll` and untick `Enabled` in
  ConfigurationManager (F1) before the kick lands (about 0.9 s at the default speed: hard to time by hand; the
  `tower.toggle` self-test checks the same case, so mark this `[-]` if you cannot make it). Expected: no hit, no
  damage number; the rest of the kick and the next attacks play at their normal speed.
  *Automated: `tower.switch`, `tower.toggle`.*
- [ ] **T19 `Enabled = false` + restart:** set `Enabled = false` in the config file, start the game. Expected: normal
  tower shields (one-handed, normal block armor, no bash); the MC Mods panel says "Off (disabled in settings)."
  *Partly automated (`tower.mp.toggle`, `tower.mp.join`, `tower.switch`); by hand: The start itself: set Enabled =
  false in the config file, start the game, and check normal tower shields and the panel text (the test runs cannot
  start the game with one mod turned off).*
- [ ] **T20 Clean log:** play through the items above with `./tools/Watch-Log.ps1 -Mine` running. Expected: no error
  or exception from Tower Shield Wall, and no Towers warning with the default settings.
  *Partly automated (`tower.log`, `tower.warn`); by hand: The log during hand play of the items that stay manual.*

## 0.1.0 — multiplayer

- [x] **M01 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod"; tower shields are normal for you.
  *Automated: `probe.mp.baseline`, `tower.mp.no-server`.*
- [x] **M02 Player without the mod refused:** a friend without the mod joins a server (or host) with it. Expected:
  about a second after joining their game goes back to the menu with "Incompatible version", and the server log says
  "Refused <name>: their game does not have the mod. ...". With `AllowPlayersWithoutMod = true` on the server they may
  play (Warning in the server log: "... joined, but their game does not have the mod. ...").
  *Automated: `probe.mp.refused`, `probe.mp.off.MC.Combat.Shields.TowerWall`, `scenario:open-server`,
  `tower.mp.join`.*
- [x] **M03 Server rules for everyone:** the host sets `BlockArmorMultiplier = 4` and `BashStagger = 20`. Expected:
  the client's tooltips and bash use them; the client's log says "Using the server's tower shield rules"; the client's
  own file is unchanged and applies again in single player.
  *Automated: `tower.mp.rules`, `tower.rules`.*
- [ ] **M04 Remote view:** player B watches player A. Expected: the tower shield in A's left hand, on A's back when put
  away, the bash animation of each option, the block pose.
  *Partly automated (`tower.mp.view`, `tower.hands`); by hand: A second player watching: the shield on the arm and on
  the back, each bash animation and the block pose as they look on their screen.*
- [ ] **M05 Bash on another game's creature:** B arrives first so B's game controls the creatures; A bashes a `Draugr`
  with B standing next to it. Expected: it staggers as in T13 (the stagger does not shrink with two players nearby).
  *Partly automated (`tower.mp.creature`); by hand: Everything until the fixed test has passed once. After that still:
  the same with a second player standing next to it (the stagger must not shrink with two players nearby), since only
  one player exists in the run.*
- [ ] **M06 Hand-off to a player without the mod:** with `AllowPlayersWithoutMod` on, give a tower shield to a friend
  without the mod. Expected: for them it is a normal one-handed tower shield (with a sword, no bash, normal block
  armor); dropped back to you it is two-handed again.
  *Partly automated (`tower.mp.view`, `scenario:open-server`); by hand: A real player without the mod
  (AllowPlayersWithoutMod on) using the handed-over shield: one-handed with a sword, no bash, normal block armor.*
- [ ] **M07 Dedicated server:** install on the server and on both players. Expected: M02, M03 and M05 behave the same.
  *Partly automated (`probe.mp.refused`, `tower.mp.join`, `tower.mp.rules`, `tower.mp.creature`); by hand: The M05
  part with two players on the dedicated server (and M05 itself until tower.mp.creature has passed once).*
- [ ] **M08 PvP:** bash a friend with PvP on. Expected: their round shield blocks it, their dodge avoids it; a braced
  tower shield player blocks it with little stagger and is not pushed.
  *Partly automated (`tower.wall`, `tower.data`); by hand: Real PvP between two players: a round shield blocks the
  bash, a dodge avoids it, a braced tower player is not pushed.*
- [x] **M09 Server toggles live:** the host unticks the mod while the client braces with a tower shield. Expected: the
  client's tower shield is one-handed at once, the Braced icon and the slowdown are gone. The host ticks it again.
  Expected: the client's tower shield is two-handed again with the host's rules, without rejoining, and the client is
  not refused (its own copy stayed on).
  *Automated: `tower.mp.toggle`, `probe.mp.server-toggle`.*
- [x] **M10 Live rules push:** the host changes `BashStagger` and `CarrySlowPercent` while the client holds a tower
  shield. Expected: the client's tooltip and speed change within about a second, without re-equipping.
  *Automated: `tower.mp.rules`.*
- [ ] **M11 Different own Towers list:** the client equips `SwordIron` and `ShieldBanded` (default list), quits to the
  main menu, adds `ShieldBanded` to its own `Towers` in the config file, then joins the host (default list). Expected:
  both stay equipped at join; `ShieldBanded` stays a one-handed shield there.
  *Partly automated (`tower.mp.rules`, `tower.rules`); by hand: The real sequence from the main menu: edit the config
  file, join with the saved character, and see both items still equipped.*
- [x] **M12 Mod turned off on a client refused:** a friend with the mod unticks it in MC Mods, then joins. Expected:
  refused ("Incompatible version") about a second after joining; the server log says "their game has the mod turned
  off". A friend who unticks it while connected: refused about a second later. A friend who unticks it and ticks it
  again within that second: stays. With `AllowPlayersWithoutMod = true` both may play with normal tower shields
  (Warning in the server log).
  *Automated: `probe.mp.off.MC.Combat.Shields.TowerWall`, `tower.mp.join`, `tower.mp.toggle`.*
- [ ] **M13 Two tower shield players:** A and B both bash the same `Draugr` in turn. Expected: it is not kept
  staggered (the 8 s limit applies to both).
  *Partly automated (`tower.mp.creature`, `tower.lock`); by hand: Two tower shield players bashing the same Draugr in
  turn (and the cross-game part until tower.mp.creature has passed once).*
- [ ] **M14 Other network version refused:** build a copy with another network version (`dotnet build
  src/Combat/Shields.TowerWall/MC.Combat.Shields.TowerWall.csproj -p:ModNetworkVersion=3 -p:DeployToGame=false`), copy
  `src/Combat/Shields.TowerWall/bin/Debug/MC.Combat.Shields.TowerWall.dll` over the friend's copy (put the normal build
  back afterwards: build again without `-p:ModNetworkVersion=3` and copy it the same way). The friend joins. Expected:
  refused ("Incompatible version") about a second after joining; the server log says "their game has another version
  of the mod (network version 3, the server has 2)". With `AllowPlayersWithoutMod = true` they may play: their MC Mods
  panel says the versions cannot talk to each other, and their tower shields are normal. (A friend still on the first
  test build, network version 1, is refused the same way.)
  *Partly automated (`tower.mp.join`, `tower.rules`); by hand: A real client built with another network version:
  refused on its screen; with AllowPlayersWithoutMod its panel text and its normal tower shields.*
- [ ] **M15 Ally's spell:** PvP off. Player A holds block with `ShieldIronTower` facing player B; B casts the Staff of
  Protection (`StaffShield`) next to A. Expected: A gets the protective shield effect; no "Blocked" text appears on A
  and A's Blocking skill does not rise from it. Repeat with PvP on for both. Expected: the same.
  *Partly automated (`tower.rules`); by hand: A second player casting the Staff of Protection next to the braced
  player, PvP off and on: the bubble is applied, no 'Blocked' text, no Blocking skill gain.*
- [ ] **M16 Joining with a weapon and a tower shield in hand:** untick Tower Shield Wall in MC Mods, hold `SwordIron`
  and `ShieldIronTower` together, quit to the main menu, tick the mod again, join the host. Expected: only one of the
  two is equipped after joining (the game keeps the one it equips last, from the order it stored your items, as when
  loading a character with the mod on); the other is in the inventory; no error in the log.
  *Partly automated (`tower.equip`); by hand: The real join: untick the mod, hold both, quit, tick it, join the host,
  and check one stays equipped with no error in the log.*
- [ ] **M17 Remote players see the slow bash:** player B watches player A bash. Expected: B sees the same slow swing
  as A, with the hit when A's swing lands; the host sets `BashAnimationSpeed = 1` for comparison. Repeat with a friend
  without the mod (`AllowPlayersWithoutMod = true` on the host). Expected: the same slow swing.
  *Partly automated (`tower.mp.view`, `tower.bash`); by hand: A second player (and one without the mod) actually
  seeing the slow swing and the hit when it lands.*

## 0.1.0 — other mods

- [ ] **C01 Sort Chest, Crafting Search and Sort, Encyclopedia (MC):** sort a chest with tower shields and round
  shields, look at them in the crafting list and in the Encyclopedia. Expected: tower shields sort and group with
  shields, not with weapons.
  *Partly automated (`tower.data`); by hand: Sort a chest, open the crafting list and the Encyclopedia with the three
  mods and check tower shields sit with the shields.*
- [x] **C02 Crossbow Stays Loaded (MC):** load a `CrossbowArbalest`, equip a tower shield (the crossbow is put away),
  equip the crossbow again. Expected: still loaded.
  *Automated: `tower.mods.crossbow`.*
- [ ] **C03 Forge Idol Upgrades (MC):** refine a tower shield at the Forge of Potential (names in Forge Idol Upgrades'
  `TESTING.md`). Expected: still two-handed, block armor of its new quality ×2.5.
- [ ] **C04 Weapon Moveset (MC):** with a tower shield, jump and attack in the air; then roll and press attack in the
  second half of the roll (where a weapon's roll attack would flow out of the roll). Expected: the normal bash with its
  own stagger, no jump or roll attack; after the roll the bash starts only once the roll is over, it never cuts into
  the roll (Weapon Moveset's X03).
- [ ] **C05 Dual Wielding (MC):** with two one-handed weapons paired, equip a tower shield. Expected: both weapons are
  put away. With the tower shield, equip a one-handed weapon. Expected: the tower shield is put away (Dual Wielding's
  X03).
- [ ] **C06 Creature Morale (MC):** at boss rank 4, where Greydwarfs are afraid of you (Creature Morale's
  `ForceBossRank = 4` in a Debug build, or a character that helped kill Moder; stand in the Meadows: a Greydwarf
  spawned there still counts as a creature of its home biome, the Black Forest). An afraid `Greydwarf` runs away when
  it sees or hears you within 12 m (the game's red alert icon shows on its health bar while it runs): sneak up on it
  from behind (crouched, out of its sight), or catch it before you have stayed within 3 m of it for 2 s (after that it
  is cornered and fights back anyway), and bash it once. Expected: it stops running and attacks you (the alert icon
  stays, now because it fights you).
  In a New Game+ world (as T23), a bash on an afraid Greydwarf provokes it too, and a knife hit right after the bash is
  not a sneak attack (Creature Morale's X05).
- [ ] **C07 Other shield mods:** with ZenCombat, CaptainValheim, Goo's Combat Overhaul or ShieldBash installed.
  Expected: the log has one Warning ("Other mods that change shields, blocking or attacks are installed: ...") naming
  them.
  *Partly automated (`tower.warn`); by hand: With ZenCombat, CaptainValheim, Goo's Combat Overhaul or ShieldBash
  really installed: the Warning names them.*
- [ ] **C08 Sneak Ambush (MC):** crouch up to an unaware `Greydwarf` and bash it. Expected: no backstab, no
  sneak-attack XP; raising the tower shield ends the crouch. In a New Game+ world (as T23): bash an unaware creature,
  then hit it with `KnifeCopper` at once. Expected: no backstab and no Sneak XP (the bash alerted it) (Sneak Ambush's
  X02).
  *Partly automated (`tower.data`, `tower.follow`); by hand: With Sneak Ambush: no sneak XP for the bash, raising the
  shield ends the crouch, and the KnifeCopper case.*
- [ ] **C09 Harpoon Hooks Tames (MC):** hook a tame with the Abyssal Harpoon (`SpearChitin`), then switch to a tower
  shield and bash a wild `Greydwarf`. Expected: the tame still takes no damage from the harpoon; the bash staggers the
  Greydwarf as in T13.
  *Partly automated (`tower.follow`, `tower.bash`); by hand: Hook a tame with the Abyssal Harpoon first and check it
  still takes no harpoon damage after switching to the tower shield.*
- [ ] **C10 Creature Kill and Tame Counts (MC):** kill a `Greyling` with bashes only. Expected: its line gains one
  melee kill.
  *Partly automated (`tower.creature`); by hand: The line in Creature Kill and Tame Counts gaining one melee kill (the
  probe player is in god mode, which marks kills as cheated).*
