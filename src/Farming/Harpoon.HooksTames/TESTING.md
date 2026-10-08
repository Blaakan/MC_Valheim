# Harpoon Hooks Tames — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-09-29 (loads, patches cleanly, JitCheck clean: 171 methods, 0 failures). No
in-game test yet.

**Automated checks (2026-10-08):** 30 of 30 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

**Setup:** press F5 for the console → `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`);
use a test character. `spawn <Name>` puts the object 2 m in front of you, so walk to where you want a creature first.
`spawn SpearChitin` (Abyssal Harpoon). Tames: `spawn Boar`, `spawn Wolf`, `spawn Lox`, then `tame`. It tames **every**
tameable creature your game runs, not only nearby ones (`Tameable.TameAllInArea` ignores its radius), so spawn wild
creatures only **after** taming, and only when a test asks for one (`spawn Boar` again, `spawn Neck`, `spawn
Greyling`, `spawn Wolf`, `spawn Deer`). Wild creatures and your tames attack each other, so remove the wild ones when
the test is done. Cleanup: `killenemies` kills untamed creatures within 1000 m and spares tames (it also destroys
the loaded creature spawners). **Do not use `killall` or `killtame`** while you still need your tames: both kill every
loaded non-player creature within 1000 m, tames included, with a hit that has no attacker. `heal` refills stamina.
**Keep your PvP off unless a test says otherwise** (checkbox in the inventory screen, available after 10 s out of
combat). The hover text of a tamed creature reads `( Tame, ... )`, a wild one `( Wild, ... )`. A creature shows a
health bar for 60 s after you aimed at it, while it is within about 30 m. Kill counts: Tab → Valheim Compendium → **Player Statistics**, line `Enemy Kills` in the first
"Difficulty Category" block (with Creature Kill and Tame Counts installed, also its Creatures section at the top).

Names checked in the 1.0.16 game data (prefab list and English localization): `SpearChitin` (Abyssal Harpoon),
`Boar`, `Wolf`, `Lox`, `Neck`, `Greyling`, `Deer`, `SaddleLox` (Lox Saddle), `Karve`, `Bow` (Crude Bow),
`ArrowFlint` (Flinthead Arrow), `SpearFlint` (Flint Spear), `KnifeButcher` (Butcher Knife), `StaffSkeleton` (Dead
Raiser), `StaffShield` (Staff of Protection), `TrinketBronzeStamina` (Bronze Pendant), `Skeleton_Friendly`; messages
"harpooned", "released", "Line broke", "Target too far"; hover words "Tame", "Wild". **(unverified)**: that
`Skeleton_Friendly` spawns already tamed (otherwise summon one with `StaffSkeleton`; its hover text tells); that
equipping the Bronze Pendant is what lets you gain adrenaline, and whether the throw itself gives some (T11); that the
Staff of Protection bubble reaches tames (T15); how far a lox can be dragged (T03); the harpoon's line values (30 m
limit, 4 m break distance, stamina drain) are code defaults that the game data may override; the internal names in
the Debug lines (`$enemy_boar`, `$enemy_wolf`, `$enemy_lox`: these localization keys exist, their use by the
creatures is assumed).

Keep `./tools/Watch-Log.ps1 -Mine` open. With Debug logging (`./tools/Setup.ps1 -DevBepInExConfig`):
- every hooked tame logs one `Hooked tame <name> (simulated by this game|another player): no damage, push or stagger.`;
- a cleared kill-credit mark logs `Cleared the attacker mark a harpoon hook left on <name>.`;
- a harpoon that flies past a tame logs `Harpoon passed tame <name>: <reason>.` (the only reason left is `a player is
  riding it`: every other tame is hooked, whatever it is doing);
