# Dual Wielding — design

| | |
|---|---|
| Mod | Dual Wielding |
| GUID / project | `MC.Combat.Weapons.DualWield` (`src/Combat/Weapons.DualWield/`, root namespace `MC.Combat.WeaponsDualWieldMod`, package `WeaponsDualWield`) |
| Category / scope | Combat / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1 (RPCs `<guid>.Settings` / `<guid>.SettingsRequest`, no ZDO key). Technically everything runs on the attacking player's own game and other games only need vanilla data (4.1), but the mod changes combat, so under the house rule the server refuses players whose game does not run it (`AllowPlayersWithoutMod`) and sends its combat settings to everyone (4.3, 4.4). |
| Sheet idea | `Dual wielding` ("Make so that one handed weapons can be dual wielded") |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`; `.ref/game-version.json` is missing), decompiled `assembly_valheim` in `.ref/`. Every weapon, shield, tool and torch (SharedData scalars, damages, both `Attack` objects' scalars, the equip status effect name), the Player animator (layers, parameters with types, every clip with its length and animation events) and every creature prefab dumped at runtime from a Debug self-test on 2026-09-29 (`Weapons.Moveset/DataDump.cs`, temporary). Food prefab names from the installed game's SoftRef `manifest_extended`. Smoothbrain DualWield and RustyMods DualWielder sources and the Goo's Combat Overhaul README read on 2026-09-29 (research briefs). BepInEx 5 `BepInIncompatibility` from the installed `BepInEx.xml`. Revised on 2026-09-30 after three adversarial reviews (vanilla rules, multiplayer and robustness, player experience); every finding was checked against `.ref` and the dump. Aligned on 2026-09-30 with the framework's new join-check support (`NetworkGate.PeerCompatible`, `PeerProblem`, `PeerStateChanged`, the `HelloState` RPC; `src/Shared/Framework`) and with the four sibling Combat designs (integration review). Revised again on 2026-09-30 after the user's first in-game test (build `3cda33b+dirty`): the swap key plays the equip and draw animations and takes a vanilla equip's time (G4, 2.3, D22, D26), and a sheathed pair is shown crossed in an X (G11, 2.7, D27); the equip queue, `VisEquipment.AttachBackItem` and the back joints were read in `.ref` for it (1.7, 1.8). After the in-world run of that build (2026-09-30), the crossed pair is kept level every frame (2.7, D27), and the cross-mod texts follow the siblings' second round (6.1). Revised once more on 2026-09-30 for the user's holster rule (swords, maces and axes on the back, daggers and knives on the side hip): two knives hang one per hip and a knife with a back weapon keeps vanilla's spots (G11, 2.7, D27); `AttachBackItem`'s joint choice and the body mesh's bind pose were read for it (1.7). |
| Status | Implemented (v0.1.0 code), in-game testing; user feedback of the first in-game test implemented, not run in game yet (section 10: implementation notes and deviations; their short list closes the section) |

`Weapons` is the system and `DualWield` the feature, so other weapon mods (for example `Weapons.Moveset`, built in the
same run) sit next to it under their own GUIDs. The GUID and the display name are permanent after release.

## Goal

Requirements, numbered. Sources: the idea sheet ("Make so that one handed weapons can be dual wielded"), the user's
chat message ("Expected behavior mostly described in the excel sheet. Can take inspiration from Goo's Combat Overhaul
dual wielding approach"), the user's decisions for this run (equip gesture, pairs, moves) and the user's feedback after
the first in-game test (2026-09-30: G4's animation and timing, G11) and the user's holster rule given after it
(2026-09-30, G11). Each item is scope and has at least one test (section 8).

1. **G1 Pairs.** A one-handed sword, axe, club (maces included) or knife can be held in each hand, in **any mix**
   (sword + axe, knife + mace, two of the same kind or even two copies of the same weapon). Spears never pair (their
   special attack throws the weapon). (User decision.)
2. **G2 Automatic pairing.** Equipping a second eligible one-handed weapon while the main (right) hand holds one pairs
   them: the new weapon goes to the off (left) hand. If the off hand already holds a paired weapon, the new one replaces
   it. (User decision.)
3. **G3 Main-hand key.** Holding a configurable key while equipping a one-handed weapon does the normal game equip
   instead: the weapon replaces the main-hand weapon (vanilla behaviour). (User decision.)
4. **G4 Swap key.** A configurable key swaps the two weapons between the hands. (User decision.) The swap plays a
   re-equip animation, like putting the weapons away and drawing them again, and takes as long as a vanilla equip, with
   the same speed rules; attacks wait for it, and the weapons change hands after the put-away part. (User feedback
   after the first in-game test, 2026-09-30: "Swapping hands should play a re-equipment animation (like when putting
   the weapon away / out) and have the same speed modifier.")
5. **G5 Shield.** A shield keeps its vanilla behaviour: it takes the left hand (replacing the off-hand weapon) and the
   main-hand weapon stays. (User decision.)
6. **G6 Moves.** Two knives fight with the vanilla dual-knife moves (Skoll and Hati, `KnifeSkollAndHati`); every other
   pair fights with the vanilla dual-axe moves (the Berserkir axes, `AxeBerzerkr`): their stance, attack combo and
   special attack, read from those vanilla items at runtime. (User decision.)
7. **G7 Both weapons fight.** Each hit of a dual move is struck by one of the two weapons, or by both for the blows
   that swing both blades at once, and each weapon's hit is entirely that weapon's: its damage and damage types (a
   frost sword's frost, a poison axe's poison), its attack status effect, backstab, tool tier, its own attack effects
   (the Blood weapons' low-health bonus, the Lightning weapons' strikes, health or eitr on hit), skill experience and
   durability loss. (Implied by "dual wielded"; the run's design line for this mod: the `Attack` clone's weapon is
   swapped per hit event.)
8. **G8 The pair holds.** The pair stays in the hands the player chose through everything the game does to hands:
   hiding and drawing weapons (R), eating (also when the player rolls, attacks or dives into water right after), crafting
   stations, beds, the barber, swimming, logging out and in, death with a "keep equipment" world, moving items in the
   inventory, and the hammer entry of the radial menu. (Needed for G1-G2 to work in real play; every existing dual wield
   mod had bugs here.)
9. **G9 Required everywhere** (house rule for mods that change the experience): `ModSide=Both`; the server refuses
   every player whose game does not run the mod (not installed, turned off, a network version that cannot talk to the
   server's, or inactive next to another dual wield mod) about a second after joining, or after turning it off while
   connected, unless its `AllowPlayersWithoutMod` is on; and its combat settings apply to every player.
10. **G10 Live toggle** (MC rule): the feature turns on and off live from the MC Mods panel; off behaves as if the mod
    was not installed.
11. **G11 Sheathed pair placed by weapon kind.** When both weapons of a pair are put away (the hide key, a station, a
    bed, swimming), where each is holstered depends on the weapon: two back weapons (swords, maces, axes, in any mix)
    are crossed in an X on the back instead of side by side on one spot; two knives hang one on each hip (the
    main-hand one at vanilla's hip spot, the off-hand one at its mirror image on the other hip); a knife with a back
    weapon (either hand) keeps vanilla's placement, each at its own spot. One sheathed weapon stays exactly vanilla.
    Cosmetic, on every game with the mod, for every player it draws. (User feedback after the first in-game test,
    2026-09-30: "They are holstered side by side on the back instead of being crossed like an X." Then the user's
    rule, 2026-09-30: "When dual wielding, where the weapon is holstered depends on the weapon: swords, maces and axes
    go on the back, daggers and knives go on the side hip.")

### Added beyond the request

Small, each with a setting and a test:

- **E1 Off-hand swing trails.** Vanilla draws the swing trail only on the right-hand weapon; the off-hand weapon gets
  one too (personal cosmetic setting `LeftHandTrails`).
- **E2 Swap message.** A short top-left message names the new main-hand weapon after a swap.
- **E3 Move settings.** Which vanilla item lends its moves (`PairMoves`, `KnifePairMoves`) and whether the special
  attack is the dual item's or the main weapon's own (`SecondaryMoves`), validated at runtime, so the feel can be tried
  in game without a rebuild.
- **E4 Exclusion list.** `ExcludedWeapons`: prefab names that never pair (for modded weapons that look wrong).
- **E5 Both-hands hit pattern** (from the user's pointer to Goo's Combat Overhaul, whose dual wielding strikes with both
  hands on every hit): `HitPattern = BothHands` makes both weapons strike on every hit event, each at `BothHandsDamage`
  percent. It reuses the both-hands blow that the default pattern already needs for the specials (2.6), so it costs one
  setting. Default `Alternate` (decision D7, flagged).

### Non-goals

- New animation clips or asset bundles (Smoothbrain DualWield's approach): the pair only uses animations the player
  animator already has.
- Two-handed weapons, spears, bows, crossbows, staffs, tools or torches as a pair member.
- Any change to the vanilla dual weapons (Berserkir axes, Skoll and Hati), to single-weapon play, or to shields.
- Dual wielding for creatures and NPCs.
- An attack button for the off hand alone.
- A new "Dual Wielding" skill (each weapon trains its own skill, decision D12, flagged).

### Later

Cut from v1 (risky or polish), each with a one-line sketch:

- **L2 Block with the better weapon**: a postfix on the private `Humanoid.GetCurrentBlocker` for the local pair that
  returns the weapon with the higher block power.
- **L4 Left-hand grip correction** if the in-game check (R5) shows blades facing the wrong way: rotate
  `m_leftItemInstance` after `VisEquipment.SetLeftHandEquipped`, per weapon family.
- **L5 Gamepad controls**: hold the gamepad alt button while equipping for the main hand, a button chord for the swap
  (D16, flagged: gamepad players have no main-hand key and no swap in v1).
- **L6 Tooltip line** "Can be wielded in either hand" (`ItemData.GetTooltip` postfix).
- **L7 A Dual Wielding skill** for off-hand hits (needs skill registration; `Skills.Load` drops unknown skill ids).
- **L8 Special attack with the Club in the main hand**: an `ItemData.HaveSecondaryAttack` postfix, plus an inline
  guard like Forge Idol Upgrades' `IconInlineGuard` (the method is tiny and can be inlined).
- **L9 Keep the dual stance while eating** (count the hidden main weapon as present in the stance check).
- **L10 Keep the off-hand weapon while a bomb, a tankard or Sneak Ambush's Smoke Screen is in the main hand**, and
  rebuild the pair after the throw. Raised by the Sneak Ambush sibling: its stealth build (knife pair + Smoke Screen)
  loses the pair at every throw in v1 (listed at the pause).
- **L11 Per-pair attack speed** (animator speed on the dual clips).
- **L12 Hand table correction** if the in-game check (R4) shows the clips land the blades in another order than the
  table of 2.6.
- **L13 Armor stands showing a pair.**
- Running and jump attacks for pairs (GCO gives Smoothbrain pairs running and jump attacks): the Weapon Moveset mod's
  job (jump and roll attacks), see 6.1.

(L1, both hands on every hit, moved into v1 as E5. L3, a mirrored back mount for the sheathed off-hand weapon, moved
into v1 as G11 after the user's feedback, 2026-09-30, without a joint of its own: 2.7.)

---

## 1. Vanilla behaviour (code trace)

Only what the design relies on. Line numbers refer to `.ref/decompiled/assembly_valheim` (1.0.16).

### 1.1 Hands and the equip rules

- `Humanoid` has two hand slots: `m_rightItem` (main hand) and `m_leftItem` (off hand), plus `m_hiddenRightItem` /
  `m_hiddenLeftItem` for weapons put away by the hide key, eating, swimming, crafting stations, beds and the barber. No
  slot is saved: only `ItemData.m_equipped` (a per-item flag) and `m_customData` are (`ItemData.Save`).
- `Humanoid.EquipItem(item, triggerEquipEffects)` (1055-1272) first checks, in this order: already equipped
  (`IsItemEquiped`), not in the inventory, `InAttack() || InDodge()`, a player swimming off the ground, broken
  (`m_useDurability && m_durability <= 0`), missing DLC (with a message), and a New Game+ world-level rule for utility
  and trinket items only. Then one branch per `m_shared.m_itemType`:
  - `OneHandedWeapon` (1130-1151): a right-hand torch with an empty left hand moves to the left hand; then the right item
    is unequipped, the left item is unequipped **unless it is a `Shield` or `Torch`** (1140), the new item takes the
    right hand, its `m_equipEffect` plays at the right hand. So a second one-handed weapon never stays: vanilla has no
    way to hold two.
  - `Torch` (1104-1128): goes to the left hand only when the right hand holds a `OneHandedWeapon` **and the left hand is
    empty**; otherwise it empties the right hand and a non-shield left item and takes the right hand.
  - `Shield` (1152-1165): replaces the left item, keeps a one-handed weapon or torch in the right hand.
  - `Tool` (right then left unequipped), `Bow` and `TwoHandedWeaponLeft` (both unequipped, item to the left),
    `TwoHandedWeapon` (both unequipped, item to the right).
  - Every hand branch clears both hidden items. After the branch: `m_equipped = true`, the private `SetupEquipment()`,
    and `TriggerEquipEffect` (a sound) when asked.
- The same method equips creatures' weapons (`Humanoid.GiveDefaultItems`, `EquipBestWeapon`); most creature attacks
  are `OneHandedWeapon` items (runtime dump). Everything this mod changes must be limited to the local player.
- `Humanoid.UnequipItem` (1274-1352): clears hidden references to the item, returns if it is not equipped; for a
  weapon, stops `m_currentAttack` only when `m_currentAttack.GetWeapon() == item`; then empties whichever slot holds it
  (right first), `m_equipped = false`, `SetupEquipment()`, unequip effect.
- `Humanoid.SetupEquipment` (1428-1447): the owner writes the hand items to the ZDO (`SetupVisEquipment`), then
  `UpdateEquipmentStatusEffects` and `SetupAnimationState`.

### 1.2 Every path that takes weapons out of the hands or puts them back

| Path | Code | What it does to a pair (without the mod's handling) |
|---|---|---|
| Hide / draw key (R) | `Player.Update` (961-972): hands not empty → `HideHandItems()` (refused while attacking or dodging), else `ShowHandItems()` | Hide unequips left, then right, into the hidden slots. Show re-equips **hidden left first, then hidden right** (`Humanoid.ShowHandItems`, 1965-1990): the old off-hand weapon lands in the right hand, then the old main weapon arrives second. |
| Crafting station, bed, barber, swimming | `Player.SetCraftingStation` (4369), `Player.AttachStart` with `hideWeapons` (6398; only `Bed` passes true, 97: chairs, saddles, ship helms and the barber chair pass false), `PlayerCustomizaton` (323), `Humanoid.UpdateEquipment` while swimming off the ground (385) | `HideHandItems()` (full); only the hide key shows them again. A full hide returns early only when **both** hands are empty (1940); otherwise it overwrites `m_hiddenRightItem` with the current right item, even if that is empty. |
| Eating | `Humanoid.UseItem` → `SetUseHandVisual` (985-994) → `HideHandItems(onlyRightHand: true)`; the timer runs in `UpdateUseVisual` (434-454), called from every `FixedUpdate` whatever the player does (266); at the end `ShowHandItems(onlyRightHand: true)`. **`UseItem` calls `SetUseHandVisual` only when `ObjectDB.TryGetItemPrefab(item.m_shared)` finds the item's `SharedData` object (917)**, and in a build every `Object.Instantiate` of an item prefab (loading, pickup, crafting, `Inventory.AddItem(name, ...)`) deep-copies it (Tower Shield Wall design 1.9, Forge Idol Upgrades; confirmed by the first `dual.keep` run: a `Raspberry` from `Inventory.AddItem(name)` played the eat animation with no timer). So most food eaten from the inventory hides nothing; only items still holding the prefab's own `SharedData` (`ItemData.Clone` of the prefab's: `Inventory.AddItem(GameObject, int)`, a chest's first `m_defaultItems` loot, a destroyed piece's resources in its loot container) take this path, until the next load. The other caller is `Player.Interact` → `DoInteractAnimation` (966-983) on a world object that is a `Consumable` `ItemDrop` piece (a feast, if its prefab is one: the `dual.data` NOTE says) | Only the right hand is hidden; the off-hand weapon stays equipped alone for about a second (`m_foodEatAnimTime`, 1 s for most food). Then `ShowHandItems` **clears `m_hiddenRightItem` before** calling `EquipItem` (1981-1985), and `EquipItem` refuses while attacking, dodging or swimming off the ground: the main weapon then leaves the hands for good (a vanilla hole for single weapons too). Eating does not block a dodge (`Player.UpdateDodge`, 5769-5790, has no minor-action check). When `EquipItem` succeeds, its one-handed branch would unequip the off-hand weapon (line 1140). |
| Load (login, respawn) | `Player.Load` → `EquipInventoryItems` (4998-5007) | `EquipItem(item, false)` for every `m_equipped` item in **inventory list order** (`Inventory.GetEquippedItems`); a failure clears `m_equipped`. `Game.SpawnPlayer` calls `SetLocalPlayer()` before `LoadPlayerData` (Game.cs 489-490), so `Player.m_localPlayer` is already this player. |
| Main menu preview | `FejdStartup.SetupCharacterPreview` | Loads the character with `Player.m_localPlayer == null`. |
| Inventory drag | `InventoryGui.OnSelectedItem` (882-936), `InventoryGrid.DropItem` (572-585) | Unequips the dragged item and the target item, moves them, then re-equips the item now at each slot with `EquipItem(..., false)`, all in one frame. A move re-adds the item as a **clone at the end of the inventory list** (`Inventory.AddItem`, 69-72; the original is removed), so the re-equip uses the clone (custom data copied, `ItemData.Clone`) and a moved item comes **last** in load order. A drop onto an occupied slot moves both items this way. |
| Radial menu hammer | `Valheim.UI.HammerItemElement` (Interact delegate) | Remembers `LeftItem` / `RightItem`, equips the hammer; on toggle-off unequips the hammer, then `EquipItem(lastLeft)`, then `EquipItem(lastRight)`, in one frame. |
| Death | `Player.CreateTombStone` → `UnequipAllItems()` (right, then left, ..., 1381-1392) unless the world keeps equipment | `Inventory.MoveInventoryToGrave` moves only items with `m_equipped == false` into the tombstone. |
| Unequip by the player, drop, Forge refinement | `Player.ToggleEquipped` → `UnequipItem`; `Humanoid.DropItem`; MC Forge Idol Upgrades `ForgeRefine` | A plain `UnequipItem`. |
| Other right-then-left sequences | the tool branch of `EquipItem` (1096-1098), `Player.UnequipDeathDropItems` (3254-3263, unused by vanilla 1.0.16), loadout and quick-slot mods | `UnequipItem(right)`, then `UnequipItem(left)`. |
| Auto-equip on pickup | `Humanoid.Pickup` (654) | Equips a picked-up weapon only when the right hand and the hidden right hand are empty. |
| Thrown bomb | `Attack.ConsumeItem` | Unequips and removes the last bomb of a stack. |

### 1.3 Current weapon, blocker, stance

- `Humanoid.GetCurrentWeapon()` (475-490): the right item if it is a weapon, else a left weapon that is not a torch, else
  the unarmed weapon. Attacks, key hints and the melee camera all use it. A weapon alone in the left hand would attack
  with the right-hand moves.
- `Humanoid.GetCurrentBlocker()` (492-499, private): **the left item if there is one**, else the current weapon.
  `BlockAttack` (1751+) and `UpdateBlock` (1891+) use it: block power, parry bonus (`m_timedBlockBonus > 1` within
  0.25 s), damage modifiers and block durability come from the left item. A player with a left weapon already blocks
  and parries with it.
- `Humanoid.SetupAnimationState()` (1449-1470, private): a left torch gives `LeftTorch`; any other left item gives **the
  left item's** `m_animationState`; else the right item's. `SetAnimationState` (1472) writes animator `statef` (float)
  and `statei` (int) through `ZSyncAnimation`, which the owner stores in the ZDO for every other game. Vanilla relies on
  this for every weapon pose. States: `OneHanded` 1, `Unarmed` 0 (single knives), `Knives` 11, `DualAxes` 15
  (`ItemDrop.ItemData.AnimationState`).
- Summed over both hands with no code change: equipment modifiers (`Player.UpdateModifiers`: two swords, axes or maces
  are −0.10 movement, like a sword and a round shield), equip status effects (a `HashSet`; the dump's `equipSE` line,
  `m_equipStatusEffect.name`, is empty for all 586 weapons, shields, tools and torches in 1.0.16), eitr regeneration,
  weight, set counts.

### 1.4 One swing

1. `Humanoid.StartAttack(target, secondaryAttack)` (280-316): refuses while attacking without a queued chain, dodging,
   unable to move (`!CanMove()`), knocked back, staggering or in a minor action; takes `GetCurrentWeapon()`; refuses if
   the requested attack has no animation (`HaveSecondaryAttack` / `HavePrimaryAttack`); clones `m_shared.m_attack` or
   `m_secondaryAttack` (`Attack.Clone` = `MemberwiseClone`), calls `attack.Start(..., weapon: currentWeapon,
   previousAttack: m_previousAttack, ...)`, and on success clears the action queue and stores the clone in
   `m_currentAttack`.
2. `Attack.Start` (356-454): refuses an empty `m_attackAnimation`; stores `m_weapon`; checks stamina with
   `GetAttackStamina() + 0.1`; then the **animation choice** (410-425): with `m_attackChainLevels > 1` it continues the
   chain when `previousAttack.m_attackAnimation == m_attackAnimation`, resets it when the level reached the end or more
   than 0.2 s passed since the last attack, and fires the trigger `m_attackAnimation + m_currentAttackCainLevel`
   (`ZSyncAnimation.SetTrigger`, an RPC to everyone); with `m_attackRandomAnimations >= 2` a random suffix (not stored);
   else the plain name.
3. `Attack.GetAttackStamina` (468-487): the clone's `m_attackStamina` × (1 + equipment attack-stamina modifier), status
   effects, minus `m_staminaReturnPerMissingHP` × missing health, then × (1 − 0.33 × skill factor of **`m_weapon`'s**
   skill). Paid once, on the first in-attack frame of `Attack.Update` (516-560), which also plays `m_weapon`'s start
   effect and sets `m_nextAttackChainLevel`.
4. Animation events: `Hit` and `OnAttackTrigger` both go through `CharacterAnimEvent` → `Humanoid.OnAttackTrigger`
   (553-560, **owner only**, and only while `m_currentAttack` and `GetCurrentWeapon()` exist) → `Attack.OnAttackTrigger`
   (607-662), which **returns before `DoMeleeAttack`** when `UseAmmo` fails or the character is staggering (609-612),
   then `DoMeleeAttack()` for `Horizontal` / `Vertical` attacks. The event carries no hand; a clip with two `Hit` events
   calls it twice on the same clone. `DoMeleeAttack` is called from nowhere else.
5. `Attack.DoMeleeAttack` (1240-1572) reads **from `m_weapon`**: trigger and hit effects (`m_shared.m_triggerEffect`,
   `m_hitEffect`, `m_hitTerrainEffect`), `m_tamedOnly` target filter, dodgeable, the skill (`m_shared.m_skillType`),
   the random skill factor, tool tier, attack status effect and its chance, skill level, item level, push force
   (`m_attackForce`), backstab bonus, blockable, **damage (`m_weapon.GetDamage()`, which includes damage types such as
   frost and poison)**, hit variant, spawn-on-hit-terrain, **durability loss** (once per call when something was hit,
   players only) and `m_shared.m_spawnOnHit`. It reads **from the clone itself**: the hit shape (`m_attackType`, range,
   angle, ray width, heights, offset, hit point type, multi-hit, `m_lowerDamagePerHit`, `m_hitTerrain`), the clone's
   own effect lists (`m_triggerEffect` 1251, `m_hitEffect` 1373, `m_hitTerrainEffect`), the skill switch
   (`m_specialHitSkill` on `m_specialHitType` destructibles such as trees, else `m_skillHitType` decides whether the
   skill is raised), `m_raiseSkillAmount`, `m_damageMultiplier` and the health-based multipliers
   `m_damageMultiplierPerMissingHP` / `m_damageMultiplierByTotalHealthMissing` (in `ModifyDamage`, 1007-1027), force
   and stagger multipliers, `m_attackHealthReturnHit` and `m_attackEitrAdd` (1412, 1432-1439), the clone's
   `m_spawnOnHit` / `m_spawnOnHitChance` (`SpawnOnHit`, 1579), `m_resetChainIfHit` (1440), adrenaline per character hit
   (`m_attackAdrenaline`), hit noise, `m_spawnOnTrigger`, `m_harvest`, `m_snowShovel*`, `m_attach`, `m_pickaxeSpecial`.
   The last chain step multiplies damage by 2 and push by 1.2 (1417-1421, hard-coded for melee), for every call in that
   step. When no character was hit, a player gains `m_attackMissAdrenaline` (−5, a `Player` field, 239) per call. Each
   call also sends one `FreezeFrame` RPC (not additive: `CharacterAnimEvent.FreezeFrame` resets its timer).
6. `Humanoid.UpdateAttack` / `OnWeaponTrailStart` / `Attack.Stop` use the clone and `m_visEquipment`; trails are
   switched by `VisEquipment.SetWeaponTrails` (348-366), which only touches `m_rightItemInstance` unless
   `m_useAllTrails`, and calls `GetComponentsInChildren` (an allocation) each time. `CharacterAnimEvent` trail events
   run on every game.
7. Backstab (`Character.RPC_Damage`, 2336-2339, on the victim's owner): only when the victim's AI is not alerted, the
   hit's `m_backstabBonus > 1`, and 300 s passed since that victim's last backstab; so in practice only the first hit of
   an ambush. A staggered creature takes ×2 (2340+).

Per-weapon attack values (runtime dump, primary attack; the secondary has the same kind of values):

| Field (clone) | Weapons with a non-default value |
|---|---|
| `m_damageMultiplierPerMissingHP` 0.002 (+20% damage at 100 missing health) | `SwordNiedhoggBlood`, `MaceEldnerBlood`, `SwordGold_BloodLightning`, `AxeGold_BloodLightning`, `KnifeGold_BloodLightning`, `MaceGold_BloodLightning` (0 on every other one-handed weapon) |
| `m_spawnOnHitChance` 0.25 (secondary 0.2) with an `m_spawnOnHit` prefab | `SwordNiedhoggLightning`, `MaceEldnerLightning` and the four `Gold_BloodLightning` weapons |
| `m_resetChainIfHit` Tree (the combo restarts on a tree hit) | one-handed axes except `AxeWood` and `AxeGold_BloodLightning`; `SwordNiedhoggBlood/Lightning/Nature`, `MaceEldnerBlood/Lightning/Nature`; the template `AxeBerzerkr` (primary); None on swords, maces, knives and `KnifeSkollAndHati` |
| `m_specialHitSkill` WoodCutting with `m_specialHitType` Tree (trains Wood Cutting on trees) | one-handed axes except `AxeWood` and `AxeGold_BloodLightning` (skill set, type None: a vanilla quirk) |
| `m_attackHealthReturnHit`, `m_attackEitrAdd`, `m_damageMultiplierByTotalHealthMissing`, `m_staminaReturnPerMissingHP`, `m_raiseSkillAmount` ≠ 1 | none on eligible weapons in 1.0.16 (read anyway, for modded weapons) |

### 1.5 The vanilla dual weapons and the animator (runtime dump)

The two vanilla dual weapons are **one `TwoHandedWeapon` item each** (the second blade is part of the item's model);
every hit uses that one item's damage.

| | `AxeBerzerkr` (+ `Blood`, `Lightning`, `Nature`) | `KnifeSkollAndHati` |
|---|---|---|
| Stance (`m_animationState`) | `DualAxes` (15) | `Knives` (11) |
| Primary | `dualaxes`, Horizontal, **chain 4**, stamina 16, range 2.2, angle 90, ray 0.3, lower damage per hit, hit noise 40, speed factors 0.2 / 0.3, **chain reset on trees** | `dual_knives`, Horizontal, **chain 3**, stamina 14, range 1.8, angle 60, ray 0.2, hit noise 5, speed factors 0.3 / 1, no chain reset |
| Secondary | `dualaxes_secondary`, Horizontal, no chain, stamina 32, damage ×1.5, force ×1.5, angle 90, **full damage on every target** (`m_lowerDamagePerHit` false) | `dual_knives_secondary`, Horizontal, no chain, stamina 42, damage ×3, force ×4 |
| Block | 57, parry ×2 | 24, parry ×4 |
| Damage | slash 140, chop 80 (Ashlands tier; the Ashlands sword `SwordNiedhogg` is slash 135) | slash 45, pierce 45 (Mistlands; `KnifeBlackMetal` is 34 / 34) |

So a vanilla dual weapon hits about as hard **per hit** as a one-handed weapon of its tier, costs the same stamina per
swing, and gets its edge from more hits per combo and no shield.

Player animator (dump): layers `Base Layer` and `upperbody`; trigger parameters **`dualaxes0`-`dualaxes3`,
`dualaxes_secondary`, `dual_knives`, `dual_knives0`-`dual_knives2`, `dual_knives_secondary`** exist (there is no plain
`dualaxes` trigger); `statef` Float and `statei` Int exist. Clips and their events (length in seconds):

| Clip | Length | Events | Trigger (inferred from names and order, R2) |
|---|---|---|---|
| DualAxes Attack 1 | 1.27 | TrailOn, **Hit** 0.21, Chain 0.26 | `dualaxes0` |
| DualAxes Attack 2 2 | 1.43 | TrailOn, **Hit** 0.37, Chain 0.53 | `dualaxes1` |
| DualAxes Attack 3 2 | 1.50 | TrailOn, **Hit** 0.33, **Hit** 0.55, Chain 0.71 | `dualaxes2` |
| DualAxes Attack 4 | 1.50 | TrailOn, **Hit** 0.37, **Hit** 0.54 (no Chain) | `dualaxes3` (finisher) |
| DualAxes Attack Cleave | 1.90 | TrailOn, **Hit** 0.91 | `dualaxes_secondary` |
| Knife Attack Combo (1) / (2) | 0.40 / 0.40 | **OnAttackTrigger** 0.18, Chain | `dual_knives0` / `1` |
| Knife Attack Combo (3) | 0.50 | **OnAttackTrigger** 0.18, Speed | `dual_knives2` (finisher) |
| Knife Attack Leap | 1.50 | TrailOn, **Hit** 0.84 | `dual_knives_secondary` |

Plus `DualAxes Idle`, `Knife Idle`, `Knife Idle Block`, and 16 "New Dual Wield" / "Block DualWield" locomotion clips.
The wiki describes the Berserkir combo as "two single hits, one with each axe, followed by two double hits using both
axes", which matches the 1, 1, 2, 2 `Hit` events. For comparison, a sword combo is `Attack1` 1.13, `Attack2` 0.83,
`Attack3` 0.83 with one hit each; single knives `knife_slash0-2` 0.47 / 0.60 / 0.87.

### 1.6 One-handed weapons (runtime dump)

| Family | Prefabs (player items) | Skill | Stance | Primary | Secondary | Block |
|---|---|---|---|---|---|---|
| Swords | `SwordWood`, `SwordBronze`, `SwordIron`, `SwordIronFire`, `SwordSilver`, `SwordBlackmetal`, `SwordMistwalker`, `SwordNiedhogg` (+3), `SwordDyrnwyn`, `SwordGold` (+2) | Swords | OneHanded | `swing_longsword` chain 3, range 2.4, stamina 4-16 | `sword_secondary` ×3, stamina ×2 | 12-66, ×2 |
| Axes | `AxeWood`, `AxeStone`, `AxeFlint`, `AxeBronze`, `AxeIron`, `AxeBlackMetal`, `AxeJotunBane`, `AxeGold` (+2) | Axes | OneHanded | `swing_axe` chain 3, range 2.2; Wood Cutting on trees | `axe_secondary` ×1.5, Vertical, full damage on every target | 3-66, ×2 |
| Clubs, maces | `Club`, `MaceWood`, `MaceBronze`, `MaceIron`, `MaceSilver`, `MaceNeedle`, `MaceEldner` (+3), `MaceGold` (+2) | Clubs | OneHanded | `swing_longsword` chain 3, range 2.4 | `mace_secondary` ×2.5, stagger ×2, Vertical; **`Club` has none** | 3-66, ×2 |
| Knives | `KnifeWood`, `KnifeFlint`, `KnifeCopper`, `KnifeChitin`, `KnifeSilver`, `KnifeBlackMetal`, `KnifeVoid`, `KnifeGold` (+2), **`KnifeButcher`** | Knives | **Unarmed** | `knife_stab` chain 3, range 1.8, angle 60, stamina 4-14 (`KnifeBlackMetal` 12) | `knife_secondary` ×3, stamina ×3; **`KnifeButcher` has none and is `m_tamedOnly`** | 2 (KnifeWood 12), ×4; backstab ×6 |
| Spears | `SpearWood`, `SpearFlint`, `SpearBronze`, ..., `SpearChitin` (throw only) | Spears | OneHanded | `spear_poke`, no chain | `spear_throw` (Projectile: throws the item) | ×2 |
| Not weapons | `Tankard*` (skill Swords, primary `emote_drink` type **None**), bombs (`BombSmoke` etc.: skill None, `throw_bomb` Projectile, consumed) | | | | | |

Every eligible primary is `Horizontal`. Equip duration 0.2 s for all of them (so equips are queued, 1.8). The player
items all have a localized shared name (`$item_...`), 54 of them including the `FW_` / `SP_` copies (which share the
original's `m_name`, for example `FW_AxeBronze` = `$item_axe_bronze`). Another **184** dump entries also are
`OneHandedWeapon` with a sword, axe, club or knife skill and a melee primary: creature attacks and test items
(`Asksvin_Bite`, `troll_punch`, `draugr_axe`, `GoblinTorch` with the plain name `Torch`, `SwordCheat`), all with plain
names. Players only get those through the spawn command.

### 1.7 Visuals and what other games see

- `Humanoid.SetupVisEquipment` (1403-1426) writes `m_leftItem` / `m_rightItem` to the player's ZDO
  (`VisEquipment.SetLeftItem` / `SetRightItem`) and the hidden items to the back slots. Every game, **with or without the
  mod**, rebuilds the visuals from those ZDO values every frame (`VisEquipment.UpdateEquipmentVisuals`) and attaches any
  item's `attach` child to `m_leftHand` (`SetLeftHandEquipped` → `AttachItem`). A one-handed weapon in `m_leftItem` is
  therefore visible to everyone with no extra sync.
- Sheathed items: `SetupVisEquipment` writes `m_hiddenRightItem` to the `RightBackItem` ZDO value and
  `m_hiddenLeftItem` to `LeftBackItem` (players only), so every game knows both. `VisEquipment.UpdateEquipmentVisuals`
  (every frame, `CustomUpdate`) calls the private `SetBackEquipped` (936-966) for players (`m_isPlayer`, armor stands
  included): when any of the five back values (both hashes, the left variant, both qualities) differs from the current
  ones, it destroys both back instances and makes them again with `AttachBackItem`, then returns true.
  `AttachBackItem` (968-1001) picks the joint from `m_attachOverride`, else the item type: every one-handed sword, axe
  and mace goes to `m_backMelee`, every knife (override `Tool`, runtime dump) to `m_backTool`, spears (override
  `TwoHandedWeapon`) to `m_backTwohandedMelee`, a right-back torch to `m_backMelee` and a left-back one to `m_backTool`.
  The private `AttachItem` (1347-1415) instantiates the prefab's `attach_back` child (else `attach`), parents it to the
  joint at local position zero and identity rotation, then adds the prefab's `equipoffset` child pose. Both weapons of
  a pair therefore get the same joint and the same pose: they overlap exactly (two copies of a weapon look like one),
  or sit side by side when their models differ; a knife and a sword sit apart (two joints). The first in-world run's
  screenshots (`dual.visuals`) show a sheathed `SwordIron` diagonally across the back, hilt over the right shoulder, and
  a knife hanging nearly vertical at the right hip. Its NOTE names the joints: `m_backMelee` is `BackOneHanded_attach`
  under the `Spine1` bone (about 0.17 m right, 1.60 m up, 0.06 m behind the player root in the idle pose),
  `m_backTool` is `BackTool_attach` under the `Hips` bone (about 0.18 m right, 0.91 m up, 0.07 m behind): vanilla has
  one hip spot, on the right, and no left one. The player's body is the `SkinnedMeshRenderer` `m_bodyModel`; the game
  swaps only its `sharedMesh` for the male and female models (`UpdateBaseModel`), so its `bones` stay the skeleton,
  and each mesh's `bindposes` give every bone's frame in the mesh's bind pose (standard Unity skinning data).
- Triggers are RPCs to everyone and `statef` / `statei` are ZDO-synced, so every game plays the same moves and stance.
  An animator bool reaches other games only when it is in the prefab's `ZSyncAnimation.m_syncBools` list
  (`SyncParameters`, `SetBool` writes the ZDO value either way). The player's list holds `equipping` (runtime: Tower
  Shield Wall's `tower.data` NOTE lists the player's synced bools, among them `blocking`, `encumbered`, `equipping`
  and the `attach_*` bools), so other games play the equip animation of any equip, and of the swap (2.3).

### 1.8 Input and equip timing

- Inventory right-click (`InventoryGui.OnRightClickItem`), hotbar keys (`Player.UseHotbarItem`) and the radial menu
  (`Valheim.UI.ItemElement`, 59: `UseItem(null, item, false)`) reach `Humanoid.UseItem` → `Player.ToggleEquipped`
  (7234-7264). `ToggleEquipped` **returns true without doing anything while `InAttack()`**. Items with
  `m_equipDuration > 0` (all weapons: 0.2 s) are queued (`QueueEquipAction`, 7384-7394; **a second press on an item
  already queued cancels it**) and equipped later by `Player.UpdateActionQueue` (7308-7382), which calls
  `EquipItem(item)` with no context, one action per 0.2 s plus a 0.3 s pause. `UpdateActionQueue` runs in
  `Player.FixedUpdate` just before `PlayerAttackInput` (808-830). A successful attack clears the queue
  (`Humanoid.StartAttack` → `ClearActionQueue`).
- **The equip action in detail** (`QueueEquipAction` / `QueueUnequipAction`, 7384-7427; `UpdateActionQueue`,
  7308-7382): a `MinorActionData` with `m_duration = m_shared.m_equipDuration` (0.2 s for every one-handed weapon,
  shield and torch in the dump; 1 s for tankards and some creature items), `m_animation = "equipping"`, the HUD text
  `$hud_equipping <name>` (progress bar) and, only for a duration of 1 s or more, `m_equipStartEffects`. Each physics
  tick while it is first in the queue and the player is not attacking (`InAttack()` pauses it), the animator bool
  `equipping` is set through `ZSyncAnimation.SetBool` and `m_time` grows by the tick; when it passes the duration the
  bool is cleared, the action's `m_doneAnimation` trigger (if any; the crossbow reload uses `reload_crossbow_done`) is
  fired through `ZSyncAnimation.SetTrigger` (an RPC to everyone), then `EquipItem` / `UnequipItem` runs, then a fixed
  0.3 s pause (`m_actionQueuePause`) before the next action. **No equip speed modifier exists in 1.0.16**: nothing
  scales `m_equipDuration`, `m_time` or the pause (no skill, status effect, equipment modifier, and no world modifier:
  the `Game.m_*Rate` values cover damage, stamina, eitr, durability, food, adrenaline, skills, carry weight). What the
  player feels during an equip comes from the animator: `Player.InMinorAction` (the `upperbody` layer's state or next
  state tagged `minoraction` or `minoraction_fast`) makes `Humanoid.StartAttack` refuse attacks and stops blocking
  (`UpdateBlock`); `InMinorActionSlowdown` (tag `minoraction` only) makes the player walk (`Character.UpdateWalking`,
  `m_walkSpeed`) and stops swimming (`UpdateSwimming`). Which states carry these tags is animator data (R25). The queue
  is cleared (any queued equip cancelled) by a successful attack, a dodge (`UpdateDodge`), a jump (`OnJump`) and every
  tick of sprinting (`CheckRun`); a second request for a queued item removes it (`RemoveEquipAction`), and so does
  dropping it.
- **The hide / draw key** (R): `HideHandItems` / `ShowHandItems` (1938-1990) move the items at once and fire the
  trigger `equip_hip` (an RPC to everyone) when `animation` is true: the put-away and the draw play the same trigger,
  with no wait. The animator has an `unequip_hip` trigger that no code fires, and the clips `EquipBack` (1.17 s),
  `EquipMid` (1.00 s) and `EquipLoop` (3.97 s), with no animation events (dump); which state plays which clip is
  animator data (R25).
- `Player.TakeInput()` (2667) is false while chat, console, text input, store, inventory, menu, map, free-fly camera or
  the barber are open, and while dead, in a cutscene or teleporting.
- Vanilla keyboard bindings (`ZInput`): Left Shift is Run and AltPlace, Left Ctrl Crouch, R Hide, Q AutoRun, E Use,
  G radial, T emotes, 1-8 hotbar. **Left Alt and H are not bound.** Keys the game cannot map make `ZInput` throw every
  frame (MC Loot Pickup Filter validates its key once for this reason).

---

## 2. Design

### 2.0 The approach in short

The off-hand weapon lives in vanilla's own `m_leftItem`, so visuals, saving, equip markers, stat sums, death rules and
blocking come for free and every game sees it. A small set of prefix/postfix patches (no transpiler) decides which hand
an equipped weapon takes and keeps the pair in order; none of them acts in the middle of another caller's sequence of
unequips (a lone off-hand weapon is settled at the next frame, 2.4). While a pair is held, each swing's `Attack` clone
takes the moves of a vanilla dual item (`AxeBerzerkr` or `KnifeSkollAndHati`, read at runtime), and each hit event of
that swing is struck by the main weapon, the off-hand weapon, or both, according to a fixed table keyed by the
animation trigger that swing actually fired. The swap key goes through vanilla's own equip queue, so a swap looks,
lasts and gets cancelled like any equip (2.3). After vanilla builds the back items of a sheathed pair, the pair is
placed by weapon kind (2.7, cosmetic, on every game with the mod): two back weapons are crossed in an X (the off-hand
one re-posed as the mirror image of the other), two knives hang one per hip (the off-hand one moved to the mirror
image of its vanilla hip pose), and a knife with a back weapon stays as vanilla placed it.

What comes from Goo's Combat Overhaul (which does not implement dual wielding itself: it tunes Smoothbrain DualWield,
see the research brief): the off-hand damage factor as a setting, the both-hands hit model as an option (E5,
`HitPattern = BothHands`: GCO and Smoothbrain strike with both hands on every hit, each reduced), stamina from both
weapons (GCO uses their mean; we use the higher one, D9), each hand's own movement penalty (vanilla already sums them),
and running and jump attacks for pairs (left to the Weapon Moveset mod, 6.1). Not taken: Smoothbrain's custom clips
(non-goal) and GCO's dual-wield skill for the left hand (Later L7; D12, flagged). From DualWielder: the vanilla dual
triggers on the clone and a swap key.

### 2.1 Which weapons pair (G1)

`Eligibility.IsEligible(ItemData item)`, no allocation, used for both hands:

- `m_itemType == OneHandedWeapon`;
- `m_skillType` is `Swords`, `Axes`, `Clubs` or `Knives` (spears, bombs and unarmed are out);
- the primary attack has an animation, is `Horizontal` or `Vertical` and does not consume the item (tankards and bombs
  are out);
- not `m_tamedOnly` (the butcher knife, which only hurts tames: in the off hand it would hit your own tames in a fight;
  decision D5);
- not excluded: `ExcludedWeapons` prefab names are resolved once through `ObjectDB.GetItemPrefab` to each prefab, and
  an item is excluded when its `m_dropPrefab` **is** one of them, or its `m_shared` is that prefab's own `SharedData`
  object (reference compares). The drop prefab is the one that works for items a player holds: in a build every
  `Object.Instantiate` of an item prefab (loading, pickup, crafting) gives the item its own copy of `SharedData`
  (`ItemDrop.Awake` relinks `m_shared` only in the editor), but `ItemDrop.Awake` sets `m_dropPrefab` to the prefab
  (ItemDrop.cs:1266-1267), and `ItemData.Clone` keeps both. The `SharedData` compare covers the prefab's own
  `ItemData` and clones of it, which have no drop prefab. So excluding `AxeBronze` does not exclude `FW_AxeBronze`,
  which shares its display name. The set is rebuilt when the rules object or the `ObjectDB` instance changes; unknown
  names are logged once.

Creature attack items and test items (1.6) pass these rules; they are not filtered out (decision D24): players only get
them through the spawn command, a pair with one works, and the only cheap rule that tells them apart (a localized
`$` name) would also drop modded weapons that use plain names.

A **pair** = the local player holds eligible weapons in both `m_rightItem` and `m_leftItem`. Only a dual wield mod ever
puts a one-handed weapon in the left hand. Keeping a pair (2.4) looks at "the left item is a `OneHandedWeapon`", not at
eligibility, so a weapon that stops being eligible while held (the server's rules change) is still handled.

### 2.2 Equip rules (G2, G3, G5)

One `Humanoid.EquipItem` prefix, for the local player only. Notation: X = the item being equipped, R = right item,
L = left item. It first checks the vanilla early refusals listed in 1.1 (equipped, in the inventory, attacking or
dodging, swimming, broken, DLC); if any would refuse, it lets vanilla run, which refuses the same way with its own
message. Otherwise:

| # | Situation | Result | Vanilla would |
|---|---|---|---|
| K | X was asked for with the **main-hand key** held (G3) | vanilla equip: X takes the main hand, an off-hand weapon is put away | same |
| 1 | X eligible, **R** is an eligible weapon that arrived **this frame** as the first half of a re-equip (see 2.4), L empty, X not marked as an off-hand weapon | restore: R moves to the off hand, X takes the main hand | put X in the right hand, drop R |
| 2 | X eligible, R eligible, L empty or eligible | **X to the off hand** (G2); a weapon already there is unequipped; R's off-hand marker is cleared if it had one | replace R, drop L |
| 3 | X eligible, R empty, L eligible (a lone off-hand weapon: the main weapon is hidden for eating, or was put away earlier in this frame, 2.4) | **X to the main hand, L kept** | drop L |
| T | X is a torch, a pair is held | the off-hand weapon is unequipped, then vanilla runs, which puts the torch in the now empty left hand and keeps the main weapon (decision D4) | put the torch in the right hand, drop both weapons |
| V | anything else: shield (G5), bow, two-handed weapon, tool, spear, bomb, tankard, excluded weapon; an eligible X while the left hand holds a shield or torch; an eligible X with the right hand empty and no off-hand weapon | vanilla | |

Cases 1-3 finish like vanilla's own branches: `m_equipped = true`, both hidden items cleared, the item's
`m_equipEffect` at the hand it went to (left for case 2; only when `m_visEquipment.m_isPlayer` and not in the main
menu, as vanilla), the private `SetupEquipment()`, and `TriggerEquipEffect` when `triggerEquipEffects`. The prefix then
sets `__result = true` and skips the original. It does nothing when another mod's prefix already skipped the original
(`__runOriginal` false).

**Failure handling** (the prefix changes several hand fields itself before skipping vanilla):

1. *Decide*: `Hands.Route(player, X)` reads every input (the hands, the marker, the restore candidate, the intent) and
   returns the row, with no change. An exception here → report through `PatchGuard.Report` and return true: vanilla
   runs on untouched hands.
2. *Apply*: the row's changes run in a fixed order (the displaced item is unequipped through vanilla `UnequipItem`,
   then the slot fields, `m_equipped`, the marker, the hidden items, the effects) inside `try` / `finally`. If an
   exception escapes, the `finally` block re-syncs: for X, R and L as read in step 1, `m_equipped =
   IsItemEquiped(item)`, then `SetupEquipment()`; the prefix reports it and returns false with `__result =
   IsItemEquiped(X)`, so vanilla never runs on half-changed hands and no item stays flagged equipped in no slot.

Consequences the player sees (UI feedback: the weapon appears in the hand, vanilla equip sound, both items show the
vanilla "equipped" marker in the inventory and hotbar, the stance changes to the dual stance, 2.5):

- Sword in hand, click an axe: sword + axe. Click a mace: sword + mace (the axe goes back to the inventory).
- Sword in hand, Left Alt + click an axe: the axe alone (vanilla). With a pair: the axe alone, both others put away.
- Sword + axe, click a buckler: sword + buckler (G5). Then click an axe: axe + buckler (vanilla: a shield player
  switching weapons keeps the shield; decision D2).
- Sword + axe, click a torch: sword + torch (D4). Sword + axe, click a spear, a bomb or a Smoke Screen: that item alone
  (vanilla one-handed rule; Later L10).
- Click the off-hand weapon (hotbar or inventory): it is put away, the main weapon stays. Click the main weapon: it is
  put away and the off-hand weapon moves to the main hand (2.4).
- **First pairing of the session**, when it comes from the player's own request (hotbar, inventory, radial; not a load
  or a draw): a center message "Dual wielding: <weapon> is in your off hand. Hold <MainHandKey> while equipping to
  replace your main weapon instead. Press <SwapHandsKey> to swap hands." A part whose key is `None` is left out. Once
  per session: automatic pairing takes over the common "switch weapons on the hotbar" action, and nothing else
  tells the player how to get the old behaviour back.

**Main-hand key (G3).** The intent must reach the equip that the request causes, and only that one:

- `Player.ToggleEquipped` prefix + postfix, local player. The prefix reads `MainHandKey` (`ZInput.GetKey`, keyboard
  only) and, when it is held for an eligible item that is not equipped while the player is not attacking (vanilla then
  ignores the request), opens a **toggle window** for that item (an item with no equip duration is equipped inside the
  call). The postfix closes the window and, if the item is now queued (`IsEquipActionQueued`), stores an intent for
  that item with the time; otherwise (the press cancelled a queued equip, vanilla ignored it, or the key was not held)
  it drops any intent for that item. Intents are kept per item (a fixed array of 4 item/time pairs), so "Left Alt + 1,
  then 2" keeps the intent for 1 while 2 is queued.
- `Player.UpdateActionQueue` prefix + finalizer (local player, 50 Hz, two bool writes) open a **queue window** around
  the queued equip.
- `EquipItem` takes row K only inside a toggle or queue window and only for an item with an intent, which it consumes.
  Intents expire after 5 s (an attack clears the vanilla queue, so the equip may never come). The hide key, drags,
  loads, the radial hammer and pickups run outside both windows and never see an intent.

### 2.3 Swap key (G4)

A swap is **a vanilla equip of the off-hand weapon into the main hand**, run by vanilla's own action queue (1.8), so it
takes the time, plays the animation and follows the speed and cancel rules of every other equip (decision D22):

1. **Press.** `Player.Update` postfix, local player only; first check `ReferenceEquals(__instance,
   Player.m_localPlayer)`, then the cached key code (`SwapHandsKey`, validated once like MC Loot Pickup Filter's mark
   key: a key `ZInput` cannot map turns the swap off with a warning). On key down, `Hands.TrySwap` queues the swap when
   `TakeInput()` is true, a pair is held, the player is not attacking (nor starting an attack: in the restart gap
   between `StartAttack` and the animator entering the attack state, `InAttack()` is still false, so an unfinished
   current attack that has not entered its state and is younger than 0.5 s also refuses, the rule Weapon Moveset uses;
   review fix, section 10) and not dodging, no swap is queued already and vanilla's queue is empty (no other equip
   waiting). While sprinting (`IsRunning()`: vanilla's `CheckRun` cleared the queue this tick and does it again every
   tick) it queues nothing and shows the top-left message "Cannot swap hands while sprinting", so the key never does
   nothing silently (review fix, section 10). Otherwise it calls vanilla `Player.QueueEquipAction(offHandWeapon)`, finds
   that action in the queue and sets its `m_doneAnimation` to `equip_hip`, and remembers the item
   (`Hands.PendingSwap`). Nothing changes in the hands yet.
2. **While it runs** (the off-hand weapon's `m_equipDuration`: 0.2 s for every vanilla one-handed weapon): vanilla sets
   the `equipping` animator bool (the game's equip animation; a synced bool of the player, so every game plays it,
   1.7), shows the HUD bar "Equipping <weapon>" and marks the item as queued on the hotbar; whatever the animator's
   `minoraction` tags do during an equip (walk speed, no block) applies the same way (R25). The
   `Humanoid.StartAttack` prefix refuses every attack while the swap is queued (like the eat window, 2.4). A held
   attack button retries every tick and starts the swing as soon as the swap and the draw after it allow; a single
   press is kept only 0.5 s by vanilla (`Player.PlayerAttackInput`, `m_queuedAttackTimer`), so when the draw's
   animator state is a minor action (`Humanoid.StartAttack` refuses attacks then; R25) such a press may start the swing
   late or be lost (the `dual.equip` NOTE measures it; T32). A dodge, a jump or a sprint clears vanilla's queue and so
   cancels the swap, as for any equip (a sprint also shows the message above); a second press of the swap key does
   nothing. The pending swap is checked in the `Player.Update` postfix and in the `UpdateActionQueue` prefix, right
   before vanilla could end the action and fire its done trigger (so a pair lost inside a physics step, as swimming
   hides the hands in `FixedUpdate`, never plays a draw with nothing drawn; review fix): dropped when vanilla no
   longer has the action, and taken out of the queue (`RemoveEquipAction`) when the pair changed meanwhile (hidden,
   unequipped, eating, a rules change).
3. **End.** Vanilla clears `equipping`, fires `equip_hip` (the put-away / draw animation of the hide key, an RPC to
   everyone: the draw, played as the weapons change hands, like the hide key's draw) and calls
   `EquipItem(offHandWeapon)`. The `EquipItem` prefix sees the pending swap inside the queue window (the
   `UpdateActionQueue` prefix, 2.2) and, instead of vanilla (which would refuse an equipped item), swaps
   `m_rightItem` and `m_leftItem`, moves the off-hand marker (2.4) to the new off-hand weapon and clears it on the new
   main weapon, runs `SetupEquipment()` at once (new visuals in the ZDO for everyone, stance, status effects; before
   the cosmetic steps, so a failing effect never leaves the hands swapped in data only; review fix), then plays the new
   main weapon's `m_equipEffect` at the right hand (as vanilla's one-handed branch), `TriggerEquipEffect` (the equip
   sound), and shows the top-left message "Main hand: <weapon name>" (E2). If the pair changed in that same tick,
   nothing happens.
   Vanilla then pauses its queue 0.3 s, so swaps in a row come at most every 0.5 s (0.2 + 0.3; the fixed 0.5 s cooldown
   of the first version is gone).

The swap also **restarts the combo**: the next converted swing's `Attack.Start` prefix passes `previousAttack = null`
(a `ref` argument), so the player cannot swap between chained swings to pick which weapon takes the next step. With the
equip time the chain window (0.2 s, 1.4) is over anyway; the flag keeps the rule for weapons with a shorter equip time
(modded). The off-hand weapon is the one that blocks and parries (2.6), so the swap is also how a player chooses the
blocker, and (with a knife and a sword) which weapon's backstab opens an ambush (6.1).

### 2.4 Keeping the pair (G8)

Five small mechanisms, no transpiler, no scope counters:

1. **Off-hand marker.** `m_customData["MC.Combat.Weapons.DualWield.OffHand"] = "1"` (through
   `MC.Shared.ItemDataExtensions`) on the item in the off hand. Saved with the item; vanilla ignores unknown keys. Set
   when an item enters the off hand (rules 1 and 2, swap); cleared when an item is put in the main hand (rules 1 and 3,
   swap, the move below, whenever a marked item lands in the main hand through vanilla), on the main weapon when a
   pair forms (rule 2), on an item put away by `ReleaseOffHand`, and on `m_rightItem` / `m_hiddenRightItem` in
   `OnActivated` and whenever the local player changes (after a load). The last three catch a stale marker left by an
   item that reached the main hand while the mod was off: two marked weapons would swap hands at the next draw (rule 1
   refuses a marked X). An ordinary unequip never clears it: the hide key, drags, death and the Forge all unequip before
   re-equipping.
2. **Same-frame restore (rule 1).** When vanilla puts a marked item into an empty right hand (an `EquipItem` postfix
   sees `__result` true, `m_rightItem == item`, marked), the marker is cleared and the item is remembered as the
   "restore candidate" for the current frame (`Time.frameCount`). If an unmarked eligible weapon is equipped **in the
   same frame** while that candidate is still alone in the right hand, the candidate goes back to the off hand and the
   new weapon takes the main hand. Every vanilla re-equip burst happens in one frame (load, the hide key's show, the
   radial hammer, a drag), so the order they use no longer matters. Outside such a burst the candidate simply stays the
   main weapon.
3. **A lone off-hand weapon moves to the main hand, at the end of the frame.** `Hands.SettleLoneOffHand(player)`: when
   `m_rightItem == null`, `m_hiddenRightItem == null` and `m_leftItem` is a `OneHandedWeapon`, the left weapon moves to
   the right hand (marker cleared, `SetupEquipment()`, no effect). It runs from the `Player.Update` postfix every frame
   (local player: three reference checks) and at the start of the `Humanoid.StartAttack` prefix (so a swing in the same
   physics tick never swings a lone left weapon with right-hand moves). It never runs inside an equip or unequip call,
   so every caller that unequips the right hand and then the left hand (vanilla `UnequipAllItems`, the tool branch of
   `EquipItem`, `UnequipDeathDropItems`, loadout or quick-slot mods) finds the left weapon where it left it, and both
   hands end empty. Within the frame, an equip sees "R empty, L eligible" and takes rule 3, which is exactly what a drag
   of the main weapon needs. While eating, `m_hiddenRightItem` is set, so nothing moves.
4. **Eating** (the only vanilla path that leaves the off-hand weapon alone on purpose):
   - The `Humanoid.StartAttack` prefix refuses an attack (`__result = false`, original skipped) while the pair's main
     weapon is hidden for eating (local player, `m_rightItem == null`, `m_hiddenRightItem != null`, `m_hiddenLeftItem ==
     null`, the left item a `OneHandedWeapon`): under a second, and it avoids swinging the lone left weapon with
     right-hand moves. The eat animation may already refuse attacks as a minor action (R21).
   - `Humanoid.ShowHandItems` prefix (local player, `onlyRightHand` true, a hidden main weapon, a `OneHandedWeapon` in
     the left hand or hidden left): when `InAttack()`, `InDodge()` or swimming off the ground (the cases where vanilla's
     `EquipItem` refuses), it skips the original, so `m_hiddenRightItem` is kept, and flags a pending return; the
     `Player.Update` postfix calls `ShowHandItems(onlyRightHand: true, animation: false)` again as soon as none of these
     hold, and drops the pending return when `m_hiddenRightItem` became empty (the player equipped something else, which
     clears hidden items as in vanilla). Vanilla would drop the main weapon from the hands for good.
   - `Humanoid.HideHandItems` prefix + postfix (local player): a full hide during the eat (diving into water, opening a
     station) keeps the eat-hidden main weapon in `m_hiddenRightItem` (vanilla overwrites it with the empty right hand
     because the left hand is not empty), and the eat return then draws both hidden weapons (the prefix of
     `ShowHandItems` sets `onlyRightHand = false` when the hidden left item is a `OneHandedWeapon`): hidden left first
     (vanilla, restore candidate), then the main weapon (rule 1). Vanilla draws the main weapon at that point too.
     The same keep applies to the right-hand-only hide of a **second eat inside the eat window** (vanilla lets the
     player eat again at once; every eat that shows the food calls `SetUseHandVisual`, which calls
     `HideHandItems(onlyRightHand: true)`): without it vanilla would write the empty right hand over the hidden main
     weapon, and the lone off-hand weapon would move to the main hand next frame. With it the second eat's return
     draws the main weapon through rule 3. (Review fix, section 10.)
5. **Rules change.** The `Player.Update` postfix compares `ServerRules.Current`, `ObjectDB.instance` and
   `Player.m_localPlayer` with the last values it saw (three reference compares per frame). On a change it rebuilds the
   template and exclusion caches, clears stale markers (player change), and runs `Hands.Revalidate`: when both hands
   hold `OneHandedWeapon`s that are no longer both eligible (the server's `ExcludedWeapons` arrived or changed), the
   off-hand weapon is put away (`ReleaseOffHand`); then `SetupEquipment()`, so the stance follows the new moves
   settings at once. `OnActivated` resets the last-seen values, so the first frame after activation revalidates too.

Path by path:

| Path | What happens with the mod |
|---|---|
| Hide / draw (R), crafting station, bed, barber, swimming | Hide: vanilla (left first, then right; nothing moves in between). Draw: the old off-hand weapon (marked) lands in the right hand, then the main weapon arrives in the same frame: rule 1 restores the pair. Chairs, saddles and ship helms do not hide weapons (vanilla). |
| Eating | Food with its own `SharedData` copy (most food eaten from the inventory, 1.2): vanilla plays the eat animation and hides nothing; the pair stays in hand. Otherwise vanilla hides the right hand; the off-hand weapon stays; attacks are refused until the main weapon is back. The main weapon comes back through `EquipItem` with R empty and L eligible: rule 3 keeps the pair. Eat end while attacking, dodging or swimming: the return waits (mechanism 4). A full hide during the eat: both weapons stay hidden, the eat return draws both. A second food that hides the right hand, eaten during the eat: the main weapon stays hidden, the second eat's return draws it. |
| Login, respawn | `EquipInventoryItems` in either inventory order: off-hand weapon first → rule 1 when the main arrives; main first → rule 2 for the off-hand weapon. |
| Main menu preview | `m_localPlayer` is null: vanilla, one weapon shown. |
| Drag the main weapon to an empty slot | Unequip → lone off-hand weapon (not moved yet) → the main weapon's clone re-equips in the same frame → rule 3. Pair unchanged. |
| Drag the main weapon onto the off-hand weapon's slot, or the reverse | Both unequipped, both moved (clones), then re-equipped in the same frame: the main weapon first → vanilla, then the off-hand weapon (marked) → rule 2; the off-hand weapon first → vanilla + restore candidate, then the main weapon → rule 1. Pair unchanged. |
| Drag the main weapon into a chest, drop it, unequip it, refine it at the Forge | The off-hand weapon moves to the main hand at the end of the frame and stays there. |
| Drag or unequip the off-hand weapon | Main weapon unchanged; a drag re-equips it through rule 2 (off hand). |
| Radial hammer, then back | Hammer (tool) puts both away; on toggle-off `EquipItem(lastLeft)` then `EquipItem(lastRight)` in one frame: rule 1. |
| Death, normal world | `UnequipAllItems`: right, then left, nothing moves in between; both weapons unequipped, both go to the tombstone. |
| Death, world keeps equipment | Both stay equipped and flagged; respawn load restores the pair. |
| Other code unequipping right then left (loadout mods, future vanilla callers) | Both hands end empty, both `m_equipped` false. |
| Pickup auto-equip | Only when the right hand and hidden right hand are empty: vanilla (rule 3 in the rare frame after the main weapon was put away). |

Invariant checked by the self-tests after every step (a frame after it): every inventory item has `m_equipped ==
IsItemEquiped(item)`, no item is in two slots, at most one equipped item carries the marker and it is `m_leftItem`, and
no lone `OneHandedWeapon` sits in the left hand unless the main weapon is hidden for eating.

### 2.5 Moves and stance (G6)

**Templates.** `MoveTemplates` reads two vanilla items from `ObjectDB` at runtime: `PairMoves` (default `AxeBerzerkr`)
for every pair, and `KnifePairMoves` (default `KnifeSkollAndHati`) when both weapons are knives (`m_skillType ==
Knives`). A template is valid when the item exists, its primary attack is `Horizontal` or `Vertical`, and every trigger
it fires exists in the local player's animator: with `m_attackChainLevels > 1` the names `anim + 0 .. anim + (n-1)`,
with `m_attackRandomAnimations >= 2` the random suffixes, else the plain name
(`ZSyncAnimation.HasParameter(name, AnimatorControllerParameterType.Trigger)`, which copies the animator's parameter
array, so the result is cached). The trigger names are checked only when the cache is rebuilt (nothing per swing; the
hand table of 2.6 is fixed and needs no names from the templates). An item whose moves are not dual moves (a sword, an
axe) is a valid template: the hand fallback of 2.6 makes both weapons strike with it. The secondary is
validated the same way; a template without a valid secondary gives pairs the main weapon's own special (as
`SecondaryMoves = MainWeapon`). The cache is rebuilt when the rules object, the `ObjectDB` instance or the local player
changes (2.4, mechanism 5). An invalid setting falls back to the default item with a warning (once); if the default is
invalid too (a future game version), pairs swing with the main weapon's normal moves and stance and the off hand never
strikes, with a warning. The Info log names the moves in use, for example "Pairs use the moves of AxeBerzerkr (dualaxes0-3,
special dualaxes_secondary); two knives use KnifeSkollAndHati (dual_knives0-2, special dual_knives_secondary)".

**Stance.** `Humanoid.SetupAnimationState` postfix (private method, local player, only when a pair is held and the
template is valid): `SetAnimationState(template.m_shared.m_animationState)`, so `DualAxes` for axe-move pairs and
`Knives` for knife pairs, with their idle, locomotion and block poses. Synced to every game like any stance. Vanilla
would use the off-hand weapon's own stance (`OneHanded`, or `Unarmed` for a knife). Without a valid template (the
default moves unusable, or the animator not set up yet) the postfix sets the **main** weapon's own stance, matching
the main weapon's own moves the pair then swings with (review fix, section 10).

**Swing.** `Humanoid.StartAttack` prefix records whether the local player asked for the secondary attack (cleared in a
finalizer). `Attack.Start` prefix (`Priority.High`), when the character is the local player, a pair is held, `weapon`
is the main-hand weapon and the template is valid for this attack (for the secondary: `SecondaryMoves = PairMoves`):
copy the move from the template's `m_attack` (or `m_secondaryAttack`) into the clone, before vanilla chooses the
trigger. The **template-owned** fields:

- animation: `m_attackAnimation`, `m_attackChainLevels`, `m_attackRandomAnimations`;
- hit shape: `m_attackType`, `m_attackRange`, `m_attackAngle`, `m_attackRayWidth`, `m_attackRayWidthCharExtra`,
  `m_attackHeight`, `m_attackHeightChar1`, `m_attackHeightChar2`, `m_attackOffset`, `m_maxYAngle`, `m_hitPointtype`,
  `m_multiHit`, `m_lowerDamagePerHit`, `m_hitThroughWalls`;
- combo rule: `m_resetChainIfHit` (the Berserkir combo restarts on a tree hit, decision D21);
- feel: `m_speedFactor`, `m_speedFactorRotation`, `m_attackStartNoise`, `m_attackHitNoise`, `m_attackAdrenaline`;
- strength: `m_damageMultiplier`, `m_forceMultiplier`, `m_staggerMultiplier`;
- cost: `m_attackStamina` computed (2.6).

The prefix reads every value into locals first (template, both weapons, the stamina formula), then assigns them in one
block, so an exception cannot leave a half-converted clone (it reports and leaves the clone as vanilla made it). All
value fields: no allocation. Everything else on the clone stays the main weapon's own attack (the **weapon-owned**
fields, swapped per off-hand hit in 2.6). Because every dual swing now carries the same animation name, vanilla's chain
logic continues the combo from swing to swing (`dualaxes0` → `1` → `2` → `3`), and switching between a single weapon
and a pair restarts it. The shape comes from the template and not from the main weapon because the clips were authored
for that reach and arc, the axes' and maces' own specials are `Vertical` sweeps that do not fit a horizontal cleave,
and it makes every pair of the same kind play the same (decision D6).

**Special attack.** The pair's special is the template's (Berserkir cleave, Skoll and Hati leap), struck by both weapons
(2.6). `StartAttack` refuses a special when the **main-hand** weapon has none (`HaveSecondaryAttack`), so a pair with
the `Club` in the main hand has no special, exactly like the Club alone; swapping hands gives it one (decision D10).
With `SecondaryMoves = MainWeapon` the special is the main weapon's own vanilla special, untouched (only the main hand
strikes, in both hit patterns), for players who prefer it (a sword's own special hits a single target twice as hard per
stamina as the cleave, 2.6) or if the dual special looks wrong with some weapons.

### 2.6 Hits, damage, stamina, skills, blocking (G7)

**Which weapon strikes.** `Attack.Start` postfix, when the prefix converted the clone and `Start` returned true, reads
the trigger that was actually fired, after every prefix (Weapon Moveset's included) and vanilla's chain logic: the
clone's `m_attackAnimation` plus `m_currentAttackCainLevel` when `m_attackChainLevels > 1`, else the name alone. It
looks the trigger up in a **fixed hand table** (static, keyed by the base names `dualaxes` / `dual_knives` with the
chain level, and by the full one-off names; no string built). It records the swing: the clone, the main and the
off-hand weapons, the hand pattern for that trigger (null for a trigger not in the table), its chain step and an event
index at 0. `Attack.DoMeleeAttack` prefix (`Priority.First`): if
`__instance` is the recorded clone (one reference compare, the only cost for every other attack in the game), the hand
for this hit event is `pattern[index]`, then the index goes up. Patterns (`HitPattern = Alternate`, the default):

| Moves | Trigger | Hit events | Hands |
|---|---|---|---|
| Berserkir combo | `dualaxes0` | 1 | main |
| | `dualaxes1` | 1 | off |
| | `dualaxes2` | 2 | main, off |
| | `dualaxes3` (finisher, damage ×2) | 2 | main, off |
| Berserkir special | `dualaxes_secondary` | 1 | both |
| Skoll and Hati combo | `dual_knives0` | 1 | main |
| | `dual_knives1` | 1 | off |
| | `dual_knives2` (finisher, damage ×2) | 1 | both |
| Skoll and Hati special | `dual_knives_secondary` | 1 | both |
| Any other chain trigger (another `PairMoves` item, for example `swing_longsword0-2`) | | | alternate over the combo: chain step + event index even = main, odd = off (one-event swings: main, off, main) |
| Any other single move (such an item's special, a Weapon Moveset move outside the dual set, a random-animation attack) | | | both, on every event |
| An event past the pattern's end (a duplicated event, R19) | | | main |

The two "Any other" rows are a review fix (section 10): the first version alternated within the swing, main first,
and every vanilla non-dual clip has one hit event per swing (runtime dump), so with such a `PairMoves` item the off
hand never struck (G7 not delivered). The fallback still depends only on the fired trigger (base name and chain level).

`HitPattern = BothHands` (E5): every event of a converted swing is "both". The pattern depends only on the fired
trigger, never on earlier swings: an event that never reached `DoMeleeAttack` (a stagger, 1.4 step 4) or a duplicated
one cannot shift the hands of later swings, and a one-off move (Weapon Moveset's roll attack plays `dualaxes1`) strikes
with the hand the clip swings. The Berserkir rows are the wiki's "two single hits, one with each axe, followed by two
double hits using both axes". The specials and the knife finisher are single events that appear to swing both blades
(from the clip names and the vanilla items' look; unverified, R4), and "both" there gives each knife of a pair half of
the combo's damage (main: step 0 and half the finisher, off: step 1 and half the finisher). Which blade the clips
actually land first is unverified (R4); the self-test measures it and Later L12 covers a correction.

**An off-hand hit.** The prefix saves the clone's weapon-owned fields in a static struct (no allocation; not Harmony's
`__state`, because the postfix of a both-hands event needs them too; the re-entry flag keeps the second call from
touching it), replaces them with the off-hand weapon's, and a finalizer puts them back. The source is the off-hand weapon's own `m_attack` for
a combo swing, and its `m_secondaryAttack` for a special when it has one (else its `m_attack`), matching what the main
hand's clone holds:

- `m_weapon` = the off-hand weapon (everything 1.4 step 5 reads from `m_weapon`);
- `m_damageMultiplier` × `OffHandDamage` / 100 (the template's multiplier stays the base);
- `m_specialHitSkill`, `m_specialHitType`, `m_skillHitType`, `m_raiseSkillAmount` (an axe chops trees and trains Wood
  Cutting, a sword does not);
- `m_damageMultiplierPerMissingHP`, `m_damageMultiplierByTotalHealthMissing` (the Blood weapons' low-health bonus),
  `m_attackHealthReturnHit`, `m_attackEitrAdd`;
- `m_spawnOnHit`, `m_spawnOnHitChance` (the Lightning weapons' strikes);
- the references `m_hitEffect`, `m_hitTerrainEffect`, `m_triggerEffect` (a reference swap, no allocation).

The remaining fields `DoMeleeAttack` reads from the clone (`m_hitTerrain`, `m_spawnOnTrigger`, `m_harvest`,
`m_snowShovel*`, `m_attach`, `m_pickaxeSpecial`) are the same on every eligible weapon in the dump or unused by melee
weapons (the self-test asserts it, R23), and stay the main weapon's. So a Blood weapon keeps its bonus in either hand and
lends it to nothing else, and every value vanilla reads is the striking weapon's: damage and damage types (frost,
poison), attack status effect and chance, backstab bonus, tool tier, push force, blockable and dodgeable, skill, skill
level, random skill factor, effects, durability loss and the skill raise. Main-hand hits change nothing. If the
off-hand weapon left the left hand since the swing started, the main hand strikes instead (a "both" event becomes a
normal main-hand hit). The swap is limited to `DoMeleeAttack`, so `Attack.OnAttackTrigger` postfixes (MC Crossbow Stays
Loaded) and vanilla's "stop the swing when its weapon is unequipped" check keep seeing the main weapon.

**A both-hands event.** The prefix scales the main hand's `m_damageMultiplier` and `m_forceMultiplier` by
`BothHandsDamage` / 100 and `m_raiseSkillAmount` by 0.5, and vanilla runs the main hand's hit. A postfix then swaps in
the off-hand weapon's fields (as above, with `m_damageMultiplier` × `BothHandsDamage` × `OffHandDamage` / 10000 and
the same force and skill scaling), sets the clone's `m_attackAdrenaline` and the player's `m_attackMissAdrenaline` to 0
and the clone's `m_snowShovel` to false for this call (so one event gains or loses adrenaline once and clears Deep North
snow once: vanilla ends every `DoMeleeAttack` with the snow shovel sweep; review fix, section 10), sets a re-entry flag
and calls `__instance.DoMeleeAttack()`
once more (the private method is publicized; Smoothbrain DualWield strikes its left hand the same way). Our prefix
ignores the re-entrant call. The finalizer restores every saved field, the player's miss adrenaline and the flag, also
after an exception. Each weapon then hits the same targets once: its own damage, effects, durability loss (so both-hands
events wear both weapons) and half a skill raise; the event's total damage, push, stagger and skill experience equal one
weapon's hit at the default 50%. The second call plays the swing sound again, calls `AddNoise` again (the noise range
keeps its maximum, not additive) and sends a second `FreezeFrame` RPC (the pause is reset, not additive). A blocking
player (PvP) runs `BlockAttack` once per call: the damage, push and stagger of the two halves still add up to one hit,
but the per-block rewards that do not scale with damage (Blocking skill raise, block or perfect-block adrenaline,
`m_blockCharges` of shields that build charges, a perfect block's fixed stamina drain and regen) count twice. Accepted
and documented (README, M12); merging both weapons into one `HitData` would lose each weapon's own effects.

**Off-hand damage.** `OffHandDamage` (percent, default **100**): 100 means an off-hand hit is exactly that weapon's
normal hit, the way the vanilla dual weapons deal one weapon's damage on every hit (decision D8). The finisher ×2
applies to both of its hits (vanilla, per call). `BothHandsDamage` (percent, default **50**) is each weapon's share in
a both-hands event: 50 keeps a pair on par with the vanilla dual weapons in both patterns. Smoothbrain DualWield's
defaults (which GCO reduces "modestly") are 20-170% per hand by family and combo step, mostly 65-95% (much stronger
pairs).

**Stamina.** Per swing, set on the clone before `Attack.Start` checks it:

    m_attackStamina = max(main primary stamina, off primary stamina) × r × SwingStamina / 100

with r = 1 for the combo and r = template special stamina / template primary stamina for the special (32 / 16 = 2 for
the Berserkir moves, 42 / 14 = 3 for Skoll and Hati). Vanilla `GetAttackStamina` then applies the equipment and
status-effect modifiers and the main weapon's skill (−33% at skill 100), and pays once per swing, whatever the hit
pattern. Examples: `SwordIron` (10) + `AxeIron` (10): 10 per combo swing, 20 for the cleave, before skill.
`KnifeBlackMetal` (12) + `KnifeFlint` (4): 12 per swing, 36 for the leap. `KnifeBlackMetal` (12) + `SwordIron` (10):
12; `KnifeFlint` (4) + `SwordIron` (10): 10. Using the higher cost stops a cheap weapon in the main hand from paying for
a strong one in the off hand.

**Balance anchor.** With two weapons of the same tier (per-hit damage D, primary stamina S) and the defaults (both
hit patterns give the same totals):

| | Swings | Events | Damage (finisher ×2) | Per hand (main / off) | Stamina | Damage per stamina | Clip time (dump) |
|---|---|---|---|---|---|---|---|
| One weapon, 3-step combo | 3 | 3 | 4 D | | 3 S | 1.33 D/S | sword 2.8 s |
| Pair, Berserkir moves | 4 | 6 | 8 D | 4 / 4 | 4 S | 2 D/S | 5.7 s |
| Vanilla Berserkir axes | 4 | 6 | 8 D | | 4 S | 2 D/S | 5.7 s |
| Pair, Skoll and Hati moves | 3 | 3 | 4 D | 2 / 2 | 3 S | 1.33 D/S | 1.3 s |
| Vanilla Skoll and Hati | 3 | 3 | 4 D | | 3 S | 1.33 D/S | 1.3 s |

Specials (stamina in the same S; one special each):

| | Damage | Targets | Stamina | Damage per stamina |
|---|---|---|---|---|
| One sword (`sword_secondary`) | 3 D | less on each extra target | 2 S | 1.5 D/S |
| One mace (`mace_secondary`) | 2.5 D, stagger ×2 | less on each extra target | 2 S | 1.25 D/S |
| One axe (`axe_secondary`) | 1.5 D | full on every target | 2 S | 0.75 D/S |
| Pair, Berserkir cleave (both at 50%) | 1.5 D (0.75 per hand) | full on every target, 90° | 2 S | 0.75 D/S |
| Vanilla Berserkir cleave | 1.5 D | full on every target, 90° | 2 S | 0.75 D/S |
| One knife (`knife_secondary`) | 3 D | less on each extra target | 3 S | 1 D/S |
| Pair, Skoll and Hati leap (both at 50%) | 3 D (1.5 per hand) | less on each extra target | 3 S | 1 D/S |

So a pair of same-tier weapons plays like the vanilla dual weapons of that tier: about +50% damage per stamina on the
combo over one weapon, similar damage per second by clip time (chains cut clips at their Chain event, so the real timing
is unmeasured, R13), the same specials, no shield (block and parry with the off-hand weapon). A sword or mace in the
main hand trades its own strong single-target special for the crowd cleave (`SecondaryMoves = MainWeapon` gives it
back). A pair's extras over the vanilla dual items: two weapons' damage types, status effects and attack effects, and
two weapons' durability. `OffHandDamage`, `BothHandsDamage` and `SwingStamina` are the levers, server-synced.

**Skills.** Each hit trains the skill of the weapon that struck it (sword hits train Swords, axe hits Axes), with
vanilla amounts; in a both-hands event each weapon gets half, so one event always gives one hit's worth of experience.
No new skill (decision D12, flagged). The stamina discount uses the main weapon's skill.

**Blocking and parry.** Vanilla: the off-hand weapon blocks and parries (`GetCurrentBlocker` returns the left item),
with its block power, parry bonus, damage modifiers and block durability; the dual stance has its own block poses. A
knife in the off hand blocks badly (block power 2), like a single knife. The swap key picks which weapon blocks
(decision D11).

### 2.7 Cosmetics (E1, G11)

**Off-hand trails (E1).** `VisEquipment.SetWeaponTrails` postfix (every game with the mod): only for players (`__instance.m_isPlayer`), never on a
game without graphics (a dedicated server; a cached `SystemInfo.graphicsDeviceType == Null` check), when
`LeftHandTrails` is on, the character does not use `m_useAllTrails`, has a left item instance, and the left item is a
`OneHandedWeapon` (its prefab looked up by the synced `m_currentLeftItemHash`, cached per hash). Then the left
instance's `MeleeWeaponTrail` components get the same `Emit` value. The components are cached per left instance (a
small dictionary keyed by the instance, entry replaced when `m_leftItemInstance` changes and dropped when the instance
is destroyed), so the postfix does not allocate like vanilla's own `GetComponentsInChildren` call. For the local
player only, a running attack that is not the recorded converted swing (the main weapon's own special with
`SecondaryMoves = MainWeapon`, a pair without valid moves) gets no left trail, since the off hand does not strike it
(two reference compares; review fix, section 10); other players' swings are unknown to this game, so their rule stays
the left item. Both blades trail during dual swings, as the vanilla dual items appear to, for every dual wielder on the
screen of a player with the mod.
Players without the mod see no left trail. The off-hand grip is vanilla's (unverified, R5; Later L4).

**Sheathed pair placed by weapon kind (G11, decisions D27, D28).** `VisEquipment.SetBackEquipped` postfix (private,
every game with the mod, every player it draws), acting only when it returned true (vanilla just rebuilt both back
instances, 1.7; every other frame the cost is three bool reads, plus the retry below every 15 frames while a flat back
pair waits and the levelling below while some player has a crossed pair). `BackCross.Apply` leaves vanilla alone
unless: the character is a player and not an armor stand, the game has graphics (a cached
`SystemInfo.graphicsDeviceType` check, shared with the trails), `CrossSheathedPair` is on (personal setting, default
on), both back instances exist, and both back hashes are pair weapons (a `OneHandedWeapon` prefab with attach override
`None` or `Tool`, skill Swords, Axes, Clubs or Knives and a melee primary that consumes nothing, as `Eligibility`
pairs; cached per hash and `ObjectDB`). One sheathed weapon, a weapon and a torch or a shield, and spears stay exactly
vanilla. Then **where each weapon hangs is vanilla's own choice**: the joint `AttachBackItem` parented its instance to
(1.7: the attach override, else the item type; every one-handed sword, axe and mace goes to `m_backMelee` on the back,
every knife to `m_backTool` at the right hip). No skill list decides back or hip: a modded weapon follows its own
attach override, as vanilla places it alone. The user's rule (D27: swords, maces and axes on the back, daggers and
knives on the side hip) gives three layouts:

- **Both on `m_backMelee`** (two swords, axes or maces, in any mix): crossed in an X on the back (**crossing**,
  below), kept level.
- **Both on `m_backTool`** (two knives): one per hip (**knives on the hips**, below).
- **Anything else**: a knife with a back weapon (either hand), or instances another mod moved to joints of its own.
  Nothing is touched: each weapon stays at its own vanilla spot in its vanilla pose (the knife at the right hip, the
  other weapon on the back). That is the user's rule as vanilla already places it (the first version moved the knife
  onto the sword's joint and crossed it there).

**Crossing (two back weapons):**

1. **Shapes.** For each instance, in its vanilla pose, the biggest `MeshFilter` below it (LOD copies share its
   orientation): the mesh bounds' three edges in world space, sorted, give the long axis (signed so it points to the end
   that is up, the hilt or handle), the thin axis (the flat face's normal; "clear" when under 0.6 of the middle edge:
   blades, axe heads, knives; not a round mace head) and the centre. No mesh: vanilla.
2. **Reference.** The weapon that keeps its vanilla pose: the main-hand (right back) one, unless only the off-hand one
   has a clear thin axis.
3. **Plane.** The reference's outward normal: its thin axis (else the body's back direction), made perpendicular to its
   long axis and signed away from the body's vertical axis (the player root's position and up). The X lies in the
   plane perpendicular to it, so it follows the reference's own lean on the back. The plane's vertical is
   the body's up projected into it; its horizontal is their cross product. A plane nearly horizontal (a lying body)
   has no vertical: vanilla for now, tried again later (below).
4. **Angle.** The reference's angle from that vertical (the first in-world run's screenshot shows a sword at roughly
   30-40°, noted by `dual.visuals`, R26). Under 20° (a back weapon hanging nearly vertical: none of the game's so far;
   the rule was made for the knives, which no longer cross) the reference is turned about its outward normal, around
   its centre, to 30°; over 60° back to 60°; otherwise it is not touched.
5. **Mirror.** The other weapon is posed as the mirror image of the reference across the plane through the
   reference's centre that holds the outward normal and the vertical: its long axis is the reference's mirrored, its
   centre sits on the reference's centre, lifted 2.5 cm outward so the blades do not cut each other at the crossing
   (it lies on top). A rotation cannot mirror, so a weapon with a clear thin axis shows its other face (the face that
   was outward in vanilla looks in): its axe head, guard or edge then points to the mirrored side, as in a real mirror
   image; a round weapon keeps its own roll (the smallest turn). Both stay on `m_backMelee`. The crossing point is both
   weapons' mesh centres, whatever their lengths (a sword and a shorter axe or mace cross at both middles).

The pose is computed when vanilla builds the instances, from the vanilla pose and the body's up **on that frame**, and
stored relative to the joint, so the X follows the animated spine: a twist of the torso (about the up axis) or
a forward or backward bend of the joint (about the plane's horizontal, which lies in the plane) leaves the plane's
vertical where it is relative to the joint. A sideways lean of the joint (a turn about the plane's normal) does not:
the rebuild frame is itself a moving pose (the stance changes from the armed idle to the unarmed idle as the weapons
go away), and the in-world run of the crossing build (when two knives were still crossed at the hip) measured that
knife X 7.1° off the body's vertical two frames after it crossed (37.1° and 22.9°, the 60° opening intact: the hip had
leaned), while the sword X on the back stayed within 2°.
So the X is **kept level** (`BackCross.Level`, the same postfix on every frame without a rebuild, while some player
has a crossed pair): the plane's normal and the crossing point are taken from the joint as it is now, the X's middle
line is the sum of the two long axes, and when it is off the body's up (projected into the plane) by 0.25° or more,
both weapons are turned about the normal, around the crossing point, back onto it. The X keeps its opening and its
place on the joint, follows the twist and the bend, and holds its two angles about the body's vertical through a
sway, a walk or run cycle or the stance change (the in-world NOTE gives the total turn after 0.8 s). The postfix
runs in `Update`, so it sees the bones of the previous frame's animation: one frame late, a fraction of a degree in a
sway. Off by more than 45° (an odd pose where the body's up says nothing about the X: bent far over, head down in the
water) or with the plane near horizontal, the X is left riding the joint and levelled again once the pose comes back.
The reference's tilt (step 4) is still decided on the rebuild frame's angle; a sword sheathed mid-lean keeps that
opening, symmetric. A single sheathed weapon is not crossed, so never levelled: it keeps the game's pose. A
plane with no vertical on the rebuild frame (the body lying flat: a remote player's back items arriving while they lie
in a bed, the setting toggled while lying) leaves vanilla's overlap; that player is then tried again every 15 frames
(`BackCross.Retry` from the same postfix on frames without a rebuild, the instances still untouched), so the pair
crosses once the body is upright again (leaving a bed triggers no rebuild of its own; review fix).

**Knives on the hips (two knives).** Vanilla has one hip spot, on the right (`m_backTool`, `BackTool_attach` under
the `Hips` bone, 1.7). The main-hand knife stays there in its vanilla pose. The off-hand knife is given the mirror
image of **its own** vanilla pose (its prefab's `equipoffset` on `m_backTool`, where vanilla just put it) across the
body's centre plane as the hips bone carries it, and hangs from the hips bone (`BackCross.SplitHips`):

1. **Mirror plane** (`BackCross.HipsPlane`): through the hips bone's origin (the pelvis centre), its normal the
   player root's right expressed in the hips bone's frame **of the body mesh's bind pose**: the hips' index in
   `m_bodyModel.bones`, the mesh's `bindposes` at that index (the hips frame in the mesh, inverted), and the mesh's
   transform relative to the player root (fixed) map the hips frame into the root's frame, where the model's mirror
   plane is `x = 0`; the normal is carried back with the transpose of that map (right for any scale). The bind pose
   has the pelvis square, so this plane is the pelvis's own centre plane in every pose; a plane taken from the pose on
   the rebuild frame would bake in that frame's hip sway or twist (the reason D27 first rejected mirrored joints).
   When the hips are not a bone of the body mesh (an odd modded model) or the mesh gives no bind data, the plane comes
   from the rebuild frame's pose (the root's right in the hips frame) instead; the self-test checks the bind pose was
   used. Two small arrays are copied (`bones`, `bindposes`), only on a rebuild.
2. **Mirror:** the knife's shape as in crossing step 1, in its vanilla pose; its mesh centre and long axis are
   reflected across the plane, and since a rotation cannot mirror, its thin axis is the reflected one reversed: the
   point, edge and spine are mirrored exactly and the face that looked outward on the right hip looks in on the left
   one (a knife's two faces look alike), the same rule as the other weapon of an X.
3. **Parent:** the off-hand instance is re-parented to the hips bone (`SetParent(hips, true)`, world scale kept) and
   given that world pose. Vanilla destroys it at the next rebuild wherever it hangs.

Both knives then ride the same bone, so they stay mirror images about the pelvis's centre plane through every hip
sway, twist, walk or run cycle, lying down and swimming, with no work per frame: no levelling (turning them would
slide them along the belt line, and the main-hand knife must keep vanilla's pose) and no retry (the plane needs no
vertical). Against the player root's centre plane they move as the pelvis moves, as a single vanilla knife already
does. That turn is large in the unarmed idle: the in-world run of 2026-09-30 measured the pelvis 31° from the body's
centre plane two frames after the hide (the stance still changing) and 43° once settled, so the off-hand knife was
28-37 cm from the main knife's mirror image across the body's plane, while the two stayed exact mirror images across
the pelvis's plane. That is the belt look the rule asks for (a knife on the belt turns with the hips; the screenshots
show one knife at each hip, the main one a little behind the right thigh); mirroring across the body's plane instead
would leave the off-hand knife off the left hip by that much (D27). What holds in every pose, and what `dual.visuals`
checks in the idle pose and while walking, jogging and sprinting: each knife on its own side of the pelvis's centre
plane and of the body's, and outside its own thigh (its centre and lower end out from the thigh bone's line), never
between the legs. Knife pairs add nothing to the per-frame cost.

Nothing is synced or saved: every game with the mod places (and levels) every pair it draws; a game without the mod
shows vanilla's placement (two weapons of one kind overlapping on one spot). Turning the setting off or on, and
turning the mod on or off, marks every player's back visuals as changed (`m_currentRightBackItemQuality = -1`, a value
no quality has), so vanilla rebuilds them on the next frame: placed again while the patch is in, vanilla's own spots
and poses once it is gone (the moved knife is destroyed with the old instances); a rebuild ends the levelling of the
old instances.

### 2.8 UI feedback, per goal

| Goal | What the player sees |
|---|---|
| G1, G2 | The second weapon appears in the left hand with its equip effect; both show the "equipped" marker; the stance changes to the dual stance. The first pairing of the session shows the center message naming the main-hand and swap keys (2.2). |
| G3 | Vanilla equip (no pair). |
| G4 | The game's equip animation and the HUD bar "Equipping <weapon>" for the weapon's equip time (0.2 s), then the hands swap in view with the draw animation of the hide key, the equip sound and the top-left "Main hand: <name>"; attacks wait meanwhile (a held attack button swings as soon as the swap and the draw allow); while sprinting the key shows "Cannot swap hands while sprinting" instead; a dodge, jump or sprint cancels the swap like any equip; swaps in a row come at most every 0.5 s (equip time plus vanilla's queue pause); the next swing starts the combo from its first step. |
| G5 | Vanilla. |
| G6 | The Berserkir or Skoll and Hati moves and stance; the key hints are vanilla (from the main weapon). |
| G7 | Damage numbers, damage types, status effects and attack effects of each weapon; both-hands blows show two numbers; both weapons lose durability; both skills rise. |
| G8 | The same hands after every path in 2.4. While eating, the attack button does nothing until the main weapon is back (under a second). |
| G11 | A sheathed pair placed by weapon kind, on every player seen by a game with the mod: two swords, axes or maces crossed in an X on the back, two knives one on each hip (mirrored), a knife with a back weapon each at the game's own spot; one sheathed weapon as in the game. |
| Moves settings | One Info line naming the moves in use; a warning when a setting is invalid. |

### 2.9 Edge cases

- **Attack while eating**: refused until the main weapon is back (2.4, mechanism 4; the stance is the lone off-hand
  weapon's for that second, Later L9). **Roll, attack or dive right after eating**: the main weapon comes back as soon as
  the roll, attack or swim ends.
- **Equip while weapons are hidden**: vanilla forgets the hidden weapons (every hand branch clears them); same with the
  mod, and a pending eat return is dropped.
- **Swap key while sprinting, or another equip waiting**: sprinting clears vanilla's queue every tick, so the key
  queues nothing and shows "Cannot swap hands while sprinting" (a hotbar equip is dropped silently there; the swap
  says why); a sprint started during a queued swap drops it with the same message. With another equip in the queue
  the key does nothing. **Weapons hidden, unequipped or eaten away during a queued swap**: the swap is taken out of
  the queue before the queue's next step (or at the end of the frame), the hands stay as the other action left them.
- **Off-hand weapon unequipped mid-swing**: the swing goes on; its remaining off-hand hits are struck by the main hand.
  Main weapon unequipped mid-swing: vanilla stops the swing; the off-hand weapon moves to the main hand at the end of
  the frame.
- **Broken weapon**: vanilla refuses to equip a broken weapon, and a weapon worn to 0 during a fight stays in hand
  (vanilla, per hand).
- **Bomb, tankard or Smoke Screen while paired**: vanilla one-handed rule: both weapons are put away (Later L10).
- **Two copies of the same weapon**: allowed; stance, moves and skills as any pair.
- **Trees**: the combo restarts on every tree hit (the template's rule, D21), so on a tree every swing is `dualaxes0`,
  struck by the main-hand weapon: an axe in the main hand chops like a single axe; a sword or mace in the main hand hits
  the tree for nothing (and wears); press the swap key to chop with an off-hand axe. Two knives do not chop and their
  combo does not restart.
- **Server changes the rules while paired** (`ExcludedWeapons`, `PairMoves`, `KnifePairMoves`, `SecondaryMoves`): the
  next frame rebuilds the caches, puts away an off-hand weapon that is no longer eligible, and updates the stance
  (2.4, mechanism 5). At login on a server, `Player.Load` may run before the server's rules arrive (4.3): it then uses
  the built-in defaults (no exclusions, never the player's own settings), so a saved pair loads as a pair, and the
  server's rules are applied the frame they arrive, like a live rule change.
- **New Game+ worlds**: nothing special (the world-level rule of `EquipItem` concerns utility and trinket items).
- **Another mod puts a one-handed weapon in the left hand** (a different dual wield mod): this mod does not load, or
  goes inactive with a status naming that mod (6.3).
- **Quitting the game while dual wielding**: nothing is released (the save already ran; 7.4); the saved pair loads as a
  pair next time.

---

## 3. Decisions

Each: options, choice, reason. **Flag** marks a choice that deviates from the user's literal words or picks the
vanilla-consistent option over the literal request; they are listed at the pause.

- **D1 Where the off-hand weapon lives.** Options: vanilla `m_leftItem`; a new hidden slot with its own ZDO key, visuals
  and save. Choice: `m_leftItem`. Reason: visuals on every game (with or without the mod), saving, death rules, equip
  markers, stat sums and blocking all come from vanilla; a new slot would need all of that rebuilt.
- **D2 When the automatic pairing fires.** Options: whenever the main hand holds an eligible weapon (Smoothbrain,
  DualWielder); only when the off hand is empty or already holds a paired weapon (vanilla's torch rule). Choice: the
  second. A shield or torch in the off hand keeps vanilla: the new weapon replaces the main weapon. **Flag:** the
  user's words "while the main hand holds one pairs them" do not mention the shield case; reason: a sword-and-shield
  player switching weapons on the hotbar would otherwise lose the shield at every switch, and the user asked that the
  shield keep its vanilla behaviour.
- **D3 What the main-hand key does.** Options: exactly the vanilla equip (the new weapon replaces the main weapon, an
  off-hand weapon is put away); replace only the main weapon and keep the off-hand weapon. Choice: exactly vanilla.
  **Flag:** literal reading of "(vanilla behaviour)"; with a pair held, the key therefore ends the pair. Keeping the
  off-hand weapon instead is a one-line change if the user prefers it.
- **D4 Torch while dual wielding.** Options: vanilla code (the torch takes the right hand, both weapons are put away);
  the torch replaces the off-hand weapon and the main weapon stays. Choice: the second. **Flag:** deviates from the
  vanilla code; it is vanilla's own torch rule (a torch goes to a free left hand next to a one-handed weapon) applied
  after freeing the left hand, and it matches what a shield does (G5).
- **D5 Which weapons pair.** User: any mix of one-handed swords, axes, clubs and knives, no spears. Choice: that, minus
  tame-only weapons (`KnifeButcher`), items whose primary is not a melee swing (tankards, bombs), and the
  `ExcludedWeapons` list. **Flag:** the butcher knife is a knife but never pairs: it only damages tames, so in the off
  hand it would strike your own tames during fights (a known Smoothbrain DualWield bug), and it has no special attack.
- **D6 Where a dual swing's shape comes from.** Options: the main weapon's own `Attack` with only the animation fields
  replaced (DualWielder); the template item's move (animation, reach, arc, multipliers, combo rule). Choice: the
  template's move; the weapons only supply what weapons supply (the weapon-owned fields of 2.6). Reason: the clips were
  authored for that reach and arc; axe and mace specials are vertical sweeps that do not fit the cleave; it gives every
  pair of a kind the same, predictable moves; and it is what "the Berserkir moves" means. Consequence: a sword pair
  loses the single sword's full-damage cleave on several targets (`m_lowerDamagePerHit` comes from the template).
- **D7 Hit model.** Options: (a) one hand per hit event by a fixed table keyed on the fired trigger, both hands on the
  single-event blows that swing both blades (the specials, the knife finisher); (b) both weapons on every event, each
  reduced (Smoothbrain DualWield, which GCO builds on); (c) one weapon per combo step (DualWielder); (d) both weapons at
  their own contact times (DualWieldCore, needs edited clips); (e) a running hit counter across the combo (this
  design's first draft: drifts when an event is skipped or doubled, and cannot follow Weapon Moveset's one-off moves).
  Choice: (a) as the default `HitPattern = Alternate`, and (b) shipped as `HitPattern = BothHands` (E5, same
  mechanism). Reason: one hit per animation event, like the vanilla dual weapons, so a pair matches the Berserkir axes'
  balance exactly; every hit is a whole vanilla hit of a real weapon; deterministic. Alternating the knife combo across
  combos (M, O, M | O, M, O) was rejected for the both-hands finisher, which is simpler and gives each knife half.
  **Flag:** the user named GCO as inspiration, and GCO strikes with both hands on every hit (reduced) and trains a
  dual-wield skill for the left hand; our default alternates hands at full damage and trains each weapon's own skill.
  The GCO feel is one setting away (`HitPattern = BothHands`, `BothHandsDamage` 65-95 for GCO-like strength). Which
  default does the user want?
- **D8 Off-hand damage default.** Options: 100% (the vanilla dual weapons' balance: one weapon's damage per hit); 50-75%
  (balrond DualMastery, DualWieldCore 60%, GCO "modestly reduced"). Choice: 100%, as a server setting. **Flag:** a
  balance call to confirm in game; community mods reduce the off hand, but with our hit model a same-tier pair already
  matches the vanilla Berserkir axes exactly (2.6). Early pairs (two flint axes) get that +50% damage per stamina from
  the Meadows on, at the price of the shield.
- **D9 Stamina per swing.** Options: the main weapon's cost; the mean of both (GCO, ×1.85 on Smoothbrain's moves); the
  higher of both; a fixed table (Smoothbrain, which ignores equipment modifiers). Choice: the higher of both primary
  costs, times the template's special ratio for the special, times `SwingStamina`, then vanilla's modifiers. Reason:
  equals the vanilla dual weapons' cost for a same-tier pair, cannot be gamed with a cheap main weapon, keeps every
  vanilla stamina modifier.
- **D10 Special attack for clubs and the butcher knife.** Options: give every pair the dual special (patch
  `HaveSecondaryAttack`); follow the main-hand weapon: no special when it has none. Choice: follow the main weapon: a
  pair with the `Club` in the main hand has no special (like the Club alone); put the other weapon in the main hand to
  get one. The butcher knife never pairs (D5). **Flag:** the literal "every other pair uses the Berserkir moves"
  includes the special; reason: vanilla-consistent, avoids patching a tiny method that the JIT may inline (Later L8).
- **D11 Blocker.** Options: vanilla (the off-hand weapon); the main-hand weapon; the better of the two. Choice: vanilla,
  with the swap key to choose. Reason: no patch, the player controls it, matches vanilla for any left item. Better-of-two
  is Later L2.
- **D12 Skills.** Options: each hit trains its weapon's skill; a custom off-hand skill (Smoothbrain, balrond, GCO).
  Choice: each weapon's own skill, vanilla amounts (half each in a both-hands event). Reason: no skill registration,
  nothing lost if the mod is removed, mixed pairs train both skills naturally. **Flag:** GCO, the mod the user named,
  trains a dual-wield skill with the left hand; ours has none (Later L7).
- **D13 Stance.** Options: vanilla (the off-hand weapon's stance: one-handed idle with a weapon hanging in the left
  hand); the template's stance. Choice: the template's (`DualAxes` / `Knives`). Reason: the dual clips start from those
  states and their block and locomotion poses hold two weapons; synced to everyone for free. Unverified visually (R3,
  R10).
- **D14 Hand order.** Options: transpile `ShowHandItems` and `EquipInventoryItems` to equip right first (Smoothbrain);
  per-path fixes; an item marker plus a same-frame restore rule. Choice: marker plus same-frame restore. Reason: covers
  every re-equip burst (including the radial hammer's lambda, which cannot be patched cleanly) with Prefix/Postfix
  only.
- **D15 Live toggle off.** Options: leave the pair (vanilla code then mis-handles it: next equip drops it, the stance
  comes from the off-hand weapon); put the off-hand weapon away. Choice: put it away (it stays in the inventory).
- **D16 Keys.** `MainHandKey` = Left Alt (held), `SwapHandsKey` = H (pressed): both unbound in vanilla. Gamepad: no
  binding in v1 (Later L5). **Flag:** a gamepad or Steam Deck player cannot keep the vanilla "switch weapons" equip
  (every second one-handed weapon pairs) and cannot swap hands; the workaround is to unequip first. Ship a gamepad
  chord in v1?
- **D17 Other dual wield mods.** Options: run alongside; stand down inside the mod (every patch exits, invisible to
  the framework and the server); go inactive through the framework (a status, patches removed, the server told);
  refuse to load. Choice: refuse to load next to Smoothbrain DualWield (known GUID: `BepInIncompatibility`); go inactive
  next to the others (found by name, GUIDs unverified) through a small framework hook, `LocalBlocker` (6.3, 7.6).
  Reason: two mods routing the same equip and rewriting the same clone would fight; in both cases the player's game
  reports a copy that does not run, so the server's join check refuses that player (4.4) and G9 holds. The hook is
  now in the framework (`ModPlugin.LocalBlocker()`, state `ModState.Conflict`, 7.6) and the mod uses it; the
  stand-down fallback of 6.3 is not used.
- **D18 Moves settings.** `PairMoves`, `KnifePairMoves` (prefab names) and `SecondaryMoves` (PairMoves | MainWeapon), so
  the feel can be tried in game (the animations were made for the vanilla dual items). Server rules, validated at
  runtime, with fallback.
- **D19 Required everywhere.** Technically the mod works client-side only (4.1); under the house rule it is `Both` with
  the join check and server settings (G9).
- **D20 Eating.** Options: vanilla (the lone off-hand weapon may swing with right-hand moves; an eat that ends during a
  roll, attack or swim drops the main weapon from the hands for good); refuse attacks during the eat window and delay
  the main weapon's return until it can succeed. Choice: the second. Reason: G8 promises the pair survives eating, and
  rolling right after eating is common. The refusal lasts under a second.
- **D21 Chain reset on trees.** Options: the main weapon's own `m_resetChainIfHit` (axes: Tree; swords and maces: None);
  the template's (Berserkir: Tree). Choice: the template's. Reason: it belongs to the move (D6), and it keeps tree
  chopping simple and predictable: every swing on a tree is step 0, struck by the main hand, so an axe in the main hand
  chops like a single axe. Consequence: an axe in the off hand never chops (swap first). **Flag:** a player who pairs an
  axe next to a sword to chop may expect the axe to chop; the first-pairing message names the swap key.
- **D22 Swap timing** (revised 2026-09-30, user feedback after the first in-game test). First choice: instant, with a
  0.5 s cooldown. The user asked for a re-equip animation "with the same speed modifier" as putting weapons away and
  out. Options: (a) keep the instant swap and only fire an animation; (b) a timer of the mod's own copying the equip
  rules; (c) queue the swap as a vanilla equip action of the off-hand weapon (`Player.QueueEquipAction`) and swap the
  hands when vanilla ends it. Choice: (c), and the swap still restarts the combo. Reason: the timing, the HUD bar, the
  animation, the slowdown and the cancel rules are vanilla's own for every equip (a dodge, a jump or a sprint clears
  the queue, as a started attack would; the queue waits during attacks and pauses 0.3 s after each action), with
  nothing copied; a
  mod that changes equip times or the queue changes the swap the same way. There is **no equip speed modifier** in
  1.0.16 (1.8): "the same speed" is the weapon's `m_equipDuration` (0.2 s) plus the queue pause, and the walk speed
  and refused attacks the animator's `minoraction` tags give an equip. **Attacks** are also refused by the mod while
  the swap is queued (the `StartAttack` prefix): vanilla refuses them during an equip only if its animator state is a
  minor action (R25; otherwise a vanilla attack starts and cancels the equip), and the request says attacks wait for
  the swap. A held attack button then swings as soon as the swap and the draw after it allow; a single press is kept
  only 0.5 s by vanilla, so it may start late or be lost if the draw is a minor action (the `dual.equip` NOTE measures
  it). Sprinting clears vanilla's queue every tick, so the key refuses to queue there and says so (a top-left
  message) instead of doing nothing (review fix). **Flag:** confirm that an attack pressed during a swap should wait
  for it rather than cancel it (the `dual.equip` NOTE says what vanilla does during a hotbar equip, and whether one
  press during the swap still starts a swing).
- **D26 Swap animation** (2026-09-30, user feedback after the first in-game test). Options: (a) the hide key's
  `equip_hip` trigger alone, the hands swapping when it fires (instant, as the hide key does); (b) `equip_hip` when the
  swap starts, the hands swapping a set time later; (c) vanilla's equip animation (`equipping`, what a hotbar weapon
  change plays) for the equip time, then `equip_hip` fired by vanilla as the action's done trigger when the hands
  swap. Choice: (c). Reason: the put-away part is the game's own equip animation, the hands change after it (the
  request: "after the put-away"), and the draw is the hide key's own draw, fired exactly as the hide key fires it (the
  weapons already in the new hands); both come from vanilla code paths (`m_doneAnimation` is how the crossbow reload
  ends), and the trigger is an RPC, so every game plays it. Which clips the two animations are and how they blend is
  unverified (R25, `dual.equip` NOTE, T32). **Flag:** if the equip animation is too short to read at 0.2 s, (b) is the
  alternative (the draw trigger at the start, with the swap at the equip time).
- **D27 Sheathed pair placement** (2026-09-30, user feedback after the first in-game test; knives and mixed pairs
  changed the same day by the user's holster rule, at the end of this entry). Options: (a) a mirrored joint
  of the mod's own under the spine (the old Later L3), made from the back joints; (b) re-pose the vanilla back
  instances after vanilla builds them: the reference weapon keeps its vanilla pose, the other becomes its mirror image
  in the reference's plane, crossing at its centre; (c) fixed angles per joint. Choice: (b). Reason: a mirrored joint
  needs the body's mirror plane in the spine bone's frame, which depends on the pose at the moment it is measured (the
  torso twists when the player runs or reaches back), and two copies of the same weapon on mirrored joints would still
  not cross; (b) needs only the vanilla pose and the body's up on the rebuild frame (independent of the torso's twist
  and forward bend on that frame; a sideways lean of the joint is taken out every frame by the levelling, 2.7), works
  for any joint and any model, keeps one sheathed weapon exactly vanilla, and is undone by letting vanilla rebuild the
  instances. (First version, replaced by the holster rule below: two knives crossed at the right hip, tilted to 30°
  first because a knife hangs nearly vertical; a knife and a sword crossed on the sword's joint, the knife across the
  middle of the sword's blade.) Cosmetic and per game (nothing synced): games without the mod keep vanilla's overlap.
  **Levelling** (added 2026-09-30, after the in-world
  run of the crossing build found the knife X at the hip 7.1° lopsided two frames after it crossed). Options: (i) a
  vertical taken from the model's bind pose (fixed relative to the joint, but lopsided by whatever lean the idle pose
  has, unverified); (ii) crossing again once the stance change is over (a visible jump, and a stride at that moment
  bakes a new lean in); (iii) turning the pair about the X's normal every frame so its middle line stays on the body's
  up. Choice: (iii). Reason: it holds the X symmetric in every pose that has a vertical, at the cost of a list look-up
  per player and frame while a pair is crossed (two transform writes only when the X is a quarter degree or more off);
  the X's slight turn against a swaying back is not expected to show. **Holster placement by weapon kind** (user rule,
  2026-09-30: "When dual wielding, where the weapon is holstered depends on the weapon: swords, maces and axes go on
  the back, daggers and knives go on the side hip."). Three questions, each answered by the rule and vanilla:
  (1) *Back or hip, per weapon:* a skill list (Knives = hip) or vanilla's own choice (the joint `AttachBackItem` gives
  the item: attach override, else item type; knives carry override `Tool`, so `m_backTool`). Choice: vanilla's joint,
  read from the instance's parent after vanilla built it. Reason: it is where the game hangs each weapon alone, so a
  pair never puts a weapon somewhere vanilla would not, and a modded weapon follows its own attach override (a modded
  knife without the `Tool` override hangs on the back, as vanilla hangs it alone: vanilla-consistent, flagged).
  (2) *Two knives:* (a) both crossed at the right hip (the first version); (b) one per hip, the off-hand knife at the
  mirror image of vanilla's hip spot, its plane taken from (i) the rebuild frame's pose, (ii) the body mesh's bind
  pose, or (iii) the thigh bones' positions under the hips (needs their names); with or without levelling. Choice:
  (b) with (ii), (i) only when the body has no bind data for the hips, no levelling (2.7, "Knives on the hips"). Reason:
  "the side hip" for daggers means a knife at each side, as a belt carries them; the main-hand knife keeps the game's
  exact spot and pose; the bind pose gives the pelvis's centre plane independent of the pose (the objection to
  mirrored joints above was a plane measured in a moving pose; the bind pose removes it), with no bone names; both
  knives on the hips bone stay mirror images through every pose at no per-frame cost, while levelling would slide them
  along the belt. (3) *A knife with a back weapon:* (a) cross the knife onto the back weapon (the first version); (b)
  leave each at its vanilla spot. Choice: (b). Reason: the rule puts the knife on the hip and the sword on the back,
  which is exactly where vanilla hangs them; nothing is moved. `CrossSheathedPair` keeps its name (Tower Shield Wall's
  design cites it; 0.1.0 is unreleased) with its description reworded to "place a sheathed pair by weapon kind" (D28).
  **Knife pair: symmetric about the pelvis or about the body** (2026-09-30, after the in-world run of the holster
  build: the pelvis stood 31-43° from the body's centre plane in the unarmed idle, and the check "symmetric about the
  body's centre plane within 8 cm and 20°" failed at 28 and 37 cm, while the knives were exact mirror images across
  the pelvis's plane). Options: (a) keep them on the hips bone, mirror images about the pelvis's centre plane (as
  built); (b) mirror the off-hand knife across the player root's centre plane every frame (like the X's levelling).
  Choice: (a). Reason: a belt knife rides the pelvis, and so does the game's own single knife (`BackTool_attach` is a
  child of the hips); the screenshots show one knife at each hip, following the stance. With (b) the off-hand knife
  would keep the body's mirror spot while the left hip turns away from it: 28-37 cm off the hip in that idle, floating
  in front of or behind the thigh, and it would cost a transform write per frame. The self-test's root-plane symmetry
  check was the wrong check for a turned pelvis; it is now a NOTE (the pelvis angle and the distance), and the checks
  are what must hold in any pose: mirror images across the pelvis's plane (1 cm, 2°), each knife on its own side of
  the pelvis's plane and of the body's (3 cm), and outside its own thigh (its centre and lower end out from the line
  from the hip joint to the knee), in the idle pose and in each frame of 1.2 s of walking, jogging and sprinting in
  place (7.5).
  **Flag:** the look (angle, crossing point, the 2.5 cm lift, the levelled X turning a little against a swaying back;
  the mirrored knife on the left hip: clipping into the thigh's surface while walking or running (the test checks the
  knife stays outside the thigh bone's line, not the skin), the face it shows; the bind-pose plane itself) is
  unverified until the `dual.visuals` screenshots and NOTEs and T33 (R26).
- **D28 Sheathed pair setting.** Options: always on; a personal setting. Choice: `CrossSheathedPair` (Visuals,
  personal, default on), like `LeftHandTrails`; since the holster rule (D27) it covers the whole placement (the X on
  the back and the knives one per hip), under its first name. Reason: a modded weapon whose mesh is not measured well,
  a modded body, or a player who prefers vanilla's look, can turn it off without touching the others; it changes only
  that player's view.
- **D23 Lone off-hand weapon.** Options: move it to the main hand inside `UnequipItem` (with scopes around the vanilla
  callers that unequip right then left); settle it at the end of the frame and on the next attack. Choice: the second.
  Reason: safe for every caller, including other mods and future vanilla code, with no scope counter to keep balanced.
- **D24 Creature attack items.** Options: a player-item filter (a localized `$` name, an icon, a recipe); document.
  Choice: document (1.6, 2.1). Reason: they only come from the spawn command, pairing one breaks nothing, and a name
  filter would silently exclude modded weapons with plain names.
- **D25 Rules while a client waits for the server's.** Options: the client's own config (Forge Idol Upgrades' copy);
  vanilla, as the sibling Combat mods do (Weapon Moveset: moves off; Creature Morale: no creature afraid; Tower
  Shield Wall: no tower data); the built-in defaults. Choice: the built-in defaults (4.3). Reason: the shared rule of
  the Combat mods is that a client's own gameplay settings never apply on a server with the mod, so not the own config;
  and not vanilla, because `Player.Load` equips the saved pair when the player spawns and the answer is not guaranteed
  to be there by then: vanilla equip rules would collapse every saved pair at join. The defaults exclude nothing, so the
  pair loads, and the server's rules replace them the frame they arrive. **Flag:** differs from the three siblings that
  fall back to vanilla.

---

## 4. Multiplayer

### 4.1 Who runs what

| Piece | Runs on | Why it works for everyone |
|---|---|---|
| Equip rules, marker, restore, swap, main-hand key, eating, lone off-hand weapon | the player's own game (the owner of its player object) | The hands are the owner's state; the left item hash goes to the player ZDO (`LeftItem`, `LeftItemVariant`, `LeftItemQuality`) and every game draws it. |
| Stance | the owner (`SetupAnimationState` runs on the owner) | `statef` / `statei` animator values synced through the ZDO, like every vanilla stance. |
| Moves | the owner (`Attack.Start`) | Triggers are `ZSyncAnimation.SetTrigger` RPCs to everyone: every game, with or without the mod, plays the dual clips. |
| Hits | the owner (`Humanoid.OnAttackTrigger` is owner-only) | Each hit is a normal vanilla `HitData` of the striking weapon; the victim's owner (a creature's owner, the other player) applies it with vanilla rules, blocks included. |
| Swap animation | the owner (vanilla's equip queue) | The draw trigger `equip_hip` is an RPC to everyone; the `equipping` bool is in the player's `ZSyncAnimation` sync list (1.7, `tower.data` NOTE), so other games play the equip animation too, as for any vanilla equip. The new hands reach everyone through the ZDO like any equip. |
| Off-hand trails | every game with the mod | Local cosmetic from the synced left item hash. |
| Sheathed pair placement | every game with the mod, for every player it draws | Local cosmetic from the synced back item hashes (`LeftBackItem`, `RightBackItem`); games without the mod show vanilla's placement (a pair of one kind overlapping). |
| Join check, server settings | the server (dedicated or host) | 4.3, 4.4. |

Nothing depends on who owns a creature, so ownership changes need no handling. The player object is always owned by its
own game. Hooks on the victim's side see a normal `HitData` and never the weapon object (6.1).

### 4.2 Network

- RPCs (copy of MC Forge Idol Upgrades' `ServerRules`, the newest pattern):
  - client → server `MC.Combat.Weapons.DualWield.SettingsRequest` (`int` layout), after the handshake
    (`ZNet.RPC_PeerInfo` postfix) and when the client's copy turns on while connected;
  - server → client `MC.Combat.Weapons.DualWield.Settings` (`ZPackage`): the answer, and again to every ready player
    with the mod when the server's settings change or its copy turns on; both only to players whose hello carries the
    server's network version (another version cannot read the layout and its copy never runs there; review fix,
    section 10).
  - Payload layout 1: `int layout`, `int OffHandDamage`, `int SwingStamina`, `int SecondaryMoves`, `int HitPattern`,
    `int BothHandsDamage`, `string PairMoves`, `string KnifePairMoves`, `string ExcludedWeapons`. The client refuses an
    unknown layout or wrong field count (warning once; the last readable rules from this server stay, else the
    built-in defaults of 4.3), clamps numbers to the config ranges (warning), and caps each string at 1000 characters.
  - The framework's own RPCs (`src/Shared/Framework/NetworkGate.cs`, not this mod's code):
    - client → server `<GUID>.Hello` `"1|<network version>|<version>|<on|off>"`, right after connecting; the last field
      is the client's own ready state (feature turned on, no start error; with the hook of 7.6, also not blocked by
      another dual wield mod);
    - server → client `<GUID>.HelloAck` `"1|<on|off>|<network version>|<version>"`, the answer, sent again to every
      player when the server's copy turns on or off;
    - client → server `<GUID>.HelloState` `"on"` / `"off"`, when the client's copy turns on or off while connected.
      The server then raises `NetworkGate.PeerStateChanged`, which the join check listens to (4.4).
- **ZDO keys: none new.** Vanilla keys carry everything (left item, back items, stance, triggers). The swap and the
  sheathed pair placement add nothing to the wire: the rules payload and `ModNetworkVersion` stay as they are.
- **Item data:** `m_customData["MC.Combat.Weapons.DualWield.OffHand"] = "1"` on the off-hand weapon (saved in the
  player's inventory, a chest or a dropped item's ZDO like any custom data; vanilla keeps and ignores it).
- `ModNetworkVersion` = 1. Bump when the RPC names or the payload layout change.

### 4.3 Server settings

`DualRules` snapshot, read everywhere only through `ServerRules.Current` (cached object, one reference compare): the
server's values when this game is a client of a server with the mod, in the same `ZNet` session that delivered them;
**while such a client waits for them, the built-in defaults** (`DualRules.Defaults`, the Default column of section 5:
no excluded weapons, the Berserkir and Skoll and Hati moves, `OffHandDamage` 100, `SwingStamina` 100, `HitPattern`
Alternate, `BothHandsDamage` 50, `SecondaryMoves` PairMoves); otherwise the own config (single player, host, dedicated
server; a server never takes rules from a peer). So a client connected to a server never uses its own gameplay
settings. Contents: `OffHandDamage`, `SwingStamina`, `SecondaryMoves`, `HitPattern`, `BothHandsDamage`, `PairMoves`,
`KnifePairMoves`, `ExcludedWeapons`. Personal settings (not synced): `MainHandKey`, `SwapHandsKey`,
`LeftHandTrails`, `CrossSheathedPair`. Server only: `AllowPlayersWithoutMod`. An Info line on the client names the server's values once
per new set ("Using the server's dual wielding rules: ...; your own settings apply again in single player and when
you host."), only while the client's copy is active: a copy turned off while connected stores rules it receives
silently (the handlers stay registered), so its log never claims rules it does not use.

**Pending policy** (decision D25). The client asks right after the handshake (its `ZNet.RPC_PeerInfo` postfix) and
the server answers at once (the peer is ready by then), so the rules normally arrive while the world area loads,
before the player spawns. They are not guaranteed to, though, and `Player.Load` equips the saved pair at spawn. While
waiting, the client therefore uses the built-in defaults above, and not vanilla like Weapon Moveset (moves off),
Creature Morale (no creature afraid) and Tower Shield Wall (no tower data): vanilla equip rules at that moment
would collapse every saved pair at join, and the defaults exclude nothing, so the pair loads. The shared rule of the
Combat mods holds: a client's own gameplay settings never apply on a server with the mod. When the server's rules
arrive (a new rules object), the revalidation of 2.4 (mechanism 5) runs on the next frame, exactly as for a live rule
change: it puts away an off-hand weapon the server excludes and updates the stance to the server's moves.
`ServerRules.Pick(clientOfServer, serverRules, own)` is the pure choice behind `Current` (self-tested, 7.5).

A server that lists a `PairMoves` item the client does not have (a modded item missing on the client): the client falls
back to the default moves with a warning (2.5); the stance and moves other games see are whatever that client plays.

### 4.4 Join check (`PlayerCheck`)

Copy of MC Forge Idol Upgrades' `PlayerCheck` (itself from Breeding), with the verdict taken from the framework
(`src/Shared/Framework/NetworkGate.cs`, already changed in this run, 7.6). On the server (dedicated or host; the
host's own player is not a peer, single player has none):

- **When.** A `ZNet.RPC_PeerInfo` postfix schedules each ready peer; a `ZNet.Update` postfix checks it after a 1 s
  grace (the framework's hello always arrives before the peer's `PeerInfo` on the same connection, so the grace is only
  a safety margin). `PlayerCheck.Start` (from `OnActivated`) subscribes to `NetworkGate.PeerStateChanged` (removing
  the handler first, so it is never added twice) and schedules every connected peer; `PlayerCheck.Stop` (from
  `OnDeactivated`) unsubscribes and cancels pending checks. The handler catches its own exceptions and calls
  `Schedule(peer)` with the same 1 s grace, so a player who turns the mod off while connected is refused about a
  second later, and one who turns it back on within that second stays (the check reads the state at the deadline; a
  peer already queued keeps its first deadline). Switching `AllowPlayersWithoutMod` back to off schedules every
  connected peer.
- **Verdict.** `NetworkGate.PeerCompatible(peer)`: the player has the mod, the same `ModNetworkVersion`, and has not
  turned it off on their game (the hello carries the client's own ready state and `HelloState` updates it while
  connected; a copy that failed to start counts as off, and so does a copy blocked by another dual wield mod through the
  hook of 7.6). `PlayerCheck.Decide(isServer, connected, ready, beingKicked, compatible, allowWithoutMod)` stays pure
  (Skip / Compatible / Allowed / Refuse; self-tested).
- **Refusal** (not compatible, `AllowPlayersWithoutMod` off): vanilla `Error` RPC with `ErrorVersion` (their game shows
  "Incompatible version" and goes back to the main menu), peer added to `ZNet.PeersToDisconnectAfterKick` with a 4 s
  delay (so a slow-loading game reads the error first). Warning in the server log naming the player and
  `NetworkGate.PeerProblem(peer)`: "Refused <name>: their game <does not have the mod | has the mod turned off | has
  another version of the mod (network version N, the server has M)>. This server requires Dual Wielding on every player
  (everyone fights with the same rules). To let such players in, set AllowPlayersWithoutMod = true."
- **Allowed** (`AllowPlayersWithoutMod` on): let in, with a Warning naming the player and the same reason. Their own
  copy is inactive (missing, off, or `ServerMismatch`), so they play without this mod's rules (hand-offs in 4.5). A
  player whose copy is off because of another dual wield mod (6.3) reports "turned off" too and may still dual wield
  with that mod, by its own rules; the warning, the setting's description and the README say so ("they may play,
  without this mod's rules (with another dual wield mod installed they may still dual wield, by that mod's rules)";
  review fix, section 10).

A player whose game did not load the mod at all (`BepInIncompatibility` next to Smoothbrain DualWield, 6.3) sends no
hello and is refused like a player without it. Every copy of Dual Wielding is built with this framework, so its ready
state is always known (an older framework copy that sends no state would count as compatible). The mod has no
mechanism of its own to detect a turned-off copy or another version: the framework's state is the only source. The
rules push of 4.2 still goes to every ready player whose game answered the handshake with the same network version
(`NetworkGate.PeerHasMod` and `PeerNetworkVersion`): that is delivery, not the verdict, and a copy that is turned off
keeps the server's rules for the moment it turns back on.

### 4.5 Situations and hand-offs

| Situation | Result |
|---|---|
| Single player | Own settings; everything local. |
| Player with the mod on a server with the mod | Server's settings; pairs work; other players see them. |
| Player joins without the mod, with it turned off, or with another network version (setting off) | Refused about 1 s after joining ("Incompatible version" on their screen); the server log names the reason (`PeerProblem`). |
| Player turns the mod off while connected (setting off) | Their copy puts the off-hand weapon away (7.4) and tells the server (`HelloState`); refused about 1 s later. Turned back on within that second: stays. |
| Player without the mod, with it turned off, or with another network version, `AllowPlayersWithoutMod` on (**hand-off**) | Let in, with a server log warning naming the reason. Their copy (if any) is inactive, so they can never make a pair with this mod (the equip rule is local); a player whose copy is off because of another dual wield mod may still pair with that mod, by its own rules. They see a dual wielder's two weapons, dual stance, dual moves and the swap's draw animation (all vanilla data) and take their hits normally; they see no off-hand trail and the two sheathed weapons overlapping on one back joint (vanilla's placement). |
| Two players with the mod | Each places the other's sheathed pair on its own screen (from the synced back items: an X on the back, knives one per hip), per its own `CrossSheathedPair`, and sees the other's swap animation. |
| Weapon carrying the off-hand marker reaches a player without the mod (trade, chest, tombstone) (**hand-off**) | A normal weapon for them; the marker is kept in its data and ignored. Back with a modded player it only matters when it is re-equipped in the same frame as its partner; a stale marker on a main-hand weapon is cleared on activation, on login and when a pair forms. |
| A saved pair loads where the mod is off (vanilla server, mod disabled, uninstalled) (**hand-off**) | Vanilla `EquipInventoryItems`: the later of the two in inventory order ends in the right hand, the other is unequipped (`m_equipped` cleared). No error. The pair has to be made again. |
| Player with the mod on a server without it | The framework turns the feature off for the session before the player spawns: vanilla equip and moves. |
| Player with Smoothbrain DualWield next to this mod | This mod does not load (6.3); on a server with the mod the player is refused like a player without it. |
| Player with another dual wield mod found by name (DualWielder, DualWieldCore, DualMastery) | Their copy is inactive with a status naming that mod (6.3) and reports "off" to the server: refused like a player who turned the mod off (through the framework hook of 7.6, in place). |
| The server turns the mod off (or back on) live | Every client's copy follows (framework): off puts each player's off-hand weapon away; on allows pairs again and resends the settings. |
| The server changes a rule live | Every client revalidates on the next frame: an excluded off-hand weapon is put away, the stance follows the moves settings. |
| PvP | Off-hand hits are normal hits of that weapon; the other player blocks or parries them with their own blocker; both games use the server's settings. A both-hands event is two blocked hits: the per-block rewards count twice (2.6). |
| Dedicated server | Loads the mod (`Both` has no `BepInProcess`): join check and settings only. It has no local player, so every gameplay patch exits at its first check; the trail and back-item postfixes exit on the no-graphics check. |
| Two players with different own settings | Both use the server's rules; own files untouched. |

`ModMultiplayerNotes` (player-facing, csproj): "Install it on the server (or the host) and on every player's game. It
changes combat, so the server refuses players whose game does not run it (not installed, turned off, or a version that
cannot talk to the server's; their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on,
and the server's combat settings (off-hand damage, swing stamina, hit pattern, moves, excluded weapons) apply to
everyone. Turning it off while you play on such a server disconnects you about a second later. Next to another dual
wield mod it does not run, so such a server refuses you too. On a server without the mod it turns itself off, and a
saved pair then loads as one weapon. Players without the mod still see your two weapons and your dual moves."
(The framework hook of 7.6 is in place, so this text ships as is; the fallback wording of 6.3 is not needed.)

---

## 5. Config

`BepInEx/config/MC.Combat.Weapons.DualWield.cfg`. "Server" = a server rule: in multiplayer the server's (or host's)
value is used for everyone. Every setting applies live.

| Section | Key | Default | Range | Server | Description (user-facing) |
|---|---|---|---|---|---|
| General | Enabled | `true` | | framework | Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | | read-only | | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | | server only | Used only by the server (or the host). Off (default): a player whose game does not run this mod (not installed, turned off, or a version that cannot talk to this one) is refused about a second after joining, or after turning it off while connected, and their game shows "Incompatible version", so everyone fights with the same rules. The server log says why. On: such players may play, with a warning in the server log; they play without this mod's rules (a player running another dual wield mod may still dual wield with that mod), and they see other players' pairs normally. |
| Controls | MainHandKey | `LeftAlt` | a key, or None | personal | Hold this key while you equip a one-handed weapon (hotbar, inventory or radial menu) to put it in your main hand the normal game way instead of pairing it with the weapon you hold. The weapon in your off hand is put away. None = off. Keyboard only. |
| Controls | SwapHandsKey | `H` | a key, or None | personal | Press this key while dual wielding to swap the weapons between your hands. The swap is an equip like any other: it takes as long as equipping a weapon, plays the game's equip animation and then the draw animation of the hide key. You cannot attack meanwhile (hold the attack button to swing as soon as the swap and its draw animation allow). It does not start while you sprint, and a dodge, a jump or a sprint cancels it. Your next attack starts the combo from its first swing. The off-hand weapon is the one that blocks and parries, and the main-hand weapon strikes first. None = off. Keyboard or mouse only (no gamepad button yet). |
| Combat | OffHandDamage | `100` | 10-200 | yes | Damage of the hits struck by your off-hand weapon, in percent of that weapon's normal damage. 100 = full damage, like the game's own dual weapons, which deal one weapon's damage on every hit. |
| Combat | HitPattern | `Alternate` | Alternate, BothHands | yes | Which weapon strikes each hit of a dual attack. Alternate: the hits of the combo alternate between your weapons like the game's own dual axes (one axe, the other, then one hit from each), and the special attack and the last knife stab strike with both at once. BothHands: both weapons strike on every hit, each dealing BothHandsDamage percent (the feel of other dual wield mods). |
| Combat | BothHandsDamage | `50` | 10-100 | yes | When both weapons strike the same hit, the damage of each, in percent of its normal damage. 50 = together they deal about one weapon's hit, like the game's own dual weapons. Higher values make pairs much stronger. The off-hand weapon's share is also scaled by OffHandDamage. |
| Combat | SwingStamina | `100` | 25-300 | yes | Stamina of a dual wield attack, in percent of the higher normal attack cost of your two weapons. The special attack costs as many times more as the game's dual weapon it copies (2 times for the axe moves, 3 times for the knife moves). Your skill and equipment still reduce it as usual. |
| Combat | SecondaryMoves | `PairMoves` | PairMoves, MainWeapon | yes | Special attack of a pair. PairMoves: the special attack of the dual weapon whose moves the pair uses (the Berserkir axes' cleave, or Skoll and Hati's leap), struck by both weapons. MainWeapon: the normal special attack of your main-hand weapon, struck by that weapon only. A pair has no special attack when its main-hand weapon has none (the wooden club). |
| Combat | ExcludedWeapons | `` (empty) | prefab names, comma-separated | yes | One-handed weapons that can never be dual wielded, as prefab names (as used by the spawn command), for example modded weapons that look wrong in the dual moves. Spears, the butcher knife, tankards and bombs are never paired anyway. A weapon you hold in your off hand when it becomes excluded is put away. |
| Moves | PairMoves | `AxeBerzerkr` | an item prefab name | yes | The game item whose moves (stance, attack combo, special attack, reach and damage multipliers) a pair uses, unless both weapons are knives. Default: the Berserkir axes. If the item does not exist or its animations are missing, the default is used and the log says why. With an item that is not a dual weapon (a sword, an axe), the swings of its combo alternate between your weapons (main hand first) and its special attack strikes with both. |
| Moves | KnifePairMoves | `KnifeSkollAndHati` | an item prefab name | yes | The game item whose moves a pair of two knives uses. Default: Skoll and Hati. Same checks as PairMoves. |
| Visuals | LeftHandTrails | `true` | | personal | Show the swing trail on off-hand weapons too, for every player you see dual wielding. |
| Visuals | CrossSheathedPair | `true` | | personal | Place a sheathed pair (weapons put away with the hide key, at a crafting station, in water...) by weapon kind, for every player you see with one: two swords, axes or maces crossed in an X on the back, two knives one on each hip. A knife with a sword, axe or mace hangs where the game puts each (the knife at the hip, the other weapon on the back) either way. Off: two weapons of the same kind sit on the same spot and overlap, as the game places them. Only what you see changes. |

Values that need in-game tuning (`OffHandDamage`, `BothHandsDamage`, `HitPattern`, `SwingStamina`, the moves) are server
settings. `MainHandKey` and `SwapHandsKey` are validated once when set (a key the game cannot read turns that key off
with a warning).

---

## 6. Compatibility

### 6.1 Sibling mods built in this run

- **Weapon Moveset** (`MC.Combat.Weapons.Moveset`) edits the same per-swing clone in an `Attack.Start` prefix (jump and
  roll attacks). Contract:
  - Dual Wielding's `Attack.Start` prefix runs at `Priority.High`; Moveset's runs after it (`[HarmonyAfter]` +
    `Priority.Low`), so it sees the clone already converted (template animation, chain levels, shape, stamina).
  - Moveset recognises a pair's swing **only from the clone's current `m_attackAnimation`** after our prefix (the
    `dualaxes` / `dual_knives` base names and their specials, which its `DualAxes` / `DualKnives` families cover). It
    never looks at the left item: a lone one-handed weapon sits there while eating, and a pair's clone is not converted
    with `SecondaryMoves = MainWeapon` or an invalid template. Moveset picks its move from that family (for example
    `dualaxes3` or `dualaxes1` as a one-off; its move clones always have chain levels 0).
  - What Moveset reads from the clone: our prefix has already replaced the template-owned fields (2.5), so the base
    name, the chain level count (the template's: 4 for `dualaxes`, 3 for `dual_knives`) and the `m_maxYAngle` to
    restore after an aimed jump attack must come **from the clone as it arrives in Moveset's prefix**, never from
    `weapon.m_shared.m_attack` (the main weapon's own `swing_longsword` or `swing_axe` has 3 levels). Otherwise a
    `DualAxes` move set to `dualaxes2` would count as the last step and the next swing would restart at `dualaxes0`
    instead of the `dualaxes3` finisher. (`m_maxYAngle` is 0 on every vanilla player melee attack in the 1.0.16 dump,
    templates included, so the aim reset is harmless today either way.) This is our side of the contract; Moveset's
    2.5 and 7.1 implement it.
  - Hands: our `Attack.Start` postfix reads the trigger actually fired, after Moveset's prefix and vanilla (2.6), and
    maps each hit event by the fixed table, with the event index reset for every swing. So Moveset's jump attack
    `dualaxes3` strikes main then off, its roll attacks `dualaxes1` and `dual_knives1` strike with the off hand,
    `dualaxes_secondary` with both; Moveset's entry-time rename of the clone back to the base name does not change the
    recorded pattern; the next swing continuing the chain maps by its own step (after a roll attack, `dualaxes2` =
    main, then off). Nothing carries over between swings: there is no running hit counter (the rejected option e of
    D7), and a move does not necessarily start with the main hand.
  - Multipliers compose: Dual Wielding scales `m_damageMultiplier` by `OffHandDamage` (and by `BothHandsDamage`,
    with `m_forceMultiplier`, in a both-hands event) only during a hit and restores it; Moveset may scale
    `m_attackStamina`, `m_damageMultiplier` and others after Dual Wielding set them; the per-hit scaling multiplies
    whatever the clone holds.
  - Our `Humanoid.StartAttack` prefix (`Priority.First`) can refuse an attack (skips the original) during a pair's eat
    window (2.4) and while a swap is queued (2.3; Moveset's jump and roll attacks wait for the swap too); Moveset
    already tolerates a skipped original. The swap key passes `previousAttack = null` in our
    `Attack.Start` prefix; Moveset's prefix then sees the combo restarting.
  - Moveset ignores attacks with `m_skillType` Blocking (the tower shield bash); never a pair.
  - Moveset's roll attack flows straight out of the late roll: from its `FlowStart` on (never before the roll's
    invulnerability is over) a pressed, remembered or held attack cuts the roll short and the attack state is
    cross-faded in over `FlowBlend`. The swing is still the one-off `dualaxes1` / `dual_knives1` clone that our
    `Attack.Start` postfix records, and its hit events come from that clip (the `moveset.roll` NOTE of the first
    in-world run: a cross-fade into `DualAxes Attack 2 2` and `Knife Attack Combo (2)`), so the hand table applies
    unchanged: off hand. Moveset opens its roll gate (`m_inDodge` false for one `StartAttack` call) in its own prefix;
    ours, at `Priority.First`, reads no dodge state for attacks.
- **Tower Shield Wall** (`MC.Combat.Shields.TowerWall`): tower shields become two-handed (`TwoHandedWeaponLeft` in its
  design): not eligible, so equipping one takes vanilla's branch, which puts both weapons of a pair away; equipping a
  weapon afterwards puts the tower shield away (vanilla one-handed rule: a left item that is not a `Shield` or `Torch`
  is unequipped). The lone off-hand and revalidation rules act only on a left `OneHandedWeapon`, never on a tower. If
  tower shields stay `Shield` type, G5 applies (the tower shield replaces the off-hand weapon). `ItemKinds` now keeps a
  tower shield in the Shield kind after it becomes `TwoHandedWeaponLeft` (shield pose + Blocking skill; already in
  `src/Shared/ItemKinds.cs`, from Tower Shield Wall's run); this mod does not use `ItemKinds` (eligibility has its own
  rules, 2.1), so that change does not affect it.
- **Sneak Ambush** (`MC.Combat.Sneak.Ambush`):
  - Where a hook runs matters. On the attacker's game, code may read `Attack.m_weapon` during `DoMeleeAttack` (the
    striking weapon; `GetCurrentWeapon()` returns the main weapon). On the victim's side (the creature's owner, often
    another game) the hit arrives as a `HitData` with no `Attack` object: hooks there must rely on `m_skill`,
    `m_backstabBonus`, `m_toolTier`, `m_statusEffectHash` and the attacker, and never expect to know which weapon struck.
  - Backstab: vanilla applies it only to the first hit on an unalerted target (1.4 step 7). An ambush opens with step 0
    of the combo, struck by the main hand (with `HitPattern = BothHands`, the main hand's hit comes first), so **the
    backstab is the main-hand weapon's**: put the knife in the main hand (swap key) for a ×6 knife backstab next to a
    sword. A Weapon Moveset roll attack (`dualaxes1`) would open with the off hand.
  - Sneak's Smoke Screen (`MC_SmokeScreen`, a `BombSmoke` clone: skill None, projectile) is not eligible: equipping it
    while paired puts both weapons away (vanilla one-handed rule; Later L10, listed at the pause because the two mods
    ship together).
  - Sneak's throw guard is an `[AlwaysOnPatch]` prefix on `Humanoid.StartAttack` (it stays applied while its feature is
    off, framework change done in this run) that can skip the original for a Smoke Screen throw. Our `StartAttack`
    prefix and finalizer tolerate a skipped original (the finalizer clears "secondary requested"), and a Smoke Screen
    is never part of a pair, so the two never act on the same attack.
  - A knife pair uses Skoll and Hati's quiet moves (hit noise 5); a mixed pair uses the Berserkir's (40).
- **Creature Morale** (`MC.Combat.Creatures.Morale`): no shared method. An outclassed creature is afraid of a player
  (it runs from them within its `FearRange` when it senses them) and fights back when that player hits it, lands a
  projectile close to it or corners it. Its hit check runs on the creature's owner, on the `HitData` and the attacker:
  hits from either hand are normal hits from the player, so an off-hand hit makes an afraid creature fight back like a
  main-hand hit (a pair throws nothing, so the near-miss rule never involves it). Its sneak rule (an afraid creature
  that sees the player gives no sneak-attack bonus; one that cannot see them still does) applies to a pair's opening
  hit like to any hit; the backstab itself stays vanilla's (Sneak Ambush above).

### 6.2 Other MC mods touching the same methods or items

- **Crossbow Stays Loaded**: patches `Humanoid.UnequipItem` (prefix), `Humanoid.BlockAttack` (postfix),
  `Attack.OnAttackTrigger` (postfix, reads `m_weapon`, acts only for reload weapons). Crossbows are `Bow` (two-handed),
  never paired. Our `m_weapon` swap and the both-hands second call happen inside `DoMeleeAttack`, so its
  `OnAttackTrigger` postfix always sees the main weapon, once per event. We no longer patch `UnequipItem`. Cross test
  X01.
- **Forge Idol Upgrades**: `ForgeRefine` calls `UnequipItem` before remaking an item: refining the off-hand weapon puts
  it away; refining the main weapon leaves a lone off-hand weapon, which moves to the main hand at the end of the frame
  (2.4). Its `ItemData.GetTooltip` postfix is unaffected (this mod adds no tooltip). Cross test X05.
- **Harpoon Hooks Tames**: `Character.Damage` prefix (`Priority.Last`, after EpicLoot) and finalizer with a nested-hit
  scope, and `Character.RPC_Damage` prefix and postfix; all keyed on the harpoon status-effect hash. The harpoon
  (`SpearChitin`) is a spear, never paired, so our hits never carry that hash; the both-hands second `DoMeleeAttack`
  runs after the first call's `Character.Damage` returned, never inside it, so its nested-hit scope is never open for
  our hits. No interaction.
- **Stats Per Creature**, **Compendium Encyclopedia**: no shared method; kills by off-hand hits count as the player's.
- UX mods (Sort Chest, Crafting Search and Sort, Loot Pickup Filter, Repair All): no hand logic. A drag inside the
  inventory goes through vanilla `InventoryGui.OnSelectedItem`, handled by 2.4.
- `src/Shared`: `ItemKinds` now keeps tower shields in the Shield kind (Tower Shield Wall's run); Dual Wielding does
  not use `ItemKinds`. The join check uses the framework's `NetworkGate.PeerCompatible`, `PeerProblem` and
  `PeerStateChanged` (4.4, 7.6). Forge Idol Upgrades, Breeding and Lights, whose `PlayerCheck` this mod copies, still
  decide with `PeerHasMod`; bringing them to the same check is their own change, not part of this mod.

### 6.3 Popular external mods

- **Smoothbrain DualWield** (`org.bepinex.plugins.dualwield`, from its source): the Plugin class carries
  `[BepInIncompatibility("org.bepinex.plugins.dualwield")]` plus a soft `[BepInDependency]` on the same GUID (so that
  plugin is always processed first, whichever list BepInEx checks, R22). BepInEx then does not load Dual Wielding and
  logs a warning; the MC Mods panel does not list it; a server with the mod refuses that player like one without it.
  GCO's dual wielding needs Smoothbrain DualWield, so it is covered too.
- **Other dual wield mods: the feature goes inactive** (decision D17). RustyMods DualWielder, Ketanol DualWieldCore and
  balrond DualMastery (GUIDs unverified, R17) are found by plugin name containing "DualWielder", "DualWieldCore" or
  "DualMastery" in `BepInEx.Bootstrap.Chainloader.PluginInfos` (excluding our own GUID; only plugins with a live
  instance). `Plugin` overrides the framework hook `LocalBlocker()` (7.6), which returns "Inactive: <mod> also handles
  dual wielding. Remove one of them." when one is present, else null. The framework then removes every patch of this
  mod, shows that status in the MC Mods panel and in its spawn notice (inactive for a reason the player did not
  choose), and reports the copy as off to the server (the hello's last field, `HelloState` if it changes while
  connected), so a server with the mod refuses that player like one who turned it off (4.4). BepInEx adds plugins to
  `PluginInfos` as it loads them, but the framework refreshes again in `Start`, once every plugin has loaded, and on
  every world change, so load order does not matter (the feature may be active between this plugin's `Awake` and
  `Start`, at game start, before any world exists, where nothing can pair). The scan runs only when the framework
  refreshes (config, world and peer events), never per frame. When R17 finds their GUIDs, they move to
  `BepInIncompatibility` (the mod then does not load at all, as next to Smoothbrain DualWield). Valheim Ascended
  (kpttr) has its own off-hand slot (GUID unverified): README warning only.
- **Fallback if the hook is not accepted** (not used: the hook exists, 7.6; kept for the record of D17): a stand-down
  inside the mod. The same
  name check runs lazily, the first time a patch needs it in a world, and `OnActivated` resets it; if a mod is found, a
  Warning in the log, a center message the first time the player equips something that would pair ("Dual Wielding is
  off: <mod> also handles dual wielding. Remove one of them."), and every gameplay patch exits at its first check
  (`ForeignMods.StandDown`). The framework cannot see this, so that player passes the join check and fights with the
  other mod's rules: the only documented gap in G9 (README, `ModMultiplayerNotes`), closed mod by mod as R17 finds their
  GUIDs.
- **Goo's Combat Overhaul** (gnls; `goo.valheim.gooscombatoverhaul` from its config file name, per the Weapon Moveset
  design, unverified): without Smoothbrain DualWield it does not dual wield; its per-family tuning (lunge, swing speed,
  its own jump and running attacks) may apply to our pair swings if it recognises the `dualaxes` / `dual_knives`
  families by name (unverified). Asked at the pause whether the user plays with it; if yes, cross test X07.
- **EpicLoot** (`randyknapp.mods.epicloot`, unverified here): its per-weapon effects would follow `Attack.m_weapon` per
  hit, or apply every equipped item's effects to every hit (Smoothbrain issue #13). README note.
- **Animation replacers** (KG AnimationReplaceManager users such as Therzie's Warfare, Weapons Attack Animation Manager):
  may replace the dual clips or their triggers, or the equip and draw clips a swap plays (unverified). The Info line
  names the triggers in use.
- **Equip speed and quick-swap mods** (mods that shorten `m_equipDuration`, skip or reorder vanilla's action queue):
  the swap goes through that queue (2.3), so it follows them; a mod that equips without the queue does not delay the
  swap either way (unverified per mod).
- **Mods that move sheathed weapons** (back-slot or holster placement mods patching `VisEquipment.AttachBackItem` or
  the back joints; unverified): our `SetBackEquipped` postfix places a sheathed pair after vanilla (and after such a
  mod's `AttachBackItem` changes) only when both instances sit on `m_backMelee` or both on `m_backTool`; a pair such a
  mod parented to joints of its own is left alone. A mod that moves the vanilla joints themselves is followed (the X
  and the mirrored hip spot are built from them); if the result looks wrong, `CrossSheathedPair = false` leaves their
  placement alone.
- **Inventory and equipment-slot mods** (AzuExtendedPlayerInventory, quick slots, loadouts): equips through `EquipItem`
  follow our rules; unequips in any order are safe (2.4, mechanism 3); mods that write `m_rightItem` / `m_leftItem`
  directly bypass the rules, and a lone left weapon they leave is still moved to the main hand (unverified per mod).
- **Keys:** mods binding Left Alt or H (unverified): both keys are settings.

---

## 7. Implementation plan

### 7.1 Files (`src/Combat/Weapons.DualWield/`)

| File | Content |
|---|---|
| `Plugin.cs` | Config binding, `OnActivated` / `OnDeactivated`, and the `LocalBlocker` override (6.3; the only override beyond `BindConfig`, `OnActivated`, `OnDeactivated`; the framework hook of 7.6 exists); the `BepInIncompatibility` and soft `BepInDependency` attributes for Smoothbrain DualWield on the partial class. |
| `DualRules.cs` | The server-rules snapshot (`Own()`, `Defaults` for the pending state, `Write`, `TryRead` with clamping, `Describe`), layout 1. |
| `ServerRules.cs` | Copy of Forge Idol Upgrades' `ServerRules` (RPC names from `ModInfo.Guid`, `Current` accessor, Debug `TestRules`), plus the pending state: `Current` goes through the pure `Pick(clientOfServer, serverRules, own)`, which gives `DualRules.Defaults` to a client that has no server rules yet (4.3); the "cannot read" warning names the defaults instead of the own settings. |
| `PlayerCheck.cs` | Copy of Forge Idol Upgrades' `PlayerCheck`, with the verdict from `NetworkGate.PeerCompatible`, `NetworkGate.PeerProblem` in the log lines, and the `NetworkGate.PeerStateChanged` subscription in `Start` / `Stop` (4.4). |
| `Eligibility.cs` | `IsEligible(ItemData)`, the exclusion set of prefabs (each with its own `SharedData`; lazy, keyed to the rules object and the `ObjectDB` instance). |
| `Hands.cs` | Pair state: `IsPaired`, the route decision (`Route`, a pure function the self-tests hammer), `Apply` with its re-sync `finally`, `SettleLoneOffHand`, `Revalidate`, `ClearStaleMarkers`, the swap (`TrySwap` queues it, `CompleteSwap` ends it, `CancelSwap`, the pending swap and the combo restart flag), `ReleaseOffHand`, restore candidate, main-hand intents and windows, eat-return state, the first-pairing message, the marker key. |
| `BackCross.cs` | The sheathed pair placement (2.7): the pair-weapon check per back hash, `Measure` (shape of a back instance from its biggest mesh), `Apply` (picks the layout from the joints vanilla used: `Cross` for two back weapons, with reference, plane, angle, mirror, lift, registering the pair for levelling; `SplitHips` for two knives; nothing for a knife with a back weapon), `HipsPlane` (the body's centre plane in the hips bone's frame, from the bind pose), `Level` (keeps a crossed pair's middle line on the body's up every frame), `Retry`, `RebuildAll` (marks every player's back visuals as changed); Debug-only record of the last pair's layout, crossing and levelling, and the in-memory setting override. |
| `MoveTemplates.cs` | Reads and validates `PairMoves` / `KnifePairMoves` (triggers checked at rebuild), caches the templates and stance, the template-owned field copy (`Shape`: locals first, then one assignment block), the stamina formula. |
| `DualSwing.cs` | The fixed hand table (static patterns keyed by `dualaxes` / `dual_knives` and their one-off names; any other trigger takes the fallback rule of 2.6), the recorded swing (clone, main, off, pattern, chain step, event index), the weapon-owned field save/swap/restore in a static struct guarded by the re-entry flag, the both-hands second call; Debug-only record of each hit (hand, trigger, event, clone field values; for each damage also vanilla's random skill factor and the objects the sweep touched) for the self-tests. |
| `Controls.cs` | Key parsing and validation (copy of MC Loot Pickup Filter's `IsUsableKey`), `MainHandHeld()` with a Debug override, swap polling. |
| `ForeignMods.cs` | Name-based detection of the other dual wield mods (`Find()` over `Chainloader.PluginInfos`, a pure name match behind it for the self-test, the blocker text read by `LocalBlocker`). With the fallback of 6.3 instead: the lazy check, the `StandDown` flag and the one-time message. |
| `Patches/HumanoidPatches.cs` | `EquipItem`, `HideHandItems`, `ShowHandItems`, `SetupAnimationState`, `StartAttack`. |
| `Patches/PlayerPatches.cs` | `ToggleEquipped`, `UpdateActionQueue`, `Update`; Debug-only `GetRandomSkillFactor` (self-test record). |
| `Patches/AttackPatches.cs` | `Start`, `DoMeleeAttack`; Debug-only `AddHitPoint` (self-test record). |
| `Patches/VisEquipmentPatches.cs` | `SetWeaponTrails`, `SetBackEquipped`. |
| `Patches/ZNetPatches.cs` | Copy of Forge Idol Upgrades' (`OnNewConnection`, `RPC_PeerInfo`, `Update`). |
| `SelfTests.cs` | Debug-only in-world self-tests (7.5), including a Debug-only `Character.Damage` prefix that records the attacker-side damage of the local player's hits. |
| `README.md`, `CHANGELOG.md`, `TESTING.md`, `icon.png` | Per house rules. |

### 7.2 Harmony patches

Every patch body catches its exceptions and calls `PatchGuard.Report(site, e)`; gameplay patches first check
`ReferenceEquals(__instance, Player.m_localPlayer)` (no `?.` / `??` on Unity objects). No stand-down check: next to
another dual wield mod the framework removes the patches (6.3; with the fallback, every gameplay patch checks
`ForeignMods.StandDown` first). No `[AlwaysOnPatch]` class: nothing of this mod must outlive the toggle (no items or
prefabs; the settings RPCs are registered per connection, and vanilla keeps the off-hand marker in custom data).

| Target | Kind, priority | Purpose | Cost |
|---|---|---|---|
| `Humanoid.EquipItem(ItemDrop.ItemData, bool)` | Prefix (Priority.Low; does nothing when another prefix already skipped the original), Postfix | Prefix: the end of a queued swap (the pending off-hand weapon inside the queue window: swap the hands, never vanilla, 2.3); rows K, 1, 2, 3, T of 2.2 in two steps (pure route, then apply with a re-sync `finally`); return false with `__result = true` for 1-3. Postfix: a marked item landed in the right hand through vanilla → clear the marker, restore candidate. | Per equip. |
| `Humanoid.HideHandItems(bool, bool)` | Prefix + Postfix | Keep the eat-hidden main weapon during a full hide or the right-hand hide of a second eat (2.4, mechanism 4). | Per hide; every frame while swimming off the ground (`UpdateEquipment` calls it; it returns early once both hands are empty): one reference compare. |
| `Humanoid.ShowHandItems(bool, bool)` (protected) | Prefix | Eat return: wait while attacking, dodging or swimming; draw both when the off-hand weapon was hidden too. | Per show. |
| `Humanoid.SetupAnimationState()` (private) | Postfix | Dual stance while paired (2.5). | Per equip change. |
| `Humanoid.StartAttack(Character, bool)` | Prefix (First) + Finalizer | Record "secondary requested"; settle a lone off-hand weapon; refuse during a pair's eat window and while a swap is queued. | Per attack. |
| `Attack.Start(Humanoid, Rigidbody, ZSyncAnimation, CharacterAnimEvent, VisEquipment, ItemDrop.ItemData, Attack, float, float)` | Prefix (High), Postfix | Prefix: copy the template move and the stamina into the clone (locals, then one block; no allocation); `previousAttack = null` after a swap. Postfix (on success): read the fired trigger, record the swing and its hand pattern. | Per swing. |
| `Attack.DoMeleeAttack()` (private) | Prefix (First), Postfix, Finalizer | Pick the hand; for an off-hand hit swap the weapon-owned fields (static saved struct); for a both-hands event scale the main hand, then in the postfix swap to the off hand and call `DoMeleeAttack` once more (re-entry flag); the finalizer restores everything. | Per hit event of any melee attacker on this game: one reference compare when it is not our swing. |
| `Player.ToggleEquipped(ItemDrop.ItemData)` | Prefix + Postfix | Main-hand intent per item, toggle window. | Per equip request. |
| `Player.UpdateActionQueue(float)` (private) | Prefix + Finalizer | Queue window for the intent and for the end of a queued swap; a queued swap whose pair is gone leaves the queue before vanilla can end it (2.3). | 50 Hz on the local player: two bool writes and one null check. |
| `Player.Update()` (private) | Postfix | Rules / `ObjectDB` / player change check and revalidation; lone off-hand weapon; pending eat return; queued swap upkeep; swap key. | Every frame per Player object: one reference compare, then for the local player three reference compares, three slot checks, one null check and one key check; no allocation. |
| `VisEquipment.SetWeaponTrails(bool)` | Postfix | Off-hand trails (E1), players only, not without graphics. | Twice per swing per player; cached type check and trail array. |
| `VisEquipment.SetBackEquipped(int, int, int, int, int)` (private) | Postfix | Sheathed pair placement (G11, 2.7) when it returned true; otherwise, while a back pair left flat waits, its retry, and while some player has a crossed pair, the levelling of that player's X. | Every frame per player: three bool reads; the placement only when vanilla rebuilt the back instances (a hide or draw), or every 15 frames while a retry waits, with a small list reused for the mesh lookup (a knife pair also copies the body's `bones` and `bindposes` arrays, on that rebuild only); while a pair is crossed, a look-up in a small list, and for that player a few vector operations, with two transform writes only when the X is 0.25° or more off the body's up. Knives on the hips cost nothing per frame. No allocation per frame. |
| `ZNet.OnNewConnection`, `ZNet.RPC_PeerInfo`, `ZNet.Update` | Postfix | Server rules and join check (copies; the join check's verdict comes from the framework, and re-checks after a player turns the mod on or off come from the `NetworkGate.PeerStateChanged` event, not a patch). | `Update`: two bool reads when idle. |
| `Character.Damage(HitData)` | Prefix, **Debug builds only** | Self-test record of the local player's hits (attacker-side damage, skill). | Debug only. |
| `Attack.AddHitPoint(List<Attack.HitPoint>, GameObject, Collider, Vector3, float, bool)` (private) | Postfix, **Debug builds only** | Self-test record of the objects the local player's melee sweep touched (vanilla divides each hit by objects × 0.75 when there are several). | Debug only. |
| `Player.GetRandomSkillFactor(Skills.SkillType)` | Postfix, **Debug builds only** | Self-test record of the random skill factor vanilla rolled for each local player hit. | Debug only. |

No transpiler. No `HaveSecondaryAttack` patch (tiny, inlining risk; D10). No `UnequipItem`, `UnequipAllItems` or
`UnequipDeathDropItems` patch (D23). Methods called (publicized): the private `Humanoid.SetupEquipment`,
`SetAnimationState`, `TriggerEquipEffect`, `ShowHandItems`, `Attack.DoMeleeAttack`, `Player.TakeInput`,
`Player.IsEquipActionQueued`, `Player.QueueEquipAction`, `Player.RemoveEquipAction`, `Player.GetActionQueueCount`;
fields `m_rightItem`, `m_leftItem`, `m_hiddenRightItem`, `m_hiddenLeftItem`, `m_visEquipment`, `m_zanim`,
`Player.m_attackMissAdrenaline`, `Player.m_actionQueue` (the swap's `m_doneAnimation`), and on `VisEquipment`
`m_leftBackItemInstance`, `m_rightBackItemInstance`, `m_currentLeftBackItemHash`, `m_currentRightBackItemHash`,
`m_currentRightBackItemQuality`, `m_backMelee`, `m_backTool`, `m_isArmorStand`.

### 7.3 State and lifetime

| State | Where | Lifetime |
|---|---|---|
| Off-hand marker | the item's `m_customData` | Persistent (saved); set and cleared as in 2.4. |
| Restore candidate | static (item, frame) | One frame. |
| Main-hand intents | static array of 4 (item, time); toggle and queue window flags | Until consumed, dropped by the next request for that item, or 5 s; windows within one call. |
| "Secondary requested" | static bool | Within `StartAttack`. |
| Recorded swing | static (clone, main, off, pattern, chain step, event index) | Until the next converted swing; cleared in `OnDeactivated`. |
| Both-hands re-entry flag | static bool | Within one `DoMeleeAttack` postfix. |
| Pending swap (the queued off-hand weapon), combo restart flag | static (item, bool); the action itself lives in vanilla's queue | Swap: until vanilla ends or drops the action, the pair changes (taken out of the queue, checked every frame and before each queue step), the player changes, or the feature turns off (taken out of the queue first). Flag: until the next converted swing starts. |
| Pending eat return | static bool | Until the main weapon is back or `m_hiddenRightItem` is empty. |
| First-pairing message shown | static bool | Session. |
| Last seen rules object, `ObjectDB`, local player | static references | Reset in `OnActivated`. |
| Template cache, exclusion set, trail caches | static | Rebuilt when the rules object, the `ObjectDB` instance or the local player changes; cleared in `OnDeactivated`. (The hand table is fixed.) |
| Pair-weapon check per back hash (sheathed pair) | static dictionary | Rebuilt when the `ObjectDB` instance changes; cleared in `OnDeactivated`. The placed poses themselves (the X, the knife moved to the hips bone) live on the back instances, which vanilla destroys and makes again. |
| Crossed pair retries (players whose back pair was left flat, 2.7) | static list of `VisEquipment` | Until that player's pair crosses, vanilla rebuilds its back items, or the player object is gone (dropped at the next retry); cleared in `OnDeactivated`. |
| Crossed pairs kept level (2.7) | static list (player's `VisEquipment`, the two instances, their joint, the two long axes, the normal and crossing point in the joint's space) | From the crossing until vanilla rebuilds that player's back items (removed at the start of `Apply`, before the setting check, so turning the setting off stops it), an instance is gone or no longer on the joint (removed by `Level`), or the player object is gone (dropped at the next crossing of any player); cleared in `OnDeactivated`. |
| Last moves Info line | static string | Reset when the `ObjectDB` instance changes (a new world) and in `OnDeactivated`, so each world entry and each activation logs it once. |
| Server rules | `ServerRules` | Per `ZNet` session (copy of Forge's); `DualRules.Defaults` while a client waits for them. |
| Join check queue, `PeerStateChanged` subscription | `PlayerCheck` (server) | Queue: until each peer's deadline. Subscription: from `PlayerCheck.Start` to `PlayerCheck.Stop`. |

Nothing in `ObjectDB`, prefabs or `SharedData` is ever modified (the per-hit swaps touch only the per-swing clone and,
for one call, the player's `m_attackMissAdrenaline`; the sheathed pair placement and its levelling touch only back
instances vanilla made and will make again), so there is nothing to revert in live items.

### 7.4 Live toggle

- `OnActivated`: reset the last-seen rules, `ObjectDB` and player (so the first frame rebuilds the caches and
  revalidates); clear stale markers on the local player's `m_rightItem` and `m_hiddenRightItem`; cache the keys;
  `BackCross.RebuildAll()` (pairs already sheathed on any player are placed on the next frame); `ServerRules.Start()`;
  `PlayerCheck.Start()` (subscribes to `NetworkGate.PeerStateChanged`, schedules every connected peer);
  `SelfTests.Register()`. (With the fallback of 6.3, also reset the lazy other-mods check.)
- `OnDeactivated` (patches still applied during the call): `SelfTests.Unregister()`; `PlayerCheck.Stop()`
  (unsubscribes, cancels pending checks); `ServerRules.Stop()`; then, **only when the game is not quitting**
  (`Game.instance != null && !Game.instance.IsShuttingDown()`, `Player.m_localPlayer` alive as a Unity object, its
  `m_nview.IsValid()`):
  `Hands.CancelSwap` (a queued swap leaves vanilla's queue; without the patch vanilla would equip the off-hand weapon
  into the main hand at the end of the action), then
  `Hands.ReleaseOffHand(Player.m_localPlayer)`: if the left hand holds a one-handed weapon, unequip it
  (`triggerEquipEffects: false`; it stays in the inventory) and clear its marker, which runs `SetupEquipment` so the
  stance becomes vanilla again; a hidden off-hand weapon (`m_hiddenLeftItem`, one-handed) is forgotten the same way, so
  the next draw shows only the main weapon. Always: clear the intents, candidate, pending eat return, pending swap,
  recorded swing and caches, and `BackCross.RebuildAll()` (every player's sheathed pair is rebuilt by vanilla on the
  next frame, once the patch is gone: vanilla's placement again, the knife on the left hip destroyed with the old
  instances). A swing in progress finishes with the main weapon only
  (our `DoMeleeAttack` patch is gone). At quit the
  framework calls `OnDeactivated` from the plugin's `OnDestroy` after `Game.Shutdown` saved the profile and
  `ZNetScene.Shutdown` reset the ZDOs: releasing then would spawn unequip effects on a dying player and dirty the
  clean-log test for nothing; the saved pair loads next time.
- `Enabled = false` + restart: no patch; vanilla equip rules collapse a saved pair to one weapon at load (4.5).
- Turned off while connected to a server with the mod: `OnDeactivated` puts the off-hand weapon away as above; after
  the refresh the framework sends `HelloState "off"`, and the server refuses the player about a second later unless
  its `AllowPlayersWithoutMod` is on (4.4). Nothing in this mod sends anything for it.

### 7.5 Debug in-world self-tests (`SelfTests.cs`, `#if DEBUG`)

