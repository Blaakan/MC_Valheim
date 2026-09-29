# Harpoon Hooks Tames — design

| | |
|---|---|
| Mod | Harpoon Hooks Tames |
| GUID / project | `MC.Farming.Harpoon.HooksTames` (`src/Farming/Harpoon.HooksTames/`) |
| Category / scope | Farming / QoL |
| Side | Client (only the player who throws the harpoon needs it; the tame's owner benefits from running it too, see 3.7) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/`; prefab names and English strings checked in the installed game data (`StreamingAssets/SoftRef/manifest_extended`, `resources.assets` localization table) |
| Status | Implemented (0.1.0), smoke test passed, in-game testing pending |

## Goal

Requirement from the user (2026-09-28): "When using harpoon on tamed animal, they should be attached like against
enemies but without damaging the tamed animal." Made precise:

1. A thrown Abyssal Harpoon (`SpearChitin`) that hits a **tamed** creature hooks it exactly like a wild creature:
   same status effect (`SE_Harpooned`), same "<creature> harpooned" message, same line, same pull (the tame is dragged
   when you walk away), same stamina drain, same release rules (line breaks when too far or out of stamina, blocking or
   attacking releases it after 2 s), same 30 m limit.
2. That hit does **no harm** to the tame: no health loss, no damage number, no knockback (push), no stagger, no
   sneak-attack bonus, and the tame does not become alerted, flee or retaliate. It is not treated as an attack
   either: it gives no Spears XP and no adrenaline (decision 5), and it leaves no "attacker" mark that would credit
   you with the tame's kill later (decision 6; when the tame's owner also runs the mod, installed and turned on).
3. Nothing else changes: wild creatures, players (PvP on or off), bosses and every other weapon, projectile and
   damage source behave exactly as in vanilla. Only harpoon hits on tamed creatures are affected. **Every** tame is
   hookable, whatever its AI is doing (fighting, chasing prey, fleeing, watching a wild creature it senses): the main
   use is pulling back a tame that is busy with something. The only exception is a tame ridden by a player, which
   keeps the vanilla PvP rule (decision 2). Consequence: a tame standing in the line of fire catches a harpoon thrown
   at an enemy behind it (hooked, no damage) and the enemy is not hooked, exactly as vanilla already does with PvP on,
   where the tame is also damaged (decision 7).
4. Client-side and multiplayer-compatible: only the thrower needs the mod, including when the tame is simulated by
   another player's game that does not have the mod.
5. Live toggle like every MC mod: turned off, harpoons pass through tames again immediately (vanilla).

Non-goals: tuning the pull (speed, break distance, stamina drain, mass scaling), lifting tames onto a ship deck
(vanilla pulls horizontally only), a leash mode without line break, restricting to "your" tames or tames inside your
ward (vanilla has no tame ownership), fixing where a "stay" tame walks after release, protecting tames from other
players' harpoons thrown without the mod, protecting tames from the environment they are dragged into, new items or
recipes.

---

## 1. Vanilla behaviour (code trace)

### 1.1 The weapon and its throw

- Item `SpearChitin`, English name "Abyssal Harpoon" (`$item_spear_chitin`), projectile prefab
  `projectile_chitinharpoon`, status effect asset `Harpooned` (`SE_Harpooned`), line effect `vfx_Harpooned`.
  All four names are verified in the game's `SoftRef/manifest_extended`; the English name and the messages below are
  verified in `resources.assets`. The throw is the **primary** attack (wiki; the Goldenrevolver mod moves it to the
  secondary). Workbench level 2, durability 50, 10 pierce, 20 knockback, 1x backstab, 15 stamina (wiki).
- The throw is `Attack.ProjectileAttackTriggered` → `Attack.FireProjectileBurst`. For each projectile it builds a
  `HitData` with `m_damage` (weapon damage), `m_pushForce` (`m_attackForce * m_forceMultiplier`, scaled by the random
  skill factor), `m_backstabBonus`, `m_staggerMultiplier`, `m_skill` = the weapon skill (Spears),
  `m_skillRaiseAmount`, `SetAttacker(m_character)`, and
  `m_statusEffectHash = m_weapon.m_shared.m_attackStatusEffect.NameHash()` (subject to
  `m_attackStatusEffectChance`), then `Projectile.Setup(owner, velocity, hitNoise, hitData, weapon, ammo)`.
- `Projectile.Setup` copies those fields into the projectile (`m_raiseSkillAmount = hitData.m_skillRaiseAmount`,
  `m_weapon = item`, ...) and **overwrites** the projectile's own `m_statusEffectHash` with
  `hitData.m_statusEffectHash` when they differ. So the hook comes from
  `SpearChitin.m_shared.m_attackStatusEffect` = `Harpooned` (inferred from the code: otherwise vanilla would never
  hook; prefab value not readable). It also connects the projectile's `LineConnect` to the owner (the visible rope).
  `m_adrenaline` is a projectile prefab field (code default 2), not set by `Setup`.
- The projectile is created on the thrower's machine and only its owner runs `Projectile.FixedUpdate` physics and
  hits (`if (!m_nview.IsOwner()) return`). `m_owner` (private `Character`) is set only there, by `Setup`.

### 1.2 The target filter: why tames are skipped

`Projectile.FixedUpdate` raycasts along the flight step, sorts the hits by distance and calls
`Projectile.OnHit(Collider, Vector3, bool, Vector3)` for each one, **stopping at the first one that sets `m_didHit`**
(or bounces). `OnHit` finds the `IDestructible` of the hit collider (`FindHitObject`) and calls the private
`Projectile.IsValidTarget(IDestructible destr)`; `false` → `return` (the projectile keeps flying and the loop tries the
next collider behind it). `DoAOE` calls the same filter for area projectiles. Consequence: any target the filter
accepts **shields everything behind it** — accepting tames means a tame in the line of fire stops a harpoon aimed at
an enemy behind it (accepted consequence, decision 7).

`IsValidTarget` for a `Character`, in order:

1. The owner itself → false unless `m_hitOwner`.
2. If `m_owner != null && !m_hitFriendly`: `flag = BaseAI.IsEnemy(owner, target) || (target is aggravatable && owner
   is a player)`. A **player owner without PvP** and `!flag` → false. A non-player owner and `!flag` → false.
3. `m_dodgeable && target.IsDodgeInvincible()` → false (players only; `Character.IsDodgeInvincible` is `false`).
4. Otherwise true.

`BaseAI.IsEnemy(a, b)`: if either is tamed, they are **not** enemies when both are tamed or the other is in faction
`Players` (or Dverger, not aggravated). So for a player without PvP a tame is never valid: the harpoon passes through.
With **PvP on** (`Player.IsPVPEnabled`, toggle in the inventory, `Player.CanSwitchPVP` = 10 s out of combat) step 2 is
skipped and vanilla **does** hook tames — ridden mounts included — and damages them. A tame and a wild creature are
enemies of each other (wild wolves attack tamed boars).

The only `IHitProjectile` in the game is `ArcheryTarget` (not on creatures), and `OnHit` never bounces off a
`Character`, so once `IsValidTarget` accepts a character the hit always goes on to `Damage` in the same call.

