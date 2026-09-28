# Nexus packages — install tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
Run before the first public release, and again whenever the package layout changes (tools/Package-Mod.ps1).
Use a clean install: rename your `BepInEx/plugins/MC_Valheim` folder first, and restore it afterwards.

## Nexus zips (layout v1: BepInEx/plugins/MC_Valheim/<Category>/<GUID>/)

- [ ] **P01 Single mod, manual:** extract `dist/nexus/<GUID>/<version>/<Package>-<version>.zip` into the Valheim folder.
  Expected: `BepInEx/plugins/MC_Valheim/<Category>/<GUID>/` with the dll, README.md, CHANGELOG.md; the mod shows as
  Active in the MC Mods panel; nothing new next to `valheim.exe`.
- [ ] **P02 Pack, manual:** extract `dist/nexus/_Pack/<version>/MC_Valheim-AllMods-<version>.zip` into the Valheim
  folder. Expected: every mod under `MC_Valheim/<Category>/`, `MC_Valheim/README.md`, all Active in MC Mods.
- [ ] **P03 Update over the top:** extract a newer zip over an existing install. Expected: the new build id shows
  in MC Mods; settings in `BepInEx/config/` unchanged.
- [ ] **P04 Uninstall:** delete the mod folder. Expected: game starts without it, no errors in the log.
- [ ] **P05 Vortex:** install the single-mod zip with Vortex ("Add from file" or Mod Manager Download). Expected:
  same folder layout as P01 and the mod is Active. Note what Vortex did if not.
- [ ] **P06 Description preview:** paste `description.bbcode.txt` in the Nexus page editor preview. Expected:
  headings, lists, bold, links and code render; no raw BBCode or Markdown visible.
