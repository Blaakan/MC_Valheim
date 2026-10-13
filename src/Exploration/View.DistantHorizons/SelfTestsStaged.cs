#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. Staged self tests (world probe, tools/Test-InWorld.ps1: real log out to main menu, real game
// restart with every MC mod Enabled = false). One small test per TESTING.md item, so one can be fixed alone:
//   horizons.relog.far-terrain    T17: far terrain up; real log out: at main menu nothing of mine left (no manager, no
//                                 root object, no camera buffer, nobody listen to settings), mod still active; same
//                                 world loaded again: vanilla 3x3 never made, one manager of each kind, one root, paint
//                                 buffer, far plane raised, far ground whole and single around camera, far sea painted,
//                                 far objects drawn again (when some were before), no error of mine over the whole relog
//   horizons.relog.load-cover     T26 "right after loading a world": watcher started at main menu look at every frame
//                                 from the moment the player stand in the loaded world: coarse far tiles keep
//                                 ground-level vertices inside loaded real zones at most 2 s, no far vertex above real
//                                 ground within 200 m meanwhile; screenshot of that moment when it happen
//   horizons.disabled.vanilla     T13: first game note the game's own far plane; second game (Enabled = false from the
//                                 start): registry say Disabled + "Off (disabled in settings)." (what MC Mods panel
//                                 print), vanilla 3x3 with meshes that end about 1.2 km from the camera, no manager, no
//                                 root, no buffer, no live patch, no "dh" command, real zones plain, game's distant
//                                 water plane, game's far plane, clear weather fog not thinned
//   horizons.disabled.session-log T19: end of first game (after every test and every real relog): log watch proven
//                                 to hear, no error line from or about me since the plugin loaded, no guarded
//                                 failure; second game: the same with the mod off from the start
//   horizons.disabled.checked     T13, T19: second game only (no first-game part): the second-game parts of the two
//                                 tests above ran to their last check. Their first parts must pass alone in the first
//                                 game, so their name can read "passed" when the second game never judged them
// Relog tests register in OnActivated (SelfTests.Register), disabled-start tests from BindConfig
// (SelfTests.RegisterAtStart): in second game OnActivated never run.
internal static class StagedTests
{
    private const string RelogName = "horizons.relog.far-terrain";
    private const string LoadCoverName = "horizons.relog.load-cover";
    private const string DisabledName = "horizons.disabled.vanilla";
    private const string SessionLogName = "horizons.disabled.session-log";
    private const string CheckedName = "horizons.disabled.checked";

    // Second game only: the second-game part of the two tests above ran to its last check in this game process.
    private static bool _vanillaChecked;
    private static bool _sessionChecked;

    private const string FarClipKey = DisabledName + ".farclip";
    private const string OffStatus = "Off (disabled in settings).";
    // Same limit as horizons.attach-cover (README "Good to know": up to about 2 seconds).
    private const float CoverLimit = 2f;
    private const float WatchSeconds = 12f;

    internal static void Register()
    {
        SelfTest.RegisterRelog(RelogName, RelogBefore, RelogMenu, RelogAfter);
        SelfTest.RegisterRelog(LoadCoverName, LoadCoverBefore, LoadCoverMenu, LoadCoverAfter);
    }

    internal static void Unregister()
    {
        SelfTest.UnregisterRelog(RelogName);
        SelfTest.UnregisterRelog(LoadCoverName);
    }

    // BindConfig (never unregistered: second game need them while mod is off).
    internal static void RegisterAtStart()
    {
        SelfTest.RegisterDisabledStart(DisabledName, DisabledLeave, DisabledCheck);
        SelfTest.RegisterDisabledStart(SessionLogName, SessionLeave, SessionCheck);
        // No first-game part: this one only ever ends in the second game. The two above end in both games under one
        // name, and their first part must report a pass: when the second game stops before their second part (crash,
        // script timeout), the first part's pass is all a result reader sees. A tick that needs the second game must
        // list this test too.
        SelfTest.RegisterDisabledStart(CheckedName, null, DisabledChecked);
    }

