# Combat

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> Scope: attacks (melee / ranged / projectile / AoE), the damage pipeline, blocking / parry / stagger / dodge,
> weapon & shield data, crossbow loading, skills, stealth & perception, adrenaline & trinkets, Forsaken
> (guardian) powers, status effects, monster AI, turrets (ballista), training dummies, summons, magic staffs,
> and the player's HP / stamina / eitr model. Code is cited as `Class.Method` (files live in
> `.ref/decompiled/assembly_valheim/<Class>.cs`). Numbers marked *(prefab)* are Unity-serialized values that the
> decompiled code only shows as C# defaults; verify them at runtime (e.g. dump `ObjectDB.instance` /
> `ZNetScene.instance.GetPrefab(...)`) before relying on them.

---

## Overview

### The combat loop in one picture

```
Input ─► Player.SetControls (m_attack / m_blocking / dodge / jump flags)
      ─► Player.FixedUpdate (owner only)
           ├─ UpdateActionQueue      (equip / unequip / reload "minor actions")
           ├─ PlayerAttackInput ─► UpdateWeaponLoading / UpdateAttackBowDraw / Humanoid.StartAttack
           ├─ UpdateDodge, UpdateCrouch, UpdateGuardianPower, UpdateStats (stamina/eitr/adrenaline regen, food)
           └─ UpdateStealth
Humanoid.StartAttack ─► Attack.Clone().Start ─► animator trigger (ZSyncAnimation, networked)
Animation event "Hit"/"OnAttackTrigger" ─► CharacterAnimEvent ─► Humanoid.OnAttackTrigger ─► Attack.OnAttackTrigger
      ─► DoMeleeAttack | DoAreaAttack | ProjectileAttackTriggered | DoNonAttack
      ─► builds HitData ─► IDestructible.Damage(hit)              (runs on ATTACKER's machine)
Character.Damage ─► ZNetView.InvokeRPC("RPC_Damage")             (routed to VICTIM's ZDO owner)
Character.RPC_Damage (victim owner): dodge check, PvP gate, difficulty scaling, SEMan.OnDamaged, backstab,
      stagger bonus, Humanoid.BlockAttack, pushback, status effect, resistances, armor ─► Character.ApplyDamage
      ─► health (ZDO "health"), stagger accumulation, DoT status effects (fire/spirit/poison/frost/lightning)
```

### Authority rules that matter for every combat mod

| Thing | Who simulates / decides | How others learn about it |
|---|---|---|
| Player movement, attacks, blocking, dodge, stamina, eitr, adrenaline, food, skills, status effects on that player | The player's own client (it owns its Player ZDO) | ZDO floats/bools (`stamina`, `eitr`, `adrenaline`, `IsBlocking`, `dodgeinv`, `WeaponLoaded`, `Stealth`, `noise`, `health`, `max_health`, `crowned`), `ZSyncAnimation` triggers, `VisEquipment` ZDO item hashes |
| Outgoing damage numbers (weapon, skill roll, SE modifiers) | Attacker's client (`Attack.*`, `Projectile.OnHit`, `Aoe.OnHit`) | Serialized `HitData` in `RPC_Damage` |
| Incoming damage resolution (block, armor, resistances, backstab, stagger) | Victim's ZDO owner (`Character.RPC_Damage`) | Health written to ZDO |
| Monster AI, monster attacks, monster status effects | The monster's ZDO owner = usually the nearest client (`ZDOMan.ReleaseNearbyZDOS` hands ownership to a peer whose active area contains the object) | ZDO (`alert`, `haveTarget`, `huntplayer`, `sleeping`, ...) + transform/animation sync |
| Turrets, projectiles, AoEs, summons | ZDO owner of that object (projectile owner = the client that spawned it) | ZDO |
| Status effects | Only exist on the owner of the character (`SEMan` list is NOT replicated; only the attribute bitmask `seAttrib` is) | Visual start effects are networked prefabs; `SEMan.AddStatusEffect(int hash, ...)` on a non-owner sends `RPC_AddStatusEffect` |

Consequences:
- A dedicated server practically never runs combat logic. "Server-only" combat mods are almost impossible; AI / turret / summon changes must be installed by **every client** (whoever owns the monster runs the AI).
- Player-side changes (reload, stamina, block, stealth factor, food HP) can be **client-only** because the player owns their ZDO and syncs results through it. Balance changes should still ship with a server-enforced config (ServerSync / Jotunn synced config) to avoid "my client is stronger than yours".
- Any custom `StatusEffect` that is applied **by hash** (hit status effects, guardian powers, `RPC_AddStatusEffect`) must be registered in `ObjectDB.m_StatusEffects` on the receiving machine (`SEMan.Internal_AddStatusEffect` looks it up there). Custom SEs applied by reference (equip SE, local `AddStatusEffect(StatusEffect)`) only need to exist locally.
- Remote players have an empty `SEMan` and no `Inventory` on your machine: AI code that inspects `player.GetSEMan()` or `player.GetInventory()` only works when the AI owner is that player (vanilla `MonsterAI.PheromoneFleeCheck` has exactly this limitation). Use ZDO-synced data instead.

### Tick cadence
- `Player.FixedUpdate` (Unity FixedUpdate) drives the player; `Humanoid.CustomFixedUpdate` / `Character.CustomFixedUpdate` run from `MonoUpdaters.FixedUpdate` (owner-only branches inside).
- AI: `MonoUpdaters.FixedUpdate` calls `IUpdateAI.UpdateAI(0.05f)` for every `BaseAI.Instances` at **20 Hz** (accumulator `m_updateAITimer`).
- `SEMan.Update` runs inside `Character.CustomFixedUpdate` on the owner.

---

## 1. Attacks (melee, area, projectile, chains, secondary)

### Key classes
- `Attack` – `[Serializable]` plain class stored on `ItemDrop.ItemData.SharedData.m_attack` / `m_secondaryAttack`. **Cloned per swing** (`Humanoid.StartAttack` calls `Clone()`), so per-swing state (chain level, burst counters, attach) lives on the clone while tuning lives on the shared template.
- `Humanoid` – owns `m_currentAttack`, `m_previousAttack`, equipment slots, block state.
- `Player` – input queueing (`m_queuedAttackTimer` 0.5 s buffer), bow draw, reload, chains (`HaveQueuedChain`).
- `CharacterAnimEvent` – animation events: `Hit`/`OnAttackTrigger` (fire the attack), `Chain` (open chain window), `TrailOn`, `DodgeMortal` (end dodge i-frames), `GPower` (activate guardian power), `Speed` (animator speed), `FreezeFrame` (hit-stop).
- `IProjectile` implementations spawned by attacks: `Projectile`, `Aoe`, `SpawnAbility` (summons), `TriggerSpawnAbility`, etc.
- `IDestructible` (`Character`, `WearNTear`, `Destructible`, `TreeBase`, `MineRock5`, ...) receives `Damage(HitData)`.

### Flow
1. `Player.PlayerAttackInput` (owner, FixedUpdate): calls `UpdateWeaponLoading`; bows go through `UpdateAttackBowDraw`; everything else buffers input 0.5 s and calls `Humanoid.StartAttack(null, secondary)`.
2. `Humanoid.StartAttack` gates: not (in attack without queued chain), not `InDodge`, `CanMove`, not knocked back / staggering / `InMinorAction`; picks primary/secondary `Attack`, clones it, calls `Attack.Start`. On success `ClearActionQueue()` (cancels queued reload/equip!).
3. `Attack.Start` – rejects if `m_requiresReload && (!IsWeaponLoaded() || InMinorAction())`, `m_cantUseInDungeon`, insufficient stamina (`GetAttackStamina`), eitr (`TryUseEitr`), ammo (`HaveAmmo`); equips ammo; chooses animation trigger (chain: `m_attackAnimation + level`; random: `+ Random(m_attackRandomAnimations)`); rotates player toward look/move dir.
4. `Attack.Update` (from `Humanoid.UpdateAttack`, owner): on first frame "in attack" deducts stamina/eitr/health (unless per-burst), plays start effects, `AddNoise(m_attackStartNoise)`, advances chain level; aborts on stagger; handles projectile bursts; `Stop()` when the animation leaves the `attack` tag.
5. `Attack.OnAttackTrigger` (animation event): `UseAmmo` (ammo is consumed **here**, not at reload), adds `m_attackUseAdrenaline` (it *adds*, despite the name), dispatches by `m_attackType`:
   - `Horizontal`/`Vertical` → `DoMeleeAttack`: fan of ray/sphere casts every 4° across `m_attackAngle`, range `m_attackRange`, width `m_attackRayWidth` (+ `m_attackRayWidthCharExtra` at `m_attackHeightChar1/2`), friend/foe filter (`BaseAI.IsEnemy`, PvP, `m_tamedOnly`), dodge i-frame check (`IsDodgeInvincible` → `Player.HitWhileDodging`), builds one `HitData` per object, damage reduced when hitting several (`/(count*0.75)` if `m_lowerDamagePerHit`), **last chain hit ×2 damage, ×1.2 push**, `SEMan.ModifyAttack`, `Damage()`, health/eitr return, adrenaline per character hit, durability, hit-stop `FreezeFrame(0.15)`, skill raise (×1.5 if a character was hit), attach (`m_attach`, atgeir-style), harvest, snow shovel (Deep North).
   - `Area` → `DoAreaAttack`: overlap spheres at the origin; last-chain uses `m_lastChainDamageMultiplier`.
   - `Projectile` → `ProjectileAttackTriggered` → `FireProjectileBurst` (possibly repeated `m_projectileBursts` times every `m_burstInterval`; `m_perBurstResourceUsage` charges per burst; `m_destroyPreviousProjectile`).
   - `None` → `DoNonAttack`: applies `m_consumeStatusEffect` to self (buff staves, etc.).
   - Afterwards: `m_toggleFlying`, recoil, self damage, `m_consumeItem` (throwables), and **`if (m_requiresReload) ResetLoadedWeapon()`**.
6. Projectiles: `FireProjectileBurst` computes accuracy / velocity (bow: `Lerp(min,max, sqrt(draw%))`, damage × draw%, `m_drawVelocityCurve`; non-bow `m_skillAccuracy` lerps by skill), adds ammo damage/force/status effect, calls `IProjectile.Setup(owner, vel, hitNoise, hitData, weapon, ammo)`.

### Data model (tuning fields on `Attack`)
| Group | Fields |
|---|---|
| Animation | `m_attackAnimation`, `m_chargeAnimationBool` (AI charge-up), `m_attackRandomAnimations`, `m_attackChainLevels`, `m_loopingAttack` (released button → `Abort`), `m_speedFactor`, `m_speedFactorRotation` (movement/rotation multipliers during attack) |
| Costs | `m_attackStamina` (20), `m_attackEitr`, `m_attackHealth`, `m_attackHealthPercentage`, `m_attackHealthLowBlockUse`, `m_staminaReturnPerMissingHP` |
| Damage | `m_damageMultiplier`, `m_damageMultiplierPerMissingHP`, `m_damageMultiplierByTotalHealthMissing`, `m_forceMultiplier`, `m_staggerMultiplier`, `m_lastChainDamageMultiplier` (2, area only), `m_attackHealthReturnHit`, `m_attackEitrAdd` |
| Geometry | `m_attackRange` (1.5), `m_attackHeight`, `m_attackOffset`, `m_attackAngle` (90), `m_attackRayWidth`, `m_maxYAngle`, `m_hitPointtype`, `m_hitThroughWalls`, `m_multiHit`, `m_hitTerrain`, `m_hitFriendly`, `m_attackOriginJoint` |
| Loading / draw | `m_requiresReload`, `m_reloadAnimation`, `m_reloadTime` (2), `m_blockReloadTime`, `m_reloadStaminaDrain`, `m_reloadEitrDrain`, `m_bowDraw`, `m_drawDurationMin`, `m_drawStaminaDrain`, `m_drawEitrDrain`, `m_drawAnimationState`, `m_drawVelocityCurve` |
| Projectile | `m_attackProjectile`, `m_projectileVel(Min)`, `m_projectileAccuracy(Min)`, `m_projectiles`, `m_projectileBursts`, `m_burstInterval`, `m_launchAngle`, `m_useCharacterFacing(YAim)`, `m_circularProjectileLaunch` |
| Skill | `m_raiseSkillAmount`, `m_skillHitType`, `m_specialHitSkill`/`m_specialHitType` (e.g. axes raising woodcutting) |
| Adrenaline | `m_attackAdrenaline` (1, per enemy hit × `Character.m_enemyAdrenalineMultiplier`), `m_attackUseAdrenaline` (added on trigger) |
| Noise | `m_attackStartNoise` (10), `m_attackHitNoise` (30) |
| Spawns | `m_spawnOnTrigger`, `m_spawnOnHit` + `m_spawnOnHitChance`, `SharedData.m_spawnOnHit`, `m_spawnOnHitTerrain` |

