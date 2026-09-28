# The MC mod framework

Every MC mod is built on a small shared framework (`src/Shared/Framework`). It is compiled into each mod, so there is
no extra "core" download and no mod depends on another one just to work. It gives every mod the same behaviour:

- **each feature can be turned on or off on its own, live, without restarting;**
- **a feature whose dependency is missing or turned off goes inactive, says why, and never crashes;**
- **a feature that needs the server switches itself off on servers that don't have it;**
- **everything is visible in one place: the in-game _MC Mods_ panel.**

## Turning features on and off

Three equivalent ways, all writing to the same file, `BepInEx/config/MC.<Category>.<System>.<Feature>.cfg`:

| Where | How |
|---|---|
| In game | Main menu or pause menu (Esc): click **MC Mods** (top-right). Tick or untick a feature. |
| Mod manager | r2modman / Thunderstore Mod Manager → Config editor → the mod's file → `General.Enabled`. |
| By hand | Edit `Enabled = true/false` in the mod's `.cfg`. The game picks the change up while running. |

ConfigurationManager (F1, shudnal's build for Valheim 1.0) also shows every setting, including `Enabled` and the
read-only `Status`.

Uninstalling or disabling a mod in the mod manager (r2modman, Thunderstore Mod Manager, Vortex) also works: every mod
is a separate package, so mod managers can enable/disable each one individually. Players who install the whole
collection (Thunderstore modpack or the Nexus all-in-one zip) get all mods, all on by default, and pick in the panel.

A turned-off feature has **all of its patches removed**: it behaves exactly as if it was not installed.

## Status

Each mod keeps a read-only `Status` line in its config file and in the panel:

| Status | Meaning |
|---|---|
| Active | Running. |
| Off (disabled in settings) | You turned it off. |
| Inactive: needs X, which is not installed | A required MC mod is missing or failed to load. |
| Inactive: needs X, which is turned off | A required MC mod is installed but off. The panel offers a **Turn on X** button. |
| Inactive: the server does not have this mod | The feature needs the server to have it too. |
| Inactive: the server has this mod turned off | The server has it, but it is off (or broken) there. Follows the server live. |
| Inactive: the server has version A, you have B... | Client and server network versions differ (`ModNetworkVersion`). |
| Runs on the server/host only... | A server-side feature while you are a client of someone else's server. Informational (grey), not a problem. |
| Inactive: single-player only | A single-player feature while other players are connected. |
| Error: could not start | Patching failed (usually a game update changed something). Details in `BepInEx/LogOutput.log`. The feature stays off until you toggle it. |

When you spawn in a world, one short message lists features that are inactive for a reason you did not choose
(shown again only when that list changes, and not while the HUD is hidden).

## Dependencies

A mod declares the MC mods it needs in its csproj (`<ModRequires>MC.Core.Magic</ModRequires>`). The build turns that
into a *soft* BepInEx dependency (load order only) and a Thunderstore dependency (mod managers install it too).

At runtime, the dependent mod is active only while every dependency is installed **and active**. Turning a dependency
off never changes the dependents' own `Enabled` setting: they simply become inactive, show why, and come back on by
themselves as soon as the dependency is back. Chains work too (C off → B inactive → A inactive).

Rules for mod code that uses another mod's API: only touch the other mod's types from code that runs while the feature
is active (patches, `OnActivated`), never from `BindConfig` or static constructors. Then a missing dependency can
never cause a crash.

## Who needs to install it, and multiplayer

Every mod declares two things in its csproj; the build refuses to compile if they are inconsistent:

| Property | Values | Rule |
|---|---|---|
| `ModSide` | `Client` · `Server` · `Both` | **Prefer `Client`.** Only use `Server`/`Both` when the feature cannot work otherwise, and explain why in `ModMultiplayerNotes`. |
| `ModMultiplayer` | `Compatible` · `Limited` · `SinglePlayer` | **Prefer `Compatible`.** `Limited` and `SinglePlayer` require `ModMultiplayerNotes`. |

What the framework does with them:

- **Client**: works everywhere, including vanilla servers and with vanilla friends. Loaded only by the game client.
- **Server**: active only on the host or dedicated server; inactive (with status) on a client of a remote server.
- **Both**: when you join a server, the mod asks the server whether it has the mod too (a private message that
  vanilla servers ignore). The server answers with its real state: "on", or "off" if the feature is turned off or
  broken there, and it tells connected players again whenever that changes. No answer before the server finishes the
  handshake → the feature stays off for that session. While the answer is pending the feature stays on, so load-time
  hooks run on clients exactly like on the host (players only spawn after the handshake). The host and single-player
  always count as having it. The host's log names players who joined without the mod, and mod code can ask
  `NetworkGate.PeerHasMod(peer)`.
- **SinglePlayer**: switches itself off while other players are fully connected (players still logging in or being
  rejected don't count).

Both-side mods also declare `<ModNetworkVersion>` (default 1). Bump it whenever RPC names or payloads, or ZDO keys
or formats, change: client and server must have the same network version, whatever their release versions.

`ModMultiplayerNotes` is shown to players in the README and the panel. Use it to explain hand-off cases too (what
happens when an item or structure touched by the mod reaches a player who doesn't have it).

## Testing

Each mod has a `TESTING.md` checklist (single-player and multiplayer). The framework itself has
`src/Shared/TESTING.md`. `./tools/Get-TestTodo.ps1` lists everything still to test. Automated checks:

- `./tools/Test-Smoke.ps1`: every mod loads, patches cleanly, and compiles against the current game (JIT check).
- `./tools/Test-Framework.ps1`: probe mods check live toggling, config-file watching, dependency gating and recovery.

## Internals (for mod authors)

- `ModPlugin` (base class of every `Plugin`): owns `Enabled`, `Status`, the Harmony instance and the life cycle.
  Mods override `BindConfig`, `OnActivated`, `OnDeactivated` (and optionally `ApplyPatches`). They must not define
  `Awake`, `Start`, `Update` or `OnDestroy`.
- Generated per mod from the csproj: `ModInfo` (GUID, name, version, build id, side, multiplayer, requires) and the
  `BepInPlugin` / `BepInProcess` / `BepInDependency` attributes on the partial `Plugin` class.
- `FeatureRegistry`: shared list of all MC mods, stored in an AppDomain slot using only BCL/BepInEx types, because
  every mod carries its own copy of the framework types. Keys are never renamed; the highest framework version is the
  "leader" that draws the panel.
- `NetworkGate`: per-mod connection handshake (`<GUID>.Hello` / `<GUID>.HelloAck`) and world/peer events.
- `PatchGuard`: every patch body catches its own exceptions and reports once, so a mod bug can never break a game
  method halfway.
- `JitCheck` (Debug builds): JIT-compiles every method at startup (`MethodHandle.GetFunctionPointer`; note that
  `RuntimeHelpers.PrepareMethod` is an empty stub in Unity's Mono) so runtime-only binding failures (stale DLL after a
  game update, APIs present in .NET Framework but missing in Unity's Mono, missing assemblies) show up in the smoke
  test instead of mid-game. The framework test proves it works with a deliberately missing DLL.
- Mod namespace is not the GUID: `MC.<Category>.<Feature without dots>Mod` (e.g. `MC.Combat.CrossbowStaysLoadedMod`),
  because a namespace segment like `Inventory` would hide the game class of the same name.
- The panel blocks clicks to the game's buttons behind it with an invisible top-most canvas while it is open.
- Install layout: direct/Nexus installs put every mod in `BepInEx/plugins/MC_Valheim/<Category>/<GUID>/`; mod
  managers use their own `plugins/MC-<Package>/` folders. BepInEx loads both (it scans `plugins/` recursively).
  Mod code must find its own files through `ModFolder` (the DLL's folder), never a fixed path.
