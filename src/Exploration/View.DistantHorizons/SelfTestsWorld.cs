#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self tests about what get drawn (world probe, tools/Test-InWorld.ps1):
//   horizons.horizon       clear noon, camera 200 m up: far ground under every point of the world disc (no hole, no
//                          ground drawn twice), crack filler under every surface tile, coarse tile corners = world
//                          generator heights, far tiles never listed as real ground; screenshot
//   horizons.seam          where loaded real ground end: far ground there is exact, never above the real ground and at
//                          most NearTerrainOffset + 0.6 m under it; every far tile keep my hide distance (no dissolve
//                          band); no far vertex above real ground within 200 m and no painted zone with tessellating
//                          depth renderer, from the game camera and from 60 m up, also with every zone painted
//   horizons.fog           weather forced like console "env": Clear = weather fog x 0.25, Rain = own fog, a storm
//                          weather = own fog, a weather between = ramp; FogDensityMultiplier 1 = normal, back = thin;
//                          "dh envs" list every weather with its density; NOTE every weather's densities
//   horizons.fog-blizzard  env Twilight_SnowStorm (Deep North blizzard) day and night: fog follow the rule, NOTE
//                          whether it stay thick
//   horizons.sea           FarWater on: all bands painted, one buffer, sheet at water level from the automatic inner
//                          radius to past the world edge, game's distant water plane out of reach; off: nothing
//                          painted, plane has the game's value; on again; NOTE plane size; screenshots
//   horizons.realground    RealTerrainFadeFix: every real zone past the game's fade sunk and painted, near ones left
//                          to the game; off: nothing sunk, no copy, far tiles still painted; on again; screenshots
//   horizons.interior      player lifted above 3000 m (what the game call inside a dungeon): no far tile renderer on,
//                          nothing painted, no sea, no far object; back down: all back
//   horizons.interior-off  mod turned off (framework path) while up there, then down: nothing of mine left on real
//                          zones, vanilla grid with meshes, water plane and fog the game's, no buffer; on again
//   horizons.stream        player flown 2 km and back at 90 m/s: far ground under every point around the camera
//                          at every check, no error, no frame over 3 s; after: zones load, finest tile under the
//                          camera within 15 s; NOTE frame times
// Settings forced only in memory (Kit.Force), weather with EnvMan debug fields, all put back in finally.
internal static class WorldTests
{
    private const string HorizonName = "horizons.horizon";
    private const string SeamName = "horizons.seam";
    private const string FogName = "horizons.fog";
    private const string BlizzardName = "horizons.fog-blizzard";
    private const string SeaName = "horizons.sea";
    private const string RealGroundName = "horizons.realground";
    private const string InteriorName = "horizons.interior";
    private const string InteriorOffName = "horizons.interior-off";
    private const string StreamName = "horizons.stream";

    private const string BlizzardEnv = "Twilight_SnowStorm";
    private const float InteriorLift = 4000f;

    internal static void Register()
    {
        SelfTest.Register(HorizonName, RunHorizon);
        SelfTest.Register(SeamName, RunSeam);
        SelfTest.Register(FogName, RunFog);
        SelfTest.Register(BlizzardName, RunBlizzard);
        SelfTest.Register(SeaName, RunSea);
        SelfTest.Register(RealGroundName, RunRealGround);
        SelfTest.Register(InteriorName, RunInterior);
        SelfTest.Register(InteriorOffName, RunInteriorOff);
        SelfTest.Register(StreamName, RunStream);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(HorizonName);
        SelfTest.Unregister(SeamName);
        SelfTest.Unregister(FogName);
        SelfTest.Unregister(BlizzardName);
        SelfTest.Unregister(SeaName);
        SelfTest.Unregister(RealGroundName);
        SelfTest.Unregister(InteriorName);
        SelfTest.Unregister(InteriorOffName);
        SelfTest.Unregister(StreamName);
    }

