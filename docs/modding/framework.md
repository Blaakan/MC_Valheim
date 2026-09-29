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
- `./tools/Test-InWorld.ps1`: loads a throwaway single-player world and runs every in-world self test the mods
  register (Debug builds). See below.

### In-world self-tests

`./tools/Test-InWorld.ps1 [-Mod <names>] [-Only <tests>] [-KeepRunning] [-TimeoutSec 900] [-TestTimeoutSec 120] [-NoBuild]`
builds and deploys the mods (all, or those matching `-Mod`) and the world probe (`tests/Probes/Core.Probe.World`),
launches the game through `Start-Game.ps1` and waits. Mods already deployed in the game folder load too, so their
tests run as well: every deployed mod's tests run unless `-Only` picks tests by name. Inside the game the probe:

1. **Keeps your saves out of reach.** At plugin load, before the main menu reads anything, it points all save data
   at `%TEMP%\MC_Valheim_InWorld\<yyyyMMdd-HHmmss>\saves` (`Utils.SetSaveDataPath`) and turns Steam Cloud saves off
   for that game session (the same state as a player who disabled Steam Cloud). At the main menu it proves it in the
   log (every save path, every save file the game knows) and refuses to start a world otherwise. The main-menu
   preferences its button presses write (last character, last world, crossplay) are put back at once.
2. **Starts the world like a player.** It creates (or reuses) the local character **MCProbe** and the local world
   **MCProbe** (fixed seed `MCProbe01`) and starts it through the methods the main-menu buttons call, as a private,
   closed server (no PlayFab login needed). The first-spawn intro is skipped: the player appears at the spawn stones.
3. **Prepares the player.** When the player has spawned and the area around it has loaded (plus 8 s), it turns
   **god mode** on (`Player.SetGodMode`, not a console command) and sets the world modifier **StaminaRate 0** (no
   stamina use at all).
4. **Runs the tests.** `probe.baseline` (player exists, position, biome, time of day, screenshots of the world and of
   the inventory), `probe.runner` (checks the test runner itself), then every registered test in registration order,
   each with a timeout. It logs `[selftest] BEGIN <name> (<mod GUID>)` / `[selftest] END <name>: passed|FAILED`
   (tests that `-Only` skips are listed in `NOTE probe: filtered out: ...`), then
   `[selftest] DONE pass=<tests passed> fail=<tests failed> tests=<tests run>`, and quits the game.

The script prints every test's `PASS` / `FAIL` / `NOTE` / `SHOT` lines grouped by test, how many tests each mod ran,
plus the errors that come from our mods (like the smoke test). It passes only when `DONE` was logged with `fail=0`, no
error came from our mods, and at least one mod test ran: a run where only `probe.baseline` and `probe.runner` ran
fails. With `-Only`, every piece must match a test; with `-Mod`, a test of the selected mods must have run, and each
selected mod that has self tests must have run one (unless `-Only` filtered them out). When a mod ran no test,
the script says why: it has no self tests, `-Only` filtered them out, it did not load, a Release build is deployed, or
it is turned off in its `.cfg` (`Enabled = false`).

It always removes the probe from the game folder and deletes the run's saves; the screenshots (`shots\`) and a copy
of the log stay in the run folder (the 10 newest run folders are kept). Tests should not write settings (force them in
memory, see below), but a test or a tester can still change `BepInEx/config/MC.*.cfg` during a run, so before launch the script copies those files to the run folder's
`config-backup\`, and once the game has exited it puts them back byte for byte (a config file created during the run
is deleted). It does this on every path, also when the run is cut short by Ctrl+C, the timeout or an error, and it
names each file it had to put back. If the script itself is killed (for example its console window is closed),
nothing is put back: the copies wait in `config-backup\`, and `config-backup\pending.txt` (deleted only when every
file went back) makes the next run stop and say so, instead of backing up the changed files as the new "before".
A new world is generated on every run: about 7 s to the main menu, 25-30 s from
world start to spawn, about 1 minute from launch to quit plus the tests; a clean start is worth it. With
`-KeepRunning` the game stays in the world after the tests and the clean-up (probe, saves, config files) happens when
you close it; `cleanup.log` in the run folder then lists the config files that were put back. A probe left in the
plugins folder by accident does nothing: it is idle unless the script launched the game (environment variable
`MC_INWORLD_DIR`).

**Writing a test** (Debug builds only; API in `src/Shared/SelfTest.cs`). Put the code in a file wrapped in
`#if DEBUG`, register in `OnActivated`, unregister in `OnDeactivated`:

