# Existing mods — Combat / UX

Research done 2026-09 (snapshot taken 2026-09-28, about three weeks after Valheim 1.0 shipped on 2026-09-09).

## How to read this

- **Sources.** Thunderstore package pages and the Thunderstore package API (`/api/experimental/package/<ns>/<name>/`) for versions and dates. GitHub for source repositories. Nexus pages return HTTP 403 to automated fetches, so Nexus facts come from search snippets only and are marked as such.
- **Dates.** "Updated" is Thunderstore's `date_updated`. Listing edits can also change this date, so it does not always mean a new build.
- **1.0 status legend:**
  - **yes**: the author says the mod supports Valheim 1.0, Unity 6 or the Deep North update.
  - **likely**: a release came out after 2026-09-09, but the author does not say it supports 1.0.
  - **unknown**: the last release is from before 1.0. Valheim 1.0 moved the game to Unity 6000.0.75f1 and broke most BepInEx mods, so assume these need a rebuild.
  - **dead**: the package is deprecated or has not changed since 2021-2022.
- **Coverage.** Coverage says how much of *our* idea the existing mods already implement: none, partial or full.
- **Low-effort mods.** Many packages uploaded after 1.0 carry Thunderstore's "AI Generated" category and have very few downloads. Treat them as sources of ideas only, not as a measure of quality.
- **Licenses.** Several useful repos are GPL/LGPL. Read them to learn, but do not copy their code into our repo.

---

## Combat

### Weapon revamp (Revamp): new moves on roll / jump / parry, a unique move per weapon type

**Coverage: partial.** Several recent mods add running and jump attacks, per-weapon "special" attacks, or a quickstep dash. None of them offers a coherent set of *context* moves: roll-attack, plunge from a jump, riposte after a parry, plus one signature move per weapon type, all feeling like vanilla.

