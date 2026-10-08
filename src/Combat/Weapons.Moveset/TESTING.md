# Weapon Moveset — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0 with the roll flow (user feedback of 2026-09-30: the roll attack flows out of the roll;
network version 2), build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). The build before
the roll flow passed the smoke test (`./tools/Test-Smoke.ps1`, 2026-09-30: loads, patches cleanly, JitCheck clean) and
the in-world self-tests (`./tools/Test-InWorld.ps1 -Mod Weapons.Moveset`, 2026-09-30, a full run with every MC mod, 71
of 71 tests): `moveset.triggers`, `moveset.rules`, `moveset.jump`, `moveset.jump-input`, `moveset.roll`,
`moveset.roll-input`, `moveset.aim`, `moveset.stamina`, `moveset.exclusions`, `moveset.watchdog`, `moveset.order`.
**The roll-flow build (with its review fixes: default FlowStart 0.85, a press after the roll is a roll attack only
while the roll still blends out, no cut without a known attack state, a 0.2 s margin after the invulnerability) ran
in the in-world self-tests on 2026-09-30 (full run, 66 of 71 tests): every moveset test passed except `moveset.roll`
(the first roll attack with a one-handed axe, a two-handed sword, an atgeir or a knife played after the roll: no
animation state known yet, none is named after its animation) and `moveset.roll-input` (a test bug: it read the
game's cached "in attack" flag and took the first swing for a second one). The fix (the attack state is now read from
a hidden copy of the animation controller, so the first roll attack of a session flows too) has only been compiled so
far: run `./tools/Test-InWorld.ps1 -Mod Weapons.Moveset` again before testing by hand** (`moveset.triggers` checks
the controller copy against the live animations, `moveset.roll` forgets every known state first and expects each
weapon type's first roll attack to flow; their NOTE lines give the copy's cost). First hands-on test (build
3cda33b+dirty, before the roll flow): the user reported the stop between the roll and the roll attack, which this
build removes.

**Automated checks (2026-10-08):** 50 of 50 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (it does not change stamina costs), `ghost` makes creatures ignore you, `heal` refills health and
stamina, `killall` removes nearby creatures. Give yourself items with `spawn <name>` (Tab autocompletes; add `p` to put
it straight into your inventory: `spawn SwordIron p`). Names checked in the 1.0.16 game data:

- weapons, one per weapon type: `SwordIron`, `MaceIron`, `Club`, `AxeIron`, `Battleaxe`, `AxeBerzerkr` (dual axes),
  `THSwordKrom` (two-handed sword), `AtgeirIron`, `KnifeCopper`, `KnifeSkollAndHati` (dual knives), `SpearBronze`,
  `SledgeIron`, `FistFenrirClaw` (fist weapon); bare hands = nothing in the right hand;
- items that must stay vanilla: `Torch`, `PickaxeIron`, `Scythe`, `Bow`, `CrossbowArbalest`, `StaffFireball`,
  `SpearChitin` (the harpoon), `BombOoze`, `Tankard`, `Hammer`, `ShieldWoodTower`, `ShieldBronzeBuckler`;
- targets: `Greyling`, `Boar`, `Skeleton`, `Troll`, `piece_TrainingDummy` (it strikes back and pushes you: attack it
  from its side or from behind; it never staggers).

Not in the checked data **(unverified)**: the ammo `ArrowWood` and `BoltBone` (the `moveset.triggers` self-test notes
whether they exist; Tab autocomplete shows the names), and Sneak Ambush's Smoke Screen `MC_SmokeScreen` (only with
that mod). Most expected results name Debug log lines: turn on Debug logging with
`./tools/Setup.ps1 -DevBepInExConfig` and follow the log with `./tools/Watch-Log.ps1 -Mine`. For a low frame rate, type
`maxfps 20` in the console (it changes the graphics setting: put your usual limit back afterwards). Settings are changed with
ConfigurationManager (F1) or in `BepInEx/config/MC.Combat.Weapons.Moveset.cfg`; put each one back to its default after
the item that changed it. Use a world with the default stamina world modifier (a lowered one hides stamina costs).

## 0.1.0 — single player