Formulas:
- Stamina: `m_attackStamina × (1 + equip attack-stamina mod)` → `SEMan.ModifyAttackStaminaUsage` → `× (1 − 0.33·skill)` → minus missing-HP refund (`Attack.GetAttackStamina`). Eitr / health costs use the same `1 − 0.33·skill` reduction (`GetAttackEitr`, `GetAttackHealth`).
- Damage per hit: `ItemData.GetDamage(quality, worldLevel)` (+ ammo) × random skill factor (`Skills.GetRandomSkillFactor`: centre `Lerp(0.4, 1, skill)` ± 0.15) × `m_damageMultiplier` × creature level factor `1 + 0.5·(level−1)` × missing-HP multipliers (`Attack.ModifyDamage`) × chain bonus × `SEMan.ModifyAttack`.
- Chains: `Attack.Start` continues the chain if the previous attack used the same animation and `timeSinceLastAttack ≤ 0.2 s`; `Player.HaveQueuedChain` lets a buffered click start the next swing once `CharacterAnimEvent.CanChain()` (animation event `Chain`).

### Multiplayer authority
Everything above runs on the attacker's client (`Humanoid.CustomFixedUpdate` only updates attacks when `m_nview.IsOwner()`; `Humanoid.OnAttackTrigger` checks ownership). Animation triggers replicate through `ZSyncAnimation`; a trigger name that does not exist in the Animator silently does nothing — custom animations therefore need the same Animator changes on every client.

### Patch points
| Method | Why |
|---|---|
| `Humanoid.StartAttack` (prefix) | Swap which `Attack` is used (context moves: after roll/parry/jump), relax gating (attack from dodge). |
| `Attack.Start` (prefix/postfix) | Resource checks, animation selection, chain logic. |
| `Attack.OnAttackTrigger` (prefix/postfix) | The single moment an attack "fires"; ammo & reload reset happen here. |
| `Attack.DoMeleeAttack` / `DoAreaAttack` / `FireProjectileBurst` (transpiler or prefix-replace) | Hit detection geometry, per-hit HitData. Long methods; prefer postfix + `SEMan.ModifyAttack`-style hooks when possible. |
| `Attack.ModifyDamage` (private, postfix) | Clean place to add damage multipliers for all attack types. |
| `SEMan.ModifyAttack` / custom `StatusEffect.ModifyAttack` | No-patch way to modify outgoing damage (add an SE). |
| `Humanoid.GetAttackSpeedFactorMovement/Rotation` (protected override) | Movement while attacking. |
| `CharacterAnimEvent.Speed` / `Animator.speed` | Attack speed (vanilla has no attack-speed stat; `CharacterAnimEvent.CustomFixedUpdate` resets speed to 1 when not attacking). |

---

## 2. Damage pipeline & `HitData`

### Key classes
`HitData` (serializable payload, `Serialize`/`Deserialize` with a flags bitmask for compact defaults), `HitData.DamageTypes` (struct: `m_damage, m_blunt, m_slash, m_pierce, m_chop, m_pickaxe, m_fire, m_frost, m_lightning, m_poison, m_spirit, m_nonPlayer`), `HitData.DamageModifiers` (per-type `DamageModifier`: Normal, Resistant, Weak, Immune, Ignore, VeryResistant, VeryWeak, SlightlyResistant, SlightlyWeak), `HitData.HitType` (EnemyHit, PlayerHit, Fall, Turret, Catapult, AshlandsLava, ...), `Character`, `WeakSpot`.

Important `HitData` fields: `m_damage`, `m_dodgeable`, `m_blockable`, `m_ranged`, `m_ignorePVP`, `m_toolTier`, `m_pushForce`, `m_backstabBonus`, `m_staggerMultiplier`, `m_point`, `m_dir`, `m_statusEffectHash`, `m_attacker` (ZDOID), `m_skill`, `m_skillRaiseAmount`, `m_skillLevel`, `m_itemLevel`, `m_itemWorldLevel`, `m_hitType`, `m_healthReturn`, `m_eitrAdd`, `m_radius`, `m_weakSpot`, `m_variant`, `m_hitCollider` (local only).

### Flow (`Character.RPC_Damage`, victim owner)
1. `m_staggerMultiplier ≥ 100` → forced `Stagger`.
2. Return if dead/teleporting/cutscene or `m_dodgeable && IsDodgeInvincible()`.
3. `m_eitrAdd`; PvP gate (player vs player needs `IsPVPEnabled` unless `m_ignorePVP`).
4. Non-player attacker: `Game.GetDifficultyDamageScalePlayer` (nearby player count scaling, `m_difficultyScaleRange` 100 m, max 5 players) and `Game.m_enemyDamageRate`.
5. Attacker bookkeeping in ZDO (`Attackers` count + per-player flags → kill credit in `OnDeath`), `KillModifiers` (melee/ranged/magic/unarmed for achievements).
6. `SEMan.OnDamaged` (e.g. `SE_Shield` absorbs by zeroing the hit, `SE_React` reflects).
7. Aggravate Dvergr-style mobs (`BaseAI.AggravateAllInArea` 20 m).
8. **Backstab**: `!m_baseAI.IsAlerted() && m_backstabBonus > 1 && Time.time − m_backstabTime > 300` (and with PassiveMobs, only if it cannot see the attacker) → damage × `m_backstabBonus`, backstab effect. Cooldown is per victim, local to the victim owner.
9. **Staggered non-players take ×2**.
10. `if (hit.m_blockable && IsBlocking()) BlockAttack(hit, attacker)`; else players lose `m_nonBlockDamageAdrenaline` (−5).
11. `ApplyPushback(hit)`; apply `m_statusEffectHash` unless immune to that element (`AddStatusEffect(hash, false, itemLevel, skillLevel, variant)` or refresh existing).
12. Resistances (`GetDamageModifiers(weakSpot)` = creature/weakspot mods + `ApplyArmorDamageMods` (player armor pieces) + `SEMan.ApplyDamageMods`), armor (players: `GetBodyArmor`; enemies in NG+: `worldLevel × m_worldLevelEnemyBaseAC`), armor durability.
13. Strip poison/fire/spirit into DoT, `ApplyDamage`, then `AddFireDamage`/`AddSpiritDamage` (`SE_Burning`), `AddPoisonDamage` (`SE_Poison`), `AddFrostDamage` (`SE_Frost` slow), `AddLightningDamage`.

`Character.ApplyDamage`: enemy difficulty scale + `Game.m_playerDamageRate` (on non-players) or `m_localDamgeTakenRate` (players), writes health to ZDO, `AddStaggerDamage(totalStagger × m_staggerMultiplier)`, camera shake/hit effects if > 10% max HP, `OnDamaged`, `m_onDamaged` callback (BaseAI subscribes), drops `Piece` resources if a Character is also a Piece (training dummy).

Armor formula (`HitData.DamageTypes.ApplyArmor`): if `ac < dmg/2` → `dmg − ac`, else `dmg × clamp01(dmg / (4·ac))`. Block power uses the same formula.

### Data & persistence
`HitData` is transient (serialized only inside the RPC; `m_hitCollider` is converted to `m_weakSpot` index in `Character.Damage`). Health lives in ZDO `health` (the key is removed in `Character.Awake` when at max) and `max_health`; kill credit in ZDO `Attackers` (count) plus one bool key per attacking player name; kill style in `Modifiers`; cheat flag `cheated`. Stagger accumulation and backstab cooldown are plain fields on the owner (lost on ownership change).

### Multiplayer authority
`Character.Damage` → `ZNetView.InvokeRPC("RPC_Damage", hit)` (no target = ZDO owner). Everything after that runs on the victim owner. Heals use `RPC_Heal`, stagger `RPC_Stagger`, remote adrenaline `RPC_AddAdrenaline`, hit-stop `RPC_FreezeFrame` (to everybody).

