# Creature Morale — design

| | |
|---|---|
| Mod | Creature Morale |
| GUID / project | `MC.Combat.Creatures.Morale` (`src/Combat/Creatures.Morale/`, root namespace `MC.Combat.CreaturesMoraleMod`, package `CreaturesMorale`) |
| Category / scope | Combat / Revamp |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 2. Creature AI runs on whichever game simulates each creature, and a player's progression only exists on that player's own game, so every game needs the mod, turned on; the server refuses players without it, with it turned off or with another network version (`AllowPlayersWithoutMod`, default off) and sends its rules to everyone (section 4). |
| Sheet idea | `Mob AI revamp` |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`), decompiled `assembly_valheim` in `.ref/`; every creature prefab (Character, BaseAI and MonsterAI fields, factions, tokens, boss order, defeat keys), the world spawn table and the weapons' attack noise values dumped at runtime from a real world (2026-09-29); sources of The Mark of Oden 0.4.0, TruePassiveMobs 1.1.0 and FearMe 1.0.1 read on GitHub (research brief, 2026-09-29); HarmonyX 2.9.0 in the installed `BepInEx/core` |
| Status | Implemented (v0.1.0 code), in-game testing. Reworked on 2026-09-30 after the user's first in-game test (two bosses ahead, biome by spawn place, afraid instead of calm: section 3, decisions 25-35; section 10, stage 8). Later the same day: the name plate label (E1) removed, running and routed creatures show the game's own alerted state (G7, decision 36; section 10, stage 9) |

## Goal

The user's expected behaviours, numbered. Each one is in scope and gets at least one test (section 8).

1. **G1 — Weak creatures are afraid of you.** A creature that is weak compared with a player never picks that player
   as a target: it does not chase or attack them, and it does not make them "sensed" (no combat music, and it does not
   stop the player from sleeping or resting). It is **not passive** either: when that player comes within `FearRange`
   (12 m) and it can see or hear them, it runs away until the player is a few metres beyond that range, then calms
   down and goes on with its life. A player who sneaks up unseen and unheard can still close in. No aggressive vanilla
   creature ignores a player because of this mod: it either behaves as in the normal game or is afraid of them.
   Something else can still startle it (fire, another creature); that alert without a target fades within seconds
   while every player near it outclasses it. This also covers the night hunters the game sends into the lower biomes
   after some boss kills (Fulings, Seekers, Charred), unless the server turns that off. Example from the sheet: a
   player who has progressed to the Ashlands is no longer attacked by necks and boars: they run from him. (Sheet:
   "Being fully geared from Ashlands and still being attacked by necks and boars does not make sense", "Weak enemies
   should not try to attack the player". User after the first in-game test, 2026-09-30: "The creatures should not be
   completely passive. Aggressive creatures should become frightened when the player comes close, and fight back if
   the player attacks or if they are cornered. No vanilla aggressive enemy should completely ignore the player from
   this mod.")
2. **G2 — "Weak" follows that player's progression, two bosses ahead, in the biome where the creature spawned.**
   Whether a creature is weak against a player is decided per player by (a) the bosses that player helped kill and (b)
   how many creatures of that kind that player has killed, the model of The Mark of Oden (user, in chat). Gear, food and
   skills do not count. A creature is weak against a player who has helped kill the boss **two bosses after** the boss
   of the creature's biome: Black Forest greydwarfs after Bonemass **and** Moder, Meadows boars after The Elder and
   Bonemass; a biome's elites and pack leaders one boss later still. The biome is **the one the creature spawned in**,
   never weaker than its home biome: skeletons that spawned in the Plains, the Mountains or the Black Forest are judged
   by those biomes, greydwarfs in the Deep North by the Deep North. Two players standing side by side can get different
   treatment from the same creature. (User, 2026-09-30: "The threshold should be 2 bosses ahead (for example,
   greydwarfs from the black forest are less aggressive when Bonemass AND Moder are dead). Make sure that enemies that
   spawn in different biomes are properly considered as the biome they spawn in".)
3. **G3 — Except if provoked or cornered.** An afraid creature fights back when that player **provokes** it (hits it,
   or lands a projectile right next to it) or **corners** it (it tries to run away but the player stays right next to
   it for a few seconds: it cannot get away, or the player keeps up with it). It becomes afraid again some time after
   the last provocation. (Sheet: "except if frighten"; user: "interpret frightened sensibly (provoked by a hit,
   cornered...) and decide"; user, 2026-09-30: "fight back if the player attacks or if they are cornered".)
4. **G4 — A pack routs when its leader falls.** When a pack leader dies, the followers of that pack around it flee for
   a while. Default leaders and followers: a Troll, a Greydwarf Shaman or a Greydwarf Brute leads the Greydwarfs and
   Greylings around it (the user's example). The rout does not depend on who killed the leader. (Sheet: "Groups of
   enemies should enter a flee mode if their leader is taken down (for example, Greydwarfs would run away if a troll,
   shaman, or greydwarf brute is killed)".)
5. **G5 — Multiplayer with one set of rules.** Every player is judged by their own progression, whoever's game
   simulates the creature; the server's rules apply to everyone; the server refuses players without the mod, with it
   turned off (also when they turn it off while playing) or with another network version, unless
   `AllowPlayersWithoutMod` is on (MC house rule for gameplay mods).
6. **G6 — Live toggle.** The mod can be turned on and off live from the MC Mods panel; off = vanilla behaviour at
   once (MC framework rule).
7. **G7 — The game's own look, no extra name plates.** The mod adds nothing to the creatures' name plates. A creature
   that runs from a player (G1) or from its fallen leader (G4) is in the game's own **alerted** state, so every player
   sees the vanilla alert icon (the red "!" above its name) while it runs, and it goes back to the vanilla unalerted
   look when it calms down. A creature that is afraid but not running (the player is far away) looks like any other.
   (User, 2026-09-30, after trying the first builds' label: "Remove the extra nameplates from the creature morale. Use
   the vanilla alerted visual state.")

**Added beyond the request** (small; listed for the user at the pause):

- **E1 Name plate tag — removed on 2026-09-30** (G7, decision 36). The first builds showed a small green "afraid" line
  under the health bar of a creature afraid of you and a blue "routed" line on a routed one (personal setting
  `ShowOnNameplates`). The user asked for no extra name plates and the vanilla alerted state instead; the ID stays
  so older notes keep their meaning.
- **E2 Shaken after a rout.** When the rout ends, the followers stay afraid of every player for 60 s unless someone
  provokes them, so the pack does not come straight back.
- **E3 Afraid creatures are not fooled by sneaking in plain sight.** An afraid creature that can see you gets no
  sneak-attack (backstab) bonus from you (vanilla's own rule for the Passive Mobs world setting; one that is running
  from you is alerted, which vanilla already treats the same way), and crawling next to afraid creatures earns the slow
  Sneak rate, not the full one. Without this, every afraid creature would be a free triple-damage hit and a free Sneak
  trainer. One that cannot see you (behind it, in smoke) still takes the sneak attack.
- **E4 More packs by default.** The user wrote "for example", so the default pack list also covers the other
  leader-and-followers groups of the game: Fulings (Berserker, Shaman), Draugr (Draugr Elite), Cultists, Seekers
  (Seeker Soldier), Charred (Warlock) and the Deep North frozen greydwarfs. All of it is one setting.
- **E5 Progress messages.** When a boss kill raises your standing, a center message says "Weaker creatures now keep
  out of your way" (a few seconds after the kill, once the boss's own death message has faded); when a kill step is
  reached for a kind of creature, a corner message names it. Without them the effect of G2 would be invisible.
  Personal setting, on by default.

**Non-goals** (v1): a gear, damage or "can it hurt you" test (G2 replaces it); creatures fleeing from players who are
not close (beyond `FearRange`) or that they cannot sense; creatures breaking and fleeing at low health in a fight;
packmates joining a fight when one of them is hit; changing how tames fight; changing building attacks; `AnimalAI`
creatures (deer, hares, calves, piglets, seals: they already flee); bosses, boss adds during a boss fight, and raids; a
per-world (global key) progression mode; night or pack-size courage; any new item, piece or asset.

**Later** (ideas for a later version, each with a one-line sketch):

- **Help call.** Hitting one creature also provokes its packmates within 10 m: the victim's owner broadcasts
  `<GUID>.Provoke (position, radius, player id, prefab hash)` like the rout, each game provokes the packmates it
  simulates.
- **Leader courage.** Followers within 20 m of a living leader need one more boss to become afraid (their nerve +1), so
  killing the leader both routs them and lets them be afraid.
- **Break in a fight.** A provoked creature that is far outclassed flees below a health share that grows with the gap
  between standing and nerve (The Mark of Oden's rule), never for hunted animals.
- **Afraid raids.** A server setting that lets raid creatures be afraid of the players who outclass them (same hooks as
  the night hunters, 2.5).
- **Tames leave afraid creatures alone** unless their owner provokes them (a `BaseAI.CanSenseTarget` rule for tames).
- **World progression option** for servers that want shared progress: boss rank from the world's `defeated_*` global
  keys (the boss prefabs' `m_defeatSetGlobalKey`), with a penalty.
- **Ranks for modded creatures** from their spawn biome alone (a creature in no home biome list judged by where it
  spawned, with a health check against bosses), instead of "never afraid".
- **Kill bonuses for every biome.** The standing lists a kind's kill bonus only while it can matter for that kind's
  home biomes and world spawns (2.2); a creature met far above both (a 2-star Greydwarf that spawned in the Mistlands)
  could use it too, at the cost of a bigger player ZDO.
- **Older characters.** Characters whose boss kills predate the game's kill table get their rank from their Forsaken
  powers or carried boss trophies.
- **Kill steps in MC Creature Kill and Tame Counts** (the next step per kind of creature).
- **Buildings.** Afraid creatures also leave buildings alone while an outclassing player is near.

---

## 1. Vanilla behaviour

All references are to `.ref/decompiled/assembly_valheim/<Class>.cs` (1.0.16). Prefab values come from the runtime
dump unless marked otherwise.

### 1.1 Where creature AI runs

- `MonoUpdaters.FixedUpdate` calls `IUpdateAI.UpdateAI(0.05f)` on `BaseAI.Instances` every 0.05 s (20 Hz,
  `MonoUpdaters.cs:44-48`), on every game, for every loaded creature.
- `BaseAI.UpdateAI` (`BaseAI.cs:305-328`): a game that does not own the creature's ZDO only copies the ZDO `alert`
  bool into `m_alerted` and returns false. The owner updates take-off, jump and random-move timers, regeneration
  (`UpdateRegeneration`, every 2 s) and `m_timeSinceHurt`, and returns true. `MonsterAI.UpdateAI` (`MonsterAI.cs:347`)
  returns at once when the base returns false, so **all decisions run on the owner only**.
- Ownership moves between players (`ZDOMan.ReleaseNearbyZDOS`, `ZDOMan.cs:967-992`): the owner releases a creature
  only when it leaves the owner's active area, and another game takes it only when it is outside its owner's active
  area. So a creature chasing a player stays with its owner as long as that owner's player is near; a creature
  spawned with the `spawn` command is owned by the game that spawned it. All of `MonsterAI`'s private state
  (`m_targetCreature` 114, `m_targetStatic` 120, `m_timeSinceAttacking` 122, `m_updateTargetTimer` 126, flee and path
  timers) lives in the instance, stays behind on the old owner (the GameObject is not destroyed) and is not sent; only
  ZDO values travel.
- A dedicated server never simulates creatures (its reference position is outside the world; Breeding design 1.11).

### 1.2 How a creature picks a target

- `MonsterAI.UpdateTarget` (`MonsterAI.cs:233-345`) runs `BaseAI.FindEnemy` every 2 s when a player is within 50 m,
  else every 6 s, not while attacking. A found character **replaces** the target; **null keeps the old target**
  (`:247-252`).
- `BaseAI.FindEnemy` (`BaseAI.cs:1395-1427`): the closest character that is an enemy (`BaseAI.IsEnemy`), alive, not
  `m_aiSkipTarget`, not sleeping, and that **`CanSenseTarget(item)`** accepts. If none and `HuntPlayer()` is true, the
  closest player within 200 m, **with no sense check** (null if that player is in ghost or debug-fly mode).
- `BaseAI.CanSenseTarget(Character)` → `CanSenseTarget(Character, bool passiveAggresive)` → the static overload
  (`BaseAI.cs:717-742`): false when the world has `GlobalKeys.PassiveMobs` (unless `m_passiveAggresive`), else hearing
  (`CanHearTarget`, `:749-773`: within hear range **and** closer than the target's current noise range) or sight
  (`CanSeeTarget`, `:780-821`: view range × the target's stealth factor, view angle while not alerted, a raycast,
  mist). The instance overloads are called only by `FindEnemy` and `AnimalAI.UpdateAI`.
- Noise: a player's noise range decays by 4 m/s to 0 (`Character.UpdateNoise`, `Character.cs:3797-3799`); walking
  sets 15, running 30, crouching 0. A player standing still is therefore soon silent and can only be **seen**.
- For the **kept** target, `UpdateTarget` uses `CanHearTarget` / `CanSeeTarget` directly (`:311-318`), never
  `CanSenseTarget`. So vanilla's Passive Mobs setting stops creatures from picking players but lets a provoked creature
  keep fighting. This mod uses the same split.
- A kept player target is told it is targeted every tick (`Player.OnTargeted`, `MonsterAI.cs:319-322`,
  `Player.cs:6762`), which makes `Player.IsSensed()` true for 1 s (`Player.cs:6806`). `IsSensed` blocks sleeping in a
  bed (`Bed.CheckEnemies`, `Bed.cs:121-129`) and the Rested buff (`Player.UpdateEnvStatusEffects`, `Player.cs:2218`),
  and drives combat music. **So "ignore" must mean "no target", not only "no attack".**
- Building targets: with no creature target, `m_attackPlayerObjects` creatures may pick the closest *priority*
  `StaticTarget` in view (`BaseAI.FindClosestStaticPriorityTarget`, `BaseAI.cs:1316`), and an alerted creature whose
  target is a player may pick a random building within 10 m (`MonsterAI.cs:255-278`).
- Give-up (`MonsterAI.cs:329-344`): after 30 s without sensing the target, or 60 s without attacking (not for a hunter
  chasing a player), the creature un-alerts, drops its targets and waits 5 s before the next search.

### 1.3 Other ways a creature gets a target or an alert

- A hit: `Character.RPC_Damage` (victim's owner) sets the player's attacker flag (1.6), then applies resistances, then
  **removes fire, poison and spirit from the hit** and turns them into damage-over-time effects (`Character.cs:
  2390-2396`) before `ApplyDamage`. `ApplyDamage` fires `m_onDamaged` only when the remaining total is above 0.1
  (`:2436-2442, 2483-2486`) → `MonsterAI.OnDamaged` (`MonsterAI.cs:184-190`): wake up, `SetAlerted(true)`,
  `SetTarget(attacker)`. `SetTarget` (`:192-201`) only takes the attacker when there is **no** current target. So a
  hit that is only fire, poison or spirit never reaches `OnDamaged` with its attacker.
- A projectile impact: every projectile impact, **a hit or a miss**, calls `BaseAI.DoProjectileHitNoise(position,
  m_hitNoise, owner)` (`Projectile.cs:692-695`), which sends `OnNearProjectileHit` to every creature within
  `m_hitNoise` of the impact that is an enemy of the shooter (`BaseAI.cs:1512-1521`). `m_hitNoise` is set by
  `Projectile.Setup` (`:414-417`) to the weapon's attack `m_attackHitNoise` plus the ammo's (`Attack.cs:892-905`).
  Dump: bows and the Arbalest 8 m, the Huntsman bow 4 m, a thrown spear and the Abyssal Harpoon (`SpearChitin`,
  10 pierce) 30 m, ammo 0 (sneak research brief). `MonsterAI.RPC_OnNearProjectileHit` (`:203-223`), on the owner
  and without Passive Mobs, alerts the creature and (unless `m_fleeIfNotAlerted`) targets the shooter. So an arrow
  that hits a deer alerts every enemy of the archer within 8 m of the deer, and a thrown spear every enemy within
  30 m of where it lands.
- Alerts without a target: a burn or poison tick calls `ApplyDamage` with no attacker (`SE_Burning.cs:41-48`), and
  `MonsterAI.OnDamaged(dmg, null)` then alerts the creature without targeting anyone; `SpawnAbility` (`:273-276`) and
  pheromones (`MonsterAI.Alert`, called from `Character.cs:925`) alert creatures; a projectile with no owner alerts
  every creature in range.
  Such a creature stays alerted until vanilla's 30 s give-up.
- Hunters: `BaseAI.SetHuntPlayer` writes ZDO `huntplayer` **on the owner only** (`BaseAI.cs:1483-1491`); a non-owner
  reads it only once, in `BaseAI.Awake` (`:244`). Two sources:
  - world spawns with `m_huntPlayer` (`SpawnSystem.cs:295-297`), which also get `SetDespawnInDay(true)` when they
    spawn only at night (`:321-324`). Dump: the **night hunters** after a boss: `Goblin` (Fuling) in the Meadows,
    Black Forest and Mountain after `defeated_goblinking` (groups of 3), `Seeker` in the five lower biomes and
    `SeekerBrood`/`Tick` in the Mistlands after `defeated_queen` (groups of 3), `Charred_Melee`/`Charred_Archer` in
    the five lower biomes after `defeated_fader` (groups of 1-3); each a 5 % chance every 3000 s of game time per
    spawn zone, at night only (`SpawnSystem.cs:219-230`), so rare but real. Also `JotunWarrior`/`JotunWitch` with
    `jotun_killed` (by day, Deep North; in no home biome list);
  - prefabs with `MonsterAI.m_enableHuntPlayer` (`MonsterAI.cs:164-167`), dump: the bosses, the Aspects,
    `FallenWarrior` and `Hive`.
  `MonsterAI.UpdateAI` alerts a hunter **every tick** (`if (HuntPlayer()) SetAlerted(true)`, `:358-361`).
  `MonsterAI.HuntPlayer` (`:928-943`) is false for an event creature outside an event (`RandEventSystem.InEvent`,
  this game's own event state) and for a day-despawner by day.
- Raid creatures: `SpawnSystem.Spawn` calls `MonsterAI.SetEventCreature` (`SpawnSystem.cs:327`); `IsEventCreature`
  reads ZDO `EventCreature` every 4 s on any game (`MonsterAI.cs:779-787`). The owner clears their hunting when the
  event ends (`:378-381`).

### 1.4 Movement and fleeing

- `MonsterAI.UpdateAI` order (`:347-619`): sleep → hunt alert → `UpdateTarget` → saddle, water, day despawn, event
  despawn → flee branches (crown fear 387, `m_fleeIfNotAlerted` 392, low health 397, lava, fire, no-monster area,
  unreachable target 436) → consume items (`:443`, when not alerted or without a target) → circle → select attack →
  idle or attack. Sight alerts a creature only for its **current target** within `m_alertRange` × the target's
  stealth factor (`:528-533`).
- `BaseAI.Flee(dt, from)` (`BaseAI.cs:556-582`): when `m_fleeInterval` has passed since `m_fleeTargetUpdateTime`, a
  point `m_fleeRange` away from `from` (within ±`m_fleeAngle`, with a path, not water or lava); in between it keeps
  the old `m_fleeTarget`. Then `MoveTo(..., run: IsAlerted())`: it **runs only while alerted**, else walks. Dump:
  `m_fleeRange` 25 and `m_fleeInterval` 2 for every creature except the Charred and Jotun (interval 5) and
  `TrollFrost` (range 300).
- `BaseAI.MoveTo` → `FindPath` (`BaseAI.cs:946-962`) keeps **one** path cache (`m_path`, `m_lastFindPathTarget`,
  `m_lastFindPathTime`): the cached result is reused for 1 s, and for 5 s while the target moved less than 1 m. Two
  different `MoveTo` targets in one frame (vanilla idle movement, then a mod's flee) share that cache and the creature
  follows the wrong path; a creature that switches from chasing to fleeing keeps walking its chase path for up to 1 s.
  **A mod that takes over movement must skip vanilla's movement for that frame, and reset the cache when it starts.**
- Dump: `Boar`, `Neck` and `Hen` have `m_fleeIfNotAlerted` true and `m_alertRange` 6 (they back off from a target
  until it is within 6 m). `Greydwarf` has `m_fleeIfLowHealth` 0 (vanilla greydwarfs fight to the death). `Boar`:
  `m_consumeRange` 1, `m_consumeSearchRange` 10, `m_randomMoveRange` 10, `m_randomMoveInterval` 30: a boar can walk to
  food at a player's feet and stay there.
- Senses (dump): `m_hearRange` is 9999 for most creatures (hearing is then limited by the player's own noise range:
  15 m walking, 30 m running, decaying 4 m/s, 1.2), 20 m for `Boar`, `Neck`, `Hen`, 15 m for `Lox`, `Bjorn`, `Unbjorn`,
  `Moose`, and at most 12 m in a dungeon (`BaseAI.CanHearTarget`, `BaseAI.cs:760-763`). `m_viewRange` is 30 m for most,
  20 m for `Boar`, `Neck`, `Hen`, 25 m for `Lox`, `Bjorn`, `Unbjorn`, `Moose`, 40-50 m for `Wraith`, `Hatchling`, `Gjall`,
  `Serpent`, `Elaking`; the view angle (`m_viewAngle`, 90 for most: a half-space in front) counts only while the
  creature is **not alerted** (`CanSeeTarget`, `BaseAI.cs:806`): an alerted creature sees all around. Run speeds: 4-8
  (`Greydwarf` 6, `Boar` 8, `Troll` 6, `Skeleton` 4, `Draugr` 5).
- Combat music comes only from `Player.RPC_OnTargeted` with `alerted` true (`Player.cs:6779-6790` →
  `MusicMan.ResetCombatTimer`), i.e. from a creature that **targets** the player (1.2); an alerted creature without a
  target starts no music.

### 1.5 Alerting, sleeping, sneak attacks and Sneak XP

- `BaseAI.SetAlerted` (`BaseAI.cs:1554-1580`, overridden by `MonsterAI.SetAlerted` 919, which also resets
  `m_timeSinceSensedTargetCreature`): does nothing when the value does not change or `m_canBeAlerted` is false (dump:
  only `ShadowPerson`, no listed creature); else sets the animator's `alert` bool, the **owner** writes ZDO `alert`, and
  a change to true plays `m_alertedEffects` (the alert sound) **once per change to true** (and the boss message and
  active-boss count for bosses only). Every game runs every `BaseAI` at 20 Hz (`MonoUpdaters.FixedUpdate`); on a
  non-owner `BaseAI.UpdateAI` copies ZDO `alert` into `m_alerted` (`BaseAI.cs:311-314`), so `IsAlerted()` is the owner's
  state everywhere within a tick or so.
- **Plate icons** (`EnemyHud.UpdateHuds`, `EnemyHud.cs:188-207`, per frame on each game): while a non-boss creature's
  plate is in its crosshair time (1.10), the `Alerted` icon (the red "!" above the name) is on when `IsAlerted()`, and
  the `Aware` icon when `HaveTarget()` (ZDO `haveTarget`, written by the owner's `SetTargetInfo`) and not alerted. So
  an alerted creature with no target shows the red icon on every screen, and an unalerted one with no target shows
  neither: nothing for a mod to draw.
- Vanilla takes an alert back only in `MonsterAI.UpdateTarget` (`MonsterAI.cs:330-344`: 30 s without sensing its
  target, or 60 s without attacking; target cleared at the same time), on `Sleep`, `MakeTame`, and a hunter is forced
  alerted every tick (`:359-361`). Other readers of the alert: backstab (below), Sneak XP (below), taming
  (`Tameable.TamingUpdate` pauses; the hover status is `$hud_tamefrightened`, `Tameable.GetStatusString`, `:149-153`),
  `Procreation` (tames only), `RandomIdle` (the alerted idle animation), `AnimalAI`. Combat music and the "sensed"
  state come from targeting, not from the alert (1.2, 1.4).
  `BaseAI.OnDamaged` resets `m_timeSinceHurt` (`:706`).
- Sleepers (`MonsterAI.UpdateSleep`, `MonsterAI.cs:817-858`): wake `m_sleepDelay` after a non-ghost, non-flying
  player comes within `m_wakeupRange` (dump `Draugr_sleeping`: 10 m, delay 0.5 s, view range 30 m), or at once when
  hunting.
- Sneak attack (`Character.RPC_Damage`, victim owner, `Character.cs:2336`): the hit is multiplied by
  `hit.m_backstabBonus` when the victim's AI is **not alerted**, the bonus is above 1, 300 s passed since the last one
  on this victim, and — only with Passive Mobs — the victim **cannot see** the attacker. `HitData` is a fresh object
  per call, so a prefix may change `m_backstabBonus` for that hit only.
- Sneak XP: `Player.OnSneaking` (`Player.cs:6920-6942`), once per second while sneaking, raises Sneak by 1 when
  `BaseAI.InStealthRange(player)` (`BaseAI.cs:1582-1602`: some enemy creature within its view range or 10 m, and none
  of those alerted; sleeping ones count), else by 0.1. Deer are `ForestMonsters` (dump), enemies of players, so they
  count.
- Taming only progresses while the creature is not alerted (`Tameable.TamingUpdate`, `Tameable.cs:397`).

### 1.6 Death and kill credit

- `Character.CheckDeath` runs only in the owner branch of `Character.CustomFixedUpdate` (`Character.cs:845`).
  `Character.OnDeath` (`:2907-3001`) returns at `:2931` on other games; the rest runs **on the owner only**:
  - kill credit (`:2935-2952`): for every connected player whose ZDO flag `"<Attackers hash><player name>"` is set,
    `Game.RPC_RegisterKill` locally or routed to that player, with no tamed check (killing your own tame counts). The
    flag is set in `Character.RPC_Damage` for every hit by a `Player`, **before damage is resolved**
    (`:2283-2292`), so a zero-damage or fully blocked hit sets it too; it is keyed by player **name**, never cleared,
    and saved with the creature. The ZDO int `Attackers` counts them. **Credit = the player landed a hit**, not the
    last hit;
  - `m_onDeath()` (`:2990`), to which `BaseAI.Awake` subscribed `BaseAI.OnDeath` (`BaseAI.cs:228`); neither
    `MonsterAI` nor `AnimalAI` overrides `OnDeath`, so a postfix on `BaseAI.OnDeath` sees every creature death, on
    its owner, with the ZDO and transform still valid; then `ZNetScene.Destroy`.
- `Character.m_lastHit` (`:466`, protected) is set in `ApplyDamage` while health > 0 (`:2453-2456`). Burning, poison,
  falls and drowning build hits without an attacker, so the last hit of a burn kill names nobody; the `Attackers`
  count is the robust "a player took part" signal.

### 1.7 Progression data

- `Game.RPC_RegisterKill(long sender, string enemyName, int bossNumber, int modifiers, int attackers, bool
  cheatsUsed)` (`Game.cs:1072-1101`, registered at 291) runs on **each credited player's own game** and calls
  `PlayerProfile.IncrementStatEnemy(enemyName, …)` (`PlayerProfile.cs:788-790`), which always adds 1 to
  `m_playerStats[0].m_enemyStats[0][enemyName]` (`PlayerProfile.cs:86`, `:41`): the character's lifetime kills per
  creature **name token** (`Character.m_name`, e.g. `$enemy_greydwarf`), cheated or not, in every world. `bossNumber`
  = `Character.BossOrder()` = `m_bossOrder`. Vanilla's own campaign progress is the **highest** boss number seen
  (`:1086-1091`).
- The boss ladder (dump, verified):

  | Order | Prefab | Token | Defeat key |
  |---|---|---|---|
  | 1 | `Eikthyr` | `$enemy_eikthyr` | `defeated_eikthyr` |
  | 2 | `gd_king` | `$enemy_gdking` | `defeated_gdking` |
  | 3 | `Bonemass` | `$enemy_bonemass` | `defeated_bonemass` |
  | 4 | `Dragon` | `$enemy_dragon` | `defeated_dragon` |
  | 5 | `GoblinKing` | `$enemy_goblinking` | `defeated_goblinking` |
  | 6 | `SeekerQueen` | `$enemy_seekerqueen` | `defeated_queen` |
  | 7 | `Fader` | `$enemy_fader` | `defeated_fader` |
  | 8 | `FrozenKing_p3` | `$enemy_frozenking_p3` | `defeated_frozenking_p3` |

  Also `m_boss` but order 0: `FrozenKing` and `FrozenKing_p2` (Kall's first phases, token `$enemy_frozenking`),
  `Hive` and `TheHive`. Hildir's named bosses (`GoblinBruteBros`, `GoblinShaman_Hildir`, `Fenring_Cultist_Hildir`,
  `Skeleton_Hildir`, and their `_nochest` versions) are **not** `m_boss` (keys `BossHildir1..3`).
- Tokens are shared across biomes (dump): every skeleton variant (`Skeleton`, `_NoArcher`, `_Meadows`, `_Swamps`,
  `_Mountains`, `_DeepNorth`, their `_noarcher` versions) is `$enemy_skeleton`; `Greydwarf_Frozen` is
  `$enemy_greydwarf` and `Greydwarf_Shaman_Frozen` `$enemy_greydwarfshaman`; `Draugr_Ranged` is `$enemy_draugr`;
  `GoblinArcher` is `$enemy_goblin`; `Bat_Swamp` is `$enemy_bat`; `Leech_cave` is `$enemy_leech`; the `_sleeping`
  variants share their base token. Kills are counted per token, so these share one count: plain Black Forest kills
  count toward the Deep North reskins.
- Nothing about a player's progression is visible to another game: the player ZDO carries no stats or unique keys,
  and the profile exists only on the player's own game. Vanilla player keys for bosses (`Player.m_addUniqueKeyQueue`,
  `Character.cs:2923-2926`) are queued on whichever game runs `OnDeath`, not per participant (research brief §2.2):
  unusable. Vanilla solves the same "remote player state" problem for the crown with a player ZDO bool
  (`Player.SetCrownMode` 4474 → ZDO `crowned`, read by `MonsterAI.UpdateAI` 387). This mod does the same.

### 1.8 Who must be left alone (signals)

| Case | Signal (verified) |
|---|---|
| Bosses | `Character.IsBoss()` (`m_boss`, `Character.cs:3114`) or faction `Boss` (dump: bosses, Aspects, `Tendril`, `TentaRoot`), or `MonsterAI.m_enableHuntPlayer` (bosses, Aspects, `FallenWarrior`, `Hive`) |
| Tames | `Character.IsTamed()` (`:4346`); summons are faction `Players`/`PlayerSpawned` and tamed |
| Raid creatures | `MonsterAI.IsEventCreature()` (ZDO, every game) |
| Night hunters (world spawns) | ZDO `huntplayer` true, not an event creature, not `m_enableHuntPlayer` |
| Training dummies | faction `TrainingDummy`: `piece_TrainingDummy`, and (dump) `FrostWisp` and `Frysling` |
| Dvergr | faction `Dverger` (dump: every `Dverger*`, `BogWitchKvastur`, `Mistile`): not enemies of players unless aggravated, i.e. unless the player attacked them |
| Animals that only flee | component `AnimalAI` (dump: `Deer`, `Deer_White`, `Hare`, `Boar_piggy`, `Chicken`, `Lox_Calf`, `Wolf_cub`, `Moose_calf`, `Seal`, `Seal_Pup`, `Goblin_Gem`) |

`m_aiSkipTarget` is set only on `piece_TrainingDummy`, `ShadowPerson` and `staff_greenroots_tentaroot` (dump).

### 1.9 Networking facts used

- `ZRoutedRpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, name, …)` (`ZRoutedRpc.cs:120-138`) handles the call locally
  **at once** and routes it through the server to every other ready peer (fire and forget). A peer without the
  handler ignores a ZDO-less routed call silently (`HandleRoutedRPC`, `m_functions.TryGetValue`). The handler is
  invoked **without a try/catch** (`:185-192`): an exception in it reaches the network receive path.
- `ZRoutedRpc.Register` uses `Dictionary.Add` (`ZRoutedRpc.cs:210-243`): registering the same name twice on one
  instance **throws**; there is no unregister. A new `ZRoutedRpc` is created in every `ZNet.Awake` (`ZNet.cs:338`).
- `ZNetView.InvokeRPC(method, …)` reaches the ZDO owner as known locally (`ZNetView.cs:331-334`).
- A removed ZDO key never reaches other games; to clear a value, overwrite it (Breeding design 1.12).
- A ZDO update from another game re-reads every byte array (`ZDO.Deserialize`, `ZDO.cs:985`), so a byte array read
  from a remote player's ZDO is a new object after every sync of that player.
- **Every position change of a ZDO's owner bumps its data revision** (`ZDO.InternalSetPosition`, `ZDO.cs:480-489`),
  and `ZDOMan` sends changed ZDOs, whole, every 0.05 s (`ZDOMan.cs:909-915`). A moving player's ZDO is therefore resent
  with all its extra data up to 20 times per second to the server, which relays it to every peer: data on it must
  stay small (tens of bytes).
- `ZNet.GetTime()` (`ZNet.cs:2802`) is the server-synced clock: a deadline stored as its ticks means the same on
  every game.
- The server knows each peer's character ZDO (`ZNetPeer.m_characterID`, set by `ZNet.RPC_CharacterID`,
  `ZNet.cs:2108-2118`) and holds every player ZDO.
- The MC framework's handshake (`src/Shared/Framework/NetworkGate.cs`, `docs/modding/framework.md` "Who needs to
  install it") tells the server, for each connected player, whether their game has the mod, its network version, and
  whether their copy is **ready** (turned on, dependencies active, started without error: `ModPlugin.ComputeLocalState`,
  `ModPlugin.cs:318-358`, sent in the hello, `:94`). The client tells the server again whenever that changes while
  connected (`<GUID>.HelloState`, `NetworkGate.OnLocalReadyChanged`, `:153-174`). Server-side mod code asks
  `NetworkGate.PeerCompatible(peer)` (has the mod, same network version, not turned off: `:202-203`),
  `NetworkGate.PeerProblem(peer)` (a short reason for logs, or null: `:206-218`), and subscribes to the server-side
  event `NetworkGate.PeerStateChanged(ZNetPeer)` (`:51`, raised by `OnClientState`, `:301-327`). A copy built before
  this check reports no ready state and counts as compatible.

### 1.10 Name plates

- `EnemyHud.UpdateHuds` (`EnemyHud.cs:~160-215`, per frame): a non-boss creature's plate exists only within
  `m_maxShowDistance` (`:54`, `:115`; the HUD is destroyed farther away): 10 in code, but **30 m** in the game's
  `EnemyHud` object (runtime value, logged by `morale.rules`; the MC Encyclopedia mod's notes say the same). It is
  shown only for `m_hoverShowDuration` = 60 s after the crosshair passed over the creature (`m_hoverTimer` starts at
  99999, `:37`, `:58`, `:183-188`). Vanilla rewrites `m_name.text` every frame the plate is shown (`:200`).
- A plate that fails `EnemyHud.TestShow` (farther than `m_maxShowDistance`, or hidden by another mod's `TestShow`
  postfix) is destroyed, one per frame (`:178`, `:252`), and when the creature passes the test again `EnemyHud.ShowHud`
  (`:126-158`) makes a **new** plate (a new `HudData` and `m_gui`), shown only after the crosshair passes over the
  creature again. Anything a mod attached to the old plate is gone with it.
- Since G7 the mod attaches nothing to plates: what a player sees is vanilla's own plate and its alert icon (1.5),
  within these same limits (30 m, 60 s after the crosshair).

### 1.11 Where a creature spawned (biome)

- **Spawn point.** `BaseAI.Awake` (`BaseAI.cs:241-250`) sets `m_spawnPoint` from ZDO `spawnpoint`
  (`ZDOVars.s_spawnPoint`), or from its position when the key is missing, and the owner writes it back. So the point is
  where the creature was first created (a world spawn, a location's `CreatureSpawner`, a `spawn` command, an offspring),
  saved with the creature, the same on every game (a non-owner reads the owner's key at its own `Awake`), and never
  moved afterwards. Vanilla uses it as the centre of idle wandering (`BaseAI.cs:488`). It is `Vector3.zero` only for a
  `BaseAI` without a ZDO.
- **Biome at a point.** `WorldGenerator.GetBiome(Vector3)` → `GetBiome(x, z)` (`WorldGenerator.cs:746-833`) is pure
  world-generation math (x and z only, a few noise calls), available on every game including a dedicated server
  (`WorldGenerator.instance`). Order: the Ashlands region first (its sea too), then **Ocean** where the base height is
  0.02 or less (deep water; shores and shallow water return the land biome), the Deep North, Mountains, Swamp,
  Mistlands, Plains, Black Forest, Meadows. `Heightmap.Biome` is a flags enum (`Meadows` 1, `Swamp` 2, `Mountain` 4,
  `BlackForest` 8, `Plains` 16, `AshLands` 32, `DeepNorth` 64, `Ocean` 256, `Mistlands` 512).
- **Dungeons.** `Location.Awake` (`Location.cs:54-64`) puts a location's interior at the x, z of its **zone centre**, at
  the entrance's height + 5000; `Character.InInterior(point)` = y > 3000 (`Character.cs:4371`);
  `Location.GetZoneLocation(point)` (`Location.cs:154-165`) returns the loaded location of that zone, whose position is
  the entrance. A creature made by a dungeon's spawner therefore has a spawn point up in the sky, above its zone.
- **World spawn table** (dump, `SpawnSystem` lists): several kinds spawn outside their home biome.
  `Greydwarf` also at night in the Meadows after `defeated_eikthyr`, `Greydwarf_Elite` and `Greydwarf_Shaman` in the
  Meadows after `defeated_gdking`; `Skeleton` at night in the Meadows, Swamp, Mountains, Black Forest and Plains after
  `defeated_bonemass`; `Draugr` at night in the Meadows, Mountains, Black Forest and Plains after `defeated_gdking`;
  `Serpent` in the Ocean; the night hunters (1.3) in the lower biomes. `Surtling` has entries for the Swamp and the
  Ashlands, both disabled (`m_enabled` false), so the table spawns no Surtling anywhere. The
  variants `Skeleton_Meadows`, `Skeleton_Mountains`, `Bat_Swamp`, `BlobFrost`, `Leech_cave`, `Ghost` and `Hen` are not
  in the world spawn table (location spawners or tames only, `morale.rules` NOTE).

---

## 2. Design

### 2.1 Overview

For one creature and one player, the mod decides an **attitude**:

| Attitude | Toward | Behaviour |
|---|---|---|
| Hostile | that player | vanilla (the default, and for everything the mod does not cover) |
| Afraid | that player | never picks the player as a target; runs away while the player is close and sensed, else goes on with its life (G1) |
| Provoked | that player | vanilla fight: the player hit it, shot next to it or cornered it (G3) |
| Routed | everyone | runs away from its dead leader, ignoring everything (G4) |
| Shaken | everyone | afraid of every player with a standing until provoked (E2) |

The decision (2.4) reads the same inputs on every game: the creature's prefab, level, spawn point and ZDO keys
(including the hunter and raid flags, read from the ZDO rather than from the instance), the player's published
standing, and the rules. So the creature's owner (AI), the victim's owner (sneak attacks) and the sneaking player's
game (Sneak XP) get the same answer, with two exceptions: the boss-fight exemption (2.8) depends on the bosses each
game has loaded, and a client still waiting for the server's rules treats everyone as hostile (4.4). Whether an afraid
creature is running right now (2.5 piece 2) is the owner's own memory: other games see only its alert and its lack of
a target, which is exactly what their vanilla plates show (G7: the alert icon while it runs, nothing once it calms).

### 2.2 Player standing (G2)

**Boss rank** = the highest `m_bossOrder` among the bosses whose token has at least one kill in the character's
lifetime kill table (`m_playerStats[0].m_enemyStats[0]`), 0..8. The token → order map is built at runtime from the
`ZNetScene` prefabs whose `Character` has `m_boss` and `m_bossOrder > 0` (1.7), never hard-coded.
- One data source: the vanilla profile. The mod saves nothing of its own. A character that killed bosses before the
  mod was installed has its rank at once (as far back as the game's kill table goes, section 9).
- "Helped kill" = vanilla kill credit: the player landed at least one hit on the boss (1.6).
- Highest, not count: killing Moder first gives rank 4, like vanilla's campaign progress (decision 8).

**Kill bonus per kind** = the number of `KillSteps` (default `100, 400`) reached by the character's lifetime kills of
that creature's name token: 100 kills → +1, 400 → +2. The count is the game's own, per token, so reskins share it
(1.7): kills of Black Forest Greydwarfs count for the Deep North's Frozen Greydwarfs, kills of any skeleton for every
skeleton. Kills of your own tames count too (the game's table does not tell them apart), so a lox farm earns lox kill
steps. The limit in 2.3 (`MaxBossesSkippedByKills`) keeps these shared counts from making a far biome afraid early.

**Standing(player, creature)** = boss rank + kill bonus of the creature's token.

**Publishing.** The player's own game computes its standing and writes it into its **own player ZDO** (the crown-mode
pattern), key `MC.Combat.Creatures.Morale.Standing`, a byte array (ZPackage):

```
byte  layout      = 1
byte  bossRank      0..8; 255 = no standing (withdrawn: mod turned off)
byte  n             0..16 entries
n × { int  tokenHash   (m_name.GetStableHashCode())
      byte bonus       1..5 }
