#if DEBUG
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self tests that do the real thing where an older test took a shortcut:
//   horizons.seam-hills   T02: horizons.seam stands at the spawn point. Here the player is flown low (12 m over the
//                         ground) to the hilliest place 300 to 800 m away and stopped there. Once the real zones of
//                         the old place are gone (the game drops them one by one): where the loaded real ground ends,
//                         the far ground is exact, never above the real ground and at most NearTerrainOffset + 0.6 m
//                         under it; no far vertex above real ground within 200 m; every far tile keeps my hide
//                         distance. On the way there and back: far ground under every point at every look. Screenshot
// Player moved with debug fly (Kit.Flight), put back at the end.
internal static class RealTests
{
    private const string SeamHillsName = "horizons.seam-hills";

    internal static void Register()
    {
        SelfTest.Register(SeamHillsName, RunSeamHills);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(SeamHillsName);
    }

    // Height range of the land within 60 m of a point (world generator heights), -1 = water or shore there.
    private static float Relief(WorldGenerator wg, float water, float x, float z, out float height)
    {
        height = wg.GetHeight(x, z);
        if (height < water + 3f)
        {
            return -1f;
        }
        var min = height;
        var max = height;
        for (var k = 0; k < 8; k++)
        {
            var a = k * Mathf.PI / 4f;
            for (var r = 30f; r <= 60f; r += 30f)
            {
                var h = wg.GetHeight(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r);
                if (h < water + 1f)
                {
                    return -1f;
                }
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
        }
        return max - min;
    }

    // Hilliest land place 300 to 800 m from the start. relief = its height range within 60 m (-1 = no land found).
    private static Vector3 FindHill(Vector3 from, out float relief)
    {
        var wg = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        var best = from;
        relief = -1f;
        for (var r = 300f; r <= 800f; r += 50f)
        {
            for (var i = 0; i < 24; i++)
            {
                var a = i * Mathf.PI / 12f;
                var x = from.x + Mathf.Cos(a) * r;
                var z = from.z + Mathf.Sin(a) * r;
                var here = Relief(wg, water, x, z, out var h);
                if (here > relief)
                {
                    relief = here;
                    best = new Vector3(x, h, z);
                }
            }
        }
        return best;
    }

    // Farthest loaded real zone from the zone the player stands in (zones, the larger of the two axes).
    private static int ZoneSpread(Vector3 at, out int zones)
    {
        var mine = ZoneSystem.GetZone(at);
        var spread = 0;
        zones = 0;
        foreach (var kv in ZoneSystem.instance.m_zones)
        {
            zones++;
            spread = Mathf.Max(spread, Mathf.Max(Mathf.Abs(kv.Key.x - mine.x), Mathf.Abs(kv.Key.y - mine.y)));
        }
        return spread;
    }

    private sealed class Way
    {
        internal int Looks;
        internal int HoleLooks;
        internal string FirstHole = "";
    }

    // Flown low in a straight line, 12 m over the ground under the player; far ground looked at every 0.25 s.
    private static IEnumerator FlyLow(Kit.Flight flight, Vector3 from, Vector3 to, float speed, Way way)
    {
        var wg = WorldGenerator.instance;
        var cam = Utils.GetMainCamera();
        var length = Utils.DistanceXZ(from, to);
        var t0 = Kit.Now;
        var nextLook = 0f;
        while (true)
        {
            var elapsed = Kit.Now - t0;
            var k = length <= 0.01f ? 1f : Mathf.Clamp01(elapsed * speed / length);
            var p = Vector3.Lerp(from, to, k);
            p.y = Mathf.Max(wg.GetHeight(p.x, p.z), ZoneSystem.instance.m_waterLevel) + 12f;
            flight.Put(p);
            var mgr = LodTerrainManager.Instance;
            if (elapsed >= nextLook && mgr != null && cam != null)
            {
                nextLook = elapsed + 0.25f;
                way.Looks++;
                var cp = cam.transform.position;
                Kit.Coverage(mgr, cp.x, cp.z, 1024f, 64f, out _, out var holes, out _, out var hole);
                if (holes > 0)
                {
                    way.HoleLooks++;
                    if (way.FirstHole.Length == 0)
                    {
                        way.FirstHole = $"{hole} with the camera at ({cp.x:0},{cp.z:0})";
                    }
                }
            }
            if (k >= 1f)
            {
                yield break;
            }
            yield return null;
        }
    }

    private static IEnumerator RunSeamHills()
    {
        const string N = SeamHillsName;
        const float Speed = 100f;
        var c = new Kit.Checks(N);
        Kit.Flight flight = null;
        try
        {
            if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || ZNet.instance == null)
            {
                c.Check(false, "no local player, camera or network object");
                c.Report("");
                yield break;
            }
            if (Plugin.Instance == null || !Plugin.Instance.IsActive)
            {
                c.Check(false, "Distant Horizons is not active: " + (Plugin.Instance != null ? Plugin.Instance.StatusText : "no plugin"));
                c.Report("");
                yield break;
            }
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up);
            if (!c.Check(up.Ok, up.Detail) || !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the near-ground checks need it"))
            {
                c.Report("");
                yield break;
            }
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            var offset = Plugin.Cfg.NearTerrainOffset.Value;
            var hill = FindHill(player.transform.position, out var relief);
            if (!c.Check(relief >= 5f, $"a hilly place 300 to 800 m from the start (hilliest found: ground height range {relief:0.0} m within 60 m, 5 m or more wanted)"))
            {
                c.Report("");
                yield break;
            }
            var errors = ErrorWatch.Errors;
            flight = new Kit.Flight(player);
            var home = flight.Origin;
            var way = new Way();
            var start = home + Vector3.up * 12f;
            var target = hill + Vector3.up * 12f;
            yield return FlyLow(flight, start, target, Speed, way);

            // Stopped. The real zones of the place just left are still loaded: the game drops them 4 s later, one per
            // tick. Until then real ground reaches farther than the far ground was made for, so nothing is judged.
            var zonesBox = new Kit.Box();
            yield return ObjectTests.ZonesSettle(35f, zonesBox, 5f);
            var near = ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance;
            var spread = ZoneSpread(player.transform.position, out var zones);
            if (!c.Check(zonesBox.Ok && spread <= near,
                    $"at the hilly place ({hill.x:0},{hill.z:0}), {Utils.DistanceXZ(hill, home):0} m from the start, height range {relief:0.0} m: only the real zones the game wants here are loaded "
                    + $"({zones} zones, the farthest {spread} zones away, the game loads {near} around the player; {zonesBox.Detail}); when old ones stay (other mods can keep them) the place where real ground ends cannot be judged"))
            {
                c.Report("");
                yield break;
            }
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 20f);
            var mgr = LodTerrainManager.Instance;
            if (!c.Check(settled.Ok && mgr != null, "the far terrain settled at the hilly place: " + settled.Detail))
            {
                c.Report("");
                yield break;
            }
            var rim = WorldTests.Rim(mgr, offset);
            WorldTests.RimChecks(c, rim, "hills: ", offset);
            mgr.TestTileRenderers(out var shown, out _, out var wrongHide, out var wrongWater);
            c.Check(shown > 0 && wrongHide == 0, $"hills: every shown far tile carries its own hide distance: nothing dissolves near the camera ({wrongHide} of {shown} wrong)");
            c.Check(wrongWater == 0, $"hills: every shown far tile renderer is depth-only under its painted copy ({wrongWater} of {shown} not)");
            var ground = hill;
            if (ZoneSystem.instance.GetGroundHeight(hill, out var gh))
            {
                ground.y = gh;
            }
            yield return Kit.Frames(3);
            var probe = mgr.ProbeNearGround(cam.transform.position, ground, 200f, out var poking, out var mismatched);
            c.Note($"hills, camera flying 12 m up: {(probe.Length > 400 ? probe.Substring(0, 400) + "..." : probe)}");
            // Nothing compared = "none above" would say nothing.
            var compared = Kit.ProbeCompared(probe);
            c.Check(compared > 0, $"hills: far-tile vertices over loaded real ground were there to compare within 200 m ({compared})");
            c.Check(poking == 0, $"hills: {poking} far-tile vertices above the real ground within 200 m");
            c.Check(mismatched == 0, $"hills: {mismatched} painted real zones whose depth renderer still tessellates");
            yield return Kit.Shot(N, "hills-12m-up");

            // Back the same way, low.
            yield return FlyLow(flight, target, start, Speed, way);
            c.Check(way.Looks >= 10 && way.HoleLooks == 0,
                $"flying low there and back: far ground under every point within 1 km of the camera at every look ({way.HoleLooks} of {way.Looks} looks found holes, first {way.FirstHole})");
            flight.Put(home + Vector3.up * 2f);
            var back = new Kit.Box();
            yield return Kit.WaitUntil(() => ZoneSystem.instance.IsActiveAreaLoaded() && ZNetScene.instance.IsAreaReady(home), 20f, back);
            if (c.Check(back.Ok, $"the start place loaded again within 20 s ({back.Detail})"))
            {
                flight.Land();
            }
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"real and far ground meet at a hilly place ({relief:0} m of height within 60 m, {Utils.DistanceXZ(hill, home):0} m from the start), {rim.Samples} points where the real ground ends");
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
