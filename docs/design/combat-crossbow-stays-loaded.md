# Crossbow Stays Loaded — design

| | |
|---|---|
| Mod | Crossbow Stays Loaded |
| GUID / project | `MC.Combat.Crossbow.StaysLoaded` (`src/Combat/Crossbow.StaysLoaded/`) |
| Category / scope | Combat / QoL |
| Side | Client (only the player who wants it installs it) |
| Game version checked | Valheim 1.0.16 (network version 40), decompiled `assembly_valheim` in `.ref/` |
| Status | Implemented (v0.2 code), in-game testing |

## Goal

When the player puts away or swaps out a **loaded** reload weapon (any item whose primary `Attack.m_requiresReload`
is true: crossbows, and probably Dundr and the grappling hook), the weapon remembers that it is loaded. When it is
equipped again it is ready to fire immediately, with no reload. Firing consumes the loaded state exactly like
vanilla, and a new reload works exactly like vanilla (same time, stamina, eitr, animation and sounds).

Non-goals: faster reloads, reloading while the weapon is not in hand, more than one "stored" shot per weapon,
changes to ammo, damage or skills.

The user's first idea was "a boolean flag on the item, set when the reload finishes". That is the core of the
design. Two additions are needed: a per-player **token** so that you cannot pre-load a stack of crossbows for a
free burst (section 3.8), and a **guard** that tells "put away" apart from "fired" (section 3.4).

---

## 1. Vanilla behaviour (code trace)

### 1.1 State model

- `Player.m_weaponLoaded` (private `ItemDrop.ItemData`) is a **reference to the item that is loaded**, not a
  bool. `null` means "not loaded". There is no per-item loaded state anywhere in vanilla.
- `Player.SetWeaponLoaded(ItemData weapon)` (private). If the value changes, it stores the reference and writes
  `ZDOVars.s_weaponLoaded` ("WeaponLoaded") = `weapon != null` on the player's ZDO. This bool is how other clients
  see it.
- `Player.IsWeaponLoaded()` (override of the virtual `Humanoid.IsWeaponLoaded`, which returns `false`). The owner
  returns `m_weaponLoaded != null`. Other clients return the ZDO bool. It returns `false` if the net view is invalid.
- `WeaponLoadState` (MonoBehaviour on the weapon visual, with `m_loaded` / `m_unloaded` child objects). Every
  `Update` it asks the parent `Player.IsWeaponLoaded()` and switches the loaded or unloaded model. There is **no
  animator parameter** for "loaded". The only animator use is the reload animation bool (`m_reloadAnimation`,
  e.g. `reload_crossbow`) and the `<m_reloadAnimation>_done` trigger.
- `Player.UpdateControllerTriggerFeedback` reads `IsWeaponLoaded()` and `IsReloadActionQueued()` for gamepad
  adaptive triggers.
- `Humanoid.ResetLoadedWeapon()` is virtual and empty. `Player.ResetLoadedWeapon()` calls `SetWeaponLoaded(null)`
  and removes the first queued `Reload` minor action.

Invariant in vanilla: `m_weaponLoaded` is either `null` or the current weapon. The mod keeps this invariant.

### 1.2 Reload lifecycle

1. `Player.FixedUpdate` runs `UpdateActionQueue(dt)` and then `PlayerAttackInput(dt)`. It does this only for the
   owner, when `m_localPlayer == this`, and when the player is not dead.
2. `Player.PlayerAttackInput` returns early in place mode. Otherwise it calls
   `UpdateWeaponLoading(GetCurrentWeapon(), dt)` **every fixed tick**, before any attack starts.
3. `Player.UpdateWeaponLoading(ItemData weapon, float dt)` (private):
   - If the weapon is null or its primary attack has no `m_requiresReload`, it calls `SetWeaponLoaded(null)`
     (every tick while you hold anything else).
   - If `m_weaponLoaded != weapon`, no reload is queued, and `TryUseEitr(m_reloadEitrDrain)` passes, it calls
     `QueueReloadAction()`. `TryUseEitr` only checks eitr; it does not spend it.
