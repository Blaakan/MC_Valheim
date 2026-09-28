# Farming & Cooking

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> Scope: plants and cultivation, Ashlands/cold rules and the shield generator, pickables, beehive, sap
> collector, taming, breeding and growth, fishing, the harpoon, creatures on ships, portals and creature
> teleporting, minimap icons, food, cooking stations, fermenter, feasts, cauldron/food stations and recipe
> discovery. Code is cited as `Class.Method` (files live in `.ref/decompiled/assembly_valheim/<Class>.cs`).
> Numbers marked *(prefab)* are Unity-serialized values: the decompiled code only shows the C# default, so check
> the real value at runtime (dump `ZNetScene.instance.m_prefabs` / `ObjectDB.instance.m_recipes`) before relying
> on it. Prefab names quoted here were checked against
> `valheim_Data/StreamingAssets/SoftRef/manifest_extended` (asset paths) and the English localization strings in
> `valheim_Data/resources.assets`.
>
> Related chapters: `building-crafting.md` §1 (piece placement), §7-§9 (CraftingStation, Recipe, InventoryGui
> crafting); `exploration-player.md` §2 (Sailing) and §3 (Map); `ux.md` §8 (Minimap pins).

---

## Overview

### One template for almost everything

Every farming and cooking object is a networked `ZNetView` whose state lives in its `ZDO`. Nearly all of them
follow the same pattern:

1. **Timer on every client, mutation on the owner.** A timer (`InvokeRepeating("Name", …)`,
   `SlowUpdate.SUpdate`, or `CustomFixedUpdate`) runs wherever the object is loaded, but only the **ZDO owner**
   (`m_nview.IsOwner()`) writes state.
2. **Interaction on the client, work on the owner.** `Interact` / `UseItem` run on the player's machine and call
   `ZNetView.InvokeRPC(name, …)`. That overload routes to `m_zdo.GetOwner()` (`ZNetView.InvokeRPC(string, …)`),
   so an `RPC_*` handler without an explicit `IsOwner()` check still runs on the owner. Visual refresh is
   broadcast with `InvokeRPC(ZNetView.Everybody, …)`.
3. **Server clock, stored as ticks.** Progress is computed from `ZNet.instance.GetTime()` (server-synced
   `DateTime`) stored as `long` ticks in the ZDO. Anything measured this way "catches up" when the zone is
   loaded again (plants, beehive, sap collector, sap root, fermenter, cooking station, pregnancy due date,
   hunger, Growup). Things that only accumulate per tick while loaded do **not** catch up (taming progress, love
   points).
4. **Owner = nearby client.** The server assigns ownership in `ZDOMan.ReleaseNearbyZDOS`: a persistent ZDO
   whose owner's active area no longer contains it is handed to a peer whose active area does. A dedicated server
   almost never owns farm objects, so farming/cooking logic changes usually need **every client** to run the mod.

| System | Main class | Ticks on | ZDO keys (`ZDOVars.*`) | Catches up offline |
|---|---|---|---|---|
| Sapling growth | `Plant` | owner (`SUpdate`) | `s_seed`, `s_plantTime` | yes |
| Pickable respawn | `Pickable` | owner, every 60 s | `s_picked`, `s_pickedTime`, `s_enabled` | yes |
| Honey | `Beehive` | owner, every 10 s | `s_lastTime`, `s_product`, `s_level` | yes |
| Sap | `SapCollector` / `ResourceRoot` | owner, 5 s / 10 s | `s_lastTime`, `s_product`, `s_level` | yes |
| Taming | `Tameable` | owner, every 3 s | `s_tameTimeLeft`, `s_tameLastFeeding`, `s_tamed` | no (progress), yes (hunger) |
| Breeding | `Procreation` | owner, every `m_updateInterval` | `s_lovePoints`, `s_pregnant` | love no, due date yes |
| Offspring growth | `Growup` / `EggGrow` | owner, 10 s / 5 s | `s_spawnTime` / `s_growStart` | yes |
| Fishing | `FishingFloat` / `Fish` | float owner = fisher | `s_rodOwner`, `s_bait`, `s_sessionCatchID`, `s_hooked`, `s_escape` | n/a |
| Cooking | `CookingStation` | owner, every 1 s | `"slot{i}"`, `"slotstatus{i}"`, `s_fuel`, `s_startTime` | yes |
| Fermenting | `Fermenter` | owner, every 2 s | `s_content`, `s_startTime` | yes |
| Feast portions | `Feast` | owner on RPC | `s_value` | n/a |
| Food buffs | `Player` | local player | player save (not ZDO) | n/a |

### What the code does not tell you

Grow times, biome masks, bait tables, taming foods, cook times, feast portions, recipe stations and levels are all
set on prefabs. This chapter lists C# defaults where useful, but real values must be dumped at runtime (a small
dev plugin that iterates `ZNetScene.instance.m_prefabs` and `ObjectDB.instance.m_recipes` after
`ObjectDB.Awake`, printing `Plant`, `Pickable`, `Procreation`, `Tameable`, `MonsterAI.m_consumeItems`,
`Fish.m_baits`, `CookingStation.m_conversion`, `Fermenter.m_conversion`, `Feast` and `Recipe` data).

### General patching notes for this area

- Private methods started with `InvokeRepeating("Name")` (`Procreation.Procreate`, `Tameable.TamingUpdate`,
  `Growup.GrowUpdate`, `EggGrow.GrowUpdate`, `Beehive.UpdateBees`, `SapCollector.UpdateTick`,
  `ResourceRoot.UpdateTick`, `Fermenter.SlowUpdate`, `CookingStation.UpdateCooking`, `Pickable.UpdateRespawn`,
  `ShieldGenerator.UpdateShield`, `TeleportWorld.UpdatePortal`) patch normally with HarmonyX
  (`AccessTools.Method(typeof(X), "Name")`).
- Do not patch tiny getters that Mono may inline (`Character.GetLevel`, `Character.IsTamed()`,
  `Pickable.GetPicked`, `Plant.GetStatus`). Patch their callers.
- To add behaviour to every instance of a vanilla prefab, postfix `ZNetScene.Awake` and `AddComponent` on the
  prefab (or on prefabs found through `m_prefabs`). Every client that can own the ZDO must have the mod.
- Custom ZDO keys: use unique string names (for example `"vm.breeding.partnerLevel"`), hashed with
  `GetStableHashCode()`. Vanilla ignores unknown keys, and saves keep them.
- Per-character persistence: `Player.m_customData` (`Dictionary<string,string>`, saved in `Player.Save`).
  Per-item persistence: `ItemDrop.ItemData.m_customData`.

---

## 1. Plants: saplings, crops, cultivated ground

### Key classes
- `Plant` (extends `SlowUpdate`): the sapling stage of crops, trees, vines.
- `Heightmap`: biome lookup (`GetBiome`, static `FindBiome`, `FindHeightmap`) and the terrain paint mask
  (`IsCultivated`, `GetCultivationMask`, `IsCleared`, `GetVegetationMask`, `IsLava`).
- `Piece` placement flags plus `Player.UpdatePlacementGhost` / `Player.TryPlacePiece` / `Player.PlacePiece`.
  See `building-crafting.md` §1.
- Result prefabs: `TreeBase` (trees, `TreeBase.Grow` plays the grow animation through `RPC_Grow` to everybody) or
  `Pickable` (crops such as `Pickable_Barley`, `Pickable_Carrot`, `Pickable_Onion`, `Pickable_Turnip`,
  `Pickable_Flax`, `Pickable_Kale`, `Pickable_Oat`, `Pickable_Poteitr`, plus the `Pickable_Seed*` variants).
- `Destructible`: sapling hit points (`s_health`). `Plant.Destroy` applies 9999 damage through `IDestructible`.
- Verified sapling prefabs: `sapling_barley`, `sapling_carrot`, `sapling_flax`, `sapling_onion`,
  `sapling_turnip`, `sapling_Kale`, `sapling_oat`, `sapling_poteitr`, `sapling_magecap`, `sapling_jotunpuffs`,
  `sapling_seedcarrot/seedkale/seedonion/seedturnip`, `Beech_Sapling`, `Birch_Sapling`, `Oak_Sapling`,
  `PineTree_Sapling`, `FirTree_Sapling`, `FirTree_big_Sapling` (Deep North), `VineAsh_sapling`,
  `VineGreen_sapling`. Tools: `Cultivator` (`_CultivatorPieceTable`), `Hoe` (`_HoePieceTable`), `Scythe`.

### Flow
1. **Placement (client).** `Player.UpdatePlacementGhost` sets `m_placementStatus`:
   `Piece.m_cultivatedGroundOnly` fails with `NeedCultivated` when `Heightmap.IsCultivated(point)` is false;
   `m_onlyInBiome` fails with `WrongBiome`; `m_vegetationGroundOnly` fails with `NeedDirt` (vegetation mask
   < 0.25, or > 0.1 in the Ashlands); `m_allowedInDeepSnow` / `m_requireDeepSnow` produce
   `DeepSnow` / `NoSnow`; `m_blockRadius` + `m_blockingPieces` produce `MoreSpace`. `Player.TryPlacePiece`
   turns the status into a message, then `Player.PlacePiece` instantiates the prefab and calls every
   `IPlaced.OnPlaced`. `Player.UpdatePlacement` consumes resources with `Player.ConsumeResources`.
2. **`Plant.Awake`.** Reads or creates `s_seed` from the ZDOID (written with `okForNotOwner: true`). The owner
   stamps `s_plantTime` with server ticks if it is 0.
3. **`Plant.SUpdate`** (driven by `SlowUpdater`: 100 instances per frame, then a 0.1 s wait) calls
   `UpdateHealth(TimeSincePlanted())`. It switches healthy/unhealthy models, using the "grown" pair once 50% of
   the grow time has passed. The owner calls `Grow()` when `timeSincePlanted > GetGrowTime()` and the instance has
   existed locally for more than 10 s.
   - Quirk: as decompiled, the throttle is inverted. The body runs when `time <= m_updateTime` and then sets
     `m_updateTime = time + 10`, so it runs on every SlowUpdater pass: one upward raycast and one or two overlap
     spheres per plant, each pass. Keep per-plant work in patches cheap.
4. **`Plant.UpdateHealth`** checks rules in order; the first failure wins:
   - less than 10 s since planting: `Healthy`
   - only if a `Heightmap` is found under the plant:
     - biome not in `m_biome`: `WrongBiome`
     - `m_needCultivatedGround` and ground not cultivated: `NotCultivated`
     - Ashlands, `!m_tolerateHeat`, and not `ShieldGenerator.IsInsideShield(pos)`: `TooHot`
     - DeepNorth or Mountain, `!m_tolerateCold`, and not inside a shield: `TooCold`
   - `HaveRoof()` (100 m upward raycast against Default/static_solid/piece): `NoSun`
   - `!HaveGrowSpace()`: `NoSpace`. An overlap sphere of `m_growRadius` fails on any non-`Plant` collider or any
     *healthy* other `Plant`; `m_growRadiusVines` also fails on any `Vine`.
   - vines (`m_attachDistance > 0`) need a `Piece` with `!m_noVines` in range: `NoAttachPiece`
   - Note: with no heightmap under the plant (for example on a floating object), the biome, cultivation, heat and
     cold checks are skipped entirely.
