# Weapon Moveset — design

| | |
|---|---|
| Mod | Weapon Moveset |
| GUID / project | `MC.Combat.Weapons.Moveset` (`src/Combat/Weapons.Moveset/`, root namespace `MC.Combat.WeaponsMovesetMod`, package `WeaponsMoveset`) |
| Category / scope | Combat / Revamp |
| Side | **Both**: the server (or the host) and every player install it; multiplayer Compatible; network version 2 (RPCs `<GUID>.Settings` / `<GUID>.SettingsRequest`, rules layout 2). Every move runs on the attacker's own game (section 4), so the mod is technically client-side; it ships as Both because it changes combat (house rule): the server refuses players without it, with another network version, or with it turned off on their game (the framework's `NetworkGate.PeerCompatible`), and sends its settings to everyone. |
| Sheet idea | `Weapon revamp` (this mod covers the jump attack and the roll attack; the parry attack and the "more dynamic moveset" line stay under Later) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/`; player Animator (layers, every parameter, every clip with its length and animation events), every weapon's `SharedData` and `Attack` fields, and the creature and Player prefab values dumped at runtime in a real world on 2026-09-29 (temporary `DataDump.cs` self-test); BepInEx 5 `AcceptableValueList<T>.Clamp` and `ConfigEntry<T>.Value` decompiled from the installed `BepInEx.dll`; other mods read on Thunderstore and GitHub on 2026-09-29; sibling design docs (Dual Wielding, Tower Shield Wall, Sneak Ambush, Creature Morale) read on 2026-09-30 |
| Status | Implemented (v0.1.0 code), in-game testing (design: adversarial review applied 2026-09-30; aligned on 2026-09-30 with the framework's join-check API and with the cross review of the five combat designs; user feedback after the first in-game test applied 2026-09-30: the roll attack flows out of the roll, Decisions 21-26, with the review fixes of section 10 stage 9; deviations in section 10) |

## Goal

Expected behaviours, from the idea sheet ("Objective is a SLIGHT rework of the vanilla weapon gameplay. MAYBE rework
the existing animations/moveset to be a bit more dynamic. Add a roll attack (roll then attack). Add a parry attack
(parry then attack). Add a jump attack (jump then attack).") and from the user in chat for the first version ("Jump
attack; Sprint attack. Put parry attack and roll attack as later TODO ideas (investigate if the Sprint attack can be
replaced by the roll attack instead, resulting in Jump attack and roll attack in the first version). Can take
inspiration from Goo's Combat Overhaul."). Each item is scope and has at least one test (section 8).

1. **G1 Jump attack.** After a jump the player makes, the first attack started before landing, when it is a primary
   attack of a melee weapon family whose jump attack is on (every family but spears and sledges by default, table
   2.2), is a jump attack: it plays another vanilla animation of the weapon's family (default: the family's combo
   finisher) and carries its own damage, stagger, knockback and stamina multipliers. One jump attack per jump.
2. **G2 Roll attack (in place of the sprint attack, Decision 1).** The first attack of a roll (a press the game keeps
   from the roll, the attack button held through it, or a new press within a short window after it), when it is a
   primary attack of a melee weapon family whose roll attack is on (every family but battleaxes, spears and sledges by
   default), is a roll attack: another vanilla animation of the family (default: the second combo swing) with its own
   multipliers. The user asked for a sprint attack in the first version and to investigate replacing it by the roll
   attack; the investigation says yes (Decision 1), so the sprint attack moves to Later (L1).
   **The roll attack flows out of the roll** (user feedback after the first in-game test, 2026-09-30: "The roll attack
   needs to flow from the roll itself without interruption. Right now there is the roll, full reset to idle state, then
   the attack."): a pressed or held attack cuts into the very end of the roll (from `FlowStart`, never before the
   roll's invulnerability has been over for a short margin) and the player's animation goes straight from the roll
   into the swing, with no idle or locomotion pose in between; a new press just after the roll does the same from the
   roll's end while the Animator still blends out of it, and a later press is a normal swing, so a roll attack never
   plays after a stand-up (Decisions 21-26).
3. **G3 A slight rework on top of vanilla.** The moves only use animations the game already has (no new assets;
   everyone sees each other's moves, players without the mod too), the default bonuses are modest (settings to tune
   them in game), a move takes the place of one step of the normal combo and the combo continues from it (Decision 3),
   and everything else stays vanilla: normal combos, secondary attacks, bows, crossbows, staves, bombs, tools,
   torches, shields and the Tower Shield Wall bash.
4. **G4 Multiplayer (MC rule for mods that change combat).** Required on the server and on every player: the server
   refuses players without the mod, with another version of it, or with it turned off on their game (setting
   `AllowPlayersWithoutMod`), and its settings apply to everyone.
5. **G5 Live toggle (MC rule).** The mod turns on and off live, and each move can be switched off on its own.

Inspiration taken from Goo's Combat Overhaul (GCO), the user's pointer: its jump-attack input rules (a deliberate jump
qualifies at once, primary before landing, one jump attack per airtime, it spends attack stamina on top of the jump),
borrowing a chain step of the weapon's own family without the vanilla finisher's ×2 spike, "a queued combo stays a
combo", and the fix of GCO 2.0.2 for the short gap between a successful attack start and the Animator entering the
attack state (2.7). Not taken: hyperarmor, lunge amplification, speed gates for a running attack, PvP multipliers,
per-weapon YAML (sections 3 and 6).

**Added beyond the request** (flagged at the pause):

- **A1 Aim for jump attacks.** While the player is in the air, the jump attack's swing follows the aim up or down by up
  to `AimAngle` degrees (default 30, at most 45: vanilla tilts a swing less than the view, never past 45°, 2.3), so a
  jump attack can strike an enemy below (or a flyer above). Vanilla player melee swings are always level; once the
  player lands the swing is level again.
- **A2 Start check.** A move whose animation does not start within 0.5 s (a trigger with no way in from the current
  animation state) is cancelled locally, a normal swing follows, and a warning names the setting to change, instead of
  the swing playing late.
- **A3 Animation settings.** For each weapon family and each move, the animation is a server-synced setting (a list
  of the game's melee attack animations, or Off), checked against the player's Animator at runtime. Not asked for: it
  exists because the defaults of table 2.2 are partly unverified (which clip each trigger plays, whether it can start
  from the air or the end of a roll) and the user flagged the table for review, so the user can try variants in game
  without a new build. It is the largest extra (24 synced strings, a cache, warnings): open question 7 offers the
  smaller alternative (a fixed table with a Debug-only override).
- **A4 Cooldown.** A move cannot start within `Cooldown` seconds (default 1) of the previous move's start, either kind
  (research recommendation, "optional cooldown ~1 s"). It never touches a vanilla roll or jump followed by a move (they
  take longer); it limits back-to-back moves with instant dash mods and fast jump spam (2.5).

**Non-goals (v1):**

- New animation clips, AssetBundles or Animator override controllers.
- Moves on the secondary attack; changes to swing tempo, lunge, or movement and turning while swinging (the sheet's
  "MAYBE ... more dynamic" line: Later L3).
- I-frames, hyperarmor or damage reduction during a move; cancelling a move (or its recovery) into a roll or block.
- Adrenaline tuning: moves pay the vanilla adrenaline of their hits (the swing's `m_attackAdrenaline` per enemy hit,
  once per swing for sledges), and their higher stagger multipliers make staggers, and the vanilla
  `m_staggerEnemyAdrenaline` paid for each, come sooner (1.3). No per-move adrenaline setting (Adrenaline revamp: L11).
- A PvP-only multiplier (GCO uses 0.6-0.75): the defaults are modest, PvP is opt-in, and it needs a hit-time patch
  (L13).
- Moves for weapons whose primary animation is not a vanilla melee family (modded animation systems), and for bows,
  crossbows, staves, bombs, tools, torches, pickaxes, farming tools, the fishing rod, tankards and shields.
- A jump attack after a fall that was not a jump: a ledge drop, rolling off a ledge, a catapult or AoE launch, a
  grappling pull, a step-up (L4).
- A HUD element, message or sound for a move: the move's animation is the feedback, so a family whose move would look
  exactly like its normal swing (spears, sledges) has that move Off by default (Decision 12).
- Creature attacks (monsters keep vanilla).

**Later** (TODO ideas the user listed for later, and what v1 leaves out):

- **L1 Sprint attack** (the user's v1 pick, replaced by G2): a third detector feeding the same execution path. A
  primary attack started while `Character.IsRunning()` has been true for at least ~0.3 s and the horizontal speed is at
  least ~5 m/s (GCO's gate; `m_runSpeed` is 7), on the ground or within 0.3 s of leaving it, and not while
  `InAttack()`. Default animation per family like GCO (finisher for one-handed weapons, second swing for two-handed
  ones), and it needs movement tuning to feel like a charge (clone `m_speedFactor`, a root-motion lunge): a vanilla
  attack from a sprint stops the player within about three physics ticks (1.4). Stands down when GCO is installed.
- **L2 Parry attack** (the user's later idea): a `Humanoid.BlockAttack` prefix runs the vanilla parry test
  (`m_timedBlockBonus > 1`, `0 <= m_blockTimer < 0.25`) and opens a scope; a `Player.AddAdrenaline` call inside it
  (a `Priority.First` prefix or a postfix) marks a held parry with an attacker; a primary attack within ~1 s is a parry
  attack (the family's first swing with a faster wind-up through `CharacterAnimEvent.Speed`). Share the helper with the
  Adrenaline revamp, ignore calls made during `Attack.StartWithoutAnimation`.
- **L3 Livelier swings** (the sheet's "MAYBE" line): per-family `m_speedFactor` / `m_speedFactorRotation` on the
  clone, a `Character.AddRootMotion` prefix for a lunge, tempo by scaling the clip-authored `CharacterAnimEvent.Speed`
  values (never a captured `Animator.speed`, which the hit-stop freezes).
- **L4 Ledge-drop jump attacks**: an attack after at least N s of airtime without a jump (GCO uses 1 s) also counts.
- **L5 Perfect-roll bonus**: `Player.m_beenHitWhileDodging` is still true on the roll's end edge (1.5), so a roll
  attack after a perfect dodge can get a stronger multiplier.
- **L6 Keep an early press for the whole roll**: a `Player.PlayerAttackInput` prefix that keeps a press made anywhere
  in the roll alive until the roll attack can cut in (`FlowStart`; vanilla keeps a press 0.5 s, so at the default a
  press in the first 0.35 s of a roll is lost; holding the button already works).
- **L7 Creature-only damage bonus**: a scope around the move's `Attack.OnAttackTrigger` plus an attacker-side
  `Character.Damage` prefix, so trees and rocks take vanilla damage from a move (Decision 9).
- **L8 Landing slam or plunge**: a downward pull on the body, and a small cloned Area attack fired with
  `Attack.StartWithoutAnimation` on the first ground contact after a jump attack (ValheimLegends' leap pattern).
- **L9 Borrowed finisher keeps its ×2**: force the clone's chain level instead of renaming the trigger (Decision 3,
  option B), so a jump attack playing the finisher hits like the finisher.
- **L10 Attack-cancel loop guard**: no roll attack from a roll that cancelled an attack (AttackCancel and similar mods).
- **L11 Per-move adrenaline** through the Adrenaline revamp (the clone's `m_attackAdrenaline`).
- **L12 Roll cancel**: moved into v1 by the user's feedback (G2, Decision 21): the roll attack cuts into the
  vulnerable tail of the roll (after `DodgeMortal`) and a script cross-fade takes the Animator out of the dodge state.
  What stays for later: letting a non-move attack (a secondary, a normal swing of a family whose roll attack is Off)
  flow out of the roll too.
- **L13 PvP multiplier**: a damage and stagger factor for move hits on players (attacker-side `Character.Damage`
  prefix inside the move's hit scope, shared with L7).

---

## 1. Vanilla behaviour (code trace)

Only what the design relies on. Line-level evidence is in the two research briefs of this run (vanilla code and
existing mods); every method below was re-read in `.ref`.

### 1.1 Input and tick order

- `Player.SetControls` acts at once on jump and dodge presses. **Keyboard, and a gamepad on the Default layout**: with
  block held the press is a `Dodge` (backwards without move input); crouching, crouch toggled or the dodge key make a
  forward `Dodge`; otherwise it calls `Jump()` (= `Character.Jump(force: false)`). So there, **a jump cannot start from
  sneak or with block held**: those presses roll. **Gamepad on the Alternative1/Alternative2 layouts**
  (`ZInput.IsNonClassicFunctionality`): the dodge button dodges only when blocking or crouching, and the jump button
  always calls `Jump()`, blocking or crouching included (`ForceJump` then ends the crouch). So there a jump from sneak
  or with block held is possible.
- `Player.FixedUpdate` (owner, local player, alive) runs `UpdateActionQueue`, **`PlayerAttackInput`**, `UpdateAttach`,
  `UpdateDoodadControls`, `UpdateCrouch`, **`UpdateDodge`**, then the rest. Within one physics tick the attack is tried
  before the dodge state is refreshed. `Humanoid.CustomFixedUpdate` (which runs `Attack.Update`) is driven by
  `MonoUpdaters`; its order against `Player.FixedUpdate` is not in code (R7), and the runtime run settled it (next
  bullet).
- Runtime order (the `moveset.order` NOTE, 1.0.16, at the normal frame rate and at 20 FPS): every physics tick runs
  `PlayerController.FixedUpdate`, then `MonoUpdaters.FixedUpdate` (which increments `MonoUpdaters.UpdateCount`, so the
  per-tick caches of `InAttack()` and of the animator tags refresh each tick) and inside it
  `Humanoid.CustomFixedUpdate` (so `Attack.Update`), then `Player.FixedUpdate`. The player Animator's update mode is
  `AnimatePhysics` ("Fixed"): it updates once per physics step, after the scripts' `FixedUpdate`. A trigger or a
  cross-fade set during `Player.FixedUpdate` of tick N is applied by the Animator in the same step, and `Attack.Update`
  of tick N+1 sees the new state.
- `PlayerController.FixedUpdate` calls `SetControls` every tick; `attackHold` is true on every tick the button is down
  (a normal click often spans two or more ticks), `attack` only on the first.
- `Player.PlayerAttackInput`: a primary press sets `m_queuedAttackTimer = 0.5` (and clears the secondary buffer); every
  tick, `(m_queuedAttackTimer > 0 || m_attackHold) && StartAttack(null, false)` clears the timer on success. A press is
  retried for 0.5 s, and holding the button retries every tick.

### 1.2 Starting an attack

- `Humanoid.StartAttack(Character target, bool secondaryAttack)` refuses when `(InAttack() && !HaveQueuedChain())`,
  `InDodge()`, `!CanMove()`, knocked back, staggering or in a minor action; no weapon or an empty animation for the
  chosen slot refuses too. **There is no ground, run or block check**: attacking in the air is vanilla. Then it stops
  the current attack (before the new one is validated), clones `m_shared.m_attack` or `m_secondaryAttack`
  (`Attack.Clone` = `MemberwiseClone`), and calls `Attack.Start(..., previousAttack, m_timeSinceLastAttack, ...)`; on
  success `ClearActionQueue`, `m_currentAttack = clone`. Callers: `Player.PlayerAttackInput`,
  `Player.UpdateAttackBowDraw` and `MonsterAI` (monsters).
- **The restart gap.** `Player.InAttack()` reads only the Animator (next or current state tagged `attack`, on any
  layer), cached once per `MonoUpdaters` tick. Between a successful `StartAttack` and the Animator entering the attack
  state, a held or buffered button makes `StartAttack` succeed again: it stops the first clone and starts a new one.
  This happens when two physics ticks run before the Animator updates (below 50 FPS), and whenever the Animator cannot
  take the attack transition at once (for example in the middle of the transition out of the dodge state, 1.5). In
  vanilla it is harmless: the new clone fires the same trigger again. GCO 2.0.2 fixed the same gap for its moves.
- `Humanoid.GetCurrentWeapon`: the right-hand weapon, else a left-hand weapon that is not a torch, else the unarmed
  weapon (`PlayerUnarmed`). A shield in the left hand with an empty right hand attacks with bare hands.
- `Attack.Start` on the clone: empty `m_attackAnimation` → false; it then sets `m_character`, `m_weapon` and the other
  references; reload check; stamina check `GetAttackStamina()` (the clone's `m_attackStamina` with equipment, status
  effects and skill) `+ 0.1` → false with the HUD flash; eitr, health, ammo. Trigger: with `m_attackChainLevels > 1`
  it continues the chain when `previousAttack.m_attackAnimation` equals its own and `timeSinceLastAttack <= 0.2`, and
  fires `m_attackAnimation + level`; with `m_attackRandomAnimations >= 2` a random suffix; **otherwise it fires
  `m_attackAnimation` as is**. At chain level 0 a player is turned: to the camera yaw when the gameplay option "attack
  towards player look direction" is off (the keyboard default); when it is on (the gamepad default, and the default of
  `Player.Awake` when the preference was never written) to the move direction **only when there is move input**, and
  not at all without it.
- `Attack.Update` (owner): on the first tick in the `attack` animator tag (`m_wasInAttack` still false) it pays stamina,
  eitr and health, plays the start effects, adds `m_attackStartNoise`, and sets `m_nextAttackChainLevel = level + 1`
  (0 when `>= m_attackChainLevels`). Leaving the tag stops the attack. **A trigger that never leads to an attack state
  costs nothing and never stops**; the next `StartAttack` stops it, but the Unity trigger stays set until something
  consumes it.
- `Attack.Stop` sets `m_attackDone`, but `Humanoid.OnAttackTrigger` calls `m_currentAttack.OnAttackTrigger()` without
  checking it: a stopped clone that is still `m_currentAttack` hits if its animation plays. `Humanoid.UnequipItem`
  (and `StartAttack`) drop an attack with `Stop()`, `m_previousAttack = m_currentAttack`, `m_currentAttack = null`.
- Chains: `Player.HaveQueuedChain` needs a buffered or held primary and `Attack.CanStartChainAttack`
  (`m_nextAttackChainLevel > 0` and the clip's `Chain` event passed). Finisher clips (`Attack3`, `Axe combo 3`,
  `BattleAxe_Combo3`) and all battleaxe clips have no `Chain` event (dump): a press during them waits in the 0.5 s
  buffer (or the held button) until the state ends.
- `ZSyncAnimation.SetTrigger` invokes the `SetTrigger` RPC on everybody; `RPC_SetTrigger` calls
  `Animator.SetTrigger(name)` on each peer's own Animator. `ZSyncAnimation.HasParameter(name, type)` scans
  `Animator.parameters` (a copy per call); vanilla never calls it.

### 1.3 The hit

- `Attack.OnAttackTrigger` (from the clip's `Hit` / `OnAttackTrigger` event, owner only through
  `Humanoid.OnAttackTrigger`) dispatches Horizontal/Vertical to `DoMeleeAttack`, Area to `DoAreaAttack`.
- `DoMeleeAttack`: origin = the character's feet + `m_attackHeight` (no vanilla player melee attack has an origin
  joint, dump); direction `GetMeleeAttackDir` = the facing pitched toward the aim (`Humanoid.GetAimDir` = the eye's
  forward) by at most `m_maxYAngle`, which is **0 for every vanilla player melee attack** (dump). Every vanilla player
  melee primary has `m_hitTerrain` true, so the rays include terrain, and each ray stops at its first hit (unless
  `m_hitThroughWalls`): a ray pitched into the ground ends there. Per target:
  `m_pushForce = m_attackForce × skill factor × m_forceMultiplier`, `m_staggerMultiplier` copied, `ModifyDamage`
  (× `m_damageMultiplier`, skill factor, level factor), then **the finisher bonus ×2 damage and ×1.2 push only when
  `m_attackChainLevels > 1` and the current level is the last one**, `SEMan.ModifyAttack`, `Damage(hit)`,
  adrenaline `m_attackAdrenaline × victim.m_enemyAdrenalineMultiplier` per character hit, hit noise, hit-stop.
- `DoAreaAttack` (sledges) uses the same `m_forceMultiplier`, `m_staggerMultiplier` and `ModifyDamage`,
  `m_lastChainDamageMultiplier` only with more than one chain level, a fixed origin in front of the character (it never
  reads `m_maxYAngle`), and pays `m_attackAdrenaline` once per swing × the highest victim multiplier.
- `HitData.DamageTypes.Modify(float)` multiplies every type, chop and pickaxe included: a damage multiplier on a swing
  also hits trees and rocks harder.
- `Character.RPC_Damage` (victim owner) staggers at once when `hit.m_staggerMultiplier >= 100`, and doubles damage on a
  staggered non-player. `Character.ApplyDamage` adds the landed damage × `m_staggerMultiplier` to the stagger bar;
  `AddStaggerDamage` pays the attacking player `m_staggerEnemyAdrenaline × victim multiplier` on each stagger.

### 1.4 Jumping and the air

- `Character.Jump(bool force = false)`: nothing when dead, encumbered, in a dodge, knocked back or staggering; needs
  `IsOnGround()` (or swim depth after touching the world) and `force || !InAttack()` (**no jump out of an attack**, but
  a jump is possible in the restart gap of 1.2, before the Animator shows the attack); otherwise, while grappling, it
  pulls the hook instead. Velocity from `m_jumpForce` (8 on the Player prefab, dump) × `(1 + 0.4 × Jump skill)` plus
  `m_jumpForceForward` (2) along the move direction, then `ForceJump`.
- `Character.ForceJump(vel, effects = true)`: sets the velocity, `m_lastGroundTouch = 1` (so `IsOnGround()` is false
  at once), **`m_jumpTimer = 0`**, `AddNoise(30)`, and with effects the `jump` trigger, `OnJump` (Player: jump stamina,
  10 base, `ClearActionQueue`) and `SetCrouch(false)`. Other callers of `ForceJump`: `Aoe` launches, `Catapult`,
  `GrapplingPoint`. `CharacterAnimEvent.Jump` calls `Jump(force: true)` from animation events (no player clip has a
  `Jump` event, dump). `Character.OnAutoJump` (step-up) sets `m_jumpTimer = 0` without `Jump`.
- `Character.IsOnGround()` = `m_lastGroundTouch < 0.2` (or a sleeping body); `UpdateMotion` adds `fixedDeltaTime` to
  `m_lastGroundTouch` and `m_jumpTimer` every tick; ground contact resets `m_lastGroundTouch`, and is ignored while
  `m_jumpTimer < 0.1` (`OnCollisionStay`). So a jump is airborne for at least 0.1 s, and walking off a ledge keeps
  `IsOnGround()` true for 0.2 s.
- While airborne and in an attack, `Humanoid.GetAttackSpeedFactorMovement` returns 1, and `Character.UpdateWalking`
  scales any velocity change by `m_airControl` (0.1, dump) with move input, or drops it: **the jump keeps its momentum
  through an air swing**. Once grounded the clone's `m_speedFactor` (0.1-0.3) applies again. No player clip has a
  `Stop` event (dump), so `Humanoid.OnStopMoving` never zeroes those factors for players.
- Stamina: every `UseStamina` (the jump's included) restarts `m_staminaRegenTimer` = `m_staminaRegenDelay` (1 s), and
  `UpdateStats` gives no regeneration while `InAttack()` or `InDodge()`. Stamina does not come back during a normal
  jump.
- The airtime of a jump depends on `Physics.gravity`, which is not in code (unverified; the `moveset.jump` self-test
  logs it). Fall damage uses the height fallen (above 4 m), not the speed.

### 1.5 Rolling

- `Player.Dodge(dir)`: `m_queuedDodgeTimer = 0.5`, stores the direction, Dodge skill +0.1.
- `Player.UpdateDodge` (every tick): a queued dodge starts when on the ground, alive, **not in an attack**, not
  encumbered, not already dodging, not staggering, with stamina for `GetDodgeStaminaUse` (10 base, skill-reduced):
  `ClearActionQueue`, `m_dodgeInvincible = true`, facing snapped to the roll direction, trigger `dodge`, noise 5,
  stamina. At the end of every call `m_inDodge` = the `dodge` trigger still pending or the next/current layer-0 tag is
  `dodge`; its true → false edge is the tick the Animator **starts** its transition out of the dodge state (the next
  state is no longer tagged `dodge`). `m_beenHitWhileDodging` is reset only on the tick after that edge.
- `Player.InDodge()` returns `m_inDodge` on the owner, and `StartAttack` refuses while it is true. Because
  `PlayerAttackInput` runs before `UpdateDodge`, **a buffered or held attack starts on the tick after the edge**, while
  the Animator may still be in that transition (restart gap, 1.2).
- I-frames end on the `DodgeMortal` event (`Player.OnDodgeMortal` sets `m_dodgeInvincible = false`); the only clip
  with it is `Dive` (1.50 s, event at 0.70 s, dump). `UpdateDodge` publishes `m_inDodge && m_dodgeInvincible` to the
  ZDO (`s_dodgeinv`), which is what every attacker's game reads through `IsDodgeInvincible()` (the owner reads its own
  cached value; another peer reads the ZDO, so it sees the i-frames end only once the ZDO reaches it: send interval
  plus network latency). The attack after a roll gets no i-frames.
- **Runtime timeline of a vanilla roll** (the `moveset.roll` NOTEs, 1.0.16, every family the same): the roll starts
  0.02 s after the dodge press; `m_inDodge` stays true 0.92 s; the i-frames end 0.42 s after the start (so the dodge
  state plays the 1.50 s `Dive` clip about 1.67 times faster); an attack buffered or held through the roll starts on
  the tick after the edge and **enters its attack state 0.20 s later, for every weapon family**, while a jump attack
  enters 0.06 s after its start. So the Animator's transition out of the dodge state (to idle or locomotion) runs about
  0.2 s and the attack trigger is only taken once it is over: the player sees the roll, the character standing up to
  idle, then the swing. This is the "full reset to idle" of the user's feedback (G2). Whether the controller has a
  transition from the dodge state itself on an attack trigger is Animator graph data (unverified): vanilla never sets
  an attack trigger during the dodge state (`StartAttack` refuses in it), so the runtime data cannot show it; the
  `moveset.roll-input` "learned state" case measures it (2.9).
- Quickstep-style dash mods (Quickstep, SecondaryAttacks, SpecialAttack) skip the vanilla roll but set `m_inDodge` by
  hand (research brief), so the same edge also marks the end of their dash.

### 1.6 The player Animator (runtime dump, 1.0.16)

- Two layers (`Base Layer`, `upperbody`). Every chained melee family fires `<base><index>`; all these triggers exist:
  `swing_longsword0-2`, `swing_axe0-2`, `battleaxe_attack0-2`, `dualaxes0-3`, `greatsword0-2`, `atgeir_attack0-2`,
  `knife_stab0-2`, `dual_knives0-2`, `unarmed_attack0-1`; single ones `spear_poke`, `swing_sledge`; secondaries
  `sword_secondary`, `mace_secondary`, `axe_secondary`, `battleaxe_secondary`, `dualaxes_secondary`,
  `greatsword_secondary`, `atgeir_secondary`, `knife_secondary`, `dual_knives_secondary`, `unarmed_kick`. Other
  Trigger parameters used by the self-tests: `reload_crossbow_done`, `recharge_lightningstaff_done`.
- Which clip each trigger plays, and whether each trigger has a transition from the jump, fall and end-of-roll
  states, is Animator graph data that the dump cannot show (unverified; section 9). Mods on 1.0 play chain triggers
  from idle and from a run as real attacks (GCO's running and jump attacks borrow chain steps; SecondaryAttacks fires
  `swing_axe2` and `battleaxe_attack1`; Sword Heavy Slash `dualaxes0` and `greatsword2`), which is good evidence for
  transitions from most states.
- The Animator's update mode is not in code; the runtime run logged `AnimatePhysics` ("Fixed", culling
  `CullUpdateTransforms`), 1.1.
- **State tags** used by the game (`ZSyncAnimation.GetHash` = `Animator.StringToHash`): `attack` (`Humanoid.InAttack`,
  on any layer), `dodge` (`Player.UpdateDodge`, layer 0), `stagger`, `freeze`, `sitting`. Every melee move played on
  layer 0 in the runtime run ("layer 1 in none").
- **Clip each trigger plays** (layer 0 clip at entry, `moveset.jump` / `moveset.roll` NOTEs, 1.0.16):
  `swing_longsword1` `Attack2`, `swing_longsword2` `Attack3`, `swing_axe1` `Axe combo 2`, `swing_axe2` `Axe combo 3`,
  `battleaxe_attack2` `BattleAxe_Combo3`, `dualaxes1` `DualAxes Attack 2 2`, `dualaxes3` `DualAxes Attack 4`,
  `greatsword1` `Greatsword BaseAttack (2)`, `greatsword2` `Greatsword BaseAttack (3)`, `atgeir_attack1`
  `2Hand-Spear-Attack9`, `atgeir_attack2` `2Hand-Spear-Attack3`, `knife_stab1` `knife_slash1`, `knife_stab2`
  `knife_slash2`, `dual_knives1` `Knife Attack Combo (2)`, `dual_knives2` `Knife Attack Combo (3)`, `unarmed_attack1`
  `Punchstep 2`. **State names** are not readable at runtime (Unity exposes only hashes: `AnimatorStateInfo.fullPathHash`,
  `shortNameHash`; `Animator.HasState(layer, hash)` answers whether a state exists). None of these states is named
  after its clip or its trigger (in-world run of 2026-09-30, `moveset.triggers`: no full-path or short name matched),
  so the roll flow gets the state hashes from a copy of the controller (2.9).

### 1.7 Multiplayer authority

Everything above runs on the attacking player's own game: input, the clone, the trigger, hit detection and the
`HitData` (damage, `m_pushForce`, `m_staggerMultiplier`). The victim's owner resolves the hit with vanilla
`Character.RPC_Damage`, whether or not it runs the mod. Other peers see the trigger through the RPC, and their own
Animator plays the vanilla animation of that name.

---

## 2. Design

### 2.1 One execution path

Two detectors (jump, roll) produce a **token** on the local player. The next successful attack start consumes it; when
that attack is an eligible primary, the per-swing `Attack` clone is edited before `Attack.Start` fires its trigger,
and the mod then watches that clone until its animation has started (2.7). Vanilla code does the rest: stamina check
and payment, facing, trigger RPC, hit detection, chains, other mods' patches.

```
Character.Jump (input) ─► jump token ─┐
Player.UpdateDodge (start/end edges) ─► roll token (in the roll, after it)
                                      ├─► Humanoid.StartAttack prefix: move starting? refuse (2.7). Else Decide;
                                      │   a roll attack inside the roll opens vanilla's InDodge gate for this call
                                      │   Attack.Start prefix (after Dual Wielding): edit the clone (or refuse an
                                      │   attack that started inside the roll but is no roll attack)
                                      ├─► StartAttack postfix: success → consume tokens, roll attack: cross-fade the
                                      │   Animator out of the roll (2.9), watch the move; finalizer: gate closed again
                                      ├─► Player.UpdateDodge postfix: edges, landing, move watch (entry, cancel, aim),
                                      │   state learning (2.9)
