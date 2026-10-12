# Cheated Tag Remover — design

| | |
|---|---|
| Mod | Cheated Tag Remover |
| GUID / project | `MC.UX.CheatedTag.Remover` (`src/UX/CheatedTag.Remover/`, root namespace `MC.UX.CheatedTagRemoverMod`, package `CheatedTagRemover`) |
| Category / scope | UX / QoL |
| Side | **Both** (loads on the dedicated server and on the game), multiplayer **Compatible**, network version 1. No player check: nobody is refused. The server needs it for the whole world, the admin's game for the command; other players are optional (section 4). |
| Sheet idea | None: asked for directly by the user on 2026-10-12 (no row in the idea sheet, no `ModIdea`). |
| Game version checked | Valheim 1.0.17, decompiled `assembly_valheim` and `assembly_utils` in `.ref/` (ItemDrop, Inventory, Container, ItemStand, ArmorStand, CookingStation, Smelter, Fermenter, Character, Piece, ZDO, ZDOMan, ZDOExtraData, ZDODataHelper, BinarySearchDictionary, ZPackage, ZNet, ZRpc, ZRoutedRpc, Terminal, Achievements, PlayerProfile); the 1.0.17 dedicated server's own `assembly_valheim.dll` decompiled and compared on 2026-10-12 (item format, ZDO keys and station code are the same text) |
| Status | Implemented (v0.1.0 code); smoke test passed 2026-10-12; in-world self-tests passed 2026-10-12 on the final code (4 `uncheat.*` tests, 81 checks); multiplayer self-tests with a real dedicated server passed 2026-10-12 on the final code (`uncheat.mp.sweep`, `uncheat.mp.contested`, `uncheat.mp.local`, 49 checks); one adversarial review round (4 reviewers, every finding checked by a skeptic; every confirmed finding fixed in code, tests or text, one judged plausible only and documented, see section 8) and a second check of the fix code (2 findings, fixed; all tests re-run after it); not yet tested by hand in game |

## Goal

The user's request (2026-10-12): "a mod to remove the 'cheat' tag that was assigned to many objects on my Valheim
server. Ideally client side but I can install it on the server side as well. Expected behaviour is that running a
command that the mod enables removes the tag from every object and entity in the world (including builds and in
storage items)." Numbered; each one is scope and has at least one test (section 8).

1. **G1 — A command.** The mod adds a console command; nothing happens until it is typed.
2. **G2 — Every object and entity.** The command removes the tag from every object of the world: building pieces,
   creatures, stations, and everything else that carries it, loaded or not.
3. **G3 — Stored items.** Items inside containers (chests, carts, ship holds, tombstones) lose the tag, and so do items
   on the ground and on item and armor stands.
4. **G4 — Client side where the game allows it.** The command is typed on a player's game. What only that game can do
   (the items its player carries) is done there, on any server and for any player; a game alone on a server without
   the mod does what a single game can.
5. **G5 — Server install for the whole world.** With the mod on the server too, the command cleans the whole world of
   that server.

### Added beyond the request (small)

- **E1 — `cheated_scan`.** The same pass without writing: a count per kind. It answers "what does my world hold" before
  anything is changed, and it is the first half of the clear anyway (the report is "before minus after").