### Patch points
- `Character.Damage` (public, **runs on the attacker's machine** before the RPC) – attacker-side hooks (e.g. "sneak attack XP"), sees every melee/projectile/AoE hit.
- `Character.RPC_Damage` (private, victim owner) – the authoritative resolution; prefix to alter `hit` (e.g. force `m_blockable`), postfix to react.
- `Character.ApplyDamage` – final numbers, also used by fall/drown/lava.
- `Character.m_onDamaged` delegate – no-patch subscription (per instance).
- `Character.GetDamageModifiers` / `SEMan.ApplyDamageMods` – resistances.

---

## 3. Blocking, parry, stagger, dodge, knockback, stamina/eitr

### Blocking & parry (`Humanoid`)
- State: `Character.m_blocking` (input) → `Humanoid.IsBlocking()` = blocking && not attacking/dodging/placing/encumbered/minor action/staggering; remote clients read ZDO `IsBlocking`. `Humanoid.UpdateBlock` (owner) writes the ZDO bool, animator `blocking`, and runs `m_blockTimer` (−1 when not blocking, counts up from 0 while blocking).
- Blocker = left item if any, else the current weapon (`Humanoid.GetCurrentBlocker`, private).
- `Humanoid.BlockAttack(hit, attacker)` (victim owner):
  - Only frontal: rejects if `Dot(hit.m_dir, forward) > 0` (hit coming from behind).
  - **Parry** = `m_timedBlockBonus > 1 && m_blockTimer < 0.25` (`m_perfectBlockInterval`). Block power = `GetBlockPower(skill) = base(quality) × (1 + 0.5·blockingSkill)`, × `m_timedBlockBonus` on parry (`SEMan.ModifyTimedBlockBonus`).
  - Shield `m_damageModifiers` applied to the hit first; blocked amount = blockable damage minus armor-formula result with block power as AC.
  - Stamina: normal `m_blockStaminaDrain` (25) × `clamp01(blocked/blockPower)`; parry uses `m_perfectBlockStaminaDrain`; both × (1 + equip block-stamina mod) → `SEMan.ModifyBlockStaminaUsage` (negative ⇒ stamina gain). Parry with `m_perfectBlockStaminaRegen > 0` refunds stamina instead.
  - Residual stagger damage goes to `AddStaggerDamage`; the block only "holds" if the player still has stamina and was not staggered (`flag3`); then status effect is cancelled and `hit.BlockDamage(blocked)`.
  - Durability, `RaiseSkill(Blocking, parry ? 2 : 1)`, block-charge mechanic (`m_buildBlockCharges`, `m_maxBlockCharges`, fires the shield's own `m_attack` via `StartWithoutAnimation`).
  - Adrenaline: `m_blockAdrenaline` (2) on normal block, `m_perfectBlockAdrenaline` (5) on successful parry; parry staggers the attacker if `attacker.m_staggerWhenBlocked`, applies `m_perfectBlockStatusEffect`.
  - Holding block: `hit.m_pushForce *= fraction`; melee attackers get a deflection push (`GetDeflectionForce`).
- Blocking also: no running (`Humanoid.CheckRun`), stamina & eitr regen × 0.8 (`Player.UpdateStats`).
- Only `hit.m_blockable` hits reach `BlockAttack`. Note `Aoe.m_blockable` defaults to `false`, so many AoEs are unblockable unless the prefab sets it.

### Stagger
- `Character.AddStaggerDamage`: accumulates into `m_staggerDamage`; threshold `GetMaxHealth() × m_staggerDamageFactor` *(prefab)*; on reaching it → `Stagger(dir)` (RPC to owner, animator trigger `stagger`), and if the hit's attacker is a player, that player gains `m_staggerEnemyAdrenaline` (5) × enemy multiplier (via `RPC_AddAdrenaline` if remote). `SEMan.ModifyStagger` can scale.
- Decay: threshold/5 per second (`UpdateStagger`). `IsStaggering()` = animator tag `stagger`. Staggered monsters take ×2 damage; staggering aborts attacks.

### Dodge / roll (`Player`)
- Triggered from `Player.SetControls`: jump while blocking (dodges backwards if no move input), or jump while crouched, or the dedicated dodge button → `Player.Dodge` queues `m_queuedDodgeTimer = 0.5` and raises Dodge skill 0.1.
- `Player.UpdateDodge`: requires on ground, not attacking/encumbered/dodging/staggering, stamina `GetDodgeStaminaUse()` = `(10 − 10·moveMod + 10·dodgeMod)` → SE → × `Lerp(1, 0.5, dodgeSkill)`; sets `m_dodgeInvincible`, trigger `dodge`, noise 5. I-frames end on anim event `DodgeMortal`. `IsDodgeInvincible` is synced via ZDO `dodginv`.
- Perfect dodge: an attacker that hits dodging i-frames calls `Player.HitWhileDodging` → `RPC_HitWhileDodging` on the dodger's owner → stamina refund `× m_perfectDodgeStaminaReturnMultiplier`, `m_perfectDodgeAdrenaline` (10), Dodge skill +1 (once per dodge).
- `Player.m_dodgeAdrenaline` (10) is declared but **unused** in 1.0.16.

### Jump, run, knockback
- `Character.Jump`: force `m_jumpForce × (1 + 0.4·jumpSkill)`, raises Jump skill, tired jumps ×`m_jumpForceTiredFactor`; `Player.OnJump` stamina `10·(1 − moveMod + jumpMod)` → SE → × `Game.m_moveStaminaRate`. `ForceJump` adds noise 30. Grappling (Deep North `GrapplingPoint`) hooks into `Jump` when airborne.
- Run: `Player.CheckRun` drain `m_runStaminaDrain (10) × Lerp(1, 0.5, runSkill)` (± move/run mods, SE); speed `m_runSpeed × (1 + 0.25·runSkill) × (1 + 1.5·moveMod)`; jog `m_speed × (1 + moveMod)` (`GetRunSpeedFactor`/`GetJogSpeedFactor`).
- Knockback: `Character.ApplyPushback(hit)` uses `m_pushForce`; `IsKnockedBack()` blocks attacks/jumps.

### Stamina / eitr regeneration (`Player.UpdateStats(float)`)
- Stamina: after `m_staminaRegenDelay` (1 s) since last use, `(m_staminaRegen + (1 − s/max)·m_staminaRegen·m_staminaRegenTimeMultiplier) × SE multiplier × Game.m_staminaRegenRate`; ×0.8 while blocking; 0 while swimming, attacking, dodging, wall-running or encumbered.
- Eitr: after `m_eitrRegenDelay` (1 s), `(m_eiterRegen + (1 − e/max)·m_eiterRegen) × (SE mult + equip eitr regen mod)`; 0 while attacking/dodging.
- `Player.UseStamina/UseEitr` multiply by `Game.m_staminaRate/m_eitrRate`; remote calls go through RPC `UseStamina` (note: `UseEitr` RPC is invoked but never registered – remote eitr drain is ignored).

### Data & persistence / multiplayer authority
ZDO: `IsBlocking`, `dodgeinv`, `stamina`, `eitr`, `adrenaline` (all written by the player's owner; stamina/eitr also saved in the profile). Block timer, block charges, stagger damage, queued dodge are owner-local fields. Blocking is resolved where the damage is resolved (victim owner = the blocking player's own client), so block/parry/dodge mods are naturally client-side. RPCs: `RPC_Stagger`, `RPC_HitWhileDodging`, `UseStamina`, `RPC_AddAdrenaline`.

### Patch points
`Humanoid.BlockAttack` (protected override; the whole block rule), `Humanoid.IsBlocking`, `Humanoid.UpdateBlock` (private; block start/stop edge), `Player.UpdateDodge` / `Player.Dodge` (private), `Player.RPC_HitWhileDodging` (perfect dodge), `Character.AddStaggerDamage` (protected), `Character.Stagger`, `Player.GetJogSpeedFactor` / `GetRunSpeedFactor` (protected overrides), `Player.CheckRun`, `Player.OnJump`.

---

## 4. Weapon & shield data (`ItemDrop.ItemData.SharedData`)

`SharedData` is shared by every instance of an item prefab (edit it once at `ObjectDB` load; changes are global and not saved). Per-instance state is on `ItemData`: `m_stack`, `m_durability`, `m_quality`, `m_variant`, `m_worldLevel`, `m_crafterID/Name`, **`m_customData` (Dictionary<string,string>)**, `m_equipped`, `m_lastAttackTime`, `m_lastProjectile`.

| Group | Fields |
|---|---|
| Identity / type | `m_name`, `m_itemType` (OneHandedWeapon, Bow, Shield, TwoHandedWeapon, TwoHandedWeaponLeft, Torch, Tool, Ammo, AmmoNonEquipable, Utility, **Trinket**, ...), `m_animationState` (OneHanded, TwoHandedClub, Bow, Shield, Atgeir, TwoHandedAxe, Crossbow, Knives, Staves, Greatsword, MagicItem, DualAxes, Feaster, Scythe, ...), `m_skillType`, `m_equipDuration` |
| Stat modifiers (summed over equipped items by `Player.UpdateModifiers` via reflection over `s_equipmentModifierSources`) | `m_movementModifier`, `m_homeItemsStaminaModifier`, `m_heatResistanceModifier`, `m_jumpStaminaModifier`, `m_attackStaminaModifier`, `m_blockStaminaModifier`, `m_dodgeStaminaModifier`, `m_swimStaminaModifier`, `m_sneakStaminaModifier`, `m_runStaminaModifier`, `m_maxAdrenaline` (index 10); plus `m_eitrRegenModifier` (separate sum) |
| Weapon | `m_damages`, `m_damagesPerLevel`, `m_attackForce`, `m_backstabBonus` (4), `m_dodgeable`, `m_blockable`, `m_tamedOnly`, `m_attackStatusEffect` + chance, `m_spawnOnHit(Terrain)`, `m_ammoType`, `m_attack`, `m_secondaryAttack`, `m_toolTier`, `m_hitVariant` |
| Shield / block | `m_blockPower`, `m_blockPowerPerLevel`, `m_deflectionForce(PerLevel)`, `m_timedBlockBonus` (1.5 default; ≤1 = cannot parry), `m_perfectBlockStaminaRegen`, `m_perfectBlockStatusEffect`, block charges (`m_buildBlockCharges`, `m_maxBlockCharges`, `m_blockChargeDecayTime`, `m_blockChargeBlockingDecayMult`), `m_damageModifiers` |
| Adrenaline | `m_maxAdrenaline`, `m_fullAdrenalineSE`, `m_blockAdrenaline` (2), `m_perfectBlockAdrenaline` (5) |
| Status effects | `m_equipStatusEffect` (while equipped, any slot incl. trinket), `m_setName/m_setSize/m_setStatusEffect` (sets; trinket counts toward set size but its own set SE is not added), `m_consumeStatusEffect` |
| AI usage (monsters) | `m_aiTargetType` (Enemy/FriendHurt/Friend), `m_aiAttackInterval`, `m_aiAttackRange(Min)`, `m_aiAttackMaxAngle`, `m_aiPrioritized`, `m_aiWhenFlying/Walking/Swiming`, `m_aiInDungeonOnly`, `m_aiInMistOnly`, `m_aiMin/MaxHealthPercentage` |
| Durability | `m_useDurability`, `m_maxDurability`, `m_durabilityPerLevel`, `m_useDurabilityDrain`, `m_durabilityDrain` (per second while equipped) |

Vanilla tower shields (asset names verified in the SoftRef manifest): `ShieldWoodTower`, `ShieldBoneTower`, `ShieldIronTower`, `ShieldBlackmetalTower`, `ShieldFlametalTower`, `ShieldGoldTower` (plus an unused-looking `ShieldTower`). Their block/parry/movement values are *(prefab)*.

Persistence: `ItemData.Save/Load` write `m_customData` (flag bit 128) — inventories (`Inventory.Save`), containers and ground drops (`ItemDrop.SaveToZDO` stores the whole item as a byte array under ZDO `itemData`, version byte 109) all keep custom data. Vanilla ignores unknown keys, so `m_customData` is the safe place for per-item mod state.

Patch points: `ObjectDB.Awake` / `ObjectDB.CopyOtherDB` postfix (or Jotunn `ItemManager.OnItemsRegistered`) to edit SharedData; `ItemData.GetBlockPower`, `GetDeflectionForce`, `GetDamage`, `GetWeaponLoadingTime`, `GetDrawStaminaDrain` for computed values; `ItemData.GetTooltip` (static) for UI.

---

## 5. Weapon loading (crossbows)

### Key classes
`Player` (private `m_weaponLoaded : ItemData`, reload minor action), `Humanoid` (virtual `ResetLoadedWeapon` / `IsWeaponLoaded` return nothing/false for non-players – monsters cannot use `m_requiresReload` weapons), `Attack` (`m_requiresReload`, `m_reloadTime`, `m_reloadAnimation`, `m_reloadStaminaDrain`, `m_reloadEitrDrain`, `m_blockReloadTime`), `WeaponLoadState` (MonoBehaviour on the crossbow visual toggling `m_loaded`/`m_unloaded` child objects from `Player.IsWeaponLoaded()` every frame), `Player.MinorActionData` (types Equip / Unequip / Reload).

Vanilla crossbows (asset names): `CrossbowArbalest`, `CrossbowRipper` (+ `Blood`/`Lightning`/`Nature` variants), `CrossbowGold` (+ `_BloodLightning`/`_FrostFire`).

### Flow
1. Every FixedUpdate `Player.PlayerAttackInput` → `Player.UpdateWeaponLoading(currentWeapon)`:
   - weapon null or not `m_requiresReload` → `SetWeaponLoaded(null)`;
   - else if `m_weaponLoaded != weapon`, no reload queued and `TryUseEitr(m_reloadEitrDrain)` → `QueueReloadAction()`.
2. `Player.QueueReloadAction` (skipped while `m_grappling > 0` or `m_blockReload > 0`) adds a `MinorActionData` with `m_duration = ItemData.GetWeaponLoadingTime()` = `Lerp(m_reloadTime, m_reloadTime × 0.5, crossbowSkill)`, animation `m_reloadAnimation` and `<anim>_done`, per-second stamina/eitr drain.
3. `Player.UpdateActionQueue` progresses the first action (paused while attacking; 0.3 s pause between actions); on completion → `SetWeaponLoaded(item)`.
4. `Player.SetWeaponLoaded` stores the reference and writes ZDO bool `WeaponLoaded` (`ZDOVars.s_weaponLoaded`). `Player.IsWeaponLoaded` = owner: `m_weaponLoaded != null`; remote: ZDO bool.
5. Firing: `Attack.Start` requires loaded and not in a minor action; `Attack.OnAttackTrigger` → `ResetLoadedWeapon()` after a successful trigger (no reset if ammo missing or staggered).

What unloads the crossbow today (all funnel into `Player.ResetLoadedWeapon` / `SetWeaponLoaded(null)`):
- `Humanoid.UnequipItem` for **any weapon** (explicit unequip, swapping weapons, `ToggleEquipped`, dropping it).
- `Humanoid.HideHandItems` (R "hide weapons", swimming while not grounded → `UnequipItem`). Eating only hides the right hand (`SetUseHandVisual` → `HideHandItems(onlyRightHand: true)`), and crossbows sit in the left hand (`ItemType.Bow`), so eating does not unload them.
- Note: `ClearActionQueue()` (attack start, running with stamina in `Player.CheckRun`, `Player.OnJump`, dodge) cancels an **in-progress** reload; `UpdateWeaponLoading` then re-queues it from zero.
- `Player.Load` (`UnequipAllItems` then `EquipInventoryItems`), death.
- `QueueEquipAction` / `QueueUnequipAction` call `CancelReloadAction()` (queued reload only).

`m_weaponLoaded` is only an in-memory reference – nothing is persisted.

### Patch points
- `Player.SetWeaponLoaded(ItemData)` (private) – postfix: the single "reload finished" / "unloaded" funnel.
- `Player.UpdateWeaponLoading(ItemData, float)` (private) – prefix: decide whether a reload is needed on (re)equip.
- `Player.ResetLoadedWeapon` (public override) – called both by firing and by unequip; you cannot tell them apart here without context.
- `Attack.OnAttackTrigger` – firing; `Attack.GetWeapon()` is public.
- `Humanoid.UnequipItem` – unequip context.
- `ItemData.GetWeaponLoadingTime` – reload duration.

---

## 6. Skills

- `Skills` component on the Player; `Skills.SkillType` combat values: Swords 1, Knives 2, Clubs 3, Polearms 4, Spears 5, Blocking 6, Axes 7, Bows 8, ElementalMagic 9, BloodMagic 10, Unarmed 11, Pickaxes 12, WoodCutting 13, Crossbows 14; movement: Jump 100, Sneak 101, Run 102, Swim 103, Dodge 108, Ride 110; `All` 999 (used by SEs).
- `Skills.Skill.Raise`: `accumulator += m_increseStep × factor × Game.m_skillGainRate`; level up when accumulator ≥ `floor(level+1)^1.5 × 0.5 + 0.5`; cap 100. `Skills.RaiseSkill` → message + `Player.OnSkillLevelup`. Optional total cap (`m_useSkillCap`, `m_totalSkillCap` 600, `RebalanceSkills`).
- `Player.RaiseSkill` multiplies by `SEMan.ModifyRaiseSkill` (e.g. SE_Stats `m_raiseSkill/m_raiseSkillModifier`). `Character.RaiseSkill` on a *tamed* creature forwards to the follow-target player's `Tameable.m_levelUpOwnerSkill` × `m_levelUpFactor` (how summons train Blood Magic).
- Skill level read: `Skills.GetSkillLevel` applies `SEMan.ModifySkillLevel`, floors. `GetSkillFactor = level/100`.
- Death: `Skills.OnDeath` → `LowerAllSkills(m_DeathLowerFactor (0.25) × Game.m_skillReductionRate)`.
- Combat sources of skill XP: melee hits (`Attack.DoMeleeAttack`, ×1.5 when a character was hit), area attacks, projectile hits on characters (`Projectile.OnHit`), AoE first character hit (`Aoe.OnHit`, once per AoE), blocking (1 / parry 2), dodge (0.1 per dodge, 1 per perfect dodge), sneak (1/s in stealth range else 0.1/s), run (1/s), jump, archery targets (`ArcheryTarget.OnProjectileHit` scaled by accuracy), `SE_Shield` break (`m_levelUpSkillOnBreak`).
- Persistence: `Skills.Save/Load` inside the player profile (`Player.Save`).
- Multiplayer: skills are purely local to the owning client; other machines only see derived values (`HitData.m_skillLevel` for SE scaling, ZDO `RandomSkillFactor` on summons). Client-only skill mods are safe.

Patch points: `Player.RaiseSkill` (public override; all player skill gains), `Skills.RaiseSkill`, `Skills.GetSkillFactor/GetSkillLevel`, `Skills.Skill.Raise` (nested class).

---

## 7. Sneak, stealth & perception

### Key classes
`Player` (stealth factor, crouch, sneak drain), `Character` (noise), `StealthSystem` (light level), `BaseAI` (senses), `MonsterAI` (alert logic), `EnemyHud` (eye icons: `Aware` = `BaseAI.HaveTarget()`, `Alerted` = `IsAlerted()`).

### Player side (owner, synced through ZDO)
- Crouch: `Player.SetControls` toggles `m_crouchToggled`; `UpdateCrouch` cancels on no stamina / swimming / running / blocking / flying. `IsCrouching` = animator tag `crouch`; `Character.IsSneaking` = crouching + moving + grounded.
- `Player.OnSneaking` (from `Character.UpdateWalking`): stamina `m_sneakStaminaDrain (5) × Lerp(1, 0.25, sqrt(sneakSkill))` (+ equip mod, SE); every 1 s `RaiseSkill(Sneak, BaseAI.InStealthRange(this) ? 1 : 0.1)`. `InStealthRange` = an enemy within its `m_viewRange` (or 10 m) and none of those alerted.
- `Player.UpdateStealth` (every 0.5 s target, then `MoveTowards` at 0.25/s): crouching → `Lerp(0.5 + 0.5·light, 0.2 + 0.4·light, sneakSkill)` → `SEMan.ModifyStealth` → clamp; standing → 1. Written to ZDO `Stealth`; `GetStealthFactor` reads ZDO on non-owners. `light = StealthSystem.GetLightFactor(center)` (ambient + all `Light`s incl. shadow raycasts; light list refreshed every 1 s via `FindObjectsOfType<Light>`).
- Noise: `Character.AddNoise(range)` keeps the max; decays 4 m/s; synced to ZDO `noise` every 0.5 s; `SEMan.ModifyNoise`. Sources: running 30, walking 15, crouched 0 (`UpdateWalking`), jump 30 (`ForceJump`), dodge 5, attack start `m_attackStartNoise` (10), attack hit `m_attackHitNoise` (30), projectile impact → `BaseAI.DoProjectileHitNoise(pos, m_hitNoise)` which **alerts** enemies of the shooter in range.

### Monster side (AI owner)
- `BaseAI.CanSenseTarget` = PassiveMobs gate, then `CanHearTarget` or `CanSeeTarget`.
- `CanHearTarget`: distance ≤ `m_hearRange` (interiors capped to 12 m) and distance < target's noise range.
- `CanSeeTarget` (static): distance ≤ `m_viewRange × target.GetStealthFactor()`; when **not alerted** the target must be within `m_viewAngle` of forward; line of sight from eye to target eye (centre if crouching) against the `viewblock` mask; mist blocks unless `m_mistVision`.
- `MonsterAI.UpdateTarget` (every 2 s if a player within 50 m, else 6 s): `FindEnemy` (closest sensed enemy, skips `m_aiSkipTarget`, sleeping); static targets (`StaticTarget` priority/random) when aggravated or `m_attackPlayerObjects`; notifies players via `OnTargeted` RPC (combat music). Gives up after 30 s unsensed, 60 s without attacking (unless hunting) or beyond `m_maxChaseDistance`.
- Alerting: `MonsterAI.UpdateAI` sets alerted when `canSeeTarget && dist < m_alertRange × stealthFactor`; also `OnDamaged`, near projectile hits, `BaseAI.Alert` RPC. Unalerted monsters with a target still walk (not run) toward it. Alert state synced via ZDO `alert` (non-owners refresh it in `BaseAI.UpdateAI`).
- Sleep (`MonsterAI.UpdateSleep`): `m_sleeping`, `m_wakeupRange`, `m_noiseWakeup` (`Player.GetPlayerNoiseRange`), `m_fallAsleepDistance`; synced via ZDO `sleeping` + RPCs.
- Sneak attacks = backstab in `Character.RPC_Damage` (see §2): requires `!IsAlerted()` on the victim, bonus `m_backstabBonus` (weapon: 4 default; many daggers higher *(prefab)*), 300 s cooldown per victim.

### Data & persistence / multiplayer authority
ZDO: `Stealth`, `noise` (player owner → read by AI owners), `alert`, `haveTarget`, `huntplayer`, `sleeping` (AI owner → read by everyone, e.g. `EnemyHud`). RPCs: `Alert`, `OnNearProjectileHit`, `SetAggravated` (BaseAI → owner), `OnTargeted` (MonsterAI → targeted player, music), `RPC_Wakeup`/`RPC_Sleep` (everybody). Nothing is saved except the monster ZDO values. Player-side stealth/noise changes are client-only; perception-rule changes need every client (AI owner).

### Patch points
`Player.UpdateStealth` (private; owner; synced) · `Player.GetStealthFactor` (read everywhere – patching it only on the AI owner also works) · `Player.OnSneaking` (protected override) · `Character.AddNoise`/`RPC_AddNoise` · `BaseAI.CanSeeTarget`/`CanHearTarget` (static, hot paths) · `MonsterAI.UpdateTarget` (private) · `Character.RPC_Damage` (backstab) · `Character.Damage` (attacker-side detection of unalerted targets).

---

## 8. Adrenaline & trinkets (1.0)

### Key classes / data
- `Player`: `m_adrenaline` (not saved – resets on load; synced to ZDO `adrenaline` each stats tick), `m_maxAdrenaline` (code default 100 *(prefab – verify; likely 0 so that only trinkets grant capacity)*), `m_adrenalineDegen` / `m_adrenalineDegenDelay` / `m_adrenalineGainMultiplier` (AnimationCurves over fill fraction *(prefab)*), `m_adrenalineEffects : List<StatusEffectLevel{m_rate, m_se}>` (tiered SEs – assets `AdrenalineRush`, `AdrenalineRush2..4` exist), `m_adrenalinePopEffects`, `m_lastMaxAdrenaline`, private `m_adrenalineGuardianPower` (10).
- `Character.GetMaxAdrenaline()` = `GetEquipmentMaxAdrenaline()` (sum of `SharedData.m_maxAdrenaline` of equipped items); `Player.GetMaxAdrenaline` adds `m_maxAdrenaline`.
- Trinkets: `ItemType.Trinket`, slot `Humanoid.m_trinketItem` (ZDO `TrinketItem` via `VisEquipment.SetTrinketItem`). Vanilla trinkets: `TrinketBronzeHealth/Stamina`, `TrinketIronHealth/Stamina`, `TrinketSilverDamage/Resist`, `TrinketBlackDamageHealth/Stamina`, `TrinketCarapaceEitr`, `TrinketChitinSwim`, `TrinketScaleStaminaDamage`, `TrinketBloodGoldHealth/Stamina`, `TrinketFlametalEitr/StaminaHealth` (each has a same-named SE asset, i.e. its `m_fullAdrenalineSE`). Equipping trinkets requires matching world level in NG+ (`Humanoid.EquipItem`).
- Rate knob: `Game.m_adrenalineRate` (world modifier `GlobalKeys.AdrenalineRate`).
- HUD: `Hud.UpdateAdrenaline` (bar hidden at 0; width `max/25×64`), `Hud.AdrenalineBarFlash`.

### Flow
`Player.AddAdrenaline(v)` (owner):
1. Gains (`v > 0`, max > 0): set degen delay `m_adrenalineDegenDelay.Evaluate(fill)`, × `Game.m_adrenalineRate` × `m_adrenalineGainMultiplier.Evaluate(fill)` → `SEMan.ModifyAdrenaline` (SE_Stats `m_adrenalineModifier`).
2. Add (losses always, gains only below max).
3. **At max**: for every equipped item with `m_fullAdrenalineSE` → add or refresh that SE; if any existed, adrenaline resets to 0 and plays `m_adrenalinePopEffects` ("pop"), else stays at max.
4. Choose the highest `m_adrenalineEffects` tier with `adrenaline ≥ m_rate`, swap tier SEs, flash bar.
Degeneration in `Player.UpdateStats`: after the delay timer, `m_adrenalineDegen.Evaluate(fill) × dt` is removed.

Sources (all call `AddAdrenaline` on the local owner except stagger):
| Event | Amount | Where |
|---|---|---|
| Melee hit on character | `m_attackAdrenaline (1) × target.m_enemyAdrenalineMultiplier` per character | `Attack.DoMeleeAttack` |
| Melee swing hitting no character | `Player.m_attackMissAdrenaline` (−5) | `Attack.DoMeleeAttack` |
| Area attack | `m_attackAdrenaline × max multiplier` | `Attack.DoAreaAttack` |
| Projectile damaging a character | `Projectile.m_adrenaline` (2) | `Projectile.OnHit` |
| Attack trigger | `Attack.m_attackUseAdrenaline` | `Attack.OnAttackTrigger`, per burst |
| Block / parry | `m_blockAdrenaline` (2) / `m_perfectBlockAdrenaline` (5) | `Humanoid.BlockAttack` |
| Taking unblocked damage | `m_nonBlockDamageAdrenaline` (−5) | `Character.RPC_Damage` |
| Staggering an enemy | `m_staggerEnemyAdrenaline` (5) × enemy mult (RPC if remote) | `Character.AddStaggerDamage` |
| Perfect dodge | `m_perfectDodgeAdrenaline` (10) | `Player.RPC_HitWhileDodging` |
| Guardian power activation | 10 | `Player.ActivateGuardianPower` |
| SE up-front | `SE_Stats.m_adrenalineUpFront` | `SE_Stats.StartupEffects` |

### Multiplayer authority
Entirely owned by the local player; only the float is mirrored to ZDO. Client-only mods are possible.

### Patch points
`Player.AddAdrenaline` (single funnel for gain/loss/pop/tiers), `Player.UpdateStats(float)` (private; degen), `Player.GetMaxAdrenaline`, `Humanoid.UpdateEquipmentStatusEffects` (private; trinket equip SEs), `Hud.UpdateAdrenaline` (UI), `ItemData.GetTooltip` (`$item_fulladrenaline` line).

---

## 9. Forsaken (guardian) powers

### Key classes
`Player` (`m_guardianPower` name, `m_guardianPowerHash`, `m_guardianSE`, public `m_guardianPowerCooldown`), `ItemStand` (boss stones: `m_guardianPower` SE, sets the player's power), `StatusEffect` (`m_cooldown`, `m_activationAnimation` = `gpower`), `CharacterAnimEvent.GPower`, `Hud` (`Player.GetGuardianPowerHUD`).

SE assets present: `GP_Eikthyr`, `GP_TheElder`, `GP_Bonemass`, `GP_Moder`, `GP_Yagluth`, `GP_Queen`, `GP_Fader` (Deep North one: check `ObjectDB` at runtime; `Player.StartGuardianPower` stat switch mentions `GP_Ashlands` and `GP_DeepNorth`).

### Flow
1. `ItemStand.Interact` (boss stone with `m_guardianPower`) → delayed `ItemStand.DelayedPowerActivation` → `Player.SetGuardianPower(name)` (looks up SE in ObjectDB, adds unique key).
2. Key press → `Player.StartGuardianPower`: same gating as attacks; cooldown check (`$hud_powernotready`); trigger `gpower`; stats.
3. Animation event → `Player.ActivateGuardianPower`: adds `m_guardianSE` **by hash** to every player within 10 m (`Player.GetPlayersInRange`) – remote players get it through `RPC_AddStatusEffect`; +10 adrenaline; `m_guardianPowerCooldown = SE.m_cooldown`.
4. `Player.UpdateGuardianPower` ticks the cooldown (owner, FixedUpdate).
Persistence: name + cooldown in `Player.Save/Load`.

### Patch points
`Player.StartGuardianPower` (activation rules / cost), `Player.ActivateGuardianPower` (targets, radius, extra effects), `Player.UpdateGuardianPower` (cooldown / passive upkeep), `Player.SetGuardianPower`, `ItemStand.Interact` → `Invoke("DelayedPowerActivation", m_powerActivationDelay)` → `ItemStand.DelayedPowerActivation` (private; the boss-stone selection, local player only), `ItemStand.IsGuardianPowerActive`. Custom GP SEs must be in ObjectDB on all clients.

---

## 10. Status effects

### Key classes
`StatusEffect` (ScriptableObject base; `m_name`, `m_category`, `m_icon`, `m_ttl`, `m_attributes` (ColdResistance, DoubleImpactDamage, SailingPower, TamingBoost), `m_startEffects/m_stopEffects`, guardian `m_cooldown`, and virtual hooks: `ModifyAttack`, `ModifyHealthRegen`, `ModifyStaminaRegen`, `ModifyEitrRegen`, `ModifyDamageMods`, `ModifyTimedBlockBonus`, `ModifyArmorMods`, `ModifyRaiseSkill`, `ModifySkillLevel`, `ModifySpeed`, `ModifyJump`, `ModifyWalkVelocity`, `ModifyFallDamage`, `ModifyNoise`, `ModifyStealth`, `ModifyMaxCarryWeight`, `ModifyRunStaminaDrain`, `Modify{Jump,Attack,Block,Dodge,Swim,HomeItem,Sneak}StaminaUsage`, `ModifyAdrenaline`, `ModifyStagger`, `OnDamaged`, `SetLevel(itemLevel, skillLevel)`, `CanAdd`, `IsDone`).
Subclasses: `SE_Stats` (the workhorse – see fields below), `SE_Shield` (absorb pool, breaks → skill XP), `SE_Burning` (fire & spirit DoT), `SE_Poison`, `SE_Frost` (slow scaling with `damage/maxHP`, `m_frostSlowMultipliers`), `SE_Wet`, `SE_Rested`, `SE_Cozy`, `SE_Smoke`, `SE_Puke`, `SE_Harpooned`, `SE_Demister`, `SE_Finder`, `SE_React` (reflect projectile on hit), `SE_Spawn` (spawns a prefab after a delay), `SE_Crowned` (crown mode → monsters flee), `SE_HealthUpgrade` (legacy; overwritten by food recalculation).
`SE_Stats` fields: HP per tick, health/stamina/eitr up-front & over time, `m_staminaDrainPerSec`, all stamina-use modifiers, `m_adrenalineUpFront/Modifier`, `m_staggerModifier`, `m_timedBlockBonus`, regen multipliers, `m_addArmor/m_armorMultiplier`, raise-skill & skill-level modifiers, `m_mods` (damage modifiers), `m_modifyAttackSkill`/`m_damageModifier`/`m_percentigeDamageModifiers`, `m_noiseModifier`, `m_stealthModifier`, `m_addMaxCarryWeight`, `m_speedModifier`, `m_swimSpeedModifier`, `m_jumpModifier`, fall modifiers, wind modifiers, pheromones (`m_pheromoneTarget`, `m_pheromoneFlee`, ...).
Notable assets: `Immobilized`, `ImmobilizedAshlands`, `ImmobilizedLong` (root effects), `Tared`, `Slimed`, `Frost`, `Staff_shield`, `Staff_FrostOrbs`, `AdrenalineRush*`, set effects `SetEffect_*`, potions `Potion_*`.

`SEMan` (per Character): `m_statusEffects` list + hash set; `AddStatusEffect(int hash, resetTime, itemLevel, skillLevel, variant)` (non-owner → `RPC_AddStatusEffect`), `AddStatusEffect(StatusEffect)` (clones the asset; `CanAdd` check; `Setup`, `SetLevel`), `RemoveStatusEffect`, `Update` (owner; ticks, removes done SEs, writes ZDO `seAttrib` bitmask), aggregate `Modify*`/`Apply*` fan-outs. Well-known hashes: `SEMan.s_statusEffectBurning/Frost/Lightning/Poison/Spirit/Wet/Cold/Freezing/Rested/Encumbered/...`.

Multiplayer: SEs exist only on the character owner. Hit-applied SEs travel as `HitData.m_statusEffectHash` and are added on the victim owner → SE assets must exist in ObjectDB there. Speed modifiers (`ModifySpeed`) are applied in `Character.UpdateWalking/UpdateSwimming/UpdateFlying` and to turn speed – a `m_speedModifier ≤ -1` SE is a full root (speed clamped to 0, jumping also fails because `Jump` treats `speed ≤ 0` as tired).

Patch points / extension: create SEs with `ScriptableObject.CreateInstance<T>()` and add to `ObjectDB.m_StatusEffects` in an `ObjectDB.Awake`/`CopyOtherDB` postfix (Jotunn: `ItemManager.AddStatusEffect`); subclass `StatusEffect`/`SE_Stats` for logic (the vanilla SEMan fan-out calls your overrides – no Harmony needed for most stat effects).

---

## 11. Monster AI (BaseAI, MonsterAI, AnimalAI) & bosses

### Key classes
- `BaseAI` (MonoBehaviour, `IUpdateAI`): senses, movement, pathing (`Pathfinding.instance.GetPath/HavePath`, `m_pathAgentType`), flee, fire/water/lava avoidance, flying take-off/landing, aggravation (Dvergr), alert, regeneration (`m_regenAllHPTime` from `Character`), static helpers `IsEnemy`, `FindClosestCreature`, `FindClosestEnemy`, `FindRandomEnemy`, `InStealthRange`, `AggravateAllInArea`, `DoProjectileHitNoise`.
- `MonsterAI : BaseAI`: target selection, attacking via `Humanoid.EquipBestWeapon` + `StartAttack`, circling, charge animations, intercept prediction, sleep, consume items (taming food), follow (`m_follow`), despawn-in-day, event creatures, hunt player, flee rules.
- `AnimalAI : BaseAI`: flee from any sensed enemy for `m_timeToSafe` (4 s), else idle.
- `Humanoid.EquipBestWeapon`: chooses among inventory "weapons" (monster attacks are items) by `m_aiTargetType`, range, `m_aiAttackInterval`, priority, angle check, `BaseAI.CanUseAttack` (flying/swimming/health %/mist/dungeon filters).

### Flow (`MonsterAI.UpdateAI`, owner, 20 Hz)
`BaseAI.UpdateAI` (non-owner just refreshes `alert` from ZDO and returns false) → `UpdateSleep` → `UpdateTarget` → saddle riding → `m_avoidLand` → despawn-in-day / event cleanup → **flee checks in order**: crown mode (`player.InCrownMode()` & not boss & within `m_crownFearRange`), `m_fleeIfNotAlerted` (beyond alert range), `m_fleeIfLowHealth` (HP% below threshold and hurt within `m_fleeTimeSinceHurt`), lava, fire (`AvoidFire`, `m_afraidOfFire` super-afraid), no-monster areas (`EffectArea.IsPointInsideNoMonsterArea`), `m_fleeIfHurtWhenTargetCantBeReached` (no attack for 30 s but hurt within 20 s) → consume items → circle target (`m_circleTargetInterval`) → `SelectBestAttack` (re-equip every 1 s) → charge start → circulate while charging → idle/follow when no target → attack branch: move to (predicted) last known position, stop when in range & visible & alerted, `PheromoneFleeCheck`, `LookAt`, `DoAttack` when `IsLookingAt(... m_aiAttackMaxAngle)` and ready.

Movement: `BaseAI.MoveTo` → `FindPath` (cached ≥1 s; up to 5 s if target moved <1 m) → steer to next corner (`MoveTowards`/`MoveTowardsSwoop`). **If no path is found, `MoveTo` stops and returns true ("arrived")** – the classic "monster stands still" behaviour. Only flying AI (`MoveAndAvoid`) has stuck detection (1.5 s window, <0.2 m → 4 s "get out of corner"). Protected helper `BaseAI.StandStillDuration(threshold)` exists but is not used by ground movement.

Flee (`BaseAI.Flee`): every `m_fleeInterval` (2 s) pick a point `m_fleeRange` (25 m) away within ±`m_fleeAngle` (45°) that has a path and is not water/lava; runs if alerted.

Factions (`Character.Faction`): Players, AnimalsVeg, ForestMonsters, Undead, Demon, MountainMonsters, SeaMonsters, PlainsMonsters, Boss, MistlandsMonsters, Dverger, PlayerSpawned, TrainingDummy, DeepNorth. `BaseAI.IsEnemy(a, b)`: same group → friends; tamed rules (tamed ≠ enemy of Players / other tamed / non-aggravated Dvergr); aggravated Dvergr vs Players; same faction → friends; then a per-faction table (e.g. `TrainingDummy` is only an enemy of Players; AnimalsVeg/PlayerSpawned are enemies of everyone; Boss is enemy of Players/PlayerSpawned). Hot path – called for every character in every search.

Crowd control: stagger (animator tag), `SE_Frost` slow, roots (`Immobilized*` SEs, speed → 0), `freeze` animator tag (`Character.CanMove` false), knockback (`IsKnockedBack`), pheromone flee.

### Data & persistence (ZDO)
`alert`, `haveTarget`, `huntplayer`, `aggravated`, `spawnpoint`, `patrol`/`patrolPoint`, `sleeping`, `DespawnInDay`, `EventCreature`, `spawntime`, `lastWorldTime` (regen), `tamed`, `level`, `health`, `max_health`, `RandomSkillFactor`, `follow`, `maxInstances`, `bosscount`, `ShownAlertMessage`, `Attackers`, `Modifiers`.

### Bosses
`Character.m_boss`, `m_bossOrder`, `m_bossEvent` (music/event), `m_defeatSetGlobalKey` (sets global + player unique key on death), `m_dreamCinematic`; alerting a boss increments `GlobalKeys.activeBosses` (`BaseAI.SetAlerted`), death decrements it. Summoned through `OfferingBowl` (`RPC_SpawnBoss` to the altar owner, `RPC_RemoveBossSpawnInventoryItems` back to the sender). Boss attacks are ordinary `Attack` items + `Aoe`/`SpawnAbility` prefabs.

### Patch points
`MonsterAI.UpdateAI` (public override; prefix-replace carefully – it also drives sleep/targeting), `MonsterAI.UpdateTarget` (private), `BaseAI.MoveTo` / `FindPath` (protected/private), `BaseAI.Flee` (protected), `BaseAI.IsEnemy` (static), `BaseAI.CanSenseTarget`, `Humanoid.EquipBestWeapon`, `BaseAI.CanUseAttack`, `MonsterAI.OnDamaged`, `BaseAI.SetAlerted`. Use Harmony reverse patches or a publicized assembly to call protected helpers (`Flee`, `MoveTo`, `RandomMovement`, `StandStillDuration`).

---

## 12. Turrets (ballista)

### Key classes
`Turret` (on `piece_turret`; `Hoverable`, `Interactable`, `IPieceMarker`, `IHasHoverMenu`), `Projectile` (bolts `TurretBolt`, `TurretBoltWood`, `TurretBoltBone`, `TurretBoltFlametal`, `TurretBoltBloodgold`), `BaseAI.FindClosestCreature` (targeting), `WearNTear` (destroy → ammo returned).

### Flow
- `Turret.FixedUpdate`: everyone runs `UpdateReloadState` (armed/unarmed visuals from ZDO `lastAttack`), `UpdateTurretRotation`, `UpdateVisualBolt`; non-owners re-read configured targets when ZDO data revision changes; **owner** runs `UpdateTarget` + `UpdateAttack`.
- `Turret.UpdateTarget` (owner): no ammo → clear; every `m_updateTargetIntervalNear` (1 s, if any character within 40 m) or `Far` (10 s): `BaseAI.FindClosestCreature(transform, eye, hear 0, m_viewDistance, m_horizontalAngle, alerted:false, mistVision:false, passiveAggressive:true, m_targetPlayers, tamed flag, m_targetEnemies, configured targets)` → **closest** sensed creature; change → `RPC_SetTarget` to everybody. Clears target when dead.
- `Turret.UpdateTurretRotation`: lead = target velocity × (distance / projectile speed) × `m_predictionModifier`; **no gravity compensation**; clamps to ±`m_horizontalAngle`; `m_aimDiffToTarget` = quaternion dot.
- `Turret.UpdateAttack`: shoot when target, aim dot ≥ `m_shootWhenAimDiff` (0.9), ammo, not cooling down (`lastAttack + m_attackCooldown` in ZDO).
- `Turret.ShootProjectile`: decrements ZDO `ammo`, builds `HitData` from the ammo item (`HitType.Turret`), `Projectile.Setup(owner: null, ...)`. With a null owner `Projectile.IsValidTarget` skips the friend/foe test → **bolts damage anything they hit, including players and tamed creatures in the line of fire**.
- Ammo: `UseItem`/`Interact` → `RPC_AddAmmo(prefabName)` to owner → ZDO `ammo`/`ammoType`; `m_maxAmmo`, `m_allowedAmmo` (with visuals). Trophy targeting: using a trophy listed in `m_configTargets` toggles it (max `m_maxConfigTargets`) → ZDO `targets` + `target{i}` strings; `ReadTargets` rebuilds `m_targetCharacters` (matched by `Character.m_name`).
- Target flags `m_targetPlayers` / `m_targetTamed` / `m_targetEnemies` / `m_targetTamedConfig` default to true in code *(prefab – verify ballista values)*.

### Data & persistence / multiplayer authority
ZDO: `ammo`, `ammoType`, `lastAttack` (network time, drives cooldown visuals on all clients), `targets` + `target0..n` (trophy names). `m_target` is a local field kept in sync by `RPC_SetTarget` (everybody). `RPC_AddAmmo` goes to the owner. `SetTargets` claims ownership before writing. Projectiles are spawned by the turret owner and simulated by that client.

### Patch points
`Turret.UpdateTarget` (private; target selection/assignment), `Turret.UpdateAttack` (private; fire decision, line-of-fire check), `Turret.UpdateTurretRotation` (private; aiming/ballistics), `Turret.ShootProjectile` (public), `Projectile.IsValidTarget` (private; friendly fire), `Turret.RPC_SetTarget`. Everything decision-related runs on the turret's ZDO owner (any client) → all clients need the mod.

---

## 13. Training dummies & archery targets

- Faction `Character.Faction.TrainingDummy`: `BaseAI.IsEnemy` treats it as an enemy **only of Players**; monsters see it as an enemy (they fall into the generic faction table) unless the prefab sets `Character.m_aiSkipTarget`.
- Assets (SoftRef manifest): building piece `piece_TrainingDummy` and a character prefab `Assets/Characters/TrainingDummy/TrainingDummy.prefab` with attack items `TrainingDummy_attack`, `TrainingDummy_attack2`, `TrainingDummy_attack3`, `TrainingDummy_throw` (+ `_throw_projectile`), alert/idle/stagger SFX. So the dummy is a `Humanoid` with `MonsterAI` that fights back. `Character.ApplyDamage` calls `Piece.DropResources` when a Character that is also a `Piece` dies, which is how a buildable character refunds materials. *(Verify the exact component layout of `piece_TrainingDummy` at runtime — whether the piece is itself the Character or spawns it.)*
- `ArcheryTarget` (`IHitProjectile`): scores hits by distance to `m_center` (`m_points`, `m_targetSize`), keeps last scores in ZDO (`data`, `dataCount`, `HitPoint`), raises the shooter's skill `m_raiseSkillAmount × m_raiseSkillMultiplier × accuracy`, can return ammo (`m_returnAmmo`, `RPC_DropArrows`), sticks projectiles (`Projectile.SetStayTTL`).
- DPS measurement already exists: `Character.SetDPSDebug(true)` (console) logs "DPS To-others/To-you" via `Character.AddDPS` from `ApplyDamage`.

Patch points: `BaseAI.IsEnemy` (faction rules), `MonsterAI.UpdateTarget` / `FindEnemy` (target filters), `Character.m_onDamaged` (per-dummy meters), `ArcheryTarget.OnProjectileHit`.

---

## 14. Summons

### Key classes
`SpawnAbility` (an `IProjectile` spawned by a staff attack; spawns creatures/projectiles), `Tameable` (`m_startsTamed`, `m_commandable`, `m_unsummonDistance`, `m_unsummonOnOwnerLogoutSeconds`, `m_levelUpOwnerSkill`, `m_levelUpFactor`, `m_maxSummonReached`), `MonsterAI` (follow target), `SE_Spawn`, `TriggerSpawnAbility`.
Vanilla summon prefabs: `Skeleton_Friendly` (Dead Raiser `StaffSkeleton`), `Troll_Summoned` (`StaffRedTroll`, uses `SpawnAbility.m_aoePrefab`), `Charred_Twitcher_Summoned`, roots from `StaffGreenRoots`, plus boss-summon VFX `_Summon_*`.

### Flow
1. Staff attack's projectile/spawn prefab carries `SpawnAbility`; `Attack` calls `Setup(owner, ..., weapon, ...)` → coroutine `Spawn`.
2. Count `Random(m_minToSpawn, m_maxToSpawn)`; target point by `m_targetType` (ClosestEnemy, RandomEnemy, Caster, Position, RandomPathfindablePosition).
3. Global cap: `m_maxSpawned` via `SpawnSystem.GetNrOfInstances(prefab, pos, 0)` = **all loaded instances of that prefab, regardless of owner** (other players' summons count).
4. Instantiate; `m_copySkill` → ZDO `RandomSkillFactor = 1 + skill × m_copySkillToRandomFactor` (summon damage scales with caster skill through `Character.GetRandomSkillFactor`); `m_levelUpSettings` → `SetLevel` and ZDO `maxInstances` (or weapon quality if `m_setMaxInstancesFromWeaponLevel`).
5. `m_commandOnSpawn` → `Tameable.Command(owner)` → follow (ZDO `follow` = player name) → `Tameable.UnsummonMaxInstances` keeps the newest N summons of that name following that player.
6. `Tameable.Update` (owner): `UpdateSummon` unsummons beyond `m_unsummonDistance`; `UpdateSavedFollowTarget` re-attaches after reload or unsummons after `m_unsummonOnOwnerLogoutSeconds`.
7. Tamed summons raise the owner's skill when they hit things (`Character.RaiseSkill` → `m_levelUpOwnerSkill`).

Multiplayer: spawn code runs on the caster; the summon's AI runs on its ZDO owner; `RPC_Command`, `RPC_UnSummon` (to everybody). All summon prefabs must be registered in `ZNetScene` on every client.

Patch points: `SpawnAbility.Spawn` (coroutine; prefer prefab data), `SpawnAbility.FindTarget`, `Tameable.UnsummonMaxInstances` (private), `Tameable.UpdateSummon`, `SpawnSystem.GetNrOfInstances` (careful: also used by world spawning).

---

## 15. Magic staffs (elemental & blood)

Staffs are ordinary weapons (`m_animationState = Staves`/`MagicItem`, skill ElementalMagic/BloodMagic) whose `Attack` uses: eitr costs (`m_attackEitr`, `m_drawEitrDrain`, `m_reloadEitrDrain`), health costs (`m_attackHealth`, `m_attackHealthPercentage`, `m_attackHealthLowBlockUse` – low HP only flashes the bar on start; `UseHealth` never goes below 1 HP), bursts (`m_projectileBursts`, `m_burstInterval`, `m_perBurstResourceUsage`), channelling (`m_loopingAttack`), missing-HP scaling (`m_damageMultiplierPerMissingHP`, `m_damageMultiplierByTotalHealthMissing`, `m_staminaReturnPerMissingHP`), `m_attackHealthReturnHit`, `m_attackEitrAdd`.
Payload prefabs: `Projectile` (fireball, ice shards, lightning), `Aoe` (heal/shield/nova/cluster fields: `m_hitFriendly/m_hitEnemy`, `m_statusEffect`/`IfBoss`/`IfPlayer`, `m_launchCharacters`, chain lightning `m_chainObj`...), `SpawnAbility` (summons, root spawns).
Vanilla staff assets: `StaffFireball`, `StaffClusterbomb`, `StaffIceShards`, `StaffLightning`, `StaffFrostOrbs`, `StaffNova`, `StaffOrbofAhri`, `StaffSpiritCaller`, `StaffGreenRoots`, `StaffSkeleton`, `StaffRedTroll`, `StaffShield` (SE `Staff_shield` = `SE_Shield`), **`StaffHeal`** (`StaffHeal_aoe`, `StaffHeal_heal`), **`StaffBlocker`** (barrier shapes `StaffBlocker_blockWall/Circle/CircleBig/Hemisphere/U`).
Buff/heal pattern: a zero-damage `Aoe` with `m_hitFriendly = true, m_hitEnemy = false` and `m_statusEffect` → each friendly hit goes through `Character.RPC_Damage`, which adds the SE with `hit.m_itemLevel`/`hit.m_skillLevel` → `StatusEffect.SetLevel(itemLevel, skillLevel)` scales it (e.g. `SE_Shield.m_absorbDamagePerSkillLevel`, `m_ttlPerItemLevel`).

---

## 16. Player vitals: HP, stamina, eitr

- Base values: `Player.m_baseHP` (25), `Player.m_baseStamina` (75), eitr base 0 *(prefab – verify)*; public fields.
- `Player.UpdateFood(dt, force)` (private; every 1 s × `Game.m_foodRate`, or forced on eat): each food's contribution = item value × `clamp01(time/burnTime)^0.3`; expired foods removed; `GetTotalFoodValue(out hp, out stamina, out eitr)` (private) = base + Σ foods; then `SetMaxHealth(hp)` (writes ZDO `max_health`), `SetMaxStamina`, `SetMaxEitr` (private). Every 10 s heals Σ `m_foodRegen` × `SEMan.ModifyHealthRegen`.
- Max 3 foods (`m_maxFoods`), re-eat when `m_time < burnTime/2` (`Food.CanEatAgain`); `Player.EatFood`, `CanEat`.
- Because max values are recomputed every second, **any bonus must be injected in `GetTotalFoodValue` (postfix on out params) or by changing `m_baseHP`/`m_baseStamina`** – `SetMaxHealth` from elsewhere is overwritten (why `SE_HealthUpgrade` is effectively dead).
- Persistence: `Player.Save` writes max HP, HP, max stamina, stamina, max eitr, eitr, foods (name + time), and `Player.m_customData` (string dictionary – good for per-character mod state).
- Regen/drain formulas: §3. Rested/comfort: `SE_Rested` (not combat).
- Useful per-character counters for "stats": `Game.instance.GetPlayerProfile().GetStat(PlayerStatType.X)` (e.g. `DistanceRun`, `Jumps`, `EnemyKillsLastHits`), plus skill levels.

---

## Feature ideas

Legend: **Who needs the mod** — *client-only* works if only the user installs it; *everyone* means all clients that may own the relevant objects (the server should also carry it for config sync).

### Weapon revamp (Revamp) — new base movesets, moves on roll / jump / parry, unique move per weapon type
- **Feasibility:** very-hard with new animations; medium if limited to logic + re-used vanilla animation triggers.
- **Who needs the mod:** everyone (animations/Animator changes must exist on every client; damage changes also need server-synced config for fairness).
- **Assets:** yes for genuinely new movesets (animation clips + an `AnimatorOverrideController`/runtime controller patching in an AssetBundle); no for "context moves" built from existing triggers.
- **Hooks:** `Humanoid.StartAttack` (swap `Attack` template per context), `Attack.Start` (chain/animation choice), `Attack.OnAttackTrigger` / `Attack.ModifyDamage` (bonus effects), `Player.UpdateDodge` + `CharacterAnimEvent.DodgeMortal` (roll window), `Character.Jump`/`Player.OnJump` + `Character.IsOnGround` (airborne attacks), `Humanoid.BlockAttack` (parry success = `m_timedBlockBonus > 1 && m_blockTimer < 0.25` and block held), `Humanoid.GetAttackSpeedFactorMovement`, `CharacterAnimEvent.Speed` (attack speed), `ObjectDB.Awake` postfix (edit `SharedData.m_attack/m_secondaryAttack` per `m_skillType`/`m_animationState`).
- **Sketch:** Build a "move table" keyed by weapon skill/animation state with context entries (Riposte after parry, RollStrike within N s of a dodge end, Plunge while airborne). A `Humanoid.StartAttack` prefix checks the context timers (set in `BlockAttack` postfix / `UpdateDodge` / `OnJump`), clones the chosen `Attack` (vanilla trigger of another weapon family or a custom one), and lets the normal pipeline run; per-move damage/stagger/adrenaline via `Attack` fields.
- **Risks:** animation-trigger names must exist in the player Animator (missing triggers silently do nothing); new clips need an Animator pipeline that other animation mods (e.g. attack-speed or emote mods) also touch; `ClearActionQueue` on attack start interacts with reload/equip queues; EpicLoot and weapon-pack mods (Therzie/Warfare-style) patch `Attack`/`Humanoid` heavily — keep patches as prefixes that only swap data; balance and PvP fairness.

### Sneak revamp (Revamp) — XP on sneak attack, harder to spot early
- **Feasibility:** easy.
- **Who needs the mod:** client-only for the XP and for the stealth-factor/noise part (both are computed on the sneaking player's client and synced via ZDO `Stealth`/`noise`); everyone if you also change monster view angle/range (runs on the AI owner).
- **Assets:** no.
- **Hooks:** `Character.Damage` (attacker-side, before `RPC_Damage`), `BaseAI.IsAlerted` (non-owner value refreshed from ZDO `alert`), `Player.RaiseSkill(Sneak, …)`, `Player.UpdateStealth` (private) or `Player.GetStealthFactor`, `Character.RPC_AddNoise`, optionally `BaseAI.CanSeeTarget` / `MonsterAI.UpdateTarget`.
- **Sketch:** Prefix `Character.Damage`: if attacker is `Player.m_localPlayer`, target has a `BaseAI` that is not alerted and `hit.m_backstabBonus > 1`, raise Sneak by a config amount (optionally scaled by damage) and remember the victim ZDOID for 300 s to mirror the vanilla cooldown. For "early game" detection, postfix `Player.UpdateStealth` to multiply `m_stealthFactorTarget` by a bonus that fades with sneak skill (or with defeated-boss global keys) and scale noise in `RPC_AddNoise`.
- **Risks:** attacker-side prediction can disagree with the victim-owner backstab (per-victim cooldown and PassiveMobs sight check live on the victim owner) — acceptable for XP, or add an optional owner→attacker RPC when both have the mod; stealth factor ramps at 0.25/s; mods like "Sneaky"-type stealth overhauls or EpicLoot sneak effects also postfix stealth.

### Trinket revamp (Revamp) — passive effect + adrenaline boost
- **Feasibility:** medium.
- **Who needs the mod:** client-only functionally (equip SEs and full-adrenaline SEs are applied locally), everyone recommended so tooltips/recipes/balance match; custom SEs must be in ObjectDB of the wearer only (added by reference).
- **Assets:** no (reuse trinket icons/models); optional new icons.
- **Hooks:** `ObjectDB.Awake`/`CopyOtherDB` postfix (set `SharedData.m_equipStatusEffect`, `m_maxAdrenaline`, `m_fullAdrenalineSE` on `ItemType.Trinket` items), `Humanoid.UpdateEquipmentStatusEffects` (already includes the trinket's `m_equipStatusEffect`), `Player.AddAdrenaline` (pop behaviour), `ItemDrop.ItemData.GetTooltip`.
- **Sketch:** For each vanilla trinket generate a weaker "passive" `SE_Stats` (clone of the trinket's full-adrenaline SE with scaled values, or hand-authored) and assign it as `m_equipStatusEffect`; keep/boost the full-adrenaline SE as the burst. Optionally scale passive strength with current adrenaline fill via a custom `SE_Stats` subclass overriding `ModifyAttack`/`ModifyStaminaRegen` reading `Player.GetAdrenaline()/GetMaxAdrenaline()`.
- **Risks:** SharedData edits are global and not saved (re-apply on every ObjectDB init, including `CopyOtherDB` in the main menu); NG+ world-level equip restriction; conflicts with mods that add trinket slots/sets or re-balance trinkets (EpicLoot trinket support, Jotunn item mods).

### Adrenaline revamp (Revamp) — change how it is generated and used
- **Feasibility:** medium.
- **Who needs the mod:** client-only (all gains happen on the owning player; stagger gain arrives via `RPC_AddAdrenaline` to the owner); server-synced config recommended.
- **Assets:** no (optional HUD art).
- **Hooks:** `Player.AddAdrenaline` (funnel), `Player.UpdateStats(float)` (degen), `Player.GetMaxAdrenaline`, source sites: `Attack.DoMeleeAttack`, `Projectile.OnHit`, `Humanoid.BlockAttack`, `Character.RPC_Damage`, `Character.AddStaggerDamage`, `Player.RPC_HitWhileDodging`, `Player.ActivateGuardianPower`; `Hud.UpdateAdrenaline`.
- **Sketch:** Replace the "fill → pop trinket SE" model with a resource: a prefix on `Player.AddAdrenaline` routes gains through a mod-defined table keyed by source (detect source by setting a thread-static "context" flag in prefixes of the source methods), removes the miss/unblocked penalties, and disables the automatic pop; add explicit spenders (e.g. a hotkey or context move consumes adrenaline for an empowered attack/guardian power) and optional tier SEs via `m_adrenalineEffects`.
- **Risks:** the curves `m_adrenalineDegen` / `m_adrenalineDegenDelay` / `m_adrenalineGainMultiplier` and base `m_maxAdrenaline` are prefab data (dump them first); adrenaline is not saved; `Game.m_adrenalineRate` world modifier still applies; overlaps with Trinket and Boss-power revamps — design them together (shared library).

### Boss power revamp (Revamp) — same model as trinkets (passive + boosted activation)
- **Feasibility:** medium.
- **Who needs the mod:** everyone if custom GP SEs are shared with nearby players (added by hash via `RPC_AddStatusEffect`); client-only if only the activator's own behaviour changes and vanilla SEs are reused.
- **Assets:** no.
- **Hooks:** `Player.SetGuardianPower`, `Player.StartGuardianPower` (cost/cooldown gate), `Player.ActivateGuardianPower` (targets/radius 10 m/cooldown), `Player.UpdateGuardianPower` (passive upkeep), `Player.GetGuardianPowerHUD` / `Hud` (display), `ObjectDB` SE registration.
- **Sketch:** While a power is selected, keep a passive SE (a scaled clone of the GP SE, e.g. 25% of Moder's wind bonus) on the player, refreshed in an `UpdateGuardianPower` postfix; make activation require and consume adrenaline (prefix `StartGuardianPower`: check/consume, bypass `m_guardianPowerCooldown`), optionally stronger when adrenaline is full.
- **Risks:** GP SE names differ from the stat switch (`GP_Fader` vs `GP_Ashlands`); cooldown is saved in the profile; interaction with the Adrenaline revamp (activation already grants +10); mods that change GP durations/cooldowns (ValheimPlus-style "guardian power" configs) patch the same methods.

### Ballista revamp (Revamp) — better AI, target assignment
- **Feasibility:** medium.
- **Who needs the mod:** everyone (targeting/firing runs on the turret's ZDO owner, which can be any nearby client).
- **Assets:** no.
- **Hooks:** `Turret.UpdateTarget` (prefix-replace), `Turret.UpdateAttack` (line-of-fire/friendly-fire gate), `Turret.UpdateTurretRotation` (ballistic lead), `Turret.RPC_SetTarget` (already broadcast → every client knows each turret's target), `Projectile.IsValidTarget` (null-owner friendly fire, check `m_hitType == HitData.HitType.Turret`), `BaseAI.FindClosestCreature`, `MonsterAI.GetTargetCreature/GetStaticTarget` (threat).
- **Sketch:** Replace "closest creature every 1 s" with a scored pick: threat (targeting a player or attacking a structure), low HP, distance, and a claim map built from other turrets' current targets within radius so each target gets at most N turrets (keep the current target until it dies/leaves unless a much higher score appears). Before firing, sphere-cast from `m_eye` along the aim and skip if a player/tamed/own structure is first; add gravity compensation using the bolt's `Projectile.m_gravity`; make bolts ignore players/tamed in `Projectile.IsValidTarget` when the hit type is Turret.
- **Risks:** decisions happen on different owners → keep the claim logic derived from networked data only; FixedUpdate cost with many turrets (throttle scoring); vanilla ammo/trophy UI must stay intact; mods that add turret types (e.g. custom ballistas via Jotunn) inherit the patch — filter by config.

### Mob AI revamp (Revamp) — flee when scared, avoid strong players, don't get stuck when rooted/frozen
- **Feasibility:** hard.
- **Who needs the mod:** everyone (AI runs on each monster's ZDO owner; mixed installs make behaviour depend on who owns the mob).
- **Assets:** no.
- **Hooks:** `MonsterAI.UpdateAI` (prefix; early-out branch), `MonsterAI.UpdateTarget` (private; threat/strength evaluation), `BaseAI.Flee`, `BaseAI.MoveTo` / `FindPath` (stuck handling), `BaseAI.StandStillDuration`, `BaseAI.RandomMovement`, `SEMan.ApplyStatusEffectSpeedMods` (detect root: speed after mods ≈ 0), `Humanoid.EquipBestWeapon` (prefer ranged when rooted), `MonsterAI.OnDamaged`.
- **Sketch:** Add a per-instance brain component (added in `MonsterAI.Awake` postfix) that computes fear from HP%, recent damage taken, nearby allies, fire, and a "player strength" score derived from **ZDO-synced** data (target `max_health`, equipped item hashes from `VisEquipment` ZDO → ObjectDB armor/damage, defeated-boss global keys vs the monster's biome); when fearful, a prefix on `MonsterAI.UpdateAI` calls the vanilla base update (reverse-patched `BaseAI.UpdateAI`) and `Flee` (reverse patch) then returns false. For stuck/rooted handling, postfix `BaseAI.MoveTo`: if the mob wants to move, is not rooted and `StandStillDuration(0.3)` > 3 s, invalidate the path cache and do a side-step/jump; while rooted, suppress the "can't reach → flee" timers and prefer ranged attacks.
- **Risks:** `MonsterAI.UpdateAI` is long and changes between patches — prefer small prefixes and reverse patches over copying it; performance (20 Hz × many mobs; avoid allocations); remote players' SEs/inventory are not visible to the AI owner (use ZDO data); conflicts with creature overhauls (RRR monsters, CLLC levels, MonsterLabZ, Therzie Monstrum) that tune the same MonsterAI fields; bosses should be excluded from fear.

### Training dummies can attack enemies (Revamp)
- **Feasibility:** medium (patch is easy; tuning the dummy's AI/attacks is the unknown).
- **Who needs the mod:** everyone (`BaseAI.IsEnemy` is evaluated by the dummy's AI owner and by attackers' clients when filtering hits).
- **Assets:** no.
- **Hooks:** `BaseAI.IsEnemy` (static; `Faction.TrainingDummy` case), `MonsterAI.UpdateTarget`/`FindEnemy`, `Character.m_aiSkipTarget`, `Humanoid.EquipBestWeapon` (dummy attack items), `ObjectDB`/`ZNetScene` prefab edits (`TrainingDummy_attack*` ranges/damage).
- **Sketch:** Postfix `BaseAI.IsEnemy`: when either side is `TrainingDummy`, return true against monster factions (config list) and optionally false against Players ("guard dummy" mode, per-piece toggle stored in the piece ZDO); give the dummy's attack items real damage/range and keep it stationary. Optionally set `m_aiSkipTarget` so monsters ignore dummies.
- **Risks:** `IsEnemy` is a hot path — keep the postfix branch-cheap; dummies may kill tames/Dvergr if faction lists are wrong; if the dummy has zero movement speed it can only hit what walks into range; the dummy dropping piece resources on death (`Character.ApplyDamage` → `Piece.DropResources`) makes monster kills refund materials — acceptable or patch; verify the prefab layout first.

### Increase base HP / Stamina with stats (Revamp) — running, jumping, etc.
- **Feasibility:** easy.
- **Who needs the mod:** client-only (max HP is written to the player's own ZDO `max_health`; stamina is local); server-synced config recommended.
- **Assets:** no.
- **Hooks:** `Player.GetTotalFoodValue(out float hp, out float stamina, out float eitr)` (private postfix), or `Player.m_baseHP`/`m_baseStamina` (public fields) set from `Player.OnSpawned`/periodic update; data from `Player.GetSkills().GetSkillFactor(...)` or `Game.instance.GetPlayerProfile().GetStat(PlayerStatType.DistanceRun/Jumps/...)`; `Hud` food bar uses `GetBaseFoodHP()`.
- **Sketch:** Postfix `GetTotalFoodValue` adding `hp += f(Run, Swim, Blocking skills)`, `stamina += g(Run, Jump, Sneak skills)` with caps; optionally mirror into `m_baseHP` so `GetBaseFoodHP()` and the HUD stay consistent.
- **Risks:** skill loss on death lowers the bonus (feature or not); other food/vitals mods (ValheimPlus food settings, "Better food"-style mods) also postfix `GetTotalFoodValue`/`UpdateFood` — order matters; PvP balance.

### Better tower shields (Revamp) — immovable wall, heavy slow, can't parry, blocks virtually anything
- **Feasibility:** medium.
- **Who needs the mod:** client-only (a player's incoming hits are resolved on that player's own client: `Character.RPC_Damage` → `Humanoid.BlockAttack`; speed is local).
- **Assets:** no.
- **Hooks:** `Character.RPC_Damage` (private prefix: force `hit.m_blockable = true` for frontal non-environmental hits when holding a tower shield), `Humanoid.BlockAttack` (postfix/prefix: extra reduction, no stagger from blocked residual, zero `m_pushForce`, reduced stamina), `Humanoid.UpdateBlock` or `Player.GetJogSpeedFactor` (slow while bracing), `Humanoid.CheckRun`, `ObjectDB` edits (`m_timedBlockBonus = 1` to disable parry, `m_blockPower`, `m_deflectionForce`, `m_movementModifier`), `Character.ApplyPushback`.
- **Sketch:** Identify tower shields by a config prefab list (`ShieldWoodTower`, `ShieldBoneTower`, `ShieldIronTower`, `ShieldBlackmetalTower`, `ShieldFlametalTower`, `ShieldGoldTower`) and force `m_timedBlockBonus ≤ 1`. While blocking with one: add a local SE (`SE_Stats.m_speedModifier ≈ -0.6`, also slows turning) in an `UpdateBlock` postfix, make unblockable-but-frontal hits blockable, and after `BlockAttack` scale residual damage/stagger/pushback down heavily so the player becomes a wall; keep rear hits (the `Dot(hit.m_dir, forward) > 0` rule) and stamina as the counterplay.
- **Risks:** some attacks bypass blocking entirely (AoE `m_launchCharacters` calls `ForceJump` before damage; DoTs, lava, fall) — decide which HitTypes are "blockable anything"; EpicLoot/shield mods postfix `GetBlockPower`/`BlockAttack`; PvP balance; `RPC_Damage` is a hot, long method — prefix only, no transpiler.

### Crossbow revamp (QoL) — stays loaded when put away
- **Feasibility:** easy.
- **Who needs the mod:** client-only. The loaded state is owned by the local player and mirrored to ZDO `WeaponLoaded`, so other players (with or without the mod) see the correct loaded visual through `WeaponLoadState`.
- **Assets:** no.
- **Hooks:** `Player.SetWeaponLoaded(ItemDrop.ItemData)` (private; postfix sets the item flag when non-null), `Player.UpdateWeaponLoading(ItemDrop.ItemData, float)` (private; prefix: if the equipped crossbow carries the flag and `m_weaponLoaded != weapon`, call `SetWeaponLoaded(weapon)` and skip the vanilla reload queue), `Attack.OnAttackTrigger` (postfix: if `m_requiresReload` and the character is no longer loaded, clear the flag on `GetWeapon()`), optionally `ItemDrop.ItemData.GetTooltip` (show "Loaded").
- **Sketch:** Store the state per item in `ItemData.m_customData["VM.CrossbowLoaded"] = "1"` (persisted in inventories, chests, drops and saves; ignored by vanilla). Set it when a reload completes (`SetWeaponLoaded` postfix), clear it only when a shot actually fires (`Attack.OnAttackTrigger` postfix — vanilla leaves the weapon loaded if the trigger aborts for missing ammo/stagger), and on (re)equip restore the loaded state instantly in the `UpdateWeaponLoading` prefix. Vanilla unequip paths (`Humanoid.UnequipItem` → `ResetLoadedWeapon`, hide weapons, swimming, logout) keep calling `SetWeaponLoaded(null)`, which is fine because the flag lives on the item, not on `m_weaponLoaded`.
- **Risks:** balance — carrying several pre-loaded crossbows allows instant weapon-swap volleys (offer a config "only remember the last loaded crossbow"); ammo is consumed at fire time, so no ammo dupe; items loaded before uninstall just keep a harmless custom-data key; reload-speed mods and weapon packs with custom crossbows (any `m_requiresReload` weapon) are handled generically; `UpdateWeaponLoading`/`SetWeaponLoaded` are private (use `AccessTools`/publicizer) and could be renamed in a future patch.

### Ritual (New) — consumes items, requires multiple people
- **Feasibility:** hard.
- **Who needs the mod:** everyone (new networked piece, RPCs, SEs/prefabs must exist on all clients).
- **Assets:** yes (altar/circle piece, VFX/SFX; can start by cloning vanilla models like the offering bowl or boss stones).
- **Hooks:** new `MonoBehaviour` with `ZNetView` on a custom piece (Jotunn `PieceManager`), modelled on `OfferingBowl` (`RPC_SpawnBoss` to owner → confirmation RPC back to the sender) and `Feast` (`RPC_TryEat` → `RPC_EatConfirmation`) to consume items without dupes; `Player.GetPlayersInRange` / `Player.GetAllPlayers` for participants; `SEMan.AddStatusEffect(hash)` to buff each participant (RPC to their owner); `Character.m_onDeath`/`SpawnAbility` for summoning rewards; ZDO keys for state.
- **Sketch:** Players interact with the altar to "join" (owner stores player IDs + join time in ZDO) and offer items (owner validates, increments ZDO counters, sends a confirmation RPC so the offering client removes the items). When the owner sees ≥ N joined players within radius and the recipe complete, it starts a channel (players must stay in radius, e.g. check every second), then resolves: global SE to participants, boss/event spawn, or a crafted reward drop.
- **Risks:** ownership changes mid-ritual (persist all state in the ZDO, not in fields); dupes/exploits if item removal isn't confirmed; players with different mod versions; design overlap with Magic (Crafting/Building categories).

### New summons (New)
- **Feasibility:** medium.
- **Who needs the mod:** everyone (new creature prefabs must be registered in `ZNetScene` on all clients; AI runs on the summon's owner).
- **Assets:** yes for new creatures (models/animations); no if cloning vanilla creatures (`Skeleton_Friendly`, `Troll_Summoned`, wolves, etc.) with tint/stat changes.
- **Hooks:** Jotunn `CreatureManager`/`PrefabManager` clones; `SpawnAbility` (prefab fields: `m_spawnPrefab`, `m_maxSpawned`, `m_levelUpSettings`, `m_copySkill`, `m_commandOnSpawn`, `m_targetType`), `Tameable` (`m_startsTamed`, `m_commandable`, `m_unsummonDistance`, `m_unsummonOnOwnerLogoutSeconds`, `m_levelUpOwnerSkill`), `MonsterAI` (follow/targets), new staff item (`ItemDrop` whose `m_attack.m_attackProjectile` is the SpawnAbility prefab).
- **Sketch:** Clone an existing summon setup (Dead Raiser staff + `Skeleton_Friendly`) and swap the spawned creature for a cloned/new creature with `Tameable` configured as a summon; scale level/count with Blood Magic via `m_levelUpSettings`; give each summon type a role (tank that taunts via `BaseAI.Alert`, healer using a FriendHurt `m_aiTargetType` item with a heal `Aoe`).
- **Risks:** `SpawnAbility.m_maxSpawned` counts all players' summons of that prefab (use separate prefabs or patch the count); unsummon relies on player-name matching (`follow` ZDO); performance with many summons; creature-mod conflicts (AllTameable-style mods change `Tameable`).

### New magical items based on Valheim magic (ward, heal) (New)
- **Feasibility:** medium.
- **Who needs the mod:** everyone (new items/SEs added by hash on other players through hits and AoEs).
- **Assets:** yes (icons at minimum; models can reuse vanilla staffs with recolors).
- **Hooks:** Jotunn `ItemManager` clones of `StaffShield`/`StaffHeal`/`StaffBlocker`; `Aoe` (friendly buff pattern: `m_hitFriendly = true`, `m_hitEnemy = false`, `m_statusEffect`), custom SE subclasses (`SE_Shield` for wards, `SE_Stats` or a subclass overriding `SetLevel(itemLevel, skillLevel)` to scale heals with Blood Magic), `ShieldGenerator` API (`IsInsideShield`, `CheckProjectile`) for a projectile-blocking ward bubble, `Attack` blood-magic fields (`m_attackHealth`, `m_attackHealthReturnHit`).
- **Sketch:** Ward: a staff whose friendly AoE applies a custom `SE_Shield` variant (absorb scales with skill, reflects via `SE_React`-style logic), or a placed "ward totem" piece that projects a small `ShieldGenerator`-like dome; Heal: a channelled staff (`m_loopingAttack`) whose AoE applies a heal-over-time SE scaled by skill and paid with health/eitr.
- **Risks:** vanilla already has `StaffHeal`, `StaffShield` and `StaffBlocker` — differentiate; SEs must be registered in ObjectDB on all clients; balance vs. EpicLoot/Wizardry-type magic packs that add similar staffs.

### More training dummies (New)
- **Feasibility:** easy.
- **Who needs the mod:** everyone (new piece/creature prefabs).
- **Assets:** optional (clone `piece_TrainingDummy`/`TrainingDummy` with material tints; custom meshes only for new looks).
- **Hooks:** Jotunn `PieceManager`/`PrefabManager` clone of the dummy; `Character.m_health`, `m_damageModifiers`, `m_faction`, `m_staggerDamageFactor`; `Humanoid` default items (`TrainingDummy_attack*` clones with different timings); `Character.m_onDamaged` (DPS meter), `DamageText`/`MessageHud` for readouts; `BaseAI.m_viewRange`/`MonsterAI` for a stationary parry trainer.
- **Sketch:** Variants: "Resistance dummy" (configurable `m_damageModifiers` to test damage types), "Parry trainer" (attacks on a fixed rhythm with telegraphed heavy attack, blockable), "DPS dummy" (huge HP, auto-heal via `m_regenAllHPTime`, per-player DPS readout from `m_onDamaged`), "Archery dummy" (moving target using `ArcheryTarget` scoring).
- **Risks:** dummies are Characters (count toward AI/character lists, `IsEnemy` hot path); ensure they don't despawn/level-up like creatures (`SetLevel`, spawn systems); resource refund on death (`Piece.DropResources` from `Character.ApplyDamage`).