ZSyncAnimation.RPC_SetTrigger postfix ─► another player's roll attack trigger during their roll: same cross-fade
```

A later sprint or parry attack (L1, L2) is one more detector and one more settings section, same path.

### 2.2 Weapon families and eligibility (`Families`)

The family is read from **the clone's current `m_attackAnimation`** when our `Attack.Start` prefix runs (after
Dual Wielding's prefix, which may have replaced the animation, chain levels and hit shape with its template's), plus
the weapon's skill for the one shared name. Pure string switch on the exact base names, no allocation; a full trigger
name such as `swing_longsword2` (a clone renamed by another mod) matches no family. The same prefix saves the clone's
base name, `m_attackChainLevels` and `m_maxYAngle` as they arrived, before our edit: the combo continuation (2.5) and
the aim reset (2.3) use those values, never the weapon's `m_shared.m_attack` (for a Dual Wielding pair they are the
template's: `dualaxes` has 4 chain levels where the main hand's axe or sword has 3).

| Family (setting key) | Detected by | Items (runtime dump) | Normal primary (opener hit) | Jump default (hit) | Roll default (hit) |
|---|---|---|---|---|---|
| `Swords` | `swing_longsword`, skill not Clubs | `SwordIron`, `SwordBlackmetal`, … (17 incl. FW_/SP_ copies) | `swing_longsword0-2` (0.45 s) | `swing_longsword2` (0.35 s) | `swing_longsword1` (0.26 s) |
| `Maces` | `swing_longsword`, skill Clubs | `MaceIron`, `Club`, … (13) | same chain (0.45 s) | `swing_longsword2` | `swing_longsword1` |
| `Axes` | `swing_axe` | `AxeIron`, … (12) | `swing_axe0-2` (0.56 s) | `swing_axe2` (0.70 s) | `swing_axe1` (0.63 s) |
| `Battleaxes` | `battleaxe_attack` | `Battleaxe`, … (10) | `battleaxe_attack0-2` (1.01 s) | `battleaxe_attack2` (0.43 s) | **Off** |
| `DualAxes` | `dualaxes` | `AxeBerzerkr`, `AxeEarly`, … (5); every Dual Wielding pair except two knives (axe, sword, mace and mixed pairs: its `PairMoves` template, default `AxeBerzerkr`) | `dualaxes0-3` (0.21 s) | `dualaxes3` (0.37 s, two hits) | `dualaxes1` (0.37 s) |
| `Greatswords` | `greatsword` | `THSwordKrom`, … (9) | `greatsword0-2` (0.60 s) | `greatsword2` (0.64 s) | `greatsword1` (0.49 s) |
| `Atgeirs` | `atgeir_attack` | `AtgeirIron`, … (8) | `atgeir_attack0-2` (0.47 s) | `atgeir_attack2` (0.55 s) | `atgeir_attack1` (0.61 s) |
| `Knives` | `knife_stab` | `KnifeCopper`, … (13) | `knife_stab0-2` (0.23 s) | `knife_stab2` (0.33 s) | `knife_stab1` (0.18 s) |
| `DualKnives` | `dual_knives` | `KnifeSkollAndHati` (3); Dual Wielding pairs of two knives (its `KnifePairMoves` template, default `KnifeSkollAndHati`) | `dual_knives0-2` (0.18 s) | `dual_knives2` (0.18 s) | `dual_knives1` (0.18 s) |
| `Spears` | `spear_poke` | `SpearBronze`, … (13) | `spear_poke` (single, 0.34 s) | **Off** | **Off** |
| `Fists` | `unarmed_attack` | bare hands (`PlayerUnarmed`), `FistFenrirClaw`, … (6) | `unarmed_attack0-1` (0.42 s) | `unarmed_attack1` (0.45 s) | `unarmed_attack1` (0.45 s) |
| `Sledges` | `swing_sledge` (Area) | `SledgeIron`, … (7) | `swing_sledge` (single, 1.16 s) | **Off** | **Off** |

Times in brackets: real seconds from the trigger to the first `Hit` event, integrating the clip's `Speed` events
(research clip table); the trigger → clip pairing is inferred from clip names (unverified, the self-tests log the clip).
Rules behind the defaults (Decisions 4 and 12):

- the **jump attack plays the family's last combo step** (GCO's choice for its jump attacks: a heavy strike), the
  **roll attack the second step**, so the two moves look different from each other and from the normal first swing;
- a move must look different from the normal swing, so families with a single primary animation (spears, sledges)
  have both moves Off (the settings can turn them on, numbers only);
- a roll attack must not open much faster than the family's own opener: the battleaxe's second step hits at 0.31 s
  against its 1.01 s opener (a 3× faster heavy opener, with ×1.5 stagger on a 1.5 base), so the battleaxe roll attack
  is Off; its jump attack stays, because the jump costs 10 stamina and commits the player to the air (**flag**);
- fists have two steps, so both moves play `unarmed_attack1`. The kick (`unarmed_kick`) stays in the list, but a move
  keeps the primary's numbers (2.5): a jump kick would stagger and push like a punch (×1, against the kick's ×6 and ×3)
  and lock the player for the kick's 1.77 s clip.

Other families' move hits land 0.03-0.2 s sooner or later than their opener: they trade a little speed either way for
the move's bonus.

**Eligible** (`Families.Eligible(clone, weapon)`, all cheap compares; failing any = the attack stays vanilla):

1. `ItemKinds.Classify(weapon.m_shared) == ItemKind.Weapon` (shared helper: torches, tankards, pickaxes, scythe,
   shovel, fishing rod, tools and shields are other kinds; its shield rule, shield pose + Blocking skill → `Shield`
   whatever the item type, keeps tower shields `Shield` after Tower Shield Wall makes them `TwoHandedWeaponLeft`);
2. `weapon.m_shared.m_skillType != Skills.SkillType.Blocking` (the Tower Shield Wall bash, whatever its item type);
3. `clone.m_attackType` is Horizontal, Vertical or Area (bows, crossbows, staves, bombs, Sneak Ambush's Smoke Screen
   `MC_SmokeScreen`, `SpearChitin` are Projectile; tankards and the Sparkler None);
4. `!clone.m_requiresReload && !clone.m_bowDraw`;
5. a known family (else a modded animation system or another mod's rename: stand down).

### 2.3 G1 Jump attack

**Jump token.** `Character.Jump` prefix, only for `Player.m_localPlayer` and `force == false`: remember `m_jumpTimer`.
Postfix: if it was above 0 and is now exactly 0, `ForceJump` ran inside this call, so the player really jumped →
`JumpLive = true`, `JumpAt = Time.fixedTime`, **unless an attack was starting** (`m_currentAttack` not done, not yet
in its attack state, `m_time < 0.5`): a jump taken in the restart gap (1.4) belongs to that attack, which then plays in
the air. This counts jumps from input (and swim-depth jumps), and ignores the grappling pull (no `ForceJump`),
catapults, AoE launches and grappling points (they call `ForceJump` directly), step-ups (`OnAutoJump`) and
animation-event jumps (`force: true`). A tired jump (no stamina) is still a jump.

**Token end.** The per-tick postfix on `Player.UpdateDodge` (2.4) ends it at the first tick where
`Time.fixedTime - JumpAt >= 0.1` and `IsOnGround()` (landing; the 0.1 s is the vanilla ground-contact lock). Any
successful attack start (primary or secondary, move or not) also ends it: **one jump attack per jump, and only the
first attack after it**. A new jump makes a new token.

**Decision** (`MoveTracker.Decide`, pure, at `Humanoid.StartAttack`): a jump attack when the move is on, GCO is not
installed (Decision 15), the attack is primary, not `InAttack()` (a queued chain stays a chain), not swimming, not
attached, `JumpLive`, `!IsOnGround()`, and the cooldown has passed (A4: `Time.fixedTime - LastMoveAt >= Cooldown`).
**The jump owns the air attack:** with `JumpLive` and `!IsOnGround()` but the jump attack unavailable (`JumpAttack`
off, or GCO installed) the attack is a normal swing, never a roll attack from a roll just before the jump (else
`JumpAttack = false` would still give a move on jump + attack, and GCO's jump attack would get our roll attack on top).

**Clone edits:** the common ones (2.5) with the Jump numbers, plus `m_maxYAngle = max(m_maxYAngle, AimAngle)` (A1):
`GetMeleeAttackDir` then pitches the swing toward the camera aim by up to that angle, so looking down at a boar while
airborne sweeps down at it (and looking up at a Deathsquito sweeps up). Looking level changes nothing. The aim target
is the look direction's height on the body's heading (`aimDir.x/z` = the unit forward, `y` = the look's), so its pitch
is `atan(sin(look pitch))`: looking 30° down aims about 26.6° down, and even straight down aims at most 45°. The swing
pitch is `min(AimAngle, atan(sin(look pitch)))`, hence the setting's 0-45 range. **Only in the
air**: the move watch (2.7) sets the clone's `m_maxYAngle` back to the value it had before our edit (saved by the
prefix, 2.2: 0 for every vanilla player melee attack and for Dual Wielding's templates in 1.0.16) on the first tick the
player is on the ground. Kept after landing, a pitched fan would hit the ground: with the eye 30° down the rays of a
sword (origin 1 m, reach 2.4 m) meet the terrain at about 2 m and stop there, so a target at 2-2.4 m would be missed,
and the terrain hit costs durability and a hit-stop. A hit event in the same physics tick as the touchdown can still use
the pitch (at most one tick). Sledges' area slam never reads the angle.

**What the player sees:** the jump (10 stamina), then the family's finisher animation with the momentum of the jump
kept (vanilla air physics). If the hit comes before landing it uses the airborne sweep; with a wind-up longer than
the remaining airtime, the hit lands right after touchdown, level, where the clone's `m_speedFactor` stops the slide (a
planted strike). The next attack starts the normal combo at its first swing, as after a vanilla finisher (2.5).

**Edge cases:**

- Jump from sneak or with block held: on keyboard and the Default gamepad layout those presses roll (1.1), so there is
  no jump attack (the roll attack covers them). On the Alternative gamepad layouts the jump button jumps anyway, the
  jump ends the crouch, and the jump attack works like any other.
- Walking off a ledge, rolling off a ledge, catapults, launches: no jump token (Non-goals, L4). A roll that ends in the
  air still gives a roll attack (2.4).
- Several attacks in one long airtime (a jump off a cliff, the Feather Cape's slow fall): the first is the jump attack,
  the others are normal air swings (vanilla).
- Pressing attack late, so the attack only starts after landing: the token is gone on the landing tick (the decision
  also checks `!IsOnGround()`), so it is a normal swing.
- Not enough stamina for the swing after the jump: vanilla `Attack.Start` refuses with the HUD flash, and the 0.5 s
  buffer retries every tick, but stamina does not come back during a jump (1.4), so there is no attack in that jump;
  the token ends on landing. With `StaminaMultiplier` above 1, a player who can pay a normal swing but not the move
  gets a normal swing instead (2.5).
- Jumping into water: no move while swimming; the token ends on the next ground contact.
- Swimming, riding, sitting, ladders: `IsSwimming()` / `IsAttached()` → vanilla.
- A second jump in the air from a double-jump mod: whether it re-arms the token depends on how that mod jumps
  (unverified; harmless either way).

### 2.4 G2 Roll attack

**Roll edges** (`Player.UpdateDodge` postfix, local player, every tick): compare `m_inDodge` with its value on the
previous tick (kept by the mod, reset when the local `Player` object changes). False → true (a new roll or dash): the
**roll token** is live *in the roll* (`RollActive`, `RollStartAt = Time.fixedTime`, a roll counter goes up) and any
older end token is cleared. True → false (the roll ended, or was cut by our roll attack, below): when the roll token is
still live (no attack started inside the roll) and the player is not staggering (a roll interrupted by a hit gives no
token), it becomes the *after the roll* token (`RollEndAt = Time.fixedTime`); either way the in-roll token ends. The
same postfix ends the jump token (2.3), runs the move watch (2.7) and the state learning (2.9). It reads `m_inDodge`,
which dash mods set by hand, so a Quickstep dash counts as a roll; a Harmony postfix also runs when such a mod's prefix
skips the vanilla body.

**Decision:** a roll attack when the move is on, the attack is primary, not `InAttack()`, not swimming, not attached,
the cooldown has passed (A4), and either

- **inside the roll (the cut):** the in-roll token is live, the Animator is in the vanilla dodge state (layer 0 next or
  current state tagged `dodge`: dash mods without it never cut, they use the after-the-roll case), at least `FlowStart`
  seconds (default 0.85) have passed since the roll started, **and the roll's i-frames have been over for at least
  `IframeMargin` (0.2 s)** (`m_dodgeInvincible` false: `DodgeMortal` has fired, 0.42 s into a vanilla roll; the tick
  `MoveTracker` saw it is the start of the margin). The i-frame rule is not a setting: whatever `FlowStart` says, the
  attack never starts while the roll makes the player invulnerable, on the player's own game or, thanks to the margin,
  on another peer's that reads the ZDO flag (1.5; that peer checks the hits of the creatures it owns);
- **after the roll:** `Time.fixedTime - RollEndAt <= Window` (default 0.4 s), **and, after a roll that ended out of the
  vanilla dodge state, only while layer 0 is still in it** (its current state still tagged `dodge` during its blend
  out, or blending into one: the game's cached tags `GetCurrentAnimHash` / `GetNextAnimHash`, the same test as the
  cross-fade's `RollFlow.InRoll`). Once the blend out is over (about 0.2 s after the edge) the stand-up has played, and
  a roll attack from there would be exactly the "roll, stand-up, attack" the user asked to remove: the press is a
  normal swing, whatever `Window` says (Decision 26). Whether the roll ended out of the dodge state is recorded on the
  true → false edge (dash mods: no dodge state, `Window` alone).

Jump wins when both tokens are live (a roll, then a jump, then an attack in the air), also when the jump attack is off
or GCO is installed: the attack is then a normal swing (2.3). A roll that ends in the air without a jump (off a ledge)
still gives a roll attack, and the cut works in the air too while the dodge state plays.

**Opening vanilla's gate for the cut.** Vanilla `StartAttack` refuses while `InDodge()` (1.2). When `Decide` returns a
roll attack inside the roll, the `StartAttack` prefix sets `m_inDodge = false` for this call only, and the finalizer
sets it back to true on every path (success, refusal, another prefix's skip, an exception). Nothing else reads it in
between (in the tick order of 1.1 `Attack.Update` and the movement already ran), and `UpdateDodge` recomputes it from the
Animator at the end of the tick anyway. Vanilla then runs as for any attack: it stops the previous attack, clones,
calls `Attack.Start`, which fires the trigger (local Animator at once, RPC to everyone), checks stamina, turns the player.
**Before opening the gate, the prefix checks that the current weapon can become a roll attack that flows**
(`MoveTracker.CanFlow`, only from the cut point to the roll's end, a few ticks per roll, no allocation): the weapon's own
primary attack passes `Families.Eligible`, the family's roll animation is on (`MoveTriggers.Resolve`), and an attack
state is known for it on the local Animator (`RollFlow.HasStateFor`, 2.9). If not, the gate stays shut and vanilla
refuses the attack (`InDodge`), with no clone made (Debug counter `GateSkips`). A weapon in each hand (a Dual Wielding
pair; vanilla never has one) always passes, since Dual Wielding changes the clone's family inside `Attack.Start`.
**An attack that started inside the roll only because we opened the gate but does not become a roll attack that can
flow is refused** by our `Attack.Start` prefix (`__result = false`, original skipped): a family whose roll animation is
Off, an ineligible item (torch, tool, bomb, tower shield bash, another mod's renamed swing), a trigger with no known
attack state, the stamina fallback. That is exactly vanilla's `InDodge` refusal: the press stays buffered, and the
attack starts when the roll ends, as without the mod (Decision 24). With the check before the gate, this backstop only
serves pairs, clones other mods changed and the stamina fallback.

**The flow** (2.9 for the mechanics): right after a roll attack started (the `StartAttack` postfix, the trigger already
set on the local Animator), when the local Animator's layer 0 is still in the dodge state (current state tagged
`dodge`, also during its 0.2 s blend out of it, or blending into it), the mod cross-fades it straight into the move's
attack state (`Animator.CrossFadeInFixedTime(state, FlowBlend, layer 0)`, `FlowBlend` default 0.15 s) and resets the
trigger on the local Animator (else the Animator would take the trigger again after the swing and play it twice). With
the Animator in `AnimatePhysics` mode the cross-fade starts in the same physics step; on the next tick `Attack.Update`
sees the attack state (entry after one tick, 0.02 s) and pays the stamina, and `m_inDodge` falls because the next state
is tagged `attack`: the roll is over, and since the attack used the in-roll token, its end gives no second token. The
attack state plays from its start during the blend, so `FlowBlend` changes only the look, not when the hit comes.

**Which presses count:** the vanilla buffer does most of the work. A press during the roll is kept 0.5 s: a press from
`FlowStart - 0.5` s into the roll on (0.35 s at the default), or the button held through the roll, starts the roll
attack at the cut point; a press after the cut point starts it at once. Before the cut point vanilla refuses the attack
every tick (still in the dodge), so holding the button is safe. A new press after the roll also counts while the
Animator is still blending out of the dodge state (about the first 0.2 s after the edge, capped by `Window`): the same
cross-fade replaces the blend to idle. A later press is a normal swing with the vanilla look, whatever `Window` says:
the stand-up has played, and a roll attack from idle would be the stop the user asked to remove (Decision 26). A press
made earlier in the roll is lost, as in vanilla (Decision 7, L6). Any successful attack start clears the tokens: only
the first attack of a roll. `FlowStart` longer than the roll (0.92 s) means no cut: the attack starts on the tick after
the roll's end and cross-fades from its blend out, still without the idle pose.

**Clone edits:** the common ones (2.5) with the Roll numbers.

**What the player sees:** the roll (10 stamina, i-frames for its first 0.42 s), and from 0.85 s into it (the tick at
0.86 s) the family's second swing growing out of the very end of the roll over 0.15 s, with no stop in between (before:
the whole roll, the character standing up to idle for 0.2 s, then the swing, entering 1.14 s after the roll started;
now the swing enters about 0.88 s after the roll started: only the stand-up is gone, Decision 21). Facing is set by the
vanilla chain-level-0 rule and the player's own gameplay option (1.2, Decision 19):

- option "attack towards player look direction" off (the keyboard default): the strike turns to the camera, so
  "roll past, strike back" works by looking back;
- option on (the gamepad default): the strike follows the stick; after a roll the stick is usually still held in the
  roll direction, so the strike goes along the roll. Pull the stick back toward the enemy to strike back; with no stick
  input there is no turn.

The strike has no i-frames. The combo continues from the move (2.5): a sword roll attack is followed by the finisher.
Other players with the mod see the same flow (2.9); players without it (only on a server with
`AllowPlayersWithoutMod`) see the roll finish and stand up, then the swing, a little later than it really hits.

**Edge cases:**

- Block held through a keyboard roll (block + jump): block does not gate `StartAttack`, so the roll attack works;
  blocking resumes after it (vanilla).
- Sneak roll (crouch + jump): a roll attack from sneak, which can also be a backstab on an unaware target (vanilla
  backstab × the move's multiplier; cross-mod notes for Sneak Ambush and Creature Morale in 6.1).
- Roll off a ledge: the roll can end in the air; a roll attack in the air is allowed (cut, or after the roll while the
  Animator still blends out of the roll into the fall).
- Quickstep dash (about 0.25 s instead of the vanilla dive, no dodge state): no cut; the attack after the dash is a
  roll attack through the window, as before; the cooldown (A4) limits back-to-back moves.
- Hit and staggered during the roll: vanilla refuses attacks while staggering; the roll ends in the stagger and gives
  no token.
- Cooldown still running at the cut point: no cut, the attack waits for the roll's end (vanilla) and is a roll attack
  then if the cooldown is over, else a normal swing.
- No attack state known for the trigger (2.9: nothing learned, and the controller probe found none: a fallback that
  should not happen with the game's own controller, since the probe answers before the first roll attack): **no
  cut**. The gate stays shut (or our
  `Attack.Start` prefix refuses a pair's clone), so the attack waits for the roll's end as in vanilla and starts on the
  tick after it, a roll attack in the roll's blend out with the trigger alone (`TriggerOnly`): the Animator plays the
  swing once its blend to idle is over, so that one roll attack shows the old stand-up, and learning then records its
  state (2.9): the next roll attack with that animation flows. Starting the attack at the cut point with the trigger
  alone was the first design; it snapped the player's facing to the attack direction in the middle of the roll (the
  chain-level-0 turn of `Attack.Start`) and so redirected the rest of the roll's root motion, and other players with the mod who knew the state showed the swing before the attacker's own
  Animator played it (about 0.4 s before, at the first default `FlowStart` of 0.7 s).

### 2.5 Editing the clone (both moves)

In the `Attack.Start` prefix, on the per-swing clone only (the item's `SharedData` is never touched):

```
m_attackAnimation        = trigger             (full name, e.g. swing_longsword2)
m_attackChainLevels      = 0
m_attackRandomAnimations = 0
m_damageMultiplier      *= Damage
m_staggerMultiplier      = min(m_staggerMultiplier × Stagger, 99)
m_forceMultiplier       *= Push
m_attackStamina         *= Stamina
m_maxYAngle              = max(m_maxYAngle, AimAngle)      (jump attack only, while airborne: 2.3)
```

- Multiplying (not setting) keeps each weapon's own primary values and composes with other mods that edit the clone
  before us (Dual Wielding's stamina and multipliers). The clone is fresh per swing, so nothing compounds.
- Stagger stays below 100: `Character.RPC_Damage` staggers anything at once from 100 up (1.3).
- Everything else stays the primary attack's: type, reach, arc, height, damage types, status effects, adrenaline,
  noise, speed factors. A borrowed secondary animation (for example `knife_secondary` or `unarmed_kick`) therefore hits
  with the primary's numbers and arc, not the secondary's.
- **Stamina fallback.** Before editing, the prefix sets the clone's `m_character` and `m_weapon` (the first thing
  `Attack.Start` does anyway) and compares the clone's own `GetAttackStamina()` with and without the `Stamina`
  multiplier: when the player can pay the normal swing (`HaveStamina(cost + 0.1)`) but not the move, the clone stays
  vanilla (Debug line). Without this, a `StaminaMultiplier` above 1 would make vanilla retry the failing move every tick
  for 0.5 s (a HUD flash each tick) and end with no attack at all. At the default (1) both costs are equal.
- With chain levels 0 `Attack.Start` fires the name as is at chain level 0, so:
  - the vanilla facing applies (1.2, 2.4);
  - **no finisher ×2 on the move**: a jump attack playing the sword finisher hits for ×1.2 (default), not ×2
    (Decision 3);
  - a move whose trigger is another animation than the family's own chain step (a secondary, another family's clip)
    ends the combo: the next attack starts at the first swing, because the previous attack's name differs from the
    next swing's base name.
- **Combo continuation** (Decision 3). The chain is **the clone's own as it arrived in our prefix** (2.2): its base name
  `<base>` and its chain level count *n*, both saved before our edit (after Dual Wielding's conversion, so a pair's
  chain is its template's: `dualaxes` with *n* = 4, `dual_knives` with *n* = 3), never read from
  `weapon.m_shared.m_attack`. When the trigger is step *k* of that chain (`<base><k>` with *n* > 1 and *k* < *n*), the
  move watch (2.7), on the first tick the clone is in its attack state (`m_wasInAttack`, so after `Attack.Update` wrote
  its own value), sets `clone.m_attackAnimation` back to `<base>` and `clone.m_nextAttackChainLevel` to *k* + 1 (0 when
  *k* + 1 = *n*, after the last step). The move then counts as that step: a press after its `Chain` event, or held,
  continues the combo at the next step through the vanilla transition, exactly as after the normal step *k* (the next
  clone, converted again by Dual Wielding for a pair, has the same base name, so vanilla's `Attack.Start` continues the
  chain). A sword roll attack (`swing_longsword1`) is followed by the finisher (vanilla ×2); a jump attack playing the
  finisher is followed by the first swing, as after a vanilla finisher (finisher clips have no `Chain` event, so a press
  during them waits in the 0.5 s buffer or needs the button held, as in vanilla); a Dual Wielding axe pair's move set to
  `dualaxes2` is followed by `dualaxes3` (with the axe's own 3 levels it would wrongly restart at `dualaxes0`). The
  move's own clone keeps chain levels 0, so it never gets the ×2 itself.
- Stamina: the swing's own cost (`Attack.GetAttackStamina`: equipment, status effects, skill) × `Stamina`, checked by
  vanilla before the trigger fires and paid when the animation starts. The jump or roll was paid before.
- Area attacks (sledges, when turned on) read the same three multipliers (1.3), not the aim angle.

**Balance at the defaults** (estimates from the clip table; tune in game). A move takes the place of one combo step
and the combo continues from it, so over a fight the swing rhythm stays vanilla; the gain is the move's multipliers on
one swing, plus a slightly quicker or slower first hit (table 2.2).

- Sword, vanilla: 1 + 1 + 2 = 4 damage units in about 2.7 s (clips 1.09 + 0.81 + 0.81). After a roll with the mod:
  roll attack 1.3 → finisher 2 → first swing 1 = 4.3 units in the same 2.7 s, +8 % on that one cycle.
- Jump attack: 1.2 units and ×2 stagger (2 stagger units with a sword, against 1 for a normal swing), plus the jump's
  10 stamina; the next swing is the slow opener again. Battleaxe: a jump attack pressed at take-off hits 0.43 s later
  (its opener: 1.01 s) with 3 stagger units; a jump-attack loop (≈ 1.1-1.2 s per jump, 1.2 units) matches the vanilla
  battleaxe combo's ≈ 1.1 units/s while costing a jump and a swing of stamina each time (**flag**).
- Loops: a vanilla roll (no roll during an attack, and the roll attack cuts in at `FlowStart`, 0.85 s, never before the
  i-frames end at 0.42 s plus the 0.2 s margin) or a jump (none during an attack) plus a move clip (0.40-1.50 s) always
  takes more than the 1 s cooldown, so A4 never blocks those (the first runtime run measured 1.62 s for roll → roll
  attack → roll → roll attack before the roll flow; `moveset.roll-input` measures it again). The roll flow makes a roll
  attack enter about 0.25 s sooner after its roll, the stand-up it removes (Decision 21; about 0.4 s with `FlowStart`
  0.7); the swing itself and the combo after it are unchanged.
  Instant dash mods shorten the roll to about 0.25 s: dash + sword roll attack + finisher ≈ 1.9 s for 3.3 units
  (about +20 % over the vanilla combo, with the dash mod's own i-frames). The cooldown blocks back-to-back moves whose
  clips are shorter than about 0.75 s (knives, dual knives, fists) and immediate jump-attack spam with fast weapons; the
  README tells dash-mod users they can raise it.
- Adrenaline: the vanilla per-hit (per-swing for sledges) adrenaline, plus stagger adrenaline that comes sooner with
  the higher stagger multipliers (Non-goals).

### 2.6 A3 Animation settings and runtime check

**Settings.** One setting per family and move (24), in the sections `Jump attack animations` and `Roll attack
animations`, each with a drop-down (`AcceptableValueList<string>`) of 40 values: **that setting's own default first**,
then `Off`, then the rest of the 39 vanilla melee attack triggers: `swing_longsword0-2`, `sword_secondary`,
`mace_secondary`, `swing_axe0-2`, `axe_secondary`, `battleaxe_attack0-2`, `battleaxe_secondary`, `dualaxes0-3`,
`dualaxes_secondary`, `greatsword0-2`, `greatsword_secondary`, `atgeir_attack0-2`, `atgeir_secondary`,
`knife_stab0-2`, `knife_secondary`, `dual_knives0-2`, `dual_knives_secondary`, `spear_poke`, `unarmed_attack0-1`,
`unarmed_kick`, `swing_sledge` (a default of `Off` gives `Off` first). Left out on purpose: `spear_throw` (throws the
weapon), tool and farming triggers, the suffix-less `swing_axe`, `knife_stab`, `dual_knives`, `greatsword` (no weapon
fires them; unknown clips), staves, bows, bombs, emotes. `Off` = that family's move stays a normal swing with no bonus.
Only vanilla names are offered, so every peer's Animator has them, with or without the mod.

**A value typed into the `.cfg` that is not in the list** (a typo, wrong case, a name from another build): BepInEx 5
replaces it on load with the first value of that setting's list (`AcceptableValueList<T>.Clamp`, applied by the
`ConfigEntry<T>.Value` setter), before the mod sees it. Because each list starts with the setting's own default, a typo
gives the default, silently (BepInEx logs nothing, the mod cannot tell). One list per distinct default is built once.

**Runtime check** (`MoveTriggers.Resolve`): per (move, family) the effective trigger is cached for the current rules
snapshot and the current local `Player`; the cache rebuilds when either reference changes (a settings change makes a
new snapshot; the server's rules are a new snapshot; a respawn is a new player). Building it makes the same test as
vanilla `ZSyncAnimation.HasParameter(name, AnimatorControllerParameterType.Trigger)` for each configured name, but
reads the local player's `Animator.parameters` once per rebuild into a set of Trigger names (each read of that property
copies the whole parameter array, about 150 objects). A name received from the server that is not in the
list (a server of another version), or any name that is not a Trigger parameter of this Animator (a game update that
dropped it) → the family default, and if that fails too → Off, with one Warning per (move, family) per session:
`Weapon Moveset: "<value>" is not an attack animation of this game; the <jump attack> of <Swords> uses
<swing_longsword2> instead. Pick another animation from the list in the "Jump attack animations" settings.` When the
rules in force are the server's, the last sentence says so and asks the player to tell the server admin (their own
settings do nothing there, Decision 20). Game updates that drop a trigger degrade to vanilla swings with a warning,
never an exception.

### 2.7 The move watch (A2 start check, combo continuation, aim reset)

When a move started (`StartAttack` succeeded with an edited clone), `MoveTracker` records the watch: the clone, its
trigger, kind, family, base name, chain level count and chain step, its `m_maxYAngle` before our edit, the start time,
the roll counter of 2.4 (which roll it belongs to), for a roll attack whether it cut into the roll and how it left the
roll (2.9), and the `Player` it belongs to. It has two phases.

**Starting** (from the start until the animation enters its state). The start check's clock starts at the attack
start, except for a roll attack that cut into the roll: while that same roll's dodge state still lasts (`m_inDodge`
true, the roll counter unchanged) the clock waits. A cut always cross-fades (no cut without a known attack state, 2.4),
so that is the start tick only; the wait stays as a guard. A roll attack that plays with the trigger alone starts after
the roll, and its 0.5 s deadline counts from its start.

- **No restart.** The `Humanoid.StartAttack` prefix, for the local player, while the watched clone is still
  `m_currentAttack`, not done, not entered (`m_wasInAttack` false) and its clock younger than 0.5 s, sets
  `__result = false` and skips the original, for primary and secondary requests alike. This is what vanilla does once
  the Animator shows the attack (`InAttack()`), a few ticks earlier; without it a held button, a buffered press on the
  next tick, or two physics ticks in one rendered frame would stop the move and start a plain first swing in its place
  (1.2), with the tokens already spent. The mod's other skip is the refusal of an attack that started inside a roll
  without becoming a roll attack (2.4).
- **Entered**: `clone.m_wasInAttack` is true → the starting phase ends (Debug line with the delay from the real
  start), `LastMoveAt = Time.fixedTime` (cooldown, A4) and the combo continuation of 2.5 is applied. At the 0.5 s
  deadline, `InAttack()` true also counts as entered: the watch waits one more tick for `m_wasInAttack` before the
  continuation (kept although the runtime order of 1.1 makes it rare: `Attack.Update` runs before our tick).
- **Interrupted**: a *new* roll starts (the roll counter changed: vanilla starts a queued roll in the restart gap) or
  the player is staggered before entry → the move is dropped at once (trigger reset, clone stopped and taken off the
  player), Debug line, no warning. The roll a roll attack cut into is not a new roll.
- **Dropped by the game**: the clone is no longer `m_currentAttack` (an unequip drops it: `Stop`, `m_currentAttack =
  null`) before it entered → reset the trigger locally (`Animator.ResetTrigger`), unless the new current attack fired
  that same trigger (the name vanilla `Attack.Start` built: base name + chain level for a chain, the name alone
  otherwise; a random-animation attack counts as different); Debug line.
- **Cancel at 0.5 s** without entering and without `InAttack()`: `ResetTrigger`, then drop the clone the way vanilla
  `UnequipItem` does (`Stop()`, `m_previousAttack = clone`, `m_currentAttack = null`, so a late animation can no longer
  hit with the move's numbers: `Humanoid.OnAttackTrigger` does not check `IsDone`), set `m_queuedAttackTimer = 0.5` so
  vanilla starts a normal swing on the next tick (tokens already consumed), and log a Warning once per (move, family)
  per session: `Weapon Moveset: the <jump attack> animation <trigger> did not start for <item> (<airborne | after a
  roll>), so a normal swing was used. Pick another animation from the list in the "<Jump attack animations>"
  settings.` With the server's rules the last sentence asks the player to tell the server admin instead (2.6). A
  dedicated server has no local player, so it never runs this check: its admin tries a custom animation in single
  player or as a host (README).

**Playing** (after entry, until the clone is done or no longer `m_currentAttack`): apply the continuation once
`m_wasInAttack` is seen; for a jump attack, on the first tick with `IsOnGround()` (also before entry), set the clone's
`m_maxYAngle` back to its value before our edit (A1). The watch then ends. Each tick costs a reference compare and two
bool reads; nothing allocates.

Without the reset, a trigger with no transition from the current state (the air, the end of a roll) would stay set
and could play the swing later from another state (1.2). Remote peers received the same trigger and are not reset
(vanilla has no reset RPC): at worst they see a late swing animation with no hit (hits only run on the attacker).
A cross-faded roll attack has already reset its trigger on the local Animator (2.9); a later reset of the same name
changes nothing.

**Safety:** every `ResetTrigger` first checks that the watch's `Player` is still alive as a Unity object (`== null`
check) and is `Player.m_localPlayer`; otherwise the watch is dropped silently (a death or logout within 0.5 s of a move:
`UpdateDodge` stops ticking and the Animator may be destroyed).

### 2.8 UI feedback and log

- The move's animation (seen by everyone) and the vanilla hit, stagger and knockback effects on the target are the
  feedback. Vanilla also flashes the stamina bar when a swing cannot be paid. No new HUD element (Non-goals).
- Debug lines (for tests; built only when a move happens, not per tick):
  `Jump attack: <item> (<family>) plays <trigger>; damage x<d>, stagger x<s>, push x<p>, stamina x<st>, aim <a>°; <own |
  server | self-test> settings.` / `Roll attack: …; cut into the roll <t> s after it started (<how>); …` or `Roll
  attack: …; <t> s after the roll ended (<how>); …`, where `<how>` is `cross-fade from the roll, learned state` /
  `cross-fade from the roll, state from the animation controller` / `no attack state known yet: the game's own
  transition` / `normal transition` (the Animator had already left the roll) / `<Move> <trigger> started after <t> s;
  the combo continues at step <n>.` / `<Move> <trigger> was dropped before it started; trigger reset.` / `<Move>
  skipped: stamina for a normal swing only.` / `<Move> skipped: cooldown (<t> s left).` / `Refused an attack restart
  while <trigger> was starting.` / `Learned the animation state of <trigger>.` (once per trigger and session, 2.9) /
  `Found the animation state of <trigger> in the animation controller (<ms> ms).` or `The animation controller has no
  attack state for <trigger>: a roll attack with it plays after the roll.` (controller probe, once per trigger and
  controller, 2.9) / `Cross-faded <player>'s roll attack <trigger> out of the roll (<source>).` (another player, 2.9).