Melee has its own filter (`Attack.DoMeleeAttack` / `DoAreaAttack`, with `SharedData.m_tamedOnly` for `KnifeButcher`);
it is not involved in a thrown harpoon.

### 1.3 The hit (attacker side)

After a valid target, `OnHit` (non-AOE case):

- builds a new `HitData` from the projectile: `m_damage` (or `default` when `m_noDamageFriendly` and the target is not
  an enemy — a prefab flag whose harpoon value is unknown), `m_pushForce = m_attackForce`, `m_backstabBonus`,
  `m_statusEffectHash`, `m_dodgeable`, `m_blockable`, `m_ranged = true`, `m_skill`, `m_skillRaiseAmount`,
  `SetAttacker(m_owner)`, `m_hitType = PlayerHit`; `m_staggerMultiplier` stays at its default 1;
- calls `destructible.Damage(hitData)` → `Character.Damage(HitData)` (not virtual, not overloaded): sets
  `m_weakSpot` and `m_nview.InvokeRPC("RPC_Damage", hit)`, which is routed to **the tame's owner** (may be this
  machine — then `ZRoutedRpc.InvokeRoutedRPC` handles it synchronously, on a serialized copy of the hit — another
  client, rarely the server);
- then, on the thrower: hit effects, `BaseAI.DoProjectileHitNoise` (alerts only creatures that are **enemies** of the
  thrower: tames are not), and because `didDamage` is set whenever `Damage` was called (whatever the amount):
  `m_owner.RaiseSkill(m_skill, m_raiseSkillAmount)` (Spears) and `m_owner.AddAdrenaline(m_adrenaline)`, both read
  from the **projectile's own fields** after `Damage` returned. `Player.RaiseSkill(skill, 0)` →
  `Skills.RaiseSkill(skill, 0)` → `Skill.Raise(0)` adds nothing and returns false (no level-up);
  `Player.AddAdrenaline(0)` changes nothing;
- `m_didHit = true` (unless `m_onlyStopOnTerrain`), `RPC_OnHit`, and, when the hit collider has a rigidbody and the
  prefab's attach flags (`m_attachToClosestBone` / `m_attachToRigidBody`) are set, `RPC_Attach` to the target's
  `ZNetView` (the harpoon sticks to the creature). A non-AOE projectile therefore hits once.

### 1.4 The hit (tame owner side): `Character.RPC_Damage(long, HitData)`

Private, one overload, registered in `Character.Awake` as `m_nview.Register<HitData>("RPC_Damage", RPC_Damage)`.
For a tame, in order:

| Step | What it does | With a zero-damage, zero-push hit |
|---|---|---|
| attacker is the **local** player (any machine the RPC runs on, i.e. the owner) | `IncrementPlayerStat(EnemyHits)`, `m_localPlayerHasHit = true` (a field never read) | still happens when the thrower owns the tame (not undone, see 3.6) |
| not the owner → return | | |
| `hit.m_staggerMultiplier >= 100` → `Stagger` | forced stagger | skipped (we send 0) |
| dead / teleporting / dodge checks | early exits | unchanged |
| `m_eitrAdd > 0` → `AddEitr` | | harpoon has none |
| `HaveAttacker() && attacker == null` → return | attacker not loaded | unchanged |
| attacker is a `Player`: ZDO key `(ZDOVars.s_attackers + playerName)` (the **decimal text of the int hash** of "Attackers" followed by the player name, hashed again by `ZDO.GetBool/Set(string)`) set to true, and if it was not set yet the ZDO int `s_attackers` (count) +1 | marks the thrower as an attacker of this creature; **never cleared** by vanilla | still happens (see 1.8); undone by our owner-side patch |
| `KillModifiers` in ZDO `s_modifiers` (read with default 5 = `CountNone`) from `hit.m_skill` (Spears → `Melee`; a different earlier modifier → `MixedAndTotal`) | kill-stat modifier | still happens; undone by our owner-side patch. Vanilla resets it only when `SetHealth` sets full health (to 0) |
| `m_seman.OnDamaged` | only `SE_Shield` / `SE_React` react | a Staff of Protection bubble plays its hit effect, absorbs 0 |
| `AggravateAllInArea` | only if `GetTotalDamage() > 0` | skipped |
| backstab (`m_backstabBonus > 1`, not alerted) | ×bonus and `m_backstabHitEffects` | skipped (we send 1) |
| `IsStaggering()` → ×2 and `m_critHitEffects` | crit effect if already staggering | cosmetic only, very rare |
| `m_blockable && IsBlocking()` → `BlockAttack` | `Humanoid.BlockAttack`: a successful block sets `hit.m_statusEffectHash = 0` (**no hook**), drains the blocker's stamina, raises Blocking, block effects, perfect block staggers the attacker | skipped (we send `m_blockable = false`) |
| `ApplyPushback(hit)` | knockback if `m_pushForce != 0` | skipped (we send 0) |
| status effect: `m_seman.AddStatusEffect(hash, …)` or `ResetTime` + `SetLevel`, then `SetAttacker(attacker)` | **the hook** | happens, as for wild creatures |
| resistances, armor, then `ApplyDamage` | | `GetTotalDamage() <= 0.1` → **early return**: no health change, no `DamageText`, no `OnDamaged`, no `m_onDamaged` |
| `AddFireDamage` … `AddLightningDamage` | DoT status effects | all `<= 0` → no-op |

Because `ApplyDamage` exits early, `Character.m_onDamaged` (subscribed by `BaseAI.OnDamaged`) never fires: no
`MonsterAI.OnDamaged` (`Wakeup`, `SetAlerted`, `SetTarget`), no `AnimalAI.OnDamaged` (alert → flee). A tame's
`MonsterAI.SetTarget` would ignore a player attacker anyway (`!attacker.IsPlayer() || !IsTamed()`).

ZDO notes for the restore (3.2): `ZDO.GetInt(int hash, out int value)` tells whether a key exists;
`ZDO.Set(int, int)` bumps the data revision when the value changes; `ZDO.RemoveInt(int)` does **not** bump it.
Bools are stored as ints (`ZDO.Set(int, bool)` → `Set(hash, value ? 1 : 0)`).

### 1.5 `SE_Harpooned` (runs on the tame's owner)

- `SEMan.Update` is called from `Character.CustomFixedUpdate` **only on the ZDO owner**; `SEMan.AddStatusEffect(int …)`
  forwards to the owner by RPC. SE instances are not synced, so the pull always runs on the owner's machine.
- `SetAttacker(attacker)`: logs `Setting attacker <name>` (vanilla `ZLog`), breaks at once on bosses
  (`Character.IsBoss`), breaks with `$msg_harpoon_targettoofar` ("Target too far") beyond `m_maxDistance` (code
  default 30), else stores the base distance, sends `<m_name> $msg_harpoon_harpooned` ("harpooned") to the attacker
  and links the `vfx_Harpooned` line to the attacker.
