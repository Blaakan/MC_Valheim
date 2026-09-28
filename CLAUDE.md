# ValheimMods — working rules

Mono-repo of Valheim 1.0 mods (BepInEx 5 + HarmonyX) published by **MC**. Goal: QoL, revamps and new
features that build on the vanilla experience. The idea backlog is `docs/backlog.md` (source: the user's
Google Sheet, CSV export: https://docs.google.com/spreadsheets/d/1nd_oWjYyphCcjt5F0SrstpkMK_UzDdKwj0U1UC3FHG0/export?format=csv).

## House rules (whole folder)

- **Commit messages and code comments are written in caveman speech.** Applies to C#, PowerShell, MSBuild/XML
  comments and XML doc comments. Examples: `// Me keep bolt in crossbow when put away.` /
  commit `Crossbow keep bolt now. Me put flag on item.`
- **User-facing text stays normal, clear English:** mod README/CHANGELOG, Thunderstore descriptions, BepInEx
  config descriptions, in-game strings, and everything under `docs/`.
- **Never credit Claude in commits** (no `Co-Authored-By`, no "Generated with" lines).
- Commit locally at each milestone. There is no remote; never push.
- **Never commit game code.** Decompiled source lives in `.ref/` (git-ignored). In docs, cite `Class.Method`
  instead of pasting game code (at most ~5 lines when truly essential).

## Layout

```
Directory.Build.props     shared build rules, game refs, mod identity from project name
Directory.Build.targets   metadata checks, ModInfo.g.cs generation, deploy to game
Local.props               machine-only (ValheimDir); written by tools/Setup.ps1; git-ignored
ValheimMods.slnx          solution (solution folders = categories)
src/Shared/               tiny helpers compiled into every mod as internal code (Log, ItemDataExtensions)
src/<Category>/<System>.<Feature>/   one mod = one project = one DLL = one Thunderstore package
templates/Mod/            template used by tools/New-Mod.ps1
tools/                    PowerShell workflow scripts (see Commands)
docs/game/                game-systems knowledge base per category + core-engine.md (read before designing)
docs/research/            existing-mod surveys (inspiration)
docs/modding/             toolchain research and notes
docs/design/              one design doc per mod, incl. manual test checklist
.ref/decompiled/          decompiled game source (git-ignored, nested local git = one commit per game version)
dist/                     packaged zips (git-ignored)
```

## Naming

- GUID = `MC.<Category>.<System>.<Feature>` (e.g. `MC.Combat.Crossbow.StaysLoaded`). It is also the assembly
  name, root namespace, csproj file name, deploy folder and config file name. **Never change it after release.**
- Folder: `src/<Category>/<System>.<Feature>/`.
- Categories: Combat, Exploration, Farming, Cooking, Building, Crafting, UX (+ Core for shared runtime/framework
  mods). Categories are soft; pick the main one. Scope: `QoL` | `Revamp` | `New`. Side: `Client` (only the user
  needs it) | `Server` (host/server needs it) | `Both` (everyone needs it).
- Thunderstore package name is derived by removing dots (`CrossbowStaysLoaded`); override with `<ModPackageName>`.

## Commands (PowerShell, from repo root)

| Task | Command |
|---|---|
| First-time setup | `./tools/Setup.ps1 [-DevBepInExConfig]` |
| New mod | `./tools/New-Mod.ps1 -Category Combat -Feature Crossbow.StaysLoaded -Name '...' -Scope QoL -Side Client -Description '...'` |
| Build all (Debug deploys to `<Valheim>/BepInEx/plugins/<GUID>/`) | `dotnet build ValheimMods.slnx` |
| Smoke test (launch game, check mods load + patch cleanly, close) | `./tools/Test-Smoke.ps1 [-Mod Crossbow] [-KeepRunning]` |
| Launch game | `./tools/Start-Game.ps1 [-Vanilla] [-DebugMono [-Suspend]]` |
| Follow log | `./tools/Watch-Log.ps1 -Mine` |
| Package for Thunderstore + Nexus | `./tools/Package-Mod.ps1 -Mod Crossbow` → `dist/` |
| After a Valheim update | `./tools/Update-GameRefs.ps1` (re-decompiles, lists changed classes that we patch) |
| List mods | `./tools/Get-Mods.ps1 \| Format-Table` |

If the game is running, deploy fails with a "file locked" warning: close the game first.
There are no unit tests: code runs inside Unity. Verification = build + `Test-Smoke.ps1` + the manual checklist
in the mod's design doc.

## Game code reference

- `.ref/decompiled/assembly_valheim/<Class>.cs` (global namespace; nested types live in the parent's file, e.g.
  `ItemDrop.ItemData` is in `ItemDrop.cs`). Game version stamp: `.ref/game-version.json`.
- Start from `docs/game/<chapter>.md`, then confirm in `.ref`. Valheim changed a lot through 1.0 (Ashlands,
  Bog Witch, Deep North, trinkets, adrenaline…): never trust memory of older versions; verify every name.

## Mod code conventions

- `Plugin.cs`: `[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]`. `ModInfo` is generated from the
  csproj; never hardcode GUID/name/version. Client-only mods add `[BepInProcess("valheim.exe")]`.
- `Awake`: `Log.Init(Logger)` → config binds → `Harmony.CreateAndPatchAll(assembly, ModInfo.Guid)` →
  `Log.Ready(ModInfo.Guid, ModInfo.Version)` (the smoke test waits for this marker).
- Patches go in `Patches/<GameClass>Patches.cs`, using `[HarmonyPatch(typeof(X), nameof(X.Method))]`.
  `assembly_valheim` is publicized, so `nameof` works on private members and typos fail at compile time.
  Give argument types for overloaded methods.
- Prefer Prefix/Postfix. Use a transpiler only when unavoidable, and write down why (they break on game updates).
- Patches must not throw into game code; guard nulls; respect the `Enabled` config; keep per-frame patches cheap.
- Multiplayer: know who runs the code — `Player.m_localPlayer`, `m_nview.IsOwner()`, `ZNet.instance.IsServer()`.
  Client-side mods only change the local player's own state.
- Per-item state: `ItemData.m_customData` through `MC.Shared.ItemDataExtensions`, key `"{ModInfo.Guid}.<Name>"`.
- Config: BepInEx `ConfigEntry`; section `General` with `Enabled` first; descriptions are user-facing English.
- `src/Shared` stays tiny and generic. Features that several mods must share at runtime go in a `Core` mod that
  others reference with `[BepInDependency]`.
- Every mod has: `README.md` (features, config table, multiplayer notes), `CHANGELOG.md` (`## x.y.z` per
  version), `icon.png` 256×256, and `docs/design/<category>-<feature>.md` with a manual test checklist.
- Release: bump `<Version>` in the csproj + add a CHANGELOG entry, then `Package-Mod.ps1`.