Run by `./tools/Test-InWorld.ps1 -Mod Weapons.DualWield` in the throwaway world (god mode, world modifier StaminaRate 0).
Items are added with `Inventory.AddItem(prefabName, ...)` (the food for the eat window with
`Inventory.AddItem(GameObject, int)`, 1.2) and removed in `finally`; spawned creatures are destroyed in
`finally`; skills changed for a measurement are restored in `finally`; a swap still queued is taken out of vanilla's
queue in `finally`; hands are emptied at the end. Settings are forced only through Debug overrides
(`ServerRules.TestRules`, `Controls.TestMainHandHeld`, `LeftTrails.TestLeftHandTrails`, `BackCross.TestEnabled`), never
a `ConfigEntry`. Every step waits a frame and checks the invariant of 2.4.

- **`dual.data`**: `AxeBerzerkr` and `KnifeSkollAndHati` exist with the dumped values (animations, chain 4 / 3,
  specials, stances `DualAxes` / `Knives`, stamina 16/32 and 14/42, primary `m_resetChainIfHit` Tree / None); every
  trigger is found by `HasParameter` (`dualaxes0-3`, `dualaxes_secondary`, `dual_knives0-2`, `dual_knives_secondary`);
  the hand table gives the patterns of 2.6 for every known trigger, including a one-off `dualaxes1` with chain levels 0
  (off), an unknown chain trigger (`swing_longsword0-2`: main, off, main) and an unknown single move (both); template
  fallback: `PairMoves = NoSuchItem` (test rules) → default used and a
  warning; eligibility over every `ObjectDB` item: all player swords, axes, clubs and maces and knives (`$` names)
  eligible except `KnifeButcher`; spears, tankards, bombs, `AxeBerzerkr` not (NOTE with the player list and the count of
  non-player items that also pass, 184 in the dump); every eligible player item has `m_spawnOnTrigger` null, `m_harvest`,
  `m_attach` and `m_pickaxeSpecial` false on both attacks (else a NOTE, R23); NOTE the per-weapon fields of 1.4 for each
  eligible item; `ExcludedWeapons = AxeBronze` → `AxeBronze` excluded, `FW_AxeBronze` not, both for the prefabs' own
  items and for items made the way `Inventory.AddItem(name)` makes them (own `SharedData` copy; NOTE whether the copy
  shares the prefab's object); NOTE, for every `Feast` prefab, whether eating at it hides the main weapon (a
  `Consumable` `ItemDrop` piece, 1.2); `DualRules` round trip
  (every field, including `HitPattern` and `BothHandsDamage`), clamping, unknown layout refused, single player never
  takes rules from a peer; `ServerRules.Pick`: a client of a server with no server rules yet → `DualRules.Defaults`
  (never the own rules, also when the own rules differ from the defaults), with the server's rules → those, not a
  client → own; `PlayerCheck.Decide` verdicts: compatible → Compatible; not compatible (no mod, turned off, other
  network version: the framework's `PeerCompatible` is false for all three) with the setting off → Refuse, on →
  Allowed; not the server, gone, not ready or already being kicked → Skip (the framework's side, `PeerCompatible`
  and `HelloState`, is covered by its own tests N07 and N08 in `src/Shared/TESTING.md`); `ForeignMods.Find()` in the
  test game → none (NOTE the plugin names scanned); the pure name match behind it → a match for "DualWielder",
  "DualWieldCore" and "balrond DualMastery", none for "Dual Wielding" (our own name).
- **`dual.equip`**: `SwordIron` then `AxeIron` → right/left, marker on the axe only, `statei` = 15; `MaceIron` →
  replaces the axe; `KnifeBlackMetal` with the main-hand override → knife alone, others unequipped; intent windows:
  an intent stored for an item is not used by a `ShowHandItems` or a drag of that item; pair + `ShieldBronzeBuckler` →
  buckler left, sword kept; then `AxeIron` → axe replaces the sword, buckler kept; pair + `Torch` → torch left, sword
  kept; pair + `SpearFlint` → spear alone; `KnifeFlint` + `KnifeBlackMetal` → `statei` = 11; `KnifeButcher` never goes
  left; swap (2.3), through `Hands.TrySwap` (the key's own path; a key press cannot reach a test): first a vanilla
  hotbar equip of `MaceIron` with a pair, sampled (NOTE: the `equipping` bool, `InMinorAction`,
  `InMinorActionSlowdown`, the animator clips per layer), to compare with the swap; a queued swap then a dodge → the
  swap is dropped, same hands; the key while sprinting (`IsRunning` set for that one call) → refused, nothing queued;
  a queued swap then a hide → dropped, the draw gives the same hands; the swap itself →
  queued as vanilla's equip action of the off-hand weapon (its `m_equipDuration`, done trigger `equip_hip`), hands
  unchanged until it ends, a second press does nothing, `StartAttack` refused while queued, then hands and marker
  swapped, the time from the key to the swap within the equip duration + 0.3 s, the `equipping` bool seen, the
  combo-restart flag up; NOTE the time, the same samples while queued and for 0.6 s after (the draw), and whether
  `equipping` is in the player's synced bools (R25); two swaps in a row → the second waits vanilla's 0.3 s queue
  pause; one attack press during a swap (vanilla's 0.5 s press buffer `m_queuedAttackTimer` set right after the key) →
  no swing before the hands changed; NOTE whether the press still starts a swing and how long after the swap, with
  the samples between the two (R25, T32), then a swap back; unequip the main → next frame the off-hand weapon is in the main hand, marker cleared; a broken weapon (durability 0) is refused like vanilla; a forced
  exception inside the apply step (Debug hook) → hands consistent, `m_equipped` matches the slots. Screenshots: sword +
  axe idle, knife pair idle.
- **`dual.keep`**: `HideHandItems()` + `ShowHandItems()` → same hands (three times); eating a `Raspberry` from
  `Inventory.AddItem(name)` (own `SharedData` copy, like most food in a real game) → nothing hidden, same hands (NOTE
  whether `ObjectDB` knows its `SharedData` and whether the eat visual started); eating: `UseItem` on a `Raspberry`
  added through `Inventory.AddItem(GameObject, int)` (the prefab's `SharedData`, so vanilla shows it and hides the main
  weapon; an older stack it merges into is relinked for the test and put back after), the eat timer running, wait its
  eat time, same hands; each eat step checks that the eat window started and is skipped (failed) otherwise; eat, then
  start a dodge (the private `Player.Dodge`) just before the eat ends → the
  main weapon returns after the dodge, same hands; eat, then a full `HideHandItems()` during the eat, then after the eat
  time → both weapons drawn, same hands; `StartAttack` during the eat window returns false; load order: empty the
  hands, flag both weapons `m_equipped`, reorder the inventory list (publicized) so the off-hand weapon comes first,
  call `EquipInventoryItems` → same hands; then the other order; drags through the real `InventoryGrid.DropItem`
  (`InventoryGui.instance.m_playerGrid`) with vanilla's unequip / re-equip sequence of `OnSelectedItem`: the main weapon
  to an empty slot, the main weapon onto the off-hand weapon's slot, the off-hand weapon onto the main weapon's slot →
  same hands each time (clones); unequip the main then nothing → off-hand weapon becomes the main next frame;
  `UnequipItem(right)` then `UnequipItem(left)` in one call chain → both hands empty, both `m_equipped` false; radial
  sequence (equip `Hammer`, unequip it, `EquipItem(left)`, `EquipItem(right)` in one frame) → same hands;
  `UnequipAllItems` → nothing equipped, no item equipped in no slot; stale marker: mark the main weapon by hand, run
  `Hands.ClearStaleMarkers` (what `OnActivated` and a player change run), hide and draw → same hands; rule 2 leaves the
  main weapon unmarked; rules change: test rules `ExcludedWeapons = AxeIron` while holding `SwordIron` + `AxeIron` →
  next frame the axe is put away, the sword stays, `statei` = 1; test rules back.
- **`dual.attack`**: a `Troll` spawned 1.5 m in front with its AI component disabled and its health raised
  (`SetMaxHealth` / `SetHealth` to 100000, so it never dies); hits are read from the Debug `Character.Damage` record
  (attacker-side damage, before the victim's backstab, stagger ×2 and resistances) and the `DualSwing` record. Swings
  are driven the way the attack button does it (the test keeps the player's private `m_queuedAttackTimer` above 0, so
  vanilla `PlayerAttackInput` starts and chains the swings through `HaveQueuedChain`).
  - *A, hands and wear*: `SwordIron` + `AxeIron`, skills as they are: four chained swings → triggers `dualaxes0-3`,
    hands main | off | main, off | main, off; both durabilities lower; Swords and Axes skills (level or accumulator)
    raised; clone stamina = max(10, 10) = 10; the special → `dualaxes_secondary`, two hits (main, then off), special
    stamina 20.
  - *B, off-hand damage*: Swords set to 100 for the measurement (random skill factor 0.85-1), two `SwordIron` copies,
    `OffHandDamage = 50`: in `dualaxes2` and `dualaxes3` the off-hand hit / main-hand hit ratio is 0.50 (within 0.01);
    `BothHandsDamage = 50` against 100 for the special: each hand's hit 0.50 of its hit at 100. The hits are compared
    without vanilla's two factors of its own, which the Debug record keeps per hit: the random skill factor (a fresh
    roll per hit, `Player.GetRandomSkillFactor`; checked within 0.85-1) and the multi-object split
    (`Attack.DoMeleeAttack` divides each hit by objects × 0.75 when the sweep touched more than one object, the target
    plus the ground or a rock, with `m_multiHit` and `m_lowerDamagePerHit`; the objects from `Attack.AddHitPoint`).
    NOTE each hit's raw damage, roll, objects and split.
  - *C, weapon-owned fields*: `SwordNiedhogg` main + `SwordNiedhoggBlood` off, then `SwordNiedhoggLightning` off: during
    each off-hand hit the clone's `m_damageMultiplierPerMissingHP`, `m_spawnOnHitChance`, `m_specialHitType`,
    `m_resetChainIfHit` and effect references equal the off-hand weapon's own attack (the last one the template's);
    during main-hand hits the main weapon's; after the swing the clone holds the main weapon's values again.
  - *D, knives*: `KnifeFlint` + `KnifeBlackMetal`: `dual_knives0-2` → main | off | both; the leap → both.
  - *E, BothHands*: `HitPattern = BothHands`: every event records two hits (main, then off); miss adrenaline changes once
    per event (NOTE the player's adrenaline before and after a missed swing).
  - *F, swap and Club*: after `dualaxes0`, swap (queued, waited for) → the combo-restart flag is up and the next swing
    is `dualaxes0` again (the equip time alone also ends the chain window). `Club` in the main hand: the special is
    refused; after a swap it plays.
  - NOTE lines for the unverified points: at each hit event the current clip names on both layers (R2), the distance
    from `m_leftHand` and `m_rightHand` to the target (R4), the number of events per clip including fast chaining
    (duplicate events during transitions, R19), the time of each swing and event (R13); `InMinorAction()` while eating
    (R21) is noted by `dual.keep`. Screenshots at each hit event of `dualaxes2` and `dualaxes3` (R3).
- **`dual.visuals`**: the player ZDO's left item hash equals the off-hand weapon; `SetWeaponTrails(true)` turns on the
  left instance's trails (NOTE the number of `MeleeWeaponTrail` components on one-handed prefabs, R6) and `false` turns
  them off; the second call reuses the cached trail array; NOTE the player prefab's `m_useAllTrails` (R6),
  `m_attackMissAdrenaline` (R8), and whether `statef` / `statei` are in the player's `ZSyncAnimation` sync lists (R7);
  screenshots: blocking pose with a pair (R10), a sword in the left hand close up (R5). Sheathed pairs placed by
  weapon kind (G11, 2.7), with the setting forced on in memory (`BackCross.TestEnabled`), each hidden through the hide
  key's `HideHandItems`: NOTE both back joints (name, parent, position and rotation relative to the player root).
  **Back pairs** (two `SwordIron`, `SwordIron` + `AxeIron`): both instances on `m_backMelee`, the angle between their
  long axes at least 38° (twice the 20° minimum, less 2°), their centres within the 2.5 cm lift + 1 cm, symmetric
  about the vertical of their plane (within 2°), and again 0.8 s later, once the stance change is over (the levelling;
  NOTE the total and the biggest single turn `BackCross.Level` gave the pair since it crossed), the plane along the
  body (its normal at least 0.7 along the horizontal direction from the body's axis to the crossing: not standing off
  the back with a blade through the body; NOTE that normal's right, back and up parts relative to the player), crossed
  by the patch (its recorded layout), the reference sword left at the game's own angle (not tilted: its vanilla angle
  between 20° and 60°); NOTE what `BackCross` did (reference, vanilla angle, tilt, flat axes, lengths). **Knife
  pairs** (two `KnifeBlackMetal`, then `KnifeFlint` + `KnifeBlackMetal`), a couple of frames after the hide and again
  0.8 s later: placed by the patch as a knife pair, its mirror plane from the body's bind pose; the main-hand knife at
  the game's own pose on `m_backTool`; the off-hand knife on the hips bone and the mirror image of its own game pose
  across the hips' centre plane (centre within 1 cm, long axis within 2°, its other face out within 2°); for two
  `KnifeBlackMetal` also each other's mirror image across that plane (1 cm, 2°). The pair rides the pelvis (D27), so
  symmetry about the player root's centre plane is only NOTEd (the angle between the two planes, and how far the
  off-hand knife is from the main knife's mirror image across the body's plane). Checked in every pose instead: each
  knife's centre at least 3 cm on its own side of the hips' centre plane and of the body's (main on the right), and
  each knife outside its own thigh: its centre and its lower end (centre less half its length along its long axis)
  out from the line from the hip joint to the knee, measured along the pelvis's right (left for the left knife). The
  thighs are bones under the hips named like a leg (any name with "leg"; the rig's names are not in `.ref`, the NOTE
  names the bones found): children of the hips, or grandchildren under a bone of another name (never a leg bone's
  child, the shin), one on each side of the hips' centre plane, each the one with the longest bone to a child of its
  own (the knee, at least 15 cm). For two `KnifeBlackMetal`, after the screenshots, the same pose checks over every
  frame of 1.2 s of walking, jogging and sprinting in place (after 0.6 s to settle into the gait): the player
  controller off, `Player.SetControls` forward each frame (walk toggle for the walk, the run flag for the sprint), the
  player put back on its spot every frame (the animator's forward speed is the commanded velocity `m_currentVel`, so a
  pinned player still plays the gait); each gait's top forward speed at least 0.8 of the player's own speed for it and
  the sprinting flag only in the sprint, so a pass means the gait played; the least numbers per gait NOTEd, one
  screenshot per gait (`sheathed-knife-pair-walk`, `-jog`, `-sprint`); controller, walk toggle, spot, facing and the
  Run skill put back. NOTE what `BackCross` did (plane source, the plane's angle to the body's centre plane on the
  rebuild frame, flat axis, length). Setting off + `RebuildAll` with the `KnifeBlackMetal` pair sheathed →
  both knives at the game's own pose on `m_backTool` (the moved one too). **Knife + sword**, in both hand orders
  (`KnifeBlackMetal` + `SwordIron`, `SwordIron` + `KnifeBlackMetal`): seen by the patch and left apart (its recorded
  layout), the knife at the game's own pose on `m_backTool` and the sword on `m_backMelee`, and still so 0.8 s later
  (nothing moved, nothing levelled). A screenshot from behind and one from the right side (`sheathed-sword-pair`,
  `sheathed-sword-axe`, `sheathed-knife-pair`, `sheathed-knife-sword`, each also `-side`: the body turned a quarter
  right for one frame, the eye's rotation kept so the camera stays). Setting off + `RebuildAll` with the sword pair →
  both swords at the game's own pose (parent, local position and rotation of the prefab's `equipoffset`; what turning
  the mod off leaves too) and a screenshot (`sheathed-sword-pair-vanilla`); on again (the record reset first) →
  crossed. One `SwordIron` sheathed → nothing done, the game's own pose.

### 7.6 Framework and `src/Shared` changes

**Done** (already in `src/Shared` in this run; `Test-Framework.ps1` checks the always-on patches, and the framework's
multiplayer tests N07 and N08 in `src/Shared/TESTING.md` cover the join-check support; this mod only uses them):

- `NetworkGate.PeerCompatible(peer)` (has the mod, same `ModNetworkVersion`, not turned off on their game),
  `NetworkGate.PeerProblem(peer)` (the reason for the log), and the server-side event `NetworkGate.PeerStateChanged`,
  raised when a connected player's `HelloState` RPC says their copy turned on or off. The hello now carries the client's
  own ready state. Used by the join check (4.4); `PeerHasMod` stays in use only for the rules push (delivery).
- `ItemDataExtensions.GetCustomBool` / `SetCustomBool` for the off-hand marker.
- Not used: `[AlwaysOnPatch]` (nothing of this mod must outlive the toggle, 7.2) and `ItemKinds` (now keeps tower
  shields in the Shield kind, Tower Shield Wall's change; eligibility has its own rules, 2.1).

**Done: the local blocker hook** (6.3, D17; added to `src/Shared` in this run, and used by this mod since its
foundation stage). `ModPlugin` has `protected virtual string LocalBlocker() => null`. `ComputeLocalState` calls it
after the dependency checks (an exception there is reported through `PatchGuard` and never blocks); a non-null text
makes the feature inactive with that text as its status (`ModState.Conflict`, shown in the panel like a missing
dependency). Because `ComputeLocalState` is also what the framework reports to the server as the client's ready state,
the feature is unpatched, the hello says "off" and `HelloState` follows changes, so the server's join check refuses
that player with no extra code. `Plugin.LocalBlocker()` returns `ForeignMods.BlockerText()`. The framework side
(probe case, `docs/modding/framework.md`, `CLAUDE.md`) belongs to the framework change, not to this mod. The fallback
of 6.3 is not used.

### 7.7 csproj metadata

`ModIdea` = `Dual wielding`; `ModNetworkVersion` = 1; `ModSide` = Both, `ModMultiplayer` = Compatible (already);
`ModMultiplayerNotes` as in 4.5 (replacing the scaffold's shorter text); `ModDescription`: "Wield a one-handed
sword, axe, club or knife in each hand. A second one-handed weapon goes to your off hand, pairs fight with the game's
own dual axe and dual knife moves, and every hit is struck by one of your two weapons, or both."

---

## 8. Test plan

The `TESTING.md` items. Spawn names checked in the runtime dump (items, creatures) or the SoftRef manifest (food);
`MC_SmokeScreen` is Sneak Ambush's own item (its design). "Pair" = `SwordIron` in the main hand + `AxeIron` in the off
hand unless said otherwise.

### Setup

- `devcommands`; spawn `SwordIron` (two), `AxeIron` (two), `MaceIron`, `Club`, `KnifeBlackMetal`, `KnifeFlint`,
  `KnifeButcher`, `SpearFlint`, `ShieldBronzeBuckler`, `Torch`, `Hammer`, `BombSmoke`, `Raspberry`,
  `SwordMistwalker` (frost damage), `AxeJotunBane` (poison damage), `SwordNiedhogg`, `SwordNiedhoggBlood`,
  `SwordNiedhoggLightning`; for comparison `AxeBerzerkr` and `KnifeSkollAndHati`; targets `Greyling`, `Troll`; for
  X04 `MC_SmokeScreen` (with Sneak Ambush). For the stamina test turn god mode off and use a world without the stamina
  modifier.
- Put the weapons on the hotbar. Keep `BepInEx/LogOutput.log` open (`./tools/Watch-Log.ps1 -Mine`).
- Multiplayer: a dedicated server or a host with the mod, and a second game. For M01 "other network version": a build
  of this mod with another network version, made without deploying (`dotnet build
  src/Combat/Weapons.DualWield/MC.Combat.Weapons.DualWield.csproj -p:ModNetworkVersion=2 -p:DeployToGame=false`) and
  copied by hand over the second game's copy (put the normal build back afterwards).

### Single player

- **T01 Pair (G1, G2):** equip the sword, then the axe (hotbar and inventory right-click). Sword in the right hand, axe
  in the left, both marked equipped, dual-axe stance. Also mace + knife and two swords.
- **T02 Replace the off hand (G2):** with the pair, equip the mace: the mace replaces the axe; the sword stays.
- **T03 Main-hand key (G3):** hold Left Alt and equip the knife (hotbar, inventory right-click, then the radial menu):
  the knife alone in the main hand, sword and axe put away. Without a pair: Left Alt + another sword replaces the sword.
  Left Alt + 1 (a sword), then quickly 2 (an axe) without Left Alt: the sword ends in the main hand and the axe in the
  off hand.
- **T04 Swap key (G4):** with the pair press H: after the equip animation the hands swap, message "Main hand: Axe"
  (localized name), attacks now start with the axe, blocking uses the sword. Refused while a swing runs (or in the
  frame one starts); H pressed together with the attack button either comes too late (the swing keeps its hands) or
  queues the swap first (the swing waits for it, T32), never a swing whose hits change weapons midway. Pressing H
  quickly several times: at most one swap every half second. Swing once, press H, swing again: the second swing is the
  first step of the combo again. (Animation, timing and cancelling: T32.)
- **T05 Shield (G5):** with the pair equip the buckler: buckler left, sword kept. Then equip the axe: the axe replaces
  the sword, the buckler stays.
- **T06 Torch (D4):** with the pair equip the torch: torch left, sword kept.
- **T07 Not pairable (D5):** with the pair equip the spear, then the bomb: each alone (both weapons put away). The butcher
  knife never goes to the off hand. A weapon in `ExcludedWeapons` never pairs.
- **T08 Axe moves (G6):** pair on a `Troll`: hold attack: a 4-step combo (Berserkir moves), 6 hits; the special is the
  cleave. Compare with the real `AxeBerzerkr`. Also mace + sword and knife + sword (axe moves).
- **T09 Knife moves (G6):** two knives: the fast 3-step dual-knife combo and the leap special; knife stance.
- **T10 Every hit from a real weapon (G7):** `SwordMistwalker` + `AxeJotunBane` on a `Troll`: frost on the main-hand
  hits, poison on the off-hand hits, both on the cleave; both weapons lose durability; Swords and Axes both rise
  (skills screen).
- **T11 Off-hand damage (G7, settings):** two `SwordIron` on a `Troll` (ignore the first hit: an unaware target takes a
  sneak-attack bonus). `OffHandDamage = 50`: every off-hand hit (the second swing's hit, the second hit of the third
  and of the fourth swing) is about half of the main-hand hit before it in the same swing, or of the first swing's hit
  (both hits of the fourth swing are doubled). Back to 100.
- **T12 Stamina (D9):** without god mode: a pair swing costs about the same as one swing of the dearer weapon:
  `KnifeFlint` + `SwordIron` = the sword's (10); `KnifeBlackMetal` + `SwordIron` = the knife's (12); the special twice
  (axe moves) or three times (knife moves) that.
- **T13 Club (D10):** club in the main hand: no special attack; after a swap (club in the off hand) the cleave works.
- **T14 Block and parry (D11):** the off-hand weapon blocks (its block value) and parries a `Greyling` or `Troll`; a
  knife in the off hand blocks badly.
- **T15 Trees (D21):** axe + axe on a tree: every swing is the first move of the combo, struck by the main-hand axe,
  which chops; Wood Cutting rises. Sword + axe (sword in the main hand): the sword hits the tree, no chopping; press H:
  the axe now chops. Two knives: no restart, no chopping.
- **T16 Hide and draw (G8):** R, R, several times: same hands each time.
- **T17 Eating (G8, D20):** eat a `Raspberry` from the inventory with a pair: no food in hand, nothing hidden (vanilla,
  1.2), same hands. Then eat at a feast (`spawn FeastMeadows`, a piece in the SoftRef manifest; whether it hides the
  main weapon is prefab data, see the `dual.data` NOTE): when the main weapon is hidden, same hands afterwards. Eat and
  roll at once: same hands after the roll. Eat and press attack at once: no attack until the weapons are back, then
  same hands. Eat and jump into deep water: after leaving the water and pressing R if needed, same hands. The hidden
  eat window with food from the inventory is covered by `dual.keep` (a hand-made item with the prefab's `SharedData`).
- **T18 Stations, beds, barber, swimming (G8):** use a workbench, sleep in a bed, use the barber, swim: after R the same
  hands. Sit on a chair: the weapons stay in hand (vanilla).
- **T19 Relog (G8):** log out and in with a pair. Then drag the off-hand weapon to any other inventory slot (it now
  loads last), relog: same hands. Then drag the main weapon to any other slot (now it loads last), relog: same hands.
- **T20 Death (G8):** die with a pair in a normal world: both weapons in the tombstone, nothing equipped after respawn.
  In a world with the "keep equipment" death modifier (world settings): the same pair after respawn.
- **T21 Inventory moves (G8):** drag the main weapon to an empty slot: same pair. Drag the main weapon onto the off-hand
  weapon's slot, then the off-hand weapon onto the main weapon's slot: same pair each time. Drag the main weapon into a
  chest: the axe becomes the main weapon. Drop the off-hand weapon on the ground: the sword stays.
- **T22 Unequip (G8):** unequip the main weapon (hotbar): the off-hand weapon moves to the main hand. Unequip the
  off-hand weapon: the main weapon stays.
- **T23 Radial hammer (G8):** open the radial menu (G), pick the hammer, then pick it again: same hands.
- **T24 Trails (E1):** both blades trail during dual swings; `LeftHandTrails = false` → only the right one.
- **T25 Move settings (E3):** `PairMoves = KnifeSkollAndHati`: every pair uses the knife moves; `PairMoves = Nothing`:
  a warning in the log and the axe moves; `SecondaryMoves = MainWeapon`: the sword's own special (sword hits only).
- **T26 Look and feel (R3, R5, R10):** watch every family in the axe moves and the stance (idle, run, block): blades
  through the body, the left-hand grip. Note what looks wrong. (The sheathed pair: T33.)
- **T27 Both hands (E5):** `HitPattern = BothHands` with `SwordMistwalker` + `AxeJotunBane`: every hit shows two damage
  numbers, with frost and poison each time; `BothHandsDamage = 100`: each number as large as a normal hit. Back to
  `Alternate` and 50.
- **T28 Weapon effects (G7):** `SwordNiedhogg` + `SwordNiedhoggBlood` (Blood in the off hand), at low health (take
  damage to about a fifth of your health): the off-hand hits deal more than the main-hand hits of the same step; press
  H: now the main-hand hits deal more. `SwordNiedhoggLightning` in the off hand: lightning strikes on some off-hand
  hits only.
- **T29 First pairing message (G2):** the first pairing of the session shows the message naming Left Alt and H; the
  next pairing does not; logging in with a saved pair does not.
- **T30 Rules change (2.4):** with sword + axe, set `ExcludedWeapons = AxeIron`: the axe is put away at once, the sword
  stays (one-handed stance). Clear it: pairing works again.
- **T32 Swap animation and timing (G4, D22, D26, R25):** with the pair press H, standing and walking. Expected: the
  game's equip animation with the HUD bar "Equipping <axe>" for about 0.2 s (as when you switch weapons on the
  hotbar), then the hands swap with the hide key's draw animation; walking speed during it as for a hotbar equip.
  Press H, then hold attack during the bar: no swing until the hands changed, then the swing starts with the axe (note
  how long after the swap). Press H, then click attack once during the bar: note whether the swing starts after the
  swap (and how late) or not at all. Press H and dodge at once (also: jump): no swap. Press H while sprinting: no swap,
  top-left "Cannot swap hands while sprinting"; press H, then sprint: no swap, the same message. Press H twice
  quickly: one swap. Note how the two animations look together.
- **T33 Sheathed pair placement (G11, D27, D28, R26):** press R with two `SwordIron`, with `SwordIron` + `AxeIron`,
  `MaceIron` + `SwordIron`, two `KnifeBlackMetal`, `KnifeFlint` + `KnifeBlackMetal`, `KnifeBlackMetal` + `SwordIron`
  and `SwordIron` + `KnifeBlackMetal`; also with a pair at a workbench and while swimming. Expected: two swords, axes
  or maces crossed in an X on the back, hilts over both shoulders; two knives one on each hip, the main-hand knife
  exactly where the game hangs a single knife (right hip), the off-hand knife its mirror image on the left hip (same
  height, distance and angle); a knife with a sword (either hand): the knife on the right hip, the sword on the back,
  each exactly as the game places it alone. One sheathed weapon (sword alone, R): as in the game.
  `CrossSheathedPair = false`: a pair of one kind overlaps on one spot, as in the game (the knife + sword pair does
  not change); back to true: placed again at once. Standing, walking and running with the pair sheathed, and just
  after R mid-stride (several times): the X stays symmetric (both weapons at the same angle either side of the
  vertical; note whether it visibly turns against the back while running, the levelling); the two knives move with the
  hips like knives on a belt and stay mirror images (note whether one looks higher, lower or more tilted); standing
  still the hips turn a good way from where the body faces (about 30-45° in the self-test), so the knives turn with
  them, one a little forward and one a little back: expected; in every stance and stride each knife stays on its own
  side and outside its own leg, never both on one side, never between the legs or through a thigh. Lie in a bed
  with a back pair and with a knife pair sheathed and set `CrossSheathedPair` false, then true (a rebuild while
  lying): after standing up the back pair is crossed within a moment, the knives one per hip. Note what looks wrong
  (clipping into the body or the legs, the crossing point, the knives' angle, a knife off the hip).

### Multiplayer

- **M01 Join check (G9):** with `AllowPlayersWithoutMod = false` on the server, each of these players is refused about
  a second after joining (their game shows "Incompatible version" and returns to the main menu), and the server log
  names the player and the reason: a player **without the mod** ("does not have the mod"); a player with the mod and
  **`Enabled = false`** ("has the mod turned off"); a player with the **other network version** build of the setup
  ("has another version of the mod", with both network versions; their own status says the versions cannot talk).
  A player with the mod turned on joins normally. With `AllowPlayersWithoutMod = true`, all three join, the server log
  warns with the reason, and none of them can make a pair.
- **M01b Turned off while connected (G9):** a player with the mod joins and holds a pair, then unticks Dual Wielding in
  the MC Mods panel: the off-hand weapon is put away, their log says "Told the server that Dual Wielding is now off on
  this game.", and about a second later they are refused ("Incompatible version"; server log "has the mod turned
  off"). Untick and tick again quickly (within the second): they stay. With `AllowPlayersWithoutMod = true`: they stay
  and the server log warns.
- **M02 Server settings (G9):** server `OffHandDamage = 50`, client 100: the client uses 50 (Info line); back in single
  player the client uses its own.
- **M02b Live rule change (G9):** while a client holds sword + axe, the server sets `ExcludedWeapons = AxeIron`: the
  client's axe is put away; the server sets `PairMoves = KnifeSkollAndHati`: the client's pairs switch to the knife
  moves and stance at once.
- **M03 Hand-off, observer without the mod:** with `AllowPlayersWithoutMod = true`, the unmodded player watches a dual
  wielder: two weapons in hand, dual stance, dual moves; the dual wielder's hits on creatures the unmodded player
  owns deal damage; no left trail and an overlapping sheathed pair (expected).
- **M03b Observer with the mod:** two modded players: each sees both blades of the other's dual swings trail; with
  `LeftHandTrails = false` the observer sees only the right trail.
- **M04 Hand-off, items:** give the former off-hand weapon to a player without the mod: a normal weapon; give it back:
  it pairs normally.
- **M05 PvP:** a dual wielder hits a player who blocks with a shield: both weapons' hits blocked or parried; damage from
  each weapon.
- **M06 Server toggles the mod:** the server turns the mod off: every client's off-hand weapon is put away, status
  inactive; on again: pairs work.
- **M07 Vanilla server:** a client with the mod joins a server without it: status inactive, a saved pair loads as one
  weapon, no error.
- **M08 Dedicated server:** the mod loads, clean log, join check works.
- **M16 Swap seen by others (G4, R25):** a second player (with or without the mod) watches a dual wielder press H.
  Expected: they see the equip animation (the `equipping` bool is in the player's sync list, 1.7), then the draw
  animation as the weapons change hands.
- **M17 Sheathed pair on other players (G11):** two players with the mod; one sheathes a sword pair (R), then a knife
  pair. Expected: the other sees each placed as in T33 (the X on the back, the knives one per hip); with
  `CrossSheathedPair = false` on the observer's game, overlapping; the observer turns the mod off in the MC Mods panel
  (with `AllowPlayersWithoutMod = true` on the server): overlapping at once, and placed again when turned back on. The
  observer toggles `CrossSheathedPair` off and on while the other lies in a bed with a sheathed sword pair: crossed
  within a moment once that player stands up. A player without the mod sees both pairs overlapping (vanilla).

(Multiplayer ids in `TESTING.md` follow the stage 4 numbering of section 10; M16 and M17 are the same there.)

### Cross-mod

- **X01 Crossbow Stays Loaded:** load `CrossbowArbalest`, switch to a pair and back: bolt kept; the pair is put away by
  the crossbow (two-handed) and can be made again.
- **X02 Weapon Moveset:** two one-handed axes: the jump attack plays `dualaxes3` and hits main hand then off hand; the
  roll attack plays `dualaxes1`, flowing straight out of the end of the roll, and strikes with the off hand, and the
  combo continues with `dualaxes2` (main, then off);
  two knives: the `dual_knives` set, the roll attack (`dual_knives1`) strikes with the off hand; with the Moveset jump
  trigger for DualAxes set to `dualaxes2`, the next swing is the `dualaxes3` finisher (Moveset counts the pair's four
  chain levels); no errors in the log.
- **X03 Tower Shield Wall:** equip `ShieldIronTower` while paired: both weapons put away; equip a sword: the tower shield
  is put away.
- **X04 Sneak Ambush:** a sneak attack with `KnifeBlackMetal` in the main hand and `SwordIron` in the off hand: the
  backstab is the knife's (×6); after a swap, the sword's (×3). Equipping `MC_SmokeScreen` while paired puts the pair
  away; it never goes to the off hand. With Creature Morale, an afraid `Greyling` that has not seen the player still
  gives the backstab; one that watches them gives none.
- **X05 Forge Idol Upgrades:** refine the off-hand weapon: it comes back unequipped; refine the main weapon: the
  off-hand weapon becomes the main weapon.
- **X06 Creature Morale:** an off-hand hit makes an afraid creature (a `Greyling` at boss rank 3, in the Meadows) fight
  back like a main-hand hit.
- **X07 Goo's Combat Overhaul** (only if the user plays with it, without Smoothbrain DualWield): a pair swing costs
  stamina once; note whether GCO's lunge and speed tuning applies to pair swings; no errors.
- **X08 Other dual wield mods (D17, R22):** install Smoothbrain DualWield next to the mod: the BepInEx log says Dual
  Wielding was not loaded (incompatible), and a server with the mod refuses that player ("does not have the mod").
  Install RustyMods DualWielder instead: the MC Mods panel shows "Inactive: <its plugin name> also handles dual
  wielding. Remove one of them." (note the name, R17), the spawn notice lists it, and a server with the mod refuses
  that player ("has the mod turned off"). (With the fallback of 6.3: the center message at the first would-be
  pairing, and the player is not refused.)

### Live toggle, Enabled = false, clean log

- **L01 Live toggle (G10):** turn the mod off in the MC Mods panel while dual wielding: the off-hand weapon is put
  away, stance and attacks vanilla; turn it on: pairs work again. Also with the weapons hidden (R): after turning it
  off, only the main weapon stays on the back, at the game's own place, and R draws only it.
- **L02 Enabled = false + restart:** vanilla equips (a second one-handed weapon replaces the first), status "Off
  (disabled in settings)".
- **L03 Clean log:** no errors or warnings from the mod during all of the above (except the expected ones in T07 and
  T25, and the server's refusal and "allowed" warnings in M01 and M01b), including quitting the game while dual
  wielding.

---

## 9. Open questions and unverified

### Unverified names and values, and how to verify them

| # | What | How |
|---|---|---|
| R2 | Trigger-to-clip mapping (`dualaxes0-3` → "DualAxes Attack 1 / 2 2 / 3 2 / 4", `dualaxes_secondary` → "Cleave", `dual_knives0-2` → "Knife Attack Combo (1-3)", `dual_knives_secondary` → "Leap"); inferred from names and order. | `dual.attack` NOTE: clip names on both animator layers at each event. |
| R3 | The dual clips and stances played with two separate one-handed weapons (not the vanilla item): clean start from the stance, transitions to dodge and jump, long swords and maces clipping. | `dual.attack` screenshots; T08, T09, T26. |
| R4 | Which blade lands each hit event (drives the hand table of 2.6), and whether the knife finisher swings both knives. | `dual.attack` NOTE: hand joint distances to the target at each event; Later L12 if different. |
| R5 | Left-hand grip of weapons authored for the right hand. | `dual.visuals` screenshot, T26; Later L4. |
| R6 | Player prefab `VisEquipment.m_useAllTrails`; `MeleeWeaponTrail` on one-handed prefabs. | `dual.visuals` NOTE. |
| R7 | `statef` / `statei` in the Player's `ZSyncAnimation` sync lists (vanilla stances reach other games, so almost certain). | `dual.visuals` NOTE; M03. |
| R8 | Player prefab `m_attackMissAdrenaline` (C# default −5): a pair's 6 events cost more on misses than a sword's 3. | `dual.visuals` NOTE. |
| R10 | Idle, walk, run and block poses of `DualAxes` / `Knives` with separate weapons. | `dual.visuals` screenshot, T26. |
| R11 | Sheathed pair overlapping on `m_backMelee` / `m_backTool`. | Answered by the first in-world run's screenshots and the user's test: the two weapons overlap (1.7); now placed by weapon kind (G11, R26). |
| R13 | Real combo timing with chaining (clip lengths are an upper bound), hence the true damage per second. | `dual.attack` NOTE times; T08 with a stopwatch against a sword. |
| R15 | Another trigger (the main weapon's own special, `SecondaryMoves = MainWeapon`) playing from the dual stance. | T25. |
| R17 | Plugin GUIDs (and names) of RustyMods DualWielder, Ketanol DualWieldCore, balrond DualMastery, Valheim Ascended, GCO; EpicLoot's `randyknapp.mods.epicloot`. Until then the three dual wield mods are found by name (6.3). | Read each mod's `BepInPlugin` attribute (dnSpy on the downloaded DLLs, or DualWielder's GitHub source) or the BepInEx log with the mod installed; X08. |
| R18 | Left Alt and H free in popular mods. | README note; both are settings. |
| R19 | Duplicate hit events during animator transitions when chaining fast (DualWieldCore 1.2.0 fixed such a case). | `dual.attack` NOTE event count per swing. |
| R20 | The "keep equipment" world modifier path for T20 (world settings, or the `DeathKeepEquip` global key through the console; command syntax unverified). | T20. Answered in stage 4 (section 10): `setkey DeathKeepEquip`, then `removekey DeathKeepEquip`. |
| R21 | Whether the eat animation is tagged as a minor action (vanilla then already refuses attacks while eating). | `dual.keep` NOTE `InMinorAction()` during an eat. |
| R22 | Whether BepInEx 5 compares `BepInIncompatibility` against every discovered plugin or only those processed before (the soft dependency covers both). | Install Smoothbrain DualWield next to the mod: the log says Dual Wielding was not loaded. |
| R23 | `m_spawnOnTrigger` (not dumped: an object field), `m_harvest`, `m_attach`, `m_pickaxeSpecial` on every eligible weapon. | `dual.data` assertion. |
| R24 | Whether eating at a feast hides the main weapon: `DoInteractAnimation` does it only when the feast object is a `Consumable` `ItemDrop` piece, and `Feast` may point at a separate food item instead (`Feast.Start`). `FeastMeadows` is a piece in the SoftRef manifest; its components are prefab data. | `dual.data` NOTE on every `Feast` prefab; T17. |
| R25 | The swap's animations (D26): which clips the `equipping` bool and the `equip_hip` trigger play (`EquipBack`, `EquipMid`, `EquipLoop`), whether an equip's `upperbody` state and the draw's are tagged `minoraction` (walk speed, attacks refused by vanilla) or `minoraction_fast`, how the equip animation reads at 0.2 s followed by the draw, and so whether one attack press during a swap still starts a swing after it (vanilla keeps a press 0.5 s) and how late. Answered: `equipping` is in the player's `ZSyncAnimation.m_syncBools` (Tower Shield Wall's `tower.data` NOTE), so other players see the equip animation (1.7). | `dual.equip` NOTE (a vanilla hotbar equip and the swap sampled the same way; one attack press during a swap); T32, M16. |
| R26 | The sheathed pair's look (D27): the vanilla angle of each weapon kind on its joint (a sword left untilted is checked), the X on the back (its plane along the body is checked), the 2.5 cm lift, clipping into the body or the ground, meshes that measure badly (a modded weapon, a round mace head), the levelled X turning a little against a swaying back (a sideways lean of the joint is taken out every frame, 2.7; the in-world run of the crossing build found the then-crossed knife X at the hip 7.1° lopsided two frames after the crossing, before the levelling), the retry of a back pair left flat; the knives one per hip: whether the body mesh gives the hips' bind pose (checked), how far the pair turns with the pelvis against the body (answered for the idle: the pelvis 31-43° from the body's centre plane in the in-world run of 2026-09-30; the pair follows it by design, D27), each knife on its own side and outside its own thigh bone's line in the idle pose and while walking, jogging and sprinting (checked), the left knife's surface clipping into the thigh's skin in a walk or run (not measurable by the test), the face it shows; and remote players. | `dual.visuals` NOTE per pair (joints, angle, tilt, flat axes, the plane's normal relative to the player, the levelling's turns over 0.8 s; for knives the plane source, the pelvis's angle to the body's centre plane, each knife's distance from both planes and from its thigh bone's line, per pose and per gait, the thigh bones found, the mirror errors) and screenshots from behind, from the side and in each gait; T33, M17. |

Verified: every prefab name in section 8 (runtime dump; `Raspberry` in the SoftRef manifest; `MC_SmokeScreen` is
defined by the Sneak Ambush design), the dual items' values (dump), all dual trigger parameters and clip events (dump),
the per-weapon attack values of 1.4 and the creature items of 1.6 (dump), the empty equip status effect of every weapon
(dump `equipSE`), the radial menu's equip path (`ItemElement.cs` 59 → `UseItem` → `ToggleEquipped`, formerly R16),
every method and field cited in section 1 (`.ref`), including the equip queue, the hide key's trigger and the absence of
any equip speed modifier (1.8), and the back attach code (1.7); the `equipping`, `equip_hip` and `unequip_hip`
parameters and the `EquipBack`, `EquipMid`, `EquipLoop` clips (animator dump); the attach overrides of every one-handed
weapon (dump: knives `Tool`, spears `TwoHandedWeapon`, swords, axes and maces `None`) and their equip duration (0.2 s).

### Open questions for the user

1. **Hit model (D7):** the default alternates hands (one weapon per hit, both on the special), matching the vanilla
   Berserkir axes; GCO, which you named, strikes with both hands on every hit (reduced). It ships as `HitPattern =
   BothHands`. Which should be the default?
2. **Dual skill (D12):** GCO trains a dual-wield skill with the left hand; ours trains each weapon's own skill. Fine,
   or do you want a Dual Wielding skill (Later L7)?
3. **Balance default (D8):** off-hand hits at 100% (matches the vanilla Berserkir axes for a same-tier pair) or lower
   (community mods use 50-75%)? It is a server setting either way.
4. **Main-hand key (D3):** with a pair held, should Left Alt + equip end the pair (vanilla, current choice) or only
   replace the main weapon and keep the off-hand one?
5. **Keys and gamepad (D16):** Left Alt (hold, main hand) and H (swap) as defaults? Gamepad players get no main-hand key
   and no swap in v1: acceptable, or add a gamepad chord now (Later L5)?
6. **Shield or torch in the off hand (D2):** equipping a second weapon replaces the main weapon (vanilla, current
   choice) rather than pushing the shield or torch out to form a pair. Confirm.
7. **Goo's Combat Overhaul:** do you play with it? If yes, cross test X07 is added and we decide whether to coexist or
   stand down. (Running and jump attacks for pairs come from the Weapon Moveset mod.)
8. **Smoke Screen friction (L10):** with Sneak Ambush, throwing a Smoke Screen puts a knife pair away (vanilla
   one-handed rule). Keep for v1, or pull Later L10 (keep the off-hand weapon while a bomb is in the main hand) into v1?
9. **Framework hook for other dual wield mods (D17, 7.6):** answered: `ModPlugin.LocalBlocker()` is in the shared
   framework, so next to DualWielder, DualWieldCore or DualMastery the mod goes inactive with a clear status and a
   server with the mod refuses that player.
10. **Rules while joining (D25):** until the server's rules arrive (normally before you spawn), a client uses the
    built-in defaults, not its own settings and not vanilla as the other Combat mods do, so a saved pair survives the
    join. Confirm.
11. **Swap and attacks (D22, 2026-09-30):** the swap is a vanilla equip of the off-hand weapon (equip time 0.2 s, then
    vanilla's 0.3 s queue pause before the next equip). Attacks wait for it (the mod refuses them while it is queued);
    in vanilla an attack pressed during an equip may instead start and cancel the equip (depends on the animator,
    R25). A held attack button swings as soon as the swap and its draw allow; a single click may start late or be lost
    if the draw blocks attacks longer than vanilla's 0.5 s press buffer (R25). Keep "attacks wait"? A dodge, jump or
    sprint cancels the swap, as for any equip, and the key says so when pressed while sprinting.
12. **Swap animation (D26):** the game's equip animation, then the hide key's draw animation when the hands swap.
    If it reads badly, the alternative is the draw animation at the start of the swap. Which looks right in game?
13. **Sheathed pair placement (D27, D28):** answered by the user's holster rule (2026-09-30): swords, axes and maces
    crossed on the back (hilts over both shoulders, the off-hand weapon on top), two knives one on each hip (the
    off-hand one mirrored to the left hip), a knife with a back weapon each where the game hangs it; a personal setting
    turns it off. To confirm: a modded knife without the game's hip attach override hangs on the back, as the game
    hangs it alone (D27); the knives ride the pelvis (they sway with it) rather than being kept level.

Knowledge-base notes for step 5 (the 2026-09-30 feedback adds: the equip queue has no speed modifier in 1.0.16 and its
done trigger, the hide key's `equip_hip` trigger, and how `AttachBackItem` places sheathed items, 1.7 and 1.8; the
holster rule adds the joints' bones (`m_backTool` under `Hips`, right hip only; `m_backMelee` under `Spine1`), and the
feature note's "two knives at the hip" (crossed) now reads one knife per hip, a knife with a back weapon at vanilla's
spots; to add to `docs/game/combat.md` in a run that may edit it): `docs/game/combat.md` (Better tower shields note, "Equipping a `OneHandedWeapon` or
`Torch` unequips a left item that is not a `Shield`") needs a fix: equipping a one-handed weapon also keeps a left
torch, and moves a right-hand torch to an empty left hand (1.1). Add to the *Dual wielding* feature note, with a status
link to this mod: the dual clip and event table (1.5), the one-handed catalogue (1.6), the per-weapon attack values
that live on the `Attack` object rather than `SharedData` (1.4), the eat-return hole (1.2), the fact that chairs,
saddles, ship helms and the barber chair do not hide weapons (only beds, stations, the barber UI and swimming do), and
that an inventory move re-adds the item as a clone at the end of the list.

---

## 10. Implementation notes

Built in stages. **Stage 1 (foundation), done:** csproj metadata (7.7, 4.5 notes), `Plugin.cs` (every setting of
section 5, `LocalBlocker`, the Smoothbrain `BepInIncompatibility` + soft `BepInDependency`), `DualRules.cs`,
`ServerRules.cs` (with `Pick` and the pending defaults), `PlayerCheck.cs` (`PeerCompatible`, `PeerProblem`,
`PeerStateChanged`), `Patches/ZNetPatches.cs`, `ForeignMods.cs`, `Controls.cs` (key validation, `MainHandHeld` with its
Debug override, `SwapPressed`), the lifecycle part of `Hands.cs` (marker key, `ClearStaleMarkers`, `ReleaseOffHand`,
last-seen references) and `SelfTests.cs` (`dual.data`: the rules, pick, join-check and other-mods checks of 7.5).

Deviations from the text above (small, each for a reason):

- **Other dual wield mods** (6.3): `ForeignMods` looks for the name parts in the plugin's GUID as well as its name (the
  GUIDs are unverified, R17, so either may carry them), and writes one log warning per found mod and session besides
  the status line (the status alone is easy to miss at game start).
- **Keys** (section 5, D16): `ConfigEntry<KeyCode>`. Keyboard keys and the mouse buttons `Mouse0`-`Mouse4` are accepted; gamepad buttons
  are refused with a warning like unreadable keys (gamepad is Later L5; since stage 5 the warning says that gamepad
  buttons are not supported yet). The descriptions say "Keyboard or mouse only (no gamepad button yet)" instead of
  "Keyboard only".
- **Wire format** (4.2): "wrong field count" is a missing field (the read fails) or bytes left after the last field
  (refused). An unknown `SecondaryMoves` or `HitPattern` number becomes the default and counts as clamped (warning).
- **Rules objects** (4.3): `DualRules` is immutable (read-only fields set by its constructor), so the shared
  `DualRules.Defaults` can never be changed by a caller. When the server sends the same values again, `ServerRules`
  keeps the rules object it has, so nothing is revalidated or rebuilt for nothing.
- **Setting descriptions** (section 5): server rules end with "In multiplayer the setting of the server (or host) is
  used for everyone." and personal settings with "Each player's own setting.", as in Forge Idol Upgrades. The enum
  types are named `HitPatternMode` and `SecondaryMovesMode`; the values the config shows are the design's.
- **Join check log** (4.4): the "allowed" warning reads "<player> plays without Dual Wielding: their game <reason>.
  AllowPlayersWithoutMod is on, so they may play, without this mod's rules (with another dual wield mod installed they
  may still dual wield, by that mod's rules)." (wording since the review fixes; it first ended "they cannot dual
  wield"); the refusal adds "Their game shows "Incompatible version"." (as Forge Idol Upgrades).

**Stage 2 (features), done:** every goal G1-G10 and extra E1-E5 in code, with every patch of 7.2 (targets, kinds,
priorities as listed):

- `Eligibility.cs` (2.1): the pairing rules and the `ExcludedWeapons` set of prefabs (matched by drop prefab or
  `SharedData` object), rebuilt when the rules object or the `ObjectDB` changes; an unknown name is warned about once.
- `MoveTemplates.cs` (2.5): the two templates, validated against the local player's animator, with the fallback to the
  default item, the Info line naming the moves, the stamina formula of 2.6 and the template-owned field copy (`Shape`:
  read into a struct first, then one block of writes).
- `DualSwing.cs` (2.5, 2.6): the conversion in the `Attack.Start` prefix, the recorded swing, the fixed hand table
  (`PatternFor`, `HandAt`, both pure), the weapon-owned field save / swap / restore, the both-hands second call, and the
  Debug-only records (`Recording`, `Swings`, `Hits`, `Damages`) for the self-tests.
- `Hands.cs` (2.2-2.4): the pure `Route(RouteInput)` behind `Decide(player, item)`, `Apply` with its re-sync `finally`,
  the restore candidate, the main-hand intents (4 slots, 5 s) and the toggle and queue windows, `SettleLoneOffHand`,
  `Revalidate`, the swap (0.5 s cooldown, combo restart, "Main hand: <name>" message; the cooldown gave way to the
  vanilla equip queue after the user's feedback, see the last notes of this section), the eat handling, the stance,
  the first-pairing message, and a Debug hook (`TestThrowInApply`) for the forced-exception test.
- `LeftTrails.cs` (2.7) and the patch files `HumanoidPatches`, `PlayerPatches`, `AttackPatches`,
  `VisEquipmentPatches` and the Debug-only `CharacterPatches`.
- `OnActivated` / `OnDeactivated` reset every stage-2 state and cache (7.3, 7.4); `OnDeactivated` also turns off any
  off-hand trail still on. Nothing in `ObjectDB`, prefabs or `SharedData` is ever changed.
- `dual.data` gained the pure hand-table and equip-row checks; the rest of 7.5 is the next stage.

Stage 2 deviations (small, each for a reason):

- **Conversion only inside `StartAttack`** (2.5): the `Attack.Start` prefix converts a swing only while the local
  player's `Humanoid.StartAttack` runs (a flag set by our `StartAttack` prefix, cleared by its finalizer). It is the
  only vanilla caller, and it is where "special requested" is known; a mod that calls `Attack.Start` directly gets a
  vanilla swing instead of a guess.
- **Toggle window** (2.2): besides the main-hand key window, the `ToggleEquipped` prefix flags every local request
  (`InToggle`), because the first-pairing message must know "the player asked" even without the key. A finalizer on
  `ToggleEquipped` closes both windows if vanilla throws (the postfix would not run).
- **Markers on displaced weapons** (2.4, mechanism 1): row 2 (a weapon already in the off hand) and row T (torch) clear
  the marker of the off-hand weapon they put away, and row 3 sets it on the kept off-hand weapon, so the only equipped
  marked item is always `m_leftItem`. A marked item that lands in the right hand through vanilla always loses its
  marker; it becomes the restore candidate only when the right hand was empty before that equip (the `EquipItem`
  prefix records it), which every re-equip burst satisfies.
- **Revalidation** (2.4, mechanism 5): after a rules change, `SetupEquipment` runs only while a pair is still held
  (releasing the off-hand weapon already runs it through `UnequipItem`; without a pair the stance is vanilla). This
  avoids re-running the build-mode setup of a held hammer for nothing.
- **Templates** (2.5): an animator that has no parameters yet (not set up) is neither cached nor warned about; the
  templates are built at the next use.
- **Trail caches** (2.7): in their own small class, keyed by `VisEquipment` (the entry is replaced when its left
  instance changes; entries of destroyed players are pruned when the cache holds more than 32). Switching trails off
  always passes, even with `LeftHandTrails` off, so a trail can never stay on when the setting changes mid-swing.
- **Debug damage record** (7.1): the `Character.Damage` prefix lives in `Patches/CharacterPatches.cs` (house rule:
  patches in `Patches/<GameClass>Patches.cs`) instead of `SelfTests.cs`; it records only while
  `DualSwing.Recording` is on.
- **Rule snapshot per swing** (2.6): `HitPattern`, `OffHandDamage` and `BothHandsDamage` are read when the swing
  starts; a rule change in the middle of a swing applies from the next swing.
- **Player change** (7.3): a new local player (login, respawn) also resets the intents, the windows, the swap state
  (the pending swap since 2026-09-30, the cooldown before) and combo-restart flag, the pending eat return and the
  recorded swing, besides the caches.
- **Apply order** (2.2): in row 1 the right slot is written before the left one, so an item is never in two slots
  even for an instant; a missing `DLCMan` counts as a refusal (vanilla then runs and decides).

**Stage 3 (self-tests), done:** `SelfTests.cs` implements 7.5, registered in `OnActivated` and unregistered in
`OnDeactivated` (Debug builds only). Tests: `dual.data`, `dual.equip`, `dual.keep`, `dual.attack`, `dual.damage`,
`dual.fields`, `dual.visuals`. Every test forces the built-in default rules (`ServerRules.TestRules =
DualRules.Defaults`) so the player's own config never changes a result, gives its own items and takes back every item
that was not in the inventory before (drags leave clones), restores skills, food, camera distance and the in-memory
overrides in `finally`, and checks the invariant of 2.4 a frame after each step. `dual.data` also checks that every
prefab of section 8's Setup exists (`ZNetScene` and `ObjectDB`), that every trigger of the default templates plus
`eat` and `dodge` exists in the player animator, and the stance parameters `statef`, `statei` and `blocking`.
The unverified points are noted in the log: R2 (clips on both layers at each hit event), R4 (hand joint distances to
the target), R6 (`m_useAllTrails`, trail components on the left instance and on every pairing prefab), R7 (sync
lists), R8 (`m_attackMissAdrenaline`, adrenaline around a missed swing), R13 (swing and event times), R19 (events per
swing), R21 (`InMinorAction` while eating), R23, the per-weapon values of 1.4, the eat time of `Raspberry`, and the
plugin names scanned for R17. Screenshots: sword + axe and knife pair idle, the hit events of `dualaxes2` and
`dualaxes3` (R3), a sword in the left hand close up (R5), the block pose (R10), sheathed sword and knife pairs (R11).

Stage 3 deviations (small, each for a reason):

- **Seven tests instead of five** (7.5): the swings of `dual.attack` are split into `dual.attack` (A hands, wear,
  skills, stamina; D knives; F swap and Club), `dual.damage` (B off-hand and both-hands damage; E `BothHands` and
  adrenaline) and `dual.fields` (C weapon-owned fields), so each stays well under the runner's 120 s timeout.
- **Forced exception** (7.5 `dual.equip`): the test calls `Hands.Apply` directly with the Debug hook (rows 1 and 2),
  not through the `EquipItem` patch: through the patch `PatchGuard` would log an Error, which fails the in-world run,
  and it reports a site only once, which would hide a later real error of that prefix. The re-sync `finally` is what
  is tested; the prefix's `__result` after a failure is not.
- **Swap in tests** (2.3): `Hands.TrySwap` is now internal and returns whether it swapped; the tests call it directly
  (a key press cannot reach a test). The key path (`Controls.SwapPressed` in `Player.Update`) is covered by T04 only.
- **Target placement** (7.5 `dual.attack`): the Troll's body surface is placed 1 m from the player centre (its
  collider radius is read at runtime and noted) instead of its centre 1.5 m away: a Troll is wide, and too close it
  pushes the player out of reach. It is held in place every frame (position, velocity), its health topped up, its AI
  components disabled. `dual.fields` swings without a target (the fields are recorded at each hit event either way).
  The Troll goes straight ahead only when the line to it is clear, else in the first clear direction in 30° turns
  (the player then faces it): ground under it within 0.3 m of the player's feet, no terrain, rock, tree or building
  piece across the swing rays (0.5, 0.6 and 0.8 m above the feet, over the angles the Troll covers, up to its centre),
  nothing solid inside its body. The note on the target says the turn, or what blocks every direction. Why: the pair
  special (`dualaxes_secondary`, ray width 0) casts one thin ray per angle at 0.6 m, with terrain and static objects
  in its mask, and stops at the first thing it touches; the combo moves also cast at other heights against
  characters only. A Troll placed behind one of the start temple's stones took every combo hit and no special hit
  (`dual.damage` recorded no damage for the special, a failure unrelated to the mod's code).
- **Pairs in tests** are formed with the markers of earlier steps cleared first: a marked item equipped alone, then
  another in the same frame, is the same-frame restore of 2.4 (the hands swap), which is tested on purpose elsewhere.
- **Controls added to two cases**, so a pass means something: the stale marker case first shows that without
  `ClearStaleMarkers` a draw swaps the hands; the swap case first shows that without a swap a swing started right
  after another continues the combo (`dualaxes0` then `dualaxes1`).
- **Damage checks**: besides the design's damage ratios, the tests check the exact `m_damageMultiplier`,
  `m_forceMultiplier` and `m_raiseSkillAmount` the clone holds at each hit (deterministic). The ratios were first
  checked on the raw hits within 0.40-0.62 (the random skill factor at skill 100); since the in-world run of the
  holster build they are compared without the roll and the multi-object split, 0.50 within 0.01 (7.5 B, stage note
  "In-world run of the holster build"). The eat + dodge case checks "if the eat ended during the dodge, the delayed
  return was used" and notes whether the timing hit.
- **Stamina** is checked on the clone (`m_attackStamina`, the formula of 2.6); stamina spent is not measured (the test
  world has StaminaRate 0), T12 covers it in game.
- **Drags** use the real `InventoryGrid.DropItem` when the player grid shows the player inventory, else the same
  inventory moves by hand (with a NOTE); the unequip / re-equip sequence of `InventoryGui.OnSelectedItem` is
  reproduced around it.
- **Debug-only additions to the mod for the tests** (Release unchanged): the swing record keeps the clone stamina; the
  hit record keeps its swing index, spawn-on-hit prefab, special hit skill, the player's miss adrenaline, both hand
  joint positions and the animator clips per layer; the damage record keeps its swing index, trigger and event (and,
  since the in-world run of the holster build, the hit's random skill factor and the objects its sweep touched, from
  Debug postfixes on `Player.GetRandomSkillFactor` and `Attack.AddHitPoint`);
  `MoveTemplates.LastWarning`; `LeftTrails.TestLeftHandTrails` (in-memory `LeftHandTrails`, cleared in
  `OnDeactivated`) and `LeftTrails.CachedTrails`.

**Stage 4 (docs), done:** `README.md` (features, every setting of `Plugin.cs` with its default, multiplayer, good to
know, compatibility with the sibling Combat mods and the other MC mods), `CHANGELOG.md` (`## 0.1.0`) and `TESTING.md`
(section 8, 58 items). Checked while writing them:

- **Spawn names:** every name of the Setup is in the 1.0.16 runtime dump, except `Raspberry` and `BoltBone` (X01's
  bolts), both checked in the installed game's SoftRef `manifest_extended`, and `MC_SmokeScreen` (Sneak Ambush's
  `SmokeContent.ItemName`). The self-test `dual.data` checks the dump's names again at runtime.
- **R20 answered:** the keep-equipment rule is the global key `DeathKeepEquip` (`GlobalKeys`, read by
  `Player.CreateTombStone`), set with the console command `setkey DeathKeepEquip` and removed with `removekey
  DeathKeepEquip` (`Terminal`, behind `devcommands`).
- **God mode and stamina:** `Player.UseStamina` scales only by `Game.m_staminaRate`; god mode does not change stamina
  costs. So T12 asks for a world with the default stamina modifier instead of "turn god mode off" (section 8 Setup).

Stage 4 deviations from section 8 (small, each for a reason):

- **Test ids:** `Get-TestTodo.ps1` and `Package-Mod.ps1` read an id as letters then digits, so `M01b` would read as
  `M01`. The multiplayer items are numbered M01-M15: M01 is split into M01 (no mod), M02 (turned off), M03 (other
  network version) and M04 (allowed, and switching the setting back refuses the connected players); M01b is M05, M02
  is M06, M02b is M08, M03 is M09, M03b is M10, M04 is M11, M05 is M12, M06 is M13, M07 is M14, M08 is M15.
- **Added items:** M07 (rejoin a server with a saved pair: the pending policy of 4.3, D25; with the server excluding the
  off-hand weapon, only one weapon stays, which one depends on whether the rules arrived before the spawn), T31 (keys:
  another key, `None`, a gamepad button's warning) and X09 (Creature Kill and Tame Counts: a pair's kill counts as
  melee, since every pairing skill maps to melee in `Character`'s kill stats).
- **Grouping:** the live toggle, `Enabled = false` + restart and clean log items (L01-L03) close the single player
  group, as in Weapon Moveset's `TESTING.md`; the cross-mod group is named "other mods".
- **Wording:** T04 says "press H quickly several times" (the swap reads the key-down edge, so holding H swaps once);
  L01 drops a mid-swing toggle (the MC Mods panel cannot be opened during a swing). X02 uses `AxeIron` + `AxeJotunBane`
  (the poison shows the off-hand hits) instead of two plain axes.

**Stage 5 (conformance review), done:** every goal (G1-G10, E1-E5), every patch of 7.2 (target, kind, priority, one
declaration each in 1.0.16, so no overload ambiguity at patch time), every setting of section 5, the RPCs and the
marker key of 4.2, the seven self-tests and every `TESTING.md` item were checked against the code and `.ref`. The
Debug and Release builds are clean. Fixed in this stage:

- **Swap and a refused swing** (2.3): the combo-restart flag set by a swap was cleared as soon as a swing was converted,
  even when vanilla then refused it (not enough stamina). A second try within vanilla's 0.2 s chain window could then
  continue the old combo. The flag is now cleared only when a converted swing really starts (`Attack.Start` returned
  true); until then every converted swing gets `previousAttack = null`.
- **Gamepad button as a key** (section 5, D16): the warning said "the game cannot read this key", which is wrong for a
  gamepad button. It now says "gamepad buttons are not supported yet ... Pick a keyboard key or a mouse button."
  (T31 quotes the new text).
- **ExcludedWeapons wording** (section 5, 2.4 mechanism 5): the code puts the off-hand weapon away when *either* weapon
  of a held pair becomes excluded (the excluded main weapon then stays alone, which is allowed). The description said
  only "a weapon you hold in your off hand when it becomes excluded is put away"; it now reads "When a weapon of the
  pair you hold becomes excluded, your off-hand weapon is put away." (config, README). T30 gained the main-weapon case.
- **README:** the moves Info line is written when you enter a world (and when a moves setting changes), not when the
  game starts; the "+50% damage per stamina" claim is limited to the dual axe moves (a knife pair matches Skoll and
  Hati instead, 2.6); the multiplayer bullets say that a pair loses its off-hand weapon when the server excludes
  either weapon.
- **X04:** the second ambush (after the swap) needs another unaware creature: vanilla gives one backstab per creature
  every 300 s (1.4 step 7).
- A few code comments were still plain English; they are caveman now.

**First in-world run (2026-09-30, build `3cda33b+dirty`):** `dual.data`, `dual.equip`, `dual.attack`, `dual.damage`,
`dual.fields` and `dual.visuals` passed; `dual.keep` threw a `NullReferenceException` in its drag helper after 0.1 s.
Root cause and fixes:

- **Test (`dual.keep`):** the `Raspberry` came from `Inventory.AddItem(name)`, which instantiates the prefab, so the
  item carried its own `SharedData` copy. Vanilla `Humanoid.UseItem` then found no prefab for it
  (`ObjectDB.TryGetItemPrefab(item.m_shared)`), played the eat animation (hence "R21: `InMinorAction` seen = True") but
  never called `SetUseHandVisual`: no eat timer, nothing hidden, every eat wait ended at once (the whole test took 0.1
  s), and the dodge note had nothing to observe. The "full hide during the eat" step then hid both weapons with no eat
  return to draw them, so both hands were empty, the load-order step found no pair, and the first drag dereferenced the
  empty main hand. The test now eats one `Raspberry` from `Inventory.AddItem(name)` first (the common case in a real
  game: nothing hidden, same hands, NOTE), then eats `Raspberry` added through `Inventory.AddItem(GameObject, int)`
  (`ItemData.Clone` of the prefab's, so the prefab's `SharedData`), checks that each eat window really started (eat
  timer running) and skips the dependent steps with a failed check otherwise, waits for earlier minor actions (draw
  animations, the previous eat) before sampling R21, and pairs again with a NOTE when a step lost the pair, so the
  load-order and drag steps never run on empty hands.
- **Mod (`Eligibility`):** the same fact broke `ExcludedWeapons`: it compared the item's `m_shared` with the prefab's,
  which never matches an item loaded, picked up, crafted or spawned (each has its own copy), so no weapon a player held
  was ever excluded (`dual.data` only checked the prefabs' own items; `dual.keep`'s rules-change step, which uses an
  inventory `AxeIron`, never ran). Items are now matched by `m_dropPrefab` (set by `ItemDrop.Awake`, kept by
  `ItemData.Clone`) or by the prefab's `SharedData` object (2.1). `dual.data` checks both kinds of item.
- **Docs:** eating from the inventory normally hides nothing in a real game (1.2, 2.4 table, README "Good to know",
  T17); whether a feast hides the main weapon is prefab data (R24, `dual.data` NOTE).
- The warnings the run logged from this mod all came from `dual.data`'s deliberate bad values (`PairMoves =
  NoSuchItem`, `KnifePairMoves = ShieldBronzeBuckler`, `ExcludedWeapons = NoSuchSword`); nothing from this mod was
  logged at quit.

**Review fixes (2026-09-30, after an adversarial code review; every finding checked against `.ref` and the runtime
dump first).** Not run in game yet; every changed behaviour has a self-test step or a `TESTING.md` item (all `[ ]`).

- **Second eat inside the eat window** (2.4 mechanism 4, G8): the `HideHandItems` prefix kept the eat-hidden main
  weapon only for a full hide. Vanilla lets the player eat again at once, and every eat that shows the food calls
  `HideHandItems(onlyRightHand: true)`; with the main weapon already hidden and the off-hand weapon still in the left
  hand, vanilla skipped its early return and wrote the empty right hand over `m_hiddenRightItem`, so the axe moved to
  the main hand next frame and the sword stayed unequipped. The prefix now keeps it for every hide (`EatHiddenToKeep`
  returns nothing on a first eat, so that path is unchanged); the second eat's return draws the main weapon through
  rule 3. Self-test: `dual.keep` eats two foods in a row; `TESTING.md` T17 (two feasts).
- **Moves of an item that is no dual weapon** (2.6, E3, G7): the fallback for a trigger not in the hand table
  alternated within the swing, and every vanilla non-dual clip has one hit event, so with `PairMoves = SwordIron` (or
  any sword, axe, knife) the off hand never struck. Now a chain trigger alternates over the combo (chain step + event
  index; the sword combo is main, off, main) and a single move (such an item's special, a Moveset one-off outside the
  dual set) strikes with both. Still a function of the fired trigger only. Self-tests: the pure hand table in
  `dual.data`, real swings with `PairMoves = SwordIron` in `dual.attack`; `TESTING.md` T25; README and the `PairMoves`
  description say it.
- **Stance without a valid template** (2.5): the stance stayed vanilla's, taken from the off-hand weapon, while the pair
  swung with the main weapon's moves; the postfix now sets the main weapon's own stance. Only reachable when the
  default moves are unusable (a future game version, a modded animator) or before the animator is set up, so there is
  no in-game test; the warning text now says "moves and stance".
- **Snow shovel** (2.6): the second call of a both-hands event also ran vanilla's Deep North snow sweep, so one blow
  cleared twice the snow; `m_snowShovel` is saved with the weapon-owned fields and false for the second call. Noise and
  `FreezeFrame` also run twice but do not add up. Self-tests: `dual.damage` (per half), `dual.fields` (clone back).
- **Swap in the restart gap** (2.3): `TrySwap` refused only on `InAttack()`, which reads animator tags, so an H press
  right after the attack started swapped the hands under a recorded swing (its off-hand hits then fell back to the old
  main weapon, now in the left hand). It now also refuses while the current attack is starting (not done, not yet in
  its state, younger than 0.5 s; Weapon Moveset's rule). Self-test: `dual.attack` (with a control); T04.
- **Off-hand trail on swings the off hand does not strike** (2.7): for the local player, the left trail now follows
  only the recorded converted swing (not the main weapon's own special with `SecondaryMoves = MainWeapon`). Self-test:
  `dual.visuals`; T24.
- **Rules delivery** (4.2, 4.3): the server sends its rules (push and answer) only to players with the same network
  version, and a client logs "Using the server's dual wielding rules" or the "cannot read" warning only while its copy
  is active (a turned-off copy stores them silently). Before, an allowed player whose copy was inactive could log rules
  it never used, or a false "cannot read" warning after a layout change. `TESTING.md` M04 checks the M03 player's log.
- **Allowed players with another dual wield mod** (4.4, 4.5): the log line, the `AllowPlayersWithoutMod` description
  and the README no longer say such players "cannot dual wield": that mod may still give them pairs, by its rules.
- **PvP blocking of a both-hands event** (2.6, 4.5): documented, not changed: the per-block rewards count twice (README,
  M12).
- **Moves Info line** (2.5, 7.3): it was logged once per game session; it is now logged again at every world entry and
  after the mod is turned back on (T25).
- **Texts:** `HitPattern` says "then one hit from each" for the third and fourth axe swings (they are main, then off;
  "both" is kept for the specials and the knife finisher); `BothHandsDamage` says `OffHandDamage` also scales the
  off-hand share (T27 now asks for `OffHandDamage` at 100); T11 and T28 ask for `raiseskill Swords 100` first (the
  random skill factor spreads hits by more than two times at skill 0); the `ServerRules` comment no longer claims ZRpc
  has no unregister; 2.5, 2.6 and 7.1 describe the fixed hand table and the static saved-field struct; a few more code
  comments are caveman now.

**User feedback after the first in-game test (2026-09-30, build `3cda33b+dirty`).** The user wrote: "Swapping hands
should play a re-equipment animation (like when putting the weapon away / out) and have the same speed modifier." and,
about holstering, "They are holstered side by side on the back instead of being crossed like an X." Both are now part
of the design above (G4, G11, 1.7, 1.8, 2.3, 2.7, D22, D26-D28, sections 4, 5, 7, 8, R25, R26); not run in game yet,
and every changed or new `TESTING.md` item is `[ ]`. What was done:

- **Research** (`.ref` 1.0.16): `Player.QueueEquipAction` / `QueueUnequipAction` / `UpdateActionQueue`,
  `MinorActionData` (the done trigger), `m_equipDuration` (0.2 s for every one-handed weapon in the dump), the
  `Game.m_*Rate` world modifiers, `Player.InMinorAction` / `InMinorActionSlowdown` and their callers, the queue's
  cancel paths (`StartAttack`, `UpdateDodge`, `OnJump`, `CheckRun`, `RemoveEquipAction`), `Humanoid.HideHandItems` /
  `ShowHandItems` (`equip_hip`), `ZSyncAnimation` (which bools reach other games), `VisEquipment.UpdateEquipmentVisuals`
  / `SetBackEquipped` / `AttachBackItem` / `AttachItem`, and the dump's attach overrides and equip clips. Finding: no
  equip speed modifier exists in 1.0.16 (1.8), so "the same speed" is the equip time, the queue pause and the
  animator's own tags.
- **Swap** (`Hands.TrySwap`, `KeepSwap`, `CancelSwap`, `CompleteSwap`; `EquipItem` and `StartAttack` prefixes;
  `Player.Update` postfix): the 0.5 s cooldown and the minor-action refusal are gone; the swap is a queued vanilla
  equip of the off-hand weapon with `equip_hip` as its done trigger (2.3). `OnDeactivated` takes a queued swap out of
  the queue before putting the off-hand weapon away (7.4).
- **Crossed sheathed pair** (`BackCross.cs`, `VisEquipment.SetBackEquipped` postfix, `CrossSheathedPair`,
  `RebuildAll` in `OnActivated`, `OnDeactivated` and on the setting's change): 2.7. No joint of the mod's own (D27).
- **Wire:** unchanged (no rule, RPC or ZDO key added): `ModNetworkVersion` stays 1.
- **Self-tests** (names kept): `dual.equip` samples a vanilla hotbar equip and the swap, checks the queued action,
  the refused attack, the dodge and hide cancels, the timing and the queue pause (NOTE: R25); `dual.attack` waits for
  queued swaps and checks the combo-restart flag; `dual.visuals` checks and photographs four crossed pairs, the
  setting off and on, and one sheathed weapon (NOTE: the joints and what each crossing did, R26). The screenshot
  `sheathed-sword-pair` now shows the X; `sheathed-sword-pair-vanilla`, `sheathed-sword-axe` and
  `sheathed-knife-sword` are new.
- **Texts:** `SwapHandsKey`'s description, the new `CrossSheathedPair`, README, CHANGELOG (0.1.0, unreleased) and
  `TESTING.md` (T04, T26, L01 changed; T32, T33, M16, M17 new).

**Review fixes of the feedback round (2026-09-30).** Checked against `.ref` and the in-world logs; not run in game yet.

- **Swap end order** (2.3): `CompleteSwap` runs `SetupEquipment` right after the hand and marker swap, before the equip
  effect, sound and message, so a failing cosmetic step cannot leave the hands swapped in data only (`Apply` has its
  re-sync `finally` for the same reason).
- **Pair lost inside a physics step** (2.3): the `UpdateActionQueue` prefix also runs the pending-swap check, so a swap
  whose pair is gone (swimming hides the hands in `Humanoid.UpdateEquipment`, a `FixedUpdate` step) leaves the queue
  before vanilla fires its done trigger; before, only `Player.Update` checked, and with several physics steps in a frame
  the draw trigger could fire with nothing to draw.
- **Sprint** (2.3, 2.9): the key refuses to queue while `IsRunning()` and a sprint that drops a queued swap shows the
  same top-left message "Cannot swap hands while sprinting" (the queued swap is cleared by `CheckRun` every tick; the
  first version of the queued swap dropped it silently). Self-test step and T32 item added.
- **Attacks during a swap** (2.3, D22, R25): the texts no longer promise that one press starts the swing right after
  the swap (the draw may be a minor action longer than vanilla's 0.5 s press buffer); `dual.equip` NOTEs what one
  press does; T04 and T32 describe H with the attack button as the code does.
- **`equipping` synced** (1.7, 2.3, 4.1, R25, M16): answered by Tower Shield Wall's `tower.data` NOTE.
- **Crossed pair** (2.7, D27, R26): the "pose-independent" claim is corrected (a sideways lean of the joint on the
  rebuild frame is kept); a pair left flat (no plane vertical on the rebuild frame) is retried every 15 frames
  (`BackCross.Retry`); the knife + sword crossing (centre on centre) is described as it is and left to the
  screenshots. `dual.visuals` now checks that the X lies along the body and that a sword reference keeps the game's
  angle, NOTEs the plane's normal relative to the player, takes a side screenshot of each pair, and resets the record
  before the "setting on again" check.
- **Not changed:** the crossing in the joint's rest frame (needs a rest pose per joint; the doc names the limit
  instead) and the knife's crossing point next to a sword (a look question for the screenshots and the user).

**In-world run of the feedback build (2026-09-30, 66 of 71 self-tests with every MC mod).** `dual.data`,
`dual.equip`, `dual.keep`, `dual.attack`, `dual.damage` and `dual.fields` passed; `dual.visuals` failed one of 52
checks: two knives crossed at the hip measured 37.1° and 22.9° from the body's vertical two frames after the crossing.
Their sum is the 60° opening that `BackCross` built (the knife's vanilla 9.6° tilted to 30°, mirrored), so the X had
turned as a whole about its normal: the hip joint leaned 7.1° between the rebuild frame (still the armed stance) and
the check. The sword pairs stayed within 2° at that check; their screenshots 0.8 s later look a little lopsided too
(hard to judge through the perspective). Neither the construction nor the test's axis was wrong (the X is judged
against the body's vertical, which is what a player sees); the design's "sideways lean baked in" limit was. Fix (not run in
game yet):

- **Levelling** (2.7, D27, R26): `BackCross.Level`, called from the `SetBackEquipped` postfix on frames without a
  rebuild while some player has a crossed pair (`LevelPending`), turns both instances about the X's normal (carried by
  the joint), around the crossing point, so the sum of their long axes lies on the body's up projected into the plane;
  a turn under 0.25° is skipped (no write on a still body), one over 45° (bent over, head down in the water) or a plane
  near horizontal leaves the X riding the joint. `Apply` registers each crossing (joint-space normal and crossing point,
  each instance's long axis) and drops the player's old entry, and dead players' entries, at every rebuild, before the
  setting check, so turning the setting off stops the levelling; `Level` drops an entry whose instance is gone or off
  the joint; `Clear` empties the list. Hot path: three bool reads per player and frame, plus a look-up in a small list
  and a few vector operations for players with a crossed pair; no allocation.
- **Self-test** (`dual.visuals`): the symmetry check is shared (`CheckSymmetric`) and runs a second time 0.8 s after
  the crossing, right before the screenshot, once the stance change is over; a NOTE gives the total and the biggest
  single turn `Level` gave the pair meanwhile (how far the joint leaned, R26).
- **Texts:** README (the X stays level), `TESTING.md` T33 and M17 (level while walking and running and right after R,
  instead of "note whether it comes out lopsided"), the build-under-test line.
- **Sibling alignment** (their second round): Creature Morale's creatures are no longer calm but afraid (they run
  from an outclassed player and fight back when hit, near-missed or cornered; an afraid creature that sees you gives
  no sneak-attack bonus): README, X04 and X06 (a `Greyling` at boss rank 3 through its Debug `ForceBossRank`), 6.1,
  D25 and the pending policy. Weapon Moveset's roll attack flows straight out of the late roll: README, X02 and 6.1
  (the hand table is unchanged: the flow cross-fades into the same `dualaxes1` / `dual_knives1` clip). Tower Shield
  Wall's bash changes (`ShieldPunch`, cooldown, stamina, stagger lock, animation speed) do not touch pairs: a tower
  shield puts a pair away, as before.

**User's holster rule (2026-09-30).** The user wrote: "When dual wielding, where the weapon is holstered depends on the
weapon: swords, maces and axes go on the back, daggers and knives go on the side hip." Now part of the design above
(G11, 1.7, 2.0, 2.7, 2.8, D27, D28, R26, sections 4, 5, 6, 7, 8); not run in game yet, and T33 and M17 are `[ ]`.
What was done:

- **Research** (`.ref` 1.0.16): `VisEquipment.AttachBackItem` (the joint comes from the attach override, else the
  item type: `OneHandedWeapon` → `m_backMelee`, `Tool` → `m_backTool`; knives carry the `Tool` override),
  `AttachItem` (the instance's parent is the joint itself), `UpdateBaseModel` (only the body's `sharedMesh` changes
  between the male and female models); the in-world NOTE of the joints (`BackTool_attach` under `Hips`, right hip).
- **Code** (`BackCross.cs`): `Apply` reads the layout from the joints vanilla used: both on `m_backMelee` → `Cross`
  (the old crossing, minus the joint move; the reference is the main-hand weapon unless only the off-hand one is
  flat); both on `m_backTool` → `SplitHips` (the off-hand knife mirrored across `HipsPlane`, the body's centre plane in
  the hips bone's frame from the body mesh's bind pose, and re-parented to the hips bone; no levelling, no retry);
  anything else → nothing. The Debug record gives the layout (`Crossed`, `Hips`, `Apart`) and the plane source; the
  old "moved" flag is gone. Per-frame cost unchanged; a knife pair's rebuild copies two small arrays.
- **Self-test** (`dual.visuals`): the knife pair is two `KnifeBlackMetal` (exact mirror images) plus `KnifeFlint` +
  `KnifeBlackMetal` (each its own mirror); the knife + sword pair runs in both hand orders and must stay at the game's
  own spots; the setting off returns a knife pair to the game's pose; see 7.5.
- **Texts:** `CrossSheathedPair`'s description (name kept: Tower Shield Wall's design cites it; 0.1.0 unreleased),
  README (the holstering feature, config table, multiplayer, looks, holster mods), CHANGELOG (0.1.0), `TESTING.md`
  (build-under-test line, Setup: two `KnifeBlackMetal`, T33, M17).
- **Outside this run's files:** `docs/game/combat.md`'s dual wielding note still says "two knives at the hip"
  (crossed); to be updated in a run that may edit it.

**In-world run of the holster build (2026-09-30, 69 of 71 self-tests with every MC mod).** `dual.data`, `dual.equip`,
`dual.keep`, `dual.attack` and `dual.fields` passed; `dual.damage` failed 1 of 17 checks and `dual.visuals` 2 of 82.
Causes and fixes (not run in game yet):

- **`dual.damage`: `dualaxes2` off-hand / main-hand 0.33, outside 0.40-0.62** (0.51 in the run before). Not a
  regression of this round (queued swap, holster placement, levelling, hand table): the clone's multipliers were right
  at every hit (main ×1, off ×0.5; those checks passed), and the numbers fit vanilla's multi-object split exactly.
  `Attack.DoMeleeAttack` rolls a random skill factor per hit (0.85-1 at skill 100) and, with `m_multiHit` and
  `m_lowerDamagePerHit` (both on for the combo moves), divides each hit by objects × 0.75 when its sweep touched more
  than one object; the ground, a rock or a tree counts. With `SwordIron`'s 55 slash (runtime dump), the off-hand hit of
  `dualaxes2` (17.75) and both hits of `dualaxes3` (63.02 and 32.00 on the last chain step's ×2) would need rolls of
  0.65, 0.57 and 0.58 without a split, below the minimum, and 0.97, 0.86 and 0.87 with a two-object split (÷1.5); the
  main-hand hit of `dualaxes2` (54.45, roll 0.99) touched only the Troll. So three of the four sweeps also touched
  something else this time, and the check divided a split hit by an unsplit one (0.5 / 1.5 = 0.33); `dualaxes3` passed
  only because both of its hits were split. The target's clear-line search looks only toward the Troll, not along the
  whole swing arc, and the ground around the player differs between runs (this run `dual.attack` had to turn 30° to
  find a clear line). The roll alone keeps the raw ratio within 0.425-0.588, inside the old 0.40-0.62; the split does
  not, and the old check depended on both. Fix: the Debug damage record keeps each hit's roll (`Player.GetRandomSkillFactor` postfix) and the
  objects its sweep touched (`Attack.AddHitPoint` postfix, the list vanilla divides by), both Debug-only and only
  while recording; `DamageRecord.Base` is the hit without both. The ratios compare `Base`: 0.50 within 0.01, since
  only the multipliers differ, plus a check that each hit's roll was recorded within 0.85-1 and its objects counted;
  the NOTE gives each hit's raw damage, roll, objects (by name) and split. The special's `BothHandsDamage` 50 / 100
  ratios use the same comparison (the special does not split, but it rolls). The test no longer depends on where the
  player stands.
- **`dual.visuals`: two `KnifeBlackMetal` "symmetric about the body's centre plane" failed** (the off-hand knife 28.3
  cm and 13.5° from the main knife's mirror image across the body's plane two frames after the hide, 37.1 cm and
  17.9° 0.8 s later, allowed 8 cm and 20°), with the pelvis's centre plane 30.8° and 42.9° from the body's in those
  poses; every other knife check passed (mirror images across the pelvis's plane within 1 cm and 2°, one knife on each
  side of the body). The 28-37 cm match a pelvis turned 31-43° about the vertical with each knife about 27 cm out from
  its axis (2 × sin of the angle × 27 cm gives 28 and 37 cm), so the placement did what it was built to do and the
  check assumed a square pelvis. Decision
  (D27): keep the knives on the pelvis, a belt's look; the root-plane symmetry becomes a NOTE, and the checks are what
  must hold in any pose: each knife on its own side of the pelvis and of the body, outside its own thigh, in the idle
  pose and in every frame of 1.2 s of walking, jogging and sprinting in place (2.7, 7.5). The thigh bones are found by
  name under the hips ("leg"; the NOTE says which) and by side; the gaits are driven through `Player.SetControls` with
  the controller off and the player pinned to its spot, and each gait's top speed is checked so a pass means it played.
- **Texts:** 2.7 (the knives follow the pelvis, the measured angle), D27 (pelvis or body), 7.1 and 7.2 (the two Debug
  postfixes), 7.5 (B and the knife pairs), the stage 3 notes, `TESTING.md` (build-under-test line; T33: the knives
  turn with the hips, never both on one side, never between the legs).

### Deviations from the design, in short

The details are in the stage notes above; each is small and has its reason there.

- Other dual wield mods are also found by GUID, and each found one is warned about once in the log (6.3).
- Keys are `KeyCode` settings that accept keyboard keys and mouse buttons; gamepad buttons are refused with their own
  warning (section 5, D16).
- An unknown `HitPattern` or `SecondaryMoves` number from the server becomes the default and counts as clamped (4.2).
- `DualRules` is immutable, and the same values sent again keep the same rules object (4.3).
- Setting descriptions end with the "server's setting" or "own setting" sentence of Forge Idol Upgrades (section 5).
- Join check log lines follow Forge Idol Upgrades' wording (4.4).
- A swing is converted only inside the local player's `Humanoid.StartAttack` (2.5).
- `Player.ToggleEquipped` flags every local request (for the first-pairing message) and closes its windows in a
  finalizer (2.2).
- Displaced off-hand weapons lose their marker (rows 2 and T), row 3 marks the kept off-hand weapon, and a marked item
  becomes the restore candidate only when the right hand was empty before the equip (2.4).
- After a rules change, `SetupEquipment` runs only while a pair is still held (2.4, mechanism 5).
- Templates are not built (and not warned about) while the animator has no parameters yet (2.5).
- Trail caches live in their own class, keyed by `VisEquipment`; switching trails off always passes (2.7).
- The Debug damage record is in `Patches/CharacterPatches.cs` (7.1).
- `HitPattern`, `OffHandDamage` and `BothHandsDamage` are read once per swing (2.6).
- A new local player also resets intents, windows, swap state, the eat return and the recorded swing (7.3).
- Row 1 writes the right slot before the left one; a missing `DLCMan` counts as a refusal (2.2).
- Seven self-tests instead of five; the forced exception calls `Hands.Apply` directly; `Hands.TrySwap` is internal and
  returns a bool; the target Troll is placed by its collider; pairs in tests start without old markers; two control
  cases; exact multiplier checks besides the damage ratios; stamina checked on the clone; drags through the real
  `InventoryGrid.DropItem` when possible; Debug-only record fields and hooks (7.5).
- Test plan: multiplayer ids renumbered M01-M15, three items added (M07, T31, X09), and the stamina test asks for the
  default stamina world modifier instead of god mode off (section 8).
- A swap's combo restart lasts until a converted swing really starts (2.3, stage 5).
- `ExcludedWeapons` text: when either weapon of a held pair becomes excluded, the off-hand weapon is put away (section
  5 said only the off-hand case; the code always did both, stage 5).
- `ExcludedWeapons` matches an item by its drop prefab or the prefab's `SharedData` object, not by `SharedData` alone:
  items a player holds carry their own copy (2.1, first in-world run).
- A trigger outside the hand table alternates over the combo (chain step + event) or, for a single move, strikes with
  both; the design first said "alternate within the swing", which left the off hand idle with non-dual moves (2.6,
  review fixes).
- The saved weapon-owned fields live in a static struct guarded by the re-entry flag, not in Harmony's `__state`, and
  the hand table is fixed rather than built from the templates' names (2.6, 7.1, review fixes).
- A second eat inside the eat window keeps the eat-hidden main weapon like a full hide does (2.4, review fixes).
- Without a valid template a pair takes its main weapon's stance (2.5); the swap is also refused while an attack is
  starting (2.3); the local player's off-hand trail follows only converted swings (2.7); the second call of a
  both-hands event clears no snow (2.6) (review fixes).
- Rules go only to players with the same network version, and a client logs them only while its copy is active (4.2,
  4.3, review fixes).
- User feedback (2026-09-30): the swap is a queued vanilla equip with the draw trigger at its end, attacks wait for it,
  and the fixed cooldown is gone (2.3, D22, D26); a sheathed pair is crossed in an X by re-posing vanilla's back
  instances, with a personal setting (2.7, D27, D28; the old Later L3 without a joint of its own).
- Review fixes of that round: the swap key says why it does nothing while sprinting, a queued swap is also checked
  before each queue step, a pair left flat is crossed later by a retry (2.3, 2.7).
- User's holster rule (2026-09-30): only two back weapons cross (on the back); two knives hang one per hip, the
  off-hand one mirrored across the body's centre plane from the bind pose; a knife with a back weapon keeps vanilla's
  spots (2.7, D27, D28).
- In-world run of the holster build: the damage ratios are compared without vanilla's random skill factor and
  multi-object split (both recorded per hit by two Debug-only postfixes); the knife pair follows the pelvis and is
  checked against it (own side of the pelvis and of the body, outside its own thigh, idle and walking, jogging and
  sprinting), its symmetry about the body only NOTEd (7.5 B, 7.5 `dual.visuals`, D27).
