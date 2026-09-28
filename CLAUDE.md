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
src/Shared/               compiled into every mod as internal code: Log, JitCheck, ItemDataExtensions
src/Shared/Framework/     mod framework: ModPlugin life cycle, FeatureRegistry, NetworkGate, FeaturePanel (see docs/modding/framework.md)
src/Shared/TESTING.md     in-game tests for the framework
src/<Category>/<System>.<Feature>/   one mod = one project = one DLL = one Thunderstore package (+ TESTING.md)
templates/Mod/            template used by tools/New-Mod.ps1
tests/Probes/             throwaway probe mods for tools/Test-Framework.ps1 (never shipped)
tools/                    PowerShell workflow scripts (see Commands)
docs/backlog.md           GENERATED idea backlog (tools/Update-Backlog.ps1): feasibility, who needs it, existing mods
docs/game/                game-systems knowledge base per category + core-engine.md (read before designing)
docs/research/            existing-mod surveys + idea-research.json (data behind the backlog)
docs/modding/             framework.md (how toggles/deps/multiplayer work), toolchain-research.md
docs/publishing/          nexus.md: how to publish (page fields, AI tags, pack rules, Vortex behaviour)
docs/testing/             cross-cutting test lists (publishing.md: install tests for the Nexus zips)
packaging/nexus/          pack.json, PACK_CHANGELOG.md, install.md (shared install text), pages.json (page URLs)
docs/design/              one design doc per mod (decisions, edge cases, open questions)
.ref/decompiled/          decompiled game source (git-ignored, nested local git = one commit per game version)
dist/                     packaged zips (git-ignored)
```

## Naming

- GUID = `MC.<Category>.<System>.<Feature>` (e.g. `MC.Combat.Crossbow.StaysLoaded`). It is also the assembly
  name, csproj file name, deploy folder and config file name. **Never change it after release.**
- Root namespace is derived, not the GUID: `MC.<Category>.<SystemFeature>Mod` (e.g. `MC.Combat.CrossbowStaysLoadedMod`),
  so a `<System>` like `Inventory` never hides the game class `Inventory`.
- Folder: `src/<Category>/<System>.<Feature>/`.
- Install location (dev deploy, Nexus zips, all-in-one bundle): `BepInEx/plugins/MC_Valheim/<Category>/<GUID>/`
  (`ModCollectionFolder` in Directory.Build.props). BepInEx scans `plugins/` recursively. Thunderstore installs go
  where the mod manager puts them (`plugins/MC-<Package>/`). Config files stay flat in `BepInEx/config/<GUID>.cfg`
  (where mod managers and ConfigurationManager expect them). Mod code finds its own files with `ModFolder`, never a
  hardcoded path.
- Categories: Combat, Exploration, Farming, Cooking, Building, Crafting, UX (+ Core for shared runtime/framework
  mods). Categories are soft; pick the main one. Scope: `QoL` | `Revamp` | `New`.
- Thunderstore package name is derived by removing dots (`CrossbowStaysLoaded`); override with `<ModPackageName>`.

## Commands (PowerShell, from repo root)

| Task | Command |
|---|---|
| First-time setup | `./tools/Setup.ps1 [-DevBepInExConfig]` |
| New mod | `./tools/New-Mod.ps1 -Category Combat -Feature Crossbow.StaysLoaded -Name '...' -Scope QoL -Side Client [-Multiplayer Compatible] [-MultiplayerNotes '...'] [-Requires MC.X.Y] -Description '...'` |
| Build all (Debug deploys to `<Valheim>/BepInEx/plugins/MC_Valheim/<Category>/<GUID>/`) | `dotnet build ValheimMods.slnx` |
| Smoke test (launch game, check mods load + patch cleanly, close) | `./tools/Test-Smoke.ps1 [-Mod Crossbow] [-KeepRunning]` |
| Framework test (probe mods: live toggle, config watch, dependency gating) | `./tools/Test-Framework.ps1` |
| **In-game test to-do list** | `./tools/Get-TestTodo.ps1 [-Mod X] [-All]` |
| Regenerate the idea backlog from the sheet | `./tools/Update-Backlog.ps1` |
| Launch game | `./tools/Start-Game.ps1 [-Vanilla] [-DebugMono [-Suspend]]` |
| Follow log | `./tools/Watch-Log.ps1 -Mine` |
| Nexus release files (zip + BBCode description + page sheet + banner, per mod and the all-mods pack) | `./tools/Package-Mod.ps1 [-Mod X] [-Release [-AllowPending]]` → `dist/nexus/` |
| After a Valheim update | `./tools/Update-GameRefs.ps1` (re-decompiles, lists changed classes that we patch) |
| List mods | `./tools/Get-Mods.ps1 \| Format-Table` |

If the game is running, deploy fails with a "file locked" warning: close the game first. **While the user is
testing in-game, build with `-p:DeployToGame=false`** so the DLL under test doesn't change under them.
Tool scripts must stay ASCII-only (Windows PowerShell 5.1 reads BOM-less scripts as ANSI); build non-ASCII
characters at runtime (`[char]0x2014`).

## Testing workflow

There are no unit tests: code runs inside Unity. Verification = build + `Test-Smoke.ps1` (+ `Test-Framework.ps1`
when `src/Shared` changes) + in-game tests.

- Every mod has `TESTING.md` next to its code (framework: `src/Shared/TESTING.md`): numbered checkboxes
  `[ ]` to test, `[x]` passed, `[!]` failed + note, `[-]` skipped, split single-player / multiplayer, and a
  "build under test" line (build id = git hash from the `[MC:ready]` log line / MC Mods panel).
- **When the user asks what to test: run `./tools/Get-TestTodo.ps1` and give them that list.** When they report
  results, tick the boxes (keep IDs stable). When code changes a behaviour, reset its tests to `[ ]`.
- New behaviour → new test items in the same change. Multiplayer hand-off cases (items/structures reaching a
  player without the mod) always get a test.

## Game code reference

- `.ref/decompiled/assembly_valheim/<Class>.cs` (global namespace; nested types live in the parent's file, e.g.
  `ItemDrop.ItemData` is in `ItemDrop.cs`). Game version stamp: `.ref/game-version.json`.
- Start from `docs/game/<chapter>.md`, then confirm in `.ref`. Valheim changed a lot through 1.0 (Ashlands,
  Bog Witch, Deep North, trinkets, adrenaline…): never trust memory of older versions; verify every name.

## Mod code conventions

- `Plugin.cs`: `internal sealed partial class Plugin : ModPlugin`, overriding only `BindConfig`, `OnActivated`,
  `OnDeactivated` (never `Awake`/`Start`/`Update`/`OnDestroy`). The build generates `ModInfo` and the
  `BepInPlugin` / `BepInProcess` (Client mods) / soft `BepInDependency` attributes from the csproj. Never
  hardcode GUID/name/version.
- csproj metadata (single source of truth, validated by the build): `ModName`, `Version`, `ModScope`, `ModIdea`
  (sheet idea name), `ModSide` (`Client` | `Server` | `Both`), `ModMultiplayer` (`Compatible` | `Limited` |
  `SinglePlayer`), `ModMultiplayerNotes` (player-facing; required unless Client + Compatible), `ModRequires`
  (GUIDs of MC mods needed at runtime, `;`-separated, no cycles), `ModDependencies` (external Thunderstore strings),
  `ModNetworkVersion` (Both mods: bump when RPC names/payloads or ZDO keys/formats change), `ModDescription`.
- **Client-side and multiplayer-compatible whenever possible.** If the server must have it, or it can't work in
  multiplayer, say why in `ModMultiplayerNotes`. Think through hand-offs: an item/structure touched by the mod
  reaching a player without it must behave sanely.
- Features toggle live: patches are applied only while the feature is Active (enabled + deps active + server ok),
  so patch code doesn't check `Enabled`. Dependents of a disabled/missing mod go inactive and show why; they never
  crash (only touch another mod's types from patches/`OnActivated`).
- Patches go in `Patches/<GameClass>Patches.cs`, using `[HarmonyPatch(typeof(X), nameof(X.Method))]`.
  `assembly_valheim` is publicized, so `nameof` works on private members and typos fail at compile time.
  Give argument types for overloaded methods.
- Prefer Prefix/Postfix. Use a transpiler only when unavoidable, and write down why (they break on game updates).
- Patch bodies catch their own exceptions and call `PatchGuard.Report(site, e)`; never let one reach game code.
  Keep per-frame patches cheap (reference checks first, no allocations). Unity null semantics: never use `?.`/`??`
  on `UnityEngine.Object` (use `== null`).
- Multiplayer: know who runs the code — `Player.m_localPlayer`, `m_nview.IsOwner()`, `ZNet.instance.IsServer()`.
  Client-side mods only change the local player's own state.
- Per-item state: `ItemData.m_customData` through `MC.Shared.ItemDataExtensions`, key `"{ModInfo.Guid}.<Name>"`.
- Config: BepInEx `ConfigEntry`; section `General` with `Enabled` first; descriptions are user-facing English.
- `src/Shared` stays tiny and generic. Features that several mods must share at runtime go in a `Core` mod that
  others reference with `[BepInDependency]`.
- Every mod has: `README.md` (features, config table, multiplayer section), `CHANGELOG.md` (`## x.y.z` per
  version), `icon.png` 256×256, `TESTING.md`, and `docs/design/<category>-<feature>.md`.
- **Publishing target: Nexus Mods only (for now)**: each mod as its own page + one "all mods" pack page
  (`packaging/nexus/`: `pack.json`, `PACK_CHANGELOG.md`, shared `install.md`, `pages.json` with page URLs).
  Thunderstore packaging still exists behind `Package-Mod.ps1 -Thunderstore`.
- Release flow: all `TESTING.md` items pass (or release with `-AllowPending`, which lists the untested ones on the
  page sheet); bump `<Version>` + CHANGELOG entry (pack: bump `pack.json` version + PACK_CHANGELOG); commit;
  `./tools/Package-Mod.ps1 -Release` (refuses a dirty tree, an already-released version, failed tests; tags
  `nexus/<GUID>/v<ver>` and `nexus/pack/v<ver>` locally); then follow each `dist/nexus/.../nexus-page.md`
  (step-by-step guide: `docs/publishing/nexus.md`). After a first upload, save the page URL in `packaging/nexus/pages.json`.
  **Nexus AI tags for every page: "AI Assisted" + "AI Media"** (AI Assisted needs visible evidence of human-led development on the page), and untested AI mods count as spam: tag every
  release and never publish without the in-game tests.
