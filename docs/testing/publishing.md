# Nexus packages — install tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
Run before the first public release, and again whenever the package layout changes (tools/Package-Mod.ps1).
Use a clean install: rename your `BepInEx/plugins/MC_Valheim` folder first, and restore it afterwards.
The 1.0 release publishes only the pack page: P01 and P05 (single mod zips) can wait for the first single mod page.

## Nexus zips (layout v1: BepInEx/plugins/MC_Valheim/<Category>/<GUID>/)

- [ ] **P01 Single mod, manual:** extract `dist/nexus/<GUID>/<version>/<Package>-<version>.zip` into the Valheim folder.
  Expected: `BepInEx/plugins/MC_Valheim/<Category>/<GUID>/` with the dll, README.md, CHANGELOG.md; the mod shows as
  Active in the MC Mods panel; nothing new next to `valheim.exe`.
- [ ] **P02 Pack, manual:** extract `dist/nexus/_Pack/<version>/MC_Valheim-AllMods-<version>.zip` into the Valheim
  folder. Expected: every shipped mod under `MC_Valheim/<Category>/` (not the ones with `ModShip` false, such as
  Cheated Tag Remover), `MC_Valheim/README.md`, all Active in MC Mods.
- [ ] **P03 Update over the top:** extract a newer zip over an existing install. Expected: the new build id shows
  in MC Mods; settings in `BepInEx/config/` unchanged.
- [ ] **P04 Uninstall:** delete the mod folder. Expected: game starts without it, no errors in the log.
- [ ] **P05 Vortex, single mod:** install the single-mod zip with Vortex ("Add from file" or Mod Manager Download).
  Expected (from the extension's code): Vortex deploys it to `BepInEx/plugins/<GUID>/` (flattened), and the mod
  is Active. Note what Vortex actually did.
- [ ] **P07 Pack page:** on Nexus the pack's file has "mod manager download" turned off, and the page and file
  description tell Vortex users to install the pack by hand (once every included mod has its own page: to install
  the single mods), never pointing to a single mod page that does not exist.
- [ ] **P06 Description preview:** paste `description.bbcode.txt` in the Nexus page editor preview. Expected:
  headings, lists, bold, links and code render; no raw BBCode or Markdown visible.
