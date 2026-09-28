# One Click Repair All — design

| | |
|---|---|
| Mod | One Click Repair All |
| GUID / project | `MC.Crafting.Repair.OneClickAll` (`src/Crafting/Repair.OneClickAll/`) |
| Category / scope | Crafting / QoL |
| Side | Client (only the player who wants it installs it) |
| Game version checked | Valheim 1.0.16, decompiled `assembly_valheim` in `.ref/` |
| Status | Implemented (0.1.0), smoke test passed, in-game testing pending |

## Goal

Requirements from the user (2026-09-28):

1. Clicking the repair button at a crafting station repairs **all** items that can be repaired there in one click
   (vanilla: one item per click).
2. The devcommands `nocost` repair must be supported too.
3. Items that cannot be repaired at the current station must not be repaired.
4. The mod can be turned off on its own, like every MC mod (framework toggle).

Non-goals: repairing at a station that vanilla would refuse (SmartRepair-style "higher station repairs lower gear"),
auto-repair when a station opens, repairing items in chests, paced per-item effects (RhythmicRepairs), a hotkey.

---

## 1. Vanilla behaviour (code trace)

### 1.1 The button

- `InventoryGui.Awake` binds `m_repairButton.onClick` to `InventoryGui.OnRepairPressed`. That is the only caller.
  The gamepad goes through the same listener (a `UIGamePad` on the button calls `Button.OnSubmit`), and UI mods
  (Auga, SeneaL UI) call `OnRepairPressed` too. There is no repair hotkey.
- `OnRepairPressed` = `SetActiveGroup(m_uiGroups[3])` (plays a UI sound), `RepairOneItem()`, `UpdateRepair()`,
  `UpdateCraftingPanel()`.
- `UpdateRepair` also runs every frame from `InventoryGui.Update`. It hides the repair panel and button when there is
  no station and no nocost, or when the station has `m_canRepair == false`. Otherwise it shows the button and makes
  it interactable (with a pulsing glow) when `HaveRepairableItems()`.

### 1.2 One repair

`InventoryGui.RepairOneItem`:

1. Returns if there is no station and no nocost, or if the station fails `CraftingStation.CheckUsable(player, false)`
   (roof and fire; both skipped under nocost).
2. Fills the shared buffer `m_tempWornItems` with `Inventory.GetWornItems` (every item of the player's own inventory,
   equipped or not, with `m_useDurability` and durability below max).
3. Repairs the **first** item where `CanRepair(item)` is true: `Player.RaiseSkill(Crafting, 1 − durability/max)`,
   durability = max, the station's `m_repairItemDoneEffects` (only if there is a station), and a Center message
   `$msg_repaired` ("Repaired $1") with the item name. Then it returns.
4. If nothing matched: Center message "No more item to repair" (hardcoded English).

`HaveRepairableItems` runs the same station guards and the same `CanRepair` walk, without repairing.

### 1.3 `CanRepair(item)` — which station repairs what

In order:

1. `item.m_shared.m_canBeReparied` must be true.
2. **`Player.NoCostCheat()` → true for any such item, before any station check.**
3. There must be a current station, and a recipe (`ObjectDB.GetRecipe`, first match by item name) with a crafting
   or repair station.
4. The recipe's `m_repairStation` or `m_craftingStation` name equals the current station's name, **or** the item was
   made in a lower world level (`item.m_worldLevel < Game.m_worldLevel`, world modifiers).
5. `min(station.GetLevel(), 4) >= recipe.m_minStationLevel`.

`m_canRepair` on the station is not checked here; only `UpdateRepair` enforces it by hiding the button.

### 1.4 nocost

`Player.NoCostCheat()` returns the private `m_noPlacementCost`, toggled by the console command `nocost` (a cheat:
single player or the host only) and by the debug-mode key. It is local, not networked, and not saved. With it on:
`UpdateRepair` shows the repair button in the plain inventory (no station), `RepairOneItem` runs without a station
(no effects), `CheckUsable` skips roof and fire, and `CanRepair` accepts every repairable item (1.3 step 2). The world
modifier `NoCraftCost` does not touch repair at all.

### 1.5 Messages and effects

- `MessageHud.ShowMessage(Center)` overwrites the centre text immediately (the last one in a frame wins) and queues
  two fade entries per call, drained one per frame. N messages in one frame: flicker and 2N frames of fade work.
- `EffectList.Create` instantiates each effect prefab (`sfx_gui_repairitem_workbench` / `_forge`). N calls in one
  frame: N stacked sounds. `CraftingStation.m_repairItemDoneEffects` is a per-station public field read only by
  `RepairOneItem`.

### 1.6 Skill

`RaiseSkill` has no cooldown: N repairs in one frame give exactly the same Crafting progress as N clicks.

### 1.7 Multiplayer

