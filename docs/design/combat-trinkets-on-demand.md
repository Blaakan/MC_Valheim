# Trinkets on Demand — design

| | |
|---|---|
| Mod | Trinkets on Demand |
| GUID / project | `MC.Combat.Trinkets.OnDemand` (`src/Combat/Trinkets.OnDemand/`, root namespace `MC.Combat.TrinketsOnDemandMod`, package `TrinketsOnDemand`) |
| Category / scope | Combat / Revamp |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet ideas | `Trinket revamp` (all of it) and `Adrenaline revamp` (part: income while fighting, no drain, ranged catch-up; see Non-goals) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` and `assembly_utils` in `.ref/`; research briefs of 2026-10-01 (adrenaline core, adrenaline sources, framework patterns); repo runtime dumps cited in `docs/game/combat.md` §8 (trinket prefabs, Player `m_maxAdrenaline` 0, miss penalty 0) and `docs/design/combat-crossbow-stays-loaded.md` (Arbalest reload 3.5 s); this mod's in-world self-test NOTE dumps of 2026-10-01 (7.5) |
| Status | Implemented (v0.1.0). Smoke test and all 8 in-world self-tests passed on 2026-10-01 on the final code (with the 1.5 s `RangedReferenceSeconds` default, D14); no hands-on in-game test yet |

## Goal

The user's expected behaviours (idea sheet rows "Trinket revamp" and "Adrenaline revamp", orchestrator spec of
2026-10-01), numbered. Each one is scope and has at least one test (section 8).

1. **G1 — Trinket stats unchanged.** Every trinket keeps its vanilla effect (`SharedData.m_fullAdrenalineSE`) and cost
   (`SharedData.m_maxAdrenaline`), modded trinkets included. No `SharedData` is changed for good.
2. **G2 — Adrenaline builds passively while in combat**, on top of every vanilla source.
3. **G3 — Adrenaline never drains over time** while a trinket gives the bar a capacity (max above 0). Event losses
   (melee miss, unblocked hit) stay vanilla (both are 0 on the 1.0.16 Player prefab, measured).
4. **G4 — A full bar no longer fires the trinket by itself:** it waits, full.
5. **G5 — The player fires the equipped trinket(s) with an input** (keyboard/mouse key and a gamepad combination) when
   the bar is full. Firing = the vanilla pop: every equipped item with an `m_fullAdrenalineSE` gets it added or
   refreshed, the bar goes to 0, the pop effect plays.
6. **G6 — Bow and crossbow hits earn more adrenaline**, in proportion to their slower attack cycle (slower shot = bigger
   gain per hit), so ranged builds the bar at a rate comparable to melee.

### Added beyond the request (small)

- E1: feedback when the bar becomes full: bar flash (when the HUD has the animation) and a top-left message naming the
  trigger; the flash repeats every few seconds while full (personal settings).
- E2: answers to a press that cannot fire: "Adrenaline not full yet", "No trinket to trigger" (center, like the
  Forsaken power's `$hud_powernotready`).
- E3: a tooltip line on every item with a full-adrenaline effect naming the trigger.
- E4: optional "refuse while the effect still runs" (`RefuseWhileActive`, default off = vanilla refresh).
- E5: Debug log lines for testers: fight start/end, the factor of every bow or crossbow shot, each trigger.

### Non-goals

- Changing any trinket (effect, cost, recipe) or adding trinkets (the Blood trinket is its own idea).
- The Adrenaline revamp's per-source rebalance (block, parry, stagger, dodge amounts; miss and unblocked-hit penalties
  turned off), learned per-weapon attack times, kill bonus, per-enemy income scaling, a debug readout on the HUD, and a
  per-move bonus for Weapon Moveset moves. The `Adrenaline revamp` sheet row is linked to this mod but only partly
  covered: passive income while fighting, no decay, ranged catch-up.
- PvP combat state (players are never "real foes").
- Carrying the bar over death or logout (vanilla does not save it).

### Later (cut from v1, one-line sketches)

- L1: learned melee cycle per weapon (`Attack.Start`→`Stop` probe) as the ranged reference instead of a fixed 1.5 s.
- L2: income scaled by the number of engaged foes, or capped per fight.
- L3: `ShotSeconds` measured from the `bow_fire` / `crossbow_fire` clips instead of the 0.5 s seed.
- L4: a key label on the bar itself (`Hud.UpdateAdrenaline` postfix).
- L5: an optional sound when the bar becomes full.
- L6: per-layout gamepad defaults (Alternative 1 and 2 have no free pair, T13 decides).
- L7: the enemy adrenaline multiplier for projectile hits (`Projectile.OnHit` prefix), for full melee parity.

## 1. Vanilla behaviour

All code citations are `.ref/decompiled/assembly_valheim` unless another assembly is named. Prefab values come from
repo runtime dumps where stated, or from this mod's self-test NOTE dumps (7.5, run of 2026-10-01, marked
*(measured)*); what is still open is marked *(unverified)*.

### 1.1 The bar and its capacity

- `Player.m_adrenaline` (private) is the bar; only `Player.AddAdrenaline` writes it in vanilla.
  `Player.m_adrenalineDegenTimer` (private) is set by gains in `AddAdrenaline` and counted down in
  `Player.UpdateStats(float)`.
- Capacity: `Player.GetMaxAdrenaline()` = `m_maxAdrenaline` + `GetEquipmentMaxAdrenaline()`, the cached sum of
  `SharedData.m_maxAdrenaline` over the 8 slot references (right, left, chest, legs, helmet, shoulder, utility,
  trinket), refreshed by `Player.UpdateModifiers` at the top of every `UpdateStats(float)` tick (so one FixedUpdate late
  after an equip change). The Player prefab's `m_maxAdrenaline` is 0 (repo dump), so without a trinket the max is 0.
- 15 vanilla trinket item prefabs (`TrinketBronzeHealth` … `TrinketFlametalStaminaHealth`, repo dump); each has a
  same-named full-adrenaline status effect (`SE_Stats`). They are the only items with an `m_fullAdrenalineSE`. Costs
  10 to 100 (`TrinketChitinSwim` 10, `TrinketBronzeHealth` and `TrinketBronzeStamina` 50, `TrinketSilverDamage` 55,
  `TrinketIronHealth` 65, `TrinketFlametalStaminaHealth` 100), effect `m_ttl` 1 to 120 s, no up-front gain
  (`m_adrenalineUpFront` 0), no gain modifier, no equip effect *(measured)*.

### 1.2 `Player.AddAdrenaline(float v)` — the single funnel

1. Gain (`v > 0` and max > 0): the drain timer is set from `m_adrenalineDegenDelay` at the current fill; `v` is scaled
   by `Game.m_adrenalineRate` (world modifier), the `m_adrenalineGainMultiplier` curve and `SEMan.ModifyAdrenaline`.
2. Losses are always added; gains only while the bar is below the max (one gain may overshoot).
3. **Full-bar test on every call, whatever `v`** (gain, loss or 0): if the bar is at or above the max (max > 0), every
   inventory item with `m_equipped` and an `m_fullAdrenalineSE` gets its effect added (`SEMan.AddStatusEffect` object
   overload) or refreshed (`StatusEffect.ResetTime`); the bar becomes 0 and `m_adrenalinePopEffects` plays. With no
   such item the bar is set to exactly the max.
4. Clamp at 0; then the tier effects (`m_adrenalineEffects`): the highest entry the bar reaches replaces the running
   one, with `Hud.AdrenalineBarFlash()` — the only vanilla caller of the flash. The 1.0.16 Player prefab has no tier
   effects, so vanilla never flashes the bar; the pop effect is `fx_Adrenaline1` *(measured)*.

Consequences: vanilla melee misses call `AddAdrenaline(m_attackMissAdrenaline)` (0 on the prefab, repo dump), so a 0
call is not unique; any call pops a full bar. `m_fullAdrenalineSE` is read only here and in the tooltip
(`ItemDrop.ItemData.GetTooltip`), grep of every `.ref` assembly. An effect set up inside the call can call
`AddAdrenaline` again (`SE_Stats.Setup` and `SE_Stats.ResetTime` run `StartupEffects`, which adds
`m_adrenalineUpFront`); vanilla trinket effects must have no up-front gain, or the pop would recurse forever.

### 1.3 The drain

`Player.UpdateStats(float dt)` (private; the owner's `FixedUpdate`, not dead; overloaded with the profile-statistics
`UpdateStats()`): `m_adrenalineDegenTimer -= dt`; when the bar is above 0 and the timer is at or below 0, it calls
`AddAdrenaline(-m_adrenalineDegen.Evaluate(fill) × dt)` (so the drain also runs the full-bar test, but a loss lowers
the bar before the test, so the drain never pops a full bar). Curves *(measured)*: drain 1/s near empty to 4/s full
(`[0.001:1 1:4]`), drain delay 10 s at an empty bar to 6 s at a full one (`[0:10 1:6]`), gain multiplier 1 at every
fill; world rate 1 by default.

### 1.4 Where adrenaline comes from

16 call sites (research brief "adrenaline sources" §2; the attack "use" gain is paid in both
`Attack.OnAttackTrigger` and `Attack.FireProjectileBurst`, and stagger credit reaches a remote attacker through
`Character.RPC_AddAdrenaline`): melee hit per character (`Attack.DoMeleeAttack`, weapon
`m_attackAdrenaline` × victim `m_enemyAdrenalineMultiplier`), melee miss, area attacks, attack "use" gain, projectile
hit (`Projectile.OnHit`), block and parry (`Humanoid.BlockAttack`), unblocked hit penalty (`Character.RPC_Damage`,
players), stagger (`Character.AddStaggerDamage`, through `Character.RPC_AddAdrenaline` when the attacker is remote),
perfect dodge (`Player.RPC_HitWhileDodging`), Forsaken power (+10), status effect up-front gains, the `adrenaline`
console command, and the drain. All run on the gaining player's own game. Player prefab amounts *(measured)*: melee
miss 0, unblocked hit 0, perfect dodge 5, stagger 3. Weapon `m_attackAdrenaline` of the 130 player weapons (items
with a recipe, plus bare hands) *(measured, `trinkets.ranged-factor` melee-pay NOTE)*: primary attack 1 per enemy hit
on one-handed weapons (17 swords, 18 axes, 19 clubs, every knife, spear, fist and pickaxe, and the three `AtgeirGold`)
and 2 on 23 two-handed ones (9 swords, 5 axes, 4 clubs, 5 atgeirs; the NOTE's examples: `THSwordGold`, `Battleaxe`,
`SledgeIron`, `AtgeirBronze`); secondary attacks 1 to 3; a thrown spear's projectile 2 per hit; staffs pay mostly per
cast (`m_attackUseAdrenaline` 3 to 12).

### 1.5 Bows and crossbows

- A projectile pays its own instance field `Projectile.m_adrenaline` (code default 2; the prefab comes from the ammo)
  once per hit on a valid character in `Projectile.OnHit`, with no enemy multiplier. `Projectile.Setup` does not touch
  the field; `Attack` never writes it. A miss costs nothing. Every vanilla arrow and bolt (and `draugr_arrow`) pays 2
  *(measured)*.
- Bow draw: `Humanoid.GetAttackDrawPercentage` = draw time / `Lerp(D, 0.2 D, skill factor)` with
  `D = m_attack.m_drawDurationMin`; `Humanoid.StartAttack` stores it in the attack clone's `m_attackDrawPercentage`.
  D = 2.5 s on every bow item (full draw 0.5 s at Bows 100) but `charred_bow` and `charred_bow_Fader` (1 s); every
  bow fires 1 projectile in 1 burst *(measured)*.
- Crossbow reload: `ItemData.GetWeaponLoadingTime` = `Lerp(R, 0.5 R, skill factor)` (reads the local player's skill);
  R = 3.5 s on every player crossbow (Arbalest, Ripper and gold ones; repo dump and measured), 1 projectile in 1 burst.
- `Attack.FireProjectileBurst` (private) instantiates `m_projectiles` projectiles per burst and calls
  `IProjectile.Setup(m_character, …, m_weapon, m_lastUsedAmmo)` on each; later bursts come from `Attack.UpdateProjectile`.

### 1.6 Combat signals on the local game

- `Character.Damage(HitData)` runs on the attacker's game for every melee, area and projectile hit, before the RPC to
  the victim's owner; `HitData.m_attacker` is a ZDOID.
- `Character.RPC_Damage` runs on the victim's owner (the local game for the local player), blocks and parries inside.
- `Player.RPC_OnTargeted(sender, sensed, alerted)`: sent about every 0.5 s by a monster's owner while its target is a
  player; carries no targeter. The training dummy `piece_TrainingDummy` is a `MonsterAI` that targets players (repo
  dump), so targeting alone is farmable.
- `BaseAI.IsEnemy`: players are enemies of every faction except Dverger (and the training dummy faction is an enemy of
  players). `Character.m_aiSkipTarget` is set on the training dummy, `ShadowPerson` and the root tentacle (repo dump;
  `piece_TrainingDummy`: faction TrainingDummy, `m_aiSkipTarget` true, an enemy of the player, measured);
  `Faction.TrainingDummy` also covers `FrostWisp` and `Frysling`.

### 1.7 HUD, tooltip, input

- `Hud.UpdateAdrenaline` shows the bar whenever it is above 0; `Hud.AdrenalineBarFlash()` sets the animator trigger
  "Flash" (the 1.0.16 HUD animator has it *(measured)*).
- `ItemDrop.ItemData.GetTooltip(item, qualityLevel, crafting, worldLevel, stackOverride, appending)` (static) appends
  `"\n$item_fulladrenaline: <color=orange>" + se.GetTooltipString() + "</color>"`.
- `Player.Update` (private) reads gameplay keys behind `Player.TakeInput()` (false in menus, chat, console, inventory,
  map, free-fly camera, while dead, in a cutscene or teleporting). Free keyboard keys in vanilla: H, I, O, P, U, Y; H is
  Weapons.DualWield's `SwapHandsKey`.
- Gamepad (`assembly_utils` `ZInput`): layout-independent physical names are registered for every layout
  (`AddGenericGamepadButtons`: `JoyLTrigger`, `JoyRStick`, `JoyDPadUp`, `JoyButtonA`, `JoyBack`, …). Default layout
  (`AddGamepadClassicButtons`): LT = block and the alt button, RS = hide (tap) / radial (hold) / next snap; alt + RS
  is read only in build mode. The self-test lists, on the RS press: `JoyHide`, `JoyNextSnap`, `JoyRStick`,
  `JoyRadial`; on LT: `JoyAltKeys`, `JoyBlock`, `JoyLTrigger`, `JoyMapZoomIn`, `JoyRadialBack`, `JoyRotate`
  *(measured)*. Alternative 1 (`AddGamepadAlt1Buttons`): LT = hide (tap) / radial (hold), LB = block and alt, RS =
  crouch, LB + RS = alternative-placement toggle. Alternative 2 (`AddGamepadAlt2Buttons`): LT = block and alt, RS =
  crouch, LT + RS = alternative-placement toggle (read outside build mode, `Player.Update`).
  `ZInput.ResetButtonStatus(name)` clears a button's pressed/held/released state (vanilla swallows presses with it).

### 1.8 Persistence and who runs what

The bar is not saved (no field in `Player.Save`, the ZDO `adrenaline` float is write-only); login and respawn create a
new `Player`. Everything above runs on the wearer's own game; only stagger credit crosses games
(`Character.RPC_AddAdrenaline`), and it ends in the wearer's `AddAdrenaline`.

## 2. Design

### 2.1 The full bar waits (G4) — `FullBar`

`Player.AddAdrenaline` prefix with `[HarmonyPriority(Priority.First)]` and a void `[HarmonyFinalizer]`, local player
only. When the call can reach the full-bar test (max > 0 and either a gain or a bar already at the max), the prefix
sets `m_fullAdrenalineSE` of every equipped item to null and remembers it; vanilla then takes its own "no effect"
branch: no pop, the bar is set to exactly the max. The finalizer (after every postfix, also after an exception) puts
the effects back when the outermost counted call ends (depth counter). No transpiler: gain scaling, tier effects and
other mods' patches on the method run unchanged; other mods' prefixes and postfixes see the field null during the
call, which is what they must see (nothing may pop outside the trigger). Each counted call hides what is not hidden yet
(already hidden fields are null, two items of one prefab share one `SharedData`). A field another mod set meanwhile is
not overwritten on restore.

### 2.2 The trigger (G5) — `Trigger`, `Controls`, `FullBar.Fire`

`Player.Update` postfix, local player, every frame: read the input first (key, gamepad combination, Debug test press),
then the rules (pending = nothing), feedback, then the press:

1. Gates: `TakeInput()`, not `Hud.InRadial()`, not `InPlaceMode()`, not dead; else nothing (no message).
2. Max 0 or no equipped item with an effect → "No trinket to trigger" (center).
3. Bar below the max → "Adrenaline not full yet" (center).
4. `RefuseWhileActive` and any equipped effect still running (`SEMan.HaveStatusEffect(se.NameHash())`) → "Trinket
   effect still active".
5. Else open the trigger scope and call `AddAdrenaline(0)`: the prefix consumes the scope on that first call and hides
   nothing, so the vanilla pop runs (every equipped effect added or refreshed, bar 0, pop effect). A nested call inside
   the pop (an effect's up-front gain) is counted and hidden as usual and puts everything back when it ends, before the
   pop loop goes on: a modded trinket with an up-front gain cannot loop.
6. After a gamepad press (fired or answered), every `ZInput` button bound to the same physical button (`SameButton`:
   same first binding `effectivePath`, so the player's trigger/face-button swaps are followed) is reset: in the default
   layout this swallows `JoyHide` (no weapons put away when LT is released first) and `JoyRadial` (no radial on a long
   hold); in the alternative layouts `JoyCrouch`, `JoyNextSnap` and `JoyAltPlace`. The modifier is never reset (LT =
   block must keep working; in Alternative 1 a tap on LT still puts the weapons away and a hold opens the radial menu,
   which then gates the trigger).

Keyboard key `TriggerKey` (default Y), validated once like Weapons.DualWield (`ZInput.IsKeyCodeValid`,
`TryKeyCodeToKey`; bad key = warning once, keyboard trigger off), read with `ZInput.GetKeyDown(key, false)`. Gamepad:
`GamepadModifier` (None, LeftTrigger, LeftBumper, RightBumper, RightTrigger; default LeftTrigger) held and
`GamepadButton` (None, RightStick, LeftStick, D-pad ×4, A, B, X, Y, Back; default RightStick) pressed, only while
`ZInput.IsGamepadActive()`.

### 2.3 No drain (G3) — `Income.HoldDrain`

`Player.UpdateStats(float)` prefix (named `typeof(float)`), local player, not pending, max > 0:
`m_adrenalineDegenTimer = Max(timer, 1)`. Vanilla subtracts `dt` (0.02 s) and never reaches 0, so the drain never
runs; event losses are separate `AddAdrenaline` calls and stay (0 on the 1.0.16 prefab). The max read there is one tick old (harmless). A
postfix (after `UpdateModifiers`) sets a bar above the max (after a swap to a cheaper trinket) to the max by a field
write, no call, so the HUD number is honest.

### 2.4 Combat state and income (G2) — `CombatState`, `Income.Tick`

- Exchange timestamp (`Time.time`): `Character.Damage` prefix (`hit.m_attacker` = the local player's ZDOID and the
  victim is a real foe), `Character.RPC_Damage` prefix (victim = local player and `hit.GetAttacker()` is a real foe;
  before vanilla's early exits), `Player.RPC_HitWhileDodging` prefix (local player).
- Targeted timestamp: `Player.RPC_OnTargeted` postfix (local player, `alerted`).
- Real foe = `!m_aiSkipTarget && !IsPlayer() && !IsDead() && !IsTamed() && BaseAI.IsEnemy(local, c)`.
- In a fight = last exchange within `CombatLingerSeconds` (6), or (targeted within 1.5 s and last exchange within 20 s,
  `EngagedWindowSeconds`).
- Income: the `UpdateStats(float)` postfix adds `dt`; every 1 s, in a fight, max > 0 and bar below the max:
  `AddAdrenaline(IncomePerSecond)` (1). Through the vanilla funnel, so world rate, gain curve, status effects and the
  hold all apply. A long frame pays one tick, never a burst. Debug lines on fight start and end.

### 2.5 Ranged catch-up (G6) — `RangedBonus`

- `Attack.FireProjectileBurst` prefix (local player's attack, weapon skill Bows or Crossbows; staffs, spears, bombs,
  the grappling hook and Dundr stay vanilla): the first burst of a new attack clone computes the factor; later bursts
  reuse it. It opens a context (weapon, factor); a void finalizer closes it.
- `Projectile.Setup` postfix: context open, owner = local player, item = context weapon, `m_adrenaline > 0` →
  `m_adrenaline *= factor` on the new instance. The prefab is never touched; nothing happens at hit time (Harpoon Hooks
  Tames zeroes the field at hit time: 0 stays 0).
- Formula: cycle = bow: `draw% × Lerp(D, 0.2 D, skill) + ShotSeconds`; crossbow: `GetWeaponLoadingTime() +
  ShotSeconds`; `ShotSeconds` = 0.5 *(design seed, unverified)*. Cycle = `min(cycle, time since the last local bow or
  crossbow attack)` (first shot: nominal; anti swap-burst for pre-loaded crossbows and bow↔crossbow swaps). Multiplier =
  `clamp(cycle / RangedReferenceSeconds, 1, RangedMaxMultiplier)`; factor = `1 + (mult − 1) / (projectiles × bursts)`
  (multi-projectile weapons share the bonus). Defaults: reference 1.5 s (per-second parity with melee, D14), cap 4
  (reached only by a cycle of 6 s or more: no vanilla bow or crossbow); cap 1 = bonus off.
- Examples at the defaults *(measured inputs)*: every player bow, D 2.5 s → a full draw at Bows 0 is a 3.0 s cycle →
  ×2 (the arrow's 2 pays 4); Bows 50: 1.5 s + 0.5 s = 2.0 s → ×1.33 (2.67); Bows 75 and up: 1.5 s or less → ×1 (at
  Bows 100, 0.5 s + 0.5 s = 1.0 s: 2, the bow is already fast). `charred_bow` (D 1 s, a monster bow without
  `m_bowDraw`) → at most 1.5 s → ×1. Every player crossbow, R 3.5 s → cycle 4.0 s → ×2.67 at Crossbows 0 (a bolt
  pays 5.33), 2.25 s → ×1.5 at 100 (3). A tapped arrow → ×1. Any shot of 1.5 s or longer earns 2 × cycle / 1.5 per
  cycle, about 1.33 adrenaline per second of shooting at any skill. `trinkets.ranged-shot` measured the live
  full-draw shot at Bows 0 with the old 1 s default (×3, 2 → 6); it reads the reference from the rules, so its re-run
  expects ×2.

### 2.6 Feedback (E1-E3) — `Feedback`, `ItemDataPatches`

- In the `Player.Update` postfix: full = max > 0, bar ≥ max and an equipped effect. Rising edge: `Hud.AdrenalineBarFlash()`
  (only when the bar's animator has a "Flash" trigger parameter, checked once per `Hud`: Unity warns on every
  `SetTrigger` of a missing parameter) and, with `ShowFullMessage`, "Adrenaline full: press [Y] to trigger your
  trinket" top left, at most every 20 s. While full, flash again every `FullFlashInterval` s (0 = once).
- Label: the gamepad pair while the gamepad is in use (vanilla glyph sprites through `ZInput.GetBoundKeyString`,
  plain "LT + RS" when the glyph map lacks one), else the key (`[Y]`; modifier keys through the vanilla `$button_*`
  tokens, others through `ZInput.KeyCodeToDisplayName`).
- Tooltip: static `GetTooltip` postfix (6 argument types), not when `appending`, not while pending; the line goes right
  after the vanilla full-adrenaline block (rebuilt the vanilla way to find it), else at the end. Crafting previews get
  it too.

### 2.7 Trinket stats (G1)

Nothing edits `SharedData` beyond the length of one `AddAdrenaline` call on the main thread (2.1). The tooltip (the
only other reader) is built in UI code, never inside the call.

### 2.8 Pending (combat mods' rule)

`ServerRules.Current.IsPending` (a client of a server whose rules have not arrived): every piece plays vanilla — no
hiding (auto pop), no drain hold, no income, the key does nothing (Debug answer `Pending`), no feedback, no tooltip
line, no ranged factor. Host, single player and dedicated server use their own config.

## 3. Decisions

Flagged items are repeated at the pause.

| # | Decision | Options | Choice and reason |
|---|---|---|---|
| D1 | How to stop the auto pop | Transpiler on the full-bar branch / hide the effects for the call | **Hide** (prefix `Priority.First` + void finalizer, depth counter), no transpiler (house rule). The transpiler stays plan B if a trinket mod edits the field live. |
| D2 | Which calls hide | Only the outermost / every counted call that can reach the max | **Every counted call that can reach the max** (a gain, or a bar already full), restore at depth 0. Covers a nested gain whose outer call could not reach the max (a loss or a 0 call below the max). |
| D3 | Telling the trigger apart | `v == 0` / scope flag | **Scope flag, consumed by the first call** (vanilla misses send 0). Nested calls inside the pop hide as usual: a modded trinket with an up-front gain cannot pop in a loop (vanilla would recurse). |
| D4 | No drain | Timer floor 1 s / huge timer / skip the drain call | **Floor 1 s** in an `UpdateStats(float)` prefix while max > 0: after a live toggle-off the vanilla drain is back within 1 s (or when the vanilla delay after a recent gain ends, 6-10 s). |
| D5 | Bar above the max after a swap | Leave it (vanilla sets it at the next call) / clamp | **Clamp in the `UpdateStats(float)` postfix** by field write (no call, no pop): the number is honest at once. |
| D6 | "In combat" | `IsTargeted` / music timer / hits | **Hits given or taken with a real foe + perfect dodges; alerted targeting extends it up to 20 s after the last hit.** Targeting alone is farmable on the training dummy (a `MonsterAI`). Real foe excludes `m_aiSkipTarget` (dummy), tames, players, the dead, and the Dvergr unless angered (`BaseAI.IsEnemy`); not by `Faction.TrainingDummy` (it covers FrostWisp and Frysling). **Flag:** animals (deer, boars) count, as `BaseAI.IsEnemy` says; PvP does not. |
| D7 | Income numbers | | **1 per second, fight lasts 6 s after the last hit.** Server settings. **Flag:** tune with play (the Adrenaline revamp's target was "one enemy fills about half of a 60-cost trinket"). |
| D8 | How the income is paid | Field write / `AddAdrenaline` | **`AddAdrenaline` once per second:** world rate, gain curve, effects and the hold all apply like any vanilla source. |
| D9 | Keyboard default | H (backlog) / Y / U / O | **Y**: H is Weapons.DualWield's `SwapHandsKey`, read every frame while dual wielding. **Flag.** |
| D10 | Gamepad default | LT + RS / other | **LT + RS** (the only free pair in the default layout; LT also raises the shield). Physical `ZInput` names, so the same buttons in every layout. **Flag:** Alternative 1/2 have no free pair (Alternative 1: LT = hide (tap) / radial (hold), RS = crouch; Alternative 2: LT = block and alt, RS = crouch, LT + RS = alt-placement toggle); README advises another pair, T13 checks. |
| D11 | Swallowing the gamepad press | Reset JoyHide/JoyRadial / every action on that physical button | **Every `ZInput` button with the same effective binding path** as the chosen button (not the modifier): covers every layout and the swap settings. Only after a press that the gates let through. |
| D12 | Gates | Also the Forsaken power's action gates | **`TakeInput`, radial, build mode, dead only**: the trigger plays no animation, and the vanilla pop also fires mid-swing. |
| D13 | Pressing while the effect runs | Refresh (vanilla) / refuse | **Vanilla refresh by default** (restarts the effect, spends the bar), `RefuseWhileActive` option (server setting). "Running" = any equipped effect running. |
| D14 | Ranged formula | Per-hit factor at fire time / at hit time; learned or fixed reference (1 s, 1.5 s, 1.8 s) | **Fire-time factor on the projectile instance, fixed 1.5 s reference, cap 4, shot time 0.5 s, anti swap-burst, shared among projectiles.** Hit-time scaling would lose the draw and override Harpoon's zero. Reference settled on 2026-10-01 from the `trinkets.ranged-factor` melee-pay NOTE (1.4, *measured*): one-handed primary attacks pay 1 per enemy hit (a swing of about 0.9 s: about 1.1 per second), two-handed ones 2 per slower swing (about 1.4 per second), every arrow and bolt 2 per hit. At 1.5 s a full-draw bow (below Bows 75) or any crossbow earns 2 × cycle / 1.5 per cycle = about 1.33 per second at any skill, between one- and two-handers; 1 s made ranged about twice melee (2 per second), 1.8 s matched one-handers only. Swing times are estimates (Later L1 would measure them); melee pay is also scaled by the victim's `m_enemyAdrenalineMultiplier`, projectile pay is not (Later L7). |
| D15 | Ranged weapons in scope | By skill / by `m_requiresReload` / all projectiles | **Skill Bows or Crossbows** (Dundr and the grappling hook reload too; staffs and spears stay vanilla). |
| D16 | Feedback | | **Flash + message on the rising edge, flash repeat 4 s, message at most every 20 s** (a fight with hits taken refills often). Personal settings. |
| D17 | Flash safety | Call always / only when the animator has the trigger | **Only when the trigger parameter exists** (checked once per `Hud`): a missing parameter would log a Unity warning on every call. The 1.0.16 HUD has it (measured); the check guards later game versions. |
| D18 | Tooltip line | Only the trinket slot item / every item with an effect; crafting | **Every item with a full-adrenaline effect, crafting previews included**, not appended tooltips, not while pending. |
| D19 | Client waiting for server rules | Own config / vanilla | **Vanilla** (2.8), the combat mods' rule (Sneak Ambush D32, Weapon Moveset Decision 20, Creature Morale decision 24). |
| D20 | Personal vs server settings | | **Personal:** `Controls` (TriggerKey, GamepadModifier, GamepadButton) and `Feedback` (ShowFullMessage, FullFlashInterval). **Server:** IncomePerSecond, CombatLingerSeconds, RefuseWhileActive, RangedReferenceSeconds, RangedMaxMultiplier. `AllowPlayersWithoutMod` is server only. |
| D21 | Mods that change when trinkets fire | Compose / stand aside | **Stand aside** (`LocalBlocker`, "Inactive: … also changes when trinkets fire") for BetterTrinkets, Passive_Trinket_Modifiers, Balrond Battle Flow, matched by name or GUID markers (GUIDs unverified). **Flag:** such a game is "not ready", so a server requiring the mod refuses it. |
| D22 | Other adrenaline mods | | **Compose, Info log only:** AdrenalineModifier, KeepAdrenalineLonger, RageNAdrenaline, MultiTrinket (name or GUID markers, substring), Surge (short marker: only the whole name "Surge" or its exact GUID `ezomic.valheim.surge`, read in its source, so "Resurgence" and such do not match). |
| D23 | Two sheet ideas in one mod | Two mods / one | **One mod** (the backlog's point 1: the hold only makes sense with the build-up). `<ModIdea>Trinket revamp;Adrenaline revamp</ModIdea>`. **Flag:** the Adrenaline revamp row is only partly covered (Non-goals). |

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Runs on |
|---|---|
| Hold, trigger, feedback, tooltip | the wearer's game (local player only) |
| No drain, income | the wearer's game (`UpdateStats(float)` runs only for the owner, not dead) |
| Combat state | the local game: hits it deals (`Character.Damage`, attacker side), hits it takes (`RPC_Damage`, victim owner = itself), targeting RPCs addressed to its player |
| Ranged factor | the shooter's game (projectile `Setup` with `m_owner` there; other copies have no owner and never pay) |
| Join check, rules push | the server (dedicated or host) |

### 4.2 RPCs, ZDO keys, network version

RPC `MC.Combat.Trinkets.OnDemand.SettingsRequest` (int layout) and `.Settings` (ZPackage `TrinketRules`, layout 1:
income, linger, refuse flag, reference, cap). No ZDO key, no prefab, no item data. `ModNetworkVersion` 1; bump with
the layout.

### 4.3 Server settings and join check

Copy of Sneak Ambush's `PlayerCheck` and `ServerRules`, with the three upgrades of the framework brief §2.1: push
debounce (`PushDelay` 0.5 s, pure `Settled`), push only to `NetworkGate.PeerCompatible` peers, and a client stores
rules only while active. Pending = vanilla (2.8). Players without the mod, with it off or another network version are
refused about a second after joining unless `AllowPlayersWithoutMod`.

`ModMultiplayerNotes`: "Install it on the server (or the host) and on every player's game. It changes combat for
everyone, so the server refuses players who do not have the mod, have it turned off or have another version of it
(their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the
server (or host) apply to everyone. Trinkets themselves are not changed: a trinket that reaches a player without the
mod works as in the normal game."

### 4.4 Hand-off cases

- A trinket reaching a player without the mod: nothing about the item changed → vanilla (M03).
- A player without the mod (allowed in): vanilla bar; their stagger of creatures a modded game controls sends vanilla
  stagger credit; nothing of ours reaches them.
- A creature owned by another game: its hits on the local player arrive through `RPC_Damage` on the local game (fight
  state works), and the local player's hits run `Character.Damage` locally (M06).

### 4.5 Dedicated server

Runs only the join check and the rules push; no player, so none of the gameplay patches do anything there.

## 5. Config

| Section | Key | Default | Range | Kind |
|---|---|---|---|---|
| General | Enabled / Status | true / — | | framework |
| General | AllowPlayersWithoutMod | false | | server only |
| Controls | TriggerKey | Y | KeyCode (validated) | personal |
| Controls | GamepadModifier | LeftTrigger | enum | personal |
| Controls | GamepadButton | RightStick | enum | personal |
| Feedback | ShowFullMessage | true | | personal |
| Feedback | FullFlashInterval | 4 | 0-60 s | personal |
| Adrenaline | IncomePerSecond | 1 | 0-10 | server rule |
| Adrenaline | CombatLingerSeconds | 6 | 1-30 s | server rule |
| Trigger | RefuseWhileActive | false | | server rule |
| Ranged | RangedReferenceSeconds | 1.5 | 0.2-5 s | server rule |
| Ranged | RangedMaxMultiplier | 4 | 1-10 | server rule |

Constants (not settings): income tick 1 s, drain floor 1 s, targeted window 1.5 s, engaged window 20 s, `ShotSeconds`
0.5 s, message cooldown 20 s.

## 6. Compatibility

### 6.1 Sibling mods (same release)

- **Swim Dive:** drowning damage has no attacker, so it is not a fight (no income); vanilla still applies the
  unblocked-hit penalty to drowning ticks (0 on the 1.0.16 prefab). No shared hook.
- **Sailing Skill:** both add an independent `Player.Update` postfix (local player only; order does not matter);
  nothing else is shared.

### 6.2 Other MC mods

- **Weapons.DualWield:** H vs Y, no clash. Its second `DoMeleeAttack` of a both-hands event zeroes the clone's
  `m_attackAdrenaline` and the player's `m_attackMissAdrenaline` in a finalizer scope: melee pays once per event; a
  full bar waits through it (a 0 call). X01.
- **Weapons.Moveset:** roll and jump attacks are melee clones paying their own `m_attackAdrenaline`; the trigger works
  mid-roll. X02.
- **Crossbow.StaysLoaded:** a pre-loaded crossbow skips the reload; the anti swap-burst rule limits the factor to the
  time since the last shot. Its `Attack.OnAttackTrigger` postfix runs after `FireProjectileBurst` returns. X03.
- **Harpoon.HooksTames:** zeroes `Projectile.m_adrenaline` at hit time for a hooked tame (Spears, outside our skill
  filter anyway): 0 stays 0. X04.
- **Shields.TowerWall:** tower shields never parry (parry adrenaline 0): a well-timed block pays the block amount; a
  full bar waits through it. Its `Character.Damage` / `RPC_Damage` patches never skip vanilla, so our stamps run. X05.
- **Sneak.Ambush, Creatures.Morale:** smoke-blinded and routing creatures stop targeting; the fight ends after the
  linger. Their `RPC_Damage` prefixes never skip the method. X06, X07.
- **Forge.IdolUpgrades, Crossbow.StaysLoaded, Shields.TowerWall:** they also postfix the static
  `ItemDrop.ItemData.GetTooltip`, only for idols, crossbows and shields, which have no full-adrenaline effect, so the
  trigger line is not affected.

### 6.3 Popular external mods

- **Stand aside (LocalBlocker):** BetterTrinkets (Schwifty; also its Deep North compat patch by Gabadur),
  Passive_Trinket_Modifiers (Gabadur), Balrond Battle Flow (its Surge holds a full bar, then its Overcharge drains
  it). Markers `bettertrinkets`, `passivetrinket`, `battleflow` over the name and GUID, letters and digits only, lower
  case (GUIDs unverified; the one-time Warning names a blocker by its plugin name and GUID; the Debug plugin list is
  written only while this mod is active, so it never shows a blocker).
- **Compose (Info line):** AdrenalineModifier (scales gains, so the income too), KeepAdrenalineLonger (transpiles the
  drain delay; moot while a trinket is equipped), RageNAdrenaline (own keys F/G, D-pad up/left), MultiTrinket (fires
  several trinkets: one press fires all), Surge (costs; the bar follows the max). Substring markers, except Surge:
  the short `surge` must be the whole flattened name, or the GUID must be `ezomic.valheim.surge` (read in its source,
  `src/SurgePlugin.cs`, v1.0.3). Other GUIDs unverified; the Debug plugin list shows them.
- Other `AddAdrenaline` patchers (GrindstoneSkills and similar) see the trigger as a 0 call, like a vanilla miss.

## 7. Implementation plan

### 7.1 Files (`src/Combat/Trinkets.OnDemand/`)

- `Plugin.cs`: config, `OnActivated` / `OnDeactivated` (each step guarded), `LocalBlocker`, personal-section filter.
- `TrinketRules.cs`: rules snapshot (`Default`, `Pending`, `Own`, layout 1 `Write` / `TryRead` with clamping,
  `Describe`, `RangedBonusOn`).
- `ServerRules.cs`, `PlayerCheck.cs`, `Patches/ZNetPatches.cs`: Both-side plumbing (4.3).
- `FullBar.cs`: hide / restore, depth, trigger scope, `Fire`, `HasEquippedEffect`, `AnyEffectRunning`, `IsFull`.
- `Trigger.cs`: one local frame: input, pending, feedback, gates, press, answers, gamepad swallow.
- `Controls.cs`: key validation, gamepad names, `Pressed`, `SameButton`, labels.
- `CombatState.cs`: timestamps, `InCombat` (pure), `IsRealFoe`.
- `Income.cs`: drain hold, clamp, income tick.
- `RangedBonus.cs`: formula (pure), context, `Apply`.
- `Feedback.cs`: flash, message, answers, tooltip line, `CanFlash`.
- `ForeignMods.cs` (blockers), `Compat.cs` (Info lines, Debug plugin list).
- `Patches/PlayerPatches.cs`, `Patches/CharacterPatches.cs`, `Patches/AttackPatches.cs`,
  `Patches/ProjectilePatches.cs`, `Patches/ItemDataPatches.cs`.
- `SelfTests.cs` (`#if DEBUG`).

