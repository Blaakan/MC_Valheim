# ValheimMods — working rules

Mono-repo of Valheim 1.0 mods (BepInEx 5 + HarmonyX) published by **MC**. Goal: QoL, revamps and new
features that build on the vanilla experience. The idea backlog is `docs/backlog.md` (source: the user's
Google Sheet, CSV export: https://docs.google.com/spreadsheets/d/1nd_oWjYyphCcjt5F0SrstpkMK_UzDdKwj0U1UC3FHG0/export?format=csv).
Sheet columns: CATEGORY, NAME, DESCRIPTION, SCOPE, EXISTS ALREADY, Status (dropdown from the sheet's `data` tab:
`Idea` | `Implemented` | `Cancelled`; a mod in `src/` = `Implemented`). **The user maintains the sheet: never edit it.**
`./tools/Update-Backlog.ps1` (online) lists the sheet rows that changed since the research (`idea-research.json`
keeps a verbatim copy of each row; the online run copies Status changes into it; changed rows need a research pass)
and the sheet cells the repo contradicts (Status vs the mods in `src/`, EXISTS ALREADY vs the research coverage; when
the user keeps a flagged EXISTS ALREADY value, record it as `existsConfirmed` in that idea's research entry).
Whenever a task changes what the sheet should say (a mod implements or drops an idea, research contradicts a cell, an
idea overlaps or merges with another), end it with the list of sheet cells for the user to change.

## House rules (whole folder)

- **Commit messages and code comments are written in caveman speech.** Applies to C#, PowerShell, MSBuild/XML
  comments and XML doc comments. Examples: `// Me keep bolt in crossbow when put away.` /
  commit `Crossbow keep bolt now. Me put flag on item.`
- **User-facing text stays normal, clear English:** mod README/CHANGELOG, Thunderstore descriptions, BepInEx
  config descriptions, in-game strings, and everything under `docs/`.
- **Never credit Claude in commits** (no `Co-Authored-By`, no "Generated with" lines).
- Commit locally at each milestone. Remote `origin` = https://github.com/Blaakan/MC_Valheim (GitHub): push only when the user asks.
  Exception: the backlog mod workflow (below) commits nothing before its pause; the user's OK there covers the
  commit, the push and the test issue.
- **Never commit game code.** Decompiled source lives in `.ref/` (git-ignored). In docs, cite `Class.Method`
  instead of pasting game code (at most ~5 lines when truly essential).

## Layout

```
Directory.Build.props     shared build rules, game refs, mod identity from project name
Directory.Build.targets   metadata checks, ModInfo.g.cs generation, deploy to game
Local.props               machine-only (ValheimDir); written by tools/Setup.ps1; git-ignored
ValheimMods.slnx          solution (solution folders = categories)
src/Shared/               compiled into every mod as internal code: Log, JitCheck, ItemDataExtensions, ItemKinds
src/Shared/Framework/     mod framework: ModPlugin life cycle, FeatureRegistry, NetworkGate, FeaturePanel (see docs/modding/framework.md)
src/Shared/TESTING.md     in-game tests for the framework
src/<Category>/<System>.<Feature>/   one mod = one project = one DLL = one Thunderstore package (+ TESTING.md)
templates/Mod/            template used by tools/New-Mod.ps1
tests/Probes/             throwaway probe mods for tools/Test-Framework.ps1 and tools/Test-InWorld.ps1 (never shipped)
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
| In-world self-tests (throwaway world "MCProbe", your saves untouched, mod configs put back after; runs every deployed mod's `SelfTest`, screenshots in `%TEMP%\MC_Valheim_InWorld`) | `./tools/Test-InWorld.ps1 [-Mod Sleep.ThroughDay] [-Only sleep.] [-KeepRunning] [-NoBuild]` |
| **In-game test to-do list** | `./tools/Get-TestTodo.ps1 [-Mod X] [-All]` |
| Tooling regression tests (after changing tools/) | `./tools/Test-Tools.ps1` |
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
when `src/Shared` changes) (+ `Test-InWorld.ps1` when a mod has self-tests) + in-game tests.

- In-world self-tests (Debug builds): a mod registers coroutines with `SelfTest.Register` (`src/Shared/SelfTest.cs`);
  `./tools/Test-InWorld.ps1` runs them in a throwaway world with isolated saves (how to write one:
  `docs/modding/framework.md`, "In-world self-tests"). Like the smoke test it launches the game (close it first)
  and takes ~1-2 min plus the tests. Every deployed mod's tests run unless `-Only` filters them; the run fails when
  no mod test ran, or when `-Mod` / `-Only` picked tests that did not run (it says why). It puts
  `BepInEx/config/MC.*.cfg` back as before the run, also after Ctrl+C or a timeout.

- Every mod has `TESTING.md` next to its code (framework: `src/Shared/TESTING.md`): numbered checkboxes
  `[ ]` to test, `[x]` passed, `[!]` failed + note, `[-]` skipped, split single-player / multiplayer, and a
  "build under test" line (build id = git hash from the `[MC:ready]` log line / MC Mods panel).
- **When the user asks what to test: run `./tools/Get-TestTodo.ps1` and give them that list.** When they report
  results, tick the boxes (keep IDs stable). When code changes a behaviour, reset its tests to `[ ]`.
- New behaviour → new test items in the same change. Multiplayer hand-off cases (items/structures reaching a
  player without the mod) always get a test.
- A mod's pending tests can also be mirrored in a GitHub issue (backlog mod workflow, step 8). `TESTING.md` stays
  the source of truth; when ticking it, tick the issue too.

## Backlog mod workflow (implement an idea end to end)

Use it when the user assigns a backlog idea, usually "implement <idea> from the backlog" plus a list of expected
behaviours. One pause: **nothing is committed, pushed or posted until the user says OK**, and that one OK covers the
commit, the push and the test issue (steps 7-8). Work on `main` (solo repo: no feature branch, no pull request).

1. **Understand.** First run `git status` and `git log --oneline origin/main..HEAD`: if the tree is dirty or commits
   are waiting to be pushed, tell the user and ask whether they belong to this run. Read the idea's section in
   `docs/backlog.md` and `docs/research/existing-mods-*.md`, its notes in `docs/game/<chapter>.md`, the `.ref` code
   of every method involved (callers, guards, messages), and the MC mods that patch the same methods or touch the same
   items/state. Look at how existing mods hook it (GitHub source, Thunderstore "source" pages) and for compatibility
   traps (other mods patching the same methods). Number the expected behaviours: they become the design doc's Goal
   and each one gets at least one test; they are the scope (backlog extras only if small, listed as "added beyond the
   request" at the pause). Ask only when blocked. If a behaviour conflicts with a vanilla rule, pick the
   vanilla-consistent option, write it under Decisions in the design doc, and flag it at the pause.
2. **Scaffold.** Pick `<System>.<Feature>` (the GUID is permanent), run `./tools/New-Mod.ps1`, add `<ModIdea>` with
   the exact sheet idea name to the csproj (several ideas `;`-separated).
3. **Implement** per "Mod code conventions". Prefer calling the vanilla method in a loop or wrapper over copying its
   logic: other mods' patches on it keep working. `dotnet build ValheimMods.slnx`, then
   `./tools/Test-Smoke.ps1 -Mod <Feature>` (one word of `<System>.<Feature>`, e.g. `Repair`: `-Mod` matches the
   project name in Test-Smoke and the `TESTING.md` path in Get-TestTodo as a substring, so when the word is in another
   mod's name too, pass `<System>.<Feature>`: `Sort` also matches `Crafting.SearchSort`, use `Container.Sort`). The
   game must be closed (if it is running, ask the user); the smoke test takes up to ~5 min, so give the command a long
   timeout.
4. **Review** the change: correctness against `.ref`, other mods patching the same methods, and whether every claim
   in the docs and tests is true. Names and values that are not in `.ref` (spawn/prefab/piece names, station levels,
   capacities) must be checked (wiki, game data, runtime log) or marked "(unverified)" in `TESTING.md` and listed at
   the pause. Fix, rebuild, re-run the smoke test. When multi-agent workflows are allowed, fan out research (step 1)
   and review, and have each finding checked by an agent that tries to refute it.
5. **Document**, all in the same change:
   - mod folder: `README.md` (no `TODO`; features, config table, multiplayer, good to know, compatibility with other
     mods), `CHANGELOG.md`, `TESTING.md` ("Build under test": version, "build id = the commit in the `[MC:ready]` log
     line", smoke-test result; Setup with checked spawn names; items for every expected behaviour, live toggle,
     `Enabled = false` + restart, clean log; multiplayer incl. a hand-off item; cross-mod items for MC mods that
     patch the same methods or touch the same items);
   - `docs/design/<category>-<feature>.md`, same outline as the existing design docs;
   - root `README.md` mods table; `packaging/nexus/pages.json` (empty URL); `packaging/nexus/PACK_CHANGELOG.md`
     (list the mod in the top entry if that pack version has no `nexus/pack/v*` tag yet, else in a new entry);
   - `docs/game/<chapter>.md`: vanilla facts learned, and a status link on the idea's feature note;
   - `./tools/Update-Backlog.ps1`, then check the idea's rows in `docs/backlog.md` link the mod (the script warns
     about a `ModIdea` that matches no sheet idea); `-Offline` when the sheet cannot be downloaded;
   - `CLAUDE.md` or other docs when a rule or fact turned out wrong.
6. **Pause.** Do not commit. Give the user: what was built and how; the GUID and display name (permanent after
   release); decisions, assumptions and extras to confirm; the files changed (`git status`); what was verified (build,
   smoke test) and what was not (in-game, "(unverified)" names); the test list (`./tools/Get-TestTodo.ps1 -Mod
   <Feature>`); the proposed commit message(s); what the push will publish (this run's commit(s) plus any commit
   already in `git log --oneline origin/main..HEAD`); the sheet cells the user will need to change (the idea's Status =
   `Implemented`, and any other cell the run made wrong). Then say exactly what the OK does: "commit on main, push to
   origin (public repo) and open the public issue 'In-game tests: <ModName> <Version>'". Wait for an explicit OK. On
   change requests: apply, re-verify, pause again.
7. **Commit and push** after the OK: stage this run's files by path (never `git add -A`), caveman commit message(s),
   no Claude credit (in commits and in the issue), `git push origin main`. Then rebuild (`dotnet build
   ValheimMods.slnx`) so the deployed DLL's build id is the pushed commit, not `<hash>+dirty`. Then re-run
   `./tools/Update-Backlog.ps1` and give the user its list of sheet cells to change (never edit the sheet yourself).
8. **Test list as a GitHub issue**, after the push so the commit and file paths resolve on GitHub:
   `gh issue create --repo Blaakan/MC_Valheim --title "In-game tests: <ModName> <Version>" --body-file <scratch file>`.
   Body in normal English: build under test (commit hash), `TESTING.md` as the source of truth, the design doc path,
   the Setup, then every pending item as a `- [ ]` task list grouped like `TESTING.md`. Give the user the link.
   `gh` not found: try `& "C:\Program Files\GitHub CLI\gh.exe"` (a shell started before the install has an old
   PATH). Not installed or not logged in: ask the user to run `winget install GitHub.cli`, then `gh auth login`
   (interactive) in a new terminal.
9. **Results** (often a later session; find the issue by its title with `gh issue list --search`). The user may
   report in chat or tick boxes and comment on the issue: read `gh issue view <n> --comments` first and merge both.
   Tick `TESTING.md` (keep IDs; it is what `Package-Mod.ps1` reads), then rewrite the issue body from it
   (`gh issue edit <n> --body-file`): `[x]` = `- [x]`, `[!]` = `- [ ] ... **FAILED:** <note>`, `[-]` =
   `- [x] ~~...~~ (skipped: <why>)`. Commit (push only when asked). A failure that needs a code fix goes back
   through steps 3-6 (pause before commit); reset the affected tests to `[ ]` in both places and update the issue's
   build under test after the push. Close the issue when every item is `[x]` or `[-]`.

## Game code reference

- `.ref/decompiled/assembly_valheim/<Class>.cs` (global namespace; nested types live in the parent's file, e.g.
  `ItemDrop.ItemData` is in `ItemDrop.cs`). Game version stamp: `.ref/game-version.json` (written by
  `Update-GameRefs.ps1`; if missing, read `Version.CurrentVersion` in `.ref/decompiled/assembly_valheim/Version.cs`).
- Start from `docs/game/<chapter>.md`, then confirm in `.ref`. Valheim changed a lot through 1.0 (Ashlands,
  Bog Witch, Deep North, trinkets, adrenaline…): never trust memory of older versions; verify every name.

## Mod code conventions

- `Plugin.cs`: `internal sealed partial class Plugin : ModPlugin`, overriding only `BindConfig`, `OnActivated`,
  `OnDeactivated` (plus `LocalBlocker` when another mod can make it unable to run; never
  `Awake`/`Start`/`Update`/`OnDestroy`). Patch classes that must survive the live toggle (item/prefab/RPC
  registration) get `[AlwaysOnPatch]` (docs/modding/framework.md, "Content that stays registered"). The build generates `ModInfo` and the
  `BepInPlugin` / `BepInProcess` (Client mods) / soft `BepInDependency` attributes from the csproj. Never
  hardcode GUID/name/version.
- csproj metadata (single source of truth, validated by the build): `ModName`, `Version`, `ModScope`, `ModIdea`
  (sheet idea name, several ideas `;`-separated; NOT checked by the build: `./tools/Update-Backlog.ps1` warns when it matches no sheet idea, and the
  backlog row then shows the sheet Status instead of the mod), `ModSide` (`Client` | `Server` | `Both`), `ModMultiplayer` (`Compatible` | `Limited` |
  `SinglePlayer`), `ModMultiplayerNotes` (player-facing; required unless Client + Compatible), `ModRequires`
  (GUIDs of MC mods needed at runtime, `;`-separated, no cycles), `ModDependencies` (external Thunderstore strings),
  `ModNetworkVersion` (Both mods: bump when RPC names/payloads or ZDO keys/formats change), `ModDescription`.
- **Mods that change the experience are required everywhere** (user rule): odds, costs, fuel, combat, balance, world
  rules. `ModSide=Both`, the server refuses players without the mod after a short grace and sends its gameplay
  settings to every player (copy Breeding's `PlayerCheck` + server settings; `AllowPlayersWithoutMod`, default
  false). Only pure UI/QoL helpers that change nothing for others stay client-side. Either way the code stays
  multiplayer-compatible: say in `ModMultiplayerNotes` who needs it and why, and think through hand-offs (an
  item/structure touched by the mod reaching a player without it must behave sanely).
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
  After tagging, run `./tools/Update-Backlog.ps1` and commit `docs/backlog.md` (the backlog shows "released" from the
  tag; the sheet Status stays `Implemented`).
  **Nexus AI tags for every page: "AI Assisted" + "AI Media"** (AI Assisted needs visible evidence of human-led development on the page), and untested AI mods count as spam: tag every
  release and never publish without the in-game tests.