```

- **Only bonuses that can still change an outcome are listed**: token T is listed when its bonus is at least 1 and
  some rank r in [MinRank(T), MaxRank(T)] has `bossRank ≥ r − MaxBossesSkippedByKills` and `r + 2 × StarRank >
  bossRank` (a 0-2★ creature of that kind is not yet afraid through the boss rank alone). MinRank and MaxRank are the
  lowest and highest ranks (2.3) of the prefabs in the home biome lists that carry token T, each counted in its **home
  biome** and in the **hardest biome of its world spawns** (the enabled entries of the zone controller's `SpawnSystem`
  lists and of the alt biome lists, read at runtime when the token table is built; 1.11): for `$enemy_greydwarf` 4
  (`Greydwarf`) to 10 (`Greydwarf_Frozen`), for `$enemy_skeleton` 3 to 10, for `$enemy_draugr` 5 to 7 (`Draugr` also
  spawns at night in the Plains), for `$enemy_boar` 3 to 3. Ranks in between cover a creature met above its home biome
  (a Skeleton that spawned in the Mountains, rank 6). Unranked kinds (deer, hares, seagulls, Deep North elites) are
  never listed. Highest bonus first, capped at 16 (the cap is logged once). Size 3 + 5n bytes: typically a few entries,
  at most 83 bytes. The player ZDO is resent on every move (1.9), which is why the list is filtered. Consequences: kill
  bonuses make up for at most 2 stars (the game's maximum); a creature with more stars from a level mod needs the boss
  rank for the stars beyond 2 once the rank alone covers 2 of them (README, stage 7 of section 10). A creature met above
  every rank of its token's range uses its kill bonus only while the token is listed for that range: one placed in a
  harder biome by a nest, a dungeon or a location spawner (not in the world spawn table), by the `spawn` command (a Boar
  spawned in the Mountains) or by another mod.
- The filter needs the prefab → token map (`ZNetScene`), built lazily (7.1); publishing only happens with a local
  player, so `ZNetScene` always exists then.
- Written only when the new bytes differ from the bytes **currently on the player's ZDO** (a respawn or reconnection
  makes a new player ZDO without the key, so it is written again).
- When: the local character spawns (`Player.OnSpawned` postfix), a kill is credited (`Game.RPC_RegisterKill` postfix,
  after vanilla updated the table; this also covers a boss kill), the rules change (server rules received, own config
  saved), `OnActivated`. Not while the rules are pending (4.4): the first publish then follows the server's rules.
- Withdrawn: `OnDeactivated` overwrites it with `[1, 255, 0]` (a removal would not reach other games, 1.9), so every
  other game treats this player as vanilla at once: during the second before the server refuses them, and for as long
  as `AllowPlayersWithoutMod` lets them stay (4.5). Without it, their last standing would keep creatures simulated by
  other games afraid of a player whose own game now runs vanilla AI. It plays no part in the join check.
- Debug builds only: config `Debug.ForceBossRank` (−1 = off) publishes that rank instead, for manual testing; the
  self-tests use an in-memory override instead (7.6).

**Progress messages (E5).** When a publish follows a credited kill (not a spawn, rules change or activation): if the
boss rank went up **and the new rank frightens something** (it reaches the lowest home rank of the rules: 3 with the
defaults, so killing Eikthyr or The Elder says nothing while no creature is afraid of such a player yet), `MessageHud`
center message "Weaker creatures now keep out of your way", **4.5 s later** (the
boss's own `m_deathMessage` is sent by `BaseAI.OnDeath` in the same `Character.OnDeath` call, right after the kill
credit, and a center message replaces the text at once and fades in 4 s: shown at once, ours would never be seen;
every ladder boss has one, dump); else, for each listed or
ranked token whose bonus went up, a top-left message "<localized creature name>: <n> kills. They will be afraid of you
sooner." (for example "Greydwarf: 100 kills. …"). Personal setting `ShowProgressMessages`. The log line "Standing:
boss rank N, kill bonuses …" is written at Info on every change.

**Reading.** `StandingCache.Get(Player)`: per player object, at most once per second, reads the byte array and
compares it byte by byte with the cached copy (the array object changes on every sync, 1.9); decodes only when the
content changed. Missing key, layout ≠ 1, a bad array (logged once per player) or rank 255 = **no standing**: the
player is treated as vanilla (never outclassing, never shaken-afraid). Entries of destroyed `Player` objects are purged
with the `CreatureState` purge (every 30 s); the cache is cleared at world end and on toggle off.

### 2.3 Creature nerve (G2)

A creature's **rank** is the boss rank a player needs before a 0★ creature of it is afraid of them. It comes from three
server settings that say where a creature lives and how strong it is, plus where it actually spawned:

- **Home biome** = the home biome list (server settings `Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`,
  `Mistlands`, `Ashlands`, `DeepNorth`, `Ocean`: prefab names, section 5) that names the creature, keyed by prefab (ZDO
  prefab hash, not token: reskins share tokens but not danger). A prefab in several lists takes the easiest one (the
  log warns). **A prefab in no list is never afraid** (modded creatures, Deep North elites, Hildir's named bosses,
  anything new): it errs toward vanilla.
- **Biome level** = the order of the biome's boss (1.7): Meadows 1 (Eikthyr), Black Forest 2 (The Elder), Swamp 3
  (Bonemass), Mountains 4 (Moder), Plains 5 (Yagluth), Mistlands 6 (The Queen), Ashlands 7 (Fader), Deep North 8
  (Kall). The **Ocean** list has no boss of its own and counts as level 3 (a longship, after Bonemass, is what takes a
  player across the open sea; decision 27). Fixed in code (the ladder of the game), not a setting.
- **Spawn level** = the biome level at the creature's spawn point (1.11): `WorldGenerator.GetBiome` at `m_spawnPoint`
  (the position when first judged if the point is zero); for a point inside a dungeon (y > 3000) the x, z of the
  entrance of that zone's location (`Location.GetZoneLocation`), else the point's own x, z (its zone). Ocean, `None`
  or no `WorldGenerator` yet = no spawn level (0). Computed **once per creature per game** and kept in
  `CreatureState` (the spawn point never changes), never per tick; recomputed only when it could not be read yet.
  `WorldGenerator` is also what the player's own current biome uses (`Player.UpdateBiome`, `EnvMan`). The spawn
  system checks a slightly different biome: the heightmap's (`ZoneSystem.GetGroundData` → `Heightmap.GetBiome`, a blend
  of the biomes at the corners of its terrain tile), which can disagree with `WorldGenerator` within a few tens of metres
  of a border, so a creature spawned right at a border can be judged by the neighbouring biome. Kept on purpose: a
  heightmap exists only near a player, and every game must get the same answer for the same creature (2.1).
- **Biome level used** = the higher of the home level and the spawn level (decision 28): a creature is never weaker
  than its home biome (greydwarfs that spawn at night in the Meadows are still Black Forest greydwarfs; the night hunters are
  still Plains Fulings, Mistlands Seekers and Ashlands Charred), and it is stronger where it spawned in a harder biome
  (skeletons in the Plains, greydwarfs in the Deep North, draugr in the Mountains, surtlings in the Ashlands).
- **Rank** = biome level used + `BossesAhead` (default 2) + `EliteExtraBosses` (default 1) when the creature is in the
  `Elites` list. A rank above 8 can only be reached with kill steps (at most `MaxBossesSkippedByKills` early, default 1), so with
  the defaults Ashlands creatures (rank 9) need Kall and 100 kills of their kind, and the Deep North (rank 10) and the
  Mistlands and Ashlands elites (9 and 10) are never afraid (decision 25).

Default home biome lists and elites (all names checked in the 1.0.16 dump, all `MonsterAI`; **bold** = in `Elites`):

| List | Level | Rank (regular / elite) | Default prefabs |
|---|---|---|---|
| `Meadows` | 1 | 3 / 4 | `Boar, Neck, Greyling, Hen, Skeleton_Meadows, Skeleton_Meadows_noarcher` |
| `BlackForest` | 2 | 4 / 5 | `Greydwarf`, **`Greydwarf_Shaman`**, **`Greydwarf_Elite`**, **`Troll`**, **`Troll_sleeping`**, `Skeleton, Skeleton_NoArcher, Ghost, Ghost_sleeping`, **`Bjorn`**, **`Bjorn_sleeping`** |
| `Swamp` | 3 | 5 / 6 | `Draugr, Draugr_sleeping, Draugr_Ranged, Draugr_Ranged_sleeping`, **`Draugr_Elite`**, **`Draugr_Elite_sleeping`**, `Skeleton_Swamps, Skeleton_Swamps_noarcher, Skeleton_Poison, Blob`, **`BlobElite`**, `Leech, Leech_cave, Surtling, Bat_Swamp`, **`Wraith`**, **`Writhan`**, **`Abomination`** |
| `Mountain` | 4 | 6 / 7 | `Wolf, Ulv, Hatchling, Bat, BlobFrost, Skeleton_Mountains, Skeleton_Mountains_noarcher, Fenring`, **`Fenring_Cultist`**, **`StoneGolem`** |
| `Plains` | 5 | 7 / 8 | `Goblin, GoblinArcher`, **`GoblinShaman`**, **`GoblinBrute`**, `Deathsquito, BlobTar, Lox`, **`Unbjorn`** |
| `Mistlands` | 6 | 8 / 9 | `Seeker, SeekerBrood`, **`SeekerBrute`**, `Tick`, **`Gjall`** |
| `Ashlands` | 7 | 9 / 10 | `Charred_Melee, Charred_Archer, Charred_Twitcher`, **`Charred_Mage`**, `Asksvin, Asksvin_hatchling, Volture, BlobLava`, **`Morgen`**, **`Morgen_NonSleeping`**, **`FallenValkyrie`**, `BonemawSerpent` |
| `DeepNorth` | 8 | 10 / 11 | `Greydwarf_Frozen`, **`Greydwarf_Shaman_Frozen`**, `Skeleton_DeepNorth, BlobMork, BlobMorkMini, Elaking, ElakingLantern, Moose` |
| `Ocean` | 3 | 5 / 6 | `Serpent` |
| none (never afraid) | | | Deep North elites (`Barka, ElakingMole, JotunWarrior, JotunWarriorDualWield, JotunWitch, TrollFrost, ShadowPerson, GoblinDeepNorth`), Hildir's named bosses, `FallenWarrior`, `Ghost_Void`, `Ghost_old`, `Charred_Melee_Dyrnwyn`, boss adds, anything modded |

79 prefabs in the lists (the same 79 creatures the first version ranked), 24 elites: every pack leader of the default
`Packs` (2.7) plus the big creatures of each biome (`Troll`, `Bjorn`, `Abomination`, `Wraith`, `Writhan`, `BlobElite`,
`StoneGolem`, `Unbjorn`, `Gjall`, `Morgen`, `FallenValkyrie`). A name in `Elites` that is in no home biome list is never
afraid (the log warns).

**Examples of the spawn biome** (defaults, 0★):

| Creature | Spawned in | Home | Level used | Rank |
|---|---|---|---|---|
| `Skeleton` | a Black Forest burial chamber (dungeon: its entrance's biome) | Black Forest | 2 | 4 |
| `Skeleton` | the Mountains, at night after Bonemass | Black Forest | 4 | 6 |
| `Skeleton` | the Plains, at night after Bonemass | Black Forest | 5 | 7 |
| `Greydwarf` | the Meadows, at night after Eikthyr | Black Forest | 2 | 4 |
| `Greydwarf` | the Deep North | Black Forest | 8 | 10 (never) |
| `Draugr` | the Plains, at night after The Elder | Swamp | 5 | 7 |
| `Goblin` (night hunter) | the Meadows, after Yagluth | Plains | 5 | 7 |
| `Surtling` | spawned with `spawn Surtling` in the Ashlands | Swamp | 7 | 9 |
| `Serpent` | the Ocean (no spawn level) | Ocean | 3 | 5 |
| `Troll` | spawned with `spawn Troll` in the Meadows | Black Forest | 2 | 5 (elite) |

**Nerve** = rank + (level − 1) × `StarRank` (default 1): each star needs one more boss. A 2★ Greydwarf (level 3) of the
Black Forest needs rank 6. Level = `Character.GetLevel()` (from ZDO `level` on every game).

**Outclasses(player, creature)** = standing ≥ nerve **and** bossRank ≥ rank − `MaxBossesSkippedByKills` (default 1).
Kill steps can therefore make a kind of creature afraid at most one boss before its rank; beyond that they only
make up for stars. Without a kill bonus the second condition always holds (bossRank ≥ nerve ≥ rank).

### 2.4 The decision (`Attitude.Decide`)

Pure function (inputs gathered by `Attitude.Judge(MonsterAI, Player)`), in this order:

```
1. mod not active on this game, or rules pending (4.4)  → Hostile
2. creature exempt (2.8)                                 → Hostile
3. now < RoutUntil                                       → Routed   (for everyone: rout movement is per creature)
4. player has no standing (mod missing or off there)     → Hostile
5. provoked by this player (2.6)                         → Provoked
6. now < RoutUntil + ShakenSeconds                       → Afraid   (shaken, E2)
7. world has PassiveMobs                                 → Hostile  (vanilla already passive; 2.8)
8. creature in no home biome list                        → Hostile
9. Outclasses(player, creature) (2.3)                    → Afraid, else Hostile
```

`now` = `ZNet.GetTime().Ticks`. Everything after step 2 reads ZDO data and rules only, so any game can evaluate it.
Per-creature facts (exemption, prefab hash, home and spawn level, rank, token hash) are cached in `CreatureState`: the
spawn level for the creature's life on this game, the rest for 1 s or until the rules change (2.8, 7.4).

### 2.5 Afraid: the creature runs from the player, never attacks them (G1)

Six pieces, all on the creature's owner (`MonsterAI` creatures only):

1. **Never pick the player.** Postfix on `BaseAI.CanSenseTarget(Character, bool)`: when vanilla said true, the
   instance is a `MonsterAI` whose ZDO this game owns, the target is a `Player`, and `Decide` says Afraid or Routed →
   `__result = false`. Effects, all vanilla:
   - `FindEnemy` skips that player and picks the next closest sensed enemy (another player who is not outclassing,
     a tame, another creature). Unlike nulling `FindEnemy`'s result (FearMe, The Mark of Oden), a strong player's tames
     and weaker friends are still seen.
   - A kept target is sensed through `CanSee`/`CanHearTarget`, so a provoked fight is not affected (1.2).
2. **Run away from a close player (fear).** The `MonsterAI.UpdateAI` prefix's once-per-second check (7.3) picks the
   player the creature is running from (`CreatureState.FearPlayer`, memory of the owner):
   - **Start**: the closest counted player (alive, not ghost, not debug fly) within `FearRange` (default 12 m) toward
     whom `Decide` says Afraid and whom it **senses right now**: vanilla `CanSeeTarget(P) || CanHearTarget(P)` (1.2,
     1.4; not `CanSenseTarget`, which piece 1 turns false). So a player who sneaks up unseen and silent (behind it,
     crouched, in smoke, in the dark with a low stealth factor) can close in, and a walking player within 15 m or a
     running one within 30 m is heard all around (most creatures hear everything the player's noise reaches).
   - **Keep**: the same player stays within `FearRange` + 4 m and was sensed within the last 5 s (while it runs it is
     alerted, so it sees all around, 1.4). A closer qualifying player takes over.
   - **Not while fighting** (decision 29): no fear while its target is a counted player toward whom it is Hostile or
     Provoked, or while it senses such a player within `FearRange`: it fights that player (G2: each player judged
     alone; the weak player next to a strong one is still attacked). Also none while it sleeps, is exempt or routed, or
     the rules are pending.
   - **Fear frames** (the `UpdateAI` prefix, every tick while `FearPlayer` is set, the same frame as a rout, 2.7):
     vanilla `MonsterAI.UpdateAI` is skipped for that frame; the base update runs through the non-virtual call; no
     target (`m_targetCreature`, `m_targetStatic` null, `SetTargetInfo(ZDOID.None)`, `ChargeStop()`); `SetAlerted(true)`
     only when not alerted (it runs; the alert sound plays once when the fear starts, and the vanilla alert icon shows on
     every player's plate, G7), and `CreatureState.FleeAlert` set (the frames hold the alert: they raised it, or kept
     the one it had and cleared the target that was vanilla's reason for it); vanilla `BaseAI.Flee(dt, P's position)`
     (a point `m_fleeRange`, 25 m, away from P, with a path, every `m_fleeInterval`). The first fear frame resets the
     flee and path timers (1.4). Every frame also counts the cornering (2.6). Nothing un-alerts it while it runs
     (vanilla's give-up is in the skipped `MonsterAI.UpdateAI`), so the alert and its icon stay on from the first frame
     to the end of the run: no flicker, one alert sound.
   - **End**: nobody qualifies any more → `FearPlayer` cleared; when P is simply out of range or unsensed, it **calms**
     (`FleeFrames.Calm`, owner only, the same calm as at a rout's end, 2.7): an alert the frames held (`FleeAlert`) is
     taken back with a silent `SetAlerted(false)` when it has no creature target, no live provocation by anybody (its
     ZDO), is not hunting (`HuntPlayer()` after piece 5), senses no player it would fight within `FearRange` and was
     not hurt within 5 s; hurt within 5 s (a burn tick) keeps the flag and the next check tries again; a creature
     target, a provocation or a hunt means the alert is theirs now (flag dropped, vanilla gives it up later). A
     building (static) target does not hold it: vanilla never alerts a creature for a building and attacks buildings
     unalerted, so keeping the run's alert there would only leave its icon up until vanilla's 30 s give-up.
     `m_updateTargetTimer` at most 0.5 s (vanilla looks for other enemies at once). When a player it would fight
     (Hostile or Provoked) is within `FearRange` and sensed as the run ends (its standing or stars changed mid-run, or
     such a player came close as P left), or the run ends by a provocation (2.6), it does not calm: the alert stays for
     the fight (flag dropped, `m_updateTargetTimer` at most 0.5 s so vanilla picks that player at once), with no second
     alert sound and no switch to the aware icon in between. Its sight check (`Fear.SensesFought`, the decision 29
     test) runs only when a run ends. A fear player who dies, turns ghost or flies (debug) ends the run at the next
     frame and the next check decides (a new run, or the calm). An ownership change forgets the run (7.3); the new
     owner decides again at its first check.
   Fleeing creatures have no target, so the player is not "sensed" (bed and Rested work, no combat music, 1.2, 1.4).
3. **Drop the player if already targeted.** When not running (piece 2 found nobody), the once-per-second check: when
   `m_targetCreature` is a `Player` toward whom `Decide` says Afraid, do what vanilla's give-up does
   (`MonsterAI.cs:337-343`): `SetAlerted(false)`, `m_targetCreature = null`, `m_targetStatic = null`,
   `m_timeSinceAttacking = 0`, and `m_updateTargetTimer = 1` (search again soon, for another enemy). This covers a
   target taken before the player outclassed it (a boss kill or kill step mid-fight, when the player is not close or
   not sensed), a provocation that ran out (2.6), an alert by a projectile that was not ours, and a creature taken over
   mid-fight by a new owner. A close, sensed player is run from instead (piece 2 comes first in the check, so the alert
   is not dropped and raised again).
4. **What the afraid creature does otherwise**: whatever vanilla does without a target: wander around its spawn point,
   eat, rest, fight tames or other creatures. No target means no `OnTargeted`: the player is not "sensed" by it and it
   shows no "aware" icon. It may wander back toward a player who is out of range, and runs again once it senses them
   within `FearRange`.
5. **Night hunters** (world spawns with the hunt flag, 1.3), while the server setting `NightHuntersCanBeAfraid` is on
   (default): they follow the home biome lists like any creature (their home biome, 2.3, makes a Fuling in the Meadows
   a Plains Fuling). Two extra hooks cover the hunt paths that skip the sense check:
   - `MonsterAI.HuntPlayer()` postfix (owner): when vanilla said true and the creature's `CreatureState.HuntStandDown`
     is set → false. `HuntStandDown` is computed by the once-per-second check: a night hunter (not a raid creature,
     not `m_enableHuntPlayer`) with no player within 200 m (the hunt range; ghost and debug-fly players ignored)
     toward whom `Decide` says Hostile or Provoked, and whose current target is not such a player (a player it chases
     stays its prey at any distance, as vanilla chases a kept target without sensing it). While it stands down it is
     not forced alert every tick (`MonsterAI.cs:358-361`) and does not fall back to players; it behaves as a normal
     night creature, is afraid of the players who outclass it (piece 2) and still despawns at dawn. A rules change ends
     the stand-down at once, not at the next check: the postfix keeps it only while the rules in force are not pending,
     have `NightHuntersCanBeAfraid` on and leave the creature not exempt (cached facts read again on a rules change, as
     `Judge` does).
   - `BaseAI.FindEnemy()` postfix (owner): when `__result` is a `Player` toward whom `Decide` says Afraid or Routed
     (only possible through the hunt fallback, since piece 1 already skipped them) → the closest player within 200 m
     (not ghost, not debug fly) toward whom it is Hostile or Provoked, or null. So when a weak and a strong player
     stand together, the hunters go for the weak one.
   With `NightHuntersCanBeAfraid` off, night hunters are exempt (2.8) and hunt everyone as in vanilla.
6. **Startled afraid creatures calm down.** When not running, the once-per-second check un-alerts
   (`SetAlerted(false)`, silent) a creature that is alerted with no creature or static target, is not routed, is not
   hunting (`HuntPlayer()` false after piece 5), has not been hurt for 5 s (`m_timeSinceHurt`), and has at least one
   player within max(`m_viewRange`, 10 m), every one of whom it is Afraid of. This covers a burn or poison tick,
   pheromones, a spawn ability or an ownerless projectile (1.3), and a run that another game started before this game
   took the creature over (7.3: the new owner's memory has no `FleeAlert`). The end of a run of this game's own (a fear,
   a rout, E2) is `FleeFrames.Calm`'s (piece 2, 2.7), player near or not. The 5 s wait keeps a burning creature from
   flickering between alerted and calm (each change to alerted replays the alert sound, 1.5). Without players around,
   vanilla's own 30 s give-up is left alone.

**UI (G7, decision 36): the game's own alerted state, no plate patch.** The mod draws nothing and patches no
`EnemyHud` method. A creature that runs (fear frames here, rout frames 2.7) is alerted through vanilla
`BaseAI.SetAlerted` on its owner, which writes ZDO `alert`; every other game copies it into `m_alerted` at its next AI
tick (1.5), and every game's `EnemyHud.UpdateHuds` turns the plate's `Alerted` icon on from `IsAlerted()`, with the
`Aware` icon off (no target, `SetTargetInfo(ZDOID.None)`). When it calms (piece 2's end, 2.7's end), the silent
`SetAlerted(false)` turns the icon off on every screen the same way. A creature that is afraid but not running has no
target and is not alerted (piece 3 drops a target, piece 6 and the calm take back alerts): its plate looks like any
wandering creature's. Players without the mod (4.6) see the same icon, since it is vanilla state. Vanilla's limits
apply (1.10): the plate shows within 30 m and for 60 s after the crosshair passed over the creature. Side effects of
the alert, all vanilla and accepted for a running creature: the alert sound once per run (never re-raised while it
runs); no sneak attack on it (1.5; E3 needs nothing more there); the slow Sneak XP rate while it is near a sneaking
player (vanilla `InStealthRange`: an alerted enemy in range → 0.1, as for any alerted creature); taming pauses and the
hover text shows vanilla's `$hud_tamefrightened` status (decision 33). Not affected, because they come from
targeting: combat music, the player's "sensed" state, beds and Rested (1.2, 1.4).

**Worked outcomes** (default settings, each creature spawned in its home biome, no kill step reached, 0★ unless
stated; "afraid" = never attacks, runs when the player comes within 12 m):

| Player has helped kill up to | Boar, Neck (3) | Greydwarf (4) | 2★ Boar (nerve 5) | Troll (5, elite) | Draugr (5) | Wolf (6) | Fuling (7) | Fuling Berserker (8, elite) | Seeker (8) | Charred Warrior (9) | Frozen Greydwarf (10) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| nothing, Eikthyr (1), The Elder (2) | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks |
| Bonemass (3) | afraid | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks |
| Moder (4) | afraid | afraid | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks | attacks |
| Yagluth (5) | afraid | afraid | afraid | afraid | afraid | attacks | attacks | attacks | attacks | attacks | attacks |
| The Queen (6) | afraid | afraid | afraid | afraid | afraid | afraid | attacks | attacks | attacks | attacks | attacks |
| Fader (7) — "fully geared from Ashlands" | afraid | afraid | afraid | afraid | afraid | afraid | afraid | attacks | attacks | attacks | attacks |
| Kall (8) | afraid | afraid | afraid | afraid | afraid | afraid | afraid | afraid | afraid | attacks (afraid after 100 kills of it) | attacks |

The night hunters (Fulings after Yagluth, Seekers after The Queen, Charred after Fader, sent into the lower biomes at
night) follow their home biome's columns (Fuling, Seeker, Charred Warrior) while `NightHuntersCanBeAfraid` is on: they
come only after their biome's boss died, and are afraid only of players two bosses further. Raids and bosses always
attack (2.8).

**With realistic kill counts** (defaults: 100 kills → +1, 400 → +2, at most one boss early):

| Player | Creature | Standing ≥ nerve? | bossRank ≥ rank − 1? | Result |
|---|---|---|---|---|
| Bonemass (3), 120 Greydwarf kills (+1) | Greydwarf (rank 4) | 4 ≥ 4 | 3 ≥ 3 | afraid, one boss early |
| Bonemass (3), 120 Greydwarf kills | 1★ Greydwarf (nerve 5) | 4 < 5 | | attacks |
| Moder (4), 450 skeleton kills (+2) | Skeleton_Swamps (rank 5) | 6 ≥ 5 | 4 ≥ 4 | afraid, one boss early |
| Moder (4), 450 skeleton kills | Skeleton_Mountains (rank 6) | 6 ≥ 6 | 4 < 5 | attacks (kills bridge one boss at most) |
| The Queen (6), 150 skeleton kills (+1) | a Skeleton that spawned in the Plains (rank 7) | 7 ≥ 7 | 6 ≥ 6 | afraid, one boss early |
| Yagluth (5), 450 Draugr kills (+2) | 2★ Draugr (nerve 7) | 7 ≥ 7 | 5 ≥ 4 | afraid (kills make up for the stars) |
| The Queen (6), lox farm, 120 Lox kills (+1) | Lox (rank 7) | 7 ≥ 7 | 6 ≥ 6 | afraid, one boss early |
| Kall (8), 150 Charred Warrior kills (+1) | Charred Warrior (rank 9) | 9 ≥ 9 | 8 ≥ 8 | afraid |
| Kall (8), 500 Greydwarf kills (+2) | Frozen Greydwarf (rank 10) | 10 ≥ 10 | 8 < 9 | attacks (the Deep North never, with the defaults) |

### 2.6 Fighting back: provoked or cornered (G3)

**Provocation record** in the creature's ZDO, per player, written by its owner (so it survives ownership changes and
every game can read it): key `MC.Combat.Creatures.Morale.Provokers`, a byte array

```
byte  layout = 1
byte  n        0..4
n × { long playerId     (Player.GetPlayerID())
      long untilTicks   (ZNet.GetTime() ticks) }
```

(2 + 16n bytes, at most 66.)
- **Provoked by player P** = the array has an entry for P's id whose `untilTicks` is after `now`. Each player's
  provocation is separate: when two players fight the same creature, each one's hits keep only their own entry fresh,
  and nobody else's action revives an old one.
- A provocation by P sets P's entry to `now + ProvokedSeconds` (default 30 s): expired entries are pruned; with 4
  live entries, the one ending first is dropped. It is **skipped** when P's entry was written less than 1 s ago (at
  most one write per player per second), when the creature is in no home biome list and is neither routed nor shaken
  (it can never be afraid, so the record would be useless), and when P has no standing (Hostile anyway, step 4 of
  2.4).
- **A provocation ends the creature's fear at once** (2.5 piece 2: `FearPlayer` cleared, alert kept): it turns to fight
  instead of running. While P stays Provoked it does not run from anyone near P (decision 29).
- A rout writes `[1, 0]` (the rout wipes the anger).
- Read in place from the ZDO's array without allocation (a scan of at most 4 entries).
- Vanilla's attacker flags (1.6) are **not** used: they are set by zero-damage hits too, never cleared, saved with the
  creature and keyed by name. They still decide kill credit and the rout trigger (2.7), as in vanilla.

**What provokes** (owner side, `MonsterAI` creatures that are not exempt, attacker is a `Player`):

| Event | Hook | Note |
|---|---|---|
| A hit that carries damage | `Character.RPC_Damage(long, HitData)` prefix (the same prefix as the sneak-attack rule below, after it) | condition: `hit.GetTotalDamage() > 0` before resistances, **any damage type** (fire, poison and spirit included, which vanilla strips before `OnDamaged`, 1.3), blocked hits included. A hit whose damage resistances or armor absorb (0.1 or less left, for example a Tower Shield Wall bash in NG+) provokes too. Then, if the creature has no target and the world does not have Passive Mobs (2.8: there, who a hit makes a creature target is vanilla's own), vanilla-like `SetTarget(attacker)` (vanilla does the same for damaging hits; this adds it for a pure fire or poison hit and for an absorbed hit). The provocation record is written in a Passive Mobs world too: a shaken follower must fight back. The prefix does not alert the creature: vanilla checks the alert for this hit's sneak attack after the prefix (1.5), so an alert here would cancel it. Vanilla alerts it when the hit reaches `OnDamaged`, or at its next AI tick when it sees its new target within its alert range (1.4). Every hit refreshes P's entry. The Abyssal Harpoon deals 10 pierce: harpooning a wild creature provokes it like any hit. |
| A projectile of P lands close | `MonsterAI.RPC_OnNearProjectileHit(long, Vector3, float, ZDOID)` **prefix** | only when vanilla would act (owner, no Passive Mobs) and the shooter resolves like vanilla (`ZNetScene.FindInstance(attackerID)`) to a `Player`. Impact within `NearMissRange` (default 4 m) of the creature → provoke (P) and let vanilla run (it alerts and, unless `m_fleeIfNotAlerted`, targets P; boars, necks and hens pick P at the next search because they are provoked). Farther, and the creature is Afraid of P → return false: the afraid creature ignores that impact (vanilla would alert every enemy within the weapon's hit noise: 8 m for most bows, 30 m for a thrown spear or harpoon, 1.3). Otherwise vanilla. Decision 5. |
| Cornered: while the creature **runs from P** (2.5 piece 2, `FearPlayer` = P), P stays within `CorneredRange` (default 3 m) of it for `CorneredSeconds` (default 2 s) in a row | every fear frame (the `UpdateAI` prefix, owner, 20 Hz): one squared-distance compare and a time sum | This is "it cannot get away" (no path, a wall, a pen, water behind it) or "the player keeps up with it" (a player chasing a creature that runs no faster than them). It needs the fear: a creature that has not sensed P never runs, so a player who sneaks up unseen, stands in smoke or waits next to it while it eats is never cornering it; a creature that walks up to a standing player runs as soon as it senses them, and the player who does not follow never corners it. On success: provoke (P) (which ends the fear), then vanilla-like `SetTarget(P)` and `SetAlerted(true)`: it turns and strikes; vanilla runs from that same frame. The time is in memory (reset when P steps out of the range, when the fear ends or switches to another player, and by an ownership change, 7.3). Off when `CorneredRange` = 0. Decision 30. |

**End of the fight.** While P keeps hitting it, the fight is vanilla. `ProvokedSeconds` after P's last provocation,
`Decide` says Afraid again: at the next once-per-second check it runs from P if P is close and sensed (2.5 piece 2),
else it drops P (piece 3), un-alerts and goes back to its life. Vanilla's own give-up (30 s unsensed / 60 s without
attacking) still applies earlier if it happens.

**Sneak attacks (E3), re-checked with the fear.** In the `Character.RPC_Damage` prefix, **before** the provocation
above (so it uses the attitude before this hit): when the victim is a `MonsterAI` creature, the attacker a `Player`,
`hit.m_backstabBonus > 1`, the victim not alerted, `Decide` says Afraid, and the victim `CanSeeTarget(attacker)` →
`hit.m_backstabBonus = 1` for this hit. This is exactly the vanilla Passive Mobs rule (`Character.cs:2336`): an afraid
creature that watches you walk up is aware of you and not surprised; one that cannot see you (behind it, in smoke, far
away while you sneak) still takes the sneak attack. A creature running from you is alerted, and vanilla never gives a
sneak attack on an alerted creature, so a frightened creature that has seen you is never a sneak-attack target, whether
it is still running or turned around (the fear keeps it alerted until it calms, 2.5 piece 2). The hit then provokes it
as usual. **Known limit, smaller than before:** walking up behind an afraid creature without sneaking now makes it run
as soon as it hears you (walking noise 15 m, most creatures hear all around, 1.4), so the free sneak attack from an
unseen, noisy approach left by the first version is gone for creatures that hear; it remains for the few with a short
`m_hearRange` (boars, necks, hens 20 m; lox, bears, moose 15 m) approached silently from behind. Hearing still cannot
be added to the E3 test itself: the attack makes noise before the hit lands (`Attack` adds `m_attackStartNoise`, 10 m
by default, at the start of the swing), so `CanHearTarget` would deny every sneak attack on an afraid creature,
crouched ones included; requiring a crouch would break the agreed Sneak Ambush contract (a hit from inside the smoke is
a sneak attack, 6.1).

**Sneak XP (E3).** Postfix on `BaseAI.InStealthRange(Character me)` on the sneaking player's own game: when vanilla
returned true, it returns true only if at least one creature it counted (enemy of `me`, within `m_viewRange` or 10 m;
none of them is alerted, or vanilla would have returned false) is **not** Afraid of `me`. Afraid creatures alone
give the slow 0.1 rate, as if nobody who cared were near. Called once per second while sneaking. Debug builds log
"Sneak: only afraid creatures near, slow rate" at most once every 10 s when the postfix turns the result to false
(T14). A creature running from the player is alerted, so vanilla already gives the slow rate near it.

### 2.7 Leader down: the rout (G4)

**Packs** (server setting `Packs`): `;`-separated packs, each `leaders>followers`, prefab names, `,`-separated. A
prefab may lead several packs (followers are merged). Default (E4; every name checked in the dump):

```
Troll, Troll_sleeping, Greydwarf_Elite, Greydwarf_Shaman > Greydwarf, Greyling;
Greydwarf_Shaman_Frozen > Greydwarf_Frozen;
GoblinBrute, GoblinShaman > Goblin, GoblinArcher;
Draugr_Elite, Draugr_Elite_sleeping > Draugr, Draugr_sleeping, Draugr_Ranged, Draugr_Ranged_sleeping;
Fenring_Cultist > Fenring, Ulv;
SeekerBrute > Seeker, SeekerBrood;
Charred_Mage > Charred_Melee, Charred_Archer, Charred_Twitcher
```

The first line is the user's example (Greydwarf Brute = `Greydwarf_Elite`, token `$enemy_greydwarfbrute`). The
Frost Troll (`TrollFrost`, a 3000 HP Deep North spawner creature with a death animation) and Hildir's named Fulings
lead nobody.

**Trigger** (postfix on `BaseAI.OnDeath()`, which runs on the dying creature's owner only, 1.6):
- the creature's ZDO prefab hash is a leader;
- it is not exempt (tamed, raid creature, …, 2.8);
- a player or a tame took part: ZDO int `Attackers` > 0 (a player hit it at some point, which also covers a burn
  finishing it), or the last hit's attacker (`m_lastHit.GetAttacker()`) is a player or tamed. A leader that drowns,
  falls or is killed by other creatures alone does not rout its pack (decision 10);
- guarded against a second call for the same creature (`m_nview.IsValid()`, and a small set of handled ZDOIDs cleared
  after 10 s).

**Delivery.** The rout must reach the followers' owners, which can be other games, and a follower near a zone border
may not even be loaded on the leader's owner. So:
- in single player (no peers) → `Rout.Apply` runs directly;
- in multiplayer → broadcast the routed RPC `MC.Combat.Creatures.Morale.Rout` to `ZRoutedRpc.Everybody` with
  `(Vector3 deathPosition, int leaderPrefabHash)` (16 bytes; a leader death is rare). Every game, including the
  sender (handled at once, 1.9), runs `Rout.Apply` for the creatures **it owns**. No per-creature registration, it
  works whoever owns what, and vanilla games ignore it.
- **Recent routs.** Every game that runs `Apply` (sender or receiver) also keeps the rout (position, leader hash,
  `RoutUntil`) in a short list for `RoutSeconds`. The once-per-second check of a creature this game owns looks at
  that list (usually empty): a matching follower within `RoutRadius` whose `RoutUntil` is older than the rout's gets
  it applied. This catches a follower whose ownership moved while the broadcast was in flight (the old owner no longer
  owned it, the new one did not yet). What remains: a game that joined after the broadcast.
- The handler and `Apply` catch their own exceptions (`PatchGuard.Report`), as the routed-RPC call is not guarded
  (1.9). Debug builds log "Rout: <leader> died, applied to N creature(s) here" on the sender and "Rout from another
  game applied to N creature(s)" on a receiver that owns followers (M02).

**Apply** (on each game, for each creature it owns): `MonsterAI`, alive, prefab hash among the leader's followers,
within `RoutRadius` of the death position, not exempt, not sleeping (it did not see it). The loaded followers in range
that sleep (any owner; the sleep state reaches every game by RPC) are kept with the recent rout, and the recent-rout
check never routs them, also once they wake while the rout lasts. Write in its ZDO:
- `MC.Combat.Creatures.Morale.RoutUntil` (long ticks) = `now + RoutSeconds` (default 15 s; a later rout extends it);
- `MC.Combat.Creatures.Morale.RoutFrom` (Vector3) = the death position;
- `Provokers` = `[1, 0]` (the rout wipes the anger).
And in memory: `state.RoutEnd` at once, and the creature's `m_fleeTargetUpdateTime` and `m_lastFindPathTime` set to
−999, so the first `Flee` picks a fresh flee point and path at once instead of walking the old chase path for up to
1 s (1.4).
The rout does **not** depend on who killed the leader or on anyone's standing (decision 9): kill a Troll with your
first club and its greydwarfs flee.

**Rout frames** (the `MonsterAI.UpdateAI` prefix, owner, every tick while routed; the rout end time is cached in
`CreatureState` as a local `Time.time` deadline, refreshed from the ZDO by the once-per-second check and set at once
by `Apply`). The fear frames of 2.5 piece 2 are the same frame with the fleeing player's position instead of
`RoutFrom` (one shared `FleeFrames.Run`); a rout comes first (it also ends a fear):
1. on the first flee frame after the creature was gained by this game (7.3), or after a rout or fear started, reset
   the flee and path timers as in `Apply`;
2. run the **base** update (`BaseAI.UpdateAI`) through a non-virtual call (`AccessTools.MethodDelegate<Func<BaseAI,
   float, bool>>(…, virtualCall: false)`), so regeneration, timers and other mods' patches on `BaseAI.UpdateAI` still
   run; its result becomes `__result`;
3. `m_targetCreature = null`, `m_targetStatic = null`, `SetTargetInfo(ZDOID.None)`, `ChargeStop()`;
4. `SetAlerted(true)` if not alerted (it **runs**; the alert sound plays once, the vanilla alert icon shows on every
   player's plate, G7), and `FleeAlert` set (2.5 piece 2);
5. `Flee(dt, RoutFrom)`;
6. return false: vanilla `MonsterAI.UpdateAI` is skipped **for this frame only** (its movement would fight ours for
   the one path cache, 1.4).
An exception in steps 3-5 is caught: the prefix still returns false with the base result (so the base update never
runs twice in one frame), and the creature is marked `FramesBroken`: it gets no more rout or fear frames (vanilla from
the next tick) until it is unloaded; its once-per-second check skips the fear and still drops a player it is afraid of
as its target (7.3). An exception before the base call returns true (vanilla runs).
A hit during the rout still provokes (2.6) but the creature keeps running; the provocation matters only if it is
still valid when the rout ends.

**End.** When `RoutUntil` has passed, the prefix stops skipping. For `ShakenSeconds` (default 60 s) more, `Decide`
says Afraid of every player with a standing unless provoked (E2): at the next once-per-second check (within 1 s) the
followers run on from any such player who is within `FearRange` and whom they sense (2.5 piece 2: the alert simply
stays, no second alert sound), and the others calm (`FleeFrames.Calm`, 2.5 piece 2's end: the rout's alert is taken
back, player near or not, unless a creature target, a provocation by a hit during the rout, a hunt or a hurt within
5 s holds it). A follower that has a player it will fight within `FearRange` and senses them (`ShakenSeconds` = 0, or
a player without the mod let in by `AllowPlayersWithoutMod`) keeps the rout's alert for that fight instead, with `m_updateTargetTimer` at most 0.5 s: the rout frames froze vanilla's target timer, and without
this the calm could come before vanilla's next search, so the follower would be un-alerted and then alerted again by
vanilla (a second alert sound, the icon going to "aware" and back). A follower that picks a building in the second
before the calm (vanilla's target search runs again once the rout frames stop) is calmed all the same (2.5 piece 2's
end). The calm comes at the check, not at the rout's last frame, so that a follower that goes straight on running from
a close player never drops and re-raises its alert. After the shaken time, the normal rules.

**UI (G7).** The vanilla alert icon on every player's plate while it runs (2.5 UI), none once it calms; a shaken
follower that runs from a player shows it again while it runs.

### 2.8 Exempt creatures (never afraid, never routed, never provoked-by-the-mod)

`Exempt(ai)` (cached per creature for 1 s): not a `MonsterAI`; `IsTamed()`; `IsBoss()`, faction `Boss` or
`m_enableHuntPlayer` (bosses, Aspects, `FallenWarrior`, `Hive`); faction `TrainingDummy` or `Dverger`;
`IsEventCreature()` (raids, read from the ZDO on every game); a night hunter (ZDO `huntplayer` read directly, 1.3)
while `NightHuntersCanBeAfraid` is off; within **60 m of an alerted boss** (a boss fight: adds and nearby creatures behave as
vanilla; `BossFights` keeps the positions of loaded `IsBoss()` creatures that are alerted, refreshed once per second
per game, alert read from the ZDO on non-owners). `MonsterAI.HuntPlayer()` is **not** used for exemption: on a
non-owner its flag is stale, and it depends on the time of day and on this game's event state (1.3).

World modifier **Passive Mobs**: vanilla already stops every creature from picking players (1.2) and spawns no
hunters, so the afraid and cornered parts stand down (step 7 of 2.4: nothing is afraid, nothing runs) and provocation
by hits is vanilla's own. The rout and the shaken period still apply (shaken followers are afraid and run, 2.7, but
are never cornered there, as in the first version). The
sneak-attack and Sneak XP rules only concern afraid creatures, which in such a world are only shaken ones (and vanilla
already applies the same sight rule to sneak attacks there).

### 2.9 Edge cases

- **Several players.** Each is judged alone: greydwarfs are afraid of the Moder-killer and attack the newcomer next
  to them. A creature that fights, or senses within `FearRange`, a player it is hostile to or provoked by does not run
  from the players it fears (decision 29): the newcomer standing next to the strong player is still attacked, and the
  strong player is still never targeted. When the newcomer leaves, the creature runs from the strong player again. The
  newcomer's hits provoke only toward the newcomer; each player's provocation runs out on its own (2.6).
- **Standing changes mid-fight** (a boss kill credited, a kill step reached, rules changed): the next once-per-second
  check turns the fight into a flight from that player if they are close and sensed, else drops them, unless provoked
  (a player who hit it keeps the fight for 30 s).
- **Hit by someone else while running.** A creature running from P ignores everything else until the fear ends, like
  a rout (its frames skip vanilla), except a provocation, which ends the fear at once (2.6). A hostile-toward player
  who comes within `FearRange` ends the fear at the next check (decision 29). A tame that bites it is ignored while it
  runs, then fought as in vanilla.
- **Collateral.** An afraid creature fighting a tame next to the player can still hit the player with a swing (vanilla
  melee hits every enemy in its arc). It is not attacking the player; the player's return hit provokes it. When the
  owner comes within `FearRange` and it senses them, it leaves the tame and runs.
- **Tames.** An afraid creature still fights the player's tames when the player is not close (vanilla), and tames
  still attack afraid creatures.
- **Taming** is as in the normal game (decision 33): a wild tameable creature (Boar, Wolf, Lox, Asksvin) runs from a
  player it is afraid of who comes within `FearRange` and whom it senses, and taming pauses while it is alerted
  (`Tameable.TamingUpdate`, vanilla "frightened"); drop the food and step back, or sneak. It never attacks the tamer
  unless provoked or cornered.
- **Harpooning a tame** near afraid wild creatures: the harpoon's impact provokes only afraid creatures within
  `NearMissRange` of it.
- **Hunting** afraid boars, necks and deer-like prey: they run when they see or hear you within 12 m, like deer. Sneak
  up, shoot them, or chase and corner them; the first hit is not a sneak attack if it watches you (E3).
- **Buildings.** An afraid creature may still pick a priority building target like vanilla (decision 11) when no player
  it fears is close.
- **Creatures that cannot run** (a leech in its pond, a serpent near the shore, a creature in a pen or a dungeon
  corner): the fear frames call vanilla `Flee`, which stands still when it finds no path; a player who stays within
  `CorneredRange` then corners it and it fights (2.6), one who keeps a few metres away does not.
- **Leader and follower in one** (none by default): the rout never chains; a routed leader's death routs its own pack.
- **A second leader alive nearby** does not stop the rout (Later: leader courage).
- **Leader killed by a trap, fire or another creature after a player hit it**: `Attackers` > 0, so it routs.
- **Creatures with many levels** from other mods: each level adds `StarRank`; very high levels are never afraid (set
  `StarRank` to 0 to ignore levels).
- **Creatures spawned with the `spawn` command** count as spawned where they appear: a Troll spawned in the Meadows is
  still a Black Forest Troll (its home biome), a Greydwarf spawned in the Mountains is a Mountain one (2.3).
- **Player without the mod** (server allows it): no standing → vanilla toward them, shaken creatures included; their
  hits provoke nothing on modded owners (they are hostile anyway).
- **Spoofing.** A player's standing is written by their own game; a modified client could claim rank 8. Accepted, as
  for vanilla's own player-authored ZDO data.

---

## 3. Decisions

Flags (**FLAG**) mark choices the user should confirm: those that deviate from the literal words or pick a
vanilla-consistent option over them.

1. **Progression, not gear (G2).** Options: gear test (the sheet's "fully geared" wording, the earlier backlog
   approach: the creature's best hit against the player's armor and max HP); progression (boss kills + kind kills,
   The Mark of Oden); both. Choice: progression only. Reason: the user asked for it in chat. **FLAG:** the sheet
   example speaks of gear; `docs/backlog.md` and `docs/game/combat.md` still describe the gear test and must be
   updated in the documentation step.
2. **Rank table** (2.3). First version: one list of prefabs per boss (a biome's rank-and-file after its boss, elites one
   or two bosses later). **Replaced on 2026-09-30** by home biome lists + `BossesAhead` + `Elites` (decisions 25-27).
   Unlisted prefabs are still never afraid. Option kept for later: MoO's 0..8 tiers + a heuristic for unknown prefabs.
3. **Kill steps 100 → +1, 400 → +2, per name token, at most one boss early** (`MaxBossesSkippedByKills` 1). Options:
   50/200 with no limit (the first draft: tokens are shared across biomes, so a typical player's hundreds of
   greydwarf and skeleton kills made the Deep North reskins afraid at The Queen, and most biomes one or two bosses
   early); MoO's 25/100/400; a "home kind" rule (only the lowest-ranked prefab of a token gets the bonus: it breaks
   skeletons, whose lowest prefab is `Skeleton_Meadows`, leaving Black Forest skeletons without one); none. Reason:
   kill counts matter as the user asked (a kind becomes afraid one boss early, stars are offset), without letting
   shared counts reach far biomes. Kept as it was with the 2026-09-30 threshold (decision 25). **FLAG:** balance; kills
   of a shared kind (all skeletons, greydwarfs and frozen greydwarfs) and of your own tames (a lox farm) count; with
   two bosses ahead, 100 kills of a kind are what lets a Kall-killer frighten the Ashlands' rank-and-file (rank 9).
4. **"Frightened" is the fear of the outclassed creature; "fighting back" = provoked or cornered.** First version:
   "frightened" meant provoked or cornered, and cornered meant "walked up to it and stays within 2.5 m, in its sight,
   for 3 s". Since 2026-09-30 (user: "Aggressive creatures should become frightened when the player comes close, and
   fight back if the player attacks or if they are cornered"): the outclassed creature is frightened (runs, decision
   29), and it fights back when provoked (a hit carrying any damage, fire- or poison-only and blocked hits included, or
   a projectile landing within 4 m, by that player, for 30 s after the last one) or cornered (decision 30). Not
   included: packmates joining (Later: help call).
5. **A near miss provokes only within `NearMissRange` (4 m) of the creature**; farther away the afraid creature ignores
   the impact. Options: vanilla's radius (the weapon's hit noise: 8 m for bows, 30 m for a thrown spear or the
   harpoon, including impacts on other creatures, 1.3: a spear thrown at a deer would provoke every afraid creature
   within 30 m); startle only (alert without a target); ignore all near misses (Passive Mobs). Consequence: an arrow
   landing within 4 m of afraid creatures, including one that hits a deer standing next to them, provokes them.
   **FLAG.**
6. **First version: calm = ignore only**, no walking away or fleeing on sight (The Mark of Oden removed fleeing on
   sight after it turned hunting into chases and flipped whole biomes). **Replaced on 2026-09-30** by the fear of
   decision 29: the user found the creatures "completely passive" in game. The concern that made the first choice is
   answered by the limits of the fear: only within `FearRange`, only when the creature senses the player, and only two
   bosses past its biome (decision 25), so a biome flips only long after the player has moved on.
7. **Per-creature, per-player judgement on the AI owner through `CanSenseTarget`**, not by nulling `FindEnemy`
   (hides the strong player's tames and the next player) nor by `m_aiSkipTarget` juggling (TruePassiveMobs). Same split
   as vanilla Passive Mobs. The `FindEnemy` postfix only corrects the hunters' fallback, which skips the sense check.
8. **Boss rank = highest boss credited, lifetime of the character, all worlds, cheated kills included** (the
   profile's raw table). Options: count of bosses in order; per world (global keys); skip cheated kills (the self-tests
   and `spawn`-killed bosses are cheated). Reason: one vanilla data source, no own save data, like vanilla's campaign
   progress. **FLAG:** a character keeps its rank in a new world, and skipping bosses is rewarded.
9. **The rout ignores progression and applies to every player's kill.** Literal request ("if their leader is taken
   down"). Option: rout only when the killer outclasses the followers.
10. **The rout needs a player or a tame to have taken part** (a hit at any time, or the last hit). A leader that
    drowns or is killed by other creatures alone does not demoralise the pack. Small deviation from "taken down" by
    anything. **FLAG** (minor).
11. **Buildings unchanged**: afraid creatures may still attack priority building pieces as in vanilla (which pieces are
    priority targets is prefab data, section 9) while no player they fear is close. Option: suppress while an
    outclassing player is near (Later).
12. **Rout 15 s within 25 m, then shaken 60 s** (E2). Reason: a rout that ends with the pack walking straight back
    feels like no rout. All three are settings.
13. **Default packs beyond greydwarfs** (E4). The user wrote "for example"; one setting to trim. **FLAG.**
14. **Sneak-attack and Sneak XP guards (E3)**: fixed rules, no setting. Vanilla's own Passive Mobs rule for sneak
    attacks; crawling near afraid creatures pays the slow rate. Re-checked on 2026-09-30 with the fear: a creature
    running from the player is alerted, which vanilla already treats as "aware" (no sneak attack, slow Sneak rate), and
    the rule covers the afraid creature that sees the player without running (beyond `FearRange`, or turned around after
    calming). **FLAG** (added beyond the request).
15. **Name plate tag (E1) in the mod's own label**, not in `Character.GetHoverName` (which also feeds center messages
    and the mount bar) and not appended to `m_name.text` (vanilla rewrites it every frame, so an appended tag would
    make TextMeshPro rebuild every tagged plate twice per frame, and it would fight mods that restyle the name).
    **Replaced on 2026-09-30** by decision 36: no tag at all.
16. **Rout and fear frames skip vanilla `MonsterAI.UpdateAI`** (prefix returning false) but run the base update
    through a non-virtual delegate call, so other mods' `BaseAI.UpdateAI` patches still run; outside those frames
    vanilla always runs. Options: postfix override (breaks on the shared path cache, 1.4; an attack can already have
    started); transpiler (breaks on game updates); reverse patch of the base (skips other mods' patches on it); a
    vanilla flee branch (`m_fleeIfNotAlerted` flees only from a **target**, which would make the player "sensed":
    combat music, no bed, 1.2).
17. **Exemptions** (2.8): bosses (and `m_enableHuntPlayer` prefabs), creatures near an alerted boss (60 m), raids,
    tames, training dummies and Dvergr stay vanilla; **world-spawn night hunters follow the home biome lists**
    (`NightHuntersCanBeAfraid`, default on; named `CalmNightHunters` before 2026-09-30, decision 31). Options for night
    hunters: exempt (the first draft: a Fader-killer kept being hunted at night by the Fulings, Seekers and Charred the
    worked table outclassed, exactly the sheet's complaint); follow the rules (chosen). Hildir's named bosses are not
    `m_boss` in 1.0.16; they are never afraid because they are in no list. **FLAG:** night hunters lose their vanilla
    purpose (late-game danger in the lower biomes) for players who outclass them by two bosses; raids (for example a
    boar raid, if the game still sends one to a strong player) still attack everyone (Later: afraid raids).
18. **Everything that changes gameplay is a server rule**; only `ShowProgressMessages` is personal (`ShowOnNameplates`
    was too, until decision 36 removed it).
19. **Both side with a join check on the framework's verdict** (house rule): a game without the mod, or with it
    turned off or in another network version, would run vanilla AI for every creature it simulates, so its players'
    neighbours would be attacked by creatures that should be afraid. The server refuses such players with
    `NetworkGate.PeerCompatible` (has the mod, same network version, not turned off on their game, 1.9), about a
    second after they join and about a second after they turn the mod off while connected (`PeerStateChanged`), the
    same check as the other Combat mods of this run (4.5). Options: this mod's own "taking-part" check (the earlier
    draft: the server watched each player's published standing and refused after 10 s without one; dropped, because
    the framework now reports the client's state); a framework rule that keeps required Both mods on while connected
    (a player who turns a mod off would find it still on). **FLAG:** a player who turns the mod off in the MC Mods
    panel while on a server is disconnected about a second later ("Incompatible version"), with no warning of this
    mod's own (open question 9), unless `AllowPlayersWithoutMod` is on.
20. **Provocation per player in the creature's ZDO** (up to 4 players, 2.6), not one shared deadline plus vanilla's
    attacker flags. Reason: the flags are set by zero-damage hits, never cleared, saved with the creature and keyed by
    name, so an old touch would revive a provocation, and one shared "last provoker" dropped the first player in
    mid-fight.
21. **First version: taming calm creatures was easy** (calm tameable creatures stayed unalerted, and feeding never
    cornered them). **Replaced on 2026-09-30** by decision 33 (taming as in the normal game).
22. **Progress messages (E5)**: a center message when the boss rank rises, a corner message when a kill step is
    reached. Reason: without feedback G2 is invisible. **FLAG** (added beyond the request).
23. **Startled afraid creatures calm down** (2.5 piece 6) after 5 s without being hurt, when every player near them
    outclasses them and none is close enough to run from. Reason: G1 and a clean alert icon (G7); vanilla's 30 s
    give-up stays for creatures with no player near. The alert of the mod's own runs is taken back by the calm step of
    decision 36, player near or not.
24. **Rules pending → hostile** (4.4): a client that has not yet received the server's rules treats everyone as
    hostile and publishes nothing, so it never acts on its own config on a server.

**Decisions of 2026-09-30, user feedback after the first in-game test** (build `3cda33b+dirty`; the user's words are
quoted in G1-G3):

25. **Two bosses ahead** (G2). A creature is afraid of a player who has helped kill the boss `BossesAhead` (default 2)
    bosses after its biome's boss: Black Forest greydwarfs after Moder (Bonemass **and** Moder dead, the user's
    example), Meadows boars after Bonemass. Elites and pack leaders one boss later still (`Elites`, `EliteExtraBosses`
    default 1): the user asked to keep them later than the rank-and-file, and +1 keeps the first version's gap
    (Trolls, shamans and brutes a boss after greydwarfs). Stars still add one boss each, and kill steps still bring a
    kind at most one boss early (decision 3). Consequences with the defaults: the Ashlands rank-and-file (rank 9) are
    afraid only of a Kall-killer with 100 kills of that kind; the Mistlands and Ashlands elites and the whole Deep North
    are never afraid; a Fader-killer ("fully geared from Ashlands") frightens everything up to the Plains
    rank-and-file. **FLAG:** the elite +1 and its list (every default pack leader plus `Troll`, `Bjorn`,
    `Abomination`, `Wraith`, `Writhan`, `BlobElite`, `StoneGolem`, `Unbjorn`, `Gjall`, `Morgen`, `FallenValkyrie`);
    kill steps still one boss early.
26. **Rank settings = home biome lists + `BossesAhead` + `Elites`** instead of eight per-boss lists. Reason: the
    task asked for a configuration that is easy to understand; players know where a creature lives, and the boss of
    each biome is the game's own ladder (fixed in code). Options: one per-biome boss mapping setting (a text such as
    "Meadows = Eikthyr; ..."; the mapping never needs changing in vanilla, so it stays in code); deriving home biomes
    from the world spawn table (fails for dungeon creatures, which are not in it, and for the night hunters, whose
    lowest spawn biome is the Meadows). 9 list settings (8 biomes and the Ocean) + `Elites`; the 79 creatures of the
    first version are all listed.
27. **The Ocean list counts as level 3 (Bonemass)**: serpents are met on the open sea, which a player crosses with a
    longship (iron, after Bonemass); rank 5 (after Yagluth) is the first version's placement too. A spawn point in the
    Ocean biome gives no spawn level: the home biome decides (a Neck near a deep shore stays a Meadows Neck). **FLAG.**
28. **The biome is where the creature spawned, never weaker than its home biome** (G2). Level used = the higher of the
    home biome's and the spawn biome's (spawn point of 1.11; a dungeon's entrance; Ocean or unknown = home only).
    Reason: the user's examples (skeletons in the Plains, Mountains, Black Forest; greydwarfs in the Deep North), while
    keeping the creatures that the game sends **down** into easier biomes as strong as at home: greydwarfs in the
    Meadows at night, the night hunters (Fulings, Seekers, Charred in the lower biomes; spawn biome alone would make them
    afraid of players two bosses past the Meadows, i.e. of everybody who can meet them). Options: spawn biome only;
    current position (a creature that wanders or chases across a border would change its mind mid-fight, and it would
    need a biome lookup per check); home biome only (the first version). Cached per creature per game: the spawn point
    never changes. **FLAG.**
29. **Afraid instead of calm: the creature runs when the player comes close** (G1). Within `FearRange` (default 12 m,
    3-40 m; "10-15 m" in the task) and only while it senses the player (vanilla sight or hearing, so a sneaking unseen
    player can close in), it runs with vanilla `BaseAI.Flee` (alerted, no target) until the player is 4 m beyond the
    range or unsensed for 5 s, then calms (no target, un-alerted). It never attacks unless provoked or cornered.
    **Fighting beats fear:** a creature whose target is, or that senses within `FearRange`, a player it is hostile to or
    provoked by does not run from the players it fears; while it runs it ignores everything but a provocation (which
    ends the fear at once) and the arrival of such a player (at the next check). Reason: keeps each player judged alone
    (G2: the weak player next to a strong one is still attacked, the strong one still never targeted), deterministic,
    and one flee target at a time. Options: fear wins over everything (a strong player would shield every weaker
    friend); flee from every afraid-of player at once (no single flee point); walking away instead of running (vanilla
    `Flee` runs only while alerted; walking would let a sprinting player catch everything). **FLAG:** `FearRange` 12 m,
    the 4 m and 5 s release, and "fighting beats fear".
30. **Cornered = the player stays within `CorneredRange` (default 3 m) for `CorneredSeconds` (default 2 s) while the
    creature runs from them**, counted every frame. It covers both of the user's cases: it cannot get away (no path,
    a wall, a pen) and the player keeps up with it. The first version's "walked up" test and the feeding exception are
    gone: a creature that walks up to a player runs as soon as it senses them, and one that has not sensed the player
    never runs, so neither can be cornered by accident. Defaults were 2.5 m and 3 s (without running, a player standing
    close for 3 s was the whole signal); a running creature that a player keeps up with for 2 s at 3 m is already
    chased hard. **FLAG:** new defaults.
31. **Night hunters are afraid like the others**; the setting `CalmNightHunters` became `NightHuntersCanBeAfraid`
    (same default, on). The stand-down (2.5 piece 5) still turns their hunt off while no player near them is one they
    would fight; then they behave as normal night creatures, afraid of the players who outclass them.
32. **Name plate words**: "afraid" (green) for a creature afraid of you, running or not; "routed" (blue) for a pack
    whose leader fell. "fleeing" would be true of both, and only the owner knows whether an afraid creature is running
    at this moment (2.1). **Replaced on 2026-09-30** by decision 36: no words on the plates.
33. **Taming as in the normal game** (supersedes 21): a wild tameable creature runs from a close player it is afraid of
    and senses, and vanilla pauses taming while it is alerted; drop food and step back, or sneak. It still never
    attacks the tamer unless provoked or cornered. Reason: a direct consequence of the fear; keeping taming easy would
    need an exception to "afraid creatures run" that the user did not ask for. **FLAG.**
34. **`FearRange` is at least 3 m in the settings** (no "ignore only" value): the user's rule "No vanilla aggressive
    enemy should completely ignore the player from this mod". In-memory test rules (`ServerRules.TestRules`, not
    clamped) may set 0 to take the fear out of a step.