- `UpdateStatusEffect`: if the tame is not on a ship (`GetStandingOnShip() == null`) and not `IsAttached()`:
  `Utils.Pull(body, attackerPos, baseDistance, m_pullSpeed, m_pullForce, m_smoothDistance, noUpForce: true,
  useForce: true, m_forcePower)` — a force towards the attacker **only while the tame is farther than the base
  distance** (a leash of the initial length), never upwards; its strength is capped (`m_pullForce`, code default 0,
  set by the asset). Every `m_staminaDrainInterval` (0.1) it calls
  `attacker.UseStamina(m_staminaDrain * pull * m_character.GetMass())` (heavier creature = more drain). Breaks with
  `$msg_harpoon_linebroke` ("Line broke") when `distance − base > m_breakDistance` (code default 4), and with
  `<m_name> $msg_harpoon_released` ("released") when the attacker has no stamina. Whether a lox (heavy) can be dragged
  far before the line is released is **prefab data** (unverified; test T03).
- `IsDone`: also ends after `m_time > 2` when the attacker `IsBlocking()` or `InAttack()`.
- Values above are code defaults; the `Harpooned` asset may override them *(prefab, unverified)*.
- `Player.UseStamina` and `Player.Message` on a remote player send RPCs (`UseStamina`, `Message`) to that player's
  machine, so everything works when the owner is someone else. This is how vanilla already harpoons wild creatures
  simulated by another client.

### 1.6 Tame AI after the drag

- `BaseAI.IdleMovement`: a tame random-walks around its **current** position, unless it has a patrol point.
- `Tameable.RPC_Command` (the E "stay/follow" toggle): switching to stay calls `MonsterAI.SetPatrolPoint()` (current
  position, stored in ZDO `s_patrolPoint`); following calls `ResetPatrolPoint`. A **staying** tame dragged elsewhere
  walks back towards its stay point (`RandomMovement` heads back when more than `2 × m_randomMoveRange` away). A
  following tame keeps following. Never-commanded tames stay where they are dropped.
- "Has a target": `BaseAI.HaveTarget()` reads the ZDO bool `s_haveTargetHash`, written every AI update by the owner
  (`MonsterAI.UpdateTarget` / `AnimalAI.UpdateAI` via `BaseAI.SetTargetInfo`: true while the AI has a target
  creature). It is readable on every machine, so the thrower could tell whether a tame simulated by someone else is
  busy. The mod does **not** use it (decision 7): what sets it is much broader than "fighting", so skipping tames with
  a target made the hook fail most of the time near wild creatures:
  - `BaseAI.FindEnemy` (every 2 s near a player) picks the closest creature the tame can sense (`CanSenseTarget`:
    hear or see range) among its enemies, and `BaseAI.IsEnemy` makes a tame the enemy of **every** wild non-player
    creature (not one of the same `m_group`, not Dverger unless aggravated), passive ones such as deer included.
    Whether the tame can reach it does
    not matter (a creature seen through a fence counts).
  - `MonsterAI.UpdateTarget` drops the target when it dies, when a commanded tame's target is farther than
    `m_alertRange` from its stay point or followed player, after 30 s without sensing it, or after 60 s without
    attacking; then `m_updateTargetTimer = 5` and the next search picks it again if it is still sensed. A never
    commanded pen animal that sees a wild creature therefore reports a target most of the time (about 60 s out of
    every 65 s).
  - A tame chasing prey, fighting, or running from a predator it has targeted reports a target the whole time.

### 1.7 Riding, ships, falls, water, summons

- Riding: `Sadle.RPC_RequestControl` makes the rider the **owner** of the mount's ZDO; `Player.SetControls` turns any
  attack into `StopDoodadControl` (you cannot throw while riding). `Tameable.HaveRider()` (= `m_saddle` present and
  `Sadle.HaveValidUser()`, recomputed every `FixedUpdate` on every client from ZDO `s_user`) is the vanilla helper.
- Ships: `SE_Harpooned` does not pull a creature standing on a ship; `noUpForce` means a tame in the water cannot be
  pulled up onto a deck.
- Falls: only players take fall damage (`Character.UpdateGroundContact`: `if (IsPlayer() && num > 4f)`), so a hard
  pull never hurts a tame by itself. Lava, fire, Ashlands water etc. still hurt a creature dragged into them.
- Drowning: `Sadle.UpdateDrown` (owner) deals `ceil(maxHealth / 20)` per second (attacker-less `HitData`, hit type
  `Drowning`) to a **saddled** mount that swims, is not on ground and has no saddle stamina. `Sadle.UpdateStamina`
  does not regenerate while swimming, so a mount ridden until its stamina ran out and then dragged into deep water
  drowns slowly.
- Summons (`Skeleton_Friendly` from `StaffSkeleton` "Dead Raiser", `Troll_Summoned` from `StaffRedTroll`, …) have a
  `Tameable` and are tamed characters (`SpawnAbility` commands them on spawn; start-tamed flag is prefab data,
  unverified): the same filter skips them. They follow the player and fight, so they are often **between** the player
  and an enemy (with the mod they then catch the harpoon, decision 7).

### 1.8 Stats side effect of any player hit

`Character.OnDeath` runs `Game.RegisterKill` on the dying creature's **owner** for **every player in
`ZNet.GetPlayerList()` (players connected at that moment) whose attacker key (1.4) is set on the creature's ZDO**,
passing the ZDO `s_modifiers` (read with default 0 = `MixedAndTotal`) and the `s_attackers` count. The killer's game
(`Game.RPC_RegisterKill`) then calls `PlayerProfile.IncrementStatEnemy(m_name, 1, modifiers, cheated)` (per-creature
counts in `m_enemyStats[0]` and `m_enemyStats[modifier]`) and `IncrementStat(EnemyKills)` (or `BossKills`). So a player
who once hit a creature is credited with its kill if it dies later from any cause while that player is connected,
and the harpoon's `Melee` modifier leaks into that kill's breakdown. This is vanilla for any hit (PvP harpoon, butcher
knife); the mod makes a hit reachable without PvP.

The mark is written by the owner's `RPC_Damage`, so it can only be undone there: the mod does it with an owner-side
`RPC_Damage` prefix/postfix (3.2). When the thrower's game owns the tame (always in single player, usual in co-op) or
the owner runs the mod (installed and turned on), no mark survives. In the **hand-off** case (the owner does not run
the mod: not installed, or turned off) the mark stays: vanilla
rule, documented (decision 6). It cannot be prevented from the thrower's side: the attacker must be sent, or
`SE_Harpooned.SetAttacker` is never called.

The vanilla "Player Statistics" page of the Valheim Compendium (`TextsDialog.AddStats`) shows `Enemy Kills` (and the
kill-modifier headings, without per-creature numbers); tests use it.

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28.

