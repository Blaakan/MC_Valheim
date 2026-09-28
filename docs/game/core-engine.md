# Core Engine & Modding Surface

Game version: 1.0.16 (network 40). Source: decompiled assembly_valheim.

> Scope: the engine-level systems every mod in this repo touches — boot order, content registries,
> networking (ZDO / RPC / ownership), persistence, the item data model, status-effect registry,
> localization, input, console, asset loading, the character class hierarchy, dedicated-server and
> crossplay differences, logging, and the (small) official mod surface. Gameplay systems (combat,
> building, farming...) get their own chapters; they all build on the rules written here.
>
> Citations are `Class.Method` in the decompiled tree at `.ref/decompiled/<assembly>/<Class>.cs`
> (global namespace unless stated). "Inferred" marks conclusions drawn from code structure that
> were not directly observed at runtime. Verify against the decompiled source when in doubt.
> The dedicated server installed locally is **1.0.15** (same network version 40). It was decompiled
> separately for the dedicated-server diff in §15.

---

## Overview

Valheim is a Unity 6 (Mono) game whose simulation is **peer-authoritative per object**. The server is
mostly a database and router. Each networked object is a **ZDO** (a key/value record with an owner).
The peer that *owns* a ZDO runs its simulation (AI, physics, damage resolution, timers) and writes its
state. Everyone else instantiates a local GameObject from that ZDO and reads it. Nearly every modding
decision reduces to three questions:

1. **When** does my code run relative to the game's singletons (`ObjectDB`, `ZNetScene`, `ZNet`,
   `Player.m_localPlayer`)?
2. **Who** is authoritative for the state I touch (ZDO owner, server, or local client), and how do I reach
   them (RPC to owner, routed RPC to server)?
3. **Where** does the state persist (ZDO → world save, `Player` → character `.fch`, `ItemData.m_customData`
   → wherever the item lives, global keys → world), and does a client without my mod survive it?

| System | Key classes (assembly) |
|---|---|
| Boot & scenes | `FejdStartup`, `Game`, `SystemResourceManager`, `MonoUpdaters` (valheim) |
| Content registries | `ObjectDB`, `ZNetScene`, `PieceTable`, `Recipe`, `StatusEffect` (valheim) |
| Transport & handshake | `ZNet`, `ZNetPeer`, `ZRpc`, `ISocket`, `ZSteamSocket`, `ZPlayFabSocket`, `Version` (valheim) |
| Object replication | `ZDO`, `ZDOID`, `ZDOExtraData`, `ZDOVars`, `ZDOMan`, `ZDOHelper` (valheim) |
| RPC | `ZNetView`, `ZRoutedRpc`, `ZPackage`, `ISerializableParameter` (valheim) |
| Persistence | `ZNet` (world), `PlayerProfile`, `Player.Save/Load`, `Inventory`, `ZoneSystem` global keys, `SaveSystem` |
| Items | `ItemDrop`, `ItemDrop.ItemData`, `ItemDrop.ItemData.SharedData`, `Inventory` |
| Status effects | `StatusEffect` (ScriptableObject), `SEMan` |
| Localization | `Localization` (assembly_guiutils) |
| Input | `ZInput` (assembly_utils), `Player.TakeInput`, `PlayerController.TakeInput` |
| Console | `Terminal`, `Terminal.ConsoleCommand`, `Console`, `Chat` |
| Assets | `SoftReferenceableAssets.*` (SoftReferenceableAssets), `LocationList`, `DungeonDB` |
| Characters | `Character` → `Humanoid` → `Player`; `BaseAI` → `MonsterAI`/`AnimalAI`; `MonoUpdaters` |
| Logging | `ZLog` (assembly_utils), `Terminal.Log/TryTest*` |

---

## 1. Boot sequence & plugin timing

### Key classes
- **BepInEx chainloader.** `BepInEx/config/BepInEx.cfg` sets `[Preloader.Entrypoint] Assembly = UnityEngine.CoreModule.dll, Type = GameObject, Method = .cctor`. All plugins' `Awake` therefore run when `UnityEngine.GameObject` is first touched, **before any game scene object has run `Awake`**. At that point `ObjectDB.instance`, `ZNet.instance`, `ZNetScene.instance`, `Game.instance`, `Player.m_localPlayer` are all null.
- **`FejdStartup`** (start/menu scene). It parses CLI args, initializes platforms, owns the menu `ObjectDB`, and loads the main scene.
- **`Game`** (main scene). It owns the `PlayerProfile`, spawn/respawn, autosave, pause, world-rate globals, and portal connection (server).
- **`SystemResourceManager.FastLoadScene`**. It loads scenes through the SoftRef scene manager (see §13).
- **`MonoUpdaters`**. It is created once by `FejdStartup.AwakePlatforms` (`DontDestroyOnLoad`) and drives the batched `CustomFixedUpdate/CustomUpdate/CustomLateUpdate` of characters, AI, ships, etc. (§14).