- **E2 — Carried items.** The request names objects, entities, builds and stored items. Items carried by players are
  cleaned too (by each player's own game): one tagged stack in a pocket tags the world again.
- **E3 — Check, report, save.** The command counts again after writing, repeats what came back, says what is left and
  why, and saves with the calls of the game's own autosave.

### Non-goals

- **The character's "used dev commands" mark** (`PlayerProfile.m_usedCheats`, the "Cheater!" line of the stats) and the
  Cheats counter. It is not a tag on an object, it lives in each player's file, and it is the one permanent rule of the
  vanilla achievement system. Offered to the user as a possible opt-in.
- **World modifiers** that make a world count as cheated (`Achievements.IsWorldCheated`): they are world rules, and
  vanilla has admin commands for them (`listkeys`, `removekey`, `resetworldkeys`).
- **Preventing new tags.** No patch touches item or object code; the game keeps tagging as it does. Several existing
  mods do prevention (section 6.3).
- **Achievements.** The mod does not make achievements available on a modded game (`Game.isModded` counts as cheated).
- **Deleting anything.** No item, object or key is removed.

### Later (not built, one-line sketches)

- A setting that runs the clean-up once when the dedicated server has loaded the world (no player online, no client mod
  needed), then turns itself off.
- An opt-in argument that also clears the character mark of the player who types it.
- Per-player answer counts (how many carried items each player's game cleaned) in the admin's report.

## 1. Vanilla behaviour

All from `.ref` 1.0.17. The dedicated server runs its own `assembly_valheim.dll`; its text was compared for the classes
below and is the same.

### 1.1 Where the tag lives

| # | Store | Where | Format |
|---|---|---|---|
| A | Object tag | ZDO int `ZDOVars.s_cheated` ("cheated"), any object | 0 / 1 (`ZDO.Set(int, bool)` stores an int) |
| B | Queue tag | ZDO int `ZDOVars.s_cheatedQueued` ("cheatedQueued") on `Smelter` and `Fermenter` objects; `CookingStation` keeps one per slot at the key **`s_cheatedQueued + slot`** (a number added to the hash, `CookingStation.SetSlot` / `GetSlot`) | 0 / 1 |
| C | Stored items | ZDO byte array `ZDOVars.s_items` ("items"), written by `Container.Save` through `Inventory.Save`: every user of `Container` (chest, `TombStone`, `Vagon`, ship hold, `Incinerator`, loot crates) | int32 layout version, uint16 count, item records |
| D | Single items | ZDO byte array `ZDOVars.s_itemData` ("itemData") on ground items (`ItemDrop.SaveToZDO`; fish and placed feasts are ground items too) and on `ItemStand`; `"<slot>_itemData"` per `ArmorStand` slot | one byte layout version, one item record |
| E | Carried items | The player's `Inventory`, saved by `Player.Save` into the character file on that player's PC | same record |
| F | Character mark | `PlayerProfile.m_usedCheats`, character file | bool (non-goal) |

An item record (`ItemDrop.ItemData.Save`, layout `Version.Item.ChunksNCheats` = 109) is: durability, grid position,
world level, one byte of field flags, then the optional fields those flags announce (quality, stack, variant, crafter id
and name, prefab hash, custom data pairs), and always one last **flags byte whose bit 0 is the tag**. Layout 108
(`Smaller`) is the same without that byte; layout 107 (`AbandonedDN`) has a tag but, for containers, another shape
(`Inventory.LoadOld`). The game only writes 109.

Nothing else carries a tag: no other ZDO key, no string, no global key (a search for "cheat" over every decompiled
assembly only hits `assembly_valheim`, in the classes named in this section). Creature inventories, saddles, turrets
and catapults do not save items.

### 1.2 How it spreads

- Object tag to items: `Piece.DropResources` (refund of a tagged piece), `DropOnDestroyed`, `Character.OnDeath` into
  `CharacterDrop` and `Ragdoll`, `InventoryGui.DoCrafting` (**the crafting station's tag marks every item crafted at
  it**), `Smelter.Spawn`, `Fermenter.DelayedTap`, `CookingStation` drops.
- Items to object tags: `Player.PlacePiece` (a tagged stack of any required resource in the inventory), a tagged
  weapon equipped in `Character.ApplyDamage` and `Destructible.Destroy`, a tagged item fed to a station
  (`RPC_AddOre`, `RPC_AddItem` carry the item's tag).
- Items to items: `Inventory.AddItem(ItemData)` marks the stack a tagged item is merged into; crafting with a tagged
  ingredient; the trader (`StoreGui`).
- A rule a cleaner cannot beat: `Inventory.AddItem(int prefabHash, ...)`, the per-item step of every `Inventory.Load`,
  tags any item whose total damage is above 10 000 again.
- Console: `spawn` tags the spawned object and item, `location` tags every object of the location, no-cost building
  and crafting tag what they make, god mode, ghost mode and debug fly tag every creature the player damages.

### 1.3 What a player sees

Only items show it: `ItemDrop.ItemData.GetTooltip` adds the grey line `$achievements_cheated_item_inventory`
("This item was summoned through cheating means.", read in the installed game's localization table) unless the viewer
has the bypass key. Pieces, creatures and stations show nothing; their tag only acts through what they produce.
`Inventory.Changed` shows `$achievements_picked_up_cheated_item` / `$achievements_dropped_cheated_item` centre
messages, but only to a player who is not "cheated at all" (`Achievements.IsCheatedAtAll`), and a game with the BepInEx
pack always is (`Game.isModded`).

The vanilla command `yesiuseddevcommandsbutiwantmyachievementsanyway` sets the player key `bypasscheatchecks`
(`PlayerProfile.s_bypassCheatChecks`): that character's game ignores tags and hides the line. It removes nothing.

### 1.4 Saved object data: the rules that matter

- **`ZDO.Set` has no owner check** and, when the value really changes, raises the data revision, queues the object for
  sending on a client and marks its save chunk dirty (`ZDO.IncreaseDataRevision`, `ZDOMan.SetDirtySector`).
  **`ZDO.Remove*` does none of that**, and a receiving game only adds or overwrites the keys in a packet
  (`ZDO.Deserialize`, `ZDODataHelper.ReadData`): a removed key is not saved, not sent, and comes back from any other
  copy. Tags are cleared by writing 0, like vanilla does (`Fermenter.RPC_Tap`).
- `Set` on an object that does not have the key **adds** it. Writing blindly would add a key to every object, dirty
  every chunk and resend the world.
- `ZDO.GetByteArray` returns the stored array itself, and `BinarySearchDictionary.SetValue` compares with `Equals`:
  setting the same array again does nothing, and writing into it changes data behind the engine's back (no revision,
  not saved, not sent).
- **World saves only rewrite dirty chunks** (`ZDOMan.AddObjectsPerChunk`). **Portals** are saved in their own chunk
  with their own flag, which `Set` does not raise: `ZDOMan.SetDirtyPortals` is needed after a portal changes.
- **Data that comes in from another game marks nothing for saving.** `ZDOMan.RPC_ZDOData` takes the data into the
  tables (`ZDO.Deserialize`) without `SetDirtySector` or the portal flag; the only callers of the marks are local
  writes (`ZDO.IncreaseDataRevision` / `IncreaseOwnerRevision`), sector moves and new or destroyed portals. So what a
  player's game changes is written to disk only when something else marks that chunk before the next save (an
  ownership hand-over when a player arrives, a destroyed object, a creature crossing a zone border), and a portal
  only after a portal is built, destroyed or re-paired. In vanilla play such marks happen all the time around players;
  for a clean-up they must not be left to chance.
- **Conflict rule** (`ZDOMan.RPC_ZDOData`, same on both sides, no sender check): incoming data wins only with a higher
  data revision. A server write holds on objects nobody owns and on objects whose connected owner is idle; an owner
  that keeps writing (a creature it moves, a running smelter or cooking station) sends its old copy back.
- A save copies the tables on the main thread and writes them on another (`ZNet.SaveWorld`, `ZDOMan.PrepareSave`); the
  copy shares the inner arrays, so the pass does not write while `ZNet.IsSaving()`.
- Revisions are not saved: after a restart every object starts at revision 0 with no owner.

### 1.5 Who holds what

- The server process (dedicated server, the hosting player, single player) loads every chunk into
  `ZDOMan.m_objectsByID`: the whole world. A client only holds the objects the server sent for its surroundings
  (`ZDOMan.CreateSyncList`), plus old copies of places it left.
- A dedicated server builds no game object at all (its reference position is pinned outside the world): no
  `Container`, no `ItemDrop`, no player. There the saved data is the only copy.
- A game with objects keeps copies in memory: `Container` holds its `Inventory` and reloads it once a second when the
  data revision moved **and the chest is not open on it** (`Container.Load`); its owner writes the memory copy back on
  every item move (`Container.Save`). `ItemDrop` reloads only on hover and pickup and writes back on stack changes.
- `Container.SetInUse` only works on the owner. Taking ownership of a chest another player has open leaves it stuck
  "in use" there, and that game later writes its old items over everything: **a cleaner must never take ownership of a
  container**. The ZDO int `InUse` is written by the owner only and is not taken back when the owner's game dies with
  the chest open. With nobody near, the dead player stays the owner and the server can tell (no connected owner). When
  another player's game inherits the chest (`ZDOMan.ReleaseNearbyZDOS`), `InUse` stays 1 with a connected owner until
  that game sees a data change on the chest: from saved data alone this cannot be told from a real open window.
- Carried items exist only in the carrying player's game and character file.

### 1.6 Console and admin

`Terminal.ConsoleCommand` registers itself in the static `Terminal.commands` when constructed. A cheat command needs
`devcommands` and marks the character when it runs (`ConsoleCommand.RunAction`); a normal one runs anywhere, also from
chat as `/name`. An unknown command is never sent to the server. Vanilla's remote command (`ZNet.RemoteCommand`) runs
the text in the server's console after an admin check but has no way back to the caller. Admin identity is the
connection's own host name against `adminlist.txt` (`ZNet.IsAdmin`), the test `kick`, `ban` and `save` use. A dedicated
server has no console input. Unknown peer RPCs are ignored silently (`ZRpc.HandlePackage`).

`ZNet.SaveWorldAndPlayerProfiles` (the `save` command) is not safe to call on a dedicated server: within a minute of a
save (or of the server start) `ZNet.HardSaveBlock` reads `PlatformManager.DistributionPlatform`, which is null there
(seen as an exception in `uncheat.mp.sweep`). The autosave (`Game.UpdateSaving`) calls `Game.SavePlayerProfile` and
`ZNet.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: true)` without that test.

## 2. Design

### 2.1 Commands (G1, E1)

`cheated_clear` and `cheated_scan` (`ClearCommand`), not cheat commands, not secret. What they do depends on the game
they are typed on:

| Game | Path |
|---|---|
| Holds the world (single player, host) | `SweepJob` here: whole world + own items |
| Client, the server has the mod (the framework says Active) | Own carried items here first (admin or not), then `ServerLink.Ask`: the server checks the admin list and runs `SweepJob`; the answer comes back by RPC |
| Client, the server has no copy, it is off there, or it is another version (ServerMissing / ServerMismatch) | Here: own items + loaded objects (`SweepScope.Loaded`), and the answer says the rest needs the server (2.5) |
| Main menu | "No world loaded." |
| The copy failed to start or is blocked | "... is not running" + its status |

### 2.2 The world pass (`WorldSweep`, G2, G3)

One scan, then the writes, in the same frame:

1. **Scan** the two tables that can hold a tag, `ZDOExtraData.s_ints` and `s_byteArrays` (private; the game assemblies
   are publicized). The keys of one object sit sorted in two arrays (`BinarySearchDictionary`), read directly: one
   binary search per key, no allocation. Per object: `cheated` != 0; every key in `[cheatedQueued, cheatedQueued + 32)`
   with a value != 0 (above slot 0 only when the object also has the int key `"slotstatus<n>"`, which
   `CookingStation.SetSlot` writes next to it: a number key of another mod that happens to fall in the range is not
   touched); the byte arrays `items`, `itemData` and `"0_itemData"`..`"31_itemData"`.
2. **Bytes** (`ItemBlobs`): the blob is walked with the read order of `ItemData.Load`; every record must end inside the
   array and the last one exactly at its end. Only layout 109 is changed; a blob in a layout that can hold a tag but
   that the mod does not know (107, anything newer) or that does not walk to its end is counted and left alone. The
   result is a **copy** with bit 0 of each record's last byte cleared. Never `Inventory.Load` + `Save`: that drops items
   whose prefab is unknown, clamps stacks, resets variants and applies the 10 000 damage rule.
3. **Write**: `zdo.Set(key, false)` / `zdo.Set(key, copy)`, only for what the scan found, after the scan (no table is
   changed while it is walked). A portal among the changed objects: `ZDOMan.SetDirtyPortals()` once.
4. **Open chests**: a tagged container whose `InUse` is 1 with another connected player as owner is counted and not
   written (that game holds the items and would write them back; overruling it by force can lose an item move). The
   answer gives the count and the map position of the first five, and says to open and close them once: the same state
   is left behind by a player who lost the connection with the chest open (1.5).
5. **Leftover stand bytes**: a stand whose item was taken off keeps the item's bytes and writes 0 in its item key
   (`ItemStand.DropItem`, `ArmorStand.DropItem`). Such bytes are cleaned like any other, but not counted as an item
   (the item itself lies on the ground with its own bytes and is counted there).

Three scopes: `World` (all objects, where the world runs), `Owned` (loaded objects this game owns: a player's game
helping the server), `Loaded` (loaded objects around the player, owned or not: a game alone on a server without the
mod; never the old copies of places it left). No scope takes ownership of anything.

### 2.3 The memory pass (`LiveSweep`, E2)

On a game that has objects: the tag bit is cleared on the items the local player carries (`Achievements`' per-frame
answer is reset and `Inventory.Changed` called, vanilla's own bookkeeping), on the in-memory items of every loaded
`Container` and on every loaded `ItemDrop`. Nothing is saved through the game here: the saved bytes are fixed by the
world pass, and the memory copies just can no longer bring the tag back (an open chest's next item move writes clean
items).

### 2.4 One run (`SweepJob`, G5, E3)

On the game that holds the world:

1. wait while a save runs (up to 60 s);
2. count ("before"), and keep the id of every object that has a tag;
3. scan only: answer and stop;
4. ask every connected player whose game has a matching copy to clean its own side (`ServerLink` "Local": carried
   items, memory copies, and a world pass with scope `Owned`, whose writes always win; a game whose player has no
   character in the world yet cleans the carried items when it spawns), run the memory pass here (host, single
   player), and wait 2 s when somebody was asked;
5. world pass with scope `World`;
6. count again ("after"). With other players connected: 3 s first, and while something is still tagged, write and count
   again, three passes at most;
7. **mark every object of step 2 for saving** (`WorldSweep.MarkForSave`: `ZDOMan.SetDirtySector`, and
   `SetDirtyPortals` when one is a portal): what a player's game cleaned in step 4 came in unmarked (1.4), and would
   otherwise be tagged again after a restart. A save that started during the waits of step 6 must end first (up to
   60 s): marks made while a save runs go to a second list, and a save started right behind the running one still
   reads the first (`ZDOMan.DirtyChunks`, `AddObjectsPerChunk`, `UpdateSaveState`). Still saving after the wait: the
   marks are made anyway (they reach the save after that one) and the run does not start a save of its own;
8. answer with before minus after, what is left (open chests; objects another game keeps writing), unknown formats,
   players with and without the mod; then save with the calls of the game's autosave (`Game.SavePlayerProfile`,
   `ZNet.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: true)`; 1.6), which also make every
   player's game save its character.

In single player there is nothing to wait for: the whole run happens inside the command call. When the request came
from another player (an admin who is a guest of a hosted game), the host's own carried items are cleaned too, but they
are the host's business: the host gets its own line, and they are not part of that admin's answer.

A run stops without an answer when the world ends under it (`SweepJob.Here`: the `ZNet` object is stopped or the game
is shutting down; logging out keeps the stopped object for a moment with empty tables).

### 2.5 A game alone (`ClearCommand.LocalOnly`, G4)

On a server without the mod the player's game cleans its carried items, the memory copies and the loaded objects
(scope `Loaded`). What it wrote on objects it owns holds; what it wrote on objects of another player's game can be
undone by that game. So when anything of the second kind was written, the answer waits 3 s, counts again and lists
what is still tagged; otherwise it answers at once. Nothing is saved from here: the server writes these changes with
its next save of that area (1.4), and the answer says that the whole world needs the mod on the server (or, when the
server has another version of the mod, matching versions).

### 2.6 The answer (`Report`, `Output`)

Plain sentences with counts: building pieces, creatures, other objects (by the prefab's `Piece` / `Character`
component), station queues, items in containers, single items, carried items. The answer goes to the window the
command was typed in (console or chat) and to the log; from the server to the admin by RPC.

## 3. Decisions

1. **`ModSide` Both, no player check.** A Client mod is not loaded by the dedicated server (generated
   `BepInProcess`), and the whole world only exists there. "Both" normally means "required everywhere, the server
   refuses players without it" (user rule for gameplay mods); this is an admin tool that changes no rule, so nobody is
   refused. Cost: the MC Mods panel label says "server + every player need it", the server logs a warning per player
   without it, and on a server without it the status says Inactive although the own-side clean still works. The README
   says what is really needed. **To confirm with the user.**
2. **Commands follow `Enabled`, not Active.** They are created in `BindConfig` and removed when `Enabled` is turned
   off, so a client on a server without the mod (framework state ServerMissing, patches off) keeps them. The command
   body refuses in every state but Active, ServerMissing and ServerMismatch.
3. **Own peer RPCs instead of the vanilla remote command.** The vanilla one cannot answer the caller, is silent when
   the server has no copy, and cannot outlive one call; the run needs waits. Admin check by the connection's host name
   (`ZNet.IsAdmin`), as vanilla does for `save`.
4. **Bytes are patched in place; the game's own load and save are never used to rewrite data.** See 2.2 step 2.
5. **Only layout 109 is changed.** Anything else is counted as "left untouched". (For single-item blobs layout 107 has
   the same shape, but no build is known to have written one.)
6. **No ownership is ever taken, no data revision is pushed ahead.** Both tricks make the server's copy win, and both
   can undo another player's last item move. The run instead asks owners that have the mod, writes plainly, checks and
   repeats, and reports what does not hold. A plain write still wins when the owner wrote nothing, or less, in the
   same network delay. Vanilla only moves items in a chest while its window is open, and an open chest is skipped; a
   client mod that moves items in closed chests (quick stack to nearby chests, craft from chests) on the game of a
   player without this mod can collide with the write. The README says so; skipping every chest owned by a player
   without the mod would stop the mod from cleaning exactly the bases it is for.
7. **An open chest of another player is left alone and reported**, with its position. A chest whose `InUse` mark was
   left behind by a player who lost the connection and that another player's game inherited is skipped the same way
   (the two states are the same data); the answer says to open and close it once.
8. **One frame per pass.** A scan or a write never spans frames: nothing is held across a yield, and a save cannot
   start in the middle. Measured: about 1 ms for the 30 000 objects of the test world. A large world is not measured
   (section 9).
9. **Saving after a clear is automatic**, with the calls of the game's autosave (not the `save` command's path, 1.6):
   without it a crash before the next autosave brings the tags back, and the players' cleaned inventories would only
   be saved at their next character save. Before the save, every object that had a tag is marked for it (2.4 step 7).
10. **The character mark is not touched** (non-goal). **To confirm with the user** whether an opt-in is wanted.
11. **Scan and clear of a whole server are admin-only. The items a player carries are that player's own data:** the
    command always cleans them on the game it is typed on, admin or not, with or without the mod on the server. On a
    server without the mod the loaded objects are cleaned too (any client can already write the objects around it).
12. **Category UX.** An admin command whose visible effect is a tooltip line; UX is one of the seven sheet categories
    every tool knows. Core is for shared framework mods. **To confirm with the user** (the GUID is permanent).

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Runs on |
|---|---|
| Command | The game it is typed on |
| World pass, scope World; the whole run | The game that holds the world (dedicated server, host, single player) |
| Memory pass; carried items | Every game that has the mod (its own copies only) |
| World pass, scope Owned | A player's game, when the server asks |
| World pass, scope Loaded | A player's game alone on a server without the mod |

### 4.2 RPCs, ZDO keys, network version

Peer RPCs (`ZRpc`, one connection, never relayed), one string each, first field the layout "1":

| Name | Direction | Payload | Meaning |
|---|---|---|---|
| `<GUID>.Request` | client to server | `1|clear` or `1|scan` | An admin asks for the world. Refused with an answer when not admin, when the layout is unknown, or when a run is going on. |
| `<GUID>.Local` | server to client | `1|clear` | "Clean your side now", to every ready player whose copy matches (`NetworkGate.PeerCompatible`). |
| `<GUID>.Reply` | server to client | `1|<lines>` | The answer for the admin who asked. |

No own ZDO key: only vanilla keys are written (`cheated`, `cheatedQueued` and its slot keys, `items`, `itemData`,
`<n>_itemData`). Network version 1; bump it when a name or payload changes. One string per RPC on purpose: a peer RPC
whose arguments do not match disconnects the player ("incompatible version").

### 4.3 Hand-off cases

- **A player without the mod on a server with it:** joins normally. Sees cleaned items and objects like any others
  (vanilla data). The items they carry keep the tag and spread it again; the admin's answer counts such players.
- **Objects such a player owns:** written by the server. Idle ones hold; busy ones can come back, the run repeats and
  reports. A chest they have open is skipped and reported.
- **A player with the mod on a server without it:** own items and loaded objects; the answer says so. Its writes travel
  to the server like any change that game makes, and like any such change they reach that server's disk only with the
  next save of a chunk something else marked (1.4): likely soon for a base in use, not certain, and unlikely for a
  portal. Nothing can be done about it from a client (it cannot mark the server's chunks, and making a throwaway object
  to force a mark would add things to the world).
- **A player who is not an admin, on a server with the mod:** the command cleans the carried items and the server
  refuses the rest.
- **Mod turned off on one side:** on the server, clients are told (framework) and fall back to their own side; on a
  client, its commands are gone and it ignores the server's request.
- **Another version on the server** (framework state ServerMismatch; cannot happen while only network version 1
  exists): own side only, and the answer says to use matching versions.
- **The admin leaves during a run:** the run finishes and saves; the answer is in the server's log.
- **A player joins during a run:** not asked and not counted; they can type the command themselves.

### 4.4 Dedicated server

Nothing in the server path touches a game object, `Player.m_localPlayer`, `MessageHud`, `Achievements` or the console:
tables in, `ZDO.Set` out, log lines, RPC answers. `Achievements.IsCheatedAtAll` in particular is never called by the mod
(on a server it reads `ServerOptionsGUI.m_instance` without a null check).

## 5. Config

`General.Enabled` and `General.Status` (framework). No setting of its own.

## 6. Compatibility

### 6.1 Other MC mods

No MC mod patches a method this mod calls or stores data it rewrites differently. Item custom data
(`MC.Combat.Crossbow.StaysLoaded.Loaded`, `MC.Combat.Weapons.DualWield.OffHand`) and items of MC mods that add their
own prefabs survive because bytes other than the tag bit are never touched. Sort Chest refuses to merge a tagged stack
with a clean one; after a clean-up they merge. Forge Idol Upgrades copies vanilla's "tagged inputs or tagged station
give a tagged result"; a cleaned station stops that. No always-on scrubbing exists in this mod, so Sort Chest's
self-test that expects a tag it sets to stay is not disturbed.

### 6.2 Existing mods for the same job (survey 2026-10-12)

- Whole-world cleaners: **NoCheatedTag** (cleans at world load and by command on the server, skips objects owned by
  connected players; built for 1.0.12, source link dead), **CheatCleanup** (host or single player only, tested on
  1.0.7, closed source), the **BakaLoader** server manager's `baka_cleanse` (dedicated server, refuses while anyone is
  connected; readable source, license forbids derivative works: used here only as a list of what to cover; it removes
  keys and then marks the sector dirty, where this mod writes 0).
