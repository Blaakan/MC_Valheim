# Existing mods — Exploration

Research done 2026-09 (2026-09-28), about three weeks after Valheim 1.0 (Deep North update) shipped on 2026-09-09. Target game build: 1.0.16 (network version 40), Unity 6000.0.75f1, BepInEx 5.4.23.5.

## How to read this report

- **Dates** come from the Thunderstore API (`https://thunderstore.io/api/experimental/package/<namespace>/<name>/`, field `date_updated`). Nexus pages return HTTP 403 to automated fetches, so Nexus-only mods are dated approximately.
- **1.0 status**
  - `1.0 tagged`: the package carries Thunderstore's "Deep North Update" category. This is the author's own claim that it works on 1.0.
  - `updated post-1.0`: released after 2026-09-09 but without the tag. It probably works.
  - `pre-1.0`: last release before 2026-09-09. Assume it is broken until tested. Hosting blogs report that 1.0 changed the item DB, the ZNetScene registry, skills and piece categories. Jotunn 2.30 shipped a 1.0 fix on launch day.
  - `deprecated`: Thunderstore's deprecated flag is set.
- **AI** means the author tagged the package "AI Generated" on Thunderstore. A lot of 2026 mods carry this tag, so treat their code quality as unknown.
- **Licenses**: we only borrow ideas. Code under GPL (for example Advize's mods) must never be copied into our repos.
- **Vanilla hooks** list what the decompiled 1.0.16 code already provides. They are cited as `Class.Member` and were checked in `.ref/decompiled/assembly_valheim`.

## Summary

| # | Idea | Scope | User said exists | Coverage found |
|---|------|-------|------------------|----------------|
| 1 | Cartography table revamp | QoL | No | partial |
| 2 | Sleep through the day | QoL | No | partial |
| 3 | Per creature kill count | QoL | No | partial |
| 4 | Boss summon revamp | New | Partially | partial |
| 5 | Sailing revamp | New | Partially | partial |
| 6 | Big unique dungeon | New | No | partial |
| 7 | Spyglass | New | No | **full** |
| 8 | Swim dive | New | Partially | **full** |
| 9 | Hip lantern as utility item | New | Yes | **full** |
| 10 | Distant horizon | New | No | partial |
| 11 | Underwater biome | New | No | partial |
| 12 | Deep North revamp | New | No | none |
| 13 | Mob variant (magic-themed) | New | No | partial |
| 14 | Weather staff | New | No | partial |
| 15 | Compendium | New | No | partial |
| 16 | New ability depending on skill level | New | No | partial |
| 17 | Better shops | New | Partially | partial |
| 18 | Late game quest | New | (blank) | partial |

What stands out:

- **Spyglass, diving and the hip lantern already exist as maintained, 1.0-compatible mods.** We should only build them if our version offers something those mods do not, such as the integrations suggested below.
- **The kill-count data already exists in vanilla.** `PlayerProfile` records per-creature kills, but no screen in the game shows the per-creature numbers. This makes kill count the cheapest idea with a high payoff.
- **1.0 added an `AltBiome` system.** It applies per-sector biome modifiers (add or block spawns, environments, vegetation and locations; force an environment or music; change the level-up chance). It is a strong hook for mob variants, the Deep North revamp and ocean sub-biomes.
- **1.0 added a simulation/draw-distance setting** (`SimulationDistance`). The best-known render-distance mod was deprecated in 2026-08.

---

## 1. Cartography table revamp (QoL) — coverage: partial

The user's description, "More pins when near table", can be read three ways:

- (a) the table unlocks extra pin icons, types or metadata;
- (b) standing near a table shows more pins, such as shared pins, auto pins or tracked objects;
- (c) pins are shared through the table.

Mods exist for all three, but none of them ties the extra pins to being near the table.

**Vanilla hooks**
- `MapTable.OnRead` / `MapTable.OnWrite` store the compressed shared map, including pins, in the table ZDO (`ZDOVars.s_data`). Writing is gated by `PrivateArea.CheckAccess`.
- `Minimap.GetSharedMapData` / `Minimap.AddSharedMapData` produce and merge that data.
- Players get only 5 pin icons (`Minimap.PinType.Icon0..Icon4`) plus special types (Death, Boss, ...).
- `MapTable.UseItem` is an empty stub that returns false. It is a free hook for "use an item on the table".

| Mod | Status | Notes |
|-----|--------|-------|
| [Better Cartography Table](https://thunderstore.io/c/valheim/p/nbusseneau/Better_Cartography_Table/) (nbusseneau) | 0.8.1, 2025-08-12, pre-1.0 (built on Jotunn 2.26) | Pins can be private, public or guild. Changes show in real time when several players use a table at once. Supports NoMap runs. Public and guild pins are stored separately and mirrored into vanilla shared pins. MIT, [GitHub](https://github.com/nbusseneau/BetterCartographyTable). The best-engineered mod in this space. |
| [ZenMap](https://thunderstore.io/c/valheim/p/ZenDragon/ZenMap/) (ZenDragon) | 1.11.0, 2026-09-13, 1.0 tagged | The map is only usable at a table. The view is locked to the table's position, and the revealed area grows over about 7 game days (1250 m radius by default). The table live-tracks ships, carts, mounts, tombstones and players. Parchment "snapshot" maps can be carried. Pins are attached to signs. Hardcore/NoMap philosophy. |
| [Pinnacle](https://thunderstore.io/c/valheim/p/ComfyMods/Pinnacle/) (ComfyMods) | 1.17.0, 2026-09-16, 1.0 tagged | Pin manager: search, edit and filter pins, plus more icons through sprite names and pin scaling. Pure UI QoL. |
| [Cartur's Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) | 1.6.0, 2026-09-27, updated post-1.0, AI | Automatically pins ore, dungeons, camps, chests and minibosses when you get close. 158 icons, and a pin editor for icon, color, size and opacity. |

Also relevant:
- [CartographySkill](https://thunderstore.io/c/valheim/p/Advize/CartographySkill/) (Advize, 3.2.0, 2026-09-11, 1.0 tagged): a skill that grows the explore radius. Its roadmap mentions table features that have not been built.
- [Exploration](https://thunderstore.io/c/valheim/p/Smoothbrain/Exploration/) (Smoothbrain, 1.0.4, 2026-02-05, pre-1.0): recording to a table unlocks at level 20 and map copying at level 40.
- [MoreMapPins](https://thunderstore.io/c/valheim/p/KGvalheim/MoreMapPins/) (KG): custom pin icons. KG's mods are now largely deprecated.

**Inspiration**

- **Idea: make the table the "map room".** While you are inside the table's radius or ward, grant a "Cartographer" status (like Rested). The map UI then unlocks:
  - an extended pin palette (more icons, colors, notes, categories);
  - pin search (Pinnacle-style);
  - team or public pins (Better Cartography Table's per-pin visibility, stored on the table ZDO);
  - live-tracked assets (ships, carts, tames, portals by tag), as ZenMap does.
- **Keep:** the normal vanilla map everywhere else, and mirror our data into vanilla shared pins so unmodded clients degrade gracefully.
- **Avoid:** ZenMap's NoMap stance, which is too hardcore for a QoL mod.
- **Watch:** Better Cartography Table is the closest competitor, and it has not been updated for 1.0.
- **Could integrate with:** the Spyglass ("pin what I look at") and the Compendium (discovered locations).

## 2. Sleep through the day (QoL) — coverage: partial

"Sleep anytime" mods exist, but they all still skip to the *next morning*. Nobody offers "rest until dusk".

**Vanilla hooks**
- `Bed` interaction checks `EnvMan.CanSleep`.
- On the server, `Game.UpdateSleeping` only starts sleep if `EnvMan.IsAfternoon() || EnvMan.IsNight()` and `Game.EverybodyIsTryingToSleep`.
- `EnvMan.SkipToMorning` always targets the next morning's start (`GetMorningStartSec(day + 1)`).
- `EnvMan.UpdateTimeSkip` fast-forwards server net time over `c_TimeSkipDuration` (12 s).

| Mod | Status | Notes |
|-----|--------|-------|
| [BedRules](https://thunderstore.io/c/valheim/p/TastyChickenLegs/BedRules/) (TastyChickenLegs) | 2.0.6, 2026-09-11, 1.0 tagged | Sleep "anytime", without setting spawn, in others' claimed beds, and ignoring fire, enemies and wet. The time target is not documented; presumably the vanilla skip-to-morning. |
| [Sleepover](https://thunderstore.io/c/valheim/p/Azumatt/Sleepover/) (Azumatt) | 1.1.4, 2024-11-07, deprecated | "Sleep at any time of day (may be buggy)", multiple sleepers per bed, every check toggleable. Needs the mod on the server and all clients. |
| [Sleep Options](https://www.nexusmods.com/valheim/mods/2716) (Nexus) | Nexus-only, older | Toggles the Night, Enemies, Exposure, Fire and Wet checks. |
| [SleepSkip](https://thunderstore.io/c/valheim/p/Azumatt/SleepSkip/) / [SkipSleep](https://thunderstore.io/c/valheim/p/R1NS3/SkipSleep/) / BetterSleepBruh / NowYouSleep | various | Let a fraction of the players skip the night. None of them handles day sleep. |

Related day-length mods: Sundial, ExtendedDaytime and LongerDays change how long the day lasts.

**Inspiration**

- **Idea:** add a second bed option, "Rest until dusk", that is only available in the morning or midday.
- **How:** reuse the vanilla skip machinery, but set the target to the evening of the *same* day instead of `GetMorningStartSec(day + 1)`. Keep the everyone-must-agree rule (or an optional vote ratio) and grant Rested as usual.
- **Patch points:** `Game.UpdateSleeping` (server) and a custom RPC. Clients only need the mod for hover text and input.
- **Avoid:** Sleepover's approach of bypassing every check, which it itself warns is buggy. Implement a separate mode with its own timing and keep the vanilla safety checks (enemies nearby, wet).
- **Use cases:** it makes night exploration play (Mistlands, Deep North invasions) cheap to schedule.
- **Test with:** Seasons and day-length mods.

## 3. Per creature kill count (QoL) — coverage: partial

**Vanilla hooks (the key finding)**
- The profile already stores per-creature kills: `PlayerProfile.m_playerStats[category].m_enemyStats[KillModifiers]`, a dictionary keyed by creature name.
- `PlayerProfile.IncrementStatEnemy` increments it, broken down by `KillModifiers` (MixedAndTotal, Unarmed, Magic, Ranged, Melee).
- Index 0 is the all-time total and is always incremented. Other indices only count when achievements are allowed.
- Achievements use these stats (`Achievement.m_enemyStatsTriggers`, `AchievementsGui`).
- The Compendium stats page (`TextsDialog.AddStats`) prints only the "Enemies:" category headers and never lists the per-creature values. The data exists and is saved in the `.fch` profile; only the UI is missing.

| Mod | Status | Notes |
|-----|--------|-------|
| [TrophyHuntMod](https://thunderstore.io/c/valheim/p/oathorse/TrophyHuntMod/) (oathorse) | 0.12.1, 2026-09-28, updated post-1.0 | Trophy tray HUD. Tooltips show player kills, trophy pickups and drop rates. MIT-0, [GitHub](https://github.com/smariotti/TrophyHuntMod). Built for "Trophy Hunt" speedrun modes. |
| [Almanac](https://thunderstore.io/c/valheim/p/RustyMods/Almanac/) (RustyMods) | 3.8.0, 2026-09-25, 1.0 tagged | Index of all creatures, items and pieces built on the trophy panel. Player metrics (kills, deaths, ...), YAML achievements with kill thresholds, leaderboard. A large all-in-one mod. |
| [SlayerSkills](https://thunderstore.io/c/valheim/p/Neobotics/SlayerSkills/) (Neobotics) | 1.2.0, 2026-09-11, 1.0 tagged | Earns per-creature "slayer points" from trophy drops, which give bonus damage against that creature. Has multiplayer split modes. |
| [KillCountEvolution](https://thunderstore.io/c/valheim/p/basicMods/KillCountEvolution/) (basicMods) | 1.1.0, 2026-05-13, pre-1.0, AI | A world-wide kill total that drives elite spawns and loot. No per-creature UI. |

**Inspiration**

- **Idea:** build a very small mod that *reads vanilla stats*. It needs no tracking of its own and carries no save-compatibility risk.
- **Where to show the counts:**
  - a line in trophy tooltips and the creature hover/nameplate, for example "Greydwarf — 243 killed (melee 180 / ranged 50 / magic 13)";
  - the Compendium stats page, showing the per-creature values vanilla forgets to print.
- **Later:** kill milestones (1/10/50/100) can unlock bestiary knowledge tiers in the Compendium (idea 15), in the style of Monster Hunter or Hollow Knight. Milestones can also feed skill or ability ideas.
- **Avoid:** re-implementing our own counters as Almanac does.

## 4. Boss summon revamp (New) — coverage: partial

Boss affixes and harder bosses exist. Nothing escalates the fight on each re-summon or gives new rewards for it.

**Vanilla hooks**
- `OfferingBowl`: `m_bossItem`, `m_bossItems`, `m_bossPrefab`, `m_setGlobalKey`, `m_useItemStands` for item-stand altars, and `RPC_SpawnBoss` / `SpawnBoss`.
- Bosses can be re-summoned and drop the same loot every time.

| Mod | Status | Notes |
|-----|--------|-------|
| [StarLevelSystem](https://thunderstore.io/c/valheim/p/MidnightMods/StarLevelSystem/) (MidnightMods) | 1.19.1, 2026-09-26, 1.0 tagged | Boss modifiers (BossSummoner, SoulEater, LifeLink, ResistPierce, Brutal), star levels above 2, YAML loot scaling, scaling by bosses defeated, and a public API. [GitHub](https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded). |
| [CreatureLevelAndLootControl](https://thunderstore.io/c/valheim/p/Smoothbrain/CreatureLevelAndLootControl/) (Smoothbrain) | 4.6.4, 2025-05-26, deprecated | The classic mod. Boss affixes (Reflective, Shielded, Mending, Summoner, Elementalist, Enraged, Twin), boss minimum/maximum level, loot multipliers. Dead. |
| [Enhanced Bosses Redone](https://thunderstore.io/c/valheim/p/MaxFoxGaming/Enhanced_Bosses_Redone/) / [HS_EnhancedBosses](https://thunderstore.io/c/valheim/p/HS/HS_EnhancedBosses/) | 2023, deprecated | New boss attacks (for example Yagluth raising a rock pillar), with each attack's frequency and HP-threshold phase set in JSON. A good design reference for phases. |
| [ValheimFortress](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimFortress/) (MidnightMods) | 0.37.2, 2026-09-18, 1.0 tagged | A buildable shrine or arena where you summon enemy waves for rewards. The closest to a "challenge for reward" loop. |
| [CustomizeAltars](https://thunderstore.io/c/valheim/p/Huntardys/CustomizeAltars/) (Huntardys) | 2.0.0, 2024-02-04, deprecated | JSON altars: any boss with any offering. |

Also: [Boss Altar Rule of Three](https://thunderstore.io/c/valheim/p/Goldenrevolver/Boss_Altar_Rule_of_Three/) (2023, cheaper offerings), and the Steam suggestion thread "Star will be added to second and subsequent same boss", which shows players want this.

**Inspiration**

- **Idea:** "Boss Trials". Track, per world, how many times each boss has been beaten, stored on the altar ZDO or as a world counter.
- **Each re-summon moves up one trial tier:**
  - the offering cost rises;
  - the boss gains stars and affixes (reusing our mob-variant affixes);
  - the arena gets modifiers (forced weather through a temporary `EnvZone`, extra adds, hazards);
  - the tier has its own rewards: trinket upgrades or new trinkets tied to the Adrenaline revamp, Forsaken-power upgrades, crafting materials for our Crafting mods, cosmetics.
- **Borrow:** Enhanced Bosses' JSON attack/phase thresholds; ValheimFortress's challenge and reward flow.
- **StarLevelSystem:** integrate with it through its API instead of duplicating it, since it has a large user base.
- **Avoid:** global difficulty scalers that make the whole world harder.

## 5. Sailing revamp (New) — coverage: partial

The user's link, Nexus 922, is dead. Every piece of the idea exists somewhere, but the only mods that combined them are dead or deprecated.

**Vanilla hooks**
- 1.0 has **no Sailing skill**. `Skills.SkillType` gained Crafting, Dodge and Ride, but not Sailing.
- Ship handling is tuned by fields on `Ship`: `m_sailForceFactor`, `m_rudderSpeed`, `m_stearForce`, `m_backwardForce`, `m_waterImpactDamage`. The logic lives in `Ship.GetSailForce` and `Ship.UpdateSail`.
- Map reveal uses a fixed `Minimap.m_exploreRadius` of 100 m every `m_exploreInterval` (2 s), with no ship bonus.

| Mod | Status | Notes |
|-----|--------|-------|
| [Sailing Skill](https://www.nexusmods.com/valheim/mods/922) (Gaijinx, Nexus 922, the user's link) | 2021-era; depends on pipakin's Skill Injector and Mod Config Enforcer 1.x, so dead | Skill: tailwind boost, forewind dampening, ship damage reduction, rudder speed, each up to 50%. |
| [Sailing](https://thunderstore.io/c/valheim/p/Smoothbrain/Sailing/) (Smoothbrain) | 1.1.8, 2026-02-05, deprecated | Skill: health of ships you built, speed of ships you command, explore radius while sailing. Per-ship speed config and a "nudge" hotkey. |
| [ImpactfulSkills](https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/) (MidnightMods), "Voyager" skill | 0.20.2, 2026-09-28, 1.0 tagged | Level 25: paddle speed. Level 35: less boat damage. Level 50: smaller wind penalty. Level 75: immune to impact damage. |
| [GrindstoneSkills](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) (MilkyTeam), Sailing | 0.9.1, 2026-09-27, updated post-1.0 | Up to +50% ship HP (set when the ship is placed), +20% speed, 2x explore radius. Level 50 "Lookout": a pulse reveals enemies within 100 m. |

Wind and QoL mods:
- [Smooth Sailing](https://thunderstore.io/c/valheim/p/P377Y/Smooth_Sailing/) (0.3.4, 2026-09-23, 1.0 tagged, AI): favorable-wind modes, rowing multipliers, 2x explore radius on ships.
- [HelmWind](https://thunderstore.io/c/valheim/p/SirRomey/HelmWind/) (1.0.2, 2026-09-11, 1.0 tagged, AI): locks the wind to your heading.
- [Njord](https://thunderstore.io/c/valheim/p/Wubarrk/Njord/) (deprecated 2026-08, AI): physics overhaul plus HUD.
- [ShipExploration](https://thunderstore.io/c/valheim/p/GemHunter1/ShipExploration/) (1.3.0, 2026-09-27, 1.0 tagged): explore radius per ship type.
- [LongshipUpgrades](https://thunderstore.io/c/valheim/p/shudnal/LongshipUpgrades/) (shudnal, 1.0.23, 2026-09-26, 1.0 tagged): lantern, tent, storage and sail upgrades.

**Inspiration**

- **Idea:** a real Sailing skill gained from distance sailed while at the helm, covering the user's list:
  - **"Easier to catch the wind":** widen the angle at which the sail still pulls. Do not rotate the wind to the heading, so tacking still takes skill.
  - **Turning:** rudder turn rate.
  - **Stopping:** stronger braking and reverse force.
  - **Acceleration:** faster build-up of speed.
  - **Map reveal:** an explore radius above 100 m that grows with skill, and could also depend on ship type or mast height.
  - **Milestone abilities:** for example a Lookout ability, a wind forecast, a crew bonus.
- **Multiplayer:** ship physics run on the ZDO owner. Apply the skill of the controlling player (`Player.GetControlledShip`).
- **Avoid:**
  - "always tailwind" cheats (HelmWind, Smooth Sailing), which trivialize sailing;
  - health bonuses that only apply to ships the player built (Smoothbrain).
- **Watch:** conflicts with ImpactfulSkills' Voyager skill and GrindstoneSkills; check which `Ship` members they patch.

## 6. Big unique dungeon (New) — coverage: partial

Extra procedural dungeons exist. Nothing adds one handcrafted, unique, endgame-difficulty dungeon per biome.

**Vanilla hooks**
- 1.0 added Winding Tunnels and Mörkhalla.
- Dungeons are data-driven: a `DungeonGenerator` plus room prefabs, placed as `ZoneSystem` locations.

| Mod | Status | Notes |
|-----|--------|-------|
| [More World Locations AIO](https://thunderstore.io/c/valheim/p/warpalicious/More_World_Locations_AIO/) (warpalicious) | 5.1.4, 2026-09-25, 1.0 tagged | 190 POIs, including 2 procedural dungeons. High quality and very popular. |
| [Forbidden Catacombs](https://thunderstore.io/c/valheim/p/warpalicious/Forbidden_Catacombs/) / [Underground Ruins](https://thunderstore.io/c/valheim/p/warpalicious/Underground_Ruins/) (warpalicious) | 1.0.2, 2025-06-14 / 1.0.8, 2025-04-21, pre-1.0 | Procedural Swamp dungeon (21 rooms) and Black Forest dungeon (24 rooms, puzzles) with vanilla-friendly progression. |
| [Expand World Data](https://thunderstore.io/c/valheim/p/JereKuusela/Expand_World_Data/) (JereKuusela) | 1.73.0, 2026-09-23, updated post-1.0 | YAML for biomes, locations, dungeon generators and rooms (`expand_dungeons.yaml`) and, since 1.73, events. Companion mod [Dungeon Splitter](https://thunderstore.io/c/valheim/p/JereKuusela/Dungeon_Splitter/) moves dungeons into a separate instance. |
| [Dungeon Creation Kit](https://thunderstore.io/c/valheim/p/MoonTower/Dungeon_Creation_Kit/) (MoonTower) | 3.1.2, 2026-09-27, updated post-1.0, AI | Build your own multi-level dungeons and instanced areas out of pieces. |

Also:
- [Dungeonheim](https://www.nexusmods.com/valheim/mods/1997) (Nexus): a handcrafted world seed with 7 dungeons, in German. It is a world, not a plugin.
- [Venture Location Reset](https://thunderstore.io/c/valheim/p/VentureValheim/Venture_Location_Reset/) (1.1.1, 2026-09-27, 1.0 tagged): periodically resets dungeons.

**Inspiration**

- **Idea:** one named, handcrafted "legendary" dungeon per biome, placed exactly once per world as a high-priority unique location.
  - multiple floors with key/lock progression;
  - mini-bosses and a final guardian;
  - endgame difficulty that ignores the biome's normal tier;
  - unique loot tied to our Crafting ideas;
  - optional weekly reset, like Venture Location Reset.
- **Borrow:** warpalicious' room-authoring pipeline (Unity asset bundles plus Jotunn), EWD's dungeon YAML, and Dungeon Splitter's instancing for performance.
- **Avoid:** procedural sameness. The value is in handcrafted layouts.
- **Cost:** this needs a real Unity asset pipeline (asset bundles or ThunderKit). It is the most art-heavy idea in the list.

## 7. Spyglass (New) — coverage: full

The user believed no spyglass mod exists; several do.

**Vanilla hooks**
- Zoom is a change of `GameCamera.m_fov` (65 by default). Free-fly mode already allows a FOV as low as 5.

| Mod | Status | Notes |
|-----|--------|-------|
| [Spyglass](https://thunderstore.io/c/valheim/p/Advize/Spyglass/) (Advize) | 3.2.0, 2026-09-11, 1.0 tagged | Craftable (2 obsidian, 2 bronze, 1 crystal) and upgradable. 3 zoom levels (right mouse button / Shift+right mouse button, gamepad triggers). Doubles as a bash weapon that can block. ServerSync. GPL-3.0, [GitHub](https://github.com/AdvizeGH/Advize_ValheimMods). |
| [Monocular](https://thunderstore.io/c/valheim/p/jg224/Monocular/) (jg224) | 0.5.1, 2026-09-09, deprecated | Handheld monocular with mouse-wheel zoom. |
| [Visby Lens](https://www.nexusmods.com/valheim/mods/432) (Nexus) | 2021-era | Lore-friendly spyglass with right-click zoom. |

**Inspiration**

- **Status:** basic zoom is solved and maintained. Only build our own if it becomes an **exploration tool**, not just a zoom:
  - while scoping, reveal the map in the view cone at long range ("survey", ties to Cartography);
  - one key drops a pin on what you are looking at;
  - show the target creature's name, stars, variant and kill count (ties to ideas 3, 13 and 15);
  - pointing it at the sky gives a weather forecast (ties to ideas 5 and 14);
  - a crow's-nest or helm use on ships.
- **Feel:** smooth scroll zoom, a vignette overlay and slight sway.
- **Licensing:** Advize is GPL, so reimplement rather than copy.

## 8. Swim dive (New) — coverage: full

**Vanilla hooks**
- `Character.m_swimDepth` (2.0) keeps the player floating at the surface.
- There is no vertical swimming and no breath/oxygen system in 1.0.16.

| Mod | Status | Notes |
|-----|--------|-------|
| [BetterDiving](https://thunderstore.io/c/valheim/p/MainStreetGaming/BetterDiving/) (MainStreetGaming) | 1.1.2, 2026-09-10, 1.0 tagged | Crouch toggles diving and you dive where you look. Adds a Diving skill, an oxygen bar, and fast-swim stamina/oxygen tradeoffs. Apache-2.0, [GitHub](https://github.com/humansandbag/Valheim-Better-Diving-Mod). Fork of Easy_Develope's [Valheim Diving Mod](https://www.nexusmods.com/valheim/mods/1271). Incompatible with VikingsDoSwim and ImprovedSwimming. |
| [VikingsDoSwim](https://thunderstore.io/c/valheim/p/blacks7ar/VikingsDoSwim/) (blacks7ar) | 1.4.2, 2026-09-15, 1.0 tagged | Swim speed and stamina scale with the Swim skill, plus a diving mechanic. |
| [Dive In](https://thunderstore.io/c/valheim/p/sighsorry/Dive_In/) (sighsorry) | 1.2.3, 2026-09-09, 1.0 tagged, AI | Dedicated ascend/descend keys (true vertical diving), stamina drain that scales with depth, underwater darkness and murk, creatures that can chase underwater, weapons usable in water. YAML, server-synced. |
| [Underwater](https://thunderstore.io/c/valheim/p/Crystal/Underwater/) / [UnderTheSea](https://old.thunderstore.io/c/valheim/p/Jacobo/UnderTheSea/) | older | Walk on the seafloor; crouch to dive, jump to rise. |

**Inspiration**

- **Status:** diving is covered three times over, and all three mods patch the same swimming code, so they are mutually incompatible.
- **Only worth building if** it is the base layer for the Underwater biome (idea 11) and gear-driven diving:
  - vertical dive keys, as the user asked;
  - Breath tied to the vanilla Swim skill instead of a new Diving skill;
  - diving gear (helmet, weights, underwater lantern) made through Crafting;
  - cold/pressure in Deep North waters.
- **Alternative:** declare a soft dependency on BetterDiving (Apache-2.0) and build only the content.

## 9. Hip lantern as utility item (New) — coverage: full

The user already knew this exists.

**Vanilla hooks**
- `Humanoid` has exactly one `m_utilityItem` slot and one `m_trinketItem` slot. A lantern in the utility slot competes with Megingjord, the Wishbone, and so on.
- The 1.0 patch notes mention new lanterns ("hooded" and "snow" lanterns). Check whether any of them is wearable.

| Mod | Status | Notes |
|-----|--------|-------|
| [HipLantern](https://thunderstore.io/c/valheim/p/shudnal/HipLantern/) (shudnal) | 1.1.12, 2026-09-21, 1.0 tagged (about 173K downloads) | Craftable (3 surtling cores, 10 bronze nails, 4 fine wood). Burns fuel measured in minutes (0 means infinite) with a refuel recipe. Uses its own configurable equipment slot or the utility slot. Configurable light (color, range, shadows), can be placed on item stands, attracts insects at night. EpicLoot integration, server-synced. [GitHub](https://github.com/shudnal/HipLantern). |
| [ExtraSlots](https://thunderstore.io/c/valheim/p/shudnal/ExtraSlots/) (shudnal) | 1.0-era, confirmed working | Extra equipment slots. The usual companion to HipLantern. |

**Inspiration**

- **Status:** solved, so do not duplicate it.
- **If we touch lighting at all:**
  - make the *vanilla* lanterns (the Dvergr lantern and the new 1.0 lanterns) hip-mountable instead of adding a new item;
  - or fold this into a broader "utility belt" UX idea.
- **Recommendation:** deprioritize this idea and recommend HipLantern.

## 10. Distant horizon (New) — coverage: partial

**Vanilla hooks**
- 1.0 added a **Simulation/Draw Distance** graphics setting (`GraphicsSettingInt.SimulationDistance`).
  - The `SimulationDistance` struct lets the player raise the number of near zones; the far ring stays at 2.
  - The value is server-synced through `ZNet.GetSyncedSimulationDistance`, and the game warns when it goes above the original distance.
- Distant terrain is already drawn as a low-detail heightmap (`Heightmap.IsDistantLod`, `HeightmapBuilder`). Trees, rocks and buildings beyond loaded zones are not drawn.
- The console command `lodbias` exists.

| Mod | Status | Notes |
|-----|--------|-------|
| [Render Limits](https://thunderstore.io/c/valheim/p/JereKuusela/Render_Limits/) (JereKuusela) | 1.15.0, 2026-08-30, deprecated (no reason given; probably superseded by the vanilla 1.0 setting) | Controls how many zones are active, loaded and generated, the real-terrain distance, LOD bias, clutter distance, shadows and pixel lights, with server-side caps. [GitHub](https://github.com/JereKuusela/valheim-render_limits). |
| [RenderSettings](https://thunderstore.io/c/valheim/p/romenh/RenderSettings/) (romenh) | 2021, dead | Extra camera and render settings. |

**Inspiration**

- **Gap:** nothing like Minecraft's "Distant Horizons" exists for Valheim.
- **Idea:** a client-only LOD layer. It draws cheap impostors (billboards or instanced low-poly meshes) for trees, rocks and major landmarks beyond loaded zones. The positions can be computed from `WorldGenerator` and zone vegetation data, or cached from zones already visited. Add better distant-terrain LOD and fog tuning.
- **Difference from Render Limits:** Render Limits loads more zones, which costs network and CPU. This layer would load nothing from the server.
- **Risk:** high technical risk. We must first check which Unity 6 render pipeline Valheim uses and how it does GPU instancing.

## 11. Underwater biome (New) — coverage: partial

Ocean *content* mods exist. There is no real underwater biome with seafloor terrain, vegetation or its own environment.

**Vanilla hooks**
- The ocean is `Heightmap.Biome.Ocean`.
- 1.0's `AltBiome` system (see `AltBiomeWorldData.GenerateAltBiomes`) attaches modifiers to biome sectors. A modifier can:
  - add or block spawns, environments, vegetation and locations;
  - force an environment or music;
  - change the level-up chance;
  - override the terrain texture.

  Height changes are currently disabled in code. This could host ocean sub-biomes such as a "Kelp Forest" or an "Abyss".
- Water rendering and fog come from `WaterVolume` and `EnvMan`.

| Mod | Status | Notes |
|-----|--------|-------|
| [RtDOcean](https://thunderstore.io/c/valheim/p/Soloredis/RtDOcean/) (Soloredis) | 2.2.49, 2026-09-24, updated post-1.0 | Fish and whales; tameable dolphins, orcas and mermaids; predators (sharks, megalodon); sunken ships with treasure; seaweed and seashells; ocean boss Belzor with trinkets; relics; new crops and dishes. Needs no diving mod. |
| [SeaAnimals](https://thunderstore.io/c/valheim/p/Marlthon/SeaAnimals/) (Marlthon) | 0.3.8, 2026-09-11, 1.0 tagged | 12+ marine animals that spawn by depth, biome and global key. Rideable orca, dolphin, crocodile and shark with a Maritime Saddle. |
| [Bestiary](https://thunderstore.io/c/valheim/p/Radamanto/Bestiary/) (Radamanto) | 1.2.0, date not checked | Creature pack that includes white sharks and whales. |
| [Expand World Data](https://thunderstore.io/c/valheim/p/JereKuusela/Expand_World_Data/) | 1.0-era | Can define new biomes and terrain in YAML. |

**Inspiration**

- **Idea:** a true seafloor biome in the deep ocean:
  - sculpted trenches and underwater vegetation;
  - ruins and sunken longships;
  - bioluminescence and light shafts;
  - its own environment, fog and music;
  - resources you can only get by diving.
- **Depends on:** diving (idea 8, ours or BetterDiving) and breathing gear (Crafting).
- **Borrow:** RtDOcean's POI ideas and SeaAnimals' depth-based spawn rules.
- **Implementation route:** start with an ocean `AltBiome`-style modifier, then add terrain work. A heightmap override would be needed, which vanilla has disabled.
- **Cost:** heavy art and rendering work.

## 12. Deep North revamp (New) — coverage: none

**Vanilla hooks and 1.0 facts**
- The Deep North is now a full biome. According to the wiki, breaking the **Malicious Ice** at the bottom of the Mörkhalla dungeon triggers a **Jotun Invasion** somewhere in the lower biomes. At most 3 run at once; the location prefab is `FimbulLocation01`.
- This is data-driven: no dedicated C# class was found for it.
- Spawns and events can be gated by global keys: `SpawnSystem.SpawnData.m_requiredGlobalKey`, `CreatureSpawner.m_requiredGlobalKey`, `RandomEvent.m_requiredGlobalKeys`.
- `AltBiome` modifiers can swap environments and spawn lists per sector.

**Question for the user:** does "the first stone is broken" mean the Malicious Ice, or some other stone?

| Mod | Status | Notes |
|-----|--------|-------|
| [MonstrumDeepNorth](https://thunderstore.io/c/valheim/p/Therzie/MonstrumDeepNorth/) (Therzie) | 2.0.6, 2025-03-10, deprecated | Pre-1.0 content for the old placeholder Deep North: arctic creatures, locations, Forsaken bosses. Obsolete. |
| [Expand World Data](https://thunderstore.io/c/valheim/p/JereKuusela/Expand_World_Data/) (events merged in 1.73) | 1.73.0, 2026-09-23 | Per-biome spawns, events and environments with key conditions in YAML. Can prototype dormant/active phases without code. |
| [Spawn That](https://thunderstore.io/c/valheim/p/ASharpPen/Spawn_That/) / [Custom Raids](https://thunderstore.io/c/valheim/p/ASharpPen/Custom_Raids/) (ASharpPen) | 1.2.19, 2026-09-09 / 1.8.2, 2026-09-10, 1.0 tagged | Config-driven spawners and raids with global-key conditions. |
| [World Advancement Progression](https://thunderstore.io/c/valheim/p/VentureValheim/World_Advancement_Progression/) (VentureValheim) | 1.0.0, 2026-09-10, 1.0 tagged | Gates progression by key. |

**Inspiration**

- **Status:** nobody has done this; it would be a genuinely new idea.
- **Dormant phase:** calm weather, sparse wildlife, eerie silence.
- **Trigger:** a world key set the first time the trigger (for example Malicious Ice) is destroyed. Detect it by patching the destructible or hooking the invasion start.
- **Active phase:**
  - blizzard-heavy environments;
  - Jotun patrols and Gammeltrolls;
  - raids on bases inside the Deep North;
  - new music;
  - more loot.
- **Implementation:** swap spawn tables, environments and music. Most of it is data (key-gated spawn and event entries) plus a small plugin to set the key and flip the environments.
- **Prototype:** EWD or Spawn That configs can validate the design before we write code.

## 13. Mob variant (magic-themed: elemental, blood...) (New) — coverage: partial

Generic affix and infusion systems are well covered. Variants themed on Valheim's own magic schools (Elemental Magic, Blood Magic) are not.

**Vanilla hooks**
- Creature stars via `Character` level.
- `Skills.SkillType.ElementalMagic` and `Skills.SkillType.BloodMagic`.
- `AltBiome.m_levelUpChanceMultiplier` and per-sector spawn lists.

| Mod | Status | Notes |
|-----|--------|-------|
| [StarLevelSystem](https://thunderstore.io/c/valheim/p/MidnightMods/StarLevelSystem/) (MidnightMods) | 1.19.1, 2026-09-26, 1.0 tagged | Major modifiers: Fire, Frost, Poison, Lightning, Elemental Chaos, Splitter, damage resistances. Minor modifiers: fire/poison novas, EitrDrain, StaminaDrain, Big, Fast, Evolving, Lootbags. Per-level color and size, YAML loot, public API. Incompatible with CLLC. |
| [EliteCreaturesReborn](https://thunderstore.io/c/valheim/p/MilkyTeam/EliteCreaturesReborn/) (MilkyTeam) | 3.11.0, 2026-09-28, updated post-1.0, AI | 13 mutations (Leeching, Warding, Bloated, Splintering, Cloaked, Blinking, Thieving, Gilded, ...), colored nameplates, per-biome `creature_rules.yml`, 4 loot modes. [GitHub](https://github.com/geraldjglasgow/ValheimMods/tree/main/EliteCreaturesReborn). |
| [Monster Modifiers](https://thunderstore.io/c/valheim/p/warpalicious/Monster_Modifiers/) (warpalicious) | 1.2.7, 2025-08-20, pre-1.0 | 31 modifiers, one per star. [GitHub](https://github.com/jneb802/MonsterModifiers). |
| [CreatureLevelAndLootControl](https://thunderstore.io/c/valheim/p/Smoothbrain/CreatureLevelAndLootControl/) (Smoothbrain) | 4.6.4, 2025-05-26, deprecated | The original infusions: Fire, Frost, Lightning, Spirit, Poison, Chaos. Extra effects and boss affixes. |

**Inspiration**

- **Idea:** variants themed on the game's magic schools, not generic affixes:
  - **Elemental-touched:** fire, frost or lightning auras and attacks that mirror the vanilla staffs.
  - **Blood-cursed:** lifesteal, raising skeletons like the Staff of the Dead, a sacrifice-HP burst.
- **Visuals:** distinct shaders.
- **Drops:** each variant drops essences or reagents for the Magic/Crafting revamps, so it fits the cross-category magic theme.
- **Regional variants:** could use `AltBiome` sectors, for example a "Blood-soaked Swamp".
- **Compatibility:** StarLevelSystem has a big install base. Make ours additive, or plug into its modifier API, instead of competing with it.

## 14. Weather staff (New) — coverage: partial

**Vanilla hooks**
- `EnvMan.SetForceEnvironment(string)`.
- `EnvMan.m_debugEnv`, used by the console `env` command.
- `EnvZone.m_force` for local forced-environment zones.
- Environments are defined as `EnvSetup` entries.

| Mod | Status | Notes |
|-----|--------|-------|
| [WeatherStones](https://thunderstore.io/c/valheim/p/GoldenJude/WeatherStones/) (GoldenJude) | 0.1.2, 2022-10-29, deprecated | A craftable menhir: type a weather name to force it in a radius. The closest concept to the idea, but dead. |
| [Rune Magic](https://thunderstore.io/c/valheim/p/hyleanlegend/Rune_Magic/) (hyleanlegend) | 1.5.1, 2026-09-15, 1.0 tagged | Runes learned from Runestones with a Rune Focus. Canopy blocks rain locally; others give Calm Waters, Dry Land, Ocean Currents and more. A strong lore-friendly design. |
| [Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/) (shudnal) | 1.10.1, 2026-09-28, 1.0 tagged | Seasonal weather weights, frozen water, seasonal trader stock and raids. Shows how to control environments properly. |
| [WeatherTweaks](https://thunderstore.io/c/valheim/p/Alastor/WeatherTweaks/) / [WeatherTool](https://www.nexusmods.com/valheim/mods/371) | 2023 deprecated / 2021 | Weather weight config and console commands. |

**Inspiration**

- **Idea:** an Elemental Magic staff that, when channeled, shifts the weather: "Clear Skies", "Call Storm" or "Summon Fog".
- **Rules:** it lasts N minutes, costs eitr plus a consumable reagent, and has a cooldown. The duration and radius scale with skill.
- **Sync:** the server owns the forced environment, either as a local `EnvZone`-like radius or regionally.
- **Uses:**
  - clear skies for sailing (idea 5);
  - storms that strengthen lightning magic or trigger Thor strikes;
  - fog for stealth.
- **Borrow:** Rune Magic's lore framing.
- **Avoid:** WeatherStones' type-a-string UI.

## 15. Compendium (New) — coverage: partial

**Vanilla hooks**
- The Compendium is `TextsDialog`: lore texts, active effects, and a stats page split by difficulty category.
- 1.0 added an achievements UI (`AchievementsGui`).
- The trophy panel exists.

| Mod | Status | Notes |
|-----|--------|-------|
| [Almanac](https://thunderstore.io/c/valheim/p/RustyMods/Almanac/) (RustyMods) | 3.8.0, 2026-09-25, 1.0 tagged | Index of all items, pieces and creatures built on the trophy panel. Metrics and achievements, plus bounties, quests, NPC dialogue and a store. YAML-driven; 354 other mods depend on it. [GitHub](https://github.com/RustyMods/Almanac). |
| [VNEI](https://thunderstore.io/c/valheim/p/MSchmoecker/VNEI/) (MSchmoecker) | 0.17.6, 2026-09-10, 1.0 tagged | "Not Enough Items": a UI with every item and recipe, search, and where each item comes from and is used. |
| [TrophyTooltip](https://thunderstore.io/c/valheim/p/vaSto/TrophyTooltip/) (vaSto) | 0.1.2, 2026-02-20, pre-1.0, AI | Trophy tooltips with HP, weaknesses, resistances, immunities and drops. |
| [TrophyHuntMod](https://thunderstore.io/c/valheim/p/oathorse/TrophyHuntMod/) | 0.12.1, 2026-09-28 | Kills and drop rates in trophy tooltips. |

**Inspiration**

- **Idea:** a discovery-driven, spoiler-safe bestiary and atlas, like Hollow Knight's Hunter's Journal.
- **Unlocks:** an entry appears when you first meet or kill a creature. Knowledge tiers, driven by vanilla kill counts (idea 3), reveal HP, resistances, the drop table, variants (idea 13) and boss trial records (idea 4).
- **Atlas pages:** discovered POIs and dungeons, and lore you have collected.
- **UI:** add tabs to the native Compendium (`TextsDialog`) instead of opening a separate window.
- **Difference from existing mods:** Almanac is huge and admin-oriented, and VNEI is recipe-first and full of spoilers. Ours stays progression-gated and in the vanilla style.

## 16. New ability depending on skill level (New) — coverage: partial

Level-gated perk systems exist, but they mostly cover gathering and combat. The movement and exploration skills get little.

**Vanilla hooks**
- `Skills.SkillType` has Run, Jump, Sneak, Swim, Ride, Fishing, Dodge and Crafting (Crafting and Dodge are new in 1.0), but no Sailing.
- Skill effects are mostly linear curves through `Skills.GetSkillFactor`.

| Mod | Status | Notes |
|-----|--------|-------|
| [ImpactfulSkills](https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/) (MidnightMods) | 0.20.2, 2026-09-28, 1.0 tagged | Level-gated unlocks: AOE mining at 50, AOE harvest and multi-plant at 25, sneak noise reduction at 50. New Voyager, Hauling and Forging skills with milestones. |
| [GrindstoneSkills](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) (MilkyTeam) | 0.9.1, 2026-09-27, updated post-1.0 | Milestones at 25/50/100 for each skill: sailing Lookout, legendary fish, starred crops, a "last stand" for Defense, twin births for Husbandry. |
| [Exploration](https://thunderstore.io/c/valheim/p/Smoothbrain/Exploration/) (Smoothbrain) | 1.0.4, 2026-02-05, pre-1.0 | Explore radius and movement speed. Level 20: record to tables. Level 40: copy maps. Level 50: chance of double chest loot. Also grows the Wishbone radius. |
| [NorntasticSkillGrowth](https://thunderstore.io/c/valheim/p/Dafini/NorntasticSkillGrowth/) (Dafini) | 0.14.2, 2026-09-17, AI | Skyrim-style perk trees for every skill, with prestige and respec. |

**Inspiration**

- **Idea:** discrete, visible unlocks for the *exploration* skills, announced by a message when reached:
  - Run: "second wind" at 50;
  - Jump: ledge mantle at 50, a roll that cancels fall damage at 75;
  - Swim: vertical diving and longer breath, linked to idea 8;
  - Sneak: hide in tall grass;
  - Ride: mount sprint;
  - Sailing: from idea 5.
- **Rules:** thresholds are configurable and server-synced.
- **Avoid:** hidden percentage creep, which is what most mods do, and perk-tree UI bloat.

## 17. Better shops (New) — coverage: partial

**Vanilla hooks**
- Each `Trader` has a list of `TradeItem` entries with `m_requiredGlobalKey` and `m_buyKey`. `m_buyKey` makes one-time purchases possible, such as 1.0's inventory upgrades from Haldor.
- The traders are Haldor, Hildir and the Bog Witch.

| Mod | Status | Notes |
|-----|--------|-------|
| [TradersExtended](https://thunderstore.io/c/valheim/p/shudnal/TradersExtended/) (shudnal) | 2.0.4, 2026-09-24, 1.0 tagged | Per-trader buy and sell lists, a two-column UI, custom currencies, repairs, finite trader balances that replenish, discounts and markups, buyback, gating by discovery or global key. JSON, YAML or CSV. [GitHub](https://github.com/shudnal/TradersExtended). |
| [EpicLoot](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/) (RandyKnapp) | 0.14.13, 2026-09-24, 1.0 tagged | Adventure Mode at Haldor: a secret stash, gambling, treasure maps and bounties, with a token economy. |
| [Haldor Overhaul](https://thunderstore.io/c/valheim/p/ProfMags/HaldorOverhaul/) / [Trader Overhaul](https://thunderstore.io/c/valheim/p/ProfMags/TraderOverhaul/) (ProfMags) | 1.0.19, 2026-02-24 / 0.0.9, 2026-03-11, pre-1.0, AI | 580+ items priced from their recipes, search and tabs, one UI for Haldor, Hildir and the Bog Witch. |
| [Better Trader](https://thunderstore.io/c/valheim/p/Menthus/Better_Trader/) (Menthus) | 2.0.4, 2021-05-18, deprecated | The original overhaul: 310+ items, price fluctuation, discovery gating. |

**Inspiration**

- **Principle:** keep vanilla's scarcity. A shop should be a treat, not a store that sells everything.
- **Ideas:**
  - stock that rotates daily;
  - a specialty for each trader;
  - reputation earned from purchases and quests (idea 18) that unlocks tiers;
  - low sell-back prices limited by the trader's finite coin, as in TradersExtended;
  - a new **traveling sea trader** found by ship, which ties into Exploration.
- **Study:** TradersExtended is excellent; learn from it rather than compete with it on generic configuration.

## 18. Late game quest (New) — coverage: partial

**Vanilla hooks**
- Hildir's quests are already a quest pattern: find her chests in dungeons, and the returned chests unlock new stock through keys.
- `Trader.TradeItem.m_buyKey` and global keys handle persistence.

| Mod | Status | Notes |
|-----|--------|-------|
| [Almanac](https://thunderstore.io/c/valheim/p/RustyMods/Almanac/) (RustyMods) | 3.8.0, 2026-09-25, 1.0 tagged | YAML quests (Collect, Farm, Harvest, Kill, LearnItems, Mine), NPC dialogue trees with requirements and commands, bounties that spawn a target with a map pin, treasure hunts, a token store. |
| [EpicLoot](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/) Adventure Mode | 0.14.13, 2026-09-24, 1.0 tagged | Haldor bounties spawn a mini-boss and its minions at a random spot in the chosen biome. Treasure maps place a chest in a biome. Paid in tokens. |
| [Marketplace And Server NPCs Revamped](https://thunderstore.io/c/valheim/p/KGvalheim/Marketplace_And_Server_NPCs_Revamped/) (KGvalheim) | 9.8.1, 2026-04-25, deprecated | The classic server NPC framework: quest NPC, custom quest types and rewards, trader, banker, teleporter, gambler. Dead. |
| [HaldorBounties](https://old.thunderstore.io/c/valheim/p/ProfMags/HaldorBounties/) (ProfMags) | 0.0.5, 2026-03-09, pre-1.0, AI | A daily bounty board at Haldor: kill contracts, player-style minibosses, raid warbands, pick 1 of 4 rewards. |

Also: TraderQuest (RustyMods, deprecated 2025-03) and [Lost Scrolls II](https://thunderstore.io/c/valheim/p/TaegukGaming/Lost_Scrolls_II/) (AI, bounty board with Dvergr targets).

**Inspiration**

- **Idea:** a late-game contract board at a trader or a new NPC, unlocked after the Queen, Fader or Deep North boss.
- **Contract types:**
  - kill a named elite, using our mob variants;
  - deliver a rare item;
  - scout a far location (ties to Cartography and the Spyglass);
  - dive for a relic (ties to the Underwater biome).
- **Rewards:** rare, unique trinkets and crafting materials.
- **Borrow:**
  - EpicLoot's world-spawned target with a map pin;
  - Almanac's YAML quest schema;
  - Hildir's key-based persistence.
- **Ship curated content.** Do not rely only on admin-authored quests.
- **Avoid:** MMO-style token economies that bypass progression.

---

## Notable mods to study

These are well-engineered, 1.0-compatible and open-source mods, most relevant to the Exploration category.

1. **[Expand World Data](https://thunderstore.io/c/valheim/p/JereKuusela/Expand_World_Data/)** by JereKuusela ([GitHub](https://github.com/JereKuusela/valheim-expand_world_data)). YAML-driven world generation: biomes, locations, dungeons and events (events were merged in 1.73). The reference for the Underwater biome, the Deep North revamp and custom dungeons. Jere's Dungeon Splitter, Upgrade World and Render Limits are also worth reading.
2. **[Better Cartography Table](https://thunderstore.io/c/valheim/p/nbusseneau/Better_Cartography_Table/)** by nbusseneau ([GitHub](https://github.com/nbusseneau/BetterCartographyTable), MIT). A clean, well-documented example of per-pin sharing stored on ZDOs that stays compatible with vanilla data. It has not been updated for 1.0 yet.
3. **shudnal's suite**: [HipLantern](https://github.com/shudnal/HipLantern), [ExtraSlots](https://thunderstore.io/c/valheim/p/shudnal/ExtraSlots/), [TradersExtended](https://github.com/shudnal/TradersExtended), [Seasons](https://github.com/shudnal/Seasons) and [LongshipUpgrades](https://thunderstore.io/c/valheim/p/shudnal/LongshipUpgrades/). Prolific and updated for 1.0 within days. Useful patterns: custom equipment slots, environment and weather control (Seasons), trader UI, config sync through ConditionalConfigSync.
4. **Advize**: [Spyglass](https://thunderstore.io/c/valheim/p/Advize/Spyglass/), [CartographySkill](https://thunderstore.io/c/valheim/p/Advize/CartographySkill/), PlantEverything ([GitHub](https://github.com/AdvizeGH/Advize_ValheimMods)). Custom items with camera zoom and a custom skill with ServerSync, all 1.0-tagged. The license is **GPL-3.0**: study it, do not copy it.
5. **MidnightMods**: [StarLevelSystem](https://thunderstore.io/c/valheim/p/MidnightMods/StarLevelSystem/), [ImpactfulSkills](https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/) and [ValheimFortress](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimFortress/) ([GitHub](https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded)). A creature-modifier system with a public API (for mob variants and boss trials), level-gated skill unlocks, and arena wave challenges.
6. **[BetterDiving](https://thunderstore.io/c/valheim/p/MainStreetGaming/BetterDiving/)** ([GitHub](https://github.com/humansandbag/Valheim-Better-Diving-Mod), Apache-2.0). A mature diving and oxygen implementation. It is the base or reference for ideas 8 and 11.
7. **[Almanac](https://thunderstore.io/c/valheim/p/RustyMods/Almanac/)** by RustyMods ([GitHub](https://github.com/RustyMods/Almanac)). A large YAML-driven framework for achievements, bounties, quests, dialogue and an index UI, with 354 dependent mods. It shows both what players expect and the feature-bloat trap.
8. **ASharpPen**: [Spawn That](https://thunderstore.io/c/valheim/p/ASharpPen/Spawn_That/) and [Custom Raids](https://thunderstore.io/c/valheim/p/ASharpPen/Custom_Raids/) (plus Drop That). Robust, config-driven spawn and raid systems with global-key conditions, the foundation for Deep North dormancy and regional variants.

## Cross-cutting takeaways

- **Several ideas marked "No" already exist.** The Spyglass exists in full; the cartography ideas and "sleep anytime" exist in part. The hip lantern and diving mods are well served. Build these only where we have a clear integration angle.
- **Vanilla 1.0 already contains data and systems worth building on:**
  - per-creature kill stats in `PlayerProfile`;
  - `AltBiome` sector modifiers;
  - the `SimulationDistance` setting;
  - Hildir-style key-based quests;
  - `m_buyKey` one-time purchases.
- **Much of the 2026 catalogue is tagged AI-generated** (Smooth Sailing, HelmWind, Njord, Dive In, HaldorBounties, the Trader and Haldor overhauls, EliteCreaturesReborn, NorntasticSkillGrowth, Cartur's Map Pins, Dungeon Creation Kit, TrophyTooltip, KillCountEvolution). Good design ideas, uncertain quality: a well-engineered, cohesive and integrated suite is a real differentiator.
- **Many staple mods died before or at 1.0:** CLLC, the KG Marketplace, Smoothbrain's Sailing, Enhanced Bosses, WeatherStones, Render Limits. Those niches are open again.
