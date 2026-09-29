# Switchable Lights — design

| | |
|---|---|
| Mod | Switchable Lights |
| GUID / project | `MC.Building.Lights.Switchable` (`src/Building/Lights.Switchable/`, root namespace `MC.Building.LightsSwitchableMod`) |
| Category / scope | Building / QoL |
| Side | Both: the server (or the host) and every player install it, the server refuses players without it (3.6); multiplayer Compatible; network version 1 (RPCs `<guid>.Settings` / `<guid>.SettingsRequest`, no ZDO key of its own) |
| Sheet idea | `Torches ON/OFF only, no fuel` |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/`; every `Fireplace` prefab dumped at runtime (Unity 6000.0.75f1) on 2026-09-29; dedicated-server code read in the local 1.0.15 server build; other mods' sources read on GitHub (2026-09-29) |
| Status | Implemented (0.1.0), in-world self-tests pass (`lights.torch`, `lights.candle`, `lights.campfire`); in-game tests pending |
| Changes after the first pause (user, 2026-09-29) | required on the server and every player (refusal + server list); braziers and every fire that can cook removed from the lights (decision 2) |

## Goal

Requirements from the user (2026-09-29):

1. **Torches and light points need no fuel any more.** Every vanilla torch, sconce, lantern and candle stays lit
   without fuel, forever.
2. **Interacting toggles them on or off.** E on a light switches it; nothing is taken from the inventory.
3. **Furnaces and fires used for crafting are not affected.** Campfires, the iron fire pit, the hearth, the bonfire
   and the braziers (every fire that can be used for cooking) keep taking and burning fuel; smelters, kilns, furnaces,
   ovens and the hot tub are not `Fireplace` pieces and are never touched. After the pause the user asked to
   "remove brazier and any fire source that can be used for cooking" (decision 2).
4. The mod is required on the server and every client: the user expected it ("probably"), then set the rule that
   mods changing the experience are required from all players of a server (decision 1).
5. The mod can be turned off on its own, live (framework toggle): off = vanilla lights.

Non-goals: timers or day/night schedules (TimedTorchesStayLit does that), group switches or a ward-wide switch
(backlog "Inspiration"), making non-fire lights (Dvergr lanterns, demister) switchable, changing warmth or comfort.

## 1. Vanilla behaviour (code trace)

### 1.1 `Fireplace`

- `Awake`: the owner writes `s_fuel = m_startFuel` when the key is missing; registers `RPC_AddFuel`,
  `RPC_AddFuelAmount`, `RPC_SetFuelAmount`, `RPC_ToggleOn`; `InvokeRepeating("UpdateFireplace", 0, 2)`, `CheckEnv`
  every 4 s. Build ghosts have no ZDO and return early.
- `UpdateFireplace` (every 2 s): the **owner** reads the time since `s_lastTime` (`GetTimeSinceLastUpdate`, which also
  rewrites `s_lastTime` every tick) and burns `elapsed / m_secPerFuel` when `IsBurning() && !m_infiniteFuel &&
  state == 1`. Then every client calls `UpdateState` (flame objects, wet low/high flame, and the **wet auto-off**:
  owner calls `RPC_ToggleOn` when `m_canTurnOff && m_wet && state == 1`; `m_wet` is computed only for prefabs that
  have both low and high flame objects).
- `IsBurning`: false when blocked (`CheckUnderTerrain`: ground or a solid collider within 0.5 m above, or smoke
  blocked), when `s_state != 1`, or under water; else `fuel > 0 || m_infiniteFuel`. Computed on every client from the
  ZDO and its own prefab flags.
- `Interact(user, hold, alt)`: claims ownership when unowned; toggles only when `m_canTurnOff && !hold && !alt &&
  fuel > 0`; else, with `m_canRefill`, removes one fuel item and sends `RPC_AddFuel` (the owner ignores it when full:
  the sender's item is lost). Its own hold gate reads `m_lastUseTime`, which vanilla never sets: holding E repeats
  every 0.2 s (`Player.Interact`).
- `UseItem` returns false when `!m_canRefill` (before the fireworks loop). `TryGetItems` / `CanUseItems` (1.0 radial
  menu) check only `m_infiniteFuel`, not `m_canRefill`.
- `GetHoverText` returns "" for `m_infiniteFuel` fires.
- RPC handlers: `RPC_ToggleOn` flips `s_state` 1/2 on the owner (effects with variant), `UpdateState` everywhere;
  `RPC_SetFuelAmount` sets the value raw (no clamp) and always plays the fuel-added effect on the owner.
- A `s_state` of 2 on a prefab without `m_canTurnOff` is stuck off in vanilla: nothing can toggle it back.

### 1.2 The 20 `Fireplace` prefabs (runtime dump, 1.0.16)

| Prefab | Hammer | Fuel item, start/max, s per fuel | Flags | Effect areas | Low/high flame |
|---|---|---|---|---|---|
| `piece_groundtorch_wood` | yes | Resin 2/4, 10000 | – | Fire (under the flame object) | no |
| `piece_groundtorch`, `_green`, `_blue` | yes | Resin / Guck / GreydwarfEye 2/6, 20000 | – | Fire (under flame) | no |
| `piece_walltorch` (Sconce) | yes | Resin 2/6, 20000 | – | Fire (under flame) | no |
| `piece_jackoturnip`, `piece_snowlantern` | yes | Resin 2/6, 20000 | – | PlayerBase only | no |
| `piece_brazierfloor01`, `piece_brazierceiling01`, `piece_brazierfloor02` | yes | Coal, Coal, GreydwarfEye 1/5, 20000 | – | **Heat, Fire, Burning** | yes |
| `Candle_resin` | yes | Resin 3/3, 5000 | **canTurnOff**, canRefill false | PlayerBase | yes |
| `CastleKit_groundtorch_unlit` | no (world prop, no Piece) | Resin 0/1, 20000 | – | Fire (under flame) | no |
| `fire_pit`, `fire_pit_iron`, `hearth`, `bonfire` | yes | Wood, 5000 | snow melter, fireworks | Heat, Fire, Burning | yes |
| `BogWitch_Fire_Pit`, `fire_pit_haldor`, `fire_pit_hildir`, `Morkhalla_firepit` | no | Wood 10/10 | infinite fuel | Heat, Fire, Burning | yes |

The Resin candle is the only prefab with `m_canTurnOff`. No `Fireplace` prefab holds a `CookingStation`,
`CraftingStation` or `Smelter`. Forge, smelters, kilns, oven and hot tub carry their own Heat/Fire areas and are not
`Fireplace` pieces.

### 1.3 What a free fire gives (`EffectArea`)

- Heat → `Player.OnNearFire`: the "Campfire" status, resting, freezing → cold (Deep North warmth), `Bed.CheckFire`,
  egg incubation.
- Burning → `CookingStation.IsFireLit` and `CraftingStation.CheckFire` (`m_craftRequireFire`: only the cauldron and
  the Mead ketill).
- Fire → creatures that fear fire; Mistlands mist is not placed near it.
- So a free brazier is a permanent heat and cooking fire; torches, the sconce and the lanterns give light only.

### 1.4 Multiplayer

- One peer owns each light ZDO: the placer, then a nearby player (`ZDOMan.ReleaseZDOS`, server, every 2 s). Only the
  owner burns fuel.
- A **dedicated server never runs lights**: it keeps its reference position far away (server build 1.0.15:
  `Game.FixedUpdate` sets it to (1000000, 0, 1000000)), so it instantiates no world objects.
- An RPC sent to an unowned ZDO is handled by nobody (`IsOwner` fails everywhere): claim first.
- `ZDOMan.RPC_ZDOData` applies incoming data only with a higher data revision, owner and data together.

## 2. Existing mods and what they teach

- **ValheimInfiniteFire** sets `m_infiniteFuel` on prefabs and live instances; its schedule forces `IsBurning`
  false. Its notes reject writing `s_state = 2` (needs ownership, stays in the save).
- **ServersideQoL PrefabConfigurator** (server only) writes per-ZDO field overrides (`ZNetView.LoadFields`,
  `"Fireplace.m_canTurnOff"`, `m_secPerFuel = 0`, `m_canRefill = false`) and recreates the ZDO so clients reload the
  piece. It warns that toggleable lights switch off in rain.
- **TorchesAndResin** writes a fuel far above the maximum (`RPC_SetFuelAmount` does not clamp), so even vanilla owners
  never run out.
- **ValheimPlus**, **TimedTorchesStayLit**, **HexInfiniteFuel**: `Awake` or `IsBurning` patches; no real on/off switch.
- No existing "light switch" mod: ServersideQoL's MakeToggleable + InfiniteFuel is the closest.

## 3. Design

### 3.1 Core idea

A light is **on** when the vanilla state is 1 and its fuel is above 0; **off** when its fuel is 0. The candle uses the
vanilla state instead: off = state 2. On means full fuel. The mod only writes vanilla values inside vanilla bounds
(fuel 0..max, state 2 only on the candle, which vanilla can switch back), so:

- every client, with or without the mod, draws the same light (`IsBurning` reads the same keys);
- a player without the mod sees an off light as an empty light and can refuel it (which turns it on);
- after uninstall nothing is stuck: on lights are full and burn normally, off lights are empty.

Rejected: `m_infiniteFuel` (vanilla clients would still see the light die with 0 fuel, and it hides the hover text);
`m_canTurnOff` on torches (rain would switch wet-flame fires off); ZDO field overrides (stay in the save after uninstall, need
a re-instantiation); fuel above the maximum (vanilla players could never switch it off and would see "Fuel 10000/6").

### 3.2 Which pieces

An explicit list of prefab names (setting `Lights`) plus one hard rule. The default holds the 9 light prefabs of 1.2
(torches, sconce, Jack-o-turnip, snow lantern, candle, castle torch). **Hard rule:** a listed prefab that can be used
for cooking (any `EffectArea` of type `Burning` in its children: what `CookingStation.IsFireLit` and
`CraftingStation.CheckFire` look for, 1.3) is never managed, with an Info log line at world load. That covers the
campfires, the hearth, the bonfire, the braziers and any modded cooking fire, even if a player lists it. `LightRules`
keeps the stable hashes of the remaining names; a fire is managed when its ZDO prefab hash is in the set (one ZDO
read, one set lookup) **and its instance does not have `m_infiniteFuel`**: a light another mod made infinite is left
to that mod (fuel 0 cannot put it out, and state 2 would leave it dark for good once either mod is gone). Names that
match no prefab, or no fire, are logged at world load. The list in use is `ServerRules.Current` (3.6).

### 3.3 Patches (`Patches/FireplacePatches.cs`, `ZNetScenePatches.cs`)

| Patch | Why |
|---|---|
| `Fireplace.Awake` postfix | managed instance gets `m_canRefill = false` (3.4) |
| `Fireplace.Interact` prefix | managed: `LightSwitch.Toggle`, skip vanilla (no fuel item ever) |
| `Fireplace.UpdateFireplace` prefix | managed: owner tick without burn (3.5), then vanilla `UpdateState`; skip vanilla |
| `Fireplace.GetHoverText` postfix | managed: "Name ( On / Off / On, blocked )" + "[E] Turn off / Turn on" |
| `Fireplace.UseItem` prefix | managed: no fuel from the hotbar |
| `Fireplace.TryGetItems`, `CanUseItems` prefixes | managed: no fuel in the radial menu (vanilla checks only `m_infiniteFuel`) |
| `ZNetScene.Awake` postfix (Last) | log list names that match nothing |

Every patch's first check is `LightRules.IsManaged`: other fires run vanilla code.

### 3.4 Instance flag

`m_canRefill = false` on managed instances (set in `Awake`, and on activation for lights already loaded, found with
`Object.FindObjectsByType<Fireplace>`). `LiveLights` remembers the value it replaced per instance and puts back only
that, on deactivation and when a name leaves the list; fires it never changed are never touched (other mods, e.g.
ServersideQoL's ZDO field overrides, may set the same flag). Dead entries are pruned when the map doubles. It makes MC
Batch Station Feeding skip lights (`FeedTarget.FireSkip` reads it per press) and blocks vanilla `UseItem`. Prefabs
are never changed.

### 3.5 Switching and the owner tick (`LightSwitch`)

- **Toggle**: ignore `hold` (no flicker); ignore a second call on the same light in the same frame (a mod repeating
  Interact); `ClaimOwnership()` first (vanilla `Sign.SetText` and `Turret` do the same), so the write lands at once,
  the read is fresh and a quick double press cannot flip twice; the toggler's game becomes the simulator.
  - on → off: fuel 0 (direct ZDO write, `UpdateState`); candle: `RPC_ToggleOn` (state 2).
  - off → on: `RPC_ToggleOn` when state is 2; fuel set to max with the fuel-added effect; `UpdateState`.
- **Owner tick** (replaces `UpdateFireplace` for managed lights): write `s_lastTime` = now every tick (a later vanilla
  owner then burns only the hand-off gap, and the steady write re-sends the ZDO, which heals an ownership race), and
  top up a lit light whose fuel is between 0 and max (a vanilla owner's burn, `Fire.Dot` drains, the placement start
  fuel) to max. An off light (0) stays off.

### 3.6 Multiplayer and hand-off (decision 1)

`ModSide = Both`: whoever owns a light burns its fuel, so every player needs the mod. The server itself never
simulates lights (1.4); its job is to check players and hand out its list:
- **Join check** (`PlayerCheck`, copy of MC Breeding's): the server (dedicated or host) waits 1 s after a player is
  ready, then refuses a player whose game did not answer the framework handshake (vanilla "Error" RPC with
  ErrorVersion: their game shows "Incompatible version"; socket closed 4 s later). `AllowPlayersWithoutMod` lets them
  in (log warning); switching it back to `false` checks the players online.
- **Server list** (`ServerRules`, same flow as Breeding's ServerSettings): the client asks after the handshake, the
  server answers with a `ZPackage` of `LightsRules` (layout 1: the `Lights` string) and pushes again when it changes;
  the client refuses an unknown layout, uses the list only for that ZNet session (single player, host and server use
  their own config), logs "Using the server's ..." once per change, and re-resolves the hashes and loaded lights.
- Framework gate: a player with the mod on a server without it gets vanilla lights.
- Hand-off (AllowPlayersWithoutMod on): players without the mod see the same lights, can refuel off ones, and their
  ownership burns fuel normally; anyone with the mod switches a burnt-out light back on for free.

### 3.7 Live toggle

Off: patches removed, `m_canRefill` restored on loaded lights; fuel and state stay (vanilla values), so lit lights
start burning and off lights take fuel. On: flags applied to loaded lights at once.

### 3.8 Compatibility

- MC Batch Station Feeding: skips managed lights (3.4).
- Infinite-fuel mods: a light whose instance has `m_infiniteFuel` is not managed (3.2): that mod keeps it.
- Mods patching `IsBurning` (schedules) decide what is drawn; this mod does not patch `IsBurning` (small method,
  inline risk).

### 3.9 In-world self-tests (`SelfTests.cs`, Debug only)

- `lights.torch`: instance flag vs prefab, top-up of the start fuel, E off/on, same-frame repeat ignored, hold ignored,
  Shift+E, no resin taken, hover texts, no burn with `m_secPerFuel` forced to 1 s and `lastTime` 60 s back, top-up of a
  partly burned light, off stays off, no radial fuel, an infinite instance is not managed.
- `lights.candle`: off = state 2 with fuel kept, on = state 1, burnt-out candle refilled.
- `lights.campfire`: fire pit stays vanilla (takes wood, burns, fuel hover line); a flag set on it by someone else
  survives `ApplyAll` and `RestoreAll`; campfires, hearth, bonfire and the three braziers count as cooking fires, no
  default light does; a list naming `fire_pit` and a brazier (server rules override) leaves the fire pit unmanaged; rules
  wire round trip, unknown layout refused; `PlayerCheck.Decide` verdicts.

## 4. Edge cases

- Blocked light (roof or ground above, smoke blocked, under water): on but dark; the hover says "( On, blocked )".
- Rain: torches, sconce and lanterns cannot get wet; the candle auto-offs in rain or strong wind without cover as in vanilla (owner), and cannot stay lit
  while wet: E relights it once it is dry.
- What a lit light gives, forever: torches and the sconce a Fire area (fire-fearing creatures, no Mistlands mist),
  lanterns comfort (1.3). Heat and cooking stay tied to fuel (3.2).
- Ownership race: a toggle can be overwritten by the old owner's newer data once; the player presses again.
- Unloaded lights keep their fuel; nothing burns while nobody is near (as in vanilla).
- Ward: vanilla has no ward check on fires; switching has none either.
- Ashlands / Fire world modifier: a lit light keeps its vanilla ignite chance forever.
- A light removed from the `Lights` list while lit keeps its full fuel and burns normally.

## 5. Decisions and open questions

### Decisions

1. **Both side, required everywhere** (user rule after the pause: mods that change the experience are required from
   all players of a server), although a dedicated server never runs lights (1.4): the server refuses players without
   the mod and its list applies to everyone (3.6).
2. **No fire that can cook is a light** (user, after the pause). Braziers were in the first default list; they are
   out, and the `Burning` rule (3.2) keeps any cooking fire out even when listed, so a free permanent heat/cooking
   fire cannot come back through the config.
3. **The unlit castle torch** (world prop) is in the list: it is a torch; E lights it for free.
4. **Off = fuel 0** (vanilla-safe), candle off = state 2 (the vanilla way for that piece).
5. **Take ownership on switch** (Sign/Turret precedent) instead of routing RPCs to the current owner: instant,
   race-free double presses, and the toggler's game stops the burn.
6. English strings "Turn on", "Turn off", "blocked"; vanilla `$hud_on` / `$hud_off` for On/Off.

### Added beyond the request

- "( On, blocked )" hover state; the castle torch; Shift+E switching too.

### Open questions

- None blocking.

### Unverified names and values

- The dedicated server behaviour was read in the 1.0.15 server build; 1.0.16 is assumed identical.

## 6. Tests

The list of record is `src/Building/Lights.Switchable/TESTING.md` (`./tools/Get-TestTodo.ps1 -Mod Lights.Switchable`).