35. **Rules layout 2, `ModNetworkVersion` 2**: the settings changed, so the rules message changed (5.2), and the
    project rule bumps the network version whenever a payload changes, released or not. A game with a build of the
    first version (network version 1, rules layout 1) is refused at the join check with "Incompatible version",
    instead of joining, failing to read the rules and playing vanilla without anyone noticing.

**Decision of 2026-09-30, user feedback: no extra name plates, vanilla alerted state** (G7; the user's words are quoted
there):

36. **The game's own alerted state replaces the name plate label (E1, decisions 15 and 32).** Removed: `Plates.cs`,
    the `EnemyHud.UpdateHuds` postfix, the personal setting `ShowOnNameplates`, and the label's state and cleanup. A
    creature running from a player or routed is alerted through vanilla `SetAlerted` on its owner (it already was, so
    that it runs rather than walks, 1.4), which every game shows as the vanilla alert icon (1.5); the run's end now
    always takes that alert back (`FleeFrames.Calm`, 2.5 piece 2, 2.7), also at the end of a rout with no player near
    (before, only piece 6 did it, which needs a player within the creature's view range, else vanilla's 30 s give-up
    left the icon up). The calm takes back only an alert the flee frames held (`FleeAlert`: raised by them, or kept by
    them after clearing the target that vanilla alerted it for), only on the owner, only with no creature target (a
    building target does not count: vanilla never alerts for one), no live provocation by anybody, no hunt and no
    player it would fight close and sensed (the alert stays for that fight); a hurt within 5 s delays it to a later
    check. Options: keep the label as a
    setting that is off by default (still an extra plate part and a per-frame `EnemyHud` patch, which the user asked
    to remove); vanilla's `Aware` icon for afraid-but-not-running creatures (it means "has a target", which an afraid
    creature never has toward that player, and it is vanilla state shared by all players, so it cannot say "afraid of
    you" to one player only); an alert while the creature is merely afraid (it would run everywhere, lose sneak
    attacks and taming for everyone, and replay the alert sound at every change). Consequences: the icon now shows
    what the creature does, the same on every screen (the old label's hand-off limit, 4.6, is gone), but an afraid
    creature that is not running looks like any other, so a player learns that a creature is afraid of him only when
    it runs from him or never comes for him. **FLAG** (the user's own request; the calm rules are the mod's reading of
    "vanilla alerted visual state").

---

## 4. Multiplayer

### 4.1 Who runs what

| Piece | Runs on |
|---|---|
| Standing computed and published; progress messages | each player's own game (its profile, its player ZDO) |
| Afraid gate, fear check and fear frames (with the cornered count), target drop, hunter stand-down, alert while running and un-alert (the calm), provocation writes, rout frames, spawn level of the creatures it judges | the creature's owner (the game simulating it); every game that judges a creature (sneak rules) computes that creature's spawn level once too, from the same spawn point |
| Leader death detection | the leader's owner |
| Rout application | every game, for the followers it owns (routed broadcast + recent routs) |
| Sneak-attack rule, provocation by hits | the victim's owner (`Character.RPC_Damage`) |
| Sneak XP rule | the sneaking player's own game |
| Alert icon on the plates (G7) | vanilla on each game: `EnemyHud` shows `IsAlerted()`, copied from the owner's ZDO `alert` (1.5); no code of the mod |
| Join check (and its re-check when a player turns the mod on or off), rules for everyone | the server (dedicated or host) |

### 4.2 Network messages

| Name | Kind | Direction | Payload | When |
|---|---|---|---|---|
| `MC.Combat.Creatures.Morale.Hello` / `.HelloAck` | framework `ZRpc` | client ↔ server | framework (`NetworkGate`: network version, version, and whether the copy is on) | at connect |
| `MC.Combat.Creatures.Morale.HelloState` | framework `ZRpc` | client → server | framework: `"on"` / `"off"` | the client's copy turns on or off (or fails) while connected; the server raises `NetworkGate.PeerStateChanged` |
| `MC.Combat.Creatures.Morale.SettingsRequest` | `ZRpc` | client → server | `int` layout (2) | client `RPC_PeerInfo`; client turned on while connected |
| `MC.Combat.Creatures.Morale.Settings` | `ZRpc` | server → client | `ZPackage` of `MoraleRules` (5.2) | answer; to every modded peer when the server's settings change or its copy turns on |
| `MC.Combat.Creatures.Morale.Rout` | `ZRoutedRpc`, to `Everybody` | leader's owner → all | `Vector3` death position, `int` leader prefab hash | a pack leader died (multiplayer only; single player applies directly) |
| `Error` (vanilla) | `ZRpc` | server → client | `int` 3 (`ErrorVersion`) | the join check refuses a player |

The routed RPC is registered once per `ZRoutedRpc` instance (a `ZNet.Awake` postfix, and `OnActivated` when a session
already exists), remembering the instance it registered on because a second `Register` throws (1.9). The handler
stays registered while the mod is off (no unregister) and returns at once then. Every handler (`Rout`, `Settings`,
`SettingsRequest`) catches its own exceptions and calls `PatchGuard.Report`.

### 4.3 ZDO keys

| ZDO | Key | Type | Written by | Meaning / lifetime |
|---|---|---|---|---|
| player | `MC.Combat.Creatures.Morale.Standing` | byte[] (2.2), ≤ 83 bytes | that player's game | its standing; `[1,255,0]` = no standing (withdrawn when the copy turns off); player ZDOs are not saved |
| creature | `MC.Combat.Creatures.Morale.Provokers` | byte[] (2.6), ≤ 66 bytes | the creature's owner | per-player provocation deadlines; `[1,0]` = cleared (by a rout) |
| creature | `MC.Combat.Creatures.Morale.RoutUntil` | long (ticks) | same | routed until; shaken until + `ShakenSeconds` |
| creature | `MC.Combat.Creatures.Morale.RoutFrom` | Vector3 | same | flee from here |

Creature keys are written only on events (a provocation at most once per second per player, a rout), only for
creatures in a home biome list (and for routed followers), never removed (overwritten), stay in the world
save with the creature, and are ignored by vanilla. All are deadlines on the server clock: harmless once past.
Vanilla keys read: `Attackers` (int) and the per-player attacker flags (kill credit and the rout trigger only),
`level`, `tamed`, `EventCreature`, `huntplayer`, `alert`, `spawnpoint` (read by vanilla `BaseAI.Awake` into `m_spawnPoint`, 1.11).

`ModNetworkVersion` = 2 (covers the three RPC names and payloads, the three creature keys, the standing layout 1, the
provokers layout 1 and the rules layout 2). Any change of those bumps it, released or not: the rules layout went from 1
to 2 on 2026-09-30 (the settings of 5.1 changed, decision 35), so the version went from 1 to 2.

### 4.4 Server rules (`ServerRules`, copy of Forge Idol Upgrades')

Every setting except `Enabled`, `AllowPlayersWithoutMod` and `ShowProgressMessages` is a
**rule**: `MoraleRules` snapshots them (and the parsed tables: prefab hash → home biome level, elite set, leader hash → follower set, kill
steps). Every game reads rules only through `ServerRules.Current`: the server's while this game is a client of a
server with the mod (same `ZNet` session), else its own config. **Pending:** a client connected to a server that has
not answered yet has no rules: `Decide` says Hostile (step 1) and the standing is not published (decision 24); the
answer normally arrives before the player spawns. This is the pending policy the Combat mods of this run share: a
client never applies its own gameplay settings on a server that has the mod, and while it waits for the server's, the
mod's effect is the vanilla game (Tower Shield Wall: vanilla towers; Weapon Moveset: moves off; here: every creature
hostile, nothing published). A mod that must differ says why in its own design doc. The client clamps received
values to the config ranges, refuses an unknown layout (keeps the last good or own rules, Warning once), and logs the
rules in use at Info, again for every new set of rules (also when the summary reads the same). A change of the rules
rebuilds the tables and republishes the local standing (kill steps, listed tokens). A client whose copy is off ignores
the rules (it asks again when it turns on), and the server pushes a change only to peers that
`NetworkGate.PeerCompatible` accepts (stage 7).

