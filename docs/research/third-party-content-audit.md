# Third-party content audit (2026-10-16)

Question: does every MC mod pick up the content that other mods add (weapons, shields, creatures, pieces, plants,
recipes, drops), the way the Encyclopedia already lists Warfare's recipes and drops? Method: 7 auditors (4 mods
each) read every mod's code, README and design doc against the decompiled game and the decompiled Warfare 1.9.4 and
Jotunn libraries; 7 skeptics checked every claim and listed what the auditors missed. Runtime facts come from an
in-world run on 2026-10-16 (Valheim 1.0.20) with Warfare, Jotunn and other mods installed.

User decisions (2026-10-16): mods detect content by its game data, never by a table of vanilla names; name lists stay
as add/exclude overrides; mods rebuild their lists when content changes after the world loads; a stand-in content mod
(`tests/Probes/Core.Probe.Content`) plus checks on real installed mods (Warfare) prove it. Tower Shield Wall's rule:
every shield with parry bonus 0 is a tower shield (built as a parry bonus below 1, so the iron square shield, parry
bonus 1, is left out); creature-only copies (carried by a creature, no recipe) stay normal; a tower without a bash
damage in `Towers` gets one scaled by its block armor. Crossbow Stays Loaded will keep the load of every reload weapon
without eitr cost (replaces the 2026-09-28 choice "modded reload weapons by name").
Order of work: Tower Shield Wall and the test kit first; the other mods follow in waves the user approves.

## How content mods register content

