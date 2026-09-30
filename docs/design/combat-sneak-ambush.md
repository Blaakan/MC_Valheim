# Sneak Ambush — design

| | |
|---|---|
| Mod | Sneak Ambush |
| GUID / project | `MC.Combat.Sneak.Ambush` (`src/Combat/Sneak.Ambush/`, root namespace `MC.Combat.SneakAmbushMod`, package `SneakAmbush`) |
| Category / scope | Combat / Revamp |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Sneak revamp` |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/`; runtime data dump of 1.0.16 from a Debug self-test (items, creatures, status effects, player animator) on 2026-09-29; asset-level values read from the game bundles by the research briefs (marked "asset read" below); sources of SecondaryAttacks, SmartSkills, SetUpSkills, Sneaky Viking, ImpactfulSkills and Valheim Legends read on GitHub (2026-09-29); sibling design docs (Creature Morale, Tower Shield Wall, Dual Wielding, Weapon Moveset) read on 2026-09-30, and again after that day's reworks of the four (Creature Morale's afraid creatures, the slower tower bash, the queued swap and crossed sheathed pair, the roll attack that flows out of the roll) |
| Status | Implemented (v0.1.0 code), in-game testing |

## Goal

The user's expected behaviours (idea sheet: "Gain XP on sneak attack. Make sneak much more efficient when not moving at
all."; chat, 2026-09-29), numbered. Each one is scope and has at least one test (section 8).

1. **G1 — Sneak attacks give Sneak XP.** A vanilla sneak attack (the backstab: a hit on a creature that is not
   alerted) raises the attacker's Sneak skill. Vanilla gives no XP for it.
2. **G2 — Sneaking is slightly stronger early in the game, with the same end-game power.** At low Sneak skill the
   player is a bit harder to see; at Sneak 100 the stealth curve is exactly vanilla.
3. **G3 — Holding still is very effective.** A crouched player who does not move is much harder to notice: creatures
   walking close by notice them later.
4. **G4 — Fog and bushes say what they do.** The game shows the player what stealth bonus fog and bushes give. Where
   vanilla gives none (fog), the mod creates one; where vanilla already has one (bushes block sight and shade), the
   mod shows it.
5. **G5 — Smoke Screen.** A new item, `Smoke Screen`, with its own early recipe. Thrown, it makes a cloud of smoke.
   It does no damage, does not choke anyone and makes no noise that alerts creatures.
6. **G6 — The cloud hides players.** A player inside the cloud is completely hidden from creatures outside it; a
   player outside is completely hidden from creatures inside it.
7. **G7 — Attacks reveal.** A creature the player hits becomes aware of that player and sees them through the smoke.
8. **G8 — The burst blinds creatures that are already after someone.** The smoke temporarily removes the sight of
   creatures that were already aggroed, so they lose the player.
9. **G9 — Health bars.** A player outside the cloud cannot see the health bars of creatures inside it.
10. **G10 — Bosses are unaffected** by the smoke (they see and are seen normally, their bars stay).
11. **G11 — The item survives the toggle.** The Smoke Screen item and its networked objects stay registered on every
    game and on the dedicated server while the feature is turned off; only the recipe and the hiding follow the
    toggle. Uninstalling the mod removes Smoke Screens from inventories, and the README says so (user decision).
12. **G12 — House rules.** Required on the server and every player, server settings for everyone, live toggle, no
    crash when anything is missing.

### Added beyond the request (small)

- **E1** Smoke between two points outside it also blocks sight (a creature cannot see a player through the cloud).
- **E2** A setting hides health bars both ways (a player inside the smoke does not see the bars of creatures outside
  either). The default is the literal one-way rule (decision D17).
- **E3** Status icons for holding still, the Mistlands mist and standing in smoke, next to the fog and foliage icons
  of G4 (one small mechanism shows all of them).
