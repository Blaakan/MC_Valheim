# Creature Kill and Tame Counts — design

| | |
|---|---|
| Mod | Creature Kill and Tame Counts |
| GUID / project | `MC.Exploration.Stats.PerCreature` (`src/Exploration/Stats.PerCreature/`) |
| Category / scope | Exploration / QoL |
| Side | Client (only the player who wants it installs it) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` + `assembly_guiutils` in `.ref/`; localization strings and prefab names checked in the installed game data (`resources.assets` localization table, `StreamingAssets/SoftRef/manifest_extended`) |
| Status | Implemented (0.1.0), smoke test passed, in-game testing pending |

## Goal

Requirements from the user (2026-09-28):

1. The existing player statistics screen (Compendium → **Player Statistics**, built by `TextsDialog.AddStats`) shows a
   **per-creature list**: for every creature the character has killed, its name (translated) and the number of kills.
2. A future MC Compendium mod can use this data **as is**: a small, stable, documented public read API
   (`CreatureCounts`) plus a documented storage format for the data the mod stores itself. The mod's own page is
   built **only through that API**, so every display test also tests the API.
3. **Taming numbers** (investigated: feasible): the same list shows, per creature, how many the character has tamed.
   Vanilla only keeps a total (`PlayerStatType.CreatureTamed`), so per-creature tames are counted by the mod from the
   moment it runs, and saved with the character.
4. The mod can be turned off on its own, live, like every MC mod (framework toggle): off = the vanilla page and no
   tame counting.

The user assumed the game does not track per-creature kills and that counting would start at install. **It does
track them**: per-creature kill totals are saved since the Call to Arms update (character file version 42), and the
weapon type of each kill since the Deep North update (file version 46). The list is therefore retroactive (all-time
counts). A "kills since install" option was considered, then dropped (Decision 1).

Non-goals: counts on the enemy nameplate or in trophy tooltips (left to the Compendium idea), a new Compendium page or
bestiary, kill milestones or rewards, server-side or shared (world) counters, other players' counts, changing how
vanilla credits kills, counting creatures born, hatched, grown or summoned already tame.

---

## 1. Vanilla behaviour (code trace)

### 1.1 Where per-creature kills live

- `PlayerProfile.m_playerStats` is `PlayerStats[10]`, one per `DifficultyRequirement` (`RawStats, Any, Hammer,
  Casual, VeryEasy, Easy, Default, Hard, VeryHard, Hardcore`). **Index 0 (`RawStats`) is the lifetime tally** and is
  always written.
- `PlayerProfile.PlayerStats.m_enemyStats` is `Dictionary<string, float>[5]` **indexed by `KillModifiers`**:
  `0 MixedAndTotal` (total of every kill), `1 Unarmed`, `2 Magic`, `3 Ranged`, `4 Melee` (`5 CountNone` is a "not set
  yet" marker, never a bucket). Key = `Character.m_name` of the victim, the localization token (e.g.
  `$enemy_greyling`); star levels and many variants share one `m_name`.
- `PlayerProfile.IncrementStatEnemy(name, amount, modifier, cheated)` always adds to slot 0 `[0]`, and to slot 0
  `[(int)modifier]` when the modifier is a single family. Only when `Achievements.CanGetAchievements(cheated)` is true
  does it also write slot 1 and every slot from 3 up to the current difficulty index. `CountNone` logs an error and
  returns after the total. So for one creature `total >= unarmed + magic + ranged + melee`; the difference is what
  this mod calls **other**.
- Saving: `PlayerProfile.SavePlayerToDisk` writes format 46 (`Version.Player.DeepNorth`) with all slots and buckets.
  `PlayerProfile.LoadPlayerFromDisk` reads all 5 buckets only for file versions 46 and 44 (`AbandonedDN`, a Deep
  North test format); versions 42, 43 and 45 (`CallToArms`, `Celebration`, `Chunked`) keep only slot 0 `[0]`; older
  files have no per-creature data. So kills made between Call to Arms and Deep North have a total but no weapon type.
- `PlayerProfile.GetStat` returns the **current difficulty slot** when achievements are allowed, not slot 0: never
  use it for lifetime numbers.
- `Achievements.ResetAchievement` / `ResetAllAchievements` set values to 0 in every slot and keep the keys: rows with
  0 must be hidden.

### 1.2 Kill flow — who gets the credit

1. `Character.RPC_Damage` (on the victim's ZDO owner): a hit whose attacker is a `Player` sets the ZDO attacker flag
   for that player name and bumps ZDO `Attackers`. This happens before resistances and before `ApplyDamage` returns
   early for damage <= 0.1, so **a zero-damage hit counts**. For non-player victims it also tracks the weapon family
   in ZDO `Modifiers` (default `CountNone`): the first hit sets the family, a hit of another family sets
   `MixedAndTotal`. Family from `hit.m_skill`: Swords/Knives/Clubs/Polearms/Spears/Blocking/Axes/Pickaxes/WoodCutting
   → Melee; Bows/Crossbows → Ranged; ElementalMagic/BloodMagic → Magic; Unarmed → Unarmed only for a hit marked by
   `Attack.DoMeleeAttack` for the weapon whose `m_shared.m_name == "Unarmed"`; any other Unarmed-skill weapon counts
   as Melee. That the bare-fists item is named `"Unarmed"` and that `FistFenrirClaw` uses the Unarmed skill is prefab
   data (unverified; test T04).
2. `Character.SetHealth` on the owner sets `Modifiers` back to `MixedAndTotal` whenever health reaches max: a creature
   that regenerated to full health after being hurt is a mixed ("other") kill whatever finishes it. Attacker flags
   are never cleared.
3. `Character.OnDeath`, on the owner only: for **every connected player** (`ZNet.GetPlayerList`) whose attacker flag
   is set, the local player gets `Game.RPC_RegisterKill` directly, the others get it through the routed RPC
   `Game.RegisterKill`. **Every player who hit the creature gets one full kill**; players who logged out before it
   died get nothing.
4. `Game.RPC_RegisterKill` (on the credited player's client) → `PlayerProfile.IncrementStatEnemy` + `EnemyKills` or
   `BossKills`. Bosses are in the same dictionary.
5. **PvP**: `Character.OnDeath` has a branch that records a killed player's name in `m_enemyStats`, but
   `Player.OnDeath` overrides it without calling the base method, so in 1.0.16 **PvP kills are not recorded per
   victim**. Older saves may still hold player names as keys.
6. No credit: kills by tames or other creatures (attacker is not a `Player`), the console `killall` / `killenemies` /
   `killtame` (no attacker; you still get the kill if you had hit that creature before). `killall` and `killtame`
   kill every non-player character within 1000 m, tames included; `killenemies` spares tames. Kills with cheats on
   still count in slot 0 (the `cheated` flag only gates the achievement slots).
7. `EnemyKillsLastHits` / `BossLastHits` only count kills where the victim's owner landed the last hit. Not used.

### 1.3 The existing Player Statistics page

- `InventoryGui.OnOpenTexts` → `TextsDialog.Setup` → `TextsDialog.UpdateTextsList`: known lore texts sorted by
  topic, then `AddLog` and `AddActiveEffects`, then `AddStats` appended last. Built **once per open**, not refreshed
  while open.
- `TextsDialog.AddStats` (private) adds one `TextInfo` whose topic is `Localize("$inventory_stats")` = "Player
  Statistics" and whose text is one big hardcoded-English dump per difficulty slot. Its "Enemies:" block prints only
  the bucket names, **never the per-creature values**. It also writes the whole text to the log on every open.
- `TextsDialog.ShowText` localizes topic and text again before showing them (TMP, rich text on). Mouse and gamepad
  (right stick scrolls the text) are handled by the dialog itself.

### 1.4 Taming

- `Tameable.TamingUpdate` (every 3 s, on the creature's **ZDO owner**) lowers the remaining time while the creature
  is fed and not alerted, and calls `Tameable.Tame()` at 0. `ItemDrop` stores no dropper: **the game never knows who
  fed the creature**.
- **The owner is not "the player at the pen".** A peer keeps a ZDO while it stays in that peer's active area
  (`ZDOMan.ReleaseNearbyZDOS`). In multiplayer the owner can be a player 100+ m away while the one who fed the
  creature stands next to it.
- `Tameable.Tame()` (private):
  1. `Game.IncrementPlayerStat(CreatureTamed)` **first, unconditionally**, on the owner's machine (so the vanilla
     total goes to the owner, even when the call tames nothing).
  2. Guard `m_nview.IsValid() && m_nview.IsOwner() && m_monsterAI && m_character && !IsTamed()`, then
     `MonsterAI.MakeTame()`. The tamed state is set synchronously (the RPC targets the owner, which is this machine),
     so `IsTamed()` is already true when `Tame()` returns.
  3. `Player.GetClosestPlayer(position, 30f)` (closest loaded player strictly within range; `Player.s_players` also
     holds remote players loaded near this machine) gets `Message(Center, m_name + " $hud_tamedone")` → "Boar has
     been tamed". This is the **only** use of `$hud_tamedone`.
- Message delivery: `Player.Message` on the local player → `MessageHud.ShowMessage`; on a remote player → ZNetView
  RPC `"Message"` → `Player.RPC_Message` on that player's client → `MessageHud.ShowMessage`. The text travels as the
  raw token string. `MessageHud.ShowMessage` returns early while the HUD is hidden (Ctrl+F3).
- `Tameable.TameAllInArea` (console `tame`) ignores its point and radius and calls `Tame()` on **every loaded**
  tameable character: only owned, untamed ones are tamed (wild ones nearby included), but each call bumps the vanilla
  total.
- Tamed without `Tame()` (no stat, no message): `m_startsTamed` summons, `Procreation` offspring, `EggGrow`
  hatchlings, `Growup` adults.

### 1.5 Per-character custom storage

`Player.m_customData` (`Dictionary<string, string>`) is written by `Player.Save` and read by `Player.Load`, inside the
player data blob that `PlayerProfile.SavePlayerData` stores in the `.fch` (autosave, logout, and `Game._RequestRespawn`
before the dead player is destroyed). Unknown keys are kept by vanilla. At spawn, `Game.SpawnPlayer` calls
`PlayerProfile.LoadPlayerData` just before `Player.OnSpawned(bool)`. Between `Game._RequestRespawn` and the next
spawn, `Player.m_localPlayer` is null while the game and profile still exist.

### 1.6 Multiplayer summary

| Data | Who writes it | Where it lives |
|---|---|---|
| Per-creature kills | Each credited player's own client, from the victim owner's routed RPC (vanilla, even on vanilla servers and from unmodded owners) | That player's `.fch` |
| Vanilla `CreatureTamed` total | The creature owner's client | Owner's `.fch` |
| Tame message | Sent by the owner's game to the closest player within 30 m | Shown on that player's client |
| Mod's per-creature tames and start date | The local client only (3.1) | Local `Player.m_customData` |

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28 (Thunderstore pages, GitHub source; GitHub code search for patches on the same methods).

| Mod | How | Lesson |
|---|---|---|
| DudeWhatAreMyStats (DeathMonger) | Own stats panel, reads `m_playerStats[0].m_enemyStats[0]`, top N creatures | Confirms slot 0 = lifetime and that `GetStat` returns the difficulty slot; never sum slots. Separate window, not the Compendium. |
| TrophyHuntMod (oathorse) | Trophy tray tooltips with kills; keeps its own records for its tournament modes | Own kill tracking is only needed for special rules; we read vanilla. Its numbers can differ from ours. |
| Almanac (RustyMods) | Large creature/item index with player metrics | Huge all-in-one; the research notes it keeps counters of its own. We stay tiny and inside the vanilla page. |
| SlayerSkills (Neobotics) | Per-creature points from trophy drops | Different data; no conflict. |
| EquipmentAndQuickSlots (RandyKnapp), SecondaryAttacks (sighsorry) | `TextsDialog.UpdateTextsList` postfix: remove entries / add a page | Other mods edit `m_texts` after vanilla built it. We only rewrite the text of the stats entry, found by topic, from an `AddStats` postfix: compatible with both. |
| Taming mods | No public source found patching `Tameable.Tame` | Our observe-only prefix/postfix on `Tame` is low risk. Mods that tame without `Tame()` are not counted. |

No existing mod shows per-creature numbers inside the vanilla Player Statistics page, and none counts tames per
creature.

---

## 3. Implemented design

### 3.1 Core idea

- **Kills: read, never write.** The per-creature kill counts are vanilla data (`m_playerStats[0].m_enemyStats`),
  already saved and already credited by the game's multiplayer rules. The mod only displays them, all-time.
- **Tames: counted locally from the first run.** The mod listens for the game's own "`<creature> has been tamed`"
  message addressed to the local player. That message is sent by the creature owner's game, **modded or not**, to the
  closest player within 30 m: the player vanilla itself congratulates gets the tame. When nobody is within 30 m
  (vanilla sends no message), the player whose game ran `Tame()` (the owner) gets it, **only if the owner is the
  closest player loaded on the owner's game**; if another loaded player is closer, **nobody** gets it. Each tame is
  credited to at most one player.
- **Display:** a "Creatures" section prepended to the text of the vanilla Player Statistics entry, built each time the
  Compendium opens, only through the public API. No new UI objects.
- **API:** the public static class `CreatureCounts` reads everything (vanilla kills, stored tames, start date) for the
  page and for a future Compendium.

Code: `Plugin.cs` (config, `OnActivated` / `OnDeactivated`), `CreatureCounts.cs` (the only public type),
`CounterStore.cs` (tames and start date in `Player.m_customData`, parse cache), `StatsSection.cs` (page text, reads
only `CreatureCounts`), `Patches/` (one file per patched class).

### 3.2 Patches

All bodies catch their own exceptions (`PatchGuard.Report`). Applied only while the feature is Active (framework):
turned off = vanilla page, no counting. No transpiler, no per-frame patch, default Harmony priority.

| Target | Type | What it does |
|---|---|---|
| `TextsDialog.AddStats()` (private) | Postfix | Searches `m_texts` from the end for the entry whose topic is `Localize("$inventory_stats")` and prepends `StatsSection.Build()` to its text. Not found (another mod removed it): nothing, one debug line. A postfix of `AddStats`, not `UpdateTextsList`, so it runs before other mods' `UpdateTextsList` postfixes. |
| `Player.Message(MessageType, string, int, Sprite, bool)` | Postfix | If the type is Center, the text ends with `" $hud_tamedone"` (ordinal) and the instance is `Player.m_localPlayer`: one tame of the name before the suffix. Local path (we own the creature and are the closest). The local-player check ignores messages our game sends to remote players. |
| `Player.RPC_Message(long, int, string, int)` (private) | Postfix | Same test. Remote path: another game (modded or not) tamed it and we were the closest. |
| `Tameable.Tame()` (private) | Prefix + Postfix | Prefix stores the vanilla guard ("this call will tame") in `__state`. Postfix, only if `__state`: the creature is really tamed (`IsTamed()`, in case another mod skipped the original), a local player exists, `GetClosestPlayer(pos, 30f)` is null (nobody got the message) and `GetClosestPlayer(pos, float.MaxValue)` is the local player → one tame for the local player ("owner" fallback). |
| `Player.OnSpawned(bool)` | Postfix | For the local player: write the start date once (custom data is loaded just before). |

Both tame paths use vanilla's own 30 m test in the same frame, so they never both count the same tame. Hooking
`Player` rather than `MessageHud` means a tame is still counted while the HUD is hidden (Ctrl+F3) or when another mod
mutes `MessageHud.ShowMessage` (One Click Repair All does, during its repair loop).

### 3.3 Page text

Prepended to the vanilla page (TMP rich text; `<color=orange>` is what vanilla uses for its headers), default settings:

```
<color=orange>Creatures</color>
<size=85%><color=#B0B0B0>Kills: the game's own count for this character, in every world. Tames: counted while this mod is on (since 2026-09-28).</color></size>
    Greydwarf: 243 killed (melee 180, ranged 50, other 13)
    Neck: 40 killed
    Boar: 12 killed (melee 11, other 1), 3 tamed
    Wolf: 2 tamed