4. `Player.QueueReloadAction()` (private) does nothing if a reload is already queued, if `m_grappling > 0`, or if
   `m_blockReload > 0`. Otherwise it queues a `MinorActionData` of type `Reload` for the current weapon with:
   - duration `ItemData.GetWeaponLoadingTime()` = `lerp(m_reloadTime, m_reloadTime * 0.5, skillFactor)`
     (the skill halves the reload time at 100),
   - progress text `$hud_reloading <name>`,
   - animation `m_reloadAnimation`, done trigger `m_reloadAnimation + "_done"`,
   - `m_staminaDrain = m_reloadStaminaDrain`, `m_eitrDrain = m_reloadEitrDrain`.
5. `Player.UpdateActionQueue(dt)` (private) does not advance while `m_actionQueuePause > 0` or `InAttack()`.
   Otherwise it plays the action's animation bool and drains stamina and eitr **per second** (`drain * dt`).
   When `m_time > m_duration`:
   - it removes the action and fires the done trigger,
   - `Equip` calls `EquipItem`, `Unequip` calls `UnequipItem`, and **`Reload` calls `SetWeaponLoaded(item)`**,
   - it sets `m_actionQueuePause = 0.3` s.

   This is the **only** vanilla call of `SetWeaponLoaded` with a non-null value.
6. Things that cancel an unfinished reload (the stamina and eitr already drained are lost, and the reload restarts
   from 0 later):
   - `ClearActionQueue()` from `Humanoid.StartAttack` (on success), `Player.CheckRun` (every tick while
     sprinting), `Player.OnJump`, and `Player.UpdateDodge`.
   - `CancelReloadAction()` from `Player.QueueEquipAction` / `QueueUnequipAction`.
   - `ResetLoadedWeapon()` (see 1.4).

   Blocking does not cancel a reload: `Humanoid.IsBlocking` returns false while `InMinorAction()`, so the
   reload wins over the block.
7. Things that block starting a reload:
   - `Player.m_blockReload` (public float). `Attack.ProjectileAttackTriggered` / `FireProjectileBurst` set it to
     `Attack.m_blockReloadTime`, and `Player.Update` counts it down every frame.
   - `Player.m_grappling` (public float), set by `GrapplingPoint`.
8. No skill XP is given for reloading. Crossbows skill XP comes from projectile hits (`Projectile` →
   `m_owner.RaiseSkill`).
9. "Loaded" is **not** a bolt. Reloading does not check or use ammo. Ammo is used when the shot fires
   (`Attack.OnAttackTrigger` → `UseAmmo`).

### 1.3 Fire path

- `Attack.Start(...)`: for an attack with `m_requiresReload`, it returns `false` if `!IsWeaponLoaded()` or
  `InMinorAction()`. It also needs ammo (`HaveAmmo`). `Humanoid.StartAttack` also refuses while
  `InMinorAction()`, and it clears the action queue when the attack starts.
- `Attack.OnAttackTrigger()`:
  - It returns early, **without** unloading, if `UseAmmo` fails or the character is staggering. The weapon stays
    loaded in vanilla.
  - Otherwise it fires. Durability drops in `ProjectileAttackTriggered`. At the end, if `m_requiresReload`, it
    calls `m_character.ResetLoadedWeapon()`. This is the "consume" event.
- On the next tick, `UpdateWeaponLoading` sees an unloaded reload weapon and queues a reload. The reload only
  advances after the attack animation ends (`InAttack()` blocks the queue).

### 1.4 Why vanilla forgets the loaded state

- `Humanoid.UnequipItem(ItemData item, bool triggerEquipEffects = true)`: for **any weapon** that is equipped
  (`IsWeapon()`), it stops the current attack of that weapon, clears the draw state, and calls
  `ResetLoadedWeapon()`. This clears `m_weaponLoaded`, sets the ZDO bool to false, and removes a queued reload.
