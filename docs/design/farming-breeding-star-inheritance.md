# Breeding Star Inheritance — design

| | |
|---|---|
| Mod | Breeding Star Inheritance |
| GUID / project | `MC.Farming.Breeding.StarInheritance` (`src/Farming/Breeding.StarInheritance/`, root namespace `MC.Farming.BreedingStarInheritanceMod`, package `BreedingStarInheritance`) |
| Category / scope | Farming / Revamp |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. A birth is decided by the game that simulates the parent (1.11), so the server refuses players whose game does not have the mod installed (setting `AllowPlayersWithoutMod` to let them in) and sends its breeding settings to everyone (3.9, decisions 10, 22-26). The user's review (2026-09-29): the vanilla 50/50 rule must never apply; server-only was asked about and rejected (decision 22). |
| Sheet idea | `Breeding revamp` |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`), decompiled `assembly_valheim` in `.ref/`; the dedicated server's own `assembly_valheim` (installed `valheim_server`, `Game` and `ZNet` decompiled to a scratch folder, 2026-09-29) for 1.11; prefab names checked in the installed game's `StreamingAssets/SoftRef/manifest_extended` and English names in the `resources.assets` localization table (2026-09-29); per-species breeding facts from valheim.weirdgloop.org (2026-09-29); sources of Star Level System 1.18.1, AllTameable and FeedLikeGrandma read on GitHub (2026-09-29); HarmonyX 2.9.0 in the installed `BepInEx/core` |
| Status | Implemented (0.1.0). The client-only build passed the build, the smoke test and the in-world self-tests (2026-09-29); the server-and-client build (join check, settings sync) builds in Debug and Release, smoke test, in-world self-tests and in-game tests pending |

"Breeding" and not "Procreation" in the GUID: it is the player's word. "StarInheritance" names what changes (how stars pass
from parents to babies), so a later breeding mod (for example "Breeding.OneBabyPerPair") gets its own GUID.

## Goal

Requirements from the user (2026-09-29), made precise:

1. When a tamed animal gives birth after breeding (a live baby, or an egg for hens and Asksvin), the baby's level
   **starts from the lower level of the two parents** (level 1, 2 or 3 = 0, 1 or 2 stars). The second parent is the
   partner the game counted when the pregnancy started: the nearest ready partner within the species' partner range
   (3.3, decision 4).
2. The baby then has a **chance to get one more level**. The extra level never takes a baby above **2 stars (level
   3)**. A baby whose starting level is already 2 stars or more gets no extra level, and no level is ever lowered to
   fit the cap (levels above 2 stars from other mods are kept).
3. The vanilla rule (the baby takes the pregnant parent's own level, which in practice is a 50/50 between the parents)
   **never applies** to a birth decided by a game running the mod whenever a partner is known: the partner recorded at
   conception, or, for a pregnancy that started before the mod was installed, while it was off or on a game without
   it, the nearest tamed partner within the species' population range at birth (`m_totalCheckRange`, code default
   10 m; 3.3). A parent with no tamed partner of its species that close at birth starts from its own level (it still
   gets the extra-star chance, decision 6); T17 shows this limit.
4. The **chance of the extra level follows the Farming skill**: **15% at Farming 0**, rising in a straight line to
   **50% at Farming 100**. The skill that counts is the best of: the player of the game that decides the birth
   (always, wherever that player is), and other players running the mod within **60 m** of the parent. The user's
   "**base 10%**" is used only when no farmer's skill is known at all (the deciding game has no player at that moment,
   for example while it respawns, and no other farmer is in range), which is rare in normal play. The three chances
   and the range are settings; decision 1 lists the other ways to read "base 10%" for the user to pick.
5. Eggs carry the result as their item quality; the hatchling and the grown animal keep it (vanilla already copies it
   at hatching and at growing up).
6. The mod can be turned off on its own, live, like every MC mod (framework toggle): off = vanilla births at once.
7. **Multiplayer: the vanilla rule never applies because a game lacks the mod** (user's review, 2026-09-29: "could the
   mod be server side only? or needed to be installed on both client and server"; the requirement behind it is that
   the vanilla 50/50 rule must never apply). The mod is required on the server (or host) and on every player: the
   server refuses a player whose game does not answer the mod's handshake about a second after they got in (their game
   shows the vanilla "Incompatible version" message), unless the server's `AllowPlayersWithoutMod` is on; and the
   server's five breeding settings apply to every game connected to it (3.9). Server-only is not feasible (decision
   22).

Items 1-2 are the user's behaviour 1, item 3 behaviour 2, item 4 behaviour 3. Item 5 follows from item 1 for egg
layers. Item 6 is an MC rule for every mod. Item 7 is the user's review answer.

Worked outcomes (p = the chance of item 4):

| Parents | Vanilla (whichever parent gives birth) | With the mod |
|---|---|---|
| 0★ + 2★ | 0★ or 2★, roughly half each | 0★, or 1★ with chance p; never 2★ |
| 1★ + 2★ | 1★ or 2★ | 1★, or 2★ with chance p |
| 0★ + 0★ | 0★ | 0★, or 1★ with chance p |
| 2★ + 2★ | 2★ | 2★ (already at the cap, no roll) |
| 4★ + 4★ (another mod's levels) | 4★ | 4★ (never lowered, no roll) |

Non-goals: one baby per pair (both parents can still become pregnant, as in vanilla; decision 7), breeding speed,
population limits, per-species settings, genetics/traits/mutations below the parents, a rule choice (min / average /
max / vanilla), a hover line showing pregnancy or the parents' levels, a message or "+1" text when the extra star
happens (open question 2), Farming XP for births, star levels above 2 by default, wild spawns, taming, births in a game that also runs Star Level System (left to it, decision 15), showing the egg's
quality in the inventory (open question 6).

---

## 1. Vanilla behaviour (code trace)

### 1.1 Where breeding runs

- `Procreation` (on `Boar`, `Wolf`, `Lox`, `Hen`, `Asksvin`, `Moose`, …; which prefabs carry it is prefab data) caches
  `m_nview`, `m_baseAI`, `m_character`, `m_tameable` in `Procreation.Awake` and starts
  `InvokeRepeating("Procreate", Random.Range(interval, 1.5 × interval), m_updateInterval)` (code default 10 s; 30 s
  for `Boar` and `Hen`, read in game by the self-tests, so their first tick comes 30-45 s after they load). Awake
  runs **on every game where the creature is loaded**, so `Procreate` ticks on every client; the body returns at once
  unless `m_nview.IsValid() && m_nview.IsOwner() && m_tameable.IsTamed()`. Only the **ZDO owner** breeds, and only
  tamed animals (wild ones tick too and return).
- `Procreation.Procreate` is private, has no overload, and is only started by `InvokeRepeating`. On the first owner
  tick it resolves `m_offspringPrefab` (`ZNetScene.GetPrefab(Utils.GetPrefabName(m_offspring))`) and `m_myPrefab`
  (`ZNetScene.GetPrefab(zdo.GetPrefab())`), both private.
- The pregnancy is one ZDO long, `ZDOVars.s_pregnant` = server ticks at conception (0 = not pregnant), written by the
  private one-line `MakePregnant` and cleared by `ResetPregnancy`. Love points are `ZDOVars.s_lovePoints`.
  `IsPregnant()` / `IsDue()` (private) read them; `IsDue` compares `ZNet.GetTime()` with the stamp plus
  `m_pregnancyDuration` (code default 10 s) **at call time**, so the due date catches up while unloaded (love points do
  not), and a mod that changes `m_pregnancyDuration` for one call changes the answer.

### 1.2 Conception (`Procreate`, not pregnant)

In order, every tick:

1. Return when `Random.value <= m_pregnancyChance` (code default 0.5: it is really the chance to **skip** the tick),
   when `m_baseAI.IsAlerted()`, or when `m_tameable.IsHungry()` (last feeding older than `Tameable.m_fedDuration`,
   server time).
2. Population check: own prefab + offspring prefab within `m_totalCheckRange` (10) ≥ `m_maxCreatures` (4) → return.
3. Partner count: `SpawnSystem.GetNrOfInstances(m_seperatePartner ? m_seperatePartner : m_myPrefab, position,
   m_partnerCheckRange (3), eventCreaturesOnly: false, procreationOnly: true)` (1.3). The parent **counts itself**,
   so a normal species needs 2 or more; a `m_seperatePartner` species needs 1 or more.
4. If there are enough partners, or `m_noPartnerOffspring` is set: love effects (only when the count > 0), love
   points +1; at `m_requiredLovePoints` (4) love points go back to 0 and `MakePregnant()` stamps `s_pregnant`.

Nothing identifies or stores the partner.

### 1.3 Partner counting (`SpawnSystem.GetNrOfInstances`)

For a prefab with a `BaseAI`: loop over `BaseAI.BaseAIInstances` (every loaded creature on this game), keep those whose
`gameObject.name == prefab.name + "(Clone)"` and within `maxRange` (skipped only when `maxRange > 0` and
`Vector3.Distance > maxRange`: a range of 0 or less means **no limit**, and exactly at the range counts), and with
`procreationOnly` skip a creature **that has a `Procreation`** whose `ReadyForProcreation()` is false
(`ReadyForProcreation` = tamed, not pregnant, not hungry). A creature of that prefab **without** a `Procreation` counts
whatever its state. A prefab **without** `BaseAI` is counted through `GameObject.FindGameObjectsWithTag("spawned")`
and a name prefix match instead (no `procreationOnly` filter). Babies are other prefabs (`Boar_piggy`, `Wolf_cub`,
`Lox_Calf`, `Chicken`, `Asksvin_hatchling`, `Moose_calf`: all present in the 1.0.16 manifest), so they are never
counted as partners. Other mods can add to this count with a postfix (AllTameable does, section 2).

### 1.4 Birth and the offspring level (`Procreate`, pregnant and due)

1. `ResetPregnancy()` (`s_pregnant = 0`).
2. If `m_noPartnerOffspring` is set, count partners again exactly as in 1.2 (the parent is no longer pregnant, so it
   counts itself again); too few → spawn `m_noPartnerOffspring` instead of `m_offspring`.
3. Spawn position behind the parent (`m_spawnOffset`..`m_spawnOffsetMax`, or a random direction), `Object.Instantiate`.
4. The level, **the only line this mod changes in effect**: the new object's `Character` gets `SetTamed(parent tamed)`
   and `SetLevel(Mathf.Max(m_minOffspringLevel, m_character ? m_character.GetLevel() : m_minOffspringLevel))`;
   without a `Character` its `ItemDrop` gets `SetQuality(<same expression>)` (the egg).
5. `m_birthEffects.Create`.

So the offspring level is `max(m_minOffspringLevel, level of the parent that gives birth)`. `Character.GetLevel()`
returns the private field `m_level` and nothing else reads that field during the call (its only other reader is the
ragdoll setup on death). The partner does not need to be near at birth.

### 1.5 Why it looks like a coin flip, and both parents

Both animals of a pair run 1.2 on their own and keep their own love points. Whichever reaches 4 love points first
becomes pregnant (each tick is skipped half the time, so it is random) and passes **its own** level. While it is
pregnant it is not "ready", so the other one cannot gain love points unless a third ready animal is within range; once
the first has given birth, the second (which kept its love points) usually becomes pregnant soon after and passes
**its** level. Over time a mixed pair gives about half babies at each parent's level. Both parents do get pregnant, one
after the other.

### 1.6 Levels, stars and test commands

- `Character.SetLevel(int)`: ignores levels below 1; sets `m_level`, writes `ZDOVars.s_level` (no owner check, fine on
  a fresh object), `SetupMaxHealth()` (max health = base × level), `m_onLevelSet`. `Character.Awake` reads `s_level`
  (default 1) for non-players.
- `EnemyHud` shows the `level_2` star for level 2 and `level_3` for level 3 only; other levels show no star (a level-5
  creature from another mod looks like a 0★ one).
- Console (`Terminal`): `spawn <prefab> <amount> <level>` spawns at that level (creatures up to 9, items up to quality
  4, so `spawn ChickenEgg 1 2` gives an "Egg[2]"); `tame` runs `Tameable.TameAllInArea(player position, 20f)`, which
  **ignores its point and radius**: it calls `Tame()` on every loaded non-player `Character` with a `Tameable`, and
  `Tame()` only acts on untamed ones whose ZDO this game owns (and that have a `MonsterAI`), so `tame` tames every
  untamed tameable creature this game simulates, at any distance (docs/game/farming-cooking.md); `raiseskill <skill>
  <amount>` / `resetskill <skill>`; `skiptime <seconds>` advances server time (due dates and growing catch up; animals
  also become hungry); `die` kills your own character (no flag, works anywhere).
- Who can run them: all five are cheat commands (`isCheat`), and `ConsoleCommand.IsValid` accepts a cheat command only
  when `Terminal.IsCheatsEnabled()`, which is `devcommands` on **and** `ZNet.instance.IsServer()`. So they work **only
  in single player or on the host**, never on a client, admin or not; `skiptime` alone is a `remoteCommand` that a
  client forwards to the server (which runs it only for an admin). The other flags do not change this: `spawn` is
  registered with a `ConsoleEventFailable` delegate, whose constructor turns `onlyAdmin` into `OnlyServer`; `tame` is
  registered with a `void` delegate (`ConsoleEvent` overload), whose constructor keeps `OnlyServer = onlyServer`
  (false) and only stores `OnlyAdmin`, which nothing reads; `raiseskill`, `resetskill` and `skiptime` are
  `onlyServer` (docs/game/core-engine.md, console section). The multiplayer tests are built around this (section 6).

### 1.7 Eggs (`ItemDrop`, `EggGrow`)

- `ItemDrop.SetQuality(int)` sets `m_itemData.m_quality` and the scale; `ItemDrop.Start` → `Save()` writes the item to
  its ZDO afterwards, so the quality set right after `Instantiate` is kept.
- Ground hover (`ItemDrop.GetHoverText`): quality > 1 adds `[<quality>]` after the name, e.g. "Egg[2]" (English
  `$item_chicken_egg` = "Egg", `$item_asksvin_egg` = "Asksvin Egg"), whatever the item's `m_maxQuality`. The wiki
  confirms for Asksvin: egg quality 1 / 2 / 3 = a 0★ / 1★ / 2★ animal.
- Inventory: the quality number in the slot (`InventoryGrid`: `m_quality.enabled = m_maxQuality > 1`) and the tooltip's
  quality line (`ItemDrop.ItemData.GetTooltip`, `m_maxQuality > 1`) show **only when the item's `m_maxQuality` is above 1**. The
  eggs' `m_maxQuality` is prefab data (unverified, T16 settles it): if it is 1, an Egg and an Egg[2] look the same in
  inventories and chests.
- Stacking: picking up (`Inventory.AddItem` → `FindFreeStackItem`, which compares `m_quality`) and ground auto-stack
  (`ItemDrop.AutoStackItems`, same) keep different qualities apart whatever `m_maxQuality` is. A **drag-merge** in the
  inventory (`InventoryGrid.DropItem` → `ItemData.IsSameType`) ignores quality when `m_maxQuality <= 1`, so dragging an
  Egg[2] onto an Egg stack can merge them and lose the level. Vanilla behaviour, not changed.
- `EggGrow.GrowUpdate` (owner, every `m_updateInterval`): incubates only with stack 1, inside a Heat `EffectArea` and
  under a roof with enough cover (`CanGrow`); on hatching it spawns `m_grownPrefab` with `SetTamed(m_tamed)` and
  `SetLevel(m_item.m_itemData.m_quality)`.

### 1.8 Growing up (`Growup`)

`Growup.GrowUpdate` (owner, every 10 s): after `m_growTime` of server time since spawn, spawns `m_grownPrefab` (or a
weighted `m_altGrownPrefabs` entry), `SetTamed` (when `m_inheritTame`) and `SetLevel(baby.GetLevel())`, then destroys
the baby. The level survives both steps (egg → hatchling → adult).

### 1.9 The Farming skill is local

- `Skills.SkillType.Farming = 106` exists in 1.0 (English "Farming"). Vanilla raises skills through
  `Pickable.Interact` (`m_pickRaiseSkill`) and piece placement (`Player` raises `m_buildPieces.m_skill`; which pickables
  and which tool's piece table use Farming is prefab data, the cultivator and crops per the wiki), and reads Farming for the
  cultivator/scythe radius (`Piece`, `Attack`: `Lerp(..., GetSkillFactor(Farming))`) and the bonus harvest
  (`Pickable.Interact`: `Random.value < skillFactor * m_maxLevelBonusChance`, a "+N" `DamageText`).
- `Skills.GetSkillLevel` = the stored level modified by status effects (`SEMan.ModifySkillLevel`), floored;
  `GetSkillFactor` = that / 100 clamped to 0..1. Both go through the private `Skills.GetSkill`, which **adds** a
  level-0 entry to `m_skillData` when the skill is missing: that entry is a row in the Skills tab
  (`SkillsDialog.Setup` lists every entry) and is saved in the character (`Skills.Save`). Vanilla reads Farming only
  around farming (using a farming tool or showing its tooltip, harvest pieces, pickables that raise it), so a
  character that never farmed has no Farming row; reading it with `GetSkillLevel` from a breeding tick would add one
  (3.4).
- Skills live in the player profile and are loaded only into the **local** player (`Game` → `PlayerProfile.
  LoadPlayerData` → `Player.Load` → `Skills.Load`). Other players' `Player` objects on this game have an empty
  `Skills`: their Farming reads **0**. Nothing in vanilla syncs skills.
- Death: `Player.OnDeath` lowers skills (`Skills.OnDeath`) only on a "hard" death (`m_timeSinceDeath >
  m_hardDeathCooldown`, code default 10 s; the prefab value is the "No skill drain" time, unverified); a second death
  within that time keeps the skills (the time since death is saved with the player data, so it survives the
  respawn). Respawning or reconnecting creates a **new** `Player` object with a **new** ZDO
  (`Game._RequestRespawn` destroys the old one, `Game.SpawnPlayer` instantiates a fresh prefab).

### 1.10 Vanilla precedents for "players near an animal"

- `Tameable.DecreaseRemainingTime` (taming progress): `Player.GetPlayersInRange(position,
  m_tamingSpeedMultiplierRange (code default 60), list)` and, for each, `GetSEMan().HaveStatusAttribute(TamingBoost)`.
  For a remote player `SEMan.HaveStatusAttribute` reads `ZDOVars.s_seAttrib` from **that player's ZDO**, which the
  player's own game writes (`SEMan` update). This is the pattern this mod copies for other players' Farming (3.4).
- `Tameable.Tame`: the "has been tamed" message goes to `Player.GetClosestPlayer(position, 30f)`.
- `Player.GetPlayersInRange` / `GetClosestPlayer` / `GetAllPlayers` use `Player.s_players`: every **loaded** `Player`,
  local and remote, added in `Player.Awake`. A remote player is loaded only while it is inside this game's active area
  (below), so a player 60 m from a pen can be missing on a game whose own player stands on the far side.

### 1.11 Who simulates the animals

`ZDOMan.ReleaseNearbyZDOS` (run by the server for itself and for every peer, every 2 s): a persistent ZDO whose owner's
active area no longer contains it, or that has no owner, is taken by the peer whose active area contains it; the owner
keeps it while it stays in its area. So the game that breeds a pen is usually the first player who came near it, not
necessarily the one standing next to it. The active area depends on the synced simulation distance
(`ZNetScene.PointInsideActiveArea`: Chebyshev distance to the centre of the player's **zone** ≤ 1 or 1.5 zones of
64 m, so up to about 96-128 m from the player depending on where it stands in its zone).

A **dedicated server never simulates animals**. The client build in `.ref` cannot show this (its `ZNet.IsDedicated()`
returns `false`), so it was checked in the dedicated server's own `assembly_valheim`: there `ZNet.IsDedicated()`
returns `true` and `Game.FixedUpdate` sets the server's reference position to (1000000, 0, 1000000) every physics
step, far outside the world, instead of running the respawn code. `ReleaseNearbyZDOS` for the server itself and
`ZNetScene.CreateDestroyObjects` both use that position, so the server owns and instantiates no world objects;
breeding happens only on players' games (a host is a normal game). Mods that make a dedicated server simulate the
world change this (3.11).

### 1.12 ZDO notes

`ZDO.Set(int, int|long|float)` bumps the data revision only when the value changes (`ZDOExtraData.Set` returns
whether it changed); `IncreaseDataRevision` dereferences `ZNet.instance` and `ZDOMan.instance`, so it throws once they
are gone (application quit). `RemoveInt` / `RemoveLong` do not bump the revision, and a removal **never reaches other
games**, even when another change on the same ZDO is sent in the same frame: the sender sends its whole current data,
but the receiver (`ZDOMan.RPC_ZDOData` → `ZDO.Deserialize` → `ZDOExtraData.Add`) only adds or overwrites the keys in
the package and keeps the ones missing from it. So a removed key stays on the server's copy (and in the world it
saves) and on every other game that had it; to clear a value for everyone, overwrite it. Unknown keys are ignored by
vanilla and saved with the world. A player's ZDO is not saved and is replaced at every respawn or reconnection (1.9).

### 1.13 Other `Procreation` users

- `Pet` (a placeable pet piece with `Tameable` + `Procreation`, no `Character`): `m_character` is null, so vanilla
  uses `m_minOffspringLevel`. This mod leaves such objects to vanilla (3.5).
- Nothing else in `assembly_valheim` references `Procreation` except `SpawnSystem.GetNrOfInstances` and `Pet`.

### 1.14 Joining, refusing a player, per-player messages (for the join check and the settings sync)

- **Order on one connection** (`ZNet.OnNewConnection`, `RPC_ServerHandshake`, `RPC_ClientHandshake`,
  `SendPeerInfo`, `RPC_PeerInfo`; core-engine.md §3): the client registers its peer RPCs and sends `ServerHandshake`;
  the framework's `NetworkGate` postfix then sends the mod's `Hello`. The client sends `PeerInfo` only after the
  server's `ClientHandshake` answer (and the password, if any). `ZRpc` keeps order on a connection, so the server
  always has a modded client's `Hello` before its `PeerInfo`: `NetworkGate.PeerHasMod(peer)` is already final when
  the server's `RPC_PeerInfo` completes.
- **"Fully in"**: at the end of a successful server `RPC_PeerInfo`, the peer gets `m_uid` (`ZNetPeer.IsReady()`),
  `m_playerName`, the server's own `PeerInfo`, and joins `ZDOMan` and `ZRoutedRpc`. Every vanilla refusal (network
  version, ban or allow list, session ticket, crossplay, full, password, already connected) invokes `Error(n)` and
  returns before that, so the peer is never ready.
- **How a vanilla client reacts to `Error(n)`** (client `RPC_Error`): it only sets the static connection status to
  `(ConnectionStatus)n`. `Game.FixedUpdate` then sees a status that is neither Connecting nor Connected and calls
  `Logout()`; back in the main menu, `FejdStartup.Start` → `ShowConnectError()` shows the panel with the status text.
  `n = 3` (`ErrorVersion`) shows `$error_incompatibleversion`, English "Incompatible version"; `n = 12`
  (`ErrorKicked`, what the "Kicked" RPC sets) shows `$error_kicked`, "You have been kicked from the server." (English
  texts read in the installed 1.0.16 `resources.assets` localization table, 2026-09-29; the display path is read in
  code, not seen in game yet).
- **Vanilla kick** (`ZNet.InternalKick(ZNetPeer)`): invokes `Kicked` on the peer and puts it in the private static
  `ZNet.PeersToDisconnectAfterKick` with `Time.time + 1`; `ZNet.Update` calls `Disconnect(peer)` once that time has
  passed, which gives the message time to leave. A client that disconnects first is disconnected again by that loop
  (harmless: vanilla does it on every kick). `CheckWhiteList` skips peers in that list.
- **First breeding tick of an object**: `Procreation.Awake` starts `InvokeRepeating(Procreate)` after a random
  `m_updateInterval` to 1.5 × `m_updateInterval` (code default 10 s; 30 s for `Boar` and `Hen`, read in game). A
  game that just joined receives the world's objects only after `RPC_PeerInfo` (`ZDOMan.AddPeer`), so its first
  `Procreate` call on any animal comes at least that species' `m_updateInterval` after it got in (the
  `breeding.network` self-test lists the value of every species and checks it against the join check's 5 s).
- **Unknown RPC names are ignored** (`ZRpc.HandlePackage` looks the hash up and does nothing when it is missing), so
  vanilla games ignore the mod's messages. `ZRpc.Register` replaces a handler with the same name; there is no
  unregister. An `EndOfStreamException` while reading a package counts as an incompatible version and drops the
  connection, so the settings travel as one `string` (always readable) that the mod parses itself.

---

## 2. Existing mods and what they teach

From the backlog research (`docs/research/existing-mods-farming-cooking-building-crafting.md` §1) plus a read of the
published sources on GitHub (2026-09-29):

| Mod | How it hooks | Lesson / compatibility |
|---|---|---|
| [BreedingUpgrades](https://thunderstore.io/c/valheim/p/Dumba/BreedingUpgrades/) (Dumba, 1.0.2, pre-1.0), [source](https://github.com/DaiMinhTri/BreedingUpgrades) | `ProcreationPatch`: a `Procreate` prefix/postfix that sets a static "in procreation" context (and changes `m_maxCreatures`/ranges for its breeding limit), and a **transpiler** that inserts `LevelCalculator.ApplyMutation` right before the `Character.SetLevel` call (flat 5% chance of +1). `ItemDropSetQualityPatch`: an `ItemDrop.SetQuality` prefix that adds the same roll to eggs while the context is set. An `Object.Instantiate` postfix copies a size trait. | Confirms the whole birth goes through the one `Max(min, parent level)` expression. With our design its roll comes **on top of** ours (two chances of +1): README says to use one. |
| [Star Level System](https://github.com/MidnightsFX/Valheim_Star_Levels_Expanded) (MidnightsFX, GUID `MidnightsFX.StarLevelSystem`; source read at 1.18.1) | `LevelPatches.SetChildLevel`: a `Procreate` **transpiler** that replaces the vanilla level code. Live births: `SetupChildCharacter` takes the parent level from its own cache (`CompositeLazyCache`) whenever the entry's level is not 0, and SLS builds an entry with level ≥ 1 for every creature it sets up; `proc.m_character.m_level` is only the fallback. Eggs: `SetupEggItem` with `LootEggsDropIncreaseStacks` (default **on**) sets quality 1 and makes the egg **stack** `1 + PerLevelLootScale × parent m_level`; only with it off does it use vanilla's expression. Hatchlings take SLS's level configuration unless `EggLevelDeterminedByItemQuality` (default **off**). Its own +1 (`OffspringCanBeStrongerThanParents`) is **off** by default. Also transpiles `EggGrow.GrowUpdate` and `Growup.GrowUpdate`. | **Not compatible with its defaults**: live births ignore our level (cache), and our level swap would silently change its egg count (a 2★ + 0★ hen pair would lay 2 eggs instead of 4 with `PerLevelLootScale` 1). So this mod detects SLS and leaves births to it (decision 15, README). A `Character.SetLevel` prefix would miss SLS too (it writes the ZDO). |
| [Seasons](https://github.com/shudnal/Seasons) (shudnal) | `SeasonStatePatches.Procreation_Procreate_ProcreationMultiplier`: a `Procreate` prefix that **returns false** (no breeding) when the season multiplier is 0, else scales `m_pregnancyChance`, `m_partnerCheckRange`, `m_totalCheckRange`, `m_pregnancyDuration` for the call and restores them in a finalizer. | Same "change fields for one call, restore in a finalizer" pattern as ours. Our prefix runs last and does nothing when the original is skipped (`__runOriginal`); our partner scans read the (scaled) ranges during the call, exactly as vanilla's count does; a changed `m_pregnancyDuration` cannot desync us because we do not predict the birth (decision 16). Compatible. |
| [FeedLikeGrandma](https://github.com/sighsorry1029/FeedLikeGrandma) (sighsorry) | `ProcreationProcreatePatch`: a `Procreate` prefix returning `FeedProcreationSystem.ShouldRunProcreation`, which is **false** for an owned, tamed, **non-pregnant** animal over its population limit (only when its `HorizontalProcreationLimit` is on), true otherwise. | Never skips a birth. Our stand-aside log line is written only for a skipped call that would have been a due birth, so it stays silent with this mod (3.5). Compatible. |
| [TameGuildWars](https://github.com/Wacky-Mole/TameGuildWars) (Wacky-Mole) | `Procreate` transpiler right after `Instantiate` to copy a guild component to the child. | Unrelated to levels. Compatible. |
| [Procreation Plus](https://thunderstore.io/c/valheim/p/MaxFoxGaming/Procreation_Plus/) (MaxFoxGaming, 1.2.2, 1.0) | YAML `LevelUpChance` / `MaxStars` per prefab (boar 25%, wolf 10%, lox, hen/asksvin eggs, moose 5%); base level = vanilla. No source published. | Probably stacks a second +1 like BreedingUpgrades (unverified). Lists the vanilla breeders: boar, wolf, lox, hen, asksvin, moose. |
| [TameCraft](https://thunderstore.io/c/valheim/p/momos3939/TameCraft/) (momos3939, 1.0.1, AI) | Patches `Tameable`, `Procreation`, `Growup`; optional Mendelian two-parent inheritance (off by default), faster breeding; needs server and all clients. No source. | Another inheritance system: do not combine with its genetics option (unverified hooks). |
| [Creature Genetics](https://thunderstore.io/c/valheim/p/Meldurson/CreatureGenetics/) / AllTameable (Meldurson), [source](https://github.com/meldurson/AllTameableRelease) | `Genetics.cs`: postfixes on `Procreation.MakePregnant` and `ResetPregnancy`, a `void` `Procreate` prefix (never skips the original), and a `SpawnSystem.GetNrOfInstances` postfix that adds cross-species "compat mates" to the partner count. `ProcreationInfo` sets the child's level itself (the mother's or the father's level at random, plus optional mutations). | Replaces the level rule: **incompatible by design**, README says so. Our stand-aside never triggers for it (it does not skip). Its cross-species partners are invisible to our partner scan: such a conception writes no record (3.3) and the birth falls back to a same-species partner or the parent's own level. |
| CreatureLevelAndLootControl (Smoothbrain, 4.6.4) | Wild spawn levels and loot; deprecated on Thunderstore; no breeding feature listed. | Levels above 2★ reach tames through taming: our rule never lowers them and only caps the bonus. |
| [MyLittleUI](https://github.com/shudnal/MyLittleUI) (shudnal) | References `Procreation` for hover info (not inspected in detail). | Read-only display; expected compatible (unverified). |

Conclusion: every breeding mod either reads the parent's level inside `Procreate` or replaces the birth code. The
least invasive way to change "whose level" is to make the parent's level **be** the rule's result for the duration of
the birth call (3.1), so vanilla, and mods that read the parent's level for the baby, pass it on; a mod that replaces
the level code (Star Level System, AllTameable) is not compatible and is named in the README. Nobody implements "lower
parent + chance of +1 scaled by a skill"; nobody records the partner in a small mod.

---

## 3. Design

### 3.1 Core idea

1. **Record the partner at conception.** In a `Procreation.Procreate` postfix on the owner, when the call turned a
   non-pregnant animal pregnant, find the partner vanilla just counted (the nearest ready partner, 3.3) and write its
   level plus the pregnancy stamp on the pregnant parent's ZDO.
2. **Decide the level while pregnant.** In a `Procreate` prefix on the owner, whenever the parent is pregnant, compute
   the baby's level with the rule (3.2), using the recorded partner (or a fallback, 3.3) and the best farmer (3.4).
   This is done on every pregnant tick, not only when we predict a birth, so another mod that changes the due time for
   one call cannot make us miss a birth (decision 16).
3. **Let vanilla pass it on.** Put that level in the parent's `m_level` field for the rest of the call, so vanilla's
   own `Mathf.Max(m_minOffspringLevel, m_character.GetLevel())` hands it to `Character.SetLevel` (live baby) or
   `ItemDrop.SetQuality` (egg) if the birth happens in this call; restore the real level in a first-priority postfix
   (other mods' postfixes see the real level) and again in a finalizer (exception path). No patch on `SetLevel`,
   `SetQuality`, `EggGrow` or `Growup`: wild spawns and every other level change are untouched by construction.
4. **Publish your Farming skill.** Every game running the mod writes its own player's Farming level on its own player
   ZDO (when the character spawns, then throttled on breeding ticks, only when it differs from what is there), so the
   game that decides a birth can also use the skill of another player standing at the pen (3.4).
5. **Everyone runs it, with the server's numbers** (Goal 7). The server (dedicated or host) refuses players whose
   game does not have the mod installed, and sends its five breeding settings to every game with the mod; those games
   use them instead of their own (3.9).

### 3.2 The birth rule (`BirthRule.Decide`)

Inputs: `own` = the parent's `m_level`; `partner` = recorded level, level found at birth, or none (3.3);
`minOffspring` = `m_minOffspringLevel`; the best farmer (3.4); the settings.

```
base   = max(minOffspring, partner is none ? own : min(own, partner))
cap    = MaxStars + 1                               (default 3 = 2 stars)
chance = 0                                          if base >= cap
       = ChanceWithoutFarmer                        if no farmer is known (3.4)
       = ChanceAtFarming0 + (ChanceAtFarming100 - ChanceAtFarming0) * clamp01(farming / 100)   otherwise