```csharp
#if DEBUG
internal static class CrossbowSelfTests
{
    private const string Name = "crossbow.keeps-bolt";

    internal static void Register() => SelfTest.Register(Name, Run);
    internal static void Unregister() => SelfTest.Unregister(Name);

    private static IEnumerator Run()
    {
        var player = Player.m_localPlayer;
        GameObject spawned = null;
        try
        {
            spawned = Object.Instantiate(ZNetScene.instance.GetPrefab("CrossbowArbalest"), player.transform.position, Quaternion.identity);
            yield return new WaitForSeconds(1f);
            // ... act, then check:
            SelfTest.Pass(Name, "bolt kept after putting the crossbow away");
            SelfTest.Screenshot(Name, "after");
            yield return null;
            yield return null;
        }
        finally
        {
            if (spawned != null)
            {
                ZNetScene.instance.Destroy(spawned);
            }
        }
    }
}
#endif
```

- Name tests `<modshortname>.<what>`. A test must report at least one `SelfTest.Pass` or `SelfTest.Fail`, or it
  fails. `SelfTest.Note` adds context. `SelfTest.Screenshot(name, label)` saves `<name>__<label>.png`; yield two
  frames before changing the screen, and take at most one screenshot per frame.
- Yield `null`, a nested `IEnumerator`, `WaitForSeconds`, `WaitForSecondsRealtime`, `WaitUntil` / `WaitWhile`, an
  `AsyncOperation`, `WaitForEndOfFrame` or `WaitForFixedUpdate`. Do not yield a started `Coroutine`: the timeout
  cannot stop it.
- Each test has a timeout (120 s by default). When a test times out or throws, the runner closes it, so its
  `try / finally` blocks still run: put the clean-up there. A test must leave the world as it found it (destroy what
  it spawns, close windows, put back the time and global keys it changed). **Never set a `ConfigEntry.Value` in a
  test**: BepInEx saves it to the player's real `.cfg` at once, and the script puts config files back only after the
  whole run. Give the mod a Debug-only in-memory override read through one accessor (Sleep Through the Day:
  `Plugin.ReadDaySettings`; Encyclopedia: its display settings) and clear it in `finally`. Between tests the probe
  closes an inventory left open and turns god mode back on, and says so in a `NOTE`.
- The player is in god mode: its health never drops below 1, and creatures it hurts are marked as cheated (vanilla
  `Character.Damage`), which matters for kill statistics. A test that needs a normal player turns god mode off with
  `Player.SetGodMode(false)` and back on in its `finally`. Likewise a test that measures stamina use removes the
  modifier with `ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.StaminaRate)` and sets it back with
  `ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, 0f)`.
- Tests must not depend on each other or on their order; other mods' tests run in the same world. The player starts
  at the spawn stones in the Meadows, shortly before dawn of day 1 (it is dark): a test that needs daylight or a
  given hour sets the time itself and puts it back.

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
- `ItemKinds` (`src/Shared/ItemKinds.cs`, not framework but shared by every mod): `ItemKinds.Classify(SharedData)`
  returns an `ItemKind` (Weapon, Ammo, Shield, each armor slot, Utility, Trinket, Tool, SkillTool, Torch, Food, Potion,
  Material, Fish, Trophy, Misc, Other) from the item type, skill, animation and food values. Any mod that sorts or
  groups items "by type" builds its own groups and labels on top of it, so the same item lands in the same group in
  every MC mod (Crafting Search and Sort, Sort Chest). Pure enum compares, no state, no allocation.
- Mod namespace is not the GUID: `MC.<Category>.<Feature without dots>Mod` (e.g. `MC.Combat.CrossbowStaysLoadedMod`),
  because a namespace segment like `Inventory` would hide the game class of the same name.
- The panel blocks clicks to the game's buttons behind it with an invisible top-most canvas while it is open.
- Install layout: direct/Nexus installs put every mod in `BepInEx/plugins/MC_Valheim/<Category>/<GUID>/`; mod
  managers use their own `plugins/MC-<Package>/` folders. BepInEx loads both (it scans `plugins/` recursively).
  Mod code must find its own files through `ModFolder` (the DLL's folder), never a fixed path.
