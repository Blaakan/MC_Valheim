# Sailing Skill — design

| | |
|---|---|
| Mod | Sailing Skill |
| GUID / project | `MC.Exploration.Sailing.Skill` (`src/Exploration/Sailing.Skill/`, root namespace `MC.Exploration.SailingSkillMod`, package `SailingSkill`) |
| Category / scope | Exploration / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Sailing revamp` |
| Game version checked | Valheim 1.0.16 (`Version.CurrentVersion`), decompiled `assembly_valheim`, `assembly_utils`, `assembly_guiutils` in `.ref/`; research briefs on ship physics, the custom skill and the Both-side framework (2026-10-01); sources of blaxxun-boop SkillManager, Jotunn SkillManager, Smoothbrain Sailing, GrindstoneSkills and ImpactfulSkills read on GitHub by the skill brief (2026-10-01). Ship prefab values are not in `.ref`: the in-world self tests measured them in 1.0.16 (section 9). |
| Status | Implemented (v0.1.0 code), build OK; smoke test and all six in-world self tests passed 2026-10-01 on the final code (earlier runs: one wrong `sailing.math` check and one noisy `sailing.ship` sample, both fixed); in-game tests pending |

## Goal

The user's expected behaviours (idea sheet: "Skill (easier to catch the wind, turn, stop, accelerate, wider map reveal
radius)"; the run's spec added ship damage), numbered. Each one is scope and has at least one test (section 8).

1. **G1 — A new Sailing skill**, shown in the Skills panel, earned by the helmsman while the ship moves.
2. **G2 — Catch the wind.** Higher skill: the ship catches the wind better when it is not oriented optimally (higher
   wind factor at non-ideal angles, smaller no-go cone).
3. **G3 — Turn.** Higher skill: turns faster (rudder moves faster, stronger steering force).
4. **G4 — Stop.** Higher skill: stops faster (active brake while the speed setting is Stop).
5. **G5 — Accelerate.** Higher skill: accelerates faster (same top speed, reached sooner; the sail force responds
   faster).
6. **G6 — See.** Higher skill: wider map reveal radius while aboard a ship.
7. **G7 — Protect.** Higher skill: the ship takes less damage.
8. **G8 — House rules.** Required on the server and every player, server settings for everyone, live toggle, level kept
   while the mod is off, no crash when anything is missing.

### Added beyond the request (small)

- Console support: `raiseskill Sailing N`, `resetskill Sailing`, Tab completion, and `raiseskill all` / `resetskill
  all` include Sailing (vanilla only knows enum names).
- A code-drawn skill icon (sail on a longship), no asset file.
- Debug log lines for testers: helm sessions with the XP they paid, the helmsman level the simulating game uses, each
  reduced ship hit.

### Non-goals

- Milestone abilities (lookout pulse, wind forecast), a crew XP share, a ship health bonus, per-ship-type tuning.
- A higher top speed from the handling bonuses (other sailing mods add +20%): acceleration keeps the vanilla top speed;
  only the wind floor (D7) raises the speed at poor wind angles.
- An instant-stop key calling the unused `Ship.Stop` (the brake at Stop covers G4 without a new key).
- Changing the global wind (`EnvMan`): windmills, cloth and particles read it.

### Later (cut from v1, one-line sketches)

- Named abilities at 25/50/75/100 through the "New ability depending on skill level" framework (lookout: reveal a
  wider circle for a moment; wind forecast: HUD arrow of the next wind).
- Crew share: passengers earn a share of the helmsman's XP (GrindstoneSkills: 25%).
- Per-ship tuning (Raft weaker bonuses, `VikingShip_Ashlands` stronger); the prefab values are now measured (section
  9).

## 1. Vanilla behaviour

All `Class.Method` below are in `assembly_valheim` unless noted. Numbers quoted from field initializers are marked;
the prefab values differ and are noted by the self tests.

### 1.1 Who runs what

- **Helmsman input**: `ShipControlls.Interact` (player standing on this ship) sends `RequestControl(playerID)` to the
  ship's owner; `ShipControlls.RPC_RequestControl` (owner) writes the ZDO `user` and answers `RequestRespons(true)`;
  the helmsman's game then runs `Player.StartDoodadControl` + `Player.AttachStart(onShip: true)`. Every fixed step the
  helmsman's game calls `Ship.ApplyControlls(moveDir)` (through `Player.SetDoodadControlls` and
  `ShipControlls.ApplyControlls`): speed steps are edge-triggered RPCs `Forward` / `Backward` to the owner, the rudder is
  integrated **locally** (`m_rudderValue += dir.x * lerp(0.5, 1, |rudder|) * m_rudderSpeed * fixedDt`) and sent to the
  owner by RPC `Rudder` every 0.2 s.
- **Physics owner**: `Ship.CustomFixedUpdate` runs on every game, but only the ZDO owner goes past its owner check
  (damage timers, forces). `Ship.UpdateOwner` (every 2 s) moves ownership only when the current owner's local player is
  not aboard: the owner is normally the first player aboard, **possibly a passenger, not the helmsman**.
- `Player.GetControlledShip` = the doodad controller's ship (only while holding a valid helm). There is no
  `Player.StartShipControl` in 1.0.16.
- Wind is computed per game (`EnvMan.UpdateWind`); the owner's wind drives the physics.

### 1.2 Owner physics per fixed step (`Ship.CustomFixedUpdate`, in-water block)

Buoyancy; quadratic damping written into the velocity (`vf^2 * m_dampingForward * submersion`, clamped to 1 per
step; nobody aboard: horizontal speed x0.1 per step); angular damping on all axes; wave impulses; **sail** impulse
`GetSailForce(sailSize) * mass` (sail size 1 at Full, 0.5 at Half, else 0; impulse not multiplied by dt); **rudder**
impulse `mass * vf * m_stearVelForceFactor * -rudder * dt` at the stern; **paddle** (Slow / Back) `m_backwardForce *
(1 - |rudder|)` along the bow plus `m_stearForce` steering; then the private `ApplyEdgeForce(dt)`, called only here
(owner, in water, after every vanilla force). With nobody at a valid helm, Slow and Back drop to Stop; Half and Full keep
going.

### 1.3 Wind factor and sail force

`Ship.GetWindAngleFactor`: `d = Dot(windDir, -forward)` (+1 = wind onto the bow), `f = lerp(0.7, 1, 1 - |d|) * (1 -
LerpStep(0.75, 0.8, d))`. Dead run 0.7, beam reach 1, zero within about 37 degrees of the bow (ramp to 41). Also read
by `Hud.UpdateShipHud` for the wind icon colour. `Ship.GetSailForce` (private): target `normalize(windDir + forward) *
(f * lerp(0.25, 1, windIntensity) * m_sailForceFactor * sailSize)`, then `Vector3.SmoothDamp(m_sailForce, target, ref
m_windChangeVelocity, 1f, 99f)`: the 1 s smooth time is a **literal**, no field. The forward share of the force is
`cos(theta / 2)`, so in vanilla the forward drive is nearly flat (0.70-0.74) from dead run to beam reach. The rest of
the force is sideways, and the impulse is applied at `worldCenterOfMass + up * m_sailForceOffset`, so it also heels
the ship; sideways drag (`m_dampingSideway`) and angular damping are separate fields.

### 1.4 What limits accelerating, stopping and turning

- Acceleration: the SmoothDamp of the sail force (~59% after 1 s, ~91% after 2 s), the thrust size, and the quadratic
  drag (`vtop = sqrt(thrust / drag)`, approach time constant `1 / sqrt(thrust * drag)`). The sail unfurl animation
  (`UpdateSailSize`) is visual only.
- Stopping: no brake. At Stop the smoothed sail force fades over ~2 s, then only the quadratic drag slows the ship
  (weak at low speed). Back is the only active brake.
- Turning: rudder travel (`m_rudderSpeed`, helmsman's game) and torque (`m_stearVelForceFactor * vf` under sail,
  `m_stearForce` paddling) against `m_angularDamping` (which also damps roll and pitch).

### 1.5 Ship damage paths

`Ship` and `WearNTear` share the root. Upside down (`UpdateUpsideDmg`, blunt, every 1 s while `up.y < 0`), water slam
(`UpdateWaterForce`, only with players aboard), Ashlands ocean (`TakeAshlandsDamage`, hit type `AshlandsOcean`),
collisions (`ImpactEffect.OnCollisionEnter` on the owner with `m_damageToSelf`), creatures (`EnemyHit`), players
(`PlayerHit`, catapults) and fire all go through `WearNTear.Damage` -> RPC `RPC_Damage` on the owner (resistances, tool
tier, damage text, `ApplyDamage`). Weather wear (rain, ash, lava, snow) calls `ApplyDamage` directly from
`WearNTear.UpdateWear`, bypassing `RPC_Damage`. Measured in 1.0.16 (`sailing.ship`): every ship prefab has an
`ImpactEffect` with damage to itself, hit type `Boat`, scaled by relative speed from 1.5 to 7 m/s, at most blunt 20
(Raft), 30 (Karve) or 50 (the others), every 0.5 s at most: collisions are real damage the mod reduces. Only
`VikingShip_Ashlands` is `m_ashlandsReady` (no Ashlands sea damage); the others take it.

### 1.6 Map reveal

`Minimap.Update` calls `UpdateExplore(dt, localPlayer)` every frame, map open or not: timer `+= Time.deltaTime`, over
`m_exploreInterval` (initializer 2 s) -> `Explore(player position, m_exploreRadius)` (initializer 100 m). The scene
values differ: measured in 1.0.16 (`sailing.helm`), **50 m every 0.25 s** (pixel 12 m, texture 2048). No ship term.
Nothing else reads `m_exploreRadius`. Map data is local.

### 1.7 Skills (`Skills`, `SkillsDialog`, `Terminal`, `Localization`)

- `Skills.SkillType` is an enum; `Skills.m_skillData` holds the character's levels; `GetSkill` creates a missing entry
  (reading a level adds a level-0 row); `GetSkillDef` searches `m_skills` and returns null for an unknown type (then
  `Skill.Raise`, `Skills.Save` and `SkillsDialog.Setup` throw).
- `Skills.Load` keeps a saved entry only if `IsSkillValid` (= `Enum.IsDefined`); an invalid one is dropped silently
  and lost at the next save. `Skills.Save` writes every entry.
- `Skills.RaiseSkill`: `accumulator += m_increseStep * factor * Game.m_skillGainRate`; level-up at `floor(level +
  1)^1.5 * 0.5 + 0.5` (excess lost, one level per call); message `"$msg_skillup $skill_" + type.ToString().ToLower()`.
  `Player.RaiseSkill` applies status effect multipliers (Rested). Death: `Skills.OnDeath` -> `LowerAllSkills` over every
  entry. `SkillsDialog` lists `m_skillData` in insertion order, localizes `"$skill_" + type` and the def's
  description. The level-up message reads "Skill improved Sailing: N" in English (centre message without icon for the
  first level, top left with the icon after).
- Measured in 1.0.16 (`sailing.skill`): raise steps Run 0.2, Swim 0.3, Ride 0.2 (one `RaiseSkill` per second of
  running, swimming, riding), Jump, Sneak and Blocking 0.5, weapons, tools and magic 1 (Bows and Spears 1.5), Fishing, Cooking,
  Farming, Crafting and Dodge 0.25; Sailing uses 1 (its XP is already per km). Death lower factor 0.05 (5% of each level
  and the progress, times the world's skill-loss rate), skill cap off, Rested `m_raiseSkill = All` +0.5 (x1.5 XP).
- Console: `raiseskill` / `resetskill` (`Terminal.InitTerminal`, once per process) call `Skills.CheatRaiseSkill` /
  `CheatResetSkill`, which match enum names only; their Tab lists are the enum names, cached at first use.
- `Localization` (`assembly_guiutils`, not publicized): private `AddWord`, private `m_cache` (LRU), private static
  `m_instance`; `SetLanguage` clears every word and calls `SetupLanguage` again. Touching `Localization.instance` too
  early sets the language from the platform before it has a user.

## 2. Design

`s` = the relevant player's Sailing level / 100 (0..1). Every bonus is `1 + BonusAtMax * s` (or its equivalent): Sailing
0 is vanilla exactly, Sailing 100 gives the setting's full value. The level is the effective one (stored level + status
effects, floored, like `Skills.GetSkillLevel`).

### 2.1 The skill (G1, G8)

- Id `(Skills.SkillType)697262889` = `Math.Abs("MC.Exploration.Sailing.Skill".GetStableHashCode())` (Jotunn's rule:
  from the permanent GUID, positive, above 999). Distinct from Smoothbrain (2143840399), GrindstoneSkills (2060925741)
  and ImpactfulSkills Voyager (8168726). Hardcoded; `sailing.skill` checks the formula.
- One shared `SkillDef` (`m_description = "$mc_sailing_skill_desc"`, `m_increseStep = 1`). Name token
  `skill_697262889` (the game builds it from the number).
- Registration is `[AlwaysOnPatch]` (`Patches/SkillRegistrationPatches.cs`): `Skills.IsSkillValid` postfix,
  `Skills.Awake` postfix (def into each instance's `m_skills`, icon), `Skills.GetSkillDef` postfix (fallback),
  `Localization.SetupLanguage` postfix (words, then the translate cache evicted), `Terminal.InitTerminal` postfix (Tab
  lists), `Skills.CheatRaiseSkill` / `CheatResetSkill` prefix + postfix (name `sailing` or the number; `all`; another
  mod's prefix that already handled the call wins through `__runOriginal`). Constants only, no config.
- Death penalty, world skill-gain modifier, Rested bonus, Skills panel, level-up message: vanilla, no patch.

### 2.2 XP (G1)

The helmsman's own game, every 1 s while `Player.GetControlledShip()` is set: flat (x/z) distance of that ship since the
last sample (same ship); under 1 m or over 60 m counts nothing; `Player.RaiseSkill(Sailing, km * XpPerKm)`, default 50
per km. Level 20 after ~8 km, 50 after ~73 km, 100 after ~406 km at the helm. Paddling counts; passengers earn
nothing; pending rules pay nothing. Pace against vanilla movement skills: a ship at 5 m/s pays 0.25 XP per second,
between Run and Ride (0.2 per second of running or riding) and Swim (0.3); Rested makes it x1.5 like any skill.

### 2.3 Who has which level (transport)

Each game publishes its own effective level on its own player ZDO, key `MC.Exploration.Sailing.Skill.Level` (float,
0-100; checked every 2 s and at spawn and feature on, written only when it changed; -1 when the feature turns off; the
read never adds the skill, Breeding's `FarmerSkill` trick). No RPC, no ownership race. Readers (`HelmSkill`):

| Reader | Where | Level |
|---|---|---|
| Handling (G2-G5) | ship owner, per fixed step (cached 0.5 s per ship) | the helmsman: the player in `ship.m_players` whose id is the helm `user` (vanilla `HaveControllingPlayer`); none = 0 |
| HUD wind icon (G2) | helmsman's game | the helmsman = the local player (fresh) |
| Rudder (G3) | helmsman's game | own level |
| Map reveal (G6) | each player | own level |
| Damage (G7) | ship owner, per hit | the best level of all players aboard |

Missing key or -1 (no mod, mod off) = 0.

### 2.4 Catch the wind (G2)

`Ship.GetWindAngleFactor` postfix, delta form (keeps other mods' changes): `result += mine - vanilla` with
`mine = lerp(lerp(0.7, WindFloorAtMax, s), 1, 1 - |d|) * (1 - LerpStep(0.75 + NoGoShiftAtMax * s, 0.8 + NoGoShiftAtMax
* s, d))`. Defaults 0.9 and 0.1: at Sailing 100 the dead run pulls 0.9 (+29%), close-hauled angles gain most (140
degrees off the wind: forward drive 0.18 -> 0.32), the no-go cone shrinks from ~37 to ~26 degrees. `NoGoShiftAtMax` is
capped at 0.15 (~18 degrees) so it never becomes "sail into the wind". Inside the owner's step the helmsman's `s` comes
from the step context; elsewhere (the HUD) from `HelmSkill.Physics01`.

### 2.5 Accelerate (G5)

- `Ship.CustomFixedUpdate` prefix (owner only, rules not pending, `s > 0`): `m_backwardForce` (paddle) and
  `m_dampingForward` x `accel = 1 + AccelerationBonusAtMax * s`, and `accel` stored in the step context; a void
  finalizer (runs after every mod's postfix, also on exception) puts each field back **only if it still holds the value
  the prefix wrote** (`ScaledField`). That is safe in both patch orders with a mod that sets a field once or scales it
  and puts it back itself: writing the saved copy back unconditionally would make the field grow every step when such a
  mod's prefix runs first (Harmony runs postfixes and finalizers in prefix order, and a live toggle re-patches this mod
  after existing ones). A mod that changes a field during the step keeps its change.
- `m_sailForceFactor` is **not** scaled: it sizes the whole sail force, whose sideways part (up to ~97% of it close to
  the wind) pushes the ship sideways and heels it, while only the forward drag is scaled. Instead the `Ship.GetSailForce`
  postfix (inside the owner step only) multiplies only the part of the returned force along the bow by `accel`
  (`SailMath.BowBoost`: `force + forward * (Dot(force, forward) * (accel - 1))`). Sideways push, leeway and heel stay
  vanilla; the push along the bow and the forward drag x the same factor = same top speed along the bow (`sqrt(T/k)`),
  reached that factor sooner. The boost goes into the returned value only; the stored `m_sailForce` stays unboosted,
  so nothing compounds.
- Sail response, same postfix, before the boost: after vanilla's SmoothDamp, the stored `m_sailForce` moves further
  toward the same target by `1 - exp(-SailResponseAtMax * s * dt)` (3 vanilla lines copied for the target, with the
  vanilla `m_sailForceFactor`: keep them in step after game updates). Bounded by the target, nothing compounds; the
  returned force changes by the same delta.

### 2.6 Turn (G3)

- Owner: the same prefix/finalizer scales `m_stearVelForceFactor` and `m_stearForce` x `1 + TurnBonusAtMax * s`.
  `m_angularDamping` is not touched (it also damps roll and pitch).
- Helmsman: `Ship.ApplyControlls(Vector3)` prefix/finalizer scales `m_rudderSpeed` x `1 + RudderBonusAtMax * s` (own
  level), put back only while it holds the scaled value (`ScaledField`). The rudder value reaches a passenger owner by
  the vanilla 0.2 s RPC, as before. No owner check: a modded helmsman keeps the faster rudder on a ship a game without
  the mod simulates (section 4.4).

### 2.7 Stop (G4)

`Ship.ApplyEdgeForce` postfix (private, only called at the end of the owner's in-water block): at `Speed.Stop` with a
helmsman (`s > 0`), `velocity -= forward * vf * min(1, BrakeAtMax * s * dt)`: exponential decay of the forward speed
(0.8/s at Sailing 100: half-life ~0.9 s), on top of the drag (already stronger from 2.5). Setting the velocity after
the queued impulses is what vanilla damping does. A ship left at Stop with nobody at the helm drifts as vanilla.

### 2.8 See (G6)

`Minimap.UpdateExplore(float, Player)` prefix + void finalizer, only on the frame `Explore` will run (vanilla's own
timer test): local player aboard (`Ship.GetLocalShip() != null` and standing on a ship's deck or attached to a ship:
helm included, swimmers excluded) -> `m_exploreRadius *= 1 + RevealBonusAtMax * s` (50 m -> 100 m at Sailing 100
with the default), put back in the finalizer only while it holds the scaled value (`ScaledField`; a radius that grew
every 0.25 s tick would soon reveal the whole map).

### 2.9 Protect (G7)

`WearNTear.RPC_Damage` prefix on the owner, ships only (`TryGetComponent<Ship>` first: every building piece hit comes
here): not a player's hit (`PlayerHit`, `Catapult`, or a `Player` attacker), ship not capsized (`up.y < 0`) ->
`hit.m_damage.Modify(1 - DamageReductionAtMax * s)` with the best level aboard, before resistances (damage text and
health loss see the reduced hit). Weather wear never reaches `RPC_Damage`: unchanged, right for a moored ship.

### 2.10 Icon and words

Code-drawn 64x64 sprite (`SkillIcon`, like Encyclopedia's `BookIcon`), kept for the session, skipped on a headless
server; fallback the Karve / VikingShip / Raft piece icon. Words `skill_697262889` = "Sailing" and
`mc_sailing_skill_desc` (English in every language), added through the private `Localization.AddWord`.

## 3. Decisions

Flagged items are repeated at the pause.

| # | Decision | Options | Choice and reason |
|---|---|---|---|
| D1 | Skill id | Name hash / GUID hash / Smoothbrain's id | **697262889 from the GUID, permanent** (written into saves). Sharing an id with another mod would not migrate old levels and would mix two mods' XP. |
| D2 | Registration and toggle | Toggled / always-on | **Always-on**: turning the mod off (live or `Enabled = false` + restart) keeps the level; only XP and effects stop. Removing the mod loses the level at the next save (README says so). |
| D3 | Death penalty | Vanilla / none (Smoothbrain) / own % | **Vanilla**, no patch (vanilla-consistent). |
| D4 | XP rule | Per second at the helm / per distance / owner-side RPC | **Distance at the helm, 50 XP per km**, helmsman only, sampled every 1 s on the helmsman's game (skills are local). Faster sailing earns more per minute. |
| D5 | Level transport | Ship ZDO + RPC (physics brief 5.2) / player ZDO (5.3) | **Player ZDO** (spec): no RPC, no always-on RPC registration, no ownership race; the owner finds the helmsman by the helm `user` id. |
| D6 | Damage uses whose level | Helmsman only / last helmsman / best aboard | **Best level aboard** (spec): the crew's best sailor protects the ship even when nobody holds the helm. **Flag.** |
| D7 | Wind floor 0.9 at Sailing 100 | Floor only at close-hauled angles / whole floor | **Whole floor**: running downwind also gets +29% at Sailing 100, which also counts with Moder's power (wind always astern). **Flag:** a late-game ship with Moder and Sailing 100 is noticeably faster downwind. |
| D8 | Acceleration | Thrust only / thrust + drag; whole sail force / its part along the bow | **Push along the bow (paddle, and the sail force's forward part) and forward drag x the same factor**: same top speed along the bow, sooner; a coasting ship also slows sooner (helps G4); Back brakes harder. The sail's sideways part is not scaled (review fix): scaling `m_sailForceFactor` gave 1.5x sideways push and heel at Sailing 100 against vanilla sideways and angular damping. |
| D9 | Stop | Brake at Stop / instant-stop key (`Ship.Stop`) | **Brake at Stop, forward speed only, needs a helmsman**. No new key. |
| D10 | Map reveal aboard | Helmsman only / everyone aboard (own level) | **Everyone aboard, own level, own map**; swimmers in the ship's volume excluded. |
| D11 | Not reduced | | Player hits (PvP, own axe, catapults), capsized ships (wrecks still break), weather wear. |
| D12 | Pending (client before server rules) | Own config / vanilla | **Vanilla, no XP** (Combat mods' shared pending rule). The level is still published (not an effect by itself). |
| D13 | Localization reflection failure | Fail inside the always-on patches / fail in `BindConfig` / warn only | **Fail in `BindConfig`** (Error status, feature off, **registration kept** so no level is lost). Failing inside the always-on patches would unpatch `IsSkillValid` too and lose every level at the next save. **Deviation from the spec.** |
| D14 | Console name | `sailing` / also the number | Both (`itemset` resets every def by its number). Another sailing mod's prefix that already handled `sailing` wins (`__runOriginal`). |
| D15 | Other sailing mods | LocalBlocker / compose | **Compose**, one Info log line per found mod; no blocker. |
| D16 | Top speed | Bonus / none | **None from the handling bonuses**; the wind floor (D7) raises the speed at poor angles (dead run about +13% at Sailing 100). |

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Runs on |
|---|---|
| Skill registration, words, icon, console | every game (always-on); no icon on a headless server |
| XP | the helmsman's game |
| Level publishing | every player's game (own player ZDO) |
| Wind factor, acceleration, sail response, turning force, brake | the ship's owner (possibly a passenger), helmsman's published level |
| Rudder speed, HUD wind icon | the helmsman's game, own level |
| Map reveal | each player's game, own level |
| Damage cut | the ship's owner, best published level aboard |
| Join check, rules push | the server (dedicated or host) |

### 4.2 RPCs, ZDO keys, network version

- RPCs `MC.Exploration.Sailing.Skill.Settings` (server -> client, `SailingRules` layout 1) and `.SettingsRequest`
  (client -> server, int layout). Vanilla peers ignore unknown RPCs.
- ZDO key `MC.Exploration.Sailing.Skill.Level` (float) on each player's own player ZDO. Vanilla games ignore it; it is
  not saved with the world (player ZDOs are per session).
- Skill id 697262889 in character saves: never change it.
- `ModNetworkVersion` 1; bump with any change above (the csproj comment lists them).

### 4.3 Server settings and join check

Copy of Sneak Ambush's `PlayerCheck` / `ServerRules` / `ZNetPatches`, with Weapon Moveset's push debounce (0.5 s after
the last change) and `_active` guard in `OnSettings`, and Creature Morale's push filter (`NetworkGate.PeerCompatible`).
A client of a server never plays by its own config: until readable server rules arrive, no XP and no effect (vanilla).
The server refuses players without the mod, with it turned off or with another network version, after a 1 s grace
(`AllowPlayersWithoutMod`, default off).

### 4.4 Hand-off cases

- Nothing on a ship or in the world is changed permanently: every scaled field is put back inside the same call. A ship
  that reaches a game without the mod (ownership moves) simply sails like vanilla there.
- With `AllowPlayersWithoutMod` on: a ship simulated by a player without the mod gets none of the owner-side bonuses
  (their game runs no patch); only a modded helmsman's rudder speed, applied on the helmsman's own game, still reaches
  it through the vanilla Rudder RPC (documented, small, only with this setting on). A player without the mod publishes
  no level, so their helm gives no bonus and their presence no damage cut; they earn no Sailing.
- Mod removed from a character's game: the level is dropped at the next load and lost at the next save (vanilla
  `Skills.Load`).

### 4.5 Dedicated server

Loads the mod (registration harmless, no icon), runs the join check and the rules push. It does not normally own ships
(ownership goes to players near them, except a ship the server took over near the world centre: its reference position
stays at the origin and `Ship.UpdateOwner` needs a local player to hand a ship on). Such a ship uses the published
levels and the server's rules like any owner.

## 5. Config

| Section | Key | Default | Range | Notes |
|---|---|---|---|---|
| General | Enabled / Status | true | | framework |
| General | AllowPlayersWithoutMod | false | | server only |
| Skill | XpPerKm | 50 | 0-1000 | rule |
| Handling | WindFloorAtMax | 0.9 | 0.7-1 | rule |
| Handling | NoGoShiftAtMax | 0.1 | 0-0.15 | rule |
| Handling | AccelerationBonusAtMax | 0.5 | 0-2 | rule |
| Handling | SailResponseAtMax | 1.5 | 0-5 (per s) | rule |
| Handling | TurnBonusAtMax | 0.5 | 0-2 | rule |
| Handling | RudderBonusAtMax | 0.5 | 0-2 | rule |
| Handling | BrakeAtMax | 0.8 | 0-3 (per s) | rule |
| Map | RevealBonusAtMax | 1 | 0-3 | rule |
| Damage | DamageReductionAtMax | 0.5 | 0-0.9 | rule |

Every rule is server-synced; there are no personal gameplay settings.

## 6. Compatibility

### 6.1 Sibling mods (same release)

- **Swim Dive**: a swimmer inside a ship's volume is not "aboard" for the map reveal; no shared patch.
- **Trinkets on Demand**: no overlap.

### 6.2 Other MC mods

No MC mod patches `Ship`, `Minimap`, `WearNTear.RPC_Damage` or `Skills`. Encyclopedia removes level-0 skill entries
it created while reading item tooltips (`DetailBuilder.TooltipKeepingSkills`); tooltips never read Sailing. Sneak
Ambush's holding still ends when a ship carries the player (its rule). Creature Kill and Tame Counts patches
`Player.Message` (the level-up message passes through unchanged).

### 6.3 Popular external mods

- GrindstoneSkills (own Sailing skill: ship speed, health, explore radius), Smoothbrain Sailing (deprecated; skill,
  ship health, speed, explore radius), ImpactfulSkills Voyager: separate ids, bonuses stack; logged. Their GUIDs are
  unverified: `Compat` finds them by name markers and logs every plugin GUID in Debug.
- ValheimRAFT and ship physics replacers: a prefix that skips `CustomFixedUpdate` or `GetSailForce` silently turns the
  handling bonuses off; logged as "may not apply".
- Mods that call `Enum.GetValues(typeof(Skills.SkillType))` do not see Sailing (same limit as Jotunn skills).

## 7. Implementation plan

### 7.1 Files (`src/Exploration/Sailing.Skill/`)

`Plugin.cs` (config, life cycle), `SailingRules.cs` (snapshot, wire), `ServerRules.cs`, `PlayerCheck.cs`,
`Patches/ZNetPatches.cs`, `SailingSkill.cs` (id, def, words, icon, console), `SkillIcon.cs`, `HelmSkill.cs` (level
readers), `LevelPublisher.cs`, `SailingXp.cs`, `SailMath.cs` (pure numbers), `Compat.cs`, `SelfTests.cs`,
`Patches/SkillRegistrationPatches.cs` (always-on), `Patches/ShipPatches.cs`, `Patches/MinimapPatches.cs`,
`Patches/ScaledField.cs` (scale a field for one call, put it back only while it holds the scaled value),
`Patches/WearNTearPatches.cs`, `Patches/PlayerPatches.cs`.

### 7.2 Harmony patches and other entry points

| Target | Kind | Class | Always-on |
|---|---|---|---|
| `Skills.IsSkillValid`, `Skills.Awake`, `Skills.GetSkillDef` | postfix | `SkillRegistrationPatches` | yes |
| `Localization.SetupLanguage`, `Terminal.InitTerminal` | postfix | `SkillRegistrationPatches` | yes |
| `Skills.CheatRaiseSkill`, `Skills.CheatResetSkill` | prefix + postfix | `CheatRaiseSkillPatches`, `CheatResetSkillPatches` | yes |
| `Ship.CustomFixedUpdate(float)` | prefix + finalizer | `ShipFixedUpdatePatches` | no |
| `Ship.ApplyControlls(Vector3)` | prefix + finalizer | `ShipControlsPatches` | no |
| `Ship.GetWindAngleFactor`, `Ship.GetSailForce`, `Ship.ApplyEdgeForce` | postfix | `ShipSailPatches` | no |
| `Minimap.UpdateExplore(float, Player)` | prefix + finalizer | `MinimapPatches` | no |
| `WearNTear.RPC_Damage` | prefix | `WearNTearPatches` | no |
| `Player.Update`, `Player.OnSpawned(bool)` | postfix | `PlayerPatches` | no |
| `ZNet.OnNewConnection`, `RPC_PeerInfo`, `Update` | postfix | `ZNetPatches` | no |

No transpiler.

### 7.3 State and lifetime

Static only: rules snapshots, the per-ship helmsman cache (4 slots, 0.5 s), the local level cache (0.5 s), the owner
step context (`ShipTick`, set in the prefix, cleared in the finalizer), the XP sample, the publish timer. The skill
def and icon live for the session.

### 7.4 Live toggle

Off: patches removed after `OnDeactivated` (own level withdrawn, XP session reset, caches cleared, join check and rules
stopped). Scaled fields are always put back inside the call (unless another mod changed them during it), so nothing is
left scaled. The always-on registration
stays: the level stays in the Skills panel and in saves. On: rules start, the level is published at once.

### 7.5 Debug in-world self-tests (`SelfTests.cs`)

`sailing.network` (wire, pending pick, push debounce, join verdicts), `sailing.math` (wind factor shape, same top
speed / faster approach on the vanilla step model, sail boost along the bow only, brake, catch-up, damage, scaled
fields put back in both patch orders against a scale-and-restore partner, XP per sample, console names, compat
markers), `sailing.skill` (id formula, registration, words, icon, no entry added by reading, Save/Load round trip,
console incl. `all` and clamp, death penalty, Tab lists; NOTE dumps of the level-up text, vanilla skill steps, death
factor, skill cap, Rested), `sailing.helm` (a kinematic Karve 30 m above the spawn with the player at its helm, no
water needed: owner reads the helmsman's real and overridden level, level published, owner-step factors and restored
fields after 50 steps, one `GetSailForce` inside a step against one outside it (along the bow x1.5, sideways and the
stored force unchanged), wind factor at 0/50/100, rudder step, reveal radius, damage cut and its exceptions, XP for 30 m
and none when not steering, normal reveal after leaving), `sailing.pending` (same rig: pending = vanilla for
everything, then back),
`sailing.ship` (NOTE dump of every ship prefab's tuning, health, resistances, ImpactEffect and helm range; open sea
found with `WorldGenerator` within 5 km, vanilla distant teleport, Karve spawned and steered: paddle acceleration,
brake and sail speed at Sailing 0 and 100 measured, then back to the spawn; no sea or no teleport = NOTE only; the
paddle check compares the 0.5 s and 1 s speeds added up (Sailing 100 at least 15% more), because the 0.25 s speed is
within wave noise: one run failed on it with 0.047 against 0.051 m/s while Sailing 100 was clearly faster from 0.5 s
on). Tests
force rules with `ServerRules.TestRules` / `TestPending` and the level with `HelmSkill.TestLocalLevel`; character
skills changed by a test are restored from a `Skills.Save` copy; every ship is destroyed.

### 7.6 Shared framework used

`ModPlugin`, `[AlwaysOnPatch]`, `NetworkGate` (`PeerCompatible`, `PeerProblem`, `PeerStateChanged`), `PatchGuard`,
`Log`, `SelfTest`. No change to `src/Shared`.

### 7.7 Implementation notes

- `GetSailForce`'s postfix copies the vanilla target (3 lines); `SailMath.VanillaWindFactor` copies
  `GetWindAngleFactor`. `tools/Update-GameRefs.ps1` lists `Ship` when it changes: re-check both.
- The owner step context avoids a second helmsman lookup inside the step (wind factor, sail force, brake).

## 8. Test plan (`TESTING.md`)

- **Setup:** console commands (`raiseskill`, `resetskill`, `spawn Karve`, `wind`, `resetwind`, `resetmap`), unverified
  names with the self test that checks each.
- **Single player:** T01 skill shown, T02 XP, T03 no XP without moving or steering, T04 wind (G2), T05 turning (G3),
  T06 stopping (G4), T07 accelerating (G5), T08 map reveal (G6), T09 damage (G7), T10 not reduced, T11 level kept, T12
  console `all`, T13 death penalty, T14 language, T15 Rested, T16 no extra drift or heel (G5).
- **Live toggle / Enabled = false / clean log / turned on mid-voyage:** L01-L04.
- **Multiplayer** (cheat commands work only in single player or on the host: characters are prepared at Sailing 0 and
  100 before joining): M01 server rules, M02 refused, M03 passenger owner with a high-level helmsman, M04 only the helmsman
  earns, M05 best sailor protects, M06 personal map reveal, M07 hand-off to a player without the mod, M08 server
  without the mod, M09 turned off refused, M10 other network version, M11 dedicated server, M12 rudder on a ship
  another game simulates.
- **Cross-mod:** X01 Encyclopedia, X02 Swim Dive, X03 Sneak Ambush, X04 other sailing mods.

## 9. Open questions and unverified

### Open questions for the user

- D6: damage uses the best sailor aboard (not only the helmsman). OK?
- D7: the wind floor also boosts running downwind (+29% at Sailing 100, also with Moder). OK, or limit the floor to
  close-hauled angles?
- Default pace 50 XP per km (Sailing 50 after ~73 km; about 0.25 XP per second at 5 m/s, like Run 0.2 and Swim 0.3).
  OK?

### Settled by the in-world self tests (1.0.16, 2026-10-01)

| Item | Measured |
|---|---|
| Ship prefabs (`sailing.ship`, every prefab with a `Ship`) | Five: `Karve`, `Raft`, `Trailership` (no sail, sail force 0), `VikingShip`, `VikingShip_Ashlands` (the only `m_ashlandsReady` one, also ash-immune) |
| Ship tuning (`sailing.ship`) | Sail force factor Raft 0.05, Karve 0.03, VikingShip 0.05, VikingShip_Ashlands 0.085; rudder speed 1 (Trailership and VikingShip_Ashlands 0.5); paddle 0.2-0.5; forward damping 0.001-0.005; mass 1000 (VikingShip 2000, VikingShip_Ashlands 3000); health Raft 300, Karve 500, VikingShip and Trailership 1000, VikingShip_Ashlands 3000; blunt Normal, fire Weak (VikingShip_Ashlands VeryResistant) |
| Collision damage (`sailing.ship`) | Every ship has an `ImpactEffect` with damage to itself, hit type `Boat`, relative speed 1.5-7 m/s, blunt 20 (Raft), 30 (Karve), 50 (others), every 0.5 s |
| `Time.fixedDeltaTime` | 0.02 s, as assumed in the math |
| Map reveal (`sailing.helm`) | 50 m every 0.25 s (not the 100 m / 2 s initializers), pixel 12 m, texture 2048 |
| Vanilla skill numbers (`sailing.skill`) | Raise steps Run 0.2, Swim 0.3, Ride 0.2 (section 1.7); death factor 0.05; skill cap off; Rested +0.5 raise for All; level-up text "Skill improved Sailing: N" |
| Real acceleration and stopping, Karve (`sailing.ship`, light wind 0.13-0.14) | Paddle 0.2 / 0.59 m/s after 1 / 3 s at Sailing 0, 0.3 / 0.87 at 100; at Stop 0.59 -> 0.58 m/s in 2 s at 0, 0.87 -> 0.17 at 100; sail 0.40 / 1.09 m/s after 2 / 4 s at 0, 0.89 / 1.85 at 100 (measured before the sail boost moved to the bow part only) |
| `wind` console angle | From `.ref` (`EnvMan.UpdateWind`): direction `(sin a, 0, cos a)`, the way the wind blows; 0 = toward +z |

### Still unverified (and how to verify)

| Item | Checked by |
|---|---|
| Top speed with the defaults (the voyage is too short to reach it) | T07 |
| Unity's SmoothDamp never overshoots after the catch-up moved the value (engine code) | T07 (no oscillation), `sailing.ship` sail speed |
| External mod GUIDs / names (GrindstoneSkills, Smoothbrain Sailing, ImpactfulSkills, ValheimRAFT) | Debug "Loaded plugins" line; X04 |
| Creature prefab `Serpent` (used by T09 / M05) | `spawn Serpent` in T09 |

## Implementation notes

- Built as specified; deviations: D13 (where the localization lookup fails), the extra `sailing.helm` test (the owner
  reading the helmsman's level is checked on a kinematic ship above the spawn, so it runs without water; `sailing.ship`
  measures paddle acceleration and braking, which do not depend on the wind, and compares the sail speed only when
  there is enough wind (else a NOTE)), clearer config keys (`NoGoShiftAtMax`, `AccelerationBonusAtMax`, `BrakeAtMax`),
  `Catapult` hits never reduced, and the sail force postfix adds its change to the returned value (delta) instead of
  replacing it.
- Review fixes (2026-10-01): the acceleration no longer scales `m_sailForceFactor` (sideways push and heel grew 1.5x);
  only the sail force's part along the bow is boosted (D8, 2.5). Scaled fields (`Ship` step fields, `m_rudderSpeed`,
  `m_exploreRadius`) are put back only while they still hold the value the prefix wrote (`ScaledField`), so a mod that
  scales and restores the same field in the other order cannot make it grow. The first in-world run failed one pure
  check: `CatchUp(100, 1, 1) < 1` is false in single precision (`1 - exp(-100)` rounds to exactly 1); the formula was
  right (the real maximum is 5/s x 0.02 s = 9.5% per step), the check now tests the largest setting and "never past
  the target".