- Clean what a client loads: AchievementEnabler (112k downloads; item flags at load, `clearcheateditems` for carried
  items), Achievement Enabler+, NoCheatFlag, KeepAchievements, AchievementSync, CheatTagCleaner (the only client mod
  that also clears piece tags, in a radius), Server Devcommands `clearcheats`.
- Always-on: OdinLookedAway (forces "not cheated" wherever data passes; patches `ZDO` and `Inventory` methods).
- None offers one audited command that covers a dedicated server with players online plus their inventories; none
  states 1.0.17. Vanilla has no removal (Iron Gate announced a fix for wrongly marked items in the 1.0.15 notes; 1.0.16
  and 1.0.17 do not contain one).

### 6.3 Mods that interact

- **Delete tagged items** (NoCheatItems, ItemIntegrity ServerOnly, CheatTagEnforcement): can delete before the clean-up.
  README: remove them first (NoCheatItems and ItemIntegrity ServerOnly have a dry-run setting; the survey found none
  for CheatTagEnforcement, which also kicks players who carry tagged items).
- **Scrub by themselves** (the lists above): no conflict; this mod patches none of the methods they patch.
- **Valheim Community Patch** replaces `CookingStation.GetSlot` / `SetSlot` with the same keys: no conflict, the mod
  reads and writes the keys directly.