### 4.5 Join check (`PlayerCheck`, copy of Forge Idol Upgrades' with the framework's verdict)

Server (dedicated or host), while Active; never in single player, never the host's own player (not a peer). The same
check as the other Combat mods of this run:
- **Verdict:** `NetworkGate.PeerCompatible(peer)` (1.9): the player's game has the mod, the same `ModNetworkVersion`,
  and has not turned it off (their copy is on, its dependencies are active, it did not fail to start). A copy that
  reports no state (built before the framework check) counts as compatible.
- **When:** a peer becomes ready (`ZNet.RPC_PeerInfo` postfix); a connected player's copy turns off or on
  (`NetworkGate.PeerStateChanged`, raised by the framework's `HelloState` message); the server's copy turns on
  (`OnActivated`: every ready peer); `AllowPlayersWithoutMod` is switched back to off (every ready peer). Each one
  schedules the player with the same grace of 1 s (`PlayerCheck.Schedule`); a player already waiting keeps its
  deadline, and the check reads the state at the deadline, so turning the mod off and on again within that second
  changes nothing. `PlayerCheck.Start` (in `OnActivated`) subscribes to `PeerStateChanged` (unsubscribing first, so it
  is never subscribed twice) and checks every ready peer; `PlayerCheck.Stop` (in `OnDeactivated`) unsubscribes and
  cancels the pending checks.
- **Refusal** (not compatible, `AllowPlayersWithoutMod` off): vanilla `Error(ErrorVersion)` (their game shows
  "Incompatible version" and goes back to the main menu) and disconnect 4 s later through vanilla's kick list. Server
  log (Warning): "Refused <player>: their game <`NetworkGate.PeerProblem(peer)`, for example "has the mod turned off">.
  Creature Morale is required on every player of this server (the creatures their game controls must follow the same
  rules). To let such players in, set AllowPlayersWithoutMod = true." So a player is refused about a second after
  joining (usually while their game still loads the world, before their character appears), or about a second after
  turning the mod off while connected.
- **Allowed** (not compatible, `AllowPlayersWithoutMod` on): a Warning naming the problem; the player stays and is
  treated as a player without the mod (4.6). A compatible player gets a Debug line.
- `PlayerCheck.Decide(isServer, connected, ready, beingKicked, compatible, allowWithoutMod)` stays pure → Skip /
  Compatible / Allowed / Refuse (self-tested in `morale.rules`). Why a player is not compatible (no mod, another
  network version, turned off) is the framework's `PeerCompatible` / `PeerProblem`, not duplicated here.
- The withdrawn standing (2.2) is not part of the check: it only makes other games treat a player whose copy turned
  off as vanilla at once.
- **No warning of this mod's own** on the player's screen when they turn it off while connected: the refusal comes
  about a second later, too soon for "turn it back on", and a warning that every required Both mod needs belongs in
  the framework (open question 9). A refused player who turns it back on during the 4 s before the disconnect stays
  refused (`Decide` skips a player being kicked).

### 4.6 Hand-off cases

| Case | Result |
|---|---|
| A creature simulated by a game **without** the mod (server allows such players) | vanilla AI: it attacks everyone, ignores `Rout`, and our ZDO keys; when a game with the mod takes it back, the keys still apply (a rout still running resumes, a provocation still counts) |
| A player **without** the mod near creatures simulated by modded games (server allows it) | no standing: every creature is hostile toward them, shaken ones included, as in vanilla; their sneak attacks and Sneak XP are vanilla (nothing is ever afraid of them); their game shows the alert icon of creatures running from other players and of routed packs, since it is vanilla state (G7, M07) |
| Alert icon on a creature simulated by a game **without** the mod (server allows it), seen by a modded player | vanilla's state of that creature, the same on every screen (it attacks everyone the normal way). The first builds' label was each viewer's own judgement and could say "afraid" on a creature attacking the viewer; that limit went with the label (decision 36) |
| A player with the mod installed but **turned off** (or failed to start), or in **another network version** | refused about a second after joining, or about a second after they turn it off while connected (4.5); their game shows "Incompatible version". With `AllowPlayersWithoutMod` on: like a player without the mod (a turned-off copy withdrew its standing, 2.2; a copy in another network version is kept off by the framework and never publishes one) |
| Ownership changes mid-fight | the new owner's creature has no target (private state); the provocation in the ZDO lets it pick the player again at its next search; a player it is afraid of is not picked (it runs from them if they are close and sensed) |
| Ownership changes while it runs from a player | the new owner's memory has no fear and no `FleeAlert` (7.3): the creature, alerted with no target (ZDO `alert`, so its icon stays on every screen), is judged at the new owner's first check: it runs again if the player is still close and sensed (its frames take the alert over, and their calm takes it back later), else piece 6 calms it when a player it is afraid of is near, or vanilla's 30 s give-up does (2.5 piece 6) |
| Spawn level on different games | every game computes it from the same ZDO spawn point with the same world generator, so the rank agrees; a game without a `WorldGenerator` yet (none in a loaded world) would use the home biome only until it has one |
| Ownership changes mid-rout | the new owner detects the gain (7.3), checks at once, reads `RoutUntil`/`RoutFrom`, resets the flee and path timers and keeps fleeing from the same point |
| Ownership changes while being cornered | the cornered time restarts (memory), with the fear |
| A game loses a creature and gains it back later | the gain is detected (7.3): memory is reset, nothing stale (fear, cornered time, timers) comes back; the cached spawn level is kept (it cannot change) |
| Follower owned by a game that did not see the leader die | reached by the `Rout` broadcast |
| Follower whose ownership moves while the `Rout` broadcast is in flight | the new owner applies it from its recent-routs list at its first check (2.7); a game that joined after the broadcast does not |
| Leader simulated by a game without the mod | no rout (nobody sends it) |
| World or save loaded without the mod | the creature keys stay, ignored; nothing on items, pieces or characters |
| Items, pieces, character saves | nothing touched (the standing is recomputed from the vanilla profile) |

### 4.7 Dedicated server

The Both mod loads in `valheim_server.exe`: join check, rules, and it relays the `Rout` broadcast
(vanilla routing). It simulates no creatures and has no local player, so its AI and standing patches have nothing to
do. A host is a normal game plus the server part. A mod that makes a dedicated server simulate creatures
would run the AI parts there; they work without a local player (not tested).

### 4.8 `ModMultiplayerNotes` (player-facing, csproj)

"Install it on the server (or the host) and on every player's game, and keep it turned on. Each creature is controlled
by one player's game, and each player's boss kills and kill counts are only known to their own game, so the server
refuses players who do not have the mod, have it turned off or have a version that cannot talk to the server's (their
game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on. Turning it off while playing on
such a server disconnects you. The settings of the server (or host) apply to everyone. Each player is judged by
their own progress. On a server without the mod it turns itself off."

---

## 5. Config

### 5.1 Settings

Every rule ends its description with " In multiplayer the setting of the server (or host) is used for everyone."
All settings apply live (the tables rebuild; the standing republishes); in a world, a rule edit applies once edits
stop for 0.75 s (stage 7). Synced = part of the server rules.

| Section | Key | Default | Range | Synced | Description (user-facing) |
|---|---|---|---|---|---|
| General | Enabled | `true` | | no (framework) | Turn this feature on or off. Takes effect immediately, no restart needed. |
| General | Status | — | read-only | no | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | | server only | Used only by the server (or the host). Off: a player whose game does not have this mod, has it turned off, or has a version that cannot talk to the server's is refused and their game shows "Incompatible version", because the creatures their game controls would attack everyone the normal way. This happens about a second after they join, or about a second after they turn the mod off while playing. On: such players may play; creatures near them behave as in the normal game toward them. |
| Afraid creatures | BossesAhead | `2` | 0-8 | yes | How many bosses past the boss of a creature's biome a player must have helped kill (hit it at least once before it died) before that creature is afraid of them. The biome bosses are: Meadows Eikthyr, Black Forest The Elder, Swamp and Ocean Bonemass, Mountains Moder, Plains Yagluth, Mistlands The Queen, Ashlands Fader, Deep North Kall. With 2, Black Forest creatures are afraid of a player who has helped kill Moder. A creature counts as a creature of the biome it spawned in, or of its home biome if that is harder (see Home biomes). |
| Afraid creatures | Elites | list in 2.3 (24 names) | prefab names | yes | Creatures (prefab names, as used by the spawn command, comma-separated) that need EliteExtraBosses more bosses than the others of their biome: pack leaders and the biome's big creatures. A creature must also be in a Home biomes list. |
| Afraid creatures | EliteExtraBosses | `1` | 0-3 | yes | How many more bosses the Elites need before they are afraid of a player. |
| Afraid creatures | StarRank | `1` | 0-5 | yes | Each star of a creature needs this many more bosses before it is afraid of you. 0 = stars do not matter. |
| Afraid creatures | KillSteps | `100, 400` | up to 5 increasing numbers, 1-100000 | yes | Each number of kills you reach of one kind of creature counts as one more boss toward that kind only (see MaxBossesSkippedByKills). Kills are your character's lifetime kills, as the game counts them: creatures that share a name share the count (all skeletons; Greydwarfs and Frozen Greydwarfs), and killing your own tames counts. Empty = kill counts do not matter. |
| Afraid creatures | MaxBossesSkippedByKills | `1` | 0-8 | yes | Kill steps can make a kind of creature afraid of you at most this many bosses early; beyond that they only make up for its stars. 0 = kill steps only make up for stars. |
| Afraid creatures | FearRange | `12` | 3-40 m | yes | An afraid creature runs away when a player it is afraid of comes this close (in metres) and it can see or hear them, until that player is a few metres farther away; then it calms down. A player who sneaks up unseen and unheard can get closer. It never attacks that player unless they attack it or corner it. |
| Afraid creatures | NightHuntersCanBeAfraid | `true` | | yes | The creatures the game sends at night to hunt players after some boss kills (Fulings after Yagluth, Seekers after The Queen, Charred after Fader) follow the same rules: they are afraid of the players who outclass them and hunt the others. They count as creatures of their home biome (Plains, Mistlands, Ashlands). Off = they hunt everyone as in the normal game. Raids and bosses always behave as in the normal game. |
| Home biomes | Meadows | `Boar, Neck, Greyling, Hen, Skeleton_Meadows, Skeleton_Meadows_noarcher` | prefab names | yes | Creatures (prefab names, as used by the spawn command, comma-separated) that live in the Meadows, whose boss is Eikthyr. A creature counts as a creature of this biome or of the biome it spawned in, whichever is harder. A creature in no list always behaves as in the normal game; a creature in several lists uses the easiest one. |
| Home biomes | BlackForest | list in 2.3 | prefab names | yes | Same, the Black Forest (The Elder). |
| Home biomes | Swamp | list in 2.3 | prefab names | yes | Same, the Swamp (Bonemass). |
| Home biomes | Mountain | list in 2.3 | prefab names | yes | Same, the Mountains (Moder). |
| Home biomes | Plains | list in 2.3 | prefab names | yes | Same, the Plains (Yagluth). |
| Home biomes | Mistlands | list in 2.3 | prefab names | yes | Same, the Mistlands (The Queen). |
| Home biomes | Ashlands | list in 2.3 | prefab names | yes | Same, the Ashlands (Fader). |
| Home biomes | DeepNorth | list in 2.3 | prefab names | yes | Same, the Deep North (Kall Fimbulbringer). |
| Home biomes | Ocean | `Serpent` | prefab names | yes | Creatures of the open sea. The Ocean has no boss and counts as the Swamp's level (Bonemass). A creature that spawned at sea counts as a creature of its home biome. |
| Fighting back | ProvokedSeconds | `30` | 5-300 s | yes | After you hit an afraid creature (or one of your arrows, bolts or thrown weapons lands within NearMissRange of it), it fights you until this many seconds have passed since the last time, then is afraid of you again. |
| Fighting back | NearMissRange | `4` | 0-30 m | yes | One of your projectiles (arrow, bolt, thrown spear, harpoon) that lands this close to an afraid creature, even on another target, makes it fight you. Farther away it ignores the impact, while in the normal game every creature within the weapon's noise range is alerted. That noise range is also the limit of this setting: creatures farther from the impact never notice it (8 m for bows and crossbows, 4 m for the Huntsman bow, 30 m for thrown spears and the harpoon). 0 = only real hits count. |
| Fighting back | CorneredRange | `3` | 0-6 m | yes | An afraid creature that is running from you fights back when you stay this close to it (in metres) for CorneredSeconds: it cannot get away, or you keep up with it. 0 = never. |
| Fighting back | CorneredSeconds | `2` | 1-30 s | yes | How long (in seconds) you must stay within CorneredRange of a running creature before it fights back. |
| Rout | Packs | list in 2.7 | `leaders>followers; …` | yes | Packs whose followers flee when a leader dies: each pack is "leader prefabs > follower prefabs", comma-separated, packs separated by semicolons. The rout happens only when a player or a tame took part in the kill. Empty = no rout. |
| Rout | RoutRadius | `25` | 5-60 m | yes | Followers within this distance of the dead leader flee. |
| Rout | RoutSeconds | `15` | 3-60 s | yes | How long the followers run away. |
| Rout | ShakenSeconds | `60` | 0-600 s | yes | After running away, the followers are afraid of every player for this long unless someone attacks them. 0 = they go back to normal at once. |
| Display | ShowProgressMessages | `true` | | no (personal) | Show a message when a boss kill or a number of kills makes more creatures afraid of you. |
| Debug (Debug builds only) | ForceBossRank | `-1` | -1..8 | no | For testing: publish this boss rank instead of your real one. -1 = off. |

No name plate setting (G7, decision 36): the first builds' `Display.ShowOnNameplates` is gone. BepInEx 5 keeps an
unknown entry of an existing config file (an orphaned entry) and writes it back on every save without reading it, so
the line stays in the file until the player deletes it; the README and `TESTING.md` (Setup, T25) say it can be
deleted. ConfigurationManager shows only bound settings, so its `Display` section tells which build runs.

**Name parsing.** Prefab names are turned into `name.GetStableHashCode()`, which is exactly the ZDO prefab hash, so
the home biome, elite and pack tables never need `ZNetScene` (the rules are first built at plugin start, before any
world, 7.5). Names are validated in a separate pass that runs only when `ZNetScene.instance` exists, once per
`ZNetScene` instance and rules version: unknown prefab names are logged (one Warning listing them) and ignored; a
prefab in several home biome lists takes the easiest (lowest level) one; elites that are in no home biome list are
never afraid (both in the same Warning).

### 5.2 Rules on the wire (`MoraleRules`, layout 2)

`int layout` (2), 9 × `string` home biome lists (Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, Ashlands,
DeepNorth, Ocean), `int BossesAhead`, `string Elites`, `int EliteExtraBosses`, `int StarRank`, `string KillSteps`,
`int MaxBossesSkippedByKills`, `bool NightHuntersCanBeAfraid`, `float FearRange`, `float ProvokedSeconds`, `float
NearMissRange`, `float CorneredRange`, `float CorneredSeconds`, `string Packs`, `float RoutRadius`, `float RoutSeconds`,
`float ShakenSeconds`. Layout 1 (the first version: 8 per-boss lists, `CalmNightHunters`, no fear range) is refused
(decision 35). Received numbers are clamped to the ranges above; the lists are parsed by the receiver. Rules built in
memory for self-tests (`ServerRules.TestRules`) are not clamped (asserted by `morale.rules`; a `FearRange` of 0 there
turns the fear off for a test step).

---

## 6. Compatibility

### 6.1 Sibling mods of this run

- **Sneak Ambush** (`MC.Combat.Sneak.Ambush`):
  - Its smoke bomb makes `BaseAI.CanSeeTarget` / `CanHearTarget` return false. Our gate is a postfix on
    `CanSenseTarget` that only turns true into false: both combine (a smoked player is not sensed at all; a player
    a creature is afraid of is never targeted even when seen). Our fear check (2.5 piece 2) uses `CanSeeTarget` and
    `CanHearTarget` directly, so a smoked player never starts a creature's fear: they can walk through the smoke up to
    an afraid creature, and one that ran from them before the burst stops running after 5 s without sensing them. Our
    cornering needs the creature to be running from the player, so standing in the smoke next to an afraid creature
    that has not sensed you never corners it. Our hunter fallback (2.5 piece 5) picks a hostile-toward player without a
    sense check, like vanilla's.
  - Sneak attacks: our `Character.RPC_Damage` prefix lowers `hit.m_backstabBonus` to 1 only for an afraid creature
    that **can see** the attacker and is not alerted, through `CanSeeTarget`, so smoke or Sneak Ambush's
    standing-still stealth (a lower stealth factor shrinks sight) make the sneak attack possible again. A creature
    running from the player is alerted, and vanilla gives no sneak attack on an alerted creature. Sneak Ambush detects a
    sneak attack by comparing the victim's `m_backstabTime` before and after `RPC_Damage`; when we suppress the bonus,
    vanilla does not touch `m_backstabTime`, so it pays no sneak-attack XP. Order of the two prefixes does not matter.
  - Sneak XP: **this mod owns the "afraid creatures do not count" rule** in a `BaseAI.InStealthRange` postfix; Sneak
    Ambush must not change `InStealthRange` for the same purpose. If it adds its own postfix, both can only turn true
    into false and compose.
  - Hidden health bars under smoke: its `EnemyHud.TestShow` postfix makes vanilla destroy the plate of a creature in
    smoke and make a new one later; we put nothing on plates (G7), and the new plate shows the alert icon from
    `IsAlerted()` at once (2.5 UI). Its X01 checks that icon on the bar that comes back.
  - **Our hooks, for its compatibility row** (7.2): `BaseAI.CanSenseTarget(Character, bool)` postfix; `BaseAI.FindEnemy`
    and `MonsterAI.HuntPlayer` postfixes (night hunters); `MonsterAI.UpdateAI` prefix at `Priority.Low` (skips vanilla
    only on rout and fear frames; its once-per-second check starts and ends the fear, drops afraid-of targets and
    un-alerts startled afraid creatures; the fear frames count the cornering); `MonsterAI.RPC_OnNearProjectileHit`
    prefix (an afraid creature farther than `NearMissRange` from the impact skips vanilla); `Character.RPC_Damage`
    prefix (E3, then provocation); `BaseAI.InStealthRange` and `BaseAI.OnDeath` postfixes; no `EnemyHud` patch (since
    decision 36). We do **not** patch `MonsterAI.OnDamaged` (we provoke in the `RPC_Damage` prefix). We **do** create targets in three
    places, each compatible with its smoke rule: a provoking hit gives a creature with no target the attacker (2.6),
    after which vanilla sight and hearing, and so its smoke rule, decide whether the creature can find him; cornering
    needs a running creature, which needs `CanSeeTarget` or `CanHearTarget`, so a player who stayed smoked the whole
    time never corners; the hunter fallback picks a player without a sense check, as vanilla's hunt fallback does.
    A running creature makes no target and ignores Sneak Ambush's forget (it has nothing to forget).
  - **Which hits count** (contract, agreed): our provocation counts any hit by a player that carries damage before
    resistances (fire-only, poison-only, blocked and armor-absorbed hits included, 2.6), and Sneak Ambush's reveal
    (its 2.10, decision D31) uses the same test in its `Character.RPC_Damage` hooks. So a hit from inside the smoke
    that makes an afraid creature target the player also lets it see him through the smoke, and it fights him. If
    either side changes this test, the other must follow: otherwise such a creature would target a player it cannot
    sense and drop him again after Sneak Ambush's 3 s forget.
  - Agreed: Sneak Ambush's X01 is the same test as our X01 (seen by an afraid creature → no backstab and no
    sneak-attack XP; unseen, behind it or in smoke → backstab and XP; crawling near afraid creatures only → the slow
    0.1/s rate; a hit from inside the smoke provokes and reveals). Both X01 items run at rank 6, so that the two-star
    Greydwarf shot from the smoke (Sneak Ambush's T20 setup, a hit it survives) is afraid too. Sneak Ambush's documents
    use "afraid" since 2026-09-30 (the first version said "calm"); the contract is unchanged.
  - Cross-mod test X01.
- **Tower Shield Wall** (`MC.Combat.Shields.TowerWall`): a bash carries blunt damage before resistances, so it
  provokes like any hit, also in NG+ where armor absorbs nearly all of it (2.6), and it ends a running creature's fear
  at once (it turns to fight the bearer). Both mods prefix `Character.RPC_Damage` on the creature's owner: Tower only
  zeroes `m_staggerMultiplier` of bash hits (and changes `m_blockable` on its own bearer as the victim) and adds the
  bash's stagger itself after `ApplyDamage` (decoupled from armor); we read the damage before resistances and the
  attacker, so no order is needed. The bash has backstab ×1, so E3 never applies to it. **NG+ contract:** a bash whose
  landed damage is 0.1 or less never reaches vanilla `OnDamaged`, so vanilla would not alert the staggered creature,
  and the next hit would be a sneak attack on a staggered creature. Our side: the provocation gives it the bearer as
  target (not an alert, 2.6), and vanilla alerts it at its next AI tick when it sees him within its alert range. Tower
  Shield Wall's side (its 2.6, agreed): after such a bash it makes the `m_onDamaged` call vanilla skipped, once, so the
  creature is alerted and targets the bearer whatever it sees, and the hit that follows is not a sneak attack. X05 (=
  its C06; the NG+ part pairs with its T23). Reaching an afraid creature for a bash now means sneaking up on it
  unseen, or catching it (it runs when the bearer comes within `FearRange` and it senses him); a cornered one already
  fights back. The bash is slow on purpose (Tower Shield Wall's defaults: the hit lands about 0.8 s after the press,
  one bash every 2 s, 20 stamina), which changes nothing here.
- **Dual Wielding** and **Weapon Moveset**: their hits go through vanilla `Attack` → `Character.Damage`; they provoke
  like any hit, and their sneak-attack bonuses pass through the E3 rule. No shared patch. Their cross tests: Dual
  Wielding X06 (off-hand hits provoke), Weapon Moveset X04 (a sneak roll attack on a Greyling: no backstab from an
  afraid Greyling that faces the player; a Greyling is afraid of a player of rank 3, Bonemass, since 2026-09-30, rank 1
  before) and X05 (a jump attack provokes). Their own changes of 2026-09-30 (Dual Wielding's swap key is a queued
  equip and a sheathed pair is crossed on the back; Weapon Moveset's roll attack flows out of the end of the roll)
  touch no hit and no sense, so nothing changes here.

### 6.2 Other MC mods

- **Harpoon Hooks Tames**: prefix/postfix on `Character.RPC_Damage` for zero-damage harpoon hooks on **tames** (it
  puts the attacker flags back). Our prefix skips tames (exempt), so there is no overlap. The vanilla harpoon deals
  10 pierce, so harpooning an afraid **wild** creature provokes it like any hit. Its projectile has a 30 m hit noise:
  hooking a tame provokes only afraid creatures within `NearMissRange` (4 m) of the impact. X06.
- **Creature Kill and Tame Counts** (`Stats.PerCreature`): reads the same kill table (slot 0); its counts are the
  counts our kill steps use, so a player can see their progress there. X03.
- **Compendium Encyclopedia**: its `EnemyHud.UpdateHuds` postfix records `Character.m_name` of shown plates; we no
  longer patch `EnemyHud` or touch plates (decision 36), so nothing is shared. X02.
- **Sleep Through the Day**: afraid creatures, running or not, do not make the player "sensed" (no target), so they do not block a bed; hostile ones
  do (vanilla). X04.
- **Breeding Star Inheritance**: tames are exempt; nothing shared. **Crossbow Stays Loaded**, **Forge Idol Upgrades**,
  **Batch Feed**, UX mods: nothing shared.

### 6.3 External mods

Detected at `OnActivated` by plugin GUID **or** by plugin name (case-insensitive substring, because most GUIDs are
unverified) in `BepInEx.Bootstrap.Chainloader.PluginInfos`; one Warning each ("X also changes when creatures attack
players; both will run and the result is hard to predict; use one"). The mod does not stand down.

| Mod | Known hooks | Overlap |
|---|---|---|
| TruePassiveMobs (GUID `com.lhoffl.TruePassiveMobs` per the research brief's source read; name `TruePassiveMobs`) | prefix that skips `MonsterAI.UpdateAI` (with a reverse patch of `BaseAI.UpdateAI`), `FindEnemy` prefix/postfix/finalizer, `RPC_Damage` prefix | same purpose. Our `UpdateAI` prefix runs at `Priority.Low` and does nothing in a frame another prefix already skipped (`__runOriginal`); our `FindEnemy` postfix only replaces an afraid-of player result. |
| The Mark of Oden (`picsoul.valheim.markofoden` per the research brief; name "Mark of Oden" / "MarkOfOden") | `FindEnemy`, `UpdateAI` postfixes, `RPC_Damage`, `RPC_RegisterKill`, `GetHoverName` | same purpose and data; its plate text shows (we add none) |
| FearMe (GUID unverified; name "FearMe") | `FindEnemy` postfix, transpiler on `MonsterAI.UpdateAI` | same purpose; its transpiler is skipped only in our rout and fear frames |
| Odin's Ótti, CowardlyGreydwarfs, FleeOnSight, Monster AI Tweaks (GUIDs unverified; names) | unknown | same purpose |
| MonsterDB (RustyMods) | edits `MonsterAI` flee fields | compatible: we read the fields live |
| Creature Level and Loot Control, Star Level System (`MidnightsFX.StarLevelSystem`) | creature levels above 3 | compatible; each level raises the nerve (`StarRank`) |
| BetterUI (`useCustomAlertedStatus`) | its own display of the alerted status on plates | compatible: we add nothing to plates; a running or routed creature is alerted in the vanilla way, so its custom alerted display applies to it like to any alerted creature (unverified in game) |
| Mods adding creatures (Jötunn, RRR, Monstrum…) | new prefabs | never afraid unless added to a home biome list; they can still be packs |

---

## 7. Implementation plan

### 7.1 Files (`src/Combat/Creatures.Morale/`)

| File | Content |
|---|---|
| `MC.Combat.Creatures.Morale.csproj` | existing scaffold; `<ModIdea>Mob AI revamp</ModIdea>`; `ModNetworkVersion` 2 (decision 35); multiplayer notes 4.8 |
| `Plugin.cs` | `BindConfig` (section 5), `OnActivated` / `OnDeactivated` (7.5); `SettingChanged` → `ServerRules.OwnEdited`, `AllowPlayersWithoutMod` switched back to off → `PlayerCheck.ScheduleAllConnected` |
| `MoraleRules.cs` | rules snapshot (`Own()`), wire `Write`/`TryRead` (layout 2, clamping), parsed tables from name hashes (prefab hash → home biome level, elite set, leader hash → followers, kill steps), `TryGetRank(prefab, spawnLevel)`; no `ZNetScene` needed |
| `Biomes.cs` | biome levels (the game's boss ladder per biome, Ocean 3), the spawn level of a creature (`m_spawnPoint`, dungeon entrance, `WorldGenerator.GetBiome`), the hardest level of a spawn table entry's biomes |
| `PrefabTokens.cs` | ranked tokens (token → lowest and highest rank of its listed prefabs, at home and in the hardest biome of their world spawns, 2.2) and the name-validation pass; built lazily once per `ZNetScene` instance and rules object (never cached when built without one) |
| `ServerRules.cs` | copy of Forge Idol Upgrades' (`Current`, request/answer, push on change, Debug `TestRules`), plus the `Pending` state (4.4) |
| `PlayerCheck.cs` | copy of Forge Idol Upgrades' (messages name this mod) with the verdict from `NetworkGate.PeerCompatible`, the reason from `NetworkGate.PeerProblem`, and a `NetworkGate.PeerStateChanged` subscription in `Start` / `Stop` (4.5) |
| `BossLadder.cs` | token → boss order map from `ZNetScene.m_prefabs` (lazy, rebuilt when `ZNetScene` changes) |
| `Standing.cs` | pure `Compute(enemyStats, ladder, rules)` (with the listing filter), `Encode`/`Decode`, `PublishNow`, `Withdraw`, progress messages (E5), Debug `TestOverride` |
| `StandingCache.cs` | per-player decoded standing, refreshed at most once per second; purge of destroyed players |
| `Attitude.cs` | `Attitude` enum (`Hostile`, `Afraid`, `Provoked`, `Routed`), `AttitudeFacts`, pure `Decide(...)` and `Outclasses(...)`, `Judge(MonsterAI, Player)` |
| `CreatureState.cs` | per-creature memory: last owned time, next check time, cached facts (prefab hash, home level, spawn level, elite, rank, token hash, exemption, night hunter), hunt stand-down, rout deadline, fear player and last sensed time, cornered time, flee-frame flags (first frame, broken, `FleeAlert`); `Dictionary<int, CreatureState>` by instance id, purged of destroyed creatures every 30 s |
| `CreatureKeys.cs` | ZDO key hashes and read/write helpers (provokers array in place, rout) |
| `CreatureCheck.cs` | the owner's side of the `UpdateAI` prefix: gain detection, the once-per-second check (7.3), hunt stand-down, target drop, calming down |
| `Fear.cs` | the fear (2.5 piece 2): pure `Qualifies`, the check's start / keep / end, `Stop`, and the fear frame (cornered count, then the shared flee frame) |
| `FleeFrames.cs` | the shared rout and fear frame (base update through the non-virtual delegate, no target, alerted, `Flee`), the calm at the end of a run (`Calm`: takes back the alert the frames held, G7), and the flee timer reset |
| `Cornered.cs` | pure `Advance` (time within `CorneredRange`) and `Strike` (provoke, target, alert) (2.6) |
| `Provocation.cs` | hits, near misses, the provocation record, the E3 rule (2.6) |
| `BossFights.cs` | positions of alerted bosses, once per second |
| `Rout.cs` | leader death handling, `Rout` routed RPC register/send/receive (guarded), `Apply`, recent routs |
| `Compat.cs` | overlapping-mod warnings (6.3) |
| `SelfTests.cs` | `#if DEBUG` in-world tests (7.6) |
| `Patches/BaseAIPatches.cs` | `CanSenseTarget`, `FindEnemy`, `OnDeath`, `InStealthRange` |
| `Patches/MonsterAIPatches.cs` | `UpdateAI`, `HuntPlayer`, `RPC_OnNearProjectileHit` |
| `Patches/CharacterPatches.cs` | `RPC_Damage` |
| `Patches/GamePatches.cs` | `RPC_RegisterKill` |
| `Patches/PlayerPatches.cs` | `OnSpawned` |
| `Patches/ZNetPatches.cs` | `Awake`, `OnNewConnection`, `RPC_PeerInfo`, `Update`, `OnDestroy` |
| `README.md`, `CHANGELOG.md`, `TESTING.md`, `icon.png` | step 5 of the workflow |

`Plates.cs` (the label, E1) and `Patches/EnemyHudPatches.cs` were removed on 2026-09-30 (decision 36).

### 7.2 Harmony patches

All patch bodies, and every RPC handler, catch their own exceptions and call `PatchGuard.Report(site, e)`. No
transpiler. Unity null checks with `== null`, never `?.`/`??` on Unity objects.

| Target | Kind, priority | Purpose | Cost on hot paths |
|---|---|---|---|
| `BaseAI.CanSenseTarget(Character, bool)` (overloaded: `new[] { typeof(Character), typeof(bool) }`) | postfix, normal | Afraid/Routed toward a player → false (2.5) | called per candidate in `FindEnemy` (every 2-6 s per creature) and by `AnimalAI`: exits at `!__result`, `!(__instance is MonsterAI)`, `!(target is Player)`, not owner; then cached facts, a ZDO long read, an in-place scan of the provokers array (≤ 4 entries) and a cached standing lookup; no allocation |
| `BaseAI.FindEnemy()` | postfix, normal | hunt fallback returned an afraid-of player → the closest hostile-toward player within 200 m, or null (2.5 piece 5) | every 2-6 s per creature: exits at `!(__result is Player)`, not owner |
| `MonsterAI.HuntPlayer()` | postfix, normal | night hunter standing down → false (2.5 piece 5), unless the rules changed since the check | several calls per tick per creature: exits at `!__result` (every non-hunter); hunters: one dictionary lookup; standing-down hunters: a few compares (rules, cached facts) |
| `MonsterAI.UpdateAI(float)` | prefix, `Priority.Low`, `bool __runOriginal`, returns `bool` | ownership-gain detection, rout and fear frames (skip vanilla, 2.5, 2.7); once-per-second check (7.3) | **20 Hz × every creature on every game**: non-owner → `IsValid` + `IsOwner` and return; owner → one dictionary lookup and a few float compares, work only once per second, or every frame while routed or running (a fear frame adds one squared distance and a rules read to the flee frame); no allocation |
| `MonsterAI.RPC_OnNearProjectileHit(long, Vector3, float, ZDOID)` | prefix | provoke within `NearMissRange`; an afraid creature farther away ignores the impact (2.6) | per projectile impact per creature in range, owner only |
| `BaseAI.OnDeath()` | postfix | leader death → rout (2.7) | per creature death on its owner |
| `BaseAI.InStealthRange(Character)` (static) | postfix | afraid creatures do not count (2.6, E3) | once per second while the local player sneaks; loops `BaseAIInstances` only when vanilla said true |
| `Character.RPC_Damage(long, HitData)` | prefix | no sneak-attack bonus from an afraid creature that sees the attacker (E3), then provoke (2.6) | per hit on the owner; exits at attacker not a `Player`, victim not a non-exempt `MonsterAI` |
| `Game.RPC_RegisterKill(long, string, int, int, int, bool)` | postfix | republish the standing (after vanilla updated the table); progress messages | per credited kill |
| `Player.OnSpawned(bool)` | postfix | publish the standing on the new player ZDO (local player only) | per spawn |
| `ZNet.Awake()` | postfix | register `Rout` on the new `ZRoutedRpc` | per world start |
| `ZNet.OnNewConnection(ZNetPeer)`, `ZNet.RPC_PeerInfo(ZRpc, ZPackage)`, `ZNet.Update()` | postfixes | rules, join check (grace timer), rule edits once settled, the delayed boss rank message (E5) | `Update`: three bool reads per frame when idle |
| `ZNet.OnDestroy()` | postfix | world end: apply a rule edit still settling; clear `CreatureState`, `StandingCache`, recent routs, `BossFights`, the pending boss rank message | per world end |

The base-update delegate: `AccessTools.MethodDelegate<Func<BaseAI, float, bool>>(AccessTools.Method(typeof(BaseAI),
nameof(BaseAI.UpdateAI)), null, virtualCall: false)`, created once. HarmonyX 2.9.0 in the game has this overload
(checked by reflection on `BepInEx/core/0Harmony.dll`); that it runs other mods' `BaseAI.UpdateAI` patches and the
base body is asserted by `morale.rout` and `morale.calm` (7.6).

`Flee`, `SetAlerted`, `SetTargetInfo`, `SetTarget`, `FindEnemy`, `m_targetCreature`, `m_targetStatic`,
`m_updateTargetTimer`, `m_timeSinceAttacking`, `m_timeSinceHurt`, `m_fleeTargetUpdateTime`, `m_lastFindPathTime`,
`m_spawnPoint`, `m_lastHit`, `EnemyHud.m_huds` (self-tests only) are non-public; `assembly_valheim` is publicized in
the build. No `EnemyHud` patch (decision 36): the alert icon is vanilla's.

### 7.3 Order inside the `UpdateAI` prefix (owner)

```
if (!__runOriginal || !nview valid || !owner) return true
state = CreatureState.Get(__instance)
if (Time.time - state.LastOwnedTime > 0.25) state.OnGained()   // new, or regained after a gap: reset memory
state.LastOwnedTime = Time.time
if (Time.time >= state.NextCheck) Check(state)                 // once per second
if (!state.Exempt && !state.FramesBroken)
    if (state.RoutEnd > Time.time) return FleeFrames.Run(from RoutFrom)                   // skip vanilla this frame
    if (state.FearPlayer != null) return Fear.Frame(...)     // cornered count, then FleeFrames.Run(from the player)
return true
```

`OnGained`: clears the fear, the cornered time, the hunt stand-down and `FleeAlert`, marks the next flee frame as the
first (flee and path timers reset), and sets `NextCheck = now`, so `RoutUntil` and the provocations are read on the
first owned frame.
The cached spawn level is kept (it cannot change).
`Check`: refresh cached facts and exemption (the spawn level is computed at the first refresh that has a
`WorldGenerator`); read `RoutUntil` (set `state.RoutEnd`); exempt, rules pending or mod off → no rout frames, no fear,
no stand-down, `FleeAlert` dropped (the alert is vanilla's now), stop; apply a matching recent rout (2.7); routed → no
fear, stop; hunt stand-down (2.5 piece 5); fear (2.5 piece 2; never once `FramesBroken`, so such a creature, which runs
vanilla every tick, still gets the target drop and the calming down instead of keeping a player it fears as its
target); when not running: the calm of a run that ended (`FleeFrames.Calm`: the rout is over or the fear ended, G7;
the alert kept instead when a player it would fight is within `FearRange` and sensed), target drop (piece 3) and
calming down (piece 6); set `NextCheck = Time.time + 1`
(the first interval after `OnGained` is stretched by a random 0-0.3 s, so creatures loaded together do not all check in
the same frame).
A fear frame first checks that the fear player still counts (else the fear ends, `FleeAlert` kept for the next check's
calm, and vanilla runs), then advances the cornered time; at `CorneredSeconds` it strikes (2.6) and returns true, so
vanilla runs that frame with its new target. Every flee frame sets `FleeAlert` (one bool write).

### 7.4 State and lifetime

| State | Where | Lifetime |
|---|---|---|
| Creature keys (3) | creature ZDO | the creature (world save); time-bounded |
| Standing | player ZDO | the player object; overwritten at spawn, kill, rules change, withdraw |
| `CreatureState` | memory, by instance id | until the creature is destroyed (purged every 30 s), the world ends, or the mod turns off (cleared); owner memory (fear, cornered time, stand-down, rout frames, `FleeAlert`) reset on ownership gain; the spawn level stays for the creature's life on this game |
| `StandingCache` | memory, by player | refreshed once per second; destroyed players purged every 30 s; cleared at world end and on toggle off |
| `BossLadder`, `PrefabTokens`, rules tables | memory | rebuilt on rules change or new `ZNetScene`; cleared on toggle off |
| Handled leader deaths | memory set of ZDOIDs | 10 s |
| Recent routs | memory list | `RoutSeconds`; cleared at world end |
| Alert of a running creature | vanilla `m_alerted` and ZDO `alert` (1.5), written by the owner's `SetAlerted` | raised by the first flee frame, taken back by the calm (2.5 piece 2, 2.7); vanilla's own after that (no plate object of the mod's, decision 36) |
| `Rout` registration | the `ZRoutedRpc` instance | the `ZNet` session (flag checked in the handler) |

### 7.5 Live toggle

- **OnActivated** (also runs at plugin start, before any world exists): `Compat.Warn()`; build rules
  (`ServerRules.Start`: client asks the server, server pushes; home biome, elite and pack tables from name hashes, no
  `ZNetScene` needed; token map and validation wait for `ZNetScene`); register `Rout` on the current `ZRoutedRpc` if
  not yet registered on it; `PlayerCheck.Start()` (server: subscribes to `NetworkGate.PeerStateChanged`, checks every
  ready peer); `Standing.PublishNow()` if the local player exists and the rules are not pending; `SelfTests.Register()`.
- **OnDeactivated** (patches still applied during the call): `SelfTests.Unregister()`; `PlayerCheck.Stop()`
  (unsubscribes, cancels pending checks); `ServerRules.Stop()`; `Standing.Withdraw()` (guarded: `ZNet`, `ZDOMan`,
  local player ZDO valid; skipped at application quit);
  clear `CreatureState`, `StandingCache`, `BossFights`, recent routs; `Rout` handler inactive. Creatures that were
  routing or running from a player on this game go back to vanilla at the next tick (alerted, no target: vanilla finds
  a target or gives up); afraid creatures pick players again at their next search, once they see or hear them. ZDO
  keys stay (time-bounded; they apply again if the mod comes back before they run out). Nothing in `ObjectDB`, prefabs
  or item data is changed, so nothing needs reverting. On a client connected to a server, the framework then tells the
  server that this copy is off (`HelloState`), and the server's join check refuses the player about a second later
  unless `AllowPlayersWithoutMod` is on (4.5); turned on again, the framework says so and the server checks the player
  again.

### 7.6 Debug in-world self-tests (`SelfTests.cs`)

Registered in `OnActivated`, unregistered in `OnDeactivated`. The player is in god mode in the throwaway world
`MCProbe` (Meadows, spawn stones, near dawn). Tests never write a `ConfigEntry`: they use `ServerRules.TestRules`
(an in-memory `MoraleRules`, not clamped) and `Standing.TestOverride` (rank and bonuses fed into the normal publish
path), both cleared in `finally`. Every creature is spawned with `Object.Instantiate(ZNetScene.GetPrefab(name))`
(owned by the test game) and destroyed in `finally`. Before each spawning test, other enemy `BaseAI` creatures within
40 m (world spawns such as deer and boars, all owned by the test game in single player) are destroyed, so they cannot
change a result. Kills by the god-mode player are cheated; the standing counts them anyway (decision 8). Player moves
set the transform and the body position.

| Name | What it does | Asserts |
|---|---|---|
| `morale.rules` | pure checks, no spawn | `Decide` table on synthetic facts: every row of 2.4, incl. shaken, Passive Mobs, exempt, no home biome, rules pending, and **no standing during shaken → Hostile**; default ranks of 2.3 (Boar 3, Greydwarf and Skeleton 4, Troll, Greydwarf Shaman and Brute, Draugr, Skeleton_Swamps and Serpent 5, Draugr_Elite, Wolf and Skeleton_Mountains 6, Fenring_Cultist, Lox and Fuling 7, Fuling Berserker and Seeker 8, Seeker Soldier and Charred Warrior 9, Charred Warlock and Frozen Greydwarf 10, Frozen Greydwarf Shaman 11; Deep North elites, `FallenWarrior` and bosses unlisted); `Outclasses` cases: the user's example (Greydwarf hostile at rank 3, afraid at 4), 2★ Boar at rank 5 → Afraid and at 4 → Hostile; rank 3 + 120 `$enemy_greydwarf` kills → Greydwarf Afraid, rank 2 + 450 → Hostile (one boss at most); rank 8 + 500 → `Greydwarf_Frozen` Hostile; rank 4 + 450 `$enemy_skeleton` → `Skeleton_Swamps` Afraid, `Skeleton_Mountains` Hostile; rank 8 + 150 Charred Warrior kills → Afraid; `MaxBossesSkippedByKills` 0 → kills only offset stars; the whole worked outcomes table of 2.5; biomes: levels of every `Heightmap.Biome` (Ocean, `None` and several flags = 0), the Ocean list = 3, ranks by spawn level (Skeleton at home 4, Mountains 6, Plains 7, Meadows 4; Greydwarf in the Deep North 10; Draugr in the Plains 7; Fuling in the Meadows 7; Surtling in the Ashlands 9; Serpent at sea 5; Troll 5, in the Mountains 7), `BossesAhead` 0 and `EliteExtraBosses` 3, a name in two home lists takes the easiest, an elite in no home list is reported; real world points found with `WorldGenerator` (Meadows, Mountains, Plains, Deep North, Ocean) give their level, a sky point with no loaded location uses its own x, z (NOTE with the points); `Standing.Compute` on synthetic tables (`$enemy_eikthyr` + `$enemy_gdking` → 2; only `$enemy_frozenking` → 0; `$enemy_frozenking_p3` → 8; rank 3 + 120 greydwarf kills → +1 listed, 400 → +2, rank 1 + 120 → known but not listed); listing filter at rank 8 (Boar and Moose not listed, Greydwarf and Charred Warrior listed, Deer never; cap 16, highest first); at rank 7 the Draugr bonus listed (world spawn in the Plains, rank 7, above its Swamp home); standing bytes round trip, withdrawn, corrupt array → no standing; provokers array: add, refresh, prune expired, fifth player drops the entry ending first, 1 s write throttle, rout clear; cornered count (`Cornered.Advance`: counts within the range, strikes at `CorneredSeconds`, restarts outside, 0 = never); fear start and release (`Fear.Qualifies`: sensed within 12 m starts, unsensed never starts, keeps up to 16 m, 5 s unsensed releases, `FearRange` 0 = none); rules wire layout 2 round trip, clamping on read (a `FearRange` of 0 becomes 3), edges not clamped, `TestRules` not clamped (0 allowed), unknown layout and layout 1 refused; `Packs` parsing; 79 home-listed creatures, 24 elites, 12 leaders, every default pack leader an elite, every default name exists in `ZNetScene` with a `MonsterAI`, none always exempt, none in two home lists; `BossLadder` from live prefabs = orders 1..8; `PlayerCheck.Decide` verdicts; the creature plate template has vanilla's `Alerted` and `Aware` icons (G7). NOTE lines: English names, leaders' death animation, world spawns of the variants and the night hunters, raid `army_eikthyr`, player bombs, priority building targets, **senses and flee numbers** of ten creatures |
| `morale.calm` | Greydwarf 4 m in front of the player, facing it | rank 0 → within 4 s `HaveTarget()` and the player `IsSensed()`; rank 4 → within 3 s it runs from the player (`FearPlayer`), no target, alerted; 2.5 s later farther from the player (or a NOTE when vanilla `Flee` found no navmesh path), no target all along, alerted on every frame of those 2.5 s (raised once), ZDO `alert` true (what other games read), the player not sensed, the base update ran (`m_timeSinceHurt` grew) and vanilla was skipped (target timer unchanged); held facing the player: `CanSenseTarget` false, `CanSeeTarget` true, still running, never a target; its plate forced shown (hover timer 0) has the vanilla `Alerted` icon on and the `Aware` icon off (screenshot `alert-icon`); moved 20 m away (`FearRange` + 4 m + 4 m) → it stops running and calms within 3 s (not alerted, ZDO `alert` false, both plate icons off, screenshot `calm-no-icon`), and stays calm 2 s with the noisy player 20 m away; a **silent** player 5 m behind it (facing away) → not noticed, no fear; turned toward him → runs within 2.5 s; `SetLevel(3)` (2★, nerve 6) → stops running and targets the player within 5 s; burn at 16 m (beyond `FearRange`, inside its view range) → alerted, no target, calm again within 7 s after the burn; night hunter at rank 8, 16 m away → no target, stand-down, `HuntPlayer()` false, not alerted; moved to 6 m → it runs from the player; `NightHuntersCanBeAfraid` off → `HuntPlayer()` true at once and it hunts the player within 8 s; **spawn biome**: a Skeleton spawned at the test spot has the spot's spawn level and rank (4 in the Meadows), hostile one rank below, afraid at it; its spawn point (`m_spawnPoint` + ZDO `spawnpoint`, as `BaseAI.Awake` reads it) moved to a Plains point → hostile at 6, afraid at 7; to a Mountains point → hostile at 5, afraid at 6; a Greydwarf's to a Deep North point → hostile at 8 (rank 10); to a Meadows point → afraid only from 4; a sky point → the zone's entrance rule |
| `morale.provoke` | afraid Greydwarfs at rank 4; `TestRules` with `ProvokedSeconds` 5, `NearMissRange` 4, `CorneredRange` 3, `CorneredSeconds` 1, and `FearRange` 0 for the steps that test what a hit does | a 1-damage slash hit from the player → the provokers array has the player's entry, target + alerted within 1 s; 7 s later no target, not alerted; a fire-only hit → provoked and targets the player; a near miss 2 m away (through `BaseAI.DoProjectileHitNoise`) → provoked; 6 m away → not provoked, not alerted, no target; sneak attack with `m_backstabBonus` 3 on one that sees the player → `m_backstabTime` unchanged, turned away → changed; a real flint knife stab on one that watches → no sneak attack, provoked, targets the player. Then with the fear on (`FearRange` 12): a fresh one 5 m away runs within 3 s, a sneak-attack hit on it (alerted) is no sneak attack, and the hit ends its fear at once (it targets the player); **cornered**: held at its spot with the player 1.5 m in front → it runs, then is provoked within 3.5 s, stops running, targets the player, alerted; 5 s later (provocation over) it runs from the player 5 m away again; the player 5 m in front of a held one → it runs, not provoked after 4 s; a **silent** player 1.5 m behind one facing away → no fear, not provoked, not alerted after 3 s |
| `morale.rout` | Troll 15 m away, 3 Greydwarfs within 6 m of it, a Boar and a tamed Greydwarf within 6 m, at a spot where every creature stands free and every Greydwarf has a navmesh path to run away (stage 6 of section 10); rank 0; `TestRules` with `RoutSeconds` 4, `ShakenSeconds` 12, `FearRange` 0 | kill the Troll with a player hit → within 1.5 s each wild Greydwarf has `RoutUntil` > now, `RoutFrom` within 1 m of the Troll's position, empty provokers; Boar and tame have no `RoutUntil`; 3 s later each is farther from `RoutFrom` than at the start, alerted, no target, `m_timeSinceHurt` grew (the base update ran in skipped frames), `Judge` Routed with ZDO `alert` true, alerted on every frame from about 1 s after the kill (raised once); a Greydwarf spawned 5 m from the death point 1 s after the kill gets `RoutUntil` at its first check (recent routs); within 2 s after the rout ends: not alerted and no target although rank 0 (shaken, Afraid, ZDO `alert` false); `FearRange` 12 turned on: the followers within 10 m of the player run from him within 2.5 s, no target; back to 0: they calm within 2.5 s; after `ShakenSeconds`: targets the player within 4 s; a second Troll killed with an attacker-less hit → no rout; the first part repeated through the `Rout` message (`Rout.ForceBroadcast`), and within 2 s after that rout ends both followers are unalerted with ZDO `alert` false wherever they ran (no player needed near them; NOTE with their distance) |
| `morale.stealth` | `Draugr_sleeping` 15 m from the player (outside its 10 m wake range, inside its 30 m view range), asleep, unalerted | rank 0 → `BaseAI.InStealthRange(player)` true; rank 5 (the Swamp Draugr afraid) → false; the Draugr is still asleep at the end (sleeping creatures never run); with `Packs` = `Troll > Draugr_sleeping`, `Rout.Apply` 3 m from it routs nothing, and woken (`Wakeup`) 2.5 s into the rout it is still not routed |
| `morale.publish` | none | after `PublishNow`, the local player ZDO's `Standing` decodes to `Standing.Compute(profile)`; `Withdraw` → rank 255; publish again restores it; the test override (rank 3, 150 Greydwarf kills: listed one boss early) goes through the normal publish path; a kill that raises the boss rank (override 0 → 1 through `Standing.TestKill`) does not show the center message at once but 4-4.5 s later; after Eikthyr's own `m_deathMessage` is shown the way `BaseAI.OnDeath` does, the center text becomes the rank-up message |

### 7.7 Framework or `src/Shared` changes

None left to do. The mod uses the framework as changed in this run (done: `src/Shared/Framework/NetworkGate.cs` and
`ModPlugin.cs`; the client reports whether its copy is on in the hello and through `HelloState`; its in-game items are
N07-N08 in `src/Shared/TESTING.md`): `NetworkGate.PeerCompatible`, `NetworkGate.PeerProblem` and
`NetworkGate.PeerStateChanged` for the join check (4.5), plus `ModPlugin`, `PatchGuard` and `SelfTest`. It has no
`src/Shared` change of its own, so `./tools/Test-Framework.ps1` is not needed for it. It adds no items, prefabs or
pieces, so it has no `[AlwaysOnPatch]` class: every patch follows the live toggle (the `Rout` handler, once registered
on a `ZRoutedRpc`, stays there and returns at once while the mod is off, 4.2). It does not use `ItemKinds`.

---

## 8. Test plan (`TESTING.md`)

Spawn names are prefab names checked in the 1.0.16 dump. Rank for manual tests: a Debug build with
`Debug.ForceBossRank` in `BepInEx/config/MC.Combat.Creatures.Morale.cfg`, or real kills (`spawn Eikthyr`, then kill it:
spawned kills count, decision 8). Commands need `devcommands` in single player or on the host; `goto <x> <z>` reaches
other biomes (the `morale.rules` NOTE lists points of the test world). Creatures spawned with `spawn` count as spawned
where the player stands (2.9). Name plates show only within 30 m and for 60 s after the crosshair passed over a
creature: aim at each creature you check. Default ranks (2.3): Meadows 3, Black Forest 4, Swamp and Ocean 5, Mountains
6, Plains 7, Mistlands 8; elites one more; each star one more. The items below were rewritten on 2026-09-30 for the
fear and the new ranks, and again later that day for G7 (the vanilla alert icon instead of the label; M14 new; after
its review M07 and M14 set up again and T34 new); IDs are kept, new items come at the end of their group.

### Setup

- **S01** Debug build deployed; `devcommands`; `god` for survival while testing; `[MC:ready]` line shows the build id;
  the log shows "Standing: boss rank N …" at spawn.
- **S02** Names used: `Boar`, `Neck`, `Greyling`, `Greydwarf`, `Greydwarf_Shaman`, `Greydwarf_Elite`, `Troll`,
  `Skeleton`, `Draugr`, `Draugr_Elite`, `Wolf`, `Goblin`, `GoblinArcher`, `GoblinBrute`, `GoblinShaman`, `Fenring`,
  `Fenring_Cultist`, `Seeker`, `SeekerBrute`, `Charred_Melee`, `Charred_Mage`, `Greydwarf_Frozen`,
  `Greydwarf_Shaman_Frozen`, `Eikthyr`, `Deer`, `piece_TrainingDummy` (build it with the hammer), `Dverger`.
- **S03** For kill-step tests, note the character's current kill counts (MC Creature Kill and Tame Counts, or the
  "Standing" log line), or use a fresh character.

### Single player

- **T01 (G1, G7)** Rank 3: boars and necks never attack; they run when the player comes within about 12 m and they
  sense him, with the vanilla alert icon while they run; they calm down at about 16 m and the icon goes; far away
  their plates look vanilla; no line or word of the mod's own on any plate.
- **T02 (G1)** Rank 2: vanilla (contrast).
- **T03 (G1)** Rank 4 with 3 Greydwarfs 10 m away: they run but never attack; Rested, bed, no combat music.
- **T04 (G2, E5)** Fresh character: Eikthyr kill → rank 1, no rank-up message (nothing afraid yet), boars attack;
  with `BossesAhead = 0` a second Eikthyr kill → the message 4-5 s later, boars afraid.
- **T05 (G2, E5)** Kill steps at rank 2: Greyling step reached → corner message, Greylings afraid one boss early,
  Boars still attack.
- **T06 (G2)** Kill-step limit at rank 2: two Greydwarf steps → no message, still attacked; rank 3 → afraid.
- **T07 (G2)** Stars: rank 4, 2★ Greydwarf attacks, 0★ runs; rank 5, 2★ Boar afraid.
- **T08 (G2)** Ranks across biomes at rank 6 in the Meadows: Troll, Draugr, Wolf afraid; Fuling, Fuling Berserker
  attack.
- **T09 (G1)** Standing change mid-chase: rank 2 → 3 while a Boar chases: it turns and runs within about 2 s.
- **T10 (G3, G7)** Hit an afraid Greydwarf (caught or cornered): it fights (the alert stays, no second alert sound);
  35 s later afraid again, no alert icon.
- **T11 (G3)** Near misses from 20 m: within 2 m → it comes and fights; a deer 5-7 m away or a spear 10 m away → not.
- **T12 (G3)** A poison-only Ooze bomb from 15 m → it comes and fights.
- **T13 (G3)** Cornered: (a) trapped in a pen with the player within 2-3 m → attacks after about 2 s; (b) chased and
  kept up with → attacks, outrunning → just runs; (c) the player 5 m away → never; (d) sneaking unseen next to it →
  never noticed.
- **T14 (E3)** Sneak attacks: seen → none (and running = alerted = none); unseen crouched from behind → yes; a
  walking approach → heard, it runs; crawling near afraid ones only → the Debug log line, slow rate.
- **T15-T19 (G4, E4, G7)** Routs as before: the alert icon while the followers run, gone within about a second after
  the rout ends (player more than 15 m away); then they are afraid (they run, with the icon, from a close player).
- **T20** Exemptions at rank 8 (Eikthyr and nearby creatures, raid, training dummy, Dvergr).
- **T21 (G1)** Night hunters at rank 7 or more: afraid and running when close; `NightHuntersCanBeAfraid = false` →
  hunt; optional 200 m chase at rank 6.
- **T22** Taming a Wolf at rank 6: food and distance or sneaking; taming pauses while it runs; it never attacks.
- **T23** An afraid Greydwarf fights a tame while the player stays back, runs when the player comes close.
- **T24** Clean log.
- **T25** Personal setting `ShowProgressMessages` (and no `ShowOnNameplates` any more in ConfigurationManager; an old
  config file may keep an unread line).
- **T26** Settings live: a Greydwarf added to `Meadows`, a typo, `RoutSeconds`, `FearRange`.
- **T27** Follower in no home biome list: provoked during a rout, fights after it.
- **T28** Sleeping follower not routed.
- **T29 (G1)** Fear range: never approached from 30 m; runs within 12 m, calms at 16 m or 5 s unsensed; `FearRange 5`.
- **T30 (G1)** Sneaking up on an afraid creature: not noticed until seen or heard; sneak attack from behind.
- **T31 (G2)** Biome by spawn place: Mountains skeleton afraid at 6; Plains skeleton at 7 only; Meadows night
  Greydwarfs stay Black Forest (rank 4); optional Deep North Greydwarf attacks at 8.
- **T32 (G2)** Elites one boss later: at rank 4 the Greydwarf is afraid, the Shaman, the Brute and the Troll attack; at
  rank 5 all four are afraid.
- **T33 (G1, G3)** Fighting beats fear with a tame: it leaves the tame and runs when the player comes close, and a hit
  ends the fear at once.
- **T34 (G4, G7)** End of a rout: (a) with a bed 15 m away and the player 15-25 m away, the icon goes within about a
  second, also on a follower that then goes for the bed (unalerted, as in vanilla); (b) `ShakenSeconds = 0` with the
  rank 0 player 8-10 m from a follower when the rout ends: it fights him with its alert on all along (no second alert
  sound, no aware icon in between).

### Multiplayer

- **M01 (G5, G2)** P1 rank 8, P2 rank 0 together: P2's Greydwarfs (and P1's) attack P2, never target P1, and do not run
  from P1 while P2 is near; with P2 50 m away they run from P1.
- **M02 (G4, G5)** Rout across games.
- **M03 (G3, G5)** Provocation survives an ownership change; afterwards afraid again.
- **M04 (G3)** Per-player provocation (cornering or a hit by P1, a near miss by P2; the second part with 5 minutes
  between the hits).
- **M05 (G4)** Rout across an ownership change.
- **M06 (G5)** Server settings for everyone (`KillSteps`, then `FearRange = 20` logged again on the client).
- **M07 (hand-off)** Player without the mod: refused, or allowed with `AllowPlayersWithoutMod` (the vanilla player,
  crouched and still about 18 m to one side of a Greydwarf that runs from P1, also sees its alert icon and its end;
  every creature is hostile toward the vanilla player, so they must stay beyond 12 m, where they do not stop the run,
  and after the calm it may come for them as in vanilla: aware icon, then alert icon).
- **M08 (G5)** Turned off while connected.
- **M09 (G5)** Joins with the mod turned off. **M12** another network version (optional).
- **M10** Dedicated server.
- **M11 (G6)** The server turns the mod off: running creatures stop, vanilla; back on: afraid again.
- **M13 (G1, hand-off)** A creature running from P1 changes owner mid-run: it keeps running (or starts again within
  a second), never attacks, calms at about 16 m (the icon goes within a few seconds).
- **M14 (G7)** Alert icon on every screen: both players rank 8 (a player the creature would fight near it would stop
  the run, decision 29), P2 still about 18 m to one side of the creature (beyond the fear's 16 m, inside the 30 m
  plate range), P1 walking in at a right angle; a creature running from P1 (controlled by P2's game, then by P1's)
  shows the icon on both screens and loses it on both when it calms; the same for a routed pack (rank 0 for both,
  15-25 m from the followers) and the rout's end.

### Cross-mod

- **X01** Sneak Ambush (its X01), at rank 6: E3 in both mods (an arrow at an afraid Greydwarf that watches from
  beyond 12 m: no sneak attack); smoke: an afraid creature does not run from a smoked player, a hit from the smoke on
  a two-star Greydwarf (it survives) provokes and reveals; standing in the smoke never corners.
- **X02** Compendium Encyclopedia: an afraid creature seen running (alert icon) and calm counts as met, normal name.
- **X03** Creature Kill and Tame Counts: the kill count used by T05.
- **X04** Sleep Through the Day: afraid creatures, running or not, do not block a day sleep.
- **X05** Tower Shield Wall: a bash provokes (and ends the fear), also in NG+ (its C06, T23); the creature is reached by
  sneaking up on it or catching it.
- **X06** Harpoon Hooks Tames: harpooning an afraid wild Boar provokes it; hooking a tame provokes only those within
  4 m.
- **X07** Dual Wielding: off-hand hits provoke.
- **X08** Weapon Moveset: sneak roll attack seen / unseen, jump attack provokes (a Greyling at rank 3).
- **X09** Another creature AI mod: one warning.

### Live toggle, disabled, log

- **L01 (G6)** Off: standing withdrawn, running creatures stop (still alerted, vanilla from there), creatures notice
  the player normally; on: afraid again within about 2 s (alert icon while they run, none once calm).
- **L02** Off during a rout: they stop running.
- **L03** `Enabled = false` + restart.
- **L04** Clean log over the whole session.

---

## 9. Open questions and unverified

### Open questions for the user

1. The home biome lists and elites (2.3): placements such as the Ocean counting as the Swamp's level (serpents after
   Yagluth), `Writhan`, `Wraith`, `Bjorn`, `Abomination` and `Unbjorn` as elites, `Skeleton_Poison` and `Fenring` as
   rank-and-file, the Deep North, Mistlands elites and Ashlands elites never afraid with the defaults.
2. Two bosses ahead with elites +1 (decision 25): with kill steps still bringing a kind one boss early, a Kall-killer
   frightens the Ashlands rank-and-file only with 100 kills of that kind. Right, or should kill steps stop there?
3. The spawn biome rule (decision 28): the harder of home and spawn biome. Or should a creature sent down into an
   easier biome (night Greydwarfs in the Meadows, night hunters) count as that easier biome's creature?
4. The fear (decision 29): `FearRange` 12 m, running until 16 m or 5 s unsensed; a creature that fights, or senses near
   it, a player it would attack does not run from the players it fears. Right distances? Should a strong player's
   presence scare creatures away from a weaker friend instead?
5. Cornered (decision 30): 3 m for 2 s while it runs. Too easy, too hard?
6. Kill steps 100 / 400 and "at most one boss early": right amounts? Should kills of shared kinds (all skeletons;
   greydwarfs and frozen greydwarfs) and of your own tames count, as the game's kill table does?
7. Should the rout need anything from the killer (for example that the killer outclasses the followers), or stay "any
   leader kill by a player or tame"? Default packs beyond the greydwarfs (E4): keep all?
8. Progression per character across all worlds (chosen) or per world?
9. Night hunters afraid of strong players (chosen, `NightHuntersCanBeAfraid`), or keep them hunting everyone? Should
   raids also be afraid of strong players (Later)?
10. Taming is back to the normal game (decision 33): afraid tameable creatures run from a close player they sense.
    Keep, or make tameable kinds tolerate a player who carries their food?
11. A player who turns the mod off while on a server is disconnected about a second later, with only "Incompatible
    version" on their screen (the framework now reports it; the same for every Combat mod of this run). Acceptable, or
    should the framework warn in the MC Mods panel before a required mod is turned off on a server that has it (one
    change for every MC Both mod)?
12. With no plate label (G7, decision 36), a creature that is afraid of you but not running looks like any other: you
    learn it is afraid when it runs from you or never comes for you. Enough, or is some other vanilla-looking hint
    wanted (for example in the hover text)?

### Unverified names and values (and how to verify)

- **The fear in real play** (2.5 piece 2): how far and how well creatures run with vanilla `BaseAI.Flee` from a
  player (a fresh world has few navmesh tiles; the first in-world run found paths for only 3 of 9 rout directions near
  the spawn stones), whether the 12 m / 16 m / 5 s numbers feel right, and how often an afraid creature wanders back
  and runs again. `morale.calm` notes how far the Greydwarf ran in 2.5 s; T29 in game.
- **Dungeon spawn level**: a creature made by a dungeon's spawner is judged by the entrance of its zone's location
  (`Location.GetZoneLocation`), found only while that location is loaded (it is, while the dungeon is); otherwise the
  x, z of the zone. T31 does not cover a dungeon; a burial chamber skeleton at rank 4 (afraid) and a Swamp crypt
  skeleton at rank 4 (attacks) would.
- **Creatures that cannot run on land** (`Leech`, `Serpent`, `m_avoidLand`): the fear frames skip vanilla's "move to
  water" step while they run; vanilla `Flee` paths with their own agent type. Unverified in game.
- **Non-virtual base call** (`AccessTools.MethodDelegate` with `virtualCall: false`) runs the base `BaseAI.UpdateAI`
  body and other mods' patches on it: the overload exists in HarmonyX 2.9.0 (reflection); behaviour asserted by
  `morale.rout` and `morale.calm` (`m_timeSinceHurt` grows in skipped frames).
- **How far back the kill table goes**: which game version introduced `m_enemyStats` (older boss kills are not in it).
  The Mark of Oden and MC Creature Kill and Tame Counts assume the Call to Arms update; unverified.
- **Placement of some variants** (home biome guessed from the prefab name; not in the world spawn table, `morale.rules`
  NOTE): `Skeleton_Meadows`, `Skeleton_Mountains`, `Bat_Swamp`, `BlobFrost`, `Leech_cave`, `Ghost` (burial
  chambers). Their spawn biome now decides whenever it is harder, so a wrong home list only matters when the real
  biome is easier.
- **External GUIDs**: TruePassiveMobs `com.lhoffl.TruePassiveMobs` and The Mark of Oden `picsoul.valheim.markofoden`
  come from the research brief's source read (re-check in their `Plugin.cs`); FearMe, Odin's Ótti, CowardlyGreydwarfs,
  FleeOnSight and Monster AI Tweaks GUIDs are unknown (name matching covers them).
- **Hens**: whether untamed `Hen` exist in normal play (probably only tamed ones hatch); in the Meadows list anyway.
- **Timing of the standing reaching other games**: one ZDO sync after spawn (a newly joined player is treated as
  vanilla for a moment). M01 observes it; the join check does not depend on it.
- **Refusal timing** ("about a second after joining, usually before the character appears"): the 1 s grace starts at
  the server's `RPC_PeerInfo`; how long a client then takes to spawn depends on its loading. M09 observes it.
- **The alert icon in play** (G7): that a running creature's icon shows on a non-owner's screen within a moment and
  goes with the calm (M14; the self-tests have one game only, so they check the ZDO `alert` key that other games
  read, and the plate icons on the owner's own screen); how BetterUI's custom alerted status shows it (6.3).
- Settled by the first in-world run (NOTE lines, 2026-09-30): plate range 30 m and the label layout (the label is gone
  since decision 36), the English names
  of the Charred, Fuling and elite creatures, no default leader has a death animation, the night hunters' spawn table,
  raid `army_eikthyr` (Neck and Boar, not after `defeated_eikthyr`), the player bombs (`BombOoze` poison only), the 110
  priority building targets, the burning time (5 s).

---

## 10. Implementation notes

**In short**, the code differs from sections 1-9 in these points (each is explained in its stage below). Stages 1-7
describe the first version (rank lists per boss, calm creatures that only ignored the player); stage 8 is the rework of
2026-09-30 that sections 1-9 now describe, and the points below that stage 8 replaced are marked so; stage 9 (the same
day) removed the name plate label for the vanilla alerted state (G7), so what stages 2-7 say about the label, `Plates`
and `ShowOnNameplates` is history:

- Other mods (6.3) are looked for at world start, not at plugin start.
- A client that gets unreadable server rules and has none yet stays pending (every creature hostile) instead of using
  its own settings.
- The kill-step corner message (E5) shows only for kinds whose bonus is listed (can still change an outcome).
- A sleeping creature is never cornered, and never runs (stage 2; the first version's "closed at least 0.1 m" rule went
  with the walk-up test in stage 8).
- `Decide` step 6 (shaken) also needs `RoutUntil > 0`; the `HuntPlayer` postfix also checks ownership; dead, ghost and
  debug-fly players never count.
- The name check (5.1) also warns about names that are not creatures that attack players, names in several home
  biome lists and elites in no home list; a dedicated server runs it when it sends its rules.
- The self-tests stage the world more than 7.6 says and assert more (stage 3).
- `TESTING.md` follows section 8 with a few corrections and additions (stage 4).
- A hit on a routed or shaken follower that is in no home biome list provokes it (2.6 skipped every creature without
  a rank); a recent rout is not applied again to a follower already routed by the same rout on another game; while the
  rules are pending, a creature routed by another game gets no rout frames (stage 5).
- Name plates show within 30 m, not 10 m (the game's `EnemyHud` object); a rules change ends a night hunter's
  stand-down at once; `morale.rout` picks its scene where the followers can run (stage 6).
- The boss rank message waits 4.5 s (the boss's own death message replaced it at once); feeding a wild tameable
  creature never corners it (replaced in stage 8: cornering now needs the creature to run); cornering ignores dead,
  ghost and debug-fly players; a follower asleep at a rout is never routed by it; a night hunter keeps chasing a
  hostile player at any distance; no extra target from a hit in a Passive Mobs world; rule edits apply once they stop
  for 0.75 s; the client ignores rules while off, the server pushes only to compatible players, and each new set of
  server rules is logged; kill bonuses make up for at most 2 stars, and an unseen approach without sneaking still gets
  the sneak attack on a calm creature (documented limits; the second one mostly gone in stage 8: a walking player is
  heard and the creature runs) (stage 7).
- The fear check (2.5 piece 2) looks at the sight of players the creature would fight only when a player it fears
  qualifies (same answer, and a creature that can never be afraid never pays for a raycast there) (stage 8), or when a
  run ends, to keep the alert for a fight instead of calming (stage 9).

Built in stages. **Stage 1 (foundation), done:** csproj metadata (`ModIdea`, network version 1, the notes of 4.8),
`Plugin.cs` (every setting of section 5, the `OnActivated` / `OnDeactivated` order of 7.5), `MoraleRules.cs` (snapshot,
wire layout 1 with clamping, parsed rank / pack / kill-step tables from name hashes), `ServerRules.cs` (with `Pending`,
`Version` and the Debug `TestRules`), `PlayerCheck.cs` (`PeerCompatible`, `PeerProblem`, `PeerStateChanged`),
`Compat.cs`, `Patches/ZNetPatches.cs`, and the network half of `Rout.cs` (registration once per `ZRoutedRpc`, `Send`,
the guarded handler, Debug `ForceBroadcast`). `Standing`, `CreatureState`, `StandingCache`, `BossFights`, `Plates` and
`SelfTests` exist only with the calls the life cycle makes (publish, withdraw, clear, register); their bodies come with
the AI stage.

Deviations from the text above (small, each for a reason):

- **Other mods (6.3)** are looked for at world start (`ZNet.Awake` postfix) and in `OnActivated` only when a world
  already exists, not in every `OnActivated`: the first activation runs inside the plugin's own `Awake`, when BepInEx
  has not yet put the plugins that load after it into `Chainloader.PluginInfos` (the same reason as Breeding Star
  Inheritance's `Compat`). One set of warnings per activation.
- **Unreadable server rules (4.4):** the client keeps the last good rules, and with none yet it **stays pending**
  (every creature hostile) instead of falling back to its own settings, so a client never plays by its own gameplay
  settings on a server that has the mod (decision 24; the same as Tower Shield Wall). Bytes left over after the last
  field also count as unreadable. The same rules sent again (for example the push after the server's copy turns on
  plus the answer to the client's request) keep the rules object: nothing is rebuilt or published again.
- **Parse problems** (an unreadable kill step, a number outside 1-100000, more than 5 steps, a pack without exactly
  one `>` or without a leader or follower) are logged as one Warning per new text ("Some of your settings could not be
  read: ..."), besides the unknown-name check of 5.1. The bad part is skipped (a number is brought into range).
- **Setting descriptions (5.1):** the rank lists say "prefab names, as used by the spawn command" and explain "helped
  kill" ("hit it at least once before it died"); lengths and times say their unit; personal settings end with "Each
  player's own choice." (as Forge Idol Upgrades).
- **Join check log (4.5):** the refusal also says "Their game shows "Incompatible version"." (as Forge Idol Upgrades);
  the "allowed" warning reads "<player>: their game <reason>. AllowPlayersWithoutMod is on, so they may play: creatures
  behave as in the normal game toward them, and the creatures their game controls attack everyone the normal way."
- **Rules version:** `ServerRules.Version` goes up at every change of the rules in force (own settings, server rules,
  `TestRules`, activation, a new session), for the caches of 7.4 keyed on the rules (name check, listed tokens). Every
  change also calls `Standing.PublishNow()` while the mod is on.

**Stage 2 (features), done:** every goal, extra and mechanism of section 2 and every patch of 7.2. New files:
`Attitude.cs` (enum `Attitude`, struct `AttitudeFacts`, static class `Attitudes` with `Decide`, `Outclasses`, `Judge`),
`CreatureKeys.cs`, `BossLadder.cs`, `PrefabTokens.cs`, `Cornered.cs`, `Provocation.cs`, `CreatureCheck.cs`, and
`Patches/BaseAIPatches.cs`, `MonsterAIPatches.cs`, `CharacterPatches.cs`, `GamePatches.cs`, `PlayerPatches.cs`,
`EnemyHudPatches.cs`. `Standing`, `StandingCache`, `CreatureState`, `BossFights`, `Plates` and `Rout` are complete. The
Debug hooks of 7.6 exist (`ServerRules.TestRules`, `Standing.TestOverride`, `Rout.ForceBroadcast`, `Plates.TagFor`,
`Rout.LastAppliedHere`); the self-tests themselves come in the test stage. Nothing was run in the game yet.

Deviations and additions of stage 2 (small, each for a reason):

- **File layout (7.1):** C# enums cannot hold methods, so the decision lives in `Attitudes` next to the `Attitude` enum,
  with its inputs in an `AttitudeFacts` struct (the self-tests fill it directly). The hit, near-miss and provocation
  code is in `Provocation.cs`, and the owner's side of the `UpdateAI` prefix (gain detection, once-per-second check,
  hunt stand-down, target drop, calming down) in `CreatureCheck.cs`, so the patch classes stay thin. `Judge` reads its
  inputs in `Decide`'s order and stops reading once the answer is known (same answer, less work).
- **Progress messages (E5):** the corner message is shown only for kinds whose bonus is **listed** (can still change an
  outcome); a kill step reached for a kind that is already calm through the boss rank alone would otherwise say "They
  will leave you alone sooner" for nothing. The boss-rank message is unchanged.
- **Cornered (2.6):** "walked up" also needs the player to have closed at least 0.1 m since the previous check. When
  both moved almost nothing (a player who stands up from a crouch, or comes back into the creature's sight, without
  moving) the text left the case open; this way it does not count, like a creature walking up. A **sleeping** creature
  is never cornered (it sees nobody walk up; vanilla wakes it when a player comes close).
- **Rout wipes the anger (2.7):** `Provokers = [1, 0]` is written only when the array has entries, so a creature that
  was never provoked gets no extra key. Same meaning.
- **Broken rout frame (2.7):** an exception before the base call returns true (vanilla runs that frame, as written),
  and also marks the creature `RoutBroken`, so a broken path is not retried every frame.
- **Name check (5.1):** besides unknown names, one Warning also lists names that are prefabs without `MonsterAI` (they
  could never be calm) and names in several rank lists; it is logged again only when its text changes. A dedicated
  server has no local player and simulates no creatures, so it runs the check when it sends its rules (a player's
  request, a push after a change).
- **Withdraw (2.2):** skipped when nothing was ever published on this player ZDO (other games already see no standing);
  otherwise it logs "Standing withdrawn: other players' games treat you as in the normal game."
- **Standing log line (2.2):** "Standing: boss rank N (Boss name), kill bonuses: Greydwarf +1." with localized creature
  names, "forced for testing" when `ForceBossRank` or the self-test override is in use, and "none that matter" when
  nothing is listed. A cap above 16 listed kinds is logged once at Info.
- **Players counted (2.5, 2.6):** dead, ghost and debug-fly players are ignored by the hunt stand-down, the hunters'
  fallback, the calm-down check and cornering (vanilla never senses or hunts ghost and debug-fly players).
- **`HuntPlayer` postfix:** it also checks ownership, so a stand-down left over from an earlier ownership never
  applies on a game that no longer owns the creature.
- **`Decide` step 6** also needs `RoutUntil > 0` (a creature that was never routed is never shaken). No change in the
  game (the server clock is far past `ShakenSeconds`); it keeps the pure function right for synthetic clocks in tests.
- **Plate label (E1):** font 0.8 × the name's size, auto-size off, placed 1 unit under the bottom edge of the plate's
  `Health` child, whatever the pivots (unverified until the `morale.calm` screenshot and T01).

**Stage 3 (self-tests), written:** `SelfTests.cs` has the six tests of 7.6 (`morale.rules`, `morale.publish`,
`morale.calm`, `morale.provoke`, `morale.stealth`, `morale.rout`), registered in `OnActivated` and unregistered (with
every test hook reset) in `OnDeactivated`. They build cleanly but have not run in the game yet (`Test-InWorld.ps1` was
not run in this stage). Two small Debug hooks were added for them: `Plates.TestShow` (labels shown without writing
`ShowOnNameplates`) and `Standing.LifetimeKills` made internal (the expected standing in `morale.publish`).

How the tests stage the world (each for a reason):

- **A test's piece of world:** the world spawner is paused while a spawning test runs (`SpawnSystem.m_nospawn`, the
  vanilla `nospawn` switch, put back after), other enemy creatures within 50 m (60 m for `morale.stealth`) are removed,
  and the player's position, health and hands (a weapon given for a step) are put back at the end, also after a
  timeout. Reason: a world spawn or a creature left by another test would change a result.
- **Spots** are picked in the camera's direction, turned by 30 degree steps when the spot is under water, in or near a
  no-monster area or (when the step needs it) out of sight of the player. The calm Greydwarf stands 4 m away (7.6 says
  5 m) so that sight at spawn does not hang on terrain.
- **Noisy player:** where a step needs vanilla sensing whatever the creature's facing (rank 0 targeting, 2 stars, the
  end of the shaken period, the night hunter with `CalmNightHunters` off), the player gets `AddNoise(40)` every frame, so
  a hostile creature hears them. It also makes the calm checks stronger: the calm creature hears and sees the player and
  still does not sense them.
- **Pinned creatures:** for the sense-vs-see check, the plate label, the sneak attacks, the knife stab and cornering,
  the creature is held at its spot (transform and body) facing the player, or turned away, every frame.
- **Test Trolls** drop no loot and leave no ragdoll (`CharacterDrop.SetDropsEnabled(false)` and an empty
  `m_deathEffects` on that instance only), so the kills leave nothing behind.

Deviations from the test table of 7.6 (small, each for a reason):

- **`morale.rout`:** the tame Greydwarf is made just before the kill: made with the pack 2 s earlier, it fights the wild
  Greydwarfs (hurt timers, even a death) and spoils the timing checks. Before the rout ends, the player moves to the
  middle of the scattered pack and any follower more than 20 m away is moved to 15 m (Note): calming down needs a player
  within the creature's view range (2.5 piece 5), and a pack that fled 25 m from a leader 15 m away can be out of it. The
  shaken check waits up to 2 s after the rout ends (7.6 says 1.5 s): the once-per-second check that un-alerts it comes
  within 1 s of the end. Extra asserts: the rout lasts `RoutSeconds`, the follower the player hit before the kill has its
  provocation wiped, and vanilla `MonsterAI.UpdateAI` is skipped in rout frames (its target timer does not move) while
  the base update runs (`m_timeSinceHurt` grows).
- **`morale.provoke`:** the near misses go through `BaseAI.DoProjectileHitNoise` (what every projectile impact calls,
  1.3) instead of invoking the `OnNearProjectileHit` RPC directly. Added: a real flint knife stab (`Humanoid.EquipItem`,
  `Humanoid.StartAttack`, vanilla `Attack` to `Character.Damage`, sneak-attack bonus ×6) on a calm Greydwarf that watches
  the player: no sneak attack, provoked, it targets the player (the player animator's `knife_stab` trigger is checked
  first). The "creature walked up" step also asserts that the check saw the player close but did not count the time.
- **`morale.calm`:** the night hunter step also asserts `HuntStandDown` and `HuntPlayer()` false, then true again with
  `CalmNightHunters` off. The burn uses the vanilla path of a fire hit (`Character.AddFireDamage`, which adds the
  burning status effect with its damage).
- **`morale.rules` extras:** the whole worked outcomes table of 2.5, the default list sizes (79 ranked creatures, 12
  leaders), no default name that is always exempt (boss, hunter, training dummy, Dvergr), and the plate children the
  label needs (`Health`, `Name`).
- **`morale.publish`** also checks the cache after a withdraw and a publish, and the test override path.

The unverified values of section 9 are settled by NOTE lines in the log: the English names of the Charred, Fuling and
elite creatures (`morale.rules`), whether a default leader has a death animation, the world spawn tables of the
variants placed by name only and of the night hunters, what raid `army_eikthyr` sends and needs, the damage types of
the player bombs (T12), the priority building targets (decision 11), the burning status effect's duration and the
Greydwarf's fire modifier (`morale.calm`), and the plate layout numbers with a screenshot (`morale.calm`). The
non-virtual base update is asserted by `morale.rout`. The mod itself uses no animator trigger.

**Stage 4 (documentation), done:** `README.md` (features, every setting of `Plugin.cs` with its default, multiplayer,
good to know, compatibility), `CHANGELOG.md` (0.1.0) and `TESTING.md` (section 8). Smoke test and in-world self-tests
not run yet.

Deviations of `TESTING.md` from section 8 (small, each for a reason):

- **Setup:** the item dump holds weapons only, so the arrows (`ArrowWood`), the food (`Mushroom`, `RawMeat`) and the
  raid name (`army_eikthyr`) are marked unverified; every creature name used is checked in the dump. The setup also
  names `nospawn` (clean tests) and `setkey defeated_goblinking` (Fuling night hunters, key checked in the spawn table).
- **T13** says rank 1 (a Boar is calm only from rank 1). **T20:** a training dummy never attacks, so "still hits you"
  became "shows no calm line and takes your hits as usual". **T06** also expects no corner message (at rank 0 the
  Greydwarf bonus is not listed, stage 2). **T15** names the Debug log line of the sender.
- **Split:** the other-network-version refusal is its own optional item **M12** (it needs a second build), so M09 can
  be ticked alone.
- **Added:** **T25** (personal settings `ShowOnNameplates` and `ShowProgressMessages`), **T26** (settings apply live,
  the name-check warnings), **X07** Dual Wielding (= its X06: off-hand hits provoke), **X08** Weapon Moveset (= its X04
  and X05: sneak roll attack and jump attack), **X09** (the warning for an overlapping creature AI mod, 6.3). The live
  toggle, disabled and clean-log items (L01-L04) have their own section.
- **README names:** the player-facing creature names in the README tables (for example "Charred Warlock", "Fuling
  Berserker", "Writhan") are the names used elsewhere in this repo; the English strings are unverified (section 9), the
  prefab names in the configuration table are exact.

**Stage 5 (conformance review), done:** every goal, patch of 7.2, setting of 5.1, network message and ZDO key of 4.2-4.3,
self-test of 7.6 and `TESTING.md` item was checked against the code; `README.md` and `TESTING.md` use the code's real
names, defaults and messages. The Debug and Release builds of the project are clean. Nothing was run in the game.

Fixes (each changes behaviour only in the case named):

- **Unranked followers can be provoked during a rout (2.6, 2.7).** 2.6 skipped provocations of creatures without a
  rank ("it can never be calm"), but a routed follower is calm toward everyone until its shaken time ends, whatever
  its rank. A follower missing from the rank lists (custom `Packs`, or a rank list edited) that a player hit during
  the rout was then dropped again at every check and never fought back. The skip now applies only when the creature
  is neither routed nor shaken (`RoutUntil + ShakenSeconds` passed). New test **T27**.
- **The same rout is not applied twice after an ownership change (2.7, recent routs).** Every game computes the rout's
  end on its own clock (a client's clock trails the server's by the network trip), so the new owner's recent-rout
  entry ended a little later than the `RoutUntil` the old owner wrote, and the new owner applied the rout again. That
  wiped the anger of a player who hit a follower during the rout and picked a new flee point. A recent rout is now
  skipped when the follower's `RoutUntil` is at most 2 s older than it. **M05** now also covers the hit.
- **Rules pending means no rout frames (4.4, decision 24).** The once-per-second check read `RoutUntil` before its
  pending test, so a client still waiting for the server's rules (or that got unreadable ones) ran rout frames for a
  creature routed by another game. The check now clears the rout end in that case, as it does for exempt creatures.
- **Cornering after a pause (2.6).** After a pause of the check (exempt, pending, routed), the positions of the last
  check before the pause are dropped as well, so a player's position from before a rout never counts as "walked up".
- A few code comments were put back into caveman speech.

**Stage 6 (first in-world self-test run).** `Test-InWorld.ps1` (throwaway world, god mode, `StaminaRate 0` world
modifier, player at the Meadows spawn before dawn of day 1, build `3cda33b+dirty`): `morale.rules`, `morale.publish`,
`morale.provoke` and `morale.stealth` passed; `morale.calm` failed 4 of 37 checks and `morale.rout` 1 of 58. The mod
logged no warning or error of its own (none at quit either). Causes and fixes:

- **Plate range (test and docs).** `morale.calm` moved the Greydwarf 15 m away and waited for its plate to be dropped;
  the plate stayed, so the label was not dropped and no new plate was made (3 checks). `EnemyHud.m_maxShowDistance` is
  10 in code but 30 m in the game's `EnemyHud` object (the `morale.rules` NOTE "plate: shown within 30 m" is that
  runtime value). The test now reads `m_maxShowDistance` and moves the creature 5 m beyond it; the check says how far
  the creature really was. The design (1.10, E1, 2.5 UI, 5.1, 7.2, 8), `README.md`, the `ShowOnNameplates` setting's
  description, `Plates.cs` and `TESTING.md` said 10 m; they now say 30 m, and T01 walks 40 m away (20 m kept the old
  plate, so T01 would not have tested the new-plate label).
- **Night hunter stand-down after a rules change (mod).** With `CalmNightHunters` turned off, `Judge` saw the new rules
  at once (cached facts are read again on a rules change) and the night hunter targeted the player, but the
  `HuntPlayer` postfix kept returning false until the next once-per-second check cleared `HuntStandDown` (1 check:
  "HuntPlayer is true again", asserted right when it targeted the player). The postfix now keeps the stand-down only
  while `CreatureCheck.StandDownHolds`: mod live, rules not pending, `CalmNightHunters` on, creature not exempt with the
  rules in force (`EnsureFacts`, two compares while the facts are fresh). New assert: `HuntPlayer()` true right after
  the rule is turned off.
- **A follower that did not run (test setup).** Greydwarf 1 (spawned 4 m right of the Troll) was 3.86 m from the death
  point at the kill and 3.95 m 3 s later, while its rout frames ran (the base update and skipped-vanilla checks passed
  for it) and another follower ran more than 20 m. Rout frames call vanilla `BaseAI.Flee`: 10 random points 25 m away
  within 45 degrees of "away from the death point", the first with a full navmesh path (`HavePath`) on dry ground;
  none = a random point with no path check, and `MoveTo` stands still when `FindPath` fails (a failed result is kept up
  to 5 s for the same point; a new point every 2 s). The scene spots were only checked for water and wards: the test
  area (the spawn stones, big rocks and trees) can put a follower inside a boulder or tree (`Ground` reads terrain
  only), and a fresh world has no navmesh tiles yet (built on demand, one 32 m tile at a time; `HavePath` is false
  until then). The log cannot tell which of the two held Greydwarf 1; both are setup, not the mod's decision. The test
  now picks the scene among 12 directions in 30 degree steps: of the first 4 where every creature's spot is free of
  rocks, trees and pieces (a creature-size capsule), the first where every follower has a navmesh path to a dry point 25 m away, straight
  and 30 degrees either side; it asks again every 0.25 s for up to 15 s so the tiles get built, and notes the scene (or
  the best one found and how many paths it had). The "runs away" check now also says how far the follower moved since
  the kill and before it, how far its flee point is from the death point, and whether it had a path.

**Stage 7 (review fixes).** A review of stage 6's code, each finding checked against `.ref` and the runtime dump.
Built clean (Debug and Release); nothing run in the game yet. `TESTING.md` items stay `[ ]`.

Fixes (each changes behaviour only in the case named):

- **Boss rank message (E5) was never seen.** `Character.OnDeath` credits the kill (`Game.RPC_RegisterKill`, where the
  message was shown) and then calls `m_onDeath()`, whose `BaseAI.OnDeath` sends the boss's `m_deathMessage` to
  everybody as a center message; `MessageHud.ShowMessage(Center)` replaces the text at once (only top-left messages
  queue), and every ladder boss has a death message (dump). The message is now shown 4.5 s after the kill, after the
  4 s fade of the boss's line (`Standing.UpdateMessage`, polled by the `ZNet.Update` postfix; dropped at world end and
  when the mod turns off). Kill-step corner messages stay immediate (they queue). New `morale.publish` steps; T04 says
  when to expect it.
- **Hand-feeding a calm tameable creature cornered it (2.6, 2.9, decision 21).** Walking up and dropping food at your
  feet is exactly the cornered pattern, so the creature struck after 3 s and taming (which needs it unalerted) stopped:
  T22 and the feeding part of T13 would fail. Cornering now never counts for a wild creature with a `Tameable` while it
  is being fed: it goes for food, it ate within its fed time, or an item it eats lies within its food search range
  (`Cornered.BeingFed`: a non-allocating physics query on the `item` layer, run only while a walk-up counts). The food
  lying there is the signal because vanilla searches for food only every 10 s, after the 3 s strike. Non-tameable
  creatures that eat are cornered as before. New `morale.provoke` feeding step (the Boar's own food, no item name);
  T13 and T22 reworded, README and the `CorneredRange` description say it.
- **Cornering and ghost players.** The candidate was `Player.GetClosestPlayer`, which also returns dead, ghost and
  debug-fly players; such a player then hid a real one behind them. The candidate is now the closest player that
  counts (stage 2's "players counted" rule). Cornering still looks at the closest player only (2.6); M04 now says P2
  stays farther away than P1.
- **Sleeping followers woke into the rout (2.7).** `Apply` skipped sleepers, but the recent-rout check routed one that
  woke while the rout lasted (a player walking up wakes it), against "sleeping followers don't notice". The recent
  rout now keeps the ZDOIDs of the loaded followers in range that slept (any owner), and the check skips them. New
  `morale.stealth` steps; new manual test **T28**.
- **Night hunters let go of their prey beyond 200 m (2.5 piece 4).** The stand-down looked only at players within
  200 m, so a hunter chasing a hostile player who got farther stood down, where vanilla keeps chasing a kept target
  without sensing it and a hunter never gives up for not attacking. A hostile or provoked player it has as target now
  keeps the stand-down off at any distance. T21 has an optional check (no self-test: it needs a 200 m move, which
  unloads the test area).
- **Passive Mobs world (2.8).** A player's hit set the attacker as target also there (for fire-only, poison-only and
  absorbed hits vanilla gives none). The extra `SetTarget` now happens only outside Passive Mobs; the provocation record
  is still written, because a shaken follower (the rout applies in such a world) must fight back.
- **Settings edits.** ConfigurationManager sets a text setting at every key and a slider at every step; each set
  rebuilt the rules, the token table and the standing, queued a push to every client, and logged (a Warning for each
  partial creature name typed on the way). In a world, a rule edit now applies once edits stop for 0.75 s
  (`ServerRules.OwnEdited`, polled by the `ZNet.Update` postfix; applied at once without a world or while off, flushed
  at world end and when turned off). T26 now checks it.
- **Server rules traffic and log.** A client whose copy was off still stored and logged pushed rules. It now ignores
  them (it asks again when it turns on, `ServerRules.Start`), and the server pushes a change only to peers that
  `NetworkGate.PeerCompatible` accepts (another network version or a turned-off copy no longer receives rules it cannot
  or would not use). A client logs "Using the server's creature rules" for every new set of rules, also when the
  summary reads the same (a rank list or pack edit that keeps the counts): M06 relied on it. The code comment that said
  `ZRpc` has no unregister was wrong (`ZRpc.Unregister` exists); the handlers simply stay registered.
- **Texts.** The `NearMissRange` description, the README and 5.1 now say the setting cannot reach beyond the weapon's
  noise range (dump: 8 m for every bow and crossbow, 4 m for the Huntsman bow, 30 m for thrown spears and the harpoon).
  T11's deer stands 5-7 m from the Greydwarfs (at 10 m, beyond the bow's 8 m noise, the step could not fail).

Kept as they were, and documented (the review's other options were not cheap or not safe):

- **Kill bonuses make up for at most 2 stars** (2.2): the listing filter assumes the game's maximum of 2 stars, so with
  level mods a 3-star-or-more creature needs the boss rank for the stars beyond. Listing every bonus would grow the
  player ZDO that is resent on every move; tracking the highest level in play would need every game to see every
  creature. README "Good to know" and 2.2 say it.
- **Sneak attacks on calm creatures from an unseen, noisy approach** (E3): see the known limit in 2.6. `CanHearTarget`
  would see the attack's own start noise and deny every sneak attack; a crouch rule would break the Sneak Ambush
  contract. README and T14 say it.
- **Name plate tag on creatures simulated by a game without the mod** (4.6): no game can tell who simulates a creature,
  so the tag can say "calm" on a creature that attacks the viewer. README Multiplayer and M07 say it. (Gone with the
  label in stage 9.)

**Stage 8 (2026-09-30, user feedback after the first in-game test), done in code; not run in the game yet.** The user
tested build `3cda33b+dirty` and asked for three changes (quoted in G1-G3): two bosses ahead, the biome of the spawn
place, and creatures that are frightened instead of passive. Sections 1-9 were rewritten to describe the result
(decisions 25-35); the first version's text survives only in stages 1-7 above and in decisions 2, 4, 6 and 21, marked
as replaced; their section references use the first version's numbering (2.5 had five pieces: the night hunters were
piece 4 and the calming down piece 5, now pieces 5 and 6). The Debug and Release builds of the project are clean; the
smoke test and the in-world self-tests were not run in this stage.

- **Ranks (2.3, decisions 25-28).** The eight per-boss lists (`AfterEikthyr` … `AfterKall`) became nine home biome
  lists (`Meadows` … `DeepNorth`, `Ocean`), `BossesAhead` (2), `Elites` (24 names) and `EliteExtraBosses` (1). The
  same 79 creatures are listed. New `Biomes.cs` (levels, the spawn level from `m_spawnPoint` with the dungeon entrance
  rule); `CreatureState` computes the spawn level once and the rank at each fact refresh; `MoraleRules.TryGetRank`
  takes the spawn level. `PrefabTokens` keeps the lowest and highest home rank per token for the listing filter (2.2).
- **Fear (2.5 piece 2, decision 29).** New `Fear.cs` (check part and frame) and `FleeFrames.cs` (the rout frame, now
  shared: `Rout.Frame` and its delegate moved there; `RoutBroken` became `FramesBroken`). `Attitude.Calm` became
  `Attitude.Afraid`. The once-per-second check order is now stand-down, fear, then target drop and calming down only
  when not running. A provocation (hit, near miss, cornering) ends the fear at once.
- **Cornered (2.6, decision 30).** `Cornered.cs` lost the walk-up gap test and the feeding exception (`BeingFed`); the
  count now runs in the fear frames (`Cornered.Advance`, `Cornered.Strike`). Defaults 3 m and 2 s (were 2.5 m and 3 s).
- **Settings and wire (5.1, 5.2, decisions 31, 34, 35).** New `FearRange` (12 m, 3-40); `CalmNightHunters` renamed
  `NightHuntersCanBeAfraid`; sections `Afraid creatures`, `Home biomes`, `Fighting back` (was `Calm creatures` and
  `Frightened`); rules layout 2; `ModNetworkVersion` 2 (decision 35).
- **Texts.** Plate words "afraid" and "routed" (decision 32); the kill-step message says "They will be afraid of you
  sooner."; the rank-up message shows only when the new rank frightens something (2.2: nothing is afraid of a rank 1
  or 2 player with the defaults, so "Weaker creatures now keep out of your way" after Eikthyr would have been false).
- **Self-tests (7.6).** Same six names. `morale.rules`: new ranks, spawn-level ranks, biome points found with
  `WorldGenerator`, `Fear.Qualifies`, `Cornered.Advance`, layout 2 and the new clamps, elites, senses NOTE.
  `morale.calm`: runs instead of ignoring, fear frames skip vanilla, calms beyond the range, a silent player behind it
  is not noticed, burn and night hunter held beyond `FearRange`, the night hunter runs when close, and the spawn-biome
  steps (spawn point moved to real Plains, Mountains, Deep North and Meadows points). `morale.provoke`: the old steps
  with the fear off, then with it on: no sneak attack on a running creature, the hit ends the fear, cornered when it
  cannot get away, not at 5 m, not by a silent player behind it, afraid again after the provocation. `morale.rout`:
  shaken followers run from a rank 0 player once the fear is on. `morale.stealth`: rank 5. `morale.publish`: no
  rank-up message at rank 1, one at rank 3.
- **Documents.** `README.md`, `CHANGELOG.md` (0.1.0 bullets) and `TESTING.md` rewritten for the new behaviour; every
  item stays `[ ]`; new items T29-T33 and M13. The sibling mods' documents (Sneak Ambush, Tower Shield Wall, Dual
  Wielding, Weapon Moveset) still say "calm" and name the first version's ranks in their cross tests (6.1); their
  contracts with this mod are unchanged. (Sneak Ambush's documents, this mod's cross tests and `docs/game/combat.md`
  were brought in line later the same day: X01 at rank 6, X05 and X07 reach the creature by sneaking up or catching
  it.)

**Stage 8 review fixes (2026-09-30), done in code; not run in the game yet.** Each finding was checked against `.ref`
and the 1.0.16 dump.

- **Kill bonuses and world spawns (2.2).** The listing ranges held home ranks only, so a kind that the game itself
  spawns in a harder biome lost its kill bonus too early: `$enemy_draugr` stopped being listed at rank 7 while Draugr
  spawn at night in the Plains (rank 7; a 1-star one needs 7 + 1). `PrefabTokens` now also counts each prefab's rank in
  the hardest biome of its enabled world spawns (zone controller `SpawnSystem` lists and alt biome lists), and
  `morale.rules` checks the Draugr case. The review's example, Ashlands Surtlings, does not happen in the vanilla world
  spawns: both Surtling entries are disabled (1.11); 2.2 now says which creatures stay uncovered (nests, dungeons,
  location spawners, `spawn`, other mods) instead of "only other mods or the `spawn` command".
- **Broken flee frames (7.3).** After a flee frame threw (`FramesBroken`: vanilla every tick), the check still ran the
  fear, which set the fear player and skipped the target drop, so a creature that already had a player it fears as
  its target kept attacking them (vanilla keeps a kept target without the `CanSenseTarget` gate). The check now stops
  the fear for such a creature and still drops that target and calms it down.
- **Network version (4.3, decision 35).** `ModNetworkVersion` 1 → 2 for the rules layout 2, as the project rule asks for
  any payload change, released or not: a game with the first test build is now refused at the join check.
- **Setup of the tests.** `TESTING.md` Setup now says to test in the Meadows (the Greydwarf steps also work in the
  Black Forest): `spawn` counts the spot where you stand, so a Boar or a Greyling spawned in the Black Forest needs one
  more boss.
- **Kept, documented (2.3):** the spawn level stays `WorldGenerator`'s biome (the same as the player's own current
  biome), not the heightmap biome the spawn system checks: a heightmap exists only near a player, and every game must
  judge a creature the same way. The review's other finding (the sibling mods' documents and `docs/game/combat.md`
  still describe calm creatures and the first ranks) is outside this mod's files (Sneak Ambush's documents and
  `docs/game/combat.md` were aligned later that day).

**Stage 9 (2026-09-30, user feedback: no extra name plates, vanilla alerted state), done in code; not run in the game
yet.** The user asked: "Remove the extra nameplates from the creature morale. Use the vanilla alerted visual state."
(G7, decision 36). The Debug and Release builds of the project are clean (0 warnings); the smoke test and the in-world
self-tests were not run in this stage.

- **Removed:** `Plates.cs` (the "afraid" / "routed" label, `TagFor`, the Debug `TestShow` hook),
  `Patches/EnemyHudPatches.cs` (the `EnemyHud.UpdateHuds` postfix, a per-frame loop over the plates), the personal
  setting `Display.ShowOnNameplates`, the label fields of `CreatureState` (`Label`, `LabelText`, `LabelPlate`,
  `LabelTag`, `NextPlateJudge`) and `CreatureState.All`, and the label cleanup in `OnDeactivated`. The mod no longer
  references TextMeshPro. A config file from an earlier build keeps a `ShowOnNameplates` line that nothing reads
  (README).
- **What was already there:** the flee frames (fear and rout) already called `SetAlerted(true)` when not alerted, so
  a running creature showed the vanilla icon next to the label (the first in-world run's screenshot shows both); the
  fear's end already un-alerted it when calm. Nothing un-alerts a creature while its flee frames run (vanilla's give-up
  lives in the skipped `MonsterAI.UpdateAI`), so the alert was already raised once per run.
- **Made consistent (`FleeFrames.Calm`, `CreatureState.FleeAlert`):** every flee frame sets `FleeAlert` (the frames
  hold the alert). The end of a run takes it back through one rule: owner only, only a held alert, no creature or
  static target, no live provocation by anybody (new `CreatureKeys.AnyLive` / `IsProvoked`, an in-place scan of the
  provokers array, only at the end of a run), not hunting, not hurt within 5 s (then the flag stays and the next check
  tries again). Before, the fear's end un-alerted directly (same conditions minus the provocation and the retry) and
  the **rout's end** relied on 2.5 piece 6, which needs a player within the creature's view range: a pack that fled
  out of the players' sight stayed alerted until vanilla's 30 s give-up. The check now calls the calm when the
  creature is neither routed nor running, so the rout's alert goes within a second of its end, player near or not; it
  is not done at the rout's last frame, so a shaken follower that goes on running from a close player keeps its alert
  without a second alert sound. A run whose fear player dies or turns ghost keeps `FleeAlert` for the next check's
  calm (it used to drop the alert's owner and wait for piece 6). A run that ends because a player the creature would
  fight is close and sensed (its standing or stars changed mid-run) no longer calms first: the alert stays for the
  fight (that `SensesFought` raycast runs only when a run ends). A provocation, a rout taking over a fear, an
  exemption or pending rules drop `FleeAlert` (the alert is theirs or vanilla's now). `OnGained` clears it.
- **Side effects checked in `.ref`** (1.5, 2.5 UI): the alert sound plays once per change to true; combat music,
  "sensed", beds and Rested come from targeting and are untouched; vanilla gives no sneak attack on an alerted
  creature and pays the slow Sneak rate near one (as it already did for running creatures); taming pauses while it
  runs (decision 33). Performance: one bool write per flee frame, one bool read per check; no ZDO write unless the
  alert really changes (`SetAlerted` compares first).
- **Self-tests (same names):** `morale.rules` checks the plate template's `Alerted` and `Aware` icons (was: the
  label's `Health` and `Name` parts). `morale.calm`: the running Greydwarf stays alerted on every frame of the 2.5 s
  run and its ZDO `alert` is on; its plate, forced shown, has the vanilla alert icon on and the aware icon off
  (screenshot `alert-icon`; the label steps, including the trip beyond the plate range, are gone); after it calms 20 m
  away, ZDO `alert` is off and both icons are off (screenshot `calm-no-icon`). `morale.provoke`: the label check on a
  provoked creature is gone (Judge Provoked stays). `morale.rout`: routed followers are Judge Routed with ZDO `alert`
  on and stay alerted on every frame from about 1 s after the kill; shaken ones are Afraid with ZDO `alert` off; the
  broadcast part now also waits for that rout's end and checks both followers unalerted within 2 s, wherever they ran
  (NOTE with their distance to the player).
- **Documents:** `README.md` (the label feature and the `ShowOnNameplates` row replaced by "No extra name plates";
  multiplayer: the same icon on every screen, the old hand-off limit gone; Sneak Ambush, BetterUI, Encyclopedia),
  `CHANGELOG.md` (0.1.0 bullets), `TESTING.md` (build line, Setup, T01, T02, T10, T15, T20, T21, T25, M01, M06, M07,
  M13, X01, X02, L01 rewritten, new M14; every item `[ ]`).

**Review of stage 9 (2026-09-30), done in code; not run in the game yet.** The mod's Debug build is clean (0 warnings,
0 errors; built without deploying, so the game folder still held the label build until the next deploying build); the
smoke test and the in-world self-tests were not run.

- **One rule for the end of every run (`FleeFrames.Calm`, 2.5 piece 2, 2.7):** when a player the creature would fight
  (Hostile or Provoked) is within `FearRange` and sensed as the run ends, the alert stays for that fight (`FleeAlert`
  dropped, `m_updateTargetTimer` at most 0.5 s). The fear's end already did this in `Fear.Update`; that branch now just
  calls the calm, and `Fear.SensesFought` (the decision 29 test) is internal. The rout's end did not: the rout frames
  freeze vanilla's target timer, so the calm (at the next once-per-second check) could come before vanilla's next
  search, un-alert the follower, and vanilla then alerted it again when it saw the player (a second alert sound, the
  icon going to "aware" and back). This happens with `ShakenSeconds` = 0 or a player without the mod let in by
  `AllowPlayersWithoutMod`. New test T34 (b).
- **A building target no longer holds the run's alert.** In the second between a rout's last frame and the calm,
  vanilla's target search runs again and, the follower being alerted (it sees all around), can pick a priority
  building within its view range; the calm then left the alert to vanilla's 30 s give-up, so the "!" stayed up while it
  ran to the building. Vanilla never alerts a creature for a building and attacks buildings unalerted, so the calm now
  ignores a static target. New test T34 (a).
- **Tests set up again:** M14 had P2 at rank 0 next to the Greydwarf P2 spawned, so it fought P2 and never ran from P1
  (decision 29), and 30 m was the plate's very edge: both players are now rank 8, with P2 still about 18 m to one side
  of it and P1 walking in at a right angle. M07's vanilla player (hostile for every creature) now crouches and stands
  still about 18 m to one side, and the text says that after the calm the creature may come for them as in vanilla.
  T25 and the Setup say that an old config file keeps an unread `ShowOnNameplates` line (BepInEx 5 writes orphaned
  entries back), and the build line says to deploy the new build before testing (ConfigurationManager's `Display`
  section shows which build runs).
- **Self-tests unchanged:** the rout steps run with `FearRange` 0 (no keep possible) and a rank 0 player who is shaken
  (Afraid), and `morale.calm`'s two-star step (the player becomes Hostile mid-run) gives the same result through the
  calm as before through `Fear.Update`.
- **Documents:** 2.5 piece 2 End, 2.7 End, decision 36, 5.1 (the name plate note sat in the middle of the table), 7.3,
  8 (T25, T34, M07, M14); `TESTING.md` (build line, Setup, T24, T25, M07, M14, new T34; every item `[ ]`).
