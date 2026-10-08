#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. One small in-world self test per edge case where the mod is wrong today ("horizons.bug.*"): each
// one checks the RIGHT behaviour, so it fails until the mod is fixed, and it fails alone (nothing else is mapped to
// it). More of them in SelfTestsObjects.cs (horizons.bug.simdistance-still, horizons.bug.tree-reseat).
//   horizons.bug.paused-objects  game paused, mod off then on: once the far terrain is done, far objects have looked
//                              at the final terrain and near far trees stand on it (today they wait for the unpause)
//   horizons.bug.attach-cover  mod off then on: coarse far tiles must not keep ground-level vertices inside loaded
//                              real zones for more than half a second (today they do until the fine tiles are built);
//                              screenshot of that moment
//   horizons.bug.exact-evict   builder drops the finished heights of an exact tile before the manager takes them
//                              (hook copies what the builder's own 16-entry trim does): the tile must end exact or
//                              not flagged exact (today: flagged exact, built from approximate heights)
//   horizons.bug.off-foreign-errors  mod turned off while "inside" (above 3000 m), then back down while the grass
//                              grows again: no error line of ANY plugin in the log (run of 2026-10-07: 22 exceptions
//                              from Valheim Community Patch's grass ground lookup while the game's own distant
//                              terrain came back)
// And one that is no bug (the README says so), kept because the setting is easy to get wrong:
//   horizons.viewdistance-sea  ViewDistance 5000: the far sea ends there at once (README: "also the outer edge of the
//                              far sea"), the far land is still whole; back to 22000 = sea to the world's edge again
internal static class EdgeTests
{
    private const string PausedObjectsName = "horizons.bug.paused-objects";
    private const string AttachCoverName = "horizons.bug.attach-cover";
    private const string ExactEvictName = "horizons.bug.exact-evict";
    private const string OffForeignName = "horizons.bug.off-foreign-errors";
    private const string ViewDistanceSeaName = "horizons.viewdistance-sea";

    private const float InteriorLift = 4000f;