### 7.2 Harmony patches

| Target (`.ref` signature) | Kind | Local filter first | Purpose |
|---|---|---|---|
| `Player.AddAdrenaline(float v)` | prefix `Priority.First` + void finalizer | `__instance == m_localPlayer` | hold (2.1), trigger scope |
| `Player.UpdateStats(float dt)` (overload: `typeof(float)`) | prefix | local | drain hold |
| same | postfix | local | clamp, income |
| `Player.Update()` (private) | postfix | local | trigger, feedback |
| `Player.RPC_OnTargeted(long, bool sensed, bool alerted)` | postfix | `alerted` and local | targeted stamp |
| `Player.RPC_HitWhileDodging(long)` | prefix | local | exchange stamp |
| `Character.Damage(HitData)` | prefix | `hit.m_attacker` = local ZDOID | exchange stamp |
| `Character.RPC_Damage(long, HitData)` (private) | prefix | victim = local | exchange stamp |
| `Attack.FireProjectileBurst()` (private) | prefix + void finalizer | `m_character == local` | ranged context |
| `Projectile.Setup(Character, Vector3, float, HitData, ItemData, ItemData)` | postfix | context open (one bool) | ranged scale |
| `ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int, bool)` (static) | postfix | item has an effect | tooltip line |
| `ZNet.OnNewConnection`, `ZNet.RPC_PeerInfo`, `ZNet.Update` | postfix | cheap exit | rules, join check |