| Mod | Status | Notes |
|---|---|---|
| [Goo's Combat Overhaul (gnls)](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) | v2.1.2, updated 2026-09-26, 1.0: yes (targets 1.0.16) | Souls-like overhaul. Running and jump attacks per weapon type (jump attack = press attack before landing), "planted" lunges, a hyperarmor system with 5 modes, stagger buildup rework, positional bonuses (Sneak, Flank, Execution, Counter). Uses YAML per-weapon profiles plus console commands. Needs ConditionalConfigSync. Reuses vanilla animations. Closed source. About 8.4K downloads. |
| [SpecialAttack (MM94)](https://thunderstore.io/c/valheim/p/MM94/SpecialAttack/) | v1.2.2, updated 2026-09-27, 1.0: yes (rebuilt for Unity 6) | One special per weapon category (10 categories), on the secondary attack or a custom key. Examples: sword 3-hit chain, sledge aftershock rings, spear pin, knife smoke vanish. Extra hits unlock with skill level. Cooldown HUD. A "Quickstep" dash replaces the roll. Tagged AI Generated, under 1K downloads. |
| [SecondaryAttacks (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/SecondaryAttacks/) | v1.2.13, updated 2026-09-27, 1.0: likely | New secondary attacks for bows, staves, bombs, every melee class and blood magic. YAML with per-prefab overrides, plus a list of usable animation names. Cooldown HUD, optional quickstep for knives and fists. About 12K downloads. |
| [AttackCancleCounter (IDRdhnTM)](https://thunderstore.io/c/valheim/p/IDRdhnTM/AttackCancleCounter/) | v1.2.0, 2025-11-16, 1.0: unknown | Cancels an attack mid-animation into a parry or dodge, and allows a "counter" while chaining attacks. |
| Related | | [Valheim Legends 1.0 port (momos3939)](https://thunderstore.io/c/valheim/p/momos3939/ValheimLegends/) (v0.7.10, 2026-09-11, 1.0: yes) has class abilities, including the Duelist's "Riposte" (parry and counter in one move). [GrindstoneSkills (MilkyTeam)](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) (v0.9.1, 2026-09-27) buffs your next attack after a parry. |

**Inspiration.**
- **Borrow** GCO's input model (a running attack is a primary attack above a speed threshold; a jump attack is a primary attack before landing) and its idea of per-weapon YAML profiles. Also borrow SpecialAttack's skill-gated unlocks, which give progression a reason to exist.
- **What they get wrong:**
  - Most of them overload the vanilla secondary attack or add a new key.
  - Many spawn flashy projectiles (sword waves, exploding axes) that do not look like Valheim.
  - GCO changes stamina, stagger, PvP and AI in one package, so you cannot adopt only the moveset.
- **How ours can differ:**
  - Keep the vanilla inputs and make moves depend on context: attack during or right after a roll = roll-attack; attack while airborne = plunge; attack inside a short window after a perfect block = riposte.
  - Give each weapon type exactly one signature move.
  - Build everything on the vanilla `ItemDrop.SharedData` attack data and the `Attack` class with existing animator states, so it stays multiplayer-safe.
  - Hook the moves into the adrenaline revamp. Vanilla already awards adrenaline on a perfect block inside `Humanoid.BlockAttack` through a per-item perfect-block adrenaline value.

### Sneak revamp (Revamp): Sneak XP on sneak attacks, fewer early detections

**Coverage: partial.** SmartSkills already gives bonus Sneak XP on backstabs. Nothing current changes early-game detection. The one mod that did both is dead.

| Mod | Status | Notes |
|---|---|---|
| [SmartSkills (Smoothbrain)](https://thunderstore.io/c/valheim/p/Smoothbrain/SmartSkills/) | v1.0.2, 2024-06-21, 1.0: unknown | Sneak raises backstab damage and gives bonus Sneak XP on a backstab. Also changes other skills (swim XP, catch-up XP). Open source: [blaxxun-boop/SmartSkills](https://github.com/blaxxun-boop/SmartSkills). About 120K downloads. |
| [SNEAKer (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/SNEAKer/) | v1.1.8, updated 2026-09-15, 1.0: yes (tagged Deep North) | Sneak movement speed scales with Sneak skill; configurable XP multiplier; ServerSync. About 138K downloads. |
| [Valheim Combat Overhaul (Kyresel / leseryk)](https://thunderstore.io/c/valheim/p/Kyresel/CombatOverhaul/) ([Nexus 591](https://www.nexusmods.com/valheim/mods/591)) | v1.7.8, 2021-04, dead (deprecated) | Implemented exactly this idea: XP for a successful stealth attack (scaled by the weapon's backstab bonus, lower for projectiles) and less effective enemy detection at low Sneak skill. A 2022 reupload ([HHPatch](https://thunderstore.io/c/valheim/p/NotMyMods/CombatOverhaul_HHPatch/)) is also deprecated. |
| [Goo's Combat Overhaul](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) | see above | "Sneak" positional damage bonus against unaware targets, scaling with skill. |

**Inspiration.**
- **Vanilla today:**
  - Sneak XP only comes from moving while sneaking near enemies (`Player.UpdateStealth` raises Sneak).
  - The backstab multiplier is applied in `Character`'s damage handling. It only applies when the victim's AI is not alerted, and each creature can only be backstabbed once every 300 s.
  - The stealth factor is a lerp between light level and Sneak skill (`Player.UpdateStealth`). At skill 0 the lerp starts high, which is why early-game sneaking feels useless.
- **Ours:**
  - Grant Sneak XP on a backstab, scaled by creature tier or damage dealt.
  - Flatten the low-skill end of the stealth curve, or give a bonus at night, in cover or in dark armor.
  - Add a subtle readout on the existing stealth HUD element so players can learn the system. [BetterStealthIndicator (Jaybirds)](https://thunderstore.io/c/valheim/p/Jaybirds/BetterStealthIndicator/) is a UI reference for this.
  - Keep Kyresel's design, but ship it maintained and server-synced.

### Trinket revamp (Revamp): passive effect plus an adrenaline boost

**Coverage: partial.** The exact concept exists as a small value tweak (BetterTrinkets). There is no deeper redesign.

| Mod | Status | Notes |
|---|---|---|
| [BetterTrinkets (Schwifty)](https://thunderstore.io/c/valheim/p/Schwifty/BetterTrinkets/) | v1.0.0, 2026-02-28, 1.0: unknown | Trinket effects are always active as a weaker passive. Full adrenaline **doubles** the effect for the trinket's normal duration. All 13 trinkets have config values. The mod is client-side, which is a balance and multiplayer concern. About 2.6K downloads. |
| [MultiTrinket (QQMZR)](https://thunderstore.io/c/valheim/p/QQMZR/MultiTrinket/) | v1.0.1, 2026-03-04, 1.0: unknown | For mods that add extra trinket slots: uses the highest adrenaline cost of all equipped trinkets and triggers all of them together. Source: [MoistMonster22/MultiTrinket](https://github.com/MoistMonster22/MultiTrinket). |
| [ClassTrinkets (JamesJonesTV)](https://thunderstore.io/c/valheim/p/JamesJonesTV/ClassTrinkets/) | v1.0.2, 2026-02-12, 1.0: unknown | Adds 40 new trinkets (8 classes x 5 ranks) with static stats. This is new content, not a rework of the mechanic. |

**Inspiration.**
- **Vanilla:**
  - A trinket contributes max adrenaline through `SharedData.m_maxAdrenaline` (summed as an equipment modifier in `Player`) and has a `m_fullAdrenalineSE`.
  - When the bar fills, `Player.AddAdrenaline` applies the full-adrenaline status effect of every equipped item and resets the bar to 0.
  - `Player` also has a tiered `m_adrenalineEffects` list (status effects by adrenaline level) that we could reuse for "build-up" tiers.
- **Borrow** BetterTrinkets' split into a passive and a surge.
- **Ours:**
  - Make the numbers server-authoritative.
  - Show the passive and the surge separately in the tooltip.
  - Consider a "build-up" tier that uses `m_adrenalineEffects` so a partly full bar also matters.
  - Design it together with the adrenaline and boss-power revamps below, as one system.

### Adrenaline revamp (Revamp): "it sucks, change it"

**Coverage: partial.** The only mods found are numeric tweaks (gain, decay and delay multipliers). Nobody has redesigned how adrenaline is earned or spent.

| Mod | Status | Notes |
|---|---|---|
| [AdrenalineModifier (mightywa33ior)](https://thunderstore.io/c/valheim/p/mightywa33ior/AdrenalineModifier/) | v1.0.1, 2025-10-05, 1.0: unknown | Multipliers for adrenaline growth, decay and decay delay. Source: [lukeadickinson/valhiem-adrenalinemodifier](https://github.com/lukeadickinson/valhiem-adrenalinemodifier). About 3K downloads. |
| [BetterTrinkets](https://thunderstore.io/c/valheim/p/Schwifty/BetterTrinkets/) | see above | Turns adrenaline into a bonus on top of the passive effect instead of a gate. |
| [ForsakenPowerOverhaul (momos3939)](https://thunderstore.io/c/valheim/p/momos3939/ForsakenPowerOverhaul/) | see Boss power | Makes guardian power activation feed the adrenaline boost. |

**Inspiration.**
- **Vanilla is highly data-driven:**
  - Decay, decay delay and gain are driven by `AnimationCurve`s on `Player` (`m_adrenalineDegen`, `m_adrenalineDegenDelay`, `m_adrenalineGainMultiplier`).
  - There is a world-level multiplier, `Game.m_adrenalineRate`.
  - Status effects can modify gain through `SE_Stats.m_adrenalineModifier`, and give it up front through `m_adrenalineUpFront`.
  - Individual actions give fixed amounts, for example `Player.m_staggerEnemyAdrenaline` and the perfect-block adrenaline mentioned above.
  - Activating a guardian power adds a flat 10 (`Player.m_adrenalineGuardianPower`).
- A pure numbers mod is therefore trivial, which is why that is all anyone has made.
- **Ours can change the model:**
  - Earn adrenaline from risky, skilled play: perfect blocks and dodges, backstabs, staggers.
  - Slow the decay while in combat.
  - Optionally let the player **spend** it on purpose, for example on a weapon signature move or a trinket surge, instead of the automatic pop.
  - Keep one shared resource for trinkets, boss powers and weapon moves.

### Boss power revamp (Revamp): same approach as trinkets

**Coverage: full.** "Weaker passive plus an activatable burst" is a well-established idea with a very popular mod. Our only differentiator would be tying it into our adrenaline and trinket design.

| Mod | Status | Notes |
|---|---|---|
| [PassivePowers (Smoothbrain)](https://thunderstore.io/c/valheim/p/Smoothbrain/PassivePowers/) | v1.1.5, Thunderstore release 2026-02-05, 1.0: unknown | Boss powers become weaker passives, with an optional short burst on activation. Some bosses get extra powers for balance. Server-enforceable. About 490K downloads. Source: [blaxxun-boop/PassivePowers](https://github.com/blaxxun-boop/PassivePowers). The repo was pushed on 2026-09-11, so a 1.0 build may be pending. |
| [ForsakenPowerOverhaul (momos3939)](https://thunderstore.io/c/valheim/p/momos3939/ForsakenPowerOverhaul/) | v2.2.0, 2026-09-10, 1.0: yes (port of JuneGame's mod) | Four layers: a permanent passive when the trophy is offered, an "equipped" passive for the selected power, an active buff, and a shared buff while any power is active. Cycle powers with G. Integrates with the trinket slot, and activating a power triggers the adrenaline boost. Presets plus more than 3,300 config lines. Source: [JuneGame/Valheim.ForsakenPowerOverhaul](https://github.com/JuneGame/Valheim.ForsakenPowerOverhaul). |
| [ProgressivePowers (MidnightMods)](https://thunderstore.io/c/valheim/p/MidnightMods/ProgressivePowers/) | v0.3.3, 2026-09-14, 1.0: yes (released after 1.0) | Powers become permanent passives that get stronger as you kill more bosses. |
| [ForsakenPowersPlusRemastered (turbero)](https://thunderstore.io/c/valheim/p/turbero/ForsakenPowersPlusRemastered/) | v2.0.4, updated 2026-09-10, 1.0: likely | Switch between earned powers with a button; change duration and cooldown; passive mode or stacking. Source: [Turbero/ForsakenPowersPlusRemastered](https://github.com/Turbero/ForsakenPowersPlusRemastered). |

**Inspiration.**
- **What they get wrong.** FPO's 3,300 config lines show that making powers passive leads to a balance explosion.
- **Ours:**
  - Build it as a thin layer on our adrenaline system: a small passive per defeated boss plus an adrenaline-fuelled surge, with no separate cooldown economy.
  - Otherwise, simply recommend the existing mods and skip this idea.

### Ballista revamp (Revamp): better AI, target assignment

**Coverage: partial.** Several mods expose targeting and range settings, and one adds lead prediction. None coordinates several turrets or offers target priorities.

| Mod | Status | Notes |
|---|---|---|
| [BetterBallistas (Neobotics)](https://thunderstore.io/c/valheim/p/Neobotics/BetterBallistas/) | v1.0.0, updated 2026-09-11, 1.0: yes | Toggle targeting of enemies, tames and players; detection range; firing arc up to 180 degrees; ammo cap; unlocks the vanilla one-trophy limit; scan and turn-rate tuning. About 21K downloads. |
| [Turrets Redo (MrGay)](https://thunderstore.io/c/valheim/p/MrGay/Turrets_Redo/) | v0.0.4, 2026-09-21, 1.0: likely | Lead shots using target velocity and projectile speed, spread control, ignores players, tames and friendly Dvergr (with optional projectile immunity), infinite ammo option. Needs ConditionalConfigSync. Tagged AI Generated, about 200 downloads. |
| [ValheimFortress (MidnightMods)](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimFortress/) | v0.37.2, 2026-09-18, 1.0: likely | Wave-arena mod that also changes ballista targeting: the ballista shoots whenever the shot would hit *any* hostile, not only its primary target. |
| [ImFRIENDLY DAMMIT (Azumatt)](https://thunderstore.io/c/valheim/p/Azumatt/ImFRIENDLY_DAMMIT/) | date not checked | Ballistas never hit you or your tames. Overrides BetterBallistas' settings for players and tames. |

**Inspiration.**
- **Vanilla `Turret`:**
  - Has `m_targetPlayers`, `m_targetTamed` and `m_targetEnemies` flags.
  - Supports trophy-based target filtering (`m_configTargets`, `m_maxConfigTargets = 1`).
  - Has a basic `m_predictionModifier` and near and far target-update intervals (`Turret.UpdateTarget`).
- **What nobody does:**
  - A **shared target registry**, so that neighbouring ballistas do not all shoot the same greyling.
  - **Priority rules**: closest, highest threat, attacking a structure, or low HP.
  - Line-of-fire checks against friendly pieces.
- **Borrow** lead prediction from Turrets Redo and "shoot if the path hits any hostile" from ValheimFortress.

### Mob AI revamp (Revamp): flee when scared, avoid strong players, do not get stuck when rooted or frozen

**Coverage: partial.** Two 2026 mods implement fear and "respect" of strong players. Several data editors expose the vanilla flee settings. Nothing addresses creatures getting stuck after crowd control.

| Mod | Status | Notes |
|---|---|---|
| [The Mark of Oden (PicSoul)](https://github.com/PicSoul/TheMarkOfOden) | GitHub only, created 2026-09-16, pushed 2026-09-24, 1.0: likely | Creatures judge threat from **bosses killed and species kill count, not gear**. States: Wary (will not start a fight), Fleeing, Provoked, Unafraid. Nerve depends on pack size, night and star level. Prey animals always fight back. Raid and boss-summoned creatures ignore fear. Nameplate colours show the state. Uses ServerSync. No Thunderstore or Nexus listing found. |
| FleeOnSight ([Nexus 2764](https://www.nexusmods.com/valheim/mods/2764)) | Nexus only, date unknown (page blocked) | Listed creatures flee as soon as they are alerted, gated by bosses killed. Default: Greylings fear you after Eikthyr, Greydwarfs after the Elder. |
| Monster AI Tweaks ([Nexus 758](https://www.nexusmods.com/valheim/mods/758), [Thunderstore](https://thunderstore.io/c/valheim/p/tweaks/MonsterAITweaks/)) | Thunderstore v0.4.0, 2022-11, 1.0: unknown (Nexus shows activity into 2026) | Per-monster target preferences (players or buildings), alertness, fire fear, sight and hearing ranges. The Nexus version adds flee and chase behaviour by diet (herbivore, carnivore, omnivore) and "use the surroundings" (for example, run into water when burning). |
| [MonsterDB (RustyMods)](https://thunderstore.io/c/valheim/p/RustyMods/MonsterDB/) | v0.4.0, 2026-09-20, 1.0: likely | YAML editing and cloning of creatures, including the flee fields on `MonsterAI`, custom factions, attacks and spawn data. The RRR successor. Source: [RustyMods/MonsterDB](https://github.com/RustyMods/MonsterDB). |

**Inspiration.**
- **Vanilla `MonsterAI` already has most flee settings**, and most prefabs leave them unused:
  - `m_fleeIfLowHealth`, `m_fleeIfNotAlerted`, `m_fleeIfHurtWhenTargetCantBeReached`, `m_fleeTimeSinceHurt`.
  - `m_crownFearRange`: the 1.0 Crown of Valheim makes ordinary creatures flee.
  - `BaseAI` has `m_fleeRange` and `m_fleeAngle`.
- Data editors (MonsterDB, Monster AI Tweaks) only set these values.
- **Borrow** Mark of Oden's "progression, not gear" threat model: it cannot be cheesed by borrowing armor.
- **Ours adds behaviour instead of data:**
  - Morale breaks: flee at low HP, or when the pack leader dies.
  - Weak creatures give a clearly out-levelled player a wide berth.
  - An **unstick routine** after root, freeze or stagger: re-path, hop, or reposition when the creature has not moved toward its target for N seconds. Nobody has done this.
- The AI runs on the zone owner, so all logic must work for whichever client owns the creature.
- [Valheim Community Patch](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimCommunityPatch/) (v0.30.0, 2026-09-27) fixes a related vanilla bug: creatures stuck at 0 HP.

### Training dummies can attack enemies (Revamp)

**Coverage: none.** The only related mod goes the opposite way: dummies hunt *players*.

| Mod | Status | Notes |
|---|---|---|
| [TouchGrass (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/TouchGrass/) | v1.0.7, 2026-09-10, 1.0: likely | DPS and XP training meter, configurable dummy damage, "dummies can hunt players at night", and anti-macro stationary fatigue. |
| [DPS (JereKuusela)](https://thunderstore.io/c/valheim/p/JereKuusela/DPS/) | v1.7.0, 2026-09-10, 1.0: likely | Spawn, reset and kill dummy commands, with configurable resistances and status effects. A test tool, not a gameplay feature. |
| [OdinTrainingPlace (OdinPlus)](https://thunderstore.io/c/valheim/p/OdinPlus/OdinTrainingPlace/) | v1.6.6, 2026-09-19, 1.0: yes | Its dummies are passive; none of them attacks anything. |

**Inspiration.**
- **Vanilla 1.0** has the T.W.I.G. training dummy (Call to Arms). It uses its own faction, `Character.Faction.TrainingDummy`, and `BaseAI.IsEnemy` makes that faction hostile **only to Players**.
- One faction rule and a target-selection hook are enough to make dummies decoys that draw aggro, or sparring partners that hit monsters. There is no prior art.
- **Watch out for:** balance as a base defence (keep damage low, or make them decoys only), and the trap of farming skill XP by letting monsters fight a dummy.

### Increase base HP/Stamina with stats (Revamp): running, jumping, and so on

**Coverage: full.** VitalityRewrite is almost exactly this idea. Stamina-only and RPG-attribute variants also exist.

| Mod | Status | Notes |
|---|---|---|
| [VitalityRewrite (Gratak)](https://thunderstore.io/c/valheim/p/Gratak/VitalityRewrite/) | v1.2.6, updated 2026-09-22, 1.0: likely | Running, swimming, jumping, mining, woodcutting and "not dying" raise a Vitality skill, which increases max HP, stamina and eitr and improves regeneration. Source: [Gra-tak/ValheimVitalityRewrite](https://github.com/Gra-tak/ValheimVitalityRewrite) (last push 2024, so the source may lag behind the release). |
| [StaminaExtended (shudnal)](https://thunderstore.io/c/valheim/p/shudnal/StaminaExtended/) | v1.0.11, updated 2026-09-11, 1.0: likely | Base stamina scales with skill levels up to skill 100. Also changes regeneration, encumbrance, swimming, sneaking and surface-dependent stamina. Source: [shudnal/StaminaExtended](https://github.com/shudnal/StaminaExtended). |
| [WackyEpicMMOSystem (WackyMole)](https://thunderstore.io/c/valheim/p/WackyMole/WackyEpicMMOSystem/) | v1.9.67, 2026-09-13, 1.0: yes (rebuilt for 1.0) | A full level and attribute-point system that feeds HP, stamina and more. Heavy RPG layer. Source: [Wacky-Mole/WackyEpicMMOSystem](https://github.com/Wacky-Mole/WackyEpicMMOSystem). |
| [SkillsReworked (M2Valheim)](https://thunderstore.io/c/valheim/p/M2Valheim/SkillsReworked/) | v2.0.0, 2026-09-24, 1.0: likely | Level XP from kills and 4 attributes that drive all skills, with prestige. |

**Inspiration.**
- **Vanilla:** base values are `Player.m_baseHP` and `Player.m_baseStamina`. Their code defaults are 25 and 75 in 1.0, but the prefab may override them. Food adds to them in `Player.GetTotalFoodValue`.
- **Ours:** a small, capped bonus from existing skills only (Run, Jump, Swim, Sneak), with no new "Vitality" skill. That keeps it a light touch next to food.
- Only build this if it fits our progression philosophy; VitalityRewrite already covers the idea.

### Better tower shields (Revamp): an immovable wall that cannot parry, heavily slowed, blocks nearly everything

**Coverage: partial.** GCO is close to the vision, but it only comes as part of a huge overhaul. The standalone mods only change numbers.

| Mod | Status | Notes |
|---|---|---|
| [Goo's Combat Overhaul](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) | see above, 1.0: yes | Tower shields keep blocking until a blocked hit empties your stamina. A successful block prevents stagger and knockback, covers a wider angle and reduces the remaining physical damage. You cannot parry or run while blocking, and the equipment penalty is heavier. |
| [ZenCombat (ZenDragon)](https://thunderstore.io/c/valheim/p/ZenDragon/ZenCombat/) | v1.0.2, updated 2026-09-25, 1.0: likely | Tower shield "block charges": every N blocks (default 5) triggers a counter-attack. "Reliable block": block defence always applies while you have stamina, even when staggered. Also dodge on a separate button, auto-equip shield, and more. Closed source (the [GitHub repo](https://github.com/ZenDragonX/ZenMods_Valheim/wiki) is only a wiki and issue tracker). About 97K downloads. |
| [Make Tower Shields Great Again (FactoriaTeam)](https://thunderstore.io/c/valheim/p/FactoriaTeam/Make_Tower_Shields_Great_Again/) ([Nexus 2900](https://www.nexusmods.com/valheim/mods/2900)) | v0.0.4, 2024-10-25, 1.0: unknown | Stat buffs, extra resistances, and knockback damage dealt on block, scaled by block level. |
| [Valheim Combat Overhaul (Kyresel)](https://thunderstore.io/c/valheim/p/Kyresel/CombatOverhaul/) | 2021, dead | No parry, but blocks damage beyond block power. Surplus damage drains stamina instead of health. Lower block stamina cost, "stickiness" to the opponent, and the vanilla -20% movement penalty. |

**Inspiration.**
- **Vanilla `Humanoid.BlockAttack`:**
  - A perfect block needs `m_timedBlockBonus > 1`, and tower shields do not have it.
  - Block power scales with skill; the stamina drain is based on excess damage.
- **Borrow** GCO's "hold until stamina is empty, no stagger or knockback" and Kyresel's "overflow drains stamina, not HP".
- **Ours:** a standalone, cleanly configurable "shield wall" stance:
  - Heavy slow while blocking (much stronger than vanilla).
  - Frontal immunity to stagger, knockback and most AoE and projectiles.
  - No parry.
  - Optionally a shield bash, or a "brace" that roots you in place.
- It must not clash with GCO or ZenCombat. Detect them and warn, or document the incompatibility.

### Crossbow revamp (QoL): stays loaded

**Coverage: full, but abandoned.** The exact feature exists in SaveCrossbowState, which is now deprecated. No maintained 1.0 mod has it. FastArbalest only keeps the bolt loaded while the crossbow is slung on your back.

| Mod | Status | Notes |
|---|---|---|
| [SaveCrossbowState (Azumatt)](https://thunderstore.io/c/valheim/p/Azumatt/SaveCrossbowState/) | v1.0.2, updated 2025-08-29, **deprecated** (no reason given), 1.0: unknown | Keeps the loaded state when you switch weapons or unequip. v1.0.1 added the Dundr staff, which also uses reload. Client-only, no config. About 152K downloads. No public source repo found (github.com/AzumattDev/SaveCrossbowState returns 404). |
| [FastArbalest (Neobotics)](https://thunderstore.io/c/valheim/p/Neobotics/FastArbalest/) ([Nexus 2316](https://www.nexusmods.com/valheim/mods/2316)) | v1.0.0, updated 2026-09-11, 1.0: yes | Reload speed (10-100%) with optional proportional damage. "Pausable reload" pauses instead of restarting. "Sling Loaded" keeps the bolt only while the crossbow is hidden with the show/hide key or while swimming: *unequipping or swapping still unloads it*. Also realistic recoil and knockback. |
| [BetterCrossbows (xtavim)](https://thunderstore.io/c/valheim/p/xtavim/BetterCrossbows/) | v1.1.0, updated 2026-09-09, 1.0: likely | Reload time, reload while moving, stamina drain toggle, ServerSync. Its README tells users to add SaveCrossbowState to get this feature. Source: [xtavim/BetterCrossbows](https://github.com/xtavim/BetterCrossbows). |
| [Crossbow Reload Tweaked (Crbakc)](https://thunderstore.io/c/valheim/p/Crbakc/Crossbow_Reload_Tweaked/) | v1.1.1, 2025-06-01, 1.0: unknown | Reload speed, movement speed and stamina drain while reloading. |

**Inspiration.**
- **Vanilla mechanics:**
  - `Player` stores the loaded weapon as an `ItemData` reference (`m_weaponLoaded`) and mirrors a bool to the player ZDO (`ZDOVars.s_weaponLoaded`) so other clients can see it (`Player.SetWeaponLoaded` / `Player.IsWeaponLoaded`).
  - `Humanoid.UnequipItem` calls `ResetLoadedWeapon` for any weapon.
  - `Player.UpdateWeaponLoading` queues a reload action whenever the equipped weapon is not the loaded one. The action drains eitr through `m_reloadEitrDrain` for Dundr.
  - `Attack` checks `IsWeaponLoaded` for weapons with `m_requiresReload`.
- **Ours:**
  - Persist a per-item "loaded" flag, for example in `ItemData.m_customData`, so the state survives unequip, swaps and (by config) logout.
  - Restore it on equip instead of re-queuing the reload.
  - Cover *every* weapon with `m_requiresReload`: crossbows, Dundr and modded weapons.
  - Decide what happens when the item is dropped or traded, and whether eitr is refunded.
- The market gap is simply a maintained, 1.0-verified version.

### Ritual (New): consumes items, needs several people

**Coverage: none.** No mod found requires several participants. The existing altar and sacrifice mods are single-player, and most are deprecated.

| Mod | Status | Notes |
|---|---|---|
| [Krumpac Sacrificing Altars](https://thunderstore.io/c/valheim/p/Krumpac/Krumpac_Sacrificing_Altars/) | v7.3.2, 2025-09-07, dead (deprecated; needs registration) | Altars per biome to Norse gods, a "praying" skill, and sacrifices for blessings. Part of the Reforge modpack. About 300 MB. |
| [CustomizeAltars (Huntardys)](https://thunderstore.io/c/valheim/p/Huntardys/CustomizeAltars/) | v2.0.0, 2024-02, dead (deprecated) | JSON-configured boss altars: which boss spawns, and which items and amounts are required. |
| [Vikings Summoner (Radamanto)](https://thunderstore.io/c/valheim/p/Radamanto/Vikings_Summoner/) | v1.5.1, 2026-09-26, 1.0: likely | "Ritual artifacts" and summoning totems in a necromancer theme. The ritual is flavour only; one player uses an item. |
| [RtDMonsters (Soloredis)](https://thunderstore.io/c/valheim/p/Soloredis/RtDMonsters/) | v2.4.14, 2026-05, dead (deprecated) | Summon custom bosses by placing items on an altar. |

**Inspiration.**
- **Vanilla building blocks:**
  - `OfferingBowl`: the boss altar, a single interaction that consumes items and spawns something.
  - `Incinerator`: the obliterator, which consumes items and produces a result.
  - `ShieldGenerator` and `PrivateArea`: area effects.
- **Ours:** a ritual circle piece with N participant runestones. Each player must hold "interact" at the same time, synchronized through the piece's ZDO. The ritual consumes offerings and triggers a world effect: summon, weather, a timed area buff, faster crop growth, calling a raid, and so on.
- This is a strong co-op hook, and a good cross-category "magic" theme (Combat, Farming, Building).

### New summons (New)

**Coverage: partial.** Undead necromancy is thoroughly covered. Non-undead, command-driven summons are not.

| Mod | Status | Notes |
|---|---|---|
| [Cheb's Necromancy (ChebGonaz)](https://thunderstore.io/c/valheim/p/ChebGonaz/ChebsNecromancy/) | v5.2.1, 2026-09-28, 1.0: likely | Wands for skeleton and draugr warriors, archers, mages, miners and woodcutters. Commands: follow, wait, roam, teleport to you. Pylons for gathering, refuelling, farming, repair and ghost guards. Built on Jötunn. Open source (Unlicense): [jpw1991/chebs-necromancy](https://github.com/jpw1991/chebs-necromancy). About 119K downloads. |
| [Vikings Summoner (Radamanto)](https://thunderstore.io/c/valheim/p/Radamanto/Vikings_Summoner/) | v1.5.1, 2026-09-26, 1.0: likely | Necromancer weapons, summoning totems and armor for each biome. |
| [MagicPlugin (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/MagicPlugin/) | v2.2.2, 2026-09-24, 1.0: likely | Summoning totems (7 summonable creatures) among many magic weapons and accessories. |
| [Summon Mastery (Kolefield)](https://thunderstore.io/c/valheim/p/Kolefield/Summon_Mastery/) | v0.1.2, 2026-09-13, 1.0: likely | Summons scale with the staff's upgrade level (HP, regen, armor, speed, damage), and nearby summons follow you through portals. Source: [kolefield/valheim-summon-mastery](https://github.com/kolefield/valheim-summon-mastery). |

**Inspiration.**
- **Vanilla:** summons are spawned through `SE_Spawn`/spawn attacks (for example, Dead Raiser skeletons). They are short-lived tames with no commands.
- **Borrow** Cheb's command set and Summon Mastery's upgrade scaling and portal following.
- **Ours:**
  - Avoid undead, which is Cheb's territory. Go for nature, elemental or spirit summons that fit Valheim lore.
  - Make them come out of our ritual system, and give them a small command layer (hold, follow, attack the pinged target) that ties into the ping revamp.

### New magical items based on Valheim magic (ward, heal) (New)

**Coverage: partial.** Big content packs have healing staffs and shields. One new mod (ArcaneWard) is close to our "ward" idea and uses only vanilla assets.

| Mod | Status | Notes |
|---|---|---|
| [ArcaneWard (Madkatz)](https://thunderstore.io/c/valheim/p/Madkatz/ArcaneWard/) | v1.1.15, 2026-09-25, 1.0: yes | Three off-hand "wards" that block using a separate eitr reserve that scales with Blocking and Elemental Magic, plus battlemage weapons (Arcane Blade and Knife) with elemental secondaries. **Built only from vanilla assets.** Needs Jötunn. Source: [Torvios/ArcaneWards](https://github.com/Torvios/ArcaneWards). |
| [Wizardry (Therzie)](https://thunderstore.io/c/valheim/p/Therzie/Wizardry/) | v1.2.2, 2026-09-22, 1.0: likely | 5 elemental and 2 blood-magic staffs, mage armors, scrolls, potions, summons, a Wizard Table, a potion cauldron, and new enemies and locations. Custom assets (107 MB). About 509K downloads. |
| [MagicPlugin (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/MagicPlugin/) | v2.2.2, 2026-09-24, 1.0: likely | Includes a Staff of Healing, totems, and magical belts, rings, earrings and necklaces with their own slots. |
| [RtDMagic (Soloredis)](https://thunderstore.io/c/valheim/p/Soloredis/RtDMagic/) | v0.7.34, 2026-05, dead (deprecated) | Four tiers of AoE healing staffs, mage armor, early eitr food. |

**Inspiration.**
- **Vanilla already has:**
  - The blood-magic shield status effect `SE_Shield` (Staff of Protection).
  - The base `PrivateArea` ward.
  - The Ashlands `ShieldGenerator` dome.
- **Ours:** extend these systems, the ArcaneWard way (vanilla assets and mechanics first):
  - Placeable ward and heal totems that pulse `SE_Shield` or heal-over-time to allies in a radius.
  - A personal "ward charm".
  - Cleansing (remove poison or burning).
- Theme them with the ritual system rather than making another big staff pack.

### More training dummies (New)

**Coverage: partial.** OdinTrainingPlace is the maintained reference. Nothing builds on the vanilla T.W.I.G.

| Mod | Status | Notes |
|---|---|---|
| [OdinTrainingPlace (OdinPlus)](https://thunderstore.io/c/valheim/p/OdinPlus/OdinTrainingPlace/) | v1.6.6, 2026-09-19, 1.0: yes (updated in 1.6.5) | Archery target (also for crossbows), wooden dummy, mechanical block-training dummy, woodcutting pole, flint rock, running track, swimming pool, and XP potions. About 247K downloads. Focused on skill grinding. |
| [TargetPractice (Norheim)](https://thunderstore.io/c/valheim/p/Norheim/TargetPractice_Valheim/) ([Nexus 1246](https://www.nexusmods.com/valheim/mods/1246)) | v1.0.2, 2021, dead | A dummy and 4 archery targets. |
| [DPS (JereKuusela)](https://thunderstore.io/c/valheim/p/JereKuusela/DPS/) | v1.7.0, 2026-09-10, 1.0: likely | Configurable dummies (resistances, status effects) and DPS, stamina and XP meters. |
| [TouchGrass (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/TouchGrass/) | v1.0.7, 2026-09-10, 1.0: likely | Training meter, configurable dummy damage, anti-macro fatigue. |

**Inspiration.**
- **Ours:** variants of the vanilla T.W.I.G. rather than new standalone skill grinders:
  - Resistance or weakness dummies for testing damage types.
  - A **parry and dodge trainer** that swings on a readable timer.
  - A moving archery target.
  - A mounted or tall dummy.
  - An optional DPS readout, borrowed from DPS and TouchGrass.
- Keep XP gains neutral. TouchGrass's fatigue idea shows why: players exploit dummies to macro-farm skills.

---

## UX

### Loot filter (QoL)

**Coverage: full.** The user already knew of Nexus 116. All current options except one are configured through **text lists of prefab names**, which is poor UX.

| Mod | Status | Notes |
|---|---|---|
| Loki's Autoloot Trash Filtering ([Nexus 116](https://www.nexusmods.com/valheim/mods/116), [Thunderstore LCDR](https://thunderstore.io/c/valheim/p/LCDR/Lokis_Autoloot_Trash_Filtering/)) | Thunderstore v1.0.1, 2022-12-18, 1.0: unknown | Three modes (block all, block the trash list, allow all) and a configurable trash list (stone, wood, resin, bone fragments, and so on). |
| [AutoPickupIgnorer (PipMods)](https://thunderstore.io/c/valheim/p/PipMods/AutoPickupIgnorer/) | v1.1.0, updated 2026-09-09, 1.0: likely | You uncomment item names in the config. A hotkey cycles between config list, ignore all, and vanilla. Source: [michaelpipkin/PipValheimMods](https://github.com/michaelpipkin/PipValheimMods/tree/main/AutoPickupIgnorer). |
| [LootFilter (wonkotron)](https://thunderstore.io/c/valheim/p/wonkotron/LootFilter/) | v1.0.2, 2026-02-17, 1.0: unknown | A comma-separated prefab blacklist, edited through Configuration Manager. Source: [wonkovalheim/lootfilter](https://github.com/wonkovalheim/lootfilter). |
| [InventoryActions (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/InventoryActions/) | v1.1.8, 2026-09-26, 1.0: likely | Per-item auto-pickup control from an inventory hover button, with controller support. Also sort, favourites, restock and trash. |

**Inspiration.**
- **Best UX idea found:** the deprecated [RagnarsRokare AutoPickupSelector](https://thunderstore.io/c/valheim/p/RagnarsRokare/RagnarsRokare_AutoPickupSelector/) (2022) turned the Trophies tab into a clickable list that toggled auto-pickup per item.
- **Vanilla:** pickup is `Player.AutoPickup`, an overlap sphere of `m_autoPickupRange` with a global toggle key.
- **Ours:**
  - Toggle "never auto-pickup" from the inventory (modifier+click, or a context button) and show an icon on filtered items.
  - Category rules (trophies, materials of past biomes).
  - Share the same item-marking model as our sort, trash and favourite features.

### Sort chest (QoL)

**Coverage: full.** The space is saturated: one mature, MIT-licensed reference and at least four new 1.0-era competitors.

| Mod | Status | Notes |
|---|---|---|
| [Quick Stack Store Sort Trash Restock (Goldenrevolver)](https://thunderstore.io/c/valheim/p/Goldenrevolver/Quick_Stack_Store_Sort_Trash_Restock/) | v1.4.15, 2026-09-12, 1.0: yes (rebuilt for 1.0) | Sort the container and the inventory by category, name, weight or value. Alt+click to favourite items or slots. Quick stack to nearby chests, store all, trash, restock. Will not touch chests another player has open. About 940K downloads. MIT source: [Goldenrevolver/QuickStackStore](https://github.com/Goldenrevolver/QuickStackStore). |
| [QuickStackPlus (Goneryx)](https://thunderstore.io/c/valheim/p/Goneryx/QuickStackPlus/) | v1.2.1, 2026-09-24, 1.0: yes (released after 1.0) | "Smart Storage": item filters per container, stored on the container, which drive quick-stack routing. Sorting only on demand. Blacklist and junk. Server-controlled master settings. |
| [RunicStorage (Chazman)](https://thunderstore.io/c/valheim/p/Chazman/RunicStorage/) | v1.3.12, 2026-09-28, 1.0: likely | Remembers chest contents; quick-stack routing rules by item and biome; coloured chest labels; previews; restock, search, sort and consolidation. |
| [HexQuickStackStorage (Hex_Viking)](https://thunderstore.io/c/valheim/p/Hex_Viking/HexQuickStackStorage/) | v1.4.3, 2026-09-26, 1.0: yes (released after 1.0) | A Sort button under the container and an "S" button for the inventory, plus quick stack and delete. |

**Inspiration.**
- **Vanilla `InventoryGui`** only has Take All and Stack All (`OnTakeAll`, `OnStackAll`).
- **Build ours only as part of a unified storage-UX module** (sort, search, filter and loot rules sharing one item-category model and one favourites model). A standalone sorter adds nothing.
- **Borrow** QSSSTR's favouriting and multiplayer safety. Container writes must respect ZDO ownership.
- **Avoid** auto-sorting on open. QuickStackPlus deliberately does not do it, and it destroys players' spatial memory.

### Sort bags (QoL)

**Coverage: full** for the player inventory. For modded backpacks, the coverage is unclear.

| Mod | Status | Notes |
|---|---|---|
| [Quick Stack Store Sort Trash Restock](https://thunderstore.io/c/valheim/p/Goldenrevolver/Quick_Stack_Store_Sort_Trash_Restock/) | see above | Sorts the inventory while respecting favourites and the hotbar. Documents compatibility with Comfy Quick Slots and AzuExtendedPlayerInventory, but not with backpack mods. |
| [Stackmaster (JStack424)](https://thunderstore.io/c/valheim/p/JStack424/Stackmaster/) | v1.2.0, 2026-09-24, 1.0: likely | "Backpack" here means the player inventory. Auto-sorts it on open, plus opted-in vanilla chests. The hotbar and equipped items stay fixed. Protected stacks can have restock targets. Source: [JStack424/Stackmaster](https://github.com/JStack424/Stackmaster). |
| [InventoryActions (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/InventoryActions/) | see above | Sorts inventories and containers, with favourites and controller support. |
| [AdventureBackpacks (Vapok)](https://thunderstore.io/c/valheim/p/Vapok/AdventureBackpacks/) | v2.2.1, 2026-09-28, 1.0: likely | The main backpack mod. No sort feature documented. A compatibility target if "bags" means backpacks. Source: [Vapok/AdventureBackpacks](https://github.com/Vapok/AdventureBackpacks). |

**Inspiration.**
- **Open question:** does "bags" mean the player inventory, or backpack items? If it means backpacks, the gap is sorting inside AdventureBackpacks and similar mods, which needs soft integration with them.
- Otherwise this is the same module as "Sort chest", applied to the player grid: lock the hotbar row, respect favourites.

### Search chest (QoL)

**Coverage: full.** The user assumed nothing existed, but a wave of mods appeared right after 1.0.

| Mod | Status | Notes |
|---|---|---|
| [SonicChestFilters (SonicDM)](https://thunderstore.io/c/valheim/p/SonicDM/SonicChestFilters/) | v1.0.1, 2026-09-11, 1.0: yes (released after 1.0) | A Jötunn text box on the open chest. Substring and wildcard matching on localized name, token and prefab. **Hides** non-matching slots. The `locate` command makes matching nearby chests glow. Sort button. Tagged AI Generated. Source: [sonicdm/SonicChestFilters](https://github.com/sonicdm/SonicChestFilters). |
| [TidyChests (Muindor)](https://thunderstore.io/c/valheim/p/Muindor/TidyChests/) | v1.3.1, 2026-09-24, 1.0: likely | Ctrl+O opens a search box over everything in nearby chests; a hotkey shows where an item is stored; quick stack. Source: [egor-muindor/ValheimMods](https://github.com/egor-muindor/ValheimMods). |
| [RunicStorage (Chazman)](https://thunderstore.io/c/valheim/p/Chazman/RunicStorage/) | see above | Remembers chest contents, so it can search chests that are not loaded, and highlights every matching chest. |
| [CraftSearch (XineladaLendaria)](https://thunderstore.io/c/valheim/p/XineladaLendaria/CraftSearch/) | v2.0.10, 2026-09-27, 1.0: yes | Search inside the open chest, which moves matches to the front of the visible grid, plus the recipe search below. |
| Older | | [Chest Search (QuinnWilton)](https://thunderstore.io/c/valheim/p/QuinnWilton/Chest_Search/) (2021, dead, a chat command that highlights chests). [ChestContents (Sticky)](https://thunderstore.io/c/valheim/p/Sticky/ChestContents/) (2025-05, an index of chest contents). Nexus-only (dates not visible): Search Bar Plus ([3630](https://www.nexusmods.com/valheim/mods/3630)), MixueStack ([3907](https://www.nexusmods.com/valheim/mods/3907)), and hover previews such as Contents Within ([1838](https://www.nexusmods.com/valheim/mods/1838)). |

**Inspiration.**
- There are two separate features:
  - **(a) Filter the open grid.** Prefer dimming or highlighting to hiding or reordering: hiding breaks spatial memory and makes drag-and-drop confusing.
  - **(b) Find across nearby containers.** Only containers in loaded zones have live inventories, so "remember what was inside" (RunicStorage) is the only way to search further.
- **Vanilla 1.0 now has a native search field in the build menu** (`BuildUi`: a `GuiInputField` filtered by localized search terms in `BuildUi.UpdateSearch`). Copy that look and behaviour, including controller focus handling, so our search feels built-in rather than like a Jötunn overlay.

### Search crafting station (QoL)

**Coverage: full.** Several maintained options exist.

| Mod | Status | Notes |
|---|---|---|
| [CraftSearch (XineladaLendaria)](https://thunderstore.io/c/valheim/p/XineladaLendaria/CraftSearch/) | v2.0.10, 2026-09-27, 1.0: yes | Recipe search in inventory, workbench, forge, cauldron and so on. Category filters (Weapons, Armor, Consumables...), `@mod` source search, and quantity crafting that works with craft-from-chest mods. Client-only. Its readme says "network version 39"; the game is now at 40. |
| [AAA Crafting (Azumatt)](https://thunderstore.io/c/valheim/p/Azumatt/AAA_Crafting/) | v2.1.8, updated 2026-09-14, but **flagged deprecated** | Amount-to-craft field, search filter, `!` prefix to search by ingredient, recipe tracking, controller virtual keyboard. |
| [CraftingFilter (cjayride fork)](https://thunderstore.io/c/valheim/p/cjayride/CraftingFilter/) | v1.2.1, 2026-09-09, 1.0: yes ("Updated for Valheim 1.0") | A category filter menu on the crafting tab. A fork of aedenthorn's mod. Source: [cjayride/CraftingFilter_Fork](https://github.com/cjayride/CraftingFilter_Fork). |
| [Sorted Menus (Goldenrevolver)](https://thunderstore.io/c/valheim/p/Goldenrevolver/Sorted_Menus_Cooking_Crafting_and_Skills_Menu/) | v1.3.4, 2024-10-04, 1.0: unknown | Sorts the crafting, cooking and skills lists by configurable criteria. Source: [Goldenrevolver/ValheimSortedMenus](https://github.com/Goldenrevolver/ValheimSortedMenus). |

**Inspiration.**
- **Mirror vanilla.** 1.0 gave the hammer build menu a search field (`BuildUi.UpdateSearch`) but not the `InventoryGui` crafting list, so a mod that looks exactly like the vanilla build search is the most "native" option.
- **Borrow** ingredient search (AAA's `!`), a "craftable now" toggle and category chips.
- **Ours:** one search component shared between crafting, containers and the build menu.
- **Related:** [VNEI (MSchmoecker)](https://thunderstore.io/c/valheim/p/MSchmoecker/VNEI/) (v0.17.6, 2026-09-10) is a full item and recipe browser, useful as a reference for item indexing.

### Notifications stay longer and show multiple (QoL)

**Coverage: partial.** Mods for duration and visible count exist, but none is confirmed on 1.0, and they mostly target the top-left pickup feed.

| Mod | Status | Notes |
|---|---|---|
| [NotificationsOverhaul (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/NotificationsOverhaul/) ([Nexus 3160](https://www.nexusmods.com/valheim/mods/3160)) | v1.0.3, 2026-02-25, 1.0: unknown | Configurable duration, max visible notifications, position, font size and colour. Can apply to pickups only or to everything. About 12K downloads. |
| [BetterPickupNotifications (Pfhoenix)](https://thunderstore.io/c/valheim/p/Pfhoenix/BetterPickupNotifications/) | v1.3.3, 2025-11-15, 1.0: unknown | Reworks how pickup messages are handled so you can see everything you pick up. |
| [StackingNotifications (paddywan)](https://thunderstore.io/c/valheim/p/paddywan/StackingNotifications/) | v1.0.3, 2021-09, dead | A timed notification layer with several stacked notifications at the bottom right. Source: [paddywaan/PaddysVHMods](https://github.com/paddywaan/PaddysVHMods/tree/master/StackingNotifications). |
| [NotificationTweaks (digitiliad)](https://thunderstore.io/c/valheim/p/digitiliad/NotificationTweaks/) | v0.3.2, 2021-04, dead (deprecated) | Number shown at once, fade speed, font size, and merging of similar notifications. |

**Inspiration.**
- **Vanilla `MessageHud`:**
  - Top-left messages go through a queue and are shown **one at a time** on a short timer. Identical consecutive messages are merged by text and icon (`MessageHud.UpdateMessage`).
  - Unlock messages have their own queue.
  - A 50-entry `m_messageLog` exists.
- **Ours:**
  - A stacked feed that shows N messages at once.
  - Merge counts per item ("Wood x37").
  - Show important messages (unlocks, skill-ups) longer than pickups.
  - Optionally add a recallable history panel built from the existing message log.
- This is a small, low-risk mod, and nothing current and maintained is confirmed on 1.0.

### Ping system revamp (QoL)

**Coverage: partial.** World pings and auto-pins exist (the QuickPing revival), and so do ping behaviour tweaks (PingTweaks). No mod offers *typed* contextual pings (enemy, loot, go here, danger), a ping wheel, or a shared in-world marker with distance for everyone.

| Mod | Status | Notes |
|---|---|---|
| [QuickPing, TeamVibes revival](https://thunderstore.io/c/valheim/p/TeamVibes/QuickPing/) ([Nexus 2033](https://www.nexusmods.com/valheim/mods/2033)) | v1.0.5, 2026-09-27, 1.0: yes | Ping what you look at: its name floats in the world and on the minimap. Auto-pins useful things (ores, berries, dungeons, portals...), force-pins, a pin editor (rename, choose icon), and a tracer beam. The [original by Atopy](https://thunderstore.io/c/valheim/p/Atopy/QuickPing/) is deprecated (2024); source [Vodianoi/QuickPingMod](https://github.com/Vodianoi/QuickPingMod). |
| [PingTweaks (Riintouge)](https://thunderstore.io/c/valheim/p/Riintouge/PingTweaks/) | v1.0.7, updated 2026-09-11, 1.0: yes (tagged Deep North) | Pings are private unless you hold a modifier; persistent pings; re-ping to cancel; marker text; distance in metres and elevation; configurable duration and colour; double-click to pin another player's ping. LGPL source: [Terrenteller/Valheim-Plugins](https://github.com/Terrenteller/Valheim-Plugins). |
| [ImRightHere (Vidguy)](https://thunderstore.io/c/valheim/p/Vidguy/ImRightHere/) | v1.0.2, 2026-03-17, 1.0: unknown | Ping your own position with a hotkey; duration, colour, live distance; pin other players' pings. |
| [Hotkey To Ping (shineblind)](https://thunderstore.io/c/valheim/p/shineblind/Hotkey_To_Ping/) ([Nexus 2993](https://www.nexusmods.com/valheim/mods/2993)) | date not checked | Aim and press T to send a vanilla map ping, with a pointing emote. |

**Inspiration.**
- **Vanilla:**
  - A ping is only sent from the map (`Minimap` calls `Chat.SendPing`).
  - It travels through the `ChatMessage` routed RPC as `Talker.Type.Ping`.
  - It is drawn by `Minimap.UpdatePingPins` and as world text.
- **Keep the vanilla RPC** so players without the mod still see a basic ping.
- **Borrow:** QuickPing's "ping what you look at" and PingTweaks' distance, cancel and private options.
- **Ours adds:**
  - Ping types chosen by context (a creature becomes "enemy", an item "loot") or from a small wheel.
  - An in-world marker with distance that everyone with the mod sees.
  - A "follow this target" link for our summons.
- [ExploreTogether](https://thunderstore.io/c/valheim/p/Rolo/ExploreTogether/) (2021, deprecated) first introduced the world-ping key.

---

## Notable mods to study

| Mod | Why |
|---|---|
| [Jötunn](https://github.com/Valheim-Modding/Jotunn) (MIT, v2.30.2, 2026-09-21, 1.0: yes) | The standard Valheim modding library: custom items, pieces, recipes, GUI helpers, input and config sync. Read it even if we decide not to depend on it (several mods above, such as ChebsNecromancy, ArcaneWard and SonicChestFilters, do). |
| [Quick Stack Store Sort Trash Restock](https://github.com/Goldenrevolver/QuickStackStore) (MIT, pushed 2026-09-12) | Mature, 1.0-rebuilt reference for inventory and container manipulation: favourites, multiplayer-safe container access, sort comparators, UI buttons in `InventoryGui`. About 940K downloads. |
| [Cheb's Necromancy](https://github.com/jpw1991/chebs-necromancy) (Unlicense, pushed 2026-09-28) | Best open-source example of custom minion AI, command keys, worker behaviours and custom pieces with Jötunn. Directly relevant to summons and ritual pieces. |
| [blaxxun-boop / Smoothbrain mods](https://github.com/blaxxun-boop/PassivePowers) (PassivePowers pushed 2026-09-11, SmartSkills) | Small, focused, server-syncable mods. This author also maintains the widely used ServerSync / *Manager helper libraries. A good model for our "one feature per mod, config synced" style. |
| [valheim-dps (JereKuusela)](https://github.com/JereKuusela/valheim-dps) (Unlicense, pushed 2026-09-10) | A testing tool as well as a reference: dummy spawning, DPS and stamina meters. Use it while balancing the combat revamps. |
| [Valheim Community Patch (MidnightsFX)](https://github.com/MidnightsFX/Valheim-Community-Patch) (GPL-3.0, pushed 2026-09-27) | Written for 1.0 and Unity 6 from day one. Shows how the 1.0 internals (AI, ZDO) are patched today. GPL: study only, do not copy code. |
| [PingTweaks / Valheim-Plugins (Terrenteller)](https://github.com/Terrenteller/Valheim-Plugins) (LGPL-3.0, pushed 2026-09-27) | Minimal, clean patches of the vanilla ping pipeline (`Chat` and `Minimap`), maintained across every update up to Deep North. |
| [Goo's Combat Overhaul](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) (closed source, v2.1.2, 1.0.16) | The most complete *design* reference for combat in 1.0: per-weapon YAML profiles, hyperarmor modes, running and jump attacks, tower shields. It is also the main mod our combat revamps must coexist with or detect. |

Also worth a look:
- [ArcaneWard](https://github.com/Torvios/ArcaneWards): new magic built only from vanilla assets.
- [ForsakenPowerOverhaul](https://github.com/JuneGame/Valheim.ForsakenPowerOverhaul): a layered power design.
- [StaminaExtended (shudnal)](https://github.com/shudnal/StaminaExtended): clean, config-heavy stat changes.

## Key takeaways

1. **Already well covered, only worth doing inside a bigger integrated module:**
   - Sort chest, sort bags, search chest, search crafting station and the loot filter. A wave of 1.0-era mods appeared in September 2026.
   - Boss powers made passive, and HP/stamina from activity.
2. **Real gaps:**
   - A maintained "crossbow stays loaded" mod: the only precedent is deprecated.
   - A real adrenaline redesign, a tower-shield "wall" stance as a standalone mod, and ballista target assignment.
   - Mob AI unsticking and morale, dummies that fight monsters, multi-player rituals.
   - Typed and contextual pings, and a notification feed confirmed on 1.0.
3. **Design lever.** Trinkets, adrenaline, boss powers and the weapon signature moves can share one resource. Nobody has built them as one system; every existing mod treats them separately.