- **Storage in other formats** (backpacks inside item custom data, extra equipment slots): not reached.
- **Server-side character storage** (ServerCharacters and similar): a restore from an older copy brings tags back.

## 7. Implementation

### 7.1 Files

| File | What |
|---|---|
| `Plugin.cs` | Life cycle; commands follow `Enabled`; Debug `TestBlocked` |
| `ClearCommand.cs` | The two commands and where their work goes |
| `ItemBlobs.cs` | Byte walk of item blobs, pure |
| `WorldSweep.cs` | Scan and write of saved object data, three scopes, counts |
| `LiveSweep.cs` | Carried items and in-memory copies |
| `SweepJob.cs` | One run on the game that holds the world |
| `ServerLink.cs` | The three peer RPCs |
| `Report.cs`, `Output.cs` | The sentences; where they are shown |
| `Patches/ZNetPatches.cs` | RPC handlers on each connection |
| `SelfTests*.cs` | Debug self-tests |

### 7.2 Harmony patches and other entry points

| Target | Kind | Why |
|---|---|---|
| `ZNet.OnNewConnection` | postfix (feature patch) | Register the RPC handlers on the new connection |
| `ZNet.RPC_PeerInfo` | postfix (feature patch) | Again when the peer is ready (the mod may have been turned on while connecting) |

