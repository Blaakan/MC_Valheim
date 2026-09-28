## Installation

1. Install [BepInExPack_Valheim]({{BEPINEX_LINK}}) (5.4.2350 or newer). For a manual install, do not extract its zip as-is: it contains a `BepInExPack_Valheim` folder, and you copy the **contents** of that folder (`BepInEx`, `winhttp.dll`, `doorstop_config.ini`, ...) next to `valheim.exe`. Start the game once: a `BepInEx/LogOutput.log` file should now exist.
2. Open your Valheim folder: in Steam, right-click Valheim > Manage > Browse local files (the folder with `valheim.exe`).
3. Extract the downloaded zip **into that Valheim folder** (not into `BepInEx/plugins`). The result is shown below.
4. Start the game. In the main menu or the pause menu (Esc), click **MC Mods** (top right) to check that {{WHAT}} active.

```
Valheim/
  valheim.exe
  BepInEx/
    plugins/
      {{TREE}}
```

{{VORTEX}}

{{SERVER}}

## Updating

{{UPDATE}} Your settings are kept: they live in `BepInEx/config/`.

## Uninstalling

Delete `{{FOLDER}}`. Your settings stay in `BepInEx/config/{{CONFIG}}`; delete that too for a clean removal.