bonus  = chance >= 100 or (chance > 0 and roll < chance / 100)        (roll = Random.value, drawn by the caller)
level  = base + (bonus ? 1 : 0)
```

- The rule is pure (no Unity call): `BaseLevel`, `Cap`, `Chance`, `Wins` and `Decide` take the roll as an argument,
  which the prefix draws with `UnityEngine.Random.value`; the `breeding.rule` self-test hammers them (6.1). Settings
  are clamped to 0-100 inside `Chance` as well (NaN counts as 0).
- `m_minOffspringLevel` is applied **before** the bonus (vanilla's floor still holds; a bonus can go one above it).
- `base` is never lowered to the cap: a 4★ pair from another mod gives a 4★ baby, with no roll.
- `chance` 0 never rolls and 100 always wins (`Random.value` can return exactly 0 or 1).
- Chances with the default settings:

| Situation at birth | Chance of +1 |
|---|---|
| The deciding game's own player (in single player: you, always), best Farming 0 / 25 / 50 / 75 / 100 | 15% / 23.75% / 32.5% / 41.25% / 50% |
| Another player running the mod within 60 m has a higher Farming | the same line, with their level |
| No farmer known (the deciding game has no player at that moment, for example while respawning, and no other farmer is within 60 m) | 10% |
| Base already at 2★ or more | 0% (no roll) |

### 3.3 Finding the partner (`PartnerFinder`, `BirthRecord`)

**Partner prefab**: `m_seperatePartner` if set, else the parent's own prefab (`m_myPrefab`, or resolved like vanilla
from `ZNetScene.GetPrefab(zdo.GetPrefab())` when the field is still null; the field itself is never written).

**`FindNearest(procreation, filter, range)`**: among `BaseAI.BaseAIInstances`, creatures whose `gameObject.name` is the
partner prefab name + `"(Clone)"`, not the parent itself, within `range` with vanilla's distance test (a range of 0 or
less = no limit, 1.3), passing `filter`, with a `Character`; the **nearest** one (decision 4).

**At conception** (postfix, owner, the call went from not pregnant to pregnant): `FindNearest(filter = vanilla's
"ready" filter: a creature with a `Procreation` must be `ReadyForProcreation()`, one without counts as is, range =
m_partnerCheckRange)`. Then:

- Partner found → `BirthRecord.Write(zdo, partner.GetLevel(), s_pregnant ticks)`.
- No partner, and the species breeds alone (`m_noPartnerOffspring` set) → `Write(zdo, 0, ticks)`: 0 = "bred alone".
- No partner, and the species needs one → vanilla counted a partner that our scan cannot see (a partner prefab without
  `BaseAI`, which vanilla counts through tagged objects, or partners added by another mod's `GetNrOfInstances` patch,
  such as AllTameable's cross-species mates). No record is written (an old one is cleared, see below) and one Debug
  line says so; the birth uses the fallback below.

**At birth** (prefix, every pregnant tick):

1. `BirthRecord.TryRead`: the record counts only if its stamp equals the **current** `s_pregnant` value (read before
   vanilla resets it). Level > 0 → source `recorded`; level 0 → source `none (bred alone)`.
2. Otherwise (conceived before install, while the mod was off, or on a game without the mod; a conception whose partner
   our scan could not see; or a stale record from an older pregnancy): `FindNearest(filter = tamed (`Character.
   IsTamed()`), range = max(m_partnerCheckRange, m_totalCheckRange))` (`PartnerFinder.BirthRange`; if either range is
   0 or less, which another mod may set to mean "no limit", there is no limit) → source `found at birth` (decision 5). The
   partner may be pregnant or hungry by now, and it may have wandered away from the 3 m partner range during the
   pregnancy, so the scan covers the population range the game itself treats as "the pen" (code default 10 m).
3. Still nothing → no partner: the baby starts from the parent's own level, source `none` (Goal item 3's stated limit,
   T17).

The record is cleared in the postfix once the birth happened: `BirthRecord.Clear` **overwrites** it (partner level 0,
stamp 0) instead of removing the keys, because a removal never reaches the server or the other games (1.12); stamp 0
means "no record" to `TryRead`. It writes only when a record is there (a stamp other than 0), so animals that never
had one get no keys. A record that is not cleared (the birth happened on a game without the mod, or while it was off)
stays with its old stamp and is ignored because the stamp no longer matches.

The partner leaving, dying or being dragged away (Harpoon Hooks Tames) between conception and birth changes nothing:
the recorded level is used.

### 3.4 Farmers and the Farming skill (`FarmerSkill`)

**Whose skill counts** (decisions 1-3): the best of

- the **local player** of the game that decides the birth (`Player.m_localPlayer`, when it exists), at **any
  distance**: that game simulates the pen, so its player is within the loaded area anyway. Its skill is read directly
  and fresh with `FarmerSkill.OwnLevel` (below). In single player this is always you, so the chance simply follows
  your Farming;
- **other players** running the mod within `FarmerRange` of the parent, through the level they publish.

No local player and no other farmer in range → "no farmer known" → `ChanceWithoutFarmer`.

**Reading the own skill without adding it** (`FarmerSkill.OwnLevel`, decision 20): `GetSkillLevel` would add a
Farming entry to a character that never farmed (1.9). So when the character has a Farming entry
(`Skills.m_skillData`), the mod calls vanilla `GetSkillLevel(Farming)` (other mods' patches on it keep working);
when it has none, it computes what vanilla would return, 0 modified by status effects (`SEMan.ModifySkillLevel`) and
floored, without adding the entry. A character with no Farming entry therefore counts as Farming 0 (plus effects) and
gets no Skills-tab row from this mod.

**Publish** (every game running the mod; decision 2):

- Value: `FarmerSkill.OwnLevel(Player.m_localPlayer)` (effective level, status effects included, floored), written as
  a float to the local player's ZDO key `MC.Farming.Breeding.StarInheritance.FarmingLevel`. The local game owns its
  player ZDO.
- `PublishNow` compares the level with the value **currently on the local player's ZDO** (a missing key counts as
  different), never with a value cached in the mod, and writes only when they differ. Respawning or reconnecting
  creates a new player ZDO without the key (1.9, 1.12), so the next publish writes it again even when the level did
  not change (for example Farming 0, or a second death within the "No skill drain" time).
- Guards: `ZNet.instance != null`, `ZDOMan.instance != null`, `Player.m_localPlayer != null` and
  `m_localPlayer.m_nview.IsValid()` (Unity null checks); otherwise nothing is done.
- When (decision 19):
  - **When the local character spawns** (a `Player.OnSpawned` postfix, local player only): joining a world,
    respawning and reconnecting all create a new player ZDO, and the level goes on it at once. `Game.SpawnPlayer`
    calls `SetLocalPlayer`, then `PlayerProfile.LoadPlayerData` (the skills), then `OnSpawned`, so the level read there
    is the loaded one (after a death's skill loss) and the ZDO is valid and owned.
  - **On breeding ticks**: from the `Procreate` prefix, which ticks on **every** game for every loaded animal that can
    breed, tame or wild (1.1), throttled (`PublishThrottled`) to once per 5 s of `Time.time` per game. This carries
    later changes (a level gained, a status effect) within one tick of an animal loaded on that game: every 30 s for
    boars and hens while the pen stays loaded, 30-45 s after it loads. A farmer at the pen has its animals loaded on
    their own game too, so a change reaches the deciding game about one tick later (plus the usual ZDO sync delay).
  - **At once in `OnActivated`** (turning the mod on while in a world). No per-frame patch.
- Turned off: `OnDeactivated` writes **-1** ("not taking part") with the same guards (a removal would not be sent to
  peers, 1.12), only when a value is published (a missing key already means "not a farmer"). At application quit `ModPlugin.OnDestroy` also calls `OnDeactivated`; if `ZNet` or the player ZDO is
  already gone the guards skip the write, so quitting never logs an error. A new session starts without the key
  (player ZDOs are not saved).

**Read at birth** (the deciding game):

- Other players: `Player.GetAllPlayers()` except the local player, within `FarmerRange` of the parent giving birth
  (`Vector3.Distance < range`, as `Player.GetPlayersInRange`); the published key on each one's ZDO (`GetFloat(key,
  -1)`); missing or negative = a player without the mod or with it off: **not a farmer**.
- Only players **loaded** on the deciding game can count (1.10): a farmer at the pen but outside that game's active
  area (its player far on the other side) is not seen. `FarmerRange` is therefore capped at 64 m (one zone), and the
  README says a farmer counts "while the player whose game handles the pen is not too far away".
- The **highest** Farming level among the local player and the other farmers counts; `clamp01(level / 100)` feeds the
  rule (3.2).

Why 60 m for other players (decision 3): vanilla's own "players near a tame help it" range
(`Tameable.m_tamingSpeedMultiplierRange`, code default 60), roughly the size of a base, and within one zone.

### 3.5 Patches

All bodies catch their own exceptions and call `PatchGuard.Report(site, e)`. Applied only while the feature is Active
(framework); turning the mod off removes them. The three `Procreate` methods are in one patch class,
`ProcreationPatches`, so they share one `__state` (a small struct `BirthState { bool Owned; bool WasPregnant; bool Decided; bool Swapped; int OriginalLevel;
… decision data for the log }`; no allocation per tick). The finalizer also takes `Exception __exception` and acts
only when there is one.

| Target | Type | What it does | Why |
|---|---|---|---|
| `Procreation.Procreate()` (private, no overload) | Prefix (`void`, never skips), `[HarmonyPriority(Priority.Last)]`, `bool __runOriginal`, `out BirthState __state` | 1. `__state = default`. 2. `FarmerSkill.PublishThrottled()` (any game, any animal; guarded, 3.4). 3. `!__runOriginal` → if the skipped call would have been a birth (owner, tamed, `IsPregnant() && IsDue()`), one Debug line per activation ("Another mod skipped the game's breeding code for a birth that was due; Breeding Star Inheritance leaves that call alone."); return. 4. Star Level System loaded (`Compat`, looked up at the first call after activation, decision 17) → return (decision 15). 5. Return unless `m_nview.IsValid() && m_nview.IsOwner()`, `m_tameable != null && m_tameable.IsTamed()`, `m_character != null` (Unity null checks). 6. `Owned = true`; `WasPregnant` = the `s_pregnant` stamp is not 0. 7. If `WasPregnant`: partner (3.3), farmer (3.4), `BirthRule.Decide` with `Random.value` (3.2), `Decided = true`; `OriginalLevel = m_character.m_level`; only when the result differs: `m_character.m_level = level`, `Swapped = true`. | Runs last so other mods' prefixes (Seasons, BreedingUpgrades) have usually already run and may have skipped the original; the only place before vanilla reads the parent level. No birth prediction (decision 16): if the call is not due, vanilla returns before reading the level and the swap is undone in the postfix. |
| `Procreation.Procreate()` | Postfix, `[HarmonyPriority(Priority.First)]`, `BirthState __state` | Nothing unless `Owned`. 1. `Swapped` → `m_character.m_level = OriginalLevel`. 2. `WasPregnant` and the stamp is now 0 → a birth happened in this call: `BirthRecord.Clear`, Debug birth line (3.12, only when `Decided`). 3. `!WasPregnant` and the stamp is now set → conception: partner scan, `BirthRecord.Write` or no record (3.3), Debug conception line. | First among postfixes, so other mods' postfixes see the real parent level. Detecting birth and conception from the ZDO before and after the call avoids patching `MakePregnant` (decision 12) and never depends on our own prediction. |
| `Procreation.Procreate()` | Finalizer, `BirthState __state`, `Exception __exception` | Only when `__exception != null` and `Swapped` (the original or a postfix threw, so our postfix may not have run) → `m_character.m_level = OriginalLevel` (idempotent). | Exception path: the parent never keeps the baby's level. A `void` finalizer leaves the exception as it was. It does nothing on the normal path, so it never overwrites a level another mod's later postfix may set. |
| `Player.OnSpawned(bool)` (`PlayerPatches`) | Postfix | Only for `Player.m_localPlayer` (vanilla calls it only from `Game.SpawnPlayer`, for the local character): `FarmerSkill.PublishNow()` (guarded, 3.4). | Joining, respawning and reconnecting give the player a new ZDO without the published level; without this, a friend would not count until the next breeding tick on their game (30-45 s after a pen loads). Once per spawn, after the skills are loaded (decision 19). |
| `ZNet.OnNewConnection(ZNetPeer)` (`ZNetPatches`) | Postfix | Server: register `<GUID>.SettingsRequest` on the peer's `ZRpc`. Client: forget the settings of an older session, register `<GUID>.Settings` (3.9). | The per-peer `ZRpc` exists from here; the client handler must exist before the server's answer arrives. |
| `ZNet.RPC_PeerInfo(ZRpc, ZPackage)` (`ZNetPatches`) | Postfix | Only when the peer is ready (vanilla accepted it, 1.14). Server: `PlayerCheck.Schedule(peer)` (grace timer). Client: register `<GUID>.Settings` again (without forgetting anything), then send `<GUID>.SettingsRequest` to the server. | The end of the handshake: the peer is fully in, and `PeerHasMod` is final (1.14). A postfix never skips vanilla code, so version-check mods that refuse in a prefix keep working. The client registers here too because a copy turned on during the connecting screen missed the `OnNewConnection` postfix, and its `OnActivated` found no server peer yet (`ZNet.GetServerPeer()` is null until the client's own `RPC_PeerInfo` sets Connected); without the handler the server's answer would be dropped silently (unknown RPC). `ZRpc.Register` replaces, so twice is safe. |
| `ZNet.Update()` (`ZNetPatches`) | Postfix | Two bool reads; when there is work: `PlayerCheck.Update()` (deadlines) and `ServerSettings.Update()` (send the settings after a change). | The timer. Runs on every game and on the dedicated server; removed with the other patches when the feature turns off. |

No transpiler; no patch on `Character.SetLevel`, `ItemDrop.SetQuality`, `EggGrow`, `Growup` or any per-frame method
other than the two-bool `ZNet.Update` postfix.
The level field is only changed on the one parent, inside one call on the main thread.

Priority ties: `Priority.Last` is the lowest named priority, so another mod's prefix at the same priority that was
patched later still runs after ours, and a postfix at `Priority.First` patched earlier runs before ours. Such a prefix
or postfix would see the swapped level; with no birth prediction, the rule still applies to every real birth.

### 3.6 Helper classes

- `BirthRule` (static, pure, no Unity call): `RuleSettings` (the four rule settings, `Defaults` = 15 / 50 / 10 / 2),
  `BaseLevel(own, partner, minOffspring)`, `Cap(maxStars)`, `Chance(farmerKnown, farming, settings)`,
  `Wins(chance, roll)`, `Decide(own, partner, minOffspring, settings, farmerKnown, farming, roll) → BirthDecision
  { Level, Base, Cap, Chance, Roll, Bonus, AtCap }`. The roll is an argument (decision 18).
- `PartnerFinder` (static): `PartnerPrefab(Procreation)`, `BirthRange(Procreation)`, `FindNearest(Procreation,
  readyOnly, range, out distance) → Character` (3.3). Loops `BaseAI.BaseAIInstances` once (hundreds at most), at
  conception and on pregnant ticks without a valid record.
- `BirthRecord` (static): key hashes `"MC.Farming.Breeding.StarInheritance.PartnerLevel".GetStableHashCode()` (int)
  and `"MC.Farming.Breeding.StarInheritance.ConceivedAt".GetStableHashCode()` (long); `Write`, `TryRead(zdo,
  currentStamp, out level)`, `Clear` (overwrites a record with partner 0 and stamp 0, never removes, 3.3),
  `StoredStamp` (self-test: -1 = no key, 0 = cleared).
- `FarmerSkill` (static): key `"MC.Farming.Breeding.StarInheritance.FarmingLevel"` (float); `OwnLevel(Player)` (the
  own effective Farming without adding a skill entry, 3.4), `HasFarmingEntry(Player)` (self-test);
  `PublishThrottled()`, `PublishNow()`, `Withdraw()` (-1), all guarded (3.4); `ReadPublished(Player)`;
  `FindBest(Vector3 position, float range, out Player farmer, out float level, out bool local)`; `Reset()` (throttle
  timer).
- `Compat` (static): `StarLevelSystemLoaded` = `BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(
  "MidnightsFX.StarLevelSystem")` (the GUID in its `StarLevelSystem.cs`), looked up at the first use after each
  activation (`Reset()` in `OnActivated`, decision 17).
- `ServerSettings` (static): `SettingsSource` (Own / Server / Test) and `BreedingSettings { Rule, FarmerRange, Source
  }`; the pure `Encode(rule, range)`, `TryDecode(payload, out rule, out range, out clamped)` and `Pick(own,
  useServer, serverRule, serverRange)`; `Effective(own)` (the server's numbers only for a connected client that got
  them in the current `ZNet` session); the wire (`RegisterServer`, `RegisterClient`, `Request`, the two handlers,
  `Receive(payload)`); `Start`/`Stop` (activation), `MarkChanged` + `Update` (send after a change) (3.9).
- `PlayerCheck` (static): the join check (3.9): `JoinVerdict` and the pure `Decide(isServer, connected, ready,
  beingKicked, hasMod, allowWithoutMod)`; `Schedule(peer)`, `ScheduleAllConnected()`, `Update()`, `Start`/`Stop`.
- `Plugin.OwnSettings()` (this game's config values) and `Plugin.CurrentSettings()`: the only door to a birth's
  numbers: the Debug override (rule only) over `ServerSettings.Effective(OwnSettings())`.
- `SelfTests` (Debug builds only): the in-world self-tests (6.1) and `Override`, rule settings a test forces without
  writing the config file (`Plugin.CurrentSettings` applies it first in Debug builds; the range stays the effective
  one).

### 3.7 Configuration

Section `General`, `Enabled` first (framework) and `Status` (framework). All settings apply live (read at each birth,
or at each join check for `AllowPlayersWithoutMod`). The five breeding settings end with " In multiplayer the setting
of the server (or host) is used for everyone."

| Section | Setting | Type / default | Description (user-facing) |
|---|---|---|---|
| General | Enabled | bool `true` | (framework) Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | string, read-only | (framework) Written by the mod: shows whether the feature is active, and if not, why. Editing it has no effect. |
| General | ChanceAtFarming0 | float `15`, 0-100 | Chance (percent) that a baby gets one star more than its weaker parent when the best farmer has Farming 0. It rises in a straight line to the next setting at Farming 100. The Farming of the player whose game handles the birth always counts. (Order 90) |
| General | ChanceAtFarming100 | float `50`, 0-100 | Chance (percent) of the extra star when the best farmer has Farming 100. (Order 80) |
| General | ChanceWithoutFarmer | float `10`, 0-100 | Chance (percent) of the extra star when no farmer is known at all: the game handling the birth has no character at that moment (for example while its player respawns) and no other player with this mod is within FarmerRange. Rare in normal play. (Order 70) |
| General | FarmerRange | float `60`, 5-64 (metres) | How close another player must be to the animal giving birth for their Farming skill to count. Only players who have this mod count, and only while the game handling the birth has them loaded. The Farming of the player whose game handles the birth counts at any distance. (Order 60) |
| General | MaxStars | int `2`, 0-10 | The extra star never takes a baby above this many stars (2 is the most the game shows). Babies whose weaker parent already has this many stars or more keep that level and get no extra star. 0 turns the extra star off. (Order 50) |
| General | AllowPlayersWithoutMod | bool `false` | Used only by the server (or the host). Off (default): a player whose game does not have this mod installed is refused about a second after joining, and their game shows "Incompatible version", because the animals their game simulates would breed the normal game way. The check only looks at whether the mod is installed: a player who turned it off on their own game is not refused. On: players without the mod may play, and the animals their game simulates breed the normal game way (the baby takes the level of the parent that gives birth). (Order 40) |

`SettingChanged` handlers, subscribed only while Active: the five breeding settings → `ServerSettings.MarkChanged()`
(the server sends them again on the next `ZNet.Update`, once for a whole config-file save); `AllowPlayersWithoutMod`
switched to `false` → `PlayerCheck.ScheduleAllConnected()` (players already online are checked too). Nothing is
cached for the rule itself. The ranges are enforced by BepInEx (`AcceptableValueRange`, bounds shared with
`ServerSettings` so the received values are clamped to the same ranges) and the chances are clamped again in
`BirthRule.Chance`.

### 3.8 Data and persistence

| Where | Key | Type | Written by | Lifetime |
|---|---|---|---|---|
| Parent's ZDO (every animal that conceived while the mod ran) | `MC.Farming.Breeding.StarInheritance.PartnerLevel` | int, partner level or 0 = bred alone (also 0 once cleared) | the simulating game at conception; overwritten with 0 by the game that handles the birth | stays on the animal and in the world save; read only while the stamp below matches the current pregnancy |
| Same | `MC.Farming.Breeding.StarInheritance.ConceivedAt` | long, the `s_pregnant` ticks of that pregnancy; 0 = cleared (no record) | same | same: 0 after a birth handled by a game with the mod, else the old stamp, ignored once the pregnancy is over |
| Each player's own ZDO | `MC.Farming.Breeding.StarInheritance.FarmingLevel` | float, effective Farming level, -1 = mod off | that player's game | that player object (a respawn or reconnection starts a new ZDO without it; not saved) |

- The baby's level is vanilla data (`s_level` on the creature, `m_quality` in the egg's item data): nothing of ours
  on babies or eggs. The two parent keys are never removed (a removal would not reach the other games, 1.12), so
  every animal that conceived while the mod ran keeps them in the world save, also after uninstalling: an int and a
  long that vanilla ignores. Animals that never conceived under the mod get no key. The mod adds nothing to the
  character file (it never adds a Farming skill entry, 3.4).
- Key names start with the permanent GUID. A future change of meaning or format gets a new key name and a
  `ModNetworkVersion` bump (now 1).

**Network messages** (per-player `ZRpc`, never routed; vanilla games ignore unknown names, 1.14):

| RPC | Direction | Payload | When |
|---|---|---|---|
| `<GUID>.Hello` / `<GUID>.HelloAck` | client ↔ server | framework (`NetworkGate`, protocol 1 with the network version) | at connect, and when the server's copy turns on or off |
| `MC.Farming.Breeding.StarInheritance.SettingsRequest` | client → server | `string` `"1"` (protocol) | the client's `RPC_PeerInfo` (right after the handshake), and when the client's copy turns on while connected |
| `MC.Farming.Breeding.StarInheritance.Settings` | server → client | `string` with six fields separated by a vertical bar: protocol `1`, ChanceAtFarming0, ChanceAtFarming100, ChanceWithoutFarmer, FarmerRange, MaxStars (defaults: `1\|15\|50\|10\|60\|2`); floats in invariant culture, format `R` (exact round trip) | the answer to a request; to every ready player with the mod when the server's settings change and when the server's copy turns on |
| `Error` (vanilla) | server → client | `int` 3 (`ErrorVersion`) | the join check refuses a player (3.9) |

Nothing new is saved: the received settings live in memory for the current `ZNet` session only, and the join check
keeps a list of (peer, deadline) in memory.

### 3.9 Multiplayer and hand-off

Who decides a birth: the game that owns (simulates) the pregnant parent at that moment (1.11). Its record, its own
player and its view of the other players nearby, with the server's settings (3.9.2).

| Situation | Result |
|---|---|
| Single player | Always the mod's rule with the own settings; the farmer is always you (your Farming counts wherever you are while the pen is loaded). |
| The simulating game runs the mod | The mod's rule with the server's settings; farmers = that game's own player, plus other players running the mod within `FarmerRange` (through their published level); the best one counts. |
| A player whose game does not have the mod joins a server with the mod on | Refused about 1 s after getting in and disconnected 4 s later at the latest (3.9.1): their game is gone before its first breeding tick, as long as every species' tick is longer than those 5 s (checked on the real prefabs by the `breeding.network` self-test; see Unverified names and values). |
| Same, with the server's `AllowPlayersWithoutMod` on (or while the server has the mod off) | Plays; animals their game simulates get vanilla births (the baby takes that parent's own level). The record, if any, is ignored and becomes stale. Warning in the server log. |
| A player with the mod installed but turned off (`Enabled = false`, or an error state) on their own game | Not refused (the handshake only says the mod is installed); vanilla births for the animals their game simulates while it is off; not a farmer (withdrawn). Known limit (open question 7). |
| A player with the mod on a server (or host) without it | Framework: the mod turns itself off on that game for the session (ServerMissing): vanilla births there. |
| The server turns the mod off | Framework: every client's copy turns off with it (the server tells them): vanilla births everywhere until it is back on; nobody is refused meanwhile. Back on: clients turn on and ask for the settings again, players without the mod are checked again (refused after the grace). |
| Pregnancy conceived on a game without the mod, born on a game with it | Partner found at birth within the population range (3.3); the mod's rule. |
| Pregnancy conceived with the mod, ownership moves to another game with the mod | The record travels with the parent's ZDO; the new owner uses it. |
| A player without the mod stands at the pen | Does not count as a farmer (nothing published); the deciding game's own player still counts. |
| A farmer joins, dies and respawns, or reconnects | New player ZDO; the level is published on it as soon as the character spawns (3.4), and reaches the deciding game with the next ZDO sync. |
| A farmer's skill changes while alive (level gained, status effect) | Published at the next breeding tick of an animal loaded on the farmer's game (every 30 s for boars and hens; 3.4). |
| The birth of a pregnancy recorded with the mod is handled by a game with the mod | The record is cleared by overwriting (partner 0, stamp 0), so the server and every other game get the cleared value (1.12, 3.3). |
| A farmer at the pen who is not loaded on the deciding game (that game's player is far on the other side) | Not counted (1.10); `FarmerRange` is capped at 64 m to keep this rare. |
| Players with different settings in their own files | Every game connected to a server with the mod uses the server's five breeding settings (3.9.2); the own files are untouched and apply again in single player and when that player hosts. |
| A baby or an egg made with the mod reaches a player without it (allowed by the server) | Normal vanilla data: the creature keeps its level (stars for everyone); the egg keeps its quality and a vanilla game hatches it at that level; growing up keeps it (vanilla). |
| Dedicated server | Runs the mod (a `Both` mod has no `BepInProcess`, so it loads in `valheim_server.exe`): join check and settings. It never simulates animals (1.11): its birth patches have nothing to do. A host is a normal game plus the server part. |
| A mod that makes the dedicated server simulate the world | Births then run on the server, which runs this mod: the rule applies; the server has no local player, so only players with the mod within `FarmerRange` count as farmers, else `ChanceWithoutFarmer` (not tested, 3.11). |

#### 3.9.1 Join check (`PlayerCheck`)

On the server (dedicated or host), while the feature is Active. Never in single player (no peer) and never on the
host's own player (not a peer).

1. **Hook:** `ZNet.RPC_PeerInfo` postfix; only a ready peer (`IsReady()`: vanilla accepted it, 1.14) is scheduled,
   with a deadline `Time.unscaledTime + 1 s` (`GraceSeconds`). The same peer twice keeps its first deadline.
2. **Timer:** `ZNet.Update` postfix; nothing to do = one bool read. Due entries leave the list before they are
   checked.
3. **Check** (`Decide`, pure): not the server, or the peer is gone (not in `ZNet.GetPeers()` or its `ZRpc` no longer
   connected), not ready, or already in the vanilla kick list → nothing. `NetworkGate.PeerHasMod(peer)` → fine (Debug
   line). Otherwise `AllowPlayersWithoutMod` on → let in (Warning: the animals their game simulates breed the vanilla
   way); off → **refuse**.
4. **Refuse** the vanilla way: Warning naming the player (name and platform id) and why; `peer.m_rpc.Invoke("Error",
   (int)ZNet.ConnectionStatus.ErrorVersion)`; the peer goes into vanilla's `ZNet.PeersToDisconnectAfterKick` with
   `Time.time + 4 s` (`DisconnectDelay`), so vanilla's own `ZNet.Update` disconnects it after the message left (the
   vanilla kick path, 1.14, with a longer delay: see below). The client logs out and its main menu shows
   "Incompatible version".
5. **Cancel and re-check:** `OnDeactivated` clears the list (a pending check is cancelled; a refusal already sent
   still completes, through vanilla's list). `OnActivated` schedules every ready peer (the server turned on later with
   players online). `AllowPlayersWithoutMod` switched to `false` schedules every ready peer too.

Why 1 s + 4 s: the modded client's `Hello` is always there before `PeerInfo` (1.14), so the grace is only a safety
margin and can be short. The disconnect delay is long because the refusal lands while the refused game is on its
loading screen, where frames are long: on the client, `ZNet.UpdatePeers` first checks each peer's socket and, for a
closed server socket, sets `ErrorDisconnected` and drops the peer before its second loop reads the queued packets
(`ZRpc.Update`), so if one client frame covers both the `Error` packet and the socket close, the `Error` is never read
and the menu shows the disconnection text. With vanilla's 1 s kick delay one long loading frame is enough; with 4 s
the client gets many frames to read the `Error` and log out by itself. The sum, 5 s, must stay below the first
breeding tick a joining game can run (at least `m_updateInterval` after it received the animals; 30 s for boars and
hens), so a refused game never decides a birth. The `breeding.network` self-test checks this on every prefab with
`Procreation` and logs each species' tick in a `NOTE` line.

#### 3.9.2 Server settings for everyone (`ServerSettings`)

1. **Client asks:** the client registers `<GUID>.Settings` in its `OnNewConnection` postfix (after forgetting the
   values of an older session), registers it again and sends `<GUID>.SettingsRequest` in its `RPC_PeerInfo` postfix,
   right after the handshake (the second registration covers a copy turned on during the connecting screen, which
   missed `OnNewConnection` and found no server peer in `OnActivated`). A client whose copy turns on once connected
   (`OnActivated`) registers and asks then. A server without
   the mod ignores the request (and the framework turns the client's copy off anyway).
2. **Server answers** each request from a ready peer while Active with its own five values (`Encode`). It also sends
   them to every ready peer with the mod when they change (`SettingChanged` → `MarkChanged` → next `ZNet.Update`, one
   message per config save) and when its copy turns on.
3. **Client checks** (`TryDecode`): exactly six fields, protocol `1`, plain invariant numbers, no NaN or infinity,
   else the whole message is refused (Warning once per session; the last good values, or the own ones, stay in use).
   Out-of-range values are clamped to the config ranges (chances 0-100, `FarmerRange` 5-64, `MaxStars` 0-10) with a
   Warning. Accepted values are stored with the `ZNet` instance they came on, and an Info line gives them (once per new
   set of values).
4. **Effective settings** (`Plugin.CurrentSettings`, the only accessor, read at every birth): the Debug self-test
   override (rule only) over the server's values when this game is a client (`!IsServer()`) of the same `ZNet`
   session that delivered them, else the own config. Single player, host and dedicated server: always the own config
   (a server never takes settings from a peer: `Receive` refuses them). Disconnecting ends the `ZNet` session, so the
   own values apply again at once; `OnDeactivated` forgets them too.

The birth Debug line ends with `own settings`, `server settings` or `self-test settings`.

`ModMultiplayerNotes` (player-facing, in the csproj): "Install it on the server (or the host) and on every player's
game. A birth is decided by the game of the player that is simulating the animal, and a game without the mod would
use the normal game rule, so the server refuses players who do not have the mod (their game shows Incompatible
version), unless its AllowPlayersWithoutMod setting is on. The breeding settings of the server (or host) apply to
everyone. Other players' Farming counts when they stand near the animal. On a server without the mod it turns itself
off. Babies and eggs are normal for everyone."

### 3.10 Live toggle

- **Turned off**: patches removed → the next births are vanilla. `OnDeactivated` withdraws the published Farming level
  (-1, guarded) and resets the throttle. Records already on pregnant animals stay and become stale (harmless; a later
  birth handled by a game with the mod on clears them). A level
  swap cannot be in progress (toggles apply between frames). Network part: the `SettingChanged` handlers are removed,
  pending join checks are cancelled, the server stops answering settings requests (its handlers stay registered on
  the peers' `ZRpc`, which has no unregister, and check a flag), and a client forgets the server's values. On the
  server, the framework then tells every client that its copy is off, so their copies turn off too (3.9).
- **Turned on**: patches applied; `OnActivated` resets the Star Level System lookup (done again at the first breeding
  tick, decision 17), clears the stand-aside log flag, and publishes the Farming level at once if the local player
  exists. Pregnancies conceived while off use the partner found at birth. Network part: a server registers the
  settings request on peers that joined while it was off, sends its settings to every player with the mod, and
  schedules a join check for every ready peer; a client already connected registers the settings handler and asks
  for the settings.
- `Enabled = false` + restart: no patch, no key written, vanilla breeding; `Status` "Off (disabled in settings)". On a
  client the framework still answers the server's handshake, so the server counts it as having the mod (open question
  7).

### 3.11 Compatibility

**Other mods** (section 2):

- BreedingUpgrades and Procreation Plus add their own +1 roll on top of ours (two chances; the README says to use
  one).
- **Star Level System**: not compatible. When it is loaded this mod leaves every birth to it (no level swap, so its
  egg counts are not changed) and writes one Info line at the first breeding tick after activation: "Star Level
  System is installed: it decides breeding levels, so Breeding Star Inheritance leaves births to it." (decisions 15,
  17).
- Creature Genetics / AllTameable and TameCraft's genetics option replace the breeding rules: do not combine (README).
- Seasons, FeedLikeGrandma and TameGuildWars are compatible.
- A mod that skips vanilla `Procreate` makes this mod stand aside for that call (`__runOriginal`; one Debug line per
  activation, only for a skipped birth).
- A mod that patches `Character.SetLevel` or `ItemDrop.SetQuality` sees our level as the argument, like any vanilla
  level.
- Mods that make a dedicated server simulate the world (server-side simulation mods, names not checked) move births to
  the server, which now runs this mod too: the rule applies there, with only players with the mod within
  `FarmerRange` as farmers (the server has no local player), else `ChanceWithoutFarmer`. Not tested; README note.
- Version-check mods (Jotunn's, ServerSync's) refuse in a `ZNet.RPC_PeerInfo` prefix before the peer is ready; this
  mod's postfix ignores peers that are not ready, and never skips vanilla code. A player they let in is still checked
  by this mod. Not tested.
- Mods that raise the player limit or change the handshake (transpilers on `RPC_PeerInfo`): the join check only
  needs the peer to become ready at the end of `RPC_PeerInfo`.

**MC mods** (none patches `Procreation`, `Character.SetLevel`, `ItemDrop.SetQuality`, `EggGrow` or `Growup`;
`Player.OnSpawned` is also patched by Creature Kill and Tame Counts and by the shared framework's MC Mods panel, all
postfixes that do not depend on each other):

- **Sort Chest**: its merge requires the same `m_quality`, so eggs of different levels are never merged by a sort;
  sorting never changes item data (X01).
- **Creature Kill and Tame Counts**: counts tames through `Tameable.Tame`; babies, hatchlings and grown animals are
  tamed without it and are not counted (its documented rule). Both mods have a `Player.OnSpawned` postfix (it writes
  its tame-counting start day, this mod publishes Farming): independent, no order needed (X02).
- **Harpoon Hooks Tames**: can drag a partner away between conception and birth; the recorded level is used (X03).
  No shared method.
- **Loot Pickup Filter**: may skip picking up eggs; item data untouched. No cross test needed.
- **Sleep Through the Day**: also a `Both` mod. Each MC mod has its own framework copy, handshake RPC names and
  Harmony ids; the framework patches on `ZNet` of both and this mod's own `ZNet` postfixes are independent (no MC mod
  besides the framework patches `ZNet`). A player with Sleep Through the Day but without this mod is refused by this
  mod; the reverse is Sleep Through the Day's own rule (X05).
- Others (Crossbow Stays Loaded, One Click Repair All, Batch Station Feeding, Crafting Search and Sort, Encyclopedia):
  unrelated.

**README "Good to know"** (from this section and section 4): keep a breeding pair apart from other animals of the same
species if you want a predictable result (with three or more ready animals close together, the nearest one counts as
the other parent, decision 4); the egg's level always shows in its ground hover ("Egg[2]"), whether the inventory shows
it is not confirmed yet (T16, and the egg's max quality in the `breeding.egg` self-test NOTE line), and dragging an egg
onto a stack of eggs of another level can merge them (pick eggs up or drop them instead). The README states this
conditionally until T16 is done.

### 3.12 Debug aids (Debug log level only)

- Conception: `Conceived: <prefab> (level <own>) with partner level <p> at <d> m.`, `Conceived: <prefab> (level
  <own>): no ready partner within <r> m, bred alone.`, or `Conceived: <prefab> (level <own>): the game counted a
  partner this mod cannot see; the partner will be looked for at birth.`
- Birth: `Birth by <prefab> (simulated by this game): own <own>, partner <p (recorded) | p (found at birth, <d> m) |
  none (bred alone) | none (no tamed partner within <r> m)>, min offspring <m> -> base <b>; <farmer you Farming <f>
  (own) | farmer <name> Farming <f> (published) | no farmer known> -> <chance <c>%; roll <v> -> level <n>[, extra star]
  | level <n>, at the cap: no roll>; <own | server | self-test> settings.` A range of 0 or less (no limit) reads
  "anywhere nearby".
- Publish: `Published Farming level <f> for other players.` (only when a write happened); `Withdrew the published
  Farming level (mod turned off).`
- Stand aside: once per activation, only for a skipped call that would have been a due birth (3.5).
- Star Level System: the Info line at the first breeding tick after activation (3.11).
- Network (these are not Debug-only; the levels are given): client Info `Using the server's breeding settings:
  ChanceAtFarming0 <a>, ChanceAtFarming100 <b>, ChanceWithoutFarmer <c>, FarmerRange <r>, MaxStars <m>. Your own
  settings apply again in single player and when you host.` (once per new set of values); client Warning `The server
  sent breeding settings outside the allowed ranges; they were brought back into range: ...` and `The server sent
  breeding settings this version cannot read (...); ...` (once per session); server Warning `Refused <player>
  (<platform id>): their game does not run Breeding Star Inheritance, which this server requires on every player (a
  game without it would breed the animals it simulates the vanilla way). Their game shows "Incompatible version". To
  let such players in, set AllowPlayersWithoutMod = true.` or `<player> (<platform id>) joined without Breeding Star
  Inheritance. AllowPlayersWithoutMod is on, so the animals their game simulates breed the vanilla way (the pregnant
  parent's own level).`; server Debug `<player> (<platform id>) has Breeding Star Inheritance installed: allowed.` and `Sent
  the breeding settings to <n> player(s): <payload>.`

These lines are how every test reads the partner level, the farmer, the chance and the roll.

### 3.13 Performance

- `Procreate` ticks every `m_updateInterval` (30 s for boars and hens, code default 10 s) per loaded breeding animal
  on every game. The prefix: one float compare (throttle), a few Unity null checks and one ZDO long read on the owner;
  nothing else unless the animal is pregnant. The postfix: bool checks and one ZDO read on the owner.
- Per pregnant animal and tick: two ZDO reads for the record, one loop over `Player.s_players`, a loop over
  `BaseAI.BaseAIInstances` only when there is no valid record, one `Random.value`. Per conception: one loop over
  `BaseAI.BaseAIInstances`, two ZDO writes; per birth, two more when a record is cleared. One Debug line per birth or
  conception. Publishing: once per spawn, and at most once per 5 s per game on ticks: one dictionary lookup and
  `GetSkillLevel` (or the status-effect loop), one ZDO read, a write only when the value differs.
- No allocation per tick (`__state` is a struct). Debug strings are built only at events (a conception, a birth, a
  publish that writes): BepInEx 5.4.23 has no way to ask whether Debug is listened to, and these events are minutes
  apart per animal.
- Network part: the `ZNet.Update` postfix reads two bools per frame when idle. A join check is one list entry per
  joining player and one `GetPeers().Contains` (10 players at most) at its deadline. Settings: one short string per
  player at join and per config save on the server; `CurrentSettings()` at each pregnant tick reads the five config
  values and compares the `ZNet` instance.

### 3.14 Files

| File | Responsibility |
|---|---|
| `MC.Farming.Breeding.StarInheritance.csproj` | Metadata (`ModName` Breeding Star Inheritance, `ModScope` Revamp, `ModSide` Both, `ModMultiplayer` Compatible, `ModNetworkVersion` 1, notes 3.9.2), `<ModIdea>Breeding revamp</ModIdea>`. |
| `Plugin.cs` | `BindConfig` (3.7), `OwnSettings()` / `CurrentSettings()` (read at every birth); `OnActivated`: `Compat.Reset()` (lookup at first use), clear the stand-aside log flag, `FarmerSkill.Reset()`, `PublishNow()`, `ServerSettings.Start()`, `PlayerCheck.Start()`, subscribe the `SettingChanged` handlers, register the self-tests (Debug); `OnDeactivated`: unregister the self-tests, unsubscribe, `PlayerCheck.Stop()`, `ServerSettings.Stop()`, `Withdraw()`, `Reset()`, clear the stand-aside log flag. |
| `ServerSettings.cs` | `SettingsSource`, `BreedingSettings`, the settings sync (3.9.2). |
| `PlayerCheck.cs` | `JoinVerdict`, the join check (3.9.1). |
| `Patches/ProcreationPatches.cs` | Prefix, postfix, finalizer on `Procreation.Procreate` (3.5), `BirthState`, `PartnerSource`, the Debug lines (3.12). |
| `Patches/PlayerPatches.cs` | `Player.OnSpawned` postfix: publish the own Farming when the local character spawns (3.4, 3.5). |
| `Patches/ZNetPatches.cs` | `ZNet.OnNewConnection`, `RPC_PeerInfo` and `Update` postfixes (3.5, 3.9). |
| `BirthRule.cs` | The pure rule (3.2): `RuleSettings`, `BirthDecision`, `BirthRule`. |
| `PartnerFinder.cs` | Partner prefab, birth range and nearest partner (3.3). |
| `BirthRecord.cs` | The two parent keys (3.8). |
| `FarmerSkill.cs` | Own Farming read without adding the skill, publish, withdraw, best farmer (3.4). |
| `Compat.cs` | Star Level System detection (3.6). |
| `SelfTests.cs` | Debug builds only (calls vanish in Release): in-world self-tests and the settings override (6.1). |

### 3.15 Knowledge-base and backlog notes for step 5

- `docs/game/farming-cooking.md` §7 and the feature note: the partner count skips pregnant animals, so a pair's
  second pregnancy waits for the first birth (1.5); `procreationOnly` counts a partner without `Procreation` whatever
  its state, and a range of 0 means no limit (1.3); the birth reads the parent level only through
  `m_character.GetLevel()` = `m_level` and does not need the partner nearby (1.4); remote players' skills read 0 (1.9);
  the taming-boost precedent (1.10); egg quality shows in the inventory only when `m_maxQuality > 1`, drag-merge can
  lose it (1.7, plus T16's result); hens need a partner and Asksvin lay eggs whose quality is the star level (wiki).
  The feature note's "SetLevel prefix in a birth context" sketch is replaced by the level swap (decision 9); its "who
  needs the mod: everyone" stays true (server and every player since the user's review, decision 10).
- `docs/game/core-engine.md`: a dedicated server never simulates world objects (its reference position is set far
  outside the world, 1.11; the client `.ref` cannot show it); `spawn`, `tame`, `raiseskill`, `resetskill` are cheat
  commands, so they run only in single player or on the host (1.6; its console section already says so); a respawn or
  reconnection gives the player a new ZDO (1.9); a removed ZDO key never reaches other games (1.12);
  `Skills.GetSkillLevel` adds a missing skill entry (1.9); how a vanilla client reacts to `Error(n)` sent after it got
  in, the vanilla kick delay list, and the first breeding tick of a joining game (1.14).
- `docs/research/idea-research.json`, entry "Breeding revamp": `side` stays `everyone` (the backlog's "Both" is what
  shipped, decision 10; the first draft's plan to change it to `client-only` is dropped). The backlog row already
  says "Both" and links the mod; no regeneration needed for this change.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Pair 0★ + 2★, many births from both parents | About half 0★, half 2★ | 0★, or 1★ with the chance; never 2★ |
| Pair 2★ + 2★ | 2★ | 2★, no roll (at the cap) |
| Pair 1★ + 1★, extra star | 1★ | 2★ with the chance |
| Pair of 4★ animals (another mod) | 4★ | 4★, never lowered, no roll |
| Pair 4★ + 0★ (another mod) | 4★ or 0★ | 0★, or 1★ with the chance |
| Three or more ready animals within the partner range at conception (common with hens) | Partner not identified | The nearest one is the partner (decision 4, to confirm); README "Good to know" |
| Partner killed, sold, moved or harpooned away before birth | n/a | Recorded level used |
| Partner itself pregnant at birth | n/a | Irrelevant with a record; the fallback scan accepts a pregnant partner |
| Pregnancy conceived before install or while the mod was off, partner wandered 3-10 m away | Birth takes own level | Nearest tamed partner within the population range (10 m code default) found at birth (decision 5) |
| Same, no tamed partner of the species within that range at birth | Own level | Own level, with the extra-star chance (Goal item 3's limit, T17) |
| Record left from an older pregnancy (conceived later on a game without the mod) | n/a | Stamp differs: ignored, fallback |
| Vanilla conceived but our scan finds no partner (partner prefab without `BaseAI`, partners added by another mod) | Counted, not stored | No record; the birth fallback decides |
| Partner range 0 or less (set by another mod) | No distance limit | Same (no limit in our scan) |
| Species that breeds alone (`m_noPartnerOffspring`), no partner | Own level | Own level, with the extra-star chance (decision 6) |
| Separate-partner species (`m_seperatePartner`) | Counted, not stored | Nearest ready separate partner recorded |
| `m_minOffspringLevel` above the parents' level | Floor applied | Floor applied before the bonus (can go one above it) |
| Hen pair lays an egg | Egg quality = laying hen's level | Egg quality = rule result; hatchling and hen keep it |
| Egg in an inventory or chest | Quality number shown only if `m_maxQuality > 1` | Unchanged (vanilla); T16 checks it, README note |
| Egg with quality 2 dragged onto a quality-1 egg stack | May merge and lose the level (`IsSameType` when `m_maxQuality <= 1`) | Unchanged (vanilla); pick up or drop instead of dragging |
| Egg stacks on the ground or picked up | Qualities kept apart | Unchanged |
| Baby grows up | Level copied | Unchanged (vanilla) |
| Wild spawns, `spawn` command, tamed wild animals, summons | Their own levels | Untouched (no level patch) |
| Wild boar/wolf/… `Procreate` ticks | Return (not tamed) | Only the throttled skill publish runs |
| `Pet` piece with `Procreation` (no `Character`) | `m_minOffspringLevel` | Left to vanilla |
| You 80 m from the pen, pen still simulated by your game | n/a | Your Farming counts (you are the deciding game's player) |
| You come home and a birth that fell due while you were away happens on the first tick | n/a | Your Farming counts, whatever your distance |
| Friend with the mod at the pen, your game simulates it | n/a | The best of your Farming and the friend's published Farming |
| Friend without the mod at the pen | n/a | Not a farmer; your own Farming still counts |
| Friend just joined, respawned or reconnected | n/a | Published when their character spawns; counted from the next ZDO sync |
| Friend's Farming changed while alive (level gained, status effect) | n/a | The old value counts until their game's next breeding tick of a loaded animal (every 30 s for boars and hens) |
| Your character never farmed (no Farming entry) | Vanilla adds the entry only around farming | Counts as Farming 0 (plus status effects); the mod never adds a Farming entry or Skills-tab row |
| Birth of a recorded pregnancy on a game with the mod, in multiplayer | n/a | Record overwritten with 0 (partner 0, stamp 0): the server and the other games get the cleared value (a removal would not reach them) |
| Friend at the pen but not loaded on the deciding game | n/a | Not counted (1.10) |
| No local player at that moment (respawning) and no other farmer in range | n/a | `ChanceWithoutFarmer` (10%) |
| Farming raised by a status effect | n/a | Counts (effective level, as vanilla's own Farming uses) |
| Farming above 100 (modded) | n/a | Treated as 100 |
| Chance settings 0 or 100 | n/a | Exactly never / always |
| Simulating game without the mod | Vanilla | Such a game is refused by a server with the mod; when allowed (`AllowPlayersWithoutMod`) or the server has the mod off: vanilla birth (hand-off) |
| Another mod skips vanilla `Procreate` (Seasons in winter, a breeding overhaul) | No vanilla birth | Mod stands aside (Debug line only if a birth was due) |
| Another mod changes `m_pregnancyDuration` for one call (Seasons) | Due time changed | Same births as vanilla-with-that-mod, all following the rule (no prediction) |
| BreedingUpgrades / Procreation Plus extra star | Their +1 on vanilla | Their +1 on top of ours (two rolls) |
| Star Level System installed | Its own level rules | Left to Star Level System (no swap, egg counts unchanged); Info line |
| Feature turned off with a pregnant animal | n/a | Vanilla birth; stale record ignored later |
| Exception in vanilla birth code | n/a | Parent level restored by the finalizer |
| Game quit with the mod on | n/a | `OnDeactivated` runs; guarded withdraw skips the write when `ZNet` or the player is gone; no error |
| Player without the mod joins a server with the mod | Plays | Refused about 1 s after getting in: vanilla `Error(3)` ("Incompatible version" in their menu), disconnected 4 s later through vanilla's kick list; Warning with name and platform id in the server log |
| Same, `AllowPlayersWithoutMod` on | Plays | Plays; Warning in the server log; their simulated births are vanilla, they are never farmers |
| `AllowPlayersWithoutMod` switched from on to off with such players online | n/a | Every ready player is checked again: those without the mod are refused about 1 s later |
| Player without the mod leaves before the 1 s deadline | n/a | Nothing (the check finds the peer gone from `ZNet.GetPeers()` or its connection closed) |
| Player leaves and joins again within 1 s | n/a | The new connection is a new peer with its own deadline; the old entry is skipped |
| Server turns the mod off during a player's 1 s wait | n/a | Check cancelled (list cleared); the player stays. Turned on again later: every ready player is checked again |
| Server turns the mod off after a refusal was sent | n/a | The refusal completes (vanilla's kick list disconnects the player) |
| Host's own player; single player | n/a | Never checked (not a peer) |
| Modded client whose copy is off (`Enabled = false`) | n/a | Counted as having the mod (framework handshake still answers): not refused; its simulated births are vanilla while off (open question 7) |
| Modded client with another network version (a future release) | n/a | Framework: its copy stays off (ServerMismatch) and it is still counted as having the mod by `PeerHasMod`: not refused (open question 7) |
| Client connected to a server with the mod, before the settings answer arrives | n/a | Own settings (one round trip after the handshake; no birth can happen before the world has loaded on that game) |
| Server changes several settings in one config save | n/a | One settings message on the next frame; clients log one Info line with the new values |
| Unreadable or out-of-range settings from the server | n/a | Unreadable: refused whole, the previous values (or the own ones) stay, Warning once per session. Out of range: clamped to the config ranges, Warning |
| Client disconnects, then plays single player or hosts | n/a | Own settings at once (the stored values belong to the old `ZNet` session) |
| Client's own settings file differs from the server's | n/a | Ignored while connected (not overwritten); used again offline |

---

## 5. Decisions and open questions

### Decisions

The ones marked **(to confirm with the user)** pick between conflicting requirements, deviate from the literal
request or change what the player sees.

1. **Chance numbers: the Farming line 15% → 50%; "base 10%" only when no farmer is known** (to confirm with the user;
   picks between conflicting requirements). "Base 10% chance" and "15% at 0, 50% at 100" disagree at Farming 0. The
   default reads the parenthetical as the rule: the chance follows the best farmer's Farming, 15% at 0 to 50% at 100,
   and the deciding game's own player always counts (decision 2), so in single player the chance is simply your
   Farming's. The 10% is kept as `ChanceWithoutFarmer`, used only when no farmer is known at all, which is rare in
   normal play. Alternatives for the user:
   - (a) **one straight line 10% → 50%**: set `ChanceAtFarming0 = 10` (no code change);
   - (b) **position-based**: 10% whenever no farmer (including you) is within 60 m of the parent at birth, the Farming
     line otherwise. Concrete effects: a Farming-100 player 61 m away gets 10%, less than a Farming-0 player at the pen
     (15%); in single player 10% only happens between 60 m and the edge of the loaded area (about 96-128 m); a birth
     that fell due while you were away happens on the first tick after you come back into range (30-45 s after the
     pen loads for boars and hens), often while you are still more than 60 m away, so it gets 10% or your Farming depending on how fast
     you walked in. This was the first draft and was set aside for these reasons;
   - (c) 15% → 50% for everyone, no 10% at all (drop `ChanceWithoutFarmer`).
2. **Whose Farming skill: the deciding game's own player always, plus other modded players within range; the best
   counts** (to confirm with the user). Skills are local (1.9) and the game that simulates a pen is often not the
   player standing at it (1.11). The deciding game's own player is read directly, at any distance (it is in the loaded
   area by construction). Each game running the mod publishes its own player's effective Farming level on its own
   player object, so the deciding game can also use the skill of another player at the pen; this copies vanilla's
   taming boost, which reads nearby players' status from their own player objects (1.10). Players without the mod (or
   with it off) do not count. Alternatives set aside: only the deciding game's own player (no network data, but in
   co-op the farmer at the pen is ignored when someone else arrived first); the nearest farmer instead of the best
   (two players at the pen would make it depend on small movements); the player who tamed the animal (vanilla does
   not record it).
3. **Farmer range 60 m, for other players only, capped at 64 m** (to confirm with the user): vanilla's taming-boost
   range, about a base. Your own game's player needs no range. Other players count only while loaded on the deciding
   game (1.10), which one zone (64 m) keeps likely; a larger setting would promise more than the game can see.
4. **The partner is the nearest ready partner at conception** (to confirm with the user), found with vanilla's own
   partner filter, and its level is stored on the pregnant parent with the pregnancy stamp. Vanilla has no notion of
   "the" partner. With a pair it does not matter; with three or more ready animals close together (common with hens)
   the other parent, and so the baby's level, depends on where the animals stand at the fourth love point, which looks
   random to the player. Alternatives: the **lowest**-level ready partner in range (harsher, predictable: a pen never
   does better than its weakest animal), the **highest** (more generous). README "Good to know": keep breeding pairs
   apart from other animals of the same species for a predictable result.
5. **Unknown partner: the nearest tamed partner within the population range at birth, else the parent's own level.**
   Needed so that behaviour 2 holds for pregnancies that started before install, while the mod was off, or on a game
   without it, and for conceptions whose partner our scan could not see. The range is `max(m_partnerCheckRange,
   m_totalCheckRange)` (code defaults 3 and 10 m): the animals roam the pen during the pregnancy, so the 3 m partner
   range alone would often miss the partner and fall back to the vanilla rule. Pregnant or hungry partners count here.
   It follows decision 4's choice (nearest by default; lowest or highest if the user picks that). With no tamed
   partner that close, the parent's own level is the only information left (Goal item 3's stated limit).
6. **A birth without a partner still gets the extra-star chance** (to confirm with the user). With one parent, "the
   lower of the two" is its own level; the chance is the only way up and applies to every birth the mod decides. With
   vanilla animals this only happens in the fallback (hens and Asksvin need a partner per the wiki; which prefabs set
   `m_noPartnerOffspring` is unverified).
7. **Both parents can still become pregnant** (vanilla-consistent): each birth follows the rule, so a mixed pair never
   passes the higher level; suppressing the second pregnancy is a non-goal.
8. **The cap only limits the bonus**: `MaxStars` (default 2) stops the extra star; nothing is ever lowered to it;
   `m_minOffspringLevel` is applied before the bonus.
9. **Level swap instead of a `SetLevel` / `SetQuality` patch** (technical): the parent's `m_level` holds the rule's
   result only during the call, so vanilla (and mods that read the parent's level for the baby, such as BreedingUpgrades'
   transpiled roll) pass it on to a live baby or an egg. No global level patch, so wild spawns cannot be affected (the
   backlog's main risk), no birth-context flag, no transpiler. Restored first among postfixes and again in a finalizer.
   Mods that replace the level code do not see it as the baby's level (Star Level System uses its own cache and uses
   the parent level as an egg count; decision 15).
10. **Side Both, multiplayer Compatible, network version 1** (user's review, 2026-09-29; replaces the first version's
    "Client, Limited"). The first version was client-only: the server never runs the births (1.11), and a `Both` mod
    alone only checks the server, not the other players, so a player without the mod still bred the vanilla way. The
    user asked whether the mod could be server-only or had to be on both sides; the requirement behind it is that the
    vanilla 50/50 rule must never apply. Server-only cannot give that (decision 22), so the mod is required on both
    sides and closes the gap itself: the server refuses players without it (decision 23) and sends its settings to
    everyone (decision 24). Consequences accepted with it: the mod turns itself off on a server (or host) without it
    (framework), so a player with the mod gets vanilla births there; it loads on `valheim_server.exe` (no
    `BepInProcess`); RPC names, payloads and the ZDO keys are now covered by `ModNetworkVersion` (1).
11. **No UI and no message** (to confirm with the user): the result shows as the baby's stars or the egg's "[2]" ground
    hover; the Debug log explains every roll. A "+1" text like vanilla's bonus harvest is open question 2.
12. **Conception is detected in the `Procreate` postfix** (technical), not by patching the one-line private
    `MakePregnant` (Mono may inline it, which would silently drop the patch; AllTameable does patch it, but the postfix
    detection needs no such assumption).
13. **Stand aside when another mod skips vanilla breeding** (`__runOriginal`, prefix at `Priority.Last`): that mod
    owns the call. The Debug line is written only when the skipped call would have been a due birth, so mods that only
    skip conception ticks (FeedLikeGrandma's population limit) do not produce a misleading line.
14. **Identity**: GUID `MC.Farming.Breeding.StarInheritance`, display name "Breeding Star Inheritance" (permanent after
    release; open question 1).
15. **Star Level System: leave births to it** (to confirm with the user). With its default settings it ignores our level
    for live births (its cache), does not use the egg quality for hatchlings, and uses the parent's `m_level` as the egg
    **count**, which our swap would silently change. When its GUID `MidnightsFX.StarLevelSystem` is loaded, this mod
    does not swap levels (the Farming publish still runs, harmless) and says so once in the log; the README lists it as
    not compatible. Alternative: keep swapping and document the egg-count side effect (worse: silent change to another
    mod's feature).
16. **No birth prediction** (technical): the rule is computed and the level swapped on every pregnant tick, and the
    postfix decides from the ZDO whether a birth really happened (pregnant before, not after). A prefix of another mod
    that runs after ours and changes `m_pregnancyDuration` for the call (possible on a priority tie) can then neither
    make us miss a birth nor roll for nothing. Cost: one rule computation per pregnant animal per breeding tick (3.13).
17. **Star Level System is looked up at the first use after activation, not in `OnActivated`** (technical, found while
    implementing). The first activation runs inside this plugin's own `Awake`, and BepInEx adds a plugin to
    `Chainloader.PluginInfos` only when it loads it; with no dependency between the two, "MidnightsFX.StarLevelSystem"
    loads after "MC.Farming...", so a lookup in `OnActivated` would miss it. `OnActivated` resets the lookup; the first
    `Procreate` tick (always in a world, long after every plugin loaded) does it and writes the Info line.
18. **The rule takes the roll as an argument** (technical): `BirthRule` is pure (no Unity call), so the `breeding.rule`
    self-test can check exact edges and run 20000-roll Monte Carlo series; the prefix draws `UnityEngine.Random.value`.
19. **Publish also when the character spawns** (technical, from the code review): publishing only on breeding ticks
    left a friend uncounted for up to one tick after joining, respawning or reconnecting (30-45 s after a pen loads for
    boars and hens), and made M06 depend on timing. A `Player.OnSpawned` postfix (local player, once per spawn, after
    the skills are loaded) publishes at once; the throttled tick stays for changes while alive. Chosen over only
    documenting the delay because it costs one call per spawn and closes the gap (3.4, 3.5).
20. **Read the own Farming without adding the skill** (technical, from the code review): `GetSkillLevel` adds a level-0
    Farming entry to a character that has none (1.9), which would show a "Farming 0" row in the Skills tab of every
    player near any breeding animal, wild boars included, and stay in the character file after uninstalling.
    `FarmerSkill.OwnLevel` calls vanilla `GetSkillLevel` only when the entry exists, else returns what vanilla would
    (0 plus status effects, floored) without adding it (3.4).
21. **Clear the partner record by overwriting it, never by removing the keys** (technical, from the code review): a
    removed ZDO key never reaches the server or the other games (1.12), so it stayed there and in the world save.
    Overwriting with partner 0 and stamp 0 reaches everyone; stamp 0 already means "no record" to `TryRead`. The keys
    stay in the save on every animal that conceived under the mod (3.8), ignored by vanilla.
22. **Server-only rejected** (user's review question, technical answer). Births run in `Procreation.Procreate` on
    the game that owns the parent's ZDO, which is a player's game (a dedicated server owns and instantiates no world
    objects, 1.11); the level reaches the baby inside that call (`SetLevel` on the new creature, `SetQuality` on the
    egg) on that game. A server-only mod would never see the call. It could only repair the result afterwards from the
    ZDO data it receives: find each new baby or egg ZDO, work out its parents after the fact, then rewrite its level
    key or destroy and respawn it (ZDO surgery on objects another game owns and simulates). The owning game would
    keep the stale level on the live object (`Character.m_level` is read from the ZDO only in `Character.Awake`;
    an `ItemDrop` reads its item data when created) until the object reloads, and would overwrite the server's change
    with its own writes; eggs in inventories are not ZDOs at all; the partner record at conception and the farmers'
    skills (which live only on each player's game, 1.9) would be out of reach. So it cannot promise "never the vanilla
    rule" and would fight the owner's writes: rejected.
23. **Refuse players without the mod, after a 1 s grace, with the vanilla `ErrorVersion` message** (user's
    requirement; details technical). Hook: `ZNet.RPC_PeerInfo` postfix, only for ready peers (vanilla already refused
    the others), then a grace timer in a `ZNet.Update` postfix (3.9.1). The grace is only a safety margin, since the
    handshake answer always precedes `PeerInfo` (1.14); the 4 s disconnect delay gives a game on its loading screen
    time to read the message (3.9.1); together they stay below the first breeding tick of a joining game (checked by
    `breeding.network`). The review changed 5 s + 1 s to 1 s + 4 s: with 1 s, one long loading frame could lose the
    message. The refusal is vanilla's own path: `Error(3)` (the client's menu shows
    "Incompatible version", the text players of modded servers already know from version-check mods; "You have been
    kicked from the server." was the alternative and reads like an admin action) and vanilla's
    `PeersToDisconnectAfterKick` list for the disconnect (the vanilla kick mechanism, survives the feature turning off, and
    keeps vanilla's own bookkeeping), instead of a custom timer or skipping `RPC_PeerInfo` in a prefix (which would
    hide the player from the log and fight version-check mods). `AllowPlayersWithoutMod` (server setting, default off)
    lets such players in, with the vanilla births on their games written in its description and in a Warning.
    Switching it back to off, or the server turning the mod on, checks the players already online.
24. **Server settings for everyone** (user's requirement): the five breeding settings of the server (or host) are
    used by every game connected to it, pulled by the client after the handshake and pushed by the server on changes
    (3.9.2). One `string` payload (never an `EndOfStreamException`, 1.14), protocol field, invariant numbers, checked
    and clamped by the client. Values are tied to the `ZNet` session that delivered them, so disconnecting, single
    player and hosting use the own settings without any cleanup hook. `AllowPlayersWithoutMod` is not synced (only the
    server reads it). `Plugin.CurrentSettings()` is the only accessor; the Debug self-test override keeps working on
    top of it.
25. **The join check trusts the framework handshake** (`NetworkGate.PeerHasMod`, as asked; `src/Shared` unchanged): it
    proves the mod is installed, not that it is on or of the same network version (open question 7).
26. **The host is checked like a dedicated server**: a host is the server for its guests, so its guests without the
    mod are refused too; the host's own player is not a peer and is never checked.

Other implementation details that differ from the first version of this design (all technical, no behaviour change
the player sees):

- The finalizer restores the level only when the call threw (`__exception != null`), instead of restoring it a second
  time after every call; on the normal path the first-priority postfix has done it.
- The parent's `m_level` is written only when the rule's result differs from its own level.
- The birth Debug line is written only when the rule ran in the prefix (`Decided`); a prefix error (reported once by
  `PatchGuard`) leaves that birth to vanilla without a misleading line.
- `Withdraw()` writes -1 only when a value is published; a missing key already means "not a farmer".
- The fallback birth range is "no limit" when either vanilla range is 0 or less, like vanilla's own count.
- The stand-aside Debug line is written once per activation (the flag is cleared on every toggle), not once per game
  session.
- Debug strings are built at every conception, birth and publish write, whatever the log level (3.13).
- Debug builds only: `SelfTests.Override` lets the self-tests force the rule settings without writing the config
  file; Release builds do not contain it.

### Added beyond the request

`FarmerRange` as a setting (the range is this design's answer to "whose Farming skill" for other players, decisions
2-3), `MaxStars` as a setting (defaults to the requested cap of 2 stars), `ChanceWithoutFarmer` (keeps the user's
"base 10%" as a setting, decision 1), the partner fallback at birth within the population range (needed for behaviour
2 after install), the skill publishing on player objects (so another player at the pen counts), the stand-aside rule,
the Star Level System detection (decision 15), the Debug lines. From the review: `AllowPlayersWithoutMod` (the
requested server setting), the re-check when it is switched back off, the `server settings` / `own settings` marker
in the birth Debug line.

### Open questions

1. Final display name: "Breeding Star Inheritance", "Star Breeding" or "Selective Breeding"?
2. Show a small "+1 ★" text (like vanilla's bonus-harvest "+N") when the extra star happens? It would show only on
   the screen of the game that simulates the birth.
3. ~~Server-synced settings~~: done in the review (decision 24).
4. Give Farming XP for births, so breeding also trains the skill?
5. Show the recorded partner level or "pregnant" in the animal's hover?
6. If T16 shows eggs have no quality number in the inventory (`m_maxQuality` 1): show the egg's star level in the
   inventory and tooltip, and keep eggs of different levels from merging on drag, as a follow-up idea?
7. **Installed but not running** (decision 25): the framework handshake says only that a player's game has the mod.
   A player who turns it off on their own game (`Enabled = false`, or an error state), or a future release with
   another network version (its copy stays off), is not refused, and the animals their game simulates breed the
   vanilla way meanwhile. Stricter option, all inside this mod: the server requires this mod's own
   `SettingsRequest` (sent only by an active copy, with the network version) within the grace instead of
   `PeerHasMod`, and an active client tells the server when its copy turns off, so the server can refuse it then.
   Cost: a player who toggles the mod off in the MC Mods panel gets disconnected.
8. Join check timing: 1 s grace + 4 s disconnect delay (decision 23, changed from 5 s + 1 s in review). The sum must
   stay below the shortest breeding tick (`breeding.network` fails otherwise); a longer delay makes a lost message even
   rarer on slow machines.

### Unverified names and values

- Per-species prefab values: `m_updateInterval`, `m_pregnancyChance`, `m_pregnancyDuration`, `m_requiredLovePoints`,
  `m_partnerCheckRange`, `m_totalCheckRange`, `m_maxCreatures`, `m_minOffspringLevel`, `m_seperatePartner`,
  `m_noPartnerOffspring`; which prefabs carry `Procreation`; the offspring links (`Boar` → `Boar_piggy`, `Wolf` →
  `Wolf_cub`, `Lox` → `Lox_Calf`, `Hen` → `ChickenEgg` → `Chicken` → `Hen`, `Asksvin` → `AsksvinEgg` →
  `Asksvin_hatchling` → `Asksvin`, `Moose` → `Moose_calf` → `Moose`). The prefab names exist in the 1.0.16 manifest;
  the links and "hens need a partner" / "Asksvin lay eggs, quality = stars" come from the wiki; breeding times (e.g.
  TameCraft's "~30 s interval, ~120 s gestation") are third-party claims. The Debug lines show the real partner range
  and levels. **Checked for `Boar` and `Hen`** by the `breeding.birth` and `breeding.egg` self-test NOTE lines
  (in-world run of 2026-09-29, copied into docs/game/farming-cooking.md §7 and the TESTING.md Setup): `Boar` →
  `Boar_piggy`, partner range 3, population 10 m / 5, pregnancy 60 s, tick 30 s, skip chance 0.33, 3 love points;
  `Hen` → `ChickenEgg` (max quality 1, hatches into a tamed `Chicken`), partner range 4, population 10 m / 10, same
  times; no separate partner, no no-partner offspring, min offspring level 0, fed for 600 s. The other species stay
  unverified.
- `AsksvinEgg` `m_maxQuality` and `EggGrow.m_tamed`. `ChickenEgg` max quality is 1, so by 1.7 an Egg and an Egg[2]
  should look the same in inventories and may merge on drag; T16 confirms it in game before the README wording
  changes.
- `Player.m_hardDeathCooldown` prefab value (the "No skill drain" time used by M06; code default 10 s).
- The pets' `m_name` tokens (`$enemy_boar`, `$enemy_hen`, `$enemy_asksvin`, …: the English strings exist; their use
  as `m_name` is assumed).
- HarmonyX 2.9.0: `__runOriginal` is supported (the name is in `0Harmony.dll`); whether the remaining prefixes still run
  after one returns false (the design works either way).
- Hooks of Procreation Plus, TameCraft, CreatureLevelAndLootControl (no source); MyLittleUI's use of `Procreation`;
  names of server-side simulation mods.
- Join check in game: the vanilla client's reaction to `Error(3)` after it got in (log out, "Incompatible version"
  in the main menu) is read in `ZNet.RPC_Error`, `Game.FixedUpdate` and `FejdStartup.ShowConnectError`, and the
  English text in the installed localization table; not seen in game yet (M07). If the client's socket closes before
  it read the `Error`, its status reads "Disconnected" instead (vanilla `UpdatePeers` checks the socket before it reads
  the queued packets); the 4 s delay (3.9.1) makes that need a single client frame longer than 4 s.
- `m_updateInterval` of breeding species other than `Boar` and `Hen` (more than 5 s needed for decision 23's margin;
  code default 10 s). The `breeding.network` self-test now checks every prefab with `Procreation` against it and lists
  the values in a `NOTE` line; it has not run on this build yet, so the claim "a refused game is gone before its first
  breeding tick" counts as verified only once it passes.
- `new CultureInfo("de-DE")` in the game's Mono runtime (the `breeding.network` self-test notes when it is missing).
- Dedicated server: the mod has not run on `valheim_server.exe` yet (M10). The installed dedicated server was 1.0.15
  on 2026-09-29 (Sleep Through the Day M10).

---

## 6. Tests

The in-game checklist lives next to the code: `src/Farming/Breeding.StarInheritance/TESTING.md`
(`./tools/Get-TestTodo.ps1 -Mod StarInheritance`; smoke test `./tools/Test-Smoke.ps1 -Mod StarInheritance`).
Debug logging on (`./tools/Setup.ps1 -DevBepInExConfig`), `devcommands`.

**Setup** (per test unless stated): a small pen (animals within 3 m of each other, for example a 2 × 2 fence), two
tamed adults of the stated levels (`spawn Boar 1 1` = 0★, `spawn Boar 1 2` = 1★, `spawn Boar 1 3` = 2★, then `tame`,
which tames every untamed tameable creature the game simulates, at any distance, 1.6), food dropped in the pen so they
stay fed, enemies away (alerted animals do not breed). Stars are read from the hover bar, egg levels from the ground
hover ("Egg" = quality 1, "Egg[2]" = 1★, "Egg[3]" = 2★), and every birth from the Debug birth line. Remove or move
babies between births (the population limit stops breeding: 5 boars and piglets, or 10 hens, within 10 m). `skiptime`
finishes a pregnancy or a growth but makes animals hungry: feed again after it. Settings are changed in the config
file or ConfigurationManager (live). `spawn`, `tame`, `raiseskill`, `resetskill` and `skiptime` are cheat commands and
work only in single player or on the host (1.6): in multiplayer the host does every console step.

Single player:

| ID | Covers | Test |
|---|---|---|
| T01 | Goal 1, decision 7 | All three chances 0. Pair 0★ + 2★: at least 4 births, from **both** parents (Debug names the parent level): every baby 0★; Debug shows partner level 1 or 3 `recorded`. |
| T02 | Goal 3 | Default settings, same pair: at least 8 births: no baby is 2★ (vanilla would give about half). |
| T03 | Goal 2 | All chances 100. Pair 0★ + 2★ → every baby 1★. Pair 1★ + 1★ → every baby 2★. |
| T04 | Goal 2, cap | All chances 100. Pair 2★ + 2★ → 2★, Debug "at the cap: no roll". `MaxStars = 1`: pair 1★ + 1★ → 1★. |
| T05 | Goal 4 | `ChanceWithoutFarmer 0`, `ChanceAtFarming0 0`, `ChanceAtFarming100 100`. A 0★ + 0★ pen: `resetskill Farming` → babies 0★; `raiseskill Farming 100` → babies 1★; Debug names you (`own`) with Farming 0 / 100. |
| T06 | Goal 4, decision 1 | Defaults. Farming 0 / 50 / 100 → Debug chance 15% / 32.5% / 50%. Walk about 80 m away while the pen keeps breeding (optional: the pen must stay loaded) → Debug still names you with the same chance. `ChanceAtFarming0 = 10` with Farming 0 → 10% (alternative (a) of decision 1). |
| T07 | Goals 1, 5 | Hens. All chances 100, pair of 0★ hens → eggs "Egg[2]"; hatch one (fire, roof) → 1★ chicken; `skiptime` → 1★ hen. All chances 0, pair 0★ + 2★ hens → eggs "Egg" (quality 1). |
| T08 | Goals 1, 5 (optional) | Same as T07 with an Asksvin pair in the Ashlands: "Asksvin Egg[2]" → 1★ hatchling → 1★ Asksvin. |
| T09 | Goal 1 (partner gone) | All chances 0, pair 2★ + 0★. When Debug shows the 2★ parent conceived, kill or pen away the 0★ partner before the birth → the baby is 0★ (`recorded`). |
| T10 | Goal 3 (pregnancy from before) | All chances 0, pair 0★ + 2★ fed, in the small pen (both stay within about 3 m). `Enabled = false` for about 5 minutes, then `true`: births during the next minutes show `found at birth` (or `recorded` for new pregnancies) and are never 2★. |
| T11 | Goal 2 (never lowered) | `spawn Boar 1 5` twice, `tame`, all chances 100: baby level 5 in Debug (no stars shown by vanilla), not 3, no roll. |
| T12 | Goal 5, scope | A 1★ piglet grows (`skiptime`) into a 1★ tamed boar. Untamed boars from `spawn Boar 3 2` stay 1★, wild spawns around keep their usual levels, and no Debug birth line appears for them. |
| T13 | Goal 6 | Live toggle with a breeding pen: off → no Debug birth lines, and over several births the 0★ + 2★ pair gives 2★ babies again (vanilla); on → Debug lines resume at once; no restart. |
| T14 | Goal 6 | `Enabled = false` + restart: Status "Off (disabled in settings)", no patch applied (smoke log), vanilla breeding. |
| T15 | All | Clean log through T01-T19: no warning or error from the mod; log out to the main menu and quit the game with the mod on and a pen loaded: no error; smoke test passes (loads, patches cleanly, JitCheck clean). |
| T16 | Egg display (1.7) | Pick up an "Egg" and an "Egg[2]" from the ground: they go into two separate stacks. Note whether the slot shows a quality number and the tooltip a quality line (this settles `m_maxQuality` and the README "Good to know" wording). If no number is shown, drag the Egg[2] onto the Egg stack and note whether they merge (vanilla behaviour, README warning). |
| T17 | Goal 3 (stated limit, optional) | All chances 0, pair 0★ + 2★. `Enabled = false` until the pair has bred at least once (a pregnancy started while off), kill the 0★ animal (or move it more than 10 m away), then `Enabled = true`: a birth by the 2★ parent shows partner `none` and a 2★ baby (its own level). If the 0★ one was the pregnant parent, repeat. |
| T18 | All (automated) | Debug build, `./tools/Test-InWorld.ps1`: the in-world self-tests of 6.1 report PASS; the `piglets_with_stars` screenshot shows 1★ and 2★ piglets. |
| T19 | Decision 20 | A new character that never farmed, near wild or tamed boars for 2 minutes without touching a cultivator or scythe: the Skills tab still has no Farming row. `raiseskill Farming 10`: the row appears (vanilla) and a publish line with level 10 follows within about 30 s. |

Multiplayer (host A, client B; both with the mod unless stated; Debug on both; the birth lines appear only on the game
that simulates the pen; every console command on the host A; breeding settings changed on A only, since A's apply to
both games; B gets Farming by `raiseskill` in a single-player world before joining):

| ID | Covers | Test |
|---|---|---|
| M01 | Goal 4, decisions 2-3, 24 | A sets `ChanceWithoutFarmer 0`, `ChanceAtFarming0 0`, `ChanceAtFarming100 100`, `FarmerRange 10` (B's log: the server settings line). Host A sets up a 0★ + 0★ pen (`spawn`, `tame`) and leaves far away (more than 200 m, or through a portal). Client B (a fresh character, Farming 0) arrives first, so B's game simulates the pen, and stays about 20 m from it. A runs `raiseskill Farming 100`, comes back and stands in the pen: babies 1★, B's Debug names A with Farming 100 (`published`) and ends with `server settings`. A steps back to 20-30 m: B's Debug names B (`own`, Farming 0), babies 0★. A sets `FarmerRange 40`: A counts again. |
| M02 | Goal 4, open question 7 | M01 settings; A (Farming 0) simulates the pen from about 20 m; B (Farming 100) in the pen: A's Debug names B. B turns the mod off → B's withdraw line, B stays connected (not refused); A's Debug names only A (`own`, Farming 0), babies 0★. B turns it on again → B's server settings line, B counts again within a few seconds. |
| M03 | Hand-off (owner without the mod), decision 23 | A sets `AllowPlayersWithoutMod = true`; B without the mod joins (Warning on A, not refused) and simulates a 0★ + 2★ pen set up by A, A far away: births follow vanilla (2★ babies appear), no Debug birth line on A; babies look normal to both. |
| M04 | Hand-off (egg), goal 5 | A (`AllowPlayersWithoutMod = true`, chances 100) gets an "Egg[2]" from a 0★ hen pair; gives it to B without the mod; B hatches it at B's base → a 1★ chicken that grows into a 1★ hen; A sees the same stars. |
| M05 | Decision 24 | A with chances 100, B's own file with chances 0: B's log shows the server settings line with 100; births simulated by B get the extra star (`chance 100%`, `server settings`). B then plays single player: its own settings (`chance 0%`, `own settings`), its file unchanged. |
| M06 | Publishing after a respawn (3.4, decision 19) | M01 setup, A (Farming 100) in the pen. A runs `die`, respawns (a bed near the pen helps) and runs `die` again within the "No skill drain" time (a second death inside it keeps the skills), respawns and returns to the pen: each time A's character appears, A's Debug shows a publish line at once (the level after the first death), and B's next birth line names A with that Farming. Reconnection: B (a client with the mod) leaves and rejoins the running server while A keeps simulating the pen: B's publish line appears as soon as B's character appears, and A's next birth line names B when B is the best farmer near the pen. |
| M07 | Goal 7, decision 23 | B without the mod joins A (defaults): about 1 s after getting in (usually on the loading screen), B's game returns to the main menu with "Incompatible version" (the plain disconnection text = `[!]` with the loading time noted); A's log: the framework Warning and the `Refused` Warning with B's name and platform id; no error. |
| M08 | Decision 23 | `AllowPlayersWithoutMod = true`: B without the mod stays (Warning). Back to `false` with B online → B refused about 1 s later; a modded player online stays (Debug `has Breeding Star Inheritance installed: allowed`). |
| M09 | Decision 24 | B connected; A changes two settings in one config save → one `Sent the breeding settings` Debug line on A, one server settings Info line on B with both values; B's next birth uses them. |
| M10 | Side Both | Dedicated server (updated to 1.0.16) with BepInEx and the mod: `[MC:ready]`, `Activated.`, no MC error or warning. `MaxStars = 1` in its config: a modded player's log shows it, a 1★ + 1★ pen simulated by that player gives 1★ (`at the cap`, `server settings`); the pen is built with cheats in single player (or hosted) on a test world that the dedicated server then runs, since cheats never work for a client of a dedicated server. A player without the mod is refused (server log). |
| M11 | 3.9.1 step 5 | A turns the mod off with B (modded) online: B's panel says the server has it off. B rejoins without the mod: not refused while off. A turns it on → B refused about 1 s later. |
| M12 | Decision 10 | B with the mod joins a host or server without it: B's panel "Inactive: the server does not have this mod ..."; vanilla births on B's game; no error; single player afterwards: Active. |
| M13 | 3.9.1 step 3 (optional) | B without the mod closes its game the moment the loading screen appears, two or three times (a race with the 1 s check): each time A's log has either nothing from this mod or the `Refused` line, never an error. Turning the mod off during the 1 s wait (step 5, cancel) is too short to test by hand. |
| M14 | 3.9.2 step 1 (optional) | Password host; B joins with `Enabled = false` and turns it on in its config file at the password prompt: once in, Active, B's server settings line with A's values, births end with `server settings`; no error. |

Cross-mod:

| ID | Covers | Test |
|---|---|---|
| X01 | Sort Chest | A chest with "Egg" and "Egg[2]" stacks (laid, or `spawn ChickenEgg 3 1` and `spawn ChickenEgg 3 2`), Sort (MergeStacks on): the stacks stay separate; dropped eggs keep their ground hover quality. |
| X02 | Creature Kill and Tame Counts | Births, hatchings and growing up add no tame count (its rule); no error from either mod. Both patch `Player.OnSpawned`: after a respawn, this mod's publish line appears and Creature Kill and Tame Counts keeps its counts; no error. |
| X03 | Harpoon Hooks Tames | After a conception line, harpoon the partner and drag it away: the baby uses the recorded partner level (as T09). |
| X04 | Other breeding mods (optional) | With Star Level System: the Info line at the first breeding tick, no Debug birth line from this mod, and a 2★ + 0★ hen pair lays the same egg stacks as with Star Level System alone; no error. With BreedingUpgrades or Procreation Plus: extra stars can come from both mods (README note); no error. |
| X05 | Sleep Through the Day (also `Both`) | Host with both mods. A player with both: both Active. A player with Sleep Through the Day only: refused by this mod (the `Refused` line names Breeding Star Inheritance). A player with this mod only: not refused; Sleep Through the Day's own rule applies. No error. |

### 6.1 In-world self-tests (Debug builds)

`SelfTests.cs` registers five tests with `MC.Shared.SelfTest` in `OnActivated` (unregistered in `OnDeactivated`); the
world probe (`./tools/Test-InWorld.ps1`) runs them in a throwaway single-player world with a fresh character (god
mode) and reads the `[selftest] PASS|FAIL|NOTE|SHOT` lines. Rule settings are forced through `SelfTests.Override`
(the config file is never written) and put back at the end; everything spawned is destroyed at the end.

| Test | What it asserts |
|---|---|
| `breeding.rule` | Pure rule, no world needed: lower parent, no partner = own level, `m_minOffspringLevel` floor before the bonus, cap = `MaxStars` + 1 only stops the bonus (3 + 3 → 3, 5 + 5 → 5, `MaxStars` 0 → never), chance line 15% / 23.75% / 32.5% / 50% at Farming 0 / 25 / 50 / 100, Farming above 100 counts as 100, no farmer → 10% whatever the level, chance 100 always and 0 never wins (rolls 1 and 0), a 1 + 3 pair never gives a level outside 1-2; Monte Carlo, 20000 rolls each (seeded `System.Random`, tolerance ±1.5 points; and Unity's `Random.value`, ±2 points) at Farming 0 / 50 / 100 and with no farmer. |
| `breeding.farmer` | `PublishNow` writes the own effective Farming (`OwnLevel`) on the local player's ZDO; `FindBest` names the local player at 500 m; `Withdraw` writes -1 and the local player still counts; publishing again restores the value; a character without a Farming entry still has none afterwards (the PASS line says "none added" or that the character already has one). |
| `breeding.birth` | Two tamed `Boar` (levels 1 and 3) 6 m in front of the camera; their own `Procreate` ticks are cancelled and the test drives vanilla `Procreate` itself (love points set one short, pregnancy chance -1 so no tick is skipped, pregnancy duration -1 so the next call is due, population limit raised, ranges fixed to the code defaults 3 / 10 m). Every birth checks: conception happened, the partner note holds the mate's level, the birth happened, the parent's level is restored, the note is cleared by overwriting (stamp key present and 0; a missing key fails as "removed instead of cleared"), a new `Boar_piggy` (if another mod makes more than one, a NOTE says so and the extra ones within 10 m of the mother are destroyed). Phases: chance 0 (3 births by the level-3 parent, 2 by the level-1 one) → level 1; chance 100 → level 2; defaults with the character's own Farming (12 births) → only 1 or 2 (distribution in the PASS line); note cleared and partner 6 m away → partner found at birth → 1; note cleared and partner 25 m away → own level 3; 3 + 3 at 100% → 3; 5 + 5 at 100% → 5; parents 1 + 1 with `m_minOffspringLevel` 2 → 2 at 0%, 3 at 100%. Screenshot `piglets_with_stars` (two 1★ piglets and one 2★ piglet in front of the camera, health bars shown). A NOTE line logs the boar's prefab breeding values. |
| `breeding.egg` | The same with two tamed `Hen` (levels 1 and 3): egg (`ChickenEgg`) quality 1 at chance 0, 2 at chance 100, 1 or 2 with the defaults, 3 for a 3 + 3 pair at 100%. A NOTE line logs the hen's breeding values and the egg's `m_maxQuality`, hatch prefab and `m_tamed` (settles T16's question and the unverified egg values). |
| `breeding.network` | Mostly pure (3.9): RPC names `<GUID>.Settings` / `<GUID>.SettingsRequest`; the defaults encode as `1\|15\|50\|10\|60\|2` and decode back unchanged; decimals (12.5 / 49.75 / 0.1 / 33.3 / 1) come back exact, also with the thread culture set to de-DE (skipped with a NOTE if the runtime lacks it); out-of-range values clamp to the config ranges (0-100, 5-64, 0-10) and say so, the edges do not; 15 unreadable messages are refused whole (null, empty, other protocol, 5 or 7 fields, text, NaN, infinities, 1e40, a decimal MaxStars, a comma decimal, blanks); `Pick` gives the server's numbers only when told to. In this single-player world: it is its own server with no peer; `CurrentSettings()` is the own config (source Own); `Receive` of a settings message returns NotAClient and changes nothing; with `Override` set the rule is the override (source Test) and the range stays the effective one; no join check pending. Join-check verdicts: refuse only a ready, connected peer without the mod, not already being kicked, on a server with the setting off; allow with the setting on; fine with the mod; skip otherwise. Every prefab with `Procreation` (from `ZNetScene`, real prefab values) has an `m_updateInterval` longer than grace + disconnect delay (1 s + 4 s); a `NOTE` line lists each species' tick and the shortest one. |

With Star Level System installed, the birth and egg tests report PASS "skipped" (the mod leaves births to it by
design).
