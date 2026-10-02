# Tower Shield Wall — design

| | |
|---|---|
| Mod | Tower Shield Wall |
| GUID / project | `MC.Combat.Shields.TowerWall` (`src/Combat/Shields.TowerWall/`, root namespace `MC.Combat.ShieldsTowerWallMod`, package `ShieldsTowerWall`) |
| Category / scope | Combat / Revamp |
| Side | Both: the server (or host) and every player install it (user rule for mods that change the experience); multiplayer Compatible; network version 2 (own RPCs `<guid>.Settings` / `<guid>.SettingsRequest`, rules layout 2, plus the framework's `<guid>.Hello` / `<guid>.HelloAck` / `<guid>.HelloState`); no ZDO keys |
| Sheet idea | `Better tower shields` (`<ModIdea>` in the csproj) |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`), decompiled `assembly_valheim` in `.ref/`; item, animator, creature and status-effect data dumped at runtime from a real world (Debug DataDump self-test, 2026-09-29); other shield mods' sources and pages read on 2026-09-29 (CaptainValheim, SecondaryAttacks, ReliableBlock and ReliableBlockRebuilt, Combat Adjustments, WeaponArts, ShieldMeBruh on GitHub; Goo's Combat Overhaul, ZenCombat, ShieldBash, Make Tower Shields Great Again on Thunderstore); design reviewed against `.ref` on 2026-09-30, and aligned the same day with this run's shared changes (`NetworkGate` peer state, `ItemKinds` shield rule); the user's feedback after the first in-game test (build `3cda33b+dirty`) applied on 2026-09-30, with the runtime facts of that day's in-world run (all self-tests passed; their NOTE lines) |
| Status | Implemented (v0.1.0 code), in-game testing; bash reworked after the first in-game test (decisions 30-34, stage 8 of section 10), then reviewed (decision 35, stage 9). Section 10 holds the implementation notes; its short list of deviations closes the section |

## Goal

The user's words. Sheet: "Make them usable. I see them as an immovable wall that can't parry: the player should be
slowed A LOT but should be able to block virtually anything. Idea: make them TWO HANDED, very slow, with a small shield
bash attack that has heavy stagger numbers but low damage number, and a lot of block armor." Chat: "more tanky,
focused on defense, two handed" and "I think the animation for shield bash can be the same as the parry animation
with a standard shield but to be tested." After the first in-game test (2026-09-30): "Shield bash is too fast,
cooldown is too short.", "Proccing the stagger should not be easier than with a buckler, so maybe increase the
stamina consumption on the shield bash." and "ShieldUp and ShieldPunch seem to be the same animation but also seem to
be the best looking. Is there a way to play them slower?"

Expected behaviours (each is scope and has at least one test in section 8):

1. **G1 Two-handed.** Equipping a tower shield empties both hands. Equipping a one-handed weapon, a torch, another
   shield, a bow, a two-handed weapon or a tool while a tower shield is held puts the tower shield away. The shield
   keeps its shield look: left hand, shield slot on the back, shield slot on armor stands. The tooltip says
   "Two-handed".
2. **G2 Very slow.** A tower shield in hand slows the player a lot (default: jog -30%, sprint -45%, against -10% and
   -15% in vanilla); sprinting never becomes slower than jogging. Put away on the back it does not slow (vanilla rule
   for every item). Bracing (holding block with a tower shield) slows further (default -30% more, on every ground speed
   and on turning), so a braced wall advances at about half the vanilla jog speed.
3. **G3 Cannot parry.** A block with a tower shield is never a parry: no parry bonus, no parry stagger of the
   attacker, no parry adrenaline, no parry effect. The tooltip says so.
4. **G4 A lot of block armor.** Block armor is multiplied (default ×2.5, per quality level too). In the vanilla block
   formula this cuts, per blocked hit, the damage that gets through, the stamina, the stagger and the knockback. With
   G2 and G6 this is the "tanky, defense first" role from the chat.
5. **G5 An immovable wall that blocks virtually anything from the front, while stamina lasts.** While bracing:
   (a) frontal enemy attacks that the game marks unblockable (area and spray attacks such as the Blob's and the
   Greydwarf Shaman's poison) are blocked too; (b) as long as the bearer has the stamina for one more full block (10 by
   default), stagger is strongly reduced (default -80%), so block-time stagger no longer drops the guard; below that,
   the guard breaks the vanilla way; (c) under the same condition, hits and area knockback from the front do not push
   the bearer back (default: no push at all). Hits from behind and a few damage kinds stay unblockable (2.5).
6. **G6 Shield bash.** The normal attack button bashes with the tower shield: a single strike with low damage (flat
   blunt per tower, about a fifth of a weapon of the same tier) and heavy stagger (×25 of its blunt, applied before
   the target's armor: a tower staggers the standard creatures of its tier in one bash, like a mace's secondary
   attack), scaled and trained by the Blocking skill. It is a deliberate move, not a spam: a bash costs 20 stamina, so
   a bash stagger is not easier to get than a stagger by a buckler parry (it costs what a player who lands one parry
   in three pays, decision 31), like a parry one bash staggers one creature at most, also in a group (the one nearest
   the middle of the swing, decision 35), and a new bash can start at most every 2 s (decision 30). One player cannot
   keep a creature staggered: after a bash staggers a creature, bashes add no heavy stagger to it for 8 s.
7. **G7 Bash animation, played slower.** Vanilla has no parry animation (1.10): a parry is the ordinary block pose
   plus effects. The bash plays the shield-arm punch (`ShieldPunch`, default) at 0.6 of its speed
   (`BashAnimationSpeed`): the whole swing, its hit included, takes longer, and every player sees the same speed
   (decision 32). The first variant, "the block pose held during the punch" (`ShieldUp`), looked the same as the plain
   punch in the first test and in the runtime data: the pose never shows during the swing, so it is gone (decision
   33). The animation is a setting so variants can be tried in game: `ShieldPunch` (default), `OtherPunch`, `Kick`,
   `Custom` (a player attack animation, checked at runtime); the mod falls back on its own (with a log warning) when an
   animation cannot land its hit or does not end. In multiplayer the server's choice applies to everyone, so variants
   are tried in single player or by the host.
8. **G8 Required everywhere, server settings for everyone** (user rule for mods that change the experience): the
   server refuses players without the mod, with the mod turned off on their game, or with a version of the mod that
   cannot talk to the server's (unless `AllowPlayersWithoutMod`), and the server's gameplay settings apply to every
   player.
9. **G9 Live toggle** (MC rule): the mod can be turned off on its own, live; off = vanilla tower shields at once, for
   items in hand, in the inventory, in loaded chests and on the ground.

Added beyond the request (small):

- Tooltip lines: "Cannot parry", the bash's stagger multiplier and cooldown, and what bracing does.
- A HUD status icon "Braced" while bracing, which flashes and reads "Braced (exhausted)" when stamina is too low for the
  brace to hold.
- The sprint floor of G2 (sprinting never slower than jogging), so heavy armor plus a tower shield cannot make sprinting
  pointless or negative.
- A `BlockForcePercent` setting for the block push on attackers, because that push decides whether attackers stay in
  bash range and needs tuning in game.
- A warning in the log when another mod that changes shields or blocking is installed (6.3).

Non-goals: new models, animations or sounds; a separate bash key; a secondary attack on tower shields; shield throw,
charge, taunt or projectile reflection; changes to round shields and bucklers; the creature copies
`FW_ShieldBlackmetalTower` / `SP_ShieldBlackmetalTower`, and any tower a creature carries; blocking from behind;
parrying with tower shields; changes to the Blocking skill itself; PvP-only rules.

Later (not in v1):

- **L1 Custom bash clip:** a real shield-shove animation from an AssetBundle through an AnimatorOverrideController on
  every client, plus its own remote sync (ShieldBash needed one).
- **L2 "Block pose pulse" bash:** raise the shield with the `blocking` animator bool for ~0.3 s and fire the hit from
  code (`Attack.StartWithoutAnimation` after a delay), with own stamina, cooldown and hit timing. It shows no motion
  when bashing out of guard (the pose is already up), which is why v1 uses a real attack clip.
- **L3 Secondary attack:** the vanilla kick (a clone of the unarmed `m_secondaryAttack`) or a short shield charge.
- **L4 AoE launch while braced:** a prefix on `Character.ForceJump` on the bearer's game for frontal AoE launches
  (`Aoe.OnHit` throws the character before any damage). Area knockback is already covered by G5 (c).
- **L5 Wider block arc** for tower shields (vanilla blocks the front half-space).
- **L6 Hits with stagger ×100 or more** (they stagger before the block check; only PvP block-charge counters reach it).
- **L7 Front-only stagger resistance** (scope opened in `Humanoid.BlockAttack`, closed at the end of
  `Character.RPC_Damage`), if the all-direction resistance of v1 proves too strong.
- L8 (sprint floor) moved into v1 (2.2).
- **L9 Modded tower shields by heuristic** (item type Shield and parry bonus ≤ 1.01), off by default; v1 has a list.
- **L10 Shield-wall aura:** allies standing behind a braced player take less damage.
- **L11 Bash damage that grows with quality** (`m_damagesPerLevel`).
- **L12 Exact brace reserve:** compute the stamina cost of the incoming block (armor, block fraction, status effects)
  instead of reserving one full block (2.5 b).

## 1. Vanilla behaviour

Only what the design relies on. Line numbers are in `.ref/decompiled/assembly_valheim/<Class>.cs`; *(dump)* = the
runtime data dump of 1.0.16.

### 1.1 Tower shield data *(dump)*

Every shield has `m_itemType = Shield`, `m_animationState = Shield`, `m_skillType = Blocking`,
`m_attachOverride = None`, `m_maxQuality = 3`, `m_blockPowerPerLevel = 6`, `m_deflectionForcePerLevel = 5`,
`m_useDurabilityDrain = 1`, `m_buildBlockCharges = False`. No other item has the Shield animation state or the
Blocking skill (24 shield items in the dump, all with both).

| Prefab | Block armor q1 | Block force q1 | `m_timedBlockBonus` | `m_perfectBlockAdrenaline` | `m_movementModifier` | `m_damages` | `m_attackForce` | blockable / dodgeable | backstab |
|---|---|---|---|---|---|---|---|---|---|
| `ShieldWoodTower` | 10 | 100 | 0 | 0 | -0.1 | 10 true (`m_damage`) | 40 | False / False | 1 |
| `ShieldBoneTower` | 32 | 100 | 0 | 0 | -0.1 | 10 true | 40 | False / False | 1 |
| `ShieldIronTower` | 52 | 100 | 0 | 0 | -0.1 | 10 true | 40 | False / False | 1 |
| `ShieldSerpentscale` | 60 | 100 | 0 | 0 | -0.1 | 10 blunt | 50 | True / True | 4 |
| `ShieldBlackmetalTower` | 104 | 150 | 0 | 0 | -0.1 | 10 true | 50 | False / False | 1 |
| `ShieldFlametalTower` | 140 | 150 | 0 | 0 | -0.1 | 10 true | 50 | False / False | 1 |
| `ShieldGoldTower` (Nord Greatshield) | 158 | 150 | 0 | **15** | -0.1 | 10 true | 50 | False / False | 1 |

- Vanilla towers already cannot parry (`m_timedBlockBonus` 0), but the Gold tower's tooltip still prints
  "Parry adrenaline: 15" (`ItemData.AddBlockTooltip`, ItemDrop.cs:821, prints it whenever it is above 0). Every tower
  has `m_blockAdrenaline` 2.
- Their hidden `m_attack` has an empty `m_attackAnimation` (so `ItemData.HavePrimaryAttack`, ItemDrop.cs:600, is
  false) and `m_staggerMultiplier = 1000`: it is the unused block-charge counter (1.4 step 9).
- Round shields: block armor 6-132, `m_timedBlockBonus` 1.5 (bucklers 2.5), movement -0.05. A tier's tower has only
  1.2-1.35× the round shield's armor, while a round-shield parry multiplies by 1.5: that is why towers feel pointless.
- Bucklers *(dump)*: `ShieldBronzeBuckler` 16, `ShieldIronBuckler` 28, `ShieldCarapaceBuckler` 78, `ShieldGoldBuckler`
  88 block armor at quality 1, `m_timedBlockBonus` 2.5, `m_perfectBlockStaminaRegen` 0, `m_perfectBlockAdrenaline` 5,
  `m_blockAdrenaline` 1. Creature attacks for the parry comparison *(dump)*: `draugr_sword` 58 slash, `draugr_axe` 48
  slash, `troll_punch` 60 blunt.
- `ShieldIronSquare` (block 35, `m_timedBlockBonus` 1, movement -0.2) is not a tower by name; whether a player can get
  it is unverified. The creature copies `FW_ShieldBlackmetalTower` / `SP_ShieldBlackmetalTower` are separate prefabs
  (carried by `FallenWarrior` and `ShadowPerson`).
- Player prefab *(dump)*: `m_speed` 4 (jog), `m_runSpeed` 7, `m_walkSpeed` 1.6, `m_crouchSpeed` 2, `m_turnSpeed` 300,
  `m_staggerDamageFactor` 0.4, `m_blockStaminaDrain` 10, **`m_perfectBlockStaminaDrain` 0**, `m_unarmedWeapon` =
  `PlayerUnarmed`. Stamina regen (code): `m_staminaRegen` 5/s, up to twice that when low, after `m_staminaRegenDelay`
  1 s, none while attacking (`Player.UpdateStats`, Player.cs:2100-2115).
- `PlayerUnarmed` *(dump)*: primary `unarmed_attack`, 2 chain levels (fires `unarmed_attack0`, `unarmed_attack1`),
  Horizontal, stamina 4, `m_speedFactor` 0.1, range 1.5, angle 25, ray width 0.2, stagger ×1, `m_multiHit` True,
  `m_lowerDamagePerHit` True, `m_skillHitType` Character; secondary `unarmed_kick`, stamina 8, stagger ×6, force ×3,
  range 1.6.
- Heavy vanilla attacks for comparison *(dump; stagger = damage × multipliers × mean skill factor 0.4 at skill 0, before
  the target's armor)*: Iron mace secondary 55 blunt × 2.5 damage × 2 stagger → 110 (20 stamina; clip `MaceAltAttack`,
  hit about 1.14 s after the press, about 2 s to its end from its Speed events); Iron atgeir secondary 65 pierce × 6 →
  156 around the player (28 stamina); Iron sledge 55 × 2 → 44 in an area (20 stamina; `Sledge-Attack1`, hit about
  1.16 s, end about 1.5 s); unarmed kick 5 × 6 → 12 (8 stamina).

### 1.2 Hands, equipping and look

- `Humanoid.EquipItem` (Humanoid.cs:1055) refuses while in an attack or dodge, then branches on
  `item.m_shared.m_itemType`. **TwoHandedWeaponLeft** (1191-1202): unequip left and right, the item goes to the left
  hand. OneHandedWeapon (1130-1151) unequips a left item that is not Shield or Torch; Torch (1104-1129) unequips a left
  item that is not Shield; Shield (1152-1166) replaces the left item; Bow, TwoHandedWeapon and Tool empty both hands.
  Vanilla precedent: the blood staffs `StaffSkeleton`, `StaffFrostOrbs`, `StaffSpiritCaller` are TwoHandedWeaponLeft
  *(dump)*; crossbows, `AxeBerzerkr*` and `GrapplingHook` use `m_attachOverride = Shield` for the back slot *(dump)*.
- `Humanoid.UnequipItem` (1274): first clears a matching hidden hand item and calls `SetupVisEquipment` (1276-1288),
  then returns if the item is not equipped; for an equipped `IsWeapon()` item it does `m_currentAttack.Stop()`,
  `m_previousAttack = m_currentAttack`, `m_currentAttack = null` when the attack uses the item (1294-1301), and calls
  `ResetLoadedWeapon()`. `ItemData.IsWeapon` (ItemDrop.cs:582) and `IsTwoHanded` (591) include TwoHandedWeaponLeft.
- `Humanoid.SetupAnimationState` (1449): a left item's `m_animationState` wins, so a tower in the left hand keeps the
  Shield stance whatever its item type.
- `Humanoid.GetCurrentWeapon` (475): the right item if `IsWeapon()`, else **a left item that `IsWeapon()` and is not
  a torch**, else the unarmed weapon. `Humanoid.GetCurrentBlocker` (492): the left item first. So a TwoHandedWeaponLeft
  tower is both the current weapon and the blocker, natively.
- `VisEquipment.AttachBackItem` (VisEquipment.cs:968) reads the **ObjectDB prefab** and switches on
  `m_attachOverride` when set, else `m_itemType`: Shield → `m_backShield`, TwoHandedWeaponLeft →
  `m_backTwohandedMelee`. `ArmorStand.CanAttach` (ArmorStand.cs:470) also honours `m_attachOverride`.
- `ItemStand.CanAttach` (ItemStand.cs:500-519): the stand needs an attach child for the item's prefab, refuses items
  in `m_unsupportedItems`, then **accepts any item when its `m_supportedItems` list is empty** (`IsSupported`,
  533-537) or names the item; only a stand with a non-empty list that does not name the item falls back to the
  `m_supportedTypes` test on `m_itemType`. So generic item stands accept a tower whatever its item type.
- `Humanoid.Pickup` (608, auto-equip at 654): auto-equips a picked-up item that `IsWeapon()` when the right hand is
  empty and the left item is not two-handed. Vanilla never auto-equips shields.
- Creatures: `Humanoid.GiveDefaultItem` (247-254) gets the item through `PickupPrefab` (576: `Instantiate` + `Pickup`
  with auto-equip off) and equips it only when it is **not** `IsWeapon()`; weapons are chosen by
  `EquipBestWeapon` (670), which takes `IsWeapon()` items.
- `Player.EquipInventoryItems` (Player.cs:4998) re-equips the saved equipped items in inventory order;
  `Humanoid.HideHandItems` / `ShowHandItems` (1938-1990) re-equip left, then right.
- `ObjectDB.GetAllCraftableWeapons` (ObjectDB.cs:154) lists and **caches** recipes whose item `IsWeapon()`; it feeds
  the "craft every weapon" achievement (Achievements.cs:462) and `PlayerProfile` craft progress (917).

### 1.3 Attacks

- `Humanoid.StartAttack` (280): refuses while `(InAttack && !HaveQueuedChain) || InDodge || !CanMove ||
  IsKnockedBack || IsStaggering || InMinorAction`, and when the current weapon has no primary animation. It does
  **not** check blocking. It stops and moves any current attack to `m_previousAttack`, clones
  `currentWeapon.m_shared.m_attack` (`Attack.Clone` = `MemberwiseClone`, Attack.cs:1719) and calls `Attack.Start`.
  `Player.PlayerAttackInput` (Player.cs:1805) buffers a press 0.5 s and calls it every FixedUpdate while attack is
  held (1831).
- `Attack.Start(…, ItemDrop.ItemData weapon, …)` (Attack.cs:356): empty animation → false; sets `m_weapon = weapon`
  (368; before that the clone's `m_weapon` is the template's, i.e. null); stamina gate `HaveStamina(cost + 0.1)`
  (387-395); trigger (410-431): `m_attackChainLevels > 1` fires `name + chainLevel`, where the level continues only
  when the previous attack had the same animation and ended at most 0.2 s ago, else 0; `m_attackRandomAnimations >= 2`
  fires `name + random`; **else it fires `m_attackAnimation` as is**. The trigger goes through
  `ZSyncAnimation.SetTrigger` (sent to everybody). So vanilla fires `unarmed_attack1` only right after
  `unarmed_attack0`.
- `Attack.Update` (516): on the first frame in an `attack`-tagged state it spends `GetAttackStamina()` (468:
  `m_attackStamina` × equipment modifier, SE modifiers, **-33% × skill factor of the weapon's `m_skillType`**); a
  stagger aborts it (`m_abortAttack`); leaving the attack tag calls `Stop()`. With 0 chain levels
  `CanStartChainAttack` (1729) is false, so the next attack waits for the clip to end. **A trigger that leads to no
  `attack`-tagged state** never pays stamina and never stops; `StartAttack` refuses only while `InAttack`, so a held
  button starts it again every FixedUpdate, each time with a new trigger RPC. Vanilla never calls `ResetTrigger`, so an
  unconsumed trigger stays set on every animator that received it.
- **The hit**: the clip's Hit event → `Humanoid.OnAttackTrigger` (553-559) → `m_currentAttack.OnAttackTrigger()`
  whenever `m_currentAttack != null` and a current weapon exists. `Attack.OnAttackTrigger` (607) checks ammo and
  stagger, not `m_attackDone`; `Attack.Stop` (563-596) only sets flags. So **a stopped attack still hits** unless
  `m_currentAttack` is cleared, as `UnequipItem` and `StartAttack` do. A clip with two Hit events hits twice.
- `Attack.DoMeleeAttack` (1240; HitData at 1392-1415): from the **Attack clone**: geometry (`m_attackRange`,
  `m_attackAngle`, `m_attackRayWidth`, heights), `m_staggerMultiplier` → `HitData.m_staggerMultiplier`,
  `m_forceMultiplier`, `m_multiHit` / `m_lowerDamagePerHit` (damage ÷ targets × 0.75), `m_attackAdrenaline`,
  `m_raiseSkillAmount`. From the **weapon's SharedData**: `GetDamage()`, `m_attackForce`, `m_skillType` (→
  `HitData.m_skill`), `m_blockable`, `m_dodgeable`, `m_backstabBonus`, hit/trigger/start effects, durability drain
  (1479: -1 per hitting swing). The last chain step gets damage ×2 only when `m_attackChainLevels > 1` (1417-1421).
  The skill of every hit target type in `m_skillHitType` is raised, ×1.5 when a character was hit (1491): a Training
  Dummy is a character, so hitting it trains the weapon's skill.
- `ItemData.GetDamage(int quality, float worldLevel)` (ItemDrop.cs:695-706): in an NG+ world it adds
  `worldLevel × Game.m_worldLevelGearBaseDamage` (120 in code, Game.cs:174) spread over the item's damage types in
  proportion (`HitData.DamageTypes.IncreaseEqually`, HitData.cs:381): a blunt-only item gets it all as blunt. Items
  created in an NG+ world carry its level (Inventory.cs:1001/1038/1081, ItemDrop.cs:1332). Block power has no
  world-level term (`GetBaseBlockPower`, 714).
- `Skills.GetRandomSkillFactor` (Skills.cs:188): `lerp(0.4, 1, skill) ± 0.15` → 0.25-0.55 at skill 0 (mean 0.4),
  0.55-0.85 at 50 (0.7), 0.85-1.0 at 100 (0.925). It scales damage and push.
- `HitData` serializes the stagger multiplier, `m_skill`, the hit type and the attacker (HitData.cs:740-860), so the
  victim's owner sees them.

### 1.4 Blocking

- `Humanoid.IsBlocking` (1878): remote copies read ZDO `IsBlocking`; the owner computes `m_blocking && !InAttack &&
  !InDodge && !InPlaceMode && !IsEncumbered && !InMinorAction && !IsStaggering`. `m_blocking` (Character.cs:332) is set
  by `Player.SetControls` from the block key.
- `Humanoid.UpdateBlock` (1891, owner, every FixedUpdate from `CustomFixedUpdate`, 256-269): writes the private
  `m_internalBlockingState`, ZDO `IsBlocking` and the synced animator bool `blocking` **only on its edges**.
- `Humanoid.BlockAttack` (1751), in order:
  1. `Dot(hit.m_dir, forward) > 0` → not blocked (the front half-space is covered).
  2. Parry flag = `m_timedBlockBonus > 1 && m_blockTimer` in [0, 0.25) (1762).
  3. Block power = `GetBlockPower(skillFactor(Blocking))` = base × (1 + 0.5 × skill) (ItemDrop.cs:724), where base =
     `m_blockPower + (q - 1) × m_blockPowerPerLevel` (714).
  4. The blocker's damage modifiers apply to the hit; `damage.ApplyArmor(blockPower)`
     (HitData.cs:414: `ac < dmg/2 → dmg - ac`, else `dmg × clamp01(dmg / 4ac)`, on the sum of blockable types:
     blunt, slash, pierce, fire, frost, lightning, poison, spirit; not true, chop or pickaxe damage, 313).
  5. Stamina = `m_blockStaminaDrain × clamp01(blocked / blockPower)` (+ equipment and SE modifiers), **spent first**
     (1787). **A parry spends `m_perfectBlockStaminaDrain` instead** (1782), and spends it again after a held parry
     unless the shield has `m_perfectBlockStaminaRegen` (1833-1851); both are 0 on the Player prefab and on every
     buckler in 1.0.16 (1.1), so **a parry costs no stamina**.
  6. `AddStaggerDamage(residual blunt+slash+pierce+lightning, dir, null)`.
  7. **Only if `HaveStamina() && !staggered`** (1795; `Player.HaveStamina` = stamina > 0, Player.cs:4668) the hit is
     reduced (`hit.BlockDamage`) and its status effect cleared; otherwise the whole hit lands (1796-1802).
  8. Durability `-= m_useDurabilityDrain × totalBlockable / blockPower`; Blocking skill +1 (+2 on a parry).
  9. If `m_buildBlockCharges`: every `m_maxBlockCharges` blocks it fires `m_shared.m_attack.StartWithoutAnimation`
     on the shared Attack (1810-1820): no animation, no stamina.
  10. `m_blockAdrenaline` when not a parry; parry adrenaline, parry effect and **the attacker's stagger** only on a
      held parry (1821-1860). The parry staggers the attacker outright (`attacker.Stagger`, whatever its stagger bar)
      when it has `m_staggerWhenBlocked` (145 of the 162 creature entries in the dump, among them Greydwarf, Skeleton,
      Draugr and Troll).
  11. If held: `hit.m_pushForce *= fraction` (the blocker is still pushed, less); against a non-ranged attacker, a
      push of `GetDeflectionForce() × (1 - clamp01(fraction × 0.5))` (1861-1874): **the smaller the blocked fraction,
      the harder the attacker is pushed** (from 0.5× to 1× the block force).
- After any stamina use, regen waits `m_staminaRegenDelay` = 1 s (Player.cs:210, 4664) and runs ×0.8 while blocking
  (`Player.UpdateStats`, 2100-2115). So after a guard break a sliver of stamina comes back within about a second.
- **What a buckler parry stagger costs** (the yardstick of decision 31): no stamina when it succeeds (step 5), but it
  needs the enemy to swing and the shield raised at most 0.25 s before the hit lands (`m_blockTimer < 0.25`, 1762).
  Raised earlier, it is a normal block with the buckler's small armor: the Iron buckler (28 at Blocking 0) against a
  Draugr's sword (58 slash) blocks 28, lets 30 through (and 30 stagger onto the blocker) and costs the full 10 stamina;
  raised too late, the whole hit lands. So a player who lands one parry in two pays about 10 stamina per parry stagger,
  one in three about 20, plus the damage of the missed ones.
- `Humanoid.CheckRun` (1729): no running while blocking. `Player.SetControls` (6689): jump while blocking = dodge
  backwards.
- `Character.ApplyPushback(Vector3, float)` (Character.cs:1909-1921): scales the push by
  `clamp01(1 + equipment movement modifier)` and keeps the stronger push; `IsKnockedBack` (3932) = a push is running,
  and `StartAttack` refuses while knocked back.
- **There is no block or parry animation call anywhere in `BlockAttack`** (1.10).

### 1.5 What never reaches the block

`Character.RPC_Damage` (Character.cs:2241, on the victim's owner): `m_staggerMultiplier >= 100` staggers before
anything (2261-2264); dodge i-frames; PvP filter; **enemy hits on a player are scaled by
`GetDifficultyDamageScalePlayer` (1 + 0.04 per extra player nearby) and `Game.m_enemyDamageRate` (world modifier)**
(2275-2280); backstab; a staggered non-player takes ×2 (2342-2346); then
**`if (hit.m_blockable && IsBlocking()) BlockAttack(...)`** (2347-2354); push (`ApplyPushback(hit)`); status effect;
resistances; body armor (players) or NG+ enemy armor (2386-2389); fire, spirit and poison split off as DoTs;
`ApplyDamage`. A creature's attack is also scaled by its star level. **Worked numbers in this doc are single player,
0-star attacker, default world modifiers, world level 0.**

- `HitData.m_blockable` defaults to false. Only `Attack` (weapon `m_blockable`), `Projectile`, `Aoe` (prefab
  `m_blockable`), `Catapult` and `Turret` set it. Unblockable attack items *(dump)* include `blob_attack_aoe`
  (Area, poison 90), `Greydwarf_shaman_attack` (spray, poison 30), the bonemass, hive, Fader, Fenring flame/frost,
  gjall and Frozen King AoEs, and player staffs.
- Hit types (`HitData.HitType`, HitData.cs:80): attacks from `Attack`, `Projectile` and `Aoe` are `EnemyHit` or
  `PlayerHit` (Attack.cs:971/1136/1411, Aoe.cs:711; an Aoe may override). Fall (`Character.cs:2808`, direction = ground
  normal), drowning, burning, poison, smoke, water, lava, CinderFire (`Fire.cs:158`), impact, structural, self and the
  rest are other types.
- `Aoe.OnHit` (Aoe.cs:665-719): `m_launchCharacters` → `ForceJump` before damage (672), and `m_knockBackForce` →
  `ApplyPushback` after it (715-718), both outside the block path. Aoe hits are `m_ranged` (no deflection push).
  Area-attack hits (`Attack.DoAreaAttack`) use a horizontal direction from the attack origin (Attack.cs:1110-1117).

### 1.6 Stagger

- `Character.AddStaggerDamage` (2613): nothing if `m_staggerDamageFactor <= 0`; **`SEMan.ModifyStagger`** (every
  `SE_Stats` adds `base × m_staggerModifier`, SE_Stats.cs:402; -1 = no stagger); at the threshold
  `max HP × m_staggerDamageFactor` **the bar is set to the threshold** (2624), the character staggers, and the attacker
  gets `m_staggerEnemyAdrenaline` when `hit` is set. `RPC_Stagger` (2673) ignores a new stagger while staggering, but
  the bar stays full. The bar drains threshold/5 per second (`UpdateStagger`, 2650): **5 s from full to empty**. So
  hits landing during or right after a stagger re-stagger the creature as soon as it recovers.
- `Character.ApplyDamage` (2417): stagger = `(blunt + slash + pierce + lightning) × hit.m_staggerMultiplier` of the
  **damage left after everything the victim's owner applies** (2468-2469): resistances, NG+ enemy armor
  `ApplyArmor(worldLevel × 100)` (2386-2389, where a small hit almost vanishes: `dmg² / 4ac`),
  `GetDifficultyDamageScaleEnemy` = 1 / (1 + 0.3 × (players nearby − 1)) and `Game.m_playerDamageRate` (2424-2428).
  With 0.1 or less left it returns **before** the stagger step (2436-2442). Thresholds are not scaled by player count,
  but NG+ multiplies creature health, so thresholds, by `worldLevel × 2` (`GetMaxHealthBase`, 3079-3087).
- Thresholds at 0 stars, world level 0 *(dump)*: Greyling 6, Greydwarf 12, Skeleton 20, Draugr 50, Goblin 52.5,
  Seeker 60, Draugr_Elite 100, Fenring 150, Troll 180, GoblinBrute 240, Charred_Melee 300, Lox 300; bosses, `Blob`,
  `TrainingDummy`, deer and hares have factor 0 (never staggered by damage). Player: 0.4 × max HP.
- `Character.Stagger` / `RPC_Stagger` (2663-2685) fire the synced `stagger` trigger; `IsStaggering` = animator tag.

### 1.7 Movement

- `Player.UpdateModifiers` (Player.cs:2548-2590) sums `m_movementModifier` (and 10 other modifiers) over the
  **items in hand** (`m_rightItem`, `m_leftItem`), armor, utility and trinket, from each item's **own SharedData**,
  every stats update. Items put away (hidden hand items) do not count.
- `Player.GetJogSpeedFactor` (7108) = `1 + mod`; `GetRunSpeedFactor` (7113) = `(1 + 0.25 × runSkill) × (1 + 1.5 × mod)`.
- `Character.UpdateWalking` (1602): jog = `m_speed × jog factor`; walk = `m_walkSpeed` and crouch = `m_crouchSpeed`
  **unscaled**; run = `m_runSpeed × run factor` (**not clamped**); then `× GetAttackSpeedFactorMovement()` and
  **`SEMan.ApplyStatusEffectSpeedMods`**, which also scales the turn speed (1815-1819). `SE_Stats.ModifySpeed`
  (SE_Stats.cs:436) adds `base × m_speedModifier` and clamps at 0.
- The equipment movement modifier also raises run, jump and dodge stamina (`Player.CheckRun`, `OnJump`, dodge) and
  scales knockback taken by `clamp01(1 + mod)` (`Character.ApplyPushback`).
- Speeds with the player prefab (run skill 0): mod -0.1 → jog 3.6, run 5.95; -0.3 → 2.8, 3.85; -0.4 → 2.4, 2.8;
  -0.46 → run = jog; -0.667 → run 0; below that run is negative.

### 1.8 Status effects

`SEMan.AddStatusEffect(StatusEffect)` (SEMan.cs:184-212) clones any instance (`MemberwiseClone`), keys it by
`NameHash()` (from the object name) and logs an analytics event for players on every add; no ObjectDB registration is
needed for a local add. `HaveStatusEffect(int)` is a hash-set lookup. The HUD lists only effects with an icon and
`!m_hidden` (`GetHUDStatusEffects`, 328), and `Hud.UpdateStatusEffects` (Hud.cs:1635) reads the name, icon and
`m_flashIcon` every frame. `Humanoid.UpdateEquipmentStatusEffects` (1483) adds and removes `m_equipStatusEffect` of
equipped items.

### 1.9 SharedData is a copy per item object

In a build, `ItemDrop.Awake` (ItemDrop.cs:1257) and `Humanoid.EquipItem` relink `m_shared` to the prefab's only in
the editor. Every `Object.Instantiate` of an item prefab (inventory load, crafting, chests, spawned drops, creature
default items; Inventory.cs:984) deep-copies the SharedData; `ItemData.Clone()` shares it. Verified at runtime by MC
Forge Idol Upgrades. So an `ObjectDB` edit reaches items created later, not items already loaded, and every reader
above (type, attack, block power, movement modifier, damages) reads **the item's own copy**;
`VisEquipment.AttachBackItem` is the one reader of the prefab.

The first `ObjectDB` pass with items happens in the **main menu**: `FejdStartup.Start` → `SetupObjectDB`
(FejdStartup.cs:428-431, 855-860) adds an `ObjectDB` (its `Awake` sees no items) and calls `CopyOtherDB`, before any
world scene and any server sync. Item prefabs are shared by every later `ObjectDB`.

### 1.10 Animation: there is no parry animation

- The only shield motion is the block pose: the synced animator bool `blocking` (Humanoid.cs:145, written in
  `UpdateBlock`), with pose clips `Block idle` and `Block Shield Walk/Jog …` that have no animation events *(dump)*.
- A parry is the same pose. `BlockAttack` calls no animator method on the blocker; a held parry only creates
  `m_perfectBlockEffect` (VFX/SFX) and staggers the **attacker** (`stagger` trigger). The player animator's 150+
  parameters have no parry, bash or shove parameter, and no clip name contains "shield" or "bash" *(dump)*.
- What a shield and an empty right hand do today: `GetCurrentWeapon` returns the unarmed weapon, so the attack button
  fires `unarmed_attack0` (and `unarmed_attack1` as the second step of the combo) **in the Shield stance**, and the
  secondary fires `unarmed_kick`. `unarmed_attack1` also plays when fired on its own, outside the combo *(in-world
  run, 2026-09-30)*. The dump lists the animator's layers (`Base Layer`, `upperbody`), parameters and clips, not its
  states or transitions. `IsStaggering` and `InAttack` read the base layer's state tags.
- Clips *(dump; the trigger-to-clip mapping is from the in-world run of 2026-09-30; which hand strikes is unverified)*:
  `unarmed_attack0` plays `Punchstep 1` 1.17 s (`Speed@0:2`, `Hit@0.84`, `Speed@0.92:1`, `Chain@1.17`): hit 0.46 s
  after the press, swing over 0.78 s; `unarmed_attack1` plays `Punchstep 2` 1.20 s (`Speed@0:2`, `Hit@0.90`,
  `Chain@0.99`): hit 0.5 s, swing over 0.74 s; `unarmed_kick` plays `Kickstep` 1.83 s (1× until 0.46, 2× until 0.58,
  then 1×; `Hit@0.59`): hit 0.48 s, swing over 1.5 s (measured times include the transition and the 0.15 s hit-stop).
  `DualAxes Attack 3 2` and `DualAxes Attack 4` have two Hit events each. The wiki says the second punch of the fist
  combo is the left hand.
- **The block pose does not show during an attack clip** *(in-world run, 2026-09-30)*: with the `blocking` bool forced
  on during the swing (the former `ShieldUp` option) and without it (`ShieldPunch`), the animator played the same
  state (`Punchstep 2` on the base layer, no clip on the upperbody layer 0.2 s after the press), the hit came at the
  same time (0.5 s) and the swing ended at the same time (0.742 / 0.744 s); the two mid-swing screenshots show the
  shield in the same place. Neither layer played a block pose clip during the swing, so the bool has no visible effect
  while the attack state plays; it could only show for a moment after the swing if the bool were still on then.
- **Animation speed.** Clips set their own speed: a `Speed` animation event calls `CharacterAnimEvent.Speed(float)`
  (CharacterAnimEvent.cs:213), which sets `m_animator.speed` (the punches play ×2 from their first frame; the kick 1×,
  2× around its hit, 1×; `BombThrow` 1× then 1.5×). Out of an attack (and of a minor action and an emote, able to
  move), `CharacterAnimEvent.CustomFixedUpdate` (160-170) sets the speed back to 1 every fixed step; `RPC_Stagger` sets
  1 (Character.cs:2682); a melee hit freezes the animator for 0.15 s (`Attack.DoMeleeAttack` → `Character.FreezeFrame`
  → RPC to everybody → `CharacterAnimEvent.FreezeFrame`, which stores the speed and puts it back, 177-211). Hit events
  are events of the same clip, so they move with the speed. No vanilla stat changes attack speed.
- **Animation speed sync.** The owner's `ZSyncAnimation.SyncParameters` writes `m_animator.speed` into the ZDO
  (`animation_speed`) whenever it changes; every other game sets the animator speed of its copy from that value every
  fixed step (ZSyncAnimation.cs:100-145), which overrides the copy's own `Speed` events one step later. So every game
  plays a player's clips at the speed the owner's animator plays them, with or without any mod.
- Triggers are global names (any family can be fired from any stance, if the animator has a transition for it).
  CaptainValheim fires `battleaxe_attack1` from the Shield stance; vanilla sword secondaries borrow other families.
  `ZSyncAnimation.SetTrigger` (ZSyncAnimation.cs:148) sends the trigger name to everybody (RPC → `Animator.SetTrigger`,
  206-209); a name missing from an animator is ignored; there is no network reset. `ZSyncAnimation.SetBool` (159)
  writes the ZDO; remote clients apply only the bools listed in the prefab's `m_syncBools` (SyncParameters, 100-145;
  the player's list has `blocking`, in-world run). `ZSyncAnimation.HasParameter(name, type)` (222) checks a parameter
  at runtime (it allocates the parameter array).

### 1.11 Tooltip

`ItemData.GetTooltip(ItemData, int, bool, float, int, bool)` (static, ItemDrop.cs:848): `AddHandedTip` (803) prints
`$item_twohanded` for TwoHandedWeaponLeft; the weapon branch (931-994) prints damage per type (with the skill, and
`GetDamage` with the item's world level), stamina use (`m_attack.m_attackStamina`), `AddBlockTooltip` (block armor with
the skill-scaled value, block force, parry lines), knockback (`m_attackForce`), backstab if > 1; then
`Player.AppendEquipmentModifierTooltips` ("Movement -30%").

## 2. Design

### 2.0 Core: which items, and how the data reaches them

- **Tower list** (`Towers` setting): `ShieldWoodTower:6, ShieldBoneTower:8, ShieldIronTower:12,
  ShieldSerpentscale:15, ShieldBlackmetalTower:20, ShieldFlametalTower:28, ShieldGoldTower:32` = prefab name and the
  bash's blunt damage. An entry without `:n` uses 10. Checked against the vanilla snapshot of each prefab: unknown
  names are logged once as MISSING (Combat Adjustments' lesson: a silent typo is a bug); an entry that is not a shield
  (item type Shield, animation Shield, skill Blocking) is refused with a Warning; a shield that can parry
  (`m_timedBlockBonus > 1`, e.g. `ShieldBanded`) is accepted with a Warning that it loses its parry.
  `ShieldIronSquare` and the creature copies are not in the default list (decision 10).
- **Identify an item**: `item.m_dropPrefab` (set on every real item) → dictionary of tower prefabs; when it is null
  (a prefab's own data, e.g. recipe panels), `ReferenceEquals(item.m_shared, prefab's shared)`. Never by item name
  (the creature copies share the name). **Hot paths** (per-tick and per-hit code on the local player) use a cached
  verdict for the current left item: it is recomputed only when the `m_leftItem` reference or the rules generation
  changes, then combined with a live read of `m_itemType == TwoHandedWeaponLeft` (the copy carries the tower data
  now). A third-party two-handed shield that is not in the list is never treated as a tower.
- **Base values = vanilla**: the first time a tower prefab is seen in the game session, its SharedData fields listed
  in 2.0.1 (and the reference of its original `m_attack`) are stored by prefab name. That first pass is the main
  menu's (1.9), so the base is the vanilla data (plus whatever a mod changed before the menu). Prefab edits live in
  memory for the whole process, so the snapshot is never taken again (a second world load would otherwise snapshot our
  own values). Every write is computed from the snapshot and the rules: idempotent, never "multiply the current value".
  Data mods that edit these fields later (in the world's `ObjectDB` or after a server sync, as WackysDatabase packs do)
  are overwritten at every pass and heal; the README says not to combine them (decision 26).
- **Rules in force** (`TowerRules.InForce`): single player, host and dedicated server: their own config; a client of
  a server with the mod: the server's rules once they have arrived, and **no tower data at all (vanilla) while it waits
  for them**, so the client's own `Towers` list never unequips anything at join (the pending policy of 4: a client's
  own gameplay settings never apply on a server with the mod); Debug `TestRules` override. The
  copied `ServerRules` keeps a **generation** counter, raised when the rules in force change (server rules received and
  different, own config changed while not using the server's, session forgotten, `TestRules` set or cleared).
- **Where it is written** (`TowerData.Apply`):
  - the ObjectDB prefabs (`ObjectDB.Awake` / `CopyOtherDB` postfixes; the main-menu pass with an empty `m_items` is
    skipped);
  - every live copy on this game at activation and on every rules apply: the local player's inventory (equipped
    items included), all loaded `Container` inventories, `ItemDrop.s_instances`;
  - **heal on sight** for players (a copy created before activation, or before a rules change, is fixed before it
    matters): `Humanoid.EquipItem` prefix, `Humanoid.StartAttack` prefix, `GetTooltip` prefix.
- **Only players' towers are towers** (decision 25): a non-player humanoid that picks up or equips a listed tower gets
  that item's own copy reverted to the snapshot (`Humanoid.Pickup` and `Humanoid.EquipItem` prefixes, before
  `GiveDefaultItem` tests `IsWeapon()`). A modded creature that carries a player tower keeps a vanilla one-handed
  shield, and every game agrees on how it fights.
- **Rules apply** (`TowerSync`, driven by the `ZNet.Update` postfix and by `OnActivated`): when the generation differs
  from the applied one, wait until 0.5 s after the last change (typing in ConfigurationManager fires one change per
  key), compare the rules' text with the applied one, then on the main thread: new catalog, `ApplyAll`, `FixHands`,
  bash rebuilt, Braced effect updated, and a running bash cancelled when its tower leaves the list (2.6).
- `TowerData.Revert` writes the snapshot back to the same places (G9).
- **Robustness**: `ApplyAll` / `RevertAll` handle each item and each container in its own `try`/`catch` and log one
  summary Warning ("n items could not be updated"), so one bad item never stops the pass or the toggle.

#### 2.0.1 Fields written on a tower's SharedData

| Field | Value | Goal |
|---|---|---|
| `m_itemType` | `TwoHandedWeaponLeft` | G1 |
| `m_attachOverride` | `Shield` | G1 (back slot, armor stand) |
| `m_timedBlockBonus` | `min(vanilla, 1)` | G3 |
| `m_perfectBlockAdrenaline` | 0 | G3 (false tooltip line) |
| `m_blockPower`, `m_blockPowerPerLevel` | vanilla × `BlockArmorMultiplier` | G4 |
| `m_deflectionForce`, `m_deflectionForcePerLevel` | vanilla × `BlockForcePercent` / 100 | tuning |
| `m_movementModifier` | `-CarrySlowPercent / 100` | G2 |
| `m_damages` | blunt = the tower's bash damage, every other type 0 | G6 |
| `m_damagesPerLevel` | all 0 | G6 (decision 4) |
| `m_attackForce` | `BashKnockback` | G6 |
| `m_blockable`, `m_dodgeable` | true | G6 (the bash can be blocked and dodged in PvP) |
| `m_backstabBonus` | 1 | G6 (no sneak multiplier on a shove) |
| `m_buildBlockCharges` | false | decision 11 |
| `m_attack` | the shared bash `Attack` (2.6), when it exists | G6, G7 |

`m_skillType` (Blocking), `m_animationState` (Shield), `m_maxQuality`, durability, weight, `m_blockAdrenaline`,
`m_damageModifiers` (Serpentscale pierce resistance) and `m_secondaryAttack` (empty) stay vanilla. The NG+ damage
term of `GetDamage` is removed for tower copies by a postfix (2.6, decision 21).

### 2.1 G1 Two-handed

- **Mechanism**: the item type (2.0.1). Vanilla `EquipItem` then enforces two hands on every path (hotbar,
  inventory, load, re-show after swimming), with no equip patch: equipping the tower empties both hands; a one-handed
  weapon or a torch unequips it because it is neither Shield nor Torch; a round shield replaces it; bows, two-handed
  weapons and tools empty both hands. The stance stays Shield (left item wins), the hand model is the same, the back
  and armor-stand slots follow `m_attachOverride = Shield`.
- **Guards that keep vanilla behaviour** (the item now counts as a weapon):
  - `Humanoid.Pickup` prefix: for a player, `autoequip = false` when the picked-up item is a tower (vanilla never
    auto-equips shields, also with both hands empty); for a non-player humanoid, the item's copy is reverted (2.0).
    While a tower is held, vanilla already skips auto-equip for other weapons (two-handed left item).
  - `ObjectDB.GetAllCraftableWeapons` postfix: remove tower prefabs from the returned (cached) list, so the
    "craft every weapon" achievement and craft progress do not change.
  - Item stands: **no patch**. Generic stands have an empty `m_supportedItems` list and accept any item with an attach
    child, whatever its type (1.2). Only a stand with a non-empty list that does not name the tower but lists `Shield`
    in `m_supportedTypes` would refuse a two-handed tower; the `tower.data` self-test lists every stand's lists (NOTE)
    to confirm none exists (9).
  - MC `ItemKinds` (src/Shared, already done, 7.5): tower shields stay "Shield" for Sort Chest, Crafting Search and
    Sort and the Compendium.
- **Edge cases**:
  - A save made before the mod with a sword and a tower both equipped: `Player.EquipInventoryItems` equips in
    inventory order, the later one wins, the other is marked unequipped (vanilla code, no crash).
  - Turning the mod on while a one-handed weapon (or torch) and a tower are held (`FixHands`): the right-hand item is
    unequipped (decision 15); a hidden right item (swimming, hide key) is dropped from the hidden slot with
    `UnequipItem(hiddenRightItem)`, which clears the slot and refreshes the look (vanilla, 1.2), so re-showing does not
    drop the tower and the weapon does not stay drawn on the back.
  - Rules that add or remove a tower while it is equipped: the same fix after every apply.
  - A broken tower cannot be equipped (vanilla durability guard).
- **UI**: tooltip "Two-handed" (vanilla `$item_twohanded`); the right hand visibly empties.

### 2.2 G2 Very slow

- **In hand**: the tower's `m_movementModifier = -CarrySlowPercent/100` (default -0.30, vanilla -0.10). Vanilla then
  gives jog ×0.70 (4 → 2.8 m/s), sprint ×0.55 (7 → 3.85 m/s at run skill 0), run/jump/dodge stamina +30%, knockback
  taken ×0.70, and the tooltip line "Movement -30%". Walk (1.6) and crouch (2.0) are unscaled in vanilla and stay so.
  Like every item, a tower put away on the back does not slow (1.7, decision 29). `CarrySlowPercent` is capped at 40.
- **Sprint floor**: armor slows add up, and below a total of -0.46 sprinting is slower than jogging, below -0.667 it
  goes negative (1.7). A `Player.GetRunSpeedFactor` postfix, for the local player with a tower in hand, raises the
  factor to at least `m_speed × GetJogSpeedFactor() / m_runSpeed`: sprinting is never slower than jogging (it still
  costs sprint stamina, vanilla). Armor slow values are unverified; the `tower.data` self-test lists them (NOTE).
- **Braced**: one status effect `SE_Stats` "Braced" (name `MC.Combat.Shields.TowerWall.Brace`) stays on the local
  player **while a tower is in the left hand**: added on equip, at activation and after a rules apply; removed
  (quietly) when the tower leaves the hand, on deactivation and when `Player.m_localPlayer` changes (death); re-added at
  once if something else removed it (`HaveStatusEffect(hash)`, a hash-set lookup per tick). One add per equip, not
  one per block press (each add clones and logs an analytics event, 1.8). Every tick (`UpdateBlock` postfix, after
  vanilla has written `m_internalBlockingState`), its fields follow the state:
  - braced (`m_internalBlockingState`): `m_speedModifier = -BraceSlowPercent/100` (default -0.30), else 0.
    `SE_Stats.ModifySpeed` scales every ground speed (jog, walk, crouch) and the turn speed, and clamps at 0.
  - stagger resistance and HUD: see 2.5 (b).
  Default braced jog: 4 × 0.7 × 0.7 = 1.96 m/s (vanilla tower block: 3.6), braced walk 1.12, turn 300 → 210°/s.
  Vanilla already forbids sprinting and crouching while blocking.
- Speed table (run skill 0, no armor slow):

  | State | Vanilla tower | Default mod |
  |---|---|---|
  | Jog | 3.6 | 2.8 |
  | Sprint | 5.95 | 3.85 (never below jog) |
  | Walk / crouch | 1.6 / 2.0 | 1.6 / 2.0 |
  | Braced jog / walk | 3.6 / 1.6 | 1.96 / 1.12 |
  | Braced turn | 300°/s | 210°/s |
  | Tower put away | normal | normal |

### 2.3 G3 Cannot parry

- `m_timedBlockBonus = min(vanilla, 1)` (vanilla towers are 0): the parry flag needs `> 1` (BlockAttack step 2), so
  no parry bonus, no parry stamina rule, no attacker stagger, no parry effect, no parry adrenaline (a timed block gives
  the tower's `m_blockAdrenaline`, 2, like any block); the tooltip has no parry-bonus line.
  `m_perfectBlockAdrenaline = 0` removes the Gold tower's false "Parry adrenaline: 15" line.
- **UI**: an explicit tooltip line "Cannot parry".

### 2.4 G4 A lot of block armor

- `m_blockPower` and `m_blockPowerPerLevel` × `BlockArmorMultiplier` (default 2.5). The Blocking skill still adds up
  to +50% (vanilla). Durability wear per block drops by the same factor (it divides by block power).
- Worked numbers, one hit of 100 blunt, Blocking 0, block stamina drain 10:

  | Tower q1 | Vanilla armor: gets through / stamina | ×2.5 armor: gets through / stamina |
  |---|---|---|
  | Iron (52 → 130) | 48.1 / 10.0 | 19.2 / 6.2 |
  | Blackmetal (104 → 260) | 24.0 / 7.3 | 9.6 / 3.5 |
  | Flametal (140 → 350) | 17.9 / 5.9 | 7.1 / 2.7 |
  | Nord Greatshield (158 → 395) | 15.8 / 5.3 | 6.3 / 2.4 |

  Same-tier example (single player, 0-star Troll, default world modifiers, no body armor): Iron tower against a Troll
  punch (60 blunt): vanilla 17.3 through and 8.2 stamina, the mod 6.9 and 4.1. More players nearby, a higher
  enemy-damage world modifier or a starred Troll raise the hit before the block (1.5).
- **Block push on attackers** (`BlockForcePercent`, default 100 = the tower's own block force): the deflection push
  grows as the blocked fraction shrinks (1.4 step 11), and ×2.5 armor lowers that fraction, so at 100 attackers are
  pushed about 10-35% harder than by a vanilla tower for the same hit (Troll punch on the Iron tower: 0.80 of the block
  force instead of 0.59). Lower values keep attackers in bash range (decision 16, T29).
- **UI**: the tooltip's block armor (vanilla line) shows the new value; blocked hits show the vanilla "Blocked" text.

### 2.5 G5 An immovable wall that blocks virtually anything from the front

All three parts are decided on the bearer's game in one `Character.RPC_Damage` prefix, only when the victim is
`Player.m_localPlayer` (the owner of the player runs `RPC_Damage`) and a tower is in its left hand (cached verdict,
2.0), before vanilla's own code runs. **Braced for this hit** = `IsBlocking()` (live, so there is no lag after the
block key is pressed). **The brace holds for this hit** = braced and `GetStamina() > reserve`, where reserve = one full
block as vanilla spends it = `m_blockStaminaDrain × (1 + GetEquipmentBlockStaminaModifier()) × Game.m_staminaRate`
(10 by default; `Player.UseStamina` applies the world stamina rate; status-effect changes to block stamina are
ignored, L12).

- **(a) Unblockable frontal attacks** (`BlockUnblockableAttacks`, default on). Cheapest checks first:
  `!hit.m_blockable`; hit type `EnemyHit` or `PlayerHit`; `hit.HaveAttacker()` and the attacker is not the victim;
  braced; the direction flattened to the ground is not zero and comes from the front (`Dot(flat, forward) < 0`); last,
  the attacker is **hostile** (`Brace.IsHostile`): a player attacker only when the hit has no `m_ignorePVP` (a PvP
  attack; `Aoe` sets `m_ignorePVP` on spells that must reach every player whatever the PvP setting, such as the Staff
  of Protection's bubble, and vanilla drops other player hits before the block when PvP is off), any other attacker
  when `BaseAI.IsEnemy(attacker, bearer)` (faction, tame, group and aggravation: the friend-or-foe test vanilla `Aoe`
  uses). Then `hit.m_blockable = true`, and vanilla's own `BlockAttack` does the rest (its own facing test, armor,
  stamina, status-effect clear). So the Blob's poison cloud and the Greydwarf Shaman's spray, hit from the front, are
  blocked like a sword swing and any status effect carried by the hit is cleared. Poison removed at Blocking 0: Blob
  (90): Wood tower 28%, Bone 72%, Iron 83%, Blackmetal 91%; Greydwarf Shaman (30): Wood 70%, Bone 91%. The hostility
  test matters because a successful block clears the hit's status effect even with 0 damage: an ally's protective
  bubble would be lost, and each cast would give the bearer Blocking skill and adrenaline for free.
- **(b) The wall does not collapse** (`BraceStaggerResistPercent`, default 80). The prefix sets the Braced effect's
  `m_staggerModifier` to `-resist/100` when the brace holds for this hit, else 0. Vanilla `AddStaggerDamage` applies it
  through `SEMan.ModifyStagger`, so the block-time stagger (BlockAttack step 6) and the stagger from what still lands
  (`ApplyDamage`) are both cut; step 7 then keeps the block. The `UpdateBlock` postfix writes the same value every tick
  from the same rule. `SEMan.ModifyStagger` adds every effect's modifier with no floor, so the Braced value is raised
  when needed to keep the sum with the other effects' `m_staggerModifier` at -1 or more (Fader's power gives -0.5:
  -0.8 - 0.5 would make each blocked hit lower the stagger bar); it is never above 0. Example: Wood tower (×2.5 = 25)
  against a Troll punch (60): 35 gets through, which staggers a player with 75 max HP (threshold 30) and drops the
  whole hit in vanilla; with -80% the stagger is 7 and the block holds, at 10 stamina per block.
- **Guard break is exact and vanilla** (decision 8): with the stamina for one more full block or less, the
  resistance is off for the hit; vanilla then spends the stamina, and a block that staggers or empties stamina lets the
  whole hit through (step 7). The resistance comes back only when stamina rises above the reserve again (regen waits
  1 s after the last use and runs ×0.8 while blocking), not with the first sliver of stamina.
- **(c) Immovable** (`BraceKnockbackResistPercent`, default 100). A `Character.ApplyPushback(Vector3, float)` prefix
  on the local player: when the push comes from the front (`Dot(dir, forward) < 0`) and the brace holds, the push is
  multiplied by `1 - resist/100`. For a hit, "the brace holds" is the value decided in the `RPC_Damage` prefix for that
  hit (so the hit that breaks the guard pushes as in vanilla); for area knockback outside a hit (`Aoe.OnHit`, 1.5) the
  rule is evaluated at that moment. Being pushed also blocks `StartAttack` (1.4), so without the push the bearer can
  bash right after a heavy blocked hit. AoE **launch** (`ForceJump`) is not covered (L4, decision 22).
- **What stays unblockable** (by design or by vanilla): hits from behind or the side (the facing test; they also push
  normally); true damage, chop and pickaxe damage (never blockable); DoTs already running, falls, drowning, lava, hot
  ocean, fire zones (`CinderFire`), smoke, impact, structural and self hits (hit-type filter); hits without an
  attacker; hits from attackers that are not hostile (allies' spells such as the Staff of Protection's bubble, tamed
  or neutral creatures); hits with stagger ×100 or more (they stagger before the block, L6); AoE launch (L4).
- **UI**: the vanilla "Blocked" number appears for these attacks. The Braced effect is a HUD status icon (the equipped
  tower's icon, `m_icons[0]`), hidden (`m_hidden`) when not bracing; while bracing it reads "Braced", and "Braced
  (exhausted)" with a flashing icon (`m_flashIcon`) when the brace no longer holds. Its vanilla status tooltip shows the
  movement and stagger lines. The item tooltip has the line "Braced: movement -30%, blocks attacks from the front that
  normally cannot be blocked; while you have stamina for another block: stagger -80%, no knockback from the front":
  each part carries the condition the code applies (the slow and the made-blockable rule whenever braced; the stagger
  resistance from every side, decision 7, and the push rule only while the brace holds).

### 2.6 G6 Shield bash

- **Input**: the normal attack button. With a tower in the left hand, `GetCurrentWeapon` returns the tower, vanilla
  `StartAttack` clones the tower's `m_attack` (the bash) and plays it. Pressing attack while holding block bashes:
  `IsBlocking()` is false during `InAttack()`, so the guard drops for the bash and comes back after it (no patch).
- **Cooldown** (`BashCooldown`, default 2 s, decision 30): a bash cannot start less than `BashCooldown` seconds after
  the start of the local player's last bash. The `StartAttack` prefix returns false before vanilla runs (only for the
  primary attack of a held tower, right hand empty), the way vanilla refuses an attack while the last one plays; a
  held button retries every tick, so the bash starts as soon as the time is up; nothing is spent and no trigger is sent
  while it waits. Vanilla's input buffer keeps a press only 0.5 s (`Player.PlayerAttackInput` sets
  `m_queuedAttackTimer` to 0.5 on the press and calls `StartAttack` each tick while it runs), so a single press made
  once the slowed swing is over (about 1.1 s) but more than 0.5 s before the 2 s are up would vanish: nothing happens
  while the guard is visibly back. So when the prefix refuses a bash for the cooldown and a press is buffered while the
  player is no longer in an attack, it keeps `m_queuedAttackTimer` above 0 until the cooldown ends (two ticks of
  margin, never above 0.5): the bash starts at the first tick allowed. A press made earlier in the swing keeps
  vanilla's 0.5 s. The time is taken in the `StartAttack` postfix; a start that never reaches its attack state
  (watchdog A) clears it. With 0 the next bash can start when the last swing ends (vanilla rule, the first test
  build). 2 s is the rate of the vanilla move the bash now matches in stamina and stagger, the Iron mace's secondary
  attack (clip about 2 s, 1.1); the slowed punch itself takes about 1.1 s (2.7), so the guard is back for about 0.9 s
  between two bashes.
