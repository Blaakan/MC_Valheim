# Existing mods — Farming / Cooking / Building / Crafting

Research done 2026-09 (2026-09-28), about three weeks after Valheim 1.0 (Deep North update) shipped on 2026-09-09. Target game build: 1.0.16 (network version 40), Unity 6000.0.75f1, BepInEx 5.4.23.5.

## How to read this report

- **Dates** come from the Thunderstore API (`https://thunderstore.io/api/experimental/package/<namespace>/<name>/`, fields `date_updated`, `is_deprecated` and the community-listing categories). Nexus pages return HTTP 403 to automated fetches, so Nexus-only mods are undated.
- **1.0 status**
  - `1.0 tagged`: the package carries Thunderstore's "Deep North Update" category. This is the author's own claim that it works on 1.0.
  - `updated post-1.0`: released after 2026-09-09 but without the tag. It probably works.
  - `pre-1.0`: last release before 2026-09-09. Assume it is broken until tested. Known 1.0 breakages include `ZRoutedRpc.Everybody` becoming a `const`. Any mod that embeds an old ServerSync or PieceManager reads it as a field and throws `MissingFieldException`. This hit Azumatt's and blaxxun's libraries and even Jotunn (issue #490). Other breakages: methods that were renamed or moved, and a new world-save layout (`.chunk` folders).
  - `deprecated`: Thunderstore's deprecated flag is set.
- **AI** means the author tagged the package "AI Generated" on Thunderstore. Treat the code quality as unknown.
- **Licenses**: we only borrow ideas. Never copy code from GPL projects such as Advize's mods, and check the license of any other project before reusing its code.
- **Vanilla hooks** list what the decompiled 1.0.16 code already provides. They are cited as `Class.Member` and were checked in `.ref/decompiled/assembly_valheim`.

## Summary

| # | Idea | Cat. | Scope | User said exists | Coverage found |
|---|------|------|-------|------------------|----------------|
| 1 | Breeding revamp (lowest parent + chance of +1) | Farming | Revamp | No | partial |
| 2 | Ashlands trees (plant trees in the Ashlands; unsheltered saplings burn, grown trees are scorched) | Farming | Revamp | No | partial |
| 3 | Forge of Potential revamp | Crafting | QoL | No | partial |
| 4 | Easy plant (cancelled) | Farming | QoL | Yes | **full** |
| 5 | Plant "everything" (cancelled) | Farming | QoL | Yes | **full** |
| 6 | Unlock biome feast in the biome, not after | Cooking | QoL | No | none (only generic trader configurators) |
| 7 | Harpoon works on tamed animals | Farming | QoL | No | **full** (released 2026-09-27) |
| 8 | One click repair all | Crafting | QoL | Yes | **full** |
| 9 | Torches ON/OFF only, no fuel | Building | QoL | Partially | **full** |
| 10 | Place sign on chest | Building | QoL | No | partial |
| 11 | Shift+E feeds 5 items to smelters/kilns | Crafting | QoL | No | **full** (as "fill all"/"add stack") |
| 12 | One trinket per trophy | Crafting | New | No | partial |
| 13 | Trophies for mobs without one | Crafting | New | No | none |
| 14 | Hunting (gatherables + animals on minimap, radius cap 100) | Farming | New | No | **full** (as radar/ESP), partial (as progression) |
| 15 | Sap collector on other trees | Farming | New | No | partial |
| 16 | Better fishing (another minigame) | Farming | New | No | partial |
| 17 | Magic applied to non-combat | Building (cross) | New | No | partial |
| 18 | Enchanting | Crafting | New | No | **full** |
| 19 | Cooking equipment | Cooking | New | No | partial |
| 20 | Bring tamed / pets on ship | Farming | New | No | partial (workarounds only) |
| 21 | Bring tamed / pets through portals | Farming | New | No | **full** |
| 22 | Ward revamp | Building | New | No | partial |
| 23 | Player trophy | Crafting | New | No | partial (one deprecated mod) |
| 24 | Barber shop to customize armor | Crafting | New | No | partial |
| 25 | Gemstone revamp (gold/valuables sink) | Crafting | New | No | partial |

What stands out:

- **Five "No" ideas already exist as maintained 1.0 mods:**
  - Harpoon on tames: TamedHarpoon, released the day before this research.
  - Tames through portals: several mods.
  - Shift+E bulk feeding: BulkSmelt, AutomaticFuel.
  - Repair-all: SmartRepair.
  - Enchanting: EpicLoot.

  We should only build these if our version is clearly different, or if we need them inside a bundle.
- **Vanilla 1.0 already has most of the plumbing for several QoL ideas:**
  - Fires: `Fireplace.m_infiniteFuel`, `Fireplace.m_canTurnOff` and the on/off state in `ZDOVars.s_state`.
  - Forge of Potential: the idol is a `Piece.Requirement.m_upgraderResource`, and `SharedData` has `m_upgradeChance`, `m_breakChance` and `m_breakReturnIngreientsAmount` (the typo is the game's).
  - Trader unlocks: `Trader.TradeItem.m_requiredGlobalKey`.
  - Plant heat: `Plant.m_tolerateHeat` combined with `ShieldGenerator.IsInsideShield`.
  - Barber: a `Barber` piece plus `PlayerCustomizaton.ShowBarberGui`.
- **Many flagship mods are deprecated or stuck before 1.0:** Jewelcrafting (deprecated 2026-05), Valheim Enchantment System, WardIsLove, BetterWards, UnderTheRadar, Tameable Collector, Player Heads. This leaves real gaps for gems, wards, player trophies and pet transport.
- **Genuine gaps (nothing found):**
  - Scorched Trees from planted trees left outside a shield in the Ashlands.
  - Feast unlock on biome discovery.
  - New trophies for trophy-less creatures.
  - Tames as real ship passengers.
  - A "Forge of Potential" variant where only the idol is lost on failure.
  - A native (non-Stardew) fishing minigame.
  - Trophy-per-trinket using the 1.0 adrenaline system.

---

## 1. Breeding revamp (Revamp) — coverage: partial

The user's rule is "offspring level = lowest of the two parents, plus a chance of +1". Vanilla gives the birthing parent's level, which in practice is a 50/50 between the parents.

**Vanilla hooks**
- `Procreation.Procreate`: the offspring gets `max(m_minOffspringLevel, <own level>)`, where the level is the one of the creature that gives birth. The partner is only counted (`SpawnSystem.GetNrOfInstances` within `m_partnerCheckRange`). No reference to it is kept, which is why the result looks like a 50/50.
- Eggs: `Procreation` sets the egg `ItemDrop` quality from the same level. When the egg hatches, `EggGrow` calls `Character.SetLevel` with the egg item's quality (`m_item.m_itemData.m_quality`), so the level travels through the item quality.
- Pregnancy is a timestamp in the mother's ZDO (`ZDOVars.s_pregnant`).

| Mod | Status | Notes |
|-----|--------|-------|
| [Procreation Plus](https://thunderstore.io/c/valheim/p/MaxFoxGaming/Procreation_Plus/) (MaxFoxGaming) | 1.2.2, 2026-09-13, 1.0 tagged | A YAML file sets a `LevelUpChance` and `MaxStars` per prefab. Defaults include boar 25% and wolf 10%, and it covers boar, wolf, lox, hen, asksvin and moose. Adds a chance of +1 on birth. The base level is still the vanilla one (the birthing parent). |
| [BreedingUpgrades](https://thunderstore.io/c/valheim/p/Dumba/BreedingUpgrades/) (Dumba) | 1.0.2, 2026-01-11, pre-1.0 | A flat 5% chance of +1 star for live births and eggs. Config is server-synced and locked. Source on [GitHub](https://github.com/DaiMinhTri/BreedingUpgrades). The smallest reference implementation. |
| [TameCraft](https://thunderstore.io/c/valheim/p/momos3939/TameCraft/) (momos3939) | 1.0.1, 2026-09-13, 1.0 tagged, AI | Animal-husbandry overhaul: affection and petting, faster conception, and tracking of mother and sire across reloads. Optional Mendelian two-parent star inheritance with mutation chances (off by default). |
| [CreatureGenetics](https://thunderstore.io/c/valheim/p/Meldurson/CreatureGenetics/) (Meldurson) | 0.0.3, 2026-09-14, 1.0 tagged | A standalone rewrite of AllTameable's DNA system. It adds random genes and inherits and mutates them over generations. Its parent, [AllTameable Taming Overhaul](https://thunderstore.io/c/valheim/p/Meldurson/AllTameableTamingOverhaul/) (1.5.4, 2026-03-14, pre-1.0, [GitHub](https://github.com/meldurson/AllTameable)), inherits levels from the parents with a ±1 mutation, and eggs carry the level. |

**Inspiration**
- **Already covered:** the "+1 chance" half, twice over (BreedingUpgrades, Procreation Plus). Two-parent inheritance exists too, but only inside large overhauls (TameCraft, AllTameable, CreatureGenetics). **Nobody implements "min(parents) + p(+1)"** as a small, vanilla-feeling rule.
- **Key technical piece: capture the partner.**
  - When pregnancy starts (the moment `ZDOVars.s_pregnant` is set), find the nearest valid partner inside `m_partnerCheckRange`.
  - Write the partner's level into the mother's ZDO.
  - At birth, compute `min(mother, partner)` and roll the +1.
  - For eggs, pass the result through `ItemDrop` quality, which is what `EggGrow` already reads.
- **Balance:** the "min" rule makes breeding upward slower. The +1 chance must compensate, for example by scaling with the Ranching-like skill, food quality or comfort.
- **Configurable:** min / avg / max / random-parent / vanilla, a per-species chance, and a cap.
- **Networking:** the result must be decided only by the ZDO owner. Show both parents' levels on hover, as TameCraft and Smoothbrain Ranching do.

## 2. Ashlands trees (Revamp) — coverage: partial

The request (sheet text of 2026-09-29):
1. Trees without a biome restriction can be planted in the Ashlands.
2. A tree sapling (the sheet's "crop") that is not inside a shield burns away and is destroyed.
3. Inside a shield, it grows by its own rules.
4. A fully grown tree outside a shield turns into a Scorched Tree after a delay.
5. A fully grown tree inside a shield stays unhurt.

Decided by the user (2026-09-30): "crop" in the sheet means the planted tree sapling. A sapling planted outside a shield cannot grow and burns. A sapling planted in a shield grows into a full tree; if the shield is then removed, the tree turns into a Scorched Tree after the delay. Vegetable crops (carrot, turnip, onion, barley and anything else that grows into a pickable) are therefore out of scope and keep their vanilla behaviour in the Ashlands.

Coverage is partial. PlantEverything comes close to the planting half through config: vanilla tree saplings can grow in any biome, so also under a shield in the Ashlands, and its defaults remove an unsheltered sapling silently once its grow time is over. No mod burns saplings on a timer, and none turns grown trees into Scorched Trees. Seasons uses the same "dies outside the shield" rule for winter cold.

**Vanilla hooks**
- `Plant.UpdateHealth` checks the biome (`WrongBiome`) before heat. It then sets `TooHot` for a plant in the Ashlands that has no `m_tolerateHeat` and is not inside `ShieldGenerator.IsInsideShield`. An unhealthy plant never grows, and `Plant.Grow` destroys it only when `m_destroyIfCantGrow` is set. Nothing burns it.
- Once `Plant.Grow` has spawned the tree (`TreeBase`), no shield rule applies to it. Outside a dome, cinder fire can burn it (`Cinder.CanBurn`). An active dome never spawns cinders inside (`CinderSpawner.SpawnCinder`) and destroys the ones that enter it (`ShieldGenerator.CheckObjectInsideShield`), which costs fuel.
- The vanilla Scorched Tree (`$prop_ashlandstree`) is the `AshlandsTree*` family: `AshlandsTree1`, `AshlandsTree3` to `AshlandsTree6` and `AshlandsTree6_big` in the 1.0.16 manifest.

| Mod | Status | Notes |
|-----|--------|-------|
| [PlantEverything](https://thunderstore.io/c/valheim/p/Advize/PlantEverything/) (Advize) — partial | 1.21.3, 2026-09-25, 1.0 tagged | Comes close to lines 1 and 3 through config, and to line 2 only as a silent removal. `EnforceBiomesVanilla` (default on) writes the temperate biomes (fir also Mountain) into the vanilla saplings' `Plant.m_biome` and `Piece.m_onlyInBiome`. Turned off, every vanilla sapling and crop can be planted and grow in any biome, the Ashlands included. `PlantsRequireShielding` (default on) keeps the vanilla heat rule, so a shield is needed there. Its saplings get `m_destroyIfCantGrow` unless `PlaceAnywhere` is on, so an unsheltered sapling is removed without any burn when its grow time ends. Adds a heat-tolerant `Ashwood_Sapling` (Beech Seeds + Sulfur) that grows `AshlandsTree3/4/5/6_big`. Nothing scorches grown trees. It rewrites these fields on the prefabs at start and on every config change, and patches `Plant.Grow`, `Plant.GetHoverText` and `TreeBase.Awake`. GPL source on [GitHub](https://github.com/AdvizeGH/Advize_ValheimMods). |
| [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) (shudnal) — same pattern for cold | 1.10.3, 2026-09-29, 1.0 tagged | Crops and pickables can perish in winter, at once or (a config option) gradually over a set number of seconds. The shield generator works as a greenhouse (against winter only, by default), and fire heat protects plants. Must be installed on the server and every client. Source on [GitHub](https://github.com/shudnal/Seasons). |
| [PlantEasily](https://thunderstore.io/c/valheim/p/Advize/PlantEasily/) (Advize) — compatibility | 2.2.2, 2026-09-24, 1.0 tagged | Grid planting. Its ghost check reads the ghost's `Plant.m_biome` and flags `TooHot` outside a shield in the Ashlands. By default (`PreventInvalidPlanting`) it refuses spots where the plant cannot grow. |
| [CropUtils](https://thunderstore.io/c/valheim/p/NoPetRides/CropUtils/) (NoPetRides) — compatibility | 2.1.0, 2026-09-18, 1.0 tagged | Pattern planting. When its planting tool is used, it refuses spots where a crop could never grow, checking the biome plus Ashlands heat and Mountain / Deep North cold "the same way Plant.UpdateHealth does" (its README, 2.0.1 notes). It reads `Plant.m_biome` from the selected piece prefab. Source on [GitHub](https://github.com/nopetrides/modding/tree/main/ValheimMods/Crop_Utils). |
| [TreesReborn](https://thunderstore.io/c/valheim/p/TastyChickenLegs/TreesReborn/) (TastyChickenLegs) — related | 1.1.3, 2026-09-11, 1.0 tagged | Replants a sapling when a stump is destroyed. According to a Nexus snippet ([Nexus 2312](https://www.nexusmods.com/valheim/mods/2312)), its config maps `AshlandsTreeStump` to PlantEverything's `Ashwood_Sapling`. If a chopped Scorched Tree leaves such a stump (unverified), our scorched trees could keep regrowing. |
| [ServersideQoL PrefabConfigurator](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_PrefabConfigurator/) (ArgusMagnus) — related | 2.0.14, 2026-09-20, updated post-1.0, server-only | Generic prefab-field editor (plant grow time, space and more). It shows how far pure data tweaks go; it has no heat or scorch logic. |

**Inspiration**
- **Covered only through config:** planting trees in the Ashlands and growing them under a shield (PlantEverything with `EnforceBiomesVanilla` off). That setting opens every biome at once, crops included. Ours adds only the Ashlands, only to a fixed list of trees without a special biome, leaves vegetable crops alone, and keeps the shield requirement.
- **The gap:**
  - a visible, timed burn of unsheltered saplings (vanilla stalls them; PlantEverything removes them silently at the end of their grow time);
  - grown trees turning into Scorched Trees outside a shield, which no mod does.
- **Borrow from Seasons:**
  - a gradual death over a configurable time rather than an instant one;
  - the shield as the greenhouse;
  - the rule that the mod runs on the server and on every client, with the server's settings.
- **Compatibility:**
  - Re-apply our Ashlands bit to the selected prefab before the ghost is made and to instances as they load, because PlantEverything rewrites the sapling prefabs when it starts and whenever its config changes.
  - Skip heat-tolerant saplings (such as its `Ashwood_Sapling`), and respect its `PlantsRequireShielding` off.
  - PlantEasily (ghost) and CropUtils (prefab) already refuse unsheltered Ashlands spots, which fits the burn rule.
- **Balance:** Scorched Trees drop Ashwood, so this makes Ashwood renewable, as PlantEverything's Ashwood sapling and TreesReborn's stump replanting already do. Decide whether the scorched result keeps the vanilla drops.

## 3. Forge of Potential revamp (QoL) — coverage: partial

Desired behavior:
- On failure, only the idol is consumed and the item is never destroyed.
- Higher-tier idols are required at higher levels.
- Optionally, a skill level is required.

**Vanilla hooks** (all in `InventoryGui.DoCrafting`)
- The upgrade path looks for the recipe requirement flagged `Piece.Requirement.m_upgraderResource`, which is the idol.
- It rolls against the idol's `SharedData.m_upgradeChance` (the field defaults to 0.65) and then against `m_breakChance`.
  - Success: the item gains a level.
  - Break: the item is destroyed. Recoverable materials are refunded by `m_breakReturnIngreientsAmount`.
  - Otherwise: the item loses one level.
- All resources, including the idol, are consumed in every case (`Player.ConsumeResources`).
- `CraftingStation.m_craftItemDoneFailEffects` plays on failure.

| Mod | Status | Notes |
|-----|--------|-------|
| [ReforgedPotential](https://thunderstore.io/c/valheim/p/Akuichi/ReforgedPotential/) (Akuichi) | 2.0.6, 2026-09-27, 1.0 tagged | Success is 100% by default (configurable). On failure the item either breaks or degrades by 1 (Break Chance setting). Idols are craftable, at the Artisan table by default. Cost scales with level. Two optional progressions: boss progression, and **idol progression, where the required idol changes at level thresholds**, which matches half of our idea. No mention of skill gating or idol-only loss. |
| [OdinBet ForgeOfPotential](https://thunderstore.io/c/valheim/p/Igao/OdinBet_ForgeOfPotential/) (Igao) | 1.4.2, 2026-09-28, updated post-1.0 | "Gamble" framing: 100% success by default, and three selectable failure modes (lose levels, destroyed with partial refund, reset to level 1 with no material loss). All 16 idols become craftable with boss trophies, costs scale per level, and crafting takes time. Adds an in-game settings panel (Shift+E), a Hall of Fame, "Thunder Night" elemental infusions and a version lock. Heavy on features. |

**Inspiration**
- Both mods mostly **remove the gamble** (100% by default) or re-tune it. Neither implements the rule "failure = the idol is lost, the item and the other materials are kept", which keeps tension without the feel-bad.
- **Our design:**
  - Success works as in vanilla.
  - On failure, consume only the idol, refund the other materials, and keep the item at its level.
  - Show the odds in the tooltip.
  - Require an idol tier by target level (ReforgedPotential proves players like this).
  - Optionally gate on `Skills.SkillType.Crafting` and let skill nudge the chance.
- **Patch point:** a narrow transpiler or prefix around the upgrader branch of `InventoryGui.DoCrafting`. Consumption happens in the same method, so this needs care.
- **Compatibility:** declare the two mods above incompatible, or detect them.
- **Synergy:** gems (#25) could act as catalysts.

## 4. Easy plant (QoL) — coverage: full

Cancelled in the idea sheet.

| Mod | Status | Notes |
|-----|--------|-------|
| [PlantEasily](https://thunderstore.io/c/valheim/p/Advize/PlantEasily/) (Advize) | 2.2.2, 2026-09-24, 1.0 tagged, client-side | Grid planting (rows × columns, resizable with keybinds), snapping, random rotation, and red highlighting that blocks invalid spots. Bulk harvest and optional auto-replant. Gamepad support. Source: [AdvizeGH/Advize_ValheimMods](https://github.com/AdvizeGH/Advize_ValheimMods) (GPL, so ideas only). The reference implementation. |
| [ImpactfulSkills](https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/) (MidnightMods) | 0.20.2, 2026-09-28, 1.0 tagged | Skill-driven perks, including multi-planting unlocked by the farming skill and cheaper cultivator stamina. |
| [Farming](https://thunderstore.io/c/valheim/p/Smoothbrain/Farming/) (Smoothbrain / blaxxun) | 2.2.2, 2026-02-05, pre-1.0 | A farming skill: faster growth and higher yield. Uses blaxxun's ServerSync, so it is at risk on 1.0 until updated. |

**Inspiration:** do not rebuild this. PlantEasily is mature, maintained and client-side. At most, make our farming features (#2, #15, any new crops) behave correctly with its grid ghosting, which in practice means vanilla `Piece` and `Plant` components on the Cultivator piece table. A skill- or tool-tier-gated grid size (like ImpactfulSkills) is the only differentiator left.

## 5. Plant "everything" (QoL) — coverage: full

Cancelled in the idea sheet.

| Mod | Status | Notes |
|-----|--------|-------|
| [PlantEverything](https://thunderstore.io/c/valheim/p/Advize/PlantEverything/) (Advize) | 1.21.3, 2026-09-25, 1.0 tagged | The Cultivator can plant berry bushes, thistle, dandelion, mushrooms, missing sapling types and decorative flora. Config covers grow time, yields and biome enforcement. [Nexus](https://www.nexusmods.com/valheim/mods/1042). |
| [PlantEverything (unofficial rebuild)](https://thunderstore.io/c/valheim/p/fedorovdgap/PlantEverything/) (fedorovdgap) | interim 1.0 rebuild | Fixed the 1.0 `ZRoutedRpc.Everybody` crash before Advize shipped. Now superseded. Worth knowing as a pattern for how the community reacts to breakage. |
| [ServersideQoL PrefabConfigurator](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_PrefabConfigurator/) | 2.0.14, 2026-09-20, server-only | Plant grow time, space and similar values, set by server config. |

**Inspiration:** covered. Any new plantables we add (for example Ashlands or Deep North flora, or tappable trees from #15) should register as ordinary Cultivator pieces, so that PlantEverything and PlantEasily users get them for free.

## 6. Unlock the biome feast in the biome, not after (QoL) — coverage: none

**Vanilla hooks**
- A feast recipe becomes known once every ingredient is a known material. See `Player.UpdateKnownRecipesList` and the `RequirementMode.IsKnown` checks over `Player.m_knownMaterial`.
- The gating ingredient is the biome spice sold by the Bog Witch. `Trader.GetAvailableItems` filters `Trader.TradeItem.m_requiredGlobalKey`, which is a boss-defeated world key. For example, Mountain Peak Pepper Powder appears after Moder, and the Deep North spice after the final boss.
- Player-side biome discovery exists: `Player.IsBiomeKnown(BiomeSector)` backed by `m_knownBiome`. 1.0 introduced the `BiomeSector` type.

| Mod | Status | Notes |
|-----|--------|-------|
| [TradersExtended](https://thunderstore.io/c/valheim/p/shudnal/TradersExtended/) (shudnal) | 2.0.4, 2026-09-24, 1.0 tagged | Rewrites trader buy and sell lists through synced JSON or YAML, with optional global-key, player-key and discovery requirements. It can approximate the idea by config: re-key each spice. [GitHub](https://github.com/shudnal/TradersExtended). |
| — | — | No dedicated mod found. |

**Inspiration**
- A small, focused patch on `Trader.GetAvailableItems`: for each spice mapped to a biome, replace the world boss key with "the local player has discovered biome X", or with an alternative such as having a trophy of that biome's elite.
- This is per-player, so in multiplayer each Viking unlocks feasts through their own exploration.
- Keep the spice-to-biome map in config.
- **Compatibility:** TradersExtended also rewrites trader lists. Run after it, or expose the mapping as TradersExtended config.

## 7. Harpoon works on tamed animals (QoL) — coverage: full

**Vanilla hooks**
- `SE_Harpooned` is the status effect that does the pulling.
- Friendly and tamed targets are filtered in the hit logic (`Attack` / `Projectile` checks on `Character.IsTamed()`). `SharedData.m_tamedOnly` is the inverse flag, used by weapons that only hit tames.

| Mod | Status | Notes |
|-----|--------|-------|
| [TamedHarpoon](https://thunderstore.io/c/valheim/p/Brainless_Azura/TamedHarpoon/) (Brainless_Azura) | 1.0.0, 2026-09-27, 1.0 tagged, AI | The Abyssal Harpoon hooks tames like wild creatures, with no damage and no knockback. The vanilla pull does the rest. Only the thrower needs the mod. |
| [CreatureCarry](https://thunderstore.io/c/valheim/p/Wendigo/CreatureCarry/) (Wendigo) — related | 1.3.0, 2026-03-21, pre-1.0, AI | Pick up and carry tamed creatures (also through portals), protect tames from enemies, and remove the fire requirement for eggs. |

**Inspiration:** covered as of yesterday, by an AI-tagged 1.0.0. If we want this inside a "Ranching QoL" bundle, it is a tiny patch. Possible improvements:
- restrict to tames you own, or tames inside your ward;
- a "leash" mode (soft pull with no line break and no stamina drain);
- a "lead to pen" follow command.

Otherwise, recommend TamedHarpoon.

## 8. One click repair all (QoL) — coverage: full

**Vanilla hooks**
- `InventoryGui.OnRepairPressed` → `RepairOneItem` repairs exactly one worn item per click. It iterates `Inventory.GetWornItems` and filters with `CanRepair` (recipe station type and station level) and `CraftingStation.CheckUsable`.
- `HaveRepairableItems` drives the glowing button.

| Mod | Status | Notes |
|-----|--------|-------|
| [SmartRepair](https://thunderstore.io/c/valheim/p/LJIndustries/SmartRepair/) (LJIndustries / 3WiseStooges) | 1.0.1, 2026-09-16, 1.0 tagged | Repairs everything when a station opens. Later stations can repair gear from earlier ones (for example, a black forge repairs forge items), with a `RequireStationLevel` toggle. Crafting skill still rises. Client-side. [GitHub](https://github.com/3WiseStooges/SmartRepair). |
| [RhythmicRepairs](https://thunderstore.io/c/valheim/p/hoskope/RhythmicRepairs/) (hoskope) | 1.0.0, 2026-06-15, pre-1.0 | One click repairs everything, paced one item at a time to keep the vanilla repair "rhythm" (sound and effect per item). Optional auto-repair on open. [GitHub](https://github.com/hoskonen/valheim-rhythmic-repairs). A nice UX touch. |
| [AzuWorkbench Inventory Repair](https://thunderstore.io/c/valheim/p/Azumatt/AzuWorkbench_Inventory_Repair/) (Azumatt) | 1.0.2, 2024-11-05, pre-1.0 | Auto-repairs on interact. Azumatt's embedded ServerSync is exposed to the 1.0 `MissingFieldException`. |
| [RepairAll](https://thunderstore.io/c/valheim/p/LoadedGun/RepairAll/) (LoadedGun) | 1.1.1, 2023-03-01, pre-1.0 | The original "press once" mod. |

**Inspiration:** trivial to do ourselves (loop the `RepairOneItem` logic). Do it only as part of a Crafting QoL bundle. Worth borrowing:
- RhythmicRepairs' paced effects;
- one summary message ("Repaired 7 items") instead of 7 center-screen messages;
- Shift+click for the old one-at-a-time behavior;
- keep the skill gain.

Station-tier crossover (SmartRepair) is a design choice, not QoL. Leave it off by default.

## 9. Torches ON/OFF only, no fuel (QoL) — coverage: full

**Vanilla hooks** (on `Fireplace`)
- `m_infiniteFuel`, `m_canTurnOff` and `m_canRefill` already exist.
- The on/off state is stored in `ZDOVars.s_state` (1 = on). `Fireplace.Interact` toggles it when `m_canTurnOff` is set.
- So the feature is mostly **data on prefabs** at `ZNetScene` / `ObjectDB` load, plus a small UI or hover tweak.

| Mod | Status | Notes |
|-----|--------|-------|
| [ValheimInfiniteFire](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimInfiniteFire/) (MidnightMods) | 1.4.0, 2026-09-21, 1.0 tagged | Per-category infinite fire (fire pits, bonfires, hearths, torches, braziers, lanterns, candles). Placed fires update live when config changes. |
| [HexInfiniteFuel](https://thunderstore.io/c/valheim/p/Hex_Viking/HexInfiniteFuel/) (Hex_Viking) | 1.0.4, 2026-09-09, 1.0 tagged, AI | Client-side: every `Fireplace` is permanently lit. |
| [TimedTorchesStayLit](https://thunderstore.io/c/valheim/p/TastyChickenLegs/TimedTorchesStayLit/) (TastyChickenLegs) | 1.4.0, 2026-09-13, 1.0 tagged | Torches follow an on/off schedule (default 16:30–06:30) and optionally need no fuel. |
| [ServersideQoL PrefabConfigurator](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_PrefabConfigurator/) (ArgusMagnus) | 2.0.14, 2026-09-20, server-only | "Make fireplaces/light sources toggleable or have infinite fuel", set from the server for vanilla or console clients. |
| [FireplaceUtilities](https://thunderstore.io/c/valheim/p/Smallo/FireplaceUtilities/) (Smallo) | 2.1.0, 2021-03-25, pre-1.0 | Historic: extinguish and ignite without losing fuel. [GitHub](https://github.com/smallo92/FireplaceUtilities). |

**Inspiration**
- Covered. Our angle would be the exact "light switch" UX:
  - lighting a torch once "installs" it (one fuel or a craft cost);
  - after that, E toggles it and no fuel is needed;
  - cooking fires, bonfires and hearths optionally keep fuel, so wood still matters.
- Implement it as prefab flags plus a hover text ("[E] Turn off").
- **Bonus:** a "light group" toggle, where a wall switch or a ward toggles every light in its radius (Building synergy with #22).

## 10. Place sign on chest (QoL) — coverage: partial

The user's example: "placing sign more easily like on side of chests".

| Mod | Status | Notes |
|-----|--------|-------|
| [Chest Labels](https://www.nexusmods.com/valheim/mods/3629) (Nexus) | undated (Nexus blocked), single-player only | Ctrl+E on a chest opens the vanilla sign text panel. The label renders as 3D text on the front, left, right and back, auto-sized per chest type, and replaces "Chest" in the hover. Stored in the world save, and removing the mod is harmless. |
| [RunicStorage](https://thunderstore.io/c/valheim/p/Chazman/RunicStorage/) (Chazman) | 1.3.12, 2026-09-28, updated post-1.0, AI | Colored labels on chest faces and lids (white or black backing), plus a whole storage suite (remembered contents, routed quick-stack, search, sort). Multiplayer needs the server and clients updated together. |
| [ServersideQoL ContainerSigns](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_ContainerSigns/) (ArgusMagnus) | 2.1.0, 2026-09-25, server-only | The **server automatically attaches vanilla signs to chests and barrels**, optionally with an automatic content list. The signs double as configuration for the other SQoL modules (auto-store range, auto-feed range). Works with vanilla and console clients. |
| [ZenSign](https://thunderstore.io/c/valheim/p/ZenDragon/ZenSign/) (ZenDragon) | 1.9.1, 2026-09-23, 1.0 tagged | Assign an item to a vanilla sign, and it shows the item icon plus the total count in nearby chests. Tap it to highlight the chests. |
| [CustomChestName](https://thunderstore.io/c/valheim/p/GemHunter1/CustomChestName/) (GemHunter1) | 1.1.2, 2026-09-09, 1.0 tagged | Rename chests (hover text only). |

**Inspiration**
- Labelling is well served. The literal **building QoL** is not: letting the player place an ordinary vanilla sign flush on a chest or barrel face with snapping.
- **Ours:** a placement tweak. When the sign ghost's ray hits a `Container` collider, snap the sign to that face's normal and center, with a small offset, and allow placement there.
  - No new data: the sign stays an independent piece, so multiplayer and uninstall are safe.
  - Optional nice-to-have: remove the sign when its chest is destroyed.
  - Add Chest Labels' auto-sizing text and ZenSign's item icon as a follow-up (the "item frame on chest" look).
- Test round barrels and the different chest sizes. Also check the vanilla sign's `Piece` placement rules (it currently snaps to build pieces, not containers).

## 11. Shift+E feeds 5 items to furnaces and kilns (QoL) — coverage: full (different batch semantics)

**Vanilla hooks**
- `Smelter.OnAddOre` / `Smelter.OnAddFuel` add exactly one item per call and send one RPC to the owner.
- `Switch.m_holdRepeatInterval` already gives "hold E to repeat" on some switches.
- Capacity is enforced by the smelter's max ore and max fuel values.

| Mod | Status | Notes |
|-----|--------|-------|
| [BulkSmelt](https://thunderstore.io/c/valheim/p/ChillCodeClub/BulkSmelt/) (ChillCodeClub) | 3.0.4, 2026-09-12, updated post-1.0, AI | Shift+E fills a station to capacity from the inventory: kiln wood; smelter and blast furnace coal and ores; windmill; spinning wheel. |
| [ComfyAddAllFuel](https://thunderstore.io/c/valheim/p/ComfyMods/ComfyAddAllFuel/) (ComfyMods) | 1.10.0, 2024-11-12, pre-1.0 | Shift adds up to one stack or up to the limit. It patches the interaction entry point once for every device: smelters, kilns, windmills, spinning wheels, fireplaces, cooking stations, torches and shield generators. [GitHub](https://github.com/BruceOfTheBow/BruceComfyMods/tree/main/ComfyAddAllFuel). Clean code worth reading. |
| [AutomaticFuel](https://thunderstore.io/c/valheim/p/TastyChickenLegs/AutomaticFuel/) (TastyChickenLegs) | 1.5.2, 2026-09-27, 1.0 tagged | Automation: stations pull fuel and ore from nearby containers. Also stacks smelters and blast furnaces. |
| [Add All Fuel And Ore](https://thunderstore.io/c/valheim/p/rin_jugatla/Add_All_Fuel_And_Ore_For_Smelter_Charcoalkiln_etc/) (rin_jugatla) | 1.6.2, 2021-03-07, pre-1.0 | The original. It can also pull from containers when the inventory is empty. |
| ServersideQoL AutoProcess (ArgusMagnus) | server-only | Feeds stations (including shield generators) from nearby containers. |

**Inspiration**
- The user's version is deliberately **smaller than "fill all"**: +5 (configurable N) per Shift+E. It keeps a little interaction and loses none of the pacing.
- **Implementation:**
  - Call the vanilla per-item path N times, clamped to capacity and inventory count.
  - Each call sends vanilla's own RPC, so it works on vanilla servers with no server-side component.
  - Cover smelter, kiln, blast furnace, eitr refinery, windmill, spinning wheel and shield generator.
  - Optionally cover fireplaces as well (ComfyAddAllFuel's unified patch is the model).
- Show "+5 Copper ore (12/20)" feedback.

## 12. One trinket per trophy (New) — coverage: partial

**Vanilla hooks**
- Trinkets are `ItemDrop.ItemData.ItemType.Trinket` (value 24), with `SharedData.m_maxAdrenaline` and `SharedData.m_fullAdrenalineSE`. The status effect fires when the adrenaline bar is full.
- Vanilla recipes (Forge or Black Forge) already consume trophies, but only for a handful of trinkets.

| Mod | Status | Notes |
|-----|--------|-------|
| [ClassTrinkets](https://thunderstore.io/c/valheim/p/JamesJonesTV/ClassTrinkets/) (JamesJonesTV) | 1.0.2, 2026-02-12, pre-1.0 | 40 trinkets (8 classes × 5 ranks) with static stats. Rank 1 needs a class-specific trophy, and each rank consumes the previous one. Uses ItemManager. |
| [Glorious Trinkets](https://thunderstore.io/c/valheim/p/Catharis/Glorious_Trinkets/) (Catharis) | 2.0.0, 2026-05-15, **deprecated** | New trinkets, capes and utility items with equip and risk/reward buffs. |
| [BetterTrinkets](https://thunderstore.io/c/valheim/p/Schwifty/BetterTrinkets/) (Schwifty) | 1.0.0, 2026-02-28, pre-1.0 | Makes vanilla trinket effects passive, and a full adrenaline bar doubles them. A rebalance, not new trinkets. |
| [TrophyBuffs](https://thunderstore.io/c/valheim/p/blacks7ar/TrophyBuffs/) (blacks7ar) | 1.2.9, 2025-09-12, pre-1.0 | 72 craftable glowing trophies that can be eaten for buffs. Related: [UsefulTrophies](https://thunderstore.io/c/valheim/p/probablykory/UsefulTrophies/), where consuming a trophy gives skill XP or a boss power. |

**Inspiration**
- **The gap:** every vanilla trophy mapped to a themed trinket that uses the 1.0 adrenaline system. For example:
  - Neck: swim speed or water stamina;
  - Deer: sprint burst;
  - Troll: stagger resistance;
  - Seeker: a vision pulse.
- **Build it data-driven:** one table row = trophy → trinket (tier metal, `m_maxAdrenaline`, a status-effect definition).
- **Assets:** clone a vanilla trinket prefab, and render the trophy mesh as a pendant with a runtime-generated icon (as Jotunn's RenderManager does).
- **Recipe unlock:** the recipe appears when the trophy is known (the vanilla known-material rule).
- **Guardrails:** balance against vanilla trinkets, and give only one active adrenaline trinket, as vanilla does.
- **Synergy:** #13 (new trophies feed new trinkets), #23 (player trophy) and #25 (gem sockets).

## 13. Trophy on mobs without trophy (New) — coverage: none

Creatures without a trophy in vanilla, per the [wiki](https://valheim.weirdgloop.org/w/Trophies): Greyling, Gull, Crow, Root, Fish, Leviathan, Oozer, Bat, Chicken/Hen, Seeker Brood, Ash Crow, Charred Twitcher and Skugg. Loot comes from the `CharacterDrop` component on each creature prefab.

| Mod | Status | Notes |
|-----|--------|-------|
| [Drop That](https://thunderstore.io/c/valheim/p/ASharpPen/Drop_That/) (ASharpPen) | 3.1.6, 2026-09-23, 1.0 tagged | A loot-table configurator. It can add *existing* items as drops but cannot create new trophy items. [GitHub](https://github.com/ASharpPen/Valheim.DropThat). If we add drops, we must coexist with it because it rewrites `CharacterDrop`. |
| [CreatureLevelAndLootControl](https://thunderstore.io/c/valheim/p/Smoothbrain/CreatureLevelAndLootControl/) (Smoothbrain) | 4.6.4, 2025-05-26, pre-1.0 | Level and loot control, including multiple boss trophies. No new trophies. |
| [TrophyBuffs](https://thunderstore.io/c/valheim/p/blacks7ar/TrophyBuffs/) (blacks7ar) | 1.2.9, 2025-09-12, pre-1.0 | Adds craftable "glowing trophies", not creature drops. |

**Inspiration**
- A clear gap.
- **Build:**
  - Clone a vanilla trophy prefab for each trophy-less creature and swap in a head mesh taken from the creature's own renderer (or a simple model).
  - Generate the icon at runtime.
  - Inject a `CharacterDrop` entry with a low rate.
  - Register them as trophies, so `Player.AddTrophy` and the trophy wall work.
- **Give them a use:** trinkets (#12), Item Stand decoration, and "Trophy Hunt" completion.
- **Watch:**
  - Coexist with Drop That: add our drops after its reload, or ship a Drop That config.
  - Fish are items, not characters, so they need a separate path.
  - Leviathan is a location, not a creature drop.

## 14. Hunting (New) — coverage: full as a radar, partial as progression

The request: show gatherables and animals nearby on the minimap, capped to the discovery radius of 100.

**Vanilla hooks**
- `Minimap.m_exploreRadius` defaults to 100.
- Pins are added with `Minimap.AddPin(pos, type, name, save, isChecked, ...)` and removed with `RemovePin`.

| Mod | Status | Notes |
|-----|--------|-------|
| [OneMapToRuleThemAll](https://thunderstore.io/c/valheim/p/DrummerCraig/OneMapToRuleThemAll/) (DrummerCraig) | 2.8.1, 2026-09-12, 1.0 tagged, AI | Shared exploration and pins, auto-pins, and a **client radar**: nearby objects and creatures appear as temporary pins that fade, and creatures are tracked live. |
| [Hunting](https://thunderstore.io/c/valheim/p/blacks7ar/Hunting/) (blacks7ar) | 1.4.5, 2026-09-14, 1.0 tagged | A Hunting skill (XP from bow, knife and spear) with a **configurable auto-track radius for prey** (boar, deer, wolf, lox, hare, neck, chicken, bear). Yield scales with skill and prey level. The closest to our "progression" framing. |
| [CreatureTrackerMinimap](https://thunderstore.io/c/valheim/p/ChrisBomarGamingMods/CreatureTrackerMinimap/) (Fusionette) | 0.9.10, 2025-09-20, pre-1.0 | Zero-config. Every creature is shown **using its trophy sprite as the pin icon**, with a drop-icon fallback. Tame names are shown. [GitHub](https://github.com/Fusionette/CreatureTrackerMinimap). |
| [UnderTheRadar](https://thunderstore.io/c/valheim/p/Kits_Bitz/UnderTheRadar/) (Kits_Bitz) | 3.1.7, 2025-03-07, **deprecated** | Formerly the reference: live pins for pickables, ores, critters and locations within a radius. |

Also relevant:
- [Valheim Tracking](https://www.nexusmods.com/valheim/mods/3457) (Nexus, undated): footprint trails while crouching, with a radius scaled by Sneak, plus lure whistles and tracking potions.
- [AutoMapPins](https://thunderstore.io/c/valheim/p/Kempeth/AutoMapPins/) (Kempeth, 1.3.0, 2023, pre-1.0): groups nearby objects into single temporary pins.

**Inspiration**
- The radar/ESP exists. What is missing is **radar as earned hunter craft**:
  - the radius grows with a Hunting skill (or with trinkets, #12) up to the explore radius of 100;
  - it is active only while crouched, or with a hunting horn or tracking potion;
  - animals and pickables are separate toggles;
  - pins fade.
- Borrow the trophy icons from CreatureTrackerMinimap and the fading temporary pins from OneMapToRuleThemAll.
- **Performance:**
  - throttle scans (about 1 Hz) using `Character.GetAllCharacters` and a cached list of `Pickable` objects;
  - use `save: false` pins;
  - make no network calls (client-only).
- Blend with Exploration's Spyglass and Compendium ideas.

## 15. Sap collector on other trees (New) — coverage: partial

The goal is a new cooking or crafting item.

**Vanilla hooks**
- `SapCollector` looks for a `ResourceRoot` via `GetComponentInParent` on nearby colliders. The only roots are the Mistlands Ancient Roots.
- `ResourceRoot` stores its level and regeneration in the ZDO (`ZDOVars.s_level`), with `m_maxLevel` and `CanDrain`.
- `SapCollector` has `m_secPerUnit` and `m_maxLevel`.

| Mod | Status | Notes |
|-----|--------|-------|
| [ResinCollector](https://thunderstore.io/c/valheim/p/blacks7ar/ResinCollector/) (blacks7ar) | 1.1.7, 2026-09-15, 1.0 tagged | A new build piece that works only when placed on a tree (Meadows, Black Forest, Plains) and collects **Resin**. Duration, capacity and recipe are configurable and server-synced. Proves the feature is feasible. |
| [BetterSapCollector](https://thunderstore.io/c/valheim/p/xtavim/BetterSapCollector/) (xtavim) | 1.1.0, 2026-09-09, 1.0 tagged | Tunes the vanilla sap collector and ancient-root speeds, capacities and hover text. [GitHub](https://github.com/xtavim/BetterSapCollector). |

**Inspiration**
- **Ours:** new outputs per tree species that feed Cooking and Crafting. For example:
  - Birch → birch syrup (a sweetener for new recipes);
  - Pine / Fir → pitch (a tar alternative);
  - Oak → tannin (leather work);
  - Yggdrasil and Ashwood variants → magic reagents (#17).
- **Implementation:**
  - Attach a `ResourceRoot`-like component to tree prefabs (`TreeBase`), created lazily, or write a "Tap" piece that finds a `TreeBase` in range.
  - Give output and regeneration per species.
  - Store state in the ZDO.
- Make the tap "drain" the tree (for example, a tapped tree yields less wood, or the tap moves on), so it is not free.
- This is a cross-category theme: Farming (gather) → Cooking (syrup dishes) → Crafting (pitch, tannin).

## 16. Better fishing (New) — coverage: partial

The user's verdict: "It sucks, pick another minigame."

**Vanilla hooks:** `FishingFloat` (bite detection, reeling and line state) and `Fish` (bait, hook and escape behavior).

| Mod | Status | Notes |
|-----|--------|-------|
| [PeasFishing](https://thunderstore.io/c/valheim/p/Laki/PeasFishing/) (Laki) | 0.6.2, 2026-09-12, 1.0 tagged, AI | The only minigame replacement found. **Stardew-style vertical catch bar**: Shift hooks the fish, then hold or release Shift to steer the bar. Adds a nearby-fish counter, a bait-interest indicator (the author notes it shows proximity, not the real AI decision), a custom HUD and a world clock. Client-only. |
| [Reely SpecTackleLure](https://thunderstore.io/c/valheim/p/Neobotics/Reely_SpecTackleLure/) (Neobotics) | 1.0.0, 2026-09-11, 1.0 tagged | Gear rather than a minigame: craftable and upgradable rods (which need repair), craftable bait, a larger float, field skinning without a cauldron, and optional serpent or leviathan danger on deep water. Server-authoritative config. |
| [FishTrap](https://thunderstore.io/c/valheim/p/Qmds/FishTrap/) (Qmds) | 1.1.4, 2026-09-27, updated post-1.0 | Passive fishing: baited traps in four tiers that catch fish over time. |
| [TheFisher](https://thunderstore.io/c/valheim/p/Marlthon/TheFisher/) (Marlthon) | 0.3.7, 2026-09-12, 1.0 tagged | Content: more than 30 new fish species, aquatic creatures and working aquariums. |

**Inspiration**
- A Stardew clone exists. A **Valheim-native** minigame does not. For example, line tension with rod bend:
  - the fish pulls in a direction and you steer the camera or rod against it;
  - reeling raises tension;
  - stamina drains while you hold;
  - rod quality and Fishing skill widen the safe band;
  - species get behavior profiles (sprinters, divers, thrashers).
- Keep vanilla's float and bite phase, and replace only the reel phase inside `FishingFloat`.
- Must support gamepad and hold/toggle accessibility options.
- Pair with passive traps (FishTrap-like) and gear (Reely-like) so fishing has a progression.

## 17. Magic applied to non-combat (New) — coverage: partial

Examples: crafting, farming, cooking, sailing.

| Mod | Status | Notes |
|-----|--------|-------|
| [Rune Magic](https://thunderstore.io/c/valheim/p/hyleanlegend/Rune_Magic/) (hyleanlegend) | 1.5.1, 2026-09-15, 1.0 tagged | **The closest match and a great reference.** Runic energy is extracted from runestones scattered across the map (biome-specific). Three kinds of rune: *casts* (terrain shaping, stone summoning), *augments* (for example Decumberance, Frozen Footfalls, Ocean Currents) and *engravings* on standing stones (Canopy shields from weather, Dry Land lowers water, Calm Waters, Repair regenerates buildings and ships, Chimney, Extinguishing). Engravings have a one-time cost and can be removed for a refund. [Nexus](https://www.nexusmods.com/valheim/mods/1359). |
| [MagicRevamp](https://thunderstore.io/c/valheim/p/blacks7ar/MagicRevamp/) (blacks7ar) | 1.5.1, 2026-09-20, 1.0 tagged | 7 staves, 7 wands, a spellbook and 96 spells with vanilla-like progression. Mostly combat. |
| [MagicPlugin](https://thunderstore.io/c/valheim/p/blacks7ar/MagicPlugin/) (blacks7ar) | 2.2.2, 2026-09-24, 1.0 tagged | Magic weapons (elemental and summoning totems), armor, accessories and eitr foods. Combat-focused. |
| [ProtectiveWards](https://thunderstore.io/c/valheim/p/shudnal/ProtectiveWards/) (shudnal) — related | 2.0.15, 2026-09-21, 1.0 tagged | Ward-area **multipliers** for smelting, cooking, fermenting and sap speed, plus passive repair and "offerings": base-wide passive effects that could be themed as magic. |

**Inspiration**
- Rune Magic shows the appetite for utility magic. Its weakness is a parallel economy (runic energy) detached from vanilla eitr and staves.
- **Ours:** utility spells that use vanilla **eitr** and the Blood and Elemental magic skills, or a new "Seiðr" skill. Candidate spells:
  - *Growth* (speed up `Plant` growth in an area);
  - *Kindle* (a smelter or kiln works faster for a while);
  - *Preserve* (slows food buff decay);
  - *Tailwind* (sailing);
  - *Soothe* (faster taming, fewer fleeing animals);
  - *Mend* (repairs pieces in a radius).
- Also make them persistent **engravings or totems** (Rune Magic's best idea), fed by eitr or sap (#15).
- This is the "theme across categories" the user described. Plan it as a shared magic core that the Farming, Cooking and Building mods consume.

## 18. Enchanting (New) — coverage: full

| Mod | Status | Notes |
|-----|--------|-------|
| [EpicLoot](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/) (RandyKnapp → Vapok → OrianaVenture) | 0.14.13, 2026-09-24, 1.0 tagged | The flagship: rarity loot (Magic → Mythic), an enchanting table, disenchanting and augmenting, set items, and an Adventure mode with bounties, treasure maps and gambling at Haldor, which also act as coin sinks. Data-driven through JSON. Open source ([GitHub](https://github.com/OrianaVenture/Randy_Vapok_ValheimMods)). Incompatible with Jewelcrafting. |
| [Valheim Enchantment System](https://thunderstore.io/c/valheim/p/KGvalheim/Valheim_Enchantment_System/) (KG) | 1.7.4, 2025-03-08, **deprecated** | MMO-style "+N" enchant scrolls (D to S tiers, blessed variants) with failure chances. A forked continuation exists ([Chaos Valheim Enchantment System](https://thunderstore.io/c/valheim/p/nutnnut/Chaos_Valheim_Enchantment_System/), status not checked). |
| [Jewelcrafting](https://thunderstore.io/c/valheim/p/Smoothbrain/Jewelcrafting/) (Smoothbrain / blaxxun) | 2.0.1, 2026-05-06, **deprecated** | Sockets and gems: six gem types × three tiers, higher tiers break more, and bosses drop unique gems. Effects depend on the slot. Configured with YAML. [GitHub](https://github.com/blaxxun-boop/Jewelcrafting). |

**Inspiration**
- A saturated space dominated by EpicLoot, which is huge, RNG-loot-centric and conflicts with the socket mods.
- Our enchanting should be **small, hand-authored and grounded in 1.0 systems**:
  - enchant by infusing a Forge of Potential upgrade (#3) or a gem (#25);
  - effects tie into adrenaline (trinkets) and the Ashlands gemstone variants;
  - a few carefully balanced effects rather than affix soup.
- Store data in `ItemDrop.ItemData.m_customData` (a vanilla `Dictionary<string,string>` that is saved and networked with items).
- Decide early whether we are EpicLoot-compatible (most modpacks run it).

## 19. Cooking equipment (New) — coverage: partial

The request can mean either **gear worn while cooking** or **new cooking tools and stations**. Both are covered below.

| Mod | Status | Notes |
|-----|--------|-------|
| [ReadyToCook](https://thunderstore.io/c/valheim/p/Wyvern/ReadyToCook/) (Wyvern) | 1.0.1, 2025-12-05, pre-1.0 | Mini-mod: Hildir's headscarves give a configurable Cooking skill and XP bonus when worn. The only wearable cooking gear found that is still current. |
| [Culinary Horizons](https://thunderstore.io/c/valheim/p/lnsanity/Culinary_Horizons/) (lnsanity) | 1.0.42, 2026-09-27, 1.0 tagged | Stations: a Spice Table (craft spices to season dishes), the Elderwok ("Forgotten Recipes") and a Bronzewood Banquet Bench (banquets with buffs). |
| [CookingAdditions](https://thunderstore.io/c/valheim/p/blacks7ar/CookingAdditions/) (blacks7ar) | 1.3.3, 2026-09-20, 1.0 tagged | 3 cooking stations, 2 processing stations, 23 foods, a mineable resource and pickables. |
| [ValheimCuisine](https://thunderstore.io/c/valheim/p/Xutz/ValheimCuisine/) (Xutz) | 2.3.2, 2026-09-26, updated post-1.0 | More than 300 Nordic recipes and cauldron stations (Eldhrímnir and others). |

Also relevant:
- [BoneAppetit](https://thunderstore.io/c/valheim/p/RockerKitten/BoneAppetit/) (3.3.1, 2023, pre-1.0): foods, stations and a **Chef Hat** that boosts Cooking XP.
- [Cooking](https://thunderstore.io/c/valheim/p/Smoothbrain/Cooking/) (Smoothbrain, 1.2.2, 2026-02-05, pre-1.0): the skill raises the food stats of cooked food, and "perfect" food gives a speed buff.

**Inspiration**
- Recipe and station content is crowded. What is thin is **functional cooking gear that changes *how* you cook**. For example:
  - apron: a slower overcook window on a `CookingStation`;
  - oven mitts: pull items from fire without burning;
  - cleaver: extra meat when butchering;
  - a spice pouch, used as an adrenaline-style trinket: the next meal lasts longer.
- Add station **extensions** (vanilla `StationExtension`) for the cauldron and food preparation table, such as a spice rack or a smoking rack, that unlock recipe tiers. This ties into the recipe-unlock timing in #6.
- Reuse the vanilla Cooking skill. Don't add a parallel skill.

## 20. Bring tamed animals and pets on a ship (New) — coverage: partial (workarounds only)

**Vanilla hooks**
- Ship standing is tracked by `Character.InNumShipVolumes` and `Character.GetStandingOnShip`. Players use `AttachStart(..., onShip, ...)`.
- There is no "passenger" mode for AI. Tames get knocked off, jump off to chase targets, or take wave damage, according to many Steam threads.

| Mod | Status | Notes |
|-----|--------|-------|
| [CreatureCarry](https://thunderstore.io/c/valheim/p/Wendigo/CreatureCarry/) (Wendigo) | 1.3.0, 2026-03-21, pre-1.0, AI | Pick up and carry tamed creatures (and take them through portals), and protect tames from enemies. |
| [Tameable Collector](https://thunderstore.io/c/valheim/p/KGvalheim/Tameable_Collector/) (KG) | 1.1.0, 2025-10-27, **deprecated** | Store a tame inside an item (keeping saddle, level and HP) and respawn it later. The classic "Pokéball" workaround. |
| [ValheimRAFT](https://thunderstore.io/c/valheim/p/zolantris/ValheimRAFT/) (zolantris) | 5.0.7, 2026-09-25, 1.0 tagged | Buildable vehicles (ships and land vehicles). You can build pens on deck. [GitHub](https://github.com/zolantris/ValheimMods). |
| [TamedHarpoon](https://thunderstore.io/c/valheim/p/Brainless_Azura/TamedHarpoon/) | see #7 | Harpoon a tame onto the deck. |

**Inspiration**
- No mod makes tames behave as **real ship passengers**. Ours:
  - When a tame (following, or ordered to stay) is inside a ship volume, "board" it: parent it to the ship (like a player standing on a ship), stop AI locomotion and target acquisition, play a sit or idle animation, and make it immune to falling or drowning while aboard.
  - Disembark on a command, or automatically when the ship touches land and the owner steps off.
- **Networking is the hard part:**
  - The ship's owner and the creature's owner can differ. Make the ship owner claim creature ownership while it is aboard, or sync relative positions the way players do.
- Capacity per ship type (Karve 1 small, Longship 2 lox) keeps it Valheim-y.
- A deliberately "New"-scope project. Prototype early.

## 21. Bring tamed animals and pets through a stone portal (New) — coverage: full

**Vanilla hooks:** `Teleport.Interact` and `Player.TeleportTo` (which overrides `Character.TeleportTo`) only move the player. There is no tame logic in `Tameable`, `MonsterAI` or `BaseAI`.

| Mod | Status | Notes |
|-----|--------|-------|
| [TeleportEverything](https://thunderstore.io/c/valheim/p/Zenox/TeleportEverything/) (Zenox) | 1.2.7, 2026-09-26, 1.0 tagged | Nearby tamed allies travel with you. Modes: following only, all nearby, except named, or named only. Also restricted items, the attached cart and optionally enemies. |
| [TeleportEverything](https://thunderstore.io/c/valheim/p/OdinPlus/TeleportEverything/) (OdinPlus) | 2.9.1, 2026-02-08, pre-1.0 | The original and most popular: allies, ores and ingots, carts, enemies, and blocking teleport when enemies are near. [Nexus](https://www.nexusmods.com/valheim/mods/1806). |
| [ServersideQoL TameAssist](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_TameAssist/) (ArgusMagnus) | 2.1.0, 2026-09-25, server-only | All tames can follow you, even through portals and into dungeons. Shows taming and growth progress. Works with vanilla clients. |
| [AdjustablePortals](https://thunderstore.io/c/valheim/p/MidnightMods/AdjustablePortals/) (MidnightMods) | 0.4.0, 2026-09-12, 1.0 tagged | Portal progression config. Tames near the portal travel with you (following-only by default). |

Also: [TeleportWolves](https://thunderstore.io/c/valheim/p/ToastyWzrd/TeleportWolves/) (2021, the original idea; sneak-teleport takes every tame in a radius) and [Beyond The Pen](https://thunderstore.io/c/valheim/p/L3ca/Beyondthepen/) (2024, pre-1.0; deer and hare taming, and followers go through portals).

**Inspiration:** fully covered by several maintained mods, including one that is server-only. Build it only if our tame suite (#1, #7, #20) needs tight integration, for example "tames aboard a ship go through a portal with the ship crew". Respect the vanilla "no ore through portals" rule. Don't turn this into a cheat.

## 22. Ward revamp (New) — coverage: partial

The user gave no description.

**Vanilla hooks**
- `PrivateArea`: `m_radius` 10, `m_enabledByDefault`, a permitted-player list, and `PrivateArea.CheckAccess` gating interactions.
- `ShieldGenerator` (1.0) is the other "base protection" object.

| Mod | Status | Notes |
|-----|--------|-------|
| [ProtectiveWards](https://thunderstore.io/c/valheim/p/shudnal/ProtectiveWards/) (shudnal) | 2.0.15, 2026-09-21, 1.0 tagged | The most complete *maintained* option. **Access and PvE:** per-ward range and visuals, access policy, permitted players, guild binding, password, connected ward networks, protected interactions (containers, doors, portals, stations, vehicles, tames), and expiration. **Passive support:** repair one piece every 10 s, protect plants, tames and ships, auto-close doors. **Multipliers:** damage, drains, and smelting/cooking/fermenting/sap speed. **Offerings.** [GitHub](https://github.com/shudnal/ProtectiveWards). |
| [WardIsLove](https://thunderstore.io/c/valheim/p/Azumatt/WardIsLove/) (Azumatt) | 3.7.2, 2026-02-04, **deprecated** | Per-ward configurable wards with an admin/owner GUI. Was the flagship. |
| [BetterWards](https://thunderstore.io/c/valheim/p/Azumatt/BetterWards/) (Azumatt) | 1.9.5, 2025-03-06, **deprecated** | WardIsLove's predecessor. |

**Inspiration**
- Existing ward mods are **server-admin and PvE-protection toolkits**. A *gameplay* revamp is open:
  - **ward tiers** upgraded through station extensions (radius, features);
  - **upkeep** in surtling cores or eitr (ties to #17 and #25);
  - base auras, a "comfort for the whole base" style (faster processing inside the ward, as ProtectiveWards' multipliers show players want);
  - ward-scoped automation: toggle every light (#9), auto-repair, and tames protected or kept from wandering.
- Clarify intent with the user before building this.

## 23. Player trophy (New) — coverage: partial

| Mod | Status | Notes |
|-----|--------|-------|
| [Player Heads](https://thunderstore.io/c/valheim/p/KGvalheim/Player_Heads/) (KG) | 1.0.0, 2023-10-10, **deprecated** | A player "head" drops when a player dies in PvP. The only prior art. |
| [UsefulTrophies](https://thunderstore.io/c/valheim/p/probablykory/UsefulTrophies/) (probablykory) — related | not checked | Consumable trophies (skill XP, boss powers). Shows another sink for trophies. |

**Inspiration**
- Nothing current.
- **Ours:** a player trophy (PvP kill, or an optional "memento" placed in the tombstone on any death). It stores the victim's name and look (hair and beard items, hair and skin colors) in `ItemData.m_customData`. On an `ItemStand` it renders the actual face and hair, using the `VisEquipment` model data.
- **Uses:** a "memorial" trinket (#12), bragging on a trophy wall, and bounties.
- **Watch:** keep the custom data small, and make sure `ItemStand` shows it for every client.

## 24. Barber shop to customize armor (New) — coverage: partial

Examples from the user: shield pattern, armor color and glow.

**Vanilla hooks**
- Vanilla 1.0 **already has a barber**: the `Barber` piece plus `PlayerCustomizaton.ShowBarberGui`, which handles hair and beard. This is a natural place to extend.
- Shield and cape styles are chosen only at craft time, as the item's `m_variant` (`InventoryGui` craft variant).

| Mod | Status | Notes |
|-----|--------|-------|
| [Transmog](https://thunderstore.io/c/valheim/p/Alpus/Transmog/) (Alpus) | 3.2.0, 2026-09-10, 1.0 tagged | Equipped armor and weapons (and shields, if enabled in config) look like any other item while keeping their stats. Uses a hotkey panel. |
| [Armoire](https://thunderstore.io/c/valheim/p/Advize/Armoire/) (Advize) | 1.2.2, 2026-09-17, 1.0 tagged | A buildable animated wardrobe. Unlock appearances, save 3 outfits, override visuals. Gamepad and multiplayer support. [Nexus](https://www.nexusmods.com/valheim/mods/3170). |
| [Change Style Of Existing Shield](https://thunderstore.io/c/valheim/p/Goldenrevolver/Change_Style_Of_Existing_Shield/) (Goldenrevolver) | 1.1.0, 2023-10-23, pre-1.0 | Restyle a shield or linen cape after crafting by "using" it on a workbench or forge, the same way items are offered to a boss altar. [GitHub](https://github.com/Goldenrevolver/ValheimMiniMods). |
| [balrond BannerColorizer](https://thunderstore.io/c/valheim/p/Balrond/balrond_BannerColorizer/) (Balrond) | 1.0.8, 2026-09-11, 1.0 tagged | A dye station plus dyes for *build pieces* (banners, carpets, roofs, crystal walls). Proof that a dye economy works. |

Also:
- [Transmogrification](https://thunderstore.io/c/valheim/p/KGvalheim/Transmogrification/) (KG, 2022, pre-1.0): a station plus 20 special visual effects (glows) for armor and weapons.
- [ColorfulLights](https://thunderstore.io/c/valheim/p/ComfyMods/ColorfulLights/) and ColorfulWards (ComfyMods): RGB tints for fires and wards.

**Inspiration**
- Transmog is covered (Transmog, Armoire). **Missing:**
  - dye and tint channels on armor;
  - emissive glow color and intensity;
  - changing shield pattern and banner after crafting, on 1.0.
- **Ours:** extend the vanilla **Barber** into a "Tailor/Armorer" chair (or a sister station) with tabs for armor tint (dyes from flowers, berries and gems, #25), glow (costs eitr or gems), and shield or cape pattern (swap `m_variant`, as Goldenrevolver does).
  - Store choices in `ItemData.m_customData` and apply them in `VisEquipment` through a `MaterialPropertyBlock`, so other players see them.
  - Coexist with Transmog: apply our tint after their mesh swap.

## 25. Gemstone revamp (New) — coverage: partial

The user wants a crafting use for gemstones, "because right now we have too much gold". This reads as a sink for valuables and gold.

**Vanilla context**
- Valuables (Ruby, Amber, Amber Pearl, Silver Necklace) mostly get sold to Haldor for coins. Ruby has a few recipe uses.
- The Ashlands gemstones (Jade, Bloodstone, Iolite, from Charred Fortresses) turn Ashlands weapons into elemental variants at the Black Forge or Galdr Table.
- Coins have few sinks once Haldor's and Hildir's stock is bought.

| Mod | Status | Notes |
|-----|--------|-------|
| [Jewelcrafting](https://thunderstore.io/c/valheim/p/Smoothbrain/Jewelcrafting/) (Smoothbrain) | 2.0.1, 2026-05-06, **deprecated** | Gems and sockets (see #18). It was the answer to "what do gems do", and it is now gone. That leaves a real gap. |
| [EpicJewels](https://thunderstore.io/c/valheim/p/MidnightMods/EpicJewels/) (MidnightMods) | 1.1.2, 2026-09-09, updated post-1.0 | More jewels for Jewelcrafting. Depends on a now-deprecated base. |
| [ConfigurableCraftingRecipes](https://thunderstore.io/c/valheim/p/MGDev/ConfigurableCraftingRecipes/) (MGDev) | 1.1.3, 2026-05-29, pre-1.0 | Adds recipes for uncraftable items, including valuables ↔ coins, trophies and trader items. |
| [EpicLoot](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/) | see #18 | Its Adventure mode (gambling, treasure maps, bounties at Haldor) is the best existing **coin sink**. |

**Inspiration**
- The pain is **economy**, not gems themselves. Ours:
  - a **Jeweler's bench** (a `StationExtension` of the Forge) that cuts raw valuables into gems;
  - gems act as:
    - trinket sockets (#12);
    - armor glow and tint (#24);
    - ward foci (#22);
    - Forge of Potential catalysts that raise the success chance or protect the idol (#3).
  - Haldor services as coin sinks: repair-anywhere tokens, idol purchase, pet transport crates (#20).
- Avoid prefab-name clashes with Jewelcrafting in case old saves still hold its items.

---

## Notable mods to study

| Mod / repo | Why study it |
|------------|--------------|
| **ServersideQoL suite** (ArgusMagnus), [GitHub](https://github.com/ArgusMagnus/ValheimServersideQoL), [Thunderstore](https://thunderstore.io/c/valheim/p/ArgusMagnus/) | **Server-only mods that work with vanilla and console clients**, done by manipulating ZDOs. The modules touch many of our ideas: ContainerSigns (#10), PrefabConfigurator (#9, plants), TameAssist (#21), AutoProcess (#11), AutoMapTables, Treesurrection. Very active (27 packages, updated this week). The reference for "what can be done server-side without client mods" in a crossplay 1.0 world. |
| **PlantEasily / PlantEverything / Armoire** (Advize), [GitHub](https://github.com/AdvizeGH/Advize_ValheimMods) | Mature, fast-updating (1.0 tagged within weeks), polished UX: placement ghost grids, validity highlighting, gamepad, localization. GPL, so study only. |
| **EpicLoot** (OrianaVenture fork), [GitHub](https://github.com/OrianaVenture/Randy_Vapok_ValheimMods) | The largest open-source item-effect system. Data-driven JSON, custom item data, UI injection into the crafting and enchanting panels, and multiplayer sync. Also houses smaller mods (EquipmentAndQuickSlots, AdvancedPortals, CreatureLevelAndLootControl). |
| **ProtectiveWards / TradersExtended** (shudnal), [GitHub](https://github.com/shudnal/ProtectiveWards), [GitHub](https://github.com/shudnal/TradersExtended) | Clean, maintained (1.0 tagged) mods with server-synced JSON/YAML data lists and in-game editors. Good patterns for #6, #22 and data-driven configuration in general. |
| **ComfyMods** (BruceOfTheBow), [GitHub](https://github.com/BruceOfTheBow/BruceComfyMods) | Small, focused, well-structured single-feature mods (ComfyAddAllFuel, ColorfulLights, ColorfulWards, Pinnacle). A model for our QoL mod granularity and for "one patch, all devices" designs. |
| **AllTameable / CreatureGenetics** (Meldurson), [GitHub](https://github.com/meldurson/AllTameable), [GitHub](https://github.com/meldurson/Creature_Genetics) | Breeding internals: inheritance through ZDO data, egg-level passthrough, mutation, and DNA traits. Directly relevant to #1 and the tame suite. |
| **Goldenrevolver mini-mods / QuickStackStore**, [GitHub](https://github.com/Goldenrevolver/ValheimMiniMods), [GitHub](https://github.com/Goldenrevolver/QuickStackStore) | Tiny, idiomatic patches, such as "use an item on a station" to restyle a shield (#24). Good examples of minimal Harmony surface. |
| **Jewelcrafting and the blaxxun libraries** (ItemManager, PieceManager, ServerSync), [GitHub](https://github.com/blaxxun-boop/Jewelcrafting) | The socket and gem design (#25) and the most widely embedded helper libraries. Also a **cautionary tale**: embedded old ServerSync binaries broke on 1.0 (`ZRoutedRpc.Everybody` became a const), and Jewelcrafting ended up deprecated. We should compile our shared code against the current game assemblies and not vendor stale binaries. |

Honorable mentions:
- **ValheimRAFT** (zolantris), [GitHub](https://github.com/zolantris/ValheimMods): vehicles, relevant to #20.
- **Rune Magic** (hyleanlegend): non-combat magic design, #17.
- **SmartRepair** (3WiseStooges), [GitHub](https://github.com/3WiseStooges/SmartRepair): a small, readable 1.0 QoL mod.
- **Drop That** (ASharpPen), [GitHub](https://github.com/ASharpPen/Valheim.DropThat): loot-table interop for #13.