- `ResetLoadedWeapon` has exactly **two** callers: `Attack.OnAttackTrigger` (fired) and `Humanoid.UnequipItem`
  (put away).
- Even without that call, `UpdateWeaponLoading` would set `SetWeaponLoaded(null)` on the next tick when you hold a
  different weapon. The state is player-level, so it cannot survive a weapon change. **This is why the mod needs
  per-item state.**

### 1.5 Every path that changes or removes the equipped weapon

All of them go through `Humanoid.EquipItem` / `Humanoid.UnequipItem`. `Player` does not override either.

| Path | Code | Loaded in vanilla after it? |
|---|---|---|
| Hotbar, inventory right-click, gamepad radial | `Humanoid.UseItem` → `Player.ToggleEquipped`. If `m_equipDuration > 0`: `QueueEquipAction` / `QueueUnequipAction` (these call `CancelReloadAction`), then `UpdateActionQueue` → `EquipItem` / `UnequipItem`. If the duration is 0 it is direct. | No |
| Equipping another hand item | `EquipItem` of a `Bow` / two-hander / tool / shield / one-hander unequips the conflicting hand items → `UnequipItem` | No |
| Hide-weapons key | `Player.Update` → `HideHandItems()` / `ShowHandItems()` (not during attack or dodge) | No |
| Swimming (not touching ground) | `Humanoid.UpdateEquipment` → `HideHandItems()`. Items stay hidden until you press the hide key or equip them again. | No |
| Crafting station | `Player.SetCraftingStation` → `HideHandItems()` | No |
| Bed / sleeping | `Bed` → `Player.AttachStart(hideWeapons: true)` → `HideHandItems()` | No |
| Chair, ship helm, Lox saddle, barber seat | `AttachStart(hideWeapons: false)`: nothing is unequipped | Yes (unchanged) |
| Barber / customization UI | `PlayerCustomizaton.ShowBarberGui` → `HideHandItems()` | No |
| Eating from a feast / food piece | `Humanoid.SetUseHandVisual` → `HideHandItems(onlyRightHand: true)`. A crossbow is a left-hand `Bow`, so it is not touched. | Yes (unchanged) |
| Emotes, ladders | No unequip (`Emote`, `Ladder` never call it) | Yes (unchanged) |
| Build hammer radial | Equipping the hammer unequips the hands. `HammerItemElement` re-equips the last items. | No |
| Drag within the inventory | `InventoryGui.OnSelectedItem` unequips both items. `InventoryGrid.DropItem` → `Inventory.MoveItemToThis(from, item, amount, x, y)` → `Inventory.AddItem(item, amount, x, y)` **clones** the item into the target slot. The clone is re-equipped. | No |
| Move to container | `OnSelectedItem` (Move modifier) → `UnequipItem`, then `Inventory.MoveItemToThis(from, item)` moves the **same** object | No |
| Drop on ground | `Humanoid.DropItem` → `UnequipItem`, then `ItemDrop.DropItem` → `item.Clone()` → `SaveToZDO` | No |
| Item stand / armor stand | `ItemStand` / `ArmorStand` → `UnequipItem`, `Clone()`, `ItemDrop.SaveToZDO` | No |
| Upgrade at a crafting station (also the upgrader station) | `InventoryGui.DoCrafting` → `UnequipItem` + `RemoveItem`, then `Inventory.AddItem(name, …)` creates a **new ItemData** from the prefab (fresh, empty `m_customData`) | No |
| Repair | `InventoryGui` sets `m_durability` in place (same object) | Not equipped at a station anyway |
| Durability reaches 0 | `Humanoid.UpdateEquipment` → `DrainEquipedItemDurability` → `UnequipItem` (and destroy if `m_destroyBroken`). `EquipItem` refuses broken items. | No |
| Consumable weapon | `Attack.ConsumeItem` → `UnequipItem` (not used by crossbows) | n/a |
| Death | `Player.OnDeath` → `CreateTombStone` → `UnequipAllItems` (depends on world modifiers) → `MoveInventoryToGrave` (same objects). The new `Player` starts with `m_weaponLoaded = null`. | No |
| Logout / login | `Player.Save` writes the inventory and `Player.m_customData`. `Player.Load` → `UnequipAllItems` → `m_inventory.Load` (**new ItemData objects**) → `EquipInventoryItems` → `EquipItem`. | No |
| Teleport (portal) | Nothing is unequipped. The player object and `m_weaponLoaded` survive. | Yes (unchanged) |
| Attack aborted or staggered before the bolt leaves | `Attack.Stop` / `Abort` never reach `ResetLoadedWeapon` | Yes (unchanged) |

