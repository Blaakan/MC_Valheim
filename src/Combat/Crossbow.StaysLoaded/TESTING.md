# Crossbow Stays Loaded — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Builds:**
- *Draft* — deployed 2026-09-28 (built from `645be49` + local changes). The one being tested first. T01–T17, M01–M05.
- *v0.2* (still version 0.1.0) — **deployed 2026-09-28 evening**, build id = the git commit shown in the `[MC:ready]`
  log line and next to the mod in the MC Mods panel. Adds the durability stamp, re-stamp on block/put-away/repair,
  the "Loaded" tooltip, the Debug `[reload weapon]` dump, and the framework (live toggle, MC Mods panel).
  Tests marked **(v0.2)** and the R-tests need this build.

**Results so far:**
- 2026-09-28, *draft* build: T01-T17 passed (T14 = unloads, kept on purpose).
- 2026-09-28, v0.2 build `f15f765`: T18-T22 and R01-R05 passed. Log clean (0 exceptions; live toggle seen twice).
  T22 dump: 9 crossbows (equip 0.2 s, reload 3.5 s), GrapplingHook (reload 2 s), StaffLightning/Dundr (reload 1.9 s,
  25 eitr) are all reload weapons and all currently keep their load.
- Multiplayer: not tested yet.

**Setup:** press F5 for the console → `devcommands` → `spawn Crossbow` + Tab to autocomplete the crossbow name, then
`spawn Bolt` + Tab for bolts (spawn a stack, e.g. `spawn <BoltName> 50`). Spawn two different crossbows for the
two-crossbow tests. Keep `./tools/Watch-Log.ps1 -Mine` open, or check `BepInEx/LogOutput.log` afterwards for errors.

## 0.1.0 — single player

- [x] **T01 Swap and back:** equip a crossbow and let it reload fully. Switch to another weapon (hotbar), then back.
  Expected: loaded immediately (bolt visible, no reload animation), and it fires straight away.
- [x] **T02 Firing still consumes:** fire the loaded crossbow. Expected: it reloads as in vanilla (animation, time).
- [x] **T03 Holster:** loaded crossbow → holster it (press its hotbar key again / empty hands) → re-equip. Expected: loaded.
- [x] **T04 Interrupted reload:** equip an empty crossbow and switch away mid-reload, then back. Expected: not
  loaded; the reload starts again from the beginning.
- [x] **T05 Two crossbows:** load A, switch to B (B reloads), switch back to A. Expected: A loaded. Fire A, switch to
  B. Expected: B still loaded.
- [x] **T06 No free resources:** re-equipping a loaded crossbow costs no stamina or eitr. A real reload costs the
  usual amount.
- [x] **T07 Swimming:** walk into deep water with a loaded crossbow (weapons get hidden), swim, get out. Expected: loaded.
- [x] **T08 Sitting / steering:** sit on a chair and/or take a ship's rudder with a loaded crossbow, then stand up.
  Expected: loaded.
- [x] **T09 Chest round-trip:** put a loaded crossbow in a chest, take it back, equip. Expected: loaded.
- [x] **T10 Drop and pick up:** drop a loaded crossbow on the ground, pick it up, equip. Expected: loaded.
- [x] **T11 Logout/login:** log out with a loaded crossbow (equipped, and another one in the inventory), log back in.
  Expected: both still loaded.
- [x] **T12 No ammo:** loaded crossbow and no bolts → try to fire. Expected: no shot, still loaded. Get bolts → it fires.
- [x] **T13 Repair:** repair a damaged loaded crossbow at its crafting station. Expected: still loaded.
- [x] **T14 Upgrade:** upgrade a loaded crossbow at its crafting station. Expected: **unloaded** (upgrading creates a new item in the game code). User decision 2026-09-28: keep it that way.
- [x] **T15 Death:** die with a loaded crossbow, recover it from the tombstone. Expected: loaded (acceptable either way; note what happens).
- [x] **T16 Disabled:** set `Enabled = false` in `BepInEx/config/MC.Combat.Crossbow.StaysLoaded.cfg`, restart the
  game. Expected: vanilla behaviour (reload after every swap).
- [x] **T17 Clean log:** after a session, no errors or exceptions mentioning `Crossbow Stays Loaded` or `MC.` in
  `BepInEx/LogOutput.log`.