Every body catches its own exceptions (`PatchGuard.Report`). Per-frame bodies (`Player.Update`,
`UpdateStats(float)`, `ZNet.Update`, `Projectile.Setup`) do reference or bool checks first and allocate nothing.

### 7.3 State and lifetime

Static only, all reset in `OnActivated` and `OnDeactivated`: hidden-effect list and depth (`FullBar`), trigger scope,
feedback edge and flash timer (the 20 s message cooldown and the HUD flash check survive a toggle), income accumulator
and fight flag, combat timestamps, ranged context and last shot. No world, item or ZDO state.

### 7.4 Live toggle

Off: patches removed after `OnDeactivated` restored anything hidden (never expected: single thread). The drain timer
is at 1 s or more (the hold's floor, or what is left of the vanilla delay after a recent gain). A bar held full fires
at the next vanilla call that finds it full (a gain, a hit, a block or a miss) if one comes before the drain starts;
the drain, 1 s or more later, lowers a full bar without a pop. A partly full bar drains after the same timer. On: the
next frame applies the rules; a full bar gets the rising-edge feedback.

### 7.5 Debug in-world self-tests (`SelfTests.cs`)

| Test | What it proves |
|---|---|
| `trinkets.network` | rules round trip, clamping, layout refusal, cut package, only `Pending` pending, server never takes peer rules, `Select`, `Settled`, `PlayerCheck.Decide` |
| `trinkets.controls` | every gamepad name exists in `ZInput`, key validation, key label, `SameButton` for RS holds JoyHide and JoyRadial (default layout; NOTE lists all), blocker markers match and do not match this mod or the composing ones, no blocker loaded, Surge matched only by whole name or exact GUID ("Resurgence" and such not), long compose markers match by substring |
| `trinkets.pending` | pending: full bar pops at once, key answers `Pending`, no tooltip line, no ranged bonus, no income, bar drains; pending over: bar held again |
| `trinkets.hold-and-fire` | NOTE dump (every item with a full-adrenaline effect: cost, effect, class, ttl, up-front gain; Player curves, loss amounts, tier effects, pop effects, HUD Flash trigger); hold on gain / 0 call / cross-max gain / above max; effect put back, depth 0; stats unchanged; tooltip order; no drain with the timer forced to 0; flash and message on the rising edge; presses: not full, fired (effect on, bar 0), refused while active, refresh restarts the effect, no trinket |
| `trinkets.income` | no income out of a fight, income in one, fills and waits (no effect), stops after the linger (and no drain; skipped when a real hit or a hunting monster lands in the wait), 0 = off, none without a trinket |
| `trinkets.combat-state` | `InCombat` timing (pure); live: hitting a Greydwarf and being hit by one stamp, a tame, the training dummy and no attacker do not; alerted targeting stamps but alone is no fight |
| `trinkets.ranged-factor` | formula cases (cap, floor, swap burst, sharing, off, bad numbers), bow and crossbow cycles, the default numbers the docs give (1.5 s reference: bow x2 / x1.33 / x1 at Bows 0 / 50 / 75+, crossbow x2.67 / x1.5); NOTE of every bow's D, crossbow's R, ammo's pay and the resulting factors; NOTE of the melee pay of player weapons (items with a recipe, plus bare hands) by skill (Swords, Axes, Clubs, Knives, Spears, Polearms, Unarmed, Pickaxes, ElementalMagic, BloodMagic): primary and secondary `m_attackAdrenaline`, `m_attackUseAdrenaline` when above 0 and the projectile's pay, as "value: count (examples)" (for the reference, D14) |
| `trinkets.ranged-shot` | a real full-draw `Bow` shot: the arrow's `m_adrenaline` = the prefab value × the formula's factor at the default rules' reference and cap (first shot) |

Overrides only in memory (`ServerRules.TestRules` / `TestPending`, `Controls.TestPress`), never a config value; the
rig restores equipment, ammo, bar, drain timer, effects (trinket and tier), controls and destroys what it spawns.

### 7.6 Shared framework used

`ModPlugin` (life cycle, `LocalBlocker`), `NetworkGate` (`PeerCompatible`, `PeerProblem`, `PeerStateChanged`),
`PatchGuard`, `Log`, `SelfTest`, `ConfigurationManagerAttributes`. Nothing added to `src/Shared`.

### 7.7 Implementation notes

- The trigger scope is consumed in the prefix, and `Fire` also clears it in a `finally` (a prefix that never ran must
  not leave it open).
- `Fire` checks the bar after the call: still full = `Failed` (Debug line), for mods that skip the method.
- The tier-effect struct `Player.StatusEffectLevel` is a value type (no null checks).

## 8. Test plan (`TESTING.md`)

- Setup: console commands (`adrenaline`, `spawn … p`, `raiseskill`, `tame`), checked names (trinkets, Greydwarf,
  training dummy, Bow, Arbalest, KnifeCopper, ArrowWood, BoltBone), the values the self-tests measured, and what is
  still unverified.
- Single player: T01 (G1), T02-T05 (G2), T06-T07 (G3), T08 (G4, G5), T09-T16 (G5 and extras), T17-T19 (G6, at the
  1.5 s default: bow x2 / x1.33 / x1 at Bows 0 / 50 / 100, crossbow x2.67 / x1.5 at Crossbows 0 / 100), T20
  (respawn); L01 live toggle (drain back; a full bar fires on a hit before the drain starts, and drains without firing
  after), L02 `Enabled = false` + restart, L03
  clean log.
- Multiplayer: M01 server rules, M02 refusal and AllowPlayersWithoutMod, M03 hand-off, M04 server without the mod, M05
  personal settings, M06 another game's creatures and stagger credit, M07 turned off, M08 network version, M09
  dedicated server.
- Other mods: X01 Dual Wielding, X02 Weapon Moveset, X03 Crossbow Stays Loaded, X04 Harpoon Hooks Tames, X05 Tower
  Shield Wall, X06 Sneak Ambush, X07 Creature Morale, X08 external trinket and adrenaline mods.

## 9. Open questions and unverified

### Open questions for the user

1. Default key Y (H is Dual Wielding's); gamepad LT + RS; no default for the alternative gamepad layouts.
2. Income 1 per second, fight lasting 6 s (20 s while hunted); animals count, PvP does not.
3. Ranged reference: settled at 1.5 s on 2026-10-01 from the measured melee pay (D14): shooting earns about 1.33
   adrenaline per second, between one-handed (about 1.1) and two-handed (about 1.4) melee. Tune with
   `RangedReferenceSeconds` after hands-on play (T17, T18).
4. Refresh while running spends the bar (vanilla), refusal off by default.
5. The `Adrenaline revamp` sheet row is linked but only partly done.

### Unverified names and values (and how to verify)

| Item | Check |
|---|---|
| Melee swing times behind the 1.5 s reference (about 0.9 s one-handed, slower two-handed; D14) | estimates; Later L1 (`Attack.Start`→`Stop` probe) |
| `ShotSeconds` 0.5 | design seed; Later L3 |
| The swallow works on a real pad | T12 |
| Alternative gamepad layouts | T13 |
| External mod GUIDs and names (Surge's is `ezomic.valheim.surge`, from its source) | blockers: the one-time Warning (name and GUID); composing mods: Debug "Loaded plugins" line; X08 |

Settled by the in-world self-tests of 2026-10-01 (all 8 passed; NOTE values in section 1 and `TESTING.md` Setup):
trinket costs (10-100), effect durations (1-120 s) and up-front gains (0); the Player's curves, loss amounts (miss 0,
unblocked hit 0), dodge 5 and stagger 3, no tier effects, pop effect `fx_Adrenaline1`; the HUD "Flash" trigger (present);
bow D (2.5 s), crossbow R (3.5 s), arrow and bolt pay (2), the melee pay per player weapon (1.4), the live full-draw
shot (x3, 2 → 6, at the old 1 s reference); `ArrowWood` and `BoltBone`; `piece_TrainingDummy` has `m_aiSkipTarget`;
`SameButton` of RS in the default layout holds hide and radial. That run was before the reference default moved to
1.5 s; the final run on 2026-10-01 passed with it (the live shot then pays x2, 2 → 4).

## Implementation notes

- Refinements of the orchestrator spec while building: the trigger scope is consumed by the first call so nested
  calls inside the pop hide (D3); every counted call that can reach the max hides, not only the outermost (D2); the
  gamepad swallow covers every action on the physical button (D11); the flash is guarded by an animator parameter check
  (D17); a 20 s message cooldown (D16); Debug lines for fights and shots (E5); a bar above the max after a swap is
  clamped (D5, spec "optional: do it").
- The self-test list grew from five to eight: `trinkets.controls`, `trinkets.income` and `trinkets.ranged-shot` prove
  the gamepad names, the income and the live ranged path.
- Review fixes of 2026-10-01: the after-fight income check also skips when a real monster hunts the player during the
  wait (it is a fight by design); Surge is matched by its whole name or exact GUID only; a blocker's Warning names its
  GUID (the Debug plugin list never runs while a blocker keeps the mod off); `trinkets.ranged-factor` dumps the melee
  pay by skill for the ranged reference default.
- 2026-10-01, after the melee-pay NOTE: `RangedReferenceSeconds` default 1 s → 1.5 s (D14). `trinkets.ranged-shot`
  now reads the reference and cap from the rules it sets (it had 1 s and 4 written in), and `trinkets.ranged-factor`
  checks the default numbers the README, `TESTING.md` and 2.5 give. T17, T18 and X03 follow the new numbers.
