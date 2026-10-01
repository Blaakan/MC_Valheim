# Fishing Fight — design

| | |
|---|---|
| Mod | Fishing Fight |
| GUID / project | `MC.Farming.Fishing.Fight` (`src/Farming/Fishing.Fight/`, root namespace `MC.Farming.FishingFightMod`, package `FishingFight`) |
| Category / scope | Farming / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), and sends its gameplay settings to everyone (section 4). |
| Sheet idea | `Better fishing` |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` and `assembly_utils` in `.ref/` (FishingFloat, Fish, Utils, Player, Character, Humanoid, PlayerController, Hud, MonoUpdaters, ZDO, ZDOExtraData, Terminal); research briefs of 2026-10-01 (vanilla fishing, repo conventions, existing fishing mods, MC overlaps, plus a verifier pass); sources of GrindstoneSkills, FeastMaster, ComfyFishing, Angler's Eye, EpicLoot, Trolling Fishing and VHVR read on GitHub for GUIDs and patch shapes (2026-10-01); Stardew Valley 1.5 `BobberBar.update` (decompile mirror) for the catch bar maths |
| Status | Implemented (v0.1.0 code); smoke test passed 2026-10-01; in-world self-tests passed 2026-10-01 on the final code (`fishing.network`, `fishing.logic`, `fishing.pending`, `fishing.fight`: a live fight on a sea shore; an earlier run failed the struggle reel check, fixed by D13); multi-agent review (11 findings confirmed, all fixed: D11, D12, D14, E6 and test robustness); not yet tested by hand in game |

## Goal

The user's expected behaviours (idea sheet, Better fishing, updated 2026-10-01; answers to four questions in the run
request), numbered. Each one is scope and has at least one test (section 8).

1. **G1 — Bite unchanged.** Throw the bait, wait for the bite, hook: vanilla.
2. **G2 — Catch bar while calm.** When the fish is not struggling, a Stardew Valley-like minigame: reel (hold) to make
   the bar go up, release to make it go down.
3. **G3 — In the bar = free line.** Keeping the fish in the bar reels it in automatically with no stamina cost.
4. **G4 — Out of the bar = stamina.** With the fish outside the bar, no line comes in (the line holds; user answer) and
   stamina is consumed.
5. **G5 — Struggle replaces the bar.** When the fish struggles, the minigame goes away and the fish runs to one side.
   The player reads the side from the fish itself (user answer: no HUD cue by default).
6. **G6 — Rod against the run.** The player orients the character so the rod points to the side opposite the fish's
   run, and reels in. This consumes stamina.
7. **G7 — Wrong orientation costs a lot.** Reeling with the wrong orientation consumes a lot of stamina. User answer:
   two sides only (anything that is not the good side is wrong, straight at the fish included), and no line comes in.
8. **G8 — Not reeling = the fish takes line.** User answer: a struggling fish that is not reeled takes line; past the
   maximum line length the line breaks.
9. **G9 — Side changes.** The fish can change direction mid-struggle; the player must re-orient.
10. **G10 — Bar comes back.** Once the fish stops struggling, the minigame is back.

### Added beyond the request (small)

- **E1** Difficulty per fish: species (by biome tier) and stars drive the bar fish's motion, how often and how long it
  fights, how fast it takes line; the Fishing skill makes the zone bigger (on top of vanilla's lower costs and faster
  reel). Stardew-style motion types per species (dart, smooth, sinker, floater).
- **E2** Optional arrow during struggles (personal setting `ShowStruggleArrow`, off by default; offered in the
  questions, the user chose "fish only" as the default cue).
- **E3** Bar size and position (personal settings `BarScale`, `BarOffsetX`, `BarOffsetY`).
- **E4** Every fight exit also lets the fish go (`Fish.OnHooked(null)`). Vanilla forgets it: on stamina-out the fish
  keeps fighting the empty float; when the rod disappears the float is destroyed but the fish stays flagged as hooked
  and keeps splashing.
- **E5** A fish that runs into shallow water turns around when the other way is deeper (keeps the run visible and the
  fish in the water).
- **E6** A fish whose fisher left mid-fight (disconnect) stops splashing: the game that now owns it clears the
  "hooked" flag left on it (vanilla leaves it, so such a fish splashes for everyone until it is caught again).

### Non-goals

- No change to casting, bait, nibble or hook (G1), nor to the empty reel before a bite.
- No new rods, baits, treasure or perfect-catch bonus (Stardew extras); no change to what a catch gives.
- No server-side simulation: the fight stays on the fisher's game like vanilla.

### Later (cut from v1, one-line sketches)

- Rod tiers or bait that change the bar (bigger zone, slower drain), as gear progression.
- A short hint the first time a player fights a fish (tutorial raven text).
- Sound cues for the struggle start and for a wrong-side reel (line creak).

## 1. Vanilla behaviour

### 1.1 Cast, bite, hook (unchanged)

- The rod's attack fires a projectile that spawns `FishingRodFloat` (an `IProjectile`); `FishingFloat.Setup` stores
  the rod owner (`s_rodOwner`, the player's UserID) and the bait (`s_bait`) in the float ZDO and sets
  `m_lineLength` = rod top to float.
- `Fish.CustomFixedUpdate` (run by `MonoUpdaters.FixedUpdate`) finds a free float within range, swims to it, tests the
  bait (`Fish.TestBate`) and calls `FishingFloat.Nibble` (RPC to the float owner). `FishingFloat.TryToHook` hooks
  within 0.5 s of a nibble (called while reeling, or when the float moves fast). `SetCatch(fish)` writes
  `s_sessionCatchID`, links the hook line and calls `Fish.OnHooked(float)`, which **claims the fish** for the fisher
  and always starts an escape.

### 1.2 Vanilla reel (`FishingFloat.FixedUpdate`, float owner)

- Hooked: stamina drain every tick `Lerp(m_hookedStaminaPerSec, m_hookedStaminaPerSecMaxSkill, skill)` (code
  defaults 1 to 0.2 per second), so stamina never regenerates during a vanilla fight.
- Block held (`owner.IsBlocking() && owner.HaveStamina()`): stamina `m_pullStaminaUse + fish.GetStaminaUse() x
  quality`, scaled to x`m_pullStaminaUseMaxSkillMultiplier` at skill 100; the line shortens at
  `Lerp(m_pullLineSpeed, m_pullLineSpeedMaxSkill, skill)` (halved while the fish escapes), only while
  `m_lineLength > distance - 0.2` (the float is not dragged beyond the line); Fishing XP every second (x2 hooked).
  The catch happens inside that branch at `m_lineLength <= 0.5`.
- Losses: stamina 0 (`SetCatch(null)`, "Lost catch", `FishLost` stat, but no `OnHooked(null)`); line break when the
  float is `m_breakDistance` beyond the line or farther than `m_maxDistance` (30); attack or bow draw cancels.
- Tail of every tick: rod line slack, then two `Utils.Pull`s: float toward the fish (0.5 m), float toward the rod top
  at `m_lineLength` (the rope).

### 1.3 Vanilla struggle (`Fish.Escape`)

- `Escape` sets `m_escapeTime = Random(m_escapeMin, m_escapeMax + quality x m_escapeMaxPerLevel)` and the ZDO float
  `s_escape`; the next escape comes `Random(m_escapeWaitMin, m_escapeWaitMax)` after one ends (code defaults: 0.5-3 s
  + 1.5 s per level, 0.75-4 s apart): a vanilla fish fights about half the time.
- An escaping fish wiggles (`sin(t x 40) x 12` degrees) and swims to random waypoints around its spawn point: the
  struggle has **no direction** relative to the player. A calm hooked fish (`m_escapeTime < 0`) drops its waypoint and
  gets `Stop()`: it is dragged by the line.
- Every client plays a splash at 2.5 % per tick while `s_hooked == 1` and the ZDO **float** `s_escape > 0`. Vanilla
  ends an escape with the **int** overload (`Set(s_escape, 0)`), which leaves the float value positive: in vanilla the
  splashes go on after the first escape.

### 1.4 Fish species

| Prefab | Name | Biome |
|---|---|---|
| Fish1 | Perch | Meadows |
| Fish2 | Pike | Meadows, Black Forest |
| Fish5 | Trollfish | Black Forest |
| Fish6 | Giant Herring | Swamp |
| Fish3 | Tuna | Ocean |
| Fish4_cave | Tetra | Mountain caves |
| Fish7 | Grouper | Plains |
| Fish8 | Coral Cod | Ocean |
| Fish12 | Pufferfish | deep Ocean, Mistlands shores |
| Fish9 | Anglerfish | Mistlands |
| Fish11 | Magmafish | Ashlands |
| Fish10 | Northern Salmon | Deep North |

Prefab names from the 1.0.16 SoftRef manifest and checked at runtime by `fishing.logic`; names and biomes from
`docs/design/ux-container-sort.md` (wiki-checked). Fish level = `ItemDrop.m_itemData.m_quality` (1 = no star).

### 1.5 Input and facing

- Reel = Block (`ZInput` `Block` / `JoyBlock`, `PlayerController.FixedUpdate` -> `Player.SetControls`), rebindable,
  ToggleBlock accessibility option. `Humanoid.IsBlocking` needs no shield (the rod is the blocker) and is false while
  attacking, dodging, encumbered, staggered or in a minor action; the inventory, radial menu and chat clear it.
- While `m_blocking`, `Player.AlwaysRotateCamera` is true: the body turns toward the camera yaw at `m_turnSpeed`
  (300 degrees per second). So "where the rod points" = `player.transform.forward`, and turning the rod turns the view.
- Stamina: any non-zero `Player.UseStamina` resets the 1 s regen delay; regen is x0.8 while blocking.

### 1.6 Who runs what

The fisher owns the float (spawned on the attacker's game) and claims the fish on hook, so the whole fight runs on the
fisher's game. Other games see transforms, the lines (float ZDO) and the splash (fish ZDO `s_escape`).

## 2. Design

### 2.1 Take-over (all G)

A `FishingFloat.FixedUpdate` prefix (priority Low) calls `Fight.TryStep`. When the float is owned here, has a fish,
belongs to the local player and the rules are not pending, `Fight` does the whole tick and the prefix returns false.
Otherwise vanilla runs (cast, bite, hook, empty reel). The fight keeps vanilla's tail as is (slack, break check, the
two pulls) and calls vanilla's own success path (`FishingFloat.Catch`, `SetCatch(null)`, `OnHooked(null)`, destroy).
If another mod's prefix already skipped the tick (`__runOriginal` false), the prefix stays out.

### 2.2 Phases (G2, G5, G10)

- A fight starts **calm** (vanilla starts with an escape; the first seconds are for learning the bar).
- Calm lasts `CalmSeconds x Random(0.6, 1.4) x Lerp(1.2, 0.8, difficulty)`; a struggle lasts `StruggleSeconds x
  Random(0.67, 1.33) x Lerp(0.8, 1.2, difficulty) + 0.5 s per star`. Defaults 7 s and 3 s.
- In a struggle the side (left or right of the fisher's line of sight) is random; after at least 1 s on a side the
  fish turns with a rate of `Lerp(0.1, 0.5, difficulty)` per second (G9). Shallow water ahead turns it too, only when
  the other way is deeper and never sooner than 1 s after the last turn (E5, D12).
- Phase changes write the fish ZDO **float** `s_escape` (struggle length, then 0) so every game splashes during
  struggles only.

### 2.3 Catch bar (G2, G3, G4)

Stardew Valley's `BobberBar` maths, turned from 60 ticks per second and pixels into seconds and track parts
(`CatchBar`, pure):

- Zone: accelerates up 1.6 tracks/s² while Block is held (`IsBlocking`, so rebinding, gamepad, ToggleBlock and VR
  gestures work), falls at 1.6 tracks/s² otherwise, both x0.6 while the fish is inside; bounces off the bottom with 2/3
  of its speed, stops at the top. Height `Lerp(BarSize, BarSizeAtMaxSkill, skill)` (0.24 to 0.38).
- Fish marker: new target with a rate of `difficulty / 4000 x 60` per second (x20 for smooth fish), speed eased toward
  `gap x 60 / (10..30 + 100 - difficulty)`, small darts, big darts for dart fish, slow drift for sinkers and floaters.
  Start: easy fish near the bottom (inside the zone), hard fish high.
- Fish inside the zone: the line shortens at vanilla reel speed x `ReelSpeed` (vanilla's "float not dragged" rule
  kept), Fishing XP like vanilla's hooked reel, and **no `UseStamina` call** (regen goes on; x0.8 while Block is held,
  vanilla). Outside: no line, `UseStamina(vanilla reel cost x OffBarStamina)`.
- Vanilla's passive hooked drain is gone in both phases.

### 2.4 Struggle (G5-G9)

- Steering the fish (`FishPatches`, fish owner only): `Fish.CustomFixedUpdate` prefix blocks vanilla escapes
  (`m_nextEscape` = max), keeps `m_escapeTime` = time left (vanilla wiggle and escaping state, so
  `Fish.GetStaminaUse` returns the escape cost) and a waypoint far along the run; `Fish.SwimDirection` prefix swims the
  fish itself along the run at 2.5-4.5 m/s, turning at 540 degrees per second (vanilla turns at `m_turnRate`, often 10,
  so a side run would never show); `Fish.RandomizeWaypoint` prefix keeps vanilla from picking random waypoints or
  nibbling another float. Calm: `m_escapeTime = -1`, no waypoint: vanilla makes the fish passive.
- Run direction: across the fisher->fish line toward the side, plus half "away" while the fish takes line.
- Rod verdict (`FightLogic.Judge`): flat signed yaw from the fisher->float line to the body's forward; good when it is
  at least `RodAngle` (30) degrees on the side opposite the run, wrong otherwise (G7: two sides).
- Reeling (Block): cost = vanilla reel cost (with the escape cost) x `StruggleStamina`, x `WrongSideStamina` (4) when
  wrong. Good side: line in at half the reel speed (vanilla halves while escaping), while the float is dragged no more
  than 1 m past the line (vanilla's calm rule is 0.2 m; a running fish holds the float about that far out, D13).
  Wrong side: the line holds.
- Not reeling: no cost, the line grows at `LineRunSpeed x Lerp(0.6, 1.4, difficulty)` (1.5 m/s for a middle fish).
  Past `m_maxDistance` (30 m) the line breaks ("Line broke"), as does vanilla's break check.

### 2.5 Difficulty (E1)

`FishProfile`: base difficulty by species (table above: Perch 15, Pike 30, Trollfish 35, Giant Herring 40, Tuna 45,
Tetra 50, Grouper 55, Coral Cod 55, Pufferfish 60, Anglerfish 65, Magmafish 75, Northern Salmon 80), +8 per star,
x `FishDifficulty`, clamped 5-100. Unknown species: by the biome where it swims. Motion: Pike, Tetra, Magmafish dart;
Giant Herring, Tuna smooth; Grouper, Anglerfish sink; Pufferfish floats; others mixed.

### 2.6 HUD (G2, E2, E3)

`FightHud` builds a uGUI group under `Hud.m_rootObject` (so Ctrl+F3 and cutscenes hide it): frame, track, zone
(green in / orange out), the hooked fish's item icon as the marker, a line meter (line already in). Shown only in the
calm phase; values drawn between the last two physics ticks. The optional arrow sits beside the crosshair, points the
way to turn the rod (fish runs right -> arrow left), green when the verdict is good.

## 3. Decisions

- **D1 Rod direction = body facing.** `player.transform.forward` follows the camera while Block is held (vanilla); it
  is what other players see. The camera yaw would let a player "point" without turning the body.
- **D2 Two sides, wrong gains nothing** (user answer). Pointing straight at the fish is wrong.
- **D3 Wrong side holds the line.** "No line gained" read as "the line holds": the three choices in a struggle are
  don't reel (lose line, free), reel wrong (hold, x4), reel right (gain, x1).
- **D4 No HUD cue by default** (user answer); the arrow is an off-by-default personal setting. Consequence: while
  reeling the view turns with the rod, so a 30-45 degree turn keeps the float at the side of the screen.
- **D5 The mod owns the struggle timing**, not vanilla `Fish.Escape` (vanilla fights half the time, which leaves no
  room for the bar). GrindstoneSkills rewrites `Escape`: it blocks this mod while its fishing is on.
- **D6 Start calm.** Vanilla starts every hook with an escape; the mod starts with the bar.
- **D7 Stamina numbers stay vanilla's.** Costs are built on the float's `m_pullStaminaUse*` and
  `Fish.GetStaminaUse`, so prefab values and other mods' changes (FeastMaster, Reely Good Rod, EpicLoot) apply.
- **D8 Stamina 0 = lost, as vanilla** (the float stays, empty), plus the fish is let go (E4).
- **D9 Both side** (user rule: gameplay mod required everywhere). The backlog row said "Client" (who runs it); the
  fight changes stamina costs and catching, so the server requires it and sends its rules.
- **D10 One fight per player.** A second hooked float of the same player (multi-float mods) reels vanilla.
- **D11 A fight never outlives its float.** `Fight.DropStale` (every float tick and HUD frame) ends a fight whose
  float or fish was destroyed without a tick of the mod (logout, zone unload, other code), and a `ZNet.OnDestroy`
  postfix ends it at world end (the feature stays Active from one world to the next). Found by the review: without it
  a frozen bar stayed on screen in the next world.
- **D12 Shallow-water turn only when it helps.** The first version flipped every second when both sides were
  shallow (near a beach), swapping the side the player must follow (review finding).
- **D13 Struggle reel allows 1 m of drag.** The first in-world run showed the line never coming in during a struggle:
  the running fish keeps the float about 0.2-0.3 m past the line, and vanilla's 0.2 m rule stops the reel.
- **D14 Leaving a fight hands the fish back cleanly.** `End` makes a still-hooked fish passive (no leftover long
  escape when the feature is turned off mid-fight); every exit where the fisher or float is gone lets the fish go.

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Where |
|---|---|
| Fight (phases, bar, stamina, line, catch, loss) | the fisher's game (float owner, vanilla) |
| Fish steering | the fish owner = the fisher (claimed on hook, vanilla) |
| Catch bar, arrow | the fisher's screen |
| Join check (`PlayerCheck`), rules push (`ServerRules`) | server (dedicated or host) |
| What others see | fish transform, lines and slack (vanilla), splashes from the fish ZDO `s_escape` (vanilla code) |

### 4.2 RPCs, ZDO keys, network version

- RPC `MC.Farming.Fishing.Fight.SettingsRequest` (int layout) and `.Settings` (`FightRules`, layout 1). Copied from
  Swim Dive.
- No ZDO key of its own; it writes vanilla's fish `s_escape` float with vanilla's meaning.
- `ModNetworkVersion` 1; bump with the RPCs or the rules layout.

### 4.3 Server settings and join check

As Swim Dive: the server sends its Fight section to every compatible player (0.5 s after the last change); a client
uses its own config only in single player or when hosting; until the server's rules arrive the client is **pending**
and fishes vanilla. Players without the mod, with it off or another network version are refused after a 1 s grace
unless `AllowPlayersWithoutMod`.

### 4.4 Hand-off cases

- **Watching a fight** (with or without the mod): the fish moves by its synced transform; splashes during struggles
  only (the mod clears the float `s_escape` in calm phases).
- **Fisher leaves mid-fight:** the float's new owner cannot find the rod owner and destroys it (vanilla); the fish's
  steering stopped with the fisher's game; the fish is free. A fish left in a struggle still carries `s_hooked` = 1
  and a positive `s_escape`; the game that now owns it clears both (E6), so the splashes stop.
- **Fish taken by another float** (vanilla edge case when a hooked fish nibbles another float): blocked while the mod
  drives the fish (`RandomizeWaypoint` prefix).
- **Allowed player without the mod:** vanilla fishing for them; their fish fights vanilla escapes.

### 4.5 Dedicated server

The server only checks players and sends rules; it never owns a float with a local player, so `Fight` never runs
there.

## 5. Config

| Section | Key | Default | Range | Scope |
|---|---|---|---|---|
| General | Enabled | true | | own |
| General | AllowPlayersWithoutMod | false | | server only |
| Fight | BarSize | 0.24 | 0.1-0.8 | server wins |
| Fight | BarSizeAtMaxSkill | 0.38 | 0.1-0.8 | server wins |
| Fight | FishDifficulty | 1 | 0.25-2 | server wins |
| Fight | OffBarStamina | 1 | 0-5 | server wins |
| Fight | StruggleStamina | 1 | 0-5 | server wins |
| Fight | WrongSideStamina | 4 | 1-10 | server wins |
| Fight | RodAngle | 30 | 10-90 degrees | server wins |
| Fight | CalmSeconds | 7 | 2-30 s | server wins |
| Fight | StruggleSeconds | 3 | 1-10 s | server wins |
| Fight | LineRunSpeed | 1.5 | 0-5 m/s | server wins |
| Fight | ReelSpeed | 1 | 0.25-4 | server wins |
| Display | ShowStruggleArrow | false | | personal |
| Display | BarScale | 1 | 0.5-2 | personal |
| Display | BarOffsetX | 260 | -1000-1000 | personal |
| Display | BarOffsetY | 0 | -600-600 | personal |

## 6. Compatibility

### 6.1 Sibling mods (same release)

None changes fishing.

### 6.2 Other MC mods

No MC mod patches `FishingFloat`, `Fish`, `UseStamina`, `HaveStamina` or `IsBlocking`.

- **Swim Dive:** swimming hides the rod (vanilla), the float is destroyed and the fight ends (the fish is let go).
  Never uses Crouch or Jump.
- **Trinkets on Demand:** gamepad trigger LT + RS press; LT is the reel and stays held.
- **Harpoon Hooks Tames:** vanilla releases a harpoon line on block or attack after 2 s; no overlap otherwise.
- **Weapon Moveset, Dual Wielding, Tower Shield Wall:** the cast is a vanilla attack; Block + Jump is a vanilla dodge
  (no reel during the roll: in a fight the fish takes line, in calm the zone falls). No shared patch.
- **Loot Pickup Filter:** the catch goes `ItemDrop.Pickup`, not auto pickup; extra drops on a full inventory do.
- **Encyclopedia:** the catch stays vanilla (`FishingFloat.Catch`), so discovery still works.

### 6.3 Popular external mods

| Mod | GUID | Overlap | Action |
|---|---|---|---|
| Trolling Fishing | `sighsorry.TrollingFishing` | several floats, own `FixedUpdate` bool prefix | LocalBlocker |
| PeasFishing | unknown (name match) | Stardew bar | LocalBlocker |
| ChillHook | unknown (name match; config `Andejx.ChillHook.cfg`) | circle minigame | LocalBlocker |
| ChillFishing | unknown (name match) | tension fight | LocalBlocker |
| GrindstoneSkills | `com.GrindstoneSkills` | tension fight, `Escape` rewrite | LocalBlocker while `[50 - Fishing] Fishing Enabled` (server-synced: the mod listens to that config and refreshes at once) |
| Hooked | `Azumatt.Hooked` (unverified, closed source) | Stardew clone | LocalBlocker while `[2 - Minigame] Enabled` |
| EpicLoot | `randyknapp.mods.epicloot` | void prefix flag + `UseStamina` discount | composes (runs before ours) |
| FeastMaster | `com.FeastMaster` | void prefix scales float stamina fields, `GetStaminaUse` postfix | composes |
| ComfyFishing | `games.loxley.comfyfishing` | takes over its own rods by default | composes (its prefix skips first; ours stays out) |
| Angler's Eye | `com.jumpingmushroom.anglerseye` | smart reel (`IsBlocking` prefix) | composes (its smart reel turns off when another mod patches `FixedUpdate`) |
| VHVR | `org.bepinex.plugins.valheimvrmod` | `GetRodTop`, reel gesture | composes (untested) |
| Reely SpecTackleLure | `neobotics.valheim_mod.reelyspectacklelure` | rods, bait | composes (closed source) |
| ValheimPlus | | `[StaminaUsage] fishing` reads a stack frame named `FixedUpdate` | its fishing modifier may not see our costs (unverified) |

## 7. Implementation plan

### 7.1 Files

| File | Role |
|---|---|
| `Plugin.cs` | config (General, Fight, Display), life cycle |
| `FightRules.cs`, `ServerRules.cs`, `PlayerCheck.cs`, `Patches/ZNetPatches.cs` | rules snapshot, server rules, join check (Swim Dive copies) |
| `Fight.cs` | live fight: take-over tick, phases, stamina, line, exits |
| `CatchBar.cs` | catch bar maths (pure) |
| `FightLogic.cs` | rod verdict, run direction, durations, costs (pure) |
| `FishProfile.cs` | difficulty and motion per species (pure) |
| `FightHud.cs`, `Patches/HudPatches.cs` | the bar and arrow |
| `Patches/FishingFloatPatches.cs` | take-over prefix |
| `Patches/FishPatches.cs` | fish steering |
| `ForeignMods.cs` | other fishing mods |
| `SelfTests.cs` | Debug self-tests |

### 7.2 Harmony patches

- `FishingFloat.FixedUpdate` prefix (Low): take-over.
- `Fish.CustomFixedUpdate` prefix, `Fish.SwimDirection` prefix (skips vanilla in a struggle), `Fish.RandomizeWaypoint`
  prefix (skips while driven): only for the current fight's fish. For every other fish this game owns, the
  `CustomFixedUpdate` prefix clears a hooked flag left with no float (E6): a Unity null check, then one ZDO int read.
- `Hud.Update` postfix: draw.
- `ZNet.OnNewConnection` / `RPC_PeerInfo` / `Update` postfixes: rules and join check; `ZNet.OnDestroy` postfix: end a
  fight at world end (D11).

### 7.3 State and lifetime

`Fight.Current` (one per game). It ends on catch, loss, break, cancel, a missing float or fish (`DropStale`, also from
the HUD frame), the fisher gone, pending rules, world end, and feature off (`OnDeactivated`: the fish stays hooked and
vanilla's reel takes over next tick). `End` makes a still-hooked fish passive, puts its vanilla escape timer back and
clears the splash key. The HUD objects are rebuilt per `Hud` and destroyed on feature off.

### 7.4 Live toggle

Patches come off with the feature; `OnDeactivated` ends the fight and destroys the HUD. Nothing is saved.

### 7.5 Debug self-tests

`fishing.network`, `fishing.logic`, `fishing.pending`, `fishing.fight` (live on a shore found with `WorldGenerator`:
rod, float and Fish1 spawned and hooked; calm in and out of the bar; struggle side, no reel, wrong and right side
costs and line; side switch; calm again; catch; loss at 0 stamina). See `SelfTests.cs` header.

## 8. Test plan

`src/Farming/Fishing.Fight/TESTING.md`: T01 (G1), T02-T03 (G2), T04 (G3), T05 (G4), T06 (G5, G10), T07 (G6), T08
(G7), T09 (G8), T10 (G9), T11 (E2), T12-T15 (catch, loss, difficulty E1, cancel E4), T16-T19 (input, menus, display
E3), T20-T22 (live toggle, Enabled = false, clean log), T23-T25 (logout mid-fight D11, shallows D12, struggle reel
D13); M01-M06 (rules, refusal, hand-offs, E6); X01-X05 (MC mods).

## 9. Open questions and unverified

- Prefab values: measured by the in-world run (1.0.16) for `FishingRodFloat` (reel speed 2 m/s at Fishing 0 to 6 at
  100, pull cost 0 with x0.2 at 100, break distance 10 m, max 30 m) and `Fish1` (stamina 3 calm, 10 fighting). Other
  fish are unknown; the code uses whatever their prefabs say. Effective line speed is lower than the reel speed while
  the fish is calm (the passive fish brakes, about 0.8 m/s for a Perch at Fishing 0); in a fight on the right side the
  run measured 1 m/s.
- The struggle swim speed (2.5-4.5 m/s) and turn rate are first guesses: check in game that the run reads clearly and
  the line does not break from the physics alone.
- Whether 30-45 degrees keeps the float on screen at every FOV and camera distance (D4).
- Which bait each fish takes (game data).
- ValheimPlus fishing stamina option and VHVR: untested.

## Implementation notes

- The vanilla "waypoint reached" test compares x and y (`Vector2.Distance` on a `Vector3`), so the steering waypoint
  is set 1 m below the run point to stay "far" in every run direction.
- `Set(s_escape, 0)` in vanilla is the int overload; the mod always writes the float.