Repair is 100% local: it changes durability of items in the local player's inventory (saved in the character file).
The repair effects are spawned by the local client, as in vanilla; whether nearby players hear them depends on the
effect prefab (a prefab with a `ZNetView` is replicated). With the mod there is one effect per click instead of one
per item either way.

### 1.8 Other item repair paths

The console cheat `repairall` sets every worn item to max durability with no station, skill or effects. Nothing else
repairs items: `Player.Repair` / `WearNTear.Repair` are for building pieces.

---

## 2. Existing mods and what they teach

Surveyed 2026-09-28 (sources on GitHub / Thunderstore):

| Mod | How | Lesson |
|---|---|---|
| RepairRequiresMats (aedenthorn) | Charges in a `CanRepair` postfix, only when the stack trace contains `RepairOneItem` and not `HaveRepairableItems`; refreshes its item order in an `UpdateRepair` postfix | Never name our code after those methods; call `UpdateRepair` between presses; `HaveRepairableItems` stays true when the player cannot pay. |
| RepairRequiresCoins (cjayride), RepairCost (Radamanto) | `RepairOneItem` prefix that charges, or returns false when the player cannot pay (reason shown Center or TopLeft) | A loop ended only by `HaveRepairableItems` **freezes the game**. Need a progress check. |
| RepairRequiresMaterials (sighsorry) | Replaces `HaveRepairableItems` and `RepairOneItem` | The loop ends by itself; fine. |
| RhythmicRepairs | `RepairOneItem` prefix → coroutine, 0.12 s per item, cap counts calls | With it installed, our extra presses are skipped: progress check stops us. |
| FastRepairButton | `OnRepairPressed` prefix that replaces the press and writes durability directly | Bypasses cost mods. Our postfix must judge from durability, not assume vanilla ran. |
| SmartRepair, ValheimPlus autoRepair, AzuWorkbench | Repair everything on open / every frame (V+ and Azu: uncapped `while (HaveRepairableItems())` loops) | Redundant with us; proves the freeze risk. |
| Skaft | Repairs several items per `RepairOneItem` call | Count repaired items from durability, not from calls. |
| RepairAll (LoadedGun) | — | Users complained the count popup could not be translated: keep the message localised. |

Conclusion: call the **vanilla** press, never write durability ourselves, and stop at the first press that repairs
nothing.

---

## 3. Implemented design

### 3.1 Core idea

One click = the vanilla press, then more vanilla presses (`RepairOneItem` + `UpdateRepair`, exactly what
`OnRepairPressed` does per click) until nothing this station can repair is left. Because every repair still goes
through `RepairOneItem` and `CanRepair`, everything vanilla decides stays vanilla: station rules (requirement 3),
nocost (requirement 2), `CheckUsable`, skill gain, and every other mod's patches on those methods (cost mods,
Crossbow Stays Loaded's re-stamp).

### 3.2 Loop (`BulkRepair.cs`)