### 1.6 Per-item data (`ItemDrop.ItemData.m_customData`)

- The type is `Dictionary<string, string>`.
- `ItemData.Clone()` copies it (a new dictionary).
- `ItemData.Save` / `ItemData.Load` write it and read it back: flag bit `0x80`, the count, then the key/value
  pairs. This covers the player inventory (`Inventory.Save` / `Load`, format 109), containers, tombstones and
  dropped items (`ItemDrop.SaveToZDO` / `LoadFromZDO`), item and armor stands, and legacy formats
  (`Inventory.LoadOld` from `Version.Item.CustomData`).
- Vanilla keeps unknown keys and never reads them.
- `Player.m_customData` (also `Dictionary<string, string>`) is saved in `Player.Save` and restored in
  `Player.Load`.
- `Game._RequestRespawn` saves the **dead** player's data before destroying it, so writes made at death persist.

### 1.7 Multiplayer and dedicated server

- The owner (the local client) runs everything in section 1.2 and 1.3.
- Remote clients only read the ZDO bool through `IsWeaponLoaded()` for `WeaponLoadState` visuals.
- No RPC is involved. The dedicated server runs no `Player` logic for clients.
- Nothing about the loaded state is validated by the server.

---

## 2. Which weapons use `m_requiresReload`

This is prefab data and cannot be read from code. Evidence from the asset manifest
(`valheim_Data/StreamingAssets/SoftRef/manifest_extended`) and from the code:

| Item (prefab) | Confidence | Evidence |
|---|---|---|
| `CrossbowArbalest`, `CrossbowRipper`, `CrossbowRipperBlood`, `CrossbowRipperLightning`, `CrossbowRipperNature`, `CrossbowGold`, `CrossbowGold_BloodLightning`, `CrossbowGold_FrostFire` | High | Crossbow weapon prefabs, `reload_crossbow` animator parameter, Arbalest reload SFX. `Player.UpdateControllerTriggerFeedback` treats a left-hand `Bow` with skill `Crossbows` as a reload weapon. |
| `CrossbowGoldUncooked` | Unknown, probably not a usable weapon | "Uncooked" prefabs look like crafting intermediates (there is a `MoldCrossbow` item) |
| `StaffLightning` (Dundr) | Likely | Reload SFX `Player_Weapons_LightningStaff_Reload_*`. `UpdateControllerTriggerFeedback` special-cases a **right-hand** item with `m_reloadEitrDrain > 0`, which only fits an eitr-reloaded staff. |
| `GrapplingHook` | Likely | `sfx_grapplinghook_reload`. `QueueReloadAction` refuses while `m_grappling > 0`. `Attack.m_blockReloadTime` exists. |
| `DvergerArbalest` (NPC gear) | No | `Humanoid.IsWeaponLoaded()` always returns `false` for non-players, so an NPC weapon with `m_requiresReload` could never fire |

The mod stays **generic**: it keys on `m_shared.m_attack.m_requiresReload`, never on prefab names. The debug option
`Debug.LogReloadWeapons` (section 3.5) dumps the real list and values at first spawn. Paste it into this section
once measured.

---


## 3. Implemented design (v0.2)

### 3.1 Core idea

The user's idea, kept simple: a **"loaded" stamp stored on the item** (`ItemData.m_customData`), set when a real
reload completes. `m_customData` already travels with the item everywhere (inventory save, chests, drops, stands,
tombstones, `Clone`), so no extra persistence code is needed.