- **The bash `Attack`** (`BashAttack`, one object shared by every tower copy; vanilla clones it per swing): a
  `MemberwiseClone` of the player's unarmed primary attack (`Humanoid.m_unarmedWeapon.m_itemData.m_shared.m_attack`,
  taken from the player in the `StartAttack` prefix, or from `Player.m_localPlayer` for tooltips; no player exists at
  ObjectDB time), with:

  | Field | Value | Why |
  |---|---|---|
  | `m_attackAnimation` | trigger of the resolved `BashAnimation` (2.7) | G7 |
  | `m_attackChainLevels`, `m_attackRandomAnimations` | 0, 0 | `Start` fires the name as is; no combo, no ×2 finisher |
  | `m_attackStamina` | `BashStamina` (default 20; -33% at Blocking 100) | a stagger not easier than with a buckler parry (decision 31) |
  | `m_staggerMultiplier` | `BashStagger` (default 25; max 50, never ≥ 100) | heavy stagger; ≥ 100 would bypass blocks |
  | `m_forceMultiplier` | 1 (push = `BashKnockback` × skill factor) | moderate push |
  | `m_attackRange`, `m_attackAngle`, `m_attackRayWidth` | `BashRange` 1.8, `BashAngle` 60, 0.4 | a shield face is wide |
  | `m_multiHit`, `m_lowerDamagePerHit` | true, false | every enemy in the arc takes the bash's full damage and push (crowd control); only the one nearest the middle of the swing keeps the stagger multiplier (decision 35, below) |
  | `m_hitTerrain` | false | a shove does not stop on the ground |
  | kept from the unarmed attack | `m_attackType` Horizontal, `m_speedFactor` 0.1, `m_speedFactorRotation` 0.5, heights, `m_attackAdrenaline` 1, `m_raiseSkillAmount` 1, `m_skillHitType` Character, noise, its effect lists (never mutated) | |

  Rebuilt when the rules apply (a new object; each tower copy gets it at the next heal). Built lazily; until it
  exists, a tower's `m_attack` stays the vanilla one (`HavePrimaryAttack` false) and the `StartAttack` prefix builds
  and sets it before vanilla checks it. The `StartAttack` postfix remembers the started clone as **the running bash**
  (local player only) for the cooldown, the swing speed, the watchdogs, the one-hit rule (2.7) and cancelling. Vanilla
  spends the stamina when the swing reaches its attack state (1.3), whether it hits or not.