| Library | Items and recipes | Pieces | Creatures and spawns | Late changes |
|---|---|---|---|---|
| blaxxun ItemManager (Warfare) | `ObjectDB.Awake` prefix, `CopyOtherDB` postfix (priority 700) | PieceManager, same idea | CreatureManager | Server-synced recipe config edits existing `Recipe` objects in place (enabled, station, resources) |
| Jotunn | `ObjectDB.Awake` prefix; mods edit item data in `OnItemsRegistered` = `ObjectDB.Awake` postfix priority 0 (runs after MC postfixes at Low) | `ObjectDB.Awake` postfix priority 200 (same as Priority.Low) | `SpawnSystemList` kept on an inactive object and injected into each `SpawnSystem` in a `SpawnSystem.Awake` prefix: not on the zone controller prefab | Custom build categories appear only when a build tool is equipped |
| Server-synced editors (Wacky's Database, spawn and creature editors) | Add or edit items after join | | Edit spawn entries in place | Counts may stay the same |

Consequences for MC mods: read item data after every `ObjectDB.Awake` postfix (Jotunn edits come last); read spawn
lists from `SpawnSystem.m_instances` as well as the zone controller prefab; a rebuild trigger based only on counts
misses in-place edits (recipe state, spawn entries).

## Mods that already pick up third-party content (tests to add)

| Mod | Why it already works | Notes from the skeptics |
|---|---|---|
| Sneak Ambush | Every rule reads game data (victim health, `HitData.m_ranged`, `BaseAI`, `IsBoss`) | Sneak XP skips the TrainingDummy faction, which also covers the vanilla FrostWisp and Frysling (probably unintended); `m_aiSkipTarget` would match the intent |
| Trinkets on Demand | Trinket = any item with `m_fullAdrenalineSE`, read live | Bows without a draw attack get no catch-up (Warfare greatbows unverified) |
| Stats per Creature | Reads the game's own kill table | An untranslated `$` token shows as `[token]` under Creatures |
| One-Click Repair All | Presses the vanilla repair, which decides by recipe and station | Warfare disables six vanilla recipes; those items still repair (vanilla rule) |
| Batch Station Feeding | Component types and capacity fields, at hover time | Only the Debug coverage dump is timing-sensitive |
| Auto Pickup Filter | Item catalog from `ObjectDB`, rebuilt after load | |
| Sailing Skill, Sleep Through Day, Swimming Dive, Breeding Star Inheritance, Fishing Fight, Harpoon Hooks Tames | No content tables | |
| Music Instruments | Own content (clones of `KnifeFlint`); materials and station are config | Recipe should rebuild when a named material or station arrives late; edits other mods make to `KnifeFlint` leak into the instruments |
| Cheated Tag Remover | Not applicable | |

## Mods with gaps

| Mod | Missed today | Proposed data rule | Vanilla changes to confirm | Wire | Size |
|---|---|---|---|---|---|
| **Tower Shield Wall** (done in 0.2.0) | Only the 7 listed towers; Warfare's Bronze, Chitin and Carapace towers stayed normal | Shield (type, pose, Blocking skill) with parry bonus below 1, read from the vanilla snapshot; not creature-only (carried by a creature and no recipe); `Towers` becomes add/override, new `ExcludedTowers`, `AutoTowers`; bash damage interpolated from block armor over the vanilla towers (Bronze 8.4, Chitin 10.2, Carapace 24.4); catalog built after Jotunn's late edits and rebuilt when content changes | None (the 7 towers keep their values; `FW_`/`SP_` creature copies stay normal) | Rules layout 3, network version bump | Medium |
| **Weapons Moveset** | Weapons with custom attack animations (Warfare's dual weapons, war pikes, staffs) get no jump or roll attacks | Unknown animation name: family from the weapon's animation state, then its skill | None (fallback only for unknown names) | Layout 3, network version bump | Medium |
| **Crossbow Stays Loaded** | Reload weapons that are not crossbows (modded hooks, guns) unless named | Any `m_requiresReload` weapon without eitr cost; `ExtraItems` default empty | Reverses the 2026-09-28 decision "modded reload weapons by name" | Client | Small |
| **Dual Wield** | Content that arrives after load: exclusions, back cross and trails remember "no" | Rebuild caches on content change; no false "unknown name" warning before spawn | None | None | Small |
| **Creatures Morale** | Every modded creature (79 vanilla names in home lists) | Home rank from the creature's own enabled world-spawn entries (lowest land biome); unlisted creatures count as elites; new `NeverAfraid` | Jotun invasion creatures and Barka would be ranked: exclude or confirm | Layout 3, network version bump | Large |
| **Forge Idol Upgrades** | Modded trophies never pay for idol upgrades; a modded catalyst gets the plain-idol chance | Auto trophy pools from creature drops (rule still to settle: the user's curation is not health-based); catalyst chance rule needs design (raw `m_upgradeChance` is unsafe) | Settled per rule | Layout 3, network version bump | Large |
| **Deep North Awakening** | Modded Jotun army members, Deep North nature, invasion stones | Army = Deep North faction creatures of the `jotun_invasion` spawn entries; nature = Deep North faction monsters with natural Deep North spawns; stones = prefabs with `TriggerPersistentEventOnDestroy` | Moose would join nature (exclude by default) | Layout bump | Large |
| **Switchable Lights** | Modded torches and lanterns | Fireplace with fuel, not infinite, no heat or burning area, no ignite; `Lights` becomes add list, new `ExcludedLights` | To confirm with a dump of the 9 vanilla lights | Layout bump | Medium |
| **Cultivator Replant** | Modded wild plants; other cultivators | Regrowing `Pickable` vegetation; replant tool = item whose piece table holds a `Plant` | Unverified false positives (tar, eggs): restrict to vegetation | Layout bump | Large |
| **Crafting Search & Sort** | Custom-skill weapons (Warfare throwing axes, scythes) only under "Weapons" | One sort option per custom skill | None | Client | Medium |
| **Sort Chest** | Drops of Jotunn creatures and items with only disabled recipes sort as Unknown | Read spawn lists from `SpawnSystem.m_instances`; disabled recipes as a last pass; content fingerprint once per sort | Disabled-recipe pass to dump before/after | Client | Medium |
| **Encyclopedia** | Jotunn creatures land in "Habitat unknown"; in-place recipe edits and late build categories not seen | Spawn lists from `SpawnSystem.m_instances`; fingerprint adds a recipe-state hash and category counts | None | Client | Medium |
| **Distant Horizons** | Content registered after the first look stays skipped; modded meshes without CPU read access | "Missing" sentinel and fingerprint; unreadable meshes need a dump first | None | Client | Small + medium |
| **Spyglass** | Late materials make the recipe cheaper; a late station hides it | Rebuild the recipe on content change | None | Server rules unchanged | Small |
| Shared `ItemKinds` | A combat scythe may be filed as a tool (unverified) | Only after a dump of Warfare's scythes | Affects Weapons Moveset | | Small |

## Known limits

- A content mod that clones a vanilla tower after Tower Shield Wall wrote it gets a copy with tower data and no
  snapshot (server-synced item editors). Not detectable generically.
- Count-based fingerprints miss in-place edits by server-synced spawn or creature editors (boss order, spawn biome).
- Biomes added by world mods have no rank in progression; content found only there stays "Unknown".

Per-mod details (evidence with file and line, skeptic corrections) were kept in the session's audit files; the
design doc of each mod records its decisions when it changes.