To make the stamp trustworthy without any server or player bookkeeping, it records the item's **durability at the
time it was stamped**. Every shot costs durability (`Attack.ProjectileAttackTriggered`), even when fired by a player
who does not have the mod. So "durability unchanged since the stamp" means "nobody fired it since". This single rule
handles vanilla friends, uninstall/reinstall, the mod being toggled off, and malformed values, with no player ids,
timestamps or counters.

### 3.2 Data

- Key: `MC.Combat.Crossbow.StaysLoaded.Loaded` (namespaced by GUID).
- Value: `v1:<durability>` (invariant culture, round-trip format). Anything else = not loaded.
- Valid when `|stamped − current durability| < 0.0001`.
- Eligible items: primary attack has `m_requiresReload`, not `m_consumeItem`, and the item is allowed by the `Weapons` config (`LoadedState.IsEligible` + `WeaponFilter`).

### 3.3 Patches (`Patches/CrossbowLoadPatches.cs`)

All bodies catch their own exceptions (`PatchGuard`) and never skip the original method. They are applied only while
the feature is Active (framework), so turning the mod off removes them entirely.

| Target | Type | What it does |
|---|---|---|
| `Player.SetWeaponLoaded(ItemData)` | Postfix | A real reload completed (the only vanilla non-null caller is `UpdateActionQueue`): stamp the item. Local player only. |
| `Player.UpdateWeaponLoading(ItemData, float)` | Prefix | Every fixed tick, where vanilla decides to reload. If the held weapon is stamped and the stamp is valid: `CancelReloadAction()` + `SetWeaponLoaded(weapon)` (sets the ZDO bool, so everyone sees the loaded model). Invalid stamp: removed, vanilla reloads. Same `m_grappling` / `m_blockReload` gate as vanilla. Cheap checks first. |
| `Humanoid.UnequipItem(ItemData, bool)` | Prefix | Putting away the weapon the local player has loaded: re-stamp with the current durability (it may have changed while held, e.g. by blocking). |
| `Attack.OnAttackTrigger()` | Postfix | The shot happened (player no longer loaded): remove the stamp from the weapon that fired. Early returns (no ammo, stagger) keep it. |
| `InventoryGui.RepairOneItem()` | Prefix + Postfix | Remember which items were loaded before the repair, re-stamp them after (repair changes durability). |
| `ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int, bool)` | Postfix | Adds an orange "Loaded" line (config `UI.ShowLoadedInTooltip`). Skips crafting and appended tooltips. |
| `Player.OnSpawned` (Debug builds only) | Postfix | Logs every reload weapon with equip/reload timings (`[reload weapon]` lines) to answer the open questions below. |

The stamp is written at reload completion and refreshed at put-away and repair. It is never *created* by a put-away
of an unloaded weapon (the prefix requires `m_weaponLoaded == item`).

### 3.4 Configuration

| Section | Key | Default | Meaning |
|---|---|---|---|
| General | Enabled | true | Framework toggle (live). |
| General | Status | — | Framework status line (read-only). |
| Weapons | Crossbows | true | Every weapon using the Crossbows skill keeps its load (modded crossbows too). |
| Weapons | ExtraItems | GrapplingHook | More reload weapons by prefab name or `$item_` token (e.g. StaffLightning). |
| Weapons | ExcludedItems | (empty) | Never keep a load; wins over the two above. |
| UI | ShowLoadedInTooltip | true | Show "Loaded" in the tooltip of a crossbow that holds a bolt. |

### 3.5 Multiplayer and client-side

`ModSide=Client`, `ModMultiplayer=Compatible`. Nothing is sent over the network except the vanilla `WeaponLoaded`
ZDO bool (set through vanilla `SetWeaponLoaded`), so vanilla players and servers see normal visuals.

