# Valheim 1.0 / Unity 6 BepInEx toolchain research

Researched on 2026-09-28. Game build: **Valheim 1.0.16 (network version 40)** on **Unity 6000.0.75f1 (Mono)**.
This covers the toolchain for a BepInEx 5 + HarmonyX mono-repo at `D:\Gits\ValheimMods`. Every version number below comes from the local install or from a live registry or API query made on the research date. Game code is only referenced by `Class.Member` and never reproduced.

---

## 0. TL;DR (decisions)

| Topic | Decision |
|---|---|
| Target framework | **`net48`**, SDK-style csproj, `LangVersion` 12/latest, plus **PolySharp** for compiler polyfills |
| BepInEx dependency string | **`denikson-BepInExPack_Valheim-5.4.2350`** matches the installed pack. `5.4.2351` (2026-09-24) is the latest and only improves logging and the Linux doorstop. |
| Publicizer | **`BepInEx.AssemblyPublicizer.MSBuild` 0.4.3** (`PrivateAssets="all"`, `Publicize="true"` on the game references, `AllowUnsafeBlocks=true`). Avoid 0.5.0-beta.x. |
| Game/Unity refs | **Local references** from `valheim_Data\Managed` and `BepInEx\core` with **`Private="false"`**, set centrally in `Directory.Build.props`. No `BepInEx.Core` or `UnityEngine.Modules` NuGet needed. |
| Jotunn | **2.30.2** (2026-09-21) is 1.0-ready. Make it the standard dependency for content, network and config-sync mods. Pure client-side QoL mods can stay plain BepInEx. |
| Config sync | Use **Jotunn** (`IsAdminOnly` + `[SynchronizationMode(AdminOnlyStrictness.IfOnServer)]`). Use **ServerSync 1.20** (MIT-0, ILRepack-merged) only for mods that must not depend on Jotunn. |
| Debugger | Doorstop 4.4.0 `debug_enabled=true`, **portable PDB** next to the DLL, VS 2026 "Attach Unity Debugger → Input IP". Use dnSpyEx 6.6.0 to step through game code. |
| Inspection | **UnityExplorer yukieiji v4.13.6** (Unity 6 fix since v4.13.2), **RuntimeUnityEditor v6.3.2**, **shudnal ConfigurationManager 1.1.22**, **ScriptEngine r11.1** for logic-only hot reload |
| Packaging | Thunderstore zip (`manifest.json`, 256×256 `icon.png`, `README.md`, `CHANGELOG.md`, `plugins/`) built by MSBuild, optionally uploaded with `tcli` 0.2.4. Nexus zip uses `BepInEx/plugins/<Mod>/`. **Add the AI disclosure category or tag on both sites.** |
| CI | GitHub Actions: download the **Valheim Dedicated Server (app 896660)** anonymously with steamcmd and the BepInExPack from Thunderstore, then build. **Never commit game DLLs**, stripped or not. |

---

## 1. Local install audit (E:\SteamLibrary\steamapps\common\Valheim)