5. **`Plant.GetGrowTime`** returns `Lerp(m_growTime, m_growTimeMax, Random(seed))`, deterministic per plant.
6. **`Plant.Grow`** (owner):
   - If the plant is unhealthy: with `m_destroyIfCantGrow` it calls `Destroy()`; otherwise it does nothing and
     keeps waiting forever.
   - If healthy: instantiates a random `m_grownPrefabs` entry (random yaw ±11.25°, scale
     `Random(m_minScale, m_maxScale)` through `ZNetView.SetLocalScale`, vines placed with `PlaceAgainst`), calls
     `TreeBase.Grow()`, destroys the sapling and plays `m_growEffect`. `__result` is the spawned GameObject.

**Scythe mass harvest (1.0):** a `Piece` with `m_harvest = true` placed from the scythe runs
`Piece.OnPlaced`. It overlaps radius `Lerp(m_harvestRadius, m_harvestRadiusMaxLevel, Farming skill factor)`,
calls `Pickable.Interact` on every `m_harvestable` pickable, then destroys itself.

### Cultivated ground
- `Heightmap.IsCultivated`: paint-mask green channel > 0.5. `GetCultivationMask` returns the raw green value.
  Paint is written by `TerrainComp` from `TerrainModifier.PaintType` (`Dirt`, `Cultivate`, `Paved`, `Reset`,
  `ClearVegetation`, `DeepSnow`).
- In the Deep North the same green channel doubles as **snow depth**:
  - `Heightmap.m_paintMaskDeepSnow` is white (G = 1).
  - `Player.UpdatePlacementGhost` compares `GetCultivationMask` against `m_deepSnowBuildHeight`.
  - `Character.UpdateWalking` uses it for deep-snow slowdown, and `Character.UpdateGroundContact` for the
    snow landing effect.
  - `TerrainComp.PaintCleared` special-cases `Cultivate` in the Deep North (`isChangingSnow`).

  Anything that reads or writes cultivation in the Deep North must account for this.

