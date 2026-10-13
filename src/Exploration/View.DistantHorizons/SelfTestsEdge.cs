#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. One small in-world self test per edge case.
// Two that were wrong until 0.1.1 (each was a "bug" test of the same name until then). No other test has their
// set-up (mod turned on in a paused game; terrain builder robbed of finished heights), so each stays a test of its
// own:
//   horizons.paused-objects    game paused, mod off then on: once the far terrain is done, far objects have looked
//                              at the final terrain and near far trees stand on it (they do not wait for the unpause)
//   horizons.exact-evict       builder drops the finished heights of an exact tile before the manager takes them
//                              (hook copies what the builder's own 16-entry trim does): the tile is built from exact
//                              heights again, and no tile flagged exact gets built from approximate ones
// One that the user accepted as it is (2026-10-08; README "Good to know"), was a "bug" test with a 0.5 s limit. No
// other test watches every frame after a turn-on, so it stays a test of its own:
//   horizons.attach-cover      mod off then on: coarse far tiles keep ground-level vertices inside loaded real zones
//                              for at most 2 s (until the fine tiles are built), and no far vertex stands above the
//                              real ground meanwhile; screenshot of that moment
// And one that is no bug (the README says so), kept because the setting is easy to get wrong:
//   horizons.viewdistance-sea  ViewDistance 5000: the far sea ends there at once (README: "also the outer edge of the
//                              far sea"), the far land is still whole; back to 22000 = sea to the world's edge again
internal static class EdgeTests
{
    private const string PausedObjectsName = "horizons.paused-objects";
    private const string AttachCoverName = "horizons.attach-cover";
    private const string ExactEvictName = "horizons.exact-evict";
    private const string ViewDistanceSeaName = "horizons.viewdistance-sea";

    internal static void Register()
    {
        SelfTest.Register(PausedObjectsName, RunPausedObjects);
        SelfTest.Register(AttachCoverName, RunAttachCover);
        SelfTest.Register(ExactEvictName, RunExactEvict);
        SelfTest.Register(ViewDistanceSeaName, RunViewDistanceSea);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(PausedObjectsName);
        SelfTest.Unregister(AttachCoverName);
        SelfTest.Unregister(ExactEvictName);
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

    // ---------- horizons.paused-objects

    // Far objects look at far terrain on real time (DistantObjectManager.Tick), so they follow it in a paused game too.
    // Until 0.1.1 that timer stood still there: trees kept standing on the first coarse tiles until the unpause.
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

    // ---------- horizons.attach-cover

    // Right after attach the first far tiles shown are coarse ones. Skirt push a vertex down only when it and its
    // four neighbours lie in loaded zones (LodTerrainManager.ApplyCoverage), so coarse tiles keep vertices at ground
    // level inside the loaded zones until the exact fine tiles take their place (measured 1.3 to 1.4 s, four runs
    // of 2026-10-08). User accepted that (mod not changed; README "Good to know" say "up to about 2 seconds"): limit
    // is 2 s, was 0.5 s. What must hold meanwhile: no far vertex above the real ground (measured 0 within 200 m).
    private const float AttachCoverLimit = 2f;

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
            var coverLooks = 0;
            var coverPoking = 0;
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
                    // Every half second, and at once in the first frame coarse tiles sit inside the loaded zones (so
                    // "meanwhile" below is never judged on no look at all).
                    if (Kit.Now >= nextProbe || (vertices > 0 && coverLooks == 0))
                    {
                        nextProbe = Kit.Now + 0.5f;
                        mgr.ProbeNearGround(cam.transform.position, player.transform.position, 200f, out var poking, out _);
                        worstPoking = Mathf.Max(worstPoking, poking);
                        // Look made while coarse tiles still sit inside the loaded zones = "meanwhile".
                        if (vertices > 0)
                        {
                            coverLooks++;
                            coverPoking = Mathf.Max(coverPoking, poking);
                        }
                    }
                }
                yield return null;
            }
            var open = lastOpen < 0f ? 0f : lastOpen - firstSurface;
            c.Note($"after turning on: first far ground {(firstSurface < 0f ? -1f : firstSurface - start):0.0} s later; coarse tiles kept ground-level vertices inside loaded zones for {open:0.0} s "
                   + $"(up to {worstVertices} vertices, coarsest tile level {worstLevel}); far-tile vertices above the real ground within 200 m: up to {coverPoking} at the {coverLooks} looks made meanwhile, "
                   + $"up to {worstPoking} in the whole 12 s");
            c.Check(firstSurface >= 0f, "the far terrain came back within 12 s");
            c.Check(open <= AttachCoverLimit,
                $"after turning on, the coarse far tiles are out of the loaded real zones within {AttachCoverLimit:0} s: none keeps ground-level vertices inside them longer (it lasted {open:0.0} s: up to {worstVertices} vertices, tile level up to {worstLevel})");
            c.Check(coverPoking == 0,
                $"…and meanwhile no far-tile vertex stands above the real ground within 200 m of the player (up to {coverPoking} at {coverLooks} looks, one every 0.5 s)");
            c.Report($"coarse far tiles leave the loaded real zones within {AttachCoverLimit:0} s of turning on, nothing far above the real ground meanwhile");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.exact-evict

    // Tile flagged exact take exact heights only (LodTerrainManager.Fetch). Until 0.1.1 it took the plain ones the
    // builder made after it dropped the exact ones: flagged exact, built from approximate heights.
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
            // What the fix does (0.1.1): plain heights that come back for an exact tile are dropped and the exact job
            // goes on the builder's queue again, so the tile stays exact.
            c.Check(tile.Exact && tile.BuiltFromExact,
                $"…it is still an exact tile and its mesh came from exact heights asked from the builder again (tile {key}: flagged exact {tile.Exact}, built from {(tile.BuiltFromExact ? "exact" : "approximate")} heights)");
            c.Check(mgr.TestExactFromPlain == 0,
                $"…and no tile flagged exact was built from approximate heights since this far terrain started ({mgr.TestExactFromPlain})");
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
