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

Vanilla dual weapons (asset names verified in the SoftRef `manifest_extended`): `AxeBerzerkr` (+ `AxeBerzerkrBlood`, `AxeBerzerkrLightning`, `AxeBerzerkrNature`) and `KnifeSkollAndHati` (plus the creature copies `FW_KnifeSkollAndHati` and `SP_KnifeSkollAndHati`). Each is one item (the wiki lists the Berserkir axes as dual wielded and Skoll and Hati as a two-handed knife), and the player's animation sets are `player_DualAxes.fbx` and `player_DualKnives.fbx`. No code splits an item over two hands, so the second blade comes from the prefab (unverified). `Humanoid.EquipItem` never keeps two `OneHandedWeapon` items: equipping one unequips a left item that is not a `Shield` or `Torch`. See *Dual wielding* under Feature ideas.

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
- `Player`: `m_adrenaline` (not saved – resets on load; synced to ZDO `adrenaline` each stats tick), `m_maxAdrenaline` (code default 100 *(prefab – verify; Surge's README, read from the game, says 0, so only trinkets grant capacity)*), `m_adrenalineDegen` / `m_adrenalineDegenDelay` / `m_adrenalineGainMultiplier` (AnimationCurves over fill fraction *(prefab)*), `m_adrenalineEffects : List<StatusEffectLevel{m_rate, m_se}>` (tiered SEs – assets `AdrenalineRush`, `AdrenalineRush2..4` exist), `m_adrenalinePopEffects`, `m_lastMaxAdrenaline`, private `m_adrenalineGuardianPower` (10).
- `Character.GetMaxAdrenaline()` = `GetEquipmentMaxAdrenaline()` (sum of `SharedData.m_maxAdrenaline` of equipped items); `Player.GetMaxAdrenaline` adds `m_maxAdrenaline`.
- Trinkets: `ItemType.Trinket`, slot `Humanoid.m_trinketItem` (ZDO `TrinketItem` via `VisEquipment.SetTrinketItem`). Vanilla trinkets (15 item prefabs in 1.0.16, runtime item dump of 2026-09-29): `TrinketBronzeHealth/Stamina`, `TrinketIronHealth/Stamina`, `TrinketSilverDamage/Resist`, `TrinketBlackDamageHealth`, `TrinketBlackStamina`, `TrinketCarapaceEitr`, `TrinketChitinSwim`, `TrinketScaleStaminaDamage`, `TrinketBloodGoldHealth/Stamina` (Neckstabber, Witch Crown), `TrinketFlametalEitr/StaminaHealth` (each has a same-named SE asset in the SoftRef `manifest_extended`, i.e. its `m_fullAdrenalineSE`). `TrinketDNGold` and `TrinketDNNornThread` (SoftRef names in building-crafting.md §13) are only models and icons in `manifest_extended`, with no item prefab. Equipping trinkets requires matching world level in NG+ (`Humanoid.EquipItem`).
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
`Turret` (on `piece_turret`, and also on the Ashlands enemy `piece_Charred_Balista` and the trap `fuling_turret` according to ServersideQoL's 1.0 component dump, [ArgusMagnus/ValheimServersideQoL](https://github.com/ArgusMagnus/ValheimServersideQoL) `Docs/Components`; `Hoverable`, `Interactable`, `IPieceMarker`, `IHasHoverMenu`), ammo items `TurretBolt`, `TurretBoltWood`, `TurretBoltBone`, `TurretBoltFlametal`, `TurretBoltBloodgold` whose `Projectile` prefabs (`Turret_projectile`, `Turret_projectilebone`, `Turret_projectile_bloodgold` in the dump) have `m_gravity` 0 and `m_drag` 0, `BaseAI.FindClosestCreature` (targeting), `WearNTear` (destroy → ammo returned).

### Flow
- `Turret.FixedUpdate`: everyone runs `UpdateReloadState` (armed/unarmed visuals from ZDO `lastAttack`), `UpdateTurretRotation`, `UpdateVisualBolt`; non-owners re-read configured targets when ZDO data revision changes; **owner** runs `UpdateTarget` + `UpdateAttack`.
- `Turret.UpdateTarget` (owner): no ammo → clear; every `m_updateTargetIntervalNear` (1 s, if any character within 40 m) or `Far` (10 s in code, 4 s in the `piece_turret` dump): `BaseAI.FindClosestCreature(transform, eye, hear 0, m_viewDistance, m_horizontalAngle, alerted:false, mistVision:false, passiveAggressive:true, m_targetPlayers, tamed flag, m_targetEnemies, configured targets)` → **closest** sensed creature, re-picked at every search with no memory (a closer creature steals the target); change → `RPC_SetTarget` to everybody. Clears the target at once when it dies, but the next search waits for the timer. Here `FindClosestCreature` is sight only (hear range 0; range × the target's stealth factor, angle from the turret's base forward, `m_viewBlockMask` raycast, mist), skips sleeping and dead characters, and checks neither `BaseAI.IsEnemy` nor `Character.m_aiSkipTarget`: with `m_targetEnemies`, every non-player `Character` is a candidate, passive animals and T.W.I.G. dummies included.
- `Turret.UpdateTurretRotation`: returns at once while `IsCoolingDown()`, so the turret **freezes during every reload**. Lead = target velocity × (`Vector2.Distance`(target, eye) / projectile speed) × `m_predictionModifier`: the implicit `Vector3` → `Vector2` conversion keeps only x and y, so the flight time ignores the z offset (no lead at all for a target offset only along z), and the `piece_turret` dump sets `m_predictionModifier` 2 (lead doubled). Aim point = target position + lead, raised by half the target's first `CapsuleCollider` height (1 m without one); the direction is taken from `m_turretBody`'s position, while `ShootProjectile` fires from `m_eye`. **No gravity compensation**, and none is needed since the turret projectiles in the dump have `m_gravity` 0. Clamps the yaw to ±`m_horizontalAngle`; there is no pitch limit (`m_verticalAngle` is never read). Turning: `Utils.RotateTorwardsSmooth(…, m_turnRate × dt, m_lookAcceleration, m_lookDeacceleration, m_lookMinDegreesDelta)` (in `assembly_utils`): far from the aim, the step ramps up from the last step's movement (× acceleration); within about 11° of it, the speed falls in proportion to the remaining angle (× deacceleration). `m_aimDiffToTarget` = quaternion dot of the new and the wanted rotation.
- `Turret.UpdateAttack`: shoot when target, aim dot ≥ `m_shootWhenAimDiff` (0.9 in code; 0.9999 in the `piece_turret` dump, i.e. within about 1.6°), ammo, not cooling down (`lastAttack + m_attackCooldown` in ZDO; 1 s in code, 2 s in the dump). With the dump values (`m_turnRate` 45, `m_lookDeacceleration` 0.05) the final approach closes about 3.9 × the remaining angle per second, so a target whose bearing changes faster than about 6° per second (for example 3 m/s sideways at 20 m) keeps the aim outside the 1.6° window: the ballista follows it but never shoots (computed from the code, not measured in game).
- `Turret.ShootProjectile`: decrements ZDO `ammo`, builds `HitData` from the ammo item (`HitType.Turret`), `Projectile.Setup(owner: null, ...)`. With a null owner `Projectile.IsValidTarget` skips the friend/foe test → **bolts damage anything they hit, including players and tamed creatures in the line of fire**.
- Ammo: `UseItem`/`Interact` → `RPC_AddAmmo(prefabName)` to owner → ZDO `ammo`/`ammoType`; `m_maxAmmo`, `m_allowedAmmo` (with visuals). Trophy targeting: using a trophy listed in `m_configTargets` toggles it (max `m_maxConfigTargets`; when full, `UseItem` drops the oldest one) → `SetTargets` claims ownership and writes ZDO `targets` + `target{i}` strings; `ReadTargets` rebuilds `m_targetCharacters` from every stored trophy (matched by `Character.m_name`), whatever the reader's own cap, and the hover text lists them all. With a trophy set, `m_targetTamedConfig` replaces `m_targetTamed`.
- Target flags: `m_targetPlayers` / `m_targetTamed` / `m_targetEnemies` default to true and `m_targetTamedConfig` to false, in code and in the `piece_turret` dump. Other `piece_turret` values in ServersideQoL's 1.0 component dump (third-party, updated for 1.0 on 2026-09-10; re-check at runtime): `m_turnRate` 45, `m_horizontalAngle` 50, `m_verticalAngle` 50, `m_viewDistance` 30, `m_noTargetScanRate` 7, `m_lookAcceleration` 1.2, `m_lookDeacceleration` 0.05, `m_attackCooldown` 2, `m_predictionModifier` 2, `m_maxAmmo` 40, `m_maxConfigTargets` 1 (code defaults: 10, 25, 20, 10, 10, 1.2, 0.05, 1, 1, 0, 1). The wiki gives ±45° and 28 m. `m_verticalAngle`, `m_attackWarmup` and `m_warmUpStartEffect` are declared but never read.

### Data & persistence / multiplayer authority
ZDO: `ammo`, `ammoType`, `lastAttack` (network time, drives cooldown visuals on all clients), `targets` + `target0..n` (trophy names). `m_target` is a local field kept in sync by `RPC_SetTarget` (everybody). `RPC_AddAmmo` goes to the owner. `SetTargets` claims ownership before writing. Projectiles are spawned by the turret owner and simulated by that client.

### Patch points
`Turret.UpdateTarget` (private; target selection/assignment), `Turret.UpdateAttack` (private; fire decision, line-of-fire check), `Turret.UpdateTurretRotation` (private; aiming/ballistics), `Turret.ShootProjectile` (public), `Projectile.IsValidTarget` (private; friendly fire), `Turret.RPC_SetTarget`. Everything decision-related runs on the turret's ZDO owner (any client) → all clients need the mod.

---

## 13. Training dummies & archery targets

- Faction `Character.Faction.TrainingDummy`: in the `BaseAI.IsEnemy` faction table it is an enemy **only of Players**, but the tamed rule runs first, so `IsEnemy(dummy, tamed creature)` is also true and the dummy swings at tames it sees (from the code, not tested; the wiki says it only attacks players who approach it, and that other creatures, tames included, never choose it as a target on their own). Monsters see it as an enemy (generic faction table), but the prefab sets `Character.m_aiSkipTarget` (dump below), so `BaseAI.FindEnemy` never picks it; `MonsterAI.OnDamaged` → `SetTarget(attacker)` has no such check, so a creature the dummy hits targets it back when that creature has no target yet. `BaseAI.FindClosestCreature` (ballistas) checks neither `IsEnemy` nor `m_aiSkipTarget`, so a ballista with no trophy set shoots dummies (from reading the code, not tested). An unalerted dummy takes backstabs like any creature (`Character.RPC_Damage`: once per 300 s per dummy) and makes `BaseAI.InStealthRange` true for a player within its view range, so sneaking near it pays full Sneak XP (`Player.OnSneaking`); hitting it pays adrenaline like any enemy (`Attack.DoMeleeAttack`: `m_attackAdrenaline` × its `m_enemyAdrenalineMultiplier`, prefab value unverified).
- Assets (SoftRef manifest): building piece `piece_TrainingDummy` and a character prefab `Assets/Characters/TrainingDummy/TrainingDummy.prefab` with attack items `TrainingDummy_attack`, `TrainingDummy_attack2`, `TrainingDummy_attack3`, `TrainingDummy_throw` (+ `_throw_projectile`), alert/idle/stagger SFX. The piece is the T.W.I.G. (`$piece_trainingdummy`). ServersideQoL's 1.0 component dump (third-party; re-check at runtime) shows that `piece_TrainingDummy` is itself a `Humanoid` with `MonsterAI` and `Piece`: `m_health` 2500, `m_regenAllHPTime` 30, `m_aiSkipTarget` true, `m_speed`/`m_walkSpeed`/`m_runSpeed` 0 (it cannot walk), `m_turnSpeed` 0 and `m_runTurnSpeed` 300 (whether it turns in place is unverified), `MonsterAI` view range 30 m with `m_viewAngle` 90 (`BaseAI.CanSeeTarget` allows up to 90° off its facing, so the front half, and ignores the angle once alerted), hear range 0, alert range 10 m. `MonsterAI.UpdateAI` only attacks an alerted dummy's target, so a creature must first come within 10 m (× its stealth factor) or hit the dummy. `TrainingDummy_throw_projectile` has `m_gravity` 10. The separate `TrainingDummy` prefab (40,000 HP, `m_aiSkipTarget` false) is not the piece. The wiki adds: no resistances, every attack deals 1 damage (three melee attacks and a throw), recipe 5 Fine wood, 10 Bronze nails, 5 Ectoplasm at the workbench. `Character.ApplyDamage` calls `Piece.DropResources` when a Character that is also a `Piece` dies, which is how a buildable character refunds materials.
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

### Weapon revamp (Revamp) — slight rework: roll attack, parry attack, jump attack, optionally livelier swings
- **Feasibility:** medium. The three context attacks reuse vanilla inputs, the per-swing `Attack` clone and animation triggers the player Animator already has, and every check runs on the local player. Livelier swings are easy with vanilla fields (movement while attacking, root-motion lunge, animation speed). Only brand-new animation clips (the "maybe" in the idea) would make it very hard.
- **Who needs the mod:** technically client-only, unless new animation clips are added (then everyone). It changes combat balance, so it ships as Both: the server refuses players without the mod and sends its settings to everyone.
  - Rolls, jumps, blocks and attack starts run on the player's own client (`Player.UpdateDodge`, `Character.Jump`, `Humanoid.StartAttack`). A parry is resolved in `Humanoid.BlockAttack` on the victim's owner, which for a player is their own client.
  - The move's `HitData` (damage, `m_staggerMultiplier`, `m_pushForce`) is built on the attacker's client and applied by the victim's owner with vanilla rules, so a vanilla peer that owns the target handles it.
  - Other players see the move: `ZSyncAnimation.SetTrigger` sends the trigger name to everybody, and every Animator has the vanilla triggers. The owner's animator speed is mirrored through the ZDO, the position through the transform sync.
  - New clips would need everyone: an `AnimatorOverrideController` swap is local, so players without the mod see the vanilla clip (the hit still fires on the attacker's own clip).
  - Under Both, the server's settings apply to everyone, which also keeps PvP fair.
- **Assets:** no. Optional new clips need an AssetBundle and an Animator override.
- **Hooks:**
  - `Humanoid.StartAttack` (prefix for `Player.m_localPlayer`: decide the context move for a primary attack; clear the flag in a finalizer).
  - `Attack.Start` (prefix on the per-swing clone: set its trigger, chain levels and multipliers before the trigger fires).
  - `Player.UpdateDodge` (private; prefix/postfix to see `m_inDodge` turn false = end of the roll), `Player.OnDodgeMortal` (end of the roll's i-frames).
  - `Character.Jump` (prefix: `force == false` is a jump from input; `CharacterAnimEvent.Jump` calls `Jump(force: true)` from animation events), `Player.OnJump` (the jump happened), `Character.IsOnGround`.
  - `Humanoid.BlockAttack` (protected; prefix runs the vanilla parry test with the private `m_blockTimer` and opens a scope that a finalizer closes) with `Player.AddAdrenaline` (a `Priority.First` prefix or a postfix, inside that scope: on a parry, vanilla calls it only when the block held and there was an attacker; the Adrenaline revamp shares this test).
  - `Attack.OnAttackTrigger` (the hit moment), `Character.m_onLand` (an `Action<Vector3>` fired on landing after a drop of more than 0.8 m from the highest point, and on entering water after 0.5 m; `FootStep` also subscribes).
  - `Humanoid.GetAttackSpeedFactorMovement` / `GetAttackSpeedFactorRotation` (protected overrides), `Humanoid.OnStopMoving` (the clip's `Stop` event sets both factors to 0), `Character.AddRootMotion`, `CharacterAnimEvent.Speed` (swing tuning).
  - `ZSyncAnimation.HasParameter` (check a trigger exists before using it), `Player.PlayerAttackInput` (private; the input buffer, read only).
- **Sketch:**
  - Vanilla today:
    - `Humanoid.StartAttack` refuses while dodging, staggering, knocked back or in a minor action, but has no ground check: you can already swing in the air, and `GetAttackSpeedFactorMovement` returns 1 while airborne, so the jump keeps its momentum.
    - `Player.PlayerAttackInput` buffers a press for 0.5 s and retries `StartAttack` every FixedUpdate, so an attack pressed late in a roll starts as soon as the roll ends. `Player.UpdateDodge` refuses a roll during an attack.
    - A parry (`m_timedBlockBonus > 1`, block raised less than 0.25 s before the hit, stamina left and no stagger from the hit) staggers an attacker with `m_staggerWhenBlocked`, and `Character.RPC_Damage` doubles damage on staggered non-players.
    - So the three moments exist; they just play the normal swing.
  - Detect the context on the local player, each with a short configurable window:
    - Roll attack: a `Player.UpdateDodge` postfix stamps the moment `m_inDodge` turns false. A primary attack within about 0.4 s (example value) counts; the vanilla buffer already covers a press made in the last 0.5 s of the roll.
    - Parry attack: the `BlockAttack` prefix runs the vanilla parry test (`m_timedBlockBonus > 1`, `0 <= m_blockTimer < 0.25`). On a parry, vanilla calls `AddAdrenaline` only when the block held and there was an attacker, whatever the amount (even 0, and also without a trinket), so an `AddAdrenaline` call inside the scope marks a held parry exactly. Testing `HaveStamina()` and `GetStaggerPercentage()` in a postfix is close, but it sees the stamina after the parry's own stamina change. The Adrenaline revamp needs the same test to tell a parry from a block: write it once and share it. Catch the call with a `Priority.First` prefix or a postfix, so the detector still fires when another prefix (the Adrenaline revamp's, GrindstoneSkills') rewrites the amount. A held parry opens a window of about 1 s (PPR uses 1 s, GrindstoneSkills 2 s).
    - Jump attack: the `Character.Jump` prefix marks jumps from input, `Player.OnJump` confirms them, and a primary attack before `IsOnGround()` is true again counts. Walking off a ledge does not count (GCO accepts it after 1 s of airtime; optional).
  - Run the move through vanilla code:
    - The `StartAttack` prefix only acts for primary attacks of melee weapons (`m_attackType` Horizontal, Vertical or Area); bows, staves and `m_requiresReload` weapons keep vanilla. Attacks with skill Blocking (the weapon's `m_skillType`) keep vanilla too: the Better tower shields bash is a clone of the unarmed combo, which may pass the type test *(prefab, unverified)*, and a roll or jump move would replace it and stack its ×2 stagger on the bash's ×5-10. It sets a "pending move" flag.
    - The `Attack.Start` prefix edits the clone that `StartAttack` just made (`Attack.Clone` is a `MemberwiseClone`, so the item's `SharedData` stays untouched): `m_attackAnimation` becomes the move's trigger, with `m_attackChainLevels` and `m_attackRandomAnimations` at 0 so `Start` fires that name as is, plus `m_damageMultiplier`, `m_staggerMultiplier`, `m_forceMultiplier`, `m_attackStamina` and `m_attackAdrenaline`.
    - The stamina check, rotation and other mods' patches run unchanged. The name differs from the weapon's own, so the next normal swing restarts the combo at its first hit, as GCO's jump attacks do.
  - Pick the animation per weapon family (skill type and `m_animationState`), defaulting to the family's own triggers. A chained attack fires `m_attackAnimation` plus the chain index (`Attack.Start`), so the finisher can be played directly:
    - Roll attack: the finisher, a lunging heavy hit out of the roll.
    - Parry attack: the first chain hit with a faster wind-up (animator speed through `CharacterAnimEvent.Speed`), a quick riposte.
    - Jump attack: the finisher or the secondary animation with extra stagger; optionally a downward pull on the body and a small area slam on landing. `Character.m_onLand` only fires after a drop of more than 0.8 m, so a short jump on flat ground may not reach it (jump height is prefab data, unverified): fall back to the first `IsOnGround()` after the attack.
    - Check each trigger with `ZSyncAnimation.HasParameter` at start-up and fall back to the weapon's secondary animation. Trigger names such as `swing_longsword`, `atgeir_attack`, `greatsword_secondary` or `dualaxes` come from mod READMEs *(unverified)*: dump every weapon's `m_attackAnimation` and chain levels at runtime. Sword Heavy Slash already plays `dualaxes0` and `greatsword2` on swords, so borrowing another family's trigger works.
  - Bonus: `Attack.DoMeleeAttack` gives the finisher ×2 damage and ×1.2 push only when `m_attackChainLevels > 1`, so the moves use `m_damageMultiplier` instead. Starting values to tune: roll attack ×1.3 damage, parry attack ×1.5 damage and ×2 stagger, jump attack ×1.2 damage and ×2 stagger. A normal combo already ends on that ×2 finisher, so a move that plays the finisher animation at ×1.3 hits less hard than the vanilla finisher: tune against it.
  - Adrenaline: vanilla pays the clone's `m_attackAdrenaline` per enemy hit, times the victim's `m_enemyAdrenalineMultiplier` (`Attack.DoMeleeAttack`), so that field is the per-move bonus. The Adrenaline revamp can pay a per-move bonus for them; the moves cost no adrenaline, because the bar is kept for the trinket proc. It does not time these moves (their `m_attackAnimation` differs from the weapon's own) and pays the weapon's learned amount × the clone's `m_attackAdrenaline` / the weapon's. A move that borrows the secondary animation stays untimed too, because the Adrenaline revamp compares it with the primary attack it was cloned from. A parry attack on a family without a chain (`m_attackChainLevels` 1 or less, prefab data) would keep the vanilla trigger and be timed as a normal swing with no per-move bonus: give it another trigger.
  - Livelier swings (optional, mild by default):
    - Movement and turning while swinging: raise the clone's `m_speedFactor` / `m_speedFactorRotation` (code default 0.2; prefab values unverified) per family. The clip's `Stop` event (`Humanoid.OnStopMoving`) sets both to 0 on the current attack, so patch it too to keep some movement through the recovery.
    - Lunge: scale the forward root motion in a `Character.AddRootMotion` prefix while attacking. Root motion only accumulates during a dodge, attack or emote, and `Character.ApplyRootMotion` uses it when it is larger than the input velocity.
    - Tempo: scale `CharacterAnimEvent.Speed` during wind-up or recovery.
    - Recovery cancel into block or roll after the hit: vanilla forbids it (`IsBlocking` is false and `UpdateDodge` refuses while `InAttack`), and leaving the attack state needs an Animator exit (unverified). AttackCancel and GCO's block canceling do it; keep it an off-by-default option.
- **Risks:**
  - Animator: a missing trigger does nothing, silently. A trigger that exists but has no transition from the current state (idle, dodge, jump or fall) may fire late or never (Animator data, unverified). Test every family.
  - Borrowed clips can clip through a shield or change the hit timing (the hit fires on the clip's `Hit` event), while the hit geometry (`m_attackRange`, `m_attackAngle`, `m_attackHeight`) stays the weapon's.
  - Cancel Animation Cancels, for unarmed, spears, axes, battleaxes and atgeirs, clears the queued attacks when a dodge starts right after an attack, and suppresses the primary attack while block and attack are held during a dodge (the default keyboard roll is block + jump). A roll attack pressed mid-roll with block still held is lost for those families. The koumodgp anti-cheat lists animation cancels among the exploits it guards against. Detect both and warn.
  - Same inputs, other mods: GCO (running and jump attacks on primary; turn our jump attack off when it is present), PPR (counter on the secondary attack after a parry), GrindstoneSkills Riposte (a bonus that stacks with ours), SpecialAttack, SecondaryAttacks and Quickstep (a dash may not set the dodge tag the roll detector watches, unverified), AttackCancel and AttackCancleCounter. The *Dual wielding* idea edits the same `Attack.Start` clone for a pair of one-handed weapons (vanilla dual-axe or dual-knife triggers): run it first, and pick the context moves from the pair's set.
  - Balance: roll then boosted hit can be spammed behind the roll's i-frames (charge stamina or add a short cooldown); the jump attack pays jump plus attack stamina; the parry attack stacks with the vanilla ×2 on staggered targets. The server settings (Both) keep the values equal for everyone, PvP included.
  - `Player.AddAdrenaline` is shared: the Adrenaline revamp, GrindstoneSkills and AdrenalineModifier patch it with prefixes that rewrite amounts. The parry detector reads the call, not the amount (`Priority.First` or a postfix). Test the parry attack with the Adrenaline revamp on. A mod that turns on the vanilla block-charge counter (CaptainValheim, ZenCombat) fires `Attack.StartWithoutAnimation` inside `BlockAttack`, and the hits of that counter call `AddAdrenaline` (`Attack.DoMeleeAttack`). A parry that did not hold would then look held, and the Adrenaline revamp would tag those hit gains as block or parry: the shared helper ignores calls made during `Attack.StartWithoutAnimation` (a flag set in its prefix, cleared in a finalizer).
  - A plunge's speed does not raise fall damage (`Character.UpdateGroundContact` uses the height fallen), but a fall above 4 m still hurts.
  - `StartAttack` also runs for monsters: act only for `Player.m_localPlayer`. MC Crossbow Stays Loaded patches `Attack.OnAttackTrigger` and `Humanoid.BlockAttack` (postfixes; crossbows are excluded; cross test). EpicLoot and weapon packs such as Therzie's Warfare (its own animation replacement) patch `Attack` and `Humanoid`.
  - Optional new clips: AssetBundle and override pipeline, clashes with other animation mods, and players without the mod see the vanilla clips.

### Sneak revamp (Revamp) — XP on sneak attack, much stealthier when standing still
- **Feasibility:** easy.
- **Who needs the mod:** everyone for the sneak-attack XP; the standing-still bonus alone is client-only.
  - The sneak attack (backstab) is decided on the victim's ZDO owner in `Character.RPC_Damage`, which in multiplayer is often another player's client. Detecting it there and sending the XP to the attacker needs the mod on both clients.
  - The stealth factor is computed by the sneaking player's own client (`Player.UpdateStealth`) and reaches every AI owner through the player ZDO `Stealth` (`Player.GetStealthFactor`), so the stillness bonus needs nobody else.
  - It changes gameplay, so it ships as a Both mod: the server refuses players without it and sends its settings to everyone (house rule for experience-changing mods).
- **Assets:** no. The optional "holding still" status icon reuses the Sneak skill icon (`Skills.SkillDef.m_icon`).
- **Hooks:** `Character.RPC_Damage` (private; prefix + postfix around the vanilla backstab, victim owner), `Character.GetFaction` (skip training dummies), `Character.GetMaxHealth` (XP scaling), `Player.RaiseSkill(Skills.SkillType, float)`, `Player.Awake` (register the XP RPC) with `ZNetView.Register` / `ZNetView.InvokeRPC`, `Player.UpdateStealth` (private postfix; stillness and the floor), `Player.IsCrouching`, `Character.IsSneaking`, `SEMan.AddStatusEffect(StatusEffect, …)` / `SEMan.RemoveStatusEffect(int, bool)`, `SE_Stats.m_stealthModifier` (applied by `SEMan.ModifyStealth`), `Hud.UpdateStealth` (read only: the vanilla stealth bar already shows the factor).
- **Sketch:**
  - Vanilla today:
    - Sneak XP only comes from `Player.OnSneaking`: 1 per second when `BaseAI.InStealthRange` (an enemy within its view range or 10 m, and none of those alerted), else 0.1. `Character.UpdateWalking` calls it only while `Character.IsSneaking` (crouching, moving faster than 0.1, on the ground), so standing still earns nothing and costs no stamina. A sneak attack gives no XP. The Sneak skill is read in only two places: `OnSneaking` and `UpdateStealth`.
    - The stealth factor ignores movement. `Player.UpdateStealth` only checks `IsCrouching`, the light factor and the Sneak skill, so a crouched player standing still is exactly as visible as one sneaking. Standing still only saves stamina: crouched movement adds no noise anyway (`Character.UpdateWalking`). `m_lastStealthPosition` is written there but never read.
    - The factor scales how far monsters see (`BaseAI.CanSeeTarget`: view range × factor) and how close they must be to become alerted (`MonsterAI.UpdateAI`: `m_alertRange` × factor).
  - XP on sneak attack:
    - A `Character.RPC_Damage` prefix saves the victim's private `m_backstabTime` and the postfix compares it. It changes only on the victim owner, and only when the vanilla rule passed: victim not alerted, `hit.m_backstabBonus > 1`, 300 s cooldown per victim, PassiveMobs sight check.
    - With a `Player` attacker, raise Sneak by a config amount (for example 5, about five seconds of sneaking near enemies) times the victim's max health (`Character.GetMaxHealth`) divided by a config reference. The factor is capped at the top but has no floor, so weak creatures pay almost nothing. Halve it for ranged hits (`hit.m_ranged`). If the attacker is `Player.m_localPlayer`, call `RaiseSkill` directly; otherwise invoke an RPC on the attacker's `ZNetView`, which reaches the attacker's own client. `Player.RaiseSkill` keeps `SEMan.ModifyRaiseSkill` and `Game.m_skillGainRate`.
    - No XP when the victim is a training dummy (`Character.GetFaction` returns `Faction.TrainingDummy`). Dummies are easy to hit while unalerted, and an OFF dummy of the *Training dummies revamp* is never alerted, so every dummy would pay a backstab every 300 s.
    - Melee, projectile and AoE hits all carry the weapon's backstab bonus (`Attack`, `Projectile`, `Aoe` copy it into `HitData`), so all of them count.
    - SecondaryAttacks (sighsorry) already ships this exact detection (see `docs/research/existing-mods-combat-ux.md`), so ours should stand down when it, GCO or SmartSkills is loaded.
  - Standing still:
    - A `Player.UpdateStealth` postfix on the local player tracks how long it has been crouched, on the ground and not moving (horizontal speed ≤ 0.1). After a short delay (for example 1 s) it adds a local `SE_Stats` with a negative `m_stealthModifier`. `SE_Stats.ModifyStealth` adds base × modifier, so −0.5 halves the factor, and with it the view and alert ranges. Moving, standing up or leaving the ground removes the SE.
    - Floor: every stealth SE adds its own base × modifier, and `Player.UpdateStealth` only clamps the sum to 0..1. Our SE stacked with other stealth SEs (the Sneak tiers of *New ability depending on skill level*, or other mods') could drive the factor to 0: unseen at any range. While our SE is active, the postfix raises `m_stealthFactorTarget` to a config floor (for example 0.1), but never above the value it would have without our SE.
    - The SE is created at runtime with a unique name (`StatusEffect.NameHash` hashes the object name) and added by reference, so it needs no ObjectDB entry. Optionally make the bonus grow with Sneak skill.
    - The factor still moves at 0.25 per second (`Mathf.MoveTowards` in `UpdateStealth`), so the bonus fades in over one to two seconds, and the vanilla stealth bar shows it.
  - No XP for standing still: vanilla gives none, and it keeps AFK farming out.
  - With the *Mob AI revamp*: weak creatures never target a player who outclasses them, so they usually stay unalerted. Vanilla `Player.OnSneaking` then pays the full 1 XP per second while the player crawls near them (`BaseAI.InStealthRange`), and the first hit on each is a backstab. The victim-HP scaling keeps that backstab XP near zero; the crawling XP is vanilla's (see the Mob AI revamp for an option).
- **Risks:**
  - Stop-and-go: the 0.25 per second ramp lets a player crawl in short bursts and keep part of the bonus. Tune the delay, or drop the bonus instantly on movement.
  - Balance: halving the view range while still makes bow shots from cover and ambushes much safer; server settings.
  - Stacking: without the floor, our SE plus the Sneak tiers of *New ability depending on skill level* (or another mod's stealth SE) could reach a factor of 0. Tune the floor with both mods loaded.
  - Other mods:
    - Double XP: SecondaryAttacks grants Sneak XP on every vanilla backstab with the same detection, SmartSkills on hits against unaware enemies, and GCO with its sneak bonus. Detect them and skip our XP, or document it.
    - SecondaryAttacks recomputes the stealth target in its own `Player.UpdateStealth` postfix but still applies `SEMan.ModifyStealth`, so an SE modifier composes with it; writing `m_stealthFactorTarget` directly would be overwritten. SetUpSkills (a transpiler on `UpdateStealth`), Sneaky Viking and ImpactfulSkills compose with an SE modifier too.
    - MC Harpoon Hooks Tames sets `m_backstabBonus = 1` on harpoon hits against tames (`Character.Damage` prefix), so hooking a tame never grants sneak XP.
  - `Player.OnDeath` clears all status effects (`SEMan.RemoveAllStatusEffects`); the stillness check re-adds the SE on its own.

### Trinket revamp (Revamp) — a slightly weaker always-on passive with an adrenaline interaction, tied to the Adrenaline revamp
- **Feasibility:** medium.
- **Who needs the mod:** technically client-only; ships as Both (the server refuses players without the mod and sends its settings to everyone), because it changes combat balance. The trinket's `m_equipStatusEffect` (`Humanoid.UpdateEquipmentStatusEffects`) and its `m_fullAdrenalineSE` (the pop in `Player.AddAdrenaline`) are both passed as objects to `SEMan.AddStatusEffect` on the wearer's own client, not looked up by hash. Custom status effects therefore need no registration on other machines.
- **Assets:** no (vanilla icons and models).
- **Hooks:**
  - `ObjectDB.Awake` (private) / `ObjectDB.CopyOtherDB` postfix: build the passives and set `SharedData.m_equipStatusEffect` on `ItemType.Trinket` items (optionally `m_maxAdrenaline` too).
  - `Humanoid.UpdateEquipmentStatusEffects` (private): applies the trinket's equip SE, no patch needed.
  - `Player.AddAdrenaline`: the vanilla pop, no patch needed.
  - `Player.GetAdrenaline` / `Player.GetMaxAdrenaline`: the bar fill.
  - `SEMan.HaveStatusEffect`: is the proc running.
  - `SE_Stats.UpdateStatusEffect`: overridden in a subclass.
  - `ItemDrop.ItemData.GetTooltip`: no patch needed. It prints the equip SE through the private `GetStatusEffectTooltip`, and the proc under `$item_fulladrenaline`.
- **Sketch:**
  - Vanilla: a trinket only adds adrenaline capacity (`SharedData.m_maxAdrenaline`, summed as an equipment modifier). Its effect only exists after a full bar (`m_fullAdrenalineSE`, 30 to 120 s per the wiki).
  - 1.0.16 has 15 trinkets in `ObjectDB`, costing 10 to 100: the 13 listed by Surge, plus the Deep North Neckstabber (`TrinketBloodGoldHealth`, 65 per the wiki) and Witch Crown (`TrinketBloodGoldStamina`, 60 per the wiki) (runtime item dump, 2026-09-29). `TrinketDNGold` and `TrinketDNNornThread`, listed as SoftRef names in building-crafting.md, are only models and icons in the SoftRef `manifest_extended`, with no item prefab or status effect.
  1. **Tied to the Adrenaline revamp.** The trinket part assumes the revamp's steady build-up. Ship it as a second feature of the same mod, or as its own mod with `ModRequires` on the adrenaline mod. The framework then turns it off, and says why, when the adrenaline mod is missing or disabled.
  2. **Passive bonus, slightly nerfed.** On every ObjectDB init, build one passive per trinket whose `m_fullAdrenalineSE` is an `SE_Stats`:
     - copy its fields into an instance of our `SE_Stats` subclass (`ScriptableObject.CreateInstance`) with a new name, so its `NameHash()` differs from the proc's (do not copy the private cached `m_nameHash`);
     - no duration (`m_ttl` 0);
     - one-shot and timed gains set to 0: `m_healthUpFront`, `m_staminaUpFront`, `m_eitrUpFront`, `m_adrenalineUpFront`, `m_healthOverTime`, `m_staminaOverTime`, `m_eitrOverTime`;
     - continuous stats × a strength factor (config, starting value 0.75, with per-trinket overrides): the regen multipliers' part above 1, armor, skill levels, damage percentages, speed and swim speed, stamina-use modifiers;
     - damage-resistance steps one weaker (they are enums), in a new `m_mods` list, not the proc's;
     - assign it as the trinket's `m_equipStatusEffect`, after checking that vanilla trinkets have none *(prefab – verify)*. Vanilla then applies it on equip and shows it in the tooltip.
  3. **Interaction with adrenaline.** Both parts are toggles, and both are on by default.
     - (a) Build-up: the passive grows with the bar, from a floor (config, e.g. 50% of the passive) at empty to 100% at full. The subclass recomputes its fields from the stored base values × that factor, a few times per second, in `UpdateStatusEffect`. It reads the fill from `Player.GetAdrenaline() / GetMaxAdrenaline()`.
     - (b) Proc: a full bar still pops the vanilla full effect, one-shot gains included, for its vanilla duration. While it runs (`SEMan.HaveStatusEffect` on the proc's hash), the passive gives nothing, so the two do not stack.
  4. Some trinkets' procs are only one-shot gains (the wiki gives Brimstone 100 health and 100 stamina at once). They get no passive, or a small regen passive by config: a decision for the user. Trinkets whose `m_fullAdrenalineSE` is not an `SE_Stats` (the Blood trinket's custom effect) get no passive either.
  5. Costs: optionally even out `m_maxAdrenaline` of the 15 vanilla trinkets (10 to 100; matched by prefab name, from stored originals), so the Adrenaline revamp's "one proc per fight" target holds for each. Modded trinkets keep their own cost: the Blood trinket has its own cost config.
- **Risks:**
  - `SharedData` edits are global and not saved. Reapply them on every ObjectDB init (`Awake` in a world, `CopyOtherDB` in the main menu) from stored originals, so they never compound.
  - `SEMan.AddStatusEffect` skips an SE whose `NameHash()` is already present, so a passive with the proc's name would block the proc. `NameHash()` caches its value in the private `m_nameHash`, so a field-by-field copy by reflection must skip that field.
  - `StatusEffect.Clone` is a shallow copy (`MemberwiseClone`). Per-instance changes may only touch value fields, never shared lists such as `m_mods`.
  - When the pop hits a proc that is still running, `Player.AddAdrenaline` calls `ResetTime`, and `SE_Stats.ResetTime` re-runs the one-shot gains (vanilla behaviour).
  - An always-on passive at 75% is a large buff over vanilla uptime. "Slightly nerfed" needs playtesting; the server's numbers apply to everyone.
  - A modded trinket cloned from a vanilla prefab after our postfix inherits that trinket's passive. The Blood trinket clears `m_equipStatusEffect` on its clone.
  - Extra trinket slots (ExtraSlots with MultiTrinket, RPG Equipment on Nexus) stack several passives.
  - Conflicts: BetterTrinkets, its 1.0 patch and Passive_Trinket_Modifiers put passives on the same items, Balrond Battle Flow replaces trinket effects, and Surge edits `m_maxAdrenaline`. Detect them and step aside, or document it.
  - The NG+ world-level rule for equipping trinkets (`Humanoid.EquipItem`) still applies.

### Adrenaline revamp (Revamp) — faster build-up that is fair across weapons, income while fighting, a trinket proc every fight
- **Feasibility:** medium. Each part is a small patch on the local player, and the work is in the tuning. The decay and gain curves and the per-weapon and per-creature values are prefab data, so dump them at runtime first. Then every weapon family needs a test fight.
- **Who needs the mod:** technically client-only; ships as Both (the server refuses players without the mod and sends its settings to everyone), because it changes combat balance. Adrenaline lives on the owning player (`Player.AddAdrenaline`, mirrored to ZDO `adrenaline`), and the remote signals already reach that client:
  - stagger adrenaline, through `Character.RPC_AddAdrenaline` when another client owns the staggered creature;
  - "a monster targets you", through `Player.RPC_OnTargeted`;
  - kill credit, through `Game.RPC_RegisterKill` on each player who hit the creature.
- **Assets:** no (optional: an "in combat" cue on the adrenaline bar).
- **Hooks:**
  - `Player.AddAdrenaline` is the single funnel. A gain sets the decay delay (`m_adrenalineDegenDelay` curve), is multiplied by `Game.m_adrenalineRate`, the `m_adrenalineGainMultiplier` curve and `SEMan.ModifyAdrenaline`, and only counts below max. At max, it pops every equipped `m_fullAdrenalineSE` and resets the bar.
  - `Player.UpdateStats(float)` (private, every FixedUpdate on the owner): decay starts once the private `m_adrenalineDegenTimer` runs out.
  - Sources to tag:
    - `Attack.DoMeleeAttack`: per character hit, and the miss penalty `Player.m_attackMissAdrenaline`;
    - `Attack.DoAreaAttack`: once per attack, × the highest enemy multiplier;
    - `Attack.OnAttackTrigger` and `Attack.FireProjectileBurst`: `m_attackUseAdrenaline` (again in each burst only when `m_perBurstResourceUsage` is set);
    - `Projectile.OnHit`: a flat `m_adrenaline`, with no enemy multiplier;
    - `Humanoid.BlockAttack`: a block or a parry, told apart by the vanilla parry test (point 1);
    - `Player.RPC_HitWhileDodging`, `Player.ActivateGuardianPower` and `SE_Stats.StartupEffects`;
    - `Character.AddStaggerDamage` (protected; runs on the staggered creature's owner) and `Character.RPC_AddAdrenaline` (private; how that stagger credit reaches the player when the creature's owner is another client);
    - `Character.RPC_Damage`: the unblocked-hit penalty.
  - Combat state:
    - `Player.RPC_OnTargeted` (private). Its `alerted` flag also starts the combat music through `MusicMan.ResetCombatTimer`. It carries no targeter.
    - `Player.IsTargeted`.
    - `Character.Damage`, on the attacker side: the local player hit something.
    - `Character.RPC_Damage`: the local player was hit.
    - `BaseAI.IsEnemy`, `BaseAI.IsAlerted` and `BaseAI.HaveTarget`. Any client can read the last two, because they come from the ZDO.
    - `BaseAI.GetAllInstances` and `Character.GetFaction`: is an alerted enemy near that is not a `Faction.TrainingDummy`.
  - Attack time:
    - `Humanoid.StartAttack`: read the protected `m_attackDrawTime` at bow release.
    - `Attack.Start` (a postfix, so it sees the per-swing clone after the Weapon revamp's prefix has edited it) and `Attack.Stop`.
    - `Humanoid.GetAttackDrawPercentage`: a full draw takes `m_drawDurationMin`, down to 20% of it at skill 100.
    - `ItemDrop.ItemData.GetWeaponLoadingTime`: the crossbow reload, down to 50% with skill.
  - Kills: `Game.RPC_RegisterKill`. UI: `Hud.UpdateAdrenaline` (private).
- **Sketch:**
  - Vanilla today:
    - With no trinket the player's own max adrenaline is 0 (Surge's README, read from the game; the code default of `Player.m_maxAdrenaline` is 100), so nothing is gained. Vanilla trinkets cost 10 to 100.
    - The wiki lists +1 per hit for one-handed weapons and +2 for two-handed weapons, polearms, bows and crossbows. It also gives parry +5, perfect dodge +5, block +1 or +2, stagger +3 and Forsaken power +10. The code defaults differ (perfect dodge 10, stagger 5).
    - Per the wiki, the bar starts to drain after 6 to 10 s without a gain, then loses 1 to 4 per second.
    - Melee pays per character hit in a swing. An arrow pays once, for the one character it hits. A full-power bow shot needs the full draw, which is longest at low skill, so bows fall furthest behind.
    - Short fights rarely fill the bar, and the next fight starts from zero.
  - The plan, per line of the sheet:
  1. **Rebalance across all combat systems.**
     - A prefix on `Player.AddAdrenaline` rewrites the amount by source. The source comes from a `[ThreadStatic]` context, set in prefixes (and cleared in finalizers) of the source methods above. Unknown sources pass through unchanged. The prefix only changes the amount and never skips the call.
     - Block or parry: the `Humanoid.BlockAttack` prefix runs the vanilla parry test, `m_timedBlockBonus` above 1 and the private `m_blockTimer` between 0 and 0.25 s. It is the same test as the Weapon revamp's parry attack (one shared helper if both ship). Vanilla pays `m_blockAdrenaline` only when it is not a parry, and `m_perfectBlockAdrenaline` only when the parry held and there is an attacker. So an `AddAdrenaline` call inside that scope is a held parry when the test passed, and a block otherwise. Our prefix never skips the call, so the Weapon revamp's detector (`Priority.First` or a postfix) still fires. A tower shield that cannot parry (Better tower shields) earns only the block amount.
     - One config table covers defence (block, parry, perfect dodge, stagger), offence (point 3), kills and income (point 2), and the two penalties (miss and unblocked hit, off by default).
     - `Character.m_enemyAdrenalineMultiplier` stays (the wiki says Seeker Broods give 10%), so swarm mobs stay cheap.
     - A weapon-hit amount of 0 or less is kept as it is, because data or another mod turned it off.
  2. **Passive income while in a fight.**
     - The local player counts as "in combat" for a few seconds after hitting an enemy, being hit by one, blocking, or being reported as the target of an alerted monster (`Player.RPC_OnTargeted` with `alerted`). That RPC carries no targeter, so it only counts while an alerted enemy that is not a dummy is near (`BaseAI.GetAllInstances`, `IsAlerted`).
     - `Faction.TrainingDummy` is ignored for the combat state, the engaged count and the kill bonus. Hitting a dummy is a player action and `BaseAI.IsEnemy` makes it an enemy, so a row of dummies would otherwise pay income, a large engaged count and trinket procs with no danger. Weapon hits on a dummy still pay (point 3), so a trinket can be tried out on one.
     - While in combat, a `Player.UpdateStats` postfix adds `base + perEnemy × max(0, engaged − 1)` per second, capped. It goes through `AddAdrenaline` once per second, so `Game.m_adrenalineRate` and status-effect modifiers still apply.
     - "Engaged" = the sum of `m_enemyAdrenalineMultiplier` over the distinct enemies (by ZDOID) the player hit or was hit by in the last ~10 s. A swarm of cheap mobs counts as one or two enemies, as point 1 intends.
     - Income needs a player action in the last few seconds, not only being targeted, so standing next to a stuck or caged monster earns nothing.
  3. **Weapon gains that follow active time.**
     - An attack that hits at least one enemy pays `rate × attack time` instead of a flat amount per hit, so every weapon earns the same per second of fighting.
     - Attack time is learned per weapon (by `m_dropPrefab` name), from `Attack.Start` to `Attack.Stop`. It adds the bow draw read at release and the crossbow reload from `GetWeaponLoadingTime`. A per-skill config table seeds it until it is measured.
     - Moves that are not the weapon's own are not timed, because they would skew the learned time. An attack whose `m_attackAnimation` differs from the weapon's own (the `Attack` it was cloned from: `m_shared.m_attack` for a primary attack, `m_secondaryAttack` for a secondary one, as the `secondaryAttack` argument of `Humanoid.StartAttack` says), such as a Weapon revamp roll, parry or jump attack (all primary attacks, also when one borrows the secondary animation), pays the weapon's learned amount × the clone's `m_attackAdrenaline` / the weapon's own `m_attack.m_attackAdrenaline`. The Weapon revamp's per-move bonus survives, and the moves cost no adrenaline.
     - Melee pays once per swing, plus a small, capped bonus per extra enemy hit: cleaving still pays, but not × N.
     - Projectiles pay per arrow that hits, divided by the projectiles per attack.
     - `m_attackUseAdrenaline` on staffs follows the same rule (wiki: Trollstav +12 per cast).
  4. **A trinket proc every fight, and across chained fights.**
     - **Kill bonus**, from `Game.RPC_RegisterKill`. It runs on every credited player's client, also when another client owned the creature, and is larger for bosses (`bossNumber > 0`). The RPC carries only the creature's name token (`m_name`), boss number, kill modifiers and attacker count, not its `m_enemyAdrenalineMultiplier`. The bonus is paid only when the name matches an enemy the player fought in this combat (a dummy never enters that list), scaled by that enemy's multiplier.
     - **Hold after combat.** After the last combat event, `m_adrenalineDegenTimer` is held at a configurable value. The starting value is 45 s, long enough to walk to the next lone monster. After the hold, the bar drains slower: scale the amount when `AddAdrenaline` is called from `UpdateStats`.
     - **Starting targets**, to tune: one enemy fills about half of a 60-cost trinket. A group of four or five, or two or three chained lone fights, fill it.
     - Trinket costs range from 10 to 100, so tune together with the Trinket revamp. It can even out the costs through `SharedData.m_maxAdrenaline`.
  5. Small extra: a debug readout (log line or overlay) of the gains per source per fight, for tuning.
  - The bar only feeds the trinket proc: Weapon revamp moves cost no adrenaline (point 3), and boss powers keep their own cooldown (the Boss power revamp is a separate small boost; activation still gives +10).
- **Risks:**
  - Prefab data: the curves `m_adrenalineDegen`, `m_adrenalineDegenDelay` and `m_adrenalineGainMultiplier`, every weapon's `m_attackAdrenaline` and `m_attackUseAdrenaline`, each projectile's `m_adrenaline` and each creature's `m_enemyAdrenalineMultiplier`. Dump them before choosing numbers; the wiki values above are not checked in code.
  - Without a trinket (max 0) nothing changes: `AddAdrenaline` ignores gains when the max is 0.
  - Farming: training dummies are excluded by faction (point 2). A trapped mob is a real enemy, so require recent damage dealt or taken, and cap the income per fight.
  - Context tagging misses `AddAdrenaline` calls from other mods; they pass through unchanged. Animation-speed mods change the measured attack time, which is correct because they also change the hit rate.
  - `Game.m_adrenalineRate` (a world modifier) and SE modifiers still multiply every gain, income included. An example is GP_Fader's +100% (wiki and BossRules' table of the vanilla effects; prefab value, unverified).
  - Mods that change the same gains (give our prefix an explicit Harmony priority; it never skips the call, so the Weapon revamp's parry detector still sees it):
    - KeepAdrenalineLonger: a transpiler on `Player.AddAdrenaline` (flat 15 s decay delay);
    - AdrenalineModifier: a prefix on it that scales gains and decay, and a postfix that scales the decay timer;
    - GrindstoneSkills: a prefix on it that scales block, parry and unblocked-hit amounts;
    - Balrond Battle Flow and Passive_Trinket_Modifiers (a global gain multiplier): no source link, hooks unknown.
  - The MC mod Harpoon Hooks Tames sets `Projectile.m_adrenaline` to 0 when a tame is hooked, and our projectile rule must keep that 0.
  - Adrenaline is not saved (it resets on login), and the tuning depends on each trinket's cost.

### Boss power revamp (Revamp) — keep the vanilla design, make the Forsaken powers slightly stronger
- **Feasibility:** easy. Data edits on the `GP_*` status effects in `ObjectDB`; the optional larger sharing radius is one prefix/postfix pair.
- **Who needs the mod:** technically depends; ships as Both (the server refuses players without the mod and sends its settings to everyone), because it changes combat balance.
  - The cooldown is set on the activator from the activator's own `ObjectDB` (`Player.ActivateGuardianPower` copies `m_guardianSE.m_cooldown` into `m_guardianPowerCooldown`). A shorter cooldown is therefore client-only.
  - The buff is added by hash to every player within 10 m, and each receiver's `SEMan.Internal_AddStatusEffect` looks it up in its own `ObjectDB`. Players with the mod get the boosted duration and values; players without it get vanilla, whoever activated it.
  - Shipped as Both, every player has the mod and the same settings, so the boosted power is the same for the whole group.
- **Assets:** no.
- **Hooks:**
  - `ObjectDB.Awake` (private) / `ObjectDB.CopyOtherDB` postfix: edit `StatusEffect.m_ttl`, `StatusEffect.m_cooldown` and the numeric `SE_Stats` fields of the `GP_*` effects, in place on the shared assets.
  - `Player.ActivateGuardianPower`, for the optional radius. It always returns `false`, so a prefix records whether the power is ready (cooldown 0 and `m_guardianSE` set), and a postfix then adds the SE by hash (`SEMan.AddStatusEffect(int, resetTime: true)`) to the players between 10 m and the new radius, found with `Player.GetPlayersInRange`. Players without the mod receive it too, because the GP effects are vanilla assets.
  - `Player.Load` (optional): clamp a saved cooldown that is longer than the new one.
  - `Player.SetGuardianPower`: no patch, it takes the SE from `ObjectDB`.
  - `SE_Stats.GetTooltipString`: no patch, the power's tooltip reads the edited fields.
- **Sketch:**
  - Vanilla: seven `GP_*` powers, each a 300 s buff with a 1200 s cooldown (BossRules' dump of the vanilla effects, ValheimPlus and ForsakenPowersPlusRemastered defaults; prefab data, verify). Whether the Deep North boss adds a power is unverified (the 1.0.16 SoftRef `manifest_extended` has only these seven `GP_*` assets). On activation a power is shared with players within a hardcoded 10 m, plus +10 adrenaline.
  - The boost uses per-power multipliers with small defaults (starting values, to tune):
    - duration × 1.2;
    - cooldown × 0.8;
    - effect strength × 1.15, on numeric deltas only: the regen multipliers' part above 1, stamina-use modifiers, speed, carry weight, skill levels, damage percentages.
  - Unchanged by default:
    - damage-resistance steps (enum steps are not "slight");
    - the adrenaline modifier (GP_Fader's +100% adrenaline, per the wiki and BossRules' table of the vanilla effects, prefab value unverified, feeds the Adrenaline revamp);
    - the 10 m radius, unless configured.
  - Apply to every SE in `ObjectDB.m_StatusEffects` whose name starts with `GP_`, modded powers included (config list). Work from stored originals, and reapply on config change.
  - Out of scope: passive powers. The sheet only asks for a slight boost, and PassivePowers and ForsakenPowerOverhaul already cover the passive model.
- **Risks:**
  - Mixed installs happen only when the server allows players without the mod (`AllowPlayersWithoutMod`): the same shared power is then stronger for players who have it.
  - The cooldown is saved in the profile (`Player.Save` / `Player.Load`). A running vanilla cooldown stays until it ends, unless it is clamped at load.
  - GP effect names differ from the stat switch in `Player.StartGuardianPower` (`GP_Fader` vs `GP_Ashlands`). Match effects by name prefix, not by that switch.
  - Mods that edit the same effects or fields: ValheimPlus (`guardianBuffDuration` / `guardianBuffCooldown`), ForsakenPowersPlusRemastered, ForsakenPowersRevisited, BossRules, EasyVitals, PassivePowers. The last writer wins, so detect them and step aside.
  - Balance and PvP: keep the defaults small; the server's settings apply to everyone.

### Blood trinket (New) — when it procs, drop to 15% HP for 10 s, then heal back quickly
- **Feasibility:** medium. The effect is one small custom status effect. Most of the work is the new item: prefab clone, recipe, icon and texts.
- **Who needs the mod:** everyone; it ships as Both (the server refuses players without the mod and sends its settings to everyone). A new item prefab must exist on every client (`ObjectDB`, `ZNetScene`) for drops, chests and the trinket visual (`VisEquipment.SetTrinketItem` syncs the prefab name). A player without the mod loses the item. The effect itself is local: the vanilla pop passes the SE object to `SEMan.AddStatusEffect` on the wearer's client, and HP changes reach others through the owner's ZDO `health`.
- **Assets:** yes, an icon at least. The model can be a recoloured clone of a vanilla trinket.
- **Hooks:**
  - `ObjectDB.Awake` / `ObjectDB.CopyOtherDB` (item, recipe, status effect) and `ZNetScene.Awake` (prefab).
  - `Player.AddAdrenaline`: the vanilla pop adds `m_fullAdrenalineSE`, no patch needed.
  - A `StatusEffect` subclass overriding `Setup`, `UpdateStatusEffect`, `ResetTime`, `IsDone` and `Stop`.
  - `Character.SetHealth` (writes only on the owner), `Character.GetMaxHealth` and `Character.Heal`.
  - `Character.RPC_Heal` (private): optional heal block.
  - `Character.ApplyDamage`: optional 1 HP floor, one prefix shared with the Blood stone revamp's last stand.
  - `Attack.ModifyDamage`: the bloodstone bonus, no patch needed.
  - `Humanoid.EquipItem`: the NG+ world-level rule applies.
  - `ItemDrop.ItemData.GetTooltip`.
- **Sketch:**
  1. **Item.** A new trinket cloned from a vanilla trinket prefab, for example `TrinketBloodGoldHealth` (the Neckstabber). It gets its own name, icon and `m_maxAdrenaline` (config; starting value 70, balance unverified), and a proposed Ashlands-tier recipe around Bloodstone (`GemstoneRed`) *(recipe and station to test)*. Its `m_fullAdrenalineSE` is our "blood pact" effect.
     - Cost: 70 is a bit above the 60-cost trinket the Adrenaline revamp tunes against, so it pops a little less than once per fight. The Trinket revamp's cost levelling (its point 5) covers only the vanilla trinkets, so this config is the Blood trinket's cost.
     - Clear `m_equipStatusEffect` on the clone. The source may carry one (the Trinket revamp gives the Neckstabber a passive), and the Blood trinket gets no passive: the Trinket revamp skips trinkets whose proc is not an `SE_Stats`.
  2. **Proc: drop to 15%.** When the bar fills, vanilla `Player.AddAdrenaline` adds our effect on the local player (`SEMan.AddStatusEffect` clones it and calls `Setup`). `Setup` records the current HP and, if it is above 15% of max, sets it to 15% with `Character.SetHealth`. This is not a hit: no armor, no Staff of Protection shield, no unblocked-hit adrenaline loss, and HP never reaches 0. Nor is it a blood-magic payment (`SetHealth`, not `Character.UseHealth`), so the *Blood magic XP* idea pays no XP for it.
  3. **Hold for 10 s.**
     - `UpdateStatusEffect` clamps HP to at most 15% of the current max every tick. Max HP is recomputed from food every second in `Player.UpdateFood`. Food regen, meads and heals therefore cannot lift HP, and neither can the Blood stone revamp's lifesteal.
     - Optionally, block `Character.RPC_Heal` for the local player during the window, so no heal numbers flash.
     - Damage still counts: a hit bigger than what is left kills. That is the trinket's risk.
     - Config option: HP cannot drop below 1 during the window (`Character.ApplyDamage` prefix for the local player). The Blood stone revamp has the same optional last stand: plan one shared prefix (both effects in one mod, or a small Core helper), not two on the same lethal hit.
  4. **Then heal back quickly.**
     - Over 2 to 3 s (config), heal in ticks with `Character.Heal`. The heal returns the HP the trinket took (HP before the proc minus the 15% floor), capped at max.
     - `IsDone` fires after 10 s plus the heal time. If the effect ends early (death), nothing is healed.
     - Override `ResetTime`, so a second proc during the window does not restart it. Vanilla calls `ResetTime` on a running full-adrenaline effect.
  5. **Why it pays.**
     - Bloodstone weapons multiply every hit by `1 + missing HP × m_damageMultiplierPerMissingHP` (`Attack.ModifyDamage`). The wiki gives 0.2% per HP, so +34% at 200 max HP with 15% left. `m_damageMultiplierByTotalHealthMissing` scales with the missing share.
     - Blood-magic health costs are a share of current HP, so casting is cheap in the window.
     - `Attack.m_staminaReturnPerMissingHP` refunds stamina.
     - Without that gear, the window is only a risk. An optional bonus of its own during the window (for example more damage; lifesteal would do nothing while HP is clamped) is a decision for the user.
- **Risks:**
  - Death risk, by design. Other low-HP mods change the balance: GrindstoneSkills (Desperation below 25% HP, Last Stand), UndyingAmulet (cheat death), EpicLoot's low-health effects.
  - `Character.SetHealth` writes only on the owner. The pop runs on the owner, so that is fine, but never call it for other players.
  - `StatusEffect.Clone` is a shallow copy (`MemberwiseClone`). Keep per-instance state (HP before the proc, phase timer) in value fields.
  - New-item risks: uninstalling deletes the item, and players without the mod lose it. The prefab name, recipe and station must be checked at runtime.
  - The proc rate comes from the adrenaline system: tune the cost together with the Adrenaline revamp. Extra trinket slots (MultiTrinket) can pop it together with another trinket.
  - It overlaps the Blood stone revamp's blood rite (pay HP, no regen, optional last-stand ward). During the window HP is clamped at 15%, so that idea's lifesteal and heals do nothing, and both ideas offer the same 1-HP last-stand prefix on `Character.ApplyDamage`: plan one shared prefix and design them together.
  - PvP: 10 s at 15% HP is an easy kill. The server's settings apply to everyone.

### Ballista revamp (Revamp) — turns and shoots faster, aims better, smarter target choice, several trophy targets
- **Feasibility:** medium. Several trophies per ballista is one field (`Turret.m_maxConfigTargets`) that vanilla code already honours, and turn speed is prefab data. The work is a correct aim solution and a target choice that run on the turret's owner, and staying compatible with the many turret mods.
- **Who needs the mod:** everyone.
  - Target choice (`Turret.UpdateTarget`), the fire decision (`Turret.UpdateAttack`) and the shot direction run on the turret's ZDO owner, any nearby client: `Turret.ShootProjectile` fires along `m_eye`'s forward, which `Turret.UpdateTurretRotation` turned on that client. Other clients turn the model for show only.
  - The trophy cap is read on the client that uses the trophy (`Turret.UseItem` → `Turret.SetTargets`, which claims ownership).
  - House rule for gameplay mods: the server refuses players without the mod and sends its settings to everyone.
  - Hand-off: a ballista owned by a client without the mod aims and picks like vanilla. `Turret.ReadTargets` reads the whole stored trophy list (ZDO `targets` + `target{i}`) on every client, so extra trophies keep working there, and a vanilla client that adds a trophy to a full list drops the oldest one, so the list keeps its length.
- **Assets:** no.
- **Hooks:** `Turret.Awake` (postfix: turn tuning and `m_maxConfigTargets` per instance); `Turret.UpdateTurretRotation` (private; prefix that replaces the "has a target" branch: aim point and lead. The clamp to the arc and the `Utils.RotateTorwardsSmooth` call are inline code, not a method to call, so the prefix repeats them, or a small transpiler replaces only the flight-time term); `Turret.IsCoolingDown` (answers false while `UpdateTurretRotation` runs, so the turret keeps tracking during the reload); `Turret.UpdateTarget` (private; prefix-replace the pick, keep `RPC_SetTarget` to everybody); `Turret.UpdateAttack` (private; fire window, line of fire); `Turret.ShootProjectile` (spread); `Turret.RPC_SetTarget`; `Turret.UseItem` / `Turret.SetTargets` / `Turret.ReadTargets` / `Turret.GetHoverText` (trophies); `BaseAI.FindClosestCreature` and `BaseAI.CanSeeTarget` (static; candidates and sight); `BaseAI.HaveTarget` / `BaseAI.IsAlerted` (threat, ZDO data readable on any machine); `Character.GetCenterPoint`, `Character.GetVelocity`.
- **Sketch** (vanilla causes in §12; prefab values from ServersideQoL's 1.0 component dump of `piece_turret`):
  - **Turns faster.** Config multipliers on `m_turnRate` (45 °/s), `m_lookAcceleration` (1.2) and `m_lookDeacceleration` (0.05), set per instance, re-applied on a live config change and restored when the feature turns off. The deceleration term is what makes the last 11° slow: there the speed falls in proportion to the remaining angle.
  - **Shoots more.** Keep tracking during the reload (vanilla freezes the turret for the whole 2 s `m_attackCooldown`, then ramps its turning up again from almost zero). Replace the fixed 1.6° fire window (`m_shootWhenAimDiff` 0.9999 as a quaternion dot) with "the predicted miss at the target is smaller than its radius", so far targets need a tight aim and near ones do not. Keep the cooldown itself, with a config multiplier (default: vanilla).
  - **Better aim.** Flight time from the true 3D distance (vanilla's `Vector2.Distance` reads only x and y), no doubling (`m_predictionModifier` 2), the intercept point iterated two or three times, aimed at `Character.GetCenterPoint` so short creatures are hit too. Compute the direction from `m_eye`: vanilla aims from `m_turretBody`'s pivot while the bolt leaves from `m_eye`, so any offset between the two shifts every shot (size unverified). The turret projectiles have `m_gravity` 0 and `m_drag` 0: bolts fly straight at constant speed and need no drop compensation. Optional: less spread (scale the ammo's `m_projectileAccuracy` inside `ShootProjectile`, restore it in a finalizer).
  - **Smarter target choice.** Score the creatures that `FindClosestCreature`'s rules accept instead of taking the closest:
    - keep the current target while it lives and stays in the arc and in sight (vanilla re-picks every second with no memory, so a closer creature steals the target and the aim restarts);
    - search again at once when the target dies, instead of waiting for the timer;
    - prefer what the ballista can hit: a small turn from the current aim, a low angular speed, a clear line of fire from `m_eye` (raycast);
    - prefer threats: `BaseAI.HaveTarget()` or `IsAlerted()`;
    - skip characters with `Character.m_aiSkipTarget` (the T.W.I.G.) and, by config, passive animals: `FindClosestCreature` checks neither `BaseAI.IsEnemy` nor `m_aiSkipTarget`.
  - **Several targets.** Raise `m_maxConfigTargets` (config, for example 4). Vanilla code already stores, reads and matches any number of trophies, drops the oldest one when full and lists them all on hover. Optional mode (BottleShips' idea): trophy kinds first, other hostiles when none of them is in sight.
  - Small extra, not asked: neighbouring ballistas spread their fire. Clients learn each turret's target through `RPC_SetTarget` (sent to everybody, not stored in the ZDO, so a client that loads the turret later learns it at the next change); the score can penalise a creature that N other ballistas within X m already target.
  - Out of scope: friendly fire (bolts fired with a null owner hit anything, see `Projectile.IsValidTarget`); several existing mods handle it.
- **Risks:**
  - The Ashlands enemy `piece_Charred_Balista` (targets only players and tames) and the trap `fuling_turret` use the same `Turret` component (dump): change only `piece_turret` by default.
  - Prefab values come from a third-party dump and the aim-lag figures are computed from the code: dump `piece_turret` at runtime and measure before tuning.
  - ReBallista, BetterBallistas, Turrets Redo, TurretRevamped, ValheimPlus (`[Turret]`), ZenWorldSettings and BottleShips change the same methods or fields: detect them and turn the overlapping parts off. ValheimFortress's Automated Ballista uses its own component and does not conflict.
  - The aim fix repeats the inline part of `UpdateTurretRotation` or uses a transpiler: re-check it after every game update.
  - `UpdateTarget` and `UpdateTurretRotation` run in `FixedUpdate` on the owner: keep the scoring allocation-free and throttled.
  - Balance: a ballista that rarely misses is strong base defence; keep the reload and the 40-bolt magazine (`m_maxAmmo` 40).

### Mob AI revamp (Revamp) — weak enemies leave strong players alone, a pack flees when its leader dies
- **Feasibility:** medium. Both parts reuse the vanilla flee (`BaseAI.Flee`) and target selection. The work is the multiplayer plumbing (the player's strength and the leader's death must reach whichever client runs each creature's AI) and tuning what counts as "weak".
- **Who needs the mod:** everyone.
  - Each creature's AI runs on its ZDO owner, which can be any nearby client. A client without the mod runs vanilla AI for the creatures it owns.
  - Each player's own client publishes that player's strength in the player ZDO: an AI owner cannot read a remote player's equipment (its slots and inventory only exist on that player's client).
  - A leader's death is only seen on the leader's owner, which must message the owners of the followers.
  - It changes gameplay, so it ships as a Both mod: the server refuses players without it and sends its settings to everyone.
- **Assets:** no.
- **Hooks:** `MonsterAI.UpdateAI` (prefix; keep-away and rout branches), `BaseAI.UpdateAI` (reverse patch, to keep the base update when the prefix skips vanilla), `BaseAI.FindEnemy` (protected; postfix: skip an outclassing player), `BaseAI.CanSenseTarget` (keep away only from a player the creature senses), `BaseAI.Flee` (protected), `BaseAI.SetAlerted` (protected virtual), `MonsterAI.OnDamaged` (postfix: provoked), `BaseAI.OnDeath` (protected virtual; postfix on the leader's owner), `MonsterAI.Awake` (register the rout RPC) with `ZNetView.Register` / `ZNetView.InvokeRPC`, `BaseAI.BaseAIInstances`, `ZDO.GetPrefab`, `Player.GetBodyArmor`, `Character.GetMaxHealth` (ZDO `max_health`), `Humanoid.GetInventory`, `ItemDrop.ItemData.GetDamage`, `HitData.DamageTypes.ApplyArmor`, `Game.GetDifficultyDamageScalePlayer`, `Game.m_enemyDamageRate`, `MonsterAI.IsEventCreature`, `MonsterAI.HuntPlayer`, `Character.IsBoss`, `Character.IsTamed`, `Character.GetFaction` (skip training dummies).
- **Sketch:**
  - Vanilla today:
    - No creature weighs the player's strength. The one precedent is the Crown of Valheim: when a creature's target is a player in crown mode (`Player.InCrownMode`, ZDO `crowned` for remote players), a non-boss creature within `m_crownFearRange` flees from them (`MonsterAI.UpdateAI`). The other flee rules (`m_fleeIfLowHealth`, `m_fleeIfNotAlerted`, `m_fleeIfHurtWhenTargetCantBeReached`, fire, lava) ignore who the player is.
    - There is no leader or group link. `Character.m_group` only makes creatures of the same group friends in `BaseAI.IsEnemy`, and spawn groups are not remembered.
    - `BaseAI.OnDeath` (subscribed to `Character.m_onDeath` in `BaseAI.Awake`) runs only on the dying creature's owner: `Character.CheckDeath` sits in the owner branch of `Character.CustomFixedUpdate`, and `Character.OnDeath` returns early elsewhere. Other clients only see the object destroyed (`ZNetScene.Destroy`).
    - Targets come from `BaseAI.FindEnemy` (closest sensed enemy) in `MonsterAI.UpdateTarget`, every 2 s near a player and every 6 s otherwise; an empty result keeps the current target. A hit (`MonsterAI.OnDamaged`) alerts the creature and targets the attacker if it has no target yet. `BaseAI.Flee` runs when the creature is alerted and walks otherwise (`MoveTo(…, run: IsAlerted())`).
    - A creature with no target is alerted only by a hit, a near-miss projectile (`MonsterAI.RPC_OnNearProjectileHit`), fire, `BaseAI.Alert` (aggravation, offerings, spawn abilities, pheromones) or `HuntPlayer`; sight alerts it only for its current target within `m_alertRange` × the target's stealth factor (`MonsterAI.UpdateAI`).
  - Player strength: each player's client writes its real armor (`Player.GetBodyArmor`, status-effect armor included) to a custom float in its player ZDO about once per second. Max HP is already there (ZDO `max_health`, `Character.GetMaxHealth`). Optionally also publish the equipped weapon's damage, for a "kills it in N hits" check.
  - Weak = its best attack barely hurts that player:
    - Take the highest total damage (without chop and pickaxe) among the creature's inventory items aimed at enemies (`m_aiTargetType` Enemy), times the star factor 1 + 0.5 × (level − 1) (as the private `Attack.GetLevelDamageFactor`), `Game.GetDifficultyDamageScalePlayer` and `Game.m_enemyDamageRate` (as `Character.RPC_Damage` applies them). In NG+ worlds add the flat bonus `Game.m_worldLevel` × `m_worldLevelEnemyBaseDamage` that `HitData.GetTotalDamage` adds for creature attackers.
    - Apply `HitData.DamageTypes.ApplyArmor` with the player's armor. Below a config share of the player's max HP (for example 5%), the creature is weak against that player. Cache per prefab and level.
    - Where the line falls (Boar and Neck against Ashlands gear, Greydwarfs against Mistlands gear) depends on prefab damage values (unverified).
  - Do not attack:
    - A `BaseAI.FindEnemy` postfix on `MonsterAI` owners turns an outclassing player into no target unless the creature is provoked.
    - The `MonsterAI.UpdateAI` prefix also drops a current player target that became outclassing (new gear), since an empty `FindEnemy` result does not clear it. When such a player comes within a keep-away range (for example 8 m) and the creature senses them (`BaseAI.CanSenseTarget`: hearing, or sight scaled by the player's stealth factor), the creature calmly walks away (`Flee` while not alerted), like crown mode but without panic. A crouched player it cannot sense can close in, so the *Sneak revamp*'s standing-still stealth still works on weak creatures. Otherwise vanilla runs (idle, eat, wander).
  - "Except if frightened", read as provoked or cornered:
    - A `MonsterAI.OnDamaged` postfix stores provoked-until in the creature's ZDO (so a new owner keeps it) when the attacker is a player, for example 30 s after the last hit, and the creature fights as in vanilla. A hit from a tame or a training dummy only makes it fight that attacker (vanilla `SetTarget`), not a nearby outclassing player.
    - Cornered: while keeping away, the player stays within 3 m for 3 s, so the creature fights back for the same window.
    - Near-miss projectiles (`MonsterAI.RPC_OnNearProjectileHit`) scare the creature away instead of provoking it.
  - Never affected: bosses (`Character.IsBoss`, faction Boss), tames, raid creatures (`MonsterAI.IsEventCreature`, `HuntPlayer`), training dummies (`Character.GetFaction` returns `Faction.TrainingDummy`), plus a config exclusion list. Dummies run `MonsterAI` and their attacks deal 1 damage (wiki, unverified), so the weak check would mark every dummy weak against every player, and the `FindEnemy` postfix would stop dummies attacking players, which breaks vanilla training.
  - Effect on Sneak XP: weak creatures never target a player who outclasses them, so they usually stay unalerted. Vanilla `Player.OnSneaking` then pays the full 1 XP per second while that player crawls near them (`BaseAI.InStealthRange`: an enemy in range and none alerted), and the first hit on each is a backstab (unalerted victim), which the *Sneak revamp* pays with XP scaled by the victim's max HP with no floor, so almost nothing.
  - Leader down:
    - A config table maps leaders to followers. Default: `Troll`, `Greydwarf_Shaman` and `Greydwarf_Elite` (the Greydwarf Brute; internal ID per the Valheim wiki) lead `Greydwarf` and `Greyling`, and a `Troll` also leads `Greydwarf_Shaman`. Optional: `GoblinBrute` and `GoblinShaman` lead `Goblin`; `Draugr_Elite` leads `Draugr` and `Draugr_Ranged`; `SeekerBrute` leads `Seeker`. The prefab names are checked in the SoftRef `manifest_extended`; the pairings are design choices.
    - A `BaseAI.OnDeath` postfix on the leader's owner looks through `BaseAI.BaseAIInstances` for followers (prefab hash from `ZDO.GetPrefab`) within a radius (for example 30 m), not tamed and, by default, not raid creatures, and invokes a rout RPC on each follower's `ZNetView` with the death position. The RPC reaches the follower's owner.
    - The owner stores rout-until (network time) in the follower's ZDO. While it lasts, the `MonsterAI.UpdateAI` prefix runs the base update (reverse patch of `BaseAI.UpdateAI`: regeneration, alert sync), clears the target, keeps the creature alerted so it runs rather than walks (`Flee` moves at run speed only while alerted), flees from the death point, and returns false. After it (for example 20 s) the creature calms down (not alerted, no target), so the pack does not come straight back.
    - Option: rout only when a player or a tame dealt the killing blow (`Character.m_lastHit`), not after a fall or drowning.
- **Risks:**
  - `MonsterAI.UpdateAI` is long and changes between game versions: skip it only in the keep-away and rout branches, and keep the base update through the reverse patch. TruePassiveMobs (a prefix that skips it), FearMe (a transpiler) and Odin's Ótti patch the same method. Creature overhauls (MonsterDB, star-level mods) change damage and flee fields, which the live check reads anyway.
  - The weak check is an estimate: it ignores resistances and damage over time (poison, fire) unless those are published too, and a creature with one strong special attack may be misjudged.
  - Gameplay: hunting boars and necks for leather and tails becomes a chase once the player outclasses them (bows, or a first hit that provokes).
  - Sneak XP: crawling near weak creatures keeps paying the full vanilla 1 XP per second, where vanilla drops to 0.1 once a creature in range is alerted. Option: a `BaseAI.InStealthRange` prefix on the sneaking player's own client leaves out creatures that are weak against that player; it can run the same weak check, because creatures get their default items on every client (`Humanoid.Start`).
  - A creature that ignores the player can still pick a building target (`MonsterAI.UpdateTarget` with `m_attackPlayerObjects`). Decide whether an outclassing player nearby should also stop that.
  - Nulling the player in `FindEnemy` also hides that player's tames from the creature for that search. Acceptable, or pick the next enemy instead.
  - Only `MonsterAI` creatures are covered. `AnimalAI` creatures already flee from their target (`AnimalAI.UpdateAI`).
  - Mixed installs: a vanilla owner ignores the rout RPC and runs vanilla AI; a player without the mod publishes no strength and counts as not outclassing. Ownership can change during a rout, hence the ZDO keys.
  - Cost: the weak check is cached, but the keep-away scan would run on AI ticks (20 Hz) for many creatures; throttle it (for example every 0.5 s).

### Training dummies revamp (Revamp) — fight hostile creatures in range, ON/OFF switch, a dummy per weapon type (merges More training dummies)
- **Feasibility:** medium. Fighting creatures (one `BaseAI.IsEnemy` rule, a longer alert range and damage scaling) and the switch are easy. The bulk is the per-weapon dummies: clones of `piece_TrainingDummy` with new attack items and vanilla weapon models, on the dummy's own animations. Weapon-specific moves (a spear thrust, a sledge smash, drawing a bow) need new animation clips for the dummy's rig in an AssetBundle, which is hard.
- **Who needs the mod:** everyone.
  - The dummy's AI runs on its ZDO owner, any nearby client: target choice (`MonsterAI.UpdateTarget` → `BaseAI.FindEnemy`), attacks, and the hit filter (`BaseAI.IsEnemy` in `Attack.DoMeleeAttack` / `Attack.DoAreaAttack`, `Projectile.IsValidTarget`, `Aoe.ShouldHit`). The switch is an RPC to that owner. The new dummy prefabs must be registered on every client.
  - Ships as Both (house rule for gameplay mods): the server refuses players without the mod and sends its settings to everyone.
  - Hand-off: a dummy owned by a client without the mod behaves like vanilla (it ignores the OFF flag and attacks only players and tames), and the switch RPC is dropped. Without the mod, the new dummies do not appear (unknown prefab) and vanilla T.W.I.G.s keep an unused ZDO flag.
- **Assets:** no for a first version (the dummy's own animations and icon, vanilla weapon models); yes for weapon-specific animations.
- **Hooks:** `BaseAI.IsEnemy` (static postfix; also filters the dummy's hits in `Attack.DoMeleeAttack` / `Attack.DoAreaAttack`, `Projectile.IsValidTarget`, `Aoe.ShouldHit`); `BaseAI.FindEnemy` (skips `Character.m_aiSkipTarget`); `MonsterAI.UpdateAI` and `MonsterAI.UpdateTarget` (private; OFF state); `MonsterAI.m_alertRange` (per-dummy engage range); `MonsterAI.OnDamaged` / `MonsterAI.SetTarget` (creatures hit back; OFF state ignores hits); `BaseAI.DoIdleSound` (private; silent when OFF); `BaseAI.UpdateRegeneration` (private; optional pause in combat); `Character.Damage` (attacker-side prefix, before `RPC_Damage`); `Character.GetHoverText` (virtual; returns "" for non-tames); `ZNetScene.Awake` / `ObjectDB.Awake` (switch component, clones and attack items); `ZNetView.Register` / `ZNetView.InvokeRPC` (switch RPC to the owner); `PrivateArea.CheckAccess`; `Humanoid.m_defaultItems`, `Humanoid.EquipBestWeapon` (per-weapon attack items); `PieceTable.m_pieces` (Hammer entries); `CharacterDrop.OnDeath` (optional: no loot from dummy kills).
- **Sketch** (vanilla values: §13, from ServersideQoL's 1.0 dump of `piece_TrainingDummy` and the wiki):
  - **Merge.** This idea absorbs More training dummies: the per-weapon dummies below are the new dummies. The older utility variants are not in the new text; they stay optional extras for the same mod, listed under More training dummies.
  - **Fight hostile creatures in range.**
    - Postfix `BaseAI.IsEnemy(a, b)`: when `a` is a `Faction.TrainingDummy` character that is ON and `b` is hostile (not a player, not tamed, has `MonsterAI`, faction not `Players`, `PlayerSpawned` or `TrainingDummy`, `Dverger` only when aggravated), return true. Check the faction first to keep this hot path cheap.
    - The same postfix makes the dummy's hits land: non-player attackers only hit characters that `IsEnemy` accepts.
    - Tames: vanilla already makes them enemies of the dummy (the tamed rule runs before the faction table). A tame never picks a dummy itself (`m_aiSkipTarget`), so Combat pet sparring starts when a dummy hits the tame. Keep this: the "spare tames" config is off by default, so dummies attack tames unless it is turned on.
    - "In range": the dummy cannot walk (`m_speed` 0). It sees 30 m, up to 90° off its facing until alerted (all around once alerted), and hears nothing. `MonsterAI.UpdateAI` only attacks once the dummy is alerted, and sight alerts it only within `m_alertRange` (10 m) × the target's stealth factor, or when it is hit, so a creature at 25 m is ignored until then. Raise `m_alertRange` per dummy (config) so the thrown attack engages at range; melee still needs each attack's reach. Whether it turns in place is unverified (`m_turnSpeed` 0; `m_runTurnSpeed` 300 applies while its AI asks it to run, which `BaseAI.MoveTo` does for an alerted AI).
    - Damage: every vanilla dummy attack deals 1 (wiki). A `Character.Damage` prefix on the attacker's side (the dummy owner, before `RPC_Damage`) sets hits from dummies on non-players to a per-dummy config value; players keep the training damage.
    - Creatures still never pick a dummy on their own (`m_aiSkipTarget` true, skipped by `BaseAI.FindEnemy`), but `MonsterAI.OnDamaged` → `SetTarget(attacker)` has no such check: a creature the dummy hits while it has no target fights back until `FindEnemy` (every 2 s near players) finds another enemy. Dummies draw creatures that are not already busy with a player.
  - **ON/OFF switch.**
    - Add an `Interactable` component to every dummy prefab (`ZNetScene.Awake` postfix) and a hover line (state and key) through a `Character.GetHoverText` postfix. E, gated by `PrivateArea.CheckAccess`, sends an RPC to the owner (`ZNetView.InvokeRPC`), which flips a ZDO bool `<GUID>.Off`. Default ON.
    - OFF, on the owner: a `MonsterAI.UpdateAI` prefix clears `m_targetCreature` and `m_targetStatic` and calls `SetAlerted(false)`; a `MonsterAI.UpdateTarget` prefix skips the search; a `MonsterAI.OnDamaged` prefix keeps training hits from alerting it and giving it a target (otherwise each hit plays the alert effect). `BaseAI.UpdateAI` still runs, so the dummy keeps regenerating and can still be hit for skill training. A `BaseAI.DoIdleSound` prefix mutes it (idle sounds play on every client, so read the ZDO flag there).
  - **A dummy per weapon type.**
    - Clone `piece_TrainingDummy` per weapon family: knives, clubs and maces, axes, two-handed axes and sledges, spears, polearms (atgeirs), fists, bows, crossbows, staffs. The vanilla T.W.I.G. is the sword and shield one.
    - Each clone gets cloned attack items (`TrainingDummy_attack`, `_attack2`, `_attack3`, `TrainingDummy_throw`) with the weapon's damage type (`SharedData.m_damages`), reach (`m_aiAttackRange`, `Attack.m_attackRange`), rhythm (`m_aiAttackInterval`) and projectile (`Attack.m_attackProjectile`: a vanilla arrow, bolt, thrown spear or staff projectile on the throw animation), set as `Humanoid.m_defaultItems`, so `Humanoid.EquipBestWeapon` picks among them by range.
    - Look: attach the vanilla weapon's model to the dummy's hand bone and hide the built-in sword and shield (whether they are separate renderers is unverified). Name, description and recipe per weapon (for example the weapon's material).
    - Register the clones in `ZNetScene` and `ObjectDB` and add them to the Hammer's `PieceTable`. Prefab names are saved in ZDOs, so they are permanent.
  - Optional extras (not asked): TouchGrass-style per-dummy damage settings on the alternate interact key; the utility variants of More training dummies.
- **Risks:**
  - `BaseAI.IsEnemy` is called for every character in every AI search: branch on the faction first.
  - Base defence: 2,500 HP, full regeneration in 30 s (`m_regenAllHPTime` 30) and creatures that retaliate make a row of dummies an almost unkillable wall of decoys. Cap the damage to creatures, optionally pause regeneration while hurt recently (`BaseAI.m_timeSinceHurt`), and limit crowding.
  - Loot farms: loot drops whoever kills (`CharacterDrop.OnDeath` has no killer check; a ragdoll that drops the items drops them instead), so dummies next to a spawner farm trophies unattended. Offer "no drops for dummy kills" (patched on the victim's owner).
  - Dummies killed by creatures refund their materials (`Character.ApplyDamage` → `Piece.DropResources`).
  - Ballistas with no trophy set shoot dummies, in vanilla too (`BaseAI.FindClosestCreature` ignores `m_aiSkipTarget`); the Ballista revamp skips them.
  - Other MC ideas must ignore dummies:
    - Adrenaline revamp: a dummy is an enemy of players, so hitting one pays vanilla adrenaline (`Attack.DoMeleeAttack`: `m_attackAdrenaline` × its `m_enemyAdrenalineMultiplier`, prefab value unverified). A row of dummies, more with one per weapon type, would give free income, a large engaged-enemy count and trinket procs with no danger: that mod ignores `Faction.TrainingDummy` for combat state, engaged enemies and the kill bonus. Weapon hits on a dummy still pay (its point 3), as in vanilla, so a trinket can be tried out on one.
    - Mob AI revamp: dummies run `MonsterAI` and hit players for 1, so its weakness test would mark every dummy weak and stop it attacking players. That mod never affects `Faction.TrainingDummy`.
    - Sneak revamp: an OFF dummy is never alerted, so every hit on it is a backstab (`Character.RPC_Damage`: unalerted AI, once per 300 s per dummy), and sneaking near it pays full vanilla Sneak XP (`BaseAI.InStealthRange`, as near any dummy that has not seen you). That mod gives no sneak-attack XP for `Faction.TrainingDummy` victims.
  - Reused sword animations make a spear or bow dummy look off until it has its own clips.
  - Compatibility: TouchGrass (dummy health, a settings window on the same interact key, night aggro that moves dummies), OdinTrainingPlace, DPS.
  - Prefab facts come from a third-party dump: dump `piece_TrainingDummy` (components, renderers, attack items) at runtime first.

### Increase base HP / Stamina with stats (Revamp) — running, jumping, etc.
- **Feasibility:** easy.
- **Who needs the mod:** client-only (max HP is written to the player's own ZDO `max_health`; stamina is local); server-synced config recommended.
- **Assets:** no.
- **Hooks:** `Player.GetTotalFoodValue(out float hp, out float stamina, out float eitr)` (private postfix), or `Player.m_baseHP`/`m_baseStamina` (public fields) set from `Player.OnSpawned`/periodic update; data from `Player.GetSkills().GetSkillFactor(...)` or `Game.instance.GetPlayerProfile().GetStat(PlayerStatType.DistanceRun/Jumps/...)`; `Hud` food bar uses `GetBaseFoodHP()`.
- **Sketch:** Postfix `GetTotalFoodValue` adding `hp += f(Run, Swim, Blocking skills)`, `stamina += g(Run, Jump, Sneak skills)` with caps; optionally mirror into `m_baseHP` so `GetBaseFoodHP()` and the HUD stay consistent.
- **Risks:** skill loss on death lowers the bonus (feature or not); other food/vitals mods (ValheimPlus food settings, "Better food"-style mods) also postfix `GetTotalFoodValue`/`UpdateFood` — order matters; PvP balance.

### Better tower shields (Revamp) — two-handed immovable wall: very slow, can't parry, huge block armor, small bash with heavy stagger and low damage
- **Feasibility:** medium. Most of it is `SharedData` and `Attack` data that vanilla already honours: two-handed, no parry, block armor, the slow, and the bash's low damage with heavy stagger. The bash reuses the vanilla unarmed combo. The medium part is "blocks virtually anything": unblockable hits, stagger while blocking, AoE launch and knockback.
- **Who needs the mod:** technically client-only. It changes combat balance, so it ships as Both: the server refuses players without the mod and sends its settings to everyone.
  - Equipping, attacks, blocking and movement all run on the shield bearer's own client. The bash's `HitData`, stagger multiplier included, is built on the attacker's client (`Attack.DoMeleeAttack`) and serialized with the hit, so a vanilla victim owner applies it. Incoming hits are resolved on the bearer's client (`Character.RPC_Damage` → `Humanoid.BlockAttack`).
  - Each machine reads `SharedData` from its own prefab. A tower shield handed to a player without the mod (only on a server that lets such players join) is a normal one-handed shield for them, and nothing is stored on the item.
  - The brace slow is a local `SE_Stats`; only status-effect attribute flags go to the ZDO, so nothing new reaches other clients.
  - Under Both, the server's settings apply to everyone, which also keeps PvP fair.
- **Assets:** no. The bash reuses the unarmed attack animation. A dedicated bash animation would need an AssetBundle and the same Animator change on every client.
- **Hooks:**
  - `ObjectDB.Awake` / `ObjectDB.CopyOtherDB` postfix to edit tower shield `SharedData`: `m_itemType`, `m_attachOverride`, `m_timedBlockBonus`, `m_perfectBlockAdrenaline`, `m_blockPower`/`m_blockPowerPerLevel`, `m_movementModifier`, `m_attack` (the bash, with its own `m_staggerMultiplier`, `m_attackStamina` and `m_attackRange`), `m_damages`/`m_damagesPerLevel`, `m_attackForce`, `m_skillType`, `m_blockable`/`m_dodgeable`.
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
  - Keep the wall standing: a stagger ends `IsBlocking()` until it wears off, so the next hits land unblocked. Inside a `BlockAttack` scope, an `AddStaggerDamage` prefix skips or scales the stagger while stamina remains. The damage that still lands adds stagger again in `ApplyDamage`, times the hit's multiplier; if that still breaks the wall, keep the scope open for the rest of that `RPC_Damage` call (prefix plus finalizer). ReliableBlock (a `BlockAttack` transpiler, and its 1.0 fork ReliableBlockRebuilt) and ZenCombat only keep that one hit blocked and still stagger you.
  - Bash: set `m_attack` to a clone of the player's unarmed combo (`Humanoid.m_unarmedWeapon`), the same approach as CaptainValheim. No player exists yet when `ObjectDB` loads, so take it from `Game.m_playerPrefab` or clone it on first use. Its animation already plays in the shield stance and replicates through `ZSyncAnimation`. Low damage with heavy stagger is plain data:
    - A landed hit adds `(blunt + slash + pierce + lightning) × hit.m_staggerMultiplier` to the victim's stagger bar, counted after resistances and armor (`Character.ApplyDamage` → `AddStaggerDamage`), and the victim staggers when the bar reaches max HP × `m_staggerDamageFactor` *(prefab)*. `Attack.DoMeleeAttack` copies `Attack.m_staggerMultiplier` into the hit.
    - So give the shield small flat blunt `m_damages` per tier (not derived from block armor) and a large `m_staggerMultiplier` on the cloned `Attack` (for example 5 to 10, to tune). With `m_skillType = Blocking`, the random skill factor scales both damage and stagger with the Blocking skill, the bash raises Blocking, and `Character.RPC_Damage` counts Blocking hits as melee kills.
    - Keep the multiplier below 100: `Character.RPC_Damage` staggers anything hit with `m_staggerMultiplier ≥ 100` at once, before the dodge and block checks.
    - A moderate `m_attackForce` (on `SharedData`) so enemies stay in reach, a short range and a low stamina cost. A staggered non-player takes ×2 damage from the next hits (`RPC_Damage`), and the bearer gains `m_staggerEnemyAdrenaline` per stagger: the bash is crowd control that sets up allies.
    - `m_blockable`/`m_dodgeable = true`. `Attack.DoMeleeAttack` copies them from the weapon, so without them the bash cannot be blocked or dodged in PvP.
    - `m_skillType = Blocking` also keeps the Weapon revamp off the bash: that idea leaves attacks with skill Blocking vanilla, so its roll and jump attacks never replace the bash or stack their ×2 stagger on its multiplier.
    - The shield has no secondary attack unless `m_secondaryAttack` is set too. Today a vanilla shield with an empty right hand uses the unarmed secondary (a kick per mod READMEs, unverified).
  - Block anything from the front: the `RPC_Damage` prefix marks frontal hits blockable. `BlockAttack` still rejects hits from behind (`Dot(hit.m_dir, forward) > 0`), and a held block also clears the hit's status effect. Filter by `HitType`, not by direction: fall hits use the ground normal as `m_dir`, which passes the facing test.
- **Risks:**
  - Changing `m_itemType` is global and breaks code that looks for `ItemType.Shield`. CaptainValheim's shield techniques check it (seen in its source). EpicLoot's shield enchant rules and auto-equip-shield features such as ZenCombat's or ShieldMeBruh's probably do too (unverified). Fallback: keep `Shield` and enforce two hands with `Humanoid.EquipItem` postfixes plus a `GetCurrentWeapon`/`StartAttack` override, as CaptainValheim does.
  - MC's shared `src/Shared/ItemKinds.cs` (Sort Chest, Crafting Search and Sort) classifies by `m_itemType`, so tower shields would sort as weapons unless it also reads `m_attachOverride`.
  - A tower shield now counts as a weapon (`IsWeapon`), so `Humanoid.Pickup` auto-equips a picked-up one when the right hand is empty, replacing the shield in hand.
  - `ItemStand.CanAttach` matches its supported types against `m_itemType`, so an item stand that lists `Shield` but not `TwoHandedWeaponLeft` would refuse tower shields (stand lists are *(prefab)*, unverified).
  - `EquipItem` enforces hands only at equip time: turning the feature on while a sword and a tower shield are held must unequip the sword.
  - Vanilla block charges (`m_buildBlockCharges`) fire the shield's own `m_attack` through `Attack.StartWithoutAnimation` after `m_maxBlockCharges` blocks. ZenCombat's and CaptainValheim's pages say the mechanism is off in vanilla. CaptainValheim's config comments give the vanilla tower shield values as 5 charges, 4 s decay and a 0.5 blocking decay factor (*(prefab)*, unverified). A mod that turns it on would fire our bash as the counter.
  - The heavy stagger only comes from hits that land. A blocking target takes stagger in `Humanoid.BlockAttack` from the damage left after block armor, without the hit's multiplier. `Character.ApplyDamage` returns before the stagger step when the hit deals 0.1 or less (after resistances, armor and `Game.m_playerDamageRate`), blunt-resistant targets take less, and creatures with `m_staggerDamageFactor ≤ 0` never stagger from damage (which ones is *(prefab)*, unverified). If the cloned unarmed combo has several chain levels *(prefab)*, its last hit gets the vanilla ×2 damage, and so ×2 stagger.
  - Walk and crouch speeds (`m_walkSpeed`, `m_crouchSpeed` in `Character.UpdateWalking`) ignore the equipment modifier. With the code defaults (`m_speed` 10, `m_walkSpeed` 5), a modifier below -0.5 makes walking faster than jogging. The run factor reaches 0 at -0.67 and the jog factor at -1. Below that, `UpdateWalking` multiplies the move direction by a negative speed, which nothing clamps unless an `SE_Stats` is active. An SE `m_speedModifier ≤ -1` roots the player and turns `Jump` into a weak tired jump (`m_jumpForceTiredFactor`).
  - Not everything reaches `BlockAttack`:
    - Hits with `m_staggerMultiplier ≥ 100` stagger the victim in `RPC_Damage` before the block check.
    - `Aoe.m_blockable` is `false` by default in code, and `Fire` (CinderFire) hits are unblockable.
    - `Aoe.OnHit` applies `m_launchCharacters` (`Character.ForceJump`) and `m_knockBackForce` (`ApplyPushback`) outside `RPC_Damage`. A networked AoE runs this on its owner's machine, so the bearer's client may not be the one applying them.
    - DoTs, lava, falls and drowning skip blocking.
  - Vanilla block, movement and parry values of tower shields are *(prefab)*. The wiki lists -10% movement for tower shields (-5% for other shields). Make Tower Shields Great Again's page says -20% (unverified).
  - Other mods patch the same code: CaptainValheim (`BlockAttack` prefix and transpiler, `GetCurrentWeapon`, `StartAttack`, `Pickup`, `RPC_Damage`), ReliableBlock and ReliableBlockRebuilt (`BlockAttack` transpilers), Combat Adjustments (`Character.GetStaggerTreshold` postfix), GCO and ZenCombat (closed source), EpicLoot (`GetBlockPower`, unverified). The MC mods Crossbow Stays Loaded (`BlockAttack` postfix, `UnequipItem` prefix, `GetTooltip` postfix), Harpoon Hooks Tames (`RPC_Damage` prefix/postfix) and Forge Idol Upgrades (`ObjectDB.Awake`/`CopyOtherDB` and `GetTooltip` patches, idols only) need cross tests.
  - PvP balance: huge block armor plus a staggering bash. The server settings (Both) keep the values equal for everyone.
  - Adrenaline: every stagger the bash causes pays the bearer `m_staggerEnemyAdrenaline` × the victim's `m_enemyAdrenalineMultiplier` (`Character.AddStaggerDamage`), so a cheap bash that staggers often is a steady adrenaline source. Tune it with the Adrenaline revamp, which rewrites the stagger amount.
  - `RPC_Damage` is a hot, long method: prefix (plus a finalizer to close scopes) only, no transpiler.
  - `SharedData` edits must be re-applied on every ObjectDB init (`Awake` and `CopyOtherDB`).

### Dual wielding (New) — a one-handed weapon in each hand, attacking with the vanilla dual-axe and dual-knife moves
- **Feasibility:** medium. Vanilla already has the moves: the Berserkir axes (`AxeBerzerkr`) and Skoll and Hati (`KnifeSkollAndHati`) are single items whose attacks play the player's dual-axe and dual-knife animation sets (`player_DualAxes.fbx` and `player_DualKnives.fbx` in the SoftRef `manifest_extended`). A pair of one-handed weapons can reuse those triggers, and each hit can come from either weapon through the per-swing `Attack` clone. The medium part is the equipment bookkeeping: vanilla never keeps two one-handed weapons, so equipping, hiding and showing, eating, loading, death, torches and shields all need care. New animation clips would make it hard.
- **Who needs the mod:** technically client-only. Under the house rule that experience-changing mods ship as Both, it ships as Both: the server refuses players without the mod and sends its settings to everyone.
  - Equipment lives on the player's own client. `Humanoid.SetupVisEquipment` writes both hand items to the player ZDO (`VisEquipment.SetLeftItem` / `SetRightItem`), and every client, with or without the mod, attaches the left weapon to `VisEquipment.m_leftHand` (`VisEquipment.AttachItem`).
  - The dual moves are vanilla triggers, which `ZSyncAnimation.SetTrigger` sends to everybody, and the stance is an animator parameter the owner writes to the ZDO (`ZSyncAnimation.SetInt` / `SetFloat`), like any vanilla stance. Every client plays both.
  - Hits are built on the attacker's client (`Attack.DoMeleeAttack`, with the `HitData` of the weapon that hits) and resolved by the victim's owner with vanilla rules. A block is resolved on the blocking player's own client.
  - Only cosmetics are per client: the left weapon's trail and a second back slot. Players without the mod (only on a server that lets them join) see no left trail and both sheathed weapons on one back joint.
  - Under Both, the server's settings apply to everyone, which also keeps PvP fair.
- **Assets:** no. The pair plays the vanilla dual-axe and dual-knife animations. Custom clips (Smoothbrain's DualWield) need an AssetBundle and an Animator override on every client.
- **Hooks:**
  - `Humanoid.EquipItem` (prefix for the dual cases only) and `Player.ToggleEquipped` (protected override; read the main-hand key when the player asks, because items with `m_equipDuration` are equipped later by `Player.UpdateActionQueue`).
  - `Humanoid.UnequipItem`, `Humanoid.UnequipAllItems` (also the death path: `Player.OnDeath` → `Player.CreateTombStone`), `Humanoid.HideHandItems`, `Humanoid.ShowHandItems` (protected), `Humanoid.SetupEquipment` (private), `Player.EquipInventoryItems` (private; the load order).
  - `Humanoid.SetupAnimationState` (private postfix) with the private `Humanoid.SetAnimationState`.
  - `Attack.Start` (prefix on the per-swing clone), `Attack.OnAttackTrigger` (prefix and postfix: which weapon hits), `ZSyncAnimation.HasParameter`, `ObjectDB.GetItemPrefab` (read the vanilla dual items).
  - `Humanoid.GetCurrentBlocker` (private; optional postfix), `Humanoid.GetCurrentWeapon` (read only).
  - `VisEquipment.SetWeaponTrails` (postfix), `VisEquipment.AttachBackItem` (private postfix), `VisEquipment.Awake` (private; create a left back joint).
  - `ItemDrop.ItemData.m_customData` through `MC.Shared.ItemDataExtensions` (the off-hand marker).
- **Sketch:**
  - Vanilla today:
    - `Humanoid.EquipItem` puts a `OneHandedWeapon` in the right hand and unequips a left item that is not a `Shield` or `Torch`, so two one-handed weapons never stay equipped (a torch in the right hand moves to an empty left hand first). A torch goes to the left hand only when the right hand holds a one-handed weapon and the left hand is empty; otherwise it empties the right hand (and a left item that is not a shield) and takes the right hand.
    - Attacks use `Humanoid.GetCurrentWeapon`: the right item, else a left weapon that is not a torch. `Humanoid.StartAttack` clones that weapon's `m_attack` or `m_secondaryAttack` and passes the weapon to `Attack.Start`.
    - Blocking uses the private `Humanoid.GetCurrentBlocker`: the left item first, else the current weapon. A player without a shield already blocks and parries with a weapon.
    - The vanilla dual weapons are one item each: the wiki lists the Berserkir axes as dual wielded (a 4-step combo of 6 hits, and a leap as secondary) and Skoll and Hati as a two-handed knife (a 3-hit combo and a leap). No code splits them over two hands, so the second blade comes from the prefab (probably an `attach_skin` child that `VisEquipment.AttachItem` binds to the body's bones; unverified).
  1. **Equip rules**, for the local player only. Eligible weapons: `OneHandedWeapon` items with skill Swords, Axes, Clubs or Knives and a melee primary attack, minus a config exclusion list. Spears stay out, as in Smoothbrain's DualWield: their secondary attack throws the weapon. (DualWielder lets a spear keep its own secondary, and balrond DualMastery lets a spear take over as a vanilla spear.)
     - An eligible weapon equipped while the main hand holds one, and the off hand is empty or holds one, goes to the off hand (replacing the off-hand weapon). Holding a config key when the player equips it puts it in the main hand instead. A swap key swaps the pair (DualWielder has one).
     - An eligible weapon equipped while the main hand is empty and the off hand holds one goes to the main hand and keeps the pair. Vanilla would unequip the off-hand weapon here. Eating relies on this: `Humanoid.SetUseHandVisual` hides only the right hand (`HideHandItems(onlyRightHand: true)`), and `ShowHandItems` re-equips it afterwards.
     - A shield keeps the vanilla rule: it replaces the off-hand weapon and keeps the main hand. A torch replaces the off-hand weapon, where vanilla would empty both hands. Bows, two-handed weapons and tools stay vanilla.
     - The prefix acts only in these cases. It repeats the short vanilla guards (already equipped, not in the inventory, attacking or dodging, swimming off the ground, broken, missing DLC), sets the hand, and finishes as vanilla does (`m_equipped`, clearing `m_hiddenRightItem` and `m_hiddenLeftItem`, the private `SetupEquipment` and `TriggerEquipEffect`, the item's `m_equipEffect` at the hand). Every other case runs vanilla. Smoothbrain's DualWield uses an `EquipItem` transpiler at the `OneHandedWeapon` test instead.
  2. **Keep the pair consistent:**
     - When the main hand empties while the off hand holds a paired one-handed weapon, move that weapon to the main hand. Otherwise `GetCurrentWeapon` returns it, and it swings with right-hand clips from the left hand. Smoothbrain's DualWield does this in a `SetupEquipment` prefix.
     - Skip the move inside `EquipItem`, `UnequipAllItems` and `HideHandItems` (a depth counter set in their prefixes and cleared in finalizers). `EquipItem` and `UnequipAllItems` unequip the right item before the left one: a weapon moved in between would stay flagged as equipped in no slot (`EquipItem` then overwrites the right hand), or stay equipped at death, where `Inventory.MoveInventoryToGrave` leaves equipped items on the player instead of the tombstone. `HideHandItems(onlyRightHand: true)` (eating) empties only the right hand, and a move there would swap the hands after the meal. (`Player.UnequipDeathDropItems` also unequips right then left, but nothing calls it in 1.0.16.)
     - Hide and show (the hide key, crafting stations through `Player.SetCraftingStation`, attaching with hidden weapons through `Player.AttachStart`, or `Humanoid.UpdateEquipment` while swimming off the ground): `ShowHandItems` re-equips the hidden left item before the right one, which the rules above would turn into swapped hands. A prefix restores the pair in order.
     - Load: `Player.EquipInventoryItems` re-equips the items flagged `m_equipped` in inventory order. An off-hand marker in `m_customData` (`"{ModInfo.Guid}.OffHand"`, set when an item enters the left hand, cleared when it leaves) lets a prefix equip the main hand first. Vanilla ignores the key.
     - Turning the feature off live unequips the off-hand weapon, so the state stays one that vanilla allows.
  3. **Stance:** a `SetupAnimationState` postfix sets the dual stance when both hands hold eligible weapons: the `m_animationState` of the same vanilla dual item the attacks use (`AxeBerzerkr`, or `KnifeSkollAndHati` for two knives), read at runtime (probably `DualAxes` for `AxeBerzerkr`; prefab data). Vanilla would use the left item's state (`OneHanded` for most one-handed weapons, unverified): the one-handed idle and block poses with a second weapon hanging in the left hand. DualWielder leaves it that way.
  4. **Attacks:** an `Attack.Start` prefix edits the per-swing clone while the pair is held:
     - Keep the main-hand weapon's `Attack` (range, angle, height, special hit skill, stagger and force multipliers, costs) and copy only the animation fields from the vanilla dual item: `AxeBerzerkr` for any pair, `KnifeSkollAndHati` for two knives. `m_attackAnimation`, `m_attackChainLevels` and `m_attackRandomAnimations` come from its `m_attack` (or `m_secondaryAttack`, when the clone's animation is the weapon's secondary one) at runtime, so no trigger name is hardcoded (DualWielder's source uses `dualaxes`, `dual_knives`, `dualaxes_secondary` and `dual_knives_secondary`; unverified in the game data). Check each trigger, with its chain index, with `ZSyncAnimation.HasParameter` once the local player exists, and fall back to vanilla.
     - `Attack.Start` then fires `m_attackAnimation` plus the chain level, and the combo continues because every dual swing carries the same animation name.
     - Stamina: set the clone's `m_attackStamina` before `Start` checks it: the main hand's cost × a config factor. Tune it against the Berserkir axes, which cost 16 stamina per swing (wiki).
     - Secondary: the vanilla dual item's leap by default, or the main-hand weapon's own secondary by config (its trigger may have no transition from the dual stance; Animator data, unverified). The leap keeps the other fields of the main-hand secondary (attack type, range), so check that they fit it.
  5. **Which weapon hits:** each `Hit` animation event runs `Attack.OnAttackTrigger` once (`CharacterAnimEvent.Hit` → `Humanoid.OnAttackTrigger`, owner only), and `Attack.DoMeleeAttack` reads everything from the clone's `m_weapon`: damage (`ItemData.GetDamage`), status effect and its chance, backstab bonus, tool tier, push, `m_blockable` and `m_dodgeable`, the skill it raises and the durability it uses.
     - A prefix sets `m_weapon` to the main-hand or the off-hand weapon, alternating per event and restarting with the combo (the wiki describes the dual-axe combo as one hit with each axe, then two double hits; DualWielder alternates per chain step), and applies an off-hand damage factor through the clone's `m_damageMultiplier`, which `Attack.ModifyDamage` applies (config; balrond DualMastery uses 50% to 75%). A postfix restores the main hand, so vanilla `UnequipItem` still stops the swing when the main weapon leaves. If the off hand is empty by then, the main hand hits.
     - So a frost sword and a poison axe each apply their own status effect, and each weapon trains its own skill and wears its own durability. The last chain step's ×2 damage and ×1.2 push (`DoMeleeAttack`, when `m_attackChainLevels > 1`) apply to each of its hits.
  6. **Blocking:** vanilla already blocks and parries with the off-hand weapon (`GetCurrentBlocker`), with its own block armor, parry bonus, damage modifiers and block adrenaline. The low block armor of one-handed weapons is the price of having no shield. Option: block with the better of the two (a `GetCurrentBlocker` postfix).
  7. **Cosmetics:** a `VisEquipment.SetWeaponTrails` postfix also lights the left instance's trails (vanilla only lights the right one unless `m_useAllTrails`). `VisEquipment.AttachBackItem` puts every one-handed weapon on `m_backMelee`, whichever hand it came from (only a torch gets a separate joint), so a sheathed pair overlaps: add a mirrored joint, as DualWielder does by cloning `m_backMelee` in a `VisEquipment.Awake` postfix.
  8. **Already handled by vanilla:** `Player.UpdateModifiers` sums the equipment modifiers of both hands (two slow weapons slow twice), and `Humanoid.UpdateEquipmentStatusEffects` adds both equip status effects (a `HashSet`, so an identical pair counts once). No new skill is needed. An off-hand skill like balrond DualMastery's or Smoothbrain's would need a custom skill (Jotunn `SkillManager` or a `Skills.IsSkillValid` patch; see *Combat pet*).
- **Risks:**
  - Animator: the dual triggers and stance were made for the vanilla dual items. Whether each trigger leaves the dual stance, a jump or a dodge cleanly, and where each clip places its `Hit` events, is Animator data (unverified). The clips fit axes and knives: long swords and maces may clip through the body, and the hit geometry stays the main weapon's. Dual sword mods (AlexDrake's and SunRay's DualSwords) already play the dual-axe set with sword blades; SunRay warns that positioning is not perfect in every animation. Test every family and mixed pairs.
  - Grip: a one-handed weapon's `attach` child is made for the right hand, and `VisEquipment.AttachItem` places it on the left hand joint with the same offset, so the blade may face the wrong way (a torch, which goes in either hand, uses the same child; unverified for weapons). Neither DualWielder's nor Smoothbrain's source corrects the hand grip. A correction in an `AttachItem` postfix only helps players with the mod.
  - Balance: the dual-axe combo lands 6 hits in 4 steps (wiki), against 3 in a sword combo. Each hit carries its weapon's damage (times the off-hand factor), a status effect roll, stagger, adrenaline (`m_attackAdrenaline` per enemy hit) and skill XP (×1.5 when a character was hit), and there is no shield. Two axes also chop a tree on every hit. Tune the off-hand factor, stamina and speed against the Berserkir axes and a sword and shield with valheim-dps. The server settings (Both) keep the values equal for everyone.
  - Equipment edge cases: hide and show, eating, swimming, crafting stations, loading, death and tombstones, a swap during an attack (`EquipItem` refuses while `InAttack`), the equip delay (`m_equipDuration`) and the live toggle. Test each with a pair, a pair plus a torch, and a weapon plus a shield.
  - `Humanoid.EquipItem` also runs for creatures (`Humanoid.EquipBestWeapon`): act only for `Player.m_localPlayer`.
  - Other mods: Smoothbrain's DualWield (transpilers on `EquipItem`, `ShowHandItems` and `DoMeleeAttack`; prefixes on `SetupEquipment`, `Attack.OnAttackTrigger` and `ZSyncAnimation.RPC_SetTrigger`), DualWielder (`EquipItem`, `UnequipItem` and `Attack.Start` prefixes, `VisEquipment.Awake` and `AttachBackItem` postfixes, a `Player.SetControls` postfix for its swap key), balrond DualMastery (its page says it is not compatible with other dual wield mods) and Valheim Ascended's off-hand slot. Detect them and turn the feature off with a message. EpicLoot and weapon packs patch `Attack`: their per-weapon effects would follow `m_weapon` per hit (unverified).
  - Our ideas: the *Weapon revamp* edits the same clone in an `Attack.Start` prefix (run this one first, and let it pick its context moves from the dual set). The *Adrenaline revamp* times swings by the weapon's own animation, which a dual swing does not use, and pays per hit. The *Better tower shields* two-handed rule empties both hands under vanilla rules. MC Crossbow Stays Loaded patches `Humanoid.UnequipItem` (prefix) and `Attack.OnAttackTrigger` (postfix) for reload weapons only, which are never paired: cross test.

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
  - It also overlaps with the *Blood trinket*: its 10 s HP clamp at 15% cancels this lifesteal and any heal, and both ideas offer the same optional 1-HP last-stand prefix on `Character.ApplyDamage`, so plan one shared prefix.

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

### More training dummies (New) — merged into Training dummies revamp
- **Feasibility:** easy for what is left here; the new dummies themselves are planned in Training dummies revamp.
- **Who needs the mod:** everyone (new piece prefabs). Ships as Both inside the Training dummies revamp mod (house rule: the server refuses players without the mod and sends its settings to everyone).
- **Assets:** no.
- **Hooks:** as in Training dummies revamp; for the extras: `Character.m_damageModifiers` (resistance dummy), `Humanoid.m_defaultItems` (parry trainer's attack items), `Character.m_onDamaged` and `Character.SetDPSDebug` (DPS readout), `ArcheryTarget.OnProjectileHit` (archery scoring).
- **Sketch:** The sheet row Training dummies revamp merges this idea into it: its "a dummy per weapon type" is the new set of dummies. What is not covered there stays here as optional extras for the same mod (more clones of `piece_TrainingDummy`): a resistance or weakness dummy (configurable `m_damageModifiers`), a parry trainer that swings on a fixed, telegraphed rhythm (cloned attack items with a set `m_aiAttackInterval`), a DPS readout from `m_onDamaged` (TouchGrass and DPS already have one), a moving archery target with `ArcheryTarget` scoring, and a tall dummy.
- **Risks:** same as Training dummies revamp (dummies are Characters: the `IsEnemy` hot path, material refunds through `Character.ApplyDamage` → `Piece.DropResources`). A separate backlog row risks planning the same dummies twice.

### Combat pet (New) — Animal Handling skill, pets gain XP and stars (up to 5), one combat ability per star
- **Feasibility:** hard. Each part is small on its own (XP in the pet's ZDO, `Character.SetLevel`, a custom skill, star UI), but multiplayer ownership forces an RPC on every XP path, and about 25 abilities (5 stars × Wolf, Boar, Lox, Asksvin, Moose) must be built from each animal's existing attacks and balanced. Medium for a first version whose abilities are passive status effects only.
- **Who needs the mod:** everyone. The pet's AI, attacks, XP and level-ups run on its ZDO owner, which can be any nearby client (`ZDOMan.ReleaseNearbyZDOS`), not only its handler. Ability items, status effects and AoE prefabs must be registered on every client (`ZNetScene`/`ObjectDB`): an attack's status effect is added by hash on the victim's owner. Each client draws stars 3-5 and their tint itself. The server should also carry the mod for config sync.
- **Assets:** none required. `Skills.SkillDef.m_icon` takes any `Sprite`, so a vanilla icon (for example a trophy's) works and a custom skill icon is optional. Ability VFX/SFX can clone vanilla prefabs, and stars 3-5 can reuse the vanilla HUD star.
- **Hooks:** `Character.SetLevel` (no owner check, so call it on the ZDO owner; writes ZDO `level`, calls the private `SetupMaxHealth` = `GetMaxHealthBase() × level`, fires `m_onLevelSet`); `Attack.GetLevelDamageFactor` (private; `1 + 0.5 × (level − 1)`, so 3.5× at level 6); `Character.RaiseSkill` (virtual; a tame's own hits call it from `Attack.DoMeleeAttack`/`DoAreaAttack`, `Projectile.OnHit` and `Aoe`, and it forwards to the followed player's skill only when `Tameable.m_levelUpOwnerSkill` is not `None`); `Character.OnDeath` (runs on the victim's owner; the killer is `m_lastHit.GetAttacker()`); `Character.GetRandomSkillFactor` (for non-players, the ZDO float `RandomSkillFactor`: it multiplies `Attack` melee, area and projectile hits, while `Aoe` uses it only for player owners); `Tameable.Awake` (register RPCs), `Tameable.GetHoverText`, `Tameable.Interact` / `Tameable.RPC_Command`; `Humanoid.GiveDefaultItems` / `Humanoid.EquipBestWeapon` / `BaseAI.CanUseAttack`, `SEMan.AddStatusEffect`; `Skills.IsSkillValid` / `Skills.GetSkillDef` (or Jotunn `SkillManager`); `EnemyHud.ShowHud` / `EnemyHud.UpdateHuds`, `LevelEffects.SetupLevelVisualization`; `Procreation.Procreate`; `BaseAI.IsEnemy` / `BaseAI.FindEnemy` / `MonsterAI.OnDamaged` (training dummies: sparring starts when a dummy hits the pet); `ZNetScene.Awake` (`m_commandable`, prefab edits), `ObjectDB.Awake`.
- **Sketch:**
  - Stars = vanilla level. A pet's stars are its `Character` level (ZDO `level`: 1 = no star, 6 = 5 stars), and vanilla already scales HP and damage from it on whichever machine owns the pet. Wild animals keep their spawn level when tamed (`SpawnSystem.GetLevelUpChance`: 10% per extra level by default, times `Game.m_enemyLevelUpRate` and a biome-sector multiplier; world level uses its own formula). So a wild 1★ or 2★ animal has abilities 1-2 from the moment it is tamed, as the idea asks.
  - XP is a custom ZDO float on the pet, written only by its owner. Sources: per hit (`Character.RaiseSkill` postfix, tamed non-players only); a kill bonus scaled by the victim's biome, level and boss flag (`Character.OnDeath` postfix, sent with `ZNetView.InvokeRPC` to the pet's owner through an RPC registered in a `Tameable.Awake` postfix); and sparring with a T.W.I.G. dummy at a reduced rate with a daily cap (tames treat dummies as enemies through the tamed branch of `BaseAI.IsEnemy`, but `BaseAI.FindEnemy` skips `Character.m_aiSkipTarget` characters and the dummy prefab sets that flag, unverified: third-party 1.0 dump, see §13; so a tame never picks a dummy itself: sparring starts when a dummy attacks the tame, which fights back through `MonsterAI.OnDamaged` → `SetTarget` when it has no target yet. According to the code, vanilla dummies already attack tames (not tested), and the Training dummies revamp keeps that by default). At each threshold the owner calls `SetLevel(level + 1)` and broadcasts the new level (see Risks).
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