| Item | Finding |
|---|---|
| Engine | `valheim.exe` / `UnityPlayer.dll` ProductVersion **6000.0.75f1 (26349cd2a5c8)** |
| Scripting backend | **Mono**: `MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll` is present. There is no `GameAssembly.dll` and no `il2cpp_data`. |
| Game version | `Version.CurrentVersion` = 1.0.16 and `Version.c_networkVersion` = 40 (decompiled `Version.cs`) |
| Corlib | `mscorlib.dll` 4.0.0.0 (file 4.6.57.0) and `netstandard.dll` 2.1.0.0. The corlib contains `System.Span`, `System.Index`, `System.Range` and `System.HashCode`. It does **not** contain `IsExternalInit` or `RequiredMemberAttribute`. |
| Game assemblies | `assembly_valheim`, `assembly_utils`, `assembly_guiutils`, `gui_framework`, `SoftReferenceableAssets`, `Splatform(.Steam)`, `assembly_postprocessing`, `assembly_sunshafts`, `assembly_lux`, `assembly_simplemeshcombine`, `assembly_googleanalytics`, `Assembly-CSharp`, PlayFab, MagicaClothV2. All have assembly version 0.0.0.0. The decompiled csproj reports `net40`, i.e. the game targets the .NET Framework API level. |
| Notable 3rd-party | `Unity.InputSystem` **1.19.0**, `Unity.TextMeshPro`, `UnityEngine.UI` 1.0.0, **`Newtonsoft.Json` 13.0.2** (ships with the game), `System.Memory` 4.0.99.0, `lib_burst_generated.dll` (Burst) |
| Render pipeline | Built-in (no `Unity.RenderPipelines.*` assemblies). The game ships `D3D12` next to D3D11. |
| BepInEx | `BepInEx.dll` / `BepInEx.Preloader.dll` **5.4.23.5+ef506e0**, `0Harmony.dll` **2.9.0** (HarmonyX), plus `0Harmony20.dll` (Harmony 2.0 shim), `MonoMod.RuntimeDetour` 22.1.29.1 and `Mono.Cecil` 0.10.4 |
| Pack version | `changelog.txt` shows "Bump Thunderstore version to **5.4.2350**" (AzumattDev fork of the BepInEx `v5-lts` branch) |
| Doorstop | `winhttp.dll` **4.4.0**, `.doorstop_version` = 4.4.0. `doorstop_config.ini` has `target_assembly=BepInEx\core\BepInEx.Preloader.dll`, **`dll_search_path_override` empty** (the old "unstripped corlib" is no longer needed), and `debug_enabled=false`, `debug_address=127.0.0.1:10000`, `debug_suspend=false`. |
| BepInEx.cfg | Pack-preconfigured with entrypoint `UnityEngine.CoreModule.dll` / `GameObject` / `.cctor`, the console enabled (`PreventClose=true`), `WriteUnityLog=true`, `HarmonyBackend=auto` and `LogChannels=Warn, Error`. BepInEx **has not run yet**, so `plugins/`, `patchers/`, `cache/` and `LogOutput.log` will appear on the first launch. |
| Modded flag | The pack toggles `Game.isModded`. The game then shows a "modded" label and `Achievements` treats modded sessions like cheat sessions (FYI for players). |
| Handy launch args | `FejdStartup` parses `-console` (F5 console), `+connect <ip:port>`, `-password`, `+connect_lobby`, `-joincode`, `-joinserverwithcharacter` and the server args `-world`, `-name`, `-port`. The `Terminal` command `devcommands` unlocks `spawn`, `god`, `fly`, `nocost`, `raiseskill`, `goto`, `pos`, `exploremap`, `killall`, `tod`, `itemset`, `resetcharacter` and more. |
| Misc | `SingularityGroup.HotReload.Runtime.Public.dll` only contains attribute stubs (Iron Gate's editor-side tooling). It is **not** usable for mod hot reload. |

Timeline: Valheim moved to Unity 6 (**6000.0.61f1**) in patch **0.221.10 on 2026-02-02** ([patched.gg](https://patched.gg/games/valheim/patch-022110-five-years-of-valheim)). **1.0 shipped 2026-09-09** ([berrybyte](https://berrybyte.net/blog/valheim-1-0-live-server-update)) and **1.0.16 on 2026-09-25** ([valheimgame.com](https://www.valheimgame.com/news/patch-1-0-16/)).

---

## 2. Target framework

**Recommendation: `net48` in an SDK-style project.** Reasons:

- Unity 6 Mono runs the "unityjit" 4.x profile (`mscorlib` 4.0.0.0 plus a `netstandard` 2.1 facade). Valheim's own assemblies are compiled against `mscorlib` (the .NET Framework API level), so a .NET Framework TFM matches the game and every other mod.
- The ecosystem does the same:
  - **Jotunn** ships `lib/net462` ([NuGet JotunnLib 2.30.2](https://www.nuget.org/packages/JotunnLib)).
  - **JotunnModStub** uses `net48` ([csproj](https://github.com/Valheim-Modding/JotunnModStub)).
  - **Azumatt** templates use `v4.8` ([ItemManagerModTemplate](https://github.com/AzumattDev/ItemManagerModTemplate)).
  - **ComfyMods** (70+ mods) uses `v4.8` with `LangVersion 12` ([redseiko/ComfyMods](https://github.com/redseiko/ComfyMods)).
  - **EpicLoot** (OrianaVenture fork) uses `net481` with `LangVersion 12` ([Randy_Vapok_ValheimMods](https://github.com/OrianaVenture/Randy_Vapok_ValheimMods)).
- `netstandard2.1` would load, because the game ships `netstandard.dll` 2.1. However, it produces NU1701 warnings when referencing net462 Jotunn and brings no benefit over net48 plus polyfills.
- The TFM only controls the compile-time API surface. At runtime the game's Mono corlib is used. Do **not** reference the game's `mscorlib.dll`/`netstandard.dll` directly ([BepInEx docs](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html)).
- Build machines without the 4.8 targeting pack need `Microsoft.NETFramework.ReferenceAssemblies` **1.0.3** (nuget.org). The official BepInEx template references it explicitly ([template csproj](https://github.com/BepInEx/BepInEx.Templates)).
- **C# version**: use `LangVersion` 12 or `latest` together with **PolySharp 1.16.0** (`PrivateAssets=all`). PolySharp source-generates `IsExternalInit`, `RequiredMemberAttribute`, nullable attributes and similar types, which the game corlib lacks. Records, `init` and `required` then compile. Avoid default interface methods and other runtime-dependent features.

---

## 3. BepInExPack dependency string

- Installed: **BepInEx 5.4.23.5** inside the pack labelled **5.4.2350**, as shown by `changelog.txt` and the file versions.
- Thunderstore versions ([versions page](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/versions/), [API](https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/)):
  - **5.4.2351**: 2026-09-24. Plugin GUIDs in load logs, Valheim version logging fixes, Linux `libdoorstop` updated to 4.5.0. Same BepInEx core 5.4.23.5.
  - **5.4.2350**: 2026-09-09, the 1.0 launch build. Moves to BepInEx 5.4.23.5, adds semver plugin version support, unifies the Unity 6 log writer handling and survives stripped log callbacks.
  - **5.4.2333**: 2025-08-29. First Unity 6 log-writer fix (`UnityLogWriter` binding). Jotunn 2.30.2 still declares this version.
  - Changelog: [Thunderstore changelog](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/changelog/). Source: [AzumattDev/BepInEx `v5-lts`](https://github.com/AzumattDev/BepInEx/tree/v5-lts).
- **Use in `manifest.json`: `"denikson-BepInExPack_Valheim-5.4.2350"`.** This is exactly what we build and test against, and it is the first 1.0-era pack. Bumping to `5.4.2351` is harmless but optional.
- Upstream BepInEx 5.4.23.5 was released on 2026-02-08 ([BepInEx releases](https://github.com/BepInEx/BepInEx/releases)). BepInEx 5.4 is in LTS mode, which means fixes only.

---

## 4. Publicizing and references

### 4.1 BepInEx.AssemblyPublicizer.MSBuild
- Latest stable is **0.4.3** (2025-02-09, MIT). Prereleases 0.5.0-beta.1/beta.2 exist, but open issue [#28 "Beta 5.0.0 missing dependency in net472 lib folder"](https://github.com/BepInEx/BepInEx.AssemblyPublicizer/issues) (2026-07-10) breaks the beta inside **Visual Studio**, whose MSBuild loads the net472 task. **Pin 0.4.3.** ([NuGet](https://www.nuget.org/packages/BepInEx.AssemblyPublicizer.MSBuild), [repo](https://github.com/BepInEx/BepInEx.AssemblyPublicizer))
- The package contains two task builds:
  - `netstandard2.1`, used by `dotnet build`. It works with the installed SDK 8.0.405 and 9.0.308.
  - `net472`, used by VS 2026 MSBuild.
  - It requires an SDK-style project.
- How it works:
  1. Runs after reference resolution.
  2. Writes publicized copies to `obj/<cfg>/publicized/`, cached by MD5 and redone automatically after a game patch.
  3. Swaps them in for the originals.
  4. Emits `IgnoresAccessChecksToAttribute` for CoreCLR.
  5. On **Mono**, the runtime skips visibility checks only when the plugin is compiled with `AllowUnsafeBlocks` (the props enable it; set it explicitly anyway).
- Item metadata:
  - `Publicize`
  - `PublicizeTarget`
  - `PublicizeCompilerGenerated` (default false, which avoids event/backing-field name clashes)
  - `IncludeOriginalAttributesAttribute`
  - `Strip`
- CLI variant: `BepInEx.AssemblyPublicizer.Cli` (`assembly-publicizer x.dll [--strip]`), useful for making reference assemblies.
- Alternative: **Krafs.Publicizer 2.3.2** (nuget.org, `<Publicize Include="assembly_valheim" />`). It works too, but BepInEx's tool is the Unity/BepInEx-native choice. The older approach, where the Jotunn prebuild writes `*_publicized.dll` into `valheim_Data\Managed\publicized_assemblies` (used by the Azumatt/ComfyMods csprojs), modifies the game folder and needs a manual re-run after patches. Avoid it.

### 4.2 Reference style
- Use **local `Reference` items with `Private="false"`** so the game, Unity and BepInEx DLLs never get copied into the output or the package. ComfyMods does this for every reference.
- `BepInEx.Core` on nuget.bepinex.dev stops at **5.4.21**, with `HarmonyX` 2.7.0 as a dependency. It does not match the 5.4.23.5 / HarmonyX 2.9.0 runtime. **Compile against `BepInEx\core\BepInEx.dll` and `0Harmony.dll` from the install.**
- `UnityEngine.Modules` **6000.0.75** exists on nuget.bepinex.dev, which is the exact engine version. It only covers engine modules though, not `UnityEngine.UI`, `Unity.TextMeshPro` or `Unity.InputSystem`, and CI needs the game assemblies anyway. Local refs from one folder are simpler and exact. Keep this package as an option only.
- Reference the game's own `Newtonsoft.Json.dll` (13.0.2) with `Private=false` if JSON is needed. Do not bundle another copy.

### 4.3 Proposed central props (sketch, to be validated when the workspace is built)
```xml
<!-- Directory.Build.props (repo root) -->
<Project>
  <Import Project="$(MSBuildThisFileDirectory)Environment.props"
          Condition="Exists('$(MSBuildThisFileDirectory)Environment.props')" />  <!-- gitignored, per machine -->
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>          <!-- required for publicized access on Mono -->
    <Deterministic>true</Deterministic>
    <DebugType>portable</DebugType>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <VALHEIM_INSTALL Condition="'$(VALHEIM_INSTALL)'==''">E:\SteamLibrary\steamapps\common\Valheim</VALHEIM_INSTALL>
    <!-- dedicated server uses valheim_server_Data -> allow override for CI -->
    <VALHEIM_MANAGED Condition="'$(VALHEIM_MANAGED)'==''">$(VALHEIM_INSTALL)\valheim_Data\Managed</VALHEIM_MANAGED>
    <BEPINEX_CORE Condition="'$(BEPINEX_CORE)'==''">$(VALHEIM_INSTALL)\BepInEx\core</BEPINEX_CORE>
    <PathMap>$(MSBuildThisFileDirectory)=/_/</PathMap>   <!-- no local paths in PDBs -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.3" PrivateAssets="all" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="2.1.0" PrivateAssets="all" />
    <PackageReference Include="BepInEx.Analyzers" Version="1.0.8" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="PolySharp" Version="1.16.0" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="$(BEPINEX_CORE)\BepInEx.dll" Private="false" />
    <Reference Include="$(BEPINEX_CORE)\0Harmony.dll" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\assembly_valheim.dll"  Publicize="true" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\assembly_utils.dll"    Publicize="true" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\assembly_guiutils.dll" Publicize="true" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\gui_framework.dll"     Publicize="true" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\SoftReferenceableAssets.dll" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\Splatform.dll" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\UnityEngine.dll" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\UnityEngine.*Module.dll" Private="false" />  <!-- glob; trim if noisy -->
    <Reference Include="$(VALHEIM_MANAGED)\UnityEngine.UI.dll" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\Unity.TextMeshPro.dll" Private="false" />
    <Reference Include="$(VALHEIM_MANAGED)\Unity.InputSystem.dll" Private="false" />
  </ItemGroup>
</Project>
```
`BepInEx.PluginInfoProps` 2.1.0 generates `MyPluginInfo.PLUGIN_GUID/NAME/VERSION` from `BepInExPluginGuid`/`AssemblyName`, `Product` and `Version` ([props source](https://github.com/BepInEx/BepInEx.Templates)). This keeps a single source of truth for the version, and the same MSBuild `Version` can generate `manifest.json`.

---

## 5. Jotunn

- **Latest: 2.30.2** (2026-09-21), on [Thunderstore](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/), [NuGet `JotunnLib`](https://www.nuget.org/packages/JotunnLib) and [GitHub](https://github.com/Valheim-Modding/Jotunn). MIT, 4.6M downloads, about 6,000 dependants. Thunderstore dependency: `ValheimModding-Jotunn-2.30.2`. BepInEx GUID: `Jotunn.Main.ModGuid` = `com.jotunn.jotunn`.
- **1.0 status** ([changelog](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/changelog/)):
  - 2.30.0 (2026-09-09, launch day): "Updated the majority of systems for Valheim 1.0.7".
  - 2.30.1: custom piece categories for the **new 1.0 build menu** (`PieceConfig.Usage`). The piece-table category API is deprecated because 1.0 replaced categories with `Piece.UsageTagFlags` (`Piece.m_usage`). In decompiled 1.0.16 code, `PieceTable.m_availablePieces` is now a flat public `HashSet<Piece>` and the per-category lists moved to a private `m_availablePiecesByCategory`.
  - 2.30.2: Deep North mock-asset and legacy build-menu fixes. The dev branch has further piece-category fixes as of 2026-09-27.
  - Game 1.0.16 (2026-09-25) is a bug-fix patch released after 2.30.2. No incompatibility has been reported, but verify.
- **What it offers** ([overview](https://valheim-modding.github.io/Jotunn/guides/overview.html)):
  - Content managers: `ItemManager`, `PieceManager` (custom tables and categories), `PrefabManager` with **mock** references (`JVLmock_`) to vanilla assets so bundles do not embed copyrighted assets, `CreatureManager`, `ZoneManager` (locations, vegetation, clutter) and `MinimapManager` (overlays and map drawing, relevant to "minimap tracking").
  - Gameplay managers: `SkillManager`, custom status effects and `CommandManager` (console commands with Terminal context since 2.29.0).
  - `LocalizationManager` (JSON/translation folders).
  - `InputManager`: `ButtonConfig` with `ConfigEntry<KeyCode>`/`KeyboardShortcut`/gamepad, queried through `ZInput.GetButtonDown(name)`, plus `KeyHintManager` ([inputs](https://valheim-modding.github.io/Jotunn/tutorials/inputs.html)).
  - `GUIManager` for Valheim-styled panels, buttons, text, inputs and dropdowns.
  - `NetworkManager` custom RPCs with large-payload slicing.
  - **Config sync** (section 6) and **`[NetworkCompatibility(CompatibilityLevel, VersionStrictness)]`** version enforcement ([docs](https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html)):
    - Levels: `NotEnforced` (the default when the attribute is omitted), `VersionCheckOnly`, `EveryoneMustHaveMod`, `ClientMustHaveMod`, `ServerMustHaveMod`.
    - Strictness: `None` < `Major` < `Minor` < `Patch`, where Patch is strictest because all three components must match.
  - Render-to-icon and undo helpers.
- **Build integration caveat.** The `JotunnLib` NuGet package auto-imports `build/JotunnLib.props`. That file injects about 80 `Reference`s to `…/publicized_assemblies/*_publicized.dll` without `Private=false`, and can run a prebuild that writes into the game folder. Those references would conflict with our own publicized refs (duplicate types).
  - Consume Jotunn as `<PackageReference Include="JotunnLib" Version="2.30.2" ExcludeAssets="build;runtime" PrivateAssets="all" />`. You get compile-time `Jotunn.dll` without the injected refs, the prebuild or the DLL copy.
  - Players get Jotunn through the Thunderstore dependency.
- **Pros for a large collection:**
  - One tested implementation of item, piece and recipe registration, localization, input, UI, RPC, config sync and version handshakes.
  - Fast updates: it was ready the evening of 1.0.
  - Mocking keeps asset bundles legal and small.
  - Players already have it installed (6,000 dependants).
  - Jotunn's `NetworkCompatibility` gives clear mismatch messages.
- **Cons:**
  - An external runtime dependency (835 KB) that every mod inherits.
  - Its release cadence gates ours after game patches.
  - An API deprecation (piece categories) already happened in 1.0.
  - Some players and servers prefer dependency-free QoL mods.
  - Misconfigured `NetworkCompatibility` can kick players.
  - Jotunn-free mods need ServerSync for synced settings.
- **Recommendation:**
  - Use Jotunn for anything that adds content (Crafting/Cooking/Building/"New" scope), localization, custom inputs or UI, or that needs server-enforced settings.
  - Allow **dependency-free** plain BepInEx for small client-only QoL mods.
  - Put shared helpers in our own `ValheimMods.Common` source project, compiled into each mod as `internal` code, so we do not create a second runtime library until one is clearly needed.

---

## 6. Multiplayer config sync

| | **Jotunn SynchronizationManager** | **ServerSync (blaxxun-boop)** |
|---|---|---|
| Status | Maintained, 2.30.2 (2026-09-21) | **v1.20 "Recompile for 1.0"** (2026-09-09), repo active ([GitHub](https://github.com/blaxxun-boop/ServerSync)) |
| License | MIT | **MIT-0** |
| Delivery | Runtime dependency (`ValheimModding-Jotunn`) | Merge `ServerSync.dll` into each mod with **ILRepack** (`ILRepack.Lib.MSBuild.Task` 2.0.48). Multiple copies coexist by design. |
| Marking entries | `new ConfigurationManagerAttributes { IsAdminOnly = true }` in `ConfigDescription` tags, or `Config.BindConfig(..., synced: true)` | `configSync.AddConfigEntry(entry).SynchronizedConfig = true` |
| Server lock | Admins (from `adminlist.txt`) can edit synced values in-game and the values push to the server. Non-admins are read-only. | `AddLockingConfigEntry(ConfigEntry<bool>)`. Only the server's values apply while it is locked. |
| Modded-client/vanilla server | `[SynchronizationMode(AdminOnlyStrictness.IfOnServer)]` only enforces when Jotunn is on the server, so a client-only fallback still works | Works client-only when the server lacks the mod |
| Version gating | `[NetworkCompatibility]` | `CurrentVersion`, `MinimumRequiredVersion` (kicks old clients), `ModRequired` |
| Extras | `SynchronizationManager.OnConfigurationSynchronized` (`InitialSynchronization` flag), `OnAdminStatusChanged`, `PlayerIsAdmin`, `RegisterCustomConfig(ConfigFile)` | `CustomSyncedValue<T>` for arbitrary data (YAML/JSON blobs), `IsSourceOfTruth` |

Sources: [Jotunn config tutorial](https://valheim-modding.github.io/Jotunn/tutorials/config.html) and the [ServerSync README](https://github.com/blaxxun-boop/ServerSync).

A newer option, **shudnal-ConditionalConfigSync 1.0.9** (2026-09-21), describes itself as a shared config-sync and server-policy library "compatible with Jotunn and ServerSync" ([Thunderstore](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync/)). Watch it, but do not adopt yet.

**Recommendation:** Jotunn sync for all Jotunn-based mods. Use ServerSync plus ILRepack only for dependency-free mods that need enforcement. Never mix both in one mod.

---

## 7. Unity 6 / Valheim 1.0 pitfalls

1. **It is Mono, not IL2CPP** (see section 1). BepInEx 5, HarmonyX and Doorstop work normally. Do not use Il2CppInterop or IL2CPP builds of any tool. For example, one UnityExplorer "Unity 6" failure report came from using the IL2CPP CoreCLR build on a Mono game ([issue #132](https://github.com/yukieiji/UnityExplorer/issues/132)).
2. **Pack version matters.** Unity 6 changed the internal log writer, so packs older than 5.4.2333 lose logging. On 1.0, require **5.4.2350+**.
3. **Obsolete engine APIs.** The game now uses `Rigidbody.linearVelocity`, `linearDamping` and `angularDamping`, and `Object.FindObjectsByType`, `FindFirstObjectByType` and `FindAnyObjectByType`. The old `velocity`, `drag`, `angularDrag`, `FindObjectsOfType` and `FindObjectOfType` still exist but are `[Obsolete]` (verified in the Unity 6000.0.75 `PhysicsModule` and `CoreModule`). Use the new names, and `FindObjectsSortMode.None` for speed.
4. **Input.** `ZInput` (assembly_utils) is built on the **new Input System 1.19** (`Keyboard.current`/`Mouse.current`). The legacy `UnityEngine.Input` still exists and is used by some gamepad definitions.
   - Query keys through `ZInput.GetKey/GetKeyDown(KeyCode)`, `ZInput.GetButtonDown(name)` (for Jotunn buttons) or BepInEx `KeyboardShortcut`. BepInEx 5.4.23 `UnityInput` auto-selects `NewInputSystem`/`LegacyInputSystem`.
   - Also check that no text field, chat or console has focus before acting on hotkeys.
5. **UI.** Vanilla UI is uGUI plus **TextMeshPro** (74 files in assembly_valheim use TMPro). Use `TMP_Text` with the game's fonts and materials: clone vanilla prefabs or use Jotunn `GUIManager`. IMGUI (`OnGUI`) still works and is what ConfigurationManager, UnityExplorer and RuntimeUnityEditor use.
6. **AssetBundles.**
   - Build them with a **Unity 6000.0.x** editor. Jotunn's asset docs say 6000.0.61 ([asset-creation](https://valheim-modding.github.io/Jotunn/tutorials/asset-creation.html)), and the game is now 6000.0.75f1, so match that. Rebuild 2022.3-era bundles, because shader bytecode differs.
   - The game uses the **built-in render pipeline**. Compile shaders for D3D11, D3D12, Vulkan, OpenGLCore and Metal, or they turn pink on some platforms.
   - Since 0.217.40, vanilla assets are loaded lazily through `SoftReferenceableAssets` ([Iron Gate FAQ](https://www.valheimgame.com/support/modding-faq-for-the-asset-bundle-update-0-217-40/)). Use Jotunn mocks or resolve prefabs at the right lifecycle point (`ZNetScene`/`ObjectDB` awake).
7. **Harmony.**
   - BepInEx 5 still ships **HarmonyX 2.9.0**. The PR to move v5 to 2.13 is still open ([#902](https://github.com/BepInEx/BepInEx/pull/902)).
   - Compile against the shipped `0Harmony.dll`. Never ship or ILRepack your own Harmony, and do not use the nuget.org HarmonyX 2.16.x APIs.
   - Mono can inline tiny methods. Harmony prevents that only if the patch is applied before the method is first called ([HarmonyX wiki](https://github.com/BepInEx/HarmonyX/wiki/Valid-patch-targets)).
   - Burst code (`lib_burst_generated.dll`, e.g. particle jobs using `IJobParticleSystemParallelFor`) and native engine code cannot be patched.
   - Use `harmony.UnpatchSelf()` or `UnpatchAll(ownId)` only. Never call `UnpatchAll()` with no ID ([wiki best practices](https://github.com/Valheim-Modding/Wiki/wiki/Best-Practices)).
8. **BCL surface.** The runtime has `Span`, `Index`, `Range` and `HashCode` (netstandard 2.1). There is no `IsExternalInit` or `RequiredMember`; use PolySharp. No "unstripped corlib" is needed anymore (`dll_search_path_override` is empty).
9. **Plugin versions.** Semver suffixes in `[BepInPlugin]` only load on pack 5.4.2350+. Use plain `x.y.z`.
10. **Unity null semantics.** `?.` and `??` bypass Unity's destroyed-object check. Use explicit `== null`/`!= null` or `TryGetComponent`.
11. **1.0 content/API churn.**
    - The build menu now uses usage tags (`Piece.UsageTagFlags`).
    - VisEquipment item setters switched to hashes.
    - Save/world/player data versions were bumped (`Version.c_WorldVersion`/`c_PlayerVersion` = DeepNorth).
    - Verify every member against `.ref/decompiled`; tutorials from before 1.0 are often wrong.
12. **Crossplay.** Any mod that requires both client and server (custom RPCs, version checks) breaks Xbox crossplay. Client-only and server-only mods remain crossplay-safe ([wiki](https://github.com/Valheim-Modding/Wiki/wiki/Xbox-Compatible-Mods)).
13. **Asset/data paths.** r2modman installs into `BepInEx/plugins/<Namespace-Name>/` and manual installs use another folder name. Always resolve files relative to `Path.GetDirectoryName(Info.Location)`.

---

## 8. Debugging (VS 2026 / Rider, Doorstop 4)

**How it works.**
- Doorstop 4 hooks `mono_jit_parse_options`. When `debug_enabled=true`, it injects the soft-debugger agent and calls `mono_debug_init` if the player has not done so ([bootstrap.c](https://github.com/NeighTools/UnityDoorstop)).
- So a normal **release** player becomes debuggable. The old approach of swapping in development-build `WindowsPlayer.exe`/`UnityPlayer.dll` and adding `player-connection-debug=1` is not needed.
- Command-line equivalents: `--doorstop-mono-debug-enabled true`, `--doorstop-mono-debug-address 127.0.0.1:55555` and `--doorstop-mono-debug-suspend`. Use these with r2modman launches: r2modman passes its own doorstop args, and the `winhttp.dll` in the game folder reads the game-folder `doorstop_config.ini`.

**Steps:**
1. In `doorstop_config.ini` on the dev machine, set `debug_enabled = true`, `debug_address = 127.0.0.1:55555` (any free port; 10000 is fine) and `debug_suspend = false`. Use `true` only when debugging `Awake` or very early code.
2. Build Debug with `DebugType=portable` and deploy `Mod.dll` plus `Mod.pdb` to `BepInEx\plugins\<Mod>\`. Unity's MonoBleedingEdge reads **portable PDBs**. `pdb2mdb` is legacy advice for old Unity 5 Mono and Windows "full" PDBs ([R2 wiki](https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Debugging-Your-Mods/)). For Release, consider `DebugType=embedded` so no separate file is needed.
3. Attach your debugger:
   - **Visual Studio 2026:** install the *Game development with Unity* workload (Visual Studio Tools for Unity). Choose **Debug → Attach Unity Debugger → Input IP** and enter `127.0.0.1` / `55555`. Breakpoints in our code work. VSTU cannot break inside game assemblies without source.
   - **Rider:** Run → Edit Configurations → **Mono Remote** with 127.0.0.1:55555, or a compound "launch game + Mono Remote" config. Rider can also step into decompiled game code.
   - **dnSpyEx 6.6.0** (2026-06-20): Debug → Start Debugging → *Unity (Connect)* → 127.0.0.1:55555. Best for breakpoints **in game code**. To debug preloader-patched assemblies, set `DumpAssemblies`/`LoadDumpedAssemblies` in BepInEx.cfg ([Valheim wiki guide](https://github.com/Valheim-Modding/Wiki/wiki/Debugging-Plugins-via-IDE), older but still conceptually valid).
4. **Line numbers in stack traces.** Mono only resolves them when debug info is initialized (`mono_debug_init`, which Doorstop calls when `debug_enabled=true`) and a portable or embedded PDB sits next to the DLL. In normal player sessions, expect method names without line numbers. **Verify this on the first run.** `DemystifyExceptions` (BepInEx.Debug) makes async and lambda frames readable.
5. There is no Edit-and-Continue or Hot Reload over the Mono soft debugger. Doorstop's .NET hot reload applies to IL2CPP CoreCLR only.

---

## 9. Hot reload & runtime inspection (do not download yet; URLs for later)

| Tool | Version (date) | Unity 6 status | Official URL |
|---|---|---|---|
| **UnityExplorer (yukieiji fork)**, BepInEx5 Mono build | **v4.13.6** (2026-04-30) | v4.13.2 (2026-02-19) "Fixed where iCall didn't work in Unity v6". The recommended fork. | https://github.com/yukieiji/UnityExplorer/releases/download/v4.13.6/UnityExplorer.BepInEx5.Mono.zip |
| UnityExplorer on Thunderstore (ValheimModding) | 4.12.7 (2025-07-27) | **Predates the Unity 6 fix.** Avoid until updated. | https://thunderstore.io/c/valheim/p/ValheimModding/UnityExplorer/ |
| UnityExplorer (sinai-dev original) | 4.9.0 (2022) | **Archived**. Do not use. | https://github.com/sinai-dev/UnityExplorer |
| **RuntimeUnityEditor** (ManlyMarco), BepInEx5 build | **v6.3.2** (2026-09-23), GPL-3.0 | IMGUI-based and actively maintained. Includes the **Harmony Patch Inspector** (v6.2), REPL, breakpoints and profiler. Not specifically verified on Valheim 1.0, so test. | https://github.com/ManlyMarco/RuntimeUnityEditor/releases/download/v6.3.2/RuntimeUnityEditor.Bepin5_v6.3.2.zip |
| **ScriptEngine** (BepInEx.Debug) | **r11.1** (2026-01-01), LGPL-3.0 | Pure managed, so it should work. Loads and reloads plugins from `BepInEx\scripts` (F6, `LoadOnStart`, `EnableFileSystemWatcher`, `IncludeSubdirectories`, `DumpAssemblies`). | https://github.com/BepInEx/BepInEx.Debug/releases/download/r11.1/ScriptEngine_r11.1.zip |
| DemystifyExceptions / MirrorInternalLogs (patchers) | r11.1 | Readable stack traces / Unity internal log mirror | https://github.com/BepInEx/BepInEx.Debug/releases/tag/r11.1 |
| **ConfigurationManager (shudnal, Valheim)** | **1.1.22** (2026-09-27) | Built for 1.0. Depends on BepInExPack 5.4.2350, shudnal-ConditionalConfigSync, YamlDotNet and JsonDotNET. | https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/ |
| BepInEx.ConfigurationManager (upstream) | v19.0 (2026-06-29) | Generic. Azumatt's Valheim builds are **deprecated**. | https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/tag/v19.0 |
| dnSpyEx | v6.6.0 (2026-06-20) | Debugger and decompiler | https://github.com/dnSpyEx/dnSpy/releases |

**Hot-reload caveats (ScriptEngine).**
- The plugin must fully undo itself in `OnDestroy`: `harmony.UnpatchSelf()` and destroy any GameObjects it created.
- Static state survives in the old assembly.
- Jotunn content registration (items, pieces, prefabs), `ZRoutedRpc` registrations and `NetworkCompatibility` cannot be undone. ScriptEngine plugins are not in `Chainloader.PluginInfos`.
- So use it for logic and UI iteration only, and do a full restart for content mods.
- For quick experiments, the UnityExplorer and RuntimeUnityEditor C# consoles often beat reloads.

---

## 10. Packaging

### 10.1 Thunderstore
Sources: [package format](https://wiki.thunderstore.io/mods/creating-a-package), [packaging/install rules](https://wiki.thunderstore.io/mods/packaging-your-mods), [r2modman structure wiki](https://github.com/ebkr/r2modmanPlus/wiki/Structuring-your-Thunderstore-package), [Valheim ecosystem schema](https://github.com/thunderstore-io/ecosystem-schema).

**Required files at the zip root** (case-sensitive): `manifest.json`, `icon.png` (**exactly 256×256 PNG**) and `README.md` (UTF-8 markdown). `CHANGELOG.md` is optional and rendered in a Changelog tab.

**`manifest.json` fields:**
- `name`: max 128 characters, `[A-Za-z0-9_]` only. Underscores display as spaces.
- `version_number`: `Major.Minor.Patch` only.
- `website_url`: a URL or `""`.
- `description`: **max 250 characters**.
- `dependencies`: an array of `Namespace-Name-x.y.z`.

Uploads are immutable per version, and the namespace equals your Thunderstore team.

```json
{
  "name": "CrossbowKeepLoaded",
  "version_number": "1.0.0",
  "website_url": "https://github.com/<owner>/ValheimMods",
  "description": "Crossbows stay loaded when you holster or swap them.",
  "dependencies": [ "denikson-BepInExPack_Valheim-5.4.2350" ]
}
```

**Layout and install rules.** Valheim's r2modman rules have the routes `BepInEx/plugins` (default, `.dll`), `core`, `patchers`, `monomod` (`.mm.dll`), `config` (merged, not namespaced) and `SlimVML`. Every route except `config` installs into `BepInEx/<route>/<Namespace-Name>/`, and unknown folders are flattened. Recommended zip layout:
```
manifest.json  icon.png  README.md  CHANGELOG.md
plugins/CrossbowKeepLoaded.dll
plugins/Translations/...        (if any)
plugins/Assets/<bundle>         (if any)
```
Do not ship `config/`; let BepInEx generate it.

**Categories** (Valheim community): mods, tweaks, gear, crafting, building, enemies, npcs, world-generation, transportation, vehicles, pvp, utility, client-side, server-side, libraries, tools, audio, language, skins, misc, the update tags (deep-north-update, bog-witch-update, …) and **ai-generated**.

**Rules** ([global rules](https://wiki.thunderstore.io/moderation/global-rules)):
- "A package with AI generated assets or code should be given the `AI Generated` category". Since these mods are AI-assisted, **tag them**.
- Mods must be tested before upload. Untested AI-generated mods count as spam.
- Do not distribute game files such as `Assembly-CSharp.dll`.
- Avoid obfuscation.
- No self-updaters.

**tcli (Thunderstore CLI).**
- Version **0.2.4**, the latest release (2024-06-11), on nuget.org and [GitHub](https://github.com/thunderstore-io/thunderstore-cli). It is still labelled pre-release.
- Install with `dotnet tool install -g tcli`.
- Commands: `tcli init`, `tcli build`, `tcli publish --token <tok>` or env `TCLI_AUTH_TOKEN`. The token comes from a service account under Team settings.
- `thunderstore.toml` schema, from [ThunderstoreProject.cs](https://github.com/thunderstore-io/thunderstore-cli/blob/master/ThunderstoreCLI/Models/ThunderstoreProject.cs):
```toml
[config]
schemaVersion = "0.0.1"

[package]
namespace = "<Team>"
name = "CrossbowKeepLoaded"
versionNumber = "1.0.0"
description = "Crossbows stay loaded when you holster or swap them."
websiteUrl = "https://github.com/<owner>/ValheimMods"
containsNsfwContent = false
[package.dependencies]
denikson-BepInExPack_Valheim = "5.4.2350"

[build]
icon = "./icon.png"
readme = "./README.md"
outdir = "./build"
[[build.copy]]
source = "./bin/Release/CrossbowKeepLoaded.dll"
target = "plugins/CrossbowKeepLoaded.dll"
[[build.copy]]
source = "./CHANGELOG.md"
target = "CHANGELOG.md"

[publish]
repository = "https://thunderstore.io"
communities = ["valheim"]
[publish.categories]
valheim = ["mods", "tweaks", "client-side", "ai-generated"]
```
Since tcli is pre-release and has not had a release since 2024, the plan is to have MSBuild produce the zip and treat `tcli publish` as the optional last step.

### 10.2 r2modman / Gale dev testing
- r2modman **v3.2.20** (2026-09-22) ([GitHub](https://github.com/ebkr/r2modmanPlus)). Profiles live under `%AppData%\r2modmanPlus-local\Valheim\profiles\<Profile>\BepInEx\...`.
  - Use *Settings → Import local mod* to install our release zip, which validates the manifest and layout.
  - Because r2modman launches through Steam with its own doorstop args, debug settings go in the game-folder `doorstop_config.ini` or the `--doorstop-mono-debug-*` args.
- [Gale](https://github.com/Kesomannen/gale) is a popular alternative manager (Thunderstore plus the new Hexium host, per the [1.0 FAQ](https://github.com/Valheim-Modding/Wiki/wiki/Valheim-1.0-FAQ)).
- Day-to-day dev: deploy straight into the game-folder `BepInEx\plugins\<Mod>\`. Use an r2modman profile as the release-candidate check.
- Multiplayer and sync testing: install the free **Valheim Dedicated Server** (Steam tool, app 896660), copy the BepInEx pack into it, and join locally (`+connect 127.0.0.1:2456`).

### 10.3 Nexus Mods
- Upload an archive whose structure extracts into the game folder: `BepInEx/plugins/<ModName>/<ModName>.dll` plus assets.
- List BepInExPack and Jotunn under **Requirements**. Include the README text in the description and version the files.
- The Vortex Valheim extension was **archived 2026-05-20** ([repo](https://github.com/Nexus-Mods/game-valheim)), and it bundled an older BepInEx (5.4.22). Nexus users will often install manually, so write clear manual install steps.
- [Submission rules](https://help.nexusmods.com/article/28-file-submission-guidelines): no game files, permission for others' assets, and **AI tagging is mandatory** (*AI-Generated Content* / *AI Assisted* / *AI Media*). Mis-tagging can lead to moderation.

---

## 11. NuGet sources and packages

`NuGet.config` sources: `https://api.nuget.org/v3/index.json` and `https://nuget.bepinex.dev/v3/index.json`. The BepInEx template also lists `https://nuget.samboy.dev/v3/index.json`, which is only needed for BepInEx 6/IL2CPP.

| Package | Feed | Version | Use |
|---|---|---|---|
| BepInEx.AssemblyPublicizer.MSBuild | nuget.org | **0.4.3** | publicize game refs |
| BepInEx.PluginInfoProps | bepinex | **2.1.0** | `MyPluginInfo` constants |
| BepInEx.Analyzers | bepinex | **1.0.8** | Roslyn analyzers for plugins and Harmony |
| Microsoft.NETFramework.ReferenceAssemblies | nuget.org | **1.0.3** | net48 ref assemblies without a targeting pack |
| PolySharp | nuget.org | **1.16.0** | C# polyfills |
| JotunnLib | nuget.org | **2.30.2** | compile against Jotunn (`ExcludeAssets="build;runtime"`) |
| ILRepack.Lib.MSBuild.Task | nuget.org | 2.0.48 | merge ServerSync or other libs if needed |
| Krafs.Publicizer | nuget.org | 2.3.2 | alternative publicizer |
| BepInEx.Core / BepInEx.BaseLib | bepinex | 5.4.21 (the last 5.x on the feed) | not needed; prefer local 5.4.23.5 DLLs |
| UnityEngine.Modules | bepinex | 6000.0.75 (exact match exists) | optional; local refs preferred |
| BepInEx.Templates | bepinex | 2.0.0-be.4 (`dotnet new bepinex5plugin`) | reference only |
| tcli (dotnet tool) | nuget.org | 0.2.4 | Thunderstore build/publish |

Public "game libs" packages exist on nuget.org: `Digitalroot.Valheim.Common.References` 1.0.16, `Digitalroot.References.Unity` 6000.0.75, `ValheimGameLibs` 0.221.4 and `ValheimGameLibz` 0.221.12. They **redistribute (stripped or publicized) Iron Gate and Unity binaries**. Do not depend on them (see section 12.3).

---

## 12. Workspace recommendations

### 12.1 How prolific authors structure repos
- **ComfyMods (redseiko):**
  - Single mono-repo with one `ComfyMods.sln` and 70+ mods, one classic csproj per mod.
  - Shared `Environment.props` at the root.
  - Each mod folder holds its own `manifest.json`, `icon.png`, `README.md` and `CHANGELOG.md`, marked as `ThunderstorePackage` items.
  - A post-build copy step deploys to `BepInEx\plugins`.
  - Helpers are copied per mod in a `ComfyLib` subfolder.
- **RandyKnapp / OrianaVenture (EpicLoot and others):**
  - Mono-repo of SDK-style csprojs (`net481`) with a root `Paths.props`/`BuildTasks.props`.
  - A shared-code project (`Common.projitems`) and a per-mod `thunderstore/` folder.
  - `JotunnLib 2.*`, plus a Unity project for assets (`EpicLoot-UnityLib`).
- **AzumattDev:**
  - One repo per mod, started from templates (ItemManager/PieceManager/CreatureManager ModTemplates).
  - Blaxxun's managers and ServerSync are merged with **ILRepack**; the templates use `environment.props`.
- **JotunnModStub:**
  - One mod per repo, with `Environment.props` (`VALHEIM_INSTALL`, `BEPINEX_PATH`, `MOD_DEPLOYPATH`) and `DoPrebuild.props`.
  - `publish.ps1` copies the Debug DLL and PDB to plugins and zips a Thunderstore package on Release.
  - A companion Unity project for bundles.

### 12.2 Proposed layout for `D:\Gits\ValheimMods` (mono-repo)
A mono-repo fits because features cross categories, share helpers and need coordinated version bumps after game patches.
```
ValheimMods/
  ValheimMods.slnx                  # VS 2026 / SDK 9.0.200+ support .slnx
  global.json                       # pin SDK 9.0.3xx, rollForward latestFeature
  NuGet.config                      # nuget.org + nuget.bepinex.dev
  Directory.Build.props             # TFM, LangVersion, refs (Private=false), publicizer, analyzers
  Directory.Build.targets           # Deploy (Debug -> BepInEx\plugins\<Mod>), Package (Thunderstore zip + Nexus zip), manifest generation from $(Version)
  Directory.Packages.props          # Central Package Management
  Environment.props.example         # copy to Environment.props (gitignored): VALHEIM_INSTALL, R2_PROFILE, DEPLOY_DIR
  .editorconfig  .gitignore  .gitattributes  LICENSE
  src/
    Common/ValheimMods.Common/      # shared *source* (internal) helpers: logging, Harmony utils, ZInput helpers, config helpers
    Combat/CrossbowKeepLoaded/      # <Mod>.csproj, Plugin.cs, Patches/, package/{icon.png, README.md, CHANGELOG.md}
    Exploration/ Farming/ Cooking/ Building/ Crafting/ UX/
  unity/                            # (later) Unity 6000.0.75f1 project for AssetBundles
  tools/                            # packaging/publish scripts, tcli config
  docs/
  .ref/                             # decompiled game source -- MUST be gitignored, never pushed
```
- Name the assemblies, plugin GUIDs (reverse-DNS, **never change once published**, because the config file name derives from them), Harmony IDs (= GUID) and Thunderstore package names consistently.
- Log with `Logger` (BepInEx `ManualLogSource`), not `Debug.Log` or `ZLog`.
- Use `[BepInDependency(Jotunn.Main.ModGuid)]` when using Jotunn, and `[BepInIncompatibility]` only for real conflicts.

### 12.3 CI without the game, and licensing
- A proven pattern:
  1. On a GitHub runner, fetch the **Valheim Dedicated Server** anonymously (`steamcmd +login anonymous +app_update 896660 +quit`). It ships the same managed assemblies under `valheim_server_Data/Managed`, so set `VALHEIM_MANAGED` accordingly.
  2. Download `BepInExPack_Valheim` 5.4.2350 from Thunderstore for `BepInEx/core`.
  3. Run `dotnet build -c Release`, then package and upload artifacts.
  4. Publish only on tags, with a `TCLI_AUTH_TOKEN` secret.
  5. Cache the server download.
- **Licensing:**
  - Never commit game or Unity DLLs, including stripped or publicized reference assemblies, or the `.ref/decompiled` output.
  - They are Iron Gate's and Unity's copyrighted code. Thunderstore and Nexus both forbid distributing game files, and public repos carrying them risk DMCA takedowns.
  - Public NuGet "game libs" packages exist but are a legal gray area and a supply-chain trust issue. Avoid them.
  - If an anonymous steamcmd download is not acceptable, use a self-hosted runner on a machine that has the game installed.
- Choose a repo license for **our** code (MIT is typical). Note that Jotunn (MIT) and ServerSync (MIT-0) are compatible, while UnityExplorer and RuntimeUnityEditor (GPL-3.0) and ScriptEngine (LGPL-3.0) are dev tools only and must never be bundled.

### 12.4 Other useful references
- Valheim Modding Wiki: [Best Practices](https://github.com/Valheim-Modding/Wiki/wiki/Best-Practices), RPC guides, ZDO hashes, Layers, Snappoints, [Valheim Unity Project Guide](https://github.com/Valheim-Modding/Wiki/wiki/Valheim-Unity-Project-Guide) (AssetRipper project opened with a 6000.0.x editor), Key Binding Strings, Xbox-compatible mods, [Valheim 1.0 FAQ](https://github.com/Valheim-Modding/Wiki/wiki/Valheim-1.0-FAQ).
- Jotunn docs: https://valheim-modding.github.io/Jotunn/ (tutorials: config, inputs, pieces, items, localization, asset-creation, networkcompatibility).
- BepInEx docs: https://docs.bepinex.dev/ (plugin tutorial, runtime patching, debugging).
- Thunderstore Valheim API used for version checks: `https://thunderstore.io/api/experimental/package/<ns>/<name>/`.

---

## 13. Sources
- Thunderstore: [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) ([versions](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/versions/), [changelog](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/changelog/)) · [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) ([versions](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/versions/), [changelog](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/changelog/)) · [ValheimModding UnityExplorer](https://thunderstore.io/c/valheim/p/ValheimModding/UnityExplorer/) · [shudnal ConfigurationManager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/) · [Global rules](https://wiki.thunderstore.io/moderation/global-rules) · [Creating a package](https://wiki.thunderstore.io/mods/creating-a-package) · [Packaging your mods](https://wiki.thunderstore.io/mods/packaging-your-mods)
- BepInEx: [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) · [AzumattDev/BepInEx v5-lts](https://github.com/AzumattDev/BepInEx/tree/v5-lts) · [AssemblyPublicizer](https://github.com/BepInEx/BepInEx.AssemblyPublicizer) ([NuGet](https://www.nuget.org/packages/BepInEx.AssemblyPublicizer.MSBuild)) · [BepInEx.Templates](https://github.com/BepInEx/BepInEx.Templates) · [BepInEx.Debug](https://github.com/BepInEx/BepInEx.Debug) · [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) · [HarmonyX wiki](https://github.com/BepInEx/HarmonyX/wiki/Valid-patch-targets) · [HarmonyX 2.13 PR #902](https://github.com/BepInEx/BepInEx/pull/902) · [BepInEx docs: plugin setup](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/1_setup.html) · [VS debugging](https://docs.bepinex.dev/articles/advanced/debug/plugins_vs.html) · feed https://nuget.bepinex.dev/v3/index.json
- Doorstop: [NeighTools/UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) ([releases](https://github.com/NeighTools/UnityDoorstop/releases)) · [R2 wiki debugging](https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Debugging-Your-Mods/)
- Jotunn: [repo](https://github.com/Valheim-Modding/Jotunn) · [docs overview](https://valheim-modding.github.io/Jotunn/guides/overview.html) · [config](https://valheim-modding.github.io/Jotunn/tutorials/config.html) · [network compatibility](https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html) · [VersionStrictness API](https://valheim-modding.github.io/Jotunn/api/Jotunn.Utils.VersionStrictness.html) · [inputs](https://valheim-modding.github.io/Jotunn/tutorials/inputs.html) · [asset creation](https://valheim-modding.github.io/Jotunn/tutorials/asset-creation.html) · [step-by-step guide](https://valheim-modding.github.io/Jotunn/guides/guide.html) · [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub) · [NuGet JotunnLib](https://www.nuget.org/packages/JotunnLib)
- Sync: [ServerSync](https://github.com/blaxxun-boop/ServerSync) · [ConditionalConfigSync](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync/)
- Inspection: [yukieiji/UnityExplorer](https://github.com/yukieiji/UnityExplorer) ([releases](https://github.com/yukieiji/UnityExplorer/releases)) · [sinai-dev/UnityExplorer (archived)](https://github.com/sinai-dev/UnityExplorer) · [RuntimeUnityEditor](https://github.com/ManlyMarco/RuntimeUnityEditor) · [dnSpyEx](https://github.com/dnSpyEx/dnSpy)
- Repos studied: [redseiko/ComfyMods](https://github.com/redseiko/ComfyMods) · [OrianaVenture/Randy_Vapok_ValheimMods](https://github.com/OrianaVenture/Randy_Vapok_ValheimMods) · [RandyKnapp/ValheimMods](https://github.com/RandyKnapp/ValheimMods) · [AzumattDev/ItemManagerModTemplate](https://github.com/AzumattDev/ItemManagerModTemplate) · [AzumattDev repos](https://github.com/AzumattDev?tab=repositories) · [ChebsValheimLibrary publicization notes](https://jpw1991.github.io/chebs-valheim-library/articles/publicization.html)
- Packaging/tools: [thunderstore-cli](https://github.com/thunderstore-io/thunderstore-cli) ([wiki](https://github.com/thunderstore-io/thunderstore-cli/wiki)) · [ecosystem-schema](https://github.com/thunderstore-io/ecosystem-schema) · [r2modmanPlus](https://github.com/ebkr/r2modmanPlus) ([package structure](https://github.com/ebkr/r2modmanPlus/wiki/Structuring-your-Thunderstore-package)) · [Nexus file submission guidelines](https://help.nexusmods.com/article/28-file-submission-guidelines) · [Nexus game-valheim (archived)](https://github.com/Nexus-Mods/game-valheim) · [Vortex Valheim guide](https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Users-GameGuides-Modding-Valheim-with-Vortex)
- Valheim: [Patch 0.221.10 (Unity 6)](https://patched.gg/games/valheim/patch-022110-five-years-of-valheim) · [1.0 live](https://berrybyte.net/blog/valheim-1-0-live-server-update) · [Patch 1.0.16](https://www.valheimgame.com/news/patch-1-0-16/) · [Asset bundle modding FAQ 0.217.40](https://www.valheimgame.com/support/modding-faq-for-the-asset-bundle-update-0-217-40/) · [Valheim Modding Wiki](https://github.com/Valheim-Modding/Wiki/wiki) ([Best Practices](https://github.com/Valheim-Modding/Wiki/wiki/Best-Practices), [Debugging via IDE](https://github.com/Valheim-Modding/Wiki/wiki/Debugging-Plugins-via-IDE), [Xbox-compatible mods](https://github.com/Valheim-Modding/Wiki/wiki/Xbox-Compatible-Mods), [1.0 FAQ](https://github.com/Valheim-Modding/Wiki/wiki/Valheim-1.0-FAQ))
- Local: `E:\SteamLibrary\steamapps\common\Valheim` (file versions, `changelog.txt`, `doorstop_config.ini`, `BepInEx\config\BepInEx.cfg`), `D:\Gits\ValheimMods\.ref\decompiled\*` (`Version`, `ZInput`, `InputDefinition`, `PieceTable`, `Piece`, `Game`, `Achievements`, `FejdStartup`, `Terminal`).
