#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self tests about far objects, the simulation distance and the spyglass boost (world
// probe, tools/Test-InWorld.ps1):
//   horizons.far-objects       far objects drawn, none beyond ObjectFarDistance; then a tree and a building part put
//                              in the world data far away (ZDO only, never an object in the scene): drawn inside
//                              their distance (tree 4.5 km, building 1.2 km), not beyond it (8.1 km, 3.8 km); after
//                              that object rebuild near trees stand on the far ground; no tree card empty, white or
//                              black; removed from the world data = gone from the far tiles by themselves
//   horizons.simdistance       simulation distance lowered to the lowest and put back (ZNet.ApplySimulationDistance,
//                              what the graphics menu calls): in that same call every far tile keeps my hide distance
//                              and the game's distant water plane stays out of reach; sea inner radius follows; after
//                              a 40 m camera move: no zone without objects, none with real and far ones (zones never
//                              scanned count too); back: the same, rim seam fine. Near trees on the ground there get
//                              measured only: horizons.bug.tree-reseat judge them
//   horizons.simdistance-rim   same change (measured by horizons.simdistance): the real zones beyond the lowest
//                              distance unloaded (waited for: the game drops them after 4 s, one per tick; other
//                              mods can keep them), then no far vertex above real ground and the rim seam fine
//   horizons.bug.simdistance-still  same change, camera NOT moved: zones that lost their real objects get far ones
//                              (and zones that got real ones lose far ones) without the player moving. Fails today.
//   horizons.bug.tree-reseat   near trees stand on the far ground after it changed under them. Three looks: far terrain
//                              and far objects rebuilt together (not every run show the fault); one far terrain tile
//                              made to refine by a camera move while the object tile east of it keep its own ground
//                              (trees in the 32 m strip past that tile's west edge: show it every run); and what
//                              horizons.simdistance measured at the lowest simulation distance. Fails today.
//   horizons.spyglass          SpyglassDetail off and SpyglassMaxBoost 1 = a raised spyglass changes nothing; boost 4:
//                              finer built land in the view cone 2 to 5 km out, object tiles and buildings farther
//                              out, a turned view refines the new direction; boost 8 = more than 4; lowered = within
//                              2 s nothing wanted past the no-spyglass reach (SplitFactor * (1 + SplitHysteresis) tile
//                              widths: tiles inside that band stay split, by design), objects back inside their
//                              distances, no finer tile than wanted left drawn, no hole
// The spyglass is faked through ViewBoost.TestSlot (same layout the Spyglass mod writes).
internal static class ObjectTests
{
    private const string FarObjectsName = "horizons.far-objects";
    private const string SimDistanceName = "horizons.simdistance";
    private const string SimStillName = "horizons.bug.simdistance-still";
    private const string TreeReseatName = "horizons.bug.tree-reseat";
    private const string SimRimName = "horizons.simdistance-rim";
    private const string SpyglassName = "horizons.spyglass";