<size=85%><color=#B0B0B0>Other names (older PvP kills, or creatures without a translation key):</color></size>
    Bjorn: 1 killed
<empty line>
<empty line>
<vanilla text, unchanged>
```

- Built only through `CreatureCounts` (`GetCreatureNames`, `GetKillBreakdown`, `GetTames`, `CountingSince`).
- Rows = names with kills >= 1 or tames >= 1. Names starting with `$` (localization tokens: creatures and bosses) are
  translated and listed under "Creatures"; the rest (player names from older saves' PvP branch, modded creatures with
  a plain name) go under the grey "Other names" line, shown only when that group is not empty.
- `N killed` only when kills >= 1; `N tamed` only when tames >= 1. Whole numbers, invariant culture.
- Weapon types (setting `ShowWeaponTypes`): non-zero parts only, in the order melee, ranged, magic, unarmed, other,
  where **other** = total minus the four, never below 0, so the parts add up to the total. When melee, ranged, magic
  and unarmed are all 0, the parentheses are left out ("(other 40)" would say nothing).
- Sort (setting `SortBy`), inside each group: `MostKilled` = kills desc, then tames desc, then name; `Name` =
  translated name (current culture, ignore case), with an ordinal tie-break.
- Date: `CountingSince` as `yyyy-MM-dd` (invariant culture); missing → "since this mod was installed".
- Nothing to show → `    Nothing killed or tamed yet.`
- Labels are English, like the rest of the vanilla page; creature names are translated.
- One debug line per build: `Creatures section: <rows> rows (<other> other names).`

### 3.4 Configuration

| Section | Key | Default | Description (user-facing) |
|---|---|---|---|
| General | Enabled | `true` | Framework toggle, live. |
| General | Status | (read-only) | Framework status line. |
| Display | SortBy | `MostKilled` | "Order of the creature list in Player Statistics. MostKilled: the creatures you killed most come first. Name: alphabetical." (enum `SortOrder`) |
| Display | ShowWeaponTypes | `true` | "After each kill count, show how the kills were made: melee, ranged, magic, unarmed, or other (several weapon types, no weapon, or kills from before the game recorded weapon types, that is before the Deep North update). Left out when only other is known." |

Both display settings are read when the Compendium opens: a change applies the next time the Compendium is opened,
no restart. The MC Mods panel only has the `Enabled` toggle; the display settings are changed with ConfigurationManager
or in the cfg file (the framework reloads it while the game runs).

### 3.5 Persistence

| Data | Where | Written by the mod? |
|---|---|---|
| Kills per creature (+ weapon types) | Vanilla `m_playerStats[0].m_enemyStats[0..4]` in the `.fch` | **Never** |
| Tames per creature | `Player.m_customData["MC.Exploration.Stats.PerCreature.Tames"]` | On each counted tame |
| Day tame counting started | `Player.m_customData["MC.Exploration.Stats.PerCreature.CountingSince"]` | Once per character: first spawn with the mod, the feature turned on mid-game, or the first counted tame; never overwritten |

Tames format, version 1 (documented contract; never change it without changing the version line): lines separated by
`\n`; line 1 = `1`; every other line = count (plain digits, invariant culture), one TAB, creature name (rest of the
line), lines sorted by name (ordinal). Example: `1\n3\t$enemy_boar\n2\t$enemy_wolf`. Reading skips unparsable lines
and counts <= 0, tolerates a trailing `\r`, and adds up duplicate names. If line 1 is not `1` (a newer version wrote
it), the mod shows what it can read but **never writes** (warning logged once), so newer data is never damaged. A
creature name containing a line break is refused (warning). The new value is built completely, then assigned once.

`CountingSince` = local date, `yyyy-MM-dd`, invariant culture (Gregorian calendar, Western digits).

Removing the mod: vanilla keeps both keys and ignores them; reinstalling shows the tames again with the old start
date. Tames that happen while the mod is off or not installed are not counted (the page says "counted while this mod
is on"). Kills made while the mod is off are counted by the game and show. Save timing is vanilla's: a crash loses the
last minutes of tames exactly like it loses vanilla stats.

### 3.6 Public read API (for the future MC Compendium)

`public static class CreatureCounts`, namespace `MC.Exploration.StatsPerCreatureMod`, the only public type. Creature
key everywhere = `Character.m_name`. All members: main thread only, never throw (catch, `PatchGuard.Report`, return
0/empty), work whether the feature is active or not.

```csharp
public static int ApiVersion => 1;                 // property, not const: a const is copied into the caller at its build
public const string TamesDataKey = ModInfo.Guid + ".Tames";
public const string CountingSinceDataKey = ModInfo.Guid + ".CountingSince";
public static bool IsAvailable { get; }            // game + profile AND local character exist
public static int GetKills(string creatureName);   // all-time total
public static int GetKills(string creatureName, KillModifiers modifier); // one bucket; CountNone = 0
public static KillBreakdown GetKillBreakdown(string creatureName); // Total, Melee, Ranged, Magic, Unarmed, Other
public static int GetTames(string creatureName);
public static DateTime? CountingSince { get; }
public static List<string> GetCreatureNames();     // new list, unsorted, kills >= 1 or tames >= 1, non-'$' names too
```

- Kill members need only the game and its profile (0 or empty on the main menu). Tame members (`GetTames`,
  `CountingSince`, the tame half of `GetCreatureNames`) also need the local character, so they read 0 / null / empty
  on the main menu, while the world loads and in the respawn wait (1.5). `IsAvailable` = both ready; a caller that
  caches reads again when it turns true.
- `KillBreakdown` is a `readonly struct` nested in `CreatureCounts` with an internal constructor (other mods can only
  read it). `Other` holds kills with several weapon families, no weapon family, after the full-heal reset, and kills
  from before the Deep North update: a Compendium must not show it as "mixed".
- Stability promise (README): names, meanings and the storage format never change; new members may be added (then
  `ApiVersion` goes up). A caller puts calls to members newer than version 1 in a separate
  `[MethodImpl(MethodImplOptions.NoInlining)]` method, called only after checking `ApiVersion` (the runtime resolves a
  missing member when it compiles the method containing the call).
- Two ways to use the data: the API (compile-time reference to this DLL + `BepInDependency`, call it only from
  patches/`OnActivated` per the framework rule; open question 3), or the data contract without a reference (vanilla
  kills, then parse the two `m_customData` keys; a missing key = nothing recorded).

### 3.7 Multiplayer and hand-off

Client-side, compatible. The mod sends nothing and writes nothing to ZDOs, items or other players. Kills are the
vanilla numbers (credited by the victim owner's game through the vanilla RPC: works with vanilla servers and unmodded
friends, test M01). Tames: the vanilla message reaches the closest player from any owner, modded or not (hand-off
test M02); with nobody within 30 m the owner counts it only when it is the closest player its game has loaded (M04),
else nobody does (M06). A closer player outside the owner's loaded zones is not seen (known limit, section 4). A
creature tamed with the mod is an ordinary vanilla tame for everyone (nothing stored on it, test M05).

### 3.8 Performance

- `AddStats` postfix: once per Compendium open; about 100 rows, one `Localize` each, one sort, a reused
  `StringBuilder`. Negligible next to the vanilla dump.
- `Player.Message` / `RPC_Message` postfixes run for every message to a player: int compare, then an ordinal
  `EndsWith` (no allocation), then the Unity `==`. Substring only on a match.
- `Tameable.Tame`: two `GetClosestPlayer` loops over the loaded players, only when a tame really happened.
- Stored tames are parsed once and cached on the raw string reference, so one read per row costs one lookup.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Character played since Call to Arms, mod installed now | Per-creature kills saved, never shown | Shown immediately (retroactive) |
| Kills made between Call to Arms and Deep North | Total saved, weapon buckets not saved | In the total and in "other"; a creature with only such kills shows no parentheses |
| Character last played before Call to Arms | Only the `EnemyKills` total | List starts with kills made since that update |
| Kills in several worlds | One profile dictionary per character | All included ("in every world") |
| You and a friend hit a Greyling, the friend kills it | Both get +1 | Both see it (each on their own character) |
| You hit it, then log out before it dies | No credit | Nothing |
| Kill with a club / bow or crossbow / staff / fists | Melee / Ranged / Magic / Unarmed bucket (fists: if the item is named `"Unarmed"`, unverified) | "melee" / "ranged" / "magic" / "unarmed" |
| Kill with Fenris claws (Unarmed skill, not fists: prefab data, unverified; T04) | Melee bucket | "melee" |
| Club hit, then bow kill | Total only | "other" |
| Creature regenerates to full health, then one weapon kills it | Total only (`Modifiers` reset to mixed) | "other" |
| Your tame, another creature or a console kill command kills it | No player credit (unless you had hit it) | Nothing |
| Butcher knife on your own tame (prefab data, unverified; T14) | Kill credited under that creature's name | Counted as a kill, e.g. "Boar" |
| You once hit a tame (even for 0 damage); it dies later to anything while you are connected | Kill credited to you (attacker flag, 1.2) | Counted as your kill |
| Boss | Same dictionary | Listed like any creature |
| Variants / star levels sharing one `m_name` | One key | One row |
| PvP kill | Not recorded per victim in 1.0.16 | Nothing; player names from older saves go under "Other names" |
| Modded creature whose `m_name` has no `$` | Key stored as is | Under "Other names" (cannot be told apart from a player name) |
| Creature from a removed mod | Key stays in the profile | Row shown with vanilla's `[token]` for a missing translation |
| Stats reset through an achievements reset | Values 0, keys kept | Rows with 0 hidden |
| Cheats used | Slot 0 still counts | Counted |
| Compendium open while a kill or tame happens | Page not refreshed | Same: reopen |
| Tame completes, you are the closest player within 30 m | Message to you; `CreatureTamed` +1 for the owner | You +1 |
| Tame completes, a friend is closer (within 30 m) | Message to the friend | Friend +1 (if modded); not you |
| Nobody within 30 m, you (the owner) are the closest loaded player (single player: always) | No message; `CreatureTamed` +1 for you | You +1 |
| Nobody within 30 m, a friend at 35 m, you (the owner) at 120 m | No message; `CreatureTamed` +1 for you | **Nobody** (the friend's game never ran `Tame`; crediting you could be wrong) |
| Nobody within 30 m, a friend closer but outside the owner's loaded zones | No message; `CreatureTamed` +1 for the owner | Owner +1 (known limit: the owner's game cannot see that friend) |
| Owner without the mod, you closest within 30 m | Message routed to you | You +1 |
| Owner without the mod, nobody within 30 m | No message | Nobody (vanilla total still goes to the owner) |
| Another mod's `Tame` prefix skips the original | Not tamed | Not counted (postfix checks `IsTamed()`) |
| `tame` console near you | Tames every loaded tameable you own (wild ones nearby too) | +1 per creature tamed, per the rules above |
| `tame` on already tamed creatures | `CreatureTamed` +1 again (quirk) | No change |
| `tame` while standing 40 m away | Tamed, no message | Owner fallback: you +1 (single player) |
| HUD hidden (Ctrl+F3) when the message comes | Message not shown | Still counted (T20) |
| Offspring, hatchling, grown calf, summon | Tamed without `Tame()` | Not counted |
| Young animal tamed by feeding (if possible) | Message uses its own name | Own row |
| Death and respawn, logout, other machine, cloud save | `m_customData` saved and carried in the `.fch` | Tames and start date kept |
| Mod off, tame happens, mod back on | — | That tame is not counted; start date unchanged |
| Mod off, kill happens, mod back on | Kill saved by vanilla | Shown |
| Stored tames written by a newer mod version | — | Shown as far as readable; new tames not counted (warning) |
| Another mod removes the Player Statistics entry | — | No section (the API still works) |

---

## 5. Decisions and open questions

### Decisions

1. **Kills are the game's own all-time numbers, always retroactive** (*to confirm with the user*, who expected
   counting to start at install). Vanilla already records per-creature kills since Call to Arms; our own counter would
   give lower, conflicting numbers and a second save. Consequence: kills from before Call to Arms are missing, and
   kills from every world of the character are included. A "kills since install" setting with a stored baseline was
   considered, then dropped: the page and the API only give all-time kills. The start date on the page
   applies to tames only.
2. **Kill credit follows vanilla** (*to confirm*): every player who hit the creature gets a full kill, not only the
   last hitter, including a zero-damage hit on a tame that dies later. Changing it would need our own tracking and
   would disagree with the game's achievements.
3. **Tame credit** (*to confirm*): the player the game tells "has been tamed" (closest within 30 m); when nobody was
   that close, the owner, only if the owner is the closest player loaded on the owner's game; otherwise nobody. The
   game does not know who fed the creature, and ownership follows the active area, not the pen (1.4), so a far-away
   owner getting the credit while another player stands closer would often be wrong. Consequences: per-creature tames
   can differ from the vanilla "Creature Tamed" line (multiplayer, `tame` cheat), a tame can go uncounted in
   multiplayer, and a closer player outside the owner's loaded zones is not seen. Single player is never affected.
4. **Tames count from the first run**, per character, only while the mod is on; the page states the start date.
   Offspring, hatchlings, grown calves and summons are not "tamed by you".
5. **Placement: top of the existing Player Statistics text**, vanilla text untouched below. English labels like the
   rest of that page; creature names translated.
6. **Keys split, nothing hidden**: `$` keys under "Creatures", other keys under a grey "Other names" line, so a player
   name never reads as a creature. The API returns all keys alike.
7. **Slot 0 (lifetime) only**; no per-difficulty numbers.
8. **Storage in `Player.m_customData`** (versioned value), not a separate file: travels with the character, including
   cloud saves, and vanilla keeps it if the mod is removed.
9. **"other", not "mixed"**, for the remainder of the weapon split, because it also holds kills whose weapon type the
   game never saved; parentheses are left out when only "other" is known.

### Added beyond the request (small)

- Weapon-type breakdown (vanilla data) with an "other" remainder so the parts add up; setting `ShowWeaponTypes`.
- Setting `SortBy`.
- "Other names" sub-group for non-creature keys.
- The public `CreatureCounts` API and documented storage format (asked for indirectly: "the Compendium could use this
  data as is").

### Open questions

1. ModName "Creature Kill and Tame Counts" (GUID `MC.Exploration.Stats.PerCreature`): confirm before release.
2. Should the list also get its own Compendium entry near the top (easier to find than the last entry)? Not done:
   the user asked for the existing page.
3. For the future Compendium, a compile-time reference to another MC mod's DLL is not supported by the build yet
   (`ModRequires` only adds a soft `BepInDependency`, no reference). Either add a reference mechanism then, or use the
   data contract (3.6), which needs none.
4. Nameplate / trophy-tooltip counts (backlog extras) are left to the Compendium idea.

## 6. Tests

The in-game checklist lives next to the code: `src/Exploration/Stats.PerCreature/TESTING.md`
(run `./tools/Get-TestTodo.ps1 -Mod PerCreature`).