- [x] **T18 Tooltip (v0.2):** hover a loaded crossbow in the inventory: the tooltip ends with an orange "Loaded". Fire
  it: the line disappears. `UI.ShowLoadedInTooltip = false` hides it.
- [x] **T19 Blocking while loaded (v0.2):** with a loaded crossbow, block a few hits (if crossbows can block), swap
  away and back. Expected: still loaded.
- [x] **T20 Item stand / armor stand (v0.2):** put a loaded crossbow on an item stand (and an armor stand), take it
  back, equip. Expected: loaded.
- [x] **T21 Live toggle (v0.2):** Esc → MC Mods → untick Crossbow Stays Loaded → swap weapons: vanilla (reload). Tick it
  again → swap: stays loaded again, no restart.
- [x] **T22 Reload weapon list (v0.2, Debug build):** after spawning, `BepInEx/LogOutput.log` has `[reload weapon]`
  lines. Check which items are listed (crossbows only? also Dundr / grappling hook?) and note equip vs reload times:
  (several pre-loaded crossbows are allowed on purpose). Tell me which items are listed: decide whether non-crossbow
  reload weapons should keep their load too.

## 0.1.0 — weapon selection (v0.3: Weapons config)

- [ ] **W01 Defaults:** crossbows and the grappling hook keep their load across swaps; Dundr (StaffLightning) does
  NOT: swap away and back, it reloads and costs eitr again (vanilla).
- [ ] **W02 Add Dundr live:** in the cfg (or MC Mods / ConfigurationManager) set `Weapons.ExtraItems` to
  `GrapplingHook, StaffLightning` while in game. Dundr now keeps its charge across swaps, no restart.
- [ ] **W03 Exclude one crossbow:** set `Weapons.ExcludedItems = CrossbowArbalest`. The Arbalest reloads after every
  swap; other crossbows still keep their load. Clear it again: the Arbalest keeps its load.
- [ ] **W04 Crossbows off:** `Weapons.Crossbows = false`: crossbows reload as vanilla, the grappling hook still keeps
  its load. Back to `true` afterwards.
- [ ] **W05 Debug list:** the `[reload weapon]` log lines show `keeps load (config) True` for crossbows and the
  grappling hook, `False` for StaffLightning (with defaults).

## 0.1.0 — v0.2 regression (the loaded check changed: durability stamp, block/repair re-stamp)

Quick re-run of the draft tests that touch the new logic.

- [x] **R01 Basics:** T01 swap and back, T03 holster, T05 two crossbows, T02 fire still consumes. Expected: as before.
- [x] **R02 Block then save hops:** load a crossbow, **block one hit with it**, then: swap away and back; chest
  round-trip; drop and pick up; logout/login while holding it. Expected: loaded every time.
- [x] **R03 Repair:** repair a damaged loaded crossbow. Expected: still loaded. Then fire it, swap away before the
  reload finishes, repair it, equip. Expected: must reload (a fired crossbow is never revived by a repair).
- [x] **R04 Death:** T15 again (die, recover tombstone). Expected: loaded.
- [x] **R05 Mod off, fire, mod on:** untick the mod in MC Mods, fire the loaded crossbow, swap away before the reload
  finishes, tick the mod again, repair, equip. Expected: must reload.

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Remote visual:** a friend watches you swap back to a loaded crossbow. Expected: they see it loaded
  (bolt visible) and see you fire without a reload.
- [ ] **M02 Give to a modded friend:** load a crossbow, put it in a chest (or drop it), friend with the mod takes and
  equips it. Expected: loaded for them.
- [ ] **M03 Give to a vanilla friend:** same with a friend WITHOUT the mod. Expected: they must reload (vanilla).
- [ ] **M04 Round-trip through a vanilla friend:** after M03 the friend fires the crossbow and gives it back.
  Expected: you must reload. **Draft build:** known issue, you will see it as still loaded. **v0.2:** fixed (the
  stamp is tied to durability, which drops whenever anyone fires). Also repair it yourself before equipping: you must
  still reload. (Known limit: if the friend without the mod fires AND repairs it before giving it back, it cannot be
  detected.)
- [ ] **M05 Vanilla server:** join a server/host that does not have the mod. Expected: the mod works for you.
