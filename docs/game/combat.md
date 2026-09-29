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
   - The weapon's `m_attackStatusEffect` goes into the `HitData` (subject to `m_attackStatusEffectChance`), and `Projectile.Setup` overwrites the projectile's own `m_statusEffectHash` with it (this is how the harpoon carries `SE_Harpooned`).
   - Only the projectile's owner simulates it (`Projectile.FixedUpdate`): it casts along the flight step, sorts the hits by distance and calls `OnHit` for each until one sets `m_didHit` (or bounces). `OnHit` asks the private `Projectile.IsValidTarget` (friend/foe: `BaseAI.IsEnemy`, aggravatable, owner's PvP; `m_hitFriendly`; dodge i-frames); a refused target is flown through, so **the first accepted target shields everything behind it**. `DoAOE` uses the same filter.
   - After `Damage`, `OnHit` gives the owner `RaiseSkill(m_skill, m_raiseSkillAmount)` and `AddAdrenaline(m_adrenaline)` from the **projectile's own fields**, for any character hit, even a zero-damage one. Harpoon details: [farming-cooking.md §9](farming-cooking.md).

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
5. Attacker bookkeeping in ZDO (`Attackers` count + per-player flags → kill credit in `OnDeath`), `KillModifiers` (melee/ranged/magic/unarmed for achievements). Any player hit writes them, **even a zero-damage one**, and vanilla never clears the per-player flag: a player who once hit a creature is credited with its kill if it dies later while they are connected. (Before the owner check, `RPC_Damage` also adds `EnemyHits`/`PlayerHits` for a local attacker.)
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
`HitData` is transient (serialized only inside the RPC; `m_hitCollider` is converted to `m_weakSpot` index in `Character.Damage`). Console kill commands send attacker-less hits: `killall` and `killtame` (despite its name) kill every loaded non-player creature within 1000 m, tames included; `killenemies` spares tames (`Terminal`, `ShouldKillAll`). Health lives in ZDO `health` (the key is removed in `Character.Awake` when at max) and `max_health`; kill credit in ZDO `Attackers` (count) plus one bool key per attacking player name; kill style in `Modifiers`; cheat flag `cheated`. Stagger accumulation and backstab cooldown are plain fields on the owner (lost on ownership change).

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
  - Residual stagger damage goes to `AddStaggerDamage`; the block only "holds" if the player still has stamina and was not staggered (`flag3`); then status effect is cancelled (`hit.m_statusEffectHash = 0`, so a blocked harpoon does not hook) and `hit.BlockDamage(blocked)`.
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

Vanilla tower shields (asset names verified in the SoftRef `manifest_extended`): `ShieldWoodTower`, `ShieldBoneTower`, `ShieldIronTower`, `ShieldBlackmetalTower`, `ShieldFlametalTower`, `ShieldGoldTower`, plus `ShieldSerpentscale`, which the wiki lists with the tower shields. There is no plain `ShieldTower` prefab: `MoldShieldTower` and `ShieldTowerGoldUncooked` (crafting steps, unverified) and the creature copies `FW_ShieldBlackmetalTower` / `SP_ShieldBlackmetalTower` are separate prefabs. Their block/parry/movement values are *(prefab)*.

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
- Death: `Skills.OnDeath` → `LowerAllSkills(m_DeathLowerFactor (0.25) × Game.m_skillReductionRate)`. 0.25 is the C# initialiser; the wiki gives a 5 % loss per hard death on the Normal preset, so the Player prefab probably sets a lower value *(prefab)*: log it at runtime.
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
| Projectile hitting a character (even for 0 damage) | `Projectile.m_adrenaline` (2) | `Projectile.OnHit` |
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

Targets: `MonsterAI.UpdateTarget` searches every 2 s within 50 m of a player (6 s otherwise) and drops a target when it dies, after 30 s without sensing it or 60 s without attacking (then the next search waits 5 s); a tame also drops a target too far from its patrol point or followed player. `BaseAI.HaveTarget()` reads the ZDO `haveTarget` written by the owner, so any machine can read it. Tames target every wild creature they sense (details: [farming-cooking.md §6](farming-cooking.md), Targets).

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

### Better tower shields (Revamp) — two-handed immovable wall: very slow, can't parry, huge block armor, small shield bash
- **Feasibility:** medium. Most of it is `SharedData` edits that vanilla already honours: two-handed, no parry, block armor and the slow. The bash reuses the vanilla unarmed combo. The medium part is "blocks virtually anything": unblockable hits, stagger while blocking, AoE launch and knockback.
- **Who needs the mod:** client-only.
  - Equipping, attacks, blocking and movement all run on the shield bearer's own client. The bash's `HitData` is built on the attacker's client (`Attack.DoMeleeAttack`). Incoming hits are resolved on the bearer's client (`Character.RPC_Damage` → `Humanoid.BlockAttack`).
  - Each machine reads `SharedData` from its own prefab. A tower shield handed to a player without the mod is a normal one-handed shield for them, and nothing is stored on the item.
  - The brace slow is a local `SE_Stats`; only status-effect attribute flags go to the ZDO, so nothing new reaches other clients.
  - Ship a server-synced config for PvP fairness.