    internal static void Register()
    {
        SelfTest.Register(PausedObjectsName, RunPausedObjects);
        SelfTest.Register(AttachCoverName, RunAttachCover);
        SelfTest.Register(ExactEvictName, RunExactEvict);
        SelfTest.Register(OffForeignName, RunOffForeignErrors);
        SelfTest.Register(ViewDistanceSeaName, RunViewDistanceSea);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(PausedObjectsName);
        SelfTest.Unregister(AttachCoverName);
        SelfTest.Unregister(ExactEvictName);
        SelfTest.Unregister(OffForeignName);
        SelfTest.Unregister(ViewDistanceSeaName);
    }

    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null)
        {
            c.Check(false, "no local player or camera");
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

    // ---------- horizons.bug.paused-objects

    private static IEnumerator RunPausedObjects()
    {
        const string N = PausedObjectsName;
        var c = new Kit.Checks(N);
        var made = new List<ZDOID>();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            Kit.Force(cfg.ObjectsEnabled, true);
            Kit.Force(cfg.DrawTrees, true);
            // A tree of my own in the world data about 1.1 km away, so a near far tree exists whatever the world holds.
            var planted = ObjectTests.PlantNearTree(made);
            c.Note(planted != null ? $"a {planted} was put in the world data about 1.1 km away for this test" : "no tree prefab to put in the world");
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f);
            c.Check(settled.Ok, "before: " + settled.Detail);

            Game.Pause();
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => Time.timeScale == 0f, 3f, box);
            if (!c.Check(box.Ok, "the game pauses (single player)"))
            {
                c.Report("");
                yield break;
            }
            // Off and on again while paused (the code the toggle runs; no Status line touched).
            TerrainLink.Detach();
            yield return Kit.Frames(2);
            TerrainLink.AttachIfInWorld();
            yield return Kit.Settle(settled, 50f);
            if (!c.Check(settled.Ok && Time.timeScale == 0f, "paused, turned on again: the far terrain finished building: " + settled.Detail))
            {
                c.Report("");
                yield break;
            }
            yield return new WaitForSecondsRealtime(2f);
            var mgr = LodTerrainManager.Instance;
            var om = DistantObjectManager.Instance;
            c.Check(Time.timeScale == 0f, "still paused");
            c.Note($"paused: far terrain surface version {mgr.SurfaceVersion}, far objects last looked at version {om.TestSurfaceVersion}; {om.GetStats()}");
            c.Check(om.TestSurfaceVersion == mgr.SurfaceVersion,
                $"paused, 2 s after the far terrain finished: the far objects have looked at the final terrain (they looked at surface version {om.TestSurfaceVersion}, the terrain is at {mgr.SurfaceVersion})");
            ObjectTests.Seated(c, om, mgr, "paused, turned on again", true);
            yield return Kit.Shot(N, "paused-on-again");

            Game.Unpause();
            yield return Kit.WaitUntil(() => Time.timeScale > 0f, 3f, box);
            yield return new WaitForSecondsRealtime(1.5f);
            c.Note($"1.5 s after resuming: far objects looked at surface version {om.TestSurfaceVersion}, terrain at {mgr.SurfaceVersion}");
            c.Report("far objects follow the terrain while the game is paused");
        }
        finally
        {
            Kit.Safe("unpause", () =>
            {
                if (Game.IsPaused())
                {
                    Game.Unpause();
                }
            });
            Kit.Safe("test tree", () => ObjectTests.Unmake(made));
            Kit.PutBack();
        }
    }

    // ---------- horizons.bug.attach-cover

    private static IEnumerator RunAttachCover()
    {
        const string N = AttachCoverName;
        var c = new Kit.Checks(N);
        try
        {
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
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            LodTerrainManager.Instance.TestCoarseOverLoaded(out _, out var steady, out _);
            c.Check(settled.Ok && steady == 0, $"settled: no coarse far tile has ground-level vertices inside loaded real zones ({steady}); {settled.Detail}");

            TerrainLink.Detach();
            yield return Kit.Frames(2);
            TerrainLink.AttachIfInWorld();
            var start = Kit.Now;
            var firstSurface = -1f;
            var lastOpen = -1f;
            var worstVertices = 0;
            var worstLevel = -1;
            var worstPoking = 0;
            var nextProbe = 0f;
            var shot = false;
            while (Kit.Now - start < 12f)
            {
                var mgr = LodTerrainManager.Instance;
                if (mgr != null && mgr.TestSurfaceCount > 0)
                {
                    if (firstSurface < 0f)
                    {
                        firstSurface = Kit.Now;
                    }
                    mgr.TestCoarseOverLoaded(out _, out var vertices, out var level);
                    if (vertices > 0)
                    {
                        lastOpen = Kit.Now;
                        worstVertices = Mathf.Max(worstVertices, vertices);
                        worstLevel = Mathf.Max(worstLevel, level);
                        // One picture of that moment, for a human: is coarse ground really seen on the grass?
                        if (!shot && Kit.Now - firstSurface >= 0.3f)
                        {
                            shot = true;
                            SelfTest.Screenshot(N, "coarse-ground-over-loaded-zones");
                        }
                    }
                    if (Kit.Now >= nextProbe)
                    {
                        nextProbe = Kit.Now + 0.5f;
                        mgr.ProbeNearGround(cam.transform.position, player.transform.position, 200f, out var poking, out _);
                        worstPoking = Mathf.Max(worstPoking, poking);
                    }
                }
                yield return null;
            }
            var open = lastOpen < 0f ? 0f : lastOpen - firstSurface;
            c.Note($"after turning on: first far ground {(firstSurface < 0f ? -1f : firstSurface - start):0.0} s later; coarse tiles kept ground-level vertices inside loaded zones for {open:0.0} s "
                   + $"(up to {worstVertices} vertices, coarsest tile level {worstLevel}); far-tile vertices above the real ground within 200 m: up to {worstPoking}");
            c.Check(firstSurface >= 0f, "the far terrain came back within 12 s");
            c.Check(open <= 0.5f,
                $"after turning on, no coarse far tile keeps ground-level vertices inside loaded real zones for more than 0.5 s (it lasted {open:0.0} s: up to {worstVertices} vertices, tile level up to {worstLevel})");
            c.Report("coarse far tiles never lie over loaded real ground");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.bug.exact-evict

    private static IEnumerator RunExactEvict()
    {
        const string N = ExactEvictName;
        var c = new Kit.Checks(N);
        var rebuild = false;
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
            yield return Kit.Settle(settled, 30f);
            var mgr = LodTerrainManager.Instance;
            c.Note($"exact tiles built from approximate heights so far this session: {mgr.TestExactFromPlain}");

            // Rebuild: every exact tile near the camera is asked from the builder again; the first one whose finished
            // heights wait on the builder's list loses them before the manager takes them.
            mgr.TestEvicted = false;
            mgr.TestEvictExact = true;
            rebuild = true;
            mgr.Rebuild();
            var evicted = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.TestEvicted, 60f, evicted);
            if (!c.Check(evicted.Ok, "an exact tile's finished heights were taken off the builder's list before the manager fetched them (test set-up)"))
            {
                c.Report("");
                yield break;
            }
            var key = mgr.TestEvictedKey;
            var built = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.TestTile(key, out var t) && t.State == TileState.Built, 45f, built);
            if (!c.Check(built.Ok && mgr.TestTile(key, out _), $"the tile {key} got built afterwards ({built.Detail})"))
            {
                c.Report("");
                yield break;
            }
            mgr.TestTile(key, out var tile);
            c.Note($"tile {key}: flagged exact {tile.Exact}, mesh built from exact heights {tile.BuiltFromExact}, vertex spacing {tile.Scale:0.##} m");
            c.Check(!tile.Exact || tile.BuiltFromExact,
                $"a tile whose exact heights the builder dropped is built exact again or no longer flagged exact (tile {key}: flagged exact {tile.Exact}, built from {(tile.BuiltFromExact ? "exact" : "approximate")} heights; "
                + "flagged exact it is drawn NearTerrainOffset lower and trusted to meet the real ground)");
            c.Report("exact tiles are only ever built from exact heights");
        }
        finally
        {
            Kit.Safe("evict switch", () =>
            {
                var m = LodTerrainManager.Instance;
                if (m == null)
                {
                    return;
                }
                m.TestEvictExact = false;
                if (rebuild)
                {
                    m.Rebuild(); // the tile with wrong heights go away
                }
            });
            Kit.PutBack();
        }
    }

    // ---------- horizons.bug.off-foreign-errors

    // Same walk as horizons.interior-off (up above 3000 m = "inside", mod off through the framework, back down), but
    // what is counted is every error line of the log, whoever wrote it. Up there the grass around the player is thrown
    // away; back down it grows again while the game rebuilds its own distant terrain. Run of 2026-10-07 (with Valheim
    // Community Patch 0.31.0 installed): 22 "ArgumentOutOfRangeException ... Heightmap.GetHeight ...
    // ValheimCommunityPatch.HeightmapSampling.Sample ... ClutterSystem.LateUpdate" between the mod turning off and
    // the game's distant terrain having its meshes. Other mod's code throws, my turn-off is what sets it off.
    private static IEnumerator RunOffForeignErrors()
    {
        const string N = OffForeignName;
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
            var player = Player.m_localPlayer;
            var tl = TerrainLink.Current;
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f);
            c.Check(settled.Ok, "before: " + settled.Detail);

            flight = new Kit.Flight(player);
            flight.Put(flight.Origin + Vector3.up * InteriorLift);
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => !RenderGroupSystem.IsGroupActive(RenderGroup.Overworld), 3f, box);
            yield return Kit.Frames(3);
            c.Check(box.Ok && player.InInterior(), "lifted above 3000 m the player counts as inside and the game hides the overworld");
            yield return new WaitForSecondsRealtime(0.5f);
            var quiet = ErrorWatch.AnyErrors;

            Kit.SwitchOff();
            yield return Kit.Frames(2);
            c.Check(!Plugin.Instance.IsActive && LodTerrainManager.Instance == null, $"turned off while inside (status '{Plugin.Instance.StatusText}')");

            flight.Land();
            yield return Kit.WaitUntil(() => RenderGroupSystem.IsGroupActive(RenderGroup.Overworld), 3f, box);
            var meshes = new Kit.Box();
            yield return Kit.WaitUntil(() => tl != null && tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(box.Ok && meshes.Ok, $"back outside the game's own distant terrain has its meshes ({meshes.Detail})");
            // Grass keeps growing for a moment after the player is back on the ground.
            yield return new WaitForSecondsRealtime(1.5f);
            var logged = ErrorWatch.AnyErrors - quiet;
            c.Check(logged == 0,
                $"turned off inside, back outside: no error line of any plugin in the log ({logged} error lines, the last: {ErrorWatch.LastAnyError})");

            Kit.SwitchOn();
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 60f);
            c.Check(Plugin.Instance.IsActive && up.Ok, "turned on again: " + up.Detail);
            c.Report("turning the mod off sets off no error in other plugins");
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

    // ---------- horizons.viewdistance-sea

    private static IEnumerator RunViewDistanceSea()
    {
        const string N = ViewDistanceSeaName;
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
            Kit.Force(cfg.FarWater, true);
            Kit.Force(cfg.ViewDistance, 22000f);
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws > 0, 10f, box);
            yield return Kit.Frames(3);
            var radius = cfg.V(cfg.WorldRadius);
            if (!c.Check(box.Ok && mgr.Water.Outer >= radius, $"ViewDistance 22000: the far sea is painted out to the world's edge ({mgr.Water.Outer:0} m, world radius {radius:0} m)"))
            {
                c.Report("");
                yield break;
            }
            // README, ViewDistance: "Tiles farther than this ... are never subdivided, but still drawn as one coarse
            // tile ... Also the outer edge of the far sea." (and: lower ViewDistance and CameraFarClip together).
            cfg.SetForTest(cfg.ViewDistance, 5000f);
            yield return Kit.Frames(5);
            Kit.Coverage(mgr, 0f, 0f, radius, 256f, out var points, out var holes, out _, out _);
            c.Note($"ViewDistance 5000: far sea sheet {mgr.Water.Inner:0} m to {mgr.Water.Outer:0} m, far ground under {points - holes} of {points} points of the world disc. "
                   + $"Beyond the sheet no water is drawn (the game's own distant water plane stays out of reach while the far sea is painted), so with CameraFarClip left at {cfg.CameraFarClip.Value:0} m the sea bed out there is drawn as dry land");
            c.Check(holes == 0, "ViewDistance = 5000: the far land is still drawn to the world's edge");
            c.Check(Kit.Near(mgr.Water.Outer, 5000f),
                $"ViewDistance = 5000: the far sea ends 5000 m out at once, as the README says (sheet {mgr.Water.Inner:0} m to {mgr.Water.Outer:0} m)");
            c.Check(mgr.SeaPainting && mgr.TestSeaDraws > 0, $"ViewDistance = 5000: the far sea is still painted ({mgr.TestSeaDraws} bands)");
            cfg.SetForTest(cfg.ViewDistance, 22000f);
            yield return Kit.Frames(5);
            c.Check(mgr.Water.Outer >= radius, $"ViewDistance back to 22000: the far sea reaches the world's edge again ({mgr.Water.Outer:0} m, world radius {radius:0} m)");
            c.Report("the far sea ends at ViewDistance (README), the far land stays whole");
        }
        finally
        {
            Kit.PutBack();
        }
    }
}
#endif