| Mod | How | Lesson |
|---|---|---|
| [TamedHarpoon](https://thunderstore.io/c/valheim/p/Brainless_Azura/TamedHarpoon/) (Brainless_Azura) 1.0.0, 2026-09-27, AI-tagged, client-only | Hooks tames with no damage and no knockback; config `Enabled`, `NoDamage`; "only the thrower needs it". Source not published (DLL not inspected). | Confirms the client-only approach works on 1.0.16. Running both is redundant; expected harmless (unverified). |
| [HarpoonExtended](https://thunderstore.io/c/valheim/p/shudnal/HarpoonExtended/) (shudnal) 1.2.0, [source](https://github.com/shudnal/HarpoonExtended) | `HarpoonProjectilePatches.cs`: identifies the harpoon by projectile name prefix `projectile_chitinharpoon`. Its `Projectile.OnHit` prefix, when creature pulling is on (`targetCreatures` and `targetPulling`), sets the projectile's `m_statusEffectHash = 0` for **any non-player `Character`** (tames included) before `IsValidTarget` runs, attaches its own rope in a postfix when the hit happened (`m_didHit`), and restores the hash in a finalizer. Also patches `Projectile.Awake/Setup/FixedUpdate` (optional `disableDamage`). With creature pulling off it leaves characters on the vanilla path. | Identify the harpoon by the **status effect type at `IsValidTarget` time** and never zero the hash ourselves. With HE creature pulling on, our mod sees no harpoon and does nothing: with PvP off tames are not hooked (vanilla rejects them, so HE's rope is not attached either); with the thrower's **PvP on**, vanilla accepts the tame, our `Damage` prefix sees hash 0, so the tame takes the vanilla 10 pierce and push (unless HE's `disableDamage`) and gets HE's rope. Do not fight it: README note + one Info log line per session (3.3). |
| [Harpoon Melee Attack And Upgrading](https://thunderstore.io/c/valheim/p/Goldenrevolver/Harpoon_Melee_Attack_And_Upgrading/) (Goldenrevolver) | Adds a spear melee primary; the throw becomes the secondary. | Never key on the attack index. Its melee is filtered by `Attack`'s own tame filter, untouched by us. |
| [ValheimPlus](https://github.com/valheimPlus/ValheimPlus) (`GameClasses/Character.cs`) | With `[Tameable]` enabled and mortality = `Immortal`, a default-priority `Character.Damage` prefix (runs on the **attacker's** machine) replaces the hit with `new HitData()` (by `ref`) for tames it protects. | Runs before our `Priority.Last` prefix, which then sees hash 0 and no attacker: the owner never adds `SE_Harpooned`. Result: with V+ Immortal tames, tames are **not hooked** (no "harpooned" message, no pull, no damage; the projectile still sticks). Do not fight it; README note, optional test C04. Whether V+ runs on 1.0.16 is unverified. |
| [EpicLoot](https://github.com/RandyKnapp/ValheimMods/tree/main/EpicLoot) (RandyKnapp; Harmony ID `randyknapp.mods.epicloot`) | `SharedCharacterDamagePatch` on `Character.Damage`: a Normal-priority prefix and a `[HarmonyPriority(Priority.Last)]` prefix modify the outgoing hit (conversions, crits, Executioner); a postfix runs on-hit effects on the attacker's machine: life steal, `ChainLightning`, `StrikeCausesLightning`, `ApplySlow`, `Paralyze`, `StaggerOnDamageTaken`, `MeteorSummoner` and more, with no tame or zero-damage guard in the shared patch (per-effect guards not inspected). Some of them deal further damage "which re-enters this patch"; others apply effects to the target by RPC. | Backlog risk "keep the patch additive to coexist with EpicLoot projectile patches". Our prefix must run **after** EpicLoot's prefixes (`Priority.Last` + `[HarmonyAfter("randyknapp.mods.epicloot")]`) so any damage they add is zeroed, and nested hits that its postfix sends to the **same tame** during our hit must not reach it (protection scope, 3.2). Slow/paralyse applied by RPC and delayed procs (meteors, next-frame projectiles) are not covered: documented limitation, optional test C05. Our `IsValidTarget` postfix and projectile field writes are additive. |
| Jewelcrafting (Smoothbrain) | Many `Character.Damage` patches (e.g. FireStarter adds fire in a prefix, proportional to the hit's damage). (unverified in detail) | Proportional additions stay zero after ours; flat additions from a prefix that runs after ours would not be zeroed (none known). Covered by the same scope for nested hits. |
| [Pet Protection](https://thunderstore.io/c/valheim/p/zebediah49/Pet_Protection/), [Selective Pet Protection](https://thunderstore.io/c/valheim/p/MagnusMagnuson/Selective_Pet_Protection/), [BetterTames](https://thunderstore.io/c/valheim/p/xtavim/BetterTames/) | Owner-side protection from death (knock-out instead of dying). | A zero-damage hit never reaches their death handling: compatible (unverified that none of them rewrites the hit on the attacker side like V+). |
| [TameProtection](https://github.com/N3bby/TameProtection) (N3bby) | AI targeting between mobs and tames. | Unrelated hooks. |
| [CreatureCarry](https://thunderstore.io/c/valheim/p/Wendigo/CreatureCarry/) (Wendigo), [Leash](https://thunderstore.io/c/valheim/p/CookieMilk/Leash/) (CookieMilk) | Other ways to move tames (carry / lead). Not inspected further. | Alternatives, not conflicts. |

Conclusion: two additive patches on the thrower's side do the feature (let the harpoon accept tames; strip the harm
from that one hit before it is sent), plus a small owner-side clean-up of the vanilla attacker mark. The tame's owner
runs only vanilla code for the hook itself.

**Cross-mod contract with Creature Kill and Tame Counts (MC, `src/Exploration/Stats.PerCreature`):** that mod reads
the vanilla per-creature kill stats (`PlayerProfile.m_enemyStats`, 1.8). A harpooned tame that dies later is **not**
counted as the thrower's kill when the tame's owner runs this mod (installed and turned on; always in single player).
In the hand-off case (the owner does not run it) vanilla may credit the thrower, and that kill then shows in its list.
Tames you kill yourself (Butcher Knife) count once, as usual. Tested here (C02); that mod's files are not changed apart
from one line in its README compatibility section. Its tame counting (the `$hud_tamedone` message, `Tameable.Tame`) is
not touched: the hook message is `<name> $msg_harpoon_harpooned`.

---

## 3. Implemented design

### 3.1 Core idea

1. **Accept the tame.** `Projectile.IsValidTarget` postfix: when vanilla said *no*, the projectile belongs to the
   local player, carries a status effect whose type is `SE_Harpooned`, and the target is a tamed non-player character
   that is not ridden (and not dodge-invincible), return *yes*, whatever the tame's AI is doing. The rest of `OnHit` runs
   unchanged (hit effects, noise, attach, `Character.Damage`). For every harpoon hit on a tame by the local player
   (also the ones vanilla already accepts with PvP on) the postfix sets the projectile's `m_raiseSkillAmount` and
   `m_adrenaline` to 0 for non-area, stop-on-hit projectiles, so `OnHit` gives no Spears XP and no adrenaline.
2. **Strip the harm.** `Character.Damage` prefix (runs on the thrower before the RPC is sent, after every other
   mod's prefix): when the hit carries an `SE_Harpooned` hash, the attacker is the local player and the target is a
   tamed non-player, set `m_damage = default`, `m_pushForce = 0`, `m_staggerMultiplier = 0`, `m_backstabBonus = 1`,
   `m_blockable = false`, and open a **protection scope** on that tame until the call ends: any nested
   `Character.Damage` on the same tame by the local player during it (another mod's on-hit proc) is skipped. The
   owner's vanilla `RPC_Damage` then applies the hook and nothing else (table in 1.4).
3. **Clean the attacker mark.** Owner-side `Character.RPC_Damage` prefix/postfix: for a zero-damage harpoon hit on a
   tame from any player, snapshot the three ZDO keys vanilla writes (attacker key, `s_attackers`, `s_modifiers`) and
   restore them after vanilla ran. Works whenever the tame's owner runs the mod (always in single player).

Identification is by **status-effect type** (`ObjectDB.instance.GetStatusEffect(hash) is SE_Harpooned`), not by
item or prefab name: it covers the vanilla harpoon whatever attack slot throws it, modded harpoons that reuse
`SE_Harpooned`, and it switches itself off when another mod removes the hash (HarpoonExtended, ValheimPlus).

The zero-damage rule (step 2) applies to **every** harpoon hit on a tame by the local player, including hits vanilla
already allowed with PvP on (ridden tames included). The target rule (step 1) only adds targets vanilla refused.

### 3.2 Patches

All bodies catch their own exceptions (`PatchGuard.Report`). Applied only while the feature is Active (framework):
off = vanilla immediately.

| Target | Type | What it does | Why |
|---|---|---|---|
| `Projectile.IsValidTarget(IDestructible destr)` (private, one overload) | Postfix, default priority | 1. `HarpoonEffect.IsHarpoon(__instance.m_statusEffectHash)` false → `HarpoonEffect.ReportRemovedHookOnce(__instance, destr)` (3.3) and return. 2. `__instance.m_owner` is not `Player.m_localPlayer` → return. 3. `destr` is not a `Character c` with `TameRules.IsTame(c)` → return. 4. If `!__result`: `TameRules.CanHook(c, out refusal)` false → one `Log.Debug("Harpoon passed tame <name>: <refusal>.")` per projectile/tame pair (the last pair is kept by reference: `Projectile.FixedUpdate` raycasts 1.5 steps ahead, so the same tame is tested on 2-3 physics steps) and return; `__instance.m_dodgeable && c.IsDodgeInvincible()` → return; else `__result = true`. 5. (Now `__result` is true for a tame.) If `__instance.m_aoe <= 0f && !__instance.m_onlyStopOnTerrain`: `__instance.m_raiseSkillAmount = 0f; __instance.m_adrenaline = 0f;`. | Only place that decides "hit or pass through", used by both `OnHit` and `DoAOE`; a postfix keeps every vanilla rule and other mods' prefixes. `OnHit` reads the two projectile fields right after `Damage` (1.3) and a non-AOE, stop-on-hit projectile hits once, so writing them here only affects this hit (AOE or pass-through projectiles are left alone: the fields would apply to other targets too). |
| `Character.Damage(HitData hit)` (public, not virtual, one overload) | Prefix returning `bool`, `[HarmonyPriority(Priority.Last)]`, `[HarmonyAfter("randyknapp.mods.epicloot")]`, `out bool __state` | 1. `HitScope.IsNestedHit(__instance, hit)` (scope open on this very tame, attacker is the local player, hash is not a harpoon) → one `Log.Debug`, `return false` (skip the original: nothing is sent to the tame). 2. `hit == null`, `!HarpoonEffect.IsHarpoon(hit.m_statusEffectHash)` or `!hit.HaveAttacker()` → return true. 3. `hit.m_attacker != Player.m_localPlayer.GetZDOID()` → return true. 4. `!TameRules.IsTame(__instance)` → return true. 5. Zero `m_damage`, `m_pushForce`, `m_staggerMultiplier`; `m_backstabBonus = 1`; `m_blockable = false`. Keep `m_statusEffectHash`, attacker, skill, point, dir. `__state = HitScope.Open(__instance)`. One `Log.Debug` line saying whether this game simulates the tame (`__instance.m_nview.IsOwner()`). Return true. | Last place on the thrower's machine before the `HitData` is serialized to the owner; works whoever owns the tame. Last + after EpicLoot so damage added by other mods' prefixes (enchantments) is zeroed too. Skipping only nested proc hits on the protected tame is the one way to keep their damage, slow and status effects off it. |
| `Character.Damage(HitData hit)` | Finalizer, `bool __state` | `if (__state) HitScope.Close();` | Runs after every mod's postfix, even on an exception, so the scope never leaks to a later hit. A `void` finalizer does not change the exception. |
| `Character.RPC_Damage(long sender, HitData hit)` (private, one overload) | Prefix, default priority, `out AttackerMarks __state` | `__state = null`. Return unless: `hit != null`, `HarpoonEffect.IsHarpoon(hit.m_statusEffectHash)` (cheap check first), `__instance.m_nview.IsValid() && __instance.m_nview.IsOwner()`, `!__instance.IsPlayer() && __instance.IsTamed()`, `hit.GetTotalDamage() <= 0f`, `hit.GetAttacker() is Player p`. Then `__state = AttackerMarks.Take(__instance.m_nview.GetZDO(), p.GetPlayerName())`. | Only the owner writes the mark (1.4). Any player's zero-damage harpoon hit on a tame is a hook, never a real attack (a damaging PvP hit keeps its vanilla credit). |
| `Character.RPC_Damage` | Postfix, default priority, `AttackerMarks __state` | `if (__state != null && __state.Restore(__instance.m_nview.GetZDO())) Log.Debug("Cleared the attacker mark ...")`. | Runs after the SE was added and `SetAttacker` called, so the hook is untouched. A mark that existed before this hit (a real earlier hit) is kept, because only changed keys are restored. |

No transpiler, no patch on `SE_Harpooned`, `ApplyDamage` or any per-frame method. Put the two `RPC_Damage` patches in
their own `[HarmonyPatch]` class (`CharacterRpcDamagePatches`) so each class has one `__state` type.

### 3.3 Helper classes

- `HarpoonEffect` (static):
  - `IsHarpoon(int hash)`: `hash == 0` → false. `var db = ObjectDB.instance; if (db == null) return false;` (Unity
    null). If `db.m_StatusEffects` is not the cached list reference or its `Count` differs from the cached count, clear
    the cache and remember the new reference and count (covers world reloads, `ObjectDB.CopyOtherDB` and mods that
    register status effects late, with no extra patch). Then a `Dictionary<int, bool>` lookup; on a miss,
    `db.GetStatusEffect(hash) is SE_Harpooned` (a linear scan of `m_StatusEffects`), and cache **both** outcomes
    (`null` → false too: e.g. every unarmed melee hit carries the fake hash 99999, `Attack.cs`).
  - `ReportRemovedHookOnce(Projectile p, IDestructible destr)`: returns at once when already reported this session
    or `p.m_statusEffectHash != 0`. Otherwise, if `p.m_weapon != null` (plain C# object),
    `p.m_weapon.m_shared.m_attackStatusEffect is SE_Harpooned`, `p.m_owner == Player.m_localPlayer` and `destr` is a
    tame (`TameRules.IsTame`): one `Log.Info`: "A harpoon hit a tame without its hook effect. Another mod (for example
    HarpoonExtended with creature pulling) or the weapon's effect chance removed it; Harpoon Hooks Tames leaves such
    hits to vanilla." Set the flag.
  - `Clear()`: empties the cache and the list reference; resets the reported flag.
- `TameRules` (static):
  - `IsTame(Character c)`: `c != null` (Unity null), `!c.IsPlayer()`, `c.IsTamed()`.
  - `CanHook(Character c, out string refusal)`: `IsTame(c)` (refusal "it is not a tame") and not ridden (`var t =
    c.GetComponent<Tameable>(); t == null || !t.HaveRider()`, refusal "a player is riding it"). Nothing about the AI
    (no `HaveTarget()` check, decision 7). The refusals are constant strings (no allocation); `refusal` is null when
    it returns true.
- `ProjectilePatches.ClearSkipLog()`: forgets the last logged projectile/tame pair (called with the other clears in
  `OnActivated` / `OnDeactivated`).
- `HitScope` (static): one field `Character s_target`.
  - `IsNestedHit(Character target, HitData hit)`: `(object)s_target == null` → false (plain reference check first: this
    runs for every `Character.Damage`); else `ReferenceEquals(target, s_target) && hit != null && hit.HaveAttacker() &&
    !HarpoonEffect.IsHarpoon(hit.m_statusEffectHash) && Player.m_localPlayer != null && hit.m_attacker ==
    Player.m_localPlayer.GetZDOID()`. Attacker-less hits (drowning, fire, `killtame`) and other players' hits pass.
  - `Open(Character target)`: if a scope is already open return false; else set it and return true.
  - `Close()`: `s_target = null`.
- `AttackerMarks` (sealed class, allocated only for zero-damage harpoon hits on tames):
  - `Take(ZDO zdo, string playerName)`: build the attacker key exactly like vanilla, `int s_attackers =
    ZDOVars.s_attackers; string text = s_attackers + playerName;` (same expression, same culture formatting), key hash
    `text.GetStableHashCode()`. For that key, `ZDOVars.s_attackers` and `ZDOVars.s_modifiers`, record
    `had = zdo.GetInt(key, out value)`.
  - `Restore(ZDO zdo)`: for each key read `has = zdo.GetInt(key, out now)`; unchanged (`has == had` and, when both
    exist, `now == value`) → nothing; else `had` → `zdo.Set(key, value)`, not `had` → `zdo.RemoveInt(key)`. Returns
    whether anything was restored. `RemoveInt` does not bump the ZDO data revision, but a key that did not exist can
    only have been added by vanilla's `Set` in this same call, which bumped it, and nothing is sent to peers between
    the two, so peers never see the mark.

### 3.4 UI

None. Every message is the vanilla one ("Boar harpooned", "Line broke", "Boar released", "Target too far"), sent by
vanilla code. No hotkey, no gamepad handling, no input focus.

### 3.5 Configuration

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Framework: turn the feature on or off. Takes effect immediately, no restart needed. |
| General | Status | — | Framework, read-only. |

No other setting: the pull, break distance and stamina drain stay vanilla (they run on the tame owner's machine,
which may not have the mod, so a client-side setting could not change them reliably). Decisions 5 and 7 have no
setting (to add only if the user asks).

### 3.6 Persistence

Nothing is stored by the mod: no item data, no ZDO key of its own, no file. Lasting traces of a hook:

- Tame owner runs the mod (always in single player): none on the tame. The owner's own `EnemyHits` stat +1 when the
  owner is also the thrower (vanilla, 1.4; not undone: `PlayerProfile.IncrementStat` also feeds achievement events,
  so a negative increment is not safe). A harpoon hook on a wild creature counts the same way.
- Tame owner not running the mod (hand-off): the vanilla attacker key, the `s_attackers` count and the `Melee` modifier on
  the tame's ZDO, never cleared by vanilla (the modifier only on a heal to full). The thrower is credited with the
  tame's kill if it dies while the thrower is connected (1.8).

### 3.7 Multiplayer and hand-off

| Who runs the mod | Result |
|---|---|
| Thrower runs it, tame simulated by the thrower | Works; no attacker mark left. |
| Thrower runs it, tame simulated by a friend **running** the mod (installed and turned on) | Works; the friend's owner-side patch clears the mark. |
| Thrower runs it, tame simulated by a friend **without** the mod, or with it installed but turned off (`Enabled = false` or an error state: the framework unpatches everything) (hand-off) | Works: the friend's vanilla `RPC_Damage` receives a zero-damage hit with the harpoon hash, hooks, pulls and drains the thrower's stamina by RPC. The attacker mark stays (kill credit, 3.6). |
| Thrower does not run the mod, whoever simulates the tame | Vanilla: passes through (thrower's PvP off) or hooks and damages (PvP on; this mod on the owner's side does not zero someone else's hit); a damaging hit keeps its mark. |
| Friend riding a saddled tame, your PvP off | Not hooked: the harpoon passes through, as in vanilla (`TameRules.CanHook`). |
| Friend riding a saddled tame, your PvP on | Vanilla accepts it; hooked **without damage**; `SE_Harpooned` runs on the rider's machine (the rider owns the mount) and pulls mount and rider (vanilla PvP also hooks it, and damages it). Decision 2. |
| Another player as the target | Unchanged vanilla (PvP rules). |
| Ownership of the tame moves mid-pull (owner walks away) | The SE instance stays on the old owner and is lost: the line ends. Vanilla, same for wild creatures. |

An owner keeps a ZDO while it stays inside that owner's active area (`ZDOMan.ReleaseNearbyZDOS`), so a friend standing
next to a tame before the thrower arrives keeps simulating it (used in test M01).

Dedicated servers need nothing. No RPC of our own, no `NetworkGate` use, no network version.

### 3.8 Performance

- `IsValidTarget` runs per projectile collider hit, not per frame. The postfix starts with the harpoon check (int
  compare, list-reference/count compare, dictionary lookup); a hash of 0 returns after the one-time report check
  (a bool, or an int compare).
- `Character.Damage` runs per hit. The prefix first does a plain reference check on the scope field, then returns on a
  hash that is not a harpoon (most hits have hash 0) before touching the attacker or `IsTamed()` (ZDO read at most
  once a second on non-owners). The finalizer is a bool check.
- `RPC_Damage` runs per hit on the owner; the prefix returns on a non-harpoon hash first.
- No allocation on the hot paths (the dictionary grows once per distinct status-effect hash seen, a few dozen;
  `AttackerMarks` only for zero-damage harpoon hits on tames; the "Harpoon passed tame" Debug string only when a
  harpoon passes a tame, once per projectile/tame pair).

### 3.9 Files

| File | Content |
|---|---|
| `MC.Farming.Harpoon.HooksTames.csproj` | Metadata (the build's single source of truth) and `<ModIdea>Harpoon to work on tamed animals</ModIdea>`. Root namespace `MC.Farming.HarpoonHooksTamesMod`, package name `HarpoonHooksTames`. |
| `Plugin.cs` | `OnActivated` and `OnDeactivated` call `HarpoonEffect.Clear()`, `HitScope.Close()` and `ProjectilePatches.ClearSkipLog()`. No `BindConfig` (no settings of its own), no UI. |
| `HarpoonEffect.cs`, `TameRules.cs`, `HitScope.cs`, `AttackerMarks.cs` | The helpers of 3.3. |
| `Patches/ProjectilePatches.cs` | `IsValidTarget` postfix and the once-per-pair "Harpoon passed tame" Debug line. |
| `Patches/CharacterPatches.cs` | `CharacterPatches` (`Damage` prefix and finalizer) and `CharacterRpcDamagePatches` (`RPC_Damage` prefix and postfix): two classes so that each has one `__state` type. |

### 3.10 Pitfalls

- `assembly_valheim` is publicized, so `nameof(Projectile.IsValidTarget)`, `nameof(Character.RPC_Damage)`,
  `m_owner`, `m_weapon` and `m_nview` compile directly; a rename in a game update fails the build, not the game.
  Harmony binds `destr` and `hit` by name. `IsValidTarget`, `Damage` and `RPC_Damage` each have one overload.
  `RPC_Damage` is registered as a delegate in `Character.Awake`; Harmony patches the method body, so the RPC goes
  through the patch. A finalizer shares `__state` with the prefix of the same class (checked in the installed
  HarmonyX).
- Unity null: `==` on `UnityEngine.Object`, never `?.` / `??`. The scope field uses a plain `(object)` reference
  compare on purpose (hot path; it only holds a live tame for the length of one call).
- The `Damage` prefix returns `false` only for a nested hit on the protected tame; any exception path returns `true`
  (vanilla runs). It and `HitScope.IsNestedHit` both require the hit to have an attacker, so an attacker-less hit
  never passes for the local player's (for example when the player ID is empty while logging out).
- Build the attacker key with the same expression as vanilla (`int` variable + `string`): the decimal text of the
  hash, not the literal "Attackers".
- Never identify the harpoon by item name, attack slot or projectile prefab (Goldenrevolver swaps attacks,
  HarpoonExtended clears the hash on purpose).
- Never zero the hit's `m_statusEffectHash` or attacker: the owner needs both to hook (`RPC_Damage` returns early on
  `HaveAttacker() && attacker == null` and calls `SetAttacker` with the attacker). The mark is undone afterwards on the
  owner, never prevented on the thrower.
- Never change the hit in `RPC_Damage`, nor patch `SE_Harpooned` or `ApplyDamage`: the owner may not run the mod.
- Write `m_raiseSkillAmount` / `m_adrenaline` only for non-area, stop-on-hit projectiles (otherwise the fields would
  apply to other targets of the same projectile).
- `[HarmonyAfter]` with an absent Harmony ID is harmless. The patch order against EpicLoot's `Priority.Last` prefix is
  not logged (nothing to observe without EpicLoot); check it with `Harmony.GetPatchInfo` if C05 shows damage.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Tamed boar within 30 m, PvP off | Harpoon passes through | Hooked: "Boar harpooned", line, no damage number, no push, no alert |
| Walk away with a hooked tame | n/a | Dragged like a wild creature; stamina drains; "Line broke" / "released" as vanilla |
| Block or attack more than 2 s after hooking | Releases a wild creature | Releases the tame ("released") |
| Tame farther than 30 m | Passes through | "Target too far", not hooked, no damage |
| Lox (heavy) | n/a | Hooked; stamina drains much faster (drain × mass, vanilla formula); how far it can be dragged is prefab data (T03) |
| Wild creature | Hooked, 10 pierce, pushed, alerted | Identical |
| Creature being tamed (fed, not yet tame) | Hooked and damaged; alerted, so taming pauses | Identical (it is not tamed yet) |
| Tame, your PvP on | Hooked **and damaged/pushed** | Hooked, no damage, no push (decision 1) |
| Other player, PvP off / both on | Passes through / hooked and damaged | Identical |
| Tame ridden by someone, your PvP off | Passes through | Passes through (decision 2) |
| Tame ridden by someone, your PvP on | Hooked and damaged, mount and rider pulled | Hooked **without damage**, mount and rider pulled (decision 2) |
| Saddled lox nobody rides | Passes through | Hooked |
| Summoned skeleton / troll (tamed), idle or fighting | Passes through | Hooked, no damage (decisions 3, 7; T09, T13) |
| Tame or summon in the line of fire (idle or fighting), enemy behind it, your PvP off | Passes through the tame, hooks the enemy | The tame catches the harpoon: hooked, no damage; the enemy is not hooked (decision 7, T12, T13) |
| Same, your PvP on | The tame catches the harpoon: hooked and damaged | The tame catches the harpoon (vanilla target rule): hooked, no damage (decisions 1, 7) |
| Pen tame that sees a wild creature it cannot reach (deer or boar outside the fence) | Passes through | Hooked, no damage, every throw (its AI target does not matter; decision 7, T21) |
| Stray tame chasing prey, fighting, or running from a predator it targets | Passes through | Hooked, no damage; held on the line like any hooked creature (decision 7, T22) |
| Tamed humanoid blocking with a shield | n/a | Hit is unblockable: hooked, no block effects (decision 4) |
| Tame standing on a ship deck | n/a | Hooked but not pulled (vanilla skips the pull on ships) |
| You on a ship, tame on the shore | n/a | Pulled horizontally into the water, never lifted onto the deck; line breaks as the ship leaves |
| Tame dragged into lava, fire, Ashlands water | n/a | Takes that environmental damage (vanilla); no fall damage for creatures |
| Saddled mount with no saddle stamina pulled into deep water | n/a | Drowns slowly (`Sadle.UpdateDrown`, 1/20 of max health per second) while it swims (vanilla) |
| Tame told to stay, dragged 20 m, released | n/a | Walks back to its stay spot (vanilla patrol point): tell it follow, then stay at the new spot |
| Tame with a Staff of Protection bubble | n/a | Bubble hit effect plays, absorbs 0, hooked |
| Tame already staggering at the hit | n/a | Crit hit effect may play; still no damage |
| Bow, spear throw, melee, staffs, fishing rod on a tame | Pass through / no hit | Identical (no `SE_Harpooned` hash) |
| Harpooned tame later killed by a wild creature, fire, drowning (owner runs the mod) | n/a (PvP only) | No kill credit for the thrower (decision 6) |
| Same, tame simulated by a friend not running the mod (not installed, or turned off) | n/a | Thrower credited with the kill if connected (vanilla attacker rule, decision 6) |
| Harpooned tame then killed by you with the butcher knife | n/a | +1 kill, once (the knife hit marks you, vanilla) |
| Spears skill and adrenaline from the hit | Given for any harpoon hit, even 0 damage | Not given for a hit on a tame (decision 5); unchanged for other targets |
| Feature turned off while a tame is hooked | — | That line continues until it breaks or you block (vanilla SE already applied); new throws pass through |
| HarpoonExtended with creature pulling on, PvP off | HE rope on wild creatures; tames not targeted | Identical: our mod sees hash 0 and does nothing; one Info log line per session |
| HarpoonExtended with creature pulling on, your PvP on | Tame damaged + HE rope | Identical (our mod sees hash 0): the tame **is** damaged (unless HE `disableDamage`) |
| ValheimPlus with Immortal tames | Tames never damaged | Tames not hooked: no "harpooned" message, no pull (V+ empties the hit first) |
| EpicLoot-enchanted harpoon on a tame | n/a (PvP only) | Hook without damage; nested proc hits on the same tame during the hit are skipped; slow/paralyse sent by RPC and delayed procs (meteors) may still reach it (limitation) |
| TamedHarpoon installed too | Hooks tames | Same result, redundant (unverified) |
| Friend without the mod simulates the tame | n/a | Works (hand-off, 3.7) |
| Friend without the mod (or with it turned off), PvP off, throws at a tame | Passes through | Identical: only the thrower's mod matters |
| Friend without the mod (or with it turned off), PvP on, throws at a tame | Hooked and damaged | Identical: the mod only protects tames from its user's own harpoon (M06) |

---

## 5. Decisions and open questions

### Decisions

Decisions 1-6 were accepted before implementation. Decision 7 was changed by the lead after the first implementation
(2026-09-29): the first rule skipped tames whose AI has a target; every tame is now hookable.

1. **PvP on still means no damage to tames.** Vanilla already lets PvP players hook tames and damages them; the mod
   zeroes every harpoon hit on a tame by its user. Follows the requirement "without damaging" literally. Hitting a
   tame on purpose stays possible with any other weapon (PvP) or the Butcher Knife.
2. **Ridden tames:** with your PvP off they are not hooked (the harpoon passes through, as in vanilla): vanilla never
   lets one player act on another player's controls without PvP, and hooking a mount pulls its rider around. With
   your PvP on vanilla already hooks (and damages) them; the mod keeps the hook and removes the damage, like every
   tame (decision 1). The ridden check uses the vanilla `Tameable.HaveRider()`.
3. **All tamed characters count**, including summons (skeletons, trolls) and every tameable animal. There is no clean
   vanilla line between "animal" and "summon": both are tamed `Character`s with a `Tameable`. They count whatever
   their AI is doing (decision 7).
4. **The hit is made unblockable.** A blocking tamed humanoid would otherwise cancel the hook (`Humanoid.BlockAttack`
   clears the status-effect hash) and spend stamina / raise its Blocking skill on a harmless hit.
5. **No Spears XP and no adrenaline for hooking a tame.** Vanilla gives both for any harpoon hit on a character, even
   a zero-damage one, but it never lets a player without PvP hit a tame. Keeping them would add a new, zero-risk,
   unlimited Spears-XP and adrenaline farm (throw at a pen again and again; adrenaline feeds 1.0 trinkets).
   Suppressing them costs no extra patch: the `IsValidTarget` postfix sets the projectile's `m_raiseSkillAmount` and
   `m_adrenaline` to 0 for that hit. Harpoon hits on wild creatures are unchanged.
6. **Kill credit** (1.8): vanilla marks every player who hits a creature, and credits them with its kill later. The
   mod clears that mark for a harpoon hook whenever the tame's owner runs the mod, installed and turned on (always in
   single player). In the hand-off case (the owner does not run it: not installed, `Enabled = false`, or an error
   state, since the framework then unpatches everything) the mark stays and the thrower may be credited with the
   tame's kill: vanilla rule, documented in the README and tested (M05, C02). Cross-mod contract with Creature Kill
   and Tame Counts: section 2.
7. **Every tame is hookable, whatever its AI is doing** (decided by the lead, 2026-09-29). The main use case is
   pulling back a tame, very often one that is chasing prey or watching a wild creature. The first implementation
   skipped tames whose AI has a target (`BaseAI.HaveTarget()`, synced in the ZDO), so that a harpoon thrown past your
   own fighting wolf or skeleton would still hook the enemy. But "has a target" (1.6) means any wild creature the
   tame senses (deer included), whether it can reach it or not, kept until the creature dies, 30 s unsensed or 60 s
   without an attack, and picked again 5 s later: near wild creatures that rule made the hook fail most of the time.
   It is removed; only ridden tames with your PvP off are still skipped (decision 2), and no AI state is read.
   **Consequence, accepted:** vanilla stops a projectile at the first valid target (1.2), so a tame standing in the
   line of fire catches a harpoon thrown at an enemy behind it: the tame is hooked (no damage, no push) and the enemy
   is not. Vanilla already does the same with PvP on (with damage). The player releases the tame (block or attack)
   and throws again from another angle (T12, T13). A pen animal watching a wild creature outside the fence and a
   stray chasing prey are hooked every time (T21, T22). Alternatives set aside: skip tames with a target (the first
   rule); skip only summons (a wolf still catches harpoons aimed at its enemy, and summons could not be pulled back);
   `HaveTarget() && IsAlerted()` (only a partial narrowing).
8. **Identity and description.** GUID `MC.Farming.Harpoon.HooksTames` and name "Harpoon Hooks Tames" (permanent after
   release). The description does not promise dragging a lox: how far a lox can be dragged depends on prefab data
   (unverified, T03), so the README mentions it only as "to verify in game" until T03 has run.

### Added beyond the request

Nothing player-facing. Guards that serve "without damaging": no Spears XP/adrenaline (decision 5), attacker-mark
clean-up (decision 6), protection against enchantment procs (EpicLoot), one Info log line when another mod removed the
hook, and one Debug log line when a harpoon passes a ridden tame.

### Open questions

No action planned: a "stay" tame walks back after release (vanilla patrol point; fixing it needs the tame's owner to
run a mod); restricting the hook to tames inside your ward.

### Unverified names and values

- `Skeleton_Friendly` spawning already tamed (T09); that equipping the Bronze Pendant (`TrinketBronzeStamina`) is what
  lets a player gain adrenaline (T11); whether the harpoon's attack itself gives adrenaline on the throw
  (`Attack.m_attackUseAdrenaline`, weapon data; T11 compares with an empty throw); that the Staff of Protection bubble
  reaches tames (T15).
- The weapon stats in 1.1 (workbench level 2, durability 50, 10 pierce, 20 knockback, 1x backstab, 15 stamina, throw
  on the primary attack) come from the wiki.
- The creatures' internal names shown in Debug lines (`$enemy_boar`, `$enemy_wolf`, `$enemy_lox`): the localization
  keys exist, their use as the prefabs' `m_name` is assumed.
- `SpearChitin.m_shared.m_attackStatusEffect` = `Harpooned` (inferred from the code), its chance = 1.
- `SE_Harpooned` field values (30 m, 4 m break, stamina drain, pull force) are code defaults or asset data; whether a
  lox can be dragged usefully (T03).
- The harpoon projectile's `m_aoe`, `m_hitFriendly`, `m_noDamageFriendly`, `m_onlyStopOnTerrain`, `m_adrenaline`
  (prefab data; the design works either way, but the XP/adrenaline suppression needs `m_aoe <= 0` and stop-on-hit).
- TamedHarpoon's hooks (no published source); EpicLoot per-effect guards and Harmony ordering against our prefix;
  Jewelcrafting's `Character.Damage` patches; ValheimPlus on 1.0.16; pet-protection mods working owner-side only.

---

## 6. Tests

The in-game checklist lives next to the code: `src/Farming/Harpoon.HooksTames/TESTING.md`
(run `./tools/Get-TestTodo.ps1 -Mod HooksTames`; smoke test `./tools/Test-Smoke.ps1 -Mod HooksTames`). It covers
every goal item: T01-T22 single player (T12, T13: a tame in the line of fire catches the harpoon; T21, T22: busy
tames are hooked; decision 7), M01-M06 multiplayer (M01
and M05: hand-off; M06: a friend with the mod turned off), C01-C05 compatibility (C02: the kill-credit contract with
Creature Kill and Tame Counts).