    private static IEnumerator DisabledChecked()
    {
        const string N = CheckedName;
        var c = new Kit.Checks(N);
        c.Check(SelfTest.Stage == SelfTest.DisabledStage, $"this is the game started with every MC mod turned off (stage '{SelfTest.Stage}')");
        c.Check(_vanillaChecked, $"the second-game part of {DisabledName} ran to its last check in this game");
        c.Check(_sessionChecked, $"the second-game part of {SessionLogName} ran to its last check in this game");
        c.Report("both disabled-start tests of the mod were really judged in the game started with the mod off");
        yield break;
    }

    // Player, camera, mod active, far terrain up? Else one failed check.
    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready, float timeout = 80f)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || EnvMan.instance == null || ZNet.instance == null)
        {
            c.Check(false, "no local player, camera, weather manager or network object");
            yield break;
        }
        if (Plugin.Instance == null || !Plugin.Instance.IsActive)
        {
            c.Check(false, "Distant Horizons is not active: " + (Plugin.Instance != null ? Plugin.Instance.StatusText : "no plugin"));
            yield break;
        }
        var up = new Kit.Box();
        yield return Kit.WaitTerrain(up, timeout);
        ready.Ok = c.Check(up.Ok, up.Detail);
    }

    // My command buffers on every camera alive now.
    private static int BuffersOnAnyCamera()
    {
        var n = 0;
        foreach (var cam in Camera.allCameras)
        {
            n += Kit.Buffers(cam, null);
        }
        return n;
    }

    // Guarded failures of my code this game (PatchGuard keep one line per site). Null = list not readable.
    private static List<string> Guarded()
    {
        var field = typeof(PatchGuard).GetField("Reported", BindingFlags.Static | BindingFlags.NonPublic);
        var reported = field != null ? field.GetValue(null) as HashSet<string> : null;
        return reported == null ? null : new List<string>(reported);
    }

    // ---------- horizons.relog.far-terrain (T17)

    private static int _relogErrors;
    // Things far object tiles drew before the log out (0 = far objects off in this config, or before part failed).
    private static int _relogObjects;

    // Things drawn by built far object tiles now (0 = no object manager).
    private static int FarObjects()
    {
        var om = DistantObjectManager.Instance;
        if (om == null)
        {
            return 0;
        }
        om.TestTotals(out _, out _, out var instances, out _, out _, out _);
        return instances;
    }

    private static IEnumerator RelogBefore()
    {
        const string N = RelogName;
        var c = new Kit.Checks(N);
        _relogErrors = ErrorWatch.Errors;
        _relogObjects = 0;
        var ready = new Kit.Box();
        yield return Ready(c, ready);
        if (!ready.Ok)
        {
            c.Report("");
            yield break;
        }
        var mgr = LodTerrainManager.Instance;
        c.Check(!TerrainLink.VanillaActive && mgr.TestSurfaceCount > 0, $"before the log out: far terrain drawn, vanilla 3x3 gone ({mgr.TestSurfaceCount} surface tiles)");
        c.Check(Plugin.Cfg.TestHandlerCount == 2, $"before the log out: the two managers listen to the settings ({Plugin.Cfg.TestHandlerCount})");
        // Far trees, rocks and buildings too: person who look at the horizon after a relog see when they are gone,
        // "manager runs" alone not. Me count them now, after part want them back.
        if (Plugin.Cfg.V(Plugin.Cfg.ObjectsEnabled))
        {
            var objects = new Kit.Box();
            yield return Kit.WaitUntil(() => FarObjects() > 0, 40f, objects);
            _relogObjects = FarObjects();
            c.Check(_relogObjects > 0, $"before the log out: far objects are drawn ({_relogObjects} on built far object tiles, waited {objects.Detail})");
        }
        else
        {
            c.Note("ObjectsEnabled is off in this config: far objects are not looked at over the log out");
        }
        c.Note("before the log out: " + mgr.GetStats());
        c.Report("far terrain up before the real log out");
    }

    private static IEnumerator RelogMenu()
    {
        const string N = RelogName;
        var c = new Kit.Checks(N);
        yield return Kit.Frames(2);
        c.Check(FejdStartup.instance != null && Player.m_localPlayer == null, "the game is at the main menu (no player)");
        c.Check(LodTerrainManager.Instance == null && DistantObjectManager.Instance == null && TerrainLink.Current == null,
            "at the main menu: both managers stopped and the link to the world's distant terrain is dropped");
        var lod = Kit.Roots("DistantHorizons_LOD");
        var objects = Kit.Roots("DistantHorizons_Objects");
        var rig = Kit.Roots("DH_ImpostorBakeRig");
        c.Check(lod == 0 && objects == 0 && rig == 0, $"at the main menu: no far tile, far object or bake object left ({lod}, {objects}, {rig})");
        var buffers = BuffersOnAnyCamera();
        c.Check(buffers == 0, $"at the main menu: no Distant Horizons buffer on any camera ({buffers})");
        c.Check(Plugin.Cfg.TestHandlerCount == 0, $"at the main menu: nothing listens to the settings ({Plugin.Cfg.TestHandlerCount})");
        c.Check(Plugin.Instance != null && Plugin.Instance.IsActive,
            $"at the main menu: the mod stays active ('{(Plugin.Instance != null ? Plugin.Instance.StatusText : "no plugin")}')");
        c.Check(ErrorWatch.Errors == _relogErrors, $"logging out: no error from Distant Horizons ({ErrorWatch.Errors - _relogErrors}: {ErrorWatch.FirstError})");
        c.Report("real log out: nothing of the far terrain left at the main menu");
    }

    private static IEnumerator RelogAfter()
    {
        const string N = RelogName;
        var c = new Kit.Checks(N);
        var ready = new Kit.Box();
        yield return Ready(c, ready, 60f);
        if (!ready.Ok)
        {
            c.Report("");
            yield break;
        }
        var cfg = Plugin.Cfg;
        var cam = Utils.GetMainCamera();
        var tl = TerrainLink.Current;
        c.Check(tl != null && tl.m_heightmaps.Count == 0, $"world loaded again: the vanilla 3x3 was not created ({(tl != null ? tl.m_heightmaps.Count : -1)} tiles)");
        c.Check(tl != null && tl.GetComponents<LodTerrainManager>().Length == 1 && tl.GetComponents<DistantObjectManager>().Length == 1,
            "…one terrain manager and one object manager");
        c.Check(Kit.Roots("DistantHorizons_LOD") == 1, $"…one far tile root ({Kit.Roots("DistantHorizons_LOD")})");
        c.Check(!Kit.Shrink || Kit.Buffers(cam, Kit.TerrainBuffer) == 1, $"…one paint buffer on the camera ({Kit.Buffers(cam, Kit.TerrainBuffer)})");
        c.Check(cfg.TestHandlerCount == 2, $"…the two managers listen to the settings again ({cfg.TestHandlerCount})");
        c.Check(Kit.LivePatches() == 5 && Terminal.commands.ContainsKey("dh"), $"…patches ({Kit.LivePatches()} of 5) and the dh console command are there");
        var first = LodTerrainManager.Instance;
        var originalFar = first != null ? first.TestOriginalFarClip : -1f;
        c.Check(!cfg.RaiseCameraFarClip.Value || (originalFar > 0f && cam.farClipPlane > originalFar),
            $"…the far plane is raised again ({cam.farClipPlane:0} m, the game's {originalFar:0} m)");

        var settled = new Kit.Box();
        yield return Kit.Settle(settled, 45f);
        var mgr = LodTerrainManager.Instance;
        if (c.Check(settled.Ok && mgr != null, "…the far terrain finishes streaming: " + settled.Detail))
        {
            var p = cam.transform.position;
            Kit.Coverage(mgr, p.x, p.z, 2560f, 128f, out var points, out var holes, out _, out var firstHole);
            mgr.TestSurfaceCut(out var overlaps, out var notShown);
            c.Check(points > 0 && holes == 0, $"…far ground under every point within 2.5 km of the camera ({holes} of {points} without, first {firstHole})");
            c.Check(overlaps == 0 && notShown == 0, $"…no far ground drawn twice and every surface tile shown ({overlaps} overlaps, {notShown} not shown)");
            if (Kit.Shrink && cfg.V(cfg.FarWater))
            {
                var sea = new Kit.Box();
                yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws == mgr.Water.BandCount, 10f, sea);
                c.Check(sea.Ok, $"…the far sea is painted again ({mgr.TestSeaDraws} of {mgr.Water.BandCount} bands)");
            }
            c.Note("after loading the world again: " + mgr.GetStats());
        }
        c.Check(DistantObjectManager.Instance != null, "…the far object manager runs");
        if (_relogObjects > 0)
        {
            var objects = new Kit.Box();
            yield return Kit.WaitUntil(() => FarObjects() > 0, 40f, objects);
            c.Check(objects.Ok, $"…far objects are drawn again ({FarObjects()} on built far object tiles after {objects.Detail}; {_relogObjects} before the log out)");
        }
        c.Check(ErrorWatch.Errors == _relogErrors, $"…no error from Distant Horizons over the whole log out and load ({ErrorWatch.Errors - _relogErrors}: {ErrorWatch.FirstError})");
        c.Report("real log out and load of the same world: the far terrain works again, one of everything, no error");
    }

    // ---------- horizons.relog.load-cover (T26, "right after loading a world")

    private sealed class LoadWatch
    {
        internal bool Running;
        internal bool Done;
        internal string Problem = "";
        internal float Spawned = -1f;
        internal int SurfaceAtSpawn;
        internal float FirstSurface = -1f;
        internal float LastOpen = -1f;
        internal int WorstVertices;
        internal int WorstLevel = -1;
        internal int Looks;
        internal int Compared;      // most far-tile vertices held against real ground at one look
        internal int ComparedLooks; // looks that had some to compare
        internal int CoverLooks;
        internal int CoverPoking;
        internal int WorstPoking;
        internal bool Shot;
    }

    private static LoadWatch _load;

    private static IEnumerator LoadCoverBefore()
    {
        const string N = LoadCoverName;
        var c = new Kit.Checks(N);
        var ready = new Kit.Box();
        yield return Ready(c, ready);
        if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the check needs it"))
        {
            ready.Ok = false;
        }
        if (!ready.Ok)
        {
            c.Report("");
            yield break;
        }
        var settled = new Kit.Box();
        yield return Kit.Settle(settled, 40f);
        var mgr = LodTerrainManager.Instance;
        var steady = -1;
        if (mgr != null)
        {
            mgr.TestCoarseOverLoaded(out _, out steady, out _);
        }
        c.Check(settled.Ok && steady == 0, $"before the log out, settled: no coarse far tile has ground-level vertices inside loaded real zones ({steady}); {settled.Detail}");
        c.Report("settled before the real log out");
    }

    // Main menu: watcher start now, so it see the very first frame of the loaded world. It run on the plugin object
    // (lives across scenes) and write only into its own box; LoadCoverAfter read the box and stop it.
    private static IEnumerator LoadCoverMenu()
    {
        const string N = LoadCoverName;
        // Old watcher stopped and forgotten: when this part fail, the after part must find none (not old numbers).
        if (_load != null)
        {
            _load.Running = false;
            _load = null;
        }
        if (Plugin.Instance == null || !Plugin.Instance.IsActive || Player.m_localPlayer != null)
        {
            SelfTest.Fail(N, "not at the main menu with the mod active: the world load cannot be watched");
            yield break;
        }
        _load = new LoadWatch { Running = true };
        Plugin.Instance.StartCoroutine(LoadWatcher(_load));
        SelfTest.Pass(N, "at the main menu: every frame of the next world load is watched from here");
    }

    private static IEnumerator LoadWatcher(LoadWatch w)
    {
        var giveUp = Kit.Now + 1500f;
        var nextProbe = 0f;
        while (w.Running && Kit.Now < giveUp)
        {
            try
            {
                var player = Player.m_localPlayer;
                var cam = Utils.GetMainCamera();
                var mgr = LodTerrainManager.Instance;
                if (player != null && cam != null && mgr != null && ZNet.instance != null && ZoneSystem.instance != null)
                {
                    if (w.Spawned < 0f)
                    {
                        w.Spawned = Kit.Now;
                        w.SurfaceAtSpawn = mgr.TestSurfaceCount;
                    }
                    if (mgr.TestSurfaceCount > 0)
                    {
                        if (w.FirstSurface < 0f)
                        {
                            w.FirstSurface = Kit.Now;
                        }
                        mgr.TestCoarseOverLoaded(out _, out var vertices, out var level);
                        if (vertices > 0)
                        {
                            w.LastOpen = Kit.Now;
                            w.WorstVertices = Mathf.Max(w.WorstVertices, vertices);
                            w.WorstLevel = Mathf.Max(w.WorstLevel, level);
                            // One picture of that moment for a person (same as horizons.attach-cover take).
                            if (!w.Shot && Kit.Now - w.Spawned >= 0.3f)
                            {
                                w.Shot = true;
                                SelfTest.Screenshot(LoadCoverName, "coarse-ground-after-world-load");
                            }
                        }
                        if (Kit.Now >= nextProbe || (vertices > 0 && w.CoverLooks == 0))
                        {
                            nextProbe = Kit.Now + 0.5f;
                            var probe = mgr.ProbeNearGround(cam.transform.position, player.transform.position, 200f, out var poking, out _);
                            w.Looks++;
                            // Look that found no far vertex over loaded real ground compared nothing.
                            var compared = Kit.ProbeCompared(probe);
                            w.Compared = Mathf.Max(w.Compared, compared);
                            w.ComparedLooks += compared > 0 ? 1 : 0;
                            w.WorstPoking = Mathf.Max(w.WorstPoking, poking);
                            if (vertices > 0)
                            {
                                w.CoverLooks++;
                                w.CoverPoking = Mathf.Max(w.CoverPoking, poking);
                            }
                        }
                    }
                    if (Kit.Now - w.Spawned >= WatchSeconds)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                w.Problem = e.GetType().Name + ": " + e.Message;
                break;
            }
            yield return null;
        }
        w.Running = false;
        w.Done = true;
    }

    private static IEnumerator LoadCoverAfter()
    {
        const string N = LoadCoverName;
        var c = new Kit.Checks(N);
        var w = _load;
        try
        {
            if (w == null)
            {
                c.Check(false, "no watcher was started at the main menu");
                c.Report("");
                yield break;
            }
            var waited = new Kit.Box();
            yield return Kit.WaitUntil(() => w.Done, WatchSeconds + 15f, waited);
            if (!c.Check(waited.Ok, $"the watcher did not finish its {WatchSeconds:0} s in the loaded world (player seen: {w.Spawned >= 0f})"))
            {
                c.Report("");
                yield break;
            }
            c.Check(w.Problem.Length == 0, "the watcher ran without an exception: " + (w.Problem.Length == 0 ? "ok" : w.Problem));
            c.Check(w.Spawned >= 0f && w.Looks > 0, $"the watcher saw the player stand in the loaded world and looked at the near ground ({w.Looks} looks, one every 0.5 s)");
            c.Check(w.ComparedLooks > 0,
                $"far-tile vertices over loaded real ground were there to compare within 200 m (at {w.ComparedLooks} of {w.Looks} looks, up to {w.Compared} vertices): without any, 'none above the real ground' would say nothing");
            var open = w.LastOpen < 0f || w.Spawned < 0f ? 0f : Mathf.Max(0f, w.LastOpen - w.Spawned);
            c.Note($"world load: {w.SurfaceAtSpawn} far surface tiles were drawn in the first frame with the player in the world, far ground first seen "
                   + $"{(w.FirstSurface < 0f ? -1f : w.FirstSurface - w.Spawned):0.0} s after that frame; coarse tiles kept ground-level vertices inside loaded zones for {open:0.0} s "
                   + $"after it (up to {w.WorstVertices} vertices, coarsest tile level {w.WorstLevel}); far-tile vertices above the real ground within 200 m: up to {w.CoverPoking} at the "
                   + $"{w.CoverLooks} looks made meanwhile, up to {w.WorstPoking} in the whole {WatchSeconds:0} s");
            c.Check(w.FirstSurface >= 0f, $"the far terrain was drawn within {WatchSeconds:0} s of the player standing in the loaded world");
            c.Check(open <= CoverLimit,
                $"right after loading a world, the coarse far tiles are out of the loaded real zones within {CoverLimit:0} s of the player standing in it (it lasted {open:0.0} s: up to {w.WorstVertices} vertices, tile level up to {w.WorstLevel})");
            c.Check(w.CoverPoking == 0,
                $"…and meanwhile no far-tile vertex stands above the real ground within 200 m of the player (up to {w.CoverPoking} at {w.CoverLooks} looks)");
            c.Check(w.WorstPoking == 0, $"…nor at any other look in those {WatchSeconds:0} s (up to {w.WorstPoking} at {w.Looks} looks)");
            c.Report($"real world load: coarse far tiles leave the loaded real zones within {CoverLimit:0} s, nothing far above the real ground");
        }
        finally
        {
            if (w != null)
            {
                w.Running = false;
            }
        }
    }

    // ---------- horizons.disabled.vanilla (T13)

    // First game, mod on: note the far plane the game itself use (mod raise it), for the second game to compare.
    private static IEnumerator DisabledLeave()
    {
        const string N = DisabledName;
        var c = new Kit.Checks(N);
        var ready = new Kit.Box();
        yield return Ready(c, ready, 60f);
        if (!ready.Ok)
        {
            c.Report("");
            yield break;
        }
        var cam = Utils.GetMainCamera();
        var mgr = LodTerrainManager.Instance;
        // Mod not holding the camera (RaiseCameraFarClip off): what the camera has now is the game's own.
        var own = mgr.TestOriginalFarClip > 0f ? mgr.TestOriginalFarClip : cam.farClipPlane;
        c.Check(!TerrainLink.VanillaActive && Kit.LivePatches() == 5,
            $"first game, mod on: far terrain in place of the vanilla distant terrain, patches on ({Kit.LivePatches()} of 5)");
        c.Check(SelfTest.Carry(FarClipKey, own.ToString("R", CultureInfo.InvariantCulture)), "the game's own far plane could be kept for the second game");
        c.Report($"first game, mod on: the game's own far plane is {own:0} m (the camera has {cam.farClipPlane:0} m now)");
    }

    // Second game: Enabled = false in the config file before the game started.
    private static IEnumerator DisabledCheck()
    {
        const string N = DisabledName;
        var c = new Kit.Checks(N);
        Kit.Weather weather = null;
        try
        {
            var cam = Utils.GetMainCamera();
            if (!c.Check(Player.m_localPlayer != null && cam != null && EnvMan.instance != null && ZNet.instance != null, "local player, camera and weather manager exist"))
            {
                c.Report("");
                yield break;
            }
            c.Check(SelfTest.Stage == SelfTest.DisabledStage, $"this is the game started with every MC mod turned off (stage '{SelfTest.Stage}')");
            var mine = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(mine.HasValue && mine.Value.IsDisabledByUser && mine.Value.Status == OffStatus,
                mine.HasValue
                    ? $"the MC Mods panel's row for the mod says off: state {mine.Value.State}, status \"{mine.Value.Status}\" (expected \"{OffStatus}\")"
                    : "the mod is not in the list the MC Mods panel shows");
            c.Check(Plugin.Instance != null && !Plugin.Instance.IsActive && Plugin.Instance.Enabled != null && !Plugin.Instance.Enabled.Value,
                "the plugin is loaded, not active, and its Enabled setting reads false");

            var tl = UnityEngine.Object.FindAnyObjectByType<TerrainLod>();
            if (!c.Check(tl != null && tl.isActiveAndEnabled, "the game's distant terrain object exists and is on"))
            {
                c.Report("");
                yield break;
            }
            var meshes = new Kit.Box();
            yield return Kit.WaitUntil(() => tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, $"vanilla distant terrain: its 3x3 tiles exist and have their meshes ({Kit.VanillaMeshes(tl)} of {tl.m_heightmaps.Count})");
            // How far the game's own distant land reaches from the camera (its tiles' outer edges, the larger axis).
            var reach = 0f;
            var eye = cam.transform.position;
            foreach (var h in tl.m_heightmaps)
            {
                if (h == null || h.m_heightmap == null)
                {
                    continue;
                }
                var half = h.m_heightmap.m_width * h.m_heightmap.m_scale * 0.5f;
                var p = h.m_heightmap.transform.position;
                reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(p.x - eye.x), Mathf.Abs(p.z - eye.z)) + half);
            }
            c.Check(tl.m_heightmaps.Count == 9 && reach >= 1000f && reach <= 1500f,
                $"the distant land is the game's own 3x3 grid and ends about 1.2 km from the camera ({tl.m_heightmaps.Count} tiles, farthest edge {reach:0} m away)");
            c.Check(LodTerrainManager.Instance == null && DistantObjectManager.Instance == null && TerrainLink.Current == null
                    && tl.GetComponent<LodTerrainManager>() == null && tl.GetComponent<DistantObjectManager>() == null,
                "no far terrain manager and no far object manager");
            var lod = Kit.Roots("DistantHorizons_LOD");
            var objects = Kit.Roots("DistantHorizons_Objects");
            var rig = Kit.Roots("DH_ImpostorBakeRig");
            c.Check(lod == 0 && objects == 0 && rig == 0, $"no far tile, far object or bake object in the scene ({lod}, {objects}, {rig})");
            c.Check(BuffersOnAnyCamera() == 0, $"no Distant Horizons buffer on any camera ({BuffersOnAnyCamera()})");
            c.Check(Kit.LivePatches() == 0, $"none of the mod's five live patches is applied ({Kit.LivePatches()})");
            c.Check(Plugin.Cfg != null && Plugin.Cfg.TestHandlerCount == 0, "nothing listens to the mod's settings");
            c.Check(!Terminal.commands.ContainsKey("dh") && Kit.AnyContains(Kit.Console("dh"), "is not a recognized command"), "dh is an unknown console command");
            LodTerrainManager.TestRealZoneLeftovers(out var zones, out var sunk, out var forcedOff, out var tessOff);
            c.Check(zones > 0 && sunk == 0 && forcedOff == 0 && tessOff == 0,
                $"the real zones are drawn by the game alone ({sunk} of {zones} depth-only, {forcedOff} forced off, {tessOff} with changed tessellation)");
            Kit.LodPlanes(out var planes, out var hidden, out var visible, out _);
            c.Check(planes >= 1 && hidden == 0 && Kit.Near(visible, Kit.VanillaPlaneDistance()),
                $"the game's own distant water plane is in use ({Kit.F(visible)}, the game writes {Kit.F(Kit.VanillaPlaneDistance())}; {hidden} of {planes} out of reach)");

            var carried = SelfTest.Carried(FarClipKey);
            if (c.Check(carried != null && float.TryParse(carried, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                    $"the first game left the game's own far plane for comparison ('{carried ?? "nothing"}')"))
            {
                var own = float.Parse(carried, CultureInfo.InvariantCulture);
                c.Check(Mathf.Abs(cam.farClipPlane - own) < 0.5f, $"normal sky distance: the far plane is the game's own ({cam.farClipPlane:0} m, the game's {own:0} m)");
            }

            // Normal fog: clear noon, what the weather itself gives (with the mod on it would be a quarter of that).
            weather = new Kit.Weather();
            weather.Force("Clear", 0.5f);
            var box = new Kit.Box();
            yield return Kit.WaitWeather("Clear", box);
            if (c.Check(box.Ok, "clear weather at noon could be shown: " + box.Detail))
            {
                yield return Kit.WaitFixed(3);
                var raw = EnvMan.instance.GetEnv("Clear").m_fogDensityDay;
                c.Check(Kit.Near(RenderSettings.fogDensity, raw), $"normal fog: clear-weather fog is the weather's own ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
                c.Check(Patches.EnvManPatches.TestAfter < 0f, "the mod wrote no fog value in this game");
            }
            // Every check above was made (early ends report a failure and never come here).
            _vanillaChecked = true;
            c.Report("started with Enabled = false: vanilla distant terrain, sky distance, water and fog; the mod is listed as off");
        }
        finally
        {
            if (weather != null)
            {
                weather.Restore();
            }
        }
    }

    // ---------- horizons.disabled.session-log (T19)

    // End of the first game: every normal test and every real relog are behind us. Log listener (ErrorWatch) is on
    // since BindConfig of this game (before my patches went on).
    private static IEnumerator SessionLeave()
    {
        const string N = SessionLogName;
        var c = new Kit.Checks(N);
        if (!c.Check(Plugin.Instance != null && Plugin.Instance.IsActive,
                "Distant Horizons is active at the end of the first game (its log watch runs since the plugin loaded): "
                + (Plugin.Instance != null ? Plugin.Instance.StatusText : "no plugin")))
        {
            c.Report("");
            yield break;
        }
        // Deaf listener would say "no error" too: prove it hears first.
        ErrorWatch.Proof(c, N);
        c.Check(ErrorWatch.Errors == 0,
            $"no error line from or about Distant Horizons from the loading of the plugin to the end of its tests, real log outs included ({ErrorWatch.Errors}, first: {ErrorWatch.FirstError})");
        var guarded = Guarded();
        c.Check(guarded != null && guarded.Count == 0,
            guarded == null ? "cannot read the list of guarded failures" : $"no patch or update of the mod failed ({string.Join(", ", guarded.ToArray())})");
        c.Note($"warning lines of the mod this game: {ErrorWatch.Warnings("")}; error lines of others: {ErrorWatch.OtherErrors} "
               + $"(last: {ErrorWatch.LastOtherError}), of them through Valheim Community Patch: {ErrorWatch.KnownForeignErrors}; {ErrorWatch.Heard} log lines heard");
        c.Report("whole first game (tests, real log outs): log clean of Distant Horizons errors");
    }

    // Second game, mod off: its code still load (settings, registry). Log lines of that game are judged by the probe
    // from the log file (probe.disabled.<GUID>) and here by the log watch (on since BindConfig, also with the mod
    // off: it hears what a log file without the game's own lines would not show); and what no log shows: failures
    // the mod's guards caught.
    private static IEnumerator SessionCheck()
    {
        const string N = SessionLogName;
        var c = new Kit.Checks(N);
        ErrorWatch.Proof(c, N);
        c.Check(ErrorWatch.Errors == 0,
            $"no error line from or about Distant Horizons in the game started with the mod off ({ErrorWatch.Errors}, first: {ErrorWatch.FirstError})");
        var guarded = Guarded();
        c.Check(guarded != null && guarded.Count == 0,
            guarded == null ? "cannot read the list of guarded failures" : $"no guarded failure of the mod's code in the game started with the mod off ({string.Join(", ", guarded.ToArray())})");
        c.Check(Plugin.Instance != null && !Plugin.Instance.IsActive, "the mod stayed off in this game");
        _sessionChecked = true;
        c.Report("game started with the mod off: log watch hears, no error line of the mod, no failure caught by its guards");
        yield break;
    }
}
#endif