No transpiler, no per-frame patch, nothing always-on. `ServerLink.Start` (from `OnActivated`) covers connections made
while the mod was off. The commands are plain objects in `Terminal.commands`.

### 7.3 State and lifetime

No saved state. Static state: the two command objects, the running coroutine, request counters, the terminal the last
command was typed in. `OnDeactivated` stops a running job and makes the RPC handlers inert.

### 7.4 Live toggle

`Enabled` off: commands removed at once, patches off, handlers inert, a running job stopped. On: back. The world is
never changed by turning the mod on or off.

### 7.5 Debug in-world self-tests

`uncheat.blob`, `uncheat.world`, `uncheat.scan`, `uncheat.blocked` (single player), `uncheat.mp.sweep` and
`uncheat.mp.contested` (dedicated server, scenario `modded`, with the server half `uncheat.mp.server`),
`uncheat.mp.local` (scenario `vanilla-server`, registered from `BindConfig` because the mod is inactive there). One
client cannot be a second player: to play a player without the mod, the test's game ignores the server's "clean your
side" (`ServerLink.TestIgnoreLocal`). Debug hooks: `Output.TestLines`,
`SweepJob.LastScan/LastBefore/LastRemoved/LastAfter/LastWrote/LastRounds/LastMarked/LastMarkedPortal/Finished`,
`ServerLink.TestIgnoreLocal`, `Plugin.TestBlocked`.

