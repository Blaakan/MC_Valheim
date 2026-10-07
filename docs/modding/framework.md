# The MC mod framework

Every MC mod is built on a small shared framework (`src/Shared/Framework`). It is compiled into each mod, so there is
no extra "core" download and no mod depends on another one just to work. It gives every mod the same behaviour:

- **each feature can be turned on or off on its own, live, without restarting;**
- **a feature whose dependency is missing or turned off goes inactive, says why, and never crashes;**
- **a feature that needs the server switches itself off on servers that don't have it;**
- **everything is visible in one place: the in-game _MC Mods_ panel** (or a configuration manager's window, F1, when
  one is installed: the panel then hides its button).

## Turning features on and off

Three equivalent ways, all writing to the same file, `BepInEx/config/MC.<Category>.<System>.<Feature>.cfg`:

| Where | How |
|---|---|
| In game | Main menu or pause menu (Esc): click **MC Mods** (top-right). Tick or untick a feature. With a configuration manager installed this button is hidden: use its window (F1, see below). |
| Mod manager | r2modman / Thunderstore Mod Manager → Config editor → the mod's file → `General.Enabled`. |
| By hand | Edit `Enabled = true/false` in the mod's `.cfg`. The game picks the change up while running. |

A configuration manager (F1, see [ConfigurationManager](#configurationmanager) below) also shows every setting,
including `Enabled` and the read-only `Status`.

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
| Inactive: <reason given by the mod> | The mod cannot run on this game even though it is turned on, usually because another installed mod does the same job (for example Dual Wielding next to another dual wield mod). The mod names the reason. On a server that requires the mod, this counts like having it turned off. |
| Inactive: the server does not have this mod | The feature needs the server to have it too. |
| Inactive: the server has this mod turned off | The server has it, but it is off (or broken) there. Follows the server live. |
| Inactive: the server has version A, you have B... | Client and server network versions differ (`ModNetworkVersion`). |
| Runs on the server/host only... | A server-side feature while you are a client of someone else's server. Informational (grey), not a problem. |
| Inactive: single-player only | A single-player feature while other players are connected. |
| Error: could not start | Patching failed (usually a game update changed something). Details in `BepInEx/LogOutput.log`. The feature stays off until you toggle it. |

When you spawn in a world, one short message lists features that are inactive for a reason you did not choose
(shown again only when that list changes, and not while the HUD is hidden).

## ConfigurationManager

A configuration manager is an optional mod that lists every BepInEx setting in a window (F1). Nothing in MC_Valheim
depends on it: it reads each mod's BepInEx config file like any other, and changing a setting there is the same as
editing the `.cfg` file: MC mods apply it at once, without a restart.

**The MC Mods button is hidden when a configuration manager is installed** (user decision 2026-10-02): its window
already lists every MC mod with `Enabled` and `Status`, so there is one place for everything. The panel recognises the
three builds below by plugin ID, and any other plugin whose name ends with "Configuration Manager"; a manager that
failed to load does not count. The spawn notice about inactive features stays and points to F1 instead. What only the
panel shows (build id, who needs the mod, multiplayer notes, the "Turn on X" button for a dependency) is in each mod's
README; a dependency that is off is named in the mod's `Status`. Remove the manager and the button comes back.

Three builds exist for Valheim (checked 2026-10-02, from their source code):

| Build | Where | On Valheim 1.0 |
|---|---|---|
| Valheim Configuration Manager (shudnal) | Nexus mod 2746, Thunderstore `shudnal/ConfigurationManager` | Maintained (1.1.23, 2026-10-02). The one to recommend. |
| Configuration Manager (aedenthorn) | Nexus mod 740 | Version 0.5.0 from 2021, no longer updated by its author. Its main-menu button looks for an older menu and does not appear, and its console hook was built for the old console: on 1.0 it can stop the F5 console from running commands (expected from its code, not yet seen in game). cjayride's fork (Thunderstore `cjayride/ConfigurationManager` 0.6.2, same plugin) fixes these. |
| BepInEx ConfigurationManager (upstream; Azumatt's Thunderstore upload) | Thunderstore `Azumatt/Official_BepInEx_ConfigurationManager` | The upload is deprecated and built for the pre-1.0 BepInEx pack. |

What MC mods do so all three show their settings correctly (new mods follow every point):

- Each setting carries a `ConfigurationManagerAttributes` object as its **first** tag (the managers stop reading tags
  at the first one they do not know) with an `Order`: inside a section, settings are listed in the order the mod's
  README gives them (higher `Order` first). Without it a manager sorts them by name.
- `Status` is drawn as plain text with no Reset button. `ReadOnly` alone only greys it out in shudnal's build; the
  others still draw an edit box (typing in it does nothing).
- Whole-number settings whose range is 0..100 or 1..100 but that are not percentages (counts, levels) set
  `ShowRangeAsPercent = false`; aedenthorn's and the upstream build otherwise show them as a percentage slider with no
  number box, and do the same with float ranges 0..1 (shudnal's build never turns percent on by itself).
- Expensive reactions to a change (rebuilds, scene-wide updates, sending the rules to players) should wait until the
  value stops changing (about 0.5 s; see the `ServerRules` of Weapon Moveset or Deep North Awakening): a slider being
  dragged sets the value (and rewrites the `.cfg` file) every frame, and a text field at every keystroke. Not yet done
  in Switchable Lights, Sneak Ambush, Forge Idol Upgrades, Dual Wielding and Breeding Star Inheritance: they send
  their rules again at the next network update after each change (follow-up).
- Key settings: a manager can store any key, including keys the game cannot read (F13 and up, some symbol keys,
  Mouse5/6). Mods check the key when it changes and log a warning instead of failing every frame.
- Never `IsAdvanced`: shudnal's and the upstream build hide advanced settings by default.

Differences between the builds that players can see:

- **Section order:** aedenthorn's build (Nexus 740) sorts sections alphabetically, so `General` (`Enabled`, `Status`)
  is not always first. shudnal's and the upstream build keep the mod's order. The `.cfg` file itself always lists
  sections alphabetically.
- **Game keys while the window is open:** aedenthorn's and the upstream build do not block the game's keys (the game
  reads keys through the new Input System, which their blocking does not reach), so a letter typed in their text
  fields also reaches the game, MC hotkeys included (Y for Trinkets on Demand, H for Dual Wielding). shudnal's build
  blocks game input while its window is open.
- **Key settings stored as a plain key** (`KeyCode`): aedenthorn's build shows them as a long drop-down list of every
  key; the others let you press the key.
- **Two managers at once:** aedenthorn's and shudnal's builds do not detect each other: both open on F1. Install one.

## Content that stays registered

Most patches come and go with the live toggle. A mod that adds game content (an item, a networked prefab, an RPC
handler) cannot work that way: an item whose prefab is unknown is dropped from inventories when a character loads,
and the server deletes world objects whose prefab it does not know. So a patch class marked `[AlwaysOnPatch]` (next
to its `[HarmonyPatch]`) is applied once when the game starts, under its own Harmony id (`<GUID>.alwayson`), and
stays applied while the feature is turned off; only quitting the game removes it. Such code must check the feature
state itself and must not rely on config entries being bound. Everything else about the content (its recipe, what
it does when used) follows the toggle as usual. If these patches cannot be applied (usually after a game update),
the feature stays off for the session with an error status.

Uninstalling a mod that adds items still removes those items from inventories: say so in the mod's README.

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
  `NetworkGate.PeerHasMod(peer)`. The player's game also says whether its own side is ready (the feature is turned
  on, its dependencies are active and it did not fail to start) and tells the server again whenever that changes
  while connected. Server-side mod code can ask `NetworkGate.PeerCompatible(peer)` (the player has the mod, the same
  network version, and has not turned it off), `NetworkGate.PeerProblem(peer)` (a short reason for logs, or null),
  `NetworkGate.PeerNetworkVersion(peer)` and `NetworkGate.PeerReady(peer)` (null when unknown: a copy of the mod built
  before this check says nothing, and counts as compatible), and subscribe to `NetworkGate.PeerStateChanged` to
  check a player again when they turn the mod on or off. Gameplay mods use this to refuse players whose game would
  not play by the server's rules (see the `PlayerCheck` of the Combat mods).
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
- `./tools/Test-Multiplayer.ps1`: starts a dedicated server and a client on this PC, the client joins, and the
  multiplayer self tests run (Debug builds). See "Multiplayer self-tests" below.

### In-world self-tests

`./tools/Test-InWorld.ps1 [-Mod <names>] [-Only <tests>] [-KeepRunning] [-TimeoutSec 900] [-TestTimeoutSec 120] [-NoBuild] [-AsConfigured]`

MC mods turned off in their `.cfg` (`Enabled = false`) are turned on for the run (their files are put back afterwards
like every other config change), so their tests run too; `-AsConfigured` keeps them off.
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

### Multiplayer self-tests

`./tools/Test-Multiplayer.ps1 [-Scenario all|modded|vanilla-server|vanilla-client|open-server|each-off] [-Only <tests>] [-NoModBuild] [-KeepRunning] [-Port 2466] [-TimeoutSec 2400] [-TestTimeoutSec 120]`
runs a real dedicated server and a game client on this PC. Nothing of yours is changed: the server runs from a copy
of the Steam dedicated server install (`%LOCALAPPDATA%\MC_Valheim\server`, refreshed every run, plus the Doorstop
loader), and server and client each load BepInEx from their own folder in the run folder
(`%TEMP%\MC_Valheim_MP\<yyyyMMdd-HHmmss>\<scenario>\server|client\BepInEx`, through Doorstop's
`--doorstop-target-assembly`), with only MC mods, the probes and fresh default configs. Tests may therefore change
settings freely (also `ConfigEntry.Value`) in a multiplayer test. The client's saves are isolated like in
`Test-InWorld.ps1`; the server's go to the run folder (`-savedir`, world `MCProbeMP`, shared by the scenarios of one
run). The game must be closed (the script starts its own client). All five scenarios take about 12 minutes without
mod tests. `-NoModBuild` reuses the mod DLLs built by the newest earlier run (the probes are always built): use it to
re-run a scenario while mod code is being edited.

The client runs the world probe (`tests/Probes/Core.Probe.World`), which joins the server instead of starting a world;
the server runs the server probe (`tests/Probes/Core.Probe.Server`). Scenarios:

| Scenario | Server | Client | Checks |
|---|---|---|---|
| `modded` | every MC mod | every MC mod | `probe.mp.baseline` (every Both mod active on both sides), every mod test registered for `modded`, then `probe.mp.server-toggle` (the server turns each Both mod off and on: the client follows live) |
| `vanilla-server` | probe only | every MC mod | `probe.mp.baseline`: every Both mod `ServerMissing`, every client mod active |
| `vanilla-client` | every MC mod | probe only | `probe.mp.refused`: the server refuses the player ("Incompatible version"); the script checks a mod logged "Refused" |
| `open-server` | every MC mod, `AllowPlayersWithoutMod = true` | probe only | the player stays in; the script checks every refusing mod logged that it let the player in |
| `each-off` | every MC mod | every MC mod | one `probe.mp.off.<GUID>` per Both mod: turned off on the client while in, the server refuses (live re-check), and again when joining with it off; mods without a player check let the player stay, and so do mods with the older player check (`NetworkGate.PeerHasMod` instead of `PeerCompatible`: it only refuses a player who does not have the mod), which the script and the test result name |

**Writing a multiplayer test** (Debug builds only; same file as the mod's other self tests):

```csharp
SelfTest.RegisterMultiplayer("dive.mp.rules", SelfTest.Modded, RunRulesClient); // client side, in OnActivated
SelfTest.RegisterServerStep("dive.mp.server-rules", ServerRules);              // server half, in OnActivated too
// OnDeactivated: SelfTest.UnregisterMultiplayer(...), SelfTest.UnregisterServerStep(...)

private static IEnumerator RunRulesClient()
{
    var reply = new SelfTest.ServerReply();
    yield return SelfTest.CallServer("dive.mp.server-rules", "", reply); // runs ServerRules on the server
    if (!reply.Answered || !reply.Ok) { SelfTest.Fail(Name, reply.ToString()); yield break; }
    // compare reply.Detail with what the client received ...
}

private static IEnumerator ServerRules(string arg, object[] reply)
{
    yield return null;
    SelfTest.Answer(reply, true, ServerRules.Current.Describe()); // what the client test gets as reply.Detail
}
```

- A client test runs only in the scenario it registered for, after `probe.mp.baseline`, with the same timeout, filter
  (`-Only`) and Pass / Fail / Note / Screenshot rules as an in-world test. `SelfTest.Scenario` tells the scenario
  (`""` in a single-player run), `SelfTest.IsMultiplayerRun` whether this is one.
- A server step gets the argument string and must call `SelfTest.Answer(reply, ok, detail)`; it runs with a timeout
  (70 % of the test timeout) on the server. The client waits for the answer up to 75 % of the test timeout.
  Server-side `Note`/`Pass`/`Fail` lines go to the server's log; the script prints them with an `S` prefix and a
  server `FAIL` fails the scenario.
- The server probe has one step of its own: `probe.set-enabled` with `"<GUID>=on"` or `"<GUID>=off"`.
- The player is in god mode with stamina rate 0 like in a single-player run, on a world with a random seed.
- Before every test the probe checks that the player is still in the server's world. When the test before left it
  (refused, kicked, player object gone), the probe logs out if needed, joins again from the main menu and waits for
  the spawn, so one test cannot make the following ones fail.
- In a multiplayer run the probe makes `Utils.GenerateUID` return a new value on every call. Without that, a client
  that leaves and joins again within one game process got the same session id each time; the server keeps the ids of
  destroyed objects (`ZDOMan.m_deadZDOs`) and destroys a new object that reuses one, so the rejoined player vanished
  right after spawning.

## Internals (for mod authors)

- `ModPlugin` (base class of every `Plugin`): owns `Enabled`, `Status`, the Harmony instance and the life cycle.
  Mods override `BindConfig`, `OnActivated`, `OnDeactivated` (and optionally `ApplyPatches` and `LocalBlocker`). They
  must not define `Awake`, `Start`, `Update` or `OnDestroy`. The default `ApplyPatches` patches every `[HarmonyPatch]`
  class except the `[AlwaysOnPatch]` ones, which `Awake` applies once under `<GUID>.alwayson` (see "Content that stays
  registered"). `LocalBlocker()` returns a status sentence when the mod cannot run on this game (state `Conflict`,
  shown like a missing dependency, reported to the server as "not ready"), or null; the framework asks it at every
  refresh, never per frame.
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