- [x] **T01 Jump attack, sword:** with `SwordIron`, jump and attack in the air. Expected: the sword's third combo
  swing (finisher animation) plays; Debug "Jump attack: SwordIron (Swords) plays swing_longsword2; damage x1.2,
  stagger x2, push x1, stamina x1, aim 30°; own settings." then "Jump attack swing_longsword2 started after … s; the
  next attack starts the combo at its first swing."; the hit lands (in the air or right after landing); the next
  attack is the normal first swing.
  *Automated: `moveset.lines`, `moveset.jump`.*
- [x] **T02 Jump attack, every weapon type:** jump and attack with `MaceIron`, `Club`, `AxeIron`, `Battleaxe`,
  `AxeBerzerkr`, `THSwordKrom`, `AtgeirIron`, `KnifeCopper`, `KnifeSkollAndHati`, `FistFenrirClaw` and bare hands.
  Expected: each plays its default animation of the README table from the jump (Debug "Jump attack: … plays …"), no
  attack press is lost, no "did not start" warning. `SpearBronze` and `SledgeIron`: a normal swing, no "Jump attack"
  Debug line.
  *Automated: `moveset.jump`, `moveset.settings`.*
- [x] **T03 One per jump:** jump from a height (a roof or a cliff) and attack twice before landing. Expected: the first
  attack is a jump attack, the second a normal air swing (only one "Jump attack" Debug line).
  *Automated: `moveset.air`.*
- [x] **T04 No jump, no jump attack:** walk off a ledge and attack in the air; then jump, land, and attack. Expected:
  both are normal swings, no "Jump attack" Debug line.
  *Automated: `moveset.jump-input`.*
- [x] **T05 Aim:** with `KnifeCopper` (fast hit, still in the air), jump next to a `Boar` and look down at it while
  attacking. Expected: the swing sweeps down and hits it. Then the level swing after landing: with `THSwordKrom` (its
  jump attack hits later than most), stand about 2.5 m from the side of the `piece_TrainingDummy`, jump in place, look
  at the dummy's feet and press attack just before you land, so the jump attack starts in the air and hits after
  touchdown. Expected: Debug "Jump attack: THSwordKrom …"; the hit comes after the landing and strikes the dummy, not
  the ground (a swing still tilted down would stop in the ground less than 2 m ahead). If the hit still comes in the
  air, press later. (The angles themselves, and the tilt that never passes 45 degrees, are checked by the
  `moveset.aim` self-test.)
  *Automated: `moveset.aim-hit`, `moveset.aim`.*