- **Assets:** no. The bash reuses the unarmed attack animation. A dedicated bash animation would need an AssetBundle and the same Animator change on every client.
- **Hooks:**
  - `ObjectDB.Awake` / `ObjectDB.CopyOtherDB` postfix to edit tower shield `SharedData`: `m_itemType`, `m_attachOverride`, `m_timedBlockBonus`, `m_perfectBlockAdrenaline`, `m_blockPower`/`m_blockPowerPerLevel`, `m_movementModifier`, `m_attack`, `m_damages`, `m_attackForce`, `m_skillType`, `m_blockable`/`m_dodgeable`.
  - Vanilla paths the item-type change relies on:
    - `Humanoid.EquipItem`: a `TwoHandedWeaponLeft` item unequips both hands and takes the left hand. Equipping a `OneHandedWeapon` or `Torch` unequips a left item that is not a `Shield`.
    - `Humanoid.Pickup`: auto-equip is skipped when the left item `IsTwoHanded()`.
    - `Humanoid.GetCurrentWeapon`: returns a left item that `IsWeapon()` (and is not a torch), which `TwoHandedWeaponLeft` is. Otherwise it returns `m_unarmedWeapon`, so a vanilla shield with an empty right hand already punches.
    - `Humanoid.GetCurrentBlocker` (private): the left item comes first.
    - `Humanoid.SetupAnimationState` (private): the left item's `m_animationState` wins (unless it is a torch), so the shield stance is kept.
    - `VisEquipment.AttachBackItem` and `ArmorStand.CanAttach` read `m_attachOverride` before `m_itemType`. Hand attach ignores the type. `ItemStand.CanAttach` does not read the override (see Risks).
  - `Humanoid.StartAttack` (the bash goes through it unchanged).
  - `Character.RPC_Damage` (private prefix: force `hit.m_blockable` for frontal hits of allowed `HitType`s).
  - `Humanoid.BlockAttack` (protected; prefix/finalizer scope for the stagger rule, and clamp `m_pushForce`) with `Character.AddStaggerDamage` (protected prefix inside that scope: skip or scale the stagger while stamina remains).
  - `Humanoid.UpdateBlock` (private postfix: add or remove the brace SE on the block start/stop edge).
  - `Character.ApplyPushback` (overloaded: private `(HitData)`, public `(Vector3, float)`) and `Aoe.OnHit` (private): AoE launch and knockback bypass blocking.
  - `Player.GetJogSpeedFactor` / `GetRunSpeedFactor` (protected overrides; only if the slow needs more than `SharedData`).
  - `ItemDrop.ItemData.GetTooltip` (static six-argument overload, give the argument types; add "cannot parry" and bash lines).