### Flow (client, verified call order)
1. **Plugin Awake** (BepInEx). Only bind config and apply Harmony patches here. Do not touch game singletons, and do not touch `Localization.instance` either: it lazily initializes and reads `PlatformManager.DistributionPlatform.LocalUser` (`Localization.SetLanguageFromLocale`).
2. **`FejdStartup.Awake`**: `ParseArguments` (`-console`, `-password`, `-joincode`...), then `AwakePlatforms` (creates `MonoUpdaters`, Steam, PlayFab), logs `Valheim version: … (network version 40)`, `Settings.ApplyStartupSettings`, `WorldGenerator.Initialize(menu world)`. It then instantiates the **Console** prefab, whose `Terminal.Awake` calls `Terminal.InitTerminal`, which registers all vanilla commands. Finally it calls `ZInput.Initialize`.
3. **`FejdStartup.Start`**: `SetupGui` (shows `m_moddedText` iff `Game.isModded`), then **`SetupObjectDB`**. That method does `AddComponent<ObjectDB>()`, which runs `ObjectDB.Awake` with **empty** lists, then `ObjectDB.CopyOtherDB(m_objectDBPrefab)`, which fills items, recipes, SEs and terrain ops and calls `UpdateRegisters`. This menu `ObjectDB` serves the character preview (`FejdStartup.SetupCharacterPreview`, which loads the profile's inventory).
4. The user picks a character and world, or joins. Then `Game.SetProfile`, `ZNet.SetServer(...)` (host) or `ZNet.SetServerHost(...)` (client) run, followed by `FejdStartup.TransitionToMainScene`, `LoadMainSceneIfBackendSelected` and `LoadMainScene`, which calls `SystemResourceManager.FastLoadScene(m_mainScene)`.
5. **Main scene `Awake`s** (Unity order between different objects is not visible in code). What we can state:
   - `ZNet.Awake` creates `new ZRoutedRpc(isServer)` and `new ZDOMan(512)`. The `ZDOMan` constructor registers the `DestroyZDO`/`RequestZDO` routed RPCs and calls `ZDOExtraData.Init()`, which **clears session-only hashes**. Server-side, `ZNet.Awake` also loads admin/ban/permitted lists.
   - `ZNetScene.Awake` dereferences `ZDOMan.instance` and `ZRoutedRpc.instance`, so it *must* run after `ZNet.Awake` (inferred from the null-safety). It builds `m_namedPrefabs` from `m_prefabs` and `m_nonNetViewPrefabs` with `Dictionary.Add`, so a duplicate name throws. It registers the `SpawnObject` routed RPC.
   - `ObjectDB.Awake` (main-scene instance): `UpdateRegisters` over its serialized lists.
   - `Game.Awake`: `ZInput.Initialize`, instantiates Console/ServerOptions if missing, **loads the `PlayerProfile` from disk** (`PlayerProfile.Load`), logs `isModded: …`.
   - `ZoneSystem.Awake`: instantiates `m_locationLists`/`m_altBiomeLists`.
6. **Main scene `Start`s**:
   - `ZNet.Start`: `s_onZNetStart?.Invoke()`, then **server:** `ServerLoadWorld` (`LoadWorld`, which handles chunked saves, or `LoadOldWorld`, followed by `ZoneSystem.GenerateLocationsIfNeeded`), or **client:** `ClientConnect`.
   - `Game.Start`: registers routed RPCs (`SleepStart`, `RPC_SetConnection`, `RPC_DiscoverLocationResponse`...; server-only ones behind `IsServer()`).
   - `ZoneSystem.Start`: `SetupLocations` (merges every live `LocationList`, see §18), then global-key RPCs (server: `SetGlobalKey`/`RemoveGlobalKey`; client: `GlobalKeys`, `LocationIcons`).
7. **Connected**: on the first `Game.FixedUpdate` where `ZNet.GetConnectionStatus() == Connected`, it calls `RequestRespawn(0)`. `Game.UpdateRespawn` then runs `FindSpawnPoint` and `SpawnPlayer`, which does: `Instantiate(m_playerPrefab)`, then `Player.SetLocalPlayer`, then **`PlayerProfile.LoadPlayerData`**, which calls `Player.Load` (inventory, skills, `m_customData`), then `ZNet.SetCharacterID`, then `Player.OnSpawned`. On the very first spawn of the session, `Game.m_playerInitialSpawn` fires.
8. **Logout/quit**: `Game.Logout`, `ContinueLogout`, then `Shutdown`, which calls `SavePlayerProfile(setLogoutPoint: true)`, `ZNetScene.Shutdown` and `ZNet.Shutdown(save)`. After that it loads the start scene again. **Everything in the main scene is destroyed and recreated on the next join**, including `ZNet`, `ZNetScene`, `ObjectDB`, `ZRoutedRpc` and `ZDOMan`.

### Data & persistence
- `Game.m_saveInterval = 1800` s autosave (`Game.UpdateSaving`), with a 30 s pre-warning broadcast by the server. `Game.SavePlayerProfile` writes the character. `ZNet.Save` writes the world (host/server only).
- `Game.CollectResources` calls `Resources.UnloadUnusedAssets` (at most every 20 min, plus hourly). Mod-created objects that nothing references can be unloaded. Keep strong refs (e.g. `DontDestroyOnLoad` container) to runtime-created prefabs/materials.

### Multiplayer authority
- `ZNet.m_isServer` (static, default `true`) is set by `ZNet.SetServer` before the main scene loads. Host = server + local player. Single-player = server with `m_openServer == false` (`ZNet.IsSinglePlayer`).

### Patch points
| Hook | Why |
|---|---|
| Plugin `Awake` | Harmony + config only. |
| `FejdStartup.Awake` postfix | Earliest point with Console, `ZInput` and platforms up. Good for menu UI tweaks. |
| `FejdStartup.SetupObjectDB` / `ObjectDB.CopyOtherDB` postfix | Register mod items into the **menu** ObjectDB (character preview shows modded gear; see §2). |
| `ZNet.Awake` postfix | First point per session where `ZRoutedRpc.instance` and `ZDOMan.instance` exist. Register routed RPCs and `ZDOExtraData.AddSessionHash` here. |
| `ZNetScene.Awake` prefix/postfix | Register networked prefabs (§2). |
| `ObjectDB.Awake` postfix | Register items/recipes/SEs in the **main** ObjectDB (§2). |
| `Game.Start` postfix | Also a fine place for routed RPC registration (runs after `ZNet.Awake`). |
| `ZNet.s_onZNetStart` (static Action) | No-patch hook fired at the start of `ZNet.Start`. |
| `Game.m_playerInitialSpawn` (static event) | No-patch hook: local player spawned for the first time this session. |
| `Player.OnSpawned` postfix | Every spawn (incl. respawn after death). Local player is fully loaded. |
| `Game.Shutdown` prefix | Last chance to flush mod state before save and teardown. |

---

## 2. Content registries: `ObjectDB` and `ZNetScene` (where and when to register)

### Key classes
- **`ObjectDB`** (MonoBehaviour singleton, one per scene). Public lists: `m_items` (item prefabs, `GameObject` with `ItemDrop`), `m_recipes` (`Recipe` ScriptableObjects), `m_StatusEffects` (`StatusEffect` ScriptableObjects), `m_terrainOps` (`TerrainOp`, new in 1.0). Private indices: `m_itemByHash` (name hash → prefab), `m_itemByData` (`SharedData` reference → prefab), and public `m_terrainOpsByHash`. They are rebuilt by **private** `ObjectDB.UpdateRegisters` (called from `Awake` and `CopyOtherDB`).
- **`ZNetScene`**. It holds the public `m_prefabs` (networked prefabs, i.e. anything with a `ZNetView`), the public `m_nonNetViewPrefabs`, and the private readonly `m_namedPrefabs` (hash → prefab). It also keeps `m_instances` (ZDO → ZNetView). **Every object that can exist in the world as a ZDO must be in `ZNetScene`, including item prefabs (dropped items), pieces, creatures, projectiles and spawned effects that carry a `ZNetView`.**
- **`PieceTable`** (MonoBehaviour on a tool's `SharedData.m_buildPieces`, e.g. the hammer). Its `m_pieces` list holds piece prefabs. Pieces must also be in `ZNetScene.m_prefabs`.
- **`Recipe`** (ScriptableObject): `m_item`, `m_amount`, `m_enabled`, `m_craftingStation`, `m_repairStation`, `m_minStationLevel`, `m_resources` (`Piece.Requirement[]`), `m_requireOnlyOneIngredient`, `m_qualityResultAmountMultiplier`.

### Flow / lookups
- Prefab identity everywhere is **`name.GetStableHashCode()`** (`StringExtensionMethods.GetStableHashCode`, a deterministic djb2-style hash, safe to store). Lookups:
  - `ObjectDB.GetItemPrefab(string|int)`, `TryGetItemPrefab(...)`, `GetItemPrefab(SharedData)`
  - `ZNetScene.GetPrefab(string|int)`, `HasPrefab(int)`, `GetPrefabHash(go)`
  - `ObjectDB.GetStatusEffect(int nameHash)` (linear scan)
  - `ObjectDB.GetRecipe(ItemData)` (linear scan by `m_shared.m_name`)
- `Utils.GetPrefabName(name)` truncates at the first `'('` **or space**. `ZNetView.Awake` and `ItemDrop.Awake` derive the prefab hash from that truncated name. **Mod prefab names must not contain spaces or parentheses.**
- `ZNetScene.CreateObject(zdo)` instantiates `GetPrefab(zdo.GetPrefab())` with `ZNetView.m_initZDO` set. An unknown hash logs `Missing prefab hash`. **If the peer is the server, `CreateObjectsSorted`/`CreateDistantObjects` take ownership and `DestroyZDO` the object** (`Destroyed invalid prefab ZDO`). A host without the mod therefore **deletes** modded objects that come into its active area. A dedicated server never instantiates world objects (§15), so it does not trigger this path. Clients without the mod just log warnings and see nothing.

### Where and when to register (recommended project pattern)
| Content | Register into | When | Notes |
|---|---|---|---|
| Networked prefab (piece, creature, projectile, item world-object) | `ZNetScene.m_prefabs` | `ZNetScene.Awake` **prefix** (the list is turned into `m_namedPrefabs` inside Awake) | In a postfix you must also insert into private `m_namedPrefabs`. Guard duplicates with `HasPrefab`. |
| Item | `ObjectDB.m_items` **and** `ZNetScene.m_prefabs` | `ObjectDB.Awake` postfix (main) **and** `ObjectDB.CopyOtherDB` postfix (menu); ZNetScene as above | After adding, re-run private `UpdateRegisters` (AccessTools) or insert into `m_itemByHash`/`m_itemByData` yourself. `UpdateRegisters` uses `Dictionary.Add`, so a duplicate name throws. |
| Recipe | `ObjectDB.m_recipes` | same as items | `Recipe.m_item` must reference the registered prefab's `ItemDrop`. Known recipes are matched by `m_shared.m_name`. |
| Status effect | `ObjectDB.m_StatusEffects` | same as items | Identity = `ScriptableObject.name` hash (`StatusEffect.NameHash`). Set a unique `.name`. |
| Piece | `ZNetScene.m_prefabs` + tool's `PieceTable.m_pieces` | ZNetScene hook + `ObjectDB.Awake` postfix (tool prefab lives in `m_items`) | `PieceTable` lives on the tool's shared data, so edits persist across sessions. Add idempotently. |
| Terrain op | `ObjectDB.m_terrainOps` (+ `m_terrainOpsByHash`) | `ObjectDB.Awake` postfix | New 1.0 registry. |
| Location / vegetation / env / random event | a `LocationList` component alive before `ZoneSystem.Start` | see §18 | Locations use SoftReferences (§13). |

**Why both ObjectDB hooks:** the main-scene `ObjectDB` is a fresh scene object every session. The menu one is built by `CopyOtherDB` from a prefab. Character loading (`Player.Load` → `Inventory.Load` → `Inventory.AddItem(int prefabHash, …)`) resolves items through `ObjectDB.instance`. **An item whose prefab is not registered at load time is silently dropped (`Failed to find item prefab`) and disappears at the next save.** The same happens in container ZDOs when a client without the mod opens them and saves.

### Data & persistence
- Registries are rebuilt each session, but **prefab assets are shared and live across sessions**. Mutations to vanilla prefabs, such as `m_shared` fields, `PieceTable.m_pieces` or recipe lists on prefabs, are not undone on logout. Every registration must be **idempotent** (check `Contains`/`HasPrefab` first).

### Multiplayer authority
- Registries are purely local. **All peers must register identical content with identical names**, or items/pieces vanish for peers that lack them (see "server destroys unknown ZDOs" above).

### Patch points
`ZNetScene.Awake`, `ObjectDB.Awake`, `ObjectDB.CopyOtherDB`, `ObjectDB.UpdateRegisters` (private, call it rather than patch it), `ZNetScene.CreateObject` (diagnostics only).

---

## 3. Networking I: transport, peers, handshake & version checks

### Key classes
- **`ZNet`** (main-scene singleton). Peers, connection status, handshake, world load/save, net time, admin/ban lists, player list, remote commands.
- **`ZNetPeer`**: `m_rpc` (`ZRpc`), `m_socket` (`ISocket`), `m_uid` (session UID, 0 until handshake completes, see `IsReady()`), `m_server` (this peer *is* the server), `m_refPos` (their active-area center), `m_characterID` (their player ZDO), `m_playerID`, `m_playerName`, `m_playfabId`, `m_serverSyncedPlayerData`.
- **`ZRpc`**: a point-to-point RPC over one socket. Method id = `name.GetStableHashCode()`. `ZRpc.Register` **replaces** an existing handler with the same name. Ping keep-alive; `m_timeout` is 30 s, or 90 s with `ZRpc.SetLongTimeout(true)`. An `EndOfStreamException` while handling a package is treated as `ErrorCode.IncompatibleVersion` (`ZRpc.Update`).
- **Sockets** (`ISocket`): `ZSteamSocket` (Steam networking, reliable sends, global send rate pinned to **153 600 B/s** min=max in `ZSteamSocket.RegisterGlobalCallbacks`), `ZPlayFabSocket` (crossplay, zlib compression enabled after `VersionMatch`), and `CustomSocket` backend (direct IP). `ZNet.m_onlineBackend` selects the backend.
- **`Version`**: `CurrentVersion = 1.0.16`, `c_networkVersion = 40u` (a **const**, inlined as the literal `40u` at every use site), `c_PlayerVersion = Player.DeepNorth (46)`, `c_WorldVersion = World.DeepNorth (41)`, `c_ItemDataVersion = Item.ChunksNCheats (109)`, `c_PlayerDataVersion = PlayerData.ChunkedNorth (33)`.

### Flow: connection handshake
1. The socket connects, and `ZNet.OnNewConnection(peer)` registers peer RPCs on **both** sides: `PeerInfo`, `Disconnect`, `SavePlayerProfile`, simulation-distance RPCs. The server adds `ServerHandshake`. The client adds `Kicked`, `Error`, `ClientHandshake` and then **immediately invokes `ServerHandshake(inviteSecretKey)`**.
2. Server `ZNet.RPC_ServerHandshake` replies `ClientHandshake(needPassword, salt)`.
3. Client `ZNet.RPC_ClientHandshake` shows the password dialog if needed, then `SendPeerInfo`, which writes: UID, version string, **network version (40)**, ref position, player name, PlayFab id, desired `SimulationDistance`, then (client) password hash, invite key, Steam session ticket. The server's `SendPeerInfo` additionally writes world name/seed/seedName/uid/worldGenVersion and `m_netTime`.
4. `ZNet.RPC_PeerInfo` (both sides) checks the version: **only the network version is compared** (`num != 40` produces `Error(3)` = `ErrorVersion`). A 1.0.15 client can join a 1.0.16 server. The server then checks the white/black list (`Error 8`), the Steam session ticket, PlayFab block list and crossplay privilege (`Error 10`), `ServerPlayerLimit = 10` (`Error 9`), password or invite key (`Error 6`), and already-connected (`Error 7`).
5. On success, it registers post-handshake RPCs (`ServerSyncedPlayerData`, `PlayerList`, `AdminList`, `RemotePrint`; server: `CharacterID`, `PlayerID`, `Kick`, `Ban`, `RPC_RemoteCommand`, `Save`...; client: `NetTime`). It calls `peer.m_socket.VersionMatch()`, then `m_zdoMan.AddPeer(peer)` and `m_routedRpc.AddPeer(peer)`, which **fires `ZRoutedRpc.m_onNewPeer(uid)`**.
6. Steady state (`ZNet.SendPeriodicData`, every 2 s): the server sends `NetTime` and the player list to clients. Clients send `ServerSyncedPlayerData` (ref position + the public `ZNet.m_serverSyncedPlayerData` string dictionary) to the server. The server stores it in `peer.m_serverSyncedPlayerData`.

`ConnectionStatus` values (the `Error(int)` payload): 0 None, 1 Connecting, 2 Connected, **3 ErrorVersion**, 4 ErrorDisconnected, 5 ErrorConnectFailed, 6 ErrorPassword, 7 ErrorAlreadyConnected, 8 ErrorBanned, 9 ErrorFull, 10 ErrorPlatformExcluded, 11 ErrorCrossplayPrivilege, 12 ErrorKicked.

### Mod version checks (there is no built-in mechanism)
There is **no modded flag in the handshake**. `Game.isModded` is never sent (see §18), and the network version is a const that a patch cannot change. To enforce mod parity, do what Jotunn and ServerSync do:
- In a **`ZNet.OnNewConnection` postfix**, register a peer RPC (e.g. `VMods_VersionCheck`, taking a `ZPackage`). On the client, invoke it right away with the list of our mod GUIDs and versions. The reliable ordered channel guarantees it arrives before `PeerInfo`.
- In a **`ZNet.RPC_PeerInfo` prefix** on the server, reject peers whose check failed or never arrived: `rpc.Invoke("Error", 3)` and skip the original. Mirror this on the client to detect a vanilla or mismatched server.
- Optional: have the server send its list back so the client can show a readable reason, since `ErrorVersion` alone shows the vanilla "incompatible version" text.

### Data & persistence
- `ZNet.GetUID()` equals `ZDOMan.GetSessionID()`: a **per-session** id from `Utils.GenerateUID`. It is not stable across sessions. For persistent identity use `Player.GetPlayerID()` (the profile's `m_playerID`, stored in the player ZDO as `playerID`) or the platform id (`peer.m_socket.GetHostName()`, used for admin lists).
- Net time: `ZNet.GetTime()` / `GetTimeSeconds()` are server-authoritative. `m_netTime` is saved in the world and only advances on the server while players are connected (`ZNet.UpdateNetTime`). **Use it for all persistent timestamps** (vanilla stores ticks, e.g. `ZDOVars.s_spawnTime`, `s_pickedTime`).

### Multiplayer authority
- `ZNet.IsServer()`: true on host, dedicated server and single-player.
- `ZNet.IsDedicated()`: **hard-coded `false` in the client DLL and `true` in the server DLL** (separate builds; §15). For "is the server I'm connected to dedicated", use `ZNet.IsCurrentServerDedicated()` (a ready peer with no character).
- Admin: `ZNet.LocalPlayerIsAdminOrHost()`, `PlayerIsAdmin(PlatformUserID)`. The server checks admin via the **socket host name** of the calling `ZRpc` (`ZNet.RPC_RemoteCommand`, `ListContainsId`), which cannot be spoofed.

### Patch points
| Hook | Use |
|---|---|
| `ZNet.OnNewConnection` postfix | Register per-peer `ZRpc` methods (version check, config sync, admin-only requests). |
| `ZNet.RPC_PeerInfo` prefix | Accept/reject peers (mod parity), attach per-peer mod state. |
| `ZRoutedRpc.m_onNewPeer` (Action) | Server: push config/state to a newly ready peer without patching. |
| `ZNet.Disconnect` prefix | Clean up per-peer mod state. |
| `ZNet.m_serverSyncedPlayerData` (public dict) | Zero-patch client→server string channel, sent every 2 s, not persisted. Vanilla uses `platformDisplayName` and `baseValue`. |
| `ZNet.RPC_PeerInfo` transpiler | Only for raising the 10-player cap (`ServerPlayerLimit` is an inlined const). |

---

## 4. Networking II: the ZDO data model

### Key classes
- **`ZDO`**: one networked object record. Fields: `m_uid` (`ZDOID`), private `m_position`, `m_rotation` (euler), `m_prefab` (hash). Flags: `Persistent`, `Distant`, `Type` (`ObjectType`: Default, Prioritized, Solid, Terrain), `Created`. Revisions: `OwnerRevision` (ushort), `DataRevision` (uint). The key/value payload is **not stored on the ZDO object**: it lives in static per-type dictionaries in `ZDOExtraData` keyed by `ZDOID`.
- **`ZDOID`**: `(long UserID, uint ID)`. The creator's session id plus a counter. User ids are interned to a `ushort` key (`ZDOID.AddUser`).
- **`ZDOExtraData`** (static): separate `BinarySearchDictionary<int, T>` per type: float, Vector3, Quaternion, int (bools are stored as int 0/1), long, string, byte[]. It also holds connections (`ConnectionType`: Portal, SyncTransform, Spawned, with a `Target` flag), owners, and **`s_sessionOnly`**, the hashes that are synced but **never saved**.
- **`ZDOVars`** (static readonly int hashes). The canonical key list (~200 keys). Examples relevant to mods: `s_health`, `s_level`, `s_tamed`, `s_items` (container inventory byte[]), `s_itemData` (dropped item byte[]), `s_adrenaline`, `s_weaponLoaded` (`"WeaponLoaded"`), `s_seAttrib`, `s_playerID`, `s_playerName`, `s_spawnTime`, `s_creator`, `s_rightItem`/`s_leftItem`/`s_trinketItem` (visual equipment), `s_scaleHash`. `ZDOVars.s_sessionHashes` lists the vanilla sync-only keys (velocities, `alert`, `InUse`, `animation_speed`...).
- **`ZDOMan`**: owns all ZDOs (`m_objectsByID`), the sector grid (512×512 zones of 64 m), dead-ZDO tombstones (server), per-peer sync state (`ZDOPeer.m_zdos` revision cache, `m_forceSend`), and the save/load of chunks.

### Flow
- **Create**: `ZNetView.Awake` (no init ZDO) calls `ZDOMan.CreateNewZDO(pos, prefabHash)`. The **creator becomes the owner** (`SetOwnerInternal(m_sessionID)`). `ZNetView` then copies `m_persistent`, `m_type`, `m_distant`, prefab and rotation into the ZDO.
- **Write**: `ZDO.Set(int|string, value)` calls `ZDOExtraData.Set`. If the value changed, `IncreaseDataRevision` runs: `DataRevision++`, the ZDO is queued (`ClientChanged` on clients), and the sector is marked dirty for save. **`ZDO.Set` does not check ownership.** Any peer can write any ZDO locally.
- **Replicate** (`ZDOMan.Update` → `SendZDOToPeers2`): the manager round-robins peers every 50 ms. `SendZDOs` builds a sync list from sectors around the peer's ref position (`CreateSyncList`) plus force-sends, within a **10 KB send-queue budget** per pass. Each ZDO is sent **whole** (`ZDO.Serialize`: flags, prefab, rotation, all key/values) whenever its revision is newer than what that peer has acknowledged.
- **Receive** (`ZDOMan.RPC_ZDOData`): the incoming data is applied **only if its `DataRevision` > local `DataRevision`**. Otherwise only a higher `OwnerRevision` updates the owner. There is no per-key merge. **Concurrent writes from a non-owner are lost or overwrite the owner's changes unpredictably.** A server receiving a ZDO it has tombstoned (`m_deadZDOs`) re-destroys it.
- **Destroy**: `ZNetScene.Destroy(go)` or `ZNetView.Destroy()` calls `ZDOMan.DestroyZDO`, which **only acts if you are the owner**. It is batched into the `DestroyZDO` routed RPC to everybody, then `HandleDestroyedZDO` runs, which fires `m_onZDODestroyed`, and `ZNetScene.OnZDODestroyed` destroys the GameObject.
- **Instantiate/unload** (`ZNetScene.Update` at 30 Hz → `CreateDestroyObjects`): for ZDOs in the local active area (by `ZNet.GetReferencePosition()` and `SimulationDistance`), create at most 10 objects per tick (100 while teleporting/loading). Objects that leave the area are destroyed (GameObject only). **Non-persistent ZDOs are deleted when their owner unloads them** (`RemoveObjects`).

### Data & persistence
- Only `Persistent` ZDOs are saved and survive owner disconnect. Non-persistent ones are destroyed by the server when the owner leaves (`ZDOMan.RemoveOrphanNonPersistentZDOS`). Players, projectiles and effects are non-persistent.
- Keys in `s_sessionOnly` are filtered out at save (`ZDOExtraData.GetSaveData`). Register your own with `ZDO.AddSessionHash(hash)` / `ZDOExtraData.AddSessionHash(hash)`. The set is **cleared per session** by `ZDOExtraData.Init` (in the `ZDOMan` constructor), so re-register after `ZNet.Awake`, **on the server too** (the server is the one that saves). Vanilla example: `ZSyncAnimation.SetBool/SetFloat/SetInt` sync animator params through session keys `438569 + paramHash`.
- Old/unused keys are stripped on load (`ZDOHelper.s_strip*`, `ZDO.Strip*`). Default values are not stripped for custom keys, so remove keys you no longer need (`ZDO.RemoveInt/RemoveFloat/...` or `Remove(hash)`).
- Type namespaces are independent: `Set("x", 1)` and `Set("x", 1f)` are two different entries.

### Multiplayer authority
The **owner** simulates and writes; others read. The server holds all ZDOs, while clients only hold ZDOs in or near their area (plus force-sent ones). Ownership rules are in §5.

### Patch points
`ZDOMan.RPC_ZDOData` (network diagnostics only), `ZDOMan.m_onZDODestroyed` (Action, clean up per-ZDO caches), `ZDOMan.GetAllZDOsWithPrefabIterative(prefab, list, ref index)` (server-side world scans; it is incremental, so spread it over frames), `ZDOMan.ForceSendZDO(peer, id)` / `RequestZDO(id)` (push/pull a specific ZDO outside the area). Avoid patching `ZDO.Set`/`Get*` (hot path).

---

## 5. Networking III: ownership & authority model

### Rules (from `ZDOMan.ReleaseNearbyZDOS`, server only, every 2 s)
For every **persistent** ZDO inside a peer's near simulation area (the server's own ref position plus every peer's `m_refPos`):
- If the owner is that peer and the ZDO left the peer's active area, the owner is set to 0 (unowned).
- If the ZDO is unowned, or its owner is not in range of it (`IsInPeerActiveArea`), **ownership goes to this peer**. The first peer to be iterated wins; there is no nearest-player tie-break (the server is iterated first, then peers in list order).
- Unowned ZDOs are **not simulated by anyone**. `Character.CustomFixedUpdate`, `BaseAI.UpdateAI`, smelters, fermenters and similar all gate on `IsOwner()`.
- Explicit transfer: `ZDO.SetOwner(uid)` / `ZNetView.ClaimOwnership()` increments `OwnerRevision`, which wins the owner field on receivers.

### Canonical patterns (copy these)
- **Request → owner applies.** For example `Character.Damage(hit)` calls `m_nview.InvokeRPC("RPC_Damage", hit)` (to the owner). `Character.RPC_Damage` returns early `if (!m_nview.IsOwner())`, and damage is computed and applied on the owner. The same pattern is used by `SEMan.AddStatusEffect` (non-owner sends `RPC_AddStatusEffect`), `Character.RPC_AddAdrenaline`, `RPC_Heal` and `RPC_Stagger`.
- **Exclusive edit by ownership hand-off.** In `Container.Interact`, the requester sends `RPC_RequestOpen` to the owner. `Container.RPC_RequestOpen` (running on the owner) checks `IsInUse`/access, calls `ZDOMan.ForceSendZDO(uid, id)`, then **`SetOwner(uid)`** and replies `RPC_OpenResponse(true)`. The requester then edits the inventory as the new owner and `Container.Save` writes `ZDOVars.s_items`.
- **Owner writes, everyone reads.** For example `Player.SetWeaponLoaded` writes `s_weaponLoaded`. `Player.IsWeaponLoaded` returns the local field if owner, else reads the ZDO bool.
- **Server-authoritative world state.** Global keys: `ZoneSystem.SetGlobalKey` sends a routed RPC to the server, `RPC_SetGlobalKey` updates it there, and `SendGlobalKeys(Everybody)` broadcasts. Net time, save, admin lists and sleep work the same way.

### Who owns what (typical)
| State | Authority |
|---|---|
| Local player (position, stats, inventory, equipment, SEs, skills, `m_customData`) | That client (player ZDO is owned by it, non-persistent) |
| Creatures, AI, pieces, containers, pickables, ships, dropped items | Current ZDO owner (a nearby client, or the host) |
| Global keys, world modifiers, net time, world save, events, portal links | Server |
| Character save file | Client (server merely asks via `SavePlayerProfile` RPC) |

### Implications
- Any "rule" computed in a component method gated by `IsOwner()` must be installed on **every client that can own the object** (effectively everyone). A server-only mod cannot change creature AI, damage resolution or crafting-station behaviour on a dedicated server (§15).
- Remote `Player` instances exist on each client but their `Inventory` is empty. Only visual equipment is synced (`VisEquipment` via ZDO item hashes). Never read another player's inventory. Ask their owner via RPC.

---

## 6. Networking IV: `ZNetView` & RPCs

### Key classes
- **`ZNetView`** (component on every networked prefab). Inspector flags: `m_persistent`, `m_distant`, `m_type`, `m_syncInitialScale`. API: `GetZDO()`, `IsValid()` (**always check before use; the ZDO is null after unload or destroy**), `IsOwner()`, `HasOwner()`, `ClaimOwnership()`, `Destroy()`, `Register(name, handler)` (up to 6 args), `Unregister`, `InvokeRPC(method, args)` (→ **owner**), `InvokeRPC(targetPeer, method, args)` (`ZNetView.Everybody = 0` → all peers including self), `SetLocalScale`, `HoldReferenceTo` (SoftRef ref-counting).
  - Static knobs: `m_forceDisableInit`, `StartGhostInit/FinishGhostInit`, `m_useInitZDO/m_initZDO`. In `ZNetView.Awake`, **if `m_forceDisableInit` is set or `ZDOMan.instance == null`, the ZNetView destroys itself** (`Destroy(this)`). The game uses this for throwaway instances (`Inventory.AddItem`, `Humanoid` preview, character preview).
  - `ZNetView.LoadFields`: if the ZDO has `"HasFields"` and `"HasFields<Component>"`, the public fields of that component are overwritten from ZDO values keyed `"<Component>.<field>"` (int, float, bool, Vector3, string, GameObject/ItemDrop by prefab name). The `spawn` console command writes these. This gives per-instance data-driven overrides with no extra code (§18).
- **`ZRoutedRpc`** (per session, created in `ZNet.Awake`). Global and ZDO-targeted RPCs routed **through the server**. `InvokeRoutedRPC(targetPeer, name, args)` handles locally if `target == self` or `target == Everybody (0)`, and forwards otherwise. The server relays to the target or to everyone except the sender. `InvokeRoutedRPC(name, args)` targets the **server** (`GetServerPeerID`). `Register` uses `Dictionary.Add`, so a duplicate name throws. **Unknown method hashes are silently ignored** for global RPCs and logged as warnings for ZDO RPCs. Clients without the mod stay safe.
- **`ZRpc` serialization** (`ZRpc.Serialize/Deserialize`): allowed parameter types are `int, uint, long, float, double, bool, string, ZPackage, List<string>, Vector3, Quaternion, ZDOID, HitData`, and any `ISerializableParameter` (needs a parameterless constructor; used via `Activator.CreateInstance`). **Anything else must go in a `ZPackage`.**
- **`ZPackage`**: binary writer/reader (primitives, `ZDOID`, `Vector3`, `Vector2i/s`, `Quaternion`, byte[], nested packages), `WriteCompressed`/`ReadCompressedPackage`, `GetBase64`/`ZPackage(string)`, `WriteNumItems`/`ReadNumItems` (varint-style counts).

### Flow
`ZNetView.InvokeRPC` → `ZRoutedRpc.InvokeRoutedRPC(owner, zdoId, ...)` → server relay → target `ZRoutedRpc.HandleRoutedRPC` → `ZNetScene.FindInstance(zdo)` → `ZNetView.HandleRoutedRPC`. **If the target peer has not instantiated that object (out of its area), the RPC is dropped.** Owner RPCs normally land, because the owner is by definition in range.

### Security note
`RoutedRPCData.m_senderPeerID` is written by the sender, and the server relays it without rewriting (`ZRoutedRpc.RPC_RoutedRPC` / `RouteRPC`). **Do not use the routed-RPC `sender` for admin or permission checks.** For privileged requests, use a peer `ZRpc` registered in `ZNet.OnNewConnection` and identify the caller by `rpc.GetSocket().GetHostName()`, as `ZNet.RPC_RemoteCommand` does.

### Patch points
Register RPCs in your own component's `Awake` (guard `m_nview.GetZDO() != null`) or in a postfix of the vanilla component's `Awake`. Global routed RPCs go in `ZNet.Awake`/`Game.Start` postfix, **every session**.

---

## 7. Persistence: world save, player profile, custom data, global keys

### World save (server/host)
- Trigger: `ZNet.Save(sync, saveOtherPlayerProfiles, waitForNextFrame)` from autosave (`Game.UpdateSaving`), the `save` command/RPC, or shutdown. `ZNet.SaveWorld` runs `ZDOMan.PrepareSave`, `ZoneSystem.PrepareSave`, `RandEventSystem.PrepareSave` and `PersistentEventSystem.PrepareSave` (snapshot on the main thread), then starts a **background thread** `ZNet.SaveWorldThread`.
- 1.0 **chunked format**: `<savedir>/worlds_local/<worldName>/`. `ZDOMan.SaveChunks` writes dirty chunks only, alongside a main `.db2` (version 41, net time, `ZoneSystem.Save`, `RandEventSystem.Save`, `PersistentEventSystem.Save`), an `.fwl2` meta file and an OK marker. Old `<world>.db/.fwl` worlds load through `ZNet.LoadOldWorld` and are converted.
- Hooks: `ZNet.WorldSaveStarted` (main thread, before snapshot) and `ZNet.WorldSaveFinished` (main thread, from `PrintWorldSaveMessage`, ~0.5 s after the thread ends).
- **Mod world data**: prefer ZDO keys on a persistent object, or global keys. Sidecar files must be keyed by `ZNet.instance.GetWorldUID()` and written from `WorldSaveStarted`/`Finished` on the main thread. Never touch Unity objects from the save thread.
- Load failure sets `ZNet.m_loadError`, which disables saving for the session.

### Player profile (client)
- `PlayerProfile` (`.fch` in `characters_local/`, or cloud). It holds: stats (10 difficulty buckets × 205 `PlayerStatType`), `m_firstSpawn`, **per-world** `WorldPlayerData` keyed by world UID (spawn/logout/death/home points, map data), `m_playerID`, name, and `m_playerData`, **an opaque blob produced by `Player.Save`**.
- `Game.SavePlayerProfile` calls `PlayerProfile.SavePlayerData(Player.m_localPlayer)`, then `Minimap.SaveMapData`, `SaveLogoutPoint` and `PlayerProfile.Save`. A server can request it from all clients (`ZNet.SaveOtherPlayerProfiles` → `SavePlayerProfile` RPC).
- `Player.Save/Load` (version `PlayerData.ChunkedNorth = 33`): health/stamina/eitr, guardian power, **inventory** (`Inventory.Save`), known recipes, stations, materials, tutorials, uniques (player keys), trophies, biomes, known texts, appearance, foods, **skills** (`Skills.Save`), **`Player.m_customData`** (`Dictionary<string,string>`), and build-UI favorites.
- **`Player.m_customData`**: persisted per **character** (across all worlds), local only (not synced to other peers or the server), and trivially editable by the user. For per-world player data, key it with the world UID. For data others must see, mirror it into the player's ZDO.
- Custom skills are dropped on load: `Skills.Load` keeps only `Enum.IsDefined(SkillType)` values (`Skills.IsSkillValid`). Custom skills need that check patched (Jotunn's SkillManager does this).
- Status effects are **not** saved (only guardian power cooldown and foods).

### Global keys & world modifiers (server-authoritative, persisted)
- `ZoneSystem.m_globalKeys` (strings `"name value"`), `m_globalKeysValues`, `m_globalKeysEnums`. API: `SetGlobalKey(string|GlobalKeys[, float])`, `GetGlobalKey(...)`, `RemoveGlobalKey`, `GetGlobalKeys`, `CheckKey(key, GameKeyType.Global|Player)`.
- Writes go to the server (`RPC_SetGlobalKey`/`RPC_RemoveGlobalKey`, **no admin check**) and are broadcast whole to everybody (`SendGlobalKeys`). Clients read their local copy.
- `GlobalKeys` enum below `NonServerOption` are **world modifiers** mirrored into `World.m_startingGlobalKeys`. `Game.UpdateWorldRates` turns them into static rates that **every mod touching these quantities must multiply in**: `Game.m_playerDamageRate, m_enemyDamageRate, m_resourceRate, m_staminaRate, m_eitrRate, m_adrenalineRate, m_durabilityRate, m_foodRate, m_moveStaminaRate, m_staminaRegenRate, m_skillGainRate, m_skillReductionRate, m_carryWeightRate, m_enemySpeedSize, m_enemyLevelUpRate, m_eventRate, m_worldLevel (0..10), m_localDamgeTakenRate` (per-player key `PlayerKeys.DamageTaken`). The same keys hold boss flags (`defeated_*`) and toggles (`NoMap`, `NoPortals`, `NoBuildCost`...).
- Custom global keys (any string not in the enum) are allowed and persisted. Prefix them (`vm_<mod>_<key>`). They are broadcast in full on every change, so keep them few and short.
- Per-player keys: `Player.AddUniqueKey/HaveUniqueKey/TryGetUniqueKeyValue` (saved in `m_uniques`).

### `ItemDrop.ItemData.m_customData`
Covered in §8. It is the standard per-item persistent store.

### Patch points
`Player.Save` postfix / `Player.Load` postfix (append nothing to the blob; use `m_customData`), `PlayerProfile.SavingStarted/SavingFinished` (static Actions), `ZNet.WorldSaveStarted/Finished`, `ZoneSystem.GlobalKeyAdd` (react to key changes locally), `Game.UpdateWorldRates` postfix (derive your own rate from a custom modifier key).

---

## 8. Item data model

### Key classes
- **`ItemDrop`** (component on item prefabs and world items). `m_itemData` is an `ItemData`. `ItemDrop.Awake` resolves `m_itemData.m_dropPrefab = ObjectDB.GetItemPrefab(prefabName)`, sets the spawn time, registers `RPC_RequestOwn`/`RPC_MakePiece`, and `Load`s from the ZDO.
- **`ItemDrop.ItemData`** (per-instance, `[Serializable]`): `m_stack`, `m_durability`, `m_quality`, `m_variant`, `m_worldLevel`, `m_pickedUp`, `m_shared` (reference), and the non-serialized runtime fields `m_crafterID`, `m_crafterName`, **`m_customData`** (`Dictionary<string,string>`), `m_gridPos`, `m_equipped`, **`m_cheated`**, `m_dropPrefab`, `m_lastAttackTime`, `m_lastProjectile`.
- **`ItemDrop.ItemData.SharedData`** (per item *type*): name/description tokens, `m_itemType`, stack/weight/value, quality and durability tuning, armor/damage/blocking, `m_attack`/`m_secondaryAttack` (`Attack` definitions: `m_requiresReload`, `m_reloadTime`, `m_reloadEitrDrain`...), `m_buildPieces` (`PieceTable`), `m_consumeStatusEffect`, `m_equipStatusEffect`, `m_setStatusEffect`, AI hints (`m_aiAttackRange`...), effects, `m_dlc`, and more.

### Rules
- **`m_shared` is meant to be one object per item type, shared by every instance.** `ObjectDB.m_itemByData` is keyed by the `SharedData` object reference, and `ObjectDB.GetItemPrefab(SharedData)`/`TryGetItemPrefab(SharedData)` are used on live inventory items (`Humanoid.UseItem` eat visual, `Player.AddTrophy` fallback). The code re-links `m_shared` to the prefab's instance **only under `Application.isEditor`** (`ItemDrop.Awake`, `Humanoid.EquipItem`), which implies that retail builds already share the reference. Community experience agrees: editing a prefab's `m_shared` affects existing items. **VERIFY at runtime once** (core test: `ReferenceEquals(invItem.m_shared, ObjectDB.GetItemPrefab(name).GetComponent<ItemDrop>().m_itemData.m_shared)`). Until then: **treat `m_shared` as per-type config, never write per-instance state into it, and assume an edit reaches every copy.**
- **`ItemData.Clone()`** is `MemberwiseClone` plus a **new dictionary copy of `m_customData`**. `m_shared` stays shared.
- Prefab lookup from an item: `item.m_dropPrefab` (fast), `ObjectDB.GetItemPrefab(item.m_shared)`, or by name hash.
- Identity for stacking: `ItemData.IsSameType` compares `m_shared.m_name` + `m_worldLevel` (+ `m_quality` if `m_maxQuality > 1`). `Inventory.FindFreeStackItem` matches name/quality/worldLevel. **Neither compares `m_customData`.** When a stackable item merges into an existing stack, the incoming item's custom data is lost. Use `m_customData` only on non-stackable items (`m_maxStackSize == 1`), or handle merging explicitly.
- Cheated flag (1.0): `Inventory.AddItem(int prefabHash, …)` marks items cheated if total damage > 10 000. The flag propagates into stacks unless `PlayerProfile.s_bypassCheatChecks`. It affects achievements only.

### Data & persistence (`ItemData.Save/Load`, version `Item.ChunksNCheats = 109`)
Durability (int ×100), grid pos, world level, a flags byte, then optional quality, stack, variant, crafter id/name, drop-prefab hash, **custom data (count + key/value strings)**, and the cheated byte. This one format is used by:
- the player inventory (`Inventory.Save` inside `Player.Save`, so the `.fch`)
- container ZDOs (`Container.Save` → `ZDOVars.s_items` byte[])
- dropped world items (`ItemDrop.SaveToZDO` → `s_itemData` byte[]; `index + "_itemData"` for item stands and armor stands)
- dropping/splitting (`ItemDrop.DropItem` clones)

**`m_customData` therefore survives equip/unequip, drop/pickup, chests, item stands, logout and world transfer.** It is the right place for per-item mod state (e.g. "crossbow is loaded"). Keep values short, because container ZDOs re-send the whole inventory on every change.

### Multiplayer authority
- The inventory owner edits (local player, or container ZDO owner after the hand-off in §5). A world item is owned by the ZDO owner. Pickup uses `RPC_RequestOwn` and then destroys it on the owner.

### Patch points
`ItemDrop.ItemData.Clone` (rarely needed), `Inventory.AddItem(ItemData)` / `FindFreeStackItem` (custom-data-aware stacking), `Humanoid.EquipItem` / `UnequipItem` (read and write custom data on equip changes), `ItemDrop.Awake` postfix (world item created), `ItemDrop.ItemData.GetTooltip` (static tooltip, see UX chapter).

---

## 9. Status effects

### Key classes
- **`StatusEffect`** (ScriptableObject). Identity is `NameHash()` = `name.GetStableHashCode()`. Main fields: `m_name` (token), `m_category`, `m_icon`, `m_ttl`, `m_attributes` (`StatusAttribute` flags), start/stop effects, messages. Lifecycle virtuals: `CanAdd`, `Setup(Character)`, `UpdateStatusEffect(dt)`, `IsDone`, `Stop`, `ResetTime`, `SetLevel(itemLevel, skillLevel)`, `OnDamaged`. Modifier virtuals (called by `SEMan.Modify*` / `Apply*`): `ModifyAttack`, `ModifyHealthRegen`, `ModifyStaminaRegen`, `ModifyEitrRegen`, `ModifyDamageMods`, `ModifyArmorMods`, `ModifyTimedBlockBonus`, `ModifyRaiseSkill`, `ModifySkillLevel`, `ModifySpeed`, `ModifyJump`, `ModifyWalkVelocity`, `ModifyFallDamage`, `ModifyNoise`, `ModifyStealth`, `ModifyMaxCarryWeight`, `ModifyRunStaminaDrain`, `ModifyJumpStaminaUsage`, `ModifyAttackStaminaUsage`, `ModifyBlockStaminaUsage`, `ModifyDodgeStaminaUsage`, `ModifySwimStaminaUsage`, `ModifySneakStaminaUsage`, `ModifyHomeItemStaminaUsage`, **`ModifyAdrenaline`**, `ModifyStagger`. Subclasses: `SE_Stats` (the data-driven workhorse), `SE_Rested`, `SE_Burning`, `SE_Poison`, `SE_Shield`, and others.
- **Registry**: `ObjectDB.m_StatusEffects`, looked up with `ObjectDB.GetStatusEffect(int)` (linear scan). Vanilla hash constants live on `SEMan` (`s_statusEffectRested`, `Wet`, `Cold`, `Burning`...).
- **`SEMan`** (one per `Character`, created in `Character.Awake`): the active list and hash set, attribute bits.

### Flow
`SEMan.AddStatusEffect(hash, resetTime, itemLevel, skillLevel, variant)`: if not the owner, it sends `RPC_AddStatusEffect` to the owner. On the owner, `Internal_AddStatusEffect` looks the hash up in `ObjectDB` and calls `AddStatusEffect(StatusEffect)`: if already active, `ResetTime`/`SetLevel`; else `CanAdd`, **`Clone()`**, `Setup`, `SetLevel`. `SEMan.Update` (from `Character.CustomFixedUpdate`, owner only) ticks, removes the ones that are done, and writes the attribute bitmask to `ZDOVars.s_seAttrib`.

### Data & persistence
Instances are clones living only on the owner. Not saved (except guardian power and food, which Player tracks separately). Other peers see only `s_seAttrib` bits and effects.

### Multiplayer authority
The character's owner. Remote peers cannot query another character's SE list. Mirror it into ZDO keys if they need to.

### Patch points
Subclass `StatusEffect`/`SE_Stats` for new effects and register them in the ObjectDB hook. `SEMan.AddStatusEffect` / `RemoveStatusEffect` for cross-cutting rules. The `Modify*` virtuals are the intended extension surface (no Harmony needed for your own SEs).

---

## 10. Localization

### Key classes
`Localization` (**assembly_guiutils**, plain C# singleton). `instance` is lazily created: `Resources.Load<LocalizationSettings>`, `SetupLanguage("English")`, then the saved or locale language. Public: `Localize(string)`, `Localize(string, params string[] words)` (`$1`-style inserts via `InsertWords`), `Localize(Transform)`, `SetLanguage`, `GetSelectedLanguage`, `GetLanguages`, `TranslateSingleId`, `SetupLanguage(language)` (loads every CSV `TextAsset` in settings via `LoadCSV`), `static Action OnLanguageChange`. **Private**: `AddWord(key, text)` (remove+add), `Clear()`, `m_translations`, `m_cache` (**LRU of 100 localized strings**).

### Flow
`Localize(text)` checks the cache, then scans for `$word` tokens (terminated by space or punctuation), then `Translate(word)`. `Translate`: `KEY_<Action>` resolves to the bound key name via `ZInput.GetBoundKeyString` (gamepad variant `Joy<Action>`). Otherwise it looks the word up in `m_translations`, and a missing key yields `[word]`. Results are cached unless they contain `MISSING KEY/BUTTON`, so a `[word]` placeholder *does* get cached.

### How mods add strings
Postfix `Localization.SetupLanguage(string language)` and call `AddWord` (reflection or publicized assembly) for that language, falling back to English. Also apply them immediately if `Localization.instance` already exists when your registration runs. Tokens must be registered **before first use**, or the `[token]` placeholder sits in the LRU until evicted. Example (our code):
```csharp
static readonly MethodInfo AddWord = AccessTools.Method(typeof(Localization), "AddWord");
[HarmonyPostfix, HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
static void AddModWords(Localization __instance, string language) {
    foreach (var kv in VmLoc.For(language)) AddWord.Invoke(__instance, new object[] { kv.Key, kv.Value }); }
```
(`VmLoc.For` is our own table lookup with English fallback. A publicized assembly avoids the reflection.) UI built from prefabs is localized via `Localization.Localize(Transform)` / `Localize` components. Call `Localization.instance.Localize(root)` on custom UI after creation.

### Multiplayer authority
Local only. Send tokens (`$vm_msg_x`) over the network, never localized text. Vanilla does this, for example `MessageHud.MessageAll(..., "$msg_worldsaved ...")`.

### Patch points
`Localization.SetupLanguage` postfix, `Localization.OnLanguageChange` (refresh cached UI strings).

---

## 11. Input & custom keybinds

### Key classes
- **`ZInput`** (**assembly_utils**) wraps the Unity **Input System** package. The game code never uses legacy `UnityEngine.Input` (only an unused post-processing sample and `InputDefinition` do). `ZInput.Initialize` runs in `FejdStartup.Awake` and again in `Game.Awake` (no-op if already created). Input updates are pumped from `Game.Update` → `ZInput.Update`.
- Named actions (`ButtonDef`) are built by private `ResetKBMButtons` / `AddGenericGamepadButtons` / `AddGamepad*Buttons` through **private** `AddButton`, stored in the private `m_buttons` dictionary. Examples: `Attack, SecondaryAttack, Block, Use, Hide, Jump, Crouch, Run, AutoRun, Sit, GP, AltPlace, Inventory, Map, BuildMenu, Remove, AutoPickup, OpenRadial, OpenEmote, Hotbar1..8, Console, Chat`, plus `Joy*` gamepad actions. Rebindable KBM bindings persist as `PlatformPrefs` `kbmBinding_<Name>` (`ZInput.Save/Load`).
- Queries: `ZInput.GetButton/GetButtonDown/GetButtonUp(name)`, `GetButtonPressedTimer`, `GetKey/GetKeyDown/GetKeyUp(KeyCode)` (maps `KeyCode` onto Input System keyboard/mouse/gamepad), `GetMouseDelta`, `GetMouseScrollWheel`, `IsGamepadActive`, `GetBoundKeyString(name)`, `GetLongPressProgress`, radial tap helpers.
- Input gating: **`Player.TakeInput()`** (protected) returns false while chat has focus or while Console, TextInput, Store, InventoryGui, Menu, TextViewer, Minimap, free-fly, barber or the build-menu search field is open, or while the player is dead, in a cutscene or teleporting. `PlayerController.TakeInput(look)` is the camera/movement equivalent.

### Custom keybinds (project convention)
- Use a BepInEx `ConfigEntry<KeyboardShortcut>` for the binding, polled via **`ZInput.GetKeyDown(shortcut.MainKey)`** plus modifier checks with `ZInput.GetKey`. `KeyboardShortcut.IsDown()` goes through BepInEx's `UnityInput`, which works on both input backends in 5.4.23, but ZInput keeps us consistent with the game's own frame state. **Always gate on the same UI conditions as `Player.TakeInput`** (expose a shared `VmInput.CanTakeInput()`), and on `Player.m_localPlayer != null`.
- Adding a real rebindable action inside the vanilla settings menu requires reflection into `ZInput.m_buttons`/`AddButton` plus settings UI work. Defer this to a UX-chapter mod.
- `$KEY_<ActionName>` in localized strings renders the bound key for **vanilla** actions only.

### Patch points
`Player.Update` postfix (input for player actions; runs every frame on all Player instances, so check `this == Player.m_localPlayer`), `PlayerController.FixedUpdate` (movement input), `ZInput.ResetKBMButtons` postfix (if we ever register real actions).

---

## 12. Console commands & devcommands

### Key classes
- **`Terminal`** (abstract MonoBehaviour; subclasses `Console` (F5) and `Chat`). The static `commands` dictionary (lowercased name → `ConsoleCommand`) is filled once by private static **`Terminal.InitTerminal`** (called from `Terminal.Awake`; guarded by `m_terminalInitialized`). It includes `AddConsoleCheatCommands`.
- **`Terminal.ConsoleCommand`**. The constructor **self-registers** (`commands[command.ToLower()] = this`), so a later registration with the same name overwrites the earlier one. Signature: `(command, description, ConsoleEvent|ConsoleEventFailable action, isCheat, isNetwork, onlyServer, isSecret, allowInDevBuild, hideBehindDevCommands, optionsFetcher, alwaysRefreshTabOptions, remoteCommand, onlyAdmin)`. `ConsoleEventFailable` may return `false` or an error string, which gets printed. `ConsoleEventArgs`: `Args`, `ArgsAll`, `FullLine`, `Context`, `TryParameterInt/Float/Long`, `HasArgumentAnywhere`.
- Execution: `Terminal.TryRunCommand` checks `ConsoleCommand.IsValid`, **`(!IsCheat || IsCheatsEnabled()) && (!IsNetwork || ZNet) && (!OnlyServer || IsServer)`**, then calls `RunAction`. A `remoteCommand` that fails locally on a client is forwarded with `ZNet.RemoteCommand`, and the server's `RPC_RemoteCommand` requires admin.
- **`Terminal.IsCheatsEnabled()` = `m_cheat && ZNet.instance.IsServer()`**. `devcommands` toggles `m_cheat` (and forwards to the server). Cheat commands therefore only run on the host or server (or remotely for admins, for commands flagged `remoteCommand`).
- **1.0 cheat accounting**: `ConsoleCommand.RunAction` refuses cheat commands until `confirmcheats` has been used (unless `Achievements.IsCheatedAtAll()`), then sets `PlayerProfile.m_usedCheats` and increments `PlayerStatType.Cheats`. The character is permanently flagged as cheated.
- Console availability: the `-console` CLI arg (`Console.SetConsoleEnabledForThisSession`) or the `EnableConsole` pref.

### Registering mod commands
Postfix `Terminal.InitTerminal` and `new Terminal.ConsoleCommand("vm_<mod>_<cmd>", "...", args => {...})`. Running after `InitTerminal` means we never get overwritten by vanilla, and names are prefixed to avoid collisions. Use **`isCheat: false`** for diagnostics (no cheat flagging), and `isCheat: true` only for real cheats. For server-side actions triggered by clients, use `remoteCommand: true` (admin-checked by the server) or our own peer RPC.

### Dev tooling built into Terminal
`test` (not a cheat, secret) toggles `Terminal.m_showTests`. `test <key> <value>` sets `Terminal.m_testList`. **`Terminal.TryTestFloat/TryTestInt/TryTest(key, default)`** read those values, and `Terminal.Increment` counts. This gives **live tuning knobs with no UI**, and our mods can read `Terminal.TryTestFloat("vm.crossbow.reload", 1f)` in dev builds. `Terminal.Log/LogWarning/LogError` echo to the console only while `m_showTests` is on.

### Patch points
`Terminal.InitTerminal` postfix, `Terminal.TryRunCommand` prefix (command aliases or permissions), `Terminal.ConsoleCommand.IsValid` (dangerous, global).

---

## 13. Asset loading: AssetBundles, SoftReferenceableAssets, prefab cloning

### Key classes (SoftReferenceableAssets assembly, 1.0)
- **`Runtime`** (static). Lazily creates the `IAssetLoader` (`AssetBundleLoader`) on first use from `valheim_Data/StreamingAssets/SoftRef/manifest` (+ `manifest_extended` if `MakeAllAssetsLoadable()`), plus any **`Runtime.AddManifest(path)`** registered **before first use** (an official extension point, but it needs a manifest in IronGate's SoftRef format, `"SoftRef manifest"` v2, built by their tooling).
- **`AssetID`** (128-bit GUID as four uint). **`SoftReference<T>`** (serializable struct): `m_assetID`, `Asset` (sync get), `Load()`, `LoadAsync(callback)`, `IsValid`, `IsLoaded`, `HoldReference()/Release()` (ref-counted), `Name`.
- **`AssetBundleLoader`** (internal): `m_bundleLoaders`, `m_assetLoaders`, `m_assetIDToLoaderIndex`. Bundles in `StreamingAssets/SoftRef/Bundles`. Scenes are also SoftRef assets (`SoftReferenceableAssets.SceneManagement.SceneManager`, used by `SystemResourceManager.FastLoadScene`).
- Where the game uses soft references: **`ZoneSystem.ZoneLocation.m_prefab`** (all locations), **`DungeonDB.RoomData.m_prefab`** (dungeon rooms; `RoomData.Hash` = `m_prefab.Name` hash), `Player.m_valkyrie`, graphics configs, `SoftReferencePrefabSpawner`. `ZNetView.HoldReferenceTo` keeps location assets alive while instances exist.
- Where it does **not**: items, pieces, creatures, projectiles, effects, status effects and recipes are **hard references** from the scene's `ObjectDB`/`ZNetScene` lists. They are always loaded, and cloning or adding them works the classic way.

### Impact on modding
- **Adding or cloning items, pieces or creatures**: unaffected by SoftRef. Load our own AssetBundle with `AssetBundle.LoadFromFile`/`LoadFromMemory` (bundles must be built with **Unity 6000.0.x**; shaders must be the game's, or the materials need re-linking to vanilla shaders at runtime), or clone vanilla prefabs.
- **Adding locations or dungeon rooms** needs an `AssetID` the loader knows. The options are: (a) `Runtime.AddManifest` with a SoftRef manifest (tooling not public); (b) inject into the internal `AssetBundleLoader` arrays via reflection (Jotunn's approach for custom locations); (c) bypass SoftRef by placing hard-referenced objects ourselves (post-generation spawning). Treat this as **hard** and centralize it in the core library if the Exploration work needs it.
- Location prefabs are only loaded around generation and placement (`ZoneSystem.m_locationPrefabs` with lifetimes, `UpdatePrefabLifetimes`). **Do not cache `SoftReference.Asset` beyond a `HoldReference/Release` pair.**

### Cloning prefabs safely (rules)
1. Create a root `GameObject` that is **inactive** and `DontDestroyOnLoad`, then `Object.Instantiate(vanilla, inactiveRoot.transform)`. Because the clone is inactive, `Awake` never runs. If it did: in the menu, `ZNetView.Awake` sees `ZDOMan.instance == null` and **destroys the ZNetView component** on the clone. In-game, it **creates a real ZDO** at the origin, which is then saved into the world.
2. Rename the clone (no spaces, no `(`) before registering. Its hash is `name.GetStableHashCode()`.
3. **Cloned item prefabs may share `m_itemData.m_shared` with the source** (see the sharing note in §8). Before editing a clone's shared data, check `ReferenceEquals(clone.m_shared, source.m_shared)`. If they are the same object, assign a fresh shallow copy first (e.g. `MemberwiseClone` via `AccessTools`), then edit the copy. Otherwise, tuning the new item silently retunes the vanilla one. Nested reference fields (`m_attack`, `m_secondaryAttack`, `EffectList`s, `m_buildPieces`) need their own copies if you change them.
4. Register in `ZNetScene` + `ObjectDB` per §2. Keep the clone referenced so `Resources.UnloadUnusedAssets` cannot collect its assets.

### Patch points
`ZoneSystem.SetupLocations` (inject `ZoneLocation`s), `DungeonDB` room lists (Exploration chapter), `Runtime.AddManifest` (plugin `Awake`, before any SoftRef use).

---

## 14. Character / Humanoid / Player / BaseAI hierarchy & update loops

### Hierarchy
- **`Character`** (`MonoBehaviour, IDestructible, Hoverable, IWaterInteractable, IMonoUpdater`): health and max health, level, faction, tamed flag, `SEMan`, movement/physics/swim/fly, stagger, pushback, damage pipeline (`Damage` → `RPC_Damage` → `ApplyDamage` → `OnDamaged` → `CheckDeath` → `OnDeath`), noise, adrenaline RPC. The events `m_onDamaged`, `m_onDeath`, `m_onLevelSet`, `m_onLand` can be used without Harmony. `Character.GetAllCharacters()`, `Character.Instances`.
- **`Humanoid : Character`**: `Inventory`, equipment slots (`m_rightItem`, `m_leftItem`, armor, utility, trinket...), `EquipItem/UnequipItem`, `StartAttack` (clones `SharedData.m_attack`), block, `VisEquipment`, reload hooks (`IsWeaponLoaded/ResetLoadedWeapon` virtual).
- **`Player : Humanoid`**: `Player.m_localPlayer`, `GetAllPlayers()`, `GetPlayer(playerID)`, input handling, placement and building, stamina/eitr/food/**adrenaline**, skills, known recipes/pieces, action queue (`MinorActionData`: Equip/Unequip/Reload), guardian power, `m_customData`, `Save/Load`.
- **`BaseAI`** (**separate component** next to the Character, `IUpdateAI`): target search, pathing, alert/aggravation RPCs (`Alert`, `OnNearProjectileHit`, `SetAggravated`), regen. **`MonsterAI : BaseAI`** (combat AI, sleep/wake RPCs, consume items; `m_onConsumedItem`). **`AnimalAI : BaseAI`** (flee). Companions: `Tameable`, `Procreation`, `Growup`, `CharacterDrop`, `ZSyncTransform`, `ZSyncAnimation`.

### Update loops
- **`MonoUpdaters.FixedUpdate`** (a single MonoBehaviour driving batched lists) runs, in order: `ZSyncTransform`, `ZSyncAnimation`, `Floating`, `Ship`, `Fish`, `CharacterAnimEvent`, then **`BaseAI.UpdateAI` at a fixed 20 Hz** (`m_updateAITimer >= 0.05`), then **`Character.CustomFixedUpdate`** (every physics tick), `Aoe`, `EffectArea`, `RandomFlyingBird`, `MeleeWeaponTrail`. `Update` covers `Smoke`, `ZSFX`, `VisEquipment`, `FootStep`, `InstanceRenderer`, `WaterTrigger`, `LightFlicker`, `SmokeSpawner`, `CraftingStation`. `LateUpdate` covers `ZSyncTransform`, `CharacterAnimEvent`, `Heightmap`, `ShipEffects`, `Tail`, `LineAttach`.
- `Character.CustomFixedUpdate`: every peer runs water, tilt, layer, visibility and look; **the owner only** runs ground contact, noise, **`SEMan.Update`**, stagger, pushback, **`UpdateMotion`**, smoke/lava/heat/ashlands water, pheromones, `SyncVelocity` and **`CheckDeath`**. `Humanoid.CustomFixedUpdate` adds, owner only, `UpdateAttack`, `UpdateEquipment` and `UpdateBlock`.
- `Player.FixedUpdate` (Unity message; **owner and local player only** past the early return): action queue, attack input, attach, doodads, crouch, dodge, cover, stations, guardian power, base value (comfort), `UpdateStats` (stamina/eitr/food/adrenaline), teleport, auto-pickup, biome, stealth.
- `Player.Update` (Unity message, all instances): `TakeInput`, hover, placement, hotbar and build input, cloth fix. `Player.LateUpdate` updates the ZNet reference position for the local player.
- `BaseAI.UpdateAI(dt)`: returns false for non-owners (reading `alert` from the ZDO). `MonsterAI.UpdateAI` / `AnimalAI.UpdateAI` extend it.

### Multiplayer authority
The ZDO owner simulates characters (the local client for its player; the area owner for creatures). Damage and SEs are routed to the owner via RPC (§5).

### Patch points
`Character.RPC_Damage` / `ApplyDamage` (post-mitigation rules, owner side), `Character.Damage` (attacker-side HitData tweaks), `Humanoid.StartAttack`, `Humanoid.EquipItem/UnequipItem`, `Player.UpdateStats`, `Player.FixedUpdate`, `BaseAI.UpdateAI` / `MonsterAI.UpdateAI` (AI rework), `Character.Awake` postfix (per-instance setup; guard `m_nview.GetZDO() != null`). These are **hot paths**, so keep patches allocation-free.

---

## 15. Dedicated server differences

The dedicated server is a **separate build** (`valheim_server.exe`, `valheim_server_Data/Managed/assembly_valheim.dll`) compiled from the same source with a server define. Verified by decompiling the local 1.0.15 server DLL:
- `ZNet.IsDedicated()` returns `true` (the client DLL returns `false`).
- `FejdStartup.Awake`: `ParseServerArguments` (`-name -port -world -password -savedir -public -logfile -crossplay -instanceid -backups -backupshort -backuplong -saveinterval -resetmodifiers -preset -modifier <k> <v> -setkey <k>`) calls `ZNet.SetServer(true, openServer: true, ...)`. The server **quits unless running headless** (`graphicsDeviceType == Null`, i.e. `-batchmode -nographics`). `FejdStartup.Start` goes straight to `LoadMainScene` (no menu, no character). Steam logs on anonymously as a game server.
- `Game.Awake` creates an **empty `PlayerProfile`** and adds `ServerLog` every 600 s. **`Game.FixedUpdate` pins `ZNet.SetReferencePosition(1e6, 0, 1e6)`**. As a result:
  - the server's own active area is empty, so **`ZNetScene` instantiates no world objects on a dedicated server**. No `Character`, `BaseAI`, `Container` or `CraftingStation` component runs there.
  - `ZDOMan.ReleaseNearbyZDOS` never assigns ownership to the server. **Everything is simulated by clients.**
  - `Player.m_localPlayer` is null. UI singletons may be absent; null-check them.
- The server still: holds and saves all ZDOs, routes RPCs, owns global keys/time/events, runs `RandEventSystem`, and **generates new zones as "ghost zones"** around peers (`ZoneSystem.CreateGhostZones`, `SpawnMode.Ghost`: objects are instantiated under `ZNetView.StartGhostInit` to create ZDOs, then destroyed). Component `Awake`s *do* run briefly during ghost generation. Server-side mod code in `Awake` patches must tolerate that.
- Consequences for mod design:
  - **Server-only mods** can only affect: connection/handshake, ZDO data directly (`ZDOMan` scans), routed RPC handlers registered on the server, global keys, world generation (ghost zones), events, saves, console/admin commands.
  - **Anything behavioral (AI, damage, crafting, building, items) must be installed on clients.** Config must be pushed from the server (§3 patterns).
  - Test both topologies: **host (listen server)** (`IsServer && !IsDedicated`, the host is also a client and owns things) and **dedicated**.
- Build target: our DLLs are the same for client and server. Guard server-only code with `ZNet.instance.IsServer()` and dedicated-only code with `IsDedicated()`. Avoid referencing UI types in code paths that execute on the server, since they are present in the assembly but not instantiated.
- Installed versions: client 1.0.16, local dedicated server 1.0.15. Both are network 40 and compatible for testing. Keep them in sync for release testing.

---

## 16. Crossplay (PlayFab)

- Backends (`OnlineBackendType`): `Steamworks` (Steam sockets / SteamID or IP), `PlayFab` (crossplay; server started with `-crossplay`), `CustomSocket`. The server registers with the matching matchmaking (`ZSteamMatchmaking.RegisterServer`, `ZPlayFabMatchmaking.RegisterServer`), advertising **game version, network version and world modifiers**, and no mod info.
- Handshake extras on PlayFab (`ZNet.RPC_PeerInfo`): `PlatformUserID` from the socket host name (e.g. `Steam_…`, `Xbox_…`), block list, crossplay privilege (`Error 10`), and asynchronous PlayFab authentication (`Error 5`). `ZPlayFabSocket` switches on zlib compression after `VersionMatch`. PlayFab traffic is relayed, so expect **higher latency and lower throughput than Steam**. Keep RPC payloads small.
- **Players on Xbox / Microsoft Store / Game Pass cannot run BepInEx mods** in practice. A server that *requires* our mods (version check) excludes them. Mods that are server-optional and client-optional stay crossplay-friendly. Tag every mod's `side` accordingly and let server operators choose strictness per mod.
- API surface is identical across backends (`ISocket`). Admin/ban lists accept platform-prefixed ids (`ZNet.ListContainsId`).

---

## 17. Logging & debug tooling

- **`ZLog`** (assembly_utils): `Log`, `LogWarning`, `LogError` prefix `DateTime.Now` and call `UnityEngine.Debug.*`. `DevLog` only logs if `Debug.isDebugBuild` (never in retail). Unity log: `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\Player.log`. The dedicated server writes to stdout or `-logfile`.
- **BepInEx** (this install): `UnityLogListening = true` (Unity/ZLog lines land in `BepInEx/LogOutput.log`), `[Logging.Disk] WriteUnityLog = true`, console window enabled. `[Harmony.Logger] LogChannels = Warn, Error`.
- Convention: each plugin uses its `BepInEx.Logging.ManualLogSource` (`Logger`), with a config-gated `Debug` level. Never log per frame. Prefix our lines through the source name (`[VM.<Mod>]`).
- In-game: `Terminal.m_testList` / `test` command, `TryTest*` knobs (§12), `Console.instance.Print`. `info` prints system info. `Game.ServerLog` prints connections and ZDO counts on the dedicated server.

---

## 18. What IronGate provides for mods in 1.0 (and useful built-in extension points)

**Official:**
- `Game.isModded` (static bool, default false) and `Game.messageForModders`, which asks mods to set it to true. Effects (verified):
  1. `FejdStartup.SetupGui` shows a "modded" label on the main menu (`m_moddedText`). Set the flag in plugin `Awake`, before the menu's `Start`.
  2. `Game.Awake` logs `isModded: <bool>` (support triage).
  3. **`Achievements.IsCheatedAtAll()` returns true when `Game.isModded`**, so `Achievements.CanGetAchievements()` is false unless the player has the `bypasscheatchecks 1` unique key. **Setting it disables achievement unlocks and the achievement-bucket stat tracking** (`PlayerProfile.IncrementStat*` only fill bucket 0). It also skips the `confirmcheats` prompt.
  4. It is **not** sent over the network and not shown in server lists.
  - No other mod API, modded-flag handshake or plugin loader exists. `grep -i modded` finds only these uses.
- `SoftReferenceableAssets.Runtime.AddManifest` / `MakeAllAssetsLoadable` (§13), with non-public tooling.

**Built-in extension points (no or minimal Harmony):**

| Extension point | Use |
|---|---|
| `LocationList` component (static `m_allLocationLists`, auto-registered in `Awake`) | Any live instance before `ZoneSystem.Start` gets its `m_locations`, `m_vegetation`, `m_environments`, `m_biomeEnvironments`, `m_clutter` and `m_events` merged by `ZoneSystem.SetupLocations`. Order by `m_sortOrder`. `AltBiomeList.m_altBiomes` adds sub-biome content similarly. |
| `ZNetView.LoadFields` + `"HasFields"` ZDO keys | Per-instance public-field overrides on any component, persisted in the ZDO. |
| `ZDO.AddSessionHash` | Sync-only ZDO keys. |
| `ZNet.m_serverSyncedPlayerData` | Client→server key/value channel. |
| `ZRoutedRpc.m_onNewPeer`, `ZDOMan.m_onZDODestroyed` | Peer join and object destruction callbacks. |
| `Game.m_playerInitialSpawn`, `ZNet.s_onZNetStart`, `ZNet.WorldSaveStarted/Finished`, `PlayerProfile.SavingStarted/Finished`, `Localization.OnLanguageChange`, `ZInput.OnInputLayoutChanged`, `GraphicsSettingsManager.GraphicsSettingsChanged` | Lifecycle events. |
| `Character.m_onDamaged/m_onDeath/m_onLevelSet/m_onLand`, `WearNTear.m_onDestroyed/m_onDamaged`, `Destructible.m_onDestroyed/m_onDamaged`, `Inventory.m_onChanged`, `ItemDrop.m_onDrop`, `Container.m_onTakeAllSuccess`, `BaseAI.m_onBecameAggravated`, `MonsterAI.m_onConsumedItem`, `HitArea.m_onHit`, `MineRock.m_onHit` | Per-instance delegates (subscribe in an `Awake` postfix). |
| `Terminal.ConsoleCommand` self-registration, `Terminal.TryTest*` | Commands and live tuning. |
| `StatusEffect` virtual `Modify*` family | Stat hooks for our own SEs. |
| Custom global keys | World-persistent flags broadcast to all. |

---

## 19. Modding pitfalls & conventions

These are rules for every mod in this repo. The shared core library (`VMods.Core`, name TBD) should make the right thing the easy thing.

### 19.1 Timing & registration
1. **Plugin `Awake` = patches + config only.** No `ObjectDB`, `ZNetScene`, `ZNet`, `Player` or `Localization.instance` access.
2. Register content in `ZNetScene.Awake` (prefix) and `ObjectDB.Awake` + `ObjectDB.CopyOtherDB` (postfix), then **re-run `ObjectDB.UpdateRegisters`**. Items not registered when `Player.Load` runs are **permanently deleted** from the character at the next save. The same goes for containers saved by a peer without the mod.
3. **Everything main-scene is recreated per session** (`ZNet`, `ZNetScene`, `ObjectDB`, `ZRoutedRpc`, `ZDOMan`, session hashes). Re-register routed RPCs and session hashes every session (`ZNet.Awake`/`Game.Start` postfix). **Prefab-asset edits persist across sessions**, so make them idempotent.
4. Console commands go in a `Terminal.InitTerminal` postfix. Localization goes in a `Localization.SetupLanguage` postfix, and must be registered before first use (LRU cache).
5. Unique names: prefab names without spaces or `(`, and a mod prefix everywhere. Duplicate names **throw inside `Awake`** (`ZNetScene.Awake`, `ObjectDB.UpdateRegisters`, `ZRoutedRpc.Register`, `ZNetView.Register`) and can break world loading for everyone.

### 19.2 Naming convention (collision-proof, hash-stable)
| Thing | Pattern | Example |
|---|---|---|
| Plugin GUID | `vmods.<category>.<mod>` | `vmods.combat.crossbowreload` |
| Prefab | `VM_<Mod>_<Name>` | `VM_Magic_RuneStone` |
| Localization token | `$vm_<mod>_<key>` | `$vm_crossbow_loaded` |
| ZDO key | `vm.<mod>.<key>`, hashed once into `static readonly int` | `vm.crossbow.loaded` |
| `m_customData` key | `vm.<mod>.<key>` | `vm.crossbow.loaded` = `"1"` |
| RPC name | `VM_<Mod>_<Action>` | `VM_Core_VersionCheck` |
| Global key | `vm_<mod>_<key>` (lowercased by the game) | `vm_magic_leyawake` |
| Console command | `vm_<mod>_<cmd>` / `vm <sub>` | `vm_core_status` |
| Status effect `.name` | `VM_SE_<Name>` | `VM_SE_Overcharge` |

Hashes are `GetStableHashCode` of these strings. **Never rename a shipped name**, because saves reference the hashes.

### 19.3 Networking & authority
1. **Write ZDOs only as owner.** Replication is whole-ZDO with revision "newer wins"; there is no merge. Non-owners send an RPC to the owner (`nview.InvokeRPC(...)`) or request ownership (container pattern).
2. Always `nview.IsValid()` before `GetZDO()`. Objects unload at any time when they leave the area.
3. Owner-gated logic (AI, damage, SEs, stations) runs **on whichever client owns the object**. Behavioral mods must be on all clients.
4. RPC payloads: allowed primitive types or `ZPackage`. Compress big data (`ZPackage.WriteCompressed`) and stay well under Steam's reliable message limit (~512 KB). The global Steam send rate is 150 KB/s, shared with ZDO sync.
5. **Routed-RPC `sender` is spoofable.** Privileged actions need a peer `ZRpc` with a socket-identity admin check.
6. Clients without our mod ignore our RPCs and keys. Design keys so **absence = vanilla behavior** (defaults on `Get*`).
7. Use `ZNet.GetTime()` for persistent timers, never `Time.time` or `DateTime.Now`.
8. Treat `ZNet.GetUID()` as session-scoped. Use `Player.GetPlayerID()` or the platform id for persistent identity.

### 19.4 Persistence
| Need | Store in | Scope / sync |
|---|---|---|
| Per item instance | `ItemData.m_customData` (non-stackable items only) | Follows the item everywhere, synced via container/world-item ZDOs |
| Per world object | ZDO key on a persistent ZNetView | World save, synced to peers in range |
| Sync-only object state | ZDO key + `AddSessionHash` (server too) | Not saved |
| Per character | `Player.m_customData` | `.fch`, local only, client-editable |
| Per character per world | `Player.m_customData` keyed by world UID, or `PlayerProfile` world data | `.fch` |
| Per world, global | Global key (small), or a ZDO on a dedicated persistent object | World save, broadcast |
| Server config | BepInEx config on server, pushed to clients on connect | Not persisted client-side |

Never append bytes to vanilla save blobs (`Player.Save`, `ItemData.Save`). Removing the mod would corrupt the load. Everything above degrades gracefully when the mod is removed, except content registration (§19.1.2).

### 19.5 Server / client split & versions
1. Declare every mod's side: **client-only** (UI, input, visuals, local QoL), **server-only** (handshake, world scans, global keys), **both-required** (new content, rule changes, RPC protocols).
2. Both-required mods must **fail loudly** on mismatch: the core version handshake (§3) rejects missing or incompatible peers with `ErrorVersion` and a readable message. The network version (40) cannot be used for this.
3. Server-authoritative config: the server sends values on `ZRoutedRpc.m_onNewPeer`/peer RPC. Clients lock local config while connected (ServerSync-style), so every owner enforces the same numbers. There is no anti-cheat model: rules are enforced by whichever client owns the object, so server-pushed config is about consistency, not security.
4. Test matrix: single-player, host + client, dedicated + 2 clients, and client-without-mod against server-with-mod (and the reverse) for every both-required mod.
5. Decide once, in core: set `Game.isModded = true`, as IronGate requests, and document that it **disables achievements** (§18).

### 19.6 Prefabs & assets
1. Clone under an **inactive** `DontDestroyOnLoad` root. An active clone in-game creates a phantom world ZDO; in the menu it loses its ZNetView.
2. AssetBundles must be built with Unity 6000.0.x. Re-link shaders to the game's shaders. Keep bundles loaded, or re-instantiate per session. Protect runtime assets from `Resources.UnloadUnusedAssets` (`Game.CollectResources`).
3. Locations and dungeon rooms are SoftRef assets (§13). Plan them as hard, core-library work.
4. Don't edit `SharedData` of a vanilla item for a per-instance effect (it changes every copy). Clone the prefab or use `m_customData`.

### 19.7 Performance hot paths
| Hot path | Frequency | Rule |
|---|---|---|
| `Character.CustomFixedUpdate`, `Humanoid.CustomFixedUpdate` | Every physics tick × every character | No allocations, no `GetComponent`, no LINQ, no logging in patches |
| `BaseAI.UpdateAI` / `MonsterAI.UpdateAI` | 20 Hz × every owned AI | Same, and cache target lookups |
| `Player.Update`/`FixedUpdate` | Every frame/tick, **all Player instances** | Early-out on `this != Player.m_localPlayer` |
| `ZNetScene.CreateDestroyObjects` | 30 Hz | Don't patch it. Awake postfixes on common prefabs must be cheap |
| `ZDO.Set/Get*` | Thousands/frame | Never patch. Precompute key hashes (`static readonly int`); the string overloads hash on every call |
| ZDO payload size | Whole ZDO re-sent on any change | Keep byte[]/string keys small. Avoid writing every frame (only on change) |
| `ObjectDB.GetStatusEffect`, `GetRecipe` | Linear scans | Cache results |
| `Localization.Localize` | Per UI refresh | Localize once, cache strings. The LRU holds only 100 entries |
| `Inventory.Changed` / `m_onChanged` | Every inventory edit | Heavy work belongs on a timer, not the callback |
| HUD/UI `Update` | Every frame | Throttle refreshes (e.g. 5–10 Hz) |

Harmony notes: prefer postfixes. Use transpilers only when a const or inlined value must change (e.g. `40u`, `ServerPlayerLimit`). Tiny methods (`ZNet.IsServer`, `ZNetView.IsOwner`, property getters) may be **inlined by the Mono JIT**, so patches on them can be silently skipped at some call sites. Patch the caller instead.

### 19.8 Worked example: where the Crossbow "stay loaded" state lives (for the Combat chapter)
Vanilla keeps the loaded state **on the player, not the item**. `Player.m_weaponLoaded` (an `ItemData` reference) is set by `Player.SetWeaponLoaded` when the queued `MinorActionData.ActionType.Reload` completes (in `Player.UpdateActionQueue`), and mirrored to `ZDOVars.s_weaponLoaded` for remote peers. `Humanoid.UnequipItem` calls `ResetLoadedWeapon`, which clears it and cancels queued reloads. `Player.UpdateWeaponLoading` queues a reload whenever `m_weaponLoaded != weapon`. `Attack.Start` requires `IsWeaponLoaded()` for `m_requiresReload` attacks, and `Attack.OnAttackTrigger` calls `ResetLoadedWeapon` when the shot fires. The QoL mod should persist a per-item flag in `ItemData.m_customData` (crossbows are non-stackable), set it when the reload completes and clear it on fire, and on equip restore `m_weaponLoaded` from the flag. That makes it a **client-only** mod: all of this runs on the owning client, and the ZDO bool keeps remote visuals right.

---

## Appendix A: key constants

| Constant | Value | Where |
|---|---|---|
| Network version | 40 (const, inlined) | `Version.c_networkVersion`, `ZNet.SendPeerInfo/RPC_PeerInfo` |
| Max players | 10 (const) | `ZNet.ServerPlayerLimit` |
| Zone size | 64 m | `ZoneSystem.m_zoneSize` |
| ZDO sector grid | 512 × 512 zones | `ZNet.Awake` → `new ZDOMan(512)` |
| ZDO send cadence / budget | 50 ms per peer round, 10 240 B queue budget | `ZDOMan.SendZDOToPeers2`, `SendZDOs` |
| Ownership release/claim | every 2 s (server) | `ZDOMan.ReleaseZDOS` |
| Object create/destroy tick | 30 Hz, 10 objs/tick (100 while loading) | `ZNetScene.Update/CreateObjects` |
| AI tick | 20 Hz | `MonoUpdaters.FixedUpdate` |
| Periodic net data | 2 s | `ZNet.SendPeriodicData` |
| RPC timeout | 30 s (90 s long) | `ZRpc.m_timeout`, `SetLongTimeout` |
| Steam send rate | 153 600 B/s | `ZSteamSocket.RegisterGlobalCallbacks` |
| Autosave | 1800 s (+30 s warning) | `Game.m_saveInterval`, `Game.UpdateSaving` |
| Unused-asset GC | ≥20 min / hourly | `Game.CollectResourcesCheck*` |
| Save versions | Player 46, World 41, Item 109, PlayerData 33 | `Version` |
| Localization cache | 100 entries (LRU) | `Localization.m_cache` |
| Dedicated server ref pos | (1e6, 0, 1e6) | server `Game.FixedUpdate` |

## Appendix B: file locations (Windows, local saves)
- Characters: `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\characters_local\<name>.fch` (`SaveSystem.GetCharacterFolderPath`)
- Worlds (1.0 chunked): `...\worlds_local\<worldName>\` (`World.GetSaveDirectory`). Legacy: `...\worlds_local\<world>.db/.fwl`
- Admin/ban/permit lists: `...\adminlist.txt`, `bannedlist.txt`, `permittedlist.txt` (`ZNet.Awake`, server)
- Unity log: `...\Player.log`. BepInEx log: `<game>\BepInEx\LogOutput.log`
- SoftRef bundles: `<game>\valheim_Data\StreamingAssets\SoftRef\{manifest,manifest_extended,Bundles}`
- Dedicated server: `E:\SteamLibrary\steamapps\common\Valheim dedicated server\` (`valheim_server_Data\Managed\` for its DLLs)