- [x] **T06 Roll attack, sword:** with `SwordIron`, roll (block + jump, or crouch + jump) and press attack in the
  second half of the roll. Expected: the second combo swing grows straight out of the end of the roll, with no
  standing up to idle in between; Debug "Roll attack: SwordIron (Swords) plays swing_longsword1; damage x1.3, stagger
  x1.5, push x1, stamina x1; cut into the roll 0.86 s after it started (cross-fade from the roll, …); own settings."
  Then hold attack through a roll. Expected: the same, never replaced by a first swing (no "was dropped before it
  started" Debug line). Then press right after a roll. Expected: a roll attack that still flows out of the end of the
  roll (Debug "… s after the roll ended (cross-fade from the roll, …)"). Repeat the hold case with `maxfps 20`.
  Expected: the same.
  *Automated: `moveset.lines`, `moveset.roll`, `moveset.roll-input`.*
- [x] **T07 Roll attack, every weapon type:** roll and attack with `MaceIron`, `Club`, `AxeIron`, `AxeBerzerkr`,
  `THSwordKrom`, `AtgeirIron`, `KnifeCopper`, `KnifeSkollAndHati`, `FistFenrirClaw` and bare hands. Expected: each
  plays its default roll animation of the README table, flowing out of the roll (Debug "Roll attack: … plays …; cut
  into the roll …"), no press lost, no "did not start" warning. `Battleaxe`, `SpearBronze`, `SledgeIron`: a normal
  swing that starts only once the roll is over, with the usual stand-up in between, no "Roll attack" line.
  *Automated: `moveset.roll`, `moveset.settings`.*
- [x] **T08 Window:** press attack 1 s after a roll ends. Expected: a normal swing. Set Roll attack `Window = 1.5`
  and do the same. Expected: still a normal swing, no "Roll attack" Debug line (after a normal roll a roll attack must
  flow out of it; Window only lengthens the time after a dash from a dash mod, X07).
  *Automated: `moveset.roll-input`.*
- [x] **T09 Numbers:** `raiseskill Swords 50` (steadier damage), set Roll attack `StaggerMultiplier = 10`, roll
  attack a `Troll` with `SwordIron`. Expected: it staggers from one roll attack (never from one normal swing). Set Roll attack `DamageMultiplier = 3`, hit the
  `piece_TrainingDummy`. Expected: the roll attack's damage number is about three times a normal swing's (damage
  varies a little from hit to hit). Set Roll attack `PushMultiplier = 5`, roll attack a `Greyling` with the `Club`
  (weak, so it survives). Expected: it flies back much further than from a normal swing.
  *Automated: `moveset.numbers`.*
- [x] **T10 Combo:** (a) a secondary attack after a jump or a roll. Expected: the normal secondary, no move line. (b) A
  combo on the ground with no jump or roll (attack, attack, attack). Expected: the three normal swings, no move line.
  (c) With `SwordIron`, mash attack during a roll attack. Expected: the finisher follows (Debug "… the combo continues
  at step 2."), no press is lost. (d) After a jump attack, attack again. Expected: the first swing of the combo. (e)
  Set Roll attack animations `Knives = knife_secondary`, roll attack with `KnifeCopper`, then attack. Expected: Debug
  "… the next attack starts a new combo."; the next attack is the first stab.
  *Automated: `moveset.combo`, `moveset.after-roll`, `moveset.roll`, `moveset.jump`.*
- [x] **T11 Excluded items:** with `Torch`, `PickaxeIron`, `Scythe`, `Bow` (+ `ArrowWood`), `CrossbowArbalest`
  (+ `BoltBone`), `StaffFireball`, `BombOoze` and `SpearChitin` (and Sneak Ambush's Smoke Screen, if installed),
  attack after a jump and after a roll. Expected: each item's own vanilla action, no move line. With
  `ShieldBronzeBuckler` in the left hand and nothing in the right hand: jump and roll attacks with fists.
  *Automated: `moveset.exclusions`, `moveset.exclusions-ranged`.*
- [x] **T12 Animation settings:** set Jump attack animations `Swords = greatsword2`, jump attack with `SwordIron`.
  Expected: the two-handed sword's third swing plays (Debug "plays greatsword2"; "the next attack starts a new
  combo"). Set it to `Off`. Expected: a normal swing, no "Jump attack" line. Close the game, type `swing_longsword9` as
  the value of `Swords` in the `[Jump attack animations]` section of the config file, start the game. Expected: the
  setting shows `swing_longsword2` and it is used; no error or warning.
  *Automated: `moveset.settings`, `moveset.triggers`.*
- [x] **T13 Stamina:** turn off any stamina cheat; `heal` to refill. Expected: a jump attack costs the jump plus one
  normal swing. Set Jump attack `StaminaMultiplier = 3`. Expected: the jump attack's swing costs three swings. With
  stamina for one swing but not three after the jump: a normal swing (Debug "Jump attack skipped: stamina for a
  normal swing only."), the stamina bar does not keep flashing. With too little stamina for any swing after the jump:
  the swing fails like vanilla (one flash) and there is no attack in that jump.
  *Automated: `moveset.stamina-jump`, `moveset.stamina`.*
- [x] **T14 Facing (unverified option label):** with the Gameplay option for attack direction off (the keyboard
  default), roll past a `Greyling` and roll attack while looking back at it. Expected: the strike turns to the camera
  and hits it. With the option on (the gamepad default): the same with the stick still held in the roll direction.
  Expected: the strike follows the roll. Stick pulled back toward the Greyling: it strikes it. No stick input: no turn.
  *Partly automated (`moveset.facing`); by hand: The name of the option in the Gameplay settings (unverified label)
  and a real gamepad stick: the test sets the game's flag and feeds the stick direction through the game's control
  method.*
- [x] **T15 Move switches:** set `JumpAttack = false` while playing. Expected: jump attacks stop at once, roll attacks
  still work. Still with `JumpAttack = false`, set Roll attack `Window = 2`, roll, jump right after the roll and attack
  in the air. Expected: a normal swing (no "Roll attack" Debug line: the jump owns the air attack). Put both back, then
  set `RollAttack = false`. Expected: roll attacks stop at once, jump attacks work.
  *Automated: `moveset.settings`.*
- [x] **T16 Cooldown:** default settings: roll attack, roll again as soon as the swing ends, attack. Expected: a roll
  attack (a normal roll takes longer than the 1 s cooldown). Set `Cooldown = 5` and do the same. Expected: the second
  attack is a normal swing (Debug "Roll attack skipped: cooldown (… s left)."). Set `Cooldown = 0`; with `KnifeCopper`,
  jump attack, land, jump again at once and attack. Expected: a jump attack.
  *Automated: `moveset.cooldown`.*
- [x] **T17 Gamepad layouts:** gamepad on the Alternative1 or Alternative2 layout: crouch (or hold block), press jump.
  Expected: the player jumps and the crouch ends; attacking in the air gives a jump attack. On the Default layout the
  same press rolls (and the attack after it is a roll attack).
  *Partly automated (`moveset.gamepad`); by hand: A real gamepad and choosing the layout in the settings menu.*
- [x] **T18 Roll while a move starts:** with `SwordIron`, press attack just as a roll ends (while the character is
  still getting up) and at once roll again (block + jump). The timing is very tight now that roll attacks start at
  once: try several times. Expected: either the roll attack plays, or the second roll plays with no swing (Debug "Roll attack swing_longsword1
  was dropped before it started (a roll started); trigger reset."); never a swing that plays late after the second
  roll, and no "did not start" warning.
  *Automated: `moveset.interrupt`, `moveset.roll-input`.*
- [x] **T19 Roll flow settings:** set Roll attack `FlowStart = 0`. Expected: the roll attack starts 0.2 s after the
  invulnerability of the roll ends (about 0.6 s in; Debug "cut into the roll 0.6… s after it started"). Set
  `FlowStart = 2`. Expected: the swing starts as the roll ends (Debug "… s after the roll ended"), still with no
  stand-up in between. Set `FlowBlend = 0`: an instant switch from the roll to the swing; `FlowBlend = 0.4`: a slow
  blend, the hit at the same moment as with the default. Put both back and judge the default look (0.85 / 0.15), then
  `FlowStart = 0.7` (snappier, cuts the end of the roll): note what feels best. With each FlowStart, roll along a
  straight line of floor marks (or next to a wall) with and without a roll attack: note how much less ground the roll
  covers when a roll attack cuts it.
  *Partly automated (`moveset.flow`, `moveset.roll-input`); by hand: Judging the look: the default blend, FlowStart
  0.7 against 0.85, "what feels best", and comparing roll distances by eye.*
- [x] **T20 No invulnerability in a roll attack:** without `god`, let a `Greyling` or a `Skeleton` attack you, roll
  through its swing and roll attack at once, several times. Expected: a hit that lands during your roll attack's swing
  damages you (the invulnerability of the roll always ends before the swing starts).
  *Automated: `moveset.iframes`, `moveset.roll`.*
- [x] **T21 Other attacks after a roll stay vanilla:** hold attack through a roll with a `Battleaxe` (its roll attack
  is off), then with `SwordIron` and `RollAttack = false`, then press the secondary attack during a roll with
  `SwordIron`. Expected each time: the attack starts only once the roll is over, with the usual stand-up in between,
  and no "Roll attack" Debug line.
  *Automated: `moveset.after-roll`, `moveset.roll`.*
- [x] **T22 Late press after a roll:** with `SwordIron`, roll without pressing anything, then press attack just as the
  roll ends. Expected: a roll attack that grows out of the end of the roll (Debug "Roll attack: … s after the roll
  ended (cross-fade from the roll, …)"). Roll again and press about a third of a second after the roll ends, once the
  character has stood up. Expected: a normal swing, no "Roll attack" Debug line. Try presses at several moments in
  between. Expected: each is either a roll attack flowing out of the roll or a normal swing; never the roll, the
  stand-up, then a roll attack.
  *Automated: `moveset.roll-input`, `moveset.lines`.*
- [x] **T23 First roll attack of a session:** start the game, load the world, and before any other attack equip
  `AxeIron`, roll and press attack. Expected: at the start of the roll Debug "Found the animation state of swing_axe1
  in the animation controller (… ms)."; the roll attack flows out of the roll like T06 (Debug "… cut into the roll …
  (cross-fade from the roll, state from the animation controller) …"), with no hitch you can feel. Do the same with
  `THSwordKrom`, `AtgeirIron` and `KnifeCopper` (each one's first roll attack of the session, no swing with it
  before). Expected: the same for each. Log: no error and no warning from Weapon Moveset.
  *Partly automated (`moveset.roll`, `moveset.triggers`); by hand: A real fresh game start (the test forgets the
  states instead of restarting) and "no hitch you can feel" on that first roll attack.*
- [x] **T24 Roll off a ledge:** with `SwordIron`, roll off a ledge about 3 m high without jumping, three ways. (a) Hold
  attack (or press it late in the roll), so that the ground ends while the roll still plays. Expected: a roll attack cut
  into the roll (Debug "… cut into the roll … (cross-fade from the roll, …)"), in its animation within two or three
  ticks and played in the air as a level swing (the downward tilt belongs to jump attacks only). If the fall ends the
  roll's animation before the cut point, the attack is instead a roll attack that flows out of the roll's last blend,
  or a normal swing. (b) Press attack right as the roll ends in the air. Expected: while the character still blends out
  of the roll, a roll attack that flows out of it (Debug "… s after the roll ended (cross-fade from the roll, …)").
  (c) Press once the character has left the roll's last blend (about 0.3 s after the roll ended), still in the air.
  Expected: a normal swing, no "Roll attack" Debug line. In every case: never a jump
  attack (no "Jump attack" Debug line), never the roll, then a fall or stand-up pose, then a roll attack, and no "did
  not start" warning. A roll attack never starts while the roll's invulnerability is on, nor less than 0.2 s after it
  ended, even when the ground ends early in the roll. (The `moveset.ledge` and `moveset.ledge-iframes` self-tests lift
  the player 3.5 m during a roll and note what the roll's animation does in the air.)
  *Automated: `moveset.ledge`, `moveset.ledge-iframes`.*
- [x] **T25 Roll attack out of a sneak roll:** with `SwordIron`, crouch, then roll (crouch + jump) and press attack in
  the second half of the roll; do it twice in a row. Expected: a roll attack that flows out of the roll like in T06,
  in its animation within two or three ticks: either cut into the roll (Debug "Roll attack: … cut into the roll …
  (cross-fade from the roll, …)", no idle pose before the swing) or, if a roll from a crouch ends before the cut point,
  out of the roll's last blend (Debug "… s after the roll ended (cross-fade from the roll, …)"); never the roll, a
  pose, then the roll attack; the roll's invulnerability is over when the swing starts; no warning and no error from
  Weapon Moveset. (The `moveset.crouch-roll` self-test notes which of the two a roll from a crouch gives, next to a
  standing roll.)
  *Automated: `moveset.crouch-roll`.*
- [x] **L01 Live toggle:** in single player (or as the host), Esc → MC Mods → untick Weapon Moveset in the middle of a
  fight. Expected: jump and roll attacks stop at once (a move already playing finishes); normal swings only. Tick it
  again. Expected: the moves come back at once.
  *Automated: `moveset.toggle`, `moveset.mp.client-toggle`.*
- [x] **L02 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Combat.Weapons.Moveset.cfg` and
  start the game. Expected: `Status` reads "Off (disabled in settings).", combat is vanilla (no move Debug line).
  *Partly automated (`moveset.mp.client-toggle`); by hand: Starting the game with Enabled = false already in the
  config file (a real restart): Status line and vanilla combat.*
- [x] **L03 Clean log:** play a session with jump and roll attacks on every weapon type, including a death and a
  logout right after a move, while `./tools/Watch-Log.ps1 -Mine` runs. Expected: no error and no warning from Weapon
  Moveset, except those an item above provokes on purpose.
  *Partly automated (`moveset.log`); by hand: A death and a logout right after a move while watching the log.*

## 0.1.0 — multiplayer

- [x] **M01 Server settings:** a dedicated server (or a host) and two players, all with the mod. Expected: each player
  sees the other's jump and roll attacks, the roll attacks flowing out of the roll on both screens. Change the server's Jump attack `DamageMultiplier`. Expected: the clients log
  Info "Using the server's move settings: …" with the new value and their jump attacks hit harder; their own config
  files are unchanged. On a host, drag that setting's slider in ConfigurationManager for a few seconds. Expected: each
  client logs the "Using the server's move settings" line once, about half a second after you let go, not once per
  step of the drag.
  *Partly automated (`moveset.mp.settings`, `moveset.mp.join`, `moveset.mp.observer`); by hand: Two real players
  seeing each other's jump and roll attacks on their screens, and a real slider drag in ConfigurationManager on a
  host.*
- [x] **M02 Player without the mod refused:** a player without the mod joins. Expected: about a second after joining
  their game goes back to the menu with "Incompatible version"; the server logs a Warning "Refused <player>: their
  game does not have the mod, …". With `AllowPlayersWithoutMod = true` on the server they can play, and the server
  logs "Not refusing <player>: their game does not have the mod, but AllowPlayersWithoutMod is on. …". A player with
  the mod on joins normally (Debug "<player> has Weapon Moveset: allowed.").
  *Partly automated (`probe.mp.refused`, `scenario:open-server`, `probe.mp.off.MC.Combat.Weapons.Moveset`,
  `moveset.mp.join`, `moveset.rules`); by hand: That the server's Warning "Refused <player>: their game does not have
  the mod, ..." comes from Weapon Moveset when it is the mod doing the refusing (with every MC mod on the server
  another one may refuse first), and the "about a second" timing.*
- [x] **M03 Hand-off to a player without the mod:** with `AllowPlayersWithoutMod = true` and the server's Roll attack
  `StaggerMultiplier = 10`, a player without the mod stands next to a `Troll` (their game controls it), then the modded
  player arrives with `SwordIron` after `raiseskill Swords 50` (as in T09: at skill 0 the lowest damage rolls fall just
  short of the Troll's stagger bar). Expected: the player without the mod sees the modded player's moves with the
  normal game animations (a roll attack shows after the stand-up at the end of the roll: expected, only the look
  differs); the Troll staggers from one roll attack, never from one normal swing; in PvP the player
  without the mod takes a modded jump attack's damage.
  *Partly automated (`moveset.numbers`); by hand: A real second player without the mod: what they see (vanilla
  animations, roll attack after the stand-up), a Troll their game controls staggering, PvP damage.*
- [x] **M04 Server toggles:** the server (or host) turns the mod off. Expected: the clients' MC Mods panel shows
  "Inactive: the server has this mod turned off …", their moves stop. The server turns it back on. Expected: the
  clients' moves come back once the server's settings arrive, and a player without the mod who joined in the meantime
  is refused about a second later (every connected player is checked again).
  *Partly automated (`probe.mp.server-toggle`, `moveset.mp.server-toggle`); by hand: A real player without the mod who
  joined while the server had it off being refused about a second after it comes back on.*
- [x] **M05 Server without the mod:** a client with the mod joins a server without it. Expected: the MC Mods panel
  shows "Inactive: the server does not have this mod. …"; combat is vanilla.
  *Automated: `probe.mp.baseline`, `moveset.mp.vanilla-server`.*
- [x] **M06 Synced animation:** the server sets Jump attack animations `Swords = greatsword2`. Expected: a modded
  client's sword jump attack plays it (Debug "… plays greatsword2 …; server settings."), and a player without the mod
  (AllowPlayersWithoutMod on) sees the same animation.
  *Partly automated (`moveset.mp.settings`); by hand: A player without the mod seeing the same greatsword animation.*
- [x] **M07 Other network version refused:** a client with a test build whose `ModNetworkVersion` is 3 (or the first
  test build, network version 1) joins. Expected: refused about a second after joining ("Incompatible version"); the
  server's Warning says "has another version of the mod (network version 3, the server has 2)". With `AllowPlayersWithoutMod = true`: let in with a Warning naming the same
  reason; the client's MC Mods panel shows the version mismatch, and its moves are vanilla.
  *Partly automated (`moveset.mp.version`, `moveset.rules`); by hand: A real client build with another network
  version: refused about a second after joining ("Incompatible version") with the server's "Refused ..." Warning, and
  let in with AllowPlayersWithoutMod on.*
- [x] **M08 Mod turned off on the client:** (a) a client with `Enabled = false` joins. Expected: refused about a second
  after joining ("Incompatible version"); the server's Warning says "has the mod turned off". (b) A client with the mod
  on joins, then unticks it in the MC Mods panel. Expected: the server logs "<player> turned Weapon Moveset off on
  their game." and refuses them about a second later. (c) The same with `AllowPlayersWithoutMod = true`. Expected: not
  refused, a server Warning with that reason, their moves are vanilla; ticking it back on, the server logs "<player>
  turned Weapon Moveset on on their game.", the client logs "Using the server's move settings" again and its jump and
  roll attacks come back.
  *Partly automated (`probe.mp.off.MC.Combat.Weapons.Moveset`, `moveset.mp.client-toggle`); by hand: The server's
  Warning text when it refuses in (a) and (b) ("Refused <player>: their game has the mod turned off, ...").*
- [x] **M09 Other players' roll flow:** two players with the mod, A and B. A rolls and roll attacks next to B several
  times, with a few weapon types. Expected: on B's screen A's swing grows out of A's roll with no stand-up, at the same
  moment as on A's screen; B's log has Debug "Cross-faded A's roll attack … out of the roll (…)". A normal swing of A
  after a roll with a `Battleaxe` keeps the usual stand-up on both screens.
  *Partly automated (`moveset.mp.observer`, `moveset.roll-input`); by hand: A real second player's screen: the swing
  growing out of the roll at the same moment as on the roller's own screen (the dedicated server is only a stand-in
  for that second game, and it has no screen).*
- [x] **M10 No invulnerability on other players' games:** the server (or host) sets Roll attack `FlowStart = 0`. Player
  B stands next to a `Greyling` or a `Skeleton` first (B's game controls it and checks its hits); player A arrives
  without `god`, rolls through the creature's swing and roll attacks at once, several times. Expected: a creature hit
  that lands during A's roll attack's swing damages A (B's game no longer sees A invulnerable once the swing starts:
  the roll attack waits 0.2 s after the roll's invulnerability ends). Put FlowStart back.
  *Partly automated (`moveset.mp.early-cut`, `moveset.iframes`); by hand: A creature controlled by a real second
  player's game (one more network hop than the server) hitting player A during the roll attack.*
- [x] **M11 Roll attacks seen by a dedicated server:** a dedicated server with the mod; a player with the mod roll
  attacks with several weapon types while standing near the world centre, then again more than about 200 m away from
  it. Expected: the server log has no error and no warning from Weapon Moveset. A dedicated server creates no player
  objects anywhere (Valheim 1.0.17: it keeps its own area outside the world), so even with Debug logging on it logs no
  "Cross-faded <player>'s roll attack … out of the roll (…)" and no "Found the animation state of … in the animation
  controller (… ms)." line. Should it log them anyway (a server mod that makes the server simulate the world), both are
  harmless (note how many, and the time in the second line). The player sees their own roll attacks exactly as in T06.
  *Automated: `moveset.mp.server-log`.*

## 0.1.0 — other mods

- [x] **X01 Crossbow Stays Loaded (MC):** with a loaded `CrossbowArbalest`, jump or roll, then fire. Expected: a
  vanilla shot, the bolt is used, no move line. Switch to `SwordIron`, roll attack, switch back. Expected: the crossbow
  is still loaded.
  *Automated: `moveset.x.crossbow`.*
- [x] **X02 Dual Wielding (MC):** equip two `AxeIron` as a Dual Wielding pair. Expected: the jump attack plays
  `dualaxes3` and hits main hand then off hand (each axe loses durability once); the roll attack plays `dualaxes1` and
  strikes with the off hand, and the combo continues with `dualaxes2` (main, then off) and `dualaxes3` (main, then off),
  the same hands as Dual Wielding's X02. Set Jump attack animations `DualAxes = dualaxes2`: a jump attack is followed by
  `dualaxes3` (the pair's four-swing combo, not the axe's three). Two `KnifeCopper` as a pair: the `dual_knives`
  animations; the roll attack `dual_knives1` strikes with the off hand. The normal dual combo is unchanged; no errors.
  *Automated: `moveset.x.dualwield`.*
- [x] **X03 Tower Shield Wall (MC):** with a tower shield (`ShieldWoodTower`) and its bash, jump or roll, then bash.
  Expected: the normal bash (Tower Shield Wall's shield punch at its own, slower speed) with its own stagger, and a
  second bash only after its 2-second cooldown; no move Debug line (Tower Shield Wall's C04).
  *Partly automated (`moveset.x.towershield`); by hand: That the bash still plays at Tower Shield Wall's slower speed
  and staggers as its own C04 says.*
- [x] **X04 Sneak Ambush (MC):** (a) with Creature Morale off (or a character with no boss kills): sneak roll
  (crouch + jump) into a roll attack on an unaware `Greyling`. Expected: a backstab, sneak XP once. (b) Creature Morale
  on, with the boss kills that make a Meadows `Greyling` afraid of you (Eikthyr, The Elder and Bonemass: Creature
  Morale's rank 3), in the Meadows: a sneak roll attack on an afraid Greyling that faces you (from beyond 12 m, so it
  sees you before it runs; catch it or corner it). Expected: no backstab, no sneak XP (Creature Morale's rule). (c) The
  same afraid Greyling, approached from behind, unseen and quiet. Expected: a backstab. A jump ends crouching. Jumping
  or rolling with the Smoke Screen equipped, then attacking: the plain throw (Sneak Ambush's X04; Creature Morale's
  X08 is the same test).
  *Partly automated (`moveset.x.sneak`, `moveset.exclusions`); by hand: (b) and (c): the Creature Morale rank 3 cases
  (afraid Greyling that sees you: no backstab; approached from behind: backstab).*
- [x] **X05 Creature Morale (MC):** with the boss kills of X04 (b), jump attack an afraid `Greyling` (catch it or
  corner it). Expected: it fights back, like after a normal hit (Creature Morale's X08).
- [x] **X06 Harpoon Hooks Tames (MC):** throw `SpearChitin` after a jump or a roll. Expected: the vanilla harpoon throw
  and hook, no move line.
  *Partly automated (`moveset.exclusions-ranged`); by hand: The harpoon really thrown and hooking a creature after a
  jump or a roll.*
- [ ] **X07 Other mods (optional):** Goo's Combat Overhaul installed. Expected: Info "Goo's Combat Overhaul is
  installed: it has its own jump attack, so Weapon Moveset's jump attack stays off (the roll attack still works).", no
  jump attack from this mod, roll attacks work; with Roll attack `Window = 2`, a roll, a jump right after it and an
  attack in the air give no "Roll attack" Debug line (GCO's jump attack only); note its GUID from the Debug plugin list
  (the GUID the mod looks for is unverified). Quickstep installed: a dash then an attack is a roll attack, and a second
  dash right after gives a normal swing (cooldown).
  *Partly automated (`moveset.x.gco`, `moveset.rules`); by hand: The real Goo's Combat Overhaul (found by its GUID or
  name, its own jump attack) and a real dash mod such as Quickstep.*
- [x] **X08 Creature Kill and Tame Counts (MC):** kill a `Boar` with a `SwordIron` roll attack. Expected: it counts as
  a melee kill, like a normal swing.
  *Automated: `moveset.x.killcount`.*
- [x] **X09 Dual Wielding (MC), pair with its roll attack off:** two `AxeIron` as a Dual Wielding pair, Roll attack
  animations `DualAxes = Off`; hold (or press) attack through a roll. Expected: no roll attack and no "Roll attack"
  Debug line; no attack starts inside the roll, which plays its full length; the press is not lost: the pair's normal
  first swing starts as soon as the roll is over, after the usual stand-up, with the main hand, and the next swing
  strikes with the off hand as usual. No error and no warning from either mod. (For a pair the game's attack is refused
  only at its own start, a tick or two per roll at the default FlowStart: expected, the pair's weapon type is not known
  earlier.)
  *Automated: `moveset.x.dualwield-off`.*