- an extra hit from another mod's on-hit effect, stopped on the hooked tame, logs `Blocked an extra hit on hooked tame
  <name> during the harpoon hit.` (C05).
`<name>` is the creature's internal name, for example `$enemy_boar` (unverified, see above).
The vanilla log line `Setting attacker ...` is normal.

## 0.1.0 — single player

- [x] **T01 Hook a tame:** tamed Boar 10-20 m away, PvP off, throw the harpoon at it.
  Expected: message "Boar harpooned", the line appears, no damage number, the health bar stays full (walk within
  10 m to see it), the boar is not pushed back and does not run away or turn on you.
  *Automated: `harpoon.hook`, `harpoon.throw`.*
- [x] **T02 Drag and release:** after T01 walk away. Expected: the boar is dragged behind you and your stamina drains
  while it pulls; hold block for a moment after 2 s: "Boar released". Hook it again and run far away: "Line broke".
  Hook it again and let your stamina run out: "Boar released".
  *Automated: `harpoon.drag`.*
- [ ] **T03 Heavy tame:** hook a tamed Lox and walk away; try to drag it at least 10 m (`heal` if needed). Expected:
  hooked with no damage; stamina drains much faster than with the boar (the game's drain grows with weight). Note how
  far it moved: if a lox can be dragged usefully, the README's "Dragging a lox: to verify in game" becomes a
  statement; if not, the README says it cannot be dragged far.
  *Partly automated (`harpoon.heavy`); by hand: Read the NOTE lines of harpoon.heavy (or drag a lox in game) and
  decide whether a lox can be dragged usefully, then word the README line 'Dragging a lox' accordingly; the test does
  not judge that. (First run, with the player held 2.5 m beyond the line: the lox came 0.89 m in 5 s.).*
- [x] **T04 Too far:** throw at a tame more than 30 m away (if the harpoon reaches). Expected: "Target too far", not
  hooked, no damage.
  *Automated: `harpoon.hook`.*
- [x] **T05 Wild creatures unchanged:** after taming, `spawn Boar` (wild) and harpoon it. Expected: vanilla: hooked,
  damage number, pushed, it flees or attacks.
  *Automated: `harpoon.wild`.*
- [x] **T06 Other weapons unchanged:** PvP off, shoot a tame with the `Bow` (`ArrowFlint`), throw `SpearFlint` at it,
  hit it with a melee weapon other than the Butcher Knife. Expected: vanilla: arrows and spears pass through, melee
  does not hit, no damage.
  *Automated: `harpoon.others`.*
- [x] **T07 PvP on:** turn PvP on (inventory), harpoon a tame. Expected: hooked, no damage number, no push (vanilla
  would damage it). Turn PvP off again.
  *Automated: `harpoon.hook`.*
- [x] **T08 Saddled, not ridden:** `spawn SaddleLox`, put it on the tamed lox, ride it, get off, step back, harpoon
  it. Expected: hooked, no damage.
  *Automated: `harpoon.rider`.*
- [x] **T09 Summon:** `spawn Skeleton_Friendly` (unverified that it spawns tamed; else summon one with the Dead
  Raiser), harpoon it. Expected: hooked, no damage, it does not attack you.
  *Automated: `harpoon.summon`.*
- [x] **T10 Stay command:** tell a tame to stay (E), drag it about 20 m, block to release. Expected: it walks back
  towards its stay spot (vanilla). Tell it to follow, then to stay at the new spot: it stays there.
  *Automated: `harpoon.stay`.*
- [x] **T11 No Spears XP, no adrenaline:** equip `TrinketBronzeStamina` (Bronze Pendant; unverified that it is what
  lets you gain adrenaline); let the adrenaline bar empty and note the Spears progress bar in the Skills tab. First
  throw the harpoon once at open ground (no creature): if the adrenaline bar moves, the throw itself gives adrenaline
  (vanilla weapon data, unverified) and only the extra from the hit counts below. Hook a tame 3 times. Expected: the
  Spears bar does not move, and the adrenaline bar gains nothing beyond what an empty throw gives. Then harpoon a wild
  boar once. Expected: both grow (vanilla).
  *Automated: `harpoon.xp`.*
- [x] **T12 Tame in the line of fire:** tamed Wolf following you (E), `spawn Greyling` about 15 m ahead; while the
  wolf fights it, stand so the wolf is between you and the greyling and throw at the greyling. Expected: the wolf
  catches the harpoon: "Wolf harpooned", no damage number, its health bar stays full, it is not pushed and does not
  turn on you; the greyling is not hooked; no `Harpoon passed tame` Debug line. Block to release the wolf, then throw
  at the greyling from an angle where the wolf is not in the way. Expected: the greyling is hooked and damaged
  (vanilla).
  *Automated: `harpoon.line`.*
- [x] **T13 Fighting summon:** summon a skeleton with the Dead Raiser, `spawn Neck` ahead; while the skeleton fights,
  throw at the skeleton. Expected: hooked, no damage, it does not turn on you. Release it, then throw at the Neck with
  the skeleton in the way. Expected: the skeleton catches the harpoon (hooked, no damage), the Neck is not hooked.
  *Automated: `harpoon.summon`.*
- [x] **T14 No kill credit from a hook:** note `Enemy Kills` (Player Statistics). Harpoon a fresh tamed Boar you
  never hit (release it), then `spawn Wolf` next to it and let the wild wolf kill it. Do not hit the wild wolf before
  you read the count (killing it yourself adds 1; `god` keeps you safe, and `killenemies` afterwards adds nothing:
  its hits have no attacker). Expected: `Enemy Kills` unchanged; with Debug logging, `Cleared the attacker mark ...`
  after the hook. Control: harpoon another tamed boar, then kill it with the Butcher Knife. Expected: `Enemy Kills`
  +1, once.
  *Automated: `harpoon.credit`.*
- [ ] **T15 Staff of Protection (optional):** cast the Staff of Protection bubble on a tame (unverified that the
  bubble reaches tames; if it does not, mark `[-]`), harpoon it. Expected: the bubble hit effect may play, the bubble
  is not weakened, the tame is hooked, no damage.
  *Partly automated (`harpoon.shield`); by hand: Cast the real Staff of Protection next to a tame and see whether its
  bubble reaches the tame at all (if not, the item is [-]); the test puts the bubble on the tame itself and does not
  play the cast or look at the bubble's hit effect.*
- [x] **T16 Creature being tamed (optional):** `spawn Boar`, feed it once (not yet tame), harpoon it. Expected:
  vanilla: hooked and damaged (the mod does not treat it as a tame).
  *Automated: `harpoon.wild`.*
- [x] **T17 Tame on a ship (optional):** get a tame onto a `Karve` deck, harpoon it from the shore. Expected: hooked
  message, no damage, not pulled while it stands on the deck (vanilla).
  *Automated: `harpoon.ship`.*
- [x] **T18 Live toggle:** Esc → MC Mods → untick Harpoon Hooks Tames, harpoon a tame. Expected: the harpoon passes
  through (vanilla). Tick it again, throw again. Expected: hooked, no restart.
  *Automated: `harpoon.toggle`, `harpoon.mp.toggle`.*
- [ ] **T19 Disabled in the config file:** set `Enabled = false` in
  `BepInEx/config/MC.Farming.Harpoon.HooksTames.cfg`, restart. Expected: vanilla (passes through). Set it back to
  `true`.
  *Partly automated (`harpoon.mp.toggle`, `harpoon.toggle`); by hand: Start the game with Enabled = false already in
  the config file and check the harpoon passes through tames from the start (the restart path cannot run from a self
  test of a mod that is off).*
- [ ] **T20 Clean log:** after a session, no errors or exceptions mentioning `Harpoon Hooks Tames` or `MC.Farming` in
  `BepInEx/LogOutput.log`.
  *Partly automated (`harpoon.log`); by hand: After a normal play session (long uptime, leaving and re-entering a
  world) look in BepInEx/LogOutput.log for errors that mention Harpoon Hooks Tames or MC.Farming.*
- [x] **T21 Tame watching a wild creature it cannot reach:** tamed Boar (never told to follow or stay) in a pen,
  `spawn Deer` (or `Greyling`) about 15 m outside the fence where the boar can see it (its AI keeps the creature as
  its target). Stand inside the pen, so the creature is not in the line of fire, and throw at the boar 3 times over
  about a minute (block to release it between throws). Expected: hooked every time, no damage number, the health bar
  stays full, no `Harpoon passed tame` Debug line. `killenemies` afterwards.
  *Automated: `harpoon.busy`.*
- [x] **T22 Stray chasing prey:** tamed Wolf, `spawn Deer` about 20 m away so the wolf chases it. Throw at the wolf
  while it chases, with the deer not in the line of fire. Expected: hooked, no damage number, the wolf does not turn
  on you; walk away: it is held on the line and pulled towards you like any hooked creature (T02 rules), no `Harpoon
  passed tame` Debug line.
  *Automated: `harpoon.chase`.*
- [x] **T23 Earlier real hit keeps its kill credit:** turn PvP on, shoot a fresh tamed Wolf once with the `Bow` (it
  must survive, and must not have healed back to full health), harpoon it right away, block to release it, turn PvP
  off and note `Enemy Kills`. Then run `killtame` (it kills every loaded creature, tames included, with a hit that has
  no attacker, so do this last). Expected: the hook did no damage; `Enemy Kills` +1: the arrow hit keeps its vanilla
  kill credit, the hook does not take it away, and the kill counts as a ranged one (with Creature Kill and Tame
  Counts: "ranged" on the Wolf line). With Debug logging, `Cleared the attacker mark ...` still appears after the
  hook (only the hook's own trace is undone).
  *Automated: `harpoon.credit-kept`.*
- [x] **T24 Second throw at a hooked tame:** hook a tamed Boar, step a few metres closer (the line hangs loose) and
  throw again while it is still hooked. Expected: "Boar harpooned" again, still hooked with one line (its length is
  taken again from where you stand), no damage number, the health bar stays full, the boar is not pushed and does not
  run away or turn on you; with Debug logging, `Cleared the attacker mark ...` after each throw.
  *Automated: `harpoon.rehook`.*

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Hand-off: tame simulated by a friend without the mod** (vanilla server or host): the friend, who does NOT
  have the mod, stands next to an existing tamed Boar (or tames one; spawning needs `devcommands`, which on a
  dedicated server requires the friend to be admin). You arrive from far away (more than about 150 m) so the friend's
  game keeps simulating the boar; with Debug logging your `Hooked tame ...` line says "simulated by another player".
  Harpoon it and walk away. Expected: hooked and dragged, no damage on either screen, the boar does not flee, your
  stamina drains, no errors on either side. Afterwards the boar behaves normally for the friend.
  *Partly automated (`harpoon.handoff`, `harpoon.mp.hook`, `harpoon.mp.handoff`); by hand: A real friend without the
  mod standing next to the tame: check on both screens that there is no damage and no flee, that the boar is dragged
  and your stamina drains, that there are no errors in either log, and that the boar behaves normally for the friend
  afterwards.*
- [ ] **M02 Friend without the mod throws:** the friend (no mod, PvP off) throws a harpoon at the same tame.
  Expected: passes through (vanilla): only the thrower's mod matters.
  *Partly automated (`harpoon.handoff`); by hand: A real friend without the mod (PvP off) throws a harpoon at the
  tame: it must pass through.*
- [ ] **M03 Ridden tame:** the friend rides a saddled lox; you harpoon it with PvP off. Expected: passes through, the
  friend's ride is unaffected (Debug: `Harpoon passed tame $enemy_lox: a player is riding it.`). Turn your PvP on and
  harpoon it again. Expected: hooked, no damage to the lox, the lox and its rider are pulled while you walk away.
  *Partly automated (`harpoon.rider`); by hand: A friend riding the lox: PvP off, your harpoon passes and the friend's
  ride is unaffected; PvP on, the lox is hooked without damage and the lox and its rider are pulled while you walk
  away.*
- [ ] **M04 Players unchanged:** both PvP off: your harpoon passes through the friend. Both PvP on: vanilla (hooked
  and damaged).
- [ ] **M05 Hand-off kill credit (optional, documents a limitation):** after M01 (the friend's game still simulates
  the boar you hooked), note your `Enemy Kills`; the friend kills that boar with the Butcher Knife while you are
  connected. Expected: your `Enemy Kills` +1 as well (vanilla attacker rule; the friend's game has no mod to clear
  it). If the friend also runs the mod (installed and `Enabled = true`): no +1 for you. If the friend has it installed
  but `Enabled = false`: +1, like the hand-off case.
  *Partly automated (`harpoon.handoff`); by hand: A real friend's game kills the boar you hooked while you are
  connected, in the three cases (friend without the mod, with the mod on, with the mod installed but off), and you
  read your Enemy Kills.*
- [ ] **M06 Friend with the mod turned off, PvP on (optional):** the friend has the mod installed with
  `Enabled = false`, turns PvP on and throws a harpoon at your tame. Expected: vanilla: hooked, and damaged as vanilla
  damages it; no errors on either side.
  *Partly automated (`harpoon.handoff`); by hand: A real friend with the mod installed but Enabled = false, PvP on,
  throws at your tame: hooked and damaged as in vanilla, no errors in either log.*

## 0.1.0 — compatibility (optional, needs another mod)

- [x] **C01 With Crossbow Stays Loaded (MC)** (patches `Attack.OnAttackTrigger`, which the harpoon throw also runs):
  reload a crossbow, switch to the harpoon, hook a tame, switch back. Expected: the tame was hooked without damage and
  the crossbow is still loaded; no errors.
  *Automated: `harpoon.compat.crossbow`, `harpoon.log`.*
- [x] **C02 With Creature Kill and Tame Counts (MC):** both installed, single player. Note the Boar line in Valheim Compendium
  → Player Statistics → Creatures. Harpoon a fresh tamed Boar you never hit, release it, then `spawn Wolf` next to it
  and let the wild wolf kill it. Expected: the Boar kill count does not change, and hooking never changes the Boar
  tamed count (only taming does). Harpoon another tamed Boar, then kill it with the Butcher Knife. Expected: Boar +1
  killed, exactly one. Hand-off (optional, after M01): when the friend without the mod kills the boar you hooked
  while you are connected, your Boar kill count may go up by 1 (vanilla credit, documented in the README).
  *Automated: `harpoon.compat.counts`.*
- [ ] **C03 With HarpoonExtended (optional):** creature pulling on. PvP off: harpoon a tame. Expected: not hooked by
  this mod (passes through), one Info line "A harpoon hit a tame without its hook effect ..." in the log, no errors.
  PvP on: the tame is damaged and gets HarpoonExtended's rope (documented, not our hook).
  *Partly automated (`harpoon.compat.nohook`); by hand: Install the real HarpoonExtended with creature pulling on and
  repeat both cases (with PvP on the tame should also get HarpoonExtended's own rope); the test only imitates what its
  code does to the projectile.*
- [ ] **C04 With ValheimPlus Immortal tames (optional):** harpoon a tame. Expected: not hooked (no "harpooned"
  message, no pull), no damage, no errors (documented).
  *Partly automated (`harpoon.compat.immortal`); by hand: Install the real ValheimPlus with immortal tames (if it runs
  on this game version) and harpoon a tame: not hooked, no damage, no errors.*
- [ ] **C05 With EpicLoot (optional):** an enchanted Abyssal Harpoon (lightning / slow / paralyse effects) hooks a
  tame. Expected: hooked, no damage number from the hit or from instant procs (with Debug logging, one `Blocked an
  extra hit on hooked tame ...` line per stopped proc); delayed procs may still hurt it (documented limitation); no
  errors.
  *Partly automated (`harpoon.compat.procs`, `harpoon.compat.order`); by hand: Install the real EpicLoot and hook a
  tame with an enchanted Abyssal Harpoon (lightning, slow, paralyse): hooked, no damage number from the hit or instant
  effects, no errors.*