- **Sketch:**
  - Identify tower shields by a config prefab list: `ShieldWoodTower`, `ShieldBoneTower`, `ShieldIronTower`, `ShieldSerpentscale`, `ShieldBlackmetalTower`, `ShieldFlametalTower`, `ShieldGoldTower`. The wiki lists the Serpent Scale Shield with the tower shields (no parry bonus, -10% movement). `FW_ShieldBlackmetalTower` and `SP_ShieldBlackmetalTower` are separate prefabs (their prefixes match the `FallenWarrior` and `ShadowPerson` creature prefabs, unverified), so creature copies are not affected. `ShieldTowerGoldUncooked` is another separate prefab (probably a crafting step, unverified).
  - Two-handed: set `m_itemType = TwoHandedWeaponLeft` and `m_attachOverride = Shield`. Vanilla then empties both hands and swaps the shield out when a one-handed weapon or torch is equipped. The back model and the armor stand slot stay those of a shield. `ItemData.AddHandedTip` prints `$item_twohanded`, and the weapon tooltip branch shows damage plus `AddBlockTooltip`.
  - No parry: keep `m_timedBlockBonus ≤ 1`. A parry needs `> 1`, and the `$item_parrybonus` line only shows above 1. The wiki already lists no parry bonus for tower shields, so this only guards against other mods. Also set `m_perfectBlockAdrenaline = 0`: `AddBlockTooltip` shows `$item_parryadrenaline` whenever it is above 0 (code default 5).
  - Block armor: multiply `m_blockPower`/`m_blockPowerPerLevel` (the tooltip's `$item_blockarmor`). `GetBlockPower` adds up to 50% more with the Blocking skill.
    - `BlockAttack` uses block power as the armor value in `HitData.DamageTypes.ApplyArmor`. Once block power is at least half the hit's damage, only damage²/(4·blockPower) gets through.
    - The stamina cost is `m_blockStaminaDrain × clamp01(blocked / blockPower)`, then the equipment block-stamina modifier and SEs apply.
    - Stagger comes only from what gets through: `BlockAttack` adds it from the damage left after block armor, and `ApplyDamage` adds it again from what finally lands, times `m_staggerMultiplier`. Push is scaled by the same fraction as the stamina cost.
    - So high block armor alone makes each blocked hit cheap and nearly harmless.
  - Very slow while carried: make `m_movementModifier` more negative. The equipment sum (`Player.UpdateModifiers`) affects the following, and nothing else:
    - Jog speed: `1 + mod`.
    - Run speed: `(1 + 0.25·runSkill)(1 + 1.5·mod)`.
    - Run, jump and dodge stamina: `Player.CheckRun`, `OnJump`, `GetDodgeStaminaUse`.
    - Knockback: scaled by `clamp01(1 + mod)` in `Character.ApplyPushback`.
  - Slower while bracing: add a local `SE_Stats` with `m_speedModifier` from the `UpdateBlock` postfix. `SE_Stats.ModifySpeed` scales every ground speed and the turn speed in `Character.UpdateWalking`, and clamps the result at 0. Blocking already forbids running (`Humanoid.CheckRun`).
  - Keep the wall standing: a stagger ends `IsBlocking()` until it wears off, so the next hits land unblocked. Inside a `BlockAttack` scope, an `AddStaggerDamage` prefix skips or scales the stagger while stamina remains. ReliableBlock (a `BlockAttack` transpiler) and ZenCombat only keep that one hit blocked and still stagger you.
  - Bash: set `m_attack` to a clone of the player's unarmed combo (`Humanoid.m_unarmedWeapon`), the same approach as CaptainValheim. No player exists yet when `ObjectDB` loads, so take it from `Game.m_playerPrefab` or clone it on first use. Its animation already plays in the shield stance and replicates through `ZSyncAnimation`. Give it:
    - blunt `m_damages` from block armor, a high `m_attackForce` (on `SharedData`) and `m_staggerMultiplier` (on the cloned `Attack`), a short range and a low stamina cost;
    - `m_skillType = Blocking`, so it raises Blocking, and `Character.RPC_Damage` counts Blocking hits as melee kills;
    - `m_blockable`/`m_dodgeable = true`. `Attack.DoMeleeAttack` copies them from the weapon, so without them the bash cannot be blocked or dodged in PvP.
  - Block anything from the front: the `RPC_Damage` prefix marks frontal hits blockable. `BlockAttack` still rejects hits from behind (`Dot(hit.m_dir, forward) > 0`), and a held block also clears the hit's status effect. Filter by `HitType`, not by direction: fall hits use the ground normal as `m_dir`, which passes the facing test.
- **Risks:**
  - Changing `m_itemType` is global and breaks code that looks for `ItemType.Shield`. CaptainValheim's shield techniques check it (seen in its source). EpicLoot's shield enchant rules and auto-equip-shield features such as ZenCombat's probably do too (unverified). Fallback: keep `Shield` and enforce two hands with `Humanoid.EquipItem` postfixes plus a `GetCurrentWeapon`/`StartAttack` override, as CaptainValheim does.
  - MC's shared `src/Shared/ItemKinds.cs` (Sort Chest, Crafting Search and Sort) classifies by `m_itemType`, so tower shields would sort as weapons unless it also reads `m_attachOverride`.
  - `ItemStand.CanAttach` matches its supported types against `m_itemType`, so an item stand that lists `Shield` but not `TwoHandedWeaponLeft` would refuse tower shields (stand lists are *(prefab)*, unverified).
  - `EquipItem` enforces hands only at equip time: turning the feature on while a sword and a tower shield are held must unequip the sword.
  - Vanilla block charges (`m_buildBlockCharges`) fire the shield's own `m_attack` through `Attack.StartWithoutAnimation` after `m_maxBlockCharges` blocks. ZenCombat's and CaptainValheim's pages say the mechanism is off in vanilla. CaptainValheim's config comments give the vanilla tower shield values as 5 charges, 4 s decay and a 0.5 blocking decay factor (*(prefab)*, unverified). A mod that turns it on would fire our bash as the counter.
  - Walk and crouch speeds (`m_walkSpeed`, `m_crouchSpeed` in `Character.UpdateWalking`) ignore the equipment modifier. With the code defaults (`m_speed` 10, `m_walkSpeed` 5), a modifier below -0.5 makes walking faster than jogging. The run factor reaches 0 at -0.67 and the jog factor at -1. Below that, `UpdateWalking` multiplies the move direction by a negative speed, which nothing clamps unless an `SE_Stats` is active. An SE `m_speedModifier ≤ -1` roots the player and turns `Jump` into a weak tired jump (`m_jumpForceTiredFactor`).
  - Not everything reaches `BlockAttack`:
    - Hits with `m_staggerMultiplier ≥ 100` stagger the victim in `RPC_Damage` before the block check.
    - `Aoe.m_blockable` is `false` by default in code, and `Fire` (CinderFire) hits are unblockable.
    - `Aoe.OnHit` applies `m_launchCharacters` (`Character.ForceJump`) and `m_knockBackForce` (`ApplyPushback`) outside `RPC_Damage`. A networked AoE runs this on its owner's machine, so the bearer's client may not be the one applying them.
    - DoTs, lava, falls and drowning skip blocking.
  - Vanilla block, movement and parry values of tower shields are *(prefab)*. The wiki lists -10% movement for tower shields (-5% for other shields). Make Tower Shields Great Again's page says -20% (unverified).
  - Other mods patch the same code: CaptainValheim (`BlockAttack` transpiler, `GetCurrentWeapon`, `StartAttack`, `Pickup`, `RPC_Damage`), ReliableBlock (`BlockAttack` transpiler), GCO and ZenCombat (closed source), EpicLoot (`GetBlockPower`, unverified). The MC mods Crossbow Stays Loaded (`BlockAttack` postfix, `UnequipItem` prefix, `GetTooltip` postfix) and Harpoon Hooks Tames (`RPC_Damage` prefix/postfix) need cross tests.
  - PvP balance.
  - `RPC_Damage` is a hot, long method: prefix only, no transpiler.
  - `SharedData` edits must be re-applied on every ObjectDB init (`Awake` and `CopyOtherDB`).

### Blood magic XP (Revamp) — XP for sacrificing HP and for every hit on a magic shield, given to the caster
- **Feasibility:** medium. The single-player parts are easy (a wrapper on `Character.UseHealth`, a patch around `SE_Shield.OnDamaged`). Crediting the caster is what makes it medium: the bubble lives on whichever client simulates the shielded character, so the XP has to reach the caster through a custom routed RPC with a fallback for mixed installs, and it needs testing with several clients (a shielded friend, a tame owned by another client).
- **Who needs the mod:** everyone for the full idea: the caster, and every client that may simulate a shielded player or tame. Paying HP and shielding yourself are client-only, because both happen on the caster's own client. A shield on another character exists only in the `SEMan` of that character's ZDO owner (`Character.RPC_Damage` adds it there, and `SEMan` state is not synced), so that client must run the mod to see the hits, and the caster's client must run it to receive the XP. A dedicated server only needs it for config sync: `ZRoutedRpc.RPC_RoutedRPC` forwards routed RPCs to the target peer without knowing the method.
- **Assets:** no.
- **Hooks:** `Character.UseHealth` (prefix/postfix to get the HP actually lost; vanilla only calls it from `Attack.Update` on the first attack frame and from `Attack.FireProjectileBurst` when `m_perBurstResourceUsage`, with `Min(GetHealth() - 1, GetAttackHealth())`, so a cast at 1 HP pays nothing), `Attack.GetAttackHealth` (private: `m_attackHealth` + current HP × `m_attackHealthPercentage` / 100, reduced by up to 33% with skill), `Humanoid.m_currentAttack` (read `m_weapon.m_shared.m_skillType`), `Player.RaiseSkill` (applies `SEMan.ModifyRaiseSkill`), `SE_Shield.OnDamaged` (adds `hit.GetTotalDamage()` to the private `m_damage`, then `hit.ApplyModifier(0)`), `SE_Shield.IsDone` (break = `m_damage > m_totalAbsorbDamage`), `SE_Shield.SetLevel` (a recast calls `ResetTime` + `SetLevel`, which recomputes `m_totalAbsorbDamage` but keeps `m_damage`, so a recast does not refill the bubble; the wiki agrees), `StatusEffect.SetAttacker` (an empty virtual; `Character.RPC_Damage` calls it with the caster after adding or refreshing the hit's status effect; only `SE_Harpooned` overrides it), `Player.Awake` (register the RPC next to vanilla `RPC_HitWhileDodging`), `ZNetView.InvokeRPC(string, …)` (routes to the ZDO owner, which is the caster's own client), optionally `Character.RaiseSkill` (summon XP forwarding).
- **Sketch:** Vanilla today: paying HP gives no XP (the wiki says casting gives none). Blood Magic comes from summon hits (a tame's `Character.RaiseSkill` raises the follow target's `Skills` by `Tameable.m_levelUpOwnerSkill` × `m_levelUpFactor`; wiki: Dead Raiser skeletons 0.75 per melee hit and 0.5 per ranged hit, the Trollstav troll nothing) and from the shield break. `SE_Shield.IsDone` pays `m_levelUpSkillFactor` only when damage breaks the bubble, never on expiry, to `m_character` (the wearer) through `Skills.RaiseSkill`, which skips the `Player.RaiseSkill` multipliers; a shielded tame pays nothing because `Character.GetSkills` returns null for non-players. (1) HP sacrifice: wrap `Character.UseHealth` for `Player.m_localPlayer` while `m_currentAttack` uses a Blood Magic weapon, and raise Blood Magic by the share of max HP actually lost × a config rate, so more food HP does not level faster. (2) Remember the caster: postfix `StatusEffect.SetAttacker` on `SE_Shield` instances and store the caster per status-effect instance (e.g. a `ConditionalWeakTable`); on a recast the last caster wins. (3) XP per hit: around `SE_Shield.OnDamaged`, measure what was really absorbed (the change in `m_damage`, capped at the remaining capacity; this stays correct when another mod's prefix skips the original), keep only hits whose attacker exists and is not the wearer, and pay XP in proportion to the share of the bubble's capacity used, so a bigger bubble does not pay more. (4) Break XP to the caster: in an `IsDone` prefix, when the bubble breaks, send the vanilla `m_levelUpSkillFactor` to the caster and set `m_levelUpSkillOnBreak` to `None` on that instance so the wearer is not also paid. (5) Delivery: a local caster gets `Player.RaiseSkill`; a remote caster gets `casterPlayer.m_nview.InvokeRPC("<GUID>.Xp", skill, amount)`, sent only when the caster's Player ZDO carries the mod's capability flag (set while the feature is active); otherwise the vanilla behaviour stays and the wearer keeps the break XP. Credit the skill named by `m_levelUpSkillOnBreak` (Blood Magic for `Staff_shield`; prefab value, verify), so modded shields tied to another skill keep that skill, and only pay shields cast by players (monsters have their own shield effect: `GoblinShaman_shield` is listed in the game's SoftRef manifest; its class is unverified). Optionally, send summon XP the same way.
- **Risks:** XP farming: besides attacker hits, the bubble absorbs every attacker-less `Character.Damage` hit (fall in `Character.UpdateGroundContact`, drowning in `Player.OnSwimming`, Ashlands lava and hot ocean in `Character.UpdateHeatDamage`, `Player.EdgeOfWorldKill`, `SE_Wet` water damage for water-intolerant characters, `SE_Stats` damage ticks), while burning, poison and smoke ticks call `Character.ApplyDamage` and never reach it; a character's own AoE (`Aoe.m_hitOwner`) hits it with itself as the attacker; `HitData.GetTotalDamage` includes chop and pickaxe damage and, for non-player attackers in NG+, `Game.m_worldLevel` × `m_worldLevelEnemyBaseDamage`, which `ApplyModifier(0)` does not remove (so the wearer still takes that part; from reading the code). Count only hits from other attackers, cap them at the remaining capacity, and add a rate cap per caster; PvP-enabled friends and training dummies that fight back (§13) can still feed shields, just as they train weapon skills in vanilla. Absorb scaling is unverified: the wiki gives 200 + 5 per caster skill level, but `Aoe.Setup` passes the weapon's skill only when the Aoe keeps `m_useAttackSettings` (otherwise a player's Aoe uses `SkillType.All` and `m_skillLevel` is 0), and SkeletonCrew's README says vanilla never scales the bubble; dump the prefab or read `SE_Shield.SetLevel` at runtime. Cast-heal-recast loops are only limited by eitr (wiki: Staff of Protection 60 eitr + 40% current HP, Dead Raiser 100 + 40%, Trollstav 120 + 60%; prefab data, verify at runtime); mods that turn eitr costs into HP costs (BloodMagicCompanion) remove that limit, and recast-refill mods (BubbleBar, ShieldOverflowFix, Better Staff Of Protection) let one bubble absorb without end, so also cap XP per bubble. Lost credit: the caster reference lives only in the status effect on the wearer's current owner; a caster logout loses it, a respawn gives the caster a new Player ZDO (a global routed RPC to the caster's peer, the session ID in `ZDOID.UserID`, would survive that; unverified), and an ownership change drops the bubble itself, because status effects are not synced; there is no queue. Unmodded caster: a ZDO-targeted RPC only logs "Failed to find rpc method" (`ZNetView.HandleRoutedRPC`) and a global routed RPC is dropped silently; either way the XP is lost, so the capability flag is what lets the wearer's client fall back to vanilla. Conflicts: ImpactfulSkills (`SE_Shield.OnDamaged` prefix paying the local player), SmartSkills (`SEMan.AddStatusEffect`, `SE_Shield.IsDone`, `Player.RaiseSkill`, `Skills.Skill.Raise`), BloodMagicCompanion (break XP redirect), EitrMagicExtended (`OnDamaged` prefix that can skip the original, `OnDamaged` and `IsDone` postfixes), SecondaryAttacks (`UseHealth` XP, `Attack.GetAttackHealth` postfix), BubbleBar (`SE_Shield.SetLevel` postfix that clears `m_damage`); detect them and turn off the overlapping part, or XP is paid twice. Vanilla summon XP is raised on the follow target's `Skills` on whichever client simulates the summon; when that is not the summoner's client, it lands on a remote copy and is lost (from reading the code; not tested in game). Balance: ship with server-synced config.

### Blood stone revamp (Revamp) — cheap blood spell to sacrifice life, lifesteal, better health management
- **Feasibility:** easy. No new prefab, item, RPC or asset: every part runs on the local player and reuses vanilla fields and rules. It becomes medium, and everyone needs the mod, if the spell ships as a new craftable item instead of a hotkey.
- **Who needs the mod:** client-only. The missing-HP bonus is computed on the attacker's client in `Attack.ModifyDamage`. The rite's health cost, the lifesteal heal (`Character.Heal` on the local owner), the rite status effect (added by reference) and the HUD readout are all local. Other players see the result through the player's ZDO `health`. Server-synced config is recommended for PvP fairness.
- **Assets:** no. Reuse vanilla blood effects, for example `vfx_BloodHit`, `sfx_weapons_blood_enable` and `fx_bloodweapon_hit`, all listed in the SoftRef `manifest_extended` (check that `ZNetScene` has them). Use the Bloodstone (`GemstoneRed`) icon for the status effect.
- **Hooks:** `Attack.ModifyDamage` (private). It multiplies every damage type by `1 + (GetMaxHealth − GetHealth) × m_damageMultiplierPerMissingHP` and by `1 + (1 − GetHealthPercentage) × m_damageMultiplierByTotalHealthMissing`. It runs when each hit is built in `DoMeleeAttack`/`DoAreaAttack`, and at release in `FireProjectileBurst`, so an arrow or bolt keeps the bonus it was fired with. The bonus uses absolute missing HP, so more food HP raises the ceiling. Other hooks:
  - `ItemDrop.ItemData.GetTooltip` (static overload): the vanilla lines `$item_damagemultiplierhp` and `$item_damagemultipliertotal` show the rate × 100 %. This is the only place the bonus is visible; the HUD never shows it.
  - `Hud.UpdateHealth` (private): add a live "+X%" readout.
  - `Attack.GetAttackHealth` (private): the cost rule to mirror, `(m_attackHealth + current HP × m_attackHealthPercentage / 100) × (1 − 0.33 × skill factor)`, where the skill is the weapon's own (Blood Magic on the blood staffs). `Attack.Update` pays `UseHealth(min(HP − 1, cost))`. `Attack.Start` only flashes the health bar when HP is short. A per-burst payment in `FireProjectileBurst` stops the attack instead.
  - `Character.UseHealth`: it only clamps at 0, so apply the HP − 1 cap yourself.
  - `Character.Heal`.
  - `Player.Update` (private): the hotkey, only when `TakeInput` is true and the player is not `InAttack`, `InDodge` or `IsStaggering`.
  - `Player.RaiseSkill(Skills.SkillType.BloodMagic, …)`.
  - `SEMan.AddStatusEffect(StatusEffect, …)` with an `SE_Stats`: use `m_modifyAttackSkill`/`m_damageModifier` and `m_healthRegenMultiplier`, or a subclass that overrides `ModifyAttack` for a dynamic value.
  - `Character.Damage`: attacker-side postfix for lifesteal.
  - `BaseAI.IsEnemy`.
  - `Character.ApplyDamage`: optional last-stand ward (the local player owns its own ZDO).
  - `ObjectDB.Awake`/`CopyOtherDB`: optional per-HP multiplier.
- **Sketch:** Today the vanilla way to "go low" on purpose is a blood staff. The wiki lists Staff of Protection at 60 eitr + 40% of current HP, Dead Raiser and Spirit Caller at 100 eitr + 40%, and Trollstav at 120 eitr + 60%, so going low needs an eitr build and a weapon swap. Heal-on-hit exists only as the flat `Attack.m_attackHealthReturnHit`, and no vanilla weapon pairs it with the bloodstone bonus.
  1. Detect bloodstone weapons from data: `m_attack` or `m_secondaryAttack` with `m_damageMultiplierPerMissingHP` or `m_damageMultiplierByTotalHealthMissing` above 0. This covers the Ashlands Bleeding weapons (`AxeBerzerkrBlood`, `MaceEldnerBlood`, `SwordNiedhoggBlood`, `THSwordSlayerBlood`, `SpearSplitner_Blood`, `BowAshlandsBlood`, `CrossbowRipperBlood`; prefab names from `manifest_extended`), probably the Deep North `*Gold_BloodLightning` set *(prefab – verify)*, and modded items.
  2. **Blood rite** (the cheap spell): a hotkey pays a share of current HP with the vanilla rule (Blood Magic skill discount, capped at HP − 1). Starting values: 25% of current HP, no eitr, 30 s cooldown. It raises Blood Magic, plays vanilla blood effects and adds a local 15 s "Blood rite" `SE_Stats`.
  3. **Lifesteal**, while the rite is active (or always, by config): a `Character.Damage` postfix runs when the attacker is `Player.m_localPlayer`, the victim is a `Character` and `BaseAI.IsEnemy` is true. It heals a share of the outgoing hit that grows with missing HP. For example, 0.05% of the hit per missing HP gives 10% at 200 missing HP. The heal is capped per swing. Damage-over-time ticks never reach this hook: `SE_Burning`/`SE_Poison` call `Character.ApplyDamage` directly, with no attacker.
  4. **Readout:** "+X% damage" next to the health bar, and the live value in the tooltip.
  5. **Options:**
     - The rite SE sets `m_healthRegenMultiplier = 0`, so the 10 s food regen tick in `Player.UpdateFood` (Σ `m_foodRegen` × `SEMan.ModifyHealthRegen`) does not refill the HP you paid.
     - The first lethal hit during the rite leaves 1 HP and ends the rite.
     - A config multiplier for the per-HP bonus.
  6. **Without Harmony:** an `SE_Stats` with a negative `m_healthPerTick` damages its owner every `m_tickInterval` while HP% ≥ `m_healthPerTickMinHealthPercentage` (`SE_Stats.UpdateStatusEffect`). That gives a "bleed down to a floor" rite with no patch. It goes through `Character.Damage`/`RPC_Damage`, though, so a Staff of Protection shield absorbs it.
- **Risks:**
  - Lifesteal is estimated from the outgoing hit on the attacker's client, as EpicLoot does. The victim owner applies resistances, NG+ enemy armor, difficulty scaling, the backstab bonus and the ×2 on staggered targets only later. The outgoing total also counts fire, poison and spirit damage that lands later as damage over time. An exact value needs an RPC from the victim owner, and then everyone needs the mod.
  - Multi-target swings and AoEs call `Character.Damage` once per target, so cap the heal per attack.
  - `BaseAI.IsEnemy` is false for tames and for every other player, so PvP needs its own `IsPVPEnabled` check. It is true for `Faction.TrainingDummy`, so exclude dummies if heal-farming matters. Structures are not Characters.
  - Do not use vanilla `Projectile.m_healthReturn`: `Projectile.OnHit` heals the owner after damaging any destructible, trees, rocks and buildings included (`IsValidTarget` accepts every non-character).
  - EpicLoot LifeSteal/Blood Drinker (its shared `Character.Damage` patch), JardsAdditions vampirism and WeaponArts Bloodthirst stack with ours and multiply the healing. Detect them or document it.
  - Health meads and food regen keep raising HP.
  - PvP balance needs a server-synced config.
  - It overlaps with the *Blood magic XP* idea (XP for sacrificing HP), so design the rite's XP together with it.

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

### Combat pet (New) — Animal Handling skill, pets gain XP and stars (up to 5), one combat ability per star
- **Feasibility:** hard. Each part is small on its own (XP in the pet's ZDO, `Character.SetLevel`, a custom skill, star UI), but multiplayer ownership forces an RPC on every XP path, and about 25 abilities (5 stars × Wolf, Boar, Lox, Asksvin, Moose) must be built from each animal's existing attacks and balanced. Medium for a first version whose abilities are passive status effects only.
- **Who needs the mod:** everyone. The pet's AI, attacks, XP and level-ups run on its ZDO owner, which can be any nearby client (`ZDOMan.ReleaseNearbyZDOS`), not only its handler. Ability items, status effects and AoE prefabs must be registered on every client (`ZNetScene`/`ObjectDB`): an attack's status effect is added by hash on the victim's owner. Each client draws stars 3-5 and their tint itself. The server should also carry the mod for config sync.
- **Assets:** none required. `Skills.SkillDef.m_icon` takes any `Sprite`, so a vanilla icon (for example a trophy's) works and a custom skill icon is optional. Ability VFX/SFX can clone vanilla prefabs, and stars 3-5 can reuse the vanilla HUD star.
- **Hooks:** `Character.SetLevel` (no owner check, so call it on the ZDO owner; writes ZDO `level`, calls the private `SetupMaxHealth` = `GetMaxHealthBase() × level`, fires `m_onLevelSet`); `Attack.GetLevelDamageFactor` (private; `1 + 0.5 × (level − 1)`, so 3.5× at level 6); `Character.RaiseSkill` (virtual; a tame's own hits call it from `Attack.DoMeleeAttack`/`DoAreaAttack`, `Projectile.OnHit` and `Aoe`, and it forwards to the followed player's skill only when `Tameable.m_levelUpOwnerSkill` is not `None`); `Character.OnDeath` (runs on the victim's owner; the killer is `m_lastHit.GetAttacker()`); `Character.GetRandomSkillFactor` (for non-players, the ZDO float `RandomSkillFactor`: it multiplies `Attack` melee, area and projectile hits, while `Aoe` uses it only for player owners); `Tameable.Awake` (register RPCs), `Tameable.GetHoverText`, `Tameable.Interact` / `Tameable.RPC_Command`; `Humanoid.GiveDefaultItems` / `Humanoid.EquipBestWeapon` / `BaseAI.CanUseAttack`, `SEMan.AddStatusEffect`; `Skills.IsSkillValid` / `Skills.GetSkillDef` (or Jotunn `SkillManager`); `EnemyHud.ShowHud` / `EnemyHud.UpdateHuds`, `LevelEffects.SetupLevelVisualization`; `Procreation.Procreate`; `BaseAI.IsEnemy` / `BaseAI.FindEnemy` (training dummies); `ZNetScene.Awake` (`m_commandable`, prefab edits), `ObjectDB.Awake`.
- **Sketch:**
  - Stars = vanilla level. A pet's stars are its `Character` level (ZDO `level`: 1 = no star, 6 = 5 stars), and vanilla already scales HP and damage from it on whichever machine owns the pet. Wild animals keep their spawn level when tamed (`SpawnSystem.GetLevelUpChance`: 10% per extra level by default, times `Game.m_enemyLevelUpRate` and a biome-sector multiplier; world level uses its own formula). So a wild 1★ or 2★ animal has abilities 1-2 from the moment it is tamed, as the idea asks.
  - XP is a custom ZDO float on the pet, written only by its owner. Sources: per hit (`Character.RaiseSkill` postfix, tamed non-players only); a kill bonus scaled by the victim's biome, level and boss flag (`Character.OnDeath` postfix, sent with `ZNetView.InvokeRPC` to the pet's owner through an RPC registered in a `Tameable.Awake` postfix); and sparring with a T.W.I.G. dummy at a reduced rate with a daily cap (tames already treat dummies as enemies through the tamed branch of `BaseAI.IsEnemy`; `BaseAI.FindEnemy` skips targets with `Character.m_aiSkipTarget`, so check the dummy prefab at runtime). At each threshold the owner calls `SetLevel(level + 1)` and broadcasts the new level (see Risks).
  - Animal Handling skill: Jotunn `SkillManager`, or a `SkillDef` added to `Skills.m_skills` plus a `Skills.IsSkillValid` patch. It rises on the handler's own client through an RPC to the handler's player: the built-in forwarding in `Character.RaiseSkill` would raise the tame owner's local copy of a remote player's `Skills`, which is never saved. It also rises from taming, commanding and petting (vanilla counts these as `PlayerStatType.CreatureTamed`, `TamedCommand` and `TamedPetting`; `CreatureTamed` is counted on the creature's owner, who is not always the tamer). Effects: the highest star the handler can train (for example 3★ from 25, 4★ from 50, 5★ from 75), faster XP gain, and a damage bonus that the pet's owner writes to its ZDO `RandomSkillFactor`. `SpawnAbility.m_copySkill` summons use the same key, and even a vanilla owner applies it.
  - Active abilities come from a config table per species and star: cloned attack items for the unlocked stars. A `Humanoid.GiveDefaultItems` postfix adds them (the method runs from `Humanoid.Start` on every client); on level-up the mod adds the new item directly, because calling `GiveDefaultItems` again would duplicate the default items. `Humanoid.EquipBestWeapon` runs on the AI owner about once a second through `MonsterAI.SelectBestAttack`, only while the pet has a target, and picks among inventory items by `m_aiTargetType` (Enemy / FriendHurt / Friend), `m_aiAttackRange(Min)`, `m_aiAttackInterval` and `m_aiPrioritized`. So an ability is a clone with a long interval and a new payload (`m_attackStatusEffect`, `m_spawnOnTrigger` AoE, `m_spawnOnHit`) that reuses an attack animation the creature already has. The extended SoftRef manifest (`StreamingAssets/SoftRef/manifest_extended`) has attack-like names: `Wolf_Attack1`-`3`, `boar_base_attack`, `lox_bite`, `lox_stomp`, `Asksvin_Bite`, `Asksvin_Headbutt`, `Asksvin_Pounce`, `Asksvin_Turnaround`, `moose_horns`, `moose_horns_sweep`, `moose_hooves` *(verify which are items in each prefab's `m_defaultItems`)*. A `FriendHurt` item makes a support ability such as healing the handler, but only mid-fight.
  - Passive abilities are `SE_Stats` on the pet, re-applied by its owner because the SE list is not networked, or short auras on the handler sent by hash.
  - Example set (a design proposal, not tuned): Wolf 1★ hamstring bite (slow), 2★ pack howl (speed for the handler and pack), 3★ lunge, 4★ bleed, 5★ alpha aura. Lox: stronger staggering stomp, taunt. Boar: knockback charge. Asksvin: pounce that roots. Moose: horn sweep that knocks back.
  - UI: `EnemyHud.ShowHud` only finds the `level_2`/`level_3` children, and `UpdateHuds` shows them for level 2 and level 3 exactly: clone the star for levels 4-6. `LevelEffects.SetupLevelVisualization` returns early when `level − 1` exceeds `m_levelSetups.Count` (prefab data), so a pet above the last setup keeps its previous tint and scale, and shows none after a reload: clamp to the last setup. A `Tameable.GetHoverText` postfix shows stars, XP to the next star and unlocked abilities.
  - Commands: `Tameable.m_commandable` is prefab data, and sources disagree on which vanilla tames have it (Cartur's Follow Command says wolves and lox; the Valheim wiki's Taming page names only wolves) *(prefab – verify)*. Set it in `ZNetScene.Awake` for every combat species that lacks it. `Tameable.Interact` checks the flag on the interacting client, but `RPC_Command` on the owner does not, so this works even when a vanilla client owns the pet. An optional "attack my target" command could work like WolfPack's or use the ping revamp.
  - Breeding: `Procreation.Procreate` gives the newborn `Max(m_minOffspringLevel, level of the pregnant parent)`; for egg layers it sets the egg's quality instead, and `EggGrow` turns the quality into the hatchling's level. `Growup` copies the level to the adult. A trained 5★ pet would therefore breed 5★ babies with every ability: clamp offspring to the natural maximum (level 3) or apply the Breeding revamp rule. XP is not inherited.
- **Risks:**
  - Level sync: non-owners read `Character.m_level` from the ZDO only in `Character.Awake`, and nothing refreshes it later. After a mid-session star-up, other clients keep the old stars, tint and `GetLevel()` until the pet reloads. Modded clients can refresh through a broadcast RPC or a ZDO-revision check; a vanilla client that later takes ownership still attacks with the stale level factor.
  - Ownership: the pet's owner is not necessarily its handler, and kills resolve on the victim's owner. BetterTames' kill level-up (a `Character.OnDeath` postfix that requires `IsOwner()` on the killer) silently does nothing when the victim and the tame have different owners. Route every XP event to the pet's owner and every skill raise to the handler.
  - Balance: vanilla scaling at level 6 is 6× HP and 3.5× damage. Flattening it (`Character.SetupMaxHealth` prefix, `Attack.GetLevelDamageFactor` postfix) only works when the pet's owner has the mod, and `Character.Awake` re-runs `SetupMaxHealth` on the owner whenever the pet is at full HP. The `RandomSkillFactor` bonus does not reach AoE payloads (a non-player `Aoe` ignores it). Also watch XP farming on dummies (daily cap) and many followers in boss fights.
  - Permanence: a pet's death is final (`Tameable.OnDeath` only drops the saddle), so losing hours of training hurts: offer an optional knock-out, or point to pet-protection mods. Removing the mod drops the custom skill on the next load (`Skills.Load` → `IsSkillValid`). The death penalty (`Skills.OnDeath`: `m_DeathLowerFactor` 0.25 × `Game.m_skillReductionRate`, on a hard death) applies to the new skill too.
  - Hand-off / uninstall: vanilla clients see 3★+ pets with no stars and no tint. A vanilla owner still applies the level and `RandomSkillFactor`, but not the ability items (it rebuilds the inventory from the prefab in `Humanoid.Start`) or the passive SEs, and it never levels the pet. After uninstall, level 4-6 animals remain and vanilla handles them mechanically.
  - Content limits: abilities must use animation triggers the creature's Animator already has (a missing trigger does nothing). The manifest has no attack-like asset for the hen, so hens probably cannot fight. `BaseAI.IsEnemy` checks `m_group` first, so tame wolves never fight wild wolves (group is prefab data).
  - Compatibility: StarLevelSystem / CLLC / ReefLevels (level scaling and star UI); BetterTames and Monster Level Up (their own kill level-ups: disable one); BetterTames, GrindstoneSkills, ReefLevels and our Breeding revamp (offspring stars); BeastMaster / AllTameable / LetMeTameYou (they patch `Tameable` and have their own progression); WolfPack (commands).