### Data & persistence
- The Plant ZDO stores only `s_seed` (int) and `s_plantTime` (long ticks). Status is recomputed, never saved.
- Everything else is *(prefab)*: `m_growTime` (C# default 10), `m_growTimeMax` (2000), `m_growRadius` (1),
  `m_biome`, `m_needCultivatedGround`, `m_destroyIfCantGrow`, `m_tolerateHeat/Cold`, `m_minScale/maxScale`,
  `m_grownPrefabs`.

### Multiplayer authority
Status is evaluated on every client (visuals only). Only the owner grows or destroys. Growth is wall-clock: a
plant grows on the first owner update after enough server time has passed.

### Patch points
- `Plant.UpdateHealth` postfix: the one place to add or override growth rules (greenhouse, fertilizer, Ashlands
  rules). Write the private `m_status` with `AccessTools.FieldRefAccess<Plant, Plant.Status>("m_status")`.
- `Plant.GetGrowTime` postfix: scale growth by skill, fertilizer or season.
- `Plant.Grow` prefix/postfix: swap the grown prefab, add a yield modifier, or tag lineage on the spawned object.
- `Player.UpdatePlacementGhost` postfix and `Player.TryPlacePiece` / `Player.PlacePiece`: extra placement rules,
  multi-ghost planting.
- `ZNetScene.Awake` postfix: edit sapling prefab data (`m_biome`, `m_tolerateHeat`, `m_destroyIfCantGrow`).

---

## 2. Shield generator effects on plants (Ashlands heat, mountain/Deep North cold)

### Key classes
`ShieldGenerator` (`piece_shieldgenerator`). Its consumers: `Plant.UpdateHealth`, `CinderSpawner.UpdateSpawnCinder`,
`Cinder.FixedUpdate` via `CheckObjectInsideShield`, `WearNTear` (`IsInsideShieldCached` for ash damage and snow),
`Projectile`, `Player` (indoor audio, shelter), `AudioMan`. Fire: `Cinder.CanBurn`, `Fire.Dot`.

### Flow
- `ShieldGenerator.Start` registers the instance in a **static list of loaded generators** (`m_instances`) and
  starts `UpdateShield` every 0.22 s.
- `UpdateShield`: fuel ratio = `s_fuel / m_maxFuel`; radius target = `m_minShieldRadius + ratio × (max − min)`,
  or 0 when out of fuel and `m_offWhenNoFuel`. `Update` animates `m_radius` toward the target at
  `m_startStopSpeed`; the first check snaps it.
- `IsInsideShield(point)`: distance from `m_shieldDome` is less than the current animated `m_radius` of any
  loaded generator. `IsInsideMaxShield` uses `m_maxShieldRadius`; `IsInsideShieldCached` is the cheaper variant
  used by `WearNTear`.
- Plants: `TooHot` in the Ashlands and `TooCold` in Mountain/DeepNorth unless inside a shield, or unless
  `m_tolerateHeat` / `m_tolerateCold` is set. So a shield is also a mountain / Deep North greenhouse in vanilla.
- **Grown objects are not protected or harmed by any shield rule.** Pickables and trees ignore shields. Ashlands
  trees can still burn: `CinderSpawner` only spawns cinders outside shields, and `Cinder.CanBurn` returns true
  for any `TreeBase` or `TreeLog`, so cinder fire (`Fire.Dot`, `HitData.HitType.CinderFire`) can damage trees
  outside shields.
- Fuel: `RPC_AddFuel` and `RPC_SetFuel` run on the owner. Projectile hits drain `m_fuelPerDamage × damage`
  (`OnProjectileHit`). A full generator starts the attack charge timer (`s_startTime`); `RPC_Attack` spends all
  fuel.

### Data & persistence
- ZDO keys: `s_fuel` (float) and `s_startTime` (attack charge).
- Tuning *(prefab)*: `m_minShieldRadius` (default 10), `m_maxShieldRadius` (30), `m_maxFuel` (10),
  `m_fuelPerDamage` (0.01), `m_attackChargeTime` (900 s).

### Multiplayer authority
- `IsInsideShield` only knows generators **loaded on the calling machine**. A plant's owner always has the
  nearby generator loaded, since the radius is at most 30 m and the active area spans neighbouring zones.
- For about a frame after load the radius is 0. `Plant` hides this with its 10 s grace; custom rules must add
  their own grace period (15-20 s) before punishing "not inside shield".

### Patch points
- `ShieldGenerator.IsInsideShield` (static) postfix to make other objects count as shielded. This is global, so
  keep it cheap.
- `Plant.UpdateHealth` postfix for plant-specific heat/cold rules.
- New per-object exposure logic belongs in a custom component (see the Ashlands trees idea), not in
  `ShieldGenerator`.

---

## 3. Pickables and PickableItem (wild and grown gatherables)

### Key classes
- `Pickable`: berries, mushrooms, flint, stone, thistle, crops, `Pickable_*`.
- `PickableItem`: world loot with a random item and stack, such as dungeon `Pickable_*Random`.
- `Game.ScaleDrops`: applies the world "resources" modifier.

### Flow (`Pickable`)
1. `Pickable.Awake` registers `RPC_SetPicked(bool)` and `RPC_Pick(int bonus)`, and reads `s_picked`,
   `s_pickedTime`, `s_enabled`. If `m_respawnTimeMinutes > 0` it starts `UpdateRespawn` every 60 s. A
   non-respawning pickable without `m_hideWhenPicked` that is already picked is destroyed on load (one-shot crops).
2. `Pickable.Interact` (client):
   - Refuses tar-stuck items (`m_tarPreventsPicking`).
   - Increments stats.
   - If `m_pickRaiseSkill` is set (Farming for crops): raises the skill and rolls
     `Random < skillFactor × m_maxLevelBonusChance` (default 0.25) for `+m_bonusYieldAmount`.
   - Then `InvokeRPC("RPC_Pick", bonus)`. The bonus is decided by the **picker's** client.
3. `Pickable.RPC_Pick` (owner): spawns `max(m_minAmountScaled, ScaleDrops(m_amount)) + bonus` items (or
   `m_amount` when `m_dontScale`), plus `m_extraDrops`; optionally `BaseAI.AggravateAllInArea` with the `Theif`
   reason; then `RPC_SetPicked(true)` to everybody.
4. `Pickable.SetPicked` (owner): stores `s_picked` and `s_pickedTime`, or destroys a one-shot pickable.
5. `Pickable.UpdateRespawn` (owner): when `ShouldRespawn()` (more than `m_respawnTimeMinutes` since
   `s_pickedTime`, plus the optional `m_spawnCheck` delegate), broadcasts `RPC_SetPicked(false)`.
6. `PickableItem`: on first load the owner picks from `m_randomItemPrefabs` and stores `s_itemPrefab` (hash) and
   `s_itemStack`. `Interact` calls RPC `"Pick"` on the owner, which drops the item and destroys the object.

### Data & persistence
- ZDO keys: `s_picked`, `s_pickedTime`, `s_enabled` (`Pickable`); `s_itemPrefab`, `s_itemStack`
  (`PickableItem`).
- `m_harvestable` marks scythe-harvestable pickables.

### Multiplayer authority
The owner spawns drops and the picked state. The picker decides the bonus and raises the skill.

### Patch points
- `Pickable.Interact` prefix/postfix: auto-replant, extra yield, "hunting" XP.
- `Pickable.RPC_Pick` (owner-side yield).
- `Pickable.ShouldRespawn` / `m_spawnCheck` for respawn rules.
- `Pickable.CanBePicked` is public and cheap: use it for "available" filtering on the minimap.
- There is **no static registry** of pickables. A tracker must add itself in a `Pickable.Awake` postfix and prune
  destroyed (Unity-null) entries lazily, since `Pickable` has no `OnDestroy` to patch.

---

## 4. Beehive

### Key classes
`Beehive` (`piece_beehive`, also the world `Beehive`), `Cover.GetCoverForPoint`, `PrivateArea.CheckAccess`.

### Flow
- `Beehive.UpdateBees`, every 10 s on all clients:
  - Active when `CheckBiome()` (`Heightmap.FindBiome(pos) & m_biome`) and `HaveFreeSpace()`
    (`Cover.GetCoverForPoint(m_coverPoint)` < `m_maxCover`, default 0.25, i.e. an open sky).
  - The owner adds the elapsed server time since `s_lastTime` to `s_product`. For each full `m_secPerUnit` it
    adds honey to `s_level`, clamped to `m_maxHoney` (default 4).
  - Daylight only affects the bee effect and the "sleeping" message, not production.
- `Beehive.Interact` (client, ward-checked): with honey it calls `RPC_Extract` on the owner, which spawns
  `level × ScaleDrops(honey)` items and resets `s_level`. Otherwise it shows a diagnostic message (biome, free
  space, sleep, happy).

### Data, authority, patch points
- ZDO keys: `s_lastTime`, `s_product` (float accumulator), `s_level`. Everything runs on the owner.
- Patch `Beehive.UpdateBees` (rate, biome, cover rules), `Beehive.RPC_Extract` (yield) or
  `Beehive.CheckBiome` / `HaveFreeSpace`.
- Unused text fields `m_notConnectedText` / `m_blockedText` show the class shares a template with `SapCollector`.

---

## 5. SapCollector and ResourceRoot

### Key classes
- `SapCollector` (`piece_sapcollector`): time-based producer attached to a root.
- `ResourceRoot` (`YggdrasilRoot`): drainable, regenerating reservoir. Produces `Sap`.

### Flow
- **Placement:** `Piece.m_mustConnectTo` (a `ZNetView` prefab reference) + `m_connectRadius` +
  `m_mustBeAboveConnected`. `Player.UpdatePlacementGhost` overlaps `m_connectRadius` and accepts any parent
  `ZNetView` whose **GameObject name contains** `m_mustConnectTo.name`.
- **`SapCollector.UpdateTick`** (every 5 s):
  - If `SapCollector.m_mustConnectTo` is set and no root is cached, `OverlapSphere(pos, 0.2)` finds a
    `ResourceRoot` in a parent.
  - On the owner, while `level < m_maxLevel` (4) and `root.CanDrain(1)`: accumulate elapsed time in
    `s_product`, produce `min(root level, units)` and call `root.Drain(units)`.
  - `Drain` sends `RPC_Drain` to the root's owner, which may be a different client.
- **`ResourceRoot.UpdateTick`** (10 s, owner): regenerates `m_regenPerSec × elapsed` (default 1/s) up to
  `m_maxLevel` (100). `m_highThreshold` (50) and `m_emptyTreshold` (10) drive the hover text, the emissive colour
  and the "draining slowly" status.
- **Extract:** `SapCollector.RPC_Extract` (owner) spawns `level × ScaleDrops(m_spawnItem)` and broadcasts
  `RPC_UpdateEffects`.

### Data & persistence
- `SapCollector` ZDO: `s_lastTime`, `s_product`, `s_level` (int).
- `ResourceRoot` ZDO: `s_lastTime`, `s_level` (float, default `m_maxLevel`).

### Multiplayer authority, patch points
- Collector owner and root owner are independent; drains are RPCs.
- Patch `SapCollector.UpdateTick` (rate, root discovery), `SapCollector.RPC_Extract` (output item; the private
  `m_root` tells which root is connected), `ResourceRoot.CanDrain` / `UpdateTick` (capacity, regen).
- To tap other trees, see the idea below: trees (`TreeBase`) carry no reservoir, so either add a `ResourceRoot`
  or make the collector self-sufficient.

---

## 6. Taming (`Tameable`, `MonsterAI`)

### Key classes
- `Tameable`: taming progress, hunger, commands (follow/stay), naming, saddle, unsummon.
- `MonsterAI`: feeding (`m_consumeItems`), follow target, `MakeTame`.
- `Character`: `SetTamed` / `RPC_SetTamed`, tamed flag in `s_tamed`, `SetLevel`.
- `BaseAI`: patrol point, idle movement, follow, alert.
- `Sadle`: riding.
- `Pet` / `Petable`: placeable pets; see the note at the end of this section.

### Flow
- **Feeding:** `MonsterAI.UpdateConsumeItem` (only when hungry, search every `m_consumeSearchInterval` (10 s)
  within `m_consumeSearchRange` (5 m) on the `item` layer, then `HavePath`). It walks to the item, calls
  `ItemDrop.RemoveOne`, and fires `m_onConsumedItem`. That triggers `Tameable.OnConsumedItem`, which calls
  `ResetFeedingTimer` (`s_tameLastFeeding`).
- **Hunger:** `Tameable.IsHungry` = `now − s_tameLastFeeding > m_fedDuration` (default 30 s *(prefab)*).
- **Taming:** `Tameable.TamingUpdate` (every 3 s, owner, not tamed, not hungry, not alerted):
  - Disables day-despawn and event-creature flags.
  - `DecreaseRemainingTime(3)`: the elapsed time is doubled (`m_tamingBoostMultiplier`) for **each** player
    within `m_tamingSpeedMultiplierRange` (60 m) who has a `TamingBoost` status attribute.
  - Progress is stored in `s_tameTimeLeft` (default `m_tamingTime`, 1800 s).
  - At 0 it calls `Tame()`, which runs `MonsterAI.MakeTame` (tamed, alert cleared, targets cleared) and messages
    the closest player.
- **`Tameable.Tame()` in detail** (private):
  - It first calls `Game.IncrementPlayerStat(PlayerStatType.CreatureTamed)` **unconditionally**, on the machine
    running it (the owner): the vanilla total goes to the owner's player, even when the call tames nothing.
  - Then the guard `m_nview.IsValid() && IsOwner() && m_monsterAI && m_character && !IsTamed()`. `MakeTame` →
    `Character.SetTamed` sends `RPC_SetTamed` to the owner, which is this machine, so it runs synchronously:
    `IsTamed()` is already true when `Tame()` returns.
  - The message goes to `Player.GetClosestPlayer(position, 30f)` only (closest loaded player strictly within 30 m;
    `Player.s_players` also holds remote players loaded near the owner): Center `m_character.m_name + " $hud_tamedone"`
    ("Boar has been tamed"). Nobody within 30 m = no message. This is the only use of `$hud_tamedone` in the game code.
    It reaches a remote player through `Player.Message` → ZNetView RPC `"Message"` → `Player.RPC_Message` on their
    client (see [ux.md §6](ux.md)).
- **Nobody records who fed the creature**: `ItemDrop` stores no dropper, and the owner is whoever holds the ZDO
  (`ZDOMan.ReleaseNearbyZDOS`: a peer keeps it while it stays in its active area), not necessarily the player at the
  pen. Vanilla keeps **no per-creature tame data**: only the `CreatureTamed` total (plus `TamedPetting`,
  `TamedCommand`). Mod: [Creature Kill and Tame Counts](../../src/Exploration/Stats.PerCreature) counts tames per
  creature from the message.
- **Console `tame`** (`Tameable.TameAllInArea`) ignores its point and radius: it calls `Tame()` on **every loaded**
  tameable character. Only owned, untamed ones get tamed (wild ones nearby included), but each call bumps
  `CreatureTamed`, even on creatures already tame.
- **Tamed without `Tame()`** (no stat, no message): creatures with `m_startsTamed` (summons), `Procreation`
  offspring (inherit the parent's state), `EggGrow` hatchlings, `Growup` adults.
- **Commands:** `Tameable.Interact` on a tamed, `m_commandable` creature calls `Command(user)`, which sends the
  `"Command"` RPC (to the owner). `RPC_Command` toggles:
  - **stay**: `SetFollowTarget(null)` + `BaseAI.SetPatrolPoint()`. This stores the current **world** position in
    `s_patrolPoint` / `s_patrol`.
  - **follow**: `ResetPatrolPoint` + `SetFollowTarget(player)` + `s_follow = player name`, and enforces
    `s_maxInstances` via `UnsummonMaxInstances`.
- **Follow restore:** `Tameable.UpdateSavedFollowTarget` (every frame, owner) re-issues follow for the player
  whose **name** matches `s_follow` when that player is loaded. Summons with `m_unsummonOnOwnerLogoutSeconds`
  unsummon otherwise; `UpdateSummon` unsummons past `m_unsummonDistance`.
- **Movement:** `BaseAI.Follow` stops within 3 m and runs beyond 10 m (`MoveTo` → `Pathfinding`).
  `BaseAI.IdleMovement` for tamed creatures random-walks around their *current position* or the patrol point.
- **Naming** (`s_tamedName`, `s_tamedNameAuthor`, privilege checked) and **saddle** (`s_haveSaddleHash`,
  `RPC_AddSaddle`, `SetSaddle`, `DropSaddle`).

### Data & persistence
- `Character` ZDO: `s_tamed`, `s_level`.
- `Tameable`: `s_tameTimeLeft`, `s_tameLastFeeding`, `s_follow`, `s_tamedName`, `s_tamedNameAuthor`,
  `s_haveSaddleHash`, `s_maxInstances`.
- `BaseAI`: `s_patrolPoint`, `s_patrol`, `s_spawnTime`.

### Multiplayer authority
All of it runs on the creature's owner. The follow target is a local `GameObject` reference, rebuilt from the
player name.

### Patch points
- `Tameable.TamingUpdate` / `DecreaseRemainingTime`: taming speed.
- `Tameable.IsHungry`: prefer patching callers, since it is small.
- `Tameable.RPC_Command`: new commands such as "board ship" or "guard".
- `Tameable.UpdateSavedFollowTarget`.
- `MonsterAI.UpdateConsumeItem` / `CanConsume`: feeding rules.
- `BaseAI.IdleMovement` / `BaseAI.Follow`: ship behaviour.

### Pets
`Pet` is a placeable pet object with `Tameable` + `Procreation` + `ItemStand` and face variants
(`MaterialVariation`). It implements `IPlaced` / `IRemoved`: when deconstructed, the face index is stored in the
player's unique key `"Pet"` and restored on the next placement (`Pet.OnRemoved` / `Pet.OnPlaced`). Such pets
already travel as items; the "bring pets" ideas below are about living tamed creatures.

---

## 7. Breeding and growth (`Procreation`, `Growup`, `EggGrow`)

### Flow (`Procreation.Procreate`, every `m_updateInterval` (10 s), owner, tamed only)
1. Resolves `m_offspringPrefab` and `m_myPrefab` by name through `ZNetScene`.
2. **Pregnant and due** (`s_pregnant` ticks + `m_pregnancyDuration` (10 s) elapsed):
   - `ResetPregnancy`.
   - Picks `m_offspring`, or `m_noPartnerOffspring` when no partner (or `m_seperatePartner` prefab) is within
     `m_partnerCheckRange`.
   - Spawns it behind the parent (`m_spawnOffset`..`m_spawnOffsetMax`, optionally random direction).
   - Creature offspring: `SetTamed(parent tamed)` and
     `SetLevel(Mathf.Max(m_minOffspringLevel, own level))`. Item offspring (eggs):
     `ItemDrop.SetQuality(same value)`.
3. **Not pregnant:**
   - Returns early when `Random.value <= m_pregnancyChance`, when alerted, or when hungry. So
     `m_pregnancyChance` (default 0.5) is effectively the chance to **skip** a tick.
   - Aborts if own-prefab + offspring-prefab count within `m_totalCheckRange` (10 m) is at least
     `m_maxCreatures` (4).
   - Counts partners within `m_partnerCheckRange` (3 m) using
     `SpawnSystem.GetNrOfInstances(..., procreationOnly: true)`, which only counts creatures where
     `Procreation.ReadyForProcreation` (tamed, not pregnant, not hungry). Self is included, so 2 or more are
     needed, or 1 or more of `m_seperatePartner`.
   - If enough partners are found, or `m_noPartnerOffspring` is set: adds a love point (`s_lovePoints`). At
     `m_requiredLovePoints` (4) it resets to 0 and calls `MakePregnant` (`s_pregnant = now`).

**Offspring level rule.** Only the pregnant parent's level is used; the partner is never identified or stored.
Both partners run the loop independently and **both can become pregnant**. For a mixed-level pair, which level
passes on is decided by which parent reaches the love-point threshold, which is effectively a coin flip.

### `Growup.GrowUpdate` (every 10 s, owner)
When `BaseAI.GetTimeSinceSpawned()` (from `s_spawnTime`) exceeds `m_growTime` (default 60 s), it instantiates
`m_grownPrefab` (or a weighted `m_altGrownPrefabs` entry), copies the tamed flag (`m_inheritTame`) and level, and
destroys itself. The **name, follow state and patrol point are not copied**.

### `EggGrow.GrowUpdate` (every `m_updateInterval` (5 s), owner)
- An egg (`ChickenEgg`, `AsksvinEgg` …) only incubates when its stack is 1, it is inside a Heat `EffectArea`
  (`m_requireNearbyFire`), and it has cover of at least `m_requireCoverPercentige` (0.7) under a roof.
- `s_growStart` stores the server time in seconds (float) and resets to 0 when conditions fail.
- On hatch it spawns `m_grownPrefab`, applies `SetTamed(m_tamed)` and `SetLevel(egg quality)`.

### Data & persistence
- `Procreation`: `s_lovePoints`, `s_pregnant`.
- `Growup`: uses `s_spawnTime`. `EggGrow`: `s_growStart`.
- Level is `s_level` via `Character.SetLevel`. It writes the ZDO without an owner check, which is fine on freshly
  spawned objects.

### Patch points
- `Procreation.Procreate` prefix/postfix. Use it as a context flag, then patch `Character.SetLevel` /
  `ItemDrop.SetQuality` to rewrite the offspring level, or transpile the single `Mathf.Max(int,int)` call.
- `Procreation.MakePregnant` postfix: record partner data at conception.
- `Growup.GrowUpdate` postfix: carry over name and follow state.
- `EggGrow.CanGrow` for incubation rules.
- The HUD only shows stars for level 2 and level 3 (`EnemyHud` toggles `level_2` / `level_3`). Levels above 3
  work but show no stars without a UI mod.

---

## 8. Fishing (`FishingFloat`, `Fish`, rod attack, bait)

### Key classes
- `FishingFloat` (`FishingRodFloat`, an `IProjectile` spawned by the rod's projectile attack).
- `Fish` (`Fish1`..`Fish12`, `Fish4_cave`; also an `ItemDrop` so it can be carried).
- The rod `FishingRod` uses baits as ammo: `FishingBait`, `FishingBaitForest`, `FishingBaitSwamp`,
  `FishingBaitPlains`, `FishingBaitOcean`, `FishingBaitMistlands`, `FishingBaitAshlands`, `FishingBaitDeepNorth`,
  `FishingBaitCave`.
- `Attack.FireProjectileBurst` spawns the float and calls `IProjectile.Setup(owner, vel, …, item, ammo)`.

### Flow
1. **Cast.** `FishingFloat.Setup` destroys the owner's previous float, stores `s_rodOwner` (the player's ZDOID
   UserID) and `s_bait` (ammo prefab name), and sets the initial line length.
2. **Bite.** In `Fish.CustomFixedUpdate` (owner), `Fish.RandomizeWaypoint(canHook)` calls `Fish.FindFloat`: any
   float within `m_range` (10) that is in water and has no catch is chosen with `m_baseHookChance` (0.5). The fish
   swims to it. When close, `Fish.TestBate` rolls the `m_baits` entry matching the bait name (`BaitSetting.m_chance`).
   It then calls `FishingFloat.Nibble`, which sends `RPC_Nibble` to the float owner. Good bait jerks the float and
   remembers the nibbler; wrong bait shows `$msg_fishing_wrongbait` and the fish ignores that float afterwards.
3. **Hook.** `FishingFloat.TryToHook` succeeds if called within 0.5 s of a nibble. It is called while reeling and
   whenever the float moves horizontally faster than 2 m/s. `SetCatch` stores `s_sessionCatchID` and calls
   `Fish.OnHooked`, which **claims fish ownership**, sets `s_hooked` and starts `Escape`. Bait is now consumed;
   otherwise `ReturnBait` refunds it.
4. **Reel** (`FishingFloat.FixedUpdate`, float owner = fisher):
   - Hooked: stamina drain `Lerp(m_hookedStaminaPerSec, …MaxSkill, skill)` per second.
   - Holding **Block** reels: stamina `(m_pullStaminaUse + fish.GetStaminaUse() × quality)`, scaled down by skill.
     The line shortens by `Lerp(m_pullLineSpeed, m_pullLineSpeedMaxSkill, skill)`, halved while the fish escapes.
   - Fishing skill is raised every 1 s of reeling (×2 when hooked).
   - Line ≤ 0.5 m: `FishingFloat.Catch`, which picks up the fish item and extra drops and updates stats.
   - Failure: out of stamina loses the fish; `distance − lineLength > m_breakDistance` (4) or
     `distance > m_maxDistance` (30) breaks the line; attacking or drawing a bow cancels.
5. **Fight.** `Fish.Escape` rolls `m_escapeMin..m_escapeMax + quality × m_escapeMaxPerLevel`, with a wiggle.
   Escapes repeat after `m_escapeWaitMin..Max`. `Fish.CustomFixedUpdate` pulls the fish toward the float with
   `Utils.Pull` while hooked.

### Data & persistence
- Float ZDO: `s_rodOwner`, `s_bait`, `s_sessionCatchID` (session key, not saved).
- Fish ZDO: `s_hooked`, `s_escape`.
- Fish quality lives on the fish `ItemDrop`.

### Multiplayer authority
The fisher owns the float and claims the fish on hook, so the **entire minigame runs on the fisher's machine**.
Other clients only see transforms and effects.

### Patch points
- `FishingFloat.FixedUpdate`: the whole reel loop. A prefix returning false while hooked replaces the minigame.
- `FishingFloat.TryToHook` / `RPC_Nibble`: bite window.
- `FishingFloat.Catch` (static): reward hook.
- `Fish.TestBate` / `FindFloat`: bait rules.
- `Fish.Escape`: difficulty.
- The Terminal debug path also calls `FishingFloat.Catch`, so reward patches apply there too.

---

## 9. Harpoon (`SE_Harpooned`) and its target filter

### Key classes
- `SE_Harpooned` (status effect).
- Chitin harpoon: `SpearChitin`, projectile `projectile_chitinharpoon`, `vfx_Harpooned`.
- Filtering happens before the status effect exists: `Projectile.IsValidTarget` and `Character.RPC_Damage`.

### Flow
1. `Projectile.OnHit` (projectile owner = attacker) → `IsValidTarget(IDestructible)`. For a character hit by a
   projectile from a player without `m_hitFriendly`, the target is valid only if `BaseAI.IsEnemy(owner, target)`,
   or the target is aggravatable, or the player has **PvP enabled**. Tamed creatures are not enemies, so the
   harpoon ignores them for non-PvP players.
2. `HitData.m_statusEffectHash` = the projectile's `m_statusEffect`. `Character.Damage` sends `RPC_Damage` to the
   target's owner. `Character.RPC_Damage` adds or refreshes the SE and calls `SetAttacker(attacker)` **before**
   damage is applied. `ApplyDamage` returns early when total damage ≤ 0.1, so `OnDamaged` / AI alert never fire
   for zero-damage hits.
3. `SE_Harpooned.SetAttacker`:
   - Bosses: breaks immediately.
   - Beyond `m_maxDistance` (30): breaks.
   - Otherwise records the base distance and links the line effect.
4. `SE_Harpooned.UpdateStatusEffect` runs on the target's owner via `SEMan.Update`:
   - If the target is not on a ship and not attached: `Utils.Pull(body, attacker pos, baseDistance, m_pullSpeed,
     m_pullForce, …, noUpForce: true)`.
   - Drains attacker stamina `m_staminaDrain × pull × target mass`.
   - Breaks when `distance − base > m_breakDistance`, or when the attacker has no stamina.
   - `IsDone` also ends it after 2 s if the attacker blocks or attacks.

### Multiplayer authority, patch points
- The attacker decides validity and damage; the target owner simulates the pull.
- Patch points:
  - `Projectile.IsValidTarget` postfix (private; `m_owner` and `m_statusEffectHash` are private fields).
  - `Character.Damage` prefix (attacker side) to strip damage and pushback.
  - `SE_Harpooned.SetAttacker` / `UpdateStatusEffect` for pull tuning.
- Melee `Attack` has its own tame filter (`Attack.DoMeleeAttack` / `DoAreaAttack`) using
  `SharedData.m_tamedOnly`, the flag behind `KnifeButcher`, "designed specifically for slaughtering tamed
  animals". It is not used by the harpoon.

---

## 10. Creatures on ships

### Key classes
`Ship`, `Character` (ground contact and ship attach), `ZSyncTransform` (parent-relative sync), `BaseAI`,
`Tameable`. See `exploration-player.md` §2 for sailing physics.

### What happens today
- **Ship volume.** `Ship.OnTriggerEnter` / `OnTriggerExit` increment `Character.InNumShipVolumes` for any
  `Character`. Only `Player`s enter `Ship.m_players`, which drives controls, `Ship.UpdateOwner` /
  `GetNewOwnerID` and `CanBeRemoved`. `Ship.UpdateOwner` moves ship ownership to the first onboard player only
  when the current owner is not aboard; the helmsman (`ShipControlls`, ZDO `s_user`) sends Forward / Backward /
  Rudder RPCs to that owner. Tames are invisible to ship logic.
- **Standing on the deck.** `Character.GetStandingOnShip` needs `InNumShipVolumes > 0`, `IsOnGround()` and a
  ground rigidbody (`m_lastGroundBody`) with a `Ship`.
- **Being carried.** `Character.ApplyGroundForce` (character owner, physics):
  - Always adds the ground body's point velocity.
  - On a ship with no move intent (`targetVel.magnitude <= 0.01`) it **attaches**: it remembers a ship-local
    offset (`m_lastAttachBody`, `m_lastAttachPos`) and pulls back to it. If the same client owns the ship this is
    a velocity correction (×10); otherwise it hard-sets `m_body.position` onto the locally interpolated ship.
  - Any move intent drops the attachment. More than 4 m of drift also drops it.
- **Network view.** With `ZSyncTransform.m_characterParentSync`, the owner writes a parent ZDOID
  (`ZDOExtraData.ConnectionType.SyncTransform`) plus `s_relPosHash`, `s_relRotHash` and `s_velRelHash` from
  `Character.GetRelativePosition`, and `ZSyncTransform.SyncPosition` on non-owners places the object relative to
  the ship. Both sides read the flag from their own prefab copy. Players have it; whether each creature prefab has
  it is *(prefab)* and must be verified.
- **AI fights the ship.**
  - `BaseAI.IdleMovement` makes a tamed creature random-walk; a "stay" command stores a **world-space** patrol
    point (`BaseAI.SetPatrolPoint`), so a staying tame walks back toward where the ship *was*, which means off
    the deck.
  - Following uses `BaseAI.MoveTo` → `FindPath` over the `Pathfinding` navmesh, built from static colliders.
    On a moving deck, paths usually fail, `StopMoving` is called, and the creature re-attaches. Near the player
    it may still try to walk.
  - The harpoon cannot pull a creature that stands on a ship (`SE_Harpooned` skips the pull).
- **Ownership mismatch.** The ship owner is one onboard player. The creature owner is whoever the server
  assigned (`ZDOMan.ReleaseNearbyZDOS`). When they differ, the creature is simulated against a lagging copy of the ship,
  which causes jitter and slide-off.

### Patch points
- `BaseAI.IdleMovement` / `BaseAI.Follow` (freeze or stay-on-deck logic when `GetStandingOnShip() != null`).
- `BaseAI.SetPatrolPoint` / `GetPatrolPoint` (store the patrol point ship-local).
- `Character.ApplyGroundForce` (stronger attach while the ship moves).
- `Ship.CustomFixedUpdate` postfix (ship owner claims creature ZDOs onboard).
- `ZNetScene.Awake` (set `ZSyncTransform.m_characterParentSync` on tameable prefabs).
- `Tameable.RPC_Command` (a "board" command).

---

## 11. Portals, teleports and creatures

### Key classes
- `TeleportWorld` (`portal_wood`, `portal_stone`, legacy `portal`), `TeleportWorldTrigger`.
- `Teleport`: dungeon entrances.
- `Player.TeleportTo` / `Player.UpdateTeleport`.
- `Character.TeleportTo`: virtual, **returns false**; only `Player` overrides it.
- `Inventory.IsTeleportable`, `Game.ConnectPortals` (server pairing), `ZDOMan.GetPortals`.

### Flow
- **Pairing.** `TeleportWorld.RPC_SetTag` (owner) writes `s_tag` / `s_tagauthor` and clears the connection.
  `Game.ConnectPortalsCoroutine` (started in `Game.Start` only when `ZNet.instance.IsServer()`) → `ConnectPortals`
  pairs unconnected portals with equal tags via `ZDOExtraData.ConnectionType.Portal`.
- **UI hint.** `TeleportWorld.UpdatePortal` (0.5 s) shows the "target found" effect when the closest player
  within `m_activationRange` is teleportable.
- **Entering.** `TeleportWorldTrigger.OnTriggerEnter` → `TeleportWorld.Teleport(Player)`, **local player only**.
  It checks, in order:
  - `TargetFound` (the connected ZDO must be known locally; otherwise `RequestZDO` is sent)
  - `GlobalKeys.NoPortals`
  - `GlobalKeys.NoBossPortals` + active boss
  - `player.IsTeleportable(m_allowAllItems)`: items with `m_toolTier >= 1000` always block; otherwise
    `m_teleportable == false` items block unless `m_allowAllItems` or `GlobalKeys.TeleportAll`

  The destination is the connected portal ZDO position + `forward × m_exitDistance` + up; then
  `player.TeleportTo(pos, rot, distantTeleport: true)`.
- **`Player.TeleportTo`** (owner; a non-owner sends `RPC_TeleportTo`): needs a 2 s cooldown, then sets
  `m_teleporting`.
- **`Player.UpdateTeleport`:**
  - After 2 s it moves the player to the target.
  - For a distant teleport it waits at least 8 s, until `ZNetScene.IsAreaReady` and `ZoneSystem.FindFloor`.
  - After 15 s it gives up and snaps to the solid height. Short teleports revert with `$msg_portal_blocked`.
- **Creatures.** Nothing moves creatures. Tames stay behind and their zone unloads, but `s_follow` keeps the
  player name, so they re-follow when the player returns (`Tameable.UpdateSavedFollowTarget`). Summons unsummon
  (`m_unsummonDistance`, logout timer).
- `Teleport` (dungeon doors) calls `character.TeleportTo(..., distantTeleport: false)`, which is a no-op for
  non-players.

### Multiplayer authority
The teleport is decided and executed entirely by the teleporting player's client. Moving a creature requires
owning its ZDO (`ZNetView.ClaimOwnership` → `ZDO.SetOwner(ZDOMan.GetSessionID())`).

### Patch points
- `TeleportWorld.Teleport` prefix/postfix (per-portal rules; know which portal via `Utils.GetPrefabName`).
- `Player.TeleportTo` postfix when it returns true (start of teleport).
- `Player.UpdateTeleport` postfix detecting `m_teleporting` true→false (arrival; area ready).
- `Inventory.IsTeleportable`.

---

## 12. Minimap icons for objects and creatures

### Key classes
`Minimap` (`PinData`, `PinType`, `AddPin`, `RemovePin`, `UpdatePins`, `UpdateDynamicPins`, `Explore`). See
`ux.md` §8 for the full pin model.

### Facts relevant to farming
- Vanilla has **no pins for creatures, tames or gatherables.** Dynamic pins are only profile pins (spawn and
  death), shouts, pings, other public players, locations, events and persistent event areas
  (`Minimap.UpdateDynamicPins`).
- `Minimap.AddPin(pos, type, name, save, isChecked, ownerID, author)` is public.
  - It rejects types outside the `PinType` enum (`Icon0..Icon4`, `Death`, `Bed`, `Shout`, `None`, `Boss`,
    `Player`, `RandomEvent`, `Ping`, `EventArea`, `Hildir1-3`, `Memorial`).
  - It sets `m_icon = GetSprite(type)`, which is null for `None`. **Overwrite `pinData.m_icon` after `AddPin`**
    to use any sprite, for example an item icon from `ItemDrop.ItemData.GetIcon()`.
  - Use `save: false` for transient pins; move them by writing `m_pos`.
- `Minimap.UpdatePins` rebuilds UI elements. It hides pins filtered by `m_visibleIconTypes[(int)type]` and honours
  `m_doubleSize`, `m_animate` (pulse) and `m_worldSize` (area circle).
- Explore: `Minimap.UpdateExplore` reveals fog every `m_exploreInterval` (2 s) within `m_exploreRadius`
  (100 m *(prefab)*). This is the "discovery radius" users know.
- Sources for scanning:
  - `Character.GetAllCharacters()` and `BaseAI.BaseAIInstances`: static lists of loaded creatures.
  - Pickables: a self-built registry (see §3).
  - `ZDOMan.instance.GetAllZDOsWithPrefabIterative`, or ZDO sector queries, for unloaded objects.

---

## 13. Food (`ItemDrop.ItemData.SharedData`, `Player` food slots)

### Data model (`SharedData`)
- `m_food` (health), `m_foodStamina`, `m_foodEitr`, `m_foodBurnTime` (s), `m_foodRegen` (heal per 10 s tick),
  `m_foodEatAnimTime`, `m_isDrink`, `m_consumeStatusEffect` (meads and feast buffs).
- `m_itemType` must be `Consumable` to be eaten (`Humanoid.CanConsumeItem`).
- Helpers: `ObjectDB.GetAllFoodItems`, `ObjectDB.GetAllCreatableFood`.

### Flow
- `Player.ConsumeItem` → `CanConsumeItem`:
  - consumable type
  - world-level check (`item.m_worldLevel < Game.m_worldLevel` fails when `checkWorldLevel`)
  - `CanEat`
  - no active SE with the same name or category

  Then it adds `m_consumeStatusEffect` and calls `EatFood`.
- `Player.CanEat`: maximum **3** foods (`m_maxFoods` const). The same food can be re-eaten, and any food
  replaced, only when `Food.CanEatAgain()` (remaining time < half of `m_foodBurnTime`).
- `Player.EatFood`: refreshes an existing entry of the same food, adds a new slot, or replaces the most depleted
  re-eatable food.
- `Player.UpdateFood`:
  - Every 1 s (× `Game.m_foodRate`) time decreases.
  - Current value = `base × clamp01(t / burnTime)^0.3`, so a food stays near full for most of its duration.
  - Totals: `Player.GetTotalFoodValue` = `m_baseHP` / `m_baseStamina` + sum → `SetMaxHealth` / `SetMaxStamina` /
    `SetMaxEitr`.
  - Every 10 s it heals the sum of `m_foodRegen` × SE regen modifiers.
- Persistence: `Player.Save` writes each food's prefab name + remaining time. `Player.Load` rebuilds the entry
  from the **prefab's** `m_itemData`. A missing prefab is skipped with a warning, and per-instance data on the
  eaten item (`m_customData`, quality) is gone after a relog. A "cooking quality" bonus must therefore be stored
  separately, for example in `Player.m_customData`.

### Authority, patch points
- Local player only.
- Patch points: `Player.EatFood` (post-eat effects, "well fed" buffs), `Player.UpdateFood` (decay curve),
  `Player.GetTotalFoodValue` (stat scaling, cooking-quality bonuses), `Player.CanEat` (slot count),
  `Player.CanConsumeItem`.

---

## 14. CookingStation (cooking spit, iron cooking station, stone oven, and friends)

### Key classes
`CookingStation` (`piece_cookingstation`, `piece_cookingstation_iron`, `piece_oven`), `Switch` (separate food and
fuel switches on the oven), `EffectArea` (fire detection).

### Flow
- **`CookingStation.UpdateCooking`** (every 1 s):
  - "lit" = (`m_requireFire` and `IsFireLit()`) or (`m_useFuel` and fuel > 0 and (`m_useFueldWhileEmpty` or it
    has uncooked items)).
  - `IsFireLit` checks the `EffectArea.Type.Burning` area at `m_fireCheckPoints`.
  - The owner adds `GetDeltaTime()` (server time since `s_startTime`, **catches up**) to each slot. At
    `m_cookTime` an item becomes `Done` (`m_to`); at `2 × m_cookTime`, if `m_canOvercookItems`, it becomes
    `Burnt` (`m_overCookedItem`, e.g. coal).
  - Fuel burns `dt / m_secPerFuel`.
- **Add food:** `Interact` / `UseItem` → `OnUseItem` (fire and free slot checks) → `CookItem`, which claims
  ownership if nobody owns the station, removes 1 item and calls `RPC_AddItem(prefabName, cheated)` on the owner.
  The owner fills a free slot and broadcasts `RPC_SetSlotVisual`. Cooking skill +0.4.
- **Take food:** `OnInteract` with a done item:
  - Cooking skill +0.6.
  - Client-side bonus roll `Random < skillFactor × InventoryGui.m_craftBonusChance` (0.25) → +`m_craftBonusAmount`.
  - Then `RPC_RemoveDoneItem(userPos, amount)` on the owner, which runs `SpawnItem` per unit (optionally records
    the crafter).
- **Oven fuel:** `OnAddFuelSwitch` → `RPC_AddFuel`. Destroying the station drops fuel and every slot
  (`DropAllItems` on the owner via `WearNTear.m_onDestroyed`).

### Data & persistence
- String keys per slot: `"slot{i}"` holds **both** a string (item prefab name) and a float (cooked seconds),
  separate typed maps under the same hash. `"slotstatus{i}"` is an int (`NotDone` / `Done` / `Burnt`);
  `s_cheatedQueued + i` holds the cheated flag.
- `s_fuel` (float) and `s_startTime` (last update ticks).
- Recipes are `m_conversion` (`ItemConversion { m_from, m_to, m_cookTime }`) *(prefab)*.

### Patch points
- `CookingStation.UpdateCooking`: cook speed. Scale `GetDeltaTime` via a postfix on the private `GetDeltaTime`.
- `IsItemAllowed` / `GetItemConversion`: dynamic recipes.
- `OnInteract`: bonus yield. Note the bonus is rolled on the client.
- `SpawnItem`: quality tagging through `m_customData`.
- `IsFireLit`: alternative heat sources.
- New stations are best made by cloning a vanilla prefab and replacing `m_conversion`.

---

## 15. Fermenter

### Flow
- **`Fermenter.Interact`:**
  - Forces a cover update and checks the ward.
  - Empty barrel: needs a roof (`Cover.GetCoverForPoint` underRoof) and not "exposed" (cover ≥ 0.7). `AddItem`
    removes one item and calls `RPC_AddItem(nameHash, cheated)` on the owner, which sets `s_content` and
    `s_startTime`.
  - Ready barrel: `RPC_Tap` (owner) clears the content, then after `m_tapDelay` (1.5 s) `DelayedTap` spawns
    `m_producedItems` (default 4) of `m_to`.
- **`Fermenter.GetStatus`:** `Empty` if content is 0; `Ready` if `now − s_startTime > m_fermentationDuration`
  (default 2400 s *(prefab)*); otherwise `Fermenting`.
- **`Fermenter.UpdateCover`** (every 10 s): when exposed or without a roof, the owner calls
  `ResetFermentationTimer`, which **restarts** fermentation from zero rather than pausing it.

### Data, authority, patch points
- ZDO keys: `s_content` (prefab-name hash), `s_startTime`, `s_cheatedQueued`. Owner-authoritative.
- Patch points: `Fermenter.GetStatus` (duration scaling), `Fermenter.UpdateCover` / `ResetFermentationTimer`
  (pause instead of reset), `Fermenter.DelayedTap` (yield).

---

## 16. Feasts (1.0)

### Key classes
- `Feast` (a placed piece with N portions).
- The **Serving Tray** tool (`Feaster` prefab, localized `$item_feaster`, piece table `_FeasterPieceTable`). It
  places feasts, and ordinary food items as decorative pieces through `ItemDrop.MakePiece` (ZDO `s_piece`).
- `Piece.DropResources` (refund).
- Prefabs: `FeastMeadows`, `FeastBlackforest`, `FeastSwamps`, `FeastMountains`, `FeastPlains`, `FeastMistlands`,
  `FeastAshlands`, `FeastDeepNorth`, `FeastOceans`, each with a companion `<Name>_Material` prefab (probably the
  craftable item consumed when the feast is placed; verify with a dump).
- Localized names include "Whole Roasted Meadow Boar" and "Northern Morning Fare".

### Flow
1. Placement: the Serving Tray piece table. `Piece.FreeBuildKey` returns `GlobalKeys.NoCraftCost` for pieces that
   are `ItemDrop`s or `Feast`s. In `Player.UpdatePlacement`, removal is allowed for feasts and placed items only
   by a tool with `PieceTable.m_canRemoveFeasts`; a tool with `m_canRemovePieces` (hammer) cannot remove them.
2. `Feast.Interact` (client):
   - Checks `m_useDistance` (2 m), stack > 0 and `Player.CanConsumeItem(m_foodItem, checkWorldLevel: true)`.
   - Calls `RPC_TryEat` on the owner, which decrements `s_value` (sets -1 when empty) and replies:
     - `RPC_OnEat` to everybody (effect + `UpdateVisual`)
     - `RPC_EatConfirmation` to the eater: applies `m_consumeStatusEffect`, then `Player.EatFood(m_foodItem.m_itemData)`
3. Visuals: `FeastLevel` thresholds on `GetStackPercentige()`.
4. Dismantling refunds `floor(amount × stack%)`, so a partly eaten feast returns little or nothing
   (`$item_feaster_remove_description`).

### Unlocking feasts
1. The feast **item** recipe is discovered like any recipe (see §18).
2. The feast **piece** appears in the Serving Tray when `Player.UpdateKnownRecipesList` finds every resource of
   that piece in `m_knownMaterial` (`HaveRequirements(piece, RequirementMode.IsKnown)`). `AddKnownPiece` then
   shows `$msg_newdish` for the Food / Meads / Feasts categories.

Nothing is tied to biomes: all gating comes from station level and "known material" ingredients. Which station
and level each feast recipe uses is *(prefab)*. Community sources point at the Food Preparation Table
(`piece_preptable`); confirm with a dump.

### Patch points
- `Feast.RPC_EatConfirmation` (extra buffs).
- `Feast.RPC_TryEat` (portion rules).
- `Feast.GetStack` / `m_eatStacks` (portion count).
- Recipe data at `ObjectDB.Awake` (unlock rules).

---

## 17. Cauldron and food crafting stations

### Key classes
`CraftingStation` and `StationExtension` (generic; full detail in `building-crafting.md` §7), `InventoryGui.DoCrafting`.

### Food stations (verified prefabs)
- `piece_cauldron` (Cauldron) with extensions `cauldron_ext1_spice` (Spice Rack), `cauldron_ext3_butchertable`
  (Butcher's Table), `cauldron_ext4_pots` (Pots and Pans), `cauldron_ext5_mortarandpestle` (Mortar and Pestle),
  `cauldron_ext6_rollingpins` (Rolling Pins and Cutting Boards), `cauldron_ext7_smoker` (Smoker).
- `piece_preptable` (Food Preparation Table).
- `piece_MeadCauldron` (Mead Ketill).
- `BogWitch_Cauldron` (at the Bog Witch's location).
- Which of these carry a `CraftingStation` and which recipes point at them is *(prefab)*; confirm with a dump.

### Rules
- Station level = `1 + extensions`. `StationExtension.FindExtensions` counts extensions within each extension's
  `m_maxStationDistance` (default 5) whose `m_craftingStation.m_name` matches, de-duplicated by piece name unless
  `m_stack`. The list refreshes every 2 s (`CraftingStation.GetExtensions`).
- `CraftingStation.CheckUsable`: `m_craftRequireRoof` (under roof and cover ≥ 0.7) and `m_craftRequireFire`
  (`CheckFire` → `m_haveFire`).
- Known station level: `CraftingStation.UpdateKnownStationsInRange` (from `Player.UpdateStations`, every 1 s,
  within `m_discoverRange` (4 m)) and `Player.PlacePiece` both call `Player.AddKnownStation`, which re-runs
  `UpdateKnownRecipesList` when the level rises.
- Cooking bonus in `InventoryGui.DoCrafting`: only when the station has a `m_craftingSkill` and the result stacks
  (`m_maxStackSize > 1`). Each crafted unit rolls `skillFactor × m_craftBonusChance`.
  - Quirk: with multi-craft, the bonus accumulates cumulatively (`num3 += num4` with a growing `num4`).
- Craft time shrinks with skill by up to `m_craftDurationSkillMaxDecrease` (0.6).

---

## 18. Recipe discovery and unlock logic (food focus)

### Key classes
`Player` (`m_knownRecipes`, `m_knownMaterial`, `m_knownStations`, `m_knownBiome`), `Recipe`, `SeasonalItemGroup`.
General rules: `building-crafting.md` §8.

### Flow
- **Knowledge sources:**
  - `Player.AddKnownItem`: an item is picked up for the first time; the shared name goes into `m_knownMaterial`,
    then `UpdateKnownRecipesList` runs.
  - `Player.AddKnownStation`: station name → max level seen.
  - `Player.AddKnownBiome`: `BiomeSector.GetName()` strings, from `Player.UpdateBiome` every 1 s.
  - `UpdateKnownRecipesList` also runs on spawn and when the inventory changes.
- **`Player.UpdateKnownRecipesList`:**
  - For each `ObjectDB.m_recipes` entry that is `m_enabled` (or in the current seasonal group), not yet known, and
    passes `HaveRequirements(recipe, discover: true, 0)`: calls `AddKnownRecipe`. It stores the *item shared
    name* and shows a message.
  - Then, for each piece in owned piece tables, `HaveRequirements(piece, IsKnown)` → `AddKnownPiece`.
- **`HaveRequirements(recipe, discover: true)`:** the station must be known at `m_minStationLevel`
  (`KnowStationLevel`), the DLC present, and every required ingredient with `m_amount > 0` must be in
  `m_knownMaterial`. With `m_requireOnlyOneIngredient`, any one ingredient is enough.
- **Crafting:** `HaveRequirements(recipe, discover: false)` needs the station in use at level
  `m_minStationLevel + quality − 1` (`RequiredCraftingStation`) plus the items in the inventory.
- **Persistence:** all four sets live in the player profile (`Player.Save`). Known entries never disappear, even
  if a mod that caused them is removed.

### Patch points
- `Player.UpdateKnownRecipesList` postfix: add extra unlock conditions such as biome.
- `Player.HaveRequirements(Recipe, bool, int, int)` postfix, gated on `discover`.
- `Player.AddKnownBiome` postfix: react to entering a biome; `AddKnownRecipe` is private, call it via AccessTools.
- Recipe data (`m_minStationLevel`, `m_resources`, `m_craftingStation`) edited in an `ObjectDB.Awake` /
  `ObjectDB.CopyOtherDB` postfix, or through Jotunn `ItemManager.OnItemsRegistered`.

---

## Feature ideas

Summary of the verdicts below. "Needs mod" means who must install it for correct multiplayer behaviour.

| Idea | Scope | Feasibility | Needs mod | Custom assets |
|---|---|---|---|---|
| Breeding revamp | Revamp | easy | everyone | no |
| Ashlands trees | Revamp | medium | everyone | no (optional) |
| Easy plant | QoL (exists) | medium | client-only | no |
| Plant "everything" | QoL (exists) | medium | everyone | no (icons reuse) |
| Unlock biome feast in the biome | QoL | easy | depends (data: synced config) | no |
| Harpoon works on tamed animals | QoL | easy | client-only (harpooner) | no |
| Hunting (minimap tracking) | New | easy | client-only | no (optional icons) |
| Sap collector on other trees | New | medium | everyone | yes (item icons) |
| Better fishing | New | medium | client-only | yes (UI art, optional) |
| Cooking equipment | New | hard (phase 1 alone: medium) | everyone | yes |
| Tamed / pets on ships | New | hard | everyone | no |
| Tamed / pets through stone portal | New | medium | client-only (teleporting player) | no |

### Farming — Breeding revamp (Revamp)
*Now: the offspring takes the pregnant parent's level (a coin flip between parents). Target: the offspring takes
the lower parent's level, with a chance of +1.*

- **Feasibility:** easy.
- **Who needs the mod:** everyone. `Procreation.Procreate` runs on whichever client owns the parent's ZDO. A
  vanilla owner would still breed the vanilla way.
- **Hooks:**
  - `Procreation.MakePregnant` (postfix): record the partner.
  - `Procreation.Procreate` (prefix/postfix): set and clear a `[ThreadStatic]` "birth context".
  - `Character.SetLevel` (prefix: rewrite the level inside the context).
  - `ItemDrop.SetQuality` (same, for eggs).
  - `SpawnSystem.GetNrOfInstances` semantics (partner = same prefab or `m_seperatePartner`, `ReadyForProcreation`).
  - `BaseAI.BaseAIInstances` for the partner scan.
- **Sketch:**
  - In a `MakePregnant` postfix, scan `BaseAI.BaseAIInstances` within `m_partnerCheckRange` for the nearest ready
    partner (same prefab, or `m_seperatePartner`). Store its level in a custom ZDO int
    (`"vm.breeding.partnerLevel"`) on the pregnant parent.
  - At birth, replace `max(minOffspringLevel, ownLevel)` with
    `max(minOffspringLevel, min(ownLevel, partnerLevel ?? ownLevel)) + (Random < chance ? 1 : 0)`, clamped to a
    configurable max (default 3, because `EnemyHud` shows stars only for levels 2 and 3).
  - Optionally carry the name through `Growup.GrowUpdate`.
- **Risks:**
  - Pregnancies started before install have no stored partner level (fall back to own level).
  - Vanilla quirk: both parents can become pregnant, so pairs make two babies. Keep it, or suppress the partner's
    pregnancy.
  - Conflicts with mods that rewrite breeding or levels: AllTameable-style mods, CreatureLevelAndLootControl-style
    star mods (level caps above 3).
  - The `Character.SetLevel` patch must be strictly scoped to the context flag, or it will affect world spawns.

### Farming — Ashlands trees (Revamp)
*Safe inside a shield; outside it, crops burn out and trees turn into scorched trees.*

- **Feasibility:** medium.
- **Who needs the mod:** everyone. Exposure is judged by whoever owns the plant, crop or tree ZDO.
- **Hooks:**
  - `ZNetScene.Awake` (postfix): add an `AshExposure` component to every crop `Pickable` reachable from
    `Plant.m_grownPrefabs`, and to their `TreeBase` results (`Beech1`, `Birch1/2`, `Oak1`, `PineTree`, `FirTree`,
    … read from the saplings rather than hard-coded).
  - `Plant.UpdateHealth` (postfix, for saplings).
  - `ShieldGenerator.IsInsideShield`, `Heightmap.FindBiome`, `ZNetView.IsOwner`, `ZNetScene.Destroy`.
  - `TreeBase` / `Pickable` for the replacement.
  - Existing scorched visuals: `AshlandsTree1`..`AshlandsTree6`, `AshlandsTreeStump1..3`.
- **Sketch:**
  - The component ticks every 10-20 s on the owner and only does work when `FindBiome(pos) == AshLands` and a grace
    period (at least 20 s after load) has passed.
  - Outside every shield it accumulates exposure in a custom ZDO float. Past a threshold, a crop plays a burn
    effect and is destroyed (optionally dropping `Coal`). A tree instantiates a configured scorched prefab at the
    same position, rotation and scale, then destroys itself.
  - Saplings that stay `TooHot` past the same threshold are destroyed with an effect instead of stalling forever.
- **Risks:**
  - `IsInsideShield` only sees loaded generators and reads the *animated* radius, which is 0 right after load or
    when fuel runs out. Without a grace period and hysteresis, farms flicker or die on login.
  - Losing a whole farm because a shield ran dry is harsh: add warnings (hover text via a `Plant.GetHoverText`
    postfix) and a config.
  - Vanilla cinder fire already burns trees outside shields.
  - Interacts with PlantEverything-style saplings: include their prefabs via the same `m_grownPrefabs` scan.

### Farming — Easy plant (QoL, already exists)
*Plant many crops at once, snapped to correct spacing.*

- **Feasibility:** medium. Already exists: Advize's "PlantEasily" is the reference mod, so prefer compatibility
  over a rewrite.
- **Who needs the mod:** client-only. Extra plants are ordinary `PlacePiece` instantiations that vanilla clients
  load normally.
- **Hooks:**
  - `Player.UpdatePlacementGhost` (postfix: spawn and validate extra ghosts in a grid, spacing from
    `Plant.m_growRadius`).
  - `Player.TryPlacePiece` / `Player.PlacePiece` (place the extras).
  - `Player.ConsumeResources` / `HaveRequirements(piece, CanBuild)` (charge per plant).
  - `Plant.HaveGrowSpace` rules, `Heightmap.IsCultivated`.
  - `Pickable.Interact` (optional replant-on-harvest).
- **Sketch:**
  - While a cultivator piece with a `Plant` component is selected, compute an N×M grid of positions aligned to the
    ghost's rotation, spaced `2 × m_growRadius`.
  - Validate each point with the same checks `UpdatePlacementGhost` uses (cultivated, biome, space). On place, call
    `PlacePiece` for each valid point and consume resources once per plant.
  - Optional: after harvesting a crop, replant the matching sapling if the player carries the seed.
- **Risks:**
  - Duplicates PlantEasily: conflicts if both are installed.
  - Grid ghosts cost performance (one ghost per cell).
  - The vanilla 1.0 Scythe already covers mass *harvest* (`Piece.m_harvest`), so skip that part.

### Farming — Plant "everything" (QoL, already exists)
*Plant berries, mushrooms, thistle, trees and more with the cultivator.*

- **Feasibility:** medium. Already exists: Advize's "PlantEverything".
- **Who needs the mod:** everyone. New piece prefabs must be registered in every client's `ZNetScene`, or the ZDOs
  cannot be instantiated.
- **Hooks:**
  - `ZNetScene.Awake` / `ObjectDB.Awake` (register cloned prefabs).
  - `_CultivatorPieceTable` `PieceTable.m_pieces`.
  - `Piece` (placement flags `m_cultivatedGroundOnly`, `m_onlyInBiome`, `m_resources`).
  - `Plant` (sapling stage with `m_grownPrefabs = Pickable_*`).
  - `Pickable` (respawn).
  - Jotunn `PieceManager` / `PrefabManager` if using Jotunn.
- **Sketch:**
  - For each target (for example `RaspberryBush`, `BlueberryBush`, `CloudberryBush`, `Pickable_Mushroom*`,
    `Pickable_Thistle`, `Pickable_Dandelion`, tree saplings), clone a vanilla sapling prefab. Give it the target as its only
    `m_grownPrefabs` entry, a `Piece` with the seed item as resource, and a biome mask.
  - Add it to the cultivator table.
  - Bushes whose pickable respawns can be placed directly as pieces.
- **Risks:**
  - Save compatibility: uninstalling leaves ZDOs with unknown prefab hashes.
  - Duplicates PlantEverything.
  - Balance, for example Ashlands or Mistlands plants anywhere.
  - Interaction with the Ashlands revamp (saplings must be heat-checked).

### Cooking — Unlock biome feast in the biome, not after (QoL)
*Each feast becomes available while the player is in its biome, not one biome later.*

- **Feasibility:** easy.
- **Who needs the mod:** depends. Recipe discovery and crafting are evaluated only on the client (the server never
  validates crafting), so it works client-only. For a fair multiplayer game, ship it with a server-synced config
  (ServerSync or Jotunn synced config) so everyone sees the same recipes.
- **Hooks:**
  - `ObjectDB.Awake` / `ObjectDB.CopyOtherDB` (postfix: patch the feast `Recipe` data, `m_minStationLevel`,
    `m_resources`).
  - `Player.AddKnownBiome` (postfix: early unlock).
  - `Player.UpdateKnownRecipesList` / `Player.HaveRequirements(Recipe, discover: true)`.
  - Private `Player.AddKnownRecipe` via AccessTools.
  - Serving Tray piece unlock (`AddKnownPiece` via `HaveRequirements(piece, IsKnown)`).
- **Sketch:**
  - First dump every `Feast*` recipe (station, `m_minStationLevel`, ingredients) to see what gates each feast
    behind the next biome: station level or a next-biome ingredient.
  - Add a config table `feastPrefab → {biome, stationLevel, ingredient overrides}` applied to the Recipe objects at
    `ObjectDB.Awake`.
  - Optionally, a `Player.AddKnownBiome` postfix marks the mapped feast recipe known on first entry to its biome,
    so it shows in the list immediately.
- **Risks:**
  - Pure data change, so desync-free. Mismatched configs only make clients see different recipe lists.
  - Known recipes persist in the profile after uninstall (harmless).
  - Other recipe-editing mods (for example WackyDB-style data mods, or Jotunn mods that re-register recipes) may
    overwrite or be overwritten depending on load order: apply late, in a postfix, keyed by prefab name.

### Farming — Harpoon works on tamed animals (QoL)
*Use the chitin harpoon to drag your own tames without hurting them.*

- **Feasibility:** easy.
- **Who needs the mod:** client-only (the harpooner). The attacker decides the valid target and builds the
  `HitData`. The tame's owner applies the SE and simulates the pull with vanilla code.
- **Hooks:**
  - `Projectile.IsValidTarget` (private; postfix).
  - `Character.Damage` (prefix, attacker side).
  - `SE_Harpooned.SetAttacker` / `UpdateStatusEffect` (optional tuning).
  - `ObjectDB.GetStatusEffect(hash)`, to identify the harpoon SE by type rather than by name.
- **Sketch:**
  - Postfix `Projectile.IsValidTarget`: when vanilla rejected a target that is `IsTamed()`, the owner is the local
    player, and the projectile's status effect resolves to an `SE_Harpooned`, return true.
  - Prefix `Character.Damage` on a tamed target hit by that SE: zero `hit.m_damage`, `m_pushForce` and
    `m_staggerMultiplier`. `ApplyDamage` then exits early, so no damage, no alert and no AI retaliation, while the
    SE is still applied in `RPC_Damage`.
- **Risks:**
  - PvP players can already harpoon tames but also damage them: apply the zero-damage rule regardless of PvP.
  - Lox-sized mass drains a lot of stamina (`m_staminaDrain × mass`); consider a config multiplier.
  - The pull is disabled when the tame stands on a ship; `noUpForce` means you cannot lift a tame onto a deck.
  - EpicLoot and other mods that patch projectile hits: keep the patch additive (postfix only).

### Farming — Hunting (New)
*Shows gatherables and animals nearby on the minimap, capped to the 100 m discovery radius.*

- **Feasibility:** easy (public Minimap API, client-side scan; most of the work is filtering and pooling).
- **Who needs the mod:** client-only.
- **Hooks:**
  - `Minimap.AddPin` / `RemovePin` (dynamic pins, `save: false`, custom `m_icon`).
  - `Minimap.m_exploreRadius` (the 100 m cap).
  - `Character.GetAllCharacters()` (animals and tames).
  - `Pickable.Awake` (postfix: own registry) + `Pickable.CanBePicked` (availability).
  - `PickableItem`; `Tameable` / `Character.IsTamed()` for tame styling.
  - Optionally a trigger: a status effect, trinket or skill gate via `SEMan`.
- **Sketch:**
  - A client-side tracker updates every 0.5-1 s.
  - It gathers loaded non-player characters (filter by faction or config lists: huntable animals, tames) and
    available pickables (`CanBePicked()` and not in a blacklist, e.g. the player's own planted crops
    `Pickable_Barley` versus wild `Pickable_Barley_Wild`) within `min(config radius, m_exploreRadius)` of the
    player.
  - It keeps one pooled `PinData` per object and updates `m_pos`. Icons: `ItemDrop.ItemData.GetIcon()` of the
    pickable's `m_itemPrefab`; for creatures, the trophy item from `CharacterDrop`, or a generic sprite.
  - Pins for objects that left range or were picked are removed.
  - "New" scope could gate it behind a Hunting status effect or trinket, with a radius that scales with a skill.
- **Risks:**
  - Pin UI churn: `Minimap.UpdatePins` rebuilds only on change, so move pins in place instead of re-adding.
  - Keep counts bounded, to protect performance in dense forests.
  - Only loaded objects are known (the active area is about zone radius), which matches the 100 m cap anyway.
  - Conflicts with pin-manager mods that assume all pins are saved or user-made (Pinnacle, AutoMapPins-style):
    use `save: false` and a distinct `PinType`.
  - Some players may consider it a cheat on servers: offer a server-synced "disable" flag.

### Farming — Sap collector on other trees (New)
*Tap ordinary trees for new cooking and crafting items (birch syrup, pine resin, and so on).*

- **Feasibility:** medium.
- **Who needs the mod:** everyone. A new piece and new items must exist on every client, and the collector owner
  produces the output.
- **Hooks:**
  - `ZNetScene.Awake` / `ObjectDB.Awake` (register the tapper piece and items).
  - `Piece.m_mustConnectTo` / `Player.UpdatePlacementGhost` (placement against a `TreeBase` instead of
    `YggdrasilRoot`).
  - `SapCollector.UpdateTick` / `RPC_Extract`, or a custom `TreeTap` component modelled on it.
  - `ResourceRoot` (optional).
  - `TreeBase` (connection target and prefab name → output mapping).
  - `Recipe` for uses of the new items.
- **Sketch:**
  - Clone `piece_sapcollector` into `piece_treetap`. Clear `m_mustConnectTo`, and use an `UpdatePlacementGhost`
    postfix that requires a `TreeBase` collider within 0.3 m.
  - Replace `SapCollector` on the clone with a `TreeTap` component that copies the time-accumulation pattern
    (`s_lastTime`, `s_product`, `s_level`).
  - It picks its output from a config map `tree prefab → item` (`Birch1/2` → birch syrup, `PineTree` /
    `FirTree` → resin, `Oak1` → oak tannin, `AshlandsTree*` → ember sap, …) and stops producing when the tree is
    gone.
  - Avoid adding `ResourceRoot` to every tree prefab: that is thousands of 10 s `InvokeRepeating` timers.
- **Risks:**
  - Custom items need icons. Tinting the vanilla Sap icon at runtime is possible, but real art is better.
  - Uninstalling leaves unknown piece and item hashes.
  - Tree chopping destroys the connection: handle the piece's support or drop it.
  - Balance with vanilla `Sap` (a Mistlands resource).

### Farming — Better fishing (New)
*"It sucks, pick another minigame."*

- **Feasibility:** medium.
- **Who needs the mod:** client-only. The float is owned by the fisher and the fish is claimed on hook, so the
  whole loop is local (§8).
- **Hooks:**
  - `FishingFloat.FixedUpdate` (prefix: take over while `GetCatch() != null`).
  - `FishingFloat.TryToHook` / `RPC_Nibble` (bite window, strike input).
  - `FishingFloat.Catch` (reward).
  - `Fish.Escape` / `Fish.GetStaminaUse` / fish `ItemDrop` quality (difficulty inputs).
  - `Skills.SkillType.Fishing` (`RaiseSkill`, `GetSkillFactor`).
  - `Hud` / a custom Canvas for the UI.
- **Sketch:**
  - Keep the vanilla cast, bait and bite logic.
  - Once hooked, show a tension or reel minigame (for example "keep the marker in the moving fish zone", with
    width and fish speed from quality, bait and skill). Drive `m_lineLength` from minigame progress and skip the
    vanilla block-to-reel branch.
  - Success reproduces the vanilla success path (`Catch(fish, owner)`, `SetCatch(null)`, `fish.OnHooked(null)`,
    destroy the float). Failure calls `SetCatch(null)` with the "lost" message.
  - Keep stamina drain as the fail pressure.
- **Risks:**
  - `SetCatch` and several fields are private: use AccessTools or reflection helpers.
  - Gamepad and keyboard input (`ZInput`) must not trigger block or attack, which would cancel the float.
  - The UI can be built procedurally (no bundle) for a prototype; a polished version wants sprites.
  - Fishing overhaul mods and ValheimPlus-like fishing tweaks patch the same `FixedUpdate`.

### Cooking — Cooking equipment (New)
*No description given. Proposed scope: new cooking gear (utensils and stations) plus wearable cook's gear.*

- **Feasibility:** hard for the full scope. Phase 1 alone (new stations from cloned prefabs) is medium.
- **Who needs the mod:** everyone (new prefabs and items; station owners run the cooking).
- **Hooks:**
  - Stations: clone `piece_cookingstation` / `piece_oven` and edit `CookingStation.m_conversion`, `m_slots`,
    `m_requireFire` / `m_useFuel`.
  - Cauldron upgrades: new `StationExtension` pieces pointing at `piece_cauldron`, which raise
    `CraftingStation.GetLevel`.
  - Gear effects: `CookingStation.OnInteract` (bonus yield, client), `CookingStation.RPC_AddItem` (tag slots),
    `CookingStation.UpdateCooking` / `GetDeltaTime` (speed, owner), `InventoryGui.DoCrafting` (cauldron bonus),
    `Player.EatFood` / `GetTotalFoodValue` ("well-cooked" bonus).
  - `SE_Stats` (gear status effects) and `ItemDrop.ItemData.m_customData` (quality tags on output).
- **Sketch:**
  - Phase 1, data only: new stations (griddle or frying pan with new pan dishes, drying rack for jerky/fish,
    spit-roast for whole game) built from vanilla prefabs with new conversions and items.
  - Phase 2: wearable gear (apron, chef's hat, oven mitts as utility or trinket) whose `SE_Stats` flags are read in
    the hooks above:
    - bonus-yield chance (client-side roll)
    - burn protection: the slot's cooked-time cap
    - cook speed: needs the adder's gear written into a slot ZDO key at `RPC_AddItem` time, because the owner, not
      the cook, advances time
- **Risks:**
  - Needs models, icons and localization.
  - Cook-speed effects must be stored in the ZDO, since the owner may be another player.
  - Many new ZDO prefabs make uninstall messy.
  - Recipe-data mods and food-overhaul mods (for example RtD/"Therzie"-style content packs) may add competing
    stations.

### Farming — Bring tamed / pets on ship (New)
*Tames travel on ships reliably.*

- **Feasibility:** hard.
- **Who needs the mod:** everyone. The ship owner simulates, and remote clients need `m_characterParentSync` on
  their copy for smooth rendering.
- **Hooks:**
  - `Character.GetStandingOnShip`, `Character.ApplyGroundForce` (attach strength).
  - `BaseAI.IdleMovement` / `BaseAI.Follow` / `BaseAI.MoveTo` (freeze or deck-local wandering).
  - `BaseAI.SetPatrolPoint` / `GetPatrolPoint` (store ship-local, e.g. a custom ZDO key with the ship ZDOID +
    local offset).
  - `Ship.CustomFixedUpdate` / `Ship.UpdateOwner` (ship owner claims ownership of tames onboard via
    `ZNetView.ClaimOwnership`).
  - `ZSyncTransform.m_characterParentSync` (enable on tameable prefabs in `ZNetScene.Awake`).
  - `Tameable.RPC_Command` (a "board" / "stay on deck" command).
  - `SE_Harpooned` (pull onto a deck: needs `noUpForce` relaxed).
- **Sketch:**
  - While a tamed `Character` has `GetStandingOnShip() != null` and the ship is moving, force the AI into a deck
    state: no random wander, `StopMoving`, face the travel direction, attach active. A "stay" patrol point
    becomes ship-local.
  - The ship owner periodically claims ownership of tames whose `GetStandingOnShip()` is its ship, so ship and
    creature simulate on the same machine (the velocity-correction branch of `ApplyGroundForce`).
  - Enable parent-relative sync so remote players see them glued to the deck.
  - Optional "board" command: teleport a following tame onto a free deck point when the player boards.
- **Risks:**
  - Ownership churn with `ZDOMan.ReleaseNearbyZDOS` (the server may hand ownership back).
  - Physics explosions with big creatures (Lox mass pushes ships: `ApplyGroundForce` also pushes lighter ground
    bodies).
  - Pathfinding assumptions.
  - Conflicts with ship mods (ValheimRAFT-style moving bases already reparent characters) and AI overhauls.

### Farming — Bring tamed / pets through stone portal (New)
*Following tames come with you through the (Ashlands-tier) stone portal.*

- **Feasibility:** medium.
- **Who needs the mod:** client-only (the teleporting player moves ZDOs it has claimed). Vanilla clients just see
  the creature appear. Ship a synced config so servers can forbid it.
- **Hooks:**
  - `TeleportWorld.Teleport` (prefix/postfix: know the portal; restrict to `Utils.GetPrefabName(portal) ==
    "portal_stone"`, or to `m_allowAllItems`).
  - `Player.TeleportTo` (true = teleport started).
  - `Player.UpdateTeleport` (postfix: detect arrival, `m_teleporting` true→false).
  - `Character.GetAllCharacters()` + `MonsterAI.GetFollowTarget()` (who follows me).
  - `ZNetView.ClaimOwnership`, `ZDO.SetPosition`, `ZNetScene.FindInstance(ZDOID)`.
  - `Tameable` (`s_follow`, `m_unsummonDistance`).
- **Sketch:**
  - At teleport start, collect tamed characters within about 10 m whose follow target is the local player. Claim
    their ZDOs and remember their ZDOIDs and relative offsets.
  - Set their ZDO position (and transform) to the destination. They unload locally, and the destination owns them
    once you arrive.
  - When `UpdateTeleport` reports arrival (area ready, floor found), find each instance
    (`ZNetScene.FindInstance`), place it at player position + offset snapped to `ZoneSystem.GetSolidHeight`, zero
    the velocity and re-issue follow.
  - Retry for a few seconds if the instance is not spawned yet.
- **Risks:**
  - Creatures spawning before terrain colliders exist can fall through: always re-place after arrival and
    consider a short kinematic window.
  - If another peer owned the tame, ownership transfer can race.
  - Saddled or ridden creatures (`Sadle`): dismount first.
  - Summons may unsummon by distance before the move: move them first or skip them.
  - Portal-restriction mods and existing "teleport everything"-style mods patch the same `TeleportWorld.Teleport`.