- **E4** Sneak attacks with bows, crossbows and thrown weapons pay half XP (from the backlog idea's approach).
- **E5** A Smoke Screen cannot be thrown while the feature is inactive (turned off, or a server without the mod);
  the player gets a short message instead of losing the bomb.
- **E6** A short "Sneak attack!" message top left with the Sneak progress gained, so G1 can be seen (personal
  setting, on by default).

### Non-goals

New animations; noise changes; sneak speed; backstab damage or the vanilla 300 s backstab cooldown itself; stealth
while standing up (vanilla: a standing player's stealth factor is 1); grass cover; changes to how creatures see other
than through the smoke; keeping sleeping creatures asleep; hiding tames or other creatures in smoke; hiding players
from turrets (ballistas) or any PvP concealment; making the vanilla Smoke Bomb hide players; choking, damage or fire
extinguishing by the Smoke Screen.

### Later (cut from v1, one-line sketches)

- **Moving creatures notice less:** an AI-side factor on the instance `BaseAI.CanSenseTarget` from the observer's own
  speed (the other reading of G3's parenthesis, decision D8).
- **Early-game sneak stamina:** a sneak-stamina cut that fades out at Sneak 100 (for example −20% drain at Sneak 0),
  in a `SEMan.ModifySneakStaminaUsage` postfix on the local player (open question 8, decision D27).
- **Light cue:** a "Dark / Dim / Lit" status icon from a cached `StealthSystem.GetLightFactor` (the tree-crown part is
  already in v1's In foliage cue).
- **Fair light in multiplayer:** compute the light level without `LightLod` limits, so graphics settings stop changing
  stealth (vanilla quirk, research brief 1.3).
- **Grass cover:** a small bonus from world data (`Heightmap.GetVegetationMask`, `IsCleared`, biome), never from the
  rendered grass.
- **Vanilla Smoke Bomb option:** `VanillaSmokeBombHides` (default off) that also spawns our hiding cloud.
- **Own icon:** a tinted or shipped Smoke Screen icon; translation tokens for the item and cue names.
- **Crosshair through smoke:** skip smoke-hidden creatures in `Player.FindHoverObject`, as vanilla does for mist.
- **Blind and reveal in the creature's ZDO,** so they survive an ownership change.
- **Stillness that grows with Sneak skill;** Sneak-scaled cloud size or duration.
- **Raids fooled too:** a `BaseAI.FindEnemy` postfix that stops the `HuntPlayer` fallback from picking a hidden player.
- **Sleepers stay asleep:** a `MonsterAI.UpdateSleep` prefix that skips the wake-up while the closest player is
  smoke-hidden from the sleeper.

---

## 1. Vanilla behaviour

Only what the design relies on. `File.cs:line` = `.ref/decompiled/assembly_valheim`. "Dump" = the 1.0.16 runtime
dump; "asset read" = values the research read from the game bundles with a scratch parser (not yet confirmed at
runtime; each has a self-test check).

### 1.1 The stealth factor (player's own client)

- `Player.FixedUpdate` calls `UpdateStealth(fixedDeltaTime)` only in the owner branch (`Player.cs:844`), so each
  player's own game computes that player's factor.
- `Player.UpdateStealth` (`Player.cs:6947-6975`): every 0.5 s (`m_stealthFactorUpdateTimer > 0.5f`, reset to 0) it
  recomputes `m_stealthFactorTarget`:
  - crouching (`IsCrouching`, the animator tag `crouch`, `Player.cs:6239`):
    `Lerp(0.5 + 0.5·LF, 0.2 + 0.4·LF, s)` with `s = Skills.GetSkillFactor(Sneak)` and
    `LF = StealthSystem.GetLightFactor(GetCenterPoint())`, clamped, then `m_seman.ModifyStealth(target, ref target)`,
    clamped again;
  - standing: 1.
  - Then every call: `m_stealthFactor = MoveTowards(m_stealthFactor, target, dt / 4)` (0.25 per second) and ZDO
    `Stealth` (`ZDOVars.s_stealth`) is written when it changed. After a change of target the bar needs up to 0.5 s
    for the refresh plus the ramp (1.0 to 0.85 takes about 0.6-1.1 s).
  - `m_lastStealthPosition` is written and never read: movement plays no part.
- `Player.GetStealthFactor` (`Player.cs:6977-6988`): the owner returns the field, every other game reads ZDO
  `Stealth`. So **anything that changes the factor on the player's own game reaches every creature's owner.**
- `SEMan.ModifyStealth(float baseStealth, ref float stealth)` (`SEMan.cs:411-417`) calls every status effect's
  `ModifyStealth`; `SE_Stats.ModifyStealth` adds `baseStealth × m_stealthModifier` (`SE_Stats.cs:345-348`). It is
  called only from `Player.UpdateStealth` (grep). No vanilla `SE_Stats` sets `m_stealthModifier` or `m_noiseModifier`
  (dump: all 80 are 0).
- `Skills.GetSkillFactor` = `Clamp01(GetSkillLevel / 100)`; the level includes `SEMan.ModifySkillLevel`
  (`Skills.cs:160-178`).
- `Player.UpdateCrouch` (`Player.cs:6244-6259`) ends the crouch on no stamina, swimming, bed, build mode, running,
  **blocking** or flying, and clears the animator bool `crouching` during `InAttack() || IsDrawingBow()`. Any damage
  ends it too (`Humanoid.OnDamaged` → `SetCrouch(false)`, `Humanoid.cs:472`).
- `Character.IsSneaking` (`Character.cs:1868-1875`) = crouching, `m_currentVel.magnitude > 0.1`, on the ground.
  `m_currentVel` is the character's own move-input velocity (`Character.UpdateWalking`, `:1747-1762`): it does not
  include a ship or platform carrying the player, nor sliding.
- Sneak stamina (`Player.OnSneaking`, `Player.cs:6920-6927`): drain × `Lerp(1, 0.25, sqrt(s))`, so a Sneak-0 player
  pays four times the Sneak-100 drain. The mod does not change it in v1 (D27).

### 1.2 How creatures use it (the creature's owner)

- `BaseAI.CanSeeTarget` (static, `BaseAI.cs:780-821`): not in fly or ghost mode; distance ≤ `viewRange`, then
  ≤ `viewRange × target.GetStealthFactor()`; when not alerted, within `viewAngle` of forward; one raycast from the eye
  to the target (`GetCenterPoint()` when crouching, `m_eye.position` otherwise) against `m_viewBlockMask` = Default,
  static_solid, Default_small, piece, terrain, **viewblock**, vehicle (`BaseAI.cs:222`); Mistlands mist.
- `BaseAI.CanHearTarget` (static, `BaseAI.cs:749-773`): distance ≤ hear range (12 m cap in dungeons) and < the
  target's noise range.
- Every sense check reaches these two statics: `CanSenseTarget` (static, `BaseAI.cs:727-742`) and all instance
  overloads (`:717-775`) call them; callers are `BaseAI.FindEnemy` (`:1395-1427`), `MonsterAI.UpdateTarget`
  (`MonsterAI.cs:313-314`), `AnimalAI.UpdateAI` (`AnimalAI.cs:57`), `BaseAI.FindClosestCreature` (turrets:
  `Turret.cs:306`, which targets players by default, `m_targetPlayers = true`), the PassiveMobs backstab test
  (`Character.cs:2336`) and `NpcTalk` (`NpcTalk.cs:139-140`).
- `MonsterAI.UpdateAI`: `HuntPlayer()` forces alert every tick (`MonsterAI.cs:359-362`). With a target it sees or
  hears but is not alerted, it **walks to the target** (`MoveTo` the last known position, `:524-547`) and becomes
  alerted when `canSeeTarget && dist < m_alertRange × stealthFactor`: for an unalerted creature, "sees you" already
  means "notices you and comes over". It **attacks only when `canSeeTarget && IsAlerted()`** (`:554`). A target it
  neither sees nor hears is searched at its last known position (`:575-590`). With no target it calls
  `IdleMovement`, which wanders around `m_spawnPoint` (`BaseAI.cs:486-494`; its own position when tamed or hunting).
- `MonsterAI.UpdateTarget` (`MonsterAI.cs:233-345`): `FindEnemy` every 2 s near players; with a target it computes
  `canHearTarget` / `canSeeTarget`, resets `m_timeSinceSensedTargetCreature` when either is true, and **gives up**
  when that timer passes 30 s (or 60 s without attacking, or chase distance): `SetAlerted(false)`, target cleared,
  `m_updateTargetTimer = 5` (no new search for 5 s).
- `MonsterAI.SetAlerted(true)` also puts `m_timeSinceSensedTargetCreature` back to 0, even on a creature already
  alerted (`MonsterAI.cs:919-924`). `UpdateAI` calls it every tick while `HuntPlayer()` is true, before `UpdateTarget`
  (`:359-362`), so a hunting creature (raids, hunt spawns, `SpawnSystem` / `TriggerSpawner` entries with hunt on) never
  reaches the give-up; `RPC_OnNearProjectileHit` calls it too (`:209`), so a projectile landing near a creature starts
  its count again.
- Other alert sources: `MonsterAI.OnDamaged` (`:184-190`: alert, `SetTarget(attacker)`), projectile impacts
  (`MonsterAI.RPC_OnNearProjectileHit`, `:203-222`, from `BaseAI.DoProjectileHitNoise`), the `Alert` RPC.
- `MonsterAI.OnDamaged` and `AnimalAI.OnDamaged` (protected overrides, `MonsterAI.cs:184`, `AnimalAI.cs:25`; the only
  two) are bound to `Character.m_onDamaged` by `BaseAI.Awake` (`BaseAI.cs:226`, virtual dispatch) and reached from
  `Character.ApplyDamage` (`Character.cs:2485`), **only for damage above 0.1** (`Character.cs:2436`), on the victim's
  owner. Both first call `base.OnDamaged`, whose body is one field write (`BaseAI.cs:704-707`, `m_timeSinceHurt = 0`):
  a tiny method the Mono JIT may inline into them, so a patch on it can be skipped (`docs/game/core-engine.md`,
  Harmony notes). Burning and poison ticks build a `HitData` without an attacker (`SE_Burning.cs:40-48`,
  `SE_Poison.cs:34-39`), so they reach `OnDamaged` with `attacker = null`.
- `AnimalAI.UpdateAI` (`AnimalAI.cs:30-82`): `FindEnemy` sets `m_target`; while it has a target it flees from the
  target's **real position** every tick, sensed or not, and drops it only after `m_timeToSafe` alerted (Deer and Hare
  15 s, dump). Its `OnDamaged` alerts but sets no target.
- **Sleeping creatures** (`MonsterAI.UpdateSleep`, `MonsterAI.cs:817-855`) wake by raw distance
  (`Player.GetClosestPlayer(pos, m_wakeupRange)`) or by the player noise range (`m_noiseWakeup`), not through
  `CanSeeTarget` / `CanHearTarget`. Dump: 22 prefabs sleep, for example `Draugr_sleeping` (wake 10 m),
  `Troll_sleeping` (15 m), `Ghost_sleeping` (5 m); 4 use noise.
- Bosses: `Character.IsBoss()` = `m_boss` (`Character.cs:3114`). Dump: `m_boss` is true for Bonemass, Dragon,
  Eikthyr, Fader, FrozenKing, FrozenKing_p2, FrozenKing_p3, gd_king, GoblinKing, Hive, SeekerQueen, TheHive (the
  `Aspect_*` creatures and Hildir's bosses are not bosses).
- Perception and health values used in examples (dump): Greydwarf and Skeleton view 30 m, angle 90, hear 9999,
  alert 20, 40 HP; Greyling same senses, 20 HP; Troll same senses, 600 HP; Boar, Hen and Neck view 20, hear 20,
  alert 6, 10 / 10 / 5 HP; Deer and Hare (AnimalAI) view 25, 10 HP; `Draugr_sleeping` 100 HP;
  `piece_TrainingDummy` faction `TrainingDummy`, 2500 HP, hear 0, alert 10.
- Max health (`Character.SetupMaxHealth` / `GetMaxHealthBase`, `Character.cs:747-751, 3079-3087`) = prefab
  `m_health` × star level (1 for no star), × world level × `m_worldLevelEnemyHPMultiplier` when the world level is
  above 0.

### 1.3 Sneak XP and the sneak attack

- `Player.OnSneaking` (`Player.cs:6920-6945`), only while sneaking (moving): every 1 s
  `RaiseSkill(Sneak, BaseAI.InStealthRange(this) ? 1 : 0.1)`. `InStealthRange` (`BaseAI.cs:1582-1601`) = an enemy
  within its view range or 10 m and none of them alerted. Standing still gives no XP.
- The sneak attack is the backstab in `Character.RPC_Damage` (`Character.cs:2336-2341`), on the victim's owner:
  `!m_baseAI.IsAlerted() && hit.m_backstabBonus > 1 && Time.time − m_backstabTime > 300 && (!PassiveMobs ||
  !CanSeeTarget(attacker))` → `m_backstabTime = Time.time`, damage × bonus, `m_backstabHitEffects`. The private
  `m_backstabTime` (`Character.cs:298`) is written nowhere else. No XP, no message.
- **What blocks a second backstab:** the first damaging hit alerts the victim (`MonsterAI.OnDamaged` →
  `SetAlerted(true)`), so the next hit fails `!IsAlerted()`; the 300 s cooldown only matters once the creature is
  unalerted again (after a give-up). `m_backstabTime` is an instance field on the owner's game, not in the ZDO: it
  starts at −99999 for every new instance, so it resets when the creature is loaded again (zone unload and reload,
  relog), and a new owner's instance has its own.
- `RPC_Damage` also runs for zero-damage hits: it counts `EnemyHits` for the local attacker (`:2247-2251`) and sets
  the ZDO "attackers" flag for a `Player` attacker before damage is resolved (`:2283-2292`), which is what gives kill
  credit (`Character.OnDeath`: every player who hit it gets the kill).
- `HitData.m_ranged` (`HitData.cs:682`) is set for projectile and `Aoe` hits (`Projectile.cs:510, 649`, `Aoe.cs:705`).
- `Player.RaiseSkill` (`Player.cs:2077-2086`) applies `SEMan.ModifyRaiseSkill` (Rested ×1.5); `Skills.Skill.Raise`
  (`Skills.cs:62-79`) adds `m_increseStep × value × Game.m_skillGainRate` with **no sign check** (a negative value
  lowers the progress) and levels up at `floor(level + 1)^1.5 × 0.5 + 0.5`, **one level per call, the excess is
  lost**. Sneak's `m_increseStep` is 0.5 (asset read), so `RaiseSkill(Sneak, N)` = N seconds of sneaking near unaware
  enemies. `Skill.GetLevelPercentage()` gives the progress to the next level.

### 1.4 Light, bushes, fog and mist

- Light: `StealthSystem.GetLightFactor` (`StealthSystem.cs:31`) maps the summed light at the point (ambient, sun or
  moon with a shadow raycast, point lights in range) to 0..1. Night, rain and shade only act through this. The sun
  test casts from 1000 m toward the point (`StealthSystem.cs:55`), so a point inside a bush's collider is shaded
  by it.
- **Bushes already hide, invisibly** (asset read, research brief 5): 38 vegetation prefabs (Bush01, RaspberryBush,
  BlueberryBush, LingonberryBush, shrub_2, Bush02_en, and trees: Beech1, Birch1, Oak1, FirTree, Pinetree_01...) carry
  a child collider on layer `viewblock`, which is in `m_viewBlockMask` and in the stealth shadow mask. A creature's ray
  through foliage is blocked, and foliage or a tree crown shades the player from sun and moon: a crouched player in or
  under foliage at clear noon drops from a light factor of about 0.97 to about 0.35, which cuts the Sneak-0 sight
  range by about a third. Nothing in the game tells the player.
- **Weather fog does nothing** to stealth: no AI or stealth code reads fog (grep). `EnvMan.SetEnv(env, dayInt,
  nightInt, morningInt, eveningInt, dt)` (`EnvMan.cs:833-877`, called every frame from `EnvMan.FixedUpdate` with the
  current, possibly blended or forced, environment) writes `RenderSettings.fogDensity` = the environment's four fog
  densities × the day-part weights (`:871-875`); it is the only writer in game code. Per environment (asset read,
  day / evening / morning / night): Clear 0.003 / 0.01 / 0.01 / 0.01, Misty 0.02 / 0.1 / 0.1 / 0.15, Heath clear
  0.006 / 0.01 / 0.03 / 0.02, DeepForest Mist 0.01 / 0.02 / 0.02 / 0.02, Rain and LightRain 0.03, SwampRain 0.02,
  Darklands_dark 0.03, Snow 0.004, SnowStorm 0.05, Mistlands 0.02-0.05, Ashlands 0.01-0.05, Deep North Twilight
  0.01-0.07, boss arenas 0.003-0.05, dungeons 0.1-0.2. Weather is seeded per biome and period, so games in the same
  biome agree. A dedicated server has no weather.
- **Mistlands mist** hides beyond 10 m: `ParticleMist.IsMistBlocked` (`ParticleMist.cs:235-268`) blocks sight when
  the eye, the target or the midpoint is in a `Mister` and not in a `Demister`, for creatures without
  `m_mistVision`. `ParticleMist.IsInMist(point)` (`:218-233`) is the cheap "am I in mist" test.

### 1.5 Status effects and HUD

- `SEMan.AddStatusEffect(StatusEffect, …)` (`SEMan.cs:184-212`) adds `statusEffect.Clone()` (a `MemberwiseClone`,
  `StatusEffect.cs:77-80`) unless one with the same `NameHash()` (hash of the object name, `StatusEffect.cs:355-362`)
  is there, so values stored on the template after the add never reach the clone. No ObjectDB entry is needed for an
  effect added by reference. `RemoveStatusEffect(StatusEffect)` removes by name hash; `Player.OnDeath` removes all.
- `Hud.UpdateStatusEffects` (`Hud.cs:1635-1690`) runs every frame: when the number of shown effects changes it
  destroys and rebuilds every icon, and it plays the "flash" animation on each newly added effect; it calls the
  virtual `GetIconText()` (`StatusEffect.cs:209`) of every icon every frame. The inventory's Active effects page
  prints `m_name` and `GetTooltipString()` (`TextsDialog.AddActiveEffects`, `TextsDialog.cs:230-253`).
- `Hud.UpdateStealth` (`Hud.cs:947-980`): the stealth bar and eye show while crouching (or with a factor below 1)
  and no bow is drawn; the eye shows sensed / targeted / hidden.
- **Enemy plates** (`EnemyHud`): `TestShow(Character c, bool isVisible)` (`EnemyHud.cs:101-124`, private) is a
  distance test: a non-boss within `m_maxShowDistance` (30 m, see below), a boss within 100 m when alerted, never a
  crouching player. `LateUpdate` creates a plate for every character that passes; `UpdateHuds` destroys **one**
  failing plate per frame (`:170-181`). A non-boss creature's plate is only drawn for `m_hoverShowDuration` (60 s)
  after the local player's crosshair was on it: a new plate starts with `m_hoverTimer = 99999` (`:37, :188`), so a
  creature never aimed at shows no bar. The plate also carries the creature's Alerted / Aware icons (`:201-207`), set
  every frame from `BaseAI.IsAlerted` / `HaveTarget`, so a new plate shows the current state at once.
  `m_maxShowDistance` is 10 in code but **30 m** in the game's `EnemyHud` object (1.0.16, measured at runtime by the
  Compendium Encyclopedia and Creature Morale self-tests; `sneak.healthbars` notes it too), so plates show within 30 m.

### 1.6 Throwing, projectiles, spawn on hit, zero damage

- Dump, `BombSmoke` (vanilla "Smoke Bomb", `$item_smokebomb`): OneHandedWeapon, attach Tool, stack 50, weight 0.3,
  blunt 5, backstab ×3, attack force 40; primary attack Projectile, animation `throw_bomb`, stamina 8, consume item,
  start noise 0, **hit noise 0**, projectile speed 20. The player animator has the `throw_bomb` trigger; clip
  `BombThrow` 1.17 s, `OnAttackTrigger` at 0.76 s (dump). The projectile and explosion prefabs are named
  `smokebomb_projectile` / `smokebomb_explosion` in the asset list (their field values are unverified).
- `Projectile.Setup` (`Projectile.cs:408-450`) copies the attack's hit noise when ≥ 0 and the hit data (damage,
  backstab bonus, status effect); **when `m_respawnItemOnHit` is set it sets `m_spawnItem = item`** (`:442`). Only
  the projectile's owner (the thrower) simulates it (`FixedUpdate`, `:262`).
- `Projectile.FixedUpdate` (`:255-345`): while `!m_didHit` it moves, tests water only when `m_canHitWater` (default
  false: otherwise the projectile sinks and hits the bottom, `:291-297`) and raycasts; each hit calls `OnHit`. The TTL
  block sits **outside** the `!m_didHit` test: when `m_ttl` runs out it calls `SpawnOnHit` if `m_spawnOnTtl`, then
  destroys the projectile (`:331-341`).
- `Projectile.OnHit` (`:570-725`): with `m_onlyStopOnTerrain`, every new collider is a hit of its own; a bounce
  (`m_bounce`, and on water `m_bounceOnWater`; never on a character) returns before spawning; with `m_aoe` 0 and an
  `IDestructible` it sends a `HitData` (attacker set) through `destructible.Damage`, **even with zero damage**; then
  spawn when `m_spawnOnHit` / `m_spawnItem` / random spawns are set and the hit passes `m_spawnOnTerrain` /
  `m_spawnOnCharacters` / `m_spawnOnWearNTear` (`:687-690`); then `if (m_hitNoise > 0) BaseAI.DoProjectileHitNoise`
  (`:692-694`), which alerts enemies of the thrower near the impact and points them at him. Only then (`:701-725`),
  unless `m_onlyStopOnTerrain` and not terrain: `m_didHit = true`, **`m_ttl = m_stayTTL`** (default 1 s), and the
  projectile is destroyed unless it stays (`m_stayAfterHitStatic`, `m_stayAfterHitDynamic`) or attaches
  (`m_attachToRigidBody`, `m_attachToClosestBone`). A projectile that stays or attaches runs the TTL block later: with
  `m_spawnOnTtl` it **spawns a second time**.
- `Projectile.SpawnOnHit` (`:776-859`): returns early for `m_groundHitOnly` / `m_staticHitOnly`;
  `Object.Instantiate(m_spawnOnHit)` on the thrower's game, nudged by `normal × 0.25`, then `IProjectile.Setup(m_owner,
  …)` on it (`IProjectile.cs`: `Setup(Character, Vector3, float, HitData, ItemData, ItemData)` and
  `GetTooltipString(int)`); then `ItemDrop.DropItem(m_spawnItem)` when set. If `Setup` throws, `OnHit` stops before
  `m_didHit` is set: the projectile keeps flying and hits again next physics step (a new cloud each time).
- Zero damage: `Character.ApplyDamage` returns at total ≤ 0.1 (`Character.cs:2436`) before `m_onDamaged`, so no
  `OnDamaged`, no alert, no target (but the hit still counts for kill credit, 1.3). A backstab bonus of 1 never
  backstabs.
- Before `ApplyDamage`, `Character.RPC_Damage` applies the victim's resistances (`:2379`) and, in NG+, the world-level
  enemy armor (`:2388`), then takes fire, poison and spirit out of the hit (`:2393-2395`; they become burning,
  poison and spirit status damage without an attacker). So a hit that is only fire, poison or spirit, or whose damage
  the resistances or armor bring to 0.1 or less, never reaches `OnDamaged` with its attacker: no alert and no target
  from vanilla, although it carried damage.
- Vanilla smoke (`Smoke` puffs on layer `smoke`) chokes non-tolerant characters (`Character.UpdateSmoke`, SE `Smoked`);
  the Player prefab has `m_tolerateSmoke = False` (dump). `smoke` is not in `m_viewBlockMask`: vanilla smoke hides
  nobody.
- `Floating.GetLiquidLevel(Vector3, float, LiquidType)` (`Floating.cs:234`) gives the water surface at a point.

### 1.7 Adding an item and networked prefabs

- `ObjectDB.Awake` → `UpdateRegisters` (`ObjectDB.cs:35-66`) rebuilds `m_itemByHash` with `Dictionary.Add` (a
  duplicate name throws). `CopyOtherDB` (`:41-48`) assigns the other DB's lists **by reference**; the main menu builds
  its DB with `AddComponent<ObjectDB>()` (so `Awake` runs on **empty** lists) and then `CopyOtherDB` from the ObjectDB
  prefab (`FejdStartup.SetupObjectDB`, `FejdStartup.cs:855-860`), so anything added in the menu lands in the prefab's
  list. Registration must be idempotent and must not treat the empty first pass as an error.
- `ZNetScene.Awake` (`ZNetScene.cs:32-47`) fills `m_namedPrefabs` from `m_prefabs`. Every 1/30 s
  (`CreateDestroyObjects`, `:349-367`) it creates the objects of the ZDOs near this game's reference position and
  removes those that left it:
  - `CreateObject` (`:88-111`) instantiates by prefab hash; an unknown hash logs the warning "Missing prefab hash"
    and creates nothing, **on every pass** while the ZDO is near (it is never marked created). Only on the server is an
    unknown prefab ZDO found near its reference destroyed ("Destroyed invalid prefab ZDO", `:226-235`): on a host,
    near the host player; a dedicated server has nothing near its reference (see the active area below).
  - At world load the server keeps ZDOs of unknown prefabs and only warns ("Found N ZDOs with unknown prefabs. Will
    load anyway.", `ZDOMan.FilterZDO` / `WarnAndRemoveBrokenZDOs`, `ZDOMan.cs:398-472`). The load runs in `ZNet.Start`
    (`ZNet.cs:435-462`), after the `Awake`s of the scene, so a prefab added by a `ZNetScene.Awake` postfix is known
    by then (read in the client build; the dedicated server build is compiled from the same source).
  - `RemoveObjects` (`:288-318`) destroys the instance of every ZDO that left the area and, **for a non-persistent ZDO
    this game owns, destroys the ZDO for everyone** (`:313`).
  - `ZNetScene.Destroy` (`:115-129`) destroys the ZDO only when this game owns it; otherwise it only drops the local
    instance and resets `Created`, so the next pass creates the object again.
- Ownership hand-over (`ZDOMan.ReleaseNearbyZDOS`, `ZDOMan.cs:967-990`, run by the server every 2 s): only
  **persistent** ZDOs are handed over; one whose owner is gone or no longer has it in its active area goes to a game
  that has it in its area. Non-persistent ZDOs of a disconnected owner are destroyed (`RemoveOrphanNonPersistentZDOS`,
  `:1472-1485`). Persistent ZDOs are saved with the world (`:1638`).
- The active area is one or two zones of 64 m around a game's reference position, depending on the world's
  simulation distance (`ZNetScene.PointInsideActiveArea`, `:390-410`). In the client build a game's reference position
  starts at (0, 0, 0) (`ZNet.cs:219`; `SetReferencePosition` is only called by the player, `Game`, `Tracker` and
  `Valkyrie`), but the dedicated server build keeps its reference position far away (server build 1.0.15,
  `Game.FixedUpdate`, see [building-crafting.md](../game/building-crafting.md)): a dedicated server instantiates no
  world objects and simulates no creatures, so clouds and the creatures near them are always simulated by a player's
  game (a host's game counts as a player's).
- `ZNetView.Awake` (`ZNetView.cs:42-109`) creates a new ZDO for a freshly instantiated object, owned by this game,
  with `Persistent = m_persistent`; in the main menu (`ZDOMan.instance == null`) it destroys itself.
  `ZNetView.Register` uses `Dictionary.Add` (registering twice throws, `:273-306`); a routed RPC that reaches a view
  without that method logs "Failed to find rpc method" on the receiving game (`:316-323`).
- `Inventory.AddItem(int prefabHash, …)` (`Inventory.cs:1051-1061`): an unknown prefab logs "Failed to find item
  prefab" and **drops the item** from the loaded inventory.
- `Container` (`Container.cs:462-499`): `Load` fills the inventory with `m_loading = true`, and `OnContainerChanged`
  saves only when `!m_loading` and this game owns the chest. Opening and closing a chest saves nothing; the first
  change (an item taken or added) by the owner saves the whole inventory.
- Recipes: `Player.GetAvailableRecipes` skips `!m_enabled` recipes (`Player.cs:5545-5553`); known recipes are learned
  by `m_shared.m_name` once the materials were seen (`Player.UpdateKnownRecipesList`, `Player.cs:5329-5342`).
- Time shared by all games: `ZNet.GetTimeSeconds()` (`ZNet.cs:2813`).

### 1.8 Who runs what

| Thing | Runs on | Others learn it through |
|---|---|---|
| Stealth factor, crouch, status effects of a player | that player's game | ZDO `Stealth` |
| Sight, hearing, alert, target, give-up of a creature | the creature's ZDO owner: a player's game (a host's included), never a dedicated server (1.7) | ZDO `alert`, `haveTarget` |
| Backstab | the victim's owner | nothing (`m_backstabTime` is local to the instance) |
| Skills | the player's game | nothing |
| Projectile and what it spawns | the thrower's game | the spawned objects' ZDOs |
| Weather, fog, light | each game from its camera | nothing (none on a dedicated server) |

---

## 2. Design

### 2.1 Sneak-attack XP (G1, E4, E6)

- **Detect on the victim's owner.** `Character.RPC_Damage(long, HitData)` prefix: when `m_baseAI != null` and
  `hit.m_backstabBonus > 1`, keep `m_backstabTime` in `__state`. Postfix: the backstab happened when
  `m_backstabTime` changed. Every other hit costs two reference checks (the same prefix and postfix also note the
  reveal of 2.10 while a cloud is loaded). XP is paid **only for a vanilla backstab**:
  hits whose weapon has backstab ×1 (the Tower Shield Wall bash, the Smoke Screen) never pay.
- **Eligible:** the attacker (`hit.GetAttacker()`) is a `Player`; the victim is not tamed and its faction is not
  `Faction.TrainingDummy`; the victim owner's rules are not pending (4.3; nothing is sent and `LastXp` is not
  written).
- **XP cooldown per creature, in its ZDO.** Vanilla's 300 s is per instance and resets on reload or ownership change
  (1.3), so the owner also checks the victim's ZDO long `MC.Combat.Sneak.Ambush.LastXp` (network time in ms): XP is
  sent only when `SneakAttackXpCooldown` (300 s) has passed since it, and the key is written when the XP is sent (the
  attacker's view is valid). So the "smoke, forget, walk away and back" loop cannot pay faster than once per 300 s per
  creature, whoever owns it. A sent payout counts whether or not the attacker's game pays it: the victim's owner cannot
  know that (the attacker's copy turned off and let in, its rules pending, another sneak-XP mod paying there), so such
  a sneak attack also uses up the creature's 300 s (said in the setting's description and the README).
- **Send:** `attacker.m_nview.InvokeRPC("MC.Combat.Sneak.Ambush.SneakAttackXp", victimHealth, hit.m_ranged)` with
  `victimHealth = victim.m_health × victim.GetLevel()` (the prefab's base health times the star level: stars pay
  more, the world-level health multiplier does not). Routed RPCs go to the ZDO owner of the attacker's Player object,
  which is the attacker's own game (handled locally when the attacker is this game's player).
- **Receive (attacker's game):** registered on every `Player`'s `ZNetView` by an **always-on** `Player.Awake`
  postfix (section 2.13), so the handler exists whatever the toggle said when the player spawned. The handler ignores
  calls for anyone but `Player.m_localPlayer`, while the feature is inactive or the rules are pending (4.3), or when
  another mod already pays sneak-attack XP on this game (section 6, unless `PayAlongsideOtherSneakXpMods`). It
  ignores a non-finite value,
  clamps `victimHealth` to `[0, ReferenceHealth × MaxHealthScale]` and computes the amount with the **receiver's**
  rules (the server's):

  `xp = (SneakAttackXpFlat + SneakAttackXp × min(victimHealth / ReferenceHealth, MaxHealthScale)) × (ranged ? RangedXpFactor : 1)`

  then, when `xp > 0`, `Player.RaiseSkill(Skills.SkillType.Sneak, xp)` (keeps Rested and the world's skill-gain
  rate). A Debug line names the amount.
- **Message (E6):** with `ShowSneakAttackMessage` (personal, default on), a top-left `MessageHud` line: "Sneak attack!
  +N% Sneak", where N is the progress gained toward the next level (`Skill.GetLevelPercentage()` before and after);
  after a level-up only "Sneak attack!" (vanilla shows its own level-up message).
- Defaults: `SneakAttackXpFlat` 3, `SneakAttackXp` 10, `ReferenceHealth` 100, `MaxHealthScale` 3, `RangedXpFactor`
  0.5, `SneakAttackXpCooldown` 300 s. XP is in "seconds of sneaking near unaware enemies"; the progress gain is half of
  it (Sneak step 0.5, asset read). Values for level-1 creatures (no star), Rested and skill-gain rate not included:

| Victim (dump HP) | Melee XP | Ranged XP | Share of a level at Sneak 5 (7.85 needed) | at Sneak 20 (48.6) |
|---|---|---|---|---|
| Neck 5 | 3.5 | 1.75 | 22% | 4% |
| Boar, Deer, Hare, Hen 10 | 4 | 2 | 25% | 4% |
| Greyling 20 | 5 | 2.5 | 32% | 5% |
| Greydwarf 40, Skeleton 40 | 7 | 3.5 | 45% | 7% |
| Draugr 100 | 13 | 6.5 | 83% | 13% |
| Troll 600 (health part capped at ×3) | 33 | 16.5 | 1 level (capped) | 34% |

- Every sneak attack pays something (the flat part), bigger creatures more, capped. Farming stays out: no XP for
  standing still (vanilla), training dummies and tames pay nothing, one payout per creature per 300 s (ZDO), and a
  creature that Creature Morale makes afraid of you gives no backstab while it watches you or runs from you (section
  6.1). A value over one level at
  once is cut by `Skill.Raise` (1.3); only big creatures at low skill reach that.

### 2.2 Early-game curve (G2)

The crouched target is multiplied by

`early(s) = 1 − EarlyGameBonus × (1 − s)`  (s = Sneak skill factor 0..1, `EarlyGameBonus` default 15%, range 0-25%)

applied in the `SEMan.ModifyStealth` postfix (2.5) to the value after all status effects. Status effect bonuses are
linear in the base (`stealth = base × (1 + Σ modifiers)`), so this equals scaling the vanilla base. At Sneak 100 the
factor is 1: exactly vanilla. The range stops at 25% because above about 28% a higher skill would make the player
*more* visible in full light (the product must keep falling with skill). The early-game help is on visibility only;
sneak stamina stays vanilla in v1 (decision D27, open question 8).

| Sneak | Vanilla dark / half / full light | With the mod (15%) | Greydwarf sees / alerts (full light), vanilla → mod |
|---|---|---|---|
| 0 | 0.50 / 0.75 / 1.00 | 0.43 / 0.64 / 0.85 | 30 / 20 m → 25.5 / 17 m |
| 25 | 0.43 / 0.66 / 0.90 | 0.38 / 0.59 / 0.80 | 27 / 18 m → 24 / 16 m |
| 50 | 0.35 / 0.58 / 0.80 | 0.32 / 0.53 / 0.74 | 24 / 16 m → 22.2 / 14.8 m |
| 75 | 0.28 / 0.49 / 0.70 | 0.26 / 0.47 / 0.67 | 21 / 14 m → 20.2 / 13.5 m |
| 100 | 0.20 / 0.40 / 0.60 | same | same |

The vanilla stealth bar shows it; no icon (it is the curve itself).

### 2.3 Holding still (G3)

- **Still** = crouching (`IsCrouching`), on the ground, not walking (`!IsSneaking()`: the vanilla 0.1 m/s threshold
  on the move-input speed, so turning the camera in place is fine), **and** the body moved less than 0.25 m in the
  world since the last 0.5 s refresh (so a ship, a cart or a slide carrying a crouched player is not "still";
  `m_currentVel` does not see them, 1.1). The local player's still time grows in the `Player.UpdateStealth` prefix
  and resets when any condition fails (the displacement is checked at each refresh against the position kept at the
  previous one).
- After `StillDelay` (1 s) of stillness the **Holding still** bonus applies: the factor × (1 − `StillBonus`), default
  **70%**. It cuts how far creatures see the player, and so how close a passing creature must come to notice them.
  Example at Sneak 0 in daylight: a crawling player is seen by a Greydwarf at 25.5 m, a still one only at 7.7 m, so a
  Greydwarf wandering past 8-10 m away walks on. One that does come within 7.7 m in its front half notices the
  player, walks over (vanilla, 1.2) and is alerted at 5.1 m. At night a still Sneak-0 player is seen at 3.8 m.
- The factor still ramps toward the new target at vanilla speed (0.25/s), so the bar visibly sinks over 2-3 s.
- **Ends at once** (`StillEndsAtOnce`, default on): when stillness ends (walking, standing up, jumping, being hit,
  crouch dropped by an attack or a bow draw, being carried), the prefix forces a target refresh this very call
  (`m_stealthFactorUpdateTimer = 0.51f`) and the postfix raises `m_stealthFactor` at once to the value it would have
  without the still bonus (kept from the last refresh) and writes ZDO `Stealth`. From there vanilla ramps to the new
  target as usual (standing up still ramps to 1 at 0.25/s). This stops stop-and-go crawling from keeping the bonus.
- No XP for holding still (vanilla gives none; keeps AFK farming out).
- Whether the animator keeps the `crouch` tag during a crouched bow draw is unverified (research C3): if it does, an
  archer keeps the bonus while drawing; if not, the draw ends it like any attack. Either way it is the vanilla crouch
  rule.

### 2.4 Foliage, fog and mist made visible (G4)

Evaluated on the local player's game at each 0.5 s refresh, only while crouching (vanilla stealth only exists
crouched), each shown by a status icon (2.6):

| Cue | Test | Effect | Icon text |
|---|---|---|---|
| **In foliage** | touching: a `viewblock` collider within `FoliageReach` (0.5 m) of the body centre (`Physics.CheckSphere(GetCenterPoint(), FoliageReach, viewblockMask, QueryTriggerInteraction.Ignore)`; implemented with `UseGlobal`, see 7.7); or under a crown: `Physics.Raycast(GetCenterPoint(), up, 20 m, viewblockMask)` | none by default: vanilla's own effect (sight block and shade) is what the cue shows. `FoliageBonus` (server setting, default 0%) adds factor × (1 − bonus) while touching | none, or `−N%` while touching when `FoliageBonus` > 0 |
| **Fog** | outdoors (`!Character.InInterior`) and the environment's fog density `d` above `FogDensityNoBonus` (0.01) | factor × (1 − `FogBonus` × clamp01((d − 0.01) / (0.08 − 0.01))), full 30% at `FogDensityFullBonus` 0.08 | `−N%`, shown from 2% and hidden below 1% |
| **In the mist** | `ParticleMist.IsInMist(GetCenterPoint())` | none: vanilla already blocks sight beyond 10 m for creatures without mist sight | (none) |

- **Bushes and trees:** vanilla already has a bonus (1.4): foliage blocks a creature's line of sight, and in or under
  foliage the player is shaded from sun and moon, which lowers the stealth bar in daylight. The user asked to create a
  bonus only where vanilla has none, so by default the mod **shows** this one (icon and tooltip) and adds no number
  (decision D11); `FoliageBonus` stays as a server option. Research says a player cannot always get their centre
  *inside* a small bush's sphere (the stem collider may push them out, unverified), hence "within 0.5 m". The crown ray
  catches trees and tall bushes overhead (the tree crown collider shapes are unverified; `sneak.cover` checks one).
- **Fog:** vanilla has none, so the mod creates one, scaled by the fog of the current environment. The density `d` is
  computed from **game data, not the render state**: an `EnvMan.SetEnv` postfix keeps `env.m_fogDensityNight ×
  nightInt + m_fogDensityDay × dayInt + m_fogDensityMorning × morningInt + m_fogDensityEvening × eveningInt`, the same
  inputs vanilla renders, so fog-removal or visibility mods that change `RenderSettings` do not change anyone's bonus.
  It follows weather changes and the time of day smoothly. Expected bonuses from the asset-read densities (also the
  README table):

| Environment (biome) | Fog bonus |
|---|---|
| Clear (most biomes), Snow (Mountain) | none |
| Misty (Meadows, Black Forest, Plains) | 4% by day, 30% at dawn, dusk and night |
| Heath clear (Plains) | none by day and evening, 9% in the morning, 4% at night |
| DeepForest Mist (Black Forest) | none by day, 4% otherwise |
| Rain, ThunderStorm, LightRain | 9% |
| SwampRain / Darklands_dark (Swamp) | 4% / 9% (the Swamp is always a little foggy) |
| SnowStorm (Mountain) | 17% |
| Mistlands clear, rain, thunder | 4-17%, plus the mist rule |
| Ashlands weathers | 0-17% |
| Deep North Twilight weathers | 0-26% |
| Boss arenas | 0-17% |
| Dungeons and caves | none (indoors) |

  Permanent biome haze counts: it is fog the player sees. Dungeons are excluded: their dense fog (0.1-0.2) draws
  darkness in closed rooms and would give the full bonus everywhere inside, where the dark already helps through the
  light level (decision D12). The fog is read on the sneaking player's game, whose weather matches the creatures'
  owners in the same biome, and it replicates through the stealth factor.
- **Mistlands mist:** surfaced only (vanilla rule, no new number).
- The README gets the table above, which plants hide you (the 38 prefabs), the mist rule, and that standing up raises
  your head over low bushes (the traced point moves from 0.93 m to 1.81 m).

### 2.5 The stealth math in one place

A postfix on `SEMan.ModifyStealth(float, ref float)` for the local player's `SEMan` (`__instance.m_character ==
Player.m_localPlayer`) runs after every status effect (vanilla and other mods'), once per 0.5 s refresh:

```
before  = stealth                                       // vanilla curve + all status effects
product = early(s) × Π (1 − bonus_k)  over active cues k ∈ {still, foliage (touching, if FoliageBonus > 0), fog}
after   = before × product
if product < 1: after = max(after, min(VisibilityFloor, before))   // never below the floor, never above "before"
stealth = after                                          // vanilla clamps 0..1 next
capped  = (the floor raised "after")                     // for the cue texts (2.6)
lastWithoutStill = same formula without the still bonus  // for the snap in 2.3
```

- Our bonuses **multiply** (each takes its share of what is left), so they can never reach 0; the floor (default 0.1)
  is a safety net against other mods' additive stealth effects, as the backlog asked.
- `s` and the cue states are computed in the `Player.UpdateStealth` prefix when a refresh is due
  (`m_stealthFactorUpdateTimer + dt > 0.5`), so the postfix only reads fields.
- SecondaryAttacks recomputes the target in its own `UpdateStealth` postfix and calls `SEMan.ModifyStealth` again:
  our postfix applies to that call too, so the bonuses survive it (section 6).

Worked examples (Greydwarf, view 30 m / alert 20 m, default rules):

| Case | Factor | Seen / alerted at |
|---|---|---|
| Sneak 0, full light, crawling | 0.85 | 25.5 / 17 m |
| Sneak 0, full light, still | 0.85 × 0.3 = 0.26 | 7.7 / 5.1 m |
| Sneak 0, night, still | 0.43 × 0.3 = 0.13 | 3.8 / 2.6 m |
| Sneak 0, night, still, thick fog | 0.13 × 0.7 = 0.09 → floor 0.1 | 3 / 2 m |
| Sneak 100, full light, still | 0.6 × 0.3 = 0.18 | 5.4 / 3.6 m |
| Sneak 100, night, still | 0.2 × 0.3 = 0.06 → floor 0.1 | 3 / 2 m |

### 2.6 Stealth cues (status icons, E3)

- One small class `StealthCue : StatusEffect` (display only: it changes nothing itself), created once per session
  with `ScriptableObject.CreateInstance`, a unique object name (`MC.Combat.Sneak.Ambush.<Cue>`: the name hash is the
  identity), `m_ttl = 0`, an icon, `m_name` (English), and overrides of `GetIconText()` and `GetTooltipString()`.
- **The overrides read live static state** (`StealthState` and the current rules), never values stored on the
  template: `SEMan` adds a clone (1.5), and the rules can change while a cue is shown. `GetIconText()` runs every frame
  (1.5), so the texts are cached per value (for example one string per whole percent) and allocate nothing while
  unchanged. Every override catches its own exceptions (7.2).
- The `Player.UpdateStealth` postfix (at a refresh) adds or removes each cue on the local player's `SEMan` by
  comparing the wanted set with `HaveStatusEffect(hash)`, so death (which clears all effects) and other mods cannot
  desync it. The math of 2.5 never depends on the icons: the personal setting `ShowStealthCues = false` hides the
  icons and keeps the bonuses.
- **Steadier icons:** vanilla rebuilds the whole icon row when the count changes and flashes each new icon (1.5), so
  the cues avoid flicker: In foliage and In smoke stay 1 s after their condition ends (display only; the bonus follows
  the real state), the fog icon appears at 2% and goes below 1%. Holding still appears with its bonus (already 1 s
  late by `StillDelay`) and goes at once, because moving ends the bonus at once.
- Cues: **Holding still** (`−70%`), **In foliage** (no number, or `−N%` with a `FoliageBonus`), **Fog** (`−N%`),
  **In the mist** (no number), shown only while crouching; **In smoke** (no number) shown whenever the traced point
  is inside an active cloud (2.9). Numbers come from the live rules. When the floor applies (`capped`, 2.5), numeric
  cues show `max` and their tooltip explains it.
- Tooltips (English, built from the live rules; also in the README):
  - Holding still: "You are crouched and not moving: creatures see you at {100 − StillBonus}% of the distance, so
    they must come much closer to notice you. Moving ends it at once."
  - In foliage: "Bushes and tree crowns block creatures' sight when they are between you and them, and they shade
    you from the sun and moon (in daylight your stealth bar drops)." With a `FoliageBonus` while touching: "Touching
    foliage also makes creatures see you at {100 − FoliageBonus}% of the distance."
  - Fog: "The fog hides you: creatures see you at {100 − N}% of the distance. Thicker fog hides you better."
  - In the mist: "Creatures without mist sight cannot see you from more than 10 m away."
  - In smoke: "Creatures outside the smoke cannot see or hear you (sleeping ones still wake up when you come close).
    Creatures in the smoke with you notice you only within {InsideSightRange} m. A creature you hit sees you. Bosses
    are not fooled."
  - Added when capped: "You are already as hidden as this mod allows: more cover adds nothing now."
- Icons (vanilla sprites looked up by name at first use, each falling back to the Sneak skill icon from
  `Skills.m_skills`, unverified looks): Holding still = Sneak skill icon; In foliage = `Fiddleheadfern` item icon;
  Fog = `Wet` status effect icon; In the mist = `Wisp` item icon; In smoke = the Smoke Screen icon.

### 2.7 Smoke Screen: item, projectile, recipe (G5)

Three runtime clones, built once per game session under an **inactive** `DontDestroyOnLoad` holder (so their
`Awake` never runs there), from whichever database first has `BombSmoke` (the main menu's ObjectDB once populated, or
the game's ZNetScene on a dedicated server). **The three prefab names are permanent after release** (saved in
inventories, chests and ZDOs):

| Prefab | Built from | Changes |
|---|---|---|
| `MC_SmokeScreen` (item) | `BombSmoke` | `m_name` "Smoke Screen", `m_description` (below); all damages and per-level damages 0; `m_attackForce` 0; `m_backstabBonus` 1; `m_attackStatusEffect` null; primary attack: `m_attackProjectile` = our projectile, `m_attackHitNoise` 0, `m_attackStartNoise` 0; everything else vanilla (icon, model, stack 50, weight 0.3, `throw_bomb` animation, stamina 8). |
| `MC_SmokeScreen_projectile` | the item's vanilla `m_attack.m_attackProjectile`, read at runtime (not hardcoded) | **One projectile, one cloud:** `m_spawnOnHit` = our cloud; `m_randomSpawnOnHit` cleared; `m_spawnItem` null and `m_respawnItemOnHit` false (so `Setup` cannot put the item back); `m_spawnOnHitChance` 1, `m_spawnCount` 1; `m_spawnOnTerrain`, `m_spawnOnCharacters`, `m_spawnOnWearNTear`, `m_spawnOnTtl` true; `m_groundHitOnly`, `m_staticHitOnly` false; `m_onlyStopOnTerrain` false (the first hit stops it); `m_stayAfterHitStatic`, `m_stayAfterHitDynamic`, `m_attachToRigidBody`, `m_attachToClosestBone` false (it is destroyed at its first hit, so the TTL spawn cannot fire a second time); `m_canHitWater` true and `m_bounceOnWater` false (it bursts on the water surface instead of sinking); `m_aoe` 0; damage zero; `m_statusEffect` ""; `m_hitNoise` 0; `m_bounce` and the flight values stay vanilla (the throw feels the same). `m_hitEffects` / `m_hitWaterEffects` / `m_spawnOnHitEffects` keep only entries whose prefab has none of `ZNetView`, `Aoe`, `SmokeSpawner`, `Smoke`, `SpawnAbility` (the impact sound and puff stay, nothing networked or harmful). |
| `MC_SmokeScreen_cloud` | a new GameObject | `ZNetView` (**`m_persistent` true**, type Default; decision D25) + `SmokeCloud` (2.8). No collider, not on the `smoke` layer: no choking, no fire or fireplace side effects. |

- Unity's `Instantiate` deep-copies the serialized `ItemData`, `SharedData` and `Attack`, so the vanilla Smoke Bomb
  keeps its own data (checked by `sneak.content`: vanilla name, damage and projectile unchanged).
- A Smoke Screen that hits a creature sends it a zero-damage hit (1.6): no alert, no target, but vanilla counts it as
  the thrower's hit (the `EnemyHits` stat, and kill credit if the creature dies: section 6.2). Kept as vanilla, like a
  harpoon hit (decision D29).
- **Description:** "A pouch of soot and resin. Throw it to raise a thick cloud of smoke for a few seconds. Creatures
  outside the cloud cannot see or hear you inside it, and creatures inside cannot see out. Creatures chasing someone
  close to the burst are blinded for a moment and lose track. A creature you hit sees you. Bosses are not fooled.
  Does no damage."
- **Recipe** `Recipe_MC_SmokeScreen`: `piece_workbench` level 1, **2 Resin + 1 Coal + 1 Leather scraps → 2 Smoke
  Screens** (decision D3). All four values are server settings (`RecipeResources` as `Name:amount,…`,
  `RecipeAmount`, `RecipeStation`, `RecipeStationLevel`). The station comes from any recipe in the database that uses
  a station with that prefab name, else from `ZNetScene.GetPrefab(name).GetComponent<CraftingStation>()`; unknown
  resource names are logged once and skipped; no valid resource → recipe disabled, one warning. `m_enabled` follows
  the feature state (G11) and is off while the rules are pending (4.3). A client rebuilds the recipe when the
  server's rules arrive or change.
- **Names in English literals** (no `$` token): `Localization` is in the non-publicized `assembly_guiutils`, and
  every MC mod uses English strings (decision D22). The known-recipe list stores the name, so changing it later only
  means the recipe is rediscovered.
- **Icon:** the vanilla Smoke Bomb icon (decision D23).

### 2.8 The cloud object (`SmokeCloud`)

- **Owner setup** (`IProjectile.Setup`, called by `Projectile.SpawnOnHit` on the thrower's game): find the base: a
  down raycast on terrain and solid layers (1000 m long, so a projectile whose time ran out high in the air, 7.7,
  still finds the ground; the closest hit wins, so a normal impact is unchanged), and `base.y = max(ground,
  Floating.GetLiquidLevel(position))` (on water the cloud sits on the surface); move the object there,
  `zdo.SetPosition`, and write the ZDO keys with the **current rules** (so the shape never changes mid-cloud, also for
  games whose settings lag). The whole body is in a
  `try/catch` with `PatchGuard.Report`: on a failure it destroys the cloud and returns normally, so the projectile's
  `OnHit` still finishes and the projectile is destroyed (a throw inside `Setup` would make it spawn again every
  physics step, 1.6).

| ZDO key (string hashed with `GetStableHashCode`) | Type | Meaning |
|---|---|---|
| `MC.Combat.Sneak.Ambush.CloudStart` | long | network time of the impact, milliseconds (`ZNet.GetTimeSeconds() × 1000`) |
| `MC.Combat.Sneak.Ambush.CloudDuration` | float | seconds from the impact to the end of the hiding (`CloudDuration`, default 15) |
| `MC.Combat.Sneak.Ambush.CloudRadius` | float | metres (`CloudRadius`, default 4) |
| `MC.Combat.Sneak.Ambush.CloudHeight` | float | metres above the base (`CloudHeight`, default 4) |

  A cloud spawned any other way (console `spawn MC_SmokeScreen_cloud`) sets itself up in `Start` with the defaults.
- **Shape:** a vertical cylinder: horizontal distance to the base ≤ radius, and base.y − 0.5 ≤ y ≤ base.y + height.
- **Timeline** (shared network time): ready once `CloudStart` is known → **active** from start + `ActivationDelay`
  (0.5 s, the smoke builds up) to start + duration → fading 3 s (visual only) → ended. The duration counts from the
  impact and includes the build-up, so the cloud hides for duration − `ActivationDelay` (14.5 s by default); the
  build-up is cut to duration − 1 s when longer (`MinHideSeconds`: a 3 s cloud with a 3 s build-up would never hide).
- **Registry:** `OnEnable` / `OnDisable` add and remove the component in a static list; every game near the cloud
  has an instance, so the owner of every creature near it knows it.
- **Persistence and hand-over (D25):** the cloud's ZDO is persistent, so vanilla hands it over (1.7): when the thrower
  teleports, dies and respawns far away, runs out of the area or disconnects, the server gives the cloud within about
  2 s to a game that has it in its area, and the cloud keeps hiding the players still in it until its normal end. (A
  non-persistent cloud would be destroyed for everyone the moment its thrower's game let go of it.)
- **Expiry:** only an owner destroys the cloud (`ZNetScene.Destroy`, which then destroys the ZDO for everyone), after
  the fade. Any other game that still holds the cloud 5 s after the fade claims ownership first and then destroys it
  (`ClaimOwnership` + `ZNetScene.Destroy`; the `TimedDestruction.m_forceTakeOwnershipAndDestroy` pattern): this
  covers an owner whose game does not have the mod. A non-owner never destroys without claiming (that would only drop
  its local instance, which vanilla creates again the next pass). A cloud past its end, also one loaded from a world
  save, is inert (no visual, never hides) until it is destroyed that way. The expiry runs whatever the feature state.
- **Blind trigger:** the first frame the cloud is active on a game (or on arrival, if still within the blind window),
  2.11 runs once. Only while the feature is active.
- **Visual** (skipped when `ZNet.instance.IsDedicated()`): our own `ParticleSystem` built by code (shape: the
  cylinder, 60-80 particles, start size 3-4 m, lifetime 4-6 s, slow upward drift, grey; a puff is nearly opaque at
  birth and full within 8% of its life, so the burst is thick by the end of `ActivationDelay`, and fully thick until
  60% of its life; emission stops 2.4 s (60% of the shortest life) before the end, so the last puffs start thinning as
  the hiding ends and are gone about 3.6 s after it) with the **material borrowed** from the vanilla smoke: the first
  `ParticleSystemRenderer` of the vanilla explosion (the projectile's original `m_spawnOnHit`, captured before we
  replace it), else
  `SmokeRenderer.Instance._particleSystemPrefab`'s renderer, else no particles and one warning. The impact sound and
  puff come from the kept projectile hit effects. The visual is local and has no gameplay role.
- **Exceptions:** `Start`, `Update`, `OnEnable`, `OnDisable`, `Setup` and `GetTooltipString` each catch their own
  exceptions and call `PatchGuard.Report` (7.2).

### 2.9 Who is hidden: the smoke rule (G6, G10, E1)

One function, `SmokeRegistry.Hides(Transform observer, Vector3 observerPoint, Character target, out bool geometry)`,
used by the sight and hearing postfixes, the forget rule and (geometry only) the health bars:

```
if target is not a Player → not hidden
if observer is not creature AI (no BaseAI) → not hidden                // turrets, NPC talk: D28
T = target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position        // what vanilla aims at
for each registered cloud c that is active now:
    a = c.Contains(observerPoint); b = c.Contains(T)
    if a != b                                   → geometry hidden   // outside cannot see in, inside cannot see out
    if a && b && distance(observerPoint, T) > InsideSightRange (2.5 m) → geometry hidden
    if !a && !b && BlocksLineOfSight && c.SegmentCrosses(observerPoint, T) → geometry hidden   // E1
hidden = geometry || Blinded(observer)
if hidden and observer is a boss (Character.IsBoss) → not hidden          // G10
if hidden and Revealed(observer, target)    → not hidden          // G7
```

- `SegmentCrosses`: closest point of the segment to the cylinder axis in the horizontal plane closer than the radius,
  with its height inside the cylinder (a cheap approximation).
- Observer points: sight = the `eyePoint` vanilla passes; hearing = `me.position + 1 m up`.
- **Sight:** `BaseAI.CanSeeTarget` static postfix: `if (__result && Hides(...)) __result = false`.
- **Hearing:** `BaseAI.CanHearTarget` static postfix, same, when `BlocksHearing` (default on, decision D6).
- **Only creature AI is fooled:** the observer test (`TryGetComponent<BaseAI>` and `IsBoss` on the observer, cached
  per transform) makes turrets, which pass their own transform, and `NpcTalk` see normally (decision D28). The blind
  lookup runs only while some creature is blinded; the observer test and the reveal lookup run only when something
  says hidden, which is rare.
- Consequences through vanilla code: a hidden player is never picked by `FindEnemy`, never alerts by sight, is never
  attacked (attacks need `canSeeTarget`), and pursuers walk to the last known position. Creatures in the smoke with
  the player notice them within 2.5 m, so smoke is concealment, not invulnerability.
- **Sleeping creatures** wake by distance or player noise, not by sight or hearing (1.2): a smoke-hidden player who
  comes within a sleeper's wake-up range (10 m for a sleeping Draugr) still wakes it. Once awake it cannot find him
  while he stays hidden. README "Good to know"; test T25; keeping them asleep is in Later.
- **Raids** (`HuntPlayer`): vanilla tracks the player's real position without sensing (`MonsterAI.cs:524`) and the
  `FindEnemy` fallback ignores senses. They keep homing in; the smoke only stops them seeing (and so attacking) until
  they are within 2.5 m inside the cloud (decision D14).

### 2.10 Attacks reveal (G7)

- **What counts as a hit:** on the victim's owner, the `Character.RPC_Damage(long, HitData)` prefix of 2.1 notes a
  reveal when a cloud is loaded, the victim has creature AI (`m_baseAI != null`), the attacker (`hit.GetAttacker()`)
  is a `Player`, vanilla's early exits before damage do not apply (owner, alive, not teleporting, not in a cutscene,
  not dodging a dodgeable hit), and **`hit.GetTotalDamage() > 0` before resistances**: any damage type, including
  fire-, poison- and spirit-only hits (vanilla takes them out before `ApplyDamage`, 1.6) and hits that the victim's
  resistances or NG+ armor absorb. The postfix then writes `Revealed[(victim, attacker)] = now + RevealSeconds`
  (default 8 s, renewed by every such hit) when the victim is still alive. This is the same "the player hit it" test
  as Creature Morale's provocation (6.1): a hit that makes an afraid creature target the player also lets it see him
  through the smoke (decision D31). A hit landed while no cloud is loaded on the victim owner's game is not remembered
  (it keeps every other hit at two reference checks; the burst clears the reveals of the creatures it blinds anyway),
  so the README and the `RevealSeconds` description say "a creature you hit while a smoke cloud is nearby".
- Not `MonsterAI.OnDamaged`: it only runs for more than 0.1 damage after resistances, fire, poison and spirit not
  counted (1.2, 1.6), so a fire-only hit, a fully resisted hit or a Tower Shield Wall bash in NG+ (armor turns 4.8
  into 0.06) would leave the player hidden from a creature that Creature Morale just made target him.
- Only hits with an attacker count: burning and poison ticks carry none (1.2), so they neither reveal nor renew the
  reveal. A creature set on fire from inside the smoke loses sight of the player 8 s after the last direct hit, while
  the burn keeps it alerted without seeing him.
- For a hit that deals damage, vanilla then alerts and targets the attacker (`MonsterAI.OnDamaged`); the reveal lets
  it see and hear him through the smoke, so it fights. For a hit that deals no damage after resistances, vanilla
  neither alerts nor targets (a burn or poison it caused alerts the creature later, without a target): the reveal
  only lets the creature's next search find the player (with Creature Morale it targets him at once, 6.1). For an
  animal, the reveal lets its next search find the attacker, so it flees from him. Only the creature that was hit is
  revealed (vanilla does not alert its friends either).
- The Smoke Screen never reveals: its hit carries no damage at all (1.6). Its impact makes no noise.
- Noise from attacks is blocked by the smoke like any sound (hearing rule), but an arrow impact near other creatures
  still points them at the shooter's position through `RPC_OnNearProjectileHit` (vanilla); they walk there and search
  (with Creature Morale, afraid creatures farther than its `NearMissRange` ignore the impact).

### 2.11 Blind and forget (G8)

- **Blind** (on each game, for the creatures it owns, once per cloud at activation): every non-boss `MonsterAI` in
  `BaseAI.BaseAIInstances` with `m_nview.IsOwner()` that is **chasing a player near the burst**: its
  `m_targetCreature` is a `Player` whose traced point is within `radius + BlindMargin` (default 5 m, so 9 m) of the
  cloud's axis and within the same margin of its height span. It gets `BlindUntil = CloudStart + BlindSeconds`
  (default 6 s), capped at the cloud's end + fade (`CloudDuration` + 3 s: the registry forgets every blind when its
  last cloud goes, so a longer blind would not hold), and its earlier reveals are cleared (the burst removes the
  visibility a hit gave). While blinded it
  cannot see or hear **any** player, except one who hits it after the blind started (2.10).
- Creatures chasing someone farther away are not blinded, even when they stand near the cloud: a teammate fighting a
  creature 10 m from the smoke keeps his fight. Creatures that are alerted but chase nobody (searching) and unalerted
  creatures are not blinded: they cannot find a hidden player anyway, which also keeps "throw it next to an unaware
  group, then walk up" from being a free pass (decision D13).
- **Animals** (`AnimalAI`) are not blinded: they flee from their target's real position whether they sense it or not
  (1.2), so a blind would change nothing. An animal already fleeing keeps fleeing; the smoke only stops new animals
  from spotting a hidden player.
- **Forget** (`MonsterAI.UpdateTarget` postfix, the creature's owner): when its target is a `Player`, it has not
  sensed that target for `ForgetSeconds` (default 3 s; vanilla's own `m_timeSinceSensedTargetCreature`), and the
  target is smoke-hidden from it or it is blinded, set `m_timeSinceSensedTargetCreature = 31`. On the next tick
  vanilla's give-up runs: not alerted, target cleared, 5 s before the next search (1.2). Other code that hooks the
  give-up still sees it happen. Hunting creatures (`HuntPlayer`: raids, hunt spawns) never give up: vanilla re-alerts
  them every tick, which puts the timer back to 0 (1.2), so neither our forget nor vanilla's 30 s give-up happens; the
  smoke and the blind only stop them seeing and attacking the player (D14). A projectile that lands near a pursuer
  (`RPC_OnNearProjectileHit`) also puts its timer back to 0, so its 3 s start again.
- **Timeline of an escape** (defaults): t 0 impact; 0.5 s active, creatures chasing a player within 9 m of the cloud
  are blinded and stop attacking; until about 3.5 s they run to where they last sensed their target (inside the cloud
  when it was thrown at the player's feet: blinded, they cannot see anyone there even up close); about 3.5 s they give
  up (no alert icon, no target) and walk back toward where they spawned; 6 s the blind ends; about 8.5 s their next
  search, which must see the player again (unalerted: front half only, stealth factor applies); until 15 s a player
  in the cloud stays hidden from outside. A player who stays at the burst point can still meet a creature there after
  the blind (2.5 m rule inside the cloud): moving off is the safer play (README).
- **Backstab after forgetting:** a creature that gave up is unalerted, so the next hit can be a vanilla backstab
  (vanilla: once per 300 s per creature instance). This is the "ambush" loop the mod is named after; sneak-attack XP
  from it is limited to once per 300 s per creature by the ZDO cooldown (2.1). Flagged (decision D13).
- Raids and other hunting creatures re-alert every tick, so they never give up the chase (see Forget above): they
  keep homing in on the player's real position (2.9) and, inside the cloud, find him within 2.5 m.

### 2.12 Health bars (G9, E2)

`EnemyHud.TestShow(Character, bool)` postfix, every frame per character, cheap exit when `__result` is false or no
cloud is registered. For a character that is not a player, not a boss and not tamed (the smoke hides players only,
D15: hiding a tame's bar would tell its owner something false), with the local player's traced point and the
character's `GetCenterPoint()`:

- `HealthBars = OnlyInside` (**default**, the request): hide when the character is inside an active cloud and the
  local player is not inside that cloud.
- `ThroughSmoke` (E2): hide when the geometry of 2.9 says hidden: also creatures outside while you are inside, and
  through a cloud between.
- `Off`: vanilla.

No blind and no reveal here (those are creature states): a creature in the smoke that fights you while you stand
outside has no bar until it comes out, which is the literal rule. The whole plate goes, alert icons included; with the
default, a player hiding in the smoke still sees the plates and alert icons of creatures outside, which is the feedback
that an escape worked. `UpdateHuds` removes the plate within a few frames (one plate per frame, 1.5). Once the
character passes the test again, vanilla makes a new plate, which shows only after the player aims at the creature
again (a new plate starts hidden, 1.5).

### 2.13 Always-on content and the throw guard (G11, E5)

- Registration patches (`ObjectDB.Awake`, `ObjectDB.CopyOtherDB`, `ZNetScene.Awake` postfixes) and the XP RPC
  registration (`Player.Awake` postfix, 2.1) are marked `[AlwaysOnPatch]` and applied once at plugin start under their
  own Harmony id (framework change, section 7.6). They run whatever `Enabled` says, in the menu, in game and on the
  dedicated server, so Smoke Screens never disappear from inventories while the mod is installed.
- Idempotent: a prefab is added to `ObjectDB.m_items`, `m_itemByHash`, `m_itemByData` only when its name hash is not
  registered yet; to `ZNetScene.m_prefabs` and `m_namedPrefabs` only when `!HasPrefab(hash)`; the recipe only when no
  recipe of that name is in `m_recipes`.
- **Missing `BombSmoke`:** an empty database is skipped silently (the main menu's first `ObjectDB.Awake` runs on
  empty lists before `CopyOtherDB` fills them, 1.7); the `CopyOtherDB` postfix then does the work. Only a populated
  database (`m_items.Count > 0`, or a `ZNetScene` with prefabs) without `BombSmoke` (a game update) logs one error: no
  item, the feature's Status says so, everything else of the mod still works.
- **Throw guard** (`[AlwaysOnPatch]` prefix on `Humanoid.StartAttack(Character, bool)`): for the local player, when
  the feature is not active and the current weapon is a Smoke Screen, refuse the attack and show (at most every 3 s)
  "Smoke Screen does nothing here: Sneak Ambush is turned off (or the server does not have it)." Without it, a
  throw on a vanilla server would spawn objects other games cannot create. While the rules are pending (4.3) it
  refuses too, with "Smoke Screen: waiting for the server's Sneak Ambush settings." (normally never seen: the rules
  arrive before the player spawns).
- **Uninstalling** removes Smoke Screens from inventories the next time they are loaded and saved, and from chests the
  next time an owner changes them (vanilla drops unknown items, 1.7). README "Good to know": use or drop them first.
  Chests are safe while the mod is installed, even with `Enabled = false`.

---

## 3. Decisions

Flagged items deviate from the user's literal words or pick a vanilla-consistent option over the literal request;
they are repeated at the pause.

| # | Decision | Options | Choice and reason |
|---|---|---|---|
| D1 | Smoke item | New item / earlier recipe for the vanilla Smoke Bomb | **New item `Smoke Screen`** (user decision). Prefabs `MC_SmokeScreen`, `MC_SmokeScreen_projectile`, `MC_SmokeScreen_cloud`, permanent. |
| D2 | Vanilla Smoke Bomb | Also hides / stays vanilla / option | **Stays vanilla, no option in v1** (user default; Later lists a default-off option). |
| D3 | Recipe | Meadows-only / Black Forest / later | **Workbench level 1: 2 Resin + 1 Coal + 1 Leather scraps → 2.** Early (Coal comes from the first Black Forest crypts or a kiln), thematic (resin smoke, soot), cheap enough to use, not free. All server settings. **Flag:** user to confirm cost and tier. |
| D4 | Cloud size and time | | **Cylinder 4 m radius, 4 m high, 15 s, active after 0.5 s, 3 s visual fade.** Covers a small group; a Troll's head may stick out of the top (it then counts as outside). Settings. |
| D5 | Both inside the cloud | Vanilla sight / fully hidden / close range only | **Notice each other only within 2.5 m** (`InsideSightRange`). Keeps fights in the smoke possible and pursuers able to bump into a player who stays put; vanilla mist has the same idea (never blocks under 10 m). |
| D6 | Smoke blocks hearing | Yes / no | **Yes** (`BlocksHearing`), same geometry: the request says "completely hides". Sleeping creatures still wake by distance (D30). |
| D7 | Smoke between two outside points | Blocks / does not | **Blocks** (`BlocksLineOfSight`). **Flag: added beyond the request** (E1); it is what a smoke screen does. |
| D8 | Reading of G3's parenthesis | The player's stillness lowers noticing / moving creatures notice less | **The player's stillness** (player-side, visible in the bar, replicated for free). The observer-movement rule is in Later. **Flag.** |
| D9 | Stillness strength and end | Flat / skill-scaled; ramp / instant end; what counts as still | **Flat 70% after 1 s, ends at once on movement** (stop-and-go fix). "Very effective": at 50% a Sneak-0 player in daylight was still noticed by anything passing within 12.8 m, since an unalerted creature that sees you walks over (1.2); 70% brings that to 7.7 m (3.8 m at night). Still also needs no world movement (a ship or cart carrying you does not count). Settings. **Flag:** the number. |
| D10 | "Same end-game power" | Curve only / everything | Applies to the **base curve** (exactly vanilla at 100). Stillness, fog and the smoke are separate requests and help at every level. **Flag.** |
| D11 | Bushes | Surface the vanilla effect only / add a number | **Surface only by default:** icon and tooltip for the vanilla sight block and shade (in or under foliage), no extra number (`FoliageBonus` 0, kept as a server option). Vanilla already has a bush bonus, and the user asked to create one only where none exists. **Flag.** |
| D12 | Fog | Factor bonus (player side) / sight cap (AI side); source of the density; which fog counts | **Factor bonus up to −30%, from the current environment's fog data (not the render state), outdoors only.** Replicates through the factor, shows in the bar, and graphics or fog mods cannot change it. Biome haze counts (Swamp 4-9%, Mistlands 4-17%, Deep North up to 26%): it is fog you see. Dungeons are excluded (their fog draws darkness in closed rooms and would give the full bonus everywhere inside). **Flag:** fog is read on the sneaking player's game, not the creature owner's. |
| D13 | Blind whom, and un-alerting | All creatures near / only those chasing someone near the burst; instant un-alert / timer | **Only creatures chasing a player within 9 m of the cloud** (the request: "already aggroed mobs"; a teammate's fight farther off is not touched); un-alerting through vanilla's give-up after 3 s unsensed, not instantly (a hit in that window keeps the fight going). **Flag:** a creature that gave up is unalerted, so the next hit can be a vanilla backstab (vanilla's 300 s per creature instance stays; sneak-attack XP is limited to once per 300 s per creature through its ZDO); and a smoke thrown next to a teammate's fight blinds his opponent too. |
| D14 | Raids (`HuntPlayer`) | Exempt / fooled / vanilla tracking | **Vanilla tracking kept:** they still home in on the player (vanilla ignores stealth for them) and never give up the chase (vanilla re-alerts them every tick, which also stops our forget, 2.11); the smoke only blocks their sight. Vanilla-consistent. |
| D15 | Who is hidden | Players / players and tames | **Players only** (the request). |
| D16 | Bosses | `IsBoss()` / faction Boss / list | **`Character.IsBoss()`** (the 12 prefabs in 1.2); Aspects, Hildir's bosses and minibosses are fooled. Their bars are never hidden. |
| D17 | Health bars | One way / both ways | **One way by default** (`HealthBars = OnlyInside`, the literal request); both ways (`ThroughSmoke`) is a setting (E2). The plate also carries the alert icons: one way keeps them visible for creatures outside while you hide in the smoke. |
| D18 | Status icon for smoke | Yes / no | **Yes, "In smoke"** (E3), local only. |
| D19 | Sneak-attack XP amount | Flat / weapon backstab / victim HP / flat + HP | **Flat 3 + 10 per 100 base health (× stars), health part capped at ×3, half for ranged, once per 300 s per creature (ZDO).** Every sneak attack pays something visible early (a Boar 4, a Greydwarf 7), big creatures more (Troll 33); the backlog's "no floor" came from the Mob AI revamp's weak creatures handing out free backstabs, which Creature Morale blocks (an afraid creature that sees you gives none, 6.1). Computed by the attacker's game from its (the server's) rules. **Flag:** the numbers. |
| D20 | Other sneak-XP mods | Stand down / pay both | **Stand down** when SecondaryAttacks or SmartSkills (GCO once its GUID is known) is loaded on the attacker's game; `PayAlongsideOtherSneakXpMods` overrides. |
| D21 | Content registration | Gated by the toggle / always on | **Always on** (user decision), through a generic framework attribute (7.6). The recipe and the hiding follow the toggle; the throw guard refuses throws while inactive. |
| D22 | Strings | `$` tokens + reflection / English literals | **English literals** (every MC mod does; `Localization.AddWord` is private in a non-publicized assembly). Translation in Later. |
| D23 | Icon | Vanilla icon / tinted / custom | **Vanilla Smoke Bomb icon** (no custom assets; the vanilla bomb is Ashlands-tier, so rarely both in one bag early). |
| D24 | Blind and reveal storage | Owner-local memory / creature ZDO | **Owner-local**, short windows (6-8 s). An ownership change mid-window forgets them (documented, Later). |
| D25 | Cloud persistence | Persistent / not | **Persistent**: vanilla hands the cloud over when its thrower leaves the area, teleports, dies or disconnects, so it keeps hiding the players in it until its end; only an owner destroys it (a non-owner claims first). A cloud caught by a world save is inert after loading and destroyed by the first game that loads it. |
| D26 | Choking and fire | Vanilla smoke puffs / visual only | **Visual only:** no `Smoked` (it would hurt and so un-crouch the player), no fire or chimney side effects, no world-wide puff cap. |
| D27 | Early-game help axis | Visibility only / visibility and sneak stamina | **Visibility only in v1** (the stealth curve, 2.2); sneak stamina stays vanilla. **Flag / open question 8:** vanilla's early-game pain is also the sneak stamina drain (4× the Sneak-100 drain at Sneak 0); a cut that fades out at Sneak 100 is a small addition if wanted. |
| D28 | Turrets and NPCs | Fooled by smoke / not | **Not fooled:** the smoke rule applies only to observers with creature AI. Ballistas target players by default, so fooling them would be PvP concealment (a non-goal). |
| D29 | A Smoke Screen hitting a creature | Vanilla zero-damage hit / no hit at all | **Vanilla zero-damage hit:** the creature stays unaware, but the throw counts as the thrower's hit (the `EnemyHits` stat, kill credit if it dies soon), like a harpoon. Avoiding it needs a prefix on `Projectile.OnHit` that copies vanilla logic. |
| D30 | Sleeping creatures | Vanilla wake-up / kept asleep | **Vanilla wake-up** (by distance and noise, not senses): a hidden player can wake a sleeper but it cannot find him. Documented; Later sketch. |
| D31 | Which hits reveal (G7) | Vanilla `OnDamaged` (more than 0.1 damage after resistances) / any hit by a player that carries damage before resistances | **Any hit carrying damage before resistances** (`Character.RPC_Damage`, 2.10), the same test as Creature Morale's provocation: fire- or poison-only hits, fully resisted hits and NG+ tower bashes reveal too. With `OnDamaged`, such a hit would make an afraid Morale creature target a player it still cannot see, and the literal request says "a creature the player hits". The Smoke Screen (no damage) never reveals. |
| D32 | A client waiting for the server's rules | Its own config / vanilla | **Vanilla** (4.3): no stealth bonuses, no XP, recipe hidden, smoke inert, throws refused. A client's own gameplay numbers never apply on a server with the mod, and the stealth factor it would compute replicates to creatures other games own. Same policy as Creature Morale, Tower Shield Wall and Weapon Moveset. The wait normally ends before the player spawns. |
| D33 | Players with the mod turned off or in another network version | Let in (only "installed" counts) / refused like players without it | **Refused like players without the mod**, about a second after joining or after turning it off while connected, unless `AllowPlayersWithoutMod` (4.3). Such a player's game would ignore smoke for the creatures it controls, and their stealth would follow vanilla, so the server's rules would not apply to everyone. Uses the framework's `NetworkGate.PeerCompatible` and `PeerStateChanged` (7.6). |

---

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Runs on | Reaches others through |
|---|---|---|
| Early curve, stillness, foliage, fog, snap, floor | the sneaking player's game (`Player.UpdateStealth`, `SEMan.ModifyStealth`, `EnvMan.SetEnv`) | vanilla ZDO `Stealth`: every creature owner reads it (`Player.GetStealthFactor`) |
| Stealth cues (icons), sneak-attack message | the local player's game | nothing (local status effects, local message) |
| Backstab detection, XP cooldown | the victim's owner (`Character.RPC_Damage`) | routed RPC to the attacker's game; victim ZDO `LastXp` |
| XP grant | the attacker's game | nothing (skills are local) |
| Throw, projectile, cloud creation | the thrower's game | the cloud's ZDO (persistent, first owned by the thrower, handed over by vanilla) |
| Smoke rule (sight, hearing), reveal, blind, forget | each creature's owner (always a player's game, a host's included; never a dedicated server, 1.7), from its local copy of every nearby cloud | creature ZDO `alert` / `haveTarget` (vanilla) |
| Health bars | each player's own game | nothing |
| Recipe | each game, with the server's rules | nothing |
| Registration (item, projectile, cloud prefabs), XP RPC handler | every game **and the dedicated server** (always on) | prefab hashes in ZDOs and inventories |
| Join check (and its live re-check), rules | server (dedicated or host) | framework handshake (which carries whether the player's copy is on) + `Settings` RPC |

### 4.2 RPCs, ZDO keys, network version

- `MC.Combat.Sneak.Ambush.SneakAttackXp`: routed `ZNetView` RPC on the attacker's `Player` object; payload `(float
  victimHealth, bool ranged)`; sender: the victim's owner; handler on the attacker's game (owner of the Player ZDO),
  registered on every Player by the always-on `Player.Awake` postfix. A game without the mod logs vanilla's "Failed
  to find rpc method" (only possible with `AllowPlayersWithoutMod`); a game with the mod turned off has the handler
  and ignores the call.
- `MC.Combat.Sneak.Ambush.SettingsRequest` (`ZRpc`, `int layout`) and `MC.Combat.Sneak.Ambush.Settings` (`ZRpc`,
  `ZPackage` of `AmbushRules`, layout 1): copy of Forge Idol Upgrades' `ServerRules`.
- Framework (`NetworkGate`, `ZRpc`): `MC.Combat.Sneak.Ambush.Hello` (client → server right after connecting: protocol,
  network version, version, and whether the client's copy is on), `.HelloAck` (server → client: the server's state;
  sent again to every player when the server's copy turns on or off) and `.HelloState` (client → server, `"on"` /
  `"off"`, when the client's copy turns on or off while connected).
- ZDO keys: the four cloud keys of 2.8, on the cloud ZDO; `MC.Combat.Sneak.Ambush.LastXp` (long, network time in ms of
  the last sneak-attack XP payout) on a creature's ZDO, written by its owner (2.1). No key on players (vanilla
  `Stealth` carries the stealth changes).
- `ModNetworkVersion` = 1. Bump it when the XP RPC payload, the rules layout, the ZDO keys or the prefab names
  change.

### 4.3 Server settings and join check

- Every setting except `ShowStealthCues`, `ShowSneakAttackMessage` (personal) and `AllowPlayersWithoutMod` (server
  only) is a **rule**: `AmbushRules` snapshots them; game code reads them only through `ServerRules.Current` (a Debug
  in-memory override for self-tests first):
  - single player, host and dedicated server: their own config;
  - a client: the server's rules once they have arrived (same ZNet session), and **`AmbushRules.Pending` until
    then** (decision D32). The client clamps received values to the config ranges. An unknown layout is refused with
    one warning: the last good rules of the server stay in use, or the client stays pending if none came before.
    This differs from Forge's copy, which uses the client's own config meanwhile.
- **Pending** (`AmbushRules.Pending`, built in code, never read from the config and never sent): the feature acts as
  vanilla apart from its always-on content. The `SEMan.ModifyStealth` postfix changes nothing (no early curve, still,
  foliage or fog bonus, no floor) and no cue icon is shown; the victim's owner sends no sneak-attack XP and writes no
  `LastXp`; the XP handler ignores calls; the recipe is disabled; `SmokeRegistry.Hides` answers "not hidden" (no
  blind, no forget); health bars stay vanilla; the throw guard refuses throws with its waiting message (2.13).
  Personal settings still apply. The client's feature is already Active while it waits (the framework turns a Both
  mod on while the handshake is pending and once the server says "on"; the settings request only goes out after
  the handshake), which is why the rules need this state. In practice the answer arrives before the player spawns,
  so nobody plays in it; it matters when the answer is late or unreadable. Same policy as Creature Morale (hostile
  while pending), Tower Shield Wall (no tower data) and Weapon Moveset (moves off); Dual Wielding differs on purpose
  (its `Player.Load` equips before the answer).
- **`PlayerCheck`** (server, while the feature is Active; never in single player, never the host's own player): a
  copy of Forge Idol Upgrades' join check with the framework's new verdict and a live re-check.
  - **When:** the `ZNet.RPC_PeerInfo` postfix schedules a ready peer; the check runs after the 1 s grace
    (`GraceSeconds`). `PlayerCheck.Start` (from `OnActivated`) schedules every connected peer and subscribes to
    `NetworkGate.PeerStateChanged` (after unsubscribing, so it is never added twice); `PlayerCheck.Stop` (from
    `OnDeactivated`) unsubscribes and clears the queue. The handler (it catches its own exceptions) calls
    `Schedule(peer)`, so a player whose copy turns off while connected (MC Mods panel, `Enabled = false` in the
    file, or an error that stops it) is checked again with the same 1 s grace. A peer already queued keeps its
    first deadline, so turning it off and on again within the grace passes.
  - **Verdict:** `PlayerCheck.Decide(isServer, connected, ready, beingKicked, compatible, allowWithoutMod)` (pure) →
    Skip / Compatible / Allowed / Refuse, with `compatible = NetworkGate.PeerCompatible(peer)`: the player has the
    mod, the same `ModNetworkVersion`, and has not turned it off on their game.
  - **Refuse** (not compatible, `AllowPlayersWithoutMod` off): vanilla `Error` RPC with `ErrorVersion` (their game
    shows "Incompatible version" and goes back to the menu) and the peer in vanilla's kick list, disconnected 4 s
    later (`DisconnectDelay`, so the client reads the error first). Server log (Warning): "Refused <player>:
    <reason>. This server requires Sneak Ambush on every player (everyone plays by the same stealth and smoke rules).
    Their game shows "Incompatible version". To let such players in, set AllowPlayersWithoutMod = true." The reason
    is `NetworkGate.PeerProblem(peer)`: "does not have the mod", "has another version of the mod (network version X,
    the server has Y)" or "has the mod turned off".
  - **Allowed** (not compatible, setting on): Warning "<player> <reason>; AllowPlayersWithoutMod is on, so they may
    play, but creatures their game controls ignore smoke and give nobody Sneak XP for sneak attacks, and their
    stealth follows the normal game." (Their game runs no `Character.RPC_Damage` patch, so a backstab on a creature it
    controls is never detected, 4.4. A player without the mod also loses any Smoke Screen that reaches their
    inventory.)
  - **Compatible:** a Debug line.
  - Setting `AllowPlayersWithoutMod` switched back to off, or the server's copy turned on → every connected peer is
    checked again (Forge's flow).
  - Nothing else: no mod-specific RPC or timer detects a turned-off copy or another version; the framework's hello
    and `HelloState` carry it (7.6).
- README "Good to know": on a server that requires Sneak Ambush, turning it off in the MC Mods panel while connected
  disconnects you about a second later.
- Proposed `ModMultiplayerNotes` (csproj): "Install it on the server (or the host) and on every player's game. It
  changes combat for everyone, so the server refuses players who do not have the mod, have it turned off or have
  another version of it (their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on,
  and the settings of the server (or host) apply to everyone. Smoke Screens only exist for games with the mod: a
  player without it loses any Smoke Screen that reaches their inventory, and a chest loses its Smoke Screens when
  such a player takes or adds an item in it. On a server without the mod, Smoke Screens dropped on the ground can be
  deleted."

### 4.4 Hand-off cases

| Situation | Result |
|---|---|
| A crouched still player with the mod near creatures owned by a game **without** it | The bonuses still work: they are in the stealth factor, which vanilla AI reads. |
| A cloud near creatures owned by a game without the mod | Those creatures ignore the smoke (vanilla AI). That game shows no cloud and logs the vanilla warning "Missing prefab hash" on every create pass (about 30 per second) while it is near the cloud; the same for a projectile in flight. Test M06. |
| A backstab on a creature owned by a game without the mod (or with it turned off) | No detection there: no XP for anyone, players with the mod included. Said in the server's Allowed warning, the `AllowPlayersWithoutMod` description and the README. |
| XP RPC to a player without the mod | Ignored, with vanilla's "Failed to find rpc method" warning in their log. The creature's XP cooldown (`LastXp`) is used up all the same (2.1). |
| **A Smoke Screen in a chest, opened by a player without the mod** | Their game drops the unknown item from the chest it loaded. Opening and closing saves nothing, so the chest keeps it. If they **take or add any item** while their game owns the chest, the chest is saved without it: **the Smoke Screens are gone for everyone** (vanilla `Container`, 1.7). Test M06. |
| A Smoke Screen dropped on the ground, near a player without the mod | Invisible to them, with the same warning spam in their log while near; it stays in the world for others. |
| Server without the mod | Framework: the feature is inactive on every client. Registration still runs locally, the recipe is hidden, the throw guard refuses throws; Smoke Screens stay in inventories. A Smoke Screen dropped on the ground is deleted by a **vanilla host** once it is in the host player's active area ("Destroyed invalid prefab ZDO" in the host's log, 1.7). A **vanilla dedicated server** never deletes it: it creates no world objects, and at world load it keeps unknown prefabs with a warning (1.7), so the Smoke Screen stays in the save. Test M08. |
| Player with the mod installed but turned off (`Enabled = false` at join, or unticked while connected) | **Refused** about a second after joining, or about a second after turning it off (their game shows "Incompatible version"; the server log says "has the mod turned off"), unless `AllowPlayersWithoutMod` (4.3, D33). With the setting on they stay: their stealth bonuses are off, the creatures their game owns ignore smoke and pay nobody sneak-attack XP, they cannot throw (guard); the XP handler exists (always on) and ignores calls, so no warnings, but their sneak attacks on creatures a game with the mod controls use up that creature's XP cooldown (2.1). Tests M12, M15. |
| Player with another network version of the mod | **Refused** about a second after joining (server log: "has another version of the mod (network version X, the server has Y)"), unless `AllowPlayersWithoutMod`. With the setting on, their copy stays inactive (framework status: the versions cannot talk to each other) and behaves like one turned off; its always-on registration still keeps their Smoke Screens. Test M16. |
| A client whose server rules have not arrived yet | Pending (4.3): vanilla stealth, no XP, smoke inert on creatures it owns, throws refused with the waiting message. Normally over before the player spawns. Self-test `sneak.pending`. |
| Ownership of a creature changes during a blind or reveal | The new owner does not know it: vanilla senses plus the smoke geometry apply. Short windows (6-8 s). |
| Ownership of a creature changes, or it is reloaded, after a sneak attack | Vanilla's backstab cooldown restarts on the new instance, but the XP cooldown is in the creature's ZDO: no second payout within 300 s. |
| The thrower teleports away, dies and respawns far away, or runs out of the area | The persistent cloud is handed to a game near it within about 2 s: it keeps hiding the players in it until its end, then its new owner destroys it. Test M10. |
| The thrower disconnects | Same hand-over: the cloud stays until its end. Test M07. |
| The cloud's new owner is a game without the mod | That game never destroys it; a game with the mod claims it 5 s after its end and destroys it. |
| A late-joining player near an active cloud | Gets the ZDO with start and duration: same remaining time as everyone; blind applies if still within the blind window. |
| Creatures, clouds and dropped Smoke Screens on a dedicated server | The server never owns or creates them (1.7): some player's game always runs the creatures near a cloud, so the smoke rule, blind, forget, reveal and XP detection run there with the server's rules (4.3), and that game (or another with the mod) destroys the cloud at its end. Test M13. |
| A mod that makes a dedicated server simulate the world (server-side simulation mods, names not checked) | The server then owns and creates objects near players like a host: it has the mod (required), so it knows the prefabs (no "Destroyed invalid prefab ZDO") and runs the smoke rule, blind, forget, reveal and XP detection for the creatures it owns, with its own rules, from its own instance of the cloud. Not tested. |

### 4.5 Dedicated server

Loads the mod (Both: no `BepInProcess`). Runs `PlayerCheck` (the join check and the re-check when a player turns the
mod off), `ServerRules` (its own config, never pending) and the always-on registration. Its reference position stays
far outside the world (1.7), so it **creates no world objects and owns no creature, cloud, projectile or dropped Smoke
Screen**: the players' games run all of it (smoke rule, reveal, blind, forget, backstab detection and the XP RPC,
cloud expiry) with the server's rules, and the server only stores and routes their ZDOs and RPCs. It has no local
player, no weather and no HUD, so the stealth, fog, cue and health-bar code does nothing there either.

What the always-on registration does there: the world load already knows the three prefabs, so the server's start-up
log does not list saved Smoke Screen objects (dropped Smoke Screens, clouds caught by a save) as unknown prefabs (1.7;
a vanilla dedicated server keeps them too, with that warning). And with a mod that makes a dedicated server simulate
the world, the server creates objects near players and, without the registration, would delete them as unknown
prefabs like a vanilla host (4.4). `Enabled = false` on the server turns the feature off for every client (framework)
but keeps the prefabs registered (tests M13, M14).

---

## 5. Config

`BepInEx/config/MC.Combat.Sneak.Ambush.cfg`. "Synced" = the server's value is used by every player while connected.
User-facing descriptions end with "In multiplayer the setting of the server (or host) is used for everyone." when
synced.

| Section | Key | Default | Range | Synced | Description |
|---|---|---|---|---|---|
| General | `Enabled` | true | | (framework) | Turn this feature on or off. Takes effect immediately, no restart needed. Smoke Screens stay in your inventory while it is off. |
| General | `Status` | | read-only | | Written by the mod. |
| General | `AllowPlayersWithoutMod` | false | | server only | Off: a player whose game does not have this mod, has it turned off or has another version of it is refused about a second after joining, or after turning it off ("Incompatible version"). On: they may play; creatures their game controls ignore smoke and give nobody Sneak XP for sneak attacks, and a player without the mod loses any Smoke Screen that reaches their inventory. |
| Sneak attacks | `SneakAttackXpFlat` | 3 | 0-50 | yes | Sneak XP every sneak attack gives, in seconds of sneaking near unaware enemies, before the bonus for the creature's health. |
| Sneak attacks | `SneakAttackXp` | 10 | 0-50 | yes | Extra Sneak XP for a sneak attack on a creature with ReferenceHealth health. Bigger creatures pay more, up to MaxHealthScale times this; small ones less. Stars count, world level does not. |
| Sneak attacks | `ReferenceHealth` | 100 | 10-2000 | yes | Health of a creature that pays exactly SneakAttackXp extra. |
| Sneak attacks | `MaxHealthScale` | 3 | 0.1-10 | yes | Cap on the health bonus (a 600-health Troll pays 3 × SneakAttackXp extra by default). |
| Sneak attacks | `RangedXpFactor` | 0.5 | 0-1 | yes | Share of the XP for sneak attacks with arrows, bolts and thrown weapons. |
| Sneak attacks | `SneakAttackXpCooldown` | 300 | 0-3600 s | yes | A creature pays sneak-attack XP at most once in this time, whoever hits it. A sneak attack counts even when the attacker's game pays nothing for it. |
| Sneak attacks | `PayAlongsideOtherSneakXpMods` | false | | yes | Off: when SecondaryAttacks or SmartSkills is installed, they pay sneak-attack XP and this mod does not. On: both pay. |
| Sneak attacks | `ShowSneakAttackMessage` | true | | no (personal) | Show "Sneak attack!" and the Sneak progress gained at the top left. |
| Stealth | `EarlyGameBonus` | 15 | 0-25 % | yes | How much harder you are to see while sneaking at Sneak 0. The bonus shrinks evenly as Sneak rises and is gone at Sneak 100, where sneaking is exactly as in the normal game. |
| Stealth | `StillBonus` | 70 | 0-90 % | yes | Crouched and not moving: creatures see you at this much less distance. |
| Stealth | `StillDelay` | 1 | 0-5 s | yes | How long you must hold still before the bonus starts. |
| Stealth | `StillEndsAtOnce` | true | | yes | On: moving ends the holding-still bonus immediately. Off: it fades out at the normal speed of the stealth bar. |
| Stealth | `FoliageBonus` | 0 | 0-90 % | yes | Extra bonus while crouched touching a bush or low branches: creatures see you at this much less distance. Foliage already blocks sight and shades you in the normal game, which the In foliage icon shows. |
| Stealth | `FoliageReach` | 0.5 | 0-1.5 m | yes | How close to foliage your body must be to count as touching it. |
| Stealth | `FogBonus` | 30 | 0-90 % | yes | Bonus in the thickest fog, outdoors. Thinner fog gives less. |
| Stealth | `FogDensityNoBonus` | 0.01 | 0-0.2 | yes | Fog density at or below which fog gives nothing. A clear night is 0.01. |
| Stealth | `FogDensityFullBonus` | 0.08 | 0.01-0.3 | yes | Fog density at or above which fog gives the full bonus. Misty dawn, dusk and night are thicker than this. |
| Stealth | `VisibilityFloor` | 0.1 | 0-0.5 | yes | This mod's bonuses never take your visibility below this (1 = fully visible). |
| Stealth | `ShowStealthCues` | true | | no (personal) | Show status icons for holding still, foliage, fog, mist and smoke. The bonuses apply either way. |
| Smoke Screen | `RecipeResources` | `Resin:2,Coal:1,LeatherScraps:1` | prefab names | yes | Materials to craft Smoke Screens (prefab names as used by the spawn command). |
| Smoke Screen | `RecipeAmount` | 2 | 1-50 | yes | Smoke Screens per craft. |
| Smoke Screen | `RecipeStation` | `piece_workbench` | prefab name | yes | Crafting station. |
| Smoke Screen | `RecipeStationLevel` | 1 | 1-10 | yes | Station level needed. |
| Smoke Screen | `CloudRadius` | 4 | 2-10 m | yes | Radius of the smoke cloud. |
| Smoke Screen | `CloudHeight` | 4 | 2-10 m | yes | Height of the smoke cloud. |
| Smoke Screen | `CloudDuration` | 15 | 3-60 s | yes | How long the cloud lasts, counted from the impact: it hides from the end of ActivationDelay until then, then fades. |
| Smoke Screen | `ActivationDelay` | 0.5 | 0-3 s | yes | Time for the smoke to build up after the impact. Part of CloudDuration; shortened so a cloud always hides at least 1 s. |
| Smoke Screen | `InsideSightRange` | 2.5 | 0-10 m | yes | Inside the same cloud, you and creatures notice each other only this close. |
| Smoke Screen | `BlocksLineOfSight` | true | | yes | Creatures cannot see you through a cloud even when neither of you is inside it. |
| Smoke Screen | `BlocksHearing` | true | | yes | Creatures cannot hear you through the smoke either. |
| Smoke Screen | `RevealSeconds` | 8 | 0-60 s | yes | A creature you hit while a smoke cloud is nearby can see you through smoke for this long (each hit starts it again). |
| Smoke Screen | `BlindMargin` | 5 | 0-20 m | yes | Creatures chasing a player who is within this distance of the cloud are blinded by the burst. |
| Smoke Screen | `BlindSeconds` | 6 | 0-30 s | yes | How long the burst blinds them, counted from the impact (they cannot see or hear any player). Never longer than the cloud and its fade (CloudDuration + 3 s). |
| Smoke Screen | `ForgetSeconds` | 3 | 0-30 s | yes | A creature that cannot sense its target because of the smoke gives up the chase after this long. Hunting creatures (raids) never give up; an arrow landing near a pursuer starts its count again. |
| Smoke Screen | `HealthBars` | `OnlyInside` | `OnlyInside` / `ThroughSmoke` / `Off` | yes | OnlyInside: you do not see the health bars of creatures inside a smoke cloud while you are outside it. ThroughSmoke: bars are hidden through smoke either way (also creatures outside while you are inside). Off: normal game. Bosses and tamed creatures always keep their bar. |

All rules apply live: recipe settings rebuild the recipe; cloud shape settings apply to the next throw; cue texts
follow the rules at once.

---

## 6. Compatibility

### 6.1 Sibling mods (same release)

| Mod | Overlap | Handling |
|---|---|---|
| **Creature Morale** (`MC.Combat.Creatures.Morale`) | Both change creature senses and targeting on the owner. A creature is **afraid** of a player who outclasses it (two bosses past its biome's boss): it never targets that player and runs from them when they come within `FearRange` (12 m) and it senses them. Morale's hooks (its 7.2): `BaseAI.CanSenseTarget(Character, bool)` postfix (an afraid or routed creature does not sense that player); `BaseAI.FindEnemy` postfix (the hunters' fallback: an afraid-of player is replaced by the closest hostile-toward player within 200 m, without a sense check, like vanilla's fallback) and `MonsterAI.HuntPlayer` postfix (night hunters stand down); `MonsterAI.UpdateAI` prefix at `Priority.Low` (rout and fear frames skip vanilla and run `BaseAI.Flee` with no target, alerted; a once-per-second check starts the fear only for a close player the creature senses through vanilla `CanSeeTarget` / `CanHearTarget`, ends it when that player is out of range or unsensed for 5 s, drops an afraid-of player as target and un-alerts startled afraid creatures; the fear frames count the cornering, which provokes and then `SetTarget` + `SetAlerted`); `MonsterAI.RPC_OnNearProjectileHit` prefix (an impact within `NearMissRange` provokes; an afraid creature farther away ignores it); `Character.RPC_Damage` prefix (E3: `hit.m_backstabBonus = 1` when an afraid creature can see the attacker and is not alerted; then provocation, and `SetTarget(attacker)` when the creature has no target, for any hit carrying damage before resistances, fire- or poison-only included); `BaseAI.InStealthRange` postfix (afraid creatures do not count); `BaseAI.OnDeath` postfix (rout); no `EnemyHud` patch (an afraid creature that runs and a routed one are alerted, so the plate shows the game's own alert icon) | **Compose.** Ours only turns `CanSeeTarget` / `CanHearTarget` results false and shortens the give-up timer; Morale turns `CanSenseTarget` false. **The fear uses our senses:** Morale starts and keeps it through `CanSeeTarget` / `CanHearTarget`, so a smoked player never starts an afraid creature's fear (he can walk up to it through the smoke), and one that ran from him before the burst stops running 5 s after it last sensed him. **Morale does create targets**, and each case goes through our rules: (1) provocation by a hit targets the attacker, and the same hit reveals him to that creature (our reveal uses Morale's own test, D31), so it fights him, as G7 wants; (2) cornering needs the creature to be running from the player, which needs `CanSeeTarget` or `CanHearTarget`, so a player who stayed in the smoke never corners it; (3) the hunters' fallback targets without sensing, exactly like vanilla's `HuntPlayer` fallback, so our raid rule applies (D14: they home in and, since the fallback only runs for `HuntPlayer` creatures, which vanilla re-alerts every tick, they never give up and our forget never fires for them; the smoke only blocks their sight). When Morale's prefix skips vanilla `UpdateAI` (rout and fear frames), `UpdateTarget` does not run and our forget does nothing that tick; routing and running creatures have no target, so a burst does not blind them (they have nothing to forget). An arrow impact near afraid creatures farther than `NearMissRange` no longer points them at the shooter (Morale), which only helps stealth. **Health-bar plates:** our `TestShow` postfix makes vanilla destroy the plate of a creature in smoke and create a new one later (2.12); Morale puts nothing on the plate (its running and routed creatures are alerted through vanilla `BaseAI.SetAlerted`, and `EnemyHud.UpdateHuds` sets the plate's alert icon from `BaseAI.IsAlerted` every frame), so the new plate shows the right icon at once and nothing has to be re-attached. **Sneak attacks and XP:** an afraid creature that **can see** you gets no backstab (Morale's prefix), so vanilla leaves `m_backstabTime` alone and our detection pays nothing, by construction; one that runs from you is alerted, so vanilla gives none either. The order of the `RPC_Damage` prefixes does not matter (Morale changes the backstab bonus, we compare `m_backstabTime` and read the damage, which Morale leaves alone). One that **cannot** see you (behind it, out of its reduced sight while you hold still, or in smoke) takes a normal backstab and pays our XP. Crawling next to afraid creatures gives the slow 0.1/s vanilla rate through Morale's `InStealthRange` rule (Morale owns it; we do not patch `InStealthRange`). Morale has no `RPC_Damage` postfix, and neither mod patches `MonsterAI.OnDamaged`. Test X01 (= Morale's X01). |
| **Tower Shield Wall** (`MC.Combat.Shields.TowerWall`) | Crouch and the bash; `Character.RPC_Damage` (its prefix changes only the bash's stagger multiplier and `m_blockable` on its own bearer's incoming hits, its finalizer puts its per-call state back); `Character.ApplyDamage` prefix/postfix (the bash's stagger, added after vanilla) | Blocking ends crouch (vanilla `UpdateCrouch`), so holding the wall ends sneaking and the still bonus. The bash has backstab ×1 (its design), so it never backstabs and, by our rule (XP only for vanilla backstabs, 2.1), never pays Sneak XP. A bash carries damage before armor, so from inside the smoke it reveals the bearer to the bashed creature (D31), also in NG+. **Contract for NG+:** when NG+ armor leaves 0.1 damage or less, vanilla skips `OnDamaged`, so the bashed creature would stay unalerted and the next hit would be a vanilla backstab (×2 more on a staggered creature) that pays our XP. Tower Shield Wall alerts the creature and targets the bearer after such a bash (its `ApplyDamage` postfix calls the `m_onDamaged` that vanilla skipped, once), so the hit after a bash is never a sneak attack; we pay XP for any vanilla backstab and do not special-case the bash. Its bash is now slower (the hit lands about 0.8 s after the press) and limited to one every 2 s; neither changes this row. Tests X02 (= Tower's C08) and its NG+ variant. |
| **Dual Wielding** (`MC.Combat.Weapons.DualWield`) | One-handed items; backstabs by either hand | A Smoke Screen is a OneHandedWeapon with skill None and a projectile attack, like vanilla bombs: Dual Wielding must never pair it (its design: equipping it puts the pair away). Only the first hand's hit on an unaware creature backstabs (vanilla's per-creature cooldown), and the pair pays XP once per creature per 300 s (our ZDO cooldown). Our guard prefix on `Humanoid.StartAttack` returns before `Attack.Start` only while our feature is inactive or its rules are pending. Test X03. |
| **Weapon Moveset** (`MC.Combat.Weapons.Moveset`) | `Attack.Start` clone edits | A Smoke Screen throw must stay a plain throw: Moveset excludes skill None / projectile consumables and lists `MC_SmokeScreen` in its exclusion test (its design). A sneak roll into a roll attack (which now flows straight out of the end of the roll) can backstab and pays our XP like any backstab (with Creature Morale on, an afraid creature that sees the player gives none, Morale's E3). Test X04 (= Moveset's X04). |

### 6.2 Other MC mods

- **Harpoon Hooks Tames** (`Character.Damage` prefix, `Character.RPC_Damage` prefix/postfix): same method as our
  detection, different fields; its hook sets the backstab bonus to 1 and tames are excluded anyway. Test X05.
- **Compendium Encyclopedia** (`EnemyHud.UpdateHuds` postfix counts shown plates as "met"): a creature only ever seen
  through smoke is met later. It may list the Smoke Screen as an item (literal name). Test X06.
- **Forge Idol Upgrades** (`ObjectDB.Awake` / `CopyOtherDB` postfixes): independent entries in the same lists. Test X07.
- **Crossbow Stays Loaded**: no shared method; a crossbow backstab is ranged (half XP). Test X08.
- **Creature Kill and Tame Counts** (`Stats.PerCreature`) and Creature Morale's kill counts: they credit every player
  who hit a creature (vanilla "attackers" flag). A Smoke Screen that hits a creature is such a hit (D29): if a teammate
  then kills it, the thrower also gets the kill, as with a harpoon. Test X09.
- **Sort Chest / Crafting Search and Sort** (`ItemKinds`): Smoke Screen is a Weapon like vanilla bombs.
- **Loot Pickup Filter**: lists the Smoke Screen like any item.
- **Lights Switchable**: lights you switch off make an area darker, which helps sneaking (vanilla light rule).

### 6.3 Popular external mods

| Mod (GUID) | Overlap | Reaction |
|---|---|---|
| SecondaryAttacks (`sighsorry.SecondaryAttacks`) | Backstab XP (same detection); `Player.UpdateStealth` postfix that recomputes the target (factor 2 by default) and calls `SEMan.ModifyStealth`; knife "Sneak Ambush" aggro reset | Our XP stands down (D20). Our bonuses still apply (we postfix `SEMan.ModifyStealth`, which it calls). Our `UpdateStealth` postfix runs `Priority.Last` so the snap is not overwritten. One Info line: "set its Sneak Visibility Skill Effect Factor to 1 to keep Sneak Ambush's early-game curve". |
| SmartSkills (`org.bepinex.plugins.smartskills`) | +20 Sneak XP per hit on an unaware enemy | Our XP stands down. |
| Goo's Combat Overhaul (gnls; GUID unverified) | Sneak damage and XP | Stand down once the GUID is known (self-test logs every plugin GUID). |
| SetUpSkills (`neocor.SetUpSkills`), ImpactfulSkills (`MidnightsFX.ImpactfulSkills`), Sneaky Viking (`Brutaliaa.SneakyViking`), SNEAKer (blacks7ar; GUID unverified) | Curve constants, noise, crouch speed, XP multiplier | Compose (our multiplier applies to their base; noise untouched). |
| EliteCreaturesReborn (GUID unverified) | "Relentless" creatures read stealth 1 and no crouch | Our factor bonuses do not fool them (their design); the smoke still does (it is not in the stealth factor). |
| GracefulTeleportation (blaxxun) | `FindEnemy` transpiler | Compose. |
| SpecialAttack (MM94; GUID unverified), Valheim Legends (Shadow Stalk; GUID unverified) | Smoke / vanish abilities that clear aggro | Overlap only; no crash. Documented. |
| BetterStealthIndicator (Jaybirds) | Re-skins the stealth eye | Compose (our cues are status icons). |
| Nameplate or HUD mods that replace `EnemyHud` (unverified) | May show bars through smoke | Documented. |
| Fog removers and visibility mods (change `RenderSettings`) | Fog look | No effect on the fog bonus (read from environment data, 2.4). |
| Smoke visual mods (Smoke Collision, NoSmoke) | Vanilla smoke puffs | Unaffected (our cloud is its own particles). |
| Item mods (ItemManager, Jotunn) | ObjectDB and ZNetScene registration | Compose (unique `MC_` prefab names). |

Detection: `BepInEx.Bootstrap.Chainloader.PluginInfos` by GUID in `OnActivated` (and at the first XP event), one Info
line per detected mod naming the overlap.

---

## 7. Implementation plan

### 7.1 Files (`src/Combat/Sneak.Ambush/`)

- `Plugin.cs`: config binding, `OnActivated` / `OnDeactivated`, static `FeatureActive`.
- `AmbushRules.cs`: snapshot of every rule setting; `Own()`, the built-in `Pending` snapshot (`IsPending`), `Write` /
  `TryRead` (layout 1, clamping), `Describe`.
- `ServerRules.cs`: copy of Forge Idol Upgrades' (settings request/answer, `Current`, Debug `TestRules`), with the
  pending state of 4.3 (a pure `Select(isClient, serverRules, own)` for the self-test) and a Debug `TestPending`
  override.
- `PlayerCheck.cs`: copy of Forge Idol Upgrades' join check, with the verdict from `NetworkGate.PeerCompatible`, the
  reason from `NetworkGate.PeerProblem` and the `NetworkGate.PeerStateChanged` re-check (4.3; texts adapted).
- `Compat.cs`: detection of the mods in 6.3 (GUID lookups, cached), Info lines, `OtherModPaysSneakXp`.
- `SneakXp.cs`: backstab eligibility, ZDO cooldown, the XP formula, RPC name, send, register, receive (input checks),
  message.
- `StealthState.cs`: local player's still timer and last refresh position, cue states (foliage touching / crown, fog,
  mist, smoke), fog density from `EnvMan.SetEnv`, `early(s)`, product, floor, `capped`, `lastWithoutStill`, snap flag;
  `Reset()`.
- `StealthCue.cs`: `StealthCue : StatusEffect` (live icon text and tooltip, cached strings) and the cue set
  (templates, icons, add/remove with the display delays).
- `SmokeContent.cs`: holder, the three clones, recipe object and rebuild, registration into ObjectDB and ZNetScene,
  `IsSmokeScreen(ItemData)`, captured vanilla smoke material.
- `SmokeCloud.cs`: `MonoBehaviour, IProjectile` on the cloud prefab (ZDO keys, setup, timeline, blind trigger,
  expiry with claim, visual start; every entry point guarded).
- `SmokeVisual.cs`: the particle system built by code.
- `SmokeRegistry.cs`: static cloud list, per-frame network time cache, `Contains`, `SegmentCrosses`, `Hides`.
- `AiMemory.cs`: owner-local blind and reveal dictionaries (keys: transform instance ids), observer cache (creature
  AI or not, boss or not), pruning, `Clear()`.
- `SelfTests.cs`: `#if DEBUG` in-world tests (7.5).
- `Patches/PlayerPatches.cs`: `Player.UpdateStealth`.
- `Patches/PlayerAwakePatches.cs` `[AlwaysOnPatch]`: `Player.Awake` (XP RPC registration).
- `Patches/SEManPatches.cs`: `SEMan.ModifyStealth`.
- `Patches/EnvManPatches.cs`: `EnvMan.SetEnv`.
- `Patches/CharacterPatches.cs`: `Character.RPC_Damage` (own class: one `__state` type, a small struct with the
  backstab time and the reveal flag).
- `Patches/BaseAIPatches.cs`: `BaseAI.CanSeeTarget`, `BaseAI.CanHearTarget`.
- `Patches/MonsterAIPatches.cs`: `MonsterAI.UpdateTarget`.
- `Patches/EnemyHudPatches.cs`: `EnemyHud.TestShow`.
- `Patches/ZNetPatches.cs`: `ZNet.OnNewConnection`, `RPC_PeerInfo`, `Update` (copy of Forge's).
- `Patches/ObjectDBPatches.cs` `[AlwaysOnPatch]`: `ObjectDB.Awake`, `ObjectDB.CopyOtherDB`.
- `Patches/ZNetScenePatches.cs` `[AlwaysOnPatch]`: `ZNetScene.Awake`.
- `Patches/HumanoidPatches.cs` `[AlwaysOnPatch]`: `Humanoid.StartAttack` (throw guard).
- `README.md`, `CHANGELOG.md`, `TESTING.md`, `icon.png` (existing scaffold), csproj `<ModIdea>Sneak revamp</ModIdea>`.

### 7.2 Harmony patches and other entry points

Every Harmony patch body **and every method game code calls on our own types** catches its own exceptions and calls
`PatchGuard.Report(site, e)`: `SmokeCloud` (`Setup`, `GetTooltipString`, `Start`, `Update`, `OnEnable`,
`OnDisable`), `StealthCue` (`GetIconText`, `GetTooltipString`), the XP RPC handler, the `PeerStateChanged` handler.
An exception there would otherwise reach vanilla loops (`Projectile.OnHit`, `Hud.UpdateStatusEffects` every frame).
No `?.` / `??` on Unity objects.

| Target | Kind | Purpose | Cost |
|---|---|---|---|
| `Player.UpdateStealth(float)` (private) | prefix | local player only (`__instance == Player.m_localPlayer`): still timer; when a refresh is due, check the displacement since the last refresh, compute skill factor and cues; on stillness end (with `StillEndsAtOnce`), force the refresh and mark the snap | FixedUpdate (50 Hz), one player: a few field reads; every 0.5 s one `CheckSphere`, one `Raycast`, `IsInMist`, a registry test |
| `Player.UpdateStealth(float)` | postfix, `Priority.Last` | snap `m_stealthFactor` up to `lastWithoutStill` and write ZDO `Stealth`; at a refresh (`m_stealthFactorUpdateTimer == 0`), sync the cue icons | same |
| `SEMan.ModifyStealth(float, ref float)` | postfix | local player's `SEMan` only: `early × product`, floor (2.5) | every 0.5 s (twice with SecondaryAttacks) |
| `EnvMan.SetEnv(EnvSetup, float, float, float, float, float)` (private) | postfix | keep the fog density from the environment data and day-part weights (2.4) | every frame on a client: four multiply-adds |
| `Character.RPC_Damage(long, HitData)` (private) | prefix + postfix | backstab detection (`__state` holds `m_backstabTime`), ZDO cooldown, send XP; reveal (2.10: the prefix reads the damage before resistances, the postfix writes the reveal) | every hit on owned characters: two reference checks and one int compare (registry empty) unless the bonus is above 1 or a cloud is loaded; then a few field reads and one `GetTotalDamage` |
| `Player.Awake()` | postfix, **always on** | register the XP RPC on the Player's `ZNetView` (skip invalid views in the menu) | once per Player |
| `BaseAI.CanSeeTarget(Transform, Vector3, float, float, bool, bool, Character)` (static; overloads exist) | postfix | smoke rule, sight | hot (per AI tick for the target, per candidate in `FindEnemy`, turrets): exit on `!__result` or registry empty (one bool, one int); with clouds a few float ops per cloud, no allocation |
| `BaseAI.CanHearTarget(Transform, float, Character)` (static; overloads exist) | postfix | smoke rule, hearing (`BlocksHearing`) | same |
| `MonsterAI.UpdateTarget(Humanoid, float, out bool, out bool)` (private) | postfix | forget: `m_timeSinceSensedTargetCreature = 31` when due | per owned monster per AI tick (20 Hz): exit on registry empty |
| `EnemyHud.TestShow(Character, bool)` (private) | postfix | health bars through smoke | per character per frame: exit on `!__result` or registry empty |
| `ZNet.OnNewConnection`, `ZNet.RPC_PeerInfo`, `ZNet.Update` | postfix | rules and join check (Forge copy; the re-check comes from the `NetworkGate.PeerStateChanged` event, not a patch) | `Update`: two bool reads when idle |
| `ObjectDB.Awake()`, `ObjectDB.CopyOtherDB(ObjectDB)` | postfix, **always on** | build clones if needed (skip an empty database), register item and recipe, set `m_enabled` | once per database |
| `ZNetScene.Awake()` | postfix, **always on** | register item, projectile, cloud | once per scene |
| `Humanoid.StartAttack(Character, bool)` | prefix, **always on** | throw guard (E5; also while the rules are pending) | per attack start: two bool reads when active |

No transpiler. `nameof` on publicized private members; argument types given for the two overloaded statics, for
`UpdateTarget` and for `SetEnv`. No patch on `BaseAI.OnDamaged` (inlining risk, 1.2).

### 7.3 State and lifetime

| State | Lifetime | Cleared |
|---|---|---|
| Clones, holder, recipe object, cue templates, captured material | game session | never (the item must stay registered; cue templates are tiny) |
| Cloud registry | scene objects | `OnDisable` of each cloud |
| Blind, reveal, observer cache (`AiMemory`) | seconds | pruned on read; cleared when the registry empties, on `OnDeactivated`, on world end |
| `StealthState` (incl. fog density) | local player | `Reset()` on a new local player instance and on `OnDeactivated` |
| Sneak-attack XP cooldown | creature ZDO (`LastXp`) | never (a long per creature that got a sneak attack) |
| Server rules | ZNet session (a client is pending until they arrive) | Forge's flow |
| Join-check queue | seconds | Forge's flow |
| `NetworkGate.PeerStateChanged` subscription | from `PlayerCheck.Start` to `PlayerCheck.Stop` | `Stop` (feature off, game quit) |

### 7.4 Live toggle

- **OnActivated:** `FeatureActive = true`; recipe `m_enabled = true` (when built and the rules are not pending);
  `ServerRules.Start()`, `PlayerCheck.Start()` (subscribes to `NetworkGate.PeerStateChanged`); `Compat` detection;
  self-tests registered. (The XP handler is registered by the always-on
  `Player.Awake` postfix, whatever the state when the player spawned.)
- **OnDeactivated** (patches still applied during the call): `FeatureActive = false`; recipe `m_enabled = false`;
  remove every cue from the local player; force a stealth refresh (`m_stealthFactorUpdateTimer = 0.51f`) so the
  vanilla target is back within a frame (the factor then ramps up at vanilla speed); `StealthState.Reset()`;
  `AiMemory.Clear()`; `ServerRules.Stop()`, `PlayerCheck.Stop()` (unsubscribes); self-tests unregistered. The XP
  handler stays registered and ignores calls while inactive (no warnings on the sender). Clouds already in the world
  keep their visual and expire; they no longer hide (the hooks are gone).
- **Late activation:** the reveal patches `Character.RPC_Damage`, a large method called through the RPC delegate that
  the JIT never inlines, so turning the feature on after creatures were already hit still works (test L04).
- **Rules arriving:** when the server's rules arrive (pending ends) or change, the recipe is rebuilt and enabled, and
  the next stealth refresh uses them.
- **Nothing vanilla to revert:** the mod never edits vanilla items, recipes or shared data (clones only).
- **`Enabled = false` + restart:** the always-on registration still runs (Smoke Screens load, can be moved, stored,
  dropped); the recipe is hidden; throws are refused with the message; no stealth changes.

### 7.5 Debug in-world self-tests (`SelfTests.cs`)

God mode, throwaway world, StaminaRate 0 (crouch never runs out). Every test cleans up in `finally` (destroys what it
spawned, restores skill level, time and environment through the fields the `tod` / `env` console commands use,
crouch off, cues removed, `ServerRules.TestRules = null`, `ServerRules.TestPending = false`). Never writes a
`ConfigEntry`: rules come from the Debug `ServerRules.TestRules` override.

| Name | Does | Asserts |
|---|---|---|
| `sneak.content` | Looks up everything registered; calls the registration twice more | `MC_SmokeScreen` in `ObjectDB` and `ZNetScene`, projectile and cloud in `ZNetScene`; exactly one `Recipe_MC_SmokeScreen`, resources resolve (`Resin`, `Coal`, `LeatherScraps`), station `piece_workbench` found; counts unchanged after re-registration; vanilla `BombSmoke` keeps `$item_smokebomb`, blunt 5, its own projectile; ours: damage 0, backstab 1, hit and start noise 0, projectile = ours, cloud ZNetView **persistent**; our projectile: `m_stayAfterHitStatic`, `m_stayAfterHitDynamic`, `m_attachToRigidBody`, `m_attachToClosestBone`, `m_onlyStopOnTerrain`, `m_respawnItemOnHit`, `m_bounceOnWater` false, `m_canHitWater`, `m_spawnOnTtl` true, `m_spawnItem` null; no "BombSmoke missing" error was logged since start. NOTE dumps: the vanilla projectile's values of those fields plus `m_bounce`, `m_ttl`, `m_stayTTL`, its components and spawn fields, explosion components, vanilla `BombSmoke` recipe, `StealthSystem` light range and shadow mask, `LayerMask.NameToLayer("viewblock")`, Sneak `m_increseStep`, every plugin GUID, the cue icons found. |
| `sneak.curve` | Rules: stillness 0. Sneak set to 0 then 100 (restored), player crouched in the open | `m_stealthFactorTarget` = vanilla formula (with the live `GetLightFactor`) × 0.85 at Sneak 0 and × 1.00 at Sneak 100 (±0.01) |
| `sneak.still` | Crouched, no input for 2.5 s; then drives the player forward with `Player.SetControls` for 0.5 s; then crouched still while the test moves the player 0.5 m every 0.5 s (a carried player); then the rules change `StillBonus` to 50 | "Holding still" cue present with text `−70%`; target ≈ 0.3 × the value without it; after moving, `m_stealthFactor` ≥ that value the same frame and the cue is gone; while carried, no cue and no bonus; after the rule change the cue text reads `−50%` without being re-added |
| `sneak.cover` | Spawns `Bush01` beside the crouched player, its stem next to the player's capsule, on what the player stands on (also dumps its colliders and layers, raycasts from 12 m to the crouched centre); spawns `Beech1` and puts the player under its crown; forces Misty at night, then Clear at noon; then SwampRain, Darklands_dark, Mistlands_clear and Twilight_Clear, each switched at once | "In foliage" cue at the bush (factor unchanged with default rules, × 0.7 with test rule `FoliageBonus` 30); ray blocked; under the tree: the crown ray hits and the cue shows (logs the hit; a miss is a NOTE, the crown shape being unverified); our fog density equals `RenderSettings.fogDensity` (±0.001) in every forced environment, both logged; fog cue with the expected percentage; no fog cue in Clear; `IsInMist` false in the Meadows |
| `sneak.xp` | Spawns an unaware level-1 Greydwarf; hits it through `Character.Damage` with a player hit (backstab bonus 3); un-alerts it (`SetAlerted(false)`, target cleared) and hits again; sets its `m_backstabTime` back 301 s (as a reload or new owner would) and hits again; sets the ZDO `LastXp` back 301 s too and hits again; calls the handler directly with −5, NaN and infinity | first hit: Sneak progress rises by 0.5 × 7 × gain rate × the player's raise-skill modifier (Rested); second: no backstab, no XP (vanilla cooldown); third: backstab but no XP (ZDO cooldown); fourth: XP; bad payloads: no change; a ranged hit on a fresh Greydwarf pays half; a tamed Boar and a `piece_TrainingDummy` pay nothing |
| `sneak.smoke-throw` | Gives a Smoke Screen, equips it, `StartAttack` toward the ground 6 m away, with an unaware Greydwarf 12 m away; then launches our projectile at a Greydwarf 3 m away, down onto water (a spot where `Floating.GetLiquidLevel` is above the ground within 200 m; a NOTE when none), and upward with its instance `m_ttl` set to 0.5 s | Stack decreases; exactly **one** `MC_SmokeScreen_cloud` per throw for the terrain hit, the creature hit, the water hit (base at the water surface) and the TTL expiry, and no Smoke Screen item dropped; ZDO keys set, persistent; the Greydwarfs stay unalerted; test rules with a 3 s cloud: destroyed after duration + fade; screenshot of the cloud |
| `sneak.smoke-sight` | Test cloud placed by code; Greydwarf at set positions | `CanSeeTarget` / `CanHearTarget` (player noise forced to 30): player inside + creature outside → false; creature inside + player outside → false; cloud between → false (true with `BlocksLineOfSight` off); both inside at 2 m → true, at 3.5 m → false; after a damaging player hit → true; on fresh creatures, after a player hit of fire 5 only and after one of blunt 0.05 (both below vanilla's `OnDamaged` threshold, D31) → true; after a player hit with no damage at all (like a Smoke Screen) → still false; `m_boss = true` on the test creature (restored) → true; a plain transform without `BaseAI` as observer (a turret) → unaffected |
| `sneak.smoke-blind` | Alerted Greydwarf targeting the player at 3 m, hit once before the burst; cloud at the player; then a second Greydwarf targeting the player with the cloud placed 15 m from the player | first: within `ForgetSeconds + 0.5 s` no target, not alerted; the earlier hit's reveal is gone; during the blind, at 2 m inside, `CanSeeTarget` false; a new hit → true. Second: not blinded, keeps its target |
| `sneak.healthbars` | Greydwarf 8 m away, its plate made visible (`m_hoverTimer` = 0); cloud on it | `TestShow(c, true)` true without cloud, false with it; the plate's entry leaves `EnemyHud.m_huds` within a few frames and does not come back while the cloud is there; cloud on the player instead → true (`OnlyInside`), false with `ThroughSmoke`; boss flag → true |
| `sneak.network` | Pure checks | rules round trip, clamping, unknown layout refused, a server never takes rules from a peer; `ServerRules.Select`: single player, host and server → own rules; client with server rules → the server's; client without (none yet, or only an unreadable layout) → `Pending`; `PlayerCheck.Decide`: compatible → Compatible; not compatible (no mod, turned off or other network version: all reach `Decide` as `compatible = false`) → Refuse, or Allowed with `AllowPlayersWithoutMod`; not a server, not connected, not ready or already being kicked → Skip |
| `sneak.guard` | Debug override "feature inactive" | `StartAttack` with a Smoke Screen returns false and the stack is unchanged; with the override off it throws |
| `sneak.pending` | Debug override `ServerRules.TestPending = true` (cleared in `finally`); player crouched and still in the open; a test cloud on a Greydwarf; a Smoke Screen equipped | stealth target = the vanilla formula (no ×0.85, no still bonus) and no cue icon; `Hides` false with the player in the cloud; `EnemyHud.TestShow` unaffected; the recipe disabled; `StartAttack` refused with the waiting message, stack unchanged; after clearing the override: recipe enabled again and the still bonus back at the next refresh |

### 7.6 Shared framework used (already in `src/Shared`, done in this run)

Both pieces below are **implemented and tested** in `src/Shared/Framework` in this run (`./tools/Test-Framework.ps1`
and `src/Shared/TESTING.md`) and documented in `docs/modding/framework.md`. This mod only uses them; it adds no file
to `src/Shared` and needs no further framework change.

- **`[AlwaysOnPatch]`** (`AlwaysOnPatchAttribute.cs`, applied by `ModPlugin.Awake`; section "Content that stays
  registered" of `framework.md`): patch classes marked with it are applied once at start under `<GUID>.alwayson`, the
  default `ApplyPatches` skips them (so the live toggle never removes them), and only game quit removes them. If they
  cannot be applied, the mod gets the error status "Error: could not register this mod's items..." and stays off for
  the session. Covered by `Test-Framework.ps1` (probe `tests/Probes/Core.Probe.A`, `AlwaysOnProbe.cs`) and framework
  test F11 (Sneak Ambush is the first mod with such content). This mod marks four classes (7.1: `PlayerAwakePatches`,
  `ObjectDBPatches`, `ZNetScenePatches`, `HumanoidPatches`). Their code must not depend on the feature being active
  or on config entries being bound (defaults when they are null), and reads the feature state itself
  (`Plugin.FeatureActive`, `ServerRules` pending).
- **`NetworkGate` join-check support:** the hello carries whether the client's copy is ready (on, dependencies active,
  no start error) and `HelloState` updates it live; server side, `NetworkGate.PeerCompatible(peer)`,
  `NetworkGate.PeerProblem(peer)` and the event `NetworkGate.PeerStateChanged(ZNetPeer)`. `PlayerCheck` (4.3) uses
  exactly these three; no mod-specific RPC, timer or accessor. Framework tests N07 and N08 (`src/Shared/TESTING.md`)
  check the hello and `HelloState` lines; this mod's M12, M15 and M16 check how its join check reacts.
- No change to `FeatureRegistry` or `PatchGuard`. `ItemKinds` now keeps tower shields in the Shield kind (Tower Shield
  Wall's run); the Smoke Screen (skill None, not the shield pose) stays a Weapon, and this mod does not call
  `ItemKinds`.

### 7.7 Implementation notes

Recorded while building the mod; each differs from, or makes precise, a point above.

- **`Enabled` description:** the framework binds `Enabled` with its own description, so the sentence "Smoke Screens
  stay in your inventory while it is off" (section 5) is in the README instead of the config file.
- **Percent settings are whole numbers** (`EarlyGameBonus`, `StillBonus`, `FoliageBonus`, `FogBonus`), like the
  percent settings of the other MC mods.
- **`FogDensityFullBonus` not above `FogDensityNoBonus`:** any fog thicker than `FogDensityNoBonus` gives the full
  bonus (said in the setting's description), so the fog formula never divides by zero or a negative span.
- **Other-mod detection is late:** `Compat` is reset in `OnActivated` and looks at the loaded plugins at its first
  use (XP event, stealth refresh, self-test), not during `OnActivated`: the first activation runs inside the plugin's
  own `Awake`, when BepInEx has not yet loaded the plugins that come after it (same finding as Breeding Star
  Inheritance). The Info lines of 6.3 appear at that first use, once per activation.
- **Rules only go to matching copies:** the server pushes its rules only to players with the same network version, and
  answers a rules request only when it asks for layout 1. Other copies could not read the package and are inactive
  anyway (framework: the versions cannot talk to each other).
- **Rules change notice:** `ServerRules.Changed` (an event) and `ServerRules.Revision` (a counter) tell the recipe
  rebuild and the cue text caches that the rules in force may have changed: server rules arrived, own settings
  changed, server rules forgotten, or a self-test override set.
- **`AmbushRules.Pending`** also holds neutral numbers (no bonuses, no XP, no recipe, smoke blocks nothing, health bars
  `Off`), so code that forgot the pending test would still act like the normal game; the code checks `IsPending`
  anyway.
- **Unreadable values from the server** (not a number, out of range, an unknown `HealthBars` value, recipe texts over
  1000 characters) are brought back into range or to the default, with one warning.
- **Debug override `Plugin.TestInactive`** makes `Plugin.FeatureActive` false for the `sneak.guard` self-test without
  touching the toggle.
- **Snap when stillness ends (2.3):** the factor jumps up to the lower of "the value without the still bonus" (kept
  from the last refresh) and the new target of the forced refresh, and never down. Walking straight from stillness
  into a bush or thicker fog therefore does not overshoot and ramp back down.
- **Bad XP payloads:** the XP handler refuses a negative victim health like NaN and infinity (no XP), instead of
  clamping it to 0 and paying the flat part; a real game never sends one, and `sneak.xp` expects no change for −5.
- **Attack-level spawn cleared:** the Smoke Screen's primary attack also gets `m_spawnOnHit` null and
  `m_spawnOnHitChance` 0, because vanilla copies an attack-level spawn onto the projectile
  (`Attack.ProjectileAttackTriggered`) and it would replace our cloud. A secondary attack that uses the vanilla
  projectile (none in 1.0.16) would get ours too.
- **Missing `BombSmoke` (2.13):** one error in the log only. The Status line cannot say it without turning the feature
  off: the framework has no "active with a note" state, and `LocalBlocker` would make the feature inactive and get the
  player refused by servers that require the mod, while everything else still works.
- **Cloud visual lives apart from the cloud object** (the cloud never moves): when the cloud object is destroyed
  (expiry, or the player leaves the area), its particles stop emitting and fade out on their own before the visual is
  removed, instead of vanishing mid-puff. Material search order as in 2.8 (vanilla explosion first, then the world smoke
  renderer); the look is unverified until the `sneak.smoke-throw` screenshot.
- **Clouds made without a Smoke Screen** (console `spawn MC_SmokeScreen_cloud`, self-tests through
  `SmokeCloud.Spawn`) use the rules in force (the defaults unless changed); while the rules are pending, the defaults.
- **Water base:** the liquid level is sampled at the impact point and 1 m below it (the projectile bursts a little
  under or over the surface); the water surface wins over the sea floor.
- **Blind timing:** a cloud tries its burst blind every frame of its blind window until it succeeds once, so a cloud
  that arrives (or a feature that turns on, or rules that arrive) inside the window still blinds.
- **Hearing with `BlocksHearing` off:** the smoke geometry no longer blocks hearing, but a blinded creature still
  cannot hear players (2.11: "cannot see or hear any player"). The In smoke tooltip then says "cannot see you" instead
  of "cannot see or hear you".
- **Blind needs an alerted chaser:** the burst blinds a creature only when it is alerted and its target is a player
  near the burst (2.11: unalerted creatures are not blinded). A creature that has only noticed a player and walks over
  to look is left alone.
- **Holding still tooltip with `StillEndsAtOnce` off:** "Moving ends it; your stealth bar then rises at its normal
  speed." instead of "Moving ends it at once."
- **"Capped" while standing:** the floor flag behind the "max" icon text is cleared at each refresh while standing
  (vanilla only calls `SEMan.ModifyStealth` crouched), so an icon that lingers after standing up never shows a stale
  "max" or floor tooltip line. A stillness end that forces a refresh also restarts the "moved less than 0.25 m since
  the last refresh" measure there.
- **Network time is read directly** (`ZNet.GetTimeSeconds()`, a field read) instead of a per-frame cache: many callers
  run in `FixedUpdate`, where a per-frame cache would be stale.
- **Fog density survives `StealthState` resets:** it is the world's weather, refreshed every frame by the `EnvMan.SetEnv`
  postfix.
- **Cue icon numbers use an ASCII hyphen** ("-70%") instead of the minus sign, which the game's fonts may not have.
- **Recipe station:** an empty `RecipeStation` means no station (craft anywhere); an unknown station hides the recipe
  with one warning; a material without a valid amount is skipped with one warning. Warnings appear only in a world
  (the main menu has no station prefabs); the `ZNetScene.Awake` registration rebuilds the recipe for that. A rebuild
  while the feature is off only hides the recipe, reading and saying nothing: at game quit `ModPlugin.OnDestroy` runs
  `OnDeactivated` after Unity has destroyed the item prefabs, and a full rebuild there warned that `Resin`, `Coal`,
  `LeatherScraps` and `piece_workbench` did not exist (first in-world run). `OnActivated` rebuilds it in full.
- **`NpcTalk`** checks its sight through its creature's `MonsterAI.CanSeeTarget`, so an NPC with creature AI inside
  smoke does not greet a hidden player (turrets are unaffected, as designed). Harmless; noted for accuracy of D28.
- **Foliage queries include trigger colliders** (`QueryTriggerInteraction.UseGlobal` instead of `Ignore`, 2.4): vanilla's
  sight ray (`BaseAI.CanSeeTarget`) uses the global default, so a trigger collider on the `viewblock` layer blocks
  creature sight; the cue now finds exactly what blocks sight. `sneak.cover` logs whether Bush01's and Beech1's
  colliders are triggers.
- **Review fixes (2026-09-30):**
  - **Ground under a mid-air burst:** the base ray (2.8) reaches 1000 m down instead of 12 m. The projectile spawns on
    its time running out (`m_spawnOnTtl`, 4 s by default, 1.6), which can happen high above the ground (a throw off a
    cliff); the short ray then found nothing and the cloud floated where the time ran out, hiding nobody.
  - **Build-up inside the duration:** `ActivationDelay` is cut to `CloudDuration` − 1 s when longer
    (`SmokeCloud.BuildUp`, used by the hiding test and the blind trigger); with both at 3 s the cloud never hid. The
    settings, the README and 2.8 say the duration counts from the impact.
  - **Blind capped at the cloud's end + fade** (`SmokeCloud.BlindEnd`, 2.11): the blind lives in `AiMemory`, which the
    registry clears when its last cloud goes, and every sense hook exits when no cloud is loaded, so a `BlindSeconds`
    longer than `CloudDuration` + 3 s silently ended early. Now the number is honest and the setting says so.
  - **Visual follows the hiding** (2.8): emission stops 2.4 s before the end (it stopped at the end, and the last puffs
    stayed fully thick for up to 3.6 s after the hiding ended and lingered up to 6 s); a puff starts nearly opaque, so
    the burst is thick by the end of `ActivationDelay` (it faded in over about 1 s).
  - **XP cooldown written with the send** (2.1): `LastXp` is written only once the attacker's view is found valid,
    right before the RPC, instead of before that check. A sent payout still counts whatever the attacker's game does
    with it (documented in 2.1, 4.4, the setting and the README).
  - **Tames keep their plates** (2.12): the `EnemyHud.TestShow` postfix also skips tamed creatures (D15: the smoke
    hides players only).
  - **Hunting creatures never give up** (1.2, 2.11, D14): documented, no code change; the README and design had said
    raids re-target after the 5 s pause.
  - **Texts** name the mod through `ModInfo.Name` (throw-guard messages, log lines) instead of the literal name; the
    server's Allowed warning, the `AllowPlayersWithoutMod` description and the README say that creatures controlled by
    a game without the mod (or with it turned off) give nobody sneak-attack XP; the README says the stealth bar climbs
    back over a few seconds after turning the mod off, and that only hits landed while a smoke cloud is nearby reveal.
- **Self-tests** (`SelfTests.cs`, 7.5): the twelve tests of 7.5, registered in `OnActivated` and unregistered (with
  every Debug override cleared) at the start of `OnDeactivated`. A `Rig` puts back the player (crouch, place, aim,
  controls, Sneak level and progress, equipped items, given Smoke Screens), time and weather, and destroys every object
  and cloud a test made. Debug-only helpers added for them: `StealthCues.TestShowCues` and `SneakXp.TestShowMessage`
  (icons and message on even when the player's personal setting is off), `HumanoidPatches.TestResetMessageTimer` (the
  refused-throw message is not held back by an earlier one within 3 s), `StealthCues.Template` made internal (icon
  dump). Details that differ from, or make precise, the table of 7.5:
  - `sneak.content` reads the recipe with the design's default rules (a test rule), not this game's config, and also
    checks that Eikthyr is a boss, the Greydwarf's base health is 40, the `Wet` status effect and the `viewblock`
    layer exist, the player animator has the `throw_bomb` trigger and the `crouching` bool, and the Smoke Screen's
    throw animation is that trigger. The Karve is a NOTE only.
  - `sneak.still` "carries" the player 0.4 m every 0.25 s (not 0.5 m every 0.5 s), so every 0.5 s refresh window holds
    a move whatever the timing; it notes how many of those steps were otherwise still (crouched, grounded, not
    walking). It drives the player with `Player.SetControls` while the `PlayerController` is off, and notes the
    distance walked.
  - `sneak.cover`: the bush is placed beside the player (stem radius 0.2 m from the collider dump, plus the player's
    radius and 0.1 m), at the height of what is under that spot (`ZoneSystem.GetSolidHeight` from 2 m above the feet),
    the way a player crouches against a bush. The first run spawned it on the player's feet: its stem lifted the player
    (centre 1.74 m above the root, above the bush's `viewblock` sphere), so nothing was found or blocked. "Ray
    blocked" means blocked by one of Bush01's own colliders, cast from the bush's side. Each forced weather is switched
    at once with `EnvMan.ForceInstantEnvironmentSwitch` (what the game does after a spawn or a teleport); the first run
    waited for the normal blend (`EnvMan.m_transitionDuration`, an asset value) and none of the six weathers was in
    place after 6 s. The fog NOTE logs that duration; a weather still not in place names what `EnvMan` holds. The
    real weather also comes back at once in the clean-up. Fog icons follow the icon's own rule: expected at 2% or
    more, absent at 0%, either at 1%.
  - `sneak.xp`, `sneak.guard` and `sneak.pending` check a message in the HUD's top-left queue
    (`MessageHud.m_msgQeue`), read in the same frame as the hit or the refused throw: `Player.Message` passes
    `log: false` (the game's default), so top-left messages never reach the message log (`MessageHud.GetLog`), which
    the first run read. Exactly one message is expected each time.
  - `sneak.xp` hits from behind the Greydwarf (a creature that Creature Morale makes afraid of the attacker loses the
    backstab when it sees him), sets Sneak to 20 so no payout is cut by a level-up, and calls the handler through the real routed RPC
    on the local player (also minus infinity, plus a melee and a ranged good payload).
  - `sneak.smoke-sight` and `sneak.healthbars` use `ActivationDelay` 0 (test rule) so a placed cloud hides at once; the
    fire-only and 0.05 blunt hits are also checked not to alert (they are below vanilla's `OnDamaged`). The boss case of
    `sneak.healthbars` also sets `m_dontHideBossHud` (vanilla shows a boss bar only when it is alerted or has it).
  - `sneak.smoke-blind` uses `BlindSeconds` 12 (test rule) so the blind still holds when the "2 m inside" check runs
    after the give-up, and makes that check (and the one after the new hit) as an alerted look from the creature's own
    transform, next to a plain eye at the same spot that must see the player: after its give-up the creature idles and
    may turn away.
  - `sneak.smoke-throw` uses the shortest cloud (3 s) and checks it is inert after its end and destroyed no earlier
    than end + fade; the creature-hit case checks the hit reached the Greydwarf (its "attackers" flag, D29). A fifth
    case lets the projectile's time run out 30 m above the ground and checks the cloud sits on the terrain, the solid
    ground or the water below.
  - `sneak.network` also checks the pure timeline helpers: build-up 0.5 s by default, a 3 s build-up cut to 2 s in a
    3 s cloud; blind end 6 s by default, a 30 s blind cut to 6 s (cloud + fade) in a 3 s cloud.
  - `sneak.healthbars` also checks that a tamed creature in the cloud keeps its plate, and notes the game's plate
    distances and duration (`m_maxShowDistance` is 30 m in 1.0.16).

---

## 8. Test plan (`TESTING.md`)

Spawn names checked in the dump: `Greydwarf`, `Troll`, `Skeleton`, `Boar`, `Neck`, `Deer`, `Draugr_sleeping`,
`Eikthyr` (boss), `piece_TrainingDummy`, `BombSmoke`. From the asset list (checked by `sneak.content` / `sneak.cover`):
`Resin`, `Coal`, `LeatherScraps`, `Bush01`, `RaspberryBush`, `Beech1`, `piece_workbench`. Unverified: `Karve` (ship
prefab, only used in T08). Ours: `MC_SmokeScreen`.

### Setup

- **S01** Build under test: version + build id from the `[MC:ready]` log line; smoke-test result.
- **S02** Commands (`devcommands` first): `god`; `spawn Greydwarf 1 1` (one creature, level 1: stars change the XP),
  `spawn Troll 1 1`, `spawn Boar 1 1`, `spawn Draugr_sleeping`, `spawn piece_TrainingDummy`; `tame` (tames tameable
  creatures nearby, such as a Boar); `spawn MC_SmokeScreen 10`; `spawn Resin 10`, `spawn Coal 5`,
  `spawn LeatherScraps 5`; `raiseskill Sneak 100` / `resetskill Sneak`; `env Misty` / `env Clear` / `resetenv`;
  `tod 0` (midnight) / `tod 0.5` (noon) / `tod -1` (normal time). Set the BepInEx log level to Debug to see the XP and
  rules lines.
- **S03** Default settings unless a test says otherwise (`HealthBars = OnlyInside`). A creature's health bar shows only
  within 30 m and for 60 s after your crosshair was on it (vanilla, 1.5): in every health bar test, aim at the creature
  first so its bar shows.

### Single player

- **T01 (G1, E6)** Sneak 0, crouch behind an unaware level-1 Greydwarf and hit it with a knife: backstab effect,
  "Sneak attack!" at the top left, and Sneak rises from 0 to 1 (vanilla level-up message); Debug line "sneak attack …
  7 Sneak XP".
- **T02 (G1)** Hit that Greydwarf again so it chases you, throw a Smoke Screen at your feet, wait until it gives up
  (alert icon off, it walks away), then hit it again within 5 minutes: no backstab, no XP, no message.
- **T03 (G1, E4)** At Sneak 5 or more, bow sneak attack on a fresh unaware Greydwarf: "Sneak attack! +N%" with about
  half the percentage a knife gives (Debug line 3.5 XP against 7).
- **T04 (G1)** Sneak attacks on a level-1 Troll and a level-1 Boar: 33 and 4 XP (Debug line).
- **T05 (G1)** A training dummy and a tamed Boar (`tame`): backstab possible, no XP, no message.
- **T06 (G2)** Sneak 0 in open daylight: crawl slowly in circles (so the holding-still bonus never starts) and read the
  stealth bar after about 3 s: near 85% (vanilla 100%). At `raiseskill Sneak 100`, the same: near 60% (vanilla).
- **T07 (G3)** Sneak 0, crouch and hold still in open daylight: after about 1 s a "Holding still −70%" icon; the bar
  sinks to about 25% over 2-3 s. A wandering Greydwarf passing about 8-10 m in front of you does not notice you.
  Control: crawl slowly while one passes at the same distance: it notices you and comes over.
- **T08 (G3)** Take one step: the icon disappears and the bar jumps back up at once. Standing up, jumping or taking a
  hit also end it. On a Karve with the sail set and the helm released, crouch on deck while it moves: no Holding still
  icon.
- **T09 (G4)** Crouch against a raspberry bush or a Bush01: "In foliage" icon without a number; its tooltip in the
  inventory's Active effects explains the sight block and the shade; in daylight the bar drops a little (shade).
  Crouch under a big tree (Beech, Oak): the same icon. Step into the open: the icon goes about 1 s later.
- **T10 (G4)** Crouched behind a bush, a Greydwarf on the other side facing you does not see you until it walks round.
- **T11 (G4)** `env Misty` at `tod 0`: "Fog −30%" icon; at `tod 0.5` "Fog −4%"; `env Clear`: no fog icon; `resetenv`
  in the Swamp: a small fog icon (4-9%). In a crypt: none.
- **T12 (G4, E3)** In the Mistlands mist (without a Wisplight): "In the mist" icon while crouched.
- **T13 (G5)** Workbench: the Smoke Screen recipe shows once Resin, Coal and Leather scraps were seen; crafting uses
  2 Resin + 1 Coal + 1 Leather scraps and gives 2.
- **T14 (G5)** Throw one: the same throw as the vanilla bomb; one grey cloud about 8 m wide appears where it lands,
  also when it hits a creature or lands in water (the cloud sits on the surface); it hides until about 15 s after
  the impact and is gone about 4 s later. No
  damage numbers, no "Smoked" icon, no choking, no fire put out, no Smoke Screen dropped back; a nearby unaware
  Greydwarf stays unaware; the one it hit stays unaware.
- **T15 (G6, E3)** Stand in the cloud, an unaware Greydwarf 6-10 m away outside it facing you: it does not notice you,
  even when you walk or run inside (no hearing); "In smoke" icon while inside.
- **T16 (G6, G9)** Aim at a Greydwarf within 10 m so its bar shows, throw the Smoke Screen onto it and stay outside
  the cloud: its bar disappears and it does not notice you. After the smoke ends its bar shows again once you aim at
  it.
- **T17 (G6, D5)** Both inside: it notices you only when about 2.5 m away.
- **T18 (G6, E1)** Neither inside, cloud between you and an unaware Greydwarf: it does not see you through it.
- **T19 (G8)** Get chased by two or three Greydwarfs and aim at them so their bars show, throw at your feet and move a
  few metres inside the cloud: they stop attacking; within about 3-4 s their alert icons go off and they walk back
  toward where they came from; after about 6 s, once you leave the smoke, they can find you again the normal way.
- **T20 (G7)** From inside the smoke, hit one of two nearby Greydwarfs: that one fights you, the other does not
  notice you. Shoot a Deer from inside the smoke: it flees from you. The first hit on an unaware creature is a
  backstab (x3), so the targets must survive it: Bows 0 (`resetskill Bows`: a full-draw Bow shot with wood arrows does
  11-24, 33-73 as a backstab), two-star Greydwarfs (120 health) and a level-9 Deer (90 health).
- **T21 (G10)** `spawn Eikthyr` near the cloud, you inside: it sees you and attacks; its health bar stays; it is not
  blinded.
- **T22 (G9, E2)** You inside the cloud, a Greydwarf outside within 10 m that you aimed at: its bar shows (default
  `OnlyInside`); with `HealthBars = ThroughSmoke` it is hidden.
- **T23 (G12)** Die while holding still in fog: after respawn, the icons come back when you crouch again; no errors.
- **T24 (E5)** With the feature off, try to throw a Smoke Screen: refused with the message, the stack is unchanged.
- **T25 (2.9, D30)** `spawn Draugr_sleeping`, throw a Smoke Screen about 7 m from it and walk into the cloud: the
  Draugr wakes up when you come within about 10 m (vanilla wake-up, documented), but it does not come for you while
  you stay in the smoke more than 2.5 m from it.
- **T26 (G9, D15)** A tamed Boar in a cloud, you outside: its bar stays (T16 shows a wild creature's bar going).

### Multiplayer

- **M01 (G12)** Dedicated server and two players with the mod: change `StillBonus` on the server; both players log
  "Using the server's rules" and see the new percentage on the Holding still icon.
- **M02 (G12)** A player without the mod is refused about a second after joining ("Incompatible version"; the server
  log says "Refused <player>: does not have the mod"); with `AllowPlayersWithoutMod = true` on the server they get in
  and the server log warns.
- **M03 (G1)** Player A sneak-attacks a creature that player B's game controls (B stands closer): A gets the XP and
  the message (Debug line on both games).
- **M04 (G6, G9)** A throws a Smoke Screen near creatures B's game controls: they do not see A or B inside; both
  players see the same cloud for the same time; a player outside who aimed at a creature inside sees its bar go, on
  both screens.
- **M05 (G3)** A holds still crouched near creatures B's game controls: they notice A later (same as T07).
- **M06 (hand-off)** With `AllowPlayersWithoutMod = true`: put a Smoke Screen and a stone in a throwaway chest.
  (a) A player without the mod opens and closes it: the Smoke Screen is still there for players with the mod.
  (b) They open it, take the stone, close it: the Smoke Screen is gone for everyone (documented loss). Creatures that
  player controls ignore smoke; their log shows "Missing prefab hash" warnings while near a cloud (documented).
- **M07 (hand-off)** B stands in A's cloud; A disconnects: the cloud stays for B and still hides B until its normal
  end, then disappears.
- **M08 (G11)** Join a server without the mod: Status "the server does not have this mod"; the recipe is hidden,
  throws are refused, Smoke Screens stay in the inventory.
- **M09 (G7, G8)** B's game controls two Greydwarfs chasing A (B stands closer to them); A throws at his feet: both
  give up within about 4 s (alert icons off on both screens); A hits one from the smoke: it fights A.
- **M10 (hand-off)** A throws, B stands inside; A teleports away (or dies and respawns at a far bed): the cloud stays
  for B and still hides B until its end.
- **M11 (G8, D13)** B melee-fights a Greydwarf about 10 m from the edge of the spot where A throws a Smoke Screen: it
  keeps fighting B.
- **M12 (G12, D33)** With `AllowPlayersWithoutMod = true` on the server, a player with the mod but `Enabled = false`
  joins and stays (server log: a warning naming "has the mod turned off"): creatures their game controls ignore the
  smoke (documented); they sneak-attack a creature controlled by a player with the feature on: no XP for them and no
  warning in either log.
- **M13 (G6, G8, G11)** Dedicated server with the mod, one player (a dedicated server never controls creatures or
  creates world objects, 1.7, so this checks what it does do). (a) T15 and T19 there, with wild or spawned Greydwarfs
  (the player's game controls them): same results as in single player. (b) With Debug logging on in the server's
  `BepInEx.cfg`, its log has Debug "Made the Smoke Screen from BombSmoke: …" at start-up (always-on registration).
  (c) A Smoke Screen in a chest, one dropped on the ground, one thrown; the server stopped while the cloud is up
  (Ctrl+C in its window saves the world) and started again: no "ZDOs with unknown prefabs" warning at start-up (a
  server without the mod warns about these objects; other mods' objects can cause their own); after rejoining, the
  dropped and the chest's Smoke Screens are still there; no error from Sneak Ambush in either log.
- **M14 (G11)** Dedicated server with the mod, `Enabled = false`, restarted, with a Smoke Screen dropped on the ground
  before the restart: it is still there; the server's start-up log has no "ZDOs with unknown prefabs" warning (the
  prefabs stay registered while the feature is off); clients see the recipe hidden and throws refused. Then
  `Enabled = true` on the server without a restart: throws work and the cloud hides.
- **M15 (G12, D33)** `AllowPlayersWithoutMod = false` (default) on the server. (a) A player with the mod and
  `Enabled = false` joins: about a second after joining their game shows "Incompatible version" and goes back to the
  menu; the server log says "Refused <player>: has the mod turned off". (b) A player with the mod on joins and plays,
  then unticks Sneak Ambush in the MC Mods panel: their log says "Told the server that Sneak Ambush is now off on this
  game."; about a second later they are refused the same way. (c) Untick and tick it again within a second: they
  stay. (d) Another player with the mod on is never affected.
- **M16 (G12, D33)** A second player's copy built with another network version (`dotnet build
  src/Combat/Sneak.Ambush/MC.Combat.Sneak.Ambush.csproj -p:ModNetworkVersion=2 -p:DeployToGame=false`, that DLL
  copied over the second player's copy; put the normal build back afterwards) joins a server with network version 1:
  refused about a second after joining; the server log says "has another version of the mod (network version 2, the
  server has 1)". With `AllowPlayersWithoutMod = true` they stay: their MC Mods panel says the versions cannot talk
  to each other, Smoke Screens stay in their inventory and throws are refused.

### Cross-mod

- **X01** Creature Morale, at its boss rank 6 (plain and two-star Greydwarfs afraid): an afraid Greydwarf that
  watches you from beyond 12 m (it does not run yet): an arrow gives no backstab and no Sneak XP. A hit from behind
  while you are crouched out of its sight, or an arrow from inside a Smoke Screen cloud, is a backstab and pays (7 XP
  melee, 3.5 ranged). Crawling next to afraid creatures raises Sneak only slowly. Inside the smoke you can walk up to an
  afraid Greydwarf without it running (it cannot sense you). From inside the cloud, hit an afraid Greydwarf with a hit
  it survives (a two-star one shot at Bows 0, as in T20): it targets you and fights you through the smoke (Morale
  provokes it, the same hit reveals you); the other afraid Greydwarfs do not notice you. Standing in the smoke next to
  an afraid creature that has not noticed you never corners it. Creatures that chase you (rank 0) forget you after a
  smoke burst; a routing pack is unaffected; the bar of an afraid creature hidden by the smoke comes back, with the
  game's alert icon while it runs; no errors. (Same test as Morale's X01.)
- **X02** Tower Shield Wall: raising the tower shield ends the crouch and the "Holding still" icon; a shield bash on
  an unaware creature gives no backstab and no Sneak XP (= Tower's C08). From inside a Smoke Screen cloud, a bashed
  Greydwarf sees you and fights you. **NG+ variant:** in a world at world level 1 (see Tower Shield Wall's T23 for
  the setup), bash an unaware Greydwarf, then hit it with a knife: the bash alerts it, so the knife hit is no sneak
  attack and pays no Sneak XP (Tower Shield Wall alerts a creature its bash hits without damage, 6.1).
- **X03** Dual Wielding: a Smoke Screen cannot be put in a dual pair; a pair's sneak attack pays XP once.
- **X04** Weapon Moveset: jumping or rolling with a Smoke Screen equipped still gives the plain throw. With Creature
  Morale off, or with a character that has no boss kills, or from behind the Greyling: a sneak roll attack on an
  unaware Greyling is a backstab and pays sneak-attack XP once. With Morale on at its rank 3 and an afraid Greyling
  that sees you (from beyond 12 m, before it runs): no backstab and no XP (Morale's E3). Same expectations as
  Moveset's X04 (a)-(c).
- **X05** Harpoon Hooks Tames: hooking a tame gives no sneak XP and no errors.
- **X06** Compendium Encyclopedia: a creature seen only through smoke is not "met" until its bar shows.
- **X07** Forge Idol Upgrades: both load; the Forge works; Smoke Screen craftable.
- **X08** Crossbow Stays Loaded: a crossbow sneak attack pays half XP; the crossbow stays loaded as usual.
- **X09** Creature Kill and Tame Counts: A hits a Greydwarf with a Smoke Screen and B kills it: both get the kill
  (vanilla "took part" credit, documented).

### Live toggle, Enabled = false, clean log

- **L01** In a world, crouched and still in fog next to an active cloud, untick Sneak Ambush in MC Mods: icons go,
  the bar returns to vanilla, the cloud no longer hides you (its smoke still shows until it ends), the recipe is
  hidden, throws are refused. Tick it again: all back.
- **L02** `Enabled = false` and restart: Smoke Screens still in the inventory and in chests, the recipe hidden, throws
  refused, Status "Off (disabled in settings)", no error.
- **L03** After the tests above, the log has no error or warning from Sneak Ambush (other than the documented ones:
  refused and allowed players in M02, M12, M15 and M16, and M06-M08).
- **L04** Start with `Enabled = false`, hit a few Greydwarfs, then turn the feature on and run T20 (with its setup, so
  the shot does not kill): the hit creature sees you through the smoke.

---

## 9. Open questions and unverified

### Open questions for the user

1. **Recipe:** Workbench level 1, 2 Resin + 1 Coal + 1 Leather scraps → 2 Smoke Screens? (D3)
2. **Health bars:** the literal one-way rule by default, with both ways as a setting (E2)? (D17)
3. **Numbers:** stillness −70% (raised from 50% so creatures passing close really miss you), fog up to −30%,
   early-game −15% at Sneak 0, floor 0.1 (all settings). (D9)
4. **Blind only creatures chasing someone near the burst** (literal), or everything near it? (D13)
5. **Smoke between** you and a creature blocks its sight (E1): keep?
6. **Smoke, then backstab:** a creature that lost you is unalerted, so your next hit can be a backstab (vanilla
   cooldown per creature instance; XP at most once per 300 s per creature). Keep, or block backstabs for a few
   seconds after a creature gave up? (D13)
7. **Bushes:** show the vanilla sight block and shade only (default), or also add a number (`FoliageBonus`)? (D11)
8. **Early game:** visibility only (current), or also a small sneak-stamina cut that fades out at Sneak 100 (for
   example −20% drain at Sneak 0)? (D27)
9. **Sneak-attack XP:** 3 + 10 per 100 health (Greydwarf 7, Troll 33, half for ranged), once per 300 s per
   creature? (D19)
10. **Turning the mod off while connected** to a server that requires it: the player is refused about a second later
    with the game's own "Incompatible version" screen, without a warning first (D33; same for every Combat mod of this
    run). Enough, or should the player see a short message such as "This server requires Sneak Ambush: turning it
    off disconnects you" when they untick it?

### Unverified names and values (and how to verify)

| Item | Source | Check |
|---|---|---|
| Vanilla smoke projectile and explosion contents (`smokebomb_projectile`, `smokebomb_explosion`: components, stay / attach / bounce / water / TTL fields, hit effects, particle renderer) | asset names only | `sneak.content` NOTE dump; our clone sets every field that matters (2.7); `sneak.smoke-throw` counts clouds |
| `piece_workbench` station prefab | common knowledge, research "unverified" | `sneak.content` |
| `Resin`, `Coal`, `LeatherScraps` prefab names | SoftRef manifest (via `ux-container-sort.md`) | `sneak.content` |
| `Bush01`, `RaspberryBush`, `Beech1` and the 38 viewblock plants; layer `viewblock` index 25; tree crown collider shapes | asset read | `sneak.cover` dump (colliders, layers, raycasts) |
| Whether the player's centre can get inside a bush's viewblock sphere (stem collision) | asset read | `sneak.cover`; `FoliageReach` covers it |
| Fog densities per environment (all of 1.4) | asset read | `sneak.cover` logs our density and `RenderSettings.fogDensity` per forced environment |
| Sneak `m_increseStep` 0.5 | asset read | `sneak.content` NOTE; `sneak.xp` measures the progress |
| `StealthSystem` light range 0.2-2.0 and shadow mask | asset read | `sneak.content` NOTE |
| Crouch kept or dropped during a crouched bow draw | code reading (animator) | manual T08 with a bow; research check C3 |
| Cue icon sources (`Fiddleheadfern`, `Wisp` items, `Wet` effect, Sneak skill icon) and how they look | names from the manifest / dump; looks unknown | `sneak.content` NOTE; screenshot |
| The cloud visual (borrowed material, particle settings) | none | `sneak.smoke-throw` screenshot; in-game T14 |
| `Player.SetControls` drives movement in a self-test | not tried | `sneak.still` (fallback: set `m_moveDir`) |
| `Karve` ship prefab name | common knowledge | T08 (any ship works) |
| GUIDs of Goo's Combat Overhaul, SNEAKer, EliteCreaturesReborn, SpecialAttack, Valheim Legends port | closed source or not read | `sneak.content` logs every loaded plugin GUID when present |
| `tod` / `env` / `tame` console syntax for testers | `Terminal.cs` (`tod` sets `EnvMan.m_debugTime` 0-1; `env` sets the debug environment; `tame` tames nearby tameable creatures) | S02 |

---

## Implementation notes

v0.1.0 as built, in short (details and reasons in 7.7). Every point below differs from, or makes precise, the design
above; nothing in the Goal (G1-G12) or the extras (E1-E6) was dropped.

- **Code:** percent settings are whole numbers; `FogDensityFullBonus` not above `FogDensityNoBonus` gives the full
  bonus; other-mod detection runs at first use (XP event, stealth refresh, self-test), not in `OnActivated`; the
  server sends its rules only to players with the same network version and answers only layout-1 requests;
  `AmbushRules.Pending` also holds neutral numbers; the stillness snap goes up to the lower of "without the still
  bonus" and the new target, never down; the XP handler refuses a negative victim health (no flat XP); the Smoke
  Screen's attack-level spawn is cleared too; a missing `BombSmoke` only logs an error (the Status line stays active);
  the cloud visual lives apart from the cloud object and fades on its own; clouds made without a throw use the rules in
  force (defaults while pending); the water base is sampled at the impact and 1 m below; the burst blind is retried
  each frame of its window until it has run once and blinds only alerted chasers; a blinded creature cannot hear
  players even with `BlocksHearing` off; the Holding still and In smoke tooltips follow `StillEndsAtOnce` and
  `BlocksHearing`; network time is read directly; icon numbers use an ASCII hyphen ("-70%"); an empty `RecipeStation` means craft
  anywhere and an unknown one hides the recipe with a warning; foliage checks include trigger colliders
  (`QueryTriggerInteraction.UseGlobal`, 2.4); an `NpcTalk` NPC with creature AI does not greet a smoke-hidden player.
  Review fixes (7.7): the cloud's ground ray reaches 1000 m (a mid-air time-out no longer floats); the build-up is cut
  to leave at least 1 s of hiding; the blind never outlasts the cloud and its fade; the smoke look stops emitting
  2.4 s before the end and starts nearly opaque; `LastXp` is written with the send; tamed creatures keep their plates;
  texts use `ModInfo.Name`. Hunting creatures (raids) never give up the chase (vanilla re-alert, documented).
- **Self-tests:** the twelve tests of 7.5 with the precisions listed at the end of 7.7 (test rules instead of the
  config, extra name checks in `sneak.content`, the carried-player steps of `sneak.still`, a 12 s blind in
  `sneak.smoke-blind`, `ActivationDelay` 0 in `sneak.smoke-sight` and `sneak.healthbars`, a mid-air time-out 30 m up
  in `sneak.smoke-throw`, the timeline helpers in `sneak.network`, a tame in `sneak.healthbars`). Debug-only hooks:
  `StealthCues.TestShowCues`, `SneakXp.TestShowMessage`, `HumanoidPatches.TestResetMessageTimer`,
  `Plugin.TestInactive`.
- **`TESTING.md`** keeps the IDs of section 8 (T01-T26, L01-L04, M01-M16, X01-X09) with these changes: T02's first
  hit is with bare hands (a flint knife backstab, x6, kills a 40-health Greydwarf, which would end the test); T08 also
  checks the crouched bow draw (research C3); T14 adds a throw onto a burning campfire (no fire put out); T20 shoots
  from inside the smoke with a bow (a melee hit from inside means standing within reach, where the 2.5 m rule already
  applies), at Bows 0 on two-star Greydwarfs (120 health) and a level-9 Deer (90 health), because the arrow is a
  backstab (x3, 33-73 damage at Bows 0) that kills a no-star Greydwarf about four times in five and a Deer always,
  and a dead target can neither fight nor flee; L04 reruns T20 with that setup and X01's hit from the smoke on an afraid
  two-star Greydwarf (afraid from Creature Morale's rank 6) uses the same shot; T26 (tames keep their bars) is new with the tame fix; M03, M04, M09 make B's game
  control the creatures by having B spawn them (ownership does not follow who stands closer); L01 adds a Greydwarf 8 m
  away to show that the cloud stops hiding; X01 adds Creature Morale's plate check (its X01; since Morale dropped its own
  "afraid" label, the check is the game's alert icon on the bar that comes back); **X10 is new**
  (optional): SecondaryAttacks or SmartSkills installed, our XP stands down and the log says so (the `Compat` code had
  no test). The Setup asks to run the non-X items with Creature Morale off.
  Names not in the checked game data are marked "(unverified)" there: `Resin`, `Coal`, `LeatherScraps`,
  `piece_workbench`, `ArrowWood`, `BoltBone`, `Bush01`, `RaspberryBush`, `Beech1`, `Karve` and the weather names.
- **README:** the sentence "Smoke Screens stay in your inventory while it is off" is in the `Enabled` row (the
  framework writes that setting's description). The XP table lists only creatures whose health is in the 1.0.16 dump.