    // Player, camera and far terrain there? Else one failed check.
    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || GameCamera.instance == null || EnvMan.instance == null)
        {
            c.Check(false, "no local player, camera or weather manager");
            yield break;
        }
        if (Plugin.Instance == null || !Plugin.Instance.IsActive)
        {
            c.Check(false, "Distant Horizons is not active: " + (Plugin.Instance != null ? Plugin.Instance.StatusText : "no plugin"));
            yield break;
        }
        var up = new Kit.Box();
        yield return Kit.WaitTerrain(up);
        ready.Ok = c.Check(up.Ok, up.Detail);
    }

    // ---------- horizons.horizon

    private static IEnumerator RunHorizon()
    {
        const string N = HorizonName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        var hold = new Kit.CameraHold();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            weather.Force("Clear", 0.5f);
            var at = Player.m_localPlayer.transform.position;
            hold.At(at + Vector3.up * 200f, at + Vector3.forward * 3000f);
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 60f);
            var mgr = LodTerrainManager.Instance;
            if (!c.Check(settled.Ok && mgr != null, settled.Detail))
            {
                c.Report("");
                yield break;
            }
            c.Note(mgr.GetStats());

            mgr.TestSurfaceCut(out var overlaps, out var notShown);
            c.Check(overlaps == 0, $"no far ground drawn twice ({overlaps} surface tiles lie under another surface tile)");
            c.Check(notShown == 0, $"every surface tile is built and switched on ({notShown} are not)");

            var radius = Plugin.Cfg.V(Plugin.Cfg.WorldRadius);
            Kit.Coverage(mgr, 0f, 0f, radius, 96f, out var points, out var holes, out var coarsest, out var firstHole);
            c.Check(points > 1000 && holes == 0,
                $"far ground under every point of a 96 m grid inside the {radius:0} m world disc ({holes} of {points} points have none, first {firstHole})");
            var ringHoles = 0;
            for (var a = 0; a < 360; a += 2)
            {
                var x = Mathf.Cos(a * Mathf.Deg2Rad) * (radius - 64f);
                var z = Mathf.Sin(a * Mathf.Deg2Rad) * (radius - 64f);
                if (!mgr.TrySampleSurface(x, z, out _, out _))
                {
                    ringHoles++;
                }
            }
            c.Check(ringHoles == 0, $"far ground all around the world's edge ({ringHoles} of 180 directions without, 64 m inside the edge)");

            if (Plugin.Cfg.FillCracks.Value)
            {
                var bare = mgr.TestSurfaceWithoutFiller();
                c.Check(bare == 0, $"a lowered parent tile under every surface tile hides the cracks between levels ({bare} have none)");
            }
            else
            {
                c.Note("FillCracks is off in this config: crack filler not checked");
            }

            // Coarse tiles take the game's own distant heights: at tile corners (never smoothed) that is the world
            // generator's height at that very point.
            var wg = WorldGenerator.instance;
            var corners = 0;
            var off = 0;
            var worst = 0f;
            foreach (var t in mgr.TestTiles)
            {
                if (t.Mode != DisplayMode.Surface || t.Exact || t.Heightmap == null)
                {
                    continue;
                }
                var heights = t.Heightmap.m_heights;
                var w = t.Width;
                var num = w + 1;
                if (heights == null || heights.Count != num * num)
                {
                    continue;
                }
                var ox = t.Center.x + w * t.Scale * -0.5f;
                var oz = t.Center.z + w * t.Scale * -0.5f;
                for (var k = 0; k <= w; k += w)
                {
                    for (var l = 0; l <= w; l += w)
                    {
                        var wx = (float)((double)ox + (double)l * (double)t.Scale);
                        var wz = (float)((double)oz + (double)k * (double)t.Scale);
                        var d = Mathf.Abs(heights[k * num + l] - wg.GetHeight(wx, wz));
                        corners++;
                        if (d > 0.05f)
                        {
                            off++;
                        }
                        if (d > worst)
                        {
                            worst = d;
                        }
                    }
                }
            }
            c.Note($"coarse surface tile corners against the world generator: {corners} checked, {off} off by more than 5 cm, worst {worst:0.00} m; coarsest surface level {coarsest}");
            c.Check(corners >= 40 && off * 50 <= corners,
                $"coarse far tiles have the world generator's heights at their corners ({off} of {corners} differ by more than 5 cm, worst {worst:0.00} m)");

            Kit.FarTilesAsGround(out var tiles, out var listed, out var notDistant);
            c.Check(tiles > 0 && listed == 0 && notDistant == 0,
                $"no far tile is listed with the game's real ground heightmaps ({listed} of {tiles} listed, {notDistant} not flagged distant)");
            c.Check(Heightmap.FindHeightmap(at + Vector3.right * 3000f) == null, "the game finds no ground heightmap 3 km away, where only far tiles are");

            yield return Kit.Shot(N, "clear-noon-200m-up");
            c.Report($"far ground whole and single under {points} points to the world edge, {corners} coarse corners match the generator");
        }
        finally
        {
            hold.Release();
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.seam

    // Rim of the loaded area: zone edges with real ground on one side only. Real ground 5 cm inside the loaded zone
    // against the far ground on the edge itself.
    internal struct RimResult
    {
        internal int Samples, Holes, Coarse, Above, Below, Zones;
        internal float WorstAbove, WorstBelow, MinY, MaxY;
    }

    internal static RimResult Rim(LodTerrainManager mgr, float offset)
    {
        var r = new RimResult { MinY = float.MaxValue, MaxY = float.MinValue };
        var zs = ZoneSystem.instance;
        r.Zones = zs.m_zones.Count;
        LodTile hint = null;
        foreach (var kv in zs.m_zones)
        {
            var zone = kv.Key;
            for (var side = 0; side < 4; side++)
            {
                var nx = zone.x + (side == 0 ? 1 : side == 1 ? -1 : 0);
                var ny = zone.y + (side == 2 ? 1 : side == 3 ? -1 : 0);
                if (zs.m_zones.ContainsKey(new Vector2s(nx, ny)))
                {
                    continue;
                }
                var inward = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
                for (var s = -28f; s <= 28f; s += 8f)
                {
                    var edge = side < 2
                        ? new Vector3(zone.x * 64f + (side == 0 ? 32f : -32f), 0f, zone.y * 64f + s)
                        : new Vector3(zone.x * 64f + s, 0f, zone.y * 64f + (side == 2 ? 32f : -32f));
                    if (!Heightmap.GetHeight(edge + inward * 0.05f, out var realY))
                    {
                        continue;
                    }
                    r.Samples++;
                    r.MinY = Mathf.Min(r.MinY, realY);
                    r.MaxY = Mathf.Max(r.MaxY, realY);
                    if (!mgr.TrySampleSurface(edge.x, edge.z, ref hint, out var farY, out _))
                    {
                        r.Holes++;
                        continue;
                    }
                    if (hint == null || !hint.Exact)
                    {
                        r.Coarse++;
                    }
                    var d = farY - realY;
                    if (d > 0.1f)
                    {
                        r.Above++;
                        r.WorstAbove = Mathf.Max(r.WorstAbove, d);
                    }
                    if (d < -(offset + 0.6f))
                    {
                        r.Below++;
                        r.WorstBelow = Mathf.Min(r.WorstBelow, d);
                    }
                }
            }
        }
        return r;
    }

    internal static void RimChecks(Kit.Checks c, RimResult r, string when, float offset)
    {
        var sim = ZNet.instance.GetSyncedSimulationDistance();
        c.Note($"{when}rim of the loaded area (simulation near={sim.NearSimulationDistance}, {r.Zones} zones): {r.Samples} points, real ground there "
               + $"{(r.Samples > 0 ? r.MinY : 0f):0.0} to {(r.Samples > 0 ? r.MaxY : 0f):0.0} m high; far ground missing at {r.Holes}, coarse at {r.Coarse}, "
               + $"more than 0.1 m above at {r.Above} (worst {r.WorstAbove:0.00} m), more than {offset + 0.6f:0.0} m below at {r.Below} (worst {r.WorstBelow:0.00} m)");
        c.Check(r.Samples >= 20, $"{when}found the rim of the loaded area ({r.Samples} sample points)");
        c.Check(r.Holes == 0, $"{when}far ground exists everywhere along the rim ({r.Holes} points without)");
        c.Check(r.Coarse == 0, $"{when}the far ground along the rim is exact-height ground ({r.Coarse} points on coarse tiles)");
        c.Check(r.Above * 50 <= r.Samples, $"{when}far ground never stands above the real ground at the rim ({r.Above} of {r.Samples} points, worst {r.WorstAbove:0.00} m)");
        c.Check(r.Below * 50 <= r.Samples, $"{when}no step down at the rim: far ground at most {offset + 0.6f:0.0} m under the real ground ({r.Below} of {r.Samples} points lower, worst {r.WorstBelow:0.00} m)");
    }

    private static IEnumerator RunSeam()
    {
        const string N = SeamName;
        var c = new Kit.Checks(N);
        var hold = new Kit.CameraHold();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the near-ground checks need it"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 45f);
            var mgr = LodTerrainManager.Instance;
            if (!c.Check(settled.Ok && mgr != null, settled.Detail))
            {
                c.Report("");
                yield break;
            }
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            var offset = Plugin.Cfg.NearTerrainOffset.Value;

            // Rim of the loaded area: zone edges with real ground on one side only.
            var rim = Rim(mgr, offset);
            RimChecks(c, rim, "", offset);

            mgr.TestTileRenderers(out var shown, out _, out var wrongHide, out var wrongWater);
            c.Check(shown > 0 && wrongHide == 0, $"every shown far tile carries its own hide distance: nothing dissolves near the camera ({wrongHide} of {shown} wrong)");
            c.Check(wrongWater == 0, $"every shown far tile renderer is depth-only under its painted copy ({wrongWater} of {shown} not)");

            // Near ground from the game camera, then from high up (the set of painted zones changes with the camera).
            c.Note(NearGroundCapture.DescribeSettings(player.transform.position));
            var at = player.transform.position;
            for (var view = 0; view < 2; view++)
            {
                var label = view == 0 ? "game camera" : "60 m up";
                if (view == 1)
                {
                    hold.At(at + Vector3.up * 60f - Vector3.forward * 20f, at);
                }
                yield return Kit.Frames(3);
                var probe = mgr.ProbeNearGround(cam.transform.position, at, 200f, out var poking, out var mismatched);
                c.Note($"{label}: {(probe.Length > 400 ? probe.Substring(0, 400) + "..." : probe)}");
                c.Check(poking == 0, $"{label}: {poking} far-tile vertices above the real ground within 200 m");
                c.Check(mismatched == 0, $"{label}: {mismatched} painted real zones whose depth renderer still tessellates");
                mgr.PaintAllRealZones = true;
                yield return Kit.Frames(3);
                mgr.ProbeNearGround(cam.transform.position, at, 200f, out _, out var mismatchedAll);
                mgr.PaintAllRealZones = false;
                c.Check(mismatchedAll == 0, $"{label}, every zone painted: {mismatchedAll} depth renderers still tessellate");
            }
            yield return Kit.Shot(N, "60m-up");
            c.Report($"{rim.Samples} rim points meet the far ground, nothing pokes through near the player");
        }
        finally
        {
            Kit.Safe("paint all", () =>
            {
                if (LodTerrainManager.Instance != null)
                {
                    LodTerrainManager.Instance.PaintAllRealZones = false;
                }
            });
            hold.Release();
            Kit.PutBack();
        }
    }

    // ---------- horizons.fog

    private static IEnumerator RunFog()
    {
        const string N = FogName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var man = EnvMan.instance;
            var cfg = Plugin.Cfg;
            Kit.ForceFogDefaults();
            var clearLimit = cfg.V(cfg.FogClearDensity);
            var stormLimit = cfg.V(cfg.FogStormDensity);

            // Every weather's own densities, for the record.
            var all = new List<string>();
            foreach (var e in man.m_environments)
            {
                all.Add($"{e.m_name}={Kit.F(e.m_fogDensityDay)}/{Kit.F(e.m_fogDensityNight)}{(e.m_isWet ? " wet" : "")}");
            }
            c.Note($"weathers (fog density day/night): {string.Join(", ", all.ToArray())}");

            // env Clear, noon.
            var box = new Kit.Box();
            weather.Force("Clear", 0.5f);
            yield return Kit.WaitWeather("Clear", box);
            if (!c.Check(box.Ok, box.Detail))
            {
                c.Report("");
                yield break;
            }
            var clear = man.GetEnv("Clear");
            var raw = clear.m_fogDensityDay;
            var shown = RenderSettings.fogDensity;
            c.Note($"Clear at noon: weather fog {Kit.F(raw)}, on screen {Kit.F(shown)}, the game wrote {Kit.F(Patches.EnvManPatches.TestBefore)} before the mod");
            c.Check(raw > 0f && raw <= clearLimit, $"Clear counts as clear weather: its fog {Kit.F(raw)} is at or below FogClearDensity {Kit.F(clearLimit)}");
            c.Check(Kit.Near(shown, raw * 0.25f), $"env Clear: fog on screen {Kit.F(shown)} is the weather's {Kit.F(raw)} x 0.25 ({Kit.F(raw * 0.25f)})");
            // "Far land visible": the far tiles are drawn under this thin fog (how it looks stays a matter for the eye).
            var land = LodTerrainManager.Instance;
            land.TestTileRenderers(out var tilesShown, out var tilesOn, out _, out _);
            c.Check(tilesOn > 0 && (!Kit.Shrink || land.TestTileDraws > 0),
                $"env Clear: the far land is drawn under it ({tilesOn} of {tilesShown} far tiles on, {land.TestTileDraws} painted last frame)");
            c.Check(Kit.Near(Patches.EnvManPatches.TestBefore, raw) && clear.m_fogDensityDay == raw,
                $"the weather's own fog value is untouched: the game wrote {Kit.F(Patches.EnvManPatches.TestBefore)}, the weather says {Kit.F(raw)}");

            // FogDensityMultiplier = 1, then back.
            cfg.SetForTest(cfg.FogDensityMultiplier, 1f);
            yield return Kit.WaitFixed(2);
            c.Check(Kit.Near(RenderSettings.fogDensity, raw), $"FogDensityMultiplier = 1: clear weather has the game's normal fog ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            cfg.SetForTest(cfg.FogDensityMultiplier, 0.25f);
            yield return Kit.WaitFixed(2);
            c.Check(Kit.Near(RenderSettings.fogDensity, raw * 0.25f), $"back to 0.25: thin again ({Kit.F(RenderSettings.fogDensity)})");

            // env Rain.
            var rain = man.GetEnv("Rain");
            if (c.Check(rain != null, "the game has a weather named Rain"))
            {
                weather.Force("Rain", 0.5f);
                yield return Kit.WaitWeather("Rain", box);
                c.Check(box.Ok, box.Detail);
                c.Check(rain.m_isWet, "Rain is a wet weather");
                c.Check(Kit.Near(RenderSettings.fogDensity, rain.m_fogDensityDay),
                    $"env Rain: normal rain fog ({Kit.F(RenderSettings.fogDensity)} on screen, weather {Kit.F(rain.m_fogDensityDay)})");
            }

            // One dry weather at or above the storm density, one between the two limits.
            EnvSetup storm = null;
            EnvSetup between = null;
            foreach (var e in man.m_environments)
            {
                if (e.m_isWet)
                {
                    continue;
                }
                var d = e.m_fogDensityDay;
                if (storm == null && d >= stormLimit)
                {
                    storm = e;
                }
                if (between == null && d > clearLimit * 1.1f && d < stormLimit * 0.9f)
                {
                    between = e;
                }
            }
            foreach (var e in new[] { storm, between })
            {
                if (e == null)
                {
                    continue;
                }
                weather.Force(e.m_name, 0.5f);
                yield return Kit.WaitWeather(e.m_name, box);
                if (!c.Check(box.Ok, box.Detail))
                {
                    continue;
                }
                var want = Kit.ExpectedFog(e, e.m_fogDensityDay);
                var kind = ReferenceEquals(e, storm) ? "dense dry weather keeps its fog" : "weather between clear and storm gets part of the thinning";
                c.Check(Kit.Near(RenderSettings.fogDensity, want),
                    $"{e.m_name} ({kind}): on screen {Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(e.m_fogDensityDay)}, expected {Kit.F(want)}");
            }
            c.Note($"dense dry weather tried: {(storm != null ? storm.m_name : "none in this game")}; in-between weather tried: {(between != null ? between.m_name : "none in this game")}");

            // dh envs.
            var lines = Kit.Console("dh envs");
            c.Check(Kit.AnyStartsWith(lines, $"Environments ({man.m_environments.Count})"),
                $"'dh envs' prints the header with the number of weathers ({Kit.First(lines)})");
            var listed = 0;
            foreach (var e in man.m_environments)
            {
                if (Kit.AnyStartsWith(lines, e.m_name + " day=" + e.m_fogDensityDay.ToString("0.#####")))
                {
                    listed++;
                }
            }
            c.Check(listed == man.m_environments.Count, $"'dh envs' lists every weather with its day density ({listed} of {man.m_environments.Count})");

            c.Report("clear weather thinned x 0.25, rain and dense weather kept, multiplier 1 = normal fog, dh envs complete");
        }
        finally
        {
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.fog-blizzard

    private static IEnumerator RunBlizzard()
    {
        const string N = BlizzardName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var man = EnvMan.instance;
            var cfg = Plugin.Cfg;
            var blizzard = man.GetEnv(BlizzardEnv);
            if (!c.Check(blizzard != null, $"the game has a weather named {BlizzardEnv} (the Deep North blizzard)"))
            {
                c.Report("");
                yield break;
            }
            Kit.ForceFogDefaults();
            var stormLimit = cfg.V(cfg.FogStormDensity);
            var box = new Kit.Box();
            weather.Force(BlizzardEnv, 0.5f);
            yield return Kit.WaitWeather(BlizzardEnv, box);
            if (!c.Check(box.Ok, box.Detail))
            {
                c.Report("");
                yield break;
            }
            var dayRaw = blizzard.m_fogDensityDay;
            var dayShown = RenderSettings.fogDensity;
            c.Check(Kit.Near(dayShown, Kit.ExpectedFog(blizzard, dayRaw)),
                $"blizzard at noon: on screen {Kit.F(dayShown)}, weather {Kit.F(dayRaw)}, expected {Kit.F(Kit.ExpectedFog(blizzard, dayRaw))}");
            weather.Force(BlizzardEnv, 0f);
            yield return Kit.WaitFixed(3);
            var nightRaw = blizzard.m_fogDensityNight;
            var nightShown = RenderSettings.fogDensity;
            c.Check(Kit.Near(nightShown, Kit.ExpectedFog(blizzard, nightRaw)),
                $"blizzard at midnight: on screen {Kit.F(nightShown)}, weather {Kit.F(nightRaw)}, expected {Kit.F(Kit.ExpectedFog(blizzard, nightRaw))}");
            var thickDay = blizzard.m_isWet || dayRaw >= stormLimit;
            var thickNight = blizzard.m_isWet || nightRaw >= stormLimit;
            c.Check(!thickDay || Kit.Near(dayShown, dayRaw), "its day density is at or above FogStormDensity, so the day fog is exactly the weather's own");
            c.Check(!thickNight || Kit.Near(nightShown, nightRaw), "its night density is at or above FogStormDensity, so the night fog is exactly the weather's own");
            c.Note($"{BlizzardEnv}: fog density day {Kit.F(dayRaw)} / night {Kit.F(nightRaw)}{(blizzard.m_isWet ? " (wet)" : "")}, FogStormDensity {Kit.F(stormLimit)}. "
                   + $"Blizzard fog stays thick by day: {(thickDay ? "yes" : "no, thinned to " + Kit.F(dayShown))}; by night: {(thickNight ? "yes" : "no, thinned to " + Kit.F(nightShown))}");
            c.Report($"blizzard fog follows the rule (day {(thickDay ? "kept" : "thinned")}, night {(thickNight ? "kept" : "thinned")})");
        }
        finally
        {
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.sea

    private static IEnumerator RunSea()
    {
        const string N = SeaName;
        var c = new Kit.Checks(N);
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the far sea is only painted in that mode"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var mgr = LodTerrainManager.Instance;
            var cam = Utils.GetMainCamera();
            Kit.Force(cfg.FarWater, true);
            var on = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws > 0, 10f, on);
            c.Note(mgr.Water.Describe());
            if (!c.Check(on.Ok, "FarWater = true: the far sea is painted within 10 s"))
            {
                c.Report("");
                yield break;
            }
            yield return Kit.Frames(2);
            var water = mgr.Water;
            c.Check(mgr.TestSeaDraws == water.BandCount, $"all {water.BandCount} bands of the far sea drawn last frame ({mgr.TestSeaDraws})");
            c.Check(Kit.Buffers(cam, Kit.SeaBuffer) == 1, $"one far-sea paint buffer on the main camera ({Kit.Buffers(cam, Kit.SeaBuffer)})");
            c.Check(Mathf.Abs(water.Center.y - ZoneSystem.instance.m_waterLevel) < 0.001f,
                $"the sheet lies at the game's water level ({water.Center.y:0.##} m, water {ZoneSystem.instance.m_waterLevel:0.##} m)");
            var innerWant = cfg.FarWaterInnerRadius.Value > 0f ? cfg.FarWaterInnerRadius.Value : FarWater.AutoInnerRadius();
            c.Check(Kit.Near(water.Inner, innerWant), $"the sheet starts {water.Inner:0} m out (expected {innerWant:0} m: one zone inside the game's own water)");
            var radius = cfg.V(cfg.WorldRadius);
            c.Check(water.Outer >= radius, $"the sheet reaches the world's edge (outer radius {water.Outer:0} m, world radius {radius:0} m)");
            Kit.LodPlanes(out var planes, out var hidden, out var visible, out var size);
            c.Note($"the game's own distant water plane: {planes} found, bounds {size.x:0} x {size.z:0} m");
            c.Check(planes >= 1, "the game's distant water plane exists (water with _IsLod)");
            c.Check(planes >= 1 && hidden == planes, $"while the far sea is painted the game's distant water plane is out of reach ({hidden} of {planes})");
            yield return Kit.Shot(N, "far-sea-on");

            cfg.SetForTest(cfg.FarWater, false);
            var off = new Kit.Box();
            yield return Kit.WaitUntil(() => !mgr.SeaPainting && mgr.TestSeaDraws == 0, 5f, off);
            yield return Kit.Frames(2);
            c.Check(off.Ok, $"FarWater = false: the far sea is no longer painted (painting {mgr.SeaPainting}, bands drawn {mgr.TestSeaDraws})");
            Kit.LodPlanes(out planes, out hidden, out visible, out _);
            var vanilla = Kit.VanillaPlaneDistance();
            c.Check(planes >= 1 && hidden == 0 && Kit.Near(visible, vanilla),
                $"FarWater = false: the game's own water is back, its distant plane has the game's value ({Kit.F(visible)}, the game writes {Kit.F(vanilla)}; {hidden} of {planes} still out of reach)");
            yield return Kit.Shot(N, "far-sea-off");

            cfg.SetForTest(cfg.FarWater, true);
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws == water.BandCount, 10f, on);
            yield return Kit.Frames(2);
            Kit.LodPlanes(out planes, out hidden, out _, out _);
            c.Check(on.Ok && planes >= 1 && hidden == planes, $"FarWater = true again: painted again, the game's plane out of reach again ({hidden} of {planes})");
            c.Report($"far sea on / off / on, sheet {water.Inner:0} m to {water.Outer:0} m");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.realground

    private static IEnumerator RunRealGround()
    {
        const string N = RealGroundName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        var hold = new Kit.CameraHold();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: RealTerrainFadeFix only works in that mode"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var mgr = LodTerrainManager.Instance;
            var cam = Utils.GetMainCamera();
            weather.Force("Clear", 0.5f);
            Kit.Force(cfg.RealTerrainFadeFix, true);
            var at = Player.m_localPlayer.transform.position;
            hold.At(at + Vector3.up * 40f, at + Vector3.forward * 600f + Vector3.up * 20f);
            yield return Kit.Frames(5);

            var sim = ZNet.instance.GetSyncedSimulationDistance();
            mgr.TestRealZones(cam.transform.position, out var zones, out var need, out var missing, out var extra, out var farthest);
            c.Note($"simulation distance near={sim.NearSimulationDistance}: {zones} real zones drawn, {need} reach past the game's fade (farthest {farthest:0} m away), "
                   + $"{mgr.TestRealDraws} painted copies and {mgr.TestTileDraws} far tiles painted last frame");
            c.Check(mgr.RealFadeFixActive, "RealTerrainFadeFix = true: the fix is active");
            c.Check(mgr.TestTileDraws > 0, "far tiles are painted");
            c.Check(need > 0, "some real zones reach past the game's fade from here");
            c.Check(missing == 0, $"every real zone that reaches past the fade is depth-only under its painted copy ({missing} of {need} are not)");
            c.Check(extra == 0, $"real zones next to the camera are left to the game ({extra} sunk though near)");
            c.Check(mgr.TestRealDraws > 0, "painted copies of real zones were drawn last frame");
            if (farthest >= 200f)
            {
                c.Note($"real ground is painted out to {farthest:0} m: it stays lit beyond the game's 200 m fade");
            }
            else
            {
                c.Note($"with this simulation distance no real zone lies beyond 200 m (farthest {farthest:0} m): the far band of the fade cannot be shown here");
            }
            yield return Kit.Shot(N, "fix-on");

            cfg.SetForTest(cfg.RealTerrainFadeFix, false);
            yield return Kit.Frames(3);
            LodTerrainManager.TestRealZoneLeftovers(out var all, out var sunk, out var forcedOff, out var tessOff);
            c.Check(!mgr.RealFadeFixActive && mgr.TestRealDraws == 0, $"RealTerrainFadeFix = false: no painted copy of real zones ({mgr.TestRealDraws} drawn)");
            c.Check(all > 0 && sunk == 0 && forcedOff == 0 && tessOff == 0,
                $"RealTerrainFadeFix = false: every real zone is drawn by the game alone again ({sunk} of {all} still sunk, {forcedOff} forced off, {tessOff} with changed tessellation)");
            c.Check(mgr.TestTileDraws > 0, "RealTerrainFadeFix = false: the far tiles are still painted");
            yield return Kit.Shot(N, "fix-off");

            cfg.SetForTest(cfg.RealTerrainFadeFix, true);
            yield return Kit.Frames(3);
            mgr.TestRealZones(cam.transform.position, out _, out need, out missing, out extra, out _);
            c.Check(mgr.RealFadeFixActive && need > 0 && missing == 0 && extra == 0 && mgr.TestRealDraws > 0,
                $"back to true: painted again ({need} zones need a copy, {missing} without, {mgr.TestRealDraws} drawn)");
            c.Report($"{need} real zones past the fade painted, none when the fix is off");
        }
        finally
        {
            hold.Release();
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.interior

    private static void ObjectRenderers(out int total, out int on)
    {
        total = 0;
        on = 0;
        var root = GameObject.Find("DistantHorizons_Objects");
        if (root == null)
        {
            return;
        }
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            total++;
            if (r.enabled)
            {
                on++;
            }
        }
    }

    private static IEnumerator RunInterior()
    {
        const string N = InteriorName;
        var c = new Kit.Checks(N);
        Kit.Flight flight = null;
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the paint checks need it"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var mgr = LodTerrainManager.Instance;
            var player = Player.m_localPlayer;
            Kit.Force(cfg.FarWater, true);
            Kit.Force(cfg.ObjectsEnabled, true);
            var sea = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws > 0, 10f, sea);
            var objects = new Kit.Box();
            yield return Kit.WaitUntil(() => { ObjectRenderers(out var t, out _); return t > 0; }, 25f, objects);
            yield return Kit.Frames(2);

            mgr.TestTileRenderers(out var shown, out var enabled, out _, out _);
            ObjectRenderers(out var objTotal, out var objOn);
            c.Check(sea.Ok && enabled > 0 && mgr.TestTileDraws > 0,
                $"outside: far tiles drawn ({enabled} of {shown} renderers on, {mgr.TestTileDraws} painted), far sea painted ({mgr.TestSeaDraws} bands)");
            var hadObjects = objTotal > 0;
            if (hadObjects)
            {
                c.Check(objOn == objTotal, $"outside: far objects drawn ({objOn} of {objTotal} renderers on)");
            }
            else
            {
                c.Note("no far object is drawn around here: the far-object part of this test has nothing to look at");
            }

            // In: what the game calls an interior is "above 3000 m" (every dungeon sits up there).
            flight = new Kit.Flight(player);
            flight.Put(flight.Origin + Vector3.up * InteriorLift);
            var hid = new Kit.Box();
            yield return Kit.WaitUntil(() => !RenderGroupSystem.IsGroupActive(RenderGroup.Overworld), 3f, hid);
            yield return Kit.Frames(3);
            c.Check(player.InInterior() && hid.Ok, "lifted above 3000 m the player counts as inside and the game hides the overworld");
            mgr.TestTileRenderers(out shown, out enabled, out _, out _);
            ObjectRenderers(out objTotal, out objOn);
            c.Check(enabled == 0, $"inside: no far tile renderer is on ({enabled} of {shown})");
            c.Check(mgr.TestTileDraws == 0 && mgr.TestRealDraws == 0, $"inside: no far ground painted ({mgr.TestTileDraws} tiles, {mgr.TestRealDraws} real-zone copies)");
            c.Check(mgr.TestSeaDraws == 0 && !mgr.SeaPainting, $"inside: no far sea painted ({mgr.TestSeaDraws} bands)");
            c.Check(objOn == 0, $"inside: no far object renderer is on ({objOn} of {objTotal})");
            yield return Kit.Shot(N, "inside");

            // Out again.
            flight.Land();
            var back = new Kit.Box();
            yield return Kit.WaitUntil(() => RenderGroupSystem.IsGroupActive(RenderGroup.Overworld) && mgr.SeaPainting && mgr.TestSeaDraws > 0, 8f, back);
            yield return Kit.Frames(3);
            mgr.TestTileRenderers(out shown, out enabled, out _, out _);
            ObjectRenderers(out objTotal, out objOn);
            c.Check(back.Ok, "outside again: the overworld is shown and the far sea is painted again");
            c.Check(enabled == shown && shown > 0 && mgr.TestTileDraws > 0, $"outside again: far tiles are back ({enabled} of {shown} renderers on, {mgr.TestTileDraws} painted)");
            c.Check(!hadObjects || (objTotal > 0 && objOn == objTotal), $"outside again: far objects are back ({objOn} of {objTotal} renderers on)");
            yield return Kit.Shot(N, "outside-again");
            c.Report("nothing far is drawn while inside, all of it is back outside");
        }
        finally
        {
            if (flight != null)
            {
                flight.End();
            }
            Kit.PutBack();
        }
    }

    // ---------- horizons.interior-off

    private static IEnumerator RunInteriorOff()
    {
        const string N = InteriorOffName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        Kit.Flight flight = null;
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the paint checks need it"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var mgr = LodTerrainManager.Instance;
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            var tl = TerrainLink.Current;
            var errors = ErrorWatch.Errors;
            Kit.Force(cfg.FarWater, true);
            Kit.Force(cfg.RealTerrainFadeFix, true);
            Kit.ForceFogDefaults();
            var box = new Kit.Box();
            weather.Force("Clear", 0.5f);
            yield return Kit.WaitWeather("Clear", box);
            c.Check(box.Ok, box.Detail);
            var raw = EnvMan.instance.GetEnv("Clear") != null ? EnvMan.instance.GetEnv("Clear").m_fogDensityDay : -1f;
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws > 0, 10f, box);
            yield return Kit.Frames(3);
            LodTerrainManager.TestRealZoneLeftovers(out _, out var sunkBefore, out _, out _);
            c.Check(sunkBefore > 0, $"before: some real zones are depth-only under painted copies ({sunkBefore})");
            c.Check(Kit.Near(RenderSettings.fogDensity, raw * 0.25f), $"before: clear-weather fog is thinned ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");

            flight = new Kit.Flight(player);
            flight.Put(flight.Origin + Vector3.up * InteriorLift);
            var hid = new Kit.Box();
            yield return Kit.WaitUntil(() => !RenderGroupSystem.IsGroupActive(RenderGroup.Overworld), 3f, hid);
            yield return Kit.Frames(3);
            c.Check(player.InInterior() && hid.Ok, "lifted above 3000 m the player counts as inside and the game hides the overworld");
            LodTerrainManager.TestRealZoneLeftovers(out _, out var sunkInside, out _, out _);
            c.Note($"inside, before turning off: {sunkInside} real zones still carry the depth-only values (their renderers are off)");

            Kit.SwitchOff();
            yield return Kit.Frames(2);
            c.Check(!Plugin.Instance.IsActive && LodTerrainManager.Instance == null && DistantObjectManager.Instance == null,
                $"turned off while inside: mod inactive, both managers gone (status '{Plugin.Instance.StatusText}')");

            // Walk out.
            flight.Land();
            yield return Kit.WaitUntil(() => RenderGroupSystem.IsGroupActive(RenderGroup.Overworld), 3f, box);
            yield return Kit.WaitFixed(3);
            LodTerrainManager.TestRealZoneLeftovers(out var zones, out var sunk, out var forcedOff, out var tessOff);
            c.Check(zones > 0 && sunk == 0, $"outside: no real zone is left depth-only ({sunk} of {zones})");
            c.Check(forcedOff == 0 && tessOff == 0, $"outside: no real zone left forced off ({forcedOff}) or with changed tessellation ({tessOff})");
            var meshes = new Kit.Box();
            yield return Kit.WaitUntil(() => tl != null && tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, $"outside: the vanilla distant terrain has its meshes within 20 s ({Kit.VanillaMeshes(tl)} of {(tl != null ? tl.m_heightmaps.Count : 0)})");
            Kit.LodPlanes(out var planes, out var hidden, out var visible, out _);
            c.Check(hidden == 0 && (planes == 0 || Kit.Near(visible, Kit.VanillaPlaneDistance())),
                $"outside: the game's distant water plane has the game's value ({Kit.F(visible)}, the game writes {Kit.F(Kit.VanillaPlaneDistance())}; {hidden} of {planes} out of reach)");
            c.Check(Kit.Buffers(cam, null) == 0, $"outside: no Distant Horizons buffer on the main camera ({Kit.Buffers(cam, null)})");
            c.Check(Kit.Roots("DistantHorizons_LOD") == 0 && Kit.Roots("DistantHorizons_Objects") == 0 && Kit.Roots("DH_ImpostorBakeRig") == 0,
                "outside: no far tile, far object or bake object left");
            c.Check(Kit.Near(RenderSettings.fogDensity, raw), $"outside: clear-weather fog is the game's own ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            yield return Kit.Shot(N, "off-outside");

            Kit.SwitchOn();
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 60f);
            c.Check(Plugin.Instance.IsActive && up.Ok, "turned on again: " + up.Detail);
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("turned off inside a dungeon, the overworld is plain vanilla when walking out");
        }
        finally
        {
            if (flight != null)
            {
                flight.End();
            }
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.stream

    private static IEnumerator RunStream()
    {
        const string N = StreamName;
        const float Speed = 90f;
        const float Length = 2000f;
        var c = new Kit.Checks(N);
        Kit.Flight flight = null;
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 20f);
            c.Note("before: " + settled.Detail);
            var mgr = LodTerrainManager.Instance;
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            var wg = WorldGenerator.instance;
            var water = ZoneSystem.instance.m_waterLevel;
            var radius = Plugin.Cfg.V(Plugin.Cfg.WorldRadius);
            flight = new Kit.Flight(player);
            var origin = flight.Origin;
            var dir = (origin + Vector3.right * Length).magnitude < radius - 600f ? Vector3.right : Vector3.left;
            var errorsBefore = ErrorWatch.Errors;

            int checks = 0, holeChecks = 0, worstHoles = 0, notShownChecks = 0, overlapChecks = 0, groundListed = 0, frames = 0, coarsestUnder = 0;
            var firstHole = "";
            var worstFrame = 0f;
            var flown = 0f;
            var flightStart = Kit.Now;
            for (var leg = 0; leg < 2; leg++)
            {
                var t0 = Kit.Now;
                var nextCheck = 0f;
                var nextGround = 0f;
                while (true)
                {
                    var elapsed = Kit.Now - t0;
                    var d = Mathf.Min(Length, elapsed * Speed);
                    var along = leg == 0 ? d : Length - d;
                    var p = origin + dir * along;
                    p.y = Mathf.Max(wg.GetHeight(p.x, p.z), water) + 100f;
                    flight.Put(p);
                    frames++;
                    if (frames > 2)
                    {
                        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                    }
                    if (elapsed >= nextCheck && LodTerrainManager.Instance == mgr && mgr != null)
                    {
                        nextCheck = elapsed + 0.25f;
                        checks++;
                        var cp = cam.transform.position;
                        Kit.Coverage(mgr, cp.x, cp.z, 2560f, 128f, out _, out var holes, out _, out var hole);
                        if (holes > 0)
                        {
                            holeChecks++;
                            worstHoles = Mathf.Max(worstHoles, holes);
                            if (firstHole.Length == 0)
                            {
                                firstHole = $"{hole} with the camera at ({cp.x:0},{cp.z:0})";
                            }
                        }
                        mgr.TestSurfaceCut(out var overlaps, out var notShown);
                        if (overlaps > 0)
                        {
                            overlapChecks++;
                        }
                        if (notShown > 0)
                        {
                            notShownChecks++;
                        }
                        if (mgr.TrySampleSurface(cp.x, cp.z, out _, out var under) && under.Level > coarsestUnder)
                        {
                            coarsestUnder = under.Level;
                        }
                    }
                    if (elapsed >= nextGround)
                    {
                        nextGround = elapsed + 2f;
                        Kit.FarTilesAsGround(out _, out var listed, out var notDistant);
                        groundListed += listed + notDistant;
                    }
                    if (d >= Length)
                    {
                        break;
                    }
                    yield return null;
                }
                flown += Length;
            }
            var flightSeconds = Kit.Now - flightStart;
            c.Note($"flew {flown:0} m in {flightSeconds:0.0} s ({frames} frames, {frames / Mathf.Max(0.1f, flightSeconds):0} per second, longest frame {worstFrame * 1000f:0} ms); "
                   + $"{checks} checks, coarsest far tile under the camera on the way: level {coarsestUnder}");
            c.Check(checks >= 40, $"enough checks on the way ({checks})");
            c.Check(holeChecks == 0, $"far ground under every point within 2.5 km of the camera at every check ({holeChecks} of {checks} checks found holes, at most {worstHoles} points, first {firstHole})");
            c.Check(notShownChecks == 0, $"every surface tile was built and switched on at every check ({notShownChecks} checks found one that was not)");
            c.Check(overlapChecks == 0, $"no far ground drawn twice at any check ({overlapChecks} checks)");
            c.Check(groundListed == 0, $"no far tile was ever listed with the game's real ground heightmaps ({groundListed})");
            c.Check(worstFrame < 3f, $"no long freeze on the way (longest frame {worstFrame:0.00} s)");

            // Stopped over the start place: zones load, then the finest tiles come.
            var stop = Kit.Now;
            var loaded = new Kit.Box();
            yield return Kit.WaitUntil(() => ZoneSystem.instance.IsActiveAreaLoaded() && ZNetScene.instance.IsAreaReady(origin), 25f, loaded);
            var loadSeconds = Kit.Now - stop;
            c.Check(loaded.Ok, $"zones around the player loaded again within 25 s of stopping ({loadSeconds:0.0} s)");
            if (loaded.Ok)
            {
                flight.Land();
            }
            var fine = new Kit.Box();
            var fineStart = Kit.Now;
            yield return Kit.WaitUntil(() =>
            {
                var cp = cam.transform.position;
                return LodTerrainManager.Instance != null && LodTerrainManager.Instance.TrySampleSurface(cp.x, cp.z, out _, out var k) && k.Level == 0;
            }, 15f, fine);
            c.Check(fine.Ok, $"the finest far tile is under the camera within 15 s of the zones being loaded ({Kit.Now - fineStart:0.0} s)");
            c.Check(ErrorWatch.Errors == errorsBefore, $"no error from Distant Horizons on the way ({ErrorWatch.Errors - errorsBefore}: {ErrorWatch.FirstError})");
            c.Report($"{flown:0} m flown, far ground whole at {checks} checks, zones back after {loadSeconds:0.0} s");
        }
        finally
        {
            if (flight != null)
            {
                flight.End();
            }
            Kit.PutBack();
        }
    }
}
#endif
