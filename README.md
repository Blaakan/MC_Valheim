# ValheimMods

A collection of Valheim 1.0 mods by **MC**: quality-of-life improvements, revamps of existing mechanics, and new
systems that build on the vanilla experience. Built on BepInEx 5 and HarmonyX.

| Category | What it covers |
|---|---|
| Combat | weapon movesets, combat skills and mechanics, adrenaline/trinkets, mob AI, boss fights |
| Exploration | navigation, sailing, swimming, points of interest, biomes, world interaction |
| Farming | gathering, planting, taming, breeding, fishing |
| Cooking | food, recipes, cooking stations and gear |
| Building | base building, placement, fuel/lighting, wards |
| Crafting | recipes, repair, upgrades, trinkets/trophies, new items |
| UX | inventory/chest UI, search and sorting, notifications, pings |

Each mod is tagged with a scope: **QoL** (improves a system without changing its rules), **Revamp** (changes a core
mechanic) or **New** (adds a new system or content).

## Mods

| Mod | Category | Scope | Multiplayer | Status |
|---|---|---|---|---|
| [Crossbow Stays Loaded](src/Combat/Crossbow.StaysLoaded) | Combat | QoL | client-side | in development |

## Why one repository

All mods live in one repository and one solution, but each mod still builds to its own DLL and ships as its own
Thunderstore/Nexus package, so players install only what they want. One repository was chosen because:

- many planned features overlap (a magic theme touches Combat, Crafting, Building and Farming), and cross-cutting
  refactors and shared helpers are simpler when everything builds together;
- one build configuration, one game-reference setup and one set of tools serve every mod;
- a game update can be checked against every mod at once (`tools/Update-GameRefs.ps1` lists the patched classes
  that changed).

Shared helpers in `src/Shared` are compiled into each mod, so mods have no runtime dependency on each other.
When a feature needs to be shared at runtime by several mods, it becomes a dedicated `Core` mod.

## Development

Requirements: Windows, Valheim with BepInExPack_Valheim installed, .NET SDK 9.0.300+, `ilspycmd`
(`dotnet tool install -g ilspycmd`), git.

```powershell
./tools/Setup.ps1                 # find the game, write Local.props, decompile game reference source
dotnet build ValheimMods.slnx     # Debug builds copy each mod into <Valheim>/BepInEx/plugins/<GUID>/
./tools/Test-Smoke.ps1            # launch the game, check every mod loads and patches cleanly, close it
./tools/Package-Mod.ps1           # Thunderstore + Nexus zips in dist/
```

See [CLAUDE.md](CLAUDE.md) for conventions, naming and the full command list, and `docs/` for the game-systems
knowledge base, existing-mod research and per-mod design documents.

Game code is never committed: the decompiled reference source lives in the git-ignored `.ref/` folder and is
regenerated locally from your own game install.
