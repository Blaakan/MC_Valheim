# Batch Station Feeding — design

| | |
|---|---|
| Mod | Batch Station Feeding |
| GUID / project | `MC.Crafting.Stations.BatchFeed` (`src/Crafting/Stations.BatchFeed/`) |
| Category / scope | Crafting / QoL |
| Side | Client (only the player who wants it installs it) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` / `assembly_utils` / `assembly_guiutils` in `.ref/`; prefab values read from the game's asset bundle and localization table |
| Status | Implemented (0.1.0), smoke test passed, in-game testing pending |

## Goal

Requirements from the user (2026-09-28), "Shift + E to feed 5 items":

1. Interacting with a furnace, blast furnace, Frost Foundry, kiln, Frigid Kiln, campfire, hearth, bonfire, brazier,
   smelter, Stone Oven, Hot Tub, spinning wheel, windmill, Eitr Refinery (every utility build that needs fuel or
   item input; not boss summoning altars and similar points of interest) while holding Shift adds **5 items instead
   of 1**.
2. With fewer than 5 in the inventory, add what the inventory holds.
3. With room for fewer than 5 in the build, add what is needed to reach the limit.
4. When hovering the build, show `Shift + <Use key>` below the existing `<Use key>` line.
5. The key is remappable and displays correctly in the hover.

Also in scope: the amount is a setting (default 5), and, like every MC mod, the mod can be turned off on its own,
live (framework toggle).

Non-goals: "fill to max", pulling fuel from chests (AutomaticFuel, ServersideQoL), batching the hotbar "use item"
keys (1-8) or the radial use-item menu, batching take-out actions (cooked food, Empty switches), changing capacities,
processing speed or fuel burn, the Fermenter, boss altars.

---

## 1. Vanilla behaviour (code trace)

### 1.1 The Use key and `alt`

- `Player.Update` computes `alt` once per frame: gamepad active on a non-classic layout
  (`ZInput.IsNonClassicFunctionality()`, Alternative 1/2) → `JoyAltKeys`; otherwise `AltPlace || JoyAltPlace`. Use
  pressed → `Player.Interact(m_hovering, hold: false, alt)`; Use held → `Interact(..., hold: true, alt)` every frame.
- `Player.Interact(GameObject, bool, bool)` (private, only called from `Player.Update`): returns on
  `InAttack() || InDodge()` or on a hold less than 0.2 s after `m_lastHoverInteractTime`; otherwise
  `GetComponentInParent<Interactable>()`, sets `m_lastHoverInteractTime`, calls `Interactable.Interact(this, hold,
  alt)` and plays `DoInteractAnimation` when it returns true. It is the single entry point of the local player's Use
  key, keyboard and gamepad alike.
- Default bindings (`ZInput.ResetKBMButtons`): `Use` = E, `AltPlace` = **Left Shift only** (Right Shift is bound to
  nothing in play), both rebindable; Left Shift is also `Run`, Left Ctrl is `Crouch`. Settings lists them as "Use"
  and "Alternative placement".
- `Player.UpdateHover` clears `m_hovering` while `InPlaceMode()` (hammer, hoe or cultivator equipped): no hover text
  and no Interact on stations then.
- Pieces that already give `alt` a meaning: `Tameable` (rename), `Sadle` (remove saddle), `ItemStand` (orientation)
  and `Fireplace` ("add fuel instead of toggling", 1.4). `Switch`, `CookingStation` and `Turret` ignore it.

### 1.2 Hover text and key labels

- `Hud.UpdateCrosshair` calls `Hoverable.GetHoverText()` every frame. Vanilla key lines are
  `"[<color=yellow><b>$KEY_Use</b></color>] <action>"`; on gamepad `Hud` rewrites the brackets around sprite glyphs.
- `ItemStand.GetHoverText` is the vanilla combined-key precedent: `$KEY_AltPlace + $KEY_Use`, or `$KEY_AltKeys` when
  `IsNonClassicFunctionality() && IsGamepadActive()`.
- `Localization.Localize` serves whole strings from an LRU cache that only a language change or an input-layout
  change clears; a key rebind does not. `Localization.GetBoundKeyString(name, emptyStringOnMissing)` reads the live
  binding (localized key names such as `$button_lshift` "L-Shift", or controller sprite tags) and skips that cache.

### 1.3 Smelter family (`Smelter` + child `Switch`es)

- `Smelter.Awake` wires `m_addOreSwitch` → `OnAddOre`, `m_addWoodSwitch` → `OnAddFuel`, `m_emptyOreSwitch` →
  `OnEmpty`, with `OnHoverAddOre` / `OnHoverAddFuel` hovers.
- `Switch.Interact` repeats on hold only when `m_holdRepeatInterval > 0` (default −1; the station switches in the
  game data seem to use 0.2 s, read by record layout: T16 checks it), sets `m_lastUseTime` on every call, then calls
  `m_onUse`.
- `OnAddOre`: item = first conversion (in `m_conversion` order) whose input the player carries; none →
  `$msg_noprocessableitems`; **`GetQueueSize() >= m_maxOre` → `$msg_itsfull`**; else `"$msg_added " + name`, removes
  1, `RPC_AddOre` to the owner. `OnAddFuel`: **`GetFuel() > m_maxFuel − 1` → `$msg_itsfull`**; no fuel item →
  `$msg_donthaveany`; else removes 1, `RPC_AddFuel`. Both read the local ZDO copy; the owner's handlers add without
  any cap.
- A conversion can have `m_from == null` (no-source conversion: fuel-only machines such as the Frigid Kiln);
  `FindCookableItem` skips it but code that reads `m_conversion` must null-check it.
- The owner burns fuel in `UpdateSmelter` (`m_secPerProduct / m_fuelPerProduct` seconds per fuel) while it has fuel
  and, when `m_maxOre > 0`, ore.

### 1.4 Fireplace

- `Fireplace.Interact`: claims ownership if unowned; **`m_canTurnOff && !hold && !alt && fuel > 0` → toggle, no
  add**; else, when refillable and not infinite: no fuel item carried → `$msg_outof`; **`CeilToInt(fuel) >= m_maxFuel`
  → `$msg_cantaddmore`**; else `$msg_fireadding`, removes 1, `RPC_AddFuel`. Its hold gate compares against
  `m_lastUseTime`, which vanilla never sets, so holds repeat at `Player.Interact`'s 0.2 s (`m_holdRepeatInterval`
  defaults to 0.2, and every fire record read in the game data uses 0.2).
- Owner `RPC_AddFuel` **does nothing when already full: the sender's item is lost.** Fuel burns on the owner only.
- `GetHoverText` is empty for infinite fires.
- Game data (1.0.16, the fire records read from the main asset bundle): the only fire that can be turned off is the
  Resin Candle, which cannot be refilled; so in vanilla no fire both takes fuel with E and toggles (T24 checks it
  for every prefab). The toggle rules below matter for modded pieces.

### 1.5 CookingStation

- Food goes in through the station itself (`Interact`, which ignores holds and does nothing when a food switch
  exists) or through `m_addFoodSwitch`; both reach `OnInteract`.
- `OnInteract`: **if `HaveDoneItem()`, takes one finished item and returns** (Cooking +0.6). Else finds a cookable
  item (message if none), then `OnUseItem`: `m_requireFire && !IsFireLit()` → `$msg_needfire`; no free slot →
  `$msg_nocookroom`; else `CookItem` (claims ownership if unowned, removes 1, `RPC_AddItem`, no message on success),
  then Cooking +0.4 per item. `CookItem` returns true for an item in `m_incompatibleItems` without removing anything.
- Owner `RPC_AddItem`: **no free slot → nothing (the item is lost)**. It has no `IsOwner()` check; it is safe only
  because `CookItem` claims ownership before sending.
- The hover switches to "take" only when every slot is done (`IsEverythingCooked`), but E takes a finished item as
  soon as any slot is done.
- Fuel switch (`OnAddFuelSwitch`): same pattern and rule as `Smelter.OnAddFuel`; owner handler without cap.

### 1.6 ShieldGenerator and Turret (Ballista)

- `ShieldGenerator`: `m_addFuelSwitch` → `OnAddFuel` (first of `m_fuelItems` carried, `GetFuel() > m_maxFuel − 1` →
  `$msg_itsfull`), owner `RPC_AddFuel` without cap. Its own `Interact` fires the attack, not fuel.
- `Turret.UseItem(user, null)`: `FindAmmoItem(inventory, onlyCurrentlyLoadableType: true)` = the loaded missile kind
  when it has ammo, else any allowed missile in the **lowest inventory slot**; refuses another kind while loaded
  (`$msg_turretotherammo` + loaded name, checked against the local ZDO); **`GetAmmo() >= m_maxAmmo` →
  `$msg_itsfull`**; else removes 1, `RPC_AddAmmo(prefab)`.
- Owner `RPC_AddAmmo`: ammo + 1 and `s_ammoType = name`, **no cap and no type check**. A destroyed ballista drops
  `ammo` copies of the last kind (owner, `m_returnAmmoOnDestroy`, default true). So a stale non-owner that sends two kinds makes every missile the last kind (dupe
  or loss). `m_addAmmoEffect` has no initializer (may be null). No Use line in the hover without ward access or when
  not targeting enemies.

### 1.7 Not covered

| Class | Why |
|---|---|
| `Fermenter` | Adds only when empty: capacity 1, nothing to batch. |
| `OfferingBowl` | Boss altars and similar points of interest: excluded by the user. |
| `ItemStand`, `ArmorStand`, `Beehive`, `SapCollector`, `Incinerator`, `Container`, `Tameable`, `Sadle`, `ItemDrop` | Not feeders, or one item; Shift+E keeps its vanilla meaning. |
| Hotbar use (`Humanoid.UseItem` → `Interactable.UseItem`), radial use-item menu | Not the Use key. |

### 1.8 Network: who runs what

- `ZNetView.InvokeRPC` routes to the ZDO owner: run at once when the local game owns it, through the server
  otherwise. With **no owner** it is handled locally and broadcast, and handlers that check `IsOwner()` do nothing
  anywhere (the add is lost). `Fireplace.Interact` and `CookItem` claim ownership first; smelters, fuel switches,
  the shield and the ballista do not.
- Ownership: the first game whose area contains the piece; handed on when it leaves. When the owner logs out, the
  station keeps its uid until the server's next `ZDOMan.ReleaseZDOS` pass (every 2 s; longer while a crashed peer
  has not timed out), and `ZRoutedRpc.RouteRPC` drops every add sent to it meanwhile. The server sends a new player
  list on disconnect.
- A non-owner sees its own adds only after the round trip. Vanilla's per-press checks read the local copy: exact on
  the owner, stale on a non-owner.

| Owner handler | Caps? | Stale non-owner sends too many → |
|---|---|---|
| `Smelter.RPC_AddOre` / `RPC_AddFuel`, `CookingStation.RPC_AddFuel`, `ShieldGenerator.RPC_AddFuel` | no | overfill |
| `Turret.RPC_AddAmmo` | no, no type check | overfill; mixed kinds relabelled (1.6) |
| `Fireplace.RPC_AddFuel`, `CookingStation.RPC_AddItem` | yes | items lost |

Vanilla has this race with fast presses; a batch makes it five times easier to hit.

### 1.9 Messages, effects, skills

- Every add sends a Center message (cooking food does not). `MessageHud.ShowMessage` localizes the text, overwrites
  the center line at once and queues fade work per call: N messages in one frame = flicker.
- Add effects are created by the owner inside the RPC handler (`m_oreAddedEffects`, `m_fuelAddedEffects`,
  `m_addAmmoEffect`; cooking food: `m_addEffect` at each slot). N adds in one frame = N stacked sounds.
- Cooking +0.4 per added item. `Skills.RaiseSkill` shows a level-up as a **Center** message when the level before was
  0 (`"$msg_skillup $skill_<name>: <level>"`, "Skill improved Cooking: 1"), TopLeft afterwards.

### 1.10 Pieces in vanilla 1.0.16

Read from the game data (asset bundle and localization table; prefab names from the game's prefab manifest). The mod
decides coverage by component at runtime and logs the real list (3.7, test T24).

| Piece | Prefab | Component | Capacities | Batched |
|---|---|---|---|---|
| Smelter, Blast Furnace | `smelter`, `blastfurnace` | `Smelter` | 10 ore, 20 Coal | ore + fuel |
| Eitr Refinery | `eitrrefinery` | `Smelter` | 20 input, 20 fuel | input + fuel |
| Charcoal Kiln, Windmill, Spinning Wheel | `charcoal_kiln`, `windmill`, `piece_spinningwheel` | `Smelter` | 25, 50, 40 input, no fuel | input |
| Frigid Kiln | `piece_FrostKiln` | `Smelter`, fuel switch only, no-source conversion to Liquid Frost (`FrozenFuel`), 5 fuel per product | 25 Ice (fuel item unverified: "Add Ice" text in the data, T24 prints it) | fuel |
| Hot Tub | `piece_bathtub` | `Smelter`, fuel switch only, no conversion (one Wood per 5000 s) | 10 Wood | fuel |
| Campfire, Iron Fire Pit, Hearth, Bonfire, braziers, torches, Sconce | `fire_pit`, `fire_pit_iron`, `hearth`, `bonfire`, `piece_brazierfloor01`, `piece_walltorch`... | `Fireplace` | per prefab (hover); records read: Wood fires 10 or 20, three fires on other fuels 5, torches and Sconce 6, two Standing Wood Torch records 4 and 1 (which prefab each is: unverified, T24) | fuel; a light that holds 1 stays vanilla |
| Resin Candle, fires with infinite fuel | — | `Fireplace` | — | no (not refillable / infinite) |
| Cooking Station, Iron Cooking Station | `piece_cookingstation`, `piece_cookingstation_iron` | `CookingStation`, no switches (food on the station) | 2, 5 slots | food |
| Stone Oven | `piece_oven` | `CookingStation`, food + fuel switches, no fire needed | 4 slots, 10 Wood | food + fuel |
| Frost Foundry | `piece_FrostFoundry` | `CookingStation`, food ("Place Cast") + fuel switches, no fire needed, no skill, 30 cast conversions (inputs probably the "Cast: ..." items such as `AtgeirGoldUncooked`, unverified) | **1** cast slot, 20 Liquid Frost | fuel only (cast slot: capacity 1) |
| Shield Generator | `piece_shieldgenerator` | `ShieldGenerator` | per prefab | fuel (bones) |
| Ballista | `piece_turret` | `Turret` | per prefab | missiles |

The Frost Foundry and the Frigid Kiln are real 1.0.16 pieces with no class of their own. The Hot Tub is a `Smelter`
fuel slot, not a `Fireplace`, so its summary uses the smelter wording ("Added 5 Wood (5/10)").

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28 (Thunderstore pages, ComfyAddAllFuel source on GitHub):

| Mod | How | Lesson |
|---|---|---|
| ComfyAddAllFuel (pre-1.0) | `ZInput.GetKey(KeyCode)` key; transpilers on `Smelter.Awake`, `CookingStation.Awake`, `ShieldGenerator.Start` swap switch callbacks; one `Fireplace.AddFuel(n)`; **replacing prefixes on `CookingStation.GetFreeSlot` / `HaveDoneItem`** | A mod-only KeyCode works in 1.0. `Awake` swaps do not toggle live. Room from the stale local value: a non-owner overfills or loses items. Never patch `GetFreeSlot` / `HaveDoneItem` (only call them). |
| BulkSmelt (2026) | Shift+E fills the smelter family to capacity; key cannot be rebound | Users want a rebindable key; fires, cooking and shield are not covered there. |
| Add All Fuel And Ore (2021) | Fill all, can pull from chests | Pre-1.0; the "fill all" idea. |
| AutomaticFuel, ServersideQoL AutoProcess | Stations pull from containers (owner or server side) | Different feature; reading capacities live keeps us correct. |
| ValheimPlus | Changes `m_maxOre` / `m_maxFuel`, infinite fires | Read those fields live; skip infinite fires. |

Conclusion: one patch on the local player's Use entry point, **N calls of the station's own `Interact`** (vanilla item
choice, checks, messages, RPCs, skill and other mods' patches run per item), clamped by our own model of the room,
never writing a ZDO.

Coexistence with the "fill all" mods: their patch runs inside our first press and adds many items. Every vanilla add
path removes exactly one item, so a press that removed more than one means another mod batched it: the loop stops
right after that press (otherwise, on a stale non-owner copy, their fill would repeat on every loop press).

---

## 3. Implemented design

### 3.1 Core idea

A batch press = the vanilla press repeated up to N times in the same frame, on the same spot, through the spot's own
`Interactable.Interact(player, hold: false, alt: true)` (`alt: true` so a fire adds fuel instead of toggling; every
other covered class ignores it). Before the loop the mod computes how many adds fit (`room`) from the local ZDO and,
for a non-owner, the adds it sent recently (3.4). The loop stops at N, at `room`, at the first press that returns
false or removes nothing, right after a press that removed more than one item, and on a ballista before a press that
would load another missile kind. One summary message replaces the per-item messages. Everything else stays vanilla.

### 3.2 Coverage (`FeedTarget`)

Resolved from the hovered object like vanilla (`GetComponentInParent<Interactable>()`), by component class and by
reference to the station's own switch fields (so only the add switches count, never an Empty switch). No prefab
name appears in the code.

| Kind | Spot | Value (local ZDO) | Vanilla "can add" rule replayed | Full message | Owner add effect |
|---|---|---|---|---|---|
| `SmelterInput` | `Smelter.m_addOreSwitch`, `m_maxOre > 1` | `GetQueueSize()` | `v < m_maxOre` | `$msg_itsfull` | `m_oreAddedEffects` |
| `SmelterFuel` | `Smelter.m_addWoodSwitch`, fuel item set, `m_maxFuel > 1` | `GetFuel()` | `!(v > m_maxFuel − 1)`, `v += 1` | `$msg_itsfull` | `m_fuelAddedEffects` |
| `Fire` | `Fireplace`, refillable, not infinite, fuel item set, `m_maxFuel > 1` | ZDO `s_fuel` | `CeilToInt(v) < m_maxFuel`, `v = min(v + 1, max)` | `$msg_cantaddmore` | `m_fuelAddedEffects` |
| `CookFood` | the `CookingStation` (no food switch) or its `m_addFoodSwitch`, more than 1 slot | used slots | `v < m_slots.Length` | `$msg_nocookroom` | none (per-slot effect kept) |
| `CookFuel` | `CookingStation.m_addFuelSwitch`, fuel item set, `m_maxFuel > 1` | `GetFuel()` | as `SmelterFuel` | `$msg_itsfull` | `m_fuelAddedEffects` |
| `ShieldFuel` | `ShieldGenerator.m_addFuelSwitch`, fuel items set, `m_maxFuel > 1` | `GetFuel()` | as `SmelterFuel` | `$msg_itsfull` | `m_fuelAddedEffects` |
| `TurretAmmo` | `Turret`, `m_maxAmmo > 1` | `GetAmmo()` | `v < m_maxAmmo` | `$msg_itsfull` | `m_addAmmoEffect` |

- `room` = the rule run step by step on a copy of the value, at most `Amount` steps (exact float comparisons, no
  formula drift near the maximum).
- A spot with capacity 1 or less is not covered at all: no hint, vanilla press (the Frost Foundry's cast slot).
- `CookFood` is not batchable while `HaveDoneItem()`: vanilla E takes a finished item first, so the press stays
  vanilla and the hint is hidden.

### 3.3 The batch press (`Player.Interact` prefix)

Prefix on `Player.Interact(GameObject, bool, bool)`, priority Low, with `__runOriginal` and `ref bool alt`:

1. Plain vanilla (return true) unless all of these hold: no earlier prefix cancelled the press, the player is
   `Player.m_localPlayer` and is not attacking or dodging, the hovered object resolves to a covered spot, and (for
   holds) vanilla's 0.2 s gate has passed. The hold gate is checked first, so a held key costs one comparison per
   frame.
2. Batch when `!hold` and the batch key is held: `ModifierKey == None` or a gamepad is active → vanilla's `alt`;
   otherwise `ZInput.GetKey(ModifierKey)`.
3. `BatchFeeder.TryRun` declines (vanilla single press) when: the ZDO is invalid or **has no owner** (1.8: the add
   would be lost; vanilla's press claims it for next time); the spot is not batchable (cooked food waiting); a
   ballista has no missile to load (vanilla shows why); or the station's **owner is gone** (3.4). When it declines on
   a `Fire`, the prefix sets `alt = true` before vanilla's press, so a mod-only key never toggles a fire.
4. `room == 0`: vanilla press ("It's full"), unless only the pending model says full (3.4).
5. Snapshot the inventory counts per item name and the total. `Muting = true`. Loop up to `room` times:
   - ballista, press 2 onwards: stop (`otherammo`) if the missile vanilla would pick is not the first press's kind;
   - `ok = Interact(player, false, true)`; `removed` = drop of the inventory total;
   - stop (`refused`) if `!ok` or nothing removed ("true" alone is no proof: see `CookItem`, 1.5);
   - count the press, then stop (`othermod`) if `removed > 1`;
   - after the first successful add on a station this game owns, swap the kind's add effect list for an empty one.
   `finally`: `Muting = false`, restore the effect list (same frame).
6. After the loop the press always counts as handled (return false), even if the summary step throws, so vanilla
   never adds one more. Set `m_lastHoverInteractTime`; if anything was added: one `DoInteractAnimation`, record the
   adds for a non-owner (3.4), show the summary (3.6); if nothing was added, re-show vanilla's last swallowed reason.
7. One `Log.Debug` line: prefab, kind, `owner=you|other`, room, `asked` (the Amount setting), added, presses, stop
   reason (`amount`, `room`, `refused`, `othermod`, `otherammo`, `exception`). Declines log `owner=gone` or
   `stop=pending` / `stop=otherammo` when they come from 3.4.

An exception inside a press (another mod's patch) stops the loop; items added so far stay and are summarized; the
error is reported once (`PatchGuard`). Only the press is multiplied: holding the keys afterwards continues with
vanilla's hold repeat (+1 per 0.2 s where the spot repeats).

### 3.4 Non-owner correctness

- **`PendingAdds`**: per (ZDOID, kind), the adds this player sent while not the owner: baseline, count sent, expiry,
  and for the ballista the missile kind sent. Effective value = `max(local, baseline + sent)` (conservative: burn and
  finished products only make us add less). An entry dies when the local copy catches up or 3 s after the last add.
  Owners ignore and drop it (local = truth). Nothing is written to any ZDO.
- **Plain presses and hold repeats** of a non-owner on a covered spot: when that press would add (never a fire toggle,
  never a cooking take-out, never a hold repeat vanilla ignores) and the pending model says full while the local copy
  does not, the press is blocked with the full message (a vanilla add would overfill or lose the item); otherwise
  vanilla runs and the postfix records what left the inventory. So E, E, Shift+E in quick succession clamps correctly.
- **Ballista missile lock**: a batch loads one missile kind, the kind vanilla picks for the first press. A non-owner
  whose pending entry holds a kind gets vanilla's "The ballista is already loaded with ..." (nothing removed) for any
  press that would load another kind within the window.
- **Owner gone**: not the owner, and the owner uid on the ZDO belongs to no connected game (server: a ready peer with
  that uid; client: the server's uid or a player-list entry with that user id) → single vanilla press, `owner=gone`.
  Vanilla loses at most that one item; a batch never multiplies the loss or fills the pending model with lost adds.
  A dead owner waiting to respawn also reads as gone (safe).
- Not seen by the model: hotbar-key adds and the radial menu (the vanilla race stays; README "Good to know").

### 3.5 Hover hint

- Postfixes (priority Low, so they see other mods' changes) on `Switch`, `Fireplace`, `CookingStation` and
  `Turret.GetHoverText`. Early return when `ShowHint` is off or the text is empty. A one-slot cache (last hovered
  component → spot or "not covered") avoids per-frame lookups; the cooking done-item check runs at most every 0.25 s.
- **Anchor by structure**: insert the line at the end of the line holding the first `"[<color=yellow><b>"` (the Use
  line in every covered hover). No anchor → no hint (ballista without access, a mod that restyled the text). The key
  text is never searched for: a cached vanilla string can still show an old key after a rebind (1.2).
- Line: `[<modifier> + <use>] <action> x<Amount>`, e.g. `[L-Shift + E] Add Coal x5`; the action is the spot's own
  vanilla tooltip. Labels come from `GetBoundKeyString` (live binding): `Use`, and `AltPlace` — or `AltKeys` on a
  non-classic gamepad layout (the `ItemStand` rule) — or, for a mod-only key on keyboard, the vanilla `$button_*` name
  (L-Shift, R-Shift, L-Alt...) or the Input System name.
- The line is rebuilt when the spot, the input device, the gamepad layout or a setting changes, and every 0.5 s
  (rebinds). Per frame: one ordinal `IndexOf`, one `IndexOf(char)`, one `Insert`.

### 3.6 Messages and effects

- `MessageHud.ShowMessage` prefix: while `Muting` (set and cleared inside the batch press only), Center messages are
  swallowed and the last one kept as the refusal reason; TopLeft passes. Inside the loop, a message containing
  `$hud_tamedone` always passes (the player must see it; Creature Kill and Tame Counts counts it from
  `Player.Message` anyway), and a `$msg_skillup` message is held and shown as a second line under the summary (or
  alone / under the refusal reason when nothing was added); it is never taken as the refusal reason. Outside the
  loop: one static bool check.
- Summary, vanilla tokens only (no new localization): fires `Localize("$msg_fireadding", "<n> <item>") + " (x/max)"`
  → "Adding 5 Wood to the fire (8/10)"; everything else `"$msg_added <n> <item>[, <n> <item>] (x/max)"` → "Added 3
  Copper Ore, 2 Tin Ore (5/10)". Items in inventory order; `(x/max)` from the live ZDO (owner) or the pending model.
- Sound: one add effect per press on stations this game owns (3.3 step 5); cooking food keeps one effect per slot;
  a non-owner's adds make the owner's game play one per item, as vanilla does.

### 3.7 Coverage dump

`ZNetScene.Awake` postfix (priority Last), and on activation when a world is loaded: one `Log.Debug` line per covered
component found in `ZNetScene.m_prefabs` (also on child objects): prefab, class, `m_name`, capacities, input and fuel
item prefab names, fire flags (`canRefill`, `canTurnOff`, `infiniteFuel`), and "covered" or "skipped (why)", then a
count line. Null-safe for no-source conversions, missing fuel items and effect lists; each prefab in its own
`try/catch`. This is how the prefab facts of 1.10 are confirmed in game (T24).

### 3.8 Patches

All bodies catch their own exceptions (`PatchGuard`); applied only while the feature is Active, so turning it off
removes the hint and the batch at once. No transpiler.

| Target | Type | What it does |
|---|---|---|
| `Player.Interact(GameObject, bool, bool)` | Prefix (Low), `ref bool alt`, `__runOriginal`, `out PressRecord __state` | Batch press (3.3); guard and snapshot for non-owner plain presses (3.4). |
| same | Postfix | Records a non-owner plain press in `PendingAdds`. |
| `Switch.GetHoverText()`, `Fireplace.GetHoverText()`, `CookingStation.GetHoverText()`, `Turret.GetHoverText()` | Postfix (Low) | Hint line (3.5). |
| `MessageHud.ShowMessage(...)` | Prefix | Muting during the loop (3.6). |
| `ZNetScene.Awake()` | Postfix (Last) | Coverage dump (3.7). |

Never patched (only called): `Smelter.OnAddOre` / `OnAddFuel`, `CookingStation.GetFreeSlot` / `HaveDoneItem`.
Hooking `Player.Interact` rather than each station leaves other mods' programmatic `Interact` calls untouched.

### 3.9 Configuration

`BepInEx/config/MC.Crafting.Stations.BatchFeed.cfg`, section `General`, all live:

| Setting | Default | Notes |
|---|---|---|
| `Enabled`, `Status` | `true`, — | Framework. |
| `Amount` | `5` (2–50) | Read on every press; changes refresh the hint. |
| `ModifierKey` | `None` (`KeyCode`) | None = the game's Alternative placement binding. A key here is read with `ZInput.GetKey` for this mod only; ignored on a controller. |
| `ShowHint` | `true` | Hides only the hint line. |

The user-facing descriptions are in the README.

### 3.10 Multiplayer, hand-off and persistence

Client-side, compatible. The mod only drives the vanilla add path of the local player: vanilla removes the item and
sends its own RPC to the station's owner (vanilla game or server). No mod RPC, no ZDO write, nothing stored on items
or characters; the pending model lives in memory for at most 3 s. A station fed by the mod holds ordinary entries: a
player without the mod sees the same counters and adds, empties or takes out as usual (tests M01, M05). Two players
feeding the same station in the same second remain the vanilla race.

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| Empty smelter (10), 20 copper ore, Shift+E | Adds 1 | Adds 5, one sound, "Added 5 Copper Ore (5/10)" |
| Smelter at 8/10 / full | Adds 1 / "It's full" | Adds 2 / "It's full" (vanilla press) |
| 3 copper ore only | Adds 1 | Adds 3 |
| 3 copper + 3 tin | Adds the first in the smelter's list | Adds 5, mixed, in vanilla's order |
| No processable item, no fuel, no fire under a cooking station | Vanilla message | Same message once, nothing removed |
| Smelter fuel at 17/20 | 1 per press while fuel ≤ 19 | Adds 3 (vanilla rule replayed) |
| Campfire 7/10 | Adds 1 | Adds 3, "Adding 3 Wood to the fire (10/10)" |
| Infinite fire, Resin Candle | No fuel line | No hint, vanilla |
| Hold Shift+E on a fire | +1 per 0.2 s | +5, then +1 per 0.2 s |
| Cooking station with a finished item | E takes it | No hint; Shift+E takes one (vanilla) |
| Frost Foundry cast slot (1 slot) | Places 1 | Not covered: no hint, places 1; its Liquid Frost slot is batched |
| Frigid Kiln (Ice), Hot Tub (Wood) | 1 per press | Up to 5 |
| New character, 3+ raw items in one batch | Cooking 1 message in the center | "Added N Boar Meat (N/max)" with the level-up line below |
| Ballista with wood missiles, carrying black metal too | Refuses the other kind | Adds wood only, stops when it runs out |
| Empty ballista, 2 wood (lower slot) + 20 black metal | Owner: 2 wood, then "already loaded with Wooden Missile"; stale non-owner: 2 wood, then black metal (the owner relabels all as black metal) | Adds 2 wood, stops (`otherammo`); non-owner: other kind refused for 3 s |
| Non-owner, Shift+E twice quickly on an empty smelter (10) | (fast plain presses can overfill) | 5 + 5, then "It's full" until the owner's update |
| Non-owner, E, E, Shift+E quickly | Each press reads the stale value | 1 + 1 + 5 = 7 |
| Non-owner fire at 7/10, Shift+E twice quickly | (fast presses can lose wood) | 3, then "You can't add more Wood": nothing lost |
| Non-owner plain E toggle / take-out right after a batch | Toggles / takes | Same (guard applies only to presses that add) |
| Non-owner hotbar key right after a batch | Stale check | Unchanged: the vanilla race (README "Good to know") |
| Station without owner | Smelter-type add lost | Vanilla single press (no batch) |
| Owner just logged out or crashed | The add is lost (1 item) | Vanilla single press (`owner=gone`): at most 1 item lost |
| Shift+E on a tame, item stand, saddle, dropped item, chest, Empty switch, Fermenter | Their own action | Unchanged |
| Build tool equipped | No hover, E does nothing | Same |
| Default key, Right Shift + E | Plain E | Plain E (`ModifierKey = RightShift` to use it) |
| Mod-only key on a full or unowned fire that can be turned off (modded) | E toggles | Single press with `alt = true`: never toggled |
| Gamepad, default / alternative layout | Alt-place / alt-keys + use | Same combination batches; hint shows those glyphs |
| Other batch mod installed | Its fill | Its fill inside our first press, then stop (`othermod`) |
| Another mod's prefix cancels `Player.Interact` | — | Nothing (`__runOriginal` false) |
| Another mod throws inside an add | — | Loop stops, items so far stay, summary shown, error reported once |
| Feature toggled off | — | No hint, one item per press, immediately |

---

## 5. Decisions and open questions

1. **Modifier = the game's "Alternative placement" binding by default** (Left Shift only; the Shift binding vanilla
   already passes to pieces as `alt`, rebindable in the game's controls, controller support for free), plus an
   optional mod-only `ModifierKey`. The user said "Shift": Right Shift does not batch by default. *To confirm with
   the user.*
2. **Amount** setting, default 5, range 2–50 (1 would be vanilla).
3. **Only the press is multiplied**; holding continues with vanilla's +1 repeat. *To confirm with the user*
   (alternative: every repeat is a batch, which fills fires almost at once).
4. **Item choice per press follows vanilla**, so a batch can mix ore kinds when the first runs out; "fewer than 5 in
   the inventory" counts everything the station takes. *To confirm with the user* (alternative: stop when the first
   kind runs out).
5. **Cooking stations: the batch only adds.** A finished item is taken first, one per press, as E does. *To confirm
   with the user.*
6. **Coverage by component class.** Added beyond the user's list: **Shield Generator** (bones) and **Ballista**
   (missiles, with the missile lock of 3.4). Left to vanilla: the Fermenter and every spot with capacity 1. *To
   confirm with the user.*
7. **Frost Foundry, Frigid Kiln, Hot Tub** (named by the user): the Frigid Kiln (Ice, 25) and the Hot Tub (Wood, 10)
   are batched as smelter fuel slots; the Frost Foundry's Liquid Frost slot (20) is batched, its single cast slot is
   vanilla (capacity 1). The Hot Tub's summary uses the smelter wording, not the fire wording. *To confirm with the
   user.*
8. **Non-owner pending model** (3 s): may add fewer than the real room right after a batch, never more; a plain press
   that would add is blocked only when the model says full; toggles and take-outs are never blocked.
9. **No batch on an unowned piece or when the owner has just left**: a single vanilla press, so a vanilla item loss
   is never multiplied.
10. **Hint shows the configured amount** ("x5" = up to 5), ASCII "x", only on covered, batchable spots.
11. **One summary message per press; one add sound on stations this game owns.** Center messages are hidden only
    during the batch press; the tame message always passes; the first skill level-up shows under the summary.
12. **Hotbar and radial "use item" stay vanilla.**
13. **The batch key never toggles a fire** (loop passes `alt: true`; a declined batch on a fire gets `alt = true`).
14. **Other batch mods**: not detected by GUID (their GUIDs are unverified); the loop stops after a press that removed
    more than one item, and the README says to keep only one.
15. **Identity**: GUID `MC.Crafting.Stations.BatchFeed`, name "Batch Station Feeding", permanent after release;
    station-oriented because "feeding" alone reads like feeding tames. *To confirm with the user.*

Added beyond the request (small): `ShowHint` setting; the fill state "(x/max)" in the summary; Shield Generator and
Ballista coverage; the Debug coverage dump (silent at normal log levels).

Open questions: none blocking. Checked in game by T24: the capacities and components of 1.10, the Frigid Kiln's fuel
item, which casts the Frost Foundry accepts, what the Stone Oven bakes, which light (if any) holds a single fuel, and
that no vanilla fire both takes fuel and can be turned off; by T16: the station switches' hold repeat.

## 6. Tests

The in-game checklist lives next to the code: `src/Crafting/Stations.BatchFeed/TESTING.md`
(run `./tools/Get-TestTodo.ps1 -Mod BatchFeed`).