- **Prefix** (priority First, so it runs before other mods' prefixes): snapshot the worn items and their durability
  in our own list (never `m_tempWornItems`, which vanilla clears on every call). Fewer than 2 worn items: do nothing.
- **Postfix**: if the click repaired nothing (durability of the snapshot unchanged: no station, unusable station, a
  cost mod refused), stop — the next press would be refused too. Otherwise, for at most 10 presses per worn item
  (vanilla needs one per item; the headroom is for mods that repair only part of an item per press) and while
  `HaveRepairableItems()`: call `RepairOneItem()` then `UpdateRepair()`; if no snapshot item's
  durability rose during that press (compared per item, not as a float sum, so even a tiny repair counts), the press
  was blocked: stop. The cap guarantees termination even with odd mods.
- **Why the stop never cuts vanilla short:** `HaveRepairableItems` and `RepairOneItem` run the same guards and the
  same `CanRepair` walk in the same order, in the same frame. So when the first says "yes", the second repairs that
  exact item to max, and an item is only "worn" below max: every press makes progress. The "no progress" stop can
  only fire when another mod refuses a repair; nothing changed then, so a manual click would be refused the same
  way. Remaining gaps (another click continues, the button keeps glowing): a mod whose refused press changes its own
  state so the next one would succeed, an exception from another mod's patch, a mod repairing items outside the
  player's inventory.
- After extra repairs: `UpdateCraftingPanel()` once more (the upgrade list's durability bars were built after the
  first repair only), then one summary message.
- An exception from inside a press (another mod's broken patch) stops the loop, is reported once (`PatchGuard`), and
  the items repaired so far stay repaired.

### 3.3 Messages and effects

- During the extra presses only, `MessageHud.ShowMessage` Center messages are swallowed and the last one is kept.
  TopLeft messages pass (cost mods' "Used 2 coins" receipts). The vanilla press's own message and effect are left
  untouched.
- Summary: `Localize("$msg_repaired", "<item>, <item>, <item> +N")`, the vanilla text with the item name tokens, so
  it is translated like the vanilla message. Shown only when the loop repaired at least one extra item; with a single
  repair the vanilla message stays exactly as it was.
- If a press was blocked and the swallowed Center message explains why (cost mod), it is re-shown TopLeft (the
  Center line holds the summary). Vanilla's "No more item to repair" is never re-shown, and it never replaces a
  reason swallowed earlier in the same press (a cost mod refusing inside `CanRepair` makes vanilla print it last).
- Effects: during the extra presses the station's `m_repairItemDoneEffects` is swapped for an empty `EffectList` and
  restored in `finally`, same frame. Result: one repair sound per click. No transpiler, no global `EffectList.Create`
  patch.

### 3.4 Patches

All bodies catch their own exceptions (`PatchGuard`). Applied only while the feature is Active (framework), so
turning the mod off removes them entirely and the button is vanilla again.

| Target | Type | What it does |
|---|---|---|
| `InventoryGui.OnRepairPressed()` | Prefix (priority First) | Snapshot worn items and durability (`BulkRepair.Take`). |
| `InventoryGui.OnRepairPressed()` | Postfix | Extra vanilla presses, UI refresh, summary (`BulkRepair.Finish`). |
| `MessageHud.ShowMessage(...)` | Prefix | While `BulkRepair.Muting`: swallow Center messages, keep the last one. One static bool check otherwise. |

Static state (`Muting`, `LastMuted`, the swapped effect list) lives only inside the postfix and is reset in `finally`,
so a toggle or an exception can never leave messages muted or a station silent.

### 3.5 Configuration

Only `General.Enabled` (and the read-only `Status`) from the framework. No other setting: the behaviour follows the
vanilla rules and needs no tuning.

### 3.6 Multiplayer and client-side

Client-side, compatible. Nothing is stored on items, nothing is sent over the network by the mod itself. Other
players see nothing different (at most one repair effect per click instead of one per item, see 1.7). Items
repaired by the mod are ordinary items for everyone (hand-off test M02).

---

## 4. Edge cases

| Scenario | Vanilla | Mod |
|---|---|---|
| 5 damaged items this station can repair | 5 clicks | 1 click, 1 sound, 1 message listing 3 names + "+2" |
| Damaged items for another station mixed in | Stay damaged | Stay damaged |
| Station level too low for an item's recipe | Stays damaged | Stays damaged |
| Only 1 repairable item | "Repaired X" | Identical (the mod stays out) |
| Nothing repairable | Button greyed out | Same |
| Station without roof or fire | Cannot repair | Same (first press repairs nothing → stop) |
| nocost, no station | Button in the inventory, 1 item per click, no sound | All items in 1 click, no sound |
| nocost at a station | Any repairable item, even from another station | All of them in 1 click (vanilla rule) |
| Item from a lower world level | Repairable at any station of high enough level | Same |
| Repair-cost mod, player can pay for 2 of 5 | 2 clicks succeed, 3rd refused | 2 repaired and paid in 1 click, stop, reason TopLeft |
| Mod that repairs only part of an item per press | Several clicks per item | Continues while each press makes progress, up to 10 presses per worn item |
| Mod that skips `RepairOneItem` (RhythmicRepairs) | That mod's behaviour | Same: our first extra press makes no progress → stop |
| Mod that replaces the press (FastRepairButton) | That mod's behaviour | Same: nothing left, no extra press |
| Loaded crossbow (Crossbow Stays Loaded) | Stays loaded | Stays loaded (its `RepairOneItem` patches run per item) |
| Items in an open chest | Not repaired | Not repaired |
| Feature toggled off | — | Vanilla one item per click, immediately |

---

## 5. Decisions and open questions

1. **nocost follows vanilla** (implementer's decision 2026-09-28, to confirm with the user): under nocost, vanilla
   `CanRepair` accepts every repairable item at any station, so one click repairs everything, even forge gear at a
   workbench. Requirement 3 is therefore met as "what vanilla allows at this station". The alternative (keep station
   rules under nocost when a station is open) would need our own copy of the station check and would break mods that
   change `CanRepair`.
2. **No extra settings.** Candidate options that were left out: Shift+click for the vanilla single repair, an
   item exclusion list (modded items that use durability as charges; vanilla repairs them per click anyway), paced
   per-item sounds. Add them only if players ask.
3. **Summary text** reuses the vanilla `$msg_repaired` key with item names instead of a new "N items" key, so it is
   translated without shipping a translation table.

## 6. Tests

The in-game checklist lives next to the code: `src/Crafting/Repair.OneClickAll/TESTING.md`
(run `./tools/Get-TestTodo.ps1 -Mod Repair`).