| Hand-off | Result |
|---|---|
| Loaded crossbow given to a friend **with** the mod | Loaded for them (the bolt is in the crossbow). |
| Given to a friend **without** the mod | They reload as usual. The stamp stays on the item, harmless. |
| Vanilla friend fires it and gives it back | Durability changed → stamp invalid → you reload. |
| Vanilla friend reloads it but does not fire, gives it back | Your old stamp is still valid → loaded (consistent: it holds a bolt). |
| Mod uninstalled, crossbow fired, mod reinstalled | Durability changed → stamp invalid. |

Known limitation: in a world where durability does not drop (a durability rate of 0), a shot fired by someone
without the mod, or while the mod is off, cannot be detected. Our own shots always clear the stamp.

### 3.6 Swap-burst analysis (open question)

With the stamp per item, a player can pre-load several crossbows and fire them in quick succession by swapping
(each swap costs the equip time `E` instead of the reload time `R`). The gain is a one-time opener of
`(N−1)·(R−E)` that must be prepared out of combat; the sustained fire rate never beats vanilla, and each extra
crossbow costs weight and a slot. Measured: `E` = 0.2 s, `R` = 3.5 s (1.75 s at skill 100), so each extra pre-loaded crossbow saves about 3.3 s (1.55 s at skill 100). Decision: allowed anyway (user, 2026-09-28).
The Debug-build `[reload weapon]` log gives the real `E` and `R`. If the burst is too strong, a
`Balance.OnlyLastReloaded` option can keep only the most recently reloaded crossbow loaded.

---

## 4. Edge cases

"Mod" = v0.2 defaults.

| Scenario | Vanilla | Mod |
|---|---|---|
| Loaded → swap to sword → back | Reload | Loaded after the equip time |
| Two crossbows, A loaded, swap to B (B reloads), back to A | A reloads | A loaded |
| Swap away before a reload finishes | Reload restarts | Same (no stamp until it completes) |
| Fire, swap away and back | Reload | Reload (stamp cleared at the shot) |
| No ammo / staggered before the bolt leaves | Stays loaded | Same |
| Hide key, swimming, crafting station, bed, barber | Reload | Loaded |
| Chair, ship helm, saddle, emote, ladder | Stays loaded | Same |
| Drag to another slot (clone) | Reload | Loaded (clone keeps the stamp) |
| Chest, ground, item stand, armor stand, tombstone | Reload | Loaded |
| Logout / login | Reload | Loaded |
| Death, recover tombstone | Reload | Loaded (UnequipAllItems re-stamps first) |
| Repair | n/a | Stays loaded (re-stamped) |
| Block while loaded, then swap | Reload | Loaded (re-stamped at put-away) |
| Upgrade at a station | New item | Unloaded (new ItemData, open question) |
| Durability reaches 0 on the last shot | Unloads | Same |
| Grappling hook / `m_blockReload` active | Reload waits | Restore waits for the same gate |
| Feature toggled off mid-session | n/a | Vanilla; stamps kept but invalid once fired |
| Remote players watching | Unloaded model, reload | Loaded model immediately (ZDO bool) |
| Dedicated server | n/a | Not loaded (`BepInProcess("valheim.exe")`) |
| Malformed or foreign stamp value | n/a | Treated as not loaded and removed |

---

## 5. Decisions and open questions

Decided by the user (2026-09-28):

1. **Several pre-loaded crossbows: allowed.** Every crossbow keeps its own load; no "only the last one" limit.
2. **Upgrade: unloads** (the game creates a new item). Kept as is.
3. **Death: the load is kept** in the tombstone.

4. **Which weapons: crossbows + grappling hook by default, configurable.** Dundr (StaffLightning) back to vanilla by
   default (its reload costs 25 eitr). `Weapons.Crossbows` (every weapon using the Crossbows skill, so modded
   crossbows are included), `Weapons.ExtraItems` (default `GrapplingHook`; add `StaffLightning` or other mods' reload
   weapons by prefab name) and `Weapons.ExcludedItems` (wins). Implemented in `WeaponFilter.cs`, read live.

## 6. Tests

The in-game checklist lives next to the code: `src/Combat/Crossbow.StaysLoaded/TESTING.md`
(run `./tools/Get-TestTodo.ps1`).