    internal static void Register()
    {
        SelfTest.Register(FarObjectsName, RunFarObjects);
        SelfTest.Register(SimDistanceName, RunSimDistance);
        SelfTest.Register(SimRimName, RunSimRim);
        SelfTest.Register(SimStillName, RunSimStill);
        SelfTest.Register(TreeReseatName, RunTreeReseat);
        SelfTest.Register(SpyglassName, RunSpyglass);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(FarObjectsName);
        SelfTest.Unregister(SimDistanceName);
        SelfTest.Unregister(SimRimName);
        SelfTest.Unregister(SimStillName);
        SelfTest.Unregister(TreeReseatName);
        SelfTest.Unregister(SpyglassName);
    }

    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || GameCamera.instance == null || ZNet.instance == null)
        {
            c.Check(false, "no local player, camera or network object");
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

    // Trees of near object tiles (mesh and full band) stand on the far ground drawn under them now.
    // mustHave: no sampled tree at all = failed check (else only a note).
    internal static void Seated(Kit.Checks c, DistantObjectManager om, LodTerrainManager mgr, string when, bool mustHave)
    {
        var samples = new List<Vector3>();
        om.TestNearTreeSamples(samples);
        var s = Measure(mgr, samples);
        c.Note($"{when}: {s.Known} sampled far trees of near object tiles, {s.Off} more than 0.1 m off the far ground, worst {s.Worst:0.00} m");
        if (s.Known == 0 && !mustHave)
        {
            return;
        }
        c.Check(s.Ok,
            $"{when}: far trees stand on the far ground, not floating, not sunk ({s.Off} of {s.Known} sampled trees more than 0.1 m off, worst {s.Worst:0.00} m)");
    }

    // Trees (where their tile stood them) against far ground drawn under them now.
    // Off = more than 0.1 m away. Stale = not at the height the far ground give now at all (tree stood on the very
    // tile that is drawn now come out the same to the last digit). Coarse = far ground there is not finest level.
    internal struct Seat
    {
        internal int Known, Off, Stale, Coarse;
        internal float Worst;
        internal bool Ok => Known > 0 && Off * 50 <= Known;
        internal string Text => $"{Off} of {Known} more than 0.1 m off, worst {Worst:0.00} m";
    }

    internal static Seat Measure(LodTerrainManager mgr, List<Vector3> samples)
    {
        var s = new Seat();
        LodTile hint = null;
        foreach (var p in samples)
        {
            if (!mgr.TrySampleSurface(p.x, p.z, ref hint, out var y, out var key))
            {
                continue;
            }
            s.Known++;
            var d = Mathf.Abs(p.y - y);
            if (d > 0.1f)
            {
                s.Off++;
            }
            if (d > 0.005f)
            {
                s.Stale++;
            }
            if (key.Level != 0)
            {
                s.Coarse++;
            }
            if (d > s.Worst)
            {
                s.Worst = d;
            }
        }
        return s;
    }

    // ---------- horizons.far-objects

    private static readonly string[] TreeNames = { "Beech1", "Oak1", "FirTree", "Birch1" };
    private static readonly string[] PieceNames = { "piece_workbench", "piece_chest_wood" };

    private static GameObject PickTree(out string why)
    {
        why = "";
        foreach (var name in TreeNames)
        {
            var prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null || prefab.GetComponent<ZNetView>() == null)
            {
                why += name + ": no such prefab; ";
                continue;
            }
            var info = PrefabCatalog.Get(name.GetStableHashCode());
            if (info.Kind == ObjectKind.Tree && info.AtlasCell != -2 && (info.AtlasCell >= 0 || info.BakeLod.Count > 0))
            {
                return prefab;
            }
            why += $"{name}: kind {info.Kind}, card {info.AtlasCell}; ";
        }
        return null;
    }

    private static bool PieceOk(GameObject prefab)
    {
        var nv = prefab != null ? prefab.GetComponent<ZNetView>() : null;
        if (nv == null || !nv.m_persistent)
        {
            return false;
        }
        var info = PrefabCatalog.Get(prefab.name.GetStableHashCode());
        return info.Kind == ObjectKind.Piece && info.FarLod.Count > 0;
    }

    private static GameObject PickPiece()
    {
        foreach (var name in PieceNames)
        {
            var prefab = ZNetScene.instance.GetPrefab(name);
            if (PieceOk(prefab))
            {
                return prefab;
            }
        }
        // Any building part the far renderer can draw.
        var tried = 0;
        foreach (var prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null || prefab.GetComponent<Piece>() == null)
            {
                continue;
            }
            if (PieceOk(prefab))
            {
                return prefab;
            }
            if (++tried >= 80)
            {
                break;
            }
        }
        return null;
    }

    // An object in the world data only (like a tree of a zone nobody is near): no object in the scene.
    private static ZDOID Make(GameObject prefab, Vector3 pos)
    {
        var nv = prefab.GetComponent<ZNetView>();
        var hash = prefab.name.GetStableHashCode();
        var zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
        zdo.Persistent = nv.m_persistent;
        zdo.Type = nv.m_type;
        zdo.Distant = nv.m_distant;
        zdo.SetPrefab(hash);
        zdo.SetRotation(Quaternion.identity);
        return zdo.m_uid;
    }

    // One tree in the world data about 1.1 km from the camera (near band of the far objects), so a test has a far tree
    // whatever the world around holds. Null = no usable tree prefab. Caller rebuilds the objects and calls Unmake.
    internal static string PlantNearTree(List<ZDOID> made)
    {
        var tree = PickTree(out _);
        var camera = Utils.GetMainCamera();
        if (tree == null || camera == null)
        {
            return null;
        }
        var cfg = Plugin.Cfg;
        var flat = new Vector3(camera.transform.position.x, 0f, camera.transform.position.z);
        var dir = flat.magnitude > 200f ? -flat.normalized : Vector3.right;
        var spot = flat + dir * Mathf.Max(cfg.ObjectMeshDistance.Value + 750f, cfg.ObjectFullDistance.Value * 0.95f);
        spot.y = WorldGenerator.instance.GetHeight(spot.x, spot.z);
        made.Add(Make(tree, spot));
        return tree.name;
    }

    internal static void Unmake(List<ZDOID> made)
    {
        if (ZDOMan.instance == null)
        {
            return;
        }
        foreach (var id in made)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null)
            {
                ZDOMan.instance.DestroyZDO(zdo);
            }
        }
        made.Clear();
    }

    private static void At(DistantObjectManager om, Vector3 p, out bool tile, out int band, out int trees, out int pieces)
    {
        tile = om.TestTileAt(p.x, p.z, out band, out _, out trees, out pieces);
    }

    private static void Cards(Kit.Checks c, DistantObjectManager om)
    {
        var atlas = om.TestAtlas;
        if (!c.Check(atlas != null && atlas.Texture != null && atlas.Baked > 0, "tree cards are baked"))
        {
            return;
        }
        var perRow = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(atlas.Capacity)));
        var cell = ImpostorAtlas.AtlasSize / perRow;
        int empty = 0, white = 0, black = 0;
        var notes = new List<string>();
        for (var i = 0; i < atlas.Baked; i++)
        {
            var px = atlas.Texture.GetPixels(i % perRow * cell, i / perRow * cell, cell, cell, 0);
            var opaque = 0;
            var sum = 0f;
            foreach (var p in px)
            {
                if (p.a < ImpostorAtlas.Cutoff)
                {
                    continue;
                }
                opaque++;
                sum += (p.r + p.g + p.b) / 3f;
            }
            var cover = opaque / (float)px.Length;
            var mean = opaque > 0 ? sum / opaque : 0f;
            if (cover < 0.005f)
            {
                empty++;
            }
            else if (mean > 0.92f)
            {
                white++;
            }
            else if (mean < 0.02f)
            {
                black++;
            }
            notes.Add($"{i}: {cover * 100f:0}% / {mean:0.00}");
        }
        c.Note($"tree cards (cell: covered / mean brightness of the covered part): {string.Join(", ", notes.ToArray())}; {atlas.Failed} bakes failed");
        c.Check(empty == 0 && white == 0 && black == 0, $"no tree card is empty ({empty}), white ({white}) or black ({black}) among {atlas.Baked}");
    }

    private static IEnumerator RunFarObjects()
    {
        const string N = FarObjectsName;
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
            Kit.Force(cfg.DrawPieces, true);
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f, objects: true);
            var om = DistantObjectManager.Instance;
            var mgr = LodTerrainManager.Instance;
            if (!c.Check(settled.Ok && om != null && mgr != null, settled.Detail))
            {
                c.Report("");
                yield break;
            }
            var cam = Utils.GetMainCamera().transform.position;
            var errors = ErrorWatch.Errors;
            c.Note(om.GetStats());
            om.TestTotals(out _, out var built, out _, out var trees, out var pieces, out var rocks);
            c.Note($"as found: {trees} far trees, {pieces} far building parts, {rocks} far rocks in {built} built object tiles");
            var far = cfg.ObjectFarDistance.Value;
            var pieceFar = cfg.PieceDistance.Value;
            om.TestTilesBeyond(cam, far * 1.08f + 65f, out var beyond, out _);
            om.TestTilesBeyond(cam, pieceFar * 1.08f + 1f, out _, out var piecesBeyond);
            c.Check(beyond == 0, $"no object tile farther than ObjectFarDistance ({far:0} m): {beyond}");
            c.Check(piecesBeyond == 0, $"no object tile draws buildings farther than PieceDistance ({pieceFar:0} m): {piecesBeyond}");
            // Trees "as found" (what tests before left) me no judge here: horizons.bug.tree-reseat do that after far terrain
            // rebuild of its own. Here: after my own object rebuild, below.

            // Trees and building parts put in the world data: a tree in the far band, one beyond every band, one in the
            // near (full) band; a building part inside and one beyond PieceDistance.
            var tree = PickTree(out var why);
            var piece = PickPiece();
            if (!c.Check(tree != null, "a tree the far renderer can draw as a card was found (" + why + ")")
                | !c.Check(piece != null, "a building part the far renderer can draw was found"))
            {
                c.Report("");
                yield break;
            }
            var flat = new Vector3(cam.x, 0f, cam.z);
            var dir = flat.magnitude > 200f ? -flat.normalized : Vector3.right;
            var wg = WorldGenerator.instance;
            var nearTree = Mathf.Max(cfg.ObjectMeshDistance.Value + 750f, cfg.ObjectFullDistance.Value * 0.95f);
            var dists = new[] { far * 0.75f, far * 1.08f + 1650f, pieceFar * 0.6f, pieceFar * 1.08f + 1650f, nearTree };
            var spots = new Vector3[dists.Length];
            for (var i = 0; i < dists.Length; i++)
            {
                var spot = flat + dir * dists[i];
                spot.y = wg.GetHeight(spot.x, spot.z);
                spots[i] = spot;
            }
            var radius = cfg.V(cfg.WorldRadius);
            c.Check(spots[1].magnitude < radius - 300f, $"the test spots lie inside the world ({spots[1].magnitude:0} m from the centre)");
            At(om, spots[0], out _, out _, out var treeInBefore, out _);
            At(om, spots[1], out _, out _, out var treeOutBefore, out _);
            At(om, spots[2], out _, out _, out _, out var pieceInBefore);
            At(om, spots[3], out _, out _, out _, out var pieceOutBefore);
            At(om, spots[4], out _, out _, out var treeNearBefore, out _);
            made.Add(Make(tree, spots[0]));
            made.Add(Make(tree, spots[1]));
            made.Add(Make(piece, spots[2]));
            made.Add(Make(piece, spots[3]));
            made.Add(Make(tree, spots[4]));
            var lines = Kit.Console("dh objects rebuild");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: rebuilding all object tiles"), "dh objects rebuild ran");
            yield return Kit.Frames(2);
            yield return Kit.Settle(settled, 50f, objects: true);
            c.Check(settled.Ok, "far objects rebuilt: " + settled.Detail);
            c.Note(om.GetStats());

            At(om, spots[0], out var tile0, out var band0, out var treeIn, out _);
            At(om, spots[1], out var tile1, out _, out var treeOut, out _);
            At(om, spots[2], out var tile2, out var band2, out _, out var pieceIn);
            At(om, spots[3], out _, out _, out _, out var pieceOut);
            At(om, spots[4], out var tile4, out var band4, out var treeNear, out _);
            if (cfg.ObjectFarFactor.Value == 1 && cfg.ObjectThinFactor.Value == 1)
            {
                c.Check(tile0 && treeIn == treeInBefore + 1,
                    $"a {tree.name} {dists[0]:0} m away (inside ObjectFarDistance) is drawn as a far tree (trees in its tile: {treeInBefore} -> {treeIn}, band {band0})");
            }
            else
            {
                c.Note("ObjectFarFactor or ObjectThinFactor is not 1 in this config: a single far tree may be thinned out, not checked");
            }
            c.Check(!tile1 || treeOut == treeOutBefore, $"a {tree.name} {dists[1]:0} m away (beyond ObjectFarDistance) is not drawn (tile there: {tile1}, trees {treeOutBefore} -> {treeOut})");
            c.Check(tile2 && pieceIn == pieceInBefore + 1,
                $"a {piece.name} {dists[2]:0} m away (inside PieceDistance) is drawn as a far building (building parts in its tile: {pieceInBefore} -> {pieceIn}, band {band2})");
            c.Check(pieceOut == pieceOutBefore, $"a {piece.name} {dists[3]:0} m away (beyond PieceDistance) is not drawn ({pieceOutBefore} -> {pieceOut})");
            c.Check(tile4 && treeNear == treeNearBefore + 1 && (band4 == 1 || band4 == 2),
                $"a {tree.name} {dists[4]:0} m away is drawn in a near band (trees in its tile: {treeNearBefore} -> {treeNear}, band {band4})");
            Seated(c, om, mgr, "after the rebuild", true);
            Cards(c, om);
            yield return Kit.Shot(N, "far-objects");

            // Taken out of the world data: the far tiles follow by themselves (no rebuild command).
            Unmake(made);
            var gone = new Kit.Box();
            yield return Kit.WaitUntil(() =>
            {
                At(om, spots[0], out _, out _, out var t, out _);
                At(om, spots[2], out _, out _, out _, out var p);
                return t == treeInBefore && p == pieceInBefore;
            }, 25f, gone);
            c.Check(gone.Ok, $"removed from the world, the far tree and the far building are gone from their tiles by themselves ({gone.Detail})");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"test trees at {dists[4]:0} m and {dists[0]:0} m and a building at {dists[2]:0} m drawn and seated, the ones beyond their distance not; gone again when removed");
        }
        finally
        {
            Kit.Safe("test objects", () => Unmake(made));
            Kit.PutBack();
        }
    }

    // ---------- horizons.bug.tree-reseat

    // Trees of near object tiles must stand on far ground drawn under them, also after that ground changed.
    // The fault (read from code, then seen): object tile draw zones whose CENTRE lie in it, so its trees reach 32 m
    // past its west and south edge, but it only watch far terrain under own square (DistantObjectManager.Visit,
    // TerrainKeyHash). Far terrain tile next door refine or merge later = trees in that strip keep old ground's
    // height. Whether a run show it hang on which of two tiles get done last:
    //   2026-10-07  after dh rebuild + dh objects rebuild: 12 of 80 sampled trees up to 0.38 m off
    //   2026-10-07  same thing once more, and 2026-10-08: 0 off
    //   2026-10-08  lowest simulation distance, after the 40 m camera move of horizons.simdistance: 7 of 146, 0.33 m
    //               (the two runs before: 0 of 144)
    // So three looks here: the rebuild pair (item T30 as written), one made on purpose (ReseatAtEdge: no luck in it),
    // and what horizons.simdistance measured.
    private static IEnumerator RunTreeReseat()
    {
        const string N = TreeReseatName;
        var c = new Kit.Checks(N);
        var made = new List<ZDOID>();
        var hold = new Kit.CameraHold();
        var start = Kit.Now;
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
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 30f, objects: true);
            c.Check(settled.Ok, "before: " + settled.Detail);
            if (!c.Check(cfg.SnapObjectsToTerrain.Value, "SnapObjectsToTerrain is on in this config (off = far trees keep their real height, nothing to check)"))
            {
                c.Report("");
                yield break;
            }

            // Look one: item T30 as written.
            var lines = Kit.Console("dh rebuild");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: rebuilding all LOD tiles"), "dh rebuild ran");
            lines = Kit.Console("dh objects rebuild");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: rebuilding all object tiles"), "dh objects rebuild ran");
            yield return Kit.Frames(2);
            yield return Kit.Settle(settled, 40f, objects: true);
            var mgr = LodTerrainManager.Instance;
            var om = DistantObjectManager.Instance;
            if (!c.Check(settled.Ok && mgr != null && om != null, "far terrain and far objects streamed in again: " + settled.Detail))
            {
                c.Report("");
                yield break;
            }
            // Two more seconds: tile that still follow terrain be done by now.
            yield return new WaitForSecondsRealtime(2f);
            yield return ObjectsIdle(10f);
            Seated(c, om, mgr, "after the far terrain streamed in again", true);

            // Look two: made on purpose. Whole test must stay well under the 120 s limit of the probe.
            if (c.Check(Kit.Now - start < 45f, $"the first look took {Kit.Now - start:0} s: no time left for the look at a tile edge"))
            {
                yield return ReseatAtEdge(c, made, hold, start + 95f);
            }
            DistantObjectManager.TestWatchOn = false;
            Unmake(made);
            hold.Release();

            // Look three: lowest simulation distance, measured by horizons.simdistance right after its 40 m camera move.
            var sim = _lastSim;
            if (sim != null && sim.Done && sim.SeatMeasured && Kit.Now - sim.At <= 600f)
            {
                c.Check(sim.SeatLow.Ok,
                    $"lowest simulation distance (measured {Kit.Now - sim.At:0} s ago by {SimDistanceName}, after its 40 m camera move): far trees stand on the far ground, "
                    + $"not floating, not sunk ({sim.SeatLow.Off} of {sim.SeatLow.Known} sampled trees more than 0.1 m off, worst {sim.SeatLow.Worst:0.00} m; "
                    + $"inside their tile's own square: {sim.SeatLowInside.Text}; in the 32 m strip past its west / south edge: {sim.SeatLowStrip.Text})");
            }
            else
            {
                c.Note($"{SimDistanceName} did not run in this game session: the far trees at the lowest simulation distance are not judged");
            }

            // Camera is back at the player and my trees are gone: test after me start from far objects that are done.
            yield return Kit.Frames(3);
            yield return Kit.Settle(settled, Left(start + 110f, 15f), objects: true);
            c.Report("near far trees follow the far ground when it changes under them, also in the strip past their tile's edge");
        }
        finally
        {
            DistantObjectManager.TestWatchOn = false;
            Kit.Safe("test objects", () => Unmake(made));
            hold.Release();
            Kit.PutBack();
        }
    }

    private static float Left(float deadline, float want) => Mathf.Clamp(deadline - Kit.Now, 2f, want);

    private static Vector3 EdgeCam(WorldGenerator wg, float x, float z) =>
        new Vector3(x, Mathf.Max(wg.GetHeight(x, z), ZoneSystem.instance.m_waterLevel) + 60f, z);

    // One far terrain tile made to refine while the object tile east of it keep its own ground. No luck in it:
    //   T = far terrain tile one level above the finest (512 m), west edge on a multiple of 1024 m.
    //   E = far-object tile east of T (512 m, card band). Its own ground is the far terrain tile east of T: same
    //       parent as T and farther from the camera, so it stay one 512 m tile all along.
    //   Camera on T's row, west of T. First so far that T is one tile whatever was before (past the split line and
    //   its hysteresis), then 40 m inside the split line: T become four finer tiles, nothing under E's square change.
    // Trees watched: the ones in the 32 m strip past E's west edge. E draw them (their zone's centre lie in E), they
    // stand on T. Twenty put there by me (world data only), so the look never hang on what the world grew there.
    // E far enough from the player that the game itself draw no tree there (near set and distant set).
    private static IEnumerator ReseatAtEdge(Kit.Checks c, List<ZDOID> made, Kit.CameraHold hold, float deadline)
    {
        const float Coarse = 512f;
        var cfg = Plugin.Cfg;
        var mgr = LodTerrainManager.Instance;
        var om = DistantObjectManager.Instance;
        var wg = WorldGenerator.instance;
        var tree = PickTree(out var why);
        var reach = cfg.SplitFactor.Value * Coarse;
        var leafAt = reach * (1f + cfg.SplitHysteresis.Value) + 8f;
        var at = ZNet.instance.GetReferencePosition();
        var clear = ZNet.instance.GetSyncedSimulationDistance().TotalSimulationDistance * 64f + 120f;
        var tMinX = Mathf.Ceil((at.x + clear - Coarse) / (2f * Coarse)) * 2f * Coarse;
        var eMinX = tMinX + Coarse;
        var zMin = Mathf.Floor(at.z / Coarse) * Coarse;
        var midZ = zMin + Coarse * 0.5f;
        var tCx = tMinX + Coarse * 0.5f;
        var eCx = eMinX + Coarse * 0.5f;
        var camZ = Mathf.Clamp(at.z, zMin + 1f, zMin + Coarse - 1f);
        var layoutOk = Mathf.Approximately(cfg.BaseTileSize.Value, 256f) && reach >= 100f && leafAt < 2f * reach - 8f
                       && Coarse + leafAt < cfg.ObjectFullDistance.Value - 20f
                       && new Vector2(eMinX + Coarse, midZ).magnitude < cfg.V(cfg.WorldRadius) - 300f;
        if (!c.Check(tree != null, "tile edge: a tree the far renderer can draw as a card was found (" + why + ")")
            | !c.Check(layoutOk,
                $"tile edge: this look needs BaseTileSize 256 (it is {cfg.BaseTileSize.Value:0}), a card band that reaches {Coarse + leafAt + 20f:0} m "
                + $"(ObjectFullDistance {cfg.ObjectFullDistance.Value:0}) and room in the world {eMinX + Coarse:0} m east of the centre"))
        {
            yield break;
        }

        // Camera far west of T: T is one tile.
        var p1 = EdgeCam(wg, tMinX - leafAt, camZ);
        var p2 = EdgeCam(wg, tMinX - reach + 40f, camZ);
        hold.At(p1, p1 + Vector3.right * 100f);
        yield return Kit.Frames(2);
        var settled = new Kit.Box();
        yield return Kit.Settle(settled, Left(deadline, 30f), objects: true);
        if (!c.Check(settled.Ok, $"tile edge: camera held {leafAt:0} m west of the far terrain tile at x {tMinX:0}: " + settled.Detail))
        {
            yield break;
        }

        // Strip watched, trees put in it, far objects built anew: they stand on T while it is one tile.
        DistantObjectManager.TestWatchMinX = eMinX - 32f;
        DistantObjectManager.TestWatchMaxX = eMinX;
        DistantObjectManager.TestWatchMinZ = zMin + 4f;
        DistantObjectManager.TestWatchMaxZ = zMin + Coarse - 36f;
        DistantObjectManager.TestWatchOn = true;
        for (var i = 0; i < 20; i++)
        {
            var spot = new Vector3(eMinX - 13f, 0f, zMin + 12f + i * 23f);
            spot.y = wg.GetHeight(spot.x, spot.z);
            made.Add(Make(tree, spot));
        }
        var lines = Kit.Console("dh objects rebuild");
        yield return Kit.Frames(2);
        yield return Kit.Settle(settled, Left(deadline, 30f), objects: true);
        if (!c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: rebuilding all object tiles") && settled.Ok,
                "tile edge: far objects built anew with 20 test trees in the strip: " + settled.Detail))
        {
            yield break;
        }
        var tOne = mgr.TrySampleSurface(tCx, midZ, out _, out var tKey) && tKey.Level == 1;
        var eOne = mgr.TrySampleSurface(eCx, midZ, out _, out var eKey) && eKey.Level == 1;
        var eTile = om.TestTileAt(eCx, midZ, out var eBand, out var eBuilt, out _, out _);
        var samples = new List<Vector3>();
        om.TestWatchSamples(samples);
        var before = Measure(mgr, samples);
        c.Note($"tile edge: far terrain tile x {tMinX:0}..{eMinX:0}, z {zMin:0}..{zMin + Coarse:0} is drawn as {tKey}; the far-object tile east of it (band {eBand}, built {eBuilt}) "
               + $"stands on {eKey}; {before.Known} trees watched in its 32 m west strip, {before.Stale} not at the far ground's height, worst {before.Worst:0.00} m");
        if (!c.Check(tOne && eOne && eTile && eBuilt && (eBand == 1 || eBand == 2),
                $"tile edge, set-up: the far ground under the strip is one 512 m tile ({tKey}), the ground east of it too ({eKey}), and a near far-object tile draws the trees there (band {eBand}, built {eBuilt})")
            | !c.Check(before.Known > 0 && before.Stale == 0 && before.Coarse == before.Known,
                $"tile edge, before: the {before.Known} watched trees in the strip stand on the far ground ({before.Stale} not at its height, worst {before.Worst:0.00} m)"))
        {
            yield break;
        }

        // Camera 40 m inside T's split line: T become four finer tiles.
        hold.At(p2, p2 + Vector3.right * 100f);
        var refined = new Kit.Box();
        yield return Kit.WaitUntil(() => mgr.TrySampleSurface(tCx, midZ, out _, out var k) && k.Level == 0, Left(deadline, 25f), refined);
        yield return Kit.Settle(settled, Left(deadline, 25f), objects: true);
        // Two more seconds: tile that still follow terrain be done by now.
        yield return new WaitForSecondsRealtime(2f);
        yield return ObjectsIdle(Left(deadline, 8f));
        var eSame =mgr.TrySampleSurface(eCx, midZ, out _, out var eKeyNow) && eKeyNow == eKey;
        eTile = om.TestTileAt(eCx, midZ, out eBand, out eBuilt, out _, out _);
        samples.Clear();
        om.TestWatchSamples(samples);
        var after = Measure(mgr, samples);
        c.Note($"tile edge: camera moved {p2.x - p1.x:0} m east; the far ground under the strip became finer tiles after {refined.Detail} (done: {refined.Ok}), the ground east of it is {eKeyNow}; "
               + $"{after.Known} watched trees: {after.Stale} not at the far ground's height, {after.Off} more than 0.1 m off, worst {after.Worst:0.00} m");
        if (!c.Check(refined.Ok && settled.Ok && after.Known > 0 && after.Coarse == 0 && eSame && eTile && eBuilt && (eBand == 1 || eBand == 2),
                $"tile edge: after a camera move of {p2.x - p1.x:0} m the far ground under the strip is made of finer tiles ({after.Coarse} of {after.Known} watched trees still over a coarse one; {settled.Detail}) "
                + $"while the ground under the object tile's own square is the same tile as before ({eKey} -> {eKeyNow}, band {eBand})"))
        {
            yield break;
        }
        c.Check(after.Stale == 0,
            $"tile edge: after the far ground under them was replaced by finer tiles, the {after.Known} trees in the 32 m strip past their object tile's west edge stand on the new ground "
            + $"({after.Stale} still stand at the height of the ground that is gone; {after.Off} of them more than 0.1 m off the far ground, worst {after.Worst:0.00} m)");
    }

    // ---------- horizons.simdistance and horizons.bug.simdistance-still

    // What the two simulation-distance tests share: one change down and back, measured.
    private sealed class SimRun
    {
        internal float At = -1f;
        internal bool Done;
        internal bool Changed;
        internal string Before = "";
        internal string After = "";
        // Real zones (ZoneSystem.m_zones) before the change and when the lowered checks ran.
        internal int ZonesBefore = -1;
        internal int ZonesLow = -1;
        // Lowered: the real zones beyond the new distance really left (count dropped, then stopped moving).
        internal bool Unloaded;
        internal string UnloadDetail = "";
        // Lowered, after the camera move: far-tile vertices above real ground within 200 m, and the rim of the real ground.
        internal int PokingLow = -1;
        internal WorldTests.RimResult RimLow;
        internal float Offset;
        // Lowered, after the camera move: near trees against the far ground. All sampled ones, then the same ones in two
        // heaps: inside own square of the tile that draw them, and in the 32 m strip past its west / south edge.
        internal bool SeatMeasured;
        internal Seat SeatLow, SeatLowInside, SeatLowStrip;
        // Camera not moved since the change.
        internal int StillHolesLow = -1;
        internal string StillHoleLow = "";
        internal int StillDoubledBack = -1;
        internal string StillDoubleBack = "";
    }

    private static SimRun _lastSim;

    private static string Describe(SimulationDistance s) => $"near {s.NearSimulationDistance} + far {s.FarSimulationDistance}{(s.IsClassic ? " classic" : "")}";

    // Camera 40 m aside and back: both managers look again, like after a few steps of the player.
    private static IEnumerator Nudge(Kit.CameraHold hold)
    {
        var cam = Utils.GetMainCamera();
        var pos = cam.transform.position;
        var look = pos + cam.transform.forward * 100f;
        hold.At(pos + cam.transform.right * 40f, look + cam.transform.right * 40f);
        yield return new WaitForSecondsRealtime(0.6f);
        hold.Release();
        yield return new WaitForSecondsRealtime(0.6f);
    }

    internal static IEnumerator ObjectsIdle(float timeout)
    {
        var start = Kit.Now;
        var idleSince = -1f;
        while (Kit.Now - start < timeout)
        {
            var om = DistantObjectManager.Instance;
            if (om == null || om.TestBusy)
            {
                idleSince = -1f;
            }
            else if (idleSince < 0f)
            {
                idleSince = Kit.Now;
            }
            if (idleSince >= 0f && Kit.Now - idleSince >= 1.5f)
            {
                yield break;
            }
            yield return null;
        }
    }

    // Zones settle after a change: their count stops moving and the area the game wants is loaded.
    // atLeast: never done before this many seconds. Zone the game no longer want only leave after
    // ZoneSystem.m_zoneTTL (4 s), then one zone per zone-system tick: 2.5 s of same count right after lowering the
    // distance say nothing (run of 2026-10-07: "unloaded" was said with all 137 zones still there).
    private static IEnumerator ZonesSettle(float timeout, Kit.Box result, float atLeast = 0f)
    {
        var start = Kit.Now;
        var last = -1;
        var since = start;
        result.Ok = false;
        while (Kit.Now - start < timeout)
        {
            var count = ZoneSystem.instance.m_zones.Count;
            if (count != last)
            {
                last = count;
                since = Kit.Now;
            }
            else if (Kit.Now - since >= 2.5f && Kit.Now - start >= atLeast && ZoneSystem.instance.IsActiveAreaLoaded())
            {
                result.Ok = true;
                break;
            }
            yield return null;
        }
        result.Detail = $"{ZoneSystem.instance.m_zones.Count} zones after {Kit.Now - start:0.0} s";
    }

    private sealed class ZoneCount
    {
        internal int WithItems, Far, Real, Holes, Doubled;
        internal string FirstHole = "", FirstDouble = "";

        internal void Read(DistantObjectManager om, int reach)
        {
            om.TestZoneReport(reach, out WithItems, out Far, out Real, out Holes, out Doubled, out FirstHole, out FirstDouble);
        }
    }

    // Me wait (bounded) until every zone have its objects exactly once and far objects have nothing left to do. Real
    // objects of a zone come back over several seconds (game make a few per frame) and far tile let go of the zone one
    // look after they are there: one report right after "settled" can catch a zone in between.
    // Zone that stay without or doubled still fail: last report is what caller check.
    private static IEnumerator ZonesOnce(DistantObjectManager om, int reach, float timeout, ZoneCount z)
    {
        var start = Kit.Now;
        var nextRead = 0f;
        z.Read(om, reach);
        while (Kit.Now - start < timeout && (z.Holes > 0 || z.Doubled > 0 || om.TestBusy))
        {
            yield return null;
            if (Kit.Now >= nextRead)
            {
                nextRead = Kit.Now + 0.25f;
                z.Read(om, reach);
            }
        }
        z.Read(om, reach);
    }

    private static IEnumerator SimCycle(Kit.Checks c, SimRun run, string shotName)
    {
        var hold = new Kit.CameraHold();
        var znet = ZNet.instance;
        var saved = znet.m_simulationDistance;
        var restored = false;
        try
        {
            var cfg = Plugin.Cfg;
            Kit.Force(cfg.FarWater, true);
            Kit.Force(cfg.ObjectsEnabled, true);
            Kit.Force(cfg.DrawTrees, true);
            Kit.Force(cfg.RealTerrainFadeFix, true);
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f, objects: true);
            var mgr = LodTerrainManager.Instance;
            var om = DistantObjectManager.Instance;
            if (!c.Check(settled.Ok && mgr != null && om != null, settled.Detail))
            {
                yield break;
            }
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws > 0, 10f, box);
            c.Check(box.Ok, "before: the far sea is painted");
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            var offset = cfg.NearTerrainOffset.Value;
            var before = znet.GetSyncedSimulationDistance();
            var reach = before.NearSimulationDistance + 2;
            run.Before = Describe(before);

            var z = new ZoneCount();
            yield return ZonesOnce(om, reach, 15f, z);
            run.ZonesBefore = ZoneSystem.instance.m_zones.Count;
            c.Note($"before ({run.Before}, {run.ZonesBefore} real zones): {z.WithItems} zones with objects within {reach} zones: {z.Real} with real objects, {z.Far} with far objects");
            c.Check(z.WithItems > 0 && z.Holes == 0, $"before: no zone without its objects ({z.Holes}, first {z.FirstHole})");
            c.Check(z.Doubled == 0, $"before: no zone with real and far objects at once ({z.Doubled}, first {z.FirstDouble})");
            c.Check(z.Real > 0 && z.Far > 0, $"before: the report sees zones with real objects ({z.Real}) and zones with far objects ({z.Far})");

            // Down to the lowest, the way the graphics menu applies it.
            znet.ApplySimulationDistance(new SimulationDistance(1, 2, classic: true));
            var after = znet.GetSyncedSimulationDistance();
            run.After = Describe(after);
            run.Changed = !after.Equals(before);
            // Same call, same frame.
            mgr.TestTileRenderers(out var shown, out _, out var wrongHide, out var wrongWater);
            c.Check(shown > 0 && wrongHide == 0 && wrongWater == 0,
                $"lowered ({run.Before} -> {run.After}): in that same call every shown far tile keeps its own hide distance and depth-only state ({wrongHide} and {wrongWater} of {shown} lost them)");
            Kit.LodPlanes(out var planes, out var hidden, out _, out _);
            c.Check(hidden == planes, $"lowered: in that same call the game's distant water plane stays out of reach under the far sea ({hidden} of {planes})");
            c.Check(run.Changed, $"the simulation distance could be lowered for this test (it is {run.Before}; when the graphics setting is already the lowest nothing changes)");
            yield return Kit.Frames(3);
            if (cfg.FarWaterInnerRadius.Value <= 0f)
            {
                var wantInner = Mathf.Max(64f, after.NearSimulationDistance * 64f - 64f);
                c.Check(Kit.Near(mgr.Water.Inner, wantInner), $"lowered: the far sea now starts {mgr.Water.Inner:0} m out (expected {wantInner:0} m)");
            }
            Kit.LodPlanes(out planes, out hidden, out _, out _);
            c.Check(hidden == planes && mgr.SeaPainting, $"lowered, three frames later: far sea still painted, the game's plane still out of reach ({hidden} of {planes})");

            // Real ground beyond new distance must really be gone before me look at rim: game drop a zone 4 s (m_zoneTTL)
            // after it stop wanting it, one zone per tick.
            yield return ZonesSettle(30f, box, ZoneSystem.instance.m_zoneTTL + 2f);
            run.ZonesLow = ZoneSystem.instance.m_zones.Count;
            run.Unloaded = box.Ok && run.ZonesLow < run.ZonesBefore;
            run.UnloadDetail = box.Detail;
            // Whether they unload hang on the game and on other mods (run of 2026-10-07, Valheim Community Patch installed:
            // still all 137 zones 7 s later), so that get judged alone: horizons.simdistance-rim.
            c.Note($"lowered: real zones {run.ZonesBefore} -> {run.ZonesLow} ({box.Detail})"
                   + (run.Unloaded ? "" : ": the real zones beyond the new distance did NOT unload, so the ground at the new rim cannot be looked at"));
            yield return ObjectsIdle(8f);
            // Camera not moved yet.
            z.Read(om, reach);
            run.StillHolesLow = z.Holes;
            run.StillHoleLow = z.FirstHole;

            yield return Nudge(hold);
            yield return Kit.Settle(settled, 20f, objects: true);
            c.Check(settled.Ok, "lowered: " + settled.Detail);
            yield return ZonesOnce(om, reach, 15f, z);
            c.Note($"lowered ({run.After}, {ZoneSystem.instance.m_zones.Count} real zones), after a 40 m camera move: {z.WithItems} zones with objects: {z.Real} real, {z.Far} far; before the move {run.StillHolesLow} zones had neither");
            c.Check(z.Holes == 0, $"lowered, after a 40 m move: every zone that lost its real objects has far objects ({z.Holes} have neither, first {z.FirstHole})");
            c.Check(z.Doubled == 0, $"lowered, after a 40 m move: no zone with real and far objects at once ({z.Doubled}, first {z.FirstDouble})");
            var probe = mgr.ProbeNearGround(cam.transform.position, player.transform.position, 200f, out var poking, out var mismatched);
            c.Note("lowered: " + (probe.Length > 300 ? probe.Substring(0, 300) + "..." : probe));
            c.Check(mismatched == 0, $"lowered: {mismatched} painted real zones whose depth renderer still tessellates");
            // Far ground against real ground at new rim: me keep it for horizons.simdistance-rim (only mean something once
            // old real zones are gone).
            run.PokingLow = poking;
            run.RimLow = WorldTests.Rim(mgr, offset);
            run.Offset = offset;
            mgr.TestTileRenderers(out shown, out _, out wrongHide, out _);
            c.Check(wrongHide == 0, $"lowered: every shown far tile still has its own hide distance ({wrongHide} of {shown} wrong)");
            // Near trees on far ground: me only measure here, horizons.bug.tree-reseat judge. Tree in the 32 m strip past
            // its tile's west / south edge stay on old ground when far terrain tile next door change after tile was
            // built, and camera move above can make one change (camera at spawn sit right on a split line). Real fault of
            // the mod, not every run: 7 of 146 off, worst 0.33 m (2026-10-08); 0 off the two runs before.
            var all = new List<Vector3>();
            var inside = new List<Vector3>();
            var strip = new List<Vector3>();
            om.TestNearTreeSamples(all);
            om.TestNearTreeSplit(inside, strip);
            run.SeatLow = Measure(mgr, all);
            run.SeatLowInside = Measure(mgr, inside);
            run.SeatLowStrip = Measure(mgr, strip);
            run.SeatMeasured = true;
            c.Note($"lowered: {run.SeatLow.Known} sampled far trees of near object tiles, {run.SeatLow.Off} more than 0.1 m off the far ground, worst {run.SeatLow.Worst:0.00} m "
                   + $"(inside their tile's own square: {run.SeatLowInside.Text}; in the 32 m strip past its west / south edge: {run.SeatLowStrip.Text}); judged by {TreeReseatName}");
            if (shotName != null)
            {
                yield return Kit.Shot(shotName, "lowest-simulation-distance");
            }

            // And back.
            znet.ApplySimulationDistance(saved);
            restored = true;
            mgr.TestTileRenderers(out shown, out _, out wrongHide, out wrongWater);
            Kit.LodPlanes(out planes, out hidden, out _, out _);
            c.Check(wrongHide == 0 && wrongWater == 0 && hidden == planes,
                $"put back: in that same call far tiles keep their values ({wrongHide}, {wrongWater} of {shown} wrong) and the water plane stays out of reach ({hidden} of {planes})");
            yield return ZonesSettle(40f, box);
            c.Check(box.Ok, "put back: the zones loaded again: " + box.Detail);
            yield return ObjectsIdle(8f);
            z.Read(om, reach);
            run.StillDoubledBack = z.Doubled;
            run.StillDoubleBack = z.FirstDouble;

            yield return Nudge(hold);
            yield return Kit.Settle(settled, 20f, objects: true);
            c.Check(settled.Ok, "put back: " + settled.Detail);
            // Real objects of zones that came back get made over several seconds: me wait for the hand-over.
            yield return ZonesOnce(om, reach, 30f, z);
            c.Note($"put back, after a 40 m camera move: {z.WithItems} zones with objects: {z.Real} real, {z.Far} far; before the move {run.StillDoubledBack} zones had both");
            c.Check(z.Holes == 0 && z.Doubled == 0, $"put back, after a 40 m move: every zone has its objects once ({z.Holes} without, {z.Doubled} twice, first {z.FirstHole}{z.FirstDouble})");
            WorldTests.RimChecks(c, WorldTests.Rim(mgr, offset), "put back: ", offset);
            if (cfg.FarWaterInnerRadius.Value <= 0f)
            {
                c.Check(Kit.Near(mgr.Water.Inner, FarWater.AutoInnerRadius()), $"put back: the far sea starts {mgr.Water.Inner:0} m out again");
            }
            run.Done = true;
            run.At = Kit.Now;
        }
        finally
        {
            if (!restored && ZNet.instance != null)
            {
                Kit.Safe("simulation distance", () => ZNet.instance.ApplySimulationDistance(saved));
            }
            hold.Release();
        }
    }

    private static IEnumerator RunSimDistance()
    {
        const string N = SimDistanceName;
        var c = new Kit.Checks(N);
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the checks need it"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var run = new SimRun();
            yield return SimCycle(c, run, N);
            if (run.Done)
            {
                _lastSim = run;
            }
            c.Check(run.Done, "the whole down-and-back cycle ran");
            c.Report($"simulation distance {run.Before} -> {run.After} -> back: far ground, far sea and far objects follow");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // Down-and-back cycle horizons.simdistance measured in this game session, or (run alone, or long ago) one made
    // here. Its own checks then go to scratch list: caller only judge what it is about.
    private static IEnumerator Measured(string name, Kit.Checks c, SimRun[] result)
    {
        var run = _lastSim;
        if (run == null || !run.Done || Kit.Now - run.At > 600f)
        {
            var ready = new Kit.Box();
            var scratch = new Kit.Checks(name);
            yield return Ready(scratch, ready);
            run = new SimRun();
            if (ready.Ok && Kit.Shrink)
            {
                yield return SimCycle(scratch, run, null);
            }
            if (run.Done)
            {
                _lastSim = run;
            }
        }
        else
        {
            c.Note($"measured {Kit.Now - run.At:0} s ago by {SimDistanceName}");
        }
        result[0] = run;
    }

    // ---------- horizons.simdistance-rim

    // Lowest simulation distance: where real ground now end, far ground meet it. Only mean something once real zones
    // beyond new distance are gone, and that hang on the game and on other mods: own small test.
    private static IEnumerator RunSimRim()
    {
        const string N = SimRimName;
        var c = new Kit.Checks(N);
        try
        {
            var got = new SimRun[1];
            yield return Measured(N, c, got);
            var run = got[0];
            if (!c.Check(run.Done && run.Changed, $"the simulation distance was lowered and put back ({run.Before} -> {run.After})"))
            {
                c.Report("");
                yield break;
            }
            if (!c.Check(run.Unloaded,
                    $"after lowering the simulation distance the real zones beyond it unloaded ({run.ZonesBefore} -> {run.ZonesLow} real zones, {run.UnloadDetail}); "
                    + "when they stay, the ground at the new rim cannot be looked at"))
            {
                c.Note($"with the old real zones still loaded, {run.PokingLow} far-tile vertices lay above real ground within 200 m (far ground is only pushed under real zones up to one zone past the near distance)");
                c.Report("");
                yield break;
            }
            // Only real zones the game still want are left: far vertex above real ground now poke through it.
            c.Check(run.PokingLow == 0, $"lowered: {run.PokingLow} far-tile vertices above the real ground within 200 m");
            WorldTests.RimChecks(c, run.RimLow, "lowered (as measured then): ", run.Offset);
            c.Report($"lowest simulation distance: real zones {run.ZonesBefore} -> {run.ZonesLow}, the far ground meets the real ground at the new rim");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    private static IEnumerator RunSimStill()
    {
        const string N = SimStillName;
        var c = new Kit.Checks(N);
        try
        {
            var got = new SimRun[1];
            yield return Measured(N, c, got);
            var run = got[0];
            if (!c.Check(run.Done && run.Changed, $"the simulation distance was lowered and put back ({run.Before} -> {run.After})"))
            {
                c.Report("");
                yield break;
            }
            c.Note($"{run.Before} -> {run.After} -> back; real zones loaded {run.ZonesBefore} before, {run.ZonesLow} when the lowered state was looked at "
                   + "(the camera was not moved between each change and its look, 4 s or more later)");
            c.Check(run.StillHolesLow == 0,
                $"after lowering the simulation distance, without moving: every zone that lost its real objects shows far objects ({run.StillHolesLow} zones show neither, first {run.StillHoleLow})");
            c.Check(run.StillDoubledBack == 0,
                $"after raising it again, without moving: no zone shows real and far objects at once ({run.StillDoubledBack} zones, first {run.StillDoubleBack})");
            c.Report("far objects follow a simulation-distance change without the player moving");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.spyglass

    private static double[] Slot(float zoom, Vector3 forward, float halfAngle) =>
        new double[] { 1, Time.frameCount, zoom, 1.0, forward.x, forward.y, forward.z, halfAngle, halfAngle * 0.6f };

    // Finest far tile drawn under points along the view (every 250 m between the two distances, on the view line and
    // 4 degrees to each side). 99 = no far ground there.
    private static int FinestInCone(LodTerrainManager mgr, Vector3 cam, Vector2 forward, float halfAngle, float min, float max)
    {
        var finest = 99;
        LodTile hint = null;
        for (var d = min; d <= max; d += 250f)
        {
            for (var a = -halfAngle * 0.5f; a <= halfAngle * 0.5f; a += halfAngle * 0.5f)
            {
                var rad = a * Mathf.Deg2Rad;
                var dir = new Vector2(forward.x * Mathf.Cos(rad) - forward.y * Mathf.Sin(rad), forward.x * Mathf.Sin(rad) + forward.y * Mathf.Cos(rad));
                if (mgr.TrySampleSurface(cam.x + dir.x * d, cam.z + dir.y * d, ref hint, out _, out var key) && key.Level < finest)
                {
                    finest = key.Level;
                }
            }
        }
        return finest;
    }

    // Finest far tile the no-spyglass rule can want at distance or farther from the camera: its parent (p wide) is
    // split only while the camera is within SplitFactor * (1 + SplitHysteresis) * p of it (Chebyshev), and the parent
    // reach at most p nearer than a point in it (point at distance d = at least d / sqrt 2 away, Chebyshev).
    private static int FinestNormal(DHConfig cfg, float distance)
    {
        var reach = cfg.SplitFactor.Value * (1f + cfg.SplitHysteresis.Value);
        var level = 0;
        while (level < 24 && cfg.BaseTileSize.Value * (1 << (level + 1)) <= distance / (1.41422f * (1f + reach)))
        {
            level++;
        }
        return level;
    }

    private sealed class Seen
    {
        internal int Leaves;
        // Most wanted nodes split past the no-spyglass reach (LodTerrainManager.TestBoostLeft).
        internal int PastReach;
        internal int ObjectTilesBeyond;
        internal int BuildingTilesBeyond;
        internal float WorstFrame;
    }

    // A spyglass held at the eye for some seconds; Seen keeps the most of each count.
    private static IEnumerator Raise(float seconds, float zoom, Vector3 cam, Vector2 forward, float objectsBeyond, float piecesBeyond, Seen seen)
    {
        var mgr = LodTerrainManager.Instance;
        var om = DistantObjectManager.Instance;
        var fwd3 = new Vector3(forward.x, 0f, forward.y);
        var until = Kit.Now + seconds;
        var frames = 0;
        while (Kit.Now < until)
        {
            ViewBoost.TestSlot = Slot(zoom, fwd3, 8f);
            if (mgr != null)
            {
                seen.Leaves = Mathf.Max(seen.Leaves, mgr.CountLeavesInView(cam, forward, 8f, 600f));
                mgr.TestBoostLeft(out var past, out _, out _);
                seen.PastReach = Mathf.Max(seen.PastReach, past);
            }
            if (om != null)
            {
                om.TestTilesBeyond(cam, objectsBeyond, out var tiles, out _);
                om.TestTilesBeyond(cam, piecesBeyond, out _, out var pieces);
                seen.ObjectTilesBeyond = Mathf.Max(seen.ObjectTilesBeyond, tiles);
                seen.BuildingTilesBeyond = Mathf.Max(seen.BuildingTilesBeyond, pieces);
            }
            if (++frames > 2)
            {
                seen.WorstFrame = Mathf.Max(seen.WorstFrame, Time.unscaledDeltaTime);
            }
            yield return null;
        }
    }

    private static IEnumerator RunSpyglass()
    {
        const string N = SpyglassName;
        var c = new Kit.Checks(N);
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
            Kit.Force(cfg.SpyglassDetail, true);
            Kit.Force(cfg.SpyglassMaxBoost, 4f);
            Kit.Force(cfg.ObjectsEnabled, true);
            Kit.Force(cfg.DrawPieces, true);
            ViewBoost.TestSlot = null;
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f, objects: true);
            var mgr = LodTerrainManager.Instance;
            var om = DistantObjectManager.Instance;
            if (!c.Check(settled.Ok && mgr != null && om != null, settled.Detail))
            {
                c.Report("");
                yield break;
            }
            var errors = ErrorWatch.Errors;
            var camera = Utils.GetMainCamera();
            var cam = camera.transform.position;
            var f3 = camera.transform.forward;
            var forward = new Vector2(f3.x, f3.z);
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = new Vector2(0f, 1f);
            }
            forward.Normalize();
            var side = new Vector2(forward.y, -forward.x);
            var fwd3 = new Vector3(forward.x, 0f, forward.y);
            var objectsBeyond = cfg.ObjectFarDistance.Value * 1.08f + 65f;
            var piecesBeyond = cfg.PieceDistance.Value * 1.08f + 1f;

            // Reading the message: cap, no cap, setting off, no message.
            ViewBoost.TestSlot = Slot(8f, fwd3, 10f);
            c.Check(!ViewBoost.Read(true, 1f).Active, "SpyglassMaxBoost = 1: a x8 spyglass gives no boost");
            ViewBoost.TestSlot = Slot(8f, fwd3, 10f);
            var read = ViewBoost.Read(true, 8f);
            c.Check(read.Active && Mathf.Approximately(read.Boost, 8f), $"SpyglassMaxBoost = 8: a x8 spyglass gives the whole x8 ({read.Boost:0.##})");
            ViewBoost.TestSlot = Slot(8f, fwd3, 10f);
            read = ViewBoost.Read(true, 4f);
            c.Check(read.Active && Mathf.Approximately(read.Boost, 4f), $"SpyglassMaxBoost = 4: a x8 spyglass is capped to x4 ({read.Boost:0.##})");
            ViewBoost.TestSlot = Slot(8f, fwd3, 10f);
            c.Check(!ViewBoost.Read(false, 8f).Active, "SpyglassDetail = false: no boost");
            ViewBoost.TestSlot = null;
            c.Check(!ViewBoost.Read(true, 8f).Active, "no spyglass at the eye (no fresh message): no boost");
            yield return Kit.Frames(3);

            var leaves = mgr.CountLeavesInView(cam, forward, 8f, 600f);
            var sideLeaves = mgr.CountLeavesInView(cam, side, 8f, 600f);
            var finest = FinestInCone(mgr, cam, forward, 8f, 2000f, 5000f);
            om.TestTilesBeyond(cam, objectsBeyond, out var objects0, out _);
            om.TestTilesBeyond(cam, piecesBeyond, out _, out var buildings0);
            c.Note($"spyglass down: {leaves} far leaf tiles within 8 degrees of the view, {sideLeaves} to the side, finest built tile 2 to 5 km out: level {finest}; "
                   + $"object tiles beyond {objectsBeyond:0} m: {objects0}, tiles with buildings beyond {piecesBeyond:0} m: {buildings0}");
            c.Check(objects0 == 0 && buildings0 == 0, "spyglass down: no object tile beyond ObjectFarDistance and no buildings beyond PieceDistance");
            // Measure of "detail only a spyglass give": 0 now, more than 0 while raised, 0 again when lowered.
            mgr.TestBoostLeft(out var past0, out _, out var pastFirst0);
            c.Check(past0 == 0, $"spyglass down: no wanted far tile is split farther out than SplitFactor x (1 + SplitHysteresis) tile widths ({past0} nodes, first {pastFirst0})");

            // Settings that switch the boost off.
            var seen = new Seen();
            cfg.SetForTest(cfg.SpyglassDetail, false);
            yield return Raise(2f, 8f, cam, forward, objectsBeyond, piecesBeyond, seen);
            c.Check(seen.Leaves <= leaves && seen.PastReach == 0 && seen.ObjectTilesBeyond == 0 && seen.BuildingTilesBeyond == 0,
                $"SpyglassDetail = false: a raised x8 spyglass changes no detail ({leaves} -> {seen.Leaves} tiles in view, {seen.PastReach} wanted nodes split past the normal reach, {seen.ObjectTilesBeyond} object tiles and {seen.BuildingTilesBeyond} with buildings farther out)");
            cfg.SetForTest(cfg.SpyglassDetail, true);
            cfg.SetForTest(cfg.SpyglassMaxBoost, 1f);
            seen = new Seen();
            yield return Raise(2f, 8f, cam, forward, objectsBeyond, piecesBeyond, seen);
            c.Check(seen.Leaves <= leaves && seen.PastReach == 0 && seen.ObjectTilesBeyond == 0 && seen.BuildingTilesBeyond == 0,
                $"SpyglassMaxBoost = 1: a raised x8 spyglass changes no detail ({leaves} -> {seen.Leaves} tiles in view, {seen.PastReach} wanted nodes split past the normal reach, {seen.ObjectTilesBeyond} object tiles and {seen.BuildingTilesBeyond} with buildings farther out)");

            // Boost 4: finer land where the spyglass looks, objects farther out.
            cfg.SetForTest(cfg.SpyglassMaxBoost, 4f);
            var four = new Seen();
            yield return Raise(8f, 8f, cam, forward, objectsBeyond, piecesBeyond, four);
            var finestBoosted = FinestInCone(mgr, cam, forward, 8f, 2000f, 5000f);
            c.Note($"x8 spyglass, boost 4, 8 s: {four.Leaves} leaf tiles in view, finest built tile 2 to 5 km out: level {finestBoosted}, "
                   + $"{four.ObjectTilesBeyond} object tiles beyond the normal distance, {four.BuildingTilesBeyond} with buildings beyond PieceDistance, longest frame {four.WorstFrame * 1000f:0} ms");
            c.Check(four.Leaves >= leaves + 2, $"boost 4: more far tiles where the spyglass looks ({leaves} -> {four.Leaves})");
            c.Check(four.PastReach > 0, $"boost 4: wanted far tiles are split farther out than without a spyglass ({four.PastReach} nodes past the normal reach)");
            c.Check(finest < 99 && finestBoosted < finest, $"boost 4: the land 2 to 5 km out is built finer within 8 s (level {finest} -> {finestBoosted})");
            c.Check(four.ObjectTilesBeyond > 0, $"boost 4: far objects are drawn farther out that way ({four.ObjectTilesBeyond} object tiles beyond {objectsBeyond:0} m)");
            c.Check(four.BuildingTilesBeyond > 0, $"boost 4: buildings are drawn farther out that way ({four.BuildingTilesBeyond} tiles beyond {piecesBeyond:0} m)");
            c.Check(four.WorstFrame < 3f, $"boost 4: no long freeze (longest frame {four.WorstFrame:0.00} s)");

            // Turned to a new direction.
            var turned = new Seen();
            yield return Raise(3f, 8f, cam, side, objectsBeyond, piecesBeyond, turned);
            c.Check(turned.Leaves >= sideLeaves + 2, $"turned 90 degrees: that direction gets more far tiles too ({sideLeaves} -> {turned.Leaves})");

            // Boost 8 against 4, same view as the first.
            cfg.SetForTest(cfg.SpyglassMaxBoost, 8f);
            var eight = new Seen();
            yield return Raise(3f, 8f, cam, forward, objectsBeyond, piecesBeyond, eight);
            c.Check(eight.Leaves > four.Leaves, $"SpyglassMaxBoost = 8: more detail through a x8 spyglass than with 4 ({four.Leaves} -> {eight.Leaves} far tiles in view)");
            cfg.SetForTest(cfg.SpyglassMaxBoost, 4f);

            // Lowered. "Back to normal" = the no-spyglass rule again, not the tile count of before: a tile the spyglass
            // split stay split while the camera is within SplitFactor * (1 + SplitHysteresis) tile widths (README,
            // SplitHysteresis: same as a tile the player walked away from). Counting leaves in the view cone against
            // the count of before was wrong: at the spawn point (world origin = corner of every quadtree level) that
            // band alone gave 0 -> 9 with nothing of the boost left.
            ViewBoost.TestSlot = null;
            yield return Kit.Frames(2);
            Kit.Coverage(mgr, cam.x, cam.z, 6000f, 128f, out var points, out var holes, out _, out var firstHole);
            c.Check(holes == 0, $"spyglass lowered: no hole in the far ground ({holes} of {points} points, first {firstHole})");
            var back = new Kit.Box();
            var past = 0;
            var pastFirst = "-";
            yield return Kit.WaitUntil(() =>
            {
                mgr.TestBoostLeft(out past, out _, out pastFirst);
                return past == 0;
            }, 2f, back);
            var lowered = mgr.CountLeavesInView(cam, forward, 8f, 600f);
            c.Check(back.Ok, "spyglass lowered: within 2 s no wanted far tile is split farther out than without a spyglass "
                             + $"({past} nodes past SplitFactor x (1 + SplitHysteresis) tile widths, first {pastFirst}; {four.PastReach} with it up at x4, {eight.PastReach} at x8)");
            c.Check(lowered < four.Leaves, $"spyglass lowered: fewer far tiles wanted in view than with it up ({four.Leaves} at x4, {eight.Leaves} at x8 -> {lowered}; {leaves} before)");
            var objectsBack = new Kit.Box();
            var objectsLeft = 0;
            var buildingsLeft = 0;
            yield return Kit.WaitUntil(() =>
            {
                var now = camera.transform.position;
                om.TestTilesBeyond(now, objectsBeyond, out objectsLeft, out _);
                om.TestTilesBeyond(now, piecesBeyond, out _, out buildingsLeft);
                return objectsLeft == 0 && buildingsLeft == 0;
            }, 2f, objectsBack);
            c.Check(objectsBack.Ok, $"spyglass lowered: within 2 s no far object tile is wanted beyond ObjectFarDistance and no buildings beyond PieceDistance ({objectsLeft} tiles, {buildingsLeft} with buildings)");
            // Drawn ground follow the wanted one: finer tiles leave when their wanted parent (built, it was the crack filler) take over.
            var drawn = new Kit.Box();
            var finer = 0;
            yield return Kit.WaitUntil(() =>
            {
                mgr.TestBoostLeft(out _, out finer, out _);
                return finer == 0;
            }, 3f, drawn);
            c.Check(drawn.Ok, $"spyglass lowered: 3 s later at most, no far tile finer than wanted is still drawn ({finer} such tiles, waited {drawn.Detail})");
            yield return new WaitForSecondsRealtime(1f);
            Kit.Coverage(mgr, cam.x, cam.z, 6000f, 128f, out points, out holes, out _, out firstHole);
            mgr.TestSurfaceCut(out var overlaps, out var notShown);
            c.Check(holes == 0 && overlaps == 0 && notShown == 0, $"…and a second later still no hole and nothing drawn twice ({holes} holes, {overlaps} overlaps, {notShown} not shown)");
            var finestLowered = FinestInCone(mgr, cam, forward, 8f, 2000f, 5000f);
            var finestNormal = FinestNormal(cfg, 2000f);
            c.Check(finestLowered >= finestNormal,
                $"…and the ground drawn 2 to 5 km out is no finer than the no-spyglass rule allows (level {finestLowered}, the rule allows level {finestNormal} or coarser; level {finestBoosted} with the spyglass up, {finest} before)");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"spyglass boost: {leaves} -> {four.Leaves} (x4) -> {eight.Leaves} (x8) far tiles in view, {lowered} when lowered (the no-spyglass rule again)");
        }
        finally
        {
            Kit.PutBack();
        }
    }
}
#endif