- **Damage**: blunt from the tower list (2.0), scaled by the random Blocking factor, dealt by the vanilla pipeline:
  the victim's resistances and armor apply. In an NG+ world the vanilla gear bonus (+120 per world level, 1.3) is
  **not** added to the bash: an `ItemData.GetDamage(int, float)` postfix recomputes the damage of an applied tower copy
  without the world-level term when the world level is above 0 (the tooltip reads the same method). NG+ enemy armor
  then absorbs nearly all of it (e.g. 4.8 → 0.06): in NG+ the bash is a pure stagger tool (decision 21).
- **Stagger: decoupled from the target's armor and group scaling** (decision 19). Vanilla derives stagger from the
  damage left after the victim's owner has applied resistances, NG+ armor and the group scaling (1.6); a deliberately
  small hit then loses most of its stagger (a Nord bash of 30 becomes 2.25 at world level 1; two players nearby cut it
  to 77%). So the bash's stagger is applied by the **creature's owner** (every game on a server with the mod has it):
  - `Character.RPC_Damage` prefix, on the victim's owner, for a **bash hit on a non-player**: `hit.m_skill ==
    Blocking`, `1 < hit.m_staggerMultiplier < 100`, the attacker is a `Player`, hit type `PlayerHit` (the enum compare
    comes first; every other hit costs one compare). It computes `S = (blunt + slash + pierce + lightning) of the hit
    as received × hit.m_staggerMultiplier`, × the NG+ health factor (`worldLevel × m_worldLevelEnemyHPMultiplier`,
    1 at world level 0) so a bash staggers the same creatures in NG+ as in a normal world; stores the hit and `S`, and
    sets `hit.m_staggerMultiplier = 0` so vanilla adds none of its own.
  - `Character.ApplyDamage` prefix + postfix (the stored hit on this character only; one reference compare for every
    other call): if vanilla's early-return conditions do not hold (dead, teleporting, cutscene), the postfix calls
    `AddStaggerDamage(S, hit.m_dir, hit)` after vanilla, **also when the landed damage was 0.1 or less** (vanilla would
    return before stagger). The status-effect stagger modifiers, the threshold, the stagger adrenaline for the bearer
    and the ×2 damage window for everyone's next hits stay vanilla.
  - **A bash that lands almost nothing still alerts** (NG+, decision 21). When vanilla returned at its damage check
    (the landed damage, `hit.GetTotalDamage()` after vanilla's in-place scaling, is 0.1 or less), it also skipped the
    end of `ApplyDamage`: `m_onDamaged`, through which `MonsterAI.OnDamaged` wakes the creature, alerts it and targets
    the attacker (Character.cs:2436-2442, 2483-2486; MonsterAI.cs:184-190). Left like that, a bash would stagger a
    creature that is still unaware, and the next hit on it would be a vanilla backstab (not alerted, Character.cs:2336)
    on a staggered target (×2): a free "bash, then knife" opener with sneak-attack XP. So after adding the stagger in
    that case, the postfix makes the call vanilla skipped, once: `m_onDamaged(landed damage, attacker)`. The creature
    then reacts as to any hit that staggers it in vanilla; other mods that hook `OnDamaged` (Sneak Ambush's reveal) see
    it too. A bash within the lock limit (no stored stagger, below) is left to vanilla, like any tiny hit; the creature
    was alerted by the bash that started the limit.
  - `Character.RPC_Damage` finalizer puts back the stored hit of the call around it (dodged, filtered or otherwise not
    landed: no stagger; stage 7: a finalizer, so this also happens when the call throws).
  - A bash that is dodged never reaches `ApplyDamage`: no stagger. Against **players** (PvP) nothing changes: vanilla
    stagger from the landed damage, reduced by their armor and their block.
  - On a creature owned by a game without the mod (only with `AllowPlayersWithoutMod`), vanilla applies the serialized
    multiplier to the landed damage: weaker, as described above.
- **One heavy stagger per bash** (decision 35), on the bearer's game, before the hits leave for the creatures' owners.
  `Attack.DoMeleeAttack` (Attack.cs:1240-1452) first sweeps the whole arc with rays 4° apart, from one side to the
  other, collecting every object hit into a local list (`AddHitPoint`), then loops over that list and calls
  `Damage(hitData)` on each, every `HitData` with the attack's `m_staggerMultiplier`. So the mod opens a scope when
  the first Hit event of the running bash clone runs (`Attack.OnAttackTrigger` prefix, closed by a finalizer), keeps
  vanilla's list from an `Attack.AddHitPoint` postfix (our clone only), and in a `Character.Damage` prefix (scope open
  and a bash hit: `BashStagger.IsBashHit`) picks, at the first bash hit, **the creature nearest the middle of the
  swing**: among the `Character`s of the list, the smallest flat angle between the bearer's facing and the direction
  to the creature's position, the nearer one when two are within 1°. The pick keeps the multiplier; every other
  creature of that swing gets `m_staggerMultiplier = 1`, so on its owner it is not a bash hit (`IsBashHit` needs more
  than 1) and staggers like a punch (landed blunt × 1, vanilla). Damage, push, skill and durability are unchanged for
  all. The pick is geometry only: the lock limit lives on each creature's owner, so a pick inside its lock staggers
  like a punch and so do the others (that bash staggers nobody; aim at another creature). If the list never came (the
  postfix did not run), the first creature hit is the pick. Vanilla's own multi-hit split (`m_lowerDamagePerHit`,
  Attack.cs:1389: the skill factor ÷ (targets × 0.75)) was not used: it gives 1.33 bashes' worth of stagger in total
  whatever the group, so two Draugr beside each other would both be staggered by an Iron tower (each still 50-110
  against 50), and it would also split the push that keeps a line back.
- **Lock limit** (`BashStaggerLock`, default 8 s, decision 20). Without it, one player could re-stagger a creature
  with a bash every time it recovers, because the bar stays full after a stagger (1.6). On the creature's owner: when a
  bash's `AddStaggerDamage` staggers the creature, the time is stored for that creature (a `ConditionalWeakTable` keyed
  by the `Character`, cleared with it; lost when ownership moves, so at worst one extra stagger). A bash that lands
  while the creature is already staggering (from another hit) starts no limit: `AddStaggerDamage` returns true
  whenever the bar reaches the threshold, but `RPC_Stagger` ignores a creature that is already staggering, so that
  bash staggered nothing. A bash hit on that creature within the lock keeps multiplier 1 and no stored `S`: it
  staggers like a punch (landed blunt × 1). Shared by every bearer (two tower players cannot alternate either); other
  weapons are not limited (vanilla). A Draugr's stagger lasts about 2.55 s *(in-world run)*. With the 2 s bash cooldown,
  bashes land at 0, 2, 4, 6 s...; the bar has drained after the lock, so the first bash after it staggers again: with
  8 s every 8 s, about 32% of the time at most; with the former 5 s every 6 s, about 42% (about half with the first
  build's 0.74 s between bashes and no cooldown).
- **Mean stagger per bash** (`S` before the NG+ factor; mean skill factor 0.4 / 0.7 / 0.925; damage before the target's
  armor):

  | Tower (bash blunt) | Blocking 0 | 50 | 100 | Range at 0 | Damage at 0 / 100 |
  |---|---|---|---|---|---|
  | Wood (6) | 60 | 105 | 139 | 38-83 | 2.4 / 5.6 |
  | Bone (8) | 80 | 140 | 185 | 50-110 | 3.2 / 7.4 |
  | Iron (12) | 120 | 210 | 278 | 75-165 | 4.8 / 11.1 |
  | Serpentscale (15) | 150 | 263 | 347 | 94-206 | 6 / 13.9 |
  | Blackmetal (20) | 200 | 350 | 463 | 125-275 | 8 / 18.5 |
  | Flametal (28) | 280 | 490 | 648 | 175-385 | 11.2 / 25.9 |
  | Nord Greatshield (32) | 320 | 560 | 740 | 200-440 | 12.8 / 29.6 |

  Against thresholds (1.6), Blocking 0, bashes 2 s apart (the cooldown): a Wood tower staggers a Greydwarf or a
  Skeleton in one bash; an Iron tower always staggers a Draugr (50) in one bash and a Draugr Elite (100) in one about
  7 times in 10 (else two); a Troll (180, draining 36/s, so 72 between two bashes) takes two bashes about 4 times in 10
  (S1 + S2 ≥ 252), three about 4 times in 10 (S1 + S2 + S3 ≥ 324), four about 2 times in 10, more when the bashes are
  further apart than 2 s (it was usually two quick bashes 0.75 s apart); a Blackmetal tower staggers a
  Goblin (52.5) or a Seeker (60) in one, a Goblin Brute (240) in two or three and a Lox (300, draining 120 between two
  bashes) in two to four (like the Iron tower on a Troll). At Blocking 50 an Iron
  tower staggers a Troll in two. A staggered non-player takes ×2 damage from the next hits (vanilla), and the bearer gets
  `m_staggerEnemyAdrenaline` per stagger: the bash sets up allies. Weapons of the same tiers deal 12 (club) to 135
  (Ashlands); the bash's damage is about a fifth.
- **Cost per stagger, against a buckler parry** (decision 31; 1.4, 1.1). A buckler parry staggers the attacker
  outright and costs no stamina, but it needs the enemy to swing and the shield raised within the 0.25 s before the
  hit; a mistimed one is a normal block with little armor (Iron buckler against a Draugr's sword: 10 stamina and 30
  damage through). So a player who lands one parry in two pays about 10 stamina per stagger, one in three about 20.
  The bash needs neither timing nor an enemy swing, so it must cost at least what a middling parrier pays:

  | Iron tower, Blocking 0 | Stamina per bash | Draugr (50) | Draugr Elite (100) | Troll (180) | Other costs |
  |---|---|---|---|---|---|
  | First test build | 12 | 12 (1 bash) | 12-24 | 24-48 (2-4 bashes 0.75 s apart) | none: 0.5 s to the hit, next bash at once |
  | Now | 20 | 20 (1 bash) | 20-40 | 40-80 (2-4 bashes 2 s apart, mostly 2 or 3) | guard down about 1.1 s per swing, hit 0.8 s after the press, a missed bash costs the full 20, 2 s between bashes |
  | Iron buckler parry | 0 when timed, 10 + damage when not | ~10 at 1 in 2, ~20 at 1 in 3 | same | same (a parry ignores the bar) | the enemy must swing; 0.25 s window |

  20 is also what the Iron mace's secondary attack costs for a similar stagger (110 for 20 stamina, 1.1), which the
  first build undercut. At Blocking 100 a bash costs 13.4 (-33%, vanilla attack rule) but also staggers much more (278
  mean for the Iron tower), as a skilled parrier also misses less. `BashStagger` stays 25 so that a tower still
  staggers the standard creatures of its tier in one bash (the "heavy stagger numbers" of the Goal): the cost is
  raised through stamina, as the user suggested. The comparison holds in a group too: one parry staggers the one
  attacker it parries, and one bash staggers one creature at most, the one nearest the middle of the swing (decision
  35); without that rule one 20-stamina bash staggered every creature in the 60° arc (three Draugr: about 7 stamina
  per stagger, no timing). The Iron mace's secondary, which splits its hit between targets, can still stagger two
  creatures well below its tier at once; the bash cannot.
- **Skill and bookkeeping**: the weapon is the tower (`m_skillType` Blocking): the bash's damage, push and stamina
  scale with Blocking, it trains Blocking (+1, ×1.5 when a character is hit, **including a Training Dummy**, as every
  weapon trains its skill there; decision 23), and a bash kill counts as a melee kill (`RPC_Damage` KillModifiers).
  Durability -1 per hitting bash (vanilla weapon rule). Backstab ×1: a bash never backstabs.
- **Bash rate**: one bash per `BashCooldown` (2 s) at most; a swing longer than that (the kick, about 2.4 s at speed
  0.6) sets the rate itself, because with 0 chain levels the next bash can only start when the clip ends. Stamina
  limits it further: 20 per 2 s is 10 per second, above the regen of 5-10 per second, which also waits 1 s after each
  use and stops during the swing (1.1).
- **Cancelling a bash** (decision 14): when the local player's running bash uses a tower that is about to be reverted
  (deactivation, a rules apply that drops it from the list), the mod does what `UnequipItem` does: `Stop()`, move it to
  `m_previousAttack`, `m_currentAttack = null` (1.3), and puts the clip's own speed back (2.7). Otherwise the clip's
  Hit event would still land with the reverted vanilla data (10 true damage, unblockable, undodgeable).
- **UI**: the tooltip's weapon lines (vanilla): Blunt damage, stamina use, knockback; plus "Bash stagger: ×25" and
  "Bash cooldown: 2 s" (left out at 0). The victim's stagger animation; the vanilla crit effect when an ally hits a
  staggered enemy.

### 2.7 G7 Bash animation

`BashAnimation` (server rule, default `ShieldPunch`):

| Option | Trigger | Clip *(in-world run)* | Why it is there |
|---|---|---|---|
| `ShieldPunch` (default) | `unarmed_attack1` | `Punchstep 2`: the second punch of the fist combo, which the wiki says is the left hand; with the tower on the left forearm it reads as a shield shove | the shield-arm motion; the look the user liked best in the first test; first fallback of `Custom` |
| `OtherPunch` | `unarmed_attack0` | `Punchstep 1`, the first punch | vanilla fires it from the Shield stance whenever a shield player punches; the last-resort fallback; also in case the hand mapping is the other way round |
| `Kick` | `unarmed_kick` | `Kickstep` | vanilla's own heavy-stagger move; slower (hit 0.48 s, swing 1.5 s at speed 1), for comparison |
| `Custom` | `BashCustomTrigger` | e.g. `BombThrow` for `throw_bomb` | free experiments: a player attack animation, e.g. `throw_bomb`, `spear_poke`, `mace_secondary`, `knife_stab0`, `battleaxe_attack1` |

The first build also had `ShieldUp` (the shield-arm punch with the `blocking` animator bool held on, to show the block
pose during the swing). It played exactly like `ShieldPunch` (1.10) and is gone (decision 33). The setting is a text
with the list of the four names: a config file that still says `ShieldUp` is read as `ShieldPunch`, without a warning.
BepInEx 5's `AcceptableValueList<T>` matches with `Equals` (case-sensitive) and clamps anything else to the first
name, and `ConfigFile.Bind` writes the clamped value back; the former enum setting parsed names in any case
(`Enum.Parse(..., ignoreCase: true)`). So the list is the mod's own subclass (`AnimationNameList`): a name in any case
(spaces around ignored) is read as the list spells it (`kick` → `Kick`); `ShieldUp` → `ShieldPunch` silently; any
other value → `ShieldPunch` with one Warning ("BashAnimation 'x' is not one of ShieldPunch, OtherPunch, Kick, Custom;
ShieldPunch is used.").

- **Resolution** (when the bash is built with a player present; once per rules generation and player object: the
  check allocates): the trigger must exist (`ZSyncAnimation.HasParameter(name, Trigger)`); a `Custom` trigger must also
  be an attack trigger that some vanilla player weapon fires: the set of every `m_attack` / `m_secondaryAttack`
  animation of the ObjectDB items, expanded with their chain and random suffixes (built once per ObjectDB). That
  refuses emotes, `stagger`, `death*`, `dodge`, `jump`, `attack_abort` and the like. The names of held, aimed,
  reloaded or attached attacks (`m_loopingAttack`, `m_bowDraw`, `m_requiresReload`, `m_attach`: `staff_rapidfire`,
  `bow_fire`, `crossbow_fire`, `staff_lightningshot`, `fishingrod_throw` in the 1.0.16 dump) are refused too, even
  when another item fires them plainly: their clips wait for a held button, a bool or a target the bash never gives.
  A looping clip is left only through `attack_abort`, which `Attack.Stop` fires only for a looping attack, and the
  bash clone is not one: the bearer would stay in the loop, unable to block, dodge, re-equip or attack. A refused
  name → one Warning ("BashCustomTrigger 'x' is not a player attack animation; using ShieldPunch", or "... is the
  animation of an attack that is held, aimed or reloaded, which a bash cannot play; using ShieldPunch") and the
  fallback.
- **Swing speed** (`BashAnimationSpeed`, default 0.6, range 0.3-1.5; decision 32; `BashSpeed`, the local player's
  running bash only). The clips set their own speed with `Speed` events (1.10); the mod scales them, never replaces
  them, so each clip keeps its own rhythm:
  - a `CharacterAnimEvent.Speed(float)` prefix multiplies the value of every `Speed` event of the running bash by the
    factor, while the bash is the current attack, not done, and the player is not staggering (the punches' ×2 becomes
    ×1.2);
  - when the bash reaches its attack state and no `Speed` event of its clip came yet (the kick, the bomb throw, clips
    without events play at 1 until their first one), the `UpdateBlock` postfix scales the animator speed once
    (1 → 0.6);
  - the Hit event belongs to the clip, so it comes later by the same factor: the default punch hits about 0.8 s after
    the press instead of 0.5 s and its swing ends after about 1.1 s instead of 0.74 s; the kick hits after about 0.9 s
    and ends after about 2.4 s (estimates from the clip events, measured by `tower.bash`). The 0.15 s hit-stop of a
    melee hit is not scaled; it stores the scaled speed and puts it back (1.10);
  - **everybody sees the same speed**: the owner's animator speed goes into the ZDO and every other game plays the
    player's copy at that speed (1.10). No RPC and no patch on the other games: players without the mod see it too;
  - **back to normal**: when the clip ends, vanilla sets the speed back to 1 (1.10). When the bash stops in the
    middle of its clip (tower put away, a rules apply that drops the tower, a watchdog, the mod turned off),
    `BashSpeed.End` writes back the clip's own speed (the last unscaled `Speed` value; into the hit-stop's stored speed
    while a hit-stop runs), and only if the animator speed is still the value the mod wrote: a stagger (vanilla sets
    1 and aborts the attack) is never undone. Turning the mod off cancels the running bash first (7.3); the patch goes
    with the live toggle;
  - watchdog (C)'s limit is divided by the factor when it is below 1 (3 s at 0.6 → 5 s), so a slowed long clip is not
    taken for one that never ends.
- **Watchdogs** (local player, in the `UpdateBlock` postfix; one reference compare when no bash is pending):
  - **(A) No attack state.** From the first bash start that has not reached an `attack`-tagged state (counted across
    the new clones that a held button creates every FixedUpdate when no cooldown holds it back, 1.3), if 0.3 s pass
    without `InAttack()` while the player is not staggering, knocked back, dead or in a minor action:
    `ResetTrigger(name)` on the local animator, cancel the bash (as above), clear its cooldown, one Warning, next
    option in the fallback chain. At most about 15 trigger RPCs go out once per failing option and rules generation.
  - **(B) No hit.** The running bash ends (`IsDone()`) without its Hit event and was not aborted by a stagger
    (`m_abortAttack`, `IsStaggering`): one Warning ("OtherPunch: the swing ends before its hit; using ..."), next
    option. With the attack button held (or a press queued), `Player.PlayerAttackInput` can start the next bash in the
    tick the clip ends, before the `UpdateBlock` verdict (`Player.FixedUpdate` and `Character.CustomFixedUpdate` have
    no fixed order): `StartAttack` stops the ended bash and moves it to `m_previousAttack`, so the `StartAttack`
    postfix gives the verdict for the bash it replaced.
  - **(C) No end.** The running bash is still not done 3 s (divided by the swing speed when below 1) after it reached
    its attack state (the longest vanilla player attack clip is about 2.2 s, `Atgeir360Attack` in the dump): stop it as
    `UnequipItem` does, fire `attack_abort` through `m_zanim` (vanilla's own way out of a looping state, synced to
    every game playing the clip), one Warning ("Custom: the animation 'x' did not end within 5 seconds; using
    ShieldPunch"), next option. A safety net: the resolution already refuses held attacks. If the state has no
    `attack_abort` transition, the trigger stays set until a later looping attack consumes it (vanilla leaves it the
    same way after a stagger abort).
  - Fallback chain: `Custom` → `ShieldPunch` → `OtherPunch`; `Kick` → `OtherPunch`; `OtherPunch` fails → one Error,
    no further fallback. The fallback holds for the rules generation (a settings change retries the chosen option).
    Remote animators keep a failed trigger set (no network reset exists, 1.10); a later punch by that player could
    consume it (cosmetic, unverified; at most one per failing option).
- **One hit per bash**: an `Attack.OnAttackTrigger` prefix skips the second and later Hit events of the running bash
  clone (clips with two Hit events, e.g. `DualAxes Attack 4` through Custom), so a bash is always one hit for one
  stamina cost. It returns false only for our own bash clone; every other attack runs untouched (7.2).
- **What changes with the option**: every option goes through the vanilla attack pipeline (stamina, attack tag, Hit
  event, network trigger) with the same damage, stagger, stamina, cooldown and speed factor; the look and the hit
  timing differ. The bash rate is the cooldown for every option whose slowed swing is shorter than 2 s (the punches,
  about 1.1 s); the kick's swing (about 2.4 s) sets its own slower rate.
- **Multiplayer**: `BashAnimation`, `BashCustomTrigger` and `BashAnimationSpeed` are server rules; on a server everyone
  uses the server's choice, so variants are tried in single player or by the host (README and T15 say so).

### 2.8 Performance

Hot paths: `Humanoid.UpdateBlock` (every FixedUpdate for every humanoid this game owns) returns after one reference
compare with `Player.m_localPlayer`; for the local player it reads the cached tower verdict (one reference compare plus
one field read), updates a few fields of the Braced clone and checks the watchdog reference. `Humanoid.StartAttack`
(every tick while the attack button is held) returns after one reference compare for other characters and the cached
held-tower lookup for the local player; during the bash cooldown it refuses after two more compares and one float
compare, before any heal. `CharacterAnimEvent.Speed` (a few animation events per clip, every character) returns after
one static read unless a bash is being slowed. `Character.RPC_Damage` (every hit on every owned character) tests
`hit.m_skill == Blocking` and the victim reference first; `Character.ApplyDamage`, `Attack.OnAttackTrigger` and
`Attack.AddHitPoint` (every ray hit of every melee attack) compare one stored reference; `Character.Damage` (every hit
sent) returns after one static read unless our bash's Hit event runs, and the pick (one `GetComponent` per object hit)
runs once per bash; `Character.ApplyPushback` and `Player.GetRunSpeedFactor` return after the local-player compare;
`ItemData.GetDamage` returns when the world level is 0. The dictionary lookup only runs in equip, attack start, tooltip
and pickup paths and when the left item changes. No allocation on any per-frame or per-hit path (the
`ConditionalWeakTable` entry is created only when a bash staggers).

## 3. Decisions

1. **Two-handed through the vanilla item type** (`TwoHandedWeaponLeft` + `m_attachOverride = Shield`). Options:
   (a) item type, as vanilla's blood staffs; (b) keep `Shield` and enforce two hands with `EquipItem` postfixes plus a
   `GetCurrentWeapon` / `StartAttack` override (CaptainValheim's route). Choice (a): vanilla enforces hands on every
   path, `GetCurrentWeapon` returns the tower natively (the bash is a plain vanilla attack), and (b) loops with every
   auto-shield mod (they equip the shield when a one-handed weapon is drawn, (b) would unequip the weapon). Cost,
   **flagged**: mods that look for `ItemType.Shield` stop seeing towers as shields (6.3).
2. **Bash animation: the user's literal request cannot be met; the closest option is the default, with an automatic
   fallback and variants in config.** **Flagged.** The user asked for "the same as the parry animation with a standard
   shield". Vanilla has no parry animation: a parry is the block pose plus effects and the attacker's stagger (1.10).
   Options: the block pose held during a shield-arm punch (`ShieldUp`), the plain shield-arm punch (`ShieldPunch`),
   the other punch, the kick, any attack trigger, a pose pulse without an attack clip (L2), a custom clip (L1). Choice:
   default `ShieldUp`, because it shows the one shield motion vanilla has, the raised guard, while the shield arm
   strikes. Two things are unverified: whether `unarmed_attack1` plays from the Shield stance outside the fist combo
   (vanilla only fires it as the combo's second step, 1.3), and whether the forced block pose lets the swing land its
   hit. So the watchdogs of 2.7 fall back to `ShieldPunch`, then `OtherPunch` (which vanilla fires from the Shield
   stance every time a shield player punches), with a Warning. `ShieldPunch`, `OtherPunch`, `Kick` and `Custom` stay
   selectable (G7); the user picks the final default after trying them (T15). **Settled by the first in-game test
   (2026-09-30), decision 33**: `unarmed_attack1` plays from the Shield stance, the pose never showed, `ShieldUp` is
   gone and `ShieldPunch` is the default.
3. **Bash on the normal attack button, no separate key, no secondary attack.** **Flagged**: vanilla gives a shield
   with an empty right hand the unarmed kick as secondary; a two-handed tower has an empty `m_secondaryAttack`, so the
   secondary button does nothing (L3).
4. **Bash numbers**: flat blunt per tower in the `Towers` list (not derived from block armor, which would give high
   damage, the opposite of the request), no growth with quality (quality raises block armor and durability; L11), ×25
   stagger of its blunt (vanilla's heaviest normal attack uses ×6; with the bash's small blunt this makes a tower's bash as
   heavy as a mace secondary of its tier), a single step (no ×2 finisher), the full damage and push on every enemy in
   the arc but the heavy stagger on one (decision 35; until then the full bash on every enemy in the arc),
   skill Blocking, blockable and dodgeable (vanilla towers carry unblockable, undodgeable data for their hidden
   counter), backstab ×1 (the Serpentscale's vanilla 4 is dropped), 1 durability per hitting bash. **Flagged:**
   ×25 and 12 stamina were first numbers for the in-game test; after it the stamina is 20 (decision 31), ×25 stays.
5. **"Slowed A LOT" = -30% in hand through the item's movement modifier + -30% more while braced through a status
   effect.** **Flagged** (interpretation, tunable). The movement modifier is the vanilla mechanism (tooltip line,
   heavier stamina, less knockback) but leaves walk and crouch unscaled (vanilla rule); so it is capped at 40%, sprint
   is floored at jog speed (decision 27), and the extra slow while bracing is a status effect that scales every ground
   speed and turning and never goes below 0. Carrying alone does not slow walking and crouching.
6. **"Block virtually anything" = ×2.5 block armor + frontal attack hits made blockable while braced + stagger
   resistance while braced with stamina.** **Flagged**: literally anything is neither possible nor wanted; hits from
   behind, true damage, environmental damage, DoTs, AoE launch and ×100 stagger hits stay as listed in 2.5 (L4, L6).
   Only attack hit types with an attacker are made blockable, so falls (whose direction passes the facing test) are
   never blocked.
7. **Stagger resistance applies to stagger from any direction while braced.** **Flagged.** It is a vanilla
   `SE_Stats.m_staggerModifier`, which cannot see the hit's direction. Rear hits are still not blocked (full damage,
   full push). A front-only version needs a scope across `BlockAttack` and `RPC_Damage` (L7).
8. **Guard break stays vanilla and is exact**: the brace (stagger resistance and knockback immunity) holds for a hit
   only while the bearer has the stamina for one more full block (10 by default), decided per hit before the block.
   Below that, blocks follow the vanilla rules: a block that staggers or empties stamina lets the whole hit through.
   Checking only "stamina above 0" would let the resistance come back with the first sliver of regen and keep a bearer
   who takes full hits from ever being staggered. No new "guard break" mechanic.
9. **No parry, vanilla-consistent**: the towers already cannot; the value is only guarded against other mods, and
   the Gold tower's parry-adrenaline line is removed.
10. **Which items**: the seven vanilla prefabs, including `ShieldSerpentscale` (the wiki lists it with the tower
    shields and its data is tower data: no parry, -10%, block force 100) and `ShieldGoldTower`. **Flagged:
    Serpentscale.** `ShieldIronSquare` is left out (not called a tower); creature copies untouched. A list setting
    lets servers add modded towers; entries that are not shields are refused, parrying shields are accepted with a
    Warning.
11. **Block charges forced off on towers** (`m_buildBlockCharges = false`). Otherwise a mod that turns the vanilla
    counter on (ZenCombat's default list has six of our towers) would fire our bash every N blocks with no animation
    and no stamina, on the shared Attack object. **Flagged**: ZenCombat's tower block charges stop on these towers.
12. **Vanilla-preserving guards**: a tower never auto-equips on pickup; towers stay out of the craft-every-weapon
    achievement; item stands need no patch (1.2); MC `ItemKinds` keeps them shields.
13. **No torch with a tower**: a consequence of two-handed (the vanilla torch rule keeps only a Shield-type left
    item).
14. **Data edits with snapshot, heal on sight and full revert**, rather than method patches for each field: the item
    type and the attack can only be data, and one mechanism for all fields keeps apply and revert symmetric. A running
    bash is cancelled before its tower is reverted (2.6).
15. **Turning the mod on while a weapon and a tower are held puts the weapon away** (the tower was the item the mod
    changed; the alternative, putting the tower away, would surprise a player who just switched the mod on to use it).
16. **Block push on attackers: the tower's own block force by default** (`BlockForcePercent` 100). **Flagged**:
    because the push grows as the blocked fraction shrinks, ×2.5 armor makes it 10-35% stronger than a vanilla tower's
    for the same hit (2.4). ZenCombat and Goo's zero it so attackers stay in reach; whether the push leaves attackers
    out of bash range is measured in game (T29), then the default is tuned.
17. **English tooltip and HUD strings**, no new localization keys (as the other MC mods).
18. **Both side, join check, server rules for everyone** (user rule; most of the mod runs on the bearer's game, the
    bash stagger on the creature's owner, 4).
19. **Bash stagger decoupled from the target's armor and group scaling, applied by the creature's owner.**
    **Flagged.** Options: (a) vanilla (stagger from the landed damage): the user's "heavy stagger, low damage" collapses
    in NG+ (enemy armor erases small hits) and in groups (-23% with two players, -47% with four), while thresholds do
    not shrink; (b) raise the bash's damage: contradicts "low damage"; (c) stagger from the hit as sent, applied by the
    creature's owner. Choice (c), possible because every game on a server with the mod has it. Resistances do not
    reduce the bash's stagger either. Players (PvP) keep the vanilla rule.
20. **Lock limit: after a bash staggers a creature, bashes add no heavy stagger to it for 8 s** (5 s until the
    first in-game test, see decision 34). **Flagged.** The vanilla stagger bar stays full after a stagger (1.6), so a
    heavy bash could keep a creature staggered by one player indefinitely (an Iron tower on a Draugr: one bash every
    ~4 s, below stamina regen). The first value, 5 s, was the time a bar needs to drain from full; the creature
    recovers and attacks between staggers. Setting (`BashStaggerLock`, formerly `BashStaggerCooldown`), per creature,
    shared by every bearer.
21. **NG+: no world-level damage bonus on the bash; its stagger follows creature health.** **Flagged.** Vanilla adds
    +120 blunt per world level to every item's damage; on a blunt-only bash that is 4 to 20 times its base, and with
    the decoupled stagger it would stagger everything. Without it, NG+ enemy armor absorbs the bash's damage (almost 0),
    and its stagger is multiplied by the same `worldLevel × 2` as creature health, so it staggers the same creatures in
    the same number of bashes as in a normal world. A bash that staggers but lands 0.1 damage or less still alerts the
    creature (2.6), as any staggering hit does in vanilla, so it never sets up a sneak attack.
22. **"Immovable wall" = no push from frontal hits and frontal area knockback while the brace holds** (G5 c,
    `BraceKnockbackResistPercent` 100). **Flagged**: AoE launch (`ForceJump`, e.g. some area attacks throw the player
    up) is not covered in v1 (L4); hits from behind push normally; carried, not braced, the vanilla ×0.7 knockback of
    the movement modifier applies.
23. **The bash trains Blocking like any weapon trains its skill, including on a Training Dummy.** **Flagged.** In
    vanilla, Blocking trains only by blocking real hits; with the mod, the tower's "weapon" is the shield, and the
    Training Dummy is vanilla's weapon-skill trainer. Accepted as vanilla-consistent; the README mentions it, and that
    toggle-blocking a weak monster is cheaper with ×2.5 armor (vanilla AFK blocking, easier). Alternative: no Blocking
    XP from bash hits on targets that cannot fight back (open question 10).
24. **A player whose copy of the mod is turned off counts as a player without the mod.** **Flagged.** Otherwise a
    player who unticks the mod would play vanilla towers with a sword on a server that requires the mod. The framework
    reports a turned-off copy: the client's hello says whether its own side is ready (on, no start error), and the
    `HelloState` message updates it while connected (`NetworkGate.PeerCompatible`, `PeerStateChanged`). Such a player
    is refused like one without the mod, about a second after joining or after turning it off, unless
    `AllowPlayersWithoutMod` (4). No mod-specific mechanism is needed.
25. **Only players' towers get tower data.** A non-player humanoid (third-party creatures that carry player shields)
    keeps vanilla data on its own copy: vanilla creature equip logic treats TwoHandedWeaponLeft items as weapons and
    would drop the creature's real weapon, and the creature would fight differently depending on which game owns it.
26. **The base is vanilla; later data-mod edits of the same fields are overwritten.** The first `ObjectDB` pass is in
    the main menu, before any world or server sync; adopting later foreign edits as a new base would stack our
    multipliers on packs that already buff towers. The README says: do not combine with tower data packs.
27. **Sprint floor** (formerly L8): sprinting is never slower than jogging with a tower in hand, whatever the armor.
28. **Custom animations are limited to player attack triggers, checked at runtime, one hit per bash.** A trigger that
    never reaches an attack state would give free, endlessly repeated bashes or stuck triggers; a clip with two Hit
    events would double the bash. Triggers of held, aimed, reloaded or attached attacks are refused (a looping clip
    would never end: 2.7), and watchdog (C) stops any bash still running 3 s after its attack state.
29. **A tower put away does not slow** (vanilla rule for every item: only items in hand count). **Flagged**: players
    can travel at normal speed with the tower on their back and draw it for fights; "slowed a lot" applies while it is
    in hand.

Decisions 30-34 (2026-09-30): user feedback after the first in-game test (build `3cda33b+dirty`; the quotes are in the
Goal). Decision 35: from the review of that change (stage 9).

30. **A real bash cooldown: a new bash starts at most 2 s after the start of the last one** (`BashCooldown`, server
    rule, 0-10 s). **Flagged (default).** "Cooldown is too short": until then the only limit was vanilla's, the next
    bash starting as soon as the 0.74 s punch ended. Options: (a) a minimum time between bash starts, refused in a
    `StartAttack` prefix; (b) a longer clip only (the swing speed of decision 32), which also makes the bash slower
    but leaves the rate tied to the animation choice (a Custom clip of 0.5 s would spam again); (c) extra stamina per
    bash in quick succession. Choice (a), together with (b): the rate is the same for every animation and does not
    depend on the look. 2 s is the rate of the Iron mace's secondary attack (its clip runs about 2 s, 1.1), the vanilla
    move the bash now matches in stamina and stagger; with the slowed punch (about 1.1 s) it leaves about 0.9 s with
    the guard up between two bashes, and 20 stamina per 2 s is above the stamina regen, so bashing cannot go on for
    long. A press during the cooldown starts the bash as soon as the time is up, like the held button: a press once
    the swing is over is kept until then (vanilla's buffer alone keeps it 0.5 s, so a press between about 1.1 and
    1.5 s would have been dropped with no feedback while the guard is visibly back, 2.6); a press early in the swing
    keeps vanilla's 0.5 s. Other readings kept apart: the per-creature lock (decision 34) and the swing speed
    (decision 32).
31. **A bash stagger costs at least what a buckler parry stagger costs a middling parrier: `BashStamina` 20 (was 12),
    `BashStagger` stays 25.** **Flagged (numbers).** "Proccing the stagger should not be easier than with a buckler, so maybe increase
    the stamina consumption on the shield bash." In 1.0.16 a timed parry costs no stamina (Player
    `m_perfectBlockStaminaDrain` 0, buckler `m_perfectBlockStaminaRegen` 0) and staggers the attacker outright, but it
    needs the enemy's swing and a 0.25 s window, and a mistimed parry is a full-cost block with little armor (10
    stamina and 30 damage from a Draugr's sword through an Iron buckler): about 10 stamina per stagger for a player who
    lands one parry in two, about 20 at one in three (1.4). The bash needs no timing and no enemy swing, and one bash
    staggers the standard creatures of its tier: at 12 stamina it was cheaper than a middling parrier and cheaper than
    the Iron mace's secondary attack (20 stamina for about the same stagger). At 20 it costs what a one-in-three
    parrier pays, the same as the mace's secondary, 40-80 for a Troll, and it now has its own risks: the guard is down
    for the whole slowed swing (about 1.1 s, the hit 0.8 s after the press), a missed bash costs the full 20 (vanilla
    spends attack stamina when the swing starts), and bashes are 2 s apart. Lowering `BashStagger` instead would make
    the standard creatures of a tier need two bashes, against the Goal's "heavy stagger numbers"; the user's own
    suggestion was the stamina. At Blocking 100 the bash costs 13.4 (vanilla -33%) and staggers much more (2.6), as a
    skilled parrier misses less too. These numbers are per stagger, one creature at a time; in a group they hold
    because one bash staggers one creature at most (decision 35), as one parry staggers one attacker.
32. **The bash swing plays at 0.6 of its speed, hit included, and every player sees the same speed**
    (`BashAnimationSpeed`, server rule, 0.3-1.5). **Flagged (default, to tune in game).** "Is there a way to play them
    slower?" Options: (a) scale the clip's own `Speed` events of the running bash (`CharacterAnimEvent.Speed` prefix)
    and its start speed when no event came yet; (b) override the animator speed every tick; (c) a slower custom clip
    (L1). Choice (a): the clip keeps its own rhythm (a punch that speeds up for its strike still does), the Hit event
    moves with the clip, the hit-stop keeps working (it stores and restores the speed), a stagger's speed reset is never
    undone, and vanilla already sends the owner's animator speed to every other game through the ZDO (1.10), so remote
    players, with or without the mod, see the same slow swing with no new RPC. (b) would fight the clip's events, the
    hit-stop and the stagger every tick. 0.6 turns the punch's ×2 into ×1.2: the hit about 0.8 s after the press
    (0.5 before), the swing about 1.1 s (0.74 before); 0.5 would play the clip at its authored rate (hit about 0.95 s);
    the user tunes it after trying (T34). The speed goes back to the clip's own when a bash is cancelled mid-clip, and
    vanilla sets it back to 1 at the end of the clip.
33. **`ShieldUp` removed; `ShieldPunch` is the default.** "ShieldUp and ShieldPunch seem to be the same animation but
    also seem to be the best looking." The runtime data agree (1.10): both played `Punchstep 2` on the base layer with
    nothing on the upperbody layer, hit at the same 0.5 s and ended at the same 0.742 / 0.744 s, and the mid-swing
    screenshots show the shield in the same place: the forced `blocking` bool has no visible effect during the swing.
    Keeping it as an alias would keep a setting value that does nothing, plus the pose code (a bool written every tick,
    a possible block-pose flash at the end of the swing, one more thing to restore on cancel). Removal is allowed (0.1.0
    is unreleased): the enum value and the pose code are gone (rules layout 2), and `BashAnimation` is now a text
    setting with the list of the four names, so a config file that still says `ShieldUp` is read as `ShieldPunch`
    silently (the list puts an unknown value back to its first name) and offers only the four names. The list reads a
    name in any case (`kick` → `Kick`), as the enum setting did, and warns once about any other unknown value (2.7).
34. **The per-creature lock is 8 s (was 5 s) and is renamed `BashStaggerLock` (was `BashStaggerCooldown`).**
    **Flagged (optional now).** "Cooldown is too short" can also mean the only setting that was called a cooldown (the
    first build's config had `BashStaggerCooldown` = 5 and nothing else named a cooldown). With the first build (bashes
    0.74 s apart, no bash cooldown), 5 s and a Draugr's 2.55 s stagger (in-world run), one bearer kept a creature
    staggered about half the time. The new 2 s bash cooldown (decision 30) already lowers that on its own: bashes land
    at 0, 2, 4, 6 s, so with 5 s the first bash after the lock comes at 6 s, about 42% of the time (2.55 / 6). 8 s
    brings it to one bash stagger every 8 s, about 32%; the creature recovers and fights back for about 5.5 s between
    two bash staggers. So on top of the cooldown, the 20 stamina and the slower swing, the longer lock is a fourth step
    on the same stagger rate, which the user did not clearly ask for: 5 s is a fair choice too, and T21 decides. The
    rename keeps the two settings apart: `BashCooldown` is the time between the bearer's bashes, `BashStaggerLock` the
    time a creature is protected from another bash stagger.
35. **One heavy stagger per bash: only the creature nearest the middle of the swing keeps the stagger multiplier; the
    others in the arc take the bash's damage and push and stagger like a punch.** **Flagged.** Found in the review of
    decision 31: the bash hit every creature in its 60° arc with its full stagger (`m_multiHit` on,
    `m_lowerDamagePerHit` off, decision 4), while a buckler parry staggers only the attacker it parries. So in a group
    one 20-stamina bash with no timing staggered the whole front line (three Draugr side by side: all three, about 7
    stamina per stagger), and "not easier than with a buckler" held only for one creature. Options: (a) vanilla's
    multi-hit split (`m_lowerDamagePerHit`, as the unarmed attack and the mace secondary): each of N targets gets
    1 / (0.75 N) of the bash, 1.33 bashes' worth in total, so an Iron tower still staggers two Draugr at once (each
    50-110 against 50) and the push that holds a line back is split too; (b) the heavy multiplier on one target per
    bash, ×1 for the others; (c) keep the crowd stagger as a tower trait and say that the buckler comparison is per
    target. Choice (b): it is the only one that keeps the user's rule in a group ("proccing the stagger should not be
    easier than with a buckler"), and it keeps the crowd push of a wall. The target is the one the bearer faces (the
    smallest angle from the facing, the nearer one on a tie), not vanilla's first hit: vanilla sweeps the arc from
    one side, so its first hit is the creature furthest to that side. Done on the bearer's game (2.6), so it needs no
    RPC and works on creatures owned by any game; a creature in its lock limit can be the pick (the lock lives on its
    owner), and then that bash staggers nobody: turn to another creature (T36).

## 4. Multiplayer

**Who runs what.**

| Piece | Runs on |
|---|---|
| Equip rules, bash start, bash cooldown, stamina, swing speed, watchdogs, Braced effect, sprint floor | the bearer's game (owner of its player) |
| Slowed swing seen by others | every other game, through vanilla's animation speed sync (the owner's animator speed in the ZDO, 1.10); nothing of the mod runs there |
| Bash hit (`Attack.DoMeleeAttack`, `HitData` with blunt, ×25 stagger multiplier and skill Blocking); which creature of the swing keeps the ×25 (the others get ×1, decision 35) | the bearer's game; the hit goes to the victim's owner (`Character.Damage` → `RPC_Damage`) |
| Bash stagger rule (decoupled stagger, NG+ factor, lock limit) | the **creature's owner** (every game on a server with the mod; the dedicated server for creatures it owns). On a game without the mod: vanilla stagger from the landed damage (weaker) |
| Incoming hits: made-blockable, per-hit brace, knockback immunity, the block itself | the bearer's game (owner of its player runs `RPC_Damage`) |
| Tower data on items | every game, each on its own prefab and item copies, from the same (server's) rules |
| Back-slot look | the observer's game, from its own ObjectDB prefab |
| Join check, settings | the server (dedicated or host) |

**RPCs** (network version 2). The mod's own two are a copy of MC Forge Idol Upgrades' `ServerRules` and only carry
the rules sync:

- client → server `MC.Combat.Shields.TowerWall.SettingsRequest` (int layout) after the handshake
  (`ZNet.RPC_PeerInfo` postfix) and when the client's copy turns on while connected;
- server → client `MC.Combat.Shields.TowerWall.Settings` (`ZPackage`, layout 2): `Towers` (string),
  `BlockArmorMultiplier`, `BlockForcePercent`, `CarrySlowPercent`, `BraceSlowPercent`, `BraceStaggerResistPercent`,
  `BraceKnockbackResistPercent`, `BlockUnblockableAttacks`, `BashAnimation` (int: 0 ShieldPunch, 1 OtherPunch, 2 Kick,
  3 Custom), `BashCustomTrigger` (string), `BashAnimationSpeed`, `BashStamina`, `BashCooldown`, `BashStagger`,
  `BashStaggerLock`, `BashKnockback`, `BashRange`, `BashAngle`; sent as the answer, again to every ready player with
  the mod 0.5 s after the server's last settings change and when its copy turns on. Layout 1 (the first test builds:
  with `ShieldUp`, without the speed and the cooldown) is refused as an unknown layout; the network version rose with
  it, so such a build is refused at join (below) instead of waiting for rules it cannot read.
- Framework handshake (`NetworkGate`, shared by every Both mod, not written by this mod): client → server
  `<guid>.Hello` (network version, release version, and whether the client's own copy is on), server → client
  `<guid>.HelloAck` (the server's state, again whenever it changes), client → server `<guid>.HelloState` (`on` / `off`,
  when the client's copy turns on or off while connected). The join check reads it (below).
- The client refuses an unknown layout (keeps the last good rules; with none yet, it stays pending, below), clamps
  values to the config ranges (Warning), and uses the server's rules only for that `ZNet` session. Single player, host
  and dedicated server use their own config.
- **Pending policy** (the Combat mods' shared rule: a client's own gameplay settings never apply on a server with the
  mod; Creature Morale and Weapon Moveset follow the same rule): until the server's rules arrive, a
  client of a server with the mod applies **no tower data** (vanilla towers for that moment), so its own `Towers` list
  can never unequip an item at join. The arrival triggers the apply and `FixHands`. The rules are asked for in the
  `RPC_PeerInfo` postfix and applied about half a second after the answer, while the game spawns the character only
  once its spawn area has loaded (`Game.FindSpawnPoint`; at a logout point or a bed also after
  `m_respawnLoadDuration`, 8 s in code): so the rules normally arrive before the character exists and `FixHands` has
  nothing to do. The character then loads with the
  prefabs already carrying tower data, and a saved weapon next to a tower is sorted out by vanilla's equip order in
  `Player.Load` → `EquipInventoryItems` (the item later in the inventory stays), as when a character is loaded with
  the mod on; decision 15 (the weapon is put away) applies only when the rules arrive after the character is in the
  world.
- **Rules sync**: tower data is written into items, so a change of the rules in force must be pushed into them. The
  generation counter (2.0) is raised on every change; `TowerSync` applies it 0.5 s after the last change (2.0).
- **ZDO keys: none.** Nothing is stored on items, creatures or the player.
- `ModNetworkVersion` 2 (bump when the RPC names or the `Settings` layout change; 1 until layout 2, 2026-09-30).

**Server settings list**: every setting except `Enabled` (framework) and `AllowPlayersWithoutMod` (read by the
server only).

**Join check** (`PlayerCheck`, copy of Forge Idol Upgrades' check, with the verdict taken from the framework): the
server (dedicated or host, while its copy is active) waits 1 s after a peer is ready, then asks
`NetworkGate.PeerCompatible(peer)`: the player has the mod, the same `ModNetworkVersion`, and has not turned it off on
their game (a copy that failed to start counts as off). A player who is not compatible is refused: vanilla `Error` RPC
with `ErrorVersion` (their game shows "Incompatible version"; disconnected 4 s later through vanilla's kick list),
and the server log names the reason from `NetworkGate.PeerProblem(peer)` ("does not have the mod", "has another
version of the mod (network version n, the server has 2)", "has the mod turned off"). With `AllowPlayersWithoutMod`
on, such a player is let in with a Warning naming the same reason. `PlayerCheck.Start` subscribes to
`NetworkGate.PeerStateChanged` and `Stop` unsubscribes; the handler calls `Schedule(peer)` with the same 1 s grace, so
a player who turns the mod off while connected is refused about a second later (and one who turns it back on within
that second is not). Never the host's own player (not a peer). When the server's own copy turns on again,
`PlayerCheck.Start` checks every connected player after the grace. A client whose copy went inactive only because the
server's copy was off still reports "on" (the framework reports the client's own state, not the network verdict), so
it is not refused when the server's copy comes back.

**Hand-off and mixed cases:**

| Situation | Result |
|---|---|
| A tower given to (or dropped for) a player without the mod (allowed by the server) | A normal vanilla one-handed tower for them: their game builds the item from its own prefab. Nothing is stored on the item. Back in a modded player's hands it is two-handed again. |
| A player without the mod looks at a modded bearer | Sees the tower in the left hand and in the back shield slot (their prefab is Shield type), the bash animation (vanilla trigger broadcast) at the bearer's slowed speed (vanilla animation speed sync, 1.10), the block pose. A trigger that failed on the bearer's game may stay set on theirs (2.7). |
| A modded bash hits a creature owned by a game without the mod, or a vanilla PvP player | Their vanilla `RPC_Damage` applies the blunt and the ×25 multiplier to the landed damage: less stagger than on a modded owner, no lock limit. The other creatures of that swing arrive with ×1 (picked on the bearer's game) and stagger like a punch there too. |
| One bash hits creatures owned by several games | The bearer's game picks the one that keeps ×25 before sending (decision 35); each owner applies its own part (the pick: the bash stagger rule and its lock; the others: vanilla ×1). |
| A player without the mod hits a modded bearer | Resolved on the bearer's game: tower block, made-blockable rule, brace and knockback immunity all apply. |
| Two tower players bash the same creature | The creature's owner applies the lock limit for both: they cannot alternate to keep it staggered. The bash cooldown is each bearer's own (it limits one player's bashes, not the creature). |
| Modded player on a server without the mod | The framework turns the mod off for the session (ServerMissing): towers revert to vanilla (G9 path). |
| The server turns the mod off | Every client's copy turns off with it: towers revert everywhere, the Braced effect goes, a running bash is cancelled; back on: clients re-apply and ask for the rules again, and every connected player is checked again after the 1 s grace (their copies still report "on", so modded players stay). |
| A player with the mod installed but turned off on their own game | Refused like a player without the mod, about a second after joining or after turning it off while connected (decision 24), unless `AllowPlayersWithoutMod`; then they play with vanilla towers. |
| A player whose copy has another network version | Refused like a player without the mod, about a second after joining, unless `AllowPlayersWithoutMod`; then their own copy stays inactive ("cannot talk to each other") and their towers are vanilla. |
| Players with different own configs | Everyone on a server with the mod uses the server's rules; own files untouched; nothing is unequipped at join because of a client's own list. |
| Ownership changes | No ZDO state; a creature's stagger bar and bash-stagger time live on its current owner (the time is lost on a change: at most one extra bash stagger). |
| Dedicated server | Loads the mod (Both has no `BepInProcess`): join check, rules, and the bash stagger rule for creatures it owns. Its ObjectDB edits are harmless (no player equips there). |

`ModMultiplayerNotes` (csproj) gets two more sentences: "Tower shields handed to a player without the mod are normal
one-handed shields for them. A player whose copy of the mod is turned off, or cannot talk to the server's version, is
refused like a player without it."

## 5. Config

File `BepInEx/config/MC.Combat.Shields.TowerWall.cfg`. Every value applies live (about half a second after the last
change; on the clients of a server about a second: the server's push delay plus the client's apply delay). "Server" =
in multiplayer the server's (or host's) value is used by everyone (4).

| Section | Key | Default | Range | Server | Description (user-facing) |
|---|---|---|---|---|---|
| General | Enabled | `true` | on/off | framework | Turn the mod on or off. Takes effect immediately. |
| General | Status | — | — | — | Written by the mod: whether it is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | on/off | server only | Used only by the server (or the host). Off: a player whose game does not have this mod, has it turned off, or has a version that cannot talk to the server's, is refused about a second after joining, or after turning it off ("Incompatible version"), so everyone plays with the same tower shields. On: such players may play; for them tower shields are normal. |
| Tower shields | Towers | `ShieldWoodTower:6, ShieldBoneTower:8, ShieldIronTower:12, ShieldSerpentscale:15, ShieldBlackmetalTower:20, ShieldFlametalTower:28, ShieldGoldTower:32` | shield prefab names; bash damage 0-200 | yes | Which shields are tower shields (prefab names, as used by the spawn command), each with the blunt damage of its bash after a colon (10 when left out). Only shields are accepted; a shield that can parry loses its parry. |
| Tower shields | BlockArmorMultiplier | `2.5` | 1-10 | yes | Tower shield block armor is multiplied by this (also the armor gained per quality level). |
| Tower shields | BlockForcePercent | `100` | 0-200 | yes | How hard a block pushes the attacker back, in percent of the tower shield's own block force. Because tower shields block more of each hit, 100 already pushes somewhat harder than a normal tower shield would; lower keeps attackers within bash range. |
| Tower shields | CarrySlowPercent | `30` | 0-40 | yes | How much slower you jog while a tower shield is in your hands (sprinting is slowed 1.5 times as much, but never below jogging speed). Not while it is put away on your back. Armor slowness adds to it. The normal game value is 10. |
| Bracing | BraceSlowPercent | `30` | 0-90 | yes | Extra slowdown while you block with a tower shield, on every movement speed and on turning. |
| Bracing | BraceStaggerResistPercent | `80` | 0-100 | yes | While you block with a tower shield and have stamina for at least one more full block, you take this much less stagger, so blocked hits do not break your guard. |
| Bracing | BraceKnockbackResistPercent | `100` | 0-100 | yes | Under the same conditions, hits and area attacks from the front push you back this much less (100 = not at all). |
| Bracing | BlockUnblockableAttacks | `true` | on/off | yes | While you block with a tower shield, enemy attacks from the front that normally cannot be blocked (poison clouds, sprays, area attacks) are blocked too. Spells of your allies (such as the Staff of Protection's shield) are never blocked. |
| Bash | BashAnimation | `ShieldPunch` | ShieldPunch, OtherPunch, Kick, Custom (a text with this list, read in any case; any other value is read as ShieldPunch, silently for the old `ShieldUp`, else with a Warning) | yes | Animation of the shield bash. ShieldPunch: a punch with the shield arm (the arm that holds the shield). OtherPunch: the other arm. Kick: a kick. Custom: the animation named in BashCustomTrigger. If the chosen animation cannot land its hit or does not end, the mod switches to another one and says so in the log: Custom to ShieldPunch, then OtherPunch; Kick to OtherPunch. Names can be written in any case; any other value is read as ShieldPunch (with a warning in the log). In multiplayer the server's choice is used. |
| Bash | BashCustomTrigger | `` (empty) | a player attack animation name | yes | With BashAnimation = Custom: the attack animation to play (for example throw_bomb, spear_poke, mace_secondary). Names that are not player attack animations, and animations of attacks that are held, aimed or reloaded (such as staff_rapidfire, bow_fire, crossbow_fire), fall back to ShieldPunch and are reported in the log. |
| Bash | BashAnimationSpeed | `0.6` | 0.3-1.5 | yes | Speed of the bash animation, as a multiple of its normal speed (1 = as fast as a normal punch). Lower is slower: the whole swing, its hit included, takes longer, and other players see the same speed. |
| Bash | BashStamina | `20` | 1-50 | yes | Stamina cost of a bash, spent when the swing starts, whether it hits or not (the Blocking skill lowers it by up to a third). |
| Bash | BashCooldown | `2` | 0-10 | yes | Shortest time between the starts of two bashes, in seconds. Pressing attack before the time is up (once the swing is over, or in its last half second) starts the next bash as soon as it is. 0 = the next bash can start as soon as the previous one ends. |
| Bash | BashStagger | `25` | 2-50 | yes | How much a bash fills a creature's stagger bar, as a multiple of its blunt damage before the creature's armor (only for the enemy nearest the middle of the swing; the others are staggered as by a punch). The heaviest normal-game attacks use 6. |
| Bash | BashStaggerLock | `8` | 0-30 | yes | Seconds after a bash staggers a creature during which bashes add no heavy stagger to it (for every player), so it recovers and fights between staggers. 0 = no limit. |
| Bash | BashKnockback | `40` | 0-200 | yes | How far a bash pushes enemies (grows with the Blocking skill). |
| Bash | BashRange | `1.8` | 1-3 | yes | Reach of the bash in meters. |
| Bash | BashAngle | `60` | 10-180 | yes | Width of the bash arc in degrees. Every enemy in it takes the bash's damage and push, but only the one nearest the middle of the swing takes its heavy stagger. |

Order in ConfigurationManager: General, Tower shields, Bracing, Bash; `Enabled` first (framework).

Changed after the first in-game test (decisions 30-34; 0.1.0 is unreleased, so no migration): `BashAnimationSpeed`
and `BashCooldown` are new; `BashStamina` 12 → 20; `BashAnimation` lost `ShieldUp` and its default is `ShieldPunch`
(a file that still says `ShieldUp` is read as `ShieldPunch`); `BashStaggerCooldown` (5) is now `BashStaggerLock` (8)
(BepInEx keeps the old key in an old file as an orphaned entry and nothing reads it).

## 6. Compatibility

### 6.1 Sibling mods (same run)

- **Weapon Moveset** (`MC.Combat.Weapons.Moveset`): its `Attack.Start` prefix must leave alone attacks whose
  **`weapon` argument** (not `__instance.m_weapon`, which `Start` sets later, 1.3) has `m_shared.m_skillType ==
  Blocking` (the bash is a Horizontal melee Attack with 0 chain levels, so a type filter alone would take it), and must
  not offer jump or roll attacks with a tower in hand. Otherwise a jump or roll move would replace the bash. Its design
  doc already gates on `weapon.m_shared.m_skillType` Blocking and on `ItemKinds` Shield (its 2.2 and cross test X03),
  which the `ItemKinds` rule of 7.5 (already in `src/Shared`) makes true for towers. Contract on stagger: the bash's
  stagger multiplier is `BashStagger` (×25 by default, range 2-50), and this mod applies it on the creature's owner
  (2.6); Moveset never adds its own stagger multiplier to a hit with skill Blocking. Contract on `StartAttack` (both
  mods prefix it): during the bash cooldown our prefix skips vanilla with `__result = false` (2.6), as Moveset's own
  prefix does for a move still starting; Moveset acts on `__result` only and clears its call state in a finalizer, so
  it tolerates the skip (its patch comment says so). Contract on animation speed: our `CharacterAnimEvent.Speed`
  prefix scales the events of the local player's running bash only; if Moveset ever scales speeds of its own moves,
  the two never touch the same attack. Roll flow: Moveset's roll attack flows out of the late roll (it opens vanilla's
  `InDodge` gate of `StartAttack` for that one call, only for a move it decided on, never before the roll's
  invulnerability ends); a tower never gets a move, so the gate stays shut and a bash pressed late in a roll starts,
  as in vanilla, once the roll is over (C04). Its `ZSyncAnimation.RPC_SetTrigger` postfix cross-fades another
  player's roll-attack trigger out of the roll on this game's copy of that player; its trigger list (its roll-attack
  settings) holds by default `unarmed_attack1` (the bare-hand roll attack), which is also the ShieldPunch bash's
  trigger (the same for OtherPunch's `unarmed_attack0` or a Custom trigger if one of its settings names it). The
  bearer's game only starts a bash after the roll, so at most a copy that still shows the end of the roll (lag)
  starts the bash punch from there: cosmetic, the same timing as on the bearer's own game.
- **Dual Wielding** (`MC.Combat.Weapons.DualWield`): leave TwoHandedWeaponLeft items (towers, blood staffs) to vanilla
  `EquipItem`: never keep an off-hand weapon next to one, never re-equip one after a tower. A "shield" test by
  `ItemType.Shield` no longer sees towers; use `ItemKinds.Classify` (its shield rule is already in `src/Shared`) if it
  needs to know. Its `Attack.Start` prefix only acts on weapon pairs, read from the `weapon` argument, never on a
  tower. Our `Attack.OnAttackTrigger` prefix skips hit events only for our own bash clone, never for its pairs, and our
  `Attack.AddHitPoint` postfix and `Character.Damage` prefix act only inside our clone's Hit event (its
  `DoMeleeAttack` patches act only on its own clone). Its
  `StartAttack` prefix runs first (`Priority.First`) and may skip vanilla too; ours, which may skip vanilla during the
  bash cooldown, only acts when a listed tower is in the left hand, which a weapon pair never is. Our
  `CharacterAnimEvent.Speed` prefix never scales a pair's clips (only our running bash clone's). Its swap key is a
  queued vanilla equip (`Player.UpdateActionQueue` patches) that only swaps a pair's two weapons, so it never moves a
  tower. Its crossed sheathed pair (`VisEquipment.SetBackEquipped` postfix, setting `CrossSheathedPair`) acts only
  when both back items are pair weapons; a tower put away is a hidden left item with no right one, so it stays in the
  back shield slot where vanilla puts a shield (`tower.hands` screenshot).
- **Creature Morale** (`MC.Combat.Creatures.Morale`): contract. Both mods prefix `Character.RPC_Damage` on the
  creature's owner. Ours changes only bash hits on non-players (it stores their stagger and sets `m_staggerMultiplier`
  to 0) and, on its own bearer's game, `m_blockable` of frontal attacks from hostile attackers on that player (Morale
  does not patch `BaseAI.IsEnemy`, which our hostility test reads); the bash's stagger is then
  **applied by this mod** after `ApplyDamage` (2.6), not by vanilla. Morale reads `GetTotalDamage()` before resistances
  and the attacker, which we never change, so the order does not matter. Morale makes an outclassed creature
  **afraid** of a player (the player has helped kill the boss two bosses past the boss of the creature's biome, elites
  one more; biome = the harder of its home biome and where it spawned): it runs away when that player comes within
  `FearRange` (12 m) and it senses him, and fights back when he hits it, lands a projectile near it, or corners it
  (stays within 3 m of it for 2 s); no creature ignores a player any more (its former "calm" state is gone). A bash
  carries blunt damage before resistances (also in NG+, where armor absorbs almost all of it), so it provokes like any
  hit: the afraid creature stops running and fights the bearer. To bash one, the bearer sneaks up on it or catches it
  before cornering it. The bash has backstab ×1, so Morale's sneak-attack rule (E3: an afraid creature that sees the
  attacker gives no sneak-attack bonus; one that cannot see him still does) never applies to the bash itself; and
  because a bash that staggers also alerts the creature (2.6), the hit that follows is not a sneak attack either. Cross
  test C06 = their X05.
- **Sneak Ambush** (`MC.Combat.Sneak.Ambush`): the bash has backstab ×1, so **a bash never backstabs and never pays
  sneak-attack XP** (Sneak pays XP only when a vanilla backstab happens, `hit.m_backstabBonus > 1`). The carried slow
  does not change crouch speed (vanilla), and blocking ends crouch (vanilla `Player.UpdateCrouch`). Contract for NG+:
  a bash that staggers a creature but lands 0.1 damage or less still runs vanilla's `m_onDamaged` once (2.6), so
  `MonsterAI.OnDamaged` alerts the creature and Sneak's reveal (its `OnDamaged` postfix) sees the bearer; "bash, then
  knife" gives no backstab and no Sneak XP. Cross test C08 = their X02 (C08 also covers the NG+ case).

### 6.2 Other MC mods

- **Crossbow Stays Loaded**: towers now take the weapon path of `Humanoid.UnequipItem` (its prefix) and call
  `ResetLoadedWeapon`; a crossbow is never held together with a tower. Its `BlockAttack` postfix is unaffected (we do
  not patch `BlockAttack`). Its `Attack.OnAttackTrigger` postfix still runs when our prefix skips an extra bash hit
  event; it only acts on crossbows. Cross test C02.
- **Forge Idol Upgrades**: refining a tower keeps our data (its success keeps the item object; vanilla re-creation
  instantiates the edited prefab). Its `ObjectDB` and `GetTooltip` postfixes only touch idols. Cross test C03.
- **Harpoon Hooks Tames**: also prefixes and postfixes `Character.RPC_Damage` on creature owners; ours acts only on
  hits with skill Blocking (the harpoon's are not) and on the local player; no order needed. It also prefixes
  `Character.Damage` on the thrower's game (harpoon hits only, `Priority.Last`); ours there changes only the stagger
  multiplier of our own bash's hits (decision 35): no overlap.
- **Sort Chest, Crafting Search and Sort, Compendium**: get the `ItemKinds` shield rule (7.5, already in
  `src/Shared`) at their next rebuild (each DLL compiles its own copy); a DLL built before it files towers under
  weapons (skill Blocking).
- **Forge Idol Upgrades, Breeding, Lights** (existing Both mods): their join checks still decide with
  `NetworkGate.PeerHasMod`; this mod's check uses `PeerCompatible` (4). Nothing shared at runtime: each mod refuses by
  its own rule.
- **Stats Per Creature**: a bash kill is a normal kill (melee kill modifier); nothing to do.

### 6.3 External mods (detect and warn; no stand-down in v1)

`TowerGuard` logs one Warning per session at the first ObjectDB pass with items (the main menu, when every plugin
has loaded and patched; also from `OnActivated` when the ObjectDB already has items, i.e. the mod was turned on
later): the plugins below when present (`BepInEx.Bootstrap.Chainloader.PluginInfos`), and any other Harmony owner (not
`MC.*`) with a prefix or transpiler on `Humanoid.BlockAttack`, a prefix on `Humanoid.StartAttack`, or any patch on
`Humanoid.GetCurrentWeapon` (found through `Harmony.GetPatchInfo`, so it works without known GUIDs).

| Mod | GUID | Effect with this mod |
|---|---|---|
| CaptainValheim (sighsorry) | `sighsorry.CaptainValheim` (unverified, author pattern) | Its shield strike, throw, charge, reflection and block-charge setup test `ItemType.Shield`: they stop applying to towers and keep working on round shields. A shield strike with skill Blocking and a stagger multiplier between 1 and 100 gets our creature stagger rule and lock limit. |
| SecondaryAttacks (sighsorry) | `sighsorry.SecondaryAttacks` (verified in its source) | Its last-shield auto-equip keys on `ItemType.Shield`: towers are ignored (no equip loop). |
| ZenCombat (ZenDragon) | unverified (closed source) | Tower block charges: forced off on our towers (decision 11). Reliable block (towers): overlaps with our stagger resistance, compatible in effect. Auto-shield ignores towers. |
| Goo's Combat Overhaul (gnls) | unverified (closed source) | Has its own tower family (hold rules, penalties); it may stop treating TwoHandedWeaponLeft towers as towers. README: use one of the two for tower shields. |
| ShieldBash (Mexanik) | unverified | A second bash on its own key; which shields it accepts is not documented. Its hits, if skill Blocking, get our creature stagger rule. README: pick one. |
| ReliableBlock (Korppis), ReliableBlockRebuilt (RYEO) | unverified | Keep a staggered block mitigating; compatible in effect. RBR turns itself off when any other mod patches `BlockAttack` (MC Crossbow Stays Loaded does). |
| Make Tower Shields Great Again (WackysDatabase data pack), other WackysDatabase edits of towers | WackysDatabase: `WackyMole.WackysDatabase` (unverified) | Edits the same tower values after the main menu; our values overwrite theirs on the fields of 2.0.1 at every pass and heal (decision 26). README: do not combine. |
| WeaponArts (j1gA) | unverified | Detects towers as `ItemType.Shield` with low parry bonus: its tower art (Taunt) stops working on our towers. |
| ShieldMeBruh (Vapok), SmartShield, AutoShield | `vapok.mods.shieldmebruh` (from SecondaryAttacks' source) | Auto-equip shields by `ItemType.Shield`: towers are ignored. |
| EpicLoot (RandyKnapp) | `randyknapp.mods.epicloot` (unverified) | Its enchant rules by item type may treat towers as two-handed weapons (unverified); magic already on a tower stays in its custom data. |
| Combat Adjustments (Mushroom_Vikings) | unverified | Classifies towers as `ItemType.Shield` + parry bonus: its tower stagger-bar bonus stops on our towers. |
| NPC / creature mods that give player shields to creatures | — | Those creatures keep vanilla one-handed shields (decision 25). |

## 7. Implementation plan

### 7.1 Files

- `MC.Combat.Shields.TowerWall.csproj`: add `<ModIdea>Better tower shields</ModIdea>`,
  `<ModNetworkVersion>2</ModNetworkVersion>` (1 until the rules layout 2), the sentences of 4 in
  `ModMultiplayerNotes`.
- `Plugin.cs`: config binding (5); `SettingChanged` on any rule → `ServerRules.OwnChanged()`; an
  `Application.quitting` flag; `OnActivated` / `OnDeactivated` (7.3).
- `TowerRules.cs`: snapshot of every rule (`Own()`, `Write`, `TryRead` with clamping, `Layout = 2`, `Describe()`),
  parsed tower list, the bash animation enum and the list of its names for the config; `InForce` (2.0).
- `ServerRules.cs`: copy of Forge's (rules sync only), plus the generation counter, the 0.5 s push delay and the
  "waiting for the server's rules" state (pending policy, 4; Debug `TestRules` override).
- `PlayerCheck.cs`: copy of Forge's join check with the verdict from `NetworkGate.PeerCompatible`, the reason from
  `NetworkGate.PeerProblem` in the log lines, and the `NetworkGate.PeerStateChanged` subscription (4).
- `TowerCatalog.cs`: resolves the list to prefabs for the current ObjectDB (MISSING / not-a-shield / parry Warnings),
  vanilla snapshots per prefab name (process lifetime), `TowerOf(ItemData)` lookup, the cached held-tower verdict.
- `TowerData.cs`: `Apply` / `Revert` for one SharedData; `ApplyAll` / `RevertAll` (prefabs, local inventory, loaded
  containers, ground items; per-item isolation); `FixHands(player)`.
- `TowerSync.cs`: generation check, 0.5 s delay, apply on the main thread.
- `BashAttack.cs`: builds the bash `Attack` from the unarmed template and the rules; trigger resolution (`HasParameter`,
  the attack-trigger set for Custom), fallback state per rules generation; cancel.
- `BashWatch.cs`: the running bash, the bash cooldown (and the kept press), watchdogs (A), (B) and (C), the one-hit
  counter.
- `BashTarget.cs`: one heavy stagger per bash (the scope of a Hit event, vanilla's hit list, the pick, ×1 for the
  others; decision 35).
- `BashSpeed.cs`: the swing speed of the running bash (scaled `Speed` events, the start speed, putting the clip's own
  speed back).
- `BashStagger.cs`: the creature-owner stagger rule (stored hit, NG+ factor, lock-limit table).
- `Brace.cs`: the Braced `SE_Stats` template (created with `ScriptableObject.CreateInstance`, `HideAndDontSave`),
  upkeep, HUD fields, per-hit brace decision, knockback rule, cleanup.
- `TowerTooltip.cs`: tooltip lines.
- `TowerGuard.cs`: other-mod detection and the one Warning.
- `Patches/ObjectDBPatches.cs`, `Patches/HumanoidPatches.cs`, `Patches/CharacterPatches.cs`,
  `Patches/PlayerPatches.cs`, `Patches/ItemDataPatches.cs`, `Patches/AttackPatches.cs`,
  `Patches/CharacterAnimEventPatches.cs`, `Patches/ZNetPatches.cs` (copy + `TowerSync`), `Patches/PlayerDebugPatches.cs`
  (Debug only, 7.4).
- `SelfTests.cs` (Debug only).
- `README.md`, `CHANGELOG.md`, `TESTING.md`, `icon.png` (step 5 of the workflow).
- No `src/Shared` file: the shared changes this mod needs are already there (7.5).

### 7.2 Harmony patches

Every body catches its own exceptions and calls `PatchGuard.Report(site, e)`; no `?.` / `??` on Unity objects.

| Target | Kind | Priority | Purpose | Cost |
|---|---|---|---|---|
| `ObjectDB.Awake()` | postfix | Low | skip empty `m_items`; `TowerCatalog.Invalidate`; apply the rules in force (or vanilla while waiting) to prefabs; first pass: `TowerGuard` check | per scene load |
| `ObjectDB.CopyOtherDB(ObjectDB)` | postfix | Low | same | per scene load |
| `ObjectDB.GetAllCraftableWeapons()` | postfix | normal | remove tower prefabs from the returned list | rare (cached list) |
| `Humanoid.EquipItem(ItemDrop.ItemData, bool)` | prefix | High | player: heal a tower item before vanilla reads its type; non-player: revert a listed tower copy | per equip |
| `Humanoid.StartAttack(Character, bool)` | prefix (bool) | normal | local player with a tower in the left hand: primary attack within the bash cooldown → `__result = false`, vanilla skipped, and a press buffered once the swing is over kept until the cooldown ends (`m_queuedAttackTimer`, 2.6); else heal (sets the current bash `m_attack`, builds it from `__instance.m_unarmedWeapon`) | per attack press / held-attack tick: two reference compares when not a tower; two more and a float compare while the cooldown runs (plus `InAttack()` while a press is buffered) |
| `Humanoid.StartAttack(Character, bool)` | postfix | normal | `__result` and a bash: remember the running bash, start the cooldown, begin the swing speed, start watchdog (A) | same |
| `Humanoid.UpdateBlock(float)` (private) | postfix | normal | local player only: Braced upkeep (add / re-add / remove, speed and stagger modifiers, HUD fields), watchdogs, the swing's start speed when its attack state is reached | every FixedUpdate per owned humanoid; one reference compare for others; no allocation (while the brace holds, one pass over the player's status effects for the stagger floor) |
| `CharacterAnimEvent.Speed(float)` | prefix | normal | the local player's running bash only: multiply the clip's speed value by `BashAnimationSpeed` (2.7) | a few animation events per clip, every character; one static read when no bash is slowed |
| `Humanoid.Pickup(GameObject, bool, bool)` | prefix | normal | player: `ref bool autoequip` = false for a tower; non-player: revert the item's copy | per pickup (one `GetComponent`) |
| `Character.RPC_Damage(long, HitData)` (private) | prefix | normal | local bearer: per-hit brace decision, make frontal attack hits blockable (2.5); non-player victim and bash hit: lock limit, store the hit and its stagger, multiplier 0 (2.6) | per hit; one enum compare and one reference compare otherwise |
| `Character.RPC_Damage(long, HitData)` (private) | finalizer | normal | put back the stored hit and the per-hit brace value saved by the prefix (also when `RPC_Damage` or another patch throws) | per hit; two field writes |
| `Character.ApplyDamage(HitData, bool, bool, HitData.DamageModifier)` | prefix + postfix | normal | the stored bash hit on this character: add its stagger after vanilla, record a bash stagger; when vanilla returned at its damage check (landed 0.1 or less), call `m_onDamaged(landed, attacker)` once (2.6) | one reference compare per call |
| `Character.ApplyPushback(Vector3, float)` | prefix | normal | local braced bearer, frontal push, brace holds: scale the push | per push; one reference compare |
| `Player.GetRunSpeedFactor()` (protected) | postfix | normal | local player with a tower in hand: floor at jog speed | per movement update of players; one reference compare |
| `ItemDrop.ItemData.GetDamage(int, float)` | postfix | normal | world level > 0 and an applied tower copy: damage without the world-level term | per damage read; one float compare in normal worlds |
| `ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int, bool)` (static) | prefix + postfix | normal | heal; append the tower lines | per tooltip build |
| `Attack.OnAttackTrigger()` | prefix (bool) | normal | the running bash clone only: count hit events, return false for the second and later; the first opens the one-stagger scope (`BashTarget`, 2.6) | per hit event; one reference compare |
| `Attack.OnAttackTrigger()` | finalizer | normal | close the one-stagger scope the prefix opened (also when `OnAttackTrigger` or another patch throws) | per hit event; one bool read |
| `Attack.AddHitPoint(List<HitPoint>, GameObject, Collider, Vector3, float, bool)` (private) | postfix | normal | the scope's clone only: keep vanilla's hit list of the sweep (2.6) | per ray hit of every melee attack; one reference compare |
| `Character.Damage(HitData)` | prefix | normal | scope open and a bash hit (bearer's game, before the RPC): pick the creature nearest the middle of the swing at the first one, `m_staggerMultiplier = 1` on every other (2.6, decision 35) | per hit sent; one static read when no bash Hit event runs |
| `ZNet.OnNewConnection`, `ZNet.RPC_PeerInfo`, `ZNet.Update` | postfix | normal | copy of Forge's network patches; `Update` also drives `TowerSync` and the delayed push | `Update`: two bool reads and one int compare when idle |
| `Player.SetControls(...)` (Debug only) | postfix | normal | self-test hooks: hold block while `SelfTestHooks.HoldBlock`, hold the attack button while `SelfTestHooks.HoldAttack` | two static bool reads |

No transpiler and no patch on `Humanoid.BlockAttack` (it keeps ReliableBlockRebuilt's own check and every block mod's
hooks untouched). Two bool prefixes skip a vanilla method: `Attack.OnAttackTrigger`, only for our own bash clone after
its first hit (2.7), and `Humanoid.StartAttack`, only for the local player's bash within the cooldown (2.6), the same
refusal vanilla gives while an attack plays; every vanilla or other mod's attack runs normally. The swing speed scales
the value of a vanilla animation event and never skips it. The one-stagger rule changes only the stagger multiplier of
our own bash's hits, in `Character.Damage` before they leave, and reads vanilla's hit list without touching it; no
transpiler is needed because `DoMeleeAttack` finishes its sweep (every `AddHitPoint`) before its first `Damage` call.
Other `Character.Damage` prefixes (MC Harpoon Hooks Tames: harpoon hits only; MC Dual Wielding: a Debug-only record)
touch other hits.

### 7.3 State, lifetime, live toggle

| State | Lifetime |
|---|---|
| Vanilla snapshots per tower prefab name | process (never re-taken) |
| Rules generation, applied generation, apply delay | process / per `ZNet` session (Forget raises it) |
| Tower catalog (prefab → entry), attack-trigger set for Custom | per ObjectDB and rules generation, rebuilt lazily |
| Bash `Attack` object, resolved trigger, fallback state | per rules generation (and player object for the trigger check) |
| Running bash clone, watchdog start time, hit-event count | per bash; dropped when `Player.m_localPlayer` changes |
| One-stagger scope: vanilla's hit list, the pick | one `Attack.OnAttackTrigger` call of our clone (closed by its finalizer) |
| Swing speed of the running bash (factor, the clip's own speed, "scaled by me") | per bash; ended with it, reset on deactivation |
| Last bash start (cooldown) | the local player object; cleared on deactivation and when a start never reached its attack state |
| Braced template | process; its fields follow the rules |
| Braced clone on the player, cached held-tower verdict | per tower in hand; dropped when `Player.m_localPlayer` changes |
| Stored bash hit and stagger (creature owner) | one `RPC_Damage` call |
| Bash-stagger time per creature | `ConditionalWeakTable` keyed by the `Character` (freed with it) |
| Per-hit brace value | one `RPC_Damage` call |
| Server rules | per `ZNet` session (Forge's `ServerRules`) |
| Join-check queue, `PeerStateChanged` subscription | while the server's copy is active (`PlayerCheck.Start` / `Stop`) |
| TowerGuard "warned" flag | process |

- **`OnActivated`** (patches already applied): build the catalog; `TowerData.ApplyAll` with the rules in force
  (vanilla while a client waits for the server's rules); `FixHands` on the local player; Braced ensured if a tower is
  held; `ServerRules.Start`; `PlayerCheck.Start` (subscribes to `NetworkGate.PeerStateChanged`, removing any earlier
  subscription first; the event only fires on a server, which also checks every connected player after the grace);
  `TowerGuard.Reset`; `SelfTests.Register`. Per-item isolation (2.0)
  keeps one bad item from putting the feature in Error.
- **`OnDeactivated`** (patches still applied during the call; each step in its own `try`/`catch`, in this order):
  `SelfTests.Unregister`; cancel the local player's running bash if it uses a tower (2.6), which also puts the clip's
  own speed back (2.7); remove the Braced effect (quietly); revert the local inventory (equipped items included);
  revert the prefabs; unless the application is quitting (plugins are destroyed at quit and call
  `OnDeactivated`), revert every loaded `Container` found with `Object.FindObjectsByType<Container>` and
  `ItemDrop.s_instances`; `PlayerCheck.Stop` (unsubscribes, clears the queue); `ServerRules.Stop`. Nothing is sent to
  the server: when this client's copy turns off, the framework tells the server itself (`HelloState`, 4). A tower
  held in the left hand becomes a one-handed shield in place; the right hand is empty, the attack button punches again
  (vanilla).
- What cannot be reached on toggle-off: nothing that matters. Items live only in inventories, containers and ground
  drops; stands keep an item name in their ZDO and rebuild the item from the prefab when taken.
- **No `[AlwaysOnPatch]` class**: the mod adds no item, prefab or ZDO state, and everything it writes is reverted on
  deactivation, so every patch follows the live toggle. The RPC handlers registered by the `ServerRules` copy stay on
  the connection after the toggle (ZRpc has no unregister): as in Forge, the server answers only while its copy is
  active, and a client only stores what arrives.
- **Rules apply** (own config, server, `TestRules`, session change; 2.0): new catalog, `ApplyAll` + `FixHands`, running
  bash cancelled if its tower left the list, bash rebuilt, fallback state reset, Braced template and clone updated. A
  running bash keeps the swing speed it started with; the cooldown reads the rules in force at the next press.

### 7.4 Debug in-world self-tests (`SelfTests.cs`)

Run in god mode in the throwaway world; every test cleans up in `finally` (removes spawned items and creatures,
unequips, puts back god mode, the `StaminaRate` and `WorldLevel` keys it changed, clears `SelfTestHooks` and the rules
override, and puts the player back where the test found him, facing the same way); never writes a `ConfigEntry`
(settings forced through `ServerRules.TestRules`).

**Bash lane.** Before a creature is put in front of the player for a bash, the test checks that nothing solid or alive
stands in the lane between them: a capsule 0.4 m wide from 0.5 m to 1.8 m above the player's feet (the band of the
bash rays: 1 m up, at -0.3, 0 and +0.3, 0.4 m wide), cast over the player's radius plus the creature's width plus the
0.3 m gap, on the layers vanilla melee rays stop at plus the terrain, over a fan of ±10° (the group bash: the side
creature's angle + 10°); and the ground at the lane's end is at most 0.8 m below the player's feet (a creature put
over a drop would fall below the rays). If the lane is not free, the player is turned to the nearest free one, 20°
steps both ways (NOTE "bash lane: turned the player ..."; no free lane at all is a NOTE too). Vanilla
`Attack.DoMeleeAttack` stops each ray at the first collider it meets (`m_hitThroughWalls` off), so a rock between the
two takes the bash, and each Punchstep clip steps the player forward (root motion, `CharacterAnimEvent.OnAnimatorMove`):
after many bashes the player can stand right in front of a rock. A Hit event after which the target took nothing
names what stands in the lane in its NOTE.

- **`tower.data`**: every default tower prefab exists and after activation has the 2.0.1 values (type, override,
  parry ≤ 1, parry adrenaline 0, block armor = snapshot × 2.5, block force, movement -0.30, blunt from the list,
  blockable/dodgeable, block charges off); `FW_ShieldBlackmetalTower`, `SP_ShieldBlackmetalTower`, `ShieldBanded` and
  `ShieldIronSquare` unchanged; `ItemKinds.Classify` = Shield for each tower; `GetAllCraftableWeapons` has no tower;
  the Gold tower's tooltip has no parry-adrenaline line and has "Two-handed", "Cannot parry", the bash stagger and the
  bash cooldown lines; the bash `Attack` plays `ShieldPunch` and costs 20; a tower copy's `GetDamage(1, 1)` equals its
  blunt from the list (no NG+ term). NOTE lines: EffectList counts of each tower (`m_hitEffect`, `m_blockEffect`,
  `m_startEffect`, `m_triggerEffect`) and of `PlayerUnarmed`; every `ItemStand` prefab's `m_supportedItems` count and
  `m_supportedTypes` (answers 9); whether `ObjectDB.m_recipes` has a recipe for `ShieldIronSquare`; the
  `m_movementModifier` of every armor item (for the sprint floor).
- **`tower.hands`**: add `ShieldIronTower`, `SwordIron`, `Torch`, `ShieldBanded`; sword + banded, then tower → only
  the tower (left); tower then sword → only the sword; tower then torch → only the torch; tower then banded → banded;
  `HideHandItems` with the tower → back item parented under `VisEquipment.m_backShield` (screenshot); drop a tower and
  `Pickup` it with the banded shield held and an empty right hand → banded still equipped; drop it again and `Pickup`
  it with both hands empty → not equipped; sword hidden (`HideHandItems`) + tower, `FixHands` → hidden slot empty and
  no right back item; a spawned `Skeleton` gets a `ShieldIronTower` through `PickupPrefab` → its copy is Shield type,
  not `IsWeapon()`, and its own weapon stays equipped.
- **`tower.bash`**: equip `ShieldIronTower`; spawn a `Troll` (threshold 180, above one bash's maximum 165) just in
  front (capsules 0.3 m apart, in a free bash lane), re-placed before each bash; every press waits until the player is
  idle and out of the bash cooldown. The
  default (`ShieldPunch` at speed 0.6), then `ShieldPunch` at speed 1, `OtherPunch`, `Kick`, then `Custom` =
  `throw_bomb`: `StartAttack` returns true; NOTE whether `InAttack()` came within 0.3 s, whether the hit came, the
  option in use after any fallback, the delay from press to hit, the time to the clip's end, the clip names on both
  animator layers 0.2 s after the press, the animator speed, the clip's own speed and the ZDO speed at that moment,
  the speed after the swing, and a screenshot mid-swing. Asserts: with the default settings a bash lands within two
  presses (the first may fall back); `OtherPunch` reaches `InAttack()` within 0.3 s (FAIL otherwise: it is the last
  resort); on each hit, the Troll's stagger gained is between 0.25 × 12 × 25 and 0.55 × 12 × 25 and its health lost is
  at most 12 × 0.55. Swing speed: 0.2 s after the press the animator plays at the clip's own speed × the factor (the
  punch clip's own speed is 2; the kick's start is scaled by the mod, its own speed 1), the ZDO holds the same value
  (what other games apply), and after the swing the speed is 1 again; the slowed punch hits 1.4-1.9 times later than
  at speed 1 and ends later too (NOTE both timings, and whether the punch clip's first `Speed` event came before or
  after the attack state was seen). Custom `emote_wave` and a made-up name are refused at resolution (Warning, the
  resolved trigger is `unarmed_attack1`); Custom `dualaxes3` (two Hit events): at most one hit per bash (the counter
  shows the skipped event); the no-end check allows 3 s / speed. Then one bash staggers a `Draugr` (threshold 50).
  Cooldown through the vanilla entry points: a `StartAttack` right after a bash (inside the 2 s) returns false and
  starts nothing, one after the cooldown returns true; one press (`m_queuedAttackTimer` = 0.5, what vanilla sets on a
  press) once the swing is over, more than 0.5 s before the cooldown ends, starts the next bash 2.0-2.1 s after the
  last one (NOTE the gap); the attack button held through the vanilla input (Debug hook
  `SelfTestHooks.HoldAttack`) for 4.6 s gives 3 bash starts, 2.0-2.15 s apart (NOTE the gaps); with `BashCooldown` 0
  a press right after the swing is accepted. Stamina: with the `StaminaRate` key removed and full stamina, one bash
  costs 20 with the vanilla attack rules (equipment, effects, -33% × Blocking factor, world rate).
- **`tower.lock`, `tower.ng`, `tower.toggle` use `BashAnimation = OtherPunch` on purpose**, not the default
  ShieldPunch: it is the last option of the fallback chain, so no watchdog verdict can change the clip in the middle of
  a stagger measure, and the stagger, the lock and the NG+ factor are the same whatever the clip. The default and every
  other option are `tower.bash`'s part.
- **`tower.lock`**: `Draugr` in front, Iron tower, `BashStaggerLock` 8 (default), with `BashCooldown` 0 and speed 1 so
  the lock alone is measured: the first bash staggers it; bashes every 0.8 s for 6 s never raise its stagger bar by
  more than the landed blunt × 1; a bash 8.5 s after the first staggers it again; NOTE how long its stagger animation
  lasts; `BashStaggerLock` 0: a second bash right after the first, from an empty bar, fills it again (the bar stops at
  the threshold, so only a heavy bash can fill an empty Draugr bar; a ×1 bash adds about 13 at most). One heavy
  stagger per bash (decision 35): a second `Draugr` beside the first (same distance, capsules 0.1 m apart; arc 160°
  so both are hit), on the right, then on the left (vanilla's sweep hits it first once, last once): each time the
  middle one is the pick and its bar fills, the side one loses health and its bar rises by the landed blunt × 1 at
  most (NOTE the angle, the pick and both bars). A bash on a creature already staggering starts no lock.
- **`tower.ng`**: global key `WorldLevel` 1 (put back after), a new `ShieldIronTower` and a `Draugr` spawned after the
  change: the tooltip's blunt is the list value; a bash raises the Draugr's stagger bar by the NG+ health factor
  (world level × `m_worldLevelEnemyHPMultiplier`, 2.5 at runtime) × (0.25-0.55) × 12 × 25, measured with the Draugr's
  stagger factor raised ×4 for that bash (its real threshold is 50 × 2.5 = 125, below the full stagger); a second bash
  at the real threshold staggers it; NOTE the health it lost. Alert: the Draugr's view and hearing ranges are set to 0 for the
  test (only a hit can alert it) and it is not alerted before the bash; right after the bash it is alerted and targets
  the player (2.6); NOTE whether the landed damage was 0.1 or less (our postfix made the `m_onDamaged` call) or above
  (vanilla made it).
- **`tower.block`** (god mode on, `StaminaRate` key removed): hold block through the Debug hook, wait until
  `IsBlocking()` and the Braced effect is shown (`m_hidden` false). Hits built in code and passed to the player's
  `RPC_Damage`:
  - 60 blunt from the front, attacker a spawned `Greydwarf`: health lost = the vanilla formula with block power 130
    (6.9 gets through the block; the player's body armor then applies, as in vanilla `RPC_Damage`), stamina used ≈
    10 × blocked/130;
  - 30 poison, `m_blockable = false`, attacker a spawned `Greydwarf_Shaman`, status-effect hash set: from the front the
    hash is cleared (the block held); from behind it is not; with `HitType.Fall` and no attacker it is not;
  - stagger, 60 blunt from the front on the fresh character (max health 25, threshold 10): stamina set to 15 → the
    stagger bar rises by 20% of the vanilla amount (about 2.8) and the player does not stagger; stamina set to 8 (at or
    below the reserve) → the resistance is off for that hit, the bar rises by the full amount (about 13.8) and the
    player staggers; the HUD name reads "Braced (exhausted)" and the icon flashes at 8; stamina set to 3 → the block
    (cost about 4.1) empties stamina and the whole hit lands (health lost about 60);
  - knockback: stamina 50, a frontal hit with push 100 leaves `IsKnockedBack()` false; the same from behind pushes;
    `ApplyPushback(dir, 50)` from the front (area knockback) does not push;
  - release block → the Braced effect is hidden with speed modifier 0; unequip the tower while blocking → the effect
    is gone and the speed is the carried one.
- **`tower.slow`**: tower equipped, no armor: `GetJogSpeedFactor` = 0.70, `GetRunSpeedFactor` = 0.55 (run skill 0),
  and while braced `ApplyStatusEffectSpeedMods` turns 1.0 into 0.70; with the tower copy's movement modifier set to
  -0.70 for the check (put back after), `GetRunSpeedFactor` × 7 ≥ `GetJogSpeedFactor` × 4; tower hidden → factors
  back to 1.
- **`tower.toggle`**: sword in the right hand and a tower (reverted, Shield type) in the left; `ApplyAll` +
  `FixHands` → the sword is put away; `RevertAll` → the snapshot values are back on the inventory copy, on the prefab,
  in a spawned chest's copy and on a ground drop; `ApplyAll` again → the tower values are back. Mid-bash: start a bash
  at a `Troll`, deactivate-cancel + `RevertAll` before the hit event → `m_currentAttack` is null and the Troll's health
  is unchanged 1 s later, animator speed 1. Mid-swing: after the attack state is reached (the swing slowed, before the
  hit), deactivate → the animator speed is the clip's own again at once, no hit, speed 1 after the clip. (The internal
  functions `OnActivated` / `OnDeactivated` use; the real switch is an in-game test.)
- **`tower.rules`**: rules wire round trip (layout 2, the new fields too); clamping of out-of-range values; unknown
  layout and layout 1 refused; `BashAnimation` offers exactly ShieldPunch, OtherPunch, Kick, Custom (default
  ShieldPunch), its own clamp turns `ShieldUp` into `ShieldPunch`, `kick` into `Kick` and ` CUSTOM ` into `Custom`, and
  an unknown name is no name (pure lookup: the clamp would log its Warning); a changed cooldown, speed or lock changes
  the rules key;
  single player never takes rules from a peer; the generation rises on a different received rule set, not on an
  identical one, and on an own change only while not using the server's; a `TestRules` change reaches a tower's block
  armor about 0.5 s later;
  `Towers` validation (`SwordIron` refused, `ShieldBanded` accepted with a Warning); `PlayerCheck.Decide` verdicts
  (pure function; its `compatible` input is `NetworkGate.PeerCompatible` in the game): compatible → let in; not
  compatible (no mod, other network version or turned off: the framework's three reasons) → refused, or let in with a
  Warning when `AllowPlayersWithoutMod`; not the server, player gone, not ready or already being kicked → skipped.
  Which of the three reasons a real player gets is the framework's part (`src/Shared/TESTING.md` N07-N08) and M02,
  M12, M14.

### 7.5 Shared and framework changes (done in this run)

Both are already in `src/Shared` (implemented and checked with `./tools/Test-Framework.ps1` in this run's framework
change); they are no longer Tower Shield Wall tasks, and this mod only uses them.

- **`ItemKinds` shield rule** (`src/Shared/ItemKinds.cs`, `ItemKinds.Classify`, before the item-type switch):
  `m_animationState == AnimationState.Shield && m_skillType == Skills.SkillType.Blocking → ItemKind.Shield`, so tower
  shields stay `Shield` after becoming `TwoHandedWeaponLeft`. Every vanilla shield has both, no other item has either
  *(dump: 24 of 24)*. Sort Chest, Crafting Search and Sort and the Compendium pick it up at their next rebuild (each
  DLL compiles its own copy). The `tower.data` self-test checks it for every tower.
- **`NetworkGate` peer state** (`src/Shared/Framework/NetworkGate.cs`, `ModPlugin.cs`): the client's hello carries
  whether its own copy is ready (on, no start error), `<guid>.HelloState` updates it while connected, and the server
  side offers `NetworkGate.PeerCompatible`, `PeerProblem`, `PeerNetworkVersion`, `PeerReady` and the
  `PeerStateChanged` event. `PlayerCheck` uses `PeerCompatible`, `PeerProblem` and `PeerStateChanged` (4). Its
  multiplayer tests are the framework's `src/Shared/TESTING.md` N07-N08, plus this mod's M02, M12 and M14.
- **`[AlwaysOnPatch]`** exists in the framework; this mod does not use it (7.3).

## 8. Test plan

`TESTING.md` items. Names checked in the 1.0.16 dump: items `ShieldWoodTower`, `ShieldBoneTower`, `ShieldIronTower`,
`ShieldSerpentscale`, `ShieldBlackmetalTower`, `ShieldFlametalTower`, `ShieldGoldTower`, `ShieldBanded`, `ShieldWood`,
`ShieldIronBuckler`, `SwordIron`, `Torch`, `Bow`, `Battleaxe`, `Hammer`, `CrossbowArbalest`; creatures `Greydwarf`,
`Greydwarf_Shaman`, `Draugr`, `Troll`, `Blob`, `Goblin`, `Skeleton`, `FallenWarrior`. Piece names of the item stands,
the armor stand and the training dummy, armor movement values and the console syntax for the world level are
unverified.

**Setup**: F5, `devcommands`; `spawn ShieldIronTower 1 1` (amount, quality); `god` for the handling tests and T09
(health stays at 1 or more, stagger and stamina still count), off for the damage tests; `raiseskill Blocking 100` /
`resetskill Blocking` for skill checks; watch `./tools/Watch-Log.ps1 -Mine`. Expected numbers are for single player,
0-star creatures, default world modifiers.

**Single player**

- T01 **Two-handed (G1)**: hold `SwordIron` + `ShieldBanded`, equip `ShieldIronTower`. Expected: both put away, only
  the tower in the left hand; tooltip "Two-handed".
- T02 **Swaps (G1)**: with the tower held, equip in turn `SwordIron`, `Torch`, `ShieldBanded`, `Bow`, `Battleaxe`,
  `Hammer`. Expected: each one puts the tower away; re-equipping the tower empties both hands again.
- T03 **Look (G1)**: the tower sits in the left hand as before; hide weapons (R) and swim: it hangs in the shield slot
  on the back, not like a greatsword; place it on an armor stand (shield slot) and on an item stand.
- T04 **Pickup (G1)**: drop a tower, hold a round shield with an empty right hand, pick the tower up: the round shield
  stays equipped. Hold a tower and pick up a sword: the sword is not auto-equipped.
- T05 **Carried slow (G2)**: jog and sprint the same stretch with and without the tower (same armor). Expected: much
  slower (tooltip "Movement -30%"); sprint still faster than jog; walk and crouch speed unchanged.
- T06 **Braced slow (G2)**: hold block with the tower: noticeably slower advance and slower turning than blocking with
  a round shield; release: back to the carried speed.
- T07 **No parry (G3)**: block right as a `Greydwarf` hits, many times. Expected: never a parry (no parry flash or
  sound, the Greydwarf is never staggered by the block). `ShieldGoldTower` tooltip: no "Parry adrenaline" line, a
  "Cannot parry" line.
- T08 **Block armor (G4)**: tooltips at quality 1: Wood 25, Iron 130, Blackmetal 260, Nord Greatshield 395
  (+15 per quality level). God mode off, no armor, Blocking 0, alone, a 0-star `Troll`, default world modifiers: block
  its punches with the Iron tower: about 7 damage and 4 stamina per punch (vanilla about 17 and 8).
- T09 **The wall holds (G5 b)**: god mode on, food eaten (max health 75 or more, so the stagger threshold is 30 or
  more), no body armor, Wood tower, a `Troll`, block its punches from full stamina. Expected:
  about 35 damage gets through each block (god mode keeps you alive) and each block costs about 10 stamina; you are
  not staggered while the HUD shows "Braced"; when stamina is down to about one block, the icon flashes "Braced
  (exhausted)" and the next punch lands in full and staggers you; after about a second of regen the brace does not
  come back until stamina is above one block again.
- T10 **Unblockable attacks from the front (G5 a)**: Iron tower, brace facing a `Blob` and a `Greydwarf_Shaman`: their
  poison shows "Blocked" and you take little poison (a fifth or less). Turn your back: full poison.
  `BlockUnblockableAttacks = false`: the poison goes through as in vanilla.
- T11 **Still unblockable (G5)**: while braced, fall damage, standing in fire and hits from behind are not blocked.
- T12 **Bash (G6)**: press attack with the tower: a shield-arm strike (ShieldPunch), about 20 stamina, low damage
  numbers; the tooltip lists Blunt, stamina use 20, knockback, "Bash stagger: ×25" and "Bash cooldown: 2 s"; bashing
  raises Blocking.
- T13 **Bash stagger (G6)**: Iron tower, Blocking 0: a `Greydwarf` and a `Draugr` stagger in one bash, a `Troll` in two
  to four bashes (2 s apart; two or three for about four Trolls in five); hit a staggered enemy with a sword (quick
  swap): crit effect (×2).
- T14 **Bash from guard (G6)**: hold block and press attack: the bash plays, the guard is back right after it.
- T15 **Bash animations (G7)**: try each `BashAnimation`: ShieldPunch, OtherPunch, Kick, Custom with `throw_bomb`,
  `spear_poke`, `mace_secondary` (the list offers only these four names). Note which one looks best, which arm swings,
  and whether the log says an option fell back. Custom `emote_wave` or a made-up name: a Warning in the log and
  ShieldPunch plays. Custom `staff_rapidfire` (a held attack): a Warning that it is held, aimed or reloaded, and
  ShieldPunch plays. A config file that still says `BashAnimation = ShieldUp`: ShieldPunch plays, no warning;
  `BashAnimation = kick`: the kick plays and the setting reads `Kick`; `BashAnimation = Kicks`: ShieldPunch plays, one
  Warning. (On a server only the host's choice applies.)
- T16 **Settings live (G2-G7)**: change `BlockArmorMultiplier`, `BlockForcePercent`, `CarrySlowPercent`,
  `BraceSlowPercent`, `BraceKnockbackResistPercent`, `BashStagger`, `BashStaggerLock`, `BashCooldown`,
  `BashAnimationSpeed` and `BashAnimation` while playing with the tower equipped: tooltips, speed, push and bash change
  within about a second, without re-equipping.
- T17 **Creature copies untouched (G1 scope)**: `spawn FallenWarrior`: it still fights with a sword and its tower.
- T18 **Live toggle (G9)**: hold the tower, untick Tower Shield Wall in MC Mods: it becomes a one-handed shield in
  place (tooltip), you can add a sword, attack punches, block armor is vanilla, the Braced icon is gone; towers in a
  chest and on the ground are vanilla too. Tick again while holding a sword and a tower: the sword is put away and the
  tower is two-handed.
- T18b **Toggle mid-bash (G9)**: set `BashAnimation = Kick` (slow hit: about 0.9 s at the default speed), bash a
  `Troll` and untick the mod before the kick lands: no hit, no damage number; the rest of the kick plays at its
  normal speed, and the next attacks too.
- T19 **`Enabled = false` + restart**: vanilla towers; Status "Off (disabled in settings)".
- T20 **Clean log**: T01-T30 with `./tools/Watch-Log.ps1 -Mine`: no error or exception from the mod.
- T21 **Lock limit (G6)**: alone, Iron tower, bash a `Draugr` continuously: it staggers on the first bash, then not
  again for about 8 s however often you bash, and it attacks between staggers. `BashStaggerLock = 0`: every bash that
  lands after the previous stagger ended staggers it again (bashes 2 s apart, a Draugr stagger about 2.5 s: about every
  other bash, so it is staggered most of the time; `RPC_Stagger` ignores a creature already staggering).
- T22 **Immovable (G5 c)**: braced with stamina, a `Troll` punch from the front does not push you back; from behind it
  does; with stamina at one block or less, the punch that breaks the guard pushes you. `BraceKnockbackResistPercent =
  0`: blocked punches push again (less than unblocked ones).
- T23 **NG+ (G6)**: in a world set to world level 1 (world modifiers, or the global key `WorldLevel`; exact command
  unverified), `spawn ShieldIronTower`: its tooltip blunt is 12 (no +120); a `Draugr` still staggers in one bash; the
  bash shows almost no damage. Crouch up behind an unaware `Draugr`, bash it, then hit it with a sword right away
  (quick swap): the Draugr turns to fight after the bash, and the sword hit is not a sneak attack (no sneak-attack
  effect; the staggered ×2 crit effect still shows).
- T24 **Braced HUD (G5)**: while bracing, a status icon with the tower's picture and "Braced"; it disappears when you
  release block; when stamina is low it flashes "Braced (exhausted)"; the item tooltip has the "Braced: ..." line.
- T25 **Stagger resistance from behind (decision 7)**: braced, let a `Greydwarf` hit you from behind: full damage, but
  little stagger.
- T26 **Brace ends with the tower (G2, G5)**: while blocking, unequip the tower from the inventory: the Braced icon is
  gone and you move at normal speed. Die while blocking: after respawn no Braced icon, normal speed.
- T27 **No parry adrenaline (G3)**: with `ShieldGoldTower`, block right as a `Greydwarf` hits: adrenaline rises by the
  normal block amount (2), never by 15.
- T28 **Put away (G2)**: hide the tower on your back (R): jog and sprint at normal speed; draw it: slow again.
- T29 **Block push (decision 16)**: block `Greydwarf` and `Draugr` hits with the Iron tower: note whether the attacker
  ends up beyond bash range (1.8 m) after the push; repeat with `BlockForcePercent = 50`.
- T30 **Heavy armor sprint (G2)**: in the heaviest armor you have, with the tower in hand: sprinting is never slower
  than jogging.
- T31 **Pickup with empty hands (G1)**: with both hands empty, pick up a tower: it goes to the inventory, not into your
  hands.
- T32 **Training (decision 23)**: bash a training dummy (built with the hammer; piece name unverified): Blocking rises
  (documented behaviour).
- T33 **Bash cooldown (G6, decision 30)**: hold the attack button with the tower: one bash every 2 s, not one right
  after another; press attack once, right after a bash's swing is over (about 1.3 s after its press): the next bash
  starts on its own 2 s after that bash started, not earlier and not never; no stamina is spent while the press waits.
  `BashCooldown = 0`: the bashes follow each other as soon as the swing ends.
- T34 **Slow swing (G7, decision 32)**: compare `BashAnimationSpeed = 1` and the default 0.6: the default swing is
  visibly slower and its hit (damage number, stagger) comes later, about 0.8 s after the press. Try 0.5 and 0.7 and note
  which looks best. The kick and a Custom animation are slowed too. After a bash, jogging, blocking and other attacks
  play at their normal speed.
- T35 **Stagger cost against a buckler (G6, decision 31)**: Blocking 0, no food: each bash costs about 20 stamina,
  also when it misses; stagger a `Draugr` with one bash. Then with `ShieldIronBuckler`, parry a `Draugr`'s swings
  until one staggers it: note how many tries it took and the stamina and health they cost. Note whether proccing the
  stagger with the bash still feels easier than with the buckler.
- T36 **One stagger per bash in a group (G6, decision 35)**: Iron tower, Blocking 0, two `Draugr` side by side in
  front: a bash damages and pushes both but staggers only the one you face; a second bash at the same one within 8 s
  staggers neither; turned to the other, the bash staggers it. Three `Greydwarf`: one bash staggers one of them.

**Multiplayer**

- M01 **Server without the mod**: the MC Mods panel says inactive; towers are vanilla for you.
- M02 **Player without the mod refused**: a friend without the mod joins a server or host with it: "Incompatible
  version" about a second after joining, and the server log says they do not have the mod; with
  `AllowPlayersWithoutMod = true` they may play (Warning in the server log).
- M03 **Server rules for everyone**: the host sets `BlockArmorMultiplier = 4` and `BashStagger = 20`; the client's
  tooltips and bash use them; the client's own file is unchanged and applies again in single player.
- M04 **Remote view**: player B watches player A: tower in the left hand, on the back when hidden, the bash animation
  of each option, the block pose.
- M05 **Bash on another game's creature**: B arrives first so B's game owns the creatures; A bashes a `Draugr`, with B
  standing next to it: it staggers as in T13 (the stagger does not shrink with two players nearby).
- M06 **Hand-off (G1, G8)**: with `AllowPlayersWithoutMod` on, give a tower to a friend without the mod: for them it
  is a normal one-handed tower (with a sword, no bash, vanilla block armor); dropped back to you it is two-handed.
- M07 **Dedicated server**: install on the server and both players: M02, M03 and M05 behave the same.
- M08 **PvP**: bash a friend with PvP on: their round shield blocks it, their dodge avoids it; a braced tower player
  blocks it with little stagger and is not pushed.
- M09 **Server toggles live (G8, G9)**: the host unticks the mod while the client braces with a tower: the client's
  tower is one-handed at once, the Braced icon and slow are gone; the host ticks it again: the client's tower is
  two-handed again with the host's rules, without rejoining, and the client is not refused (its own copy stayed on).
- M10 **Live rules push (G8)**: the host changes `BashStagger` and `CarrySlowPercent` while the client holds the tower:
  the client's tooltip and speed change within about a second, without re-equipping.
- M11 **Different own Towers list (G8)**: the client equips `SwordIron` and `ShieldBanded` (default list), quits to
  the main menu, adds `ShieldBanded` to its own `Towers` in the config file, then joins the host (default list): both
  stay equipped at join; `ShieldBanded` stays a one-handed shield there.
- M12 **Mod turned off on a client (G8)**: a friend with the mod unticks it in MC Mods, then joins: refused
  ("Incompatible version") about a second after joining, and the server log says they have the mod turned off; a
  friend who unticks it while connected: refused about a second later; a friend who unticks it and ticks it again
  within that second stays. With `AllowPlayersWithoutMod = true` both may play with vanilla towers (Warning in the
  server log).
- M13 **Two tower players (G6)**: A and B both bash the same `Draugr` in turn: it is not kept staggered (the 8 s lock
  limit applies to both).
- M14 **Other network version refused (G8)**: build a copy with another network version (`dotnet build
  src/Combat/Shields.TowerWall -p:ModNetworkVersion=3 -p:DeployToGame=false`, then rebuild without the override) and
  give its DLL to the friend: they are refused ("Incompatible version") about a second after joining, and the server
  log says they have another version of the mod (network version 3, the server has 2). With
  `AllowPlayersWithoutMod = true` they may play: their MC Mods panel says the versions cannot talk to each other, and
  their towers are vanilla. A build of the first test (network version 1) is refused the same way.
- M15 **Ally's spell (G5 a)**: PvP off, player B casts the Staff of Protection (`StaffShield`) next to player A, who
  braces facing B: A gets the protective bubble; no "Blocked" text, no Blocking skill gain. Repeat with PvP on.
- M16 **Joining with a weapon and a tower in hand (4)**: with the mod turned off, hold a sword and a tower, quit to
  the main menu, turn the mod on, join the host: only one of the two stays equipped (the one `EquipInventoryItems`
  equips last, in the inventory's stored order, as when loading a character with the mod on); no error in the log.
- M17 **Remote players see the slow bash (G7, decision 32)**: player B watches player A bash: B sees the same slowed
  swing as A (compare with `BashAnimationSpeed = 1` on the host), also when B's game does not have the mod (with
  `AllowPlayersWithoutMod` on); the hit comes when A's swing lands.

**Compatibility (optional)**

- C01 **Sort Chest / Crafting Search and Sort / Compendium**: towers sort and group with shields.
- C02 **Crossbow Stays Loaded**: load `CrossbowArbalest`, equip a tower (the crossbow goes away), equip the crossbow
  again: still loaded.
- C03 **Forge Idol Upgrades**: refine a tower: still two-handed, block armor of the new quality ×2.5.
- C04 **Weapon Moveset**: jump or roll, then attack with a tower: the normal bash plays, no jump or roll attack; a
  press late in a roll starts the bash once the roll is over (it never flows out of the roll).
- C05 **Dual Wielding**: with two one-handed weapons, equip a tower: both go; with a tower, equip a one-handed weapon:
  the tower goes.
- C06 **Creature Morale**: a creature that is afraid of you (it runs when you come within 12 m) stops running and
  fights you after a bash (sneak up on it or catch it first), also in NG+.
- C07 **External shield mods**: with ZenCombat, CaptainValheim, Goo's Combat Overhaul or ShieldBash installed, the log
  has one Warning naming them.
- C08 **Sneak Ambush**: crouch-approach an unaware `Greydwarf` and bash it: no backstab, no sneak-attack XP; raising the
  tower ends the crouch. In an NG+ world (as T23): bash an unaware creature, then hit it with a knife at once: no
  backstab and no Sneak XP (the bash alerted it).

## 9. Open questions and unverified

### Open questions for the user

1. **Bash animation** (G7): answered after the first test (decision 33): ShieldPunch is the default, ShieldUp is gone.
   Still open: the swing speed (0.6 by default, T34) and the cooldown (2 s, T33).
2. **Serpent Scale Shield** counted as a tower shield (the wiki lists it with them; its data is tower data)?
3. **No secondary attack** on tower shields (the vanilla empty-hand kick is lost): fine, or add the kick (L3)?
4. **Numbers**: carried -30%, braced -30% more, block armor ×2.5, bash ×25 stagger, bash stamina 20 (was 12), 2 s
   bash cooldown (new), swing speed 0.6 (new), 8 s lock limit (was 5 s; optional now that the bash cooldown exists,
   decision 34): right amounts? (Decisions 30-34; a Troll now takes two to four bashes, T13, T35.)
5. **Stagger resistance from all sides** while braced (decision 7), or front only (L7)?
6. **Block push**: at 100 the push on attackers is 10-35% stronger than a vanilla tower's (decision 16); lower the
   default?
7. **`ShieldIronSquare`** (cannot parry, -20% movement): add it to the tower list?
8. **ZenCombat's tower block charges** stop on our towers (decision 11): fine?
9. **Immovable**: no push at all from the front while braced (decision 22); AoE launch still throws you (L4): fine?
10. **Blocking training**: the bash trains Blocking on a training dummy (decision 23): fine, or no XP from targets that
    cannot fight back?
11. **Mod turned off by a player on a server** gets them refused about a second later (decision 24): fine?
12. **Tower on the back does not slow** (decision 29): fine, or should a carried (not held) tower slow too?
13. **NG+**: the bash deals almost no damage in NG+ worlds but staggers as usual (decision 21): fine?
14. **"Cooldown is too short"**: read both ways (decision 30: time between bashes; decision 34: the per-creature lock,
    renamed `BashStaggerLock`, 5 → 8 s). Was one of the two meant, or both? With the bash cooldown alone the lock of
    5 s already gives about 42% stagger time on one creature; 8 s gives about 32%.
15. **One heavy stagger per bash** (decision 35): in a group, only the creature you face is staggered by a bash; the
    others take its damage and push. Fine, or should the tower keep staggering every creature in its arc (crowd
    control), with the buckler comparison then true only one creature at a time?

### Unverified names and values, and how to check

| Item | How to verify |
|---|---|
| ~~`unarmed_attack1` plays from the Shield stance outside the fist combo~~ settled: it plays `Punchstep 2` (in-world run, 2026-09-30) | — |
| Which hand strikes (`unarmed_attack1` = left?); the clips are settled (`Punchstep 2`, `Punchstep 1`, `Kickstep`, 1.10) | T15 |
| ~~The look of ShieldUp~~ settled: the same as ShieldPunch (1.10, decision 33) | — |
| A failed trigger left set on remote animators and consumed later (cosmetic) | M04 |
| Other families' triggers play from the Shield stance (Custom): `throw_bomb` and `dualaxes3` do (in-world run) | T15 |
| ~~`dualaxes3` plays a clip with two Hit events~~ settled: `DualAxes Attack 4`, 2 Hit events, one skipped (in-world run) | — |
| ~~A looping clip is left through `attack_abort`~~ settled: `staff_rapidfire` forced on the bash was stopped and the bearer free after 3.07 s (in-world run, speed 1) | `tower.bash` again at speed 0.6 (5 s limit) |
| Hit timings and swing ends at speed 1 settled (1.10); at the default speed 0.6 estimated from the clip events (punch hit about 0.8 s, swing about 1.1 s; kick hit about 0.9 s, swing about 2.4 s) | `tower.bash` NOTE delays; T34 |
| Whether the punch clip's `Speed` event at its first frame comes before the attack state is seen (either way the speed ends up scaled, 2.7) | `tower.bash` NOTE |
| Remote players see the slowed swing at the same speed (vanilla ZDO sync, 1.10) | `tower.bash` (the ZDO value); M17 |
| Stagger animation length: a Draugr about 2.55 s (in-world run); others | T21 |
| Tower EffectLists (a bash hit without sound if the tower's `m_hitEffect` is empty; then borrow the unarmed one) | `tower.data` NOTE counts |
| No item stand has a non-empty `m_supportedItems` list that would refuse a two-handed tower; piece names of the stands | `tower.data` NOTE; T03 |
| A runtime-created `SE_Stats` works as the Braced effect with no ObjectDB registration, and toggling `m_hidden` / `m_name` / `m_icon` shows on the HUD | `tower.block`, `tower.slow`; T24 |
| Armor movement modifiers in 1.0.16 (armor was not dumped), for the total slow with heavy armor | `tower.data` NOTE; T30 |
| `ShieldIronSquare` obtainable (recipe) | check `ObjectDB.m_recipes` in `tower.data` NOTE |
| Console syntax to set the world level; `m_worldLevelGearBaseDamage` / `m_worldLevelEnemyHPMultiplier` prefab values (code defaults 120 / 2) | `tower.ng` (global key); T23 |
| Training dummy piece name | T32 |
| External GUIDs: CaptainValheim, ZenCombat, Goo's, ShieldBash, ReliableBlock(s), WeaponArts, EpicLoot, Combat Adjustments, WackysDatabase | `Chainloader.PluginInfos` with the mods installed (C07); the Harmony-owner scan works without them |
| ZenCombat enables block charges through `m_buildBlockCharges` data (closed source) | test with ZenCombat: no instant bash every 5 blocks |
| Goo's Combat Overhaul classifies towers by item type (closed source) | test with GCO installed |
| EpicLoot enchant rules for a TwoHandedWeaponLeft tower | test with EpicLoot |
| Remote players see the tower on the back correctly (code says yes: their prefab is Shield type) | M04 |

## 10. Implementation notes

Built in stages. **Stage 1 (foundation), done:** csproj metadata (`ModIdea`, `ModNetworkVersion` 1, the notes of 4 and a
239-character `ModDescription`), `Plugin.cs` (every setting of section 5, the `Application.quitting` flag, the
`OnActivated` / `OnDeactivated` order of 7.3), `TowerRules.cs` (snapshot, tower list parsing, wire layout 1, clamping,
`Describe`, `InForce`), `ServerRules.cs` (generation counter, 0.5 s push delay, pending state, Debug `TestRules`),
`PlayerCheck.cs` (`PeerCompatible`, `PeerProblem`, `PeerStateChanged`), `TowerSync.cs` (apply timing only: the item,
hand, bash and Braced steps are stage 2), `TowerGuard.cs` and `Patches/ZNetPatches.cs`.

Deviations from the text above (small, each for a reason):

- **Section order** (5): BepInEx writes the sections of the `.cfg` file in alphabetical order, so they show as Bash,
  Bracing, General, Tower shields instead of General, Tower shields, Bracing, Bash. Of the ConfigurationManager builds
  only aedenthorn's (Nexus 740) also sorts them by name; shudnal's and the upstream build keep the bind order (read in
  their source code 2026-10-02, `docs/modding/framework.md`, "ConfigurationManager"). Forcing the design's order would need numbered section names, which become
  permanent config keys; the names stay as designed. Inside each section the `Order` attributes give the order of 5.
- **Setting descriptions** (5): every server rule ends with "In multiplayer the setting of the server (or host) is used
  for everyone." (as Forge Idol Upgrades and the sibling Combat mods); `BashAnimation` adds "Try the options in single
  player or as the host." The percent settings say "in percent". The enum type is named `BashAnimationKind` (the config
  key stays `BashAnimation`, the values are the design's).
- **Tower list parsing** (2.0): separators are commas, semicolons and new lines; prefab names are case-sensitive (as in
  `ObjectDB`). A damage that is not a number becomes 10, one outside 0-200 is clamped, an empty name is skipped and a
  duplicate keeps its first entry; all of these go into one Warning ("The Towers setting has problems: ...", or "The
  server's Towers setting ..." on a client) once per rule set. The MISSING, not-a-shield and parry checks against the
  `ObjectDB` belong to the catalog (stage 2).
- **Rules key** (2.0 "compare the rules' text"): `TowerRules.Key` is every value with the tower list normalized
  (spaces and separators do not count), so an edit that changes nothing never re-applies. When the server sends the
  same values again, `ServerRules` keeps the rules object it has and raises no generation.
- **Apply trigger** (2.0, 7.3): besides the generation, `TowerSync` also applies when the `ZNet` object changes
  (joining a server, starting a world after leaving one): the rules in force switch there without any rules change
  (main menu own rules → client waiting; server's rules → own rules). `ServerRules.Forget` raises the generation only
  when server rules were actually dropped.
- **Join check re-check** (4): a `PeerStateChanged` restarts that player's 1 s grace (a check already queued is moved
  to one second from now), so the verdict is taken on the state one second after the last change. Log lines: "<player>
  joined, but their game <reason>. AllowPlayersWithoutMod is on, so they may play; for them tower shields are normal
  shields." and "Refused <player>: their game <reason>. ... Their game shows "Incompatible version". ..."
- **Other-mod warning** (6.3, 7.3): because most GUIDs of the table are unverified, a plugin is matched by its GUID or
  by a name part in its name or GUID (letters only, any case: `captainvalheim`, `secondaryattacks`, `zencombat`,
  `combatoverhaul`, `shieldbash`, `reliableblock`, `wackysdatabase`, `weaponarts`, `shieldmebruh`, `smartshield`,
  `autoshield`, `epicloot`, `combatadjustments`); the Harmony scan names each owner with what it patches ("changes
  blocking", "changes attacks", "changes which weapon is used"). `TowerGuard.Reset` runs in `OnActivated` (7.3), so the
  flag lives per activation, not per process (7.3 table): turning the mod off and on again warns again.
- **Deactivation steps** (7.3): each step of `OnDeactivated` runs through `Plugin.Step`, which reports a failure with
  `PatchGuard.Report` (one Error per step) and carries on.

**Stage 2 (features), done:** `TowerCatalog.cs` (vanilla snapshots, tower table, the MISSING / not-a-shield / parry
Warnings, the cached held-tower verdict), `TowerData.cs` (`Apply` / `Revert` of the 2.0.1 fields, heal, `ApplyAll`,
the revert steps, `FixHands`), `BashAttack.cs` (the bash `Attack`, trigger resolution, the fallback chain),
`BashWatch.cs` (running bash, watchdogs A and B, one hit per bash, cancelling), `BashStagger.cs` (the creature-owner
stagger rule, the lock limit, the NG+ alert call), `Brace.cs` (the Braced effect, the per-hit brace, the push rule and
`BashPose`, the ShieldUp pose), `TowerTooltip.cs`, `TowerSync.cs` (the apply and deactivation steps of 7.3), and every
patch of 7.2: `Patches/ObjectDBPatches.cs`, `HumanoidPatches.cs`, `CharacterPatches.cs`, `PlayerPatches.cs`,
`ItemDataPatches.cs`, `AttackPatches.cs` and the Debug-only `PlayerDebugPatches.cs` (with `SelfTestHooks.HoldBlock`).
`SelfTests.cs` (7.4) is stage 3 (below).

More deviations (stage 2):

- **ObjectDB pass** (7.2): the `ObjectDB.Awake` / `CopyOtherDB` postfixes force a full rules apply through `TowerSync`
  (new catalog, prefabs and live copies, hands, bash, Braced), not only the prefabs, so the catalog, the item data and
  the rules the game code reads are always one state.
- **Rules read by the game code** (2.0): patches read `TowerSync.Applied` (the rules now written into the items; null =
  vanilla: mod off or client waiting) rather than `TowerRules.InForce`, so during the 0.5 s apply delay the per-hit and
  per-tick numbers never disagree with the item data. While a client waits for the server's rules nothing applies,
  the bash stagger rule on creatures it owns included (vanilla stagger then).
- **Heal** (2.0): heal also reverts: an item that was a tower this session but is not listed now (or no rules are
  applied) is written back to its snapshot when seen. A copy written with the current rules carries a stamp (weak
  table keyed by its `SharedData`), so repeated heals (a held attack button, a tooltip redrawn every frame) cost one
  lookup; every rules apply or revert makes the stamps stale. Heal on equip covers every player object (also the
  main-menu character preview), not only the local player.
- **Custom triggers** (2.7): the allowed set is built from items with an icon (player items; creature attack items
  have none) plus the local player's unarmed attacks, with listed towers counted by their vanilla attack, instead of
  every `ObjectDB` item. A non-Custom option whose trigger is missing from the animator also falls back, with the
  Warning "The bash animation X ('trigger') does not exist in this game version; using Y."
- **Watchdogs** (2.7): (A) at the timeout, if the player is staggering, knocked back, dead or in a minor action, the
  timer restarts instead of failing; a bash that stops being the current attack before reaching its attack state
  (put away, replaced) is dropped without a verdict. (B) needs the bash to still be `m_currentAttack` (a bash stopped by
  unequipping is not a failure). When `OtherPunch` fails there is one Error and no further fallback, but watchdog (A)
  still cancels each stuck bash and resets its trigger.
- **Per-hit state** (2.5, 2.6): the `RPC_Damage` postfix (a finalizer since stage 7) puts back the state saved by its
  prefix instead of clearing it, because `BlockAttack` can nest an `RPC_Damage` inside another (the block push on an
  attacker this game owns).
- **Facing tests** (2.5 c): the push direction is flattened to the ground before `Dot(dir, forward) < 0`, like the
  hit direction in (a) (vanilla `ApplyPushback` flattens it too).
- **Snapshot** (2.0): a known prefab name that now maps to another prefab object (a mod registered it again) gets a
  fresh snapshot: that object was never written by this mod.
- **`FixHands`** (2.1): also acts when the tower is the hidden left item (both hands hidden), as the `tower.hands` test
  expects.
- **Tooltip** (2.3, 2.5): "Cannot parry" in orange, "Bash stagger: ×25", and the "Braced: ..." line with the parts at 0
  left out; its "(from the front, while you have stamina for another block)" only when a conditional part remains
  (replaced in stage 7: each part now carries its own condition).

**Stage 3 (self-tests), done:** `SelfTests.cs` (Debug only) registers the nine tests of 7.4 in `OnActivated` and
unregisters them first in `OnDeactivated` (which also clears `SelfTestHooks` and the rules override). Every test forces
its rules through `ServerRules.TestRules` and waits until `TowerSync` has applied them (about 0.5 s). In its `finally`
it puts back the rules override, the block hook, god mode, the `StaminaRate` / `WorldLevel` keys, stamina, health,
stagger bar, push and the player's hands. It also takes back every item it gave and destroys what it spawned. Debug-only
hooks were added to the mod's own files: `BashWatch.DebugHitEvents` / `DebugSkippedEvents` / `DebugFirstHitTime` (Hit
events of our bash clones, never reset) and `TowerSync.Active`.

Deviations from 7.4 (each for a reason):

- **Creatures stand still**: every creature a test spawns has its AI turned off at once. It cannot attack the player (a
  stagger or a push would refuse the next bash) and cannot wander off. Its `OnDamaged` hook is set in `Awake`, so a hit
  still alerts it and sets its target (`tower.ng`).
- **Placement**: creatures are put in front of the player with the capsule surfaces 0.3 m apart (from the collider
  radii), not at a fixed 1.3 m. This works for every creature size, and the look direction is set to the body's
  facing so the swing does not turn away. Vanilla `Character.GetRadius` scales creatures by the length of the scale
  vector (×1.73 at scale 1), so the tests read the capsule radius themselves.
- **Numbers from the live state**: expected values come from the state at the moment of the check. Block armor comes
  from the current Blocking skill, read again right before each hit of `tower.block`: every block trains the skill,
  so the block armor grows during that test too. What lands on health after the block also goes through the player's
  blunt resistance, body armor and the damage-rate world modifiers, as in vanilla `Character.RPC_Damage` /
  `ApplyDamage`; the stagger of a held block is what gets through the block plus what lands. The bash stagger range
  comes from the Blocking skill factor at the press, and the stagger threshold from the player's real max health.
  The worked numbers of 7.4 (6.9, 2.8, 13.8, 60) are the skill-0, 25-health, no-body-armor case of the same
  formulas. A NOTE gives the body armor and the worn armor pieces, and the Blocking level before and after the
  blocks. The stagger bar is read one frame after the Hit event, and the drain since the hit is added back.
- **`tower.ng`**: a world level 1 Draugr's threshold is 100, but the full bash stagger is 150-330. The bar stops at the
  threshold, so the stagger cannot be measured directly. The first bash is therefore made with the Draugr's stagger
  factor raised ×4 for that swing only, which gives the full `S` with the NG+ factor and the alert check. A second
  bash at the real threshold then checks that one bash staggers it.
- **Added checks** (small, same tests): every animator trigger and bool and every prefab the mod and the tests use
  (`tower.data`). The item-stand question of 9 is turned into a check: every `ItemStand` prefab that takes the tower as a
  Shield-typed item must also take it as two-handed; the same for armor stand slots. The rules check that
  `BlockUnblockableAttacks = false` leaves frontal poison unblocked; the Braced effect is in the HUD list
  (`GetHUDStatusEffects`); the stagger modifier values are right. The block pose is released after a ShieldUp bash.
  Each bash whose Hit event came must have hit its target (a reach problem fails clearly). The Draugr needs exactly one
  bash hit (asserted, not only noted). `BashStaggerCooldown = 0` staggers on every bash. `tower.rules` also checks
  list parsing, the rules key, the fallback chain, the Custom trigger set, the Braced tooltip line and
  `TowerSnapshot.IsShieldData`.
- **NOTE lines added**: the length of the Draugr's stagger animation (9, "stagger animation length"); the Game NG+
  numbers; whether the `piece_TrainingDummy` prefab exists; which chest prefab `tower.toggle` used.
- **Not testable in single player**: "the generation rises on an own change only while not using the server's rules".
  A single-player game is never a client, so `tower.rules` checks the single-player half and says so in a NOTE; the
  client half is M10 and M11.
- **`tower.bash`** calls `BashAttack.Reset()` first. The same rules as before rebuild nothing, so a fallback made by an
  earlier bash under the default rules would otherwise carry over into the "within two presses" check.
- **`tower.toggle`** calls `TowerSync.Deactivate(false)` / `TowerSync.Activate()` (the steps `OnDeactivated` /
  `OnActivated` run). If it fails half-way, its `finally` turns `TowerSync` back on, but only while the mod is still
  active.

**Stage 4 (docs), done:** `README.md` (features with the speed, block armor and bash tables of 2.2, 2.4 and 2.6, every
setting of `Plugin.cs` with its default and range, multiplayer, good to know, compatibility from 6), `CHANGELOG.md`
(0.1.0) and `TESTING.md` from section 8. Nothing has been run in game yet (build only).

Notes and deviations (stage 4):

- **Test order**: T18 (live toggle), T18b (toggle mid-bash), T19 (`Enabled = false` + restart) and T20 (clean log)
  close the single player group, as in the sibling Combat mods' `TESTING.md`; every id is the one of section 8 (T18b
  kept). The cross-mod group is named "other mods"; C04, C05, C06 and C08 name the siblings' matching items (Weapon
  Moveset X03, Dual Wielding X03, Creature Morale X05, Sneak Ambush X02).
- **Added items**: C09 Harpoon Hooks Tames (it also prefixes and postfixes `Character.RPC_Damage`, 6.2) and C10
  Creature Kill and Tame Counts (a bash kill counts as melee: `Character.RPC_Damage` maps skill Blocking to
  `KillModifiers.Melee`).
- **Setup checked**: the console syntax is read from `Terminal` in `.ref` (`spawn <name> <amount> <level> p`, where
  level is the item quality, at most 4, or the creature level; `setkey WorldLevel 1` / `removekey WorldLevel`, read by
  `Game.UpdateWorldRates`). `piece_TrainingDummy` is in the creature dump and the Troll's punch is 60 blunt (dump). Two
  rows of section 9 are therefore settled from code and data (training dummy name, world-level console syntax), not
  yet run. Still unverified and marked so: the item stand and armor stand piece names, crossbow bolt names, armor
  slowdowns, the English item names.
- **T17**: `FallenWarrior` carries its tower shield in only one of its random equipment sets (dump), so the item asks
  to spawn several.
- **T18b**: the kick's hit comes about half a second after the press, too short to untick by hand reliably; the item
  says so and may be skipped, because `tower.toggle` checks the same case.
- **Names**: the README and `TESTING.md` use the display names of the MC mods the design calls "Compendium" and "Stats
  Per Creature" (Encyclopedia, Creature Kill and Tame Counts). The README says the config sections show in alphabetical
  order (stage 1 deviation).

**Stage 5 (conformance review), done:** the design, the code and the docs were read again side by side. Every goal
(G1-G9 and the extras), every patch of 7.2, every setting of 5, the RPCs of 4 (no ZDO keys), the nine self-tests of 7.4
and every `TESTING.md` item are implemented as specified, apart from the deviations listed in this section. The README
and `TESTING.md` use the code's real names, defaults, ranges, log texts and framework status texts. Checked against
`.ref`: the signatures of every patched method (`Humanoid.Pickup`'s `autoequip` argument, the
`Character.ApplyPushback(Vector3, float)` overload, the static `GetTooltip` overload, `GetDamage(int, float)`), the
early returns of `Character.ApplyDamage` that `BashStagger.WillApply` copies, the NG+ health factor
(`Character.GetMaxHealthBase`), the jog and run factors behind the sprint floor, `SEMan.AddStatusEffect` /
`RemoveStatusEffect` (a clone is never destroyed, so the Braced template stays valid), `HideHandItems` (hidden items
are already unequipped, so `FixHands` leaves no stale equipped flag) and `ZSyncAnimation.SetBool` (a no-op when the
value is unchanged, so the per-tick ShieldUp write is cheap). Also checked: no `?.` or `??` on Unity objects, no LINQ
outside the self-tests, every patch body and RPC handler catches its own exceptions, and code that needs a local
player returns early on a dedicated server. Debug and Release both build with no warnings. Nothing has been run in
game.

Fixes made in stage 5:

- **Watchdog (A) waits for rendered frames too** (2.7): the 0.3 s must pass and at least 3 frames must be drawn since
  the bash started. After a hitch, Unity runs several FixedUpdates in a row to catch up, with no animator update
  between them, so time alone could pass before the animator had even seen the trigger. That would switch ShieldUp to
  ShieldPunch for no reason. At 60 fps, 0.3 s is about 18 frames, so this changes nothing in normal play.
- **Watchdog (B) ignores a bash cut short by the player's death** (2.7), like (A) already did. Dying mid-bash no
  longer counts as a failed animation.
- **Tower list warnings after turning the mod back on** (2.0): the MISSING / not-a-shield / parry Warnings are said
  again after the mod is turned off and on, like the Towers text problems and the other-mod warning (the catalog's
  "warned" key is cleared on deactivation).
- Code comments: a few that were plain English were rewritten in caveman speech.

**Stage 6 (first in-world run):** `tower.data`, `tower.rules`, `tower.hands`, `tower.slow`, `tower.bash`,
`tower.lock`, `tower.ng` and `tower.toggle` passed. `tower.block` failed 4 of 22 checks, all from the test's own
expected values; the mod behaved as designed:

- The test character had 1 body armor. Vanilla applies body armor after the block (`Character.RPC_Damage`), so a
  held block landed 5.923 instead of the 6.923 that gets through the block, and a broken block landed 59 instead of
  60 (the poison left after a blocked spray, 0.75, matches the same armor). The test compared with the value before
  body armor. The "block holds" check at stamina 15 failed for the same reason.
- The test worked out block armor once, at Blocking 0, but the first blocks raised the skill to about 1 (block armor
  130 to about 130.65). The stagger at stamina 15 (2.555) matches 20% of the real full amount (what gets through the
  block plus what lands after body armor at the new skill: 12.777), not 20% of 2 × 6.923.

Fix (test only): the expected values of every `tower.block` hit are worked out right before that hit from the live
state, through the same steps as vanilla (difficulty and enemy damage rate, block armor from the current skill, block
stamina cost, blunt resistance, body armor, damage-taken rate). The stamina 3 check also asserts that the block cost
more than 3 and left 0 stamina. The mod logged no Warning or Error of its own in the run (the ones in the log come
from the tests' deliberately bad settings) and nothing at quit. Not re-run yet.

**Stage 7 (review fixes), done:** a review of the code against `.ref` and the runtime dump found these; each is fixed
in the code, the text above (2.5, 2.6, 2.7, 4, 5, 7.2, 8, decision 28) and the README / `TESTING.md`. Build only;
nothing run in game or in the world yet.

- **Allies' spells are no longer made blockable** (2.5 a). The rule only checked that the hit was unblockable, an
  attack type, from an attacker and from the front. An ally's Staff of Protection bubble (`StaffShield`: its attack
  is unblockable, 0 damage) reaches a braced bearer as a `PlayerHit` with `m_ignorePVP`, so it was made blockable;
  vanilla `BlockAttack` then succeeds with 0 blockable damage and clears the hit's status effect: the bubble was lost,
  and each cast gave Blocking skill and adrenaline. Now the attacker must be hostile (`Brace.IsHostile`: a player only
  without `m_ignorePVP`, anything else by `BaseAI.IsEnemy`). Self-tests: `tower.rules` (the player rule) and
  `tower.block` (a creature of the players' faction: its poison is not blocked); in-game: M15.
- **Custom refuses held, aimed, reloaded and attached attacks, and a bash that never ends is stopped** (2.7, decision
  28). `staff_rapidfire` passed the Custom check (an icon item fires it, the animator has it), but it is a looping
  clip left only through `attack_abort`, which `Attack.Stop` fires only for a looping attack; the bash clone copies the
  unarmed attack (not looping), so the bearer would have stayed in the loop (no block, dodge, re-equip or attack) until
  a stagger or death, on every tower bearer of a server with that setting. Such names are now refused with their own
  Warning, and watchdog (C) stops any bash still running 3 s after its attack state (stop, `attack_abort`, next
  option). Self-tests: `tower.rules` (the refused set), `tower.bash` (`staff_rapidfire` refused at resolution; the same
  trigger forced onto the bash past the check is stopped and falls back to ShieldPunch, with a NOTE of how the player
  got free); in-game: T15.
- **A bash on a creature that is already staggering starts no lock** (2.6). `AddStaggerDamage` returns true whenever
  the bar reaches the threshold, also while the creature is staggering from another hit, when `RPC_Stagger` ignores
  the new stagger; such a bash used up the 5 s limit without staggering anything. Self-test: `tower.lock` (bash hits
  passed to a second Draugr's `RPC_Damage`, with a Debug-only read of the lock table).
- **The brace reserve counts the world stamina rate** (2.5). `Player.UseStamina` multiplies every cost by
  `Game.m_staminaRate` (`StaminaRate` key), so with another rate the "stamina for one more full block" test, the
  "exhausted" HUD and the knockback rule disagreed with what the block really cost.
- **`BashStagger` minimum is 2** (5; was 1). Bash hits are recognised on the creature's owner by a stagger multiplier
  above 1 (other Blocking-skill hits: vanilla tower block-charge counter ×1000, a plain ×1 shield hit of another mod);
  at 1 the bash lost the decoupled stagger, the NG+ factor and the lock without any sign. Self-test: `tower.rules`.
- **Tooltip line** (2.5 UI): each part now carries its own condition. The old line put "from the front, while you have
  stamina for another block" after every part, but the slow and the made-blockable rule apply whenever braced and the
  stagger resistance applies from every side (decision 7). Self-test: `tower.rules` (two line shapes).
- **`BashAnimation` description and README** named the wrong fallback for Kick: the chain is ShieldUp and Custom →
  ShieldPunch → OtherPunch, Kick → OtherPunch (as 2.7 says); the text now says so, and that a bash that does not end
  also falls back.
- **Per-hit state is put back by a finalizer** (7.2), not a postfix: Harmony skips postfixes when `RPC_Damage` (or an
  earlier postfix of another mod) throws, and the scope of that hit would then have stayed for every later hit (each
  later call saved it as the "outer" state and put it back), so area knockback outside a hit used a stale "brace
  holds". The finalizer restores only when the prefix ran (`DamageState.Saved`).
- **Watchdog (B) with a held attack button** (2.7): the next bash can start in the tick the previous one ends, before
  the `UpdateBlock` verdict; the `StartAttack` postfix now gives the verdict of the bash it replaced (still at most one
  fallback per option: the new bash keeps the option it was started with).
- **The stagger resistance never takes the sum of stagger modifiers below -1** (2.5 b). With another effect's
  resistance (Fader's power, -0.5 in the dump) the sum was -1.3 and each blocked hit lowered the stagger bar.
  Self-test: `tower.block` (a -50% test effect: Braced gives -0.5, a blocked hit leaves the bar at 0).
- **Joining with a weapon and a tower both in hand** (4, README "Good to know"): the docs said the weapon is put away
  once the server's rules arrive; the rules normally arrive before the character spawns, so vanilla's equip order at
  load decides (the item later in the inventory stays), as when loading a character with the mod on. Docs corrected
  (no code change); in-game: M16.
- **README Multiplayer**: a host's settings change reaches clients about a second later (the server's 0.5 s push delay
  plus the client's 0.5 s apply delay), not half a second (M10 already said about a second).

**Stage 8 (user feedback after the first in-game test, 2026-09-30), done:** the in-world run after stage 7 passed all
nine self-tests (71 tests in the full run with every MC mod); the user then tested build `3cda33b+dirty` in game and
asked for a slower bash with a real cooldown, a stagger no easier than with a buckler, and a slower ShieldUp /
ShieldPunch animation (Goal). Decisions 30-34; the text above (G6, G7, 1.1, 1.4, 1.10, 2.6, 2.7, 2.8, 4, 5, 6.1, 7,
8, 9) describes the result. Code:

- `BashWatch`: the bash cooldown (last start per local player; `InCooldown` / `CooldownLeft`; cleared by watchdog (A)
  and on deactivation), watchdog (C)'s limit divided by the swing speed, `BashSpeed` begun, told and ended with the
  bash; the ShieldUp pose calls are gone.
- `BashSpeed.cs` (new) and `Patches/CharacterAnimEventPatches.cs` (new): the swing speed (2.7).
- `Patches/HumanoidPatches.cs`: the `StartAttack` prefix is a bool prefix (cooldown refusal before the heal); the
  `UpdateBlock` postfix no longer keeps a pose.
- `Brace.cs`: `BashPose` (the ShieldUp pose) removed; `TowerSync`'s deactivation step for it removed (the bash
  cancel puts the speed back instead).
- `TowerRules.cs`: layout 2 (`BashAnimationSpeed`, `BashCooldown`, `BashStaggerLock` instead of
  `BashStaggerCooldown`; the enum without `ShieldUp`, renumbered), new defaults (stamina 20, lock 8, animation
  ShieldPunch), `ParseAnimation`, `AnimationNames`; `Plugin.cs`: the new settings, `BashAnimation` as a text setting
  with `AcceptableValueList<string>` (a `ConfigEntry<enum>` cannot take a list: BepInEx's `AcceptableValueList<T>`
  needs `IEquatable<T>`, and a range on an enum would show a slider); the csproj's `ModNetworkVersion` 2.
- `TowerTooltip`: "Bash cooldown: 2 s" (left out at 0).
- `PlayerDebugPatches` (Debug): `SelfTestHooks.HoldAttack` holds the attack button through `Player.SetControls`.
- Self-tests (7.4): every press waits for the cooldown and records the speeds and the stamina; `tower.bash` checks the
  swing speed (event and start scaling, ZDO value, back to 1, later hit), the cooldown through `StartAttack` and a held
  button, the stamina of one bash with the world stamina rate back; `tower.lock` measures the 8 s lock with the
  cooldown off and speed 1; `tower.toggle` turns the mod off mid-swing and checks the speed is put back; `tower.rules`
  checks layout 2, the animation name list and the old `ShieldUp` value; `tower.data` checks stamina 20, ShieldPunch
  and the cooldown tooltip line. The blocking-bool check and NOTE are gone. Built clean (Debug and Release); not run in
  the world or in game yet.

Deviations (stage 8):

- **`BashAnimation` is a text setting** (5): the list of names is what turns an old `ShieldUp` into `ShieldPunch`
  without a warning; the rules and the wire keep the enum.
- **The per-creature lock's key is renamed** (`BashStaggerLock`): an old file keeps `BashStaggerCooldown` as an
  orphaned entry that nothing reads.

**Stage 9 (review of stage 8), done:** a review of the stage 8 change against `.ref`, the runtime data and BepInEx
found these; each is fixed in the code, the text above (G6; 2.6, 2.7; decisions 4, 30, 31, 33, 34, 35; sections 4, 5,
7, 8, 9) and the README, CHANGELOG and `TESTING.md`. Built clean (Debug and Release); not run in the world or in game yet.

- **One bash staggered a whole group** (decision 35, new `BashTarget.cs`, `Attack.AddHitPoint` postfix,
  `Attack.OnAttackTrigger` finalizer, `Character.Damage` prefix): with `m_lowerDamagePerHit` off every creature in the
  arc got the full ×25, so "not easier than a buckler parry" held for one creature only. Now only the creature nearest
  the middle of the swing keeps it; the others get ×1. Self-test: `tower.lock` (two Draugr, the side one right then
  left); in-game: T36.
- **A single press after the swing was dropped** (decision 30, `BashWatch.KeepPress`): the cooldown refusal relied on
  vanilla's 0.5 s buffer, so a press between about 1.1 s (swing over) and 1.5 s did nothing, with no cue. Now a press
  made once the swing is over is kept until the cooldown ends. Self-test: `tower.bash`; in-game: T33.
- **`BashAnimation` became case-sensitive** (2.7, `AnimationNameList` in `Plugin.cs`, `TowerRules.AnimationName`):
  BepInEx's `AcceptableValueList` turned `kick` into `ShieldPunch` and wrote it back to the file; the enum setting read
  it as Kick. Now any case is read, and an unknown value other than `ShieldUp` gets one Warning. Self-test:
  `tower.rules` checks the setting's own clamp (it only checked `ParseAnimation`, which the config never reaches with a
  wrong case); in-game: T15.
- **`tower.lock`'s "no limit" check could not fail**: the second bash landed on the first one's bar, clamped at the
  threshold and not yet drained, so a ×1 bash read "staggered" too. It now starts from an empty bar.
- **Docs**: T21 expected every bash to re-stagger with `BashStaggerLock` 0, but bashes 2 s apart land during a 2.5 s
  stagger (about every other one does); T13 and the README said a Troll takes two or three bashes, but about one in
  five needs four (2.6); decision 34 gave the lock's effect without the new bash cooldown (5 s already gives about 42%,
  not half; 8 s about 32%) and is now marked optional.

**Stage 10 (in-world run of stage 9), done:** the full run of 2026-09-30 (every MC mod, 66 of 71 tests passed) passed
`tower.data`, `tower.rules`, `tower.hands`, `tower.slow`, `tower.block`, `tower.bash` (every option, the swing speed,
the cooldown, the stamina, one bash staggers a Draugr) and `tower.toggle`, and failed `tower.lock` ("the first bash
staggers the Draugr") and `tower.ng` (stagger +0.557 against 205.5-430.5 expected, not alerted, no stagger at the real
threshold). Each of those bashes started, reached its attack state and fired its Hit event on time (watchdogs quiet,
option unchanged), but the Draugr took no damage and no stagger, and was not alerted, so no `RPC_Damage` ever reached
it: its collider was never in the swing's hit list. Why:

- **Not the mod.** The one-heavy-stagger pick (`BashTarget`) only lowers the stagger multiplier of creatures that
  are hit; it never removes one from the hit list or zeroes damage. The lock is kept per creature (each test spawns a
  new Draugr) and `tower.lock` runs with the cooldown off; the same Draugr bash in `tower.bash` (ShieldPunch) landed a
  few seconds earlier, and the runs before stage 8 passed both tests with the same `OtherPunch` clip.
- **The placement.** The `tower.ng` screenshot shows a boulder between the player and the Draugr, by the start
  stones. Vanilla `Attack.DoMeleeAttack` keeps only the first collider of each ray (`m_hitThroughWalls` off), so every
  ray stopped at the rock. The player got there by walking: each Punchstep clip moves him forward (root motion), and
  stage 8's cooldown checks added ten Draugr bashes to the end of `tower.bash` (the `tower.ng` screenshot is taken
  much closer to the start stones than `tower.bash`'s first one); the tests put the creature in front of wherever the
  player stood and faced, never checking the space between. The Troll bashes earlier in `tower.bash` came before the
  drift.
- **Fix (self-tests only):** the bash lane of 7.4 (before each placed bash, turn the player to a lane free of
  anything solid or alive, NOTE the turn), used by every `Press`, by `tower.lock`'s group bash (fan wide enough for the
  side Draugr) and by `tower.toggle`'s cancel checks (so "no hit" there means the cancel, not a rock); every test puts
  the player back where it found him (place, body and camera facing), so the drift of one test never reaches the next
  (nor the next mod's tests); a Hit event after which the target took nothing now names what stands in the lane.
  No check was changed. Built clean (Debug); not run in the world yet.
- **Why these tests show `OtherPunch`:** `tower.lock`, `tower.ng` and `tower.toggle` pin `BashAnimation = OtherPunch`
  (`WithAnimation(BashAnimationKind.OtherPunch)`): the last option of the fallback chain, so no watchdog can change
  the clip under a stagger measure; the default ShieldPunch is covered by `tower.bash`. Now written in 7.4 and in the
  `SelfTests.cs` header.
- Sibling texts brought up to date (6.1, C04, C06; README; `TESTING.md`): Creature Morale's afraid creatures (they run
  within 12 m, fight back when hit or cornered; C06 at boss rank 4), Weapon Moveset's roll attack that flows out of
  the late roll (a bash never does), Dual Wielding's queued swap and crossed sheathed pair (a tower is never part of
  either).

### Deviations from the design, in short

The details and reasons are in the stage notes above.

- Config sections show in alphabetical order; setting descriptions end with the "server's setting" sentence; the enum
  type is `BashAnimationKind`, and `BashAnimation` is a text setting with the list of its names (5, stage 8).
- `Towers` parsing: commas, semicolons or new lines; case-sensitive names; bad damage values become 10 or are clamped;
  all problems in one Warning per rule set (2.0).
- The rules compare by a normalized key, and the same server values keep the same rules object; `TowerSync` also
  applies when the `ZNet` object changes (2.0).
- A `PeerStateChanged` restarts that player's 1 s grace (4).
- Other mods are also found by a name part of their GUID or name; the "warned" flag lives per activation, not per
  process (6.3, 7.3).
- Each `OnDeactivated` step runs through `Plugin.Step` (7.3).
- The `ObjectDB` postfixes force a full rules apply, not only the prefabs (7.2).
- Game code reads the rules written into the items (`TowerSync.Applied`), so a client waiting for the server's rules
  also leaves bash stagger on its creatures vanilla (2.0, 4).
- Heal also reverts copies that are no longer towers, and a stamp per `SharedData` makes repeated heals one lookup
  (2.0).
- The Custom trigger set comes from items with an icon plus the unarmed attacks; a missing non-Custom trigger falls
  back too (2.7).
- Watchdog (A) restarts its timer while the player cannot act and also waits for 3 rendered frames; watchdog (B) needs
  the bash to still be the current attack and the player alive (2.7).
- The tower list warnings (MISSING, not a shield, can parry) are given again after the mod is turned off and on (2.0).
- The `RPC_Damage` finalizer puts back the saved per-hit state (nested calls; also when the call throws) instead of
  clearing it (2.5, 2.6, stage 7).
- The push direction is flattened before the facing test (2.5 c).
- A prefab name that maps to a new prefab object gets a fresh snapshot (2.0).
- `FixHands` also acts when the tower is the hidden left item (2.1).
- Tooltip: "Cannot parry" in orange, and the "Braced" line leaves out parts at 0 and gives each part its own
  condition (2.3, 2.5, stage 7).
- Review fixes of stage 7 (each also in the text above): made-blockable only for hostile attackers; Custom refuses
  held, aimed, reloaded and attached attacks and watchdog (C) stops a bash that never ends; watchdog (B) also judges a
  bash replaced by a held button; no lock from a bash on a creature already staggering; the brace reserve counts the
  world stamina rate; the stagger modifier sum never below -1; `BashStagger` minimum 2.
- Self-tests: creatures with AI off, placed by collider radius; expected numbers from the live state (block hits:
  per hit, with body armor after the block); `tower.ng`
  raises the Draugr's stagger factor for one bash; extra checks (stands, animator names, HUD list, cooldown 0,
  unblockable setting off); `tower.bash` resets the fallback state first; `tower.toggle` drives `TowerSync` directly;
  the client half of the generation rule is left to M10 and M11 (7.4).
- Test plan: T18-T20 moved to the end of the single player group, C09 and C10 added, T18b may be skipped (8).
- Stage 8 (user feedback): bash cooldown, slower swing, stamina 20, lock 8 s under a new key, ShieldUp removed; rules
  layout 2 and network version 2 (decisions 30-34).
- Stage 9 (review): one heavy stagger per bash (decision 35), a press after the swing kept until the cooldown ends,
  `BashAnimation` read in any case; no wire change (the ×1 travels in vanilla's `HitData`), so network version 2 stays.
- Stage 10 (in-world run): self-tests check a free bash lane before each placed bash and put the player back where
  each test found him (7.4); no mod code changed.