What the first dedicated-server runs found (2026-10-12): the `save` command's path throws on a dedicated server within
a minute of a save (1.6; replaced by the autosave calls), and a test that kept an item object of a chest over frames
(a chest reloads with new objects).

### 7.6 Shared framework used

`ModPlugin`, `NetworkGate` (handshake, `PeerCompatible`), `FeatureRegistry`, `PatchGuard`, `Log`, `SelfTest`. Nothing
changed in `src/Shared`.

## 8. Test plan (TESTING.md)

The list is `src/UX/CheatedTag.Remover/TESTING.md`. G1: T01, T02, T08-T12. G2: T02-T04, M01, M04, M06. G3: T02, T05-T07,
M01, M05, M10. G4: T02, M02, M03, M07. G5: M01-M06, M08, M09, M11. E1: T01, M02. E2: T02, M02, M03, M13. E3: T02, T04,
M06, M09, M11. Live toggle T09, `Enabled = false` T10, clean log T14 and M14, hand-off M03 and M04, cross-mod C01-C06.

What the review round changed (2026-10-12), for the record:

- the game that holds the world now marks every object that had a tag for saving: what a player's game cleans comes in
  unmarked and could be tagged again after a restart (a portal in particular);
- the command cleans the carried items of the player who types it on every server, admin or not;
- a game alone on a server without the mod counts again before it says "removed" when it wrote objects of other
  players' games;