- Info (client) each time the server's rules arrive and differ from the last ones: `Using the server's move settings:
  …`; Warning once when the server's rules cannot be read (4); the GCO stand-down line (6.2).
- Server, join check (4): Warning `Refused <player>: their game <reason> …` or `<player> joined, but their game
  <reason>. AllowPlayersWithoutMod is on, so …`, where `<reason>` is `NetworkGate.PeerProblem` ("does not have the
  mod", "has another version of the mod (network version N, the server has 2)", "has the mod turned off"); Debug
  `<player> has Weapon Moveset: allowed.` The framework itself logs Info `<player> turned Weapon Moveset off on their
  game.` when a connected player's copy changes.
- Debug builds only: at activation, one line listing the loaded plugins (GUID, name, version), to confirm the GUIDs
  of section 6.2; for each new local player, one line with `m_animator.updateMode` (1.6, R7).

### 2.9 Roll flow: the attack state, and other players (`RollFlow`)

**Cross-fade** (`RollFlow.Cut(animator, trigger, blend)`): when the Animator's layer 0 is in the dodge state (current
state tagged `dodge`, in a transition or not, or blending into a state tagged `dodge`) and an attack state is known for
the trigger: `CrossFadeInFixedTime(state, blend, 0, 0)` and `ResetTrigger(trigger)` on that Animator, result
`CrossFade`. No state known: nothing is changed, result `TriggerOnly` (the controller takes the trigger when it can).
For the local player that only happens after the roll, in its blend out: it never cuts into the roll without a known
state (2.4), so the swing plays once the blend is over. On another player's Animator the trigger can arrive mid-roll
(their cut); with no state known there it waits for that roll's end the same way. Not in the dodge state: result
`None` (the normal transition). Scripted cross-fades interrupt the dodge state and its blend out, whatever the controller's own
transition settings; the attack state keeps its own transitions, tags and events, so `InAttack()`, `Attack.Update`,
the `Hit` and `Chain` events and the combo continuation work as after a normal start.

**Which state** (`RollFlow.StateFor`), first that applies:

1. **Learned**: the state the Animator itself entered for that trigger earlier in this game session, validated with
   `Animator.HasState(0, hash)` (another mod may have swapped the controller). **Learning** (MoveTracker, from the
   `UpdateDodge` postfix): after any successful attack start of the local player whose trigger has no learned state
   that is still in the current controller (`RollFlow.Knows(animator, trigger)` checks `HasState` too, so a state lost
   to a controller swap is learned again and overwritten; a normal swing: the name vanilla fired, `FiredTrigger`; a
   move: its trigger; not a cross-faded roll attack, whose state is already known), the watch reads layer 0's
   next-or-current state each tick; the first state tagged `attack`
   whose hash differs from the one at the start is that trigger's state (a chain step is learned when the Animator
   moves from the previous step into it). It gives up when the attack is replaced or after 1.5 s. Cost: two or three
   Animator reads per tick for the first swings of each trigger, then nothing. Any combo of a weapon teaches its steps,
   so the default roll animations (every family's second combo step) are known after the first two-swing combo.
2. **Probed** (`StateProbe`, the controller probe): Unity cannot list a controller's states at runtime, and the
   player's attack states are not named after their trigger or clip (1.6: the first design looked them up by name, and
   the 2026-09-30 in-world run found none, so the first roll attack of a session with an axe, a two-handed sword, an
   atgeir or a knife played after the roll). So the mod asks a copy of the controller: a hidden `GameObject` with its
   own `Animator` (no avatar, no renderer, `fireEvents` off, `logWarnings` off, `AlwaysAnimate`, far under the world),
   given the same `runtimeAnimatorController`. Every Bool parameter false except `onGround`, every number 0, every
   trigger reset; `statei` and `statef` (the stance, `Humanoid.SetAnimationState`) copied from the Animator being
   served, so the probe stands in the stance of the weapon in hand. The copy is stepped by hand (`Animator.Update`):
   in 0.1 s steps until layer 0 rests (one state, no transition, and it either played its whole clip once or stayed
   2 s; 8 s at most): that idle is the start. Per trigger: `Play` the idle, reset every trigger, set this one, then
   0.02 s steps (one physics tick, as live) until layer 0's next-or-current state is tagged `attack` and is not the
   idle (the learning rule above; 1 s at most): its `fullPathHash` is the answer, 0 when none came. The copy is made
   and destroyed within the one call (set inactive at once, `Object.Destroy`), so it never updates on its own and
   costs nothing per frame. The controller's state machine behaviours run on the copy (Unity gives each Animator its
   own instances): `StateController`s are muted first (enter effect list emptied, child toggles off) because their
   effects would spawn at the copy, except an instance the served Animator also has (a behaviour shared between
   Animators, not Unity's default: muting it would mute the player's; its effect would spawn far under the world);
   `RandomIdle` and `AnimSetTrigger` only set parameters on it. The answer is cached
   per trigger and controller (keyed by the controller's instance id, the last table kept by reference; at most 8
   controllers, then the cache starts over), including "none". **When**: `MoveTracker.Tick` asks on the tick a roll
   starts, for the current weapon's roll animation (a weapon in each hand: the `DualAxes` and `DualKnives` roll
   animations, the rows of Dual Wielding's default templates), so the one-time cost of each animation (a few
   milliseconds, measured by the `moveset.triggers` NOTE) falls at the start of the roll, not on the cut tick 0.85 s
   later; any other miss (another pair template, another player's Animator in `OnRemoteTrigger`, a swapped
   controller) is probed when first asked. Learned states keep priority (exact, from the live Animator), and learning
   still runs for every first swing, so a controller that differs from its probe copy is corrected by the first combo.
3. **None**: `TriggerOnly` (the probe failed, or a controller swapped in by another mod has no such state).

**Other players** (`ZSyncAnimation.RPC_SetTrigger` postfix, on every peer with the mod active): when the trigger that
just arrived is one of the roll attack animations of the rules in force (all peers share the server's rules) and
`RollAttack` is on, the `ZSyncAnimation` belongs to a `Player` that is not the local player, and that player's Animator
is in the dodge state: the same `RollFlow.Cut` on that Animator, with the rules' `FlowBlend`. So every player with the
mod sees another player's roll attack grow out of the roll as the attacker does; no RPC is added. The filter order
keeps it cheap for the many other triggers (monsters, jumps, equips): the rules flag and twelve string compares first,
the Animator and component reads only for a roll attack animation. On a remote player the attack trigger can only
arrive during the dodge state for a roll attack of a modded player, or at the very end of a vanilla player's roll, where
cross-fading instead of waiting 0.2 s is harmless (visual only). Players without the mod keep the vanilla order.

**Nothing persistent**: learned and probed states live in dictionaries for the game session (cleared on activation
and deactivation); nothing is written to items, players, the world or the config, and the probe's copy is gone at
the end of the call that made it.

---

## 3. Decisions

1. **v1 = jump attack + roll attack; the sprint attack moves to Later (L1).** **Flag: deviates from the user's literal
   v1 list ("Jump attack; Sprint attack"), as the user allowed ("investigate if the Sprint attack can be replaced by
   the roll attack").** Options: A jump + sprint, B jump + roll, C all three. Evidence for B: both are equally cheap to
   detect (`IsRunning` vs one field edge); the roll attack reuses a path vanilla already exercises (the buffered attack
   at the end of a roll: only the trigger and the numbers change; the user's first test then asked for the flow out of
   the roll, Decision 21, which adds a script cross-fade), so it has the lowest animation risk, while a vanilla
   attack from a sprint stops the player in about three physics ticks (`m_speedFactor` 0.1-0.3 × run speed, the
   deceleration of `UpdateWalking`), so a sprint attack only feels distinct with movement tuning (lunge, speed factor)
   plus a speed gate per family, the riskiest and most tuning-heavy part; the sprint attack duplicates GCO's running
   attack on the same input (ours would have to turn off next to it), while no Valheim mod has a roll attack (unique);
   the roll attack is also on the idea sheet, the sprint attack is not; run stamina keeps draining during a vanilla
   sprint swing. Against B (research brief): **rolling then attacking is the most common vanilla pattern, so every
   such attack changes**; hence modest bonuses, the combo continuing from the move (Decision 3), roll attacks Off for
   families without a fitting animation (Decision 12) and a cooldown (A4). Both research briefs recommend B. A running
   jump attack already gives v1 a gap-closer. Choice: B, with the detector list kept generic so L1 is one more
   detector.
2. **Moves replace the primary attack only.** The secondary keeps its vanilla role everywhere (the sheet asks for
   "roll then attack", "jump then attack"; PPR and SecondaryAttacks own the secondary slot).
3. **Play the move by renaming the clone's trigger (option A), then let the combo continue from it.** A: full trigger
   name with chain levels 0 → own multipliers instead of the finisher ×2, vanilla facing applies; proven by
   SecondaryAttacks, KorCaptain's rules and GCO ("does not inherit the finisher spike"). B (force the chain level)
   keeps the finisher's ×2 but skips the facing snap (chain level ≠ 0, so the mod would rotate the player itself).
   A alone makes every move a combo dead end: presses during it wait only 0.5 s and the combo restarts at the first
   swing, so the most common vanilla loop (roll, then combo) would become roll, move, full recovery, then the combo
   from the start. So when the move plays a step of the clone's own chain (the weapon's, or a Dual Wielding pair's
   template chain), the move watch restores the base name and sets the next chain level at entry (2.5): the move
   counts as that step, and the combo continues through the vanilla transitions (roll attack → finisher; after a
   jump-attack finisher, the first swing). Chosen over "the move counts
   as the opener, then step 1" (a reviewer's variant), which would need a transition from a step to itself (roll
   attack `swing_longsword1` → `swing_longsword1`, unverified). **Flag:** a jump attack that plays the sword finisher
   hits ×1.2, below the vanilla finisher's ×2 (L9); a roll attack is followed by the real finisher.
4. **Default animations per family by look and hit time** (table 2.2): jump = the family's last combo step, roll = the
   second step, fists `unarmed_attack1` for both, battleaxe roll attack Off (its second step would open 0.7 s faster
   than the slow opener that defines the weapon). Options: finisher for both (the older backlog sketch), secondaries
   (long wind-ups: the knife and dual knives "leap" clips hit after 0.84-0.88 s, mostly after landing; the kick keeps
   punch numbers and locks the player 1.77 s), cross-family clips (clip through shields). Choice: own-family chain
   triggers, which mods prove play from idle and a run, keep SDW DualWield and KG animation swappers working like the
   normal combo, never clip a shield more than the normal combo, and make the two moves look different from each other
   and from the first swing. Every choice is a setting (A3). **Flag** the table, in particular the battleaxe jump
   attack (a fast opener paid with a jump) and its roll attack Off.
5. **Animations are server settings chosen from a list of vanilla triggers, checked with `HasParameter`, with a
   fallback** (A3, beyond the request: open question 7). A hit's timing depends on the clip, so it is gameplay
   (server-synced like the numbers). Vanilla names only, so players without the mod see the right animation. Each list
   starts with its own default so BepInEx's clamp gives the default for a typo (2.6).
6. **Jump attack = only after a jump the player makes (`Character.Jump` from input that reached `ForceJump`), first
   attack only, until landing.** Matches "jump then attack" and GCO's "deliberate jumps qualify immediately" and its
   AllowOnlyOneJumpAttack mode (GCO's default for one-handed swords and maces; applied to every family here). Ledge
   drops (GCO: after 1 s of airtime) are L4. A jump taken while an attack is starting gives no token (2.3). **Flag
   (vanilla-consistent choice):** on keyboard and the Default gamepad layout a jump attack cannot start from sneak or
   with block held, because vanilla turns those presses into a roll; on the Alternative gamepad layouts vanilla jumps
   there, and the jump attack follows (no special case: a jump is a jump).
7. **Roll attack = the first attack of a roll: from the cut point inside the roll (Decision 22) or within `Window`
   (0.4 s) after the roll ends, after a vanilla roll only while it still blends out (Decision 26); no extra input
   buffer.** The vanilla 0.5 s buffer and holding the button already cover presses late in the roll. **Flag
   (vanilla-consistent choice):** a press made more than 0.5 s before the cut point (in the first 0.35 s of a roll at the
   default) is lost, as in vanilla (L6 would keep it).
8. **A small cooldown (A4), no attack-cancel loop guard in v1.** With vanilla rolls and jumps a move is already paced
   by the roll or the jump (2.5), so the cooldown is a guard for instant dash mods and jump spam, measured from the
   previous move's start (entry) and set so it never blocks a move after a vanilla roll or jump (1 s, to confirm with
   the roll and airtime NOTEs). The attack-cancel mods' attack → roll → attack loop exists without this mod; ours only
   adds the move's bonus to it (README note, L10).
9. **The damage multiplier applies to whatever the move hits, trees and rocks included** (the clone's vanilla
   `m_damageMultiplier`, 1.3). Options: this, or a creature-only bonus through an attacker-side `Character.Damage`
   prefix inside a scope around the move's `OnAttackTrigger` (two more patches). A roll or jump costs more time and
   stamina than a chop it speeds up by 20-30 %, so v1 keeps the simple path. **Flag**; L7 if chopping abuse shows up.
10. **Stagger stays below 100 on the clone**, so a move never uses vanilla's instant-stagger rule.
11. **Moves stand down whenever vanilla context differs:** secondary attacks, a queued chain (`InAttack()`), swimming,
    attached (ship, chair, saddle, ladder), non-weapon kinds and the Blocking skill, non-melee attack types, reload and
    bow-draw attacks, unknown families, the cooldown, and a stamina bar that pays a normal swing but not the move. Any
    successful attack start consumes the tokens, so "the first attack after" is exact and a secondary or a bow shot
    after a jump uses up that jump.
12. **Spears and sledges have both moves Off by default.** Options: own single animation with the move's numbers (the
    move would look exactly like the normal attack, and there is no other cue: the player cannot tell a move
    happened), borrow another family (two-handed atgeir clips on a one-handed spear with a shield), or Off (GCO gives
    them nothing; the research brief recommends Off; a sledge's 1.16 s wind-up lands only after touchdown). Choice: Off;
    the settings still offer `spear_poke` / `swing_sledge` (numbers only) or any other animation. **Flag.**
13. **Dual Wielding composes before us**, per its design's contract (its 6.1): our `Attack.Start` prefix runs after
    `MC.Combat.Weapons.DualWield`'s (`[HarmonyAfter]` + `Priority.Low`; theirs is `Priority.High`) and reads the family
    from the clone's current name, so a dual pair uses the `DualAxes` / `DualKnives` rows (the pair's dual trigger
    set: `DualKnives` for two knives, `DualAxes` for every other pair, swords and maces included, with Dual Wielding's
    default templates), and its chain (base name and level count) from the converted clone (2.5). Dual Wielding maps each hit event
    of a swing by the trigger actually fired, recorded in its `Attack.Start` postfix after our prefix and vanilla's
    chain logic, through a fixed table per trigger with the event index reset every swing: our jump attack `dualaxes3`
    strikes main then off, our roll attacks `dualaxes1` and `dual_knives1` strike with the off hand, and the next swing
    maps by its own step (`dualaxes2` = main, then off). Our entry-time rename changes nothing (the pattern was recorded
    at start), and nothing carries over between swings.
14. **Both side with join check and server settings** (house rule for mods that change combat), although every piece
    runs on the attacker's game. The join check uses the framework's verdict `NetworkGate.PeerCompatible` (has the mod,
    the same network version, not turned off on their game) and re-checks a player who turns the mod off or on while
    connected (4). `AllowPlayersWithoutMod` (default off) lets in players without the mod, with another version, or
    with it turned off; their own jumps and rolls stay vanilla.
15. **GCO installed → our jump attack stays off, the roll attack stays on.** Both mods would change the same
    jump + primary input; GCO has no roll attack. One Info line says so. An attack in the air after a jump stays
    GCO's even right after a roll: the jump owns it (2.3), so our roll attack never lands on GCO's jump attack.
16. **The start check cancels, warns and falls back to a normal swing; it never disables a move for the session.** A
    failure can be specific to one state (a quick landing), and silently turning a move off would hide it; the
    self-tests prove the defaults. The re-queued press keeps the player's input from being eaten.
17. **No persistent state.** Nothing in `ObjectDB`, `SharedData`, items or ZDOs: the live toggle has nothing to put
    back, and removing the mod leaves nothing behind.
18. **Refuse attack restarts while a move is starting** (2.7), rather than consuming the tokens at entry and
    re-applying the move to each restarted clone. Both fix the restart gap; refusing is simpler (one check, no second
    clone, no second trigger), matches what vanilla does a few ticks later, and cannot lose the move to a second
    trigger name. Cost: the `StartAttack` prefix skips the original in that window (at most 0.5 s, normally 1-3 ticks).
19. **Facing after a roll stays vanilla** (the player's own "attack towards player look direction" option, 2.4), no
    forced turn to the camera. The option is the player's choice of control scheme; with it on, "roll past, strike
    back" needs the stick pulled back. **Flag.**
20. **A client connected to a server uses only the server's rules**: until they arrive, or when they cannot be read and
    none came before, both moves are off (vanilla swings), instead of the client's own config (4). A client's own
    numbers must never apply on a server that enforces the same moves. This is the combat mods' shared pending rule
    (a client's own gameplay settings never apply on a server with the mod; while the server's rules are pending, the
    mod plays vanilla), and it costs Moveset nothing: no move can start before the player spawns, and nothing of ours
    is applied at load time. A mod that must act at load time (Dual Wielding's saved pairs at `Player.Load`) documents
    its own exception.

**User feedback after the first in-game test (2026-09-30)** — build 3cda33b+dirty: "The roll attack needs to flow from
the roll itself without interruption. Right now there is the roll, full reset to idle state, then the attack."

21. **The roll attack cuts into the late roll and a script cross-fade takes the Animator straight into the attack**
    (2.4, 2.9). Cause (1.5): vanilla lets an attack start only once the Animator has begun leaving the dodge state, and
    the Animator finishes its 0.2 s blend to idle before it takes the attack trigger. Options: (A) start the attack
    earlier and rely on the controller having a transition from the dodge state on the attack trigger (unknown graph
    data; nothing to fall back on if it has none); (B) start it earlier and cross-fade the Animator into the attack
    state by script (`CrossFadeInFixedTime`), which needs the state's hash; (C) speed the Animator through the rest of
    the roll (fast-forwards the roll instead of flowing out of it, and `Animator.speed` is also written by the game).
    Choice: B: the attack starts at the cut point through vanilla `StartAttack` / `Attack.Start` (stamina check and
    payment, facing, trigger RPC, hit events, combo all vanilla), then the mod cross-fades and clears the trigger
    locally. With no known state there is no cut: the attack waits for the roll's end as in vanilla and plays with the
    trigger alone (A as a fallback only there, where the controller's own transitions apply), and learning records the
    state for the next time (2.4; A at the cut point snapped the facing and bent the roll mid-roll). The attack state
    comes from what the Animator itself entered for that trigger earlier (learned, exact) or from a hidden copy of the
    controller fed the same trigger (probed, Decision 27; the first design looked it up by its clip name, and no state
    carries one); both are validated at runtime and by the self-tests. **Hard rule:** the roll's
    i-frames never extend into the attack, on any peer: the cut waits `IframeMargin` (0.2 s) after `DodgeMortal`, so the
    ZDO flag other peers read (1.5) says "not invulnerable" on their side too before the swing starts (at the default
    `FlowStart` the margin never binds; with `FlowStart` 0 it moves the cut from 0.42 s to about 0.64 s). **Flag
    (gameplay):** the roll attack now enters about 0.88 s after the roll starts instead of about 1.14 s (vanilla roll
    then attack): "roll, strike" is about 0.25 s quicker, the stand-up it removes; the player is vulnerable from 0.42 s
    either way. `FlowStart` tunes it (Decision 22).
22. **The cut point is a synced setting in seconds after the roll starts, `FlowStart` = 0.85 s (0-2), with the i-frame
    floor (i-frames end + `IframeMargin`).** Options: seconds after the roll starts (readable, what the player feels),
    seconds after the i-frames end, the dodge state's normalized time (robust to other animation speeds, but meaningless
    to players). It is gameplay (it decides when a hit can come), hence synced like the numbers. The default 0.85 s is
    near the end of the 0.92 s dodge state (the tick at 0.86 s, about 1.4 s into the 1.50 s `Dive` clip): the whole roll
    plays and only the 0.2 s stand-up goes, which is all the user asked for. The first default, 0.7 s (the last quarter
    of the dodge state, about 1.15 s into the clip), also cut the end of the roll: the attack replaced about 22 % of the
    dodge state and came about 0.4 s sooner than vanilla, and since the dodge moves the player by root motion
    (`Character.AddRootMotion` counts it only while `InDodge` or `InAttack`, and the dodge's share fades out over the
    blend) a roll that ends in a roll attack probably covers less ground (unverified: T19 compares). With the ×1.5
    stagger that strengthens roll-attack loops, a balance change the user did not ask for (their feedback on the tower
    shield bash, "too fast", points the same way), so 0.7 stays a documented option for a snappier feel. 0 = as soon as
    the i-frames and their margin are over (about 0.64 s); above the roll's length (0.92 s) = no cut, the attack starts
    as the roll ends and still cross-fades out of its blend. **Flag** the default for tuning in game (T19).
23. **The blend length is a synced setting, `FlowBlend` = 0.15 s (0-0.5).** It changes only the look (the attack state
    plays from its start during the blend, so the hit timing does not move); synced so every peer with the mod blends a
    player's roll attack the same way. 0.15 s is a little shorter than the game's own 0.2 s blend out of the roll, so the
    swing's wind-up stays visible.
24. **Only roll attacks flow; every other attack after a roll stays vanilla.** A normal swing of a family whose roll
    attack is Off (battleaxes, spears, sledges), an ineligible item, a secondary attack, the stamina fallback, a
    cooldown still running, `RollAttack` off: none starts inside the roll (our `Attack.Start` prefix refuses an attack
    that started inside the roll only because we opened the gate, 2.4; a second skip in the mod, the vanilla refusal
    moved one step later), and they keep the vanilla blend to idle after the roll. Options: let every attack flow
    (changes vanilla for weapons the user did not ask about, and for secondaries); decide the family before the gate
    (exact for a single weapon, from its own primary attack; not for a Dual Wielding pair, whose clone changes family
    inside `Attack.Start`). Both are used: the check before the gate (2.4) keeps the gate shut for a weapon that cannot
    flow, so vanilla refuses it with no clone made each tick, and the `Attack.Start` refusal stays as the backstop for
    pairs, clones other mods changed and the stamina fallback. On other players, only a trigger that is a roll attack
    animation of the rules in force cross-fades (2.9).
25. **Rules layout 2, network version 2.** `FlowStart` and `FlowBlend` join the synced rules after `Window`; per the rule
    of section 4 the `MoveRules` layout and `ModNetworkVersion` go to 2, so a player with an older test build is refused
    with "has another version of the mod" instead of reading a wrong package. The cross-fade on other players needs no
    RPC: it keys on the vanilla trigger RPC every peer already receives.
26. **After a vanilla roll, a roll attack only while the Animator still blends out of the roll; `Window` alone only
    after a dash.** Found in review: with `Window` 0.4 s, a new press 0.2-0.4 s after the roll (a natural reaction time)
    got a roll attack and its bonuses while the Animator was already out of the dodge state, so the player saw the roll,
    the stand-up, then the roll attack: the exact sequence of the user's feedback, which the README promised could not
    happen. Options: (A) lower the default `Window` to about the blend out (0.2 s): true at the default only, and any
    longer `Window` brings the stop back; (B) grant the after-roll roll attack only while layer 0 is still in the dodge
    state or its blend out (the cross-fade's own test), `Window` as a cap. Choice: B, the rule the user asked for holds
    for every setting. `Window` keeps its full meaning after a dash from a dash mod (no dodge state, so nothing to flow
    from, 6.2), and its default stays 0.4 s so those mods work as before. **Flag:** after a vanilla roll, `Window` above
    about 0.2 s changes nothing (the setting text says so; T08, T22).

**In-world run of 2026-09-30** — `moveset.roll` failed for axes, two-handed swords, atgeirs and knives: their first
roll attack played after the roll (`TriggerOnly`, gap 0.22 s), because no state was learned yet and none is named after
its clip. The user's rule is that every roll attack flows out of the roll, the first one of a session included.

27. **The attack state comes from a hidden copy of the controller, asked once per animation** (2.9, `StateProbe`).
    Options: (A) keep learning only (the first roll attack with each animation per session keeps the stand-up: breaks
    the rule); (B) a table of state hashes or names read from the game's asset files (unknown names today, breaks
    silently on a game update or a controller-swapping mod, and cannot follow a custom animation choice); (C) feed the
    trigger to a copy of the live controller and read which state it enters: the same rule as learning, from the same
    controller object, for any animation a setting names and any controller another mod swaps in; (D) set the trigger
    on the player's own Animator and step it (visible, and it would play the swing). Choice: C. The copy lives only
    inside the call that asks (a few milliseconds, once per animation and controller per session, at the start of the
    roll), updates only by hand, fires no animation events, and its `StateController` behaviours are muted, so nothing
    shows, sounds or changes in the world; the stance parameters come from the Animator it serves. Learned states keep
    priority, and learning keeps running, so any difference between the copy and the live Animator is corrected by the
    first swing. The self-tests check the copy against the live Animator (the clip of every probed state, the learned
    and entered state hashes, the player's own behaviours untouched). **Flag:** the muting relies on Unity giving each
    Animator its own state machine behaviour instances (Unity's default; the game marks none as shared); the probe never
    mutes an instance the player's Animator also has, and `moveset.triggers` checks that none is shared.

---

## 4. Multiplayer

| Piece | Runs on |
|---|---|
| Jump token, roll edges, move decision, clone edit, roll cut and its cross-fade, move watch, state learning | the attacking player's own game (owner of its `Player` ZDO): all these patches filter `Player.m_localPlayer` |
| The move's animation for others | every peer: the vanilla `SetTrigger` RPC with a vanilla trigger name; their own Animator plays it (players without the mod too). A roll attack's trigger that arrives while that player's Animator is still in the roll is cross-faded out of the roll on every peer with the mod (`ZSyncAnimation.RPC_SetTrigger` postfix, 2.9); peers without it take it after the roll's blend to idle |
| Hit detection, `HitData` (damage × multiplier, push, stagger multiplier) | the attacker's game |
| Damage, block, armor, stagger bar, backstab, kill credit | the victim's owner, vanilla `Character.RPC_Damage` (with or without the mod) |
| Settings | the server (dedicated or host) sends its rules to every player with the mod |
| Join check | the server |

**RPCs** (per-player `ZRpc`, never routed; vanilla games ignore unknown names):

| RPC | Direction | Payload | When |
|---|---|---|---|
| `<GUID>.Hello` / `<GUID>.HelloAck` | client ↔ server | framework (`NetworkGate`: network version, release version, and in the hello whether the client's own copy is on) | at connect; the ack again when the server's copy turns on or off |
| `<GUID>.HelloState` | client → server | framework (`NetworkGate`): `"on"` / `"off"` | when the client's own copy turns on or off while connected (panel, config, a failed start); the server raises `NetworkGate.PeerStateChanged` |
| `MC.Combat.Weapons.Moveset.SettingsRequest` | client → server | `int` layout (2) | the client's `RPC_PeerInfo`, and when the client's copy turns on while connected |
| `MC.Combat.Weapons.Moveset.Settings` | server → client | `ZPackage` `MoveRules` layout 2: `int` layout, `bool` JumpAttack, `bool` RollAttack, `float` Cooldown, 5 `float` (Jump Damage, Stagger, Push, Stamina, AimAngle), 7 `float` (Roll Damage, Stagger, Push, Stamina, Window, FlowStart, FlowBlend), `int` family count (12), 12 `string` jump triggers, 12 `string` roll triggers (family order of table 2.2) | the answer to a request; to every ready player with the mod when the server's settings change (0.5 s after the last change, so a slider drag sends once) and at once when its copy turns on |
| `Error` (vanilla) | server → client | `int` 3 (`ErrorVersion`) | the join check refuses a player |

- **ZDO keys:** none. Nothing is stored on items, players or the world.
- **`ModNetworkVersion` 2** (1 until the roll flow added two numbers to the rules, Decision 25). Bump when an RPC name or the `MoveRules` layout changes.
- **Server settings** (every setting of section 5 except `AllowPlayersWithoutMod`, which only the server reads, and the
  framework's `Enabled`): single player, host and dedicated server use their own config. A client connected to a server
  uses only the server's rules for that `ZNet` session (`ServerRules.Current`). **Pending policy** (the combat mods'
  shared rule, Decision 20): until the first readable rules arrive, both moves are off (vanilla swings), never the
  client's own config; an unknown layout or a family count other than 12 → Warning once, the last readable rules from
  this server stay, or both moves stay off when there were none; numbers are clamped to the config ranges (Warning);
  trigger names go through the client's own check (2.6: unknown or missing → family default + one Warning). A client
  whose copy turns off forgets the server's rules (`ServerRules.Stop`); turned on again, it asks for them again and the
  moves stay off until they arrive.
- **Join check** (`PlayerCheck`, copy of Forge Idol Upgrades' flow, verdict from the framework): the
  `ZNet.RPC_PeerInfo` postfix schedules a ready peer 1 s later (grace); at the deadline the verdict is
  `NetworkGate.PeerCompatible(peer)`: the player has the mod, the same `ModNetworkVersion`, and has not turned it off on
  their game (their hello and `HelloState` say whether their copy is on; a copy that says nothing counts as on).
  Not compatible, with `AllowPlayersWithoutMod` off → vanilla `Error(ErrorVersion)` ("Incompatible version" on their
  side), disconnect 4 s later through vanilla's kick list; on → let in with a Warning. Both log lines name
  `NetworkGate.PeerProblem(peer)` (2.8). `PlayerCheck.Start` subscribes to `NetworkGate.PeerStateChanged` and
  `PlayerCheck.Stop` unsubscribes; the handler calls `Schedule(peer)` with the same 1 s grace, so **a player who turns
  the mod off while connected is refused about a second later** unless `AllowPlayersWithoutMod` is on (a player who
  turns it back on before the deadline is not refused: the check reads the state at the deadline). Without this, such
  a player would play vanilla moves on a server meant to enforce the same moves, and so would a player with another
  network version (their own copy goes inactive, ServerMismatch). The framework's own messages are the only mechanism:
  no mod-specific RPC or version test.
- **Hand-off cases** (a player allowed in with the mod turned off or with another version plays exactly like one
  without the mod):
  - A creature simulated by a player without the mod (allowed in) is hit by a move: its owner applies the damage,
    push and stagger the attacker computed (plain `HitData` fields), so the move works on it.
  - PvP against a player without the mod: same; their own jumps and rolls stay vanilla.
  - A creature another peer owns attacks a player during a roll attack: its owner's game checks the hit and reads the
    player's i-frames from the ZDO (1.5), which lags the player's own game by the ZDO send and the latency. The cut
    waits 0.2 s after the i-frames end (`IframeMargin`, Decision 21), so that peer sees them over before the swing
    starts and the hit lands (M10).
  - A player without the mod watching: sees the vanilla animation of the move's trigger. A roll attack shows as the
    whole roll, the stand-up to idle, then the swing: their Animator takes the trigger only after its blend out of the
    roll, about 0.25 s after the attacker's swing started at the default `FlowStart` (about 0.4 s at 0.7; the hit is
    the attacker's, so it may land before their
    screen shows the swing; visual only). A move cancelled by the attacker's start check may still play late on their
    screen, without a hit.
  - Items, structures, creatures: nothing of ours is attached to them.
- **Ownership changes:** none (a player object is always owned by its own game; creatures only receive vanilla
  `HitData`).
- **Dedicated server:** loads the mod (Both mods have no `BepInProcess`), runs the join check and the settings sync;
  it has no local player and simulates no combat, so the gameplay patches have nothing to do.
- **The server turns the mod off:** the framework turns every client's copy off (their moves stop); back on, clients
  turn on and ask for the rules again, and every connected player is checked again (`PlayerCheck.Start` →
  `ScheduleAllConnected`).
- **A player turns the mod off on their own game** (MC Mods panel, config, or their copy fails to start): their game
  sends `HelloState "off"`, the server logs it and re-checks them after the grace: refused, or let in with a Warning
  when `AllowPlayersWithoutMod` is on (their moves are vanilla from that moment). Turned back on while still
  connected: `HelloState "on"`, the check passes, their copy asks for the rules and the moves come back once they
  arrive.

---

## 5. Config

All settings apply live. "Synced" = in multiplayer the server's (or host's) value is used by everyone; those
descriptions end with " In multiplayer the setting of the server (or host) is used for everyone."

| Section | Key | Default | Range | Synced | Description (user-facing) |
|---|---|---|---|---|---|
| General | Enabled | true | | no (framework) | Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | | read-only | no | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | false | | server only | Used only by the server (or the host). Off: a player whose game does not have this mod, has a version that cannot talk to this one, or has it turned off, is refused about a second after joining or after turning it off (their game shows "Incompatible version"), so every player fights with the same moves and numbers. On: such players may play; their own jumps and rolls stay normal. |
| Moves | JumpAttack | true | | yes | Jump, then attack before you land: the first attack of the jump becomes a jump attack with its own animation and bonuses. Off: attacks in the air after a jump stay normal swings, even right after a roll. |
| Moves | RollAttack | true | | yes | Roll, then attack: the first attack of the roll becomes a roll attack with its own animation and bonuses, and it flows straight out of the end of the roll. |
| Moves | Cooldown | 1 | 0-10 s | yes | Minimum time between the start of one jump or roll attack and the next one (either kind); an attack inside that time is a normal swing. A normal roll or jump already takes longer, so this mostly limits instant dash mods and fast jump-attack spam. 0 = no limit. |
| Jump attack | DamageMultiplier | 1.2 | 0.1-5 | yes | Damage of a jump attack compared with a normal swing of the same weapon (1 = the same). It also applies to trees and rocks. |
| Jump attack | StaggerMultiplier | 2 | 0-10 | yes | How much faster a jump attack fills the target's stagger bar (1 = like a normal swing). |
| Jump attack | PushMultiplier | 1 | 0-5 | yes | Knockback of a jump attack (1 = like a normal swing). |
| Jump attack | StaminaMultiplier | 1 | 0-5 | yes | Stamina of a jump attack's swing, on top of the jump itself (1 = the swing's normal cost). If you can pay a normal swing but not the jump attack, you get a normal swing. |
| Jump attack | AimAngle | 30 | 0-45 | yes | Up to how many degrees a jump attack's swing tilts toward where you look (down or up) while you are in the air, so you can strike an enemy below you (or a flyer above). The game tilts a swing less than your view (looking 30 degrees down tilts it about 27 degrees) and never more than 45 degrees, hence the limit. Once you land the swing is level again. 0 = always level, like a normal swing. No effect on sledges (their slam hits an area). |
| Roll attack | DamageMultiplier | 1.3 | 0.1-5 | yes | Damage of a roll attack compared with a normal swing of the same weapon. It also applies to trees and rocks. |
| Roll attack | StaggerMultiplier | 1.5 | 0-10 | yes | How much faster a roll attack fills the target's stagger bar. |
| Roll attack | PushMultiplier | 1 | 0-5 | yes | Knockback of a roll attack. |
| Roll attack | StaminaMultiplier | 1 | 0-5 | yes | Stamina of a roll attack's swing, on top of the roll itself. If you can pay a normal swing but not the roll attack, you get a normal swing. |
| Roll attack | Window | 0.4 | 0.05-2 s | yes | How long (seconds) after a roll ends a new press of the attack button still makes a roll attack. After a normal roll only while you are still getting up from it (about the first 0.2 seconds), so a roll attack always flows out of the roll: a later press is a normal swing, whatever this value. The whole time counts after a dash from a dash mod (it has no roll animation). A press during the roll (the game remembers a press for half a second) or holding the button makes the roll attack cut into the end of the roll instead (see FlowStart). |
| Roll attack | FlowStart | 0.85 | 0-2 s | yes | How long (seconds) after the start of a roll an attack can cut into it: from then on a pressed, remembered or held attack ends the roll and the roll attack flows straight out of it, with no stop in between. A normal roll lasts about 0.9 seconds, so the default plays almost all of it and only removes the stand-up; a lower value (for example 0.7) gives a snappier roll attack that cuts the end of the roll short. The roll's invulnerability ends about 0.4 seconds in, and the attack never starts until 0.2 seconds after that, whatever this value, so no other player's game still sees you invulnerable. Longer than the roll = the attack starts as the roll ends, still without the stop. |
| Roll attack | FlowBlend | 0.15 | 0-0.5 s | yes | How long (seconds) the animation blends from the roll into the roll attack. Only the look changes: the swing hits at the same time. 0 = an instant switch. |
| Jump attack animations | Swords, Maces, Axes, Battleaxes, DualAxes, Greatswords, Atgeirs, Knives, DualKnives, Spears, Fists, Sledges | table 2.2 | list (2.6), own default first | yes | Animation of the jump attack for <one-handed swords / maces and clubs / …>: one of the game's melee attack animations, or Off (a normal swing, no bonus). The move keeps the weapon's own reach, hit arc and damage; an animation of another weapon type can look odd or clip through a shield. A value that is not in the list is replaced by the default when the game starts. |
| Roll attack animations | same 12 keys | table 2.2 | list, own default first | yes | Same, for the roll attack. |

- The numbers need in-game tuning, hence settings. Ranges use `AcceptableValueRange` (bounds shared with
  `MoveRules` so received values clamp to the same ranges); the animation keys use one `AcceptableValueList<string>`
  per distinct default (2.6).
- `Config.SettingChanged` (any section but General) → `ServerRules.OwnChanged()` (new own snapshot, and the server
  pushes it again from `ZNet.Update` once the settings have not changed for 0.5 s, `ServerRules.PushDelay`); the
  trigger cache follows the new snapshot by reference (2.6).
  `AllowPlayersWithoutMod` switched to false → `PlayerCheck.ScheduleAllConnected()`.
- No personal (non-synced) gameplay setting.

---

## 6. Compatibility

### 6.1 MC mods

Four sibling mods patch `Humanoid.StartAttack(Character, bool)`: Sneak Ambush (always-on prefix that can skip the
original), Tower Shield Wall (prefix that skips the original for a bash inside its `BashCooldown`, and postfix), Dual
Wielding (`Priority.First` prefix and finalizer) and this mod
(prefix that can skip the original, postfix, finalizer). Ours tolerates a skipped original: its logic runs only in the
postfix on `__result == true`, and its finalizer clears `Pending` / `Applied` and closes the roll gate (puts
`m_inDodge` back, 2.4) on every path. Our own skip (2.7) acts only while one of our moves is starting; the other mods'
postfixes and finalizers still run and see `__result` false. While our prefix has opened the roll gate for a roll
attack (2.4), the prefixes that run after ours see `InDodge()` false for that call; none of the sibling prefixes reads
it. No sibling mod patches `ZSyncAnimation.RPC_SetTrigger`, and only Dual Wielding shares `Attack.Start` (next table).

| Mod | Overlap | Result / action |
|---|---|---|
| **Dual Wielding** (`MC.Combat.Weapons.DualWield`, same run) | Both edit the per-swing clone in an `Attack.Start` prefix; both patch `Humanoid.StartAttack` (theirs records "secondary requested" and refuses a start, skipping the original, while a hand swap is queued: the swap key is an equip like any other, with the vanilla equip animation and time). Their crossed sheathed pair (`CrossSheathedPair`) is visual only. | Per their contract (their 6.1): ours runs after (`[HarmonyAfter("MC.Combat.Weapons.DualWield")]`, `Priority.Low`; theirs `Priority.High`) and reads the family from the clone's current name, so a pair uses the `DualAxes` / `DualKnives` rows; chain levels 0 for a one-off move. The combo continuation and the aim reset use the converted clone's values (template-owned: `dualaxes` 4 levels, `dual_knives` 3, `m_maxYAngle`), saved before our edit, never the main weapon's (2.2, 2.5). Hands: their `Attack.Start` postfix reads the trigger actually fired (after our rename and vanilla's chain logic) and maps each hit event by a fixed table per trigger, the index reset every swing: our jump attack `dualaxes3` = main then off, our roll attacks `dualaxes1` and `dual_knives1` = off hand, the continued `dualaxes2` = main then off. Our entry-time rename changes nothing (the pattern was recorded at start); nothing carries over between swings. Our multipliers compose with theirs. An attack we let start inside a roll that does not become a roll attack is refused by our `Attack.Start` prefix after theirs (2.4): their postfix sees `__result` false and records nothing (their contract for a refused start), and the converted clone is dropped; the press starts the pair's normal swing when the roll ends. Their skip while a swap is queued leaves `__result` false: our postfix does nothing and our finalizer clears the call (roll gate closed); a dodge or a jump cancels their swap, so no move waits on one. Cross test X02. |
| **Tower Shield Wall** (`MC.Combat.Shields.TowerWall`, network version 2) | Its bash is the tower's own `m_attack` (the tower is the current weapon: TwoHandedWeaponLeft, skill Blocking), set in its `StartAttack` prefix, which also refuses a bash inside its `BashCooldown` (2 s between bash starts; skips the original with `__result = false`); its `StartAttack` postfix watches the bash (one hit, cooldown start, the swing slowed to `BashAnimationSpeed` 0.6, synced). Default animation `ShieldPunch` (the ShieldUp pose is gone); `BashStamina` 20, `BashStaggerLock` 8 s. | Excluded by skill Blocking of the `weapon` argument (gate 2) and by `ItemKinds` Shield (the shared shield rule, already in `src/Shared`), as its 6.1 asks; the bash never becomes a move and never stacks our stagger multiplier on the bash's own (`BashStagger`, ×25 by default, applied by Tower Shield Wall on the creature's owner), whatever its animation (a `Custom` bash trigger that is also one of our move animations is still a bash: the gates read the weapon, not the trigger). No shared `Attack.Start` patch, so patch order does not matter; our `StartAttack` skip never acts on a tower (no move starts with one), and their cooldown skip leaves `__result` false: our postfix does nothing and our finalizer clears the call. A vanilla tower shield without that mod plus an empty right hand attacks with fists and gets the fists' moves (vanilla behaviour). Cross test X03 (their C04). |
| **Sneak Ambush** (`MC.Combat.Sneak.Ambush`) | `Humanoid.StartAttack`: an `[AlwaysOnPatch]` prefix (applied while their feature is off too) refuses a Smoke Screen throw (skips the original) while their feature is inactive. Backstabs and sneak-attack XP; noise. | Their skip is harmless to us (see above). The Smoke Screen (`MC_SmokeScreen`: OneHandedWeapon, skill None, Projectile) is excluded by gate 3, as their design asks (their X04); it is in our `moveset.exclusions` self-test and T11. A sneak roll (crouch + jump) followed by a roll attack can backstab: vanilla backstab × the roll multiplier, and Sneak Ambush counts it like any backstab (unless Creature Morale suppresses it, next row). A jump makes noise 30 and ends crouching (vanilla `ForceJump`), a roll noise 5. Cross test X04; the paired Sneak Ambush test must expect the same: backstab and XP with Creature Morale off, a character with no boss kills, or from behind the Greyling unseen; none from an afraid Greyling that sees the player (our X04 (a)-(c)). |
| **Creature Morale** (`MC.Combat.Creatures.Morale`) | AI target choice: a creature is **afraid** of a player who has helped kill the boss two bosses past the creature's biome boss (elites one more; biome = the harder of its home biome and the one it spawned in); it runs from that player within `FearRange` (12 m) when it senses them, and fights back when hit, near-missed or cornered (3 m for 2 s). No creature ignores a player any more (the first version's "calm" state is gone). Its sneak rule (`Character.RPC_Damage` prefix on the victim's owner) sets `hit.m_backstabBonus = 1` when an afraid creature can see the attacker. | No shared method. A move's hit counts like any hit: an afraid creature fights back. A roll attack from sneak on an afraid creature that sees the player gets no backstab and no Sneak Ambush XP: Morale's rule, not a Moveset bug; one that cannot see the player (behind it, out of sight, smoke) still takes the backstab. Cross tests X04, X05 (their X08: a Meadows Greyling at their rank 3, Eikthyr, The Elder and Bonemass killed). |
| **Crossbow Stays Loaded** | `Attack.OnAttackTrigger` and `Humanoid.BlockAttack` postfixes, reload weapons only. | Crossbows are excluded (Projectile, `m_requiresReload`); no shared patch target. Cross test X01. |
| **Harpoon Hooks Tames** | `Character.RPC_Damage` (victim side). | The harpoon (`SpearChitin`) has a Projectile primary: excluded. Move hits pass through its patches like any hit. Cross test X06. |
| Forge Idol Upgrades, Creature Kill and Tame Counts, Encyclopedia, others | none | Kills by a move are credited by vanilla `RPC_Damage` as usual. |
| Shared code | `ItemKinds.Classify` (read only); `NetworkGate.PeerCompatible`, `PeerProblem`, `PeerStateChanged` (7.6) | The shield rule already in `ItemKinds` (shield pose + Blocking → `Shield`, so tower shields stay `Shield`) only strengthens our gate. Contract with the sibling combat mods: every join check decides with `PeerCompatible`, names `PeerProblem` in its log and re-checks on `PeerStateChanged` with the same 1 s grace, so a player is refused by all of them for the same reasons at the same time. |

Every Both mod has its own framework copy, handshake RPC names and Harmony id; our `ZNet` postfixes are independent
of the other MC mods'.

### 6.2 Other mods

| Mod (GUID) | Overlap | Action |
|---|---|---|
| Goo's Combat Overhaul (`goo.valheim.gooscombatoverhaul`, from its config file name, unverified) | Jump attack and running attack on the primary; per-weapon tuning of the same swings. | Detected once per activation (`Chainloader.PluginInfos` by that GUID, else a plugin whose name contains "Goo" and "Combat Overhaul"): our jump attack stays off, one Info line: `Goo's Combat Overhaul is installed: it has its own jump attack, so Weapon Moveset's jump attack stays off (the roll attack still works).` Roll attack stays on. Our `Attack.Start` prefix also declares `[HarmonyAfter]` on that id (its Harmony id is unverified; an unknown id is ignored), so a clone GCO renames first matches no family and stays GCO's. |
| Quickstep (`shudnal.Quickstep`), SecondaryAttacks (`sighsorry.SecondaryAttacks`) quickstep, SpecialAttack (MM94, GUID unknown) | Replace the roll with a dash that sets `m_inDodge`. | A dash counts as a roll (2.4); it has no dodge state, so it is never cut: the attack after it is a roll attack through the whole window, as before (with no dodge state to leave there is nothing to cross-fade, and no blend-out limit, Decision 26). Roll attacks come sooner; the cooldown (A4) limits back-to-back moves (README). SecondaryAttacks also patches `Attack.Start` (an adrenaline field only) and rewrites secondaries: compatible. |
| Cancel Animation Cancels (sighsorry, GUID unknown) | Clears queued attacks when a dodge starts right after an attack, and suppresses the primary while block and attack are held during a dodge (unarmed, spears, axes, polearms). | README: with it, a roll attack pressed during a keyboard roll (block + jump) with block still held may be lost for those weapons (the cut happens during the dodge, where it suppresses the primary); press after the roll or release block. No detection in v1. |
| AttackCancel (MrGay), AttackCancleCounter (IDRdhnTM), Nexus "Cancel Attack to Roll-Dodge" (GUIDs unknown) | Attack → roll cancels. | The loop gets the roll attack's bonus (Decision 8); README note, L10. |
| Sword Heavy Slash (Fai, GUID unknown) | Swords play `dualaxes0` as the third hit and `greatsword2` as the secondary (mechanism unknown). | If it renames the clone before us, our family check stands down; if it swaps triggers at `RPC_SetTrigger`, our sword jump attack may show its clip. Test only. |
| ChainAttacks (blacks7ar, GUID unknown) | Chain-level manipulation. | Our move clones have chain levels 0 and set the next level at entry (2.5); test only. |
| Attack-speed mods and animation replacers that read `Humanoid.m_currentAttack.m_attackAnimation` | Per-swing speed or clip by name. | They see the move's full trigger until its animation starts, then the family base name (2.5): a move may get the speed of the name they map (unverified per mod; test only). A replacer that swaps the clip inside the state is played by our cross-fade too (it targets the state, not the clip). |
| SDW DualWield (`org.bepinex.plugins.dualwield`) | Swaps the player's Animator controller in `ZSyncAnimation.RPC_SetTrigger` when a trigger starts with an equipped weapon's attack name. | Our defaults are the family's own chain triggers, so it swaps exactly as for the normal combo. Cross-family choices may show the wrong controller. README note. The roll cross-fade checks the state with `HasState` on the controller in place at that moment: a swapped controller without that state gets the trigger alone (no flow, no error). |
| KG AnimationReplaceManager users (Therzie's Warfare, AlmanacClasses; GUIDs vary) | Custom `m_attackAnimation` names, controller swaps. | Unknown family → vanilla (2.2). Modded weapons that use vanilla names get the family's moves. |
| PPR (`com.KorCaptain.PPR`, unverified), GrindstoneSkills, Combat Momentum | Parry counters. | Only relevant to the later parry attack (L2). |
| PIXPIX Movement, ZenCombat, WeaponArts, SpecialAttack specials | Own keys (airborne Slam on G, separate dodge button, arts). | No shared input; a separate dodge button still goes through `m_inDodge` (unverified). |
| EpicLoot and weapon packs | Patch `Attack` / `Humanoid`. | The clone keeps `m_weapon`, so per-weapon effects apply to moves (unverified). |

---

## 7. Implementation plan

### 7.1 Files

| File | Content |
|---|---|
| `MC.Combat.Weapons.Moveset.csproj` | Metadata as scaffolded, plus `<ModIdea>Weapon revamp</ModIdea>`, `<ModNetworkVersion>2</ModNetworkVersion>` (comment: RPC names and `MoveRules` layout; 2 since the roll flow, Decision 25), `ModMultiplayerNotes` completed (section 4, "Required on the server and on every player: the server refuses players without it, with another version, or with it turned off, unless AllowPlayersWithoutMod is on, and its settings apply to everyone. The moves use the game's own animations, so every player sees them. On a server without the mod it turns itself off."). |
| `Plugin.cs` | `BindConfig` (section 5), `OnActivated`, `OnDeactivated` (7.4). |
| `Families.cs` | `WeaponFamily` enum (None + the 12 rows, settings order), `Families.Of(string animation, Skills.SkillType skill)`, `Families.Eligible(Attack clone, ItemDrop.ItemData weapon)`, display names for descriptions. |
| `MoveTriggers.cs` | The 39-name list (+ `Off`), defaults per (move, family), the per-default `AcceptableValueList`s (default first), `Resolve(MoveRules, MoveKind, WeaponFamily)` with the cache and warnings (2.6), `StepOf(trigger, base, chainLevels)` for the continuation (base and level count from the clone before our edit, 2.5). |
| `MoveTracker.cs` | `MoveKind` (None, Jump, Roll), tokens (jump; roll in and after the roll), `LastMoveAt`, `Decide` (pure), `OnJumped`, `Tick(Player)` (roll edges, landing, move watch, state learning), `IsStarting(Humanoid)` (the skip test), `Consume`, `Reset`; the pending/applied move context of the current `StartAttack` call (kind, family, trigger, the clone's base name, chain level count and `m_maxYAngle` before our edit, the roll gate). |
| `RollFlow.cs` | The roll flow of 2.9: `Cut` (dodge-state test, cross-fade, local trigger reset), `StateFor` (learned, probed, none), `Learn`, the probe cache per controller, `OnRemoteTrigger` (other players), Debug hooks and counters (the 1.0.16 trigger → clip table, Debug only: the self-tests check the probe against it). |
| `StateProbe.cs` | The controller probe of 2.9 (Decision 27): `Find(animator, trigger)` (one trigger, Debug line), `Run(animator, triggers, hashes, clips, stance)` (a batch on one hidden copy: set up, mute `StateController`s, settle to the idle, fire each trigger); never throws (`PatchGuard.Report`, zeros). |
| `MoveEdit.cs` | `Apply(Attack clone, Humanoid character, ItemDrop.ItemData weapon, MoveKind kind, string trigger, MoveRules rules)`: the stamina fallback and the edits of 2.5, and the Debug line. |
| `MoveRules.cs` | Snapshot of every synced setting; `Own()`, `Write`, `TryRead` (layout, family count, clamping), `Describe`. |
| `ServerRules.cs` | Copy of Forge Idol Upgrades' (`Current`, `Start/Stop`, `OwnChanged`, request/answer, Debug `TestRules`), changed so a connected client gets "no rules" (moves off) instead of its own config until the server's rules arrive (Decision 20), and logs Info on each changed set of received rules. |
| `PlayerCheck.cs` | Copy of Forge Idol Upgrades' join check (grace, queue, `Error(ErrorVersion)`, kick list; texts adapted), with two changes (4): the verdict is `NetworkGate.PeerCompatible(peer)` and the log reason `NetworkGate.PeerProblem(peer)` (Forge's copy still decides with `PeerHasMod`), and `Start` / `Stop` subscribe to and unsubscribe from `NetworkGate.PeerStateChanged`, whose handler calls `Schedule(peer)` inside its own `try` / `PatchGuard.Report`. The pure `Decide` takes `compatible` in place of `hasMod`. |
| `Compat.cs` | GCO lookup (first use after activation), Debug plugin list. |
| `Patches/HumanoidPatches.cs` | `StartAttack` prefix (skip while a move is starting, then `Decide`), postfix, finalizer. |
| `Patches/AttackPatches.cs` | `Attack.Start` prefix. |
| `Patches/CharacterPatches.cs` | `Character.Jump` prefix and postfix. |
| `Patches/PlayerPatches.cs` | `Player.UpdateDodge` postfix. |
| `Patches/ZSyncAnimationPatches.cs` | `ZSyncAnimation.RPC_SetTrigger` postfix (other players' roll attacks, 2.9). |
| `Patches/ZNetPatches.cs` | Copy of Forge Idol Upgrades' (`OnNewConnection`, `RPC_PeerInfo`, `Update`). |
| `SelfTests.cs` | Debug builds only (7.5). |
| `README.md`, `CHANGELOG.md`, `TESTING.md`, `icon.png` | Per house rules; README "Good to know": no jump attack from sneak or with block held on keyboard and the default gamepad layout (those roll), facing after a roll follows your "attack towards look direction" option, early presses in a roll are lost as in vanilla, the roll attack flows out of the roll from `FlowStart` (never during the roll's invulnerability) and players without the mod see it after the roll, a move continues the combo, spears and sledges (and the battleaxe roll attack) are off by default, the bonus also hits trees, the cooldown and dash mods, Cancel Animation Cancels and attack-cancel mods, GCO stand-down, animation choices from other weapon types can clip or keep the primary's numbers, `AimAngle` only in the air; on a server with the mod, turning it off on your own game (or joining with it off) gets you disconnected about a second later unless the server allows players without it. |
| `DataDump.cs` | **Deleted** (temporary data dump; `moveset.triggers` replaces its useful part). |

### 7.2 Harmony patches

All bodies catch their own exceptions and call `PatchGuard.Report(site, e)`; Unity objects are compared with
`== null` / `ReferenceEquals`, never `?.` / `??`. Applied only while Active. Two prefixes can skip the original: the
`StartAttack` prefix while one of our moves is starting (2.7), and the `Attack.Start` prefix for an attack that started
inside a roll only because we opened the gate and does not become a roll attack (2.4).

| Target | Kind, priority | What it does | Cost |
|---|---|---|---|
| `Humanoid.StartAttack(Character target, bool secondaryAttack)` (single overload) | Prefix (`ref bool __result`), Normal | `ReferenceEquals(__instance, Player.m_localPlayer)` else return true. `MoveTracker.IsStarting(__instance)` → `__result = false`, Debug line, return false. Else `Pending = MoveTracker.Decide(...)` (secondary, `InAttack()`, `IsSwimming()`, `IsAttached()`, `IsOnGround()`, tokens, time in the roll and its i-frames with their margin, time since the roll and whether its blend out is over, cooldown, rules, GCO); returns at once when no token is live. A roll attack inside the roll: `CanFlow` (weapon eligible, roll animation on, attack state known; a weapon in each hand passes), then `m_inDodge = false` for this call (the roll gate, 2.4); else the gate stays shut. | Every call (player ticks while attack is buffered or held, monster AI attacks): one reference compare for monsters, a few field reads for the player (plus the game's cached animator tags after a roll); `CanFlow` only from the cut point to the roll's end (a few compares, a cached trigger, one dictionary lookup and `HasState`). |
| same | Postfix (`bool __result`), Normal | Local player and `__result`: `MoveTracker.Consume()` (both tokens); if a roll attack was applied: `RollFlow.Cut` on the local Animator (2.9); if a move was applied: start the move watch, Debug line; else, for a trigger with no learned state: start learning it (2.9). | same |
| same | Finalizer | Clear `Pending` and `Applied` and close the roll gate (`m_inDodge = true` when our prefix opened it) on every path (StartAttack can return before `Attack.Start`, and other prefixes can skip it). | two writes |
| `Attack.Start(Humanoid character, Rigidbody body, ZSyncAnimation zanim, CharacterAnimEvent animEvent, VisEquipment visEquipment, ItemDrop.ItemData weapon, Attack previousAttack, float timeSinceLastAttack, float attackDrawPercentage)` (single overload) | Prefix (`ref bool __result`), `[HarmonyAfter("MC.Combat.Weapons.DualWield", "goo.valheim.gooscombatoverhaul")]`, `[HarmonyPriority(Priority.Low)]` | `Pending != None` and `character` is the local player: `Families.Eligible`, family, `MoveTriggers.Resolve`; saves the clone's base name, `m_attackChainLevels` and `m_maxYAngle` as they arrived (after Dual Wielding's prefix); a trigger → `MoveEdit.Apply(__instance, …)` (stamina fallback, edits), `Applied = Pending` with those saved values and the step (`StepOf`); Off / ineligible / fallback → vanilla, **except inside a roll with the gate open: `__result = false`, original skipped** (vanilla's `InDodge` refusal, 2.4), also for a trigger with no known attack state (`RollFlow.HasStateFor`: no cut without a cross-fade). | One compare when nothing is pending. |
| `Character.Jump(bool force)` (single overload) | Prefix (`out float __state`), Postfix (`float __state`), Normal | Prefix: local player and `!force` → `__state = m_jumpTimer`, else -1. Postfix: `__state > 0 && m_jumpTimer == 0` and no attack starting (2.3) → `MoveTracker.OnJumped()`. | On jump input and monster jumps: one compare. |
| `Player.UpdateDodge(float dt)` (private) | Postfix, Normal | Local player: `MoveTracker.Tick` (player change → reset; roll end/start edges; jump token end on landing; move watch: entry, continuation, cancel, aim reset; state learning; at a roll start, the controller probe for the weapon's roll animation when its state is not known yet). | 50 Hz on the local player: about ten field reads and compares, the watch only while a move is starting or playing, the learning (two or three Animator reads) only for the first swings of a new trigger; no allocation (the probe, a few milliseconds, runs once per roll animation and session at a roll start). |
| `ZSyncAnimation.RPC_SetTrigger(long sender, string name)` (private) | Postfix, Normal | `RollFlow.OnRemoteTrigger`: rules' `RollAttack` on and `name` one of their twelve roll animations, not the local player's `ZSyncAnimation`, its Animator in the dodge state, a `Player` → `RollFlow.Cut` (2.9). | Every trigger RPC of every character in range: one bool and up to twelve string compares; the rest only for a roll attack animation. |
| `ZNet.OnNewConnection(ZNetPeer)`, `ZNet.RPC_PeerInfo(ZRpc, ZPackage)`, `ZNet.Update()` | Postfixes, Normal | Settings sync and join check (section 4). | `Update`: two bool reads per frame when idle. |

No transpiler. No patch on `Attack.OnAttackTrigger`, `DoMeleeAttack`, `Character.Damage` or `RPC_Damage`. No
`[AlwaysOnPatch]` class: the mod adds no item, prefab or ZDO key that must outlive the toggle, so every patch follows
the live toggle (the framework's own handshake patches stay on under their own Harmony id, `<GUID>.framework`, so a
turned-off client still tells the server that it is off).

### 7.3 State and lifetime

| State | Where | Lifetime |
|---|---|---|
| `JumpLive`, `JumpAt`, `RollActive`, `RollStartAt`, roll counter, the tick the roll's i-frames were seen off, `RollEndAt`, whether the roll ended out of the dodge state, `LastMoveAt`, previous `m_inDodge`, tracked `Player` | `MoveTracker` statics | Session; reset on a new local `Player` (respawn, reconnect), in `OnActivated` and `OnDeactivated`. All times are `Time.fixedTime`. |
| `Pending`, `Applied` (kind, family, trigger, the clone's base name, chain level count and `m_maxYAngle` before our edit, step, time in the roll or since its end, whether it cut into the roll), the open roll gate (the `Player` whose `m_inDodge` we cleared) | `MoveTracker` statics | One `StartAttack` call (finalizer clears and closes the gate). |
| Move watch (clone, trigger, kind, family, base name, chain level count, step, `m_maxYAngle` before our edit, start time, start-check clock, roll counter, flow result and state source, owning `Player`, phase) | `MoveTracker` | From a successful move start until the clone is done or replaced (and, for a jump attack, has landed); `Reset` drops it, resetting the trigger only when the owning `Player` is still alive and the local player (2.7). |
| State learning (attack, trigger, the state at its start, start time) | `MoveTracker` | From an attack start with an unlearned trigger until its state is seen, the attack is replaced, or 1.5 s. |
| Learned and probed attack states (trigger → state hash; probed ones per controller instance id, at most 8 controllers) | `RollFlow` | Game session; cleared on activation and deactivation. The probe's hidden copy of the controller lives only inside the call that made it. |
| Trigger cache (`string[2, 12]` + the rules and `Player` references it was built for) | `MoveTriggers` | Rebuilt when either reference changes. |
| Warned-once flags (unknown trigger, start check, per move and family; unreadable rules) | `MoveTriggers`, `MoveTracker`, `ServerRules` | Game session. |
| Own and server rules snapshots | `ServerRules` | Own: until a setting changes; server: the `ZNet` session. |
| Join check queue | `PlayerCheck` | Until each deadline; cleared on deactivation. |
| `NetworkGate.PeerStateChanged` subscription | `PlayerCheck` | From `PlayerCheck.Start` (activation) to `PlayerCheck.Stop` (deactivation, quit). |

### 7.4 Live toggle

- `OnActivated`: `Compat.Reset()` (lookup at first use), `MoveTracker.Reset()`, `MoveTriggers.Invalidate()`,
  `RollFlow.Reset()`, `ServerRules.Start()`, `PlayerCheck.Start()` (subscribes to `NetworkGate.PeerStateChanged`; on a
  server, schedules every connected player), Debug plugin list, `SelfTests.Register()`.
- `OnDeactivated` (patches still applied during the call; also called at game quit): `SelfTests.Unregister()`,
  `PlayerCheck.Stop()` (unsubscribes, clears the queue), `ServerRules.Stop()`, `MoveTracker.Reset()` (a move still
  starting has its trigger reset, only if its `Player` is alive; a roll gate still open is closed, although the
  finalizer always closes it within the call), `MoveTriggers.Invalidate()`, `RollFlow.Reset()`; each step in its own
  `try` / `PatchGuard.Report`, so one failure does not skip the rest.
- Nothing to revert: no `ObjectDB`, `SharedData`, item or ZDO edit. `m_inDodge` is only changed for the length of one
  `StartAttack` call. A move already playing when the mod turns off finishes with its edited clone (private to that
  swing), and a cross-fade already started finishes (it is the Animator's own state now); the next attack is vanilla,
  and a roll attack of another player then shows the vanilla order again.
- `Enabled = false` + restart: no patch, vanilla combat; `Status` "Off (disabled in settings)". On a client, the
  framework's hello says "off", so a server with the mod refuses it about a second after joining unless
  `AllowPlayersWithoutMod` is on (M08). Turning the mod off live while connected does the same through `HelloState`
  (4); in single player and on the host nothing refuses anyone.

### 7.5 Debug in-world self-tests (`SelfTests.cs`)

Run by `./tools/Test-InWorld.ps1 -Mod Weapons.Moveset` in the throwaway world (god mode, world modifier StaminaRate 0).
Settings are forced through the Debug in-memory `ServerRules.TestRules`, never a `ConfigEntry`. Each test records the
player's equipped items and position, and in `finally` removes the items it added, re-equips the old ones, destroys
what it spawned, puts the position back, restores `PlayerController.enabled`, `Application.targetFrameRate` and
`QualitySettings.vSyncCount`, clears `TestRules` and calls `MoveTracker.Reset()`.

- **Input**, through vanilla paths: `player.Jump()`, `player.Dodge(dir)` (private); a press = `m_queuedAttackTimer =
  0.5f` (the vanilla buffer); a **held button** = `PlayerController` disabled (`pc.enabled = false`, it rewrites the
  controls every tick) and `player.SetControls(...)` with `attack` on the first tick and `attackHold` true on every
  tick, after each `WaitForFixedUpdate`.
- **Low frame rate** variants: `QualitySettings.vSyncCount = 0`, `Application.targetFrameRate = 20`, so two or three
  physics ticks run per rendered frame (the restart gap, 1.2).
- **Target**: `piece_TrainingDummy` spawned 1.8 m ahead, its `MonsterAI` disabled at once (`enabled = false` takes it
  out of `BaseAI.Instances`): the dummy is a Humanoid whose faction is hostile to players, with four attacks (1.8-3 m
  reach, `m_attackForce` 60) and a 12 m throw, which would push the player back (`IsKnockedBack` refuses attacks; god
  mode does not stop pushback). Each test asserts the dummy stays inert (its `MonsterAI` still disabled and out of
  `BaseAI.Instances`, and the dummy never started an attack) and `!IsKnockedBack() && !IsStaggering()` before each
  press. Its target is no sign of activity: `BaseAI.Awake` hooks `MonsterAI.OnDamaged` to `Character.m_onDamaged`, so
  the first hit sets the player as its target even with the component disabled (NOTE only).
- **Idle** (between steps): on the ground, not attacking, rolling, staggered, knocked back or in a minor action, no
  buffered press or roll, no move being watched, the last attack more than 0.3 s ago, for three ticks in a row, and no
  attack still starting (the current attack is done, has played, or is older than 1 s without ever playing).
  `InAttack()` reads only the Animator, so in the restart gap (1.2) of a swing that just started it is false: a step
  that then takes the item off or rolls leaves the swing's trigger pending, and the Animator plays that swing later.

Tests:

- **`moveset.triggers`** (no spawning): every name of the list and every default is a Trigger parameter of the local
  player's Animator (`HasParameter`); every (move, family) resolves to its default with the default rules; each
  animation setting's list starts with its default; received rules with an unknown name (and with a name
  `HasParameter` rejects, simulated by a test hook) fall back to the default with exactly one warning; every **player
  melee weapon** maps to a family (NOTE any that does not: a game update added a family), where a player weapon is an
  item of `ObjectDB.m_items` that has a recipe in `ObjectDB.m_recipes`, plus `PlayerUnarmed` (creature weapons such as
  `GoblinSword`, `Axe1h_JotunWarrior` or `troll_log_swing_h` are not player weapons); one item per family maps to its
  row (`SwordIron` Swords, `MaceIron` and `Club` Maces, `AxeIron` Axes, `Battleaxe`, `AxeBerzerkr` DualAxes,
  `THSwordKrom`, `AtgeirIron`, `KnifeCopper`, `KnifeSkollAndHati` DualKnives, `SpearBronze`, `PlayerUnarmed` and
  `FistFenrirClaw` Fists, `SledgeIron`); `Eligible` is false for `Torch`, `PickaxeIron`, `Scythe`, `Bow`,
  `CrossbowArbalest`, `StaffFireball`, `SpearChitin`, `BombOoze`, `MC_SmokeScreen` (when Sneak Ambush registered it,
  else SKIP that item), `Tankard`, `Hammer`, `ShieldWoodTower`, and for a copy of `PlayerUnarmed`'s data with skill
  Blocking. **Controller probe** (2.9): every default roll animation is in the clip table; for every trigger of the
  table, probed one at a time with the stance of its own weapon type (the row weapon's `m_animationState`, bare hands
  for fists): an attack state exists in the live Animator (`HasState`), it plays the table's clip (the clip the live
  Animator played for that trigger), and it equals the learned state when a swing already taught one this session;
  the player's own `StateController` behaviours still have their effects after the probes (the copy mutes only its
  own instances) and none of the copy's is shared with the player's Animator. NOTE the time and Animator updates, the
  muted behaviours, and how many states one batch with the bare-hands stance finds the same (does the stance matter?).
- **`moveset.rules`** (pure): `MoveRules` write/read round trip (with `FlowStart` and `FlowBlend`), clamping, unknown
  layout (layout 1 of the first test build included) and wrong family count refused; a connected client with no readable rules gets "moves off"; single player and server never take rules from
  a peer; `PlayerCheck.Decide` verdicts (compatible → allowed without a warning; not compatible → refused, or allowed
  with a warning when `AllowPlayersWithoutMod` is on; not a server, player gone, not ready, already being kicked →
  skip). The compatibility verdict, its reasons and the `PeerStateChanged` re-check need a real connection: framework
  items N07 and N08 in `src/Shared/TESTING.md`, and our M02, M07, M08. A `Decide` truth table (secondary, in attack,
  swimming, attached, airborne with and without jump token, roll age inside and outside the window, both tokens →
  Jump, both tokens in the air with the jump attack off or GCO → None (never Roll), a roll ended in the air without a
  jump → Roll, moves off, GCO → no Jump, cooldown; inside the roll: at and after `FlowStart` with the i-frames over →
  Roll, before it or with the i-frames (or their margin) on → None whatever `FlowStart`, `FlowStart` 0 → Roll as soon
  as the i-frames and their margin are over, secondary, roll attack off and cooldown → None; after the roll: in the
  window with the blend out over → None, and with `Window` 1.5 a press 1 s after → None after a vanilla roll, Roll after
  a dash); `StepOf` (`swing_longsword1` of `swing_longsword` with 3 levels = 1, next
  level 2; `swing_longsword2` with 3 levels = 2, next level 0; `dualaxes2` of `dualaxes` with 4 levels = 2, next level
  3; `knife_secondary` = none).
- **`moveset.jump`**: turns the player to the dummy. For each family item whose jump attack is on (10 families, with
  bare hands and `FistFenrirClaw`): add, equip, wait for the equip to finish; `Jump()`; assert the token is live; after
  0.1 s press; assert the recorded move is the expected trigger, the clone has chain levels 0 and the multipliers
  applied; poll every physics tick up to 2 s and assert the clone entered its state within 0.5 s, and that after entry
  its name is the family base and `m_nextAttackChainLevel` is the step after the move (0 after the last); NOTE the
  airtime (jump to first `IsOnGround()`), the entry delay, the layer-0 clip name at entry (trigger → clip map), the
  dummy's health drop time and whether the player was still airborne then (landing vs hit), and `m_maxYAngle` back to
  0 after landing. Then, grounded and idle, press: assert a normal swing (`<base>0`, no move). **Hold variant** (at 20
  FPS): `attackHold` true from the jump on → the first attack is the jump attack, it enters, no restart replaced it
  (the refused-restart counter may be above 0). Also: jump, land without attacking, press → no move; teleport 5 m up
  (no jump), wait 0.3 s (past the 0.2 s coyote time), press in the air → no move; start a ground swing and call
  `Jump()` on the next tick (restart gap) → no jump token. Spears and sledges: jump and press → normal swing (Off).
- **`moveset.roll`**: first forgets every learned and probed state (`RollFlow.ForgetAll`), so each family's first
  roll attack is the first of a game session. For each family item: `Dodge(forward)`; buffered press (press every tick
  while `m_inDodge`). Families whose roll attack is on: assert the attack started **inside the roll at the cut point**
  (`FlowStart` after the roll started, within a tick, and at least `IframeMargin` after the i-frames ended), the roll
  ended on the next tick (cut, not its natural 0.92 s), it is a roll attack with the expected trigger that cut into the
  roll, it cross-faded (state known), the first use of each roll animation in the test took its state from the
  controller probe, the Animator entered exactly the state the cut cross-faded into, it entered within two ticks, the
  clip at entry is the one the trigger plays (1.6), **every tick from the
  roll's start to the attack's entry layer 0 was in or heading to a `dodge` or `attack` state (no idle or locomotion:
  the "gap" is 0)**, and `IsDodgeInvincible()` was false from the attack start on. Families whose roll attack is Off
  (battleaxes, spears, sledges): no attack inside the roll, the roll gate stayed shut (no refusal, the check before the
  gate said no: `GateSkips`), the normal swing starts on the tick after the edge (vanilla). NOTE per family: roll length, i-frames, the cut time and the dodge state's normalized time at the cut,
  the flow result and state source, the gap (for the Off families: the vanilla gap, the reference), the entry delay
  and clip, and the combo step that follows. **Continuation**: a press (or hold) after the move's `Chain` event → the
  next attack fires `<base><step + 1>` (swords: `swing_longsword2`).
- **`moveset.roll-input`** (`SwordIron`): **hold variant, at 50 FPS and at 20 FPS**: `attackHold` true through the roll
  and after → the roll attack starts at the cut point, is never replaced (the watched clone is still `m_currentAttack`
  at entry), enters, and is never dropped or cancelled. **Double start**, inside the roll at the cut point and right
  after the edge: `player.StartAttack(null, false)` twice in the same tick → the second call returns false and the
  move plays (after the edge it cross-fades out of the roll's blend). **Window** (no press during the roll): press 0.06
  s after the edge (the Animator still blending out of the roll) → roll attack, cross-faded, entered within three ticks;
  0.2 s and 0.3 s after → either a roll attack that cross-faded out of the roll's blend or a normal swing, never a roll
  attack after the stand-up (NOTE which, and whether the Animator was still in the blend); `Window + 0.2` s after →
  normal swing; with `Window` 1.5, 1 s after → normal swing (Decision 26). **FlowStart 0**: the roll attack cuts into
  the roll `IframeMargin` after the i-frames ended (within the tick the self-test and `MoveTracker` see them end), never
  invulnerable from its start. **Cooldown** 5, two rolls in a row → the second attack is normal;
  NOTE the fastest roll → roll attack → roll → roll attack loop against the default cooldown. **Learned state**: with
  the learned states forgotten and the controller probe off (test hooks), no state is known, so the roll attack does
  not cut into the roll: it starts on the tick after the roll's end with the trigger alone, no refusal in the roll
  (NOTE what the controller does with the trigger in the roll's blend out, the entry delay and the gap); its state is
  then learned and must be the one the Animator entered and the one the probe found before the roll, and a second roll
  attack cuts into the roll and cross-fades from the learned state with no gap. **Other player's view**: during a roll
  without an attack, the trigger `swing_longsword1` set on the player's Animator the way the RPC does and handed to
  the remote handler (its player filters skipped) → cross-faded into an attack state with no gap, the trigger no
  longer pending, the swing ends (read on the Animator itself, not through `InAttack()`, whose per-tick tag cache is
  taken before the Animator update), and no second swing after it.
- **`moveset.aim`**: `SwordIron`, jump; pitch the look 45° down (`m_lookPitch`, wait a frame for the eye); press;
  call the clone's `GetMeleeAttackDir` (private) while airborne → pitch ≈ 30° down with `AimAngle` 30, level with 0,
  and ≈ 45° (never more) with `AimAngle` 45 while looking 85° down; after landing → level.
- **`moveset.stamina`** (god mode off for this test, StaminaRate restored): `StaminaMultiplier` 3 and stamina set
  between one and three swings' cost → a roll attack falls back to a normal swing (Debug line), no flash loop; the same
  with the press held during the roll → no attack inside the roll (our refusal), the roll plays its full length, and
  the normal swing starts on the tick after it.
- **`moveset.exclusions`**: `Torch`, `PickaxeIron`, `Scythe`, `BombOoze`, `MC_SmokeScreen` (if present): jump and press,
  and roll and press → no attack inside the roll, and the clone keeps the item's own animation and no move is applied.
- **`moveset.watchdog`**: `TestRules` sets the Swords roll animation to a Trigger with no transition from idle or
  locomotion (first `reload_crossbow_done`, else `recharge_lightningstaff_done`; the test override skips the list but
  not `HasParameter`), and the roll flow is forced to the trigger alone (test hook, so the trigger stays pending);
  `SwordIron`, roll and press → with no attack state known the move does not cut into the roll: it starts right after
  the roll with the trigger alone, and its start clock runs from its start; while the move is
  starting, `Animator.GetBool(trigger)` is true just before the 0.5 s deadline (else NOTE "the Animator consumed
  <trigger>" and try the next candidate); a second press in that time is refused; at the deadline (0.5 s after the
  clock started) the Debug reset counter goes up by one, `GetBool(trigger)` is false, the clone is done and no longer
  `m_currentAttack`, the warning was logged once, and on the next tick a normal swing (`swing_longsword0`) starts.
- **`moveset.order`** (NOTE only, R7): for a few ticks, log `Time.frameCount`, `MonoUpdaters.UpdateCount` and a
  sequence number from `PlayerController.FixedUpdate`, `Player.FixedUpdate` and `Humanoid.CustomFixedUpdate` (Debug
  test hooks), plus `m_animator.updateMode`.

### 7.6 Framework and shared code

No framework or shared-code change for this mod. What it uses is **already in `src/Shared` (done in this run and
tested with `./tools/Test-Framework.ps1`; its multiplayer part is framework items N07 and N08 of
`src/Shared/TESTING.md`, to test in game together with the combat mods)**:

- `NetworkGate.PeerCompatible`, `NetworkGate.PeerProblem` and the server-side event `NetworkGate.PeerStateChanged`
  (raised by the client's `HelloState`), used by `PlayerCheck` (4). `PeerNetworkVersion` and `PeerReady` exist too;
  Moveset does not call them (`PeerCompatible` covers both).
- `ItemKinds.Classify`, read as is, including its shield rule (shield pose + Blocking skill → `Shield`), which keeps
  Tower Shield Wall's towers out of the `Weapon` kind (gate 1).
- `[AlwaysOnPatch]` is not used (7.2).

---

## 8. Test plan

`TESTING.md` items. Spawn and prefab names below are in the 1.0.16 runtime dump, except `ArrowWood` and `BoltBone`
(the dump covers weapons, not ammo; see S1).

**Setup**

- **S1** F5 console: `devcommands`. Weapons: `spawn SwordIron`, `MaceIron`, `Club`, `AxeIron`, `Battleaxe`,
  `AxeBerzerkr`, `THSwordKrom`, `AtgeirIron`, `KnifeCopper`, `KnifeSkollAndHati`, `SpearBronze`, `SledgeIron`,
  `FistFenrirClaw` (plus bare hands). Excluded: `Torch`, `PickaxeIron`, `Scythe`, `Bow` + `ArrowWood`,
  `CrossbowArbalest` + `BoltBone`, `StaffFireball`, `SpearChitin`, `BombOoze`, `ShieldWoodTower`, `ShieldBronzeBuckler`
  (and `MC_SmokeScreen` with Sneak Ambush). Targets: `spawn Greyling`, `Boar`, `Skeleton`, `Troll`,
  `piece_TrainingDummy` (it strikes back and pushes you: stand at its side or behind it). (`ArrowWood` is checked in
  the asset manifest by Creature Kill and Tame Counts' tests; `BoltBone` is unverified: check it with the spawn
  command's autocomplete.)
- **S2** Debug lines: BepInEx log level with Debug (`./tools/Setup.ps1 -DevBepInExConfig`), `./tools/Watch-Log.ps1
  -Mine`. God mode hides stamina costs: turn it off for T13.

**Single player**

- **T01 Jump attack, sword (G1):** jump, attack in the air. Expected: the sword finisher animation, Debug "Jump
  attack: SwordIron (Swords) plays swing_longsword2", the hit lands (in the air or right after landing); the next
  attack starts the normal combo at its first swing.
- **T02 Jump attack, every family (G1, A3):** the 10 families whose jump attack is on (table 2.2, bare hands too): the
  default animation plays from a jump, no press is eaten, no start-check warning. Spears and sledges: a normal swing,
  no Debug "Jump attack" line.
- **T03 One per jump (G1):** jump from a height (a roof) and attack twice before landing: the first is a jump attack,
  the second a normal air swing.
- **T04 No jump, no jump attack (G1):** walk off a ledge and attack in the air → normal swing. Jump, land, attack →
  normal swing.
- **T05 Aim (A1):** jump next to a `Boar` and look down at it while attacking with a knife (fast hit, in the air): the
  swing sweeps down and hits it. Then a jump attack that starts in the air and hits after touchdown (`THSwordKrom`,
  pressed just before landing, looking at the feet of a dummy about 2.5 m away): the hit is level (it strikes the
  dummy, not the ground). The angles are checked by the `moveset.aim` self-test.
- **T06 Roll attack, sword (G2):** roll (block + jump, or crouch + jump), press attack during the second half of the
  roll → Debug "Roll attack: … cut into the roll 0.86 s after it started (cross-fade from the roll, …)", the second
  combo swing grows out of the end of the roll with no stand-up to idle in between; holding attack through the roll →
  the same, never replaced by a first swing (no "was dropped" Debug line); a press right after the roll → roll attack
  ("… s after the roll ended (cross-fade …)"), still without the idle pose. Repeat the hold case with the game capped at
  a low frame rate (for example 20 FPS in the graphics settings, or a busy base).
- **T07 Roll attack, every family (G2, A3):** the 9 families whose roll attack is on, as T02, each flowing out of the
  roll. Battleaxes, spears and sledges: a normal swing that starts only when the roll has ended, with the vanilla
  stand-up in between.
- **T08 Window (G2, Decision 26):** press 1 s after the roll → normal swing; `Window = 1.5` → still a normal swing
  (after a vanilla roll a roll attack must flow out of it; `Window` only lengthens the time after a dash, X07).
- **T09 Numbers (G1, G2):** `StaggerMultiplier = 10` → a `Troll` staggers from one roll attack (never on a normal
  swing); `DamageMultiplier = 3` → visibly more damage on the `piece_TrainingDummy` (it strikes back: attack from its
  side); `PushMultiplier = 5` → a `Greyling` flies back. Back to defaults.
- **T10 Combo (G3):** secondary after a jump or roll → normal secondary; a queued combo (attack, attack) is not
  replaced; **mash attack during a sword roll attack → the finisher follows, no press is lost**; after a jump attack the
  next attack is the first swing; a roll attack with `knife_secondary` picked (setting) → the next attack is the first
  swing.
- **T11 Excluded weapons (G3):** Torch, pickaxe, scythe, bow, crossbow, staff, `BombOoze`, `SpearChitin` (and Sneak
  Ambush's Smoke Screen) after a jump or roll: vanilla. A buckler in the left hand with bare right hand: fists moves.
- **T12 Animation settings (A3):** in ConfigurationManager set Swords jump to `greatsword2` → used on the next jump
  attack; `Off` → normal swing, no Debug line; a misspelled value typed into the `.cfg` with the game closed → after
  start the setting shows its default (`swing_longsword2`), which is used; no error.
- **T13 Stamina (G1, G2):** god mode off: a jump attack costs the jump plus the swing; `StaminaMultiplier = 3` → the
  move costs three swings; with stamina for one swing but not three → a normal swing (no move), no repeated flash;
  with too little stamina for any swing after a jump → the swing fails like vanilla (flash) and there is no attack in
  that jump.
- **T14 Facing (G2):** "attack towards player look direction" off: roll past a `Greyling` and attack while looking
  back at it → the strike turns to the camera. Option on (gamepad): same with the stick still held in the roll
  direction → the strike follows the roll; stick pulled back toward the Greyling → strikes it; no stick input → no turn.
- **T15 Move switches (G5):** `JumpAttack = false` / `RollAttack = false` live: that move stops at once, the other
  works. With `JumpAttack = false` (and `Window = 2`), roll, jump, attack in the air → a normal swing, not a roll attack.
- **T16 Cooldown (A4):** default: roll attack, roll again at once, attack → a roll attack (a vanilla roll takes longer
  than the cooldown); `Cooldown = 5` → the second is a normal swing (Debug "cooldown"); `Cooldown = 0` with knives,
  jump attack, land, jump, attack at once → a jump attack.
- **T17 Gamepad layouts (G1):** gamepad on the Alternative1 or Alternative2 layout: crouch (or hold block), press jump
  → the player jumps and crouch ends; attack in the air → jump attack. Default layout: the same press rolls.
- **T19 Roll flow tuning (G2, Decisions 21-23):** `FlowStart` 0 → the roll attack starts 0.2 s after the roll's
  invulnerability ends (about 0.64 s in); 2 → it starts as the roll ends, still with no idle pose; `FlowBlend` 0 → an
  instant switch, 0.4 → a slow blend, the hit at the same moment. Back to the defaults; judge the look of 0.85 / 0.15,
  then of `FlowStart` 0.7 (snappier, cuts the end of the roll). For each, compare the ground a roll covers with and
  without a roll attack (Decision 22: the dodge's root motion stops with the cut).
- **T20 No invulnerability in the roll attack (G2, Decision 21):** god mode off, roll through a `Greyling`'s or a
  `Skeleton`'s swing and roll attack at once: a hit that lands during the roll attack's swing hurts (the roll's
  i-frames ended before the swing).
- **T21 Other attacks after a roll stay vanilla (Decision 24):** with a `Battleaxe` (roll attack Off), with
  `RollAttack = false`, and with a secondary attack: press during the roll → the attack starts only when the roll has
  ended, with the vanilla stand-up in between, no "Roll attack" Debug line.
- **T22 Late press after a roll (G2, Decision 26):** no press during the roll; a press just as the roll ends → a roll
  attack flowing out of the roll's end ("… s after the roll ended (cross-fade …)"); a press about 0.3 s after the roll,
  once the character stands → a normal swing; presses in between → either of the two, never the roll, the stand-up,
  then a roll attack.

**Multiplayer**

- **M01 Server settings (G4):** dedicated server (or host) and two players, all with the mod: each sees the other's
  jump and roll attacks, the roll attacks flowing out of the roll on both screens (Debug "Cross-faded <player>'s roll
  attack …" on the watcher's game); change the server's Jump `DamageMultiplier` → clients log Info "Using the server's move
  settings" with the new value and hit harder; their own files are unchanged. A host dragging that slider → each client
  logs the line once, about 0.5 s after the drag ends.
- **M02 Join check (G4):** a player without the mod is refused about a second after joining ("Incompatible version";
  the server's Warning says "does not have the mod"); with `AllowPlayersWithoutMod = true` they get in (server Warning
  with the same reason). A player with the mod on joins normally (Debug "allowed").
- **M03 Hand-off (G4):** with `AllowPlayersWithoutMod = true` and the server's Roll `StaggerMultiplier = 10`: the player
  without the mod sees the modded player's moves with the vanilla animations (a roll attack after the roll's
  stand-up, not flowing: expected, visual only); a `Troll` their game simulates (they
  stand next to it, the modded player arrives later) staggers from one roll attack of a `SwordIron` at Swords skill 50
  (the T09 setup, stage 4 of section 10), never from one normal swing; in PvP they take a modded jump attack's damage.
- **M04 Server toggles (G4, G5):** the server turns the mod off → clients show "the server has this mod turned off",
  moves stop; back on → moves return, and a player without the mod who joined meanwhile is refused about a second
  later (every connected player is checked again).
- **M05 Server without the mod:** a client with the mod joins a vanilla server → inactive (ServerMissing), vanilla
  combat.
- **M06 Synced animation (G4, A3):** the server sets the Swords jump animation to `greatsword2`: the modded client's
  jump attack plays it (Debug "server settings"), and a player without the mod (Allow on) sees the same clip.
- **M07 Version mismatch (G4):** a client with another `ModNetworkVersion` (a test build) joins: refused ("Incompatible
  version"; the server's Warning says "has another version of the mod (network version 3, the server has 2)"); with
  Allow on, let in with a Warning naming the same reason, its own copy shows the version mismatch status, and its
  moves are vanilla.
- **M08 Mod turned off on the client (G4):** (a) a client with `Enabled = false` joins → refused about a second after
  joining ("Incompatible version"; the server's Warning says "has the mod turned off"). (b) A client with the mod on
  joins, then unticks it in the MC Mods panel → the server logs "<player> turned Weapon Moveset off on their game."
  and refuses them about a second later. (c) Same with `AllowPlayersWithoutMod = true` → not refused, a server
  Warning with that reason, their moves are vanilla; ticking it back on → the server logs it turned on, the client
  logs "Using the server's move settings" again and its jump and roll attacks return.
- **M09 Other players' roll flow (G2, G4):** two modded players: A rolls and roll attacks next to B, several times
  and with several weapon types. On B's screen A's swing grows out of the roll (no stand-up), at the same moment as
  on A's; B's log has Debug "Cross-faded A's roll attack …". A normal swing of A after a roll with a battleaxe
  keeps the vanilla stand-up on both screens.
- **M10 No invulnerability on other players' games (G2, G4, Decision 21):** server `FlowStart = 0`; player B next to a
  `Greyling` or a `Skeleton` first (B's game owns it and checks its hits); player A, without god mode, rolls through its
  swing and roll attacks at once, several times → a creature hit that lands during A's roll attack's swing damages A.

**Cross-mod**

- **X01 Crossbow Stays Loaded:** a loaded crossbow, jump or roll, fire → vanilla shot, the bolt is used; a sword roll
  attack, then back to the crossbow → still loaded.
- **X02 Dual Wielding:** two one-handed axes: the jump attack plays `dualaxes3` and hits main hand then off hand (each
  weapon loses durability once); the roll attack plays `dualaxes1` and strikes with the off hand, and the combo
  continues with `dualaxes2` (main, then off) and `dualaxes3` (main, then off), the same hands as their X02; with the
  DualAxes jump animation set to `dualaxes2`, a jump attack is followed by `dualaxes3` (the pair's 4-step chain, not the
  axe's 3). Two knives: the `dual_knives` set, the roll attack `dual_knives1` strikes with the off hand. The normal dual
  combo is unchanged; no errors.
- **X03 Tower Shield Wall:** the tower-shield bash after a jump or roll stays the bash (no move Debug line, bash
  stagger, animation speed and cooldown unchanged).
- **X04 Sneak Ambush:** (a) with Creature Morale off (or a character with no boss kills): sneak roll (crouch + jump)
  into a roll attack on an unaware `Greyling` → a backstab, sneak XP once; (b) Creature Morale on, a Meadows Greyling
  afraid of the player (their rank 3: Eikthyr, The Elder, Bonemass) that sees the player → no backstab, no sneak XP
  (expected, Morale's sneak rule); (c) the same Greyling approached from behind, unseen and quiet → backstab. A jump
  ends crouching. Jumping or rolling with a Smoke Screen equipped still gives the plain throw (their X04).
- **X05 Creature Morale:** a jump attack on an afraid creature makes it fight back, like a normal hit (their X08).
- **X06 Harpoon Hooks Tames:** the harpoon after a jump or roll stays vanilla.
- **X07 Other mods (optional):** GCO installed → the Info line, no jump attack, roll attack works, a roll then a jump
  then an air attack is no roll attack; Quickstep → a dash then attack is a roll attack, and a second dash right after
  gives a normal swing (cooldown).

**Live toggle and clean log**

- **L01 Live toggle (G5):** single player (or as the host): turn the mod off in the MC Mods panel mid-fight → vanilla
  swings at once; on again → moves back. (A client of a server is refused when it turns the mod off: M08.)
- **L02 `Enabled = false` + restart:** `Status` "Off (disabled in settings)", vanilla combat.
- **L03 Clean log:** a session with jump and roll attacks on every family, including a death and a logout right after a
  move: no error and no warning from the mod except those a test provokes on purpose.

---

## 9. Open questions and unverified

**Unverified names and values**, and how each is settled:

- The trigger → clip mapping (table 2.2 timings) and whether every default trigger has a transition from the jump, fall
  and end-of-roll states: `moveset.jump` / `moveset.roll` NOTE lines (clip at entry, entry delay) and T02 / T07. A
  default that fails there gets another default before release.
- The combo continuation's transitions (a step played as a move, then the next step): `moveset.roll` continuation and
  T10.
- Whether a quick landing cuts an airborne swing before its hit (GCO fixed "jump wind-ups interrupted by quick
  landings"): `moveset.jump` dummy health vs landing time.
- Jump airtime and apex (`Physics.gravity` is not in code): `moveset.jump` NOTE.
- Settled by the first runtime run: the dodge state's length (`m_inDodge` 0.92 s, i-frames 0.42 s, 1.5), the Animator's
  update mode and the tick order (R7, 1.1). The cooldown's "never blocks a vanilla roll" claim is measured again with
  the roll flow (`moveset.roll-input` loop NOTE: the roll attack now starts sooner).
- **Roll flow** (Decisions 21-24, 27): whether the controller has a transition from the dodge state on an attack
  trigger (`moveset.roll-input` "learned state" NOTE: the 2026-09-30 run says no, the trigger alone waits for the end
  of the roll's blend to idle); the attack states are not named after their clips (same run), so the controller probe
  (Decision 27) must find them: that it runs without a warning or an error in the log, lands in the state the live
  Animator plays (`moveset.triggers` clip and learned-state checks, `moveset.roll-input` comparison with the entered
  state), costs only a few milliseconds (`moveset.triggers` NOTE, T23), and leaves the player's own state behaviours
  alone (`moveset.triggers` check); whether the stance parameters matter for the attack transitions (the
  bare-hands batch NOTE of `moveset.triggers`);
  that a script cross-fade interrupts the dodge state and its blend out and lands in the right attack state
  (`moveset.roll`: gap 0, clip at entry); the look of the cut at 0.85 s (and 0.7 s) with a 0.15 s blend, and how much
  ground a roll loses when a roll attack cuts it (T19); how long the blend out of the roll lasts, which is how long a
  press after the roll still makes a roll attack (the 0.2 s and 0.3 s `Window` NOTEs of `moveset.roll-input`, T22);
  other players' view (the remote-handler case of `moveset.roll-input`, and M09 with two real players); that the 0.2 s
  `IframeMargin` covers the ZDO send and the latency of a normal connection (M10; a very laggy peer may still see the
  i-frames a little longer, as it does at the end of every vanilla roll).
- The watchdog triggers (`reload_crossbow_done`, `recharge_lightningstaff_done`) have no transition from idle: the
  watchdog test checks it and NOTEs otherwise.
- Atgeir chain clip order (`2Hand-Spear-Attack1/3/9`): `moveset.roll` / `moveset.jump` clip NOTE.
- GCO's GUID `goo.valheim.gooscombatoverhaul` (from its config file name) and its Harmony id: the Debug plugin list
  with GCO installed (X07). GUIDs of Cancel Animation Cancels, AttackCancel, AttackCancleCounter, Sword Heavy Slash,
  ChainAttacks and SpecialAttack are unknown; v1 only names them in the README.
- A Quickstep dash counts as a roll (it sets `m_inDodge`, per its source): X07.
- The bolt name `BoltBone` in S1 is not in this run's dump (it covers weapons, not ammo; `ArrowWood` was checked by
  another mod's tests): check it with the spawn command.
- The default numbers (Jump ×1.2 damage / ×2 stagger, Roll ×1.3 / ×1.5, Window 0.4 s, FlowStart 0.85 s, AimAngle 30°,
  Cooldown 1 s) and the balance estimates of 2.5: in-game tuning (T09, T13, T16, T19).
- Double-jump mods re-arming the jump token, EpicLoot per-weapon effects on moves, a separate dodge button (ZenCombat),
  attack-speed mods keyed on the attack name: not tested.
- Remote peers playing a late swing after a local start-check reset: visual only; seen only if a default fails.

**Open questions for the user** (at the pause):

1. Confirm v1 = jump attack + roll attack, with the sprint attack under Later (Decision 1).
2. The default animations (table 2.2, Decision 4): the battleaxe jump attack kept (a fast opener paid with a jump) and
   its roll attack Off; spears and sledges Off (Decision 12); fists `unarmed_attack1` for both moves (no kick).
3. The combo continuing from a move (Decision 3): a roll attack is followed by the finisher (vanilla ×2), a jump attack
   that plays the finisher hits ×1.2, not ×2.
4. The default numbers (section 5), the 1 s cooldown (A4), and "the bonus also applies to trees and rocks" (Decision
   9).
5. GCO installed → our jump attack off, roll attack on (Decision 15).
6. Vanilla input limits kept: no jump attack from sneak or with block held on keyboard and the default gamepad layout
   (the Alternative layouts allow it); an early press in a roll is lost; facing after a roll follows the player's own
   "attack towards look direction" option (Decisions 6, 7, 19).
7. Keep the 24 synced animation settings (A3, beyond the request), or ship a fixed table (the self-tests check it) with
   a Debug-only override for tuning, which removes 24 settings and the synced strings.
8. The roll flow (user feedback of 2026-09-30, Decisions 21-26): the roll attack now enters about 0.25 s sooner after
   the roll starts than a vanilla roll-then-attack, the stand-up it removes (`FlowStart` 0.85 s: the whole roll plays;
   0.7 s is the snappier option that also cuts the end of the roll; the i-frames always over 0.2 s before the swing);
   `FlowBlend` 0.15 s; a new press after a vanilla roll is a roll attack only while the roll still blends out, later
   it is a normal swing (so `Window` above about 0.2 s only matters after a dash from a dash mod); only roll attacks
   flow (a normal swing after a roll, a secondary, the battleaxe keep the vanilla stand-up); players without the mod
   (only with `AllowPlayersWithoutMod`) see a roll attack after the roll's stand-up.
9. The controller probe (Decision 27): to make the first roll attack of a session flow, the mod builds a hidden copy of
   the character's animation controller for a few milliseconds at the start of the first roll with each weapon type.

---

## 10. Implementation notes

Differences between this design and the code, and choices the design left open. Each later stage adds its own.

**Stage 1 (foundation: csproj, settings, rules, join check, network patches, compatibility lookup)**

- `MoveKind` (and a small `Moves` helper with the move names and setting sections) lives in its own file
  `MoveKind.cs` instead of `MoveTracker.cs`: the settings, `MoveRules` and `MoveTriggers` need it before the move
  logic exists.
- `MoveTriggers.Resolve` (cache, `HasParameter` check, fallback, one warning per move and family) and
  `MoveTriggers.StepOf` are written with the trigger list. Debug-only hooks for the self-tests:
  `MoveTriggers.TestHasParameter` (pretend Animator answer), `WarningCount` and `ResetWarnings()`. Rules set through
  `ServerRules.TestRules` skip the list check but not `HasParameter` (the watchdog test of 7.5).
- `ServerRules.Source` (`Own`, `Server`, `Pending`, `SelfTest`) feeds the "<own | server | self-test> settings" part of
  the Debug lines; `Pending` is a client of a server whose rules have not arrived, which plays with `MoveRules.Off`
  (both moves off, Decision 20).
- `MoveRules.TryRead`: a number that is not a number (NaN) becomes that setting's default, an infinity the nearest
  bound; both count as "clamped" (Warning). `MoveRules.Defaults()` builds the default rules without the config (for the
  self-tests and `MoveRules.Off`).
- Log texts drop the "Weapon Moveset:" prefix shown in 2.6 and 2.7: BepInEx already names the mod on every line
  (`[Warning:Weapon Moveset]`).
- The join check's line for a player let in by `AllowPlayersWithoutMod` reads `Not refusing <player>: their game
  <reason>, but AllowPlayersWithoutMod is on. …` instead of `<player> joined, but …` (2.8): the same line follows a
  re-check after `PeerStateChanged`, which is not a join.
- The Debug plugin list (2.8, 7.4) is logged with the first Goo's Combat Overhaul lookup (the first read of
  `Compat.GcoLoaded` after activation, in the world), not in `OnActivated`: the first activation runs inside our own
  `Awake`, before BepInEx has loaded (and listed) the plugins that come after ours.
- Settings text: the animation settings whose default is `Off` (spears, sledges, the battleaxe roll attack) add one
  sentence saying why; the roll attack's multipliers say "(1 = like a normal swing)" like the jump attack's;
  `Cooldown` and `Window` name their unit (seconds).
- `ModMultiplayerNotes` is the text of 7.1 with small additions ("(or the host)", "with or without the mod").

**Stage 2 (features: families, tokens, decision, clone edit, move watch, gameplay patches)**

- Files as in 7.1, plus the `MoveInfo` struct (the applied move and the watch: clone, item, kind, family, trigger, the
  clone's base name, chain level count and `m_maxYAngle` as they arrived, step, rules, roll age) in `MoveTracker.cs`.
  `MoveTracker`'s entry points are named after the patch that calls them: `BeforeStart` (`StartAttack` prefix, runs
  `Decide`), `OnAttackStart` (`Attack.Start` prefix), `AfterStart` (`StartAttack` postfix: consumes the tokens and
  starts the watch), `ClearCall` (finalizer), `OnJumped`, `Tick`, `IsStarting`, `OnRefused`, `Reset`. The pure
  `Decide` returns the move and, through `out` values, the move a cooldown blocked and the time left.
- `Families.Eligible(clone, weapon, out family)` also returns the family, so the gates and the family lookup run once.
- **Debug lines are written in the `StartAttack` postfix, only when the attack really started**: the move line of 2.8,
  "skipped: cooldown" and "skipped: stamina for a normal swing only". `Attack.Start` can refuse after our edit (stamina,
  ammo) and vanilla then retries every tick for 0.5 s: logging in the `Attack.Start` prefix would write one line per
  retry.
- "Refused an attack restart while <trigger> was starting." is written once per move (the Debug counter
  `RefusedRestarts` counts every refusal): a held button would otherwise log it on every tick of the starting phase.
- **Added case: a roll or a stagger before the move enters its state.** Vanilla starts a queued roll in the restart gap
  (the Animator does not show the attack yet, so `UpdateDodge` sees `!InAttack()`), and a hit can stagger the player
  there. The watch then drops the move at once (trigger reset, clone stopped and taken off the player as in the
  cancel), with a Debug line, no warning and no re-queued press. Without it the start check would cancel the move in
  the middle of the roll, log a wrong "did not start" warning, and queue a press the roll would swallow.
- "Dropped by the game" also covers a clone that was stopped (`IsDone`) while it is still `m_currentAttack` (vanilla
  `Abort`), not only one that is no longer `m_currentAttack`. When the new current attack uses the same trigger name,
  the trigger is kept and the Debug line says so.
- Deadline with `InAttack()` true: after the one extra tick, a clone that still has no `m_wasInAttack` counts as
  started without the combo continuation (Debug line says so); the cooldown starts there.
- The restart refusal (`IsStarting`) has its own failsafe limit of 0.6 s: the watch cancels at 0.5 s in the same tick,
  after the attack input, so the refusal never needs longer; the limit only matters if the watch stopped ticking.
- A new successful attack start while a watch is still running (the move entered and a chain followed, or the old clone
  was stopped) finishes the old watch first: aim put back, and a move that had not entered goes through "dropped".
- Stagger cap: `min(own × Stagger, 99)`, except that a swing whose own multiplier is already 100 or more (a modded
  weapon) is never capped below its own value, so the mod never takes a weapon's own instant stagger away.
- The stamina fallback only runs when `StaminaMultiplier` is above 1 and the swing costs stamina: otherwise the move
  cannot cost more than the normal swing.
- The roll token expires in `Tick` 2 s (the largest `Window`) after the roll, so `Decide` stops running for old rolls.
- A roll edge while `IsStaggering()`, a new local `Player`, `Reset` (activation and deactivation) clear the tokens;
  `Reset` also puts a pitched jump attack's `m_maxYAngle` back.
- The trigger reset uses the Animator of `ZSyncAnimation` (`m_zanim.m_animator`), the one `RPC_SetTrigger` set it on.
- `Moves.Title` gives the capitalised move name for the start of log lines.
- Debug-only hooks for the stage 3 self-tests: `MoveTracker.JumpLive`, `RollEndAt`, `LastMoveAt`, `Watching`,
  `WatchStarting`, `Watched`, `LastMove` (kept after the watch ends), `LastEntryDelay`, and the counters
  `RefusedRestarts`, `TriggerResets`, `StartFailures`.
- At each new local player: the Goo's Combat Overhaul lookup (Info line and Debug plugin list at spawn) and, in Debug
  builds, the Animator update mode line.

**Stage 3 (in-world self-tests, `SelfTests.cs`)**

- Eleven tests instead of the nine of 7.5: `moveset.jump` and `moveset.roll` are each split into a family test
  (`moveset.jump`, `moveset.roll`: every row of table 2.2, 13 items including bare hands, `FistFenrirClaw` and the Off
  rows) and an input test (`moveset.jump-input`: held button at 20 FPS, jump and land then press, fall without a jump,
  jump in the restart gap; `moveset.roll-input`: held button at 50 and 20 FPS, two `StartAttack` calls in one tick,
  window inside and outside, cooldown). Each test must end inside the probe's 120 s timeout, and one test running
  every family plus every input case would not.
- `moveset.order` patches `PlayerController.FixedUpdate`, `Player.FixedUpdate`, `MonoUpdaters.FixedUpdate` and
  `Humanoid.CustomFixedUpdate` with a temporary Harmony instance (`<GUID>.selftest.order`) that exists only while the
  test runs (removed in `finally`), instead of permanent Debug hooks in the mod's patches. It logs the call order per
  physics tick (with `Time.frameCount` and `MonoUpdaters.UpdateCount`) at the normal frame rate and at 20 FPS, plus the
  Animator's update and culling modes. It passes when it saw the player and humanoid updates; the order itself is a NOTE.
- Code changes made for the tests: `ServerRules.Pick` (the pure client rule pick behind `ServerRules.Current`, so
  `moveset.rules` can prove "a connected client without the server's rules has both moves off"), and the Debug-only
  `MoveTracker.StartWarnings` counter and `MoveTracker.ResetWarnings()` (the watchdog proves the warning is logged
  once; the flags are cleared again after the test so a real failure later still warns).
- **Fix found while writing the tests:** the move watch now puts a jump attack's `m_maxYAngle` back when the swing
  ends or is replaced, not only on landing. Before, a fast jump attack that finished in the air kept the pitched aim on
  its finished clone. That clone can no longer hit, so it changed nothing in play, but the design's "level again once
  the player lands" was not true of the clone.
- The rules forced in memory (`ServerRules.TestRules`) use `Cooldown` 0 in the family loops (the cooldown has its own
  checks in `moveset.rules` and `moveset.roll-input`) and multipliers that differ from 1 (jump 1.5 / 2.5 / 1.25 / 1.5,
  roll 1.4 / 1.75 / 1.2 / 1.25), so the checks on the clone's damage, stagger, push and stamina prove the edit.
- `moveset.roll` and the other roll checks use no training dummy (a roll into it would stop the roll). Every test puts
  the player back at its start after each try and rolls or jumps toward the first direction with no wall within 6 m
  and flat ground (raycasts), so the spawn stones cannot block a roll.
- `moveset.exclusions` takes the item off the player on the tick its attack starts: the check is made at the start
  (the item's own animation, no move, both tokens used up), and the pickaxe never digs, the bomb never flies and the
  Smoke Screen is never thrown. Stackable items get a stack of two, non-stackable ones one (`Inventory.AddItem` makes
  two separate items of a non-stackable).
- `moveset.stamina` proves "the move costs three swings" by the largest one-tick stamina drop after the press (the
  payment at entry), and "no retry loop" by the normal swing starting on the first tick after the press.
- `moveset.jump-input`, restart gap: when the swing is already in its animation state right after its first tick (the
  gap closed before the test could jump), the case is written as a NOTE ("not exercised") instead of a failure.
- `moveset.watchdog` fails only when no candidate trigger stayed pending until the deadline (the start check could not
  be tested); a candidate the Animator consumes or enters is a NOTE, and the next candidate is tried (7.5).
- Added checks and NOTEs beyond 7.5: the press-to-roll delay, roll length and i-frame length per family; after every
  roll attack the next press continues the combo and its step enters its animation; the fastest vanilla loop "roll,
  roll attack, roll as soon as it ends, roll attack" measured against the default 1 s cooldown (settles the claim of
  Decision 8 and 2.5); every family has at least one craftable weapon; `BoltBone` and `ArrowWood` exist (setup S1);
  whether Goo's Combat Overhaul was detected; gravity, jump force, jump apex and airtime; the "attack towards look
  direction" option of the test profile.
- `SelfTests.Unregister()` is called in its own `try` / `PatchGuard.Report` in `OnDeactivated`: a `[Conditional]`
  method cannot be passed to `Step` as a delegate.

**Stage 4 (docs: `README.md`, `CHANGELOG.md`, `TESTING.md`)**

- **Correction to S2 (section 8):** god mode does not hide stamina costs. `Player.UseStamina` and `HaveStamina` have
  no god-mode check (`god` only keeps health above 0, `Character.ApplyDamage`); what hides the costs is
  `Game.m_staminaRate`, set by the stamina world modifier (0 in the self-test world). `TESTING.md` says so, and T13
  asks for a world with the default stamina modifier instead of "god mode off".
- `TESTING.md` keeps the IDs of section 8 (T01-T17, M01-M08, X01-X07, L01-L03) and adds two items: **T18** (the
  stage 2 added case: a roll started while a move is starting drops the move with a Debug line, no warning, no late
  swing) and **X08** (Creature Kill and Tame Counts: a kill by a move counts as a melee kill; vanilla picks the kill
  type from the hit's skill, which a move keeps). L01-L03 sit at the end of the single-player section (the sections
  are "single player", "multiplayer", "other mods").
- Setup: every spawn name was checked in the 1.0.16 runtime data except `ArrowWood`, `BoltBone` (ammo, not in the
  dump) and `MC_SmokeScreen` (Sneak Ambush's item), marked "(unverified)"; the `moveset.triggers` self-test notes
  whether the two ammo items exist. Added to the setup: `Tankard` and `Hammer` (in the `moveset.triggers` gates), the
  vanilla console commands `heal`, `killall` and `maxfps 20` (`Terminal`: sets and saves the graphics FPS limit) for
  the low-frame-rate cases of T06.
- T09: the push check uses the `Club` on the `Greyling` (20 health; a `SwordIron` swing, 55 slash, kills it at once).
  The `Troll` stagger check: its stagger bar is 600 × 0.3 = 180 (`GetStaggerTreshold`); an iron-sword roll attack at
  ×10 stagger adds 55 × 1.3 × 10 × the random skill factor (`Skills.GetRandomSkillFactor`: 0.25-0.55 at skill 0,
  0.55-0.85 at skill 50), so T09 starts with `raiseskill Swords 50` (at skill 0 the lowest rolls fall just short);
  a normal swing (at most 55) never reaches 180. This assumes the Troll takes slash damage normally (its damage
  modifiers are not in the dump: unverified). The `piece_TrainingDummy` never staggers (`m_staggerDamageFactor`
  100): it is used only for the damage check.
- T14: the in-game label of the Gameplay option behind `Player.AttackTowardsPlayerLookDir` is UI data, not code; the
  item is marked "(unverified option label)" and the README describes the option by what it does.
- README: the defaults of table 2.2 are shown per weapon type with the swing they play ("third swing",
  `swing_longsword2`); the configuration table lists all 24 animation settings one per row, then the list of values;
  the compatibility section names the sibling MC Combat mods and the other mods of 6.2 (GUIDs left out: several are
  unverified).

**Stage 5 (conformance review)**

Every goal, patch, setting, RPC, self-test and `TESTING.md` item was checked against this design and `.ref`. Fixes:

- **A client ignores the server's settings while its own copy is off.** The settings handler stays registered on the
  connection after the feature turns off (a `ZRpc` has no unregister), so a server push that arrived while the
  client's copy was off (turned off, or another network version) was stored and logged "Using the server's move
  settings". The client now drops it, as section 4 says: turned on again, it asks for the rules and the moves stay off
  until they arrive.
- **"Dropped" when the stopped clone is still the current attack.** Vanilla `Attack.Update` aborts an attack that
  enters its state while the player is staggering, and `Attack.Stop` clears `m_wasInAttack`, so the watch saw a done
  clone that never "entered". It kept the trigger and logged "the new attack uses the same animation" about the
  move's own clone. Now the trigger is reset (harmless when already used) and the line says "trigger reset".
- `MoveTriggers` treats a player whose `ZSyncAnimation` has no Animator yet like one with no `ZSyncAnimation` (all Off,
  retry at the next attack) instead of letting `HasParameter` throw.
- A few code comments rewritten in caveman speech.

No other deviation was found; the earlier stages' notes above still hold.

**Stage 6 (first in-world self-test run)**

Eight of the eleven tests passed (`moveset.triggers`, `rules`, `jump-input`, `roll-input`, `aim`, `exclusions`,
`watchdog`, `order`). The three failures were test bugs; the mod code did not change.

- **`moveset.jump`, "dummy has no target" (every row after the first hit) and "the dummy never had a target".** The
  check assumed a disabled `MonsterAI` keeps no target. It does: `BaseAI.Awake` adds `OnDamaged` to
  `Character.m_onDamaged`, and `MonsterAI.OnDamaged` calls `SetTarget(attacker)` on every hit, whatever the component's
  `enabled` state. `SwordIron`, the first row, hit the dummy, so every later row saw the player as its target. What
  the check guards (the dummy never fights back or pushes the player) is now asserted directly: the AI is still
  disabled and out of `BaseAI.Instances` (no `UpdateAI`: no attack, no movement, no regeneration) and the dummy never
  started an attack. The target after the hits is a NOTE.
- **`moveset.jump`, "no hit seen"** (NOTE, not a check) for knives, dual knives, spears and bare hands: probably
  geometry, not measured: these weapons reach 1.5-1.9 m against a dummy 1.8 m ahead, and a standing jump in place
  lifts the swing's origin (the weapon's attack height over the feet) by up to 1.5 m. Longer weapons hit it in the
  air. The NOTE now gives the weapon's reach and attack height.
- **`moveset.roll`, `AxeBerzerkr` ("the roll started and ended", "the buffered press started the attack on the tick
  after the roll ended", "a roll attack started"), and `moveset.stamina` case B ("roll attack with three times the
  swing's stamina", "took three swings of stamina ... (0, expected 29.8)").** The shared "wait until idle" returned in
  the restart gap of a swing that had just started: `InAttack()` was still false (the Animator needs about 0.2 s to
  leave the roll) and `m_timeSinceLastAttack` was still large, because the Battleaxe row (roll attack Off) stops
  watching on the tick the normal swing starts, and case A checks its normal swing on its first tick. The next step
  then took the battleaxe off (vanilla `UnequipItem` stops the clone but leaves its Animator trigger set) or rolled,
  and the Animator played the stale swing a few ticks later. For `AxeBerzerkr` that swing outlasted the 0.5 s dodge
  buffer, so the roll never started; bare hands, after the Spear row, rolled only 0.5 s after the call for the same
  reason. In case B the stale swing of case A ran into case B's roll and press: no attack entered its animation after
  the press (every swing pays its stamina on entry, and the largest drop was 0). Idle now also waits for the current attack to be done (7.5), case B checks
  that its roll ended, and `moveset.exclusions` resets the trigger of the attack it takes off in the restart gap. The
  NOTE "fastest roll loop: could not measure" of `moveset.roll-input` had the same cause (its second roll followed the
  cooldown case's normal swing).
- The stamina world modifier: `moveset.stamina` already removed `StaminaRate` (`ZoneSystem.RemoveGlobalKey`, handled
  at once on the host) and put it back with `SetGlobalKey(GlobalKeys.StaminaRate, <old value>)`; its check "stamina is
  used again" passed, so the zero payment was not the modifier.
- Warnings: the mod logs none at game quit (the lines after "Steam manager on destroy" belong to Sneak Ambush). The
  warnings of the run are the ones `moveset.triggers` (unknown and missing animation names) and `moveset.watchdog`
  (a move animation that never starts) cause on purpose.

**Stage 7 (review fixes)**

- **The jump owns the air attack.** `Decide` returned a roll attack for "roll, jump, attack in the air" when the jump
  attack was off (`JumpAttack = false`) or GCO was installed: the roll branch had no air check and a jump does not
  clear the roll token. So `JumpAttack = false` still gave a move on jump + attack, and our roll attack landed on GCO's
  jump attack (the clash Decision 15 avoids). Now a live jump token in the air with the jump attack unavailable gives a
  normal swing (2.3, 2.4). A roll that ends in the air without a jump (off a ledge) still gives a roll attack. The
  `moveset.rules` truth table was changed to match (it asserted the old result) and gained these cases; T15 and X07
  check it in game.
- **"Dropped" compares the name the new attack really fired.** It compared the new attack's `m_attackAnimation` with
  the dropped move's trigger, but a chained clone keeps only the base name (`swing_longsword`) while vanilla fires
  `base + chain level`, so the "same trigger, keep it" case could only match single-animation weapons. New
  `MoveTracker.FiredTrigger` rebuilds the fired name as `Attack.Start` does (a random-animation attack counts as
  different); `moveset.rules` checks it.
- **`AimAngle` range 0-45 (was 0-90).** `GetMeleeAttackDir` aims at the look's height on the unit body heading, so the
  aim pitch is `atan(sin(look pitch))`, at most 45° (2.3): every value from 45 to 90 behaved the same. The description
  says the swing tilts less than the view (30° down → about 27°). Wire values above 45 are clamped like any out-of-range
  number. `moveset.aim` gained a case (AimAngle 45, looking 85° down → about 45°, never more).
- **Warnings under the server's rules.** On a client of a server the animations come from the server's rules and the
  client's own settings do nothing (Decision 20), yet both warnings (unknown animation, start check) told the player to
  change their own settings. `ServerRules.FromServer(rules)` (reference compare with the stored server rules) picks the
  last sentence, `Moves.AnimationAdvice`: own rules → "Pick another animation from the list in the "…" settings." (the
  start check said "Try another animation in …" before), server rules → the setting comes from the server, ask its
  admin. A dedicated server never runs either check (no local player): the README tells admins to try a custom
  animation in single player or as a host.
- **Settings push waits 0.5 s.** Every rule change marked a push for the next `ZNet.Update`, so a slider dragged in
  ConfigurationManager sent the whole `MoveRules` package to every player each frame, and each client logged "Using the
  server's move settings" and rebuilt its trigger cache each time. The server now pushes once the settings have not
  changed for `ServerRules.PushDelay` (0.5 s, unscaled time); turning the copy on still pushes at once. M01 checks it.
- **Trigger cache rebuild reads the parameters once.** `ZSyncAnimation.HasParameter` copies the Animator's whole
  parameter array (about 150 objects) on every call, and the rebuild called it about 20 times inside the `Attack.Start`
  prefix. `MoveTriggers.Build` now reads `Animator.parameters` once into a reused set of Trigger names (same test); an
  Animator with no parameters yet is treated like a missing one (all Off, no warning, retry).
- **Labels of the dual rows.** MC Dual Wielding gives every pair except two knives its `PairMoves` template (default the
  Berserkir axes, `dualaxes`), so sword, mace and mixed pairs use the `DualAxes` rows. The family labels (settings
  descriptions, warnings), the README table and its Dual Wielding note say so.
- The join check and GCO log lines use `ModInfo.Name` instead of the literal mod name.
- `TESTING.md`: T05's second half now tests the reset of a jump attack that starts in the air and hits after touchdown
  (an attack started after landing is never a jump attack, so the old check could not fail); M03 names the weapon and
  skill of T09 (`SwordIron`, Swords 50), without which one roll attack can fall short of the Troll's stagger bar.

**Stage 8 (roll flow, user feedback after the first in-game test, 2026-09-30)**

Implements Decisions 21-25 (G2's flow). Built clean (Debug and Release); not yet run in the game or in the in-world
self-tests: every roll-flow item of section 9 waits for the next `Test-InWorld.ps1` run and for T19-T21 and M09.

- New files: `RollFlow.cs` (cross-fade, state lookup, learning store, other players) and
  `Patches/ZSyncAnimationPatches.cs`. `MoveKind`, `Families`, `MoveTriggers` and the join check are unchanged.
- `Decide` takes the time inside the roll (`rollTime`, negative when not in a roll) and the i-frame flag; the
  `FlowStart` compare has a 0.0001 s slack (`Time.fixedTime` steps are floats: 0.7 s after the roll start can read
  0.69999). The caller passes a negative `rollTime` unless the in-roll token is live, `m_inDodge` is true and layer 0's
  next-or-current tag is `dodge` (`Character.GetNextOrCurrentAnimHash`, cached per tick).
- The start check has its own clock (`WatchClockAt`), separate from the real start time: the Debug "started after" line
  and `LastEntryDelay` keep the delay from the attack start, while the deadline and the restart refusal count from the
  roll's end for a cut that plays with the trigger alone.
- "A roll started" (the Interrupted case of 2.7) compares the roll counter instead of reading `m_inDodge`, so the roll
  a move cut into never drops it.
- The roll attack's Debug line is built after the cross-fade (the StartAttack postfix runs `RollFlow.Cut` before it
  starts the watch), so it names the flow result and the state source; `MoveEdit.Describe` takes that text.
- Learning runs for every local attack start whose trigger has no learned state: moves that were not cross-faded, and
  normal swings (trigger = `FiredTrigger`, so a random-animation attack is skipped). It reads layer 0 only while it
  waits (at most 1.5 s).
- The name lookup tries the clip name of the 1.0.16 table, then the trigger name, each as the full path
  (`<layer 0 name>.<name>`, `Animator.GetLayerName(0)`) and then as the short name; the result (a hash or "none") is
  cached per trigger for the controller it was probed on.
- On other players, the filter order is: the rules' `RollAttack` and a string compare with the twelve roll animations,
  the local player's own `ZSyncAnimation` skipped (its roll attacks are cut by `MoveTracker`, which knows whether the
  trigger is a move), the dodge-state test, then the `Player` component; the Debug line names the player. It compares
  with the roll animation names *as configured* in the rules, not as resolved by the attacker's `MoveTriggers`: when a
  server's name is not an animation of the game and the attacker falls back to the default (2.6, with a warning), the
  watchers do not cross-fade that fallback (visual only; the server's setting is wrong anyway).
- Debug-only hooks: `MoveTracker.RollActive`, `RollStartAt`, `RollSeq`, `WatchClockAt`, `Learning`, `LastStartAt`,
  `GateRefusals` (attacks refused inside a roll); `RollFlow.TestNoState` (trigger alone), `TestNoNames` (learned states
  only), `ForgetLearned`, `ProbeHash`, `LearnedHash`, `ClipOf`, `TableTriggers`, counters `CrossFades`, `RemoteCuts`,
  `LastSource`, `LastHash`.
- Self-tests (7.5): `moveset.roll` rows share a `Roll` helper that records the roll, the i-frames, the cut, the dodge
  state's normalized time, the gap ticks and the state at entry; `moveset.roll-input` gained the double start inside the
  roll, the press 0.06 s after the roll, the learned-state case and the other player's view, and its fastest-loop NOTE
  now measures to the second move's real start (`LastStartAt`); `moveset.stamina` gained case C (press held in the roll
  with stamina for a normal swing only); `moveset.exclusions` checks that no attack starts inside the roll;
  `moveset.watchdog` forces the trigger alone and times the deadline on the start-check clock; `moveset.triggers` checks
  that every default roll animation is in the name table and NOTEs which states are found by name. Test names are
  unchanged.
**Stage 9 (review fixes of the roll flow, 2026-09-30)**

Built clean (Debug); not yet run in the game or in the in-world self-tests, like stage 8.

- **A press after the roll is a roll attack only while the roll still blends out** (Decision 26). `Decide` takes
  `flowOver`: the roll ended out of the vanilla dodge state (recorded on the true → false edge from the game's cached
  layer-0 tags) and layer 0 is no longer in it. Before, a press 0.2-0.4 s after the roll got a roll attack from idle or
  locomotion after the full stand-up, while the README promised a flow. `Window` keeps its meaning after a dash;
  `moveset.rules` gained two truth-table cases, `moveset.roll-input` the 0.3 s press and a `Window` 1.5 case, TESTING
  T08 (rewritten) and T22 (new).
- **No cut without a known attack state.** `MoveTracker.OnAttackStart` refuses a cut whose trigger has no state on the
  local Animator (`RollFlow.HasStateFor`), and the check before the gate (next point) keeps the gate shut in that case.
  With the trigger alone, `Attack.Start` had turned the player to the attack direction in the middle of the roll (and
  redirected the rest of its root motion), and other players with the mod who knew the state showed the swing about 0.4
  s early. The attack now starts after the roll, in its blend out, with the trigger alone; learning records its state.
  `moveset.roll-input` (learned state) and `moveset.watchdog` expect the start after the roll, and the watchdog's clock
  now runs from the attack start. The wait of the start-check clock inside the roll stays as a guard.
- **Check before the roll gate** (`MoveTracker.CanFlow`): from the cut point to the roll's end, the gate opens only for a
  weapon whose own primary attack is eligible, whose family's roll animation is on and whose attack state is known; a
  weapon in each hand (a Dual Wielding pair) always passes. Before, every buffered or held primary press in that time
  opened the gate, so vanilla stopped the previous attack, cloned the weapon's attack and ran Dual Wielding's
  `Attack.Start` prefix before our prefix refused it, about once per tick for Off families and ineligible items. No
  cache per weapon and rules: the check runs a few ticks per roll and allocates nothing (the trigger lookup is already
  cached per rules). Debug counter `GateSkips`; the Off rows of `moveset.roll` now expect the gate to stay shut (no
  refusal) instead of a refusal.
- **Learned states are checked against the current controller** before learning is skipped (`RollFlow.Knows(animator,
  trigger)` calls `HasState`), so a state lost to a controller swap is learned again (and overwritten) instead of
  blocking learning for the session.
- **Default `FlowStart` 0.85 s** (was 0.7, Decision 22): the whole roll plays and only the stand-up goes; 0.7 is the
  documented snappier option. README, settings text, CHANGELOG, T06 and T19 follow; T19 also compares the ground a roll
  covers with and without a roll attack.
- **`IframeMargin` (0.2 s)** between the i-frames' end and the cut (Decision 21's hard rule on other peers, 1.5): `Tick`
  records the tick it sees `m_dodgeInvincible` turn false, and `BeforeStart` passes the i-frame flag as on until 0.2 s
  after it (also while `Tick` has not seen the end yet). `moveset.roll`'s cut point includes it; `moveset.roll-input`
  gained a `FlowStart` 0 case; TESTING T19 and M10 (new) cover it in game.

**Stage 10 (in-world run of 2026-09-30: the first roll attack of a session, Decision 27)**

The full run (66 of 71 tests) passed every moveset test but two. Built clean (Debug and Release); not yet run in the
game again.

- **`moveset.roll`: the first roll attack with an axe, a two-handed sword, an atgeir or a knife played after the roll**
  (`TriggerOnly`, gap 0.22 s, cut at 0.94 s instead of 0.85 s). No state was learned for `swing_axe1`, `greatsword1`,
  `atgeir_attack1` or `knife_stab1` (no earlier test swung them), and the name lookup found none: the `moveset.triggers`
  NOTE listed all 16 clip-table triggers as "not found" (the states are not named after their clips or triggers). Swords,
  maces, dual axes and fists flowed only because earlier tests had taught their states. Fix: the name lookup is gone;
  the new `StateProbe` asks a hidden copy of the controller (2.9, Decision 27) and `RollFlow` caches its answer per
  trigger and controller (`StateSource.Probed`, Debug text "state from the animation controller"). `MoveTracker.Tick`
  asks at each roll start for the current weapon's roll animation (`Prepare`; a pair: the `DualAxes` and `DualKnives`
  animations), so the probe's cost falls there. `CrossFadeInFixedTime` already used the state's full-path hash.
- **`moveset.roll-input`, "other player's view: no second swing after it" was a test bug.** The test waited for the
  swing to end with `p.InAttack()`, which reads `Character`'s per-tick tag cache (`GetCurrentAnimHash`, keyed on
  `MonoUpdaters.UpdateCount`). The cache is filled during `FixedUpdate`, before the Animator's own update of that
  physics step (update mode Fixed), and the test coroutine runs after it (`WaitForFixedUpdate`): on the tick the
  cross-fade began, the Animator was already heading into the attack state, but the cache still said "not in attack",
  so the wait ended at once and the replay window saw the first swing ("played twice"). The test now reads the
  Animator itself (`RollFlow.NextOrCurrent`), waits for the swing to end (new check: "the swing ended"), then watches
  0.6 s for another attack state. The mod code was right: `RollFlow.Cut` resets the trigger (the check "no longer
  pending" passed).
- Test changes (no check loosened): `moveset.triggers` replaces the by-name NOTE with checks on the probe (every table
  trigger found, playing the table's clip, equal to a learned state when one exists, the player's own
  `StateController`s untouched, none shared with the copy) and a NOTE (cost, muted behaviours, the bare-hands batch); `moveset.roll` forgets every
  state first (`RollFlow.ForgetAll`) and adds two checks per row (the first use of a roll animation came from the probe;
  the Animator entered the state the cut cross-faded into); `moveset.roll-input`'s learned-state case compares the
  probe's state with the entered one (a check instead of the NOTE when no name matched), with the probe off
  (`RollFlow.TestNoProbe`, formerly `TestNoNames`) so the trigger-alone path is still covered.
- The trigger → clip table is Debug-only now (the self-tests' reference); `RollFlow.ProbeHash` became `ProbeNow` (a
  fresh, uncached probe of several triggers with a chosen stance), plus `ForgetAll`; `StateProbe` keeps the last
  batch's time, update count, stance and muted-behaviour count for the NOTEs.
- `HumanoidPatches` comment and 6.1 follow the sibling mods' current behaviour: Tower Shield Wall's prefix skips the
  original for a bash inside its `BashCooldown` (default bash `ShieldPunch`, network version 2), Dual Wielding's skips
  it while a hand swap is queued; Creature Morale's creatures are afraid (no longer calm): README, TESTING X03-X05 and
  6.1 say so. TESTING gained T23 (the first roll attack of a session with four weapon types).