- a run stops when the world is closing under it; a player without a character yet cleans its carried items when it
  spawns; a guest admin's answer no longer counts the host's carried items;
- sentences: the open-chest answer (also a chest left open by a player who lost the connection, with positions),
  "their games were asked", another version on the server;
- bytes left on an emptied stand are no longer counted as items;
- tests: a portal check that could not fail, the non-admin scan, the second save within a minute, the open-chest skip
  and the three passes on the server (`uncheat.mp.contested`), server clean-up on every path.

A second reader then checked only the code written for those fixes and found two more things, both fixed: the marking
and the save could run while another save was still writing (the marks would have been lost; 2.4 step 7 now waits), and
a game alone said "No cheated tag could be removed." when all it had cleaned on another player's objects was leftover
stand bytes. Also from that pass: a host without a character in the world (dead, loading) cleans its carried items
when it spawns, like a guest does; the delayed answer of a game alone says "Still tagged around you" and names both
reasons (another player's game, or objects that were just loaded).

## 9. Open questions and unverified

- **Cost on a large world.** One pass took about 1 ms on the 30 000 objects of the test world. Worlds of hundreds of
  thousands to millions of objects are not measured; the scan is one binary search per object with number data, and
  vanilla does comparable whole-world work on the main thread at every save. The server's log line gives the time.
- **Objects that come back.** `uncheat.mp.contested` shows the mechanism (an object its owner tags again every frame is
  written three times and reported). Not seen with two real players: how often a creature in a fight, a running
  smelter or a lit cooking station owned by a player without the mod really needs more than three passes.
- **A chest open on a player without the mod** while the server runs: skipped and reported in `uncheat.mp.contested`
  (one client playing that player); a real second player is a by-hand item.
- **`InUse` left behind by a player who lost the connection with a chest open**, with and without another player near
  (1.5): from code; by-hand items M10.
- **Persistence across a dedicated server restart**, a tagged portal included, with the portal built before the last
  save (a portal built in the same session is saved anyway): by-hand items T04 and M09.
- **A player's game with a mod that moves items in closed chests**, without this mod (decision 6): the collision is
  reasoned from the conflict rule, not reproduced.
- **A server without the mod**: what scope Loaded cleaned reaches that server's disk only with a later change of the
  same area (4.3). By-hand item M07 asks to restart that server and write down what came back.
- **Why the user's server got tagged** (admin tools, the launch-week bug fixed in 1.0.14, imported characters) decides
  how fast tags return.
- Prefab data not read: the largest `ArmorStand` slot count and `CookingStation` slot count of the game and of modded
  stations (bounds 32 and 32 are far above vanilla's 5 cooking slots and the 14 slots vanilla's own converter knows).
